using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TrackGeneration.Core;
using TrackGeneration.Definitions;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using UnityEngine;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Definition-focused half of the single V2 replay/connector gate fixture.
    /// Kept in a second source file for readability; Unity Test Runner presents and
    /// runs every case under V2ReplayAndConnectorGateTests.
    /// </summary>
    public sealed partial class V2ReplayAndConnectorGateTests
    {
        [Test]
        public void FeatureFitting_DefaultContractsResolveAtHalfLegacyDistance()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
                settings.Scale.DesignSpeedKph = 1300f;
                settings.Transitions.FeatureFitScale = 0.5f;
                settings.Transitions.DefaultApproachSeconds = 2f;
                settings.Transitions.DefaultRecoverySeconds = 1.5f;

                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                float speed = 1300f / 3.6f;

                Assert.AreEqual(0.5f, resolved.FeatureFitScale, 0.0001f);
                Assert.AreEqual(speed, resolved.DefaultApproachLength, 0.05f);
                Assert.AreEqual(speed * 0.75f, resolved.DefaultRecoveryLength, 0.05f);
                Assert.AreEqual(speed, resolved.JumpApproachLength, 0.05f);
                Assert.AreEqual(speed, resolved.SpiralApproachLength, 0.05f);
                Assert.AreEqual(speed, resolved.CorkscrewApproachLength, 0.05f);
                Assert.LessOrEqual(resolved.HalfLoopRolloutLength, speed * 1.1f + 0.05f,
                    "The authored half-loop rollout starts at half its legacy 2.2-second budget; " +
                    "the pattern may still expand it to satisfy the hard roll-rate envelope.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void FeatureFitting_EntryWarmupRemainsIndependentOfStructuralCorridor()
        {
            var lead = SectionDefs.Straight(TrackMacroSectionType.Straight, 180f, 48f,
                "Straight_00", fitRole: FeatureFitRole.StructuralConnector);
            var entry = SectionDefs.Straight(TrackMacroSectionType.BoostStraight, 360f, 48f,
                "JumpApproach", locked: true, patternId: "jump-0",
                fitRole: FeatureFitRole.EntryWarmup);
            entry.AllowsBoost = true;
            entry.SpeedIntent = SectionSpeedIntent.FullThrottle;
            var core = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.JumpRamp,
                Length = 200f,
                Width = 48f,
                PatternId = "jump-0",
                FeatureFitRole = FeatureFitRole.Core
            };
            var defs = new List<TrackMacroSectionDefinition> { lead, entry, core };

            Assert.AreEqual(3, defs.Count);
            Assert.AreSame(lead, defs[0]);
            Assert.AreSame(entry, defs[1]);
            Assert.AreSame(core, defs[2]);
            Assert.AreEqual(180f, lead.Length, 0.001f);
            Assert.AreEqual(FeatureFitRole.StructuralConnector, lead.FeatureFitRole);
            Assert.AreEqual(360f, entry.Length, 0.001f);
            Assert.AreEqual(FeatureFitRole.EntryWarmup, entry.FeatureFitRole);
            Assert.AreEqual("jump-0", entry.PatternId,
                "The compact warmup remains owned by its feature for exact editor replacement.");
        }

        [Test]
        public void FeatureFitting_DoesNotScaleStructuralClosureCorridors()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings compact =
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
                compact.Transitions.FeatureFitScale = 0.5f;
                TrackDesignerSettings legacy = compact.Clone();
                legacy.Transitions.FeatureFitScale = 1f;

                ResolvedTrackGenerationConfig compactResolved =
                    ResolvedTrackGenerationConfig.Resolve(config, compact);
                ResolvedTrackGenerationConfig legacyResolved =
                    ResolvedTrackGenerationConfig.Resolve(config, legacy);

                float compactLead = TrackTopologyPlanner.ResolveStructuralLeadLength(
                    compactResolved, 0.65f);
                float legacyLead = TrackTopologyPlanner.ResolveStructuralLeadLength(
                    legacyResolved, 0.65f);

                Assert.AreEqual(legacyLead, compactLead, 0.001f,
                    "Feature fitting may compact authored warmups, never route pitch or closure authority.");
                Assert.Greater(compactLead, compactResolved.DefaultApproachLength * 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void FeatureFitting_RecoveryAndNextLeadRemainIndependentSolverControls()
        {
            var recovery = SectionDefs.Straight(
                TrackMacroSectionType.RecoveryStraight, 270f, 48f,
                "RecoveryStraight", locked: true, patternId: "loop-0",
                fitRole: FeatureFitRole.ExitRecovery);
            var lead = SectionDefs.Straight(
                TrackMacroSectionType.Straight, 500f, 48f,
                "Straight_01", fitRole: FeatureFitRole.StructuralConnector);
            var defs = new List<TrackMacroSectionDefinition> { recovery, lead };

            Assert.AreEqual(2, defs.Count);
            Assert.AreEqual(270f, recovery.Length, 0.001f);
            Assert.IsTrue(recovery.LockLength);
            Assert.AreEqual("loop-0", recovery.PatternId);
            Assert.AreEqual(500f, lead.Length, 0.001f);
            Assert.IsFalse(lead.LockLength);
            Assert.AreEqual(FeatureFitRole.StructuralConnector, lead.FeatureFitRole);
        }

        [Test]
        public void ElevationClosure_UsesUnusedStructuralCorridorsWithoutTouchingFeatureRoads()
        {
            var cfg = new ResolvedTrackGenerationConfig
            {
                DesignSpeedMps = 361f,
                Gravity = 9.81f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                MaxCurvatureInducedG = 15f,
                MaxVerticalCurvatureRate = 0.00002f,
                DefaultRecoveryLength = 200f
            };
            var featureEntry = SectionDefs.Straight(TrackMacroSectionType.Straight,
                250f, 92f, "LoopEntry", locked: true, patternId: "Loop_Test",
                fitRole: FeatureFitRole.EntryWarmup);
            var structural = SectionDefs.Straight(TrackMacroSectionType.Straight,
                1200f, 92f, "Straight_00", fitRole: FeatureFitRole.StructuralConnector);
            var definitions = new List<TrackMacroSectionDefinition>
            {
                featureEntry,
                structural
            };

            float residual = TrackTopologyPlanner.ApplyProceduralElevationRecovery(
                cfg, definitions, new HashSet<int>(), 80f);

            Assert.AreEqual(0f, residual, 0.25f);
            Assert.AreEqual(0f, featureEntry.ElevationChange, 0.001f,
                "Feature warmup geometry must remain authored and level.");
            Assert.Less(structural.ElevationChange, 0f);
        }

        [Test]
        public void Catalog_ImplementedFeatures_AreValidVersionedDefinitions()
        {
            Assert.IsEmpty(TrackFeatureDefinitionCatalog.LoadErrors,
                string.Join("\n", TrackFeatureDefinitionCatalog.LoadErrors));

            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.FullLoop, out TrackFeatureDefinition loop));
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.Corkscrew, out TrackFeatureDefinition corkscrew));
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.Spiral, out TrackFeatureDefinition spiral));
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                SemanticElementId.WideTurnaround, out TrackFeatureDefinition wideTurnaround));
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                SemanticElementId.Horseshoe, out TrackFeatureDefinition horseshoe));
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.WideTurnaround, out TrackFeatureDefinition proceduralWide));
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.Horseshoe, out TrackFeatureDefinition proceduralHorseshoe));

            AssertDefinitionContract(loop);
            AssertDefinitionContract(corkscrew);
            AssertDefinitionContract(spiral);
            AssertDefinitionContract(wideTurnaround);
            AssertDefinitionContract(horseshoe);
            Assert.AreEqual("slingshot.vertical-loop", loop.StableId);
            Assert.AreEqual("slingshot.inline-corkscrew", corkscrew.StableId);
            Assert.AreEqual("slingshot.spiral", spiral.StableId);
            Assert.AreEqual("slingshot.wide-turnaround", wideTurnaround.StableId);
            Assert.AreEqual("slingshot.horseshoe", horseshoe.StableId);
            Assert.AreSame(wideTurnaround, proceduralWide,
                "Wide Turnaround must resolve through the procedural pattern catalog, not only Track Editor semantics.");
            Assert.AreSame(horseshoe, proceduralHorseshoe,
                "Horseshoe must resolve through the procedural pattern catalog, not only Track Editor semantics.");
            Assert.AreEqual(SemanticElementId.WideTurnaround,
                FeaturePlanning.ElementOf(TrackPatternType.WideTurnaround));
            Assert.AreEqual(SemanticElementId.Horseshoe,
                FeaturePlanning.ElementOf(TrackPatternType.Horseshoe));
        }

        [Test]
        public void Spiral_DefinitionAndGeneratorStayWithinTwoStoreys()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.Spiral, out TrackFeatureDefinition definition));
            CollectionAssert.AreEqual(new[] { 1f, 1.5f, 2f },
                definition.Spiral.AllowedStoryCounts);
            Assert.AreEqual(FeatureDefinitionSolver.SpiralV1, definition.Solver);

            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                // Simulate an older serialized rulebook that still contains the former
                // 3–6 revolution range. Resolve must remain safe even before OnValidate.
                typeof(TrackConfig).GetField("maxSpiralRevolutions",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(config, 6);
                TrackDesignerSettings settings =
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                Assert.AreEqual(2, resolved.MaxSpiralRevolutions,
                    "The generator rulebook must reject legacy 3–4-storey spiral limits.");

                bool sawOne = false;
                bool sawOneAndHalf = false;
                bool sawTwo = false;
                for (uint seed = 1; seed <= 96; seed++)
                {
                    var rng = new Unity.Mathematics.Random(seed);
                    var output = new List<TrackMacroSectionDefinition>();
                    Assert.IsTrue(TrackFeatureDefinitionCompiler.TryPlanPattern(
                        TrackPatternType.Spiral, resolved, $"Spiral_{seed}", ref rng, output));
                    TrackMacroSectionDefinition core = output.Single(section =>
                        section.SectionType == TrackMacroSectionType.Spiral);
                    Assert.LessOrEqual(core.TurnAngle, 720f + 0.001f);
                    Assert.IsTrue(Mathf.Approximately(core.TurnAngle, 360f) ||
                                  Mathf.Approximately(core.TurnAngle, 720f),
                        "Heading-neutral spirals must finish complete coils at a clean weld.");
                    sawOne |= core.DebugName.Contains("_1story_");
                    sawOneAndHalf |= core.DebugName.Contains("_1.5story_");
                    sawTwo |= core.DebugName.Contains("_2story_");
                }

                Assert.IsTrue(sawOne && sawOneAndHalf && sawTwo,
                    "The authored 1, 1.5 and 2-storey variants must all be reachable procedurally.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Loop_ComposesExactlyTwoAtomicHalfLoopPrimitives()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.FullLoop, out TrackFeatureDefinition definition));

            Assert.AreEqual(2, definition.Primitives.Count);
            Assert.IsTrue(definition.Primitives.All(
                primitive => primitive.PrimitiveType == FeatureGeometryPrimitiveType.HalfLoop));
            Assert.AreEqual(TrackOrientationTag.Upright, definition.Primitives[0].EntryOrientation);
            Assert.AreEqual(TrackOrientationTag.Inverted, definition.Primitives[0].ExitOrientation);
            Assert.AreEqual(TrackOrientationTag.Inverted, definition.Primitives[1].EntryOrientation);
            Assert.AreEqual(TrackOrientationTag.Upright, definition.Primitives[1].ExitOrientation);
            Assert.IsFalse(definition.Primitives[1].AllowGenericConnectorBefore);
            Assert.AreEqual(1f, definition.Primitives.Sum(primitive => primitive.LengthFraction), 0.0001f);
        }

        [Test]
        public void Corkscrew_ComposesExactlyTwoAtomicHalfCorkscrewPrimitives()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.Corkscrew, out TrackFeatureDefinition definition));

            Assert.AreEqual(2, definition.Primitives.Count);
            Assert.IsTrue(definition.Primitives.All(
                primitive => primitive.PrimitiveType == FeatureGeometryPrimitiveType.HalfCorkscrew));
            Assert.AreEqual(FeatureLengthResolution.PhysicsAndRollRateSolved,
                definition.Length.Resolution);
            Assert.AreEqual(2.5f, definition.Corkscrew.BarrelClearanceMultiplier, 0.0001f);
            Assert.AreEqual(0.55f, definition.Corkscrew.TargetArrivalSpeedFraction, 0.0001f);
            Assert.IsFalse(definition.Primitives[1].AllowGenericConnectorBefore);
        }

        [Test]
        public void WideTurnaround_IsOneDefinitionNativeEasedTurnWithOwnedRecovery()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                SemanticElementId.WideTurnaround, out TrackFeatureDefinition definition));

            Assert.AreEqual(FeatureDefinitionSolver.EasedTurnV1, definition.Solver);
            Assert.AreEqual(TopologyRole.TurnRealization, definition.TopologyRole);
            Assert.AreEqual(TrackMacroSectionType.BankedHairpin, definition.Turn.CoreSectionType);
            Assert.AreEqual(1, definition.Primitives.Count);
            Assert.AreEqual(FeatureGeometryPrimitiveType.EasedTurn,
                definition.Primitives[0].PrimitiveType);
            Assert.AreEqual(1.35f, definition.Turn.RadiusMultiplier.Preferred, 0.0001f);
            Assert.IsTrue(definition.RequiresRecoveryAfter);
            Assert.IsTrue(definition.EmitsOwnRecovery);
            Assert.Greater(definition.RecoveryLength.Maximum, definition.RecoveryLength.Minimum);

            FeatureCapability capability = FeatureCapabilities.Get(SemanticElementId.WideTurnaround);
            Assert.NotNull(capability);
            Assert.AreEqual(definition.TopologyRole, capability.Role);
            Assert.AreEqual(definition.EntryContract.MaximumBankDegrees,
                capability.MaxEntryBankDeg, 0.0001f);
            Assert.AreEqual(definition.EntryContract.MaximumHorizontalCurvature,
                capability.MaxEntryHorizontalCurvature, 0.0000001f);
            Assert.AreEqual(definition.RequiresRecoveryAfter, capability.RequiresRecoveryAfter);
            Assert.AreEqual(definition.EmitsOwnRecovery, capability.EmitsOwnRecovery);
        }

        [Test]
        public void Horseshoe_IsDefinitionNativeElevatedEasedTurn()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                SemanticElementId.Horseshoe, out TrackFeatureDefinition definition));

            Assert.AreEqual(FeatureDefinitionSolver.EasedTurnV1, definition.Solver);
            Assert.AreEqual(TopologyRole.TurnRealization, definition.TopologyRole);
            Assert.AreEqual(1, definition.Primitives.Count);
            Assert.AreEqual(FeatureGeometryPrimitiveType.ElevatedEasedTurn,
                definition.Primitives[0].PrimitiveType);
            Assert.Greater(definition.Turn.CrestHeightMeters.Preferred, 0f);
            Assert.Greater(definition.Turn.BankMultiplier.Preferred, 1f);
            Assert.IsTrue(definition.RequiresRecoveryAfter);
            Assert.IsTrue(definition.EmitsOwnRecovery);

            FeatureCapability capability = FeatureCapabilities.Get(SemanticElementId.Horseshoe);
            Assert.NotNull(capability);
            Assert.AreEqual(definition.TopologyRole, capability.Role);
            Assert.AreEqual(definition.EntryContract.MaximumPitchDegrees,
                capability.MaxEntryPitchDeg, 0.0001f);
        }

        [Test]
        public void ElevatedTurnCrestProfile_IsC2AtOwnedBoundaries()
        {
            const float h = 0.001f;
            Assert.AreEqual(0f, SectionFrameBuilders.CrestBump(0f), 0.0000001f);
            Assert.AreEqual(1f, SectionFrameBuilders.CrestBump(0.5f), 0.000001f);
            Assert.AreEqual(0f, SectionFrameBuilders.CrestBump(1f), 0.0000001f);
            Assert.AreEqual(0f, SectionFrameBuilders.CrestBumpDerivative(0f), 0.0000001f);
            Assert.AreEqual(0f, SectionFrameBuilders.CrestBumpDerivative(1f), 0.0000001f);

            float entrySecondDerivative =
                (SectionFrameBuilders.CrestBump(2f * h) -
                 2f * SectionFrameBuilders.CrestBump(h) +
                 SectionFrameBuilders.CrestBump(0f)) / (h * h);
            float exitSecondDerivative =
                (SectionFrameBuilders.CrestBump(1f) -
                 2f * SectionFrameBuilders.CrestBump(1f - h) +
                 SectionFrameBuilders.CrestBump(1f - 2f * h)) / (h * h);
            Assert.AreEqual(0f, entrySecondDerivative, 0.25f);
            Assert.AreEqual(0f, exitSecondDerivative, 0.25f);
        }

        [Test]
        public void ElevatedTurnLength_UsesSameDeterministicCrestBudgetAsBuilder()
        {
            const float angle = 180f;
            const float radius = 900f;
            const float crest = 80f;
            float horizontal = SectionFrameBuilders.EasedArcLength(angle, radius);
            float elevated = SectionFrameBuilders.ElevatedEasedArcLength(angle, radius, crest);

            Assert.Greater(elevated, horizontal);
            Assert.AreEqual(horizontal,
                SectionFrameBuilders.ElevatedEasedArcLength(angle, radius, 0f), 0.0001f);
            Assert.AreEqual(elevated,
                SectionFrameBuilders.ElevatedEasedArcLength(angle, radius, -crest), 0.0001f,
                "Mirroring the crest vertically must not change occupied length.");
        }

        [Test]
        public void TurnDefinitionCompiler_OutOfWindowExplainsFailureWithoutMutation()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                    SemanticElementId.Horseshoe, out TrackFeatureDefinition definition));

                var sentinel = new TrackMacroSectionDefinition { DebugName = "KeepMe" };
                var output = new List<TrackMacroSectionDefinition> { sentinel };
                Assert.IsFalse(TrackFeatureDefinitionCompiler.TryPlanTurnDefinition(
                    definition, resolved, 90f, "OutOfWindow", output, out string failure));
                StringAssert.Contains("150.0", failure);
                StringAssert.Contains("180.0", failure);
                StringAssert.Contains("90.0", failure);
                Assert.AreEqual(1, output.Count);
                Assert.AreSame(sentinel, output[0]);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [TestCase(180f)]
        [TestCase(-180f)]
        public void HorseshoeCompiler_ProducesElevatedExactExitWithDefinitionIdentity(
            float signedHeading)
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                    SemanticElementId.Horseshoe, out TrackFeatureDefinition definition));

                var output = new List<TrackMacroSectionDefinition>();
                Assert.IsTrue(TrackFeatureDefinitionCompiler.TryPlanTurnDefinition(
                    definition, resolved, signedHeading, "HorseshoeDefinitionTest", output));
                Assert.AreEqual(2, output.Count);

                TrackMacroSectionDefinition turn = output[0];
                Assert.AreEqual(definition.Turn.CrestHeightMeters.Preferred,
                    turn.HillHeight, 0.001f);
                Assert.Greater(turn.Length,
                    SectionFrameBuilders.EasedArcLength(Mathf.Abs(signedHeading), turn.Radius));
                Assert.Greater(turn.BankingAngle,
                    SectionDefs.RecommendedBank(resolved, turn.Radius));
                Assert.LessOrEqual(turn.BankingAngle, resolved.MaxBankAngle + 0.001f);
                Assert.AreEqual("slingshot.horseshoe", turn.FeatureDefinitionId);
                Assert.AreEqual("elevated-banked-reversal", turn.FeaturePrimitiveSequence);

                FeaturePlanResult result = FeaturePlanning.ComputePlanResult(
                    output, 0, output.Count,
                    TrackConnectionFrame.Origin(resolved.RoadWidth),
                    FrameBuildContext.From(resolved),
                    SemanticElementId.Horseshoe,
                    "horseshoe-definition-test");
                Assert.IsFalse(result.Failed, result.FailureReason);
                Assert.AreEqual(signedHeading, result.HeadingContributionDeg, 0.25f);
                Assert.AreEqual(0f, result.ElevationChange, 0.01f);
                Assert.Greater(result.MaxElevation,
                    definition.Turn.CrestHeightMeters.Preferred * 0.95f);
                Assert.AreEqual(0f, result.ExitState.PitchAngle, 0.05f);

                TrackConnectionFrame[] frames = SectionFrameBuilders.BuildSectionFrames(
                    TrackConnectionFrame.Origin(resolved.RoadWidth),
                    turn,
                    FrameBuildContext.From(resolved));
                float maximumVerticalCurvature = resolved.MaxCurvatureInducedG *
                    Mathf.Max(0.1f, resolved.Gravity) /
                    Mathf.Max(1f, resolved.DesignSpeedMps * resolved.DesignSpeedMps);
                Assert.LessOrEqual(
                    frames.Max(frame => Mathf.Abs(frame.VerticalCurvature)),
                    maximumVerticalCurvature * 1.05f,
                    "The authored crest must stay inside the global vertical-load rulebook.");
                Assert.LessOrEqual(
                    frames.Max(frame => Mathf.Abs(frame.VerticalCurvatureRate)),
                    resolved.MaxVerticalCurvatureRate * 1.05f,
                    "The authored crest must stay inside the global curvature-rate rulebook.");
                Assert.AreEqual(0f, frames[0].PitchAngle, 0.0001f);
                Assert.AreEqual(0f, frames[frames.Length - 1].PitchAngle, 0.0001f);
                Assert.AreEqual(0f, frames[0].VerticalCurvature, 0.000001f);
                Assert.AreEqual(0f, frames[frames.Length - 1].VerticalCurvature, 0.000001f);
                Assert.AreEqual(turn.Length,
                    frames[frames.Length - 1].ArcLength - frames[0].ArcLength, 0.001f,
                    "Definition length and built driving-line length must be identical.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [TestCase(180f)]
        [TestCase(-180f)]
        public void WideTurnaroundCompiler_PreservesSignedExitAndDefinitionIdentity(float signedHeading)
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                    SemanticElementId.WideTurnaround, out TrackFeatureDefinition definition));

                var output = new List<TrackMacroSectionDefinition>();
                Assert.IsTrue(TrackFeatureDefinitionCompiler.TryPlanTurnDefinition(
                    definition, resolved, signedHeading, "WideDefinitionTest", output));
                Assert.AreEqual(2, output.Count);

                TrackMacroSectionDefinition turn = output[0];
                float expectedBaseRadius = Mathf.Max(
                    Mathf.Lerp(resolved.MaxCurveRadius, resolved.MinCurveRadius,
                        Mathf.Abs(signedHeading) / 180f),
                    resolved.MinCurveRadius);
                float expectedRadius = Mathf.Max(
                    expectedBaseRadius * 1.35f,
                    resolved.MinCurveRadius * 1.35f);
                Assert.AreEqual(signedHeading, turn.Contract.HeadingDeltaDegrees, 0.001f);
                Assert.AreEqual(signedHeading > 0f
                    ? SectionTurnDirection.Right
                    : SectionTurnDirection.Left, turn.Direction);
                Assert.AreEqual(expectedRadius, turn.Radius, 0.001f,
                    "The definition adapter must preserve the accepted Wide Turnaround radius formula.");
                Assert.AreEqual(
                    SectionFrameBuilders.EasedArcLength(Mathf.Abs(signedHeading), expectedRadius),
                    turn.Length,
                    0.001f,
                    "The definition adapter must preserve the accepted eased-arc core length.");
                Assert.AreEqual("slingshot.wide-turnaround", turn.FeatureDefinitionId);
                Assert.AreEqual("broad-eased-reversal", turn.FeaturePrimitiveSequence);
                Assert.AreEqual(SemanticElementId.WideTurnaround, turn.SemanticElement);
                Assert.AreEqual(TrackMacroSectionType.RecoveryStraight, output[1].SectionType);
                Assert.AreEqual(
                    Mathf.Clamp(
                        resolved.DefaultRecoveryLength,
                        definition.RecoveryLength.ResolvedMinimum(resolved.DesignSpeedMps) *
                        resolved.FeatureFitScale,
                        definition.RecoveryLength.ResolvedMaximum(resolved.DesignSpeedMps) *
                        resolved.FeatureFitScale),
                    output[1].Length,
                    0.001f,
                    "The definition owns the full-size bounds; fitting resolves a compact runtime contract.");
                Assert.AreEqual(SemanticElementId.RecoverySection, output[1].SemanticElement);
                Assert.AreEqual("slingshot.wide-turnaround", output[1].FeatureDefinitionId);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DefinitionContentHash_ChangesWhenAuthoredContentChanges()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.FullLoop, out TrackFeatureDefinition definition));

            TrackFeatureDefinition changed = definition.Clone();
            string originalHash = definition.ComputeContentHash();
            changed.EntryLength.Preferred += 1f;

            Assert.AreNotEqual(originalHash, changed.ComputeContentHash());
            Assert.AreEqual(originalHash, definition.ComputeContentHash(),
                "Hashing a definition must not mutate the catalog copy.");
        }

        [Test]
        public void DefinitionCompiler_ConsumesAuthoredCorkscrewRadiiAndPhaseSplit()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                    TrackPatternType.Corkscrew, out TrackFeatureDefinition catalogDefinition));
                TrackFeatureDefinition definition = catalogDefinition.Clone();
                definition.Corkscrew.FirstHalfRadius.Minimum = 200f;
                definition.Corkscrew.FirstHalfRadius.Preferred = 200f;
                definition.Corkscrew.FirstHalfRadius.Maximum = 200f;
                definition.Corkscrew.SecondHalfRadius.Minimum = 240f;
                definition.Corkscrew.SecondHalfRadius.Preferred = 240f;
                definition.Corkscrew.SecondHalfRadius.Maximum = 240f;
                definition.Corkscrew.PhaseSplit.Minimum = 0.42f;
                definition.Corkscrew.PhaseSplit.Preferred = 0.42f;
                definition.Corkscrew.PhaseSplit.Maximum = 0.42f;

                var rng = new Unity.Mathematics.Random(0xC0FFEEu);
                var output = new List<TrackMacroSectionDefinition>();
                Assert.IsTrue(TrackFeatureDefinitionCompiler.TryPlanDefinition(
                    definition, resolved, "AuthoredCorkscrew", ref rng, output));

                TrackMacroSectionDefinition section = output.Single();
                RotationalPhaseDefinition phase = section.RotationalPhases.Single();
                Assert.AreEqual(200f, phase.FirstHalfRadius, 0.001f);
                Assert.AreEqual(240f, phase.SecondHalfRadius, 0.001f);
                Assert.AreEqual(0.42f, phase.FirstHalfLength / section.Length, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ExactRecipe_CapturesDefinitions_AndRejectsContentMismatch()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(7142);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(7142), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);

                Assert.GreaterOrEqual(recipe.FeatureDefinitions.Count, 2);
                Assert.IsTrue(recipe.ValidateFor(config, RecipeReplayMode.Strict, out string initialError),
                    initialError);

                GenerationRecipeV1 priorCatalogRecipe = recipe.Clone();
                priorCatalogRecipe.FeatureDefinitions.RemoveAll(identity =>
                    identity.StableId == "slingshot.wide-turnaround");
                priorCatalogRecipe.RefreshHashes();
                Assert.IsTrue(priorCatalogRecipe.ValidateFor(
                    config, RecipeReplayMode.Strict, out string additiveCatalogError),
                    "Adding an unused opt-in definition must not invalidate an older exact recipe: " +
                    additiveCatalogError);

                recipe.FeatureDefinitions[0].ContentHash = new string('0', 64);
                recipe.RefreshHashes();
                Assert.IsFalse(recipe.ValidateFor(config, RecipeReplayMode.Strict, out string mismatchError));
                StringAssert.Contains("Feature Definition", mismatchError);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PreDefinitionRecipeHash_RemainsValidWithoutSchemaBump()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(7143);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(7143), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);
                recipe.FeatureDefinitions.Clear();
                recipe.RefreshHashes();

                GenerationRecipeV1 legacyHashCopy = recipe.Clone();
                legacyHashCopy.RecipeHash = "";
                legacyHashCopy.BaseRecipeHash = "";
                legacyHashCopy.ExpectedLayoutHash = "";
                string legacyJson = JsonUtility.ToJson(legacyHashCopy, false)
                    .Replace(",\"FeatureDefinitions\":[]", "");
                recipe.RecipeHash = GenerationHashUtility.Sha256Hex(legacyJson);

                Assert.IsTrue(recipe.ValidateFor(config, RecipeReplayMode.Strict, out string error), error);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [TestCase(TrackPatternType.FullLoop, "slingshot.vertical-loop", "half-loop-up>half-loop-down")]
        [TestCase(TrackPatternType.Corkscrew, "slingshot.inline-corkscrew", "half-corkscrew-in>half-corkscrew-out")]
        public void CompiledPattern_StampsExactDefinitionIdentity(
            TrackPatternType patternType, string expectedId, string expectedPrimitives)
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                Assert.IsTrue(FeaturePatternLibrary.TryGet(patternType, out ITrackFeaturePattern pattern));

                var rng = new Unity.Mathematics.Random(0xA17E1u + (uint)patternType);
                var output = new List<TrackMacroSectionDefinition>();
                Assert.IsTrue(pattern.TryPlan(resolved, $"DefinitionTest_{patternType}", ref rng, output));
                Assert.IsNotEmpty(output);

                foreach (TrackMacroSectionDefinition section in output)
                {
                    Assert.AreEqual(expectedId, section.FeatureDefinitionId);
                    Assert.AreEqual(1, section.FeatureDefinitionVersion);
                    Assert.IsNotEmpty(section.FeatureDefinitionContentHash);
                    Assert.AreEqual(expectedPrimitives, section.FeaturePrimitiveSequence);
                    Assert.Greater(section.FeatureDefinitionEntryBudget, 0f);
                    Assert.Greater(section.FeatureDefinitionRecoveryBudget, 0f);
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void FeatureDefinitionCatalog_CoversEveryDesignerPatternExactlyOnce()
        {
            Assert.IsEmpty(TrackFeatureDefinitionCatalog.LoadErrors,
                string.Join("\n", TrackFeatureDefinitionCatalog.LoadErrors));

            TrackPatternType[] patterns = (TrackPatternType[])System.Enum.GetValues(
                typeof(TrackPatternType));
            foreach (TrackPatternType pattern in patterns)
            {
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                        pattern, out TrackFeatureDefinition definition),
                    $"Designer pattern {pattern} has no individual Feature Definition asset.");
                Assert.IsTrue(definition.HasPatternType);
                Assert.AreEqual(pattern, definition.PatternType);
                Assert.AreEqual(FeaturePlanning.ElementOf(pattern), definition.SemanticElement,
                    $"Definition {definition.StableId} does not match the authoritative semantic mapping.");
                AssertDefinitionContract(definition);
            }

            Assert.AreEqual(patterns.Length,
                TrackFeatureDefinitionCatalog.All.Count(definition => definition.HasPatternType),
                "Every designer pattern must own exactly one definition asset.");
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryValidateDesignerCoverage(
                out string coverage, out List<TrackPatternType> missing), coverage);
            Assert.IsEmpty(missing);
        }

        [TestCase(SemanticElementId.OrdinaryCurve, "slingshot.ordinary-curve")]
        [TestCase(SemanticElementId.DirectionalCorkscrew, "slingshot.directional-corkscrew")]
        public void FeatureDefinitionCatalog_CoversSelectableNonPatternRealizations(
            SemanticElementId element, string expectedStableId)
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                element, out TrackFeatureDefinition definition));
            Assert.IsFalse(definition.HasPatternType);
            Assert.AreEqual(expectedStableId, definition.StableId);
            AssertDefinitionContract(definition);
        }

        [Test]
        public void LegacyPatternStamp_UsesDefinitionIdentityWithoutChangingGeometry()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                var section = SectionDefs.Straight(
                    TrackMacroSectionType.Straight, 432f, resolved.RoadWidth, "LegacySCurveCore");
                section.Contract = SectionConnectionContract.Level(0f);
                var sections = new List<TrackMacroSectionDefinition> { section };

                FeaturePlanning.StampRange(
                    sections, 0, SemanticElementId.SCurve, resolved);

                Assert.AreEqual(432f, section.Length, 0.0001f);
                Assert.AreEqual(TrackMacroSectionType.Straight, section.SectionType);
                Assert.AreEqual("slingshot.s-curve", section.FeatureDefinitionId);
                Assert.AreEqual(1, section.FeatureDefinitionVersion);
                Assert.IsNotEmpty(section.FeatureDefinitionContentHash);
                Assert.AreEqual("offset-arc-out>offset-arc-return",
                    section.FeaturePrimitiveSequence);
                Assert.Greater(section.FeatureDefinitionEntryBudget, 0f);
                Assert.Greater(section.FeatureDefinitionRecoveryBudget, 0f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Camelback_DefinitionCompilerProducesSafeLevelC2Crest()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                    TrackPatternType.Camelback, out TrackFeatureDefinition definition));
                Assert.AreEqual(SemanticElementId.Camelback, definition.SemanticElement);
                Assert.AreEqual(FeatureDefinitionSolver.VerticalBumpV1, definition.Solver);
                Assert.LessOrEqual(definition.Length.Maximum, 1000f);
                Assert.IsTrue(FeaturePatternLibrary.TryGet(
                    TrackPatternType.Camelback, out ITrackFeaturePattern pattern));

                var rng = new Unity.Mathematics.Random(0xCA4E1u);
                var output = new List<TrackMacroSectionDefinition>();
                Assert.IsTrue(pattern.TryPlan(resolved, "CamelbackDefinitionTest", ref rng, output));
                TrackMacroSectionDefinition core = output.Single();
                Assert.AreEqual(TrackMacroSectionType.Straight, core.SectionType);
                Assert.AreEqual(SemanticElementId.Camelback, core.SemanticElement);
                Assert.AreEqual("slingshot.camelback", core.FeatureDefinitionId);
                Assert.GreaterOrEqual(core.HillHeight, definition.VerticalBump.HeightMeters.Minimum);
                Assert.LessOrEqual(core.HillHeight, definition.VerticalBump.HeightMeters.Maximum);

                TrackConnectionFrame entry = TrackConnectionFrame.Origin(resolved.RoadWidth);
                TrackConnectionFrame[] frames = SectionFrameBuilders.BuildSectionFrames(
                    entry, core, FrameBuildContext.From(resolved));
                TrackConnectionFrame exit = frames[frames.Length - 1];
                Assert.AreEqual(0f, exit.PitchAngle, 0.01f);
                Assert.AreEqual(0f, exit.Position.y, 0.01f);
                Assert.Less(Vector3.Angle(entry.Forward, exit.Forward), 0.01f);
                Assert.AreEqual(0f, SectionFrameBuilders.CrestBump(0f), 0.0000001f);
                Assert.AreEqual(0f, SectionFrameBuilders.CrestBump(1f), 0.0000001f);
                Assert.AreEqual(0f, SectionFrameBuilders.CrestBumpDerivative(0f), 0.0000001f);
                Assert.AreEqual(0f, SectionFrameBuilders.CrestBumpDerivative(1f), 0.0000001f);

                float maxCurvature = resolved.MaxCurvatureInducedG * resolved.Gravity /
                                     (resolved.DesignSpeedMps * resolved.DesignSpeedMps);
                Assert.LessOrEqual(frames.Max(frame => Mathf.Abs(frame.VerticalCurvature)),
                    maxCurvature * 0.921f);
                Assert.LessOrEqual(frames.Max(frame => Mathf.Abs(frame.VerticalCurvatureRate)),
                    resolved.MaxVerticalCurvatureRate * 0.921f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void VerticalBumpDefinition_RejectsPrimitiveAndLobeDirectionMismatch()
        {
            Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                TrackPatternType.Camelback, out TrackFeatureDefinition source));
            TrackFeatureDefinition invalid = source.Clone();
            invalid.Primitives[0].PrimitiveType = FeatureGeometryPrimitiveType.DipStraight;
            Assert.IsFalse(invalid.Validate(out string error));
            StringAssert.Contains("requires CrestStraight", error);
        }

        [Test]
        public void Cutback_DefinitionCompilerOwnsACompact135DegreeTurn()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                    TrackPatternType.Cutback, out TrackFeatureDefinition definition));
                Assert.AreEqual(SemanticElementId.Cutback, definition.SemanticElement);
                Assert.AreEqual(FeatureDefinitionSolver.EasedTurnV1, definition.Solver);
                Assert.IsTrue(FeaturePatternLibrary.IsCornerSlotPattern(TrackPatternType.Cutback));

                var output = new List<TrackMacroSectionDefinition>();
                Assert.IsTrue(TrackFeatureDefinitionCompiler.TryPlanTurn(
                    SemanticElementId.Cutback, resolved, -135f,
                    "CutbackDefinitionTest", output, out string failure), failure);
                TrackMacroSectionDefinition core = output.Single();
                Assert.AreEqual(SemanticElementId.Cutback, core.SemanticElement);
                Assert.AreEqual("slingshot.cutback", core.FeatureDefinitionId);
                Assert.AreEqual(135f, core.TurnAngle, 0.001f);
                Assert.AreEqual(SectionTurnDirection.Left, core.Direction);
                Assert.LessOrEqual(core.Length, 1000.01f,
                    "Definition-native ordinary-road turns retain the shared 1 km core cap.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RotationalDefinition_MissingCoreRollRateUsesSafeMigrationDefault()
        {
            var parameters = new RotationalFeatureDefinitionParameters
            {
                MaximumCoreRollRateDegreesPerSecond = 0f
            };

            parameters.Sanitize();

            Assert.AreEqual(
                RotationalFeatureDefinitionParameters.DefaultMaximumCoreRollRateDegreesPerSecond,
                parameters.MaximumCoreRollRateDegreesPerSecond,
                0.0001f,
                "Definitions authored before the core-rate field existed must not migrate to 1 degree/second.");
        }

        [Test]
        public void RotationalCatalogWave_CompilesCompactStampedExitExactFeatures()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cases = new[]
                {
                    (TrackPatternType.HeartlineRoll, SemanticElementId.HeartlineRoll, 0f),
                    (TrackPatternType.ZeroGRoll, SemanticElementId.ZeroGRoll, 0f),
                    (TrackPatternType.DiveLoop, SemanticElementId.DiveLoop, 180f),
                    (TrackPatternType.DiveLoop, SemanticElementId.DiveLoop, -180f),
                    (TrackPatternType.Sidewinder, SemanticElementId.Sidewinder, 90f),
                    (TrackPatternType.Sidewinder, SemanticElementId.Sidewinder, -90f)
                };

                foreach (float speedKph in new[] { 400f, 1300f, 2000f })
                {
                    TrackDesignerSettings settings =
                        TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                    settings.Scale.DesignSpeedKph = speedKph;
                    ResolvedTrackGenerationConfig resolved =
                        ResolvedTrackGenerationConfig.Resolve(config, settings);

                    foreach (var item in cases)
                    {
                        Assert.IsTrue(TrackFeatureDefinitionCatalog.TryGet(
                            item.Item1, out TrackFeatureDefinition definition));
                        Assert.AreEqual(item.Item2, definition.SemanticElement);
                        Assert.AreEqual(FeatureDefinitionSolver.RotationalSequenceV1, definition.Solver);
                        Assert.IsTrue(definition.Validate(out string definitionError), definitionError);

                        var output = new List<TrackMacroSectionDefinition>();
                        if (Mathf.Approximately(item.Item3, 0f))
                        {
                            Assert.IsTrue(FeaturePatternLibrary.TryGet(item.Item1, out ITrackFeaturePattern pattern));
                            var rng = new Unity.Mathematics.Random(0xB011u + (uint)item.Item1);
                            Assert.IsTrue(pattern.TryPlan(resolved, $"Rotational_{item.Item1}", ref rng, output),
                                $"{item.Item1} failed at {speedKph:F0} km/h.");
                        }
                        else
                        {
                            Assert.IsTrue(FeaturePatternLibrary.IsCornerSlotPattern(item.Item1));
                            Assert.IsTrue(TrackFeatureDefinitionCompiler.TryPlanTurnDefinition(
                                definition, resolved, item.Item3, $"Rotational_{item.Item1}",
                                output, out string failure),
                                $"{item.Item1} at {speedKph:F0} km/h: {failure}");
                        }

                        TrackMacroSectionDefinition core = output.Single(section =>
                            section.SectionType == TrackMacroSectionType.RotationalEvent);
                        Assert.AreEqual(item.Item2, core.SemanticElement);
                        Assert.AreEqual(definition.StableId, core.FeatureDefinitionId);
                        bool ownsVerticalCenterline = definition.Primitives.Any(primitive =>
                            primitive.PrimitiveType == FeatureGeometryPrimitiveType.VerticalArc);
                        float expectedMaximum = ownsVerticalCenterline
                            ? definition.Length.Maximum * Mathf.Max(1f,
                                resolved.DesignSpeedMps / definition.Length.ReferenceSpeedMps)
                            : definition.Length.ResolvedMaximum(resolved.DesignSpeedMps);
                        Assert.LessOrEqual(core.Length, expectedMaximum + 0.5f);
                        Assert.LessOrEqual(
                            Mathf.Abs(Mathf.DeltaAngle(core.TurnAngle, item.Item3)),
                            1f,
                            $"{item.Item1} must preserve its authored signed heading.");
                        Assert.IsTrue(core.RotationalPhases.All(phase => phase.RotationUnits > 0));

                        TrackConnectionFrame[] frames = SectionFrameBuilders.BuildRotationalEvent(
                            TrackConnectionFrame.Origin(resolved.RoadWidth), core,
                            FrameBuildContext.From(resolved));
                        Assert.Greater(frames.Length, 2);
                        float authoredCoreRollRate =
                            definition.Rotational.MaximumCoreRollRateDegreesPerSecond /
                            resolved.DesignSpeedMps;
                        Assert.LessOrEqual(frames.Max(frame => Mathf.Abs(frame.RoadRollRate)),
                            authoredCoreRollRate * 1.021f);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void FeatureDefinitionCatalog_AuthoritativeSolversSmokeBuildFiniteStampedGeometry()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                Assert.IsTrue(TrackFeatureDefinitionCompiler.TrySmokeCompileCatalog(
                    resolved, out int compiledCount, out List<string> errors),
                    string.Join("\n", errors));
                Assert.GreaterOrEqual(compiledCount, 12,
                    "Every definition-owned solver must participate in the authoring smoke gate.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AdditiveFeatureRequiredPattern_SurvivesExactRecipeRoundTrip()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                settings.Features.RequiredPatterns.Add(new RequiredPatternEntry
                {
                    Pattern = TrackPatternType.Camelback,
                    Count = 1
                });
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(81234);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(81234), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);

                GenerationRecipeV1 restored = JsonUtility.FromJson<GenerationRecipeV1>(
                    JsonUtility.ToJson(recipe, false));
                Assert.NotNull(restored);
                TrackDesignerSettings restoredSettings = restored.DeserializeDesignerSettings();
                Assert.NotNull(restoredSettings);
                RequiredPatternEntry required = restoredSettings.Features.RequiredPatterns.Single(
                    entry => entry.Pattern == TrackPatternType.Camelback);
                Assert.AreEqual(1, required.Count);
                Assert.IsTrue(restored.ValidateFor(
                    config, RecipeReplayMode.Strict, out string validationError), validationError);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AdditiveFeatureAmountRules_AreOptInAndSurviveExactRecipeRoundTrip()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                Assert.IsFalse(settings.Features.Camelbacks.Enabled);
                Assert.IsFalse(settings.Features.Cutbacks.Enabled);
                Assert.IsFalse(settings.Features.HeartlineRolls.Enabled);
                Assert.IsFalse(settings.Features.ZeroGRolls.Enabled);
                Assert.IsFalse(settings.Features.DiveLoops.Enabled);
                Assert.IsFalse(settings.Features.Sidewinders.Enabled);

                settings.Features.Camelbacks = new TrackFeatureRule(true, 1, 1, 0.7f);
                settings.Features.Cutbacks = new TrackFeatureRule(true, 1, 1, 0.6f);
                settings.Features.HeartlineRolls = new TrackFeatureRule(true, 1, 1, 0.5f);
                settings.Features.ZeroGRolls = new TrackFeatureRule(true, 1, 1, 0.45f);
                settings.Features.DiveLoops = new TrackFeatureRule(true, 1, 1, 0.4f);
                settings.Features.Sidewinders = new TrackFeatureRule(true, 1, 1, 0.35f);
                settings.Sanitize();

                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                Assert.AreEqual(1, resolved.Camelbacks.MinimumCount);
                Assert.AreEqual(1, resolved.Cutbacks.MinimumCount);
                Assert.AreEqual(1, resolved.HeartlineRolls.MinimumCount);
                Assert.AreEqual(1, resolved.ZeroGRolls.MinimumCount);
                Assert.AreEqual(1, resolved.DiveLoops.MinimumCount);
                Assert.AreEqual(1, resolved.Sidewinders.MinimumCount);

                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(41973);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(41973), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);
                GenerationRecipeV1 restored = JsonUtility.FromJson<GenerationRecipeV1>(
                    JsonUtility.ToJson(recipe, false));
                TrackDesignerSettings restoredSettings = restored.DeserializeDesignerSettings();
                Assert.NotNull(restoredSettings);
                Assert.AreEqual(1, restoredSettings.Features.Camelbacks.MinimumCount);
                Assert.AreEqual(1, restoredSettings.Features.Cutbacks.MinimumCount);
                Assert.AreEqual(1, restoredSettings.Features.HeartlineRolls.MinimumCount);
                Assert.AreEqual(1, restoredSettings.Features.ZeroGRolls.MinimumCount);
                Assert.AreEqual(1, restoredSettings.Features.DiveLoops.MinimumCount);
                Assert.AreEqual(1, restoredSettings.Features.Sidewinders.MinimumCount);
                Assert.IsTrue(restored.ValidateFor(
                    config, RecipeReplayMode.Strict, out string validationError), validationError);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LegacyFeatureSettings_MigrateWithAdditiveDefinitionsDisabled()
        {
            const string legacyJson =
                "{\"Features\":{\"ProceduralRhythmSettingsVersion\":1," +
                "\"EnforceProceduralRhythm\":true}}";
            TrackDesignerSettings restored =
                JsonUtility.FromJson<TrackDesignerSettings>(legacyJson);
            Assert.NotNull(restored);
            restored.Sanitize();
            Assert.NotNull(restored.Features.Camelbacks);
            Assert.NotNull(restored.Features.Cutbacks);
            Assert.NotNull(restored.Features.HeartlineRolls);
            Assert.NotNull(restored.Features.ZeroGRolls);
            Assert.NotNull(restored.Features.DiveLoops);
            Assert.NotNull(restored.Features.Sidewinders);
            Assert.IsFalse(restored.Features.Camelbacks.Enabled);
            Assert.IsFalse(restored.Features.Cutbacks.Enabled);
            Assert.IsFalse(restored.Features.HeartlineRolls.Enabled);
            Assert.IsFalse(restored.Features.ZeroGRolls.Enabled);
            Assert.IsFalse(restored.Features.DiveLoops.Enabled);
            Assert.IsFalse(restored.Features.Sidewinders.Enabled);
        }

        private static void AssertDefinitionContract(TrackFeatureDefinition definition)
        {
            Assert.NotNull(definition);
            Assert.IsTrue(definition.Validate(out string error), error);
            Assert.Greater(definition.DefinitionVersion, 0);
            Assert.Greater(definition.Length.Maximum, 0f);
            Assert.Greater(definition.EntryLength.Maximum, 0f);
            Assert.Greater(definition.RecoveryLength.Maximum, 0f);
            Assert.Greater(definition.PreferredTotalOccupiedLength(361.1111f),
                definition.Length.ResolvedPreferred(361.1111f));
        }
    }
}
