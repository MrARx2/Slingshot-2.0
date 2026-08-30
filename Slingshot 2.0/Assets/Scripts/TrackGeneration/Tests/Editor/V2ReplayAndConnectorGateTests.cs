using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using TrackGeneration.Validation;

namespace TrackGeneration.Tests
{
    /// <summary>Small V2 gate. No corpus scan, seed search, mesh build, or persistent scene mutation.</summary>
    [Category("V2FastGate")]
    public sealed partial class V2ReplayAndConnectorGateTests
    {
        [Test]
        public void TrackEditorReturnToOriginal_RemovesReplacementOverride()
        {
            var overrides = new List<TopologySlotOverride>
            {
                new TopologySlotOverride
                {
                    TopologySlotId = "slot-q2-r0-turn-002-n180",
                    OriginalRealization = SemanticElementId.Hairpin,
                    RequestedRealization = SemanticElementId.Horseshoe
                }
            };
            var restore = new TopologySlotOverride
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Hairpin
            };

            Assert.IsTrue(TrackGenerator.MergeTopologyOverrideForAuthoring(
                overrides, restore, out bool restoredOriginal));

            Assert.IsTrue(restoredOriginal);
            Assert.IsEmpty(overrides,
                "Restoring the procedural original must remove the override rather than compile another generic Hairpin.");
        }

        [Test]
        public void TrackEditorReturnToOriginal_WithAcceptedImpact_KeepsRemovedNeighborsRemoved()
        {
            var accepted = new TopologySlotOverride
            {
                TopologySlotId = "selected",
                StructuralAnchor = "source-anchor",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Horseshoe,
                ImpactForwardFeatures = 1,
                AllowFeatureOverrides = true,
                ImpactMembers = new List<TopologyImpactMember>
                {
                    new TopologyImpactMember
                    {
                        TopologySlotId = "removed-neighbor",
                        RelativeFeatureOffset = 1,
                        OriginalRealization = SemanticElementId.InlineCorkscrew
                    }
                }
            };
            var overrides = new List<TopologySlotOverride> { accepted };
            var restoreFeatureOnly = new TopologySlotOverride
            {
                TopologySlotId = "selected",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Hairpin
            };

            Assert.IsTrue(TrackGenerator.MergeTopologyOverrideForAuthoring(
                overrides, restoreFeatureOnly, out bool restoredOriginal));

            Assert.IsFalse(restoredOriginal,
                "The whole override cannot be removed while it still owns an accepted Area of Impact removal.");
            Assert.AreEqual(1, overrides.Count);
            Assert.AreEqual(SemanticElementId.Hairpin, overrides[0].RequestedRealization);
            Assert.AreEqual(1, overrides[0].ImpactMembers.Count);
            Assert.AreEqual("removed-neighbor", overrides[0].ImpactMembers[0].TopologySlotId);
            Assert.IsTrue(overrides[0].AllowFeatureOverrides);
        }

        [Test]
        public void TrackEditorAreaOfImpact_UsesIndependentRouteDirectionControls()
        {
            List<TopologySlotRecord> slots = ImpactSlots();

            TrackEditorImpactPlan plan = TrackEditorImpact.Analyze(
                slots, "selected", backwardFeatures: 2, forwardFeatures: 1);

            Assert.AreSame(slots[2], plan.Selected);
            CollectionAssert.AreEqual(new[] { "before-2", "before-1" },
                plan.Backward.ConvertAll(slot => slot.TopologySlotId));
            CollectionAssert.AreEqual(new[] { "after-1" },
                plan.Forward.ConvertAll(slot => slot.TopologySlotId));
            Assert.AreEqual(3, plan.FeatureOverrideCount);
            Assert.IsFalse(plan.Forward.Contains(slots[4]),
                "A one-feature reach must stop at the nearest forward feature.");
        }

        [Test]
        public void TrackEditorAreaOfImpact_CanonicalLapCanReachBeforeAndAfterAcrossQuarterBoundary()
        {
            var slots = new List<TopologySlotRecord>
            {
                ImpactSlot("before-q2", 1, 0, 1, 10, 14, SemanticElementId.InlineCorkscrew),
                ImpactSlot("selected-q2-end", 1, 0, 2, 20, 24, SemanticElementId.Hairpin),
                ImpactSlot("after-q3", 2, 0, 0, 25, 31, SemanticElementId.InlineCorkscrew),
                ImpactSlot("alternate-q3", 2, 1, 0, 32, 38, SemanticElementId.VerticalLoop)
            };

            TrackEditorImpactPlan plan = TrackEditorImpact.Analyze(
                slots, "selected-q2-end", backwardFeatures: 1, forwardFeatures: 1);

            CollectionAssert.AreEqual(new[] { "before-q2" },
                plan.Backward.ConvertAll(slot => slot.TopologySlotId));
            CollectionAssert.AreEqual(new[] { "after-q3" },
                plan.Forward.ConvertAll(slot => slot.TopologySlotId));
            Assert.AreEqual(2, plan.FeatureOverrideCount);
            Assert.IsFalse(plan.Forward.Contains(slots[3]),
                "Area of Impact must not jump onto an alternate-road branch.");

            TopologySlotRecord selected = slots[1];
            var request = new TopologySlotOverride
            {
                TopologySlotId = selected.TopologySlotId,
                StructuralAnchor = TopologySlotCatalog.BuildStructuralAnchor(selected),
                OriginalRealization = selected.OriginalRealization,
                RequestedRealization = SemanticElementId.Immelmann,
                ImpactBackwardFeatures = 1,
                ImpactForwardFeatures = 1,
                ImpactMembers = TrackEditorImpact.CaptureMembers(plan),
                AllowFeatureOverrides = true
            };

            Assert.AreEqual(-1, request.ImpactMembers[0].RelativeFeatureOffset);
            Assert.AreEqual(1, request.ImpactMembers[1].RelativeFeatureOffset);
            Assert.IsTrue(TrackTopologyPlanner.TryResolveAuthoringImpactWindow(
                slots, request, selected, out int first, out int last,
                out List<TopologySlotRecord> impacted, out string reason), reason);
            Assert.AreEqual(10, first);
            Assert.AreEqual(31, last);
            CollectionAssert.AreEqual(new[] { "before-q2", "after-q3" },
                impacted.ConvertAll(slot => slot.TopologySlotId));
        }

        [Test]
        public void TrackEditorAreaOfImpact_RequestCapturesConfirmedStableIdentities()
        {
            var owner = new GameObject("TrackEditor_AreaOfImpact_Request_Test");
            try
            {
                TrackEditor editor = owner.AddComponent<TrackEditor>();
                List<TopologySlotRecord> slots = ImpactSlots();
                editor.SetImpactReach(1, 1);
                TrackEditorImpactPlan plan = TrackEditorImpact.Analyze(
                    slots, "selected", editor.ImpactBackwardFeatures,
                    editor.ImpactForwardFeatures);

                Assert.IsTrue(editor.TryRequestReplacement(slots[2],
                    SemanticElementId.WideTurnaround, plan,
                    allowFeatureOverrides: true, out string reason), reason);

                TopologySlotOverride request = editor.FindRequestedOverride("selected");
                Assert.NotNull(request);
                Assert.IsTrue(request.AllowFeatureOverrides);
                Assert.AreEqual(LocalReplanScope.TurnComplex, request.MaximumReplanScope);
                Assert.AreEqual(2, request.ImpactMembers.Count);
                Assert.AreEqual(-1, request.ImpactMembers[0].RelativeFeatureOffset);
                Assert.AreEqual(1, request.ImpactMembers[1].RelativeFeatureOffset);
                Assert.That(request.ImpactMembers[0].StructuralAnchor, Is.Not.Empty);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void TrackEditorAreaOfImpact_WindowRequiresExplicitOverrideConfirmation()
        {
            List<TopologySlotRecord> slots = ImpactSlots();
            TopologySlotRecord selected = slots[2];
            TrackEditorImpactPlan plan = TrackEditorImpact.Analyze(
                slots, selected.TopologySlotId, 1, 1);
            var request = new TopologySlotOverride
            {
                TopologySlotId = selected.TopologySlotId,
                StructuralAnchor = TopologySlotCatalog.BuildStructuralAnchor(selected),
                OriginalRealization = selected.OriginalRealization,
                RequestedRealization = SemanticElementId.WideTurnaround,
                ImpactMembers = TrackEditorImpact.CaptureMembers(plan),
                AllowFeatureOverrides = false
            };

            Assert.IsFalse(TrackTopologyPlanner.TryResolveAuthoringImpactWindow(
                slots, request, selected, out _, out _, out _, out string blocked));
            StringAssert.Contains("not confirmed", blocked);

            request.AllowFeatureOverrides = true;
            Assert.IsTrue(TrackTopologyPlanner.TryResolveAuthoringImpactWindow(
                slots, request, selected, out int first, out int last,
                out List<TopologySlotRecord> impacted, out string error), error);
            Assert.AreEqual(10, first);
            Assert.AreEqual(31, last);
            CollectionAssert.AreEqual(new[] { "before-1", "after-1" },
                impacted.ConvertAll(slot => slot.TopologySlotId));
        }

        [Test]
        public void ExactRecipe_AreaOfImpact_SurvivesSaveLoadAndChangesVariantIdentity()
        {
            var recipe = new GenerationRecipeV1();
            recipe.TopologySlotOverrides.Add(new TopologySlotOverride
            {
                TopologySlotId = "selected",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Horseshoe
            });
            recipe.RefreshHashes();
            string focusedHash = recipe.RecipeHash;
            recipe.TopologySlotOverrides[0].ImpactForwardFeatures = 1;
            recipe.TopologySlotOverrides[0].AllowFeatureOverrides = true;
            recipe.TopologySlotOverrides[0].ImpactMembers.Add(new TopologyImpactMember
            {
                TopologySlotId = "after-1",
                StructuralAnchor = "0|0|1|90|2|3",
                RelativeFeatureOffset = 1,
                OriginalRealization = SemanticElementId.OrdinaryCurve
            });

            GenerationRecipeV1 loaded = GenerationRecipeV1.Deserialize(
                GenerationRecipeV1.Serialize(recipe, false), out string error);

            Assert.NotNull(loaded, error);
            Assert.AreNotEqual(focusedHash, loaded.RecipeHash);
            TopologySlotOverride loadedRequest = loaded.TopologySlotOverrides[0];
            Assert.AreEqual(1, loadedRequest.ImpactForwardFeatures);
            Assert.IsTrue(loadedRequest.AllowFeatureOverrides);
            Assert.AreEqual("after-1", loadedRequest.ImpactMembers[0].TopologySlotId);
        }

        [Test]
        public void ExactRecipe_PreAreaOfImpactMetadataHash_RemainsCompatible()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(20260828);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(20260828), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);
                recipe.TopologySlotOverrides.Add(new TopologySlotOverride
                {
                    TopologySlotId = "legacy-impact-shape",
                    OriginalRealization = SemanticElementId.Hairpin,
                    RequestedRealization = SemanticElementId.Horseshoe
                });
                recipe.RefreshHashes();

                GenerationRecipeV1 legacyHashCopy = recipe.Clone();
                legacyHashCopy.RecipeHash = "";
                legacyHashCopy.BaseRecipeHash = "";
                legacyHashCopy.ExpectedLayoutHash = "";
                string legacyJson = JsonUtility.ToJson(legacyHashCopy, false);
                legacyJson = RemovePrimitiveFieldForLegacyFixture(legacyJson,
                    nameof(TopologySlotOverride.ImpactBackwardFeatures));
                legacyJson = RemovePrimitiveFieldForLegacyFixture(legacyJson,
                    nameof(TopologySlotOverride.ImpactForwardFeatures));
                legacyJson = RemovePrimitiveFieldForLegacyFixture(legacyJson,
                    nameof(TopologySlotOverride.AllowFeatureOverrides));
                legacyJson = legacyJson.Replace(",\"ImpactMembers\":[]", "");
                recipe.RecipeHash = GenerationHashUtility.Sha256Hex(legacyJson);

                Assert.IsTrue(recipe.ValidateFor(config, RecipeReplayMode.Strict,
                    out string error), error);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TrackEditorAuthoringPolicy_MakesProceduralLengthCapAdvisory()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                TargetTrackLength = 45000f,
                MaxTrackLength = 60000f,
                MaxCurveRadius = 900f,
                MaxClosureCurveRadius = 1200f
            };

            TrackEditorAuthoringPolicy.Apply(resolved, 61700f);

