using System;
using System.Collections.Generic;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using UnityEngine;

namespace TrackGeneration.Planning
{
    /// <summary>One road through a quarter: a contiguous run of built sections.</summary>
    [Serializable]
    public class GeneratedQuarterRoute
    {
        [Tooltip("0 = canonical road (route A, part of the lap-length line), 1 = alternate road (route B).")]
        public int RoadId;

        [Tooltip("Index of the road's first section in the layout's flat section list.")]
        public int FirstSectionIndex = -1;

        [Tooltip("Index of the road's last section in the layout's flat section list.")]
        public int LastSectionIndex = -1;

        [Tooltip("Physical centerline length of the road in meters.")]
        public float PhysicalLengthMeters;

        [Tooltip("Predicted neutral-craft traversal time in seconds.")]
        public float EstimatedNeutralTimeSeconds;

        [Tooltip("Distinct non-gate feature pattern IDs carried by this road. Empty means the route is feature-free.")]
        public List<string> FeaturePatternIds = new List<string>();

        [Tooltip("The road's first frame (landing mouth for dual-quarter roads).")]
        public TrackConnectionFrame EntryFrame;

        [Tooltip("The road's last frame (launch lip for dual-quarter roads).")]
        public TrackConnectionFrame ExitFrame;

        public bool HasFeatures => FeaturePatternIds != null && FeaturePatternIds.Count > 0;
    }

    /// <summary>
    /// One authoritative quarter of the generated lap (always exactly 4 per track).
    /// Quarters are defined by LOGICAL lap progress (0–25–50–75–100%), not equal
    /// physical distance. A quarter's physical length counts road A only — the player
    /// rides one road, so a Dual Road Quarter never duplicates lap length.
    /// </summary>
    [Serializable]
    public class GeneratedTrackQuarter
    {
        public int QuarterIndex;
        public TrackQuarterType QuarterType;

        [Tooltip("Logical lap progress where this quarter begins (0, 0.25, 0.5, 0.75).")]
        public float LogicalProgressStart;

        [Tooltip("Logical lap progress where this quarter ends (0.25, 0.5, 0.75, 1).")]
        public float LogicalProgressEnd;

        [Tooltip("The canonical road. Always populated.")]
        public GeneratedQuarterRoute RouteA = new GeneratedQuarterRoute();

        [Tooltip("The alternate road. Populated only for DualRoad quarters.")]
        public GeneratedQuarterRoute RouteB;

        [Tooltip("How the route choice is presented (v1: always JumpSelection).")]
        public DualQuarterChoiceType ChoiceType = DualQuarterChoiceType.JumpSelection;

        [Tooltip("Lateral separation between the two landing mouths / launch lips, meters. Dual only.")]
        public float LaneSeparationMeters;

        [Tooltip("Frame where the quarter begins on the canonical line (the entry boundary).")]
        public TrackConnectionFrame EntryFrame;

        [Tooltip("Frame where the quarter ends on the canonical line (the exit boundary).")]
        public TrackConnectionFrame ExitFrame;

        [Tooltip("Archetype time-table balance verdict. Dual only.")]
        public QuarterRouteBalance Balance;

        [Tooltip("Per-quarter lock for partial regeneration (staged; v1 honors Unlocked/FullyLocked).")]
        public QuarterLockMode LockMode = QuarterLockMode.Unlocked;

        public bool IsDual => QuarterType == TrackQuarterType.DualRoad;
    }

    /// <summary>
    /// Plan-side record of one quarter, carried through the topology plan while defs
    /// are being emitted and the alternate road is fitted. Frames come later (builder).
    /// </summary>
    public class PlannedQuarter
    {
        public int Index;
        public bool Dual;

        /// <summary>Gap-index span [GapStart, GapEnd) of the corner plan this quarter covers.</summary>
        public int GapStart;
        public int GapEnd;

        /// <summary>Def-index range of the quarter's canonical content (filled during emission).</summary>
        public int FirstDefIndex = -1;
        public int LastDefIndex = -1;

        /// <summary>Def index where the fitted road B chain is inserted (after road A's exit lip).</summary>
        public int RoadBInsertIndex = -1;

        /// <summary>Ballistic solutions for the entry choice jump and the exit convergence jump. Dual only.</summary>
        public JumpBallistics.Solution EntryJump;
        public JumpBallistics.Solution ExitJump;

        /// <summary>Lateral separation between the two landing mouths (== lip separation in v1).</summary>
        public float LaneSeparation;

        /// <summary>Planned centerline lengths (road A = quarter content between entry flare and exit lip).</summary>
        public float RoadALength;
        public float RoadBLength;

        /// <summary>Plan-time balance verdict from provisional frames. Dual only.</summary>
        public QuarterRouteBalance Balance;
    }
}
