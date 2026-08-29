using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    public class GenerationRecipeTests
    {
        [Test]
        public void RecipeRatingRoundTripDoesNotChangeExactIdentity()
        {
            TrackConfig config = ScriptableObject.CreateInstance<TrackConfig>();
            try
            {
                config.Validate();
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(
                    TrackStylePresetLibrary.Rollercoaster);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(314159);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(314159), streams, settings, config, null,
                    LayoutLockMode.Unlocked, null);
                string hashBeforeRating = recipe.RecipeHash;
                recipe.Rating = new TrackRecipeRating
                {
                    Version = TrackRecipeRating.CurrentVersion,
                    Overall = 84,
                    Flow = 87,
                    Variety = 91,
                    RequestFit = 78,
                    TechnicalQuality = 80,
                    RatedLayoutHash = "layout-abc",
                    Source = "Test"
                };

                string json = GenerationRecipeV1.Serialize(recipe, true);
                GenerationRecipeV1 restored = GenerationRecipeV1.Deserialize(json, out string error);

                Assert.NotNull(restored, error);
                Assert.AreEqual(hashBeforeRating, restored.RecipeHash,
                    "Output rating metadata changed exact generation identity.");
                Assert.AreEqual(84, restored.Rating.Overall);
                Assert.AreEqual("layout-abc", restored.Rating.RatedLayoutHash);
                Assert.IsTrue(restored.ValidateFor(config, RecipeReplayMode.Strict, out error), error);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RecipeRatingCalculatorProducesBoundedExplainableComponents()
        {
            TrackDesignerSettings settings = TrackStylePresetLibrary.Create(
                TrackStylePresetLibrary.Rollercoaster);
            var metrics = new TrackGenerationMetrics
            {
                EstimatedNeutralLapTimeSeconds = settings.Scale.TargetLapTimeSeconds,
                TurnCount = Mathf.RoundToInt((settings.Layout.MinTurnCount +
                                              settings.Layout.MaxTurnCount) * 0.5f),
                MinElevation = -150f,
                MaxElevation = 150f,
                EncounterCount = 9,
                DistinctEncounterFamilies = 8,
                RhythmScore = 90f,
                RhythmViolationCount = 0,
                LongestEncounterFamilyStreak = 2,
                TotalRings = Mathf.RoundToInt(settings.Generation.TargetTotalRings * 0.8f),
                MaxFacetAngleObserved = settings.Generation.MaxFacetAngleDegrees
            };
            var report = new TrackGenerationReport { Success = true };

            TrackRecipeRating rating = TrackRecipeRatingCalculator.Calculate(
                metrics, report, settings, "layout-good");

            Assert.IsTrue(rating.IsRated);
            Assert.That(rating.Overall, Is.InRange(0, 100));
            Assert.That(rating.Flow, Is.InRange(0, 100));
            Assert.That(rating.Variety, Is.InRange(0, 100));
            Assert.That(rating.RequestFit, Is.InRange(0, 100));
            Assert.That(rating.TechnicalQuality, Is.InRange(0, 100));
            Assert.GreaterOrEqual(rating.Overall, 85);
        }

        [Test]
        public void DisplayedRecipeRatingRecoversFromCommittedReportWhenSerializedRecipeIsStale()
        {
            var host = new GameObject("TrackGenerator_RatingState_Test");
            try
            {
                TrackGenerator generator = host.AddComponent<TrackGenerator>();
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(
                    TrackStylePresetLibrary.Rollercoaster);
                generator.Designer = settings;

                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(271828);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(271828), streams, settings, null, null,
                    LayoutLockMode.Unlocked, host.transform);
                recipe.ExpectedLayoutHash = "accepted-layout";
                recipe.Rating = new TrackRecipeRating();

                var metrics = new TrackGenerationMetrics
                {
                    EstimatedNeutralLapTimeSeconds = settings.Scale.TargetLapTimeSeconds,
                    TurnCount = settings.Layout.MinTurnCount,
                    EncounterCount = 6,
                    DistinctEncounterFamilies = 5,
                    RhythmScore = 83f,
                    TotalRings = 1000,
                    MaxFacetAngleObserved = settings.Generation.MaxFacetAngleDegrees
                };
                var report = new TrackGenerationReport
                {
                    Success = true,
                    TrackRating = 77
                };

                SetPrivateField(generator, "lastAcceptedRecipe", recipe);
                // Reproduce the Inspector regression: an older retained serialized
                // snapshot still contains a pending recipe even though the user did
                // not load anything for replay in the current state.
                SetPrivateField(generator, "pendingImportedRecipe", recipe.Clone());
                SetPrivateField(generator, "pendingRecipeAwaitingReplay", false);
                SetPrivateField(generator, "lastMetrics", metrics);
                SetPrivateField(generator, "lastReport", report);
                SetPrivateField(generator, "lastResultManifest", new TrackResultManifest
                {
                    CanonicalLayoutHash = "accepted-layout"
                });

                Assert.AreSame(recipe, generator.DisplayedRecipe,
                    "A stale pending snapshot must not replace the accepted recipe card.");
                TrackRecipeRating displayed = generator.DisplayedRecipeRating;

                Assert.NotNull(displayed);
                Assert.IsTrue(displayed.IsRated);
                Assert.AreEqual(77, displayed.Overall,
                    "The card must show the exact score already published by generation.");
                Assert.AreEqual("accepted-layout", displayed.RatedLayoutHash);
                Assert.AreEqual(displayed.Overall, generator.LastReport.TrackRating);
                Assert.IsFalse(generator.LastAcceptedRecipe.Rating.IsRated,
                    "Inspector repaint must not mutate or re-hash hidden recipe state.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RecipeRoundTripPreservesExactInputsAndHash()
        {
            TrackConfig config = ScriptableObject.CreateInstance<TrackConfig>();
            try
            {
                config.Validate();
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
                settings.Corners.MaxBankAngle = 57f;
                settings.Elevation.Profile = VerticalGenerationProfile.Extreme;
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(741380678);
                streams.FeatureSeed = 1234567;
                var locks = new SettingsLockState { Features = true, RoadShape = true };

                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(741380678), streams, settings, config, locks,
                    LayoutLockMode.Skeleton, null);

                string json = GenerationRecipeV1.Serialize(recipe, true);
                GenerationRecipeV1 restored = GenerationRecipeV1.Deserialize(json, out string error);

                Assert.IsNotNull(restored, error);
                Assert.IsTrue(restored.ValidateFor(config, RecipeReplayMode.Strict, out error), error);
                Assert.AreEqual(recipe.RecipeHash, restored.RecipeHash);
                Assert.AreEqual(recipe.BaseRecipeHash, restored.BaseRecipeHash);
                Assert.AreEqual(741380678, restored.BaseSeed);
                Assert.AreEqual(1234567, restored.SeedStreams.FeatureSeed);
                Assert.IsTrue(restored.SettingsLocks.Features);
                Assert.IsTrue(restored.SettingsLocks.RoadShape);
                Assert.AreEqual(LayoutLockMode.Skeleton, restored.LayoutLockMode);
                Assert.AreEqual(57f, restored.DeserializeDesignerSettings().Corners.MaxBankAngle, 0.001f);
                Assert.AreEqual(
                    VerticalGenerationProfile.Extreme,
                    restored.DeserializeDesignerSettings().Elevation.Profile,
                    "The designer-facing elevation personality was lost during exact recipe replay.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void VariantOverrideChangesExactHashButKeepsBaseHash()
        {
            TrackConfig config = ScriptableObject.CreateInstance<TrackConfig>();
            try
            {
                config.Validate();
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(42);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(42), streams, settings, config, null,
                    LayoutLockMode.Unlocked, null);
                string baseHash = recipe.BaseRecipeHash;
                string exactHash = recipe.RecipeHash;

                recipe.RecipeRevision = 1;
                recipe.TopologySlotOverrides.Add(new TopologySlotOverride
                {
                    TopologySlotId = "turn-012",
                    CanonicalOrder = 12,
                    StructuralAnchor = "2|0|1|-180|2|3",
                    OriginalRealization = SemanticElementId.Hairpin,
                    RequestedRealization = SemanticElementId.None,
                    MaximumReplanScope = LocalReplanScope.Quarter
                });
                recipe.RefreshHashes();

                Assert.AreEqual(baseHash, recipe.BaseRecipeHash,
                    "A handpicked variant lost its lineage to the same base recipe.");
                Assert.AreNotEqual(exactHash, recipe.RecipeHash,
                    "A topology-slot override did not change exact track identity.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ModifiedRecipeWithStaleHashIsRejected()
        {
            TrackConfig config = ScriptableObject.CreateInstance<TrackConfig>();
            try
            {
                config.Validate();
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
                var streams = new TrackSeedStreams();
                streams.DeriveAllFrom(99);
                GenerationRecipeV1 recipe = GenerationRecipeV1.Capture(
                    TrackSeed.CreateNew(99), streams, settings, config, null,
                    LayoutLockMode.Unlocked, null);

                recipe.SeedStreams.LayoutSeed++;
                Assert.IsFalse(recipe.ValidateFor(config, RecipeReplayMode.Strict, out string error));
                StringAssert.Contains("hash", error.ToLowerInvariant());
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void CanonicalLayoutHashIgnoresSubdivisionDensityButDetectsRouteChange()
        {
            GeneratedTrackLayout a = LayoutWithOneStraight(new Vector3(0f, 0f, 100f), 5);
            GeneratedTrackLayout b = LayoutWithOneStraight(new Vector3(0f, 0f, 100f), 50);

            string hashA = TrackCanonicalHasher.ComputeLayoutHash(a);
            string hashB = TrackCanonicalHasher.ComputeLayoutHash(b);
            Assert.AreEqual(hashA, hashB, "Mesh subdivision density changed the macro layout identity.");

            b.Sections[0].EndFrame = Frame(new Vector3(0f, 0f, 101f), 101f);
            Assert.AreNotEqual(hashA, TrackCanonicalHasher.ComputeLayoutHash(b),
                "A one-meter route change did not change the canonical layout identity.");
        }

        private static GeneratedTrackLayout LayoutWithOneStraight(Vector3 end, int subdivisionCount)
        {
            TrackConnectionFrame start = Frame(Vector3.zero, 0f);
            TrackConnectionFrame finish = Frame(end, end.z);
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                SemanticElement = SemanticElementId.None,
                Direction = SectionTurnDirection.None,
                Length = end.z,
                Width = 48f,
                SubdivisionCount = subdivisionCount,
                DebugName = "Straight"
            };
            return new GeneratedTrackLayout
            {
                LapLength = end.z,
                Sections =
                {
                    new GeneratedTrackSection
                    {
                        SectionIndex = 0,
                        QuarterIndex = 0,
                        RoadId = 0,
                        Definition = definition,
                        StartFrame = start,
                        EndFrame = finish,
                        SubdivisionFrames = new TrackConnectionFrame[subdivisionCount + 1]
                    }
                }
            };
        }

        private static TrackConnectionFrame Frame(Vector3 position, float arc)
        {
            TrackConnectionFrame frame = TrackConnectionFrame.Origin(48f);
            frame.Position = position;
            frame.ArcLength = arc;
            frame.LapProgress = arc / 100f;
            return frame;
        }

        private static void SetPrivateField<T>(TrackGenerator generator, string name, T value)
        {
            FieldInfo field = typeof(TrackGenerator).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"TrackGenerator field '{name}' was not found.");
            field.SetValue(generator, value);
        }
    }
}
