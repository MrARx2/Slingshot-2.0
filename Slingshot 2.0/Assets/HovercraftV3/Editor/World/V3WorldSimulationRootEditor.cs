using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    [CustomEditor(typeof(V3WorldSimulationRoot))]
    public sealed class V3WorldSimulationRootEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var root = (V3WorldSimulationRoot)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Validation", EditorStyles.boldLabel);
            V3WorldQueryStatus status = V3WorldQueryService.Resolve(
                root.gameObject.scene,
                out V3WorldSimulationRoot active);
            MessageType messageType = status == V3WorldQueryStatus.Valid
                ? MessageType.Info
                : MessageType.Error;
            string message = status switch
            {
                V3WorldQueryStatus.Valid =>
                    "This is the authoritative World Simulation root for the scene.",
                V3WorldQueryStatus.DuplicateWorld =>
                    "Multiple active World Simulation roots exist in this scene.",
                V3WorldQueryStatus.MissingProfile =>
                    "The active World Simulation root has no profile assigned.",
                _ => "No active World Simulation root exists in this scene."
            };
            EditorGUILayout.HelpBox(message, messageType);
            EditorGUILayout.LabelField(
                "Registered Zones",
                root.ZoneRegistry != null
                    ? root.ZoneRegistry.Count.ToString()
                    : "0");
            if (GUILayout.Button("Refresh Environment Zones"))
            {
                root.ZoneRegistry?.Refresh();
                SceneView.RepaintAll();
            }
            if (active != null && active != root)
            {
                EditorGUILayout.ObjectField(
                    "Resolved Active Root",
                    active,
                    typeof(V3WorldSimulationRoot),
                    true);
            }
        }
    }
}
