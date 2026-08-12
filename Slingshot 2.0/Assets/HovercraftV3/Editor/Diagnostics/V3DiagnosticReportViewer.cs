using System.IO;
using Lunarlight.Hovercraft.V3.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public sealed class V3DiagnosticReportViewer : EditorWindow
    {
        private string path = string.Empty;
        private string contents = "Choose a report.md or report.json file.";
        private Vector2 scroll;

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Report Viewer")]
        public static void Open()
        {
            GetWindow<V3DiagnosticReportViewer>("V3 Report Viewer");
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(path, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("Open…", GUILayout.Width(70f)))
            {
                string selected = EditorUtility.OpenFilePanel(
                    "Open Hovercraft V3 Diagnostic Report", string.Empty, string.Empty);
                if (!string.IsNullOrEmpty(selected) && File.Exists(selected))
                {
                    path = selected;
                    contents = File.ReadAllText(selected);
                }
            }
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.SelectableLabel(contents,
                EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    public static class V3DiagnosticSchemaValidator
    {
        [MenuItem("Tools/Hovercraft V3/Diagnostics/Validate Schema")]
        public static void Validate()
        {
            var registry = new V3DiagnosticChannelRegistry();
            registry.RegisterCanonicalChannels();
            var seen = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.Ordinal);
            for (int i = 0; i < registry.Channels.Count; i++)
            {
                V3DiagnosticChannelDefinition channel = registry.Channels[i];
                if (string.IsNullOrWhiteSpace(channel.id) ||
                    string.IsNullOrWhiteSpace(channel.sourceId) ||
                    channel.schemaVersion != V3DiagnosticSchema.Version ||
                    !seen.Add(channel.id))
                    throw new InvalidDataException(
                        "Invalid diagnostic channel at index " + i + ".");
            }
            Debug.Log("Hovercraft V3 diagnostic schema valid: " +
                registry.Count + " unique versioned channels.");
        }
    }
}
