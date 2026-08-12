using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Cockpit", fileName = "Cockpit")]
    public sealed class CockpitDefinition : EndpointDefinition
    {
        [Min(0), SerializeField] private int equipmentSlots;
        [Min(0), SerializeField] private int displaySlots;
        [SerializeField] private V3SystemComputerRole[] supportedComputerRoles =
            System.Array.Empty<V3SystemComputerRole>();
        [SerializeField] private bool supportsFutureHud;
        [Min(0f), SerializeField] private float systemsPowerThroughput;
        [Min(0f), SerializeField] private float dataThroughput;
        [SerializeField] private Vector3 internalMassAnchor;

        public override EndpointCategory Category => EndpointCategory.Cockpit;
        public int EquipmentSlots => equipmentSlots;
        public int DisplaySlots => displaySlots;
        public System.Collections.Generic.IReadOnlyList<V3SystemComputerRole>
            SupportedComputerRoles => supportedComputerRoles;
        public bool SupportsFutureHud => supportsFutureHud;
        public float SystemsPowerThroughput => systemsPowerThroughput;
        public float DataThroughput => dataThroughput;
        public Vector3 InternalMassAnchor => internalMassAnchor;
    }
}
