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
        public bool AllowedInBranch;
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
            public float LaunchPitchDeg;     // shallow pitch at the lip
            public float ClimbPitchDeg;      // steeper mid-ramp pitch
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

        public static bool TrySolve(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng, out Solution s)
        {
            s = default;
            float v = cfg.DesignSpeedMps;
            float g = Mathf.Max(0.1f, cfg.Gravity);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float t = Mathf.Lerp(cfg.MinJumpAirtimeSeconds, cfg.MaxJumpAirtimeSeconds, rng.NextFloat());

                // Descent fraction k: arrival vertical speed = -g·t·k, and the flight's
                // net rise is g·t²·(0.5−k). k > 0.5 means the craft spends MORE of the
                // flight descending, so the landing mouth always sits BELOW the launch
                // lip — jumps drop onto their landings, never climb up to them.
                float k = rng.NextFloat(0.6f, 0.8f);

                float vy0 = g * t * (1f - k);
                float sinLaunch = vy0 / v;
                if (sinLaunch > Mathf.Sin(6f * Mathf.Deg2Rad)) continue; // absurd pitch for this speed

                float launchPitch = Mathf.Asin(sinLaunch) * Mathf.Rad2Deg;
                float climbPitch = rng.NextFloat(5f, 9f);
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
                for (float descent = 4.5f; descent <= 12.5f; descent += 1f)
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = false
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

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.JumpRamp,
                Length = s.LaunchLength,
                Width = cfg.RoadWidth,
                ElevationChange = s.LipHeight,
                PitchChange = s.LaunchPitchDeg,
                SecondaryPitchDeg = s.ClimbPitchDeg,
                PlanHorizontalLength = s.LaunchHorizontal,
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Risky,
                AllowsJump = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"JumpRamp_H{s.LipHeight:F0}m_{s.LaunchPitchDeg:F1}deg",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.VerticalAscending,
                    PitchDeltaDegrees = s.LaunchPitchDeg,
                    ElevationDelta = s.LipHeight,
                    ClosureCompatible = false
                }
            });

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.AirGap,
                Length = s.FlightLength,
                Width = cfg.RoadWidth,
                ElevationChange = s.GapRise,
                PitchChange = s.ArrivalPitchDeg,
                AirtimeSeconds = s.AirtimeSeconds,
                PlanHorizontalLength = s.GapHorizontal,
                RiskLevel = SectionRiskLevel.Extreme,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"AirGap_{s.GapHorizontal:F0}m_{s.AirtimeSeconds:F2}s",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.VerticalAscending,
                    ExitOrientation = TrackOrientationTag.VerticalDescending,
                    ElevationDelta = s.GapRise,
                    ClosureCompatible = false
                }
            });

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.LandingRamp,
                Length = s.LandingLength,
                Width = cfg.RoadWidth,
                ElevationChange = -s.LandingHeight,
                PitchChange = s.ArrivalPitchDeg,
                SecondaryPitchDeg = s.LandingDescentPitchDeg,
                PlanHorizontalLength = s.LandingHorizontal,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Risky,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"LandingRamp_H{s.LandingHeight:F0}m",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.VerticalDescending,
                    ExitOrientation = TrackOrientationTag.Upright,
                    ElevationDelta = -s.LandingHeight,
                    ClosureCompatible = false
                }
            });

            AddRecoveryAndTail(cfg, patternId, output);
            return true;
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = false
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = false
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float radius = rng.NextFloat(cfg.MinLoopRadius, cfg.MaxLoopRadius);

            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.LoopApproachLength, cfg.RoadWidth,
                "LoopApproach", locked: true, patternId));

            output.Add(MakeLoopDef(cfg, radius, patternId));

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.LoopRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
            return true;
        }

        internal static TrackMacroSectionDefinition MakeLoopDef(ResolvedTrackGenerationConfig cfg, float radius, string patternId)
        {
            return new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Loop,
                Length = SectionFrameBuilders.LoopArcLength(radius),
                Width = cfg.RoadWidth,
                Radius = radius,
                Direction = SectionTurnDirection.Right, // lateral exit-offset side
                PitchChange = 360f,
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"Loop_R{radius:F0}m",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.Upright,
                    PitchDeltaDegrees = 360f,
                    ClosureCompatible = false
                }
            };
        }
    }

    /// <summary>Single corkscrew (full 360° roll around the travel axis).</summary>
    public sealed class CorkscrewPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.Corkscrew;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = true
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.CorkscrewApproachLength, cfg.RoadWidth,
                "CorkscrewApproach", locked: true, patternId));

            output.Add(MakeCorkscrewDef(cfg, ref rng, cfg.CorkscrewRollDegrees * (rng.NextBool() ? 1f : -1f), patternId));

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.CorkscrewRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
            return true;
        }

        internal static TrackMacroSectionDefinition MakeCorkscrewDef(ResolvedTrackGenerationConfig cfg,
            ref Unity.Mathematics.Random rng, float signedRoll, string patternId)
        {
            float minLen = Mathf.Max(cfg.MinCorkscrewLength, Mathf.Abs(signedRoll) * 1.5f / Mathf.Max(0.001f, cfg.MaxRollRateDegPerMeter));
            float length = rng.NextFloat(minLen, Mathf.Max(minLen, cfg.MaxCorkscrewLength));
            float radius = rng.NextFloat(cfg.MinCorkscrewRadius, cfg.MaxCorkscrewRadius);

            return new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Corkscrew,
                Length = length,
                Width = cfg.RoadWidth,
                Radius = radius,
                Direction = signedRoll >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                RollChange = signedRoll,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"Corkscrew_{(signedRoll >= 0 ? "R" : "L")}_{length:F0}m",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.Upright,
                    RollDeltaDegrees = signedRoll,
                    ClosureCompatible = false
                }
            };
        }
    }

    /// <summary>Climbing (or descending) helix spiral.</summary>
    public sealed class SpiralPattern : ITrackFeaturePattern
    {
        public TrackPatternType PatternType => TrackPatternType.Spiral;

        public FeaturePatternRequirements GetRequirements(ResolvedTrackGenerationConfig cfg) => new FeaturePatternRequirements
        {
            EstimatedLength = cfg.EstimateFeatureFootprint(PatternType),
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = false
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
            float climbPerRev = rng.NextFloat(cfg.MinSpiralClimbPerRevolution, cfg.MaxSpiralClimbPerRevolution);

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
            NetHeadingDeltaDegrees = 180f,
            AllowedInBranch = false
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float radius = rng.NextFloat(cfg.MinHalfLoopRadius, cfg.MaxHalfLoopRadius);
            float halfArc = SectionFrameBuilders.HalfLoopArcLength(radius);
            float totalRoll = TotalRollDegrees;

            // Rollout must satisfy the roll-rate limit (smoothstepped roll peaks at 1.5×).
            float rollout = Mathf.Max(cfg.HalfLoopRolloutLength,
                totalRoll * 1.5f / Mathf.Max(0.001f, cfg.MaxRollRateDegPerMeter));

            float rollSign = rng.NextBool() ? 1f : -1f;

            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.HalfLoopApproachLength, cfg.RoadWidth,
                "HalfLoopApproach", locked: true, patternId));

            output.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.HalfLoopTwist,
                Length = halfArc + rollout,
                Width = cfg.RoadWidth,
                Radius = radius,
                Direction = rollSign >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                TurnAngle = 180f,
                PitchChange = 180f,
                RollChange = totalRoll * rollSign,
                ElevationChange = SectionFrameBuilders.HalfLoopTopHeight(halfArc),
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = true,
                LockLength = true,
                PatternId = patternId,
                DebugName = totalRoll > 200f ? $"HalfLoopToCorkscrew_R{radius:F0}m" : $"HalfLoopRollout_R{radius:F0}m",
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = TrackOrientationTag.Upright,
                    ExitOrientation = TrackOrientationTag.Upright,
                    HeadingDeltaDegrees = 180f,
                    PitchDeltaDegrees = 180f,
                    RollDeltaDegrees = totalRoll * rollSign,
                    ElevationDelta = SectionFrameBuilders.HalfLoopTopHeight(halfArc),
                    ClosureCompatible = false
                }
            });

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.HalfLoopRecoveryLength, cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = false
        };

        public bool TryPlan(ResolvedTrackGenerationConfig cfg, string patternId, ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            output.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, ApproachLength(cfg), cfg.RoadWidth,
                $"{PatternType}Approach", locked: true, patternId));

            if (!PlanElements(cfg, patternId, ref rng, output)) return false;

            output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, RecoveryLength(cfg), cfg.RoadWidth,
                "RecoveryStraight", locked: true, patternId));
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
            float radius = rng.NextFloat(cfg.MinLoopRadius, cfg.MaxLoopRadius);
            output.Add(FullLoopPattern.MakeLoopDef(cfg, radius, patternId));
            output.Add(Transition(cfg, patternId));
            output.Add(CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, cfg.CorkscrewRollDegrees * (rng.NextBool() ? 1f : -1f), patternId));
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
            float sign = rng.NextBool() ? 1f : -1f;
            output.Add(CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, cfg.CorkscrewRollDegrees * sign, patternId));
            output.Add(Transition(cfg, patternId));
            output.Add(CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, cfg.CorkscrewRollDegrees * -sign, patternId));
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = true
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = true
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
            NetHeadingDeltaDegrees = 0f,
            AllowedInBranch = true
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
                { TrackPatternType.AlternatingRadiusSequence, new AlternatingRadiusPattern() }
            };

        /// <summary>Corner-slot patterns are realized by the corner planner, not here.</summary>
        public static bool IsCornerSlotPattern(TrackPatternType type) => type switch
        {
            TrackPatternType.Hairpin => true,
            TrackPatternType.DoubleApex => true,
            TrackPatternType.TighteningCorner => true,
            TrackPatternType.OpeningCorner => true,
            TrackPatternType.SweeperIntoHairpin => true,
            _ => false
        };

        public static bool TryGet(TrackPatternType type, out ITrackFeaturePattern pattern)
            => Patterns.TryGetValue(type, out pattern);
    }
}
