using UnityEngine;

namespace TrackGeneration.Core
{
    /// <summary>
    /// The hard TECHNICAL RULEBOOK for procedural track generation: absolute legal
    /// limits, physical safety constraints, mesh/collider budgets, clearances,
    /// maximum counts, supported features, validation tolerances and the reference
    /// physics assumptions used for generation estimates.
    ///
    /// It never describes the personality of a particular track — that lives in
    /// <see cref="TrackGeneration.Design.TrackDesignerSettings"/> on the generator.
    /// Time-based limits are in seconds and convert to meters via the requested
    /// design speed (1300 km/h ≈ 361 m/s → 1 s ≈ 361 m).
    /// </summary>
    [CreateAssetMenu(fileName = "TrackConfig", menuName = "Track/Generation Config")]
    public class TrackConfig : ScriptableObject
    {
        // ══════════════════ Global technical limits ══════════════════

        [Header("Global Technical Limits")]

        [Tooltip("Lowest design speed the generator supports (km/h).")]
        [SerializeField] private float minDesignSpeedKph = 400f;

        [Tooltip("Highest design speed the generator supports (km/h).")]
        [SerializeField] private float maxDesignSpeedKph = 2000f;

        [Tooltip("Shortest target lap time (seconds).")]
        [SerializeField] private float minTargetLapTime = 20f;

        [Tooltip("Longest target lap time (seconds).")]
        [SerializeField] private float maxTargetLapTime = 180f;

        [Tooltip("Minimum total lap length (meters).")]
        [SerializeField] private float minTrackLength = 6000f;

        [Tooltip("Absolute maximum lap length (meters).")]
        [SerializeField] private float maxTrackLength = 60000f;

        [Tooltip("Minimum road width (meters).")]
        [SerializeField] private float minRoadWidth = 12f;

        [Tooltip("Maximum road width (meters).")]
        [SerializeField] private float maxRoadWidth = 60f;

        [Tooltip("Absolute minimum curve radius (meters). Rulebook lower bound — presets use safer values.")]
        [SerializeField] private float minCurveRadius = 180f;

        [Tooltip("Absolute maximum curve radius (meters).")]
        [SerializeField] private float maxCurveRadius = 6000f;

        [Tooltip("Maximum bank angle (degrees).")]
        [Range(0f, 85f)]
        [SerializeField] private float maxBankAngle = 82f;

        [Tooltip("Maximum ordinary climb angle (degrees).")]
        [Range(5f, 60f)]
        [SerializeField] private float maxClimbAngle = 45f;

        [Tooltip("Maximum ordinary drop angle (degrees).")]
        [Range(5f, 60f)]
        [SerializeField] private float maxDropAngle = 45f;

        [Tooltip("Maximum total elevation range of the lap (meters).")]
        [SerializeField] private float maxElevationRange = 1200f;

        [Tooltip("Minimum vertical clearance wherever unrelated track passes over track (meters). Wall heights and slab thickness are added on top during validation.")]
        [SerializeField] private float minVerticalClearance = 35f;

        // ══════════════════ Allowed features ══════════════════

        [Header("Allowed Features (project build support)")]

        [SerializeField] private bool allowJumps = true;
        [SerializeField] private bool allowLoops = true;
        [SerializeField] private bool allowCorkscrews = true;
        [SerializeField] private bool allowSpirals = true;
        [SerializeField] private bool allowHalfLoops = true;
        [SerializeField] private bool allowBranches = true;
        [SerializeField] private bool allowBridges = true;
        [SerializeField] private bool allowUnderpasses = true;

        // ══════════════════ Straight & pacing limits (seconds) ══════════════════

        [Header("Straight & Pacing Limits (seconds at design speed)")]

        [Tooltip("Ordinary straight duration window (seconds). 0.30 s ≈ 108 m, 8 s ≈ 2889 m at 1300 km/h.")]
        [SerializeField] private float minStraightSeconds = 0.30f;
        [SerializeField] private float maxStraightSeconds = 8.0f;

        [Tooltip("Boost straight duration window (seconds).")]
        [SerializeField] private float minBoostStraightSeconds = 0.80f;
        [SerializeField] private float maxBoostStraightSeconds = 6.0f;

        [Tooltip("Recovery duration window after dangerous features (seconds).")]
        [SerializeField] private float minRecoverySeconds = 0.70f;
        [SerializeField] private float maxRecoverySeconds = 3.0f;

        [Tooltip("Generic feature approach window (seconds).")]
        [SerializeField] private float minApproachSeconds = 0.80f;
        [SerializeField] private float maxApproachSeconds = 4.0f;

        [Tooltip("Spacing between unrelated dangerous features (seconds).")]
        [SerializeField] private float minDangerousSpacingSeconds = 1.20f;
        [SerializeField] private float maxDangerousSpacingSeconds = 5.0f;

