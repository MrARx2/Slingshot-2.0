using System;
using System.Collections.Generic;
using System.Linq;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using UnityEngine;

namespace TrackGeneration.Definitions
{
    [Serializable]
    public sealed class TrackFeatureDefinitionIdentity
    {
        public string StableId = "";
        public int DefinitionVersion;
        public string ContentHash = "";

        public void Normalize()
        {
            StableId = (StableId ?? "").Trim();
            DefinitionVersion = Mathf.Max(1, DefinitionVersion);
            ContentHash = (ContentHash ?? "").Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Deterministic runtime catalog for source-controlled feature-definition JSON files.
    /// A malformed or duplicate definition is rejected instead of silently falling back
    /// to generator constants.
    /// </summary>
    public static class TrackFeatureDefinitionCatalog
    {
        public const string ResourcesPath = "TrackFeatureDefinitions";

        private static List<TrackFeatureDefinition> definitions;
        private static Dictionary<string, TrackFeatureDefinition> byId;
        private static Dictionary<TrackPatternType, TrackFeatureDefinition> byPattern;
        private static Dictionary<SemanticElementId, TrackFeatureDefinition> bySemantic;
        private static List<string> loadErrors;

        public static IReadOnlyList<TrackFeatureDefinition> All
        {
            get
            {
                EnsureLoaded();
                return definitions;
            }
        }

        public static IReadOnlyList<string> LoadErrors
        {
            get
            {
                EnsureLoaded();
                return loadErrors;
            }
        }

        public static bool TryGet(string stableId, out TrackFeatureDefinition definition)
        {
            EnsureLoaded();
            return byId.TryGetValue((stableId ?? "").Trim(), out definition);
        }

        public static bool TryGet(TrackPatternType patternType, out TrackFeatureDefinition definition)
        {
            EnsureLoaded();
            return byPattern.TryGetValue(patternType, out definition);
        }

        public static bool TryGet(SemanticElementId semanticElement, out TrackFeatureDefinition definition)
        {
            EnsureLoaded();
            return bySemantic.TryGetValue(semanticElement, out definition);
        }

        /// <summary>
        /// Verifies the designer contract that every serialized pattern enum owns one
        /// individual, enabled Feature Definition. This is intentionally diagnostic:
        /// legacy geometry remains buildable while the Inspector reports the exact gap.
        /// </summary>
        public static bool TryValidateDesignerCoverage(out string summary,
            out List<TrackPatternType> missingPatterns)
        {
            EnsureLoaded();
            missingPatterns = new List<TrackPatternType>();
            foreach (TrackPatternType pattern in Enum.GetValues(typeof(TrackPatternType)))
            {
                if (!byPattern.TryGetValue(pattern, out TrackFeatureDefinition definition) ||
                    definition == null || !definition.Enabled)
                    missingPatterns.Add(pattern);
            }

            int total = Enum.GetValues(typeof(TrackPatternType)).Length;
            int ready = total - missingPatterns.Count;
            if (loadErrors.Count > 0)
            {
                summary = $"{ready}/{total} ready · {loadErrors.Count} invalid definition" +
                          (loadErrors.Count == 1 ? "" : "s");
                return false;
            }
            if (missingPatterns.Count > 0)
            {
                summary = $"{ready}/{total} ready · missing {string.Join(", ", missingPatterns)}";
                return false;
            }

            summary = $"{ready}/{total} designer features have individual definition assets";
            return true;
        }

        public static List<TrackFeatureDefinitionIdentity> CaptureIdentities()
        {
            EnsureLoaded();
            return definitions
                .Select(definition => new TrackFeatureDefinitionIdentity
                {
                    StableId = definition.StableId,
                    DefinitionVersion = definition.DefinitionVersion,
                    ContentHash = definition.ComputeContentHash()
                })
                .OrderBy(identity => identity.StableId, StringComparer.Ordinal)
                .ToList();
        }

        public static void InvalidateCache()
        {
            definitions = null;
            byId = null;
            byPattern = null;
            bySemantic = null;
            loadErrors = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntimeSession() => InvalidateCache();

        private static void EnsureLoaded()
        {
            if (definitions != null) return;

            definitions = new List<TrackFeatureDefinition>();
            byId = new Dictionary<string, TrackFeatureDefinition>(StringComparer.Ordinal);
            byPattern = new Dictionary<TrackPatternType, TrackFeatureDefinition>();
            bySemantic = new Dictionary<SemanticElementId, TrackFeatureDefinition>();
            loadErrors = new List<string>();

            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourcesPath);
            Array.Sort(assets, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (TextAsset asset in assets)
            {
                TrackFeatureDefinition definition;
                try
                {
                    definition = JsonUtility.FromJson<TrackFeatureDefinition>(asset.text);
                }
                catch (Exception exception)
                {
                    loadErrors.Add($"Feature definition '{asset.name}' is not readable JSON: {exception.Message}");
                    continue;
                }

                if (definition == null)
                {
                    loadErrors.Add($"Feature definition '{asset.name}' is empty.");
                    continue;
                }
                if (!definition.Validate(out string validationError))
                {
                    loadErrors.Add($"Feature definition '{asset.name}' is invalid: {validationError}");
                    continue;
                }
                if (byId.ContainsKey(definition.StableId))
                {
                    loadErrors.Add($"Duplicate feature definition ID '{definition.StableId}'.");
                    continue;
                }
                if (definition.HasPatternType && byPattern.ContainsKey(definition.PatternType))
                {
                    loadErrors.Add($"Pattern '{definition.PatternType}' has more than one active feature definition.");
                    continue;
                }
                if (bySemantic.ContainsKey(definition.SemanticElement))
                {
                    loadErrors.Add($"Semantic element '{definition.SemanticElement}' has more than one active feature definition.");
                    continue;
                }

                definitions.Add(definition);
                byId.Add(definition.StableId, definition);
                if (definition.HasPatternType) byPattern.Add(definition.PatternType, definition);
                bySemantic.Add(definition.SemanticElement, definition);
            }

            definitions.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
        }
    }
}
