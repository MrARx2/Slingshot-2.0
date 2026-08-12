using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V3CanonicalAssetRegenerator
    {
        [MenuItem(
            "Tools/Hovercraft V3/Regenerate All Canonical V3 Assets")]
        public static void Generate()
        {
            V2ReferencePrototypeGenerator.Generate();
            V3FreeDriveSceneGenerator.Generate();
            V3SystemsIntegrationPrototypeGenerator.Generate();
            V3FreeDriveSceneGenerator.Generate();
            V3ParityHarnessGenerator.Generate();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Hovercraft V3 canonical assets and development scenes " +
                "regenerated successfully.");
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
        }
    }
}
