using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Design;

namespace TrackGeneration.Tests
{
    /// <summary>The debug-report exporter must produce every analysis block for a real generated track.</summary>
    public class DebugReportExportTests
    {
        [Test]
        public void ReportContainsAllAnalysisBlocks()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            var go = new GameObject("ReportExportTest_Generator");
            try
            {
                go.AddComponent<Core.TrackSeedManager>();
                var generator = go.AddComponent<TrackGenerator>();
                generator.Config = config;
                generator.Designer = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                generator.Designer.Quarters.MinimumDualQuarterCount = 1;
                generator.Designer.Quarters.MaximumDualQuarterCount = 1;

                var seedManager = go.GetComponent<Core.TrackSeedManager>();
                seedManager.UseRandomSeed = false;
                // A failed seed logs a [Error] the LogAssert harness would fail the
                // test on — the scan only needs ONE success, so mute while scanning.
                bool success = false;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                try
                {
                    for (int s = 9900; s < 9914 && !success; s++)
                    {
                        seedManager.CurrentSeedInput = s;
                        generator.GenerateTrack();
                        success = generator.LastReport is { Success: true };
                    }
                }
                finally
                {
                    UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                }
                Assert.IsTrue(success, "No valid track generated for the export test.");

                string text = TrackDebugReportExporter.BuildReport(generator);

                Assert.Greater(text.Length, 5000, "Report suspiciously short.");
                StringAssert.Contains("SCENE SETTINGS", text);
                StringAssert.Contains("MATERIAL CONTRACT", text);
                StringAssert.Contains("RoadSurface:", text);
                StringAssert.Contains("RESOLVED", text);
                StringAssert.Contains("CHAIN MAP", text);
                StringAssert.Contains("VERTICAL SILHOUETTE", text);
                StringAssert.Contains("longest sustained grade", text);
                StringAssert.Contains("FULL SECTION + RING DUMP", text);
                StringAssert.Contains("CONNECTOR AUDIT", text);
                StringAssert.Contains("WALL-WAVE HOTSPOTS", text);
                StringAssert.Contains("DISCONTINUITY SCAN", text);
                StringAssert.Contains("QUARTER DISSECTION", text);
                StringAssert.Contains("DUAL ROAD", text);
                StringAssert.Contains("road B:", text);
                StringAssert.Contains("SUBDIVISION REGIONS", text);
                StringAssert.Contains("END OF REPORT", text);

                // The scene-settings dump must expose the wavy-wall-relevant sliders.
                StringAssert.Contains("INHERITANCE", text);
                StringAssert.Contains("BRIDGE", text);

                // FULL dump: one row per ring for EVERY meshed section — the report must
                // carry at least as many lines as the track has rings.
                int totalRings = generator.LastMetrics.TotalRings;
                int lineCount = 0;
                foreach (char c in text) if (c == '\n') lineCount++;
                Assert.Greater(lineCount, totalRings,
                    $"Report has {lineCount} lines but the track has {totalRings} rings — the per-ring dump is incomplete.");

                // Every section header must appear.
                foreach (var sec in generator.CurrentMacroSections)
                    StringAssert.Contains($"[{sec.SectionIndex:D3}] {sec.Definition.SectionType}", text);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
            }
        }
    }
}

