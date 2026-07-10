using UnityEngine;

namespace TrackGeneration.Core
{
    /// <summary>
    /// ScriptableObject containing the technical rulebook for procedural track generation.
    ///
    /// IMPORTANT:
    /// TrackConfig defines what is possible and safe.
    /// It should not define the personality of a specific generated track.
    ///
    /// Track personality should live on TrackGenerator / TrackDesignerProfile.
    /// ResolvedTrackGenerationConfig should translate designer intent + this rulebook
    /// into internal generation values.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackConfig", menuName = "Track/Generation Config")]
    public class TrackConfig : ScriptableObject
    {
        // ══════════════════════════════════════════════
        //  TrackConfig = RULEBOOK / SANDBOX LIMITS
        // ══════════════════════════════════════════════

        [Header("Technical Constraints")]

        [Tooltip("Minimum total lap length the generator may produce (meters).")]
        [SerializeField] private float minTrackLength = 900f;

        [Tooltip("Maximum total lap length the generator may produce (meters).")]
        [SerializeField] private float maxTrackLength = 12000f;

        [Tooltip("Minimum allowed road width (meters).")]
        [SerializeField] private float minRoadWidth = 7f;

        [Tooltip("Maximum allowed road width (meters).")]
        [SerializeField] private float maxRoadWidth = 40f;

        [Tooltip("Smallest safe curve radius (meters).")]
        [SerializeField] private float minCurveRadius = 45f;

        [Tooltip("Largest allowed curve radius (meters). Allows huge high-speed sweepers.")]
        [SerializeField] private float maxCurveRadius = 1200f;

        [Tooltip("Maximum allowed banking angle in degrees. High values support NASCAR / futuristic high-speed banks.")]
        [Range(0f, 80f)]
        [SerializeField] private float maxBankAngle = 75f;

        [Tooltip("Maximum road incline angle in degrees.")]
        [Range(5f, 60f)]
        [SerializeField] private float maxSlopeAngle = 40f;

        [Tooltip("Maximum vertical elevation change the generator may use (meters). Needed for Layered / Rainbow Road tracks.")]
        [Range(10f, 300f)]
        [SerializeField] private float maxElevationChange = 220f;

        [Header("Allowed Features")]

        [Tooltip("Whether jump sections may be generated.")]
        [SerializeField] private bool allowJumps = true;

        [Tooltip("Whether vertical loops may be generated.")]
        [SerializeField] private bool allowLoops = true;

        [Tooltip("Whether corkscrews may be generated.")]
        [SerializeField] private bool allowCorkscrews = true;

        [Tooltip("Whether two-route split groups may be generated.")]
        [SerializeField] private bool allowRouteSplits = true;

        [Tooltip("Whether one route may pass over another or over the main track.")]
        [SerializeField] private bool allowOverpasses = true;

        [Tooltip("Whether one route may pass under another or under the main track.")]
        [SerializeField] private bool allowUnderpasses = true;

        [Tooltip("Whether layered routes may cross above/below each other.")]
        [SerializeField] private bool allowLayeredRouteCrossings = true;

        [Tooltip("Whether wall-ride sections may be generated.")]
        [SerializeField] private bool allowWallRides = true;

        [Tooltip("Whether gravity zones may be generated.")]
        [SerializeField] private bool allowGravityZones = true;

        [Tooltip("Whether shortcuts / branches may be generated. Legacy spline mode may still use this.")]
        [SerializeField] private bool allowShortcuts = true;

        [Header("Safety Limits")]

        [Tooltip("Minimum vertical clearance when one route passes above another (meters).")]
        [SerializeField] private float minVerticalClearance = 20f;

        [Tooltip("Minimum recovery straight length after dangerous sections (meters).")]
        [SerializeField] private float minRecoveryLength = 70f;

        [Tooltip("Minimum readable approach length before a jump section (meters).")]
        [SerializeField] private float minJumpApproachLength = 100f;

        [Tooltip("Minimum recovery length after a jump landing (meters).")]
        [SerializeField] private float minJumpExitRecoveryLength = 100f;

        [Tooltip("Minimum spacing between dangerous macro sections such as jumps, loops, corkscrews, hairpins, and route merges (meters).")]
        [SerializeField] private float minDangerousSectionSpacing = 160f;

        [Header("Macro Section Limits")]

        [Tooltip("Minimum length for a normal straight macro section (meters).")]
        [SerializeField] private float minStraightLength = 60f;

        [Tooltip("Maximum length for a normal straight macro section (meters).")]
        [SerializeField] private float maxStraightLength = 900f;

        [Tooltip("Minimum length for a boost straight macro section (meters).")]
        [SerializeField] private float minBoostStraightLength = 100f;

        [Tooltip("Maximum length for a boost straight macro section (meters).")]
        [SerializeField] private float maxBoostStraightLength = 1000f;

        [Tooltip("Minimum length for a recovery straight macro section (meters).")]
        [SerializeField] private float minRecoveryStraightLength = 60f;

        [Tooltip("Maximum length for a recovery straight macro section (meters).")]
        [SerializeField] private float maxRecoveryStraightLength = 300f;

        [Tooltip("Minimum turn angle for a curve macro section (degrees).")]
        [SerializeField] private float minCurveAngle = 25f;

        [Tooltip("Maximum turn angle for a curve macro section (degrees).")]
        [SerializeField] private float maxCurveAngle = 180f;

        [Tooltip("Minimum distance over which banking eases in/out (meters).")]
        [SerializeField] private float minBankTransitionLength = 12f;

        [Tooltip("Maximum distance over which banking eases in/out (meters).")]
        [SerializeField] private float maxBankTransitionLength = 100f;

        [Header("Verticality Limits")]

        [Tooltip("Minimum meaningful elevation step for non-flat verticality presets (meters).")]
        [SerializeField] private float minElevationStep = 12f;

