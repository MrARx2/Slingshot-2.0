using System.IO;
using Lunarlight.Hovercraft.V3.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    [CustomEditor(typeof(V3DiagnosticRecorder))]
    public sealed class V3DiagnosticRecorderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var recorder = (V3DiagnosticRecorder)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying ||
                recorder.IsRecording || recorder.IsExporting))
            {
                if (GUILayout.Button("Start Recording")) recorder.StartRecording();
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying ||
                !recorder.IsRecording || recorder.IsExporting))
            {
                if (GUILayout.Button("Stop Recording")) recorder.StopRecording();
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying ||
                recorder.CurrentSession == null || recorder.IsExporting))
            {
                if (GUILayout.Button("Export Now")) recorder.ExportNow();
            }
            if (GUILayout.Button("Open Output Folder"))
            {
                Directory.CreateDirectory(recorder.OutputRoot);
                EditorUtility.RevealInFinder(recorder.OutputRoot);
            }
            if (recorder.IsExporting)
            {
                EditorGUILayout.LabelField("Export", recorder.ExportStage);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(),
                    recorder.ExportProgress,
                    (recorder.ExportProgress * 100f).ToString("0") + "%");
                Repaint();
            }
            EditorGUILayout.HelpBox(
                "F7: start/stop  •  F8: manual marker  •  F9: export. " +
                "The recorder is a passive observer and never submits commands or forces.",
                MessageType.Info);
        }
    }

    public sealed class V3DiagnosticRecorderWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Recorder Window")]
        public static void Open()
        {
            GetWindow<V3DiagnosticRecorderWindow>("V3 Diagnostics");
        }

        private void OnGUI()
        {
            V3DiagnosticRecorder recorder =
                UnityEngine.Object.FindAnyObjectByType<V3DiagnosticRecorder>(
                    FindObjectsInactive.Include);
            EditorGUILayout.LabelField("Forensic Dynamics Recorder",
                EditorStyles.boldLabel);
            if (recorder == null)
            {
                EditorGUILayout.HelpBox(
                    "The current scene has no diagnostic recorder.",
                    MessageType.Warning);
                if (GUILayout.Button("Install Recorder In Current Scene"))
                    V3DiagnosticSceneInstaller.InstallInCurrentScene();
                return;
            }
            EditorGUILayout.ObjectField("Recorder", recorder,
                typeof(V3DiagnosticRecorder), true);
            V3DiagnosticSession session = recorder.CurrentSession;
            EditorGUILayout.LabelField("Status",
                recorder.IsRecording ? "Recording" :
                recorder.IsExporting ? "Exporting" :
                session != null ? "Stopped" : "Idle");
            if (recorder.IsExporting)
            {
                EditorGUILayout.LabelField("Export Stage", recorder.ExportStage);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(),
                    recorder.ExportProgress,
                    (recorder.ExportProgress * 100f).ToString("0") + "%");
                Repaint();
            }
            if (session != null)
            {
                EditorGUILayout.LabelField("Session", session.Manifest.sessionId);
                EditorGUILayout.LabelField("Samples", session.SampleCount.ToString());
                EditorGUILayout.LabelField("Dropped",
                    session.Manifest.droppedSampleCount.ToString());
                EditorGUILayout.LabelField("Events", session.EventCount.ToString());
                scroll = EditorGUILayout.BeginScrollView(scroll,
                    GUILayout.MinHeight(120f));
                for (int i = 0; i < session.DataWarnings.Count; i++)
                    EditorGUILayout.HelpBox(session.DataWarnings[i], MessageType.Warning);
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!Application.isPlaying ||
                recorder.IsExporting))
            {
                if (GUILayout.Button(recorder.IsRecording ? "Stop" : "Start"))
                {
                    if (recorder.IsRecording) recorder.StopRecording();
                    else recorder.StartRecording();
                }
                if (GUILayout.Button("Marker")) recorder.MarkManualEvent();
                if (GUILayout.Button("Export")) recorder.ExportNow();
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Select Recorder"))
                Selection.activeGameObject = recorder.gameObject;
        }
    }
}
