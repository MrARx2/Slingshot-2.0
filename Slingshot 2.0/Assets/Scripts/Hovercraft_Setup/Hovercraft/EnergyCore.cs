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

        // Discharging Overcharge draws real power through the main thruster above.
        // Regenerating the stored reserve is also a real BUS load, represented as
        // virtual demand because it does not directly drive a ThrusterNode.
        float overchargeBurstRequest = overchargeRequest;
        float virtualOverchargeChargeRequest = overcharge != null
            ? Mathf.Max(0f, overcharge.RequestedRechargePower)
            : 0f;
        overchargeRequest += virtualOverchargeChargeRequest;

        float nonHoverPerformanceRequest = driveRequest + vectoringRequest + roofRequest + bottomRequest + overchargeRequest + stabilizerRequest + otherRequest;
        float sharedRequest = nonHoverPerformanceRequest
                            - (protectStabilizer ? stabilizerRequest : 0f)
                            + (protectBaseHover ? 0f : baseHoverRequest);

        // Strict priority allocator. Unlike the former bias multiplier, this can
        // never grant more shared power than the BUS reports. That makes overload
        // a gameplay rule the player can learn instead of a cosmetic meter.
        float remainingBudget = budget;
        float stabilizerGranted = protectStabilizer ? stabilizerRequest : 0f;

        float steeringReserve = budget * Mathf.Clamp01(steeringReserve01);
        float vectoringGuaranteed = Mathf.Min(vectoringRequest, steeringReserve, remainingBudget);
        remainingBudget -= vectoringGuaranteed;

        float activeBoostReserve = budget * Mathf.Clamp01(boostReserve01);
        float overchargeGuaranteed = Mathf.Min(overchargeRequest, activeBoostReserve, remainingBudget);
        remainingBudget -= overchargeGuaranteed;

        float residualBaseHover = protectBaseHover ? 0f : baseHoverRequest;
        float residualStabilizer = protectStabilizer ? 0f : stabilizerRequest;
        float residualVectoring = Mathf.Max(0f, vectoringRequest - vectoringGuaranteed);
        float residualOvercharge = Mathf.Max(0f, overchargeRequest - overchargeGuaranteed);
        float residualRequest = residualBaseHover + driveRequest + residualVectoring + roofRequest +
                                bottomRequest + residualOvercharge + residualStabilizer + otherRequest;
        float residualScale = residualRequest > remainingBudget
            ? Mathf.Clamp01(remainingBudget / Mathf.Max(0.001f, residualRequest))
            : 1f;

        float baseHoverGranted = protectBaseHover ? baseHoverRequest : baseHoverRequest * residualScale;
        float driveGranted = driveRequest * residualScale;
        float vectoringGranted = vectoringGuaranteed + residualVectoring * residualScale;
        float roofGranted = roofRequest * residualScale;
        float bottomGranted = bottomRequest * residualScale;
        float overchargeGranted = overchargeGuaranteed + residualOvercharge * residualScale;
        stabilizerGranted += residualStabilizer * residualScale;
        float otherGranted = otherRequest * residualScale;

        float baseHoverScale = GetPower01(baseHoverRequest, baseHoverGranted);
        float driveScale = GetPower01(driveRequest, driveGranted);
        float vectoringScale = GetPower01(vectoringRequest, vectoringGranted);
        float roofScale = GetPower01(roofRequest, roofGranted);
        float bottomScale = GetPower01(bottomRequest, bottomGranted);
        float overchargeScale = GetPower01(overchargeRequest, overchargeGranted);
        float overchargeBurstGranted = overchargeBurstRequest * overchargeScale;
        float overchargeChargeGranted = virtualOverchargeChargeRequest * overchargeScale;
        float stabilizerScale = GetPower01(stabilizerRequest, stabilizerGranted);
        float otherScale = GetPower01(otherRequest, otherGranted);

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

        float totalRequested = baseHoverRequest + nonHoverPerformanceRequest;
        float totalGranted = baseHoverGranted + driveGranted + vectoringGranted + roofGranted + bottomGranted + overchargeGranted + stabilizerGranted + otherGranted;
        float sharedGranted = totalGranted
                            - (protectBaseHover ? baseHoverGranted : 0f)
                            - (protectStabilizer ? stabilizerGranted : 0f);
        float performanceScale = sharedRequest > 0.001f ? Mathf.Clamp01(sharedGranted / sharedRequest) : 1f;

        currentEnergy = new EnergyState
        {
            totalBudget = budget,
            totalRequested = totalRequested,
            totalGranted = totalGranted,
            overload01 = Mathf.Clamp01(sharedRequest <= budget ? 0f : (sharedRequest - budget) / budget),

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
            overchargePower01 = overchargeScale,
            overchargeChargePower01 = GetPower01(virtualOverchargeChargeRequest, overchargeChargeGranted),
            overchargeBurstPower01 = GetPower01(overchargeBurstRequest, overchargeBurstGranted),
            stabilizerPower01 = GetPower01(stabilizerRequest, stabilizerGranted),
            baseHoverPower01 = GetPower01(baseHoverRequest, baseHoverGranted),
            otherPower01 = GetPower01(otherRequest, otherGranted),
            baseHoverProtected = protectBaseHover,
            stabilizerProtected = protectStabilizer,
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

    private static float GetPower01(float request, float granted)
    {
        if (request <= 0.001f)
            return 1f;
        return Mathf.Clamp01(granted / request);
    }
}
