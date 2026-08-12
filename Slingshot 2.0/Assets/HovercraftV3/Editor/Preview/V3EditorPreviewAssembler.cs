using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    [DisallowMultipleComponent]
    public sealed class V3EditorPreviewRoot : MonoBehaviour
    {
        public CraftBuildDefinition Build { get; private set; }
        public float TotalMassKg { get; private set; }
        public Vector3 CenterOfMass { get; private set; }
        public bool IsValid { get; private set; }
        public int SensorDeviceCount { get; private set; }
        public int ComputerRuntimeCount { get; private set; }
        public int SystemRuntimeCount { get; private set; }
        public string ValidationSummary { get; private set; } =
            string.Empty;

        public void Initialize(
            CraftBuildDefinition build,
            V3MassCalculationResult mass,
            BuildValidationReport report)
        {
            Build = build;
            TotalMassKg = mass != null ? mass.TotalMassKg : 0f;
            CenterOfMass =
                mass != null ? mass.FinalCenterOfMass : Vector3.zero;
            SensorDeviceCount =
                GetComponentsInChildren<
                    V3DirectionalSensorDeviceRuntime>(true).Length;
            ComputerRuntimeCount =
                GetComponentsInChildren<
                    V3SystemComputerRuntime>(true).Length;
            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(true);
            int systems = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IV3CraftSystemRuntime)
                {
                    systems++;
                }
            }

            SystemRuntimeCount = systems;
            IsValid = report != null && report.IsValid && mass != null;
            ValidationSummary = IsValid
                ? "Build topology and mass calculation are valid."
                : BuildSummary(report);
        }

        private static string BuildSummary(BuildValidationReport report)
        {
            if (report == null)
            {
                return "Validation returned no report.";
            }

            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity ==
                    BuildIssueSeverity.Error)
                {
                    return $"[{report.Issues[i].Code}] " +
                        report.Issues[i].Message;
                }
            }

            return "Preview mass calculation failed.";
        }
    }

    [InitializeOnLoad]
    public static class V3EditorPreviewAssembler
    {
        public const string PreviewRootName =
            "__V3_EDITOR_PREVIEW__";

        static V3EditorPreviewAssembler()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static V3EditorPreviewRoot BuildPreview(
            V3CraftAssembler assembler)
        {
            CraftBuildDefinition build = ResolveBuild(assembler);
            if (assembler == null || build == null)
            {
                throw new InvalidOperationException(
                    "Assign a CraftBuildDefinition before building preview.");
            }

            BuildValidationReport report =
                CraftBuildValidator.Validate(build);
            if (!report.IsValid)
            {
                throw new InvalidOperationException(
                    FirstValidationError(report));
            }

            ClearPreview(assembler);
            var previewHost = new GameObject(PreviewRootName);
            previewHost.tag = "EditorOnly";
            previewHost.hideFlags = HideFlags.DontSaveInBuild;
            previewHost.transform.SetParent(assembler.transform, false);
            V3EditorPreviewRoot marker =
                previewHost.AddComponent<V3EditorPreviewRoot>();

            GameObject chassis = null;
            try
            {
                chassis = (GameObject)PrefabUtility.InstantiatePrefab(
                    build.Chassis.Prefab,
                    previewHost.transform);
                chassis.name =
                    build.Chassis.DisplayName +
                    " (EDITOR PREVIEW)";
                chassis.transform.localPosition = Vector3.zero;
                chassis.transform.localRotation = Quaternion.identity;

                V3Socket[] sockets =
                    chassis.GetComponentsInChildren<V3Socket>(true);
                var byId =
                    new Dictionary<string, V3Socket>(
                        sockets.Length,
                        StringComparer.Ordinal);
                for (int i = 0; i < sockets.Length; i++)
                {
                    byId[sockets[i].SocketId] = sockets[i];
                }

                var parts = new List<RuntimePartInstance>();
                for (int i = 0;
                     i < build.Installations.Count;
                     i++)
                {
                    SocketInstallation installation =
                        build.Installations[i];
                    if (installation == null || installation.IsEmpty)
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(
                            installation.SocketId,
                            out V3Socket socket))
                    {
                        throw new InvalidOperationException(
                            "Preview could not find socket '" +
                            installation.SocketId + "'.");
                    }

                    Transform endpointParent = socket.MountTransform;
                    if (installation.Connector != null)
                    {
                        RuntimePartInstance connector =
                            InstantiatePreviewPart(
                                installation.Connector,
                                installation.SocketId,
                                endpointParent);
                        parts.Add(connector);
                        ConnectorChildMount mount =
                            connector.GetComponentInChildren<
                                ConnectorChildMount>(true);
                        if (mount == null)
                        {
                            throw new InvalidOperationException(
                                "Connector preview has no child mount: " +
                                installation.Connector.DisplayName);
                        }

                        endpointParent = mount.MountTransform;
                    }

                    if (installation.Endpoint != null)
                    {
                        parts.Add(InstantiatePreviewPart(
                            installation.Endpoint,
                            installation.SocketId,
                            endpointParent));
                    }
                }

                InstantiatePreviewSensors(
                    chassis,
                    build.Chassis.IntegratedSensors);

                Rigidbody body = chassis.GetComponent<Rigidbody>();
                V3MassCalculationResult mass = null;
                if (!V3CraftMassCalculator.TryCalculate(
                        build,
                        body,
                        parts,
                        out mass,
                        out string massError))
                {
                    throw new InvalidOperationException(massError);
                }

                marker.Initialize(build, mass, report);
                MakeNonSimulating(previewHost);
                Selection.activeGameObject = previewHost;
                SceneView.RepaintAll();
                return marker;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(previewHost);
                throw;
            }
        }

        public static void ClearPreview(V3CraftAssembler assembler)
        {
            if (assembler == null)
            {
                return;
            }

            Transform preview =
                assembler.transform.Find(PreviewRootName);
            if (preview != null)
            {
                UnityEngine.Object.DestroyImmediate(preview.gameObject);
            }
        }

        public static BuildValidationReport Validate(
            V3CraftAssembler assembler)
        {
            return CraftBuildValidator.Validate(
                ResolveBuild(assembler));
        }

        public static CraftBuildDefinition ResolveBuild(
            V3CraftAssembler assembler)
        {
            if (assembler == null)
            {
                return null;
            }

            V3CraftTestSpawner spawner =
                ResolveControllingSpawner(assembler);
            return spawner != null && spawner.SelectedBuild != null
                ? spawner.SelectedBuild
                : assembler.Build;
        }

        public static V3CraftTestSpawner ResolveControllingSpawner(
            V3CraftAssembler assembler)
        {
            if (assembler == null)
            {
                return null;
            }

            V3CraftTestSpawner[] spawners =
                UnityEngine.Object.FindObjectsByType<
                    V3CraftTestSpawner>(FindObjectsInactive.Include);
            for (int i = 0; i < spawners.Length; i++)
            {
                if (spawners[i].Assembler == assembler)
                {
                    return spawners[i];
                }
            }

            return null;
        }

        public static void SetAuthoritativeBuild(
            V3CraftAssembler assembler,
            CraftBuildDefinition build)
        {
            if (assembler == null)
            {
                return;
            }

            V3CraftTestSpawner spawner =
                ResolveControllingSpawner(assembler);
            if (spawner != null)
            {
                spawner.SelectedBuild = build;
            }

            assembler.SetBuild(build);
        }

        private static void InstantiatePreviewSensors(
            GameObject chassis,
            IReadOnlyList<V3DirectionalSensorDefinition> definitions)
        {
            if (chassis == null || definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                V3DirectionalSensorDefinition definition =
                    definitions[i];
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"Preview sensor entry {i} is empty.");
                }

                string mountName =
                    "Sensor." + definition.Direction;
                Transform mount = FindNamedTransform(
                    chassis.transform,
                    mountName);
                if (mount == null)
                {
                    throw new InvalidOperationException(
                        "Preview could not find sensor mount '" +
                        mountName + "'.");
                }

                GameObject instance;
                if (definition.Prefab != null)
                {
                    instance = (GameObject)
                        PrefabUtility.InstantiatePrefab(
                            definition.Prefab,
                            mount.parent);
                }
                else
                {
                    instance =
                        GameObject.CreatePrimitive(PrimitiveType.Cube);
                    instance.transform.SetParent(mount.parent, false);
                    instance.transform.localScale =
                        definition.LocalVisualScale;
                }

                instance.name =
                    definition.DisplayName + " (EDITOR PREVIEW)";
                instance.transform.localPosition =
                    mount.localPosition;
                instance.transform.localRotation =
                    mount.localRotation;
                V3DirectionalSensorDeviceRuntime runtime =
                    instance.GetComponent<
                        V3DirectionalSensorDeviceRuntime>();
                if (runtime == null)
                {
                    runtime = instance.AddComponent<
                        V3DirectionalSensorDeviceRuntime>();
                }

                var serialized = new SerializedObject(runtime);
                serialized.FindProperty("definition")
                    .objectReferenceValue = definition;
                serialized.FindProperty("firmware")
                    .objectReferenceValue = definition.Firmware;
                serialized.FindProperty("mountName")
                    .stringValue = mountName;
                serialized.FindProperty("runtimeDeviceId")
                    .stringValue = definition.StableId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
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

        private static RuntimePartInstance InstantiatePreviewPart(
            PartDefinition definition,
            string socketId,
            Transform parent)
        {
            GameObject instance =
                (GameObject)PrefabUtility.InstantiatePrefab(
                    definition.Prefab,
                    parent);
            instance.name = definition.DisplayName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            RuntimePartInstance runtime =
                instance.GetComponent<RuntimePartInstance>();
            if (runtime == null)
            {
                runtime = instance.AddComponent<RuntimePartInstance>();
            }

            runtime.Initialize(definition, socketId);
            return runtime;
        }

        private static void MakeNonSimulating(GameObject root)
        {
            Rigidbody[] bodies =
                root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = bodies.Length - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(bodies[i]);
            }

            Collider[] colliders =
                root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Behaviour[] behaviours =
                root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is V3EditorPreviewRoot))
                {
                    behaviours[i].enabled = false;
                }
            }
        }

        private static string FirstValidationError(
            BuildValidationReport report)
        {
            if (report == null)
            {
                return "Build validation returned no report.";
            }

            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity ==
                    BuildIssueSeverity.Error)
                {
                    return $"[{report.Issues[i].Code}] " +
                        report.Issues[i].Message;
                }
            }

            return "The selected build is invalid.";
        }

        private static void OnPlayModeChanged(
            PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode &&
                change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            V3CraftAssembler[] assemblers =
                UnityEngine.Object.FindObjectsByType<V3CraftAssembler>(
                    FindObjectsInactive.Include);
            for (int i = 0; i < assemblers.Length; i++)
            {
                if (change == PlayModeStateChange.ExitingEditMode)
                {
                    ClearPreview(assemblers[i]);
                }
                else if (assemblers[i].PreviewBuildInEditor &&
                         ResolveBuild(assemblers[i]) != null)
                {
                    try
                    {
                        BuildPreview(assemblers[i]);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, assemblers[i]);
                    }
                }
            }
        }
    }
}
