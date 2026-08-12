using UnityEngine;

/// <summary>
/// <b>V2 Craft Core — Root Coordinator / Chassis Brainstem.</b>
/// <para>
/// The central MonoBehaviour on the hovercraft root GameObject. Holds references
/// to every V2 subsystem, wires shared dependencies (Rigidbody, ThrusterBus),
/// and enforces the correct execution order each frame.
/// </para>
/// <para>
/// <b>Update() order:</b>
/// <list type="number">
///   <item><see cref="PilotCommandInterface.TickInput"/> + <see cref="PilotCommandInterface.SampleInput"/></item>
///   <item><see cref="CraftFeedbackSystem.UpdateVisuals"/> (optional)</item>
/// </list>
/// </para>
/// <para>
/// <b>FixedUpdate() order:</b>
/// <list type="number">
///   <item><see cref="ThrusterBus.BeginFrame"/></item>
///   <item><see cref="TelemetryMainframe.SampleTelemetry"/></item>
///   <item><see cref="VectoringComputer.EvaluateIntent"/></item>
///   <item><see cref="TractionCore.EvaluateTraction"/> (optional)</item>
///   <item><see cref="HoverStabilizerArray.ApplyHover"/> (optional)</item>
///   <item><see cref="DriveCore.ApplyDrive"/> (optional)</item>
///   <item><see cref="VectorThrusterArray.ApplyVectoring"/> (optional)</item>
///   <item><see cref="TractionCore.ApplyTractionForces"/> (optional)</item>
///   <item><see cref="ThrusterBus.ApplyAllThrust"/></item>
/// </list>
/// </para>
/// <para>
/// This component contains <b>no handling math</b> and does <b>not</b> directly
/// set thruster throttles. It is purely an orchestrator.
/// </para>
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ThrusterBus))]
public class CraftCore : MonoBehaviour, TrackGeneration.ITrackRaceCraft
{
    // ══════════════════════════════════════════════════════════════
    //  ITrackRaceCraft — contract used by the track-generation assembly
    //  (keeps the track code free of concrete craft/camera references)
    // ══════════════════════════════════════════════════════════════

    Transform TrackGeneration.ITrackRaceCraft.CraftTransform => transform;

    Rigidbody TrackGeneration.ITrackRaceCraft.CraftRigidbody => _rb != null ? _rb : GetComponent<Rigidbody>();

    float TrackGeneration.ITrackRaceCraft.SpawnRideHeight
        => hoverArray != null ? Mathf.Max(0f, hoverArray.hoverHeight + 0.05f) : 2.05f;

    void TrackGeneration.ITrackRaceCraft.OnPlacedAtTrackStart()
    {
        var hovercraftCamera = FindAnyObjectByType<HovercraftCamera>();
        if (hovercraftCamera == null) return;

        hovercraftCamera.target = transform;
        hovercraftCamera.targetRigidbody = _rb != null ? _rb : GetComponent<Rigidbody>();
        hovercraftCamera.craftCore = this;
        hovercraftCamera.ResetCameraImmediate();
    }

    // ══════════════════════════════════════════════════════════════
    //  V2 SUBSYSTEM REFERENCES
    // ══════════════════════════════════════════════════════════════

    [Header("Core Systems (Required)")]
    [Tooltip("Cockpit input layer. Reads hardware input and produces PilotCommand.")]
    public PilotCommandInterface pilotInput;

    [Tooltip("Sensor package. Measures craft state each physics frame.")]
    public TelemetryMainframe telemetry;

    [Tooltip("Flight computer. Converts pilot input + telemetry into CraftIntent.")]
    public VectoringComputer vectoring;

    [Header("Handling Systems (Optional — wire as implemented)")]
    [Tooltip("Traction / grip system. Evaluates and applies surface traction forces.")]
    public TractionCore traction;

    [Tooltip("Hover stabilizer array. Manages hover thruster PD control and attitude.")]
    public HoverStabilizerArray hoverArray;

    [Tooltip("Drive core. Manages main/brake propulsion thrusters.")]
    public DriveCore drive;

    [Tooltip("Vector thruster array. Manages strafe/yaw differential thrusters.")]
    public VectorThrusterArray vectorThrusters;

