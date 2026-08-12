using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Hovercraft camera rig for racing + trick gameplay.
/// P cycles camera profiles (Chase Far / Chase Close / Cockpit / Nose);
/// O toggles ground-orientation following.
///
/// Design:
/// - Profile list (racing-game style view modes) on top of two rig types:
///   third-person chase camera and first-person mounted camera.
/// - Built around a smoothed REFERENCE FRAME instead of hard-coded world up:
///   grounded, the frame follows the track surface normal (wall rides, half-pipes,
///   loops, inverted driving); airborne, it relaxes back to world up so flips
///   and twists never spin the camera.
/// - Zero steady-state position lag: the camera is anchored rigidly to the craft
///   and all smoothing happens in the offset/rotation domain. At 250+ m/s a
///   world-space SmoothDamp would trail tens of meters behind — this rig never does.
/// - Third person: follows movement intention (velocity blended with craft forward),
///   speed-scaled distance/look-ahead/FOV tuned for the 0–280 m/s regime,
///   drift side-framing, pop-free collision avoidance.
/// - First person: cockpit camera hard-locked to a mount point, rotation follows
///   the craft with slight damping, yaw-lead into turns, mild horizon softening.
/// - Uses CraftCore telemetry (grounded factor, surface normal, yaw rate) when available.
/// </summary>
[RequireComponent(typeof(Camera))]
public class HovercraftCamera : MonoBehaviour
{
    public enum CameraPerspective
    {
        ThirdPerson,
        FirstPerson
    }

    public enum GroundOrientationMode
    {
        /// <summary>Always follow the track surface orientation while grounded.</summary>
        On,

        /// <summary>Never follow — camera stays world-up stabilized.</summary>
        Off,

        /// <summary>
        /// Follow only when the surface tilts meaningfully away from world up
        /// (loops, corkscrews, walls). Stays world-stabilized on regular ground,
        /// blending in/out between the dynamic threshold angles.
        /// </summary>
        Dynamic
    }

    /// <summary>
    /// One camera view mode, cycled with the profile key like view modes in
    /// racing games. Per-profile fields cover framing and FOV; everything else
    /// (smoothing, reference frame, collision, look-ahead feel) is shared rig
    /// tuning on the main component.
    /// </summary>
    [System.Serializable]
    public class CameraProfile
    {
        public string profileName = "Camera";
        public CameraPerspective perspective = CameraPerspective.ThirdPerson;

        [Tooltip("How this profile follows the track surface orientation. Dynamic = only inside loops/corkscrews/walls. The global O-key switch overrides all profiles when off.")]
        public GroundOrientationMode groundOrientation = GroundOrientationMode.On;

        [Header("Third Person (used when perspective = ThirdPerson)")]
        public float distanceBase = 8f;
        public float distanceMax = 13f;
        public float heightBase = 2.8f;
        public float heightMax = 3.6f;
        public float lookAheadBase = 4f;
        public float lookAheadMax = 26f;

        [Header("First Person (used when perspective = FirstPerson)")]
        [Tooltip("Mount point in craft local space. Position is hard-locked here every frame.")]
        public Vector3 mountOffset = new Vector3(0f, 0.85f, 0.35f);

        [Header("FOV")]
        public float fovBase = 58f;
        public float fovMax = 84f;

        [Header("Follow Direction")]
        [Tooltip("How much the camera direction follows the craft's NOSE vs its velocity. 1 = default blend, 0 = pure velocity — drift/grip-breaker angles never drag the view off the road.")]
        [Range(0f, 1f)] public float noseInfluence = 1f;

        [Header("Dynamic Framing (loops / corkscrews)")]
        [Tooltip("Blend to a tighter, lower, upward-looking frame while the track is engaged as a loop/corkscrew (rollercoaster feel). Uses the dynamic-orientation detector regardless of this profile's orientation mode.")]
        public bool dynamicFraming = false;

        [Tooltip("Chase distance while engaged in a loop/corkscrew.")]
        public float engagedDistance = 5.5f;

        [Tooltip("Camera height while engaged. Lower than normal = looking slightly up at the craft.")]
        public float engagedHeight = 1.3f;

        [Tooltip("Extra aim-point height while engaged — raises the view like lifting your head on a rollercoaster.")]
        public float engagedLookUp = 1.6f;
    }

