using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Definitions;
using TrackGeneration.Design;
using TrackGeneration.Planning;

namespace TrackGeneration.Macro
{
    /// <summary>Severity of one resolution issue.</summary>
    public enum ResolvedIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>One clamping/validation note produced while resolving designer settings.</summary>
    [Serializable]
    public class ResolvedConfigIssue
    {
        public ResolvedIssueSeverity Severity;
        public string Field;
        public string Message;

        public override string ToString() => $"[{Severity}] {Field}: {Message}";
    }

    /// <summary>A resolved feature rule: counts clamped by the rulebook and allow flags.</summary>
    [Serializable]
    public class ResolvedFeatureRule
    {
        public bool Enabled;
        public int MinimumCount;
        public int MaximumCount;
        public float OptionalWeight;
        public float MinimumSpacingMeters; // -1 = use default dangerous spacing
        public bool AllowInCompoundPatterns;

        public static ResolvedFeatureRule From(TrackFeatureRule rule, bool allowed, float speedMps, float defaultSpacing)
        {
            bool enabled = rule != null && rule.Enabled && allowed;
            return new ResolvedFeatureRule
            {
                Enabled = enabled,
                MinimumCount = enabled ? rule.EffectiveMinimum : 0,
                MaximumCount = enabled ? rule.EffectiveMaximum : 0,
                OptionalWeight = enabled ? rule.OptionalWeight : 0f,
                MinimumSpacingMeters = rule != null && rule.MinimumSpacingOverrideSeconds > 0f
                    ? rule.MinimumSpacingOverrideSeconds * speedMps
                    : defaultSpacing,
                AllowInCompoundPatterns = rule != null && rule.AllowInCompoundPatterns && enabled
            };
        }
    }

    /// <summary>
    /// A pure, validated snapshot of every final numerical value generation uses:
    /// designer values clamped against the <see cref="TrackConfig"/> rulebook, with
    /// time-based values already converted to meters via the design speed.
    ///
    /// It contains no preset switches and no hidden randomness — resolution is a
    /// deterministic function of (rulebook, settings). Impossible combinations are
    /// recorded in <see cref="Issues"/>; hard errors make the request invalid.
    /// </summary>
    public class ResolvedTrackGenerationConfig
    {
        // ── Provenance ──
        public List<ResolvedConfigIssue> Issues = new List<ResolvedConfigIssue>();
        public bool HasHardErrors { get { foreach (var i in Issues) if (i.Severity == ResolvedIssueSeverity.Error) return true; return false; } }

        // ── Speed & scale ──
        public float DesignSpeedKph;
        public float DesignSpeedMps;
        public float TargetLapTimeSeconds;
        public float TargetTrackLength;     // design speed × lap time (may grow)
        public float MaxTrackLength;        // designer cap clamped by rulebook
        public float PacingVariation;

        /// <summary>
        /// Track Editor rebuilds are authored variants, not random generation. In this
        /// mode the normal lap-length target is advisory and closure primitives receive
        /// additional safe fitting authority. Driveability validators remain active.
        /// </summary>
        public bool DesignerAuthoringMode;

        // ── Layout rhythm ──
        public int MinTurnCount;
        public int MaxTurnCount;
        public TurnDirectionPattern DirectionPattern;
        public float MinStraightLength;
        public float MaxStraightLength;
        public float CornerSequenceChance;

        // ── Corners & banking ──
        public float MinCurveRadius;
        public float MaxCurveRadius;
        public float MinimumLegalCurveRadius;
        public float MaxClosureCurveRadius;
        public float LegalCurveRadiusFloor => Mathf.Max(1f,
            MinimumLegalCurveRadius > 0f ? MinimumLegalCurveRadius : MinCurveRadius);
        public TurnFamilyWeights TurnWeights;
        public float BankingStrength;
        public float MaxBankAngle;
        public float FloorTiltFraction;   // fraction of the bank realized as geometric floor tilt (capped at 18°)

        // ── Transitions (meters) ──
        public TrackBlendCurve BlendCurve;
        public float GenericTransitionLength;
        public float BankTransitionLength;
        public float PitchTransitionLength;
        public float RollTransitionLength;
        public float WidthTransitionLength;
        public float CrossSectionTransitionLength;
        public float MaxBankRampAngle;

        // ── Connectors ──
        public float MinimumConnectorLength;        // meters
        public float ConnectorInheritanceStrength;  // 0..1
        public float MinimumBankReversalLength;     // meters
        public float SameDirectionBridgeLength;     // meters — straights shorter than this bridge their turn complex
        public bool AllowConnectorExpansion;
        public bool AllowConnectorAbsorption;

        // ── Safety & readability (meters) ──
        public float FeatureFitScale;
        public float DefaultApproachLength;
        public float DefaultRecoveryLength;
        public float DangerousSpacingLength;
        public float VisualPreviewLength;

        // ── Elevation ──
        public VerticalGenerationProfile VerticalProfile;
        public float TargetElevationAmplitude;
        public int MinMajorElevationSections;
        public int MaxMajorElevationSections;
        public float ElevationPreferredCarrierLength;
        public float ElevationRisingTargetMinimum;
        public float ElevationRisingTargetMaximum;
        public float ElevationRecoveryThreshold;
        public float ElevationRecoveryTargetFraction;
        public float MaxClimbAngle;
        public float MaxDropAngle;
        public float MaxCurvatureInducedG;
        public float MaxVerticalCurvatureRate;
        public ResolvedFeatureRule Crests;
        public ResolvedFeatureRule Bridges;
        public ResolvedFeatureRule Underpasses;
        public TrackGroundLevelPolicy GroundLevelPolicy;
        public float MaxElevationRange;

        // ── Features ──
        public ResolvedFeatureRule Jumps;
        public ResolvedFeatureRule Loops;
        public ResolvedFeatureRule Corkscrews;
        public ResolvedFeatureRule Spirals;
        public ResolvedFeatureRule HalfLoops;
        public ResolvedFeatureRule FullPipes;
        public ResolvedFeatureRule Camelbacks;
        public ResolvedFeatureRule HeartlineRolls;
        public ResolvedFeatureRule ZeroGRolls;
        public ResolvedFeatureRule DiveLoops;
        public ResolvedFeatureRule Sidewinders;
        public ResolvedFeatureRule Wallrides;
        public ResolvedFeatureRule Chicanes;
        public ResolvedFeatureRule SCurves;
        public ResolvedFeatureRule Hairpins;
        public ResolvedFeatureRule WideTurnarounds;
        public ResolvedFeatureRule Horseshoes;
        public ResolvedFeatureRule Cutbacks;

        // Procedural encounter rhythm. Track Editor exact overrides deliberately do
        // not consume these as restrictions; they guide generated content only.
        public bool EnforceProceduralRhythm;
        public int MaxConsecutiveSCurveEncounters;
        public int SCurveDiversityWindow;
        public int MaxSCurveEncountersPerWindow;
        public int SameFamilyCooldownEncounters;

        // ── Full pipes ──
        public float MinFullPipeLength;
        public float MaxFullPipeLength;
        public float PipeTransitionLength;
        public float FullPipeRadiusScale;
        public int MinFeatureGroups;
        public int MaxFeatureGroups;
        public float CompoundFeatureChance;
        public int MaxCompoundElements;
        public List<RequiredPatternEntry> RequiredPatterns = new List<RequiredPatternEntry>();

        // ── Loops ──
        public float MinLoopRadius;
        public float MaxLoopRadius;
        public float LoopApproachLength;
        public float LoopRecoveryLength;
        public float LoopClearance;

        // ── Half-loops ──
        public float MinHalfLoopRadius;
        public float MaxHalfLoopRadius;
        public float HalfLoopRolloutLength;
        public float HalfLoopApproachLength;
        public float HalfLoopRecoveryLength;

        // ── Corkscrews ──
        public float MinCorkscrewLength;
        public float MaxCorkscrewLength;
        public float MinCorkscrewRadius;
        public float MaxCorkscrewRadius;
        public float CorkscrewRollDegrees;
        public float MaxRollRateDegPerMeter;   // rulebook °/s converted at design speed
        public float CorkscrewApproachLength;
        public float CorkscrewRecoveryLength;
        public float CorkscrewClearance;

