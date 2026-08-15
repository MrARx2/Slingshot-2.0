using UnityEngine;

/// <summary>
/// Reactor / power-source model for the hovercraft.
/// <para>
/// EnergyCore is no longer a set of scattered per-system multipliers. It is the
/// craft's available power source. Systems submit desired throttle to ThrusterBus;
/// ThrusterBus forwards those requests here; EnergyCore calculates how much
/// power the reactor can grant based on each ThrusterNode's max output and
/// efficiency.
/// </para>
/// <para>
/// In short:
/// <c>ThrusterNode.maxForce</c> = hardware output capability.
/// <c>ThrusterNode.efficiency</c> = how expensive that output is.
/// <c>EnergyCore.totalPowerOutput</c> = how much power the craft can supply this frame.
/// </para>
/// </summary>
public class EnergyCore : MonoBehaviour
{
    [Header("Power Source")]
    [Tooltip("Total reactor output available to non-protected systems each physics tick. This is not fuel; it does not drain over time.")]
    public float totalPowerOutput = 100f;

    [Tooltip("Global conversion from thruster force capability into power draw. Higher values make all thrusters more energy-hungry.")]
    public float powerCostPerForce = 1f;

    [Tooltip("If true, automatic base hover requests are granted before the shared performance budget. This prevents normal hover from collapsing when the player drives, steers or boosts.")]
    public bool protectBaseHover = true;

    [Header("Channel Priority / Feel")]
    [Tooltip("Keep automatic surface-retention/stabilizer demand outside the player performance BUS, like base hover. This prevents loops and wall transitions from starving propulsion.")]
    public bool protectStabilizer = true;

    [Tooltip("Fraction of the shared BUS reserved for steering while overloaded. Unused reserve immediately returns to the other channels.")]
    [Range(0f, 1f)]
    public float steeringReserve01 = 0.28f;

    [Tooltip("Fraction of the shared BUS reserved for Overcharge discharge or regeneration while overloaded. Unused reserve immediately returns to other systems.")]
    [Range(0f, 1f)]
    public float boostReserve01 = 0.62f;

    [Header("Two-Slot BUS (power channels)")]
    [Tooltip("How many control channels the BUS funds at FULL power at once. 2 = two 'directions' at 100%. A third+ active channel is throttled, not denied.")]
    [Range(1f, 4f)] public float maxConcurrentChannels = 2f;

    [Tooltip("Below this control engagement (peak throttle 0..1) a channel is idle and claims no BUS slot.")]
    [Range(0f, 0.25f)] public float channelActivationDeadzone = 0.03f;

    [Tooltip("Overload priority — higher keeps more power when the BUS is over budget. Steer is highest so you never lose control.")]
    public float steerPriority = 4f;
    public float boostPriority = 3f;
    public float drivePriority = 2f;
    public float verticalPriority = 1f;

    [Header("Debug / Read Only")]
    [SerializeField] private EnergyState currentEnergy = EnergyState.Full;

    public EnergyState CurrentEnergy => currentEnergy;

