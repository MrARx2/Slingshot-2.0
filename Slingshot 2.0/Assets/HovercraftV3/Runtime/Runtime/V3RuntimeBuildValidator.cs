using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3RuntimeBuildValidator : MonoBehaviour
    {
        private CraftBuildDefinition build;
        private V3CraftRuntime runtime;
        private V3SocketRegistry socketRegistry;
        private V3PartRegistry partRegistry;
        private V3CapabilityRegistry capabilityRegistry;
        private V3CraftMassCalculator massCalculator;

        public BuildValidationReport AuthoringReport { get; private set; }
        public BuildValidationReport RuntimeReport { get; private set; }
        public bool IsValid =>
            AuthoringReport != null &&
            AuthoringReport.IsValid &&
            RuntimeReport != null &&
            RuntimeReport.IsValid;

        public bool Initialize(
            CraftBuildDefinition craftBuild,
            V3CraftRuntime craftRuntime,
            V3SocketRegistry sockets,
            V3PartRegistry parts,
            V3CapabilityRegistry capabilities,
            V3CraftMassCalculator mass)
        {
            build = craftBuild;
            runtime = craftRuntime;
            socketRegistry = sockets;
            partRegistry = parts;
            capabilityRegistry = capabilities;
            massCalculator = mass;
            return RefreshValidation();
        }

        public bool RefreshValidation()
        {
            AuthoringReport = CraftBuildValidator.Validate(build);
            RuntimeReport = new BuildValidationReport();
            ValidateRequiredKernel();
            if (!RuntimeReport.IsValid)
            {
                return false;
            }

            ValidateRigidbody();
            ValidateSockets();
            ValidateParts();
            ValidateCapabilities();
            ValidateMass();
            return IsValid;
        }

        private void ValidateRequiredKernel()
        {
            if (build == null)
            {
                AddError(
                    "RUNTIME_BUILD_MISSING",
                    "Runtime validator has no Craft Build Definition.");
            }

            if (runtime == null)
            {
                AddError(
                    "RUNTIME_CRAFT_MISSING",
                    "Runtime validator has no V3CraftRuntime.");
            }

            if (socketRegistry == null)
            {
                AddError(
                    "RUNTIME_SOCKET_REGISTRY_MISSING",
                    "Runtime craft has no Socket Registry.");
            }

            if (partRegistry == null)
            {
                AddError(
                    "RUNTIME_PART_REGISTRY_MISSING",
                    "Runtime craft has no Part Registry.");
            }

            if (capabilityRegistry == null)
            {
                AddError(
                    "RUNTIME_CAPABILITY_REGISTRY_MISSING",
                    "Runtime craft has no Capability Registry.");
            }

            if (massCalculator == null || massCalculator.Result == null)
            {
                AddError(
                    "RUNTIME_MASS_CALCULATOR_MISSING",
                    "Runtime craft has no completed Mass Calculator.");
            }
        }

        private void ValidateRigidbody()
        {
            Rigidbody[] bodies =
                runtime.GetComponentsInChildren<Rigidbody>(true);
            if (bodies.Length != 1 ||
                bodies[0] != runtime.RootRigidbody ||
                bodies[0].gameObject != runtime.gameObject)
            {
                AddError(
                    "RUNTIME_RIGIDBODY",
                    "Runtime craft must contain exactly one root Rigidbody.");
            }
        }

        private void ValidateSockets()
        {
            if (!socketRegistry.IsValid)
            {
                AddError(
                    "RUNTIME_SOCKET_REGISTRY_INVALID",
                    socketRegistry.ValidationError);
                return;
            }

            int expectedCount =
                build.Chassis.Prefab
                    .GetComponentsInChildren<V3Socket>(true).Length;
            if (socketRegistry.Count != expectedCount)
            {
                AddError(
                    "RUNTIME_SOCKET_COUNT",
                    $"Runtime registered {socketRegistry.Count} sockets; " +
                    $"the build chassis defines {expectedCount}.");
            }
        }

        private void ValidateParts()
        {
            int expectedPartCount = 0;
            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation =
                    build.Installations[i];
                if (installation == null)
                {
                    continue;
                }

                int expectedAtSocket = 0;
                if (installation.Connector != null)
                {
                    expectedPartCount++;
                    expectedAtSocket++;
                }

                if (installation.Endpoint != null)
                {
                    expectedPartCount++;
                    expectedAtSocket++;
                }

                IReadOnlyList<RuntimePartInstance> runtimeParts =
                    partRegistry.GetPartsAtSocket(
                        installation.SocketId);
                if (runtimeParts.Count != expectedAtSocket)
                {
                    AddError(
                        "RUNTIME_CHAIN_COUNT",
                        $"Socket '{installation.SocketId}' expected " +
                        $"{expectedAtSocket} parts but registered " +
                        $"{runtimeParts.Count}.",
                        installation.SocketId);
                    continue;
                }

                RuntimePartInstance connector = null;
                if (installation.Connector != null &&
                    (!partRegistry.TryGetConnector(
                         installation.SocketId,
                         out connector) ||
                     connector.Definition != installation.Connector))
                {
                    AddError(
                        "RUNTIME_CONNECTOR_MISMATCH",
                        $"Socket '{installation.SocketId}' does not match " +
                        "its authored connector.",
                        installation.SocketId);
                }

                RuntimePartInstance endpoint = null;
                if (installation.Endpoint != null &&
                    (!partRegistry.TryGetEndpoint(
                         installation.SocketId,
                         out endpoint) ||
                     endpoint.Definition != installation.Endpoint))
                {
                    AddError(
                        "RUNTIME_ENDPOINT_MISMATCH",
                        $"Socket '{installation.SocketId}' does not match " +
                        "its authored endpoint.",
                        installation.SocketId);
                }

                if (connector != null && endpoint != null)
                {
                    ConnectorChildMount childMount =
                        connector.GetComponentInChildren<ConnectorChildMount>(true);
                    if (childMount == null)
                    {
                        AddError(
                            "RUNTIME_CONNECTOR_MOUNT_MISSING",
                            $"Connector at socket '{installation.SocketId}' has no " +
                            "Connector Child Mount.",
                            installation.SocketId);
                    }
                    else if (!endpoint.transform.IsChildOf(childMount.MountTransform))
                    {
                        AddError(
                            "RUNTIME_ENDPOINT_HIERARCHY",
                            $"Endpoint at socket '{installation.SocketId}' must be a " +
                            "child of its connector's moving mount.",
                            installation.SocketId);
                    }
                }
            }

            if (partRegistry.Count != expectedPartCount ||
                runtime.InstalledParts.Count != expectedPartCount)
            {
                AddError(
                    "RUNTIME_PART_COUNT",
                    $"Runtime registered {partRegistry.Count} parts and " +
                    $"craft runtime exposes {runtime.InstalledParts.Count}; " +
                    $"the build defines {expectedPartCount}.");
            }
        }

        private void ValidateCapabilities()
        {
            if (!capabilityRegistry.AreRequirementsSatisfied)
            {
                AddError(
                    "RUNTIME_CAPABILITY_MISSING",
                    $"Runtime craft is missing capabilities: " +
                    capabilityRegistry.MissingCapabilities);
            }
        }

        private void ValidateMass()
        {
            V3MassCalculationResult result = massCalculator.Result;
            Rigidbody body = runtime.RootRigidbody;
            if (!Mathf.Approximately(body.mass, result.TotalMassKg) ||
                !Mathf.Approximately(
                    runtime.CalculatedMassKg,
                    result.TotalMassKg))
            {
                AddError(
                    "RUNTIME_MASS_MISMATCH",
                    $"Calculated mass is {result.TotalMassKg:0.###} kg, " +
                    $"Rigidbody mass is {body.mass:0.###} kg, and runtime " +
                    $"mass is {runtime.CalculatedMassKg:0.###} kg.");
            }

            if ((body.centerOfMass - result.FinalCenterOfMass)
                    .sqrMagnitude > 0.00000001f ||
                (runtime.CalculatedCenterOfMass -
                 result.FinalCenterOfMass).sqrMagnitude >
                0.00000001f)
            {
                AddError(
                    "RUNTIME_CENTER_OF_MASS_MISMATCH",
                    $"Runtime center of mass does not match the calculated " +
                    $"value {result.FinalCenterOfMass}.");
            }
        }

        private void AddError(
            string code,
            string message,
            string socketId = null)
        {
            RuntimeReport.Add(
                BuildIssueSeverity.Error,
                code,
                message,
                socketId);
        }
    }
}
