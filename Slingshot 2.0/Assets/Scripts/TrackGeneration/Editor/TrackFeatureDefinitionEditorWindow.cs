#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TrackGeneration.Core;
using TrackGeneration.Definitions;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using UnityEditor;
using UnityEngine;

namespace TrackGeneration.Editor
{
    /// <summary>
    /// Designer-facing editor for the source-controlled JSON Feature Definitions.
    /// The JSON remains the runtime authority; this window provides the same safe,
    /// typed Inspector workflow as a ScriptableObject without introducing a second
    /// serialized source of truth.
    /// </summary>
    public sealed class TrackFeatureDefinitionEditorWindow : EditorWindow
    {
        private const string DefinitionFolder = "Assets/Resources/TrackFeatureDefinitions";

        [Serializable]
        private sealed class DefinitionProxy : ScriptableObject
        {
            public TrackFeatureDefinition Definition = new TrackFeatureDefinition();
        }

        private string[] assetPaths = Array.Empty<string>();
        private string[] displayNames = Array.Empty<string>();
        private int selectedIndex = -1;
        private DefinitionProxy proxy;
        private SerializedObject serializedProxy;
        private Vector2 listScroll;
        private Vector2 definitionScroll;
        private string status = "";
        private MessageType statusType = MessageType.Info;
        private bool validateAllQueued;

        [MenuItem("Track/V2/Feature Definitions")]
        public static void Open()
        {
            var window = GetWindow<TrackFeatureDefinitionEditorWindow>();
            window.titleContent = new GUIContent("Feature Definitions");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshAssets();
            if (assetPaths.Length > 0) Select(Mathf.Clamp(selectedIndex, 0, assetPaths.Length - 1));
        }

