using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Low-level thruster router for the V2 hovercraft.
/// <para>
/// Higher-level systems submit desired raw throttle values here. ThrusterBus
/// collects those requests by power channel, asks EnergyCore to resolve the
/// craft's available reactor power, then applies the final granted throttle to
/// each ThrusterNode in one pass.
/// </para>
/// <para>
/// The bus does not decide steering, hover, grip, or gameplay. It only routes
/// thruster requests and exposes debug telemetry.
/// </para>
/// </summary>
public class ThrusterBus : MonoBehaviour
{
    [Header("Global Overrides")]
    [Tooltip("Master throttle multiplier applied to ALL thrusters after EnergyCore grants power.")]
    [Range(0f, 2f)]
    public float masterThrottleMultiplier = 1f;

    [Tooltip("If true, automatically find all ThrusterNode children on Awake.")]
    public bool autoDiscover = true;

    [Header("Per-Thruster Settings")]
    [Tooltip("Individual thruster entries. Auto-populated if autoDiscover is true.")]
    public List<ThrusterNodeEntry> thrusters = new List<ThrusterNodeEntry>();

    [System.Serializable]
    public class ThrusterNodeEntry
    {
        [Tooltip("Reference to the ThrusterNode component.")]
        public ThrusterNode node;

        [Tooltip("Custom label for display/debugging.")]
        public string label;

        [Tooltip("Enable/disable this thruster.")]
        public bool enabled = true;

        [Tooltip("Per-thruster force multiplier stacked with masterThrottleMultiplier.")]
        [Range(0f, 3f)]
        public float forceMultiplier = 1f;

        [HideInInspector] public float baseHoverThrottle;
        [HideInInspector] public float driveThrottle;
        [HideInInspector] public float vectoringThrottle;
        [HideInInspector] public float roofThrottle;
        [HideInInspector] public float bottomThrottle;
        [HideInInspector] public float overchargeThrottle;
        [HideInInspector] public float stabilizerThrottle;
        [HideInInspector] public float otherThrottle;

        [HideInInspector] public float lastThrottle;
        [HideInInspector] public float lastRequestedThrottle;
        [HideInInspector] public float lastFinalThrottle;
        [HideInInspector] public float lastAppliedForce;
        [HideInInspector] public float lastRequestedPower;
        [HideInInspector] public float lastGrantedPower;
        [HideInInspector] public float lastPowerScale = 1f;

        public float TotalRequestedThrottle =>
            baseHoverThrottle + driveThrottle + vectoringThrottle + roofThrottle +
            bottomThrottle + overchargeThrottle + stabilizerThrottle + otherThrottle;

        public void ClearFrame()
        {
            baseHoverThrottle = 0f;
            driveThrottle = 0f;
            vectoringThrottle = 0f;
            roofThrottle = 0f;
            bottomThrottle = 0f;
            overchargeThrottle = 0f;
            stabilizerThrottle = 0f;
            otherThrottle = 0f;

            lastThrottle = 0f;
            lastRequestedThrottle = 0f;
            lastFinalThrottle = 0f;
            lastAppliedForce = 0f;
            lastRequestedPower = 0f;
            lastGrantedPower = 0f;
            lastPowerScale = 1f;
        }

        public void SetChannel(ThrusterPowerChannel channel, float value)
        {
            switch (channel)
            {
                case ThrusterPowerChannel.BaseHover: baseHoverThrottle = value; break;
                case ThrusterPowerChannel.Drive: driveThrottle = value; break;
                case ThrusterPowerChannel.Vectoring: vectoringThrottle = value; break;
                case ThrusterPowerChannel.Roof: roofThrottle = value; break;
                case ThrusterPowerChannel.Bottom: bottomThrottle = value; break;
                case ThrusterPowerChannel.Overcharge: overchargeThrottle = value; break;
                case ThrusterPowerChannel.Stabilizer: stabilizerThrottle = value; break;
                default: otherThrottle = value; break;
            }
        }

        public void AddChannel(ThrusterPowerChannel channel, float value)
        {
            switch (channel)
            {
                case ThrusterPowerChannel.BaseHover: baseHoverThrottle += value; break;
                case ThrusterPowerChannel.Drive: driveThrottle += value; break;
                case ThrusterPowerChannel.Vectoring: vectoringThrottle += value; break;
                case ThrusterPowerChannel.Roof: roofThrottle += value; break;
                case ThrusterPowerChannel.Bottom: bottomThrottle += value; break;
                case ThrusterPowerChannel.Overcharge: overchargeThrottle += value; break;
                case ThrusterPowerChannel.Stabilizer: stabilizerThrottle += value; break;
                default: otherThrottle += value; break;
            }
        }
    }

    public List<ThrusterNodeEntry> HoverNodes { get; private set; } = new List<ThrusterNodeEntry>();
    public List<ThrusterNodeEntry> PropulsionNodes { get; private set; } = new List<ThrusterNodeEntry>();
    public List<ThrusterNodeEntry> StrafeNodes { get; private set; } = new List<ThrusterNodeEntry>();
    public List<ThrusterNodeEntry> RoofNodes { get; private set; } = new List<ThrusterNodeEntry>();

