using UnityEngine;

namespace TrackGeneration.Design
{
    /// <summary>
    /// Pure translation from a designer-facing vertical profile into elevation-planner
    /// intent. It deliberately contains no safety values: legal slope, curvature,
    /// clearance and closure limits continue to come exclusively from the rulebook.
    /// </summary>
    public readonly struct VerticalGenerationIntent
    {
        public readonly VerticalGenerationProfile Profile;
        public readonly float TargetAmplitude;
        public readonly int MinMajorSections;
        public readonly int MaxMajorSections;
        public readonly float PreferredCarrierLength;
        public readonly float RisingTargetMinimum;
        public readonly float RisingTargetMaximum;
        public readonly float RecoveryThreshold;
        public readonly float RecoveryTargetFraction;

        public VerticalGenerationIntent(
            VerticalGenerationProfile profile,
            float targetAmplitude,
            int minMajorSections,
            int maxMajorSections,
            float preferredCarrierLength,
            float risingTargetMinimum,
            float risingTargetMaximum,
            float recoveryThreshold,
            float recoveryTargetFraction)
        {
            Profile = profile;
            TargetAmplitude = targetAmplitude;
            MinMajorSections = minMajorSections;
            MaxMajorSections = maxMajorSections;
            PreferredCarrierLength = preferredCarrierLength;
            RisingTargetMinimum = risingTargetMinimum;
            RisingTargetMaximum = risingTargetMaximum;
            RecoveryThreshold = recoveryThreshold;
            RecoveryTargetFraction = recoveryTargetFraction;
        }
    }

    public static class VerticalGenerationProfilePolicy
    {
        public static VerticalGenerationIntent Resolve(
            VerticalGenerationProfile profile,
            float authoredAmplitude,
            int authoredMinimum,
            int authoredMaximum)
        {
            authoredAmplitude = Mathf.Max(0f, authoredAmplitude);
            authoredMinimum = Mathf.Clamp(authoredMinimum, 0, 16);
            authoredMaximum = Mathf.Clamp(authoredMaximum, authoredMinimum, 16);

            switch (profile)
            {
                case VerticalGenerationProfile.Subtle:
                {
                    int minimum = Mathf.Max(0, authoredMinimum - 1);
                    int maximum = Mathf.Max(minimum, authoredMaximum - 1);
                    return new VerticalGenerationIntent(
                        profile,
                        authoredAmplitude * 0.65f,
                        minimum,
                        maximum,
                        140f,
                        0.25f,
                        0.68f,
                        0.42f,
                        0.58f);
                }

                case VerticalGenerationProfile.Extreme:
                {
                    int minimum = Mathf.Min(16, authoredMinimum + 1);
                    int maximum = Mathf.Clamp(authoredMaximum + 1, minimum, 16);
                    return new VerticalGenerationIntent(
                        profile,
                        authoredAmplitude * 1.35f,
                        minimum,
                        maximum,
                        160f,
                        0.55f,
                        1f,
                        0.62f,
                        0.22f);
                }

                default:
                    // These are the exact historical constants from PlanElevation.
                    // Do not "clean them up" without intentionally versioning recipes.
                    return new VerticalGenerationIntent(
                        VerticalGenerationProfile.Balanced,
                        authoredAmplitude,
                        authoredMinimum,
                        authoredMaximum,
                        100f,
                        0.4f,
                        1f,
                        0.5f,
                        0.4f);
            }
        }
    }
}
