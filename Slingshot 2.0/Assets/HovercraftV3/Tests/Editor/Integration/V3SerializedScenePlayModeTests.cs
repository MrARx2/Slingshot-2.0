using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3SerializedScenePlayModeTests
    {
        private const string SystemsBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/" +
            "ApexV3_SystemsIntegration.asset";
        private const string CraftLabPath =
            "Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity";
        private const string HandlingTrackPath =
            "Assets/HovercraftV3/Scenes/Development/" +
            "V3_HandlingTrack.unity";

        [UnityTest]
        public IEnumerator CraftLab_SerializedPlayModeTopologyInputAndRebuild()
        {
            return VerifySerializedScene(CraftLabPath, true, false);
        }

        [UnityTest]
        public IEnumerator HandlingTrack_SerializedPlayModeTopologyAndInput()
        {
            return VerifySerializedScene(HandlingTrackPath, false, true);
        }

        private static IEnumerator VerifySerializedScene(
            string scenePath,
            bool verifyRebuild,
            bool verifyDiagnostics)
        {
            EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Single);

            yield return new EnterPlayMode();
            yield return null;

            CraftBuildDefinition expectedBuild =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SystemsBuildPath);
            Assert.That(expectedBuild, Is.Not.Null);
            V3CraftTestSpawner spawner =
                Object.FindAnyObjectByType<V3CraftTestSpawner>(
                    FindObjectsInactive.Include);
            Assert.That(spawner, Is.Not.Null, scenePath);
            Assert.That(
                spawner.SelectedBuild,
                Is.SameAs(expectedBuild),
                scenePath);
            Assert.That(
                Object.FindObjectsByType<V3CraftTestSpawner>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1),
                scenePath);
            Assert.That(
                Object.FindObjectsByType<V3PilotInputAdapter>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1),
                scenePath);
            Lunarlight.Hovercraft.V3.Diagnostics.V3DiagnosticRecorder recorder =
                Object.FindAnyObjectByType<
                    Lunarlight.Hovercraft.V3.Diagnostics.V3DiagnosticRecorder>(
                    FindObjectsInactive.Include);
            Assert.That(recorder, Is.Not.Null, scenePath);

            for (int i = 0;
                 i < 120 && spawner.CurrentCraft == null;
                 i++)
            {
                yield return null;
            }

            GameObject craft = spawner.CurrentCraft;
            Assert.That(craft, Is.Not.Null, scenePath);
            yield return WaitUntilReady(craft, scenePath);
            AssertCompleteTopology(spawner, craft, scenePath);

            if (verifyDiagnostics)
            {
                yield return VerifyDiagnosticCapture(recorder, craft, scenePath);
            }

            if (verifyRebuild)
            {
                GameObject oldRoot = craft;
                Assert.That(spawner.Rebuild(), Is.True, scenePath);
                yield return null;
                craft = spawner.CurrentCraft;
                Assert.That(craft, Is.Not.Null, scenePath);
                Assert.That(craft, Is.Not.SameAs(oldRoot), scenePath);
                Assert.That(oldRoot == null, Is.True, scenePath);
                yield return WaitUntilReady(craft, scenePath);
                AssertCompleteTopology(spawner, craft, scenePath);
            }

            yield return ExerciseSceneOwnedWInput(
                spawner,
                craft,
                scenePath);
            yield return new ExitPlayMode();
        }

        private static IEnumerator VerifyDiagnosticCapture(
            Lunarlight.Hovercraft.V3.Diagnostics.V3DiagnosticRecorder recorder,
            GameObject craft,
            string scenePath)
        {
            string outputRoot = System.IO.Path.Combine(
                Application.temporaryCachePath,
                "V3SerializedDiagnostic_" + System.Guid.NewGuid().ToString("N"));
            var serialized = new SerializedObject(recorder);
            serialized.FindProperty("maximumDurationSeconds").floatValue = 2f;
            serialized.FindProperty("outputRoot").stringValue = outputRoot;
            serialized.FindProperty("autoExportOnStop").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(recorder.StartRecording(), Is.True, scenePath);
            // EditMode UnityTests that temporarily enter Play Mode do not map one
            // yielded WaitForFixedUpdate to one simulated physics step. Wait on
            // the recorder's canonical sample identity instead of assuming that
            // editor-coroutine scheduling is the physics clock.
            for (int i = 0;
                 i < 300 && recorder.CurrentSession.SampleCount < 10;
                 i++)
            {
                yield return new WaitForSecondsRealtime(0.02f);
            }
            Assert.That(recorder.MarkManualEvent("Serialized benchmark marker"),
                Is.True, scenePath);
            recorder.StopRecording(false);
            Assert.That(recorder.CurrentSession, Is.Not.Null, scenePath);
            Assert.That(recorder.CurrentSession.SampleCount,
                Is.GreaterThanOrEqualTo(10), scenePath);
            Lunarlight.Hovercraft.V3.Diagnostics.V3DiagnosticSample last =
                recorder.CurrentSession.Samples[
                    recorder.CurrentSession.SampleCount - 1];
            Assert.That(last.identity.craftBuildStableId,
                Is.EqualTo(craft.GetComponent<V3CraftRuntime>().Build.StableId),
                scenePath);
            Assert.That(last.deviceCount, Is.GreaterThan(0), scenePath);
            Assert.That(last.taskCount, Is.GreaterThan(0), scenePath);
            Assert.That(recorder.ExportNow(), Is.True,
                recorder.LastExport != null ? recorder.LastExport.error : scenePath);
            for (int i = 0; i < 1500 && recorder.IsExporting; i++)
                yield return null;
            Assert.That(recorder.IsExporting, Is.False,
                "Incremental diagnostics export did not finish within 1500 frames.");
            Assert.That(recorder.LastExport.success, Is.True,
                recorder.LastExport.error);
            string output = recorder.LastExport.outputDirectory;
            Assert.That(System.IO.File.Exists(
                System.IO.Path.Combine(output, "session_manifest.json")), Is.True);
            Assert.That(System.IO.File.Exists(
                System.IO.Path.Combine(output, "report.md")), Is.True);
            Assert.That(System.IO.File.Exists(
                System.IO.Path.Combine(output, "samples_compact.bin")), Is.True);
            if (System.IO.Directory.Exists(outputRoot))
                System.IO.Directory.Delete(outputRoot, true);
        }

        private static IEnumerator WaitUntilReady(
            GameObject craft,
            string scenePath)
        {
            V3CraftMainframe mainframe =
                craft.GetComponent<V3CraftMainframe>();
            Assert.That(mainframe, Is.Not.Null, scenePath);
            for (int i = 0;
                 i < 180 &&
                 mainframe.BootState != V3MainframeBootState.Ready;
                 i++)
            {
                yield return new WaitForSecondsRealtime(0.02f);
            }

            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Ready),
                mainframe.FaultReason + " | " +
                string.Join(
                    " | ",
                    mainframe.Scheduler.Tasks.Select(
                        task =>
                            task.TaskId +
                            " grant=" +
                            task.GrantedRateHz.ToString("0.0") +
                            " min=" +
                            task.Software.MinimumUsefulRateHz
                                .ToString("0.0") +
                            " runs=" +
                            task.ExecutionCount +
                            " bottleneck=" +
                            task.Bottleneck)));
        }

        private static void AssertCompleteTopology(
            V3CraftTestSpawner spawner,
            GameObject craft,
            string scenePath)
        {
            V3CraftMainframe mainframe =
                craft.GetComponent<V3CraftMainframe>();
            V3PilotInputAdapter input = spawner.InputSource;
            V3SystemsConsoleController console =
                Object.FindAnyObjectByType<
                    V3SystemsConsoleController>(
                    FindObjectsInactive.Include);

            Assert.That(mainframe, Is.Not.Null, scenePath);
            Assert.That(input, Is.Not.Null, scenePath);
            Assert.That(console, Is.Not.Null, scenePath);
            Assert.That(
                input.TargetCraft,
                Is.SameAs(craft),
                scenePath);
            Assert.That(
                input.Route,
                Is.EqualTo(V3InputRoute.MainframeIntentBus),
                scenePath);
            Assert.That(
                input.AuthorityState,
                Is.EqualTo(V3InputAuthorityState.Active),
                scenePath);
            Assert.That(
                console.BoundCraft,
                Is.SameAs(craft),
                scenePath);
            Assert.That(
                console.BoundInput,
                Is.SameAs(input),
                scenePath);
            Assert.That(console.PageCount, Is.EqualTo(10), scenePath);
            V3CraftRuntime runtime = craft.GetComponent<V3CraftRuntime>();
            V3HoverController hover = craft.GetComponent<V3HoverController>();
            Assert.That(runtime, Is.Not.Null, scenePath);
            Assert.That(hover, Is.Not.Null, scenePath);
            Assert.That(hover.Configuration,
                Is.SameAs(runtime.Build.HoverConfiguration), scenePath);
            Assert.That(hover.TargetHeight,
                Is.EqualTo(runtime.Build.HoverConfiguration.TargetHoverHeight),
                scenePath);
            Assert.That(hover.TargetHeight, Is.EqualTo(8f).Within(0.0001f),
                scenePath);

            V3DirectionalSensorDeviceRuntime[] sensors =
                craft.GetComponentsInChildren<
                    V3DirectionalSensorDeviceRuntime>(true);
            Assert.That(sensors, Has.Length.EqualTo(6), scenePath);
            Assert.That(
                sensors.All(
                    sensor =>
                        sensor.Definition != null &&
                        sensor.Firmware != null),
                Is.True,
                scenePath);
            Assert.That(
                mainframe.Sensors,
                Has.Count.EqualTo(6),
                scenePath);

            V3SystemComputerRuntime[] computers =
                craft.GetComponentsInChildren<
                    V3SystemComputerRuntime>(true);
            Assert.That(computers, Has.Length.EqualTo(7), scenePath);
            Assert.That(
                mainframe.Computers,
                Has.Count.EqualTo(7),
                scenePath);
            Assert.That(
                mainframe.SystemRuntimes,
                Has.Count.EqualTo(7),
                scenePath);
            Assert.That(
                mainframe.Scheduler.Tasks,
                Has.Count.EqualTo(7),
                scenePath);
            Assert.That(
                mainframe.Scheduler.Tasks.All(
                    task =>
                        task.HasExecutableCallback &&
                        task.ExecutionCount > 0),
                Is.True,
                scenePath);
            Assert.That(
                craft.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(1),
                scenePath);
            Assert.That(
                mainframe.BootState,
                Is.EqualTo(V3MainframeBootState.Ready),
                scenePath);
        }

        private static IEnumerator ExerciseSceneOwnedWInput(
            V3CraftTestSpawner spawner,
            GameObject craft,
            string scenePath)
        {
            V3PilotInputAdapter input = spawner.InputSource;
            V3CraftMainframe mainframe =
                craft.GetComponent<V3CraftMainframe>();
            V3ActuatorCommandRouter actuatorRouter =
                craft.GetComponent<V3ActuatorCommandRouter>();
            RuntimeThrusterInstance mainThruster =
                actuatorRouter.FindThruster(
                    "Propulsion.Rear.Center");
            Rigidbody body = craft.GetComponent<Rigidbody>();
            V3ScheduledSoftwareTask pilotTask =
                mainframe.Scheduler.Tasks.First(
                    task =>
                        task.Software.Role ==
                        V3SystemComputerRole.PilotInterface);
            V3ScheduledSoftwareTask driveTask =
                mainframe.Scheduler.Tasks.First(
                    task =>
                        task.Software.Role ==
                        V3SystemComputerRole.Drive);

            Assert.That(mainThruster, Is.Not.Null, scenePath);
            input.SetUiSuspended(false);
            input.SetCursorCaptured(true);
            InputSettings.BackgroundBehavior previousBackground =
                InputSystem.settings.backgroundBehavior;
            input.Actions.Disable();
            var inputFixture = new InputTestFixture();
            inputFixture.Setup();
            InputSystem.settings.backgroundBehavior =
                InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            input.Actions.devices =
                new InputDevice[] { keyboard };
            input.Actions.Enable();
            try
            {
                double publicationBefore =
                    input.LastIntentPublicationTime;
                long pilotExecutionsBefore =
                    pilotTask.ExecutionCount;
                long driveExecutionsBefore =
                    driveTask.ExecutionCount;
                Vector3 initialForward = craft.transform.forward;
                float initialForwardSpeed =
                    Vector3.Dot(
                        body.linearVelocity,
                        initialForward);

                InputSystem.QueueStateEvent(
                    keyboard,
                    new KeyboardState(Key.W));
                InputSystem.Update();
                Assert.That(keyboard.wKey.isPressed, Is.True);
                Assert.That(
                    input.FindAction("Throttle")
                        .ReadValue<float>(),
                    Is.GreaterThan(0.9f),
                    scenePath);

                for (int i = 0; i < 40; i++)
                {
                    yield return new WaitForSecondsRealtime(
                        0.02f);
                }

                Assert.That(
                    input.CurrentCommand.Throttle,
                    Is.GreaterThan(0.9f),
                    scenePath);
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.Active),
                    scenePath);
                Assert.That(
                    input.LastIntentPublicationTime,
                    Is.GreaterThan(publicationBefore),
                    scenePath);
                Assert.That(
                    mainframe.Intents.HasPilotIntent,
                    Is.True);
                Assert.That(
                    mainframe.Intents.LatestPilotIntent.Command
                        .Throttle,
                    Is.GreaterThan(0.9f),
                    scenePath);
                Assert.That(
                    pilotTask.ExecutionCount,
                    Is.GreaterThan(pilotExecutionsBefore),
                    scenePath);
                Assert.That(
                    pilotTask.HasPublishedOutput,
                    Is.True,
                    scenePath);
                Assert.That(
                    driveTask.ExecutionCount,
                    Is.GreaterThan(driveExecutionsBefore),
                    scenePath);
                Assert.That(
                    mainframe.ControlRouter.Requests.Any(
                        request =>
                            request.Domain ==
                                V3ControlDomain.Propulsion &&
                            request.RequestedState > 0.9f),
                    Is.True,
                    scenePath);
                Assert.That(
                    mainThruster.RequestedOutput,
                    Is.GreaterThan(0f),
                    scenePath);
                Assert.That(
                    mainThruster.GrantedPower,
                    Is.GreaterThan(0f),
                    scenePath);
                Assert.That(
                    mainThruster.CurrentOutput,
                    Is.GreaterThan(0f),
                    scenePath);
                Assert.That(
                    mainThruster.CurrentAppliedForceN,
                    Is.GreaterThan(0f),
                    scenePath);
                Assert.That(
                    Vector3.Dot(
                        body.linearVelocity,
                        initialForward),
                    Is.GreaterThan(
                        initialForwardSpeed + 0.05f),
                    scenePath);

                InputSystem.QueueStateEvent(
                    keyboard,
                    new KeyboardState());
                InputSystem.Update();
            }
            finally
            {
                input.Actions.Disable();
                if (keyboard.added)
                {
                    InputSystem.RemoveDevice(keyboard);
                }

                inputFixture.TearDown();
                input.Actions.devices = null;
                input.Actions.Enable();
                InputSystem.settings.backgroundBehavior =
                    previousBackground;
            }
        }
    }
}
