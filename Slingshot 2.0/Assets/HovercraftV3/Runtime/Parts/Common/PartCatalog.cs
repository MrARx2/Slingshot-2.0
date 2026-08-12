using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Data/Part Catalog", fileName = "PartCatalog")]
    public sealed class PartCatalog : ScriptableObject
    {
        [SerializeField] private List<PartDefinition> parts = new List<PartDefinition>();

        public IReadOnlyList<PartDefinition> Parts => parts;

        public bool Contains(PartDefinition definition)
        {
            return definition != null && parts.Contains(definition);
        }
    }
}
