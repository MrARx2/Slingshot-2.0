using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V2ReferencePrototypeGenerator
    {
        public const string PrototypeRoot = "Assets/HovercraftV3/Prototype";
        public const string ChassisPrefabPath = PrototypeRoot + "/Prefabs/ApexV3_V2Reference_Chassis.prefab";
        public const string ReferenceBuildPath = PrototypeRoot + "/Builds/ApexV3_V2Reference.asset";
        public const string GimbalBuildPath = PrototypeRoot + "/Builds/ApexV3_MainGimbalDemo.asset";
        public const string SpringBuildPath =
            PrototypeRoot + "/Builds/ApexV3_HoverSpringDemo.asset";
        public const string CatalogPath = PrototypeRoot + "/Definitions/V2Reference_PartCatalog.asset";
        private const string MaterialsRoot = PrototypeRoot + "/Materials";

        private const float ChassisMassKg = 8430f;

        [MenuItem("Tools/Hovercraft V3/Generate V2 Reference Prototype")]
        public static void Generate()
        {
            EnsureFolders();

            GameObject mainPrefab = CreatePartPrefab(
                "V2Reference_MainThruster",
                new Vector3(1.35f, 0.8f, 1.6f));
            GameObject brakePrefab = CreatePartPrefab(
                "V2Reference_BrakeThruster",
                new Vector3(1.1f, 0.65f, 1.2f));
            GameObject hoverPrefab = CreatePartPrefab(
                "V2Reference_HoverThruster",
                new Vector3(0.62f, 0.42f, 0.24f));
            GameObject roofPrefab = CreatePartPrefab(
                "V2Reference_RoofThruster",
                new Vector3(0.58f, 0.38f, 0.22f));
            GameObject strafePrefab = CreatePartPrefab(
                "V2Reference_StrafeThruster",
                new Vector3(0.5f, 0.4f, 0.65f));
            GameObject corePrefab = CreatePartPrefab(
                "V2Reference_EnergyCore",
                new Vector3(1.2f, 0.8f, 1.4f));
            GameObject cockpitPrefab = CreatePartPrefab(
                "V2Reference_Cockpit",
                new Vector3(1.4f, 0.75f, 1.8f));
            GameObject fixedAdapterPrefab = CreateConnectorPrefab(
                "V2Reference_FixedAdapter",
                new Vector3(0.85f, 0.7f, 0.6f));
            GameObject gimbalPrefab = CreateConnectorPrefab(
                "V2Reference_MainGimbal",
                new Vector3(1f, 0.9f, 0.6f),
                -0.65f,
                true);
            GameObject springPrefab = CreateConnectorPrefab(
                "V2Reference_HoverSpringMount",
                new Vector3(0.55f, 0.35f, 0.3f),
                -0.35f,
                true);

            ThrusterDefinition main = CreateThruster(
                "V2Reference_MainThruster",
                "Thruster.Main.V2Reference",
                PartRole.Propulsion,
                PartSize.Large,
                mainPrefab,
                SocketFamily.HeavyPropulsion,
                300f,
                480000f,
                1.5f,
                0f,
                0f,
                480f);
            SetFloat(main, "normalOutputMultiplier", 1.5f);
            SetFloat(main, "overloadOutputMultiplier", 1.875f);
            SetNestedFloat(main, "thermal", "thermalCapacity", 20f);
            SetNestedFloat(main, "thermal", "passiveCoolingPerSecond", 0.38f);
            SetNestedFloat(main, "thermal", "hotOutputLimitAtOverheat", 0.92f);
            ThrusterDefinition brake = CreateThruster(
                "V2Reference_BrakeThruster",
                "Thruster.Brake.V2Reference",
                PartRole.Braking,
                PartSize.Large,
                brakePrefab,
                SocketFamily.HeavyPropulsion,
                250f,
                1700000f,
                1f,
                0f,
                0f,
                1700f);
            ThrusterDefinition hover = CreateThruster(
                "V2Reference_HoverThruster",
                "Thruster.Hover.V2Reference",
                PartRole.Hover,
                PartSize.Medium,
                hoverPrefab,
                SocketFamily.HoverVerticalControl,
                100f,
                160000f,
                7f,
                0f,
                0f,
                160f);
            ThrusterDefinition roof = CreateThruster(
                "V2Reference_RoofThruster",
                "Thruster.Roof.V2Reference",
                PartRole.Control,
                PartSize.Medium,
                roofPrefab,
                SocketFamily.HoverVerticalControl,
                80f,
                160000f,
                7f,
                50f,
                50f,
                160f);
            ThrusterDefinition strafe = CreateThruster(
                "V2Reference_StrafeThruster",
                "Thruster.Strafe.V2Reference",
                PartRole.Control,
                PartSize.Medium,
                strafePrefab,
                SocketFamily.LateralControl,
                75f,
                800000f,
                1f,
                0f,
                0f,
                800f);

            EnergyCoreDefinition core =
                CreateOrLoad<EnergyCoreDefinition>(
                    PrototypeRoot + "/Definitions/V2Reference_EnergyCore.asset");
            ConfigureBasePart(
                core,
                "EnergyCore.Main.V2Reference",
                "V2 Reference EnergyCore",
                PartRole.Energy,
                PartSize.Large,
                corePrefab,
                new[] { SocketFamily.EnergyBay },
                600f,
                PowerDomain.Systems,
                25f,
                25f,
                false,
                false,
                PartCapability.PowerDistribution | PartCapability.ThermalTelemetry);
            SetFloat(core, "continuousOutput", 4500f);
            SetFloat(core, "propulsionChannelCeiling", 4500f);
            SetFloat(core, "systemsChannelCeiling", 1000f);
            SetFloat(core, "temporaryPeakOutput", 0f);

            CockpitDefinition cockpit =
                CreateOrLoad<CockpitDefinition>(
                    PrototypeRoot + "/Definitions/V2Reference_Cockpit.asset");
            ConfigureBasePart(
                cockpit,
                "Cockpit.Main.V2Reference",
                "V2 Reference Cockpit",
                PartRole.Cockpit,
                PartSize.Large,
                cockpitPrefab,
                new[] { SocketFamily.Cockpit },
                400f,
                PowerDomain.Systems,
                20f,
                100f,
                true,
                true,
                PartCapability.DriveControl |
                PartCapability.HoverControl |
                PartCapability.Stabilization |
                PartCapability.VectoringControl);
            SetInt(cockpit, "equipmentSlots", 3);
            SetInt(cockpit, "displaySlots", 2);

            FixedAdapterDefinition fixedAdapter =
                CreateOrLoad<FixedAdapterDefinition>(
                    PrototypeRoot + "/Definitions/V2Reference_FixedAdapter.asset");
            ConfigureConnector(
                fixedAdapter,
                "Connector.Fixed.V2Reference",
                "V2 Reference Fixed Adapter",
                ConnectorKind.FixedAdapter,
                fixedAdapterPrefab,
                60f,
                10f);

            GimbalDefinition gimbal =
                CreateOrLoad<GimbalDefinition>(
                    PrototypeRoot + "/Definitions/V2Reference_MainGimbal.asset");
            ConfigureConnector(
                gimbal,
                "Connector.Gimbal.Main.V2Reference",
                "V2 Reference Main Gimbal",
                ConnectorKind.Gimbal,
                gimbalPrefab,
                120f,
                50f);
            SetBool(gimbal, "supportsPitch", true);
            SetBool(gimbal, "supportsYaw", true);
            SetFloat(gimbal, "maximumPitchDegrees", 30f);
            SetFloat(gimbal, "maximumYawDegrees", 30f);
            SetFloat(gimbal, "rotationSpeedDegreesPerSecond", 180f);
            SetFloat(gimbal, "angularAccelerationDegreesPerSecondSquared", 720f);
            SetFloat(gimbal, "actuatorTorqueNm", 25000f);

            SpringMountDefinition spring =
                CreateOrLoad<SpringMountDefinition>(
                    PrototypeRoot +
                    "/Definitions/V2Reference_HoverSpringMount.asset");
            ConfigureSpringMount(spring, springPrefab);

            GameObject chassisPrefab = CreateChassisPrefab();
            ChassisDefinition chassis =
                CreateOrLoad<ChassisDefinition>(
                    PrototypeRoot + "/Definitions/ApexV3_V2Reference_Chassis.asset");
            ConfigureChassis(chassis, chassisPrefab);

            PartCatalog catalog = CreateOrLoad<PartCatalog>(CatalogPath);
            SetCatalog(
                catalog,
                main,
                brake,
                hover,
                roof,
                strafe,
                core,
                cockpit,
                fixedAdapter,
                gimbal,
                spring);

            CraftBuildDefinition reference =
                CreateOrLoad<CraftBuildDefinition>(ReferenceBuildPath);
            ConfigureReferenceBuild(
                reference,
                chassis,
                catalog,
                main,
                brake,
                hover,
                roof,
                strafe,
                core,
                cockpit);
            CalibrateReferenceCenterOfMass(chassis, reference);
            ConfigureBuildIdentity(
                reference,
                "Apex V3 Reference",
                "Hyperclass Prototype",
                "Direct-Mount / Balanced",
                "The lowest-mass, most direct Apex V3 baseline. Every thruster " +
                "is rigidly mounted, giving predictable response and making this " +
                "the control build for comparisons.",
                "Expect neutral, immediate reactions. Compare other layouts " +
                "against this build during hard steering, landing, and uneven " +
                "surface tests rather than steady flat-ground cruising.");

            CraftBuildDefinition gimbalBuild =
                CreateOrLoad<CraftBuildDefinition>(GimbalBuildPath);
            Undo.ClearUndo(reference);
            gimbalBuild.CopySelectionsFrom(reference);
            SocketInstallation rear = gimbalBuild.FindInstallation("Propulsion.Rear.Center");
            rear.SetConnector(gimbal, main);
            ConfigureBuildIdentity(
                gimbalBuild,
                "Apex V3 Vector",
                "Hyperclass Vectoring Prototype",
                "Rear-Gimbal Vectoring",
                "Adds a powered +/-30 degree gimbal to the rear main thruster. " +
                "The mount adds 120 kg and gives the craft stronger pitch and " +
                "yaw authority while the main engine is producing thrust.",
                "The difference is strongest when steering or pitching under " +
                "acceleration. Gimbal travel and actuator response make it less " +
                "instantaneous than a fixed mount.");
            EditorUtility.SetDirty(gimbalBuild);

            CraftBuildDefinition springBuild =
                CreateOrLoad<CraftBuildDefinition>(SpringBuildPath);
            springBuild.CopySelectionsFrom(reference);
            SocketInstallation frontLeftHover =
                springBuild.FindInstallation("Hover.Front.Left.Bottom");
            frontLeftHover.SetConnector(spring, hover);
            ConfigureBuildIdentity(
                springBuild,
                "Apex V3 Terrain",
                "Hyperclass Compliance Prototype",
                "Front-Left Spring Hover",
                "Adds 0.42 m of suspension travel to the front-left hover " +
                "thruster. The mount adds 35 kg and isolates that corner from " +
                "sharp height changes and landing impacts.",
                "The difference is intentionally corner-specific. Test it on " +
                "the ramp, during landings, or across uneven surfaces; it will " +
                "feel almost identical to the reference build on smooth ground.");
            EditorUtility.SetDirty(springBuild);

            CreateAssemblerPrefab("ApexV3_V2Reference_Assembler", reference);
            CreateAssemblerPrefab("ApexV3_MainGimbalDemo_Assembler", gimbalBuild);
            CreateAssemblerPrefab("ApexV3_HoverSpringDemo_Assembler", springBuild);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateGeneratedBuild(reference, 11000f);
            ValidateGeneratedBuild(gimbalBuild, 11120f);
            ValidateGeneratedBuild(springBuild, 11035f);
            Debug.Log(
                "Hovercraft V3 V2-reference prototype generated and validated: " +
                $"{PrototypeRoot}");
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/HovercraftV3", "Prototype");
            EnsureFolder(PrototypeRoot, "Prefabs");
            EnsureFolder(PrototypeRoot, "Definitions");
            EnsureFolder(PrototypeRoot, "Builds");
            EnsureFolder(PrototypeRoot, "Materials");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static GameObject CreatePartPrefab(string name, Vector3 visualScale)
        {
            var root = new GameObject(name);
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Primitive Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition =
                new Vector3(0f, 0f, -visualScale.z * 0.5f);
            visual.transform.localScale = visualScale;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            ApplyPrototypeMaterial(
                visual,
                ResolvePartMaterialName(name),
                ResolvePartColor(name));
            string path = $"{PrototypeRoot}/Prefabs/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateConnectorPrefab(
            string name,
            Vector3 visualScale,
            float childMountOffset = -0.65f,
            bool visualFollowsChildMount = false)
        {
            var root = new GameObject(name);
            var childMountObject = new GameObject("Child Mount");
            childMountObject.transform.SetParent(root.transform, false);
            childMountObject.transform.localPosition =
                new Vector3(0f, 0f, childMountOffset);
            ConnectorChildMount mount = childMountObject.AddComponent<ConnectorChildMount>();
            var serializedMount = new SerializedObject(mount);
            serializedMount.FindProperty("mountTransform").objectReferenceValue =
                childMountObject.transform;
            serializedMount.ApplyModifiedPropertiesWithoutUndo();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = visualFollowsChildMount
                ? "Actuated Mount Visual"
                : "Connector Visual";
            Transform visualParent = visualFollowsChildMount
                ? childMountObject.transform
                : root.transform;
            visual.transform.SetParent(visualParent, false);
            visual.transform.localPosition = new Vector3(
                0f,
                0f,
                visualFollowsChildMount
                    ? -childMountOffset * 0.5f
                    : childMountOffset * 0.5f);
            visual.transform.localScale = visualScale;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            ApplyPrototypeMaterial(
                visual,
                "Connector",
                new Color(1f, 0.38f, 0.06f));

            string path = $"{PrototypeRoot}/Prefabs/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateChassisPrefab()
        {
            var root = new GameObject("ApexV3_V2Reference_Chassis");
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 11000f;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 50f;

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(3.6f, 1.55f, 7.8f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Primitive Chassis Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = collider.size;
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            ApplyPrototypeMaterial(
                visual,
                "Chassis",
                new Color(0.12f, 0.15f, 0.2f));

            AddSocket(
                root.transform,
                "Propulsion.Rear.Center",
                new Vector3(0f, 0f, -3.9f),
                Vector3.forward,
                SocketFamily.HeavyPropulsion,
                PartSize.Large,
                2000f,
                900000f,
                1000f,
                new[] { EndpointCategory.Thruster },
                new[] { ConnectorKind.FixedAdapter, ConnectorKind.Gimbal });
            AddSocket(
                root.transform,
                "Braking.Front.Center",
                new Vector3(0f, 0f, 3.9f),
                Vector3.back,
                SocketFamily.HeavyPropulsion,
                PartSize.Large,
                1500f,
                1800000f,
                1800f,
                new[] { EndpointCategory.Thruster },
                Array.Empty<ConnectorKind>());

            AddVerticalThrusterOutriggers(root.transform, "Front", 3f);
            AddVerticalThrusterOutriggers(root.transform, "Rear", -3f);

            AddSocket(
                root.transform,
                "Core.Main",
                new Vector3(0f, -0.1f, -0.5f),
                Vector3.forward,
                SocketFamily.EnergyBay,
                PartSize.Large,
                1000f,
                0f,
                0f,
                new[] { EndpointCategory.EnergyCore },
                Array.Empty<ConnectorKind>(),
                false,
                true);
            AddSocket(
                root.transform,
                "Cockpit.Main",
                new Vector3(0f, 0.2f, 1.2f),
                Vector3.forward,
                SocketFamily.Cockpit,
                PartSize.Large,
                800f,
                0f,
                200f,
                new[] { EndpointCategory.Cockpit },
                Array.Empty<ConnectorKind>());

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ChassisPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static void AddVerticalThrusterOutriggers(
            Transform root,
            string longitudinalName,
            float z)
        {
            AddVerticalThrusterOutrigger(
                root,
                "Left",
                longitudinalName,
                -1f,
                z);
            AddVerticalThrusterOutrigger(
                root,
                "Right",
                longitudinalName,
                1f,
                z);
        }

        private static void AddVerticalThrusterOutrigger(
            Transform root,
            string sideName,
            string longitudinalName,
            float sideSign,
            float z)
        {
            const float chassisHalfWidth = 1.8f;
            const float outriggerLength = 0.85f;
            const float outriggerHeight = 0.22f;
            const float outriggerDepth = 0.95f;
            const float hullOverlap = 0.05f;
            const float verticalSocketDepth = 0.12f;

            float centerX = sideSign *
                (chassisHalfWidth - hullOverlap + outriggerLength * 0.5f);
            var outriggerObject = new GameObject(
                $"Vertical Thruster Outrigger {longitudinalName} {sideName}");
            outriggerObject.transform.SetParent(root, false);
            outriggerObject.transform.localPosition =
                new Vector3(centerX, 0f, z);

            GameObject visual =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Outrigger Visual";
            visual.transform.SetParent(outriggerObject.transform, false);
            visual.transform.localScale = new Vector3(
                outriggerLength,
                outriggerHeight,
                outriggerDepth);
            UnityEngine.Object.DestroyImmediate(
                visual.GetComponent<Collider>());
            ApplyPrototypeMaterial(
                visual,
                "Outrigger",
                new Color(0.24f, 0.29f, 0.36f));

            string hoverId =
                $"Hover.{longitudinalName}.{sideName}.Bottom";
            string pairedHoverId =
                $"Hover.{longitudinalName}." +
                $"{(sideName == "Left" ? "Right" : "Left")}.Bottom";
            AddSocket(
                outriggerObject.transform,
                hoverId,
                new Vector3(0f, -outriggerHeight * 0.5f, 0f),
                Vector3.up,
                SocketFamily.HoverVerticalControl,
                PartSize.Medium,
                500f,
                1200000f,
                1400f,
                new[] { EndpointCategory.Thruster },
                new[] { ConnectorKind.SpringMount },
                true,
                true,
                pairedHoverId,
                verticalSocketDepth);

            string controlId =
                $"Control.{longitudinalName}.{sideName}.Top";
            string pairedControlId =
                $"Control.{longitudinalName}." +
                $"{(sideName == "Left" ? "Right" : "Left")}.Top";
            AddSocket(
                outriggerObject.transform,
                controlId,
                new Vector3(0f, outriggerHeight * 0.5f, 0f),
                Vector3.down,
                SocketFamily.HoverVerticalControl,
                PartSize.Medium,
                500f,
                1200000f,
                1400f,
                new[] { EndpointCategory.Thruster },
                Array.Empty<ConnectorKind>(),
                true,
                true,
                pairedControlId,
                verticalSocketDepth);

            string strafeId =
                $"Strafe.{sideName}.{longitudinalName}";
            string pairedStrafeId =
                $"Strafe.{(sideName == "Left" ? "Right" : "Left")}." +
                longitudinalName;
            AddSocket(
                outriggerObject.transform,
                strafeId,
                new Vector3(sideSign * outriggerLength * 0.5f, 0f, 0f),
                sideSign < 0f ? Vector3.right : Vector3.left,
                SocketFamily.LateralControl,
                PartSize.Medium,
                300f,
                900000f,
                900f,
                new[] { EndpointCategory.Thruster },
                Array.Empty<ConnectorKind>(),
                true,
                true,
                pairedStrafeId);
        }

        private static void AddSocket(
            Transform root,
            string socketId,
            Vector3 localPosition,
            Vector3 forward,
            SocketFamily family,
            PartSize size,
            float maximumMassKg,
            float maximumForceN,
            float power,
            EndpointCategory[] endpoints,
            ConnectorKind[] connectors,
            bool hasPower = true,
            bool hasData = true,
            string pairedSocketId = "",
            float socketDepthOverride = 0f)
        {
            var socketObject = new GameObject(socketId);
            socketObject.transform.SetParent(root, false);
            socketObject.transform.localPosition = localPosition;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up;
            socketObject.transform.localRotation = Quaternion.LookRotation(forward, up);

            bool externalThrusterSocket =
                Array.IndexOf(endpoints, EndpointCategory.Thruster) >= 0;
            Transform endpointMount = socketObject.transform;
            if (externalThrusterSocket)
            {
                float socketDepth = socketDepthOverride > 0f
                    ? socketDepthOverride
                    : GetSocketDepth(size);
                Vector3 socketScale = GetSocketVisualScale(size, socketDepth);
                GameObject socketVisual =
                    GameObject.CreatePrimitive(PrimitiveType.Cube);
                socketVisual.name = "Socket Visual";
                socketVisual.transform.SetParent(socketObject.transform, false);
                socketVisual.transform.localPosition =
                    new Vector3(0f, 0f, -socketDepth * 0.5f);
                socketVisual.transform.localScale = socketScale;
                UnityEngine.Object.DestroyImmediate(
                    socketVisual.GetComponent<Collider>());
                ApplyPrototypeMaterial(
                    socketVisual,
                    "Socket",
                    new Color(1f, 0.78f, 0.08f));

                var endpointMountObject = new GameObject("Endpoint Mount");
                endpointMountObject.transform.SetParent(
                    socketObject.transform,
                    false);
                endpointMountObject.transform.localPosition =
                    new Vector3(0f, 0f, -socketDepth);
                endpointMount = endpointMountObject.transform;
            }

            V3Socket socket = socketObject.AddComponent<V3Socket>();
            var serialized = new SerializedObject(socket);
            serialized.FindProperty("socketId").stringValue = socketId;
            serialized.FindProperty("debugName").stringValue = socketId;
            serialized.FindProperty("family").enumValueIndex = (int)family;
            serialized.FindProperty("size").enumValueIndex = (int)size;
            serialized.FindProperty("mountTransform").objectReferenceValue =
                endpointMount;
            serialized.FindProperty("maximumSupportedMassKg").floatValue = maximumMassKg;
            serialized.FindProperty("maximumSupportedForceN").floatValue = maximumForceN;
            serialized.FindProperty("powerAvailability").floatValue = power;
            serialized.FindProperty("hasPower").boolValue = hasPower;
            serialized.FindProperty("hasData").boolValue = hasData;
            serialized.FindProperty("pairedSocketId").stringValue = pairedSocketId;
            SetEnumArray(serialized.FindProperty("allowedDirectEndpoints"), endpoints);
            SetEnumArray(serialized.FindProperty("allowedConnectors"), connectors);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static float GetSocketDepth(PartSize size)
        {
            switch (size)
            {
                case PartSize.Large:
                    return 0.4f;
                case PartSize.Small:
                    return 0.2f;
                default:
                    return 0.28f;
            }
        }

        private static Vector3 GetSocketVisualScale(
            PartSize size,
            float depth)
        {
            switch (size)
            {
                case PartSize.Large:
                    return new Vector3(1.15f, 0.72f, depth);
                case PartSize.Small:
                    return new Vector3(0.38f, 0.28f, depth);
                default:
                    return new Vector3(0.58f, 0.42f, depth);
            }
        }

        private static string ResolvePartMaterialName(string name)
        {
            if (name.IndexOf("EnergyCore", StringComparison.Ordinal) >= 0)
            {
                return "EnergyCore";
            }

            if (name.IndexOf("Cockpit", StringComparison.Ordinal) >= 0)
            {
                return "Cockpit";
            }

            return "Thruster";
        }

        private static Color ResolvePartColor(string name)
        {
            if (name.IndexOf("EnergyCore", StringComparison.Ordinal) >= 0)
            {
                return new Color(0.12f, 0.8f, 0.32f);
            }

            if (name.IndexOf("Cockpit", StringComparison.Ordinal) >= 0)
            {
                return new Color(0.15f, 0.75f, 0.88f);
            }

            return new Color(0.12f, 0.42f, 0.95f);
        }

        private static void ApplyPrototypeMaterial(
            GameObject target,
            string materialName,
            Color color)
        {
            if (target == null)
            {
                return;
            }

            string path = $"{MaterialsRoot}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                if (shader == null)
                {
                    return;
                }

                material = new Material(shader)
                {
                    name = materialName
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            EditorUtility.SetDirty(material);
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static ThrusterDefinition CreateThruster(
            string assetName,
            string stableId,
            PartRole role,
            PartSize size,
            GameObject prefab,
            SocketFamily family,
            float massKg,
            float forceN,
            float overloadMultiplier,
            float risePerSecond,
            float fallPerSecond,
            float maximumPower)
        {
            ThrusterDefinition definition =
                CreateOrLoad<ThrusterDefinition>(
                    $"{PrototypeRoot}/Definitions/{assetName}.asset");
            ConfigureBasePart(
                definition,
                stableId,
                assetName.Replace('_', ' '),
                role,
                size,
                prefab,
                new[] { family },
                massKg,
                PowerDomain.Propulsion,
                maximumPower * 0.02f,
                maximumPower,
                true,
                true,
                PartCapability.ThermalTelemetry);
            SetFloat(definition, "maximumForwardForceN", forceN);
            SetFloat(definition, "maximumReverseForceN", 0f);
            SetFloat(definition, "minimumControllableOutput", 0f);
            SetFloat(definition, "thrustRisePerSecond", risePerSecond);
            SetFloat(definition, "thrustFallPerSecond", fallPerSecond);
            SetFloat(definition, "normalOutputMultiplier", overloadMultiplier);
            SetFloat(definition, "overloadOutputMultiplier", overloadMultiplier);
            SetVector3(definition, "localThrustDirection", Vector3.forward);
            SetVector3(definition, "localForceOrigin", Vector3.zero);
            return definition;
        }

        private static void ConfigureBasePart(
            PartDefinition definition,
            string stableId,
            string displayName,
            PartRole role,
            PartSize size,
            GameObject prefab,
            SocketFamily[] parentFamilies,
            float massKg,
            PowerDomain powerDomain,
            float idlePower,
            float maximumPower,
            bool requiresPower,
            bool requiresData,
            PartCapability capabilities)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("stableId").stringValue = stableId;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("productFamily").stringValue = "V2 Reference";
            serialized.FindProperty("description").stringValue =
                "Prototype parity asset generated from the Apex Hyperclass V2 Rev1.1 baseline.";
            serialized.FindProperty("role").enumValueIndex = (int)role;
            serialized.FindProperty("size").enumValueIndex = (int)size;
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("prototype").boolValue = true;
            serialized.FindProperty("requiresPower").boolValue = requiresPower;
            serialized.FindProperty("requiresData").boolValue = requiresData;
            serialized.FindProperty("providedCapabilities").intValue = (int)capabilities;
            SetEnumArray(serialized.FindProperty("compatibleParentFamilies"), parentFamilies);

            SerializedProperty physical = serialized.FindProperty("physical");
            physical.FindPropertyRelative("massKg").floatValue = massKg;
            physical.FindPropertyRelative("localCenterOfMass").vector3Value = Vector3.zero;
            physical.FindPropertyRelative("maximumStructuralLoadN").floatValue = 0f;
            physical.FindPropertyRelative("maximumSupportedForceN").floatValue = 0f;

            SerializedProperty power = serialized.FindProperty("power");
            power.FindPropertyRelative("domain").enumValueIndex = (int)powerDomain;
            power.FindPropertyRelative("idleDemand").floatValue = idlePower;
            power.FindPropertyRelative("maximumDemand").floatValue = maximumPower;
            power.FindPropertyRelative("disabledDemand").floatValue = 0f;
            power.FindPropertyRelative("efficiency").floatValue = 1f;
            power.FindPropertyRelative("overloadDemandMultiplier").floatValue = 1.55f;

            SerializedProperty thermal = serialized.FindProperty("thermal");
            thermal.FindPropertyRelative("enabled").boolValue = true;
            thermal.FindPropertyRelative("ambientTemperatureC").floatValue = 20f;
            thermal.FindPropertyRelative("maximumSafeTemperatureC").floatValue = 90f;
            thermal.FindPropertyRelative("overheatTemperatureC").floatValue = 120f;
            thermal.FindPropertyRelative("restartTemperatureC").floatValue = 75f;
            thermal.FindPropertyRelative("hotOutputLimitAtOverheat").floatValue = 0.5f;
            thermal.FindPropertyRelative("coolingLockoutSeconds").floatValue = 2f;
            thermal.FindPropertyRelative("thermalCapacity").floatValue = 100f;
            thermal.FindPropertyRelative("passiveCoolingPerSecond").floatValue = 2f;
            thermal.FindPropertyRelative("idleHeatPerSecond").floatValue = 0.5f;
            thermal.FindPropertyRelative("maximumOutputHeatPerSecond").floatValue = 25f;
            thermal.FindPropertyRelative("overloadHeatMultiplier").floatValue = 2.2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static void ConfigureConnector(
            ConnectorDefinition connector,
            string stableId,
            string displayName,
            ConnectorKind kind,
            GameObject prefab,
            float massKg,
            float maximumPower)
        {
            ConfigureBasePart(
                connector,
                stableId,
                displayName,
                PartRole.Control,
                PartSize.Large,
                prefab,
                new[] { SocketFamily.HeavyPropulsion },
                massKg,
                PowerDomain.Systems,
                maximumPower * 0.2f,
                maximumPower,
                true,
                true,
                PartCapability.VectoringControl | PartCapability.ThermalTelemetry);
            SetEnum(connector, "kind", kind);
            SetEnum(connector, "childSocketFamily", SocketFamily.HeavyPropulsion);
            SetEnum(connector, "childSize", PartSize.Large);
            SetEnumArray(connector, "allowedEndpointCategories", EndpointCategory.Thruster);
            SetFloat(connector, "supportedEndpointMassKg", 600f);
            SetFloat(connector, "supportedEndpointForceN", 900000f);
            SetFloat(connector, "childPowerAvailability", 700f);
            SetBool(connector, "childHasPower", true);
            SetBool(connector, "childHasData", true);
        }

        private static void ConfigureSpringMount(
            SpringMountDefinition spring,
            GameObject prefab)
        {
            ConfigureBasePart(
                spring,
                "Connector.Spring.Hover.V2Reference",
                "V2 Reference Hover Spring Mount",
                PartRole.Control,
                PartSize.Medium,
                prefab,
                new[] { SocketFamily.HoverVerticalControl },
                35f,
                PowerDomain.None,
                0f,
                0f,
                false,
                false,
                PartCapability.None);
            SetNestedBool(spring, "thermal", "enabled", false);
            SetEnum(spring, "kind", ConnectorKind.SpringMount);
            SetEnum(
                spring,
                "childSocketFamily",
                SocketFamily.HoverVerticalControl);
            SetEnum(spring, "childSize", PartSize.Medium);
            SetEnumArray(
                spring,
                "allowedEndpointCategories",
                EndpointCategory.Thruster);
            SetFloat(spring, "supportedEndpointMassKg", 200f);
            SetFloat(spring, "supportedEndpointForceN", 1200000f);
            SetFloat(spring, "childPowerAvailability", 1400f);
            SetBool(spring, "childHasPower", true);
            SetBool(spring, "childHasData", true);
            SetVector3(spring, "localMovementAxis", Vector3.forward);
            SetFloat(spring, "compressionLimitM", 0.3f);
            SetFloat(spring, "extensionLimitM", 0.12f);
            SetFloat(spring, "springStiffness", 600000f);
            SetFloat(spring, "damping", 18000f);
        }

        private static void ConfigureChassis(
            ChassisDefinition chassis,
            GameObject prefab)
        {
            var serialized = new SerializedObject(chassis);
            serialized.FindProperty("stableId").stringValue = "Chassis.Apex.V2Reference";
            serialized.FindProperty("displayName").stringValue = "Apex V3 — V2 Reference";
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("baseMassKg").floatValue = ChassisMassKg;
            serialized.FindProperty("baseCenterOfMass").vector3Value =
                new Vector3(0f, -0.59516f, 0.001779f);
            serialized.FindProperty("parityCenterOfMassCalibration").vector3Value =
                Vector3.zero;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chassis);
        }

        private static void CalibrateReferenceCenterOfMass(
            ChassisDefinition chassis,
            CraftBuildDefinition referenceBuild)
        {
            AssetDatabase.SaveAssets();
            var host = new GameObject("V3 COM Calibration Host");
            try
            {
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                var assemblerObject = new SerializedObject(assembler);
                assemblerObject.FindProperty("build").objectReferenceValue =
                    referenceBuild;
                assemblerObject.FindProperty("assembleOnStart").boolValue =
                    false;
                assemblerObject.ApplyModifiedPropertiesWithoutUndo();
                if (!assembler.Rebuild())
                {
                    throw new InvalidOperationException(
                        "Could not assemble the generated reference build for " +
                        "center-of-mass calibration.");
                }

                V3CraftMassCalculator calculator =
                    assembler.AssembledRoot.GetComponent<
                        V3CraftMassCalculator>();
                V3MassCalculationResult result = calculator != null
                    ? calculator.Result
                    : null;
                if (result == null)
                {
                    throw new InvalidOperationException(
                        "Generated reference build did not produce mass data.");
                }

                Vector3 desiredCenter = new Vector3(0f, -0.5f, 0f);
                Vector3 baseCenterCorrection =
                    (desiredCenter - result.FinalCenterOfMass) *
                    (result.TotalMassKg / Mathf.Max(0.001f, ChassisMassKg));
                var chassisObject = new SerializedObject(chassis);
                chassisObject.FindProperty("baseCenterOfMass").vector3Value =
                    chassis.BaseCenterOfMass + baseCenterCorrection;
                chassisObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(chassis);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void SetCatalog(PartCatalog catalog, params PartDefinition[] parts)
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("parts");
            list.arraySize = parts.Length;
            for (int i = 0; i < parts.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void ConfigureReferenceBuild(
            CraftBuildDefinition build,
            ChassisDefinition chassis,
            PartCatalog catalog,
            ThrusterDefinition main,
            ThrusterDefinition brake,
            ThrusterDefinition hover,
            ThrusterDefinition roof,
            ThrusterDefinition strafe,
            EnergyCoreDefinition core,
            CockpitDefinition cockpit)
        {
            var serialized = new SerializedObject(build);
            serialized.FindProperty("chassis").objectReferenceValue = chassis;
            serialized.FindProperty("catalog").objectReferenceValue = catalog;
            serialized.FindProperty("installations").ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            build.GetOrCreateInstallation("Propulsion.Rear.Center").SetDirect(main);
            build.GetOrCreateInstallation("Braking.Front.Center").SetDirect(brake);
            build.GetOrCreateInstallation("Hover.Front.Left.Bottom").SetDirect(hover);
            build.GetOrCreateInstallation("Hover.Front.Right.Bottom").SetDirect(hover);
            build.GetOrCreateInstallation("Hover.Rear.Left.Bottom").SetDirect(hover);
            build.GetOrCreateInstallation("Hover.Rear.Right.Bottom").SetDirect(hover);
            build.GetOrCreateInstallation("Control.Front.Left.Top").SetDirect(roof);
            build.GetOrCreateInstallation("Control.Front.Right.Top").SetDirect(roof);
            build.GetOrCreateInstallation("Control.Rear.Left.Top").SetDirect(roof);
            build.GetOrCreateInstallation("Control.Rear.Right.Top").SetDirect(roof);
            build.GetOrCreateInstallation("Strafe.Left.Front").SetDirect(strafe);
            build.GetOrCreateInstallation("Strafe.Right.Front").SetDirect(strafe);
            build.GetOrCreateInstallation("Strafe.Left.Rear").SetDirect(strafe);
            build.GetOrCreateInstallation("Strafe.Right.Rear").SetDirect(strafe);
            build.GetOrCreateInstallation("Core.Main").SetDirect(core);
            build.GetOrCreateInstallation("Cockpit.Main").SetDirect(cockpit);
            EditorUtility.SetDirty(build);
        }

        private static void ConfigureBuildIdentity(
            CraftBuildDefinition build,
            string displayName,
            string vehicleClass,
            string layoutName,
            string summary,
            string drivingNotes)
        {
            var serialized = new SerializedObject(build);
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("manufacturerName").stringValue =
                "Lunarlight";
            serialized.FindProperty("vehicleClass").stringValue = vehicleClass;
            serialized.FindProperty("layoutName").stringValue = layoutName;
            serialized.FindProperty("summary").stringValue = summary;
            serialized.FindProperty("drivingNotes").stringValue = drivingNotes;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(build);
        }

        private static void CreateAssemblerPrefab(
            string name,
            CraftBuildDefinition build)
        {
            var host = new GameObject(name);
            V3CraftAssembler assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.FindProperty("assembleOnStart").boolValue = true;
            serialized.FindProperty("installReferenceControllers").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(
                host,
                $"{PrototypeRoot}/Prefabs/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(host);
        }

        private static void ValidateGeneratedBuild(
            CraftBuildDefinition build,
            float expectedMassKg)
        {
            BuildValidationReport report = CraftBuildValidator.Validate(build);
            if (!report.IsValid)
            {
                var messages = new List<string>();
                for (int i = 0; i < report.Issues.Count; i++)
                {
                    BuildIssue issue = report.Issues[i];
                    if (issue.Severity == BuildIssueSeverity.Error)
                    {
                        messages.Add($"{issue.Code}: {issue.Message}");
                    }
                }

                throw new InvalidOperationException(
                    $"Generated build '{build.name}' is invalid:\n{string.Join("\n", messages)}");
            }

            float mass = build.Chassis.BaseMassKg;
            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation?.Connector != null)
                {
                    mass += installation.Connector.Physical.massKg;
                }

                if (installation?.Endpoint != null)
                {
                    mass += installation.Endpoint.Physical.massKg;
                }
            }

            if (Mathf.Abs(mass - expectedMassKg) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"Generated build '{build.name}' mass is {mass} kg; expected {expectedMassKg} kg.");
            }
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void SetFloat(UnityEngine.Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetNestedFloat(
            UnityEngine.Object target,
            string parentProperty,
            string property,
            float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(parentProperty)
                .FindPropertyRelative(property)
                .floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetNestedBool(
            UnityEngine.Object target,
            string parentProperty,
            string property,
            bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(parentProperty)
                .FindPropertyRelative(property)
                .boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetInt(UnityEngine.Object target, string property, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetBool(UnityEngine.Object target, string property, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetVector3(
            UnityEngine.Object target,
            string property,
            Vector3 value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetEnum<T>(
            UnityEngine.Object target,
            string property,
            T value) where T : Enum
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).enumValueIndex = Convert.ToInt32(value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetEnumArray<T>(
            UnityEngine.Object target,
            string property,
            params T[] values) where T : Enum
        {
            var serialized = new SerializedObject(target);
            SetEnumArray(serialized.FindProperty(property), values);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetEnumArray<T>(
            SerializedProperty property,
            IReadOnlyList<T> values) where T : Enum
        {
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).enumValueIndex = Convert.ToInt32(values[i]);
            }
        }
    }
}
