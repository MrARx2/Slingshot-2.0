using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Cooling Module")]
    public sealed class V3CoolingModuleDefinition : EndpointDefinition
    {
        [Min(0f), SerializeField] private float maximumHeatRemovalPerSecond = 240f;
        [Min(0f), SerializeField] private float activationTemperatureC = 55f;
        [SerializeField] private V3DeviceFirmwareDefinition firmware;
        public override EndpointCategory Category => EndpointCategory.CoolingModule;
        public float MaximumHeatRemovalPerSecond => maximumHeatRemovalPerSecond;
        public float ActivationTemperatureC => activationTemperatureC;
        public V3DeviceFirmwareDefinition Firmware => firmware;
    }
}
