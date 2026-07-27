using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3OverloadProtectionTests
    {
        private const string BuildPath =
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_V2Reference.asset";
        private const string MainThrusterPath =
            "Assets/HovercraftV3/Prototype/Definitions/" +
            "V2Reference_MainThruster.asset";

        [Test]
        public void EmergencyOverload_RaisesOutputPowerAndHeatWithoutChangingNormal()
        {
            GameObject host = BuildReferenceCraft(out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                V3ThermalController thermal =
                    root.GetComponent<V3ThermalController>();
                RuntimeThrusterInstance main =
                    router.FindThruster("Propulsion.Rear.Center");

                pipeline.Tick(
                    new V3PilotCommand { Throttle = 1f },
                    1f);
                thermal.Tick(1f);

                Assert.That(main.CurrentOutput, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(main.RequestedPower, Is.EqualTo(720f).Within(0.001f));
                Assert.That(
                    main.GeneratedHeatPerSecond,
                    Is.EqualTo(25f).Within(0.001f));
                Assert.That(main.IsEmergencyOverloadActive, Is.False);

                pipeline.ResetDynamicState();
                pipeline.Tick(
                    new V3PilotCommand
                    {
                        Throttle = 1f,
                        EmergencyOverload = true
                    },
                    1f);
                thermal.Tick(1f);

                Assert.That(router.EmergencyOverloadRequested, Is.True);
                Assert.That(main.CurrentOutput, Is.EqualTo(1.875f).Within(0.0001f));
                Assert.That(main.CurrentAppliedForceN, Is.EqualTo(900000f).Within(1f));
                Assert.That(main.RequestedPower, Is.EqualTo(1116f).Within(0.001f));
                Assert.That(
                    main.GeneratedHeatPerSecond,
                    Is.EqualTo(55f).Within(0.001f));
                Assert.That(main.IsEmergencyOverloadActive, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ThermalProtection_DeratesLocksOutCoolsAndRestarts()
        {
            ThrusterDefinition source =
                AssetDatabase.LoadAssetAtPath<ThrusterDefinition>(
                    MainThrusterPath);
            ThrusterDefinition definition = Object.Instantiate(source);
            var serialized = new SerializedObject(definition);
            SerializedProperty thermal = serialized.FindProperty("thermal");
            thermal.FindPropertyRelative("maximumSafeTemperatureC").floatValue =
                20.1f;
            thermal.FindPropertyRelative("overheatTemperatureC").floatValue =
                20.4f;
            thermal.FindPropertyRelative("restartTemperatureC").floatValue =
                20.06f;
            thermal.FindPropertyRelative("hotOutputLimitAtOverheat").floatValue =
                0.5f;
            thermal.FindPropertyRelative("coolingLockoutSeconds").floatValue =
                2f;
            thermal.FindPropertyRelative("thermalCapacity").floatValue = 100f;
            thermal.FindPropertyRelative("passiveCoolingPerSecond").floatValue =
                10f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var partObject = new GameObject("Overload Protection Test Part");
            try
            {
                RuntimeThrusterInstance part =
                    partObject.AddComponent<RuntimeThrusterInstance>();
                part.Initialize(definition, "Test.Main");

                part.SetRuntimeState(1.5f, 1.5f, 720f, 720f);
                part.TickThermal(1f);
                Assert.That(
                    part.ThermalProtectionState,
                    Is.EqualTo(V3ThermalProtectionState.Warning));
                Assert.That(part.ThermalOutputLimit, Is.InRange(0.5f, 1f));

                part.PreparePowerRequest(1.5f, 1f);
                Assert.That(part.RequestedOutput, Is.LessThan(1.5f));
                Assert.That(part.RequestedOutput, Is.GreaterThan(0f));

                part.SetRuntimeState(1.5f, 1.5f, 720f, 720f);
                part.TickThermal(1f);
                Assert.That(part.IsThermallyLockedOut, Is.True);
                Assert.That(
                    part.ThermalProtectionState,
                    Is.EqualTo(V3ThermalProtectionState.Overheated));
                Assert.That(part.PreparePowerRequest(1.5f, 1f), Is.Zero);
                Assert.That(part.RequestedOutput, Is.EqualTo(1.5f));
                Assert.That(part.CurrentOutput, Is.Zero);

                for (int i = 0; i < 100 && part.IsThermallyLockedOut; i++)
                {
                    part.SetRuntimeState(0f, 0f, 0f, 0f);
                    part.TickThermal(1f);
                }

                Assert.That(part.IsThermallyLockedOut, Is.False);
                Assert.That(
                    part.ThermalProtectionState,
                    Is.EqualTo(V3ThermalProtectionState.Recovered));
                Assert.That(
                    part.CurrentTemperatureC,
                    Is.LessThanOrEqualTo(definition.Thermal.restartTemperatureC));
                Assert.That(
                    part.PreparePowerRequest(1.5f, 1f),
                    Is.GreaterThan(0f));
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
            var host = new GameObject("V3 Overload Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }
    }
}
