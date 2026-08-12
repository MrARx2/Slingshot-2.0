using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3SystemsIntegrationTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GeneratedBuild_IsValidAndHasStableIdentity()
        {
            CraftBuildDefinition build = LoadBuild();
            BuildValidationReport report =
                CraftBuildValidator.Validate(build);

            Assert.That(report.IsValid, Is.True);
            Assert.That(
                build.StableId,
                Is.EqualTo(
                    "build.apex.v3.systems_integration.balanced.01"));
            Assert.That(
                build.Chassis.IntegratedMainframe,
                Is.Not.Null);
            Assert.That(build.Chassis.IntegratedSensors.Count, Is.EqualTo(6));
        }

        [Test]
        public void BaselineBuild_IsValidAndUsesFixedRearPropulsion()
        {
            CraftBuildDefinition build = LoadBaselineBuild();
            BuildValidationReport report =
                CraftBuildValidator.Validate(build);
            SocketInstallation propulsion = build.FindInstallation(
                "Propulsion.Rear.Center");

            Assert.That(report.IsValid, Is.True);
            Assert.That(
                build.StableId,
                Is.EqualTo("build.hovercraft.baseline.01"));
            Assert.That(build.DisplayName, Is.EqualTo("Hovercraft Baseline"));
            Assert.That(
                build.HoverConfiguration.TargetHoverHeight,
                Is.EqualTo(8f));
            Assert.That(propulsion, Is.Not.Null);
            Assert.That(propulsion.Connector, Is.TypeOf<FixedAdapterDefinition>());
            Assert.That(propulsion.Connector, Is.Not.TypeOf<GimbalDefinition>());
            Assert.That(propulsion.Endpoint, Is.TypeOf<ThrusterDefinition>());
        }

        [Test]
        public void BaselineCraft_AssemblesWithoutGimbalRuntime()
        {
            V3CraftAssembler assembler = Assemble(LoadBaselineBuild());
            V3CraftRuntime runtime =
                assembler.AssembledRoot.GetComponent<V3CraftRuntime>();

            Assert.That(runtime, Is.Not.Null);
            Assert.That(
                runtime.InstalledParts.OfType<RuntimeGimbalInstance>(),
                Is.Empty);
            Assert.That(
                runtime.InstalledParts.Count(
                    part => part.Definition is FixedAdapterDefinition),
                Is.EqualTo(1));
        }

        [Test]
        public void CompleteCraft_AssemblesAndBootsReady()
        {
            V3CraftAssembler assembler = Assemble();
            GameObject root = assembler.AssembledRoot;
            V3CraftRuntime runtime = root.GetComponent<V3CraftRuntime>();
            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();

            Assert.That(runtime, Is.Not.Null);
            Assert.That(mainframe, Is.Not.Null);
            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.BootingSystems));
            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            for (int i = 0; i < 3; i++)
            {
                pipeline.Tick(default, 0.02f);
            }
            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Ready));
            Assert.That(mainframe.Sensors, Has.Count.EqualTo(6));
            Assert.That(mainframe.Scheduler.Tasks, Has.Count.EqualTo(7));
            Assert.That(
                root.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                root.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(1));
        }

        [Test]
        public void CompleteCraft_ContainsRequiredPhysicalSystems()
        {
            V3CraftRuntime runtime =
                Assemble().AssembledRoot.GetComponent<V3CraftRuntime>();

            Assert.That(
                runtime.InstalledParts.OfType<
                    RuntimeSpringMountInstance>().Count(),
                Is.EqualTo(4));
            Assert.That(
                runtime.InstalledParts.OfType<
                    RuntimeRotaryActuatorInstance>().Count(),
                Is.EqualTo(4));
            Assert.That(
                runtime.InstalledParts.OfType<
                    RuntimeAerodynamicFinInstance>().Count(),
                Is.EqualTo(4));
            Assert.That(
                runtime.InstalledParts.OfType<
                    RuntimeCoolingModuleInstance>().Count(),
                Is.EqualTo(1));
            Assert.That(
                runtime.InstalledParts.Count(
                    part => part.Definition is
                        V3SystemComputerDefinition),
                Is.EqualTo(7));
        }

        [Test]
        public void CompleteCraft_DoesNotInstallDuplicateSystemsOverlay()
        {
            GameObject root = Assemble().AssembledRoot;

            Assert.That(
                root.GetComponent<V3SystemsDebugDisplay>(),
                Is.Null,
                "The scene-owned V3SystemsConsoleController is the single " +
                "systems UI; the legacy craft overlay must not be installed.");
            Assert.That(
                root.GetComponent<V3SystemsConsoleController>(),
                Is.Null,
                "The systems console belongs to the development scene, " +
                "not to the physical craft runtime.");
        }

        [Test]
        public void SensorsAndFins_AreCompactAndMountedOnChassisSurfaces()
        {
            CraftBuildDefinition build = LoadBuild();
            Transform chassis = build.Chassis.Prefab.transform;

            AssertLocalPosition(
                chassis,
                "Sensor.Front",
                new Vector3(0f, 0f, 3.9f));
            AssertLocalPosition(
                chassis,
                "Sensor.Rear",
                new Vector3(0f, 0f, -3.9f));
            AssertLocalPosition(
                chassis,
                "Sensor.Left",
                new Vector3(-1.8f, 0f, 0f));
            AssertLocalPosition(
                chassis,
                "Sensor.Right",
                new Vector3(1.8f, 0f, 0f));
            AssertLocalPosition(
                chassis,
                "Sensor.Top",
                new Vector3(0f, 0.775f, 0f));
            AssertLocalPosition(
                chassis,
                "Sensor.Bottom",
                new Vector3(0f, -0.775f, 0f));

            AssertLocalPosition(
                chassis,
                "Aero.Front.Left",
                new Vector3(-1.8f, 0.35f, 2.9f));
            AssertLocalPosition(
                chassis,
                "Aero.Front.Right",
                new Vector3(1.8f, 0.35f, 2.9f));
            AssertLocalPosition(
                chassis,
                "Aero.Rear.Left",
                new Vector3(-1.8f, 0.35f, -2.9f));
            AssertLocalPosition(
                chassis,
                "Aero.Rear.Right",
                new Vector3(1.8f, 0.35f, -2.9f));

            GameObject sensorPrefab =
                build.Chassis.IntegratedSensors[0].Prefab;
            Assert.That(
                sensorPrefab.transform.localScale,
                Is.EqualTo(new Vector3(0.14f, 0.08f, 0.18f)));

            SocketInstallation aero = build.Installations.First(
                installation =>
                    installation.SocketId == "Aero.Front.Left");
            Assert.That(
                aero.Connector.Prefab.transform.localScale,
                Is.EqualTo(new Vector3(0.18f, 0.12f, 0.12f)));
            Assert.That(
                aero.Endpoint.Prefab.transform.localScale,
                Is.EqualTo(new Vector3(0.08f, 0.42f, 0.72f)));
        }

        [Test]
        public void MainframeTick_RunsSoftwareAndRoutesStandardRequests()
        {
            GameObject root = Assemble().AssembledRoot;
            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();
            var command = new V3PilotCommand
            {
                Throttle = 0.8f,
                Yaw = 0.3f,
                Strafe = -0.2f,
                StabilizationEnabled = true
            };

            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            pipeline.Tick(command, 0.02f);
            pipeline.Tick(command, 0.02f);
            V3PilotCommand routed =
                mainframe.FilterCommandByInstalledSoftware(command);

            Assert.That(routed.Throttle, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(mainframe.Observations.Snapshot, Is.Not.Empty);
            Assert.That(mainframe.ControlRouter.Requests, Is.Not.Empty);
            Assert.That(
                mainframe.Scheduler.Tasks.All(
                    task => task.GrantedRateHz <=
                        task.Software.MaximumUsefulRateHz + 0.001f),
                Is.True);
        }

        [Test]
        public void BalancedProfile_WeightsEveryControlDomainAtOne()
        {
            V3RouterProfileDefinition profile =
                LoadBuild().Chassis.RouterProfile;
            foreach (V3ControlDomain domain in
                System.Enum.GetValues(typeof(V3ControlDomain)))
            {
                Assert.That(
                    profile.GetWeight(domain),
                    Is.EqualTo(1f));
            }
        }

        [Test]
        public void V2ReferenceBuild_RemainsExactLegacyMass()
        {
            CraftBuildDefinition reference =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "V2Parity/Builds/" +
                    "ApexV3_V2Reference.asset");
            float mass = reference.Chassis.BaseMassKg;
            for (int i = 0; i < reference.Installations.Count; i++)
            {
                SocketInstallation installation =
                    reference.Installations[i];
                if (installation.Connector != null)
                {
                    mass += installation.Connector.Physical.massKg;
                }

                if (installation.Endpoint != null)
                {
                    mass += installation.Endpoint.Physical.massKg;
                }
            }

            Assert.That(mass, Is.EqualTo(11000f).Within(0.001f));
        }

        [Test]
        public void SystemsRuntime_SurvivesTenMinutesOfVirtualFixedTicks()
        {
            GameObject root = Assemble().AssembledRoot;
            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();
            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            const float step = 0.02f;
            for (int i = 0; i < 30000; i++)
            {
                var command = new V3PilotCommand
                {
                    Throttle = (i % 400) < 300 ? 0.7f : -0.25f,
                    Yaw = Mathf.Sin(i * 0.01f) * 0.35f,
                    Strafe = Mathf.Cos(i * 0.007f) * 0.2f,
                    StabilizationEnabled = true,
                    GripBreaker = (i % 1000) > 900
                };
                pipeline.Tick(command, step);
            }

            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Ready));
            Assert.That(
                float.IsNaN(mainframe.Scheduler.GrantedPower),
                Is.False);
            Assert.That(
                float.IsInfinity(mainframe.Scheduler.UsedComputePerSecond),
                Is.False);
            Assert.That(
                mainframe.Scheduler.Tasks.All(
                    task => task.GrantedRateHz >= 0f &&
                        !float.IsNaN(task.GrantedRateHz)),
                Is.True);
        }

        private V3CraftAssembler Assemble()
        {
            return Assemble(LoadBuild());
        }

        private V3CraftAssembler Assemble(CraftBuildDefinition build)
        {
            host = new GameObject("Systems Integration Test Host");
            V3CraftAssembler assembler =
                host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue =
                build;
            serialized.FindProperty("assembleOnStart").boolValue = false;
            serialized.FindProperty(
                "installReferenceControllers").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return assembler;
        }

        private static CraftBuildDefinition LoadBuild()
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "SystemsIntegration/" +
                    "Builds/ApexV3_SystemsIntegration.asset");
            Assert.That(build, Is.Not.Null);
            return build;
        }

        private static CraftBuildDefinition LoadBaselineBuild()
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "SystemsIntegration/" +
                    "Builds/Hovercraft_Baseline.asset");
            Assert.That(build, Is.Not.Null);
            return build;
        }

        private static void AssertLocalPosition(
            Transform root,
            string name,
            Vector3 expected)
        {
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            Transform match = transforms.FirstOrDefault(
                candidate => candidate.name == name);
            Assert.That(match, Is.Not.Null, name);
            Assert.That(
                match.localPosition,
                Is.EqualTo(expected),
                name);
        }
    }
}
