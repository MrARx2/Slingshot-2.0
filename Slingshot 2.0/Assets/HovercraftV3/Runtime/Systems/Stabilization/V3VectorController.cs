using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3VectorController : MonoBehaviour
    {
        private static readonly string[] StrafeSockets =
        {
            "Strafe.Left.Front",
            "Strafe.Right.Front",
            "Strafe.Left.Rear",
            "Strafe.Right.Rear"
        };

        // The physical-equivalent lateral thrusters are rated for the legacy
        // yaw impulse. A 0.19 lateral request preserves the original 152 kN
        // per-thruster full-strafe force while leaving full hardware authority
        // available to steering and damping.
        [SerializeField, Min(0f)] private float strafeSensitivity = 0.19f;
        [SerializeField, Min(0f)] private float steeringSensitivity = 0.2975f;
        [SerializeField, Min(0f)] private float steeringYawAuthority = 2.8f;
        [SerializeField, Min(0f)] private float yawDamping = 5.35f;

        private V3CraftRuntime runtime;
        private V3ActuatorCommandRouter router;

        public V3ActuatorAllocationResult YawDampingAllocation
        {
            get;
            private set;
        }

        public void Initialize(
            V3CraftRuntime craftRuntime,
            V3ActuatorCommandRouter commandRouter)
        {
            runtime = craftRuntime;
            router = commandRouter;
        }

        public void Submit(V3PilotCommand command, float deltaTime)
        {
            if (runtime == null || runtime.RootRigidbody == null || router == null)
            {
                return;
            }

            float localYawRate =
                runtime.transform.InverseTransformDirection(
                    runtime.RootRigidbody.angularVelocity).y;
            float strafe =
                CalculateStrafeDemand(command.Strafe, strafeSensitivity);
            float yaw = CalculateYawDemand(
                command.Yaw,
                steeringSensitivity,
                steeringYawAuthority);

            SubmitPositive("Strafe.Left.Front", strafe + yaw);
            SubmitPositive("Strafe.Right.Front", -strafe - yaw);
            SubmitPositive("Strafe.Left.Rear", strafe - yaw);
            SubmitPositive("Strafe.Right.Rear", -strafe + yaw);

            Vector3 desiredAngularAcceleration =
                runtime.transform.up *
                CalculateYawDampingAcceleration(localYawRate, yawDamping);
            Vector3 desiredTorque =
                V3ActuatorCommandRouter.CalculateRequiredWorldTorque(
                    runtime.RootRigidbody,
                    desiredAngularAcceleration);
            YawDampingAllocation = router.SubmitWrench(
                StrafeSockets,
                V3ActuatorChannel.Stabilization,
                Vector3.zero,
                desiredTorque,
                V3ActuatorPriority.Stabilization,
                0.75f,
                1f);
        }

        public static float CalculateYawDemand(
            float yawInput,
            float sensitivity,
            float authority)
        {
            return Mathf.Clamp(
                yawInput * Mathf.Max(0f, sensitivity) * Mathf.Max(0f, authority),
                -1f,
                1f);
        }

        public static float CalculateStrafeDemand(
            float strafeInput,
            float sensitivity)
        {
            return Mathf.Clamp(
                strafeInput * Mathf.Max(0f, sensitivity),
                -1f,
                1f);
        }

        public static float CalculateYawDampingAcceleration(
            float localYawRate,
            float damping)
        {
            return -localYawRate * Mathf.Max(0f, damping);
        }

        private void SubmitPositive(string socketId, float value)
        {
            router.Submit(
                socketId,
                V3ActuatorChannel.Vectoring,
                Mathf.Clamp01(value),
                V3ActuatorPriority.Vectoring);
        }
    }
}
