using NUnit.Framework;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Stage 8 guards: guide markings are non-colliding, spaced visually (never one
    /// per geometric ring), and the center guide follows the resolved flat ratio.
    /// </summary>
    public class GuideMarkingTests
    {
        [Test]
        public void MarkingsAreNonCollidingAndVisuallySpaced()
        {
            TrackGenerationResult result = null;
            for (int s = 9800; s < 9810 && (result == null || !result.Success); s++)
                result = TrackGenerationTestUtil.Generate(
                    TrackGenerationTestUtil.FastSettings(TrackStylePresetLibrary.Balanced), s);
            Assert.IsTrue(result is { Success: true });

            var profile = new TrackRoadProfileSettings();
            var visual = new TrackVisualSettings { WallMarkerSpacingMeters = 60f };
            var root = new GameObject("MarkingTestRoot");
            try
            {
                TrackGuideMarkingBuilder.Build(result.Layout.Sections, profile, visual, root.transform);

                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.Greater(renderers.Length, 0, "No marking meshes were built.");

                // NEVER raised collision geometry.
                Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length,
                    "Guide markings created colliders.");

                // Marker density is visual: with 60 m spacing a lap of L meters carries
                // roughly L/60 bands — assert we are far below one band per ring.
                int totalRings = result.Layout.Metrics.TotalRings;
                // Markers run along EVERY rideable road: the lap metric counts the
                // canonical road only, so add each dual quarter's alternate road.
                float lap = result.Layout.Metrics.LapLengthMeters;
                foreach (var q in result.Layout.Quarters)
                    if (q.RouteB != null) lap += q.RouteB.PhysicalLengthMeters;
                int expectedBands = Mathf.CeilToInt(lap / visual.WallMarkerSpacingMeters) + 8;

                int markerMeshVerts = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.gameObject.name.StartsWith("WallMarkers")) markerMeshVerts += mf.sharedMesh.vertexCount;

                int pointsPerBand = TrackCrossSection.PointCount(profile) * 2 + 8;
                Assert.Less(markerMeshVerts, expectedBands * pointsPerBand,
                    "Wall markers are denser than the visual spacing allows.");
                Assert.Less(expectedBands, totalRings, "sanity: bands must be far fewer than rings");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CenterGuidesFollowTheFlatBoundary()
        {
            var profile = new TrackRoadProfileSettings();
            var visual = new TrackVisualSettings();

            // Two synthetic straight sections: one flat, one fully rounded.
            GeneratedTrackSection MakeSection(float rounding)
            {
                var frames = new TrackConnectionFrame[8];
                for (int i = 0; i < frames.Length; i++)
                {
                    frames[i] = TrackConnectionFrame.Origin(26f);
                    frames[i].Position = new Vector3(0f, 0f, i * 10f);
                    frames[i].ArcLength = i * 10f;
                    frames[i].TurnRounding = rounding;
                }
                return new GeneratedTrackSection
                {
                    Definition = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.Straight,
                        Length = 70f,
                        Width = 26f,
                        DebugName = $"Synthetic_{rounding:F1}"
                    },
                    SubdivisionFrames = frames,
                    StartFrame = frames[0],
                    EndFrame = frames[frames.Length - 1]
                };
            }

            var root = new GameObject("GuideTestRoot");
            try
            {
                var flatList = new System.Collections.Generic.List<GeneratedTrackSection> { MakeSection(0f) };
                TrackGuideMarkingBuilder.Build(flatList, profile, visual, root.transform);

                float expectedX = profile.CenterFlatWidthRatio * 13f; // flat boundary at flat·halfW
                bool foundAtBoundary = false;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.gameObject.name.StartsWith("CenterGuide")) continue;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        if (Mathf.Abs(Mathf.Abs(v.x) - expectedX) < visual.CenterGuideWidth)
                            foundAtBoundary = true;
                    }
                }
                Assert.IsTrue(foundAtBoundary, "Center guide does not sit at the flat-region boundary.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
