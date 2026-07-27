using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public abstract class PartDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private ManufacturerDefinition manufacturer;
        [SerializeField] private string productFamily;
        [TextArea, SerializeField] private string description;
        [SerializeField] private PartRole role;
        [SerializeField] private PartSize size = PartSize.Medium;
        [SerializeField] private GameObject prefab;
        [SerializeField] private bool prototype = true;

        [Header("Shared Statistics")]
        [SerializeField] private PhysicalProfile physical;
        [SerializeField] private PowerProfile power;
        [SerializeField] private ThermalProfile thermal;

        [Header("Compatibility")]
        [SerializeField] private SocketFamily[] compatibleParentFamilies = Array.Empty<SocketFamily>();
        [SerializeField] private bool requiresPower;
        [SerializeField] private bool requiresData;
        [SerializeField] private PartCapability providedCapabilities;
        [SerializeField] private PartCapability requiredCapabilities;

        public string StableId => stableId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ManufacturerDefinition Manufacturer => manufacturer;
        public string ProductFamily => productFamily;
        public string Description => description;
        public PartRole Role => role;
        public PartSize Size => size;
        public GameObject Prefab => prefab;
        public bool IsPrototype => prototype;
        public PhysicalProfile Physical => physical;
        public PowerProfile Power => power;
        public ThermalProfile Thermal => thermal;
        public bool RequiresPower => requiresPower;
        public bool RequiresData => requiresData;
        public PartCapability ProvidedCapabilities => providedCapabilities;
        public PartCapability RequiredCapabilities => requiredCapabilities;

        public bool SupportsParentFamily(SocketFamily family)
        {
            for (int i = 0; i < compatibleParentFamilies.Length; i++)
            {
                if (compatibleParentFamilies[i] == family)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public abstract class EndpointDefinition : PartDefinition
    {
        public abstract EndpointCategory Category { get; }
        public virtual float MaximumOutputForceN => 0f;
    }
}
