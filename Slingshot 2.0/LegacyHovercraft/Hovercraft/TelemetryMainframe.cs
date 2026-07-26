using UnityEngine;

/// <summary>
/// <b>V2 Telemetry Mainframe — Sensor Package &amp; Single Source of Truth.</b>
/// <para>
/// Measures the craft's physical state once per physics frame and exposes it
/// as a <see cref="CraftTelemetry"/> struct. Every other V2 system reads
/// telemetry from here instead of measuring its own values.
/// </para>
/// <para>
/// <b>Measured values:</b>
/// <list type="bullet">
///   <item>World and local velocity, speed, forward/side speed</item>
///   <item>Roll angle, pitch angle, yaw rate (ported from V1 measurement methods)</item>
///   <item>Grounded count, grounded factor (smooth transition), isGrounded, ground normal</item>
/// </list>
/// </para>
/// <para>
/// <b>Dependencies:</b> Requires <see cref="Rigidbody"/> and <see cref="ThrusterBus"/>
/// references, wired by <see cref="CraftCore"/> during <c>Awake()</c>.
/// </para>
/// <para>
/// This system does <b>not</b> read input, compute handling intent, or apply forces.
/// </para>
/// </summary>
public class TelemetryMainframe : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  EXTERNAL REFERENCES (set by CraftCore)
    // ══════════════════════════════════════════════════════════════

    /// <summary>The craft's Rigidbody. Wired by <see cref="CraftCore"/>.</summary>
    [Tooltip("Craft Rigidbody. Set automatically by CraftCore.")]
    public Rigidbody rb;

    /// <summary>The craft's ThrusterBus for ground contact data. Wired by <see cref="CraftCore"/>.</summary>
    [Tooltip("ThrusterBus reference. Set automatically by CraftCore.")]
    public ThrusterBus thrusterBus;

    // ══════════════════════════════════════════════════════════════
    //  GROUND CONTACT FILTERING
    // ══════════════════════════════════════════════════════════════

    [Header("Ground Contact Filtering")]
    [Tooltip("If true, CraftCore sets groundedContactDistance from HoverStabilizerArray so it follows hoverHeight + cushion range.")]
    public bool autoConfigureGroundedContactDistance = true;

    [Tooltip("Maximum hover-node distance that counts as true near-ground contact. This is NOT the same as ThrusterNode.groundDetectionRange.")]
    public float groundedContactDistance = 3.25f;

    [Tooltip("How many hover nodes must be inside groundedContactDistance before the craft is considered grounded/near-ground.")]
    [Range(1, 4)]
    public int requiredGroundedHoverNodes = 1;

    [Tooltip("How quickly groundedFactor rises after enough hover nodes enter the near-ground cushion.")]
    public float groundedFactorRiseSpeed = 12f;

    [Tooltip("How quickly groundedFactor falls after the craft leaves the near-ground cushion.")]
    public float groundedFactorFallSpeed = 10f;

    // ══════════════════════════════════════════════════════════════
    //  OUTPUT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// The most recently sampled telemetry snapshot. Updated each time
    /// <see cref="SampleTelemetry"/> is called from <see cref="CraftCore.FixedUpdate()"/>.
    /// </summary>
    public CraftTelemetry CurrentTelemetry { get; private set; }

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Smoothed grounded factor. Transitions between 0 and 1 using
    /// <see cref="Mathf.MoveTowards"/> at <see cref="groundedFactorRiseSpeed"/> /
    /// <see cref="groundedFactorFallSpeed"/> per second.
    /// </summary>
    private float _groundedFactor;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Configure the near-ground contact distance from the hover system.
    /// Use this to keep telemetry grounded state tied to the active hover cushion,
    /// not to the raw ground probe length on ThrusterNode.
    /// </summary>
    public void ConfigureGroundedContactDistance(float distance)
    {
        groundedContactDistance = Mathf.Max(0.05f, distance);
    }

    /// <summary>
    /// Sample the craft's physical state and build a new <see cref="CraftTelemetry"/>.
    /// <para>
    /// Called once per <c>FixedUpdate()</c> from <see cref="CraftCore"/>. Casts the
    /// ground rays itself (via <c>ThrusterBus.CastAllGroundRays</c>) so every system
    /// downstream reads this frame's contact data.
    /// </para>
    /// </summary>
    public void SampleTelemetry()
    {
        // ── Velocity ─────────────────────────────────────────────
        Vector3 worldVelocity = rb.linearVelocity;
        Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
        float speed           = worldVelocity.magnitude;
        float forwardSpeed    = localVelocity.z;
        float sideSpeed       = localVelocity.x;

        // ── Ground contact ───────────────────────────────────────
        // Cast long/medium probes first. Then filter them into true near-ground
        // contact using groundedContactDistance. This prevents a large
        // ThrusterNode.groundDetectionRange from making the craft think it is
        // grounded while still high in the air.
        thrusterBus.CastAllGroundRays();

        int groundedCount;
        Vector3 groundNormal;
        MeasureNearGroundContacts(out groundedCount, out groundNormal);

        int surfaceContactCount;
        Vector3 surfaceNormal;
        MeasureSurfaceContacts(out surfaceContactCount, out surfaceNormal);

        float uprightDot = Vector3.Dot(transform.up, Vector3.up);
        bool isInverted = uprightDot < -0.35f;

        // Bottom-hover contacts decide normal hover grounded state.
        // If the craft is inverted and roof probes are touching the floor, also
        // report grounded so input/camera/debug systems do not treat it as mid-air.
        bool invertedSurfaceGrounded = isInverted && surfaceContactCount > 0;

        float requiredContacts = Mathf.Clamp(requiredGroundedHoverNodes, 1, 4);
        float groundTarget = (groundedCount >= requiredContacts || invertedSurfaceGrounded) ? 1f : 0f;

        float factorSpeed = groundTarget > _groundedFactor
            ? groundedFactorRiseSpeed
            : groundedFactorFallSpeed;

        _groundedFactor = Mathf.MoveTowards(
            _groundedFactor,
            groundTarget,
            Time.fixedDeltaTime * Mathf.Max(0.01f, factorSpeed)
        );

        bool isGrounded = _groundedFactor > 0.5f;

        // ── Orientation measurement ──────────────────────────────
        float rollAngle  = MeasureRollAngle(isGrounded, groundNormal);
        float pitchAngle = MeasurePitchAngle(isGrounded, groundNormal);
        float yawRate    = MeasureYawRate();

        // ── Build telemetry struct ───────────────────────────────
        CurrentTelemetry = new CraftTelemetry
        {
            worldVelocity  = worldVelocity,
            localVelocity  = localVelocity,
            speed          = speed,
            forwardSpeed   = forwardSpeed,
            sideSpeed      = sideSpeed,

            rollAngle      = rollAngle,
            pitchAngle     = pitchAngle,
            yawRate        = yawRate,

            groundedCount  = groundedCount,
            groundedFactor = _groundedFactor,
            isGrounded     = isGrounded,
            groundNormal   = invertedSurfaceGrounded ? surfaceNormal : groundNormal,

            hasSurfaceContact = surfaceContactCount > 0,
            surfaceContactCount = surfaceContactCount,
            surfaceNormal = surfaceNormal,
            uprightDot = uprightDot,
            isInverted = isInverted
        };
    }

    /// <summary>
    /// Counts only hover nodes that are inside the near-ground contact distance.
    /// A ThrusterNode may see the ground with a long ray, but it should not make
    /// the craft grounded unless it is close enough to the active hover cushion.
    /// </summary>
    private void MeasureNearGroundContacts(out int groundedCount, out Vector3 averageNormal)
    {
        groundedCount = 0;
        Vector3 normalSum = Vector3.zero;

        if (thrusterBus == null || thrusterBus.HoverNodes == null)
        {
            averageNormal = Vector3.up;
            return;
        }

        float maxDistance = Mathf.Max(0.05f, groundedContactDistance);

        foreach (var entry in thrusterBus.HoverNodes)
        {
            if (entry == null || entry.node == null || !entry.enabled)
            {
                continue;
            }

            ThrusterNode node = entry.node;

            if (!node.IsGrounded || node.GroundDistance < 0f || node.GroundDistance > maxDistance)
            {
                continue;
            }

            groundedCount++;
            normalSum += node.GroundNormal;
        }

        averageNormal = groundedCount > 0
            ? (normalSum / groundedCount).normalized
            : Vector3.up;
    }


    /// <summary>
    /// Counts all near-surface contacts from both hover and roof nodes.
    /// This is separate from normal hover grounded count so the craft can
    /// recognize being on its roof/back without enabling bottom hover logic.
    /// </summary>
    private void MeasureSurfaceContacts(out int contactCount, out Vector3 averageNormal)
    {
        contactCount = 0;
        Vector3 normalSum = Vector3.zero;

        float maxDistance = Mathf.Max(0.05f, groundedContactDistance);

        CountSurfaceContactsInList(thrusterBus != null ? thrusterBus.HoverNodes : null, maxDistance, ref contactCount, ref normalSum);
        CountSurfaceContactsInList(thrusterBus != null ? thrusterBus.RoofNodes : null, maxDistance, ref contactCount, ref normalSum);

        averageNormal = contactCount > 0
            ? (normalSum / contactCount).normalized
            : Vector3.up;
    }

    private void CountSurfaceContactsInList(
        System.Collections.Generic.List<ThrusterBus.ThrusterNodeEntry> entries,
        float maxDistance,
        ref int count,
        ref Vector3 normalSum)
    {
        if (entries == null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry == null || entry.node == null || !entry.enabled)
            {
                continue;
            }

            ThrusterNode node = entry.node;

            if (!node.IsGrounded || node.GroundDistance < 0f || node.GroundDistance > maxDistance)
            {
                continue;
            }

            count++;
            normalSum += node.GroundNormal;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  ORIENTATION MEASUREMENT (ported from V1)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Measures the craft's roll angle in degrees.
    /// <para>
    /// <b>Grounded:</b> reference up = average ground normal.<br/>
    /// <b>Airborne:</b> reference up = <see cref="Vector3.up"/>.
    /// </para>
    /// <para>
    /// Projects the reference up onto the plane perpendicular to
    /// <c>transform.forward</c>, then measures the signed angle between
    /// the projected reference up and <c>transform.up</c> around
    /// <c>transform.forward</c>.
    /// </para>
    /// </summary>
    /// <param name="isGrounded">Whether the craft is currently grounded.</param>
    /// <param name="groundNormal">Average ground surface normal.</param>
    /// <returns>Roll angle in degrees. Positive = rolled right.</returns>
    private float MeasureRollAngle(bool isGrounded, Vector3 groundNormal)
    {
        Vector3 referenceUp = isGrounded ? groundNormal : Vector3.up;

        Vector3 projectedReferenceUp = Vector3.ProjectOnPlane(
            referenceUp,
            transform.forward
        );

        if (projectedReferenceUp.sqrMagnitude < 0.001f)
        {
            return 0f;
        }

        projectedReferenceUp.Normalize();

        return Vector3.SignedAngle(
            projectedReferenceUp,
            transform.up,
            transform.forward
        );
    }

    /// <summary>
    /// Measures the craft's pitch angle in degrees.
    /// <para>
    /// Projects <c>transform.forward</c> onto the plane perpendicular to
    /// the reference up, then measures the signed angle between the
    /// projected forward and <c>transform.forward</c> around
    /// <c>transform.right</c>.
    /// </para>
    /// </summary>
    /// <param name="isGrounded">Whether the craft is currently grounded.</param>
    /// <param name="groundNormal">Average ground surface normal.</param>
    /// <returns>Pitch angle in degrees. Positive = nose up.</returns>
    private float MeasurePitchAngle(bool isGrounded, Vector3 groundNormal)
    {
        Vector3 referenceUp = isGrounded ? groundNormal : Vector3.up;

        Vector3 projectedForward = Vector3.ProjectOnPlane(
            transform.forward,
            referenceUp
        );

        if (projectedForward.sqrMagnitude < 0.001f)
        {
            return 0f;
        }

        projectedForward.Normalize();

        return Vector3.SignedAngle(
            projectedForward,
            transform.forward,
            transform.right
        );
    }

    /// <summary>
    /// Measures the craft's yaw rate in degrees per second.
    /// <para>
    /// Converts the Y component of the local angular velocity from
    /// radians/sec to degrees/sec.
    /// </para>
    /// </summary>
    /// <returns>Yaw rate in degrees/sec. Positive = turning right.</returns>
    private float MeasureYawRate()
    {
        Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity);
        return localAngularVelocity.y * Mathf.Rad2Deg;
    }
}
