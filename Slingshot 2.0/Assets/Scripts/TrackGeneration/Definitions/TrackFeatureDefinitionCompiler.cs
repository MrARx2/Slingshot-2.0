using System.Collections.Generic;
using System.Linq;
using System;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using UnityEngine;

namespace TrackGeneration.Definitions
{
    /// <summary>
    /// Boundary between authored feature data and geometry solvers. Existing specialized
    /// math remains intact behind adapters while identity, composition and common distance
    /// ownership move into definitions.
    /// </summary>
    public static class TrackFeatureDefinitionCompiler
    {
        /// <summary>
        /// Deterministic authoring smoke gate for every definition-owned solver. Legacy
        /// PrimitiveSequence assets are schema/identity checked by the catalog and keep
        /// their established builders; specialized solvers additionally have to emit
        /// finite, stamped frames under the supplied rulebook.
        /// </summary>
        public static bool TrySmokeCompileCatalog(
            ResolvedTrackGenerationConfig cfg,
            out int compiledCount,
            out List<string> errors)
        {
            compiledCount = 0;
            errors = new List<string>();
            if (cfg == null)
            {
                errors.Add("No resolved Track Generator configuration was supplied.");
                return false;
            }

            foreach (TrackFeatureDefinition definition in TrackFeatureDefinitionCatalog.All)
            {
                if (definition == null || !definition.Enabled ||
                    definition.Solver == FeatureDefinitionSolver.PrimitiveSequence)
                    continue;

                try
                {
                    uint seed = StableDefinitionSeed(definition.StableId);
                    var rng = new Unity.Mathematics.Random(seed);
                    var sections = new List<TrackMacroSectionDefinition>();
                    bool planned = definition.TopologyRole == TopologyRole.TurnRealization
                        ? TryPlanTurnDefinition(
                            definition, cfg, definition.Turn.HeadingDegrees.Preferred,
                            $"DefinitionSmoke_{definition.StableId}", sections, out _)
                        : TryPlanDefinition(
                            definition, cfg, $"DefinitionSmoke_{definition.StableId}",
                            ref rng, sections);
                    if (!planned || sections.Count == 0)
                    {
                        errors.Add($"{definition.DisplayName}: authoritative solver produced no legal section.");
                        continue;
                    }

                    TrackConnectionFrame entry = TrackConnectionFrame.Origin(cfg.RoadWidth);
                    bool valid = true;
                    for (int i = 0; i < sections.Count; i++)
                    {
                        TrackMacroSectionDefinition section = sections[i];
                        if (section == null || section.FeatureDefinitionId != definition.StableId ||
                            string.IsNullOrWhiteSpace(section.FeatureDefinitionContentHash))
                        {
                            errors.Add($"{definition.DisplayName}: section {i} lost its definition identity stamp.");
                            valid = false;
                            break;
                        }

                        TrackConnectionFrame[] frames = SectionFrameBuilders.BuildSectionFrames(
                            entry, section, FrameBuildContext.From(cfg));
                        if (frames == null || frames.Length < 2 || frames.Any(frame => !IsFinite(frame)))
                        {
                            errors.Add($"{definition.DisplayName}: section {i} emitted missing or non-finite frames.");
                            valid = false;
                            break;
                        }
                        entry = frames[frames.Length - 1];
                    }

                    if (valid) compiledCount++;
                }
                catch (Exception exception)
                {
                    errors.Add($"{definition.DisplayName}: {exception.GetType().Name}: {exception.Message}");
                }
            }

            return errors.Count == 0;
        }

        private static uint StableDefinitionSeed(string stableId)
        {
            uint hash = 2166136261u;
            string text = stableId ?? "";
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash == 0u ? 1u : hash;
        }

