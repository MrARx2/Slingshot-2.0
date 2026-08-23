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
    /// V2.1 STAGE 1 — captures the BEFORE measurement of cross-section transition quality
    /// across the frozen golden corpus. Read-only: it measures built frames and changes
    /// nothing about generation.
    ///
    /// Run <see cref="MeasureCrossSectionBaseline"/> once ([Explicit], so it never slows a
    /// normal test pass). It writes _Backups/V2_1_CrossSectionBefore.txt and logs a summary.
    /// After the V2.1 geometry fix, run it again and diff: reversals and excursions must drop
    /// while authored feature interiors stay untouched.
    /// </summary>
    public class CrossSectionTransitionBaselineTests
    {
        /// <summary>
        /// V2.1 ACCEPTANCE GUARD. A pass-through section must never assert its own neutral wall
        /// height as a visual destination: between two tall anchors that cannot fit a real neutral
        /// plateau, the wall must morph A→B without an incidental valley.
        ///
        /// Legitimate variation is explicitly NOT penalised — a long ordinary run may still
        /// transition down to neutral, hold, and rise again (that is one allowed down/up cycle),
        /// and authored feature interiors are untouched.
        /// </summary>
        [Test]
        public void PassThroughSectionsDoNotCreateIncidentalWallValleys()
        {
            int totalUnnecessaryReversals = 0;
            float worstUnnecessaryExcursion = 0f;
            string worstAt = "";
            int measured = 0;

            foreach (GoldenSeedCorpus.Entry entry in GoldenSeedCorpus.Entries)
            {
                if (!GoldenSeedCorpus.IsFrozen(entry)) continue;
                int seed = GoldenSeedCorpus.FrozenOf(entry).Seed;
                TrackGenerationResult r = Generate(entry, seed, out ResolvedTrackGenerationConfig cfg);
                if (!(r is { Success: true })) continue;

                CrossSectionTransitionDiagnostics.Summary s =
                    CrossSectionTransitionDiagnostics.Measure(r.Layout.Sections, cfg);
                measured++;
                totalUnnecessaryReversals += s.UnnecessarySideHeightReversals;
                if (s.WorstUnnecessarySideHeightExcursion > worstUnnecessaryExcursion)
                {
                    worstUnnecessaryExcursion = s.WorstUnnecessarySideHeightExcursion;
                    worstAt = $"{entry.Id}: {s.WorstUnnecessaryAt}";
                }
            }

            Assert.Greater(measured, 0, "No corpus track generated — cannot verify wall transitions.");
            Assert.AreEqual(0, totalUnnecessaryReversals,
                $"Pass-through sections reversed wall height with no design justification " +
                $"({totalUnnecessaryReversals} across {measured} tracks). Worst: {worstAt}");
            Assert.LessOrEqual(worstUnnecessaryExcursion, 0.10f,
                $"Incidental wall-height valley of {worstUnnecessaryExcursion:F2}m at {worstAt} — " +
                "a pass-through section must not pull the wall toward neutral without room for a real plateau.");
        }

        [Test, Explicit("V2.1: writes the cross-section transition measurement for the frozen corpus.")]
        public void MeasureCrossSectionBaseline()
        {
            var sb = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            sb.AppendLine("# V2.1 — Cross-section transition quality BASELINE (before any geometry change)");
            sb.AppendLine("# Read-only measurement over the FROZEN golden corpus.");
            sb.AppendLine("# reversals = derivative sign changes across an anchor→anchor span (clean morph = 0)");
            sb.AppendLine("# excursion = metres the span left the [A,B] band with no authored reason");
            sb.AppendLine();

            int measured = 0, skipped = 0;
            var totals = new List<CrossSectionTransitionDiagnostics.Summary>();

            foreach (GoldenSeedCorpus.Entry entry in GoldenSeedCorpus.Entries)
            {
                if (!GoldenSeedCorpus.IsFrozen(entry))
                {
                    sb.AppendLine($"== {entry.Id}: NOT FROZEN — skipped ==").AppendLine();
                    skipped++;
                    continue;
                }

                int seed = GoldenSeedCorpus.FrozenOf(entry).Seed;
                TrackGenerationResult result = Generate(entry, seed, out ResolvedTrackGenerationConfig cfg);

                sb.AppendLine($"════ {entry.Id} (seed {seed}) ════");
                if (!(result is { Success: true }))
                {
                    sb.AppendLine("  FAILED TO GENERATE — no measurement.").AppendLine();
                    skipped++;
                    continue;
                }

                CrossSectionTransitionDiagnostics.Summary s =
                    CrossSectionTransitionDiagnostics.Measure(result.Layout.Sections, cfg);
                totals.Add(s);
                measured++;

                CrossSectionTransitionDiagnostics.AppendReport(sb, result.Layout.Sections, cfg);
                Debug.Log($"[V2.1 baseline] {entry.Id} (seed {seed}): {s.Compact()}");
            }

            // Aggregate per CHANNEL — the attribution the V2.1 scope decision needs.
            int spans = 0;
            int n = CrossSectionTransitionDiagnostics.ChannelCount;
            var rev = new int[n];
            var exc = new float[n];
            float worstLat = 0f, worstVert = 0f, worstComb = 0f;
            foreach (var s in totals)
            {
                spans += s.SpanCount;
                for (int ch = 0; ch < n; ch++)
                {
                    rev[ch] += s.Reversals[ch];
                    exc[ch] = Mathf.Max(exc[ch], s.WorstExcursion[ch]);
                }
                worstLat = Mathf.Max(worstLat, s.WorstLateralRate);
                worstVert = Mathf.Max(worstVert, s.WorstVerticalRate);
                worstComb = Mathf.Max(worstComb, s.WorstCombinedRate);
            }

            float E(CrossSectionTransitionDiagnostics.Channel ch) => exc[(int)ch];
            int R(CrossSectionTransitionDiagnostics.Channel ch) => rev[(int)ch];

            // ── The metrics Stage 2 is judged against ──
            int unnecessaryRev = 0, shortSpans = 0, longSpans = 0, straddle = 0;
            float worstUnnecessaryExc = 0f;
            string worstUnnecessaryAt = "";
            foreach (var s in totals)
            {
                unnecessaryRev += s.UnnecessarySideHeightReversals;
                shortSpans += s.ShortSpans;
                longSpans += s.LegitimateNeutralRuns;
                straddle += s.StraddlingSpans;
                if (s.WorstUnnecessarySideHeightExcursion > worstUnnecessaryExc)
                {
                    worstUnnecessaryExc = s.WorstUnnecessarySideHeightExcursion;
                    worstUnnecessaryAt = s.WorstUnnecessaryAt;
                }
            }

            string stage2 =
                $"UnnecessarySideHeightReversals={unnecessaryRev} " +
                $"UnnecessarySideHeightExcursion={worstUnnecessaryExc.ToString("F2", c)}m " +
                $"[spans SHORT={shortSpans} LONG={longSpans} straddle={straddle}] at {worstUnnecessaryAt}";
            sb.AppendLine("════════ STAGE-2 TARGET METRICS (before) ════════");
            sb.AppendLine(stage2);
            sb.AppendLine();
            Debug.Log($"[V2.1 baseline] STAGE-2 METRICS — {stage2}");

            string aggregate =
                $"tracks={measured} spans={spans} | " +
                $"EXCURSION SideHeight={E(CrossSectionTransitionDiagnostics.Channel.SideHeight).ToString("F2", c)}m " +
                $"BoostL={E(CrossSectionTransitionDiagnostics.Channel.BoostL).ToString("F2", c)}x " +
                $"BoostR={E(CrossSectionTransitionDiagnostics.Channel.BoostR).ToString("F2", c)}x " +
                $"TopL={E(CrossSectionTransitionDiagnostics.Channel.TopLVertical).ToString("F2", c)}m " +
                $"TopR={E(CrossSectionTransitionDiagnostics.Channel.TopRVertical).ToString("F2", c)}m " +
                $"Width={E(CrossSectionTransitionDiagnostics.Channel.Width).ToString("F2", c)}m " +
                $"Bank={E(CrossSectionTransitionDiagnostics.Channel.Bank).ToString("F1", c)}deg | " +
                $"REVERSALS SideHeight={R(CrossSectionTransitionDiagnostics.Channel.SideHeight)} " +
                $"BoostL={R(CrossSectionTransitionDiagnostics.Channel.BoostL)} " +
                $"BoostR={R(CrossSectionTransitionDiagnostics.Channel.BoostR)} " +
                $"TopL={R(CrossSectionTransitionDiagnostics.Channel.TopLVertical)} " +
                $"TopR={R(CrossSectionTransitionDiagnostics.Channel.TopRVertical)} " +
                $"Width={R(CrossSectionTransitionDiagnostics.Channel.Width)} | " +
                $"RATES lat={worstLat.ToString("F3", c)} vert={worstVert.ToString("F3", c)} comb={worstComb.ToString("F3", c)} m/m";

            sb.AppendLine("════════ AGGREGATE (the V2.1 before-numbers, per channel) ════════");
            sb.AppendLine(aggregate);
            sb.AppendLine($"(measured {measured} tracks, skipped {skipped})");

            // NOTE: writes to a LATEST file so the captured "before" snapshot
            // (_Backups/V2_1_CrossSectionBefore.txt) is never overwritten by a post-fix run.
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "_Backups", "V2_1_CrossSection_Latest.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());

            Debug.Log($"[V2.1 baseline] AGGREGATE — {aggregate}");
            Debug.Log($"[V2.1 baseline] wrote {path}");

            Assert.Greater(measured, 0, "No corpus track could be measured — baseline not captured.");
        }

        private static TrackGenerationResult Generate(GoldenSeedCorpus.Entry entry, int seed,
            out ResolvedTrackGenerationConfig cfg)
        {
            TrackConfig config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings = entry.EditorFidelity
                    ? TrackStylePresetLibrary.Create(entry.Preset)
                    : TrackGenerationTestUtil.FastSettings(entry.Preset);
                entry.Mutate?.Invoke(settings);
                if (!entry.EditorFidelity)
                {
                    // Same generation path the frozen seeds were captured under.
                    settings.Generation.SelectionMode = CandidateSelectionMode.FirstValid;
                    settings.Generation.CandidatesToScore = 1;
                    settings.Generation.MaxAttempts = Mathf.Max(settings.Generation.MaxAttempts, 200);
                }
                settings.Sanitize();
                cfg = ResolvedTrackGenerationConfig.Resolve(config, settings);
                return TrackGenerationTestUtil.Generate(settings, seed, config);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
