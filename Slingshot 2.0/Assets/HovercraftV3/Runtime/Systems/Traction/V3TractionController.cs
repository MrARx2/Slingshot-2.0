using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3TractionController : MonoBehaviour
    {
        private static readonly string[] TractionSockets =
        {
            "Strafe.Left.Front",
            "Strafe.Right.Front",
            "Strafe.Left.Rear",
            "Strafe.Right.Rear",
            "Propulsion.Rear.Center",
            "Braking.Front.Center"
        };

        [SerializeField, Min(0f)] private float lateralGrip = 8f;
        [SerializeField, Min(0f)] private float longitudinalGrip = 7f;
        [SerializeField, Min(0f)] private float coastingGrip = 0.3f;
        [SerializeField, Min(0f)] private float lateralGripBreaker = 0.35f;
        [SerializeField, Min(0f)] private float longitudinalGripBreaker = 0.25f;
        [SerializeField, Min(0f)] private float coastingGripBreaker = 0.05f;
        [SerializeField, Min(0f)] private float gripBreakRate = 6f;
        [SerializeField, Min(0f)] private float gripRecoveryRate = 4f;
        [SerializeField, Min(0f)] private float gripRecoverySpeedPenalty = 0.035f;
        [SerializeField, Min(0.01f)] private float gripRecoveryExponent = 1.6f;
        [SerializeField, Min(0f)] private float counterTrajectoryMinSpeed = 0.4f;
        [SerializeField, Min(0f)] private float counterTrajectoryGripMultiplier = 1f;
        [SerializeField, Min(0f)] private float maximumGripAcceleration = 120f;
        [SerializeField, Range(0f, 1f)] private float driftSpeedConservation = 0.2f;

        private V3CraftRuntime runtime;
        private V3HoverController hover;
        private V3ActuatorCommandRouter router;
        private float gripBreakerAmount;

        public float GripBreakerAmount => gripBreakerAmount;
        public V3ActuatorAllocationResult GripAllocation { get; private set; }

        public void Initialize(
            V3CraftRuntime craftRuntime,
            V3HoverController hoverController,
            V3ActuatorCommandRouter commandRouter)
        {
            runtime = craftRuntime;
            hover = hoverController;
            router = commandRouter;
            ResetDynamicState();
        }

        public void ResetDynamicState()
        {
            gripBreakerAmount = 0f;
            GripAllocation = default;
        }

        public void Submit(V3PilotCommand command, float deltaTime)
        {
            if (runtime == null ||
                runtime.RootRigidbody == null ||
                hover == null ||
                router == null ||
                !hover.IsGrounded)
            {
                GripAllocation = default;
                return;
            }

            Rigidbody body = runtime.RootRigidbody;
            float speed = body.linearVelocity.magnitude;
            if (command.GripBreaker)
            {
                gripBreakerAmount = Mathf.MoveTowards(
                    gripBreakerAmount,
                    1f,
                    gripBreakRate * Mathf.Max(0f, deltaTime));
            }
            else
            {
                float speedPenalty = speed * gripRecoverySpeedPenalty;
                float divisor =
                    1f + Mathf.Pow(speedPenalty, gripRecoveryExponent);
                gripBreakerAmount = Mathf.MoveTowards(
                    gripBreakerAmount,
                    0f,
                    gripRecoveryRate / divisor * Mathf.Max(0f, deltaTime));
            }

            float blend = Smooth01(gripBreakerAmount);
            float activeLateralGrip =
                Mathf.Lerp(lateralGrip, lateralGripBreaker, blend);
            float activeLongitudinalGrip =
                Mathf.Lerp(longitudinalGrip, longitudinalGripBreaker, blend);
            float activeCoastingGrip =
                Mathf.Lerp(coastingGrip, coastingGripBreaker, blend);
            Vector3 localVelocity =
                runtime.transform.InverseTransformDirection(body.linearVelocity);
            Vector3 localAcceleration = CalculateLocalGripAcceleration(
                localVelocity,
                command.Throttle,
                command.GripBreaker,
                gripBreakerAmount,
                activeLateralGrip,
                activeLongitudinalGrip,
                activeCoastingGrip,
                counterTrajectoryMinSpeed,
                counterTrajectoryGripMultiplier,
                maximumGripAcceleration,
                driftSpeedConservation);
            Vector3 requestedForce =
                runtime.transform.TransformDirection(localAcceleration) *
                body.mass;
            GripAllocation = router.SubmitWrench(
                TractionSockets,
                V3ActuatorChannel.Vectoring,
                requestedForce,
                Vector3.zero,
                V3ActuatorPriority.Vectoring,
                1f,
                1f);
        }

        public static Vector3 CalculateLocalGripAcceleration(
            Vector3 localVelocity,
            float signedThrottle,
            bool gripBreakerHeld,
            float currentGripBreakerAmount,
            float activeLateralGrip,
            float activeLongitudinalGrip,
            float activeCoastingGrip,
            float minimumCounterTrajectorySpeed,
            float counterTrajectoryMultiplier,
            float maximumAcceleration,
            float conservedDriftFraction)
        {
            float lateralAcceleration =
                -localVelocity.x * Mathf.Max(0f, activeLateralGrip);
            float longitudinalAcceleration = 0f;
            if (Mathf.Abs(signedThrottle) <= 0.05f)
            {
                longitudinalAcceleration =
                    -localVelocity.z * Mathf.Max(0f, activeCoastingGrip);
            }
            else
            {
                bool movingForward =
                    localVelocity.z > minimumCounterTrajectorySpeed;
                bool movingBackward =
                    localVelocity.z < -minimumCounterTrajectorySpeed;
                bool thrustingForward = signedThrottle > 0.05f;
                bool thrustingBackward = signedThrottle < -0.05f;
                if ((thrustingForward && movingBackward) ||
                    (thrustingBackward && movingForward))
                {
                    longitudinalAcceleration +=
                        -localVelocity.z *
                        Mathf.Max(0f, activeLongitudinalGrip) *
                        Mathf.Max(0f, counterTrajectoryMultiplier);
                }
            }

            if (!gripBreakerHeld &&
                currentGripBreakerAmount > 0.01f &&
                Mathf.Abs(localVelocity.x) > 0.05f)
            {
                float lateralDeceleration =
                    Mathf.Abs(localVelocity.x) *
                    Mathf.Max(0f, activeLateralGrip);
                float direction = localVelocity.z >= 0f ? 1f : -1f;
                longitudinalAcceleration +=
                    direction *
                    lateralDeceleration *
                    Mathf.Clamp01(conservedDriftFraction);
            }

            if (maximumAcceleration > 0f)
            {
                lateralAcceleration = Mathf.Clamp(
                    lateralAcceleration,
                    -maximumAcceleration,
                    maximumAcceleration);
                longitudinalAcceleration = Mathf.Clamp(
                    longitudinalAcceleration,
                    -maximumAcceleration,
                    maximumAcceleration);
            }

            return new Vector3(
                lateralAcceleration,
                0f,
                longitudinalAcceleration);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