        [Tooltip("Maximum single elevation step for non-flat verticality presets (meters).")]
        [SerializeField] private float maxElevationStep = 80f;

        [Tooltip("Minimum height for rolling hills (meters).")]
        [SerializeField] private float minRollingHillHeight = 8f;

        [Tooltip("Maximum height for rolling hills (meters).")]
        [SerializeField] private float maxRollingHillHeight = 35f;

        [Tooltip("Minimum height offset for layered route groups (meters).")]
        [SerializeField] private float minLayeredHeightOffset = 24f;

        [Tooltip("Maximum height offset for layered route groups (meters).")]
        [SerializeField] private float maxLayeredHeightOffset = 140f;

        [Tooltip("Minimum bridge / overpass height (meters).")]
        [SerializeField] private float minBridgeHeight = 24f;

        [Tooltip("Maximum bridge / overpass height (meters).")]
        [SerializeField] private float maxBridgeHeight = 120f;

        [Tooltip("Minimum underpass depth below the main route level (meters).")]
        [SerializeField] private float minUnderpassDepth = 18f;

        [Tooltip("Maximum underpass depth below the main route level (meters).")]
        [SerializeField] private float maxUnderpassDepth = 80f;

        [Tooltip("Minimum length used to climb into a higher route / elevated section (meters).")]
        [SerializeField] private float minClimbLength = 100f;

        [Tooltip("Maximum length used to climb into a higher route / elevated section (meters).")]
        [SerializeField] private float maxClimbLength = 500f;

        [Tooltip("Minimum length used to drop into a lower route / underpass (meters).")]
        [SerializeField] private float minDropLength = 80f;

        [Tooltip("Maximum length used to drop into a lower route / underpass (meters).")]
        [SerializeField] private float maxDropLength = 450f;

        [Tooltip("Comfortable maximum climb angle for normal flowing tracks (degrees).")]
        [Range(1f, 45f)]
        [SerializeField] private float maxComfortableClimbAngle = 18f;

        [Tooltip("Aggressive maximum climb angle for hard / Rainbow Road tracks (degrees).")]
        [Range(1f, 60f)]
        [SerializeField] private float maxAggressiveClimbAngle = 32f;

        [Tooltip("Comfortable maximum drop angle for normal flowing tracks (degrees).")]
        [Range(1f, 45f)]
        [SerializeField] private float maxComfortableDropAngle = 20f;

        [Tooltip("Aggressive maximum drop angle for hard / Rainbow Road tracks (degrees).")]
        [Range(1f, 60f)]
        [SerializeField] private float maxAggressiveDropAngle = 38f;

        [Tooltip("Maximum number of major elevation changes allowed per track.")]
        [SerializeField] private int maxMajorElevationChangesPerTrack = 8;

        [Tooltip("Maximum number of layered vertical sections allowed per track.")]
        [SerializeField] private int maxLayeredSectionsPerTrack = 4;

        [Header("Route Split / Layered Track Limits")]

        [Tooltip("Maximum number of two-route groups allowed per track.")]
        [SerializeField] private int maxRouteGroupsPerTrack = 4;

        [Tooltip("Minimum straight/readable approach length before a route split (meters).")]
        [SerializeField] private float minRouteSplitApproachLength = 100f;

        [Tooltip("Minimum length of an alternate route group from split to merge (meters).")]
        [SerializeField] private float minRouteSplitLength = 180f;

        [Tooltip("Maximum length of an alternate route group from split to merge (meters).")]
        [SerializeField] private float maxRouteSplitLength = 900f;

        [Tooltip("Minimum length used to merge routes back together (meters).")]
        [SerializeField] private float minRouteMergeLength = 80f;

        [Tooltip("Minimum recovery length after routes merge (meters).")]
        [SerializeField] private float minPostMergeRecoveryLength = 100f;

        [Tooltip("Minimum approach length before a layered route group (meters).")]
        [SerializeField] private float minLayeredRouteApproachLength = 120f;

        [Tooltip("Minimum layered route group length (meters).")]
        [SerializeField] private float minLayeredRouteLength = 300f;

        [Tooltip("Maximum layered route group length (meters).")]
        [SerializeField] private float maxLayeredRouteLength = 1200f;

        [Tooltip("Minimum merge length for layered route groups (meters).")]
        [SerializeField] private float minLayeredRouteMergeLength = 100f;

        [Tooltip("Minimum recovery length after a layered route group (meters).")]
        [SerializeField] private float minLayeredRouteRecoveryLength = 120f;

        [Tooltip("Minimum vertical separation between high and low routes (meters).")]
        [SerializeField] private float minLayeredRouteHeightSeparation = 24f;

        [Tooltip("Maximum vertical separation between high and low routes (meters).")]
        [SerializeField] private float maxLayeredRouteHeightSeparation = 140f;

        [Tooltip("Maximum height offset a split route may use from the base route level (meters).")]
        [SerializeField] private float maxRouteElevationOffset = 160f;

        [Tooltip("Maximum over/under crossings allowed per track.")]
        [SerializeField] private int maxOverUnderCrossingsPerTrack = 4;

        [Tooltip("Minimum clear air gap for an overpass (meters).")]
        [SerializeField] private float minOverpassClearance = 20f;

        [Tooltip("Minimum clear air gap for an underpass (meters).")]
        [SerializeField] private float minUnderpassClearance = 20f;

        [Header("Speed Profile Support Limits")]

        [Tooltip("Minimum curve radius used by flowing speed profiles (meters).")]
        [SerializeField] private float minFlowingCurveRadius = 250f;

        [Tooltip("Maximum curve radius used by flowing speed profiles (meters).")]
        [SerializeField] private float maxFlowingCurveRadius = 1200f;

        [Tooltip("Minimum curve radius used by technical speed profiles (meters).")]
        [SerializeField] private float minTechnicalCurveRadius = 45f;

