using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Thruster-based hovercraft controller.
/// Main movement is physical and thruster-driven.
///
/// Controls:
///   W/S     → Main / Brake thruster
///   A/D     → Differential strafe/yaw thrusters
///   Mouse X → Yaw + carve lean influence / air roll
///   Mouse Y → Pitch lean / air pitch
///   Space   → Jump
///   LShift  → Grip Breaker / drift grip profile
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ThrusterManager))]
public class ThrusterHovercraftController_V1_Legacy : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  THRUSTER REFERENCES
    // ══════════════════════════════════════════════════════════════

    [Header("Thruster Assignments")]
    public Thruster hoverFL;
    public Thruster hoverFR;
    public Thruster hoverRL;
    public Thruster hoverRR;

    public Thruster mainThruster;
    public Thruster brakeThruster;

    public Thruster strafeFL;
    public Thruster strafeFR;
    public Thruster strafeBL;
    public Thruster strafeBR;

    // ══════════════════════════════════════════════════════════════
    //  HOVER
    // ══════════════════════════════════════════════════════════════

    [Header("Hover PD Controller")]
    public float hoverHeight = 2.0f;

    [Tooltip("Height correction strength. Higher = tries harder to stay at hover height.")]
    public float hoverKp = 0.42f;

    [Tooltip("Vertical damping. Higher = less bounce, lower = more natural oscillation.")]
    public float hoverKd = 0.18f;

    [Tooltip("Maximum allowed hover thruster throttle outside of jump.")]
    public float hoverMaxThrottle = 2.25f;

    [Header("Hover Feel / Smoothing")]
    [Tooltip("How quickly hover thrusters change output. Lower = smoother/heavier.")]
    public float hoverThrusterResponseSpeed = 7f;

    [Tooltip("If true, jump burst ignores smoothing for a sharp impulse.")]
    public bool jumpBypassesHoverSmoothing = true;

    [Tooltip("Allows hover to gently oscillate instead of perfectly locking height.")]
    [Range(0f, 1f)]
    public float hoverOscillationAmount = 0.035f;

    [Tooltip("Speed of the natural hover bob/oscillation.")]
    public float hoverOscillationSpeed = 1.25f;

    [Tooltip("Per-thruster phase variation so all hover points do not pulse together.")]
    public float hoverOscillationPhaseSpread = 0.7f;

    // ══════════════════════════════════════════════════════════════
    //  GROUNDED ATTITUDE / STABILITY
    // ══════════════════════════════════════════════════════════════

    [Header("Grounded Attitude - Corner Target Heights")]
    [Tooltip("Maximum left/right target-height difference used for intentional grounded roll lean.")]
    public float maxRollHoverHeightOffset = 0.22f;

    [Tooltip("Maximum front/rear target-height difference used for intentional grounded pitch lean.")]
    public float maxPitchHoverHeightOffset = 0.18f;

    [Tooltip("How strongly current roll angle changes corner target heights to self-level.")]
    public float autoLevelRollHeightPerDegree = 0.012f;

    [Tooltip("How strongly current pitch angle changes corner target heights to self-level.")]
    public float autoLevelPitchHeightPerDegree = 0.01f;

    [Tooltip("Maximum target-height correction allowed from auto-leveling.")]
    public float maxAutoLevelHeightOffset = 0.45f;

    [Tooltip("Flip this if grounded roll stabilization makes the craft roll worse. This affects auto-leveling only.")]
    public bool invertGroundedRollHeightOffset = false;

    [Tooltip("Flip this if grounded pitch stabilization makes the craft pitch worse. This affects auto-leveling only.")]
    public bool invertGroundedPitchHeightOffset = false;

    [Header("Carve Lean")]
    [Tooltip("How much A/D contributes to inside-edge carve lean while grounded.")]
    [Range(0f, 1f)]
    public float strafeCarveLeanWeight = 0.65f;

    [Tooltip("How much Mouse X yaw contributes to inside-edge carve lean while grounded.")]
    [Range(0f, 1f)]
    public float yawCarveLeanWeight = 0.25f;

    [Tooltip("Flip this if intentional carve lean goes the wrong way. This does not affect auto-leveling.")]
    public bool invertCarveLeanDirection = false;

    [Header("Anti-Flip Safety")]
    [Tooltip("When roll/pitch exceeds this angle, strafe/yaw thrusters begin reducing output.")]
    public float strafeSafetyStartAngle = 18f;

    [Tooltip("When roll/pitch reaches this angle, strafe/yaw thrusters are heavily reduced.")]
    public float strafeSafetyFullAngle = 42f;

    [Tooltip("Minimum strafe/yaw authority when the craft is dangerously tilted.")]
    [Range(0f, 1f)]
    public float minimumStrafeAuthorityWhenTilted = 0.2f;

    [Tooltip("When roll rate exceeds this value, strafe/yaw thrusters begin reducing output.")]
    public float strafeSafetyStartRollRate = 120f;

    [Tooltip("When roll rate reaches this value, strafe/yaw thrusters are heavily reduced.")]
    public float strafeSafetyFullRollRate = 300f;

    // ══════════════════════════════════════════════════════════════
    //  LEAN / ATTITUDE INPUT
    // ══════════════════════════════════════════════════════════════

    [Header("Lean Targets")]
    [Tooltip("Max throttle bias applied to hover thrusters for airborne roll/pitch attitude control.")]
    [Range(0f, 2f)]
    public float maxLeanBias = 0.25f;

    [Tooltip("How fast lean targets respond to input.")]
    public float leanResponseSpeed = 1.0f;

    [Tooltip("Target roll angle limit in degrees.")]
    public float maxRollTarget = 22f;

    [Tooltip("Target pitch angle limit in degrees.")]
    public float maxPitchTarget = 15f;

    [Tooltip("How fast lean targets decay back toward zero.")]
    public float leanDecaySpeed = 7f;

    [Header("Air Attitude Control")]
    [Tooltip("Mouse X roll strength while airborne. Uses hover thrusters.")]
    [Range(0f, 10f)]
    public float airRollSensitivity = 1.5f;

    [Tooltip("Extra multiplier for airborne roll authority.")]
    [Range(0f, 5f)]
    public float airRollAuthority = 0.65f;

    [Tooltip("How much A/D contributes to air roll.")]
    [Range(0f, 1f)]
    public float airKeyboardRollInfluence = 0.35f;

    [Tooltip("Mouse Y pitch strength while airborne. Uses hover thrusters.")]
    [Range(0f, 10f)]
    public float airControlSensitivity = 1.35f;

    [Tooltip("Extra multiplier for airborne pitch authority.")]
    [Range(0f, 5f)]
    public float airPitchAuthority = 0.75f;

    [Tooltip("Flip this if airborne roll reacts in the wrong direction.")]
    public bool invertAirRollControl = false;

    // ══════════════════════════════════════════════════════════════
    //  PROPULSION
    // ══════════════════════════════════════════════════════════════

    [Header("Propulsion")]
    public float throttleRampSpeed = 3f;

    [Header("Differential Strafe Thrusters")]
    [Range(0f, 2f)]
    public float strafeSensitivity = 0.75f;

    [Range(0f, 2f)]
    public float steerSensitivity = 0.85f;

    // ══════════════════════════════════════════════════════════════
    //  JUMP
    // ══════════════════════════════════════════════════════════════

    [Header("Jump")]
    public float jumpBurstMultiplier = 3f;
    public float jumpBurstDuration = 0.15f;
    public float jumpCooldown = 1.5f;

    // ══════════════════════════════════════════════════════════════
    //  EXTRA PHYSICS
    // ══════════════════════════════════════════════════════════════

    [Header("Extra Physics")]
    public float extraGravity = 15f;
    public float yawDamping = 4f;

    // ══════════════════════════════════════════════════════════════
    //  GRIP
    // ══════════════════════════════════════════════════════════════

    [Header("Grip - Normal Profile")]
    [Tooltip("Sideways traction. Higher = less sideways drift.")]
    public float lateralGrip = 7f;

    [Tooltip("Counter-trajectory traction when thrusting against current forward/backward motion.")]
    public float longitudinalGrip = 6f;

    [Tooltip("Forward/backward coasting drag only when not pressing throttle/brake.")]
    public float coastingGrip = 0.25f;

    [Header("Grip Breaker - Drift Profile")]
    public float lateralGripBreaker = 0.35f;
    public float longitudinalGripBreaker = 0.25f;
    public float coastingGripBreaker = 0.05f;

    [Tooltip("How fast Grip Breaker moves from normal grip to drift grip while held.")]
    public float gripBreakRate = 6f;

    [Tooltip("How fast grip returns after releasing Grip Breaker.")]
    public float gripRecoveryRate = 4f;

    [Tooltip("How much recovery is slowed by speed.")]
    public float gripRecoverySpeedPenalty = 0.035f;

    [Tooltip("Exponent for non-linear speed penalty.")]
    public float gripRecoveryExponent = 1.6f;

    [Tooltip("Minimum local forward/backward speed before counter-trajectory grip activates.")]
    public float counterTrajectoryMinSpeed = 0.4f;

    [Tooltip("Extra multiplier for grip when thrusting against current trajectory.")]
    public float counterTrajectoryGripMultiplier = 1.0f;

    [Range(0f, 1f)]
    public float gripBreakerYawDampingMultiplier = 0.35f;

    [Range(0f, 3f)]
    public float gripBreakerSteerMultiplier = 1.25f;

    [Tooltip("Prevents violent snap-back at high speed.")]
    public float maxGripAcceleration = 90f;

    [Tooltip("How much lateral slide energy becomes forward drive during grip recovery.")]
    [Range(0f, 1f)]
    public float driftSpeedConservation = 0.15f;

    // ══════════════════════════════════════════════════════════════
    //  GRIP BREAKER VISUAL
    // ══════════════════════════════════════════════════════════════

    [Header("Grip Breaker Visual Indicator")]
    [Tooltip("Small cube / mesh renderer used as the Grip Breaker visual indicator.")]
    public Renderer gripBreakerIndicatorRenderer;

    [Tooltip("Emission color when Grip Breaker is inactive.")]
    [ColorUsage(true, true)]
    public Color gripBreakerNormalEmission = Color.blue;

    [Tooltip("Emission color when Grip Breaker is active.")]
    [ColorUsage(true, true)]
    public Color gripBreakerActiveEmission = Color.red;

    [Tooltip("Emission brightness multiplier.")]
    public float gripBreakerEmissionIntensity = 3f;

    [Tooltip("If true, visual color follows GripBreakerAmount. If false, it snaps red instantly while held.")]
    public bool gripBreakerVisualUsesBlend = true;

    // ══════════════════════════════════════════════════════════════
    //  MOUSE
    // ══════════════════════════════════════════════════════════════

    [Header("Mouse Sensitivity")]
    public float mouseSensitivityX = 0.03f;
    public float mouseSensitivityY = 0.02f;
    public bool invertMouseX = false;
    public bool invertMouseY = false;

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC STATE
    // ══════════════════════════════════════════════════════════════

    public int GroundedCount { get; private set; }
    public bool IsGrounded { get; private set; }
    public float GroundedFactor { get; private set; }
    public float CurrentSpeed { get; private set; }

    public float LeanTargetRoll { get; private set; }
    public float LeanTargetPitch { get; private set; }

    public float CurrentRollAngle { get; private set; }
    public float CurrentPitchAngle { get; private set; }

    public float JumpCooldownTimer { get; private set; }
    public float MainThrottle { get; private set; }

    public bool IsGripBroken { get; private set; }

    /// <summary>0 = normal grip, 1 = full drift profile.</summary>
    public float GripBreakerAmount => _gripBreak01;

    public bool IsRecoveringGrip => !IsGripBroken && _gripBreak01 > 0.01f;

    public float ActiveLateralGrip => _currentLateralGrip;
    public float ActiveLongitudinalGrip => _currentLongitudinalGrip;
    public float ActiveCoastingGrip => _currentCoastingGrip;

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE
    // ══════════════════════════════════════════════════════════════

    private Rigidbody _rb;
    private ThrusterManager _thrusterManager;

    private float _gravityThrottle;

    private InputAction _throttleAction;
    private InputAction _steerAction;
    private InputAction _mouseDeltaAction;
    private InputAction _jumpAction;
    private InputAction _gripBreakerAction;

    private bool _usingInlineActions;

    private float _throttleInput;
    private float _strafeInput;

    private Vector2 _mouseDelta;
    private Vector2 _mouseDeltaAccum;

    private bool _jumpPressed;
    private bool _gripBreakerHeld;

    private float _gripBreak01;
    private float _currentLateralGrip;
    private float _currentLongitudinalGrip;
    private float _currentCoastingGrip;

    private float _leanTargetRoll;
    private float _leanTargetPitch;

    private float _currentMainThrottle;
    private float _currentBrakeThrottle;

    private float _jumpCooldownRemaining;
    private float _jumpBurstRemaining;

    private float _hoverThrottleFL;
    private float _hoverThrottleFR;
    private float _hoverThrottleRL;
    private float _hoverThrottleRR;

    private Material _gripBreakerIndicatorMaterial;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _thrusterManager = GetComponent<ThrusterManager>();

        _rb.useGravity = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void Start()
    {
        CalculateGravityThrottle();

        _gripBreak01 = 0f;
        _currentLateralGrip = lateralGrip;
        _currentLongitudinalGrip = longitudinalGrip;
        _currentCoastingGrip = coastingGrip;

        SetupGripBreakerVisual();
        UpdateGripBreakerVisual();
    }

    void OnEnable()
    {
        SetupInput();
    }

    void OnDisable()
    {
        TeardownInput();
    }

    void OnDestroy()
    {
        if (_gripBreakerIndicatorMaterial != null)
        {
            Destroy(_gripBreakerIndicatorMaterial);
        }
    }

    void Update()
    {
        ReadInput();

        _mouseDeltaAccum += _mouseDeltaAction?.ReadValue<Vector2>() ?? Vector2.zero;

        if (_jumpCooldownRemaining > 0f)
        {
            _jumpCooldownRemaining -= Time.deltaTime;
        }
    }

    void FixedUpdate()
    {
        _mouseDelta = _mouseDeltaAccum;
        _mouseDeltaAccum = Vector2.zero;

        _thrusterManager.CastAllGroundRays();

        GroundedCount = _thrusterManager.GetGroundedCount();

        float groundTarget = GroundedCount >= 2 ? 1f : 0f;

        GroundedFactor = Mathf.MoveTowards(
            GroundedFactor,
            groundTarget,
            Time.fixedDeltaTime * 8f
        );

        IsGrounded = GroundedFactor > 0.5f;

        CurrentSpeed = _rb.linearVelocity.magnitude;
        CurrentRollAngle = GetCurrentRollAngle();
        CurrentPitchAngle = GetCurrentPitchAngle();

        UpdateGripProfile(CurrentSpeed);
        UpdateGripBreakerVisual();

        _rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

        UpdateLeanTargets();
        ApplyHoverThrusters();
        ApplyPropulsion();
        ApplyDifferentialStrafe();

        HandleJump();

        _thrusterManager.ApplyAllThrust();

        ApplyYawDamping();
        ApplyGripForces();

        CurrentSpeed = _rb.linearVelocity.magnitude;
        CurrentRollAngle = GetCurrentRollAngle();
        CurrentPitchAngle = GetCurrentPitchAngle();

        LeanTargetRoll = _leanTargetRoll;
        LeanTargetPitch = _leanTargetPitch;
        JumpCooldownTimer = _jumpCooldownRemaining;
        MainThrottle = _currentMainThrottle;
    }

    // ══════════════════════════════════════════════════════════════
    //  SETUP
    // ══════════════════════════════════════════════════════════════

    void CalculateGravityThrottle()
    {
        float totalGravity = Mathf.Abs(Physics.gravity.y) + extraGravity;
        int hoverCount = _thrusterManager.HoverThrusters.Count;

        if (hoverCount > 0)
        {
            float avgMaxForce = 0f;
            int validCount = 0;

            foreach (var entry in _thrusterManager.HoverThrusters)
            {
                if (entry != null && entry.thruster != null)
                {
                    avgMaxForce += entry.thruster.maxForce;
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                avgMaxForce /= validCount;
            }

            if (avgMaxForce > 0f)
            {
                _gravityThrottle = totalGravity / (hoverCount * avgMaxForce);
            }
            else
            {
                _gravityThrottle = 0f;
            }
        }
        else
        {
            _gravityThrottle = 0f;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════

    void SetupInput()
    {
        _usingInlineActions = false;

        var playerInput = GetComponent<PlayerInput>();

        if (playerInput != null && playerInput.actions != null)
        {
            var map = playerInput.actions.FindActionMap("Hovercraft");

            if (map != null)
            {
                _throttleAction = map.FindAction("Throttle");
                _steerAction = map.FindAction("Steer");
                _mouseDeltaAction = map.FindAction("MouseDelta");
                _jumpAction = map.FindAction("Jump");

                _gripBreakerAction = map.FindAction("GripBreaker") ?? map.FindAction("Boost");

                map.Enable();
                return;
            }
        }

        _usingInlineActions = true;

        _throttleAction = new InputAction("Throttle", InputActionType.Value);
        _throttleAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/s")
            .With("Positive", "<Keyboard>/w");

        _steerAction = new InputAction("Steer", InputActionType.Value);
        _steerAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d");

        _mouseDeltaAction = new InputAction(
            "MouseDelta",
            InputActionType.Value,
            "<Mouse>/delta"
        );

        _jumpAction = new InputAction(
            "Jump",
            InputActionType.Button,
            "<Keyboard>/space"
        );

        _gripBreakerAction = new InputAction(
            "GripBreaker",
            InputActionType.Button,
            "<Keyboard>/leftShift"
        );

        _throttleAction.Enable();
        _steerAction.Enable();
        _mouseDeltaAction.Enable();
        _jumpAction.Enable();
        _gripBreakerAction.Enable();
    }

    void TeardownInput()
    {
        if (!_usingInlineActions)
        {
            return;
        }

        _throttleAction?.Disable();
        _steerAction?.Disable();
        _mouseDeltaAction?.Disable();
        _jumpAction?.Disable();
        _gripBreakerAction?.Disable();

        _throttleAction?.Dispose();
        _steerAction?.Dispose();
        _mouseDeltaAction?.Dispose();
        _jumpAction?.Dispose();
        _gripBreakerAction?.Dispose();
    }

    void ReadInput()
    {
        _throttleInput = _throttleAction?.ReadValue<float>() ?? 0f;
        _strafeInput = _steerAction?.ReadValue<float>() ?? 0f;

        if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
        {
            _jumpPressed = true;
        }

        _gripBreakerHeld = _gripBreakerAction != null && _gripBreakerAction.IsPressed();
        IsGripBroken = _gripBreakerHeld;
    }

    // ══════════════════════════════════════════════════════════════
    //  GRIP BREAKER VISUAL
    // ══════════════════════════════════════════════════════════════

    void SetupGripBreakerVisual()
    {
        if (gripBreakerIndicatorRenderer == null)
        {
            return;
        }

        _gripBreakerIndicatorMaterial = gripBreakerIndicatorRenderer.material;
        _gripBreakerIndicatorMaterial.EnableKeyword("_EMISSION");
    }

    void UpdateGripBreakerVisual()
    {
        if (_gripBreakerIndicatorMaterial == null)
        {
            return;
        }

        float visualAmount = gripBreakerVisualUsesBlend
            ? _gripBreak01
            : (_gripBreakerHeld ? 1f : 0f);

        Color emissionColor = Color.Lerp(
            gripBreakerNormalEmission,
            gripBreakerActiveEmission,
            visualAmount
        );

        emissionColor *= gripBreakerEmissionIntensity;

        _gripBreakerIndicatorMaterial.SetColor(EmissionColorID, emissionColor);

        if (_gripBreakerIndicatorMaterial.HasProperty("_BaseColor"))
        {
            _gripBreakerIndicatorMaterial.SetColor("_BaseColor", emissionColor);
        }
        else if (_gripBreakerIndicatorMaterial.HasProperty("_Color"))
        {
            _gripBreakerIndicatorMaterial.SetColor("_Color", emissionColor);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  GRIP / TRACTION
    // ══════════════════════════════════════════════════════════════

    void UpdateGripProfile(float speed)
    {
        if (_gripBreakerHeld)
        {
            _gripBreak01 = Mathf.MoveTowards(
                _gripBreak01,
                1f,
                gripBreakRate * Time.fixedDeltaTime
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
                recoveryRate * Time.fixedDeltaTime
            );
        }

        float shapedBlend = Smooth01(_gripBreak01);

        _currentLateralGrip = Mathf.Lerp(
            lateralGrip,
            lateralGripBreaker,
            shapedBlend
        );

        _currentLongitudinalGrip = Mathf.Lerp(
            longitudinalGrip,
            longitudinalGripBreaker,
            shapedBlend
        );

        _currentCoastingGrip = Mathf.Lerp(
            coastingGrip,
            coastingGripBreaker,
            shapedBlend
        );
    }

    void ApplyGripForces()
    {
        if (!IsGrounded)
        {
            return;
        }

        Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);

        float lateralForce = -localVel.x * _currentLateralGrip;
        float longitudinalForce = 0f;

        float throttleAbs = Mathf.Clamp01(Mathf.Abs(_throttleInput));
        bool hasThrottleInput = throttleAbs > 0.05f;

        if (!hasThrottleInput)
        {
            longitudinalForce += -localVel.z * _currentCoastingGrip;
        }
        else
        {
            bool movingForward = localVel.z > counterTrajectoryMinSpeed;
            bool movingBackward = localVel.z < -counterTrajectoryMinSpeed;
            bool thrustingForward = _throttleInput > 0.05f;
            bool thrustingBackward = _throttleInput < -0.05f;

            bool thrustAgainstCurrentTrajectory =
                (thrustingForward && movingBackward) ||
                (thrustingBackward && movingForward);

            if (thrustAgainstCurrentTrajectory)
            {
                longitudinalForce +=
                    -localVel.z *
                    _currentLongitudinalGrip *
                    counterTrajectoryGripMultiplier;
            }
        }

        if (!_gripBreakerHeld && _gripBreak01 > 0.01f && Mathf.Abs(localVel.x) > 0.05f)
        {
            float lateralDecel = Mathf.Abs(localVel.x) * _currentLateralGrip;
            float direction = localVel.z >= 0f ? 1f : -1f;

            longitudinalForce += direction * lateralDecel * driftSpeedConservation;
        }

        if (maxGripAcceleration > 0f)
        {
            lateralForce = Mathf.Clamp(
                lateralForce,
                -maxGripAcceleration,
                maxGripAcceleration
            );

            longitudinalForce = Mathf.Clamp(
                longitudinalForce,
                -maxGripAcceleration,
                maxGripAcceleration
            );
        }

        Vector3 localGripForce = new Vector3(lateralForce, 0f, longitudinalForce);
        Vector3 worldGripForce = transform.TransformDirection(localGripForce);

        _rb.AddForce(worldGripForce, ForceMode.Acceleration);
    }

    // ══════════════════════════════════════════════════════════════
    //  LEAN TARGETS
    // ══════════════════════════════════════════════════════════════

    void UpdateLeanTargets()
    {
        float mouseX = _mouseDelta.x * mouseSensitivityX;
        float mouseY = _mouseDelta.y * mouseSensitivityY;

        if (invertMouseX)
        {
            mouseX = -mouseX;
        }

        if (invertMouseY)
        {
            mouseY = -mouseY;
        }

        if (IsGrounded)
        {
            float yawDemand = mouseX;

            // Carve lean:
            // A / left input gives negative target roll, lowering the left side.
            // D / right input gives positive target roll, lowering the right side.
            float targetRoll =
                _strafeInput * maxRollTarget * strafeCarveLeanWeight
                + yawDemand * maxRollTarget * yawCarveLeanWeight;

            if (invertCarveLeanDirection)
            {
                targetRoll = -targetRoll;
            }

            _leanTargetRoll = Mathf.MoveTowards(
                _leanTargetRoll,
                targetRoll,
                leanResponseSpeed * Time.fixedDeltaTime * maxRollTarget * 2f
            );

            _leanTargetRoll = Mathf.Clamp(
                _leanTargetRoll,
                -maxRollTarget,
                maxRollTarget
            );

            _leanTargetPitch += -mouseY * leanResponseSpeed;

            float decayFactor = 1f - Mathf.Clamp01(leanDecaySpeed * Time.fixedDeltaTime);

            if (Mathf.Abs(_mouseDelta.y) < 0.5f)
            {
                _leanTargetPitch *= decayFactor;
            }

            _leanTargetPitch = Mathf.Clamp(
                _leanTargetPitch,
                -maxPitchTarget,
                maxPitchTarget
            );
        }
        else
        {
            _leanTargetRoll = Mathf.MoveTowards(
                _leanTargetRoll,
                0f,
                leanDecaySpeed * Time.fixedDeltaTime * 2f
            );

            _leanTargetPitch = Mathf.MoveTowards(
                _leanTargetPitch,
                0f,
                leanDecaySpeed * Time.fixedDeltaTime * 2f
            );
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  HOVER THRUSTERS
    // ══════════════════════════════════════════════════════════════

    void ApplyHoverThrusters()
    {
        bool jumping = _jumpBurstRemaining > 0f;

        if (jumping)
        {
            _jumpBurstRemaining -= Time.fixedDeltaTime;
        }

        float rollDemand = 0f;
        float pitchDemand = 0f;

        // Grounded attitude is handled by corner target heights.
        // Airborne attitude is handled by hover-thruster differential throttle.
        if (!IsGrounded)
        {
            float mouseRollInput = _mouseDelta.x * mouseSensitivityX;

            if (invertMouseX)
            {
                mouseRollInput = -mouseRollInput;
            }

            float keyboardRollInput = _strafeInput * airKeyboardRollInfluence;
            float combinedRollInput = mouseRollInput + keyboardRollInput;

            float airRollControl = combinedRollInput * airRollSensitivity;

            if (invertAirRollControl)
            {
                airRollControl = -airRollControl;
            }

            rollDemand =
                Mathf.Clamp(airRollControl, -1f, 1f)
                * maxLeanBias
                * airRollAuthority;

            Transform cam = Camera.main != null ? Camera.main.transform : transform;

            float pitchInput = -_mouseDelta.y * mouseSensitivityY;

            if (invertMouseY)
            {
                pitchInput = -pitchInput;
            }

            Vector3 desiredWorldRotAxis = cam.right * pitchInput;
            Vector3 localRotAxis = transform.InverseTransformDirection(desiredWorldRotAxis);

            pitchDemand =
                Mathf.Clamp(localRotAxis.x * airControlSensitivity, -1f, 1f)
                * maxLeanBias
                * airPitchAuthority;
        }

        SetHoverThrottle(hoverFL, rollDemand, pitchDemand, true, true, jumping);
        SetHoverThrottle(hoverFR, rollDemand, pitchDemand, false, true, jumping);
        SetHoverThrottle(hoverRL, rollDemand, pitchDemand, true, false, jumping);
        SetHoverThrottle(hoverRR, rollDemand, pitchDemand, false, false, jumping);
    }

    void SetHoverThrottle(
        Thruster thruster,
        float rollDemand,
        float pitchDemand,
        bool isLeft,
        bool isFront,
        bool jumping
    )
    {
        if (thruster == null)
        {
            return;
        }

        float throttle = 0f;

        if (thruster.IsGrounded)
        {
            float targetHeight = GetCornerHoverTargetHeight(isLeft, isFront);
            float heightError = targetHeight - thruster.GroundDistance;

            Vector3 pointVel = _rb.GetPointVelocity(thruster.transform.position);
            float vertVel = Vector3.Dot(pointVel, thruster.GroundNormal);

            throttle =
                _gravityThrottle
                + heightError * hoverKp
                - vertVel * hoverKd;

            throttle += GetHoverOscillation(thruster);
        }

        // Airborne attitude control only.
        // Grounded roll/pitch comes from corner target heights.
        float rollAdjust = isLeft ? rollDemand : -rollDemand;
        float pitchAdjust = isFront ? -pitchDemand : pitchDemand;

        throttle += rollAdjust + pitchAdjust;

        if (jumping)
        {
            throttle = jumpBurstMultiplier;
        }

        float maxAllowed = jumping ? jumpBurstMultiplier : hoverMaxThrottle;
        throttle = Mathf.Clamp(throttle, 0f, maxAllowed);

        bool shouldSmooth = !(jumping && jumpBypassesHoverSmoothing);

        if (shouldSmooth)
        {
            throttle = SmoothHoverThrottle(thruster, throttle);
        }

        _thrusterManager.SetThrottle(thruster, throttle);
    }

    float GetCornerHoverTargetHeight(bool isLeft, bool isFront)
    {
        float targetHeight = hoverHeight;

        float inputRoll01 = maxRollTarget > 0f
            ? Mathf.Clamp(_leanTargetRoll / maxRollTarget, -1f, 1f)
            : 0f;

        float inputPitch01 = maxPitchTarget > 0f
            ? Mathf.Clamp(_leanTargetPitch / maxPitchTarget, -1f, 1f)
            : 0f;

        // Intentional carve/lean offsets.
        // Negative roll target lowers the left side.
        // Positive roll target lowers the right side.
        float intentionalRollOffset = inputRoll01 * maxRollHoverHeightOffset;
        float intentionalPitchOffset = inputPitch01 * maxPitchHoverHeightOffset;

        // Auto-leveling offsets are separate from intentional carve lean.
        float autoRollOffset = Mathf.Clamp(
            CurrentRollAngle * autoLevelRollHeightPerDegree,
            -maxAutoLevelHeightOffset,
            maxAutoLevelHeightOffset
        );

        float autoPitchOffset = Mathf.Clamp(
            CurrentPitchAngle * autoLevelPitchHeightPerDegree,
            -maxAutoLevelHeightOffset,
            maxAutoLevelHeightOffset
        );

        if (invertGroundedRollHeightOffset)
        {
            autoRollOffset = -autoRollOffset;
        }

        if (invertGroundedPitchHeightOffset)
        {
            autoPitchOffset = -autoPitchOffset;
        }

        float rollOffset = intentionalRollOffset + autoRollOffset;
        float pitchOffset = intentionalPitchOffset + autoPitchOffset;

        targetHeight += isLeft ? rollOffset : -rollOffset;
        targetHeight += isFront ? pitchOffset : -pitchOffset;

        return Mathf.Max(0.05f, targetHeight);
    }

    float SmoothHoverThrottle(Thruster thruster, float targetThrottle)
    {
        float response = Mathf.Max(0.01f, hoverThrusterResponseSpeed);
        float t = 1f - Mathf.Exp(-response * Time.fixedDeltaTime);

        if (thruster == hoverFL)
        {
            _hoverThrottleFL = Mathf.Lerp(_hoverThrottleFL, targetThrottle, t);
            return _hoverThrottleFL;
        }

        if (thruster == hoverFR)
        {
            _hoverThrottleFR = Mathf.Lerp(_hoverThrottleFR, targetThrottle, t);
            return _hoverThrottleFR;
        }

        if (thruster == hoverRL)
        {
            _hoverThrottleRL = Mathf.Lerp(_hoverThrottleRL, targetThrottle, t);
            return _hoverThrottleRL;
        }

        if (thruster == hoverRR)
        {
            _hoverThrottleRR = Mathf.Lerp(_hoverThrottleRR, targetThrottle, t);
            return _hoverThrottleRR;
        }

        return targetThrottle;
    }

    float GetHoverOscillation(Thruster thruster)
    {
        if (hoverOscillationAmount <= 0f)
        {
            return 0f;
        }

        float phase = 0f;

        if (thruster == hoverFL)
        {
            phase = 0f;
        }
        else if (thruster == hoverFR)
        {
            phase = hoverOscillationPhaseSpread;
        }
        else if (thruster == hoverRL)
        {
            phase = hoverOscillationPhaseSpread * 2f;
        }
        else if (thruster == hoverRR)
        {
            phase = hoverOscillationPhaseSpread * 3f;
        }

        float wave = Mathf.Sin(Time.time * hoverOscillationSpeed + phase);

        return wave * hoverOscillationAmount;
    }

    // ══════════════════════════════════════════════════════════════
    //  ORIENTATION MEASUREMENT
    // ══════════════════════════════════════════════════════════════

    float GetCurrentRollAngle()
    {
        Vector3 referenceUp = IsGrounded
            ? _thrusterManager.GetAverageGroundNormal()
            : Vector3.up;

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

    float GetCurrentPitchAngle()
    {
        Vector3 referenceUp = IsGrounded
            ? _thrusterManager.GetAverageGroundNormal()
            : Vector3.up;

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

    // ══════════════════════════════════════════════════════════════
    //  PROPULSION
    // ══════════════════════════════════════════════════════════════

    void ApplyPropulsion()
    {
        float targetMain = Mathf.Max(0f, _throttleInput);
        float targetBrake = Mathf.Max(0f, -_throttleInput);

        _currentMainThrottle = Mathf.MoveTowards(
            _currentMainThrottle,
            targetMain,
            throttleRampSpeed * Time.fixedDeltaTime
        );

        _currentBrakeThrottle = Mathf.MoveTowards(
            _currentBrakeThrottle,
            targetBrake,
            throttleRampSpeed * Time.fixedDeltaTime
        );

        if (mainThruster != null)
        {
            _thrusterManager.SetThrottle(mainThruster, _currentMainThrottle);
        }

        if (brakeThruster != null)
        {
            _thrusterManager.SetThrottle(brakeThruster, _currentBrakeThrottle);
        }
    }

    void ApplyDifferentialStrafe()
    {
        float strafeDemand = _strafeInput;

        float mouseX = _mouseDelta.x * mouseSensitivityX;

        if (invertMouseX)
        {
            mouseX = -mouseX;
        }

        float yawDemand = mouseX;

        float activeSteerSensitivity = steerSensitivity * Mathf.Lerp(
            1f,
            gripBreakerSteerMultiplier,
            _gripBreak01
        );

        float stabilityScale = GetStrafeStabilityScale();

        float flDemand =
            Mathf.Max(0f, strafeDemand) * strafeSensitivity
            + Mathf.Max(0f, yawDemand) * activeSteerSensitivity;

        float frDemand =
            Mathf.Max(0f, -strafeDemand) * strafeSensitivity
            + Mathf.Max(0f, -yawDemand) * activeSteerSensitivity;

        float blDemand =
            Mathf.Max(0f, strafeDemand) * strafeSensitivity
            + Mathf.Max(0f, -yawDemand) * activeSteerSensitivity;

        float brDemand =
            Mathf.Max(0f, -strafeDemand) * strafeSensitivity
            + Mathf.Max(0f, yawDemand) * activeSteerSensitivity;

        flDemand *= stabilityScale;
        frDemand *= stabilityScale;
        blDemand *= stabilityScale;
        brDemand *= stabilityScale;

        float cap = (strafeSensitivity + activeSteerSensitivity) * stabilityScale;

        flDemand = Mathf.Min(flDemand, cap);
        frDemand = Mathf.Min(frDemand, cap);
        blDemand = Mathf.Min(blDemand, cap);
        brDemand = Mathf.Min(brDemand, cap);

        if (strafeFL != null)
        {
            _thrusterManager.SetThrottle(strafeFL, flDemand);
        }

        if (strafeFR != null)
        {
            _thrusterManager.SetThrottle(strafeFR, frDemand);
        }

        if (strafeBL != null)
        {
            _thrusterManager.SetThrottle(strafeBL, blDemand);
        }

        if (strafeBR != null)
        {
            _thrusterManager.SetThrottle(strafeBR, brDemand);
        }
    }

    float GetStrafeStabilityScale()
    {
        if (!IsGrounded)
        {
            return 1f;
        }

        float maxTilt = Mathf.Max(
            Mathf.Abs(CurrentRollAngle),
            Mathf.Abs(CurrentPitchAngle)
        );

        float tilt01 = Mathf.InverseLerp(
            strafeSafetyStartAngle,
            strafeSafetyFullAngle,
            maxTilt
        );

        Vector3 localAngVel = transform.InverseTransformDirection(_rb.angularVelocity);
        float rollRateDegrees = Mathf.Abs(localAngVel.z * Mathf.Rad2Deg);

        float rollRate01 = Mathf.InverseLerp(
            strafeSafetyStartRollRate,
            strafeSafetyFullRollRate,
            rollRateDegrees
        );

        float safety01 = Mathf.Clamp01(Mathf.Max(tilt01, rollRate01));

        return Mathf.Lerp(
            1f,
            minimumStrafeAuthorityWhenTilted,
            safety01
        );
    }

    // ══════════════════════════════════════════════════════════════
    //  JUMP / DAMPING
    // ══════════════════════════════════════════════════════════════

    void HandleJump()
    {
        if (!_jumpPressed)
        {
            return;
        }

        if (_jumpCooldownRemaining <= 0f && IsGrounded)
        {
            _jumpBurstRemaining = jumpBurstDuration;
            _jumpCooldownRemaining = jumpCooldown;
        }

        _jumpPressed = false;
    }

    void ApplyYawDamping()
    {
        Vector3 localAngVel = transform.InverseTransformDirection(_rb.angularVelocity);

        float activeYawDamping = yawDamping * Mathf.Lerp(
            1f,
            gripBreakerYawDampingMultiplier,
            _gripBreak01
        );

        Vector3 yawDampTorque = -transform.up * localAngVel.y * activeYawDamping;

        _rb.AddTorque(yawDampTorque, ForceMode.Acceleration);
    }

    // ══════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════

    float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _rb == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, _rb.linearVelocity * 0.3f);

        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.forward * 3f);
    }
#endif
}