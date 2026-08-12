using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    public readonly struct V3PilotRawInputSnapshot
    {
        public V3PilotRawInputSnapshot(
            float throttle,
            float strafe,
            Vector2 mouseLook,
            Vector2 gamepadLook,
            bool lift,
            bool downforce,
            bool gripBreaker,
            bool emergencyOverload,
            double timestamp)
        {
            Throttle = throttle;
            Strafe = strafe;
            MouseLook = mouseLook;
            GamepadLook = gamepadLook;
            Lift = lift;
            Downforce = downforce;
            GripBreaker = gripBreaker;
            EmergencyOverload = emergencyOverload;
            Timestamp = timestamp;
        }
        public float Throttle { get; }
        public float Strafe { get; }
        public Vector2 MouseLook { get; }
        public Vector2 GamepadLook { get; }
        public bool Lift { get; }
        public bool Downforce { get; }
        public bool GripBreaker { get; }
        public bool EmergencyOverload { get; }
        public double Timestamp { get; }
    }

    public enum V3InputAuthorityState
    {
        Uninitialized,
        AwaitingCraft,
        Connected,
        Active,
        SuspendedByCursor,
        SuspendedByUI,
        NoLocalPilot,
        Faulted
    }

    public enum V3InputRoute
    {
        None,
        MainframeIntentBus,
        LegacyPipeline
    }

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
        private InputAction previousSystemPage;
        private InputAction nextSystemPage;
        private bool cyclePowerModeQueued;
        private bool previousSystemPageQueued;
        private bool nextSystemPageQueued;
        private bool cursorCaptured = true;
        private bool uiSuspended;
        private V3ControllerPipeline targetPipeline;
        private V3CraftMainframe targetMainframe;
        private double lastIntentPublicationTime = -1d;
        private double lastLegacySubmissionTime = -1d;
        private double lastCommandReadTime = -1d;
        private V3PilotCommand currentCommand;
        private V3InputRoute route;
        private Object controlledInputOwner;
        private V3PilotCommand controlledCommand;

        public InputActionAsset Actions
        {
            get
            {
                EnsureActions();
                return actions;
            }
        }

        public bool StabilizationEnabled => stabilizationEnabled;
        private V3InputAuthorityState authorityState =
            V3InputAuthorityState.Uninitialized;
        public V3InputAuthorityState AuthorityState
        {
            get
            {
                if (authorityState ==
                    V3InputAuthorityState.Uninitialized)
                {
                    RefreshAuthorityState();
                }

                return authorityState;
            }
            private set => authorityState = value;
        }
        public V3ControllerPipeline TargetPipeline => targetPipeline;
        public V3CraftMainframe TargetMainframe => targetMainframe;
        public GameObject TargetCraft =>
            targetPipeline != null ? targetPipeline.gameObject : null;
        public string ActiveInputSource => gameObject.name;
        public bool AreInputActionsEnabled =>
            actions != null && actions.enabled;
        public bool IsIntentBusConnected =>
            targetPipeline != null && targetMainframe != null;
        public bool IsPublishingIntent { get; private set; }
        public double LastIntentPublicationTime =>
            lastIntentPublicationTime;
        public double LastLegacySubmissionTime =>
            lastLegacySubmissionTime;
        public double LastCommandReadTime => lastCommandReadTime;
        public V3PilotCommand CurrentCommand => currentCommand;
        public V3PilotRawInputSnapshot RawInputSnapshot { get; private set; }
        public float CurrentThrottle => currentCommand.Throttle;
        public float CurrentSteering => currentCommand.Yaw;
        public V3InputRoute Route => route;
        public bool HasControlledInput => controlledInputOwner != null;
        public string CurrentBuildId
        {
            get
            {
                V3CraftRuntime craft = targetPipeline != null
                    ? targetPipeline.GetComponent<V3CraftRuntime>()
                    : null;
                return craft != null && craft.Build != null
                    ? craft.Build.StableId
                    : string.Empty;
            }
        }

        private void OnEnable()
        {
            EnsureActions();
            actions.Enable();
            RefreshAuthorityState();
        }

        private void OnDisable()
        {
            actions?.Disable();
            cyclePowerModeQueued = false;
            previousSystemPageQueued = false;
            nextSystemPageQueued = false;
            IsPublishingIntent = false;
            AuthorityState = V3InputAuthorityState.NoLocalPilot;
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

            if (previousSystemPage.WasPressedThisFrame())
            {
                previousSystemPageQueued = true;
            }

            if (nextSystemPage.WasPressedThisFrame())
            {
                nextSystemPageQueued = true;
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

            float rawThrottle = throttle.ReadValue<float>();
            float rawStrafe = strafe.ReadValue<float>();
            bool rawLift = lift.IsPressed();
            bool rawDownforce = downforce.IsPressed();
            bool rawGripBreaker = gripBreaker.IsPressed();
            bool rawEmergencyOverload = emergencyOverload.IsPressed();
            double commandTime = Time.timeAsDouble;
            RawInputSnapshot = new V3PilotRawInputSnapshot(
                rawThrottle,
                rawStrafe,
                mouseDelta,
                stickLook,
                rawLift,
                rawDownforce,
                rawGripBreaker,
                rawEmergencyOverload,
                commandTime);

            currentCommand = new V3PilotCommand
            {
                Throttle = Mathf.Clamp(rawThrottle, -1f, 1f),
                Strafe = Mathf.Clamp(rawStrafe, -1f, 1f),
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
                Lift = rawLift ? 1f : 0f,
                Downforce = rawDownforce ? 1f : 0f,
                StabilizationEnabled = stabilizationEnabled,
                GripBreaker = rawGripBreaker,
                EmergencyOverload = rawEmergencyOverload,
                CyclePowerAllocationMode = cyclePowerMode
            };
            lastCommandReadTime = commandTime;
            return currentCommand;
        }

        public bool TryReadAuthoritativeCommand(
            out V3PilotCommand command)
        {
            if (controlledInputOwner != null)
            {
                currentCommand = controlledCommand;
                lastCommandReadTime = Time.timeAsDouble;
                command = currentCommand;
                IsPublishingIntent = false;
                return true;
            }
            bool active =
                AuthorityState == V3InputAuthorityState.Active;
            command = active
                ? ReadCommand()
                : new V3PilotCommand
                {
                    StabilizationEnabled = stabilizationEnabled
                };
            IsPublishingIntent = false;
            return active;
        }

        public void ResetDynamicState()
        {
            cyclePowerModeQueued = false;
            previousSystemPageQueued = false;
            nextSystemPageQueued = false;
            currentCommand = default;
            RawInputSnapshot = default;
            lastIntentPublicationTime = -1d;
            lastLegacySubmissionTime = -1d;
            lastCommandReadTime = -1d;
            IsPublishingIntent = false;
        }

        /// <summary>
        /// Test-only command injection. Commands still enter through this
        /// adapter and the normal pipeline/Mainframe/device chain. The API is
        /// unavailable in non-development players and never applies physics.
        /// </summary>
        public bool BeginControlledInput(Object owner)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (owner == null || controlledInputOwner != null)
                return false;
            controlledInputOwner = owner;
            controlledCommand = default;
            return true;
#else
            return false;
#endif
        }

        public bool SetControlledCommand(
            Object owner,
            V3PilotCommand command)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (owner == null || controlledInputOwner != owner)
                return false;
            controlledCommand = command;
            return true;
