using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// The readable gameplay section types for the V1 macro generator.
    /// A macro section is ONE intentional gameplay unit ("90 degree banked curve"),
    /// no matter how many prism subdivisions its geometry uses internally.
    /// </summary>
    public enum TrackMacroSectionType
    {
        // Explicit values: GeneratedTrackSection lists are scene-serialized, so removed
        // members (the old 11–13 split/merge trio) must leave holes, never shift values.
        Straight = 0,
        WideStraight = 1,
        BoostStraight = 2,
        BankedCurve = 3,
        BankedHairpin = 4,
        SCurve = 5,
        Chicane = 6,
        JumpRamp = 7,
        AirGap = 8,
        LandingRamp = 9,
        RecoveryStraight = 10,
        Loop = 14,
        Corkscrew = 15,
        TunnelVariant = 16,
        BridgeVariant = 17,

        /// <summary>Full-revolution climbing/descending helix (parking-garage spiral). Exits directly above/below its entry.</summary>
        Spiral = 18,

        /// <summary>Half loop up to inverted + half twist back upright (Immelmann). Reverses heading, exits at the loop top height.</summary>
        HalfLoopTwist = 19,

        /// <summary>Fully closed pipe road: the cross-section closes gradually into a tube the craft can ride around, then reopens. Straight centerline in V1.</summary>
        FullPipe = 20,

        /// <summary>Intentional wallride corner: fully rounded bowl with the boosted, over-curled outside wall as the primary driving surface.</summary>
        WallrideTurn = 21,

        /// <summary>One continuous, quantized sequence of vertical-centerline and road-roll phases.</summary>
        RotationalEvent = 22
    }

    /// <summary>The independently accumulated rotational channel owned by one event phase.</summary>
    public enum RotationalPhaseAxis
    {
        VerticalCenterline,
        RoadRoll
    }

    /// <summary>Sign of a quantized phase. The semantic name is supplied by the phase axis.</summary>
    public enum RotationalPhaseDirection
    {
        Negative = -1,
        Positive = 1
    }

    /// <summary>Approved overlap between adjacent phase envelopes.</summary>
    public enum RotationalBlendPreset
    {
        None,
        Short,
        Medium,
        Long
    }

    /// <summary>
    /// One ordered phase inside a continuous rotational road event. Intentional rotation
    /// is stored only as integer 90-degree units; the two halves are shaping regions, not
    /// path/mesh boundaries.
    /// </summary>
    [Serializable]
    public class RotationalPhaseDefinition
    {
        public RotationalPhaseAxis Axis;
        public RotationalPhaseDirection Direction = RotationalPhaseDirection.Positive;

        [Min(1)] public int RotationUnits = 4;

        [Min(1f)] public float FirstHalfLength = 100f;
        [Min(1f)] public float SecondHalfLength = 100f;

        [Min(1f)] public float FirstHalfRadius = 100f;
        [Min(1f)] public float SecondHalfRadius = 100f;

        [Tooltip("Independent plan-view heading change accumulated during this phase.")]
        public float HorizontalTurnDegrees;

        [Tooltip("Smooth temporary plan-view yaw in the first shaping half. Returns to zero at the midpoint, offsetting folded geometry without forcing the exit heading.")]
        public float FirstHalfYawBiasDegrees;

        [Tooltip("Smooth temporary plan-view yaw in the second shaping half. Returns to zero at phase exit.")]
        public float SecondHalfYawBiasDegrees;

        [Tooltip("Independent non-inversion pitch drift accumulated during this phase.")]
        public float VerticalDriftDegrees;

        [Tooltip("Smooth temporary pitch bias in the first shaping half. Returns to zero at the midpoint, changing elevation without forcing the exit pitch.")]
        public float FirstHalfPitchBiasDegrees;

        [Tooltip("Smooth temporary pitch bias in the second shaping half. Returns to zero at phase exit.")]
        public float SecondHalfPitchBiasDegrees;

        [Tooltip("Peak synchronized centerline-orbit tangent angle for a road-roll phase. Zero is a flat barrel roll; a positive value makes a true rising/falling corkscrew whose curvature rotates with the road surface and returns to a level exit.")]
        [Min(0f)] public float CenterlineOrbitDegrees;

        [Tooltip("Horizontal curvature carried out of the complete rotational event, in radians per meter. Only the final phase's value is used.")]
        public float ExitHorizontalCurvature;

        [Tooltip("Vertical curvature carried out of the complete rotational event, in radians per meter. Only the final phase's value is used.")]
        public float ExitVerticalCurvature;

        [Tooltip("Intentional road-roll rate carried out of the complete rotational event, in degrees per meter. Only the final phase's value is used.")]
        public float ExitRoadRollRate;

        [Tooltip("Road width reached at the end of this phase. Zero preserves the incoming width.")]
        public float ExitWidth;

        public RotationalBlendPreset BlendToNext = RotationalBlendPreset.Medium;

        public float Length => Mathf.Max(1f, FirstHalfLength) + Mathf.Max(1f, SecondHalfLength);
        public int Sign => Direction == RotationalPhaseDirection.Positive ? 1 : -1;
        public float Degrees(float unitDegrees) => Sign * Mathf.Max(1, RotationUnits) * unitDegrees;
    }

    /// <summary>Turn direction of a section (None for straight-family pieces).</summary>
    public enum SectionTurnDirection
    {
        None,
        Left,
        Right
    }

    /// <summary>Intended speed through the section — pacing metadata for the layout brain.</summary>
    public enum SectionSpeedIntent
    {
        Slow,
        Medium,
        Fast,
        FullThrottle
    }

    /// <summary>How dangerous the section is meant to feel.</summary>
    public enum SectionRiskLevel
    {
        Safe,
        Normal,
        Risky,
        Extreme
    }

    /// <summary>
    /// Readable orientation tag for connection-contract validation. The actual frames
    /// and quaternions are the source of truth; tags make grammar checks understandable.
    /// </summary>
    public enum TrackOrientationTag
    {
        Upright,
        Inverted,
        VerticalAscending,
        VerticalDescending,
        RollRecoveryRequired,
        Any
    }

    /// <summary>Connection contract of one section: what it needs at entry, what it delivers at exit.</summary>
    [Serializable]
    public struct SectionConnectionContract
    {
        public TrackOrientationTag RequiredEntryOrientation;
        public TrackOrientationTag ExitOrientation;

        public float HeadingDeltaDegrees;
        public float PitchDeltaDegrees;
        public float RollDeltaDegrees;
        public float ElevationDelta;

        public bool ClosureCompatible;

        public static SectionConnectionContract Level(float headingDelta = 0f) => new SectionConnectionContract
        {
            RequiredEntryOrientation = TrackOrientationTag.Upright,
            ExitOrientation = TrackOrientationTag.Upright,
            HeadingDeltaDegrees = headingDelta,
            ClosureCompatible = true
        };
    }

    /// <summary>
    /// Data definition for one readable gameplay section — the plan, not the geometry.
    /// Produced by the topology planner and feature patterns, consumed by the frame layout
    /// and <see cref="BoxPrismTrackMeshBuilder"/>.
    /// </summary>
    [Serializable]
    public class TrackMacroSectionDefinition
    {
        [Tooltip("The macro gameplay type of this section.")]
        public TrackMacroSectionType SectionType;

        [Tooltip("Length along the driving line in meters (arc length for curves).")]
        public float Length;

        [Tooltip("Road width in meters.")]
        public float Width;

        [Tooltip("Turn direction. None for straight-family sections.")]
        public SectionTurnDirection Direction;

        [Tooltip("Total turn angle in degrees (per sub-arc for S-curves/chicanes).")]
        public float TurnAngle;

        [Tooltip("Turn radius in meters (curves only).")]
        public float Radius;

        [Tooltip("Corkscrew only: radius of the second half. Zero uses Radius for both halves.")]
        public float SecondaryRadius;

        [Tooltip("Full-hold banking angle in degrees (curves only).")]
        public float BankingAngle;

        [Tooltip("Vertical change across the section in meters (ramp height for jump pieces).")]
        public float ElevationChange;

        [Tooltip("Pitch change across the section in degrees (jump/landing ramps).")]
        public float PitchChange;

        [Tooltip("Roll change across the section in degrees (future loops/corkscrews; 0 in V1).")]
        public float RollChange;

        [Tooltip("Number of prism subdivisions used to build this section's geometry.")]
        public int SubdivisionCount;

        [Tooltip("Intended speed through this section.")]
        public SectionSpeedIntent SpeedIntent = SectionSpeedIntent.Fast;

        [Tooltip("How dangerous this section is meant to feel.")]
        public SectionRiskLevel RiskLevel = SectionRiskLevel.Normal;

        [Tooltip("Whether the layout must place a recovery straight after this section.")]
        public bool RequiresRecoveryAfter;

        [Tooltip("Whether boost content (pads, lanes, gates) may be placed here later.")]
        public bool AllowsBoost;

        [Tooltip("Whether a jump may be placed on/over this section.")]
        public bool AllowsJump;

        [Tooltip("Human-readable name for debugging and hierarchy naming.")]
        public string DebugName;

        [Tooltip("When true, the closure solver may not change this section's length (safety approaches, recovery zones).")]
        public bool LockLength;

        [Tooltip("Net-zero vertical bump inside this section (meters): + = hill/bridge crest, - = dip/underpass. Ends return to entry height.")]
        public float HillHeight;

        [Tooltip("Quarter of the lap this def belongs to (0..3), stamped by the planner. -1 before stamping.")]
        public int QuarterIndex = -1;

        [Tooltip("Road within the quarter: 0 = canonical road (route A), 1 = alternate road (route B) of a Dual Road Quarter.")]
        public int RoadId;

        [Tooltip("AirGap only: signed lateral displacement of the landing relative to the launch lip, along the lip's right vector (meters). Dual-quarter choice jumps land offset from the flight midline; ordinary jumps leave this 0.")]
        public float PlanLateralOffset;

        [Tooltip("Feature pattern instance this section belongs to (empty for plain sections). Sections sharing a PatternId form one atomic group that bypasses ordinary spacing internally.")]
        public string PatternId;

        [Tooltip("Jump chain only: ballistic airtime of the gap in seconds (AirGap) — the gap distance is derived from the trajectory, never random.")]
        public float AirtimeSeconds;

        [Tooltip("Jump ramps only: the intermediate launch/landing shaping pitch (degrees). Launch ramps keep this between level and PitchChange so pitch never falls before the lip.")]
        public float SecondaryPitchDeg;

        [Tooltip("Corkscrew only: normalized straight shoulder held before the roll begins (0..0.08). Zero preserves the classic immediate roll-in.")]
        public float FeatureEntryStraightFraction;

        [Tooltip("Corkscrew only: normalized straight shoulder held after the roll completes (0..0.08). Zero preserves the classic immediate roll-out.")]
        public float FeatureExitStraightFraction;

        [Tooltip("Horizontal (plan-view) run of this section along its entry heading, for pitched sections whose arc length exceeds their footprint (ramps, air gaps). 0 = same as Length.")]
        public float PlanHorizontalLength;

        [Tooltip("True for sections created by the closure solver (adjustable straights, closure curves).")]
        public bool IsClosure;

        [Tooltip("FullPipe only: normalized position along the section where the closure completes (0..1). The cross-section morphs half-pipe → tube over this span — never over one or two samples.")]
        public float PipeCloseFraction;

        [Tooltip("FullPipe only: normalized position where the reopening begins (0..1).")]
        public float PipeOpenFraction;

        [Tooltip("Connector classification set by the connector analysis (meaningful for straight-family sections between content). Default BlendNeighbours is assigned by the analyzer.")]
        public ConnectorBehavior ConnectorBehavior;

        [Tooltip("GRADED turn-complex carry 0..1 set by the connector analysis: 1 = short link, neighbours' support holds fully through it; fades smoothly to 0 as the connector approaches ~2.5× the bridge window. Never a binary cliff — a 795m link must not behave differently from a 793m one.")]
        public float BridgeCarry;

        [Tooltip("Minimum legal length for this section (meters). The closure solver and budget fitter never shrink it below this. 0 = generic floor.")]
        public float MinimumLength;

        [Tooltip("Turn-complex this section belongs to (same-direction turn bridges group their two corners + connector). Empty for standalone sections. Reporting/surface-pass metadata — does NOT change pattern atomicity.")]
        public string TurnComplexId;

        [Tooltip("Authoritative semantic identity of the element this section belongs to (Stage A). Default None = legacy data; consumers fall back to PatternId/type inference for None.")]
        public SemanticElementId SemanticElement;

        [Tooltip("Orientation/heading contract of this section, used by the sequence-grammar validator.")]
        public SectionConnectionContract Contract;

        [Tooltip("RotationalEvent only: ordered quantized phases integrated as one continuous road/frame stream.")]
        public List<RotationalPhaseDefinition> RotationalPhases = new List<RotationalPhaseDefinition>();

        /// <summary>Horizontal plan-view run (falls back to Length for flat sections).</summary>
        public float HorizontalRun => PlanHorizontalLength > 0.001f ? PlanHorizontalLength : Length;

        /// <summary>True for pieces whose driving line is a straight line (incl. tunnel/bridge variants).</summary>
        public bool IsStraightFamily =>
            SectionType == TrackMacroSectionType.Straight ||
            SectionType == TrackMacroSectionType.WideStraight ||
            SectionType == TrackMacroSectionType.BoostStraight ||
            SectionType == TrackMacroSectionType.RecoveryStraight ||
            SectionType == TrackMacroSectionType.TunnelVariant ||
            SectionType == TrackMacroSectionType.BridgeVariant;

        /// <summary>True for the jump chain pieces (ramp, gap, landing).</summary>
        public bool IsJumpFamily =>
            SectionType == TrackMacroSectionType.JumpRamp ||
            SectionType == TrackMacroSectionType.AirGap ||
            SectionType == TrackMacroSectionType.LandingRamp;

        /// <summary>Signed turn: +1 right, -1 left, 0 none.</summary>
        public int TurnSign => Direction == SectionTurnDirection.Right ? 1 : (Direction == SectionTurnDirection.Left ? -1 : 0);

        public override string ToString() => $"{DebugName} ({SectionType}, L={Length:F0}m)";
    }
}