        // Quantized rotational-road grammar (runtime values derived from TrackConfig).
        public float RotationUnitDegrees;
        public int[] AllowedLoopRotationUnits;
        public int[] AllowedCorkscrewRotationUnits;
        public bool AllowHalfRotations;
        public bool AllowQuarterTurnTransitions;
        public int MaxLoopRotationUnits;
        public int MaxCorkscrewRotationUnits;
        public float NormalBankStepDegrees;
        public RotationalBlendPreset DefaultRotationalBlend;
        public float MaxRotationalSampleDistance;
        public float MaxRotationalForwardAngle;
        public float MaxRotationalRollAngle;
        public int MinSamplesPerRotationUnit;
        public float MinDistancePerRotationUnit;

        // ── Spirals ──
        public float MinSpiralRadius;
        public float MaxSpiralRadius;
        public int MinSpiralRevolutions;
        public int MaxSpiralRevolutions;
        public float MinSpiralClimbPerRevolution;
        public float MaxSpiralClimbPerRevolution;
        public float SpiralApproachLength;
        public float SpiralRecoveryLength;
        public float SpiralClearance;

        // ── Jumps (ballistic model) ──
        public float Gravity;
        public float JumpApproachLength;
        public float MinLaunchTransitionLength;
        public float MaxLaunchTransitionLength;
        public float MinJumpLaunchPitchDegrees;
        public float MaxJumpLaunchPitchDegrees;
        public float MinJumpAirtimeSeconds;
        public float MaxJumpAirtimeSeconds;
        public float MinLandingTransitionLength;
        /// <summary>
        /// Authoritative landing allowance used by topology length budgeting. It
        /// matches the solver's complete capture envelope so planning never admits
        /// content that the built landing later pushes beyond the lap-length cap.
        /// </summary>
        public float JumpLandingPlanningLength;
        public float MaxLandingTransitionLength;
        public float JumpRecoveryLength;
        public float MinJumpHeight;
        public float MaxJumpHeight;
        public float JumpLandingTolerance;
        public float TargetJumpApexHeight;
        public float JumpLipEmphasis;
        public float JumpCaptureSpeedVariation;

        // ── Clearance safety margin (dual-quarter road pairing) ──
        public float WallMaskSafetyMargin;

        // ── Quarters ──
        public int MinDualQuarters;
        public int MaxDualQuarters;
        public QuarterTypeOverride[] QuarterTypeOverrides = new QuarterTypeOverride[4];
        public bool PreventAdjacentDualQuarters;
        public bool AllowQ1Dual;
        public bool AllowQ4Dual;
        public DualQuarterChoiceType QuarterChoiceType;
        public float LaneSeparation;           // meters between the two landing mouths / exit lips
        public float QuarterCatchWidth;        // meters — broad shared convergence catch
        public float DualRoadWidth;            // meters — alternate road width
        public float RoadLengthTolerance;      // fraction of road A length
        public float NeutralTimeTolerance;     // fraction (0.04 = 4%)
        public bool RequireArchetypeDifferentiation;
        public DualQuarterBalancePolicy BalancePolicy;

        // ── Road & half-pipe ──
        public float RoadWidth;
        public TrackRoadProfileSettings RoadProfile;

        // ── Dynamic cross-section (circular bowl rounding; automatic catch walls retired) ──
        public bool DynamicTurnRoundingEnabled;
        public float TurnRoundingStrength;
        public bool CatchWallEnabled;
        public float CatchWallStrength;
        public float CatchWallMinimumDemand;

        // ── Clearance (wall-aware) ──
        public float VerticalClearance;         // rulebook clearance + wall/slab envelope

        /// <summary>
        /// Centerline distance UNRELATED track segments must keep in plan view unless
        /// they are vertically separated. Single source of truth: the built
        /// self-intersection validator rejects below exactly this value, and every
        /// plan-stage pre-check must test against AT LEAST this (plus a margin for
        /// 2D-model drift) or doomed plans pay for a full build before dying.
        /// </summary>
        public float UnrelatedCorridor => RoadWidth * 1.6f;

        // ── Mesh ──
        public bool ConsistentTextureTopology;
        public float TextureTopologyMetersPerRing;
        public float MeshMetersPerRing;
        public float FeatureMetersPerRing;
        public float MaxRingFacetAngle;
        public int MaxRingsPerSection;
        public int MaxTotalRings;
        public int RenderRingBudget;   // designer performance budget — retopology spreads it over huge tracks
        public List<int> SubdivisionLadder = new List<int>(); // approved ascending interval-count tiers

        // ── Dynamic topology (Stage D/E, experimental) ──
        public bool DynamicFeatureAdjacency;
        public bool UseCanonicalTurns;
        public bool ClosureAngleRelief;
        public bool RobustDualQuarterFit;
        public bool DirectionalCorkscrews;
        public bool SmoothCorkscrewFloor;

        // ── Closure ──
        public float ClosureReserveFraction;
        public float ClosurePositionTolerance;
        public float ClosureForwardTolerance;
        public float ClosureUpTolerance;
        public float ClosureWidthTolerance;
        public float ClosureBankTolerance;
        public float ClosurePitchTolerance;

        // ── Generation behavior ──
        public int MaxAttempts;
        public CandidateSelectionMode SelectionMode;
        public int CandidatesToScore;
        public GenerationFailurePolicy FailurePolicy;

        // ── Reference performance model ──
        public float ReferenceTopSpeedMps;
        public float ReferenceAcceleration;
        public float ReferenceBraking;
        public float ReferenceLateralAcceleration;
        public float ReferenceRollStability;
        public float ReferenceLandingRecovery;

