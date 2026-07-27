using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Internal System", fileName = "InternalSystem")]
    public sealed class InternalSystemDefinition : EndpointDefinition
    {
        public override EndpointCategory Category => EndpointCategory.InternalSystem;
    }
}
