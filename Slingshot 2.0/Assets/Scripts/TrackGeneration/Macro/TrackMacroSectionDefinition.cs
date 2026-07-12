using System;
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
        Straight,
        WideStraight,
        BoostStraight,
        BankedCurve,
        BankedHairpin,
        SCurve,
        Chicane,
        JumpRamp,
        AirGap,
        LandingRamp,
        RecoveryStraight,
        SplitEntry,
        SplitRoute,
        MergeExit,
        Loop,
        Corkscrew,
        TunnelVariant,
        BridgeVariant,

        /// <summary>Full-revolution climbing/descending helix (parking-garage spiral). Exits directly above/below its entry.</summary>
        Spiral,

        /// <summary>Half loop up to inverted + half twist back upright (Immelmann). Reverses heading, exits at the loop top height.</summary>
        HalfLoopTwist
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
    /// Data definition for one readable gameplay section — the plan, not the geometry.
    /// Produced by <see cref="MacroTrackLayoutGenerator"/>, consumed by the frame layout
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

        [Tooltip("SplitRoute only: lateral offset from the group centerline at the split end (meters).")]
        public float RouteLateralStart;

        [Tooltip("SplitRoute only: lateral offset from the group centerline at the merge end (meters). Different sign from start = routes cross over/under.")]
        public float RouteLateralEnd;

        [Tooltip("SplitRoute only: normalized zone breakpoints set by the layout — [lateralSepEnd, vertDivStart, vertDivEnd, vertConvStart, vertConvEnd, lateralMergeStart]. Used by debug visualization.")]
        public float[] RouteZoneBoundaries;

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
