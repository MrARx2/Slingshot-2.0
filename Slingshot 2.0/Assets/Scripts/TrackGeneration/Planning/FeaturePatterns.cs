using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Space/connection requirements one pattern instance declares before planning.</summary>
    public class FeaturePatternRequirements
    {
        public float EstimatedLength;
        public float NetHeadingDeltaDegrees;   // 0 for heading-neutral patterns, ±180 for half-loop family
        public bool ClosureCompatible = true;
    }

    /// <summary>
    /// One atomic feature pattern: approach, internal elements and recovery are planned
    /// together and validated as ONE group — ordinary feature spacing does not apply
    /// between its internal elements. Compound patterns (loop → corkscrew, …) are
    /// intentionally constructed here, never assembled from coincidental random rolls.
    /// </summary>
    public interface ITrackFeaturePattern
    {
        TrackPatternType PatternType { get; }

        FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg);

        /// <summary>
        /// Plans the pattern's section definitions into <paramref name="output"/>.
        /// Deterministic via the seeded rng. Returns false when no legal parameterization
        /// exists under the resolved config (the caller records a structured failure).
        /// </summary>
        bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output);
    }

    /// <summary>Shared definition factory used by patterns and the planner.</summary>
    public static class SectionDefs
    {
        public static TrackMacroSectionDefinition Straight(TrackMacroSectionType type, float length, float width,
            string name, bool locked = false, string patternId = null)
        {
            return new TrackMacroSectionDefinition
            {
                SectionType = type,
                Length = length,
                Width = width,
                Direction = SectionTurnDirection.None,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Safe,
                LockLength = locked,
                DebugName = name,
                PatternId = patternId,
                Contract = SectionConnectionContract.Level()
            };
        }

        /// <summary>Physically recommended bank for a corner at design speed, scaled by banking strength.</summary>
        public static float RecommendedBank(ResolvedTrackGenerationConfig cfg, float radius)
        {
            float v = cfg.DesignSpeedMps;
            float ideal = Mathf.Atan2(v * v, cfg.Gravity * Mathf.Max(1f, radius)) * Mathf.Rad2Deg;
            return Mathf.Min(ideal, cfg.MaxBankAngle) * cfg.BankingStrength;
        }
    }

    // ═══════════════════════════ Ballistic jump solver ═══════════════════════════

    /// <summary>
    /// Solves jump geometry from the ballistic trajectory at design speed: launch
    /// transition, launch pitch, airtime, gap distance, arrival pitch and a landing
    /// surface that matches the predicted arrival — never a random gap length.
    /// </summary>
    public static class JumpBallistics
    {
        public struct Solution
        {
            public float LaunchLength;
            public float LaunchPitchDeg;     // ballistic pitch at the lip
            public float ClimbPitchDeg;      // monotonic intermediate shaping pitch
            public float LipHeight;
            public float LaunchHorizontal;

            public float AirtimeSeconds;
            public float GapHorizontal;
            public float GapRise;            // signed height change lip → landing mouth
            public float FlightLength;
            public float ArrivalPitchDeg;    // negative = descending

            public float LandingLength;
            public float LandingDescentPitchDeg;
            public float LandingHeight;      // landing mouth height above grade
            public float LandingHorizontal;
        }

        public static bool TrySolve(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng, out Solution s,
            float minAirtimeSeconds = -1f)
        {
            s = default;
            float v = cfg.DesignSpeedMps;
            float g = Mathf.Max(0.1f, cfg.Gravity);

            // Callers with a hard airtime need (mid-air lane aim) raise the draw floor
            // instead of gambling on a high roll.
            float tFloor = Mathf.Max(cfg.MinJumpAirtimeSeconds, minAirtimeSeconds);
            if (tFloor > cfg.MaxJumpAirtimeSeconds) return false;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float t = Mathf.Lerp(tFloor, cfg.MaxJumpAirtimeSeconds, rng.NextFloat());

                // Descent fraction k: arrival vertical speed = -g·t·k, and the flight's
                // net rise is g·t²·(0.5-k). The monotonic launch removed the old
                // mid-ramp height hump. Keep k below 0.5 so the catch can sit above the
                // lip and retain enough elevation for its long transition; k > 0 still
                // guarantees that the craft arrives while descending.
                float k = rng.NextFloat(0.18f, 0.45f);

                float vy0 = g * t * (1f - k);
                float sinLaunch = vy0 / v;
                if (sinLaunch > Mathf.Sin(6f * Mathf.Deg2Rad)) continue; // absurd pitch for this speed

                float launchPitch = Mathf.Asin(sinLaunch) * Mathf.Rad2Deg;
                // The old ramp climbed at 5..9 degrees and then pitched DOWN to the
                // ballistic launch angle at the open lip. That encoded the visible
                // pre-jump flattening. Keep the intermediate key below the launch
                // pitch so the surface continues pitching upward all the way out.
                float climbPitch = launchPitch * rng.NextFloat(0.45f, 0.8f);
                float launchLength = Mathf.Lerp(cfg.MinLaunchTransitionLength, cfg.MaxLaunchTransitionLength, rng.NextFloat());

                var launchKeys = SectionFrameBuilders.LaunchRampKeys(climbPitch, launchPitch);
                SectionFrameBuilders.KeyframedPitchSpan(launchKeys, launchLength, out float launchHoriz, out float lipHeight);

                // Scale the launch length so the lip height lands inside the legal window.
                if (lipHeight < cfg.MinJumpHeight || lipHeight > cfg.MaxJumpHeight)
                {
                    float target = Mathf.Clamp(lipHeight, cfg.MinJumpHeight, cfg.MaxJumpHeight);
                    launchLength *= target / Mathf.Max(0.01f, lipHeight);
                    if (launchLength < cfg.MinLaunchTransitionLength || launchLength > cfg.MaxLaunchTransitionLength)
                        continue;
                    SectionFrameBuilders.KeyframedPitchSpan(launchKeys, launchLength, out launchHoriz, out lipHeight);
                }

                float gapRise = vy0 * t - 0.5f * g * t * t;
                float landingHeight = lipHeight + gapRise;
                if (landingHeight < 1.5f) continue;

                float vx = v * Mathf.Cos(launchPitch * Mathf.Deg2Rad);
                float vyArr = -g * t * k;
                float arrivalPitch = Mathf.Atan2(vyArr, vx) * Mathf.Rad2Deg;
                float gapHoriz = vx * t;

                // Landing length: the eased descent profile's vertical span is linear in
                // length — pick the descent pitch that fits the transition window.
                bool landed = false;
                // High-speed ramps scale their legal length with design speed. A fixed
                // 4.5-degree minimum made otherwise valid, low-rise landings too short
                // at 1300+ km/h, turning required jumps into a seed lottery. Search the
                // gentle end as well; the same length bounds still reject impractically
                // long landings and the surface continues to match the arrival tangent.
                for (float descent = 0.5f; descent <= 12.5f; descent += 0.5f)
                {
                    var landingKeys = SectionFrameBuilders.LandingRampKeys(arrivalPitch, descent);
                    SectionFrameBuilders.KeyframedPitchSpan(landingKeys, 1f, out _, out float unitVert);
                    if (unitVert >= -0.0001f) continue;

                    float landingLength = landingHeight / -unitVert;
                    if (landingLength < cfg.MinLandingTransitionLength || landingLength > cfg.MaxLandingTransitionLength)
                        continue;

                    SectionFrameBuilders.KeyframedPitchSpan(landingKeys, landingLength, out float landHoriz, out _);

                    s = new Solution
                    {
                        LaunchLength = launchLength,
                        LaunchPitchDeg = launchPitch,
                        ClimbPitchDeg = climbPitch,
                        LipHeight = lipHeight,
                        LaunchHorizontal = launchHoriz,
                        AirtimeSeconds = t,
                        GapHorizontal = gapHoriz,
                        GapRise = gapRise,
                        FlightLength = Mathf.Sqrt(gapHoriz * gapHoriz + gapRise * gapRise),
                        ArrivalPitchDeg = arrivalPitch,
                        LandingLength = landingLength,
                        LandingDescentPitchDeg = descent,
                        LandingHeight = landingHeight,
                        LandingHorizontal = landHoriz
                    };
                    landed = true;
                    break;
                }

                if (landed) return true;
            }

            return false;
        }
    }

    // ═══════════════════════════ Pattern implementations ═══════════════════════════

    /// <summary>Jump group: approach → launch → air gap → landing → recovery.</summary>
    public class JumpGapPattern : ITrackFeaturePattern
    {
        public virtual TrackPatternType PatternType => TrackPatternType.JumpGap;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public virtual bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            if (!JumpBallistics.TrySolve(cfg, ref rng, out var s)) return false;

            var approach = SectionDefs.Straight(TrackMacroSectionType.BoostStraight, cfg.JumpApproachLength, cfg.RoadWidth,
                "JumpApproach", locked: true, patternId);
            approach.AllowsBoost = true;
            approach.SpeedIntent = SectionSpeedIntent.FullThrottle;
            output.Add(approach);

            EmitJumpChain(in s, cfg.RoadWidth, patternId, "", output);

            AddRecoveryAndTail(cfg, patternId, output);
            return true;
        }

        /// <summary>
        /// Emits the ramp → air gap → landing chain for a solved ballistic jump.
        /// Shared by ordinary jump patterns and the jump-gated alternate route zones.
        /// </summary>
        public static void EmitJumpChain(in JumpBallistics.Solution s, float width, string patternId,
            string namePrefix, List<TrackMacroSectionDefinition> output)
        {
            EmitJumpRamp(in s, width, patternId, namePrefix, output);
            EmitAirGap(in s, width, patternId, namePrefix, output);
            EmitLandingRamp(in s, width, patternId, namePrefix, output);
        }

        /// <summary>The launch ramp def of a solved jump.</summary>
        public static void EmitJumpRamp(in JumpBallistics.Solution s, float width, string patternId,
            string namePrefix, List<TrackMacroSectionDefinition> output)
        {
            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.JumpRamp,
                Length = s.LaunchLength,
                Width = width,
                ElevationChange = s.LipHeight,
                PitchChange = s.LaunchPitchDeg,
                SecondaryPitchDeg = s.ClimbPitchDeg,
                PlanHorizontalLength = s.LaunchHorizontal,
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Risky,
                AllowsJump = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"{namePrefix}JumpRamp_H{s.LipHeight:F0}m_{s.LaunchPitchDeg:F1}deg",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.VerticalAscending,
                    PitchDeltaDegrees = s.LaunchPitchDeg,
                    ElevationDelta = s.LipHeight,
                    ClosureCompatible = false
                }
            });
        }

        /// <summary>The ballistic flight def of a solved jump (no geometry — the hole itself).</summary>
        public static void EmitAirGap(in JumpBallistics.Solution s, float width, string patternId,
            string namePrefix, List<TrackMacroSectionDefinition> output)
        {
            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.AirGap,
                Length = s.FlightLength,
                Width = width,
                ElevationChange = s.GapRise,
                PitchChange = s.ArrivalPitchDeg,
                AirtimeSeconds = s.AirtimeSeconds,
                PlanHorizontalLength = s.GapHorizontal,
                RiskLevel = SectionRiskLevel.Extreme,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"{namePrefix}AirGap_{s.GapHorizontal:F0}m_{s.AirtimeSeconds:F2}s",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.VerticalAscending,
                    ExitOrientation = TrackOrientationTag.VerticalDescending,
                    ElevationDelta = s.GapRise,
                    ClosureCompatible = false
                }
            });
        }

        /// <summary>The landing ramp def of a solved jump.</summary>
        public static void EmitLandingRamp(in JumpBallistics.Solution s, float width, string patternId,
            string namePrefix, List<TrackMacroSectionDefinition> output)
        {
            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.LandingRamp,
                Length = s.LandingLength,
                Width = width,
                ElevationChange = -s.LandingHeight,
                PitchChange = s.ArrivalPitchDeg,
                SecondaryPitchDeg = s.LandingDescentPitchDeg,
                PlanHorizontalLength = s.LandingHorizontal,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Risky,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"{namePrefix}LandingRamp_H{s.LandingHeight:F0}m",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.VerticalDescending,
                    ExitOrientation = TrackOrientationTag.Upright,
                    ElevationDelta = -s.LandingHeight,
                    ClosureCompatible = false
                }
            });
        }

        protected virtual void AddRecoveryAndTail(ResolvedTrackGenerationConfig cfg, string patternId,
            List<TrackMacroSectionDefinition> output)
        {
            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.JumpRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
        }
    }

    /// <summary>Jump group landing into a banked sweep pair (net-zero heading) before recovering.</summary>
    public sealed class JumpToBankedLandingPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.JumpToBankedLanding;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType) + cfg.MinCurveRadius,
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            var inner = new List<TrackMacroSectionDefinition>();
            var jump = new JumpGapPattern();
            if (!jump.TryPlan(cfg, patternId, ref rng, inner)) return false;

            // Swap the plain recovery for a banked S-sweep (net heading 0) + recovery.
            inner.RemoveAt(inner.Count - 1);
            output.AddRange(inner);

            float radius = Mathf.Lerp(cfg.MinCurveRadius, cfg.MaxCurveRadius, 0.5f);
            float angle = 35f;
            SectionTurnDirection dir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.SCurve,
                Length = 2f * SectionFrameBuilders.EasedArcLength(angle, radius),
                Width = cfg.RoadWidth,
                Direction = dir,
                TurnAngle = angle,
                Radius = radius,
                BankingAngle = SectionDefs.RecommendedBank(cfg, radius),
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Normal,
                LockLength = true,
                PatternId = patternId,
                DebugName = "BankedLandingSweep",
                Contract = SectionConnectionContract.Level()
            });

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.JumpRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
            return true;
        }
    }

    /// <summary>Full vertical loop with eased clothoid-style curvature.</summary>
    public sealed class FullLoopPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.FullLoop;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float radius = rng.NextFloat(cfg.MinLoopRadius, cfg.MaxLoopRadius);
            float secondRadius = rng.NextFloat(cfg.MinLoopRadius, cfg.MaxLoopRadius);
            int units = PickFullRotationUnits(cfg.AllowedLoopRotationUnits,
                cfg.MaxLoopRotationUnits, ref rng);
            if (units <= 0) return false;

            // A full eased 360-degree centerline otherwise returns almost exactly onto
            // its entry limb. Give each half a temporary, zero-end-rate yaw so the loop
            // is spatially offset while its exit heading remains closure-friendly.
            float sign = rng.NextBool() ? 1f : -1f;
            float[] biasCandidates = { 15f, 20f, 30f, 45f, 60f };
            float requiredClearance = cfg.RoadWidth * 1.05f;
            for (int i = 0; i < biasCandidates.Length; i++)
            {
                var candidate = MakeLoopDef(cfg, radius, patternId, units, secondRadius, 0f,
                    sign * biasCandidates[i]);
                var frames = SectionFrameBuilders.BuildRotationalEvent(
                    TrackConnectionFrame.Origin(cfg.RoadWidth), candidate, FrameBuildContext.From(cfg));
                float clearance = SectionFrameBuilders.MeasureRotationalEventClearance(frames,
                    cfg.RoadWidth, out _, out _);
                if (clearance + 0.01f < requiredClearance) continue;
                output.Add(candidate);
                return true;
            }

            return false;
        }

        internal static int PickUnits(int[] allowed, int maximum, ref Unity.Mathematics.Random rng, int fallback)
        {
            if (allowed == null || allowed.Length == 0) return Mathf.Min(fallback, maximum);
            var legal = new List<int>();
            foreach (int units in allowed)
                if (units >= 2 && units <= maximum) legal.Add(units);
            return legal.Count > 0 ? legal[rng.NextInt(legal.Count)] : Mathf.Min(fallback, maximum);
        }

        internal static int PickFullRotationUnits(int[] allowed, int maximum,
            ref Unity.Mathematics.Random rng)
        {
            if (allowed == null) return 0;
            var legal = new List<int>();
            foreach (int units in allowed)
                if (units >= 4 && units <= maximum && units % 4 == 0) legal.Add(units);
            return legal.Count > 0 ? legal[rng.NextInt(legal.Count)] : 0;
        }

        internal static float PickGentleHeading(ref Unity.Mathematics.Random rng)
        {
            if (rng.NextFloat() < 0.55f) return 0f;
            return (rng.NextBool() ? 1f : -1f) * (rng.NextBool() ? 15f : 30f);
        }

        internal static TrackMacroSectionDefinition MakeLoopDef(ResolvedTrackGenerationConfig cfg, float radius,
            string patternId, int units = 4, float secondRadius = 0f, float horizontalTurn = 0f,
            float yawBias = 15f)
        {
            units = Mathf.Clamp(units, 1, cfg.MaxLoopRotationUnits);
            secondRadius = secondRadius > 0.01f ? secondRadius : radius;
            float halfDegrees = units * cfg.RotationUnitDegrees * 0.5f;
            float easeScale = 1f / (1f - SectionFrameBuilders.LoopCurvatureEaseFraction);
            var phase = new RotationalPhaseDefinition
            {
                Axis = RotationalPhaseAxis.VerticalCenterline,
                Direction = RotationalPhaseDirection.Positive,
                RotationUnits = units,
                FirstHalfRadius = radius,
                SecondHalfRadius = secondRadius,
                FirstHalfLength = Mathf.Max(cfg.MinDistancePerRotationUnit * units * 0.5f,
                    halfDegrees * Mathf.Deg2Rad * radius * easeScale),
                SecondHalfLength = Mathf.Max(cfg.MinDistancePerRotationUnit * units * 0.5f,
                    halfDegrees * Mathf.Deg2Rad * secondRadius * easeScale),
                HorizontalTurnDegrees = horizontalTurn,
                FirstHalfYawBiasDegrees = yawBias,
                SecondHalfYawBiasDegrees = yawBias,
                ExitWidth = cfg.RoadWidth,
                BlendToNext = cfg.DefaultRotationalBlend
            };
            var def = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                Width = cfg.RoadWidth,
                Radius = radius,
                SecondaryRadius = secondRadius,
                Direction = horizontalTurn >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                PitchChange = units * cfg.RotationUnitDegrees,
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = false,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"Loop_{units}u_H1R{radius:F0}_H2R{secondRadius:F0}",
                RotationalPhases = new List<RotationalPhaseDefinition> { phase },
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Any,
                    ExitOrientation = TrackOrientationTag.Any,
                    HeadingDeltaDegrees = horizontalTurn,
                    PitchDeltaDegrees = units * cfg.RotationUnitDegrees,
                    ClosureCompatible = false
                }
            };
            SectionFrameBuilders.StampRotationalEventPlan(def, cfg);
            return def;
        }
    }

    /// <summary>Single corkscrew (full 360° roll around the travel axis).</summary>
    public sealed class CorkscrewPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.Corkscrew;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            int units = FullLoopPattern.PickFullRotationUnits(cfg.AllowedCorkscrewRotationUnits,
                cfg.MaxCorkscrewRotationUnits, ref rng);
            if (units <= 0) return false;
            output.Add(MakeCorkscrewDef(cfg, ref rng,
                units * cfg.RotationUnitDegrees * (rng.NextBool() ? 1f : -1f), patternId));
            return true;
        }

        internal static TrackMacroSectionDefinition MakeCorkscrewDef(ResolvedTrackGenerationConfig cfg,
            ref Unity.Mathematics.Random rng, float signedRoll, string patternId)
        {
            int units = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(signedRoll) / cfg.RotationUnitDegrees));
            signedRoll = Mathf.Sign(signedRoll) * units * cfg.RotationUnitDegrees;
            float minLen = Mathf.Max(Mathf.Max(cfg.MinCorkscrewLength,
                units * cfg.MinDistancePerRotationUnit), Mathf.Abs(signedRoll) * 1.5f / Mathf.Max(0.001f, cfg.MaxRollRateDegPerMeter));
            float length = rng.NextFloat(minLen, Mathf.Max(minLen, cfg.MaxCorkscrewLength));
            float radius = rng.NextFloat(cfg.MinCorkscrewRadius, cfg.MaxCorkscrewRadius);
            float secondRadius = rng.NextFloat(cfg.MinCorkscrewRadius, cfg.MaxCorkscrewRadius);
            float split = rng.NextFloat(0.40f, 0.60f);
            float verticalDrift = 0f;
            float firstPitchBias = 0f;
            float secondPitchBias = 0f;
            // Preserve the random draws used by established seeds, but do not use an
            // unrelated longitudinal slope as the corkscrew's supporting curvature.
            if (rng.NextFloat() < 0.65f)
            {
                float pitchSign = rng.NextBool() ? 1f : -1f;
                float[] presets = { 6f, 10f, 14f };
                float climbLimit = pitchSign > 0f ? cfg.MaxClimbAngle : cfg.MaxDropAngle;
                firstPitchBias = pitchSign * Mathf.Min(climbLimit, presets[rng.NextInt(presets.Length)]);
                secondPitchBias = pitchSign * Mathf.Min(climbLimit, presets[rng.NextInt(presets.Length)]);
            }
            firstPitchBias = 0f;
            secondPitchBias = 0f;
            float[] orbitPresets = { 6f, 8f, 10f };
            float orbitLimit = Mathf.Max(4f, Mathf.Min(cfg.MaxClimbAngle, cfg.MaxDropAngle));
            float radiusT = Mathf.InverseLerp(cfg.MinCorkscrewRadius, cfg.MaxCorkscrewRadius,
                (radius + secondRadius) * 0.5f);
            float centerlineOrbit = Mathf.Min(orbitLimit,
                orbitPresets[Mathf.Clamp(Mathf.RoundToInt(radiusT * (orbitPresets.Length - 1)), 0, orbitPresets.Length - 1)]);
            float yawBias = 0f;
            if (rng.NextFloat() >= 0.55f)
                yawBias = (rng.NextBool() ? 1f : -1f) * (rng.NextBool() ? 8f : 15f);
            var phase = new RotationalPhaseDefinition
            {
                Axis = RotationalPhaseAxis.RoadRoll,
                Direction = signedRoll >= 0f ? RotationalPhaseDirection.Positive : RotationalPhaseDirection.Negative,
                RotationUnits = units,
                FirstHalfLength = length * split,
                SecondHalfLength = length * (1f - split),
                FirstHalfRadius = radius,
                SecondHalfRadius = secondRadius,
                HorizontalTurnDegrees = 0f,
                FirstHalfYawBiasDegrees = yawBias,
                SecondHalfYawBiasDegrees = yawBias,
                VerticalDriftDegrees = verticalDrift,
                FirstHalfPitchBiasDegrees = firstPitchBias,
                SecondHalfPitchBiasDegrees = secondPitchBias,
                CenterlineOrbitDegrees = centerlineOrbit,
                ExitWidth = cfg.RoadWidth,
                BlendToNext = cfg.DefaultRotationalBlend
            };
            var def = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                Width = cfg.RoadWidth,
                Radius = radius,
                SecondaryRadius = secondRadius,
                Direction = signedRoll >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                RollChange = signedRoll,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = false,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"Corkscrew_{units}u_{(signedRoll >= 0 ? "R" : "L")}_H1R{radius:F0}_H2R{secondRadius:F0}_Orbit{centerlineOrbit:F0}",
                RotationalPhases = new List<RotationalPhaseDefinition> { phase },
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Any,
                    ExitOrientation = TrackOrientationTag.Any,
                    HeadingDeltaDegrees = 0f,
                    PitchDeltaDegrees = verticalDrift,
                    RollDeltaDegrees = signedRoll,
                    ClosureCompatible = false
                }
            };
            SectionFrameBuilders.StampRotationalEventPlan(def, cfg);
            var contract = def.Contract;
            contract.HeadingDeltaDegrees = def.TurnAngle;
            contract.ElevationDelta = def.ElevationChange;
            def.Contract = contract;
            return def;
        }
    }

    /// <summary>Climbing (or descending) helix spiral.</summary>
    public sealed class SpiralPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.Spiral;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.SpiralApproachLength, cfg.RoadWidth,
                "SpiralApproach", locked: true, patternId));

            output.Add(MakeSpiralDef(cfg, ref rng, patternId));

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.SpiralRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
            return true;
        }

        internal static TrackMacroSectionDefinition MakeSpiralDef(ResolvedTrackGenerationConfig cfg,
            ref Unity.Mathematics.Random rng, string patternId)
        {
            int revs = rng.NextInt(cfg.MinSpiralRevolutions, cfg.MaxSpiralRevolutions + 1);
            float radius = rng.NextFloat(cfg.MinSpiralRadius, cfg.MaxSpiralRadius);
            // Elevation is eased over the whole spiral, so clearance values are rounded
            // UP on a common vertical grid. Never round a safety separation downward.
            float climbPerRev = SectionFrameBuilders.QuantizeElevationUp(
                rng.NextFloat(cfg.MinSpiralClimbPerRevolution, cfg.MaxSpiralClimbPerRevolution), 5f);

            // Descending spirals are legal when the elevation plan may dip below start.
            bool descending = cfg.GroundLevelPolicy == TrackGroundLevelPolicy.FreeFloating && rng.NextFloat() < 0.35f;
            float climb = climbPerRev * revs * (descending ? -1f : 1f);

            // Spiral slope is clamped by the ordinary climb rules.
            float arcLen = 2f * Mathf.PI * radius * revs;
            float maxClimbByAngle = arcLen * Mathf.Tan((descending ? cfg.MaxDropAngle : cfg.MaxClimbAngle) * Mathf.Deg2Rad) / 1.5f;
            climb = Mathf.Clamp(climb, -maxClimbByAngle, maxClimbByAngle);

            SectionTurnDirection dir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

            return new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Spiral,
                Length = arcLen,
                Width = cfg.RoadWidth,
                Radius = radius,
                Direction = dir,
                TurnAngle = 360f * revs,
                BankingAngle = SectionDefs.RecommendedBank(cfg, radius) * 0.8f,
                ElevationChange = climb,
                SpeedIntent = SectionSpeedIntent.Medium,
                RiskLevel = SectionRiskLevel.Normal,
                RequiresRecoveryAfter = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"Spiral_{revs}rev_{(climb >= 0 ? "Up" : "Down")}_{dir}",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.Upright,
                    ElevationDelta = climb,
                    ClosureCompatible = false
                }
            };
        }
    }

    /// <summary>Half loop up to inverted + gradual 180° rollout: a vertical heading reversal.</summary>
    public class HalfLoopRolloutPattern : ITrackFeaturePattern
    {
        public virtual TrackPatternType PatternType => TrackPatternType.HalfLoopRollout;

        /// <summary>Total roll through the rollout (180 = plain Immelmann, 540 = corkscrew-extended).</summary>
        protected virtual float TotalRollDegrees => 180f;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 180f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            if (!cfg.AllowHalfRotations) return false;
            float radius = rng.NextFloat(cfg.MinHalfLoopRadius, cfg.MaxHalfLoopRadius);
            float secondRadius = rng.NextFloat(cfg.MinHalfLoopRadius, cfg.MaxHalfLoopRadius);
            float totalRoll = TotalRollDegrees;

            // Rollout must satisfy the roll-rate limit (smoothstepped roll peaks at 1.5×).
            float rollout = Mathf.Max(cfg.HalfLoopRolloutLength,
                totalRoll * 1.5f / Mathf.Max(0.001f, cfg.MaxRollRateDegPerMeter));

            float rollSign = rng.NextBool() ? 1f : -1f;

            float halfArcA = 0.5f * Mathf.PI * radius / (1f - SectionFrameBuilders.LoopCurvatureEaseFraction);
            float halfArcB = 0.5f * Mathf.PI * secondRadius / (1f - SectionFrameBuilders.LoopCurvatureEaseFraction);
            var vertical = new RotationalPhaseDefinition
            {
                Axis = RotationalPhaseAxis.VerticalCenterline,
                Direction = RotationalPhaseDirection.Positive,
                RotationUnits = 2,
                FirstHalfLength = halfArcA,
                SecondHalfLength = halfArcB,
                FirstHalfRadius = radius,
                SecondHalfRadius = secondRadius,
                ExitWidth = cfg.RoadWidth,
                BlendToNext = cfg.DefaultRotationalBlend
            };
            var roll = new RotationalPhaseDefinition
            {
                Axis = RotationalPhaseAxis.RoadRoll,
                Direction = rollSign >= 0f ? RotationalPhaseDirection.Positive : RotationalPhaseDirection.Negative,
                RotationUnits = Mathf.Max(1, Mathf.RoundToInt(totalRoll / cfg.RotationUnitDegrees)),
                FirstHalfLength = rollout * 0.5f,
                SecondHalfLength = rollout * 0.5f,
                FirstHalfRadius = cfg.MinCorkscrewRadius,
                SecondHalfRadius = cfg.MaxCorkscrewRadius,
                ExitWidth = cfg.RoadWidth,
                BlendToNext = cfg.DefaultRotationalBlend
            };

            var def = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                Width = cfg.RoadWidth,
                Radius = radius,
                SecondaryRadius = secondRadius,
                Direction = rollSign >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                TurnAngle = 180f,
                PitchChange = 180f,
                RollChange = totalRoll * rollSign,
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = false,
                LockLength = true,
                PatternId = patternId,
                DebugName = totalRoll > 200f ? $"HalfLoopToCorkscrew_H1R{radius:F0}_H2R{secondRadius:F0}" : $"HalfLoopRollout_H1R{radius:F0}_H2R{secondRadius:F0}",
                RotationalPhases = new List<RotationalPhaseDefinition> { vertical, roll },
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Any,
                    ExitOrientation = TrackOrientationTag.Any,
                    HeadingDeltaDegrees = 180f,
                    PitchDeltaDegrees = 180f,
                    RollDeltaDegrees = totalRoll * rollSign,
                    ClosureCompatible = false
                }
            };
            SectionFrameBuilders.StampRotationalEventPlan(def, cfg);
            var contract = def.Contract;
            contract.HeadingDeltaDegrees = def.TurnAngle;
            contract.ElevationDelta = def.ElevationChange;
            def.Contract = contract;
            output.Add(def);
            return true;
        }
    }

    /// <summary>Half loop directly into a corkscrew rollout (540° total roll) — one atomic compound.</summary>
    public sealed class HalfLoopToCorkscrewPattern : HalfLoopRolloutPattern
    {
        public override TrackPatternType PatternType => TrackPatternType.HalfLoopToCorkscrew;
        protected override float TotalRollDegrees => 540f;
    }

    /// <summary>Shared shape for A → transition → B compound patterns.</summary>
    public abstract class ChainedComPattern : ITrackFeaturePattern
    {
        public abstract TrackPatternType PatternType { get; }

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            var planned = new List<TrackMacroSectionDefinition>();
            if (!PlanElements(cfg, patternId, ref rng, planned)) return false;

            var phases = new List<RotationalPhaseDefinition>();
            foreach (var def in planned)
                if (def.SectionType == TrackMacroSectionType.RotationalEvent && def.RotationalPhases != null)
                    phases.AddRange(def.RotationalPhases);

            if (phases.Count > 1)
            {
                var merged = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RotationalEvent,
                    Width = cfg.RoadWidth,
                    Radius = phases[0].FirstHalfRadius,
                    SecondaryRadius = phases[phases.Count - 1].SecondHalfRadius,
                    SpeedIntent = SectionSpeedIntent.FullThrottle,
                    RiskLevel = SectionRiskLevel.Extreme,
                    LockLength = true,
                    PatternId = patternId,
                    DebugName = $"{PatternType}_Continuous_{phases.Count}ph",
                    RotationalPhases = phases,
                    Contract = new SectionConnectionContract
                    {
                        RequiredEntryOrientation = TrackOrientationTag.Any,
                        ExitOrientation = TrackOrientationTag.Any,
                        ClosureCompatible = false
                    }
                };
                foreach (var phase in phases)
                {
                    float degrees = phase.Degrees(cfg.RotationUnitDegrees);
                    if (phase.Axis == RotationalPhaseAxis.VerticalCenterline) merged.PitchChange += degrees;
                    else merged.RollChange += degrees;
                    merged.Contract.HeadingDeltaDegrees += phase.HorizontalTurnDegrees;
                    merged.Contract.PitchDeltaDegrees += phase.Axis == RotationalPhaseAxis.VerticalCenterline
                        ? degrees + phase.VerticalDriftDegrees
                        : phase.VerticalDriftDegrees;
                    merged.Contract.RollDeltaDegrees += phase.Axis == RotationalPhaseAxis.RoadRoll ? degrees : 0f;
                }
                SectionFrameBuilders.StampRotationalEventPlan(merged, cfg);
                var contract = merged.Contract;
                contract.HeadingDeltaDegrees = merged.TurnAngle;
                contract.ElevationDelta = merged.ElevationChange;
                merged.Contract = contract;
                output.Add(merged);
                return true;
            }

            // Spiral-to-roll remains two ordinary contiguous sections; the former hidden
            // CompoundTransition/approach/recovery pieces are deliberately discarded.
            foreach (var def in planned)
                if (def.SectionType != TrackMacroSectionType.Straight || def.DebugName != "CompoundTransition")
                    output.Add(def);
            return true;
        }

        protected TrackMacroSectionDefinition Transition(ResolvedTrackGenerationConfig cfg, string patternId)
            => SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.GenericTransitionLength, cfg.RoadWidth,
                "CompoundTransition", locked: true, patternId);

        protected abstract float ApproachLength(ResolvedTrackGenerationConfig cfg);
        protected abstract float RecoveryLength(ResolvedTrackGenerationConfig cfg);
        protected abstract bool PlanElements(ResolvedTrackGenerationConfig cfg, string patternId,
            ref Unity.Mathematics.Random rng, List<TrackMacroSectionDefinition> output);
    }

    /// <summary>Full loop chained into a corkscrew.</summary>
    public sealed class LoopToCorkscrewPattern : ChainedComPattern
    {
        public override TrackPatternType PatternType => TrackPatternType.LoopToCorkscrew;
        protected override float ApproachLength(ResolvedTrackGenerationConfig cfg) => cfg.LoopApproachLength;
        protected override float RecoveryLength(ResolvedTrackGenerationConfig cfg) => cfg.CorkscrewRecoveryLength;

        protected override bool PlanElements(ResolvedTrackGenerationConfig cfg, string patternId,
            ref Unity.Mathematics.Random rng, List<TrackMacroSectionDefinition> output)
        {
            int loopUnits = FullLoopPattern.PickFullRotationUnits(cfg.AllowedLoopRotationUnits,
                cfg.MaxLoopRotationUnits, ref rng);
            int corkUnits = FullLoopPattern.PickFullRotationUnits(cfg.AllowedCorkscrewRotationUnits,
                cfg.MaxCorkscrewRotationUnits, ref rng);
            if (loopUnits <= 0 || corkUnits <= 0) return false;
            float radius = rng.NextFloat(cfg.MinLoopRadius, cfg.MaxLoopRadius);
            output.Add(FullLoopPattern.MakeLoopDef(cfg, radius, patternId, loopUnits,
                yawBias: (rng.NextBool() ? 1f : -1f) * 15f));
            output.Add(Transition(cfg, patternId));
            output.Add(CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng,
                corkUnits * cfg.RotationUnitDegrees * (rng.NextBool() ? 1f : -1f), patternId));
            return true;
        }
    }

    /// <summary>Spiral chained into a corkscrew.</summary>
    public sealed class SpiralToCorkscrewPattern : ChainedComPattern
    {
        public override TrackPatternType PatternType => TrackPatternType.SpiralToCorkscrew;
        protected override float ApproachLength(ResolvedTrackGenerationConfig cfg) => cfg.SpiralApproachLength;
        protected override float RecoveryLength(ResolvedTrackGenerationConfig cfg) => cfg.CorkscrewRecoveryLength;

        protected override bool PlanElements(ResolvedTrackGenerationConfig cfg, string patternId,
            ref Unity.Mathematics.Random rng, List<TrackMacroSectionDefinition> output)
        {
            output.Add(SpiralPattern.MakeSpiralDef(cfg, ref rng, patternId));
            output.Add(Transition(cfg, patternId));
            output.Add(CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, cfg.CorkscrewRollDegrees * (rng.NextBool() ? 1f : -1f), patternId));
            return true;
        }
    }

    /// <summary>Two corkscrews with opposite roll, chained.</summary>
    public sealed class DoubleCorkscrewPattern : ChainedComPattern
    {
        public override TrackPatternType PatternType => TrackPatternType.DoubleCorkscrew;
        protected override float ApproachLength(ResolvedTrackGenerationConfig cfg) => cfg.CorkscrewApproachLength;
        protected override float RecoveryLength(ResolvedTrackGenerationConfig cfg) => cfg.CorkscrewRecoveryLength;

        protected override bool PlanElements(ResolvedTrackGenerationConfig cfg, string patternId,
            ref Unity.Mathematics.Random rng, List<TrackMacroSectionDefinition> output)
        {
            if (cfg.MaxCorkscrewRotationUnits < 8 || cfg.AllowedCorkscrewRotationUnits == null ||
                System.Array.IndexOf(cfg.AllowedCorkscrewRotationUnits, 8) < 0) return false;
            float sign = rng.NextBool() ? 1f : -1f;
            output.Add(CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng,
                8f * cfg.RotationUnitDegrees * sign, patternId));
            return true;
        }
    }

    /// <summary>
    /// Straight full pipe: approach → gradual closure into a complete tube → hold →
    /// gradual reopening → recovery. The simplest robust full-pipe variant (curved,
    /// rolling and twisting pipes can extend this later). Exit orientation: Upright.
    /// </summary>
    public sealed class FullPipePattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.FullPipe;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float length = rng.NextFloat(cfg.MinFullPipeLength, Mathf.Max(cfg.MinFullPipeLength, cfg.MaxFullPipeLength));

            // Closure and opening spans in meters, converted to normalized fractions.
            // Reject only if even the minimum body cannot host both transitions.
            float trans = cfg.PipeTransitionLength;
            if (trans * 2.2f > length) length = trans * 2.2f;
            if (length > cfg.MaxFullPipeLength * 1.5f) return false;

            float pipeWidth = cfg.RoadWidth * cfg.FullPipeRadiusScale;

            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.DefaultApproachLength, cfg.RoadWidth,
                "FullPipeApproach", locked: true, patternId));

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.FullPipe,
                Length = length,
                Width = pipeWidth,
                PipeCloseFraction = Mathf.Clamp(trans / length, 0.05f, 0.45f),
                PipeOpenFraction = Mathf.Clamp(1f - trans / length, 0.55f, 0.95f),
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Risky,
                RequiresRecoveryAfter = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"FullPipe_{length:F0}m_R{pipeWidth * 0.5f:F0}m",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.Upright,
                    ClosureCompatible = false
                }
            });

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
            return true;
        }
    }

    /// <summary>S-curve: two opposed sweepers, net-zero heading.</summary>
    public sealed class SCurvePattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.SCurve;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float radius = Mathf.Lerp(cfg.MinCurveRadius, cfg.MaxCurveRadius, rng.NextFloat(0f, 0.5f));
            float angle = SectionFrameBuilders.QuantizeArcAngle(rng.NextFloat(35f, 60f));
            SectionTurnDirection dir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.SCurve,
                Length = 2f * SectionFrameBuilders.EasedArcLength(angle, radius),
                Width = cfg.RoadWidth,
                Direction = dir,
                TurnAngle = angle,
                Radius = radius,
                BankingAngle = SectionDefs.RecommendedBank(cfg, radius) * 0.7f,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Normal,
                PatternId = patternId,
                DebugName = $"SCurve_{angle:F0}deg_{dir}First",
                Contract = SectionConnectionContract.Level()
            });
            return true;
        }
    }

    /// <summary>Chicane: left-right-left flick, net-zero heading, recovery after.</summary>
    public sealed class ChicanePattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.Chicane;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float radius = Mathf.Max(cfg.MinCurveRadius, Mathf.Lerp(cfg.MinCurveRadius, cfg.MaxCurveRadius, rng.NextFloat(0f, 0.3f)) * 0.6f);
            float angle = SectionFrameBuilders.QuantizeArcAngle(rng.NextFloat(25f, 40f));
            SectionTurnDirection dir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Chicane,
                Length = 2f * SectionFrameBuilders.EasedArcLength(angle, radius)
                       + SectionFrameBuilders.EasedArcLength(2f * angle, radius),
                Width = cfg.RoadWidth,
                Direction = dir,
                TurnAngle = angle,
                Radius = radius,
                BankingAngle = SectionDefs.RecommendedBank(cfg, radius) * 0.5f,
                SpeedIntent = SectionSpeedIntent.Medium,
                RiskLevel = SectionRiskLevel.Risky,
                RequiresRecoveryAfter = true,
                PatternId = patternId,
                DebugName = $"Chicane_{dir}First",
                Contract = SectionConnectionContract.Level()
            });

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
            return true;
        }
    }

    /// <summary>Alternating-radius sequence: two S-curves of different radii, net-zero heading.</summary>
    public sealed class AlternatingRadiusPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.AlternatingRadiusSequence;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType) * 2f,
            NetHeadingDeltaDegrees = 0f
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            var sCurve = new SCurvePattern();
            if (!sCurve.TryPlan(cfg, patternId, ref rng, output)) return false;
            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.GenericTransitionLength, cfg.RoadWidth,
                "AltRadiusLink", locked: true, patternId));
            return sCurve.TryPlan(cfg, patternId, ref rng, output);
        }
    }

    // ═══════════════════════════ Registry ═══════════════════════════

    /// <summary>Registry of the built-in feature patterns.</summary>
    public static class FeaturePatternLibrary
    {
        private static readonly Dictionary<TrackPatternType, ITrackFeaturePattern> Patterns =
            new Dictionary<TrackPatternType, ITrackFeaturePattern>
            {
                { TrackPatternType.JumpGap, new JumpGapPattern() },
                { TrackPatternType.JumpToBankedLanding, new JumpToBankedLandingPattern() },
                { TrackPatternType.FullLoop, new FullLoopPattern() },
                { TrackPatternType.Corkscrew, new CorkscrewPattern() },
                { TrackPatternType.Spiral, new SpiralPattern() },
                { TrackPatternType.HalfLoopRollout, new HalfLoopRolloutPattern() },
                { TrackPatternType.HalfLoopToCorkscrew, new HalfLoopToCorkscrewPattern() },
                { TrackPatternType.LoopToCorkscrew, new LoopToCorkscrewPattern() },
                { TrackPatternType.SpiralToCorkscrew, new SpiralToCorkscrewPattern() },
                { TrackPatternType.DoubleCorkscrew, new DoubleCorkscrewPattern() },
                { TrackPatternType.SCurve, new SCurvePattern() },
                { TrackPatternType.Chicane, new ChicanePattern() },
                { TrackPatternType.AlternatingRadiusSequence, new AlternatingRadiusPattern() },
                { TrackPatternType.FullPipe, new FullPipePattern() }
            };

        /// <summary>Corner-slot patterns are realized by the corner planner, not here.</summary>
        public static bool IsCornerSlotPattern(TrackPatternType type) => type switch
        {
            TrackPatternType.Hairpin => true,
            TrackPatternType.DoubleApex => true,
            TrackPatternType.TighteningCorner => true,
            TrackPatternType.OpeningCorner => true,
            TrackPatternType.SweeperIntoHairpin => true,
            TrackPatternType.WallrideTurn => true,
            _ => false
        };

        public static bool TryGet(TrackPatternType type, out ITrackFeaturePattern pattern)
            => Patterns.TryGetValue(type, out pattern);
    }
}