    /// <summary>
    /// Resolve all current thruster requests in the bus, set final granted
    /// throttles on each entry, and publish an EnergyState for HUD/debug use.
    /// </summary>
    public EnergyState ResolvePower(ThrusterBus bus, OverchargeCore overcharge)
    {
        if (bus == null)
        {
            currentEnergy = EnergyState.Full;
            return currentEnergy;
        }

        float budget = Mathf.Max(0.001f, totalPowerOutput);
        float costPerForce = Mathf.Max(0f, powerCostPerForce);

        float baseHoverRequest = 0f;
        float driveRequest = 0f;
        float vectoringRequest = 0f;
        float roofRequest = 0f;
        float bottomRequest = 0f;
        float overchargeRequest = 0f;
        float stabilizerRequest = 0f;
        float otherRequest = 0f;

        // Peak control engagement per channel (0..1-ish) — how hard each direction is
        // being driven this tick. Drives the "slot" demand for the two-slot BUS below.
        float drivePeak = 0f, vectoringPeak = 0f, roofPeak = 0f, bottomPeak = 0f, overchargePeak = 0f;

        // First pass: compute power demand per channel from the actual thruster hardware.
        foreach (var entry in bus.thrusters)
        {
            if (entry == null || entry.node == null || !entry.enabled)
                continue;

            AccumulateChannelPower(entry, entry.baseHoverThrottle, ThrusterPowerChannel.BaseHover, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.driveThrottle, ThrusterPowerChannel.Drive, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.vectoringThrottle, ThrusterPowerChannel.Vectoring, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.roofThrottle, ThrusterPowerChannel.Roof, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.bottomThrottle, ThrusterPowerChannel.Bottom, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.overchargeThrottle, ThrusterPowerChannel.Overcharge, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.stabilizerThrottle, ThrusterPowerChannel.Stabilizer, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);
            AccumulateChannelPower(entry, entry.otherThrottle, ThrusterPowerChannel.Other, costPerForce, bus.masterThrottleMultiplier, ref baseHoverRequest, ref driveRequest, ref vectoringRequest, ref roofRequest, ref bottomRequest, ref overchargeRequest, ref stabilizerRequest, ref otherRequest);

            drivePeak = Mathf.Max(drivePeak, entry.driveThrottle);
            vectoringPeak = Mathf.Max(vectoringPeak, entry.vectoringThrottle);
            roofPeak = Mathf.Max(roofPeak, entry.roofThrottle);
            bottomPeak = Mathf.Max(bottomPeak, entry.bottomThrottle);
            overchargePeak = Mathf.Max(overchargePeak, entry.overchargeThrottle);
        }

        // ── Two-slot BUS allocation ──────────────────────────────────
        // Each active control channel claims a "slot" = budget / maxConcurrent, scaled
        // by how hard it is being driven. TWO full channels = 100% of the BUS. A third+
        // active channel is NOT denied — every active channel is throttled instead,
        // weighted by priority so higher-priority channels lose the least
        // (Steer > Boost > Drive > Vertical). Auto hover and the stabilizer are free and
        // funded outside this budget, so the craft never falls or flips from overload.
        float slot = budget / Mathf.Max(1f, maxConcurrentChannels);
        float dz = Mathf.Clamp01(channelActivationDeadzone);

        float steerEngage = EngageLevel(vectoringPeak, dz);
        float boostEngage = EngageLevel(overchargePeak, dz);
        float driveEngage = EngageLevel(drivePeak, dz);
        float roofEngage = EngageLevel(roofPeak, dz);
        float bottomEngage = EngageLevel(bottomPeak, dz);
        float vertEngage = Mathf.Max(roofEngage, bottomEngage);

        // Channel order: 0 Steer, 1 Boost, 2 Drive, 3 Vertical.
        float[] slotDemand =
        {
            slot * steerEngage,
            slot * boostEngage,
            slot * driveEngage,
            slot * vertEngage
        };
        float[] slotWeight =
        {
            Mathf.Max(0.01f, steerPriority),
            Mathf.Max(0.01f, boostPriority),
            Mathf.Max(0.01f, drivePriority),
            Mathf.Max(0.01f, verticalPriority)
        };
        float[] slotScale = { 1f, 1f, 1f, 1f };
        AllocateBus(budget, slotDemand, slotWeight, slotScale);

        float vectoringScale = slotScale[0];
        float overchargeScale = slotScale[1];
        float driveScale = slotScale[2];
        float roofScale = slotScale[3];
        float bottomScale = slotScale[3];
        float baseHoverScale = 1f;   // auto hover: free, always granted
        float stabilizerScale = 1f;  // auto stabilizer: free, always granted
        float otherScale = 1f;

        // What the two-slot budget actually funds this tick (for the BUS readout).
        float controllableRequest = slotDemand[0] + slotDemand[1] + slotDemand[2] + slotDemand[3];
        float controllableGranted = slotDemand[0] * slotScale[0] + slotDemand[1] * slotScale[1]
                                  + slotDemand[2] * slotScale[2] + slotDemand[3] * slotScale[3];

        // Overcharge regeneration uses only LEFTOVER budget: it never steals a direction's
        // slot and stalls while the BUS is fully committed (a rule the player can learn).
        float rechargeRequest = overcharge != null ? Mathf.Max(0f, overcharge.RequestedRechargePower) : 0f;
        float leftover = Mathf.Max(0f, budget - controllableGranted);
        float rechargeScale = rechargeRequest > 0.001f
            ? Mathf.Clamp01(Mathf.Min(rechargeRequest, leftover) / rechargeRequest)
            : (leftover > 0.001f ? 1f : 0f);

        // Second pass: apply granted scale per channel back onto the actual thruster requests.
        foreach (var entry in bus.thrusters)
        {
            if (entry == null || entry.node == null)
                continue;

            if (!entry.enabled)
            {
                entry.node.Throttle = 0f;
                entry.lastFinalThrottle = 0f;
                entry.lastAppliedForce = 0f;
                continue;
            }

            float grantedRawThrottle =
                entry.baseHoverThrottle * baseHoverScale +
                entry.driveThrottle * driveScale +
                entry.vectoringThrottle * vectoringScale +
                entry.roofThrottle * roofScale +
                entry.bottomThrottle * bottomScale +
                entry.overchargeThrottle * overchargeScale +
                entry.stabilizerThrottle * stabilizerScale +
                entry.otherThrottle * otherScale;

            float finalThrottle = grantedRawThrottle * entry.forceMultiplier * bus.masterThrottleMultiplier;

            entry.lastRequestedThrottle = entry.TotalRequestedThrottle;
            entry.lastFinalThrottle = finalThrottle;
            entry.lastThrottle = finalThrottle;
            entry.node.Throttle = finalThrottle;

            float requestedPower = ComputeEntryPower(entry, entry.TotalRequestedThrottle, costPerForce, bus.masterThrottleMultiplier);
            float grantedPower = ComputeEntryPower(entry, grantedRawThrottle, costPerForce, bus.masterThrottleMultiplier);
            entry.lastRequestedPower = requestedPower;
            entry.lastGrantedPower = grantedPower;
            entry.lastPowerScale = requestedPower > 0.001f ? Mathf.Clamp01(grantedPower / requestedPower) : 1f;
        }

        // Reactor readout in BUS/slot terms so the UI can show each channel and its
        // "THROTTLED xx%" (power01 = granted fraction). Base hover / stabilizer are free.
        // Totals INCLUDE the protected systems so the HUD's (total − protected)/budget
        // math resolves to the controllable two-slot load; performancePowerScale01 is
        // the overall controllable grant fraction, which drives the HUD "THROTTLED xx%".
        float baseHoverGrantedFree = baseHoverRequest;   // free / full
        float stabilizerGrantedFree = stabilizerRequest; // free / full
        float busPerfScale = controllableRequest > 0.001f
            ? Mathf.Clamp01(controllableGranted / controllableRequest)
            : 1f;

        currentEnergy = new EnergyState
        {
            totalBudget = budget,
            totalRequested = controllableRequest + baseHoverRequest + stabilizerRequest,
            totalGranted = controllableGranted + baseHoverGrantedFree + stabilizerGrantedFree,
            overload01 = Mathf.Clamp01(controllableRequest <= budget ? 0f : (controllableRequest - budget) / budget),

            baseHoverRequest = baseHoverRequest,
            baseHoverGranted = baseHoverGrantedFree, // free / full

            driveRequest = slotDemand[2],
            vectoringRequest = slotDemand[0],
            roofRequest = slot * roofEngage,
            bottomRequest = slot * bottomEngage,
            overchargeRequest = slotDemand[1],
            stabilizerRequest = stabilizerRequest,
            otherRequest = otherRequest,

            driveGranted = slotDemand[2] * driveScale,
            vectoringGranted = slotDemand[0] * vectoringScale,
            roofGranted = slot * roofEngage * roofScale,
            bottomGranted = slot * bottomEngage * bottomScale,
            overchargeGranted = slotDemand[1] * overchargeScale,
            stabilizerGranted = stabilizerGrantedFree,
            otherGranted = otherRequest,

            drivePower01 = driveScale,
            vectoringPower01 = vectoringScale,
            roofPower01 = roofScale,
            bottomPower01 = bottomScale,
            overchargePower01 = overchargeScale,
            overchargeChargePower01 = rechargeScale,
            overchargeBurstPower01 = overchargeScale,
            stabilizerPower01 = 1f,
            baseHoverPower01 = 1f,
            otherPower01 = 1f,
            baseHoverProtected = protectBaseHover,
            stabilizerProtected = protectStabilizer,
            performancePowerScale01 = busPerfScale
        };

        return currentEnergy;
    }

