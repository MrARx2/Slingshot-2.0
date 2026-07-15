using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using TrackGeneration.Validation;

namespace TrackGeneration.Tests
{
    /// <summary>Determinism: same seed + settings + rulebook → identical selected result.</summary>
    public class DeterminismTests
    {
        [TestCase(12345)]
        [TestCase(-987654)]
        [TestCase(1)]
        public void SameSeedProducesIdenticalLayout(int seed)
        {
            var a = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), seed);
            var b = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), seed);

            Assert.AreEqual(a.Success, b.Success, "Success flag must be deterministic.");
            if (!a.Success) return;

            Assert.AreEqual(TrackGenerationTestUtil.Fingerprint(a.Layout), TrackGenerationTestUtil.Fingerprint(b.Layout),
                "Layout fingerprint must be identical for identical inputs.");
        }

        [Test]
        public void DifferentSeedsProduceDifferentLayouts()
        {
            var a = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), 111);
            var b = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), 222);
            if (!a.Success || !b.Success) Assert.Inconclusive("One of the seeds failed to generate.");

            Assert.AreNotEqual(TrackGenerationTestUtil.Fingerprint(a.Layout), TrackGenerationTestUtil.Fingerprint(b.Layout),
                "Different seeds should not produce identical layouts.");
        }

        [Test]
        public void MeshDensityDoesNotAlterLayoutDecisions()
        {
            var s1 = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            var s2 = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s2.Generation.MetersPerRing = Mathf.Max(1.2f, s1.Generation.MetersPerRing * 0.6f);

            var a = TrackGenerationTestUtil.Generate(s1, 424242);
            var b = TrackGenerationTestUtil.Generate(s2, 424242);
            if (!a.Success || !b.Success) Assert.Inconclusive("Generation failed.");

            Assert.AreEqual(a.Layout.Sections.Count, b.Layout.Sections.Count, "Section count must not depend on mesh density.");
            for (int i = 0; i < a.Layout.Sections.Count; i++)
            {
                Assert.AreEqual(a.Layout.Sections[i].Definition.SectionType, b.Layout.Sections[i].Definition.SectionType,
                    $"Section {i} type changed with mesh density.");
                Assert.AreEqual(a.Layout.Sections[i].Definition.Length, b.Layout.Sections[i].Definition.Length, 0.01f,
                    $"Section {i} length changed with mesh density.");
            }
        }
    }

    /// <summary>Required features/patterns must actually appear, or generation must fail loudly.</summary>
    public class FeatureGuaranteeTests
    {
        [Test]
        public void RequiredFeaturesAppearOnSuccessfulTrack()
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s.Scale.TargetLapTimeSeconds = 90f;
            s.Scale.MaxTrackLengthMeters = 60000f; // heavy required content needs the full rulebook cap
            s.Features.Loops.MinimumCount = 1; s.Features.Loops.MaximumCount = 2;
            s.Features.Corkscrews.MinimumCount = 1; s.Features.Corkscrews.MaximumCount = 2;
            s.Features.Spirals.MinimumCount = 1; s.Features.Spirals.MaximumCount = 2;
            s.Features.RequiredPatterns.Add(new RequiredPatternEntry
            {
                Pattern = TrackPatternType.HalfLoopToCorkscrew,
                Count = 1
            });
            s.Quarters.MinimumDualQuarterCount = 0;

            // Four orientation features on one lap need VERTICAL ROOM (a half-loop exits
            // ≈2× its radius above grade and that height must be paid back) and a deep
            // search — the settings a designer would actually pair with this request.
            s.Elevation.TargetElevationAmplitude = 350f;
            s.Elevation.MaxClimbAngle = 34f;
            s.Elevation.MaxDropAngle = 36f;
            s.Generation.MaxAttempts = 384;

            bool anySuccess = false;
            for (int seed = 100; seed < 124 && !anySuccess; seed++)
            {
                var result = TrackGenerationTestUtil.Generate(s.Clone(), seed);
                if (!result.Success)
                {
                    var sb = new System.Text.StringBuilder($"seed {seed} failed:");
                    foreach (var (reason, count) in result.Report.FailureCountsByReason())
                        sb.Append($" {reason}×{count}");
                    int shown = 0;
                    foreach (var f in result.Report.Failures)
                        if (shown++ < 2) sb.Append($"\n   e.g. {f.Message}");
                    Debug.Log(sb.ToString());
                    continue;
                }
                anySuccess = true;

                Assert.GreaterOrEqual(TrackGenerationTestUtil.Count(result.Layout, TrackMacroSectionType.Loop), 1, "Missing required loop.");
                Assert.GreaterOrEqual(TrackGenerationTestUtil.Count(result.Layout, TrackMacroSectionType.Corkscrew), 1, "Missing required corkscrew.");
                Assert.GreaterOrEqual(TrackGenerationTestUtil.Count(result.Layout, TrackMacroSectionType.Spiral), 1, "Missing required spiral.");

                bool hasHalfLoopToCork = false;
                foreach (var sec in result.Layout.Sections)
                    if (sec.Definition.SectionType == TrackMacroSectionType.HalfLoopTwist &&
                        Mathf.Abs(sec.Definition.RollChange) > 400f)
                        hasHalfLoopToCork = true;
                Assert.IsTrue(hasHalfLoopToCork, "Missing required HalfLoopToCorkscrew pattern.");
            }

            Assert.IsTrue(anySuccess, "No seed in the test window produced a valid track with the required features.");
        }

        [Test]
        public void ImpossibleRequiredPatternFailsWithReport()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                // Disallow corkscrews at the rulebook level, then REQUIRE one.
                var so = new UnityEditor.SerializedObject(config);
                so.FindProperty("allowCorkscrews").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                config.Validate();

                var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                s.Features.Corkscrews.MinimumCount = 1;
                s.Quarters.MinimumDualQuarterCount = 0;

                var result = TrackGenerationTestUtil.Generate(s, 555, config);
                Assert.IsFalse(result.Success, "Requiring a disallowed feature must fail, never silently succeed.");
                Assert.IsTrue(result.Report.Failures.Count > 0, "Failure must be reported with reasons.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void MaximumCountsAreNeverExceeded()
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster);
            s.Quarters.MinimumDualQuarterCount = 0;
            for (int seed = 300; seed < 306; seed++)
            {
                var result = TrackGenerationTestUtil.Generate(s, seed);
                if (!result.Success) continue;

                Assert.LessOrEqual(TrackGenerationTestUtil.Count(result.Layout, TrackMacroSectionType.Loop),
                    s.Features.Loops.MaximumCount, "Loop maximum exceeded.");
                Assert.LessOrEqual(TrackGenerationTestUtil.Count(result.Layout, TrackMacroSectionType.Corkscrew),
                    s.Features.Corkscrews.MaximumCount, "Corkscrew maximum exceeded.");
            }
        }
    }

    /// <summary>Per-preset stress across many deterministic seeds.</summary>
    public class PresetStressTests
    {
        private const int SeedsPerPreset = 100;
        private const float RequiredSuccessRate = 0.95f;

        [TestCase(TrackStylePresetLibrary.Flowing)]
        [TestCase(TrackStylePresetLibrary.Balanced)]
        [TestCase(TrackStylePresetLibrary.Technical)]
        [TestCase(TrackStylePresetLibrary.Velocity)]
        [TestCase(TrackStylePresetLibrary.Rollercoaster)]
        [TestCase(TrackStylePresetLibrary.Switchback)]
        [Timeout(900000)] // 100 corner-heavy seeds legitimately exceed NUnit's 180 s default
        public void PresetGeneratesReliably(string presetName)
        {
            int successes = 0;
            var failureCounts = new Dictionary<GenerationFailureReason, int>();
            var sampleMessages = new Dictionary<GenerationFailureReason, List<string>>();
            float lapSum = 0f;
            int turnSum = 0, ringSum = 0;

            for (int i = 0; i < SeedsPerPreset; i++)
            {
                int seed = presetName.GetHashCode() * 31 + i * 7919;
                var result = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(presetName), seed);

                if (result.Success)
                {
                    successes++;
                    lapSum += result.Layout.Metrics.LapLengthMeters;
                    turnSum += result.Layout.Metrics.TurnCount;
                    ringSum += result.Layout.Metrics.TotalRings;
                }
                else
                {
                    foreach (var (reason, count) in result.Report.FailureCountsByReason())
                    {
                        failureCounts.TryGetValue(reason, out int c);
                        failureCounts[reason] = c + count;
                    }
                    foreach (var f in result.Report.Failures)
                    {
                        if (!sampleMessages.TryGetValue(f.Reason, out var list))
                            sampleMessages[f.Reason] = list = new List<string>();
                        if (list.Count < 3) list.Add($"[{f.Subject}] {f.Message}");
                    }
                }
            }

            float rate = (float)successes / SeedsPerPreset;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{presetName}] success {successes}/{SeedsPerPreset} ({rate:P0})");
            if (successes > 0)
                sb.AppendLine($"  avg lap {lapSum / successes / 1000f:F1}km, avg turns {(float)turnSum / successes:F1}, avg rings {(float)ringSum / successes:F0}");
            foreach (var kv in failureCounts)
            {
                sb.AppendLine($"  failure {kv.Key}: ×{kv.Value}");
                if (sampleMessages.TryGetValue(kv.Key, out var samples))
                    foreach (var msg in samples) sb.AppendLine($"      e.g. {msg}");
            }
            Debug.Log(sb.ToString().TrimEnd());

            Assert.GreaterOrEqual(rate, RequiredSuccessRate,
                $"Preset {presetName} success rate {rate:P0} below the {RequiredSuccessRate:P0} target.\n{sb}");
        }
    }

    /// <summary>
    /// The 4-quarter topology: dual quarters are jump-gated (choice in the air), lap
    /// length counts road A only, roads stay similar in length, both roads agree on
    /// gate progress, and the Quarter seed stream never touches the corner skeleton.
    /// </summary>
    public class QuarterTests
    {
        private static TrackGenerationResult GenerateWithDual(int seed, int minDual = 1, int maxDual = 1)
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s.Quarters.MinimumDualQuarterCount = minDual;
            s.Quarters.MaximumDualQuarterCount = maxDual;
            s.Features.MinFeatureGroups = 0;
            s.Features.MaxFeatureGroups = 2;
            return TrackGenerationTestUtil.Generate(s, seed);
        }

        private static TrackGenerationResult ScanForDual(int firstSeed, int scan, int minDual = 1, int maxDual = 1)
        {
            TrackGenerationResult result = null;
            for (int seed = firstSeed; seed < firstSeed + scan; seed++)
            {
                result = GenerateWithDual(seed, minDual, maxDual);
                if (result.Success && result.Layout.Metrics.DualRoadQuarterCount >= minDual) return result;
            }
            return result;
        }

        [Test]
        public void DualQuarterStructureIsJumpGated()
        {
            var result = ScanForDual(900, 20);
            Assert.IsTrue(result != null && result.Success && result.Layout.Metrics.DualRoadQuarterCount >= 1,
                "No seed produced a valid dual-quarter track.");

            Assert.AreEqual(4, result.Layout.Quarters.Count, "A lap always has exactly 4 quarters.");

            var sections = result.Layout.Sections;
            foreach (var q in result.Layout.Quarters)
            {
                if (!q.IsDual) continue;
                Assert.IsNotNull(q.RouteB, "Dual quarter is missing its alternate road.");

                var flareB = sections[q.RouteB.FirstSectionIndex];
                var lipB = sections[q.RouteB.LastSectionIndex];
                var lipA = sections[q.RouteB.FirstSectionIndex - 1];

                // The exit sequence around the alternate road: lipA → [road B chain] → air gap → catch.
                Assert.AreEqual(TrackMacroSectionType.JumpRamp, lipA.Definition.SectionType, "Road A's launch lip missing before the alternate road.");
                Assert.AreEqual(TrackMacroSectionType.LandingRamp, flareB.Definition.SectionType, "Road B must start with its landing flare.");
                Assert.AreEqual(TrackMacroSectionType.JumpRamp, lipB.Definition.SectionType, "Road B must end with its launch lip.");

                var exitGap = sections[q.RouteB.LastSectionIndex + 1];
                var catchSec = sections[q.RouteB.LastSectionIndex + 2];
                Assert.AreEqual(TrackMacroSectionType.AirGap, exitGap.Definition.SectionType, "Exit air gap missing after road B's lip.");
                Assert.AreEqual(TrackMacroSectionType.LandingRamp, catchSec.Definition.SectionType, "Shared convergence catch missing.");

                // Open gate edges — mouths and lips face air, never caps.
                Assert.IsTrue(flareB.OpenStart && !flareB.CapStart, "Road B's mouth must be open.");
                Assert.IsTrue(lipB.OpenEnd && !lipB.CapEnd, "Road B's lip must be open.");
                Assert.IsTrue(lipA.OpenEnd && !lipA.CapEnd, "Road A's lip must be open.");

                // Readable in-air choice: lanes separated at the mouths AND at the lips.
                float mouthSep = Vector3.Distance(q.RouteA.EntryFrame.Position, flareB.StartFrame.Position);
                float lipSep = Vector3.Distance(lipA.EndFrame.Position, lipB.EndFrame.Position);
                Assert.GreaterOrEqual(lipSep, 16f, "Launch lips not separated — no lane identity at the exit.");
                Assert.GreaterOrEqual(Vector3.Distance(
                    FindEntryFlareA(sections, q).StartFrame.Position, flareB.StartFrame.Position), 16f,
                    "Landing mouths not separated — no in-air choice.");

                // Broad shared catch that can receive both lanes.
                Assert.Greater(catchSec.StartFrame.Width, result.Layout.Sections[0].StartFrame.Width * 1.15f,
                    "Catch is not broad enough for two arriving lanes.");

                // Both roads agree on gate lap progress; road B progress is monotonic.
                var flareA = FindEntryFlareA(sections, q);
                Assert.AreEqual(flareA.StartFrame.LapProgress, flareB.StartFrame.LapProgress, 0.002f, "Mouth progress differs.");
                Assert.AreEqual(lipA.EndFrame.LapProgress, lipB.EndFrame.LapProgress, 0.002f, "Lip progress differs.");
                for (int i = q.RouteB.FirstSectionIndex; i <= q.RouteB.LastSectionIndex; i++)
                    AssertMonotonicProgress(sections[i]);
            }
        }

        private static GeneratedTrackSection FindEntryFlareA(System.Collections.Generic.List<GeneratedTrackSection> sections,
            TrackGeneration.Planning.GeneratedTrackQuarter q)
        {
            for (int i = q.RouteA.FirstSectionIndex; i <= q.RouteA.LastSectionIndex; i++)
            {
                if (sections[i].RoadId == 0 && sections[i].OpenStart &&
                    sections[i].Definition.SectionType == TrackMacroSectionType.LandingRamp)
                    return sections[i];
            }
            Assert.Fail("Road A's landing flare not found.");
            return null;
        }

        private static void AssertMonotonicProgress(GeneratedTrackSection sec)
        {
            if (sec.SubdivisionFrames == null) return;
            for (int i = 1; i < sec.SubdivisionFrames.Length; i++)
            {
                Assert.GreaterOrEqual(sec.SubdivisionFrames[i].LapProgress + 1e-4f, sec.SubdivisionFrames[i - 1].LapProgress,
                    $"Lap progress not monotonic on {sec.Definition.DebugName} at ring {i}.");
            }
        }

        [Test]
        public void LapLengthCountsRoadAOnly()
        {
            var result = ScanForDual(1000, 20);
            Assert.IsTrue(result != null && result.Success && result.Layout.Metrics.DualRoadQuarterCount >= 1);

            // The canonical lap = the last (canonical) section's arc. Summing EVERY
            // section's span exceeds it exactly by the alternate roads' lengths —
            // a Dual Road Quarter never duplicates lap length.
            float sumAll = 0f;
            float sumAlternate = 0f;
            foreach (var sec in result.Layout.Sections)
            {
                float span = sec.EndFrame.ArcLength - sec.StartFrame.ArcLength;
                sumAll += span;
                if (sec.RoadId == 1) sumAlternate += span;
            }

            Assert.Greater(sumAlternate, 0f, "Expected alternate road sections.");
            Assert.Greater(sumAll, result.Layout.LapLength, "Alternate roads must not be part of the lap arc.");
            Assert.AreEqual(result.Layout.LapLength, result.Layout.Metrics.LapLengthMeters, 0.01f);

            foreach (var q in result.Layout.Quarters)
            {
                if (!q.IsDual) continue;
                Assert.Greater(q.RouteB.PhysicalLengthMeters, 0f);
            }
        }

        [Test]
        public void DualRoadsHaveSimilarLength()
        {
            var result = ScanForDual(1100, 20);
            Assert.IsTrue(result != null && result.Success && result.Layout.Metrics.DualRoadQuarterCount >= 1);

            var sections = result.Layout.Sections;
            foreach (var q in result.Layout.Quarters)
            {
                if (!q.IsDual) continue;

                // Compare like spans: mouth→lip on both roads.
                var flareA = FindEntryFlareA(sections, q);
                var lipA = sections[q.RouteB.FirstSectionIndex - 1];
                float lenA = lipA.EndFrame.ArcLength - flareA.StartFrame.ArcLength;
                float lenB = q.RouteB.PhysicalLengthMeters;

                Assert.AreEqual(lenA, lenB, lenA * 0.25f,
                    $"Quarter {q.QuarterIndex}: road lengths differ too much (A {lenA:F0}m, B {lenB:F0}m).");
            }
        }

        [Test]
        public void RouteTimeTableCoversAllArchetypes()
        {
            var result = ScanForDual(950, 20);
            Assert.IsTrue(result != null && result.Success && result.Layout.Metrics.DualRoadQuarterCount >= 1);

            TrackGeneration.Planning.QuarterRouteBalance balance = null;
            foreach (var q in result.Layout.Quarters)
                if (q.IsDual && q.Balance != null) { balance = q.Balance; break; }

            Assert.IsNotNull(balance, "Dual quarter has no balance table.");
            Assert.AreEqual(6, balance.TimeTable.Count, "Expected a time estimate per craft archetype.");
            foreach (var row in balance.TimeTable)
            {
                Assert.Greater(row.RouteASeconds, 0f);
                Assert.Greater(row.RouteBSeconds, 0f);
            }
        }

        [Test]
        [Timeout(300000)]
        public void DualQuarterCanOwnFeaturePatternsOnRoadA()
        {
            TrackGenerationResult found = null;
            GeneratedTrackQuarter foundQuarter = null;

            for (int seed = 1200; seed < 1224 && foundQuarter == null; seed++)
            {
                var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster);
                settings.Quarters.MinimumDualQuarterCount = 1;
                settings.Quarters.MaximumDualQuarterCount = 1;
                settings.Features.MinFeatureGroups = Mathf.Max(4, settings.Features.MinFeatureGroups);
                settings.Features.MaxFeatureGroups = Mathf.Max(6, settings.Features.MaxFeatureGroups);
                settings.Generation.MaxAttempts = Mathf.Max(192, settings.Generation.MaxAttempts);

                var result = TrackGenerationTestUtil.Generate(settings, seed);
                if (!result.Success) continue;
                foreach (var quarter in result.Layout.Quarters)
                {
                    if (!quarter.IsDual || quarter.RouteA == null || !quarter.RouteA.HasFeatures) continue;
                    found = result;
                    foundQuarter = quarter;
                    break;
                }
            }

            Assert.IsNotNull(foundQuarter,
                "No generated dual quarter owned a feature pattern on Road A; dual gaps may have become reserved again.");
            Assert.IsFalse(foundQuarter.RouteB.HasFeatures,
                "The fitted alternate road should remain feature-free until it has an explicit feature-placement solver.");

            var seen = new HashSet<string>();
            foreach (string id in foundQuarter.RouteA.FeaturePatternIds)
            {
                Assert.IsFalse(string.IsNullOrEmpty(id));
                Assert.IsFalse(id.StartsWith("Quarter_"), "Gate pattern IDs are not route features.");
                Assert.IsTrue(seen.Add(id), "Route feature IDs must be distinct.");

                bool present = false;
                for (int i = foundQuarter.RouteA.FirstSectionIndex; i <= foundQuarter.RouteA.LastSectionIndex; i++)
                {
                    var sec = found.Layout.Sections[i];
                    if (sec.RoadId == 0 && sec.PatternId == id) { present = true; break; }
                }
                Assert.IsTrue(present, $"Route metadata lists feature '{id}' but no Road-A section owns it.");
            }
        }

        [Test]
        public void ZeroDualQuartersIsCleanFourQuarterTrack()
        {
            var result = GenerateWithDual(4321, minDual: 0, maxDual: 0);
            Assert.IsTrue(result.Success, "Zero-dual track failed to generate.");
            Assert.AreEqual(0, result.Layout.Metrics.DualRoadQuarterCount);
            Assert.AreEqual(4, result.Layout.Quarters.Count, "Even a single-road lap records exactly 4 quarters.");

            foreach (var sec in result.Layout.Sections)
            {
                Assert.AreEqual(0, sec.RoadId, "No alternate road sections on a zero-dual track.");
                Assert.IsTrue(sec.QuarterIndex >= 0 && sec.QuarterIndex <= 3,
                    $"Section '{sec.Definition.DebugName}' has no quarter ({sec.QuarterIndex}).");
                Assert.IsTrue(string.IsNullOrEmpty(sec.PatternId) || !sec.PatternId.StartsWith("Quarter_"),
                    "No quarter gate pieces may exist without dual quarters.");
            }

            // Quarters are contiguous and ascending along the canonical road.
            int prevQuarter = 0;
            foreach (var sec in result.Layout.Sections)
            {
                Assert.GreaterOrEqual(sec.QuarterIndex, prevQuarter, "Quarter indices must never go backwards.");
                prevQuarter = sec.QuarterIndex;
            }
        }

        [Test]
        public void QuarterSeedNeverChangesTheCornerSkeleton()
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s.Quarters.MinimumDualQuarterCount = 0;
            s.Quarters.MaximumDualQuarterCount = 2;

            // The invariant is PER ATTEMPT: a different Quarter seed may legitimately
            // fail an attempt the other run passes (gate feasibility), advancing the
            // whole generation to a fresh Layout draw. Compare skeletons only when
            // both runs succeeded on the SAME attempt.
            TrackGenerationResult resultA = null, resultB = null;
            for (int master = 777; master < 789; master++)
            {
                var streamsA = new TrackSeedStreams();
                streamsA.DeriveAllFrom(master);
                var streamsB = streamsA.Clone();
                streamsB.QuarterSeed = streamsA.QuarterSeed + 12345;

                resultA = TrackGenerationTestUtil.Generate(s, master, streams: streamsA);
                resultB = TrackGenerationTestUtil.Generate(s, master, streams: streamsB);
                if (resultA.Success && resultB.Success &&
                    resultA.AttemptsEvaluated == resultB.AttemptsEvaluated)
                    break;
                resultA = null;
            }
            Assert.IsNotNull(resultA, "No master seed in the window succeeded on the same attempt for both quarter seeds.");

            Assert.AreEqual(CornerSkeleton(resultA.Layout), CornerSkeleton(resultB.Layout),
                "Re-rolling only the Quarter seed must keep the corner skeleton (angles, directions, order).");
        }

        private static string CornerSkeleton(GeneratedTrackLayout layout)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var sec in layout.Sections)
            {
                if (sec.RoadId == 1) continue;
                var t = sec.Definition.SectionType;
                if (t != TrackMacroSectionType.BankedCurve && t != TrackMacroSectionType.BankedHairpin &&
                    t != TrackMacroSectionType.WallrideTurn)
                    continue;
                sb.Append(t).Append(':').Append(sec.Definition.TurnAngle.ToString("F0"))
                  .Append(':').Append(sec.Definition.TurnSign).Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void JumpsDisallowedResolvesToZeroDualQuarters()
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s.Quarters.MinimumDualQuarterCount = 1;
            s.Quarters.MaximumDualQuarterCount = 2;
            s.Features.Jumps = new TrackFeatureRule(false, 0, 0, 0f);

            var config = TrackGenerationTestUtil.CreateConfig();
            var so = new UnityEditor.SerializedObject(config);
            so.FindProperty("allowJumps").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            config.Validate();

            var resolved = ResolvedTrackGenerationConfig.Resolve(config, s);
            Assert.AreEqual(0, resolved.MaxDualQuarters,
                "Dual quarters are jump-gated: with jumps disallowed they must resolve to 0.");
            bool warned = false;
            foreach (var issue in resolved.Issues)
                if (issue.Field.StartsWith("Quarters") && issue.Severity == ResolvedIssueSeverity.Warning) warned = true;
            Assert.IsTrue(warned, "Resolution must warn (not error) when dual quarters are dropped.");
            Assert.IsFalse(resolved.HasHardErrors, "Dropping dual quarters is a warning, never a hard error.");

            Object.DestroyImmediate(config);
        }
    }

    /// <summary>Geometry: NaN-free frames, weld continuity, closure, budgets, open air gaps.</summary>
    public class GeometryTests
    {
        [Test]
        public void GeneratedGeometryIsClean()
        {
            var result = TrackGenerationTestUtil.Generate(
                TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster), 777);
            if (!result.Success)
                result = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster), 778);
            Assert.IsTrue(result.Success, "Rollercoaster generation failed for both test seeds.");

            var layout = result.Layout;

            foreach (var sec in layout.Sections)
            {
                if (sec.SubdivisionFrames == null) continue;
                foreach (var f in sec.SubdivisionFrames)
                {
                    Assert.IsFalse(float.IsNaN(f.Position.x + f.Position.y + f.Position.z), $"NaN position in {sec.Definition.DebugName}");
                    Assert.Greater(f.Width, 1f, $"Degenerate width in {sec.Definition.DebugName}");
                    Assert.AreEqual(1f, f.Forward.magnitude, 0.02f, $"Non-unit forward in {sec.Definition.DebugName}");
                }
            }

            // Closure: last ring welds exactly to the start frame.
            var first = layout.Sections[0];
            var last = layout.Sections[layout.Sections.Count - 1];
            Assert.Less(Vector3.Distance(last.EndFrame.Position, first.StartFrame.Position), 0.01f, "Closure weld not exact.");
            Assert.Less(Vector3.Angle(last.EndFrame.Forward, first.StartFrame.Forward), 0.1f, "Closure heading not exact.");

            // Air-gap boundaries stay open.
            foreach (var sec in layout.Sections)
            {
                if (sec.Definition.SectionType == TrackMacroSectionType.JumpRamp)
                    Assert.IsTrue(sec.OpenEnd && !sec.CapEnd, "Jump lip must stay open.");
                if (sec.Definition.SectionType == TrackMacroSectionType.LandingRamp)
                    Assert.IsTrue(sec.OpenStart && !sec.CapStart, "Landing mouth must stay open.");
            }

            Assert.LessOrEqual(layout.Metrics.TotalRings, 40000, "Ring budget exceeded.");
        }

        [Test]
        public void WeldsAreContinuousBetweenSections()
        {
            TrackGenerationResult result = null;
            for (int s = 31337; s < 31345 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), s);
            Assert.IsTrue(result is { Success: true }, "Generation failed for every seed in the window.");

            var sections = result.Layout.Sections;
            for (int i = 0; i < sections.Count - 1; i++)
            {
                var cur = sections[i];
                var next = sections[i + 1];
                if (cur.OpenEnd || next.OpenStart) continue;   // air gaps are open by design
                if (cur.RoadId == 1 || next.RoadId == 1) continue; // alternate roads meet the lap only over air gaps

                Assert.Less(Vector3.Distance(cur.EndFrame.Position, next.StartFrame.Position), 0.02f,
                    $"Weld gap between {cur.Definition.DebugName} and {next.Definition.DebugName}");
            }
        }
    }

    /// <summary>
    /// Wall cleanliness: the wall-top line must never step or wave. Scans every ring's
    /// wall-top offset (side height × wall multiplier + lip) and rejects per-meter jumps —
    /// this is the numeric form of "very smooth, blended transitions at all times".
    /// </summary>
    public class WallSmoothnessTests
    {
        [TestCase(TrackStylePresetLibrary.Balanced, 5001)]
        [TestCase(TrackStylePresetLibrary.Switchback, 5002)]
        public void WallTopsChangeGradually(string preset, int seed)
        {
            TrackGenerationResult result = null;
            for (int s = seed; s < seed + 8 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(preset), s);
            Assert.IsTrue(result is { Success: true }, "No valid track generated for the smoothness scan.");

            var roadProfile = new TrackRoadProfileSettings();
            float worstRate = 0f;
            string worstAt = "";

            foreach (var sec in result.Layout.Sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;
                if (sec.Definition.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                    sec.Definition.SectionType == TrackMacroSectionType.Loop ||
                    sec.Definition.SectionType == TrackMacroSectionType.Corkscrew) continue;

                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);

                    // Wall-top offset above the floor baseline, both sides: the pure wall
                    // signal, independent of road elevation/pitch.
                    float TopOffset(TrackConnectionFrame f, bool left)
                    {
                        float side = f.SideHeight > 0.001f ? f.SideHeight : roadProfile.SideHeight;
                        float mult = left ? f.LeftWallMultiplier : f.RightWallMultiplier;
                        float h = roadProfile.HeightAt(1f, f.Width * 0.5f, side) * mult;
                        return h + roadProfile.SafetyLipHeight * mult;
                    }

                    float rateL = Mathf.Abs(TopOffset(frames[i], true) - TopOffset(frames[i - 1], true)) / ds;
                    float rateR = Mathf.Abs(TopOffset(frames[i], false) - TopOffset(frames[i - 1], false)) / ds;
                    float rate = Mathf.Max(rateL, rateR);
                    if (rate > worstRate)
                    {
                        worstRate = rate;
                        worstAt = $"{sec.Definition.DebugName} ring {i}";
                    }
                }
            }

            // 0.12 m of wall-height change per meter of track ≈ a 7° wall-top grade —
            // anything steeper reads as a step or spike at 361 m/s.
            Assert.LessOrEqual(worstRate, 0.12f,
                $"Wall top changes too abruptly ({worstRate:F3} m/m at {worstAt}) — walls must blend smoothly.");
        }

        /// <summary>
        /// Retopology invariant: ring spacing may never STEP — not inside a section and
        /// not across a section connection (the reported "tighter rings at connections"
        /// bug). Air-gap boundaries are the only legal breaks.
        /// </summary>
        [TestCase(TrackStylePresetLibrary.Balanced, 6001)]
        [TestCase(TrackStylePresetLibrary.Rollercoaster, 6002)]
        public void RingSpacingIsUniform(string preset, int seed)
        {
            TrackGenerationResult result = null;
            for (int s = seed; s < seed + 8 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(preset), s);
            Assert.IsTrue(result is { Success: true }, "No valid track generated for the ring-spacing scan.");

            var sections = result.Layout.Sections;
            float worstRatio = 1f;
            string worstAt = "";

            void Check(float dsA, float dsB, string where)
            {
                if (dsA <= 1e-3f || dsB <= 1e-3f) return;
                float ratio = Mathf.Max(dsA, dsB) / Mathf.Min(dsA, dsB);
                if (ratio > worstRatio)
                {
                    worstRatio = ratio;
                    worstAt = where;
                }
            }

            for (int sIdx = 0; sIdx < sections.Count; sIdx++)
            {
                var sec = sections[sIdx];
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;

                for (int i = 2; i < frames.Length; i++)
                    Check(frames[i - 1].ArcLength - frames[i - 2].ArcLength,
                          frames[i].ArcLength - frames[i - 1].ArcLength,
                          $"{sec.Definition.DebugName} ring {i}");

                // Across the connection to the next meshed section (skip air gaps).
                var next = sections[(sIdx + 1) % sections.Count];
                if (sec.OpenEnd || next.OpenStart) continue;
                if (next.SubdivisionFrames == null || next.SubdivisionFrames.Length < 2) continue;
                if (next.RoadId == 1) continue; // an alternate road starts its own chain at its mouth

                var nf = next.SubdivisionFrames;
                Check(frames[frames.Length - 1].ArcLength - frames[frames.Length - 2].ArcLength,
                      nf[1].ArcLength - nf[0].ArcLength,
                      $"{sec.Definition.DebugName} → {next.Definition.DebugName}");
            }

            // A 25% spacing change over one ring is the edge of visibility; a subdivision
            // step (2×) must never survive retopology.
            Assert.LessOrEqual(worstRatio, 1.25f,
                $"Ring spacing steps by ×{worstRatio:F2} at {worstAt} — the ring grid must stay uniform.");
        }
    }

    /// <summary>The Size modifier must scale the whole coupled parameter set, not just the cap.</summary>
    public class SizeModifierTests
    {
        [Test]
        public void SizeModifierScalesCoupledParameters()
        {
            var small = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            var baseline = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            TrackSizeModifier.Apply(small, TrackSizeLevel.Small);

            Assert.Less(small.Scale.TargetLapTimeSeconds, baseline.Scale.TargetLapTimeSeconds);
            Assert.Less(small.Scale.MaxTrackLengthMeters, baseline.Scale.MaxTrackLengthMeters);
            Assert.LessOrEqual(small.Layout.MaxTurnCount, baseline.Layout.MaxTurnCount);
            Assert.LessOrEqual(small.Features.MaxFeatureGroups, baseline.Features.MaxFeatureGroups);
            Assert.LessOrEqual(small.Quarters.MaximumDualQuarterCount, baseline.Quarters.MaximumDualQuarterCount);
            Assert.IsTrue(small.ModifiedSincePreset, "Size modifier must mark the settings as customized.");

            var huge = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            TrackSizeModifier.Apply(huge, TrackSizeLevel.Huge);
            Assert.Greater(huge.Scale.TargetLapTimeSeconds, baseline.Scale.TargetLapTimeSeconds);
            Assert.GreaterOrEqual(huge.Layout.MaxTurnCount, baseline.Layout.MaxTurnCount);
        }

        [Test]
        public void SmallTracksGenerateSmallerThanHuge()
        {
            float smallLap = 0f, hugeLap = 0f;

            for (int seed = 4000; seed < 4010 && smallLap <= 0f; seed++)
            {
                var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                TrackSizeModifier.Apply(s, TrackSizeLevel.Small);
                var result = TrackGenerationTestUtil.Generate(s, seed);
                if (result.Success) smallLap = result.Layout.Metrics.LapLengthMeters;
            }

            for (int seed = 4000; seed < 4010 && hugeLap <= 0f; seed++)
            {
                var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                TrackSizeModifier.Apply(s, TrackSizeLevel.Huge);
                var result = TrackGenerationTestUtil.Generate(s, seed);
                if (result.Success) hugeLap = result.Layout.Metrics.LapLengthMeters;
            }

            Assert.Greater(smallLap, 0f, "No Small-size track generated in the seed window.");
            Assert.Greater(hugeLap, 0f, "No Huge-size track generated in the seed window.");
            Assert.Less(smallLap, hugeLap, $"Small ({smallLap / 1000f:F1}km) should be shorter than Huge ({hugeLap / 1000f:F1}km).");
        }
    }

    /// <summary>Everything must be scaled for ≈1300 km/h — no car-sized geometry.</summary>
    public class HighSpeedScaleTests
    {
        [Test]
        public void ApproachesAreHundredsOfMeters()
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var resolved = ResolvedTrackGenerationConfig.Resolve(config, s);
                Assert.Greater(resolved.DefaultApproachLength, 200f, "Default approach must be hundreds of meters at 1300 km/h.");
                Assert.Greater(resolved.DefaultRecoveryLength, 150f);
                Assert.Greater(resolved.DangerousSpacingLength, 400f);
                Assert.GreaterOrEqual(resolved.MinLoopRadius, 160f, "Loop radii must use the high-speed scale.");
                Assert.Greater(resolved.MinCorkscrewLength, 400f, "Corkscrew length must stay readable at speed.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void JumpsAreTrajectoryDerived()
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s.Features.Jumps.MinimumCount = 1;
            s.Features.Jumps.MaximumCount = 2;
            s.Quarters.MinimumDualQuarterCount = 0;

            TrackGenerationResult result = null;
            for (int seed = 800; seed < 812; seed++)
            {
                result = TrackGenerationTestUtil.Generate(s, seed);
                if (result.Success) break;
            }
            Assert.IsTrue(result is { Success: true }, "No valid jump track generated.");

            bool sawJump = false;
            var sections = result.Layout.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                var def = sections[i].Definition;
                if (def.SectionType == TrackMacroSectionType.JumpRamp)
                {
                    sawJump = true;
                    Assert.Greater(def.Length, 100f, "Jump ramps must not be car-scale (15 m) at 1300 km/h.");
                }
                if (def.SectionType == TrackMacroSectionType.AirGap)
                {
                    Assert.Greater(def.AirtimeSeconds, 0.2f, "Air gap must carry its ballistic airtime.");
                    float v = 1300f / 3.6f;
                    Assert.Greater(def.PlanHorizontalLength, v * def.AirtimeSeconds * 0.85f,
                        "Air-gap distance must match the ballistic trajectory, not a random pick.");
                    Assert.Less(def.ElevationChange, 0f,
                        "The landing mouth must sit BELOW the launch lip — jumps drop, never climb.");
                }
            }
            Assert.IsTrue(sawJump, "Required jump missing.");
        }

        [Test]
        public void VelocityPresetUsesMonumentalRadii()
        {
            var result = TrackGenerationTestUtil.Generate(
                TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Velocity), 616);
            if (!result.Success)
                result = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Velocity), 617);
            Assert.IsTrue(result.Success, "Velocity generation failed for both seeds.");

            foreach (var sec in result.Layout.Sections)
            {
                if (sec.Definition.SectionType == TrackMacroSectionType.BankedCurve)
                    Assert.GreaterOrEqual(sec.Definition.Radius, 1000f,
                        $"Velocity corners must be monumental, got {sec.Definition.Radius:F0} m.");
            }
        }

        [Test]
        public void TechnicalPresetStaysHighSpeedSafe()
        {
            var result = TrackGenerationTestUtil.Generate(
                TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Technical), 818);
            if (!result.Success)
                result = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Technical), 819);
            Assert.IsTrue(result.Success, "Technical generation failed for both seeds.");

            foreach (var sec in result.Layout.Sections)
            {
                if (sec.Definition.SectionType == TrackMacroSectionType.BankedCurve ||
                    sec.Definition.SectionType == TrackMacroSectionType.BankedHairpin)
                    Assert.GreaterOrEqual(sec.Definition.Radius, 180f,
                        "Even technical corners must respect the high-speed rulebook minimum.");
            }
        }
    }

    public class DualRoadClearanceTests
    {
        private static TrackConnectionFrame[] Line(int count, System.Func<int, Vector3> position)
        {
            var frames = new TrackConnectionFrame[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = TrackConnectionFrame.Origin(100f);
                frames[i].Position = position(i);
                frames[i].ArcLength = i * 10f;
            }
            return frames;
        }

        private static (GeneratedTrackLayout layout, GeneratedTrackQuarter quarter) Pair(
            TrackConnectionFrame[] roadA, TrackConnectionFrame[] roadB)
        {
            var layout = new GeneratedTrackLayout();
            layout.Sections.Add(new GeneratedTrackSection { RoadId = 0, SubdivisionFrames = roadA });
            layout.Sections.Add(new GeneratedTrackSection { RoadId = 1, SubdivisionFrames = roadB });
            var quarter = new GeneratedTrackQuarter
            {
                QuarterIndex = 2,
                QuarterType = TrackQuarterType.DualRoad,
                RouteA = new GeneratedQuarterRoute { RoadId = 0, FirstSectionIndex = 0, LastSectionIndex = 0 },
                RouteB = new GeneratedQuarterRoute { RoadId = 1, FirstSectionIndex = 1, LastSectionIndex = 1 }
            };
            return (layout, quarter);
        }

        [Test]
        public void CrossingNearRouteEndIsRejected()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced));
                var pair = Pair(
                    Line(101, i => new Vector3(i * 10f, 0f, 0f)),
                    Line(101, i => new Vector3(950f, 0f, -500f + i * 10f)));
                var issues = new List<ValidationIssue>();

                TrackValidators.ValidateRoadPairClearance(pair.layout, pair.quarter, resolved, issues);

                Assert.IsTrue(issues.Exists(i => i.IsError && i.Reason == GenerationFailureReason.SelfIntersection),
                    "A road crossing at 95% progress escaped the dual-road clearance validator.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ParallelGateLanesAtResolvedSeparationAreAccepted()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced));
                float separation = Mathf.Max(resolved.RoadWidth, resolved.DualRoadWidth) +
                                   resolved.RoadProfile.SideHeight * 2f + resolved.WallMaskSafetyMargin;
                var pair = Pair(
                    Line(101, i => new Vector3(i * 10f, 0f, 0f)),
                    Line(101, i => new Vector3(i * 10f, 0f, separation)));
                var issues = new List<ValidationIssue>();

                TrackValidators.ValidateRoadPairClearance(pair.layout, pair.quarter, resolved, issues);

                Assert.IsFalse(issues.Exists(i => i.IsError),
                    "Numerical tolerance should allow the intentionally parallel gate lanes at resolved separation.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void InternalFeatureFlightCrossingRoadBIsRejected()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var resolved = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced));
                var layout = new GeneratedTrackLayout();

                var gap = new GeneratedTrackSection
                {
                    RoadId = 0,
                    PatternId = "JumpGap_0",
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.AirGap,
                        AirtimeSeconds = 2f,
                        Length = 200f,
                        PatternId = "JumpGap_0"
                    },
                    SubdivisionFrames = null,
                    StartFrame = TrackConnectionFrame.Origin(100f),
                    EndFrame = TrackConnectionFrame.Origin(100f)
                };
                gap.StartFrame.Position = new Vector3(-100f, 0f, 0f);
                gap.EndFrame.Position = new Vector3(100f, 0f, 0f);
                gap.EndFrame.ArcLength = 200f;

                layout.Sections.Add(new GeneratedTrackSection { RoadId = 0, SubdivisionFrames = null });
                layout.Sections.Add(gap);
                layout.Sections.Add(new GeneratedTrackSection { RoadId = 0, SubdivisionFrames = null });
                layout.Sections.Add(new GeneratedTrackSection
                {
                    RoadId = 1,
                    SubdivisionFrames = Line(101, i => new Vector3(0f, 5f, -500f + i * 10f))
                });

                var quarter = new GeneratedTrackQuarter
                {
                    QuarterIndex = 1,
                    QuarterType = TrackQuarterType.DualRoad,
                    RouteA = new GeneratedQuarterRoute { RoadId = 0, FirstSectionIndex = 0, LastSectionIndex = 2 },
                    RouteB = new GeneratedQuarterRoute { RoadId = 1, FirstSectionIndex = 3, LastSectionIndex = 3 }
                };
                var issues = new List<ValidationIssue>();

                TrackValidators.ValidateRoadPairClearance(layout, quarter, resolved, issues);

                Assert.IsTrue(issues.Exists(i => i.IsError && i.Reason == GenerationFailureReason.SelfIntersection),
                    "Road B crossed an internal feature's ballistic flight path without being rejected.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
