using UnityEngine;

namespace TrackGeneration.Design
{
    /// <summary>Editor-time size modifier levels.</summary>
    public enum TrackSizeLevel
    {
        Small,
        Medium,
        Large,
        Huge
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
            ScaleRule(s.Features.Chicanes, features);
            ScaleRule(s.Features.SCurves, features);
            ScaleRule(s.Features.Hairpins, features);
            // Explicit RequiredPatterns are deliberate designer demands — never scaled.

            s.Branches.MinBranchGroups = Mathf.RoundToInt(s.Branches.MinBranchGroups * branches);
            s.Branches.MaxBranchGroups = Mathf.RoundToInt(s.Branches.MaxBranchGroups * branches);

            s.Elevation.MinMajorElevationSections = Mathf.RoundToInt(s.Elevation.MinMajorElevationSections * elevSections);
            s.Elevation.MaxMajorElevationSections = Mathf.RoundToInt(s.Elevation.MaxMajorElevationSections * elevSections);
            s.Elevation.TargetElevationAmplitude *= amplitude;
            ScaleRule(s.Elevation.Crests, features);
            ScaleRule(s.Elevation.Bridges, features);
            ScaleRule(s.Elevation.Underpasses, features);

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
