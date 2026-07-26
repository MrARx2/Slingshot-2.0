using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// V2 Cockpit / Input Layer.
/// W/S throttle, A/D edge shift, mouse steering/pitch, Q roof thrusters,
/// E bottom thrusters, Space overcharge, R stabilizer toggle, Shift Grip Breaker.
/// </summary>
public class PilotCommandInterface : MonoBehaviour
{
    [Header("Mouse Sensitivity")]
    public float mouseSensitivityX = 0.03f;
    public float mouseSensitivityY = 0.02f;
    public bool invertMouseX = false;
    public bool invertMouseY = false;

    [Header("Keyboard Fallbacks")]
    public Key roofThrusterKey = Key.Q;
    public Key bottomThrusterKey = Key.E;
    public Key overchargeKey = Key.Space;
    public Key stabilizerToggleKey = Key.R;
    public Key gripBreakerKey = Key.LeftShift;

    [Header("Execution")]
    public bool sampleInputInternally = false;

    public PilotCommand CurrentCommand { get; private set; }

    private InputAction _throttleAction;
    private InputAction _edgeShiftAction;
    private InputAction _mouseDeltaAction;
    private InputAction _roofThrustersAction;
    private InputAction _bottomThrustersAction;
    private InputAction _overchargeAction;
    private InputAction _stabilizerToggleAction;
    private InputAction _gripBreakerAction;

    private bool _usingInlineActions;
    private Vector2 _mouseDeltaAccum;

    private bool _overchargePressedLatch;
    private bool _overchargeReleasedLatch;
    private bool _stabilizerTogglePressedLatch;

    private int _sampleFrame;

    private void OnEnable()
    {
        SetupInput();
    }

    private void OnDisable()
    {
        TeardownInput();
    }

    private void Update()
    {
        if (sampleInputInternally)
        {
            TickInput();
        }
    }

    public void TickInput()
    {
        _mouseDeltaAccum += _mouseDeltaAction?.ReadValue<Vector2>() ?? Vector2.zero;

        if (WasPressedThisFrame(_overchargeAction, overchargeKey))
        {
            _overchargePressedLatch = true;
        }

        if (WasReleasedThisFrame(_overchargeAction, overchargeKey))
        {
            _overchargeReleasedLatch = true;
        }

        if (WasPressedThisFrame(_stabilizerToggleAction, stabilizerToggleKey))
        {
            _stabilizerTogglePressedLatch = true;
        }
    }

    public void SampleInput()
    {
        _sampleFrame++;

        float throttle = _throttleAction?.ReadValue<float>() ?? 0f;
        float edgeShift = _edgeShiftAction?.ReadValue<float>() ?? 0f;

        Vector2 mouseDelta = _mouseDeltaAccum;
        _mouseDeltaAccum = Vector2.zero;

        float mouseX = mouseDelta.x * mouseSensitivityX;
        float mouseY = mouseDelta.y * mouseSensitivityY;

        if (invertMouseX) mouseX = -mouseX;
        if (invertMouseY) mouseY = -mouseY;

        bool roofThrustersHeld = IsPressed(_roofThrustersAction, roofThrusterKey);
        bool bottomThrustersHeld = IsPressed(_bottomThrustersAction, bottomThrusterKey);
        bool overchargeHeld = IsPressed(_overchargeAction, overchargeKey);
        bool gripBreakerHeld = IsPressed(_gripBreakerAction, gripBreakerKey);

        bool overchargePressed = _overchargePressedLatch;
        bool overchargeReleased = _overchargeReleasedLatch;
        bool stabilizerTogglePressed = _stabilizerTogglePressedLatch;

        // Do NOT clear one-shot latches here. Update can run multiple times before
        // the next FixedUpdate, so clearing here can make R / Space release vanish
        // before the physics pipeline consumes them. CraftCore clears them after
        // FixedUpdate via ClearConsumedOneShotInputs().

        CurrentCommand = new PilotCommand
        {
            sampleFrame = _sampleFrame,
            throttle = throttle,
            steer = mouseX,
            edgeShift = edgeShift,
            pitch = mouseY,
            roofThrustersHeld = roofThrustersHeld,
            bottomThrustersHeld = bottomThrustersHeld,
            overchargeHeld = overchargeHeld,
            overchargePressed = overchargePressed,
            overchargeReleased = overchargeReleased,
            stabilizerTogglePressed = stabilizerTogglePressed,
            gripBreakerHeld = gripBreakerHeld
        };
    }


