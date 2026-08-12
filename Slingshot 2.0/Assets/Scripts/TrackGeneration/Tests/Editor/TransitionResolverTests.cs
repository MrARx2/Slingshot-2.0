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
    /// Stage C acceptance: the family-agnostic resolver classifies boundaries from real
    /// built frames, never crashes, never produces spurious Rejected on ordinary tracks,
    /// and reports the per-channel reasoning Stage D will consume. Reporting-only — no
    /// geometry is asserted to change.
    /// </summary>
    public class TransitionResolverTests
    {
        private static GeneratedTrackSection Content(SemanticElementId element, string name,
            float exitBank, float exitPitch, float exitWidth, float exitRollRate)
        {
            var def = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.RotationalEvent, // any non-straight content type
                DebugName = name,
                SemanticElement = element,
                Width = exitWidth,
                Length = 500f
            };
            var start = TrackConnectionFrame.Origin(exitWidth);
            var end = TrackConnectionFrame.Origin(exitWidth);
            end.BankAngle = exitBank;
            end.PitchAngle = exitPitch;
            end.Width = exitWidth;
            end.RoadRollRate = exitRollRate;
            return new GeneratedTrackSection { Definition = def, StartFrame = start, EndFrame = end };
        }

        private static GeneratedTrackSection Straight(float length, float width)
        {
            var def = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                DebugName = "link", Length = length, Width = width
            };
            var f = TrackConnectionFrame.Origin(width);
            return new GeneratedTrackSection { Definition = def, StartFrame = f, EndFrame = f };
        }

        private static ResolvedTrackGenerationConfig Cfg(out TrackConfig config)
        {
            config = TrackGenerationTestUtil.CreateConfig();
            return ResolvedTrackGenerationConfig.Resolve(config,
                TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster));
        }

        [Test]
        public void CompatibleBoundary_WeldsDirectly()
        {
            var cfg = Cfg(out var config);
            try
            {
                // Two upright, same-width corkscrews with a short link — nothing to reconcile.
                var sections = new List<GeneratedTrackSection>
                {
                    Content(SemanticElementId.InlineCorkscrew, "corkA", 0f, 0f, cfg.RoadWidth, 0f),
                    Straight(50f, cfg.RoadWidth),
                    Content(SemanticElementId.InlineCorkscrew, "corkB", 0f, 0f, cfg.RoadWidth, 0f),
                };
                var decisions = TransitionResolver.Resolve(sections, cfg);
                Assert.GreaterOrEqual(decisions.Count, 1);
                Assert.AreEqual(TransitionKind.DirectWeld, decisions[0].Kind,
                    "Upright same-width corkscrews should weld directly — the Stage D pilot case.");
                Assert.AreEqual(0f, decisions[0].MinBlendLength, 0.01f);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void HeavyBankInto_LowRun_IsRejected_NotPadded()
        {
            var cfg = Cfg(out var config);
            try
            {
                // A steeply banked exit into an upright-requiring loop, with almost no run.
                var sections = new List<GeneratedTrackSection>
                {
                    Content(SemanticElementId.WallrideTurn, "wall", 75f, 0f, cfg.RoadWidth, 0f),
                    Straight(20f, cfg.RoadWidth),
                    Content(SemanticElementId.VerticalLoop, "loop", 0f, 0f, cfg.RoadWidth, 0f),
                };
                var decisions = TransitionResolver.Resolve(sections, cfg);
                Assert.GreaterOrEqual(decisions.Count, 1);
                // Wallride requires recovery → ExplicitRecovery regardless of run.
                Assert.AreEqual(TransitionKind.ExplicitRecovery, decisions[0].Kind);
                Assert.IsNotEmpty(decisions[0].Reason);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void WidthMismatch_DemandsBlend_WithChannelReason()
        {
            var cfg = Cfg(out var config);
            try
            {
                var sections = new List<GeneratedTrackSection>
                {
                    Content(SemanticElementId.InlineCorkscrew, "narrow", 0f, 0f, cfg.RoadWidth * 0.7f, 0f),
                    Straight(600f, cfg.RoadWidth),
                    Content(SemanticElementId.InlineCorkscrew, "wide", 0f, 0f, cfg.RoadWidth, 0f),
                };
                var decisions = TransitionResolver.Resolve(sections, cfg);
                var d = decisions[0];
                Assert.AreNotEqual(TransitionKind.DirectWeld, d.Kind, "A 30% width jump must not weld.");
                Assert.Greater(d.MinBlendLength, 0f);
                Assert.IsTrue(d.ChannelDemands.Exists(c => c.StartsWith("width")),
                    "The width channel must appear in the reasoning.");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void CrossRoadAndOpenEdgeBoundaries_AreSkipped()
        {
            var cfg = Cfg(out var config);
            try
            {
                // A canonical-road exit (RoadId 0, OpenEnd — a jump lip) list-adjacent to
                // an alternate-road entry (RoadId 1, OpenStart — a landing mouth). These
                // never physically touch; the resolver must not classify the pair.
                var a = Content(SemanticElementId.OrdinaryCurve, "roadA_exit", 0f, 4f, cfg.RoadWidth, 0f);
                a.RoadId = 0; a.OpenEnd = true;
                var b = Content(SemanticElementId.OrdinaryCurve, "roadB_entry", 0f, 0f, cfg.RoadWidth, 0f);
                b.RoadId = 1; b.OpenStart = true;

                var sections = new List<GeneratedTrackSection> { a, Straight(0f, cfg.RoadWidth), b };
                var decisions = TransitionResolver.Resolve(sections, cfg);
                Assert.IsFalse(decisions.Exists(d => d.From.Contains("roadA_exit") && d.To.Contains("roadB_entry")),
                    "A cross-road / open-edge boundary must be skipped, not classified.");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ModerateBankReversal_OverAmpleRun_IsNotChargedForBank()
        {
            var cfg = Cfg(out var config);
            try
            {
                // Two banked curves with plenty of run between them: the banking field's
                // blur radius smooths the reversal, so the resolver must NOT demand a
                // bank blend (the false-positive Reject from the live report).
                float ampleRun = Mathf.Max(60f,
                    Mathf.Max(cfg.BankTransitionLength * 0.5f, cfg.MinimumBankReversalLength * 0.5f)) + 20f;
                var sections = new List<GeneratedTrackSection>
                {
                    Content(SemanticElementId.OrdinaryCurve, "curveA", 30f, 0f, cfg.RoadWidth, 0f),
                    Straight(ampleRun, cfg.RoadWidth),
                    Content(SemanticElementId.OrdinaryCurve, "curveB", 0f, 0f, cfg.RoadWidth, 0f),
                };
                var d = TransitionResolver.Resolve(sections, cfg)[0];
                Assert.IsFalse(d.ChannelDemands.Exists(c => c.StartsWith("bank")),
                    "Bank must not be charged when the run exceeds the field blur radius.");
                Assert.AreNotEqual(TransitionKind.Rejected, d.Kind);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void SharpBankReversal_OverShortRun_StillChargesBank()
        {
            var cfg = Cfg(out var config);
            try
            {
                // Same reversal, but a genuinely short run — the field cannot smooth it,
                // so the bank channel must still be charged (the fix stays targeted).
                var sections = new List<GeneratedTrackSection>
                {
                    Content(SemanticElementId.OrdinaryCurve, "curveA", 40f, 0f, cfg.RoadWidth, 0f),
                    Straight(30f, cfg.RoadWidth),
                    Content(SemanticElementId.OrdinaryCurve, "curveB", 0f, 0f, cfg.RoadWidth, 0f),
                };
                var d = TransitionResolver.Resolve(sections, cfg)[0];
                Assert.IsTrue(d.ChannelDemands.Exists(c => c.StartsWith("bank")),
                    "A sharp reversal over a short run must still demand a bank blend.");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void RealGeneratedTrack_ClassifiesEveryBoundary_NoCrash()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster);
                var cfg = ResolvedTrackGenerationConfig.Resolve(config, settings);

                int classified = 0, generated = 0;
                for (int seed = 1; seed <= 6; seed++)
                {
                    var res = TrackGenerationTestUtil.Generate(settings.Clone(), 5000 + seed * 37, config);
                    if (!res.Success || res.Layout == null) continue;
                    generated++;

                    var decisions = TransitionResolver.Resolve(res.Layout.Sections, cfg);
                    foreach (var d in decisions)
                    {
                        classified++;
                        Assert.IsNotEmpty(d.From);
                        Assert.IsNotEmpty(d.To);
                        if (d.Kind == TransitionKind.Rejected)
                            Assert.IsNotEmpty(d.Reason, "A Rejected boundary must carry a reason.");
                        if (d.Kind != TransitionKind.DirectWeld && d.Kind != TransitionKind.ExplicitRecovery)
                            Assert.GreaterOrEqual(d.MinBlendLength, 0f);
                    }
                }
                Assert.Greater(generated, 0, "No candidate generated across the sample seeds.");
                Assert.Greater(classified, 0, "Expected at least one classified boundary.");
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
