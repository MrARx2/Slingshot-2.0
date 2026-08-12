using NUnit.Framework;
using UnityEngine;
using TrackGeneration;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// Correctness of the READ-ONLY mesh-integrity diagnostic's reconstruction premises:
    /// the cross-section is per-frame dependent (so one middle-frame eval is wrong), the
    /// collider profile is genuinely coarser, symmetric roads measure symmetrically, and
    /// each shoulder scans ONLY its own wall (side isolation).
    /// </summary>
    public class MeshIntegrityDiagnosticsTests
    {
        private static TrackRoadProfileSettings RenderProfile(out TrackConfig config)
        {
            config = TrackGenerationTestUtil.CreateConfig();
            return ResolvedTrackGenerationConfig.Resolve(config,
                TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster)).RoadProfile;
        }

        private static TrackConnectionFrame LevelFrame(float width, float turnRounding)
        {
            var f = TrackConnectionFrame.Origin(width);
            f.Width = width;
            f.TurnRounding = turnRounding;
            f.LeftWallSuppression = 0f;  // → wall multiplier 1 (walls present, symmetric)
            f.RightWallSuppression = 0f;
            return f;
        }

        [Test]
        public void PerFrameEvaluation_ChangesVertices()
        {
            var p = RenderProfile(out var config);
            try
            {
                int n = TrackCrossSection.PointCount(p);
                var a = new Vector2[n]; var b = new Vector2[n];
                TrackCrossSection.Evaluate(p, LevelFrame(112f, 0f), a, null);
                TrackCrossSection.Evaluate(p, LevelFrame(80f, 1f), b, null); // different width + turn rounding

                bool anyDiff = false;
                for (int k = 0; k < n; k++)
                    if ((a[k] - b[k]).sqrMagnitude > 1e-4f) { anyDiff = true; break; }
                Assert.IsTrue(anyDiff, "cross-section must depend on per-frame width/rounding — one middle-frame eval is wrong");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void ColliderProfile_IsCoarserThanRender()
        {
            var p = RenderProfile(out var config);
            try
            {
                var col = TrackMeshIntegrityDiagnostics.ColliderProfileFor(p);
                Assert.Less(TrackCrossSection.PointCount(col), TrackCrossSection.PointCount(p),
                    "collider reconstruction must use fewer points than render (the discretisation gap)");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void SymmetricRoad_LeftRightShouldersMatch()
        {
            var p = RenderProfile(out var config);
            try
            {
                var f = LevelFrame(112f, 0f);
                bool okR = TrackMeshIntegrityDiagnostics.MeasureFloorToWall(f, +1, p, out float jR, out float adjR, out _);
                bool okL = TrackMeshIntegrityDiagnostics.MeasureFloorToWall(f, -1, p, out float jL, out float adjL, out _);
                if (!okR || !okL) Assert.Ignore("profile produced no wall shoulder to measure");
                Assert.AreEqual(jR, jL, 1e-3f, "symmetric road: left/right floor→wall step must match");
                Assert.AreEqual(adjR, adjL, 1e-3f, "symmetric road: left/right max adjacent step must match");
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void AsymmetricWalls_EachShoulderScansOnlyItsOwnWall()
        {
            var p = RenderProfile(out var config);
            try
            {
                // Right-side baseline (symmetric multipliers).
                var symmetric = LevelFrame(112f, 0f);
                if (!TrackMeshIntegrityDiagnostics.MeasureFloorToWall(symmetric, +1, p, out float baseR, out _, out _))
                    Assert.Ignore("no shoulder to measure");

                // Now suppress the LEFT wall only. The RIGHT shoulder measurement must be UNCHANGED —
                // proving the right scan never reaches across the centre into the left wall.
                var asym = LevelFrame(112f, 0f);
                asym.LeftWallSuppression = 0.8f;  // → left wall multiplier 0.2 (suppressed)
                asym.RightWallSuppression = 0f;
                TrackMeshIntegrityDiagnostics.MeasureFloorToWall(asym, +1, p, out float asymR, out _, out _);

                Assert.AreEqual(baseR, asymR, 1e-3f,
                    "changing the LEFT wall must not change the RIGHT shoulder measurement (side isolation)");
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
