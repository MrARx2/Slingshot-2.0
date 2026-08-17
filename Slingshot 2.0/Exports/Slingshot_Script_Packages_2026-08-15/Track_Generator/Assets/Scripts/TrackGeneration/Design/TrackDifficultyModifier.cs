using UnityEngine;

namespace TrackGeneration.Design
{
    /// <summary>Editor-time difficulty modifier levels.</summary>
    public enum TrackDifficultyLevel
    {
        Easy,
        Normal,
        Hard,
        Extreme
    }

    /// <summary>
    /// Editor-time difficulty modifiers: applying one CHANGES the actual designer
    /// settings fields once (multiplicatively) and is then done — difficulty is never
    /// a hidden runtime switch. All results remain clamped by the rulebook during
    /// resolution; Extreme is not permission to create broken geometry.
    /// </summary>
    public static class TrackDifficultyModifier
    {
        /// <summary>
        /// Applies the difficulty multipliers to <paramref name="s"/> in place.
        /// Normal is a no-op. Marks the settings as modified since their preset.
        /// </summary>
        public static void Apply(TrackDesignerSettings s, TrackDifficultyLevel level)
        {
            if (s == null || level == TrackDifficultyLevel.Normal) return;

            float roadWidth, minRadius, approach, recovery, spacing, turnCounts, maxFeatures, compound, cornerSeq;
            switch (level)
            {
                case TrackDifficultyLevel.Easy:
                    roadWidth = 1.15f; minRadius = 1.25f; approach = 1.25f; recovery = 1.25f;
                    spacing = 1.2f; turnCounts = 0.8f; maxFeatures = 0.75f; compound = 0.5f; cornerSeq = 1f;
                    break;
                case TrackDifficultyLevel.Hard:
                    roadWidth = 0.95f; minRadius = 0.85f; approach = 0.9f; recovery = 0.9f;
                    spacing = 1f; turnCounts = 1.15f; maxFeatures = 1f; compound = 1.25f; cornerSeq = 1.2f;
                    break;
                default: // Extreme
                    roadWidth = 0.85f; minRadius = 0.7f; approach = 0.8f; recovery = 0.8f;
                    spacing = 1f; turnCounts = 1.3f; maxFeatures = 1f; compound = 1.5f; cornerSeq = 1.35f;
                    break;
            }

            s.Road.FlatCenterWidth *= roadWidth; // difficulty narrows/widens the drivable floor, not the walls
            s.Corners.MinCurveRadius *= minRadius;
            s.Corners.MaxCurveRadius = Mathf.Max(s.Corners.MaxCurveRadius, s.Corners.MinCurveRadius);

            s.Transitions.DefaultApproachSeconds *= approach;
            s.Transitions.DefaultRecoverySeconds *= recovery;
            s.Transitions.DangerousSpacingSeconds *= spacing;

            s.Layout.MinTurnCount = Mathf.RoundToInt(s.Layout.MinTurnCount * turnCounts);
            s.Layout.MaxTurnCount = Mathf.RoundToInt(s.Layout.MaxTurnCount * turnCounts);
            s.Layout.CornerSequenceChance *= cornerSeq;

            s.Features.MaxFeatureGroups = Mathf.Max(s.Features.MinFeatureGroups,
                Mathf.RoundToInt(s.Features.MaxFeatureGroups * maxFeatures));
            s.Features.CompoundFeatureChance *= compound;

            ScaleFeatureMaximum(s.Features.Jumps, maxFeatures);
            ScaleFeatureMaximum(s.Features.Loops, maxFeatures);
            ScaleFeatureMaximum(s.Features.Corkscrews, maxFeatures);
            ScaleFeatureMaximum(s.Features.Spirals, maxFeatures);
            ScaleFeatureMaximum(s.Features.HalfLoops, maxFeatures);

            s.ModifiedSincePreset = true;
            s.Sanitize();
        }

        private static void ScaleFeatureMaximum(TrackFeatureRule rule, float factor)
        {
            if (rule == null || Mathf.Approximately(factor, 1f)) return;
            rule.MaximumCount = Mathf.Max(rule.MinimumCount, Mathf.RoundToInt(rule.MaximumCount * factor));
        }
    }
}