    public static List<CameraProfile> CreateDefaultProfiles()
    {
        return new List<CameraProfile>
        {
            new CameraProfile
            {
                profileName = "Chase Far",
                perspective = CameraPerspective.ThirdPerson,
                distanceBase = 8f, distanceMax = 13f,
                heightBase = 2.8f, heightMax = 3.6f,
                lookAheadBase = 4f, lookAheadMax = 26f,
                fovBase = 58f, fovMax = 84f
            },
            new CameraProfile
            {
                profileName = "Chase Close",
                perspective = CameraPerspective.ThirdPerson,
                groundOrientation = GroundOrientationMode.Dynamic,
                distanceBase = 5f, distanceMax = 8.5f,
                heightBase = 1.5f, heightMax = 2f,
                lookAheadBase = 6f, lookAheadMax = 32f,
                fovBase = 60f, fovMax = 86f
            },
            new CameraProfile
            {
                profileName = "Chase Dynamic",
                perspective = CameraPerspective.ThirdPerson,
                groundOrientation = GroundOrientationMode.Dynamic,
                distanceBase = 9f, distanceMax = 14f,
                heightBase = 3.2f, heightMax = 4.2f,
                lookAheadBase = 5f, lookAheadMax = 28f,
                fovBase = 59f, fovMax = 85f,
                noseInfluence = 0f,
                dynamicFraming = true,
                engagedDistance = 5.5f, engagedHeight = 1.3f, engagedLookUp = 1.6f
            },
            new CameraProfile
            {
                profileName = "Cockpit",
                perspective = CameraPerspective.FirstPerson,
                mountOffset = new Vector3(0f, 0.85f, 0.35f),
                fovBase = 68f, fovMax = 92f
            },
            new CameraProfile
            {
                profileName = "Nose",
                perspective = CameraPerspective.FirstPerson,
                mountOffset = new Vector3(0f, 0.3f, 2.15f),
                fovBase = 70f, fovMax = 95f
            }
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  TARGET
    // ══════════════════════════════════════════════════════════════

    [Header("Target")]
    [Tooltip("The hovercraft transform to follow.")]
    public Transform target;

    [Tooltip("Craft Rigidbody for velocity readings. Auto-found from target if unset.")]
    public Rigidbody targetRigidbody;

    [Tooltip("Optional CraftCore. If assigned, camera uses telemetry (grounded factor, surface normal, yaw rate).")]
    public CraftCore craftCore;

    // ══════════════════════════════════════════════════════════════
    //  PERSPECTIVE
    // ══════════════════════════════════════════════════════════════

    [Header("Profiles")]
    [Tooltip("Camera view modes cycled with the profile key, like in racing games.")]
    public List<CameraProfile> profiles = CreateDefaultProfiles();

    [Tooltip("Profile active on start (index into Profiles).")]
    public int startProfileIndex = 0;

    [Tooltip("Key that cycles through camera profiles.")]
    public Key profileCycleKey = Key.P;

    [Tooltip("Key that toggles surface orientation following on/off (the Reference Frame master switch).")]
    public Key groundOrientationToggleKey = Key.O;

    // ══════════════════════════════════════════════════════════════
    //  SPEED SCALING
    // ══════════════════════════════════════════════════════════════

    [Header("Speed Scaling")]
    [Tooltip("Speed (m/s) where speed-driven camera effects START. Below this the camera sits at its base framing. Manoeuvring speed, not a standstill — the craft spends almost no time near 0, so anchoring the ramp at 0 spends most of the effect range before the car feels quick. 60 m/s ≈ 216 km/h.")]
    public float speedForMinEffects = 60f;

    [Tooltip("Speed (m/s) at which all speed-driven camera effects reach maximum. Set this at or slightly above the craft's realistic top speed — anything faster looks IDENTICAL, so a low value silently flattens the whole high-speed range. 380 m/s ≈ 1370 km/h.")]
    public float speedForMaxEffects = 380f;

    [Tooltip("Response curve across the min→max band. 1 = linear. >1 holds the effect back and delivers it at the top end (speed keeps building as you go faster). <1 front-loads it into the low end.")]
    [Range(0.3f, 2.5f)] public float speedResponseExponent = 1.25f;

    // ══════════════════════════════════════════════════════════════
    //  REFERENCE FRAME (shared by both perspectives)
    // ══════════════════════════════════════════════════════════════

    [Header("Reference Frame")]
    [Tooltip("Master switch: follow the track surface orientation while grounded. Disable to keep the camera world-up stabilized at all times (same behavior as airborne). Loops/wall-rides will no longer tilt the camera.")]
    public bool followSurfaceOrientation = true;

    [Tooltip("How much the camera up follows the track surface normal while grounded. 1 = fully ride walls/loops.")]
    [Range(0f, 1f)] public float surfaceUpInfluence = 1f;

    [Tooltip("How much of the surface BANKING (roll around the travel direction) the camera follows. Pitch following (loops, crests, drops) is always full. Lower = calmer horizon in banked turns at high speed; 0 = horizon never banks.")]
    [Range(0f, 1f)] public float surfaceBankInfluence = 0.6f;

    [Tooltip("Hard cap on the banking tilt the camera takes from the surface (degrees). Riding vertical or overhead surfaces (walls, loops) is not affected by this cap.")]
    public float maxSurfaceBankAngle = 55f;

    [Tooltip("Dynamic mode: CLIMB angle of the flight path (degrees up/down from horizontal) where following starts blending in. Loops climb steeply; banked corners produce zero climb, so they can never engage this.")]
    public float dynamicPitchStartAngle = 25f;

    [Tooltip("Keep the horizon near-upright on surfaces that are steeply tilted but are NOT stunts — wallrides above all. A wallride stands the surface 75-90 deg over without being a feature, and the camera would otherwise follow it all the way and roll the horizon. Only loops/corkscrews/half-loops lift the cap.")]
    public bool limitTiltOutsideFeatures = true;

    [Tooltip("Maximum camera tilt from world up while NOT in a loop/corkscrew. Wallrides and banked corners are held to this; genuine features lift it smoothly to unrestricted. Set to 180 to disable the cap without unticking the toggle above.")]
    [Range(0f, 180f)] public float maxUnengagedTiltAngle = 55f;

    [Tooltip("Dynamic mode: climb angle where following is fully engaged.")]
    public float dynamicPitchFullAngle = 50f;

    [Tooltip("Dynamic mode: surface BANK tilt (roll around the travel direction) where following starts blending in. Keep this ABOVE the track's maximum corner banking (75°).")]
    public float dynamicBankStartAngle = 76f;

    [Tooltip("Dynamic mode: surface bank tilt where following is fully engaged. Keep a WIDE gap from the start angle: the craft rolls through a corkscrew fast, so a narrow band is crossed in a fraction of a second and the camera snaps rather than blends.")]
    public float dynamicBankFullAngle = 96f;

    [Tooltip("Dynamic mode: yaw rate (deg/sec) where bank-based engagement starts being suppressed. Turning + banked = a corner, not a corkscrew.")]
    public float dynamicYawSuppressStart = 10f;

    [Tooltip("Dynamic mode: yaw rate where bank-based engagement is fully suppressed.")]
    public float dynamicYawSuppressFull = 30f;

    [Tooltip("How quickly dynamic framing ENGAGES when entering a loop/corkscrew. The camera should arrive with the feature, so this stays reasonably brisk.")]
    public float dynamicFramingResponse = 3f;

    [Tooltip("How quickly dynamic framing RELEASES when leaving a loop/corkscrew. Deliberately SLOWER than engaging: a feature briefly dipping below the engagement threshold (mid-corkscrew, between loop and rollout) would otherwise pop the camera all the way out and straight back in. Slow release rides through those dips.")]
    public float dynamicFramingReleaseResponse = 1.1f;

    [Header("Track Section Data")]
    [Tooltip("Use the generated track's section metadata (authoritative Loop/Corkscrew knowledge with arc-based anticipation) as the primary engagement signal. Realtime detection remains active as refinement and as fallback off-track / without a generated track.")]
    public bool useTrackSectionData = true;

    [Tooltip("Track section sensor. Auto-created on this GameObject if missing.")]
    public TrackSectionSensor trackSensor;

    [Tooltip("SECONDS of warning before a loop/corkscrew where engagement ramps in. This is the real anticipation control — a fixed metre distance cannot work across the craft's speed range (30 m is 0.6 s when crawling but only 0.08 s at design speed, which is a step, not a blend). The lead distance is derived from this and the current speed.")]
    public float sectionBlendInSeconds = 0.8f;

    [Tooltip("SECONDS after a loop/corkscrew where engagement ramps back out.")]
    public float sectionBlendOutSeconds = 0.5f;

    [Tooltip("Minimum metres BEFORE a loop/corkscrew for the ramp in, regardless of speed. Only binds at low speed, where the time-based lead would be shorter than the craft's own length.")]
    public float sectionBlendInDistance = 30f;

    [Tooltip("Minimum metres AFTER a loop/corkscrew for the ramp out, regardless of speed.")]
    public float sectionBlendOutDistance = 20f;

    [Tooltip("How quickly the reference up tracks the surface normal while grounded. Must be fast enough for loops.")]
    public float groundedUpResponse = 6f;

    [Tooltip("How quickly the reference up relaxes back to world up while airborne. Keep low so leaving a wall doesn't snap the view.")]
    public float airborneUpResponse = 2f;

    [Tooltip("Hard cap on reference-up rotation (deg/sec). Protects against normal pops on seams.")]
    public float maxUpDegreesPerSecond = 500f;

    [Tooltip("Below this flat speed (m/s) the camera follows craft forward instead of velocity.")]
    public float minFlatSpeedForDirection = 1.5f;

    [Tooltip("How quickly the follow direction tracks its target while grounded.")]
    public float groundedDirectionResponse = 5f;

    [Tooltip("How quickly the follow direction tracks its target while airborne. Lower = calmer during flips.")]
    public float airborneDirectionResponse = 3f;

    [Tooltip("How much the follow direction favors craft forward over velocity at LOW speed while grounded.")]
    [Range(0f, 1f)] public float lowSpeedForwardInfluence = 0.75f;

    [Tooltip("How much the follow direction favors craft forward over velocity at TOP speed. Low = trust velocity.")]
    [Range(0f, 1f)] public float highSpeedForwardInfluence = 0.1f;

    [Tooltip("How much the follow direction favors craft forward while airborne.")]
    [Range(0f, 1f)] public float airborneForwardInfluence = 0.2f;

    // ══════════════════════════════════════════════════════════════
    //  GROUND DETECTION FALLBACK (only used without CraftCore)
    // ══════════════════════════════════════════════════════════════

    [Header("Ground Detection Fallback")]
    [Tooltip("If true, grounded state comes from SetGrounded(). Ignored when craftCore is assigned.")]
    public bool useExternalGroundedState = false;

    [Tooltip("Raycast length for fallback grounded detection when no CraftCore is assigned.")]
    public float groundCheckDistance = 2.2f;

    [Tooltip("Layers counted as ground for fallback grounded detection.")]
    public LayerMask groundLayers = ~0;

    [Tooltip("Grounded/airborne blend speed for the fallback path.")]
    public float fallbackGroundedBlendSpeed = 6f;

    // ══════════════════════════════════════════════════════════════
    //  THIRD PERSON
    // ══════════════════════════════════════════════════════════════

    [Header("Third Person — Position")]
    [Tooltip("Extra distance added while airborne.")]
    public float tpAirborneExtraDistance = 2f;

    [Tooltip("Extra height added while airborne.")]
    public float tpAirborneExtraHeight = 1f;

    [Tooltip("Smoothing time for the camera offset (relative to the craft, so it adds no speed lag).")]
    public float tpOffsetSmoothTime = 0.12f;

    [Header("Third Person — Aim")]
    [Tooltip("Look target height above the craft center (along reference up).")]
    public float tpLookHeight = 1.2f;

    [Tooltip("Look-ahead multiplier while airborne. Lower keeps focus on the craft/landing.")]
    public float tpAirborneLookAheadMultiplier = 0.6f;

    [Tooltip("Sideways look offset per m/s of lateral slip. Frames drifts without unsettling normal driving.")]
    public float tpDriftLookInfluence = 0.05f;

    [Tooltip("Maximum sideways look offset from drift (meters).")]
    public float tpMaxDriftLookOffset = 3f;

    [Tooltip("Rotation smoothing response. Higher = tighter.")]
    public float tpRotationResponse = 14f;

    [Header("Surface Look-Ahead (loops / corkscrews)")]
    [Tooltip("How many seconds of track curvature the view leads by. Riding a loop pitches the view up into it; a corkscrew rolls the view ahead. 0 = off.")]
    public float surfaceLookAheadTime = 0.4f;

    [Tooltip("Maximum look-ahead angle (degrees).")]
    public float maxSurfaceLookAhead = 40f;

    [Tooltip("Smoothing response for the measured track curvature. Lower = calmer.")]
    public float surfaceLookAheadSmoothing = 5f;

    [Tooltip("How much of the surface look-ahead applies in first person.")]
    [Range(0f, 1f)] public float fpSurfaceLookAheadAmount = 0.6f;

    // ══════════════════════════════════════════════════════════════
    //  FIRST PERSON
    // ══════════════════════════════════════════════════════════════

    [Header("First Person")]
    [Tooltip("Rotation smoothing response. High = rigidly attached; lower adds head-lag.")]
    public float fpRotationResponse = 22f;

    [Tooltip("How much the cockpit horizon is softened toward the reference up while grounded. Damps carve-lean roll.")]
    [Range(0f, 1f)] public float fpGroundedHorizonSoftening = 0.2f;

    [Tooltip("Horizon softening while airborne. Keep small — in first person you should rotate with the craft.")]
    [Range(0f, 1f)] public float fpAirborneHorizonSoftening = 0.05f;

    [Tooltip("How much the view leads into turns, in degrees of yaw per (deg/sec) of craft yaw rate.")]
    public float fpYawLeadFactor = 0.05f;

    [Tooltip("Maximum yaw-lead angle (degrees).")]
    public float fpMaxYawLead = 7f;

    [Tooltip("How much the view pulls toward the velocity direction when sliding (helps read drifts). 0 = look straight along the nose.")]
    [Range(0f, 1f)] public float fpVelocityLookInfluence = 0.15f;

    // ══════════════════════════════════════════════════════════════
    //  FOV EXTRAS
    // ══════════════════════════════════════════════════════════════

    [Header("FOV Extras")]
    [Tooltip("Dolly-zoom compensation: pulls the third-person camera in as FOV rises so the craft keeps the same on-screen size. FOV then reads purely as speed — the world stretches — instead of shrinking the craft. 1 = exact size hold.")]
    [Range(0f, 1f)] public float tpFovZoomCompensation = 1f;

    [Tooltip("Extra FOV added by boost / grip breaker.")]
    public float boostExtraFOV = 10f;

    [Header("Boost Kick")]
    [Tooltip("Metres the third-person camera eases back behind the craft at full boost (acceleration setback). Smoothed, so keep it modest.")]
    public float boostThirdPersonSetback = 1.5f;

    [Tooltip("Metres the first-person view recoils backward at full boost. Kept restrained so the cockpit never leaves the craft.")]
    public float boostFirstPersonRecoil = 0.09f;

    [Tooltip("How quickly boost presentation attacks after the gameplay signal begins.")]
    public float boostVisualAttackResponse = 24f;

    [Tooltip("How quickly boost presentation relaxes after the burst ends.")]
    public float boostVisualReleaseResponse = 6f;

    [Tooltip("Length of the short visual onset pulse. This affects only camera presentation, never craft forces.")]
    [Range(0.05f, 0.5f)] public float boostOnsetPulseDuration = 0.22f;

    [Tooltip("Strength of the immediate boost onset pulse relative to the sustained boost envelope.")]
    [Range(0f, 1f)] public float boostOnsetPulseStrength = 0.72f;

    [Tooltip("Extra presentation scale for held Overcharge. Preserves existing camera tuning while giving discharge a stronger sustained read.")]
    [FormerlySerializedAs("nitroPresentationScale")]
    [Range(1f, 1.75f)] public float overchargePresentationScale = 1.35f;

    [Tooltip("If true, Grip Breaker from CraftCore / TractionCore adds FOV.")]
    public bool useGripBreakerAsBoostFOV = true;

    [Tooltip("Exponential FOV smoothing speed. Higher = faster response.")]
    public float fovSmoothSpeed = 8f;

    [Tooltip("FOV response while widening. Higher than the release response makes acceleration read immediately without a hard snap.")]
    public float fovAttackSpeed = 18f;

    // ══════════════════════════════════════════════════════════════
    //  COLLISION AVOIDANCE (third person)
    // ══════════════════════════════════════════════════════════════

    [Header("Collision Avoidance")]
    public bool useCollisionAvoidance = true;

    [Tooltip("Radius of the camera sphere cast for collision detection.")]
    public float collisionRadius = 0.35f;

    [Tooltip("Layers the camera should not clip through.")]
    public LayerMask collisionLayers = ~0;

    [Tooltip("Minimum distance from the pivot when blocked (never pushed past the obstacle).")]
    public float minDistance = 2f;

    [Tooltip("How quickly the camera pulls in when blocked (exp response). High enough to avoid clipping walls, but not an instant snap.")]
    public float collisionPullInSpeed = 14f;

    [Tooltip("How quickly the camera extends back out after an obstacle clears (fraction/sec).")]
    public float collisionRecoverySpeed = 2.5f;

    // ══════════════════════════════════════════════════════════════
    //  CLIP PLANES / CURSOR / TIMING
    // ══════════════════════════════════════════════════════════════

    [Header("Clip Planes")]
    public float tpNearClip = 0.3f;
    public float fpNearClip = 0.06f;

    [Header("Cursor")]
    public bool lockCursorOnStart = true;
    public bool unlockCursorOnDisable = true;

    [Header("Timing")]
    [Tooltip("Caps camera deltaTime so frame spikes do not cause huge smoothing jumps.")]
    public float maxCameraDeltaTime = 0.033f;

    // ══════════════════════════════════════════════════════════════
    //  INTERNAL STATE
    // ══════════════════════════════════════════════════════════════

    private Camera _cam;
    private int _profileIndex;

    // Reference frame
    private Vector3 _referenceUp = Vector3.up;
    private Vector3 _followDirection = Vector3.forward;
    private float _airBlend;              // 0 = grounded, 1 = airborne
    private bool _isGrounded;

    // Surface curvature (loops / corkscrews)
    private Vector3 _prevReferenceUp = Vector3.up;
    private Vector3 _frameAngularVelocity;   // deg/sec, axis * magnitude
    private float _dynamicEngagement;        // 0 = regular road, 1 = inside loop/corkscrew (smoothed)
    private float _engagementRaw;            // unsmoothed engagement, this frame

    // Third person
    private Vector3 _smoothedOffset;      // camera offset relative to craft position
    private Vector3 _offsetVelocity;      // SmoothDamp scratch
    private float _collisionFraction = 1f;
    private readonly RaycastHit[] _collisionHits = new RaycastHit[8];

    // FOV / boost
    private float _boostAmount;
    private float _boostVisual;
    private float _boostOnsetPulse;
    private bool _boostSignalWasActive;

    private bool _initialized;

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    void Awake()
    {
        _cam = GetComponent<Camera>();
        TryAutoAssignReferences();
    }

    void Start()
    {
        TryAutoAssignReferences();

        if (profiles == null || profiles.Count == 0)
            profiles = CreateDefaultProfiles();

        _profileIndex = Mathf.Clamp(startProfileIndex, 0, profiles.Count - 1);

        if (lockCursorOnStart)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (target != null)
            ResetCameraImmediate();
    }

    void OnDisable()
    {
        if (unlockCursorOnDisable)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        TryAutoAssignReferences();

        if (!_initialized)
            ResetCameraImmediate();

        float dt = Mathf.Min(Time.deltaTime, maxCameraDeltaTime);

        HandleInputKeys();
        UpdateBoostFeedback(dt);

        UpdateGroundedState(dt);
        UpdateReferenceUp(dt);

        float speed = GetSpeed();
        float speedT = GetSpeedT(speed);

        UpdateFollowDirection(speedT, dt);

        if (currentPerspective == CameraPerspective.ThirdPerson)
            UpdateThirdPerson(speedT, dt);
        else
            UpdateFirstPerson(speedT, dt);

        UpdateFOV(speedT, dt);
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════

    /// <summary>The camera profile currently in use.</summary>
    public CameraProfile ActiveProfile
    {
        get
        {
            if (profiles == null || profiles.Count == 0)
                profiles = CreateDefaultProfiles();

            _profileIndex = Mathf.Clamp(_profileIndex, 0, profiles.Count - 1);
            return profiles[_profileIndex];
        }
    }

    /// <summary>Perspective of the active profile.</summary>
    public CameraPerspective currentPerspective => ActiveProfile.perspective;

    /// <summary>
    /// Replaces the profile list with the built-in defaults. Use from the component
    /// context menu (⋮) when a scene's serialized list predates newly added profiles.
    /// </summary>
    [ContextMenu("Reset Profiles To Defaults")]
    public void ResetProfilesToDefaults()
    {
        profiles = CreateDefaultProfiles();
        _profileIndex = Mathf.Clamp(_profileIndex, 0, profiles.Count - 1);
    }

    /// <summary>Advance to the next camera profile (wraps around).</summary>
    public void CycleProfile()
    {
        if (profiles == null || profiles.Count == 0) return;

        CameraPerspective previousPerspective = ActiveProfile.perspective;
        _profileIndex = (_profileIndex + 1) % profiles.Count;

        Debug.Log($"[HovercraftCamera] View: {ActiveProfile.profileName}");

        // Snap only when the rig type changes. Third-person profile switches
        // glide via the offset smoothing, which reads much nicer.
        if (ActiveProfile.perspective != previousPerspective)
            ResetCameraImmediate();
    }

    /// <summary>External grounded override for setups without a CraftCore.</summary>
    public void SetGrounded(bool grounded)
    {
        _isGrounded = grounded;
    }

    /// <summary>Drives the boost presentation signal (0..1). Grip breaker remains independent.</summary>
    public void SetBoostAmount(float amount)
    {
        float next = Mathf.Clamp01(amount);
        bool active = next > 0.01f;
        if (active && !_boostSignalWasActive)
            _boostOnsetPulse = 1f;

        _boostSignalWasActive = active;
        _boostAmount = next;
    }

    /// <summary>Smoothed camera-only boost response for UI/effects that want the same timing.</summary>
    public float BoostVisual01 => Mathf.Clamp01(Mathf.Max(
        _boostVisual,
        _boostOnsetPulse * Mathf.Clamp01(boostOnsetPulseStrength * overchargePresentationScale)));

    /// <summary>
    /// Snaps the camera to its ideal pose for the current perspective and clears
    /// all smoothing state. Call after teleporting or retargeting the craft.
    /// </summary>
    public void ResetCameraImmediate()
    {
        if (target == null) return;

        TryAutoAssignReferences();

        // Snap grounded state / air blend.
        if (HasTelemetry())
        {
            var t = craftCore.telemetry.CurrentTelemetry;
            _isGrounded = t.isGrounded;
            _airBlend = 1f - t.groundedFactor;
        }
        else
        {
            UpdateFallbackGroundedRaycast();
            _airBlend = _isGrounded ? 0f : 1f;
        }

        // Snap reference frame.
        Vector3 surfaceUpNow = GetSurfaceUp();
        _engagementRaw = ComputeEngagement(surfaceUpNow);
        _dynamicEngagement = (1f - _airBlend) * _engagementRaw;
        _referenceUp = GetTargetReferenceUp(surfaceUpNow);
        _prevReferenceUp = _referenceUp;
        _frameAngularVelocity = Vector3.zero;
        _followDirection = ComputeDesiredFollowDirection(GetSpeedT(GetSpeed()));

        _smoothedOffset = Vector3.zero;
        _offsetVelocity = Vector3.zero;
        _collisionFraction = 1f;
        _boostVisual = _boostAmount;
        _boostOnsetPulse = 0f;

        if (currentPerspective == CameraPerspective.ThirdPerson)
        {
            Vector3 desiredOffset = ComputeThirdPersonOffset(GetSpeedT(GetSpeed()));
            _smoothedOffset = desiredOffset;

            transform.position = target.position + desiredOffset;
            transform.rotation = ComputeThirdPersonRotation(GetSpeedT(GetSpeed()), transform.position);

            if (_cam != null)
            {
                _cam.fieldOfView = ActiveProfile.fovBase;
                _cam.nearClipPlane = tpNearClip;
            }
        }
        else
        {
            transform.position = target.TransformPoint(ActiveProfile.mountOffset);
            transform.rotation = ComputeFirstPersonRotation();

            if (_cam != null)
            {
                _cam.fieldOfView = ActiveProfile.fovBase;
                _cam.nearClipPlane = fpNearClip;
            }
        }

        _initialized = true;
    }

    // ══════════════════════════════════════════════════════════════
    //  SETUP / TOGGLING
    // ══════════════════════════════════════════════════════════════

    private void TryAutoAssignReferences()
    {
        if (target == null) return;

        if (targetRigidbody == null)
            targetRigidbody = target.GetComponent<Rigidbody>();

        if (craftCore == null)
            craftCore = target.GetComponent<CraftCore>();

        if (useTrackSectionData)
        {
            if (trackSensor == null)
            {
                trackSensor = GetComponent<TrackSectionSensor>();
                if (trackSensor == null)
                    trackSensor = gameObject.AddComponent<TrackSectionSensor>();
            }

            if (trackSensor.target != target)
                trackSensor.target = target;
        }
    }

    private bool HasTelemetry()
    {
        return craftCore != null && craftCore.telemetry != null;
    }

    private void HandleInputKeys()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current[profileCycleKey].wasPressedThisFrame)
            CycleProfile();

        if (Keyboard.current[groundOrientationToggleKey].wasPressedThisFrame)
        {
            followSurfaceOrientation = !followSurfaceOrientation;
            Debug.Log($"[HovercraftCamera] Ground orientation: {(followSurfaceOrientation ? "ON" : "OFF")}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  GROUNDED / REFERENCE FRAME
    // ══════════════════════════════════════════════════════════════

    private void UpdateGroundedState(float dt)
    {
        if (HasTelemetry())
        {
            var t = craftCore.telemetry.CurrentTelemetry;
            _isGrounded = t.isGrounded;
            // Telemetry: 1 = grounded. Camera: 1 = airborne.
            _airBlend = 1f - t.groundedFactor;
            return;
        }

        if (!useExternalGroundedState)
            UpdateFallbackGroundedRaycast();

        float targetBlend = _isGrounded ? 0f : 1f;
        _airBlend = Mathf.MoveTowards(_airBlend, targetBlend, dt * fallbackGroundedBlendSpeed);
    }

    private void UpdateFallbackGroundedRaycast()
    {
        Vector3 origin = target.position + _referenceUp * 0.1f;

        _isGrounded = Physics.Raycast(
            origin,
            -_referenceUp,
            groundCheckDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    /// <summary>Raw track surface normal under the craft (world up when unknown).</summary>
    private Vector3 GetSurfaceUp()
    {
        if (HasTelemetry())
        {
            // groundNormal already switches to the roof-contact normal when inverted.
            Vector3 n = craftCore.telemetry.CurrentTelemetry.groundNormal;
            if (n.sqrMagnitude > 0.001f)
                return n.normalized;

            return Vector3.up;
        }

        // Fallback: approximate the surface normal with craft up.
        return _isGrounded ? target.up : Vector3.up;
    }

    /// <summary>
    /// The up direction the reference frame is currently steering toward:
    /// track surface normal while grounded, world up while airborne.
    /// </summary>
    private Vector3 GetTargetReferenceUp(Vector3 surfaceUp)
    {
        if (!followSurfaceOrientation)
            return Vector3.up;

        GroundOrientationMode mode = ActiveProfile.groundOrientation;

        if (mode == GroundOrientationMode.Off)
            return Vector3.up;

        float influence = surfaceUpInfluence;

        if (mode == GroundOrientationMode.Dynamic)
            influence *= _engagementRaw;

        float groundedWeight = (1f - _airBlend) * influence;
        Vector3 up = BlendTowardSurfaceUp(surfaceUp, groundedWeight);

        if (up.sqrMagnitude < 0.001f)
            return Vector3.up;

        return ClampTiltOutsideFeatures(ReduceSurfaceBanking(up.normalized));
    }

    /// <summary>
    /// Caps how far the camera frame may tilt away from world up when the craft is NOT
    /// in a loop/corkscrew.
    ///
    /// A wallride puts the surface normal 75–90° over without being a stunt, and
    /// <see cref="ReduceSurfaceBanking"/> deliberately hands those cases straight
    /// through ("treat as wall ride, not banking — follow fully"). That is what rolls
    /// the horizon on top of a wall. Decomposing the bank there is not an option — with
    /// the surface up near horizontal the in-plane component collapses and the frame
    /// becomes noise-sensitive — so the tilt is clamped back toward world up instead,
    /// which is stable at any surface angle.
    ///
    /// The cap lifts with engagement, so genuine orientation features still follow the
    /// surface all the way round. Engagement comes from the track sensor, whose feature
    /// list covers rotational events, loops, corkscrews and half-loops but deliberately
    /// NOT WallrideTurn — so this reads "wall, not stunt" straight off the track data.
    /// </summary>
    private Vector3 ClampTiltOutsideFeatures(Vector3 up)
    {
        if (!limitTiltOutsideFeatures)
            return up;

        // Use the smoothed engagement so the cap opens and closes with the framing
        // blend rather than snapping, but let the raw signal open it promptly.
        float engaged = Mathf.Clamp01(Mathf.Max(_engagementRaw, _dynamicEngagement));
        if (engaged >= 0.999f)
            return up;

        float maxTilt = Mathf.Lerp(maxUnengagedTiltAngle, 180f, engaged);
        float tilt = Vector3.Angle(Vector3.up, up);
        if (tilt <= maxTilt)
            return up;

        Vector3 limited = Vector3.RotateTowards(Vector3.up, up, maxTilt * Mathf.Deg2Rad, 0f);
        return limited.sqrMagnitude > 0.001f ? limited.normalized : up;
    }

    /// <summary>
    /// Blend from world up toward the surface up by <paramref name="weight"/>.
    /// Near-inverted surfaces (loop tops/exits) make a direct slerp unstable: with
    /// almost-opposite endpoints and a partial weight (a brief suspension unload),
    /// the arc passes through an arbitrary SIDEWAYS direction. There we rotate from
    /// the surface up back toward world up around a stable travel-plane axis instead.
    /// </summary>
    private Vector3 BlendTowardSurfaceUp(Vector3 surfaceUp, float weight)
    {
        float dot = Vector3.Dot(Vector3.up, surfaceUp);

        if (dot > -0.7f)
            return Vector3.Slerp(Vector3.up, surfaceUp, weight);

        Vector3 axis = Vector3.Cross(surfaceUp, Vector3.up);

        // Almost exactly inverted: the cross product is noise — use the axis of the
        // vertical travel plane (for a loop, that IS the loop's rotation axis).
        if (axis.sqrMagnitude < 0.1f)
            axis = Vector3.Cross(surfaceUp, _followDirection);

        if (axis.sqrMagnitude < 0.0001f)
            return surfaceUp;

        axis.Normalize();

        float angle = Vector3.Angle(surfaceUp, Vector3.up);
        return Quaternion.AngleAxis(angle * (1f - weight), axis) * surfaceUp;
    }

    /// <summary>
    /// Loop/corkscrew engagement for this frame.
    /// When the craft is tracked on a generated track, section metadata is the SOLE
    /// authority: only Loop/Corkscrew sections (plus their blend windows) engage.
    /// Everywhere else the camera behaves exactly like ground orientation is off —
    /// no ramp, hill, bank, or wall can tilt it. Realtime detection is used only
    /// as a fallback when no section data exists (test scenes, off-track).
    /// </summary>
    private float ComputeEngagement(Vector3 surfaceUp)
    {
        if (useTrackSectionData && trackSensor != null
            && trackSensor.HasTrack && trackSensor.IsTracking)
        {
            // Anticipation is a TIME budget, not a distance. The camera needs a
            // roughly constant number of seconds to reframe regardless of how fast
            // the craft is travelling, so convert the lead from seconds at the
            // current speed and keep the metre values as low-speed floors.
            float speed = Mathf.Max(1f, GetSpeed());
            _engagementFromSensor = true;

            return trackSensor.GetOrientationWeight(
                Mathf.Max(sectionBlendInDistance, sectionBlendInSeconds * speed),
                Mathf.Max(sectionBlendOutDistance, sectionBlendOutSeconds * speed)
            );
        }

        // A generated track exists but the craft is momentarily off the driving line
        // (high on a wall, airborne over a jump). The authoritative answer is still
        // "no orientation feature here" — falling through to realtime detection would
        // let a plain half-pipe wall masquerade as a stunt, because surface tilt alone
        // cannot tell a wall from a corkscrew. That is exactly what the section data
        // exists to disambiguate, so trust it and stay disengaged.
        if (useTrackSectionData && trackSensor != null && trackSensor.HasTrack)
        {
            _engagementFromSensor = true;
            return 0f;
        }

        // No track data at all (hand-built scene, or before generation): realtime
        // detection is the only signal available.
        _engagementFromSensor = false;
        return GetDynamicOrientationWeight(surfaceUp);
    }

    /// <summary>
    /// True while engagement comes from the track sensor (authoritative section data)
    /// rather than realtime orientation detection. The two want different smoothing:
    /// the sensor already knows exactly where a feature begins and ends and hands over
    /// a pre-eased, speed-correct ramp, so extra release damping only adds lag. The
    /// realtime signal is inferred from a noisy surface normal and needs the damping
    /// to stop it chattering around its thresholds.
    /// </summary>
    private bool _engagementFromSensor;

    /// <summary>
    /// Dynamic ground-orientation engagement (0 = stay world-up, 1 = follow surface).
    /// Built from three signals a banked corner cannot produce:
    /// LOOPS — the flight path climbing/diving steeply (banked corners have ~zero
    /// climb no matter how hard the craft slips, so they can't contaminate this);
    /// CORKSCREWS — the surface rolling hard around the travel direction while NOT
    /// yawing (turning + banked = a corner, and is suppressed);
    /// OVERHEAD — anything tilted past horizontal is unambiguous.
    /// </summary>
    private float GetDynamicOrientationWeight(Vector3 surfaceUp)
    {
        float overallTilt = Vector3.Angle(Vector3.up, surfaceUp);

        // Past-horizontal (loop tops, inverted corkscrew middle) is unambiguous —
        // and keeps engagement locked where the bank decomposition folds back.
        float overheadWeight = Mathf.InverseLerp(85f, 100f, overallTilt);

        // LOOPS: climb angle of the actual flight path. Vertical loop sides read
        // as 90° climb, so near-vertical travel stays engaged through this signal.
        float climbTilt = 0f;

        if (targetRigidbody != null)
        {
            Vector3 velocity = targetRigidbody.linearVelocity;
            float speed = velocity.magnitude;

            if (speed > minFlatSpeedForDirection)
                climbTilt = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(velocity.y) / speed)) * Mathf.Rad2Deg;
        }

        float pitchWeight = Mathf.InverseLerp(dynamicPitchStartAngle, dynamicPitchFullAngle, climbTilt);

        // CORKSCREWS: hard roll around the travel direction, suppressed while yawing.
        float bankWeight = 0f;
        Vector3 bankAxis = Vector3.Cross(_followDirection, Vector3.up);

        if (bankAxis.sqrMagnitude > 0.01f)
        {
            bankAxis.Normalize();

            float bankComponent = Mathf.Abs(Vector3.Dot(surfaceUp, bankAxis));
            float bankTilt = Mathf.Asin(Mathf.Clamp01(bankComponent)) * Mathf.Rad2Deg;

            bankWeight = Mathf.InverseLerp(dynamicBankStartAngle, dynamicBankFullAngle, bankTilt);

            float turning = Mathf.InverseLerp(
                dynamicYawSuppressStart,
                dynamicYawSuppressFull,
                Mathf.Abs(GetYawRate())
            );

            bankWeight *= 1f - turning;
        }

        // Soft union rather than a hard Max. Max lets whichever signal spikes first own
        // the result outright, so handing over between them (pitch fading as bank rises
        // through a corkscrew) shows up as a visible step. This blends: any signal
        // reaching 1 still forces full engagement, but partial evidence from several
        // signals at once accumulates smoothly instead of one of them winning.
        float notEngaged = (1f - pitchWeight) * (1f - bankWeight) * (1f - overheadWeight);
        return Mathf.Clamp01(1f - notEngaged);
    }

    /// <summary>
    /// Splits the surface orientation into pitch (rotation in the vertical plane of
    /// travel — loops, crests, drops) and banking (roll around the travel direction),
    /// then scales/caps only the banking. This is what keeps high-speed banked turns
    /// from tilting the horizon violently while loops still follow fully.
    /// </summary>
    private Vector3 ReduceSurfaceBanking(Vector3 up)
    {
        // Past horizontal (upper half of a loop, deep corkscrew) the bank
        // decomposition folds back on itself and can flip the frame — follow fully.
        if (Vector3.Dot(up, Vector3.up) < 0.05f)
            return up;

        if (surfaceBankInfluence >= 0.999f && maxSurfaceBankAngle >= 179f)
            return up;

        Vector3 travel = _followDirection;

        // Normal of the vertical plane containing the travel direction.
        Vector3 bankAxis = Vector3.Cross(travel, Vector3.up);

        // Near-vertical travel (loop sides, wall climbs): banking is ill-defined and
        // the decomposition becomes noise-sensitive — small lateral wobbles swing the
        // bank axis wildly and read as side-to-side shake. Fade the reduction out
        // GRADUALLY as travel steepens instead of hard-switching.
        float verticality = bankAxis.sqrMagnitude; // sin² of angle from vertical travel
        if (verticality < 0.02f)
            return up;

        float reductionWeight = Mathf.Clamp01(Mathf.InverseLerp(0.02f, 0.15f, verticality));

        // While engaged in a loop/corkscrew the camera follows the surface fully —
        // bank reduction there only injects noise (steep travel makes the bank axis
        // unstable, felt as sideways tilt on loop descents).
        reductionWeight *= 1f - _engagementRaw;

        if (reductionWeight < 0.001f)
            return up;

        bankAxis.Normalize();

        float bankComponent = Vector3.Dot(up, bankAxis);
        Vector3 inPlaneUp = up - bankAxis * bankComponent;

        // Surface tilted a full 90° sideways (riding a vertical wall): treat as
        // wall ride, not banking — follow fully.
        if (inPlaneUp.sqrMagnitude < 0.01f)
            return up;

        inPlaneUp.Normalize();

        float bankAngle = Mathf.Asin(Mathf.Clamp(bankComponent, -1f, 1f)) * Mathf.Rad2Deg;
        bankAngle *= surfaceBankInfluence;
        bankAngle = Mathf.Clamp(bankAngle, -maxSurfaceBankAngle, maxSurfaceBankAngle);

        Vector3 reducedUp = Quaternion.AngleAxis(bankAngle, travel) * inPlaneUp;

        return Vector3.Slerp(up, reducedUp, reductionWeight).normalized;
    }

    private void UpdateReferenceUp(float dt)
    {
        Vector3 surfaceUp = GetSurfaceUp();

        // Loop/corkscrew engagement (0 = regular road, 1 = engaged). Drives dynamic
        // framing, drift-framing fade, and Dynamic-mode orientation following.
        _engagementRaw = ComputeEngagement(surfaceUp);

        float engagementTarget = (1f - _airBlend) * _engagementRaw;

        // Asymmetric: engage briskly so the camera arrives WITH the feature, release
        // slowly so a momentary dip below the threshold cannot pop the framing out and
        // straight back in. A single symmetric rate has to choose between arriving late
        // and chattering; splitting the two removes the compromise.
        //
        // Only the realtime signal needs that guard. When the sensor is driving, the
        // ramp it produces is already eased and already scaled to speed, so damping
        // the release again would just leave the camera in loop framing well down the
        // following straight.
        bool releasing = engagementTarget <= _dynamicEngagement;
        float engagementResponse = releasing && !_engagementFromSensor
            ? dynamicFramingReleaseResponse
            : dynamicFramingResponse;

        _dynamicEngagement = Mathf.Lerp(
            _dynamicEngagement,
            engagementTarget,
            ExpSmoothing(engagementResponse, dt)
        );

        Vector3 targetUp = GetTargetReferenceUp(surfaceUp);

        float response = Mathf.Lerp(groundedUpResponse, airborneUpResponse, _airBlend);
        float lerp = ExpSmoothing(response, dt);

        Vector3 desired = Vector3.Slerp(_referenceUp, targetUp, lerp);

        // Rate cap: protects against surface-normal pops on mesh seams.
        float maxRadians = maxUpDegreesPerSecond * Mathf.Deg2Rad * dt;
        _referenceUp = Vector3.RotateTowards(_referenceUp, desired, maxRadians, 0f).normalized;

        UpdateFrameAngularVelocity(dt);
    }

    /// <summary>
    /// Measures how fast the reference frame is rotating (deg/sec). Riding a loop
    /// rotates the up around the right axis; a corkscrew rotates it around forward.
    /// This drives the surface look-ahead.
    /// </summary>
    private void UpdateFrameAngularVelocity(float dt)
    {
        Vector3 instantAngularVelocity = Vector3.zero;

        if (dt > 0.0001f)
        {
            Quaternion delta = Quaternion.FromToRotation(_prevReferenceUp, _referenceUp);
            delta.ToAngleAxis(out float angle, out Vector3 axis);

            if (angle > 0.001f && !float.IsNaN(axis.x))
                instantAngularVelocity = axis.normalized * (angle / dt);
        }

        _prevReferenceUp = _referenceUp;

        float lerp = ExpSmoothing(surfaceLookAheadSmoothing, dt);
        _frameAngularVelocity = Vector3.Lerp(_frameAngularVelocity, instantAngularVelocity, lerp);
    }

    /// <summary>
    /// Rotation that leads the view along the track curvature ("raising your head"
    /// into a loop, rolling ahead into a corkscrew). Identity when airborne or
    /// driving on flat/steady surfaces.
    /// </summary>
    private Quaternion GetSurfaceLeadRotation()
    {
        if (surfaceLookAheadTime <= 0f)
            return Quaternion.identity;

        // Strip the yaw component (rotation around the reference up). Loops and
        // corkscrews rotate the frame around right/forward axes only — any yaw here
        // is travel-plane noise and reads as side-to-side steering shake.
        Vector3 angularVelocity = _frameAngularVelocity
            - _referenceUp * Vector3.Dot(_frameAngularVelocity, _referenceUp);

        float magnitude = angularVelocity.magnitude;
        if (magnitude < 1f)
            return Quaternion.identity;

        float leadAngle = Mathf.Min(magnitude * surfaceLookAheadTime, maxSurfaceLookAhead);
        leadAngle *= 1f - _airBlend;

        if (leadAngle < 0.01f)
            return Quaternion.identity;

        return Quaternion.AngleAxis(leadAngle, angularVelocity / magnitude);
    }

    // ══════════════════════════════════════════════════════════════
    //  FOLLOW DIRECTION
    // ══════════════════════════════════════════════════════════════

    /// <summary>Craft forward projected onto the reference-up plane.</summary>
    private Vector3 GetFrameForward()
    {
        Vector3 flat = Vector3.ProjectOnPlane(target.forward, _referenceUp);

        if (flat.sqrMagnitude < 0.0001f)
        {
            // Craft is pointing straight along the reference up (vertical wall exit,
            // nose-down drop). Keep the previous direction rather than guessing.
            flat = Vector3.ProjectOnPlane(_followDirection, _referenceUp);

            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.ProjectOnPlane(Vector3.forward, _referenceUp);
        }

        return flat.normalized;
    }

    private Vector3 ComputeDesiredFollowDirection(float speedT)
    {
        Vector3 frameForward = GetFrameForward();

        if (targetRigidbody == null)
            return frameForward;

        // Project velocity onto the reference plane: hover bounce never steers the
        // camera, and on loops/walls the direction keeps tracking along the surface.
        Vector3 flatVelocity = Vector3.ProjectOnPlane(targetRigidbody.linearVelocity, _referenceUp);
        float flatSpeed = flatVelocity.magnitude;

        if (flatSpeed < minFlatSpeedForDirection)
            return frameForward;

        Vector3 velocityDirection = flatVelocity / flatSpeed;

        // Mostly reversing: keep the camera behind the craft's nose instead of
        // swinging around to face it.
        if (Vector3.Dot(velocityDirection, frameForward) < -0.2f)
            return frameForward;

        float forwardInfluence = Mathf.Lerp(
            Mathf.Lerp(lowSpeedForwardInfluence, highSpeedForwardInfluence, speedT),
            airborneForwardInfluence,
            _airBlend
        );

        // Per-profile: 0 = camera follows pure velocity, so deliberate drift angles
        // never swing the view off the road.
        forwardInfluence *= ActiveProfile.noseInfluence;

        // Grip breaker = deliberate sliding. Follow where the craft is GOING, not
        // where its nose points, in every profile.
        if (craftCore != null && craftCore.traction != null)
            forwardInfluence *= 1f - craftCore.traction.GripBreakerAmount;

        return Vector3.Slerp(velocityDirection, frameForward, forwardInfluence).normalized;
    }

    private void UpdateFollowDirection(float speedT, float dt)
    {
        Vector3 desired = ComputeDesiredFollowDirection(speedT);

        float response = Mathf.Lerp(groundedDirectionResponse, airborneDirectionResponse, _airBlend);
        float lerp = ExpSmoothing(response, dt);

        Vector3 next = Vector3.Slerp(_followDirection, desired, lerp);

        if (next.sqrMagnitude < 0.001f)
            next = desired;

        _followDirection = next.normalized;
    }

    // ══════════════════════════════════════════════════════════════
    //  THIRD PERSON
    // ══════════════════════════════════════════════════════════════

    private void UpdateThirdPerson(float speedT, float dt)
    {
        // Desired offset relative to the craft. Smoothing this offset (instead of
        // the world position) means steady travel at any speed has zero lag —
        // only changes of direction/distance are eased.
        Vector3 desiredOffset = ComputeThirdPersonOffset(speedT);

        // Boost setback: fold the fall-back into the offset BEFORE smoothing so the
        // camera eases back and returns instead of snapping (a hard add here read as a
        // teleport). The envelope already ramps _boostAmount, so this is doubly smooth.
        float boostKick = Mathf.Clamp01(_boostVisual);
        if (boostKick > 0f && boostThirdPersonSetback > 0f)
            desiredOffset -= target.forward * (boostThirdPersonSetback * overchargePresentationScale * boostKick);

        _smoothedOffset = Vector3.SmoothDamp(
            _smoothedOffset,
            desiredOffset,
            ref _offsetVelocity,
            Mathf.Max(0.001f, tpOffsetSmoothTime),
            Mathf.Infinity,
            dt
        );

        Vector3 anchor = target.position;
        Vector3 desiredPosition = anchor + _smoothedOffset;

        if (useCollisionAvoidance)
            desiredPosition = ApplyCollisionAvoidance(anchor, desiredPosition, dt);
        else
            _collisionFraction = 1f;

        transform.position = desiredPosition;

        Quaternion desiredRotation = ComputeThirdPersonRotation(speedT, desiredPosition);
        float rotationLerp = ExpSmoothing(tpRotationResponse, dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationLerp);

        if (_cam != null)
            _cam.nearClipPlane = tpNearClip;
    }

    private Vector3 ComputeThirdPersonOffset(float speedT)
    {
        CameraProfile profile = ActiveProfile;

        float distance = Mathf.Lerp(profile.distanceBase, profile.distanceMax, speedT)
                         + tpAirborneExtraDistance * _airBlend;

        float height = Mathf.Lerp(profile.heightBase, profile.heightMax, speedT)
                       + tpAirborneExtraHeight * _airBlend;

        // Dynamic framing: pull in close and low while engaged in a loop/corkscrew.
        if (profile.dynamicFraming)
        {
            distance = Mathf.Lerp(distance, profile.engagedDistance, _dynamicEngagement);
            height = Mathf.Lerp(height, profile.engagedHeight, _dynamicEngagement);
        }

        Vector3 offset = -_followDirection * distance + _referenceUp * height;

        // Dolly-zoom: as FOV rises the whole offset shrinks so the craft keeps its
        // on-screen size and position — wide FOV reads as the world stretching.
        if (tpFovZoomCompensation > 0f && _cam != null)
        {
            float baseTan = Mathf.Tan(profile.fovBase * 0.5f * Mathf.Deg2Rad);
            float currentTan = Mathf.Tan(Mathf.Clamp(_cam.fieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);

            if (baseTan > 0.0001f && currentTan > 0.0001f)
                offset *= Mathf.Lerp(1f, baseTan / currentTan, tpFovZoomCompensation);
        }

        // Surface look-ahead swings the POSITION along the curvature too: the camera
        // trails the craft along the loop/corkscrew arc. Combined with the aim lead
        // below, the whole rig tilts around the craft as one unit, so the craft
        // keeps its screen position instead of sliding off the bottom.
        return GetSurfaceLeadRotation() * offset;
    }

    private Quaternion ComputeThirdPersonRotation(float speedT, Vector3 cameraPosition)
    {
        CameraProfile profile = ActiveProfile;

        float lookAhead = Mathf.Lerp(profile.lookAheadBase, profile.lookAheadMax, speedT);
        lookAhead *= Mathf.Lerp(1f, tpAirborneLookAheadMultiplier, _airBlend);

        // Dynamic framing: raise the aim point while engaged, tilting the view up
        // like lifting your head on a rollercoaster.
        float lookHeight = tpLookHeight;
        if (profile.dynamicFraming)
            lookHeight += profile.engagedLookUp * _dynamicEngagement;

        Vector3 aimOffset = _followDirection * lookAhead + _referenceUp * lookHeight;

        // Drift side-framing: shift the look target toward the slide so drifts read.
        // Faded out inside loops/corkscrews — side-slip noise there reads as shake.
        if (tpDriftLookInfluence > 0f && HasTelemetry())
        {
            float sideSpeed = craftCore.telemetry.CurrentTelemetry.sideSpeed;
            float sideOffset = Mathf.Clamp(
                sideSpeed * tpDriftLookInfluence,
                -tpMaxDriftLookOffset,
                tpMaxDriftLookOffset
            );

            sideOffset *= 1f - _dynamicEngagement;

            Vector3 frameRight = Vector3.Cross(_referenceUp, _followDirection);
            aimOffset += frameRight * sideOffset;
        }

        // Same lead rotation as the position offset: the aim point sweeps along the
        // loop/corkscrew with the camera, keeping the craft framed.
        Quaternion lead = GetSurfaceLeadRotation();
        Vector3 lookTarget = target.position + lead * aimOffset;
        Vector3 lookDirection = lookTarget - cameraPosition;

        // Camera up: the reference frame (bank-reduced at the source), swept by the lead.
        Vector3 cameraUp = lead * _referenceUp;

        return SafeLookRotation(lookDirection, cameraUp, transform.rotation);
    }

    private Vector3 ApplyCollisionAvoidance(Vector3 anchor, Vector3 desiredPosition, float dt)
    {
        Vector3 pivot = anchor + _referenceUp * tpLookHeight;
        Vector3 toCamera = desiredPosition - pivot;
        float fullDistance = toCamera.magnitude;

        if (fullDistance < 0.001f)
            return desiredPosition;

        Vector3 direction = toCamera / fullDistance;

        // Find the closest obstruction, ignoring the craft's own colliders —
        // a pitched craft (jumps, ramp exits) must never obstruct its own camera.
        float targetFraction = 1f;
        float hitDistance = fullDistance;

        int hitCount = Physics.SphereCastNonAlloc(
            pivot,
            collisionRadius,
            direction,
            _collisionHits,
            fullDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _collisionHits[i];

            if (hit.collider == null)
                continue;

            if (hit.transform == target || hit.transform.IsChildOf(target))
                continue;

            float distance = Mathf.Max(0f, hit.distance);

            if (distance < hitDistance)
            {
                hitDistance = distance;
                targetFraction = Mathf.Clamp01(distance / fullDistance);
            }
        }

        // Pull in fast but smoothed (a snap here reads as flicker on ramp exits);
        // extend back out slowly so clearing an obstacle never pops.
        if (targetFraction < _collisionFraction)
            _collisionFraction = Mathf.Lerp(_collisionFraction, targetFraction, ExpSmoothing(collisionPullInSpeed, dt));
        else
            _collisionFraction = Mathf.MoveTowards(_collisionFraction, targetFraction, collisionRecoverySpeed * dt);

        // Keep at least minDistance from the pivot, but never past the obstacle.
        float length = fullDistance * _collisionFraction;
        length = Mathf.Max(length, Mathf.Min(minDistance, hitDistance));

        return pivot + direction * length;
    }

    // ══════════════════════════════════════════════════════════════
    //  FIRST PERSON
    // ══════════════════════════════════════════════════════════════

    private void UpdateFirstPerson(float speedT, float dt)
    {
        // Position: hard-locked to the mount point. Any world-space smoothing at
        // 250+ m/s would put the "cockpit" camera meters outside the craft.
        // A restrained backward recoil at boost onset sells the acceleration without
        // ever letting the cockpit drift out of the craft.
        Vector3 mountOffset = ActiveProfile.mountOffset;
        float fpBoost = Mathf.Clamp01(_boostVisual);
        if (fpBoost > 0f && boostFirstPersonRecoil > 0f)
            mountOffset -= Vector3.forward * (boostFirstPersonRecoil * overchargePresentationScale * fpBoost);

        transform.position = target.TransformPoint(mountOffset);

        Quaternion desiredRotation = ComputeFirstPersonRotation();
        float rotationLerp = ExpSmoothing(fpRotationResponse, dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationLerp);

        if (_cam != null)
            _cam.nearClipPlane = fpNearClip;
    }

    private Quaternion ComputeFirstPersonRotation()
    {
        Vector3 forward = target.forward;
        Vector3 up = target.up;

        // Horizon softening: damp a fraction of the craft's lean toward the reference
        // up. Grounded this settles carve-lean roll; airborne it stays tiny so the
        // pilot genuinely rotates with flips.
        float softening = Mathf.Lerp(fpGroundedHorizonSoftening, fpAirborneHorizonSoftening, _airBlend);
        up = Vector3.Slerp(up, _referenceUp, softening);

        // Velocity look: pull slightly toward where the craft is actually going.
        if (fpVelocityLookInfluence > 0f && targetRigidbody != null)
        {
            Vector3 velocity = targetRigidbody.linearVelocity;
            float speed = velocity.magnitude;

            if (speed > minFlatSpeedForDirection)
            {
                Vector3 velocityDirection = velocity / speed;

                // Only when broadly moving forward — reversing must not flip the view.
                if (Vector3.Dot(velocityDirection, forward) > 0.2f)
                    forward = Vector3.Slerp(forward, velocityDirection, fpVelocityLookInfluence);
            }
        }

        // Yaw lead: look into the turn proportionally to yaw rate.
        float yawRate = GetYawRate();
        float yawLead = Mathf.Clamp(yawRate * fpYawLeadFactor, -fpMaxYawLead, fpMaxYawLead);
        forward = Quaternion.AngleAxis(yawLead, up) * forward;

        // Surface look-ahead: raise the head into loops, roll ahead into corkscrews.
        Quaternion lead = Quaternion.Slerp(
            Quaternion.identity,
            GetSurfaceLeadRotation(),
            fpSurfaceLookAheadAmount
        );
        forward = lead * forward;
        up = lead * up;

        return SafeLookRotation(forward, up, target.rotation);
    }

    private float GetYawRate()
    {
        if (HasTelemetry())
            return craftCore.telemetry.CurrentTelemetry.yawRate;

        if (targetRigidbody == null)
            return 0f;

        Vector3 localAngular = target.InverseTransformDirection(targetRigidbody.angularVelocity);
        return localAngular.y * Mathf.Rad2Deg;
    }

    // ══════════════════════════════════════════════════════════════
    //  SPEED / FOV
    // ══════════════════════════════════════════════════════════════

    private float GetSpeed()
    {
        if (HasTelemetry())
            return craftCore.telemetry.CurrentTelemetry.speed;

        if (targetRigidbody == null)
            return 0f;

        return targetRigidbody.linearVelocity.magnitude;
    }

    /// <summary>
    /// Normalised speed for every speed-driven camera effect.
    ///
    /// Measured across the band the craft actually races in, not from a standstill.
    /// Anchoring at zero wastes the effect range: the craft is barely ever slow, so a
    /// 0→max ramp spends most of its travel before the speed even feels notable, then
    /// has nothing left where it matters. Ramping from <see cref="speedForMinEffects"/>
    /// instead keeps the full range live over the speeds actually driven.
    /// </summary>
    private float GetSpeedT(float speed)
    {
        float lo = Mathf.Max(0f, speedForMinEffects);
        float hi = Mathf.Max(lo + 1f, speedForMaxEffects);
        float t = Mathf.Clamp01((speed - lo) / (hi - lo));
        return Mathf.Pow(t, speedResponseExponent);
    }

    private void UpdateFOV(float speedT, float dt)
    {
        float boost = BoostVisual01;

        if (useGripBreakerAsBoostFOV && craftCore != null && craftCore.traction != null)
            boost = Mathf.Max(boost, craftCore.traction.GripBreakerAmount);

        CameraProfile profile = ActiveProfile;
        float targetFov = Mathf.Lerp(profile.fovBase, profile.fovMax, speedT)
                        + boostExtraFOV * overchargePresentationScale * boost;

        float response = targetFov > _cam.fieldOfView ? fovAttackSpeed : fovSmoothSpeed;
        float fovLerp = ExpSmoothing(response, dt);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, fovLerp);
    }

    private void UpdateBoostFeedback(float dt)
    {
        float response = _boostAmount > _boostVisual
            ? boostVisualAttackResponse
            : boostVisualReleaseResponse;
        _boostVisual = Mathf.Lerp(_boostVisual, _boostAmount, ExpSmoothing(response, dt));

        float duration = Mathf.Max(0.01f, boostOnsetPulseDuration);
        _boostOnsetPulse = Mathf.MoveTowards(_boostOnsetPulse, 0f, dt / duration);
    }

    // ══════════════════════════════════════════════════════════════
    //  MATH HELPERS
    // ══════════════════════════════════════════════════════════════

    /// <summary>Frame-rate-independent exponential smoothing factor.</summary>
    private static float ExpSmoothing(float responseSpeed, float dt)
    {
        if (responseSpeed <= 0f) return 1f;
        return 1f - Mathf.Exp(-responseSpeed * dt);
    }

    /// <summary>LookRotation that survives degenerate forward/up pairs.</summary>
    private static Quaternion SafeLookRotation(Vector3 forward, Vector3 up, Quaternion fallback)
    {
        if (forward.sqrMagnitude < 0.0001f)
            return fallback;

        forward.Normalize();

        // Forward and up almost parallel: pick any perpendicular up.
        if (Mathf.Abs(Vector3.Dot(forward, up.normalized)) > 0.999f)
        {
            up = Vector3.Cross(forward, Vector3.right);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.Cross(forward, Vector3.forward);
        }

        return Quaternion.LookRotation(forward, up);
    }

    // ══════════════════════════════════════════════════════════════
    //  GIZMOS
    // ══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (target == null) return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, collisionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(target.position, transform.position);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(target.position + _referenceUp * tpLookHeight, 0.25f);

        Gizmos.color = Color.green;
        Gizmos.DrawRay(target.position, _followDirection * 4f);

        Gizmos.color = Color.white;
        Gizmos.DrawRay(target.position, _referenceUp * 3f);

        if (currentPerspective == CameraPerspective.FirstPerson)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(target.TransformPoint(ActiveProfile.mountOffset), 0.15f);
        }
    }
#endif
}
