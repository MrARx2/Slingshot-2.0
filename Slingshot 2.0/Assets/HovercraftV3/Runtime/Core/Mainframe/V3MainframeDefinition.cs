using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3SystemComputerRole
    {
        PilotInterface,
        Drive,
        Hover,
        Stabilizer,
        Traction,
        Aerodynamics,
        Telemetry
    }

    public enum V3FirmwareDeviceKind
    {
        Thruster,
        Gimbal,
        RotaryFinActuator,
        CoolingModule,
        DirectionalSensor
    }

    public enum V3SensorDirection
    {
        Front,
        Rear,
        Left,
        Right,
        Top,
        Bottom
    }

    [CreateAssetMenu(
        menuName = "Hovercraft V3/Systems/Mainframe",
        fileName = "Mainframe")]
    public sealed class V3MainframeDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [Min(1), SerializeField] private int maximumRegisteredDevices = 64;
        [Min(1), SerializeField] private int maximumSoftwareTasks = 16;
        [Min(1f), SerializeField] private float computeCapacityPerSecond = 800f;
        [Min(1f), SerializeField] private float observationBandwidthPerSecond = 1200f;
        [Min(1f), SerializeField] private float commandBandwidthPerSecond = 600f;
        [Min(0f), SerializeField] private float idlePower = 35f;

        public string StableId => stableId;
        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public int MaximumRegisteredDevices => maximumRegisteredDevices;
        public int MaximumSoftwareTasks => maximumSoftwareTasks;
        public float ComputeCapacityPerSecond => computeCapacityPerSecond;
        public float ObservationBandwidthPerSecond => observationBandwidthPerSecond;
        public float CommandBandwidthPerSecond => commandBandwidthPerSecond;
        public float IdlePower => idlePower;
    }

}
