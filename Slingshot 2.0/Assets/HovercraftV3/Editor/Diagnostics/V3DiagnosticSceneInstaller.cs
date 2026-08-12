using System;
using Lunarlight.Hovercraft.V3.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V3DiagnosticSceneInstaller
    {
        public const string HostName = "[Hovercraft V3 Diagnostics]";
        private static readonly string[] DevelopmentScenes =
        {
            "Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity",
            "Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity"
        };

        public static V3DiagnosticRecorder EnsureRecorderInScene()
        {
            V3DiagnosticRecorder recorder =
                UnityEngine.Object.FindAnyObjectByType<V3DiagnosticRecorder>(
                    FindObjectsInactive.Include);
            if (recorder != null) return recorder;
            var host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host,
                "Install Hovercraft V3 Diagnostic Recorder");
            recorder = host.AddComponent<V3DiagnosticRecorder>();
            EditorSceneManager.MarkSceneDirty(host.scene);
            return recorder;
        }

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Install In Current Scene")]
        public static void InstallInCurrentScene()
        {
            V3DiagnosticRecorder recorder = EnsureRecorderInScene();
            Selection.activeGameObject = recorder.gameObject;
        }

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Install In Development Scenes")]
        public static void InstallInDevelopmentScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Exit Play Mode before installing diagnostics in scenes.");
            for (int i = 0; i < DevelopmentScenes.Length; i++)
            {
                string path = DevelopmentScenes[i];
                Scene scene = EditorSceneManager.OpenScene(
                    path, OpenSceneMode.Single);
                EnsureRecorderInScene();
                EditorSceneManager.SaveScene(scene, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Installed Hovercraft V3 diagnostics in " +
                DevelopmentScenes.Length + " development scenes.");
        }
    }
}