    /// <summary>
    /// Clears one-shot input events after the physics step has consumed them.
    /// This prevents missed R toggles / Space releases when Update runs more
    /// often than FixedUpdate, while also preventing duplicate consumption across
    /// multiple physics ticks.
    /// </summary>
    public void ClearConsumedOneShotInputs()
    {
        _overchargePressedLatch = false;
        _overchargeReleasedLatch = false;
        _stabilizerTogglePressedLatch = false;

        PilotCommand command = CurrentCommand;
        command.overchargePressed = false;
        command.overchargeReleased = false;
        command.stabilizerTogglePressed = false;
        CurrentCommand = command;
    }

    private void SetupInput()
    {
        _usingInlineActions = false;

        var playerInput = GetComponent<PlayerInput>();

        if (playerInput != null && playerInput.actions != null)
        {
            var map = playerInput.actions.FindActionMap("Hovercraft");

            if (map != null)
            {
                _throttleAction = map.FindAction("Throttle");
                _edgeShiftAction = map.FindAction("EdgeShift")
                                ?? map.FindAction("Carve")
                                ?? map.FindAction("Strafe")
                                ?? map.FindAction("Steer"); // legacy name: A/D edge shift, not mouse steering
                _mouseDeltaAction = map.FindAction("MouseDelta");

                _roofThrustersAction = map.FindAction("RoofThrusters")
                                    ?? map.FindAction("Roof")
                                    ?? map.FindAction("Downforce");

                _bottomThrustersAction = map.FindAction("BottomThrusters")
                                      ?? map.FindAction("Bottom")
                                      ?? map.FindAction("Lift");

                _overchargeAction = map.FindAction("Overcharge")
                                  ?? map.FindAction("Jump");

                _stabilizerToggleAction = map.FindAction("StabilizerToggle")
                                        ?? map.FindAction("Stabilizer");

                _gripBreakerAction = map.FindAction("GripBreaker")
                                  ?? map.FindAction("Boost");

                map.Enable();
                return;
            }
        }

        _usingInlineActions = true;

        _throttleAction = new InputAction("Throttle", InputActionType.Value);
        _throttleAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/s")
            .With("Positive", "<Keyboard>/w");

        _edgeShiftAction = new InputAction("EdgeShift", InputActionType.Value);
        _edgeShiftAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d");

        _mouseDeltaAction = new InputAction("MouseDelta", InputActionType.Value, "<Mouse>/delta");
        _roofThrustersAction = new InputAction("RoofThrusters", InputActionType.Button, "<Keyboard>/q");
        _bottomThrustersAction = new InputAction("BottomThrusters", InputActionType.Button, "<Keyboard>/e");
        _overchargeAction = new InputAction("Overcharge", InputActionType.Button, "<Keyboard>/space");
        _stabilizerToggleAction = new InputAction("StabilizerToggle", InputActionType.Button, "<Keyboard>/r");
        _gripBreakerAction = new InputAction("GripBreaker", InputActionType.Button, "<Keyboard>/leftShift");

        _throttleAction.Enable();
        _edgeShiftAction.Enable();
        _mouseDeltaAction.Enable();
        _roofThrustersAction.Enable();
        _bottomThrustersAction.Enable();
        _overchargeAction.Enable();
        _stabilizerToggleAction.Enable();
        _gripBreakerAction.Enable();
    }

    private void TeardownInput()
    {
        if (!_usingInlineActions)
        {
            return;
        }

        _throttleAction?.Disable();
        _edgeShiftAction?.Disable();
        _mouseDeltaAction?.Disable();
        _roofThrustersAction?.Disable();
        _bottomThrustersAction?.Disable();
        _overchargeAction?.Disable();
        _stabilizerToggleAction?.Disable();
        _gripBreakerAction?.Disable();

        _throttleAction?.Dispose();
        _edgeShiftAction?.Dispose();
        _mouseDeltaAction?.Dispose();
        _roofThrustersAction?.Dispose();
        _bottomThrustersAction?.Dispose();
        _overchargeAction?.Dispose();
        _stabilizerToggleAction?.Dispose();
        _gripBreakerAction?.Dispose();
    }

    private static bool IsPressed(InputAction action, Key fallbackKey)
    {
        if (action != null)
        {
            return action.IsPressed();
        }

        return Keyboard.current != null
            && Keyboard.current[fallbackKey] != null
            && Keyboard.current[fallbackKey].isPressed;
    }

    private static bool WasPressedThisFrame(InputAction action, Key fallbackKey)
    {
        if (action != null)
        {
            return action.WasPressedThisFrame();
        }

        return Keyboard.current != null
            && Keyboard.current[fallbackKey] != null
            && Keyboard.current[fallbackKey].wasPressedThisFrame;
    }

    private static bool WasReleasedThisFrame(InputAction action, Key fallbackKey)
    {
        if (action != null)
        {
            return action.WasReleasedThisFrame();
        }

        return Keyboard.current != null
            && Keyboard.current[fallbackKey] != null
            && Keyboard.current[fallbackKey].wasReleasedThisFrame;
    }
}
