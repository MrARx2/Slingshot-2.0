using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Controllable OVERCHARGE system.
/// Holding Space burns a finite reserve into the main propulsion thruster. A fresh
/// press adds a short ignition kick, while the held portion remains smooth and fully
/// controllable. Releasing Space preserves the remaining reserve, which then draws
/// from the shared POWER BUS while it regenerates.
/// </summary>
public class OverchargeCore : MonoBehaviour
{
    [HideInInspector] public ThrusterBus thrusterBus;
    [HideInInspector] public ThrusterNode mainThruster;
    [HideInInspector] public ThrusterNode brakeThruster;

    [Header("Overcharge Reserve")]
    [Tooltip("Seconds of continuous Overcharge available from a full reserve.")]
    [FormerlySerializedAs("boostDuration")]
    [Min(0.25f)] public float fullBurnDuration = 3.2f;

    [Tooltip("Delay after releasing Overcharge before regeneration begins.")]
    [Min(0f)] public float regenerationDelay = 0.9f;

    [Tooltip("Seconds required to regenerate from empty to full after the delay.")]
    [FormerlySerializedAs("cooldownDuration")]
    [Min(0.1f)] public float rechargeDuration = 7f;

    [Tooltip("POWER BUS demand while the Overcharge reserve regenerates. At the standard 100-unit BUS, 50 consumes half of the available performance power.")]
    [Min(0f)] public float rechargeBusDemand = 50f;

    [Tooltip("Minimum reserve needed to ignite. Prevents empty-tank input chatter.")]
    [Range(0f, 0.15f)] public float minimumIgnitionReserve01 = 0.025f;

    [Header("Grip Break Drift Recharge")]
    [Tooltip("Total Overcharge recharge speed during a fully committed Grip Break drift. The extra recharge still consumes proportional POWER BUS capacity.")]
    [Range(1f, 4f)] public float fullDriftRechargeMultiplier = 2.25f;

    [Tooltip("Drift angle where the Grip Break recharge bonus begins to build.")]
    [Range(0f, 45f)] public float driftRechargeStartAngle = 8f;

    [Tooltip("Drift angle that earns the full Grip Break recharge bonus.")]
    [Range(10f, 80f)] public float driftRechargeFullAngle = 34f;

    [Tooltip("Minimum craft speed required before sideways motion counts as a rewarding drift.")]
    [Min(0f)] public float driftRechargeMinimumSpeedKmh = 120f;

    [Header("Overcharge Force")]
    [Tooltip("Additional main-thruster throttle while Overcharge is held.")]
    [FormerlySerializedAs("boostThrottle")]
    [Min(0f)] public float sustainedThrottle = 1.35f;

    [Tooltip("Extra throttle layered over the sustained burn at ignition.")]
    [Min(0f)] public float ignitionThrottleBonus = 1.15f;

    [Tooltip("Duration of the sharp ignition kick.")]
    [Range(0.05f, 0.5f)] public float ignitionPunchDuration = 0.22f;

    [Tooltip("How quickly sustained Overcharge reaches full output after the button is held.")]
    [Min(0.1f)] public float burnAttackResponse = 22f;

    [Tooltip("How quickly presentation relaxes after Overcharge is released.")]
    [Min(0.1f)] public float burnReleaseResponse = 8f;

    [Tooltip("Sustained output at the final drops of Overcharge. The taper warns the player instead of cutting force abruptly.")]
    [Range(0.25f, 1f)] public float lowReserveOutputScale = 0.68f;

    [Header("Read Only")]
    [FormerlySerializedAs("nitroReserve01")]
    [SerializeField, Range(0f, 1f)] private float overchargeReserve01 = 1f;
    [SerializeField, Range(0f, 1f)] private float burnEnvelope01;
    [SerializeField, Range(0f, 1f)] private float grantPower01 = 1f;
    [SerializeField, Range(0f, 1f)] private float chargeGrantPower01 = 1f;
    [SerializeField] private float requestedBoostStrength;
    [SerializeField] private float regenerationDelayRemaining;
    [SerializeField] private float ignitionRemaining;
    [SerializeField] private bool isBurning;
    [SerializeField] private int boostSequenceId;
    [SerializeField, Range(0f, 1f)] private float driftRechargeBonus01;
    [SerializeField, Min(1f)] private float driftRechargeMultiplier = 1f;

