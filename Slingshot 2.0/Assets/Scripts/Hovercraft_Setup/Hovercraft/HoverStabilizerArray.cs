using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Hover suspension / anti-gravity cushion controller.
/// Manages the four bottom-facing hover thrusters that keep the craft airborne,
/// handle grounded attitude via corner target heights, provide airborne roll/pitch
/// control through throttle differentials, and execute jump bursts.
///
/// <para>
/// This is a pure physics module — it does not read input directly.
/// <see cref="CraftCore"/> calls <see cref="ApplyHover"/> once per FixedUpdate,
/// passing the current <see cref="CraftIntent"/>, <see cref="CraftTelemetry"/>,
/// and <see cref="TractionState"/>.
/// </para>
///
/// <para>Ported from V1 <c>ThrusterHovercraftController</c> hover logic.</para>
/// </summary>
public class HoverStabilizerArray : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  REFERENCES  (set by CraftCore)
    // ══════════════════════════════════════════════════════════════

    /// <summary>The craft's Rigidbody, assigned by CraftCore during setup.</summary>
    [HideInInspector] public Rigidbody rb;

    /// <summary>The central thruster bus for routing throttle commands.</summary>
    [HideInInspector] public ThrusterBus thrusterBus;

    /// <summary>Front-left hover thruster node.</summary>
    [HideInInspector] public ThrusterNode hoverFL;

    /// <summary>Front-right hover thruster node.</summary>
    [HideInInspector] public ThrusterNode hoverFR;

    /// <summary>Rear-left hover thruster node.</summary>
    [HideInInspector] public ThrusterNode hoverRL;

    /// <summary>Rear-right hover thruster node.</summary>
    [HideInInspector] public ThrusterNode hoverRR;

    /// <summary>Front-left roof thruster node. Used by the stabilizer for rebound/downforce control.</summary>
    [HideInInspector] public ThrusterNode roofFL;

    /// <summary>Front-right roof thruster node. Used by the stabilizer for rebound/downforce control.</summary>
    [HideInInspector] public ThrusterNode roofFR;

    /// <summary>Rear-left roof thruster node. Used by the stabilizer for rebound/downforce control.</summary>
    [HideInInspector] public ThrusterNode roofRL;

    /// <summary>Rear-right roof thruster node. Used by the stabilizer for rebound/downforce control.</summary>
    [HideInInspector] public ThrusterNode roofRR;

    // ══════════════════════════════════════════════════════════════
    //  STABILIZER ENGAGEMENT
    // ══════════════════════════════════════════════════════════════

    [Header("Stabilizer Engagement")]
    [Tooltip("How fast automatic hover throttle bleeds to zero when the stabilizer is disengaged with R.")]
    public float stabilizerDisengageThrottleDecaySpeed = 30f;

    /// <summary>True if the automatic hover stabilizer was armed by the latest CraftIntent.</summary>
    public bool IsStabilizerArmed { get; private set; } = true;

    /// <summary>True when the stabilizer is armed and currently applying meaningful hover/roof output.</summary>
    public bool IsStabilizerActive => IsStabilizerArmed && (AverageAutoHoverThrottle > 0.02f || AverageAutoRoofThrottle > 0.02f);

    /// <summary>Average automatic bottom-hover throttle currently requested by the stabilizer.</summary>
    public float AverageAutoHoverThrottle => (_hoverThrottleFL + _hoverThrottleFR + _hoverThrottleRL + _hoverThrottleRR) * 0.25f;

    /// <summary>Average automatic roof/downforce throttle currently requested by the stabilizer.</summary>
    public float AverageAutoRoofThrottle => (_roofThrottleFL + _roofThrottleFR + _roofThrottleRL + _roofThrottleRR) * 0.25f;

    [Header("Roof Stabilizer Assist")]
    [Tooltip("If true, an armed stabilizer may use roof thrusters to kill upward rebound and hold the craft down near the hover cushion.")]
    public bool useRoofThrustersForStabilizer = true;

    [Tooltip("Maximum automatic roof throttle used by the stabilizer. Keep modest so it does not feel like invisible autopilot.")]
    public float roofStabilizerMaxThrottle = 0.85f;

    [Tooltip("Upward velocity away from the surface where roof stabilizer starts adding downforce.")]
    public float roofReboundVelocityStart = 0.35f;

    [Tooltip("Upward velocity away from the surface where roof stabilizer reaches max rebound throttle.")]
    public float roofReboundVelocityFull = 3.0f;

    [Tooltip("Height above target hover height where roof stabilizer starts pulling the craft back toward the cushion.")]
    public float roofHeightErrorStart = 0.15f;

    [Tooltip("Height above target hover height where roof stabilizer reaches max height-correction throttle.")]
    public float roofHeightErrorFull = 1.0f;

    [Tooltip("How quickly automatic roof stabilizer throttle responds.")]
    public float roofStabilizerResponseSpeed = 18f;

    // ══════════════════════════════════════════════════════════════
    //  HOVER PD CONTROLLER
    // ══════════════════════════════════════════════════════════════

    [Header("Hover PD Controller")]
    [Tooltip("Desired ride height above the ground surface.")]
    public float hoverHeight = 2.0f;

    [Tooltip("Proportional gain — height correction strength. Higher = more aggressively maintains hover height.")]
    public float hoverKp = 0.42f;

    [Tooltip("Derivative gain — vertical velocity damping. Higher = less bounce, lower = more natural oscillation.")]
    public float hoverKd = 0.18f;

    [Tooltip("Maximum allowed hover thruster throttle outside of jump bursts.")]
    public float hoverMaxThrottle = 2.25f;

    // ══════════════════════════════════════════════════════════════
    //  HOVER FEEL / SMOOTHING
    // ══════════════════════════════════════════════════════════════

    [Header("Hover Feel / Smoothing")]
    [Tooltip("Exponential response speed when hover throttle needs to rise. Lower = smoother/heavier feel.")]
    public float hoverThrusterResponseSpeed = 7f;

    [Tooltip("Exponential response speed when hover throttle needs to fall. Higher values prevent pogo-stick rebound after landings.")]
    public float hoverThrottleFallResponseSpeed = 24f;

    [Tooltip("How quickly hover throttle bleeds off while airborne after a jump burst. Prevents the jump burst from lingering as lift.")]
    public float airborneHoverThrottleDecaySpeed = 32f;

    [Tooltip("If true, jump burst bypasses hover smoothing for a sharp impulse.")]
    public bool jumpBypassesHoverSmoothing = true;

    [Tooltip("Amplitude of gentle natural hover bob/oscillation.")]
    [Range(0f, 1f)]
    public float hoverOscillationAmount = 0.035f;

    [Tooltip("Frequency of the natural hover bob.")]
    public float hoverOscillationSpeed = 1.25f;

    [Tooltip("Per-thruster phase offset so all four corners don't pulse in unison.")]
    public float hoverOscillationPhaseSpread = 0.7f;

    // ══════════════════════════════════════════════════════════════
    //  LANDING / ANTI-POGO DAMPING
    // ══════════════════════════════════════════════════════════════

    [Header("Landing / Anti-Pogo Damping")]
    [Tooltip("Extra derivative damping while the craft is falling into the hover cushion. Higher = softer landings.")]
    public float landingDampingMultiplier = 3.0f;

    [Tooltip("Extra derivative damping while the craft is rebounding upward away from the ground. Higher = less bounce.")]
    public float reboundDampingMultiplier = 3.5f;

    [Tooltip("Downward speed where the hard-landing damping reaches full extra strength.")]
    public float hardLandingSpeedForMaxDamping = 12f;

    [Tooltip("Additional damping added on very hard impacts, blended in up to hardLandingSpeedForMaxDamping.")]
    public float hardLandingExtraDamping = 2.0f;

    [Tooltip("If true, aggressively cuts hover thrust while the craft is already rebounding upward near/above target height.")]
    public bool cutThrottleOnRebound = true;

    [Tooltip("Upward speed where rebound throttle cutting begins.")]
    public float reboundThrottleCutVelocity = 0.75f;

    [Tooltip("Upward speed where rebound throttle cutting reaches full cut.")]
    public float reboundFullCutVelocity = 4.0f;

    [Tooltip("Only cut rebound throttle when height error is at or below this value. Positive allows cutting slightly before perfect height.")]
    public float reboundThrottleCutHeightError = 0.05f;

    // ══════════════════════════════════════════════════════════════
    //  HOVER CUSHION ACTIVATION
    // ══════════════════════════════════════════════════════════════

    [Header("Hover Cushion Activation")]
    [Tooltip("Extra distance above the target hover height where the hover cushion begins to apply force. This decouples long ground probing from actual hover lift.")]
    public float hoverActivationHeightBuffer = 1.25f;

    [Tooltip("Extra distance above the target hover height where hover force reaches full strength. Between this and activation distance, force fades in smoothly.")]
    public float hoverFullPowerHeightBuffer = 0.25f;

    [Tooltip("If true, hover force fades in near the edge of the cushion. If false, force activates sharply at the activation distance.")]
    public bool fadeHoverForcesNearActivationEdge = true;

    // ══════════════════════════════════════════════════════════════
    //  GROUNDED ATTITUDE / CORNER TARGET HEIGHTS
    // ══════════════════════════════════════════════════════════════

    [Header("Player Attitude Authority")]
    [Tooltip("Maximum left/right target-height offset used for player-driven roll/carve banking. This is the main 'how much does it bank' knob.")]
    [FormerlySerializedAs("maxRollHoverHeightOffset")]
    public float playerRollHeightOffset = 0.45f;

    [Tooltip("Maximum front/rear target-height offset used for player-driven pitch weight shift. This is the main grounded pitch authority knob.")]
    [FormerlySerializedAs("maxPitchHoverHeightOffset")]
    public float playerPitchHeightOffset = 0.30f;

    [Tooltip("Target roll angle limit in degrees for player banking.")]
    [FormerlySerializedAs("maxRollTarget")]
    public float maxPlayerRollTarget = 30f;

    [Tooltip("Target pitch angle limit in degrees for player nose-up/nose-down weight shift.")]
    [FormerlySerializedAs("maxPitchTarget")]
    public float maxPlayerPitchTarget = 18f;

    [Tooltip("How quickly roll/carve target changes when the player switches side-to-side.")]
    [FormerlySerializedAs("leanResponseSpeed")]
    public float rollResponseSpeed = 5.0f;

    [Tooltip("How quickly grounded pitch target follows the mouse-driven pitch command.")]
    public float pitchResponseSpeed = 6.0f;

    [Tooltip("Mouse-Y pitch is a relative weight-shift input. This gain turns mouse delta into visible grounded nose-up/nose-down pitch target changes.")]
    [FormerlySerializedAs("groundedPitchInputGain")]
    public float pitchInputGain = 18f;

    [Tooltip("How quickly roll/pitch targets return to neutral when player input stops.")]
    [FormerlySerializedAs("leanDecaySpeed")]
    public float attitudeReturnSpeed = 8f;

    [Header("Auto-Level Assist")]
    [Tooltip("Auto-level roll strength. Lower values let player banking/carving feel freer.")]
    [FormerlySerializedAs("autoLevelRollHeightPerDegree")]
    public float autoLevelRollStrength = 0.004f;

    [Tooltip("Auto-level pitch strength. Lower values stop the stabilizer from fighting nose-up/nose-down input.")]
    [FormerlySerializedAs("autoLevelPitchHeightPerDegree")]
    public float autoLevelPitchStrength = 0.004f;

    [Tooltip("Maximum target-height correction allowed from auto-leveling.")]
    [FormerlySerializedAs("maxAutoLevelHeightOffset")]
    public float autoLevelMaxHeightOffset = 0.25f;

    [Tooltip("How much active roll input suppresses roll auto-level. 1 = full input almost fully disables roll auto-level.")]
    [Range(0f, 1f)] public float autoLevelSuppressionByRollInput = 0.90f;

    [Tooltip("How much active pitch input suppresses pitch auto-level. 1 = full input almost fully disables pitch auto-level.")]
    [Range(0f, 1f)] public float autoLevelSuppressionByPitchInput = 0.90f;

    [Tooltip("Invert auto-leveling roll correction if it makes the craft roll worse.")]
    public bool invertGroundedRollHeightOffset = false;

    [Tooltip("Invert auto-leveling pitch correction if it makes the craft pitch worse.")]
    public bool invertGroundedPitchHeightOffset = false;

    [Header("Air Attitude Base")]
    [Tooltip("Max throttle bias applied to hover thrusters for airborne attitude control.")]
    [Range(0f, 2f)]
    public float maxLeanBias = 0.25f;

    // ══════════════════════════════════════════════════════════════
    //  AIR ATTITUDE CONTROL
    // ══════════════════════════════════════════════════════════════

    [Header("Air Attitude Control")]
    [Tooltip("Mouse X roll strength while airborne.")]
    [Range(0f, 10f)]
    public float airRollSensitivity = 1.5f;

    [Tooltip("Extra multiplier for airborne roll authority.")]
    [Range(0f, 5f)]
    public float airRollAuthority = 0.65f;

    [Tooltip("How much A/D contributes to air roll (keyboard influence).")]
    [Range(0f, 1f)]
    public float airKeyboardRollInfluence = 0.35f;

    [Tooltip("Mouse Y pitch strength while airborne.")]
    [Range(0f, 10f)]
    public float airControlSensitivity = 1.35f;

    [Tooltip("Extra multiplier for airborne pitch authority.")]
    [Range(0f, 5f)]
    public float airPitchAuthority = 0.75f;

    [Tooltip("Additional gain applied to Mouse Y before airborne pitch demand is generated. This fixes weak/no pitch response with small mouse deltas.")]
    [Range(0f, 20f)]
    public float airPitchInputGain = 5f;

    [Tooltip("Invert airborne roll direction if it responds backwards.")]
    public bool invertAirRollControl = false;

    [Tooltip("Invert airborne pitch direction if it responds backwards.")]
    public bool invertAirPitchControl = false;

    // ══════════════════════════════════════════════════════════════
    //  JUMP
    // ══════════════════════════════════════════════════════════════

    [Header("Jump")]
    [Tooltip("Throttle value applied to all hover thrusters during a jump burst.")]
    public float jumpBurstMultiplier = 3f;

    [Tooltip("Duration of the jump burst in seconds.")]
    public float jumpBurstDuration = 0.15f;

    [Tooltip("Cooldown between jump bursts in seconds.")]
    public float jumpCooldown = 1.5f;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC READ STATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>Remaining cooldown before the next jump is allowed.</summary>
    public float JumpCooldownTimer => _jumpCooldownRemaining;

    /// <summary>Current internal roll lean target (degrees, signed).</summary>
    public float LeanTargetRoll => _leanTargetRoll;

    /// <summary>Current internal pitch lean target (degrees, signed).</summary>
    public float LeanTargetPitch => _leanTargetPitch;

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ══════════════════════════════════════════════════════════════

    private float _leanTargetRoll;
    private float _leanTargetPitch;
    private float _pitchCommandTarget;

    private float _hoverThrottleFL;
    private float _hoverThrottleFR;
    private float _hoverThrottleRL;
    private float _hoverThrottleRR;

    private float _roofThrottleFL;
    private float _roofThrottleFR;
    private float _roofThrottleRL;
    private float _roofThrottleRR;

    private float _jumpCooldownRemaining;
    private float _jumpBurstRemaining;

    private float _gravityThrottle;

    // ══════════════════════════════════════════════════════════════
    //  SETUP
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the baseline gravity-countering throttle that each hover thruster
    /// needs just to maintain altitude under gravity + extra gravity.
    /// Call once after all references are wired (typically from CraftCore.Start).
    /// </summary>
    /// <param name="extraGravity">Additional downward acceleration applied by CraftCore.</param>
    public void ComputeGravityThrottle(float extraGravity)
    {
        float totalGravity = Mathf.Abs(Physics.gravity.y) + extraGravity;

        ThrusterNode[] hoverNodes = { hoverFL, hoverFR, hoverRL, hoverRR };

        int validCount = 0;
        float avgMaxForce = 0f;

        foreach (var node in hoverNodes)
        {
            if (node != null)
            {
                avgMaxForce += node.maxForce;
                validCount++;
            }
        }

        if (validCount > 0)
        {
            avgMaxForce /= validCount;
        }

        if (validCount > 0 && avgMaxForce > 0f)
        {
            _gravityThrottle = totalGravity / (validCount * avgMaxForce);
        }
        else
        {
            _gravityThrottle = 0f;
        }
    }

    /// <summary>
    /// Recommended distance for telemetry to treat the craft as near-grounded.
    /// This is intentionally based on the hover cushion, not the raw ThrusterNode ray length.
    /// </summary>
    public float GetRecommendedGroundedContactDistance()
    {
        float maxCornerOffset = Mathf.Max(
            Mathf.Abs(playerRollHeightOffset) + Mathf.Abs(playerPitchHeightOffset),
            Mathf.Abs(autoLevelMaxHeightOffset)
        );

        return Mathf.Max(0.05f, hoverHeight + hoverActivationHeightBuffer + maxCornerOffset);
    }

    // ══════════════════════════════════════════════════════════════
    //  MAIN ENTRY POINT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluate and apply hover suspension forces for this physics tick.
    /// Called once per FixedUpdate by CraftCore.
    /// </summary>
    /// <param name="intent">Processed pilot intent for this frame.</param>
    /// <param name="telemetry">Current craft telemetry snapshot.</param>
    /// <param name="traction">Current traction/grip state.</param>
    public void ApplyHover(CraftIntent intent, CraftTelemetry telemetry, TractionState traction)
    {
        ApplyHover(intent, telemetry, traction, EnergyState.Full);
    }

    public void ApplyHover(CraftIntent intent, CraftTelemetry telemetry, TractionState traction, EnergyState energy)
    {
        float dt = Time.fixedDeltaTime;

        IsStabilizerArmed = intent.stabilizerArmed;

        // R disengages only the automatic hover stabilizer.
        // It must NOT disable player air attitude control. Airborne roll/pitch
        // still use the bottom hover thrusters as attitude jets even while the
        // stabilizer is disengaged.
        if (!IsStabilizerArmed)
        {
            DisengageAutomaticHover(dt);

            if (!telemetry.isGrounded)
            {
                ApplyAirborneAttitudeOnly(intent, telemetry);
            }

            return;
        }

        // ── Tick jump cooldown ────────────────────────────────────
        if (_jumpCooldownRemaining > 0f)
        {
            _jumpCooldownRemaining -= dt;
        }

        // ── Update lean targets ──────────────────────────────────
        UpdateLeanTargets(intent, telemetry, traction, dt);

        // ── Jump handling ────────────────────────────────────────
        bool jumping = _jumpBurstRemaining > 0f;

        if (jumping)
        {
            _jumpBurstRemaining -= dt;
        }

        HandleJump(intent, telemetry);

        // Re-evaluate after HandleJump may have started a burst.
        jumping = _jumpBurstRemaining > 0f;

        // ── Airborne attitude demands ────────────────────────────
        float rollDemand = 0f;
        float pitchDemand = 0f;

        if (!telemetry.isGrounded)
        {
            ComputeAirborneAttitude(intent, telemetry, out rollDemand, out pitchDemand);
        }

        // ── Per-corner hover throttle ────────────────────────────
        SetHoverThrottle(hoverFL, ref _hoverThrottleFL, rollDemand, pitchDemand,
                         isLeft: true, isFront: true, jumping, telemetry);

        SetHoverThrottle(hoverFR, ref _hoverThrottleFR, rollDemand, pitchDemand,
                         isLeft: false, isFront: true, jumping, telemetry);

        SetHoverThrottle(hoverRL, ref _hoverThrottleRL, rollDemand, pitchDemand,
                         isLeft: true, isFront: false, jumping, telemetry);

        SetHoverThrottle(hoverRR, ref _hoverThrottleRR, rollDemand, pitchDemand,
                         isLeft: false, isFront: false, jumping, telemetry);

        ApplyRoofStabilizer(intent, telemetry, energy);
    }

    /// <summary>
    /// Apply only player-driven airborne pitch/roll using bottom hover thrusters.
    /// This is intentionally independent from the automatic stabilizer toggle: R
    /// disables auto-hover/auto-level assistance, not the pilot's air controls.
    /// </summary>
    private void ApplyAirborneAttitudeOnly(CraftIntent intent, CraftTelemetry telemetry)
    {
        float rollDemand = 0f;
        float pitchDemand = 0f;
        ComputeAirborneAttitude(intent, telemetry, out rollDemand, out pitchDemand);

        bool hasAirAttitudeDemand = Mathf.Abs(rollDemand) > 0.001f || Mathf.Abs(pitchDemand) > 0.001f;

        if (!hasAirAttitudeDemand)
        {
            DecayAirborneAttitudeThrottles();
            return;
        }

        SetAirborneAttitudeThrottle(hoverFL, ref _hoverThrottleFL, rollDemand, pitchDemand,
                                    isLeft: true, isFront: true);
        SetAirborneAttitudeThrottle(hoverFR, ref _hoverThrottleFR, rollDemand, pitchDemand,
                                    isLeft: false, isFront: true);
        SetAirborneAttitudeThrottle(hoverRL, ref _hoverThrottleRL, rollDemand, pitchDemand,
                                    isLeft: true, isFront: false);
        SetAirborneAttitudeThrottle(hoverRR, ref _hoverThrottleRR, rollDemand, pitchDemand,
                                    isLeft: false, isFront: false);
    }

    private void DecayAirborneAttitudeThrottles()
    {
        float dt = Time.fixedDeltaTime;

        _hoverThrottleFL = Mathf.MoveTowards(_hoverThrottleFL, 0f, airborneHoverThrottleDecaySpeed * dt);
        _hoverThrottleFR = Mathf.MoveTowards(_hoverThrottleFR, 0f, airborneHoverThrottleDecaySpeed * dt);
        _hoverThrottleRL = Mathf.MoveTowards(_hoverThrottleRL, 0f, airborneHoverThrottleDecaySpeed * dt);
        _hoverThrottleRR = Mathf.MoveTowards(_hoverThrottleRR, 0f, airborneHoverThrottleDecaySpeed * dt);

        if (thrusterBus == null)
        {
            return;
        }

        if (hoverFL != null) thrusterBus.SetThrottle(hoverFL, _hoverThrottleFL, ThrusterPowerChannel.Bottom);
        if (hoverFR != null) thrusterBus.SetThrottle(hoverFR, _hoverThrottleFR, ThrusterPowerChannel.Bottom);
        if (hoverRL != null) thrusterBus.SetThrottle(hoverRL, _hoverThrottleRL, ThrusterPowerChannel.Bottom);
        if (hoverRR != null) thrusterBus.SetThrottle(hoverRR, _hoverThrottleRR, ThrusterPowerChannel.Bottom);
    }

    private void SetAirborneAttitudeThrottle(
        ThrusterNode node,
        ref float smoothedThrottle,
        float rollDemand,
        float pitchDemand,
        bool isLeft,
        bool isFront)
    {
        if (node == null)
        {
            return;
        }

        // Left thrusters push right-roll; front thrusters push nose-down pitch.
        float rollAdjust = isLeft ? rollDemand : -rollDemand;
        float pitchAdjust = isFront ? -pitchDemand : pitchDemand;

        float throttle = Mathf.Clamp(rollAdjust + pitchAdjust, 0f, hoverMaxThrottle);
        throttle = SmoothHoverThrottle(node, smoothedThrottle, throttle, false, true);
        smoothedThrottle = throttle;

        if (thrusterBus != null)
        {
            // Air attitude is manual/performance thruster use, not protected base hover.
            if (node != null) thrusterBus.SetThrottle(node, throttle, ThrusterPowerChannel.Bottom);
        }
    }


    private void DisengageAutomaticHover(float dt)
    {
        _leanTargetRoll = Mathf.MoveTowards(_leanTargetRoll, 0f, attitudeReturnSpeed * dt * 2f);
        _leanTargetPitch = Mathf.MoveTowards(_leanTargetPitch, 0f, attitudeReturnSpeed * dt * 2f);

        _hoverThrottleFL = Mathf.MoveTowards(_hoverThrottleFL, 0f, stabilizerDisengageThrottleDecaySpeed * dt);
        _hoverThrottleFR = Mathf.MoveTowards(_hoverThrottleFR, 0f, stabilizerDisengageThrottleDecaySpeed * dt);
        _hoverThrottleRL = Mathf.MoveTowards(_hoverThrottleRL, 0f, stabilizerDisengageThrottleDecaySpeed * dt);
        _hoverThrottleRR = Mathf.MoveTowards(_hoverThrottleRR, 0f, stabilizerDisengageThrottleDecaySpeed * dt);

        _roofThrottleFL = Mathf.MoveTowards(_roofThrottleFL, 0f, stabilizerDisengageThrottleDecaySpeed * dt);
        _roofThrottleFR = Mathf.MoveTowards(_roofThrottleFR, 0f, stabilizerDisengageThrottleDecaySpeed * dt);
        _roofThrottleRL = Mathf.MoveTowards(_roofThrottleRL, 0f, stabilizerDisengageThrottleDecaySpeed * dt);
        _roofThrottleRR = Mathf.MoveTowards(_roofThrottleRR, 0f, stabilizerDisengageThrottleDecaySpeed * dt);

        if (thrusterBus != null)
        {
            if (hoverFL != null) thrusterBus.SetThrottle(hoverFL, _hoverThrottleFL, ThrusterPowerChannel.BaseHover);
            if (hoverFR != null) thrusterBus.SetThrottle(hoverFR, _hoverThrottleFR, ThrusterPowerChannel.BaseHover);
            if (hoverRL != null) thrusterBus.SetThrottle(hoverRL, _hoverThrottleRL, ThrusterPowerChannel.BaseHover);
            if (hoverRR != null) thrusterBus.SetThrottle(hoverRR, _hoverThrottleRR, ThrusterPowerChannel.BaseHover);

            if (roofFL != null) thrusterBus.SetThrottle(roofFL, _roofThrottleFL, ThrusterPowerChannel.Stabilizer);
            if (roofFR != null) thrusterBus.SetThrottle(roofFR, _roofThrottleFR, ThrusterPowerChannel.Stabilizer);
            if (roofRL != null) thrusterBus.SetThrottle(roofRL, _roofThrottleRL, ThrusterPowerChannel.Stabilizer);
            if (roofRR != null) thrusterBus.SetThrottle(roofRR, _roofThrottleRR, ThrusterPowerChannel.Stabilizer);
        }
    }


    // ══════════════════════════════════════════════════════════════
    //  ROOF STABILIZER ASSIST
    // ══════════════════════════════════════════════════════════════

    private void ApplyRoofStabilizer(CraftIntent intent, CraftTelemetry telemetry, EnergyState energy)
    {
        if (!useRoofThrustersForStabilizer || thrusterBus == null)
        {
            SetRoofStabilizerThrottles(0f, 0f, 0f, 0f);
            return;
        }

        // If this is an upside-down recovery situation, do not let the automatic
        // stabilizer fight the player's Q roof-thruster recovery. Manual Q and
        // OverchargeCore are allowed later in the frame.
        if (intent.recoveryOverrideActive)
        {
            SetRoofStabilizerThrottles(0f, 0f, 0f, 0f);
            return;
        }

        // Submit the desired stabilizer roof throttle. EnergyCore grants the final
        // power later based on the roof thruster hardware.
        float targetThrottle = ComputeAutomaticRoofThrottle(telemetry);
        SetRoofStabilizerThrottles(targetThrottle, targetThrottle, targetThrottle, targetThrottle);
    }

    private float ComputeAutomaticRoofThrottle(CraftTelemetry telemetry)
    {
        // Roof stabilizer only helps near a real surface. This avoids the old
        // invisible autopilot feeling from long-distance ground probes.
        float nearSurface01 = Mathf.Clamp01(telemetry.groundedFactor);
        if (nearSurface01 <= 0.001f || !telemetry.hasSurfaceContact)
        {
            return 0f;
        }

        float averageHoverDistance = GetAverageActiveHoverDistance();
        if (averageHoverDistance < 0f)
        {
            return 0f;
        }

        float heightError = averageHoverDistance - hoverHeight;
        float height01 = Mathf.InverseLerp(
            roofHeightErrorStart,
            Mathf.Max(roofHeightErrorStart + 0.01f, roofHeightErrorFull),
            heightError
        );

        Vector3 normal = telemetry.groundNormal.sqrMagnitude > 0.001f ? telemetry.groundNormal.normalized : Vector3.up;
        float upwardSpeedAwayFromSurface = rb != null ? Vector3.Dot(rb.linearVelocity, normal) : 0f;
        float rebound01 = Mathf.InverseLerp(
            roofReboundVelocityStart,
            Mathf.Max(roofReboundVelocityStart + 0.01f, roofReboundVelocityFull),
            upwardSpeedAwayFromSurface
        );

        float request01 = Mathf.Clamp01(Mathf.Max(height01, rebound01)) * nearSurface01;
        return request01 * roofStabilizerMaxThrottle;
    }

    private float GetAverageActiveHoverDistance()
    {
        float sum = 0f;
        int count = 0;

        AccumulateHoverDistance(hoverFL, ref sum, ref count);
        AccumulateHoverDistance(hoverFR, ref sum, ref count);
        AccumulateHoverDistance(hoverRL, ref sum, ref count);
        AccumulateHoverDistance(hoverRR, ref sum, ref count);

        return count > 0 ? sum / count : -1f;
    }

    private void AccumulateHoverDistance(ThrusterNode node, ref float sum, ref int count)
    {
        if (node == null || !node.IsGrounded || node.GroundDistance < 0f)
        {
            return;
        }

        float targetHeight = hoverHeight;
        float influence = GetHoverCushionInfluence(node.GroundDistance, targetHeight);
        if (influence <= 0.001f)
        {
            return;
        }

        sum += node.GroundDistance;
        count++;
    }

    private void SetRoofStabilizerThrottles(float fl, float fr, float rl, float rr)
    {
        float dt = Time.fixedDeltaTime;
        float step = Mathf.Max(0.01f, roofStabilizerResponseSpeed) * dt;

        _roofThrottleFL = Mathf.MoveTowards(_roofThrottleFL, Mathf.Clamp(fl, 0f, roofStabilizerMaxThrottle), step);
        _roofThrottleFR = Mathf.MoveTowards(_roofThrottleFR, Mathf.Clamp(fr, 0f, roofStabilizerMaxThrottle), step);
        _roofThrottleRL = Mathf.MoveTowards(_roofThrottleRL, Mathf.Clamp(rl, 0f, roofStabilizerMaxThrottle), step);
        _roofThrottleRR = Mathf.MoveTowards(_roofThrottleRR, Mathf.Clamp(rr, 0f, roofStabilizerMaxThrottle), step);

        if (thrusterBus == null)
        {
            return;
        }

        if (roofFL != null) thrusterBus.SetThrottle(roofFL, _roofThrottleFL, ThrusterPowerChannel.Stabilizer);
        if (roofFR != null) thrusterBus.SetThrottle(roofFR, _roofThrottleFR, ThrusterPowerChannel.Stabilizer);
        if (roofRL != null) thrusterBus.SetThrottle(roofRL, _roofThrottleRL, ThrusterPowerChannel.Stabilizer);
        if (roofRR != null) thrusterBus.SetThrottle(roofRR, _roofThrottleRR, ThrusterPowerChannel.Stabilizer);
    }

    // ══════════════════════════════════════════════════════════════
    //  LEAN TARGETS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Update internal lean targets from <see cref="CraftIntent.carveLeanRequest"/>
    /// and <see cref="CraftIntent.pitchLeanRequest"/>. When grounded, lean targets
    /// are driven by input; when airborne, they decay toward zero.
    /// </summary>
    private void UpdateLeanTargets(CraftIntent intent, CraftTelemetry telemetry,
                                   TractionState traction, float dt)
    {
        if (telemetry.isGrounded)
        {
            // ── Roll / carve lean ─────────────────────────────
            float targetRoll = intent.carveLeanRequest
                               * maxPlayerRollTarget
                               * traction.carveLeanMultiplier;

            _leanTargetRoll = Mathf.MoveTowards(
                _leanTargetRoll,
                targetRoll,
                rollResponseSpeed * dt * maxPlayerRollTarget * 2f
            );

            _leanTargetRoll = Mathf.Clamp(_leanTargetRoll, -maxPlayerRollTarget, maxPlayerRollTarget);

            // ── Pitch weight shift ────────────────────────────
            // Mouse Y is treated as a relative weight-shift input. We accumulate
            // a desired pitch command, then smooth the physical lean toward it.
            // This gives pitch its own tuning path instead of depending on hoverKp/hoverKd.
            if (Mathf.Abs(intent.pitchLeanRequest) > 0.001f)
            {
                _pitchCommandTarget += intent.pitchLeanRequest * pitchInputGain;
            }
            else
            {
                _pitchCommandTarget = Mathf.MoveTowards(
                    _pitchCommandTarget,
                    0f,
                    attitudeReturnSpeed * dt * maxPlayerPitchTarget
                );
            }

            _pitchCommandTarget = Mathf.Clamp(_pitchCommandTarget, -maxPlayerPitchTarget, maxPlayerPitchTarget);

            _leanTargetPitch = Mathf.MoveTowards(
                _leanTargetPitch,
                _pitchCommandTarget,
                pitchResponseSpeed * dt * maxPlayerPitchTarget * 2f
            );

            _leanTargetPitch = Mathf.Clamp(_leanTargetPitch, -maxPlayerPitchTarget, maxPlayerPitchTarget);
        }
        else
        {
            // Airborne: automatic hover stabilizer should not keep old grounded
            // banking/pitch targets alive. Airborne attitude is handled by
            // ComputeAirborneAttitude below.
            _leanTargetRoll = Mathf.MoveTowards(
                _leanTargetRoll,
                0f,
                attitudeReturnSpeed * dt * 2f
            );

            _leanTargetPitch = Mathf.MoveTowards(
                _leanTargetPitch,
                0f,
                attitudeReturnSpeed * dt * 2f
            );

            _pitchCommandTarget = Mathf.MoveTowards(
                _pitchCommandTarget,
                0f,
                attitudeReturnSpeed * dt * maxPlayerPitchTarget
            );
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  AIRBORNE ATTITUDE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute hover-thruster differential demands for airborne roll and pitch control.
    /// </summary>
    private void ComputeAirborneAttitude(CraftIntent intent, CraftTelemetry telemetry,
                                         out float rollDemand, out float pitchDemand)
    {
        // ── Roll ──────────────────────────────────────────────
        float mouseRollInput = intent.yawRequest;
        float keyboardRollInput = intent.edgeShiftRequest * airKeyboardRollInfluence;
        float combinedRoll = mouseRollInput + keyboardRollInput;

        float airRollControl = combinedRoll * airRollSensitivity;

        if (invertAirRollControl)
        {
            airRollControl = -airRollControl;
        }

        rollDemand = Mathf.Clamp(airRollControl, -1f, 1f)
                     * maxLeanBias
                     * airRollAuthority;

        // ── Pitch ─────────────────────────────────────────────
        float pitchInput = intent.pitchLeanRequest * airPitchInputGain;

        if (invertAirPitchControl)
        {
            pitchInput = -pitchInput;
        }

        // Use craft-local pitch directly. The older camera-right conversion could
        // collapse toward zero depending on camera orientation, which made pitch
        // feel dead in both ground/air transitions.
        pitchDemand = Mathf.Clamp(pitchInput * airControlSensitivity, -1f, 1f)
                      * maxLeanBias
                      * airPitchAuthority;
    }

    // ══════════════════════════════════════════════════════════════
    //  PER-CORNER HOVER THROTTLE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute and apply hover throttle for a single corner thruster.
    /// Uses a PD controller when grounded, airborne attitude differentials,
    /// oscillation, jump bursts, and exponential response smoothing.
    /// </summary>
    private void SetHoverThrottle(
        ThrusterNode node,
        ref float smoothedThrottle,
        float rollDemand,
        float pitchDemand,
        bool isLeft,
        bool isFront,
        bool jumping,
        CraftTelemetry telemetry)
    {
        if (node == null) return;

        float throttle = 0f;

        // ── Grounded PD hover ─────────────────────────────────
        bool hoverCushionActive = false;

        if (node.IsGrounded)
        {
            float targetHeight = GetCornerHoverTargetHeight(isLeft, isFront, telemetry);
            float hoverInfluence = GetHoverCushionInfluence(node.GroundDistance, targetHeight);

            hoverCushionActive = hoverInfluence > 0.001f;

            if (hoverCushionActive)
            {
                float heightError = targetHeight - node.GroundDistance;

                // Point velocity measured along the ground normal.
                // Negative = falling into the hover cushion.
                // Positive = rebounding upward away from the ground.
                Vector3 pointVel = rb.GetPointVelocity(node.transform.position);
                float vertVel = Vector3.Dot(pointVel, node.GroundNormal);

                float effectiveKd = GetEffectiveHoverDamping(vertVel);

                throttle = _gravityThrottle
                           + heightError * hoverKp
                           - vertVel * effectiveKd;

                // Kill lingering lift while the craft is already moving upward near/above target height.
                // This is the key anti-pogo behavior: once the suspension has launched the craft upward,
                // the hover thrusters must stop adding energy and let gravity settle it back down.
                if (cutThrottleOnRebound && vertVel > reboundThrottleCutVelocity && heightError <= reboundThrottleCutHeightError)
                {
                    float rebound01 = Mathf.InverseLerp(
                        reboundThrottleCutVelocity,
                        Mathf.Max(reboundThrottleCutVelocity + 0.01f, reboundFullCutVelocity),
                        vertVel
                    );

                    throttle *= 1f - rebound01;
                }

                // Decouple raw raycast distance from lift: long ground probes may see the floor,
                // but actual hover force only fades in inside the hover cushion.
                throttle *= hoverInfluence;

                // Oscillation for living feel. Fade it during fast vertical motion and near the cushion edge
                // so it cannot feed bounce energy.
                float oscillationFade = Mathf.Clamp01(1f - Mathf.Abs(vertVel) / Mathf.Max(0.01f, hardLandingSpeedForMaxDamping));
                throttle += GetHoverOscillation(node, isLeft, isFront) * oscillationFade * hoverInfluence;
            }
        }

        // ── Airborne attitude differential ────────────────────
        // Left thrusters push right-roll; front thrusters push nose-down pitch.
        float rollAdjust = isLeft ? rollDemand : -rollDemand;
        float pitchAdjust = isFront ? -pitchDemand : pitchDemand;

        throttle += rollAdjust + pitchAdjust;

        // ── Jump burst override ───────────────────────────────
        if (jumping)
        {
            throttle = jumpBurstMultiplier;
        }

        // ── Clamp ─────────────────────────────────────────────
        float maxAllowed = jumping ? jumpBurstMultiplier : hoverMaxThrottle;
        throttle = Mathf.Clamp(throttle, 0f, maxAllowed);

        // ── Exponential response smoothing ────────────────────
        bool hasAirAttitudeDemand = Mathf.Abs(rollDemand) > 0.001f || Mathf.Abs(pitchDemand) > 0.001f;
        throttle = SmoothHoverThrottle(node, smoothedThrottle, throttle, jumping, hoverCushionActive || hasAirAttitudeDemand);
        smoothedThrottle = throttle;

        // ── Route to bus ──────────────────────────────────────
        thrusterBus.SetThrottle(node, throttle, ThrusterPowerChannel.BaseHover);
    }

    /// <summary>
    /// Returns how much the hover cushion should affect this corner based on distance.
    /// Raw groundDetectionRange can be very long for preview/telemetry, but lift only
    /// exists inside this short active cushion around the target hover height.
    /// </summary>
    private float GetHoverCushionInfluence(float groundDistance, float targetHeight)
    {
        float fullPowerDistance = targetHeight + Mathf.Max(0f, hoverFullPowerHeightBuffer);
        float activationDistance = targetHeight + Mathf.Max(hoverFullPowerHeightBuffer + 0.01f, hoverActivationHeightBuffer);

        if (groundDistance > activationDistance)
        {
            return 0f;
        }

        if (!fadeHoverForcesNearActivationEdge || groundDistance <= fullPowerDistance)
        {
            return 1f;
        }

        float linear = 1f - Mathf.InverseLerp(fullPowerDistance, activationDistance, groundDistance);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(linear));
    }

    /// <summary>
    /// Returns a velocity-aware damping value for the hover PD controller.
    /// Falling gets extra damping to absorb impact; rebounding gets extra damping
    /// to stop the craft from pogoing higher after landing.
    /// </summary>
    private float GetEffectiveHoverDamping(float verticalVelocityAwayFromGround)
    {
        float multiplier;

        if (verticalVelocityAwayFromGround < 0f)
        {
            float hardLanding01 = Mathf.Clamp01(
                -verticalVelocityAwayFromGround / Mathf.Max(0.01f, hardLandingSpeedForMaxDamping)
            );

            multiplier = landingDampingMultiplier + hardLandingExtraDamping * hardLanding01;
        }
        else
        {
            multiplier = reboundDampingMultiplier;
        }

        return hoverKd * Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// Smooth hover throttle asymmetrically: normal rise, fast fall, and very fast
    /// airborne decay. This prevents jump burst / landing lift from lingering and
    /// injecting extra energy into the next bounce.
    /// </summary>
    private float SmoothHoverThrottle(ThrusterNode node, float currentThrottle, float targetThrottle, bool jumping, bool forceContextActive)
    {
        if (jumping && jumpBypassesHoverSmoothing)
        {
            return targetThrottle;
        }

        bool targetIsRising = targetThrottle > currentThrottle;
        float response;

        if (!forceContextActive)
        {
            // We may still have a long ground ray hit, but if the craft is outside the active
            // hover cushion, treat it as airborne for throttle decay. This prevents the
            // invisible autopilot / long-range landing-path feeling.
            response = targetIsRising ? hoverThrusterResponseSpeed : airborneHoverThrottleDecaySpeed;
        }
        else if (!node.IsGrounded)
        {
            response = targetIsRising ? hoverThrusterResponseSpeed : airborneHoverThrottleDecaySpeed;
        }
        else
        {
            response = targetIsRising ? hoverThrusterResponseSpeed : hoverThrottleFallResponseSpeed;
        }

        response = Mathf.Max(0.01f, response);
        float t = 1f - Mathf.Exp(-response * Time.fixedDeltaTime);

        return Mathf.Lerp(currentThrottle, targetThrottle, t);
    }

    // ══════════════════════════════════════════════════════════════
    //  CORNER TARGET HEIGHT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the desired hover height for a specific corner, incorporating
    /// intentional carve/lean offsets and auto-leveling corrections.
    /// </summary>
    private float GetCornerHoverTargetHeight(bool isLeft, bool isFront,
                                              CraftTelemetry telemetry)
    {
        float targetHeight = hoverHeight;

        // ── Normalized player attitude amounts ────────────────
        float inputRoll01 = maxPlayerRollTarget > 0f
            ? Mathf.Clamp(_leanTargetRoll / maxPlayerRollTarget, -1f, 1f)
            : 0f;

        float inputPitch01 = maxPlayerPitchTarget > 0f
            ? Mathf.Clamp(_leanTargetPitch / maxPlayerPitchTarget, -1f, 1f)
            : 0f;

        // ── Intentional player offsets ────────────────────────
        float intentionalRollOffset = inputRoll01 * playerRollHeightOffset;
        float intentionalPitchOffset = inputPitch01 * playerPitchHeightOffset;

        // ── Auto-level suppression ────────────────────────────
        // This is the important handling fix: auto-level helps when the player
        // lets go, but backs off while the player is actively commanding pitch/roll.
        float rollAutoLevelScale = 1f - Mathf.Abs(inputRoll01) * autoLevelSuppressionByRollInput;
        float pitchAutoLevelScale = 1f - Mathf.Abs(inputPitch01) * autoLevelSuppressionByPitchInput;

        rollAutoLevelScale = Mathf.Clamp01(rollAutoLevelScale);
        pitchAutoLevelScale = Mathf.Clamp01(pitchAutoLevelScale);

        // ── Auto-leveling offsets ─────────────────────────────
        float autoRollOffset = Mathf.Clamp(
            telemetry.rollAngle * autoLevelRollStrength * rollAutoLevelScale,
            -autoLevelMaxHeightOffset,
            autoLevelMaxHeightOffset
        );

        float autoPitchOffset = Mathf.Clamp(
            telemetry.pitchAngle * autoLevelPitchStrength * pitchAutoLevelScale,
            -autoLevelMaxHeightOffset,
            autoLevelMaxHeightOffset
        );

        if (invertGroundedRollHeightOffset)  autoRollOffset = -autoRollOffset;
        if (invertGroundedPitchHeightOffset) autoPitchOffset = -autoPitchOffset;

        float rollOffset = intentionalRollOffset + autoRollOffset;
        float pitchOffset = intentionalPitchOffset + autoPitchOffset;

        // Left side gets positive roll offset; right side gets negative.
        targetHeight += isLeft ? rollOffset : -rollOffset;

        // Front gets positive pitch offset; rear gets negative.
        targetHeight += isFront ? pitchOffset : -pitchOffset;

        return Mathf.Max(0.05f, targetHeight);
    }

    // ══════════════════════════════════════════════════════════════
    //  OSCILLATION
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a gentle sine-wave oscillation offset for a given corner,
    /// with per-corner phase spread so the craft breathes organically.
    /// </summary>
    private float GetHoverOscillation(ThrusterNode node, bool isLeft, bool isFront)
    {
        if (hoverOscillationAmount <= 0f) return 0f;

        // Phase indices: FL=0, FR=1, RL=2, RR=3.
        int cornerIndex = (isLeft ? 0 : 1) + (isFront ? 0 : 2);
        float phase = cornerIndex * hoverOscillationPhaseSpread;

        float wave = Mathf.Sin(Time.time * hoverOscillationSpeed + phase);
        return wave * hoverOscillationAmount;
    }

    // ══════════════════════════════════════════════════════════════
    //  JUMP
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle jump initiation: if the pilot wants to jump, the cooldown is clear,
    /// and the craft is grounded, start a burst.
    /// </summary>
    private void HandleJump(CraftIntent intent, CraftTelemetry telemetry)
    {
        if (!intent.wantsJump) return;

        if (_jumpCooldownRemaining <= 0f && telemetry.isGrounded)
        {
            _jumpBurstRemaining = jumpBurstDuration;
            _jumpCooldownRemaining = jumpCooldown;
        }
    }
}
