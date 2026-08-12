using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public readonly struct V3PriorityPowerTelemetry
    {
        public V3PriorityPowerTelemetry(
            V3ActuatorPriority priority,
            int allocationRank,
            float requestedPower,
            float grantedPower)
        {
            Priority = priority;
            AllocationRank = allocationRank;
            RequestedPower = Mathf.Max(0f, requestedPower);
            GrantedPower = Mathf.Clamp(
                grantedPower,
                0f,
                RequestedPower);
        }

        public V3ActuatorPriority Priority { get; }
        public int AllocationRank { get; }
        public float RequestedPower { get; }
        public float GrantedPower { get; }
        public float ShedPower =>
            Mathf.Max(0f, RequestedPower - GrantedPower);
        public float GrantFraction =>
            RequestedPower <= 0.0001f
                ? 1f
                : Mathf.Clamp01(GrantedPower / RequestedPower);
        public bool IsLimited =>
            GrantedPower + 0.0001f < RequestedPower;
    }

    public readonly struct V3PartRuntimeTelemetry
    {
        public V3PartRuntimeTelemetry(
            string socketId,
            string stablePartId,
            string displayName,
            PartRole role,
            bool isConnector,
            float massKg,
            Vector3 craftLocalPosition,
            Quaternion craftLocalRotation,
            float requestedOutput,
            float actualOutput,
            float requestedPower,
            float grantedPower,
            float appliedForceN,
            Vector2 connectorDeflection,
            Vector2 connectorVelocity,
            float connectorLoad,
            float temperatureC,
            float normalizedTemperature,
            V3ThermalProtectionState thermalProtectionState,
            float thermalOutputLimit,
            bool isEnabled,
            bool isThermallyLockedOut)
        {
            SocketId = socketId ?? string.Empty;
            StablePartId = stablePartId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Role = role;
            IsConnector = isConnector;
            MassKg = Mathf.Max(0f, massKg);
            CraftLocalPosition = craftLocalPosition;
            CraftLocalRotation = craftLocalRotation;
            RequestedOutput = requestedOutput;
            ActualOutput = actualOutput;
            RequestedPower = Mathf.Max(0f, requestedPower);
            GrantedPower = Mathf.Clamp(
                grantedPower,
                0f,
                RequestedPower);
            AppliedForceN = Mathf.Max(0f, appliedForceN);
            ConnectorDeflection = connectorDeflection;
            ConnectorVelocity = connectorVelocity;
            ConnectorLoad = connectorLoad;
            TemperatureC = temperatureC;
            NormalizedTemperature = Mathf.Clamp01(normalizedTemperature);
            ThermalProtectionState = thermalProtectionState;
            ThermalOutputLimit = Mathf.Clamp01(thermalOutputLimit);
            IsEnabled = isEnabled;
            IsThermallyLockedOut = isThermallyLockedOut;
        }

        public string SocketId { get; }
        public string StablePartId { get; }
        public string DisplayName { get; }
        public PartRole Role { get; }
        public bool IsConnector { get; }
        public float MassKg { get; }
        public Vector3 CraftLocalPosition { get; }
        public Quaternion CraftLocalRotation { get; }
        public float RequestedOutput { get; }
        public float ActualOutput { get; }
        public float RequestedPower { get; }
        public float GrantedPower { get; }
        public float PowerGrantFraction =>
            RequestedPower <= 0.0001f
                ? 1f
                : Mathf.Clamp01(GrantedPower / RequestedPower);
        public float AppliedForceN { get; }
        public Vector2 ConnectorDeflection { get; }
        public Vector2 ConnectorVelocity { get; }
        public float ConnectorLoad { get; }
        public float TemperatureC { get; }
        public float NormalizedTemperature { get; }
        public V3ThermalProtectionState ThermalProtectionState { get; }
        public float ThermalOutputLimit { get; }
        public bool IsEnabled { get; }
        public bool IsThermallyLockedOut { get; }
    }

    public sealed class V3CraftTelemetrySnapshot
    {
        internal V3CraftTelemetrySnapshot(
            int sequence,
            float capturedFixedTimeSeconds,
            string buildName,
            string chassisStableId,
            string chassisDisplayName,
            float massKg,
            Vector3 centerOfMass,
            float chassisBaseMassKg,
            float installedPartsMassKg,
            Vector3 parityCenterOfMassCalibration,
            bool isRuntimeBuildValid,
            int runtimeBuildIssueCount,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Vector3 localVelocity,
            Vector3 localAngularVelocity,
            float speedKmh,
            long craftPhysicsTickId,
            string hoverConfigurationId,
            int hoverConfigurationVersion,
            float targetHoverHeight,
            float minimumHoverClearance,
            float maximumHoverRange,
            float hoverStrength,
            float hoverDamping,
            bool gravityCompensationEnabled,
            V3WorldEnvironmentSample environment,
            V3CraftAerodynamicState aerodynamics,
            V3PowerAllocationMode powerAllocationMode,
            float requestedSystemsPower,
            float grantedSystemsPower,
            float availablePropulsionPower,
            float requestedPropulsionPower,
            float grantedPropulsionPower,
            bool areSystemsPowerLimited,
            bool isPropulsionPowerLimited,
            float maximumTemperatureC,
            int hotPartCount,
            int overheatedPartCount,
            int lockedOutPartCount,
            int socketCount,
            PartCapability providedCapabilities,
            PartCapability requiredCapabilities,
            PartCapability missingCapabilities,
            V3PartRuntimeTelemetry[] parts,
            V3PriorityPowerTelemetry[] priorityPower)
        {
            Sequence = sequence;
            CapturedFixedTimeSeconds = capturedFixedTimeSeconds;
            BuildName = buildName ?? string.Empty;
            ChassisStableId = chassisStableId ?? string.Empty;
            ChassisDisplayName = chassisDisplayName ?? string.Empty;
            MassKg = massKg;
            CenterOfMass = centerOfMass;
            ChassisBaseMassKg = chassisBaseMassKg;
            InstalledPartsMassKg = installedPartsMassKg;
            ParityCenterOfMassCalibration =
                parityCenterOfMassCalibration;
            IsRuntimeBuildValid = isRuntimeBuildValid;
            RuntimeBuildIssueCount = runtimeBuildIssueCount;
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
            LocalVelocity = localVelocity;
            LocalAngularVelocity = localAngularVelocity;
            SpeedKmh = speedKmh;
            CraftPhysicsTickId = craftPhysicsTickId;
            HoverConfigurationId = hoverConfigurationId ?? string.Empty;
            HoverConfigurationVersion = hoverConfigurationVersion;
            TargetHoverHeight = targetHoverHeight;
            MinimumHoverClearance = minimumHoverClearance;
            MaximumHoverRange = maximumHoverRange;
            HoverStrength = hoverStrength;
            HoverDamping = hoverDamping;
            GravityCompensationEnabled = gravityCompensationEnabled;
            Environment = environment;
            Aerodynamics = aerodynamics;
            PowerAllocationMode = powerAllocationMode;
            RequestedSystemsPower = requestedSystemsPower;
            GrantedSystemsPower = grantedSystemsPower;
            AvailablePropulsionPower = availablePropulsionPower;
            RequestedPropulsionPower = requestedPropulsionPower;
            GrantedPropulsionPower = grantedPropulsionPower;
            AreSystemsPowerLimited = areSystemsPowerLimited;
            IsPropulsionPowerLimited = isPropulsionPowerLimited;
            MaximumTemperatureC = maximumTemperatureC;
            HotPartCount = hotPartCount;
            OverheatedPartCount = overheatedPartCount;
            LockedOutPartCount = lockedOutPartCount;
            SocketCount = socketCount;
            ProvidedCapabilities = providedCapabilities;
            RequiredCapabilities = requiredCapabilities;
            MissingCapabilities = missingCapabilities;
            Parts = new ReadOnlyCollection<V3PartRuntimeTelemetry>(
                parts ?? Array.Empty<V3PartRuntimeTelemetry>());
            PriorityPower =
                new ReadOnlyCollection<V3PriorityPowerTelemetry>(
                    priorityPower ??
                    Array.Empty<V3PriorityPowerTelemetry>());
        }

        public int Sequence { get; }
        public float CapturedFixedTimeSeconds { get; }
        public string BuildName { get; }
        public string ChassisStableId { get; }
        public string ChassisDisplayName { get; }
        public float MassKg { get; }
        public Vector3 CenterOfMass { get; }
        public float ChassisBaseMassKg { get; }
        public float InstalledPartsMassKg { get; }
        public Vector3 ParityCenterOfMassCalibration { get; }
        public bool IsRuntimeBuildValid { get; }
        public int RuntimeBuildIssueCount { get; }
        public Vector3 WorldPosition { get; }
        public Quaternion WorldRotation { get; }
        public Vector3 LocalVelocity { get; }
        public Vector3 LocalAngularVelocity { get; }
        public float SpeedKmh { get; }
        public long CraftPhysicsTickId { get; }
        public string HoverConfigurationId { get; }
        public int HoverConfigurationVersion { get; }
        public float TargetHoverHeight { get; }
        public float MinimumHoverClearance { get; }
        public float MaximumHoverRange { get; }
        public float HoverStrength { get; }
        public float HoverDamping { get; }
        public bool GravityCompensationEnabled { get; }
        public V3WorldEnvironmentSample Environment { get; }
        public V3CraftAerodynamicState Aerodynamics { get; }
        public V3PowerAllocationMode PowerAllocationMode { get; }
        public float RequestedSystemsPower { get; }
        public float GrantedSystemsPower { get; }
        public float AvailablePropulsionPower { get; }
        public float RequestedPropulsionPower { get; }
        public float GrantedPropulsionPower { get; }
        public float RequestedTotalPower =>
            RequestedSystemsPower + RequestedPropulsionPower;
        public float GrantedTotalPower =>
            GrantedSystemsPower + GrantedPropulsionPower;
        public bool AreSystemsPowerLimited { get; }
        public bool IsPropulsionPowerLimited { get; }
        public float MaximumTemperatureC { get; }
        public int HotPartCount { get; }
        public int OverheatedPartCount { get; }
        public int LockedOutPartCount { get; }
        public int SocketCount { get; }
        public PartCapability ProvidedCapabilities { get; }
        public PartCapability RequiredCapabilities { get; }
        public PartCapability MissingCapabilities { get; }
        public bool AreCapabilityRequirementsSatisfied =>
            MissingCapabilities == PartCapability.None;
        public IReadOnlyList<V3PartRuntimeTelemetry> Parts { get; }
        public IReadOnlyList<V3PriorityPowerTelemetry> PriorityPower { get; }

        public bool TryFindPart(
            string socketId,
            string stablePartId,
            out V3PartRuntimeTelemetry telemetry)
        {
            for (int i = 0; i < Parts.Count; i++)
            {
                V3PartRuntimeTelemetry candidate = Parts[i];
                if ((string.IsNullOrEmpty(socketId) ||
                     string.Equals(
                         candidate.SocketId,
                         socketId,
                         StringComparison.Ordinal)) &&
                    (string.IsNullOrEmpty(stablePartId) ||
                     string.Equals(
                         candidate.StablePartId,
                         stablePartId,
                         StringComparison.Ordinal)))
                {
                    telemetry = candidate;
                    return true;
                }
            }

            telemetry = default;
            return false;
        }
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class V3CraftTelemetryHub : MonoBehaviour
    {
        private readonly List<V3PartRuntimeTelemetry> parts =
            new List<V3PartRuntimeTelemetry>(20);
        private readonly List<V3PriorityPowerTelemetry> priorityPower =
            new List<V3PriorityPowerTelemetry>(5);

        private V3CraftRuntime runtime;
        private V3PowerDistributor power;
        private V3ThermalController thermal;
        private V3SocketRegistry socketRegistry;
        private V3CapabilityRegistry capabilityRegistry;
        private V3CraftMassCalculator massCalculator;
        private V3RuntimeBuildValidator runtimeValidator;
        private V3WorldEnvironmentProvider environmentProvider;
        private V3CraftAerodynamicsRuntime craftAerodynamics;
        private V3CraftMainframe mainframe;
        private bool schedulerDriven;

        public int Sequence { get; private set; }
        public string BuildName { get; private set; } = string.Empty;
        public string ChassisStableId { get; private set; } = string.Empty;
        public string ChassisDisplayName { get; private set; } = string.Empty;
        public float MassKg { get; private set; }
        public Vector3 CenterOfMass { get; private set; }
        public float ChassisBaseMassKg { get; private set; }
        public float InstalledPartsMassKg { get; private set; }
        public Vector3 ParityCenterOfMassCalibration { get; private set; }
        public bool IsRuntimeBuildValid { get; private set; }
        public int RuntimeBuildIssueCount { get; private set; }
        public Vector3 WorldPosition { get; private set; }
        public Quaternion WorldRotation { get; private set; } =
            Quaternion.identity;
        public Vector3 LocalVelocity { get; private set; }
        public Vector3 LocalAngularVelocity { get; private set; }
        public float SpeedKmh { get; private set; }
        public long CraftPhysicsTickId { get; private set; }
        public string HoverConfigurationId { get; private set; } = string.Empty;
        public int HoverConfigurationVersion { get; private set; }
        public float TargetHoverHeight { get; private set; }
        public float MinimumHoverClearance { get; private set; }
        public float MaximumHoverRange { get; private set; }
        public float HoverStrength { get; private set; }
        public float HoverDamping { get; private set; }
        public bool GravityCompensationEnabled { get; private set; }
        public V3WorldEnvironmentSample Environment { get; private set; }
        public V3CraftAerodynamicState Aerodynamics { get; private set; }
        public V3PowerAllocationMode PowerAllocationMode { get; private set; }
        public float RequestedSystemsPower { get; private set; }
        public float GrantedSystemsPower { get; private set; }
        public float AvailablePropulsionPower { get; private set; }
        public float RequestedPropulsionPower { get; private set; }
        public float GrantedPropulsionPower { get; private set; }
        public float RequestedTotalPower =>
            RequestedSystemsPower + RequestedPropulsionPower;
        public float GrantedTotalPower =>
            GrantedSystemsPower + GrantedPropulsionPower;
        public bool AreSystemsPowerLimited { get; private set; }
        public bool IsPropulsionPowerLimited { get; private set; }
        public float MaximumTemperatureC { get; private set; }
        public int HotPartCount { get; private set; }
        public int OverheatedPartCount { get; private set; }
        public int LockedOutPartCount { get; private set; }
        public int SocketCount { get; private set; }
        public PartCapability ProvidedCapabilities { get; private set; }
        public PartCapability RequiredCapabilities { get; private set; }
        public PartCapability MissingCapabilities { get; private set; }
        public IReadOnlyList<V3PartRuntimeTelemetry> Parts => parts;
        public IReadOnlyList<V3PriorityPowerTelemetry> PriorityPower =>
            priorityPower;

        public void Initialize(
            V3CraftRuntime craftRuntime,
            V3PowerDistributor powerDistributor,
            V3ThermalController thermalController,
            V3SocketRegistry runtimeSocketRegistry = null,
            V3CapabilityRegistry runtimeCapabilityRegistry = null,
            V3CraftMassCalculator runtimeMassCalculator = null,
            V3RuntimeBuildValidator buildValidator = null)
        {
            runtime = craftRuntime;
            power = powerDistributor;
            thermal = thermalController;
            socketRegistry = runtimeSocketRegistry;
            capabilityRegistry = runtimeCapabilityRegistry;
            massCalculator = runtimeMassCalculator;
            runtimeValidator = buildValidator;
            environmentProvider = runtime != null
                ? runtime.GetComponent<V3WorldEnvironmentProvider>()
                : null;
            craftAerodynamics = runtime != null
                ? runtime.GetComponent<V3CraftAerodynamicsRuntime>()
                : null;
            mainframe = runtime != null
                ? runtime.GetComponent<V3CraftMainframe>()
                : null;
            Sequence = 0;
            RefreshLiveTelemetry();
        }

        private void FixedUpdate()
        {
            if (!schedulerDriven)
            {
                RefreshLiveTelemetry();
            }
        }

        public void SetSchedulerDriven(bool value)
        {
            schedulerDriven = value;
        }

        public void RefreshLiveTelemetry()
        {
            Sequence++;
            parts.Clear();
            priorityPower.Clear();

            CaptureCraftIdentityAndMotion();
            CaptureEnvironmentAndAerodynamics();
            CapturePower();
            CaptureParts();
            CaptureThermal();
            CaptureRegistries();
            CaptureKernelDiagnostics();
        }

        public V3CraftTelemetrySnapshot CaptureSnapshot()
        {
            RefreshLiveTelemetry();
            return new V3CraftTelemetrySnapshot(
                Sequence,
                Time.fixedTime,
                BuildName,
                ChassisStableId,
                ChassisDisplayName,
                MassKg,
                CenterOfMass,
                ChassisBaseMassKg,
                InstalledPartsMassKg,
                ParityCenterOfMassCalibration,
                IsRuntimeBuildValid,
                RuntimeBuildIssueCount,
                WorldPosition,
                WorldRotation,
                LocalVelocity,
                LocalAngularVelocity,
                SpeedKmh,
                CraftPhysicsTickId,
                HoverConfigurationId,
                HoverConfigurationVersion,
                TargetHoverHeight,
                MinimumHoverClearance,
                MaximumHoverRange,
                HoverStrength,
                HoverDamping,
                GravityCompensationEnabled,
                Environment,
                Aerodynamics,
                PowerAllocationMode,
                RequestedSystemsPower,
                GrantedSystemsPower,
                AvailablePropulsionPower,
                RequestedPropulsionPower,
                GrantedPropulsionPower,
                AreSystemsPowerLimited,
                IsPropulsionPowerLimited,
                MaximumTemperatureC,
                HotPartCount,
                OverheatedPartCount,
                LockedOutPartCount,
                SocketCount,
                ProvidedCapabilities,
                RequiredCapabilities,
                MissingCapabilities,
                parts.ToArray(),
                priorityPower.ToArray());
        }

        private void CaptureCraftIdentityAndMotion()
        {
            CraftBuildDefinition build = runtime != null
                ? runtime.Build
                : null;
            ChassisDefinition chassis = build != null
                ? build.Chassis
                : null;
            BuildName = build != null ? build.name : string.Empty;
            V3HoverConfiguration hover = build != null
                ? build.HoverConfiguration
                : null;
            CraftPhysicsTickId = mainframe != null
                ? mainframe.CraftPhysicsTickId
                : environmentProvider != null
                    ? environmentProvider.CurrentCraftPhysicsTickId
                    : -1;
            HoverConfigurationId = hover != null ? hover.StableId : string.Empty;
            HoverConfigurationVersion = hover != null
                ? hover.ConfigurationVersion : 0;
            TargetHoverHeight = hover != null ? hover.TargetHoverHeight : 0f;
            MinimumHoverClearance = hover != null ? hover.MinimumClearance : 0f;
            MaximumHoverRange = hover != null
                ? hover.MaximumOperationalRange : 0f;
            HoverStrength = hover != null ? hover.HoverStrength : 0f;
            HoverDamping = hover != null ? hover.HoverDamping : 0f;
            GravityCompensationEnabled = hover != null &&
                hover.GravityCompensationEnabled;
            ChassisStableId =
                chassis != null ? chassis.StableId : string.Empty;
            ChassisDisplayName =
                chassis != null ? chassis.DisplayName : string.Empty;
            MassKg = runtime != null ? runtime.CalculatedMassKg : 0f;
            CenterOfMass =
                runtime != null
                    ? runtime.CalculatedCenterOfMass
                    : Vector3.zero;

            Rigidbody body = runtime != null
                ? runtime.RootRigidbody
                : null;
            if (body == null)
            {
                WorldPosition = Vector3.zero;
                WorldRotation = Quaternion.identity;
                LocalVelocity = Vector3.zero;
                LocalAngularVelocity = Vector3.zero;
                SpeedKmh = 0f;
                return;
            }

            Transform root = body.transform;
            WorldPosition = body.position;
            WorldRotation = body.rotation;
            LocalVelocity =
                root.InverseTransformDirection(body.linearVelocity);
            LocalAngularVelocity =
                root.InverseTransformDirection(body.angularVelocity);
            SpeedKmh = body.linearVelocity.magnitude * 3.6f;
        }

        private void CapturePower()
        {
            PowerAllocationMode =
                power != null
                    ? power.AllocationMode
                    : V3PowerAllocationMode.Balanced;
            RequestedSystemsPower =
                power != null ? power.RequestedSystemsPower : 0f;
            GrantedSystemsPower =
                power != null ? power.GrantedSystemsPower : 0f;
            AvailablePropulsionPower =
                power != null ? power.AvailablePropulsionPower : 0f;
            RequestedPropulsionPower =
                power != null ? power.RequestedPropulsionPower : 0f;
            GrantedPropulsionPower =
                power != null ? power.GrantedPropulsionPower : 0f;
            AreSystemsPowerLimited =
                power != null && power.AreSystemsPowerLimited;
            IsPropulsionPowerLimited =
                power != null && power.IsPowerLimited;

            if (power == null)
            {
                return;
            }

            IReadOnlyList<V3ActuatorPriority> order =
                power.GetAllocationOrder();
            for (int rank = 0; rank < order.Count; rank++)
            {
                V3ActuatorPriority priority = order[rank];
                priorityPower.Add(new V3PriorityPowerTelemetry(
                    priority,
                    rank,
                    power.GetRequestedPower(priority),
                    power.GetGrantedPower(priority)));
            }
        }

        private void CaptureEnvironmentAndAerodynamics()
        {
            if (environmentProvider == null && runtime != null)
            {
                environmentProvider =
                    runtime.GetComponent<V3WorldEnvironmentProvider>();
            }
            if (craftAerodynamics == null && runtime != null)
            {
                craftAerodynamics =
                    runtime.GetComponent<V3CraftAerodynamicsRuntime>();
            }
            Environment = environmentProvider != null
                ? environmentProvider.CurrentSample
                : default;
            Aerodynamics = craftAerodynamics != null
                ? craftAerodynamics.State
                : default;
        }

        private void CaptureParts()
        {
            if (runtime == null)
            {
                return;
            }

            Transform root = runtime.transform;
            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                PartDefinition definition = part != null
                    ? part.Definition
                    : null;
                if (part == null || definition == null)
                {
                    continue;
                }

                float appliedForce = 0f;
                Vector2 connectorDeflection = Vector2.zero;
                Vector2 connectorVelocity = Vector2.zero;
                float connectorLoad = 0f;
                if (part is RuntimeThrusterInstance thruster)
                {
                    appliedForce = thruster.CurrentAppliedForceN;
                }
                else if (part is RuntimeGimbalInstance gimbal)
                {
                    connectorDeflection = new Vector2(
                        gimbal.CurrentPitchDegrees,
                        gimbal.CurrentYawDegrees);
                    connectorVelocity = new Vector2(
                        gimbal.PitchVelocityDegreesPerSecond,
                        gimbal.YawVelocityDegreesPerSecond);
                    connectorLoad = gimbal.CurrentActuatorTorqueNm;
                }
                else if (part is RuntimeSpringMountInstance spring)
                {
                    connectorDeflection =
                        new Vector2(spring.DisplacementM, 0f);
                    connectorVelocity =
                        new Vector2(spring.VelocityMPerSecond, 0f);
                    connectorLoad = spring.CurrentEndpointLoadN;
                }

                parts.Add(new V3PartRuntimeTelemetry(
                    part.ParentSocketId,
                    definition.StableId,
                    definition.DisplayName,
                    definition.Role,
                    definition is ConnectorDefinition,
                    definition.Physical.massKg,
                    root.InverseTransformPoint(part.transform.position),
                    Quaternion.Inverse(root.rotation) *
                    part.transform.rotation,
                    part.RequestedOutput,
                    part.CurrentOutput,
                    part.RequestedPower,
                    part.GrantedPower,
                    appliedForce,
                    connectorDeflection,
                    connectorVelocity,
                    connectorLoad,
                    part.CurrentTemperatureC,
                    part.NormalizedTemperature,
                    part.ThermalProtectionState,
                    part.ThermalOutputLimit,
                    part.IsEnabled,
                    part.IsThermallyLockedOut));
            }
        }

        private void CaptureThermal()
        {
            thermal?.RefreshTelemetry();
            MaximumTemperatureC =
                thermal != null ? thermal.MaximumTemperatureC : 0f;
            HotPartCount =
                thermal != null ? thermal.HotPartCount : 0;
            OverheatedPartCount =
                thermal != null ? thermal.OverheatedPartCount : 0;
            LockedOutPartCount =
                thermal != null ? thermal.LockedOutPartCount : 0;
        }

        private void CaptureRegistries()
        {
            SocketCount =
                socketRegistry != null ? socketRegistry.Count : 0;
            ProvidedCapabilities =
                capabilityRegistry != null
                    ? capabilityRegistry.ProvidedCapabilities
                    : PartCapability.None;
            RequiredCapabilities =
                capabilityRegistry != null
                    ? capabilityRegistry.RequiredCapabilities
                    : PartCapability.None;
            MissingCapabilities =
                capabilityRegistry != null
                    ? capabilityRegistry.MissingCapabilities
                    : PartCapability.None;
        }

        private void CaptureKernelDiagnostics()
        {
            V3MassCalculationResult massResult =
                massCalculator != null ? massCalculator.Result : null;
            ChassisBaseMassKg =
                massResult != null ? massResult.ChassisBaseMassKg : 0f;
            InstalledPartsMassKg =
                massResult != null ? massResult.InstalledPartsMassKg : 0f;
            ParityCenterOfMassCalibration =
                massResult != null
                    ? massResult.ParityCenterOfMassCalibration
                    : Vector3.zero;
            IsRuntimeBuildValid =
                runtimeValidator != null && runtimeValidator.IsValid;
            RuntimeBuildIssueCount = 0;
            if (runtimeValidator?.AuthoringReport != null)
            {
                RuntimeBuildIssueCount +=
                    runtimeValidator.AuthoringReport.Issues.Count;
            }

            if (runtimeValidator?.RuntimeReport != null)
            {
                RuntimeBuildIssueCount +=
                    runtimeValidator.RuntimeReport.Issues.Count;
            }
        }
    }
}
