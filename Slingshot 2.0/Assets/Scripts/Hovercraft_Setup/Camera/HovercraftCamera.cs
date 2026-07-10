using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Custom hovercraft camera for racing + trick gameplay.
/// 
/// Design:
/// - Third-person chase camera by default.
/// - Optional first-person camera toggled with P.
/// - Grounded camera allows cinematic banking / roll influence.
/// - Airborne camera stabilizes horizon and avoids following full flips/twists.
/// - Camera follows movement intention using flattened velocity + craft forward.
/// - Uses CraftCore telemetry grounded state when available.
/// - Smoothing uses frame-rate-independent exponential interpolation where useful.
/// - Position smoothing uses SmoothDamp with explicit capped deltaTime.
/// - Dynamic FOV based on speed.
/// - Optional collision avoidance for third-person camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class HovercraftCamera : MonoBehaviour
{
    public enum CameraPerspective
    {
        ThirdPerson,
        FirstPerson
    }

    // ── Target ────────────────────────────────────────────────────
    [Header("Target")]
    [Tooltip("The hovercraft transform to follow.")]
    public Transform target;

    [Tooltip("Reference to the craft Rigidbody for velocity and speed readings.")]
    public Rigidbody targetRigidbody;

    [Header("Controller Integration")]
    [Tooltip("Optional reference to the hovercraft core. If assigned, camera uses its grounded state.")]
    public CraftCore craftCore;

    [Tooltip("If flat velocity is below this, camera will not trust velocity direction.")]
    public float minFlatSpeedForDirection = 1.5f;

    [Tooltip("At very low speed, keep the last stable camera direction instead of reacting to tiny physics drift.")]
    public bool holdDirectionAtLowSpeed = true;

    // ── Perspective ───────────────────────────────────────────────
    [Header("Perspective")]
    public CameraPerspective currentPerspective = CameraPerspective.ThirdPerson;

    [Tooltip("Key used to toggle between third-person and first-person.")]
    public Key perspectiveToggleKey = Key.P;

    // ── Ground Detection ──────────────────────────────────────────
    [Header("Ground Detection")]
    [Tooltip("If true, camera uses an external grounded value set by another script using SetGrounded(). Ignored if hovercraftController is assigned.")]
    public bool useExternalGroundedState = false;

    [Tooltip("Fallback raycast length used if no hovercraftController / external grounded state is used.")]
    public float groundCheckDistance = 2.2f;

    [Tooltip("Layers counted as ground for fallback grounded detection.")]
    public LayerMask groundLayers = ~0;

    [Tooltip("Current grounded state used by the camera.")]
    [SerializeField] private bool isGrounded;

    // ── Third Person Settings ─────────────────────────────────────
    [Header("Third Person")]
    public float thirdPersonBaseDistance = 8f;
    public float thirdPersonMaxDistance = 11f;

    public float thirdPersonBaseHeight = 3f;
    public float thirdPersonMaxHeight = 4f;

    [Tooltip("Extra distance added while airborne.")]
    public float airborneExtraDistance = 1.5f;

    [Tooltip("Extra height added while airborne.")]
    public float airborneExtraHeight = 0.8f;

    [Tooltip("Look target height above the craft center.")]
    public float thirdPersonLookHeight = 1.2f;

    [Tooltip("Look-ahead at low speed.")]
    public float thirdPersonMinLookAhead = 4f;

    [Tooltip("Look-ahead at high speed.")]
    public float thirdPersonMaxLookAhead = 14f;

    [Tooltip("Multiplier for lookahead while airborne. Lower values keep camera more focused on craft/landing.")]
    public float airborneLookAheadMultiplier = 0.65f;

    // ── First Person Settings ─────────────────────────────────────
    [Header("First Person")]
    [Tooltip("Local offset from the craft for first-person camera position.")]
    public Vector3 firstPersonLocalOffset = new Vector3(0f, 1.2f, 1.8f);

    [Tooltip("How far ahead first-person camera looks.")]
    public float firstPersonLookAhead = 8f;

    [Tooltip("How much first-person follows craft roll while grounded.")]
    [Range(0f, 1f)] public float firstPersonGroundedRollInfluence = 0.45f;

    [Tooltip("How much first-person follows craft roll while airborne.")]
    [Range(0f, 1f)] public float firstPersonAirborneRollInfluence = 0.05f;

    [Tooltip("How much first-person follows craft pitch while grounded.")]
    [Range(0f, 1f)] public float firstPersonGroundedPitchInfluence = 0.35f;

    [Tooltip("How much first-person follows craft pitch while airborne.")]
    [Range(0f, 1f)] public float firstPersonAirbornePitchInfluence = 0.05f;

    // ── Grounded / Airborne Rotation Influence ────────────────────
    [Header("Grounded Camera Influence")]
    [Tooltip("How much the third-person camera follows craft banking while grounded.")]
    [Range(0f, 1f)] public float groundedRollInfluence = 0.35f;

    [Tooltip("How much the third-person camera follows craft pitch while grounded.")]
    [Range(0f, 1f)] public float groundedPitchInfluence = 0.08f;

    [Tooltip("How much the camera direction follows craft forward while grounded.")]
    [Range(0f, 1f)] public float groundedForwardInfluence = 0.75f;

    [Header("Airborne Camera Influence")]
    [Tooltip("How much the third-person camera follows craft roll while airborne. Usually 0.")]
    [Range(0f, 1f)] public float airborneRollInfluence = 0.0f;

    [Tooltip("How much the third-person camera follows craft pitch while airborne. Usually 0.")]
    [Range(0f, 1f)] public float airbornePitchInfluence = 0.0f;

    [Tooltip("How much camera direction follows craft forward while airborne. Lower = more velocity-based.")]
    [Range(0f, 1f)] public float airborneForwardInfluence = 0.25f;

    // ── Speed / FOV ───────────────────────────────────────────────
    [Header("Speed / FOV")]
    public float maxSpeedForCamera = 40f;

    public float minFOV = 60f;
    public float maxFOV = 78f;

    [Tooltip("Optional extra FOV added by boost / grip breaker.")]
    public float boostExtraFOV = 7f;

    [Tooltip("If true, Grip Breaker from CraftCore / TractionCore adds FOV.")]
    public bool useGripBreakerAsBoostFOV = true;

    [Tooltip("Exponential FOV smoothing speed. Higher = faster response.")]
    public float fovSmoothSpeed = 8f;

    // ── Smoothing ─────────────────────────────────────────────────
    [Header("Smoothing")]
    [Tooltip("Caps camera deltaTime so frame spikes do not cause huge smoothing jumps.")]
    public float maxCameraDeltaTime = 0.033f;

    [Tooltip("Third-person position smooth time. Lower = tighter, higher = floatier.")]
    public float positionSmoothTime = 0.10f;

    [Tooltip("First-person position smoothing. Keep lower than third-person.")]
    public float firstPersonPositionSmoothTime = 0.035f;

    [Tooltip("Exponential rotation smoothing speed. Higher = more responsive.")]
    public float rotationSmoothSpeed = 12f;

    [Tooltip("Exponential movement direction smoothing speed. Lower = more stable, higher = more reactive.")]
    public float movementDirectionSmoothSpeed = 4f;

    [Tooltip("How quickly grounded -> airborne camera behavior blends if no controller is assigned.")]
    public float groundedToAirBlendSpeed = 4f;

    [Tooltip("How quickly airborne -> grounded camera behavior blends if no controller is assigned.")]
    public float airToGroundBlendSpeed = 8f;

    // ── Collision Avoidance ───────────────────────────────────────
    [Header("Collision Avoidance")]
    public bool useCollisionAvoidance = true;

    [Tooltip("Minimum distance from the craft when blocked by geometry.")]
    public float minDistance = 2f;

    [Tooltip("Radius of the camera sphere cast for collision detection.")]
    public float collisionRadius = 0.35f;

    [Tooltip("Layers representing solid ground/obstacles the camera should not clip through.")]
    public LayerMask collisionLayers = ~0;

    // ── Cursor ────────────────────────────────────────────────────
    [Header("Cursor")]
    public bool lockCursorOnStart = true;
    public bool unlockCursorOnDisable = true;

    // ── Internal State ────────────────────────────────────────────
    private Camera _cam;

    private Vector3 _positionVelocity;
    private Vector3 _smoothedMoveDirection;

    private float _airBlend;      // 0 = grounded, 1 = airborne
    private float _boostAmount;   // 0 = no boost, 1 = full boost

    private bool _initialized;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        TryAutoAssignReferences();
    }

    void Start()
    {
        TryAutoAssignReferences();

        if (lockCursorOnStart)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (target != null)
        {
            ResetCameraImmediate();
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        TryAutoAssignReferences();

        if (!_initialized)
            ResetCameraImmediate();

        float dt = GetCameraDeltaTime();

        HandlePerspectiveToggle();
        UpdateGroundedState();
        UpdateAirBlend(dt);

        float speed = GetSpeed();
        float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.001f, maxSpeedForCamera));

        Vector3 moveDirection = CalculateMovementDirection(speed01, dt);

        if (currentPerspective == CameraPerspective.ThirdPerson)
        {
            UpdateThirdPersonCamera(moveDirection, speed01, dt);
        }
        else
        {
            UpdateFirstPersonCamera(moveDirection, speed01, dt);
        }

        UpdateFOV(speed01, dt);
    }

    // ─────────────────────────────────────────────────────────────
    // Smoothing Helpers
    // ─────────────────────────────────────────────────────────────

    private float GetCameraDeltaTime()
    {
        return Mathf.Min(Time.deltaTime, maxCameraDeltaTime);
    }

    private static float ExpSmoothingFactor(float smoothingSpeed, float dt)
    {
        if (smoothingSpeed <= 0f)
            return 1f;

        return 1f - Mathf.Exp(-smoothingSpeed * dt);
    }

    // ─────────────────────────────────────────────────────────────
    // Reference Setup
    // ─────────────────────────────────────────────────────────────

    private void TryAutoAssignReferences()
    {
        if (target == null)
            return;

        if (targetRigidbody == null)
            targetRigidbody = target.GetComponent<Rigidbody>();

        if (craftCore == null)
            craftCore = target.GetComponent<CraftCore>();
    }

    // ─────────────────────────────────────────────────────────────
    // Perspective
    // ─────────────────────────────────────────────────────────────

    private void HandlePerspectiveToggle()
    {
        if (Keyboard.current == null) return;

        if (!Keyboard.current[perspectiveToggleKey].wasPressedThisFrame)
            return;

        currentPerspective =
            currentPerspective == CameraPerspective.ThirdPerson
                ? CameraPerspective.FirstPerson
                : CameraPerspective.ThirdPerson;

        ResetCameraImmediate();
    }

    // ─────────────────────────────────────────────────────────────
    // Grounded / Airborne
    // ─────────────────────────────────────────────────────────────

    private void UpdateGroundedState()
    {
        if (craftCore != null && craftCore.telemetry != null)
        {
            isGrounded = craftCore.telemetry.CurrentTelemetry.isGrounded;
            return;
        }

        if (useExternalGroundedState)
            return;

        Vector3 origin = target.position + Vector3.up * 0.1f;

        isGrounded = Physics.Raycast(
            origin,
            Vector3.down,
            groundCheckDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    public void SetGrounded(bool grounded)
    {
        isGrounded = grounded;
    }

    private void UpdateAirBlend(float dt)
    {
        if (craftCore != null && craftCore.telemetry != null)
        {
            // Controller: 0 = airborne, 1 = grounded.
            // Camera:     0 = grounded, 1 = airborne.
            _airBlend = 1f - craftCore.telemetry.CurrentTelemetry.groundedFactor;
            return;
        }

        float targetBlend = isGrounded ? 0f : 1f;
        float blendSpeed = isGrounded ? airToGroundBlendSpeed : groundedToAirBlendSpeed;

        _airBlend = Mathf.MoveTowards(
            _airBlend,
            targetBlend,
            dt * blendSpeed
        );
    }

    // ─────────────────────────────────────────────────────────────
    // Third Person
    // ─────────────────────────────────────────────────────────────

    private void UpdateThirdPersonCamera(Vector3 moveDirection, float speed01, float dt)
    {
        Vector3 cameraUp = CalculateCameraUp(
            groundedRollInfluence,
            airborneRollInfluence,
            groundedPitchInfluence,
            airbornePitchInfluence
        );

        float distance = Mathf.Lerp(thirdPersonBaseDistance, thirdPersonMaxDistance, speed01);
        float height = Mathf.Lerp(thirdPersonBaseHeight, thirdPersonMaxHeight, speed01);

        distance += airborneExtraDistance * _airBlend;
        height += airborneExtraHeight * _airBlend;

        float lookAhead = Mathf.Lerp(thirdPersonMinLookAhead, thirdPersonMaxLookAhead, speed01);
        lookAhead *= Mathf.Lerp(1f, airborneLookAheadMultiplier, _airBlend);

        Vector3 desiredPosition =
            target.position
            - moveDirection * distance
            + cameraUp * height;

        if (useCollisionAvoidance)
            desiredPosition = ApplyCollisionAvoidance(desiredPosition);

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref _positionVelocity,
            Mathf.Max(0.001f, positionSmoothTime),
            Mathf.Infinity,
            dt
        );

        Vector3 lookTarget =
            target.position
            + moveDirection * lookAhead
            + Vector3.up * thirdPersonLookHeight;

        Vector3 lookDirection = lookTarget - transform.position;

        if (lookDirection.sqrMagnitude < 0.001f)
            return;

        Quaternion desiredRotation = Quaternion.LookRotation(
            lookDirection.normalized,
            cameraUp
        );

        float rotationLerp = ExpSmoothingFactor(rotationSmoothSpeed, dt);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotationLerp
        );
    }

    // ─────────────────────────────────────────────────────────────
    // First Person
    // ─────────────────────────────────────────────────────────────

    private void UpdateFirstPersonCamera(Vector3 moveDirection, float speed01, float dt)
    {
        Vector3 cameraUp = CalculateCameraUp(
            firstPersonGroundedRollInfluence,
            firstPersonAirborneRollInfluence,
            firstPersonGroundedPitchInfluence,
            firstPersonAirbornePitchInfluence
        );

        Quaternion positionFrame = Quaternion.LookRotation(
            GetStableForward(),
            cameraUp
        );

        Vector3 desiredPosition =
            target.position + positionFrame * firstPersonLocalOffset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref _positionVelocity,
            Mathf.Max(0.001f, firstPersonPositionSmoothTime),
            Mathf.Infinity,
            dt
        );

        Vector3 lookTarget =
            target.position
            + moveDirection * firstPersonLookAhead
            + Vector3.up * firstPersonLocalOffset.y;

        Vector3 lookDirection = lookTarget - transform.position;

        if (lookDirection.sqrMagnitude < 0.001f)
            return;

        Quaternion desiredRotation = Quaternion.LookRotation(
            lookDirection.normalized,
            cameraUp
        );

        float rotationLerp = ExpSmoothingFactor(rotationSmoothSpeed, dt);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotationLerp
        );
    }

    // ─────────────────────────────────────────────────────────────
    // Direction / Rotation Helpers
    // ─────────────────────────────────────────────────────────────

    private Vector3 CalculateMovementDirection(float speed01, float dt)
    {
        Vector3 craftForward = GetFlatCraftForward();

        Vector3 flatVelocity = Vector3.zero;
        float flatSpeed = 0f;

        if (targetRigidbody != null)
        {
            // Important:
            // Use flat/horizontal velocity for camera direction.
            // Do not let hover bounce, jump velocity, or falling velocity steer the camera.
            flatVelocity = Vector3.ProjectOnPlane(targetRigidbody.linearVelocity, Vector3.up);
            flatSpeed = flatVelocity.magnitude;
        }

        Vector3 desiredDirection;
        bool hasReliableVelocity = flatSpeed >= minFlatSpeedForDirection;

        if (hasReliableVelocity)
        {
            Vector3 velocityDirection = flatVelocity.normalized;

            float forwardInfluence = Mathf.Lerp(
                groundedForwardInfluence,
                airborneForwardInfluence,
                _airBlend
            );

            // At higher speeds, trust velocity more.
            float finalCraftForwardInfluence = Mathf.Lerp(
                forwardInfluence,
                0.15f,
                speed01
            );

            desiredDirection = Vector3.Slerp(
                velocityDirection,
                craftForward,
                finalCraftForwardInfluence
            );
        }
        else
        {
            if (holdDirectionAtLowSpeed && _smoothedMoveDirection.sqrMagnitude > 0.001f)
            {
                // Do not let tiny physics drift rotate the camera while almost stationary.
                desiredDirection = _smoothedMoveDirection.normalized;
            }
            else
            {
                desiredDirection = craftForward;
            }
        }

        if (desiredDirection.sqrMagnitude < 0.001f)
            desiredDirection = craftForward;

        desiredDirection.Normalize();

        if (_smoothedMoveDirection.sqrMagnitude < 0.001f)
            _smoothedMoveDirection = desiredDirection;

        float directionLerp = ExpSmoothingFactor(movementDirectionSmoothSpeed, dt);

        _smoothedMoveDirection = Vector3.Slerp(
            _smoothedMoveDirection,
            desiredDirection,
            directionLerp
        );

        return _smoothedMoveDirection.normalized;
    }

    private Vector3 CalculateCameraUp(
        float groundedRoll,
        float airborneRoll,
        float groundedPitch,
        float airbornePitch
    )
    {
        float rollInfluence = Mathf.Lerp(groundedRoll, airborneRoll, _airBlend);
        float pitchInfluence = Mathf.Lerp(groundedPitch, airbornePitch, _airBlend);

        // Roll influence comes from target.up.
        Vector3 rollUp = Vector3.Slerp(Vector3.up, target.up, rollInfluence);

        // Pitch influence is kept subtle. This avoids camera flipping with craft pitch.
        Vector3 craftForward = target.forward;
        Vector3 flatForward = Vector3.ProjectOnPlane(craftForward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = GetFlatCraftForward();

        flatForward.Normalize();

        float pitchAmount = Vector3.SignedAngle(flatForward, craftForward, target.right) / 90f;
        pitchAmount = Mathf.Clamp(pitchAmount, -1f, 1f);

        Vector3 pitchAdjustedUp = Quaternion.AngleAxis(
            pitchAmount * pitchInfluence * 35f,
            target.right
        ) * rollUp;

        if (pitchAdjustedUp.sqrMagnitude < 0.001f)
            return Vector3.up;

        return pitchAdjustedUp.normalized;
    }

    private Vector3 GetStableForward()
    {
        Vector3 flatForward = GetFlatCraftForward();
        Vector3 craftForward = target.forward.normalized;

        // Grounded: allow more true craft forward.
        // Airborne: flatten heavily so flips do not control camera direction.
        Vector3 stableForward = Vector3.Slerp(
            craftForward,
            flatForward,
            _airBlend
        );

        if (stableForward.sqrMagnitude < 0.001f)
            return flatForward;

        return stableForward.normalized;
    }

    private Vector3 GetFlatCraftForward()
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
        {
            if (_smoothedMoveDirection.sqrMagnitude > 0.001f)
                return _smoothedMoveDirection.normalized;

            return Vector3.forward;
        }

        return flatForward.normalized;
    }

    // ─────────────────────────────────────────────────────────────
    // Collision / FOV
    // ─────────────────────────────────────────────────────────────

    private Vector3 ApplyCollisionAvoidance(Vector3 desiredWorldPos)
    {
        Vector3 origin = target.position + Vector3.up * thirdPersonLookHeight;
        Vector3 dirToCamera = desiredWorldPos - origin;

        float targetDistance = dirToCamera.magnitude;

        if (targetDistance < 0.001f)
            return desiredWorldPos;

        dirToCamera /= targetDistance;

        if (Physics.SphereCast(
            origin,
            collisionRadius,
            dirToCamera,
            out RaycastHit hit,
            targetDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore))
        {
            float safeDistance = Mathf.Max(hit.distance - collisionRadius, minDistance);
            return origin + dirToCamera * safeDistance;
        }

        return desiredWorldPos;
    }

    private void UpdateFOV(float speed01, float dt)
    {
        float boostAmount = Mathf.Clamp01(_boostAmount);

        if (useGripBreakerAsBoostFOV && craftCore != null && craftCore.traction != null)
        {
            boostAmount = Mathf.Max(boostAmount, craftCore.traction.GripBreakerAmount);
        }

        float targetFOV = Mathf.Lerp(minFOV, maxFOV, speed01);
        targetFOV += boostExtraFOV * boostAmount;

        float fovLerp = ExpSmoothingFactor(fovSmoothSpeed, dt);

        _cam.fieldOfView = Mathf.Lerp(
            _cam.fieldOfView,
            targetFOV,
            fovLerp
        );
    }

    public void SetBoostAmount(float amount)
    {
        _boostAmount = Mathf.Clamp01(amount);
    }

    private float GetSpeed()
    {
        if (craftCore != null)
            return craftCore.telemetry.CurrentTelemetry.speed;

        if (targetRigidbody == null)
            return 0f;

        return targetRigidbody.linearVelocity.magnitude;
    }

    // ─────────────────────────────────────────────────────────────
    // Reset
    // ─────────────────────────────────────────────────────────────

    public void ResetCameraImmediate()
    {
        if (target == null) return;

        TryAutoAssignReferences();

        UpdateGroundedState();

        if (craftCore != null)
            _airBlend = 1f - craftCore.telemetry.CurrentTelemetry.groundedFactor;
        else
            _airBlend = isGrounded ? 0f : 1f;

        Vector3 startDirection = GetFlatCraftForward();

        if (targetRigidbody != null)
        {
            Vector3 flatVelocity = Vector3.ProjectOnPlane(targetRigidbody.linearVelocity, Vector3.up);

            if (flatVelocity.magnitude >= minFlatSpeedForDirection)
                startDirection = flatVelocity.normalized;
        }

        _smoothedMoveDirection = startDirection.normalized;

        if (currentPerspective == CameraPerspective.ThirdPerson)
        {
            Vector3 cameraUp = CalculateCameraUp(
                groundedRollInfluence,
                airborneRollInfluence,
                groundedPitchInfluence,
                airbornePitchInfluence
            );

            transform.position =
                target.position
                - _smoothedMoveDirection * thirdPersonBaseDistance
                + cameraUp * thirdPersonBaseHeight;

            Vector3 lookTarget =
                target.position
                + _smoothedMoveDirection * thirdPersonMinLookAhead
                + Vector3.up * thirdPersonLookHeight;

            Vector3 lookDirection = lookTarget - transform.position;

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    lookDirection.normalized,
                    cameraUp
                );
            }
        }
        else
        {
            Vector3 cameraUp = CalculateCameraUp(
                firstPersonGroundedRollInfluence,
                firstPersonAirborneRollInfluence,
                firstPersonGroundedPitchInfluence,
                firstPersonAirbornePitchInfluence
            );

            Quaternion positionFrame = Quaternion.LookRotation(
                GetStableForward(),
                cameraUp
            );

            transform.position =
                target.position + positionFrame * firstPersonLocalOffset;

            Vector3 lookTarget =
                target.position
                + _smoothedMoveDirection * firstPersonLookAhead
                + Vector3.up * firstPersonLocalOffset.y;

            Vector3 lookDirection = lookTarget - transform.position;

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    lookDirection.normalized,
                    cameraUp
                );
            }
        }

        ResetSmoothing();

        if (_cam != null)
            _cam.fieldOfView = minFOV;

        _initialized = true;
    }

    private void ResetSmoothing()
    {
        _positionVelocity = Vector3.zero;
    }

    void OnDisable()
    {
        if (unlockCursorOnDisable)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (target == null) return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, collisionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(target.position, transform.position);

        Gizmos.color = Color.yellow;
        Vector3 lookPoint = target.position + Vector3.up * thirdPersonLookHeight;
        Gizmos.DrawWireSphere(lookPoint, 0.25f);

        Gizmos.color = Color.green;
        Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (flatForward.sqrMagnitude > 0.001f)
            Gizmos.DrawRay(target.position, flatForward.normalized * 4f);
    }
#endif
}