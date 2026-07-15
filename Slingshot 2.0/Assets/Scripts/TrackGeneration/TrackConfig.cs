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
        private const int CurrentSchemaVersion = 2;

        [SerializeField, HideInInspector] private int schemaVersion;

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
        [SerializeField] private float maxTrackLength = 90000f;

        [Tooltip("Minimum road width (meters).")]
        [SerializeField] private float minRoadWidth = 12f;

        [Tooltip("Maximum road width (meters).")]
        [SerializeField] private float maxRoadWidth = 60f;

        [Tooltip("Absolute minimum curve radius (meters). Rulebook lower bound — presets use safer values.")]
        [SerializeField] private float minCurveRadius = 180f;

        [Tooltip("Absolute maximum curve radius (meters).")]
        [SerializeField] private float maxCurveRadius = 1200f;

        [Tooltip("Absolute maximum radius for solver-only closure curves. These do not affect the styled corner range.")]
        [SerializeField] private float maxClosureCurveRadius = 6000f;

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
        [SerializeField] private bool allowBridges = true;
        [SerializeField] private bool allowUnderpasses = true;
        [SerializeField] private bool allowFullPipes = true;
        [SerializeField] private bool allowWallRides = true;

        // ══════════════════ Full pipe limits ══════════════════

        [Header("Full Pipe Limits")]

        [Tooltip("Full-pipe body duration window (seconds at design speed).")]
        [SerializeField] private float minFullPipeSeconds = 2f;
        [SerializeField] private float maxFullPipeSeconds = 12f;

        [Tooltip("Pipe closure/opening transition window (seconds).")]
        [SerializeField] private float minPipeTransitionSeconds = 0.6f;
        [SerializeField] private float maxPipeTransitionSeconds = 3f;

        [Tooltip("Minimum full-pipe interior radius (meters) — craft envelope + camera clearance.")]
        [SerializeField] private float minFullPipeRadius = 12f;

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

        [Tooltip("Legal window for the designer's Minimum Connector Duration (seconds). Connectors shorter than the resolved minimum are lengthened, absorbed into a turn complex, or bridged.")]
        [SerializeField] private float minConnectorSeconds = 0.1f;
        [SerializeField] private float maxConnectorSeconds = 2.5f;

        [Tooltip("Legal window for the designer's Minimum Bank-Reversal Duration (seconds): the shortest time the banking field may take to swing from one side through zero to the other.")]
        [SerializeField] private float minBankReversalSeconds = 0.3f;
        [SerializeField] private float maxBankReversalSeconds = 3.5f;

        [Tooltip("Legal window for the designer's Same-Direction Bridge Duration (seconds): a straight between related turns SHORTER than this is treated as one turn complex — bank, wall support, depth and rounding carry through instead of dipping and rebuilding.")]
        [SerializeField] private float minBridgeSeconds = 0.5f;
        [SerializeField] private float maxBridgeSeconds = 5f;

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
        [SerializeField] private float maxCorkscrewSeconds = 9f;

        [Tooltip("Corkscrew helix radius window (meters).")]
        [SerializeField] private float minCorkscrewRadius = 60f;
        [SerializeField] private float maxCorkscrewRadius = 260f;

        // Deprecated V1 migration inputs. Keep their serialized names/data, but do not
        // expose arbitrary degree targets in the V2 inspector.
        [SerializeField, HideInInspector] private float minCorkscrewRollDegrees = 180f;
        [SerializeField, HideInInspector] private float maxCorkscrewRollDegrees = 1080f;

        [Tooltip("Maximum roll rate anywhere on the track (degrees per second at design speed).")]
        [SerializeField] private float maxRollRateDegreesPerSecond = 220f;

        [SerializeField] private float minCorkscrewApproachSeconds = 1.3f;
        [SerializeField] private float maxCorkscrewApproachSeconds = 3.0f;
        [SerializeField] private float minCorkscrewRecoverySeconds = 1.0f;
        [SerializeField] private float maxCorkscrewRecoverySeconds = 2.5f;

        [Tooltip("Minimum clear space around a corkscrew volume (meters).")]
        [SerializeField] private float minCorkscrewClearance = 40f;

        // Legacy degree fields above remain serialized so existing assets are never
        // corrupted. Procedural selection uses the quantized grammar below.
        [Header("Rotational Road Grammar")]
        [Tooltip("Logical inversion unit in degrees. Version 2 uses 90 degrees.")]
        [SerializeField] private int rotationUnitDegrees = 90;

        [Tooltip("Allowed total vertical-rotation units for loop events.")]
        [SerializeField] private int[] allowedLoopRotationUnits = { 2, 4, 8, 12 };

        [Tooltip("Allowed total road-roll units for corkscrew events.")]
        [SerializeField] private int[] allowedCorkscrewRotationUnits = { 2, 4, 8, 12 };

        [SerializeField] private bool allowHalfRotations = true;
        [SerializeField] private bool allowQuarterTurnTransitions = true;
        [SerializeField] private int maxLoopRotationUnits = 12;
        [SerializeField] private int maxCorkscrewRotationUnits = 12;

        [Tooltip("Approved normal-bank target increment. Transitional samples remain smooth.")]
        [SerializeField] private float normalBankStepDegrees = 15f;

        [Tooltip("Approved phase overlap preset used by procedural mixed inversions.")]
        [SerializeField] private TrackGeneration.Macro.RotationalBlendPreset defaultRotationalBlend = TrackGeneration.Macro.RotationalBlendPreset.Medium;

        [Tooltip("Maximum centerline distance between source samples inside rotational events.")]
        [SerializeField] private float maxRotationalSampleDistance = 4f;

        [Tooltip("Maximum forward-direction change per source sample.")]
        [SerializeField] private float maxRotationalForwardAngle = 0.75f;

        [Tooltip("Maximum intentional road-roll change per source sample.")]
        [SerializeField] private float maxRotationalRollAngle = 1f;

        [Tooltip("Minimum source intervals generated inside every logical 90-degree unit.")]
        [SerializeField] private int minSamplesPerRotationUnit = 16;

        [Tooltip("Minimum travel distance allocated to every logical 90-degree unit.")]
        [SerializeField] private float minDistancePerRotationUnit = 25f;

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

        // ══════════════════ Quarter limits ══════════════════

        [Header("Quarter Limits")]

        [Tooltip("Whether Dual Road Quarters (two alternative roads through one quarter, entered and left by jumps) are allowed at all.")]
        [SerializeField] private bool allowDualRoadQuarters = true;

        [Tooltip("Maximum number of Dual Road Quarters per track (0–4).")]
        [SerializeField] private int maxDualQuartersPerTrack = 4;

        [Tooltip("Lane separation window between the two landing mouths / exit lips of a dual quarter (meters). The floor keeps the roads physically clear of each other's walls; the ceiling keeps both lanes reachable by mid-air aiming at design speed.")]
        [SerializeField] private float minLaneSeparation = 16f;
        [SerializeField] private float maxLaneSeparation = 80f;

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

        [Tooltip("Hard ceiling for how far past vertical any wall may curl (catch walls, wallride roofs). Clamped further by cross-section self-intersection validation.")]
        [Range(0f, 45f)]
        [SerializeField] private float maxOverhangAngleLimit = 32f;

        [Tooltip("Legal window for the capture-curl radius (meters).")]
        [SerializeField] private float minOverhangRadius = 2f;
        [SerializeField] private float maxOverhangRadius = 60f;

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

        [Tooltip("Approved subdivision ladder: every independent physical road region (an anchored retopology segment) uses an interval count from this ascending list, selected as the first tier ≥ its raw geometric requirement. Never rounds down below the physical requirement.")]
        [SerializeField] private int[] subdivisionLadder =
        {
            8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768,
            1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768
        };

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
        public float MaxClosureCurveRadius => maxClosureCurveRadius;
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
        public bool AllowBridges => allowBridges;
        public bool AllowUnderpasses => allowUnderpasses;
        public bool AllowFullPipes => allowFullPipes;
        public bool AllowWallRides => allowWallRides;

        public float MinFullPipeSeconds => minFullPipeSeconds;
        public float MaxFullPipeSeconds => maxFullPipeSeconds;
        public float MinPipeTransitionSeconds => minPipeTransitionSeconds;
        public float MaxPipeTransitionSeconds => maxPipeTransitionSeconds;
        public float MinFullPipeRadius => minFullPipeRadius;

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
        public float MinConnectorSeconds => minConnectorSeconds;
        public float MaxConnectorSeconds => maxConnectorSeconds;
        public float MinBankReversalSeconds => minBankReversalSeconds;
        public float MaxBankReversalSeconds => maxBankReversalSeconds;
        public float MinBridgeSeconds => minBridgeSeconds;
        public float MaxBridgeSeconds => maxBridgeSeconds;

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
        public int SchemaVersion => schemaVersion;
        public int RotationUnitDegrees => rotationUnitDegrees;
        public int[] AllowedLoopRotationUnits => allowedLoopRotationUnits;
        public int[] AllowedCorkscrewRotationUnits => allowedCorkscrewRotationUnits;
        public bool AllowHalfRotations => allowHalfRotations;
        public bool AllowQuarterTurnTransitions => allowQuarterTurnTransitions;
        public int MaxLoopRotationUnits => maxLoopRotationUnits;
        public int MaxCorkscrewRotationUnits => maxCorkscrewRotationUnits;
        public float NormalBankStepDegrees => normalBankStepDegrees;
        public TrackGeneration.Macro.RotationalBlendPreset DefaultRotationalBlend => defaultRotationalBlend;
        public float MaxRotationalSampleDistance => maxRotationalSampleDistance;
        public float MaxRotationalForwardAngle => maxRotationalForwardAngle;
        public float MaxRotationalRollAngle => maxRotationalRollAngle;
        public int MinSamplesPerRotationUnit => minSamplesPerRotationUnit;
        public float MinDistancePerRotationUnit => minDistancePerRotationUnit;

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

        public bool AllowDualRoadQuarters => allowDualRoadQuarters;
        public int MaxDualQuartersPerTrack => maxDualQuartersPerTrack;
        public float MinLaneSeparation => minLaneSeparation;
        public float MaxLaneSeparation => maxLaneSeparation;

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
        public float MaxOverhangAngleLimit => maxOverhangAngleLimit;
        public float MinOverhangRadius => minOverhangRadius;
        public float MaxOverhangRadius => maxOverhangRadius;

        public float MinMetersPerRing => minMetersPerRing;
        public float MaxMetersPerRing => maxMetersPerRing;
        public float MinFeatureMetersPerRing => minFeatureMetersPerRing;
        public float MaxFeatureMetersPerRing => maxFeatureMetersPerRing;
        public float MinFacetAngle => minFacetAngle;
        public float MaxFacetAngle => maxFacetAngle;
        public int MaxRingsPerMacroSection => maxRingsPerMacroSection;
        public int MaxTotalTrackRings => maxTotalTrackRings;
        public System.Collections.Generic.IReadOnlyList<int> SubdivisionLadder => subdivisionLadder;

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
            MigrateIfNeeded();

            void Order(ref float min, ref float max) { if (min > max) min = max; }
            void OrderInt(ref int min, ref int max) { if (min > max) min = max; }

            Order(ref minDesignSpeedKph, ref maxDesignSpeedKph);
            Order(ref minTargetLapTime, ref maxTargetLapTime);
            Order(ref minTrackLength, ref maxTrackLength);
            Order(ref minRoadWidth, ref maxRoadWidth);
            Order(ref minCurveRadius, ref maxCurveRadius);
            maxClosureCurveRadius = Mathf.Max(maxCurveRadius, maxClosureCurveRadius);

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

            Order(ref minLaneSeparation, ref maxLaneSeparation);

            Order(ref minHalfPipeSideHeight, ref maxHalfPipeSideHeight);
            Order(ref minHalfPipeCurveStrength, ref maxHalfPipeCurveStrength);
            Order(ref minHalfPipeWallAngle, ref maxHalfPipeWallAngle);
            Order(ref minHalfPipeCenterFlatRatio, ref maxHalfPipeCenterFlatRatio);
            OrderInt(ref minHalfPipeProfileResolution, ref maxHalfPipeProfileResolution);
            Order(ref minSafetyLipHeight, ref maxSafetyLipHeight);
            Order(ref minOverhangRadius, ref maxOverhangRadius);

            Order(ref minMetersPerRing, ref maxMetersPerRing);
            Order(ref minFeatureMetersPerRing, ref maxFeatureMetersPerRing);
            Order(ref minFacetAngle, ref maxFacetAngle);
            Order(ref minConnectorSeconds, ref maxConnectorSeconds);
            Order(ref minBankReversalSeconds, ref maxBankReversalSeconds);
            Order(ref minBridgeSeconds, ref maxBridgeSeconds);
            Order(ref minFullPipeSeconds, ref maxFullPipeSeconds);
            Order(ref minPipeTransitionSeconds, ref maxPipeTransitionSeconds);
            minFullPipeRadius = Mathf.Max(5f, minFullPipeRadius);

            // Subdivision ladder must be a validated ascending list starting at ≥ 8
            // (below eight intervals an independent road region reads as low-poly).
            if (subdivisionLadder == null || subdivisionLadder.Length == 0)
                subdivisionLadder = new[] { 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768,
                    1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768 };
            subdivisionLadder[0] = Mathf.Max(8, subdivisionLadder[0]);
            for (int i = 1; i < subdivisionLadder.Length; i++)
                subdivisionLadder[i] = Mathf.Max(subdivisionLadder[i - 1] + 1, subdivisionLadder[i]);

            minTrackLength = Mathf.Max(500f, minTrackLength);
            minRoadWidth = Mathf.Max(4f, minRoadWidth);
            minCurveRadius = Mathf.Max(20f, minCurveRadius);
            minVerticalClearance = Mathf.Max(5f, minVerticalClearance);
            maxDualQuartersPerTrack = Mathf.Clamp(maxDualQuartersPerTrack, 0, 4);
            minLaneSeparation = Mathf.Max(8f, minLaneSeparation);
            maxLaneSeparation = Mathf.Max(minLaneSeparation, maxLaneSeparation);
            minSpiralRevolutions = Mathf.Max(1, minSpiralRevolutions);
            maxSpiralRevolutions = Mathf.Clamp(maxSpiralRevolutions, minSpiralRevolutions, 6);
            maxRingsPerMacroSection = Mathf.Max(32, maxRingsPerMacroSection);
            maxTotalTrackRings = Mathf.Max(maxRingsPerMacroSection, maxTotalTrackRings);
            minHalfPipeProfileResolution = Mathf.Clamp(minHalfPipeProfileResolution, 3, 96);
            maxHalfPipeProfileResolution = Mathf.Clamp(maxHalfPipeProfileResolution, minHalfPipeProfileResolution, 96);
            referenceGravity = Mathf.Max(0.1f, referenceGravity);
            referenceTopSpeedKph = Mathf.Max(50f, referenceTopSpeedKph);

            rotationUnitDegrees = 90;
            maxLoopRotationUnits = Mathf.Clamp(maxLoopRotationUnits, 2, 12);
            maxCorkscrewRotationUnits = Mathf.Clamp(maxCorkscrewRotationUnits, 2, 12);
            normalBankStepDegrees = Mathf.Clamp(normalBankStepDegrees, 5f, 45f);
            maxRotationalSampleDistance = Mathf.Clamp(maxRotationalSampleDistance, 0.5f, 20f);
            maxRotationalForwardAngle = Mathf.Clamp(maxRotationalForwardAngle, 0.1f, 5f);
            maxRotationalRollAngle = Mathf.Clamp(maxRotationalRollAngle, 0.1f, 8f);
            minSamplesPerRotationUnit = Mathf.Clamp(minSamplesPerRotationUnit, 4, 128);
            minDistancePerRotationUnit = Mathf.Clamp(minDistancePerRotationUnit, 5f, 500f);
            allowedLoopRotationUnits = SanitizeUnits(allowedLoopRotationUnits, maxLoopRotationUnits, allowHalfRotations);
            allowedCorkscrewRotationUnits = SanitizeUnits(allowedCorkscrewRotationUnits, maxCorkscrewRotationUnits, allowHalfRotations);

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

        private void MigrateIfNeeded()
        {
            if (schemaVersion >= CurrentSchemaVersion) return;

            // V1 assets selected one full loop and a degree-clamped corkscrew. Preserve
            // those effective choices while retaining the old serialized degree ranges
            // for inspection/rollback. Designers can then opt into additional units.
            rotationUnitDegrees = 90;
            allowedLoopRotationUnits = new[] { 4 };
            int minUnits = Mathf.Max(2, Mathf.CeilToInt(minCorkscrewRollDegrees / 90f));
            int maxUnits = Mathf.Clamp(Mathf.FloorToInt(maxCorkscrewRollDegrees / 90f), minUnits, 12);
            var migrated = new System.Collections.Generic.List<int>();
            int[] candidates = { 2, 4, 8, 12 };
            foreach (int units in candidates)
                if (units >= minUnits && units <= maxUnits) migrated.Add(units);
            allowedCorkscrewRotationUnits = migrated.Count > 0 ? migrated.ToArray() : new[] { 4 };
            schemaVersion = CurrentSchemaVersion;
        }

        private void Reset()
        {
            schemaVersion = CurrentSchemaVersion;
            allowedLoopRotationUnits = new[] { 2, 4, 8, 12 };
            allowedCorkscrewRotationUnits = new[] { 2, 4, 8, 12 };
            Validate();
        }

        private static int[] SanitizeUnits(int[] values, int maximum, bool allowHalf)
        {
            var clean = new System.Collections.Generic.List<int>();
            if (values != null)
            {
                foreach (int raw in values)
                {
                    int units = Mathf.Clamp(raw, 1, maximum);
                    bool major = units == 4 || units == 8 || units == 12;
                    bool half = allowHalf && units == 2;
                    if ((major || half) && !clean.Contains(units)) clean.Add(units);
                }
            }
            if (clean.Count == 0) clean.Add(Mathf.Min(4, maximum));
            clean.Sort();
            return clean.ToArray();
        }
    }
}
