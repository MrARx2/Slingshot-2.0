using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Cockpit", fileName = "Cockpit")]
    public sealed class CockpitDefinition : EndpointDefinition
    {
        [Min(0), SerializeField] private int equipmentSlots;
        [Min(0), SerializeField] private int displaySlots;

        public override EndpointCategory Category => EndpointCategory.Cockpit;
        public int EquipmentSlots => equipmentSlots;
        public int DisplaySlots => displaySlots;
    }
}
