using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3DirectionalSensorHealthState
    {
        Uninitialized,
        Offline,
        Underpowered,
        Operational,
        Faulted
    }

    public readonly struct V3DirectionalSensorSnapshot
    {
        public V3DirectionalSensorSnapshot(
            bool hasSample,
            bool hit,
            float distance,
            Vector3 hitPoint,
            Vector3 surfaceNormal,
            int colliderInstanceId,
            int surfaceLayer,
            Vector3 craftPointVelocity,
            Vector3 hitBodyPointVelocity,
            Vector3 relativePointVelocity,
            Vector3 relativeAirflow,
            V3WorldEnvironmentSample environmentTruth,
            double timestamp,
            float confidence)
        {
            HasSample = hasSample;
            Hit = hit;
            Distance = distance;
            HitPoint = hitPoint;
            SurfaceNormal = surfaceNormal;
            ColliderInstanceId = colliderInstanceId;
            SurfaceLayer = surfaceLayer;
            CraftPointVelocity = craftPointVelocity;
            HitBodyPointVelocity = hitBodyPointVelocity;
            RelativePointVelocity = relativePointVelocity;
            RelativeAirflow = relativeAirflow;
            EnvironmentTruth = environmentTruth;
            Timestamp = timestamp;
            Confidence = Mathf.Clamp01(confidence);
        }

        public bool HasSample { get; }
        public bool Hit { get; }
        public float Distance { get; }
        public Vector3 HitPoint { get; }
        public Vector3 SurfaceNormal { get; }
        public int ColliderInstanceId { get; }
        public int SurfaceLayer { get; }
        public Vector3 CraftPointVelocity { get; }
        public Vector3 HitBodyPointVelocity { get; }
        public Vector3 RelativePointVelocity { get; }
        public Vector3 RelativeAirflow { get; }
        public V3WorldEnvironmentSample EnvironmentTruth { get; }
        public float AirDensity =>
            EnvironmentTruth.AirDensityKgPerCubicMeter;
        public float AmbientTemperatureC =>
            EnvironmentTruth.AmbientTemperatureC;
        public float AtmosphericPressurePa =>
            EnvironmentTruth.AtmosphericPressurePa;
        public float SpeedOfSoundMetersPerSecond =>
            EnvironmentTruth.SpeedOfSoundMetersPerSecond;
        public Vector3 GravityVector => EnvironmentTruth.GravityVector;
        public double Timestamp { get; }
        public float Confidence { get; }
    }

    [DisallowMultipleComponent]
    public sealed class V3DirectionalSensorDeviceRuntime :
        MonoBehaviour,
        IV3RuntimeDevice
    {
        [SerializeField] private V3DirectionalSensorDefinition definition;
        [SerializeField] private V3DeviceFirmwareDefinition firmware;
        [SerializeField] private string mountName;
        [SerializeField] private string runtimeDeviceId;
        [SerializeField] private bool showDebug = true;

        private readonly V3DirectionalSensorRuntime sensingEngine =
            new V3DirectionalSensorRuntime();
        private Rigidbody craftBody;
        private V3CraftMainframe mainframe;
        private IV3EnvironmentProvider environment;
        private float requestedPower;
        private float grantedPower;
        private float requestedRate;
        private float grantedRate;
        private float measuredRate;
        private int measuredSamples;
        private float measuredWindow;

        public V3DirectionalSensorDefinition Definition => definition;
        public V3DeviceFirmwareDefinition Firmware => firmware;
        public string MountName => mountName;
        public string RuntimeDeviceId => runtimeDeviceId;
        public string DeviceDisplayName =>
            definition != null
                ? definition.DisplayName
                : gameObject.name;
        public bool IsDeviceOnline =>
            HealthState == V3DirectionalSensorHealthState.Operational;
        public Component DeviceComponent => this;
        public V3DirectionalSensorHealthState HealthState
        {
            get;
            private set;
        } = V3DirectionalSensorHealthState.Uninitialized;
        public string FaultReason { get; private set; } = string.Empty;
        public float RequestedPower => requestedPower;
        public float GrantedPower => grantedPower;
        public float RequestedRateHz => requestedRate;
        public float GrantedRateHz => grantedRate;
        public float MeasuredRateHz => measuredRate;
        public string Bottleneck => sensingEngine.Bottleneck;
        public V3DirectionalSensorSnapshot Snapshot =>
            sensingEngine.Snapshot;
        public int SampleCount => sensingEngine.SampleCount;
        public bool HasSuccessfulSample =>
            sensingEngine.SampleCount > 0;

        public bool Initialize(
            V3DirectionalSensorDefinition sensorDefinition,
            Rigidbody body,
            V3CraftMainframe owner,
            IV3EnvironmentProvider environmentProvider,
            string authoredMountName)
        {
            definition = sensorDefinition;
            firmware = sensorDefinition != null
                ? sensorDefinition.Firmware
                : null;
            craftBody = body;
            mainframe = owner;
            environment = environmentProvider;
            mountName = authoredMountName ?? string.Empty;
            runtimeDeviceId = definition != null
                ? definition.StableId
                : string.Empty;
            requestedPower = 0f;
            grantedPower = 0f;
            requestedRate = 0f;
            grantedRate = 0f;
            measuredRate = 0f;
            measuredSamples = 0;
            measuredWindow = 0f;
            FaultReason = ValidateConfiguration();
            if (!string.IsNullOrEmpty(FaultReason))
            {
                HealthState =
                    V3DirectionalSensorHealthState.Faulted;
                return false;
            }

            sensingEngine.Initialize(definition, transform);
            EnsurePrototypeVisuals();
            HealthState = V3DirectionalSensorHealthState.Offline;
            return true;
        }

        public void BindMainframe(V3CraftMainframe owner)
        {
            mainframe = owner;
        }

        public void Grant(float availablePower, float physicsRateHz)
        {
            if (!IsConfigurationValid())
            {
                requestedPower = 0f;
                grantedPower = 0f;
                requestedRate = 0f;
                grantedRate = 0f;
                HealthState =
                    V3DirectionalSensorHealthState.Faulted;
                return;
            }

            requestedRate = Mathf.Min(
                definition.RequestedSampleRateHz,
                definition.MaximumSampleRateHz,
                firmware.RequestedCommandRateHz,
                Mathf.Max(1f, physicsRateHz));
            float idle = definition.IdlePower + firmware.IdlePower;
            float perSample =
                definition.PowerPerSample + firmware.PowerPerCommand;
            requestedPower = idle + requestedRate * perSample;
            grantedPower = Mathf.Min(
                requestedPower,
                Mathf.Max(0f, availablePower));
            float variableGrant = Mathf.Max(0f, grantedPower - idle);
            grantedRate = grantedPower + 0.0001f < idle
                ? 0f
                : perSample <= 0.0001f
                    ? requestedRate
                    : Mathf.Min(
                        requestedRate,
                        variableGrant / perSample);
            sensingEngine.ApplyGrant(
                requestedRate,
                grantedRate,
                requestedPower,
                grantedPower,
                grantedRate + 0.001f <
                    definition.MinimumSampleRateHz
                    ? "SystemsPower"
                    : requestedRate + 0.001f <
                      definition.RequestedSampleRateHz
                        ? "RateLimit"
                        : "None");
            HealthState = mainframe == null
                ? V3DirectionalSensorHealthState.Offline
                : grantedRate + 0.001f <
                  definition.MinimumSampleRateHz
                    ? V3DirectionalSensorHealthState.Underpowered
                    : V3DirectionalSensorHealthState.Operational;
        }

        public void Tick(
            V3ObservationBus bus,
            double timestamp,
            float deltaTime)
        {
            if (HealthState !=
                V3DirectionalSensorHealthState.Operational)
            {
                return;
            }

            V3WorldEnvironmentSample sample = default;
            if (environment != null)
            {
                environment.TrySampleEnvironment(
                    transform.position,
                    out sample);
            }
            int before = sensingEngine.SampleCount;
            sensingEngine.Tick(
                bus,
                craftBody,
                timestamp,
                deltaTime,
                sample);
            if (sensingEngine.SampleCount > before)
            {
                measuredSamples++;
            }

            measuredWindow += Mathf.Max(0f, deltaTime);
            if (measuredWindow >= 1f)
            {
                measuredRate = measuredSamples / measuredWindow;
                measuredWindow = 0f;
                measuredSamples = 0;
            }
        }

        private string ValidateConfiguration()
        {
            if (definition == null)
            {
                return "Missing sensor definition.";
            }

            if (craftBody == null)
            {
                return "Missing craft Rigidbody.";
            }

            if (firmware == null)
            {
                return "Missing sensor firmware.";
            }

            if (firmware.DeviceKind !=
                V3FirmwareDeviceKind.DirectionalSensor)
            {
                return "Incompatible sensor firmware.";
            }

            if (string.IsNullOrWhiteSpace(mountName))
            {
                return "Missing authored sensor mount.";
            }

            return string.Empty;
        }

        private bool IsConfigurationValid()
        {
            return string.IsNullOrEmpty(ValidateConfiguration());
        }

        private void EnsurePrototypeVisuals()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            GameObject body = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            body.name = "Sensor Body";
            body.transform.SetParent(transform, false);
            body.transform.localScale =
                definition != null
                    ? definition.LocalVisualScale
                    : new Vector3(0.28f, 0.18f, 0.38f);
            Collider bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(bodyCollider);
                }
                else
                {
                    DestroyImmediate(bodyCollider);
                }
            }

            GameObject arrow = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            arrow.name = "Direction Arrow";
            arrow.transform.SetParent(transform, false);
            arrow.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            arrow.transform.localScale =
                new Vector3(0.06f, 0.06f, 0.42f);
            Collider arrowCollider = arrow.GetComponent<Collider>();
            if (arrowCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(arrowCollider);
                }
                else
                {
                    DestroyImmediate(arrowCollider);
                }
            }

            var origin = new GameObject("Debug Origin");
            origin.transform.SetParent(transform, false);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebug || definition == null)
            {
                return;
            }

            Vector3 direction =
                V3DirectionalSensorRuntime.GetWorldDirection(
                    transform,
                    definition.Direction);
            Gizmos.color = definition.DebugColor;
            Gizmos.DrawLine(
                transform.position,
                transform.position +
                direction * definition.RangeMeters);
            if (definition.CastType ==
                V3SensorCastType.SphereCast)
            {
                Gizmos.DrawWireSphere(
                    transform.position,
                    definition.SphereRadius);
            }

            if (Snapshot.Hit)
            {
                Gizmos.DrawSphere(Snapshot.HitPoint, 0.08f);
                Gizmos.DrawLine(
                    Snapshot.HitPoint,
                    Snapshot.HitPoint +
                    Snapshot.SurfaceNormal * 0.75f);
            }
        }
    }
}