    [Tooltip("Manual vertical thruster array. Q fires roof/downforce, E fires bottom/lift.")]
    public AttitudeControlArray attitude;

    [Tooltip("Space-held controllable Overcharge system tied to the shared POWER BUS.")]
    public OverchargeCore overcharge;

    [Tooltip("Live performance power distribution bus. Scales drive/vectoring/manual thrusters/overcharge when several systems compete.")]
    public EnergyCore energy;

    [Tooltip("Feedback system. Manages visual/audio feedback (grip breaker indicator, etc.).")]
    public CraftFeedbackSystem feedback;

    // ══════════════════════════════════════════════════════════════
    //  EXTRA PHYSICS
    // ══════════════════════════════════════════════════════════════

    [Header("Extra Physics")]
    [Tooltip("Additional downward acceleration applied every FixedUpdate. Keeps the craft planted.")]
    public float extraGravity = 15f;

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE CACHED REFERENCES
    // ══════════════════════════════════════════════════════════════

    private Rigidbody _rb;
    private ThrusterBus _thrusterBus;
    private HovercraftCamera _boostCamera;
    private float _nextBoostCameraScan;

    /// <summary>Lazily finds the active camera so we can push the boost signal to it.</summary>
    private void EnsureBoostCamera()
    {
        if (_boostCamera != null || Time.unscaledTime < _nextBoostCameraScan) return;
        _nextBoostCameraScan = Time.unscaledTime + 1f;
        _boostCamera = FindAnyObjectByType<HovercraftCamera>();
    }

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Caches shared dependencies, configures the Rigidbody, and wires
    /// references to all subsystems that need them.
    /// </summary>
    private void Awake()
    {
        // ── Cache shared components ──────────────────────────────
        _rb = GetComponent<Rigidbody>();
        _thrusterBus = GetComponent<ThrusterBus>();

        // ── Auto-fill subsystem references if they are on the craft root ─
        if (pilotInput == null) pilotInput = GetComponent<PilotCommandInterface>();
        if (telemetry == null) telemetry = GetComponent<TelemetryMainframe>();
        if (vectoring == null) vectoring = GetComponent<VectoringComputer>();
        if (traction == null) traction = GetComponent<TractionCore>();
        if (hoverArray == null) hoverArray = GetComponent<HoverStabilizerArray>();
        if (drive == null) drive = GetComponent<DriveCore>();
        if (vectorThrusters == null) vectorThrusters = GetComponent<VectorThrusterArray>();
        if (attitude == null) attitude = GetComponent<AttitudeControlArray>();
        if (overcharge == null) overcharge = GetComponent<OverchargeCore>();
        if (energy == null) energy = GetComponent<EnergyCore>();
        if (feedback == null) feedback = GetComponent<CraftFeedbackSystem>();

        // ── Configure Rigidbody (matching V1 conventions) ────────
        _rb.useGravity = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // ── Wire shared references to subsystems ─────────────────
        WireSubsystemReferences();
    }

    /// <summary>
    /// Runs at display refresh rate. Samples input and updates visuals.
    /// <para>
    /// <b>Execution order:</b>
    /// <list type="number">
    ///   <item><see cref="PilotCommandInterface.SampleInput"/> — read hardware input</item>
    ///   <item><see cref="CraftFeedbackSystem.UpdateVisuals"/> — update visual indicators</item>
    /// </list>
    /// </para>
    /// </summary>
    private void Update()
    {
        if (pilotInput == null) return;

        // 1. Accumulate frame-based input explicitly from the core.
        //    This avoids relying on Unity script execution order between two Update() methods.
        pilotInput.TickInput();

        // 2. Sample input (consumes accumulated mouse delta).
        pilotInput.SampleInput();

        // 3. Update visual feedback (optional system).
        feedback?.UpdateVisuals(
            pilotInput.CurrentCommand,
            vectoring != null ? vectoring.CurrentIntent : default,
            telemetry != null ? telemetry.CurrentTelemetry : default,
            traction != null ? traction.CurrentTraction : TractionState.Normal
        );
    }

