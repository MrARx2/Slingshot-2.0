using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3GimbalVariationTests
    {
        private const string BuildRoot =
            "Assets/HovercraftV3/Prototype/Builds/";

        [Test]
        public void GimbalServo_UsesAuthoredAccelerationAndSpeedLimits()
        {
            float velocity =
                RuntimeGimbalInstance.CalculateRequestedVelocity(
                    0f,
                    -30f,
                    0f,
                    180f,
                    720f,
                    0.02f);

            Assert.That(velocity, Is.EqualTo(-14.4f).Within(0.0001f));
        }

        [Test]
        public void GimbalInput_HoldsTransientMouseCommandBeforeRecentering()
        {
            float holdRemaining = 0f;
            float held = V3GimbalController.UpdateTransientInput(
                0.8f,
                0f,
                ref holdRemaining,
                0.2f,
                4f,
                0.02f);

            Assert.That(held, Is.EqualTo(0.8f));
            Assert.That(holdRemaining, Is.EqualTo(0.2f));

            for (int i = 0; i < 5; i++)
            {
                held = V3GimbalController.UpdateTransientInput(
                    0f,
                    held,
                    ref holdRemaining,
                    0.2f,
                    4f,
                    0.02f);
            }

            Assert.That(held, Is.EqualTo(0.8f));
            for (int i = 0; i < 20; i++)
            {
                held = V3GimbalController.UpdateTransientInput(
                    0f,
                    held,
                    ref holdRemaining,
                    0.2f,
                    4f,
                    0.02f);
            }

            Assert.That(held, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void GimbalVariation_SameThrusterGainsPoweredYawDeflection()
        {
            GameObject directHost = BuildCraft(
                "ApexV3_V2Reference.asset",
                out V3CraftAssembler directAssembler);
            GameObject gimbalHost = BuildCraft(
                "ApexV3_MainGimbalDemo.asset",
                out V3CraftAssembler gimbalAssembler);
            try
            {
                V3ActuatorCommandRouter directRouter =
                    directAssembler.AssembledRoot.GetComponent<
                        V3ActuatorCommandRouter>();
                V3ActuatorCommandRouter gimbalRouter =
                    gimbalAssembler.AssembledRoot.GetComponent<
                        V3ActuatorCommandRouter>();
                V3ControllerPipeline directPipeline =
                    directAssembler.AssembledRoot.GetComponent<
                        V3ControllerPipeline>();
                V3ControllerPipeline gimbalPipeline =
                    gimbalAssembler.AssembledRoot.GetComponent<
                        V3ControllerPipeline>();
                V3GimbalController gimbalController =
                    gimbalAssembler.AssembledRoot.GetComponent<
                        V3GimbalController>();
                V3PowerDistributor directPower =
                    directAssembler.AssembledRoot.GetComponent<
                        V3PowerDistributor>();
                V3PowerDistributor gimbalPower =
                    gimbalAssembler.AssembledRoot.GetComponent<
                        V3PowerDistributor>();
                Rigidbody directBody =
                    directAssembler.AssembledRoot.GetComponent<Rigidbody>();
                Rigidbody gimbalBody =
                    gimbalAssembler.AssembledRoot.GetComponent<Rigidbody>();
                RuntimeThrusterInstance directMain =
                    directRouter.FindThruster("Propulsion.Rear.Center");
                RuntimeThrusterInstance gimbalMain =
                    gimbalRouter.FindThruster("Propulsion.Rear.Center");

                Assert.That(gimbalController.Gimbals, Has.Count.EqualTo(1));
                RuntimeGimbalInstance gimbal = gimbalController.Gimbals[0];
                Assert.That(gimbal.ActuatedMount, Is.Not.Null);
                Assert.That(
                    gimbalMain.transform.parent,
                    Is.SameAs(gimbal.ActuatedMount),
                    "The complete thruster endpoint must be parented to the moving mount.");
                Renderer[] thrusterVisuals =
                    gimbalMain.GetComponentsInChildren<Renderer>(true);
                Assert.That(thrusterVisuals, Is.Not.Empty);
                for (int i = 0; i < thrusterVisuals.Length; i++)
                {
                    Assert.That(
                        thrusterVisuals[i].transform.IsChildOf(gimbal.ActuatedMount),
                        Is.True,
                        "Every thruster visual must follow the moving mount.");
                }

                Transform mountVisual =
                    gimbal.ActuatedMount.Find("Actuated Mount Visual");
                Assert.That(
                    mountVisual,
                    Is.Not.Null,
                    "The prototype gimbal's moving visual must live under Child Mount.");
                Assert.That(gimbalMain.Definition, Is.SameAs(directMain.Definition));
                Assert.That(directBody.mass, Is.EqualTo(11000f).Within(0.01f));
                Assert.That(gimbalBody.mass, Is.EqualTo(11120f).Within(0.01f));
                Assert.That(
                    gimbalBody.centerOfMass.z,
                    Is.Not.EqualTo(directBody.centerOfMass.z).Within(0.001f));

                var command = new V3PilotCommand
                {
                    Throttle = 1f,
                    Yaw = 1f,
                    StabilizationEnabled = true
                };
                directPipeline.Tick(command, 0.02f);
                gimbalPipeline.Tick(command, 0.02f);

                float dynamicSystemsDemand = gimbalPower.RequestedSystemsPower;
                Vector3 directMoment = Vector3.Cross(
                    directMain.transform.position - directBody.worldCenterOfMass,
                    directMain.CurrentWorldForce);
                Vector3 gimbalMoment = Vector3.Cross(
                    gimbalMain.transform.position - gimbalBody.worldCenterOfMass,
                    gimbalMain.CurrentWorldForce);

                Assert.That(directMain.CurrentWorldForce.x, Is.Zero.Within(0.01f));
                Assert.That(gimbalMain.CurrentWorldForce.x, Is.LessThan(0f));
                Assert.That(directMoment.y, Is.Zero.Within(0.1f));
                Assert.That(gimbalMoment.y, Is.GreaterThan(0f));
                Assert.That(gimbal.CurrentYawDegrees, Is.LessThan(0f));
                Assert.That(gimbal.TargetYawDegrees, Is.EqualTo(-30f));
                Assert.That(
                    dynamicSystemsDemand,
                    Is.GreaterThan(directPower.RequestedSystemsPower + 10f));
                Assert.That(gimbal.GrantedPower, Is.EqualTo(gimbal.RequestedPower));
                Assert.That(gimbal.CurrentActuatorTorqueNm, Is.GreaterThan(0f));
                Assert.That(
                    gimbal.CurrentActuatorTorqueNm,
                    Is.LessThanOrEqualTo(
                        gimbal.GimbalDefinition.ActuatorTorqueNm));

                for (int i = 1; i < 50; i++)
                {
                    gimbalPipeline.Tick(command, 0.02f);
                }

                Assert.That(
                    gimbal.CurrentYawDegrees,
                    Is.EqualTo(-30f).Within(0.001f));
                Assert.That(
                    Mathf.Abs(gimbal.YawVelocityDegreesPerSecond),
                    Is.LessThanOrEqualTo(
                        gimbal.GimbalDefinition.RotationSpeedDegreesPerSecond));
                Assert.That(
                    gimbalAssembler.AssembledRoot.GetComponent<
                        V3HoverController>(),
                    Is.Not.Null);

                gimbalPipeline.ResetDynamicState();
                Assert.That(gimbal.CurrentYawDegrees, Is.Zero.Within(0.0001f));
                Assert.That(
                    gimbal.ActuatedMount.localRotation,
                    Is.EqualTo(Quaternion.identity));
            }
            finally
            {
                Object.DestroyImmediate(directHost);
                Object.DestroyImmediate(gimbalHost);
            }
        }

        [Test]
        public void GimbalVariation_PhysicalSimulationAddsNaturalYawAuthority()
        {
            SimulationMode previousMode = Physics.simulationMode;
            GameObject directHost = BuildCraft(
                "ApexV3_V2Reference.asset",
                out V3CraftAssembler directAssembler);
            GameObject gimbalHost = BuildCraft(
                "ApexV3_MainGimbalDemo.asset",
                out V3CraftAssembler gimbalAssembler);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Rigidbody directBody =
                    directAssembler.AssembledRoot.GetComponent<Rigidbody>();
                Rigidbody gimbalBody =
                    gimbalAssembler.AssembledRoot.GetComponent<Rigidbody>();
                directBody.useGravity = false;
                gimbalBody.useGravity = false;
                directBody.position = new Vector3(-50f, 20f, 0f);
                gimbalBody.position = new Vector3(50f, 20f, 0f);
                directBody.rotation = Quaternion.identity;
                gimbalBody.rotation = Quaternion.identity;
                Physics.SyncTransforms();

                V3ControllerPipeline directPipeline =
                    directBody.GetComponent<V3ControllerPipeline>();
                V3ControllerPipeline gimbalPipeline =
                    gimbalBody.GetComponent<V3ControllerPipeline>();
                var command = new V3PilotCommand
                {
                    Throttle = 1f,
                    Yaw = 1f,
                    StabilizationEnabled = false
                };

                for (int i = 0; i < 75; i++)
                {
                    directPipeline.Tick(command, 0.02f);
                    gimbalPipeline.Tick(command, 0.02f);
                    Physics.Simulate(0.02f);
                }

                float directYawRate =
                    Mathf.Abs(
                        directBody.transform.InverseTransformDirection(
                            directBody.angularVelocity).y);
                float gimbalYawRate =
                    Mathf.Abs(
                        gimbalBody.transform.InverseTransformDirection(
                            gimbalBody.angularVelocity).y);
                Assert.That(directYawRate, Is.GreaterThan(0.01f));
                Assert.That(
                    gimbalYawRate,
                    Is.GreaterThan(directYawRate * 1.05f),
                    "The connector-directed main thrust should add measurable " +
                    "yaw authority beyond the unchanged lateral thrusters.");
            }
            finally
            {
                Physics.simulationMode = previousMode;
                Object.DestroyImmediate(directHost);
                Object.DestroyImmediate(gimbalHost);
            }
        }

        private static GameObject BuildCraft(
            string buildAssetName,
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    BuildRoot + buildAssetName);
            var host = new GameObject("V3 Gimbal Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }
    }
}