        [Tooltip("Maximum curve radius used by technical speed profiles (meters).")]
        [SerializeField] private float maxTechnicalCurveRadius = 220f;

        [Tooltip("Minimum straight length used by insane-speed profiles (meters).")]
        [SerializeField] private float minInsaneSpeedStraightLength = 250f;

        [Tooltip("Maximum straight length used by insane-speed profiles (meters).")]
        [SerializeField] private float maxInsaneSpeedStraightLength = 1000f;

        [Tooltip("Minimum straight length used by technical profiles (meters).")]
        [SerializeField] private float minTechnicalStraightLength = 60f;

        [Tooltip("Maximum straight length used by technical profiles (meters).")]
        [SerializeField] private float maxTechnicalStraightLength = 260f;

        [Header("Loop Limits")]

        [Tooltip("Minimum vertical loop radius (meters).")]
        [SerializeField] private float minLoopRadius = 35f;

        [Tooltip("Maximum vertical loop radius (meters).")]
        [SerializeField] private float maxLoopRadius = 130f;

        [Tooltip("Minimum straight approach length before a loop (meters).")]
        [SerializeField] private float minLoopApproachLength = 160f;

        [Tooltip("Minimum recovery straight length after a loop (meters).")]
        [SerializeField] private float minLoopExitRecoveryLength = 120f;

        [Tooltip("Maximum pitch change per meter of track inside a loop (degrees/m).")]
        [SerializeField] private float maxLoopPitchRate = 3.5f;

        [Tooltip("Meters per prism subdivision ring inside loops (lower = smoother).")]
        [Range(0.5f, 5f)]
        [SerializeField] private float loopSubdivisionDensity = 1.25f;

        [Tooltip("Minimum straight length before a loop to allow speed build-up (meters).")]
        [SerializeField] private float minLoopEntrySpeedStraightLength = 180f;

        [Tooltip("Minimum vertical clearance around a loop volume (meters).")]
        [SerializeField] private float minLoopVerticalClearance = 30f;

        [Header("Corkscrew Limits")]

        [Tooltip("Minimum corkscrew length along the track axis (meters).")]
        [SerializeField] private float minCorkscrewLength = 120f;

        [Tooltip("Maximum corkscrew length along the track axis (meters).")]
        [SerializeField] private float maxCorkscrewLength = 450f;

        [Tooltip("Minimum helix radius of the corkscrew centerline (meters).")]
        [SerializeField] private float minCorkscrewRadius = 20f;

        [Tooltip("Maximum helix radius of the corkscrew centerline (meters).")]
        [SerializeField] private float maxCorkscrewRadius = 80f;

        [Tooltip("Maximum total roll rotation through a corkscrew (degrees).")]
        [Range(90f, 720f)]
        [SerializeField] private float maxCorkscrewRollDegrees = 720f;

        [Tooltip("Maximum roll change per 10 meters of track (degrees).")]
        [SerializeField] private float maxCorkscrewRollRate = 60f;

        [Tooltip("Minimum straight approach length before a corkscrew (meters).")]
        [SerializeField] private float minCorkscrewApproachLength = 140f;

        [Tooltip("Minimum recovery straight length after a corkscrew (meters).")]
        [SerializeField] private float minCorkscrewExitRecoveryLength = 120f;

        [Tooltip("Meters per prism subdivision ring inside corkscrews (lower = smoother).")]
        [Range(0.5f, 5f)]
        [SerializeField] private float corkscrewSubdivisionDensity = 1.25f;

        [Tooltip("Minimum straight length before a corkscrew to allow speed build-up (meters).")]
        [SerializeField] private float minCorkscrewEntrySpeedStraightLength = 160f;

        [Tooltip("Minimum vertical clearance around a corkscrew volume (meters).")]
        [SerializeField] private float minCorkscrewVerticalClearance = 25f;

        [Header("Half-Pipe Road Cross-Section Limits")]

        [Tooltip("Whether the global half-pipe / water-slide road cross-section is allowed. When false the generator falls back to legacy flat-with-walls roads.")]
        [SerializeField] private bool allowHalfPipeRoads = true;

        [Tooltip("Minimum half-pipe side height above the center floor (meters).")]
        [SerializeField] private float minHalfPipeSideHeight = 1.5f;

        [Tooltip("Maximum half-pipe side height above the center floor (meters).")]
        [SerializeField] private float maxHalfPipeSideHeight = 7f;

        [Tooltip("Minimum half-pipe curve strength (how smoothly the road curves upward).")]
        [SerializeField] private float minHalfPipeCurveStrength = 0.35f;

        [Tooltip("Maximum half-pipe curve strength (how aggressively the road curves upward).")]
        [SerializeField] private float maxHalfPipeCurveStrength = 1.5f;

        [Tooltip("Minimum side wall tilt angle in degrees.")]
        [SerializeField] private float minHalfPipeWallAngle = 35f;

        [Tooltip("Maximum side wall tilt angle in degrees.")]
        [SerializeField] private float maxHalfPipeWallAngle = 75f;

        [Tooltip("Minimum ratio of the road width kept flat in the center.")]
        [SerializeField] private float minHalfPipeCenterFlatWidthRatio = 0.15f;

        [Tooltip("Maximum ratio of the road width kept flat in the center.")]
        [SerializeField] private float maxHalfPipeCenterFlatWidthRatio = 0.55f;

        [Tooltip("Minimum cross-section sample points per side of the half-pipe profile.")]
        [SerializeField] private int minHalfPipeProfileResolution = 6;

        [Tooltip("Maximum cross-section sample points per side of the half-pipe profile.")]
        [SerializeField] private int maxHalfPipeProfileResolution = 18;

        [Header("Section Transition Blend Limits")]

        [Tooltip("Minimum generic transition blend length between macro sections (meters).")]
        [SerializeField] private float minTransitionBlendLength = 20f;

