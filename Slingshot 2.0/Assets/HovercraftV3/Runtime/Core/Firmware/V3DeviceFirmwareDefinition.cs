using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Systems/Device Firmware")]
    public sealed class V3DeviceFirmwareDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private V3FirmwareDeviceKind deviceKind;
        [Min(1f), SerializeField] private float requestedCommandRateHz = 120f;
        [Min(1f), SerializeField] private float minimumCommandRateHz = 30f;
        [Min(0f), SerializeField] private float idlePower = 0.25f;
        [Min(0f), SerializeField] private float powerPerCommand = 0.002f;
        [Min(0.01f), SerializeField] private float commandTimeoutSeconds = 0.25f;
        public string StableId => stableId;
        public V3FirmwareDeviceKind DeviceKind => deviceKind;
        public float RequestedCommandRateHz => requestedCommandRateHz;
        public float MinimumCommandRateHz => minimumCommandRateHz;
        public float IdlePower => idlePower;
        public float PowerPerCommand => powerPerCommand;
        public float CommandTimeoutSeconds => commandTimeoutSeconds;
    }
}
