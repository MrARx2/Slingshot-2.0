using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    internal struct V3DiagnosticSensorBinding
    {
        public V3DirectionalSensorDeviceRuntime runtime;
        public string id;
        public string direction;
    }

    public sealed class V3CraftSensorRecorder : IV3DiagnosticSource
    {
        private readonly RaycastHit[] truthHits = new RaycastHit[32];
        private V3DiagnosticContext context;
        private V3DiagnosticSensorBinding[] sensors =
            Array.Empty<V3DiagnosticSensorBinding>();
        private V3DiagnosticSession session;
        private V3HoverController hoverController;
        private bool observationOverflowWarned;

        public string SourceId => "sensors";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
        }

        public void OnSessionStarted(V3DiagnosticSession value)
        {
            session = value;
            observationOverflowWarned = false;
            hoverController = context != null && context.Craft != null
                ? context.Craft.GetComponent<V3HoverController>()
                : null;
            IReadOnlyList<V3DirectionalSensorDeviceRuntime> source =
                context != null && context.Mainframe != null
                    ? context.Mainframe.Sensors
                    : null;
            int count = source != null ? source.Count : 0;
            sensors = new V3DiagnosticSensorBinding[count];
            for (int i = 0; i < count; i++)
            {
                V3DirectionalSensorDeviceRuntime sensor = source[i];
                sensors[i] = new V3DiagnosticSensorBinding
                {
                    runtime = sensor,
                    id = sensor != null ? sensor.RuntimeDeviceId : string.Empty,
                    direction = sensor != null && sensor.Definition != null
                        ? DirectionName(sensor.Definition.Direction)
                        : "Unknown"
                };
            }
            if (value != null && value.SampleCount == 0 &&
                value.Samples != null && value.Samples.Length > 0 &&
                value.Samples[0].sensors != null &&
                count + (hoverController != null
                    ? hoverController.ProbeSnapshots.Count : 0) >
                value.Samples[0].sensors.Length)
            {
                value.AddDataWarning(
                    "Sensor and hover-probe count exceeds the configured per-sample capacity; excess sensor records will be explicitly dropped.");
            }
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            int capacity = sample.sensors != null ? sample.sensors.Length : 0;
            int count = Mathf.Min(sensors.Length, capacity);
            sample.sensorCount = count;
            for (int i = 0; i < count; i++)
            {
                V3DiagnosticSensorBinding binding = sensors[i];
                V3DirectionalSensorDeviceRuntime sensor = binding.runtime;
                if (sensor == null)
                {
                    sample.sensors[i] = new V3SensorDiagnosticRecord
                    {
                        sensorId = binding.id,
                        direction = binding.direction,
                        comparisonQuality = V3DiagnosticDataQuality.Low,
                        unavailableReason = "Sensor runtime was destroyed."
                    };
                    continue;
                }
                V3DirectionalSensorDefinition definition = sensor.Definition;
                V3DirectionalSensorSnapshot snapshot = sensor.Snapshot;
                Vector3 direction = definition != null
                    ? V3DirectionalSensorRuntime.GetWorldDirection(
                        sensor.transform, definition.Direction)
                    : sensor.transform.forward;
                RaycastHit worldHit = default;
                bool hasWorldTruth = definition != null &&
                    TryCaptureIndependentTruth(sensor, definition, direction,
                        out worldHit);
                float trueDistance = hasWorldTruth
                    ? worldHit.distance
                    : definition != null ? definition.RangeMeters : 0f;
                bool comparableDistance = snapshot.HasSample &&
                    snapshot.Hit && hasWorldTruth;
                bool comparableNormal = comparableDistance &&
                    snapshot.SurfaceNormal.sqrMagnitude > 0.000001f &&
                    worldHit.normal.sqrMagnitude > 0.000001f;
                sample.sensors[i] = new V3SensorDiagnosticRecord
                {
                    sensorId = binding.id,
                    direction = binding.direction,
                    mountPosition = sensor.transform.position,
                    mountRotation = sensor.transform.rotation,
                    forward = direction,
                    requestedRateHz = sensor.RequestedRateHz,
                    grantedRateHz = sensor.GrantedRateHz,
                    measuredRateHz = sensor.MeasuredRateHz,
                    minimumUsefulRateHz = definition != null
                        ? definition.MinimumSampleRateHz : 0f,
                    requestedPower = sensor.RequestedPower,
                    grantedPower = sensor.GrantedPower,
                    health = HealthName(sensor.HealthState),
                    fault = sensor.FaultReason,
                    hasSample = snapshot.HasSample,
                    hit = snapshot.Hit,
                    measuredDistance = snapshot.Distance,
                    measuredPoint = snapshot.HitPoint,
                    measuredNormal = snapshot.SurfaceNormal,
                    colliderId = snapshot.ColliderInstanceId,
                    relativeVelocity = snapshot.RelativePointVelocity,
                    airflow = snapshot.RelativeAirflow,
                    airDensity = snapshot.AirDensity,
                    ambientTemperatureC = snapshot.AmbientTemperatureC,
                    atmosphericPressurePa =
                        snapshot.AtmosphericPressurePa,
                    speedOfSoundMetersPerSecond =
                        snapshot.SpeedOfSoundMetersPerSecond,
                    gravityVector = snapshot.GravityVector,
                    localAirVelocity =
                        snapshot.EnvironmentTruth.LocalAirVelocity,
                    environmentValid =
                        snapshot.EnvironmentTruth.IsValid,
                    craftPhysicsTickId = context.EnvironmentProvider != null
                        ? context.EnvironmentProvider.CurrentCraftPhysicsTickId
                        : -1,
                    environmentPhysicsTickId =
                        snapshot.EnvironmentTruth.PhysicsTickId,
                    environmentProfileId =
                        snapshot.EnvironmentTruth.WorldProfileId,
                    dominantZoneId =
                        snapshot.EnvironmentTruth.DominantZoneId,
                    timestamp = snapshot.Timestamp,
                    ageSeconds = snapshot.HasSample
                        ? (float)Math.Max(0d,
                            sample.identity.simulationTimestamp - snapshot.Timestamp)
                        : 0f,
                    confidence = snapshot.Confidence,
                    trueDistance = trueDistance,
                    hasWorldTruth = definition != null,
                    trueHit = hasWorldTruth,
                    distanceError = comparableDistance
                        ? snapshot.Distance - trueDistance : 0f,
                    normalAngleError = comparableNormal
                        ? Vector3.Angle(snapshot.SurfaceNormal, worldHit.normal) : 0f,
                    comparisonQuality = comparableDistance
                        ? V3DiagnosticDataQuality.High
                        : snapshot.HasSample
                            ? V3DiagnosticDataQuality.Medium
                            : V3DiagnosticDataQuality.Low,
                    unavailableReason = comparableDistance
                        ? string.Empty
                        : BuildUnavailableReason(
                            definition != null, snapshot.HasSample,
                            snapshot.Hit, hasWorldTruth)
                };
            }

            CaptureHoverProbes(ref sample, capacity);

            CaptureObservations(ref sample);
        }

        private void CaptureHoverProbes(
            ref V3DiagnosticSample sample, int capacity)
        {
            if (hoverController == null) return;
            IReadOnlyList<V3HoverProbeSnapshot> probes =
                hoverController.ProbeSnapshots;
            for (int i = 0; i < probes.Count && sample.sensorCount < capacity; i++)
            {
                V3HoverProbeSnapshot probe = probes[i];
                sample.sensors[sample.sensorCount++] =
                    new V3SensorDiagnosticRecord
                    {
                        sensorId = probe.SocketId,
                        direction = "HoverProbe",
                        mountPosition = probe.Origin,
                        forward = probe.Direction,
                        hasSample = true,
                        hit = probe.Hit,
                        measuredDistance = probe.Distance,
                        measuredPoint = probe.Hit
                            ? probe.Origin + probe.Direction.normalized * probe.Distance
                            : probe.Origin,
                        measuredNormal = probe.Normal,
                        craftPhysicsTickId = context.EnvironmentProvider != null
                            ? context.EnvironmentProvider.CurrentCraftPhysicsTickId
                            : -1,
                        environmentPhysicsTickId = context.EnvironmentProvider != null
                            ? context.EnvironmentProvider.CurrentSample.PhysicsTickId
                            : -1,
                        confidence = probe.Hit ? 1f : 0f,
                        comparisonQuality = probe.UsedFallback
                            ? V3DiagnosticDataQuality.Medium
                            : V3DiagnosticDataQuality.High,
                        unavailableReason = probe.Hit
                            ? (probe.UsedFallback
                                ? "Hover controller used its fallback surface plane."
                                : string.Empty)
                            : "Hover probe reported no surface."
                    };
            }
        }

        public void OnSessionEnded(V3DiagnosticSession value)
        {
            session = null;
        }

        private void CaptureObservations(ref V3DiagnosticSample sample)
        {
            IReadOnlyList<V3Observation> observations =
                context != null && context.Mainframe != null
                    ? context.Mainframe.Observations.Snapshot
                    : null;
            int sourceCount = observations != null ? observations.Count : 0;
            int capacity = sample.observations != null
                ? sample.observations.Length : 0;
            int count = Mathf.Min(sourceCount, capacity);
            sample.observationCount = count;
            for (int i = 0; i < count; i++)
            {
                V3Observation observation = observations[i];
                sample.observations[i] = new V3ObservationDiagnosticRecord
                {
                    category = CategoryName(observation.Category),
                    sourceId = observation.SourceId,
                    timestamp = observation.Timestamp,
                    ageSeconds = (float)Math.Max(0d,
                        sample.identity.simulationTimestamp - observation.Timestamp),
                    confidence = observation.Confidence,
                    primaryVector = observation.PrimaryVector,
                    secondaryVector = observation.SecondaryVector,
                    primaryScalar = observation.PrimaryScalar,
                    secondaryScalar = observation.SecondaryScalar,
                    state = observation.State,
                    payloadHash = ComputeObservationHash(observation)
                };
            }
            if (sourceCount > capacity && !observationOverflowWarned &&
                session != null)
            {
                observationOverflowWarned = true;
                session.AddDataWarning(
                    "Observation count exceeds the configured per-sample capacity; excess records were dropped.");
            }
        }

        private bool TryCaptureIndependentTruth(
            V3DirectionalSensorDeviceRuntime sensor,
            V3DirectionalSensorDefinition definition,
            Vector3 direction,
            out RaycastHit closest)
        {
            closest = default;
            int count = definition.CastType == V3SensorCastType.SphereCast
                ? Physics.SphereCastNonAlloc(
                    sensor.transform.position,
                    definition.SphereRadius,
                    direction,
                    truthHits,
                    definition.RangeMeters,
                    ~0,
                    QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(
                    sensor.transform.position,
                    direction,
                    truthHits,
                    definition.RangeMeters,
                    ~0,
                    QueryTriggerInteraction.Ignore);
            float bestDistance = float.PositiveInfinity;
            bool found = false;
            Transform craftRoot = context != null ? context.CraftTransform : null;
            for (int i = 0; i < count; i++)
            {
                Collider collider = truthHits[i].collider;
                if (collider == null ||
                    (craftRoot != null && collider.transform.IsChildOf(craftRoot)))
                {
                    continue;
                }
                if (truthHits[i].distance >= definition.MinimumValidDistance &&
                    truthHits[i].distance < bestDistance)
                {
                    bestDistance = truthHits[i].distance;
                    closest = truthHits[i];
                    found = true;
                }
            }
            return found;
        }

        private static int ComputeObservationHash(V3Observation value)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)value.Category;
                hash = hash * 31 + (value.SourceId != null
                    ? value.SourceId.GetHashCode() : 0);
                hash = hash * 31 + value.Timestamp.GetHashCode();
                hash = hash * 31 + value.PrimaryVector.GetHashCode();
                hash = hash * 31 + value.SecondaryVector.GetHashCode();
                hash = hash * 31 + value.PrimaryScalar.GetHashCode();
                hash = hash * 31 + value.SecondaryScalar.GetHashCode();
                hash = hash * 31 + value.Confidence.GetHashCode();
                hash = hash * 31 + value.State.GetHashCode();
                return hash;
            }
        }

        private static string BuildUnavailableReason(
            bool configured, bool hasSample, bool measuredHit, bool trueHit)
        {
            if (!configured) return "Missing sensor definition.";
            if (!hasSample) return "No sensor sample has been published.";
            if (!measuredHit && !trueHit) return "Both sensor and independent truth reported no hit.";
            if (!measuredHit) return "Sensor missed an independently observed surface.";
            return "Independent world truth reported no hit.";
        }

        private static string DirectionName(V3SensorDirection value)
        {
            return value switch
            {
                V3SensorDirection.Front => "Front",
                V3SensorDirection.Rear => "Rear",
                V3SensorDirection.Left => "Left",
                V3SensorDirection.Right => "Right",
                V3SensorDirection.Top => "Top",
                V3SensorDirection.Bottom => "Bottom",
                _ => "Unknown"
            };
        }

        private static string HealthName(V3DirectionalSensorHealthState value)
        {
            return value switch
            {
                V3DirectionalSensorHealthState.Uninitialized => "Uninitialized",
                V3DirectionalSensorHealthState.Offline => "Offline",
                V3DirectionalSensorHealthState.Underpowered => "Underpowered",
                V3DirectionalSensorHealthState.Operational => "Operational",
                V3DirectionalSensorHealthState.Faulted => "Faulted",
                _ => "Unknown"
            };
        }

        private static string CategoryName(V3ObservationCategory value)
        {
            return value switch
            {
                V3ObservationCategory.ChassisState => "ChassisState",
                V3ObservationCategory.EnvironmentSurface => "EnvironmentSurface",
                V3ObservationCategory.EnvironmentAtmosphere => "EnvironmentAtmosphere",
                V3ObservationCategory.DeviceState => "DeviceState",
                V3ObservationCategory.PowerState => "PowerState",
                V3ObservationCategory.ThermalState => "ThermalState",
                V3ObservationCategory.TopologyState => "TopologyState",
                V3ObservationCategory.SystemHealth => "SystemHealth",
                _ => "Unknown"
            };
        }
    }
}