        [Tooltip("Maximum generic transition blend length between macro sections (meters).")]
        [SerializeField] private float maxTransitionBlendLength = 180f;

        [Tooltip("Minimum distance over which bank angle eases in/out (meters).")]
        [SerializeField] private float minBankBlendLength = 30f;

        [Tooltip("Maximum distance over which bank angle eases in/out (meters).")]
        [SerializeField] private float maxBankBlendLength = 220f;

        [Tooltip("Minimum distance over which pitch/elevation changes blend (meters).")]
        [SerializeField] private float minPitchBlendLength = 40f;

        [Tooltip("Maximum distance over which pitch/elevation changes blend (meters).")]
        [SerializeField] private float maxPitchBlendLength = 240f;

        [Tooltip("Minimum distance over which road width changes blend (meters).")]
        [SerializeField] private float minWidthBlendLength = 25f;

        [Tooltip("Maximum distance over which road width changes blend (meters).")]
        [SerializeField] private float maxWidthBlendLength = 180f;

        [Tooltip("Minimum distance over which half-pipe depth/shape changes blend (meters).")]
        [SerializeField] private float minCrossSectionBlendLength = 30f;

        [Tooltip("Maximum distance over which half-pipe depth/shape changes blend (meters).")]
        [SerializeField] private float maxCrossSectionBlendLength = 200f;

        [Tooltip("Maximum effective ramp angle a bank-in/bank-out transition may create (degrees). Blends auto-expand to respect this — prevents bank transitions becoming launch ramps.")]
        [Range(2f, 30f)]
        [SerializeField] private float maxBankRampAngle = 8f;

        [Header("Route Split Structure Limits")]

        [Tooltip("Minimum length of the lateral separation zone where routes move sideways apart (meters).")]
        [SerializeField] private float minLateralSeparationLength = 80f;

        [Tooltip("Maximum length of the lateral separation zone (meters).")]
        [SerializeField] private float maxLateralSeparationLength = 300f;

        [Tooltip("Minimum distance after the split before any vertical divergence may start (meters). Splits must read as left/right choices FIRST.")]
        [SerializeField] private float minVerticalDivergenceDelay = 60f;

        [Tooltip("Maximum vertical divergence delay (meters).")]
        [SerializeField] private float maxVerticalDivergenceDelay = 250f;

        [Tooltip("Minimum length over which a route climbs/drops to its layer height (meters).")]
        [SerializeField] private float minVerticalDivergenceLength = 120f;

        [Tooltip("Maximum vertical divergence length (meters).")]
        [SerializeField] private float maxVerticalDivergenceLength = 500f;

        [Tooltip("Minimum lateral separation between the two route centerlines (meters).")]
        [SerializeField] private float minRouteLateralSeparation = 12f;

        [Tooltip("Maximum lateral separation between the two route centerlines (meters).")]
        [SerializeField] private float maxRouteLateralSeparation = 80f;

        [Header("Mesh / Collision Limits")]

        [Tooltip("Smallest allowed ring spacing (meters). Guards against runaway vertex counts.")]
        [SerializeField] private float minMetersPerRing = 0.75f;

        [Tooltip("Largest allowed ring spacing (meters). Guards against faceted collision surfaces.")]
        [SerializeField] private float maxMetersPerRing = 5f;

        [Tooltip("Maximum subdivision rings allowed inside a single macro section.")]
        [SerializeField] private int maxRingsPerMacroSection = 512;

        [Tooltip("Maximum subdivision rings allowed for the entire generated track.")]
        [SerializeField] private int maxTotalTrackRings = 12000;

        // ══════════════════════════════════════════════
        //  LEGACY SPLINE MODE — KEPT FOR BACKWARDS COMPATIBILITY
        //  New macro generator should not use these as personality controls.
        // ══════════════════════════════════════════════

        [Header("Legacy — Circuit Shape (Spline Mode)")]

        [Tooltip("Approximate radius of the main circuit loop in meters.")]
        [Range(100f, 1000f)]
        [SerializeField] private float trackRadius = 300f;

        [Tooltip("Number of control points that define the circuit spline.")]
        [Range(8, 40)]
        [SerializeField] private int controlPointCount = 20;

