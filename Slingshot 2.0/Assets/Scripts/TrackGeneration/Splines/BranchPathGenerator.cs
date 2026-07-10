using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using TrackGeneration.Core;

namespace TrackGeneration.Splines
{
    /// <summary>
    /// Generates branching splines (shortcuts) that fork off and merge back onto the main circuit.
    ///
    /// A shortcut is a CORNER CUT: it spans a single bounded stretch of the circuit (never a large
    /// bypass), and takes the inside line across that one corner. Its two ends are sampled directly
    /// FROM the main road (position, tangent, banking) so it is born on the road's inside edge
    /// pointing in the road's direction; a lead-in / lead-out run along the road edge for one merge
    /// distance so the departure and rejoin are tangential and flush (C1). The body is essentially
    /// the straight chord across the corner.
    ///
    /// The "inside" is taken from the road's actual turn direction over the span — NOT from the loop
    /// centroid — so it is correct regardless of loop shape. Spans that are near-straight (no real
    /// corner) or near-hairpin (would force a sharp reversal) are rejected. Plain C# class.
    /// </summary>
    public class BranchPathGenerator
    {
        private const float MinGroundClearance = 5f;

        // Small vertical offset so a shortcut sitting alongside the main road never z-fights it.
        private const float ZFightBump = 0.05f;

        // A span whose NET heading change exceeds this is skipped: the road doubles back on
        // itself (near-hairpin) and the fork ends would fold. Inflections/S-curves inside the
        // span are fine — the body follows the road at a same-side offset and cannot cross it.
        private const float MaxNetTurnDegrees = 100f;

        public List<TrackBranch> GenerateBranches(GameObject parent, SplineContainer mainCircuit, TrackConfig config, ref Unity.Mathematics.Random rng)
        {
            var branches = new List<TrackBranch>();
            if (mainCircuit == null || mainCircuit.Splines.Count == 0) return branches;

            int numShortcuts = rng.NextInt(config.MinShortcuts, config.MaxShortcuts + 1);
            if (numShortcuts <= 0) return branches;

            float mainLength = SplineUtilities.GetSplineLength(mainCircuit);
            if (mainLength <= 1f) return branches;

            // Alternative-path sized span, expressed in the circuit's T parameter. Shortcuts are
            // full alternative routes now, not single corner cuts, so spans run much longer.
            float minSpanArc = math.max(240f, config.ShortcutMergeDistance * 6f);
            float maxSpanArc = minSpanArc * 2.5f;
            float minSpanT = minSpanArc / mainLength;
            float maxSpanT = math.min(maxSpanArc / mainLength, 0.3f); // never more than ~a third of the lap
            if (maxSpanT < minSpanT) maxSpanT = minSpanT;

            // Primary distribution: aim for one shortcut per region, evenly spaced around the lap.
            for (int i = 0; i < numShortcuts; i++)
            {
                float regionBase = (float)i / numShortcuts;

                for (int r = 0; r < 24; r++)
                {
                    float startT = math.frac(regionBase + rng.NextFloat(0f, 0.6f) / numShortcuts);
                    float spanT = rng.NextFloat(minSpanT, maxSpanT);
                    float endT = startT + spanT;

                    if (endT >= 0.999f) continue;                        // keep off the start/finish seam
                    if (OverlapsExisting(branches, startT, endT)) continue; // one shortcut per corner

                    TrackBranch branch = GenerateSingleBranch(parent, mainCircuit, config, mainLength, startT, endT, ref rng, branches.Count + 1);
                    if (branch != null) { branches.Add(branch); break; }
                }
            }

            // Guarantee pass: if some regions were too straight, search the whole track for more
            // valid corners until we reach the requested count.
            int guard = 0;
            while (branches.Count < numShortcuts && guard < 500)
            {
                guard++;
                float startT = rng.NextFloat(0f, 1f);
                float spanT = rng.NextFloat(minSpanT, maxSpanT);
                float endT = startT + spanT;

                if (endT >= 0.999f) continue;
                if (OverlapsExisting(branches, startT, endT)) continue;

                TrackBranch branch = GenerateSingleBranch(parent, mainCircuit, config, mainLength, startT, endT, ref rng, branches.Count + 1);
                if (branch != null) branches.Add(branch);
            }

            if (branches.Count < config.MinShortcuts)
            {
                Debug.LogWarning($"[BranchPathGenerator] Generated {branches.Count} shortcut(s); requested minimum {config.MinShortcuts}. This seed's circuit may lack enough distinct corners.");
            }

            return branches;
        }

