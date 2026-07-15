using NUnit.Framework;
using TrackGeneration.Editor;
using TrackGeneration.Design;
using TrackGeneration.Planning;
using UnityEngine;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Inspector performance contract: the TrackGenerator inspector may only DRAW
    /// cached data during ordinary repaints. These tests prove the cache refreshes
    /// exactly once per real change — never per draw call — and that report
    /// serialization stays bounded no matter how many attempts fail.
    /// </summary>
    public class InspectorPerformanceTests
    {
        [Test]
        public void ResolvedPreviewResolvesOncePerSettingsChange()
        {
            var cache = new TrackInspectorPreviewCache();
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var designer = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);

                // Simulated repaint storm: only the FIRST call may resolve.
                cache.MarkSettingsDirty();
                for (int repaint = 0; repaint < 50; repaint++)
                    cache.RefreshResolvedIfNeeded(config, designer);
                Assert.AreEqual(1, cache.ResolveCount,
                    "Idle inspector repaints resolved the settings again — the cache contract is broken.");

                // A real settings change refreshes exactly once more.
                designer.Scale.TargetLapTimeSeconds += 5f;
                cache.MarkSettingsDirty();
                for (int repaint = 0; repaint < 50; repaint++)
                    cache.RefreshResolvedIfNeeded(config, designer);
                Assert.AreEqual(2, cache.ResolveCount);
                Assert.IsTrue(cache.HasResolved);
                Assert.IsNotEmpty(cache.ResolvedSummary);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ReportSummaryRebuildsOncePerGenerationRevision()
        {
            var cache = new TrackInspectorPreviewCache();
            var report = new TrackGenerationReport { Success = true, Seed = 42, AttemptsEvaluated = 3 };
            var metrics = new TrackGenerationMetrics { LapLengthMeters = 12000f, TurnCount = 12 };

            for (int repaint = 0; repaint < 50; repaint++)
                cache.RefreshReportIfNeeded(report, metrics, generationRevision: 1);
            Assert.AreEqual(1, cache.SummaryRebuildCount,
                "Idle inspector repaints rebuilt the report summary — the cache contract is broken.");
            Assert.IsTrue(cache.HasReport);
            StringAssert.Contains("GENERATED SUCCESSFULLY", cache.ReportHeadline);

            for (int repaint = 0; repaint < 50; repaint++)
                cache.RefreshReportIfNeeded(report, metrics, generationRevision: 2);
            Assert.AreEqual(2, cache.SummaryRebuildCount);
        }

        [Test]
        public void ReportFailureEntriesAreCappedButCountsStayExact()
        {
            var report = new TrackGenerationReport();
            const int added = 2000;
            for (int i = 0; i < added; i++)
                report.AddFailure(i, GenerationFailureReason.ClosurePositionFailure, "test", $"failure {i}");

            Assert.LessOrEqual(report.Failures.Count, TrackGenerationReport.MaxStoredFailures,
                "Detailed failure entries must stay capped — unbounded lists bloat scene serialization and inspector traversal.");
            Assert.AreEqual(added, report.TotalFailureCount, "Aggregate counts must stay exact past the cap.");

            var byReason = report.FailureCountsByReason();
            Assert.AreEqual(1, byReason.Count);
            Assert.AreEqual(GenerationFailureReason.ClosurePositionFailure, byReason[0].reason);
            Assert.AreEqual(added, byReason[0].count);

            // The kept entries are the most recent ones (the useful tail for diagnosis).
            StringAssert.Contains($"failure {added - 1}", report.Failures[report.Failures.Count - 1].Message);
        }

        [Test]
        public void FallbackFailureMergeCannotBypassSerializedDetailCap()
        {
            var earlier = new TrackGenerationReport();
            var retry = new TrackGenerationReport();
            const int earlierCount = 450;
            const int retryCount = 275;

            for (int i = 0; i < earlierCount; i++)
                earlier.AddFailure(i, GenerationFailureReason.ClosurePositionFailure, "first", $"earlier {i}");
            for (int i = 0; i < retryCount; i++)
                retry.AddFailure(i, GenerationFailureReason.SelfIntersection, "retry", $"retry {i}");

            retry.PrependFailuresFrom(earlier);

            Assert.AreEqual(TrackGenerationReport.MaxStoredFailures, retry.Failures.Count);
            Assert.AreEqual(earlierCount + retryCount, retry.TotalFailureCount,
                "The bounded detail list must not lose aggregate failure counts.");
            StringAssert.Contains($"retry {retryCount - 1}", retry.Failures[retry.Failures.Count - 1].Message,
                "The newest retry failure should remain in the retained tail.");
        }
    }
}
