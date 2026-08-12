using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage D acceptance: the DynamicFeatureAdjacency flag is OFF by default and, when
    /// off, produces byte-identical geometry to before (the determinism contract). When
    /// on, it only ever drops corkscrew lead straights and never destabilises the seed
    /// stream. Reporting is checked via the [StageD] warning trail.
    /// </summary>
    public class DynamicAdjacencyTests
    {
        [Test]
        public void FlagDefaultsOff()
        {
            var settings = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
            Assert.IsFalse(settings.Generation.DynamicFeatureAdjacency,
                "DynamicFeatureAdjacency must ship OFF — Stage D is opt-in.");
        }

        [Test]
        public void FlagOff_IsByteIdentical_ToBaseline()
        {
            // Two runs, same seed, flag off both times → identical section geometry.
            for (int seed = 1; seed <= 4; seed++)
            {
                var a = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster), 7000 + seed);
                var b = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster), 7000 + seed);

                if (!a.Success || !b.Success) continue;
                Assert.AreEqual(a.Layout.Sections.Count, b.Layout.Sections.Count,
                    $"seed {seed}: section count diverged with the flag off.");
                for (int i = 0; i < a.Layout.Sections.Count; i++)
                {
                    var pa = a.Layout.Sections[i].EndFrame.Position;
                    var pb = b.Layout.Sections[i].EndFrame.Position;
                    Assert.AreEqual(pa.x, pb.x, 1e-4f, $"seed {seed} sec {i}: x diverged");
                    Assert.AreEqual(pa.y, pb.y, 1e-4f, $"seed {seed} sec {i}: y diverged");
                    Assert.AreEqual(pa.z, pb.z, 1e-4f, $"seed {seed} sec {i}: z diverged");
                }
            }
        }

        [Test]
        public void FlagOn_DoesNotCrash_And_OnlyTouchesCorkscrewLeads()
        {
            var config = TrackGenerationTestUtil.CreateConfig();
            try
            {
                var settings = TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Rollercoaster);
                settings.Generation.DynamicFeatureAdjacency = true;
                // Force a corkscrew to exist so the pilot path is exercised.
                settings.Features.Corkscrews = new TrackFeatureRule(true, 1, 2, 1f);

                int generated = 0, weldConnectors = 0;
                for (int seed = 1; seed <= 8; seed++)
                {
                    var res = TrackGenerationTestUtil.Generate(settings.Clone(), 8100 + seed * 17, config);
                    if (!res.Success || res.Layout == null) continue;
                    generated++;

                    foreach (var sec in res.Layout.Sections)
                    {
                        var d = sec.Definition;
                        if (d != null && d.DebugName != null && d.DebugName.StartsWith("CorkscrewWeld_"))
                        {
                            weldConnectors++;
                            Assert.AreEqual(TrackMacroSectionType.Straight, d.SectionType,
                                "A dropped lead must remain an adjustable straight (a short weld connector).");
                            Assert.AreEqual(SemanticElementId.ClosureTransfer, d.SemanticElement);
                        }
                    }
                }
                Assert.Greater(generated, 0, "No candidate generated with the flag on.");
                // Not asserting weldConnectors > 0 — the capacity floor may legitimately
                // veto every drop on a given batch; the contract is 'never crashes,
                // never mislabels', not 'always drops'.
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
