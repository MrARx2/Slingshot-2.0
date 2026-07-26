using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Explicit-only diagnostic probes: never run in the normal suite; invoke with a
    /// test filter to print failure distributions when a preset's success rate drops.
    /// </summary>
    public class DiagnosticProbes
    {
        private static string Distribution(string label, TrackDesignerSettings settings, int seeds)
        {
            int success = 0;
            var counts = new Dictionary<GenerationFailureReason, int>();
            var samples = new Dictionary<GenerationFailureReason, string>();

            for (int i = 0; i < seeds; i++)
            {
                var result = TrackGenerationTestUtil.Generate(settings.Clone(), 31000 + i * 101);
                if (result.Success) { success++; continue; }
                foreach (var (reason, count) in result.Report.FailureCountsByReason())
                {
                    counts.TryGetValue(reason, out int c);
                    counts[reason] = c + count;
                }
                foreach (var f in result.Report.Failures)
                    if (!samples.ContainsKey(f.Reason)) samples[f.Reason] = f.Message;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"### {label}: {success}/{seeds} success");
            foreach (var kv in counts)
            {
                sb.AppendLine($"  {kv.Key}: x{kv.Value}");
                if (samples.TryGetValue(kv.Key, out var msg)) sb.AppendLine($"    e.g. {msg}");
            }
            return sb.ToString();
        }

        [Test, Explicit]
        public void SwitchbackFailureIsolation()
        {
            var sb = new System.Text.StringBuilder();

            var baseline = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Switchback);
            sb.AppendLine(Distribution("Switchback AS-IS", baseline, 20));

            var noWallride = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Switchback);
            noWallride.Features.Wallrides = new TrackFeatureRule(false, 0, 0, 0f);
            sb.AppendLine(Distribution("Switchback NoWallrides", noWallride, 20));

            var noPipe = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Switchback);
            noPipe.Features.FullPipes = new TrackFeatureRule(false, 0, 0, 0f);
            sb.AppendLine(Distribution("Switchback NoFullPipes", noPipe, 20));

            var noDual = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Switchback);
            noDual.Quarters.MinimumDualQuarterCount = 0;
            noDual.Quarters.MaximumDualQuarterCount = 0;
            sb.AppendLine(Distribution("Switchback NoDualQuarters", noDual, 20));

            var none = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Switchback);
            none.Quarters.MinimumDualQuarterCount = 0;
            none.Quarters.MaximumDualQuarterCount = 0;
            none.Features.Wallrides = new TrackFeatureRule(false, 0, 0, 0f);
            none.Features.FullPipes = new TrackFeatureRule(false, 0, 0, 0f);
            sb.AppendLine(Distribution("Switchback NONE-of-the-new", none, 20));

            Debug.Log(sb.ToString());
            Assert.Pass(sb.ToString());
        }

        [Test, Explicit]
        public void HugeBalancedFailureIsolation()
        {
            var sb = new System.Text.StringBuilder();

            var huge = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            TrackSizeModifier.Apply(huge, TrackSizeLevel.Huge);
            sb.AppendLine(Distribution("Huge Balanced AS-IS", huge, 15));

            var hugeNoDual = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            TrackSizeModifier.Apply(hugeNoDual, TrackSizeLevel.Huge);
            hugeNoDual.Quarters.MinimumDualQuarterCount = 0;
            hugeNoDual.Quarters.MaximumDualQuarterCount = 0;
            sb.AppendLine(Distribution("Huge Balanced NoDualQuarters", hugeNoDual, 15));

            var hugeNoNew = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            TrackSizeModifier.Apply(hugeNoNew, TrackSizeLevel.Huge);
            hugeNoNew.Quarters.MinimumDualQuarterCount = 0;
            hugeNoNew.Quarters.MaximumDualQuarterCount = 0;
            hugeNoNew.Features.Wallrides = new TrackFeatureRule(false, 0, 0, 0f);
            hugeNoNew.Features.FullPipes = new TrackFeatureRule(false, 0, 0, 0f);
            sb.AppendLine(Distribution("Huge Balanced NONE-of-the-new", hugeNoNew, 15));

            Debug.Log(sb.ToString());
            Assert.Pass(sb.ToString());
        }

        [Test, Explicit]
        [Timeout(600000)]
        public void ReportedSeedUsesActiveSceneSettingsAndProducesAValidCandidate()
        {
            const string configPath = "Assets/Scripts/TrackGeneration/TrackConfig.asset";
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            const int reportedSeed = 1391502902;

            var config = AssetDatabase.LoadAssetAtPath<TrackConfig>(configPath);
            Assert.IsNotNull(config, $"Missing project config at {configPath}.");
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var generator = Object.FindAnyObjectByType<TrackGenerator>();
            Assert.IsNotNull(generator, $"No TrackGenerator found in {scenePath}.");

            var result = TrackGenerationTestUtil.Generate(generator.Designer.Clone(), reportedSeed, config);
            if (result.Success) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Seed {reportedSeed} failed all {result.AttemptsEvaluated} active-scene attempts:");
            foreach (var (reason, count) in result.Report.FailureCountsByReason())
                sb.AppendLine($"  {reason}: {count}");
            foreach (var failure in result.Report.Failures)
                sb.AppendLine($"  attempt {failure.AttemptIndex}: {failure.Reason} — {failure.Message}");
            Assert.Fail(sb.ToString());
        }

        [TestCase(-1054293041)]
        [TestCase(-1381830410)]
        [Explicit, Timeout(600000)]
        public void ReportedWallrideAndCorkscrewSeedsUseSafeFeatureGeometry(int reportedSeed)
        {
            const string configPath = "Assets/Scripts/TrackGeneration/TrackConfig.asset";
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            var config = AssetDatabase.LoadAssetAtPath<TrackConfig>(configPath);
            Assert.IsNotNull(config);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var generator = Object.FindAnyObjectByType<TrackGenerator>();
            Assert.IsNotNull(generator);

            var result = TrackGenerationTestUtil.Generate(generator.Designer.Clone(), reportedSeed, config);
            Assert.IsTrue(result.Success,
                $"Reported seed {reportedSeed} no longer produces a valid candidate after the focused geometry fix.");

            bool checkedFeature = false;
            foreach (var section in result.Layout.Sections)
            {
                var frames = section.SubdivisionFrames;
                if (frames == null || frames.Length < 5) continue;

                if (reportedSeed == -1054293041 &&
                    section.Definition.SectionType == TrackMacroSectionType.WallrideTurn)
                {
                    int mid = frames.Length / 2;
                    var f = frames[mid];
                    Vector3 inward = (frames[mid + 1].Forward - frames[mid - 1].Forward).normalized;
                    Vector3 raisedDirection = f.LeftWallMultiplier > f.RightWallMultiplier ? -f.Right : f.Right;
                    Assert.Less(Vector3.Dot(raisedDirection, inward), -0.8f,
                        $"Reported wallride '{section.Definition.DebugName}' still raises its geometric inside wall.");
                    checkedFeature = true;
                }

                if (reportedSeed == -1381830410 &&
                    section.Definition.SectionType == TrackMacroSectionType.RotationalEvent &&
                    section.Definition.DebugName?.StartsWith("Corkscrew_") == true)
                {
                    float minElevation = float.PositiveInfinity;
                    float maxElevation = float.NegativeInfinity;
                    float supportWeighted = 0f;
                    float curvatureWeight = 0f;
                    for (int i = 0; i < frames.Length; i++)
                    {
                        minElevation = Mathf.Min(minElevation, frames[i].Position.y);
                        maxElevation = Mathf.Max(maxElevation, frames[i].Position.y);
                        if (i == 0 || i == frames.Length - 1) continue;
                        float local = (float)i / (frames.Length - 1);
                        if (local < 0.2f || local > 0.8f) continue;
                        Vector3 curvature = frames[i + 1].Forward - frames[i - 1].Forward;
                        float weight = curvature.magnitude;
                        if (weight < 1e-6f) continue;
                        supportWeighted += Vector3.Dot(curvature / weight, frames[i].Up) * weight;
                        curvatureWeight += weight;
                    }
                    Assert.Greater(maxElevation - minElevation, frames[0].Width * 0.5f,
                        "Reported corkscrew is still visually flat.");
                    Assert.Greater(supportWeighted / Mathf.Max(1e-6f, curvatureWeight), 0.25f,
                        "Reported corkscrew curvature does not point into its rolled driving surface.");
                    checkedFeature = true;
                }
            }

            Assert.IsTrue(checkedFeature,
                $"Reported seed {reportedSeed} did not retain the feature this diagnostic is meant to verify.");
        }
    }
}