    private static void AccumulateChannelPower(ThrusterBus.ThrusterNodeEntry entry,
                                               float rawThrottle,
                                               ThrusterPowerChannel channel,
                                               float costPerForce,
                                               float masterMultiplier,
                                               ref float baseHover,
                                               ref float drive,
                                               ref float vectoring,
                                               ref float roof,
                                               ref float bottom,
                                               ref float overcharge,
                                               ref float stabilizer,
                                               ref float other)
    {
        if (rawThrottle <= 0f || entry == null || entry.node == null)
            return;

        float power = ComputeEntryPower(entry, rawThrottle, costPerForce, masterMultiplier);

        switch (channel)
        {
            case ThrusterPowerChannel.BaseHover: baseHover += power; break;
            case ThrusterPowerChannel.Drive: drive += power; break;
            case ThrusterPowerChannel.Vectoring: vectoring += power; break;
            case ThrusterPowerChannel.Roof: roof += power; break;
            case ThrusterPowerChannel.Bottom: bottom += power; break;
            case ThrusterPowerChannel.Overcharge: overcharge += power; break;
            case ThrusterPowerChannel.Stabilizer: stabilizer += power; break;
            default: other += power; break;
        }
    }

    private static float ComputeEntryPower(ThrusterBus.ThrusterNodeEntry entry,
                                           float rawThrottle,
                                           float costPerForce,
                                           float masterMultiplier)
    {
        if (entry == null || entry.node == null || rawThrottle <= 0f)
            return 0f;

        float finalThrottleIfGranted = rawThrottle * entry.forceMultiplier * masterMultiplier;
        return entry.node.GetPowerDraw(finalThrottleIfGranted, costPerForce);
    }

