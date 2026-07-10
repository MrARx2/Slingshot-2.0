using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Central thruster management system.
/// Holds references to all thrusters on the craft, provides a single point
/// of control with per-thruster overrides, and exposes data for debug/in-game monitors.
///
/// Add this to the craft root. It auto-discovers all Thruster children on Awake,
/// or you can assign them manually in the inspector.
/// </summary>
public class ThrusterManager : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  CONFIGURATION
    // ══════════════════════════════════════════════════════════════

    [Header("Global Overrides")]
    [Tooltip("Master throttle multiplier applied to ALL thrusters.")]
    [Range(0f, 2f)]
    public float masterThrottleMultiplier = 1f;

    [Tooltip("If true, automatically find all Thruster children on Awake.")]
    public bool autoDiscover = true;

    [Header("Per-Thruster Settings")]
    [Tooltip("Individual thruster entries. Auto-populated if autoDiscover is true.")]
    public ThrusterEntry[] thrusters = new ThrusterEntry[0];

    // ══════════════════════════════════════════════════════════════
    //  THRUSTER ENTRY
    // ══════════════════════════════════════════════════════════════

    [System.Serializable]
    public class ThrusterEntry
    {
        [Tooltip("Reference to the Thruster component.")]
        public Thruster thruster;

        [Tooltip("Custom label for display/debugging.")]
        public string label;

        [Tooltip("Enable/disable this thruster.")]
        public bool enabled = true;

        [Tooltip("Per-thruster force multiplier.")]
        [Range(0f, 3f)]
        public float forceMultiplier = 1f;

        // Runtime debug state.
        // Keep lastThrottle for compatibility with HovercraftDebugHUD.
        [HideInInspector] public float lastThrottle;

        // Cleaner debug values for future HUD work.
        [HideInInspector] public float lastRequestedThrottle;
        [HideInInspector] public float lastFinalThrottle;
        [HideInInspector] public float lastAppliedForce;
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC ACCESSORS
    // ══════════════════════════════════════════════════════════════

    public int ThrusterCount => thrusters != null ? thrusters.Length : 0;

    public List<ThrusterEntry> HoverThrusters { get; private set; } = new List<ThrusterEntry>();
    public List<ThrusterEntry> PropulsionThrusters { get; private set; } = new List<ThrusterEntry>();
    public List<ThrusterEntry> StrafeThrusters { get; private set; } = new List<ThrusterEntry>();

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    void Awake()
    {
        if (autoDiscover)
        {
            DiscoverThrusters();
        }

        CategorizeThrusters();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            CategorizeThrusters();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  DISCOVERY
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Find all Thruster components in children and populate the entries array.
    /// Preserves existing manual overrides if the thruster was already in the list.
    /// </summary>
    [ContextMenu("Discover Thrusters")]
    public void DiscoverThrusters()
    {
        Thruster[] found = GetComponentsInChildren<Thruster>(true);

        Dictionary<Thruster, ThrusterEntry> existing = new Dictionary<Thruster, ThrusterEntry>();

        if (thrusters != null)
        {
            foreach (var entry in thrusters)
            {
                if (entry != null && entry.thruster != null)
                {
                    existing[entry.thruster] = entry;
                }
            }
        }

        ThrusterEntry[] newEntries = new ThrusterEntry[found.Length];

        for (int i = 0; i < found.Length; i++)
        {
            Thruster foundThruster = found[i];

            if (foundThruster == null)
            {
                continue;
            }

            if (existing.TryGetValue(foundThruster, out ThrusterEntry existingEntry))
            {
                newEntries[i] = existingEntry;
            }
            else
            {
                newEntries[i] = new ThrusterEntry
                {
                    thruster = foundThruster,
                    label = foundThruster.gameObject.name,
                    enabled = true,
                    forceMultiplier = 1f,
                    lastThrottle = 0f,
                    lastRequestedThrottle = 0f,
                    lastFinalThrottle = 0f,
                    lastAppliedForce = 0f
                };
            }
        }

        thrusters = newEntries;

        CategorizeThrusters();
    }

    public void CategorizeThrusters()
    {
        if (HoverThrusters == null)
        {
            HoverThrusters = new List<ThrusterEntry>();
        }

        if (PropulsionThrusters == null)
        {
            PropulsionThrusters = new List<ThrusterEntry>();
        }

        if (StrafeThrusters == null)
        {
            StrafeThrusters = new List<ThrusterEntry>();
        }

        HoverThrusters.Clear();
        PropulsionThrusters.Clear();
        StrafeThrusters.Clear();

        if (thrusters == null)
        {
            return;
        }

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.thruster == null)
            {
                continue;
            }

            switch (entry.thruster.type)
            {
                case Thruster.ThrusterType.Hover:
                    HoverThrusters.Add(entry);
                    break;

                case Thruster.ThrusterType.Main:
                case Thruster.ThrusterType.Brake:
                    PropulsionThrusters.Add(entry);
                    break;

                case Thruster.ThrusterType.Strafe:
                    StrafeThrusters.Add(entry);
                    break;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  THRUSTER OPERATIONS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Set a thruster's throttle, applying enabled state, per-thruster multiplier,
    /// and master multiplier.
    /// </summary>
    public void SetThrottle(ThrusterEntry entry, float throttle)
    {
        if (entry == null || entry.thruster == null)
        {
            return;
        }

        // Keep this for your existing HovercraftDebugHUD.
        entry.lastThrottle = throttle;

        // Cleaner debug values.
        entry.lastRequestedThrottle = throttle;

        if (!entry.enabled)
        {
            entry.lastFinalThrottle = 0f;
            entry.thruster.Throttle = 0f;
            return;
        }

        float finalThrottle = throttle * entry.forceMultiplier * masterThrottleMultiplier;

        entry.lastFinalThrottle = finalThrottle;
        entry.thruster.Throttle = finalThrottle;
    }

    /// <summary>
    /// Set throttle by finding the entry for a specific Thruster component.
    /// </summary>
    public void SetThrottle(Thruster thruster, float throttle)
    {
        ThrusterEntry entry = FindByThruster(thruster);

        if (entry != null)
        {
            SetThrottle(entry, throttle);
        }
    }

    /// <summary>
    /// Cast ground rays for all hover thrusters.
    /// Call once per FixedUpdate before setting hover throttles.
    /// </summary>
    public void CastAllGroundRays()
    {
        foreach (var entry in HoverThrusters)
        {
            if (entry == null || entry.thruster == null || !entry.enabled)
            {
                continue;
            }

            entry.thruster.CastGroundRay();
        }
    }

    /// <summary>
    /// Apply thrust for all thrusters.
    /// Call once per FixedUpdate after setting all throttles.
    /// </summary>
    public void ApplyAllThrust()
    {
        if (thrusters == null)
        {
            return;
        }

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.thruster == null)
            {
                continue;
            }

            if (!entry.enabled)
            {
                entry.thruster.Throttle = 0f;
                entry.lastFinalThrottle = 0f;
                entry.lastAppliedForce = 0f;
                continue;
            }

            entry.thruster.ApplyThrust();

            entry.lastAppliedForce = entry.thruster.LastAppliedForce.magnitude;
        }
    }

    /// <summary>
    /// Kill all thrusters immediately.
    /// </summary>
    public void KillAll()
    {
        if (thrusters == null)
        {
            return;
        }

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.thruster == null)
            {
                continue;
            }

            entry.lastThrottle = 0f;
            entry.lastRequestedThrottle = 0f;
            entry.lastFinalThrottle = 0f;
            entry.lastAppliedForce = 0f;

            entry.thruster.Throttle = 0f;
        }
    }

    /// <summary>
    /// Get the number of hover thrusters currently detecting ground.
    /// </summary>
    public int GetGroundedCount()
    {
        int count = 0;

        foreach (var entry in HoverThrusters)
        {
            if (entry == null || entry.thruster == null || !entry.enabled)
            {
                continue;
            }

            if (entry.thruster.IsGrounded)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Get the average ground normal from all grounded hover thrusters.
    /// </summary>
    public Vector3 GetAverageGroundNormal()
    {
        int count = 0;
        Vector3 sum = Vector3.zero;

        foreach (var entry in HoverThrusters)
        {
            if (entry == null || entry.thruster == null || !entry.enabled)
            {
                continue;
            }

            if (entry.thruster.IsGrounded)
            {
                sum += entry.thruster.GroundNormal;
                count++;
            }
        }

        if (count <= 0)
        {
            return Vector3.up;
        }

        return (sum / count).normalized;
    }

    /// <summary>
    /// Find a thruster entry by label.
    /// </summary>
    public ThrusterEntry FindByLabel(string label)
    {
        if (thrusters == null)
        {
            return null;
        }

        foreach (var entry in thrusters)
        {
            if (entry == null)
            {
                continue;
            }

            if (entry.label == label)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Find a thruster entry by Thruster component reference.
    /// </summary>
    public ThrusterEntry FindByThruster(Thruster thruster)
    {
        if (thruster == null || thrusters == null)
        {
            return null;
        }

        foreach (var entry in thrusters)
        {
            if (entry == null)
            {
                continue;
            }

            if (entry.thruster == thruster)
            {
                return entry;
            }
        }

        return null;
    }
}