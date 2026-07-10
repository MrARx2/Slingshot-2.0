using UnityEngine;

/// <summary>
/// A single physical thruster mounted on the craft hull (V2).
/// <para>
/// The thruster fires along a configurable local axis. Default is <c>-transform.up</c>
/// for V1 prefab compatibility, because the original Thruster used negative local Y
/// as its force direction.
/// </para>
/// <para>
/// This component is intentionally "dumb". It knows nothing about hover height
/// targets, steering curves, grip, or visual effects. Higher-level systems
/// (via <see cref="ThrusterBus"/>) set <see cref="Throttle"/> each physics
/// frame, and this component applies the resulting force at its own world
/// position using <c>Rigidbody.AddForceAtPosition</c>.
/// </para>
/// </summary>
public class ThrusterNode : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  ENUMS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Defines the functional role of a thruster on the craft.
    /// Used by <see cref="ThrusterBus"/> to categorize nodes into
    /// hover, propulsion, and strafe groups.
    /// </summary>
    public enum ThrusterRole
    {
        /// <summary>Bottom-facing thruster that keeps the craft airborne.</summary>
        Hover,

        /// <summary>Rear-facing thruster providing forward propulsion.</summary>
        Main,

        /// <summary>Front-facing thruster providing braking or reverse thrust.</summary>
        Brake,

        /// <summary>Side-facing thruster providing lateral movement.</summary>
        Strafe,

        /// <summary>Roof-mounted thruster that pushes the craft downward / into surfaces.</summary>
        Roof
    }

    /// <summary>
    /// Local transform axis used as the actual force direction.
    /// NegativeY preserves the V1 convention where thrusters pushed along -transform.up.
    /// </summary>
    public enum LocalThrustAxis
    {
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ,
        PositiveX,
        NegativeX
    }

    // ══════════════════════════════════════════════════════════════
    //  CONFIGURATION
    // ══════════════════════════════════════════════════════════════

    [Header("Thruster Setup")]
    [Tooltip("What role this thruster plays on the craft.")]
    public ThrusterRole role = ThrusterRole.Hover;

    [Tooltip("Maximum force this thruster can output (applied as Acceleration).")]
    public float maxForce = 100f;

    [Header("Energy / Hardware")]
    [Tooltip("How efficiently this thruster converts reactor power into force. Higher = less power draw for the same maxForce. Keep at 1 for default parts.")]
    [Min(0.01f)] public float efficiency = 1f;

    [Tooltip("Additional per-part energy draw multiplier. Use this for heavy/cheap parts that are strong but wasteful, or premium parts that are efficient.")]
    [Min(0f)] public float powerDrawMultiplier = 1f;

    [Tooltip("Local axis this node pushes along. Default NegativeY matches the original V1 thruster convention (-transform.up).")]
    public LocalThrustAxis thrustAxis = LocalThrustAxis.NegativeY;

    [Tooltip("Human-readable name for debugging and label-based lookup.")]
    public string label;

    [Header("Ground Detection (Hover only)")]
    [Tooltip("How far this node can see the ground. This is only a probe range; actual hover lift is limited by HoverStabilizerArray hover cushion settings.")]
    public float groundDetectionRange = 4f;

    // ══════════════════════════════════════════════════════════════
    //  RUNTIME STATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Current throttle level, typically 0–1 but can exceed 1 for burst/jump.
    /// <para>Set by the controller layer (via <see cref="ThrusterBus"/>)
    /// each FixedUpdate before calling <see cref="ApplyThrust"/>.</para>
    /// </summary>
    public float Throttle { get; set; }

    /// <summary>
    /// True if this hover thruster's most recent ground raycast detected a
    /// surface within <see cref="groundDetectionRange"/>.
    /// Always false for non-hover roles.
    /// </summary>
    public bool IsGrounded { get; private set; }

    /// <summary>
    /// Distance to the ground surface from this thruster's position.
    /// Negative (<c>-1</c>) if no ground was detected.
    /// </summary>
    public float GroundDistance { get; private set; }

    /// <summary>
    /// Surface normal at the ground hit point.
    /// Defaults to <c>Vector3.up</c> when no ground is detected.
    /// </summary>
    public Vector3 GroundNormal { get; private set; }

    /// <summary>
    /// The world-space force vector that was applied during the most recent
    /// call to <see cref="ApplyThrust"/>. Zero if no force was applied.
    /// </summary>
    public Vector3 LastAppliedForce { get; private set; }

    /// <summary>
    /// The direction this thruster pushes the craft, defined by <see cref="thrustAxis"/>.
    /// </summary>
    public Vector3 ThrustDirection => GetLocalAxis(thrustAxis);

    /// <summary>
    /// Hover ground sensing direction. Usually opposite of thrust, so a hover
    /// thruster that pushes up will raycast down.
    /// </summary>
    public Vector3 GroundRayDirection => -ThrustDirection;

    // ══════════════════════════════════════════════════════════════
    //  INTERNAL
    // ══════════════════════════════════════════════════════════════

    private Rigidbody _rb;

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>();

        if (_rb == null)
        {
            Debug.LogError(
                $"[ThrusterNode] '{gameObject.name}' could not find a Rigidbody in parent hierarchy. " +
                "Ensure this thruster is a child of a GameObject with a Rigidbody.",
                this
            );
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cast a ground-detection ray from this thruster's position opposite
    /// <see cref="ThrustDirection"/>. Updates <see cref="IsGrounded"/>,
    /// <see cref="GroundDistance"/>, and <see cref="GroundNormal"/>.
    /// <para>
    /// Produces meaningful results for hover and roof thrusters. Roof probes are
    /// important for upside-down recovery, because when the craft is on its back
    /// the roof thrusters are the nodes closest to the surface.
    /// </para>
    /// <para>
    /// Call once per FixedUpdate from the bus <b>before</b> setting throttle
    /// values so that hover logic can react to current ground state.
    /// </para>
    /// </summary>
    public void CastGroundRay()
    {
        if (role != ThrusterRole.Hover && role != ThrusterRole.Roof)
        {
            IsGrounded = false;
            GroundDistance = -1f;
            GroundNormal = Vector3.up;
            return;
        }

        Vector3 origin = transform.position;
        Vector3 direction = GroundRayDirection; // raycast toward the ground/opposite thrust

        if (Physics.Raycast(origin, direction, out RaycastHit hit, groundDetectionRange))
        {
            IsGrounded = true;
            GroundDistance = hit.distance;
            GroundNormal = hit.normal;
        }
        else
        {
            IsGrounded = false;
            GroundDistance = -1f;
            GroundNormal = Vector3.up;
        }
    }

    /// <summary>
    /// Computes how much reactor power this thruster draws at a given final throttle.
    /// This ties energy cost directly to the thruster hardware: stronger maxForce
    /// thrusters naturally consume more power unless they are more efficient.
    /// </summary>
    public float GetPowerDraw(float finalThrottle, float globalPowerCostPerForce)
    {
        if (finalThrottle <= 0f || maxForce <= 0f || globalPowerCostPerForce <= 0f)
        {
            return 0f;
        }

        float safeEfficiency = Mathf.Max(0.01f, efficiency);
        return Mathf.Abs(finalThrottle) * maxForce * Mathf.Max(0f, globalPowerCostPerForce) * Mathf.Max(0f, powerDrawMultiplier) / safeEfficiency;
    }

    /// <summary>
    /// Apply thrust force at this thruster's world position using
    /// <c>Rigidbody.AddForceAtPosition</c> with <c>ForceMode.Acceleration</c>.
    /// <para>
    /// The force magnitude is <c>Throttle * maxForce</c> directed along
    /// <see cref="ThrustDirection"/>.
    /// </para>
    /// <para>
    /// No force is applied when <see cref="Throttle"/> is zero or negative,
    /// or when the cached Rigidbody is null.
    /// </para>
    /// <para>
    /// Call once per FixedUpdate from the bus <b>after</b> all throttle values
    /// have been finalized.
    /// </para>
    /// </summary>
    public void ApplyThrust()
    {
        if (_rb == null || Throttle <= 0f)
        {
            LastAppliedForce = Vector3.zero;
            return;
        }

        Vector3 force = ThrustDirection * (Throttle * maxForce);

        _rb.AddForceAtPosition(force, transform.position, ForceMode.Acceleration);
        LastAppliedForce = force;
    }

    private Vector3 GetLocalAxis(LocalThrustAxis axis)
    {
        switch (axis)
        {
            case LocalThrustAxis.PositiveY: return transform.up;
            case LocalThrustAxis.NegativeY: return -transform.up;
            case LocalThrustAxis.PositiveZ: return transform.forward;
            case LocalThrustAxis.NegativeZ: return -transform.forward;
            case LocalThrustAxis.PositiveX: return transform.right;
            case LocalThrustAxis.NegativeX: return -transform.right;
            default: return -transform.up;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  EDITOR GIZMOS
    // ══════════════════════════════════════════════════════════════

    #if UNITY_EDITOR
    /// <summary>
    /// Draws debug gizmos when the thruster is selected in the editor:
    /// thrust direction, ground-ray range (hover only), and live force magnitude.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 thrustDir = ThrustDirection;
        Vector3 groundDir = GroundRayDirection;

        // ── Thrust direction arrow ──────────────────────────────
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, thrustDir * 1.5f);

        // ── Surface probe range (hover / roof only) ─────────────
        if (role == ThrusterRole.Hover || role == ThrusterRole.Roof)
        {
            Gizmos.color = (Application.isPlaying && IsGrounded) ? Color.green : Color.red;
            Gizmos.DrawLine(origin, origin + groundDir * groundDetectionRange);

            // Ground hit point indicator
            if (Application.isPlaying && IsGrounded)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(origin + groundDir * GroundDistance, 0.1f);
            }
        }

        // ── Live force magnitude ────────────────────────────────
        if (Application.isPlaying && Throttle > 0f)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, Throttle);
            Gizmos.DrawRay(origin, thrustDir * Throttle * 2f);
        }
    }
    #endif
}
