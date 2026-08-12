using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage A acceptance: every pattern's measured plan result (built with the SAME
    /// section builders the candidate uses) agrees with its legacy stamped scalars,
    /// known contracts hold (inline corkscrew ≈ 0° heading with whole-revolution roll;
    /// Immelmann ≈ ±180°), measurement is deterministic and rng-free, and semantic
    /// identity is stamped.
    /// </summary>
    public class PlanResultTruthTests
    {
        private static ResolvedTrackGenerationConfig Cfg(out TrackConfig config)
        {
            config = TrackGenerationTestUtil.CreateConfig();
            return ResolvedTrackGenerationConfig.Resolve(config,
                TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
        }

        private static FeaturePlanResult PlanAndMeasure(ITrackFeaturePattern pattern,
            ResolvedTrackGenerationConfig cfg, uint seed, out List<TrackMacroSectionDefinition> defs)
        {
            defs = new List<TrackMacroSectionDefinition>();
            var rng = new Unity.Mathematics.Random(seed);
            Assert.IsTrue(pattern.TryPlan(cfg, $"Test_{pattern.PatternType}_{seed}", ref rng, defs),
                $"{pattern.PatternType} failed to plan under the test config (seed {seed}).");
            Assert.Greater(defs.Count, 0);

            FeaturePlanning.StampRange(defs, 0, FeaturePlanning.ElementOf(pattern.PatternType));
            var entry = TrackConnectionFrame.Origin(defs[0].Width);
            return FeaturePlanning.ComputePlanResult(defs, 0, defs.Count, entry,
                FrameBuildContext.From(cfg), defs[0].SemanticElement, defs[0].PatternId);
        }

        [Test]
        public void MeasuredExitMatchesStampedScalars_RotationalPatterns()
        {
            var cfg = Cfg(out var config);
            try
            {
                var patterns = new ITrackFeaturePattern[]
                {
                    new CorkscrewPattern(), new FullLoopPattern(), new HalfLoopRolloutPattern()
                };
                foreach (var pattern in patterns)
                for (uint seed = 1; seed <= 16; seed++)
                {
                    var result = PlanAndMeasure(pattern, cfg, seed, out var defs);
                    Assert.IsFalse(result.Failed, $"{pattern.PatternType} seed {seed}: {result.FailureReason}");

                    // Single rotational def carries the legacy stamp — the measurement
                    // must reproduce it (same builder, same entry convention).
                    if (defs.Count == 1 && defs[0].SectionType == TrackMacroSectionType.RotationalEvent)
                    {
                        var d = defs[0];
                        Assert.AreEqual(d.PlanHorizontalLength, result.PlanDisplacement.y, 0.1f,
                            $"{pattern.PatternType} seed {seed}: forward displacement");
                        Assert.AreEqual(d.PlanLateralOffset, result.PlanDisplacement.x, 0.1f,
                            $"{pattern.PatternType} seed {seed}: lateral displacement");
                        Assert.AreEqual(d.ElevationChange, result.ElevationChange, 0.1f,
                            $"{pattern.PatternType} seed {seed}: elevation");
                        Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(d.TurnAngle, result.HeadingContributionDeg)), 0.1f,
                            $"{pattern.PatternType} seed {seed}: heading");
                    }
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void InlineCorkscrew_HeadingNeutral_WholeRevolutionRoll()
        {
            var cfg = Cfg(out var config);
            try
            {
                for (uint seed = 1; seed <= 16; seed++)
                {
                    var result = PlanAndMeasure(new CorkscrewPattern(), cfg, seed, out _);
                    Assert.IsFalse(result.Failed, result.FailureReason);
                    Assert.AreEqual(SemanticElementId.InlineCorkscrew, result.Element);

                    Assert.LessOrEqual(Mathf.Abs(result.HeadingContributionDeg), 0.5f,
                        $"Seed {seed}: inline corkscrew must be heading-neutral, measured {result.HeadingContributionDeg:F2}°.");

                    float roll = Mathf.Abs(result.AccumulatedRollDeg);
                    Assert.GreaterOrEqual(roll, 359f, $"Seed {seed}: expected at least one full revolution.");
                    float rem = Mathf.Abs(Mathf.DeltaAngle(result.AccumulatedRollDeg, 0f));
                    Assert.LessOrEqual(rem, 1f,
                        $"Seed {seed}: roll {result.AccumulatedRollDeg:F1}° is not a whole revolution — the road exits rolled.");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void Immelmann_Reverses_Heading()
        {
            var cfg = Cfg(out var config);
            try
            {
                for (uint seed = 1; seed <= 16; seed++)
                {
                    var result = PlanAndMeasure(new HalfLoopRolloutPattern(), cfg, seed, out _);
                    Assert.IsFalse(result.Failed, result.FailureReason);
                    Assert.AreEqual(SemanticElementId.Immelmann, result.Element);

                    float toReversal = Mathf.Abs(Mathf.DeltaAngle(Mathf.Abs(result.HeadingContributionDeg), 180f));
                    Assert.LessOrEqual(toReversal, 0.5f,
                        $"Seed {seed}: Immelmann must reverse heading; measured {result.HeadingContributionDeg:F2}°.");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void Measurement_IsDeterministic_And_RngFree()
        {
            var cfg = Cfg(out var config);
            try
            {
                // Identical seeds → identical results; and measuring twice from the same
                // defs is bit-identical (ComputePlanResult draws no randomness).
                var a = PlanAndMeasure(new CorkscrewPattern(), cfg, 7, out var defsA);
                var again = FeaturePlanning.ComputePlanResult(defsA, 0, defsA.Count,
                    TrackConnectionFrame.Origin(defsA[0].Width), FrameBuildContext.From(cfg),
                    a.Element, a.PatternId);

                Assert.AreEqual(a.HeadingContributionDeg, again.HeadingContributionDeg, 0f);
                Assert.AreEqual(a.PlanDisplacement, again.PlanDisplacement);
                Assert.AreEqual(a.ArcLength, again.ArcLength, 0f);
                Assert.AreEqual(a.AccumulatedRollDeg, again.AccumulatedRollDeg, 0f);

                var b = PlanAndMeasure(new CorkscrewPattern(), cfg, 7, out _);
                Assert.AreEqual(a.HeadingContributionDeg, b.HeadingContributionDeg, 0f);
                Assert.AreEqual(a.ArcLength, b.ArcLength, 0f);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void RngStream_Unaffected_By_Stamping()
        {
            var cfg = Cfg(out var config);
            try
            {
                // Stage A must not add draws: planning with stamping+measurement leaves
                // the rng in exactly the state planning alone left it.
                var rngA = new Unity.Mathematics.Random(1234);
                var defsA = new List<TrackMacroSectionDefinition>();
                new CorkscrewPattern().TryPlan(cfg, "A", ref rngA, defsA);
                uint afterPlain = rngA.state;

                var rngB = new Unity.Mathematics.Random(1234);
                var defsB = new List<TrackMacroSectionDefinition>();
                new CorkscrewPattern().TryPlan(cfg, "B", ref rngB, defsB);
                FeaturePlanning.StampRange(defsB, 0, SemanticElementId.InlineCorkscrew);
                FeaturePlanning.ComputePlanResult(defsB, 0, defsB.Count,
                    TrackConnectionFrame.Origin(defsB[0].Width), FrameBuildContext.From(cfg),
                    SemanticElementId.InlineCorkscrew, "B");
                Assert.AreEqual(afterPlain, rngB.state,
                    "Stage A measurement consumed rng draws — determinism contract violated.");
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
