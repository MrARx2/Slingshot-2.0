using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum SocketFamily
    {
        HeavyPropulsion,
        HoverVerticalControl,
        LateralControl,
        Cockpit,
        EnergyBay,
        InternalEquipment
    }

    public enum PartSize
    {
        Small,
        Medium,
        Large
    }

    public enum PartRole
    {
        Propulsion,
        Hover,
        Braking,
        Control,
        Energy,
        Cockpit,
        InternalSystem,
        Sensor
    }

    public enum EndpointCategory
    {
        Thruster,
        EnergyCore,
        Cockpit,
        InternalSystem,
        Sensor,
        Fin,
        PhaseVectorRing
    }

    public enum ConnectorKind
    {
        FixedAdapter,
        Gimbal,
        SpringMount,
        SuspensionMount
    }

    public enum PowerDomain
    {
        None,
        Propulsion,
        Systems
    }

    public enum V3ThermalProtectionState
    {
        Normal,
        Warning,
        Overheated,
        CoolingLockout,
        Recovered
    }

    [Flags]
    public enum PartCapability
    {
        None = 0,
        DriveControl = 1 << 0,
        HoverControl = 1 << 1,
        Stabilization = 1 << 2,
        VectoringControl = 1 << 3,
        ThermalTelemetry = 1 << 4,
        PowerDistribution = 1 << 5
    }

    [Serializable]
    public struct PhysicalProfile
    {
        [Min(0f)] public float massKg;
        public Vector3 localCenterOfMass;
        [Min(0f)] public float maximumStructuralLoadN;
        [Min(0f)] public float maximumSupportedForceN;
    }

    [Serializable]
    public struct PowerProfile
    {
        public PowerDomain domain;
        [Min(0f)] public float idleDemand;
        [Min(0f)] public float maximumDemand;
        [Min(0f)] public float disabledDemand;
        [Min(0f)] public float efficiency;
        [Min(1f)] public float overloadDemandMultiplier;
    }

    [Serializable]
    public struct ThermalProfile
    {
        public bool enabled;
        public float ambientTemperatureC;
        public float maximumSafeTemperatureC;
        public float overheatTemperatureC;
        public float restartTemperatureC;
        [Range(0f, 1f)] public float hotOutputLimitAtOverheat;
        [Min(0f)] public float coolingLockoutSeconds;
        [Min(0.001f)] public float thermalCapacity;
        [Min(0f)] public float passiveCoolingPerSecond;
        [Min(0f)] public float idleHeatPerSecond;
        [Min(0f)] public float maximumOutputHeatPerSecond;
        [Min(1f)] public float overloadHeatMultiplier;
    }
}