        [Tooltip("Major-feature visual preview window (seconds).")]
        [SerializeField] private float minVisualPreviewSeconds = 1.20f;
        [SerializeField] private float maxVisualPreviewSeconds = 4.0f;

        // ══════════════════ Transition limits (seconds) ══════════════════

        [Header("Transition Limits (seconds at design speed)")]

        [SerializeField] private float minGenericTransitionSeconds = 0.20f;
        [SerializeField] private float maxGenericTransitionSeconds = 3.5f;

        [SerializeField] private float minBankTransitionSeconds = 0.35f;
        [SerializeField] private float maxBankTransitionSeconds = 3.0f;

        [SerializeField] private float minPitchTransitionSeconds = 0.45f;
        [SerializeField] private float maxPitchTransitionSeconds = 3.5f;

        [SerializeField] private float minRollTransitionSeconds = 0.60f;
        [SerializeField] private float maxRollTransitionSeconds = 4.0f;

        [SerializeField] private float minWidthTransitionSeconds = 0.35f;
        [SerializeField] private float maxWidthTransitionSeconds = 2.5f;

        [SerializeField] private float minCrossSectionTransitionSeconds = 0.45f;
        [SerializeField] private float maxCrossSectionTransitionSeconds = 3.0f;

        [Tooltip("Maximum effective ramp angle a bank transition may create (degrees). Transitions auto-expand to respect this; otherwise the candidate is rejected with a transition-rate failure.")]
        [Range(2f, 15f)]
        [SerializeField] private float maxBankRampAngle = 6f;

        // ══════════════════ Loop limits ══════════════════

        [Header("Loop Limits")]

        [Tooltip("Full-loop mid-arc radius window (meters).")]
        [SerializeField] private float minLoopRadius = 160f;
        [SerializeField] private float maxLoopRadius = 420f;

        [Tooltip("Loop approach window (seconds).")]
        [SerializeField] private float minLoopApproachSeconds = 1.5f;
        [SerializeField] private float maxLoopApproachSeconds = 3.0f;

        [Tooltip("Loop recovery window (seconds).")]
        [SerializeField] private float minLoopRecoverySeconds = 1.0f;
        [SerializeField] private float maxLoopRecoverySeconds = 2.5f;

        [Tooltip("Minimum clear space around a loop volume (meters).")]
        [SerializeField] private float minLoopClearance = 45f;

        // ══════════════════ Half-loop limits ══════════════════

        [Header("Half-Loop Limits")]

        [SerializeField] private float minHalfLoopRadius = 180f;
        [SerializeField] private float maxHalfLoopRadius = 420f;

        [Tooltip("Half-loop rollout duration window (seconds). Default preset value ≈ 2.2 s ≈ 794 m at 1300 km/h.")]
        [SerializeField] private float minHalfLoopRolloutSeconds = 1.5f;
        [SerializeField] private float maxHalfLoopRolloutSeconds = 3.5f;

        [SerializeField] private float minHalfLoopApproachSeconds = 1.5f;
        [SerializeField] private float maxHalfLoopApproachSeconds = 3.0f;
        [SerializeField] private float minHalfLoopRecoverySeconds = 1.0f;
        [SerializeField] private float maxHalfLoopRecoverySeconds = 2.5f;

        // ══════════════════ Corkscrew limits ══════════════════

        [Header("Corkscrew Limits")]

        [Tooltip("Corkscrew duration window (seconds). 1.5 s ≈ 542 m, 5.5 s ≈ 1986 m at 1300 km/h.")]
        [SerializeField] private float minCorkscrewSeconds = 1.5f;
        [SerializeField] private float maxCorkscrewSeconds = 5.5f;

        [Tooltip("Corkscrew helix radius window (meters).")]
        [SerializeField] private float minCorkscrewRadius = 60f;
        [SerializeField] private float maxCorkscrewRadius = 260f;

        [Tooltip("Allowed total roll through a corkscrew (degrees).")]
        [SerializeField] private float minCorkscrewRollDegrees = 180f;
        [SerializeField] private float maxCorkscrewRollDegrees = 720f;

        [Tooltip("Maximum roll rate anywhere on the track (degrees per second at design speed).")]
        [SerializeField] private float maxRollRateDegreesPerSecond = 220f;

        [SerializeField] private float minCorkscrewApproachSeconds = 1.3f;
        [SerializeField] private float maxCorkscrewApproachSeconds = 3.0f;
        [SerializeField] private float minCorkscrewRecoverySeconds = 1.0f;
        [SerializeField] private float maxCorkscrewRecoverySeconds = 2.5f;

        [Tooltip("Minimum clear space around a corkscrew volume (meters).")]
        [SerializeField] private float minCorkscrewClearance = 40f;

