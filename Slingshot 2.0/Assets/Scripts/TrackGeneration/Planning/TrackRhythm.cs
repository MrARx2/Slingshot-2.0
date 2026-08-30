using System;
using System.Collections.Generic;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using UnityEngine;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Driving-experience families used by procedural pacing. This intentionally sits
    /// above raw macro-section types: a compound pattern is one encounter, while its
    /// internal approach, body and recovery definitions are not separate choices.
    /// </summary>
    public enum TrackEncounterFamily
    {
        None,
        CornerFlow,
        Reversal,
        SCurveFlow,
        PrecisionTransfer,
        Inversion,
        Airborne,
        VerticalFlow
    }

    [Serializable]
    public sealed class TrackRhythmSummary
    {
        public int EncounterCount;
        public int DistinctFamilies;
        public int LongestRepeatedFamilyStreak;
        public int LongestSCurveStreak;
        public int MaxSCurvesInWindow;
        public int RepeatedSpecialFamilyTransitions;
        public int ViolationCount;
        public float Score;
        public readonly List<string> RouteTimelines = new List<string>();

        public string CompactDescription =>
            $"{DistinctFamilies} families across {EncounterCount} encounters; " +
            $"longest S-flow streak {LongestSCurveStreak}; " +
            $"window peak {MaxSCurvesInWindow}; family repeats {RepeatedSpecialFamilyTransitions}; " +
            $"rhythm {Score:F0}/100";
    }

    /// <summary>Shared classifier and route-order rhythm analysis.</summary>
    public static class TrackRhythm
    {
        public static TrackEncounterFamily FamilyOf(TrackPatternType pattern)
        {
            switch (pattern)
            {
                case TrackPatternType.SCurve:
                case TrackPatternType.AlternatingRadiusSequence:
                    return TrackEncounterFamily.SCurveFlow;
                case TrackPatternType.Chicane:
                    return TrackEncounterFamily.PrecisionTransfer;
                case TrackPatternType.JumpGap:
                case TrackPatternType.JumpToBankedLanding:
                    return TrackEncounterFamily.Airborne;
                case TrackPatternType.Spiral:
                case TrackPatternType.SpiralToCorkscrew:
                case TrackPatternType.Camelback:
                    return TrackEncounterFamily.VerticalFlow;
                case TrackPatternType.Hairpin:
                case TrackPatternType.SweeperIntoHairpin:
                case TrackPatternType.WideTurnaround:
                case TrackPatternType.Horseshoe:
                case TrackPatternType.HalfHelixTurnaround:
                case TrackPatternType.Cutback:
                    return TrackEncounterFamily.Reversal;
                case TrackPatternType.FullLoop:
                case TrackPatternType.Corkscrew:
                case TrackPatternType.DoubleCorkscrew:
                case TrackPatternType.HalfLoopRollout:
                case TrackPatternType.HalfLoopToCorkscrew:
                case TrackPatternType.LoopToCorkscrew:
                case TrackPatternType.FullPipe:
                case TrackPatternType.HeartlineRoll:
                case TrackPatternType.ZeroGRoll:
                case TrackPatternType.DiveLoop:
                case TrackPatternType.Sidewinder:
                    return TrackEncounterFamily.Inversion;
                default:
                    return TrackEncounterFamily.CornerFlow;
            }
        }

        public static TrackEncounterFamily FamilyOf(SemanticElementId element)
        {
            switch (element)
            {
                case SemanticElementId.SCurve:
                case SemanticElementId.AlternatingRadiusSequence:
                    return TrackEncounterFamily.SCurveFlow;
                case SemanticElementId.Chicane:
                    return TrackEncounterFamily.PrecisionTransfer;
                case SemanticElementId.JumpGap:
                case SemanticElementId.JumpToBankedLanding:
                    return TrackEncounterFamily.Airborne;
                case SemanticElementId.Spiral:
                case SemanticElementId.SpiralToCorkscrew:
                case SemanticElementId.Camelback:
                    return TrackEncounterFamily.VerticalFlow;
                case SemanticElementId.Hairpin:
                case SemanticElementId.SweeperIntoHairpin:
                case SemanticElementId.WideTurnaround:
                case SemanticElementId.Horseshoe:
                case SemanticElementId.HalfHelixTurnaround:
                case SemanticElementId.Cutback:
                    return TrackEncounterFamily.Reversal;
                case SemanticElementId.VerticalLoop:
                case SemanticElementId.InlineCorkscrew:
                case SemanticElementId.DoubleCorkscrew:
                case SemanticElementId.Immelmann:
                case SemanticElementId.HalfLoopToCorkscrew:
                case SemanticElementId.LoopToCorkscrew:
                case SemanticElementId.FullPipe:
                case SemanticElementId.DirectionalCorkscrew:
                case SemanticElementId.HeartlineRoll:
                case SemanticElementId.ZeroGRoll:
                case SemanticElementId.DiveLoop:
                case SemanticElementId.Sidewinder:
                    return TrackEncounterFamily.Inversion;
                case SemanticElementId.OrdinaryCurve:
                case SemanticElementId.DoubleApex:
                case SemanticElementId.TighteningCorner:
                case SemanticElementId.OpeningCorner:
                case SemanticElementId.WallrideTurn:
                    return TrackEncounterFamily.CornerFlow;
                default:
                    return TrackEncounterFamily.None;
            }
        }

        public static bool IsSCurveFamily(TrackPatternType pattern) =>
            FamilyOf(pattern) == TrackEncounterFamily.SCurveFlow;

        /// <summary>
        /// Arrange a selected feature multiset into a deterministic, low-repetition
        /// route order. It never removes required content. When an explicit request is
        /// mathematically impossible to diversify, the least repetitive order wins.
        /// </summary>
        public static List<TrackPatternType> ArrangeFeatureSequence(
            IReadOnlyList<TrackPatternType> source,
            ResolvedTrackGenerationConfig cfg,
            ref Unity.Mathematics.Random rng,
            out int remainingPenalty)
        {
            var best = source == null
                ? new List<TrackPatternType>()
                : new List<TrackPatternType>(source);
            if (best.Count <= 1 || cfg == null || !cfg.EnforceProceduralRhythm)
            {
                remainingPenalty = 0;
                return best;
            }

            // Seed the search with a real shuffled ordering so two seeds are not
            // forced to share the same already-valid catalog ordering.
            Shuffle(best, ref rng);
            int bestPenalty = SequencePenalty(best, cfg);
            int attempts = Mathf.Clamp(best.Count * best.Count * 2, 24, 128);
            for (int attempt = 0; attempt < attempts && bestPenalty > 0; attempt++)
            {
                var candidate = new List<TrackPatternType>(source);
                Shuffle(candidate, ref rng);
                int penalty = SequencePenalty(candidate, cfg);
                if (penalty >= bestPenalty) continue;
                best = candidate;
                bestPenalty = penalty;
            }

            remainingPenalty = bestPenalty;
            return best;
        }

        public static TrackRhythmSummary Analyze(
            IReadOnlyList<TopologySlotRecord> slots,
            ResolvedTrackGenerationConfig cfg)
        {
            var summary = new TrackRhythmSummary();
            if (slots == null || cfg == null) return summary;

            // Road A is the continuous lap. Each Road B belongs only to its own quarter.
            var routes = new SortedDictionary<int, List<TopologySlotRecord>>();
            for (int i = 0; i < slots.Count; i++)
            {
                TopologySlotRecord slot = slots[i];
                if (slot == null) continue;
                int key = slot.RoadId == 0 ? 0 : 100 + slot.QuarterIndex;
                if (!routes.TryGetValue(key, out List<TopologySlotRecord> route))
                    routes.Add(key, route = new List<TopologySlotRecord>());
                route.Add(slot);
            }

            var distinct = new HashSet<TrackEncounterFamily>();
            foreach (KeyValuePair<int, List<TopologySlotRecord>> pair in routes)
            {
                List<TopologySlotRecord> route = pair.Value;
                route.Sort((a, b) => a.CanonicalOrder.CompareTo(b.CanonicalOrder));
                var families = new List<TrackEncounterFamily>();
                var labels = new List<string>();
                for (int i = 0; i < route.Count; i++)
                {
                    TrackEncounterFamily family = FamilyOf(route[i].CurrentRealization);
                    if (family == TrackEncounterFamily.None) continue;
                    families.Add(family);
                    labels.Add(route[i].CurrentRealization.ToString());
                    distinct.Add(family);
                }

                bool circular = pair.Key == 0;
                AnalyzeRoute(families, circular, cfg, summary);
                summary.EncounterCount += families.Count;
                string routeName = pair.Key == 0 ? "Road A" : $"Q{pair.Key - 99} Road B";
                summary.RouteTimelines.Add($"{routeName}: {string.Join(" -> ", labels)}");
            }

            summary.DistinctFamilies = distinct.Count;
            summary.ViolationCount =
                Mathf.Max(0, summary.LongestSCurveStreak - cfg.MaxConsecutiveSCurveEncounters) +
                Mathf.Max(0, summary.MaxSCurvesInWindow - cfg.MaxSCurveEncountersPerWindow);
            float penalty = summary.ViolationCount * 20f +
                            summary.RepeatedSpecialFamilyTransitions * 4f;
            float coverage = Mathf.Min(12f, summary.DistinctFamilies * 1.75f);
            summary.Score = Mathf.Clamp(88f + coverage - penalty, 0f, 100f);
            return summary;
        }

        /// <summary>
        /// Audits the route that was actually built, rather than trusting planning
        /// bookkeeping alone. Topology-owned features are counted once per stable slot;
        /// unowned S-curve sections (including closure-solver S-bends) remain visible
        /// encounters and therefore cannot bypass procedural rhythm validation.
        /// Alternating Radius Sequence expands to its two visible S-curve encounters.
        /// </summary>
        public static TrackRhythmSummary AnalyzeBuiltLayout(
            GeneratedTrackLayout layout,
            ResolvedTrackGenerationConfig cfg)
        {
            if (layout?.Sections == null)
                return new TrackRhythmSummary();

            var records = new List<TopologySlotRecord>();
            var seenSlots = new HashSet<string>(StringComparer.Ordinal);
            int syntheticIndex = 0;

            for (int sectionIndex = 0; sectionIndex < layout.Sections.Count; sectionIndex++)
            {
                GeneratedTrackSection section = layout.Sections[sectionIndex];
                TrackMacroSectionDefinition definition = section?.Definition;
                if (definition == null) continue;

                int road = section.RoadId;
                int quarter = section.QuarterIndex;
                string slotId = string.IsNullOrWhiteSpace(section.TopologySlotId)
                    ? definition.TopologySlotId
                    : section.TopologySlotId;
                SemanticElementId semantic = BuiltSemantic(definition);

                if (!string.IsNullOrWhiteSpace(slotId))
                {
                    // Entry/recovery primitives can share the feature slot while
                    // carrying no gameplay semantic. Wait for the owned core instead
                    // of marking the slot as seen too early.
                    if (FamilyOf(semantic) == TrackEncounterFamily.None) continue;
                    string routeSlot = $"{quarter}|{road}|{slotId}";
                    if (!seenSlots.Add(routeSlot)) continue;

                    int visibleEncounters = semantic == SemanticElementId.AlternatingRadiusSequence ? 2 : 1;
                    for (int copy = 0; copy < visibleEncounters; copy++)
                    {
                        records.Add(new TopologySlotRecord
                        {
                            TopologySlotId = $"{slotId}:built:{copy}",
                            CanonicalOrder = sectionIndex * 3 + copy,
                            RouteOrder = sectionIndex * 3 + copy,
                            QuarterIndex = quarter,
                            RoadId = road,
                            CurrentRealization = semantic,
                            OriginalRealization = semantic
                        });
                    }
                    continue;
                }

                // Solver-created or legacy S sections have no gameplay slot, but they
                // are still felt by the driver and must participate in the visual rhythm.
                if (definition.SectionType != TrackMacroSectionType.SCurve) continue;
                records.Add(new TopologySlotRecord
                {
                    TopologySlotId = $"built-s-flow-{syntheticIndex++}",
                    CanonicalOrder = sectionIndex * 3,
                    RouteOrder = sectionIndex * 3,
                    QuarterIndex = quarter,
                    RoadId = road,
                    CurrentRealization = SemanticElementId.SCurve,
                    OriginalRealization = SemanticElementId.SCurve
                });
            }

            return Analyze(records, cfg);
        }

        private static SemanticElementId BuiltSemantic(TrackMacroSectionDefinition definition)
        {
            if (definition.SemanticElement != SemanticElementId.None)
                return definition.SemanticElement;

            switch (definition.SectionType)
            {
                case TrackMacroSectionType.BankedCurve: return SemanticElementId.OrdinaryCurve;
                case TrackMacroSectionType.BankedHairpin: return SemanticElementId.Hairpin;
                case TrackMacroSectionType.SCurve: return SemanticElementId.SCurve;
                case TrackMacroSectionType.Chicane: return SemanticElementId.Chicane;
                case TrackMacroSectionType.Loop: return SemanticElementId.VerticalLoop;
                case TrackMacroSectionType.Corkscrew: return SemanticElementId.InlineCorkscrew;
                case TrackMacroSectionType.Spiral: return SemanticElementId.Spiral;
                case TrackMacroSectionType.HalfLoopTwist: return SemanticElementId.Immelmann;
                case TrackMacroSectionType.FullPipe: return SemanticElementId.FullPipe;
                case TrackMacroSectionType.WallrideTurn: return SemanticElementId.WallrideTurn;
                default: return SemanticElementId.None;
            }
        }

        private static int SequencePenalty(
            IReadOnlyList<TrackPatternType> sequence,
            ResolvedTrackGenerationConfig cfg)
        {
            int count = sequence.Count;
            if (count <= 1) return 0;
            int penalty = 0;

            // The canonical road is a lap, so the last selected encounter and the
            // first selected encounter are neighbours as well.
            int sCurveCount = 0;
            for (int i = 0; i < count; i++)
                if (FamilyOf(sequence[i]) == TrackEncounterFamily.SCurveFlow)
                    sCurveCount++;
            if (sCurveCount == count)
            {
                penalty += Mathf.Max(0, count - cfg.MaxConsecutiveSCurveEncounters) * 100;
            }
            else
            {
                for (int start = 0; start < count; start++)
                {
                    int previous = (start - 1 + count) % count;
                    if (FamilyOf(sequence[start]) != TrackEncounterFamily.SCurveFlow ||
                        FamilyOf(sequence[previous]) == TrackEncounterFamily.SCurveFlow)
                        continue;
                    int run = 0;
                    while (run < count &&
                           FamilyOf(sequence[(start + run) % count]) == TrackEncounterFamily.SCurveFlow)
                        run++;
                    penalty += Mathf.Max(0, run - cfg.MaxConsecutiveSCurveEncounters) * 100;
                }
            }

            int cooldown = Mathf.Max(0, cfg.SameFamilyCooldownEncounters);
            for (int i = 0; i < count && cooldown > 0; i++)
            {
                TrackEncounterFamily current = FamilyOf(sequence[i]);
                if (current == TrackEncounterFamily.CornerFlow) continue;
                int lookback = Mathf.Min(cooldown, count - 1);
                for (int distance = 1; distance <= lookback; distance++)
                {
                    int previous = (i - distance + count) % count;
                    if (FamilyOf(sequence[previous]) != current) continue;
                    penalty += 20 * (cooldown - distance + 1);
                    break;
                }
            }

            int window = Mathf.Min(cfg.SCurveDiversityWindow, count);
            if (window > 0)
            {
                for (int start = 0; start < count; start++)
                {
                    int sCurves = 0;
                    for (int offset = 0; offset < window; offset++)
                        if (FamilyOf(sequence[(start + offset) % count]) == TrackEncounterFamily.SCurveFlow)
                            sCurves++;
                    if (sCurves > cfg.MaxSCurveEncountersPerWindow)
                        penalty += (sCurves - cfg.MaxSCurveEncountersPerWindow) * 25;
                }
            }
            return penalty;
        }

        private static void AnalyzeRoute(
            IReadOnlyList<TrackEncounterFamily> families,
            bool circular,
            ResolvedTrackGenerationConfig cfg,
            TrackRhythmSummary summary)
        {
            int count = families.Count;
            if (count == 0) return;
            int run = 1;
            int sRun = families[0] == TrackEncounterFamily.SCurveFlow ? 1 : 0;
            summary.LongestRepeatedFamilyStreak = Mathf.Max(summary.LongestRepeatedFamilyStreak, 1);
            summary.LongestSCurveStreak = Mathf.Max(summary.LongestSCurveStreak, sRun);
            for (int i = 1; i < count; i++)
            {
                if (families[i] == families[i - 1])
                {
                    run++;
                }
                else run = 1;
                sRun = families[i] == TrackEncounterFamily.SCurveFlow
                    ? (families[i - 1] == TrackEncounterFamily.SCurveFlow ? sRun + 1 : 1)
                    : 0;
                summary.LongestRepeatedFamilyStreak = Mathf.Max(summary.LongestRepeatedFamilyStreak, run);
                summary.LongestSCurveStreak = Mathf.Max(summary.LongestSCurveStreak, sRun);
            }

            if (circular && count > 1 && families[0] == families[count - 1])
            {
                int prefix = 1, suffix = 1;
                while (prefix < count && families[prefix] == families[0]) prefix++;
                while (suffix < count && families[count - 1 - suffix] == families[0]) suffix++;
                int seam = Mathf.Min(count, prefix + suffix);
                summary.LongestRepeatedFamilyStreak = Mathf.Max(summary.LongestRepeatedFamilyStreak, seam);
                if (families[0] == TrackEncounterFamily.SCurveFlow)
                    summary.LongestSCurveStreak = Mathf.Max(summary.LongestSCurveStreak, seam);
            }

            int cooldown = Mathf.Max(0, cfg.SameFamilyCooldownEncounters);
            for (int i = 0; i < count && cooldown > 0; i++)
            {
                TrackEncounterFamily current = families[i];
                if (current == TrackEncounterFamily.CornerFlow || current == TrackEncounterFamily.None)
                    continue;
                int lookback = circular
                    ? Mathf.Min(cooldown, count - 1)
                    : Mathf.Min(cooldown, i);
                for (int distance = 1; distance <= lookback; distance++)
                {
                    int previous = circular ? (i - distance + count) % count : i - distance;
                    if (families[previous] != current) continue;
                    summary.RepeatedSpecialFamilyTransitions++;
                    break;
                }
            }

            int window = Mathf.Min(cfg.SCurveDiversityWindow, count);
            int starts = circular ? count : Mathf.Max(1, count - window + 1);
            for (int start = 0; start < starts; start++)
            {
                int sCurves = 0;
                for (int offset = 0; offset < window; offset++)
                {
                    int index = circular ? (start + offset) % count : start + offset;
                    if (index >= count) break;
                    if (families[index] == TrackEncounterFamily.SCurveFlow) sCurves++;
                }
                summary.MaxSCurvesInWindow = Mathf.Max(summary.MaxSCurvesInWindow, sCurves);
            }
        }

        private static void Shuffle<T>(List<T> list, ref Unity.Mathematics.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
