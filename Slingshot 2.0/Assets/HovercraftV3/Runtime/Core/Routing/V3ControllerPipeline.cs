using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3ControlExecutionPath
    {
        None,
        MainframeScheduled,
        LegacyFallback,
        FaultedNoFallback
    }

    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class V3ControllerPipeline : MonoBehaviour
    {
        private V3PilotInputAdapter input;
        private V3ActuatorCommandRouter router;
        private V3DriveController drive;
        private V3HoverController hover;
        private V3VectorController vector;
        private V3TractionController traction;
        private V3GimbalController gimbal;
        private V3ThermalController thermal;
        private V3CapabilityRegistry capabilities;
        private V3CraftMainframe mainframe;
        private bool initialized;
        private bool suppressNextPhysicsTick;

        public V3PilotInputAdapter InputSource => input;
        public bool IsInitialized => initialized;
        public V3ControlExecutionPath ActiveControlPath { get; private set; }
        public string ControlPathReason { get; private set; } = string.Empty;

        public void Initialize(
            V3PilotInputAdapter inputAdapter,
            V3ActuatorCommandRouter commandRouter,
            V3DriveController driveController,
            V3HoverController hoverController,
            V3VectorController vectorController,
            V3TractionController tractionController,
            V3GimbalController gimbalController,
            V3ThermalController thermalController,
            V3CapabilityRegistry capabilityRegistry)
        {
            input = inputAdapter;
            router = commandRouter;
            drive = driveController;
            hover = hoverController;
            vector = vectorController;
            traction = tractionController;
            gimbal = gimbalController;
            thermal = thermalController;
            capabilities = capabilityRegistry;
            initialized = router != null && capabilities != null;
        }

        private void FixedUpdate()
        {
            if (initialized && input != null)
            {
                bool authoritative =
                    input.TryReadAuthoritativeCommand(
                        out V3PilotCommand command);
                Tick(command, Time.fixedDeltaTime);
                if (authoritative)
                {
                    input.NotifyCommandSubmitted(
                        Time.timeAsDouble,
                        ActiveControlPath ==
                        V3ControlExecutionPath.MainframeScheduled);
                }
            }
        }

        public void BindInputSource(V3PilotInputAdapter inputSource)
        {
            input = inputSource;
        }

        public void Tick(V3PilotCommand command, float deltaTime)
        {
            if (!initialized)
            {
                ActiveControlPath = V3ControlExecutionPath.None;
                ControlPathReason = "Pipeline is not initialized.";
                return;
            }

            if (suppressNextPhysicsTick)
            {
                suppressNextPhysicsTick = false;
                router.BeginFrame(false);
                router.Resolve(deltaTime);
                mainframe?.TickManagedDevices(deltaTime);
                ActiveControlPath = V3ControlExecutionPath.None;
                ControlPathReason =
                    "First post-reset physics tick is deliberately neutral.";
                return;
            }

            mainframe?.TickObservations(command, deltaTime);
            if (mainframe != null)
            {
                command =
                    mainframe.FilterCommandByInstalledSoftware(command);
                if (mainframe.SubmitScheduledControls(
                    command,
                    deltaTime))
                {
                    ActiveControlPath =
                        V3ControlExecutionPath.MainframeScheduled;
                    ControlPathReason = "Mainframe scheduler controls executed.";
                    return;
                }
            }

            V3CraftRuntime craft = GetComponent<V3CraftRuntime>();
            bool allowLegacy = craft == null || craft.Build == null ||
                craft.Build.AllowLegacyPipelineFallback;
            if (!allowLegacy)
            {
                ActiveControlPath = V3ControlExecutionPath.FaultedNoFallback;
                ControlPathReason = mainframe == null
                    ? "Complete build requires a Mainframe; legacy fallback is disabled."
                    : "Mainframe scheduled control was unavailable; legacy fallback is disabled.";
                router.BeginFrame(false);
                router.Resolve(deltaTime);
                mainframe?.TickManagedDevices(deltaTime);
                return;
            }

            ActiveControlPath = V3ControlExecutionPath.LegacyFallback;
            ControlPathReason = mainframe == null
                ? "Build explicitly permits Mainframe-less legacy control."
                : "Mainframe control unavailable and build explicitly permits fallback.";

            bool driveOperational =
                capabilities.HasOperationalProvider(
                    PartCapability.DriveControl);
            bool hoverOperational =
                capabilities.HasOperationalProvider(
                    PartCapability.HoverControl);
            bool stabilizationOperational =
                capabilities.HasOperationalProvider(
                    PartCapability.Stabilization);
            bool vectoringOperational =
                capabilities.HasOperationalProvider(
                    PartCapability.VectoringControl);

            if (command.CyclePowerAllocationMode && driveOperational)
            {
                router.CyclePowerAllocationMode();
            }

            V3PilotCommand driveCommand = command;
            if (!driveOperational)
            {
                driveCommand.Throttle = 0f;
                driveCommand.EmergencyOverload = false;
            }

            V3PilotCommand hoverCommand = command;
            if (!hoverOperational)
            {
                hoverCommand.Lift = 0f;
                hoverCommand.Downforce = 0f;
                hoverCommand.Pitch = 0f;
                hoverCommand.StabilizationEnabled = false;
            }
            else if (!stabilizationOperational)
            {
                hoverCommand.StabilizationEnabled = false;
            }

            V3PilotCommand vectorCommand = command;
            if (!vectoringOperational)
            {
                vectorCommand.Strafe = 0f;
                vectorCommand.Yaw = 0f;
                vectorCommand.Pitch = 0f;
                vectorCommand.GripBreaker = false;
            }

            router.BeginFrame(
                driveOperational && command.EmergencyOverload);
            gimbal?.Submit(vectorCommand, deltaTime);
            drive?.Submit(driveCommand, deltaTime);
            hover?.Submit(hoverCommand, deltaTime);
            vector?.Submit(vectorCommand, deltaTime);
            traction?.Submit(vectorCommand, deltaTime);
            router.Resolve(deltaTime);
            mainframe?.TickManagedDevices(deltaTime);
        }

        public void BindMainframe(V3CraftMainframe value)
        {
            mainframe = value;
        }

        public void ResetDynamicState()
        {
            input?.ResetDynamicState();
            mainframe?.ResetDynamicState();
            drive?.ResetDynamicState();
            hover?.ResetDynamicState();
            router?.ResetDynamicState();
            traction?.ResetDynamicState();
            gimbal?.ResetDynamicState();
            thermal?.ResetDynamicState();
            suppressNextPhysicsTick = true;
            ActiveControlPath = V3ControlExecutionPath.None;
            ControlPathReason = "Dynamic state reset.";
        }
    }
}
