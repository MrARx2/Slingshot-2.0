using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    internal struct V3DiagnosticDeviceBinding
    {
        public RuntimePartInstance part;
        public RuntimeThrusterInstance thruster;
        public RuntimeAerodynamicFinInstance fin;
        public RuntimeGimbalInstance gimbal;
        public RuntimeSpringMountInstance spring;
        public RuntimeRotaryActuatorInstance rotary;
        public string deviceId;
        public string partId;
        public string displayName;
        public string role;
        public string manufacturer;
        public string socketId;
        public string connectorId;
        public string deviceType;
    }

    public sealed class V3ActuatorExecutionRecorder : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;
        private V3DiagnosticSession session;
        private V3ActuatorCommandRouter router;
        private V3DiagnosticDeviceBinding[] devices =
            Array.Empty<V3DiagnosticDeviceBinding>();
        private V3ActuatorRouterEntrySnapshot[] routerEntries =
            Array.Empty<V3ActuatorRouterEntrySnapshot>();
        private bool overflowWarned;

        public string SourceId => "device_execution";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
            router = value != null && value.Craft != null
                ? value.Craft.GetComponent<V3ActuatorCommandRouter>() : null;
        }

        public void OnSessionStarted(V3DiagnosticSession value)
        {
            session = value;
            overflowWarned = false;
            IReadOnlyList<RuntimePartInstance> parts =
                context != null && context.Craft != null
                    ? context.Craft.InstalledParts : null;
            int count = parts != null ? parts.Count : 0;
            devices = new V3DiagnosticDeviceBinding[count];
            for (int i = 0; i < count; i++)
            {
                RuntimePartInstance part = parts[i];
                PartDefinition definition = part != null ? part.Definition : null;
                string socketId = part != null ? part.ParentSocketId : string.Empty;
                string stableId = definition != null ? definition.StableId : string.Empty;
                devices[i] = new V3DiagnosticDeviceBinding
                {
                    part = part,
                    thruster = part as RuntimeThrusterInstance,
                    fin = part as RuntimeAerodynamicFinInstance,
                    gimbal = part as RuntimeGimbalInstance,
                    spring = part as RuntimeSpringMountInstance,
                    rotary = part as RuntimeRotaryActuatorInstance,
                    deviceId = string.IsNullOrEmpty(socketId)
                        ? stableId : socketId + ":" + stableId,
                    partId = stableId,
                    displayName = definition != null
                        ? definition.DisplayName : string.Empty,
                    role = definition != null
                        ? RoleName(definition.Role) : "Unknown",
                    manufacturer = definition != null &&
                        definition.Manufacturer != null
                            ? definition.Manufacturer.DisplayName : string.Empty,
                    socketId = socketId,
                    connectorId = FindConnectorId(parts, part, socketId),
                    deviceType = DeviceType(part)
                };
            }
            routerEntries = new V3ActuatorRouterEntrySnapshot[
                Mathf.Max(1, router != null ? router.ThrusterCount : 0)];
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            int routerCount = router != null
                ? router.CopyEntrySnapshots(routerEntries) : 0;
            int capacity = sample.devices != null ? sample.devices.Length : 0;
            int count = Mathf.Min(devices.Length, capacity);
            sample.deviceCount = count;
            Vector3 centerOfMass = context != null && context.Body != null
                ? context.Body.worldCenterOfMass : Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                V3DiagnosticDeviceBinding binding = devices[i];
                RuntimePartInstance part = binding.part;
                if (part == null) continue;
                V3ActuatorRouterEntrySnapshot routerEntry = default;
                bool hasRouterEntry = FindRouterEntry(
                    binding.thruster, routerCount, out routerEntry);
                Vector3 force = Vector3.zero;
                Vector3 origin = part.transform.position;
                if (binding.thruster != null)
                {
                    force = binding.thruster.CurrentWorldForce;
                    origin = binding.thruster.WorldForceOrigin;
                }
                else if (binding.fin != null)
                {
                    force = binding.fin.CurrentWorldAerodynamicForce;
                    V3AerodynamicFinDefinition definition =
                        binding.fin.FinDefinition;
                    origin = definition != null
                        ? binding.fin.transform.TransformPoint(
                            definition.LocalCenterOfPressure)
                        : binding.fin.transform.position;
                }
                float routerCommand = hasRouterEntry
                    ? routerEntry.Combined : 0f;
                Vector3 requestedDirection = binding.thruster != null
                    ? binding.thruster.WorldForceDirection.normalized
                    : Vector3.zero;
                float requestedForceMagnitude =
                    CalculateRequestedForceMagnitude(
                        binding.thruster, routerCommand);
                Vector3 requestedForce =
                    requestedDirection * requestedForceMagnitude;
                float authorityCap = CalculateAuthorityCap(binding.thruster);
                float allocationPercentage = Mathf.Abs(routerCommand) > 0.0001f
                    ? Mathf.Clamp01(Mathf.Abs(part.CurrentOutput / routerCommand))
                    : 0f;
                sample.devices[i] = new V3DeviceDiagnosticRecord
                {
                    deviceId = binding.deviceId,
                    partId = binding.partId,
                    displayName = binding.displayName,
                    role = binding.role,
                    manufacturer = binding.manufacturer,
                    socketId = binding.socketId,
                    connectorId = binding.connectorId,
                    deviceType = binding.deviceType,
                    requestedOutput = part.RequestedOutput,
                    requestedDirection = requestedDirection,
                    requestedForce = requestedForce,
                    requestedTorqueContribution = Vector3.Cross(
                        origin - centerOfMass, requestedForce),
                    requestedOrientation = part.transform.rotation,
                    sourceChannelMask = hasRouterEntry
                        ? SourceChannelMask(routerEntry) : 0,
                    allocatorCommand = routerCommand,
                    authorityCap = authorityCap,
                    allocationPercentage = allocationPercentage,
                    selected = Mathf.Abs(routerCommand) > 0.0001f,
                    limitingReason = LimitingReason(
                        part, routerCommand, hasRouterEntry, routerEntry),
                    routerDriveRequest = hasRouterEntry ? routerEntry.Drive : 0f,
                    routerHoverRequest = hasRouterEntry ? routerEntry.BaseHover : 0f,
                    routerStabilizationRequest = hasRouterEntry
                        ? routerEntry.Stabilization : 0f,
                    routerVectoringRequest = hasRouterEntry
                        ? routerEntry.Vectoring : 0f,
                    routerManualRequest = hasRouterEntry ? routerEntry.Manual : 0f,
                    routerCombinedRequest = hasRouterEntry ? routerEntry.Combined : 0f,
                    actualOutput = part.CurrentOutput,
                    requestedPower = part.RequestedPower,
                    grantedPower = part.GrantedPower,
                    grantFraction = part.RequestedPower <= 0.0001f
                        ? 1f : Mathf.Clamp01(part.GrantedPower / part.RequestedPower),
                    firmwareId = hasRouterEntry
                        ? routerEntry.FirmwareId : string.Empty,
                    firmwareRequestedRateHz = hasRouterEntry
                        ? routerEntry.FirmwareRequestedRateHz : 0f,
                    firmwareGrantedRateHz = hasRouterEntry
                        ? routerEntry.FirmwareGrantedRateHz : 0f,
                    firmwareMinimumRateHz = hasRouterEntry
                        ? routerEntry.FirmwareMinimumRateHz : 0f,
                    firmwareSafeState = hasRouterEntry &&
                        routerEntry.FirmwareSafeState,
                    actualForceMagnitudeN = force.magnitude,
                    actualForce = force,
                    forceApplicationPoint = origin,
                    torqueContribution = Vector3.Cross(origin - centerOfMass, force),
                    gimbalPitchDegrees = binding.gimbal != null
                        ? binding.gimbal.CurrentPitchDegrees : 0f,
                    gimbalYawDegrees = binding.gimbal != null
                        ? binding.gimbal.CurrentYawDegrees : 0f,
                    gimbalTargetPitchDegrees = binding.gimbal != null
                        ? binding.gimbal.TargetPitchDegrees : 0f,
                    gimbalTargetYawDegrees = binding.gimbal != null
                        ? binding.gimbal.TargetYawDegrees : 0f,
                    gimbalPitchVelocityDegreesPerSecond = binding.gimbal != null
                        ? binding.gimbal.PitchVelocityDegreesPerSecond : 0f,
                    gimbalYawVelocityDegreesPerSecond = binding.gimbal != null
                        ? binding.gimbal.YawVelocityDegreesPerSecond : 0f,
                    gimbalActuatorTorqueNm = binding.gimbal != null
                        ? binding.gimbal.CurrentActuatorTorqueNm : 0f,
                    gimbalAtLimit = IsGimbalAtLimit(binding.gimbal),
                    springDisplacementMeters = binding.spring != null
                        ? binding.spring.DisplacementM : 0f,
                    springVelocityMetersPerSecond = binding.spring != null
                        ? binding.spring.VelocityMPerSecond : 0f,
                    springEndpointLoadN = binding.spring != null
                        ? binding.spring.CurrentEndpointLoadN : 0f,
                    springForceN = binding.spring != null
                        ? binding.spring.CurrentSpringForceN : 0f,
                    springPotentialEnergyJ = binding.spring != null
                        ? binding.spring.SpringPotentialEnergyJ : 0f,
                    springDampingLossW = binding.spring != null
                        ? binding.spring.DampingLossPowerW : 0f,
                    springAtCompressionLimit = binding.spring != null &&
                        binding.spring.IsAtCompressionLimit,
                    springAtExtensionLimit = binding.spring != null &&
                        binding.spring.IsAtExtensionLimit,
                    rotaryAngleDegrees = binding.rotary != null
                        ? binding.rotary.CurrentAngleDegrees : 0f,
                    rotaryTargetAngleDegrees = binding.rotary != null
                        ? binding.rotary.TargetAngleDegrees : 0f,
                    rotaryAtLimit = binding.rotary != null &&
                        binding.rotary.IsAtPhysicalLimit,
                    finForceN = binding.fin != null
                        ? binding.fin.CurrentAerodynamicForceN : 0f,
                    finRelativeAirflow = binding.fin != null
                        ? binding.fin.CurrentRelativeAirVelocity : Vector3.zero,
                    finAirspeedMetersPerSecond = binding.fin != null
                        ? binding.fin.CurrentRelativeAirVelocity.magnitude : 0f,
                    finAngleOfAttackDegrees = binding.fin != null
                        ? binding.fin.CurrentAngleOfAttackDegrees : 0f,
                    finLiftCoefficient = binding.fin != null
                        ? binding.fin.CurrentLiftCoefficient : 0f,
                    finDragCoefficient = binding.fin != null
                        ? binding.fin.CurrentDragCoefficient : 0f,
                    finStalled = binding.fin != null && binding.fin.IsStalled,
                    finReverseFlow = binding.fin != null && binding.fin.IsReverseFlow,
                    finLiftForce = binding.fin != null
                        ? binding.fin.CurrentWorldLiftForce : Vector3.zero,
                    finDragForce = binding.fin != null
                        ? binding.fin.CurrentWorldDragForce : Vector3.zero,
                    finStructuralWarning = binding.fin != null &&
                        binding.fin.HasStructuralWarning,
                    finStructurallyLimited = binding.fin != null &&
                        binding.fin.IsStructurallyLimited,
                    finStructuralLimitReason = binding.fin != null
                        ? binding.fin.StructuralLimitReason : string.Empty,
                    temperatureC = part.CurrentTemperatureC,
                    thermalLimit = part.ThermalOutputLimit,
                    thermallyDerated = part.ThermalOutputLimit < 0.9999f,
                    faulted = !part.IsEnabled || part.IsThermallyLockedOut
                };
            }
            if (devices.Length > capacity && !overflowWarned && session != null)
            {
                overflowWarned = true;
                session.AddDataWarning(
                    "Installed-device count exceeded configured per-sample capacity; excess records were dropped.");
            }
        }

        public void OnSessionEnded(V3DiagnosticSession value)
        {
            session = null;
        }

        private bool FindRouterEntry(
            RuntimeThrusterInstance thruster,
            int count,
            out V3ActuatorRouterEntrySnapshot entry)
        {
            if (thruster != null)
            {
                for (int i = 0; i < count; i++)
                {
                    if (routerEntries[i].Thruster == thruster)
                    {
                        entry = routerEntries[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        private static string FindConnectorId(
            IReadOnlyList<RuntimePartInstance> parts,
            RuntimePartInstance endpoint,
            string socketId)
        {
            if (parts == null || string.IsNullOrEmpty(socketId))
                return string.Empty;
            for (int i = 0; i < parts.Count; i++)
            {
                RuntimePartInstance candidate = parts[i];
                if (candidate != null && candidate != endpoint &&
                    candidate.ParentSocketId == socketId &&
                    candidate.Definition is ConnectorDefinition)
                {
                    return candidate.Definition.StableId;
                }
            }
            return string.Empty;
        }

        private static int SourceChannelMask(V3ActuatorRouterEntrySnapshot entry)
        {
            int mask = 0;
            if (Mathf.Abs(entry.Drive) > 0.0001f) mask |= 1 << 0;
            if (Mathf.Abs(entry.BaseHover) > 0.0001f) mask |= 1 << 1;
            if (Mathf.Abs(entry.Stabilization) > 0.0001f) mask |= 1 << 2;
            if (Mathf.Abs(entry.Vectoring) > 0.0001f) mask |= 1 << 3;
            if (Mathf.Abs(entry.Manual) > 0.0001f) mask |= 1 << 4;
            return mask;
        }

        private static float CalculateRequestedForceMagnitude(
            RuntimeThrusterInstance thruster, float command)
        {
            if (thruster == null || thruster.ThrusterDefinition == null)
                return 0f;
            ThrusterDefinition definition = thruster.ThrusterDefinition;
            float maximum = command >= 0f
                ? definition.MaximumForwardForceN
                : definition.MaximumReverseForceN;
            return Mathf.Sign(command) * Mathf.Abs(command) * maximum;
        }

        private static float CalculateAuthorityCap(
            RuntimeThrusterInstance thruster)
        {
            if (thruster == null || thruster.ThrusterDefinition == null)
                return 0f;
            float commandCap = thruster.IsEmergencyOverloadActive
                ? thruster.ThrusterDefinition.OverloadOutputMultiplier
                : thruster.ThrusterDefinition.NormalOutputMultiplier;
            return commandCap * thruster.ThermalOutputLimit;
        }

        private static string LimitingReason(
            RuntimePartInstance part,
            float routerCommand,
            bool hasRouterEntry,
            V3ActuatorRouterEntrySnapshot routerEntry)
        {
            if (!part.IsEnabled) return "Disabled";
            if (part.IsThermallyLockedOut) return "ThermalLockout";
            if (hasRouterEntry && routerEntry.FirmwareSafeState)
                return "FirmwareSafeState";
            if (part.RequestedPower > 0.0001f &&
                part.GrantedPower + 0.0001f < part.RequestedPower)
                return "PowerLimited";
            if (Mathf.Abs(part.RequestedOutput) + 0.0001f <
                Mathf.Abs(routerCommand))
                return "FirmwareOrThermalLimited";
            if (Mathf.Abs(part.CurrentOutput) + 0.0001f <
                Mathf.Abs(part.RequestedOutput))
                return "DynamicResponseOrPowerLimited";
            return string.Empty;
        }

        private static string DeviceType(RuntimePartInstance part)
        {
            if (part is RuntimeThrusterInstance) return "Thruster";
            if (part is RuntimeGimbalInstance) return "Gimbal";
            if (part is RuntimeSpringMountInstance) return "SpringMount";
            if (part is RuntimeAerodynamicFinInstance) return "AerodynamicFin";
            if (part is RuntimeRotaryActuatorInstance) return "RotaryActuator";
            if (part is RuntimeCoolingModuleInstance) return "CoolingModule";
            return "Part";
        }

        private static bool IsGimbalAtLimit(RuntimeGimbalInstance gimbal)
        {
            if (gimbal == null || gimbal.GimbalDefinition == null) return false;
            GimbalDefinition definition = gimbal.GimbalDefinition;
            bool pitch = definition.SupportsPitch &&
                Mathf.Abs(gimbal.CurrentPitchDegrees) + 0.01f >=
                definition.MaximumPitchDegrees;
            bool yaw = definition.SupportsYaw &&
                Mathf.Abs(gimbal.CurrentYawDegrees) + 0.01f >=
                definition.MaximumYawDegrees;
            return pitch || yaw;
        }

        private static string RoleName(PartRole value)
        {
            return value switch
            {
                PartRole.Propulsion => "Propulsion",
                PartRole.Hover => "Hover",
                PartRole.Braking => "Braking",
                PartRole.Control => "Control",
                PartRole.Energy => "Energy",
                PartRole.Cockpit => "Cockpit",
                PartRole.InternalSystem => "InternalSystem",
                PartRole.Sensor => "Sensor",
                PartRole.Aerodynamics => "Aerodynamics",
                PartRole.Cooling => "Cooling",
                _ => "Unknown"
            };
        }
    }
}
