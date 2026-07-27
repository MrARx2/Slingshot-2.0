using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3ThermalFoundationTests
    {
        private const string BuildPath =
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_V2Reference.asset";
        private const string MainThrusterPath =
            "Assets/HovercraftV3/Prototype/Definitions/" +
            "V2Reference_MainThruster.asset";

        [Test]
        public void ThermalController_TracksEveryRelevantInstalledPart()
        {
            GameObject host = BuildReferenceCraft(out V3CraftAssembler assembler);
            try
            {
                V3ThermalController thermal =
                    assembler.AssembledRoot.GetComponent<V3ThermalController>();
                V3ThermalDebugDisplay display =
                    assembler.AssembledRoot.GetComponent<V3ThermalDebugDisplay>();

                Assert.That(thermal, Is.Not.Null);
                Assert.That(display, Is.Not.Null);
                Assert.That(thermal.Telemetry, Has.Count.EqualTo(16));
                Assert.That(thermal.MaximumTemperatureC, Is.EqualTo(20f));
                Assert.That(
                    display.BuildDebugText(),
                    Does.Contain("Propulsion.Rear.Center"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ThermalController_LoadedThrusterHeatsFasterThenCoolsUnloaded()
        {
            GameObject host = BuildReferenceCraft(out V3CraftAssembler assembler);
            try
            {
                V3ThermalController thermal =
                    assembler.AssembledRoot.GetComponent<V3ThermalController>();
                V3ActuatorCommandRouter router =
                    assembler.AssembledRoot.GetComponent<V3ActuatorCommandRouter>();
                RuntimeThrusterInstance loaded =
                    router.FindThruster("Propulsion.Rear.Center");
                RuntimeThrusterInstance idle =
                    router.FindThruster("Braking.Front.Center");

                loaded.SetRuntimeState(1.5f, 1.5f, 0f, 0f);
                idle.SetRuntimeState(0f, 0f, 0f, 0f);
                for (int i = 0; i < 120; i++)
                {
                    thermal.Tick(1f);
                }

                float loadedPeak = loaded.CurrentTemperatureC;
                Assert.That(loadedPeak, Is.GreaterThan(idle.CurrentTemperatureC + 5f));
                Assert.That(loaded.GeneratedHeatPerSecond, Is.EqualTo(25f));
                Assert.That(idle.GeneratedHeatPerSecond, Is.EqualTo(0.5f));

                loaded.SetRuntimeState(0f, 0f, 0f, 0f);
                for (int i = 0; i < 60; i++)
                {
                    thermal.Tick(1f);
                }

                Assert.That(loaded.CurrentTemperatureC, Is.LessThan(loadedPeak));
                Assert.That(loaded.PassiveCoolingPerSecond, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void EnergyCore_ReportsCraftPowerUtilizationAndLoadHeat()
        {
            GameObject host = BuildReferenceCraft(
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3CraftRuntime runtime = root.GetComponent<V3CraftRuntime>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3PowerDistributor power =
                    root.GetComponent<V3PowerDistributor>();
                V3ThermalController thermal =
                    root.GetComponent<V3ThermalController>();
                RuntimePartInstance core = null;
                for (int i = 0; i < runtime.InstalledParts.Count; i++)
                {
                    if (runtime.InstalledParts[i].Definition
                        is EnergyCoreDefinition)
                    {
                        core = runtime.InstalledParts[i];
                        break;
                    }
                }

                Assert.That(core, Is.Not.Null);
                pipeline.Tick(
                    new V3PilotCommand
                    {
                        Throttle = 1f,
                        StabilizationEnabled = true
                    },
                    1f);

                Assert.That(core.CurrentOutput, Is.GreaterThan(0f));
                Assert.That(
                    core.RequestedPower,
                    Is.EqualTo(
                        power.RequestedSystemsPower +
                        power.RequestedPropulsionPower).Within(0.01f));
                Assert.That(
                    core.GrantedPower,
                    Is.EqualTo(
                        power.GrantedSystemsPower +
                        power.GrantedPropulsionPower).Within(0.01f));

                thermal.Tick(1f);
                Assert.That(
                    core.GeneratedHeatPerSecond,
                    Is.GreaterThan(
                        core.Definition.Thermal.idleHeatPerSecond));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ThermalModel_OverloadOutputUsesReservedHeatMultiplier()
        {
            ThrusterDefinition definition =
                AssetDatabase.LoadAssetAtPath<ThrusterDefinition>(
                    MainThrusterPath);
            ThermalProfile thermal = definition.Thermal;

            float normal =
                RuntimePartInstance.CalculateGeneratedHeatPerSecond(
                    thermal,
                    definition.NormalOutputMultiplier,
                    definition.NormalOutputMultiplier,
                    definition.OverloadOutputMultiplier);
            float overloaded =
                RuntimePartInstance.CalculateGeneratedHeatPerSecond(
                    thermal,
                    definition.OverloadOutputMultiplier,
                    definition.NormalOutputMultiplier,
                    definition.OverloadOutputMultiplier);

            Assert.That(normal, Is.EqualTo(25f).Within(0.0001f));
            Assert.That(overloaded, Is.EqualTo(55f).Within(0.0001f));
        }

        [Test]
        public void ThrusterDebugColor_ProgressesFromCoolToSafeToOverheat()
        {
            ThrusterDefinition definition =
                AssetDatabase.LoadAssetAtPath<ThrusterDefinition>(
                    MainThrusterPath);
            ThermalProfile thermal = definition.Thermal;

            Color cool = V3ThrusterDebugView.EvaluateTemperatureColor(
                thermal.ambientTemperatureC,
                thermal);
            Color safe = V3ThrusterDebugView.EvaluateTemperatureColor(
                thermal.maximumSafeTemperatureC,
                thermal);
            Color overheated = V3ThrusterDebugView.EvaluateTemperatureColor(
                thermal.overheatTemperatureC,
                thermal);

            Assert.That(cool.b, Is.GreaterThan(cool.r));
            Assert.That(safe.r, Is.GreaterThan(cool.r));
            Assert.That(overheated.r, Is.GreaterThanOrEqualTo(safe.r));
            Assert.That(overheated.g, Is.LessThan(safe.g));
        }

        [Test]
        public void OverheatThreshold_EntersProtectionWithoutChangingManualEnable()
        {
            ThrusterDefinition source =
                AssetDatabase.LoadAssetAtPath<ThrusterDefinition>(
                    MainThrusterPath);
            ThrusterDefinition definition = Object.Instantiate(source);
            var serialized = new SerializedObject(definition);
            SerializedProperty thermal = serialized.FindProperty("thermal");
            thermal.FindPropertyRelative("maximumSafeTemperatureC").floatValue =
                20.05f;
            thermal.FindPropertyRelative("overheatTemperatureC").floatValue =
                20.1f;
            thermal.FindPropertyRelative("restartTemperatureC").floatValue =
                19f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var partObject = new GameObject("Thermal Threshold Test Part");
            try
            {
                RuntimePartInstance part =
                    partObject.AddComponent<RuntimePartInstance>();
                part.Initialize(definition, "Test.Socket");
                part.SetRuntimeState(1f, 1f, 0f, 0f);
                part.TickThermal(1f);

                Assert.That(part.IsAboveSafeTemperature, Is.True);
                Assert.That(part.IsOverheated, Is.True);
                Assert.That(part.IsEnabled, Is.True);
                Assert.That(part.IsThermallyLockedOut, Is.True);
                Assert.That(
                    part.ThermalProtectionState,
                    Is.EqualTo(V3ThermalProtectionState.Overheated));
                Assert.That(
                    definition.Thermal.restartTemperatureC,
                    Is.EqualTo(19f));
            }
            finally
            {
                Object.DestroyImmediate(partObject);
                Object.DestroyImmediate(definition);
            }
        }

        private static GameObject BuildReferenceCraft(
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(BuildPath);
            var host = new GameObject("V3 Thermal Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }
    }
}
