using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V3SystemsIntegrationPrototypeGenerator
    {
        public const string Root =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration";
        public const string BuildPath =
            Root + "/Builds/ApexV3_SystemsIntegration.asset";
        public const string BaselineBuildPath =
            Root + "/Builds/Hovercraft_Baseline.asset";
        public const string AssemblerPrefabPath =
            Root + "/Prefabs/ApexV3_SystemsIntegration_Assembler.prefab";
        public const string ChassisPrefabPath =
            Root + "/Prefabs/ApexV3_SystemsIntegration_Chassis.prefab";
        public const string ScenePath =
            "Assets/HovercraftV3/Scenes/Development/" +
            "V3_CraftLab.unity";

        private const string SourceChassisPrefab =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "V2Parity/Prefabs/" +
            "ApexV3_V2Reference_Chassis.prefab";
        private const string SourceReferenceBuild =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "V2Parity/Builds/" +
            "ApexV3_V2Reference.asset";

        private static readonly Vector3 SensorVisualScale =
            new Vector3(0.14f, 0.08f, 0.18f);
        private static readonly Vector3 FinActuatorVisualScale =
            new Vector3(0.18f, 0.12f, 0.12f);
        private static readonly Vector3 FinVisualScale =
            new Vector3(0.08f, 0.42f, 0.72f);

        [MenuItem(
            "Tools/Hovercraft V3/Generate Systems Integration Prototype")]
        public static void Generate()
        {
            EnsureFolders();

            V3DeviceFirmwareDefinition thrusterFirmware =
                CreateFirmware(
                    "Stock_Thruster_Firmware",
                    V3FirmwareDeviceKind.Thruster,
                    240f,
                    30f);
            V3DeviceFirmwareDefinition gimbalFirmware =
                CreateFirmware(
                    "Stock_Gimbal_Firmware",
                    V3FirmwareDeviceKind.Gimbal,
                    240f,
                    30f);
            V3DeviceFirmwareDefinition finFirmware =
                CreateFirmware(
                    "Stock_FinActuator_Firmware",
                    V3FirmwareDeviceKind.RotaryFinActuator,
                    120f,
                    30f);
            V3DeviceFirmwareDefinition coolingFirmware =
                CreateFirmware(
                    "Stock_Cooling_Firmware",
                    V3FirmwareDeviceKind.CoolingModule,
                    60f,
                    10f);
            V3DeviceFirmwareDefinition sensorFirmware =
                CreateFirmware(
                "Stock_DirectionalSensor_Firmware",
                V3FirmwareDeviceKind.DirectionalSensor,
                120f,
                15f);
            GameObject sensorPrefab = CreatePartPrefab(
                "V3_DirectionalSensor_Prototype",
                typeof(V3DirectionalSensorDeviceRuntime),
                SensorVisualScale);

            V3MainframeDefinition mainframe =
                CreateOrLoad<V3MainframeDefinition>(
                    Root + "/Definitions/Balanced_Mainframe.asset");
            Set(mainframe, "stableId", "mainframe.apex.balanced.01");
            Set(mainframe, "displayName", "Apex Balanced Mainframe");
            Set(mainframe, "maximumRegisteredDevices", 64);
            Set(mainframe, "maximumSoftwareTasks", 16);
            Set(mainframe, "computeCapacityPerSecond", 1400f);
            Set(mainframe, "observationBandwidthPerSecond", 2400f);
            Set(mainframe, "commandBandwidthPerSecond", 1200f);
            Set(mainframe, "idlePower", 35f);

            V3RouterProfileDefinition router =
                CreateOrLoad<V3RouterProfileDefinition>(
                    Root + "/Definitions/Balanced_Router_Profile.asset");
            Set(router, "stableId", "router.balanced.01");
            Set(router, "displayName", "Balanced");
            SetFloatArray(router, "domainWeights", 10, 1f);

            V3DirectionalSensorDefinition[] sensors =
                new V3DirectionalSensorDefinition[6];
            for (int i = 0; i < sensors.Length; i++)
            {
                V3SensorDirection direction = (V3SensorDirection)i;
                sensors[i] = CreateOrLoad<V3DirectionalSensorDefinition>(
                    $"{Root}/Definitions/Sensor_{direction}.asset");
                Set(
                    sensors[i],
                    "stableId",
                    $"sensor.chassis.{direction.ToString().ToLowerInvariant()}");
                Set(
                    sensors[i],
                    "displayName",
                    $"{direction} Directional Sensor");
                Set(
                    sensors[i],
                    "manufacturer",
                    "Lunarlight");
                Set(sensors[i], "direction", direction);
                Set(sensors[i], "rangeMeters", 40f);
                Set(
                    sensors[i],
                    "requestedSampleRateHz",
                    direction == V3SensorDirection.Front ||
                    direction == V3SensorDirection.Bottom
                        ? 120f
                        : 60f);
                Set(sensors[i], "minimumSampleRateHz", 15f);
                Set(sensors[i], "maximumSampleRateHz", 120f);
                Set(sensors[i], "idlePower", 2f);
                Set(sensors[i], "powerPerSample", 0.025f);
                Set(
                    sensors[i],
                    "castType",
                    V3SensorCastType.Raycast);
                Set(sensors[i], "sphereRadius", 0.08f);
                Set(sensors[i], "minimumValidDistance", 0.02f);
                Set(sensors[i], "firmware", sensorFirmware);
                Set(sensors[i], "prefab", sensorPrefab);
                Set(sensors[i], "massKg", 0.75f);
                Set(
                    sensors[i],
                    "localVisualScale",
                    SensorVisualScale);
            }

            GameObject chassisPrefab = CreateChassisPrefab();
            ChassisDefinition sourceChassis =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SourceReferenceBuild).Chassis;
            ChassisDefinition chassis = CloneAsset(
                sourceChassis,
                Root + "/Definitions/ApexV3_SystemsIntegration_Chassis.asset");
            Set(chassis, "stableId", "chassis.apex.v3.systems_integration.01");
            Set(chassis, "displayName", "Apex V3 Systems Chassis");
            Set(chassis, "prefab", chassisPrefab);
            Set(chassis, "baseMassKg", sourceChassis.BaseMassKg + 90f);
            Set(chassis, "integratedMainframe", mainframe);
            SetObjectArray(chassis, "integratedSensors", sensors);
            Set(chassis, "routerProfile", router);

            V3SystemComputerDefinition[] computers =
                CreateSystemComputers();
            RuntimeAssets runtimeAssets = CreateRuntimeHardware(
                thrusterFirmware,
                gimbalFirmware,
                finFirmware,
                coolingFirmware);
            PartCatalog catalog = CreateCatalog(
                computers,
                runtimeAssets.AllParts);
            CraftBuildDefinition build = CreateBuild(
                chassis,
                catalog,
                computers,
                runtimeAssets);
            CraftBuildDefinition baselineBuild = CreateBaselineBuild(
                build,
                catalog,
                runtimeAssets);
            CreateAssemblerPrefab(build);
            CreateSystemsTestScene(build);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ValidateGeneratedBuild(
                build,
                "Systems integration build");
            ValidateGeneratedBuild(
                baselineBuild,
                "Hovercraft baseline build");

            Debug.Log(
                "Generated Apex V3 Systems Integration Prototype and " +
                "Hovercraft Baseline at " + Root);
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
        }

        private sealed class RuntimeAssets
        {
            public ThrusterDefinition Main;
            public ThrusterDefinition Brake;
            public ThrusterDefinition Hover;
            public ThrusterDefinition Roof;
            public ThrusterDefinition Strafe;
            public FixedAdapterDefinition Fixed;
            public GimbalDefinition Gimbal;
            public SpringMountDefinition Spring;
            public EnergyCoreDefinition Core;
            public CockpitDefinition Cockpit;
            public V3RotaryActuatorDefinition FinActuator;
            public V3AerodynamicFinDefinition Fin;
            public V3CoolingModuleDefinition Cooling;
            public PartDefinition[] AllParts;
        }

        private static RuntimeAssets CreateRuntimeHardware(
            V3DeviceFirmwareDefinition thrusterFirmware,
            V3DeviceFirmwareDefinition gimbalFirmware,
            V3DeviceFirmwareDefinition finFirmware,
            V3DeviceFirmwareDefinition coolingFirmware)
        {
            string source =
                "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                "V2Parity/Definitions/";
            var assets = new RuntimeAssets
            {
                Main = CloneAsset(
                    Load<ThrusterDefinition>(
                        source + "V2Reference_MainThruster.asset"),
                    Root + "/Definitions/Balanced_MainThruster.asset"),
                Brake = CloneAsset(
                    Load<ThrusterDefinition>(
                        source + "V2Reference_BrakeThruster.asset"),
                    Root + "/Definitions/Balanced_BrakeThruster.asset"),
                Hover = CloneAsset(
                    Load<ThrusterDefinition>(
                        source + "V2Reference_HoverThruster.asset"),
                    Root + "/Definitions/Balanced_HoverThruster.asset"),
                Roof = CloneAsset(
                    Load<ThrusterDefinition>(
                        source + "V2Reference_RoofThruster.asset"),
                    Root + "/Definitions/Balanced_RoofThruster.asset"),
                Strafe = CloneAsset(
                    Load<ThrusterDefinition>(
                        source + "V2Reference_StrafeThruster.asset"),
                    Root + "/Definitions/Balanced_StrafeThruster.asset"),
                Fixed = CloneAsset(
                    Load<FixedAdapterDefinition>(
                        source + "V2Reference_FixedAdapter.asset"),
                    Root +
                    "/Definitions/Baseline_RearFixedAdapter.asset"),
                Gimbal = CloneAsset(
                    Load<GimbalDefinition>(
                        source + "V2Reference_MainGimbal.asset"),
                    Root + "/Definitions/Balanced_MainGimbal.asset"),
                Spring = CloneAsset(
                    Load<SpringMountDefinition>(
                        source + "V2Reference_HoverSpringMount.asset"),
                    Root + "/Definitions/Balanced_Suspension.asset"),
                Core = CloneAsset(
                    Load<EnergyCoreDefinition>(
                        source + "V2Reference_EnergyCore.asset"),
                    Root + "/Definitions/Balanced_EnergyCore.asset"),
                Cockpit = CloneAsset(
                    Load<CockpitDefinition>(
                        source + "V2Reference_Cockpit.asset"),
                    Root + "/Definitions/Balanced_SystemsCockpit.asset")
            };
            ThrusterDefinition[] thrusters =
            {
                assets.Main,
                assets.Brake,
                assets.Hover,
                assets.Roof,
                assets.Strafe
            };
            for (int i = 0; i < thrusters.Length; i++)
            {
                Set(thrusters[i], "firmware", thrusterFirmware);
            }

            Set(assets.Main, "stableId", "thruster.main.balanced.01");
            Set(assets.Brake, "stableId", "thruster.brake.balanced.01");
            Set(assets.Hover, "stableId", "thruster.hover.balanced.01");
            Set(assets.Roof, "stableId", "thruster.roof.balanced.01");
            Set(assets.Strafe, "stableId", "thruster.strafe.balanced.01");
            Set(assets.Gimbal, "stableId", "connector.gimbal.main.balanced.01");
            Set(assets.Spring, "stableId", "connector.spring.hover.balanced.01");
            Set(assets.Core, "stableId", "energycore.main.balanced.01");
            Set(assets.Cockpit, "stableId", "cockpit.main.balanced.01");

            Set(assets.Gimbal, "firmware", gimbalFirmware);
            Set(
                assets.Fixed,
                "stableId",
                "connector.propulsion.fixed.baseline.01");
            Set(
                assets.Fixed,
                "displayName",
                "Baseline Rear Fixed Adapter");
            Set(assets.Fixed, "productFamily", "Hovercraft V3 Baseline");
            Set(
                assets.Fixed,
                "description",
                "A rigid, non-vectoring rear propulsion mount used by " +
                "the Hovercraft V3 baseline craft.");
            Set(assets.Core, "continuousOutput", 5200f);
            Set(assets.Core, "propulsionChannelCeiling", 4500f);
            Set(assets.Core, "systemsChannelCeiling", 1300f);
            Set(assets.Cockpit, "equipmentSlots", 7);
            Set(assets.Cockpit, "displaySlots", 4);
            Set(assets.Cockpit, "supportsFutureHud", true);
            Set(assets.Cockpit, "systemsPowerThroughput", 1300f);
            Set(assets.Cockpit, "dataThroughput", 2400f);
            SetEnumArray(
                assets.Cockpit,
                "supportedComputerRoles",
                (V3SystemComputerRole[])Enum.GetValues(
                    typeof(V3SystemComputerRole)));

            GameObject actuatorPrefab = CreateConnectorPrefab(
                "Balanced_FinActuator",
                typeof(RuntimeRotaryActuatorInstance),
                FinActuatorVisualScale);
            assets.FinActuator =
                CreateOrLoad<V3RotaryActuatorDefinition>(
                    Root + "/Definitions/Balanced_FinActuator.asset");
            ConfigurePart(
                assets.FinActuator,
                "connector.aero.rotary.balanced.01",
                "Balanced Rotary Fin Actuator",
                PartRole.Aerodynamics,
                18f,
                actuatorPrefab,
                PowerDomain.None,
                2f,
                30f,
                SocketFamily.Aero);
            Set(assets.FinActuator, "kind", ConnectorKind.RotaryActuator);
            Set(assets.FinActuator, "childSocketFamily", SocketFamily.Aero);
            Set(assets.FinActuator, "childSize", PartSize.Small);
            SetEnumArray(
                assets.FinActuator,
                "allowedEndpointCategories",
                new[] { EndpointCategory.Fin });
            Set(assets.FinActuator, "supportedEndpointMassKg", 100f);
            Set(assets.FinActuator, "supportedEndpointForceN", 100000f);
            Set(assets.FinActuator, "childPowerAvailability", 100f);
            Set(assets.FinActuator, "childHasPower", true);
            Set(assets.FinActuator, "childHasData", true);
            Set(assets.FinActuator, "localAxis", Vector3.right);
            Set(assets.FinActuator, "minimumAngleDegrees", -25f);
            Set(assets.FinActuator, "maximumAngleDegrees", 25f);
            Set(assets.FinActuator, "speedDegreesPerSecond", 120f);
            Set(
                assets.FinActuator,
                "accelerationDegreesPerSecondSquared",
                480f);
            Set(assets.FinActuator, "maximumLoadNm", 10000f);
            Set(assets.FinActuator, "firmware", finFirmware);

            GameObject finPrefab = CreatePartPrefab(
                "Balanced_AeroFin",
                typeof(RuntimeAerodynamicFinInstance),
                FinVisualScale);
            assets.Fin = CreateOrLoad<V3AerodynamicFinDefinition>(
                Root + "/Definitions/Balanced_AeroFin.asset");
            ConfigurePart(
                assets.Fin,
                "fin.aero.balanced.01",
                "Balanced Active Fin",
                PartRole.Aerodynamics,
                12f,
                finPrefab,
                PowerDomain.None,
                0f,
                0f,
                SocketFamily.Aero);
            Set(assets.Fin, "areaSquareMeters", 0.8f);
            Set(assets.Fin, "zeroLiftAngleDegrees", 0f);
            Set(assets.Fin, "aerodynamicModelVersion", 2);
            Set(assets.Fin, "liftSlopePerRadian", 4.5f);
            Set(assets.Fin, "maximumLiftCoefficient", 0.9f);
            Set(assets.Fin, "positiveStallAngleDegrees", 18f);
            Set(assets.Fin, "negativeStallAngleDegrees", -18f);
            Set(assets.Fin, "postStallLiftCoefficient", 0.2f);
            Set(assets.Fin, "reverseFlowScale", 0.2f);
            Set(assets.Fin, "baseDragCoefficient", 0.04f);
            Set(assets.Fin, "inducedDragFactor", 0.12f);
            Set(assets.Fin, "stalledDragCoefficient", 0.8f);
            Set(assets.Fin, "maximumAerodynamicForceN", 0f);
            Set(assets.Fin, "maximumAerodynamicMomentNm", 0f);
            Set(assets.Fin, "structuralWarningFraction", 0.85f);
            Set(assets.Fin, "localLiftAxis", Vector3.down);
            Set(assets.Fin, "localChordAxis", Vector3.forward);

            GameObject coolingPrefab = CreatePartPrefab(
                "Balanced_ActiveCooling",
                typeof(RuntimeCoolingModuleInstance),
                new Vector3(0.8f, 0.35f, 0.8f));
            assets.Cooling = CreateOrLoad<V3CoolingModuleDefinition>(
                Root + "/Definitions/Balanced_ActiveCooling.asset");
            ConfigurePart(
                assets.Cooling,
                "cooling.active.balanced.01",
                "Balanced Active Cooling",
                PartRole.Cooling,
                55f,
                coolingPrefab,
                PowerDomain.None,
                5f,
                90f,
                SocketFamily.CoolingBay);
            Set(assets.Cooling, "maximumHeatRemovalPerSecond", 300f);
            Set(assets.Cooling, "activationTemperatureC", 55f);
            Set(assets.Cooling, "firmware", coolingFirmware);

            assets.AllParts = new PartDefinition[]
            {
                assets.Main, assets.Brake, assets.Hover, assets.Roof,
                assets.Strafe, assets.Fixed, assets.Gimbal, assets.Spring,
                assets.Core,
                assets.Cockpit, assets.FinActuator, assets.Fin,
                assets.Cooling
            };
            return assets;
        }

        private static V3SystemComputerDefinition[] CreateSystemComputers()
        {
            var result = new V3SystemComputerDefinition[7];
            foreach (V3SystemComputerRole role in
                Enum.GetValues(typeof(V3SystemComputerRole)))
            {
                float requested =
                    role == V3SystemComputerRole.PilotInterface ||
                    role == V3SystemComputerRole.Hover ||
                    role == V3SystemComputerRole.Stabilizer ||
                    role == V3SystemComputerRole.Traction
                        ? 120f
                        : role == V3SystemComputerRole.Telemetry
                            ? 30f
                            : 60f;
                float minimum =
                    requested >= 120f ? 60f :
                    requested >= 60f ? 30f : 10f;
                V3SystemSoftwareDefinition software =
                    CreateOrLoad<V3SystemSoftwareDefinition>(
                        $"{Root}/Definitions/Software_{role}.asset");
                Set(
                    software,
                    "stableId",
                    $"software.{role.ToString().ToLowerInvariant()}.stock");
                Set(software, "role", role);
                Set(software, "requestedTickRateHz", requested);
                Set(software, "minimumUsefulRateHz", minimum);
                Set(software, "maximumUsefulRateHz", requested);
                Set(software, "computeCostPerTick", 1f);
                Set(software, "idlePower", 2f);
                Set(software, "powerPerTick", 0.025f);
                Set(
                    software,
                    "priority",
                    V3SoftwareScheduler.GetSurvivalRank(role));

                GameObject prefab = CreatePartPrefab(
                    $"Computer_{role}",
                    typeof(RuntimePartInstance),
                    new Vector3(0.32f, 0.18f, 0.42f));
                V3SystemComputerDefinition computer =
                    CreateOrLoad<V3SystemComputerDefinition>(
                        $"{Root}/Definitions/Computer_{role}.asset");
                ConfigurePart(
                    computer,
                    $"computer.{role.ToString().ToLowerInvariant()}.balanced.01",
                    $"Balanced {role} Computer",
                    PartRole.InternalSystem,
                    role == V3SystemComputerRole.Telemetry ? 18f : 22f,
                    prefab,
                    PowerDomain.None,
                    0f,
                    0f,
                    SocketFamily.InternalEquipment);
                Set(computer, "computerRole", role);
                Set(computer, "computeCapacityPerSecond", 300f);
                Set(computer, "taskCapacity", 1f);
                Set(computer, "bundledSoftware", software);
                Set(
                    computer,
                    "providedCapabilities",
                    (int)GetComputerCapabilities(role));
                result[(int)role] = computer;
            }

            return result;
        }

        private static PartCapability GetComputerCapabilities(
            V3SystemComputerRole role)
        {
            switch (role)
            {
                case V3SystemComputerRole.PilotInterface:
                    return PartCapability.PilotInterface;
                case V3SystemComputerRole.Drive:
                    return PartCapability.DriveControl;
                case V3SystemComputerRole.Hover:
                    return PartCapability.HoverControl;
                case V3SystemComputerRole.Stabilizer:
                    return PartCapability.Stabilization |
                           PartCapability.VectoringControl;
                case V3SystemComputerRole.Traction:
                    return PartCapability.TractionControl;
                case V3SystemComputerRole.Aerodynamics:
                    return PartCapability.AerodynamicsControl;
                case V3SystemComputerRole.Telemetry:
                    return PartCapability.Telemetry |
                           PartCapability.ThermalTelemetry;
                default:
                    return PartCapability.None;
            }
        }

        private static CraftBuildDefinition CreateBuild(
            ChassisDefinition chassis,
            PartCatalog catalog,
            V3SystemComputerDefinition[] computers,
            RuntimeAssets assets)
        {
            CraftBuildDefinition reference =
                Load<CraftBuildDefinition>(SourceReferenceBuild);
            CraftBuildDefinition build =
                CreateOrLoad<CraftBuildDefinition>(BuildPath);
            build.CopySelectionsFrom(reference);
            Set(build, "stableId",
                "build.apex.v3.systems_integration.balanced.01");
            Set(build, "ownership", V3BuildOwnership.GeneratedReference);
            Set(build, "allowLegacyPipelineFallback", false);
            Set(build, "buildAssetGuid", AssetDatabase.AssetPathToGUID(BuildPath));
            Set(build, "displayName",
                "Apex V3 Systems Integration Prototype");
            Set(build, "manufacturerName", "Lunarlight");
            Set(build, "vehicleClass", "Balanced Systems Prototype");
            Set(build, "layoutName",
                "Rear Gimbal / Four-Corner Suspension / Active Aero");
            Set(build, "summary",
                "A complete Mainframe-driven craft with seven physical " +
                "system computers, six sensors, firmware, passive suspension, " +
                "active aerodynamics, and finite active cooling.");
            Set(build, "drivingNotes",
                "F4 cycles systems debug pages. Power starvation degrades " +
                "software and sensor rates before hardware limits are bypassed.");
            Set(build, "chassis", chassis);
            Set(build, "catalog", catalog);
            V3HoverConfigurationAssetUtility.Configure(
                build, "hover.apex.systems.01", 8f, 1f, 80f);

            Replace(build, "Propulsion.Rear.Center", assets.Gimbal, assets.Main);
            Replace(build, "Braking.Front.Center", null, assets.Brake);
            string[] hoverSockets =
            {
                "Hover.Front.Left.Bottom",
                "Hover.Front.Right.Bottom",
                "Hover.Rear.Left.Bottom",
                "Hover.Rear.Right.Bottom"
            };
            for (int i = 0; i < hoverSockets.Length; i++)
            {
                Replace(build, hoverSockets[i], assets.Spring, assets.Hover);
            }

            ReplaceExistingThrusters(build, assets);
            Replace(build, "Core.Main", null, assets.Core);
            Replace(build, "Cockpit.Main", null, assets.Cockpit);
            foreach (V3SystemComputerRole role in
                Enum.GetValues(typeof(V3SystemComputerRole)))
            {
                Replace(
                    build,
                    "System." + role,
                    null,
                    computers[(int)role]);
            }

            string[] aero =
            {
                "Aero.Front.Left",
                "Aero.Front.Right",
                "Aero.Rear.Left",
                "Aero.Rear.Right"
            };
            for (int i = 0; i < aero.Length; i++)
            {
                Replace(
                    build,
                    aero[i],
                    assets.FinActuator,
                    assets.Fin);
            }

            Replace(build, "Cooling.Main", null, assets.Cooling);
            EditorUtility.SetDirty(build);
            return build;
        }

        private static CraftBuildDefinition CreateBaselineBuild(
            CraftBuildDefinition systemsBuild,
            PartCatalog catalog,
            RuntimeAssets assets)
        {
            CraftBuildDefinition build =
                CreateOrLoad<CraftBuildDefinition>(BaselineBuildPath);
            build.CopySelectionsFrom(systemsBuild);
            Set(build, "stableId", "build.hovercraft.baseline.01");
            Set(build, "ownership", V3BuildOwnership.GeneratedReference);
            Set(build, "allowLegacyPipelineFallback", false);
            Set(build, "buildAssetGuid", AssetDatabase.AssetPathToGUID(BaselineBuildPath));
            Set(build, "displayName", "Hovercraft Baseline");
            Set(build, "manufacturerName", "Lunarlight");
            Set(build, "vehicleClass", "Baseline Systems Craft");
            Set(build, "layoutName",
                "Fixed Rear Propulsion / Four-Corner Suspension / " +
                "Active Aero");
            Set(build, "summary",
                "The authoritative Hovercraft V3 tuning baseline. It " +
                "retains the complete physical systems craft while mounting " +
                "the rear main thruster on a rigid fixed adapter with no " +
                "gimbal or thrust-vectoring actuator.");
            Set(build, "drivingNotes",
                "Use this build for baseline handling and power-mode test " +
                "runs. Rear propulsion is fixed; attitude, surface tracking, " +
                "and recovery remain the responsibility of the installed " +
                "physical hover, roof, strafe, aero, and braking hardware.");
            Set(build, "catalog", catalog);
            V3HoverConfigurationAssetUtility.Configure(
                build, "hover.hovercraft.baseline.01", 8f, 1f, 80f);
            Replace(
                build,
                "Propulsion.Rear.Center",
                assets.Fixed,
                assets.Main);
            EditorUtility.SetDirty(build);
            return build;
        }

        private static void ValidateGeneratedBuild(
            CraftBuildDefinition build,
            string label)
        {
            BuildValidationReport report =
                CraftBuildValidator.Validate(build);
            if (report.IsValid)
            {
                return;
            }

            var errors = new List<string>();
            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity ==
                    BuildIssueSeverity.Error)
                {
                    errors.Add(
                        report.Issues[i].Code + ": " +
                        report.Issues[i].Message);
                }
            }

            throw new InvalidOperationException(
                label + " failed validation:\n" +
                string.Join("\n", errors));
        }

        private static void ReplaceExistingThrusters(
            CraftBuildDefinition build,
            RuntimeAssets assets)
        {
            string[] roof =
            {
                "Control.Front.Left.Top", "Control.Front.Right.Top",
                "Control.Rear.Left.Top", "Control.Rear.Right.Top"
            };
            string[] strafe =
            {
                "Strafe.Left.Front", "Strafe.Right.Front",
                "Strafe.Left.Rear", "Strafe.Right.Rear"
            };
            for (int i = 0; i < roof.Length; i++)
            {
                Replace(build, roof[i], null, assets.Roof);
                Replace(build, strafe[i], null, assets.Strafe);
            }
        }

        private static void Replace(
            CraftBuildDefinition build,
            string socket,
            ConnectorDefinition connector,
            EndpointDefinition endpoint)
        {
            SocketInstallation installation =
                build.GetOrCreateInstallation(socket);
            if (connector != null)
            {
                installation.SetConnector(connector, endpoint);
            }
            else
            {
                installation.SetDirect(endpoint);
            }
        }

        private static GameObject CreateChassisPrefab()
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(SourceChassisPrefab);
            try
            {
                root.name = "ApexV3_SystemsIntegration_Chassis";
                var systems = new GameObject("SystemsIntegrationHardpoints");
                systems.transform.SetParent(root.transform, false);
                Vector3[] aeroPositions =
                {
                    new Vector3(-1.8f, 0.35f, 2.9f),
                    new Vector3(1.8f, 0.35f, 2.9f),
                    new Vector3(-1.8f, 0.35f, -2.9f),
                    new Vector3(1.8f, 0.35f, -2.9f)
                };
                string[] aeroIds =
                {
                    "Aero.Front.Left", "Aero.Front.Right",
                    "Aero.Rear.Left", "Aero.Rear.Right"
                };
                for (int i = 0; i < aeroIds.Length; i++)
                {
                    AddSocket(
                        systems.transform,
                        aeroIds[i],
                        SocketFamily.Aero,
                        aeroPositions[i],
                        new[] { EndpointCategory.Fin },
                        new[] { ConnectorKind.RotaryActuator });
                }

                foreach (V3SystemComputerRole role in
                    Enum.GetValues(typeof(V3SystemComputerRole)))
                {
                    int index = (int)role;
                    AddSocket(
                        systems.transform,
                        "System." + role,
                        SocketFamily.InternalEquipment,
                        new Vector3(
                            -0.9f + (index % 4) * 0.6f,
                            0.9f,
                            0.6f - (index / 4) * 0.6f),
                        new[] { EndpointCategory.InternalSystem },
                        Array.Empty<ConnectorKind>());
                }

                AddSocket(
                    systems.transform,
                    "Cooling.Main",
                    SocketFamily.CoolingBay,
                    new Vector3(0f, 0.5f, -1.4f),
                    new[] { EndpointCategory.CoolingModule },
                    Array.Empty<ConnectorKind>());

                Vector3[] sensorPositions =
                {
                    new Vector3(0f, 0f, 3.9f),
                    new Vector3(0f, 0f, -3.9f),
                    new Vector3(-1.8f, 0f, 0f),
                    new Vector3(1.8f, 0f, 0f),
                    new Vector3(0f, 0.775f, 0f),
                    new Vector3(0f, -0.775f, 0f)
                };
                for (int i = 0; i < 6; i++)
                {
                    var sensor = new GameObject(
                        "Sensor." + (V3SensorDirection)i);
                    sensor.transform.SetParent(systems.transform, false);
                    sensor.transform.localPosition = sensorPositions[i];
                }

                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ChassisPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddSocket(
            Transform parent,
            string id,
            SocketFamily family,
            Vector3 position,
            EndpointCategory[] endpoints,
            ConnectorKind[] connectors)
        {
            var host = new GameObject(id);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;
            V3Socket socket = host.AddComponent<V3Socket>();
            Set(socket, "socketId", id);
            Set(socket, "debugName", id);
            Set(socket, "family", family);
            Set(socket, "size", PartSize.Small);
            Set(socket, "mountTransform", host.transform);
            Set(socket, "maximumSupportedMassKg", 500f);
            Set(socket, "maximumSupportedForceN", 200000f);
            Set(socket, "powerAvailability", 500f);
            Set(socket, "hasPower", true);
            Set(socket, "hasData", true);
            SetEnumArray(socket, "allowedDirectEndpoints", endpoints);
            SetEnumArray(socket, "allowedConnectors", connectors);
        }

        private static PartCatalog CreateCatalog(
            V3SystemComputerDefinition[] computers,
            PartDefinition[] hardware)
        {
            PartCatalog catalog = CreateOrLoad<PartCatalog>(
                Root + "/Definitions/SystemsIntegration_PartCatalog.asset");
            var parts = new List<PartDefinition>(hardware);
            parts.AddRange(computers);
            SetObjectArray(catalog, "parts", parts.ToArray());
            return catalog;
        }

        private static void CreateAssemblerPrefab(
            CraftBuildDefinition build)
        {
            var host = new GameObject(
                "ApexV3_SystemsIntegration_Assembler");
            try
            {
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                Set(assembler, "build", build);
                Set(assembler, "assembleOnStart", true);
                Set(assembler, "installReferenceControllers", true);
                PrefabUtility.SaveAsPrefabAsset(
                    host,
                    AssemblerPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void CreateSystemsTestScene(
            CraftBuildDefinition build)
        {
            const string sourceScene =
                "Assets/HovercraftV3/Scenes/Development/" +
                "V3_HandlingTrack.unity";
            Scene scene = EditorSceneManager.OpenScene(
                sourceScene,
                OpenSceneMode.Single);
            V3CraftAssembler assembler =
                UnityEngine.Object.FindAnyObjectByType<V3CraftAssembler>();
            if (assembler == null)
            {
                throw new InvalidOperationException(
                    "The V3 free-drive source scene has no craft assembler.");
            }

            Set(assembler, "build", build);
            Set(assembler, "assembleOnStart", false);
            V3CraftTestSpawner spawner =
                UnityEngine.Object.FindAnyObjectByType<V3CraftTestSpawner>();
            if (spawner == null)
            {
                throw new InvalidOperationException(
                    "The V3 systems source scene has no craft spawner.");
            }

            Set(spawner, "selectedBuild", build);
            assembler.gameObject.name =
                "Apex V3 Systems Integration Assembler";
            V3DiagnosticSceneInstaller.EnsureRecorderInScene();
            // The scene is cloned from V3_HandlingTrack before being saved as
            // V3_CraftLab, so scene.name still reflects the source here.
            // Supply the destination identity explicitly to avoid duplicate
            // serialized world-root stable IDs after regeneration.
            V3WorldSimulationInstaller.InstallIntoScene(
                scene,
                null,
                "world.root.v3_craftlab");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static GameObject CreatePartPrefab(
            string name,
            Type runtimeType,
            Vector3 scale)
        {
            GameObject host = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            host.name = name;
            host.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(
                host.GetComponent<Collider>());
            host.AddComponent(runtimeType);
            if (runtimeType == typeof(RuntimePartInstance) &&
                name.StartsWith(
                    "Computer_",
                    StringComparison.Ordinal))
            {
                host.AddComponent<V3SystemComputerRuntime>();
                string roleName = name.Substring("Computer_".Length);
                if (Enum.TryParse(
                    roleName,
                    out V3SystemComputerRole role))
                {
                    AddSystemRuntime(host, role);
                }
            }

            string path = $"{Root}/Prefabs/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(host, path);
            UnityEngine.Object.DestroyImmediate(host);
            return prefab;
        }

        private static void AddSystemRuntime(
            GameObject host,
            V3SystemComputerRole role)
        {
            switch (role)
            {
                case V3SystemComputerRole.PilotInterface:
                    host.AddComponent<
                        V3PilotInterfaceSystemRuntime>();
                    break;
                case V3SystemComputerRole.Drive:
                    host.AddComponent<V3DriveSystemRuntime>();
                    break;
                case V3SystemComputerRole.Hover:
                    host.AddComponent<V3HoverSystemRuntime>();
                    break;
                case V3SystemComputerRole.Stabilizer:
                    host.AddComponent<V3StabilizerSystemRuntime>();
                    break;
                case V3SystemComputerRole.Traction:
                    host.AddComponent<V3TractionSystemRuntime>();
                    break;
                case V3SystemComputerRole.Aerodynamics:
                    host.AddComponent<
                        V3AerodynamicsSystemRuntime>();
                    break;
                case V3SystemComputerRole.Telemetry:
                    host.AddComponent<V3TelemetrySystemRuntime>();
                    break;
            }
        }

        private static GameObject CreateConnectorPrefab(
            string name,
            Type runtimeType,
            Vector3 scale)
        {
            GameObject host = CreatePrimitiveWithoutCollider(name, scale);
            host.AddComponent(runtimeType);
            var mount = new GameObject("Actuated Child Mount");
            mount.transform.SetParent(host.transform, false);
            mount.AddComponent<ConnectorChildMount>();
            string path = $"{Root}/Prefabs/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(host, path);
            UnityEngine.Object.DestroyImmediate(host);
            return prefab;
        }

        private static GameObject CreatePrimitiveWithoutCollider(
            string name,
            Vector3 scale)
        {
            GameObject value = GameObject.CreatePrimitive(PrimitiveType.Cube);
            value.name = name;
            value.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(value.GetComponent<Collider>());
            return value;
        }

        private static V3DeviceFirmwareDefinition CreateFirmware(
            string name,
            V3FirmwareDeviceKind kind,
            float requestedRate,
            float minimumRate)
        {
            V3DeviceFirmwareDefinition value =
                CreateOrLoad<V3DeviceFirmwareDefinition>(
                    $"{Root}/Definitions/{name}.asset");
            Set(
                value,
                "stableId",
                "firmware." + name.ToLowerInvariant());
            Set(value, "deviceKind", kind);
            Set(value, "requestedCommandRateHz", requestedRate);
            Set(value, "minimumCommandRateHz", minimumRate);
            Set(value, "idlePower", 0.25f);
            Set(value, "powerPerCommand", 0.002f);
            Set(value, "commandTimeoutSeconds", 0.25f);
            return value;
        }

        private static void ConfigurePart(
            PartDefinition part,
            string stableId,
            string displayName,
            PartRole role,
            float mass,
            GameObject prefab,
            PowerDomain powerDomain,
            float idlePower,
            float maximumPower,
            SocketFamily family)
        {
            Set(part, "stableId", stableId);
            Set(part, "displayName", displayName);
            Set(part, "productFamily", "Apex Balanced Systems");
            Set(part, "description",
                "Generated systems-integration prototype hardware.");
            Set(part, "role", role);
            Set(part, "size", PartSize.Small);
            Set(part, "prefab", prefab);
            Set(part, "prototype", true);
            SetNested(part, "physical", "massKg", mass);
            SetNested(part, "physical", "maximumStructuralLoadN", 200000f);
            SetNested(part, "physical", "maximumSupportedForceN", 200000f);
            SetNested(part, "power", "domain", powerDomain);
            SetNested(part, "power", "idleDemand", idlePower);
            SetNested(part, "power", "maximumDemand", maximumPower);
            SetNested(part, "power", "efficiency", 1f);
            SetNested(part, "power", "overloadDemandMultiplier", 1f);
            SetEnumArray(
                part,
                "compatibleParentFamilies",
                new[] { family });
            Set(part, "requiresPower", maximumPower > 0f);
            Set(part, "requiresData", true);
        }

        private static T CloneAsset<T>(T source, string path)
            where T : ScriptableObject
        {
            T target = AssetDatabase.LoadAssetAtPath<T>(path);
            if (target == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
                target = UnityEngine.Object.Instantiate(source);
                target.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(target, path);
            }
            else
            {
                EditorUtility.CopySerialized(source, target);
            }

            EditorUtility.SetDirty(target);
            return target;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null)
            {
                throw new InvalidOperationException(
                    "Required source asset missing: " + path);
            }

            return value;
        }

        private static T CreateOrLoad<T>(string path)
            where T : ScriptableObject
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
                value = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(value, path);
            }

            return value;
        }

        private static void Set(
            UnityEngine.Object target,
            string property,
            object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field = serialized.FindProperty(property);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name}.{property} is not serialized.");
            }

            if (value is string text) field.stringValue = text;
            else if (value is bool boolean) field.boolValue = boolean;
            else if (value is int integer) field.intValue = integer;
            else if (value is float number) field.floatValue = number;
            else if (value is Vector3 vector) field.vector3Value = vector;
            else if (value is Enum enumeration)
                field.enumValueIndex = Convert.ToInt32(enumeration);
            else if (value is UnityEngine.Object reference)
                field.objectReferenceValue = reference;
            else if (value == null) field.objectReferenceValue = null;
            else throw new InvalidOperationException(
                "Unsupported serialized value: " + value.GetType().Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetNested(
            UnityEngine.Object target,
            string parent,
            string child,
            object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field =
                serialized.FindProperty(parent).FindPropertyRelative(child);
            if (value is float number) field.floatValue = number;
            else if (value is Enum enumeration)
                field.enumValueIndex = Convert.ToInt32(enumeration);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetObjectArray(
            UnityEngine.Object target,
            string property,
            UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(property);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue =
                    values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetEnumArray<T>(
            UnityEngine.Object target,
            string property,
            T[] values) where T : Enum
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(property);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).enumValueIndex =
                    Convert.ToInt32(values[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetFloatArray(
            UnityEngine.Object target,
            string property,
            int count,
            float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(property);
            array.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                array.GetArrayElementAtIndex(i).floatValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void EnsureFolders()
        {
            Ensure("Assets/HovercraftV3", "Generated");
            Ensure(
                "Assets/HovercraftV3/Generated",
                "ReferenceCrafts");
            Ensure(
                "Assets/HovercraftV3/Generated/ReferenceCrafts",
                "SystemsIntegration");
            Ensure(Root, "Definitions");
            Ensure(Root, "Builds");
            Ensure(Root, "Prefabs");
            Ensure("Assets/HovercraftV3", "Scenes");
            Ensure("Assets/HovercraftV3/Scenes", "Development");
        }

        private static void Ensure(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
