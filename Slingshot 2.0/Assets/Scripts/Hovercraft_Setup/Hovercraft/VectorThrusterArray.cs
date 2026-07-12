using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Steering and vectoring thruster array for the hovercraft.
/// Controls the four strafe thrusters (FL, FR, BL, BR) to produce yaw steering
/// and lateral translation, plus applies yaw damping torque directly to the rigidbody.
///
/// <para>
/// Mouse X (<see cref="CraftIntent.yawRequest"/>) is the PRIMARY steering source.
/// A/D (<see cref="CraftIntent.edgeShiftRequest"/>) is edge-shift / body commitment only by default.
/// It does NOT steer unless the optional assist toggles below are explicitly enabled.
/// </para>
///
/// <para>
/// This is a pure physics module — it does not read input directly.
/// <see cref="CraftCore"/> calls <see cref="ApplyVectoring"/> once per FixedUpdate.
/// </para>
/// </summary>
public class VectorThrusterArray : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  REFERENCES  (set by CraftCore)
    // ══════════════════════════════════════════════════════════════

    /// <summary>The craft's Rigidbody, assigned by CraftCore during setup.</summary>
    [HideInInspector] public Rigidbody rb;

    /// <summary>The central thruster bus for routing throttle commands.</summary>
    [HideInInspector] public ThrusterBus thrusterBus;

    /// <summary>Front-left strafe thruster node.</summary>
    [HideInInspector] public ThrusterNode strafeFL;

    /// <summary>Front-right strafe thruster node.</summary>
    [HideInInspector] public ThrusterNode strafeFR;

    /// <summary>Back-left strafe thruster node.</summary>
    [HideInInspector] public ThrusterNode strafeBL;

    /// <summary>Back-right strafe thruster node.</summary>
    [HideInInspector] public ThrusterNode strafeBR;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — STRAFE / STEER
    // ══════════════════════════════════════════════════════════════

    [Header("Mouse Steering")]
    [Tooltip("Yaw steering sensitivity from Mouse X (yawRequest). This IS primary steering.")]
    [Range(0f, 2f)]
    public float steerSensitivity = 0.85f;

    [Header("Primary Steering Authority")]
    [Tooltip("Main yaw authority multiplier. If the craft rotates too slowly, tune this first.")]
    [FormerlySerializedAs("yawAuthorityMultiplier")]
    [Range(0f, 6f)]
    public float steeringYawAuthority = 2.25f;

    [Header("Optional Keyboard Assist — OFF by default")]
    [Tooltip("If false, A/D edge shift never adds yaw. Keep this false when mouse should be the only steering input.")]
    public bool edgeShiftCanAssistYaw = false;

    [Tooltip("If false, A/D edge shift does not fire side/strafe thrusters. A/D will still bank/edge the craft through HoverStabilizerArray.")]
    public bool edgeShiftCanFireStrafeThrusters = false;

    [Tooltip("Optional yaw assist from carve/edge lean. Only used when edgeShiftCanAssistYaw is true.")]
    [Range(0f, 2f)]
    public float carveYawAssistFromLean = 0.35f;

    [Tooltip("Optional lateral assist from A/D. Only used when edgeShiftCanFireStrafeThrusters is true.")]
    [FormerlySerializedAs("lateralAssistAuthorityMultiplier")]
    [Range(0f, 4f)]
    public float lateralAssistAuthority = 1.0f;

    [Tooltip("Optional lateral assist sensitivity from A/D. Only used when edgeShiftCanFireStrafeThrusters is true.")]
    [Range(0f, 2f)]
    public float strafeSensitivity = 0.75f;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — ANTI-FLIP SAFETY
    // ══════════════════════════════════════════════════════════════

    [Header("Anti-Flip Safety")]
    [Tooltip("Tilt angle (degrees) at which strafe/yaw thrusters begin reducing output.")]
    public float strafeSafetyStartAngle = 18f;

    [Tooltip("Tilt angle (degrees) at which strafe/yaw thrusters are heavily reduced.")]
    public float strafeSafetyFullAngle = 42f;

    [Tooltip("Minimum strafe/yaw authority when the craft is dangerously tilted.")]
    [Range(0f, 1f)]
    public float minimumStrafeAuthorityWhenTilted = 0.2f;

    [Tooltip("Roll rate (degrees/sec) at which strafe/yaw thrusters begin reducing output.")]
    public float strafeSafetyStartRollRate = 120f;

    [Tooltip("Roll rate (degrees/sec) at which strafe/yaw thrusters are heavily reduced.")]
    public float strafeSafetyFullRollRate = 300f;

    // ══════════════════════════════════════════════════════════════
    //  TUNING — YAW DAMPING
    // ══════════════════════════════════════════════════════════════

    [Header("Yaw Damping")]
    [Tooltip("Base yaw damping torque coefficient.")]
    public float yawDamping = 2.75f;

    [Tooltip("How much yaw damping is reduced when grip breaker (drift) is active. 0 = no damping in drift; 1 = full damping in drift.")]
    [Range(0f, 1f)]
    public float gripBreakerYawDampingMultiplier = 0.35f;

    // ══════════════════════════════════════════════════════════════
    //  MAIN ENTRY POINT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluate and apply steering/vectoring forces for this physics tick.
    /// Called once per FixedUpdate by CraftCore.
    /// </summary>
    /// <param name="intent">Processed pilot intent for this frame.</param>
    /// <param name="telemetry">Current craft telemetry snapshot.</param>
    /// <param name="traction">Current traction/grip state from TractionCore.</param>
    public void ApplyVectoring(CraftIntent intent, CraftTelemetry telemetry,
                               TractionState traction)
    {
        ApplyDifferentialStrafe(intent, telemetry, traction);
        ApplyYawDamping(traction);
    }

    // ══════════════════════════════════════════════════════════════
    //  DIFFERENTIAL STRAFE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute per-corner strafe thruster demands from yaw and edge-shift requests,
    /// apply anti-flip safety scaling, and route throttle values through the bus.
    /// </summary>
    private void ApplyDifferentialStrafe(CraftIntent intent, CraftTelemetry telemetry,
                                         TractionState traction)
    {
        // Mouse X is the only steering source by default.
        // A/D remains edge-shift / body commitment through HoverStabilizerArray.
        float edgeDemand = edgeShiftCanFireStrafeThrusters
            ? intent.edgeShiftRequest * lateralAssistAuthority
            : 0f;

        float yawDemand = intent.yawRequest * steeringYawAuthority;

        if (edgeShiftCanAssistYaw)
        {
            yawDemand += intent.carveLeanRequest * carveYawAssistFromLean;
        }

        // Steering sensitivity boosted by traction state drift multiplier.
        float activeSteer = steerSensitivity * traction.steeringMultiplier;

        // Anti-flip stability reduction.
        float stabilityScale = GetStrafeStabilityScale(telemetry);

        // ── Per-corner differential demands ───────────────────
        //
        // FL fires RIGHT  → positive edgeDemand = rightward strafe.
        // FR fires LEFT   → negative edgeDemand = leftward strafe.
        //
        // Yaw:
        //   FL + BR fire together → clockwise yaw (positive yaw demand).
        //   FR + BL fire together → counter-clockwise yaw (negative yaw demand).

        float flDemand = Mathf.Max(0f, edgeDemand) * strafeSensitivity
                       + Mathf.Max(0f, yawDemand) * activeSteer;

        float frDemand = Mathf.Max(0f, -edgeDemand) * strafeSensitivity
                       + Mathf.Max(0f, -yawDemand) * activeSteer;

        float blDemand = Mathf.Max(0f, edgeDemand) * strafeSensitivity
                       + Mathf.Max(0f, -yawDemand) * activeSteer;

        float brDemand = Mathf.Max(0f, -edgeDemand) * strafeSensitivity
                       + Mathf.Max(0f, yawDemand) * activeSteer;

        // ── Apply stability scale ──────────────────────────────
        // EnergyCore scaling happens later in ThrusterBus, based on the actual
        // thruster hardware power draw.
        flDemand *= stabilityScale;
        frDemand *= stabilityScale;
        blDemand *= stabilityScale;
        brDemand *= stabilityScale;

        // ── Cap at combined maximum ───────────────────────────
        float cap = ((edgeShiftCanFireStrafeThrusters ? strafeSensitivity : 0f) + activeSteer) * stabilityScale;

        flDemand = Mathf.Min(flDemand, cap);
        frDemand = Mathf.Min(frDemand, cap);
        blDemand = Mathf.Min(blDemand, cap);
        brDemand = Mathf.Min(brDemand, cap);

        // ── Route to bus ──────────────────────────────────────
        if (strafeFL != null) thrusterBus.SetThrottle(strafeFL, flDemand, ThrusterPowerChannel.Vectoring);
        if (strafeFR != null) thrusterBus.SetThrottle(strafeFR, frDemand, ThrusterPowerChannel.Vectoring);
        if (strafeBL != null) thrusterBus.SetThrottle(strafeBL, blDemand, ThrusterPowerChannel.Vectoring);
        if (strafeBR != null) thrusterBus.SetThrottle(strafeBR, brDemand, ThrusterPowerChannel.Vectoring);
    }

    // ══════════════════════════════════════════════════════════════
    //  ANTI-FLIP SAFETY
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute a 0–1 stability scale factor that reduces strafe/yaw authority
    /// when the craft is dangerously tilted or rolling fast. Returns 1.0 when
    /// airborne (no safety reduction needed).
    /// </summary>
    private float GetStrafeStabilityScale(CraftTelemetry telemetry)
    {
        if (!telemetry.isGrounded) return 1f;

        // ── Tilt angle based reduction ────────────────────────
        float maxTilt = Mathf.Max(
            Mathf.Abs(telemetry.rollAngle),
            Mathf.Abs(telemetry.pitchAngle)
        );

        float tilt01 = Mathf.InverseLerp(strafeSafetyStartAngle, strafeSafetyFullAngle, maxTilt);

        // ── Roll rate based reduction ─────────────────────────
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);
        float rollRateDegrees = Mathf.Abs(localAngVel.z * Mathf.Rad2Deg);

        float rollRate01 = Mathf.InverseLerp(
            strafeSafetyStartRollRate,
            strafeSafetyFullRollRate,
            rollRateDegrees
        );

        // ── Combined safety factor ────────────────────────────
        float safety01 = Mathf.Clamp01(Mathf.Max(tilt01, rollRate01));

        return Mathf.Lerp(1f, minimumStrafeAuthorityWhenTilted, safety01);
    }

    // ══════════════════════════════════════════════════════════════
    //  YAW DAMPING
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply angular damping torque around the local yaw axis to prevent
    /// uncontrolled spinning. Damping is reduced during drift based on
    /// <see cref="TractionState.gripBreakerAmount"/>.
    /// </summary>
    private void ApplyYawDamping(TractionState traction)
    {
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);

        float activeYawDamping = yawDamping * Mathf.Lerp(
            1f,
            gripBreakerYawDampingMultiplier,
            traction.gripBreakerAmount
        );

        Vector3 yawDampTorque = -transform.up * localAngVel.y * activeYawDamping;

        rb.AddTorque(yawDampTorque, ForceMode.Acceleration);
    }
}
