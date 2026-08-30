using UnityEngine;

namespace TrackGeneration.Design
{
    /// <summary>Editor-time size modifier levels.</summary>
    public enum TrackSizeLevel
    {
        Small,
        Medium,
        Large,
        Huge,

        /// <summary>45–60 s sprint lap (target 52.5 s): FEWER sections and features — never individually shrunken ones.</summary>
        VerySmall
    }

    /// <summary>
    /// Editor-time SIZE modifier: one click scales every parameter that makes a track
    /// big or small — lap time, length cap, turn counts, feature counts, branch counts
    /// and elevation content — so a small track is genuinely small (fewer corners and
    /// features), not just a capped-length track that fails to fit its content.
    /// Like difficulty, this changes the actual fields once and is never a runtime mode.
    /// All results remain clamped by the rulebook during resolution.
    /// </summary>
    public static class TrackSizeModifier
    {
        /// <summary>Applies the size multipliers to <paramref name="s"/> in place. Medium is a no-op.</summary>
        public static void Apply(TrackDesignerSettings s, TrackSizeLevel level)
        {
            if (s == null || level == TrackSizeLevel.Medium) return;

            if (level == TrackSizeLevel.VerySmall)
            {
                ApplyVerySmall(s);
                return;
            }

            float lapTime, lengthCap, turns, features, branches, elevSections, amplitude;
            switch (level)
            {
                case TrackSizeLevel.Small:
                    lapTime = 0.6f; lengthCap = 0.6f; turns = 0.7f;
                    features = 0.6f; branches = 0.5f; elevSections = 0.7f; amplitude = 0.85f;
                    break;
                case TrackSizeLevel.Large:
                    lapTime = 1.35f; lengthCap = 1.35f; turns = 1.2f;
                    features = 1.3f; branches = 1.25f; elevSections = 1.25f; amplitude = 1.15f;
                    break;
                default: // Huge
                    lapTime = 1.8f; lengthCap = 1.75f; turns = 1.5f;
                    features = 1.6f; branches = 1.5f; elevSections = 1.5f; amplitude = 1.3f;
                    break;
            }

            s.Scale.TargetLapTimeSeconds *= lapTime;
            s.Scale.MaxTrackLengthMeters *= lengthCap;

            s.Layout.MinTurnCount = Mathf.RoundToInt(s.Layout.MinTurnCount * turns);
            s.Layout.MaxTurnCount = Mathf.RoundToInt(s.Layout.MaxTurnCount * turns);

            s.Features.MinFeatureGroups = Mathf.RoundToInt(s.Features.MinFeatureGroups * features);
            s.Features.MaxFeatureGroups = Mathf.RoundToInt(s.Features.MaxFeatureGroups * features);
            ScaleRule(s.Features.Jumps, features);
            ScaleRule(s.Features.Loops, features);
            ScaleRule(s.Features.Corkscrews, features);
            ScaleRule(s.Features.Spirals, features);
            ScaleRule(s.Features.HalfLoops, features);
            ScaleRule(s.Features.FullPipes, features);
            ScaleRule(s.Features.Camelbacks, features);
            ScaleRule(s.Features.HeartlineRolls, features);
            ScaleRule(s.Features.ZeroGRolls, features);
            ScaleRule(s.Features.DiveLoops, features);
            ScaleRule(s.Features.Sidewinders, features);
            ScaleRule(s.Features.Wallrides, features);
            ScaleRule(s.Features.Chicanes, features);
            ScaleRule(s.Features.SCurves, features);
            ScaleRule(s.Features.Hairpins, features);
            ScaleRule(s.Features.WideTurnarounds, features);
            ScaleRule(s.Features.Horseshoes, features);
            ScaleRule(s.Features.HalfHelixTurnarounds, features);
            ScaleRule(s.Features.Cutbacks, features);
            // Explicit RequiredPatterns are deliberate designer demands — never scaled.

            s.Quarters.MinimumDualQuarterCount = Mathf.Clamp(Mathf.RoundToInt(s.Quarters.MinimumDualQuarterCount * branches), 0, 4);
            s.Quarters.MaximumDualQuarterCount = Mathf.Clamp(Mathf.RoundToInt(s.Quarters.MaximumDualQuarterCount * branches), s.Quarters.MinimumDualQuarterCount, 4);

            s.Elevation.MinMajorElevationSections = Mathf.RoundToInt(s.Elevation.MinMajorElevationSections * elevSections);
            s.Elevation.MaxMajorElevationSections = Mathf.RoundToInt(s.Elevation.MaxMajorElevationSections * elevSections);
            s.Elevation.TargetElevationAmplitude *= amplitude;
            ScaleRule(s.Elevation.Crests, features);
            ScaleRule(s.Elevation.Bridges, features);
            ScaleRule(s.Elevation.Underpasses, features);

            s.ModifiedSincePreset = true;
            s.Sanitize();
        }

