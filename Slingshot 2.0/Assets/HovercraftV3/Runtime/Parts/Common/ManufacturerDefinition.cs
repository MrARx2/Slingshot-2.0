using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Data/Manufacturer", fileName = "Manufacturer")]
    public sealed class ManufacturerDefinition : ScriptableObject
    {
        [SerializeField] private string manufacturerId;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite logo;
        [TextArea, SerializeField] private string engineeringPhilosophy;

        public string ManufacturerId => manufacturerId;
        public string DisplayName => displayName;
        public Sprite Logo => logo;
        public string EngineeringPhilosophy => engineeringPhilosophy;
    }
}