    private static float GetPower01(float request, float granted)
    {
        if (request <= 0.001f)
            return 1f;
        return Mathf.Clamp01(granted / request);
    }

    /// <summary>Control engagement 0..1 from a channel's peak throttle, gated by a deadzone.</summary>
    private static float EngageLevel(float peakThrottle, float deadzone)
    {
        float e = Mathf.Clamp01(peakThrottle);
        return e <= deadzone ? 0f : e;
    }

    /// <summary>
    /// Priority-weighted soft BUS allocator. If total demand fits the budget, every
    /// channel is granted in full (scale = 1). When it doesn't, the budget is
    /// water-filled by weight so higher-priority channels keep more — nothing is hard
    /// denied, each active channel is simply throttled by a different amount. Writes a
    /// granted fraction [0..1] per channel into <paramref name="scaleOut"/>.
    /// </summary>
    internal static void AllocateBus(float budget, float[] demand, float[] weight, float[] scaleOut)
    {
        int n = demand.Length;

        float sumDemand = 0f;
        for (int i = 0; i < n; i++) sumDemand += Mathf.Max(0f, demand[i]);

        if (sumDemand <= budget || sumDemand <= 1e-6f)
        {
            for (int i = 0; i < n; i++) scaleOut[i] = 1f;
            return;
        }

        float[] granted = new float[n];
        bool[] satisfied = new bool[n];
        float remaining = budget;

        // A few passes redistribute any weight-share a channel couldn't use (because it
        // hit its own demand) to the still-hungry channels, again by weight.
        for (int pass = 0; pass <= n && remaining > 1e-5f; pass++)
        {
            float wSum = 0f;
            for (int i = 0; i < n; i++)
                if (!satisfied[i] && demand[i] > 1e-6f) wSum += Mathf.Max(0.01f, weight[i]);
            if (wSum <= 1e-6f) break;

            float distributed = 0f;
            bool anyNewlySatisfied = false;
            for (int i = 0; i < n; i++)
            {
                if (satisfied[i] || demand[i] <= 1e-6f) continue;
                float share = remaining * Mathf.Max(0.01f, weight[i]) / wSum;
                float give = Mathf.Min(share, demand[i] - granted[i]);
                granted[i] += give;
                distributed += give;
                if (granted[i] >= demand[i] - 1e-6f) { satisfied[i] = true; anyNewlySatisfied = true; }
            }
            remaining -= distributed;
            if (!anyNewlySatisfied) break; // stable split reached
        }

        for (int i = 0; i < n; i++)
            scaleOut[i] = demand[i] > 1e-6f ? Mathf.Clamp01(granted[i] / demand[i]) : 1f;
    }
}