        /// <summary>Converts seconds at design speed into meters.</summary>
        public float SecondsToDistance(float seconds) => DesignSpeedMps * seconds;

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves designer settings against the rulebook into the final numbers
        /// generation consumes. Deterministic; never throws — impossible combinations
        /// are reported as Error issues and the caller checks <see cref="HasHardErrors"/>.
        /// </summary>
        public static ResolvedTrackGenerationConfig Resolve(TrackConfig limits, TrackDesignerSettings settings)
        {
            var r = new ResolvedTrackGenerationConfig();

            if (limits == null)
            {
                r.Issue(ResolvedIssueSeverity.Error, "TrackConfig", "No TrackConfig rulebook assigned.");
                return r;
            }
            if (settings == null)
            {
                r.Issue(ResolvedIssueSeverity.Error, "DesignerSettings", "No designer settings provided.");
                return r;
            }

            limits.Validate(); // also performs additive TrackConfig schema migration
            settings.Sanitize();

            // ══ Speed & scale ══
            r.DesignSpeedKph = r.ClampReport(settings.Scale.DesignSpeedKph, limits.MinDesignSpeedKph, limits.MaxDesignSpeedKph, "Scale.DesignSpeedKph");
            r.DesignSpeedMps = r.DesignSpeedKph / 3.6f;
            r.TargetLapTimeSeconds = r.ClampReport(settings.Scale.TargetLapTimeSeconds, limits.MinTargetLapTime, limits.MaxTargetLapTime, "Scale.TargetLapTime");
            r.TargetTrackLength = Mathf.Clamp(r.DesignSpeedMps * r.TargetLapTimeSeconds, limits.MinTrackLength, limits.MaxTrackLength);
            r.MaxTrackLength = r.ClampReport(settings.Scale.MaxTrackLengthMeters, Mathf.Max(limits.MinTrackLength, r.TargetTrackLength * 0.75f), limits.MaxTrackLength, "Scale.MaxTrackLength");
            r.PacingVariation = Mathf.Clamp01(settings.Scale.PacingVariation);

            float S(float seconds) => r.DesignSpeedMps * seconds;

            // ══ Layout rhythm ══
            r.MinTurnCount = settings.Layout.MinTurnCount;
            r.MaxTurnCount = settings.Layout.MaxTurnCount;
            r.DirectionPattern = settings.Layout.DirectionPattern;
            r.MinStraightLength = S(r.ClampReport(settings.Layout.MinStraightSeconds, limits.MinStraightSeconds, limits.MaxStraightSeconds, "Layout.MinStraightSeconds"));
            r.MaxStraightLength = S(r.ClampReport(settings.Layout.MaxStraightSeconds, limits.MinStraightSeconds, limits.MaxStraightSeconds, "Layout.MaxStraightSeconds"));
            if (r.MaxStraightLength < r.MinStraightLength) r.MaxStraightLength = r.MinStraightLength;
            r.CornerSequenceChance = Mathf.Clamp01(settings.Layout.CornerSequenceChance);

            // ══ Corners & banking ══
            r.MinCurveRadius = r.ClampReport(settings.Corners.MinCurveRadius, limits.MinCurveRadius, limits.MaxCurveRadius, "Corners.MinCurveRadius");
            r.MaxCurveRadius = Mathf.Clamp(settings.Corners.MaxCurveRadius, r.MinCurveRadius, limits.MaxCurveRadius);
            r.MinimumLegalCurveRadius = limits.MinCurveRadius;
            // Styled corners honor the designer's visual range. Closure-only curves
            // may use the wider legal rulebook envelope to supply positional capacity.
            r.MaxClosureCurveRadius = Mathf.Max(r.MaxCurveRadius, limits.MaxClosureCurveRadius);
            r.TurnWeights = settings.Corners.TurnWeights.Clone();
            float requestedBankStrength = Mathf.Clamp01(settings.Corners.BankingStrength);
            r.NormalBankStepDegrees = limits.NormalBankStepDegrees;
            float requestedBank = r.ClampReport(settings.Corners.MaxBankAngle, 0f, limits.MaxBankAngle, "Corners.MaxBankAngle");
            r.MaxBankAngle = Mathf.Floor(requestedBank / Mathf.Max(1f, r.NormalBankStepDegrees)) * r.NormalBankStepDegrees;
            // Preserve the designer's strength intent while making the actual apex
            // target an approved bank increment. Intermediate field samples still
            // interpolate smoothly; only the selected target is discrete.
            float effectiveBank = Mathf.Floor(requestedBank * requestedBankStrength /
                                               Mathf.Max(1f, r.NormalBankStepDegrees)) * r.NormalBankStepDegrees;
            r.BankingStrength = r.MaxBankAngle > 0.001f
                ? Mathf.Clamp01(effectiveBank / r.MaxBankAngle)
                : 0f;
            r.FloorTiltFraction = Mathf.Clamp01(settings.Corners.FloorTiltStrength);

            // ══ Transitions ══
            r.BlendCurve = settings.Transitions.BlendCurve;
            r.GenericTransitionLength = S(r.ClampReport(settings.Transitions.GenericTransitionSeconds, limits.MinGenericTransitionSeconds, limits.MaxGenericTransitionSeconds, "Transitions.Generic"));
            r.BankTransitionLength = S(r.ClampReport(settings.Transitions.BankTransitionSeconds, limits.MinBankTransitionSeconds, limits.MaxBankTransitionSeconds, "Transitions.Bank"));
            r.PitchTransitionLength = S(r.ClampReport(settings.Transitions.PitchTransitionSeconds, limits.MinPitchTransitionSeconds, limits.MaxPitchTransitionSeconds, "Transitions.Pitch"));
            r.RollTransitionLength = S(r.ClampReport(settings.Transitions.RollTransitionSeconds, limits.MinRollTransitionSeconds, limits.MaxRollTransitionSeconds, "Transitions.Roll"));
            r.WidthTransitionLength = S(r.ClampReport(settings.Transitions.WidthTransitionSeconds, limits.MinWidthTransitionSeconds, limits.MaxWidthTransitionSeconds, "Transitions.Width"));
            r.CrossSectionTransitionLength = S(r.ClampReport(settings.Transitions.CrossSectionTransitionSeconds, limits.MinCrossSectionTransitionSeconds, limits.MaxCrossSectionTransitionSeconds, "Transitions.CrossSection"));
            r.MaxBankRampAngle = limits.MaxBankRampAngle;

            // ══ Connectors ══
            r.MinimumConnectorLength = S(r.ClampReport(settings.Transitions.MinimumConnectorSeconds, limits.MinConnectorSeconds, limits.MaxConnectorSeconds, "Transitions.MinimumConnector"));
            r.ConnectorInheritanceStrength = Mathf.Clamp01(settings.Transitions.ConnectorInheritanceStrength);
            r.MinimumBankReversalLength = S(r.ClampReport(settings.Transitions.MinimumBankReversalSeconds, limits.MinBankReversalSeconds, limits.MaxBankReversalSeconds, "Transitions.MinimumBankReversal"));
            r.SameDirectionBridgeLength = S(r.ClampReport(settings.Transitions.SameDirectionBridgeSeconds, limits.MinBridgeSeconds, limits.MaxBridgeSeconds, "Transitions.SameDirectionBridge"));
            r.AllowConnectorExpansion = settings.Transitions.AllowConnectorExpansion;
            r.AllowConnectorAbsorption = settings.Transitions.AllowConnectorAbsorption;

            // ══ Safety & readability ══
            r.FeatureFitScale = Mathf.Clamp(settings.Transitions.FeatureFitScale <= 0f
                ? 0.5f
                : settings.Transitions.FeatureFitScale, 0.25f, 1f);
            float Fit(float meters) => meters * r.FeatureFitScale;
            r.DefaultApproachLength = Fit(S(r.ClampReport(settings.Transitions.DefaultApproachSeconds, limits.MinApproachSeconds, limits.MaxApproachSeconds, "Transitions.DefaultApproach")));
            r.DefaultRecoveryLength = Fit(S(r.ClampReport(settings.Transitions.DefaultRecoverySeconds, limits.MinRecoverySeconds, limits.MaxRecoverySeconds, "Transitions.DefaultRecovery")));
            r.DangerousSpacingLength = S(r.ClampReport(settings.Transitions.DangerousSpacingSeconds, limits.MinDangerousSpacingSeconds, limits.MaxDangerousSpacingSeconds, "Transitions.DangerousSpacing"));
            r.VisualPreviewLength = S(r.ClampReport(settings.Transitions.VisualPreviewSeconds, limits.MinVisualPreviewSeconds, limits.MaxVisualPreviewSeconds, "Transitions.VisualPreview"));

            // ══ Road & half-pipe ══
            // The cross-section is authored as FLAT CENTER + WALL SIZE + WALL CURVE +
            // SCALE. Total road width is DERIVED, and the rulebook width window is
            // enforced by UNIFORM rescale (never by reshaping) so the authored
            // center/wall ratio — and the quarter-circle wall — survive every clamp.
            float roadScale = Mathf.Clamp(settings.Road.RoadScale, 0.25f, 5f);
            float flatCenter = Mathf.Max(2f, settings.Road.FlatCenterWidth);
            float wallSize = r.ClampReport(settings.Road.WallHeight, limits.MinHalfPipeSideHeight, limits.MaxHalfPipeSideHeight, "Road.WallHeight");
            float wallCurve = Mathf.Clamp01(settings.Road.WallCurve);

            float totalWidth = roadScale * (flatCenter + 2f * wallSize);
            if (totalWidth < limits.MinRoadWidth || totalWidth > limits.MaxRoadWidth)
            {
                float clampedTotal = Mathf.Clamp(totalWidth, limits.MinRoadWidth, limits.MaxRoadWidth);
                r.Issue(ResolvedIssueSeverity.Warning, "Road.RoadScale",
                    $"Derived road width {totalWidth:F0} m is outside the rulebook window " +
                    $"({limits.MinRoadWidth:F0}–{limits.MaxRoadWidth:F0} m) — uniformly rescaled to {clampedTotal:F0} m (ratio preserved).");
                roadScale *= clampedTotal / totalWidth;
                totalWidth = clampedTotal;
            }

            r.RoadWidth = totalWidth;
            r.RoadProfile = new TrackRoadProfileSettings
            {
                Shape = RoadCrossSectionShape.HalfPipe,
                SideHeight = wallSize * roadScale,
                WallCurve01 = wallCurve,
                CenterFlatWidthRatio = flatCenter / (flatCenter + 2f * wallSize),
                ProfileResolution = Mathf.Clamp(settings.Road.ProfileResolution, limits.MinHalfPipeProfileResolution, limits.MaxHalfPipeProfileResolution),
                ColliderProfileResolution = Mathf.Clamp(settings.Road.ColliderProfileResolution, 8, 96),
                SafetyLipHeight = r.ClampReport(settings.Road.SafetyLipHeight, limits.MinSafetyLipHeight, limits.MaxSafetyLipHeight, "Road.SafetyLipHeight") * roadScale,
                MinTurnCenterFlatRatio = Mathf.Clamp(settings.Road.MinimumTurnCenterFlatRatio, 0f, limits.MaxHalfPipeCenterFlatRatio),
                MaxOverhangAngleDeg = r.ClampReport(settings.Road.MaxOverhangAngle, 0f, limits.MaxOverhangAngleLimit, "Road.MaxOverhangAngle"),
                OverhangRadius = r.ClampReport(settings.Road.OverhangRadius, limits.MinOverhangRadius, limits.MaxOverhangRadius, "Road.OverhangRadius") * roadScale
            };

            r.DynamicTurnRoundingEnabled = settings.Road.DynamicTurnRounding;
            r.TurnRoundingStrength = Mathf.Clamp01(settings.Road.TurnRoundingStrength);
            // Ordinary road must remain a symmetric half-pipe. Keep the serialized
            // legacy settings readable for old scenes, but never turn corner demand
            // into an implicit wallride. Explicit feature builders own overhang.
            r.CatchWallEnabled = false;
            r.CatchWallStrength = Mathf.Clamp01(settings.Road.CatchWallStrength);
            r.CatchWallMinimumDemand = Mathf.Clamp01(settings.Road.CatchWallMinimumDemand);

            // Wall-aware clearance: the ordinary circular bowl occupies SideHeight
            // above the floor. The remaining margin covers the slab and explicit
            // feature morphs — ordinary banking no longer stretches one wall.
            float wallEnvelope = r.RoadProfile.SideHeight * 1.5f + 6f;
            r.VerticalClearance = limits.MinVerticalClearance + wallEnvelope;

            // ══ Elevation ══
            r.MaxElevationRange = limits.MaxElevationRange;
            VerticalGenerationIntent verticalIntent = VerticalGenerationProfilePolicy.Resolve(
                settings.Elevation.Profile,
                settings.Elevation.TargetElevationAmplitude,
                settings.Elevation.MinMajorElevationSections,
                settings.Elevation.MaxMajorElevationSections);
            r.VerticalProfile = verticalIntent.Profile;
            r.TargetElevationAmplitude = r.ClampReport(verticalIntent.TargetAmplitude, 0f, limits.MaxElevationRange, "Elevation.TargetAmplitude");
            r.MinMajorElevationSections = verticalIntent.MinMajorSections;
            r.MaxMajorElevationSections = verticalIntent.MaxMajorSections;
            r.ElevationPreferredCarrierLength = verticalIntent.PreferredCarrierLength;
            r.ElevationRisingTargetMinimum = verticalIntent.RisingTargetMinimum;
            r.ElevationRisingTargetMaximum = verticalIntent.RisingTargetMaximum;
            r.ElevationRecoveryThreshold = verticalIntent.RecoveryThreshold;
            r.ElevationRecoveryTargetFraction = verticalIntent.RecoveryTargetFraction;
            r.MaxClimbAngle = r.ClampReport(settings.Elevation.MaxClimbAngle, 1f, limits.MaxClimbAngle, "Elevation.MaxClimbAngle");
            r.MaxDropAngle = r.ClampReport(settings.Elevation.MaxDropAngle, 1f, limits.MaxDropAngle, "Elevation.MaxDropAngle");
            r.MaxCurvatureInducedG = Mathf.Clamp(settings.Elevation.MaxCurvatureInducedG, 1f, 50f);
            r.MaxVerticalCurvatureRate = Mathf.Clamp(settings.Elevation.MaxVerticalCurvatureRate, 0.0000001f, 0.001f);
            r.GroundLevelPolicy = settings.Elevation.GroundLevelPolicy;

            bool allowUnder = limits.AllowUnderpasses && settings.Elevation.GroundLevelPolicy == TrackGroundLevelPolicy.FreeFloating;
            if (settings.Elevation.Underpasses.Enabled && settings.Elevation.Underpasses.EffectiveMinimum > 0 && !allowUnder)
                r.Issue(ResolvedIssueSeverity.Warning, "Elevation.Underpasses",
                    "Underpasses require the FreeFloating ground policy (they dip below the start elevation) — disabled.");
            r.Crests = ResolvedFeatureRule.From(settings.Elevation.Crests, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Bridges = ResolvedFeatureRule.From(settings.Elevation.Bridges, limits.AllowBridges, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Underpasses = ResolvedFeatureRule.From(settings.Elevation.Underpasses, allowUnder, r.DesignSpeedMps, r.DangerousSpacingLength);

            // ══ Feature rules (allow-flag gated, with loud reporting) ══
            r.Jumps = r.GateRule(settings.Features.Jumps, limits.AllowJumps, "Features.Jumps");
            r.Loops = r.GateRule(settings.Features.Loops, limits.AllowLoops, "Features.Loops");
            r.Corkscrews = r.GateRule(settings.Features.Corkscrews, limits.AllowCorkscrews, "Features.Corkscrews");
            r.Spirals = r.GateRule(settings.Features.Spirals, limits.AllowSpirals, "Features.Spirals");
            r.HalfLoops = r.GateRule(settings.Features.HalfLoops, limits.AllowHalfLoops && limits.AllowLoops, "Features.HalfLoops");
            r.FullPipes = r.GateRule(settings.Features.FullPipes, limits.AllowFullPipes, "Features.FullPipes");
            r.Camelbacks = ResolvedFeatureRule.From(settings.Features.Camelbacks, true,
                r.DesignSpeedMps, r.DangerousSpacingLength);
            r.HeartlineRolls = ResolvedFeatureRule.From(settings.Features.HeartlineRolls, true,
                r.DesignSpeedMps, r.DangerousSpacingLength);
            r.ZeroGRolls = ResolvedFeatureRule.From(settings.Features.ZeroGRolls, true,
                r.DesignSpeedMps, r.DangerousSpacingLength);
            r.DiveLoops = ResolvedFeatureRule.From(settings.Features.DiveLoops,
                limits.AllowHalfLoops && limits.AllowLoops && limits.AllowHalfRotations,
                r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Sidewinders = ResolvedFeatureRule.From(settings.Features.Sidewinders,
                limits.AllowHalfLoops && limits.AllowLoops && limits.AllowHalfRotations,
                r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Wallrides = r.GateRule(settings.Features.Wallrides, limits.AllowWallRides, "Features.Wallrides");
            r.Chicanes = ResolvedFeatureRule.From(settings.Features.Chicanes, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.SCurves = ResolvedFeatureRule.From(settings.Features.SCurves, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Hairpins = ResolvedFeatureRule.From(settings.Features.Hairpins, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.WideTurnarounds = ResolvedFeatureRule.From(settings.Features.WideTurnarounds, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Horseshoes = ResolvedFeatureRule.From(settings.Features.Horseshoes, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Cutbacks = ResolvedFeatureRule.From(settings.Features.Cutbacks, true,
                r.DesignSpeedMps, r.DangerousSpacingLength);
            r.EnforceProceduralRhythm = settings.Features.EnforceProceduralRhythm;
            r.MaxConsecutiveSCurveEncounters = Mathf.Clamp(
                settings.Features.MaxConsecutiveSCurveEncounters, 1, 3);
            r.SCurveDiversityWindow = Mathf.Clamp(settings.Features.SCurveDiversityWindow, 3, 8);
            r.MaxSCurveEncountersPerWindow = Mathf.Clamp(
                settings.Features.MaxSCurveEncountersPerWindow, 1, r.SCurveDiversityWindow);
            r.SameFamilyCooldownEncounters = Mathf.Clamp(
                settings.Features.SameFamilyCooldownEncounters, 0, 3);
            r.MinFeatureGroups = settings.Features.MinFeatureGroups;
            r.MaxFeatureGroups = settings.Features.MaxFeatureGroups;
            r.CompoundFeatureChance = Mathf.Clamp01(settings.Features.CompoundFeatureChance);
            r.MaxCompoundElements = settings.Features.MaxCompoundElements;

            foreach (var p in settings.Features.RequiredPatterns)
            {
                if (p == null) continue;
                if (!r.PatternAllowed(p.Pattern, limits, out string why))
                {
                    r.Issue(ResolvedIssueSeverity.Error, "Features.RequiredPatterns",
                        $"Required pattern {p.Pattern} needs {why}, which the TrackConfig rulebook disallows.");
                    continue;
                }
                r.RequiredPatterns.Add(new RequiredPatternEntry { Pattern = p.Pattern, Count = p.Count });
            }

            // ══ Loops ══
            r.MinLoopRadius = limits.MinLoopRadius;
            r.MaxLoopRadius = limits.MaxLoopRadius;
            r.LoopApproachLength = Mathf.Max(r.DefaultApproachLength, Fit(S(limits.MinLoopApproachSeconds)));
            r.LoopRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, Fit(S(limits.MinLoopRecoverySeconds)));
            r.LoopClearance = limits.MinLoopClearance;

            // ══ Half-loops ══
            r.MinHalfLoopRadius = limits.MinHalfLoopRadius;
            r.MaxHalfLoopRadius = limits.MaxHalfLoopRadius;
            // Compact the authored rollout request, while the feature builder still
            // expands it whenever the hard roll-rate envelope needs more distance.
            r.HalfLoopRolloutLength = Fit(S(Mathf.Clamp(2.2f,
                limits.MinHalfLoopRolloutSeconds, limits.MaxHalfLoopRolloutSeconds)));
            r.HalfLoopApproachLength = Mathf.Max(r.DefaultApproachLength, Fit(S(limits.MinHalfLoopApproachSeconds)));
            r.HalfLoopRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, Fit(S(limits.MinHalfLoopRecoverySeconds)));

            // ══ Corkscrews ══
            r.MinCorkscrewLength = S(limits.MinCorkscrewSeconds);
            r.MaxCorkscrewLength = S(limits.MaxCorkscrewSeconds);
            r.MinCorkscrewRadius = Mathf.Max(limits.MinCorkscrewRadius, r.RoadWidth * 0.6f);
            r.MaxCorkscrewRadius = Mathf.Max(r.MinCorkscrewRadius, limits.MaxCorkscrewRadius);
            int baseCorkscrewUnits = 4;
            if (limits.AllowedCorkscrewRotationUnits == null ||
                System.Array.IndexOf(limits.AllowedCorkscrewRotationUnits, baseCorkscrewUnits) < 0)
                baseCorkscrewUnits = limits.AllowedCorkscrewRotationUnits != null &&
                                     limits.AllowedCorkscrewRotationUnits.Length > 0
                    ? limits.AllowedCorkscrewRotationUnits[0]
                    : 4;
            r.CorkscrewRollDegrees = baseCorkscrewUnits * limits.RotationUnitDegrees;
            r.MaxRollRateDegPerMeter = limits.MaxRollRateDegreesPerSecond / Mathf.Max(1f, r.DesignSpeedMps);
            r.CorkscrewApproachLength = Mathf.Max(r.DefaultApproachLength, Fit(S(limits.MinCorkscrewApproachSeconds)));
            r.CorkscrewRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, Fit(S(limits.MinCorkscrewRecoverySeconds)));
            r.CorkscrewClearance = limits.MinCorkscrewClearance;
            r.RotationUnitDegrees = limits.RotationUnitDegrees;
            r.AllowedLoopRotationUnits = (int[])limits.AllowedLoopRotationUnits.Clone();
            r.AllowedCorkscrewRotationUnits = (int[])limits.AllowedCorkscrewRotationUnits.Clone();
            r.AllowHalfRotations = limits.AllowHalfRotations;
            r.AllowQuarterTurnTransitions = limits.AllowQuarterTurnTransitions;
            r.MaxLoopRotationUnits = limits.MaxLoopRotationUnits;
            r.MaxCorkscrewRotationUnits = limits.MaxCorkscrewRotationUnits;
            r.DefaultRotationalBlend = limits.DefaultRotationalBlend;
            r.MaxRotationalSampleDistance = limits.MaxRotationalSampleDistance;
            r.MaxRotationalForwardAngle = limits.MaxRotationalForwardAngle;
            r.MaxRotationalRollAngle = limits.MaxRotationalRollAngle;
            r.MinSamplesPerRotationUnit = limits.MinSamplesPerRotationUnit;
            r.MinDistancePerRotationUnit = limits.MinDistancePerRotationUnit;

            // Roll-rate feasibility: the smoothstepped roll peaks at 1.5× the average
            // rate, so a corkscrew needs at least this length for its total roll.
            float minLenByRollRate = r.CorkscrewRollDegrees * 1.5f / Mathf.Max(0.01f, r.MaxRollRateDegPerMeter);
            if (minLenByRollRate > r.MaxCorkscrewLength)
                r.Issue(ResolvedIssueSeverity.Error, "Corkscrews",
                    $"A {r.CorkscrewRollDegrees:F0}° corkscrew needs {minLenByRollRate:F0}m at the legal roll rate, but the maximum corkscrew length is {r.MaxCorkscrewLength:F0}m.");
            else
                r.MinCorkscrewLength = Mathf.Max(r.MinCorkscrewLength, minLenByRollRate);

            // ══ Spirals ══
            r.MinSpiralRadius = limits.MinSpiralRadius;
            r.MaxSpiralRadius = limits.MaxSpiralRadius;
            // Defense in depth for legacy TrackConfig assets that have not yet passed
            // OnValidate: procedural generation and Track Editor rebuilds both obey the
            // same two-storey structural ceiling.
            r.MinSpiralRevolutions = Mathf.Clamp(limits.MinSpiralRevolutions, 1, 2);
            r.MaxSpiralRevolutions = Mathf.Clamp(
                limits.MaxSpiralRevolutions, r.MinSpiralRevolutions, 2);
            float spiralLayerClearance = Mathf.Max(r.VerticalClearance, limits.MinSpiralClearance);
            float easedClearanceStep = SectionFrameBuilders.RequiredSpiralClimbPerRevolution(
                spiralLayerClearance, r.MaxSpiralRevolutions);
            r.MinSpiralClimbPerRevolution = SectionFrameBuilders.QuantizeElevationUp(
                Mathf.Max(limits.MinSpiralClimbPerRevolution, easedClearanceStep), 5f);
            r.MaxSpiralClimbPerRevolution = Mathf.Max(r.MinSpiralClimbPerRevolution, limits.MaxSpiralClimbPerRevolution);
            if (r.MinSpiralClimbPerRevolution > limits.MaxSpiralClimbPerRevolution)
                r.Issue(ResolvedIssueSeverity.Warning, "Spirals",
                    $"Eased coil clearance ({spiralLayerClearance:F0}m walls included) forces a quantized " +
                    $"{r.MinSpiralClimbPerRevolution:F0}m climb per revolution, above the rulebook's {limits.MaxSpiralClimbPerRevolution:F0}m.");
            r.SpiralApproachLength = Mathf.Max(r.DefaultApproachLength, Fit(S(limits.MinSpiralApproachSeconds)));
            r.SpiralRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, Fit(S(limits.MinSpiralRecoverySeconds)));
            r.SpiralClearance = spiralLayerClearance;

            // ══ Full pipes ══
            r.MinFullPipeLength = S(r.ClampReport(settings.Features.MinFullPipeSeconds, limits.MinFullPipeSeconds, limits.MaxFullPipeSeconds, "Features.MinFullPipeSeconds"));
            r.MaxFullPipeLength = S(r.ClampReport(settings.Features.MaxFullPipeSeconds, limits.MinFullPipeSeconds, limits.MaxFullPipeSeconds, "Features.MaxFullPipeSeconds"));
            if (r.MaxFullPipeLength < r.MinFullPipeLength) r.MaxFullPipeLength = r.MinFullPipeLength;
            r.PipeTransitionLength = S(r.ClampReport(settings.Features.PipeTransitionSeconds, limits.MinPipeTransitionSeconds, limits.MaxPipeTransitionSeconds, "Features.PipeTransitionSeconds"));
            // Pipe radius = width × scale / 2, floored by the rulebook's craft/camera clearance.
            float minScale = limits.MinFullPipeRadius * 2f / Mathf.Max(1f, r.RoadWidth);
            r.FullPipeRadiusScale = Mathf.Max(Mathf.Clamp(settings.Features.FullPipeRadiusScale, 0.4f, 1.5f), minScale);

            // ══ Jumps (ballistic) ══
            r.Gravity = limits.ReferenceGravity;
            r.JumpApproachLength = Mathf.Max(r.DefaultApproachLength, Fit(S(limits.MinJumpApproachSeconds)));
            r.MinLaunchTransitionLength = S(limits.MinLaunchTransitionSeconds);
            r.MaxLaunchTransitionLength = S(limits.MaxLaunchTransitionSeconds);
            r.MinJumpLaunchPitchDegrees = limits.MinJumpLaunchPitchDegrees;
            r.MaxJumpLaunchPitchDegrees = limits.MaxJumpLaunchPitchDegrees;
            r.MinJumpAirtimeSeconds = limits.MinJumpAirtimeSeconds;
            r.MaxJumpAirtimeSeconds = limits.MaxJumpAirtimeSeconds;
            r.MinLandingTransitionLength = S(limits.MinLandingTransitionSeconds);
            r.JumpCaptureSpeedVariation = Mathf.Clamp(settings.Features.JumpCaptureSpeedVariation, 0f, 0.25f);
            // Keep two names because reports distinguish planning from solver limits,
            // but charge the complete legal landing envelope to the topology budget.
            // At Rollercoaster speeds the capture solver consistently needs the far
            // end of this envelope. Charging a shorter "typical" landing admitted
            // optional content that later became 55-70 km of locked geometry.
            float baseMaxLandingLength = S(limits.MaxLandingTransitionSeconds);
            r.MaxLandingTransitionLength = baseMaxLandingLength *
                                           (1f + 32f * r.JumpCaptureSpeedVariation);
            r.JumpLandingPlanningLength = r.MaxLandingTransitionLength;
            r.JumpRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, Fit(S(limits.MinJumpRecoverySeconds)));
            r.MinJumpHeight = limits.MinJumpHeight;
            r.MaxJumpHeight = limits.MaxJumpHeight;
            r.JumpLandingTolerance = limits.JumpLandingTolerance;
            r.TargetJumpApexHeight = Mathf.Max(1f, settings.Features.TargetJumpApexHeight);
            r.JumpLipEmphasis = Mathf.Clamp01(settings.Features.JumpLipEmphasis);

            r.WallMaskSafetyMargin = 3f;

            // ══ Quarters ══
            bool dualAllowed = limits.AllowDualRoadQuarters && limits.AllowJumps;
            r.MaxDualQuarters = dualAllowed
                ? Mathf.Min(settings.Quarters.MaximumDualQuarterCount, limits.MaxDualQuartersPerTrack)
                : 0;
            r.MinDualQuarters = Mathf.Min(settings.Quarters.MinimumDualQuarterCount, r.MaxDualQuarters);
            if (settings.Quarters.MinimumDualQuarterCount > 0 && !limits.AllowJumps)
                r.Issue(ResolvedIssueSeverity.Warning, "Quarters",
                    "Dual Road Quarters are entered and left by jumps, and the rulebook disallows jumps — resolving to 0 dual quarters.");
            else if (settings.Quarters.MinimumDualQuarterCount > 0 && !limits.AllowDualRoadQuarters)
                r.Issue(ResolvedIssueSeverity.Warning, "Quarters",
                    "AllowDualRoadQuarters is false in the TrackConfig rulebook — resolving to 0 dual quarters.");
            else if (settings.Quarters.MinimumDualQuarterCount > r.MaxDualQuarters)
                r.Issue(ResolvedIssueSeverity.Warning, "Quarters",
                    $"Requested minimum {settings.Quarters.MinimumDualQuarterCount} dual quarters exceeds the rulebook cap {limits.MaxDualQuartersPerTrack} — clamped.");

            r.QuarterChoiceType = settings.Quarters.ChoiceType;
            if (r.QuarterChoiceType != DualQuarterChoiceType.JumpSelection)
            {
                r.Issue(ResolvedIssueSeverity.Warning, "Quarters.ChoiceType",
                    $"{r.QuarterChoiceType} is not implemented yet — using JumpSelection (the route choice happens in the air).");
                r.QuarterChoiceType = DualQuarterChoiceType.JumpSelection;
            }

            r.QuarterTypeOverrides = settings.Quarters.QuarterTypeOverrides != null && settings.Quarters.QuarterTypeOverrides.Length == 4
                ? (QuarterTypeOverride[])settings.Quarters.QuarterTypeOverrides.Clone()
                : new QuarterTypeOverride[4];
            int forcedDual = 0;
            foreach (var o in r.QuarterTypeOverrides) if (o == QuarterTypeOverride.DualRoad) forcedDual++;
            if (forcedDual > r.MaxDualQuarters)
                r.Issue(ResolvedIssueSeverity.Warning, "Quarters.QuarterTypeOverrides",
                    $"{forcedDual} quarters are forced Dual but only {r.MaxDualQuarters} are allowed — later forced quarters resolve Single.");
            r.PreventAdjacentDualQuarters = settings.Quarters.PreventAdjacentDualQuarters;
            r.AllowQ1Dual = settings.Quarters.AllowQ1Dual;
            r.AllowQ4Dual = settings.Quarters.AllowQ4Dual;

            r.DualRoadWidth = r.RoadWidth * Mathf.Clamp(settings.Quarters.DualRoadWidthScale, 0.5f, 1.5f);

            // Dynamic separation floor: the two landing lanes are FULL half-pipe roads —
            // their mouths must clear each other's walls no matter what the designer or
            // rulebook window says (a 100 m road cannot land 24 m from its sibling).
            // Must sit ABOVE the road-pair clearance checks' thresholds: intentionally
            // parallel lanes placed exactly at the floor were failing those checks by
            // centimeters. +4 m clears the fitter's plan-time demand (validator + 3 m
            // drift margin) with room to spare for build drift at the throats.
            //
            // NOTE: raising this margin further trades a clearance failure for a WORSE
            // length/balance failure — a wider lateral offset forces road B to bulge out
            // more, so it comes out much longer than road A and fails the ~20% balance
            // gate. The real fix for guaranteed duals is vertical-separated lanes (road B
            // rides above/below road A, staying laterally close and short), not a wider
            // lateral gap. Kept at +4.
            float laneFloor = Mathf.Max(r.RoadWidth, r.DualRoadWidth)  // wider road's full envelope
                            + r.RoadProfile.SideHeight * 2f            // rising walls
                            + r.WallMaskSafetyMargin + 4f;
            float requestedLaneSep = r.ClampReport(settings.Quarters.LaneSeparationMeters,
                limits.MinLaneSeparation, limits.MaxLaneSeparation, "Quarters.LaneSeparation");
            r.LaneSeparation = Mathf.Max(requestedLaneSep, laneFloor);
            if (r.LaneSeparation > requestedLaneSep + 0.5f)
                r.Issue(ResolvedIssueSeverity.Info, "Quarters.LaneSeparation",
                    $"Raised to {r.LaneSeparation:F0} m so the two landing lanes clear each other's road envelope (road {r.RoadWidth:F0} m + walls).");
            // The shared catch must swallow BOTH arriving lanes plus aiming error:
            // the validator demands width ≥ builtLipSep + 16, and BUILT lips drift a
            // few meters wider than the planned separation — resolve with +24 so the
            // check keeps real headroom instead of failing by inches.
            r.QuarterCatchWidth = Mathf.Max(
                r.RoadWidth * Mathf.Clamp(settings.Quarters.CatchWidthScale, 1f, 2.5f),
                r.LaneSeparation + 24f);
            r.RoadLengthTolerance = Mathf.Clamp(settings.Quarters.RoadLengthTolerance, 0.02f, 1f);
            r.NeutralTimeTolerance = Mathf.Clamp(settings.Quarters.NeutralTimeTolerancePercent, 0.5f, 60f) / 100f;
            r.RequireArchetypeDifferentiation = settings.Quarters.RequireArchetypeDifferentiation;
            r.BalancePolicy = settings.Quarters.BalancePolicy;

            // ══ Mesh ══
            r.ConsistentTextureTopology = settings.Generation.ConsistentTextureTopology;
            r.TextureTopologyMetersPerRing = Mathf.Clamp(settings.Generation.TextureTopologyMetersPerRing,
                limits.MinMetersPerRing, Mathf.Min(2f, limits.MaxMetersPerRing));
            r.MeshMetersPerRing = Mathf.Clamp(settings.Generation.MetersPerRing, limits.MinMetersPerRing, limits.MaxMetersPerRing);
            r.FeatureMetersPerRing = Mathf.Clamp(settings.Generation.MetersPerRing * 0.6f, limits.MinFeatureMetersPerRing, limits.MaxFeatureMetersPerRing);
            r.MaxRingFacetAngle = Mathf.Clamp(settings.Generation.MaxFacetAngleDegrees, limits.MinFacetAngle, limits.MaxFacetAngle);
            r.MaxRingsPerSection = limits.MaxRingsPerMacroSection;
            r.MaxTotalRings = limits.MaxTotalTrackRings;
            r.RenderRingBudget = Mathf.Clamp(settings.Generation.TargetTotalRings, 2000, limits.MaxTotalTrackRings);
            r.SubdivisionLadder.AddRange(limits.SubdivisionLadder);

            // ══ Closure ══
            r.DynamicFeatureAdjacency = settings.Generation.DynamicFeatureAdjacency;
            r.UseCanonicalTurns = settings.Generation.UseCanonicalTurns;
            r.ClosureAngleRelief = settings.Generation.ClosureAngleRelief;
            r.RobustDualQuarterFit = settings.Generation.RobustDualQuarterFit;
            r.DirectionalCorkscrews = settings.Generation.DirectionalCorkscrews;
            r.SmoothCorkscrewFloor = settings.Generation.SmoothCorkscrewFloor;
            r.ClosureReserveFraction = settings.Generation.ClosureReserveFraction;
            r.ClosurePositionTolerance = limits.ClosurePositionTolerance;
            r.ClosureForwardTolerance = limits.ClosureForwardTolerance;
            r.ClosureUpTolerance = limits.ClosureUpTolerance;
            r.ClosureWidthTolerance = limits.ClosureWidthTolerance;
            r.ClosureBankTolerance = limits.ClosureBankTolerance;
            r.ClosurePitchTolerance = limits.ClosurePitchTolerance;

            // ══ Generation behavior ══
            r.MaxAttempts = settings.Generation.MaxAttempts;
            r.SelectionMode = settings.Generation.SelectionMode;
            r.CandidatesToScore = settings.Generation.CandidatesToScore;
            r.FailurePolicy = settings.Generation.FailurePolicy;

            // ══ Reference performance model ══
            r.ReferenceTopSpeedMps = limits.ReferenceTopSpeedMps;
            r.ReferenceAcceleration = limits.ReferenceAcceleration;
            r.ReferenceBraking = limits.ReferenceBraking;
            r.ReferenceLateralAcceleration = limits.ReferenceLateralAcceleration;
            r.ReferenceRollStability = limits.ReferenceRollStability;
            r.ReferenceLandingRecovery = limits.ReferenceLandingRecovery;

            r.ValidateBudgets();

            return r;
        }

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Rough length one instance of a feature needs (approach + body + recovery).</summary>
        public float EstimateFeatureFootprint(TrackPatternType pattern)
        {
            // Definition-native features own their complete approach/body/recovery
            // contract in data. Use it for procedural budgeting just as the compiler
            // does, rather than pretending they are ordinary corners.
            if (TrackFeatureDefinitionCatalog.TryGet(pattern, out TrackFeatureDefinition authored) &&
                authored.Enabled)
                return authored.PreferredTotalOccupiedLength(DesignSpeedMps);

            int MaxFullUnits(int[] allowed, int maximum)
            {
                int result = 0;
                if (allowed != null)
                    foreach (int units in allowed)
                        if (units >= 4 && units <= maximum && units % 4 == 0)
                            result = Mathf.Max(result, units);
                return Mathf.Max(4, result);
            }

            switch (pattern)
            {
                case TrackPatternType.FullLoop:
                    return 2f * Mathf.PI * (MinLoopRadius + MaxLoopRadius) * 0.5f * 1.25f *
                           (MaxFullUnits(AllowedLoopRotationUnits, MaxLoopRotationUnits) / 4f);
                case TrackPatternType.Corkscrew:
                {
                    float rateLength = MaxFullUnits(AllowedCorkscrewRotationUnits,
                        MaxCorkscrewRotationUnits) * RotationUnitDegrees * 1.5f /
                                       Mathf.Max(0.001f, MaxRollRateDegPerMeter);
                    return Mathf.Max((MinCorkscrewLength + MaxCorkscrewLength) * 0.5f, rateLength);
                }
                case TrackPatternType.Spiral:
                    return SpiralApproachLength + 2f * Mathf.PI * (MinSpiralRadius + MaxSpiralRadius) * 0.5f + SpiralRecoveryLength;
                case TrackPatternType.HalfLoopRollout:
                {
                    float rollout = Mathf.Max(HalfLoopRolloutLength, 180f * 1.5f / Mathf.Max(0.001f, MaxRollRateDegPerMeter));
                    return Mathf.PI * (MinHalfLoopRadius + MaxHalfLoopRadius) * 0.5f + rollout;
                }
                case TrackPatternType.JumpGap:
                case TrackPatternType.JumpToBankedLanding:
                    return JumpApproachLength + MaxLaunchTransitionLength + DesignSpeedMps * MaxJumpAirtimeSeconds + JumpLandingPlanningLength + JumpRecoveryLength;
                case TrackPatternType.FullPipe:
                    return DefaultApproachLength + (MinFullPipeLength + MaxFullPipeLength) * 0.5f + DefaultRecoveryLength;
                case TrackPatternType.HalfLoopToCorkscrew:
                {
                    float rollout = Mathf.Max(HalfLoopRolloutLength, 540f * 1.5f / Mathf.Max(0.001f, MaxRollRateDegPerMeter));
                    return Mathf.PI * (MinHalfLoopRadius + MaxHalfLoopRadius) * 0.5f + rollout * 0.8f;
                }
                case TrackPatternType.SpiralToCorkscrew:
                    return 2f * Mathf.PI * MinSpiralRadius + MinCorkscrewLength;
                case TrackPatternType.LoopToCorkscrew:
                    return (2f * Mathf.PI * MinLoopRadius * 1.25f + MinCorkscrewLength) * 0.85f;
                case TrackPatternType.DoubleCorkscrew:
                    return Mathf.Max(2f * MinCorkscrewLength,
                        8f * RotationUnitDegrees * 1.5f / Mathf.Max(0.001f, MaxRollRateDegPerMeter));
                case TrackPatternType.Hairpin:
                case TrackPatternType.SweeperIntoHairpin:
                    return Mathf.PI * MinCurveRadius + MinStraightLength;
                case TrackPatternType.Chicane:
                case TrackPatternType.DoubleApex:
                case TrackPatternType.TighteningCorner:
                case TrackPatternType.OpeningCorner:
                    return MinCurveRadius * 2.5f;
                case TrackPatternType.SCurve:
                case TrackPatternType.AlternatingRadiusSequence:
                    return Mathf.Min(MinCurveRadius * 2.5f,
                        SectionFrameBuilders.MaxDesignerSCurveLength);
                default:
                    return DefaultApproachLength + DefaultRecoveryLength + MinStraightLength;
            }
        }

        /// <summary>Whether a pattern's underlying features are allowed by the rulebook flags.</summary>
        private bool PatternAllowed(TrackPatternType pattern, TrackConfig limits, out string requirement)
        {
            bool HasFullRotation(int[] units, int maximum)
            {
                if (units == null) return false;
                foreach (int value in units)
                    if (value >= 4 && value <= maximum && value % 4 == 0) return true;
                return false;
            }

            requirement = "";
            switch (pattern)
            {
                case TrackPatternType.FullLoop:
                    requirement = "loops with an allowed full rotation count";
                    return limits.AllowLoops && HasFullRotation(limits.AllowedLoopRotationUnits,
                        limits.MaxLoopRotationUnits);
                case TrackPatternType.Corkscrew:
                    requirement = "corkscrews with an allowed full rotation count";
                    return limits.AllowCorkscrews && HasFullRotation(limits.AllowedCorkscrewRotationUnits,
                        limits.MaxCorkscrewRotationUnits);
                case TrackPatternType.DoubleCorkscrew:
                    requirement = "an allowed 8-unit corkscrew";
                    return limits.AllowCorkscrews && limits.MaxCorkscrewRotationUnits >= 8 &&
                           System.Array.IndexOf(limits.AllowedCorkscrewRotationUnits, 8) >= 0;
                case TrackPatternType.Spiral: requirement = "spirals"; return limits.AllowSpirals;
                case TrackPatternType.HalfLoopRollout: requirement = "half-loops"; return limits.AllowHalfLoops && limits.AllowLoops && limits.AllowHalfRotations;
                case TrackPatternType.HalfLoopToCorkscrew: requirement = "half-loops and corkscrews"; return limits.AllowHalfLoops && limits.AllowLoops && limits.AllowCorkscrews && limits.AllowHalfRotations;
                case TrackPatternType.SpiralToCorkscrew:
                    requirement = "spirals and a full corkscrew";
                    return limits.AllowSpirals && limits.AllowCorkscrews &&
                           HasFullRotation(limits.AllowedCorkscrewRotationUnits,
                               limits.MaxCorkscrewRotationUnits);
                case TrackPatternType.LoopToCorkscrew:
                    requirement = "full loop and corkscrew rotations";
                    return limits.AllowLoops && limits.AllowCorkscrews &&
                           HasFullRotation(limits.AllowedLoopRotationUnits, limits.MaxLoopRotationUnits) &&
                           HasFullRotation(limits.AllowedCorkscrewRotationUnits,
                               limits.MaxCorkscrewRotationUnits);
                case TrackPatternType.JumpGap:
                case TrackPatternType.JumpToBankedLanding: requirement = "jumps"; return limits.AllowJumps;
                case TrackPatternType.FullPipe: requirement = "full pipes"; return limits.AllowFullPipes;
                case TrackPatternType.WallrideTurn: requirement = "wallrides"; return limits.AllowWallRides;
                default: return true; // corner patterns are always buildable
            }
        }

        /// <summary>
        /// Budget feasibility checks: required content must fit the maximum length,
        /// branch minimums must fit the lap, elevation must fit the range.
        /// </summary>
        private void ValidateBudgets()
        {
            // Required feature length vs maximum lap length.
            float requiredLength = 0f;
            void AddRule(ResolvedFeatureRule rule, TrackPatternType type)
            {
                if (rule.Enabled && rule.MinimumCount > 0)
                    requiredLength += rule.MinimumCount * (EstimateFeatureFootprint(type) + DangerousSpacingLength);
            }
            AddRule(Jumps, TrackPatternType.JumpGap);
            AddRule(Loops, TrackPatternType.FullLoop);
            AddRule(Corkscrews, TrackPatternType.Corkscrew);
            AddRule(Spirals, TrackPatternType.Spiral);
            AddRule(HalfLoops, TrackPatternType.HalfLoopRollout);
            AddRule(FullPipes, TrackPatternType.FullPipe);
            AddRule(Camelbacks, TrackPatternType.Camelback);
            AddRule(HeartlineRolls, TrackPatternType.HeartlineRoll);
            AddRule(ZeroGRolls, TrackPatternType.ZeroGRoll);
            AddRule(DiveLoops, TrackPatternType.DiveLoop);
            AddRule(Sidewinders, TrackPatternType.Sidewinder);
            AddRule(Hairpins, TrackPatternType.Hairpin);
            AddRule(WideTurnarounds, TrackPatternType.WideTurnaround);
            AddRule(Horseshoes, TrackPatternType.Horseshoe);
            AddRule(Cutbacks, TrackPatternType.Cutback);
            AddRule(Chicanes, TrackPatternType.Chicane);
            AddRule(SCurves, TrackPatternType.SCurve);
            foreach (var p in RequiredPatterns)
                requiredLength += p.Count * (EstimateFeatureFootprint(p.Pattern) + DangerousSpacingLength);

            // Corners and closure reserve need room too (eased arcs run longer than circular).
            float minCornerArc = MinTurnCount * (60f * Mathf.Deg2Rad * MinCurveRadius
                / (1f - Planning.SectionFrameBuilders.ArcCurvatureEaseFraction));
            float closureReserve = TargetTrackLength * ClosureReserveFraction;
            float totalRequired = requiredLength + minCornerArc + closureReserve;

            if (totalRequired > MaxTrackLength)
                Issue(ResolvedIssueSeverity.Error, "Budget",
                    $"Required content needs ≈{totalRequired / 1000f:F1}km (features {requiredLength / 1000f:F1}km + corners {minCornerArc / 1000f:F1}km + closure reserve {closureReserve / 1000f:F1}km), but Maximum Track Length is {MaxTrackLength / 1000f:F1}km.");
            else if (totalRequired > TargetTrackLength)
                Issue(ResolvedIssueSeverity.Info, "Budget",
                    $"Required content (≈{totalRequired / 1000f:F1}km) exceeds the target lap ({TargetTrackLength / 1000f:F1}km) — the lap will grow, up to the {MaxTrackLength / 1000f:F1}km cap.");

            // Dual-quarter gate overhead vs lap time: each dual quarter costs an entry
            // choice jump, an exit convergence jump, an approach and a recovery.
            if (MinDualQuarters > 0)
            {
                float gateOverheadSeconds = 2f * (MaxLaunchTransitionLength / Mathf.Max(1f, DesignSpeedMps)
                                                  + MaxJumpAirtimeSeconds
                                                  + JumpLandingPlanningLength / Mathf.Max(1f, DesignSpeedMps))
                                          + (JumpRecoveryLength + DefaultApproachLength) / Mathf.Max(1f, DesignSpeedMps);
                float gateTime = MinDualQuarters * gateOverheadSeconds;
                if (gateTime > TargetLapTimeSeconds * 0.5f)
                    Issue(ResolvedIssueSeverity.Error, "Quarters",
                        $"{MinDualQuarters} dual quarters need ≈{gateTime:F0}s of gate jumps alone, which cannot fit inside a {TargetLapTimeSeconds:F0}s target lap.");
            }

            // Elevation amplitude must be reachable within the climb angle over the lap.
            if (TargetElevationAmplitude > 1f)
            {
                float climbCapacity = TargetTrackLength * 0.35f * Mathf.Tan(MaxClimbAngle * Mathf.Deg2Rad);
                if (TargetElevationAmplitude > climbCapacity)
                    Issue(ResolvedIssueSeverity.Warning, "Elevation",
                        $"Target amplitude {TargetElevationAmplitude:F0}m likely unreachable: at {MaxClimbAngle:F0}° max climb the lap can carry ≈{climbCapacity:F0}m.");
            }

            // Ring budget sanity.
            float validationSpacing = ConsistentTextureTopology
                ? TextureTopologyMetersPerRing
                : MeshMetersPerRing;
            float expectedRings = TargetTrackLength / Mathf.Max(0.5f, validationSpacing) * 1.6f;
            if (expectedRings > MaxTotalRings)
                Issue(ResolvedIssueSeverity.Warning, "Mesh",
                    $"Expected ring count ≈{expectedRings:F0} exceeds the rulebook budget {MaxTotalRings} — increase the active topology spacing or shorten the lap.");
        }

        private ResolvedFeatureRule GateRule(TrackFeatureRule rule, bool allowed, string field)
        {
            if (rule != null && rule.Enabled && rule.EffectiveMinimum > 0 && !allowed)
                Issue(ResolvedIssueSeverity.Error, field,
                    "Required by the designer settings, but disallowed by the TrackConfig rulebook.");
            else if (rule != null && rule.Enabled && !allowed)
                Issue(ResolvedIssueSeverity.Warning, field,
                    "Enabled in the designer settings, but disallowed by the TrackConfig rulebook — none will be generated.");
            return ResolvedFeatureRule.From(rule, allowed, DesignSpeedMps, DangerousSpacingLength);
        }

        private float ClampReport(float value, float min, float max, string field)
        {
            float clamped = Mathf.Clamp(value, min, max);
            if (!Mathf.Approximately(clamped, value))
                Issue(ResolvedIssueSeverity.Warning, field, $"Clamped from {value:G5} to {clamped:G5} by the rulebook.");
            return clamped;
        }

        private void Issue(ResolvedIssueSeverity severity, string field, string message)
        {
            Issues.Add(new ResolvedConfigIssue { Severity = severity, Field = field, Message = message });
        }
    }
}