        [Tooltip("Frequency multiplier for elevation noise — legacy spline mode only.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float elevationFrequency = 0.3f;

        [Tooltip("Multiplier applied to automatic banking on curves — legacy spline mode only.")]
        [Range(0f, 3f)]
        [SerializeField] private float bankingMultiplier = 1.5f;

        [Tooltip("Probability (per eligible segment) that a vertical loop is placed — legacy spline mode only.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float loopProbability = 0.1f;

        [Tooltip("Probability (per eligible segment) that a corkscrew is placed — legacy spline mode only.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float corkscrewProbability = 0.05f;

        [Tooltip("Probability (per eligible segment) that a steep climb/drop is exaggerated — legacy spline mode only.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float steepSectionProbability = 0.08f;

        [Tooltip("Number of smoothing passes on elevation — legacy spline mode only.")]
        [Range(0, 5)]
        [SerializeField] private int elevationSmoothingPasses = 2;

        [Tooltip("Minimum number of F1-style straight sections on the main circuit — legacy spline mode only.")]
        [Range(0, 4)]
        [SerializeField] private int minStraightSections = 2;

        [Tooltip("Maximum number of F1-style straight sections on the main circuit — legacy spline mode only.")]
        [Range(0, 6)]
        [SerializeField] private int maxStraightSections = 3;

        [Tooltip("Distance in meters over which banking eases in/out of corners — legacy spline mode only.")]
        [Range(0f, 60f)]
        [SerializeField] private float bankTransitionLength = 18f;

        [Header("Legacy — Road Dimensions (Spline Mode)")]

        [Tooltip("Width of a single lane in meters.")]
        [Range(3f, 30f)]
        [SerializeField] private float laneWidth = 4f;

        [Tooltip("Number of lanes on the main road.")]
        [Range(1, 4)]
        [SerializeField] private int mainRoadLanes = 2;

        [Tooltip("Number of lanes on shortcut roads.")]
        [Range(1, 2)]
        [SerializeField] private int shortcutRoadLanes = 1;

        [Tooltip("Height of the barrier walls in meters.")]
        [Range(0.5f, 5f)]
        [SerializeField] private float wallHeight = 1.5f;

        [Header("Legacy — Branching / Shortcuts (Spline Mode)")]

        [Tooltip("Minimum number of shortcuts generated per track.")]
        [Range(0, 5)]
        [SerializeField] private int minShortcuts = 2;

        [Tooltip("Maximum number of shortcuts generated per track.")]
        [Range(1, 6)]
        [SerializeField] private int maxShortcuts = 3;

        [Tooltip("Target shortcut length as a ratio of the bypassed main-road distance.")]
        [Range(0.3f, 0.9f)]
        [SerializeField] private float shortcutLengthRatio = 0.6f;

        [Tooltip("Bias toward placing shortcuts on harder sections.")]
        [Range(0f, 1f)]
        [SerializeField] private float shortcutDifficultyBias = 0.7f;

        [Tooltip("Distance in meters for the shortcut merge zone.")]
        [Range(10f, 60f)]
        [SerializeField] private float shortcutMergeDistance = 30f;

        [Header("Legacy — Stunts (Spline Mode)")]

        [Tooltip("Minimum number of stunt actors per track.")]
        [Range(0, 4)]
        [SerializeField] private int minStunts = 1;

        [Tooltip("Maximum number of stunt actors per track.")]
        [Range(1, 4)]
        [SerializeField] private int maxStunts = 4;

        [Tooltip("Minimum arc distance in meters between two stunts on the same road.")]
        [Range(50f, 500f)]
        [SerializeField] private float minStuntSpacing = 150f;

        [Tooltip("Relative selection weight for wall-ride candidates.")]
        [Range(0f, 1f)]
        [SerializeField] private float wallRideProbability = 0.5f;

        [Tooltip("Relative selection weight for ramp candidates.")]
        [Range(0f, 1f)]
        [SerializeField] private float rampProbabilityPerSegment = 0.5f;

        [Tooltip("Maximum height of ramps in meters.")]
        [Range(2f, 20f)]
        [SerializeField] private float rampMaxHeight = 8f;

        [Header("Legacy — Boost Pads (Spline Mode)")]

        [Tooltip("Minimum number of boost pads per track.")]
        [Range(0, 8)]
        [SerializeField] private int minBoostPads = 2;

        [Tooltip("Maximum number of boost pads per track.")]
        [Range(0, 10)]
        [SerializeField] private int maxBoostPads = 4;

        [Tooltip("Forward impulse strength applied by boost pads.")]
        [Range(5f, 100f)]
        [SerializeField] private float boostPadStrength = 30f;

        [Header("Legacy — Gravity Zones (Spline Mode)")]

        [Tooltip("Probability of a gravity zone appearing per eligible segment.")]
        [Range(0f, 1f)]
        [SerializeField] private float gravityZoneProbability = 0.1f;

        [Tooltip("Gravitational field strength inside gravity zones.")]
        [Range(10f, 200f)]
        [SerializeField] private float gravityFieldStrength = 50f;

        [Tooltip("Radius of effect for gravity zones in meters.")]
        [Range(5f, 100f)]
        [SerializeField] private float gravityFieldRadius = 30f;

        [Header("Race Settings")]

        [Tooltip("Default number of laps for a race on this track.")]
        [Range(1, 10)]
        [SerializeField] private int defaultLapCount = 3;

        [Header("Legacy — Mesh Quality (Spline Mode)")]

        [Tooltip("Minimum number of subdivisions when sampling the spline for mesh generation.")]
        [Range(16, 512)]
        [SerializeField] private int splineSubdivisions = 128;

        [Tooltip("Target distance between mesh cross-sections in meters.")]
        [Range(1f, 10f)]
        [SerializeField] private float meshSegmentLength = 2.5f;

        [Tooltip("Spline curve tension.")]
        [Range(0f, 1f)]
        [SerializeField] private float curveSmoothingTension = 0.5f;

        public float MinTrackLength => minTrackLength;
        public float MaxTrackLength => maxTrackLength;
        public float MinRoadWidth => minRoadWidth;
        public float MaxRoadWidth => maxRoadWidth;
        public float MinCurveRadius => minCurveRadius;
        public float MaxCurveRadius => maxCurveRadius;
        public float MaxBankAngle => maxBankAngle;
        public float MaxSlopeAngle => maxSlopeAngle;
        public float MaxElevationChange => maxElevationChange;

        public bool AllowJumps => allowJumps;
        public bool AllowLoops => allowLoops;
        public bool AllowCorkscrews => allowCorkscrews;
        public bool AllowRouteSplits => allowRouteSplits;
        public bool AllowOverpasses => allowOverpasses;
        public bool AllowUnderpasses => allowUnderpasses;
        public bool AllowLayeredRouteCrossings => allowLayeredRouteCrossings;
        public bool AllowWallRides => allowWallRides;
        public bool AllowGravityZones => allowGravityZones;
        public bool AllowShortcuts => allowShortcuts;

        public float MinVerticalClearance => minVerticalClearance;
        public float MinRecoveryLength => minRecoveryLength;
        public float MinJumpApproachLength => minJumpApproachLength;
        public float MinJumpExitRecoveryLength => minJumpExitRecoveryLength;
        public float MinDangerousSectionSpacing => minDangerousSectionSpacing;

        public float MinStraightLength => minStraightLength;
        public float MaxStraightLength => maxStraightLength;
        public float MinBoostStraightLength => minBoostStraightLength;
        public float MaxBoostStraightLength => maxBoostStraightLength;
        public float MinRecoveryStraightLength => minRecoveryStraightLength;
        public float MaxRecoveryStraightLength => maxRecoveryStraightLength;
        public float MinCurveAngle => minCurveAngle;
        public float MaxCurveAngle => maxCurveAngle;
        public float MinBankTransitionLength => minBankTransitionLength;
        public float MaxBankTransitionLength => maxBankTransitionLength;

        public float MinElevationStep => minElevationStep;
        public float MaxElevationStep => maxElevationStep;
        public float MinRollingHillHeight => minRollingHillHeight;
        public float MaxRollingHillHeight => maxRollingHillHeight;
        public float MinLayeredHeightOffset => minLayeredHeightOffset;
        public float MaxLayeredHeightOffset => maxLayeredHeightOffset;
        public float MinBridgeHeight => minBridgeHeight;
        public float MaxBridgeHeight => maxBridgeHeight;
        public float MinUnderpassDepth => minUnderpassDepth;
        public float MaxUnderpassDepth => maxUnderpassDepth;
        public float MinClimbLength => minClimbLength;
        public float MaxClimbLength => maxClimbLength;
        public float MinDropLength => minDropLength;
        public float MaxDropLength => maxDropLength;
        public float MaxComfortableClimbAngle => maxComfortableClimbAngle;
        public float MaxAggressiveClimbAngle => maxAggressiveClimbAngle;
        public float MaxComfortableDropAngle => maxComfortableDropAngle;
        public float MaxAggressiveDropAngle => maxAggressiveDropAngle;
        public int MaxMajorElevationChangesPerTrack => maxMajorElevationChangesPerTrack;
        public int MaxLayeredSectionsPerTrack => maxLayeredSectionsPerTrack;

        public int MaxRouteGroupsPerTrack => maxRouteGroupsPerTrack;
        public float MinRouteSplitApproachLength => minRouteSplitApproachLength;
        public float MinRouteSplitLength => minRouteSplitLength;
        public float MaxRouteSplitLength => maxRouteSplitLength;
        public float MinRouteMergeLength => minRouteMergeLength;
        public float MinPostMergeRecoveryLength => minPostMergeRecoveryLength;
        public float MinLayeredRouteApproachLength => minLayeredRouteApproachLength;
        public float MinLayeredRouteLength => minLayeredRouteLength;
        public float MaxLayeredRouteLength => maxLayeredRouteLength;
        public float MinLayeredRouteMergeLength => minLayeredRouteMergeLength;
        public float MinLayeredRouteRecoveryLength => minLayeredRouteRecoveryLength;
        public float MinLayeredRouteHeightSeparation => minLayeredRouteHeightSeparation;
        public float MaxLayeredRouteHeightSeparation => maxLayeredRouteHeightSeparation;
        public float MaxRouteElevationOffset => maxRouteElevationOffset;
        public int MaxOverUnderCrossingsPerTrack => maxOverUnderCrossingsPerTrack;
        public float MinOverpassClearance => minOverpassClearance;
        public float MinUnderpassClearance => minUnderpassClearance;

        public float MinFlowingCurveRadius => minFlowingCurveRadius;
        public float MaxFlowingCurveRadius => maxFlowingCurveRadius;
        public float MinTechnicalCurveRadius => minTechnicalCurveRadius;
        public float MaxTechnicalCurveRadius => maxTechnicalCurveRadius;
        public float MinInsaneSpeedStraightLength => minInsaneSpeedStraightLength;
        public float MaxInsaneSpeedStraightLength => maxInsaneSpeedStraightLength;
        public float MinTechnicalStraightLength => minTechnicalStraightLength;
        public float MaxTechnicalStraightLength => maxTechnicalStraightLength;

        public float MinLoopRadius => minLoopRadius;
        public float MaxLoopRadius => maxLoopRadius;
        public float MinLoopApproachLength => minLoopApproachLength;
        public float MinLoopExitRecoveryLength => minLoopExitRecoveryLength;
        public float MaxLoopPitchRate => maxLoopPitchRate;
        public float LoopSubdivisionDensity => loopSubdivisionDensity;
        public float MinLoopEntrySpeedStraightLength => minLoopEntrySpeedStraightLength;
        public float MinLoopVerticalClearance => minLoopVerticalClearance;

        public float MinCorkscrewLength => minCorkscrewLength;
        public float MaxCorkscrewLength => maxCorkscrewLength;
        public float MinCorkscrewRadius => minCorkscrewRadius;
        public float MaxCorkscrewRadius => maxCorkscrewRadius;
        public float MaxCorkscrewRollDegrees => maxCorkscrewRollDegrees;
        public float MaxCorkscrewRollRate => maxCorkscrewRollRate;
        public float MinCorkscrewApproachLength => minCorkscrewApproachLength;
        public float MinCorkscrewExitRecoveryLength => minCorkscrewExitRecoveryLength;
        public float CorkscrewSubdivisionDensity => corkscrewSubdivisionDensity;
        public float MinCorkscrewEntrySpeedStraightLength => minCorkscrewEntrySpeedStraightLength;
        public float MinCorkscrewVerticalClearance => minCorkscrewVerticalClearance;

        public bool AllowHalfPipeRoads => allowHalfPipeRoads;
        public float MinHalfPipeSideHeight => minHalfPipeSideHeight;
        public float MaxHalfPipeSideHeight => maxHalfPipeSideHeight;
        public float MinHalfPipeCurveStrength => minHalfPipeCurveStrength;
        public float MaxHalfPipeCurveStrength => maxHalfPipeCurveStrength;
        public float MinHalfPipeWallAngle => minHalfPipeWallAngle;
        public float MaxHalfPipeWallAngle => maxHalfPipeWallAngle;
        public float MinHalfPipeCenterFlatWidthRatio => minHalfPipeCenterFlatWidthRatio;
        public float MaxHalfPipeCenterFlatWidthRatio => maxHalfPipeCenterFlatWidthRatio;
        public int MinHalfPipeProfileResolution => minHalfPipeProfileResolution;
        public int MaxHalfPipeProfileResolution => maxHalfPipeProfileResolution;

        public float MinTransitionBlendLength => minTransitionBlendLength;
        public float MaxTransitionBlendLength => maxTransitionBlendLength;
        public float MinBankBlendLength => minBankBlendLength;
        public float MaxBankBlendLength => maxBankBlendLength;
        public float MinPitchBlendLength => minPitchBlendLength;
        public float MaxPitchBlendLength => maxPitchBlendLength;
        public float MinWidthBlendLength => minWidthBlendLength;
        public float MaxWidthBlendLength => maxWidthBlendLength;
        public float MinCrossSectionBlendLength => minCrossSectionBlendLength;
        public float MaxCrossSectionBlendLength => maxCrossSectionBlendLength;
        public float MaxBankRampAngle => maxBankRampAngle;

        public float MinLateralSeparationLength => minLateralSeparationLength;
        public float MaxLateralSeparationLength => maxLateralSeparationLength;
        public float MinVerticalDivergenceDelay => minVerticalDivergenceDelay;
        public float MaxVerticalDivergenceDelay => maxVerticalDivergenceDelay;
        public float MinVerticalDivergenceLength => minVerticalDivergenceLength;
        public float MaxVerticalDivergenceLength => maxVerticalDivergenceLength;
        public float MinRouteLateralSeparation => minRouteLateralSeparation;
        public float MaxRouteLateralSeparation => maxRouteLateralSeparation;

        public float MinMetersPerRing => minMetersPerRing;
        public float MaxMetersPerRing => maxMetersPerRing;
        public int MaxRingsPerMacroSection => maxRingsPerMacroSection;
        public int MaxTotalTrackRings => maxTotalTrackRings;

        public float TrackRadius => trackRadius;
        public int ControlPointCount => controlPointCount;
        public float ElevationFrequency => elevationFrequency;
        public float BankingMultiplier => bankingMultiplier;
        public float LoopProbability => loopProbability;
        public float CorkscrewProbability => corkscrewProbability;
        public float SteepSectionProbability => steepSectionProbability;
        public float LaneWidth => laneWidth;
        public int MainRoadLanes => mainRoadLanes;
        public int ShortcutRoadLanes => shortcutRoadLanes;
        public float MainRoadWidth => laneWidth * mainRoadLanes;
        public float ShortcutRoadWidth => laneWidth * shortcutRoadLanes;
        public float WallHeight => wallHeight;
        public int MinShortcuts => minShortcuts;
        public int MaxShortcuts => maxShortcuts;
        public float ShortcutLengthRatio => shortcutLengthRatio;
        public float ShortcutDifficultyBias => shortcutDifficultyBias;
        public float ShortcutMergeDistance => shortcutMergeDistance;
        public float WallRideProbability => wallRideProbability;
        public float RampProbabilityPerSegment => rampProbabilityPerSegment;
        public float RampMaxHeight => rampMaxHeight;
        public float GravityZoneProbability => gravityZoneProbability;
        public float GravityFieldStrength => gravityFieldStrength;
        public float GravityFieldRadius => gravityFieldRadius;
        public int DefaultLapCount => defaultLapCount;
        public int SplineSubdivisions => splineSubdivisions;
        public float MeshSegmentLength => meshSegmentLength;
        public int ElevationSmoothingPasses => elevationSmoothingPasses;
        public int MinStraightSections => minStraightSections;
        public int MaxStraightSections => maxStraightSections;
        public float BankTransitionLength => bankTransitionLength;
        public int MinStunts => minStunts;
        public int MaxStunts => maxStunts;
        public float MinStuntSpacing => minStuntSpacing;
        public int MinBoostPads => minBoostPads;
        public int MaxBoostPads => maxBoostPads;
        public float BoostPadStrength => boostPadStrength;
        public float CurveSmoothingTension => curveSmoothingTension;

        /// <summary>
        /// Clamps interdependent fields so rulebook invariants remain valid.
        /// </summary>
        public void Validate()
        {
            if (minTrackLength > maxTrackLength) minTrackLength = maxTrackLength;
            if (minRoadWidth > maxRoadWidth) minRoadWidth = maxRoadWidth;
            if (minCurveRadius > maxCurveRadius) minCurveRadius = maxCurveRadius;

            if (minStraightLength > maxStraightLength) minStraightLength = maxStraightLength;
            if (minBoostStraightLength > maxBoostStraightLength) minBoostStraightLength = maxBoostStraightLength;
            if (minRecoveryStraightLength > maxRecoveryStraightLength) minRecoveryStraightLength = maxRecoveryStraightLength;
            if (minCurveAngle > maxCurveAngle) minCurveAngle = maxCurveAngle;
            if (minBankTransitionLength > maxBankTransitionLength) minBankTransitionLength = maxBankTransitionLength;

            if (minElevationStep > maxElevationStep) minElevationStep = maxElevationStep;
            if (minRollingHillHeight > maxRollingHillHeight) minRollingHillHeight = maxRollingHillHeight;
            if (minLayeredHeightOffset > maxLayeredHeightOffset) minLayeredHeightOffset = maxLayeredHeightOffset;
            if (minBridgeHeight > maxBridgeHeight) minBridgeHeight = maxBridgeHeight;
            if (minUnderpassDepth > maxUnderpassDepth) minUnderpassDepth = maxUnderpassDepth;
            if (minClimbLength > maxClimbLength) minClimbLength = maxClimbLength;
            if (minDropLength > maxDropLength) minDropLength = maxDropLength;

            if (minRouteSplitLength > maxRouteSplitLength) minRouteSplitLength = maxRouteSplitLength;
            if (minLayeredRouteLength > maxLayeredRouteLength) minLayeredRouteLength = maxLayeredRouteLength;
            if (minLayeredRouteHeightSeparation > maxLayeredRouteHeightSeparation)
                minLayeredRouteHeightSeparation = maxLayeredRouteHeightSeparation;

            if (minFlowingCurveRadius > maxFlowingCurveRadius) minFlowingCurveRadius = maxFlowingCurveRadius;
            if (minTechnicalCurveRadius > maxTechnicalCurveRadius) minTechnicalCurveRadius = maxTechnicalCurveRadius;
            if (minInsaneSpeedStraightLength > maxInsaneSpeedStraightLength)
                minInsaneSpeedStraightLength = maxInsaneSpeedStraightLength;
            if (minTechnicalStraightLength > maxTechnicalStraightLength)
                minTechnicalStraightLength = maxTechnicalStraightLength;

            if (minLoopRadius > maxLoopRadius) minLoopRadius = maxLoopRadius;
            if (minCorkscrewLength > maxCorkscrewLength) minCorkscrewLength = maxCorkscrewLength;
            if (minCorkscrewRadius > maxCorkscrewRadius) minCorkscrewRadius = maxCorkscrewRadius;

            if (minMetersPerRing > maxMetersPerRing) minMetersPerRing = maxMetersPerRing;

            if (minHalfPipeSideHeight > maxHalfPipeSideHeight) minHalfPipeSideHeight = maxHalfPipeSideHeight;
            if (minHalfPipeCurveStrength > maxHalfPipeCurveStrength) minHalfPipeCurveStrength = maxHalfPipeCurveStrength;
            if (minHalfPipeWallAngle > maxHalfPipeWallAngle) minHalfPipeWallAngle = maxHalfPipeWallAngle;
            if (minHalfPipeCenterFlatWidthRatio > maxHalfPipeCenterFlatWidthRatio) minHalfPipeCenterFlatWidthRatio = maxHalfPipeCenterFlatWidthRatio;
            if (minHalfPipeProfileResolution > maxHalfPipeProfileResolution) minHalfPipeProfileResolution = maxHalfPipeProfileResolution;
            minHalfPipeSideHeight = Mathf.Max(0f, minHalfPipeSideHeight);
            minHalfPipeProfileResolution = Mathf.Max(3, minHalfPipeProfileResolution);
            maxHalfPipeProfileResolution = Mathf.Clamp(maxHalfPipeProfileResolution, minHalfPipeProfileResolution, 24);
            minHalfPipeCenterFlatWidthRatio = Mathf.Clamp01(minHalfPipeCenterFlatWidthRatio);
            maxHalfPipeCenterFlatWidthRatio = Mathf.Clamp(maxHalfPipeCenterFlatWidthRatio, minHalfPipeCenterFlatWidthRatio, 0.9f);

            if (minTransitionBlendLength > maxTransitionBlendLength) minTransitionBlendLength = maxTransitionBlendLength;
            if (minBankBlendLength > maxBankBlendLength) minBankBlendLength = maxBankBlendLength;
            if (minPitchBlendLength > maxPitchBlendLength) minPitchBlendLength = maxPitchBlendLength;
            if (minWidthBlendLength > maxWidthBlendLength) minWidthBlendLength = maxWidthBlendLength;
            if (minCrossSectionBlendLength > maxCrossSectionBlendLength) minCrossSectionBlendLength = maxCrossSectionBlendLength;

            if (minLateralSeparationLength > maxLateralSeparationLength) minLateralSeparationLength = maxLateralSeparationLength;
            if (minVerticalDivergenceDelay > maxVerticalDivergenceDelay) minVerticalDivergenceDelay = maxVerticalDivergenceDelay;
            if (minVerticalDivergenceLength > maxVerticalDivergenceLength) minVerticalDivergenceLength = maxVerticalDivergenceLength;
            if (minRouteLateralSeparation > maxRouteLateralSeparation) minRouteLateralSeparation = maxRouteLateralSeparation;

            if (minShortcuts > maxShortcuts) minShortcuts = maxShortcuts;
            if (minStraightSections > maxStraightSections) minStraightSections = maxStraightSections;
            if (minStunts > maxStunts) minStunts = maxStunts;
            if (minBoostPads > maxBoostPads) minBoostPads = maxBoostPads;

            if (!allowRouteSplits)
            {
                allowOverpasses = false;
                allowUnderpasses = false;
                allowLayeredRouteCrossings = false;
            }

            minTrackLength = Mathf.Max(1f, minTrackLength);
            maxTrackLength = Mathf.Max(minTrackLength, maxTrackLength);
            minRoadWidth = Mathf.Max(1f, minRoadWidth);
            maxRoadWidth = Mathf.Max(minRoadWidth, maxRoadWidth);
            minVerticalClearance = Mathf.Max(0f, minVerticalClearance);
            minRecoveryLength = Mathf.Max(0f, minRecoveryLength);
            maxMajorElevationChangesPerTrack = Mathf.Max(0, maxMajorElevationChangesPerTrack);
            maxLayeredSectionsPerTrack = Mathf.Max(0, maxLayeredSectionsPerTrack);
            maxRouteGroupsPerTrack = Mathf.Max(0, maxRouteGroupsPerTrack);
            maxOverUnderCrossingsPerTrack = Mathf.Max(0, maxOverUnderCrossingsPerTrack);
            maxRingsPerMacroSection = Mathf.Max(8, maxRingsPerMacroSection);
            maxTotalTrackRings = Mathf.Max(maxRingsPerMacroSection, maxTotalTrackRings);
        }

        private void OnValidate()
        {
            Validate();
        }
    }
}