        private static bool IsFinite(in TrackConnectionFrame frame) =>
            IsFinite(frame.Position.x) && IsFinite(frame.Position.y) && IsFinite(frame.Position.z) &&
            IsFinite(frame.Forward.x) && IsFinite(frame.Forward.y) && IsFinite(frame.Forward.z) &&
            IsFinite(frame.Right.x) && IsFinite(frame.Right.y) && IsFinite(frame.Right.z) &&
            IsFinite(frame.Up.x) && IsFinite(frame.Up.y) && IsFinite(frame.Up.z) &&
            IsFinite(frame.Width) && IsFinite(frame.ArcLength) && IsFinite(frame.PitchAngle) &&
            IsFinite(frame.BankAngle) && IsFinite(frame.HorizontalCurvature) &&
            IsFinite(frame.VerticalCurvature) && IsFinite(frame.VerticalCurvatureRate);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool TryPlanPattern(
            TrackPatternType patternType,
            ResolvedTrackGenerationConfig cfg,
            string patternId,
            ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
            => TryPlanPattern(patternType, cfg, patternId, ref rng, output, out _);

        public static bool TryPlanPattern(
            TrackPatternType patternType,
            ResolvedTrackGenerationConfig cfg,
            string patternId,
            ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output,
            out string failureReason)
        {
            failureReason = "";
            if (cfg == null)
            {
                failureReason = $"Pattern {patternType} has no resolved generation config.";
                return false;
            }
            if (output == null)
            {
                failureReason = $"Pattern {patternType} has no output section list.";
                return false;
            }
            if (!TrackFeatureDefinitionCatalog.TryGet(patternType, out TrackFeatureDefinition definition))
            {
                failureReason = $"Pattern {patternType} has no Feature Definition asset.";
                return false;
            }
            return TryPlanDefinition(definition, cfg, patternId, ref rng, output,
                out failureReason);
        }

        public static bool TryPlanDefinition(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            string patternId,
            ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
            => TryPlanDefinition(definition, cfg, patternId, ref rng, output, out _);

        public static bool TryPlanDefinition(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            string patternId,
            ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output,
            out string failureReason)
        {
            failureReason = "";
            if (definition == null)
            {
                failureReason = "The selected Feature Definition is missing.";
                return false;
            }
            if (cfg == null)
            {
                failureReason = $"Definition '{definition.StableId}' has no resolved generation config.";
                return false;
            }
            if (output == null)
            {
                failureReason = $"Definition '{definition.StableId}' has no output section list.";
                return false;
            }
            if (!definition.Enabled)
            {
                failureReason = $"Definition '{definition.StableId}' is disabled.";
                return false;
            }
            if (!definition.Validate(out string validationError))
            {
                failureReason = validationError;
                return false;
            }

            int outputStart = output.Count;
            bool planned;
            switch (definition.Solver)
            {
                case FeatureDefinitionSolver.FullLoopV1:
                    planned = FullLoopPattern.TryPlanLegacy(definition, cfg, patternId, ref rng, output);
                    break;
                case FeatureDefinitionSolver.InlineCorkscrewV1:
                    planned = CorkscrewPattern.TryPlanLegacy(definition, cfg, patternId, ref rng, output);
                    break;
                case FeatureDefinitionSolver.SpiralV1:
                    planned = SpiralPattern.TryPlanLegacy(definition, cfg, patternId, ref rng, output);
                    break;
                case FeatureDefinitionSolver.VerticalBumpV1:
                    planned = TryPlanVerticalBump(definition, cfg, patternId, ref rng, output);
                    break;
                case FeatureDefinitionSolver.RotationalSequenceV1:
                    if (definition.TopologyRole != TopologyRole.HeadingNeutral)
                    {
                        failureReason = $"Definition '{definition.StableId}' is a turn realization and " +
                                        "cannot be emitted from a heading-neutral feature slot.";
                        planned = false;
                    }
                    else
                    {
                        planned = TryPlanRotationalSequence(definition, cfg, 0f, patternId,
                            ref rng, output, out failureReason);
                    }
                    break;
                default:
                    failureReason = $"Definition '{definition.StableId}' uses solver " +
                                    $"{definition.Solver}, which is not definition-native.";
                    planned = false;
                    break;
            }

            if (!planned)
            {
                if (output.Count > outputStart) output.RemoveRange(outputStart, output.Count - outputStart);
                if (string.IsNullOrWhiteSpace(failureReason))
                {
                    failureReason = $"Definition '{definition.StableId}' solver {definition.Solver} " +
                                    "could not produce legal geometry under the resolved config.";
                }
                return false;
            }

            StampCompiledSections(definition, cfg, output, outputStart);
            return true;
        }

        private static bool TryPlanRotationalSequence(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            float signedHeadingDegrees,
            string patternId,
            ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output,
            out string failureReason)
        {
            failureReason = "";
            RotationalFeatureDefinitionParameters authored = definition.Rotational;
            bool ownsVerticalCenterline = definition.Primitives.Any(primitive =>
                primitive.PrimitiveType == FeatureGeometryPrimitiveType.VerticalArc);
            // Vertical radii are authored against the shared half-loop safety floor and
            // cannot shrink below their reference-speed geometry. At lower design speeds
            // keep that physical footprint; at higher speeds scale the radii and length
            // together. This makes the full 400–2000 km/h slider legal without distorting
            // a Dive Loop/Sidewinder into a long roll attached to a tiny half-loop.
            float verticalGeometryScale = ownsVerticalCenterline && definition.Length.ScaleWithDesignSpeed
                ? Mathf.Max(1f, cfg.DesignSpeedMps / definition.Length.ReferenceSpeedMps)
                : 1f;
            float lengthMinimum = ownsVerticalCenterline
                ? definition.Length.Minimum * verticalGeometryScale
                : definition.Length.ResolvedMinimum(cfg.DesignSpeedMps);
            float lengthPreferred = ownsVerticalCenterline
                ? definition.Length.Preferred * verticalGeometryScale
                : definition.Length.ResolvedPreferred(cfg.DesignSpeedMps);
            float lengthMaximum = ownsVerticalCenterline
                ? definition.Length.Maximum * verticalGeometryScale
                : definition.Length.ResolvedMaximum(cfg.DesignSpeedMps);
            if (lengthMaximum <= 0f)
            {
                failureReason = $"Definition '{definition.StableId}' resolved to a non-positive maximum length.";
                return false;
            }
            var localRng = rng;

            float Pick(FeatureScalarParameter parameter, float randomReach = 0.25f)
            {
                float low = Mathf.Clamp(parameter.Preferred, parameter.Minimum, parameter.Maximum);
                return Mathf.Lerp(low, parameter.Maximum, localRng.NextFloat() * randomReach);
            }

            float radiusA = Pick(authored.FirstHalfRadius) * verticalGeometryScale;
            float radiusB = Pick(authored.SecondHalfRadius) * verticalGeometryScale;
            float targetHeading = definition.TopologyRole == TopologyRole.TurnRealization
                ? signedHeadingDegrees
                : 0f;
            float headingSign = Mathf.Abs(targetHeading) > 0.001f ? Mathf.Sign(targetHeading) : 1f;
            float rollSign = authored.AutomaticHandedness
                ? (Mathf.Abs(targetHeading) > 0.001f ? headingSign : (localRng.NextBool() ? 1f : -1f))
                : 1f;

            var phases = new List<RotationalPhaseDefinition>();
            var phaseFractions = new List<float>();
            float physicalMinimum = 0f;
            for (int i = 0; i < definition.Primitives.Count; i++)
            {
                FeatureGeometryPrimitive primitive = definition.Primitives[i];
                RotationalPhaseDefinition phase;
                if (primitive.PrimitiveType == FeatureGeometryPrimitiveType.RoadRoll)
                {
                    float rollDegrees = authored.RoadRollUnits * cfg.RotationUnitDegrees;
                    // Intentional inversion roll is not an ordinary-road connector
                    // transition. Size it from the feature's authored physical roll
                    // speed; entry/exit rates still return to the shared rulebook
                    // contract. Using the connector rate here made compact rolls
                    // mathematically require 6–14 km and reject every candidate.
                    float coreRollRateDegPerMeter =
                        authored.MaximumCoreRollRateDegreesPerSecond /
                        Mathf.Max(1f, cfg.DesignSpeedMps);
                    // The runtime road-roll profile is a normalized sin^2 rate
                    // envelope. Its peak is exactly 2x its average (not 1.5x).
                    // A small sampling margin keeps the first build legal instead of
                    // expanding past the definition cap during the retry loop.
                    const float smoothRollPeakFactor = 2.02f;
                    float phaseLength = Mathf.Max(
                        cfg.MinDistancePerRotationUnit * authored.RoadRollUnits,
                        rollDegrees * smoothRollPeakFactor /
                        Mathf.Max(0.001f, coreRollRateDegPerMeter));
                    float split = Mathf.Clamp(Pick(authored.PhaseSplit, 0.15f), 0.2f, 0.8f);
                    phase = new RotationalPhaseDefinition
                    {
                        Axis = RotationalPhaseAxis.RoadRoll,
                        Direction = rollSign >= 0f
                            ? RotationalPhaseDirection.Positive
                            : RotationalPhaseDirection.Negative,
                        RotationUnits = authored.RoadRollUnits,
                        FirstHalfLength = phaseLength * split,
                        SecondHalfLength = phaseLength * (1f - split),
                        FirstHalfRadius = radiusA,
                        SecondHalfRadius = radiusB,
                        CenterlineOrbitDegrees = Pick(authored.CenterlineOrbitDegrees),
                        FirstHalfPitchBiasDegrees = Pick(authored.FirstHalfPitchBiasDegrees, 0.15f),
                        SecondHalfPitchBiasDegrees = Pick(authored.SecondHalfPitchBiasDegrees, 0.15f),
                        ExitWidth = cfg.RoadWidth,
                        BlendToNext = cfg.DefaultRotationalBlend
                    };
                }
                else
                {
                    float halfDegrees = authored.VerticalRotationUnits * cfg.RotationUnitDegrees * 0.5f;
                    float easeScale = 1f / (1f - SectionFrameBuilders.LoopCurvatureEaseFraction);
                    float firstLength = Mathf.Max(
                        cfg.MinDistancePerRotationUnit * authored.VerticalRotationUnits * 0.5f,
                        halfDegrees * Mathf.Deg2Rad * radiusA * easeScale);
                    float secondLength = Mathf.Max(
                        cfg.MinDistancePerRotationUnit * authored.VerticalRotationUnits * 0.5f,
                        halfDegrees * Mathf.Deg2Rad * radiusB * easeScale);
                    phase = new RotationalPhaseDefinition
                    {
                        Axis = RotationalPhaseAxis.VerticalCenterline,
                        Direction = authored.DescendingVerticalPhase
                            ? RotationalPhaseDirection.Negative
                            : RotationalPhaseDirection.Positive,
                        RotationUnits = authored.VerticalRotationUnits,
                        FirstHalfLength = firstLength,
                        SecondHalfLength = secondLength,
                        FirstHalfRadius = radiusA,
                        SecondHalfRadius = radiusB,
                        FirstHalfYawBiasDegrees = rollSign * Pick(authored.LimbYawBiasDegrees, 0.15f),
                        SecondHalfYawBiasDegrees = rollSign * Pick(authored.LimbYawBiasDegrees, 0.15f),
                        ExitWidth = cfg.RoadWidth,
                        BlendToNext = cfg.DefaultRotationalBlend
                    };
                }
                physicalMinimum += phase.Length;
                phases.Add(phase);
                phaseFractions.Add(Mathf.Max(0.001f, primitive.LengthFraction));
            }

            if (physicalMinimum > lengthMaximum + 0.01f)
            {
                failureReason = $"Definition '{definition.StableId}' needs at least {physicalMinimum:F1}m " +
                                $"for its authored rotations, above its {lengthMaximum:F1}m maximum.";
                return false;
            }
            float targetLength = Mathf.Clamp(Mathf.Max(lengthPreferred, physicalMinimum),
                Mathf.Max(lengthMinimum, physicalMinimum), lengthMaximum);
            float extra = Mathf.Max(0f, targetLength - physicalMinimum);
            for (int i = 0; i < phases.Count; i++)
            {
                RotationalPhaseDefinition phase = phases[i];
                float added = extra * phaseFractions[i];
                float split = phase.FirstHalfLength / Mathf.Max(1f, phase.Length);
                phase.FirstHalfLength += added * split;
                phase.SecondHalfLength += added * (1f - split);
            }

            string debugStem = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.StableId
                : definition.DisplayName.Replace(" ", "");
            var core = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                Width = cfg.RoadWidth,
                Radius = radiusA,
                SecondaryRadius = radiusB,
                Direction = rollSign >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                RollChange = rollSign * authored.RoadRollUnits * cfg.RotationUnitDegrees,
                PitchChange = (authored.DescendingVerticalPhase ? -1f : 1f) *
                              authored.VerticalRotationUnits * cfg.RotationUnitDegrees,
                SpeedIntent = definition.SpeedIntent,
                RiskLevel = definition.RiskLevel,
                RequiresRecoveryAfter = definition.RequiresRecoveryAfter,
                LockLength = true,
                PatternId = patternId,
                DebugName = $"{debugStem}_R{radiusA:F0}-{radiusB:F0}",
                RotationalPhases = phases,
                Contract = new SectionConnectionContract
                {
                    RequiredEntryOrientation = definition.EntryContract.Orientation,
                    ExitOrientation = definition.ExitContract.Orientation,
                    HeadingDeltaDegrees = targetHeading,
                    PitchDeltaDegrees = definition.ExitContract.PitchChangeDegrees,
                    RollDeltaDegrees = definition.ExitContract.RollChangeDegrees * rollSign,
                    ElevationDelta = definition.ExitContract.ElevationChangeMeters,
                    ClosureCompatible = false
                },
                SemanticElement = definition.SemanticElement,
                FeatureFitRole = FeatureFitRole.Core
            };

            // A vertical half-rotation naturally reverses plan heading. Apply the
            // authored token as an independent phase turn, then measure and correct
            // against the real frame builder so left/right sidewinders are exact.
            RotationalPhaseDefinition headingPhase = phases.Find(phase =>
                phase.Axis == RotationalPhaseAxis.VerticalCenterline);
            if (headingPhase != null && Mathf.Abs(targetHeading) > 0.001f)
                headingPhase.HorizontalTurnDegrees = targetHeading - headingSign * 180f;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                SectionFrameBuilders.StampRotationalEventPlan(core, cfg);
                if (headingPhase == null) break;
                float headingError = Mathf.DeltaAngle(core.TurnAngle, targetHeading);
                if (Mathf.Abs(headingError) <= 0.1f) break;
                headingPhase.HorizontalTurnDegrees += headingError;
            }

            // The final correction above mutates the phase after the stamp measured in
            // that iteration.  Always stamp once more from the finished phase list.
            // Without this, the planner could close against the previous footprint while
            // the runtime builder used the corrected one (most visible on Sidewinder and
            // Dive Loop variants as kilometre-scale closure drift).
            SectionFrameBuilders.StampRotationalEventPlan(core, cfg);
            if (core.Length > lengthMaximum + 0.5f ||
                Mathf.Abs(Mathf.DeltaAngle(core.TurnAngle, targetHeading)) > 1f)
            {
                failureReason = core.Length > lengthMaximum + 0.5f
                    ? $"Definition '{definition.StableId}' built {core.Length:F1}m, above its {lengthMaximum:F1}m maximum."
                    : $"Definition '{definition.StableId}' exited at {core.TurnAngle:F2} degrees instead of " +
                      $"{targetHeading:F2} degrees.";
                return false;
            }

            TrackConnectionFrame[] frames = null;
            float peakRollRate = 0f;
            float maximumCoreRollRate = authored.MaximumCoreRollRateDegreesPerSecond /
                                        Mathf.Max(1f, cfg.DesignSpeedMps);
            // The normalized sin^2 phase envelope peaks at 2x the average for equal
            // radii, and the authored two-half radius variation can raise it further.
            // Measure the actual runtime frames and lengthen only the roll phase until
            // the physical feature is inside its own envelope. This keeps definitions
            // compact without relying on a fragile fixed safety multiplier.
            const int rollSizingAttempts = 3;
            for (int sizingAttempt = 0; sizingAttempt < rollSizingAttempts; sizingAttempt++)
            {
                frames = SectionFrameBuilders.BuildRotationalEvent(
                    TrackConnectionFrame.Origin(cfg.RoadWidth), core, FrameBuildContext.From(cfg));
                if (frames == null || frames.Length < 3 || frames.Any(frame => !IsFinite(frame)))
                    break;

                peakRollRate = frames.Max(frame => Mathf.Abs(frame.RoadRollRate));
                if (peakRollRate <= maximumCoreRollRate * 1.01f) break;

                float expansion = peakRollRate / Mathf.Max(0.001f, maximumCoreRollRate) * 1.01f;
                foreach (RotationalPhaseDefinition phase in phases)
                {
                    if (phase.Axis != RotationalPhaseAxis.RoadRoll) continue;
                    phase.FirstHalfLength *= expansion;
                    phase.SecondHalfLength *= expansion;
                }
                SectionFrameBuilders.StampRotationalEventPlan(core, cfg);
                if (core.Length > lengthMaximum + 0.5f) break;
            }

            // The last sizing iteration may have expanded the phase. Measure that
            // final geometry rather than retaining the frames from before expansion.
            if (core.Length <= lengthMaximum + 0.5f)
            {
                frames = SectionFrameBuilders.BuildRotationalEvent(
                    TrackConnectionFrame.Origin(cfg.RoadWidth), core, FrameBuildContext.From(cfg));
                if (frames != null && frames.Length >= 3 && frames.All(frame => IsFinite(frame)))
                    peakRollRate = frames.Max(frame => Mathf.Abs(frame.RoadRollRate));
            }

            if (frames == null || frames.Length < 3 || frames.Any(frame => !IsFinite(frame)))
            {
                failureReason = $"Definition '{definition.StableId}' produced invalid rotational frames.";
                return false;
            }
            if (core.Length > lengthMaximum + 0.5f)
            {
                failureReason = $"Definition '{definition.StableId}' needs {core.Length:F1}m to keep its core roll legal, " +
                                $"above its {lengthMaximum:F1}m maximum.";
                return false;
            }
            if (peakRollRate > maximumCoreRollRate * 1.02f)
            {
                failureReason = $"Definition '{definition.StableId}' core roll rate is {peakRollRate:F4} deg/m, " +
                                $"above its {maximumCoreRollRate:F4} deg/m envelope.";
                return false;
            }

            output.Add(core);
            if (definition.EmitsOwnRecovery)
            {
                float minimumRecovery = definition.RecoveryLength.ResolvedMinimum(cfg.DesignSpeedMps) *
                                        cfg.FeatureFitScale;
                float maximumRecovery = definition.RecoveryLength.ResolvedMaximum(cfg.DesignSpeedMps) *
                                        cfg.FeatureFitScale;
                float recoveryLength = Mathf.Clamp(cfg.DefaultRecoveryLength,
                    minimumRecovery, maximumRecovery);
                output.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight,
                    recoveryLength, cfg.RoadWidth, $"{debugStem}Recovery", locked: true,
                    patternId, FeatureFitRole.ExitRecovery));
            }
            rng = localRng;
            return true;
        }

        private static bool TryPlanVerticalBump(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            string patternId,
            ref Unity.Mathematics.Random rng,
            List<TrackMacroSectionDefinition> output)
        {
            float lengthMinimum = definition.Length.ResolvedMinimum(cfg.DesignSpeedMps);
            float lengthMaximum = definition.Length.ResolvedMaximum(cfg.DesignSpeedMps);
            float preferredLength = definition.Length.ResolvedPreferred(cfg.DesignSpeedMps);
            float length = Mathf.Clamp(
                Mathf.Lerp(preferredLength, lengthMaximum, rng.NextFloat() * 0.35f),
                lengthMinimum, lengthMaximum);

            float minimumHeight = definition.VerticalBump.HeightMeters.Minimum;
            float maximumHeight = definition.VerticalBump.HeightMeters.Maximum;
            float requestedHeight = Mathf.Lerp(
                definition.VerticalBump.HeightMeters.Preferred,
                maximumHeight,
                rng.NextFloat() * 0.35f);

            string debugStem = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.StableId
                : definition.DisplayName.Replace(" ", "");
            var core = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                Length = length,
                Width = cfg.RoadWidth,
                HillHeight = requestedHeight,
                SpeedIntent = definition.SpeedIntent,
                RiskLevel = definition.RiskLevel,
                RequiresRecoveryAfter = definition.RequiresRecoveryAfter,
                // Definition-native elevation encounters own their complete footprint.
                // Leaving this false lets the generic closure solver compress a
                // Camelback into an ordinary-road ripple after its geometry preflight.
                LockLength = true,
                PatternId = patternId,
                DebugName = $"{debugStem}_{requestedHeight:F0}m",
                Contract = SectionConnectionContract.Level(0f),
                SemanticElement = definition.SemanticElement,
                FeatureFitRole = FeatureFitRole.Core,
                FeatureVerticalLobeCount = definition.VerticalBump.LobeCount,
                FeatureVerticalStartsWithDip = definition.VerticalBump.StartsWithDip,
                FeatureVerticalAlternates = definition.VerticalBump.AlternateLobeSign
            };

            // Solve down from the authored request without ever weakening the shared
            // track rulebook. A failed minimum is a rejected candidate, not a flatter
            // feature silently accepted under different limits.
            const int lengthAttempts = 7;
            const int heightAttempts = 12;
            for (int lengthAttempt = 0; lengthAttempt < lengthAttempts; lengthAttempt++)
            {
                float lengthT = (float)lengthAttempt / (lengthAttempts - 1);
                core.Length = Mathf.Lerp(length, lengthMaximum, lengthT);
                for (int heightAttempt = 0; heightAttempt < heightAttempts; heightAttempt++)
                {
                    float heightT = (float)heightAttempt / (heightAttempts - 1);
                    core.HillHeight = Mathf.Lerp(requestedHeight, minimumHeight, heightT);
                    core.DebugName = $"{debugStem}_{core.HillHeight:F0}m_{core.Length:F0}m";
                    if (!VerticalBumpFramesMeetEnvelope(core, cfg)) continue;
                    output.Add(core);
                    return true;
                }
            }

            return false;
        }

        private static bool VerticalBumpFramesMeetEnvelope(
            TrackMacroSectionDefinition core,
            ResolvedTrackGenerationConfig cfg)
        {
            TrackConnectionFrame[] frames = SectionFrameBuilders.BuildSectionFrames(
                TrackConnectionFrame.Origin(cfg.RoadWidth), core, FrameBuildContext.From(cfg));
            if (frames == null || frames.Length < 3) return false;

            float maxCurvature = cfg.MaxCurvatureInducedG * Mathf.Max(0.1f, cfg.Gravity) /
                                 Mathf.Max(1f, cfg.DesignSpeedMps * cfg.DesignSpeedMps);
            const float headroom = 0.92f;
            for (int i = 0; i < frames.Length; i++)
            {
                float pitch = frames[i].PitchAngle;
                if (pitch > cfg.MaxClimbAngle * headroom ||
                    -pitch > cfg.MaxDropAngle * headroom ||
                    Mathf.Abs(frames[i].VerticalCurvature) > maxCurvature * headroom ||
                    Mathf.Abs(frames[i].VerticalCurvatureRate) > cfg.MaxVerticalCurvatureRate * headroom)
                    return false;
            }

            TrackConnectionFrame first = frames[0];
            TrackConnectionFrame last = frames[frames.Length - 1];
            return Vector3.Distance(first.Position + first.Forward * core.Length, last.Position) <= 0.01f &&
                   Vector3.Angle(first.Forward, last.Forward) <= 0.01f &&
                   Mathf.Abs(last.PitchAngle - first.PitchAngle) <= 0.01f;
        }

        public static bool TryPlanTurn(
            SemanticElementId semanticElement,
            ResolvedTrackGenerationConfig cfg,
            float signedHeadingDegrees,
            string patternId,
            List<TrackMacroSectionDefinition> output)
            => TryPlanTurn(semanticElement, cfg, signedHeadingDegrees, patternId,
                output, out _);

        public static bool TryPlanTurn(
            SemanticElementId semanticElement,
            ResolvedTrackGenerationConfig cfg,
            float signedHeadingDegrees,
            string patternId,
            List<TrackMacroSectionDefinition> output,
            out string failureReason)
        {
            if (!TrackFeatureDefinitionCatalog.TryGet(
                    semanticElement, out TrackFeatureDefinition definition))
            {
                failureReason = $"No enabled Feature Definition is registered for {semanticElement}.";
                return false;
            }
            return TryPlanTurnDefinition(
                definition, cfg, signedHeadingDegrees, patternId, output, out failureReason);
        }

        public static bool TryPlanTurnDefinition(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            float signedHeadingDegrees,
            string patternId,
            List<TrackMacroSectionDefinition> output)
            => TryPlanTurnDefinition(definition, cfg, signedHeadingDegrees, patternId,
                output, out _);

        public static bool TryPlanTurnDefinition(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            float signedHeadingDegrees,
            string patternId,
            List<TrackMacroSectionDefinition> output,
            out string failureReason)
        {
            if (definition == null)
            {
                failureReason = "The requested turn has no Feature Definition.";
                return false;
            }
            if (cfg == null)
            {
                failureReason = $"Definition '{definition.StableId}' has no resolved generation configuration.";
                return false;
            }
            if (output == null)
            {
                failureReason = $"Definition '{definition.StableId}' has no output section list.";
                return false;
            }
            if (!definition.Enabled)
            {
                failureReason = $"Definition '{definition.StableId}' is disabled.";
                return false;
            }
            if (!definition.Validate(out string validationError))
            {
                failureReason = validationError;
                return false;
            }
            if (definition.Solver != FeatureDefinitionSolver.EasedTurnV1 &&
                definition.Solver != FeatureDefinitionSolver.RotationalSequenceV1)
            {
                failureReason = $"Definition '{definition.StableId}' uses solver " +
                                $"{definition.Solver}, not a supported turn-definition solver.";
                return false;
            }

            float magnitude = Mathf.Abs(signedHeadingDegrees);
            if (magnitude < definition.Turn.HeadingDegrees.Minimum - 0.001f ||
                magnitude > definition.Turn.HeadingDegrees.Maximum + 0.001f)
            {
                failureReason = $"Definition '{definition.StableId}' accepts " +
                                $"{definition.Turn.HeadingDegrees.Minimum:F1}–" +
                                $"{definition.Turn.HeadingDegrees.Maximum:F1} degrees, " +
                                $"but the topology requested {magnitude:F1} degrees.";
                return false;
            }

            int outputStart = output.Count;
            if (definition.Solver == FeatureDefinitionSolver.RotationalSequenceV1)
            {
                uint seed = StableDefinitionSeed($"{definition.StableId}:{patternId}:{signedHeadingDegrees:F3}");
                var turnRng = new Unity.Mathematics.Random(seed);
                bool rotationalPlanned = TryPlanRotationalSequence(definition, cfg,
                    signedHeadingDegrees, patternId, ref turnRng, output, out string rotationalFailure);
                if (!rotationalPlanned)
                {
                    if (output.Count > outputStart)
                        output.RemoveRange(outputStart, output.Count - outputStart);
                    failureReason = string.IsNullOrWhiteSpace(rotationalFailure)
                        ? $"Definition '{definition.StableId}' could not satisfy its rotational sizing and exit contract."
                        : rotationalFailure;
                    return false;
                }
                StampCompiledSections(definition, cfg, output, outputStart);
                failureReason = "";
                return true;
            }

            float baseRadius = Mathf.Max(
                Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, magnitude / 180f),
                cfg.MinCurveRadius);
            float radiusMultiplier = definition.Turn.RadiusMultiplier.Preferred;
            float radius = Mathf.Max(baseRadius * radiusMultiplier,
                cfg.MinCurveRadius * radiusMultiplier);
            if (definition.Turn.AbsoluteRadiusMeters.Preferred > 0.001f)
                radius = Mathf.Max(radius, definition.Turn.AbsoluteRadiusMeters.Preferred);
            if (definition.Turn.CoreSectionType == TrackMacroSectionType.BankedCurve)
                radius = SectionFrameBuilders.ClampOrdinaryCurveRadius(magnitude, radius);

            FeatureGeometryPrimitiveType turnPrimitive = definition.Primitives[0].PrimitiveType;
            bool elevated = turnPrimitive == FeatureGeometryPrimitiveType.ElevatedEasedTurn;
            bool halfHelix = turnPrimitive == FeatureGeometryPrimitiveType.HalfHelixTurn;
            float crestHeight = elevated
                ? definition.Turn.CrestHeightMeters.Preferred
                : 0f;
            float helixClimb = halfHelix
                ? definition.HalfHelix.ElevationChangeMeters.Preferred
                : 0f;
            float bankMultiplier = definition.Turn.BankMultiplier.Preferred > 0.001f
                ? definition.Turn.BankMultiplier.Preferred
                : 1f;
            float recommendedBank = definition.Turn.UseRecommendedBank
                ? SectionDefs.RecommendedBank(cfg, radius)
                : 0f;

            SectionTurnDirection direction = signedHeadingDegrees >= 0f
                ? SectionTurnDirection.Right
                : SectionTurnDirection.Left;
            string debugStem = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.StableId
                : definition.DisplayName.Replace(" ", "");
            var core = new TrackMacroSectionDefinition
            {
                SectionType = definition.Turn.CoreSectionType,
                Length = halfHelix
                    ? SectionFrameBuilders.EstimatePartialHelixLength(
                        radius, magnitude, 0f, helixClimb)
                    : elevated
                        ? SectionFrameBuilders.ElevatedEasedArcLength(magnitude, radius, crestHeight)
                        : SectionFrameBuilders.EasedArcLength(magnitude, radius),
                Width = cfg.RoadWidth,
                Direction = direction,
                TurnAngle = magnitude,
                Radius = radius,
                BankingAngle = Mathf.Min(recommendedBank * bankMultiplier, cfg.MaxBankAngle),
                HillHeight = crestHeight,
                ElevationChange = helixClimb,
                SpeedIntent = definition.SpeedIntent,
                RiskLevel = definition.RiskLevel,
                RequiresRecoveryAfter = definition.RequiresRecoveryAfter,
                PatternId = patternId,
                DebugName = $"{debugStem}_{magnitude:F0}deg_{direction}",
                Contract = halfHelix
                    ? new SectionConnectionContract
                    {
                        RequiredEntryOrientation = definition.EntryContract.Orientation,
                        ExitOrientation = definition.ExitContract.Orientation,
                        HeadingDeltaDegrees = signedHeadingDegrees,
                        ElevationDelta = helixClimb,
                        ClosureCompatible = false
                    }
                    : SectionConnectionContract.Level(signedHeadingDegrees)
            };
            output.Add(core);

            if (definition.EmitsOwnRecovery)
            {
                // Definition assets keep their authored/full-size contract (and stable
                // content hash). The project-wide fitting policy resolves that contract
                // into a compact initial road without changing the asset itself.
                float minimumRecovery = definition.RecoveryLength.ResolvedMinimum(cfg.DesignSpeedMps) *
                                        cfg.FeatureFitScale;
                float maximumRecovery = definition.RecoveryLength.ResolvedMaximum(cfg.DesignSpeedMps) *
                                        cfg.FeatureFitScale;
                float recoveryLength = Mathf.Clamp(
                    cfg.DefaultRecoveryLength, minimumRecovery, maximumRecovery);
                output.Add(SectionDefs.Straight(
                    TrackMacroSectionType.RecoveryStraight,
                    recoveryLength,
                    cfg.RoadWidth,
                    $"{debugStem}Recovery",
                    locked: true,
                    patternId,
                    FeatureFitRole.ExitRecovery));
            }

            StampCompiledSections(definition, cfg, output, outputStart);
            failureReason = "";
            return true;
        }

        private static void StampCompiledSections(
            TrackFeatureDefinition definition,
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> output,
            int startIndex)
        {
            string hash = definition.ComputeContentHash();
            string primitiveSequence = string.Join(">", definition.Primitives.Select(p => p.PrimitiveId));
            float entryBudget = definition.EntryLength.ResolvedPreferred(cfg.DesignSpeedMps) *
                                cfg.FeatureFitScale;
            float recoveryBudget = definition.RecoveryLength.ResolvedPreferred(cfg.DesignSpeedMps) *
                                   cfg.FeatureFitScale;

            for (int i = startIndex; i < output.Count; i++)
            {
                TrackMacroSectionDefinition section = output[i];
                if (section == null) continue;
                section.SemanticElement = section.SectionType == TrackMacroSectionType.RecoveryStraight
                    ? SemanticElementId.RecoverySection
                    : definition.SemanticElement;
                section.FeatureDefinitionId = definition.StableId;
                section.FeatureDefinitionVersion = definition.DefinitionVersion;
                section.FeatureDefinitionContentHash = hash;
                section.FeaturePrimitiveSequence = primitiveSequence;
                section.FeatureDefinitionEntryBudget = entryBudget;
                section.FeatureDefinitionRecoveryBudget = recoveryBudget;
            }
        }
    }
}