            Assert.IsTrue(resolved.DesignerAuthoringMode);
            Assert.AreEqual(61700f, resolved.TargetTrackLength, 0.01f);
            Assert.Greater(resolved.MaxTrackLength, 60000f,
                "An authored edit must not be rejected by the procedural generator's 60km cap.");
            Assert.GreaterOrEqual(resolved.MaxClosureCurveRadius, 3600f,
                "Safe larger-radius closure curves should receive extra fitting authority.");
        }

        [Test]
        public void TrackEditorAuthoringElevationResidual_UsesUnusedRecoveryCapacity()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    Length = 900f,
                    DebugName = "ExistingMajor"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 543f,
                    DebugName = "RecoveryStraight"
                }
            };
            var occupied = new HashSet<int> { 0 };

            float residual = TrackTopologyPlanner.ApplyAuthoringElevationRecovery(
                resolved, definitions, occupied, 94.1f);

            Assert.AreEqual(0f, residual, 0.01f);
            Assert.AreEqual(-94.1f, definitions[1].ElevationChange, 0.01f);
            Assert.IsTrue(occupied.Contains(1));
            Assert.That(definitions[1].DebugName, Does.Contain("AuthoringDrop94m"));
        }

        [Test]
        public void TrackEditorAuthoringVerticalFeature_AddsLegalOwnedRecoveryBeforeClosure()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                RoadWidth = 100f,
                DefaultRecoveryLength = 540f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RotationalEvent,
                    Length = 2038.4f,
                    Width = 100f,
                    ElevationChange = 223.4f,
                    PatternId = "Immelmann_Test",
                    DebugName = "HalfLoopRollout_Test"
                }
            };

            Assert.IsTrue(TrackTopologyPlanner.EnsureAuthoringVerticalRecovery(
                resolved, definitions, out float recoveryLength,
                out float recoveryElevation));

            Assert.AreEqual(2, definitions.Count);
            TrackMacroSectionDefinition recovery = definitions[1];
            Assert.AreEqual(TrackMacroSectionType.RecoveryStraight,
                recovery.SectionType);
            Assert.IsTrue(recovery.LockLength);
            Assert.AreEqual("Immelmann_Test", recovery.PatternId);
            Assert.AreEqual(-223.4f, recoveryElevation, 0.01f);
            Assert.AreEqual(recoveryElevation, recovery.ElevationChange, 0.01f);
            Assert.AreEqual(0f, definitions[0].ElevationChange +
                                recovery.ElevationChange, 0.01f);
            float legalDropCapacity = recoveryLength *
                Mathf.Tan(resolved.MaxDropAngle * Mathf.Deg2Rad) / 4f;
            Assert.Greater(legalDropCapacity, 223.4f,
                "Owned recovery must retain safety headroom below the configured drop limit.");
            Assert.IsFalse(TrackTopologyPlanner.EnsureAuthoringVerticalRecovery(
                resolved, definitions, out _, out _),
                "A vertically balanced candidate must not receive duplicate recovery road.");
        }

        [Test]
        public void TrackEditorAuthoringElevation_ExactLayersRelaxWhenTheyWouldRejectLegalEdit()
        {
            TrackMacroSectionDefinition Stable(string pattern, string name) =>
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    PatternId = pattern,
                    RoadId = 0,
                    Length = 2000f,
                    Width = 20f,
                    DebugName = name
                };

            var accepted = new List<GeneratedTrackSection>
            {
                new GeneratedTrackSection
                {
                    Definition = Stable("stable-entry", "StableEntry"),
                    StartFrame = new TrackConnectionFrame { Position = Vector3.zero },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.zero }
                },
                new GeneratedTrackSection
                {
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.WallrideTurn,
                        PatternId = "old-feature",
                        RoadId = 0,
                        DebugName = "OldFeature"
                    },
                    StartFrame = new TrackConnectionFrame { Position = Vector3.zero },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.up * 100f }
                },
                new GeneratedTrackSection
                {
                    Definition = Stable("stable-finish", "StableFinish"),
                    StartFrame = new TrackConnectionFrame { Position = Vector3.up * 100f },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.zero }
                }
            };
            IReadOnlyList<AuthoringElevationBaselineEntry> baseline =
                TrackTopologyPlanner.CaptureAuthoringElevationBaseline(accepted);
            List<TrackMacroSectionDefinition> Rebuilt() => new List<TrackMacroSectionDefinition>
            {
                Stable("stable-entry", "StableEntry"),
                new TrackMacroSectionDefinition
                {
                    // TunnelVariant walks as ordinary continuous road but is not an
                    // authoring elevation carrier, which makes the exact local anchor
                    // deliberately impossible without the designer-first fallback.
                    SectionType = TrackMacroSectionType.TunnelVariant,
                    PatternId = "replacement-feature",
                    RoadId = 0,
                    Length = 1000f,
                    Width = 20f,
                    DebugName = "Replacement"
                },
                Stable("stable-finish", "StableFinish")
            };
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                RoadWidth = 20f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                MeshMetersPerRing = 20f,
                FeatureMetersPerRing = 10f,
                MaxRingFacetAngle = 5f,
                MaxRingsPerSection = 2048
            };

            float strict = TrackTopologyPlanner.ReconcileAuthoringElevationAnchors(
                resolved, Rebuilt(), baseline, out _);
            float flexible = TrackTopologyPlanner.ReconcileAuthoringElevationForEditor(
                resolved, Rebuilt(), baseline, out _, out bool relaxed);

            Assert.Greater(Mathf.Abs(strict), 0.5f,
                "The synthetic accepted altitude layer must be impossible inside the replacement-only window.");
            Assert.IsTrue(relaxed,
                "Exact accepted altitude layers should become advisory when they would reject a continuous legal edit.");
            Assert.AreEqual(0f, flexible, 0.25f,
                "Designer authoring should keep the legal replacement and balance the continuous rebuilt lap.");
        }

        [Test]
        public void TrackEditorMeasuredClosureRefinement_UsesLegalRoadWithoutRelaxingWeld()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                RoadWidth = 20f,
                MinCurveRadius = 50f,
                MaxCurveRadius = 1200f,
                MaxClosureCurveRadius = 2400f,
                MaxStraightLength = 10000f,
                MaxTrackLength = 100000f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                DesignSpeedMps = 100f,
                Gravity = 9.81f,
                MaxCurvatureInducedG = 15f,
                MaxVerticalCurvatureRate = 0.001f
            };
            var plan = new TopologyPlan();
            for (int side = 0; side < 4; side++)
            {
                plan.Defs.Add(new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    Length = 1000f,
                    MinimumLength = 40f,
                    Width = 20f,
                    DebugName = $"ClosureCarrier_{side}"
                });
                plan.Defs.Add(new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.BankedCurve,
                    Length = SectionFrameBuilders.EasedArcLength(90f, 300f),
                    Radius = 300f,
                    TurnAngle = 90f,
                    Direction = SectionTurnDirection.Right,
                    Width = 20f,
                    DebugName = $"ClosureCurve_{side}"
                });
            }

            var originalLengths = new List<float>();
            foreach (TrackMacroSectionDefinition definition in plan.Defs)
                originalLengths.Add(definition.Length);
            Assert.IsTrue(TrackTopologyPlanner.TryRefineAuthoringBuiltClosure(
                resolved, plan, new Vector3(16.4f, 10.8f, 0f), out string summary));

            bool changedClosureVariable = false;
            for (int i = 0; i < plan.Defs.Count; i++)
                changedClosureVariable |=
                    Mathf.Abs(originalLengths[i] - plan.Defs[i].Length) > 0.001f;
            Assert.IsTrue(changedClosureVariable,
                "Measured planar drift should be absorbed by legal closure variables.");
            float elevation = 0f;
            foreach (TrackMacroSectionDefinition definition in plan.Defs)
                elevation += definition.ElevationChange;
            Assert.AreEqual(-10.8f, elevation, 0.01f,
                "Measured vertical drift should be paid back on ordinary road.");
            Assert.That(summary, Does.Contain("measured-weld refinement"));
        }

        [Test]
        public void TrackEditorUndoHistory_RemembersOnlyLatestAcceptedDesign()
        {
            var owner = new GameObject("TrackEditorUndoFixture");
            try
            {
                TrackGenerator generator = owner.AddComponent<TrackGenerator>();
                TrackEditor editor = owner.AddComponent<TrackEditor>();
                editor.Bind(generator);

                SetPrivateField(generator, "lastAcceptedRecipe", new GenerationRecipeV1
                {
                    ExpectedLayoutHash = "accepted-layout-one"
                });

                editor.RememberLastAppliedEdit("recipe-one", "Curve to Wallride");
                Assert.IsTrue(editor.CanUndoLastAppliedEdit);
                Assert.AreEqual("recipe-one", editor.LastAppliedEditUndoRecipe);

                SetPrivateField(generator, "lastAcceptedRecipe", new GenerationRecipeV1
                {
                    ExpectedLayoutHash = "fresh-generated-layout"
                });
                Assert.IsFalse(editor.CanUndoLastAppliedEdit,
                    "History from a prior accepted layout must not appear on a freshly generated or loaded track.");

                editor.RememberLastAppliedEdit("recipe-two", "Wallride to Corkscrew");
                Assert.IsTrue(editor.CanUndoLastAppliedEdit);
                Assert.AreEqual("recipe-two", editor.LastAppliedEditUndoRecipe);
                Assert.AreEqual("Wallride to Corkscrew", editor.LastAppliedEditUndoLabel);

                editor.ClearLastAppliedEditUndo();
                Assert.IsFalse(editor.CanUndoLastAppliedEdit);
                Assert.AreEqual(string.Empty, editor.LastAppliedEditUndoRecipe);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void TrackEditorAuthoringVerticalRecovery_RespectsHighSpeedCurvatureEnvelope()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                RoadWidth = 100f,
                DefaultRecoveryLength = 540f,
                DesignSpeedMps = 361.1111f,
                Gravity = 9.81f,
                MaxCurvatureInducedG = 15f,
                MaxVerticalCurvatureRate = 0.000001f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                MeshMetersPerRing = 5f,
                FeatureMetersPerRing = 5f,
                MaxRingsPerSection = 1024,
                MaxRingFacetAngle = 15f,
                MaxRollRateDegPerMeter = 1f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RotationalEvent,
                    Length = 2038.4f,
                    Width = 100f,
                    ElevationChange = 223.4f,
                    PatternId = "Immelmann_Test",
                    DebugName = "Immelmann_Test"
                }
            };

            Assert.IsTrue(TrackTopologyPlanner.EnsureAuthoringVerticalRecovery(
                resolved, definitions, out float recoveryLength, out _));

            TrackMacroSectionDefinition recovery = definitions[1];
            TrackConnectionFrame entry = TrackConnectionFrame.Origin(100f);
            TrackConnectionFrame[] frames = SectionFrameBuilders.BuildStraight(
                entry, recovery, FrameBuildContext.From(resolved));
            var layout = new GeneratedTrackLayout();
            layout.Sections.Add(new GeneratedTrackSection
            {
                Definition = recovery,
                StartFrame = entry,
                EndFrame = frames[frames.Length - 1],
                SubdivisionFrames = frames
            });
            var issues = new List<ValidationIssue>();

            TrackValidators.ValidateTransitionRates(layout, resolved, issues);

            Assert.Greater(recoveryLength, 1500f,
                "A 223m high-speed return needs more road than the grade-only estimate.");
            Assert.IsFalse(issues.Exists(issue =>
                    issue.Reason == GenerationFailureReason.TransitionRateExceeded),
                issues.Count > 0 ? issues[0].Message : "Unexpected transition failure.");
        }

        [Test]
        public void TrackEditorProximity_SameFeatureApproachAndRecoveryAreNotUnrelated()
        {
            const string pattern = "Spiral_OwnedFeature";
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Spiral,
                    PatternId = pattern,
                    DebugName = "Spiral_3rev_Up_Left"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    PatternId = pattern,
                    DebugName = "RecoveryStraight_Drop91m"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    PatternId = "AnotherFeature",
                    DebugName = "OtherRecovery"
                }
            };

            Assert.IsTrue(TrackTopologyPlanner.IsSameFeaturePatternProximity(
                definitions, 0, 1),
                "The cheap planner must match the final 3D validator's pattern ownership rule.");
            Assert.IsFalse(TrackTopologyPlanner.IsSameFeaturePatternProximity(
                definitions, 0, 2));
        }

        [Test]
        public void TrackEditorAuthoringClearance_TranslatesFeatureWithoutMovingItsWelds()
        {
            const string pattern = "Spiral_ClearanceBridge";
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                VerticalClearance = 100f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.LandingRamp,
                    PatternId = "Jump_Unrelated",
                    DebugName = "LandingRamp_H114m"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    Length = 1000f,
                    PatternId = pattern,
                    DebugName = "SpiralApproach"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Spiral,
                    Length = 3000f,
                    PatternId = pattern,
                    DebugName = "Spiral_3rev_Up_Left"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 1000f,
                    ElevationChange = -91f,
                    PatternId = pattern,
                    DebugName = "RecoveryStraight_Drop91m"
                }
            };
            float netBefore = definitions[1].ElevationChange +
                              definitions[3].ElevationChange;

            Assert.IsTrue(TrackTopologyPlanner.TryApplyAuthoringVerticalClearance(
                resolved, definitions, 0, 0f, 2, -25f, out string note), note);

            Assert.AreEqual(-75f, definitions[1].ElevationChange, 0.01f,
                "A feature already 25 m below the unrelated road needs exactly 75 m more " +
                "separation to satisfy the 100 m hard rule; authoring must not add a hidden pad.");
            Assert.AreEqual(netBefore,
                definitions[1].ElevationChange + definitions[3].ElevationChange, 0.01f,
                "Equal/opposite carriers must preserve the complete lap elevation endpoint.");
            Assert.That(definitions[1].DebugName, Does.Contain("AuthoringClearanceDrop"));
            Assert.That(definitions[3].DebugName, Does.Contain("AuthoringClearanceClimb"));
        }

        [Test]
        public void TrackEditorAuthoringClearance_UsesBroaderRouteWindowWhenFeatureMouthCannotCarryIt()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                VerticalClearance = 100f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    Length = 800f,
                    RoadId = 0,
                    DebugName = "RoadBeforeJump"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.JumpRamp,
                    RoadId = 0,
                    DebugName = "JumpRamp_H47m_7.2deg"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 800f,
                    RoadId = 0,
                    DebugName = "RoadAfterJump"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.SCurve,
                    RoadId = 0,
                    DebugName = "ClosureSBend_60deg"
                }
            };

            Assert.IsTrue(TrackTopologyPlanner.TryApplyAuthoringRouteClearance(
                resolved, definitions, 1, 0f, 3, 49f, out string note), note);

            Assert.Less(definitions[0].ElevationChange, -49f);
            Assert.Greater(definitions[2].ElevationChange, 49f);
            Assert.AreEqual(0f, definitions[0].ElevationChange +
                                definitions[2].ElevationChange, 0.01f,
                "The broad authoring window must return the lap to its exact prior elevation.");
            Assert.That(definitions[0].DebugName, Does.Contain("AuthoringWindowDrop"));
            Assert.That(definitions[2].DebugName, Does.Contain("AuthoringWindowClimb"));
        }

        [Test]
        public void TrackEditorAuthoringClearance_OrdinaryCurvesCanCarryBroaderRouteWindow()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                VerticalClearance = 100f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Chicane,
                    Length = 1200f,
                    RoadId = 0,
                    DebugName = "ChicaneBefore"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Loop,
                    Length = 1800f,
                    RoadId = 0,
                    DebugName = "LoopTarget"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.BankedCurve,
                    Length = 1200f,
                    RoadId = 0,
                    DebugName = "CurveAfter"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 600f,
                    RoadId = 0,
                    DebugName = "UnrelatedRoad"
                }
            };

            Assert.IsTrue(TrackTopologyPlanner.TryApplyAuthoringRouteClearance(
                resolved, definitions, 1, 0f, 3, 49f, out string note), note);
            Assert.AreEqual(-51f, definitions[0].ElevationChange, 0.01f);
            Assert.AreEqual(51f, definitions[2].ElevationChange, 0.01f);
            Assert.AreEqual(0f, definitions[0].ElevationChange +
                                definitions[2].ElevationChange, 0.01f);
        }

        [Test]
        public void TrackEditorAreaOfImpact_ReconcilesCompleteCanonicalLapElevation()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f
            };
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RotationalEvent,
                    Length = 2000f,
                    ElevationChange = 100f,
                    RoadId = 0,
                    DebugName = "Immelmann"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 1000f,
                    ElevationChange = -124f,
                    RoadId = 0,
                    DebugName = "TrackEditorVerticalRecovery_Drop124m"
                }
            };

            float residual = TrackTopologyPlanner.ReconcileAuthoringNetElevation(
                resolved, definitions, out float applied);

            Assert.AreEqual(0f, residual, 0.01f);
            Assert.AreEqual(24f, applied, 0.01f);
            Assert.AreEqual(-100f, definitions[1].ElevationChange, 0.01f,
                "The existing recovery should be softened before adding grade elsewhere.");
        }

        [Test]
        public void VerticalProfilePlanner_OpenBoundaryOrdinaryRoadStillGetsContinuousProfile()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                MaxCurvatureInducedG = 4f,
                Gravity = 9.81f,
                DesignSpeedMps = 100f,
                MaxVerticalCurvatureRate = 0.00002f,
                PitchTransitionLength = 80f
            };
            var frames = new TrackConnectionFrame[5];
            float[] pitches = { 0f, 12f, -10f, 9f, 0f };
            for (int i = 0; i < frames.Length; i++)
            {
                float pitch = pitches[i] * Mathf.Deg2Rad;
                frames[i] = new TrackConnectionFrame
                {
                    Position = new Vector3(0f, 0f, i * 200f),
                    Forward = new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)),
                    Right = Vector3.right,
                    Up = Vector3.up,
                    Width = 20f,
                    ArcLength = i * 200f,
                    VerticalCurvatureRate = 1f
                };
            }
            var section = new GeneratedTrackSection
            {
                Definition = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Chicane,
                    Length = 800f,
                    Width = 20f,
                    RoadId = 0,
                    DebugName = "OpenChicane"
                },
                RoadId = 0,
                OpenStart = true,
                OpenEnd = true,
                SubdivisionFrames = frames,
                StartFrame = frames[0],
                EndFrame = frames[frames.Length - 1]
            };

            Assert.IsTrue(VerticalProfilePlanner.TryApply(
                new List<GeneratedTrackSection> { section }, resolved, out string failure), failure);
            foreach (TrackConnectionFrame frame in section.SubdivisionFrames)
                Assert.LessOrEqual(Mathf.Abs(frame.VerticalCurvatureRate),
                    resolved.MaxVerticalCurvatureRate * 0.91f + 1e-8f);
        }

        [Test]
        public void TrackRetopology_FinalVerticalMetricsComeFromFinishedGeometry()
        {
            var frames = new TrackConnectionFrame[5];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = new TrackConnectionFrame
                {
                    Position = new Vector3(0f, 0f, i * 100f),
                    Forward = Vector3.forward,
                    Right = Vector3.right,
                    Up = Vector3.up,
                    Width = 20f,
                    ArcLength = i * 100f,
                    VerticalCurvature = 0.01f,
                    VerticalCurvatureRate = 0.01f
                };
            }
            var section = new GeneratedTrackSection
            {
                Definition = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Chicane,
                    DebugName = "FinishedFlatChicane"
                },
                SubdivisionFrames = frames,
                StartFrame = frames[0],
                EndFrame = frames[frames.Length - 1]
            };

            TrackRetopology.RefreshVerticalMetrics(
                new List<GeneratedTrackSection> { section });

            foreach (TrackConnectionFrame frame in section.SubdivisionFrames)
            {
                Assert.AreEqual(0f, frame.VerticalCurvature, 1e-7f);
                Assert.AreEqual(0f, frame.VerticalCurvatureRate, 1e-7f);
            }
        }

        [Test]
        public void TrackEditorFocusedElevationBaseline_PreservesUnchangedLayersWithoutTouchingReplacement()
        {
            var accepted = new List<GeneratedTrackSection>
            {
                new GeneratedTrackSection
                {
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.Straight,
                        QuarterIndex = 1,
                        RoadId = 0,
                        DebugName = "Straight_03_Drop275m",
                        ElevationChange = -275f
                    }
                },
                new GeneratedTrackSection
                {
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.BankedHairpin,
                        PatternId = "hairpin:slot-q2-r0-turn-002-n180",
                        TopologySlotId = "slot-q2-r0-turn-002-n180",
                        DebugName = "Hairpin",
                        ElevationChange = 0f
                    }
                },
                new GeneratedTrackSection
                {
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.RecoveryStraight,
                        PatternId = "later-stable-feature",
                        DebugName = "RecoveryStraight_Drop41m",
                        ElevationChange = -41f
                    }
                }
            };
            IReadOnlyList<AuthoringElevationBaselineEntry> baseline =
                TrackTopologyPlanner.CaptureAuthoringElevationBaseline(accepted);
            var rebuilt = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    QuarterIndex = 1,
                    RoadId = 0,
                    DebugName = "Straight_03"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RotationalEvent,
                    PatternId = "replacement:slot-q2-r0-turn-002-n180:Immelmann",
                    TopologySlotId = "slot-q2-r0-turn-002-n180",
                    DebugName = "Immelmann",
                    ElevationChange = 125f
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    PatternId = "later-stable-feature",
                    DebugName = "RecoveryStraight",
                    ElevationChange = 0f
                }
            };

            int restored = TrackTopologyPlanner.RestoreAuthoringElevationBaseline(
                rebuilt, baseline);

            Assert.AreEqual(2, restored);
            Assert.AreEqual(-275f, rebuilt[0].ElevationChange, 0.01f,
                "An unchanged distant road must keep its accepted altitude layer.");
            Assert.AreEqual(125f, rebuilt[1].ElevationChange, 0.01f,
                "A replacement owns its authored vertical geometry and must not inherit the old turn.");
            Assert.AreEqual(-41f, rebuilt[2].ElevationChange, 0.01f);
        }

        [Test]
        public void TrackEditorFocusedElevationAnchors_ReconnectReplacementWithoutMovingAcceptedFeatures()
        {
            TrackMacroSectionDefinition Stable(string pattern, string name, float elevation = 0f) =>
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    PatternId = pattern,
                    RoadId = 0,
                    Length = 2000f,
                    DebugName = name,
                    ElevationChange = elevation
                };

            var accepted = new List<GeneratedTrackSection>
            {
                new GeneratedTrackSection
                {
                    Definition = Stable("stable-entry", "StableEntry"),
                    StartFrame = new TrackConnectionFrame { Position = Vector3.zero },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.zero }
                },
                new GeneratedTrackSection
                {
                    Definition = Stable("old-hairpin", "Hairpin"),
                    StartFrame = new TrackConnectionFrame { Position = Vector3.zero },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.zero }
                },
                new GeneratedTrackSection
                {
                    Definition = Stable("stable-loop", "Loop", 0f),
                    StartFrame = new TrackConnectionFrame { Position = Vector3.up * 100f },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.up * 100f }
                },
                new GeneratedTrackSection
                {
                    Definition = Stable("stable-finish", "FinishRecovery", -100f),
                    StartFrame = new TrackConnectionFrame { Position = Vector3.up * 100f },
                    EndFrame = new TrackConnectionFrame { Position = Vector3.zero }
                }
            };
            IReadOnlyList<AuthoringElevationBaselineEntry> baseline =
                TrackTopologyPlanner.CaptureAuthoringElevationBaseline(accepted);
            var rebuilt = new List<TrackMacroSectionDefinition>
            {
                Stable("stable-entry", "StableEntry"),
                Stable("new-immelmann-connector", "ImmelmannOwnedRecovery"),
                Stable("stable-loop", "Loop"),
                Stable("stable-finish", "FinishRecovery")
            };
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                RoadWidth = 20f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                MeshMetersPerRing = 20f,
                FeatureMetersPerRing = 10f,
                MaxRingFacetAngle = 5f,
                MaxRingsPerSection = 2048
            };

            Assert.AreEqual(3, TrackTopologyPlanner.RestoreAuthoringElevationBaseline(
                rebuilt, baseline));
            float residual = TrackTopologyPlanner.ReconcileAuthoringElevationAnchors(
                resolved, rebuilt, baseline, out float applied);

            Assert.AreEqual(0f, residual, 0.25f);
            Assert.AreEqual(100f, applied, 0.25f);
            Assert.AreEqual(100f, rebuilt[1].ElevationChange, 0.25f,
                "Only the replacement-owned corridor should reconnect to the next accepted altitude anchor.");
            Assert.AreEqual(0f, rebuilt[2].ElevationChange, 0.01f,
                "The accepted feature must retain its own vertical design.");
            Assert.AreEqual(-100f, rebuilt[3].ElevationChange, 0.01f,
                "The accepted finish recovery must not be rewritten.");
            Assert.AreEqual(0f,
                TrackTopologyPlanner.MeasureCanonicalBuiltElevation(resolved, rebuilt),
                0.25f);
        }

        [Test]
        public void TopologyOverride_AppliedAreaWindowResolvesByStampedReplacementIdentity()
        {
            var request = new TopologySlotOverride
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                StructuralAnchor = "1|0|1|-180|2|2",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Immelmann
            };
            var rebuiltAfterCrossQuarterImpact = new TopologySlotRecord
            {
                TopologySlotId = "slot-q3-r0-turn-000-n180",
                QuarterIndex = 2,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.Hairpin,
                CurrentRealization = SemanticElementId.Immelmann,
                OverrideState = TopologySlotOverrideState.Applied,
                PatternId = "track-editor:slot-q2-r0-turn-002-n180:Immelmann"
            };

            Assert.IsTrue(TrackTopologyPlanner.TryResolveAppliedReplacementSlot(
                new[] { rebuiltAfterCrossQuarterImpact }, request,
                out TopologySlotRecord resolved));
            Assert.AreSame(rebuiltAfterCrossQuarterImpact, resolved,
                "Finished slot renumbering must not lose the exact replacement geometry.");
        }

        [Test]
        public void TrackGenerator_AcceptedAreaWindowNormalizesByStampedReplacementIdentity()
        {
            var request = new TopologySlotOverride
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                StructuralAnchor = "1|0|1|-180|2|2",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Immelmann,
                ImpactMembers = new List<TopologyImpactMember>
                {
                    new TopologyImpactMember
                    {
                        TopologySlotId = "slot-q3-r0-neutral-000-z000",
                        OriginalRealization = SemanticElementId.InlineCorkscrew
                    }
                }
            };
            var acceptedReplacement = new TopologySlotRecord
            {
                TopologySlotId = "slot-q3-r0-turn-000-n180",
                CanonicalOrder = 6,
                QuarterIndex = 2,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.Hairpin,
                CurrentRealization = SemanticElementId.Immelmann,
                OverrideState = TopologySlotOverrideState.Applied,
                PatternId = "track-editor:slot-q2-r0-turn-002-n180:Immelmann"
            };

            Assert.IsTrue(TrackGenerator.NormalizeAcceptedTopologyOverrides(
                new[] { request }, new[] { acceptedReplacement }, out string error), error);
            Assert.AreEqual(acceptedReplacement.TopologySlotId, request.TopologySlotId,
                "A valid authored candidate must not be rejected after candidate selection.");
            Assert.AreEqual(acceptedReplacement.CanonicalOrder, request.CanonicalOrder);
        }

        [Test]
        public void TrackEditorFocusedSweep_EvaluatesOnlyAcceptedAttemptIndex()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                settings.Generation.SelectionMode = CandidateSelectionMode.FirstValid;
                settings.Generation.MaxAttempts = 30;
                settings.Quarters.MinimumDualQuarterCount = 0;
                settings.Quarters.MaximumDualQuarterCount = 0;
                settings.Features.MinFeatureGroups = 0;
                settings.Features.MaxFeatureGroups = 1;
                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(20260826);
                const int acceptedAttempt = 7;

                TrackGenerationResult result = new TrackGenerationPipeline().Run(
                    resolved, TrackSeed.CreateNew(20260826), streams, null,
                    new TrackGenerationPipelineOptions { OnlyAttemptIndex = acceptedAttempt });

                Assert.AreEqual(1, result.AttemptsEvaluated,
                    "Track Editor must rebuild one accepted design, not search unrelated routes.");
                if (result.Success)
                    Assert.AreEqual(acceptedAttempt, result.SelectedAttemptIndex);
                else
                {
                    Assert.IsNotEmpty(result.Report.Failures);
                    Assert.IsTrue(result.Report.Failures.TrueForAll(failure =>
                            failure.AttemptIndex == acceptedAttempt),
                        "Focused diagnostics must retain the absolute source-attempt identity.");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ExactRecipe_DesignerAuthoringBaseline_SurvivesSaveLoad()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(1942698564);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(-1942698564), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);
                string baseIdentity = recipe.BaseRecipeHash;

                recipe.DesignerAuthoredVariant = true;
                recipe.AuthoringBaselineAttemptIndex = 37;
                recipe.AuthoringBaselineLapLengthMeters = 58840f;
                recipe.AuthoringElevationBaseline.Add(new AuthoringElevationBaselineEntry
                {
                    MatchKey = "stable|slot-q2-r0-neutral-001-z000|0",
                    ElevationChange = -41f,
                    HillHeight = 18f,
                    StartElevation = 125f,
                    EndElevation = 84f,
                    RoadId = 0,
                    CanonicalLapEnd = true
                });
                recipe.RecipeRevision = 1;
                recipe.TopologySlotOverrides.Add(new TopologySlotOverride
                {
                    TopologySlotId = "slot-q2-r0-turn-002-n180",
                    StructuralAnchor = "1|0|1|-180|2|2",
                    OriginalRealization = SemanticElementId.Hairpin,
                    RequestedRealization = SemanticElementId.WideTurnaround
                });
                recipe.RefreshHashes();

                GenerationRecipeV1 loaded = GenerationRecipeV1.Deserialize(
                    GenerationRecipeV1.Serialize(recipe, false), out string error);

                Assert.NotNull(loaded, error);
                Assert.IsTrue(loaded.DesignerAuthoredVariant);
                Assert.AreEqual(37, loaded.AuthoringBaselineAttemptIndex);
                Assert.AreEqual(58840f, loaded.AuthoringBaselineLapLengthMeters, 0.01f);
                Assert.AreEqual(1, loaded.AuthoringElevationBaseline.Count);
                Assert.AreEqual("stable|slot-q2-r0-neutral-001-z000|0",
                    loaded.AuthoringElevationBaseline[0].MatchKey);
                Assert.AreEqual(-41f,
                    loaded.AuthoringElevationBaseline[0].ElevationChange, 0.01f);
                Assert.IsTrue(loaded.AuthoringElevationBaseline[0].CanonicalLapEnd);
                Assert.AreEqual(baseIdentity, loaded.BaseRecipeHash,
                    "Authoring metadata belongs to the exact variant, not its shareable base identity.");
                Assert.IsTrue(loaded.ValidateFor(config, RecipeReplayMode.Strict,
                    out string validationError), validationError);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ExactRecipe_PreAuthoringMetadataHash_RemainsCompatible()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(20260827);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(20260827), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);

                GenerationRecipeV1 legacyHashCopy = recipe.Clone();
                legacyHashCopy.RecipeHash = "";
                legacyHashCopy.BaseRecipeHash = "";
                legacyHashCopy.ExpectedLayoutHash = "";
                string legacyJson = JsonUtility.ToJson(legacyHashCopy, false);
                legacyJson = RemovePrimitiveFieldForLegacyFixture(
                    legacyJson, nameof(GenerationRecipeV1.DesignerAuthoredVariant));
                legacyJson = RemovePrimitiveFieldForLegacyFixture(
                    legacyJson, nameof(GenerationRecipeV1.AuthoringBaselineAttemptIndex));
                legacyJson = RemovePrimitiveFieldForLegacyFixture(
                    legacyJson, nameof(GenerationRecipeV1.AuthoringBaselineLapLengthMeters));
                recipe.RecipeHash = GenerationHashUtility.Sha256Hex(legacyJson);

                Assert.IsTrue(recipe.ValidateFor(
                    config, RecipeReplayMode.Strict, out string error), error);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ExactRecipe_PreAuthoringElevationBaselineHash_RemainsCompatible()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(20260828);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(20260828), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);

                GenerationRecipeV1 legacyHashCopy = recipe.Clone();
                legacyHashCopy.RecipeHash = "";
                legacyHashCopy.BaseRecipeHash = "";
                legacyHashCopy.ExpectedLayoutHash = "";
                string legacyJson = JsonUtility.ToJson(legacyHashCopy, false);
                legacyJson = RemoveArrayFieldForLegacyFixture(legacyJson,
                    nameof(GenerationRecipeV1.AuthoringElevationBaseline));
                recipe.RecipeHash = GenerationHashUtility.Sha256Hex(legacyJson);

                Assert.IsTrue(recipe.ValidateFor(
                    config, RecipeReplayMode.Strict, out string error), error);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TrackGenerator_ExportAcceptedRecipe_UpgradesLegacyAuthoredUndoSnapshot()
        {
            var host = new GameObject("TrackGenerator_UndoSnapshotUpgrade_Test");
            try
            {
                TrackGenerator generator = host.AddComponent<TrackGenerator>();
                GenerationRecipeV1 recipe = new GenerationRecipeV1
                {
                    DesignerAuthoredVariant = true,
                    ExpectedLayoutHash = "accepted-layout"
                };
                recipe.RefreshHashes();
                SetPrivateField(generator, "lastAcceptedRecipe", recipe);
                SetPrivateField(generator, "<CurrentLayout>k__BackingField",
                    new GeneratedTrackLayout
                    {
                        Sections = new List<GeneratedTrackSection>
                        {
                            new GeneratedTrackSection
                            {
                                Definition = new TrackMacroSectionDefinition
                                {
                                    SectionType = TrackMacroSectionType.RecoveryStraight,
                                    QuarterIndex = 3,
                                    RoadId = 0,
                                    PatternId = "accepted-recovery",
                                    ElevationChange = -72f,
                                    HillHeight = 15f
                                },
                                StartFrame = new TrackConnectionFrame
                                    { Position = Vector3.up * 172f },
                                EndFrame = new TrackConnectionFrame
                                    { Position = Vector3.up * 100f }
                            }
                        }
                    });

                GenerationRecipeV1 exported = GenerationRecipeV1.Deserialize(
                    generator.ExportGenerationRecipe(preferLastAccepted: true),
                    out string error);

                Assert.NotNull(exported, error);
                Assert.AreEqual(1, exported.AuthoringElevationBaseline.Count,
                    "The accepted visible layout was not captured into the Undo snapshot.");
                Assert.AreEqual(-72f,
                    exported.AuthoringElevationBaseline[0].ElevationChange, 0.01f);
                Assert.AreEqual("accepted-layout", exported.ExpectedLayoutHash,
                    "Upgrading Undo history must retain the accepted canonical identity.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void FailedAttempt_DoesNotHideAcceptedTrackEditorTopology()
        {
            var host = new GameObject("TrackGenerator_FailedEditRollback_Test");
            try
            {
                TrackGenerator generator = host.AddComponent<TrackGenerator>();
                var acceptedSlots = new List<TopologySlotRecord>
                {
                    new TopologySlotRecord
                    {
                        TopologySlotId = "slot-q1-r0-neutral-002-z000",
                        CanonicalOrder = 2,
                        RouteOrder = 2,
                        QuarterIndex = 0,
                        RoadId = 0,
                        CurrentRealization = SemanticElementId.JumpGap
                    }
                };
                var failedReport = new TrackGenerationReport
                {
                    Success = false,
                    TopologySlots = new List<TopologySlotRecord>()
                };

                SetPrivateField(generator, "lastAcceptedTopologySlots", acceptedSlots);
                SetPrivateField(generator, "lastReport", failedReport);

                Assert.IsFalse(generator.LastReport.Success,
                    "Diagnostics should still expose the rejected rebuild.");
                Assert.NotNull(generator.EditableTopologySlots);
                Assert.AreEqual(1, generator.EditableTopologySlots.Count,
                    "The editor must remain bound to the accepted visible track.");
                Assert.AreEqual("slot-q1-r0-neutral-002-z000",
                    generator.EditableTopologySlots[0].TopologySlotId);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ExactRecipe_ReplaysTwice_WithIdenticalManifestIdentity()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                // Keep this gate intentionally small and self-consistent. Balanced
                // normally requires one dual-road quarter, whose jump gates alone do
                // not fit the fast fixture's 60-second lap budget. Dual-quarter
                // behavior has its own focused tests; exact replay only needs one
                // known-valid deterministic layout.
                TrackDesignerSettings settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                settings.Generation.SelectionMode = CandidateSelectionMode.FirstValid;
                settings.Generation.MaxAttempts = Mathf.Max(settings.Generation.MaxAttempts, 80);
                settings.Quarters.MinimumDualQuarterCount = 0;
                settings.Quarters.MaximumDualQuarterCount = 0;
                settings.Features.MinFeatureGroups = 0;
                settings.Features.MaxFeatureGroups = 2;
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(4321);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(4321), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);

                ReplayVerificationResult verified = GenerationReplayVerifier.Verify(recipe, config, 2);
                Assert.IsTrue(verified.Success, verified.ToString());
                Assert.AreEqual(2, verified.CompletedRuns);
                Assert.IsNotEmpty(verified.VerifiedLayoutHash);
                Assert.AreEqual(verified.Runs[0].SelectedAttemptIndex, verified.Runs[1].SelectedAttemptIndex);
                Assert.AreEqual(verified.Runs[0].SelectedCandidateIndex, verified.Runs[1].SelectedCandidateIndex);
                Assert.AreEqual(verified.Runs[0].ConnectionFrameChecksum,
                    verified.Runs[1].ConnectionFrameChecksum);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ExactRecipe_LoadedAcceptedIdentity_IsAdoptedWithoutRebuildingVisibleTrack()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            var host = new GameObject("TrackGenerator_ExactRecipeAdoption_Test");
            try
            {
                TrackGenerator generator = host.AddComponent<TrackGenerator>();
                generator.Config = config;

                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(809405242);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(809405242), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, host.transform);

                var layout = new GeneratedTrackLayout
                {
                    Sections = new List<GeneratedTrackSection>
                    {
                        new GeneratedTrackSection
                        {
                            SectionIndex = 0,
                            QuarterIndex = 0,
                            RoadId = 0,
                            Definition = new TrackMacroSectionDefinition
                            {
                                SectionType = TrackMacroSectionType.Straight,
                                SemanticElement = SemanticElementId.None,
                                Length = 500f,
                                Width = 36f,
                                QuarterIndex = 0,
                                RoadId = 0
                            },
                            StartFrame = TrackConnectionFrame.Origin(36f),
                            EndFrame = new TrackConnectionFrame
                            {
                                Position = Vector3.forward * 500f,
                                Forward = Vector3.forward,
                                Up = Vector3.up,
                                Width = 36f
                            }
                        }
                    },
                    LapLength = 500f
                };
                recipe.ExpectedLayoutHash = TrackCanonicalHasher.ComputeLayoutHash(layout);
                recipe.DesignerAuthoredVariant = true;
                recipe.AuthoringBaselineAttemptIndex = 37;
                recipe.AuthoringElevationBaseline.Add(new AuthoringElevationBaselineEntry
                {
                    MatchKey = "pre-edit-input",
                    ElevationChange = 42f
                });
                recipe.RefreshHashes();

                SetPrivateField(generator, "<CurrentLayout>k__BackingField", layout);
                SetPrivateField(generator, "lastResultManifest", new TrackResultManifest
                {
                    CanonicalLayoutHash = recipe.ExpectedLayoutHash
                });
                SetPrivateField(generator, "lastAcceptedRecipe", recipe.Clone());

                Assert.IsTrue(generator.TryImportGenerationRecipe(
                    GenerationRecipeV1.Serialize(recipe, false), RecipeReplayMode.Strict,
                    out string importError), importError);
                int revisionBeforeReplay = generator.GenerationRevision;

                Assert.IsTrue(generator.RegenerateExactRecipe(out string replayError), replayError);
                Assert.AreEqual(revisionBeforeReplay + 1, generator.GenerationRevision,
                    "Adopting the accepted recipe should publish exactly one UI result revision.");
                Assert.IsTrue(generator.LastReport.Success);
                Assert.AreEqual("Exact layout already accepted", generator.LastReport.AcceptedPass);
                Assert.AreEqual(recipe.ExpectedLayoutHash,
                    generator.LastAcceptedRecipe.ExpectedLayoutHash);
                Assert.AreEqual("pre-edit-input",
                    generator.LastAcceptedRecipe.AuthoringElevationBaseline[0].MatchKey,
                    "Adopting the loaded recipe must retain its deterministic pre-edit input.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ExactRecipe_TopologyOverrides_SurviveSaveLoad_WithStableVariantIdentity()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(91827);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(91827), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);
                string baseIdentity = recipe.BaseRecipeHash;
                string uneditedIdentity = recipe.RecipeHash;

                recipe.RecipeRevision = 2;
                recipe.TopologySlotOverrides.Add(new TopologySlotOverride
                {
                    TopologySlotId = "slot-q2-r0-neutral-001-z000",
                    CanonicalOrder = 7,
                    OriginalRealization = SemanticElementId.InlineCorkscrew,
                    RequestedRealization = SemanticElementId.DoubleCorkscrew,
                    MaximumReplanScope = LocalReplanScope.OwnedConnectors,
                    Parameters = new List<FeatureParameterOverride>
                    {
                        new FeatureParameterOverride { Name = "radius", Value = 320f },
                        new FeatureParameterOverride { Name = "height", Value = 110f }
                    }
                });
                recipe.TopologySlotOverrides.Add(new TopologySlotOverride
                {
                    TopologySlotId = "slot-q1-r0-turn-003-n180",
                    CanonicalOrder = 3,
                    StructuralAnchor = "0|0|1|-180|2|3",
                    OriginalRealization = SemanticElementId.Hairpin,
                    RequestedRealization = SemanticElementId.WallrideTurn,
                    MaximumReplanScope = LocalReplanScope.TurnComplex
                });
                recipe.ExpectedLayoutHash = "";
                recipe.RefreshHashes();

                string json = GenerationRecipeV1.Serialize(recipe, true);
                GenerationRecipeV1 loaded = GenerationRecipeV1.Deserialize(json, out string readError);

                Assert.IsNotNull(loaded, readError);
                Assert.IsTrue(loaded.ValidateFor(config, RecipeReplayMode.Strict, out string validationError),
                    validationError);
                Assert.AreEqual(2, loaded.RecipeRevision);
                Assert.AreEqual(2, loaded.TopologySlotOverrides.Count);
                Assert.AreEqual("slot-q1-r0-turn-003-n180",
                    loaded.TopologySlotOverrides[0].TopologySlotId,
                    "Overrides should normalize into stable slot order before hashing and saving.");
                Assert.AreEqual(SemanticElementId.WallrideTurn,
                    loaded.TopologySlotOverrides[0].RequestedRealization);
                Assert.AreEqual("0|0|1|-180|2|3",
                    loaded.TopologySlotOverrides[0].StructuralAnchor);
                Assert.AreEqual(SemanticElementId.DoubleCorkscrew,
                    loaded.TopologySlotOverrides[1].RequestedRealization);
                Assert.AreEqual("height", loaded.TopologySlotOverrides[1].Parameters[0].Name,
                    "Named parameters should also normalize deterministically.");
                Assert.AreEqual(baseIdentity, loaded.BaseRecipeHash,
                    "Hand edits create an exact variant without changing the shareable base-seed identity.");
                Assert.AreNotEqual(uneditedIdentity, loaded.RecipeHash,
                    "The exact variant identity must include its topology edits.");
                Assert.AreEqual(recipe.RecipeHash, loaded.RecipeHash);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ExactRecipe_PreStructuralAnchorOverrideHash_RemainsCompatible()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(91828);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(91828), streams, settings, config,
                    new SettingsLockState(), LayoutLockMode.Unlocked, null);
                recipe.TopologySlotOverrides.Add(new TopologySlotOverride
                {
                    TopologySlotId = "slot-q2-r0-turn-002-n180",
                    CanonicalOrder = 7,
                    StructuralAnchor = "",
                    OriginalRealization = SemanticElementId.Hairpin,
                    RequestedRealization = SemanticElementId.WideTurnaround,
                    MaximumReplanScope = LocalReplanScope.OwnedConnectors
                });
                recipe.RefreshHashes();

                GenerationRecipeV1 legacyHashCopy = recipe.Clone();
                legacyHashCopy.RecipeHash = "";
                legacyHashCopy.BaseRecipeHash = "";
                legacyHashCopy.ExpectedLayoutHash = "";
                string legacyJson = JsonUtility.ToJson(legacyHashCopy, false)
                    .Replace(",\"StructuralAnchor\":\"\"", "");
                recipe.RecipeHash = GenerationHashUtility.Sha256Hex(legacyJson);

                Assert.IsTrue(recipe.ValidateFor(
                    config, RecipeReplayMode.Strict, out string error), error);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ConnectorShadow_DirectWeldWithLink_IsOpportunity_NotMismatch()
        {
            var decisions = new List<TransitionDecision>
            {
                new TransitionDecision
                {
                    Kind = TransitionKind.DirectWeld,
                    From = "A", To = "B", AvailableLength = 80f,
                    ConnectorSectionIndices = new List<int> { 4 },
                    ActualConnectorBehaviors = new List<ConnectorBehavior>
                        { ConnectorBehavior.BlendNeighbours }
                }
            };

            List<ConnectorShadowRecord> records = ConnectorShadowAudit.Compare(decisions, out var summary);
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(ConnectorShadowStatus.DirectWeldOpportunity, records[0].Status);
            Assert.IsFalse(records[0].IsMismatch);
            Assert.AreEqual(1, summary.OpportunityCount);
            Assert.AreEqual(0, summary.MismatchCount);
        }

        [Test]
        public void ConnectorShadow_RejectedAcceptedBoundary_IsBlockingMismatch()
        {
            var decisions = new List<TransitionDecision>
            {
                new TransitionDecision
                {
                    Kind = TransitionKind.Rejected,
                    From = "A", To = "B", AvailableLength = 20f, MinBlendLength = 120f,
                    ConnectorSectionIndices = new List<int> { 8 },
                    ActualConnectorBehaviors = new List<ConnectorBehavior>
                        { ConnectorBehavior.PrepareNext }
                }
            };

            List<ConnectorShadowRecord> records = ConnectorShadowAudit.Compare(decisions, out var summary);
            Assert.AreEqual(ConnectorShadowStatus.AcceptedRejectedBoundary, records[0].Status);
            Assert.IsTrue(records[0].IsMismatch);
            Assert.IsTrue(summary.HasBlockingMismatch);
            Assert.AreEqual(1, summary.RejectedBoundaryCount);
        }

        [Test]
        public void TopologySlots_IgnoreConnectorSubdivision_AndRealizationName()
        {
            List<TrackMacroSectionDefinition> baseline = SlotFixture(
                connectorCount: 1, secondRealization: SemanticElementId.Hairpin, secondPattern: "Hairpin_7");
            List<TrackMacroSectionDefinition> rebuilt = SlotFixture(
                connectorCount: 3, secondRealization: SemanticElementId.WallrideTurn, secondPattern: "Wallride_99");

            List<TopologySlotRecord> first = TopologySlotCatalog.Assign(baseline);
            List<TopologySlotRecord> second = TopologySlotCatalog.Assign(rebuilt);

            Assert.AreEqual(2, first.Count);
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].TopologySlotId, second[i].TopologySlotId);
                Assert.AreEqual(first[i].DemandType, second[i].DemandType);
                Assert.AreEqual(first[i].SignedHeadingDelta, second[i].SignedHeadingDelta, 0.001f);
            }
            Assert.AreNotEqual(first[1].OriginalRealization, second[1].OriginalRealization,
                "The stable slot must survive a different realization of the same demand.");
        }

        [Test]
        public void TopologySlots_SCurveReportsNetZeroHeading()
        {
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.SCurve,
                    QuarterIndex = 1,
                    RoadId = 0,
                    PatternId = "SCurve_1",
                    SemanticElement = SemanticElementId.SCurve,
                    TurnAngle = 59f,
                    Direction = SectionTurnDirection.Left,
                    Contract = SectionConnectionContract.Level()
                }
            };

            List<TopologySlotRecord> slots = TopologySlotCatalog.Assign(definitions);

            Assert.AreEqual(1, slots.Count);
            Assert.AreEqual(0f, slots[0].SignedHeadingDelta, 0.001f);
            StringAssert.EndsWith("-z000", slots[0].TopologySlotId);
        }

        [Test]
        public void TopologySlotNeighbours_SkipConnectors_AndStayOnTheirRoute()
        {
            List<TrackMacroSectionDefinition> definitions = SlotFixture(
                connectorCount: 2, secondRealization: SemanticElementId.Hairpin, secondPattern: "Hairpin_7");
            definitions.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Loop,
                QuarterIndex = 0,
                RoadId = 0,
                PatternId = "FullLoop_8",
                SemanticElement = SemanticElementId.VerticalLoop,
                Contract = SectionConnectionContract.Level()
            });
            definitions.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.BankedCurve,
                QuarterIndex = 0,
                RoadId = 1,
                TurnAngle = 45f,
                Direction = SectionTurnDirection.Left,
                SemanticElement = SemanticElementId.OrdinaryCurve,
                Contract = SectionConnectionContract.Level(-45f)
            });

            List<TopologySlotRecord> slots = TopologySlotCatalog.Assign(definitions);
            TopologySlotRecord middle = slots.Find(s => s.RoadId == 0 && s.RouteOrder == 1);
            Assert.NotNull(middle);
            Assert.IsTrue(TopologySlotCatalog.TryGetNeighbours(slots, middle.TopologySlotId,
                out TopologySlotRecord previous, out TopologySlotRecord next));
            Assert.AreEqual(0, previous.RouteOrder);
            Assert.AreEqual(2, next.RouteOrder);
            Assert.AreEqual(0, previous.RoadId);
            Assert.AreEqual(0, next.RoadId);
        }

        [Test]
        public void TopologyCompatibility_180DegreeTurn_OffersReversalFamilyOnly()
        {
            var slot = new TopologySlotRecord
            {
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = 180f,
                CurrentRealization = SemanticElementId.Hairpin
            };

            List<TopologySlotCompatibilityResult> results =
                TopologySlotCompatibility.Evaluate(slot, TrackConnectionFrame.Origin(48f));

            AssertStatus(results, SemanticElementId.Hairpin, TopologySlotCompatibilityStatus.Current);
            AssertStatus(results, SemanticElementId.WideTurnaround, TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.HalfHelixTurnaround, TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.Immelmann, TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.HalfLoopToCorkscrew, TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.OrdinaryCurve, TopologySlotCompatibilityStatus.Blocked);
            AssertStatus(results, SemanticElementId.DirectionalCorkscrew, TopologySlotCompatibilityStatus.Blocked);
        }

        [Test]
        public void TopologyCompatibility_90DegreeTurn_OffersCurveWallrideAndDirectionalCorkscrew()
        {
            var slot = new TopologySlotRecord
            {
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -90f,
                CurrentRealization = SemanticElementId.OrdinaryCurve
            };

            List<TopologySlotCompatibilityResult> results =
                TopologySlotCompatibility.Evaluate(slot, TrackConnectionFrame.Origin(48f));

            AssertStatus(results, SemanticElementId.OrdinaryCurve, TopologySlotCompatibilityStatus.Current);
            AssertStatus(results, SemanticElementId.WallrideTurn, TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.DirectionalCorkscrew, TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.AlternatingRadiusSequence, TopologySlotCompatibilityStatus.Blocked);
            AssertStatus(results, SemanticElementId.Hairpin, TopologySlotCompatibilityStatus.Blocked);
            AssertStatus(results, SemanticElementId.WideTurnaround, TopologySlotCompatibilityStatus.Blocked);
            AssertStatus(results, SemanticElementId.HalfHelixTurnaround, TopologySlotCompatibilityStatus.Blocked);
            AssertStatus(results, SemanticElementId.VerticalLoop, TopologySlotCompatibilityStatus.Blocked);
        }

        [Test]
        public void TopologyCompatibility_HeadingNeutralSlot_OffersAlternatingRadiusTransfer()
        {
            var slot = new TopologySlotRecord
            {
                DemandType = TopologyRole.DirectionNeutralTransfer,
                SignedHeadingDelta = 0f,
                CurrentRealization = SemanticElementId.SCurve
            };

            List<TopologySlotCompatibilityResult> results =
                TopologySlotCompatibility.Evaluate(slot, TrackConnectionFrame.Origin(48f));

            AssertStatus(results, SemanticElementId.SCurve, TopologySlotCompatibilityStatus.Current);
            AssertStatus(results, SemanticElementId.AlternatingRadiusSequence,
                TopologySlotCompatibilityStatus.PotentialFit);
            AssertStatus(results, SemanticElementId.OrdinaryCurve, TopologySlotCompatibilityStatus.Blocked);
        }

        [Test]
        public void TopologyCompatibility_EntryWindow_BlocksInversionOnBankedEntry()
        {
            var slot = new TopologySlotRecord
            {
                DemandType = TopologyRole.HeadingNeutral,
                SignedHeadingDelta = 0f,
                CurrentRealization = SemanticElementId.InlineCorkscrew
            };
            TrackConnectionFrame entry = TrackConnectionFrame.Origin(48f);
            entry.BankAngle = 20f;

            List<TopologySlotCompatibilityResult> results =
                TopologySlotCompatibility.Evaluate(slot, entry);

            AssertStatus(results, SemanticElementId.VerticalLoop, TopologySlotCompatibilityStatus.Blocked);
            AssertStatus(results, SemanticElementId.FullPipe, TopologySlotCompatibilityStatus.Blocked);
        }

        [Test]
        public void FeatureAmounts_ExactRange_RemainsExact()
        {
            var rule = new TrackFeatureRule(true, 2, 2, 1f);
            rule.Sanitize();

            Assert.AreEqual(2, rule.EffectiveMinimum);
            Assert.AreEqual(2, rule.EffectiveMaximum);
        }

        [Test]
        public void FeatureAmounts_ZeroRange_DisablesPlacementContract()
        {
            var rule = new TrackFeatureRule(true, 0, 0, 1f);
            rule.Sanitize();

            Assert.AreEqual(0, rule.EffectiveMinimum);
            Assert.AreEqual(0, rule.EffectiveMaximum);
        }

        [Test]
        public void FeatureAmounts_DisabledRule_PreservesAuthoredRangeButPlacesNone()
        {
            var rule = new TrackFeatureRule(false, 2, 4, 1f);
            rule.Sanitize();

            Assert.AreEqual(2, rule.MinimumCount,
                "Turning a feature off should not erase the designer's authored range.");
            Assert.AreEqual(4, rule.MaximumCount);
            Assert.AreEqual(0, rule.EffectiveMinimum);
            Assert.AreEqual(0, rule.EffectiveMaximum);
        }

        [Test]
        public void FeatureAmounts_ImpossibleMinimum_ReportsExplicitFailureReason()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings =
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                settings.Features.Loops.Enabled = true;
                settings.Features.Loops.MinimumCount = 1;
                settings.Features.Loops.MaximumCount = 1;
                settings.Quarters.MinimumDualQuarterCount = 0;
                settings.Sanitize();

                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                var issues = new List<ValidationIssue>();

                TrackValidators.ValidateRequiredFeatures(
                    new GeneratedTrackLayout(),
                    new TopologyPlan(),
                    resolved,
                    issues);

                ValidationIssue missingLoop = issues.Find(issue => issue.Subject == "loops");
                Assert.IsNotNull(missingLoop, "An impossible loop minimum must produce a validation issue.");
                Assert.AreEqual(GenerationFailureReason.RequiredFeatureMissing, missingLoop.Reason);
                Assert.IsTrue(missingLoop.IsError);
                Assert.AreEqual(1f, missingLoop.RequestedValue);
                Assert.AreEqual(0f, missingLoop.AchievedValue);
                StringAssert.Contains("Required at least 1 loops", missingLoop.Message);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TrackEditorAuthoredReplacement_IsNotRejectedByProceduralFeatureAmounts()
        {
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.BankedHairpin,
                SemanticElement = SemanticElementId.WideTurnaround,
                PatternId = "track-editor:slot-q3-r0-turn-002-n180:WideTurnaround"
            };
            var layout = new GeneratedTrackLayout();
            layout.Sections.Add(new GeneratedTrackSection
            {
                Definition = definition,
                PatternId = definition.PatternId
            });
            var resolved = new ResolvedTrackGenerationConfig
            {
                DesignerAuthoringMode = true,
                Hairpins = new ResolvedFeatureRule
                {
                    Enabled = true,
                    MinimumCount = 1,
                    MaximumCount = 1
                },
                WideTurnarounds = new ResolvedFeatureRule
                {
                    Enabled = true,
                    MinimumCount = 0,
                    MaximumCount = 0
                }
            };
            var issues = new List<ValidationIssue>();

            TrackValidators.ValidateRequiredFeatures(
                layout, new TopologyPlan(), resolved, issues);

            Assert.IsFalse(issues.Exists(issue => issue.Subject == "hairpins"),
                "A deliberate editor swap must not be forced to retain the procedural Hairpin minimum.");
            Assert.IsFalse(issues.Exists(issue => issue.Subject == "wide turnarounds"),
                "A deliberate editor swap must not be rejected by the procedural Wide Turnaround maximum.");
        }

        [Test]
        public void Immelmann_RolloutDoesNotConsumeACorkscrewFeatureAmount()
        {
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                SemanticElement = SemanticElementId.Immelmann,
                PatternId = "track-editor:slot-q2-r0-turn-002-n180:Immelmann",
                RotationalPhases = new List<RotationalPhaseDefinition>
                {
                    new RotationalPhaseDefinition
                    {
                        Axis = RotationalPhaseAxis.VerticalCenterline,
                        RotationUnits = 2
                    },
                    new RotationalPhaseDefinition
                    {
                        Axis = RotationalPhaseAxis.RoadRoll,
                        RotationUnits = 2
                    }
                }
            };
            var layout = new GeneratedTrackLayout();
            layout.Sections.Add(new GeneratedTrackSection
            {
                Definition = definition,
                PatternId = definition.PatternId
            });
            var resolved = new ResolvedTrackGenerationConfig
            {
                Corkscrews = new ResolvedFeatureRule
                {
                    Enabled = true,
                    MinimumCount = 0,
                    MaximumCount = 0
                }
            };
            var issues = new List<ValidationIssue>();

            TrackValidators.ValidateRequiredFeatures(
                layout, new TopologyPlan(), resolved, issues);

            Assert.IsFalse(issues.Exists(issue => issue.Subject == "corkscrews"),
                "An Immelmann rollout restores upright orientation; it is not a corkscrew feature.");
        }

        [Test]
        public void Immelmann_UsesHalfLoopRadiusRuleAfterTrackEditorIdentityStamp()
        {
            var resolved = new ResolvedTrackGenerationConfig
            {
                RotationUnitDegrees = 90f,
                AllowQuarterTurnTransitions = true,
                MinHalfLoopRadius = 300f,
                MinLoopRadius = 500f,
                MinCorkscrewRadius = 150f,
                MinDistancePerRotationUnit = 1f,
                MinSamplesPerRotationUnit = 1,
                MaxRollRateDegPerMeter = 1f,
                MaxRotationalForwardAngle = 180f,
                MaxRotationalRollAngle = 180f
            };
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                SemanticElement = SemanticElementId.Immelmann,
                Width = 100f,
                PatternId = "track-editor:slot-q2-r0-turn-002-n180:Immelmann",
                DebugName = "Immelmann_349m",
                RotationalPhases = new List<RotationalPhaseDefinition>
                {
                    new RotationalPhaseDefinition
                    {
                        Axis = RotationalPhaseAxis.VerticalCenterline,
                        RotationUnits = 2,
                        FirstHalfLength = 500f,
                        SecondHalfLength = 500f,
                        FirstHalfRadius = 349.9f,
                        SecondHalfRadius = 349.9f
                    }
                }
            };
            var layout = new GeneratedTrackLayout();
            layout.Sections.Add(new GeneratedTrackSection
            {
                Definition = definition,
                PatternId = definition.PatternId,
                StartFrame = TrackConnectionFrame.Origin(100f),
                EndFrame = TrackConnectionFrame.Origin(100f)
            });
            var issues = new List<ValidationIssue>();

            TrackValidators.ValidateRotationalEvents(layout, resolved, issues);

            Assert.IsFalse(issues.Exists(issue =>
                    issue.Reason == GenerationFailureReason.CurveRadiusViolation),
                "A legal half-loop radius must not be rejected by the larger full-loop radius rule.");
        }

        [Test]
        public void TopologyReplacementRequest_ReplacesPriorChoiceForSameStableSlot()
        {
            var owner = new GameObject("V2_RequestFixture");
            try
            {
                TrackEditor editor = owner.AddComponent<TrackEditor>();
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q1-r0-turn-002-p090",
                    CanonicalOrder = 4,
                    RouteOrder = 2,
                    QuarterIndex = 0,
                    RoadId = 0,
                    DemandType = TopologyRole.TurnRealization,
                    SignedHeadingDelta = 90f,
                    PatternId = "Hairpin_4",
                    OriginalRealization = SemanticElementId.OrdinaryCurve,
                    CurrentRealization = SemanticElementId.OrdinaryCurve,
                    LocalReplanPolicy = LocalReplanScope.OwnedConnectors
                };

                Assert.IsTrue(editor.TryRequestReplacement(slot, SemanticElementId.WallrideTurn,
                    out string firstReason), firstReason);
                Assert.IsTrue(editor.TryRequestReplacement(slot, SemanticElementId.DirectionalCorkscrew,
                    out string secondReason), secondReason);

                Assert.AreEqual(1, editor.RequestedOverrides.Count,
                    "Changing a choice for one stable slot must replace its request, not stack another override.");
                TopologySlotOverride request = editor.RequestedOverrides[0];
                Assert.AreEqual(slot.TopologySlotId, request.TopologySlotId);
                Assert.AreEqual(slot.CanonicalOrder, request.CanonicalOrder);
                Assert.AreEqual(TopologySlotCatalog.BuildStructuralAnchor(slot),
                    request.StructuralAnchor);
                Assert.AreEqual(SemanticElementId.OrdinaryCurve, request.OriginalRealization);
                Assert.AreEqual(SemanticElementId.DirectionalCorkscrew, request.RequestedRealization);
                Assert.IsFalse(request.RebuildCurrentRealization);
                Assert.AreEqual(LocalReplanScope.OwnedConnectors, request.MaximumReplanScope);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void TopologyOverride_FinishedRouteIdResolvesShiftedPreConnectorSlot()
        {
            var target = new TopologySlotRecord
            {
                TopologySlotId = "slot-q2-r0-turn-003-n180",
                CanonicalOrder = 8,
                RouteOrder = 3,
                QuarterIndex = 1,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.Hairpin,
                PatternId = ""
            };
            var distractor = new TopologySlotRecord
            {
                TopologySlotId = "slot-q2-r0-turn-004-n180",
                CanonicalOrder = 9,
                RouteOrder = 4,
                QuarterIndex = 1,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.WideTurnaround
            };
            var request = new TopologySlotOverride
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                CanonicalOrder = 7,
                StructuralAnchor = "1|0|1|-180|2|2",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Horseshoe
            };

            Assert.IsTrue(TopologySlotCatalog.TryResolveOverride(
                new[] { distractor, target }, request,
                out TopologySlotRecord resolved, out string reason), reason);
            Assert.AreSame(target, resolved,
                "Finished-route identity must resolve the same structural demand before connectors shift its order token.");
        }

        [Test]
        public void TopologyOverride_StructuralAnchorRejectsAnExactIdCollision()
        {
            var wrongExactId = new TopologySlotRecord
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                RouteOrder = 2,
                QuarterIndex = 1,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.WideTurnaround
            };
            var intendedDemand = new TopologySlotRecord
            {
                TopologySlotId = "slot-q2-r0-turn-003-n180",
                RouteOrder = 3,
                QuarterIndex = 1,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.Hairpin
            };
            var request = new TopologySlotOverride
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                StructuralAnchor = "1|0|1|-180|2|2",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Immelmann
            };

            Assert.IsTrue(TopologySlotCatalog.TryResolveOverride(
                new[] { wrongExactId, intendedDemand }, request,
                out TopologySlotRecord resolved, out string reason), reason);
            Assert.AreSame(intendedDemand, resolved,
                "A reused route-order ID must not override the authored demand identity.");
        }

        [Test]
        public void TopologyOverride_AppliedRealizationResolvesTheRebuiltFinishedSlot()
        {
            var rebuilt = new TopologySlotRecord
            {
                TopologySlotId = "slot-q2-r0-turn-003-n180",
                RouteOrder = 3,
                QuarterIndex = 1,
                RoadId = 0,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = -180f,
                OriginalRealization = SemanticElementId.Horseshoe
            };
            var request = new TopologySlotOverride
            {
                TopologySlotId = "slot-q2-r0-turn-002-n180",
                StructuralAnchor = "1|0|1|-180|2|2",
                OriginalRealization = SemanticElementId.Hairpin,
                RequestedRealization = SemanticElementId.Horseshoe
            };

            Assert.IsTrue(TopologySlotCatalog.TryResolveOverride(
                new[] { rebuilt }, request, out TopologySlotRecord resolved,
                out string reason, appliedRealization: true), reason);
            Assert.AreSame(rebuilt, resolved,
                "Final normalization must match the replacement realization while retaining the original demand anchor.");
        }

        [Test]
        public void TopologyReplacement_OwnRecoveryConsumesSupersededAdjacentRecoveryOnly()
        {
            var existing = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.BankedHairpin,
                    QuarterIndex = 1,
                    RoadId = 0,
                    RequiresRecoveryAfter = true
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    QuarterIndex = 1,
                    RoadId = 0
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Corkscrew,
                    QuarterIndex = 1,
                    RoadId = 0
                }
            };
            var slot = new TopologySlotRecord
            {
                FirstDefinitionIndex = 0,
                LastDefinitionIndex = 0,
                QuarterIndex = 1,
                RoadId = 0,
                PatternId = ""
            };
            var replacementWithRecovery = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition { SectionType = TrackMacroSectionType.BankedHairpin },
                new TrackMacroSectionDefinition { SectionType = TrackMacroSectionType.RecoveryStraight }
            };
            var replacementWithoutRecovery = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition { SectionType = TrackMacroSectionType.BankedCurve }
            };

            Assert.AreEqual(2, TrackTopologyPlanner.ComputeReplacementRemovalCount(
                existing, slot, replacementWithRecovery, LocalReplanScope.OwnedConnectors));
            Assert.AreEqual(1, TrackTopologyPlanner.ComputeReplacementRemovalCount(
                existing, slot, replacementWithoutRecovery, LocalReplanScope.OwnedConnectors));
            Assert.AreEqual(1, TrackTopologyPlanner.ComputeReplacementRemovalCount(
                existing, slot, replacementWithRecovery, LocalReplanScope.FeatureOnly));
        }

        [Test]
        public void TopologyReplacementRequest_CurrentRealizationIsNotAChange()
        {
            var owner = new GameObject("V2_RequestFixture");
            try
            {
                TrackEditor editor = owner.AddComponent<TrackEditor>();
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q2-r0-turn-001-n090",
                    CurrentRealization = SemanticElementId.OpeningCorner
                };

                Assert.IsFalse(editor.TryRequestReplacement(slot, SemanticElementId.OpeningCorner,
                    out string reason));
                StringAssert.Contains("different", reason);
                Assert.AreEqual(0, editor.RequestedOverrides.Count);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void TopologyRegenerationRequest_CurrentRealizationCreatesExplicitRequest()
        {
            var owner = new GameObject("V2_RegenerationRequestFixture");
            try
            {
                TrackEditor editor = owner.AddComponent<TrackEditor>();
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q2-r0-turn-001-n090",
                    CanonicalOrder = 6,
                    OriginalRealization = SemanticElementId.OpeningCorner,
                    CurrentRealization = SemanticElementId.OpeningCorner,
                    LocalReplanPolicy = LocalReplanScope.OwnedConnectors
                };

                Assert.IsTrue(editor.TryRequestRegeneration(slot, out string reason), reason);
                Assert.AreEqual(1, editor.RequestedOverrides.Count);
                TopologySlotOverride request = editor.RequestedOverrides[0];
                Assert.AreEqual(slot.TopologySlotId, request.TopologySlotId);
                Assert.AreEqual(slot.CurrentRealization, request.RequestedRealization);
                Assert.IsTrue(request.RebuildCurrentRealization,
                    "Regenerating the current design must be explicit so normal same-design requests stay invalid.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void TopologyReplacementPreflight_CompatibleOwnedSegment_ReachesGeometryGate()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.OwnedConnectors);
            List<GeneratedTrackSection> owned = ReplacementOwnedGeometry(slot.TopologySlotId);

            TopologyReplacementPreflightResult result =
                TopologyReplacementPreflight.Evaluate(slot, request, owned);

            Assert.IsTrue(result.Passed, result.Reason);
            Assert.AreEqual(1, result.OwnedSectionCount);
            Assert.AreEqual(180f, result.OwnedLength, 0.001f);
            Assert.AreEqual(LocalReplanScope.OwnedConnectors, result.RequiredScope);
            StringAssert.Contains("not approved yet", result.Reason);
        }

        [Test]
        public void TopologyReplacementPreflight_ExplicitCurrentRebuild_ReachesGeometryGate()
        {
            TopologySlotRecord slot = ReplacementSlot();
            var request = new TopologySlotOverride
            {
                TopologySlotId = slot.TopologySlotId,
                CanonicalOrder = slot.CanonicalOrder,
                OriginalRealization = slot.OriginalRealization,
                RequestedRealization = slot.CurrentRealization,
                RebuildCurrentRealization = true,
                MaximumReplanScope = LocalReplanScope.OwnedConnectors
            };

            TopologyReplacementPreflightResult result = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId));

            Assert.IsTrue(result.Passed, result.Reason);
            Assert.AreEqual(TopologyReplacementPreflightStatus.ReadyForGeometryDryRun, result.Status);
        }

        [Test]
        public void TopologyReplacementPreflight_RecoveryFeature_RejectsFeatureOnlyScope()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.FeatureOnly);

            TopologyReplacementPreflightResult result = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId));

            Assert.IsFalse(result.Passed);
            Assert.AreEqual(TopologyReplacementPreflightStatus.BlockedReplanScope, result.Status);
            StringAssert.Contains("OwnedConnectors", result.Reason);
        }

        [Test]
        public void TopologyGeometryProbe_DirectBoundaryMatch_AdvancesToClearanceGate()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.OwnedConnectors);
            TopologyReplacementPreflightResult preflight = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId));
            var candidate = new FeaturePlanResult
            {
                ExitState = preflight.ExitFrame,
                ArcLength = preflight.OwnedLength
            };

            TopologyReplacementGeometryResult result =
                TopologyReplacementGeometryProbe.Evaluate(preflight, request, candidate);

            Assert.AreEqual(TopologyReplacementGeometryStatus.ReadyForClearanceValidation, result.Status,
                result.Reason);
            Assert.IsTrue(result.GeometryBuilt);
            Assert.IsTrue(result.DirectBoundaryFit);
            Assert.AreEqual(0f, result.PositionError, 0.001f);
        }

        [Test]
        public void TopologyGeometryProbe_WorldTransform_DoesNotChangeBoundaryVerdict()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.OwnedConnectors);
            TrackConnectionFrame entry = TrackConnectionFrame.Origin(48f);
            Quaternion rotation = Quaternion.Euler(0f, 67f, 0f);
            entry.Position = new Vector3(850f, 24f, -420f);
            entry.Forward = rotation * Vector3.forward;
            entry.Right = rotation * Vector3.right;
            entry.Up = rotation * Vector3.up;
            entry.ArcLength = 700f;
            TrackConnectionFrame exit = entry;
            exit.Position += entry.Forward * 180f;
            exit.ArcLength += 180f;
            TopologyReplacementPreflightResult preflight = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId, entry, exit));
            TrackConnectionFrame localExit = TrackConnectionFrame.Origin(48f);
            localExit.Position = Vector3.forward * 180f;
            localExit.ArcLength = 180f;
            var candidate = new FeaturePlanResult { ExitState = localExit, ArcLength = 180f };

            TopologyReplacementGeometryResult result =
                TopologyReplacementGeometryProbe.Evaluate(preflight, request, candidate);

            Assert.AreEqual(TopologyReplacementGeometryStatus.ReadyForClearanceValidation, result.Status,
                result.Reason);
            Assert.AreEqual(0f, result.PositionError, 0.001f);
            Assert.AreEqual(0f, result.ForwardAngleError, 0.001f);
        }

        [Test]
        public void TopologyGeometryProbe_BoundaryMismatchWithinOwnedScope_RequestsConnectorSolve()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.OwnedConnectors);
            TopologyReplacementPreflightResult preflight = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId));
            TrackConnectionFrame mismatchedExit = preflight.ExitFrame;
            mismatchedExit.Position += Vector3.right * 12f;
            var candidate = new FeaturePlanResult
            {
                ExitState = mismatchedExit,
                ArcLength = preflight.OwnedLength
            };

            TopologyReplacementGeometryResult result =
                TopologyReplacementGeometryProbe.Evaluate(preflight, request, candidate);

            Assert.AreEqual(TopologyReplacementGeometryStatus.ReadyForConnectorSolve, result.Status,
                result.Reason);
            Assert.IsTrue(result.GeometryBuilt);
            Assert.IsFalse(result.DirectBoundaryFit);
            Assert.AreEqual(12f, result.PositionError, 0.001f);
            StringAssert.Contains("No live geometry has changed", result.Reason);
        }

        [Test]
        public void TopologyGeometryProbe_BoundaryMismatchOutsideAllowedScope_IsBlocked()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.OwnedConnectors);
            TopologyReplacementPreflightResult preflight = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId));
            request.MaximumReplanScope = LocalReplanScope.FeatureOnly;
            TrackConnectionFrame mismatchedExit = preflight.ExitFrame;
            mismatchedExit.Position += Vector3.forward * 8f;
            var candidate = new FeaturePlanResult
            {
                ExitState = mismatchedExit,
                ArcLength = preflight.OwnedLength
            };

            TopologyReplacementGeometryResult result =
                TopologyReplacementGeometryProbe.Evaluate(preflight, request, candidate);

            Assert.AreEqual(TopologyReplacementGeometryStatus.BlockedBoundaryMismatch, result.Status,
                result.Reason);
            Assert.IsFalse(result.GeometryBuilt);
            StringAssert.Contains("Feature-only", result.Reason);
        }

        [Test]
        public void TopologyGeometryProbe_FailedCandidate_IsBlockedWithoutMutation()
        {
            TopologySlotRecord slot = ReplacementSlot();
            TopologySlotOverride request = ReplacementRequest(slot, LocalReplanScope.OwnedConnectors);
            TopologyReplacementPreflightResult preflight = TopologyReplacementPreflight.Evaluate(
                slot, request, ReplacementOwnedGeometry(slot.TopologySlotId));
            var candidate = new FeaturePlanResult { FailureReason = "No legal local realization." };

            TopologyReplacementGeometryResult result =
                TopologyReplacementGeometryProbe.Evaluate(preflight, request, candidate);

            Assert.AreEqual(TopologyReplacementGeometryStatus.BlockedCandidatePlanning, result.Status);
            Assert.IsFalse(result.GeometryBuilt);
            StringAssert.Contains("No legal local realization", result.Reason);
        }

        [Test]
        public void TopologyCornerCandidate_DoubleApex_UsesAuthoritativeBuilder()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                TopologySlotRecord slot = ReplacementSlot();

                FeaturePlanResult candidate = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.DoubleApex,
                    TrackConnectionFrame.Origin(resolved.RoadWidth));

                Assert.IsFalse(candidate.Failed, candidate.FailureReason);
                Assert.AreEqual(SemanticElementId.DoubleApex, candidate.Element);
                Assert.AreEqual(3, candidate.Definitions.Count,
                    "The authoritative double-apex emitter is curve + link + curve.");
                Assert.AreEqual(slot.SignedHeadingDelta, candidate.HeadingContributionDeg, 0.25f);
                Assert.IsTrue(candidate.Definitions.TrueForAll(definition =>
                    definition.SemanticElement == SemanticElementId.DoubleApex));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TopologyCornerCandidate_WideTurnaround_IsBroaderThanHairpinWithSameExactExit()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q1-r0-turn-002-p180",
                    CanonicalOrder = 4,
                    DemandType = TopologyRole.TurnRealization,
                    SignedHeadingDelta = 180f,
                    OriginalRealization = SemanticElementId.Hairpin,
                    CurrentRealization = SemanticElementId.Hairpin,
                    LocalReplanPolicy = LocalReplanScope.OwnedConnectors
                };
                TrackConnectionFrame entry = TrackConnectionFrame.Origin(resolved.RoadWidth);

                FeaturePlanResult hairpin = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.Hairpin, entry);
                FeaturePlanResult wide = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.WideTurnaround, entry);

                Assert.IsFalse(hairpin.Failed, hairpin.FailureReason);
                Assert.IsFalse(wide.Failed, wide.FailureReason);
                Assert.AreEqual(SemanticElementId.WideTurnaround, wide.Element);
                Assert.AreEqual(slot.SignedHeadingDelta, wide.HeadingContributionDeg, 0.25f,
                    $"Wide Turnaround heading was {wide.HeadingContributionDeg:F3}° for requested " +
                    $"{slot.SignedHeadingDelta:F3}°; radius {wide.Definitions.Find(d => d.SectionType == TrackMacroSectionType.BankedHairpin)?.Radius:F1}m.");
                Assert.AreEqual(hairpin.HeadingContributionDeg, wide.HeadingContributionDeg, 0.01f,
                    "The alternative must satisfy the same topology demand exactly.");

                TrackMacroSectionDefinition hairpinTurn = hairpin.Definitions.Find(definition =>
                    definition.SectionType == TrackMacroSectionType.BankedHairpin);
                TrackMacroSectionDefinition wideTurn = wide.Definitions.Find(definition =>
                    definition.SectionType == TrackMacroSectionType.BankedHairpin);
                Assert.NotNull(hairpinTurn);
                Assert.NotNull(wideTurn);
                Assert.Greater(wideTurn.Radius, hairpinTurn.Radius * 1.3f,
                    "Wide Turnaround must be a materially different broad-radius shape.");
                Assert.Greater(wide.SweptFootprint.size.x, hairpin.SweptFootprint.size.x * 1.2f,
                    $"Wide footprint X {wide.SweptFootprint.size.x:F1}m must exceed 1.2× " +
                    $"hairpin X {hairpin.SweptFootprint.size.x:F1}m.");
                Assert.AreEqual(SemanticElementId.WideTurnaround, wideTurn.SemanticElement);
                Assert.AreEqual("slingshot.wide-turnaround", wideTurn.FeatureDefinitionId);
                Assert.AreEqual("broad-eased-reversal", wideTurn.FeaturePrimitiveSequence);
                Assert.IsTrue(wide.Definitions.Exists(definition =>
                    definition.SectionType == TrackMacroSectionType.RecoveryStraight),
                    "The broad reversal retains the physical recovery contract.");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TopologyCornerCandidate_Horseshoe_IsElevatedAndUsesGenericDefinitionPath()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q2-r0-turn-003-n180",
                    CanonicalOrder = 5,
                    DemandType = TopologyRole.TurnRealization,
                    SignedHeadingDelta = -180f,
                    OriginalRealization = SemanticElementId.Hairpin,
                    CurrentRealization = SemanticElementId.Hairpin,
                    LocalReplanPolicy = LocalReplanScope.OwnedConnectors
                };

                FeaturePlanResult horseshoe = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.Horseshoe,
                    TrackConnectionFrame.Origin(resolved.RoadWidth));

                Assert.IsFalse(horseshoe.Failed, horseshoe.FailureReason);
                Assert.AreEqual(SemanticElementId.Horseshoe, horseshoe.Element);
                Assert.AreEqual(slot.SignedHeadingDelta,
                    horseshoe.HeadingContributionDeg, 0.25f);
                Assert.AreEqual(0f, horseshoe.ElevationChange, 0.01f);
                Assert.Greater(horseshoe.MaxElevation, 50f);

                TrackMacroSectionDefinition core = horseshoe.Definitions.Find(definition =>
                    definition.SemanticElement == SemanticElementId.Horseshoe);
                Assert.NotNull(core);
                Assert.Greater(core.HillHeight, 0f);
                Assert.AreEqual("slingshot.horseshoe", core.FeatureDefinitionId);
                Assert.AreEqual("elevated-banked-reversal", core.FeaturePrimitiveSequence);
                Assert.IsTrue(horseshoe.Definitions.Exists(definition =>
                    definition.SectionType == TrackMacroSectionType.RecoveryStraight));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TopologyCornerCandidate_HalfHelix_DescendsAndPreservesReversalDemand()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q2-r0-turn-004-p180",
                    CanonicalOrder = 6,
                    DemandType = TopologyRole.TurnRealization,
                    SignedHeadingDelta = 180f,
                    OriginalRealization = SemanticElementId.Hairpin,
                    CurrentRealization = SemanticElementId.Hairpin,
                    LocalReplanPolicy = LocalReplanScope.OwnedConnectors
                };

                FeaturePlanResult halfHelix = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.HalfHelixTurnaround,
                    TrackConnectionFrame.Origin(resolved.RoadWidth));

                Assert.IsFalse(halfHelix.Failed, halfHelix.FailureReason);
                Assert.AreEqual(SemanticElementId.HalfHelixTurnaround, halfHelix.Element);
                Assert.AreEqual(slot.SignedHeadingDelta,
                    halfHelix.HeadingContributionDeg, 0.25f);
                Assert.LessOrEqual(halfHelix.ElevationChange, -140f);
                Assert.AreEqual(0f, halfHelix.ExitState.PitchAngle, 0.05f);

                TrackMacroSectionDefinition core = halfHelix.Definitions.Find(definition =>
                    definition.SemanticElement == SemanticElementId.HalfHelixTurnaround);
                Assert.NotNull(core);
                Assert.AreEqual(TrackMacroSectionType.Spiral, core.SectionType);
                Assert.AreEqual("slingshot.half-helix-turnaround", core.FeatureDefinitionId);
                Assert.AreEqual("descending-half-helix-reversal", core.FeaturePrimitiveSequence);
                Assert.IsTrue(halfHelix.Definitions.Exists(definition =>
                    definition.SectionType == TrackMacroSectionType.RecoveryStraight));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TopologyCornerCandidate_SameSlotChoice_IsDeterministic()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                TopologySlotRecord slot = ReplacementSlot();
                TrackConnectionFrame entry = TrackConnectionFrame.Origin(resolved.RoadWidth);

                FeaturePlanResult first = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.WallrideTurn, entry);
                FeaturePlanResult second = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.WallrideTurn, entry);

                Assert.IsFalse(first.Failed, first.FailureReason);
                Assert.IsFalse(second.Failed, second.FailureReason);
                Assert.AreEqual(first.Definitions.Count, second.Definitions.Count);
                Assert.AreEqual(first.ArcLength, second.ArcLength, 0.0001f);
                Assert.AreEqual(first.RelativeExitPosition, second.RelativeExitPosition);
                Assert.AreEqual(first.HeadingContributionDeg, second.HeadingContributionDeg, 0.0001f);
                for (int i = 0; i < first.Definitions.Count; i++)
                {
                    Assert.AreEqual(first.Definitions[i].DebugName, second.Definitions[i].DebugName);
                    Assert.AreEqual(first.Definitions[i].Length, second.Definitions[i].Length, 0.0001f);
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void TopologyHeadingNeutralCandidate_DoubleCorkscrew_UsesFeaturePatternBuilder()
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(
                    config, TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced));
                var slot = new TopologySlotRecord
                {
                    TopologySlotId = "slot-q1-r0-neutral-001-z000",
                    CanonicalOrder = 2,
                    DemandType = TopologyRole.HeadingNeutral,
                    SignedHeadingDelta = 0f,
                    OriginalRealization = SemanticElementId.InlineCorkscrew,
                    CurrentRealization = SemanticElementId.InlineCorkscrew,
                    LocalReplanPolicy = LocalReplanScope.OwnedConnectors
                };

                FeaturePlanResult candidate = TrackTopologyPlanner.BuildReplacementCandidate(
                    resolved, slot, SemanticElementId.DoubleCorkscrew,
                    TrackConnectionFrame.Origin(resolved.RoadWidth));

                Assert.IsFalse(candidate.Failed, candidate.FailureReason);
                Assert.AreEqual(SemanticElementId.DoubleCorkscrew, candidate.Element);
                Assert.IsNotEmpty(candidate.Definitions);
                Assert.AreEqual(0f, candidate.HeadingContributionDeg, 0.25f);
                Assert.IsTrue(candidate.Definitions.Exists(definition =>
                    definition.SectionType == TrackMacroSectionType.RotationalEvent),
                    "The authoritative DoubleCorkscrew pattern must emit rotational geometry.");
                Assert.IsTrue(candidate.Definitions.TrueForAll(definition =>
                    definition.SemanticElement == SemanticElementId.DoubleCorkscrew));
            }
            finally { Object.DestroyImmediate(config); }
        }

        private static void AssertStatus(List<TopologySlotCompatibilityResult> results,
            SemanticElementId candidate, TopologySlotCompatibilityStatus expected)
        {
            TopologySlotCompatibilityResult result = results.Find(r => r.Candidate == candidate);
            Assert.NotNull(result, $"Candidate {candidate} was absent from the compatibility catalog.");
            Assert.AreEqual(expected, result.Status, result.Reason);
        }

        private static TopologySlotRecord ReplacementSlot() => new TopologySlotRecord
        {
            TopologySlotId = "slot-q1-r0-turn-002-n090",
            CanonicalOrder = 4,
            DemandType = TopologyRole.TurnRealization,
            SignedHeadingDelta = -90f,
            OriginalRealization = SemanticElementId.OrdinaryCurve,
            CurrentRealization = SemanticElementId.OrdinaryCurve,
            LocalReplanPolicy = LocalReplanScope.OwnedConnectors
        };

        private static List<TopologySlotRecord> ImpactSlots() =>
            new List<TopologySlotRecord>
            {
                ImpactSlot("before-2", 0, 0, 0, 0, 4, SemanticElementId.VerticalLoop),
                ImpactSlot("before-1", 0, 0, 1, 10, 14, SemanticElementId.InlineCorkscrew),
                ImpactSlot("selected", 0, 0, 2, 20, 24, SemanticElementId.Hairpin),
                ImpactSlot("after-1", 0, 0, 3, 30, 31, SemanticElementId.OrdinaryCurve),
                ImpactSlot("other-quarter", 1, 0, 0, 40, 45, SemanticElementId.Hairpin)
            };

        private static TopologySlotRecord ImpactSlot(
            string id, int quarter, int road, int routeOrder,
            int firstDefinition, int lastDefinition, SemanticElementId realization) =>
            new TopologySlotRecord
            {
                TopologySlotId = id,
                CanonicalOrder = quarter * 10 + routeOrder,
                QuarterIndex = quarter,
                RoadId = road,
                RouteOrder = routeOrder,
                DemandType = TopologyRole.TurnRealization,
                SignedHeadingDelta = routeOrder == 2 ? -180f : 90f,
                FirstDefinitionIndex = firstDefinition,
                LastDefinitionIndex = lastDefinition,
                OriginalRealization = realization,
                CurrentRealization = realization,
                LocalReplanPolicy = LocalReplanScope.OwnedConnectors
            };

        private static TopologySlotOverride ReplacementRequest(TopologySlotRecord slot,
            LocalReplanScope scope) => new TopologySlotOverride
        {
            TopologySlotId = slot.TopologySlotId,
            CanonicalOrder = slot.CanonicalOrder,
            OriginalRealization = slot.OriginalRealization,
            RequestedRealization = SemanticElementId.WallrideTurn,
            MaximumReplanScope = scope
        };

        private static List<GeneratedTrackSection> ReplacementOwnedGeometry(string slotId)
        {
            TrackConnectionFrame start = TrackConnectionFrame.Origin(48f);
            TrackConnectionFrame end = start;
            end.Position += Vector3.forward * 180f;
            end.ArcLength = 180f;
            return ReplacementOwnedGeometry(slotId, start, end);
        }

        private static List<GeneratedTrackSection> ReplacementOwnedGeometry(
            string slotId, TrackConnectionFrame start, TrackConnectionFrame end)
        {
            return new List<GeneratedTrackSection>
            {
                new GeneratedTrackSection
                {
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.BankedCurve,
                        Length = 180f,
                        Width = 48f,
                        SemanticElement = SemanticElementId.OrdinaryCurve,
                        TopologySlotId = slotId
                    },
                    StartFrame = start,
                    EndFrame = end,
                    TopologySlotId = slotId
                }
            };
        }

        private static List<TrackMacroSectionDefinition> SlotFixture(int connectorCount,
            SemanticElementId secondRealization, string secondPattern)
        {
            var definitions = new List<TrackMacroSectionDefinition>
            {
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.BankedCurve,
                    QuarterIndex = 0,
                    RoadId = 0,
                    TurnAngle = 90f,
                    Direction = SectionTurnDirection.Right,
                    SemanticElement = SemanticElementId.OrdinaryCurve,
                    Contract = SectionConnectionContract.Level(90f)
                }
            };
            for (int i = 0; i < connectorCount; i++)
            {
                definitions.Add(new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    QuarterIndex = 0,
                    RoadId = 0,
                    DebugName = "Connector_" + i,
                    Contract = SectionConnectionContract.Level()
                });
            }
            definitions.Add(new TrackMacroSectionDefinition
            {
                SectionType = secondRealization == SemanticElementId.WallrideTurn
                    ? TrackMacroSectionType.WallrideTurn
                    : TrackMacroSectionType.BankedHairpin,
                QuarterIndex = 0,
                RoadId = 0,
                PatternId = secondPattern,
                TurnAngle = 180f,
                Direction = SectionTurnDirection.Left,
                SemanticElement = secondRealization,
                Contract = SectionConnectionContract.Level(-180f)
            });
            definitions.Add(new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RecoveryStraight,
                QuarterIndex = 0,
                RoadId = 0,
                PatternId = secondPattern,
                SemanticElement = SemanticElementId.RecoverySection,
                Contract = SectionConnectionContract.Level()
            });
            return definitions;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing private field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static string RemovePrimitiveFieldForLegacyFixture(string json, string field)
        {
            string token = $",\"{field}\":";
            int start = json.IndexOf(token, System.StringComparison.Ordinal);
            if (start < 0) return json;
            int end = start + token.Length;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Remove(start, end - start);
        }

        private static string RemoveArrayFieldForLegacyFixture(string json, string field)
        {
            string token = $",\"{field}\":";
            int start = json.IndexOf(token, System.StringComparison.Ordinal);
            if (start < 0) return json;
            int arrayStart = start + token.Length;
            if (arrayStart >= json.Length || json[arrayStart] != '[') return json;
            int depth = 0;
            for (int i = arrayStart; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']' && --depth == 0)
                    return json.Remove(start, i - start + 1);
            }
            return json;
        }
    }
}
