using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum BuildIssueSeverity
    {
        Warning,
        Error
    }

    public readonly struct BuildIssue
    {
        public BuildIssue(BuildIssueSeverity severity, string code, string message, string socketId = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            SocketId = socketId;
        }

        public BuildIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string SocketId { get; }
    }

    public sealed class BuildValidationReport
    {
        private readonly List<BuildIssue> issues = new List<BuildIssue>();

        public IReadOnlyList<BuildIssue> Issues => issues;
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == BuildIssueSeverity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal void Add(BuildIssueSeverity severity, string code, string message, string socketId = null)
        {
            issues.Add(new BuildIssue(severity, code, message, socketId));
        }
    }

    public static class CraftBuildValidator
    {
        public static BuildValidationReport Validate(CraftBuildDefinition build)
        {
            var report = new BuildValidationReport();
            if (build == null)
            {
                report.Add(BuildIssueSeverity.Error, "BUILD_NULL", "No craft build is assigned.");
                return report;
            }

            if (build.Chassis == null)
            {
                report.Add(BuildIssueSeverity.Error, "CHASSIS_MISSING", "The build has no chassis definition.");
                return report;
            }

            if (string.IsNullOrWhiteSpace(build.Chassis.StableId))
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "CHASSIS_ID_EMPTY",
                    "The chassis definition has no stable ID.");
            }

            ValidateCatalog(build.Catalog, report);

            GameObject chassisPrefab = build.Chassis.Prefab;
            if (!IsFinite(build.Chassis.BaseMassKg) ||
                build.Chassis.BaseMassKg <= 0f)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "CHASSIS_MASS_INVALID",
                    "The chassis base mass must be finite and greater than zero.");
            }

            if (chassisPrefab == null)
            {
                report.Add(BuildIssueSeverity.Error, "CHASSIS_PREFAB_MISSING", "The chassis definition has no prefab.");
                return report;
            }

            Rigidbody[] rigidbodies = chassisPrefab.GetComponentsInChildren<Rigidbody>(true);
            if (rigidbodies.Length != 1 || rigidbodies[0].gameObject != chassisPrefab)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "CHASSIS_RIGIDBODY",
                    "The chassis prefab must contain exactly one Rigidbody on its root.");
            }

            V3Socket[] sockets = chassisPrefab.GetComponentsInChildren<V3Socket>(true);
            var socketsById = new Dictionary<string, V3Socket>(StringComparer.Ordinal);
            if (sockets.Length == 0)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "CHASSIS_SOCKETS_MISSING",
                    "The chassis prefab contains no V3 sockets.");
            }

            for (int i = 0; i < sockets.Length; i++)
            {
                V3Socket socket = sockets[i];
                if (string.IsNullOrWhiteSpace(socket.SocketId))
                {
                    report.Add(BuildIssueSeverity.Error, "SOCKET_ID_EMPTY", "A chassis socket has no stable ID.");
                    continue;
                }

                if (!socketsById.TryAdd(socket.SocketId, socket))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "SOCKET_ID_DUPLICATE",
                        $"Socket ID '{socket.SocketId}' is duplicated.",
                        socket.SocketId);
                }
            }

            ValidateSocketPairs(sockets, socketsById, report);

            var installedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation == null)
                {
                    report.Add(BuildIssueSeverity.Error, "INSTALLATION_NULL", "The build contains a null installation.");
                    continue;
                }

                if (!installedIds.Add(installation.SocketId))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "INSTALLATION_DUPLICATE",
                        $"Socket '{installation.SocketId}' has more than one installation.",
                        installation.SocketId);
                    continue;
                }

                if (!socketsById.TryGetValue(installation.SocketId, out V3Socket socket))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "SOCKET_UNKNOWN",
                        $"Build references unknown socket '{installation.SocketId}'.",
                        installation.SocketId);
                    continue;
                }

                ValidateInstallation(socket, installation, build.Catalog, report);
            }

            ValidateCapabilities(build.Installations, report);
            return report;
        }

        private static void ValidateCatalog(
            PartCatalog catalog,
            BuildValidationReport report)
        {
            if (catalog == null)
            {
                report.Add(
                    BuildIssueSeverity.Warning,
                    "CATALOG_MISSING",
                    "The build has no Part Catalog; compatibility can be " +
                    "validated, but authoring choices are unrestricted.");
                return;
            }

            var stableIds = new Dictionary<string, PartDefinition>(
                StringComparer.Ordinal);
            for (int i = 0; i < catalog.Parts.Count; i++)
            {
                PartDefinition part = catalog.Parts[i];
                if (part == null)
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "CATALOG_PART_NULL",
                        $"Part Catalog entry {i} is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(part.StableId))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "CATALOG_PART_ID_EMPTY",
                        $"Catalog part '{part.name}' has no stable ID.");
                    continue;
                }

                if (stableIds.TryGetValue(
                    part.StableId,
                    out PartDefinition existing) &&
                    existing != part)
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "CATALOG_PART_ID_DUPLICATE",
                        $"Catalog parts '{existing.name}' and '{part.name}' " +
                        $"share stable ID '{part.StableId}'.");
                }
                else
                {
                    stableIds[part.StableId] = part;
                }
            }
        }

        private static void ValidateSocketPairs(
            IReadOnlyList<V3Socket> sockets,
            IReadOnlyDictionary<string, V3Socket> socketsById,
            BuildValidationReport report)
        {
            for (int i = 0; i < sockets.Count; i++)
            {
                V3Socket socket = sockets[i];
                if (socket == null ||
                    string.IsNullOrWhiteSpace(socket.PairedSocketId))
                {
                    continue;
                }

                if (string.Equals(
                    socket.SocketId,
                    socket.PairedSocketId,
                    StringComparison.Ordinal))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "SOCKET_PAIR_SELF",
                        $"Socket '{socket.SocketId}' cannot pair with itself.",
                        socket.SocketId);
                    continue;
                }

                if (!socketsById.TryGetValue(
                    socket.PairedSocketId,
                    out V3Socket paired))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "SOCKET_PAIR_UNKNOWN",
                        $"Socket '{socket.SocketId}' references missing pair " +
                        $"'{socket.PairedSocketId}'.",
                        socket.SocketId);
                    continue;
                }

                if (!string.Equals(
                    paired.PairedSocketId,
                    socket.SocketId,
                    StringComparison.Ordinal))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "SOCKET_PAIR_NOT_RECIPROCAL",
                        $"Socket pair '{socket.SocketId}' and " +
                        $"'{paired.SocketId}' is not reciprocal.",
                        socket.SocketId);
                }
            }
        }

        private static void ValidateCapabilities(
            IReadOnlyList<SocketInstallation> installations,
            BuildValidationReport report)
        {
            PartCapability provided = PartCapability.None;
            PartCapability required = PartCapability.None;
            int energyCoreCount = 0;
            int cockpitCount = 0;
            for (int i = 0; i < installations.Count; i++)
            {
                SocketInstallation installation = installations[i];
                if (installation == null)
                {
                    continue;
                }

                if (installation.Connector != null)
                {
                    provided |=
                        installation.Connector.ProvidedCapabilities;
                    required |=
                        installation.Connector.RequiredCapabilities;
                }

                if (installation.Endpoint != null)
                {
                    if (installation.Endpoint is EnergyCoreDefinition)
                    {
                        energyCoreCount++;
                    }

                    if (installation.Endpoint is CockpitDefinition)
                    {
                        cockpitCount++;
                    }

                    provided |=
                        installation.Endpoint.ProvidedCapabilities;
                    required |=
                        installation.Endpoint.RequiredCapabilities;
                }
            }

            if (energyCoreCount != 1)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "ENERGY_CORE_COUNT",
                    $"A runnable prototype craft requires exactly one Energy " +
                    $"Core; the build contains {energyCoreCount}.");
            }

            if (cockpitCount != 1)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "COCKPIT_COUNT",
                    $"A runnable prototype craft requires exactly one Cockpit; " +
                    $"the build contains {cockpitCount}.");
            }

            PartCapability missing = required & ~provided;
            if (missing != PartCapability.None)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "CAPABILITY_MISSING",
                    $"Build requires unavailable capabilities: {missing}.");
            }
        }

        public static bool TryValidateConnector(
            V3Socket socket,
            ConnectorDefinition connector,
            out string error)
        {
            if (socket == null)
            {
                error = "Socket is missing.";
                return false;
            }

            if (connector == null)
            {
                error = "Connector is missing.";
                return false;
            }

            if (!socket.AllowsConnector(connector.Kind))
            {
                error = $"Connector kind {connector.Kind} is not allowed.";
                return false;
            }

            if (connector is SpringMountDefinition spring)
            {
                if (spring.TravelDistanceM <= 0f)
                {
                    error = "Spring mount must provide compression or extension travel.";
                    return false;
                }

                if (spring.SpringStiffness <= 0f)
                {
                    error = "Spring mount stiffness must be greater than zero.";
                    return false;
                }
            }

            if (connector is GimbalDefinition gimbal)
            {
                if (!gimbal.SupportsPitch && !gimbal.SupportsYaw)
                {
                    error = "Gimbal must support pitch, yaw, or both.";
                    return false;
                }

                if (gimbal.RotationSpeedDegreesPerSecond <= 0f ||
                    gimbal.AngularAccelerationDegreesPerSecondSquared <= 0f)
                {
                    error =
                        "Gimbal speed and angular acceleration must be " +
                        "greater than zero.";
                    return false;
                }
            }

            return TryValidatePartAgainstMount(
                connector,
                socket.Family,
                socket.Size,
                socket.MaximumSupportedMassKg,
                socket.MaximumSupportedForceN,
                socket.PowerAvailability,
                socket.HasPower,
                socket.HasData,
                out error);
        }

        public static bool TryValidateEndpoint(
            V3Socket socket,
            ConnectorDefinition connector,
            EndpointDefinition endpoint,
            out string error)
        {
            if (socket == null)
            {
                error = "Socket is missing.";
                return false;
            }

            if (endpoint == null)
            {
                error = "Endpoint is missing.";
                return false;
            }

            if (connector == null)
            {
                if (!socket.AllowsDirectEndpoint(endpoint.Category))
                {
                    error = $"Endpoint category {endpoint.Category} is not allowed directly.";
                    return false;
                }

                return TryValidatePartAgainstMount(
                    endpoint,
                    socket.Family,
                    socket.Size,
                    socket.MaximumSupportedMassKg,
                    socket.MaximumSupportedForceN,
                    socket.PowerAvailability,
                    socket.HasPower,
                    socket.HasData,
                    out error);
            }

            if (!connector.AllowsEndpoint(endpoint.Category))
            {
                error = $"Connector does not allow endpoint category {endpoint.Category}.";
                return false;
            }

            return TryValidatePartAgainstMount(
                endpoint,
                connector.ChildSocketFamily,
                connector.ChildSize,
                connector.SupportedEndpointMassKg,
                connector.SupportedEndpointForceN,
                connector.ChildPowerAvailability,
                connector.ChildHasPower,
                connector.ChildHasData,
                out error);
        }

        public static bool TryValidateChain(
            V3Socket socket,
            ConnectorDefinition connector,
            EndpointDefinition endpoint,
            out string error)
        {
            if (!TryValidateConnector(socket, connector, out error) ||
                !TryValidateEndpoint(socket, connector, endpoint, out error))
            {
                return false;
            }

            float combinedMassKg = connector.Physical.massKg + endpoint.Physical.massKg;
            if (socket.MaximumSupportedMassKg > 0f &&
                combinedMassKg > socket.MaximumSupportedMassKg)
            {
                error =
                    $"Connector chain mass {combinedMassKg:0.##} kg exceeds the " +
                    $"{socket.MaximumSupportedMassKg:0.##} kg parent-socket limit.";
                return false;
            }

            if (socket.MaximumSupportedForceN > 0f &&
                endpoint.MaximumOutputForceN > socket.MaximumSupportedForceN)
            {
                error =
                    $"Child endpoint force {endpoint.MaximumOutputForceN:0.##} N exceeds the " +
                    $"{socket.MaximumSupportedForceN:0.##} N parent-socket limit.";
                return false;
            }

            float combinedMaximumDemand =
                connector.Power.maximumDemand + endpoint.Power.maximumDemand;
            if (socket.PowerAvailability > 0f &&
                combinedMaximumDemand > socket.PowerAvailability)
            {
                error =
                    $"Connector chain demand {combinedMaximumDemand:0.##} exceeds " +
                    $"parent-socket power {socket.PowerAvailability:0.##}.";
                return false;
            }

            error = null;
            return true;
        }

        private static void ValidateInstallation(
            V3Socket socket,
            SocketInstallation installation,
            PartCatalog catalog,
            BuildValidationReport report)
        {
            if (installation.IsEmpty)
            {
                return;
            }

            if (installation.Connector != null)
            {
                if (!TryValidateConnector(socket, installation.Connector, out string connectorError))
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "CONNECTOR_INCOMPATIBLE",
                        connectorError,
                        socket.SocketId);
                }

                ValidateCatalogMembership(installation.Connector, catalog, socket.SocketId, report);

                if (installation.Endpoint == null)
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "CONNECTOR_ENDPOINT_MISSING",
                        "A connector chain must end with an endpoint.",
                        socket.SocketId);
                    return;
                }
            }

            if (installation.Endpoint != null)
            {
                bool endpointValid = installation.Connector != null
                    ? TryValidateChain(
                        socket,
                        installation.Connector,
                        installation.Endpoint,
                        out string endpointError)
                    : TryValidateEndpoint(
                        socket,
                        null,
                        installation.Endpoint,
                        out endpointError);
                if (!endpointValid)
                {
                    report.Add(
                        BuildIssueSeverity.Error,
                        "ENDPOINT_INCOMPATIBLE",
                        endpointError,
                        socket.SocketId);
                }

                ValidateCatalogMembership(installation.Endpoint, catalog, socket.SocketId, report);
            }
        }

        private static void ValidateCatalogMembership(
            PartDefinition definition,
            PartCatalog catalog,
            string socketId,
            BuildValidationReport report)
        {
            if (catalog != null && !catalog.Contains(definition))
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "PART_NOT_IN_CATALOG",
                    $"Part '{definition.DisplayName}' is not present in the build catalog.",
                    socketId);
            }

            if (string.IsNullOrWhiteSpace(definition.StableId))
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "PART_ID_EMPTY",
                    $"Part '{definition.name}' has no stable ID.",
                    socketId);
            }

            if (definition.Prefab == null)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "PART_PREFAB_MISSING",
                    $"Part '{definition.DisplayName}' has no prefab.",
                    socketId);
            }
            else if (definition.Prefab.GetComponentInChildren<Rigidbody>(true) != null)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "PART_RIGIDBODY_FORBIDDEN",
                    $"Part '{definition.DisplayName}' contains a Rigidbody; Prototype 1 permits only the chassis root Rigidbody.",
                    socketId);
            }
            else if (definition is ConnectorDefinition &&
                definition.Prefab.GetComponentInChildren<ConnectorChildMount>(
                    true) == null)
            {
                report.Add(
                    BuildIssueSeverity.Error,
                    "CONNECTOR_MOUNT_MISSING",
                    $"Connector '{definition.DisplayName}' has no Connector " +
                    "Child Mount for its endpoint.",
                    socketId);
            }
        }

        private static bool TryValidatePartAgainstMount(
            PartDefinition part,
            SocketFamily family,
            PartSize maximumSize,
            float maximumMassKg,
            float maximumForceN,
            float availablePower,
            bool hasPower,
            bool hasData,
            out string error)
        {
            if (!TryValidatePartData(part, out error))
            {
                return false;
            }

            if (!part.SupportsParentFamily(family))
            {
                error = $"Part does not support socket family {family}.";
                return false;
            }

            if (part.Size > maximumSize)
            {
                error = $"Part size {part.Size} exceeds mount size {maximumSize}.";
                return false;
            }

            if (maximumMassKg > 0f && part.Physical.massKg > maximumMassKg)
            {
                error = $"Part mass {part.Physical.massKg:0.##} kg exceeds the {maximumMassKg:0.##} kg limit.";
                return false;
            }

            if (part is EndpointDefinition endpoint &&
                maximumForceN > 0f &&
                endpoint.MaximumOutputForceN > maximumForceN)
            {
                error = $"Part force {endpoint.MaximumOutputForceN:0.##} N exceeds the {maximumForceN:0.##} N limit.";
                return false;
            }

            if (part.RequiresPower && !hasPower)
            {
                error = "Part requires power but this mount has no power connection.";
                return false;
            }

            if (part.RequiresData && !hasData)
            {
                error = "Part requires data but this mount has no data connection.";
                return false;
            }

            if (part.RequiresPower && availablePower > 0f && part.Power.maximumDemand > availablePower)
            {
                error = $"Part demand {part.Power.maximumDemand:0.##} exceeds available power {availablePower:0.##}.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryValidatePartData(
            PartDefinition part,
            out string error)
        {
            if (part == null)
            {
                error = "Part definition is missing.";
                return false;
            }

            PhysicalProfile physical = part.Physical;
            if (!IsFinite(physical.massKg) || physical.massKg < 0f)
            {
                error = "Part mass must be finite and non-negative.";
                return false;
            }

            PowerProfile power = part.Power;
            if (!IsFinite(power.idleDemand) ||
                !IsFinite(power.maximumDemand) ||
                !IsFinite(power.disabledDemand) ||
                power.idleDemand < 0f ||
                power.maximumDemand < power.idleDemand ||
                power.disabledDemand < 0f)
            {
                error =
                    "Power demand values must be finite, non-negative, and " +
                    "maximum demand must be at least idle demand.";
                return false;
            }

            ThermalProfile thermal = part.Thermal;
            if (thermal.enabled &&
                (!IsFinite(thermal.ambientTemperatureC) ||
                 !IsFinite(thermal.maximumSafeTemperatureC) ||
                 !IsFinite(thermal.overheatTemperatureC) ||
                 !IsFinite(thermal.restartTemperatureC) ||
                 thermal.thermalCapacity <= 0f ||
                 thermal.ambientTemperatureC >
                 thermal.restartTemperatureC ||
                 thermal.restartTemperatureC >
                 thermal.maximumSafeTemperatureC ||
                 thermal.restartTemperatureC >=
                 thermal.overheatTemperatureC ||
                 thermal.maximumSafeTemperatureC >=
                 thermal.overheatTemperatureC))
            {
                error =
                    "Thermal thresholds must be finite and ordered below " +
                    "overheat, with positive thermal capacity.";
                return false;
            }

            if (part is ThrusterDefinition thruster &&
                thruster.MaximumForwardForceN <= 0f)
            {
                error =
                    "Thruster maximum forward force must be greater than zero.";
                return false;
            }

            if (part is EnergyCoreDefinition core &&
                (core.ContinuousOutput <= 0f ||
                 core.PropulsionChannelCeiling <= 0f ||
                 core.SystemsChannelCeiling <= 0f))
            {
                error =
                    "Energy Core output and both channel ceilings must be " +
                    "greater than zero.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
