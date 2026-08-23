using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// V2.0 baseline preservation tests. No generation behavior is changed here.
    ///
    /// IMMUTABILITY CONTRACT:
    ///  • The normal regression tests generate ONLY the exact frozen seeds committed in
    ///    <see cref="GoldenSeedCorpus"/> — they never scan a window. A later V2 stage therefore
    ///    cannot silently pick a different seed.
    ///  • Scanning exists ONLY in <see cref="DiscoverAndFreeze"/> ([Explicit]) for the one-time
    ///    discovery of those seeds. It is never run by the normal suite.
    ///  • Once an entry is frozen, a failure to generate / satisfy its scenario / reproduce is a
    ///    hard test FAILURE. An entry that is not yet frozen reports Inconclusive (freeze pending).
    ///
    /// CANONICAL LAYOUT DATA compared per seed (see <see cref="CanonicalLayout"/>):
    ///   lap length; per-section {type, name, length, width, turnAngle, radius, banking,
    ///   elevationChange, pitchChange, rollChange, quarter, road, patternId, semanticElement,
    ///   ring count, and a hash over EVERY ring frame's position/forward/right/up/width/bank/
    ///   pitch/side-height/accumulated-roll/accumulated-vertical/curvatures/wall-multipliers/
    ///   overhang/pipe/wallride/rounding}; the existing quarter fingerprint; the full metrics
    ///   block (all feature counts + elevation min/max/range + facet + score); and the worst
    ///   wall-top transition rate (same formula the debug report uses, the V2.1 target metric).
    /// </summary>
    public class GoldenSeedCorpusTests
    {
        // ── Structural sanity (always runs) ──

        [Test]
        public void CorpusEntriesAreWellFormed()
        {
            var ids = new HashSet<string>();
            foreach (GoldenSeedCorpus.Entry e in GoldenSeedCorpus.Entries)
            {
                Assert.IsFalse(string.IsNullOrEmpty(e.Id), "Corpus entry has no Id.");
                Assert.IsTrue(ids.Add(e.Id), $"Duplicate corpus id '{e.Id}'.");
                Assert.Greater(e.ScanWindow, 0, $"'{e.Id}' has a non-positive scan window.");
            }
            Assert.GreaterOrEqual(GoldenSeedCorpus.Entries.Count, 8, "Corpus is unexpectedly small.");
        }

        // ── Core determinism (independent of feature availability; always runs) ──

        [Test]
        public void CoreDeterminism_SameSeedSameSettings_ProducesIdenticalCanonicalData()
        {
            const int seed = 482914;
            TrackGenerationResult a = GenerateBalanced(seed, out TrackRoadProfileSettings pa);
            TrackGenerationResult b = GenerateBalanced(seed, out TrackRoadProfileSettings pb);

            Assert.AreEqual(a.Success, b.Success, "Same seed produced different success outcomes — determinism broken.");
            Assert.AreEqual(a.AttemptsEvaluated, b.AttemptsEvaluated,
                "Attempt count diverged for the same seed — hidden nondeterminism.");
            Assert.AreEqual(a.ValidCandidateCount, b.ValidCandidateCount,
                "Valid-candidate count diverged for the same seed.");

            if (a.Success && b.Success)
                Assert.AreEqual(CanonicalLayout(a, pa), CanonicalLayout(b, pb),
                    "Same seed + settings produced different canonical layout data — determinism broken.");
        }

        // ── Frozen-seed regression (no scanning) ──

        private static IEnumerable<TestCaseData> CorpusSource()
        {
            foreach (GoldenSeedCorpus.Entry entry in GoldenSeedCorpus.Entries)
                yield return new TestCaseData(entry).SetName($"Frozen_{entry.Id}");
        }

        [Test]
        [TestCaseSource(nameof(CorpusSource))]
        public void FrozenSeedMatchesV1BaselineAndReproduces(GoldenSeedCorpus.Entry entry)
        {
            if (!GoldenSeedCorpus.IsFrozen(entry))
            {
                // Once the freeze has been committed, an unfrozen entry is a HARD failure so CI
                // cannot silently run an incomplete corpus. Before the first freeze it is only
                // Inconclusive (bootstrap).
                if (GoldenSeedCorpus.HasAnyFrozen)
                    Assert.Fail(
                        $"Corpus '{entry.Id}' is NOT frozen but the golden corpus is already committed. " +
                        "CI must not run with an incomplete corpus — run DiscoverAndFreeze and commit its seed+hash.");
                else
                    Assert.Inconclusive(
                        $"Corpus '{entry.Id}' is not frozen yet. Run the [Explicit] DiscoverAndFreeze test once, " +
                        "paste its output into GoldenSeedCorpus.FrozenSeeds, and commit.");
                return;
            }

            GoldenSeedCorpus.FrozenEntry frozen = GoldenSeedCorpus.FrozenOf(entry);
            int seed = frozen.Seed;

            TrackGenerationResult first = GenerateFrozen(entry, seed, out TrackRoadProfileSettings p1);
            Assert.IsTrue(first is { Success: true },
                $"FROZEN '{entry.Id}' seed {seed} no longer generates a valid track.");
            Assert.IsTrue(entry.Accept == null || entry.Accept(first),
                $"FROZEN '{entry.Id}' seed {seed} no longer satisfies its scenario requirement ({entry.Description}).");

            string canonical = CanonicalLayout(first, p1);

            // (a) run-vs-second-run determinism (kept).
            TrackGenerationResult second = GenerateFrozen(entry, seed, out TrackRoadProfileSettings p2);
            Assert.IsTrue(second is { Success: true },
                $"FROZEN '{entry.Id}' seed {seed} is nondeterministic — second run failed to generate.");
            Assert.AreEqual(canonical, CanonicalLayout(second, p2),
                $"FROZEN '{entry.Id}' seed {seed} produced different canonical layout on reproduction.");

            // (b) regression against the COMMITTED V1 baseline hash (new). A mismatch means the
            // frozen V1 output changed. For an INTENTIONAL V2 change (e.g. V2.1 wall morphology)
            // this is expected to fail first; review the diff, then re-baseline via DiscoverAndFreeze
            // only after approval.
            string currentHash = StableHash(canonical);
            Assert.AreEqual(frozen.CanonicalHash, currentHash,
                $"FROZEN '{entry.Id}' seed {seed} canonical output differs from the committed V1 baseline " +
                $"(expected {frozen.CanonicalHash}, got {currentHash}). If this is an intentional V2 change, " +
                "review the diff and re-baseline after approval; otherwise it is a regression.");
        }

        [Test]
        public void CorpusIsFullyFrozen()
        {
            var missing = new List<string>();
            foreach (GoldenSeedCorpus.Entry e in GoldenSeedCorpus.Entries)
                if (!GoldenSeedCorpus.IsFrozen(e)) missing.Add(e.Id);

            if (missing.Count == 0) return; // fully frozen

            if (GoldenSeedCorpus.HasAnyFrozen)
                Assert.Fail(
                    "Golden corpus is committed but INCOMPLETE — unfrozen entries: " +
                    string.Join(", ", missing) + ". Freeze all before CI can trust the suite.");
            else
                Assert.Inconclusive(
                    "Corpus not yet frozen. Run DiscoverAndFreeze and commit the seeds for: " +
                    string.Join(", ", missing));
        }

        // ── One-time discovery + freeze (NEVER part of the normal regression path) ──

        [Test, Explicit("Run ONCE to discover representative seeds, write the baseline artifact, " +
                        "and print the frozen-seed block to paste into GoldenSeedCorpus.")]
        public void DiscoverAndFreeze()
        {
            var resolved = new List<(string id, int seed, TrackGenerationResult r, TrackRoadProfileSettings p)>();
            var missing = new List<string>();

            foreach (GoldenSeedCorpus.Entry e in GoldenSeedCorpus.Entries)
            {
                // Already-frozen entries are NEVER re-scanned — their seed is permanently pinned.
                // Re-running discovery only recomputes their record at the pinned seed.
                if (GoldenSeedCorpus.IsFrozen(e))
                {
                    int pinned = GoldenSeedCorpus.FrozenOf(e).Seed;
                    TrackGenerationResult rr = GenerateFrozen(e, pinned, out TrackRoadProfileSettings pp);
                    if (rr is { Success: true } && (e.Accept == null || e.Accept(rr)))
                        resolved.Add((e.Id, pinned, rr, pp));
                    else
                        missing.Add(e.Id);
                    continue;
                }

                // Unfrozen entries: one-time discovery scan (the ONLY place scanning is used).
                if (TryDiscover(e, out int seed, out TrackGenerationResult r, out TrackRoadProfileSettings p))
                    resolved.Add((e.Id, seed, r, p));
                else
                    missing.Add(e.Id);
            }

            // Paste-ready frozen block (seed + committed V1 canonical hash) matching the markers
            // in GoldenSeedCorpus. Paste everything between the BEGIN/END lines.
            var freeze = new StringBuilder();
            freeze.AppendLine("            // BEGIN-FROZEN-SEEDS (auto-generated by DiscoverAndFreeze — do not hand-edit)");
            foreach ((string id, int seed, TrackGenerationResult r, TrackRoadProfileSettings p) in resolved)
            {
                string hash = StableHash(CanonicalLayout(r, p));
                freeze.AppendLine($"            {{ \"{id}\", new GoldenSeedCorpus.FrozenEntry({seed}, \"{hash}\") }},");
            }
            freeze.AppendLine("            // END-FROZEN-SEEDS");

            // Baseline artifact: metrics + canonical hash + worst wall-top rate per entry.
            var baseline = new StringBuilder();
            baseline.AppendLine("# Slingshot Track Generator — V1 Golden Corpus Baseline");
            baseline.AppendLine("# Captured before V2 changes. Do not edit by hand.");
            baseline.AppendLine("# id | seed | metrics | canonicalHash");
            baseline.AppendLine();
            foreach ((string id, int seed, TrackGenerationResult r, TrackRoadProfileSettings p) in resolved)
                baseline.Append(id).Append(" | seed=").Append(seed).Append(" | ")
                        .Append(MetricsLine(r)).Append(" | canon=")
                        .Append(StableHash(CanonicalLayout(r, p))).AppendLine();
            foreach (string id in missing)
                baseline.Append(id).AppendLine(" | NOT_FOUND (widen ScanWindow/BaseSeed and re-run)");
            baseline.AppendLine();
            baseline.Append("# resolved=").Append(resolved.Count).Append(" missing=").Append(missing.Count).AppendLine();

            WriteArtifact("GoldenCorpus_FrozenSeeds.txt", freeze.ToString());
            WriteArtifact("GoldenCorpus_Baseline.txt", baseline.ToString());
            Debug.Log("[GoldenSeedCorpus] FROZEN SEEDS — paste into GoldenSeedCorpus:\n" + freeze);
            Debug.Log("[GoldenSeedCorpus] BASELINE:\n" + baseline);

            Assert.AreEqual(0, missing.Count,
                "Discovery failed to resolve every corpus entry: " + string.Join(", ", missing) +
                ". Widen the ScanWindow/BaseSeed for those and re-run before freezing.");
        }

        [Test, Explicit("Run to re-baseline expected canonical hashes for an APPROVED intentional change " +
                        "(e.g. V2.1 wall morphology). Uses the existing pinned seeds ONLY — never scans, " +
                        "never changes a seed. Emits a refreshed FrozenSeeds block with the same seeds and new hashes.")]
        public void RebaselineFrozenCorpus()
        {
            if (!GoldenSeedCorpus.HasAnyFrozen)
            {
                Assert.Inconclusive("Nothing to re-baseline — the corpus has not been frozen yet. Run DiscoverAndFreeze first.");
                return;
            }

            var updated = new List<(string id, int seed, string oldHash, string newHash, string metrics)>();
            var broken = new List<string>();

            foreach (GoldenSeedCorpus.Entry e in GoldenSeedCorpus.Entries)
            {
                // Re-baseline touches ONLY existing golden entries, at their pinned seeds. No scanning.
                if (!GoldenSeedCorpus.IsFrozen(e)) continue;

                GoldenSeedCorpus.FrozenEntry f = GoldenSeedCorpus.FrozenOf(e);
                TrackGenerationResult r = GenerateFrozen(e, f.Seed, out TrackRoadProfileSettings p);
                if (!(r is { Success: true }))
                {
                    broken.Add(e.Id);
                    continue;
                }
                updated.Add((e.Id, f.Seed, f.CanonicalHash, StableHash(CanonicalLayout(r, p)), MetricsLine(r)));
            }

            // Paste-ready refreshed block: SAME seeds, updated hashes.
            var block = new StringBuilder();
            block.AppendLine("            // BEGIN-FROZEN-SEEDS (auto-generated by RebaselineFrozenCorpus — do not hand-edit)");
            foreach ((string id, int seed, _, string newHash, _) in updated)
                block.AppendLine($"            {{ \"{id}\", new GoldenSeedCorpus.FrozenEntry({seed}, \"{newHash}\") }},");
            block.AppendLine("            // END-FROZEN-SEEDS");

            // Human review artifact: which entries changed, old→new hash, current metrics.
            var report = new StringBuilder();
            report.AppendLine("# Golden Corpus Re-baseline (intentional-change review). Seeds are UNCHANGED.");
            report.AppendLine("# id | seed | changed? | oldHash -> newHash | metrics");
            report.AppendLine();
            int changed = 0;
            foreach ((string id, int seed, string oldHash, string newHash, string metrics) in updated)
            {
                bool diff = oldHash != newHash;
                if (diff) changed++;
                report.Append(id).Append(" | seed=").Append(seed)
                      .Append(diff ? " | CHANGED | " : " | same | ")
                      .Append(oldHash).Append(" -> ").Append(newHash).Append(" | ").Append(metrics).AppendLine();
            }
            foreach (string id in broken)
                report.Append(id).AppendLine(" | PINNED SEED NO LONGER GENERATES — investigate; do not re-baseline");
            report.AppendLine();
            report.Append("# frozen=").Append(updated.Count).Append(" changed=").Append(changed)
                  .Append(" broken=").Append(broken.Count).AppendLine();

            WriteArtifact("GoldenCorpus_Rebaseline.txt", report.ToString());
            Debug.Log("[GoldenSeedCorpus] REBASELINE — refreshed FrozenSeeds block (same seeds, new hashes):\n" + block);
            Debug.Log("[GoldenSeedCorpus] REBASELINE review:\n" + report);

            Assert.AreEqual(0, broken.Count,
                "Some permanently-pinned golden seeds no longer generate: " + string.Join(", ", broken) +
                ". A pinned seed must always generate — investigate before re-baselining.");
        }

        // ── Generation helpers ──

        /// <summary>Discovery-only scan. Used exclusively by <see cref="DiscoverAndFreeze"/>.</summary>
        private static bool TryDiscover(GoldenSeedCorpus.Entry entry, out int seed,
            out TrackGenerationResult result, out TrackRoadProfileSettings profile)
        {
            for (int s = entry.BaseSeed; s < entry.BaseSeed + entry.ScanWindow; s++)
            {
                TrackGenerationResult r = GenerateFrozen(entry, s, out TrackRoadProfileSettings p);
                if (r is { Success: true } && (entry.Accept == null || entry.Accept(r)))
                {
                    seed = s; result = r; profile = p;
                    return true;
                }
            }
            seed = 0; result = null; profile = null;
            return false;
        }

        /// <summary>Generates one entry at an EXACT seed and returns the road profile it used.</summary>
        private static TrackGenerationResult GenerateFrozen(GoldenSeedCorpus.Entry entry, int seed,
            out TrackRoadProfileSettings profile)
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                // Editor-fidelity entries use the REAL preset (BestValid + full ring density);
                // ordinary entries use the fast test settings (FirstValid + coarse rings).
                TrackDesignerSettings settings = entry.EditorFidelity
                    ? TrackStylePresetLibrary.Create(entry.Preset)
                    : TrackGenerationTestUtil.FastSettings(entry.Preset);
                entry.Mutate?.Invoke(settings);

                if (!entry.EditorFidelity)
                {
                    // FirstValid + a single coarse build per seed. The attempt budget must be at
                    // least what the FROZEN seeds needed to close when captured (some heavy
                    // Rollercoaster entries need ~150) — otherwise a pinned seed "no longer
                    // generates a valid track" and the baseline check fails. The corpus is fully
                    // frozen, so nothing SCANS here (every path generates pinned seeds directly);
                    // a high cap therefore only affects generating known-good seeds, which close
                    // fast, with no grind risk. Keep this in sync with the cap used when freezing.
                    settings.Generation.SelectionMode = CandidateSelectionMode.FirstValid;
                    settings.Generation.CandidatesToScore = 1;
                    settings.Generation.MaxAttempts = Mathf.Max(settings.Generation.MaxAttempts, 200);
                }
                settings.Sanitize();
                profile = ResolvedTrackGenerationConfig.Resolve(config, settings).RoadProfile;
                return TrackGenerationTestUtil.Generate(settings, seed, config);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static TrackGenerationResult GenerateBalanced(int seed, out TrackRoadProfileSettings profile)
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                settings.Sanitize();
                profile = ResolvedTrackGenerationConfig.Resolve(config, settings).RoadProfile;
                return TrackGenerationTestUtil.Generate(settings, seed, config);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // ── Canonical layout data ──

        private static string CanonicalLayout(TrackGenerationResult r, TrackRoadProfileSettings profile)
        {
            GeneratedTrackLayout layout = r.Layout;
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.Append("lap=").Append(layout.LapLength.ToString("F3", c))
              .Append(" sections=").Append(layout.Sections.Count).Append('\n');

            foreach (GeneratedTrackSection s in layout.Sections)
            {
                TrackMacroSectionDefinition d = s.Definition;
                sb.Append(s.SectionIndex).Append(':').Append(d.SectionType).Append(',')
                  .Append(d.DebugName).Append(',')
                  .Append(d.Length.ToString("F3", c)).Append(',')
                  .Append(d.Width.ToString("F3", c)).Append(',')
                  .Append(d.TurnAngle.ToString("F2", c)).Append(',')
                  .Append(d.Radius.ToString("F2", c)).Append(',')
                  .Append(d.BankingAngle.ToString("F2", c)).Append(',')
                  .Append(d.ElevationChange.ToString("F3", c)).Append(',')
                  .Append(d.PitchChange.ToString("F2", c)).Append(',')
                  .Append(d.RollChange.ToString("F2", c)).Append(",Q")
                  .Append(s.QuarterIndex).Append(",r").Append(s.RoadId).Append(',')
                  .Append(string.IsNullOrEmpty(d.PatternId) ? "-" : d.PatternId).Append(',')
                  .Append(d.SemanticElement).Append(",frames=")
                  .Append(s.SubdivisionFrames?.Length ?? 0).Append(",fh=")
                  .Append(FrameHash(s.SubdivisionFrames)).Append('\n');
            }

            // Existing quarter/route fingerprint (reuses the project's established member access).
            sb.Append("quarters|").Append(TrackGenerationTestUtil.Fingerprint(layout)).Append('\n');
            sb.Append("metrics: ").Append(MetricsLine(r)).Append('\n');

            float wall = WorstWallTopRate(layout.Sections, profile, out int ws, out int wr);
            sb.Append("wallTopMax=").Append(wall.ToString("F4", c))
              .Append(" at [").Append(ws).Append("] ring ").Append(wr).Append('\n');

            return sb.ToString();
        }

        /// <summary>Deterministic hash over every ring frame's geometry + cross-section channels.</summary>
        private static string FrameHash(TrackConnectionFrame[] frames)
        {
            if (frames == null || frames.Length == 0) return "0";
            CultureInfo c = CultureInfo.InvariantCulture;
            unchecked
            {
                ulong h = 1469598103934665603UL;

                void Mix(float v)
                {
                    string str = v.ToString("F4", c);
                    foreach (char ch in str) { h ^= ch; h *= 1099511628211UL; }
                    h ^= '|'; h *= 1099511628211UL;
                }

                foreach (TrackConnectionFrame f in frames)
                {
                    Mix(f.Position.x); Mix(f.Position.y); Mix(f.Position.z);
                    Mix(f.Forward.x); Mix(f.Forward.y); Mix(f.Forward.z);
                    Mix(f.Right.x); Mix(f.Right.y); Mix(f.Right.z);
                    Mix(f.Up.x); Mix(f.Up.y); Mix(f.Up.z);
                    Mix(f.Width); Mix(f.BankAngle); Mix(f.PitchAngle); Mix(f.SideHeight);
                    Mix(f.AccumulatedRoadRoll); Mix(f.AccumulatedVerticalRotation);
                    Mix(f.HorizontalCurvature); Mix(f.VerticalCurvature);
                    Mix(f.LeftWallMultiplier); Mix(f.RightWallMultiplier);
                    Mix(f.LeftOverhang); Mix(f.RightOverhang);
                    Mix(f.PipeClosure); Mix(f.WallrideMorph); Mix(f.TurnRounding);
                }
                return h.ToString("x16");
            }
        }

        /// <summary>
        /// Worst wall-top transition rate (m per m), replicating the debug report's formula
        /// exactly (TrackDebugReportExporter.WallWaveHotspots). This is the V2.1 target metric,
        /// captured now so before/after is measurable. Rotational features are skipped like the
        /// report does.
        /// </summary>
        private static float WorstWallTopRate(List<GeneratedTrackSection> sections,
            TrackRoadProfileSettings profile, out int sectionIndex, out int ring)
        {
            sectionIndex = -1; ring = -1;
            float worst = 0f;
            if (profile == null) return 0f;

            float WallTop(in TrackConnectionFrame f, bool left)
            {
                float side = f.SideHeight > 0.001f ? f.SideHeight : profile.SideHeight;
                float mult = left ? f.LeftWallMultiplier : f.RightWallMultiplier;
                return profile.HeightAt(1f, f.Width * 0.5f, side) * mult + profile.SafetyLipHeight * mult;
            }

            foreach (GeneratedTrackSection sec in sections)
            {
                TrackConnectionFrame[] frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;

                TrackMacroSectionType t = sec.Definition.SectionType;
                if (t == TrackMacroSectionType.Loop || t == TrackMacroSectionType.Corkscrew ||
                    t == TrackMacroSectionType.HalfLoopTwist || t == TrackMacroSectionType.RotationalEvent)
                    continue;

                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);
                    float rate = Mathf.Max(
                        Mathf.Abs(WallTop(frames[i], true) - WallTop(frames[i - 1], true)),
                        Mathf.Abs(WallTop(frames[i], false) - WallTop(frames[i - 1], false))) / ds;
                    if (rate > worst) { worst = rate; sectionIndex = sec.SectionIndex; ring = i; }
                }
            }
            return worst;
        }

        private static string MetricsLine(TrackGenerationResult r)
        {
            TrackGenerationMetrics m = r.Layout.Metrics;
            CultureInfo c = CultureInfo.InvariantCulture;
            return
                $"lap={m.LapLengthMeters.ToString("F0", c)}m rings={m.TotalRings} turns={m.TurnCount} " +
                $"elev=[{m.MinElevation.ToString("F0", c)}..{m.MaxElevation.ToString("F0", c)}] " +
                $"range={m.ElevationRange.ToString("F0", c)} " +
                $"loops={m.LoopCount} corks={m.CorkscrewCount} spirals={m.SpiralCount} " +
                $"halfloops={m.HalfLoopCount} jumps={m.JumpCount} pipes={m.FullPipeCount} " +
                $"wallrides={m.WallrideCount} dual={m.DualRoadQuarterCount} compound={m.CompoundPatternCount} " +
                $"maxFacet={m.MaxFacetAngleObserved.ToString("F2", c)} score={m.SelectedCandidateScore.ToString("F1", c)}";
        }

        private static string StableHash(string s)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
                return h.ToString("x16");
            }
        }

        private static void WriteArtifact(string name, string content)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_Backups", name));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            Debug.Log($"[GoldenSeedCorpus] wrote {path}");
        }
    }
}