        // ══════════════════ Spiral limits ══════════════════

        [Header("Spiral Limits")]

        [Tooltip("Spiral helix radius window (meters) — dedicated rulebook values, never derived from road width alone.")]
        [SerializeField] private float minSpiralRadius = 120f;
        [SerializeField] private float maxSpiralRadius = 700f;

        [Tooltip("Full revolutions a spiral may make.")]
        [SerializeField] private int minSpiralRevolutions = 1;
        [SerializeField] private int maxSpiralRevolutions = 3;

        [Tooltip("Climb (or descent) per revolution (meters).")]
        [SerializeField] private float minSpiralClimbPerRevolution = 35f;
        [SerializeField] private float maxSpiralClimbPerRevolution = 140f;

        [SerializeField] private float minSpiralApproachSeconds = 1.2f;
        [SerializeField] private float maxSpiralApproachSeconds = 3.0f;
        [SerializeField] private float minSpiralRecoverySeconds = 1.0f;
        [SerializeField] private float maxSpiralRecoverySeconds = 2.5f;

        [Tooltip("Minimum clearance between spiral coils and to external geometry (meters).")]
        [SerializeField] private float minSpiralClearance = 40f;

        // ══════════════════ Jump limits (ballistic model) ══════════════════

        [Header("Jump Limits (ballistic design model)")]

        [SerializeField] private float minJumpApproachSeconds = 1.3f;
        [SerializeField] private float maxJumpApproachSeconds = 3.0f;

        [Tooltip("Launch transition duration window (seconds) — the pitch-up ramp itself.")]
        [SerializeField] private float minLaunchTransitionSeconds = 0.35f;
        [SerializeField] private float maxLaunchTransitionSeconds = 1.2f;

        [Tooltip("Target airtime window (seconds). Air-gap distance is derived from the ballistic trajectory at design speed, never chosen randomly.")]
        [SerializeField] private float minJumpAirtimeSeconds = 0.25f;
        [SerializeField] private float maxJumpAirtimeSeconds = 1.2f;

        [Tooltip("Landing transition duration window (seconds).")]
        [SerializeField] private float minLandingTransitionSeconds = 0.50f;
        [SerializeField] private float maxLandingTransitionSeconds = 1.8f;

        [SerializeField] private float minJumpRecoverySeconds = 1.0f;
        [SerializeField] private float maxJumpRecoverySeconds = 2.5f;

        [Tooltip("Launch lip height window (meters).")]
        [SerializeField] private float minJumpHeight = 3f;
        [SerializeField] private float maxJumpHeight = 80f;

        [Tooltip("Tolerance between the predicted ballistic arrival and the landing surface (meters).")]
        [SerializeField] private float jumpLandingTolerance = 4f;

        // ══════════════════ Branch limits ══════════════════

        [Header("Branch Limits")]

        [Tooltip("Maximum branch groups per track.")]
        [SerializeField] private int maxBranchGroupsPerTrack = 5;

        [SerializeField] private float minDecisionPreviewSeconds = 1.5f;
        [SerializeField] private float maxDecisionPreviewSeconds = 4.0f;

        [Tooltip("Branch route duration window from fork to merge (seconds).")]
        [SerializeField] private float minBranchRouteSeconds = 4.0f;
        [SerializeField] private float maxBranchRouteSeconds = 18.0f;

        [SerializeField] private float minLateralSplitSeconds = 0.8f;
        [SerializeField] private float maxLateralSplitSeconds = 3.0f;

        [SerializeField] private float minVerticalDivergenceDelaySeconds = 0.3f;
        [SerializeField] private float maxVerticalDivergenceDelaySeconds = 1.5f;

        [SerializeField] private float minVerticalDivergenceSeconds = 0.8f;
        [SerializeField] private float maxVerticalDivergenceSeconds = 3.5f;

        [SerializeField] private float minMergeSeconds = 0.8f;
        [SerializeField] private float maxMergeSeconds = 3.0f;

        [SerializeField] private float minPostMergeRecoverySeconds = 1.0f;
        [SerializeField] private float maxPostMergeRecoverySeconds = 3.0f;

        [Tooltip("Route centerline separation window (meters). Dynamically increased for road half-widths, wall heights, banking, lips, slab thickness, craft envelope and safety margin.")]
        [SerializeField] private float minRouteCenterlineSeparation = 40f;
        [SerializeField] private float maxRouteCenterlineSeparation = 300f;

        [Tooltip("Route vertical separation window for stacked/crossing patterns (meters).")]
        [SerializeField] private float minRouteVerticalSeparation = 35f;
        [SerializeField] private float maxRouteVerticalSeparation = 300f;

        [Tooltip("Neutral-craft route time balance tolerance window (fraction).")]
        [SerializeField] private float minTimeBalanceTolerance = 0.01f;
        [SerializeField] private float maxTimeBalanceTolerance = 0.06f;