        // True if [startT,endT] overlaps (within a small gap) the main-road section of an existing shortcut.
        private static bool OverlapsExisting(List<TrackBranch> branches, float startT, float endT, float gap = 0.02f)
        {
            foreach (var b in branches)
            {
                if (startT <= b.MainEndT + gap && endT >= b.MainStartT - gap) return true;
            }
            return false;
        }

        private TrackBranch GenerateSingleBranch(GameObject parent, SplineContainer mainCircuit, TrackConfig config, float mainLength, float startT, float endT, ref Unity.Mathematics.Random rng, int branchIndex)
        {
            float span = endT - startT;
            float deltaT = math.min(config.ShortcutMergeDistance / mainLength, span * 0.3f);

            // Room for lead-in + body + lead-out.
            if (span < 2f * deltaT + 0.03f) return null;

            float tE0 = startT;              // peel-off
            float tE1 = startT + deltaT;     // end of lead-in
            float tE3 = endT - deltaT;       // start of lead-out
            float tE4 = endT;                // merge

            SplineUtilities.EvaluateSplineFrame(mainCircuit, tE0, out float3 pos0, out float3 tan0, out float3 up0, out float3 right0);
            SplineUtilities.EvaluateSplineFrame(mainCircuit, tE1, out float3 pos1, out float3 tan1, out float3 up1, out float3 right1);
            SplineUtilities.EvaluateSplineFrame(mainCircuit, tE3, out float3 pos3, out float3 tan3, out float3 up3, out float3 right3);
            SplineUtilities.EvaluateSplineFrame(mainCircuit, tE4, out float3 pos4, out float3 tan4, out float3 up4, out float3 right4);

            float3 d0 = math.normalizesafe(tan0);
            float3 d4 = math.normalizesafe(tan4);

            // ── Span survey: net horizontal turn (picks the side) and max local curvature (caps
            //    how far the body may offset before the offset path would fold into a cusp).
            //    Horizontal projection ignores hills. S-curves are allowed: the body follows the
            //    road at a SAME-SIDE offset, so it can never cross the road. ──
            float3 h0 = math.normalizesafe(new float3(d0.x, 0f, d0.z), new float3(0f, 0f, 1f));

            int surveySamples = 8;
            float prevAng = 0f;
            float finalAng = 0f;
            float maxAbsDelta = 0f;
            for (int s = 1; s <= surveySamples; s++)
            {
                float ts = math.lerp(startT, endT, (float)s / surveySamples);
                SplineUtilities.EvaluateSplineFrame(mainCircuit, ts, out _, out float3 tn, out _, out _);
                float3 htn = math.normalizesafe(new float3(tn.x, 0f, tn.z), h0);
                float crossY = h0.x * htn.z - h0.z * htn.x;                 // vertical component of h0 × htn
                float ang = math.degrees(math.atan2(crossY, math.dot(h0, htn))); // signed heading vs. start
                maxAbsDelta = math.max(maxAbsDelta, math.abs(ang - prevAng));
                prevAng = ang;
                finalAng = ang;
            }

            // Only near-hairpin spans are rejected — there the fork ends would fold back.
            float turnAngle = math.abs(finalAng);
            if (turnAngle > MaxNetTurnDegrees) return null;

            // Side = inside of the net turn (same sign convention as before: + = road's right).
            // On straightish spans either side works — pick one at random.
            float sideSign = turnAngle > 5f ? math.sign(finalAng) : (rng.NextFloat() < 0.5f ? 1f : -1f);
            if (sideSign == 0f) sideSign = 1f;

            // Curvature cap for the lateral wander: an offset curve develops a cusp when the
            // offset reaches the local curvature radius — stay well under it.
            float spanArc = span * mainLength;
            float segArc = spanArc / surveySamples;
            float maxKappa = math.radians(maxAbsDelta) / math.max(segArc, 1f);
            float wanderCap = maxKappa > 1e-5f ? 0.45f / maxKappa : 40f;

            // Lateral offsets. The BODY sits just outside the main road (inner edge overlaps by a
            // hair). The end knots (E0/E4) pull in to the main road's edge so that, combined with the
            // mesh's width-gore, the shortcut pinches to a point exactly ON the road edge — a clean
            // grow-from-edge fork with no abrupt end face and no floor gap.
            float mainHalf = config.MainRoadWidth * 0.5f;
            float scHalf   = config.ShortcutRoadWidth * 0.5f;
            float overlap  = math.min(0.5f, scHalf * 0.5f);
            float offsetBody = mainHalf + scHalf - overlap;
            float offsetTip  = mainHalf;

            float3 E0 = pos0 + right0 * (offsetTip  * sideSign) + up0 * ZFightBump;
            float3 E1 = pos1 + right1 * (offsetBody * sideSign) + up1 * ZFightBump;
            float3 E3 = pos3 + right3 * (offsetBody * sideSign) + up3 * ZFightBump;
            float3 E4 = pos4 + right4 * (offsetTip  * sideSign) + up4 * ZFightBump;

            // ── Body = an ALTERNATIVE PATH that follows the road's shape at a wandering
            //    same-side lateral offset, with its own gentle elevation profile. The offset
            //    bells up mid-span and returns to the road edge at the ends, so it flows out of
            //    and back into the circuit while genuinely separating in between. ──
            float wander = math.clamp(rng.NextFloat(18f, 34f), 6f, math.min(40f, wanderCap));
            float elevAmp = rng.NextFloat(-6f, 10f);
            float latSeed = rng.NextFloat(0f, math.PI);

            int anchorCount = math.clamp((int)math.round(spanArc / 120f), 1, 5);
            var points = new List<float3>(anchorCount + 4) { E0, E1 };
            for (int a = 0; a < anchorCount; a++)
            {
                float u = (a + 1f) / (anchorCount + 1f);
                float ta = math.lerp(tE1, tE3, u);
                SplineUtilities.EvaluateSplineFrame(mainCircuit, ta, out float3 ap, out _, out float3 au, out float3 ar);

                float bell = math.sin(math.PI * u);
                float jitter = 0.85f + 0.3f * math.sin(latSeed + u * 5.1f); // low-freq variation, no zigzag
                float lateral = offsetBody + wander * bell * jitter;

                float3 P = ap + ar * (lateral * sideSign) + au * ZFightBump;
                P.y = math.max(ap.y + elevAmp * bell, MinGroundClearance);
                points.Add(P);
            }
            points.Add(E3);
            points.Add(E4);

            // ── Tangents. The END SEGMENTS (E0→E1 lead-in, E3→E4 lead-out) must hug the road:
            // both their knots sit on road-frame offsets, so pin BOTH their tangents to the road
            // direction. Interior anchors get Catmull-Rom tangents from their neighbors. ──
            float3 d1 = math.normalizesafe(tan1);
            float3 d3 = math.normalizesafe(tan3);

            float3 T1 = d1 * (math.min(math.distance(E0, E1), math.distance(E1, points[2])) / 3f);
            float3 T3 = d3 * (math.min(math.distance(points[points.Count - 3], E3), math.distance(E3, E4)) / 3f);

            quaternion rot0 = quaternion.LookRotationSafe(d0, up0);
            quaternion rot4 = quaternion.LookRotationSafe(d4, up4);

            // BezierKnot tangents are in KNOT space: they get rotated by the knot's rotation.
            // The end knots carry a LookRotation already pointing local +Z along the road, so
            // their tangents must be plain local-forward vectors. Passing world-space vectors
            // here rotated them a second time — that bent the end segments into small loops /
            // hooks right at the junction tips. Mid knots use identity rotation, so world-space
            // tangents are correct there.
            float len0 = math.distance(E0, E1) / 3f;
            float len4 = math.distance(E3, E4) / 3f;

            Spline spline = new Spline(points.Count);
            spline.Add(new BezierKnot(E0, new float3(0f, 0f, -len0), new float3(0f, 0f, len0), rot0));
            spline.Add(new BezierKnot(E1, -T1, T1, quaternion.identity));
            for (int a = 0; a < anchorCount; a++)
            {
                int idx = 2 + a;
                float3 Tc = (points[idx + 1] - points[idx - 1]) / 6f; // Catmull-Rom
                spline.Add(new BezierKnot(points[idx], -Tc, Tc, quaternion.identity));
            }
            spline.Add(new BezierKnot(E3, -T3, T3, quaternion.identity));
            spline.Add(new BezierKnot(E4, new float3(0f, 0f, -len4), new float3(0f, 0f, len4), rot4));
            spline.Closed = false;

            GameObject branchObj = new GameObject($"ShortcutSpline_{branchIndex}");
            SplineContainer container = branchObj.AddComponent<SplineContainer>();
            container.Spline = spline;

            // ── Validity: the shortcut must clearly separate from the main road through its middle,
            //    otherwise it is a redundant parallel lane, not a real shortcut. Require the mid-point
            //    to stand off the main road by at least a full road-plus-shortcut width (a clear gap). ──
            {
                float minSepSq = math.pow(config.MainRoadWidth + config.ShortcutRoadWidth, 2f);
                float maxSepSq = 0f;
                for (int s = 1; s <= 3; s++)
                {
                    float tt = s / 4f; // 0.25, 0.5, 0.75
                    container.Evaluate(0, tt, out float3 sp, out _, out _);
                    float nT = SplineUtilities.GetNearestT(mainCircuit, sp);
                    mainCircuit.Evaluate(0, nT, out float3 mp, out _, out _);
                    float sepSq = math.lengthsq(new float2(sp.x - mp.x, sp.z - mp.z));
                    maxSepSq = math.max(maxSepSq, sepSq);
                }
                if (maxSepSq < minSepSq)
                {
                    if (Application.isPlaying) Object.Destroy(branchObj);
                    else Object.DestroyImmediate(branchObj);
                    return null;
                }
            }

            // ── Proximity rejection: reject only a shortcut that crosses an UNRELATED part of the
            //    main road at similar height. Closeness to the very corner being cut is expected and
            //    must NOT be treated as a collision — otherwise every corner-cut is rejected. ──
            float scLen = SplineUtilities.GetSplineLength(container);
            bool isSafe = true;
            int checkSamples = 24;
            float minSafeDistXZSq = math.pow(config.MainRoadWidth * 1.2f + config.ShortcutRoadWidth, 2f);
            float minClearanceY = 6f;
            float mergeFrac = (config.ShortcutMergeDistance * 1.3f) / math.max(scLen, 1f);

            for (int i = 0; i < checkSamples; i++)
            {
                float t = (float)i / (checkSamples - 1);
                if (t < mergeFrac || t > 1f - mergeFrac) continue;

                container.Evaluate(0, t, out float3 p, out _, out _);
                float nearestT = SplineUtilities.GetNearestT(mainCircuit, p);

                // If the nearest main-road point lies within the section this shortcut is cutting,
                // being close is expected — skip it. Only unrelated sections count as collisions.
                if (nearestT >= startT - 0.03f && nearestT <= endT + 0.03f) continue;

                mainCircuit.Evaluate(0, nearestT, out float3 mainP, out _, out _);
                float distXZSq = math.lengthsq(new float2(p.x - mainP.x, p.z - mainP.z));
                float distY = math.abs(p.y - mainP.y);
                if (distXZSq < minSafeDistXZSq && distY < minClearanceY) { isSafe = false; break; }
            }

            if (!isSafe)
            {
                if (Application.isPlaying) Object.Destroy(branchObj);
                else Object.DestroyImmediate(branchObj);
                return null;
            }

            branchObj.transform.SetParent(parent.transform, false);

            return new TrackBranch
            {
                Spline = container,
                Length = scLen,
                MainStartT = startT,
                MainEndT = endT,
                IsHardDifficulty = rng.NextFloat() > config.ShortcutDifficultyBias,
                StartSideSign = sideSign,
                EndSideSign = sideSign,
                StartUp = up0,
                EndUp = up4
            };
        }
    }
}
