using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
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
        private bool initialized;

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
                Tick(input.ReadCommand(), Time.fixedDeltaTime);
            }
        }

        public void Tick(V3PilotCommand command, float deltaTime)
        {
            if (!initialized)
            {
                return;
            }

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
        }

        public void ResetDynamicState()
        {
            drive?.ResetDynamicState();
            router?.ResetDynamicState();
            traction?.ResetDynamicState();
            gimbal?.ResetDynamicState();
            thermal?.ResetDynamicState();
        }
    }
}
