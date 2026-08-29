using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    public class GeometryPlannerTests
    {
        [Test]
        public void DistributedClimbCrossesOrdinaryBoundariesAsOneProfile()
        {
            var sections = new List<GeneratedTrackSection>
            {
                MakeStraight(0, 0f, 268f, 0f, 120f),
                MakeStraight(1, 268f, 990f, 120f, 120f)
            };
            // Exact regression shape from TrackReport_-157394052: the steep ordinary
            // climb is followed by a long, ordinary JumpApproach. Pattern membership
            // must not prevent that safe road from sharing the gradual grade.
            sections[1].Definition.PatternId = "JumpGap_Test";
            sections[1].Definition.DebugName = "JumpApproach";
            var cfg = PlannerConfig();

            Assert.IsTrue(VerticalProfilePlanner.TryApply(sections, cfg, out string failure), failure);
            Assert.AreEqual(0f, sections[0].StartFrame.Position.y, 0.001f);
            Assert.AreEqual(120f, sections[1].EndFrame.Position.y, 0.001f);

            TrackConnectionFrame left = sections[0].EndFrame;
            TrackConnectionFrame right = sections[1].StartFrame;
            Assert.AreEqual(left.Position, right.Position);
            Assert.AreEqual(left.Forward, right.Forward);
            Assert.AreEqual(left.VerticalCurvature, right.VerticalCurvature, 0.0000001f);
            Assert.AreEqual(left.VerticalCurvatureRate, right.VerticalCurvatureRate, 0.0000001f);
            Assert.Greater(left.PitchAngle, 1f,
                "The ordinary section boundary reset the climb to level instead of sampling one sustained profile.");

            for (int s = 0; s < sections.Count; s++)
                foreach (TrackConnectionFrame frame in sections[s].SubdivisionFrames)
                    Assert.AreEqual(frame.ArcLength, frame.Position.z, 0.001f,
                        "The vertical planner changed the horizontal route or its plan coordinate.");
        }

        [Test]
        public void CanonicalCrossSectionRateLimitsOrdinaryWidthChangeAndWeldsCopies()
        {
            var sections = new List<GeneratedTrackSection>
            {
                MakeStraight(0, 0f, 300f, 0f, 0f, 92f),
                MakeStraight(1, 300f, 600f, 0f, 0f, 45f),
                MakeStraight(2, 600f, 900f, 0f, 0f, 92f)
            };
            var cfg = PlannerConfig();
            CrossSectionPlanner.Apply(sections, cfg);

            Assert.AreEqual(sections[0].EndFrame.Width, sections[1].StartFrame.Width, 0.0001f);
            Assert.AreEqual(sections[1].EndFrame.Width, sections[2].StartFrame.Width, 0.0001f);
            foreach (GeneratedTrackSection section in sections)
            {
                TrackConnectionFrame[] frames = section.SubdivisionFrames;
                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Vector3.Distance(frames[i - 1].Position, frames[i].Position);
                    Assert.LessOrEqual(Mathf.Abs(frames[i].Width - frames[i - 1].Width) / ds,
                        0.121f);
                    Assert.Greater(frames[i].SideHeight, 0f);
                }
            }
        }

        [Test]
        public void CanonicalCrossSectionSharesClosedLapSeamContract()
        {
            var sections = new List<GeneratedTrackSection>
            {
                MakePlanSection(0, new Vector3(0f, 0f, 0f), new Vector3(300f, 0f, 0f),
                    TrackMacroSectionType.BankedCurve),
                MakePlanSection(1, new Vector3(300f, 0f, 0f), new Vector3(300f, 0f, 300f),
                    TrackMacroSectionType.Straight),
                MakePlanSection(2, new Vector3(300f, 0f, 300f), new Vector3(0f, 0f, 300f),
                    TrackMacroSectionType.Straight),
                MakePlanSection(3, new Vector3(0f, 0f, 300f), new Vector3(0f, 0f, 0f),
                    TrackMacroSectionType.Straight)
            };

            CrossSectionPlanner.Apply(sections, PlannerConfig());

            Assert.AreEqual(sections[3].EndFrame.Width, sections[0].StartFrame.Width, 0.0001f);
            Assert.AreEqual(sections[3].EndFrame.SideHeight,
                sections[0].StartFrame.SideHeight, 0.0001f,
                "The closed lap seam must be one shared wall-height boundary.");
        }

        [Test]
        public void ObjectiveJumpReportsThreeSpeedCapture()
        {
            TrackConfig limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(
                    TrackStylePresetLibrary.Rollercoaster);
                ResolvedTrackGenerationConfig cfg = ResolvedTrackGenerationConfig.Resolve(limits, settings);
                int solvedCount = 0;
                for (uint seed = 1; seed <= 64; seed++)
                {
                    var rng = new Unity.Mathematics.Random(seed);
                    if (!JumpBallistics.TrySolve(cfg, ref rng, out JumpBallistics.Solution solution))
                        continue;
                    solvedCount++;
                    Assert.Greater(solution.ApexHeight, 0f);
                    Assert.Greater(solution.TimeToApex, 0f);
                    Assert.GreaterOrEqual(solution.LaunchPitchDeg,
                        cfg.MinJumpLaunchPitchDegrees - 0.01f,
                        "The air-gap lip must finish at the configured upward launch angle.");
                    Assert.GreaterOrEqual(solution.LipHeight, cfg.MinJumpHeight - 0.01f,
                        "The progressive launch ramp must reach the configured minimum lip elevation.");
                    Assert.IsTrue(solution.CapturesMinimumSpeed);
                    Assert.IsTrue(solution.CapturesNominalSpeed);
                    Assert.IsTrue(solution.CapturesMaximumSpeed);
                }
                Assert.AreEqual(64, solvedCount,
                    "A required Rollercoaster jump must not depend on the outer track-attempt seed.");
            }
            finally
            {
                Object.DestroyImmediate(limits);
            }
        }

        [Test]
        public void JumpPlanningReserveDoesNotShrinkSolverSafetyEnvelope()
        {
            TrackConfig limits = TrackGenerationTestUtil.CreateConfig();
            try
            {
                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(
                    TrackStylePresetLibrary.Rollercoaster);
                settings.Features.JumpCaptureSpeedVariation = 0.06f;
                ResolvedTrackGenerationConfig cfg = ResolvedTrackGenerationConfig.Resolve(limits, settings);

                float baseLanding = cfg.DesignSpeedMps * limits.MaxLandingTransitionSeconds;
                Assert.AreEqual(baseLanding * (1f + 32f * 0.06f), cfg.MaxLandingTransitionLength, 0.1f);
                Assert.AreEqual(cfg.MaxLandingTransitionLength, cfg.JumpLandingPlanningLength, 0.1f,
                    "Planning must reserve the same capture envelope the jump solver can consume.");
            }
            finally
            {
                Object.DestroyImmediate(limits);
            }
        }

        private static GeneratedTrackSection MakeStraight(int index, float z0, float z1,
            float y0, float y1, float width = 92f)
        {
            const int intervals = 20;
            var frames = new TrackConnectionFrame[intervals + 1];
            for (int i = 0; i <= intervals; i++)
            {
                float t = (float)i / intervals;
                frames[i] = TrackConnectionFrame.Origin(width);
                frames[i].Position = new Vector3(0f, Mathf.Lerp(y0, y1, t), Mathf.Lerp(z0, z1, t));
                frames[i].ArcLength = Mathf.Lerp(z0, z1, t);
            }
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                Length = z1 - z0,
                Width = width,
                ElevationChange = y1 - y0,
                DebugName = $"PlannerTest_{index}",
                Contract = SectionConnectionContract.Level()
            };
            return new GeneratedTrackSection
            {
                Definition = definition,
                SectionIndex = index,
                SubdivisionFrames = frames,
                StartFrame = frames[0],
                EndFrame = frames[frames.Length - 1]
            };
        }

        private static GeneratedTrackSection MakePlanSection(int index, Vector3 start,
            Vector3 end, TrackMacroSectionType type, float width = 92f)
        {
            const int intervals = 20;
            var frames = new TrackConnectionFrame[intervals + 1];
            for (int i = 0; i <= intervals; i++)
            {
                float t = (float)i / intervals;
                frames[i] = TrackConnectionFrame.Origin(width);
                frames[i].Position = Vector3.Lerp(start, end, t);
                frames[i].ArcLength = Vector3.Distance(start, end) * t;
            }
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = type,
                Length = Vector3.Distance(start, end),
                Width = width,
                DebugName = $"ClosedPlannerTest_{index}",
                Contract = SectionConnectionContract.Level()
            };
            return new GeneratedTrackSection
            {
                Definition = definition,
                SectionIndex = index,
                SubdivisionFrames = frames,
                StartFrame = frames[0],
                EndFrame = frames[frames.Length - 1]
            };
        }

        private static ResolvedTrackGenerationConfig PlannerConfig()
        {
            return new ResolvedTrackGenerationConfig
            {
                DesignSpeedMps = 361f,
                Gravity = 9.81f,
                MaxClimbAngle = 34f,
                MaxDropAngle = 36f,
                MaxCurvatureInducedG = 15f,
                MaxVerticalCurvatureRate = 0.00005f,
                PitchTransitionLength = 220f,
                WidthTransitionLength = 250f,
                CrossSectionTransitionLength = 250f,
                RoadWidth = 92f,
                RoadProfile = new TrackRoadProfileSettings
                {
                    Shape = RoadCrossSectionShape.HalfPipe,
                    SideHeight = 26f,
                    CenterFlatWidthRatio = 0.2f,
                    ProfileResolution = 16,
                    ColliderProfileResolution = 12
                }
            };
        }
    }
}
