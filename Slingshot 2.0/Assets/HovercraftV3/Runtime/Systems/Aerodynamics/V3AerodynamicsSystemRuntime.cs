using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3AerodynamicsSystemRuntime :
        V3CraftSystemRuntimeBase
    {
        private readonly List<RuntimeRotaryActuatorInstance> actuators =
            new List<RuntimeRotaryActuatorInstance>(4);
        private float speedAuthority;
        private float yaw;

        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.Aerodynamics;

        public override void Initialize(V3SystemRuntimeContext context)
        {
            base.Initialize(context);
            actuators.Clear();
            if (context.Craft == null)
            {
                return;
            }

            for (int i = 0;
                 i < context.Craft.InstalledParts.Count;
                 i++)
            {
                if (context.Craft.InstalledParts[i] is
                    RuntimeRotaryActuatorInstance actuator)
                {
                    actuators.Add(actuator);
                }
            }
        }

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            Rigidbody body =
                Context.Craft != null
                    ? Context.Craft.RootRigidbody
                    : null;
            speedAuthority = body != null
                ? Mathf.InverseLerp(
                    20f,
                    120f,
                    body.linearVelocity.magnitude)
                : 0f;
            yaw = Context.ScheduledCommand.Yaw;
            OutputPublished = actuators.Count > 0;
        }

        public void PrepareActuators(
            bool firmwareAvailable,
            float deltaTime)
        {
            for (int i = 0; i < actuators.Count; i++)
            {
                RuntimeRotaryActuatorInstance actuator =
                    actuators[i];
                float side =
                    actuator.ParentSocketId.Contains(".Left")
                        ? -1f
                        : 1f;
                float target = firmwareAvailable &&
                    State == V3SystemRuntimeState.Operational
                        ? -6f * speedAuthority +
                          yaw * side * 10f
                        : 0f;
                actuator.PreparePowerRequest(target, deltaTime);
            }
        }

        protected override void OnDynamicStateReset()
        {
            speedAuthority = 0f;
            yaw = 0f;
            for (int i = 0; i < actuators.Count; i++)
            {
                actuators[i].ResetDynamicState();
            }
        }
    }
}
