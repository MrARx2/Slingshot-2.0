using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3Socket : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string socketId;
        [SerializeField] private string debugName;
        [SerializeField] private SocketFamily family;
        [SerializeField] private PartSize size = PartSize.Medium;
        [SerializeField] private Transform mountTransform;

        [Header("Limits")]
        [Min(0f), SerializeField] private float maximumSupportedMassKg;
        [Min(0f), SerializeField] private float maximumSupportedForceN;
        [Min(0f), SerializeField] private float powerAvailability;
        [SerializeField] private bool hasPower = true;
        [SerializeField] private bool hasData = true;

        [Header("Allowed Parts")]
        [SerializeField] private EndpointCategory[] allowedDirectEndpoints = Array.Empty<EndpointCategory>();
        [SerializeField] private ConnectorKind[] allowedConnectors = Array.Empty<ConnectorKind>();

        [Header("Layout")]
        [SerializeField] private string pairedSocketId;
        [SerializeField] private string configurationGroup;
        [SerializeField] private bool drawGizmo = true;

        public string SocketId => socketId;
        public string DebugName => string.IsNullOrWhiteSpace(debugName) ? socketId : debugName;
        public SocketFamily Family => family;
        public PartSize Size => size;
        public Transform MountTransform => mountTransform != null ? mountTransform : transform;
        public float MaximumSupportedMassKg => maximumSupportedMassKg;
        public float MaximumSupportedForceN => maximumSupportedForceN;
        public float PowerAvailability => powerAvailability;
        public bool HasPower => hasPower;
        public bool HasData => hasData;
        public string PairedSocketId => pairedSocketId;
        public string ConfigurationGroup => configurationGroup;

        public bool AllowsDirectEndpoint(EndpointCategory category)
        {
            for (int i = 0; i < allowedDirectEndpoints.Length; i++)
            {
                if (allowedDirectEndpoints[i] == category)
                {
                    return true;
                }
            }

            return false;
        }

        public bool AllowsConnector(ConnectorKind kind)
        {
            for (int i = 0; i < allowedConnectors.Length; i++)
            {
                if (allowedConnectors[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmo)
            {
                return;
            }

            Transform mount = MountTransform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(mount.position, 0.12f);
            Gizmos.DrawLine(mount.position, mount.position + mount.forward * 0.6f);
        }
    }
}