        private void OnDisable()
        {
            if (proxy != null) DestroyImmediate(proxy);
            proxy = null;
            serializedProxy = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("INDIVIDUAL TRACK FEATURE DEFINITIONS", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(validateAllQueued))
            {
                if (GUILayout.Button(validateAllQueued ? "Validating…" : "Validate All",
                        EditorStyles.toolbarButton, GUILayout.Width(82f)))
                    QueueValidateAll();
            }
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                RefreshAssets(preserveSelection: true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Each entry is one versioned runtime asset. Changing geometry contracts changes " +
                "the Exact Recipe content hash; increment Definition Version when intentionally " +
                "changing an accepted feature.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            DrawDefinitionList();
            DrawSelectedDefinition();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDefinitionList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(235f));
            EditorGUILayout.LabelField($"FEATURES ({assetPaths.Length})", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUI.skin.box);
            for (int i = 0; i < assetPaths.Length; i++)
            {
                GUIStyle style = i == selectedIndex
                    ? new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold }
                    : EditorStyles.miniButton;
                if (GUILayout.Button(displayNames[i], style, GUILayout.Height(24f))) Select(i);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedDefinition()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (serializedProxy == null || selectedIndex < 0 || selectedIndex >= assetPaths.Length)
            {
                EditorGUILayout.HelpBox("No Feature Definition selected.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            serializedProxy.Update();
            SerializedProperty definition = serializedProxy.FindProperty("Definition");
            definitionScroll = EditorGUILayout.BeginScrollView(definitionScroll, GUI.skin.box);
            EditorGUILayout.PropertyField(definition, GUIContent.none, includeChildren: true);
            EditorGUILayout.EndScrollView();
            serializedProxy.ApplyModifiedProperties();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = proxy.Definition != null;
            if (GUILayout.Button("VALIDATE", GUILayout.Height(28f))) ValidateSelected();
            if (GUILayout.Button("SAVE DEFINITION", GUILayout.Height(28f))) SaveSelected();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(status))
                EditorGUILayout.HelpBox(status, statusType);
            EditorGUILayout.EndVertical();
        }

        private void RefreshAssets(bool preserveSelection = false)
        {
            string priorPath = preserveSelection && selectedIndex >= 0 && selectedIndex < assetPaths.Length
                ? assetPaths[selectedIndex]
                : "";
            assetPaths = AssetDatabase.FindAssets("t:TextAsset", new[] { DefinitionFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            displayNames = assetPaths.Select(path =>
                ObjectNames.NicifyVariableName(Path.GetFileNameWithoutExtension(path)
                    .Replace("-v1", ""))).ToArray();

            int next = string.IsNullOrEmpty(priorPath)
                ? (assetPaths.Length > 0 ? 0 : -1)
                : Array.IndexOf(assetPaths, priorPath);
            selectedIndex = -1;
            if (next >= 0) Select(next);
            Repaint();
        }

        private void Select(int index)
        {
            if (index < 0 || index >= assetPaths.Length) return;
            selectedIndex = index;
            status = "";
            definitionScroll = Vector2.zero;

            TrackFeatureDefinition loaded;
            try
            {
                loaded = JsonUtility.FromJson<TrackFeatureDefinition>(
                    File.ReadAllText(assetPaths[index]));
            }
            catch (Exception exception)
            {
                SetStatus($"Could not read definition: {exception.Message}", MessageType.Error);
                return;
            }

            if (proxy != null) DestroyImmediate(proxy);
            proxy = CreateInstance<DefinitionProxy>();
            proxy.hideFlags = HideFlags.HideAndDontSave;
            proxy.Definition = loaded ?? new TrackFeatureDefinition();
            serializedProxy = new SerializedObject(proxy);
        }

        private void ValidateSelected()
        {
            if (proxy?.Definition == null) return;
            if (!proxy.Definition.Validate(out string error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            SetStatus(
                $"Valid · {proxy.Definition.StableId} v{proxy.Definition.DefinitionVersion} · " +
                $"content {proxy.Definition.ComputeContentHash().Substring(0, 12)}…",
                MessageType.Info);
        }

        private void QueueValidateAll()
        {
            if (validateAllQueued) return;
            validateAllQueued = true;
            EditorApplication.delayCall += () =>
            {
                validateAllQueued = false;
                if (this == null) return;

                // Import first so newly added source-controlled JSON files participate
                // in Resources loading and receive their Unity metadata before auditing.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                TrackFeatureDefinitionCatalog.InvalidateCache();
                RefreshAssets(preserveSelection: true);

                bool complete = TrackFeatureDefinitionCatalog.TryValidateDesignerCoverage(
                    out string coverage, out _);
                if (!complete)
                {
                    string errors = TrackFeatureDefinitionCatalog.LoadErrors.Count > 0
                        ? string.Join("\n", TrackFeatureDefinitionCatalog.LoadErrors)
                        : coverage;
                    SetStatus(errors, MessageType.Error);
                    return;
                }

                string configGuid = AssetDatabase.FindAssets("t:TrackConfig").FirstOrDefault();
                string configPath = string.IsNullOrEmpty(configGuid)
                    ? ""
                    : AssetDatabase.GUIDToAssetPath(configGuid);
                TrackConfig config = string.IsNullOrEmpty(configPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<TrackConfig>(configPath);
                if (config == null)
                {
                    SetStatus(
                        "Definition schema coverage is valid, but no TrackConfig asset was found for the geometry smoke gate.",
                        MessageType.Warning);
                    return;
                }

                TrackDesignerSettings settings = TrackStylePresetLibrary.Create(
                    TrackStylePresetLibrary.Balanced);
                ResolvedTrackGenerationConfig resolved =
                    ResolvedTrackGenerationConfig.Resolve(config, settings);
                if (!TrackFeatureDefinitionCompiler.TrySmokeCompileCatalog(
                        resolved, out int compiledCount, out var smokeErrors))
                {
                    SetStatus(string.Join("\n", smokeErrors), MessageType.Error);
                    return;
                }

                SetStatus(
                    $"All {TrackFeatureDefinitionCatalog.All.Count} assets are valid. {coverage}. " +
                    $"Smoke-built {compiledCount} definition-owned solvers with finite stamped geometry.",
                    MessageType.Info);
            };
        }

        private void SaveSelected()
        {
            if (proxy?.Definition == null || selectedIndex < 0 || selectedIndex >= assetPaths.Length)
                return;
            serializedProxy.ApplyModifiedProperties();
            if (!proxy.Definition.Validate(out string error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            string path = assetPaths[selectedIndex];
            File.WriteAllText(path, JsonUtility.ToJson(proxy.Definition, prettyPrint: true));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TrackFeatureDefinitionCatalog.InvalidateCache();
            SetStatus(
                $"Saved {proxy.Definition.DisplayName}. Exact Recipe identity now uses " +
                $"{proxy.Definition.StableId} v{proxy.Definition.DefinitionVersion}.",
                MessageType.Info);
            RefreshAssets(preserveSelection: true);
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message ?? "";
            statusType = type;
            Repaint();
        }
    }
}
#endif