        [Tooltip("Specialization advantage target window (fraction).")]
        [SerializeField] private float minSpecializationAdvantage = 0.01f;
        [SerializeField] private float maxSpecializationAdvantage = 0.10f;

        [Tooltip("Maximum paired-route interactions (crossovers, exchanges) per branch group.")]
        [SerializeField] private int maxPairedRouteInteractions = 5;

        // ══════════════════ Half-pipe limits ══════════════════

        [Header("Half-Pipe Cross-Section Limits")]

        [SerializeField] private float minHalfPipeSideHeight = 3f;
        [SerializeField] private float maxHalfPipeSideHeight = 14f;

        [SerializeField] private float minHalfPipeCurveStrength = 0.30f;
        [SerializeField] private float maxHalfPipeCurveStrength = 1.80f;

        [SerializeField] private float minHalfPipeWallAngle = 35f;
        [SerializeField] private float maxHalfPipeWallAngle = 82f;

        [SerializeField] private float minHalfPipeCenterFlatRatio = 0.15f;
        [SerializeField] private float maxHalfPipeCenterFlatRatio = 0.65f;

        [SerializeField] private int minHalfPipeProfileResolution = 8;
        [SerializeField] private int maxHalfPipeProfileResolution = 96;

        [SerializeField] private float minSafetyLipHeight = 0.5f;
        [SerializeField] private float maxSafetyLipHeight = 2.5f;

        // ══════════════════ Mesh & collision limits ══════════════════

        [Header("Mesh & Collision Limits")]

        [Tooltip("Ring spacing window for ordinary sections (meters per ring).")]
        [SerializeField] private float minMetersPerRing = 0.5f;
        [SerializeField] private float maxMetersPerRing = 8.0f;

        [Tooltip("Ring spacing window inside special features (meters per ring).")]
        [SerializeField] private float minFeatureMetersPerRing = 0.75f;
        [SerializeField] private float maxFeatureMetersPerRing = 4.0f;

        [Tooltip("Maximum facet-angle change between consecutive rings (degrees). 0.5° recommended at 1300 km/h.")]
        [SerializeField] private float minFacetAngle = 0.25f;
        [SerializeField] private float maxFacetAngle = 1.0f;

        [Tooltip("Maximum rings inside one macro section.")]
        [SerializeField] private int maxRingsPerMacroSection = 16384;

        [Tooltip("Maximum rings for the whole track.")]
        [SerializeField] private int maxTotalTrackRings = 150000;

        // ══════════════════ Validation tolerances ══════════════════

        [Header("Closure Validation Tolerances")]

        [Tooltip("Maximum allowed closure position error (meters).")]
        [SerializeField] private float closurePositionTolerance = 0.05f;

        [Tooltip("Maximum allowed closure forward-angle error (degrees).")]
        [SerializeField] private float closureForwardTolerance = 0.05f;

        [Tooltip("Maximum allowed closure up-angle error (degrees).")]
        [SerializeField] private float closureUpTolerance = 0.05f;

        [Tooltip("Maximum allowed closure width error (meters).")]
        [SerializeField] private float closureWidthTolerance = 0.01f;

        [Tooltip("Maximum allowed closure bank error (degrees).")]
        [SerializeField] private float closureBankTolerance = 0.05f;

        [Tooltip("Maximum allowed closure pitch error (degrees).")]
        [SerializeField] private float closurePitchTolerance = 0.05f;

        // ══════════════════ Reference performance model ══════════════════

        [Header("Reference Performance Model (generation estimates only — NOT craft physics)")]

        [Tooltip("Gravity used by ballistic jump design and speed estimates (m/s²).")]
        [SerializeField] private float referenceGravity = 9.81f;

        [Tooltip("Neutral reference top speed (km/h).")]
        [SerializeField] private float referenceTopSpeedKph = 1300f;

        [Tooltip("Reference longitudinal acceleration (m/s²).")]
        [SerializeField] private float referenceAcceleration = 45f;

        [Tooltip("Reference braking deceleration (m/s²).")]
        [SerializeField] private float referenceBraking = 70f;

        [Tooltip("Reference lateral acceleration capability on a FLAT road (m/s²). Banking multiplies effective capability.")]
        [SerializeField] private float referenceLateralAcceleration = 120f;

