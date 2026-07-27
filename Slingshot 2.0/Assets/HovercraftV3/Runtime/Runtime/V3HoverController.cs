using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3HoverController : MonoBehaviour
    {
        private static readonly string[] BottomSockets =
        {
            "Hover.Front.Left.Bottom",
            "Hover.Front.Right.Bottom",
            "Hover.Rear.Left.Bottom",
            "Hover.Rear.Right.Bottom"
        };

        private static readonly string[] RoofSockets =
        {
            "Control.Front.Left.Top",
            "Control.Front.Right.Top",
            "Control.Rear.Left.Top",
            "Control.Rear.Right.Top"
        };

        private static readonly string[] StabilizationSockets =
        {
            "Hover.Front.Left.Bottom",
            "Hover.Front.Right.Bottom",
            "Hover.Rear.Left.Bottom",
            "Hover.Rear.Right.Bottom",
            "Control.Front.Left.Top",
            "Control.Front.Right.Top",
            "Control.Rear.Left.Top",
            "Control.Rear.Right.Top"
        };

        // Measured from the external hover-thruster force origin. The V2
        // reference chassis target remains unchanged when alternate mounting
        // hardware moves that origin relative to the hull.
        [SerializeField, Min(0f)] private float targetHeight = 3.075f;
        [SerializeField, Min(0f)] private float probeRadius = 0.1f;
        [SerializeField, Min(0f)] private float probeRange = 7f;
        [SerializeField, Min(0f)] private float fallbackProbeRadius = 0.35f;
        [SerializeField, Min(0f)] private float heightGain = 0.45f;
        [SerializeField, Min(0f)] private float verticalDamping = 0.12f;
        [SerializeField, Min(0f)] private float angularDamping = 0.08f;
        [SerializeField, Min(0f)] private float pitchSensitivity = 0.589f;
        [SerializeField, Min(0f)] private float surfaceAlignStrength = 60f;
        [SerializeField, Min(0f)] private float surfaceAlignDamping = 4f;
        [SerializeField, Min(0f)] private float maxSurfaceAlignAcceleration = 60f;
        [SerializeField, Min(0f)] private float stiffenStartSpeed = 80f;
        [SerializeField, Min(0f)] private float stiffenFullSpeed = 600f;
        [SerializeField, Min(1f)] private float maxSpeedStiffness = 3.5f;
        [SerializeField] private LayerMask trackSurfaceMask = 1 << 8;

        private V3CraftRuntime runtime;
        private V3ActuatorCommandRouter router;
        private Vector3 lastGroundNormal = Vector3.up;

        public bool IsGrounded { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public int GroundedProbeCount { get; private set; }
        public Vector3 SurfaceAlignmentAcceleration { get; private set; }
        public V3ActuatorAllocationResult SurfaceAlignmentAllocation
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
            lastGroundNormal = craftRuntime != null
                ? craftRuntime.transform.up
                : Vector3.up;
        }

        public void Submit(V3PilotCommand command, float deltaTime)
        {
            if (runtime == null || runtime.RootRigidbody == null || router == null)
            {
                return;
            }

            Rigidbody body = runtime.RootRigidbody;
            Vector3 localAngularVelocity =
                runtime.transform.InverseTransformDirection(body.angularVelocity);
            Vector3 groundNormalSum = Vector3.zero;
            GroundedProbeCount = 0;
            IsGrounded = false;
            GroundNormal = runtime.transform.up;
            SurfaceAlignmentAcceleration = Vector3.zero;
            SurfaceAlignmentAllocation = default;
            bool hasFallbackSurface = TryProbeFallbackSurface(
                body,
                out Vector3 fallbackPoint,
                out Vector3 fallbackNormal);

            for (int i = 0; i < BottomSockets.Length; i++)
            {
                RuntimeThrusterInstance thruster = router.FindThruster(BottomSockets[i]);
                if (thruster == null)
                {
                    continue;
                }

                float output = command.Lift;
                Vector3 thrustDirection = thruster.transform.TransformDirection(
                    thruster.ThrusterDefinition.LocalThrustDirection);
                Vector3 probeDirection = -thrustDirection;
                Vector3 origin = thruster.transform.TransformPoint(
                    thruster.ThrusterDefinition.LocalForceOrigin);
                bool hasGround = false;
                float groundDistance = 0f;
                Vector3 groundNormal = Vector3.zero;
                if (Physics.SphereCast(
                    origin,
                    probeRadius,
                    probeDirection,
                    out RaycastHit hit,
                    probeRange,
                    trackSurfaceMask,
                    QueryTriggerInteraction.Ignore))
                {
                    hasGround = true;
                    groundDistance = hit.distance;
                    groundNormal = hit.normal;
                }
                else if (hasFallbackSurface &&
                    TryCalculateSurfacePlaneDistance(
                        origin,
                        probeDirection,
                        fallbackPoint,
                        fallbackNormal,
                        probeRange,
                        out float fallbackDistance))
                {
                    hasGround = true;
                    groundDistance = fallbackDistance;
                    groundNormal = fallbackNormal;
                }

                if (hasGround)
                {
                    groundNormalSum += groundNormal;
                    GroundedProbeCount++;
                    float weightShare =
                        body.mass * Physics.gravity.magnitude /
                        Mathf.Max(
                            1f,
                            BottomSockets.Length *
                            thruster.ThrusterDefinition.MaximumForwardForceN);
                    float surfaceNormalVelocity =
                        CalculateSurfaceNormalVelocity(
                            body.GetPointVelocity(origin),
                            groundNormal);
                    float heightCorrection =
                        (targetHeight - groundDistance) * heightGain -
                        surfaceNormalVelocity * verticalDamping;
                    output = Mathf.Max(output, weightShare + heightCorrection);

                    if (command.StabilizationEnabled)
                    {
                        bool left = BottomSockets[i].Contains(".Left.");
                        bool front = BottomSockets[i].Contains(".Front.");
                        float stabilization =
                            (left ? localAngularVelocity.z : -localAngularVelocity.z) *
                            angularDamping +
                            (front ? -localAngularVelocity.x : localAngularVelocity.x) *
                            angularDamping;
                        router.Submit(
                            BottomSockets[i],
                            V3ActuatorChannel.Stabilization,
                            stabilization,
                            V3ActuatorPriority.Stabilization);
                    }
                }

                router.Submit(
                    BottomSockets[i],
                    V3ActuatorChannel.BaseHover,
                    Mathf.Clamp01(output),
                    V3ActuatorPriority.Critical);
            }

            IsGrounded = GroundedProbeCount > 0;
            if (IsGrounded)
            {
                GroundNormal = groundNormalSum.normalized;
                lastGroundNormal = GroundNormal;
            }

            for (int i = 0; i < RoofSockets.Length; i++)
            {
                float pitch = command.Pitch * pitchSensitivity;
                float pitchContribution =
                    RoofSockets[i].Contains(".Front.")
                        ? Mathf.Max(0f, pitch)
                        : Mathf.Max(0f, -pitch);
                router.Submit(
                    RoofSockets[i],
                    V3ActuatorChannel.Manual,
                    Mathf.Clamp01(command.Downforce + pitchContribution),
                    V3ActuatorPriority.Stabilization);
            }

            if (IsGrounded && command.StabilizationEnabled)
            {
                float speed = body.linearVelocity.magnitude;
                float stiffness = Mathf.Lerp(
                    1f,
                    maxSpeedStiffness,
                    Mathf.InverseLerp(
                        stiffenStartSpeed,
                        Mathf.Max(stiffenStartSpeed + 1f, stiffenFullSpeed),
                        speed));
                SurfaceAlignmentAcceleration =
                    CalculateSurfaceAlignmentAcceleration(
                        runtime.transform.up,
                        GroundNormal,
                        body.angularVelocity,
                        surfaceAlignStrength,
                        surfaceAlignDamping,
                        maxSurfaceAlignAcceleration,
                        stiffness);
                Vector3 requestedTorque =
                    V3ActuatorCommandRouter.CalculateRequiredWorldTorque(
                        body,
                        SurfaceAlignmentAcceleration);
                SurfaceAlignmentAllocation = router.SubmitWrench(
                    StabilizationSockets,
                    V3ActuatorChannel.Stabilization,
                    Vector3.zero,
                    requestedTorque,
                    V3ActuatorPriority.Stabilization,
                    0.6f,
                    1f);
            }
        }

        private bool TryProbeFallbackSurface(
            Rigidbody body,
            out Vector3 point,
            out Vector3 normal)
        {
            Vector3 origin =
                body.worldCenterOfMass + runtime.transform.up * 0.05f;
            float range = probeRange + targetHeight;
            Vector3 primaryDirection = -runtime.transform.up;
            if (Physics.SphereCast(
                origin,
                fallbackProbeRadius,
                primaryDirection,
                out RaycastHit hit,
                range,
                trackSurfaceMask,
                QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            Vector3 rememberedDirection = lastGroundNormal.sqrMagnitude > 0.000001f
                ? -lastGroundNormal.normalized
                : Vector3.down;
            if (Vector3.Dot(primaryDirection, rememberedDirection) < 0.999f &&
                Physics.SphereCast(
                    origin,
                    fallbackProbeRadius,
                    rememberedDirection,
                    out hit,
                    range,
                    trackSurfaceMask,
                    QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            point = Vector3.zero;
            normal = Vector3.up;
            return false;
        }

        public static bool TryCalculateSurfacePlaneDistance(
            Vector3 origin,
            Vector3 direction,
            Vector3 planePoint,
            Vector3 planeNormal,
            float maximumDistance,
            out float distance)
        {
            distance = 0f;
            if (direction.sqrMagnitude <= 0.000001f ||
                planeNormal.sqrMagnitude <= 0.000001f ||
                maximumDistance < 0f)
            {
                return false;
            }

            var ray = new Ray(origin, direction.normalized);
            var plane = new Plane(planeNormal.normalized, planePoint);
            if (!plane.Raycast(ray, out float enter) ||
                enter < 0f ||
                enter > maximumDistance)
            {
                return false;
            }

            distance = enter;
            return true;
        }

        public static float CalculateSurfaceNormalVelocity(
            Vector3 pointVelocity,
            Vector3 surfaceNormal)
        {
            return surfaceNormal.sqrMagnitude > 0.000001f
                ? Vector3.Dot(pointVelocity, surfaceNormal.normalized)
                : 0f;
        }

        public static Vector3 CalculateSurfaceAlignmentAcceleration(
            Vector3 currentUp,
            Vector3 targetNormal,
            Vector3 angularVelocity,
            float strength,
            float damping,
            float maximum,
            float stiffness)
        {
            currentUp = currentUp.sqrMagnitude > 0.000001f
                ? currentUp.normalized
                : Vector3.up;
            targetNormal = targetNormal.sqrMagnitude > 0.000001f
                ? targetNormal.normalized
                : currentUp;
            stiffness = Mathf.Max(0f, stiffness);

            Vector3 cross = Vector3.Cross(currentUp, targetNormal);
            float crossMagnitude = cross.magnitude;
            float angleError = Mathf.Atan2(
                crossMagnitude,
                Vector3.Dot(currentUp, targetNormal));
            Vector3 acceleration = crossMagnitude > 0.000001f
                ? cross / crossMagnitude * (angleError * Mathf.Max(0f, strength) * stiffness)
                : Vector3.zero;

            Vector3 rollPitchAngularVelocity =
                angularVelocity -
                currentUp * Vector3.Dot(angularVelocity, currentUp);
            acceleration -= rollPitchAngularVelocity * Mathf.Max(0f, damping);
            acceleration -=
                currentUp * Vector3.Dot(acceleration, currentUp);
            return Vector3.ClampMagnitude(
                acceleration,
                Mathf.Max(0f, maximum) * stiffness);
        }
    }
}
