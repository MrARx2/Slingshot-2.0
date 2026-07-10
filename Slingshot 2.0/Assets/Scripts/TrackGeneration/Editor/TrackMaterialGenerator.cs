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
            string folderPath = "Assets/Materials/TrackGeneration";
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets/Materials", "TrackGeneration");

            CreateMaterial($"{folderPath}/MainRoadMat.mat", new Color(0.2f, 0.2f, 0.2f));
            CreateMaterial($"{folderPath}/ShortcutRoadMat.mat", new Color(0.4f, 0.2f, 0.2f));
            CreateMaterial($"{folderPath}/WallMat.mat", new Color(0.1f, 0.5f, 0.8f));
            
            AssetDatabase.SaveAssets();
            Debug.Log("[TrackMaterialGenerator] Default materials generated in " + folderPath);
        }

        private static void CreateMaterial(string path, Color color)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = color;
                AssetDatabase.CreateAsset(mat, path);
            }
        }
    }
}
#endif
