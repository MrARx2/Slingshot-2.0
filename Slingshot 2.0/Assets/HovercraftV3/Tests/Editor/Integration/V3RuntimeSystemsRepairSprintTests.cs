using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3RuntimeSystemsRepairSprintTests :
        InputTestFixture
    {
        private const string SystemsBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/" +
            "ApexV3_SystemsIntegration.asset";
        private const string V2BuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/" +
            "Builds/ApexV3_V2Reference.asset";
        private static readonly string[] ScenePaths =
        {
            "Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity",
            "Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity"
        };

        private readonly List<GameObject> cleanup =
            new List<GameObject>();

        [TearDown]
        public override void TearDown()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                {
                    Object.DestroyImmediate(cleanup[i]);
                }
            }

            cleanup.Clear();
            InputSystem.settings.backgroundBehavior = default;
            base.TearDown();
        }

        [Test]
        public void SerializedScenes_HaveOneAuthoritativeSystemsBuild()
        {
            CraftBuildDefinition systemsBuild =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SystemsBuildPath);
            Assert.That(systemsBuild, Is.Not.Null);

            for (int i = 0; i < ScenePaths.Length; i++)
            {
                Scene existing = SceneManager.GetSceneByPath(
                    ScenePaths[i]);
                bool openedByTest = !existing.isLoaded;
                Scene scene = openedByTest
                    ? EditorSceneManager.OpenScene(
                        ScenePaths[i],
                        OpenSceneMode.Additive)
                    : existing;
                try
                {
                    V3CraftTestSpawner[] spawners =
                        ComponentsInScene<V3CraftTestSpawner>(scene);
                    V3PilotInputAdapter[] inputs =
                        ComponentsInScene<V3PilotInputAdapter>(scene);
                    Assert.That(
                        spawners,
                        Has.Length.EqualTo(1),
                        ScenePaths[i]);
                    Assert.That(
                        inputs,
                        Has.Length.EqualTo(1),
                        ScenePaths[i]);
                    Assert.That(
                        spawners[0].SelectedBuild,
                        Is.SameAs(systemsBuild),
                        ScenePaths[i]);
                    Assert.That(
                        spawners[0].Assembler,
                        Is.Not.Null,
                        ScenePaths[i]);
                }
                finally
                {
                    if (openedByTest)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }

        [Test]
        public void RealKeyboardW_UsesBothSupportedInputRoutes()
        {
            InputSystem.settings.backgroundBehavior =
                InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var inputHost = Track(
                new GameObject("Real Keyboard Input Source"));
            V3PilotInputAdapter input =
                inputHost.AddComponent<V3PilotInputAdapter>();
            try
            {
                input.Actions.devices =
                    new InputDevice[] { keyboard };
                input.Actions.Enable();
                PressW(keyboard);
                AssertThrottleActionBound(input, keyboard);
                V3PilotCommand command = input.ReadCommand();
                Assert.That(command.Throttle, Is.GreaterThan(0.9f));

                V3CraftAssembler systems =
                    Assemble(SystemsBuildPath, "Systems Route Host");
                GameObject systemsRoot = systems.AssembledRoot;
                input.BindCraft(
                    systemsRoot.GetComponent<V3ControllerPipeline>(),
                    systemsRoot.GetComponent<V3CraftMainframe>());
                Assert.That(
                    input.Route,
                    Is.EqualTo(V3InputRoute.MainframeIntentBus));

                V3CraftAssembler legacy =
                    Assemble(V2BuildPath, "Legacy Route Host");
                GameObject legacyRoot = legacy.AssembledRoot;
                input.BindCraft(
                    legacyRoot.GetComponent<V3ControllerPipeline>(),
                    null);
                Assert.That(
                    input.Route,
                    Is.EqualTo(V3InputRoute.LegacyPipeline));
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.Active));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [TestCase(SystemsBuildPath)]
        [TestCase(V2BuildPath)]
        public void RealKeyboardW_AcceleratesPhysicalCraft(
            string buildPath)
        {
            SimulationMode previous = Physics.simulationMode;
            InputSystem.settings.backgroundBehavior =
                InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var inputHost = Track(
                new GameObject("Acceleration Input Source"));
            V3PilotInputAdapter input =
                inputHost.AddComponent<V3PilotInputAdapter>();
            try
            {
                input.Actions.devices =
                    new InputDevice[] { keyboard };
                input.Actions.Enable();
                Physics.simulationMode = SimulationMode.Script;
                V3CraftAssembler assembler =
                    Assemble(buildPath, "Acceleration Host");
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                body.useGravity = false;
                body.position = new Vector3(0f, 50f, 0f);
                body.linearVelocity = Vector3.zero;
                Physics.SyncTransforms();

                input.BindCraft(
                    root.GetComponent<V3ControllerPipeline>(),
                    root.GetComponent<V3CraftMainframe>());
                PressW(keyboard);
                AssertThrottleActionBound(input, keyboard);
                V3PilotCommand command = input.ReadCommand();
                Assert.That(
                    command.Throttle,
                    Is.GreaterThan(0.9f),
                    buildPath);
                for (int i = 0; i < 80; i++)
                {
                    root.GetComponent<V3ControllerPipeline>()
                        .Tick(command, 0.02f);
                    Physics.Simulate(0.02f);
                }

                float speed = body.linearVelocity.magnitude;
                Assert.That(
                    speed,
                    Is.GreaterThan(0.1f),
                    buildPath);
            }
            finally
            {
                Physics.simulationMode = previous;
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void Scheduler_GrantedCadenceControlsCallbackExecution()
        {
            V3CraftAssembler assembler =
                Assemble(SystemsBuildPath, "Scheduler Host");
            GameObject root = assembler.AssembledRoot;
            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();
            var command = new V3PilotCommand
            {
                Throttle = 0.75f,
                StabilizationEnabled = true
            };

            const float step = 0.02f;
            const int ticks = 100;
            for (int i = 0; i < ticks; i++)
            {
                pipeline.Tick(command, step);
            }

            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Ready));
            for (int i = 0;
                 i < mainframe.Scheduler.Tasks.Count;
                 i++)
            {
                V3ScheduledSoftwareTask task =
                    mainframe.Scheduler.Tasks[i];
                float expected =
                    task.GrantedRateHz * step * ticks;
                Assert.That(task.HasExecutableCallback, Is.True);
                Assert.That(task.ExecutionCount, Is.GreaterThan(0));
                Assert.That(
                    task.ExecutionCount,
                    Is.EqualTo(expected).Within(
                        Mathf.Max(3f, expected * 0.08f)),
                    task.TaskId);
                Assert.That(
                    task.SystemRuntime.CaptureSnapshot()
                        .ExecutionCount,
                    Is.EqualTo(task.ExecutionCount));
            }
        }

        [Test]
        public void FullDynamicReset_ClearsAuthorityAndNeutralizesFirstTick()
        {
            V3CraftAssembler assembler =
                Assemble(SystemsBuildPath, "Full Reset Host");
            GameObject root = assembler.AssembledRoot;
            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();
            var command = new V3PilotCommand
            {
                Throttle = 1f,
                Lift = 1f,
                StabilizationEnabled = true
            };
            for (int i = 0; i < 10; i++) pipeline.Tick(command, 0.02f);
            Assert.That(mainframe.Scheduler.Tasks.Any(
                task => task.ExecutionCount > 0), Is.True);

            V3CraftRuntime runtime = root.GetComponent<V3CraftRuntime>();
            runtime.ResetDynamicState(new V3DynamicResetContext(
                V3DynamicResetPhase.BeforePoseReset,
                "test reset"));
            runtime.ResetDynamicState(new V3DynamicResetContext(
                V3DynamicResetPhase.AfterPoseReset,
                "test reset"));

            Assert.That(mainframe.Scheduler.Tasks.All(
                task => task.ExecutionCount == 0), Is.True);
            pipeline.Tick(command, 0.02f);
            Assert.That(pipeline.ActiveControlPath,
                Is.EqualTo(V3ControlExecutionPath.None));
            Assert.That(runtime.InstalledParts
                .OfType<RuntimeThrusterInstance>()
                .All(thruster =>
                    Mathf.Abs(thruster.RequestedOutput) < 0.0001f &&
                    Mathf.Abs(thruster.CurrentOutput) < 0.0001f), Is.True);
        }

        [Test]
        public void CompleteBuild_DoesNotSilentlyUseLegacyFallback()
        {
            V3CraftAssembler assembler =
                Assemble(SystemsBuildPath, "No Fallback Host");
            GameObject root = assembler.AssembledRoot;
            V3ControllerPipeline pipeline =
                root.GetComponent<V3ControllerPipeline>();
            Object.DestroyImmediate(root.GetComponent<V3CraftMainframe>());
            pipeline.Tick(new V3PilotCommand { Throttle = 1f }, 0.02f);
            Assert.That(pipeline.ActiveControlPath,
                Is.EqualTo(V3ControlExecutionPath.FaultedNoFallback));

            V3CraftAssembler legacy =
                Assemble(V2BuildPath, "Explicit Legacy Host");
            V3ControllerPipeline legacyPipeline =
                legacy.AssembledRoot.GetComponent<V3ControllerPipeline>();
            legacyPipeline.Tick(
                new V3PilotCommand { Throttle = 1f }, 0.02f);
            Assert.That(legacyPipeline.ActiveControlPath,
                Is.EqualTo(V3ControlExecutionPath.LegacyFallback));
        }

        [Test]
        public void Readiness_FaultsWhenPhysicalSensorIsMissing()
        {
            V3CraftAssembler assembler =
                Assemble(SystemsBuildPath, "Missing Sensor Host");
            GameObject root = assembler.AssembledRoot;
            V3DirectionalSensorDeviceRuntime sensor =
                root.GetComponentInChildren<
                    V3DirectionalSensorDeviceRuntime>(true);
            Object.DestroyImmediate(sensor.gameObject);
            root.GetComponent<V3RuntimeDeviceRegistry>()
                .Initialize(root);

            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();
            bool initialized = mainframe.Initialize(
                root.GetComponent<V3CraftRuntime>(),
                root.GetComponent<V3PowerDistributor>());

            Assert.That(initialized, Is.False);
            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Faulted));
            Assert.That(
                mainframe.FaultReason,
                Does.Contain("Expected six physical sensors"));
        }

        [Test]
        public void Readiness_FaultsWhenSystemCallbackIsMissing()
        {
            V3CraftAssembler assembler =
                Assemble(SystemsBuildPath, "Missing Callback Host");
            GameObject root = assembler.AssembledRoot;
            V3DriveSystemRuntime drive =
                root.GetComponentInChildren<V3DriveSystemRuntime>(true);
            Object.DestroyImmediate(drive);

            V3CraftMainframe mainframe =
                root.GetComponent<V3CraftMainframe>();
            bool initialized = mainframe.Initialize(
                root.GetComponent<V3CraftRuntime>(),
                root.GetComponent<V3PowerDistributor>());

            Assert.That(initialized, Is.False);
            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Faulted));
            Assert.That(
                mainframe.FaultReason,
                Does.Contain("executable system runtimes"));
            Assert.That(
                mainframe.FaultReason,
                Does.Contain("executable callback"));
        }

        [Test]
        public void SensorSample_UsesLocalAirflowAndMovingSurfaceVelocity()
        {
            SimulationMode previous = Physics.simulationMode;
            var craft = Track(new GameObject("Sensor Craft"));
            var origin = Track(new GameObject("Sensor Origin"));
            var target = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube));
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Rigidbody craftBody = craft.AddComponent<Rigidbody>();
                craftBody.useGravity = false;
                craftBody.linearVelocity = Vector3.right * 5f;
                origin.transform.position = Vector3.zero;
                Rigidbody targetBody = target.AddComponent<Rigidbody>();
                targetBody.useGravity = false;
                targetBody.isKinematic = false;
                targetBody.linearVelocity = Vector3.right * 2f;
                target.transform.position = Vector3.forward * 5f;
                Physics.SyncTransforms();

                V3DirectionalSensorDefinition definition =
                    AssetDatabase.LoadAssetAtPath<
                        V3DirectionalSensorDefinition>(
                        "Assets/HovercraftV3/Generated/" +
                        "ReferenceCrafts/SystemsIntegration/" +
                        "Definitions/Sensor_Front.asset");
                var runtime = new V3DirectionalSensorRuntime();
                runtime.Initialize(definition, origin.transform);
                runtime.ApplyGrant(120f, 120f, 10f, 10f, "None");
                runtime.Tick(
                    new V3ObservationBus(),
                    craftBody,
                    0.02d,
                    0.02f,
                    Vector3.right * 10f,
                    1.225f,
                    20f);

                Assert.That(runtime.Snapshot.Hit, Is.True);
                Assert.That(
                    runtime.Snapshot.RelativePointVelocity.x,
                    Is.EqualTo(3f).Within(0.01f));
                Assert.That(
                    runtime.Snapshot.RelativeAirflow.x,
                    Is.EqualTo(5f).Within(0.01f));
            }
            finally
            {
                Physics.simulationMode = previous;
            }
        }

        private V3CraftAssembler Assemble(
            string buildPath,
            string name)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    buildPath);
            Assert.That(build, Is.Not.Null, buildPath);
            GameObject host = Track(new GameObject(name));
            V3CraftAssembler assembler =
                host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue =
                build;
            serialized.FindProperty("assembleOnStart").boolValue =
                false;
            serialized.FindProperty(
                "installReferenceControllers").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True, buildPath);
            return assembler;
        }

        private GameObject Track(GameObject value)
        {
            cleanup.Add(value);
            return value;
        }

        private static void PressW(Keyboard keyboard)
        {
            Assert.That(
                keyboard.enabled,
                Is.True);
            InputSystem.QueueStateEvent(
                keyboard,
                new KeyboardState(Key.W));
            InputSystem.Update();
            Assert.That(keyboard.wKey.isPressed, Is.True);
        }

        private static void AssertThrottleActionBound(
            V3PilotInputAdapter input,
            Keyboard keyboard)
        {
            InputAction action = input.FindAction("Throttle");
            Assert.That(action, Is.Not.Null);
            Assert.That(action.enabled, Is.True);
            Assert.That(
                action.controls.Any(
                    control => control.device == keyboard),
                Is.True,
                string.Join(
                    ", ",
                    action.controls.Select(
                        control =>
                            control.path +
                            " device=" +
                            control.device.deviceId)));
            Assert.That(
                keyboard.wKey.ReadValue(),
                Is.GreaterThan(0.9f));
        }

        private static T[] ComponentsInScene<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(
                    root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
