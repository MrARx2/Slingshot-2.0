using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using UnityEngine;

namespace TrackGeneration.Tests
{
    public class TrackRhythmTests
    {
        private static ResolvedTrackGenerationConfig RhythmConfig() =>
            new ResolvedTrackGenerationConfig
            {
                EnforceProceduralRhythm = true,
                MaxConsecutiveSCurveEncounters = 1,
                SCurveDiversityWindow = 5,
                MaxSCurveEncountersPerWindow = 2,
                SameFamilyCooldownEncounters = 1
            };

        [Test]
        public void ReportedSeed_InterleavesSCurveFamilyWhenAlternativesExist()
        {
            var source = new List<TrackPatternType>
            {
                TrackPatternType.SCurve,
                TrackPatternType.AlternatingRadiusSequence,
                TrackPatternType.FullLoop,
                TrackPatternType.JumpGap,
                TrackPatternType.Chicane,
                TrackPatternType.Spiral
            };
            var rng = new Unity.Mathematics.Random(unchecked((uint)-1334513275));

            List<TrackPatternType> arranged = TrackRhythm.ArrangeFeatureSequence(
                source, RhythmConfig(), ref rng, out int penalty);

            Assert.AreEqual(0, penalty, "This feature multiset has enough alternatives to eliminate repetition.");
            CollectionAssert.AreEquivalent(source, arranged, "Rhythm arrangement must preserve every requested feature.");
            for (int i = 1; i < arranged.Count; i++)
                Assert.IsFalse(
                    TrackRhythm.IsSCurveFamily(arranged[i - 1]) &&
                    TrackRhythm.IsSCurveFamily(arranged[i]),
                    $"S-flow encounters remained adjacent at {i - 1}/{i}: {string.Join(", ", arranged)}");
        }

        [Test]
        public void ExplicitImpossibleRequest_IsPreservedAndReportedRatherThanDropped()
        {
            var source = new List<TrackPatternType>
            {
                TrackPatternType.SCurve,
                TrackPatternType.AlternatingRadiusSequence,
                TrackPatternType.SCurve
            };
            var rng = new Unity.Mathematics.Random(77u);

            List<TrackPatternType> arranged = TrackRhythm.ArrangeFeatureSequence(
                source, RhythmConfig(), ref rng, out int penalty);

            CollectionAssert.AreEquivalent(source, arranged);
            Assert.Greater(penalty, 0,
                "An all-S-flow explicit request should be reported as impossible to diversify, not silently altered.");
        }

        [Test]
        public void SameFamilyCooldown_SeparatesRepeatedSpecialEncountersWhenPossible()
        {
            ResolvedTrackGenerationConfig cfg = RhythmConfig();
            cfg.SameFamilyCooldownEncounters = 2;
            var source = new List<TrackPatternType>
            {
                TrackPatternType.FullLoop,
                TrackPatternType.Corkscrew,
                TrackPatternType.JumpGap,
                TrackPatternType.Chicane,
                TrackPatternType.Hairpin,
                TrackPatternType.Spiral
            };
            var rng = new Unity.Mathematics.Random(4242u);

            List<TrackPatternType> arranged = TrackRhythm.ArrangeFeatureSequence(
                source, cfg, ref rng, out int penalty);

            Assert.AreEqual(0, penalty);
            CollectionAssert.AreEquivalent(source, arranged);
            int firstInversion = arranged.FindIndex(
                pattern => TrackRhythm.FamilyOf(pattern) == TrackEncounterFamily.Inversion);
            int secondInversion = arranged.FindLastIndex(
                pattern => TrackRhythm.FamilyOf(pattern) == TrackEncounterFamily.Inversion);
            int directDistance = secondInversion - firstInversion;
            int circularDistance = arranged.Count - directDistance;
            Assert.GreaterOrEqual(Mathf.Min(directDistance, circularDistance), 3,
                $"Two other encounters should separate repeated inversion families around the lap: {string.Join(", ", arranged)}");
        }

        [Test]
        public void TopologyAnalysis_DetectsSemanticSCurveStreakAcrossQuarterBoundary()
        {
            var slots = new List<TopologySlotRecord>
            {
                Slot(0, 0, SemanticElementId.SCurve),
                Slot(1, 1, SemanticElementId.AlternatingRadiusSequence),
                Slot(2, 1, SemanticElementId.VerticalLoop),
                // Structural closure content is deliberately ignored by the classifier.
                Slot(3, 2, SemanticElementId.ClosureTransfer)
            };

            TrackRhythmSummary summary = TrackRhythm.Analyze(slots, RhythmConfig());

            Assert.AreEqual(2, summary.LongestSCurveStreak);
            Assert.Greater(summary.ViolationCount, 0);
            Assert.AreEqual(3, summary.EncounterCount);
            StringAssert.Contains("Road A", summary.RouteTimelines.Single());
        }

        [Test]
        public void AlternatingRadiusSequence_CountsAsOneCompoundEncounter()
        {
            var slots = new List<TopologySlotRecord>
            {
                Slot(0, 0, SemanticElementId.AlternatingRadiusSequence),
                Slot(1, 0, SemanticElementId.OrdinaryCurve)
            };

            TrackRhythmSummary summary = TrackRhythm.Analyze(slots, RhythmConfig());

            Assert.AreEqual(2, summary.EncounterCount);
            Assert.AreEqual(1, summary.LongestSCurveStreak);
            Assert.AreEqual(0, summary.ViolationCount);
        }

        private static TopologySlotRecord Slot(
            int canonicalOrder, int quarter, SemanticElementId realization) =>
            new TopologySlotRecord
            {
                TopologySlotId = "rhythm-" + canonicalOrder,
                CanonicalOrder = canonicalOrder,
                RouteOrder = canonicalOrder,
                QuarterIndex = quarter,
                RoadId = 0,
                OriginalRealization = realization,
                CurrentRealization = realization
            };
    }
}
