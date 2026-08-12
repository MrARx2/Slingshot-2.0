using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3CraftTelemetryHubTests
    {
        private const string ReferenceBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/Builds/" +
            "ApexV3_V2Reference.asset";
        private const string GimbalBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/Builds/" +
            "ApexV3_MainGimbalDemo.asset";

        [Test]
        public void ReferenceSnapshot_ConsolidatesIdentityMotionPowerAndParts()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                true,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3PowerDistributor power =
                    root.GetComponent<V3PowerDistributor>();
                V3ThermalController thermal =
                    root.GetComponent<V3ThermalController>();
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                body.linearVelocity =
                    root.transform.TransformDirection(
                        new Vector3(2f, 3f, 4f));
                body.angularVelocity =
                    root.transform.TransformDirection(
                        new Vector3(0.1f, 0.2f, 0.3f));
                pipeline.Tick(
                    new V3PilotCommand
                    {
                        Throttle = 1f,
                        StabilizationEnabled = true
                    },
                    1f);
                thermal.Tick(1f);

                V3CraftTelemetrySnapshot snapshot =
                    telemetry.CaptureSnapshot();

                Assert.That(snapshot.BuildName, Is.EqualTo("ApexV3_V2Reference"));
                Assert.That(
                    snapshot.ChassisStableId,
                    Is.EqualTo("Chassis.Apex.V2Reference"));
                Assert.That(snapshot.MassKg, Is.EqualTo(11000f).Within(0.01f));
                Assert.That(snapshot.CenterOfMass.y, Is.EqualTo(-0.5f).Within(0.001f));
                Assert.That(snapshot.LocalVelocity.x, Is.EqualTo(2f).Within(0.001f));
                Assert.That(snapshot.LocalVelocity.y, Is.EqualTo(3f).Within(0.001f));
                Assert.That(snapshot.LocalVelocity.z, Is.EqualTo(4f).Within(0.001f));
                Assert.That(
                    snapshot.SpeedKmh,
                    Is.EqualTo(new Vector3(2f, 3f, 4f).magnitude * 3.6f)
                        .Within(0.001f));
                Assert.That(snapshot.Parts, Has.Count.EqualTo(16));
                Assert.That(snapshot.PriorityPower, Has.Count.EqualTo(5));
                Assert.That(
                    snapshot.PowerAllocationMode,
                    Is.EqualTo(V3PowerAllocationMode.Balanced));
                Assert.That(
                    snapshot.RequestedSystemsPower,
                    Is.EqualTo(power.RequestedSystemsPower).Within(0.001f));
                Assert.That(
                    snapshot.GrantedPropulsionPower,
                    Is.EqualTo(power.GrantedPropulsionPower).Within(0.001f));
                Assert.That(
                    snapshot.RequestedTotalPower,
                    Is.EqualTo(
                        power.RequestedSystemsPower +
                        power.RequestedPropulsionPower).Within(0.001f));
                Assert.That(
                    snapshot.MaximumTemperatureC,
                    Is.EqualTo(thermal.MaximumTemperatureC).Within(0.001f));

                Assert.That(
                    snapshot.TryFindPart(
                        "Propulsion.Rear.Center",
                        "Thruster.Main.V2Reference",
                        out V3PartRuntimeTelemetry main),
                    Is.True);
                Assert.That(main.IsConnector, Is.False);
                Assert.That(main.RequestedOutput, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(main.ActualOutput, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(main.RequestedPower, Is.EqualTo(720f).Within(0.001f));
                Assert.That(main.GrantedPower, Is.EqualTo(720f).Within(0.001f));
                Assert.That(main.AppliedForceN, Is.EqualTo(720000f).Within(1f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CapturedSnapshot_IsStableAfterLiveStateAndModeChange()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                true,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                pipeline.Tick(
                    new V3PilotCommand { Throttle = 1f },
                    1f);
                V3CraftTelemetrySnapshot before =
                    telemetry.CaptureSnapshot();
                Assert.That(
                    before.TryFindPart(
                        "Propulsion.Rear.Center",
                        "Thruster.Main.V2Reference",
                        out V3PartRuntimeTelemetry beforeMain),
                    Is.True);

                pipeline.ResetDynamicState();
                pipeline.Tick(default, 1f);
                pipeline.Tick(
                    new V3PilotCommand
                    {
                        CyclePowerAllocationMode = true
                    },
                    1f);
                V3CraftTelemetrySnapshot after =
                    telemetry.CaptureSnapshot();
                Assert.That(
                    after.TryFindPart(
                        "Propulsion.Rear.Center",
                        "Thruster.Main.V2Reference",
                        out V3PartRuntimeTelemetry afterMain),
                    Is.True);

                Assert.That(beforeMain.RequestedPower, Is.EqualTo(720f).Within(0.001f));
                Assert.That(afterMain.RequestedPower, Is.EqualTo(9.599999f).Within(0.001f));
                Assert.That(before.PowerAllocationMode, Is.EqualTo(V3PowerAllocationMode.Balanced));
                Assert.That(after.PowerAllocationMode, Is.EqualTo(V3PowerAllocationMode.Propulsion));
                Assert.That(after.Sequence, Is.GreaterThan(before.Sequence));
                Assert.That(
                    after.PriorityPower[0].Priority,
                    Is.EqualTo(V3ActuatorPriority.Critical));
                Assert.That(
                    after.PriorityPower[1].Priority,
                    Is.EqualTo(V3ActuatorPriority.Drive));

                Assert.That(
                    before.Parts is ICollection<V3PartRuntimeTelemetry> collection &&
                    collection.IsReadOnly,
                    Is.True);
                Assert.Throws<NotSupportedException>(
                    () => ((IList<V3PartRuntimeTelemetry>)before.Parts)
                        .Add(default));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void NestedConnectorSnapshot_PreservesBothPartsAtOneSocket()
        {
            GameObject host = BuildCraft(
                GimbalBuildPath,
                true,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                pipeline.Tick(
                    new V3PilotCommand { Yaw = 1f },
                    1f);
                V3CraftTelemetrySnapshot snapshot =
                    telemetry.CaptureSnapshot();

                Assert.That(snapshot.Parts, Has.Count.EqualTo(17));
                int rearPartCount = 0;
                bool foundGimbal = false;
                bool foundMain = false;
                for (int i = 0; i < snapshot.Parts.Count; i++)
                {
                    V3PartRuntimeTelemetry part = snapshot.Parts[i];
                    if (part.SocketId != "Propulsion.Rear.Center")
                    {
                        continue;
                    }

                    rearPartCount++;
                    if (part.StablePartId ==
                        "Connector.Gimbal.Main.V2Reference")
                    {
                        foundGimbal = true;
                        Assert.That(part.IsConnector, Is.True);
                        Assert.That(
                            Mathf.Abs(part.ConnectorDeflection.y),
                            Is.GreaterThan(0f));
                    }
                    else if (part.StablePartId ==
                             "Thruster.Main.V2Reference")
                    {
                        foundMain = true;
                        Assert.That(part.IsConnector, Is.False);
                    }
                }

                Assert.That(rearPartCount, Is.EqualTo(2));
                Assert.That(foundGimbal, Is.True);
                Assert.That(foundMain, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RuntimeWithoutReferenceControllers_StillInstallsTelemetryKernel()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                false,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                Assert.That(telemetry, Is.Not.Null);
                Assert.That(
                    root.GetComponent<V3PowerDistributor>(),
                    Is.Null);

                V3CraftTelemetrySnapshot snapshot =
                    telemetry.CaptureSnapshot();
                Assert.That(snapshot.Parts, Has.Count.EqualTo(16));
                Assert.That(snapshot.PriorityPower, Is.Empty);
                Assert.That(snapshot.RequestedTotalPower, Is.Zero);
                Assert.That(snapshot.MassKg, Is.EqualTo(11000f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static GameObject BuildCraft(
            string buildPath,
            bool installReferenceControllers,
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(buildPath);
            Assert.That(build, Is.Not.Null, buildPath);

            var host = new GameObject("V3 Gate 10 Telemetry Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var data = new SerializedObject(assembler);
            data.FindProperty("build").objectReferenceValue = build;
            data.FindProperty("installReferenceControllers").boolValue =
                installReferenceControllers;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }
    }
}
