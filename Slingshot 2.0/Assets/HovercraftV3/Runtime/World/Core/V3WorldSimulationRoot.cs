using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DefaultExecutionOrder(-2000)]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(V3EnvironmentZoneRegistry))]
    public sealed class V3WorldSimulationRoot : MonoBehaviour
    {
        [SerializeField] private string stableRootId = "world.root.default";
        [SerializeField] private V3WorldProfile profile;
        [Tooltip("Off by default to preserve V2 and shared project physics. " +
                 "V3 craft gravity is applied from the sampled world instead.")]
        [SerializeField] private bool synchronizeUnityGravity;
        [SerializeField] private bool logValidationErrors = true;
        [Header("Authoritative World Clock")]
        [Min(0f), SerializeField] private float initialSimulationTimeSeconds;
        [Min(0), SerializeField] private long initialPhysicsTickId;

        private Vector3 gravityBeforeSynchronization;
        private bool gravityWasSynchronized;
        private V3EnvironmentZoneRegistry zoneRegistry;
        private double simulationTime;
        private long physicsTickId;
        private bool clockInitialized;

        public string StableRootId => string.IsNullOrWhiteSpace(stableRootId)
            ? gameObject.name : stableRootId;
        public V3WorldProfile Profile => profile;
        public V3WorldClockState CurrentClock
        {
            get
            {
                EnsureClockInitialized();
                return new V3WorldClockState(
                    simulationTime,
                    physicsTickId);
            }
        }
        public V3EnvironmentZoneRegistry ZoneRegistry
        {
            get
            {
                if (zoneRegistry == null)
                {
                    zoneRegistry = GetComponent<V3EnvironmentZoneRegistry>();
                }
                return zoneRegistry;
            }
        }

        public void Configure(V3WorldProfile value, string rootId = null)
        {
            profile = value;
            if (!string.IsNullOrWhiteSpace(rootId))
            {
                stableRootId = rootId;
            }
            ZoneRegistry?.MarkDirty();
            SynchronizeGravityIfRequested();
            if (isActiveAndEnabled)
            {
                ValidateRuntimeState();
            }
        }

        private void OnEnable()
        {
            ResetClockToConfiguredStart();
            zoneRegistry = GetComponent<V3EnvironmentZoneRegistry>();
            zoneRegistry.MarkDirty();
            V3WorldQueryService.Register(this);
            SynchronizeGravityIfRequested();
            if (profile != null)
            {
                ValidateRuntimeState();
            }
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                ResetClockToConfiguredStart();
            }
        }

        private void FixedUpdate()
        {
            if (Application.isPlaying)
            {
                AdvanceClock(Time.fixedDeltaTime);
            }
        }

        private void OnDisable()
        {
            V3WorldQueryService.Unregister(this);
            if (gravityWasSynchronized)
            {
                Physics.gravity = gravityBeforeSynchronization;
                gravityWasSynchronized = false;
            }
        }

        private void OnValidate()
        {
            zoneRegistry = GetComponent<V3EnvironmentZoneRegistry>();
            ZoneRegistry?.MarkDirty();
            if (isActiveAndEnabled)
            {
                ValidateRuntimeState();
            }
        }

        public bool TrySample(
            Vector3 worldPosition,
            out V3WorldEnvironmentSample sample)
        {
            V3WorldClockState clock = CurrentClock;
            if (profile == null)
            {
                sample = V3WorldEnvironmentSample.Invalid(
                    worldPosition,
                    V3WorldSampleFlags.MissingProfile,
                    clock);
                return false;
            }

            float altitude = worldPosition.y - profile.ReferenceWorldY +
                profile.AltitudeOffsetMeters;
            sample = new V3WorldEnvironmentSample
            {
                IsValid = true,
                Flags = V3WorldSampleFlags.None,
                WorldRootId = StableRootId,
                WorldProfileId = profile.StableId,
                WorldProfileName = profile.DisplayName,
                WorldConfigurationVersion = profile.ConfigurationVersion,
                WorldPosition = worldPosition,
                SimulationTime = clock.SimulationTime,
                PhysicsTickId = clock.PhysicsTickId,
                RawWorldY = worldPosition.y,
                AltitudeMeters = altitude,
                GravityVector = profile.GravityVector,
                GravityMagnitude = profile.GravityMagnitude,
                EnvironmentalCoolingMultiplier =
                    profile.ThermalEnvironmentEnabled
                        ? profile.BaselineCoolingMultiplier
                        : 1f,
                CoolingReferenceAirspeed = profile.CoolingReferenceAirspeed,
                MaximumAirflowCoolingMultiplier =
                    profile.MaximumAirflowCoolingMultiplier,
                DominantZoneId = string.Empty,
                Zone0Id = string.Empty,
                Zone1Id = string.Empty,
                Zone2Id = string.Empty,
                Zone3Id = string.Empty
            };
            V3AtmosphereField.Sample(profile, altitude, ref sample);
            V3WindField.Sample(
                profile,
                worldPosition,
                clock.SimulationTime,
                ref sample);
            if (profile.EnvironmentZonesEnabled)
            {
                ZoneRegistry.Apply(ref sample);
            }
            if (sample.AtmosphereEnabled)
            {
                sample.SpeedOfSoundMetersPerSecond =
                    V3AtmosphereField.CalculateSpeedOfSound(
                        sample.AmbientTemperatureK);
                sample.DynamicViscosityPascalSeconds =
                    V3AtmosphereField.CalculateDynamicViscosity(
                        sample.AmbientTemperatureK);
            }
            sample.EnvironmentalCoolingMultiplier = Mathf.Clamp(
                sample.EnvironmentalCoolingMultiplier,
                profile.MinimumCoolingMultiplier,
                profile.MaximumCoolingMultiplier);
            return true;
        }

        public void SetClockState(double worldSimulationTime, long worldTickId)
        {
            simulationTime = System.Math.Max(0d, worldSimulationTime);
            physicsTickId = System.Math.Max(0L, worldTickId);
            clockInitialized = true;
        }

        public void ResetClockToConfiguredStart()
        {
            simulationTime = Mathf.Max(0f, initialSimulationTimeSeconds);
            physicsTickId = System.Math.Max(0L, initialPhysicsTickId);
            clockInitialized = true;
        }

        private void AdvanceClock(float deltaTime)
        {
            EnsureClockInitialized();
            simulationTime += Mathf.Max(0f, deltaTime);
            physicsTickId++;
        }

        private void EnsureClockInitialized()
        {
            if (!clockInitialized)
            {
                ResetClockToConfiguredStart();
            }
        }

        private void SynchronizeGravityIfRequested()
        {
            if (!Application.isPlaying || !isActiveAndEnabled ||
                !synchronizeUnityGravity || profile == null)
            {
                return;
            }
            if (!gravityWasSynchronized)
            {
                gravityBeforeSynchronization = Physics.gravity;
            }
            Physics.gravity = profile.GravityVector;
            gravityWasSynchronized = true;
        }

        private void ValidateRuntimeState()
        {
            if (!logValidationErrors)
            {
                return;
            }
            V3WorldQueryStatus status = V3WorldQueryService.Resolve(
                gameObject.scene,
                out _);
            if (status == V3WorldQueryStatus.DuplicateWorld)
            {
                Debug.LogError(
                    "Hovercraft V3 requires exactly one active World Simulation " +
                    $"root in scene '{gameObject.scene.name}'. Multiple roots are active.",
                    this);
            }
            else if (profile == null)
            {
                Debug.LogError(
                    "World Simulation root has no V3WorldProfile assigned.",
                    this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (profile == null || !profile.DebuggingEnabled)
            {
                return;
            }
            Vector3 origin = transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + profile.GravityVector);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                origin,
                origin + profile.GlobalWindDirection * profile.GlobalWindSpeed);
        }
    }
}
