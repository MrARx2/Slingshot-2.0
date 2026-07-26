using UnityEngine;

/// <summary>
/// Space-held charge/release burst system.
/// <para>
/// Hold Space to charge the selected thruster group. Release Space to fire
/// a burst. Selection rules:
/// Space alone = bottom thrusters.
/// Space + Q = roof thrusters.
/// Space + E = bottom thrusters.
/// Space + W = main thruster.
/// Space + S = brake thruster.
/// </para>
/// </summary>
public class OverchargeCore : MonoBehaviour
{
    [HideInInspector] public ThrusterBus thrusterBus;
    [HideInInspector] public ThrusterNode mainThruster;
    [HideInInspector] public ThrusterNode brakeThruster;

    [Header("Charge")]
    [Tooltip("How fast the overcharge fills per second.")]
    public float chargeRate = 1.5f;

    [Tooltip("Minimum charge required before release creates a burst.")]
    [Range(0f, 1f)]
    public float minimumReleaseCharge = 0.08f;

    [Tooltip("If true, changing held keys while Space is down changes the selected target. If false, target locks when charging begins.")]
    public bool allowRetargetWhileCharging = false;

    [Header("Stabilizer Interaction")]
    [Tooltip("If true, an armed stabilizer blocks roof overcharge unless the craft is inverted/recovering. Bottom overcharge is still allowed for jump bursts.")]
    public bool stabilizerBlocksRoofOvercharge = true;

    [Header("Burst")]
    [Tooltip("Burst throttle at minimum meaningful charge.")]
    public float minBurstThrottle = 1.25f;

    [Tooltip("Burst throttle at full charge.")]
    public float maxBurstThrottle = 4.0f;

    [Tooltip("Burst duration at minimum meaningful charge.")]
    public float minBurstDuration = 0.06f;

    [Tooltip("Burst duration at full charge.")]
    public float maxBurstDuration = 0.22f;

    [Header("Read Only")]
    [SerializeField] private float charge01;
    [SerializeField] private OverchargeTarget chargingTarget;
    [SerializeField] private OverchargeTarget activeBurstTarget;
    [SerializeField] private float burstRemaining;

    public float Charge01 => charge01;
    public OverchargeTarget ChargingTarget => chargingTarget;
    public OverchargeTarget ActiveBurstTarget => activeBurstTarget;
    public bool IsCharging => chargingTarget != OverchargeTarget.None;
    public bool IsBursting => burstRemaining > 0f;

    private float _burstThrottle;
    private float _burstDuration;
    private int _lastReleaseFrame = -1;

    public void TickOvercharge(CraftIntent intent, CraftTelemetry telemetry, EnergyState energy)
    {
        float dt = Time.fixedDeltaTime;

        if (intent.wantsOvercharge)
        {
            if (chargingTarget == OverchargeTarget.None)
            {
                chargingTarget = intent.overchargeTarget != OverchargeTarget.None
                    ? intent.overchargeTarget
                    : OverchargeTarget.BottomThrusters;

                charge01 = 0f;
            }
            else if (allowRetargetWhileCharging && intent.overchargeTarget != OverchargeTarget.None)
            {
                chargingTarget = intent.overchargeTarget;
            }

            charge01 = Mathf.Clamp01(charge01 + chargeRate * Mathf.Clamp01(energy.overchargeChargePower01) * dt);
        }

        if (intent.overchargeReleased && intent.commandFrame != _lastReleaseFrame)
        {
            _lastReleaseFrame = intent.commandFrame;

            if (chargingTarget != OverchargeTarget.None && charge01 >= minimumReleaseCharge)
            {
                if (CanReleaseBurst(chargingTarget, intent))
                {
                    StartBurst(chargingTarget, charge01);
                }
            }

            chargingTarget = OverchargeTarget.None;
            charge01 = 0f;
        }

        if (!intent.wantsOvercharge && chargingTarget != OverchargeTarget.None && !intent.overchargeReleased)
        {
            // Safety: if input state is interrupted without a release event, cancel charge.
            chargingTarget = OverchargeTarget.None;
            charge01 = 0f;
        }

        if (burstRemaining > 0f)
        {
            ApplyBurst();
            burstRemaining -= dt;

            if (burstRemaining <= 0f)
            {
                burstRemaining = 0f;
                activeBurstTarget = OverchargeTarget.None;
                _burstThrottle = 0f;
                _burstDuration = 0f;
            }
        }
    }

    private bool CanReleaseBurst(OverchargeTarget target, CraftIntent intent)
    {
        if (!stabilizerBlocksRoofOvercharge)
        {
            return true;
        }

        // Stabilizer owns vertical thrusters, but bottom overcharge is the
        // intentional exception for jump bursts. Roof overcharge is allowed
        // only for upside-down recovery.
        if (intent.verticalThrustersLockedByStabilizer && target == OverchargeTarget.RoofThrusters)
        {
            return intent.recoveryOverrideActive;
        }

        return true;
    }

    private void StartBurst(OverchargeTarget target, float charge)
    {
        float c = Mathf.Clamp01(charge);

        activeBurstTarget = target;
        _burstThrottle = Mathf.Lerp(minBurstThrottle, maxBurstThrottle, c);
        _burstDuration = Mathf.Lerp(minBurstDuration, maxBurstDuration, c);
        burstRemaining = _burstDuration;
    }

    private void ApplyBurst()
    {
        if (thrusterBus == null || activeBurstTarget == OverchargeTarget.None)
        {
            return;
        }

        // Optional tiny taper so it does not feel like a perfectly square force.
        float duration = Mathf.Max(0.001f, _burstDuration);
        float t = 1f - Mathf.Clamp01(burstRemaining / duration);
        float envelope = Mathf.Lerp(1f, 0.65f, t);
        float throttle = _burstThrottle * envelope;

        switch (activeBurstTarget)
        {
            case OverchargeTarget.BottomThrusters:
                thrusterBus.AddThrottle(ThrusterNode.ThrusterRole.Hover, null, throttle, ThrusterPowerChannel.Overcharge);
                break;

            case OverchargeTarget.RoofThrusters:
                thrusterBus.AddThrottle(ThrusterNode.ThrusterRole.Roof, null, throttle, ThrusterPowerChannel.Overcharge);
                break;

            case OverchargeTarget.MainThruster:
                if (mainThruster != null)
                {
                    thrusterBus.AddThrottle(mainThruster, throttle, ThrusterPowerChannel.Overcharge);
                }
                else
                {
                    thrusterBus.AddThrottle(ThrusterNode.ThrusterRole.Main, null, throttle, ThrusterPowerChannel.Overcharge);
                }
                break;

            case OverchargeTarget.BrakeThruster:
                if (brakeThruster != null)
                {
                    thrusterBus.AddThrottle(brakeThruster, throttle, ThrusterPowerChannel.Overcharge);
                }
                else
                {
                    thrusterBus.AddThrottle(ThrusterNode.ThrusterRole.Brake, null, throttle, ThrusterPowerChannel.Overcharge);
                }
                break;
        }
    }
}
