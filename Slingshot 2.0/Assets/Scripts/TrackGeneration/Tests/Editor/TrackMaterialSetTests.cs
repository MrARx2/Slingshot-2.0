using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Tests
{
    [Category("V2FastGate")]
    public sealed class TrackMaterialSetTests
    {
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in _owned)
                if (item != null) Object.DestroyImmediate(item);
            _owned.Clear();
        }

        [Test]
        public void MaterialSet_OverridesLegacyMaterials_ForCanonicalRoles()
        {
            TrackMaterialSet set = Own(ScriptableObject.CreateInstance<TrackMaterialSet>());
            set.RoadSurface = Material("set-road");
            set.InnerWallSurface = Material("set-inner-wall");
            set.WallSide = Material("set-wall");
            set.GuideMarking = Material("set-guide");
            set.WallMarker = Material("set-marker");
            set.BoostSurface = Material("set-boost");
            set.RaceGate = Material("set-gate");
            set.StartFinish = Material("set-start");
            set.StartGatePillar = Material("set-pillar");
            set.Checkpoint = Material("set-checkpoint");

            Material legacyRoad = Material("legacy-road");
            Material legacyWall = Material("legacy-wall");
            Material legacyGuide = Material("legacy-guide");
            ResolvedTrackMaterials resolved = ResolvedTrackMaterials.Resolve(
                set, legacyRoad, legacyWall, legacyGuide, null, null, null);

            Assert.AreSame(set.RoadSurface, resolved.RoadSurface);
            Assert.AreSame(set.InnerWallSurface, resolved.InnerWallSurface);
            Assert.AreSame(set.WallSide, resolved.WallSide);
            Assert.AreSame(set.GuideMarking, resolved.GuideMarking);
            Assert.AreSame(set.WallMarker, resolved.WallMarker);
            Assert.AreSame(set.BoostSurface, resolved.BoostSurface);
            Assert.AreSame(set.RaceGate, resolved.RaceGate);
            Assert.AreSame(set.StartFinish, resolved.StartFinish);
            Assert.AreSame(set.StartGatePillar, resolved.StartGatePillar);
            Assert.AreSame(set.Checkpoint, resolved.Checkpoint);
            Assert.IsEmpty(resolved.GetMissingRequiredRoles(true));
        }

        [Test]
        public void LegacyAssignments_RemainUsable_DuringMaterialSetMigration()
        {
            Material road = Material("legacy-road");
            Material wall = Material("legacy-wall");
            Material guide = Material("legacy-guide");
            Material start = Material("legacy-start");
            Material pillar = Material("legacy-pillar");
            Material checkpoint = Material("legacy-checkpoint");

            ResolvedTrackMaterials resolved = ResolvedTrackMaterials.Resolve(
                null, road, wall, guide, start, pillar, checkpoint);

            Assert.AreSame(road, resolved.RoadSurface);
            Assert.AreSame(road, resolved.InnerWallSurface,
                "Old material sets should render inner walls with the road surface until the new role is assigned.");
            Assert.AreSame(wall, resolved.WallSide);
            Assert.AreSame(guide, resolved.GuideMarking);
            Assert.AreSame(guide, resolved.WallMarker,
                "Legacy guide material should cover wall markers until a dedicated set is assigned.");
            Assert.AreSame(start, resolved.StartFinish);
            Assert.AreSame(pillar, resolved.StartGatePillar);
            Assert.AreSame(checkpoint, resolved.Checkpoint);
            Assert.IsEmpty(resolved.GetMissingRequiredRoles(true));
        }

        [Test]
        public void MissingAssignments_ReportDesignerFacingRoleNames()
        {
            Material road = Material("road-only");
            ResolvedTrackMaterials resolved = ResolvedTrackMaterials.Resolve(
                null, road, null, null, null, null, null);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    nameof(ResolvedTrackMaterials.WallSide),
                    nameof(ResolvedTrackMaterials.GuideMarking),
                    nameof(ResolvedTrackMaterials.WallMarker),
                    nameof(ResolvedTrackMaterials.StartFinish),
                    nameof(ResolvedTrackMaterials.StartGatePillar),
                    nameof(ResolvedTrackMaterials.Checkpoint)
                },
                resolved.GetMissingRequiredRoles(true));
        }

        [Test]
        public void PersistentMaterialSet_SurvivesAssetDatabaseReload()
        {
            const string folder = "Assets/__TrackMaterialSetPersistenceTests";
            const string setPath = folder + "/TrackMaterialSet.asset";
            AssetDatabase.DeleteAsset(folder);
            Assert.IsNotEmpty(AssetDatabase.CreateFolder("Assets", "__TrackMaterialSetPersistenceTests"));

            try
            {
                Shader shader = FindTestShader();
                Material CreateMaterialAsset(string name)
                {
                    var material = new Material(shader) { name = name };
                    AssetDatabase.CreateAsset(material, $"{folder}/{name}.mat");
                    return material;
                }

                TrackMaterialSet set = ScriptableObject.CreateInstance<TrackMaterialSet>();
                set.RoadSurface = CreateMaterialAsset("Road");
                set.InnerWallSurface = CreateMaterialAsset("InnerWall");
                set.WallSide = CreateMaterialAsset("Wall");
                set.GuideMarking = CreateMaterialAsset("Guide");
                set.WallMarker = CreateMaterialAsset("Marker");
                set.BoostSurface = CreateMaterialAsset("Boost");
                set.RaceGate = CreateMaterialAsset("Gate");
                set.StartFinish = CreateMaterialAsset("StartFinish");
                set.StartGatePillar = CreateMaterialAsset("StartPillar");
                set.Checkpoint = CreateMaterialAsset("Checkpoint");
                AssetDatabase.CreateAsset(set, setPath);
                AssetDatabase.SaveAssets();

                AssetDatabase.ImportAsset(setPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                TrackMaterialSet reloaded = AssetDatabase.LoadAssetAtPath<TrackMaterialSet>(setPath);

                Assert.IsNotNull(reloaded);
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.RoadSurface));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.InnerWallSurface));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.WallSide));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.GuideMarking));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.WallMarker));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.BoostSurface));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.RaceGate));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.StartFinish));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.StartGatePillar));
                Assert.IsTrue(EditorUtility.IsPersistent(reloaded.Checkpoint));
                Assert.IsEmpty(ResolvedTrackMaterials.Resolve(reloaded, null, null, null, null, null, null)
                    .GetMissingRequiredRoles(true));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void PrismMesh_UsesThreeSurfaceSlotsAndWorldMetricUvs()
        {
            Material floor = Material("floor");
            Material innerWall = Material("inner-wall");
            Material outerShell = Material("outer-shell");
            var root = Own(new GameObject("mesh-test-root"));
            var definition = new TrackMacroSectionDefinition
            {
                DebugName = "TextureTopologyTest",
                SectionType = TrackMacroSectionType.Straight
            };

            TrackConnectionFrame a = TrackConnectionFrame.Origin(30f);
            a.SideHeight = 8f;
            TrackConnectionFrame b = a;
            b.Position = Vector3.forward * 10f;
            b.ArcLength = 10f;
            b.TurnRounding = 1f;
            var section = new GeneratedTrackSection
            {
                Definition = definition,
                StartFrame = a,
                EndFrame = b,
                SubdivisionFrames = new[] { a, b }
            };

            new BoxPrismTrackMeshBuilder().Build(
                new List<GeneratedTrackSection> { section }, floor, innerWall, outerShell, root.transform);

            Transform lod0 = root.transform.Find("Track_00_00/LOD0");
            Assert.IsNotNull(lod0);
            Mesh mesh = lod0.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer renderer = lod0.GetComponent<MeshRenderer>();
            Assert.AreEqual(3, mesh.subMeshCount);
            Assert.AreSame(floor, renderer.sharedMaterials[0]);
            Assert.AreSame(innerWall, renderer.sharedMaterials[1]);
            Assert.AreSame(outerShell, renderer.sharedMaterials[2]);
            Assert.Greater(mesh.GetIndexCount(0), 0, "Floor band emitted no triangles.");
            Assert.Greater(mesh.GetIndexCount(1), 0, "Inner-wall band emitted no triangles.");
            Assert.Greater(mesh.GetIndexCount(2), 0, "Outer shell emitted no triangles.");

            int pointsPerRing = TrackCrossSection.PointCount(new TrackRoadProfileSettings());
            int vertsPerRing = pointsPerRing * 2 + 4;
            Vector2[] uv = mesh.uv;
            Assert.AreEqual(1f, uv[vertsPerRing].y - uv[0].y, 1e-4f,
                "Ten longitudinal meters should advance V by one tile.");
            Vector3[] vertices = mesh.vertices;
            int center = pointsPerRing / 2;
            float surfaceMeters = Vector3.Distance(vertices[center], vertices[center + 1]);
            float uvMeters = Mathf.Abs(uv[center + 1].x - uv[center].x) * 10f;
            Assert.AreEqual(surfaceMeters, uvMeters, 1e-3f,
                "Cross-track UVs must be measured in surface meters, not vertex index.");
        }

        private T Own<T>(T item) where T : Object
        {
            _owned.Add(item);
            return item;
        }

        private Material Material(string name)
        {
            Shader shader = FindTestShader();
            return Own(new Material(shader) { name = name });
        }

        private static Shader FindTestShader()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader") ??
                            Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            Assert.IsNotNull(shader, "The editor test environment did not expose a usable shader.");
            return shader;
        }
    }
}
