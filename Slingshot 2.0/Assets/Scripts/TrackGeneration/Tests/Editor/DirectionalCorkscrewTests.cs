using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage F foundation acceptance: a directional corkscrew is the inline barrel with a
    /// ±45/±90 turn token folded onto its axis via MakeCorkscrewDef(horizontalTurn). Because
    /// both the 2D plan-view (TurnAngle / PlanHorizontalLength / PlanLateralOffset) and the
    /// 3D geometry are derived from the SAME StampRotationalEventPlan frame walk, the plan-view
    /// heading tracks the requested token and closure agrees with the built geometry by
    /// construction. Inline corkscrews (horizontalTurn == 0) stay heading-neutral and unchanged.
    /// The barrel roll is preserved regardless of the turn.
    /// (Tightening the token match to the plan's 1e-3 target is an in-engine measurement task;
    ///  here we assert the directional turn is realized close to its token and mirror-symmetric.)
    /// </summary>
    public class DirectionalCorkscrewTests
    {
        private static ResolvedTrackGenerationConfig Cfg(out TrackConfig config)
        {
            config = TrackGenerationTestUtil.CreateConfig();
            return ResolvedTrackGenerationConfig.Resolve(config,
                TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
        }

        private static TrackMacroSectionDefinition Build(ResolvedTrackGenerationConfig cfg,
            float signedRoll, float horizontalTurn, uint seed)
        {
            var rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            return CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, signedRoll, "df_test", horizontalTurn);
        }

        [Test]
        public void InlineCorkscrew_StaysHeadingNeutral()
        {
            var cfg = Cfg(out var config);
            try
            {
                var def = Build(cfg, +360f, 0f, 101u);
                Assert.Less(Mathf.Abs(def.TurnAngle), 3f,
                    $"inline corkscrew should net ~0° heading, got {def.TurnAngle:F2}°");
                Assert.Greater(Mathf.Abs(def.RollChange), 90f, "a real barrel roll must survive");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void DirectionalCorkscrew_RealizesTurnToken_AndKeepsRoll()
        {
            var cfg = Cfg(out var config);
            try
            {
                foreach (float roll in new[] { +360f, -360f })
                    foreach (float turn in new[] { +45f, -45f, +90f, -90f })
                    {
                        var def = Build(cfg, roll, turn, 200u + (uint)Mathf.RoundToInt(turn));
                        Assert.AreEqual(turn, def.TurnAngle, 6f,
                            $"roll {roll:+0}, turn {turn:+0}: plan-view heading {def.TurnAngle:F2}° should track the token");
                        Assert.Greater(Mathf.Abs(def.RollChange), 90f,
                            $"roll {roll:+0}, turn {turn:+0}: barrel roll must be preserved");
                        Assert.AreEqual(def.TurnAngle, def.Contract.HeadingDeltaDegrees, 1e-3f,
                            "contract heading must match the stamped plan-view heading");
                        Assert.IsTrue(def.DebugName.Contains("Turn"), "directional variant should be tagged");
                    }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void RollHandedness_HasASafeChoice_AndTheTwoSignsDiffer()
        {
            var cfg = Cfg(out var config);
            try
            {
                float roll = 360f;
                foreach (float turn in new[] { +45f, -45f })
                {
                    var rngA = new Unity.Mathematics.Random(700u + (uint)Mathf.RoundToInt(turn));
                    var rngB = rngA;
                    var plus = CorkscrewPattern.MakeCorkscrewDef(cfg, ref rngA, +roll, "dco", turn);
                    var minus = CorkscrewPattern.MakeCorkscrewDef(cfg, ref rngB, -roll, "dco", turn);

                    float mPlus = TrackTopologyPlanner.DirectionalCorkscrewFloorMargin(plus, cfg);
                    float mMinus = TrackTopologyPlanner.DirectionalCorkscrewFloorMargin(minus, cfg);

                    // The whole point of the fix: the two handednesses are NOT equivalent,
                    // and at least one keeps the craft pressed on the road (positive margin).
                    Assert.AreNotEqual(mPlus, mMinus, $"turn {turn:+0}: roll handedness must matter");
                    Assert.Greater(Mathf.Max(mPlus, mMinus), 0f,
                        $"turn {turn:+0}: at least one roll handedness must contain the craft");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void RollHandedness_SelectionMirrors_ForOppositeTurns()
        {
            var cfg = Cfg(out var config);
            try
            {
                float roll = 360f;
                var rngR = new Unity.Mathematics.Random(808u);
                var rngR2 = rngR;
                var rngL = new Unity.Mathematics.Random(808u);
                var rngL2 = rngL;

                // +45 turn: which roll sign wins?
                float rMarginPlus = TrackTopologyPlanner.DirectionalCorkscrewFloorMargin(
                    CorkscrewPattern.MakeCorkscrewDef(cfg, ref rngR, +roll, "d", +45f), cfg);
                float rMarginMinus = TrackTopologyPlanner.DirectionalCorkscrewFloorMargin(
                    CorkscrewPattern.MakeCorkscrewDef(cfg, ref rngR2, -roll, "d", +45f), cfg);
                bool plusWinsForRightTurn = rMarginPlus >= rMarginMinus;

                // -45 turn (mirror): the opposite roll sign should win.
                float lMarginPlus = TrackTopologyPlanner.DirectionalCorkscrewFloorMargin(
                    CorkscrewPattern.MakeCorkscrewDef(cfg, ref rngL, +roll, "d", -45f), cfg);
                float lMarginMinus = TrackTopologyPlanner.DirectionalCorkscrewFloorMargin(
                    CorkscrewPattern.MakeCorkscrewDef(cfg, ref rngL2, -roll, "d", -45f), cfg);
                bool plusWinsForLeftTurn = lMarginPlus >= lMarginMinus;

                Assert.AreNotEqual(plusWinsForRightTurn, plusWinsForLeftTurn,
                    "the safe roll handedness for a +45 turn should flip for the mirrored -45 turn");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void DirectionalCorkscrew_IsMirrorSymmetricInTurn()
        {
            var cfg = Cfg(out var config);
            try
            {
                foreach (float mag in new[] { 45f, 90f })
                {
                    var right = Build(cfg, +360f, +mag, 300u + (uint)mag);
                    var left = Build(cfg, +360f, -mag, 300u + (uint)mag);
                    // Same roll, opposite turn token → opposite plan-view heading and mirrored lateral offset.
                    Assert.AreEqual(-right.TurnAngle, left.TurnAngle, 1.5f, $"{mag}: turn sign should mirror");
                    Assert.AreEqual(right.PlanHorizontalLength, left.PlanHorizontalLength, 2f,
                        $"{mag}: forward reach should match for mirrored turns");
                    Assert.AreEqual(-right.PlanLateralOffset, left.PlanLateralOffset, 2f,
                        $"{mag}: lateral offset should mirror");
                }
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
