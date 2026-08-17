using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Converts section-level ordinary elevation intent into a continuous, chain-level
    /// vertical field. The horizontal centreline is immutable. Protected authored
    /// features keep their geometry and form hard boundaries for the ordinary solve.
    /// </summary>
    public static class VerticalProfilePlanner
    {
        private const float WeldToleranceSqr = 0.25f;
        private const int SolverSamples = 1024;
        private const int ValidationSamples = 2048;

        private sealed class Chain
        {
            public readonly List<GeneratedTrackSection> Sections = new List<GeneratedTrackSection>();
        }

        private struct SegmentProfile
        {
            public float X0, X1, Y0, Y1;
            public float Theta0, Theta1, ThetaHold;
            public float RampLength;
            public float[] HeightTable;

            public float Length => X1 - X0;

            public void Evaluate(float x, out float y, out float theta, out float q, out float qPrime)
            {
                float local = Mathf.Clamp(x - X0, 0f, Length);
                float r = Mathf.Min(RampLength, Length * 0.499f);
                if (r < 0.001f)
                {
                    float t = Length > 0.001f ? local / Length : 0f;
                    theta = Mathf.Lerp(Theta0, Theta1, t);
                    q = Length > 0.001f ? (Theta1 - Theta0) / Length : 0f;
                    qPrime = 0f;
                    y = Mathf.Lerp(Y0, Y1, t);
                    return;
                }

                if (local < r)
                    Transition(Theta0, ThetaHold, local / r, r, out theta, out q, out qPrime);
                else if (local > Length - r)
                    Transition(ThetaHold, Theta1, (local - (Length - r)) / r, r,
                        out theta, out q, out qPrime);
                else
                {
                    theta = ThetaHold;
                    q = 0f;
                    qPrime = 0f;
                }

                if (HeightTable != null && HeightTable.Length > 1)
                {
                    float tableX = Mathf.Clamp01(local / Mathf.Max(0.001f, Length)) * (HeightTable.Length - 1);
                    int tableIndex = Mathf.Min(Mathf.FloorToInt(tableX), HeightTable.Length - 2);
                    y = Y0 + Mathf.Lerp(HeightTable[tableIndex], HeightTable[tableIndex + 1],
                        tableX - tableIndex);
                }
                else
                    y = Y0 + IntegrateHeight(this, local, SolverSamples);
                if (local >= Length - 0.0001f) y = Y1;
            }
        }

        private struct RingRef
        {
            public GeneratedTrackSection Section;
            public int Ring;
            public float X;
        }

        public static bool TryApply(List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg, out string failure)
        {
            failure = "";
            if (sections == null || sections.Count == 0) return true;

            foreach (Chain chain in BuildChains(sections))
            {
                if (chain.Sections.Count == 0) continue;
                if (!TryApplyChain(chain, cfg, out failure)) return false;
            }
            return true;
        }

        private static List<Chain> BuildChains(List<GeneratedTrackSection> sections)
        {
            var chains = new List<Chain>();
            Chain current = null;
            GeneratedTrackSection previous = null;

            foreach (GeneratedTrackSection section in sections)
            {
                bool eligible = IsEligible(section);
                bool welded = previous != null &&
                              !previous.OpenEnd && !section.OpenStart &&
                              (previous.EndFrame.Position - section.StartFrame.Position).sqrMagnitude <= WeldToleranceSqr &&
                              previous.RoadId == section.RoadId;

                if (!eligible || !welded)
                {
                    current = null;
                    if (eligible)
                    {
                        current = new Chain();
                        chains.Add(current);
                    }
                }
                if (eligible)
                {
                    if (current == null)
                    {
                        current = new Chain();
                        chains.Add(current);
                    }
                    current.Sections.Add(section);
                }
                previous = section;
            }
            return chains;
        }

        private static bool IsEligible(GeneratedTrackSection section)
        {
            if (section?.Definition == null || section.SubdivisionFrames == null ||
                section.SubdivisionFrames.Length < 2 || section.RoadId != 0 ||
                section.OpenStart || section.OpenEnd || section.IsEmptySpace) return false;

            TrackMacroSectionDefinition d = section.Definition;
            // Pattern membership alone does not make an ordinary road protected. A
            // long JumpApproach/CorkscrewApproach is often exactly the legal space a
            // neighbouring climb needs in order to become gradual. Its feature-facing
            // endpoint remains an exact pitch/elevation anchor because the authored
            // feature body below is not eligible. Explicit hill bodies remain authored.
            if (Mathf.Abs(d.HillHeight) > 0.001f) return false;

            switch (d.SectionType)
            {
                case TrackMacroSectionType.Straight:
                case TrackMacroSectionType.WideStraight:
                case TrackMacroSectionType.BoostStraight:
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                case TrackMacroSectionType.SCurve:
                case TrackMacroSectionType.Chicane:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryApplyChain(Chain chain, ResolvedTrackGenerationConfig cfg, out string failure)
        {
            failure = "";
            var rings = new List<RingRef>();
            var boundaryX = new float[chain.Sections.Count + 1];
            var boundaryY = new float[chain.Sections.Count + 1];
            float x = 0f;
            Vector3 previousPosition = default;
            bool hasPrevious = false;

            for (int s = 0; s < chain.Sections.Count; s++)
            {
                GeneratedTrackSection section = chain.Sections[s];
                TrackConnectionFrame[] frames = section.SubdivisionFrames;
                boundaryX[s] = x;
                boundaryY[s] = frames[0].Position.y;

                for (int i = 0; i < frames.Length; i++)
                {
                    Vector3 p = frames[i].Position;
                    if (hasPrevious)
                    {
                        Vector2 a = new Vector2(previousPosition.x, previousPosition.z);
                        Vector2 b = new Vector2(p.x, p.z);
                        if (i > 0 || (p - previousPosition).sqrMagnitude > WeldToleranceSqr)
                            x += Vector2.Distance(a, b);
                    }
                    if (i > 0 || !hasPrevious)
                        rings.Add(new RingRef { Section = section, Ring = i, X = x });
                    previousPosition = p;
                    hasPrevious = true;
                }
            }
            boundaryX[chain.Sections.Count] = x;
            boundaryY[chain.Sections.Count] = chain.Sections[chain.Sections.Count - 1].EndFrame.Position.y;

            if (x < 1f) return true;

            List<int> anchors = BuildAnchors(chain, boundaryY);
            var profiles = new List<SegmentProfile>(anchors.Count - 1);
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                int a = anchors[i];
                int b = anchors[i + 1];
                float theta0 = BoundaryPitch(chain, a);
                float theta1 = BoundaryPitch(chain, b);
                if (!TrySolveProfile(boundaryX[a], boundaryX[b], boundaryY[a], boundaryY[b],
                        theta0, theta1, cfg, out SegmentProfile profile, out string reason))
                {
                    failure = $"Vertical chain '{chain.Sections[0].Definition.DebugName}' → " +
                              $"'{chain.Sections[chain.Sections.Count - 1].Definition.DebugName}' is not feasible: {reason}";
                    return false;
                }
                profiles.Add(profile);
            }

            foreach (RingRef rr in rings)
            {
                SegmentProfile profile = ProfileAt(profiles, rr.X);
                profile.Evaluate(rr.X, out float y, out float theta, out float q, out float qPrime);

                TrackConnectionFrame f = rr.Section.SubdivisionFrames[rr.Ring];
                Vector3 planForward = SectionFrameBuilders.Flatten(f.Forward);
                Vector3 newForward = (planForward * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
                Quaternion transport = Quaternion.FromToRotation(f.Forward.normalized, newForward);
                Vector3 right = transport * f.Right;
                right = (right - Vector3.Dot(right, newForward) * newForward).normalized;
                if (right.sqrMagnitude < 1e-6f)
                    right = Vector3.Cross(Vector3.up, newForward).normalized;
                Vector3 up = Vector3.Cross(newForward, right).normalized;
                right = Vector3.Cross(up, newForward).normalized;

                f.Position = new Vector3(f.Position.x, y, f.Position.z);
                f.Forward = newForward;
                f.Right = right;
                f.Up = up;
                f.PitchAngle = theta * Mathf.Rad2Deg;
                f.VerticalCurvature = q * Mathf.Cos(theta);
                f.VerticalCurvatureRate = qPrime * Mathf.Cos(theta) * Mathf.Cos(theta) -
                                          q * q * Mathf.Sin(theta) * Mathf.Cos(theta);
                rr.Section.SubdivisionFrames[rr.Ring] = f;
            }

            // A physical boundary has one canonical sample. Copy it into both section
            // representations rather than calculating either side independently.
            for (int i = 0; i < chain.Sections.Count; i++)
            {
                GeneratedTrackSection section = chain.Sections[i];
                if (i > 0)
                    section.SubdivisionFrames[0] = chain.Sections[i - 1].SubdivisionFrames[
                        chain.Sections[i - 1].SubdivisionFrames.Length - 1];
                section.StartFrame = section.SubdivisionFrames[0];
                section.EndFrame = section.SubdivisionFrames[section.SubdivisionFrames.Length - 1];

                float delta = section.EndFrame.Position.y - section.StartFrame.Position.y;
                section.Definition.ElevationChange = delta;
                SectionConnectionContract contract = section.Definition.Contract;
                contract.ElevationDelta = delta;
                section.Definition.Contract = contract;
            }

            return true;
        }

        private static List<int> BuildAnchors(Chain chain, float[] boundaryY)
        {
            var anchors = new List<int> { 0 };
            int previousRunSign = 0;
            int previousRunEnd = 0;
            int currentRunStart = -1;

            for (int i = 0; i < chain.Sections.Count; i++)
            {
                float delta = boundaryY[i + 1] - boundaryY[i];
                int sign = Mathf.Abs(delta) < 0.01f ? 0 : (delta > 0f ? 1 : -1);
                if (sign == 0) continue;

                if (currentRunStart < 0)
                {
                    currentRunStart = i;
                    previousRunSign = sign;
                    previousRunEnd = i + 1;
                    continue;
                }

                if (sign != previousRunSign)
                {
                    AddAnchor(anchors, previousRunEnd);
                    if (i > previousRunEnd) AddAnchor(anchors, i);
                    currentRunStart = i;
                    previousRunSign = sign;
                }
                previousRunEnd = i + 1;
            }

            AddAnchor(anchors, chain.Sections.Count);
            return anchors;
        }

        private static void AddAnchor(List<int> anchors, int value)
        {
            if (anchors[anchors.Count - 1] != value) anchors.Add(value);
        }

        private static float BoundaryPitch(Chain chain, int boundary)
        {
            TrackConnectionFrame f = boundary == chain.Sections.Count
                ? chain.Sections[chain.Sections.Count - 1].EndFrame
                : chain.Sections[boundary].StartFrame;
            return Mathf.Asin(Mathf.Clamp(f.Forward.normalized.y, -1f, 1f));
        }

        private static bool TrySolveProfile(float x0, float x1, float y0, float y1,
            float theta0, float theta1, ResolvedTrackGenerationConfig cfg,
            out SegmentProfile profile, out string reason)
        {
            profile = default;
            reason = "";
            float length = x1 - x0;
            if (length < 1f)
            {
                reason = "the eligible span is shorter than 1 meter";
                return false;
            }

            float ramp = Mathf.Clamp(Mathf.Max(cfg.PitchTransitionLength, length * 0.2f),
                Mathf.Min(1f, length * 0.1f), length * 0.49f);
            float lo = -cfg.MaxDropAngle * Mathf.Deg2Rad;
            float hi = cfg.MaxClimbAngle * Mathf.Deg2Rad;
            float target = y1 - y0;

            var candidate = new SegmentProfile
            {
                X0 = x0, X1 = x1, Y0 = y0, Y1 = y1,
                Theta0 = theta0, Theta1 = theta1, RampLength = ramp
            };

            candidate.ThetaHold = lo;
            float lowRise = IntegrateHeight(candidate, length, SolverSamples);
            candidate.ThetaHold = hi;
            float highRise = IntegrateHeight(candidate, length, SolverSamples);
            if (target < lowRise - 0.05f || target > highRise + 0.05f)
            {
                reason = $"required rise {target:F1}m cannot fit in {length:F1}m inside the configured pitch limits";
                return false;
            }

            for (int i = 0; i < 48; i++)
            {
                float mid = (lo + hi) * 0.5f;
                candidate.ThetaHold = mid;
                float rise = IntegrateHeight(candidate, length, SolverSamples);
                if (rise < target) lo = mid; else hi = mid;
            }
            candidate.ThetaHold = (lo + hi) * 0.5f;

            float maxCurvature = Mathf.Max(0.0000001f,
                cfg.MaxCurvatureInducedG * Mathf.Max(0.1f, cfg.Gravity) /
                Mathf.Max(1f, cfg.DesignSpeedMps * cfg.DesignSpeedMps));

            // Lengthen both curvature pulses until the exact true-arc limits pass.
            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (Validate(candidate, maxCurvature, cfg.MaxVerticalCurvatureRate,
                        out float worstK, out float worstRate))
                {
                    candidate.HeightTable = BuildHeightTable(candidate, ValidationSamples);
                    profile = candidate;
                    return true;
                }

                float next = Mathf.Min(length * 0.49f, candidate.RampLength * 1.12f + 1f);
                if (next <= candidate.RampLength + 0.01f)
                {
                    reason = $"curvature {worstK:G4}rad/m or curvature-rate {worstRate:G4}rad/m2 exceeds the ordinary-road limits";
                    return false;
                }
                candidate.RampLength = next;

                // A changed phase split changes the elevation integral, so re-solve
                // the hold pitch rather than silently changing the requested height.
                lo = -cfg.MaxDropAngle * Mathf.Deg2Rad;
                hi = cfg.MaxClimbAngle * Mathf.Deg2Rad;
                for (int i = 0; i < 48; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    candidate.ThetaHold = mid;
                    float rise = IntegrateHeight(candidate, length, SolverSamples);
                    if (rise < target) lo = mid; else hi = mid;
                }
                candidate.ThetaHold = (lo + hi) * 0.5f;
            }

            reason = "the continuous profile did not converge";
            return false;
        }

        private static bool Validate(SegmentProfile p, float maxCurvature, float maxRate,
            out float worstCurvature, out float worstRate)
        {
            worstCurvature = 0f;
            worstRate = 0f;
            for (int i = 0; i <= ValidationSamples; i++)
            {
                float x = Mathf.Lerp(p.X0, p.X1, (float)i / ValidationSamples);
                p.Evaluate(x, out _, out float theta, out float q, out float qPrime);
                float c = Mathf.Cos(theta);
                float k = q * c;
                float rate = qPrime * c * c - q * q * Mathf.Sin(theta) * c;
                worstCurvature = Mathf.Max(worstCurvature, Mathf.Abs(k));
                worstRate = Mathf.Max(worstRate, Mathf.Abs(rate));
            }
            return worstCurvature <= maxCurvature + 1e-7f && worstRate <= maxRate + 1e-8f;
        }

        private static SegmentProfile ProfileAt(List<SegmentProfile> profiles, float x)
        {
            for (int i = 0; i < profiles.Count; i++)
                if (x <= profiles[i].X1 + 0.001f) return profiles[i];
            return profiles[profiles.Count - 1];
        }

        private static void Transition(float from, float to, float u, float length,
            out float theta, out float q, out float qPrime)
        {
            u = Mathf.Clamp01(u);
            float d = to - from;
            if (u <= 0.5f)
            {
                theta = from + d * (2f * u * u);
                q = 4f * d * u / length;
                qPrime = 4f * d / (length * length);
            }
            else
            {
                float v = 1f - u;
                theta = to - d * (2f * v * v);
                q = 4f * d * v / length;
                qPrime = -4f * d / (length * length);
            }
        }

        private static float IntegrateHeight(SegmentProfile p, float localLength, int samples)
        {
            if (localLength <= 0f) return 0f;
            int n = Mathf.Clamp(Mathf.CeilToInt(samples * localLength / Mathf.Max(1f, p.Length)), 8, samples);
            float dx = localLength / n;
            float sum = 0f;
            for (int i = 0; i <= n; i++)
            {
                float local = i * dx;
                EvaluateThetaOnly(p, local, out float theta);
                float weight = i == 0 || i == n ? 0.5f : 1f;
                sum += Mathf.Tan(theta) * weight;
            }
            return sum * dx;
        }

        private static float[] BuildHeightTable(SegmentProfile p, int samples)
        {
            int count = Mathf.Max(32, samples) + 1;
            var table = new float[count];
            float dx = p.Length / (count - 1);
            EvaluateThetaOnly(p, 0f, out float previousTheta);
            for (int i = 1; i < count; i++)
            {
                EvaluateThetaOnly(p, i * dx, out float theta);
                table[i] = table[i - 1] +
                           (Mathf.Tan(previousTheta) + Mathf.Tan(theta)) * 0.5f * dx;
                previousTheta = theta;
            }

            // The solver already hit the target within numerical precision. Rescale the
            // lookup accumulation by the tiny residual so the protected endpoint is exact.
            float wanted = p.Y1 - p.Y0;
            float actual = table[count - 1];
            if (Mathf.Abs(actual) > 1e-6f)
            {
                float scale = wanted / actual;
                for (int i = 1; i < count; i++) table[i] *= scale;
            }
            return table;
        }

        private static void EvaluateThetaOnly(SegmentProfile p, float local, out float theta)
        {
            float r = Mathf.Min(p.RampLength, p.Length * 0.499f);
            if (local < r)
                Transition(p.Theta0, p.ThetaHold, local / r, r, out theta, out _, out _);
            else if (local > p.Length - r)
                Transition(p.ThetaHold, p.Theta1, (local - (p.Length - r)) / r, r,
                    out theta, out _, out _);
            else
                theta = p.ThetaHold;
        }
    }
}
