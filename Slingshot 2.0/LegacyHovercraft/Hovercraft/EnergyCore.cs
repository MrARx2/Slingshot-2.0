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

    [Tooltip("If true, automatic base hover requests are granted before the shared performance budget. This prevents normal hover from collapsing when the player drives/steers/charges.")]
    public bool protectBaseHover = true;

    [Header("Channel Priority / Feel")]
    [Tooltip("When the reactor is overloaded, vectoring/steering receives this priority bias before drive/other performance channels. Keeps steering responsive under power load. 1 = no bias.")]
    [Range(0.1f, 4f)]
    public float vectoringPriorityBias = 1.35f;

    [Header("Overcharge Charging")]
    [Tooltip("Charging a thruster draws power based on the selected thruster group's max output. This is the virtual throttle used for that cost calculation.")]
    [Range(0f, 2f)]
    public float overchargeChargeThrottleEquivalent = 1f;

    [Tooltip("Extra multiplier for overcharge charging cost. 1 = same draw as running the selected thruster group at the charge throttle equivalent.")]
    public float overchargeChargeCostMultiplier = 1f;

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
        }

        // Overcharge charging is a virtual load. It does not fire a thruster yet,
        // but it still pulls reactor output. Its cost is derived from the selected
        // thruster group, so bigger upgrade parts naturally charge more slowly.
        float virtualOverchargeChargeRequest = ComputeOverchargeChargeRequest(bus, overcharge, costPerForce);
        overchargeRequest += virtualOverchargeChargeRequest;

        float nonHoverPerformanceRequest = driveRequest + vectoringRequest + roofRequest + bottomRequest + overchargeRequest + stabilizerRequest + otherRequest;

        float priorityBias = Mathf.Max(0.1f, vectoringPriorityBias);

        // Use a central, reactor-level priority bias instead of per-system cost
        // tuning. Vectoring still consumes power based on its actual thrusters,
        // but when overloaded it loses less authority than drive/optional systems.
        // This keeps steering/carve response alive under power load.
        float effectiveVectoringRequest = vectoringRequest / priorityBias;
        float effectivePerformanceRequest = driveRequest + effectiveVectoringRequest + roofRequest + bottomRequest + overchargeRequest + stabilizerRequest + otherRequest;
        if (!protectBaseHover)
            effectivePerformanceRequest += baseHoverRequest;

        float performanceScale = effectivePerformanceRequest > budget
            ? Mathf.Clamp01(budget / Mathf.Max(0.001f, effectivePerformanceRequest))
            : 1f;

        float vectoringScale = Mathf.Clamp01(performanceScale * priorityBias);
        float baseHoverScale = protectBaseHover ? 1f : performanceScale;

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
                entry.driveThrottle * performanceScale +
                entry.vectoringThrottle * vectoringScale +
                entry.roofThrottle * performanceScale +
                entry.bottomThrottle * performanceScale +
                entry.overchargeThrottle * performanceScale +
                entry.stabilizerThrottle * performanceScale +
                entry.otherThrottle * performanceScale;

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

        float driveGranted = driveRequest * performanceScale;
        float vectoringGranted = vectoringRequest * vectoringScale;
        float roofGranted = roofRequest * performanceScale;
        float bottomGranted = bottomRequest * performanceScale;
        float overchargeGranted = overchargeRequest * performanceScale;
        float stabilizerGranted = stabilizerRequest * performanceScale;
        float baseHoverGranted = baseHoverRequest * baseHoverScale;
        float otherGranted = otherRequest * performanceScale;

        float totalRequested = baseHoverRequest + nonHoverPerformanceRequest;
        float totalGranted = baseHoverGranted + driveGranted + vectoringGranted + roofGranted + bottomGranted + overchargeGranted + stabilizerGranted + otherGranted;

        currentEnergy = new EnergyState
        {
            totalBudget = budget,
            totalRequested = totalRequested,
            totalGranted = totalGranted,
            overload01 = Mathf.Clamp01(effectivePerformanceRequest <= budget ? 0f : (effectivePerformanceRequest - budget) / budget),

            baseHoverRequest = baseHoverRequest,
            baseHoverGranted = baseHoverGranted,
            driveRequest = driveRequest,
            vectoringRequest = vectoringRequest,
            roofRequest = roofRequest,
            bottomRequest = bottomRequest,
            overchargeRequest = overchargeRequest,
            stabilizerRequest = stabilizerRequest,
            otherRequest = otherRequest,

            driveGranted = driveGranted,
            vectoringGranted = vectoringGranted,
            roofGranted = roofGranted,
            bottomGranted = bottomGranted,
            overchargeGranted = overchargeGranted,
            stabilizerGranted = stabilizerGranted,
            otherGranted = otherGranted,

            drivePower01 = GetPower01(driveRequest, driveGranted),
            vectoringPower01 = GetPower01(vectoringRequest, vectoringGranted),
            roofPower01 = GetPower01(roofRequest, roofGranted),
            bottomPower01 = GetPower01(bottomRequest, bottomGranted),
            overchargePower01 = GetPower01(overchargeRequest, overchargeGranted),
            overchargeChargePower01 = virtualOverchargeChargeRequest > 0.001f ? GetPower01(overchargeRequest, overchargeGranted) : 1f,
            overchargeBurstPower01 = GetPower01(overchargeRequest - virtualOverchargeChargeRequest, overchargeGranted - virtualOverchargeChargeRequest * performanceScale),
            stabilizerPower01 = GetPower01(stabilizerRequest, stabilizerGranted),
            baseHoverPower01 = GetPower01(baseHoverRequest, baseHoverGranted),
            otherPower01 = GetPower01(otherRequest, otherGranted),
            baseHoverProtected = protectBaseHover,
            performancePowerScale01 = performanceScale
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

    private float ComputeOverchargeChargeRequest(ThrusterBus bus, OverchargeCore overcharge, float costPerForce)
    {
        if (bus == null || overcharge == null || !overcharge.IsCharging)
            return 0f;

        float throttle = Mathf.Max(0f, overchargeChargeThrottleEquivalent);
        float multiplier = Mathf.Max(0f, overchargeChargeCostMultiplier);
        if (throttle <= 0f || multiplier <= 0f)
            return 0f;

        float sum = 0f;
        switch (overcharge.ChargingTarget)
        {
            case OverchargeTarget.BottomThrusters:
                sum += SumPowerForList(bus.HoverNodes, throttle, costPerForce, bus.masterThrottleMultiplier);
                break;
            case OverchargeTarget.RoofThrusters:
                sum += SumPowerForList(bus.RoofNodes, throttle, costPerForce, bus.masterThrottleMultiplier);
                break;
            case OverchargeTarget.MainThruster:
                sum += SumPowerByRole(bus, ThrusterNode.ThrusterRole.Main, throttle, costPerForce);
                break;
            case OverchargeTarget.BrakeThruster:
                sum += SumPowerByRole(bus, ThrusterNode.ThrusterRole.Brake, throttle, costPerForce);
                break;
        }

        return sum * multiplier;
    }

    private static float SumPowerByRole(ThrusterBus bus, ThrusterNode.ThrusterRole role, float throttle, float costPerForce)
    {
        if (bus == null || bus.thrusters == null)
            return 0f;

        float sum = 0f;
        foreach (var entry in bus.thrusters)
        {
            if (entry == null || entry.node == null || !entry.enabled || entry.node.role != role)
                continue;
            sum += ComputeEntryPower(entry, throttle, costPerForce, bus.masterThrottleMultiplier);
        }
        return sum;
    }

    private static float SumPowerForList(System.Collections.Generic.List<ThrusterBus.ThrusterNodeEntry> entries, float throttle, float costPerForce, float masterMultiplier)
    {
        if (entries == null)
            return 0f;

        float sum = 0f;
        foreach (var entry in entries)
        {
            if (entry == null || entry.node == null || !entry.enabled)
                continue;
            sum += ComputeEntryPower(entry, throttle, costPerForce, masterMultiplier);
        }
        return sum;
    }

    private static float GetPower01(float request, float granted)
    {
        if (request <= 0.001f)
            return 1f;
        return Mathf.Clamp01(granted / request);
    }
}
