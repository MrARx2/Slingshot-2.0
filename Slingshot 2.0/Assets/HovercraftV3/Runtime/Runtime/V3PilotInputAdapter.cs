using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3PilotInputAdapter : MonoBehaviour
    {
        [SerializeField] private float mouseYawSensitivity = 0.03f;
        [SerializeField] private float mousePitchSensitivity = 0.02f;
        [SerializeField] private float gamepadLookSensitivity = 1f;
        [SerializeField] private bool stabilizationEnabled = true;

        private InputActionAsset actions;
        private InputAction throttle;
        private InputAction strafe;
        private InputAction mouseLook;
        private InputAction gamepadLook;
        private InputAction lift;
        private InputAction downforce;
        private InputAction gripBreaker;
        private InputAction emergencyOverload;
        private InputAction toggleStabilization;
        private InputAction cyclePowerAllocationMode;
        private bool cyclePowerModeQueued;

        public InputActionAsset Actions
        {
            get
            {
                EnsureActions();
                return actions;
            }
        }

        public bool StabilizationEnabled => stabilizationEnabled;

        private void OnEnable()
        {
            EnsureActions();
            actions.Enable();
        }

        private void OnDisable()
        {
            actions?.Disable();
            cyclePowerModeQueued = false;
        }

        private void Update()
        {
            EnsureActions();
            if (toggleStabilization.WasPressedThisFrame())
            {
                stabilizationEnabled = !stabilizationEnabled;
            }

            if (cyclePowerAllocationMode.WasPressedThisFrame())
            {
                RequestPowerModeCycle();
            }
        }

        private void OnDestroy()
        {
            if (actions == null)
            {
                return;
            }

            actions.Disable();
            if (Application.isPlaying)
            {
                Destroy(actions);
            }
            else
            {
                DestroyImmediate(actions);
            }

            actions = null;
        }

        public V3PilotCommand ReadCommand()
        {
            EnsureActions();
            Vector2 mouseDelta = mouseLook.ReadValue<Vector2>();
            Vector2 stickLook = gamepadLook.ReadValue<Vector2>();
            bool cyclePowerMode = cyclePowerModeQueued;
            cyclePowerModeQueued = false;

            return new V3PilotCommand
            {
                Throttle = Mathf.Clamp(throttle.ReadValue<float>(), -1f, 1f),
                Strafe = Mathf.Clamp(strafe.ReadValue<float>(), -1f, 1f),
                Yaw = Mathf.Clamp(
                    mouseDelta.x * mouseYawSensitivity +
                    stickLook.x * gamepadLookSensitivity,
                    -1f,
                    1f),
                Pitch = Mathf.Clamp(
                    -mouseDelta.y * mousePitchSensitivity -
                    stickLook.y * gamepadLookSensitivity,
                    -1f,
                    1f),
                Lift = lift.IsPressed() ? 1f : 0f,
                Downforce = downforce.IsPressed() ? 1f : 0f,
                StabilizationEnabled = stabilizationEnabled,
                GripBreaker = gripBreaker.IsPressed(),
                EmergencyOverload = emergencyOverload.IsPressed(),
                CyclePowerAllocationMode = cyclePowerMode
            };
        }

        public void RequestPowerModeCycle()
        {
            cyclePowerModeQueued = true;
        }

        public InputAction FindAction(string actionName)
        {
            EnsureActions();
            return actions.FindAction(actionName, false);
        }

        public string SaveBindingOverrides()
        {
            EnsureActions();
            return actions.SaveBindingOverridesAsJson();
        }

        public void LoadBindingOverrides(string json)
        {
            EnsureActions();
            if (string.IsNullOrWhiteSpace(json))
            {
                actions.RemoveAllBindingOverrides();
                return;
            }

            actions.LoadBindingOverridesFromJson(json);
        }

        private void EnsureActions()
        {
            if (actions != null)
            {
                return;
            }

            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            actions.name = "Hovercraft V3 Runtime Input";
            InputActionMap pilot = actions.AddActionMap("Pilot");

            throttle = pilot.AddAction("Throttle", InputActionType.Value);
            throttle.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/s")
                .With("Positive", "<Keyboard>/w");
            throttle.AddCompositeBinding("1DAxis")
                .With("Negative", "<Gamepad>/leftTrigger")
                .With("Positive", "<Gamepad>/rightTrigger");

            strafe = pilot.AddAction("Strafe", InputActionType.Value);
            strafe.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            strafe.AddBinding("<Gamepad>/leftStick/x");

            mouseLook = pilot.AddAction("Mouse Look", InputActionType.Value);
            mouseLook.AddBinding("<Mouse>/delta");
            gamepadLook =
                pilot.AddAction("Gamepad Look", InputActionType.Value);
            gamepadLook.AddBinding("<Gamepad>/rightStick");
            gamepadLook.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            lift = pilot.AddAction("Lift", InputActionType.Button);
            lift.AddBinding("<Keyboard>/e");
            lift.AddBinding("<Gamepad>/rightShoulder");

            downforce =
                pilot.AddAction("Downforce", InputActionType.Button);
            downforce.AddBinding("<Keyboard>/q");
            downforce.AddBinding("<Gamepad>/leftShoulder");

            gripBreaker =
                pilot.AddAction("Grip Breaker", InputActionType.Button);
            gripBreaker.AddBinding("<Keyboard>/leftShift");
            gripBreaker.AddBinding("<Keyboard>/rightShift");
            gripBreaker.AddBinding("<Gamepad>/buttonEast");

            emergencyOverload =
                pilot.AddAction("Emergency Overload", InputActionType.Button);
            emergencyOverload.AddBinding("<Keyboard>/leftCtrl");
            emergencyOverload.AddBinding("<Keyboard>/rightCtrl");
            emergencyOverload.AddBinding("<Gamepad>/buttonSouth");

            toggleStabilization =
                pilot.AddAction("Toggle Stabilization", InputActionType.Button);
            toggleStabilization.AddBinding("<Keyboard>/r");
            toggleStabilization.AddBinding("<Gamepad>/start");

            cyclePowerAllocationMode =
                pilot.AddAction("Cycle Power Mode", InputActionType.Button);
            cyclePowerAllocationMode.AddBinding("<Keyboard>/tab");
            cyclePowerAllocationMode.AddBinding("<Gamepad>/dpad/up");
        }
    }
}
