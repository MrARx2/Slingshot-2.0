using UnityEngine;

/// <summary>
/// Forward propulsion system for the hovercraft.
/// Manages the main (forward thrust) and brake (reverse/braking) thrusters
/// with smooth throttle ramping.
///
/// <para>
/// This is a pure physics module — it does not read input directly.
/// <see cref="CraftCore"/> calls <see cref="ApplyDrive"/> once per FixedUpdate,
/// passing the current <see cref="CraftIntent"/> and <see cref="CraftTelemetry"/>.
/// </para>
/// </summary>
public class DriveCore : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  REFERENCES  (set by CraftCore)
    // ══════════════════════════════════════════════════════════════

    /// <summary>The central thruster bus for routing throttle commands.</summary>
    [HideInInspector] public ThrusterBus thrusterBus;

    /// <summary>Main (forward propulsion) thruster node.</summary>
    [HideInInspector] public ThrusterNode mainThruster;

    /// <summary>Brake (reverse / braking) thruster node.</summary>
    [HideInInspector] public ThrusterNode brakeThruster;

    // ══════════════════════════════════════════════════════════════
    //  TUNING
    // ══════════════════════════════════════════════════════════════

    [Header("Propulsion")]
    [Tooltip("How quickly throttle ramps toward target value (units per second via MoveTowards).")]
    public float throttleRampSpeed = 4.8f;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC READ STATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>Current smoothed throttle value sent to the main (forward) thruster [0–1].</summary>
    public float CurrentMainThrottle => _currentMainThrottle;

    /// <summary>Current smoothed throttle value sent to the brake thruster [0–1].</summary>
    public float CurrentBrakeThrottle => _currentBrakeThrottle;

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ══════════════════════════════════════════════════════════════

    private float _currentMainThrottle;
    private float _currentBrakeThrottle;

    // ══════════════════════════════════════════════════════════════
    //  MAIN ENTRY POINT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluate and apply propulsion forces for this physics tick.
    /// Called once per FixedUpdate by CraftCore.
    /// </summary>
    /// <param name="intent">Processed pilot intent for this frame.</param>
    /// <param name="telemetry">Current craft telemetry snapshot.</param>
    public void ApplyDrive(CraftIntent intent, CraftTelemetry telemetry)
    {
        float dt = Time.fixedDeltaTime;

        // ── Ramp main throttle toward target ──────────────────
        // EnergyCore no longer modifies the request here. DriveCore submits
        // the desired hardware throttle and ThrusterBus/EnergyCore solve the
        // final granted output later in the physics frame.
        _currentMainThrottle = Mathf.MoveTowards(
            _currentMainThrottle,
            Mathf.Clamp01(intent.throttle),
            throttleRampSpeed * dt
        );

        // ── Ramp brake throttle toward target ─────────────────
        _currentBrakeThrottle = Mathf.MoveTowards(
            _currentBrakeThrottle,
            Mathf.Clamp01(intent.brake),
            throttleRampSpeed * dt
        );

        // ── Route to bus ──────────────────────────────────────
        if (mainThruster != null)
        {
            thrusterBus.SetThrottle(mainThruster, _currentMainThrottle, ThrusterPowerChannel.Drive);
        }

        if (brakeThruster != null)
        {
            thrusterBus.SetThrottle(brakeThruster, _currentBrakeThrottle, ThrusterPowerChannel.Drive);
        }
    }
}
