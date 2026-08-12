using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3SpringMountVariationTests
    {
        private const string BuildRoot =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/Builds/";

        [Test]
        public void SpringIntegrator_SettlesAtAuthoredStaticDeflection()
        {
            float displacement = 0f;
            float velocity = 0f;
            bool compressionStop = false;
            bool extensionStop = false;
            for (int i = 0; i < 480; i++)
            {
                RuntimeSpringMountInstance.IntegrateStep(
                    160000f,
                    100f,
                    600000f,
                    18000f,
                    0.3f,
                    0.12f,
                    1f / 240f,
                    ref displacement,
                    ref velocity,
                    out compressionStop,
                    out extensionStop);
            }

            Assert.That(
                displacement,
                Is.EqualTo(160000f / 600000f).Within(0.001f));
            Assert.That(Mathf.Abs(velocity), Is.LessThan(0.001f));
            Assert.That(compressionStop, Is.False);
            Assert.That(extensionStop, Is.False);

            for (int i = 0; i < 480; i++)
            {
                RuntimeSpringMountInstance.IntegrateStep(
                    0f,
                    100f,
                    600000f,
                    18000f,
                    0.3f,
                    0.12f,
                    1f / 240f,
                    ref displacement,
                    ref velocity,
                    out _,
                    out _);
            }

            Assert.That(Mathf.Abs(displacement), Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(velocity), Is.LessThan(0.001f));
        }

        [Test]
        public void SpringIntegrator_EnforcesCompressionAndExtensionStops()
        {
            float displacement = 0f;
            float velocity = 0f;
            bool atCompression = false;
            for (int i = 0; i < 120; i++)
            {
                RuntimeSpringMountInstance.IntegrateStep(
                    2000000f,
                    100f,
                    600000f,
                    18000f,
                    0.3f,
                    0.12f,
                    1f / 240f,
                    ref displacement,
                    ref velocity,
                    out bool compression,
                    out _);
                atCompression |= compression;
            }

            Assert.That(atCompression, Is.True);
            Assert.That(displacement, Is.LessThanOrEqualTo(0.3f));

            bool atExtension = false;
            for (int i = 0; i < 240; i++)
            {
                RuntimeSpringMountInstance.IntegrateStep(
                    -2000000f,
                    100f,
                    600000f,
                    18000f,
                    0.3f,
                    0.12f,
                    1f / 240f,
                    ref displacement,
                    ref velocity,
                    out _,
                    out bool extension);
                atExtension |= extension;
            }

            Assert.That(atExtension, Is.True);
            Assert.That(displacement, Is.GreaterThanOrEqualTo(-0.12f));
        }

        [Test]
        public void SpringVariation_UsesSameHoverEndpointAndOneRigidbody()
        {
            GameObject directHost = BuildCraft(
                "ApexV3_V2Reference.asset",
                out V3CraftAssembler directAssembler);
            GameObject springHost = BuildCraft(
                "ApexV3_HoverSpringDemo.asset",
                out V3CraftAssembler springAssembler);
            try
            {
                CraftBuildDefinition springBuild = springAssembler.Build;
                SocketInstallation installation =
                    springBuild.FindInstallation("Hover.Front.Left.Bottom");
                SocketInstallation directInstallation =
                    directAssembler.Build.FindInstallation(
                        "Hover.Front.Left.Bottom");
                BuildValidationReport report =
                    CraftBuildValidator.Validate(springBuild);
                V3CraftRuntime runtime =
                    springAssembler.AssembledRoot.GetComponent<V3CraftRuntime>();
                Rigidbody directBody =
                    directAssembler.AssembledRoot.GetComponent<Rigidbody>();
                Rigidbody springBody =
                    springAssembler.AssembledRoot.GetComponent<Rigidbody>();
                V3ThermalController thermal =
                    springAssembler.AssembledRoot.GetComponent<
                        V3ThermalController>();
                RuntimeSpringMountInstance spring =
                    springAssembler.AssembledRoot.GetComponentInChildren<
                        RuntimeSpringMountInstance>(true);
                RuntimeThrusterInstance mountedThruster =
                    springAssembler.AssembledRoot
                        .GetComponent<V3ActuatorCommandRouter>()
                        .FindThruster("Hover.Front.Left.Bottom");

                Assert.That(report.IsValid, Is.True, JoinIssues(report));
                Assert.That(
                    installation.Connector,
                    Is.TypeOf<SpringMountDefinition>());
                Assert.That(
                    installation.Endpoint,
                    Is.SameAs(directInstallation.Endpoint));
                Assert.That(runtime.InstalledParts, Has.Count.EqualTo(17));
                Assert.That(
                    springAssembler.AssembledRoot
                        .GetComponentsInChildren<Rigidbody>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    springAssembler.AssembledRoot
                        .GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1),
                    "Spring, socket, and thruster visuals must remain " +
                    "collider-free.");
                Assert.That(spring, Is.Not.Null);
                Assert.That(spring.ActuatedMount.localPosition.z, Is.LessThan(0f));
                Assert.That(
                    mountedThruster.transform.parent,
                    Is.SameAs(spring.ActuatedMount),
                    "The hover thruster must move with its spring connector.");
                Assert.That(
                    spring.ActuatedMount.Find("Actuated Mount Visual"),
                    Is.Not.Null,
                    "The visible spring connector must move with its endpoint.");
                Assert.That(directBody.mass, Is.EqualTo(11000f).Within(0.01f));
                Assert.That(springBody.mass, Is.EqualTo(11035f).Within(0.01f));
                Assert.That(
                    springBody.centerOfMass,
                    Is.Not.EqualTo(directBody.centerOfMass));
                Assert.That(thermal.Telemetry, Has.Count.EqualTo(16));
            }
            finally
            {
                Object.DestroyImmediate(directHost);
                Object.DestroyImmediate(springHost);
            }
        }

        [Test]
        public void SpringVariation_CompressesAndRecoversWithoutChangingForcePath()
        {
            GameObject host = BuildCraft(
                "ApexV3_HoverSpringDemo.asset",
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                V3PowerDistributor power =
                    root.GetComponent<V3PowerDistributor>();
                RuntimeSpringMountInstance spring =
                    root.GetComponentInChildren<RuntimeSpringMountInstance>();
                RuntimeThrusterInstance hover =
                    router.FindThruster("Hover.Front.Left.Bottom");
                Vector3 neutralPosition = spring.ActuatedMount.localPosition;

                var lift = new V3PilotCommand
                {
                    Lift = 1f,
                    StabilizationEnabled = true
                };
                for (int i = 0; i < 100; i++)
                {
                    pipeline.Tick(lift, 0.02f);
                }

                Assert.That(spring.CurrentEndpointLoadN, Is.EqualTo(160000f).Within(1f));
                Assert.That(
                    spring.DisplacementM,
                    Is.EqualTo(160000f / 600000f).Within(0.002f));
                Assert.That(spring.IsAtCompressionLimit, Is.False);
                Assert.That(hover.CurrentAppliedForceN, Is.EqualTo(160000f).Within(1f));
                Assert.That(hover.CurrentWorldForce.y, Is.GreaterThan(0f));
                Assert.That(
                    spring.ActuatedMount.localPosition,
                    Is.Not.EqualTo(neutralPosition));
                Assert.That(power.RequestedSystemsPower, Is.EqualTo(45f).Within(0.001f));

                for (int i = 0; i < 100; i++)
                {
                    pipeline.Tick(default, 0.02f);
                }

                Assert.That(Mathf.Abs(spring.DisplacementM), Is.LessThan(0.002f));
                Assert.That(Mathf.Abs(spring.VelocityMPerSecond), Is.LessThan(0.002f));

                pipeline.ResetDynamicState();
                Assert.That(spring.DisplacementM, Is.Zero);
                Assert.That(
                    spring.ActuatedMount.localPosition,
                    Is.EqualTo(neutralPosition));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpringVariation_RemainsBoundedDuringCoupledFlatGroundHover()
        {
            SimulationMode previousMode = Physics.simulationMode;
            GameObject ground = null;
            GameObject host = null;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "V3 Spring Flat Ground";
                ground.layer = 8;
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(200f, 1f, 200f);

                host = BuildCraft(
                    "ApexV3_HoverSpringDemo.asset",
                    out V3CraftAssembler assembler);
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3HoverController hover =
                    root.GetComponent<V3HoverController>();
                RuntimeSpringMountInstance spring =
                    root.GetComponentInChildren<RuntimeSpringMountInstance>();

                body.position = Vector3.up * 3.405f;
                body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();

                for (int i = 0; i < 250; i++)
                {
                    pipeline.Tick(
                        new V3PilotCommand { StabilizationEnabled = true },
                        0.02f);
                    Physics.Simulate(0.02f);
                }

                Assert.That(hover.IsGrounded, Is.True);
                Assert.That(body.position.y, Is.InRange(2f, 6f));
                Assert.That(float.IsNaN(body.position.y), Is.False);
                Assert.That(body.angularVelocity.magnitude, Is.LessThan(10f));
                Assert.That(
                    spring.DisplacementM,
                    Is.InRange(
                        -spring.SpringDefinition.ExtensionLimitM,
                        spring.SpringDefinition.CompressionLimitM));
                Assert.That(float.IsNaN(spring.DisplacementM), Is.False);
            }
            finally
            {
                Physics.simulationMode = previousMode;
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }

                if (ground != null)
                {
                    Object.DestroyImmediate(ground);
                }
            }
        }

        private static GameObject BuildCraft(
            string buildAssetName,
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    BuildRoot + buildAssetName);
            var host = new GameObject("V3 Spring Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }

        private static string JoinIssues(BuildValidationReport report)
        {
            var messages = new System.Collections.Generic.List<string>();
            for (int i = 0; i < report.Issues.Count; i++)
            {
                messages.Add(
                    $"{report.Issues[i].Code}: {report.Issues[i].Message}");
            }

            return string.Join("\n", messages);
        }
    }
}