    public int ThrusterCount => thrusters != null ? thrusters.Count : 0;

    private EnergyState _lastResolvedEnergy = EnergyState.Full;
    public EnergyState LastResolvedEnergy => _lastResolvedEnergy;

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

    [ContextMenu("Discover Thrusters")]
    public void DiscoverThrusters()
    {
        ThrusterNode[] found = GetComponentsInChildren<ThrusterNode>(true);
        Dictionary<ThrusterNode, ThrusterNodeEntry> existing = new Dictionary<ThrusterNode, ThrusterNodeEntry>();

        if (thrusters != null)
        {
            foreach (var entry in thrusters)
            {
                if (entry != null && entry.node != null)
                    existing[entry.node] = entry;
            }
        }

        List<ThrusterNodeEntry> newEntries = new List<ThrusterNodeEntry>(found.Length);
        foreach (ThrusterNode node in found)
        {
            if (node == null) continue;

            if (existing.TryGetValue(node, out ThrusterNodeEntry existingEntry))
            {
                newEntries.Add(existingEntry);
            }
            else
            {
                newEntries.Add(new ThrusterNodeEntry
                {
                    node = node,
                    label = !string.IsNullOrEmpty(node.label) ? node.label : node.gameObject.name,
                    enabled = true,
                    forceMultiplier = 1f,
                    lastPowerScale = 1f
                });
            }
        }

        thrusters = newEntries;
        CategorizeThrusters();
    }

