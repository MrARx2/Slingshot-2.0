using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [Serializable]
    public sealed class SocketInstallation
    {
        [SerializeField] private string socketId;
        [SerializeField] private ConnectorDefinition connector;
        [SerializeField] private EndpointDefinition endpoint;

        public string SocketId => socketId;
        public ConnectorDefinition Connector => connector;
        public EndpointDefinition Endpoint => endpoint;
        public bool IsEmpty => connector == null && endpoint == null;

        public SocketInstallation(string socketId)
        {
            this.socketId = socketId;
        }

        public void SetEmpty()
        {
            connector = null;
            endpoint = null;
        }

        public void SetDirect(EndpointDefinition value)
        {
            connector = null;
            endpoint = value;
        }

        public void SetConnector(ConnectorDefinition value, EndpointDefinition childEndpoint)
        {
            connector = value;
            endpoint = childEndpoint;
        }

        public void SetChildEndpoint(EndpointDefinition value)
        {
            endpoint = value;
        }
    }

    [CreateAssetMenu(menuName = "Hovercraft V3/Build/Craft Build", fileName = "CraftBuild")]
    public sealed class CraftBuildDefinition : ScriptableObject
    {
        [Header("Vehicle Identity")]
        [SerializeField] private string displayName;
        [SerializeField] private string manufacturerName = "Lunarlight";
        [SerializeField] private string vehicleClass = "Prototype";
        [SerializeField] private string layoutName;
        [TextArea(2, 4), SerializeField] private string summary;
        [TextArea(2, 5), SerializeField] private string drivingNotes;

        [Header("Assembly")]
        [SerializeField] private ChassisDefinition chassis;
        [SerializeField] private PartCatalog catalog;
        [SerializeField] private List<SocketInstallation> installations = new List<SocketInstallation>();

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ManufacturerName =>
            string.IsNullOrWhiteSpace(manufacturerName)
                ? "Independent"
                : manufacturerName;
        public string VehicleClass =>
            string.IsNullOrWhiteSpace(vehicleClass)
                ? "Unclassified"
                : vehicleClass;
        public string LayoutName =>
            string.IsNullOrWhiteSpace(layoutName)
                ? "Custom Layout"
                : layoutName;
        public string Summary => summary ?? string.Empty;
        public string DrivingNotes => drivingNotes ?? string.Empty;
        public ChassisDefinition Chassis => chassis;
        public PartCatalog Catalog => catalog;
        public IReadOnlyList<SocketInstallation> Installations => installations;

        public SocketInstallation FindInstallation(string socketId)
        {
            for (int i = 0; i < installations.Count; i++)
            {
                SocketInstallation installation = installations[i];
                if (installation != null &&
                    string.Equals(installation.SocketId, socketId, StringComparison.Ordinal))
                {
                    return installation;
                }
            }

            return null;
        }

        public SocketInstallation GetOrCreateInstallation(string socketId)
        {
            SocketInstallation existing = FindInstallation(socketId);
            if (existing != null)
            {
                return existing;
            }

            var created = new SocketInstallation(socketId);
            installations.Add(created);
            return created;
        }

        public void CopySelectionsFrom(CraftBuildDefinition source)
        {
            if (source == null)
            {
                return;
            }

            chassis = source.chassis;
            catalog = source.catalog;
            installations.Clear();

            for (int i = 0; i < source.installations.Count; i++)
            {
                SocketInstallation sourceInstallation = source.installations[i];
                if (sourceInstallation == null)
                {
                    continue;
                }

                var copy = new SocketInstallation(sourceInstallation.SocketId);
                if (sourceInstallation.Connector != null)
                {
                    copy.SetConnector(sourceInstallation.Connector, sourceInstallation.Endpoint);
                }
                else
                {
                    copy.SetDirect(sourceInstallation.Endpoint);
                }

                installations.Add(copy);
            }
        }
    }
}