#else
            return false;
#endif
        }

        public void EndControlledInput(Object owner)
        {
            if (owner == null || controlledInputOwner != owner)
                return;
            controlledInputOwner = null;
            controlledCommand = default;
            currentCommand = default;
            IsPublishingIntent = false;
        }

        public void BindCraft(
            V3ControllerPipeline pipeline,
            V3CraftMainframe mainframe)
        {
            if (targetPipeline == pipeline &&
                targetMainframe == mainframe)
            {
                RefreshAuthorityState();
                return;
            }

            if (targetPipeline != null)
            {
                targetPipeline.BindInputSource(null);
            }

            targetPipeline = pipeline;
            targetMainframe = mainframe;
            targetPipeline?.BindInputSource(this);
            RefreshAuthorityState();
        }

        public void UnbindCraft(V3ControllerPipeline pipeline = null)
        {
            if (pipeline != null && targetPipeline != pipeline)
            {
                return;
            }

            if (targetPipeline != null)
            {
                targetPipeline.BindInputSource(null);
            }

            targetPipeline = null;
            targetMainframe = null;
            IsPublishingIntent = false;
            RefreshAuthorityState();
        }

        public void SetCursorCaptured(bool captured)
        {
            cursorCaptured = captured;
            RefreshAuthorityState();
        }

        public void SetUiSuspended(bool suspended)
        {
            uiSuspended = suspended;
            RefreshAuthorityState();
        }

        public bool ConsumePreviousSystemPageRequest()
        {
            bool queued = previousSystemPageQueued;
            previousSystemPageQueued = false;
            return queued;
        }

        public bool ConsumeNextSystemPageRequest()
        {
            bool queued = nextSystemPageQueued;
            nextSystemPageQueued = false;
            return queued;
        }

        internal void NotifyIntentPublished(double timestamp)
        {
            IsPublishingIntent =
                (controlledInputOwner != null ||
                 AuthorityState == V3InputAuthorityState.Active) &&
                route == V3InputRoute.MainframeIntentBus;
            if (IsPublishingIntent)
            {
                lastIntentPublicationTime = timestamp;
            }
        }

        internal void NotifyCommandSubmitted(
            double timestamp,
            bool usedMainframeRoute)
        {
            if (usedMainframeRoute)
            {
                NotifyIntentPublished(timestamp);
                return;
            }

            IsPublishingIntent = false;
            if ((controlledInputOwner != null ||
                 AuthorityState == V3InputAuthorityState.Active) &&
                route == V3InputRoute.LegacyPipeline)
            {
                lastLegacySubmissionTime = timestamp;
            }
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

            previousSystemPage =
                pilot.AddAction(
                    "Previous System Page",
                    InputActionType.Button);
            previousSystemPage.AddBinding("<Keyboard>/o");
            previousSystemPage.AddBinding("<Gamepad>/dpad/left");

            nextSystemPage =
                pilot.AddAction(
                    "Next System Page",
                    InputActionType.Button);
            nextSystemPage.AddBinding("<Keyboard>/p");
            nextSystemPage.AddBinding("<Gamepad>/dpad/right");
        }

        private void RefreshAuthorityState()
        {
            if (!isActiveAndEnabled)
            {
                route = V3InputRoute.None;
                AuthorityState = V3InputAuthorityState.NoLocalPilot;
            }
            else if (targetPipeline == null)
            {
                route = V3InputRoute.None;
                AuthorityState = V3InputAuthorityState.AwaitingCraft;
            }
            else if (uiSuspended)
            {
                route = targetMainframe != null
                    ? V3InputRoute.MainframeIntentBus
                    : V3InputRoute.LegacyPipeline;
                AuthorityState = V3InputAuthorityState.SuspendedByUI;
            }
            else if (!cursorCaptured)
            {
                route = targetMainframe != null
                    ? V3InputRoute.MainframeIntentBus
                    : V3InputRoute.LegacyPipeline;
                AuthorityState =
                    V3InputAuthorityState.SuspendedByCursor;
            }
            else
            {
                route = targetMainframe != null
                    ? V3InputRoute.MainframeIntentBus
                    : V3InputRoute.LegacyPipeline;
                AuthorityState = V3InputAuthorityState.Active;
            }
        }
    }
}