        /// <summary>
        /// Very Small: a 45–60 s sprint lap (target 52.5 s). REDUCES the number of
        /// sections and features; it never shrinks individual loops, corkscrews, jumps
        /// or transitions below their safe high-speed values — feature sizes come from
        /// the rulebook windows, which this method does not touch. If locked/required
        /// content cannot fit the short lap, resolution reports the conflict instead of
        /// silently oversizing the track.
        /// </summary>
        private static void ApplyVerySmall(TrackDesignerSettings s)
        {
            s.Scale.TargetLapTimeSeconds = 52.5f;
            // Cap gives the closure solver working room but keeps the lap a sprint:
            // ≈ 75 s at design speed (a technical lap runs well below design speed).
            s.Scale.MaxTrackLengthMeters = s.Scale.DesignSpeedMps * 75f;

            // Turn count ≈ 5–10, style-dependent (proportional, clamped to the window).
            s.Layout.MinTurnCount = Mathf.Clamp(Mathf.RoundToInt(s.Layout.MinTurnCount * 0.5f), 4, 9);
            s.Layout.MaxTurnCount = Mathf.Clamp(Mathf.RoundToInt(s.Layout.MaxTurnCount * 0.5f), s.Layout.MinTurnCount + 1, 10);

            // 1–3 major feature groups, at most one compound, 0–1 dual quarters.
            s.Features.MinFeatureGroups = Mathf.Min(s.Features.MinFeatureGroups, 1);
            s.Features.MaxFeatureGroups = Mathf.Clamp(Mathf.RoundToInt(s.Features.MaxFeatureGroups * 0.4f), 1, 3);
            s.Features.CompoundFeatureChance *= 0.5f;
            s.Features.MaxCompoundElements = Mathf.Min(s.Features.MaxCompoundElements, 2);
            ScaleRule(s.Features.Jumps, 0.5f);
            ScaleRule(s.Features.Loops, 0.5f);
            ScaleRule(s.Features.Corkscrews, 0.5f);
            ScaleRule(s.Features.Spirals, 0.5f);
            ScaleRule(s.Features.HalfLoops, 0.5f);
            ScaleRule(s.Features.FullPipes, 0.5f);
            ScaleRule(s.Features.Camelbacks, 0.5f);
            ScaleRule(s.Features.HeartlineRolls, 0.5f);
            ScaleRule(s.Features.ZeroGRolls, 0.5f);
            ScaleRule(s.Features.DiveLoops, 0.4f);
            ScaleRule(s.Features.Sidewinders, 0.4f);
            ScaleRule(s.Features.Wallrides, 0.6f);
            ScaleRule(s.Features.Chicanes, 0.5f);
            ScaleRule(s.Features.SCurves, 0.5f);
            // Hairpins are cut hardest: their near-reversal arcs are driven far below
            // design speed, and a hairpin-heavy style (Switchback) blows the 45–60s
            // window on ESTIMATED time long before it blows it on distance.
            ScaleRule(s.Features.Hairpins, 0.35f);
            ScaleRule(s.Features.WideTurnarounds, 0.35f);
            ScaleRule(s.Features.Horseshoes, 0.35f);
            ScaleRule(s.Features.HalfHelixTurnarounds, 0.30f);
            ScaleRule(s.Features.Cutbacks, 0.35f);
            s.Features.Hairpins.MaximumCount = Mathf.Min(s.Features.Hairpins.MaximumCount, 2);

            s.Quarters.MinimumDualQuarterCount = 0;
            s.Quarters.MaximumDualQuarterCount = Mathf.Min(s.Quarters.MaximumDualQuarterCount, 1);

            // 1–4 major elevation groups; amplitude trimmed, angles untouched (safety).
            s.Elevation.MinMajorElevationSections = Mathf.Clamp(s.Elevation.MinMajorElevationSections / 2, 1, 3);
            s.Elevation.MaxMajorElevationSections = Mathf.Clamp(s.Elevation.MaxMajorElevationSections / 2,
                s.Elevation.MinMajorElevationSections + 1, 4);
            s.Elevation.TargetElevationAmplitude *= 0.7f;
            ScaleRule(s.Elevation.Crests, 0.5f);
            ScaleRule(s.Elevation.Bridges, 0.5f);
            ScaleRule(s.Elevation.Underpasses, 0.5f);

            s.ModifiedSincePreset = true;
            s.Sanitize();
        }

        private static void ScaleRule(TrackFeatureRule rule, float factor)
        {
            if (rule == null || Mathf.Approximately(factor, 1f)) return;

            rule.MinimumCount = Mathf.RoundToInt(rule.MinimumCount * factor);

            // Shrinking never disables a feature the designer had enabled (a maximum of
            // at least 1 keeps it possible); growing raises the ceiling proportionally.
            int newMax = Mathf.RoundToInt(rule.MaximumCount * factor);
            if (rule.MaximumCount >= 1) newMax = Mathf.Max(1, newMax);
            rule.MaximumCount = Mathf.Max(rule.MinimumCount, newMax);
        }
    }
}
