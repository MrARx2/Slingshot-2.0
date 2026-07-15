using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage 6 guards: Very Small size preset, settings-group locks, curated Random
    /// resolution and independent seed streams.
    /// </summary>
    public class VerySmallPresetTests
    {
        [TestCase(TrackStylePresetLibrary.Flowing)]
        [TestCase(TrackStylePresetLibrary.Balanced)]
        [TestCase(TrackStylePresetLibrary.Technical)]
        [TestCase(TrackStylePresetLibrary.Velocity)]
        [TestCase(TrackStylePresetLibrary.Rollercoaster)]
        [TestCase(TrackStylePresetLibrary.Switchback)]
        public void VerySmallLapsHitTheSprintWindow(string preset)
        {
            int successes = 0;
            float lapSum = 0f;

            for (int i = 0; i < 8; i++)
            {
                var s = TrackGenerationTestUtil.FastSettings(preset);
                TrackSizeModifier.Apply(s, TrackSizeLevel.VerySmall);

                // Reduced content, never shrunken features: the sprint keeps the same
                // rulebook feature windows — only counts go down.
                Assert.LessOrEqual(s.Layout.MaxTurnCount, 10);
                Assert.LessOrEqual(s.Features.MaxFeatureGroups, 3);
                Assert.LessOrEqual(s.Quarters.MaximumDualQuarterCount, 1);
                Assert.LessOrEqual(s.Elevation.MaxMajorElevationSections, 4);
                Assert.AreEqual(52.5f, s.Scale.TargetLapTimeSeconds, 0.01f);

                var result = TrackGenerationTestUtil.Generate(s, 9000 + preset.GetHashCode() % 1000 + i * 13);
                if (!result.Success) continue;
                successes++;
                lapSum += result.Layout.Metrics.EstimatedNeutralLapTimeSeconds;
            }

            Assert.GreaterOrEqual(successes, 4, $"Very Small {preset} rarely generates.");
            float avgLap = lapSum / successes;
            // Route-performance estimate, not track÷speed: technical laps run slower.
            Assert.That(avgLap, Is.InRange(35f, 75f),
                $"Very Small {preset} average estimated lap {avgLap:F1}s is far outside the 45–60s intent.");
        }

        [Test]
        public void VerySmallNeverShrinksFeatureGeometryWindows()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster);
                var before = Macro.ResolvedTrackGenerationConfig.Resolve(config, s.Clone());
                TrackSizeModifier.Apply(s, TrackSizeLevel.VerySmall);
                var after = Macro.ResolvedTrackGenerationConfig.Resolve(config, s);

                Assert.AreEqual(before.MinLoopRadius, after.MinLoopRadius, "Loop radius window shrank.");
                Assert.AreEqual(before.MinCorkscrewLength, after.MinCorkscrewLength, 0.5f, "Corkscrew length window shrank.");
                Assert.AreEqual(before.BankTransitionLength, after.BankTransitionLength, 0.5f, "Transition lengths shrank.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }

    public class SettingsLockTests
    {
        [Test]
        public void PresetSnapshotJsonIsBoundedAndLegacyRecursionIsTrimmed()
        {
            var settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
            int canonicalLength = settings.ToJson().Length;
            var snapshotField = typeof(TrackDesignerSettings).GetField("appliedPresetSnapshotJson",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(snapshotField);

            // Recreate the legacy failure mode without going through the new setter:
            // each whole-settings serialization embeds the previous whole snapshot.
            for (int i = 0; i < 7; i++)
                snapshotField.SetValue(settings, JsonUtility.ToJson(settings));
            int bloatedLength = ((string)snapshotField.GetValue(settings)).Length;
            Assert.Greater(bloatedLength, canonicalLength * 10);

            Assert.IsTrue(settings.NormalizeAppliedPresetSnapshot());
            Assert.LessOrEqual(settings.AppliedPresetSnapshotJson.Length, canonicalLength + 4);

            int boundedLength = settings.AppliedPresetSnapshotJson.Length;
            for (int i = 0; i < 50; i++)
            {
                settings.AppliedPresetSnapshotJson = settings.ToJson();
                settings = settings.Clone();
                Assert.LessOrEqual(settings.AppliedPresetSnapshotJson.Length, boundedLength + 4,
                    $"Snapshot grew again on refresh {i}.");
            }

            settings.Corners.MaxBankAngle = 51f;
            var clone = settings.Clone();
            clone.Corners.MaxBankAngle = 42f;
            Assert.AreEqual(51f, settings.Corners.MaxBankAngle, 0.01f, "Clone lost deep-copy semantics.");
        }

        [Test]
        public void LockedQuartersSurviveRollercoasterPreset()
        {
            var current = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
            current.Quarters.MinimumDualQuarterCount = 0;
            current.Quarters.MaximumDualQuarterCount = 0;

            var locks = new SettingsLockState { Quarters = true };
            var incoming = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
            var result = PresetApplicator.Apply(current, incoming, locks);

            Assert.AreEqual(0, current.Quarters.MinimumDualQuarterCount, "Locked quarter minimum changed.");
            Assert.AreEqual(0, current.Quarters.MaximumDualQuarterCount, "Locked quarter maximum changed.");
            CollectionAssert.Contains(result.SkippedGroups, "Quarters");
            CollectionAssert.Contains(result.ChangedGroups, "Features"); // Rollercoaster differs from Balanced
        }

        [Test]
        public void LockModifiedGroupsDetectsManualEdits()
        {
            var settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
            settings.Corners.MaxBankAngle = 55f; // manual edit after the preset snapshot

            var locks = new SettingsLockState();
            var locked = PresetApplicator.LockModifiedGroups(settings, locks);

            CollectionAssert.Contains(locked, "Corners");
            Assert.IsTrue(locks.Corners);
            Assert.IsFalse(locks.Layout, "Untouched group was locked.");
        }

        [Test]
        public void ResetUnlockedGroupsRestoresOnlyUnlocked()
        {
            var settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
            float originalBank = settings.Corners.MaxBankAngle;
            settings.Corners.MaxBankAngle = 55f;
            settings.Road.RoadWidth = 40f;

            var locks = new SettingsLockState { RoadShape = true };
            PresetApplicator.ResetUnlockedGroups(settings, locks);

            Assert.AreEqual(originalBank, settings.Corners.MaxBankAngle, 0.01f, "Unlocked group was not reset.");
            Assert.AreEqual(40f, settings.Road.RoadWidth, 0.01f, "Locked group was reset.");
        }
    }

    public class PresetRandomizerTests
    {
        [Test]
        public void RandomResolutionRespectsLockedQuarters()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var current = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
                current.Quarters.MinimumDualQuarterCount = 0;
                current.Quarters.MaximumDualQuarterCount = 0;
                var locks = new SettingsLockState { Quarters = true };

                var rng = new Unity.Mathematics.Random(1234);
                var req = new PresetRandomizer.Request { StyleIsRandom = true, DifficultyIsRandom = true, SizeIsRandom = true };
                var sel = PresetRandomizer.Resolve(req, config, current, locks, ref rng);

                Assert.IsFalse(string.IsNullOrEmpty(sel.Style));
                var settings = PresetRandomizer.BuildCandidate(sel.Style, sel.Difficulty, sel.Size, current, locks);
                Assert.AreEqual(0, settings.Quarters.MinimumDualQuarterCount, "Resolved combo violates the quarter lock.");
                Assert.AreEqual(0, settings.Quarters.MaximumDualQuarterCount);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RandomResolutionIsDeterministicPerSeed()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var current = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
                var req = new PresetRandomizer.Request { StyleIsRandom = true, DifficultyIsRandom = true, SizeIsRandom = true };

                var rngA = new Unity.Mathematics.Random(777);
                var rngB = new Unity.Mathematics.Random(777);
                var a = PresetRandomizer.Resolve(req, config, current, null, ref rngA);
                var b = PresetRandomizer.Resolve(req, config, current, null, ref rngB);
                Assert.AreEqual(a.ToString(), b.ToString(), "Same resolution seed produced different picks.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }

    public class SeedStreamTests
    {
        private static TrackSeedStreams Streams(int master)
        {
            var s = new TrackSeedStreams();
            s.DeriveAllFrom(master);
            return s;
        }

        [Test]
        public void SameStreamsProduceIdenticalLayouts()
        {
            var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            var a = TrackGenerationTestUtil.Generate(settings, 4242, streams: Streams(4242));
            var b = TrackGenerationTestUtil.Generate(settings, 4242, streams: Streams(4242));
            Assert.IsTrue(a.Success && b.Success);
            Assert.AreEqual(TrackGenerationTestUtil.Fingerprint(a.Layout), TrackGenerationTestUtil.Fingerprint(b.Layout));
        }

        [Test]
        public void VisualSeedNeverChangesGeometry()
        {
            var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            var s1 = Streams(555);
            var s2 = Streams(555);
            s2.VisualSeed = 999999;
            s2.SurfaceSeed = 123456;

            var a = TrackGenerationTestUtil.Generate(settings, 555, streams: s1);
            var b = TrackGenerationTestUtil.Generate(settings, 555, streams: s2);
            Assert.IsTrue(a.Success && b.Success);
            Assert.AreEqual(TrackGenerationTestUtil.Fingerprint(a.Layout), TrackGenerationTestUtil.Fingerprint(b.Layout),
                "Changing Surface/Visual seeds altered the geometry.");
        }

        [Test]
        public void FeatureSeedKeepsTheCornerSkeleton()
        {
            var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            settings.Quarters.MinimumDualQuarterCount = 0;
            settings.Quarters.MaximumDualQuarterCount = 0;

            TrackGenerationResult a = null, b = null;
            for (int master = 4300; master < 4320 && (a == null || !a.Success || !b.Success); master++)
            {
                var s1 = Streams(master);
                var s2 = Streams(master);
                s2.FeatureSeed = master * 31 + 7;

                a = TrackGenerationTestUtil.Generate(settings, master, streams: s1);
                b = TrackGenerationTestUtil.Generate(settings, master, streams: s2);
            }
            Assert.IsTrue(a is { Success: true } && b is { Success: true }, "No seed produced two valid tracks.");

            // The corner plan (layout stream) is untouched: same turn count.
            Assert.AreEqual(a.Layout.Metrics.TurnCount, b.Layout.Metrics.TurnCount,
                "Feature re-roll changed the corner plan.");
            // The content differs (with overwhelming probability across the scan).
            Assert.AreNotEqual(TrackGenerationTestUtil.Fingerprint(a.Layout), TrackGenerationTestUtil.Fingerprint(b.Layout),
                "Feature re-roll produced an identical track — streams are not independent.");
        }
    }
}
