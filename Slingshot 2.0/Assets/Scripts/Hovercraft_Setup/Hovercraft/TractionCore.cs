using UnityEngine;

/// <summary>
/// Grip, carve, and drift brain for the hovercraft.
/// Manages the transition between normal grip and drift profiles, computes the
/// <see cref="TractionState"/> output used by other subsystems, and applies
/// lateral/longitudinal grip forces to the rigidbody.
///
/// <para>
/// The module is split into two methods called at different pipeline stages:
/// <list type="bullet">
///   <item><see cref="EvaluateTraction"/> — updates grip blend and outputs <see cref="CurrentTraction"/>.</item>
///   <item><see cref="ApplyTractionForces"/> — applies grip forces to the rigidbody.</item>
/// </list>
/// </para>
///
/// <para>
/// This is a pure physics module — it does not read input directly.
/// <see cref="CraftCore"/> calls both methods each FixedUpdate.
/// </para>
/// </summary>
public class TractionCore : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  REFERENCES  (set by CraftCore)
    // ══════════════════════════════════════════════════════════════

    /// <summary>The craft's Rigidbody, assigned by CraftCore during setup.</summary>
    [HideInInspector] public Rigidbody rb;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — NORMAL GRIP PROFILE
    // ══════════════════════════════════════════════════════════════

    [Header("Grip — Normal Profile")]
    [Tooltip("Sideways traction. Higher = less sideways drift.")]
    public float lateralGrip = 6.5f;

    [Tooltip("Counter-trajectory traction when thrusting against current motion direction.")]
    public float longitudinalGrip = 5.5f;

    [Tooltip("Forward/backward coasting drag when no throttle/brake is pressed.")]
    public float coastingGrip = 0.3f;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — DRIFT (GRIP BREAKER) PROFILE
    // ══════════════════════════════════════════════════════════════

    [Header("Grip Breaker — Drift Profile")]
    [Tooltip("Lateral grip when fully drifting.")]
    public float lateralGripBreaker = 0.28f;

    [Tooltip("Longitudinal grip when fully drifting.")]
    public float longitudinalGripBreaker = 0.18f;

    [Tooltip("Coasting grip when fully drifting.")]
    public float coastingGripBreaker = 0.05f;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — GRIP TRANSITION
    // ══════════════════════════════════════════════════════════════

    [Header("Grip Transition")]
    [Tooltip("How fast grip breaker transitions from normal to drift while held (units/sec).")]
    public float gripBreakRate = 8f;

    [Tooltip("How fast grip recovers after releasing grip breaker (units/sec).")]
    public float gripRecoveryRate = 5f;

    [Tooltip("Recovery speed penalty per unit of velocity.")]
    public float gripRecoverySpeedPenalty = 0.035f;

    [Tooltip("Exponent for non-linear speed-based recovery penalty.")]
    public float gripRecoveryExponent = 1.6f;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — GRIP MODIFIERS
    // ══════════════════════════════════════════════════════════════

    [Header("Grip Modifiers")]
    [Tooltip("Minimum local speed before counter-trajectory grip activates.")]
    public float counterTrajectoryMinSpeed = 0.4f;

    [Tooltip("Extra multiplier for grip when thrusting against current trajectory.")]
    public float counterTrajectoryGripMultiplier = 1.0f;

    [Tooltip("Yaw steering sensitivity multiplier when grip breaker is fully active.")]
    [Range(0f, 3f)]
    public float gripBreakerSteerMultiplier = 1.3f;

    [Tooltip("Maximum grip acceleration to prevent violent snap-back at high speed.")]
    public float maxGripAcceleration = 105f;

    [Tooltip("How much lateral slide energy becomes forward drive during grip recovery.")]
    [Range(0f, 1f)]
    public float driftSpeedConservation = 0.2f;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — CARVE BITE
    // ══════════════════════════════════════════════════════════════

    [Header("Carve Bite")]
    [Tooltip("How much carve bite in normal (non-drift) mode. 1 = full carve response.")]
    public float carveBiteNormal = 1.0f;

    [Tooltip("How much carve bite when grip breaker (drift) is fully active.")]
    public float carveBiteGripBreaker = 0.2f;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC READ STATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>The computed traction state for the current frame, consumed by other subsystems.</summary>
    public TractionState CurrentTraction { get; private set; }

    /// <summary>True if grip breaker input is currently held.</summary>
    public bool IsGripBroken { get; private set; }

    /// <summary>True if grip is recovering (breaker released but blend > 0).</summary>
    public bool IsRecoveringGrip => !IsGripBroken && _gripBreak01 > 0.01f;

    /// <summary>Raw grip breaker blend value. 0 = normal, 1 = full drift.</summary>
    public float GripBreakerAmount => _gripBreak01;

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>Linear 0–1 grip breaker blend before smoothstep shaping.</summary>
    private float _gripBreak01;

    /// <summary>Current blended lateral grip value.</summary>
    private float _currentLateralGrip;

    /// <summary>Current blended longitudinal grip value.</summary>
    private float _currentLongitudinalGrip;

    /// <summary>Current blended coasting grip value.</summary>
    private float _currentCoastingGrip;

    /// <summary>Cached throttle from intent for use in ApplyTractionForces.</summary>
    private float _cachedThrottle;

    /// <summary>Cached brake from intent for use in ApplyTractionForces.</summary>
    private float _cachedBrake;

    /// <summary>Cached grip-breaker-held flag from command.</summary>
    private bool _cachedGripBreakerHeld;

    // ══════════════════════════════════════════════════════════════
    //  INITIALIZATION
    // ══════════════════════════════════════════════════════════════

    private void Start()
    {
        _gripBreak01 = 0f;
        _currentLateralGrip = lateralGrip;
        _currentLongitudinalGrip = longitudinalGrip;
        _currentCoastingGrip = coastingGrip;
    }

    // ══════════════════════════════════════════════════════════════
    //  STAGE 1: EVALUATE TRACTION
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Update the grip breaker blend, compute blended grip values, and produce
    /// the <see cref="TractionState"/> output. Called early in the FixedUpdate
    /// pipeline so that downstream systems have access to the current traction state.
    /// </summary>
    /// <param name="command">Raw pilot command (contains gripBreakerHeld).</param>
    /// <param name="intent">Processed pilot intent (contains throttle/brake for caching).</param>
    /// <param name="telemetry">Current craft telemetry snapshot.</param>
    public void EvaluateTraction(PilotCommand command, CraftIntent intent,
                                 CraftTelemetry telemetry)
    {
        float dt = Time.fixedDeltaTime;
        float speed = telemetry.speed;

        // ── Cache intent values for ApplyTractionForces ───────
        IsGripBroken = command.gripBreakerHeld;
        _cachedGripBreakerHeld = command.gripBreakerHeld;
        _cachedThrottle = intent.throttle;
        _cachedBrake = intent.brake;

        // ── Update grip breaker blend ─────────────────────────
        if (_cachedGripBreakerHeld)
        {
            _gripBreak01 = Mathf.MoveTowards(
                _gripBreak01,
                1f,
                gripBreakRate * dt
            );
        }
        else
        {
            float speedPenalty = speed * gripRecoverySpeedPenalty;
            float divisor = 1f + Mathf.Pow(speedPenalty, gripRecoveryExponent);
            float recoveryRate = gripRecoveryRate / divisor;

            _gripBreak01 = Mathf.MoveTowards(
                _gripBreak01,
                0f,
                recoveryRate * dt
            );
        }

        // ── Shaped blend (smoothstep for natural transitions) ─
        float shapedBlend = Smooth01(_gripBreak01);

        // ── Blended grip values ───────────────────────────────
        _currentLateralGrip = Mathf.Lerp(lateralGrip, lateralGripBreaker, shapedBlend);
        _currentLongitudinalGrip = Mathf.Lerp(longitudinalGrip, longitudinalGripBreaker, shapedBlend);
        _currentCoastingGrip = Mathf.Lerp(coastingGrip, coastingGripBreaker, shapedBlend);

        // ── Build TractionState output ────────────────────────
        CurrentTraction = new TractionState
        {
            gripBreakerAmount   = _gripBreak01,
            lateralGrip         = _currentLateralGrip,
            longitudinalGrip    = _currentLongitudinalGrip,
            coastingGrip        = _currentCoastingGrip,
            carveBite           = Mathf.Lerp(carveBiteNormal, carveBiteGripBreaker, shapedBlend),
            yawFreedom          = shapedBlend,
            steeringMultiplier  = Mathf.Lerp(1f, gripBreakerSteerMultiplier, shapedBlend),
            carveLeanMultiplier = Mathf.Lerp(1f, 0.3f, shapedBlend),
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  STAGE 2: APPLY TRACTION FORCES
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply grip (traction) forces to the rigidbody. Called late in the
    /// FixedUpdate pipeline after thrusters have been applied, so grip forces
    /// can counteract residual slide.
    /// </summary>
    /// <param name="telemetry">Current craft telemetry snapshot.</param>
    public void ApplyTractionForces(CraftTelemetry telemetry)
    {
        // Only apply grip when grounded.
        if (!telemetry.isGrounded) return;

        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);

        // ── Lateral grip ──────────────────────────────────────
        float lateralForce = -localVel.x * _currentLateralGrip;

        // ── Longitudinal grip ─────────────────────────────────
        float longitudinalForce = 0f;

        float throttleAbs = Mathf.Clamp01(Mathf.Abs(_cachedThrottle) + Mathf.Abs(_cachedBrake));
        bool hasThrottleInput = throttleAbs > 0.05f;

        if (!hasThrottleInput)
        {
            // Coasting drag: slow the craft when no throttle/brake.
            longitudinalForce += -localVel.z * _currentCoastingGrip;
        }
        else
        {
            // Counter-trajectory grip: extra traction when thrusting
            // against the current direction of travel.
            float netThrust = _cachedThrottle - _cachedBrake;

            bool movingForward  = localVel.z > counterTrajectoryMinSpeed;
            bool movingBackward = localVel.z < -counterTrajectoryMinSpeed;
            bool thrustingForward  = netThrust > 0.05f;
            bool thrustingBackward = netThrust < -0.05f;

            bool thrustAgainstTrajectory =
                (thrustingForward && movingBackward) ||
                (thrustingBackward && movingForward);

            if (thrustAgainstTrajectory)
            {
                longitudinalForce += -localVel.z
                                     * _currentLongitudinalGrip
                                     * counterTrajectoryGripMultiplier;
            }
        }

        // ── Drift speed conservation ─────────────────────────
        // During grip recovery, redirect some lateral slide energy into forward drive
        // so the craft doesn't lose all its speed exiting a drift.
        if (!_cachedGripBreakerHeld && _gripBreak01 > 0.01f && Mathf.Abs(localVel.x) > 0.05f)
        {
            float lateralDecel = Mathf.Abs(localVel.x) * _currentLateralGrip;
            float direction = localVel.z >= 0f ? 1f : -1f;

            longitudinalForce += direction * lateralDecel * driftSpeedConservation;
        }

        // ── Clamp to max grip acceleration ────────────────────
        if (maxGripAcceleration > 0f)
        {
            lateralForce = Mathf.Clamp(lateralForce, -maxGripAcceleration, maxGripAcceleration);
            longitudinalForce = Mathf.Clamp(longitudinalForce, -maxGripAcceleration, maxGripAcceleration);
        }

        // ── Apply as world-space acceleration ─────────────────
        Vector3 localGripForce = new Vector3(lateralForce, 0f, longitudinalForce);
        Vector3 worldGripForce = transform.TransformDirection(localGripForce);

        rb.AddForce(worldGripForce, ForceMode.Acceleration);
    }

    // ══════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Hermite smoothstep: smooth 0→1 ramp with zero-derivative at both ends.
    /// Produces more natural-feeling grip transitions than a linear blend.
    /// </summary>
    /// <param name="value">Input value, clamped to [0, 1].</param>
    /// <returns>Smoothed value in [0, 1].</returns>
    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