    public void CategorizeThrusters()
    {
        if (HoverNodes == null) HoverNodes = new List<ThrusterNodeEntry>();
        if (PropulsionNodes == null) PropulsionNodes = new List<ThrusterNodeEntry>();
        if (StrafeNodes == null) StrafeNodes = new List<ThrusterNodeEntry>();
        if (RoofNodes == null) RoofNodes = new List<ThrusterNodeEntry>();

        HoverNodes.Clear();
        PropulsionNodes.Clear();
        StrafeNodes.Clear();
        RoofNodes.Clear();

        if (thrusters == null) return;

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;

            switch (entry.node.role)
            {
                case ThrusterNode.ThrusterRole.Hover:
                    HoverNodes.Add(entry);
                    break;
                case ThrusterNode.ThrusterRole.Main:
                case ThrusterNode.ThrusterRole.Brake:
                    PropulsionNodes.Add(entry);
                    break;
                case ThrusterNode.ThrusterRole.Strafe:
                    StrafeNodes.Add(entry);
                    break;
                case ThrusterNode.ThrusterRole.Roof:
                    RoofNodes.Add(entry);
                    break;
            }
        }
    }

    public void BeginFrame()
    {
        if (thrusters == null) return;

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;
            entry.node.Throttle = 0f;
            entry.ClearFrame();
        }
    }

    public void SetThrottle(ThrusterNodeEntry entry, float throttle, ThrusterPowerChannel channel = ThrusterPowerChannel.Auto)
    {
        if (entry == null || entry.node == null) return;
        ThrusterPowerChannel resolved = ResolveChannel(entry.node, channel);
        entry.SetChannel(resolved, Mathf.Max(0f, throttle));
        entry.lastRequestedThrottle = entry.TotalRequestedThrottle;
    }

    public void SetThrottle(ThrusterNode node, float throttle, ThrusterPowerChannel channel = ThrusterPowerChannel.Auto)
    {
        ThrusterNodeEntry entry = FindByNode(node);
        if (entry != null)
            SetThrottle(entry, throttle, channel);
    }

    public void SetThrottle(ThrusterNode.ThrusterRole role, string label, float throttle, ThrusterPowerChannel channel = ThrusterPowerChannel.Auto)
    {
        if (thrusters == null) return;
        bool filterByLabel = !string.IsNullOrEmpty(label);

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;
            if (entry.node.role != role) continue;
            if (filterByLabel && !MatchesLabel(entry, label)) continue;

            SetThrottle(entry, throttle, channel);
            if (filterByLabel) return;
        }
    }

    public void AddThrottle(ThrusterNodeEntry entry, float throttleDelta, ThrusterPowerChannel channel = ThrusterPowerChannel.Auto)
    {
        if (entry == null || entry.node == null) return;
        ThrusterPowerChannel resolved = ResolveChannel(entry.node, channel);
        entry.AddChannel(resolved, Mathf.Max(0f, throttleDelta));
        entry.lastRequestedThrottle = entry.TotalRequestedThrottle;
    }

    public void AddThrottle(ThrusterNode node, float throttleDelta, ThrusterPowerChannel channel = ThrusterPowerChannel.Auto)
    {
        ThrusterNodeEntry entry = FindByNode(node);
        if (entry != null)
            AddThrottle(entry, throttleDelta, channel);
    }

    public void AddThrottle(ThrusterNode.ThrusterRole role, string label, float throttleDelta, ThrusterPowerChannel channel = ThrusterPowerChannel.Auto)
    {
        if (thrusters == null) return;
        bool filterByLabel = !string.IsNullOrEmpty(label);

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;
            if (entry.node.role != role) continue;
            if (filterByLabel && !MatchesLabel(entry, label)) continue;

            AddThrottle(entry, throttleDelta, channel);
            if (filterByLabel) return;
        }
    }

    public EnergyState ResolveEnergy(EnergyCore energyCore, OverchargeCore overcharge)
    {
        if (energyCore != null)
        {
            _lastResolvedEnergy = energyCore.ResolvePower(this, overcharge);
        }
        else
        {
            ApplyRequestsWithoutEnergyCore();
            _lastResolvedEnergy = EnergyState.Full;
        }

        return _lastResolvedEnergy;
    }

    private void ApplyRequestsWithoutEnergyCore()
    {
        if (thrusters == null) return;

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;

            if (!entry.enabled)
            {
                entry.node.Throttle = 0f;
                entry.lastFinalThrottle = 0f;
                entry.lastPowerScale = 1f;
                continue;
            }

            float finalThrottle = entry.TotalRequestedThrottle * entry.forceMultiplier * masterThrottleMultiplier;
            entry.lastRequestedThrottle = entry.TotalRequestedThrottle;
            entry.lastFinalThrottle = finalThrottle;
            entry.lastThrottle = finalThrottle;
            entry.lastPowerScale = 1f;
            entry.node.Throttle = finalThrottle;
        }
    }

    public void ApplyAllThrust()
    {
        if (thrusters == null) return;

        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;

            if (!entry.enabled)
            {
                entry.node.Throttle = 0f;
                entry.lastFinalThrottle = 0f;
                entry.lastAppliedForce = 0f;
                continue;
            }

            entry.node.ApplyThrust();
            entry.lastAppliedForce = entry.node.LastAppliedForce.magnitude;
        }
    }

    public void CastAllGroundRays()
    {
        CastGroundRaysForList(HoverNodes);
        CastGroundRaysForList(RoofNodes);
    }

    private void CastGroundRaysForList(List<ThrusterNodeEntry> entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
        {
            if (entry == null || entry.node == null || !entry.enabled) continue;
            entry.node.CastGroundRay();
        }
    }

    public int GetGroundedCount()
    {
        int count = 0;
        foreach (var entry in HoverNodes)
        {
            if (entry != null && entry.node != null && entry.enabled && entry.node.IsGrounded)
                count++;
        }
        return count;
    }

    public Vector3 GetAverageGroundNormal()
    {
        int count = 0;
        Vector3 sum = Vector3.zero;
        foreach (var entry in HoverNodes)
        {
            if (entry == null || entry.node == null || !entry.enabled) continue;
            if (entry.node.IsGrounded)
            {
                sum += entry.node.GroundNormal;
                count++;
            }
        }
        return count > 0 ? (sum / count).normalized : Vector3.up;
    }

    public ThrusterNodeEntry FindByLabel(string entryLabel)
    {
        if (thrusters == null) return null;
        foreach (var entry in thrusters)
        {
            if (entry == null) continue;
            if (MatchesLabel(entry, entryLabel)) return entry;
        }
        return null;
    }

    public ThrusterNodeEntry FindByNode(ThrusterNode node)
    {
        if (node == null || thrusters == null) return null;
        foreach (var entry in thrusters)
        {
            if (entry != null && entry.node == node) return entry;
        }
        return null;
    }

    public void KillAll()
    {
        if (thrusters == null) return;
        foreach (var entry in thrusters)
        {
            if (entry == null || entry.node == null) continue;
            entry.ClearFrame();
            entry.node.Throttle = 0f;
        }
    }

    private static bool MatchesLabel(ThrusterNodeEntry entry, string label)
    {
        if (entry == null || string.IsNullOrEmpty(label)) return false;
        bool entryLabelMatches = string.Equals(entry.label, label, System.StringComparison.Ordinal);
        bool nodeLabelMatches = entry.node != null && string.Equals(entry.node.label, label, System.StringComparison.Ordinal);
        bool objectNameMatches = entry.node != null && string.Equals(entry.node.gameObject.name, label, System.StringComparison.Ordinal);
        return entryLabelMatches || nodeLabelMatches || objectNameMatches;
    }

    private static ThrusterPowerChannel ResolveChannel(ThrusterNode node, ThrusterPowerChannel requested)
    {
        if (requested != ThrusterPowerChannel.Auto)
            return requested;

        if (node == null)
            return ThrusterPowerChannel.Other;

        switch (node.role)
        {
            case ThrusterNode.ThrusterRole.Hover: return ThrusterPowerChannel.BaseHover;
            case ThrusterNode.ThrusterRole.Main:
            case ThrusterNode.ThrusterRole.Brake: return ThrusterPowerChannel.Drive;
            case ThrusterNode.ThrusterRole.Strafe: return ThrusterPowerChannel.Vectoring;
            case ThrusterNode.ThrusterRole.Roof: return ThrusterPowerChannel.Roof;
            default: return ThrusterPowerChannel.Other;
        }
    }
}
