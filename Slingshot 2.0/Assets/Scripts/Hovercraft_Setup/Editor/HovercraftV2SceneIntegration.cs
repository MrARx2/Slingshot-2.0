using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot integration of the restored V2 hovercraft: swaps the craft in
/// SampleScene for an instance of Assets/Prefabs/HovercraftRootV2.prefab and
/// wires the camera and HUDs. Auto-runs once after compilation when the loaded
/// SampleScene has no V2 prefab instance yet; also available from the menu.
/// </summary>
[InitializeOnLoad]
public static class HovercraftV2SceneIntegration
{
    public const string PrefabPath = "Assets/Prefabs/HovercraftRootV2.prefab";
    public const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

    static HovercraftV2SceneIntegration()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != SampleScenePath) return;
            if (FindV2Instance(active) != null) return;
            try
            {
                Integrate();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        };
    }

    [MenuItem("Tools/Hovercraft/Integrate V2 Hovercraft Into Sample Scene")]
    public static void Integrate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != SampleScenePath)
            scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new System.InvalidOperationException($"Missing prefab: {PrefabPath}");

        // Remove whatever craft is currently in the scene (the deleted
        // LegacyHovercraft instance shows up as a broken prefab root).
        Vector3 spawnPosition = new Vector3(0f, 3f, 0f);
        Quaternion spawnRotation = Quaternion.identity;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            bool isCraft = root.GetComponentInChildren<CraftCore>(true) != null
                || root.name.StartsWith("HovercraftRoot")
                || root.name.StartsWith("LegacyHovercraft");
            if (!isCraft) continue;
            spawnPosition = root.transform.position;
            spawnRotation = root.transform.rotation;
            Object.DestroyImmediate(root);
        }

        // Strip any component whose script no longer exists.
        int removedComponents = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    removedComponents += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

        var craft = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        craft.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        CraftCore core = craft.GetComponent<CraftCore>();
        Rigidbody rb = craft.GetComponent<Rigidbody>();
        var hovercraftCamera = Object.FindAnyObjectByType<HovercraftCamera>(FindObjectsInactive.Include);
        if (hovercraftCamera != null)
        {
            hovercraftCamera.target = craft.transform;
            hovercraftCamera.targetRigidbody = rb;
            hovercraftCamera.craftCore = core;
            var sensor = hovercraftCamera.GetComponent<TrackSectionSensor>();
            if (sensor != null) sensor.target = craft.transform;
        }
        foreach (CraftHUD hud in Object.FindObjectsByType<CraftHUD>(FindObjectsInactive.Include))
            hud.craftCore = core;
        foreach (CraftDebugHUD hud in Object.FindObjectsByType<CraftDebugHUD>(FindObjectsInactive.Include))
            hud.craftCore = core;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[HovercraftV2] Integrated {PrefabPath} into {SampleScenePath} (removed {removedComponents} dead component(s)).");
    }

    private static GameObject FindV2Instance(Scene scene)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (PrefabUtility.GetCorrespondingObjectFromSource(root) == prefab
                || (PrefabUtility.IsPartOfPrefabInstance(root)
                    && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == PrefabPath))
                return root;
        }
        return null;
    }
}
