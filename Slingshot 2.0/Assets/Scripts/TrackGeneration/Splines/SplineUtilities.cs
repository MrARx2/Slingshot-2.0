using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace TrackGeneration.Splines
{
    /// <summary>A sampled point along a spline with full orientation frame.</summary>
    [System.Serializable]
    public struct SplineFrame
    {
        [Tooltip("Normalized parameter along the spline (0-1)")]
        public float T;

        [Tooltip("World-space position on the spline")]
        public float3 Position;

        [Tooltip("Forward direction (tangent) at this point")]
        public float3 Tangent;

        [Tooltip("Up direction (surface normal, accounts for banking)")]
        public float3 Up;

        [Tooltip("Right direction (cross-track)")]
        public float3 Right;

        [Tooltip("Cumulative arc length from spline start to this point (meters)")]
        public float ArcLength;
    }

    /// <summary>
    /// Static utility class providing spline evaluation helpers for position, orientation,
    /// arc-length sampling, nearest-point queries, and curvature-based banking.
    /// Uses Rotation Minimizing Frames (Double Reflection) for stable mesh generation.
    /// </summary>
    public static class SplineUtilities
    {
        // Minimum tangent magnitude to avoid degenerate frames.
        private const float MinTangentMagnitude = 1e-6f;

        // Banking response scale ≈ v²/g for the hovercraft's cruise speed
        // (v ≈ 47 m/s → 47²/9.81 ≈ 225). Bank angle = atan(κ · Factor · multiplier),
        // which is the physically ideal bank for a corner of curvature κ at that speed.
        // Curvature is normalized PER METER, so the result is independent of sample count.
        public const float BankingSpeedFactor = 225f;

        /// <summary>
        /// Computes the signed bank angle (radians) from two tangents sampled
        /// <paramref name="arcDistance"/> meters apart. Negative = bank left.
        /// Single source of truth — the mesh builder's shortcut weld uses this too,
        /// so welded strips always lie in the plane of the banked road.
        /// </summary>
        public static float ComputeSignedBankAngle(float3 tanPrev, float3 tanNext, float3 right, float arcDistance, float bankingMultiplier, float maxBankAngleDeg)
        {
            if (arcDistance < 1e-4f || bankingMultiplier <= 0f) return 0f;

            // Lateral curvature per meter: horizontal turn rate, immune to pitch.
            float lateralCurvature = math.dot(tanNext - tanPrev, right) / arcDistance;

            float bank = math.atan(lateralCurvature * BankingSpeedFactor * bankingMultiplier) * -1f;
            float maxBank = math.radians(math.clamp(maxBankAngleDeg, 0f, 80f));
            return math.clamp(bank, -maxBank, maxBank);
        }

        // ──────────────────────────── Evaluate Frame ────────────────────────────

        /// <summary>
        /// Evaluates the spline at normalized parameter <paramref name="t"/> and outputs
        /// a full orthonormal orientation frame (position, tangent, up, right).
        /// Uses a robust reference-vector approach that handles vertical tangents.
        /// NOTE: For mesh generation, prefer <see cref="SampleEvenFrames"/> which uses
        /// Rotation Minimizing Frames for twist-free results.
        /// </summary>
        public static void EvaluateSplineFrame(
            SplineContainer container, float t,
            out float3 position, out float3 tangent, out float3 up, out float3 right)
        {
            t = math.saturate(t);

            container.Evaluate(0, t, out position, out float3 rawTangent, out float3 rawUp);

            // Normalize tangent with safe fallback to world forward.
            float tangentLen = math.length(rawTangent);
            tangent = tangentLen > MinTangentMagnitude
                ? rawTangent / tangentLen
                : new float3(0f, 0f, 1f);

            // Choose a reference "up" that isn't parallel to the tangent.
            // When the tangent is nearly vertical, use world-forward instead of world-up.
            float3 refUp = math.abs(math.dot(tangent, new float3(0f, 1f, 0f))) > 0.95f
                ? new float3(0f, 0f, 1f)
                : new float3(0f, 1f, 0f);

            right = math.normalizesafe(math.cross(refUp, tangent), new float3(1f, 0f, 0f));
            up = math.normalizesafe(math.cross(tangent, right), new float3(0f, 1f, 0f));
        }

        /// <summary>
        /// Like <see cref="EvaluateSplineFrame"/> but additionally applies the same
        /// curvature-based banking used by the mesh, so stunt actors (ramps, boost pads)
        /// sit flush on the banked road surface instead of poking through it.
        /// </summary>
        public static void EvaluateSplineFrameBanked(
            SplineContainer container, float t, float bankingMultiplier, float maxBankAngleDeg, float sampleArcDist,
            out float3 position, out float3 tangent, out float3 up, out float3 right)
        {
            EvaluateSplineFrame(container, t, out position, out tangent, out up, out right);

            if (bankingMultiplier <= 0f) return;

            float totalLength = GetSplineLength(container);
            if (totalLength <= 1e-3f) return;

            float dt = math.max(sampleArcDist, 1f) / totalLength;
            bool closed = container.Spline.Closed;

            float tPrev = t - dt;
            float tNext = t + dt;
            if (closed)
            {
                if (tPrev < 0f) tPrev += 1f;
                if (tNext > 1f) tNext -= 1f;
            }
            else
            {
                tPrev = math.saturate(tPrev);
                tNext = math.saturate(tNext);
            }

            EvaluateSplineFrame(container, tPrev, out _, out float3 tanPrev, out _, out _);
            EvaluateSplineFrame(container, tNext, out _, out float3 tanNext, out _, out _);

            float bank = ComputeSignedBankAngle(tanPrev, tanNext, right, sampleArcDist * 2f, bankingMultiplier, maxBankAngleDeg);
            quaternion bankRot = quaternion.AxisAngle(tangent, bank);
            up = math.rotate(bankRot, up);
            right = math.rotate(bankRot, right);
        }

        /// <summary>
        /// Returns the normalized t reached by traveling <paramref name="distance"/> meters
        /// along the spline from <paramref name="fromT"/> (wrap-aware for closed splines).
        /// Use this instead of adding distance/length to t — spline t is NOT arc-uniform.
        /// </summary>
        public static float GetTAtDistance(SplineContainer container, float fromT, float distance)
        {
            if (container == null || container.Spline == null || container.Spline.Count == 0)
                return fromT;

            SplineUtility.GetPointAtLinearDistance(container.Spline, fromT, distance, out float resultT);

            if (container.Spline.Closed)
            {
                if (resultT > 1f) resultT -= 1f;
                if (resultT < 0f) resultT += 1f;
            }
            else
            {
                resultT = math.saturate(resultT);
            }
            return resultT;
        }

        // ──────────────────────────── Spline Length ────────────────────────────

        /// <summary>
        /// Returns the total arc length of the first spline in the container (meters).
        /// </summary>
        public static float GetSplineLength(SplineContainer container)
        {
            if (container == null || container.Spline == null || container.Spline.Count == 0)
                return 0f;

            return container.Spline.GetLength();
        }

        // ──────────────────────────── Nearest T ────────────────────────────

        /// <summary>
        /// Finds the normalized t parameter of the nearest point on the spline to
        /// <paramref name="worldPos"/> using brute-force sampling followed by binary refinement.
        /// </summary>
        public static float GetNearestT(SplineContainer container, float3 worldPos, int resolution = 100)
        {
            if (container == null || container.Spline == null || container.Spline.Count == 0)
                return 0f;

            resolution = math.max(resolution, 4);

            // Phase 1: brute-force coarse search
            float bestT = 0f;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i <= resolution; i++)
            {
                float t = (float)i / resolution;
                container.Evaluate(0, t, out float3 pos, out _, out _);
                float distSq = math.distancesq(pos, worldPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestT = t;
                }
            }

            // Phase 2: binary refinement around bestT
            float step = 1f / resolution;
            float lo = math.max(0f, bestT - step);
            float hi = math.min(1f, bestT + step);

            const int refinementIterations = 16;
            for (int iter = 0; iter < refinementIterations; iter++)
            {
                float mid1 = math.lerp(lo, hi, 0.333f);
                float mid2 = math.lerp(lo, hi, 0.667f);

                container.Evaluate(0, mid1, out float3 p1, out _, out _);
                container.Evaluate(0, mid2, out float3 p2, out _, out _);

                float d1 = math.distancesq(p1, worldPos);
                float d2 = math.distancesq(p2, worldPos);

                if (d1 < d2)
                    hi = mid2;
                else
                    lo = mid1;
            }

            // Return midpoint of final bracket
            float finalT = (lo + hi) * 0.5f;

            container.Evaluate(0, finalT, out float3 finalPos, out _, out _);
            float finalDistSq = math.distancesq(finalPos, worldPos);
            return finalDistSq < bestDistSq ? finalT : bestT;
        }

        // ──────────────────────────── Even Sampling with RMF ────────────────────────────

        /// <summary>
        /// Samples <paramref name="count"/> evenly-spaced frames along the spline by arc length,
        /// using Rotation Minimizing Frames (Double Reflection method) for twist-free orientation.
        /// Banking is integrated directly: curvature tilts the frame by
        /// <c>atan(curvature × bankingMultiplier)</c> clamped to ±<paramref name="maxBankAngleDeg"/>.
        /// </summary>
        public static SplineFrame[] SampleEvenFrames(SplineContainer container, int count,
            float bankingMultiplier = 0f, float maxBankAngleDeg = 45f, Unity.Mathematics.float3? initialUp = null,
            Unity.Mathematics.float3? targetEndUp = null, float bankTransitionLength = 0f)
        {
            count = math.max(count, 2);

            if (container == null || container.Spline == null || container.Spline.Count == 0)
                return System.Array.Empty<SplineFrame>();

            float totalLength = GetSplineLength(container);
            if (totalLength <= 0f)
                return System.Array.Empty<SplineFrame>();

            bool isClosed = container.Spline.Closed;

            // ── Phase 1: Build arc-length → t lookup table ──
            const int lutResolution = 1024;
            float[] lutT = new float[lutResolution + 1];
            float[] lutArc = new float[lutResolution + 1];

            lutT[0] = 0f;
            lutArc[0] = 0f;

            container.Evaluate(0, 0f, out float3 prevLutPos, out _, out _);

            for (int i = 1; i <= lutResolution; i++)
            {
                float t = (float)i / lutResolution;
                container.Evaluate(0, t, out float3 pos, out _, out _);
                float segLen = math.length(pos - prevLutPos);
                lutT[i] = t;
                lutArc[i] = lutArc[i - 1] + segLen;
                prevLutPos = pos;
            }

            // Normalize arc lengths to match the analytical total length.
            float lutTotal = lutArc[lutResolution];
            if (lutTotal > MinTangentMagnitude)
            {
                float scale = totalLength / lutTotal;
                for (int i = 1; i <= lutResolution; i++)
                    lutArc[i] *= scale;
            }

            // ── Phase 2: Sample positions and tangents at even arc-length intervals ──
            float3[] positions = new float3[count];
            float3[] tangents = new float3[count];
            float[] sampleTs = new float[count];
            float[] arcLengths = new float[count];

            for (int s = 0; s < count; s++)
            {
                float targetArc = (totalLength * s) / (count - 1);
                targetArc = math.clamp(targetArc, 0f, totalLength);

                // Binary search in lutArc to find bracket.
                int lo = 0, hi = lutResolution;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (lutArc[mid] <= targetArc)
                        lo = mid;
                    else
                        hi = mid;
                }

                // Lerp between lutT[lo] and lutT[hi] to get precise t.
                float arcRange = lutArc[hi] - lutArc[lo];
                float frac = arcRange > MinTangentMagnitude
                    ? (targetArc - lutArc[lo]) / arcRange
                    : 0f;
                float sampleT = math.lerp(lutT[lo], lutT[hi], frac);
                sampleT = math.saturate(sampleT);

                container.Evaluate(0, sampleT, out float3 evalPos, out float3 rawTan, out _);

                float tanLen = math.length(rawTan);
                float3 normTan = tanLen > MinTangentMagnitude
                    ? rawTan / tanLen
                    : new float3(0f, 0f, 1f);

                positions[s] = evalPos;
                tangents[s] = normTan;
                sampleTs[s] = sampleT;
                arcLengths[s] = targetArc;
            }

            // ── Phase 3: Rotation Minimizing Frame propagation (Double Reflection) ──
            float3[] ups = new float3[count];
            float3[] rights = new float3[count];

            // Bootstrap first frame: choose a reference up that is NOT parallel to the tangent.
            {
                float3 t0 = tangents[0];
                float3 refUp;
                
                if (initialUp.HasValue)
                {
                    refUp = initialUp.Value;
                }
                else
                {
                    refUp = math.abs(math.dot(t0, new float3(0f, 1f, 0f))) > 0.95f
                        ? new float3(0f, 0f, 1f)   // tangent is nearly vertical, use world-forward
                        : new float3(0f, 1f, 0f);   // normal case, use world-up
                }

                rights[0] = math.normalizesafe(math.cross(refUp, t0), new float3(1f, 0f, 0f));
                ups[0] = math.normalizesafe(math.cross(t0, rights[0]), new float3(0f, 1f, 0f));
            }

            // Propagate frames using double reflection (Wang et al. 2008).
            for (int i = 0; i < count - 1; i++)
            {
                float3 v1 = positions[i + 1] - positions[i];
                float c1 = math.dot(v1, v1);

                if (c1 < 1e-10f)
                {
                    // Degenerate segment (duplicate positions): copy previous frame.
                    rights[i + 1] = rights[i];
                    ups[i + 1] = ups[i];
                    continue;
                }

                // First reflection: reflect right and tangent across the bisector plane.
                float3 rL = rights[i] - (2f / c1) * math.dot(v1, rights[i]) * v1;
                float3 tL = tangents[i] - (2f / c1) * math.dot(v1, tangents[i]) * v1;

                // Second reflection: align reflected tangent with the actual next tangent.
                float3 v2 = tangents[i + 1] - tL;
                float c2 = math.dot(v2, v2);

                if (c2 < 1e-10f)
                {
                    // Tangent didn't change: just use the first reflection.
                    rights[i + 1] = math.normalizesafe(rL, rights[i]);
                    ups[i + 1] = math.normalizesafe(math.cross(tangents[i + 1], rights[i + 1]), ups[i]);
                    continue;
                }

                rights[i + 1] = math.normalizesafe(rL - (2f / c2) * math.dot(v2, rL) * v2, rights[i]);
                ups[i + 1] = math.normalizesafe(math.cross(tangents[i + 1], rights[i + 1]), ups[i]);
            }

            // ── Phase 3b: Closed-loop twist correction ──
            // For a closed spline, the RMF may accumulate a small twist over the full loop.
            // Measure the angular discrepancy and distribute a linear correction.
            if (isClosed && count > 2)
            {
                // Measure twist: angle between ups[count-1] and ups[0] in the plane perpendicular to tangents[0].
                float3 lastUp = ups[count - 1];
                float3 firstUp = ups[0];
                float3 firstTan = tangents[0];

                // Project both ups onto the plane perpendicular to firstTan.
                float3 lastUpProj = math.normalizesafe(lastUp - math.dot(lastUp, firstTan) * firstTan, firstUp);
                float3 firstUpProj = math.normalizesafe(firstUp - math.dot(firstUp, firstTan) * firstTan, firstUp);

                float cosAngle = math.clamp(math.dot(lastUpProj, firstUpProj), -1f, 1f);
                float twistAngle = math.acos(cosAngle);

                // Determine twist direction.
                float crossSign = math.dot(math.cross(lastUpProj, firstUpProj), firstTan);
                if (crossSign < 0f) twistAngle = -twistAngle;

                // Distribute correction linearly across all frames.
                if (math.abs(twistAngle) > 0.001f)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float correction = twistAngle * ((float)i / (count - 1));
                        quaternion corrRot = quaternion.AxisAngle(tangents[i], correction);
                        ups[i] = math.rotate(corrRot, ups[i]);
                        rights[i] = math.rotate(corrRot, rights[i]);
                    }
                }
            }

            // ── Phase 3c: Open-spline end-up correction ──
            // For an open spline (e.g. a shortcut) whose end must line up with the main road's
            // banking, rotate the frames so the last frame's up matches the requested target.
            // Correction ramps 0 → full from start to end, so the start (pinned to initialUp) is
            // preserved while the merge end matches the main road — removing twist at the join.
            if (!isClosed && targetEndUp.HasValue && count > 2)
            {
                float3 endTan = tangents[count - 1];
                float3 desired = math.normalizesafe(
                    targetEndUp.Value - math.dot(targetEndUp.Value, endTan) * endTan, ups[count - 1]);
                float3 currentProj = math.normalizesafe(
                    ups[count - 1] - math.dot(ups[count - 1], endTan) * endTan, ups[count - 1]);

                float cosAngle = math.clamp(math.dot(currentProj, desired), -1f, 1f);
                float twistAngle = math.acos(cosAngle);
                float crossSign = math.dot(math.cross(currentProj, desired), endTan);
                if (crossSign < 0f) twistAngle = -twistAngle;

                // Safety clamp: a large required correction means the geometry is doing something
                // extreme — never distribute more than this, so the road can never corkscrew.
                twistAngle = math.clamp(twistAngle, -math.radians(30f), math.radians(30f));

                if (math.abs(twistAngle) > 0.001f)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float correction = twistAngle * ((float)i / (count - 1));
                        quaternion corrRot = quaternion.AxisAngle(tangents[i], correction);
                        ups[i] = math.rotate(corrRot, ups[i]);
                        rights[i] = math.rotate(corrRot, rights[i]);
                    }
                }
            }

            // ── Phase 4: Curvature-based banking (compute → smooth → apply) ──
            // Bank angles are computed for ALL frames first, then smoothed over a
            // physical transition window, and only then applied. Applying raw per-frame
            // banking produced jittery ring-to-ring roll changes — exactly the defective
            // surfaces that launch a physics hovercraft. Smoothing in angle-space gives
            // NASCAR-style eased entry/exit into banked corners.
            if (bankingMultiplier > 0f)
            {
                float spacing = totalLength / (count - 1);
                float[] bankAngles = new float[count];

                for (int i = 0; i < count; i++)
                {
                    int prev, next;
                    if (isClosed)
                    {
                        prev = (i - 1 + count) % count;
                        next = (i + 1) % count;
                    }
                    else
                    {
                        prev = math.max(i - 1, 0);
                        next = math.min(i + 1, count - 1);
                    }

                    float arcDist = spacing * (isClosed ? 2 : (next - prev));
                    bankAngles[i] = ComputeSignedBankAngle(tangents[prev], tangents[next], rights[i], arcDist, bankingMultiplier, maxBankAngleDeg);
                }

                // Box-blur the bank angles over the transition window (two passes ≈ smooth
                // triangular kernel). Radius in samples derived from meters, so the result
                // is identical regardless of mesh resolution.
                if (bankTransitionLength > 0.5f && count > 4)
                {
                    int radius = math.clamp((int)math.round(bankTransitionLength / math.max(spacing, 0.01f) * 0.5f), 1, count / 4);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        float[] smoothed = new float[count];
                        for (int i = 0; i < count; i++)
                        {
                            float sum = 0f;
                            int samples = 0;
                            for (int o = -radius; o <= radius; o++)
                            {
                                int idx = i + o;
                                if (isClosed)
                                {
                                    idx = (idx % count + count) % count;
                                }
                                else
                                {
                                    idx = math.clamp(idx, 0, count - 1);
                                }
                                sum += bankAngles[idx];
                                samples++;
                            }
                            smoothed[i] = sum / samples;
                        }
                        bankAngles = smoothed;
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    quaternion bankRot = quaternion.AxisAngle(tangents[i], bankAngles[i]);
                    ups[i] = math.rotate(bankRot, ups[i]);
                    rights[i] = math.rotate(bankRot, rights[i]);
                }
            }

            // ── Phase 5: Build output frames ──
            SplineFrame[] frames = new SplineFrame[count];
            for (int s = 0; s < count; s++)
            {
                frames[s] = new SplineFrame
                {
                    T = sampleTs[s],
                    Position = positions[s],
                    Tangent = tangents[s],
                    Up = ups[s],
                    Right = rights[s],
                    ArcLength = arcLengths[s]
                };
            }

            return frames;
        }

        // ──────────────────────────── Bank Angle (legacy) ────────────────────────────

        /// <summary>
        /// Estimates the curvature at parameter <paramref name="t"/> using finite differences
        /// and returns a banking angle in degrees.
        /// NOTE: For mesh generation, banking is now integrated into <see cref="SampleEvenFrames"/>.
        /// This method is retained for ad-hoc queries.
        /// </summary>
        public static float ComputeBankAngle(SplineContainer container, float t, float multiplier)
        {
            if (container == null || container.Spline == null || container.Spline.Count == 0)
                return 0f;

            const float dt = 0.001f;

            float tBefore = t - dt;
            float tAfter = t + dt;

            bool isClosed = container.Spline.Closed;
            if (isClosed)
            {
                if (tBefore < 0f) tBefore += 1f;
                if (tAfter > 1f) tAfter -= 1f;
            }
            else
            {
                tBefore = math.max(0f, tBefore);
                tAfter = math.min(1f, tAfter);
            }

            container.Evaluate(0, tBefore, out _, out float3 tanBefore, out _);
            container.Evaluate(0, tAfter, out _, out float3 tanAfter, out _);

            float3 tanBNorm = math.normalizesafe(tanBefore);
            float3 tanANorm = math.normalizesafe(tanAfter);

            float3 curvatureVec = (tanANorm - tanBNorm) / (2f * dt);
            float curvatureMag = math.length(curvatureVec);

            EvaluateSplineFrame(container, t, out _, out float3 tangent, out _, out float3 right);
            float sign = math.sign(math.dot(curvatureVec, right));
            if (sign == 0f) sign = 1f;

            float angleDeg = math.degrees(math.atan(curvatureMag * multiplier)) * sign;
            angleDeg = math.clamp(angleDeg, -60f, 60f);

            return angleDeg;
        }
    }
}
