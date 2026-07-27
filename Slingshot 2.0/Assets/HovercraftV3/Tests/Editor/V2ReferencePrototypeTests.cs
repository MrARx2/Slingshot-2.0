using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V2ReferencePrototypeTests
    {
        private const string Root = "Assets/HovercraftV3/Prototype";

        private CraftBuildDefinition referenceBuild;
        private CraftBuildDefinition gimbalBuild;

        [SetUp]
        public void SetUp()
        {
            referenceBuild = AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                Root + "/Builds/ApexV3_V2Reference.asset");
            gimbalBuild = AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                Root + "/Builds/ApexV3_MainGimbalDemo.asset");
        }

        [Test]
        public void ReferenceBuild_IsValid()
        {
            Assert.That(referenceBuild, Is.Not.Null);
            BuildValidationReport report = CraftBuildValidator.Validate(referenceBuild);

            Assert.That(report.IsValid, Is.True, JoinIssues(report));
        }

        [Test]
        public void ReferenceChassis_HasSixteenStableSockets()
        {
            Assert.That(referenceBuild, Is.Not.Null);
            V3Socket[] sockets =
                referenceBuild.Chassis.Prefab.GetComponentsInChildren<V3Socket>(true);
            var ids = new HashSet<string>();
            for (int i = 0; i < sockets.Length; i++)
            {
                Assert.That(sockets[i].SocketId, Is.Not.Empty);
                Assert.That(ids.Add(sockets[i].SocketId), Is.True);
            }

            Assert.That(sockets, Has.Length.EqualTo(16));
        }

        [Test]
        public void ReferenceChassis_ThrusterSocketsExposeOutwardMounts()
        {
            Assert.That(referenceBuild, Is.Not.Null);
            V3Socket[] sockets =
                referenceBuild.Chassis.Prefab.GetComponentsInChildren<V3Socket>(true);
            int externalSocketCount = 0;

            for (int i = 0; i < sockets.Length; i++)
            {
                V3Socket socket = sockets[i];
                if (!socket.AllowsDirectEndpoint(EndpointCategory.Thruster))
                {
                    Assert.That(
                        socket.transform.Find("Socket Visual"),
                        Is.Null,
                        $"{socket.SocketId} is an internal equipment bay.");
                    continue;
                }

                externalSocketCount++;
                Transform socketVisual =
                    socket.transform.Find("Socket Visual");
                Assert.That(
                    socketVisual,
                    Is.Not.Null,
                    $"{socket.SocketId} must visibly project from the hull.");
                Assert.That(
                    socket.MountTransform,
                    Is.Not.SameAs(socket.transform),
                    $"{socket.SocketId} must mount parts outside its socket block.");
                Assert.That(
                    socket.MountTransform.name,
                    Is.EqualTo("Endpoint Mount"));
                Assert.That(
                    socket.MountTransform.localPosition.z,
                    Is.LessThan(-0.1f),
                    $"{socket.SocketId} hardware must extend opposite thrust.");
                Assert.That(
                    socketVisual.localPosition.z,
                    Is.LessThan(0f));
                Assert.That(
                    socketVisual.GetComponent<Collider>(),
                    Is.Null,
                    "Socket visuals must not add overlapping collision geometry.");
            }

            Assert.That(externalSocketCount, Is.EqualTo(14));
        }

        [Test]
        public void ReferenceChassis_HasFourDroneStyleVerticalThrusterOutriggers()
        {
            Transform chassis = referenceBuild.Chassis.Prefab.transform;
            string[] positions =
            {
                "Front Left",
                "Front Right",
                "Rear Left",
                "Rear Right"
            };

            for (int i = 0; i < positions.Length; i++)
            {
                Transform outrigger = chassis.Find(
                    "Vertical Thruster Outrigger " + positions[i]);
                Assert.That(outrigger, Is.Not.Null);
                Assert.That(
                    Mathf.Abs(outrigger.localPosition.x),
                    Is.GreaterThan(1.8f),
                    "Each outrigger must sit beyond the chassis side.");
                Assert.That(outrigger.localPosition.y, Is.Zero);

                Transform visual = outrigger.Find("Outrigger Visual");
                Assert.That(visual, Is.Not.Null);
                Assert.That(visual.localScale.y, Is.LessThan(0.25f));
                Assert.That(visual.GetComponent<Collider>(), Is.Null);

                V3Socket[] mountedSockets =
                    outrigger.GetComponentsInChildren<V3Socket>(true);
                Assert.That(
                    mountedSockets,
                    Has.Length.EqualTo(3),
                    "Each extension carries bottom hover, top control, " +
                    "and outer strafe sockets.");
                Assert.That(
                    HasSocketWithDirection(mountedSockets, Vector3.up),
                    Is.True);
                Assert.That(
                    HasSocketWithDirection(mountedSockets, Vector3.down),
                    Is.True);
            }
        }

        [Test]
        public void ReferenceBuild_MassIsElevenThousandKg()
        {
            Assert.That(CalculateMass(referenceBuild), Is.EqualTo(11000f).Within(0.01f));
        }

        [Test]
        public void ReferenceBuild_CalculatedCenterOfMassMatchesV2()
        {
            Vector3 center = CalculateCenterOfMass(referenceBuild);

            Assert.That(center.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(center.y, Is.EqualTo(-0.5f).Within(0.001f));
            Assert.That(center.z, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ReferenceActuators_ExposePhysicalParityAuthority()
        {
            ThrusterDefinition brake =
                referenceBuild.FindInstallation("Braking.Front.Center")
                    .Endpoint as ThrusterDefinition;
            ThrusterDefinition strafe =
                referenceBuild.FindInstallation("Strafe.Left.Front")
                    .Endpoint as ThrusterDefinition;

            Assert.That(brake, Is.Not.Null);
            Assert.That(strafe, Is.Not.Null);
            Assert.That(
                brake.MaximumForwardForceN,
                Is.EqualTo(1700000f).Within(1f),
                "The brake must expose both drive and traction force physically.");
            Assert.That(
                1700000f * 0.22352941f,
                Is.EqualTo(380000f).Within(1f),
                "The post-reversal drive contribution must remain 380 kN.");
            Assert.That(
                strafe.MaximumForwardForceN,
                Is.EqualTo(800000f).Within(1f),
                "Lateral hardware must expose the physical V2-equivalent yaw authority.");
            Assert.That(
                800000f * 0.19f,
                Is.EqualTo(152000f).Within(1f),
                "Full strafe must retain the calibrated V2 lateral force.");
        }

        [Test]
        public void GimbalVariation_UsesSameMainThrusterThroughConnector()
        {
            Assert.That(gimbalBuild, Is.Not.Null);
            BuildValidationReport report = CraftBuildValidator.Validate(gimbalBuild);
            SocketInstallation referenceRear =
                referenceBuild.FindInstallation("Propulsion.Rear.Center");
            SocketInstallation gimbalRear =
                gimbalBuild.FindInstallation("Propulsion.Rear.Center");

            Assert.That(report.IsValid, Is.True, JoinIssues(report));
            Assert.That(gimbalRear.Connector, Is.TypeOf<GimbalDefinition>());
            Assert.That(gimbalRear.Endpoint, Is.SameAs(referenceRear.Endpoint));
            Assert.That(CalculateMass(gimbalBuild), Is.EqualTo(11120f).Within(0.01f));
        }

        [Test]
        public void RuntimeAssembler_RebuildsReferenceWithOneRigidbody()
        {
            var host = new GameObject("V3 Assembler Test Host");
            try
            {
                V3CraftAssembler assembler = host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue = referenceBuild;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                bool rebuilt = assembler.Rebuild();
                V3CraftRuntime runtime =
                    assembler.AssembledRoot.GetComponent<V3CraftRuntime>();
                Rigidbody body = assembler.AssembledRoot.GetComponent<Rigidbody>();

                Assert.That(rebuilt, Is.True);
                Assert.That(runtime, Is.Not.Null);
                Assert.That(runtime.InstalledParts, Has.Count.EqualTo(16));
                Assert.That(
                    assembler.AssembledRoot.GetComponentsInChildren<Rigidbody>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    assembler.AssembledRoot.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1),
                    "Prototype socket, connector, and part visuals must remain " +
                    "collider-free; the chassis owns the compound body collider.");
                AssertVerticalThrustersStayWithinChassisHeight(runtime);
                Assert.That(body.mass, Is.EqualTo(11000f).Within(0.01f));
                Assert.That(body.centerOfMass.y, Is.EqualTo(-0.5f).Within(0.001f));
                Assert.That(body.linearDamping, Is.Zero);
                Assert.That(
                    body.angularDamping,
                    Is.Zero,
                    "V3 rotational damping must come from installed actuators.");
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3ControllerPipeline>(),
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3TractionController>(),
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3GimbalController>(),
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3ThermalController>(),
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3ThermalDebugDisplay>(),
                    Is.Not.Null);
                V3ThrusterDebugView thrusterDebug =
                    assembler.AssembledRoot.GetComponent<V3ThrusterDebugView>();
                Assert.That(thrusterDebug, Is.Not.Null);
                Assert.That(thrusterDebug.ThrusterCount, Is.EqualTo(14));
                Assert.That(thrusterDebug.ActiveThrusterCount, Is.Zero);
                Assert.That(thrusterDebug.ShowVisualization, Is.False);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3CockpitWarningController>(),
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3CockpitWarningDisplay>(),
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3PowerDistributor>().Core,
                    Is.Not.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponent<V3ActuatorCommandRouter>()
                        .ThrusterCount,
                    Is.EqualTo(14));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GimbalVariation_ThrusterAndVisualFollowActuatedMountOutsideHull()
        {
            var host = new GameObject("V3 Gimbal Hierarchy Test Host");
            try
            {
                V3CraftAssembler assembler = host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue = gimbalBuild;
                serialized.FindProperty("assembleOnStart").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(assembler.Rebuild(), Is.True);
                V3CraftRuntime runtime =
                    assembler.AssembledRoot.GetComponent<V3CraftRuntime>();
                RuntimeGimbalInstance gimbal = null;
                RuntimeThrusterInstance mainThruster = null;
                for (int i = 0; i < runtime.InstalledParts.Count; i++)
                {
                    RuntimePartInstance part = runtime.InstalledParts[i];
                    if (part.ParentSocketId != "Propulsion.Rear.Center")
                    {
                        continue;
                    }

                    gimbal ??= part as RuntimeGimbalInstance;
                    mainThruster ??= part as RuntimeThrusterInstance;
                }

                Assert.That(gimbal, Is.Not.Null);
                Assert.That(mainThruster, Is.Not.Null);
                Assert.That(gimbal.ActuatedMount, Is.Not.Null);
                Assert.That(gimbal.ActuatedMount.localPosition.z, Is.LessThan(0f));
                Assert.That(
                    mainThruster.transform.parent,
                    Is.SameAs(gimbal.ActuatedMount),
                    "The thruster must swivel with its connector mount.");

                Transform movingVisual =
                    gimbal.ActuatedMount.Find("Actuated Mount Visual");
                Assert.That(
                    movingVisual,
                    Is.Not.Null,
                    "The connector body must visibly swivel with the gimbal.");
                Assert.That(movingVisual.GetComponent<Collider>(), Is.Null);

                Transform thrusterVisual =
                    mainThruster.transform.Find("Primitive Visual");
                Assert.That(thrusterVisual, Is.Not.Null);
                Assert.That(thrusterVisual.localPosition.z, Is.LessThan(0f));
                Assert.That(thrusterVisual.GetComponent<Collider>(), Is.Null);
                Assert.That(
                    assembler.AssembledRoot.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ControllerPipeline_ForwardCommandUsesPoweredMainThrusterPath()
        {
            var host = new GameObject("V3 Controller Test Host");
            try
            {
                V3CraftAssembler assembler = host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue = referenceBuild;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(assembler.Rebuild(), Is.True);
                V3ControllerPipeline pipeline =
                    assembler.AssembledRoot.GetComponent<V3ControllerPipeline>();
                V3ActuatorCommandRouter router =
                    assembler.AssembledRoot.GetComponent<V3ActuatorCommandRouter>();
                V3PowerDistributor power =
                    assembler.AssembledRoot.GetComponent<V3PowerDistributor>();
                Rigidbody body = assembler.AssembledRoot.GetComponent<Rigidbody>();

                pipeline.Tick(
                    new V3PilotCommand
                    {
                        Throttle = 1f,
                        StabilizationEnabled = true
                    },
                    0.02f);

                RuntimeThrusterInstance main =
                    router.FindThruster("Propulsion.Rear.Center");
                RuntimeThrusterInstance brake =
                    router.FindThruster("Braking.Front.Center");

                Assert.That(main, Is.Not.Null);
                Assert.That(main.RequestedOutput, Is.EqualTo(0.135f).Within(0.0001f));
                Assert.That(main.CurrentOutput, Is.EqualTo(0.135f).Within(0.0001f));
                Assert.That(main.CurrentAppliedForceN, Is.EqualTo(64800f).Within(1f));
                Assert.That(main.GrantedPower, Is.EqualTo(main.RequestedPower).Within(0.001f));
                Assert.That(main.CurrentWorldForce.z, Is.GreaterThan(0f));
                Assert.That(brake.CurrentOutput, Is.Zero.Within(0.0001f));
                Assert.That(power.IsPowerLimited, Is.False);
                Assert.That(power.AreSystemsPowerLimited, Is.False);
                Assert.That(power.SystemsIdleDemand, Is.EqualTo(45f).Within(0.001f));
                Assert.That(power.GrantedSystemsPower, Is.EqualTo(45f).Within(0.001f));
                Assert.That(
                    power.AvailablePropulsionPower,
                    Is.EqualTo(4455f).Within(0.001f));
                Assert.That(
                    assembler.AssembledRoot.GetComponentsInChildren<Rigidbody>(true),
                    Has.Length.EqualTo(1));
                Assert.That(body.mass, Is.EqualTo(11000f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ControllerPipeline_LosesAuthorityWhenControlPartIsDisabled()
        {
            var host = new GameObject("V3 Physical Control Authority Test");
            try
            {
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue =
                    referenceBuild;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(assembler.Rebuild(), Is.True);
                GameObject root = assembler.AssembledRoot;
                V3CapabilityRegistry capabilities =
                    root.GetComponent<V3CapabilityRegistry>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                RuntimePartInstance controllerPart =
                    capabilities.GetProviders(
                        PartCapability.DriveControl)[0];
                controllerPart.SetEnabled(false);

                pipeline.Tick(
                    new V3PilotCommand
                    {
                        Throttle = 1f,
                        Strafe = 1f,
                        Yaw = 1f,
                        Lift = 1f,
                        StabilizationEnabled = true,
                        EmergencyOverload = true
                    },
                    0.02f);

                Assert.That(
                    capabilities.HasOperationalProvider(
                        PartCapability.DriveControl),
                    Is.False);
                Assert.That(
                    router.FindThruster("Propulsion.Rear.Center")
                        .CurrentAppliedForceN,
                    Is.Zero);
                Assert.That(
                    router.FindThruster("Hover.Front.Left.Bottom")
                        .CurrentAppliedForceN,
                    Is.Zero);
                Assert.That(
                    router.FindThruster("Strafe.Left.Front")
                        .CurrentAppliedForceN,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static float CalculateMass(CraftBuildDefinition build)
        {
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

            return mass;
        }

        private static Vector3 CalculateCenterOfMass(CraftBuildDefinition build)
        {
            Transform root = build.Chassis.Prefab.transform;
            V3Socket[] sockets = root.GetComponentsInChildren<V3Socket>(true);
            var socketsById = new Dictionary<string, V3Socket>();
            for (int i = 0; i < sockets.Length; i++)
            {
                socketsById.Add(sockets[i].SocketId, sockets[i]);
            }

            float totalMass = build.Chassis.BaseMassKg;
            Vector3 weighted =
                build.Chassis.BaseCenterOfMass * build.Chassis.BaseMassKg;
            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation?.Endpoint == null)
                {
                    continue;
                }

                V3Socket socket = socketsById[installation.SocketId];
                Vector3 localPosition = root.InverseTransformPoint(
                    socket.MountTransform.TransformPoint(
                        installation.Endpoint.Physical.localCenterOfMass));
                float endpointMass = installation.Endpoint.Physical.massKg;
                totalMass += endpointMass;
                weighted += localPosition * endpointMass;

                if (installation.Connector != null)
                {
                    float connectorMass = installation.Connector.Physical.massKg;
                    totalMass += connectorMass;
                    weighted += localPosition * connectorMass;
                }
            }

            return weighted / totalMass + build.Chassis.ParityCenterOfMassCalibration;
        }

        private static bool HasSocketWithDirection(
            IReadOnlyList<V3Socket> sockets,
            Vector3 expectedRootDirection)
        {
            for (int i = 0; i < sockets.Count; i++)
            {
                Vector3 direction =
                    sockets[i].transform.root.InverseTransformDirection(
                        sockets[i].transform.forward);
                if (Vector3.Dot(
                    direction.normalized,
                    expectedRootDirection.normalized) > 0.999f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertVerticalThrustersStayWithinChassisHeight(
            V3CraftRuntime runtime)
        {
            Transform root = runtime.transform;
            float bottom = root.TransformPoint(0f, -0.775f, 0f).y;
            float top = root.TransformPoint(0f, 0.775f, 0f).y;
            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                if (!(runtime.InstalledParts[i] is RuntimeThrusterInstance thruster) ||
                    (!thruster.ParentSocketId.StartsWith("Hover.") &&
                     !thruster.ParentSocketId.StartsWith("Control.")))
                {
                    continue;
                }

                Renderer visual =
                    thruster.GetComponentInChildren<Renderer>(true);
                Assert.That(visual, Is.Not.Null);
                Assert.That(
                    visual.bounds.min.y,
                    Is.GreaterThanOrEqualTo(bottom - 0.001f),
                    $"{thruster.ParentSocketId} must not hang below the chassis.");
                Assert.That(
                    visual.bounds.max.y,
                    Is.LessThanOrEqualTo(top + 0.001f),
                    $"{thruster.ParentSocketId} must not make the craft taller.");
            }
        }

        private static string JoinIssues(BuildValidationReport report)
        {
            var messages = new List<string>();
            for (int i = 0; i < report.Issues.Count; i++)
            {
                messages.Add($"{report.Issues[i].Code}: {report.Issues[i].Message}");
            }

            return string.Join("\n", messages);
        }
    }
}
