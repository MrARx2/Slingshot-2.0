using UnityEngine;

/// <summary>
/// Optional cleanup helper for old prototype HUD scripts.
/// <para>
/// Attach this to the hovercraft root or a scene manager object if older HUD
/// components are still present in the scene. It disables the previous prototype
/// HUD overlays by class name without requiring compile-time references to them.
/// </para>
/// <para>
/// This does not disable CraftHUD or CraftDebugHUD.
/// </para>
/// </summary>
public class LegacyHUDDisabler : MonoBehaviour
{
    [Tooltip("Disable old HUD components on Start.")]
    public bool disableOnStart = true;

    [Tooltip("If true, inactive objects are scanned too.")]
    public bool includeInactive = true;

    [Tooltip("Class names of old HUD components to disable.")]
    public string[] legacyHudTypeNames =
    {
        "HovercraftCanvasHUD",
        "HovercraftDebugHUD",
        "HovercraftHUD",
        "ThrusterDebugHUD",
        "ThrusterHUD"
    };

    private void Start()
    {
        if (disableOnStart)
        {
            DisableLegacyHUDs();
        }
    }

    [ContextMenu("Disable Legacy HUDs")]
    public void DisableLegacyHUDs()
    {
#if UNITY_2023_1_OR_NEWER
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(includeInactive);
#endif
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null) continue;

            string typeName = behaviour.GetType().Name;
            if (!IsLegacyHudName(typeName)) continue;

            behaviour.enabled = false;
            Debug.Log($"[LegacyHUDDisabler] Disabled legacy HUD component: {typeName} on {behaviour.gameObject.name}", behaviour);
        }
    }

    private bool IsLegacyHudName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || legacyHudTypeNames == null) return false;

        for (int i = 0; i < legacyHudTypeNames.Length; i++)
        {
            if (typeName == legacyHudTypeNames[i])
            {
                return true;
            }
        }

        return false;
    }
}
