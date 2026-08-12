using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Design
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Enums shared by the designer settings and the generator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Horizontal turn-direction character of the lap.</summary>
    public enum TurnDirectionPattern
    {
        [Tooltip("Predominantly turns in one direction — classic oval/circuit feel.")]
        Circuit,
        [Tooltip("Balanced left and right usage.")]
        Mixed,
        [Tooltip("Strong preference for changing direction every corner.")]
        Alternating,
        [Tooltip("Frequent reversals and 120–180° corners — mountain-pass switchbacks.")]
        Switchback,
        [Tooltip("Minimal directional bias — anything goes.")]
        Freeform
    }

    /// <summary>How the generator picks among valid candidates.</summary>
    public enum CandidateSelectionMode
    {
        [Tooltip("Stop at the first candidate that passes all validation.")]
        FirstValid,
        [Tooltip("Evaluate several valid candidates and pick the best-scoring one (default).")]
        BestValid
    }

    /// <summary>What happens when no valid candidate can be generated.</summary>
    public enum GenerationFailurePolicy
    {
        [Tooltip("Keep the previous valid track untouched and report the failure.")]
        KeepPreviousValidTrack,
        [Tooltip("Report the failure and produce nothing.")]
        FailAndReport,
        [Tooltip("Relax OPTIONAL weights/preferences and retry. Required minimum counts and required patterns are never removed.")]
        RelaxOptionalSettings,
        [Tooltip("Build a simple validated template that respects allowed-feature rules. Clearly reported as a fallback, never as a success.")]
        UseSimpleTemplate
    }

    /// <summary>How the track is allowed to relate to its start elevation.</summary>
    public enum TrackGroundLevelPolicy
    {
        [Tooltip("The track may climb above AND dip below its start elevation (track root position defines world placement).")]
        FreeFloating,
        [Tooltip("The track never dips below its start elevation (useful when the world has a ground plane at the start height).")]
        KeepAboveStart
    }

    /// <summary>
    /// Feature/corner pattern identifiers. A pattern is an ATOMIC group: approach,
    /// internal elements and recovery are planned and validated as one unit.
    /// </summary>
    public enum TrackPatternType
    {
        // Single features
        JumpGap,
        FullLoop,
        Corkscrew,
        Spiral,
        HalfLoopRollout,

        // Compound features (intentionally constructed, never coincidental)
        HalfLoopToCorkscrew,
        SpiralToCorkscrew,
        LoopToCorkscrew,
        DoubleCorkscrew,
        JumpToBankedLanding,

        // Corner patterns
        SCurve,
        Chicane,
        DoubleApex,
        TighteningCorner,
        OpeningCorner,
        SweeperIntoHairpin,
        Hairpin,
        AlternatingRadiusSequence,

        // Advanced road features (appended — serialized by int, never reorder)
        FullPipe,
        WallrideTurn
    }

    /// <summary>One required pattern request: this pattern must appear exactly/at least Count times.</summary>
    [Serializable]
    public class RequiredPatternEntry
    {
        [Tooltip("The atomic pattern that MUST appear on the track.")]
        public TrackPatternType Pattern = TrackPatternType.HalfLoopToCorkscrew;

        [Tooltip("How many instances are required. Generation fails with a report when they cannot fit.")]
        [Min(1)] public int Count = 1;
    }

    /// <summary>
    /// Relative selection weights for the horizontal corner families.
    /// Weights are relative to each other, not percentages.
    /// </summary>
    [Serializable]
    public class TurnFamilyWeights
    {
        [Tooltip("≈20–45° corners: barely-lift kinks and gentle direction adjustments.")]
        [Min(0f)] public float GentleBend = 2f;

        [Tooltip("≈45–80° corners: long committed sweepers.")]
        [Min(0f)] public float Sweeper = 4f;

        [Tooltip("≈80–120° corners: classic 90°-family corners.")]
        [Min(0f)] public float StandardCorner = 3f;

        [Tooltip("≈120–150° corners: heavy direction changes.")]
        [Min(0f)] public float SharpCorner = 1f;

        [Tooltip("≈150–180° corners: full reversals.")]
        [Min(0f)] public float Hairpin = 0.5f;

        public float Total => Mathf.Max(0.0001f, GentleBend + Sweeper + StandardCorner + SharpCorner + Hairpin);

        public TurnFamilyWeights Clone() => (TurnFamilyWeights)MemberwiseClone();

        public void Sanitize()
        {
            GentleBend = Mathf.Max(0f, GentleBend);
            Sweeper = Mathf.Max(0f, Sweeper);
            StandardCorner = Mathf.Max(0f, StandardCorner);
            SharpCorner = Mathf.Max(0f, SharpCorner);
            Hairpin = Mathf.Max(0f, Hairpin);
            if (Total <= 0.001f) { GentleBend = 1f; Sweeper = 1f; StandardCorner = 1f; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Setting groups
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Overall scale of the track: speed, lap time, length cap, pacing.</summary>
    [Serializable]
    public class TrackScaleSettings
    {
        [Tooltip("Expected craft speed the track is SCALED for (km/h). Does NOT change the craft's physics — it converts every time-based setting into meters. 1300 km/h ≈ 361 m/s.")]
        [Range(400f, 2000f)] public float DesignSpeedKph = 1300f;

        [Tooltip("Approximate desired lap duration at design speed (seconds). Requested track length ≈ design speed × lap time. The lap may grow when required features need more room, but never beyond Maximum Track Length.")]
        [Range(20f, 180f)] public float TargetLapTimeSeconds = 60f;

        [Tooltip("Hard upper limit for THIS track's lap length (meters), clamped by the TrackConfig rulebook. Generation fails with a report instead of exceeding it.")]
        [Min(1000f)] public float MaxTrackLengthMeters = 45000f;

        [Tooltip("0 = perfectly even rhythm, 1 = strong alternation between short dense areas and large open sections.")]
        [Range(0f, 1f)] public float PacingVariation = 0.6f;

        /// <summary>Design speed in meters per second.</summary>
        public float DesignSpeedMps => DesignSpeedKph / 3.6f;

        /// <summary>Converts a designer-facing duration into meters at design speed.</summary>
        public float SecondsToDistance(float seconds) => DesignSpeedMps * seconds;

        public void Sanitize()
        {
            DesignSpeedKph = Mathf.Clamp(DesignSpeedKph, 100f, 4000f);
            TargetLapTimeSeconds = Mathf.Max(5f, TargetLapTimeSeconds);
            MaxTrackLengthMeters = Mathf.Max(1000f, MaxTrackLengthMeters);
            PacingVariation = Mathf.Clamp01(PacingVariation);
        }
    }

    /// <summary>Lap rhythm: how many turns, in what directional character, with what straights between them.</summary>
    [Serializable]
    public class TrackLayoutSettings
    {
        [Tooltip("Minimum number of meaningful horizontal turns on the lap.")]
        [Range(3, 40)] public int MinTurnCount = 10;

        [Tooltip("Maximum number of meaningful horizontal turns on the lap.")]
        [Range(3, 40)] public int MaxTurnCount = 15;

        [Tooltip("Directional character of the corner plan: Circuit (one direction), Mixed, Alternating, Switchback (reversal-heavy), Freeform.")]
        public TurnDirectionPattern DirectionPattern = TurnDirectionPattern.Mixed;

        [Tooltip("Shortest ordinary straight, in SECONDS at design speed (0.6 s ≈ 217 m at 1300 km/h).")]
        [Range(0.3f, 8f)] public float MinStraightSeconds = 0.6f;

        [Tooltip("Longest ordinary straight, in SECONDS at design speed (2.4 s ≈ 867 m at 1300 km/h).")]
        [Range(0.3f, 8f)] public float MaxStraightSeconds = 2.4f;

        [Tooltip("Chance that compatible neighboring corners are assembled into an INTENTIONAL corner pattern (S-curve, double apex, sweeper-into-hairpin, …) instead of placed independently.")]
        [Range(0f, 1f)] public float CornerSequenceChance = 0.4f;

        public void Sanitize()
        {
            MinTurnCount = Mathf.Clamp(MinTurnCount, 3, 60);
            MaxTurnCount = Mathf.Clamp(MaxTurnCount, MinTurnCount, 60);
            MinStraightSeconds = Mathf.Max(0.1f, MinStraightSeconds);
            MaxStraightSeconds = Mathf.Max(MinStraightSeconds, MaxStraightSeconds);
            CornerSequenceChance = Mathf.Clamp01(CornerSequenceChance);
        }
    }

    /// <summary>Corner geometry: radii, turn-family weights and banking.</summary>
    [Serializable]
    public class TrackCornerSettings
    {
        [Tooltip("Smallest ordinary curve radius (meters). At 1300 km/h even 'tight' corners are hundreds of meters.")]
        [Min(50f)] public float MinCurveRadius = 400f;

        [Tooltip("Largest ordinary curve radius (meters).")]
        [Min(50f)] public float MaxCurveRadius = 1600f;

        [Tooltip("Relative weights of the corner families (gentle bend → hairpin). Weights are relative, not percentages.")]
        public TurnFamilyWeights TurnWeights = new TurnFamilyWeights();

        [Header("Banking")]
        [Tooltip("0 = flat corners, 1 = corners banked at the full physically recommended angle for their speed/radius (clamped by Max Bank Angle).")]
        [Range(0f, 1f)] public float BankingStrength = 0.8f;

        [Tooltip("Hard maximum bank angle for THIS track (degrees), clamped by the TrackConfig rulebook.")]
        [Range(0f, 85f)] public float MaxBankAngle = 72f;

        [Tooltip("How much of a corner's bank GEOMETRICALLY tilts the road floor (the rest is expressed through the raised outside wall). 0 = perfectly level floor, 1 = strong visible tilt. The tilt eases in/out with the bank and is capped at 18° so wide roads never become ramps.")]
        [Range(0f, 1f)] public float FloorTiltStrength = 0.2f;

        public void Sanitize()
        {
            MinCurveRadius = Mathf.Max(10f, MinCurveRadius);
            MaxCurveRadius = Mathf.Max(MinCurveRadius, MaxCurveRadius);
            BankingStrength = Mathf.Clamp01(BankingStrength);
            MaxBankAngle = Mathf.Clamp(MaxBankAngle, 0f, 85f);
            FloorTiltStrength = Mathf.Clamp01(FloorTiltStrength);
            TurnWeights ??= new TurnFamilyWeights();
            TurnWeights.Sanitize();
        }
    }

    /// <summary>The global half-pipe road cross-section.</summary>
    [Serializable]
    public class TrackRoadSettings
    {
        [Tooltip("Width of the flat center floor in meters (before Road Scale). Total road width is DERIVED: (FlatCenterWidth + 2 × WallHeight) × RoadScale.")]
        [Range(4f, 48f)] public float FlatCenterWidth = 16f;

        [Tooltip("Wall size in meters (before Road Scale): the wall's horizontal footprint, and at Wall Curve = 1 also its exact height — a perfect quarter circle of this radius.")]
        [Range(24f, 48f)] public float WallHeight = 24f;

        [Tooltip("Wall bend 0..1. 0 = walls lie flat as straight extensions of the center floor. 1 = perfect quarter circle: vertical tip, exactly Wall Height tall. In between the wall is a circular arc over the same footprint (tip height = WallHeight × tan(curve × 45°)).")]
        [Range(0f, 1f)] public float WallCurve = 1f;

        [Tooltip("Uniform scale on the whole cross-section (flat center, walls, safety lip and curl together). Keeps the authored ratio exact — the road just gets bigger.")]
        [Range(1f, 5f)] public float RoadScale = 1f;

        [Tooltip("Height of the solid safety lip at the very top edge of the half-pipe (meters).")]
        [Range(0f, 4f)] public float SafetyLipHeight = 1f;

        [Tooltip("Cross-section sample points per SIDE (total profile points = 2×resolution + 1). Higher = smoother bowl, more vertices. Points concentrate on the curved walls automatically.")]
        [Range(4, 96)] public int ProfileResolution = 32;

        [Tooltip("COLLISION cross-section resolution (capped by the render resolution above). The physics collider is built at this detail, NOT the render detail — too coarse and hover nodes feel a normal 'cliff' crossing the floor→wall shoulder (the felt wobble). The wall-concentrating warp puts most of these points on the shoulder/wall where they matter. Higher = cleaner road/wall contact, more collider triangles. 10 was the old (rough) value.")]
        [Range(8, 96)] public int ColliderProfileResolution = 40;

        [Header("Dynamic Turn Rounding")]
        [Tooltip("Turn interiors progressively lose their flat center and become continuously rounded bowls; straights keep the configured center-flat ratio. Driven by the same smooth field as banking — no ridge can appear where the flat disappears.")]
        public bool DynamicTurnRounding = true;

        [Tooltip("How strongly turn demand converts into rounding. 1 = the apex of a committed turn reaches the minimum center-flat ratio.")]
        [Range(0f, 1f)] public float TurnRoundingStrength = 0.85f;

        [Tooltip("Center-flat ratio at FULL rounding. 0 = the apex may become fully rounded across its width.")]
        [Range(0f, 0.5f)] public float MinimumTurnCenterFlatRatio = 0.05f;

        [Header("Outside Catch Wall")]
        [Tooltip("In demanding turns the outside wall continues past vertical into a gentle inward curl that guides the craft back toward the bowl. Still an open road — not a pipe, not a wallride.")]
        public bool OutsideCatchWall = true;

        [Tooltip("Maximum catch-wall engagement. 1 = full curl (Maximum Overhang Angle past vertical) on the most demanding corners.")]
        [Range(0f, 1f)] public float CatchWallStrength = 0.6f;

        [Tooltip("How far past vertical the catch wall may curl (degrees). 10–30° is the useful range.")]
        [Range(0f, 35f)] public float MaxOverhangAngle = 18f;

        [Tooltip("Radius of the capture curl (meters).")]
        [Range(2f, 60f)] public float OverhangRadius = 10f;

        [Tooltip("Turn demand (0..1 of full support) below which the catch wall stays disengaged — gentle corners keep the ordinary wall + safety lip.")]
        [Range(0f, 1f)] public float CatchWallMinimumDemand = 0.35f;

        public void Sanitize()
        {
            FlatCenterWidth = Mathf.Max(2f, FlatCenterWidth);
            WallHeight = Mathf.Max(0.5f, WallHeight);
            WallCurve = Mathf.Clamp01(WallCurve);
            RoadScale = Mathf.Clamp(RoadScale, 0.25f, 5f);
            SafetyLipHeight = Mathf.Max(0f, SafetyLipHeight);
            ProfileResolution = Mathf.Clamp(ProfileResolution, 3, 96);
            TurnRoundingStrength = Mathf.Clamp01(TurnRoundingStrength);
            MinimumTurnCenterFlatRatio = Mathf.Clamp(MinimumTurnCenterFlatRatio, 0f, 0.5f);
            CatchWallStrength = Mathf.Clamp01(CatchWallStrength);
            MaxOverhangAngle = Mathf.Clamp(MaxOverhangAngle, 0f, 35f);
            OverhangRadius = Mathf.Clamp(OverhangRadius, 1f, 100f);
            CatchWallMinimumDemand = Mathf.Clamp01(CatchWallMinimumDemand);
        }
    }

    /// <summary>Transition durations and safety/readability spacing. All in seconds at design speed.</summary>
    [Serializable]
    public class TrackTransitionSettings
    {
        [Tooltip("Generic blend time between sections (width/shape changes without a dedicated field), seconds.")]
        [Range(0.2f, 3.5f)] public float GenericTransitionSeconds = 0.9f;

        [Tooltip("Time spent easing bank in/out of corners, seconds. Too-short bank blends become launch ramps — the generator auto-expands them when room exists.")]
        [Range(0.35f, 3f)] public float BankTransitionSeconds = 0.9f;

        [Tooltip("Time spent easing pitch (climbs, drops, crests), seconds.")]
        [Range(0.45f, 3.5f)] public float PitchTransitionSeconds = 1.0f;

        [Tooltip("Time spent easing FREE ROLL (corkscrews, inversion recovery). Separate from banking: banking supports a horizontal turn; roll changes the craft's orientation.")]
        [Range(0.6f, 4f)] public float RollTransitionSeconds = 1.3f;

        [Tooltip("Time over which road-width changes blend, seconds.")]
        [Range(0.35f, 2.5f)] public float WidthTransitionSeconds = 0.7f;

        [Tooltip("Time over which half-pipe depth/shape changes blend, seconds.")]
        [Range(0.45f, 3f)] public float CrossSectionTransitionSeconds = 0.8f;

        [Tooltip("Easing curve used by all blends.")]
        public TrackBlendCurve BlendCurve = TrackBlendCurve.SmootherStep;

        [Header("Connectors")]
        [Tooltip("Shortest meaningful connector, seconds at design speed. Straights between content shorter than the resolved blend requirement are lengthened, absorbed into a turn complex, or bridged — never emitted as tiny independent roads.")]
        [Range(0.1f, 2.5f)] public float MinimumConnectorSeconds = 0.4f;

        [Tooltip("How strongly bridge/transfer connectors inherit their neighbours' surface intent (bank, wall support, turn rounding). 0 = every connector resets to neutral, 1 = full carry-through (recommended — anything less leaves a proportional dip across every turn complex).")]
        [Range(0f, 1f)] public float ConnectorInheritanceStrength = 1f;

        [Tooltip("A straight between two SAME-DIRECTION turns shorter than this (seconds at design speed) is one turn complex: bank, outside wall, depth and rounding hold through it — no reset, no wavy wall. Longer straights may legitimately relax to neutral.")]
        [Range(0.5f, 5f)] public float SameDirectionBridgeSeconds = 2.2f;

        [Tooltip("Shortest time the banking field may take to swing from full one-side bank through zero to the other side, seconds. Left→right flicks faster than this are physically a wall at 1300 km/h.")]
        [Range(0.3f, 3.5f)] public float MinimumBankReversalSeconds = 0.9f;

        [Tooltip("Allow the connector analysis to LENGTHEN too-short adjustable connectors up to their blend requirement.")]
        public bool AllowConnectorExpansion = true;

        [Tooltip("Allow too-short connectors between same-direction turns to be absorbed into one turn complex (bank, wall support and rounding carry through instead of resetting).")]
        public bool AllowConnectorAbsorption = true;

        [Header("Safety & Readability")]
        [Tooltip("Default readable approach BEFORE a major feature, seconds (1.4 s ≈ 506 m at 1300 km/h). Feature-specific approaches override via max(), never sum.")]
        [Range(0.8f, 4f)] public float DefaultApproachSeconds = 1.4f;

        [Tooltip("Default recovery AFTER a major feature, seconds.")]
        [Range(0.7f, 3f)] public float DefaultRecoverySeconds = 1.0f;

        [Tooltip("Minimum spacing between UNRELATED dangerous features, seconds. Compound patterns bypass this internally — they are validated as one atomic group.")]
        [Range(1.2f, 5f)] public float DangerousSpacingSeconds = 1.8f;

        [Tooltip("How long a major feature should be visible before the craft reaches it, seconds.")]
        [Range(1.2f, 4f)] public float VisualPreviewSeconds = 1.6f;

        public void Sanitize()
        {
            GenericTransitionSeconds = Mathf.Max(0.1f, GenericTransitionSeconds);
            BankTransitionSeconds = Mathf.Max(0.1f, BankTransitionSeconds);
            PitchTransitionSeconds = Mathf.Max(0.1f, PitchTransitionSeconds);
            RollTransitionSeconds = Mathf.Max(0.1f, RollTransitionSeconds);
            WidthTransitionSeconds = Mathf.Max(0.1f, WidthTransitionSeconds);
            CrossSectionTransitionSeconds = Mathf.Max(0.1f, CrossSectionTransitionSeconds);
            MinimumConnectorSeconds = Mathf.Max(0.05f, MinimumConnectorSeconds);
            ConnectorInheritanceStrength = Mathf.Clamp01(ConnectorInheritanceStrength);
            MinimumBankReversalSeconds = Mathf.Max(0.1f, MinimumBankReversalSeconds);
            SameDirectionBridgeSeconds = Mathf.Max(0.2f, SameDirectionBridgeSeconds);
            DefaultApproachSeconds = Mathf.Max(0.2f, DefaultApproachSeconds);
            DefaultRecoverySeconds = Mathf.Max(0.2f, DefaultRecoverySeconds);
            DangerousSpacingSeconds = Mathf.Max(0.2f, DangerousSpacingSeconds);
            VisualPreviewSeconds = Mathf.Max(0.2f, VisualPreviewSeconds);
        }
    }

    /// <summary>Vertical content: amplitude, major elevation sections, crests, bridges, underpasses.</summary>
    [Serializable]
    public class TrackElevationSettings
    {
        [Tooltip("Target height range of the lap (meters). The generator plans climbs/drops so the actual range approaches this value.")]
        [Range(0f, 1200f)] public float TargetElevationAmplitude = 180f;

        [Tooltip("Minimum number of MAJOR elevation sections (dedicated climbs/drops).")]
        [Range(0, 16)] public int MinMajorElevationSections = 3;

        [Tooltip("Maximum number of MAJOR elevation sections.")]
        [Range(0, 16)] public int MaxMajorElevationSections = 6;

        [Tooltip("Maximum sustained climb angle for THIS track (degrees), clamped by the rulebook.")]
        [Range(2f, 45f)] public float MaxClimbAngle = 30f;

        [Tooltip("Maximum sustained drop angle for THIS track (degrees), clamped by the rulebook.")]
        [Range(2f, 45f)] public float MaxDropAngle = 32f;

        [Tooltip("Net-zero crests/dips placed on straights (hill up-and-over or dip down-and-back).")]
        public TrackFeatureRule Crests = new TrackFeatureRule(true, 0, 4, 1f);

        [Tooltip("Elevated bridge sections (net-zero raised straights).")]
        public TrackFeatureRule Bridges = new TrackFeatureRule(true, 0, 2, 0.7f);

        [Tooltip("Lowered underpass sections (net-zero dipped straights). Ignored when Ground Level Policy forbids dipping below the start elevation.")]
        public TrackFeatureRule Underpasses = new TrackFeatureRule(true, 0, 2, 0.6f);

        [Tooltip("Whether the lap may dip below its start elevation. Track root position defines world placement — the generator does not force world Y = 0.")]
        public TrackGroundLevelPolicy GroundLevelPolicy = TrackGroundLevelPolicy.KeepAboveStart;

        public void Sanitize()
        {
            TargetElevationAmplitude = Mathf.Max(0f, TargetElevationAmplitude);
            MinMajorElevationSections = Mathf.Max(0, MinMajorElevationSections);
            MaxMajorElevationSections = Mathf.Max(MinMajorElevationSections, MaxMajorElevationSections);
            MaxClimbAngle = Mathf.Clamp(MaxClimbAngle, 1f, 60f);
            MaxDropAngle = Mathf.Clamp(MaxDropAngle, 1f, 60f);
            (Crests ??= new TrackFeatureRule()).Sanitize();
            (Bridges ??= new TrackFeatureRule()).Sanitize();
            (Underpasses ??= new TrackFeatureRule()).Sanitize();
        }
    }

    /// <summary>Feature rules and required patterns.</summary>
    [Serializable]
    public class TrackFeatureSettings
    {
        [Tooltip("Jump groups: approach → launch → air gap → landing → recovery. Air-gap distance is derived from the ballistic trajectory at design speed.")]
        public TrackFeatureRule Jumps = new TrackFeatureRule(true, 0, 2, 1f);

        [Tooltip("Full vertical loops (eased clothoid-style curvature).")]
        public TrackFeatureRule Loops = new TrackFeatureRule(true, 0, 1, 0.8f);

        [Tooltip("Corkscrews: the road rolls a full revolution around the travel axis.")]
        public TrackFeatureRule Corkscrews = new TrackFeatureRule(true, 0, 1, 0.8f);

        [Tooltip("Climbing/descending helix spirals (parking-garage style).")]
        public TrackFeatureRule Spirals = new TrackFeatureRule(true, 0, 1, 0.7f);

        [Tooltip("Half-loop patterns: half loop up to inverted, then a gradual 180° rollout — reverses heading vertically.")]
        public TrackFeatureRule HalfLoops = new TrackFeatureRule(true, 0, 1, 0.5f);

        [Tooltip("Full closed-pipe road sections: the cross-section closes gradually into a tube the craft can roll around, then reopens.")]
        public TrackFeatureRule FullPipes = new TrackFeatureRule(true, 0, 1, 0.6f);

        [Tooltip("Wallride turns: corners where the boosted, over-curled outside wall becomes the primary driving surface.")]
        public TrackFeatureRule Wallrides = new TrackFeatureRule(true, 0, 2, 0.7f);

        [Header("Full Pipe Shape")]
        [Tooltip("Full-pipe body duration window, seconds at design speed (includes the closure and opening spans).")]
        [Range(2f, 12f)] public float MinFullPipeSeconds = 5f;
        [Range(2f, 12f)] public float MaxFullPipeSeconds = 9f;

        [Tooltip("Pipe diameter as a fraction of the road width (radius = width × scale / 2).")]
        [Range(0.5f, 1.25f)] public float FullPipeRadiusScale = 1.1f;

        [Tooltip("Duration of the pipe closure (and reopening) span, seconds. Short closures are physical walls at 1300 km/h.")]
        [Range(0.6f, 3f)] public float PipeTransitionSeconds = 1.2f;

        [Header("Corner patterns")]
        [Tooltip("Chicane patterns (left-right-left flicks) placed as intentional corner patterns.")]
        public TrackFeatureRule Chicanes = new TrackFeatureRule(true, 0, 2, 1f);

        [Tooltip("S-curve patterns (two opposed sweepers).")]
        public TrackFeatureRule SCurves = new TrackFeatureRule(true, 0, 2, 1f);

        [Tooltip("Hairpin corner patterns (150–180° reversals).")]
        public TrackFeatureRule Hairpins = new TrackFeatureRule(true, 0, 1, 0.5f);

        [Header("Grouping")]
        [Tooltip("Minimum number of feature groups (single features or compound patterns) on the lap.")]
        [Range(0, 12)] public int MinFeatureGroups = 2;

        [Tooltip("Maximum number of feature groups on the lap.")]
        [Range(0, 12)] public int MaxFeatureGroups = 5;

        [Tooltip("Chance that an optional feature slot becomes a COMPOUND pattern (e.g. loop → corkscrew) instead of a single feature.")]
        [Range(0f, 1f)] public float CompoundFeatureChance = 0.35f;

        [Tooltip("Maximum elements chained inside one compound pattern.")]
        [Range(1, 4)] public int MaxCompoundElements = 2;

        [Tooltip("Patterns that MUST appear on the track (validated; generation fails with a report when they cannot fit).")]
        public List<RequiredPatternEntry> RequiredPatterns = new List<RequiredPatternEntry>();

        public void Sanitize()
        {
            (Jumps ??= new TrackFeatureRule()).Sanitize();
            (Loops ??= new TrackFeatureRule()).Sanitize();
            (Corkscrews ??= new TrackFeatureRule()).Sanitize();
            (Spirals ??= new TrackFeatureRule()).Sanitize();
            (HalfLoops ??= new TrackFeatureRule()).Sanitize();
            (FullPipes ??= new TrackFeatureRule()).Sanitize();
            (Wallrides ??= new TrackFeatureRule()).Sanitize();
            (Chicanes ??= new TrackFeatureRule()).Sanitize();
            (SCurves ??= new TrackFeatureRule()).Sanitize();
            (Hairpins ??= new TrackFeatureRule()).Sanitize();
            MinFullPipeSeconds = Mathf.Clamp(MinFullPipeSeconds, 1f, 15f);
            MaxFullPipeSeconds = Mathf.Clamp(MaxFullPipeSeconds, MinFullPipeSeconds, 15f);
            FullPipeRadiusScale = Mathf.Clamp(FullPipeRadiusScale, 0.4f, 1.5f);
            PipeTransitionSeconds = Mathf.Clamp(PipeTransitionSeconds, 0.4f, 4f);
            MinFeatureGroups = Mathf.Max(0, MinFeatureGroups);
            MaxFeatureGroups = Mathf.Max(MinFeatureGroups, MaxFeatureGroups);
            CompoundFeatureChance = Mathf.Clamp01(CompoundFeatureChance);
            MaxCompoundElements = Mathf.Clamp(MaxCompoundElements, 1, 4);
            RequiredPatterns ??= new List<RequiredPatternEntry>();
            foreach (var p in RequiredPatterns)
                if (p != null) p.Count = Mathf.Max(1, p.Count);
        }
    }

    /// <summary>What a quarter of the lap contains: one road, or two alternative roads.</summary>
    public enum TrackQuarterType
    {
        SingleRoad,
        DualRoad
    }

    /// <summary>
    /// How a Dual Road Quarter presents the route choice. V1 implements ONLY
    /// JumpSelection (the choice happens mid-air over an air gap); the other values
    /// exist for forward compatibility and resolve to JumpSelection with a warning.
    /// </summary>
    public enum DualQuarterChoiceType
    {
        GroundSplit,
        JumpSelection,
        HighLowSelection,
        TwinPipeSelection
    }

    /// <summary>Designer override for one quarter's type; Auto lets the Quarter stream decide.</summary>
    public enum QuarterTypeOverride
    {
        Auto,
        SingleRoad,
        DualRoad
    }

    /// <summary>
    /// Per-quarter lock levels (production doc §12). V1 honors Unlocked and FullyLocked
    /// in the partial-regeneration commands; the intermediate modes are staged work.
    /// </summary>
    public enum QuarterLockMode
    {
        Unlocked,
        TypeOnly,
        GatesAndEnvelope,
        LayoutSkeleton,
        Geometry,
        FullyLocked
    }

    /// <summary>What to do when a dual quarter's routes cannot be balanced (never silently accept a dominant route).</summary>
    public enum DualQuarterBalancePolicy
    {
        DemoteToSingleRoad,
        RejectCandidate
    }

    /// <summary>
    /// Quarter settings: the lap is always divided into exactly 4 logical quarters
    /// (0–25–50–75–100% lap progress); each is a Single Road Quarter or a Dual Road
    /// Quarter (two alternative roads, entered and left by jumps — the route choice
    /// happens in the air). Lap length counts the canonical road only.
    /// </summary>
    [Serializable]
    public class TrackQuarterSettings
    {
        [Header("Dual Quarter Count")]
        [Tooltip("Minimum number of Dual Road Quarters on the lap (0–4).")]
        [Range(0, 4)] public int MinimumDualQuarterCount = 0;

        [Tooltip("Maximum number of Dual Road Quarters on the lap (0–4).")]
        [Range(0, 4)] public int MaximumDualQuarterCount = 1;

        [Tooltip("Per-quarter type override. Auto lets the Quarter seed stream decide within the count limits.")]
        public QuarterTypeOverride[] QuarterTypeOverrides = new QuarterTypeOverride[4];

        [Tooltip("Forbid two Dual Road Quarters in a row (they would share only a short single-road neck between the convergence catch and the next choice jump).")]
        public bool PreventAdjacentDualQuarters = false;

        [Tooltip("Allow the quarter containing the start line to be dual (the choice jump would come soon after the start).")]
        public bool AllowQ1Dual = false;

        [Tooltip("Allow the quarter ending at the finish line to be dual (the convergence catch would come shortly before it).")]
        public bool AllowQ4Dual = true;

        [Header("Choice Structure")]
        [Tooltip("How the route choice is presented. V1 implements JumpSelection only; other values resolve to JumpSelection with a warning.")]
        public DualQuarterChoiceType ChoiceType = DualQuarterChoiceType.JumpSelection;

        [Tooltip("Lateral separation between the two landing mouths (and between the two exit lips), meters. Clamped by rulebook limits and by ballistic aim authority at design speed.")]
        [Range(16f, 80f)] public float LaneSeparationMeters = 24f;

        [Tooltip("Width of the shared convergence catch relative to the road width. Broad catches tolerate lateral aiming error at speed and let both lanes arrive simultaneously.")]
        [Range(1.2f, 2f)] public float CatchWidthScale = 1.7f;

        [Header("Alternate Road (Route B)")]
        [Tooltip("Road width of the alternate road relative to the main road width.")]
        [Range(0.7f, 1.3f)] public float DualRoadWidthScale = 1f;

        [Tooltip("Accepted relative length difference between the two roads (fraction of road A's length). The alternate road is fitted to a similar length so ride times stay comparable.")]
        [Range(0.05f, 0.5f)] public float RoadLengthTolerance = 0.2f;

        [Header("Balance (validation only — never iterative geometry repair)")]
        [Tooltip("Maximum allowed NEUTRAL-craft time difference between the two roads, percent of the faster road (doc target 3–5%).")]
        [Range(1f, 10f)] public float NeutralTimeTolerancePercent = 4f;

        [Tooltip("Require that at least one craft archetype prefers each road (neither road dominates every archetype).")]
        public bool RequireArchetypeDifferentiation = true;

        [Tooltip("What happens when a dual quarter's roads cannot be balanced: demote that quarter to Single Road (with a report warning) or reject the whole candidate.")]
        public DualQuarterBalancePolicy BalancePolicy = DualQuarterBalancePolicy.DemoteToSingleRoad;

        public void Sanitize()
        {
            MinimumDualQuarterCount = Mathf.Clamp(MinimumDualQuarterCount, 0, 4);
            MaximumDualQuarterCount = Mathf.Clamp(MaximumDualQuarterCount, MinimumDualQuarterCount, 4);
            if (QuarterTypeOverrides == null || QuarterTypeOverrides.Length != 4)
            {
                var fixedOverrides = new QuarterTypeOverride[4];
                if (QuarterTypeOverrides != null)
                    for (int i = 0; i < Mathf.Min(4, QuarterTypeOverrides.Length); i++)
                        fixedOverrides[i] = QuarterTypeOverrides[i];
                QuarterTypeOverrides = fixedOverrides;
            }
            LaneSeparationMeters = Mathf.Clamp(LaneSeparationMeters, 8f, 120f);
            CatchWidthScale = Mathf.Clamp(CatchWidthScale, 1f, 2.5f);
            DualRoadWidthScale = Mathf.Clamp(DualRoadWidthScale, 0.5f, 1.5f);
            RoadLengthTolerance = Mathf.Clamp(RoadLengthTolerance, 0.02f, 0.6f);
            NeutralTimeTolerancePercent = Mathf.Clamp(NeutralTimeTolerancePercent, 0.5f, 20f);
        }
    }

    /// <summary>Road guidance visuals: center-aisle guide lines and transverse wall markers.</summary>
    [Serializable]
    public class TrackVisualSettings
    {
        [Header("Center Guide Lines")]
        [Tooltip("Two longitudinal guide lines marking the edges of the center-flat region. They move inward and fade as dynamic turn rounding removes the flat zone.")]
        public bool CenterGuideEnabled = true;

        [Tooltip("Center guide color (shader-based — never raised collision geometry).")]
        public Color CenterGuideColor = Color.white;

        [Tooltip("Guide line width in meters.")]
        [Range(0.05f, 2f)] public float CenterGuideWidth = 0.35f;

        [Tooltip("Emission strength of the guide lines.")]
        [Range(0f, 4f)] public float CenterGuideEmission = 1.2f;

        [Header("Wall Markers")]
        [Tooltip("Transverse marker lines across the walls: speed, curvature and orientation rhythm. Spacing is VISUAL — independent of the physical ring density.")]
        public bool WallMarkersEnabled = true;

        [Tooltip("Wall marker color.")]
        public Color WallMarkerColor = Color.white;

        [Tooltip("Marker line width along the track (meters).")]
        [Range(0.1f, 4f)] public float WallMarkerWidth = 0.8f;

        [Tooltip("Meters between wall markers. Never draws one per geometric ring — high-resolution geometry would be visual noise.")]
        [Range(8f, 250f)] public float WallMarkerSpacingMeters = 48f;

        [Tooltip("Distance over which markers fade out (meters).")]
        [Range(50f, 3000f)] public float MarkerFadeDistance = 800f;

        public void Sanitize()
        {
            CenterGuideWidth = Mathf.Clamp(CenterGuideWidth, 0.02f, 4f);
            CenterGuideEmission = Mathf.Clamp(CenterGuideEmission, 0f, 8f);
            WallMarkerWidth = Mathf.Clamp(WallMarkerWidth, 0.05f, 8f);
            WallMarkerSpacingMeters = Mathf.Clamp(WallMarkerSpacingMeters, 4f, 500f);
            MarkerFadeDistance = Mathf.Clamp(MarkerFadeDistance, 20f, 5000f);
        }
    }

    /// <summary>Generation behavior: attempts, selection, failure policy, mesh density.</summary>
    [Serializable]
    public class TrackGenerationBehaviorSettings
    {
        [Tooltip("Maximum layout attempts before the failure policy applies. Attempts are layout-math only (no meshes) — searching deep is cheap.")]
        [Range(4, 512)] public int MaxAttempts = 48;

        [Tooltip("FirstValid = stop at the first passing candidate. BestValid = score several valid candidates and pick the best (default).")]
        public CandidateSelectionMode SelectionMode = CandidateSelectionMode.BestValid;

        [Tooltip("In BestValid mode: stop searching once this many valid candidates have been scored.")]
        [Range(1, 16)] public int CandidatesToScore = 4;

        [Tooltip("What happens when NO valid candidate exists. The generic base layout is never silently returned.")]
        public GenerationFailurePolicy FailurePolicy = GenerationFailurePolicy.KeepPreviousValidTrack;

        [Tooltip("Fraction of the lap reserved for the closure system (legal straights/curves that weld the loop shut without warping feature geometry).")]
        [Range(0.1f, 0.35f)] public float ClosureReserveFraction = 0.2f;

        [Header("Mesh Quality (advanced)")]
        [Tooltip("Target meters between mesh rings — the global retopology pass spreads rings at this spacing over the whole track (tightened automatically where features demand it).")]
        [Range(0.5f, 8f)] public float MetersPerRing = 1.5f;

        [Tooltip("Maximum facet-angle change between consecutive rings (degrees). At 1300 km/h every degree of facet reads as phantom vertical velocity to the hover suspension — 0.5° recommended.")]
        [Range(0.25f, 1f)] public float MaxFacetAngleDegrees = 0.5f;

        [Tooltip("Performance budget: total rings for the WHOLE track. Small tracks keep the full Meters Per Ring density; huge tracks (long Rollercoaster laps) automatically spread this budget instead of exploding the vertex count.")]
        [Range(8000, 100000)] public int TargetTotalRings = 20000;

        [Header("Dynamic Topology (experimental)")]
        [Tooltip("EXPERIMENTAL (Stage D). When on, a gap's mandatory lead straight is dropped where the transition resolver says the neighbours weld directly AND the closure capacity check still passes — letting a corkscrew flow straight out of a curve with no dead straight. Off = current behavior exactly. Scoped to the inline-corkscrew pilot; expands in later stages.")]
        public bool DynamicFeatureAdjacency = false;

        [Tooltip("EXPERIMENTAL (Stage E). When on, ordinary corner magnitudes are quantized to canonical 45°/90°/180° turn demands solved exactly to close the lap, instead of the continuous ±20°-step winding balancer. Changes generated layouts (flag-gated so saved seeds stay reproducible with it OFF). Half-loops stay 180°; internal geometry remains smooth.")]
        public bool UseCanonicalTurns = false;

        [Tooltip("EXPERIMENTAL (closure relief, §7 extension). When on, the 2D closure solver may nudge plain corner angles by a bounded ±4° in equal-and-opposite pairs (net heading — and any canonical token — preserved) to close position residuals the radius/straight solver leaves behind, before spending an S-bend rescue. Targets the near-miss ClosurePositionFailure tail. Off = current behavior exactly. Best paired with Use Canonical Turns, which otherwise removes this angular freedom.")]
        public bool ClosureAngleRelief = false;

        [Tooltip("EXPERIMENTAL. When on, the dual-road quarter fitter gets a larger adaptive attempt budget (its per-attempt state already homes in on corner count, clearance and length/balance target, so more iterations converge more often). A road-B fit attempt is far cheaper than re-rolling a whole candidate, so this trades a little plan-time for fewer 'Dual Road Quarter could not be built' failures — the dominant RequiredFeatureMissing bucket. Off = current behavior exactly.")]
        public bool RobustDualQuarterFit = false;

        [Tooltip("EXPERIMENTAL (Stage F pilot). When on, at most one gentle-to-medium corner per lap is realized as a DIRECTIONAL CORKSCREW — a barrel roll whose axis also turns by the corner's own angle (its stamped plan-view heading equals the corner, so lap closure is preserved by construction). Barrels are long, so expect a few more length-budget failures. Off = corners are ordinary curves exactly as before. VERIFY IN PLAY: confirm the craft stays in through the turning+inverting barrel.")]
        public bool DirectionalCorkscrews = false;

        [Tooltip("EXPERIMENTAL. When on, barrel/corkscrew regions (road roll ≥120°) are exempt from the global ring-budget relaxation, so their WIDE ROLLING FLOOR keeps its facet-target density instead of coarsening to a multi-metre twist that launches the craft off the floor edge at speed. Straights/level regions absorb the budget instead. Changes ring DENSITY only — never road width, path, walls, or corkscrew proportions. Off = current behavior exactly.")]
        public bool SmoothCorkscrewFloor = false;

        public void Sanitize()
        {
            MaxAttempts = Mathf.Clamp(MaxAttempts, 1, 512);
            CandidatesToScore = Mathf.Clamp(CandidatesToScore, 1, 32);
            ClosureReserveFraction = Mathf.Clamp(ClosureReserveFraction, 0.05f, 0.4f);
            MetersPerRing = Mathf.Clamp(MetersPerRing, 0.5f, 16f);
            MaxFacetAngleDegrees = Mathf.Clamp(MaxFacetAngleDegrees, 0.1f, 2f);
            TargetTotalRings = Mathf.Clamp(TargetTotalRings, 2000, 150000);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Root settings object
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The level designer's actual track request: every value generation uses, grouped
    /// logically. Style presets INITIALIZE these values; nothing at runtime switches
    /// behavior on a preset or difficulty enum — the fields below are the whole story.
    /// </summary>
    [Serializable]
    public class TrackDesignerSettings
    {
        [Tooltip("Name of the style preset these settings were initialized from (display only).")]
        public string AppliedPresetName = "";

        [Tooltip("Set when any field was edited after the preset was applied — displayed as 'Custom — based on <preset>'.")]
        public bool ModifiedSincePreset;

        [Tooltip("Snapshot of the settings as they were right after the preset was applied (for Reset/Compare). Managed by the inspector.")]
        [SerializeField, HideInInspector] private string appliedPresetSnapshotJson = "";

        public TrackScaleSettings Scale = new TrackScaleSettings();
        public TrackLayoutSettings Layout = new TrackLayoutSettings();
        public TrackCornerSettings Corners = new TrackCornerSettings();
        public TrackRoadSettings Road = new TrackRoadSettings();
        public TrackTransitionSettings Transitions = new TrackTransitionSettings();
        public TrackElevationSettings Elevation = new TrackElevationSettings();
        public TrackFeatureSettings Features = new TrackFeatureSettings();
        public TrackQuarterSettings Quarters = new TrackQuarterSettings();
        public TrackGenerationBehaviorSettings Generation = new TrackGenerationBehaviorSettings();
        public TrackVisualSettings Visual = new TrackVisualSettings();

        /// <summary>JSON snapshot taken when a preset was applied (empty when none).</summary>
        public string AppliedPresetSnapshotJson
        {
            get
            {
                NormalizeAppliedPresetSnapshot();
                return appliedPresetSnapshotJson;
            }
            set => appliedPresetSnapshotJson = StripEmbeddedSnapshot(value);
        }

        /// <summary>
        /// Removes a recursively embedded snapshot from legacy serialized data. Older
        /// ToJson calls included this field, so every preset refresh nested the previous
        /// JSON again and permanently enlarged the component/Inspector payload.
        /// </summary>
        public bool NormalizeAppliedPresetSnapshot()
        {
            string normalized = StripEmbeddedSnapshot(appliedPresetSnapshotJson);
            if (string.Equals(normalized, appliedPresetSnapshotJson, StringComparison.Ordinal)) return false;
            appliedPresetSnapshotJson = normalized;
            return true;
        }

        private static string StripEmbeddedSnapshot(string json)
        {
            if (string.IsNullOrEmpty(json)) return json ?? "";

            const string key = "\"appliedPresetSnapshotJson\"";
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return json;
            int colon = json.IndexOf(':', keyIndex + key.Length);
            if (colon < 0) return json;
            int valueStart = colon + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;
            if (valueStart >= json.Length || json[valueStart] != '"') return json;

            bool escaped = false;
            int valueEnd = valueStart + 1;
            for (; valueEnd < json.Length; valueEnd++)
            {
                char c = json[valueEnd];
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"') break;
            }
            if (valueEnd >= json.Length) return json;

            // Already canonical: avoid an allocation on every Inspector comparison.
            if (valueEnd == valueStart + 1) return json;
            return string.Concat(json.Substring(0, valueStart), "\"\"", json.Substring(valueEnd + 1));
        }

        /// <summary>Ensures every group exists and is internally consistent.</summary>
        public void Sanitize()
        {
            NormalizeAppliedPresetSnapshot();
            (Scale ??= new TrackScaleSettings()).Sanitize();
            (Layout ??= new TrackLayoutSettings()).Sanitize();
            (Corners ??= new TrackCornerSettings()).Sanitize();
            (Road ??= new TrackRoadSettings()).Sanitize();
            (Transitions ??= new TrackTransitionSettings()).Sanitize();
            (Elevation ??= new TrackElevationSettings()).Sanitize();
            (Features ??= new TrackFeatureSettings()).Sanitize();
            (Quarters ??= new TrackQuarterSettings()).Sanitize();
            (Generation ??= new TrackGenerationBehaviorSettings()).Sanitize();
            (Visual ??= new TrackVisualSettings()).Sanitize();
        }

        /// <summary>Deep copy via a bounded JSON round-trip (all groups are plain serializable data).</summary>
        public TrackDesignerSettings Clone()
        {
            NormalizeAppliedPresetSnapshot();
            string snapshot = appliedPresetSnapshotJson;
            var clone = JsonUtility.FromJson<TrackDesignerSettings>(SerializeSettingsWithoutSnapshot(false));
            if (clone != null) clone.appliedPresetSnapshotJson = snapshot;
            return clone;
        }

        /// <summary>Serializes designer inputs for snapshots/comparison, excluding the retained snapshot itself.</summary>
        public string ToJson()
        {
            NormalizeAppliedPresetSnapshot();
            return SerializeSettingsWithoutSnapshot(false);
        }

        private string SerializeSettingsWithoutSnapshot(bool prettyPrint)
        {
            string snapshot = appliedPresetSnapshotJson;
            appliedPresetSnapshotJson = "";
            try
            {
                return JsonUtility.ToJson(this, prettyPrint);
            }
            finally
            {
                appliedPresetSnapshotJson = snapshot;
            }
        }
    }
}
