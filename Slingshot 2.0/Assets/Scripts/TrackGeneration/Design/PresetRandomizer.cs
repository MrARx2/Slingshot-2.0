using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Macro;

namespace TrackGeneration.Design
{
    /// <summary>How Random preset selectors resolve.</summary>
    public enum PresetRandomizationMode
    {
        [Tooltip("Default: compatibility-scored selection — coherent, feasible combinations that are likely to generate successfully.")]
        Curated,

        [Tooltip("Allows unusual combinations. Still obeys hard physical constraints and validation — never deliberately broken geometry.")]
        Unrestricted
    }

    /// <summary>
    /// Resolves 'Random' preset selectors into concrete choices. Random is NOT blind
    /// uniform selection: every candidate combination is actually resolved against the
    /// rulebook (with the current locked groups merged in), hard errors disqualify it,
    /// and Curated mode additionally scores style/size/difficulty coherence and variety
    /// against the current settings. Deterministic via the PresetResolution seed stream.
    /// </summary>
    public static class PresetRandomizer
    {
        public const string RandomOption = "Random";

        /// <summary>The request: each selector is either a concrete choice or Random.</summary>
        public class Request
        {
            public bool StyleIsRandom;
            public string Style = TrackStylePresetLibrary.Balanced;
            public bool DifficultyIsRandom;
            public TrackDifficultyLevel Difficulty = TrackDifficultyLevel.Normal;
            public bool SizeIsRandom;
            public TrackSizeLevel Size = TrackSizeLevel.Medium;
            public PresetRandomizationMode Mode = PresetRandomizationMode.Curated;
        }

        /// <summary>The resolved concrete selection plus the reasons it was chosen.</summary>
        public class Selection
        {
            public string Style;
            public TrackDifficultyLevel Difficulty;
            public TrackSizeLevel Size;
            public List<string> Reasons = new List<string>();

            public string RequestedSummary = "";
            public override string ToString() => $"{Style} / {Difficulty} / {Size}";
        }

        /// <summary>
        /// Resolves the request. <paramref name="current"/> and <paramref name="locks"/>
        /// describe the designer's present state — locked groups are merged into every
        /// candidate before feasibility is evaluated, so the resolver can never pick a
        /// combination that fights the locks.
        /// </summary>
        public static Selection Resolve(Request req, TrackConfig rulebook, TrackDesignerSettings current,
            SettingsLockState locks, ref Unity.Mathematics.Random rng)
        {
            var styles = req.StyleIsRandom ? TrackStylePresetLibrary.BuiltInNames : new[] { req.Style };
            var difficulties = req.DifficultyIsRandom
                ? new[] { TrackDifficultyLevel.Easy, TrackDifficultyLevel.Normal, TrackDifficultyLevel.Hard, TrackDifficultyLevel.Extreme }
                : new[] { req.Difficulty };
            var sizes = req.SizeIsRandom
                ? new[] { TrackSizeLevel.VerySmall, TrackSizeLevel.Small, TrackSizeLevel.Medium, TrackSizeLevel.Large, TrackSizeLevel.Huge }
                : new[] { req.Size };

            var candidates = new List<(Selection sel, float score, int errors)>();

            foreach (var style in styles)
            {
                foreach (var diff in difficulties)
                {
                    foreach (var size in sizes)
                    {
                        var settings = BuildCandidate(style, diff, size, current, locks);
                        var resolved = ResolvedTrackGenerationConfig.Resolve(rulebook, settings);

                        int errors = 0, warnings = 0;
                        foreach (var issue in resolved.Issues)
                        {
                            if (issue.Severity == ResolvedIssueSeverity.Error) errors++;
                            else if (issue.Severity == ResolvedIssueSeverity.Warning) warnings++;
                        }

                        float score = -errors * 100f - warnings * 2f;
                        if (req.Mode == PresetRandomizationMode.Curated)
                            score += CoherenceScore(style, diff, size, current);

                        var sel = new Selection { Style = style, Difficulty = diff, Size = size };
                        candidates.Add((sel, score, errors));
                    }
                }
            }

            // Feasible candidates first; if literally everything has hard errors, take
            // the least-broken so the caller can report the conflict honestly.
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            float bestScore = candidates[0].score;

            List<(Selection sel, float score, int errors)> pool;
            if (req.Mode == PresetRandomizationMode.Unrestricted)
            {
                pool = candidates.FindAll(c => c.errors == 0);
                if (pool.Count == 0) pool = candidates.GetRange(0, 1);
            }
            else
            {
                pool = candidates.FindAll(c => c.errors == 0 && c.score >= bestScore - 2.5f);
                if (pool.Count == 0) pool = candidates.GetRange(0, 1);
            }

            var chosen = pool[rng.NextInt(0, pool.Count)];
            var result = chosen.sel;

            result.RequestedSummary =
                $"Style: {(req.StyleIsRandom ? RandomOption : req.Style)}, " +
                $"Difficulty: {(req.DifficultyIsRandom ? RandomOption : req.Difficulty.ToString())}, " +
                $"Size: {(req.SizeIsRandom ? RandomOption : req.Size.ToString())}";

            if (chosen.errors == 0)
                result.Reasons.Add("Resolves without hard rulebook conflicts");
            else
                result.Reasons.Add($"NO fully feasible combination exists — least-conflicted pick ({chosen.errors} hard errors remain)");
            if (locks != null && locks.LockedCount > 0)
                result.Reasons.Add($"Respects {locks.LockedCount} locked settings group(s): {string.Join(", ", locks.LockedGroupNames())}");
            if (req.Mode == PresetRandomizationMode.Curated && !string.IsNullOrEmpty(current?.AppliedPresetName)
                && result.Style != current.AppliedPresetName)
                result.Reasons.Add($"Variety over the current '{current.AppliedPresetName}' settings");

            return result;
        }