    /// <summary>
    /// Runs at physics rate. Orchestrates the full physics pipeline.
    /// <para>
    /// <b>Execution order:</b>
    /// <list type="number">
    ///   <item>ThrusterBus.BeginFrame — reset thruster state for this frame</item>
    ///   <item>TelemetryMainframe.SampleTelemetry — measure craft state</item>
    ///   <item>VectoringComputer.EvaluateIntent — interpret pilot input</item>
    ///   <item>TractionCore.EvaluateTraction — compute grip profile (optional)</item>
    ///   <item>HoverStabilizerArray.ApplyHover — set hover thruster throttles (optional)</item>
    ///   <item>DriveCore.ApplyDrive — set propulsion thruster throttles (optional)</item>
    ///   <item>VectorThrusterArray.ApplyVectoring — set strafe/yaw throttles (optional)</item>
    ///   <item>TractionCore.ApplyTractionForces — apply grip forces (optional)</item>
    ///   <item>ThrusterBus.ApplyAllThrust — fire all thrusters</item>
    /// </list>
    /// </para>
    /// </summary>
    private void FixedUpdate()
    {
        // ── Extra gravity ────────────────────────────────────────
        _rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

        // ── 1. Begin thruster frame ──────────────────────────────
        _thrusterBus.BeginFrame();

        // ── 2. Sample telemetry ──────────────────────────────────
        telemetry.SampleTelemetry();

        // ── 3. Evaluate intent ───────────────────────────────────
        // BOOST is an instant one-shot now — there is no charge target to keep in
        // sync, so intent no longer needs the (removed) overcharge capture state.
        vectoring.EvaluateIntent(
            pilotInput.CurrentCommand,
            telemetry.CurrentTelemetry
        );

        // ── 4. Evaluate traction (optional) ──────────────────────
        traction?.EvaluateTraction(
            pilotInput.CurrentCommand,
            vectoring.CurrentIntent,
            telemetry.CurrentTelemetry
        );

        TractionState activeTraction = traction != null
            ? traction.CurrentTraction
            : TractionState.Normal;

        // Important: systems now submit raw desired throttle only.
        // EnergyCore is resolved later through ThrusterBus after all requests
        // are collected, so power draw is based on actual ThrusterNode hardware.

        // ── 5. Apply hover stabilizer (optional) ─────────────────
        hoverArray?.ApplyHover(
            vectoring.CurrentIntent,
            telemetry.CurrentTelemetry,
            activeTraction
        );

        // ── 6. Apply drive (optional) ────────────────────────────
        drive?.ApplyDrive(
            vectoring.CurrentIntent,
            telemetry.CurrentTelemetry
        );

        // ── 7. Apply vectoring thrusters (optional) ──────────────
        vectorThrusters?.ApplyVectoring(
            vectoring.CurrentIntent,
            telemetry.CurrentTelemetry,
            activeTraction
        );

        // ── 8. Apply manual Q/E vertical thrusters (optional) ────
        attitude?.ApplyAttitudeControl(
            vectoring.CurrentIntent,
            telemetry.CurrentTelemetry,
            activeTraction
        );

        // ── 9. Apply Space overcharge burst / charge state ───────
        // Uses last frame's EnergyState for charge-rate feedback; this frame's
        // charge draw is resolved just below as a virtual reactor load.
        EnergyState lastEnergy = energy != null ? energy.CurrentEnergy : EnergyState.Full;
        overcharge?.TickOvercharge(
            vectoring.CurrentIntent,
            telemetry.CurrentTelemetry,
            lastEnergy
        );

        // ── 9b. Drive the synchronized camera boost response ─────
        // Onset-synced: the camera reacts the instant the burst begins, scaled by the
        // reactor-granted intensity, not by the speed the craft eventually reaches.
        if (overcharge != null)
        {
            EnsureBoostCamera();
            _boostCamera?.SetBoostAmount(overcharge.CameraBoost01);
        }

        // ── 10. Apply traction forces (optional) ─────────────────
        traction?.ApplyTractionForces(telemetry.CurrentTelemetry);

        // ── 11. Resolve reactor power and final thruster throttle ─
        _thrusterBus.ResolveEnergy(energy, overcharge);

        // ── 12. Fire all thrusters ───────────────────────────────
        _thrusterBus.ApplyAllThrust();

        // ── 13. Consume one-shot inputs after physics used them ───
        pilotInput?.ClearConsumedOneShotInputs();
    }

