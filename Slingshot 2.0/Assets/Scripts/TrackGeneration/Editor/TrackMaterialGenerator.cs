#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TrackGeneration.Editor
{
    public class TrackMaterialGenerator
    {
        [MenuItem("Track/Generate Default Materials")]
        public static void GenerateMaterials()
        {
            TrackMaterialSet set = GenerateOrLoadMaterialSet();
            Selection.activeObject = set;
            EditorGUIUtility.PingObject(set);
            Debug.Log("[TrackMaterialGenerator] Persistent material set generated in Assets/Materials/TrackGeneration");
        }

        /// <summary>
        /// Creates or repairs the persistent material palette and returns the asset so
        /// an inspector can assign it immediately. Existing material assets are never
        /// restyled here: their authored colors, shader settings and textures are preserved.
        /// This never creates scene-only materials.
        /// </summary>
        public static TrackMaterialSet GenerateOrLoadMaterialSet()
        {
            string folderPath = "Assets/Materials/TrackGeneration";
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets/Materials", "TrackGeneration");

            Material road = CreateMaterial($"{folderPath}/MainRoadMat.mat", new Color(0.2f, 0.2f, 0.2f));
            CreateMaterial($"{folderPath}/ShortcutRoadMat.mat", new Color(0.4f, 0.2f, 0.2f));
            Material innerWall = CreateMaterial($"{folderPath}/InnerWallMat.mat", new Color(0.14f, 0.32f, 0.42f));
            Material wall = CreateMaterial($"{folderPath}/WallMat.mat", new Color(0.1f, 0.5f, 0.8f));
            Material guide = CreateMaterial($"{folderPath}/GuideMarkingMat.mat",
                new Color(0.62f, 0.98f, 1f), new Color(0.25f, 0.95f, 1f) * 2.2f);
            Material wallMarker = CreateMaterial($"{folderPath}/WallMarkerMat.mat",
                new Color(0.55f, 0.92f, 1f), new Color(0.25f, 0.8f, 1f) * 1.7f);
            Material boost = CreateMaterial($"{folderPath}/BoostSurfaceMat.mat",
                new Color(0.08f, 0.7f, 1f), new Color(0f, 0.75f, 1f) * 2.8f);
            Material raceGate = CreateMaterial($"{folderPath}/RaceGateMat.mat",
                new Color(0.16f, 0.82f, 1f), new Color(0.1f, 0.85f, 1f) * 2f);
            Material startFinish = CreateMaterial($"{folderPath}/StartFinishMat.mat",
                new Color(0.72f, 0.98f, 1f), new Color(0.35f, 1f, 1f) * 2.6f);
            Material startPillar = CreateMaterial($"{folderPath}/StartGatePillarMat.mat",
                new Color(0.08f, 0.2f, 0.24f), new Color(0.04f, 0.45f, 0.55f) * 0.8f);
            Material checkpoint = CreateMaterial($"{folderPath}/CheckpointMat.mat",
                new Color(0.38f, 0.9f, 1f), new Color(0.12f, 0.85f, 1f) * 2.2f);

            string setPath = $"{folderPath}/TrackMaterialSet.asset";
            TrackMaterialSet set = AssetDatabase.LoadAssetAtPath<TrackMaterialSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<TrackMaterialSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }

            // Repair only missing role links. A designer may intentionally point a role at
            // a custom material, so an Inspector repair action must never replace that choice.
            if (set.RoadSurface == null) set.RoadSurface = road;
            if (set.InnerWallSurface == null) set.InnerWallSurface = innerWall;
            if (set.WallSide == null) set.WallSide = wall;
            if (set.GuideMarking == null) set.GuideMarking = guide;
            if (set.WallMarker == null) set.WallMarker = wallMarker;
            if (set.BoostSurface == null) set.BoostSurface = boost;
            if (set.RaceGate == null) set.RaceGate = raceGate;
            if (set.StartFinish == null) set.StartFinish = startFinish;
            if (set.StartGatePillar == null) set.StartGatePillar = startPillar;
            if (set.Checkpoint == null) set.Checkpoint = checkpoint;
            EditorUtility.SetDirty(set);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return set;
        }

        private static Material CreateMaterial(string path, Color color, Color? emission = null)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);

            mat.color = color;
            if (emission.HasValue && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