        [Tooltip("Reference roll stability: fraction of speed retained through heavy roll sections.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float referenceRollStability = 0.9f;

        [Tooltip("Reference landing recovery: fraction of speed retained through a jump landing.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float referenceLandingRecovery = 0.92f;

        [Header("Race")]

        [Tooltip("Default lap count for a race on generated tracks.")]
        [Range(1, 10)]
        [SerializeField] private int defaultLapCount = 3;

        // ══════════════════ Accessors ══════════════════

        public float MinDesignSpeedKph => minDesignSpeedKph;
        public float MaxDesignSpeedKph => maxDesignSpeedKph;
        public float MinTargetLapTime => minTargetLapTime;
        public float MaxTargetLapTime => maxTargetLapTime;
        public float MinTrackLength => minTrackLength;
        public float MaxTrackLength => maxTrackLength;
        public float MinRoadWidth => minRoadWidth;
        public float MaxRoadWidth => maxRoadWidth;
        public float MinCurveRadius => minCurveRadius;
        public float MaxCurveRadius => maxCurveRadius;
        public float MaxBankAngle => maxBankAngle;
        public float MaxClimbAngle => maxClimbAngle;
        public float MaxDropAngle => maxDropAngle;
        public float MaxElevationRange => maxElevationRange;
        public float MinVerticalClearance => minVerticalClearance;

        public bool AllowJumps => allowJumps;
        public bool AllowLoops => allowLoops;
        public bool AllowCorkscrews => allowCorkscrews;
        public bool AllowSpirals => allowSpirals;
        public bool AllowHalfLoops => allowHalfLoops;
        public bool AllowBranches => allowBranches;
        public bool AllowBridges => allowBridges;
        public bool AllowUnderpasses => allowUnderpasses;

        public float MinStraightSeconds => minStraightSeconds;
        public float MaxStraightSeconds => maxStraightSeconds;
        public float MinBoostStraightSeconds => minBoostStraightSeconds;
        public float MaxBoostStraightSeconds => maxBoostStraightSeconds;
        public float MinRecoverySeconds => minRecoverySeconds;
        public float MaxRecoverySeconds => maxRecoverySeconds;
        public float MinApproachSeconds => minApproachSeconds;
        public float MaxApproachSeconds => maxApproachSeconds;
        public float MinDangerousSpacingSeconds => minDangerousSpacingSeconds;
        public float MaxDangerousSpacingSeconds => maxDangerousSpacingSeconds;
        public float MinVisualPreviewSeconds => minVisualPreviewSeconds;
        public float MaxVisualPreviewSeconds => maxVisualPreviewSeconds;

        public float MinGenericTransitionSeconds => minGenericTransitionSeconds;
        public float MaxGenericTransitionSeconds => maxGenericTransitionSeconds;
        public float MinBankTransitionSeconds => minBankTransitionSeconds;
        public float MaxBankTransitionSeconds => maxBankTransitionSeconds;
        public float MinPitchTransitionSeconds => minPitchTransitionSeconds;
        public float MaxPitchTransitionSeconds => maxPitchTransitionSeconds;
        public float MinRollTransitionSeconds => minRollTransitionSeconds;
        public float MaxRollTransitionSeconds => maxRollTransitionSeconds;
        public float MinWidthTransitionSeconds => minWidthTransitionSeconds;
        public float MaxWidthTransitionSeconds => maxWidthTransitionSeconds;
        public float MinCrossSectionTransitionSeconds => minCrossSectionTransitionSeconds;
        public float MaxCrossSectionTransitionSeconds => maxCrossSectionTransitionSeconds;
        public float MaxBankRampAngle => maxBankRampAngle;

        public float MinLoopRadius => minLoopRadius;
        public float MaxLoopRadius => maxLoopRadius;
        public float MinLoopApproachSeconds => minLoopApproachSeconds;
        public float MaxLoopApproachSeconds => maxLoopApproachSeconds;
        public float MinLoopRecoverySeconds => minLoopRecoverySeconds;
        public float MaxLoopRecoverySeconds => maxLoopRecoverySeconds;
        public float MinLoopClearance => minLoopClearance;

        public float MinHalfLoopRadius => minHalfLoopRadius;
        public float MaxHalfLoopRadius => maxHalfLoopRadius;
        public float MinHalfLoopRolloutSeconds => minHalfLoopRolloutSeconds;
        public float MaxHalfLoopRolloutSeconds => maxHalfLoopRolloutSeconds;
        public float MinHalfLoopApproachSeconds => minHalfLoopApproachSeconds;
        public float MaxHalfLoopApproachSeconds => maxHalfLoopApproachSeconds;
        public float MinHalfLoopRecoverySeconds => minHalfLoopRecoverySeconds;
        public float MaxHalfLoopRecoverySeconds => maxHalfLoopRecoverySeconds;

        public float MinCorkscrewSeconds => minCorkscrewSeconds;
        public float MaxCorkscrewSeconds => maxCorkscrewSeconds;
        public float MinCorkscrewRadius => minCorkscrewRadius;
        public float MaxCorkscrewRadius => maxCorkscrewRadius;
        public float MinCorkscrewRollDegrees => minCorkscrewRollDegrees;
        public float MaxCorkscrewRollDegrees => maxCorkscrewRollDegrees;
        public float MaxRollRateDegreesPerSecond => maxRollRateDegreesPerSecond;
        public float MinCorkscrewApproachSeconds => minCorkscrewApproachSeconds;
        public float MaxCorkscrewApproachSeconds => maxCorkscrewApproachSeconds;
        public float MinCorkscrewRecoverySeconds => minCorkscrewRecoverySeconds;
        public float MaxCorkscrewRecoverySeconds => maxCorkscrewRecoverySeconds;
        public float MinCorkscrewClearance => minCorkscrewClearance;

        public float MinSpiralRadius => minSpiralRadius;
        public float MaxSpiralRadius => maxSpiralRadius;
        public int MinSpiralRevolutions => minSpiralRevolutions;
        public int MaxSpiralRevolutions => maxSpiralRevolutions;
        public float MinSpiralClimbPerRevolution => minSpiralClimbPerRevolution;
        public float MaxSpiralClimbPerRevolution => maxSpiralClimbPerRevolution;
        public float MinSpiralApproachSeconds => minSpiralApproachSeconds;
        public float MaxSpiralApproachSeconds => maxSpiralApproachSeconds;
        public float MinSpiralRecoverySeconds => minSpiralRecoverySeconds;
        public float MaxSpiralRecoverySeconds => maxSpiralRecoverySeconds;
        public float MinSpiralClearance => minSpiralClearance;

        public float MinJumpApproachSeconds => minJumpApproachSeconds;
        public float MaxJumpApproachSeconds => maxJumpApproachSeconds;
        public float MinLaunchTransitionSeconds => minLaunchTransitionSeconds;
        public float MaxLaunchTransitionSeconds => maxLaunchTransitionSeconds;
        public float MinJumpAirtimeSeconds => minJumpAirtimeSeconds;
        public float MaxJumpAirtimeSeconds => maxJumpAirtimeSeconds;
        public float MinLandingTransitionSeconds => minLandingTransitionSeconds;
        public float MaxLandingTransitionSeconds => maxLandingTransitionSeconds;
        public float MinJumpRecoverySeconds => minJumpRecoverySeconds;
        public float MaxJumpRecoverySeconds => maxJumpRecoverySeconds;
        public float MinJumpHeight => minJumpHeight;
        public float MaxJumpHeight => maxJumpHeight;
        public float JumpLandingTolerance => jumpLandingTolerance;

        public int MaxBranchGroupsPerTrack => maxBranchGroupsPerTrack;
        public float MinDecisionPreviewSeconds => minDecisionPreviewSeconds;
        public float MaxDecisionPreviewSeconds => maxDecisionPreviewSeconds;
        public float MinBranchRouteSeconds => minBranchRouteSeconds;
        public float MaxBranchRouteSeconds => maxBranchRouteSeconds;
        public float MinLateralSplitSeconds => minLateralSplitSeconds;
        public float MaxLateralSplitSeconds => maxLateralSplitSeconds;
        public float MinVerticalDivergenceDelaySeconds => minVerticalDivergenceDelaySeconds;
        public float MaxVerticalDivergenceDelaySeconds => maxVerticalDivergenceDelaySeconds;
        public float MinVerticalDivergenceSeconds => minVerticalDivergenceSeconds;
        public float MaxVerticalDivergenceSeconds => maxVerticalDivergenceSeconds;
        public float MinMergeSeconds => minMergeSeconds;
        public float MaxMergeSeconds => maxMergeSeconds;
        public float MinPostMergeRecoverySeconds => minPostMergeRecoverySeconds;
        public float MaxPostMergeRecoverySeconds => maxPostMergeRecoverySeconds;
        public float MinRouteCenterlineSeparation => minRouteCenterlineSeparation;
        public float MaxRouteCenterlineSeparation => maxRouteCenterlineSeparation;
        public float MinRouteVerticalSeparation => minRouteVerticalSeparation;
        public float MaxRouteVerticalSeparation => maxRouteVerticalSeparation;
        public float MinTimeBalanceTolerance => minTimeBalanceTolerance;
        public float MaxTimeBalanceTolerance => maxTimeBalanceTolerance;
        public float MinSpecializationAdvantage => minSpecializationAdvantage;
        public float MaxSpecializationAdvantage => maxSpecializationAdvantage;
        public int MaxPairedRouteInteractions => maxPairedRouteInteractions;

        public float MinHalfPipeSideHeight => minHalfPipeSideHeight;
        public float MaxHalfPipeSideHeight => maxHalfPipeSideHeight;
        public float MinHalfPipeCurveStrength => minHalfPipeCurveStrength;
        public float MaxHalfPipeCurveStrength => maxHalfPipeCurveStrength;
        public float MinHalfPipeWallAngle => minHalfPipeWallAngle;
        public float MaxHalfPipeWallAngle => maxHalfPipeWallAngle;
        public float MinHalfPipeCenterFlatRatio => minHalfPipeCenterFlatRatio;
        public float MaxHalfPipeCenterFlatRatio => maxHalfPipeCenterFlatRatio;
        public int MinHalfPipeProfileResolution => minHalfPipeProfileResolution;
        public int MaxHalfPipeProfileResolution => maxHalfPipeProfileResolution;
        public float MinSafetyLipHeight => minSafetyLipHeight;
        public float MaxSafetyLipHeight => maxSafetyLipHeight;

        public float MinMetersPerRing => minMetersPerRing;
        public float MaxMetersPerRing => maxMetersPerRing;
        public float MinFeatureMetersPerRing => minFeatureMetersPerRing;
        public float MaxFeatureMetersPerRing => maxFeatureMetersPerRing;
        public float MinFacetAngle => minFacetAngle;
        public float MaxFacetAngle => maxFacetAngle;
        public int MaxRingsPerMacroSection => maxRingsPerMacroSection;
        public int MaxTotalTrackRings => maxTotalTrackRings;

        public float ClosurePositionTolerance => closurePositionTolerance;
        public float ClosureForwardTolerance => closureForwardTolerance;
        public float ClosureUpTolerance => closureUpTolerance;
        public float ClosureWidthTolerance => closureWidthTolerance;
        public float ClosureBankTolerance => closureBankTolerance;
        public float ClosurePitchTolerance => closurePitchTolerance;

        public float ReferenceGravity => referenceGravity;
        public float ReferenceTopSpeedKph => referenceTopSpeedKph;
        public float ReferenceTopSpeedMps => referenceTopSpeedKph / 3.6f;
        public float ReferenceAcceleration => referenceAcceleration;
        public float ReferenceBraking => referenceBraking;
        public float ReferenceLateralAcceleration => referenceLateralAcceleration;
        public float ReferenceRollStability => referenceRollStability;
        public float ReferenceLandingRecovery => referenceLandingRecovery;

        public int DefaultLapCount => defaultLapCount;

        /// <summary>Clamps interdependent fields so rulebook invariants remain valid.</summary>
        public void Validate()
        {
            void Order(ref float min, ref float max) { if (min > max) min = max; }
            void OrderInt(ref int min, ref int max) { if (min > max) min = max; }

            Order(ref minDesignSpeedKph, ref maxDesignSpeedKph);
            Order(ref minTargetLapTime, ref maxTargetLapTime);
            Order(ref minTrackLength, ref maxTrackLength);
            Order(ref minRoadWidth, ref maxRoadWidth);
            Order(ref minCurveRadius, ref maxCurveRadius);

            Order(ref minStraightSeconds, ref maxStraightSeconds);
            Order(ref minBoostStraightSeconds, ref maxBoostStraightSeconds);
            Order(ref minRecoverySeconds, ref maxRecoverySeconds);
            Order(ref minApproachSeconds, ref maxApproachSeconds);
            Order(ref minDangerousSpacingSeconds, ref maxDangerousSpacingSeconds);
            Order(ref minVisualPreviewSeconds, ref maxVisualPreviewSeconds);

            Order(ref minGenericTransitionSeconds, ref maxGenericTransitionSeconds);
            Order(ref minBankTransitionSeconds, ref maxBankTransitionSeconds);
            Order(ref minPitchTransitionSeconds, ref maxPitchTransitionSeconds);
            Order(ref minRollTransitionSeconds, ref maxRollTransitionSeconds);
            Order(ref minWidthTransitionSeconds, ref maxWidthTransitionSeconds);
            Order(ref minCrossSectionTransitionSeconds, ref maxCrossSectionTransitionSeconds);

            Order(ref minLoopRadius, ref maxLoopRadius);
            Order(ref minLoopApproachSeconds, ref maxLoopApproachSeconds);
            Order(ref minLoopRecoverySeconds, ref maxLoopRecoverySeconds);

            Order(ref minHalfLoopRadius, ref maxHalfLoopRadius);
            Order(ref minHalfLoopRolloutSeconds, ref maxHalfLoopRolloutSeconds);
            Order(ref minHalfLoopApproachSeconds, ref maxHalfLoopApproachSeconds);
            Order(ref minHalfLoopRecoverySeconds, ref maxHalfLoopRecoverySeconds);

            Order(ref minCorkscrewSeconds, ref maxCorkscrewSeconds);
            Order(ref minCorkscrewRadius, ref maxCorkscrewRadius);
            Order(ref minCorkscrewRollDegrees, ref maxCorkscrewRollDegrees);
            Order(ref minCorkscrewApproachSeconds, ref maxCorkscrewApproachSeconds);
            Order(ref minCorkscrewRecoverySeconds, ref maxCorkscrewRecoverySeconds);

            Order(ref minSpiralRadius, ref maxSpiralRadius);
            OrderInt(ref minSpiralRevolutions, ref maxSpiralRevolutions);
            Order(ref minSpiralClimbPerRevolution, ref maxSpiralClimbPerRevolution);
            Order(ref minSpiralApproachSeconds, ref maxSpiralApproachSeconds);
            Order(ref minSpiralRecoverySeconds, ref maxSpiralRecoverySeconds);

            Order(ref minJumpApproachSeconds, ref maxJumpApproachSeconds);
            Order(ref minLaunchTransitionSeconds, ref maxLaunchTransitionSeconds);
            Order(ref minJumpAirtimeSeconds, ref maxJumpAirtimeSeconds);
            Order(ref minLandingTransitionSeconds, ref maxLandingTransitionSeconds);
            Order(ref minJumpRecoverySeconds, ref maxJumpRecoverySeconds);
            Order(ref minJumpHeight, ref maxJumpHeight);

            Order(ref minDecisionPreviewSeconds, ref maxDecisionPreviewSeconds);
            Order(ref minBranchRouteSeconds, ref maxBranchRouteSeconds);
            Order(ref minLateralSplitSeconds, ref maxLateralSplitSeconds);
            Order(ref minVerticalDivergenceDelaySeconds, ref maxVerticalDivergenceDelaySeconds);
            Order(ref minVerticalDivergenceSeconds, ref maxVerticalDivergenceSeconds);
            Order(ref minMergeSeconds, ref maxMergeSeconds);
            Order(ref minPostMergeRecoverySeconds, ref maxPostMergeRecoverySeconds);
            Order(ref minRouteCenterlineSeparation, ref maxRouteCenterlineSeparation);
            Order(ref minRouteVerticalSeparation, ref maxRouteVerticalSeparation);
            Order(ref minTimeBalanceTolerance, ref maxTimeBalanceTolerance);
            Order(ref minSpecializationAdvantage, ref maxSpecializationAdvantage);

            Order(ref minHalfPipeSideHeight, ref maxHalfPipeSideHeight);
            Order(ref minHalfPipeCurveStrength, ref maxHalfPipeCurveStrength);
            Order(ref minHalfPipeWallAngle, ref maxHalfPipeWallAngle);
            Order(ref minHalfPipeCenterFlatRatio, ref maxHalfPipeCenterFlatRatio);
            OrderInt(ref minHalfPipeProfileResolution, ref maxHalfPipeProfileResolution);
            Order(ref minSafetyLipHeight, ref maxSafetyLipHeight);

            Order(ref minMetersPerRing, ref maxMetersPerRing);
            Order(ref minFeatureMetersPerRing, ref maxFeatureMetersPerRing);
            Order(ref minFacetAngle, ref maxFacetAngle);

            minTrackLength = Mathf.Max(500f, minTrackLength);
            minRoadWidth = Mathf.Max(4f, minRoadWidth);
            minCurveRadius = Mathf.Max(20f, minCurveRadius);
            minVerticalClearance = Mathf.Max(5f, minVerticalClearance);
            maxBranchGroupsPerTrack = Mathf.Clamp(maxBranchGroupsPerTrack, 0, 8);
            maxPairedRouteInteractions = Mathf.Clamp(maxPairedRouteInteractions, 0, 8);
            minSpiralRevolutions = Mathf.Max(1, minSpiralRevolutions);
            maxSpiralRevolutions = Mathf.Clamp(maxSpiralRevolutions, minSpiralRevolutions, 6);
            maxRingsPerMacroSection = Mathf.Max(32, maxRingsPerMacroSection);
            maxTotalTrackRings = Mathf.Max(maxRingsPerMacroSection, maxTotalTrackRings);
            minHalfPipeProfileResolution = Mathf.Clamp(minHalfPipeProfileResolution, 3, 96);
            maxHalfPipeProfileResolution = Mathf.Clamp(maxHalfPipeProfileResolution, minHalfPipeProfileResolution, 96);
            referenceGravity = Mathf.Max(0.1f, referenceGravity);
            referenceTopSpeedKph = Mathf.Max(50f, referenceTopSpeedKph);

            closurePositionTolerance = Mathf.Max(0.001f, closurePositionTolerance);
            closureForwardTolerance = Mathf.Max(0.001f, closureForwardTolerance);
            closureUpTolerance = Mathf.Max(0.001f, closureUpTolerance);
            closureWidthTolerance = Mathf.Max(0.001f, closureWidthTolerance);
            closureBankTolerance = Mathf.Max(0.001f, closureBankTolerance);
            closurePitchTolerance = Mathf.Max(0.001f, closurePitchTolerance);
        }

        private void OnValidate()
        {
            Validate();
        }
    }
}
