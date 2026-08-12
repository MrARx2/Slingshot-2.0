using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public static class V3ForensicSchemaIdentity
    {
        public const int Version = 6;
        public const string VersionText = "6.0";
    }

    public enum V3BuildOwnership
    {
        GeneratedReference,
        AuthoredVariant,
        TestOnly,
        Regression
    }

    [Serializable]
    public sealed class SocketInstallation
    {
        [SerializeField] private string socketId;
        [SerializeField] private ConnectorDefinition connector;
        [SerializeField] private EndpointDefinition endpoint;
        [Header("Per-Installation Authority")]
        [Tooltip("Normalized product-output cap for automatic systems. Zero inherits the product normal maximum.")]
        [Min(0f), SerializeField] private float automaticAuthorityCap;
        [Tooltip("Normalized product-output cap when the pilot explicitly requests emergency authority. Zero inherits the product emergency maximum.")]
        [Min(0f), SerializeField] private float manualAuthorityCap;
        [SerializeField] private bool emergencyAllowed = true;
        [SerializeField, HideInInspector] private bool authorityConfigured;
        [SerializeField] private Vector3 localCalibration;

        public string SocketId => socketId;
        public ConnectorDefinition Connector => connector;
        public EndpointDefinition Endpoint => endpoint;
        public bool IsEmpty => connector == null && endpoint == null;
        public float AutomaticAuthorityCap => automaticAuthorityCap;
        public float ManualAuthorityCap => manualAuthorityCap;
        // Builds authored before per-installation authority was introduced do
        // not contain the Boolean in their YAML. Unity deserializes that
        // missing field as false, so preserve the historical allow-emergency
        // behavior until the installation has been explicitly configured.
        public bool EmergencyAllowed =>
            !authorityConfigured || emergencyAllowed;
        public Vector3 LocalCalibration => localCalibration;

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

        public void ConfigureAuthority(
            float automaticCap,
            float manualCap,
            bool allowEmergency,
            Vector3 calibration)
        {
            automaticAuthorityCap = Mathf.Max(0f, automaticCap);
            manualAuthorityCap = Mathf.Max(0f, manualCap);
            emergencyAllowed = allowEmergency;
            authorityConfigured = true;
            localCalibration = calibration;
        }
    }

    [CreateAssetMenu(menuName = "Hovercraft V3/Build/Craft Build", fileName = "CraftBuild")]
    public sealed class CraftBuildDefinition : ScriptableObject
    {
        [Header("Vehicle Identity")]
        [SerializeField] private string stableId;
        [SerializeField] private string buildAssetGuid;
        [SerializeField] private V3BuildOwnership ownership =
            V3BuildOwnership.GeneratedReference;
        [SerializeField] private bool allowLegacyPipelineFallback = true;
        [SerializeField] private string displayName;
        [SerializeField] private string manufacturerName = "Lunarlight";
        [SerializeField] private string vehicleClass = "Prototype";
        [SerializeField] private string layoutName;
        [TextArea(2, 4), SerializeField] private string summary;
        [TextArea(2, 5), SerializeField] private string drivingNotes;

        [Header("Assembly")]
        [SerializeField] private ChassisDefinition chassis;
        [SerializeField] private PartCatalog catalog;
        [SerializeField] private V3HoverConfiguration hoverConfiguration =
            new V3HoverConfiguration();
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
        public string StableId => stableId;
        public string BuildAssetGuid => buildAssetGuid ?? string.Empty;
        public V3BuildOwnership Ownership => ownership;
        public bool IsGenerated => ownership == V3BuildOwnership.GeneratedReference;
        public bool AllowLegacyPipelineFallback => allowLegacyPipelineFallback;
        public ChassisDefinition Chassis => chassis;
        public PartCatalog Catalog => catalog;
        public V3HoverConfiguration HoverConfiguration =>
            hoverConfiguration ??= new V3HoverConfiguration();
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
            hoverConfiguration = source.HoverConfiguration.Clone();
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
                copy.ConfigureAuthority(
                    sourceInstallation.AutomaticAuthorityCap,
                    sourceInstallation.ManualAuthorityCap,
                    sourceInstallation.EmergencyAllowed,
                    sourceInstallation.LocalCalibration);

                installations.Add(copy);
            }
        }

        public void ConfigureAuthoringIdentity(
            string newStableId,
            string assetGuid,
            V3BuildOwnership newOwnership,
            bool allowLegacyFallback)
        {
            stableId = newStableId ?? string.Empty;
            buildAssetGuid = assetGuid ?? string.Empty;
            ownership = newOwnership;
            allowLegacyPipelineFallback = allowLegacyFallback;
        }

        public void CopyPresentationFrom(CraftBuildDefinition source)
        {
            if (source == null) return;
            displayName = source.displayName;
            manufacturerName = source.manufacturerName;
            vehicleClass = source.vehicleClass;
            layoutName = source.layoutName;
            summary = source.summary;
            drivingNotes = source.drivingNotes;
        }

        private void OnValidate()
        {
            HoverConfiguration.Validate();
        }
    }
}
