using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public abstract class ConnectorDefinition : PartDefinition
    {
        [Header("Child Socket")]
        [SerializeField] private ConnectorKind kind;
        [SerializeField] private SocketFamily childSocketFamily;
        [SerializeField] private PartSize childSize = PartSize.Medium;
        [SerializeField] private EndpointCategory[] allowedEndpointCategories =
            Array.Empty<EndpointCategory>();
        [Min(0f), SerializeField] private float supportedEndpointMassKg;
        [Min(0f), SerializeField] private float supportedEndpointForceN;
        [Min(0f), SerializeField] private float childPowerAvailability;
        [SerializeField] private bool childHasPower = true;
        [SerializeField] private bool childHasData = true;

        public ConnectorKind Kind => kind;
        public SocketFamily ChildSocketFamily => childSocketFamily;
        public PartSize ChildSize => childSize;
        public float SupportedEndpointMassKg => supportedEndpointMassKg;
        public float SupportedEndpointForceN => supportedEndpointForceN;
        public float ChildPowerAvailability => childPowerAvailability;
        public bool ChildHasPower => childHasPower;
        public bool ChildHasData => childHasData;

        public bool AllowsEndpoint(EndpointCategory category)
        {
            for (int i = 0; i < allowedEndpointCategories.Length; i++)
            {
                if (allowedEndpointCategories[i] == category)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
