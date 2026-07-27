using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3DriveController : MonoBehaviour
    {
        private const string MainThruster = "Propulsion.Rear.Center";
        private const string BrakeThruster = "Braking.Front.Center";

        [SerializeField, Min(0f)] private float throttleRisePerSecond = 4.5f;
        [SerializeField, Min(0f)] private float throttleFallPerSecond = 5f;
        [SerializeField, Min(0f)] private float installedMainForceMultiplier = 1.5f;
        // The physical brake is rated for its 380 kN drive request plus the
        // traction allocator's 1.32 MN counter-trajectory request. Keeping the
        // drive contribution normalized preserves the authored 380 kN reverse
        // slope after the craft changes direction.
        [SerializeField, Min(0f)] private float installedBrakeForceMultiplier =
            0.22352941f;
        [SerializeField, Min(1f)] private float emergencyOverloadMultiplier = 1.25f;

        private V3ActuatorCommandRouter router;
        private float shapedThrottle;

        public float ShapedThrottle => shapedThrottle;

        public void Initialize(V3ActuatorCommandRouter value)
        {
            router = value;
            ResetDynamicState();
        }

        public void ResetDynamicState()
        {
            shapedThrottle = 0f;
        }

        public void Submit(V3PilotCommand command, float deltaTime)
        {
            if (router == null)
            {
                return;
            }

            float target = Mathf.Clamp(command.Throttle, -1f, 1f);
            float rate = Mathf.Abs(target) > Mathf.Abs(shapedThrottle)
                ? throttleRisePerSecond
                : throttleFallPerSecond;
            shapedThrottle = Mathf.MoveTowards(
                shapedThrottle,
                target,
                rate * Mathf.Max(0f, deltaTime));

            router.Submit(
                MainThruster,
                V3ActuatorChannel.Drive,
                Mathf.Max(0f, shapedThrottle) *
                installedMainForceMultiplier *
                (command.EmergencyOverload
                    ? emergencyOverloadMultiplier
                    : 1f),
                V3ActuatorPriority.Drive);
            router.Submit(
                BrakeThruster,
                V3ActuatorChannel.Drive,
                Mathf.Max(0f, -shapedThrottle) *
                installedBrakeForceMultiplier,
                V3ActuatorPriority.Drive);
        }
    }
}
