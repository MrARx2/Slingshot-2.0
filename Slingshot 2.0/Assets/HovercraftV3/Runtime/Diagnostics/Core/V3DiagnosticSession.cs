using System;
using System.Collections.Generic;
using Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests;
using TrackGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [Serializable]
    public sealed class V3DiagnosticCaptureCapacities
    {
        [Min(0)] public int sensors = 12;
        [Min(0)] public int tasks = 24;
        [Min(0)] public int observations = 32;
        [Min(0)] public int requests = 24;
        [Min(0)] public int devices = 48;
        [Min(1)] public int events = 512;
    }

    public sealed class V3DiagnosticContext
    {
        public V3CraftRuntime Craft { get; private set; }
        public Rigidbody Body { get; private set; }
        public V3CraftMainframe Mainframe { get; private set; }
        public V3PilotInputAdapter PilotInput { get; private set; }
        public TrackGenerator TrackGenerator { get; private set; }
        public V3WorldEnvironmentProvider EnvironmentProvider { get; private set; }
        public V3CraftAerodynamicsRuntime Aerodynamics { get; private set; }
        public Transform CraftTransform => Craft != null ? Craft.transform : null;
        public string SceneName => SceneManager.GetActiveScene().name;

        public bool Bind(
            V3CraftRuntime craft,
            V3PilotInputAdapter pilotInput,
            TrackGenerator trackGenerator)
        {
            Craft = craft;
            Body = craft != null ? craft.RootRigidbody : null;
            Mainframe = craft != null
                ? craft.GetComponent<V3CraftMainframe>()
                : null;
            EnvironmentProvider = craft != null
                ? craft.GetComponent<V3WorldEnvironmentProvider>()
                : null;
            Aerodynamics = craft != null
                ? craft.GetComponent<V3CraftAerodynamicsRuntime>()
                : null;
            PilotInput = pilotInput;
            TrackGenerator = trackGenerator;
            return Craft != null && Body != null;
        }
    }

    [Serializable]
    public sealed class V3DiagnosticInstalledPartSnapshot
    {
        public string stableId;
        public string displayName;
        public string manufacturer;
        public string role;
        public string socketId;
        public float massKg;
        public Vector3 localCenterOfMass;
        public bool requiresPower;
        public bool requiresData;
    }

    [Serializable]
    public sealed class V3DiagnosticCraftSnapshot
    {
        public string buildStableId;
        public string buildAssetGuid;
        public string buildOwnership;
        public string trackTestPreset;
        public string controlPath;
        public string buildDisplayName;
        public string manufacturer;
        public string vehicleClass;
        public string layout;
        public string chassisStableId;
        public string chassisDisplayName;
        public float calculatedMassKg;
        public Vector3 calculatedCenterOfMass;
        public Vector3 rigidbodyCenterOfMass;
        public Vector3 rigidbodyInertiaTensor;
        public Quaternion rigidbodyInertiaTensorRotation;
        public bool automaticCenterOfMass;
        public bool automaticInertiaTensor;
        public int installedPartCount;
        public V3DiagnosticInstalledPartSnapshot[] installedParts;
        public int sensorCount;
        public int computerCount;
        public int schedulerTaskCount;
        public string mainframeDefinition;
        public string routerProfile;
        public string hoverConfigurationId;
        public int hoverConfigurationVersion;
        public float targetHoverHeight;
        public float minimumHoverClearance;
        public float maximumHoverRange;
        public float nearHoverRange;
        public float captureRange;
        public float fallbackRange;
        public float automaticHoverAuthorityLimit;
        public float automaticRoofAuthorityLimit;
        public float manualAuthorityLimit;
        public float hoverStrength;
        public float hoverDamping;
        public bool gravityCompensationEnabled;
        public float gravityCompensationMultiplier;
        public float maximumCompressionMeters;
        public float maximumExtensionMeters;
        public float surfaceCurvatureFeedForward;
        public float surfaceTrackingResponse;
        public float maximumSurfaceTrackingAcceleration;
        public float roofCaptureStrength;
        public float roofCaptureDamping;
        public float maximumRoofCaptureAcceleration;
        public float roofCaptureDeadband;
    }

    [Serializable]
    public sealed class V3DiagnosticWorldSnapshot
    {
        public string sceneName;
        public Vector3 gravity;
        public float fixedDeltaTime;
        public float maximumDeltaTime;
        public int defaultSolverIterations;
        public int defaultSolverVelocityIterations;
        public float defaultContactOffset;
        public float bounceThreshold;
        public float sleepThreshold;
        public string physicsSimulationMode;
        public int trackInstanceId;
        public int trackGenerationRevision;
        public int trackSectionCount;
        public string trackRootName;
        public bool hasWorldSimulation;
        public string worldRootId;
        public string worldProfileId;
        public string worldProfileName;
        public int worldConfigurationVersion;
        public float referenceWorldY;
        public float seaLevelTemperatureC;
        public float seaLevelPressurePa;
        public float seaLevelDensity;
        public string atmosphereMode;
        public float lapseRateKPerMeter;
        public Vector3 profileGravity;
        public Vector3 globalWind;
        public float gustStrength;
        public float turbulenceStrength;
        public float coolingMultiplier;
        public int windSeed;
        public int environmentZoneCount;
    }

    [Serializable]
    public sealed class V3DiagnosticSessionManifest
    {
        public int schemaVersion;
        public string schemaVersionText;
        public string sessionId;
        public string sessionLabel;
        public string profile;
        public int physicsTickStride;
        public float nominalSampleRateHz;
        public float eventPreContextSeconds;
        public float eventPostContextSeconds;
        public string[] enabledSources;
        public string status;
        public string utcStarted;
        public string utcEnded;
        public string unityVersion;
        public string platform;
        public string sceneName;
        public string craftBuildStableId;
        public string craftBuildAssetGuid;
        public string craftBuildOwnership;
        public string trackTestPreset;
        public string controlPath;
        public string controlledTestId;
        public int controlledTestVersion;
        public string controlledTestPhase;
        public string expectedBuildId;
        public string expectedBuildGuid;
        public string expectedBuildOwnership;
        public string expectedControlPath;
        public string controlledInitialConditionJson;
        public string controlledFailuresJson;
        public string expectedActiveSystems;
        public string assertionSetId;
        public string comparisonGroupId;
        public string calibrationDecisionId;
        public int sampleCapacity;
        public int sampleCount;
        public int droppedSampleCount;
        public int droppedEventCount;
        public int eventCount;
        public float requestedDurationSeconds;
        public double capturedDurationSeconds;
        public double recordingOverheadMilliseconds;
        public double exportDurationMilliseconds;
        public int dataWarningCount;
        public string[] dataWarnings;
        public string binaryByteOrder;
        public string binaryChecksum;
        public string outputDirectory;
    }

    [Serializable]
    public sealed class V3DiagnosticSession
    {
        [SerializeField] private V3DiagnosticSessionManifest manifest;
        [SerializeField] private V3DiagnosticCraftSnapshot craftSnapshot;
        [SerializeField] private V3DiagnosticWorldSnapshot worldSnapshot;
        [SerializeField] private V3TrackDiagnosticSnapshot trackSnapshot;
        [SerializeField] private V3DiagnosticChannelRegistry channels;
        [SerializeField] private V3DiagnosticSample[] samples;
        [SerializeField] private int sampleCount;
        [SerializeField] private V3DiagnosticEvent[] events;
        [SerializeField] private int eventCount;
        private readonly List<string> dataWarnings = new List<string>(16);

        public V3DiagnosticSessionManifest Manifest => manifest;
        public V3DiagnosticCraftSnapshot CraftSnapshot => craftSnapshot;
        public V3DiagnosticWorldSnapshot WorldSnapshot => worldSnapshot;
        public V3TrackDiagnosticSnapshot TrackSnapshot => trackSnapshot;
        public V3DiagnosticChannelRegistry Channels => channels;
        public V3DiagnosticSample[] Samples => samples;
        public int SampleCount => sampleCount;
        public V3DiagnosticEvent[] Events => events;
        public int EventCount => eventCount;
        public IReadOnlyList<string> DataWarnings => dataWarnings;

        public void Initialize(
            string sessionId,
            string label,
            V3DiagnosticRecordingProfile profile,
            int sampleCapacity,
            float requestedDurationSeconds,
            V3DiagnosticCaptureCapacities capacities,
            V3DiagnosticCraftSnapshot craft,
            V3DiagnosticWorldSnapshot world,
            int physicsTickStride = 1,
            float eventPreContextSeconds = 2f,
            float eventPostContextSeconds = 3f)
        {
            capacities ??= new V3DiagnosticCaptureCapacities();
            int safeStride = Mathf.Max(1, physicsTickStride);
            manifest = new V3DiagnosticSessionManifest
            {
                schemaVersion = V3DiagnosticSchema.Version,
                schemaVersionText = V3DiagnosticSchema.VersionText,
                sessionId = sessionId,
                sessionLabel = label ?? string.Empty,
                profile = profile.ToString(),
                physicsTickStride = safeStride,
                nominalSampleRateHz = world != null && world.fixedDeltaTime > 0f
                    ? 1f / (world.fixedDeltaTime * safeStride) : 0f,
                eventPreContextSeconds = Mathf.Max(0f, eventPreContextSeconds),
                eventPostContextSeconds = Mathf.Max(0f, eventPostContextSeconds),
                status = "Recording",
                utcStarted = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                sceneName = world != null ? world.sceneName : string.Empty,
                craftBuildStableId = craft != null ? craft.buildStableId : string.Empty,
                craftBuildAssetGuid = craft != null ? craft.buildAssetGuid : string.Empty,
                craftBuildOwnership = craft != null ? craft.buildOwnership : string.Empty,
                trackTestPreset = craft != null ? craft.trackTestPreset : string.Empty,
                controlPath = craft != null ? craft.controlPath : string.Empty,
                sampleCapacity = Mathf.Max(1, sampleCapacity),
                requestedDurationSeconds = Mathf.Max(0f, requestedDurationSeconds),
                binaryByteOrder = BitConverter.IsLittleEndian ? "little-endian" : "big-endian"
            };
            craftSnapshot = craft;
            worldSnapshot = world;
            channels = new V3DiagnosticChannelRegistry();
            channels.RegisterCanonicalChannels();
            if (safeStride > 1)
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    if (channels.Channels[i].samplingMode ==
                        V3DiagnosticSamplingMode.EveryPhysicsTick)
                    {
                        channels.Channels[i].samplingMode =
                            V3DiagnosticSamplingMode.ReducedRate;
                    }
                }
            }
            samples = new V3DiagnosticSample[manifest.sampleCapacity];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i].Prepare(
                    capacities.sensors,
                    capacities.tasks,
                    capacities.observations,
                    capacities.requests,
                    capacities.devices);
            }

            events = new V3DiagnosticEvent[Mathf.Max(1, capacities.events)];
            sampleCount = 0;
            eventCount = 0;
            dataWarnings.Clear();
        }

        public void SetEnabledSources(IReadOnlyList<IV3DiagnosticSource> sources)
        {
            int count = sources != null ? sources.Count : 0;
            manifest.enabledSources = new string[count];
            for (int i = 0; i < count; i++)
                manifest.enabledSources[i] = sources[i].SourceId;
        }

        public void SetControlledTestMetadata(
            V3ControlledRecorderMetadata metadata,
            V3ControlledTestPhase phase)
        {
            if (manifest == null || metadata == null) return;
            manifest.controlledTestId = metadata.testId ?? string.Empty;
            manifest.controlledTestVersion = metadata.testVersion;
            manifest.controlledTestPhase = phase.ToString();
            manifest.expectedBuildId = metadata.expectedBuildId ?? string.Empty;
            manifest.expectedBuildGuid = metadata.expectedBuildGuid ?? string.Empty;
            manifest.expectedBuildOwnership = metadata.expectedOwnership ?? string.Empty;
            manifest.expectedControlPath = metadata.expectedControlPath ?? string.Empty;
            manifest.controlledInitialConditionJson =
                metadata.initialConditionJson ?? string.Empty;
            manifest.controlledFailuresJson =
                metadata.controlledFailuresJson ?? string.Empty;
            manifest.expectedActiveSystems =
                metadata.expectedActiveSystems ?? string.Empty;
            manifest.assertionSetId = metadata.assertionSetId ?? string.Empty;
            manifest.comparisonGroupId = metadata.comparisonGroupId ?? string.Empty;
            manifest.calibrationDecisionId =
                metadata.calibrationDecisionId ?? string.Empty;
        }

        public void SetControlledTestPhase(V3ControlledTestPhase phase)
        {
            if (manifest != null) manifest.controlledTestPhase = phase.ToString();
        }

        public void SetActualControlPath(V3ControlExecutionPath path)
        {
            string value = path.ToString();
            if (manifest != null) manifest.controlPath = value;
            if (craftSnapshot != null) craftSnapshot.controlPath = value;
        }

        public bool TryBeginSample(out int index)
        {
            if (samples == null || sampleCount >= samples.Length)
            {
                if (manifest != null)
                {
                    manifest.droppedSampleCount++;
                }
                index = -1;
                return false;
            }

            index = sampleCount++;
            V3DiagnosticSample slot = samples[index];
            slot.Prepare(
                slot.sensors != null ? slot.sensors.Length : 0,
                slot.tasks != null ? slot.tasks.Length : 0,
                slot.observations != null ? slot.observations.Length : 0,
                slot.requests != null ? slot.requests.Length : 0,
                slot.devices != null ? slot.devices.Length : 0);
            samples[index] = slot;
            return true;
        }

        public ref V3DiagnosticSample GetSample(int index)
        {
            return ref samples[index];
        }

        public bool TryAddEvent(V3DiagnosticEvent value)
        {
            if (value == null)
            {
                return false;
            }
            if (events == null || eventCount >= events.Length)
            {
                if (manifest != null) manifest.droppedEventCount++;
                AddDataWarning(
                    "The bounded event buffer reached capacity; additional events were explicitly dropped.");
                return false;
            }
            events[eventCount++] = value;
            return true;
        }

        public void AddDataWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning) || dataWarnings.Contains(warning))
            {
                return;
            }
            dataWarnings.Add(warning);
        }

        public void SetTrackSnapshot(V3TrackDiagnosticSnapshot value)
        {
            trackSnapshot = value;
        }

        public void Complete(double capturedDurationSeconds, double overheadMilliseconds)
        {
            for (int i = 0; i < eventCount; i++)
            {
                if (events[i] == null) continue;
                events[i].preEventStartSample = Math.Max(
                    0L, events[i].preEventStartSample);
                events[i].postEventEndSample = Math.Min(
                    Math.Max(0, sampleCount - 1),
                    events[i].postEventEndSample);
            }
            manifest.status = "Complete";
            manifest.utcEnded = DateTime.UtcNow.ToString("O");
            manifest.sampleCount = sampleCount;
            manifest.eventCount = eventCount;
            manifest.capturedDurationSeconds = Math.Max(0d, capturedDurationSeconds);
            manifest.recordingOverheadMilliseconds = Math.Max(0d, overheadMilliseconds);
            manifest.dataWarningCount = dataWarnings.Count;
            manifest.dataWarnings = dataWarnings.ToArray();
        }

        public void SetExportResult(
            string outputDirectory,
            double exportDurationMilliseconds,
            string binaryChecksum)
        {
            manifest.outputDirectory = outputDirectory ?? string.Empty;
            manifest.exportDurationMilliseconds =
                Math.Max(0d, exportDurationMilliseconds);
            manifest.binaryChecksum = binaryChecksum ?? string.Empty;
        }
    }
}