    private float _deniedAtTime = -999f;

    public bool CanBoost => overchargeReserve01 >= Mathf.Max(0.001f, minimumIgnitionReserve01);
    public bool IsBoosting => isBurning;
    public bool IsRecharging => !isBurning && overchargeReserve01 < 0.999f && regenerationDelayRemaining <= 0f;

    // Compatibility names used by existing camera/HUD consumers.
    public float BoostReadiness01 => OverchargeReserve01;
    public float BoostEnvelope01 => Mathf.Clamp01(burnEnvelope01);
    public float OverchargeReserve01 => Mathf.Clamp01(overchargeReserve01);
    public float NitroReserve01 => OverchargeReserve01;
    public float RechargeDelayRemaining => Mathf.Max(0f, regenerationDelayRemaining);
    public float RequestedRechargePower => IsRecharging
        ? Mathf.Max(0f, rechargeBusDemand) * DriftRechargeMultiplier
        : 0f;
    public float RechargePower01 => Mathf.Clamp01(chargeGrantPower01);
    public float DriftRechargeBonus01 => Mathf.Clamp01(driftRechargeBonus01);
    public float DriftRechargeMultiplier => Mathf.Max(1f, driftRechargeMultiplier);

    public float CooldownRemaining => RechargeDelayRemaining
        + (1f - OverchargeReserve01) * Mathf.Max(0.1f, rechargeDuration);
    public float CooldownDuration => Mathf.Max(0.1f, rechargeDuration);

    public float RequestedBoostStrength => Mathf.Max(0f, requestedBoostStrength);
    public float GrantedBoostStrength => RequestedBoostStrength * Mathf.Clamp01(grantPower01);

    /// <summary>
    /// Actual Overcharge presentation strength. Sustained burn keeps a strong camera/trail
    /// response; ignition briefly pushes the signal to its crest.
    /// </summary>
    public float CameraBoost01
    {
        get
        {
            float sustained = BoostEnvelope01 * 0.82f;
            float ignition = Ignition01 * 0.42f;
            return Mathf.Clamp01((sustained + ignition) * Mathf.Clamp01(grantPower01));
        }
    }

    public float Ignition01 => ignitionPunchDuration <= 0.001f
        ? 0f
        : Mathf.Clamp01(ignitionRemaining / ignitionPunchDuration);

    public int BoostSequenceId => boostSequenceId;
    public bool BoostDeniedRecently => Time.time - _deniedAtTime <= 0.4f;

