using UnityEngine;

/// <summary>
/// A single thruster mounted on the craft hull.
/// The thruster fires along -transform.up (its local down axis).
/// Position it as a child of the craft and orient it so its local Y points
/// AWAY from the desired thrust direction.
///
/// The controller sets <see cref="Throttle"/> each FixedUpdate,
/// and this component applies the force at its own world position.
/// </summary>
public class Thruster : MonoBehaviour
{
    // ── Configuration ─────────────────────────────────────────────
    public enum ThrusterType
    {
        Hover,      // Bottom-facing, keeps the craft airborne
        Main,       // Rear-facing, forward propulsion
        Brake,      // Front-facing, braking / reverse
        Strafe      // Side-facing, lateral movement
    }

    [Header("Thruster Setup")]
    [Tooltip("What role this thruster plays.")]
    public ThrusterType type = ThrusterType.Hover;

    [Tooltip("Maximum force this thruster can output (Newtons as Acceleration).")]
    public float maxForce = 100f;

    [Tooltip("Optional TrailRenderer attached to this thruster.")]
    public TrailRenderer trailRenderer;

    [Tooltip("Optional sphere GameObject that lights up when the thruster is firing. Assign a sphere with no collider and a URP/Lit material.")]
    public GameObject indicatorSphere;

    [Header("Ground Detection (Hover only)")]
    [Tooltip("How far to raycast for ground detection.")]
    public float groundDetectionRange = 4f;

    // ── Runtime State (set by controller, read by anyone) ─────────
    /// <summary>Current throttle level, 0 to 1 (can exceed 1 for burst/jump).</summary>
    public float Throttle { get; set; }

    /// <summary>True if this hover thruster's raycast detected ground.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Distance to ground (negative if no ground detected).</summary>
    public float GroundDistance { get; private set; }

    /// <summary>Surface normal at the hit point.</summary>
    public Vector3 GroundNormal { get; private set; }

    /// <summary>The actual force vector applied this frame (for debug).</summary>
    public Vector3 LastAppliedForce { get; private set; }

    // ── Internal ──────────────────────────────────────────────────
    private Rigidbody _rb;

    // ── Visual Effects ─────────────────────────────────────────────
    private float _originalTrailWidth;
    private Renderer _indicatorRenderer;
    private MaterialPropertyBlock _propBlock;

    // ───────────────────────────────────────────────────────────────
    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>();
    }

    void Start()
    {
        // Cache the designed starting width of the trail renderer
        if (trailRenderer != null)
        {
            _originalTrailWidth = trailRenderer.startWidth;
            trailRenderer.emitting = false;
        }

        // Cache indicator sphere renderer and set up a property block so we
        // don't create a new material instance per sphere.
        if (indicatorSphere != null)
        {
            _indicatorRenderer = indicatorSphere.GetComponent<Renderer>();
            if (_indicatorRenderer != null)
                _propBlock = new MaterialPropertyBlock();
        }
    }

    void Update()
    {
        // ── Trail renderer ────────────────────────────────────────
        if (trailRenderer != null)
        {
            float activeThreshold = 0.05f;
            if (Throttle > activeThreshold)
            {
                float cappedThrottle = Mathf.Clamp01(Throttle);
                trailRenderer.emitting = true;
                trailRenderer.startWidth = _originalTrailWidth * cappedThrottle;
            }
            else
            {
                trailRenderer.emitting = false;
            }
        }

        // ── Indicator sphere ──────────────────────────────────────
        if (_indicatorRenderer != null && _propBlock != null)
        {
            GetTypeColors(out Color idleColor, out Color activeColor);

            float t = Mathf.Clamp01(Throttle / 0.8f); // reach full glow at 80% throttle
            Color baseColor = Color.Lerp(idleColor, activeColor, t);

            // Emission scales with throttle so it clearly glows when firing
            Color emissiveColor = Throttle > 0.05f
                ? activeColor * (t * 2.5f)   // HDR brightness for bloom effect
                : Color.black;

            _indicatorRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_BaseColor", baseColor);
            _propBlock.SetColor("_EmissionColor", emissiveColor);
            _indicatorRenderer.SetPropertyBlock(_propBlock);
        }
    }

    // ── Per-type color palette ─────────────────────────────────────
    void GetTypeColors(out Color idleColor, out Color activeColor)
    {
        switch (type)
        {
            case ThrusterType.Hover:
                idleColor  = new Color(0.08f, 0.08f, 0.15f);
                activeColor = new Color(0.3f, 0.7f, 1.0f);   // cool blue
                break;
            case ThrusterType.Main:
                idleColor  = new Color(0.15f, 0.08f, 0.02f);
                activeColor = new Color(1.0f, 0.5f, 0.1f);   // warm orange
                break;
            case ThrusterType.Brake:
                idleColor  = new Color(0.12f, 0.02f, 0.02f);
                activeColor = new Color(1.0f, 0.15f, 0.1f);  // red
                break;
            case ThrusterType.Strafe:
                idleColor  = new Color(0.05f, 0.12f, 0.05f);
                activeColor = new Color(0.2f, 1.0f, 0.3f);   // green
                break;
            default:
                idleColor  = new Color(0.1f, 0.1f, 0.1f);
                activeColor = Color.white;
                break;
        }
    }

    // ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Cast the ground ray (hover thrusters only).
    /// Call once per FixedUpdate from the controller BEFORE setting throttle.
    /// </summary>
    public void CastGroundRay()
    {
        if (type != ThrusterType.Hover)
        {
            IsGrounded = false;
            GroundDistance = -1f;
            GroundNormal = Vector3.up;
            return;
        }

        Vector3 origin = transform.position;
        Vector3 direction = -transform.up; // thrust direction = raycast direction

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

    // ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Apply the thrust force at this thruster's position.
    /// Call once per FixedUpdate from the controller AFTER setting throttle.
    /// </summary>
    public void ApplyThrust()
    {
        if (_rb == null || Throttle <= 0f)
        {
            LastAppliedForce = Vector3.zero;
            return;
        }

        // Force direction is along the thruster's local UP (opposite of thrust visual)
        // i.e. a hover thruster pointing down pushes the craft UP
        Vector3 forceDir = transform.up;
        Vector3 force = forceDir * Throttle * maxForce;

        _rb.AddForceAtPosition(force, transform.position, ForceMode.Acceleration);
        LastAppliedForce = force;
    }

    // ───────────────────────────────────────────────────────────────
    /// <summary>
    /// The direction this thruster pushes the craft (opposite of where it's pointing).
    /// </summary>
    public Vector3 ThrustDirection => transform.up;

    // ───────────────────────────────────────────────────────────────
    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 thrustDir = transform.up;

        // Draw thrust direction
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, thrustDir * 1.5f);

        // Draw raycast range (hover only)
        if (type == ThrusterType.Hover)
        {
            Gizmos.color = (Application.isPlaying && IsGrounded) ? Color.green : Color.red;
            Gizmos.DrawLine(origin, origin - thrustDir * groundDetectionRange);

            // Ground hit point
            if (Application.isPlaying && IsGrounded)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(origin - thrustDir * GroundDistance, 0.1f);
            }
        }

        // Draw force magnitude
        if (Application.isPlaying && Throttle > 0f)
        {
            Gizmos.color = Color.Lerp(Color.yellow, Color.red, Throttle);
            Gizmos.DrawRay(origin, thrustDir * Throttle * 2f);
        }
    }
    #endif
}
