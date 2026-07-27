using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Energy Core", fileName = "EnergyCore")]
    public sealed class EnergyCoreDefinition : EndpointDefinition
    {
        [Min(0f), SerializeField] private float continuousOutput;
        [Min(0f), SerializeField] private float propulsionChannelCeiling;
        [Min(0f), SerializeField] private float systemsChannelCeiling;
        [Min(0f), SerializeField] private float temporaryPeakOutput;

        public override EndpointCategory Category => EndpointCategory.EnergyCore;
        public float ContinuousOutput => continuousOutput;
        public float PropulsionChannelCeiling => propulsionChannelCeiling;
        public float SystemsChannelCeiling => systemsChannelCeiling;
        public float TemporaryPeakOutput => temporaryPeakOutput;
    }
}