        /// <summary>Builds the full settings a candidate combination would produce (locks merged).</summary>
        public static TrackDesignerSettings BuildCandidate(string style, TrackDifficultyLevel difficulty,
            TrackSizeLevel size, TrackDesignerSettings current, SettingsLockState locks)
        {
            var settings = TrackStylePresetLibrary.Create(style);
            TrackDifficultyModifier.Apply(settings, difficulty);
            TrackSizeModifier.Apply(settings, size);

            // Locked groups keep the designer's current values.
            if (current != null && locks != null && locks.LockedCount > 0)
            {
                var baseline = current.Clone();
                var merged = baseline; // start from current, apply candidate into unlocked groups
                PresetApplicator.Apply(merged, settings, locks);
                return merged;
            }

            settings.Sanitize();
            return settings;
        }

        /// <summary>
        /// Curated coherence heuristics: combinations known to fight each other score
        /// lower (feature-monumental styles inside sprint laps, hairpin-heavy styles at
        /// Extreme sizes, …). Small numbers — feasibility errors always dominate.
        /// </summary>
        private static float CoherenceScore(string style, TrackDifficultyLevel diff, TrackSizeLevel size,
            TrackDesignerSettings current)
        {
            float score = 0f;

            bool sprint = size == TrackSizeLevel.VerySmall;
            bool huge = size == TrackSizeLevel.Huge;

            switch (style)
            {
                case TrackStylePresetLibrary.Rollercoaster:
                    if (sprint) score -= 6f;   // monumental feature minimums vs a 52 s lap
                    if (huge) score += 2f;
                    break;
                case TrackStylePresetLibrary.Velocity:
                    if (sprint) score -= 3f;   // enormous radii need room
                    break;
                case TrackStylePresetLibrary.Technical:
                case TrackStylePresetLibrary.Switchback:
                    if (sprint) score += 2f;   // dense corners fit sprints naturally
                    if (huge && diff == TrackDifficultyLevel.Extreme) score -= 2f; // grueling combination
                    break;
            }

            if (diff == TrackDifficultyLevel.Extreme && sprint) score -= 1.5f;

            // Variety: prefer a different style than the current settings came from.
            if (current != null && !string.IsNullOrEmpty(current.AppliedPresetName) &&
                style == current.AppliedPresetName)
                score -= 1f;

            return score;
        }
    }
}