    public void TickOvercharge(CraftIntent intent, CraftTelemetry telemetry, EnergyState energy,
                               float gripBreakerAmount = 0f)
    {
        float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        grantPower01 = Mathf.Clamp01(energy.overchargeBurstPower01);
        chargeGrantPower01 = Mathf.Clamp01(energy.overchargeChargePower01);
        UpdateDriftRecharge(intent, telemetry, gripBreakerAmount);

        bool wantsBurn = intent.boostHeld;
        bool hasReserve = overchargeReserve01 > 0.0001f;
        bool canContinueBurn = isBurning && hasReserve;
        bool canIgnite = !isBurning && CanBoost;

        if (wantsBurn && (canContinueBurn || canIgnite))
        {
            if (!isBurning)
            {
                if (CanBoost)
                    BeginBurn();
                else
                    _deniedAtTime = Time.time;
            }

            if (isBurning)
            {
                regenerationDelayRemaining = Mathf.Max(0f, regenerationDelay);

                float reserveShape = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0f, 0.25f, overchargeReserve01));
                float targetEnvelope = Mathf.Lerp(lowReserveOutputScale, 1f, reserveShape);
                burnEnvelope01 = ExpApproach(
                    burnEnvelope01, targetEnvelope, burnAttackResponse, dt);

                requestedBoostStrength = sustainedThrottle * burnEnvelope01
                                       + ignitionThrottleBonus * Ignition01;
                ApplyBoost(requestedBoostStrength);

                ignitionRemaining = Mathf.Max(0f, ignitionRemaining - dt);
                overchargeReserve01 = Mathf.Max(0f,
                    overchargeReserve01 - dt / Mathf.Max(0.25f, fullBurnDuration));
            }
        }
        else
        {
            if (wantsBurn && intent.boostPressed)
                _deniedAtTime = Time.time;

            isBurning = false;
            ignitionRemaining = 0f;
            requestedBoostStrength = 0f;
            burnEnvelope01 = ExpApproach(
                burnEnvelope01, 0f, burnReleaseResponse, dt);

            // The player must release an empty trigger before the reserve can
            // regenerate. This prevents automatic sputter/re-ignition while held.
            if (wantsBurn)
            {
                regenerationDelayRemaining = Mathf.Max(
                    regenerationDelayRemaining, regenerationDelay);
            }
            else if (regenerationDelayRemaining > 0f)
            {
                regenerationDelayRemaining = Mathf.Max(0f,
                    regenerationDelayRemaining - dt);
            }
            else if (overchargeReserve01 < 1f)
            {
                overchargeReserve01 = Mathf.Min(1f,
                    overchargeReserve01
                    + dt / Mathf.Max(0.1f, rechargeDuration)
                    * chargeGrantPower01
                    * DriftRechargeMultiplier);
            }
        }
    }

    private void UpdateDriftRecharge(CraftIntent intent, CraftTelemetry telemetry,
                                     float gripBreakerAmount)
    {
        driftRechargeBonus01 = 0f;
        driftRechargeMultiplier = 1f;

        // Reward an actual on-track slide, not merely holding Grip Break while
        // stationary, travelling straight, airborne, or during grip recovery.
        if (!intent.wantsGripBreaker || gripBreakerAmount <= 0.001f
            || (!telemetry.hasSurfaceContact && !telemetry.isGrounded))
            return;

        float planarSpeed = new Vector2(telemetry.forwardSpeed, telemetry.sideSpeed).magnitude;
        float speedKmh = planarSpeed * 3.6f;
        float speed01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            Mathf.Max(0f, driftRechargeMinimumSpeedKmh),
            Mathf.Max(driftRechargeMinimumSpeedKmh + 80f,
                      driftRechargeMinimumSpeedKmh * 1.5f),
            speedKmh));

        float driftAngle = Mathf.Abs(Mathf.Atan2(
            telemetry.sideSpeed,
            Mathf.Max(0.01f, Mathf.Abs(telemetry.forwardSpeed))) * Mathf.Rad2Deg);
        float angle01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            Mathf.Max(0f, driftRechargeStartAngle),
            Mathf.Max(driftRechargeStartAngle + 1f, driftRechargeFullAngle),
            driftAngle));

        driftRechargeBonus01 = Mathf.Clamp01(angle01 * speed01 * Mathf.Clamp01(gripBreakerAmount));
        driftRechargeMultiplier = Mathf.Lerp(
            1f, Mathf.Max(1f, fullDriftRechargeMultiplier), driftRechargeBonus01);
    }

    private void BeginBurn()
    {
        isBurning = true;
        ignitionRemaining = Mathf.Max(0.01f, ignitionPunchDuration);
        regenerationDelayRemaining = Mathf.Max(0f, regenerationDelay);
        boostSequenceId++;
    }

    private void ApplyBoost(float throttle)
    {
        if (thrusterBus == null || throttle <= 0f)
            return;

        if (mainThruster != null)
            thrusterBus.AddThrottle(mainThruster, throttle, ThrusterPowerChannel.Overcharge);
        else
            thrusterBus.AddThrottle(
                ThrusterNode.ThrusterRole.Main, null, throttle,
                ThrusterPowerChannel.Overcharge);
    }

    private static float ExpApproach(float current, float target, float response, float dt)
        => Mathf.Lerp(current, target,
            1f - Mathf.Exp(-Mathf.Max(0.1f, response) * dt));

    public bool TryTriggerBoostForTest()
    {
        if (!CanBoost || isBurning) return false;
        BeginBurn();
        return true;
    }

    public void ResetBoostState()
    {
        overchargeReserve01 = 1f;
        burnEnvelope01 = 0f;
        requestedBoostStrength = 0f;
        regenerationDelayRemaining = 0f;
        ignitionRemaining = 0f;
        isBurning = false;
        grantPower01 = 1f;
        chargeGrantPower01 = 1f;
        driftRechargeBonus01 = 0f;
        driftRechargeMultiplier = 1f;
    }
}
