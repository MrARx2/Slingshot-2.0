#if UNITY_EDITOR
using System;
using TrackGeneration.Definitions;
using UnityEditor;

namespace TrackGeneration.Editor
{
    /// <summary>Refreshes parsed definition data after a JSON definition is reimported.</summary>
    internal sealed class TrackFeatureDefinitionAssetWatcher : AssetPostprocessor
    {
        private const string DefinitionFolder = "Assets/Resources/TrackFeatureDefinitions/";

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsDefinition(importedAssets) || ContainsDefinition(deletedAssets) ||
                ContainsDefinition(movedAssets) || ContainsDefinition(movedFromAssetPaths))
            {
                TrackFeatureDefinitionCatalog.InvalidateCache();
            }
        }

        private static bool ContainsDefinition(string[] paths)
            => paths != null && Array.Exists(paths, path =>
                path != null && path.StartsWith(DefinitionFolder, StringComparison.Ordinal));
    }
}
#endif
