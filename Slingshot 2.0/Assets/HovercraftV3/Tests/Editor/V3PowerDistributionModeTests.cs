using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3PowerDistributionModeTests
    {
        private const string BuildPath =
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_V2Reference.asset";

        [Test]
        public void AllocationModes_ExposeStableDataDrivenOrders()
        {
            var host = new GameObject("V3 Power Mode Order Test");
            try
            {
                V3PowerDistributor power =
                    host.AddComponent<V3PowerDistributor>();

                AssertOrder(
                    power.GetAllocationOrder(),
                    V3ActuatorPriority.Critical,
                    V3ActuatorPriority.Stabilization,
                    V3ActuatorPriority.Vectoring,
                    V3ActuatorPriority.Drive,
                    V3ActuatorPriority.Optional);

                power.CycleAllocationMode();
                Assert.That(
                    power.AllocationMode,
                    Is.EqualTo(V3PowerAllocationMode.Propulsion));
                AssertOrder(
                    power.GetAllocationOrder(),
                    V3ActuatorPriority.Critical,
                    V3ActuatorPriority.Drive,
                    V3ActuatorPriority.Vectoring,
                    V3ActuatorPriority.Stabilization,
                    V3ActuatorPriority.Optional);

                power.SetAllocationMode(V3PowerAllocationMode.Stability);
                AssertOrder(
                    power.GetAllocationOrder(),
                    V3ActuatorPriority.Stabilization,
                    V3ActuatorPriority.Critical,
                    V3ActuatorPriority.Vectoring,
                    V3ActuatorPriority.Drive,
                    V3ActuatorPriority.Optional);

                power.SetAllocationMode(V3PowerAllocationMode.Recovery);
                AssertOrder(
                    power.GetAllocationOrder(),
                    V3ActuatorPriority.Critical,
                    V3ActuatorPriority.Stabilization,
                    V3ActuatorPriority.Drive,
                    V3ActuatorPriority.Vectoring,
                    V3ActuatorPriority.Optional);

                power.CycleAllocationMode();
                Assert.That(
                    power.AllocationMode,
                    Is.EqualTo(V3PowerAllocationMode.Balanced));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PilotPowerModeRequest_IsConsumedExactlyOnce()
        {
            var host = new GameObject("V3 Power Mode Input Queue Test");
            try
            {
                V3PilotInputAdapter input =
                    host.AddComponent<V3PilotInputAdapter>();
                input.RequestPowerModeCycle();

                Assert.That(
                    input.ReadCommand().CyclePowerAllocationMode,
                    Is.True);
                Assert.That(
                    input.ReadCommand().CyclePowerAllocationMode,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PowerModeDescriptions_ExplainPriorityAndHeadroom()
        {
            Assert.That(
                V3PowerModeInfo.GetShortDescription(
                    V3PowerAllocationMode.Balanced),
                Does.Contain("predictable"));
            Assert.That(
                V3PowerModeInfo.GetShortDescription(
                    V3PowerAllocationMode.Propulsion),
                Does.Contain("Drive"));
            Assert.That(
                V3PowerModeInfo.GetShortDescription(
                    V3PowerAllocationMode.Stability),
                Does.Contain("stabilization"));
            Assert.That(
                V3PowerModeInfo.GetShortDescription(
                    V3PowerAllocationMode.Recovery),
                Does.Contain("hover"));
            Assert.That(
                V3PowerModeInfo.GetLimitationNote(false),
                Does.Contain("feel identical"));
        }

        [Test]
        public void ConstrainedCore_ShedsLoadsBySelectedModeAndReportsCockpitState()
        {
            CraftBuildDefinition constrainedBuild = null;
            EnergyCoreDefinition constrainedCore = null;
            GameObject host = null;
            try
            {
                constrainedBuild = CreateConstrainedBuild(
                    850f,
                    out constrainedCore);
                host = new GameObject("V3 Constrained Power Test");
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                var assemblerData = new SerializedObject(assembler);
                assemblerData.FindProperty("build").objectReferenceValue =
                    constrainedBuild;
                assemblerData.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(assembler.Rebuild(), Is.True);
                GameObject root = assembler.AssembledRoot;
                V3PowerDistributor power =
                    root.GetComponent<V3PowerDistributor>();
                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3CockpitWarningController warnings =
                    root.GetComponent<V3CockpitWarningController>();
                V3CockpitWarningDisplay display =
                    root.GetComponent<V3CockpitWarningDisplay>();

                Assert.That(
                    power.AllocationMode,
                    Is.EqualTo(V3PowerAllocationMode.Balanced));
                SubmitMixedLoad(router);
                Assert.That(
                    power.AvailablePropulsionPower,
                    Is.EqualTo(805f).Within(0.01f));
                AssertGrant(power, V3ActuatorPriority.Critical, 640f);
                AssertGrant(power, V3ActuatorPriority.Stabilization, 160f);
                AssertGrant(power, V3ActuatorPriority.Vectoring, 5f);
                AssertGrant(power, V3ActuatorPriority.Drive, 0f);

                warnings.Refresh();
                Assert.That(power.IsPowerLimited, Is.True);
                Assert.That(
                    warnings.AlertLevel,
                    Is.EqualTo(V3CockpitAlertLevel.Warning));
                Assert.That(warnings.AlertMessage, Does.Contain("BALANCED"));
                Assert.That(display.BuildDisplayText(), Does.Contain("POWER MODE"));

                pipeline.Tick(
                    new V3PilotCommand
                    {
                        CyclePowerAllocationMode = true
                    },
                    0.02f);
                Assert.That(
                    power.AllocationMode,
                    Is.EqualTo(V3PowerAllocationMode.Propulsion));
                SubmitMixedLoad(router);
                AssertGrant(power, V3ActuatorPriority.Critical, 640f);
                AssertGrant(power, V3ActuatorPriority.Drive, 165f);
                AssertGrant(power, V3ActuatorPriority.Vectoring, 0f);
                AssertGrant(power, V3ActuatorPriority.Stabilization, 0f);

                power.SetAllocationMode(V3PowerAllocationMode.Recovery);
                SubmitMixedLoad(router);
                AssertGrant(power, V3ActuatorPriority.Critical, 640f);
                AssertGrant(power, V3ActuatorPriority.Stabilization, 160f);
                AssertGrant(power, V3ActuatorPriority.Drive, 5f);
                AssertGrant(power, V3ActuatorPriority.Vectoring, 0f);

                SetCoreOutput(constrainedCore, 700f);
                power.SetAllocationMode(V3PowerAllocationMode.Stability);
                SubmitMixedLoad(router);
                Assert.That(
                    power.AvailablePropulsionPower,
                    Is.EqualTo(655f).Within(0.01f));
                AssertGrant(power, V3ActuatorPriority.Stabilization, 160f);
                AssertGrant(power, V3ActuatorPriority.Critical, 495f);
                AssertGrant(power, V3ActuatorPriority.Vectoring, 0f);
                AssertGrant(power, V3ActuatorPriority.Drive, 0f);
                Assert.That(
                    power.GrantedPropulsionPower,
                    Is.EqualTo(655f).Within(0.01f));
                Assert.That(power.ShedPropulsionPower, Is.GreaterThan(0f));
                Assert.That(power.PropulsionGrantFraction, Is.InRange(0f, 1f));
            }
            finally
            {
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }

                if (constrainedBuild != null)
                {
                    Object.DestroyImmediate(constrainedBuild);
                }

                if (constrainedCore != null)
                {
                    Object.DestroyImmediate(constrainedCore);
                }
            }
        }

        [Test]
        public void SharedThruster_PreservesEachCommandPriorityWithoutDuplicatingDemand()
        {
            CraftBuildDefinition constrainedBuild = null;
            EnergyCoreDefinition constrainedCore = null;
            GameObject host = null;
            try
            {
                constrainedBuild = CreateConstrainedBuild(
                    125f,
                    out constrainedCore);
                host = new GameObject("V3 Shared Thruster Priority Test");
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                var assemblerData = new SerializedObject(assembler);
                assemblerData.FindProperty("build").objectReferenceValue =
                    constrainedBuild;
                assemblerData.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(assembler.Rebuild(), Is.True);
                GameObject root = assembler.AssembledRoot;
                V3PowerDistributor power =
                    root.GetComponent<V3PowerDistributor>();
                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                RuntimeThrusterInstance thruster =
                    router.FindThruster("Hover.Front.Left.Bottom");

                Assert.That(thruster, Is.Not.Null);
                SubmitSharedThrusterLoad(router);
                Assert.That(
                    power.AvailablePropulsionPower,
                    Is.EqualTo(80f).Within(0.01f));
                AssertGrant(power, V3ActuatorPriority.Critical, 80f);
                AssertGrant(power, V3ActuatorPriority.Stabilization, 0f);
                Assert.That(
                    power.GetRequestedPower(V3ActuatorPriority.Critical),
                    Is.EqualTo(81.6f).Within(0.01f));
                Assert.That(
                    power.GetRequestedPower(V3ActuatorPriority.Stabilization),
                    Is.EqualTo(78.4f).Within(0.01f));
                Assert.That(
                    thruster.RequestedPower,
                    Is.EqualTo(160f).Within(0.01f));
                Assert.That(
                    thruster.GrantedPower,
                    Is.EqualTo(80f).Within(0.01f));

                power.SetAllocationMode(V3PowerAllocationMode.Stability);
                SubmitSharedThrusterLoad(router);
                AssertGrant(power, V3ActuatorPriority.Stabilization, 78.4f);
                AssertGrant(power, V3ActuatorPriority.Critical, 1.6f);
                Assert.That(
                    thruster.RequestedPower,
                    Is.EqualTo(160f).Within(0.01f));
                Assert.That(
                    thruster.GrantedPower,
                    Is.EqualTo(80f).Within(0.01f));
            }
            finally
            {
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }

                if (constrainedBuild != null)
                {
                    Object.DestroyImmediate(constrainedBuild);
                }

                if (constrainedCore != null)
                {
                    Object.DestroyImmediate(constrainedCore);
                }
            }
        }

        private static CraftBuildDefinition CreateConstrainedBuild(
            float continuousOutput,
            out EnergyCoreDefinition constrainedCore)
        {
            CraftBuildDefinition source =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(BuildPath);
            Assert.That(source, Is.Not.Null);
            EnergyCoreDefinition sourceCore =
                source.FindInstallation("Core.Main").Endpoint
                    as EnergyCoreDefinition;
            Assert.That(sourceCore, Is.Not.Null);

            constrainedCore = Object.Instantiate(sourceCore);
            constrainedCore.name = "V3 Constrained Test Core";
            SetCoreOutput(constrainedCore, continuousOutput);

            CraftBuildDefinition build =
                ScriptableObject.CreateInstance<CraftBuildDefinition>();
            build.name = "V3 Constrained Test Build";
            build.CopySelectionsFrom(source);
            build.FindInstallation("Core.Main").SetDirect(constrainedCore);

            var buildData = new SerializedObject(build);
            buildData.FindProperty("catalog").objectReferenceValue = null;
            buildData.ApplyModifiedPropertiesWithoutUndo();
            return build;
        }

        private static void SetCoreOutput(
            EnergyCoreDefinition core,
            float output)
        {
            var coreData = new SerializedObject(core);
            coreData.FindProperty("continuousOutput").floatValue = output;
            coreData.FindProperty("propulsionChannelCeiling").floatValue =
                output;
            coreData.FindProperty("systemsChannelCeiling").floatValue = output;
            coreData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SubmitMixedLoad(V3ActuatorCommandRouter router)
        {
            router.BeginFrame();
            string[] hoverSockets =
            {
                "Hover.Front.Left.Bottom",
                "Hover.Front.Right.Bottom",
                "Hover.Rear.Left.Bottom",
                "Hover.Rear.Right.Bottom"
            };
            for (int i = 0; i < hoverSockets.Length; i++)
            {
                Assert.That(
                    router.Submit(
                        hoverSockets[i],
                        V3ActuatorChannel.BaseHover,
                        1f,
                        V3ActuatorPriority.Critical),
                    Is.True);
            }

            Assert.That(
                router.Submit(
                    "Control.Front.Left.Top",
                    V3ActuatorChannel.Stabilization,
                    1f,
                    V3ActuatorPriority.Stabilization),
                Is.True);
            Assert.That(
                router.Submit(
                    "Propulsion.Rear.Center",
                    V3ActuatorChannel.Drive,
                    1f,
                    V3ActuatorPriority.Drive),
                Is.True);
            Assert.That(
                router.Submit(
                    "Strafe.Left.Front",
                    V3ActuatorChannel.Vectoring,
                    1f,
                    V3ActuatorPriority.Vectoring),
                Is.True);
            router.Resolve(1f);
        }

        private static void SubmitSharedThrusterLoad(
            V3ActuatorCommandRouter router)
        {
            const string socketId = "Hover.Front.Left.Bottom";
            router.BeginFrame();
            Assert.That(
                router.Submit(
                    socketId,
                    V3ActuatorChannel.BaseHover,
                    0.5f,
                    V3ActuatorPriority.Critical),
                Is.True);
            Assert.That(
                router.Submit(
                    socketId,
                    V3ActuatorChannel.Stabilization,
                    0.5f,
                    V3ActuatorPriority.Stabilization),
                Is.True);
            router.Resolve(1f);
        }

        private static void AssertGrant(
            V3PowerDistributor power,
            V3ActuatorPriority priority,
            float expected)
        {
            Assert.That(
                power.GetGrantedPower(priority),
                Is.EqualTo(expected).Within(0.01f),
                priority.ToString());
        }

        private static void AssertOrder(
            IReadOnlyList<V3ActuatorPriority> actual,
            params V3ActuatorPriority[] expected)
        {
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
