using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage 3 guards: the unified parametric cross-section — stable topology, circular
    /// ordinary half-pipes, explicit-feature overhang, seamless pipe closure, dynamic
    /// rounding reaching its targets, and smooth per-frame evolution.
    /// </summary>
    public class CrossSectionTests
    {
        private static TrackRoadProfileSettings Profile() => new TrackRoadProfileSettings
        {
            Shape = RoadCrossSectionShape.HalfPipe,
            SideHeight = 6f,
            WallCurve01 = 1f,
            CenterFlatWidthRatio = 0.35f,
            ProfileResolution = 32,
            SafetyLipHeight = 1f,
            MinTurnCenterFlatRatio = 0.05f,
            MaxOverhangAngleDeg = 20f,
            OverhangRadius = 8f
        };

        private static TrackConnectionFrame Frame(float width = 26f) => new TrackConnectionFrame
        {
            Position = Vector3.zero,
            Forward = Vector3.forward,
            Right = Vector3.right,
            Up = Vector3.up,
            Width = width
        };

        private static Vector2[] Evaluate(TrackRoadProfileSettings p, TrackConnectionFrame f)
        {
            var pts = new Vector2[TrackCrossSection.PointCount(p)];
            TrackCrossSection.Evaluate(p, f, pts);
            return pts;
        }

        /// <summary>2D segment-intersection scan over the chain (non-adjacent segments only).</summary>
        private static bool SelfIntersects(Vector2[] pts)
        {
            bool SegInt(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
                float d1 = Cross(d - c, a - c), d2 = Cross(d - c, b - c);
                float d3 = Cross(b - a, c - a), d4 = Cross(b - a, d - a);
                return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                       ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
            }

            for (int i = 0; i < pts.Length - 1; i++)
            {
                for (int j = i + 2; j < pts.Length - 1; j++)
                {
                    if (SegInt(pts[i], pts[i + 1], pts[j], pts[j + 1])) return true;
                }
            }
            return false;
        }

        [Test]
        public void PlainProfileMatchesLegacyHeightFunctionOnTheBowl()
        {
            var p = Profile();
            var f = Frame();
            var pts = Evaluate(p, f);
            int ext = TrackCrossSection.ExtensionPointCount(p);
            int bowl = p.ProfilePointCount;

            for (int i = 0; i < bowl; i++)
            {
                float xNorm = p.ProfileXAt(i, bowl);
                float expected = p.HeightAt(xNorm, f.Width * 0.5f, p.SideHeight);
                Assert.AreEqual(xNorm * f.Width * 0.5f, pts[ext + i].x, 1e-3f, $"bowl x at {i}");
                Assert.AreEqual(expected, pts[ext + i].y, 1e-3f, $"bowl height at {i}");
            }
        }

        [Test]
        public void CatchWallPassesVerticalWithoutSelfIntersection()
        {
            var p = Profile();
            var f = Frame();
            f.RightOverhang = 1f; // full catch-wall engagement
            var pts = Evaluate(p, f);

            Assert.IsFalse(SelfIntersects(pts), "Full catch wall self-intersects.");

            // A true overhang: the wall reaches an outermost APEX (where it passes
            // vertical) and then re-curves INWARD above it — one x, two heights, which
            // no height function can express. The tip must sit inward of the apex and
            // above the wall top.
            int ext = TrackCrossSection.ExtensionPointCount(p);
            int total = pts.Length;
            float apexX = float.MinValue, apexY = 0f;
            for (int i = total - ext; i < total; i++)
            {
                if (pts[i].x > apexX) { apexX = pts[i].x; apexY = pts[i].y; }
            }
            Vector2 tip = pts[total - 1];
            float wallTopY = p.ClampedSideHeight(f.Width * 0.5f, p.SideHeight);
            Assert.Greater(apexX, f.Width * 0.5f - 0.01f, "Overhang apex never reached the road edge.");
            Assert.Less(tip.x, apexX - 0.05f, "Catch wall tip did not re-curve inward past its apex.");
            Assert.Greater(tip.y, apexY - 0.01f, "Catch wall tip is not above its apex.");
            Assert.Greater(tip.y, wallTopY, "Catch wall tip did not rise above the wall top.");
        }

        [Test]
        public void FullPipeClosesSeamlessly()
        {
            var p = Profile();
            var f = Frame(width: 40f);
            f.PipeClosure = 1f;
            var pts = Evaluate(p, f);

            // Tips meet at the roof: no collision seam where the pipe closes.
            Vector2 tipL = pts[0];
            Vector2 tipR = pts[pts.Length - 1];
            Assert.Less(Vector2.Distance(tipL, tipR), 0.05f, "Pipe roof tips do not meet.");

            // Every point sits on the circle of radius width/2 centered at (0, R).
            float R = f.Width * 0.5f;
            foreach (var pt in pts)
            {
                float rad = Vector2.Distance(pt, new Vector2(0f, R));
                Assert.AreEqual(R, rad, R * 0.02f, "Closed pipe is not circular.");
            }

            Assert.IsFalse(SelfIntersects(pts), "Closed pipe chain self-intersects.");
        }

        [Test]
        public void PartialClosureStaysBetweenOpenAndClosed()
        {
            var p = Profile();
            for (int step = 0; step <= 10; step++)
            {
                var f = Frame(width: 40f);
                f.PipeClosure = step / 10f;
                var pts = Evaluate(p, f);
                Assert.IsFalse(SelfIntersects(pts), $"Self-intersection at closure {f.PipeClosure:F1}.");
                foreach (var pt in pts)
                {
                    Assert.IsFalse(float.IsNaN(pt.x) || float.IsNaN(pt.y), "NaN in chain.");
                    Assert.GreaterOrEqual(pt.y, -0.01f, "Chain dips below the floor baseline.");
                }
            }
        }

        [Test]
        public void TurnRoundingReachesTheMinimumFlatRatio()
        {
            var p = Profile();
            var fFlat = Frame();
            var fRound = Frame();
            fRound.TurnRounding = 1f;

            var flatPts = Evaluate(p, fFlat);
            var roundPts = Evaluate(p, fRound);
            int ext = TrackCrossSection.ExtensionPointCount(p);
            int bowl = p.ProfilePointCount;

            // Count bowl points at (near) zero height — the flat center samples.
            int FlatCount(Vector2[] pts)
            {
                int c = 0;
                for (int i = 0; i < bowl; i++)
                    if (pts[ext + i].y < 0.01f) c++;
                return c;
            }

            Assert.Less(FlatCount(roundPts), FlatCount(flatPts),
                "Full turn rounding did not shrink the flat center.");

            // The rounded bowl must still be monotone in x (ordered chain, no folds).
            for (int i = 1; i < bowl; i++)
                Assert.GreaterOrEqual(roundPts[ext + i].x, roundPts[ext + i - 1].x - 1e-4f,
                    "Rounded bowl x-order broke.");
        }

        [Test]
        public void InversionBelly_IsMandatoryForCorkscrewsAndAuthoredDriveabilityFeatures()
        {
            var cfg = new ResolvedTrackGenerationConfig
            {
                RotationUnitDegrees = 90f,
                BankTransitionLength = 100f,
                DynamicTurnRoundingEnabled = false
            };

            RotationalPhaseDefinition Phase(RotationalPhaseAxis axis, int units,
                float length, float horizontalTurn = 0f)
                => new RotationalPhaseDefinition
                {
                    Axis = axis,
                    RotationUnits = units,
                    FirstHalfLength = length * 0.5f,
                    SecondHalfLength = length * 0.5f,
                    FirstHalfRadius = 200f,
                    SecondHalfRadius = 200f,
                    HorizontalTurnDegrees = horizontalTurn,
                    BlendToNext = RotationalBlendPreset.None
                };

            GeneratedTrackSection Section(TrackMacroSectionType type,
                params RotationalPhaseDefinition[] phases)
            {
                var definition = new TrackMacroSectionDefinition
                {
                    SectionType = type,
                    Width = 100f,
                    RotationalPhases = phases.Length > 0
                        ? new List<RotationalPhaseDefinition>(phases)
                        : null
                };
                float length = phases.Length > 0
                    ? SectionFrameBuilders.RotationalEventLength(definition)
                    : 800f;
                var frames = new TrackConnectionFrame[25];
                for (int i = 0; i < frames.Length; i++)
                {
                    frames[i] = TrackConnectionFrame.Origin(100f);
                    frames[i].ArcLength = length * i / (frames.Length - 1);
                }
                return new GeneratedTrackSection
                {
                    Definition = definition,
                    StartFrame = frames[0],
                    EndFrame = frames[frames.Length - 1],
                    SubdivisionFrames = frames
                };
            }

            var inline = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.RoadRoll, 4, 800f));
            var directional = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.RoadRoll, 4, 800f, 90f));
            var doubled = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.RoadRoll, 8, 1400f));
            var loopToCorkscrew = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.VerticalCenterline, 4, 600f),
                Phase(RotationalPhaseAxis.RoadRoll, 4, 800f));
            var halfLoopToCorkscrew = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.VerticalCenterline, 2, 400f),
                Phase(RotationalPhaseAxis.RoadRoll, 6, 1000f));
            var legacy = Section(TrackMacroSectionType.Corkscrew);
            var heartline = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.RoadRoll, 4, 900f));
            heartline.Definition.SemanticElement = SemanticElementId.HeartlineRoll;
            var zeroG = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.RoadRoll, 4, 1000f));
            zeroG.Definition.SemanticElement = SemanticElementId.ZeroGRoll;
            var diveLoop = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.RoadRoll, 2, 450f),
                Phase(RotationalPhaseAxis.VerticalCenterline, 2, 850f));
            diveLoop.Definition.SemanticElement = SemanticElementId.DiveLoop;
            var immelmann = Section(TrackMacroSectionType.RotationalEvent,
                Phase(RotationalPhaseAxis.VerticalCenterline, 2, 400f),
                Phase(RotationalPhaseAxis.RoadRoll, 2, 500f));
            immelmann.Definition.SemanticElement = SemanticElementId.Immelmann;

            var sections = new List<GeneratedTrackSection>
            {
                inline, directional, doubled, loopToCorkscrew,
                halfLoopToCorkscrew, legacy, heartline, zeroG, diveLoop, immelmann
            };
            MethodInfo apply = typeof(TrackCandidateBuilder).GetMethod(
                "ApplyInversionBelly", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(apply, "The finished-frame inversion belly pass is missing.");
            apply.Invoke(null, new object[] { sections, cfg });

            void AssertBelly(GeneratedTrackSection section, string variant)
            {
                float peak = 0f;
                foreach (TrackConnectionFrame frame in section.SubdivisionFrames)
                    peak = Mathf.Max(peak, frame.TurnRounding);
                Assert.AreEqual(0.45f, peak, 0.001f,
                    $"{variant} did not receive the shallow concave belly.");
                Assert.AreEqual(0f, section.SubdivisionFrames[0].TurnRounding, 0.0001f,
                    $"{variant} belly did not fade to zero at entry.");
                Assert.AreEqual(0f,
                    section.SubdivisionFrames[section.SubdivisionFrames.Length - 1].TurnRounding,
                    0.0001f, $"{variant} belly did not fade to zero at exit.");
            }

            AssertBelly(inline, "Inline corkscrew");
            AssertBelly(directional, "Directional corkscrew");
            AssertBelly(doubled, "Double corkscrew");
            AssertBelly(loopToCorkscrew, "Loop-to-corkscrew");
            AssertBelly(halfLoopToCorkscrew, "Half-loop-to-corkscrew");
            AssertBelly(legacy, "Legacy corkscrew");
            AssertBelly(heartline, "Heartline Roll");
            AssertBelly(zeroG, "Zero-G Roll");
            AssertBelly(diveLoop, "Dive Loop");

            Assert.Greater(diveLoop.SubdivisionFrames[diveLoop.SubdivisionFrames.Length / 2].TurnRounding,
                0.4f, "Dive Loop lost its belly at the roll-to-dive internal phase hand-off.");

            foreach (TrackConnectionFrame frame in immelmann.SubdivisionFrames)
                Assert.AreEqual(0f, frame.TurnRounding, 0.0001f,
                    "A plain Immelmann rollout was incorrectly treated as a corkscrew.");
        }

        [Test]
        public void OrdinaryHalfPipeStaysCircularWhenItsDepthChanges()
        {
            var p = Profile();
            var f = Frame();
            f.SideHeight = 8f;
            var pts = Evaluate(p, f);

            float halfW = f.Width * 0.5f;
            p.ResolveCircularBowl(halfW, f.SideHeight, f.TurnRounding,
                out float flat, out _, out float evaluatedSideHeight);
            float radius = evaluatedSideHeight * p.ShapeDepthScale;
            float shoulder = halfW * flat;
            int ext = TrackCrossSection.ExtensionPointCount(p);

            Assert.AreEqual(8f, radius, 1e-4f, "Per-frame wall depth was not preserved as the circle radius.");
            Assert.AreEqual(halfW - radius, shoulder, 1e-4f,
                "The floor shoulder did not move with the circular wall radius.");

            for (int i = 0; i < p.ProfilePointCount; i++)
            {
                Vector2 point = pts[ext + i];
                if (Mathf.Abs(point.x) <= shoulder + 0.01f) continue;

                float centerX = Mathf.Sign(point.x) * shoulder;
                float measuredRadius = Vector2.Distance(point, new Vector2(centerX, radius));
                Assert.AreEqual(radius, measuredRadius, 0.01f,
                    $"Bowl sample {i} became elliptical instead of circular.");
            }
        }

        [Test]
        public void OrdinaryGeneratedRoadDoesNotBecomeAnImplicitWallride()
        {
            TrackGenerationResult result = null;
            for (int s = 7640; s < 7650 && (result == null || !result.Success); s++)
            {
                var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Technical);
                settings.Road.OutsideCatchWall = true; // legacy serialized toggle must remain harmless
                result = TrackGenerationTestUtil.Generate(settings, s);
            }
            Assert.IsTrue(result is { Success: true }, "No valid track generated.");

            int ordinaryFrames = 0;
            foreach (var sec in result.Layout.Sections)
            {
                if (sec.SubdivisionFrames == null) continue;
                if (sec.Definition.SectionType == TrackMacroSectionType.WallrideTurn) continue;
                foreach (TrackConnectionFrame frame in sec.SubdivisionFrames)
                {
                    ordinaryFrames++;
                    Assert.AreEqual(0f, frame.LeftOverhang, 1e-5f,
                        $"Ordinary section {sec.Definition.DebugName} authored a left overhang.");
                    Assert.AreEqual(0f, frame.RightOverhang, 1e-5f,
                        $"Ordinary section {sec.Definition.DebugName} authored a right overhang.");
                    Assert.GreaterOrEqual(frame.LeftWallSuppression, -1e-5f,
                        $"Ordinary section {sec.Definition.DebugName} stretched its left wall.");
                    Assert.GreaterOrEqual(frame.RightWallSuppression, -1e-5f,
                        $"Ordinary section {sec.Definition.DebugName} stretched its right wall.");
                }
            }

            Assert.Greater(ordinaryFrames, 20, "The generated track did not exercise ordinary road frames.");
        }

        [Test]
        public void TurnRoundingKeepsTextureColumnsAtStableLateralPositions()
        {
            var p = Profile();
            var flat = Frame();
            var rounded = Frame();
            rounded.TurnRounding = 1f;

            Vector2[] flatPoints = Evaluate(p, flat);
            Vector2[] roundedPoints = Evaluate(p, rounded);
            Assert.AreEqual(flatPoints.Length, roundedPoints.Length);
            int ext = TrackCrossSection.ExtensionPointCount(p);
            for (int i = 0; i < p.ProfilePointCount; i++)
                Assert.AreEqual(flatPoints[ext + i].x, roundedPoints[ext + i].x, 1e-4f,
                    $"Bowl texture column {i} moved laterally when the turn rounded.");
        }

        [Test]
        public void GeneratedTurnRoundingIsSmoothAlongTheArc()
        {
            TrackGenerationResult result = null;
            for (int s = 7600; s < 7610 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Technical), s);
            Assert.IsTrue(result is { Success: true }, "No valid track generated.");

            float peak = 0f;
            foreach (var sec in result.Layout.Sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;
                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);
                    float rate = Mathf.Abs(frames[i].TurnRounding - frames[i - 1].TurnRounding) / ds;
                    // Full rounding over less than ~80 m would be a visible ridge at speed.
                    Assert.LessOrEqual(rate, 1f / 80f,
                        $"Turn rounding steps too fast at {sec.Definition.DebugName} ring {i}.");
                    peak = Mathf.Max(peak, frames[i].TurnRounding);
                }
            }

            Assert.Greater(peak, 0.2f, "No turn ever engaged dynamic rounding on a Technical track.");
        }

        [Test]
        public void EvaluatorIsDeterministic()
        {
            var p = Profile();
            var f = Frame();
            f.TurnRounding = 0.5f;
            f.LeftOverhang = 0.3f;
            f.PipeClosure = 0.4f;

            var a = Evaluate(p, f);
            var b = Evaluate(p, f);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i], $"Non-deterministic evaluation at {i}.");
        }
    }
}
