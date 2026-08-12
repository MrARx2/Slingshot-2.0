using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3SurfaceState
    {
        NoSurface,
        SurfaceDetected,
        NearSurfaceHover,
        SurfaceCaptured,
        SurfaceSeparating,
        CaptureLimited,
        ProbeLost,
        FreeFlight
    }

    public readonly struct V3HoverProbeSnapshot
    {
        public V3HoverProbeSnapshot(
            string socketId,
            Vector3 origin,
            Vector3 direction,
            bool hit,
            float distance,
            Vector3 normal,
            bool usedFallback)
        {
            SocketId = socketId;
            Origin = origin;
            Direction = direction;
            Hit = hit;
            Distance = distance;
            Normal = normal;
            UsedFallback = usedFallback;
        }

        public string SocketId { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public bool Hit { get; }
        public float Distance { get; }
        public Vector3 Normal { get; }
        public bool UsedFallback { get; }
    }

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

        private V3CraftRuntime runtime;
        private V3ActuatorCommandRouter router;
        private V3WorldEnvironmentProvider environment;
        private V3HoverConfiguration configuration;
        private Vector3 lastGroundNormal = Vector3.up;
        private Vector3 previousTrackedGroundNormal = Vector3.up;
        private bool hasSurfaceNormalHistory;
        private bool surfaceDetectedLastTick;
        private float surfaceAgeSeconds = float.PositiveInfinity;
        private readonly V3HoverProbeSnapshot[] probeSnapshots =
            new V3HoverProbeSnapshot[BottomSockets.Length];
        private readonly RuntimeThrusterInstance[] bottomThrusters =
            new RuntimeThrusterInstance[BottomSockets.Length];
        private readonly Vector3[] probeOrigins =
            new Vector3[BottomSockets.Length];
        private readonly Vector3[] probeNormals =
            new Vector3[BottomSockets.Length];
        private readonly float[] probeDistances =
            new float[BottomSockets.Length];
        private readonly float[] probeNormalVelocities =
            new float[BottomSockets.Length];
        private readonly bool[] probeHits =
            new bool[BottomSockets.Length];

        public bool IsGrounded { get; private set; }
        public bool SurfaceDetected { get; private set; }
        public bool IsNearSurface { get; private set; }
        public V3SurfaceState SurfaceState { get; private set; } =
            V3SurfaceState.NoSurface;
        public int DetectedProbeCount { get; private set; }
        public int PrimaryProbeCount { get; private set; }
        public int FallbackProbeCount { get; private set; }
        public float SurfaceConfidence { get; private set; }
        public float SurfaceAgeSeconds => surfaceAgeSeconds;
        public float CaptureAuthorityMultiplier { get; private set; }
        public float DistanceAuthorityMultiplier { get; private set; }
        public string CaptureLimitReason { get; private set; } = "No surface";
        public float BottomRequestBeforeAuthorityLimit { get; private set; }
        public float BottomRequestAfterAuthorityLimit { get; private set; }
        public float RoofRequestBeforeAuthorityLimit { get; private set; }
        public float RoofRequestAfterAuthorityLimit { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public int GroundedProbeCount { get; private set; }
        public Vector3 SurfaceAlignmentAcceleration { get; private set; }
        public float AverageSurfaceDistance { get; private set; }
        public float AverageSurfaceNormalVelocity { get; private set; }
        public float SurfaceTrackingAcceleration { get; private set; }
        public float RoofCaptureAcceleration { get; private set; }
        public float RoofCaptureOutput { get; private set; }
        public V3HoverConfiguration Configuration => configuration;
        public float TargetHeight => configuration != null
            ? configuration.TargetHoverHeight : 0f;
        public float MinimumClearance => configuration != null
            ? configuration.MinimumClearance : 0f;
        public float ProbeRadius => configuration != null
            ? configuration.ProbeRadius : 0f;
        public float ProbeRange => configuration != null
            ? configuration.MaximumOperationalRange : 0f;
        public bool GravityCompensationEnabled => configuration != null &&
            configuration.GravityCompensationEnabled;
        public float GravityCompensationMultiplier => configuration != null
            ? configuration.GravityCompensationMultiplier : 0f;
        public LayerMask TrackSurfaceMask => configuration != null
            ? configuration.TrackSurfaceMask : default;
        public System.Collections.Generic.IReadOnlyList<V3HoverProbeSnapshot>
            ProbeSnapshots => probeSnapshots;
        public V3ActuatorAllocationResult SurfaceAlignmentAllocation
        {
            get;
            private set;
        }

        public void Initialize(
            V3CraftRuntime craftRuntime,
            V3ActuatorCommandRouter commandRouter)
        {
            Initialize(
                craftRuntime,
                commandRouter,
                craftRuntime != null && craftRuntime.Build != null
                    ? craftRuntime.Build.HoverConfiguration
                    : new V3HoverConfiguration());
        }

        public void Initialize(
            V3CraftRuntime craftRuntime,
            V3ActuatorCommandRouter commandRouter,
            V3HoverConfiguration hoverConfiguration)
        {
            runtime = craftRuntime;
            router = commandRouter;
            configuration = hoverConfiguration ??
                new V3HoverConfiguration();
            environment = craftRuntime != null
                ? craftRuntime.GetComponent<V3WorldEnvironmentProvider>()
                : null;
            lastGroundNormal = craftRuntime != null
                ? craftRuntime.transform.up
                : Vector3.up;
            ResetDynamicState();
        }

        public void ResetDynamicState()
        {
            lastGroundNormal = runtime != null
                ? runtime.transform.up
                : Vector3.up;
            previousTrackedGroundNormal = runtime != null
                ? runtime.transform.up
                : Vector3.up;
            hasSurfaceNormalHistory = false;
            AverageSurfaceDistance = 0f;
            AverageSurfaceNormalVelocity = 0f;
            SurfaceTrackingAcceleration = 0f;
            RoofCaptureAcceleration = 0f;
            RoofCaptureOutput = 0f;
            IsGrounded = false;
            SurfaceDetected = false;
            IsNearSurface = false;
            SurfaceState = V3SurfaceState.NoSurface;
            DetectedProbeCount = 0;
            GroundedProbeCount = 0;
            PrimaryProbeCount = 0;
            FallbackProbeCount = 0;
            SurfaceConfidence = 0f;
            surfaceAgeSeconds = float.PositiveInfinity;
            surfaceDetectedLastTick = false;
            CaptureAuthorityMultiplier = 0f;
            DistanceAuthorityMultiplier = 0f;
            CaptureLimitReason = "Dynamic state reset";
            BottomRequestBeforeAuthorityLimit = 0f;
            BottomRequestAfterAuthorityLimit = 0f;
            RoofRequestBeforeAuthorityLimit = 0f;
            RoofRequestAfterAuthorityLimit = 0f;
            for (int i = 0; i < probeSnapshots.Length; i++)
            {
                probeSnapshots[i] = default;
                probeHits[i] = false;
                probeDistances[i] = 0f;
                probeNormals[i] = Vector3.zero;
                probeNormalVelocities[i] = 0f;
            }
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
            GroundedProbeCount = 0;
            DetectedProbeCount = 0;
            PrimaryProbeCount = 0;
            FallbackProbeCount = 0;
            IsGrounded = false;
            SurfaceDetected = false;
            IsNearSurface = false;
            GroundNormal = runtime.transform.up;
            SurfaceAlignmentAcceleration = Vector3.zero;
            SurfaceAlignmentAllocation = default;
            AverageSurfaceDistance = 0f;
            AverageSurfaceNormalVelocity = 0f;
            RoofCaptureAcceleration = 0f;
            RoofCaptureOutput = 0f;
            BottomRequestBeforeAuthorityLimit = 0f;
            BottomRequestAfterAuthorityLimit = 0f;
            RoofRequestBeforeAuthorityLimit = 0f;
            RoofRequestAfterAuthorityLimit = 0f;
            bool hasFallbackSurface = TryProbeFallbackSurface(
                body,
                out Vector3 fallbackPoint,
                out Vector3 fallbackNormal);

            for (int i = 0; i < BottomSockets.Length; i++)
            {
                RuntimeThrusterInstance thruster = router.FindThruster(BottomSockets[i]);
                bottomThrusters[i] = thruster;
                probeHits[i] = false;
                probeDistances[i] = 0f;
                probeNormals[i] = Vector3.zero;
                probeNormalVelocities[i] = 0f;
                if (thruster == null)
                {
                    probeSnapshots[i] = new V3HoverProbeSnapshot(
                        BottomSockets[i], Vector3.zero, Vector3.down,
                        false, 0f, Vector3.zero, false);
                    continue;
                }

                Vector3 thrustDirection = thruster.transform.TransformDirection(
                    thruster.ThrusterDefinition.LocalThrustDirection);
                Vector3 probeDirection = -thrustDirection;
                Vector3 origin = thruster.transform.TransformPoint(
                    thruster.ThrusterDefinition.LocalForceOrigin);
                probeOrigins[i] = origin;
                bool hasGround = false;
                float groundDistance = 0f;
                Vector3 groundNormal = Vector3.zero;
                bool usedFallback = false;
                if (Physics.SphereCast(
                    origin,
                    ProbeRadius,
                    probeDirection,
                    out RaycastHit hit,
                    ProbeRange,
                    TrackSurfaceMask,
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
                        ProbeRange,
                        out float fallbackDistance))
                {
                    hasGround = true;
                    groundDistance = fallbackDistance;
                    groundNormal = fallbackNormal;
                    usedFallback = true;
                }

                probeSnapshots[i] = new V3HoverProbeSnapshot(
                    BottomSockets[i], origin, probeDirection,
                    hasGround, groundDistance, groundNormal, usedFallback);

                if (hasGround)
                {
                    probeHits[i] = true;
                    probeDistances[i] = groundDistance;
                    probeNormals[i] = groundNormal;
                    probeNormalVelocities[i] =
                        CalculateSurfaceNormalVelocity(
                            body.GetPointVelocity(origin),
                            groundNormal);
                    DetectedProbeCount++;
                    if (usedFallback) FallbackProbeCount++;
                    else PrimaryProbeCount++;
                }
            }

            AccumulateConsistentSurface(
                out Vector3 groundNormalSum,
                out int consistentPrimaryCount);
            PrimaryProbeCount = consistentPrimaryCount;
            SurfaceDetected = GroundedProbeCount > 0;
            if (SurfaceDetected)
            {
                GroundNormal = groundNormalSum.normalized;
                AverageSurfaceDistance /= GroundedProbeCount;
                AverageSurfaceNormalVelocity /= GroundedProbeCount;
                surfaceAgeSeconds = 0f;
                IsNearSurface = AverageSurfaceDistance <=
                    configuration.NearHoverRange;
                DistanceAuthorityMultiplier =
                    configuration.EvaluateCaptureAuthority(
                        AverageSurfaceDistance);
                SurfaceConfidence =
                    configuration.EvaluateProbeConsensusAuthority(
                        GroundedProbeCount,
                        PrimaryProbeCount);
                CaptureAuthorityMultiplier =
                    DistanceAuthorityMultiplier * SurfaceConfidence;
                CaptureLimitReason = DetermineCaptureLimitReason();

                float rawTrackingAcceleration = hasSurfaceNormalHistory &&
                    GroundedProbeCount >= 2 &&
                    CaptureAuthorityMultiplier > 0f
                        ? CalculateSurfaceTrackingAcceleration(
                            body.linearVelocity,
                            previousTrackedGroundNormal,
                            GroundNormal,
                            deltaTime,
                            configuration.SurfaceCurvatureFeedForward,
                            configuration.MaximumSurfaceTrackingAcceleration)
                        : 0f;
                float response = configuration.SurfaceTrackingResponse;
                float blend = response <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-response * Mathf.Max(0f, deltaTime));
                SurfaceTrackingAcceleration = Mathf.Lerp(
                    SurfaceTrackingAcceleration,
                    rawTrackingAcceleration,
                    blend);
                previousTrackedGroundNormal = GroundNormal;
                hasSurfaceNormalHistory = true;
                if (PrimaryProbeCount >= 2 || IsNearSurface)
                {
                    lastGroundNormal = GroundNormal;
                }
            }
            else
            {
                surfaceAgeSeconds = float.IsPositiveInfinity(surfaceAgeSeconds)
                    ? Mathf.Max(0f, deltaTime)
                    : surfaceAgeSeconds + Mathf.Max(0f, deltaTime);
                hasSurfaceNormalHistory = false;
                SurfaceTrackingAcceleration = 0f;
                SurfaceConfidence = 0f;
                DistanceAuthorityMultiplier = 0f;
                CaptureAuthorityMultiplier = 0f;
                CaptureLimitReason = "No current physical probe evidence";
            }
            IsGrounded = IsNearSurface;
            SurfaceState = ClassifySurfaceState(surfaceDetectedLastTick);
            surfaceDetectedLastTick = SurfaceDetected;

            Vector3 gravity = environment != null &&
                environment.CurrentSample.IsValid
                    ? environment.CurrentSample.GravityVector
                    : Physics.gravity;
            float supportAcceleration = Mathf.Max(
                0f,
                SurfaceTrackingAcceleration);
            if (CaptureAuthorityMultiplier > 0f &&
                GravityCompensationEnabled)
            {
                supportAcceleration += Mathf.Max(
                    0f,
                    -Vector3.Dot(gravity, GroundNormal) *
                    GravityCompensationMultiplier);
            }
            float bottomNominalForce = SumNominalForce(BottomSockets);
            float sharedSupportOutput = CaptureAuthorityMultiplier > 0f
                ? body.mass * supportAcceleration /
                  Mathf.Max(1f, bottomNominalForce) *
                  CaptureAuthorityMultiplier
                : 0f;

            for (int i = 0; i < BottomSockets.Length; i++)
            {
                RuntimeThrusterInstance thruster = bottomThrusters[i];
                if (thruster == null)
                {
                    continue;
                }

                float manualOutput = Mathf.Min(
                    Mathf.Max(0f, command.Lift),
                    configuration.ManualAuthorityLimit);
                float automaticOutput = 0f;
                if (probeHits[i] && CaptureAuthorityMultiplier > 0f)
                {
                    float heightCorrection =
                        (TargetHeight - probeDistances[i]) *
                        configuration.HoverStrength -
                        probeNormalVelocities[i] *
                        configuration.HoverDamping;
                    float uncappedAutomatic = Mathf.Max(
                        0f,
                        sharedSupportOutput +
                        heightCorrection * CaptureAuthorityMultiplier);
                    BottomRequestBeforeAuthorityLimit = Mathf.Max(
                        BottomRequestBeforeAuthorityLimit,
                        uncappedAutomatic);
                    automaticOutput = Mathf.Min(
                        uncappedAutomatic,
                        configuration.AutomaticHoverAuthorityLimit);
                    BottomRequestAfterAuthorityLimit = Mathf.Max(
                        BottomRequestAfterAuthorityLimit,
                        automaticOutput);

                    if (command.StabilizationEnabled)
                    {
                        bool left = BottomSockets[i].Contains(".Left.");
                        bool front = BottomSockets[i].Contains(".Front.");
                        float stabilization =
                            (left ? localAngularVelocity.z : -localAngularVelocity.z) *
                            configuration.AngularDamping +
                            (front ? -localAngularVelocity.x : localAngularVelocity.x) *
                            configuration.AngularDamping;
                        router.Submit(
                            BottomSockets[i],
                            V3ActuatorChannel.Stabilization,
                            stabilization * CaptureAuthorityMultiplier,
                            V3ActuatorPriority.Stabilization);
                    }
                }

                router.Submit(
                    BottomSockets[i],
                    V3ActuatorChannel.BaseHover,
                    Mathf.Clamp(
                        Mathf.Max(manualOutput, automaticOutput),
                        0f,
                        thruster.ThrusterDefinition.NormalOutputMultiplier),
                    V3ActuatorPriority.Critical);
            }

            if (CaptureAuthorityMultiplier > 0f &&
                command.StabilizationEnabled)
            {
                RoofCaptureAcceleration = CalculateRoofCaptureAcceleration(
                    AverageSurfaceDistance,
                    TargetHeight,
                    AverageSurfaceNormalVelocity,
                    configuration.RoofCaptureStrength,
                    configuration.RoofCaptureDamping,
                    configuration.RoofCaptureDeadband,
                    configuration.MaximumRoofCaptureAcceleration);
                RoofCaptureAcceleration = Mathf.Min(
                    configuration.MaximumRoofCaptureAcceleration,
                    RoofCaptureAcceleration +
                    Mathf.Max(0f, -SurfaceTrackingAcceleration));
                RoofCaptureOutput = body.mass * RoofCaptureAcceleration /
                    Mathf.Max(1f, SumNominalForce(RoofSockets));
                RoofRequestBeforeAuthorityLimit =
                    RoofCaptureOutput * CaptureAuthorityMultiplier;
                RoofCaptureOutput = Mathf.Min(
                    RoofRequestBeforeAuthorityLimit,
                    configuration.AutomaticRoofAuthorityLimit);
                RoofRequestAfterAuthorityLimit = RoofCaptureOutput;
            }

            for (int i = 0; i < RoofSockets.Length; i++)
            {
                RuntimeThrusterInstance roofThruster =
                    router.FindThruster(RoofSockets[i]);
                if (roofThruster == null)
                {
                    continue;
                }

                if (RoofCaptureOutput > 0f)
                {
                    router.Submit(
                        RoofSockets[i],
                        V3ActuatorChannel.Stabilization,
                        Mathf.Min(
                            RoofCaptureOutput,
                            roofThruster.ThrusterDefinition.NormalOutputMultiplier),
                        V3ActuatorPriority.Stabilization);
                }
                float pitch = command.Pitch *
                    configuration.PitchSensitivity;
                float pitchContribution =
                    RoofSockets[i].Contains(".Front.")
                        ? Mathf.Max(0f, pitch)
                        : Mathf.Max(0f, -pitch);
                router.Submit(
                    RoofSockets[i],
                    V3ActuatorChannel.Manual,
                    Mathf.Min(
                        Mathf.Clamp01(command.Downforce + pitchContribution),
                        configuration.ManualAuthorityLimit),
                    V3ActuatorPriority.Stabilization);
            }

            if (CaptureAuthorityMultiplier > 0f &&
                command.StabilizationEnabled)
            {
                float speed = body.linearVelocity.magnitude;
                float stiffness = Mathf.Lerp(
                    1f,
                    configuration.MaximumSpeedStiffness,
                    Mathf.InverseLerp(
                        configuration.StiffenStartSpeed,
                        configuration.StiffenFullSpeed,
                        speed));
                SurfaceAlignmentAcceleration =
                    CalculateSurfaceAlignmentAcceleration(
                        runtime.transform.up,
                        GroundNormal,
                        body.angularVelocity,
                        configuration.SurfaceAlignStrength,
                        configuration.SurfaceAlignDamping,
                        configuration.MaximumSurfaceAlignAcceleration,
                        stiffness) * CaptureAuthorityMultiplier;
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

        private void AccumulateConsistentSurface(
            out Vector3 normalSum,
            out int consistentPrimaryCount)
        {
            normalSum = Vector3.zero;
            consistentPrimaryCount = 0;
            int referenceIndex = -1;
            int bestAgreement = -1;
            for (int i = 0; i < probeHits.Length; i++)
            {
                if (!probeHits[i]) continue;
                int agreement = 0;
                for (int j = 0; j < probeHits.Length; j++)
                {
                    if (probeHits[j] && Vector3.Angle(
                        probeNormals[i], probeNormals[j]) <=
                        configuration.NormalConsensusAngleDegrees)
                    {
                        agreement++;
                    }
                }
                if (agreement > bestAgreement)
                {
                    bestAgreement = agreement;
                    referenceIndex = i;
                }
            }

            if (referenceIndex < 0) return;
            Vector3 reference = probeNormals[referenceIndex];
            for (int i = 0; i < probeHits.Length; i++)
            {
                if (!probeHits[i] || Vector3.Angle(
                    reference, probeNormals[i]) >
                    configuration.NormalConsensusAngleDegrees)
                {
                    probeHits[i] = false;
                    continue;
                }
                normalSum += probeNormals[i];
                AverageSurfaceDistance += probeDistances[i];
                AverageSurfaceNormalVelocity += probeNormalVelocities[i];
                GroundedProbeCount++;
                if (!probeSnapshots[i].UsedFallback)
                {
                    consistentPrimaryCount++;
                }
            }
        }

        private string DetermineCaptureLimitReason()
        {
            if (AverageSurfaceDistance > configuration.CaptureRange)
                return "Outside automatic capture range";
            if (PrimaryProbeCount <= 0)
                return "Fallback-only evidence cap";
            if (GroundedProbeCount == 1)
                return "Single-probe confidence cap";
            if (GroundedProbeCount < BottomSockets.Length)
                return "Reduced probe consensus";
            if (DistanceAuthorityMultiplier < 0.999f)
                return "Distance falloff";
            return "Full capture authority";
        }

        private V3SurfaceState ClassifySurfaceState(bool wasDetected)
        {
            if (!SurfaceDetected)
            {
                if (wasDetected) return V3SurfaceState.ProbeLost;
                return surfaceAgeSeconds <= configuration.StaleSurfaceTimeoutSeconds
                    ? V3SurfaceState.NoSurface
                    : V3SurfaceState.FreeFlight;
            }
            if (IsNearSurface) return V3SurfaceState.NearSurfaceHover;
            if (CaptureAuthorityMultiplier <= 0f)
                return V3SurfaceState.SurfaceDetected;
            if (CaptureAuthorityMultiplier < 0.5f)
                return V3SurfaceState.CaptureLimited;
            if (AverageSurfaceNormalVelocity > 1.5f)
                return V3SurfaceState.SurfaceSeparating;
            return V3SurfaceState.SurfaceCaptured;
        }

        private float SumNominalForce(string[] socketIds)
        {
            float force = 0f;
            for (int i = 0; i < socketIds.Length; i++)
            {
                RuntimeThrusterInstance thruster =
                    router.FindThruster(socketIds[i]);
                if (thruster != null && thruster.ThrusterDefinition != null)
                {
                    force += thruster.ThrusterDefinition.MaximumForwardForceN;
                }
            }
            return force;
        }

        private bool TryProbeFallbackSurface(
            Rigidbody body,
            out Vector3 point,
            out Vector3 normal)
        {
            Vector3 origin =
                body.worldCenterOfMass + runtime.transform.up * 0.05f;
            float range = configuration.FallbackRange;
            Vector3 primaryDirection = -runtime.transform.up;
            if (Physics.SphereCast(
                origin,
                configuration.FallbackProbeRadius,
                primaryDirection,
                out RaycastHit hit,
                range,
                TrackSurfaceMask,
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
                    configuration.FallbackProbeRadius,
                    rememberedDirection,
                    out hit,
                    range,
                    TrackSurfaceMask,
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

        public static float CalculateSurfaceTrackingAcceleration(
            Vector3 velocity,
            Vector3 previousSurfaceNormal,
            Vector3 currentSurfaceNormal,
            float deltaTime,
            float feedForward,
            float maximumAcceleration)
        {
            if (deltaTime <= 0f ||
                previousSurfaceNormal.sqrMagnitude <= 0.000001f ||
                currentSurfaceNormal.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            Vector3 normal = currentSurfaceNormal.normalized;
            Vector3 tangentVelocity =
                velocity - normal * Vector3.Dot(velocity, normal);
            float speed = tangentVelocity.magnitude;
            if (speed <= 0.0001f)
            {
                return 0f;
            }

            Vector3 normalRate =
                (normal - previousSurfaceNormal.normalized) / deltaTime;
            float signedAcceleration =
                -speed *
                Vector3.Dot(normalRate, tangentVelocity / speed) *
                Mathf.Max(0f, feedForward);
            float maximum = Mathf.Max(0f, maximumAcceleration);
            return Mathf.Clamp(signedAcceleration, -maximum, maximum);
        }

        public static float CalculateRoofCaptureAcceleration(
            float surfaceDistance,
            float targetDistance,
            float separationVelocity,
            float strength,
            float damping,
            float deadband,
            float maximumAcceleration)
        {
            float distanceError = Mathf.Max(
                0f,
                surfaceDistance - targetDistance - Mathf.Max(0f, deadband));
            float acceleration =
                distanceError * Mathf.Max(0f, strength) +
                separationVelocity * Mathf.Max(0f, damping);
            return Mathf.Clamp(
                acceleration,
                0f,
                Mathf.Max(0f, maximumAcceleration));
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
