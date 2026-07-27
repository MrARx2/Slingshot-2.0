using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3GimbalController : MonoBehaviour
    {
        private readonly List<RuntimeGimbalInstance> gimbals =
            new List<RuntimeGimbalInstance>(2);
        [SerializeField, Min(0f)] private float transientInputHoldSeconds = 0.2f;
        [SerializeField, Min(0f)] private float returnInputPerSecond = 4f;

        private V3PowerDistributor power;
        private float heldPitchInput;
        private float heldYawInput;
        private float pitchHoldRemaining;
        private float yawHoldRemaining;

        public IReadOnlyList<RuntimeGimbalInstance> Gimbals => gimbals;
        public float HeldPitchInput => heldPitchInput;
        public float HeldYawInput => heldYawInput;

        public void Initialize(
            V3CraftRuntime runtime,
            V3PowerDistributor powerDistributor)
        {
            power = powerDistributor;
            gimbals.Clear();
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                if (runtime.InstalledParts[i] is RuntimeGimbalInstance gimbal)
                {
                    gimbals.Add(gimbal);
                }
            }
        }

        public void Submit(V3PilotCommand command, float deltaTime)
        {
            heldPitchInput = UpdateTransientInput(
                command.Pitch,
                heldPitchInput,
                ref pitchHoldRemaining,
                transientInputHoldSeconds,
                returnInputPerSecond,
                deltaTime);
            heldYawInput = UpdateTransientInput(
                command.Yaw,
                heldYawInput,
                ref yawHoldRemaining,
                transientInputHoldSeconds,
                returnInputPerSecond,
                deltaTime);
            for (int i = 0; i < gimbals.Count; i++)
            {
                gimbals[i].PreparePowerRequest(
                    heldPitchInput,
                    heldYawInput,
                    deltaTime);
            }

            power?.ResolveSystemsPower();
            for (int i = 0; i < gimbals.Count; i++)
            {
                gimbals[i].ApplyPowerGrant(
                    gimbals[i].GrantedPower,
                    deltaTime);
            }
        }

        public void ResetDynamicState()
        {
            heldPitchInput = 0f;
            heldYawInput = 0f;
            pitchHoldRemaining = 0f;
            yawHoldRemaining = 0f;
            for (int i = 0; i < gimbals.Count; i++)
            {
                gimbals[i].ResetActuatorState();
            }

            power?.ResolveSystemsPower();
        }

        public static float UpdateTransientInput(
            float rawInput,
            float heldInput,
            ref float holdRemaining,
            float holdSeconds,
            float returnPerSecond,
            float deltaTime)
        {
            float step = Mathf.Max(0f, deltaTime);
            if (Mathf.Abs(rawInput) > 0.001f)
            {
                holdRemaining = Mathf.Max(0f, holdSeconds);
                return Mathf.Clamp(rawInput, -1f, 1f);
            }

            if (holdRemaining > 0f)
            {
                holdRemaining = Mathf.Max(0f, holdRemaining - step);
                return heldInput;
            }

            return Mathf.MoveTowards(
                heldInput,
                0f,
                Mathf.Max(0f, returnPerSecond) * step);
        }
    }
}
