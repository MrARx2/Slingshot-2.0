using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Hover suspension / anti-gravity cushion controller.
/// Manages the four bottom-facing hover thrusters that keep the craft airborne,
/// handle grounded attitude via corner target heights, and provide airborne
/// roll/pitch control through throttle differentials.
/// (Vertical bursts are OverchargeCore's job — Space charge/release.)
///
/// SURFACE ALIGNMENT: the stabilizer also torques the craft so its belly follows
/// the ground surface angle (ramps, banked corners, loops, corkscrews) instead of
/// staying world-level, with predictive look-ahead so the nose starts rising for
/// an upcoming ramp face BEFORE it gets there. Once aligned, the hover node rays
/// and thrust axes are perpendicular to the surface again, so the whole hover
/// stack works in the surface frame on any track shape.
///
/// <para>
/// This is a pure physics module — it does not read input directly.
/// <see cref="CraftCore"/> calls <see cref="ApplyHover"/> once per FixedUpdate,
/// passing the current <see cref="CraftIntent"/>, <see cref="CraftTelemetry"/>,
/// and <see cref="TractionState"/>.
/// </para>
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

    [Tooltip("Maximum allowed hover thruster throttle. (Overcharge bursts stack on top through their own power channel.)")]
    public float hoverMaxThrottle = 2.25f;

    // ══════════════════════════════════════════════════════════════
    //  HOVER FEEL / SMOOTHING
    // ══════════════════════════════════════════════════════════════

    [Header("Hover Feel / Smoothing")]
    [Tooltip("Exponential response speed when hover throttle needs to rise. Lower = smoother/heavier feel.")]
    public float hoverThrusterResponseSpeed = 7f;

    [Tooltip("Exponential response speed when hover throttle needs to fall. Higher values prevent pogo-stick rebound after landings.")]
    public float hoverThrottleFallResponseSpeed = 24f;

    [Tooltip("How quickly hover throttle bleeds off while airborne. Prevents lift from lingering after the craft leaves the hover cushion.")]
    public float airborneHoverThrottleDecaySpeed = 32f;

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

    [Header("Hard Compression / Anti-Launch")]
    [Tooltip("Maximum upward speed the hover cushion may push the craft to while recovering to hover height. The cushion can arrest ANY fall at full power, but can never catapult the craft upward faster than this — this is what stops compression launches after steep drops and loop descents.")]
    public float maxCushionRecoverySpeed = 2.5f;

    [Tooltip("Fall speed into the cushion where throttle rise smoothing starts being bypassed, so hard falls are caught by the thrusters instead of the hull collider.")]
    public float emergencyCatchStartSpeed = 6f;

    [Tooltip("Fall speed where the emergency catch reaches full (instant) throttle response.")]
    public float emergencyCatchFullSpeed = 15f;

    // ══════════════════════════════════════════════════════════════
    //  DYNAMIC SUSPENSION (high-speed + surface curvature)
    // ══════════════════════════════════════════════════════════════

    [Header("Dynamic Suspension")]
    [Tooltip("Stiffen the hover suspension with speed and compensate surface curvature. Required for very high-speed driving (hundreds of km/h) on loops, crests and valleys.")]
    public bool dynamicSuspension = true;

    [Tooltip("Speed (m/s) where the suspension starts stiffening. ~60 m/s ≈ 216 km/h.")]
    public float stiffenStartSpeed = 60f;

    [Tooltip("Speed (m/s) where the suspension reaches full stiffness. ~250 m/s ≈ 900 km/h.")]
    public float stiffenFullSpeed = 250f;

    [Tooltip("Multiplier on hover spring/damping strength and throttle ceiling at full stiffening speed.")]
    public float maxSpeedStiffness = 3f;

    [Tooltip("Curvature compensation: on concave track (going up a loop, valleys) the cushion adds the centripetal force needed to hold the racing line; on convex track (crests, loop descents) it backs off so the craft follows the road instead of being thrown off it.")]
    public bool compensateSurfaceCurvature = true;

    [Tooltip("Cap on the curvature compensation acceleration (m/s²). 250 m/s on a 30 m loop needs ~2100.")]
    public float maxCurvatureAccel = 4000f;

    [Tooltip("Response speed of the smoothed surface-rotation measurement. Higher = snappier but noisier on bumpy geometry.")]
    public float curvatureSmoothing = 20f;

    [Tooltip("Response speed when the curvature demand DROPS (loop exits, valley ends). Much faster than the attack smoothing: a centripetal push that lingers onto flat road is a launch. At 400 m/s every 10 ms of lag is 4 m of rocket.")]
    public float curvatureReleaseSmoothing = 80f;

    [Tooltip("How strongly the look-ahead probes pre-release the centripetal push before the road flattens (0 = react only to the surface under the craft, 1 = fully trust the probes). Removes the loop-exit bump at very high speed.")]
    [Range(0f, 1f)]
    public float lookAheadCurvatureRelease = 0.8f;

    [Header("Axle Balance")]
    [Tooltip("Extra hover damping on the REAR corners relative to the front. The look-ahead alignment pitches the nose toward upcoming surfaces, which swings the tail into its cushion — the rear eats more compression at speed and wants more damping.")]
    public float rearDampingMultiplier = 1.35f;

    [Tooltip("Extra hover spring strength on the REAR corners. Keep 1 unless the tail sags; raise in small steps.")]
    public float rearSpringMultiplier = 1f;

    [Header("Loop / Rough Surface Ride")]
    [Tooltip("Filters the FACETED ring-mesh normals into one smooth surface reference. The filter first PREDICTS the surface rotation (zero lag on loops), then gently corrects toward the measured normal — this kills the facet-to-facet 'lowrider' jiggle: without it, every facet edge injects a fake vertical-velocity step the suspension then 'corrects' at full force.")]
    public float surfaceNormalSmoothing = 12f;

    [Tooltip("Nose-up attitude bias in degrees at full speed — like a motocross rider lofting the front over rough ground. The nose rides slightly high, the rear carries the load, and facet edges hit the (heavily damped) rear instead of slapping the nose. Scales in with speed.")]
    public float highSpeedNoseUpBias = 2.5f;

    [Tooltip("Corner height errors below this (meters) barely respond — they are facet polish on the ring-built track, not real errors. Stops per-corner micro-corrections from pumping the craft on curved sections.")]
    public float heightErrorDeadband = 0.05f;

    // ══════════════════════════════════════════════════════════════
    //  SECTION CONTEXT (driven by SectionAdaptiveSuspension, not serialized)
    // ══════════════════════════════════════════════════════════════

    /// <summary>Runtime multiplier on suspension stiffness from the current track section.</summary>
    public float ContextStiffnessMultiplier { get; set; } = 1f;

    /// <summary>Runtime multiplier on hover damping from the current track section.</summary>
    public float ContextDampingMultiplier { get; set; } = 1f;

    /// <summary>Runtime multiplier on surface-alignment strength from the current track section.</summary>
    public float ContextAlignmentMultiplier { get; set; } = 1f;

    /// <summary>Extra nose-up degrees requested by the current track section.</summary>
    public float ContextNoseUpDegrees { get; set; } = 0f;

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
    //  SURFACE ALIGNMENT
    // ══════════════════════════════════════════════════════════════

    [Header("Surface Alignment")]
    [Tooltip("Torque the craft so its belly follows the ground surface angle (ramps, banked track, loops, corkscrews) instead of staying world-level. This is what keeps the nose from plowing into rising road at speed.")]
    public bool alignToSurface = true;

    [Tooltip("Alignment spring strength: how quickly the craft's up axis rotates toward the surface normal. Higher = snappier surface following.")]
    public float surfaceAlignStrength = 9f;

    [Tooltip("Damping on roll/pitch angular velocity while aligning. Higher = less overshoot wobble after sharp surface changes.")]
    public float surfaceAlignDamping = 1.6f;

    [Tooltip("Maximum angular acceleration the alignment may apply. Caps violent snaps when the surface angle changes abruptly.")]
    public float maxSurfaceAlignTorque = 40f;

    [Tooltip("Look ahead this many seconds of travel and pre-rotate for the upcoming surface (ramp faces, loop entries, landing zones). 0 disables prediction.")]
    public float surfaceLookAheadTime = 0.4f;

    [Tooltip("Maximum look-ahead distance in meters, regardless of speed.")]
    public float maxSurfaceLookAheadDistance = 20f;

    [Tooltip("How strongly the predicted upcoming surface blends into the alignment target. 0 = react only to the surface currently underneath.")]
    [Range(0f, 1f)] public float surfaceLookAheadBlend = 0.65f;

    [Tooltip("Layers the alignment probes may hit. Trigger colliders are always ignored.")]
    public LayerMask surfaceProbeLayers = ~0;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC READ STATE
    // ══════════════════════════════════════════════════════════════

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

    private float _gravityThrottle;

    // Dynamic suspension state.
    private float _speedStiffness = 1f;          // 1..maxSpeedStiffness from speed
    private float _curvatureAccel;               // smoothed signed m/s² along the normal (+ = concave)
    private Vector3 _surfaceAngularVelocity;     // smoothed rotation rate of the ground normal (rad/s)
    private Vector3 _prevGroundNormal = Vector3.up;
    private bool _hasPrevGroundNormal;

    // Predicted surface from last tick's alignment probes (loop-exit pre-release).
    private Vector3 _predictedSurfaceNormal = Vector3.up;
    private float _predictedSurfaceDistance;
    private bool _hasPredictedSurface;

    // Facet-filtered surface reference (predict-correct, see surfaceNormalSmoothing).
    private Vector3 _smoothSurfaceNormal = Vector3.up;
    private bool _hasSmoothSurfaceNormal;
    private Vector3 _smoothAlignTarget = Vector3.up;
    private bool _hasSmoothAlignTarget;

    // Landing reacquisition state. Airborne attitude can leave the craft pitched or
    // rolled far enough that all four narrow hover rays miss a flat landing surface,
    // even after the hull has physically touched it. Preserve that real collision
    // normal briefly so surface alignment can rotate the hover rays back onto the
    // road. This changes sensing only; it does not add force or alter throttle tuning.
    private bool _landingReacquisitionArmed;
    private Vector3 _landingCollisionNormal = Vector3.up;
    private float _landingCollisionSeenAt = float.NegativeInfinity;
    private const float LandingCollisionMemorySeconds = 0.25f;


    // Surface alignment debug state (drawn by OnDrawGizmosSelected).
    private Vector3 _alignTargetNormal = Vector3.up;
    private Vector3 _alignForwardHitPoint;
    private Vector3 _alignDownProbeOrigin;
    private Vector3 _alignDownHitPoint;
    private bool _alignForwardHit;
    private bool _alignDownHit;

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
        float dt = Time.fixedDeltaTime;

        IsStabilizerArmed = intent.stabilizerArmed;
        UpdateLandingReacquisitionState(telemetry);

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

        // ── Dynamic suspension: speed stiffening + surface curvature ──
        UpdateDynamicSuspension(telemetry, dt);

        // ── Update lean targets ──────────────────────────────────
        UpdateLeanTargets(intent, telemetry, traction, dt);

        // ── Airborne attitude demands ────────────────────────────
        float rollDemand = 0f;
        float pitchDemand = 0f;

        if (!telemetry.isGrounded)
        {
            ComputeAirborneAttitude(intent, telemetry, out rollDemand, out pitchDemand);
        }

        // ── Per-corner hover throttle ────────────────────────────
        SetHoverThrottle(hoverFL, ref _hoverThrottleFL, rollDemand, pitchDemand,
                         isLeft: true, isFront: true, telemetry);

        SetHoverThrottle(hoverFR, ref _hoverThrottleFR, rollDemand, pitchDemand,
                         isLeft: false, isFront: true, telemetry);

        SetHoverThrottle(hoverRL, ref _hoverThrottleRL, rollDemand, pitchDemand,
                         isLeft: true, isFront: false, telemetry);

        SetHoverThrottle(hoverRR, ref _hoverThrottleRR, rollDemand, pitchDemand,
                         isLeft: false, isFront: false, telemetry);

        ApplyRoofStabilizer(intent, telemetry);

        // ── Surface alignment torque ──────────────────────────────
        // Rotates the craft to match the road angle (with look-ahead), so the
        // hover nodes stay perpendicular to the surface on ramps/loops/corkscrews.
        ApplySurfaceAlignment(telemetry);
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
        throttle = SmoothHoverThrottle(node, smoothedThrottle, throttle, true);
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

    private void ApplyRoofStabilizer(CraftIntent intent, CraftTelemetry telemetry)
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

        // Roof stabilizer only helps near a real surface. This avoids the old
        // invisible autopilot feeling from long-distance ground probes.
        float nearSurface01 = Mathf.Clamp01(telemetry.groundedFactor);
        if (nearSurface01 <= 0.001f || !telemetry.hasSurfaceContact)
        {
            SetRoofStabilizerThrottles(0f, 0f, 0f, 0f);
            return;
        }

        // PER-CORNER up-stroke damping: the bottom thrusters can only PUSH, so on
        // the up-stroke of a bounce cycle the suspension otherwise has no actuator
        // but gravity. Each roof corner catches ITS corner's rise — this is what
        // damps pitch/roll bounce (front-back seesaw), which a craft-average roof
        // throttle could never touch. Corner state comes from the paired bottom
        // hover node's ray. EnergyCore grants final power later per hardware.
        SetRoofStabilizerThrottles(
            ComputeCornerRoofThrottle(hoverFL, nearSurface01),
            ComputeCornerRoofThrottle(hoverFR, nearSurface01),
            ComputeCornerRoofThrottle(hoverRL, nearSurface01),
            ComputeCornerRoofThrottle(hoverRR, nearSurface01));
    }

    /// <summary>
    /// Down-thrust demand for one roof corner, from its paired bottom hover node:
    /// active when the corner rises too fast (rebound damping) or floats above its
    /// cushion target (height correction). Uses the same fresh instantaneous
    /// velocity form as the bottom damper — see the note there.
    /// </summary>
    private float ComputeCornerRoofThrottle(ThrusterNode hoverNode, float nearSurface01)
    {
        if (hoverNode == null || !hoverNode.IsGrounded || hoverNode.GroundDistance < 0f || rb == null)
        {
            return 0f;
        }

        float heightError = hoverNode.GroundDistance - hoverHeight;
        float height01 = Mathf.InverseLerp(
            roofHeightErrorStart,
            Mathf.Max(roofHeightErrorStart + 0.01f, roofHeightErrorFull),
            heightError
        );

        float cornerUpVel = Vector3.Dot(rb.GetPointVelocity(hoverNode.transform.position), hoverNode.GroundNormal);
        float rebound01 = Mathf.InverseLerp(
            roofReboundVelocityStart,
            Mathf.Max(roofReboundVelocityStart + 0.01f, roofReboundVelocityFull),
            cornerUpVel
        );

        return Mathf.Clamp01(Mathf.Max(height01, rebound01)) * nearSurface01 * roofStabilizerMaxThrottle;
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
    //  DYNAMIC SUSPENSION
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Measures how fast the ground surface rotates under the craft and derives:
    /// <list type="bullet">
    /// <item><b>_speedStiffness</b> — suspension spring/damping/ceiling multiplier from speed.</item>
    /// <item><b>_surfaceAngularVelocity</b> — rotation rate of the ground normal (rad/s).
    /// The alignment damping uses this as its target so it stops fighting the very
    /// rotation the craft needs to follow a loop.</item>
    /// <item><b>_curvatureAccel</b> — signed centripetal demand (m/s² along the normal):
    /// riding a CONCAVE surface at speed v with the normal rotating at ω needs a
    /// sustained v·ω push away from the surface (going up/around a loop);
    /// a CONVEX surface (crest, loop descent) yields a negative value — the cushion
    /// backs off so the craft follows the road instead of being launched off it.</item>
    /// </list>
    /// </summary>
    private void UpdateDynamicSuspension(CraftTelemetry telemetry, float dt)
    {
        _speedStiffness = dynamicSuspension
            ? Mathf.Lerp(1f, Mathf.Max(1f, maxSpeedStiffness),
                Mathf.InverseLerp(stiffenStartSpeed, Mathf.Max(stiffenStartSpeed + 1f, stiffenFullSpeed), telemetry.speed))
            : 1f;

        // Track-section context (SectionAdaptiveSuspension): a loop may want a
        // stiffer ride than a straight at the same speed.
        _speedStiffness *= Mathf.Max(0.1f, ContextStiffnessMultiplier);

        // Asymmetric smoothing: demand may RISE at the attack rate (noise rejection
        // on the faceted ring mesh), but must RELEASE much faster — a centripetal
        // push that outlives its curve becomes a launch on the flat road after it.
        float attack = 1f - Mathf.Exp(-Mathf.Max(0.01f, curvatureSmoothing) * dt);
        float release = 1f - Mathf.Exp(-Mathf.Max(Mathf.Max(0.01f, curvatureSmoothing), curvatureReleaseSmoothing) * dt);

        // No reliable surface under the craft: release the curvature state.
        if (telemetry.groundedFactor <= 0.05f || telemetry.groundNormal.sqrMagnitude < 0.001f)
        {
            _hasPrevGroundNormal = false;
            _hasSmoothSurfaceNormal = false;
            _surfaceAngularVelocity = Vector3.Lerp(_surfaceAngularVelocity, Vector3.zero, release);
            _curvatureAccel = Mathf.Lerp(_curvatureAccel, 0f, release);
            return;
        }

        Vector3 n = telemetry.groundNormal.normalized;

        // Facet-filtered surface reference: everything downstream (per-corner vertical
        // velocity, roof rebound, alignment) measures against THIS smooth surface, not
        // the raw stepped facet normals of the ring-built collider.
        _smoothSurfaceNormal = _hasSmoothSurfaceNormal ? PredictCorrectNormal(_smoothSurfaceNormal, n, dt) : n;
        _hasSmoothSurfaceNormal = true;

        if (!_hasPrevGroundNormal)
        {
            _prevGroundNormal = n;
            _hasPrevGroundNormal = true;
            return;
        }

        Vector3 dN = (n - _prevGroundNormal) / Mathf.Max(dt, 1e-5f);
        _prevGroundNormal = n;

        // ω⊥ = N × dN/dt (the normal's rotation rate, yaw-free). Clamped against
        // single-frame spikes from contact-count changes on rough geometry.
        Vector3 rawAngVel = Vector3.ClampMagnitude(Vector3.Cross(n, dN), 25f);

        // Signed centripetal demand: -(dN/dt)·v = +v²/R on concave, -v²/R on convex.
        float rawCurvature = compensateSurfaceCurvature
            ? Mathf.Clamp(-Vector3.Dot(dN, telemetry.worldVelocity), -maxCurvatureAccel, maxCurvatureAccel)
            : 0f;

        // ── Predictive pre-release (loop exit / valley end) ──
        // The alignment probes already sampled the surface AHEAD. If that surface
        // has stopped curving — its normal differs from the current one by much
        // less than the current rotation rate predicts — the concave push tapers
        // off BEFORE the flat road arrives instead of 20 m after it.
        if (rawCurvature > 0f && lookAheadCurvatureRelease > 0.001f && _hasPredictedSurface && telemetry.speed > 5f)
        {
            float travelTime = Mathf.Max(0.01f, _predictedSurfaceDistance / Mathf.Max(1f, telemetry.speed));
            float expectedRotation = _surfaceAngularVelocity.magnitude * travelTime; // radians if curvature continued
            if (expectedRotation > 0.02f)
            {
                float actualRotation = Vector3.Angle(n, _predictedSurfaceNormal) * Mathf.Deg2Rad;
                float continuation01 = Mathf.Clamp01(actualRotation / expectedRotation);
                rawCurvature *= Mathf.Lerp(1f, continuation01, lookAheadCurvatureRelease);
            }
        }

        _surfaceAngularVelocity = Vector3.Lerp(_surfaceAngularVelocity, rawAngVel,
            rawAngVel.sqrMagnitude >= _surfaceAngularVelocity.sqrMagnitude ? attack : release);

        _curvatureAccel = Mathf.Lerp(_curvatureAccel, rawCurvature,
            Mathf.Abs(rawCurvature) >= Mathf.Abs(_curvatureAccel) ? attack : release);
    }

    /// <summary>
    /// Predict-correct normal filter: first ADVANCE the filtered normal by the
    /// surface's own measured rotation (so filtering adds zero lag while riding a
    /// loop, where the true normal legitimately rotates hundreds of °/s), then blend
    /// gently toward the raw measurement to absorb the facet steps of the ring-built
    /// collider. Plain smoothing would lag the loop by ω·τ — tens of degrees.
    /// </summary>
    private Vector3 PredictCorrectNormal(Vector3 current, Vector3 measured, float dt)
    {
        float w = _surfaceAngularVelocity.magnitude;
        if (w > 1e-4f)
        {
            current = Quaternion.AngleAxis(w * Mathf.Rad2Deg * dt, _surfaceAngularVelocity / w) * current;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, surfaceNormalSmoothing) * dt);
        return Vector3.Slerp(current, measured, t).normalized;
    }

    // ══════════════════════════════════════════════════════════════
    //  SURFACE ALIGNMENT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Spring-damper torque that rotates the craft's up axis toward the (possibly
    /// predicted) surface normal. Roll and pitch only — yaw is never touched, so
    /// steering stays entirely with VectorThrusterArray. Player pitch/roll input
    /// suppresses its axis of the alignment exactly like it suppresses auto-level,
    /// so the pilot can still lean/carve on top of the surface-following attitude.
    /// </summary>
    private void ApplySurfaceAlignment(CraftTelemetry telemetry)
    {
        if (!alignToSurface || rb == null) return;

        Vector3 rawTarget = SampleAlignmentTargetNormal(telemetry, out float predictiveWeight);

        // Aligned strength: full when grounded; while airborne only the predictive
        // probes (an approaching ramp face / landing surface) drive alignment.
        float alignWeight = Mathf.Max(Mathf.Clamp01(telemetry.groundedFactor), predictiveWeight);
        if (alignWeight <= 0.001f)
        {
            _hasSmoothAlignTarget = false;
            return;
        }

        // Same facet filter as the hover reference: the raw target hops a few degrees
        // at every ring boundary (and the look-ahead probe hops facet to facet) — the
        // alignment spring must chase the smooth surface, not the polygon.
        float dt = Time.fixedDeltaTime;
        _smoothAlignTarget = _hasSmoothAlignTarget ? PredictCorrectNormal(_smoothAlignTarget, rawTarget, dt) : rawTarget;
        _hasSmoothAlignTarget = true;
        Vector3 targetNormal = _smoothAlignTarget;

        // Motocross nose-up bias: at speed, tilt the target attitude a few degrees
        // nose-high relative to the surface. The front floats over facet edges and
        // small bumps while the (extra-damped) rear carries the load.
        float speed01 = Mathf.InverseLerp(stiffenStartSpeed, Mathf.Max(stiffenStartSpeed + 1f, stiffenFullSpeed), telemetry.speed);
        float noseUp = highSpeedNoseUpBias * speed01 + ContextNoseUpDegrees;
        if (Mathf.Abs(noseUp) > 0.01f)
        {
            // Negative angle around craft-right tilts the up-target backward = nose rises.
            targetNormal = Quaternion.AngleAxis(-noseUp, transform.right) * targetNormal;
        }

        _alignTargetNormal = targetNormal;

        Vector3 currentUp = transform.up;

        // At speed the whole alignment system scales up with the suspension:
        // following a loop at racing speed needs several times the flat-track
        // rotation authority. The section context adds its own scaling on top
        // (e.g. extra alignment authority inside loops/corkscrews).
        float stiffness = (dynamicSuspension ? _speedStiffness : 1f)
                          * Mathf.Max(0.1f, ContextAlignmentMultiplier);

        // Rotation axis and angle error between craft up and the target normal.
        Vector3 cross = Vector3.Cross(currentUp, targetNormal);
        float crossMag = cross.magnitude;
        float angleError = Mathf.Atan2(crossMag, Vector3.Dot(currentUp, targetNormal)); // radians

        Vector3 torque = crossMag > 1e-6f
            ? (cross / crossMag) * (angleError * surfaceAlignStrength * stiffness)
            : Vector3.zero;

        // Damp roll/pitch spin RELATIVE to the surface's own rotation rate — never
        // toward zero. On a loop the ground normal itself rotates fast (v/R can be
        // hundreds of °/s); damping toward zero would fight exactly the rotation
        // the craft needs to hold, stalling it against the wall. Yaw damping stays
        // with VectorThrusterArray.
        Vector3 angVel = rb.angularVelocity;
        Vector3 rollPitchAngVel = angVel - currentUp * Vector3.Dot(angVel, currentUp);
        Vector3 surfaceRollPitchAngVel = _surfaceAngularVelocity - currentUp * Vector3.Dot(_surfaceAngularVelocity, currentUp);
        torque -= (rollPitchAngVel - surfaceRollPitchAngVel) * surfaceAlignDamping;

        // Player authority: an actively commanded axis backs the alignment off,
        // reusing the same suppression knobs as auto-level so the two never disagree.
        float inputRoll01 = maxPlayerRollTarget > 0f
            ? Mathf.Clamp01(Mathf.Abs(_leanTargetRoll) / maxPlayerRollTarget)
            : 0f;
        float inputPitch01 = maxPlayerPitchTarget > 0f
            ? Mathf.Clamp01(Mathf.Abs(_leanTargetPitch) / maxPlayerPitchTarget)
            : 0f;

        Vector3 local = transform.InverseTransformDirection(torque);
        local.x *= Mathf.Clamp01(1f - inputPitch01 * autoLevelSuppressionByPitchInput); // pitch axis
        local.z *= Mathf.Clamp01(1f - inputRoll01 * autoLevelSuppressionByRollInput);   // roll axis
        local.y = 0f;                                                                   // never inject yaw
        torque = transform.TransformDirection(local);

        torque *= alignWeight;
        torque = Vector3.ClampMagnitude(torque, Mathf.Max(0f, maxSurfaceAlignTorque * stiffness));

        rb.AddTorque(torque, ForceMode.Acceleration);
    }

    /// <summary>
    /// Chooses the surface normal the craft should align to.
    /// Base: the current averaged ground normal from telemetry. On top of that,
    /// two predictive probes look at where the craft is GOING:
    /// <list type="number">
    /// <item>A ray along the velocity vector — catches a ramp face, loop entry, or
    /// wall directly in the travel path (the nose-slam case) so the craft pitches
    /// up BEFORE contact.</item>
    /// <item>A drop probe at the predicted position — catches crests, drop-offs and
    /// landing surfaces so the craft matches them ahead of touchdown.</item>
    /// </list>
    /// </summary>
    private Vector3 SampleAlignmentTargetNormal(CraftTelemetry telemetry, out float predictiveWeight)
    {
        predictiveWeight = 0f;
        _alignForwardHit = false;
        _alignDownHit = false;
        _hasPredictedSurface = false;

        // Current surface (or no correction at all when airborne with no contact).
        Vector3 target = telemetry.groundedFactor > 0.001f && telemetry.groundNormal.sqrMagnitude > 0.001f
            ? telemetry.groundNormal.normalized
            : transform.up;

        // A hull contact immediately after flight is stronger landing evidence than
        // the local-down hover rays. Those rays may all be pointing beside a flat
        // road when the craft touches down with residual air attitude. Use the real
        // contact normal only during the airborne -> grounded handoff; once any hover
        // probe establishes normal grounded telemetry, the usual controller owns the
        // surface reference again.
        if (TryGetLandingCollisionNormal(telemetry, out Vector3 landingNormal))
        {
            _hasPredictedSurface = true;
            _predictedSurfaceNormal = landingNormal;
            _predictedSurfaceDistance = 0f;
            predictiveWeight = 1f;
            return landingNormal;
        }

        float lookAhead = Mathf.Min(
            telemetry.speed * Mathf.Max(0f, surfaceLookAheadTime),
            Mathf.Max(0f, maxSurfaceLookAheadDistance));

        if (surfaceLookAheadBlend <= 0.001f || lookAhead < 0.5f)
            return target;

        Vector3 moveDir = telemetry.worldVelocity.normalized;
        Vector3 origin = rb.position;

        Vector3 predictedNormal = Vector3.zero;
        float weight = 0f;

        // 1) Surface face directly in the travel path.
        if (Physics.Raycast(origin, moveDir, out RaycastHit fwdHit, lookAhead, surfaceProbeLayers, QueryTriggerInteraction.Ignore)
            && (fwdHit.rigidbody == null || fwdHit.rigidbody != rb))
        {
            predictedNormal = fwdHit.normal;

            // The closer the face, the harder the craft commits to it.
            float proximity01 = 1f - Mathf.Clamp01(fwdHit.distance / lookAhead);
            weight = surfaceLookAheadBlend * Mathf.Lerp(0.5f, 1f, proximity01);

            _alignForwardHit = true;
            _alignForwardHitPoint = fwdHit.point;

            _hasPredictedSurface = true;
            _predictedSurfaceNormal = fwdHit.normal;
            _predictedSurfaceDistance = fwdHit.distance;
        }
        else
        {
            // 2) Surface under the predicted position.
            _alignDownProbeOrigin = origin + moveDir * lookAhead + transform.up * 2f;
            float probeRange = hoverHeight + hoverActivationHeightBuffer + 8f;

            if (Physics.Raycast(_alignDownProbeOrigin, -transform.up, out RaycastHit downHit, probeRange, surfaceProbeLayers, QueryTriggerInteraction.Ignore)
                && (downHit.rigidbody == null || downHit.rigidbody != rb))
            {
                predictedNormal = downHit.normal;
                weight = surfaceLookAheadBlend * 0.5f;

                _alignDownHit = true;
                _alignDownHitPoint = downHit.point;

                _hasPredictedSurface = true;
                _predictedSurfaceNormal = downHit.normal;
                _predictedSurfaceDistance = lookAhead;
            }
        }

        if (weight > 0f)
        {
            target = Vector3.Slerp(target, predictedNormal, weight).normalized;
            predictiveWeight = weight;
        }

        return target;
    }

    /// <summary>
    /// Arms contact-backed surface reacquisition only while the craft is genuinely
    /// away from its hover cushion. A normal grounded frame ends the handoff, so
    /// ordinary road and wall handling never reads collision normals through this path.
    /// </summary>
    private void UpdateLandingReacquisitionState(CraftTelemetry telemetry)
    {
        if (telemetry.groundedFactor <= 0.05f)
        {
            _landingReacquisitionArmed = true;
            return;
        }

        if (telemetry.isGrounded)
        {
            _landingReacquisitionArmed = false;
            _landingCollisionSeenAt = float.NegativeInfinity;
        }
    }

    private bool TryGetLandingCollisionNormal(CraftTelemetry telemetry, out Vector3 normal)
    {
        normal = Vector3.up;

        if (!_landingReacquisitionArmed || telemetry.isGrounded)
        {
            return false;
        }

        if (Time.fixedTime - _landingCollisionSeenAt > LandingCollisionMemorySeconds
            || _landingCollisionNormal.sqrMagnitude < 0.001f)
        {
            return false;
        }

        normal = _landingCollisionNormal.normalized;
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        CaptureLandingCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CaptureLandingCollision(collision);
    }

    /// <summary>
    /// Records the surface normal from an actual hull landing while probe-based
    /// grounded state is still absent. Contact normals are oriented toward the craft
    /// before averaging so multi-collider hull contacts cannot cancel each other.
    /// </summary>
    private void CaptureLandingCollision(Collision collision)
    {
        if (!_landingReacquisitionArmed || collision == null || collision.contactCount <= 0)
        {
            return;
        }

        Vector3 craftCenter = rb != null ? rb.worldCenterOfMass : transform.position;
        Vector3 normalSum = Vector3.zero;
        int validContacts = 0;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            Vector3 contactNormal = contact.normal;

            if (contactNormal.sqrMagnitude < 0.001f)
            {
                continue;
            }

            if (Vector3.Dot(contactNormal, craftCenter - contact.point) < 0f)
            {
                contactNormal = -contactNormal;
            }

            normalSum += contactNormal.normalized;
            validContacts++;
        }

        if (validContacts <= 0 || normalSum.sqrMagnitude < 0.001f)
        {
            return;
        }

        _landingCollisionNormal = normalSum.normalized;
        _landingCollisionSeenAt = Time.fixedTime;
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
    /// oscillation, and exponential response smoothing.
    /// </summary>
    private void SetHoverThrottle(
        ThrusterNode node,
        ref float smoothedThrottle,
        float rollDemand,
        float pitchDemand,
        bool isLeft,
        bool isFront,
        CraftTelemetry telemetry)
    {
        if (node == null) return;

        float throttle = 0f;

        // ── Grounded PD hover ─────────────────────────────────
        bool hoverCushionActive = false;
        float cushionVertVel = 0f;
        float cushionMaxThrottle = hoverMaxThrottle;

        if (node.IsGrounded)
        {
            float targetHeight = GetCornerHoverTargetHeight(isLeft, isFront, telemetry);
            float hoverInfluence = GetHoverCushionInfluence(node.GroundDistance, targetHeight);

            hoverCushionActive = hoverInfluence > 0.001f;

            if (hoverCushionActive)
            {
                float heightError = targetHeight - node.GroundDistance;

                // Soft deadband: sub-threshold height errors are facet polish on the
                // ring-built track, not real errors. Without this, four corners
                // "fix" ±cm ripple independently and pump the craft like a lowrider.
                if (heightErrorDeadband > 0.001f)
                {
                    heightError -= heightErrorDeadband *
                        (float)System.Math.Tanh(heightError / heightErrorDeadband);
                }

                // Corner vertical velocity: INSTANTANEOUS point velocity projected on
                // the corner's LOCAL facet normal. This is deliberately the simple
                // form — three "smarter" replacements all failed:
                //  · shared smoothed normal → loop rotation read as front-rise/rear-fall
                //    (nose-dive);
                //  · velocity onto any filtered normal → 1° of filter transient = 7 m/s
                //    phantom at racing speed (belly-slide through loops);
                //  · smoothed ray-distance derivative → LAGGED damping at full authority
                //    is an oscillator at 100 Hz physics (constant bouncing).
                // A damping signal must above all be FRESH. The local facet normal is
                // exact within its facet, and its zero-mean edge noise is handled in the
                // position domain (heightErrorDeadband) and by section-adaptive tuning —
                // never by filtering the velocity.
                float dt = Time.fixedDeltaTime;
                float vertVel = Vector3.Dot(rb.GetPointVelocity(node.transform.position), node.GroundNormal);
                float stiffness = _speedStiffness;
                float nodeForce = Mathf.Max(1f, node.maxForce);

                // Curvature feed-forward (per node, 4-way load share): sustained
                // centripetal push on concave surfaces (loops!), reduced baseline on
                // convex ones so the craft follows crests/descents instead of
                // launching off them. This is force the PD terms could never supply
                // in time at very high speed.
                float curvatureThrottle = _curvatureAccel / (4f * nodeForce);

                // ── Discrete-time-stable damping ───────────────
                // Naive "-vertVel * Kd" throttle becomes UNSTABLE once
                // Kd·4·maxForce·dt exceeds ~1: the per-tick correction overshoots
                // the velocity and reverses it harder every tick — the damper
                // itself turns into a bounce generator (this is exactly what made
                // the stiffened suspension bouncy). The exponential form removes at
                // most 100% of the velocity per tick regardless of how stiff the
                // damping gets, so it stays stable at ANY gain and timestep.
                float axleKd = GetEffectiveHoverDamping(vertVel) * stiffness
                               * (isFront ? 1f : Mathf.Max(0f, rearDampingMultiplier))
                               * Mathf.Max(0.1f, ContextDampingMultiplier);
                float dampingRate = axleKd * 4f * nodeForce;               // 1/s
                float stableFactor = 1f - Mathf.Exp(-dampingRate * dt);    // fraction of vertVel removed this tick
                float dampingThrottle = -vertVel * stableFactor / Mathf.Max(0.0001f, dt * 4f * nodeForce);

                float axleKp = hoverKp * stiffness * (isFront ? 1f : Mathf.Max(0f, rearSpringMultiplier));

                throttle = _gravityThrottle + curvatureThrottle
                           + heightError * axleKp
                           + dampingThrottle;

                // Concave curvature demand gets its own throttle headroom — a loop at
                // racing speed legitimately needs several times the flat-ground ceiling.
                cushionMaxThrottle = hoverMaxThrottle * stiffness + Mathf.Max(0f, curvatureThrottle);

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

                // ── Anti-launch cap ────────────────────────────
                // The cushion may arrest a fall at FULL power, but must never act
                // like a loaded spring that fires the craft back out: cap this
                // corner's throttle so the predicted vertical velocity after this
                // tick cannot exceed maxCushionRecoverySpeed. (The four hover
                // nodes are assumed to share the load equally; ForceMode is
                // Acceleration, so throttle·maxForce is this node's contribution.)
                // Falling hard → cap is huge → full braking power stays available.
                // Compressed with v ≈ 0 → cap ≈ baseline hold + gentle climb, so
                // the stored "spring" energy of a deep compression is discarded
                // instead of being converted into a massive jump.
                // The baseline INCLUDES the concave curvature demand: holding the
                // line through a loop requires sustained force at vertVel ≈ 0 and
                // must never be mistaken for a launch.
                float maxUsefulThrottle = _gravityThrottle + Mathf.Max(0f, curvatureThrottle) +
                    (maxCushionRecoverySpeed - vertVel) / Mathf.Max(0.0001f, dt * 4f * nodeForce);
                throttle = Mathf.Min(throttle, Mathf.Max(0f, maxUsefulThrottle));

                cushionVertVel = vertVel;
            }
        }

        // ── Airborne attitude differential ────────────────────
        // Left thrusters push right-roll; front thrusters push nose-down pitch.
        float rollAdjust = isLeft ? rollDemand : -rollDemand;
        float pitchAdjust = isFront ? -pitchDemand : pitchDemand;

        throttle += rollAdjust + pitchAdjust;

        // ── Clamp ─────────────────────────────────────────────
        throttle = Mathf.Clamp(throttle, 0f, cushionMaxThrottle);

        // ── Exponential response smoothing ────────────────────
        bool hasAirAttitudeDemand = Mathf.Abs(rollDemand) > 0.001f || Mathf.Abs(pitchDemand) > 0.001f;
        float smoothed = SmoothHoverThrottle(node, smoothedThrottle, throttle, hoverCushionActive || hasAirAttitudeDemand);

        // ── Emergency catch ───────────────────────────────────
        // Rise smoothing exists for hover FEEL, but on a hard fall it delays the
        // catch until the hull hits the collider — the impact bounce then reads as
        // a violent kick. The faster the fall, the more the smoothing is bypassed
        // so the full braking throttle arrives while still inside the cushion.
        if (hoverCushionActive && cushionVertVel < 0f && throttle > smoothed)
        {
            float catch01 = Mathf.InverseLerp(
                emergencyCatchStartSpeed,
                Mathf.Max(emergencyCatchStartSpeed + 0.01f, emergencyCatchFullSpeed),
                -cushionVertVel);
            smoothed = Mathf.Lerp(smoothed, throttle, catch01);
        }

        smoothedThrottle = smoothed;

        // ── Route to bus ──────────────────────────────────────
        thrusterBus.SetThrottle(node, smoothed, ThrusterPowerChannel.BaseHover);
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
    /// airborne decay. This prevents landing lift from lingering and injecting
    /// extra energy into the next bounce.
    /// </summary>
    private float SmoothHoverThrottle(ThrusterNode node, float currentThrottle, float targetThrottle, bool forceContextActive)
    {
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
    //  EDITOR GIZMOS
    // ══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    /// <summary>
    /// Surface-alignment debug: green arrow = alignment target normal, cyan line =
    /// forward travel probe hit, yellow line = predicted-position drop probe hit.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!alignToSurface || !Application.isPlaying) return;

        Vector3 p = transform.position;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(p, p + _alignTargetNormal * 4f);
        Gizmos.DrawWireSphere(p + _alignTargetNormal * 4f, 0.15f);

        if (_alignForwardHit)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p, _alignForwardHitPoint);
            Gizmos.DrawWireSphere(_alignForwardHitPoint, 0.3f);
        }

        if (_alignDownHit)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_alignDownProbeOrigin, _alignDownHitPoint);
            Gizmos.DrawWireSphere(_alignDownHitPoint, 0.3f);
        }
    }
#endif
}
