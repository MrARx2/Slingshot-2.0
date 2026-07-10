#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TrackGeneration.Modification;

namespace TrackGeneration.Editor
{
    [CustomEditor(typeof(TrackModifier))]
    public class TrackModifierEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Select a track node in the Scene View to modify it manually.", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            // Placeholder for scene view editing tools (moving nodes, adding stunts visually)
        }
    }
}
#endif
