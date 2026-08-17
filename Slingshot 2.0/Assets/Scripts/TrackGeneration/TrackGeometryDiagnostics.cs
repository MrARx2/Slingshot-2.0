using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration
{
    /// <summary>
    /// Read-only, mesh-independent measurements for the generated driving surface.
    /// This class never mutates frames, consumes random values, or participates in
    /// candidate selection. It exists so geometry limits can be tuned from evidence.
    /// </summary>
    public static class TrackGeometryDiagnostics
    {
        private const int HotspotCount = 16;

        private struct Sample
        {
            public GeneratedTrackSection Section;
            public int Ring;
            public TrackConnectionFrame Frame;
            public float Ds;
            public float PitchDeg;
            public float VerticalCurvature;
            public float VerticalCurvatureRate;
            public float HorizontalCurvature;
            public float TotalCurvature;
            public float WidthRate;
            public float LegacyWallHeightRate;
            public float WallLateralRate;
            public float WallVerticalRate;
            public float WallCombinedRate;
            public float WallRateAcceleration;
        }

        private struct Ranked
        {
            public float Value;
            public Sample Sample;
        }

        public static void AppendBaseline(StringBuilder sb, List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            if (sb == null || sections == null || cfg == null || cfg.RoadProfile == null) return;

            sb.AppendLine("════════ GEOMETRY BASELINE (READ ONLY) ════════");
            sb.AppendLine("No value in this block influences generation. Curvature sampling is based on built frame geometry, not stored curvature metadata.");
            sb.AppendLine($"Reference speeds: min {cfg.DesignSpeedMps * 0.75f:F1}, nominal {cfg.DesignSpeedMps:F1}, max {cfg.DesignSpeedMps * 1.25f:F1} m/s");
            sb.AppendLine();

            var samples = Measure(sections, cfg.RoadProfile);
            AppendRanked(sb, "VERTICAL CURVATURE |dPitch/ds|", samples,
                s => Mathf.Abs(s.VerticalCurvature), "rad/m");
            AppendRanked(sb, "VERTICAL CURVATURE RATE |dK/ds|", samples,
                s => Mathf.Abs(s.VerticalCurvatureRate), "rad/m2");
            AppendRanked(sb, "FULL 3D TANGENT CURVATURE", samples,
                s => s.TotalCurvature, "rad/m");
            AppendRanked(sb, "ROAD WIDTH RATE", samples,
                s => Mathf.Abs(s.WidthRate), "m/m");
            AppendRanked(sb, "LEGACY SCALAR WALL-HEIGHT RATE", samples,
                s => Mathf.Abs(s.LegacyWallHeightRate), "m/m");
            AppendRanked(sb, "TRANSPORTED WALL-TOP LATERAL RATE", samples,
                s => Mathf.Abs(s.WallLateralRate), "m/m");
            AppendRanked(sb, "TRANSPORTED WALL-TOP VERTICAL RATE", samples,
                s => Mathf.Abs(s.WallVerticalRate), "m/m");
            AppendRanked(sb, "TRANSPORTED WALL-TOP COMBINED RATE", samples,
                s => s.WallCombinedRate, "m/m");
            AppendRanked(sb, "TRANSPORTED WALL-TOP RATE ACCELERATION", samples,
                s => s.WallRateAcceleration, "1/m");

            AppendLoadSummary(sb, samples, cfg);
            AppendJumpSummary(sb, sections, cfg);
        }

        private static List<Sample> Measure(List<GeneratedTrackSection> sections,
            TrackRoadProfileSettings profile)
        {
            var output = new List<Sample>(8192);
            int pointCount = TrackCrossSection.PointCount(profile);
            var crossA = new Vector2[Mathf.Max(2, pointCount)];
            var crossB = new Vector2[Mathf.Max(2, pointCount)];
            GeneratedTrackSection previousSection = null;
            float previousVerticalCurvature = 0f;
            float previousWallCombined = 0f;
            bool hasPreviousDerivative = false;

            foreach (var section in sections)
            {
                var frames = section?.SubdivisionFrames;
                if (frames == null || frames.Length < 2)
                {
                    previousSection = null;
                    hasPreviousDerivative = false;
                    continue;
                }

                bool continuesPhysicalChain = previousSection != null &&
                                              previousSection.RoadId == section.RoadId &&
                                              !previousSection.OpenEnd && !section.OpenStart &&
                                              (previousSection.EndFrame.Position - section.StartFrame.Position).sqrMagnitude <= 0.25f;
                if (!continuesPhysicalChain)
                    hasPreviousDerivative = false;

                for (int i = 1; i < frames.Length; i++)
                {
                    TrackConnectionFrame a = frames[i - 1];
                    TrackConnectionFrame b = frames[i];
                    float ds = Mathf.Max(1e-4f, Vector3.Distance(a.Position, b.Position));
                    float pitchA = PitchDegrees(a.Forward);
                    float pitchB = PitchDegrees(b.Forward);
                    float verticalCurvature = Mathf.DeltaAngle(pitchA, pitchB) * Mathf.Deg2Rad / ds;

                    Vector3 flatA = Vector3.ProjectOnPlane(a.Forward, Vector3.up);
                    Vector3 flatB = Vector3.ProjectOnPlane(b.Forward, Vector3.up);
                    float horizontalCurvature = 0f;
                    if (flatA.sqrMagnitude > 1e-8f && flatB.sqrMagnitude > 1e-8f)
                        horizontalCurvature = Vector3.SignedAngle(flatA, flatB, Vector3.up) * Mathf.Deg2Rad / ds;

                    float totalCurvature = Vector3.Angle(a.Forward, b.Forward) * Mathf.Deg2Rad / ds;

                    TrackCrossSection.Evaluate(profile, a, crossA);
                    TrackCrossSection.Evaluate(profile, b, crossB);
                    Vector2 leftA = crossA[0];
                    Vector2 rightA = crossA[pointCount - 1];
                    Vector2 leftB = crossB[0];
                    Vector2 rightB = crossB[pointCount - 1];

                    // Compare wall-tip offsets after parallel-transporting the previous
                    // ring into the current tangent frame. This removes centreline bend
                    // from the measurement while retaining real roll, width and wall-
                    // height changes — the quantities that create a visible wall wave.
                    Quaternion transport = Quaternion.FromToRotation(a.Forward, b.Forward);
                    Vector3 leftPrevious = transport * (a.Right * leftA.x + a.Up * leftA.y);
                    Vector3 rightPrevious = transport * (a.Right * rightA.x + a.Up * rightA.y);
                    Vector3 leftCurrent = b.Right * leftB.x + b.Up * leftB.y;
                    Vector3 rightCurrent = b.Right * rightB.x + b.Up * rightB.y;
                    Vector3 leftDelta = leftCurrent - leftPrevious;
                    Vector3 rightDelta = rightCurrent - rightPrevious;
                    float leftLateral = Mathf.Abs(Vector3.Dot(leftDelta, b.Right)) / ds;
                    float rightLateral = Mathf.Abs(Vector3.Dot(rightDelta, b.Right)) / ds;
                    float leftVertical = Mathf.Abs(Vector3.Dot(leftDelta, b.Up)) / ds;
                    float rightVertical = Mathf.Abs(Vector3.Dot(rightDelta, b.Up)) / ds;
                    float lateralRate = Mathf.Max(leftLateral, rightLateral);
                    float verticalRate = Mathf.Max(leftVertical, rightVertical);
                    float combinedRate = Mathf.Max(
                        leftDelta.magnitude, rightDelta.magnitude) / ds;
                    float wallHeightA = Mathf.Max(leftA.y, rightA.y);
                    float wallHeightB = Mathf.Max(leftB.y, rightB.y);

                    var sample = new Sample
                    {
                        Section = section,
                        Ring = i,
                        Frame = b,
                        Ds = ds,
                        PitchDeg = pitchB,
                        VerticalCurvature = verticalCurvature,
                        VerticalCurvatureRate = hasPreviousDerivative
                            ? (verticalCurvature - previousVerticalCurvature) / ds
                            : 0f,
                        HorizontalCurvature = horizontalCurvature,
                        TotalCurvature = totalCurvature,
                        WidthRate = (b.Width - a.Width) / ds,
                        LegacyWallHeightRate = (wallHeightB - wallHeightA) / ds,
                        WallLateralRate = lateralRate,
                        WallVerticalRate = verticalRate,
                        WallCombinedRate = combinedRate,
                        WallRateAcceleration = hasPreviousDerivative
                            ? Mathf.Abs(combinedRate - previousWallCombined) / ds
                            : 0f
                    };
                    output.Add(sample);

                    previousVerticalCurvature = verticalCurvature;
                    previousWallCombined = combinedRate;
                    hasPreviousDerivative = true;
                }

                previousSection = section;
            }

            return output;
        }

        private static void AppendRanked(StringBuilder sb, string title, List<Sample> samples,
            Func<Sample, float> selector, string unit)
        {
            var ranked = new List<Ranked>(samples.Count);
            foreach (var sample in samples)
            {
                float value = selector(sample);
                if (!float.IsNaN(value) && !float.IsInfinity(value))
                    ranked.Add(new Ranked { Value = value, Sample = sample });
            }
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            sb.AppendLine($"── {title} (top {Mathf.Min(HotspotCount, ranked.Count)}) ──");
            for (int i = 0; i < Mathf.Min(HotspotCount, ranked.Count); i++)
            {
                Ranked row = ranked[i];
                var d = row.Sample.Section.Definition;
                sb.AppendLine($"#{i + 1}: {row.Value:G6} {unit} at [{row.Sample.Section.SectionIndex:D3}] " +
                              $"{d.DebugName} ring {row.Sample.Ring} arc {row.Sample.Frame.ArcLength:F1}m " +
                              $"category {Category(d.SectionType)} pitch {row.Sample.PitchDeg:F2}deg");
            }
            sb.AppendLine();
        }

        private static void AppendLoadSummary(StringBuilder sb, List<Sample> samples,
            ResolvedTrackGenerationConfig cfg)
        {
            float[] speeds = { cfg.DesignSpeedMps * 0.75f, cfg.DesignSpeedMps, cfg.DesignSpeedMps * 1.25f };
            sb.AppendLine("── CURVATURE-INDUCED LOAD SUMMARY ──");
            foreach (float speed in speeds)
            {
                float worstVertical = 0f;
                float worstTotal = 0f;
                Sample worstSample = default;
                foreach (var sample in samples)
                {
                    float induced = speed * speed * Mathf.Abs(sample.VerticalCurvature) /
                                    Mathf.Max(0.1f, cfg.Gravity);
                    float total = speed * speed * sample.TotalCurvature /
                                  Mathf.Max(0.1f, cfg.Gravity);
                    if (induced > worstVertical)
                    {
                        worstVertical = induced;
                        worstSample = sample;
                    }
                    worstTotal = Mathf.Max(worstTotal, total);
                }
                string subject = worstSample.Section?.Definition?.DebugName ?? "none";
                sb.AppendLine($"{speed:F1}m/s: vertical {worstVertical:F2}G at {subject}; full-3D tangent load {worstTotal:F2}G");
            }
            sb.AppendLine();
        }

        private static void AppendJumpSummary(StringBuilder sb, List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            sb.AppendLine("── AIR-GAP BASELINE ──");
            bool any = false;
            foreach (var section in sections)
            {
                var d = section?.Definition;
                if (d == null || d.SectionType != TrackMacroSectionType.AirGap) continue;
                any = true;

                float speed = Mathf.Max(0.1f, cfg.DesignSpeedMps);
                float gravity = Mathf.Max(0.1f, cfg.Gravity);
                float launchPitch = PitchDegrees(section.StartFrame.Forward);
                float vy0 = speed * Mathf.Sin(launchPitch * Mathf.Deg2Rad);
                float timeToApex = d.JumpTimeToApex > 0f
                    ? d.JumpTimeToApex
                    : Mathf.Max(0f, vy0 / gravity);
                float apex = d.JumpApexHeight > 0f
                    ? d.JumpApexHeight
                    : (vy0 > 0f ? vy0 * vy0 / (2f * gravity) : 0f);
                float t = Mathf.Max(0f, d.AirtimeSeconds);
                float landingVy = vy0 - gravity * t;
                float arrivalPitch = Mathf.Atan2(landingVy,
                    speed * Mathf.Cos(launchPitch * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
                float tangentMismatch = Mathf.Abs(Mathf.DeltaAngle(arrivalPitch,
                    PitchDegrees(section.EndFrame.Forward)));
                float apexFraction = t > 1e-4f ? timeToApex / t : 0f;

                sb.AppendLine($"[{section.SectionIndex:D3}] {d.DebugName}: pitch {launchPitch:F2}deg, " +
                              $"airtime {t:F2}s, apex +{apex:F1}m at t={timeToApex:F2}s " +
                              $"(gap fraction {apexFraction:F2}), landingVy {landingVy:F1}m/s, " +
                              $"arrival mismatch {tangentMismatch:F2}deg, capture min/nom/max " +
                              $"{Flag(d.JumpCapturesMinimumSpeed)}/{Flag(d.JumpCapturesNominalSpeed)}/{Flag(d.JumpCapturesMaximumSpeed)}");
            }
            if (!any) sb.AppendLine("no air gaps");
            sb.AppendLine();
        }

        private static float PitchDegrees(Vector3 forward)
            => Mathf.Asin(Mathf.Clamp(forward.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

        private static string Flag(bool value) => value ? "PASS" : "FAIL";

        private static string Category(TrackMacroSectionType type)
        {
            switch (type)
            {
                case TrackMacroSectionType.WallrideTurn: return "active-wallride";
                case TrackMacroSectionType.Loop:
                case TrackMacroSectionType.Corkscrew:
                case TrackMacroSectionType.HalfLoopTwist:
                case TrackMacroSectionType.RotationalEvent:
                case TrackMacroSectionType.Spiral: return "inversion/rotational";
                case TrackMacroSectionType.JumpRamp:
                case TrackMacroSectionType.AirGap:
                case TrackMacroSectionType.LandingRamp: return "jump";
                case TrackMacroSectionType.RecoveryStraight: return "recovery";
                default: return "ordinary";
            }
        }
    }
}
