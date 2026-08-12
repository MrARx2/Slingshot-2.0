using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Lunarlight.Hovercraft.V3.Diagnostics;
using Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests;
using Lunarlight.Hovercraft.V3.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3ControlledDynamicsTests
    {
        private const string PrimaryBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset";
        private const string BaselineBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/Hovercraft_Baseline.asset";
        private readonly List<UnityEngine.Object> cleanup =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) UnityEngine.Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test]
        public void Definition_ValidatesSchemaBuildCommandsAndSignals()
        {
            V3ControlledTestDefinition definition = CreateDefinition(
                "test.v3.definition.validation.01");
            var errors = new List<string>();
            Assert.That(definition.Validate(errors), Is.True,
                string.Join("\n", errors));

            V3PilotCommand command = definition.CommandAt(1.5f);
            Assert.That(command.Throttle, Is.EqualTo(0.5f));
            Assert.That(command.StabilizationEnabled, Is.True);
        }

        [Test]
        public void Registry_RejectsDuplicateStableTestIds()
        {
            V3ControlledTestDefinition a = CreateDefinition(
                "test.v3.registry.duplicate.01");
            V3ControlledTestDefinition b = CreateDefinition(
                "test.v3.registry.duplicate.01");
            var errors = new List<string>();
            Assert.That(V3ControlledTestRegistry.ValidateUnique(
                new[] { a, b }, errors), Is.False);
            Assert.That(errors, Has.Some.Contains("Duplicate controlled test ID"));
        }

        [Test]
        public void Runner_UsesExplicitCraftInsteadOfDefinitionDefault()
        {
            V3ControlledTestDefinition definition = CreateDefinition(
                "test.v3.explicit_craft.01");
            CraftBuildDefinition baseline = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(BaselineBuildPath);
            Assert.That(baseline, Is.Not.Null);
            Assert.That(baseline, Is.Not.SameAs(definition.Build));

            var host = new GameObject("Controlled Runner Selection");
            cleanup.Add(host);
            V3ControlledDynamicsTestRunner runner =
                host.AddComponent<V3ControlledDynamicsTestRunner>();
            runner.Configure(definition, baseline, false);

            Assert.That(runner.Definition, Is.SameAs(definition));
            Assert.That(runner.SelectedBuild, Is.SameAs(baseline));
            var errors = new List<string>();
            Assert.That(definition.ValidateForBuild(baseline, errors), Is.True,
                string.Join("\n", errors));
        }

        [Test]
        public void ReportCleanup_RejectsPathsOutsideItsReportBoundary()
        {
            Assert.That(V3ControlledReportCleanupUtility.TryMeasure(
                "Assets",
                false,
                out _, out _, out _, out string outsideError), Is.False);
            Assert.That(outsideError, Does.Contain("outside"));

            Assert.That(V3ControlledReportCleanupUtility.TryMeasure(
                "TestDriveReports",
                false,
                out _, out _, out _, out string rootError), Is.False);
            Assert.That(rootError, Does.Contain("Refusing"));

            Assert.That(V3ControlledReportCleanupUtility.TryMeasure(
                "TestDriveReports",
                true,
                out string fullPath, out int fileCount, out long byteCount,
                out string error),
                Is.True, error);
            Assert.That(Path.GetFileName(fullPath), Is.EqualTo("TestDriveReports"));
            Assert.That(fileCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(byteCount, Is.GreaterThanOrEqualTo(0L));
        }

        [Test]
        public void ControlledInput_UsesExclusiveOwnerAndNormalPilotCommand()
        {
            var inputObject = new GameObject("Controlled Pilot Input");
            var ownerA = new GameObject("Owner A");
            var ownerB = new GameObject("Owner B");
            cleanup.Add(inputObject);
            cleanup.Add(ownerA);
            cleanup.Add(ownerB);
            V3PilotInputAdapter adapter =
                inputObject.AddComponent<V3PilotInputAdapter>();
            Assert.That(adapter.BeginControlledInput(ownerA), Is.True);
            Assert.That(adapter.BeginControlledInput(ownerB), Is.False);
            Assert.That(adapter.SetControlledCommand(ownerA,
                new V3PilotCommand
                {
                    Throttle = 0.75f,
                    StabilizationEnabled = true
                }), Is.True);
            Assert.That(adapter.TryReadAuthoritativeCommand(
                out V3PilotCommand command), Is.True);
            Assert.That(command.Throttle, Is.EqualTo(0.75f));
            adapter.EndControlledInput(ownerB);
            Assert.That(adapter.HasControlledInput, Is.True);
            adapter.EndControlledInput(ownerA);
            Assert.That(adapter.HasControlledInput, Is.False);
        }

        [Test]
        public void Assertions_EvaluateNumericEventTransitionAndBuildIdentity()
        {
            V3ControlledTestDefinition definition = CreateDefinition(
                "test.v3.assertions.01",
                new[]
                {
                    new V3ControlledTestAssertion
                    {
                        assertionId = "range",
                        type = V3ControlledAssertionType.NumericRange,
                        signalId = "hover.captureAuthority",
                        minimum = 0f,
                        maximum = 1f
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "event",
                        type = V3ControlledAssertionType.EventOccurs,
                        eventType = V3DiagnosticEventType.FreeFlightEntered,
                        expectedEventCount = 1
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "transition",
                        type = V3ControlledAssertionType.StateTransition,
                        stateSequence = new[]
                        {
                            "NearSurfaceHover", "CaptureLimited", "FreeFlight"
                        }
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "identity",
                        type = V3ControlledAssertionType.BuildIdentity
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "target",
                        type = V3ControlledAssertionType.TargetTolerance,
                        signalId = "world.acceleration.y",
                        target = -9.8f,
                        tolerance = 0.1f
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "deadline",
                        type = V3ControlledAssertionType.Deadline,
                        signalId = "hover.captureAuthority",
                        maximum = 0.1f,
                        deadlineSeconds = 0.25f
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "residual",
                        type = V3ControlledAssertionType.ContactFreeResidual,
                        signalId = "forces.residual",
                        maximum = 2f
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "rate",
                        type = V3ControlledAssertionType.TaskRate,
                        signalId = "task.Hover.measuredRateHz",
                        target = 50f,
                        tolerance = 0.1f
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "absence",
                        type = V3ControlledAssertionType.EventAbsent,
                        eventType = V3DiagnosticEventType.ContactLanding
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "reaches",
                        type = V3ControlledAssertionType.SignalReachesMinimum,
                        signalId = "track.distance",
                        minimum = 20f
                    }
                });
            CraftBuildDefinition build = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(BaselineBuildPath);
            Assert.That(build, Is.Not.Null);
            Assert.That(build, Is.Not.SameAs(definition.Build));
            var session = new V3DiagnosticSession();
            session.Initialize(
                "controlled",
                "Assertions",
                V3DiagnosticRecordingProfile.ForensicCompact,
                3,
                1f,
                new V3DiagnosticCaptureCapacities(),
                new V3DiagnosticCraftSnapshot
                {
                    buildStableId = build.StableId,
                    buildAssetGuid = build.BuildAssetGuid,
                    buildOwnership = build.Ownership.ToString()
                },
                new V3DiagnosticWorldSnapshot { fixedDeltaTime = 0.01f });
            for (int i = 0; i < 3; i++)
            {
                Assert.That(session.TryBeginSample(out int index), Is.True);
                ref V3DiagnosticSample sample = ref session.GetSample(index);
                sample.identity.dynamicsValid = true;
                sample.identity.sessionElapsed = i * 0.1d;
                sample.identity.trackDistance = i * 10f;
                sample.belief.captureAuthorityMultiplier = 1f - i * 0.5f;
                sample.belief.surfaceState = i == 0 ? "NearSurfaceHover" :
                    i == 1 ? "CaptureLimited" : "FreeFlight";
                sample.world.linearAcceleration = new Vector3(0f, -9.8f, 0f);
                sample.forces.residualForce = Vector3.right;
                sample.taskCount = 1;
                sample.tasks[0] = new V3TaskDiagnosticRecord
                {
                    role = "Hover",
                    measuredRateHz = 50f
                };
            }
            Assert.That(session.TryAddEvent(new V3DiagnosticEvent
            {
                type = V3DiagnosticEventType.FreeFlightEntered
            }), Is.True);
            session.Complete(0.3d, 0d);

            V3ControlledAssertionResult[] results =
                V3ControlledAssertionEvaluator.Evaluate(
                    definition, session, build);
            Assert.That(results, Has.All.Matches<V3ControlledAssertionResult>(
                result => result.passed));
            Assert.That(results, Has.All.Matches<V3ControlledAssertionResult>(
                result => !string.IsNullOrEmpty(result.evidenceFile)));

            V3ControlledAssertionResult persistence =
                V3ControlledAssertionEvaluator.EvaluatePersistence(
                    new V3ControlledTestAssertion
                    {
                        assertionId = "persistence",
                        type = V3ControlledAssertionType.Persistence,
                        expectedText = "Variant values survive regeneration"
                    },
                    true,
                    "Variant A and B values reloaded unchanged",
                    "asset_persistence_report.md");
            Assert.That(persistence.passed, Is.True);
            Assert.That(persistence.evidenceFile,
                Is.EqualTo("asset_persistence_report.md"));
        }

        [Test]
        public void RuntimeOverride_AllowsGeneratedBuildForGlobalTestReuse()
        {
            V3ControlledTestDefinition definition = CreateDefinition(
                "test.v3.override.guard.01");
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("runtimeOverride")
                .FindPropertyRelative("disableChassisAerodynamics")
                .boolValue = true;
            serialized.FindProperty("runtimeOverride")
                .FindPropertyRelative("disableFinAerodynamics")
                .boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var errors = new List<string>();
            Assert.That(definition.Validate(errors), Is.True,
                string.Join("\n", errors));
        }

        [Test]
        public void TrackFollower_ClampsSteeringAndRegulatesSpeed()
        {
            Assert.That(V3ControlledTrackFollower.CalculateAxisCommand(
                15f, 30f, 0.8f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(V3ControlledTrackFollower.CalculateAxisCommand(
                -90f, 30f, 0.8f), Is.EqualTo(-0.8f).Within(0.0001f));

            var settings = new V3ControlledTrackFollowerSettings
            {
                minimumThrottle = -0.3f,
                maximumThrottle = 0.7f,
                feedForwardThrottle = 0.2f,
                speedProportionalGain = 0.01f
            };
            Assert.That(V3ControlledTrackFollower.CalculateThrottle(
                100f, 150f, settings), Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(V3ControlledTrackFollower.CalculateThrottle(
                180f, 150f, settings), Is.EqualTo(-0.1f).Within(0.0001f));
        }

        [Test]
        public void EventAssertions_RespectTheirConfiguredTimeWindow()
        {
            V3ControlledTestDefinition definition = CreateDefinition(
                "test.v3.event_window.01",
                new[]
                {
                    new V3ControlledTestAssertion
                    {
                        assertionId = "early",
                        type = V3ControlledAssertionType.EventOccurs,
                        eventType = V3DiagnosticEventType.SurfaceReacquired,
                        expectedEventCount = 1,
                        windowStartSeconds = 0f,
                        windowEndSeconds = 0.5f
                    },
                    new V3ControlledTestAssertion
                    {
                        assertionId = "late.absent",
                        type = V3ControlledAssertionType.EventAbsent,
                        eventType = V3DiagnosticEventType.SurfaceReacquired,
                        windowStartSeconds = 0.5f,
                        windowEndSeconds = 2f
                    }
                });
            var session = new V3DiagnosticSession();
            session.Initialize(
                "controlled",
                "Event Window",
                V3DiagnosticRecordingProfile.ForensicCompact,
                1,
                1f,
                new V3DiagnosticCaptureCapacities(),
                new V3DiagnosticCraftSnapshot(),
                new V3DiagnosticWorldSnapshot { fixedDeltaTime = 0.01f });
            Assert.That(session.TryAddEvent(new V3DiagnosticEvent
            {
                type = V3DiagnosticEventType.SurfaceReacquired,
                timestamp = 0.25d
            }), Is.True);
            session.Complete(1d, 0d);

            V3ControlledAssertionResult[] results =
                V3ControlledAssertionEvaluator.Evaluate(definition, session);
            Assert.That(results[0].passed, Is.True);
            Assert.That(results[1].passed, Is.True);
        }

        [Test]
        public void FailureController_UsesPartAvailabilityAndRestoresState()
        {
            CraftBuildDefinition build = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(PrimaryBuildPath);
            var host = new GameObject("Controlled Failure Host");
            cleanup.Add(host);
            V3CraftAssembler assembler = host.AddComponent<V3CraftAssembler>();
            assembler.SetBuild(build);
            Assert.That(assembler.Rebuild(), Is.True);
            V3CraftRuntime craft = assembler.AssembledRoot.GetComponent<
                V3CraftRuntime>();
            V3ControlledCraftFailureController controller =
                craft.gameObject.AddComponent<
                    V3ControlledCraftFailureController>();
            controller.Initialize(craft);

            Assert.That(controller.TryApplyControlledFailure(
                new V3ControlledFailurePathRequest
                {
                    targetId = "Propulsion.Rear.Center",
                    failureMode = V3ControlledFailureMode.DeviceUnavailable
                }, out string evidence), Is.True);
            RuntimePartInstance propulsion = FindPart(
                craft, "Propulsion.Rear.Center");
            Assert.That(propulsion, Is.Not.Null);
            Assert.That(propulsion.IsEnabled, Is.False);
            Assert.That(evidence, Does.Contain("part-availability"));

            Assert.That(controller.TryApplyControlledFailure(
                new V3ControlledFailurePathRequest
                {
                    targetId = "Drive",
                    failureMode = V3ControlledFailureMode.ComputerOffline
                }, out _), Is.True);
            V3ControllerPipeline pipeline = craft.GetComponent<
                V3ControllerPipeline>();
            pipeline.Tick(default, 0.1f);
            V3ScheduledSoftwareTask driveTask = null;
            IReadOnlyList<V3ScheduledSoftwareTask> tasks = craft.GetComponent<
                V3CraftMainframe>().Scheduler.Tasks;
            for (int i = 0; i < tasks.Count; i++)
                if (tasks[i].Software.Role == V3SystemComputerRole.Drive)
                    driveTask = tasks[i];
            Assert.That(driveTask, Is.Not.Null);
            Assert.That(driveTask.Bottleneck, Is.EqualTo("ComputerOffline"));
            Assert.That(driveTask.IsOperational, Is.False);

            controller.ClearControlledFailures();
            Assert.That(propulsion.IsEnabled, Is.True);
            Assert.That(driveTask.Computer.IsEnabled, Is.True);
        }

        [UnityTest]
        [Category("ControlledCapture")]
        public IEnumerator ScenarioA_RunsThroughSerializedTrackSceneAndExports()
        {
            if (Environment.GetEnvironmentVariable(
                "HOVERCRAFT_V3_RUN_CONTROLLED_CAPTURE") != "1")
            {
                Assert.Ignore("Set HOVERCRAFT_V3_RUN_CONTROLLED_CAPTURE=1 " +
                    "to run the real controlled Scenario A capture.");
            }

            const string definitionPath =
                "Assets/HovercraftV3/Content/ControlledTests/Definitions/" +
                "A_Ballistic.asset";
            V3ControlledTestDefinition definition =
                AssetDatabase.LoadAssetAtPath<V3ControlledTestDefinition>(
                    definitionPath);
            Assert.That(definition, Is.Not.Null);
            string scenePath = definition.ScenePath;
            Scene scene = EditorSceneManager.OpenScene(
                scenePath, OpenSceneMode.Single);
            var root = new GameObject("[V3 Controlled Dynamics Runner]");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<V3ControlledDynamicsTestRunner>()
                .Configure(definition, true);

            yield return new EnterPlayMode();
            V3ControlledDynamicsTestRunner runner =
                UnityEngine.Object.FindAnyObjectByType<
                    V3ControlledDynamicsTestRunner>();
            float deadline = Time.realtimeSinceStartup + 20f;
            while (runner != null && runner.LastResult == null &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(runner, Is.Not.Null);
            Assert.That(runner.LastResult, Is.Not.Null,
                "Controlled runner timed out before producing a result.");
            Assert.That(runner.LastResult.passed, Is.True,
                runner.LastResult.abortReason + " | " +
                string.Join(" | ", runner.LastResult.setupWarnings ??
                    Array.Empty<string>()));
            Assert.That(runner.LastResult.recorderOutputDirectory,
                Is.Not.Empty);
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(
                runner.LastResult.recorderOutputDirectory,
                "controlled_test_result.json")), Is.True);
            Debug.Log("Controlled Scenario A package: " +
                runner.LastResult.recorderOutputDirectory);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Category("ControlledCapture")]
        public IEnumerator SelectedScenario_RunsThroughSerializedSceneAndExports()
        {
            string definitionPath = Environment.GetEnvironmentVariable(
                "HOVERCRAFT_V3_CONTROLLED_DEFINITION");
            if (Environment.GetEnvironmentVariable(
                    "HOVERCRAFT_V3_RUN_CONTROLLED_CAPTURE") != "1" ||
                string.IsNullOrWhiteSpace(definitionPath))
            {
                Assert.Ignore("Set HOVERCRAFT_V3_RUN_CONTROLLED_CAPTURE=1 and " +
                    "HOVERCRAFT_V3_CONTROLLED_DEFINITION to run a selected capture.");
            }

            V3ControlledTestDefinition definition =
                AssetDatabase.LoadAssetAtPath<V3ControlledTestDefinition>(
                    definitionPath);
            Assert.That(definition, Is.Not.Null, definitionPath);
            float expectedDuration = definition.ExpectedDurationSeconds;
            Scene scene = EditorSceneManager.OpenScene(
                definition.ScenePath, OpenSceneMode.Single);
            var root = new GameObject("[V3 Controlled Dynamics Runner]");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<V3ControlledDynamicsTestRunner>()
                .Configure(definition, true);

            yield return new EnterPlayMode();
            V3ControlledDynamicsTestRunner runner =
                UnityEngine.Object.FindAnyObjectByType<
                    V3ControlledDynamicsTestRunner>();
            float runtimeExpectedDuration = runner != null &&
                runner.Definition != null
                    ? runner.Definition.ExpectedDurationSeconds
                    : expectedDuration;
            float deadline = Time.realtimeSinceStartup +
                runtimeExpectedDuration + 20f;
            while (runner != null && runner.LastResult == null &&
                Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(runner, Is.Not.Null);
            Assert.That(runner.LastResult, Is.Not.Null,
                "Controlled runner timed out before producing a result.");
            V3ControlledTestResult result = runner.LastResult;
            Assert.That(result.passed, Is.True,
                BuildFailureMessage(result));
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(
                result.recorderOutputDirectory,
                "controlled_test_result.json")), Is.True);
            Debug.Log("Controlled selected-scenario package: " +
                result.recorderOutputDirectory);
            yield return new ExitPlayMode();
        }

        private V3ControlledTestDefinition CreateDefinition(
            string id,
            V3ControlledTestAssertion[] assertions = null)
        {
            CraftBuildDefinition build = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(PrimaryBuildPath);
            Assert.That(build, Is.Not.Null);
            V3ControlledTestDefinition definition =
                ScriptableObject.CreateInstance<V3ControlledTestDefinition>();
            cleanup.Add(definition);
            definition.Configure(
                id,
                "Test Definition",
                V3ControlledTestCategory.PhysicsCore,
                build,
                "Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity",
                V3ControlledFixtureKind.None,
                new V3ControlledInitialCondition(),
                0.1f,
                2f,
                0.1f,
                new[]
                {
                    new V3ControlledCommandWindow
                    {
                        startSeconds = 1f,
                        endSeconds = 2f,
                        throttle = 0.5f,
                        stabilizationEnabled = true
                    }
                },
                assertions ?? new[]
                {
                    new V3ControlledTestAssertion
                    {
                        assertionId = "speed.range",
                        type = V3ControlledAssertionType.NumericRange,
                        signalId = "world.speed",
                        minimum = 0f,
                        maximum = 1000f
                    }
                },
                "comparison.tests");
            return definition;
        }

        private static RuntimePartInstance FindPart(
            V3CraftRuntime craft, string socketId)
        {
            for (int i = 0; i < craft.InstalledParts.Count; i++)
                if (craft.InstalledParts[i].ParentSocketId == socketId)
                    return craft.InstalledParts[i];
            return null;
        }

        private static string BuildFailureMessage(V3ControlledTestResult result)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(result.abortReason))
                lines.Add(result.abortReason);
            V3ControlledAssertionResult[] assertions = result.assertions ??
                Array.Empty<V3ControlledAssertionResult>();
            for (int i = 0; i < assertions.Length; i++)
                if (!assertions[i].passed)
                    lines.Add(assertions[i].assertionId + ": expected " +
                        assertions[i].expected + "; measured " +
                        assertions[i].measured);
            if (result.setupWarnings != null)
                lines.AddRange(result.setupWarnings);
            return string.Join(" | ", lines);
        }
    }
}