    // ══════════════════════════════════════════════════════════════
    //  START LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    private void Start()
    {
        // ── Initialize hover array gravity throttle ──────────────
        if (hoverArray != null)
        {
            hoverArray.ComputeGravityThrottle(extraGravity);
        }

        // ── Keep telemetry grounded state tied to the hover cushion ─
        // ThrusterNode.groundDetectionRange may be large for preview/terrain sensing,
        // but grounded/near-ground gameplay state should only activate inside the
        // short hover cushion around hoverHeight.
        if (telemetry != null && hoverArray != null && telemetry.autoConfigureGroundedContactDistance)
        {
            telemetry.ConfigureGroundedContactDistance(
                hoverArray.GetRecommendedGroundedContactDistance()
            );
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  INTERNAL WIRING
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Passes the shared <see cref="Rigidbody"/> and <see cref="ThrusterBus"/>
    /// references to all subsystems that need them, and resolves individual thrusters.
    /// </summary>
    private void WireSubsystemReferences()
    {
        // TelemetryMainframe needs both rb and thrusterBus.
        if (telemetry != null)
        {
            telemetry.rb = _rb;
            telemetry.thrusterBus = _thrusterBus;
        }

        // HoverStabilizerArray needs rb, thrusterBus, and individual hover nodes.
        if (hoverArray != null)
        {
            hoverArray.rb = _rb;
            hoverArray.thrusterBus = _thrusterBus;
            hoverArray.hoverFL = _thrusterBus.FindByLabel("Hover_FL")?.node;
            hoverArray.hoverFR = _thrusterBus.FindByLabel("Hover_FR")?.node;
            hoverArray.hoverRL = _thrusterBus.FindByLabel("Hover_RL")?.node;
            hoverArray.hoverRR = _thrusterBus.FindByLabel("Hover_RR")?.node;
            hoverArray.roofFL = _thrusterBus.FindByLabel("Roof_FL")?.node;
            hoverArray.roofFR = _thrusterBus.FindByLabel("Roof_FR")?.node;
            hoverArray.roofRL = _thrusterBus.FindByLabel("Roof_RL")?.node;
            hoverArray.roofRR = _thrusterBus.FindByLabel("Roof_RR")?.node;
        }

        // TractionCore needs rb.
        if (traction != null)
        {
            traction.rb = _rb;
        }

        // DriveCore needs thrusterBus and propulsion nodes.
        if (drive != null)
        {
            drive.thrusterBus = _thrusterBus;
            drive.mainThruster = _thrusterBus.FindByLabel("Main_Rear")?.node;
            drive.brakeThruster = _thrusterBus.FindByLabel("Brake_Front")?.node;
        }

        // VectorThrusterArray needs rb, thrusterBus, and strafe nodes.
        if (vectorThrusters != null)
        {
            vectorThrusters.rb = _rb;
            vectorThrusters.thrusterBus = _thrusterBus;
            vectorThrusters.strafeFL = _thrusterBus.FindByLabel("Strafe_Front_Left")?.node;
            vectorThrusters.strafeFR = _thrusterBus.FindByLabel("Strafe_Front_Right")?.node;
            vectorThrusters.strafeBL = _thrusterBus.FindByLabel("Strafe_Back_Left")?.node;
            vectorThrusters.strafeBR = _thrusterBus.FindByLabel("Strafe_Back_Right")?.node;
        }

        // AttitudeControlArray needs rb and thrusterBus.
        if (attitude != null)
        {
            attitude.rb = _rb;
            attitude.thrusterBus = _thrusterBus;
        }

        // OverchargeCore needs thrusterBus and optional main/brake node references.
        if (overcharge != null)
        {
            overcharge.thrusterBus = _thrusterBus;
            overcharge.mainThruster = _thrusterBus.FindByLabel("Main_Rear")?.node;
            overcharge.brakeThruster = _thrusterBus.FindByLabel("Brake_Front")?.node;
        }
    }
}
