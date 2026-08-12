using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/System Computer")]
    public sealed class V3SystemComputerDefinition : EndpointDefinition
    {
        [SerializeField] private V3SystemComputerRole computerRole;
        [Min(1f), SerializeField] private float computeCapacityPerSecond = 240f;
        [Min(0f), SerializeField] private float taskCapacity = 1f;
        [SerializeField] private V3SystemSoftwareDefinition bundledSoftware;
        public override EndpointCategory Category => EndpointCategory.InternalSystem;
        public V3SystemComputerRole ComputerRole => computerRole;
        public float ComputeCapacityPerSecond => computeCapacityPerSecond;
        public float TaskCapacity => taskCapacity;
        public V3SystemSoftwareDefinition BundledSoftware => bundledSoftware;
    }
}
