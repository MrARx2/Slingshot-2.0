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
        [SerializeField] private bool previewBuildInEditor;

        private GameObject assembledRoot;
        private bool spawnerControlled;

        public CraftBuildDefinition Build => build;
        public GameObject AssembledRoot => assembledRoot;
        public bool AssembleOnStart => assembleOnStart;
        public bool PreviewBuildInEditor => previewBuildInEditor;
        public bool IsSpawnerControlled => spawnerControlled;
        public event System.Action<GameObject> AssembledCraftChanged;

        private void Start()
        {
            if (assembleOnStart && !spawnerControlled)
            {
                Rebuild();
            }
        }

        public bool Rebuild(CraftBuildDefinition selectedBuild)
        {
            build = selectedBuild;
            return Rebuild();
        }

        [ContextMenu("Rebuild V3 Craft")]
        public bool Rebuild()
        {
            ClearEditorPreviewRootIfPresent();
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
            V3RuntimeHierarchy hierarchy =
                V3RuntimeHierarchy.Create(assembledRoot);
            V3CraftIdentity identity =
                GetOrAdd<V3CraftIdentity>(assembledRoot);
            identity.Initialize(build);

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
                    RuntimePartInstance endpointInstance = InstantiatePart(
                        installation.Endpoint,
                        socket.SocketId,
                        endpointParent);
                    if (endpointInstance is RuntimeThrusterInstance thruster)
                    {
                        thruster.ConfigureInstallationAuthority(installation);
                    }
                    installedParts.Add(endpointInstance);
                }
            }

            if (!InstallIntegratedSensors(
                assembledRoot,
                rootRigidbody,
                build.Chassis))
            {
                DestroyAssembledRoot();
                return false;
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
            V3WorldEnvironmentProvider environment =
                GetOrAdd<V3WorldEnvironmentProvider>(assembledRoot);
            environment.Initialize(runtime);
            V3CraftAerodynamicsRuntime craftAerodynamics =
                GetOrAdd<V3CraftAerodynamicsRuntime>(assembledRoot);
            craftAerodynamics.Initialize(runtime, environment);
            V3WorldDebugDisplay worldDebug =
                GetOrAdd<V3WorldDebugDisplay>(assembledRoot);
            worldDebug.Initialize(runtime);

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
            V3RuntimeDeviceRegistry deviceRegistry =
                GetOrAdd<V3RuntimeDeviceRegistry>(assembledRoot);
            if (!deviceRegistry.Initialize(assembledRoot))
            {
                Debug.LogError(
                    "V3 runtime device registry failed: " +
                    deviceRegistry.ValidationError,
                    this);
                DestroyAssembledRoot();
                return false;
            }

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

            BindPhysicalParts(
                rootRigidbody,
                installedParts,
                build.HoverConfiguration);
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
            InstallIntegratedMainframe(runtime);
            hierarchy.BindRuntimeServices();
            AssembledCraftChanged?.Invoke(assembledRoot);

            return true;
        }

        public void SetBuild(CraftBuildDefinition value)
        {
            build = value;
        }

        public void SetSpawnerControlled(bool value)
        {
            spawnerControlled = value;
        }

        [ContextMenu("Clear V3 Craft")]
        public void ClearCraft()
        {
            DestroyAssembledRoot();
        }

        private static void InstallIntegratedMainframe(
            V3CraftRuntime runtime)
        {
            if (runtime == null ||
                runtime.Build == null ||
                runtime.Build.Chassis == null ||
                runtime.Build.Chassis.IntegratedMainframe == null)
            {
                return;
            }

            GameObject root = runtime.gameObject;
            V3PowerDistributor power = GetOrAdd<V3PowerDistributor>(root);
            if (power.Core == null)
            {
                power.Initialize(runtime);
            }

            V3CraftMainframe mainframe = GetOrAdd<V3CraftMainframe>(root);
            mainframe.Initialize(runtime, power);
            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            pipeline?.BindMainframe(mainframe);
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
                        : definition is V3RotaryActuatorDefinition
                            ? instanceObject.GetComponent<RuntimeRotaryActuatorInstance>()
                        : definition is V3AerodynamicFinDefinition
                            ? instanceObject.GetComponent<RuntimeAerodynamicFinInstance>()
                        : definition is V3CoolingModuleDefinition
                            ? instanceObject.GetComponent<RuntimeCoolingModuleInstance>()
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
                            : definition is V3RotaryActuatorDefinition
                                ? instanceObject.AddComponent<RuntimeRotaryActuatorInstance>()
                            : definition is V3AerodynamicFinDefinition
                                ? instanceObject.AddComponent<RuntimeAerodynamicFinInstance>()
                            : definition is V3CoolingModuleDefinition
                                ? instanceObject.AddComponent<RuntimeCoolingModuleInstance>()
                            : instanceObject.AddComponent<RuntimePartInstance>();
            }

            instance.Initialize(definition, socketId);
            if (definition is V3SystemComputerDefinition)
            {
                V3SystemComputerRuntime computer =
                    instanceObject.GetComponent<
                        V3SystemComputerRuntime>();
                if (computer == null)
                {
                    computer = instanceObject.AddComponent<
                        V3SystemComputerRuntime>();
                }

                computer.Initialize(instance);
                InstallSystemRuntime(
                    instanceObject,
                    computer);
            }

            return instance;
        }

        private static void InstallSystemRuntime(
            GameObject computerObject,
            V3SystemComputerRuntime computer)
        {
            V3CraftSystemRuntimeBase system;
            switch (computer.Role)
            {
                case V3SystemComputerRole.PilotInterface:
                    system = GetOrAdd<
                        V3PilotInterfaceSystemRuntime>(
                        computerObject);
                    break;
                case V3SystemComputerRole.Drive:
                    system = GetOrAdd<V3DriveSystemRuntime>(
                        computerObject);
                    break;
                case V3SystemComputerRole.Hover:
                    system = GetOrAdd<V3HoverSystemRuntime>(
                        computerObject);
                    break;
                case V3SystemComputerRole.Stabilizer:
                    system = GetOrAdd<
                        V3StabilizerSystemRuntime>(
                        computerObject);
                    break;
                case V3SystemComputerRole.Traction:
                    system = GetOrAdd<V3TractionSystemRuntime>(
                        computerObject);
                    break;
                case V3SystemComputerRole.Aerodynamics:
                    system = GetOrAdd<
                        V3AerodynamicsSystemRuntime>(
                        computerObject);
                    break;
                default:
                    system = GetOrAdd<V3TelemetrySystemRuntime>(
                        computerObject);
                    break;
            }

            system.AttachComputer(computer);
        }

        private bool InstallIntegratedSensors(
            GameObject root,
            Rigidbody body,
            ChassisDefinition chassis)
        {
            if (root == null || chassis == null)
            {
                return false;
            }

            IReadOnlyList<V3DirectionalSensorDefinition> definitions =
                chassis.IntegratedSensors;
            if (definitions.Count == 0)
            {
                return true;
            }

            V3WorldEnvironmentProvider environment =
                GetOrAdd<V3WorldEnvironmentProvider>(root);
            for (int i = 0; i < definitions.Count; i++)
            {
                V3DirectionalSensorDefinition definition =
                    definitions[i];
                if (definition == null)
                {
                    Debug.LogError(
                        $"Integrated sensor entry {i} is empty.",
                        this);
                    return false;
                }

                string mountName =
                    "Sensor." + definition.Direction;
                Transform mount = FindNamedTransform(
                    root.transform,
                    mountName);
                if (mount == null)
                {
                    Debug.LogError(
                        $"Required sensor mount {mountName} is missing.",
                        this);
                    return false;
                }

                GameObject sensorObject =
                    definition.Prefab != null
                        ? Instantiate(
                            definition.Prefab,
                            mount.parent)
                        : new GameObject();
                sensorObject.name =
                    definition.Direction + " Sensor";
                sensorObject.transform.SetParent(
                    mount.parent,
                    false);
                sensorObject.transform.localPosition =
                    mount.localPosition;
                sensorObject.transform.localRotation =
                    mount.localRotation;
                sensorObject.transform.localScale = Vector3.one;
                V3DirectionalSensorDeviceRuntime sensor =
                    sensorObject.GetComponent<
                        V3DirectionalSensorDeviceRuntime>();
                if (sensor == null)
                {
                    sensor = sensorObject.AddComponent<
                        V3DirectionalSensorDeviceRuntime>();
                }

                if (!sensor.Initialize(
                    definition,
                    body,
                    null,
                    environment,
                    mountName))
                {
                    Debug.LogError(
                        $"Sensor {definition.DisplayName} failed: " +
                        sensor.FaultReason,
                        sensor);
                    return false;
                }
            }

            return true;
        }

        private static Transform FindNamedTransform(
            Transform root,
            string name)
        {
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private static void BindPhysicalParts(
            Rigidbody rootRigidbody,
            IReadOnlyList<RuntimePartInstance> installedParts,
            V3HoverConfiguration hoverConfiguration)
        {
            for (int i = 0; i < installedParts.Count; i++)
            {
                if (installedParts[i] is RuntimeThrusterInstance thruster)
                {
                    thruster.BindRigidbody(rootRigidbody);
                }
                else if (installedParts[i] is
                    RuntimeAerodynamicFinInstance fin)
                {
                    fin.BindRigidbody(rootRigidbody);
                }
                else if (installedParts[i] is
                    RuntimeCoolingModuleInstance cooling)
                {
                    cooling.BindCraft(
                        rootRigidbody.GetComponent<V3CraftRuntime>());
                }
                else if (installedParts[i] is
                    RuntimeSpringMountInstance spring)
                {
                    spring.ConfigureOperationalTravelLimits(
                        hoverConfiguration.CompressionLimit,
                        hoverConfiguration.ExtensionLimit);
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
            hover.Initialize(
                runtime,
                router,
                runtime.Build.HoverConfiguration);
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
                null,
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
            AssembledCraftChanged?.Invoke(null);
        }

        private void ClearEditorPreviewRootIfPresent()
        {
            Transform preview = transform.Find("__V3_EDITOR_PREVIEW__");
            if (preview == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                preview.gameObject.SetActive(false);
                Destroy(preview.gameObject);
            }
            else
            {
                DestroyImmediate(preview.gameObject);
            }
        }
    }
}
