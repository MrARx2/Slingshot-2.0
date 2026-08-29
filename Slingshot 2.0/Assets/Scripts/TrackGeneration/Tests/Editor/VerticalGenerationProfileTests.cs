using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Tests
{
    [Category("V2FastGate")]
    public class VerticalGenerationProfileTests
    {
        [Test]
        public void Balanced_ReproducesHistoricalPlannerIntentExactly()
        {
            VerticalGenerationIntent intent = VerticalGenerationProfilePolicy.Resolve(
                VerticalGenerationProfile.Balanced,
                320f,
                2,
                5);

            Assert.AreEqual(VerticalGenerationProfile.Balanced, intent.Profile);
            Assert.AreEqual(320f, intent.TargetAmplitude, 0.0001f);
            Assert.AreEqual(2, intent.MinMajorSections);
            Assert.AreEqual(5, intent.MaxMajorSections);
            Assert.AreEqual(100f, intent.PreferredCarrierLength, 0.0001f);
            Assert.AreEqual(0.4f, intent.RisingTargetMinimum, 0.0001f);
            Assert.AreEqual(1f, intent.RisingTargetMaximum, 0.0001f);
            Assert.AreEqual(0.5f, intent.RecoveryThreshold, 0.0001f);
            Assert.AreEqual(0.4f, intent.RecoveryTargetFraction, 0.0001f);
        }

        [Test]
        public void Profiles_SteerContentWithoutOwningSafetyLimits()
        {
            VerticalGenerationIntent subtle = VerticalGenerationProfilePolicy.Resolve(
                VerticalGenerationProfile.Subtle, 320f, 2, 5);
            VerticalGenerationIntent balanced = VerticalGenerationProfilePolicy.Resolve(
                VerticalGenerationProfile.Balanced, 320f, 2, 5);
            VerticalGenerationIntent extreme = VerticalGenerationProfilePolicy.Resolve(
                VerticalGenerationProfile.Extreme, 320f, 2, 5);

            Assert.Less(subtle.TargetAmplitude, balanced.TargetAmplitude);
            Assert.Greater(extreme.TargetAmplitude, balanced.TargetAmplitude);
            Assert.Less(subtle.MaxMajorSections, balanced.MaxMajorSections);
            Assert.Greater(extreme.MinMajorSections, balanced.MinMajorSections);
            Assert.Greater(subtle.PreferredCarrierLength, balanced.PreferredCarrierLength);
            Assert.Greater(extreme.PreferredCarrierLength, subtle.PreferredCarrierLength);

            // The intent type deliberately exposes no climb angle, drop angle,
            // curvature, clearance, or closure value. Those remain rulebook-owned.
            Assert.IsNull(typeof(VerticalGenerationIntent).GetField("MaxClimbAngle"));
            Assert.IsNull(typeof(VerticalGenerationIntent).GetField("MaxDropAngle"));
            Assert.IsNull(typeof(VerticalGenerationIntent).GetField("Clearance"));
        }

        [Test]
        public void Extreme_CountsRemainInsideAuthoredRulebookRange()
        {
            VerticalGenerationIntent intent = VerticalGenerationProfilePolicy.Resolve(
                VerticalGenerationProfile.Extreme,
                320f,
                16,
                16);

            Assert.AreEqual(16, intent.MinMajorSections);
            Assert.AreEqual(16, intent.MaxMajorSections);
        }

        [Test]
        public void UnknownProfile_FallsBackToBalancedCompatibility()
        {
            VerticalGenerationIntent intent = VerticalGenerationProfilePolicy.Resolve(
                (VerticalGenerationProfile)999,
                250f,
                1,
                4);

            Assert.AreEqual(VerticalGenerationProfile.Balanced, intent.Profile);
            Assert.AreEqual(250f, intent.TargetAmplitude, 0.0001f);
            Assert.AreEqual(1, intent.MinMajorSections);
            Assert.AreEqual(4, intent.MaxMajorSections);
        }

        [Test]
        public void SilhouetteMeasurement_ReportsRangeMajorCarriersAndSustainedGrade()
        {
            var section = MakeSection(
                0,
                0,
                "Straight_Climb20m",
                20f,
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 10f, 100f),
                new Vector3(0f, 20f, 200f));

            VerticalSilhouetteMeasurement measurement = VerticalSilhouetteDiagnostics.Measure(
                new List<GeneratedTrackSection> { section });

            Assert.IsTrue(measurement.HasGeometry);
            Assert.AreEqual(20f, measurement.ElevationRange, 0.001f);
            Assert.AreEqual(1, measurement.PlannedMajorCarrierCount);
            Assert.That(measurement.LongestSustainedGradeLength, Is.InRange(200f, 202f));
            Assert.AreEqual(20f, measurement.LongestSustainedGradeRise, 0.001f);
            Assert.That(measurement.LongestSustainedGradeAngleDegrees, Is.InRange(5.6f, 5.8f));
        }

        [Test]
        public void SilhouetteMeasurement_DoesNotBridgeAirGaps()
        {
            var first = MakeSection(
                0,
                0,
                "Straight_Climb10m",
                10f,
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 10f, 100f));
            var gap = MakeSection(
                1,
                0,
                "AirGap",
                0f,
                new Vector3(0f, 10f, 100f),
                new Vector3(0f, 10f, 300f));
            // IsEmptySpace is derived from the authored section type. Build the
            // fixture through that same source of truth instead of trying to
            // override the read-only convenience property.
            gap.Definition.SectionType = TrackMacroSectionType.AirGap;
            var second = MakeSection(
                2,
                0,
                "Straight_Climb20m",
                20f,
                new Vector3(0f, 10f, 300f),
                new Vector3(0f, 30f, 400f));

            VerticalSilhouetteMeasurement measurement = VerticalSilhouetteDiagnostics.Measure(
                new List<GeneratedTrackSection> { first, gap, second });

            Assert.AreEqual(30f, measurement.ElevationRange, 0.001f);
            Assert.AreEqual(2, measurement.PlannedMajorCarrierCount);
            Assert.That(measurement.LongestSustainedGradeLength, Is.InRange(101f, 103f));
            Assert.AreEqual(20f, measurement.LongestSustainedGradeRise, 0.001f);
        }

        private static GeneratedTrackSection MakeSection(
            int sectionIndex,
            int roadId,
            string debugName,
            float elevationChange,
            params Vector3[] positions)
        {
            var frames = new TrackConnectionFrame[positions.Length];
            float arcLength = 0f;
            for (int i = 0; i < positions.Length; i++)
            {
                if (i > 0)
                    arcLength += Vector3.Distance(positions[i - 1], positions[i]);
                TrackConnectionFrame frame = TrackConnectionFrame.Origin(40f);
                frame.Position = positions[i];
                frame.ArcLength = arcLength;
                frames[i] = frame;
            }

            return new GeneratedTrackSection
            {
                SectionIndex = sectionIndex,
                RoadId = roadId,
                Definition = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Straight,
                    DebugName = debugName,
                    ElevationChange = elevationChange
                },
                StartFrame = frames[0],
                EndFrame = frames[frames.Length - 1],
                SubdivisionFrames = frames
            };
        }
    }
}
