using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests
{
    public enum V3ControlledTestPhase
    {
        Setup,
        Warmup,
        Measured,
        Cooldown,
        Complete,
        Aborted
    }

    public enum V3ControlledTestCategory
    {
        PhysicsCore,
        Aerodynamics,
        SurfaceControl,
        Reset,
        Inertia,
        Authoring,
        FullTrack
    }

    public enum V3ControlledFixtureKind
    {
        None,
        FlatSurface,
        CleanSurfaceEdge,
        DescendingSurface,
        TorqueFixture,
        TrackTestScene
    }

    public enum V3ControlledFailureMode
    {
        SoftwareUnavailable,
        ComputerOffline,
        FirmwareUnavailable,
        SystemsPowerDenied,
        DeviceUnavailable,
        DeviceDamaged,
        ActuatorHeldNeutral,
        TaskBelowMinimumRate
    }

    public enum V3ControlledAssertionType
    {
        NumericRange,
        TargetTolerance,
        StateTransition,
        Deadline,
        EventOccurs,
        EventAbsent,
        ContactFreeResidual,
        TaskRate,
        BuildIdentity,
        Persistence,
        SignalReachesMinimum
    }

    public enum V3ControlledCommandMode
    {
        Scripted,
        TrackFollower
    }

    [Serializable]
    public sealed class V3ControlledInitialCondition
    {
        public Vector3 worldPosition = new Vector3(0f, 40f, 0f);
        public Vector3 worldEulerAngles;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public bool startSleeping;
        public bool requireNoInitialContact = true;
        public bool useTrackRelation;
        public float trackDistanceMeters;
        public string trackSectionId = string.Empty;
    }

    [Serializable]
    public sealed class V3ControlledCommandWindow
    {
        [Min(0f)] public float startSeconds;
        [Min(0f)] public float endSeconds = 1f;
        [Range(-1f, 1f)] public float throttle;
        [Range(-1f, 1f)] public float strafe;
        [Range(-1f, 1f)] public float yaw;
        [Range(-1f, 1f)] public float pitch;
        [Range(0f, 1f)] public float lift;
        [Range(0f, 1f)] public float downforce;
        public bool stabilizationEnabled = true;
        public bool gripBreaker;
        public bool emergencyOverload;

        public bool Contains(float elapsed)
        {
            return elapsed >= startSeconds && elapsed < endSeconds;
        }

        public V3PilotCommand ToPilotCommand()
        {
            return new V3PilotCommand
            {
                Throttle = Mathf.Clamp(throttle, -1f, 1f),
                Strafe = Mathf.Clamp(strafe, -1f, 1f),
                Yaw = Mathf.Clamp(yaw, -1f, 1f),
                Pitch = Mathf.Clamp(pitch, -1f, 1f),
                Lift = Mathf.Clamp01(lift),
                Downforce = Mathf.Clamp01(downforce),
                StabilizationEnabled = stabilizationEnabled,
                GripBreaker = gripBreaker,
                EmergencyOverload = emergencyOverload
            };
        }
    }

    [Serializable]
    public sealed class V3ControlledFailurePathRequest
    {
        public string targetId = string.Empty;
        public V3ControlledFailureMode failureMode;
        public string expectedMainframeState = string.Empty;
        public string expectedAuthorityLoss = string.Empty;
        public string expectedRecorderEvidence = string.Empty;
        public bool critical = true;
    }

    [Serializable]
    public sealed class V3ControlledRuntimeOverride
    {
        public bool disableChassisAerodynamics;
        public bool disableFinAerodynamics;
        [FormerlySerializedAs("requireTestOnlyBuild")]
        public bool warnWhenBuildIsNotTestOnly = true;
        public string rationale = string.Empty;

        public bool HasAnyOverride =>
            disableChassisAerodynamics || disableFinAerodynamics;
    }

    [Serializable]
    public sealed class V3ControlledTrackFollowerSettings
    {
        [Min(1f)] public float targetSpeedMetersPerSecond = 160f;
        [Min(1f)] public float minimumCurveSpeedMetersPerSecond = 80f;
        [Min(0.1f)] public float maximumCurveAcceleration = 55f;
        [Range(-1f, 1f)] public float minimumThrottle = -0.35f;
        [Range(-1f, 1f)] public float maximumThrottle = 0.75f;
        [Range(0f, 1f)] public float feedForwardThrottle = 0.18f;
        [Min(0f)] public float speedProportionalGain = 0.012f;
        [Min(1f)] public float minimumLookAheadMeters = 35f;
        [Min(0f)] public float lookAheadSeconds = 0.7f;
        [Min(1f)] public float maximumLookAheadMeters = 180f;
        [Min(1f)] public float curvaturePreviewMeters = 450f;
        [Min(1f)] public float yawFullScaleDegrees = 30f;
        [Min(1f)] public float pitchFullScaleDegrees = 30f;
        [Min(1f)] public float lateralFullScaleMeters = 45f;
        [Range(0f, 1f)] public float maximumStrafe = 0.4f;
        [Range(0f, 1f)] public float maximumPitch = 0.85f;
        [Min(1f)] public float reacquireDistanceMeters = 250f;
    }

    [Serializable]
    public sealed class V3ControlledMeasuredReset
    {
        public bool enabled;
        [Min(0f)] public float triggerSeconds = 1f;
        public string reason = "CONTROLLED RESET";
    }

    [Serializable]
    public sealed class V3ControlledTestAssertion
    {
        public string assertionId = string.Empty;
        public string description = string.Empty;
        public V3ControlledAssertionType type;
        public string signalId = string.Empty;
        public float windowStartSeconds;
        public float windowEndSeconds = float.MaxValue;
        public float minimum;
        public float maximum;
        public float target;
        [Min(0f)] public float tolerance;
        [Min(0f)] public float deadlineSeconds;
        public V3DiagnosticEventType eventType;
        [Min(0)] public int expectedEventCount = 1;
        public string[] stateSequence = Array.Empty<string>();
        public string expectedText = string.Empty;
        public string[] supportingChannels = Array.Empty<string>();
    }

    [Serializable]
    public sealed class V3ControlledOutputSettings
    {
        public string sessionNameTemplate = "{testId}_{utc}";
        public string outputFolder = "TestDriveReports/HovercraftV3/Controlled";
        public bool rawDataEnabled;
        public bool compactDataEnabled = true;
        public bool csvEnabled = true;
        public bool markdownEnabled = true;
        public string comparisonGroupId = string.Empty;
        public string baselineReferencePackage = string.Empty;
        public string calibrationDecisionId = string.Empty;
    }

    [CreateAssetMenu(
        menuName = "Hovercraft V3/Diagnostics/Controlled Test Definition",
        fileName = "V3_ControlledTest")]
    public sealed class V3ControlledTestDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string stableTestId =
            "test.v3.controlled.unnamed.01";
        [SerializeField] private string displayName = "Controlled Test";
        [TextArea(2, 5), SerializeField] private string description = string.Empty;
        [SerializeField] private V3ControlledTestCategory category;
        [Min(1), SerializeField] private int version = 1;
        [TextArea(2, 5), SerializeField] private string authoringNotes = string.Empty;
        [Min(0.01f), SerializeField] private float expectedDurationSeconds = 5f;
        [SerializeField] private int requiredSchemaVersion = 6;
        [SerializeField] private string[] tags = Array.Empty<string>();

        [Header("Default craft")]
        [SerializeField] private CraftBuildDefinition build;
        [SerializeField] private string requiredBuildStableId = string.Empty;
        [SerializeField] private string requiredBuildGuid = string.Empty;
        [SerializeField] private V3BuildOwnership requiredOwnership =
            V3BuildOwnership.GeneratedReference;
        [SerializeField] private V3ControlExecutionPath requiredControlPath =
            V3ControlExecutionPath.MainframeScheduled;
        [SerializeField] private bool expectLegacyFallbackAllowed;

        [Header("Scene and world")]
        [SerializeField] private string scenePath =
            "Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity";
        [SerializeField] private V3WorldProfile worldProfile;
        [SerializeField] private string requiredWorldRootStableId =
            "world.root.v3_tracktest";
        [SerializeField] private Vector3 expectedGravity =
            new Vector3(0f, -9.80665f, 0f);
        [SerializeField] private bool expectAtmosphere = true;
        [SerializeField] private bool expectWind;
        [SerializeField] private V3ControlledFixtureKind fixtureKind;
        [SerializeField] private GameObject fixturePrefab;

        [Header("Lifecycle")]
        [SerializeField] private V3ControlledInitialCondition initialCondition =
            new V3ControlledInitialCondition();
        [Min(0f), SerializeField] private float warmupSeconds = 0.25f;
        [Min(0.01f), SerializeField] private float measuredSeconds = 3f;
        [Min(0f), SerializeField] private float cooldownSeconds = 0.25f;
        [SerializeField] private bool resetBeforeSetup = true;
        [SerializeField] private V3ControlledRuntimeOverride runtimeOverride =
            new V3ControlledRuntimeOverride();
        [SerializeField] private V3ControlledMeasuredReset measuredReset =
            new V3ControlledMeasuredReset();
        [SerializeField] private V3ControlledFailurePathRequest[] failures =
            Array.Empty<V3ControlledFailurePathRequest>();
        [SerializeField] private V3ControlledCommandWindow[] commands =
            Array.Empty<V3ControlledCommandWindow>();
        [SerializeField] private V3ControlledCommandMode commandMode;
        [SerializeField] private V3ControlledTrackFollowerSettings trackFollower =
            new V3ControlledTrackFollowerSettings();
        [SerializeField] private V3ControlledTestAssertion[] assertions =
            Array.Empty<V3ControlledTestAssertion>();
        [SerializeField] private V3ControlledOutputSettings output =
            new V3ControlledOutputSettings();

        public string StableTestId => stableTestId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public string Description => description ?? string.Empty;
        public V3ControlledTestCategory Category => category;
        public int Version => version;
        public string AuthoringNotes => authoringNotes ?? string.Empty;
        public float ExpectedDurationSeconds => expectedDurationSeconds;
        public int RequiredSchemaVersion => requiredSchemaVersion;
        public string[] Tags => tags ?? Array.Empty<string>();
        public CraftBuildDefinition Build => build;
        public string RequiredBuildStableId => requiredBuildStableId ?? string.Empty;
        public string RequiredBuildGuid => requiredBuildGuid ?? string.Empty;
        public V3BuildOwnership RequiredOwnership => requiredOwnership;
        public V3ControlExecutionPath RequiredControlPath => requiredControlPath;
        public bool ExpectLegacyFallbackAllowed => expectLegacyFallbackAllowed;
        public string ScenePath => scenePath ?? string.Empty;
        public V3WorldProfile WorldProfile => worldProfile;
        public string RequiredWorldRootStableId => requiredWorldRootStableId ?? string.Empty;
        public Vector3 ExpectedGravity => expectedGravity;
        public bool ExpectAtmosphere => expectAtmosphere;
        public bool ExpectWind => expectWind;
        public V3ControlledFixtureKind FixtureKind => fixtureKind;
        public GameObject FixturePrefab => fixturePrefab;
        public V3ControlledInitialCondition InitialCondition =>
            initialCondition ??= new V3ControlledInitialCondition();
        public float WarmupSeconds => warmupSeconds;
        public float MeasuredSeconds => measuredSeconds;
        public float CooldownSeconds => cooldownSeconds;
        public bool ResetBeforeSetup => resetBeforeSetup;
        public V3ControlledRuntimeOverride RuntimeOverride =>
            runtimeOverride ??= new V3ControlledRuntimeOverride();
        public V3ControlledMeasuredReset MeasuredReset =>
            measuredReset ??= new V3ControlledMeasuredReset();
        public V3ControlledFailurePathRequest[] Failures =>
            failures ?? Array.Empty<V3ControlledFailurePathRequest>();
        public V3ControlledCommandWindow[] Commands =>
            commands ?? Array.Empty<V3ControlledCommandWindow>();
        public V3ControlledCommandMode CommandMode => commandMode;
        public V3ControlledTrackFollowerSettings TrackFollower =>
            trackFollower ??= new V3ControlledTrackFollowerSettings();
        public V3ControlledTestAssertion[] Assertions =>
            assertions ?? Array.Empty<V3ControlledTestAssertion>();
        public V3ControlledOutputSettings Output =>
            output ??= new V3ControlledOutputSettings();

        public V3PilotCommand CommandAt(float measuredElapsed)
        {
            V3PilotCommand command = new V3PilotCommand
            {
                StabilizationEnabled = true
            };
            V3ControlledCommandWindow[] windows = Commands;
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null && windows[i].Contains(measuredElapsed))
                    command = windows[i].ToPilotCommand();
            }
            return command;
        }

        public bool Validate(List<string> errors)
        {
            return ValidateForBuild(build, errors);
        }

        public bool ValidateForBuild(
            CraftBuildDefinition selectedBuild,
            List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (!V3ControlledTestRegistry.IsValidStableId(StableTestId))
                errors.Add("Stable test ID is missing or invalid: " + StableTestId);
            if (version < 1) errors.Add("Definition version must be at least 1.");
            if (requiredSchemaVersion != V3DiagnosticSchema.Version)
                errors.Add("Definition requires schema " + requiredSchemaVersion +
                    " but runtime schema is " + V3DiagnosticSchema.Version + ".");
            if (selectedBuild == null)
                errors.Add("A craft build must be selected for the controlled test.");
            if (string.IsNullOrWhiteSpace(ScenePath))
                errors.Add("Scene path is required.");
            if (measuredSeconds <= 0f)
                errors.Add("Measured duration must be greater than zero.");
            if (MeasuredReset.enabled &&
                (MeasuredReset.triggerSeconds < 0f ||
                 MeasuredReset.triggerSeconds >= measuredSeconds))
                errors.Add("Measured reset must occur inside the measured phase.");
            float previousStart = -1f;
            for (int i = 0; i < Commands.Length; i++)
            {
                V3ControlledCommandWindow command = Commands[i];
                if (command == null) continue;
                if (command.startSeconds < previousStart)
                    errors.Add("Command windows must be sorted by start time.");
                if (command.endSeconds <= command.startSeconds)
                    errors.Add("Command window " + i + " has no positive duration.");
                previousStart = command.startSeconds;
            }
            for (int i = 0; i < Assertions.Length; i++)
            {
                V3ControlledTestAssertion assertion = Assertions[i];
                if (assertion == null)
                {
                    errors.Add("Assertion " + i + " is null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(assertion.assertionId))
                    errors.Add("Assertion " + i + " has no stable ID.");
                if (RequiresSignal(assertion.type) &&
                    !V3ControlledSignalRegistry.IsSupported(assertion.signalId))
                    errors.Add("Assertion " + assertion.assertionId +
                        " uses unsupported signal " + assertion.signalId + ".");
            }
            if (commandMode == V3ControlledCommandMode.TrackFollower)
            {
                if (fixtureKind != V3ControlledFixtureKind.TrackTestScene)
                    errors.Add("Track-following command mode requires the track-test fixture.");
                if (TrackFollower.maximumThrottle < TrackFollower.minimumThrottle)
                    errors.Add("Track-follower maximum throttle must not be below its minimum.");
                if (TrackFollower.maximumLookAheadMeters <
                    TrackFollower.minimumLookAheadMeters)
                    errors.Add("Track-follower maximum look-ahead must not be below its minimum.");
            }
            return errors.Count == 0;
        }

        private static bool RequiresSignal(V3ControlledAssertionType type)
        {
            return type == V3ControlledAssertionType.NumericRange ||
                type == V3ControlledAssertionType.SignalReachesMinimum ||
                type == V3ControlledAssertionType.TargetTolerance ||
                type == V3ControlledAssertionType.Deadline ||
                type == V3ControlledAssertionType.ContactFreeResidual ||
                type == V3ControlledAssertionType.TaskRate;
        }

        public void Configure(
            string id,
            string name,
            V3ControlledTestCategory testCategory,
            CraftBuildDefinition craftBuild,
            string requiredScenePath,
            V3ControlledFixtureKind fixture,
            V3ControlledInitialCondition condition,
            float warmup,
            float measured,
            float cooldown,
            V3ControlledCommandWindow[] commandSequence,
            V3ControlledTestAssertion[] assertionSet,
            string comparisonGroup)
        {
            stableTestId = id ?? string.Empty;
            displayName = name ?? string.Empty;
            category = testCategory;
            build = craftBuild;
            requiredBuildStableId = craftBuild != null
                ? craftBuild.StableId : string.Empty;
            requiredBuildGuid = craftBuild != null
                ? craftBuild.BuildAssetGuid : string.Empty;
            requiredOwnership = craftBuild != null
                ? craftBuild.Ownership : V3BuildOwnership.GeneratedReference;
            requiredControlPath = V3ControlExecutionPath.MainframeScheduled;
            expectLegacyFallbackAllowed = craftBuild != null &&
                craftBuild.AllowLegacyPipelineFallback;
            scenePath = requiredScenePath ?? string.Empty;
            fixtureKind = fixture;
            initialCondition = condition ?? new V3ControlledInitialCondition();
            warmupSeconds = Mathf.Max(0f, warmup);
            measuredSeconds = Mathf.Max(0.01f, measured);
            cooldownSeconds = Mathf.Max(0f, cooldown);
            expectedDurationSeconds = warmupSeconds + measuredSeconds +
                cooldownSeconds;
            commands = commandSequence ?? Array.Empty<V3ControlledCommandWindow>();
            assertions = assertionSet ?? Array.Empty<V3ControlledTestAssertion>();
            output ??= new V3ControlledOutputSettings();
            output.comparisonGroupId = comparisonGroup ?? string.Empty;
        }

        public void ConfigureMeasuredReset(float triggerSeconds, string reason)
        {
            measuredReset ??= new V3ControlledMeasuredReset();
            measuredReset.enabled = true;
            measuredReset.triggerSeconds = Mathf.Max(0f, triggerSeconds);
            measuredReset.reason = string.IsNullOrWhiteSpace(reason)
                ? "CONTROLLED RESET"
                : reason;
        }

        public void ConfigureRuntimeOverride(
            bool disableChassisAero,
            string overrideRationale,
            bool disableFinAero = false)
        {
            runtimeOverride ??= new V3ControlledRuntimeOverride();
            runtimeOverride.disableChassisAerodynamics = disableChassisAero;
            runtimeOverride.disableFinAerodynamics = disableFinAero;
            runtimeOverride.warnWhenBuildIsNotTestOnly = true;
            runtimeOverride.rationale = overrideRationale ?? string.Empty;
        }

        public void ConfigureTrackFollower(
            V3ControlledTrackFollowerSettings settings)
        {
            commandMode = V3ControlledCommandMode.TrackFollower;
            trackFollower = settings ?? new V3ControlledTrackFollowerSettings();
        }

        public void ConfigureWorldRequirement(
            string requiredScenePath,
            string worldRootStableId,
            V3WorldProfile requiredWorldProfile)
        {
            scenePath = requiredScenePath ?? string.Empty;
            requiredWorldRootStableId = worldRootStableId ?? string.Empty;
            worldProfile = requiredWorldProfile;
            if (requiredWorldProfile != null)
            {
                expectedGravity = requiredWorldProfile.GravityVector;
                expectAtmosphere = requiredWorldProfile.AtmosphereEnabled;
                expectWind = requiredWorldProfile.WindEnabled;
            }
        }

        private void OnValidate()
        {
            version = Mathf.Max(1, version);
            requiredSchemaVersion = V3DiagnosticSchema.Version;
            warmupSeconds = Mathf.Max(0f, warmupSeconds);
            measuredSeconds = Mathf.Max(0.01f, measuredSeconds);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            expectedDurationSeconds = warmupSeconds + measuredSeconds +
                cooldownSeconds;
        }
    }

    public static class V3ControlledTestRegistry
    {
        public static bool IsValidStableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.EndsWith("."))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    return false;
            }
            return true;
        }

        public static bool ValidateUnique(
            IReadOnlyList<V3ControlledTestDefinition> definitions,
            List<string> errors)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                V3ControlledTestDefinition definition = definitions[i];
                if (definition == null) continue;
                string id = definition.StableTestId;
                if (owners.TryGetValue(id, out string existing))
                    errors.Add("Duplicate controlled test ID " + id +
                        ": " + existing + " and " + definition.name);
                else
                    owners.Add(id, definition.name);
                definition.Validate(errors);
            }
            return errors.Count == 0;
        }
    }

    public static class V3ControlledSignalRegistry
    {
        public static bool IsSupported(string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId)) return false;
            switch (signalId)
            {
                case "world.acceleration.x":
                case "world.acceleration.y":
                case "world.acceleration.z":
                case "world.acceleration.magnitude":
                case "world.speed":
                case "world.contactCount":
                case "track.distance":
                case "track.lateralOffset":
                case "track.verticalOffset":
                case "track.insideBounds":
                case "track.mappingConfidence":
                case "identity.dynamicsValid":
                case "identity.externalStateDiscontinuity":
                case "hover.captureAuthority":
                case "hover.bottomRequest":
                case "hover.roofRequest":
                case "forces.bottom":
                case "forces.roof":
                case "forces.aero":
                case "forces.fin":
                case "forces.propulsion":
                case "forces.residual":
                case "inertia.angularAccelerationError":
                    return true;
                default:
                    return signalId.StartsWith(
                        "task.", StringComparison.Ordinal) &&
                        signalId.EndsWith(
                            ".measuredRateHz", StringComparison.Ordinal);
            }
        }

        public static bool TryRead(
            ref V3DiagnosticSample sample,
            string signalId,
            out float value)
        {
            switch (signalId)
            {
                case "world.acceleration.x": value = sample.world.linearAcceleration.x; return true;
                case "world.acceleration.y": value = sample.world.linearAcceleration.y; return true;
                case "world.acceleration.z": value = sample.world.linearAcceleration.z; return true;
                case "world.acceleration.magnitude": value = sample.world.linearAcceleration.magnitude; return true;
                case "world.speed": value = sample.world.linearVelocity.magnitude; return true;
                case "world.contactCount": value = sample.world.contactCount; return true;
                case "track.distance": value = sample.identity.trackDistance; return true;
                case "track.lateralOffset": value = sample.track.signedLateralOffset; return true;
                case "track.verticalOffset": value = sample.track.signedVerticalOffset; return true;
                case "track.insideBounds": value = sample.track.insideTrackBounds ? 1f : 0f; return true;
                case "track.mappingConfidence": value = sample.track.mappingConfidence; return true;
                case "identity.dynamicsValid": value = sample.identity.dynamicsValid ? 1f : 0f; return true;
                case "identity.externalStateDiscontinuity": value = sample.identity.externalStateDiscontinuity ? 1f : 0f; return true;
                case "hover.captureAuthority": value = sample.belief.captureAuthorityMultiplier; return true;
                case "hover.bottomRequest": value = sample.belief.bottomRequestAfterAuthorityLimit; return true;
                case "hover.roofRequest": value = sample.belief.roofRequestAfterAuthorityLimit; return true;
                case "forces.bottom": value = sample.forces.hoverForce.magnitude; return true;
                case "forces.roof": value = sample.forces.roofForce.magnitude; return true;
                case "forces.aero": value = sample.forces.aerodynamicForce.magnitude; return true;
                case "forces.propulsion": value = sample.forces.propulsionForce.magnitude; return true;
                case "forces.fin":
                    value = 0f;
                    for (int i = 0; i < sample.deviceCount; i++)
                        value += Mathf.Max(0f, sample.devices[i].finForceN);
                    return true;
                case "forces.residual": value = sample.forces.residualForce.magnitude; return true;
                case "inertia.angularAccelerationError": value = sample.forces.angularAccelerationError.magnitude; return true;
            }
            if (signalId != null && signalId.StartsWith("task.") &&
                signalId.EndsWith(".measuredRateHz"))
            {
                string role = signalId.Substring(5,
                    signalId.Length - 5 - ".measuredRateHz".Length);
                for (int i = 0; i < sample.taskCount; i++)
                {
                    if (string.Equals(sample.tasks[i].role, role,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        value = sample.tasks[i].measuredRateHz;
                        return true;
                    }
                }
            }
            value = 0f;
            return false;
        }
    }

    [Serializable]
    public sealed class V3ControlledAssertionResult
    {
        public string assertionId;
        public bool passed;
        public string expected;
        public string measured;
        public float tolerance;
        public string sampleTimeRange;
        public string[] supportingChannels;
        public string evidenceFile;
    }

    [Serializable]
    public sealed class V3ControlledTestResult
    {
        public string testId;
        public int testVersion;
        public string sessionId;
        public string buildId;
        public string buildGuid;
        public string buildOwnership;
        public string controlPath;
        public string comparisonGroupId;
        public V3ControlledTestPhase finalPhase;
        public bool passed;
        public string abortReason;
        public string utcStarted;
        public string utcEnded;
        public double runnerOverheadMilliseconds;
        public int droppedSamples;
        public int droppedEvents;
        public string recorderOutputDirectory;
        public V3ControlledAssertionResult[] assertions;
        public string[] setupWarnings;
    }

    [Serializable]
    public sealed class V3ControlledRecorderMetadata
    {
        public string testId;
        public int testVersion;
        public string expectedBuildId;
        public string expectedBuildGuid;
        public string expectedOwnership;
        public string expectedControlPath;
        public string initialConditionJson;
        public string controlledFailuresJson;
        public string expectedActiveSystems;
        public string assertionSetId;
        public string comparisonGroupId;
        public string calibrationDecisionId;
    }

    [CreateAssetMenu(
        menuName = "Hovercraft V3/Diagnostics/Calibration Decision",
        fileName = "V3_CalibrationDecision")]
    public sealed class V3CalibrationDecision : ScriptableObject
    {
        public string decisionId = "calibration.v3.unset.01";
        public string dateUtc = string.Empty;
        public string systemDomain = string.Empty;
        public string sourceTestGroup = string.Empty;
        [TextArea] public string oldValue = string.Empty;
        [TextArea] public string newValue = string.Empty;
        [TextArea] public string expectedEffect = string.Empty;
        [TextArea] public string measuredEffect = string.Empty;
        public bool accepted;
        [TextArea] public string rationale = string.Empty;
        public UnityEngine.Object targetAsset;
        public string author = string.Empty;
        public string[] reportPackageReferences = Array.Empty<string>();
    }
}
