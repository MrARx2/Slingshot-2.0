using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

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
            s.Branches.MinBranchGroups = 0;

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
                s.Branches.MinBranchGroups = 0;

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
            s.Branches.MinBranchGroups = 0;
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

    /// <summary>Branch groups: gate connection, balance, specialization, clearance, lap progress.</summary>
    public class BranchTests
    {
        private static TrackGenerationResult GenerateWithBranches(BranchPairingMode pairing, int seed)
        {
            var s = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
            s.Branches.MinBranchGroups = 1;
            s.Branches.MaxBranchGroups = 1;
            s.Branches.PairingMode = pairing;
            s.Features.MinFeatureGroups = 0;
            s.Features.MaxFeatureGroups = 2;
            return TrackGenerationTestUtil.Generate(s, seed);
        }

        [TestCase(BranchPairingMode.Contrasting)]
        [TestCase(BranchPairingMode.SafeVersusRisky)]
        [TestCase(BranchPairingMode.AlternatingAdvantage)]
        [TestCase(BranchPairingMode.FeatureVersusGround)]
        public void BranchRoutesConnectAndBalance(BranchPairingMode pairing)
        {
            TrackGenerationResult result = null;
            for (int seed = 900; seed < 920; seed++)
            {
                result = GenerateWithBranches(pairing, seed);
                if (result.Success && result.Layout.BranchGroups.Count > 0) break;
            }

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success && result.Layout.BranchGroups.Count > 0,
                $"No seed produced a valid {pairing} branch track.");

            foreach (var group in result.Layout.BranchGroups)
            {
                GeneratedTrackSection secA = null, secB = null;
                foreach (var sec in result.Layout.Sections)
                {
                    if (sec.BranchGroupId != group.BranchGroupId) continue;
                    if (sec.RouteId == 0) secA = sec;
                    if (sec.RouteId == 1) secB = sec;
                }

                Assert.IsNotNull(secA, "Route A section missing.");
                Assert.IsNotNull(secB, "Route B section missing.");

                // Both routes connect to their shared gates.
                Assert.Less(Vector3.Distance(secA.StartFrame.Position, secB.StartFrame.Position), 0.05f, "Entry gates differ.");
                Assert.Less(Vector3.Distance(secA.EndFrame.Position, secB.EndFrame.Position), 0.05f, "Merge gates differ.");

                // Continuous frames (drivable route).
                Assert.Greater(secA.SubdivisionFrames.Length, 8);
                Assert.Greater(secB.SubdivisionFrames.Length, 8);

                // Neutral time within tolerance (safe/risky may carry the sanctioned edge).
                Assert.IsNotNull(group.Balance, "Balance metrics missing.");
                if (pairing != BranchPairingMode.SafeVersusRisky)
                    Assert.LessOrEqual(group.Balance.NeutralTimeDifference, 0.06f,
                        $"Neutral time difference {group.Balance.NeutralTimeDifference:P1} too large.");

                // Lap progress agrees at the gates and is monotonic on each route.
                Assert.AreEqual(secA.StartFrame.LapProgress, secB.StartFrame.LapProgress, 0.001f, "Entry progress differs.");
                Assert.AreEqual(secA.EndFrame.LapProgress, secB.EndFrame.LapProgress, 0.001f, "Merge progress differs.");
                AssertMonotonicProgress(secA);
                AssertMonotonicProgress(secB);
            }
        }

        private static void AssertMonotonicProgress(GeneratedTrackSection sec)
        {
            for (int i = 1; i < sec.SubdivisionFrames.Length; i++)
            {
                Assert.GreaterOrEqual(sec.SubdivisionFrames[i].LapProgress + 1e-4f, sec.SubdivisionFrames[i - 1].LapProgress,
                    $"Lap progress not monotonic on {sec.Definition.DebugName} at ring {i}.");
            }
        }

        [Test]
        public void RouteTimeTableCoversAllArchetypes()
        {
            TrackGenerationResult result = null;
            for (int seed = 950; seed < 970; seed++)
            {
                result = GenerateWithBranches(BranchPairingMode.Contrasting, seed);
                if (result.Success && result.Layout.BranchGroups.Count > 0) break;
            }
            Assert.IsTrue(result != null && result.Success && result.Layout.BranchGroups.Count > 0);

            var balance = result.Layout.BranchGroups[0].Balance;
            Assert.AreEqual(6, balance.TimeTable.Count, "Expected a time estimate per craft archetype.");
            foreach (var row in balance.TimeTable)
            {
                Assert.Greater(row.RouteASeconds, 0f);
                Assert.Greater(row.RouteBSeconds, 0f);
            }
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
            var result = TrackGenerationTestUtil.Generate(
                TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), 31337);
            Assert.IsTrue(result.Success, "Generation failed.");

            var sections = result.Layout.Sections;
            for (int i = 0; i < sections.Count - 1; i++)
            {
                var cur = sections[i];
                var next = sections[i + 1];
                if (cur.OpenEnd || next.OpenStart) continue;   // air gaps are open by design
                if (cur.RouteId == 1 || next.RouteId == 1) continue; // route B shares gates via route A

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
                if (next.RouteId == 1) continue; // route B starts its own chain at the gate

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
            Assert.LessOrEqual(small.Branches.MaxBranchGroups, baseline.Branches.MaxBranchGroups);
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
            s.Branches.MinBranchGroups = 0;

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
}
