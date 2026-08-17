using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage 2 guards: intelligent connectors (no bank/wall reset between related
    /// turns, smooth reversals) and quantized subdivision (approved ladder, upward
    /// rounding, canonical shared boundaries).
    /// </summary>
    public class ConnectorTests
    {
        private static TrackGenerationResult GenerateFirstValid(string preset, int seed, int scan = 10)
        {
            TrackGenerationResult result = null;
            for (int s = seed; s < seed + scan && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(preset), s);
            Assert.IsTrue(result is { Success: true }, $"No valid {preset} track generated near seed {seed}.");
            return result;
        }

        [Test]
        public void SameDirectionBridgesNeverDipWallSupport()
        {
            // Technical layouts produce many close same-direction corner pairs.
            var result = GenerateFirstValid(TrackStylePresetLibrary.Technical, 7100);

            int bridges = 0, strictChecks = 0;
            var sections = result.Layout.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                var sec = sections[i];
                if (sec.Definition.ConnectorBehavior != ConnectorBehavior.SameDirectionTurnBridge) continue;
                bridges++;

                // Neighbours in the built list (bridge sits between two corners).
                var prev = sections[(i - 1 + sections.Count) % sections.Count];
                var next = sections[(i + 1) % sections.Count];

                // Strict no-dip contract applies to plain-corner complexes (an S-curve
                // neighbour legitimately crosses zero inside itself).
                bool PlainCorner(GeneratedTrackSection s) =>
                    s.Definition.SectionType == TrackMacroSectionType.BankedCurve ||
                    s.Definition.SectionType == TrackMacroSectionType.BankedHairpin;
                if (!PlainCorner(prev) || !PlainCorner(next)) continue;

                float PeakBoost(GeneratedTrackSection s)
                {
                    float best = 0f;
                    foreach (var f in s.SubdivisionFrames)
                        best = Mathf.Max(best, Mathf.Max(f.LeftWallMultiplier, f.RightWallMultiplier) - 1f);
                    return best;
                }
                float PeakBank(GeneratedTrackSection s)
                {
                    float best = 0f;
                    foreach (var f in s.SubdivisionFrames)
                        best = Mathf.Max(best, Mathf.Abs(f.BankAngle));
                    return best;
                }
                float PeakSideHeight(GeneratedTrackSection s)
                {
                    float best = 0f;
                    foreach (var f in s.SubdivisionFrames)
                        best = Mathf.Max(best, f.SideHeight);
                    return best;
                }

                float carryBoost = Mathf.Min(PeakBoost(prev), PeakBoost(next));
                float carryBank = Mathf.Min(PeakBank(prev), PeakBank(next));
                float carryDepth = Mathf.Min(PeakSideHeight(prev), PeakSideHeight(next));
                if (carryBoost < 0.15f) continue; // neighbours barely banked — nothing to preserve
                // The strict no-dip contract applies to FULL-carry bridges; links
                // longer than the bridge window relax support proportionally BY DESIGN
                // (graded BridgeCarry — the cliff itself used to read as a wall wave).
                if (sec.Definition.BridgeCarry < 0.99f) continue;
                strictChecks++;

                // The WHOLE complex must hold: no reset to a plain symmetric half-pipe,
                // no bank collapse, no wall-depth wave anywhere across the bridge.
                float worstBoost = float.MaxValue, worstBank = float.MaxValue, worstDepth = float.MaxValue;
                foreach (var f in sec.SubdivisionFrames)
                {
                    worstBoost = Mathf.Min(worstBoost, Mathf.Max(f.LeftWallMultiplier, f.RightWallMultiplier) - 1f);
                    worstBank = Mathf.Min(worstBank, Mathf.Abs(f.BankAngle));
                    worstDepth = Mathf.Min(worstDepth, f.SideHeight);
                }

                Assert.GreaterOrEqual(worstBoost, carryBoost * 0.75f,
                    $"Bridge '{sec.Definition.DebugName}' drops its outside wall boost to {worstBoost:F2} " +
                    $"between corners holding {carryBoost:F2} — the turn complex must carry support through.");
                if (carryBank > 10f)
                    Assert.GreaterOrEqual(worstBank, carryBank * 0.6f,
                        $"Bridge '{sec.Definition.DebugName}' lets the bank collapse to {worstBank:F1}° " +
                        $"(neighbour apexes hold {carryBank:F1}°).");
                Assert.GreaterOrEqual(worstDepth, carryDepth - 0.05f,
                    $"Bridge '{sec.Definition.DebugName}' dips the half-pipe depth to {worstDepth:F2}m " +
                    $"between turns holding {carryDepth:F2}m — wavy wall.");
            }

            // Statistical guard: technical tracks should produce at least one bridge
            // across the scanned seeds (if none exist, the classifier is dead).
            Assert.GreaterOrEqual(bridges + CountRecorded(result, ConnectorBehavior.SameDirectionTurnBridge), 1,
                "No same-direction turn bridges were classified on a Technical layout.");
        }

        private static int CountRecorded(TrackGenerationResult result, ConnectorBehavior behavior)
        {
            int n = 0;
            foreach (var d in result.Report.ConnectorDecisions)
                if (d.Behavior == behavior) n++;
            return n;
        }

        [Test]
        public void OppositeTransfersKeepSupportElevatedAndReverseSmoothly()
        {
            var result = GenerateFirstValid(TrackStylePresetLibrary.Switchback, 7200);

            var sections = result.Layout.Sections;
            foreach (var sec in sections)
            {
                if (sec.Definition.ConnectorBehavior != ConnectorBehavior.OppositeDirectionTransfer) continue;
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;

                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);
                    float bankRate = Mathf.Abs(Mathf.DeltaAngle(frames[i - 1].BankAngle, frames[i].BankAngle)) / ds;
                    // 361 m/s × 0.35 °/m ≈ 126 °/s — already aggressive; anything above is a flick.
                    Assert.LessOrEqual(bankRate, 0.35f,
                        $"Transfer '{sec.Definition.DebugName}' reverses bank at {bankRate:F2}°/m — too fast at speed.");
                }

                // Total support (both walls) should never collapse to a plain profile
                // mid-transfer when both neighbouring corners hold strong support.
                float worstTotal = float.MaxValue;
                foreach (var f in frames)
                    worstTotal = Mathf.Min(worstTotal, (f.LeftWallMultiplier - 1f) + (f.RightWallMultiplier - 1f));
                Assert.GreaterOrEqual(worstTotal, -0.4f,
                    $"Transfer '{sec.Definition.DebugName}' trims both walls below base mid-reversal.");
            }
        }

        [Test]
        public void ConnectorDecisionsAreReported()
        {
            var result = GenerateFirstValid(TrackStylePresetLibrary.Technical, 7300);
            // Every non-trivial connector produces a record with resolved lengths.
            foreach (var d in result.Report.ConnectorDecisions)
            {
                Assert.IsFalse(string.IsNullOrEmpty(d.Name));
                Assert.Greater(d.ResolvedLength, 0f);
                Assert.GreaterOrEqual(d.ResolvedLength, Mathf.Min(d.OriginalLength, d.ResolvedLength));
                if (d.Absorbed) Assert.IsFalse(string.IsNullOrEmpty(d.AbsorbedInto));
            }
        }

        [Test]
        public void FittedRoadTurnLinksReceiveConnectorCarry()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                var chain = new List<TrackMacroSectionDefinition>
                {
                    Turn(1),
                    Straight(40f),
                    Turn(1),
                    Straight(40f),
                    Turn(-1)
                };

                ConnectorAnalyzer.AnalyzeFittedChain(chain, cfg);

                Assert.AreEqual(ConnectorBehavior.SameDirectionTurnBridge, chain[1].ConnectorBehavior);
                Assert.AreEqual(1f, chain[1].BridgeCarry, 0.001f);
                Assert.AreEqual(ConnectorBehavior.OppositeDirectionTransfer, chain[3].ConnectorBehavior);
                Assert.AreEqual(1f, chain[3].BridgeCarry, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void FittedRoadElevationSkipsSolverMinimumStubs()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                var chain = new List<TrackMacroSectionDefinition>
                {
                    Straight(40f),
                    Straight(80f),
                    Straight(300f)
                };

                bool ok = QuarterRoadFitter.TryDistributeElevation(cfg, chain, 60f, out string failure);

                Assert.IsTrue(ok, failure);
                Assert.AreEqual(0f, chain[0].ElevationChange, 0.001f,
                    "A solver-minimum connector must remain level.");
                Assert.AreEqual(0f, chain[1].ElevationChange, 0.001f,
                    "Sub-100 m fitted stubs must not become pitch humps.");
                Assert.AreEqual(60f, chain[2].ElevationChange, 0.001f,
                    "The net gate elevation still has to be preserved.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DirectSamePatternTurnsHoldOutsideWallThroughTheirJoin()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                var ctx = FrameBuildContext.From(cfg);
                var entry = TrackConnectionFrame.Origin(cfg.RoadWidth);

                GeneratedTrackSection BuildTurn(TrackConnectionFrame start, float angle, float radius)
                {
                    var def = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.BankedCurve,
                        Direction = SectionTurnDirection.Right,
                        TurnAngle = angle,
                        Radius = radius,
                        Width = cfg.RoadWidth,
                        PatternId = "TighteningCorner_Test",
                        Contract = SectionConnectionContract.Level(angle)
                    };
                    var frames = SectionFrameBuilders.BuildArc(start, angle, radius, 60f, ctx);
                    return new GeneratedTrackSection
                    {
                        Definition = def,
                        PatternId = def.PatternId,
                        RoadId = 1,
                        StartFrame = frames[0],
                        EndFrame = frames[frames.Length - 1],
                        SubdivisionFrames = frames
                    };
                }

                var first = BuildTurn(entry, 65f, Mathf.Max(cfg.MinCurveRadius, 700f));
                var second = BuildTurn(first.EndFrame, 80f, cfg.MinCurveRadius);
                var layout = new GeneratedTrackLayout();
                layout.Sections.Add(first);
                layout.Sections.Add(second);

                MethodInfo apply = typeof(TrackCandidateBuilder).GetMethod("ApplyGlobalBankingField",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(apply);
                apply.Invoke(null, new object[] { layout, cfg });

                float Peak(GeneratedTrackSection s)
                {
                    float peak = 0f;
                    foreach (var f in s.SubdivisionFrames) peak = Mathf.Max(peak, Mathf.Abs(f.BankAngle));
                    return peak;
                }

                float held = Mathf.Min(Peak(first), Peak(second));
                float join = Mathf.Min(Mathf.Abs(first.EndFrame.BankAngle), Mathf.Abs(second.StartFrame.BankAngle));
                Assert.Greater(held, 10f, "Synthetic turns never developed meaningful wall support.");
                Assert.GreaterOrEqual(join, held * 0.7f,
                    $"Same-pattern right turns dip to {join:F1} degrees at their direct join while apexes hold {held:F1} degrees.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static TrackMacroSectionDefinition Straight(float length) =>
            new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                Length = length,
                Contract = SectionConnectionContract.Level()
            };

        private static TrackMacroSectionDefinition Turn(int sign) =>
            new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.BankedCurve,
                Direction = sign > 0 ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                TurnAngle = 60f,
                Radius = 700f,
                BankingAngle = 60f,
                Contract = SectionConnectionContract.Level(60f * sign)
            };
    }

    public class QuantizationTests
    {
        [Test]
        public void EasedSpiralLayerClearanceRoundsUpSafely()
        {
            const float required = 85f;
            const int revolutions = 3;
            float nominalStep = SectionFrameBuilders.RequiredSpiralClimbPerRevolution(required, revolutions);
            float quantizedStep = SectionFrameBuilders.QuantizeElevationUp(nominalStep, 5f);
            float minimumBuiltSeparation = quantizedStep * revolutions *
                                           SectionFrameBuilders.Smooth01(1f / revolutions);

            Assert.Greater(nominalStep, required,
                "Whole-feature elevation easing was not included in the spiral clearance requirement.");
            Assert.AreEqual(0f, quantizedStep % 5f, 0.001f,
                "Spiral elevation steps must land on the common 5m vertical grid.");
            Assert.GreaterOrEqual(minimumBuiltSeparation, required,
                "Upward quantization still left the first/last spiral layers below clearance.");
        }

        [Test]
        public void CorkscrewShouldersAreStraightAndKeepClassicRollRateLimit()
        {
            const float entryShoulder = 0.07f;
            const float exitShoulder = 0.07f;
            float previous = -1f;
            float peakDerivative = 0f;

            for (int i = 0; i <= 1000; i++)
            {
                float u = i / 1000f;
                float progress = SectionFrameBuilders.CorkscrewRollProgress(
                    u, entryShoulder, exitShoulder, out float derivative);
                Assert.GreaterOrEqual(progress + 0.00001f, previous, "Corkscrew roll profile moved backward.");
                previous = progress;
                peakDerivative = Mathf.Max(peakDerivative, derivative);

                if (u <= entryShoulder)
                {
                    Assert.AreEqual(0f, progress, 0.00001f);
                    Assert.AreEqual(0f, derivative, 0.00001f);
                }
                if (u >= 1f - exitShoulder)
                {
                    Assert.AreEqual(1f, progress, 0.00001f);
                    Assert.AreEqual(0f, derivative, 0.00001f);
                }
            }

            Assert.LessOrEqual(peakDerivative, 1.5f,
                "Optional straight shoulders exceeded the classic corkscrew's peak roll rate.");
        }

        [Test]
        public void JumpLaunchPitchNeverFallsBeforeTheLip()
        {
            var keys = SectionFrameBuilders.LaunchRampKeys(8f, 1.2f);
            float previous = -1f;
            for (int i = 0; i <= 200; i++)
            {
                float pitch = SectionFrameBuilders.KeyframedPitchAt(i / 200f, keys);
                Assert.GreaterOrEqual(pitch + 0.00001f, previous,
                    "Jump ramp pitched back down before the open lip.");
                previous = pitch;
            }
            Assert.AreEqual(1.2f, previous, 0.0001f, "Ramp did not preserve the ballistic launch pitch at the lip.");
        }

        [Test]
        public void JumpLaunchUsesLateRiseSilhouetteAndStableLipTangent()
        {
            var keys = SectionFrameBuilders.LaunchRampKeys(3.5f, 10f);

            Assert.AreEqual(0f, SectionFrameBuilders.KeyframedPitchAt(0.3f, keys), 0.0001f,
                "The air-gap approach should remain level through roughly its first third.");
            Assert.Less(SectionFrameBuilders.KeyframedPitchAt(0.5f, keys), 2.5f,
                "The launch started loading too early and regressed toward a long shallow hill.");
            Assert.Greater(SectionFrameBuilders.KeyframedPitchAt(0.8f, keys), 5f,
                "The final third must contain the pronounced upward launch curvature.");
            Assert.AreEqual(10f, SectionFrameBuilders.KeyframedPitchAt(0.95f, keys), 0.0001f,
                "The terminal lip must hold a stable ballistic tangent before the open edge.");
        }

        [Test]
        public void MonotonicJumpSolverStillProducesReachableDescendingLandings()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                bool solved = false;

                for (uint seed = 1; seed <= 16 && !solved; seed++)
                {
                    var rng = new Unity.Mathematics.Random(seed);
                    if (!JumpBallistics.TrySolve(cfg, ref rng, out var solution)) continue;
                    solved = true;
                    Assert.Greater(solution.LandingHeight, 0f);
                    Assert.Greater(solution.GapRise, 0f,
                        "Monotonic launch no longer reserves enough elevation for its landing transition.");
                    Assert.Less(solution.ArrivalPitchDeg, 0f, "Craft did not reach the landing while descending.");
                    Assert.LessOrEqual(solution.ClimbPitchDeg, solution.LaunchPitchDeg,
                        "Solved launch ramp still contains a pitch-down before the lip.");
                }

                Assert.IsTrue(solved, "Monotonic launch shaping made all sampled ballistic jumps infeasible.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void MonotonicJumpSolverIsReliableAcrossTheRulebookSpeedRange()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                foreach (float speedKph in new[] { 400f, 1300f, 2000f })
                {
                    var settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
                    settings.Scale.DesignSpeedKph = speedKph;
                    var cfg = ResolvedTrackGenerationConfig.Resolve(config, settings);
                    int solved = 0;

                    for (uint seed = 1; seed <= 32; seed++)
                    {
                        var rng = new Unity.Mathematics.Random(seed);
                        if (JumpBallistics.TrySolve(cfg, ref rng, out var solution))
                        {
                            solved++;
                            Assert.Less(solution.ArrivalPitchDeg, 0f);
                            Assert.Greater(solution.GapRise, 0f);
                        }
                    }

                    Assert.GreaterOrEqual(solved, 30,
                        $"Only {solved}/32 jumps solved at {speedKph:F0} km/h; required patterns would remain lottery failures.");
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void SpiralAbsorbsSameDirectionRecoveryAsRevolutionsAndForwardDrift()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                var spiral = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Spiral,
                    Length = 2f * Mathf.PI * 120f,
                    Width = cfg.RoadWidth,
                    Radius = 120f,
                    Direction = SectionTurnDirection.Left,
                    TurnAngle = 360f,
                    ElevationChange = 100f,
                    PatternId = "Spiral_Test",
                    RequiresRecoveryAfter = true,
                    Contract = SectionConnectionContract.Level()
                };
                spiral.Contract.ElevationDelta = spiral.ElevationChange;
                var recovery = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 500f,
                    Width = cfg.RoadWidth,
                    ElevationChange = 200f,
                    PatternId = spiral.PatternId,
                    DebugName = "RecoveryStraight_Climb200m",
                    Contract = SectionConnectionContract.Level()
                };
                var plan = new TopologyPlan();
                plan.Defs.Add(spiral);
                plan.Defs.Add(recovery);

                Assert.AreEqual(1, InvokeFeatureIntegration(cfg, plan));
                Assert.AreEqual(1, plan.Defs.Count, "Integrated recovery remained as a separate section.");
                Assert.AreEqual(300f, spiral.ElevationChange, 0.001f);
                Assert.GreaterOrEqual(spiral.TurnAngle, 1080f, "Spiral did not grow enough revolutions for the climb.");
                Assert.AreEqual(500f, spiral.PlanHorizontalLength, 0.001f,
                    "Spiral did not preserve the removed recovery's forward run.");

                var frames = SectionFrameBuilders.BuildSpiral(TrackConnectionFrame.Origin(cfg.RoadWidth), spiral,
                    FrameBuildContext.From(cfg));
                Assert.AreEqual(500f, frames[frames.Length - 1].Position.z, 0.05f);
                Assert.AreEqual(300f, frames[frames.Length - 1].Position.y, 0.05f);
                Assert.Less(Vector3.Angle(frames[frames.Length - 1].Forward, Vector3.forward), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void CorkscrewAbsorbsRecoveryWithDifferentHalfRadii()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                var cork = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.Corkscrew,
                    Length = 1200f,
                    Width = cfg.RoadWidth,
                    Radius = (cfg.MinCorkscrewRadius + cfg.MaxCorkscrewRadius) * 0.5f,
                    RollChange = 360f,
                    PatternId = "Corkscrew_Test",
                    RequiresRecoveryAfter = true,
                    Contract = SectionConnectionContract.Level()
                };
                var recovery = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.RecoveryStraight,
                    Length = 700f,
                    Width = cfg.RoadWidth,
                    ElevationChange = -300f,
                    PatternId = cork.PatternId,
                    DebugName = "RecoveryStraight_Drop300m",
                    Contract = SectionConnectionContract.Level()
                };
                var plan = new TopologyPlan();
                plan.Defs.Add(cork);
                plan.Defs.Add(recovery);

                Assert.AreEqual(1, InvokeFeatureIntegration(cfg, plan));
                Assert.AreEqual(1, plan.Defs.Count, "Integrated recovery remained as a separate section.");
                Assert.Greater(cork.SecondaryRadius, cork.Radius,
                    "A descending corkscrew should make its second half larger than its first.");
                Assert.AreEqual(-300f, cork.ElevationChange, 0.001f);
                Assert.AreEqual(1900f, cork.Length, 0.001f,
                    "Corkscrew did not preserve the removed recovery's forward run.");

                var frames = SectionFrameBuilders.BuildCorkscrew(TrackConnectionFrame.Origin(cfg.RoadWidth), cork,
                    FrameBuildContext.From(cfg));
                Assert.AreEqual(1900f, frames[frames.Length - 1].Position.z, 0.05f);
                Assert.AreEqual(-300f, frames[frames.Length - 1].Position.y, 0.05f);
                Assert.Less(Vector3.Angle(frames[frames.Length - 1].Forward, Vector3.forward), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static int InvokeFeatureIntegration(ResolvedTrackGenerationConfig cfg, TopologyPlan plan)
        {
            MethodInfo method = typeof(TrackTopologyPlanner).GetMethod("IntegrateFeatureRecoveries",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "Feature-recovery integration hook is missing.");
            return (int)method.Invoke(null, new object[] { cfg, plan });
        }

        [Test]
        public void TierSelectionRoundsUpwardOnly()
        {
            var ladder = new List<int> { 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256 };
            Assert.AreEqual(8, TrackRetopology.SelectSubdivisionTier(1, ladder));
            Assert.AreEqual(8, TrackRetopology.SelectSubdivisionTier(8, ladder));
            Assert.AreEqual(12, TrackRetopology.SelectSubdivisionTier(9, ladder));
            Assert.AreEqual(48, TrackRetopology.SelectSubdivisionTier(37, ladder));
            Assert.AreEqual(256, TrackRetopology.SelectSubdivisionTier(193, ladder));
            Assert.AreEqual(256, TrackRetopology.SelectSubdivisionTier(9999, ladder)); // saturates at the top
        }

        [TestCase(TrackStylePresetLibrary.Balanced, 7400)]
        [TestCase(TrackStylePresetLibrary.Rollercoaster, 7401)]
        public void EveryRegionUsesAnApprovedTier(string preset, int seed)
        {
            TrackGenerationResult result = null;
            for (int s = seed; s < seed + 10 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(preset), s);
            Assert.IsTrue(result is { Success: true }, "No valid track generated.");

            var config = TrackGenerationTestUtil.CreateConfig();
            var ladder = new HashSet<int>(config.SubdivisionLadder);
            Object.DestroyImmediate(config);

            Assert.Greater(result.Report.SubdivisionRegions.Count, 0, "No subdivision regions were reported.");

            foreach (var region in result.Report.SubdivisionRegions)
            {
                Assert.GreaterOrEqual(region.SelectedTier, 8,
                    $"{region.RegionName} uses fewer than 8 intervals.");
                Assert.GreaterOrEqual(region.SelectedTier, region.RawRequirement,
                    $"{region.RegionName} rounded DOWN below its raw requirement.");
                // Structural floors (one interval per section) may exceed the top tier;
                // otherwise the count must be a ladder entry or a structural/continuity bump.
                bool onLadder = ladder.Contains(region.SelectedTier);
                bool structural = region.LimitingFactor == "section count";
                Assert.IsTrue(onLadder || structural,
                    $"{region.RegionName} tier {region.SelectedTier} is not on the approved ladder " +
                    $"(limiting factor: {region.LimitingFactor}).");
            }
        }

        [TestCase(TrackStylePresetLibrary.Balanced, 7500)]
        public void RegionRingCountsMatchReportedTiers(string preset, int seed)
        {
            TrackGenerationResult result = null;
            for (int s = seed; s < seed + 10 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(TrackGenerationTestUtil.FastSettings(preset), s);
            Assert.IsTrue(result is { Success: true }, "No valid track generated.");

            // Rebuild region interval counts from the actual frames: contiguous meshed
            // runs (breaking at gaps/gates) must carry SelectedTier intervals.
            var actual = new List<int>();
            int run = 0;
            foreach (var sec in result.Layout.Sections)
            {
                if (sec.RoadId == 1) continue;
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2)
                {
                    if (run > 0) actual.Add(run);
                    run = 0;
                    continue;
                }
                run += sec.SubdivisionFrames.Length - 1; // intervals; boundary ring shared
            }
            if (run > 0) actual.Add(run);

            // Each alternate road (a contiguous RoadId==1 run) is one open segment.
            int altRun = 0;
            foreach (var sec in result.Layout.Sections)
            {
                if (sec.RoadId == 1 && sec.SubdivisionFrames != null && sec.SubdivisionFrames.Length >= 2)
                {
                    altRun += sec.SubdivisionFrames.Length - 1;
                    continue;
                }
                if (altRun > 0) actual.Add(altRun);
                altRun = 0;
            }
            if (altRun > 0) actual.Add(altRun);

            Assert.AreEqual(result.Report.SubdivisionRegions.Count, actual.Count,
                "Reported region count differs from the layout's anchored segments.");
            for (int i = 0; i < actual.Count; i++)
            {
                Assert.AreEqual(result.Report.SubdivisionRegions[i].SelectedTier, actual[i],
                    $"{result.Report.SubdivisionRegions[i].RegionName} reports tier " +
                    $"{result.Report.SubdivisionRegions[i].SelectedTier} but carries {actual[i]} intervals.");
            }
        }

        private static RotationalPhaseDefinition RotationPhase(RotationalPhaseAxis axis, int units,
            float firstLength = 240f, float secondLength = 320f, float firstRadius = 180f,
            float secondRadius = 300f, float heading = 0f, float pitchDrift = 0f,
            RotationalBlendPreset blend = RotationalBlendPreset.Medium)
        {
            return new RotationalPhaseDefinition
            {
                Axis = axis,
                Direction = RotationalPhaseDirection.Positive,
                RotationUnits = units,
                FirstHalfLength = firstLength,
                SecondHalfLength = secondLength,
                FirstHalfRadius = firstRadius,
                SecondHalfRadius = secondRadius,
                HorizontalTurnDegrees = heading,
                VerticalDriftDegrees = pitchDrift,
                ExitWidth = 40f,
                BlendToNext = blend
            };
        }

        private static TrackMacroSectionDefinition RotationEvent(params RotationalPhaseDefinition[] phases)
        {
            var def = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent,
                Width = 40f,
                DebugName = "RotationalEvent_Test",
                RotationalPhases = new List<RotationalPhaseDefinition>(phases)
            };
            def.Length = SectionFrameBuilders.RotationalEventLength(def);
            return def;
        }

        private static FrameBuildContext RotationContext()
        {
            return new FrameBuildContext
            {
                MetersPerRing = 4f,
                FeatureMetersPerRing = 2f,
                MaxFacetAngle = 1f,
                MaxRingsPerSection = 20000,
                RotationUnitDegrees = 90f,
                MaxRotationalSampleDistance = 4f,
                MaxRotationalForwardAngle = 0.75f,
                MaxRotationalRollAngle = 1f,
                MinSamplesPerRotationUnit = 16
            };
        }

        [TestCase(4, 360f)]
        [TestCase(8, 720f)]
        [TestCase(12, 1080f)]
        public void CorkscrewUnitsStayUnwrappedAndAdaptivelySampled(int units, float expectedDegrees)
        {
            var def = RotationEvent(RotationPhase(RotationalPhaseAxis.RoadRoll, units));
            var frames = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def,
                RotationContext());

            Assert.AreEqual(expectedDegrees, frames[frames.Length - 1].AccumulatedRoadRoll, 0.01f);
            Assert.GreaterOrEqual(frames.Length - 1, units * 16);
            for (int i = 1; i < frames.Length; i++)
                Assert.LessOrEqual(Mathf.Abs(frames[i].AccumulatedRoadRoll - frames[i - 1].AccumulatedRoadRoll), 1.05f);
        }

        [TestCase(4, 360f)]
        [TestCase(8, 720f)]
        [TestCase(12, 1080f)]
        public void LoopUnitsStayUnwrappedWithoutControlStationSeams(int units, float expectedDegrees)
        {
            var def = RotationEvent(RotationPhase(RotationalPhaseAxis.VerticalCenterline, units,
                firstLength: 300f * units / 4f, secondLength: 430f * units / 4f,
                firstRadius: 170f, secondRadius: 310f, heading: 15f));
            var frames = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def,
                RotationContext());

            Assert.AreEqual(expectedDegrees, frames[frames.Length - 1].AccumulatedVerticalRotation, 0.01f);
            float maxLateralExcursion = 0f;
            for (int i = 0; i < frames.Length; i++)
                maxLateralExcursion = Mathf.Max(maxLateralExcursion, Mathf.Abs(frames[i].Position.x));
            Assert.Greater(maxLateralExcursion, 1f,
                "Horizontally curving loop remained in a fixed world plane.");
            Assert.Greater(Vector3.Angle(Vector3.forward, frames[frames.Length - 1].Forward), 5f,
                "The loop discarded its requested plan-view heading change.");
            for (int i = 1; i < frames.Length; i++)
            {
                Assert.Less(Vector3.Angle(frames[i - 1].Forward, frames[i].Forward), 1.6f);
                Assert.Greater(Vector3.Dot(frames[i].Right, Vector3.Cross(frames[i].Up, frames[i].Forward)), 0.99f);
            }
        }

        [Test]
        public void PlannedFullLoopsAreInternallyClearAndReturnToTheirEntryHeading()
        {
            var limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                typeof(TrackConfig).GetField("allowedLoopRotationUnits",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(limits, new[] { 4 });
                typeof(TrackConfig).GetField("maxLoopRotationUnits",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(limits, 4);
                limits.Validate();
                var settings = TrackStylePresetLibrary.Create("Rollercoaster");
                settings.Corners.MinCurveRadius = 180f;
                settings.Corners.MaxCurveRadius = 1200f;
                var cfg = ResolvedTrackGenerationConfig.Resolve(limits, settings);

                for (uint seed = 1; seed <= 16; seed++)
                {
                    var rng = new Unity.Mathematics.Random(seed);
                    var output = new List<TrackMacroSectionDefinition>();
                    Assert.IsTrue(new FullLoopPattern().TryPlan(cfg, $"Loop_{seed}", ref rng, output),
                        $"Seed {seed} could not produce a clear loop shape.");
                    Assert.AreEqual(1, output.Count);

                    var frames = SectionFrameBuilders.BuildRotationalEvent(
                        TrackConnectionFrame.Origin(cfg.RoadWidth), output[0], FrameBuildContext.From(cfg));
                    float clearance = SectionFrameBuilders.MeasureRotationalEventClearance(frames,
                        cfg.RoadWidth, out float firstArc, out float secondArc);
                    Assert.GreaterOrEqual(clearance + 0.01f, cfg.RoadWidth * 1.05f,
                        $"Seed {seed} folds to {clearance:F1}m between arcs {firstArc:F0}/{secondArc:F0}m.");
                    Assert.Less(Vector3.Angle(frames[0].Forward, frames[frames.Length - 1].Forward), 0.1f,
                        "The internal loop offset leaked into the exit heading.");
                    Assert.Greater(Mathf.Abs(output[0].PlanLateralOffset), cfg.RoadWidth,
                        "The loop did not acquire a meaningful non-overlapping plan offset.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(limits);
            }
        }

        [Test]
        public void ClosureCurvesKeepRulebookCapacityBeyondStyledCornerMaximum()
        {
            var limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var settings = TrackStylePresetLibrary.Create("Rollercoaster");
                settings.Corners.MaxCurveRadius = 1200f;
                var cfg = ResolvedTrackGenerationConfig.Resolve(limits, settings);

                Assert.AreEqual(1200f, cfg.MaxCurveRadius, 0.01f);
                Assert.AreEqual(limits.MaxClosureCurveRadius, cfg.MaxClosureCurveRadius, 0.01f);
                Assert.Greater(cfg.MaxClosureCurveRadius, cfg.MaxCurveRadius);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(limits);
            }
        }

        [Test]
        public void EasedArcExitCarriesZeroCurvatureIntoStampedRotationalFootprint()
        {
            var arc = SectionFrameBuilders.BuildArc(TrackConnectionFrame.Origin(40f), 60f, 400f,
                0f, RotationContext());
            var entry = arc[arc.Length - 1];
            Assert.AreEqual(0f, entry.HorizontalCurvature, 0.000001f,
                "Eased arc propagated peak curvature beyond its zero-curvature exit.");

            var def = RotationEvent(RotationPhase(RotationalPhaseAxis.RoadRoll, 4,
                firstLength: 420f, secondLength: 520f, heading: 15f));
            var limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(limits,
                    TrackStylePresetLibrary.Create("Rollercoaster"));
                SectionFrameBuilders.StampRotationalEventPlan(def, cfg);
                var frames = SectionFrameBuilders.BuildRotationalEvent(entry, def, FrameBuildContext.From(cfg));
                Vector3 delta = frames[frames.Length - 1].Position - entry.Position;
                float actualForward = Vector3.Dot(delta, SectionFrameBuilders.Flatten(entry.Forward));
                float actualLateral = Vector3.Dot(delta, SectionFrameBuilders.Flatten(entry.Right));
                Assert.AreEqual(def.PlanHorizontalLength, actualForward, 0.5f);
                Assert.AreEqual(def.PlanLateralOffset, actualLateral, 0.5f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(limits);
            }
        }

        [Test]
        public void PlannedCorkscrewsOfferStraightAndGentleProfilesWithoutLeakingExitHeading()
        {
            var limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(limits,
                    TrackStylePresetLibrary.Create("Rollercoaster"));
                int straightProfiles = 0;
                int gentleProfiles = 0;
                for (uint seed = 1; seed <= 32; seed++)
                {
                    var rng = new Unity.Mathematics.Random(seed);
                    var planned = new List<TrackMacroSectionDefinition>();
                    Assert.IsTrue(new CorkscrewPattern().TryPlan(cfg, $"Cork_{seed}", ref rng, planned));
                    Assert.AreEqual(1, planned.Count);
                    var def = planned[0];
                    var phase = def.RotationalPhases[0];
                    Assert.AreEqual(0f, phase.HorizontalTurnDegrees, 0.001f);
                    Assert.GreaterOrEqual(phase.CenterlineOrbitDegrees, 4f,
                        $"Seed {seed} planned a flat barrel roll instead of a corkscrew centerline.");
                    Assert.LessOrEqual(Mathf.Abs(phase.FirstHalfYawBiasDegrees), 15f);
                    if (Mathf.Abs(phase.FirstHalfYawBiasDegrees) < 0.001f) straightProfiles++;
                    else gentleProfiles++;

                    var frames = SectionFrameBuilders.BuildRotationalEvent(
                        TrackConnectionFrame.Origin(cfg.RoadWidth), def, FrameBuildContext.From(cfg));
                    float minElevation = float.PositiveInfinity;
                    float maxElevation = float.NegativeInfinity;
                    for (int i = 0; i < frames.Length; i++)
                    {
                        minElevation = Mathf.Min(minElevation, frames[i].Position.y);
                        maxElevation = Mathf.Max(maxElevation, frames[i].Position.y);
                    }
                    Assert.Greater(maxElevation - minElevation, cfg.RoadWidth * 0.5f,
                        $"Seed {seed} corkscrew centerline has no meaningful internal elevation.");
                    float supportWeighted = 0f;
                    float curvatureWeight = 0f;
                    for (int i = 1; i < frames.Length - 1; i++)
                    {
                        float local = (float)i / (frames.Length - 1);
                        if (local < 0.2f || local > 0.8f) continue;
                        Vector3 curvature = frames[i + 1].Forward - frames[i - 1].Forward;
                        float weight = curvature.magnitude;
                        if (weight < 1e-6f) continue;
                        supportWeighted += Vector3.Dot(curvature / weight, frames[i].Up) * weight;
                        curvatureWeight += weight;
                    }
                    float supportAlignment = supportWeighted / Mathf.Max(1e-6f, curvatureWeight);
                    Assert.Greater(supportAlignment, 0.25f,
                        $"Seed {seed} centerline curvature does not point into the rolled road surface ({supportAlignment:F2}); " +
                        $"units {phase.RotationUnits}, orbit {phase.CenterlineOrbitDegrees:F0}, yaw {phase.FirstHalfYawBiasDegrees:+0;-0}.");
                    Assert.Less(Vector3.Angle(frames[0].Forward, frames[frames.Length - 1].Forward), 0.1f,
                        $"Seed {seed} leaked its temporary corkscrew yaw into the exit heading.");
                }

                Assert.Greater(straightProfiles, 0);
                Assert.Greater(gentleProfiles, 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(limits);
            }
        }

        [Test]
        public void BankedClimbingEntryIsInheritedAndNaturalExitIsNotRealigned()
        {
            var entry = TrackConnectionFrame.Origin(40f);
            entry.Forward = (Vector3.forward * Mathf.Cos(12f * Mathf.Deg2Rad) +
                             Vector3.up * Mathf.Sin(12f * Mathf.Deg2Rad)).normalized;
            Quaternion bank = Quaternion.AngleAxis(60f, entry.Forward);
            entry.Right = bank * Vector3.right;
            entry.Up = Vector3.Cross(entry.Forward, entry.Right).normalized;
            entry.AccumulatedRoadRoll = 60f;
            entry.BankAngle = 60f;

            var def = RotationEvent(RotationPhase(RotationalPhaseAxis.RoadRoll, 4,
                heading: 30f, pitchDrift: -45f));
            var frames = SectionFrameBuilders.BuildRotationalEvent(entry, def, RotationContext());

            Assert.AreEqual(entry.Position, frames[0].Position);
            Assert.AreEqual(entry.Forward, frames[0].Forward);
            Assert.AreEqual(entry.Up, frames[0].Up);
            Assert.AreEqual(420f, frames[frames.Length - 1].AccumulatedRoadRoll, 0.01f);
            Assert.Greater(Vector3.Angle(entry.Forward, frames[frames.Length - 1].Forward), 10f);
            Assert.Less(frames[frames.Length - 1].Position.y, entry.Position.y,
                "Descending corkscrew did not exit below its entry.");
        }

        [Test]
        public void RotationalEventMatchesInheritedAndRequestedEndpointRatesWithoutChangingTotals()
        {
            var entry = TrackConnectionFrame.Origin(40f);
            entry.HorizontalCurvature = 0.0008f;
            entry.VerticalCurvature = -0.0006f;
            entry.RoadRollRate = 0.08f;
            var phase = RotationPhase(RotationalPhaseAxis.RoadRoll, 4,
                firstLength: 420f, secondLength: 520f, heading: 15f, pitchDrift: -10f);
            phase.ExitHorizontalCurvature = -0.0005f;
            phase.ExitVerticalCurvature = 0.0004f;
            phase.ExitRoadRollRate = -0.05f;

            var frames = SectionFrameBuilders.BuildRotationalEvent(entry, RotationEvent(phase), RotationContext());
            var firstStep = frames[1];
            var end = frames[frames.Length - 1];

            Assert.AreEqual(entry.HorizontalCurvature, firstStep.HorizontalCurvature, 0.00008f);
            Assert.AreEqual(entry.VerticalCurvature, firstStep.VerticalCurvature, 0.00008f);
            Assert.AreEqual(entry.RoadRollRate, firstStep.RoadRollRate, 0.008f);
            Assert.AreEqual(phase.ExitHorizontalCurvature, end.HorizontalCurvature, 0.000001f);
            Assert.AreEqual(phase.ExitVerticalCurvature, end.VerticalCurvature, 0.000001f);
            Assert.AreEqual(phase.ExitRoadRollRate, end.RoadRollRate, 0.000001f);
            Assert.AreEqual(360f, end.AccumulatedRoadRoll, 0.01f,
                "Endpoint-rate matching changed the exact quantized rotation total.");
        }

        [Test]
        public void MixedPhasesOverlapButKeepExactIndependentTotals()
        {
            var vertical = RotationPhase(RotationalPhaseAxis.VerticalCenterline, 2,
                firstLength: 260f, secondLength: 340f, blend: RotationalBlendPreset.Medium);
            var roll = RotationPhase(RotationalPhaseAxis.RoadRoll, 2,
                firstLength: 280f, secondLength: 300f, heading: -15f);
            var def = RotationEvent(vertical, roll);
            Assert.Less(def.Length, vertical.Length + roll.Length, "Medium blend did not overlap phases.");

            var frames = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def,
                RotationContext());
            var end = frames[frames.Length - 1];
            Assert.AreEqual(180f, end.AccumulatedVerticalRotation, 0.01f);
            Assert.AreEqual(180f, end.AccumulatedRoadRoll, 0.01f);
            for (int i = 1; i < frames.Length; i++)
                Assert.Less(Vector3.Angle(frames[i - 1].Up, frames[i].Up), 2.1f);
        }

        [Test]
        public void CorkscrewHalfElevationBiasCanExitLowerAndLevel()
        {
            var phase = RotationPhase(RotationalPhaseAxis.RoadRoll, 4,
                firstLength: 360f, secondLength: 520f, firstRadius: 180f, secondRadius: 340f);
            phase.FirstHalfPitchBiasDegrees = -10f;
            phase.SecondHalfPitchBiasDegrees = -14f;
            var frames = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f),
                RotationEvent(phase), RotationContext());
            var end = frames[frames.Length - 1];

            Assert.Less(end.Position.y, -50f);
            Assert.Less(Vector3.Angle(Vector3.forward, end.Forward), 0.1f,
                "Temporary elevation curvature forced a non-level corkscrew exit.");
            Assert.AreEqual(360f, end.AccumulatedRoadRoll, 0.01f);
        }

        [TestCase(2, 2, true)]   // half corkscrew -> half loop
        [TestCase(4, 2, false)]  // full loop -> half corkscrew
        [TestCase(2, 4, true)]   // full corkscrew -> half loop
        [TestCase(4, 4, false)]  // full loop -> full corkscrew
        public void MixedPhaseOrdersRemainOneContinuousQuantizedEvent(int verticalUnits,
            int rollUnits, bool rollFirst)
        {
            var vertical = RotationPhase(RotationalPhaseAxis.VerticalCenterline, verticalUnits);
            var roll = RotationPhase(RotationalPhaseAxis.RoadRoll, rollUnits);
            var def = rollFirst ? RotationEvent(roll, vertical) : RotationEvent(vertical, roll);
            var frames = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def,
                RotationContext());

            Assert.AreEqual(2, def.RotationalPhases.Count);
            Assert.AreEqual(verticalUnits * 90f,
                frames[frames.Length - 1].AccumulatedVerticalRotation, 0.01f);
            Assert.AreEqual(rollUnits * 90f,
                frames[frames.Length - 1].AccumulatedRoadRoll, 0.01f);
            Assert.AreEqual(def.Length,
                frames[frames.Length - 1].ArcLength - frames[0].ArcLength, 0.01f);
        }

        [Test]
        public void NaturalExitFlowsDirectlyIntoStraightAndCurveWithoutFrameReset()
        {
            var def = RotationEvent(RotationPhase(RotationalPhaseAxis.RoadRoll, 4,
                heading: 30f, pitchDrift: 15f));
            var eventFrames = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def,
                RotationContext());
            var exit = eventFrames[eventFrames.Length - 1];
            var straightDef = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                Length = 200f,
                Width = 40f
            };
            var straight = SectionFrameBuilders.BuildStraight(exit, straightDef, RotationContext());
            Assert.AreEqual(exit.Forward, straight[0].Forward);
            Assert.AreEqual(exit.Up, straight[0].Up);
            Assert.Less(Vector3.Angle(exit.Forward, straight[straight.Length - 1].Forward), 0.01f);
            Assert.Less(Vector3.Angle(exit.Up, straight[straight.Length - 1].Up), 0.01f);

            var curve = SectionFrameBuilders.BuildArc(exit, 30f, 400f, 0f, RotationContext());
            Assert.AreEqual(exit.Forward, curve[0].Forward);
            Assert.AreEqual(exit.Up, curve[0].Up);
        }

        [Test]
        public void ProceduralRotationalPatternsEmitNoHiddenConnectorPieces()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(config,
                    TrackGenerationTestUtil.FastSettings("Balanced"));
                var patterns = new ITrackFeaturePattern[]
                {
                    new FullLoopPattern(),
                    new CorkscrewPattern(),
                    new LoopToCorkscrewPattern(),
                    new DoubleCorkscrewPattern()
                };

                for (int p = 0; p < patterns.Length; p++)
                {
                    var rng = new Unity.Mathematics.Random((uint)(9001 + p));
                    var output = new List<TrackMacroSectionDefinition>();
                    Assert.IsTrue(patterns[p].TryPlan(cfg, $"Pattern_{p}", ref rng, output));
                    Assert.AreEqual(1, output.Count,
                        $"{patterns[p].PatternType} inserted a connector or recovery piece.");
                    Assert.AreEqual(TrackMacroSectionType.RotationalEvent, output[0].SectionType);
                    Assert.IsFalse(output[0].RequiresRecoveryAfter);
                    Assert.IsNotNull(output[0].RotationalPhases);
                    foreach (var phase in output[0].RotationalPhases)
                    {
                        Assert.Greater(phase.RotationUnits, 0);
                        Assert.AreEqual(0f,
                            Mathf.Repeat(phase.RotationUnits * cfg.RotationUnitDegrees, 90f), 0.001f);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RotationalBuilderIsBitDeterministicForTheSameDefinition()
        {
            var def = RotationEvent(
                RotationPhase(RotationalPhaseAxis.VerticalCenterline, 4, heading: 15f),
                RotationPhase(RotationalPhaseAxis.RoadRoll, 4, firstRadius: 210f, secondRadius: 360f));
            var a = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def, RotationContext());
            var b = SectionFrameBuilders.BuildRotationalEvent(TrackConnectionFrame.Origin(40f), def, RotationContext());
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].Position, b[i].Position);
                Assert.AreEqual(a[i].Forward, b[i].Forward);
                Assert.AreEqual(a[i].Up, b[i].Up);
                Assert.AreEqual(a[i].AccumulatedRoadRoll, b[i].AccumulatedRoadRoll);
            }
        }
    }
}
