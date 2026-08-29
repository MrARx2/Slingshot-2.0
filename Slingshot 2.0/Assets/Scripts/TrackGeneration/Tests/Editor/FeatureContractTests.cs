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
    /// Stage B acceptance: every declared <see cref="FeatureCapability"/> is honest —
    /// the builders produce sane, orthonormal, finite geometry at the edges of the
    /// declared entry windows. A capability window may only be widened together with
    /// a passing case here.
    /// </summary>
    public class FeatureContractTests
    {
        private static ResolvedTrackGenerationConfig Cfg(out TrackConfig config)
        {
            config = TrackGenerationTestUtil.CreateConfig();
            return ResolvedTrackGenerationConfig.Resolve(config,
                TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
        }

        /// <summary>Origin entry perturbed by bank/pitch and boundary rates — the contract-edge probe.</summary>
        private static TrackConnectionFrame PerturbedEntry(float width, float bankDeg, float pitchDeg,
            float kH, float rollRate)
        {
            var f = TrackConnectionFrame.Origin(width);
            Quaternion rot = Quaternion.AngleAxis(-pitchDeg, f.Right) * Quaternion.AngleAxis(bankDeg, f.Forward);
            f.Forward = (rot * f.Forward).normalized;
            f.Right = (rot * f.Right).normalized;
            f.Up = (rot * f.Up).normalized;
            f.BankAngle = bankDeg;
            f.PitchAngle = pitchDeg;
            f.HorizontalCurvature = kH;
            f.RoadRollRate = rollRate;
            return f;
        }

        private static void AssertSaneExit(FeaturePlanResult result, string label)
        {
            Assert.IsFalse(result.Failed, $"{label}: {result.FailureReason}");
            Assert.Greater(result.ArcLength, 0f, $"{label}: arc");

            var x = result.ExitState;
            Assert.LessOrEqual(Mathf.Abs(x.Forward.magnitude - 1f), 1e-3f, $"{label}: |Forward|");
            Assert.LessOrEqual(Mathf.Abs(x.Right.magnitude - 1f), 1e-3f, $"{label}: |Right|");
            Assert.LessOrEqual(Mathf.Abs(x.Up.magnitude - 1f), 1e-3f, $"{label}: |Up|");
            Assert.LessOrEqual(Mathf.Abs(Vector3.Dot(x.Forward, x.Up)), 2e-3f, $"{label}: F·U");
            Assert.LessOrEqual(Mathf.Abs(Vector3.Dot(x.Forward, x.Right)), 2e-3f, $"{label}: F·R");
            Assert.Greater(Vector3.Dot(Vector3.Cross(x.Up, x.Forward), x.Right), 0.99f,
                $"{label}: handedness");

            Assert.IsFalse(float.IsNaN(result.RelativeExitPosition.x) ||
                           float.IsNaN(result.RelativeExitPosition.y) ||
                           float.IsNaN(result.RelativeExitPosition.z), $"{label}: NaN exit");
            Assert.IsFalse(float.IsNaN(result.SweptFootprint.size.x), $"{label}: NaN bounds");
        }

        private static FeaturePlanResult Measure(ITrackFeaturePattern pattern,
            ResolvedTrackGenerationConfig cfg, uint seed, in TrackConnectionFrame entry)
        {
            var defs = new List<TrackMacroSectionDefinition>();
            var rng = new Unity.Mathematics.Random(seed);
            Assert.IsTrue(pattern.TryPlan(cfg, $"Fuzz_{pattern.PatternType}_{seed}", ref rng, defs));
            var element = FeaturePlanning.ElementOf(pattern.PatternType);
            FeaturePlanning.StampRange(defs, 0, element);
            return FeaturePlanning.ComputePlanResult(defs, 0, defs.Count, entry,
                FrameBuildContext.From(cfg), element, defs[0].PatternId);
        }

        [Test]
        public void EveryStampedElement_HasACapability()
        {
            foreach (TrackPatternType p in System.Enum.GetValues(typeof(TrackPatternType)))
            {
                var element = FeaturePlanning.ElementOf(p);
                if (element == SemanticElementId.None) continue;
                Assert.IsTrue(FeatureCapabilities.TryGet(element, out _),
                    $"Pattern {p} stamps {element} but no FeatureCapability is declared for it.");
            }
            // Corner defaults and structural identities.
            Assert.IsTrue(FeatureCapabilities.TryGet(SemanticElementId.OrdinaryCurve, out _));
            Assert.IsTrue(FeatureCapabilities.TryGet(SemanticElementId.RecoverySection, out _));
        }

        [Test]
        public void ContractEdges_ProduceSaneGeometry()
        {
            var cfg = Cfg(out var config);
            try
            {
                var patterns = new ITrackFeaturePattern[]
                {
                    new CorkscrewPattern(), new FullLoopPattern(),
                    new HalfLoopRolloutPattern(), new SCurvePattern()
                };

                foreach (var pattern in patterns)
                {
                    var element = FeaturePlanning.ElementOf(pattern.PatternType);
                    var cap = FeatureCapabilities.Get(element);
                    Assert.IsNotNull(cap, $"{element} missing capability");

                    // Probe the declared window: corners of (bank, pitch), plus the
                    // curvature/roll-rate edges. Zero entry is the control.
                    var probes = new (float bank, float pitch, float kH, float roll, string tag)[]
                    {
                        (0f, 0f, 0f, 0f, "control"),
                        (cap.MaxEntryBankDeg, 0f, 0f, 0f, "bank-edge"),
                        (-cap.MaxEntryBankDeg, 0f, 0f, 0f, "bank-edge-neg"),
                        (0f, cap.MaxEntryPitchDeg, 0f, 0f, "pitch-up-edge"),
                        (0f, -cap.MaxEntryPitchDeg, 0f, 0f, "pitch-down-edge"),
                        (0f, 0f, cap.MaxEntryHorizontalCurvature, 0f, "curved-edge"),
                        (0f, 0f, 0f, cap.MaxEntryRollRateDegPerM, "rollrate-edge"),
                        (cap.MaxEntryBankDeg * 0.7f, cap.MaxEntryPitchDeg * 0.7f,
                         cap.MaxEntryHorizontalCurvature * 0.7f, 0f, "combined-70pct"),
                    };

                    foreach (var p in probes)
                    {
                        if (!cap.AcceptsCurvedEntry && p.kH != 0f) continue;

                        for (uint seed = 1; seed <= 4; seed++)
                        {
                            var entry = PerturbedEntry(cfg.RoadWidth, p.bank, p.pitch, p.kH, p.roll);
                            var result = Measure(pattern, cfg, seed, entry);
                            AssertSaneExit(result, $"{element} {p.tag} seed {seed}");
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void InlineCorkscrew_RollStaysWholeRevolution_UnderPerturbedEntry()
        {
            var cfg = Cfg(out var config);
            try
            {
                var cap = FeatureCapabilities.Get(SemanticElementId.InlineCorkscrew);
                for (uint seed = 1; seed <= 8; seed++)
                {
                    var entry = PerturbedEntry(cfg.RoadWidth,
                        cap.MaxEntryBankDeg * 0.5f, cap.MaxEntryPitchDeg * 0.5f,
                        cap.MaxEntryHorizontalCurvature * 0.5f, 0f);
                    var result = Measure(new CorkscrewPattern(), cfg, seed, entry);
                    AssertSaneExit(result, $"cork perturbed seed {seed}");

                    float rem = Mathf.Abs(Mathf.DeltaAngle(result.AccumulatedRollDeg, 0f));
                    Assert.LessOrEqual(rem, 2f,
                        $"Seed {seed}: perturbed entry broke whole-revolution roll ({result.AccumulatedRollDeg:F1}°).");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void AuthoredRoadSections_RespectOneKilometreCaps()
        {
            TrackMacroSectionDefinition straight = SectionDefs.Straight(
                TrackMacroSectionType.Straight, 8200f, 40f, "LongStraight");
            Assert.AreEqual(SectionFrameBuilders.MaxStraightSectionLength,
                straight.Length, 0.01f);

            float ordinaryRadius = SectionFrameBuilders.ClampOrdinaryCurveRadius(90f, 5000f);
            float ordinaryLength = SectionFrameBuilders.EasedArcLength(90f, ordinaryRadius);
            Assert.LessOrEqual(ordinaryLength,
                SectionFrameBuilders.MaxOrdinaryCurveLength + 0.01f);

            foreach (float angle in new[] { 35f, 47.5f, 60f })
            {
                float radius = SectionFrameBuilders.ClampDesignerSCurveRadius(angle, 5000f);
                float length = 2f * SectionFrameBuilders.EasedArcLength(angle, radius);
                Assert.LessOrEqual(length,
                    SectionFrameBuilders.MaxDesignerSCurveLength + 0.01f,
                    $"{angle:F1} degree S-curve exceeded its complete-length ceiling.");
            }

            ResolvedTrackGenerationConfig cfg = Cfg(out TrackConfig config);
            try
            {
                for (uint seed = 1; seed <= 32; seed++)
                {
                    var definitions = new List<TrackMacroSectionDefinition>();
                    var rng = new Unity.Mathematics.Random(seed);
                    Assert.IsTrue(new SCurvePattern().TryPlan(cfg, $"SCurve_{seed}",
                        ref rng, definitions));
                    Assert.AreEqual(1, definitions.Count);
                    Assert.LessOrEqual(definitions[0].Length,
                        SectionFrameBuilders.MaxDesignerSCurveLength + 0.01f,
                        $"Seed {seed} produced an oversized S-curve.");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
