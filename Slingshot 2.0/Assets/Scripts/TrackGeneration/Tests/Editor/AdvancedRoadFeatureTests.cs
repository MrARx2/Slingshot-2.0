using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage 4 guards: full pipes close gradually and reopen, wallride turns migrate
    /// the surface channels smoothly on and off the wall, and both appear on demand.
    /// </summary>
    public class AdvancedRoadFeatureTests
    {
        private static TrackGenerationResult GenerateWith(System.Action<TrackDesignerSettings> mutate, int seed, int scan = 12)
        {
            TrackGenerationResult result = null;
            for (int s = seed; s < seed + scan && (result == null || !result.Success); s++)
            {
                var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced);
                mutate(settings);
                result = TrackGenerationTestUtil.Generate(settings, s);
            }
            return result;
        }

        [Test]
        public void RequiredFullPipeGeneratesAndClosesGradually()
        {
            var result = GenerateWith(s =>
            {
                s.Features.FullPipes = new TrackFeatureRule(true, 1, 1, 1f);
                s.Quarters.MinimumDualQuarterCount = 0;
                s.Quarters.MaximumDualQuarterCount = 0;
            }, 8100);
            Assert.IsTrue(result is { Success: true }, "No valid track with a required full pipe.");
            Assert.GreaterOrEqual(result.Layout.Metrics.FullPipeCount, 1, "Metrics missed the full pipe.");

            foreach (var sec in result.Layout.Sections)
            {
                if (sec.Definition.SectionType != TrackMacroSectionType.FullPipe) continue;
                var frames = sec.SubdivisionFrames;
                Assert.IsNotNull(frames);

                float peak = 0f;
                for (int i = 0; i < frames.Length; i++)
                {
                    peak = Mathf.Max(peak, frames[i].PipeClosure);
                    if (i > 0)
                    {
                        float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);
                        float rate = Mathf.Abs(frames[i].PipeClosure - frames[i - 1].PipeClosure) / ds;
                        // Full closure over less than ~100 m would be a wall at 1300 km/h.
                        Assert.LessOrEqual(rate, 1f / 100f,
                            $"Pipe closure steps too fast ({rate:F4}/m) at ring {i} of '{sec.Definition.DebugName}'.");
                    }
                }
                Assert.Greater(peak, 0.98f, "Full pipe never fully closed.");

                // Boundary rings must weld as OPEN profile (closure 0) at road width.
                Assert.Less(frames[0].PipeClosure, 0.01f, "Pipe entry ring is not open.");
                Assert.Less(frames[frames.Length - 1].PipeClosure, 0.01f, "Pipe exit ring is not open.");
            }
        }

        [Test]
        public void RequiredWallrideGeneratesWithSmoothChannelMigration()
        {
            var result = GenerateWith(s =>
            {
                s.Features.Wallrides = new TrackFeatureRule(true, 1, 2, 1f);
                s.Quarters.MinimumDualQuarterCount = 0;
                s.Quarters.MaximumDualQuarterCount = 0;
            }, 8200);
            Assert.IsTrue(result is { Success: true }, "No valid track with a required wallride.");
            Assert.GreaterOrEqual(result.Layout.Metrics.WallrideCount, 1, "Metrics missed the wallride.");

            foreach (var sec in result.Layout.Sections)
            {
                if (sec.Definition.SectionType != TrackMacroSectionType.WallrideTurn) continue;
                var frames = sec.SubdivisionFrames;
                Assert.IsNotNull(frames);

                bool rightTurn = sec.Definition.TurnSign >= 0;
                float peakBoost = 0f, peakRounding = 0f, peakOverhang = 0f;
                float peakMorph = 0f;
                float minimumInsideWall = float.MaxValue;
                for (int i = 0; i < frames.Length; i++)
                {
                    var f = frames[i];
                    float outsideMult = rightTurn ? f.LeftWallMultiplier : f.RightWallMultiplier;
                    float insideMult = rightTurn ? f.RightWallMultiplier : f.LeftWallMultiplier;
                    float overhang = rightTurn ? f.LeftOverhang : f.RightOverhang;
                    peakBoost = Mathf.Max(peakBoost, outsideMult);
                    peakRounding = Mathf.Max(peakRounding, f.TurnRounding);
                    peakOverhang = Mathf.Max(peakOverhang, overhang);
                    peakMorph = Mathf.Max(peakMorph, f.WallrideMorph);
                    minimumInsideWall = Mathf.Min(minimumInsideWall, insideMult);

                    if (i > 0)
                    {
                        float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);
                        float prevMult = rightTurn ? frames[i - 1].LeftWallMultiplier : frames[i - 1].RightWallMultiplier;
                        Assert.LessOrEqual(Mathf.Abs(outsideMult - prevMult) / ds, 0.05f,
                            $"Wallride wall boost steps too fast at ring {i} of '{sec.Definition.DebugName}'.");
                    }
                }

                Assert.Greater(peakBoost, 2.5f, "Wallride outside wall never reached primary-surface height.");
                Assert.Greater(peakRounding, 0.95f, "Wallride center never fully rounded.");
                Assert.Greater(peakOverhang, 0.9f, "Wallride outside wall never engaged the capture curl.");
                Assert.Greater(peakMorph, 0.9f,
                    "Wallride no longer folds the road onto its intended outer-wall surface.");
                Assert.Less(minimumInsideWall, 0.1f,
                    "Wallride no longer opens its inside edge as originally authored.");

                // Weld boundaries are neutral (the migration happens inside the section).
                Assert.Less(frames[0].TurnRounding, 0.01f);
                Assert.Less(frames[frames.Length - 1].TurnRounding, 0.01f);
            }
        }

        [Test]
        public void WallrideAfterCancelledHalfLoopRotationsRaisesGeometricOutsideWall()
        {
            var limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var cfg = ResolvedTrackGenerationConfig.Resolve(limits,
                    TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
                var entry = TrackConnectionFrame.Origin(cfg.RoadWidth);
                // Report -1054293041 reaches the wallride physically upright after a
                // 180-degree vertical rotation and a cancelling 180-degree road roll.
                entry.Forward = Vector3.back;
                entry.Right = Vector3.left;
                entry.Up = Vector3.up;
                entry.AccumulatedVerticalRotation = 180f;
                entry.AccumulatedRoadRoll = 180f;

                var def = new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.WallrideTurn,
                    Direction = SectionTurnDirection.Right,
                    TurnAngle = 105f,
                    Radius = 700f,
                    BankingAngle = 75f,
                    Width = cfg.RoadWidth
                };
                var frames = SectionFrameBuilders.BuildWallrideTurn(entry, def, FrameBuildContext.From(cfg));
                int mid = frames.Length / 2;
                var f = frames[mid];
                Vector3 inward = (frames[mid + 1].Forward - frames[mid - 1].Forward).normalized;
                Vector3 raisedWallDirection = f.LeftWallMultiplier > f.RightWallMultiplier ? -f.Right : f.Right;

                Assert.Greater(Vector3.Dot(frames[mid].Position - entry.Position, entry.Right), 0f,
                    "The logical right wallride bent to the physical left after the half-loop history.");
                Assert.Greater(f.LeftWallMultiplier, f.RightWallMultiplier,
                    "A right wallride did not select its local left (outside) wall.");
                Assert.Less(Vector3.Dot(raisedWallDirection, inward), -0.8f,
                    "The raised wall points toward the center of curvature, so it is geometrically inside.");
            }
            finally
            {
                Object.DestroyImmediate(limits);
            }
        }

        [Test]
        public void DisallowedFeaturesNeverAppear()
        {
            var result = GenerateWith(s =>
            {
                s.Features.FullPipes = new TrackFeatureRule(false, 0, 0, 0f);
                s.Features.Wallrides = new TrackFeatureRule(false, 0, 0, 0f);
            }, 8300);
            Assert.IsTrue(result is { Success: true });
            Assert.AreEqual(0, result.Layout.Metrics.FullPipeCount);
            Assert.AreEqual(0, result.Layout.Metrics.WallrideCount);
        }
    }
}

