using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
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
        public float MaxClosureCurveRadius;
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
        public float DefaultApproachLength;
        public float DefaultRecoveryLength;
        public float DangerousSpacingLength;
        public float VisualPreviewLength;

        // ── Elevation ──
        public float TargetElevationAmplitude;
        public int MinMajorElevationSections;
        public int MaxMajorElevationSections;
        public float MaxClimbAngle;
        public float MaxDropAngle;
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
        public ResolvedFeatureRule Wallrides;
        public ResolvedFeatureRule Chicanes;
        public ResolvedFeatureRule SCurves;
        public ResolvedFeatureRule Hairpins;

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
        public float MinJumpAirtimeSeconds;
        public float MaxJumpAirtimeSeconds;
        public float MinLandingTransitionLength;
        public float MaxLandingTransitionLength;
        public float JumpRecoveryLength;
        public float MinJumpHeight;
        public float MaxJumpHeight;
        public float JumpLandingTolerance;

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

        // ── Dynamic cross-section (turn rounding + catch walls) ──
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
        public float MeshMetersPerRing;
        public float FeatureMetersPerRing;
        public float MaxRingFacetAngle;
        public int MaxRingsPerSection;
        public int MaxTotalRings;
        public int RenderRingBudget;   // designer performance budget — retopology spreads it over huge tracks
        public List<int> SubdivisionLadder = new List<int>(); // approved ascending interval-count tiers

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
            r.DefaultApproachLength = S(r.ClampReport(settings.Transitions.DefaultApproachSeconds, limits.MinApproachSeconds, limits.MaxApproachSeconds, "Transitions.DefaultApproach"));
            r.DefaultRecoveryLength = S(r.ClampReport(settings.Transitions.DefaultRecoverySeconds, limits.MinRecoverySeconds, limits.MaxRecoverySeconds, "Transitions.DefaultRecovery"));
            r.DangerousSpacingLength = S(r.ClampReport(settings.Transitions.DangerousSpacingSeconds, limits.MinDangerousSpacingSeconds, limits.MaxDangerousSpacingSeconds, "Transitions.DangerousSpacing"));
            r.VisualPreviewLength = S(r.ClampReport(settings.Transitions.VisualPreviewSeconds, limits.MinVisualPreviewSeconds, limits.MaxVisualPreviewSeconds, "Transitions.VisualPreview"));

            // ══ Road & half-pipe ══
            r.RoadWidth = r.ClampReport(settings.Road.RoadWidth, limits.MinRoadWidth, limits.MaxRoadWidth, "Road.RoadWidth");
            r.RoadProfile = new TrackRoadProfileSettings
            {
                Shape = RoadCrossSectionShape.HalfPipe,
                SideHeight = r.ClampReport(settings.Road.HalfPipeSideHeight, limits.MinHalfPipeSideHeight, limits.MaxHalfPipeSideHeight, "Road.HalfPipeSideHeight"),
                CurveStrength = r.ClampReport(settings.Road.WallCurveStrength, limits.MinHalfPipeCurveStrength, limits.MaxHalfPipeCurveStrength, "Road.WallCurveStrength"),
                WallAngle = r.ClampReport(settings.Road.MaxWallAngle, limits.MinHalfPipeWallAngle, limits.MaxHalfPipeWallAngle, "Road.MaxWallAngle"),
                CenterFlatWidthRatio = r.ClampReport(settings.Road.CenterFlatWidthRatio, limits.MinHalfPipeCenterFlatRatio, limits.MaxHalfPipeCenterFlatRatio, "Road.CenterFlatWidthRatio"),
                ProfileResolution = Mathf.Clamp(settings.Road.ProfileResolution, limits.MinHalfPipeProfileResolution, limits.MaxHalfPipeProfileResolution),
                SafetyLipHeight = r.ClampReport(settings.Road.SafetyLipHeight, limits.MinSafetyLipHeight, limits.MaxSafetyLipHeight, "Road.SafetyLipHeight"),
                MinTurnCenterFlatRatio = Mathf.Clamp(settings.Road.MinimumTurnCenterFlatRatio, 0f, limits.MaxHalfPipeCenterFlatRatio),
                MaxOverhangAngleDeg = r.ClampReport(settings.Road.MaxOverhangAngle, 0f, limits.MaxOverhangAngleLimit, "Road.MaxOverhangAngle"),
                OverhangRadius = r.ClampReport(settings.Road.OverhangRadius, limits.MinOverhangRadius, limits.MaxOverhangRadius, "Road.OverhangRadius")
            };

            r.DynamicTurnRoundingEnabled = settings.Road.DynamicTurnRounding;
            r.TurnRoundingStrength = Mathf.Clamp01(settings.Road.TurnRoundingStrength);
            r.CatchWallEnabled = settings.Road.OutsideCatchWall;
            r.CatchWallStrength = Mathf.Clamp01(settings.Road.CatchWallStrength);
            r.CatchWallMinimumDemand = Mathf.Clamp01(settings.Road.CatchWallMinimumDemand);

            // Wall-aware clearance: a road's rideable walls occupy SideHeight above the
            // floor (up to ~1.5× when bank-boosted) plus the slab below — over/under
            // crossings must clear all of it, not just the craft.
            float wallEnvelope = r.RoadProfile.SideHeight * 1.5f + 6f;
            r.VerticalClearance = limits.MinVerticalClearance + wallEnvelope;

            // ══ Elevation ══
            r.MaxElevationRange = limits.MaxElevationRange;
            r.TargetElevationAmplitude = r.ClampReport(settings.Elevation.TargetElevationAmplitude, 0f, limits.MaxElevationRange, "Elevation.TargetAmplitude");
            r.MinMajorElevationSections = settings.Elevation.MinMajorElevationSections;
            r.MaxMajorElevationSections = settings.Elevation.MaxMajorElevationSections;
            r.MaxClimbAngle = r.ClampReport(settings.Elevation.MaxClimbAngle, 1f, limits.MaxClimbAngle, "Elevation.MaxClimbAngle");
            r.MaxDropAngle = r.ClampReport(settings.Elevation.MaxDropAngle, 1f, limits.MaxDropAngle, "Elevation.MaxDropAngle");
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
            r.Wallrides = r.GateRule(settings.Features.Wallrides, limits.AllowWallRides, "Features.Wallrides");
            r.Chicanes = ResolvedFeatureRule.From(settings.Features.Chicanes, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.SCurves = ResolvedFeatureRule.From(settings.Features.SCurves, true, r.DesignSpeedMps, r.DangerousSpacingLength);
            r.Hairpins = ResolvedFeatureRule.From(settings.Features.Hairpins, true, r.DesignSpeedMps, r.DangerousSpacingLength);
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
            r.LoopApproachLength = Mathf.Max(r.DefaultApproachLength, S(limits.MinLoopApproachSeconds));
            r.LoopRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, S(limits.MinLoopRecoverySeconds));
            r.LoopClearance = limits.MinLoopClearance;

            // ══ Half-loops ══
            r.MinHalfLoopRadius = limits.MinHalfLoopRadius;
            r.MaxHalfLoopRadius = limits.MaxHalfLoopRadius;
            // Preset default ≈ 2.2 s rollout, clamped to the rulebook window.
            r.HalfLoopRolloutLength = S(Mathf.Clamp(2.2f, limits.MinHalfLoopRolloutSeconds, limits.MaxHalfLoopRolloutSeconds));
            r.HalfLoopApproachLength = Mathf.Max(r.DefaultApproachLength, S(limits.MinHalfLoopApproachSeconds));
            r.HalfLoopRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, S(limits.MinHalfLoopRecoverySeconds));

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
            r.CorkscrewApproachLength = Mathf.Max(r.DefaultApproachLength, S(limits.MinCorkscrewApproachSeconds));
            r.CorkscrewRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, S(limits.MinCorkscrewRecoverySeconds));
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
            r.MinSpiralRevolutions = limits.MinSpiralRevolutions;
            r.MaxSpiralRevolutions = limits.MaxSpiralRevolutions;
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
            r.SpiralApproachLength = Mathf.Max(r.DefaultApproachLength, S(limits.MinSpiralApproachSeconds));
            r.SpiralRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, S(limits.MinSpiralRecoverySeconds));
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
            r.JumpApproachLength = Mathf.Max(r.DefaultApproachLength, S(limits.MinJumpApproachSeconds));
            r.MinLaunchTransitionLength = S(limits.MinLaunchTransitionSeconds);
            r.MaxLaunchTransitionLength = S(limits.MaxLaunchTransitionSeconds);
            r.MinJumpAirtimeSeconds = limits.MinJumpAirtimeSeconds;
            r.MaxJumpAirtimeSeconds = limits.MaxJumpAirtimeSeconds;
            r.MinLandingTransitionLength = S(limits.MinLandingTransitionSeconds);
            r.MaxLandingTransitionLength = S(limits.MaxLandingTransitionSeconds);
            r.JumpRecoveryLength = Mathf.Max(r.DefaultRecoveryLength, S(limits.MinJumpRecoverySeconds));
            r.MinJumpHeight = limits.MinJumpHeight;
            r.MaxJumpHeight = limits.MaxJumpHeight;
            r.JumpLandingTolerance = limits.JumpLandingTolerance;

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
            r.RoadLengthTolerance = Mathf.Clamp(settings.Quarters.RoadLengthTolerance, 0.02f, 0.6f);
            r.NeutralTimeTolerance = Mathf.Clamp(settings.Quarters.NeutralTimeTolerancePercent, 0.5f, 20f) / 100f;
            r.RequireArchetypeDifferentiation = settings.Quarters.RequireArchetypeDifferentiation;
            r.BalancePolicy = settings.Quarters.BalancePolicy;

            // ══ Mesh ══
            r.MeshMetersPerRing = Mathf.Clamp(settings.Generation.MetersPerRing, limits.MinMetersPerRing, limits.MaxMetersPerRing);
            r.FeatureMetersPerRing = Mathf.Clamp(settings.Generation.MetersPerRing * 0.6f, limits.MinFeatureMetersPerRing, limits.MaxFeatureMetersPerRing);
            r.MaxRingFacetAngle = Mathf.Clamp(settings.Generation.MaxFacetAngleDegrees, limits.MinFacetAngle, limits.MaxFacetAngle);
            r.MaxRingsPerSection = limits.MaxRingsPerMacroSection;
            r.MaxTotalRings = limits.MaxTotalTrackRings;
            r.RenderRingBudget = Mathf.Clamp(settings.Generation.TargetTotalRings, 2000, limits.MaxTotalTrackRings);
            r.SubdivisionLadder.AddRange(limits.SubdivisionLadder);

            // ══ Closure ══
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
                    return JumpApproachLength + MaxLaunchTransitionLength + DesignSpeedMps * MaxJumpAirtimeSeconds + MaxLandingTransitionLength + JumpRecoveryLength;
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
                case TrackPatternType.SCurve:
                case TrackPatternType.DoubleApex:
                case TrackPatternType.TighteningCorner:
                case TrackPatternType.OpeningCorner:
                case TrackPatternType.AlternatingRadiusSequence:
                    return MinCurveRadius * 2.5f;
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
            AddRule(Hairpins, TrackPatternType.Hairpin);
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
                                                  + MaxLandingTransitionLength / Mathf.Max(1f, DesignSpeedMps))
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
            float expectedRings = TargetTrackLength / Mathf.Max(0.5f, MeshMetersPerRing) * 1.6f;
            if (expectedRings > MaxTotalRings)
                Issue(ResolvedIssueSeverity.Warning, "Mesh",
                    $"Expected ring count ≈{expectedRings:F0} exceeds the rulebook budget {MaxTotalRings} — increase MetersPerRing or shorten the lap.");
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
