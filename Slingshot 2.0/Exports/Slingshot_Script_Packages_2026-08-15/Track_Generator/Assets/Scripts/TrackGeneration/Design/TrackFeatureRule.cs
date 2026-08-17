using System;
using UnityEngine;

namespace TrackGeneration.Design
{
    /// <summary>
    /// Unified count/weight rule for one feature family (loops, corkscrews, jumps, ...).
    /// Replaces the old opaque probability floats:
    /// <list type="bullet">
    /// <item>Required minimum counts are reserved and placed BEFORE optional content.</item>
    /// <item>Optional placements use <see cref="OptionalWeight"/> as a relative selection weight.</item>
    /// <item>A generated track missing a required count is a FAILURE, never a silent success.</item>
    /// <item>Maximum counts are never exceeded.</item>
    /// </list>
    /// </summary>
    [Serializable]
    public class TrackFeatureRule
    {
        [Tooltip("Master switch. Disabled features are never placed, even if counts are set.")]
        public bool Enabled = true;

        [Tooltip("Guaranteed number of this feature on the track. Generation FAILS (with a report) if it cannot fit them — required content is never silently dropped.")]
        [Min(0)] public int MinimumCount;

        [Tooltip("Hard cap for this feature. Optional placements stop at this count.")]
        [Min(0)] public int MaximumCount = 1;

        [Tooltip("Relative selection weight for OPTIONAL placements beyond the minimum (weights are relative to the other enabled features, not percentages).")]
        [Min(0f)] public float OptionalWeight = 1f;

        [Tooltip("Feature-specific dangerous-section spacing override in seconds. -1 = use the default dangerous-feature spacing.")]
        public float MinimumSpacingOverrideSeconds = -1f;

        [Tooltip("Whether this feature may appear as an element INSIDE a compound pattern (e.g. Loop → Corkscrew).")]
        public bool AllowInCompoundPatterns = true;

        public TrackFeatureRule() { }

        public TrackFeatureRule(bool enabled, int min, int max, float weight, bool allowInCompound = true)
        {
            Enabled = enabled;
            MinimumCount = min;
            MaximumCount = max;
            OptionalWeight = weight;
            AllowInCompoundPatterns = allowInCompound;
        }

        /// <summary>Effective minimum (0 when disabled).</summary>
        public int EffectiveMinimum => Enabled ? Mathf.Max(0, MinimumCount) : 0;

        /// <summary>Effective maximum (0 when disabled).</summary>
        public int EffectiveMaximum => Enabled ? Mathf.Max(EffectiveMinimum, MaximumCount) : 0;

        /// <summary>Keeps the rule internally consistent after inspector edits.</summary>
        public void Sanitize()
        {
            MinimumCount = Mathf.Max(0, MinimumCount);
            MaximumCount = Mathf.Max(MinimumCount, MaximumCount);
            OptionalWeight = Mathf.Max(0f, OptionalWeight);
        }

        public TrackFeatureRule Clone() => (TrackFeatureRule)MemberwiseClone();
    }
}
