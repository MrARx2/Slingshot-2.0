using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3CraftAssembler : MonoBehaviour
    {
        [SerializeField] private CraftBuildDefinition build;
        [SerializeField] private bool assembleOnStart = true;
        [SerializeField] private bool installReferenceControllers = true;
        [SerializeField] private Transform runtimeParent;

        private GameObject assembledRoot;

        public CraftBuildDefinition Build => build;
        public GameObject AssembledRoot => assembledRoot;

        private void Start()
        {
            if (assembleOnStart)
            {
                Rebuild();
            }
        }

        [ContextMenu("Rebuild V3 Craft")]
        public bool Rebuild()
        {
            BuildValidationReport report = CraftBuildValidator.Validate(build);
            if (!report.IsValid)
            {
                for (int i = 0; i < report.Issues.Count; i++)
                {
                    BuildIssue issue = report.Issues[i];
                    if (issue.Severity == BuildIssueSeverity.Error)
                    {
                        Debug.LogError($"V3 build [{issue.Code}] {issue.Message}", this);
                    }
                }

                return false;
            }

            DestroyAssembledRoot();

            Transform parent = runtimeParent != null ? runtimeParent : transform;
            assembledRoot = Instantiate(build.Chassis.Prefab, parent);
            assembledRoot.name = $"{build.Chassis.DisplayName} (V3 Runtime)";

            Rigidbody rootRigidbody = assembledRoot.GetComponent<Rigidbody>();
            V3Socket[] sockets = assembledRoot.GetComponentsInChildren<V3Socket>(true);
            var socketsById = new Dictionary<string, V3Socket>(sockets.Length, System.StringComparer.Ordinal);
            for (int i = 0; i < sockets.Length; i++)
            {
                socketsById.Add(sockets[i].SocketId, sockets[i]);
            }

            var installedParts = new List<RuntimePartInstance>();
            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation == null || installation.IsEmpty)
                {
                    continue;
                }

                V3Socket socket = socketsById[installation.SocketId];
                Transform endpointParent = socket.MountTransform;

                if (installation.Connector != null)
                {
                    RuntimePartInstance connectorInstance = InstantiatePart(
                        installation.Connector,
                        socket.SocketId,
                        socket.MountTransform);
                    installedParts.Add(connectorInstance);

                    ConnectorChildMount childMount =
                        connectorInstance.GetComponentInChildren<ConnectorChildMount>(true);
                    if (childMount == null)
                    {
                        Debug.LogError(
                            $"V3 connector '{installation.Connector.DisplayName}' " +
                            $"at socket '{socket.SocketId}' has no Connector Child Mount. " +
                            "Its endpoint cannot follow connector articulation.",
                            connectorInstance);
                        DestroyAssembledRoot();
                        return false;
                    }

                    endpointParent = childMount.MountTransform;
                }

                if (installation.Endpoint != null)
                {
                    installedParts.Add(InstantiatePart(
                        installation.Endpoint,
                        socket.SocketId,
                        endpointParent));
                }
            }

            V3CraftMassCalculator massCalculator =
                assembledRoot.GetComponent<V3CraftMassCalculator>();
            if (massCalculator == null)
            {
                massCalculator =
                    assembledRoot.AddComponent<V3CraftMassCalculator>();
            }

            if (!massCalculator.Initialize(
                build,
                rootRigidbody,
                installedParts))
            {
                Debug.LogError(
                    $"V3 mass calculation failed: " +
                    massCalculator.LastError,
                    this);
                DestroyAssembledRoot();
                return false;
            }

            V3CraftRuntime runtime = assembledRoot.GetComponent<V3CraftRuntime>();
            if (runtime == null)
            {
                runtime = assembledRoot.AddComponent<V3CraftRuntime>();
            }

            runtime.Initialize(
                build,
                rootRigidbody,
                installedParts,
                massCalculator.Result.TotalMassKg,
                massCalculator.Result.FinalCenterOfMass);

            V3SocketRegistry socketRegistry =
                assembledRoot.GetComponent<V3SocketRegistry>();
            if (socketRegistry == null)
            {
                socketRegistry =
                    assembledRoot.AddComponent<V3SocketRegistry>();
            }

            if (!socketRegistry.Initialize(sockets))
            {
                Debug.LogError(
                    $"V3 runtime socket registry failed: " +
                    socketRegistry.ValidationError,
                    this);
                DestroyAssembledRoot();
                return false;
            }

            V3PartRegistry partRegistry =
                assembledRoot.GetComponent<V3PartRegistry>();
            if (partRegistry == null)
            {
                partRegistry =
                    assembledRoot.AddComponent<V3PartRegistry>();
            }

            partRegistry.Initialize(installedParts);
            V3CapabilityRegistry capabilityRegistry =
                assembledRoot.GetComponent<V3CapabilityRegistry>();
            if (capabilityRegistry == null)
            {
                capabilityRegistry =
                    assembledRoot.AddComponent<V3CapabilityRegistry>();
            }

            capabilityRegistry.Initialize(partRegistry);
            V3RuntimeBuildValidator runtimeValidator =
                assembledRoot.GetComponent<V3RuntimeBuildValidator>();
            if (runtimeValidator == null)
            {
                runtimeValidator =
                    assembledRoot.AddComponent<V3RuntimeBuildValidator>();
            }

            if (!runtimeValidator.Initialize(
                build,
                runtime,
                socketRegistry,
                partRegistry,
                capabilityRegistry,
                massCalculator))
            {
                LogRuntimeValidationErrors(
                    runtimeValidator.RuntimeReport);
                DestroyAssembledRoot();
                return false;
            }

            BindThrusters(rootRigidbody, installedParts);
            V3ThrusterDebugView thrusterDebug =
                assembledRoot.GetComponent<V3ThrusterDebugView>();
            if (thrusterDebug == null)
            {
                thrusterDebug =
                    assembledRoot.AddComponent<V3ThrusterDebugView>();
            }

            thrusterDebug.Initialize(runtime);
            V3ThermalController thermal =
                assembledRoot.GetComponent<V3ThermalController>();
            if (thermal == null)
            {
                thermal = assembledRoot.AddComponent<V3ThermalController>();
            }

            thermal.Initialize(runtime);
            V3ThermalDebugDisplay thermalDisplay =
                assembledRoot.GetComponent<V3ThermalDebugDisplay>();
            if (thermalDisplay == null)
            {
                thermalDisplay =
                    assembledRoot.AddComponent<V3ThermalDebugDisplay>();
            }

            thermalDisplay.Initialize(thermal);
            if (installReferenceControllers)
            {
                InstallReferenceControllerPipeline(runtime, thermal);
            }

            V3CraftTelemetryHub telemetryHub =
                assembledRoot.GetComponent<V3CraftTelemetryHub>();
            if (telemetryHub == null)
            {
                telemetryHub =
                    assembledRoot.AddComponent<V3CraftTelemetryHub>();
            }

            telemetryHub.Initialize(
                runtime,
                assembledRoot.GetComponent<V3PowerDistributor>(),
                thermal,
                socketRegistry,
                capabilityRegistry,
                massCalculator,
                runtimeValidator);

            return true;
        }

        private RuntimePartInstance InstantiatePart(
            PartDefinition definition,
            string socketId,
            Transform parent)
        {
            GameObject instanceObject = Instantiate(definition.Prefab, parent);
            instanceObject.name = definition.DisplayName;
            RuntimePartInstance instance =
                definition is ThrusterDefinition
                    ? instanceObject.GetComponent<RuntimeThrusterInstance>()
                    : definition is GimbalDefinition
                        ? instanceObject.GetComponent<RuntimeGimbalInstance>()
                        : definition is SpringMountDefinition
                            ? instanceObject.GetComponent<RuntimeSpringMountInstance>()
                        : instanceObject.GetComponent<RuntimePartInstance>();
            if (instance == null)
            {
                instance =
                    definition is ThrusterDefinition
                        ? instanceObject.AddComponent<RuntimeThrusterInstance>()
                        : definition is GimbalDefinition
                            ? instanceObject.AddComponent<RuntimeGimbalInstance>()
                            : definition is SpringMountDefinition
                                ? instanceObject.AddComponent<RuntimeSpringMountInstance>()
                            : instanceObject.AddComponent<RuntimePartInstance>();
            }

            instance.Initialize(definition, socketId);
            return instance;
        }

        private static void BindThrusters(
            Rigidbody rootRigidbody,
            IReadOnlyList<RuntimePartInstance> installedParts)
        {
            for (int i = 0; i < installedParts.Count; i++)
            {
                if (installedParts[i] is RuntimeThrusterInstance thruster)
                {
                    thruster.BindRigidbody(rootRigidbody);
                }
            }
        }

        private static void InstallReferenceControllerPipeline(
            V3CraftRuntime runtime,
            V3ThermalController thermal)
        {
            GameObject root = runtime.gameObject;
            V3PowerDistributor power = GetOrAdd<V3PowerDistributor>(root);
            V3ActuatorCommandRouter router =
                GetOrAdd<V3ActuatorCommandRouter>(root);
            V3PilotInputAdapter input = GetOrAdd<V3PilotInputAdapter>(root);
            V3DriveController drive = GetOrAdd<V3DriveController>(root);
            V3HoverController hover = GetOrAdd<V3HoverController>(root);
            V3VectorController vector = GetOrAdd<V3VectorController>(root);
            V3TractionController traction =
                GetOrAdd<V3TractionController>(root);
            V3GimbalController gimbal =
                GetOrAdd<V3GimbalController>(root);
            V3ControllerPipeline pipeline =
                GetOrAdd<V3ControllerPipeline>(root);

            power.Initialize(runtime);
            router.Initialize(runtime, power);
            drive.Initialize(router);
            hover.Initialize(runtime, router);
            vector.Initialize(runtime, router);
            traction.Initialize(runtime, hover, router);
            gimbal.Initialize(runtime, power);
            V3CockpitWarningController warnings =
                GetOrAdd<V3CockpitWarningController>(root);
            warnings.Initialize(thermal, router, power);
            V3CockpitWarningDisplay warningDisplay =
                GetOrAdd<V3CockpitWarningDisplay>(root);
            warningDisplay.Initialize(warnings);
            pipeline.Initialize(
                input,
                router,
                drive,
                hover,
                vector,
                traction,
                gimbal,
                thermal,
                root.GetComponent<V3CapabilityRegistry>());
        }

        private static T GetOrAdd<T>(GameObject target)
            where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        private void LogRuntimeValidationErrors(
            BuildValidationReport report)
        {
            if (report == null)
            {
                Debug.LogError(
                    "V3 runtime validation failed without a report.",
                    this);
                return;
            }

            for (int i = 0; i < report.Issues.Count; i++)
            {
                BuildIssue issue = report.Issues[i];
                if (issue.Severity == BuildIssueSeverity.Error)
                {
                    Debug.LogError(
                        $"V3 runtime build [{issue.Code}] " +
                        issue.Message,
                        this);
                }
            }
        }

        private void DestroyAssembledRoot()
        {
            if (assembledRoot == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                assembledRoot.SetActive(false);
                Destroy(assembledRoot);
            }
            else
            {
                DestroyImmediate(assembledRoot);
            }

            assembledRoot = null;
        }
    }
}
