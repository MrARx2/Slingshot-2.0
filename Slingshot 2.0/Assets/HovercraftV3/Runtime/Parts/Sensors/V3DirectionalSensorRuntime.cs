using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3DirectionalSensorRuntime
    {
        private V3DirectionalSensorDefinition definition;
        private Transform origin;
        private float accumulator;
        private V3DirectionalSensorSnapshot snapshot;

        public V3DirectionalSensorDefinition Definition => definition;
        public float RequestedRateHz =>
            definition != null ? definition.RequestedSampleRateHz : 0f;
        public float GrantedRateHz { get; private set; }
        public float RequestedPower { get; private set; }
        public float GrantedPower { get; private set; }
        public string Bottleneck { get; private set; } = "Uninitialized";
        public V3DirectionalSensorSnapshot Snapshot => snapshot;
        public int SampleCount { get; private set; }

        public void Initialize(
            V3DirectionalSensorDefinition value,
            Transform sensorOrigin)
        {
            definition = value;
            origin = sensorOrigin;
            accumulator = 0f;
            GrantedRateHz = 0f;
            RequestedPower = 0f;
            GrantedPower = 0f;
            snapshot = default;
            SampleCount = 0;
            Bottleneck = value == null || sensorOrigin == null
                ? "MissingDefinitionOrMount"
                : "None";
        }

        public void Grant(float availablePower, float physicsRateHz)
        {
            if (definition == null)
            {
                GrantedRateHz = 0f;
                RequestedPower = 0f;
                GrantedPower = 0f;
                Bottleneck = "MissingDefinition";
                return;
            }

            float requestedRate = Mathf.Min(
                definition.RequestedSampleRateHz,
                Mathf.Max(1f, physicsRateHz));
            RequestedPower = definition.IdlePower +
                requestedRate * definition.PowerPerSample;
            GrantedPower = Mathf.Min(
                RequestedPower,
                Mathf.Max(0f, availablePower));
            float variableRequest = Mathf.Max(
                0f,
                RequestedPower - definition.IdlePower);
            float variableGrant = Mathf.Max(
                0f,
                GrantedPower - definition.IdlePower);
            float powerFraction = variableRequest <= 0.0001f
                ? (GrantedPower >= RequestedPower ? 1f : 0f)
                : Mathf.Clamp01(variableGrant / variableRequest);
            GrantedRateHz = requestedRate * powerFraction;
            if (physicsRateHz + 0.001f <
                definition.RequestedSampleRateHz)
            {
                Bottleneck = "PhysicsRate";
            }
            else if (GrantedRateHz + 0.001f < requestedRate)
            {
                Bottleneck = "SystemsPower";
            }
            else
            {
                Bottleneck = "None";
            }
        }

        public void ApplyGrant(
            float requestedRate,
            float grantedRate,
            float requestedPower,
            float grantedPower,
            string bottleneck)
        {
            RequestedPower = Mathf.Max(0f, requestedPower);
            GrantedPower = Mathf.Clamp(
                grantedPower,
                0f,
                RequestedPower);
            GrantedRateHz = Mathf.Max(0f, grantedRate);
            Bottleneck = bottleneck ?? "Unknown";
        }

        public void Tick(
            V3ObservationBus bus,
            Rigidbody body,
            double timestamp,
            float deltaTime,
            V3WorldEnvironmentSample environment)
        {
            if (definition == null ||
                origin == null ||
                bus == null ||
                GrantedRateHz <= 0f)
            {
                return;
            }

            accumulator += Mathf.Max(0f, deltaTime);
            float interval = 1f / GrantedRateHz;
            if (accumulator + 0.000001f < interval)
            {
                return;
            }

            accumulator = Mathf.Min(accumulator - interval, interval);
            Vector3 direction = GetWorldDirection(origin, definition.Direction);
            bool hit = definition.CastType ==
                V3SensorCastType.SphereCast
                    ? Physics.SphereCast(
                        origin.position,
                        definition.SphereRadius,
                        direction,
                        out RaycastHit hitInfo,
                        definition.RangeMeters,
                        definition.SurfaceMask,
                        definition.TriggerInteraction)
                    : Physics.Raycast(
                        origin.position,
                        direction,
                        out hitInfo,
                        definition.RangeMeters,
                        definition.SurfaceMask,
                        definition.TriggerInteraction);
            hit = hit &&
                hitInfo.distance + 0.0001f >=
                definition.MinimumValidDistance;
            float distance =
                hit ? hitInfo.distance : definition.RangeMeters;
            Vector3 normal = hit ? hitInfo.normal : Vector3.zero;
            Vector3 samplePoint =
                hit ? hitInfo.point : origin.position;
            Vector3 craftPointVelocity = body != null
                ? body.GetPointVelocity(samplePoint)
                : Vector3.zero;
            Rigidbody hitBody =
                hit && hitInfo.rigidbody != null
                    ? hitInfo.rigidbody
                    : null;
            Vector3 hitBodyPointVelocity = hitBody != null
                ? hitBody.GetPointVelocity(samplePoint)
                : Vector3.zero;
            Vector3 relativeVelocity =
                craftPointVelocity - hitBodyPointVelocity;
            Vector3 sensorPointVelocity = body != null
                ? body.GetPointVelocity(origin.position)
                : Vector3.zero;
            Vector3 relativeAirflow =
                environment.LocalAirVelocity - sensorPointVelocity;
            float confidence = hit ? 1f : 0.5f;
            snapshot = new V3DirectionalSensorSnapshot(
                true,
                hit,
                distance,
                samplePoint,
                normal,
                hit && hitInfo.collider != null
                    ? hitInfo.collider.GetEntityId().GetHashCode()
                    : 0,
                hit && hitInfo.collider != null
                    ? hitInfo.collider.gameObject.layer
                    : -1,
                craftPointVelocity,
                hitBodyPointVelocity,
                relativeVelocity,
                relativeAirflow,
                environment,
                timestamp,
                confidence);
            SampleCount++;
            bus.Publish(new V3Observation(
                V3ObservationCategory.EnvironmentSurface,
                definition.StableId,
                timestamp,
                normal,
                relativeVelocity,
                distance,
                GrantedRateHz,
                hit ? 1f : 0f,
                hit));
            bus.Publish(new V3Observation(
                V3ObservationCategory.EnvironmentAtmosphere,
                definition.StableId,
                timestamp,
                relativeAirflow,
                direction,
                relativeAirflow.magnitude,
                environment.AirDensityKgPerCubicMeter,
                1f,
                environment.IsValid));
            bus.Publish(new V3Observation(
                V3ObservationCategory.SystemHealth,
                definition.StableId,
                timestamp,
                Vector3.zero,
                Vector3.zero,
                environment.AmbientTemperatureC,
                GrantedPower,
                RequestedPower <= 0.0001f
                    ? 1f
                    : Mathf.Clamp01(GrantedPower / RequestedPower),
                GrantedRateHz >= definition.MinimumSampleRateHz));
        }

        public void Tick(
            V3ObservationBus bus,
            Rigidbody body,
            double timestamp,
            float deltaTime,
            Vector3 airflowWorld,
            float density,
            float ambientTemperatureC)
        {
            Tick(
                bus,
                body,
                timestamp,
                deltaTime,
                new V3WorldEnvironmentSample
                {
                    IsValid = true,
                    SimulationTime = timestamp,
                    LocalAirVelocity = airflowWorld,
                    AirDensityKgPerCubicMeter = Mathf.Max(0f, density),
                    AmbientTemperatureC = ambientTemperatureC,
                    AmbientTemperatureK = ambientTemperatureC + 273.15f
                });
        }

        public static Vector3 GetWorldDirection(
            Transform root,
            V3SensorDirection direction)
        {
            switch (direction)
            {
                case V3SensorDirection.Rear:
                    return -root.forward;
                case V3SensorDirection.Left:
                    return -root.right;
                case V3SensorDirection.Right:
                    return root.right;
                case V3SensorDirection.Top:
                    return root.up;
                case V3SensorDirection.Bottom:
                    return -root.up;
                default:
                    return root.forward;
            }
        }
    }
}
