using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TrackGeneration;
using TrackGeneration.Macro;
using TrackGeneration.Race;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests
{
    public interface IV3ControlledFailurePathTarget
    {
        bool TryApplyControlledFailure(
            V3ControlledFailurePathRequest request,
            out string evidence);
        void ClearControlledFailures();
    }

    [DefaultExecutionOrder(-1200)]
    [DisallowMultipleComponent]
    public sealed class V3ControlledDynamicsTestRunner : MonoBehaviour
    {
        private const float CommandSourceReadyTimeoutSeconds = 5f;

        [SerializeField] private V3ControlledTestDefinition definition;
        [SerializeField] private CraftBuildDefinition selectedBuild;
        [SerializeField] private bool runOnStart;
        [SerializeField] private V3CraftTestSpawner spawner;
        [SerializeField] private V3DiagnosticRecorder recorder;
        [SerializeField] private V3PilotInputAdapter pilotInput;

        private readonly List<string> setupWarnings = new List<string>(16);
        private readonly List<IV3ControlledFailurePathTarget> failureTargets =
            new List<IV3ControlledFailurePathTarget>(8);
        private readonly List<RuntimeAerodynamicFinInstance> disabledFins =
            new List<RuntimeAerodynamicFinInstance>(8);
        private Coroutine runRoutine;
        private GameObject fixtureRoot;
        private V3CraftRuntime craft;
        private Rigidbody body;
        private V3CraftAerodynamicsRuntime aerodynamics;
        private V3FreeDriveSession freeDriveSession;
        private V3ControlledCraftFailureController failureController;
        private GameObject controlledResetAnchor;
        private bool aerodynamicsWasEnabled;
        private V3ControlledTrackFollower trackFollower;
        private Vector3 resolvedInitialPosition;
        private Quaternion resolvedInitialRotation = Quaternion.identity;
        private readonly Stopwatch runnerOverhead = new Stopwatch();

        public V3ControlledTestDefinition Definition => definition;
        public CraftBuildDefinition SelectedBuild => selectedBuild != null
            ? selectedBuild
            : definition != null ? definition.Build : null;
        public V3ControlledTestPhase Phase { get; private set; } =
            V3ControlledTestPhase.Setup;
        public V3ControlledTestResult LastResult { get; private set; }
        public bool IsRunning => runRoutine != null;

        private void Start()
        {
            if (runOnStart) RunSelected();
        }

        private void OnDisable()
        {
            if (IsRunning) Abort("Runner disabled during an active test.");
        }

        public void Configure(V3ControlledTestDefinition value, bool autoRun)
        {
            Configure(value, value != null ? value.Build : null, autoRun);
        }

        public void Configure(
            V3ControlledTestDefinition value,
            CraftBuildDefinition craftBuild,
            bool autoRun)
        {
            if (IsRunning) throw new InvalidOperationException(
                "Cannot reconfigure an active controlled test.");
            definition = value;
            selectedBuild = craftBuild;
            runOnStart = autoRun;
        }

        [ContextMenu("Run Selected Controlled Test")]
        public bool RunSelected()
        {
            if (IsRunning || definition == null) return false;
            runRoutine = StartCoroutine(RunLifecycle());
            return true;
        }

        [ContextMenu("Abort Controlled Test")]
        public void AbortFromInspector()
        {
            Abort("Manual abort");
        }

        public void Abort(string reason)
        {
            if (runRoutine != null)
            {
                StopCoroutine(runRoutine);
                runRoutine = null;
            }
            SetPhase(V3ControlledTestPhase.Aborted);
            recorder?.StopRecording(false);
            CleanupRuntimeState();
            CompleteAbortedResult(reason ?? "Aborted");
        }

        private IEnumerator RunLifecycle()
        {
            string started = DateTime.UtcNow.ToString("O");
            setupWarnings.Clear();
            runnerOverhead.Reset();
            SetPhase(V3ControlledTestPhase.Setup);

            List<string> errors = new List<string>(16);
            if (!definition.ValidateForBuild(SelectedBuild, errors) ||
                !ResolveAndPrepare(errors))
            {
                string reason = string.Join(" | ", errors);
                runRoutine = null;
                SetPhase(V3ControlledTestPhase.Aborted);
                CleanupRuntimeState();
                CompleteAbortedResult(reason, started);
                yield break;
            }

            SetPhase(V3ControlledTestPhase.Warmup);
            pilotInput.SetControlledCommand(this, NeutralCommand());
            float warmup = 0f;
            while (warmup < definition.WarmupSeconds)
            {
                yield return new WaitForFixedUpdate();
                warmup += Time.fixedDeltaTime;
            }

            if (definition.CommandMode ==
                V3ControlledCommandMode.TrackFollower)
            {
                double waitStarted = Time.realtimeSinceStartupAsDouble;
                while (!IsGeneratedTrackReady() &&
                    Time.realtimeSinceStartupAsDouble - waitStarted <
                    CommandSourceReadyTimeoutSeconds)
                {
                    yield return null;
                }
            }

            if (!PrepareCommandSource(errors))
            {
                string reason = string.Join(" | ", errors);
                runRoutine = null;
                SetPhase(V3ControlledTestPhase.Aborted);
                CleanupRuntimeState();
                CompleteAbortedResult(reason, started);
                yield break;
            }

            ApplyInitialCondition("Controlled measured-phase boundary");
            ConfigureRecorder();
            SetPhase(V3ControlledTestPhase.Measured);
            if (!recorder.StartRecording())
            {
                runRoutine = null;
                SetPhase(V3ControlledTestPhase.Aborted);
                CleanupRuntimeState();
                CompleteAbortedResult(
                    "Recorder could not start the controlled capture.",
                    started);
                yield break;
            }
            recorder.MarkManualEvent("Controlled phase: Measured");

            float measured = 0f;
            bool measuredResetIssued = false;
            while (measured < definition.MeasuredSeconds)
            {
                runnerOverhead.Start();
                V3PilotCommand command = definition.CommandAt(measured);
                if (trackFollower != null)
                    command = trackFollower.BuildCommand();
                pilotInput.SetControlledCommand(this, command);
                V3ControlledMeasuredReset measuredReset =
                    definition.MeasuredReset;
                if (measuredReset.enabled &&
                    !measuredResetIssued &&
                    measured >= measuredReset.triggerSeconds)
                {
                    measuredResetIssued = true;
                    if (freeDriveSession == null ||
                        !freeDriveSession.ResetCraft(
                            V3CraftResetReason.TestAutomation,
                            measuredReset.reason))
                    {
                        runnerOverhead.Stop();
                        Abort("The authorized measured-phase reset failed.");
                        yield break;
                    }
                    recorder.MarkManualEvent(
                        "Controlled measured-phase reset: " +
                        measuredReset.reason);
                }
                runnerOverhead.Stop();
                yield return new WaitForFixedUpdate();
                measured += Time.fixedDeltaTime;
            }

            SetPhase(V3ControlledTestPhase.Cooldown);
            recorder.MarkManualEvent("Controlled phase: Cooldown");
            pilotInput.SetControlledCommand(this, NeutralCommand());
            float cooldown = 0f;
            while (cooldown < definition.CooldownSeconds)
            {
                yield return new WaitForFixedUpdate();
                cooldown += Time.fixedDeltaTime;
            }
            V3ControllerPipeline finalPipeline = craft != null
                ? craft.GetComponent<V3ControllerPipeline>()
                : null;
            if (finalPipeline != null)
                recorder.SetActualControlPath(finalPipeline.ActiveControlPath);
            recorder.StopRecording(false);

            V3ControlledAssertionResult[] assertionResults =
                V3ControlledAssertionEvaluator.Evaluate(
                    definition,
                    recorder.CurrentSession,
                    SelectedBuild);
            bool passed = AllPassed(assertionResults) &&
                VerifyFinalControlPath() &&
                recorder.CurrentSession.Manifest.droppedSampleCount == 0 &&
                recorder.CurrentSession.Manifest.droppedEventCount == 0;
            if (recorder.CurrentSession.Manifest.droppedSampleCount > 0 ||
                recorder.CurrentSession.Manifest.droppedEventCount > 0)
                setupWarnings.Add("Controlled evidence is incomplete: dropped " +
                    recorder.CurrentSession.Manifest.droppedSampleCount +
                    " samples and " +
                    recorder.CurrentSession.Manifest.droppedEventCount +
                    " events.");
            SetPhase(V3ControlledTestPhase.Complete);
            recorder.ExportNow();
            while (recorder.IsExporting) yield return null;

            LastResult = BuildResult(
                passed,
                string.Empty,
                started,
                assertionResults);
            WriteResultPackage(LastResult);
            CleanupRuntimeState();
            runRoutine = null;
        }

        private bool ResolveAndPrepare(List<string> errors)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.path, definition.ScenePath,
                StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Active scene is " + activeScene.path +
                    "; required scene is " + definition.ScenePath + ".");
                return false;
            }

            spawner ??= FindAnyObjectByType<V3CraftTestSpawner>(
                FindObjectsInactive.Include);
            recorder ??= FindAnyObjectByType<V3DiagnosticRecorder>(
                FindObjectsInactive.Include);
            pilotInput ??= FindAnyObjectByType<V3PilotInputAdapter>(
                FindObjectsInactive.Include);
            freeDriveSession ??= FindAnyObjectByType<V3FreeDriveSession>(
                FindObjectsInactive.Include);
            if (spawner == null) errors.Add("Scene has no V3CraftTestSpawner.");
            if (recorder == null) errors.Add("Scene has no V3DiagnosticRecorder.");
            if (pilotInput == null) errors.Add("Scene has no V3PilotInputAdapter.");
            if (errors.Count > 0) return false;

            V3WorldSimulationRoot[] worlds = FindObjectsByType<
                V3WorldSimulationRoot>(
                    FindObjectsInactive.Exclude);
            if (worlds.Length != 1)
                errors.Add("Controlled tests require exactly one active world root.");
            else
            {
                if (!string.IsNullOrEmpty(definition.RequiredWorldRootStableId) &&
                    worlds[0].StableRootId !=
                    definition.RequiredWorldRootStableId)
                    errors.Add("World-root stable ID mismatch: " +
                        worlds[0].StableRootId + ".");
                if (definition.WorldProfile != null &&
                    worlds[0].Profile != definition.WorldProfile)
                    errors.Add("World profile does not match the test definition.");
                if (worlds[0].Profile != null &&
                    Vector3.Distance(
                        worlds[0].Profile.GravityVector,
                        definition.ExpectedGravity) > 0.001f)
                    errors.Add("World gravity does not match the test definition.");
            }
            if (errors.Count > 0) return false;

            CraftBuildDefinition build = SelectedBuild;
            if (!spawner.SelectAndAssemble(build))
            {
                errors.Add("Selected build failed normal craft assembly: " +
                    spawner.LastError);
                return false;
            }
            GameObject craftObject = spawner.CurrentCraft;
            craft = craftObject != null
                ? craftObject.GetComponent<V3CraftRuntime>() : null;
            body = craft != null ? craft.RootRigidbody : null;
            if (craft == null || body == null)
            {
                errors.Add("Assembly did not produce a V3 craft Rigidbody.");
                return false;
            }
            if (craft.Build != build)
                errors.Add("Assembled build does not match the selected craft.");

            if (!ResolveInitialPose(errors))
                return false;
            ConfigureMeasuredResetAnchor();

            if (!pilotInput.BeginControlledInput(this))
                errors.Add("Pilot adapter rejected the exclusive controlled-input owner.");

            if (!CreateFixture(errors)) return false;
            if (!ApplyFailureRequests(errors)) return false;
            if (!ApplyRuntimeOverrides(errors)) return false;
            ApplyInitialCondition("Controlled setup");
            return errors.Count == 0;
        }

        private static bool IsGeneratedTrackReady()
        {
            TrackGenerator generator = FindAnyObjectByType<TrackGenerator>(
                FindObjectsInactive.Exclude);
            return generator != null &&
                generator.TrackRoot != null &&
                generator.CurrentMacroSections != null &&
                generator.CurrentMacroSections.Count > 0;
        }

        private bool PrepareCommandSource(List<string> errors)
        {
            if (definition.CommandMode != V3ControlledCommandMode.TrackFollower)
                return true;

            trackFollower = new V3ControlledTrackFollower();
            if (!trackFollower.TryInitialize(
                gameObject,
                body,
                SelectedBuild,
                definition.TrackFollower,
                out string error))
            {
                errors.Add(error);
                trackFollower.Dispose();
                trackFollower = null;
                return false;
            }
            setupWarnings.Add(
                "Controlled track follower owns steering, pitch, strafe, and speed commands.");
            return true;
        }

        private void ApplyInitialCondition(string reason)
        {
            V3ControlledInitialCondition initial = definition.InitialCondition;
            if (definition.ResetBeforeSetup)
            {
                craft.ResetDynamicState(new V3DynamicResetContext(
                    V3DynamicResetPhase.BeforePoseReset,
                    reason));
            }
            body.position = resolvedInitialPosition;
            body.rotation = resolvedInitialRotation;
            body.linearVelocity = initial.linearVelocity;
            body.angularVelocity = initial.angularVelocity;
            Physics.SyncTransforms();
            if (definition.ResetBeforeSetup)
            {
                craft.ResetDynamicState(new V3DynamicResetContext(
                    V3DynamicResetPhase.AfterPoseReset,
                    reason));
            }
            if (initial.startSleeping) body.Sleep();
            else body.WakeUp();
        }

        private bool ResolveInitialPose(List<string> errors)
        {
            V3ControlledInitialCondition initial = definition.InitialCondition;
            resolvedInitialPosition = initial.worldPosition;
            resolvedInitialRotation = Quaternion.Euler(
                initial.worldEulerAngles);
            if (!initial.useTrackRelation)
                return true;

            TrackGenerator generator = FindAnyObjectByType<TrackGenerator>(
                FindObjectsInactive.Exclude);
            if (generator == null)
            {
                errors.Add("Track-relative initial condition requires a TrackGenerator.");
                return false;
            }

            Vector3 position;
            Vector3 forward;
            Vector3 up;
            bool resolved = false;
            if (Mathf.Abs(initial.trackDistanceMeters) <= 0.001f &&
                generator.TrackStartSpawnPoint != null)
            {
                Transform spawn = generator.TrackStartSpawnPoint;
                position = spawn.position;
                forward = spawn.forward;
                up = spawn.up;
                resolved = true;
            }
            else if (MacroTrackSampler.TrySampleFrame(
                generator.CurrentMacroSections,
                initial.trackDistanceMeters,
                out TrackConnectionFrame frame))
            {
                Transform root = generator.TrackRoot != null
                    ? generator.TrackRoot
                    : generator.transform;
                up = root.TransformDirection(frame.Up).normalized;
                forward = root.TransformDirection(frame.Forward).normalized;
                float rideHeight = SelectedBuild != null
                    ? SelectedBuild.HoverConfiguration.TargetHoverHeight
                    : 0f;
                position = root.TransformPoint(frame.Position) +
                    up * rideHeight;
                resolved = true;
            }
            else
            {
                position = Vector3.zero;
                forward = Vector3.forward;
                up = Vector3.up;
            }

            if (!resolved)
            {
                errors.Add("Could not resolve track-relative initial condition at " +
                    initial.trackDistanceMeters.ToString("0.###") + " m.");
                return false;
            }
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
            if (forward.sqrMagnitude < 0.1f)
                forward = Vector3.forward;
            resolvedInitialPosition = position;
            resolvedInitialRotation = Quaternion.LookRotation(forward, up);
            setupWarnings.Add("Track-relative start resolved at " +
                initial.trackDistanceMeters.ToString("0.###") + " m.");
            return true;
        }

        private void ConfigureMeasuredResetAnchor()
        {
            if (!definition.MeasuredReset.enabled || freeDriveSession == null)
                return;
            controlledResetAnchor = new GameObject(
                "[V3 Controlled Reset Anchor]");
            controlledResetAnchor.transform.SetPositionAndRotation(
                resolvedInitialPosition,
                resolvedInitialRotation);
            freeDriveSession.Configure(
                spawner.Assembler,
                controlledResetAnchor.transform,
                pilotInput);
        }

        private bool CreateFixture(List<string> errors)
        {
            if (definition.FixturePrefab != null)
            {
                fixtureRoot = Instantiate(definition.FixturePrefab);
                fixtureRoot.name = "[V3 Controlled Test Fixture] " +
                    definition.StableTestId;
                return true;
            }
            if (definition.FixtureKind == V3ControlledFixtureKind.None ||
                definition.FixtureKind == V3ControlledFixtureKind.TrackTestScene)
                return true;
            if (definition.FixtureKind == V3ControlledFixtureKind.TorqueFixture)
            {
                errors.Add("Torque fixtures must use installed physical actuators; " +
                    "no direct AddTorque fixture is provided.");
                return false;
            }
            fixtureRoot = new GameObject(
                "[V3 Controlled Test Fixture] " + definition.StableTestId);
            if (definition.FixtureKind == V3ControlledFixtureKind.FlatSurface)
            {
                CreateSurface("Flat Surface", new Vector3(0f, -0.5f, 0f),
                    new Vector3(200f, 1f, 400f), Quaternion.identity);
            }
            else if (definition.FixtureKind ==
                V3ControlledFixtureKind.CleanSurfaceEdge)
            {
                CreateSurface("Clean Edge Surface", new Vector3(0f, -0.5f, -50f),
                    new Vector3(200f, 1f, 100f), Quaternion.identity);
            }
            else if (definition.FixtureKind ==
                V3ControlledFixtureKind.DescendingSurface)
            {
                CreateSurface("Descending Surface", new Vector3(0f, -5f, 25f),
                    new Vector3(200f, 1f, 120f),
                    Quaternion.Euler(10f, 0f, 0f));
            }
            return true;
        }

        private void CreateSurface(
            string objectName,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = objectName;
            surface.layer = 8;
            surface.transform.SetParent(fixtureRoot.transform, false);
            surface.transform.SetPositionAndRotation(position, rotation);
            surface.transform.localScale = scale;
        }

        private bool ApplyFailureRequests(List<string> errors)
        {
            failureTargets.Clear();
            failureController = craft.GetComponent<
                V3ControlledCraftFailureController>();
            if (failureController == null)
                failureController = craft.gameObject.AddComponent<
                    V3ControlledCraftFailureController>();
            failureController.Initialize(craft);
            MonoBehaviour[] behaviours = craft.GetComponentsInChildren<
                MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IV3ControlledFailurePathTarget target)
                    failureTargets.Add(target);
            }
            V3ControlledFailurePathRequest[] requests = definition.Failures;
            for (int i = 0; i < requests.Length; i++)
            {
                V3ControlledFailurePathRequest request = requests[i];
                if (request == null) continue;
                bool applied = false;
                for (int j = 0; j < failureTargets.Count; j++)
                {
                    if (failureTargets[j].TryApplyControlledFailure(
                        request,
                        out string evidence))
                    {
                        applied = true;
                        if (!string.IsNullOrEmpty(evidence))
                            setupWarnings.Add(evidence);
                        break;
                    }
                }
                if (!applied)
                {
                    string message = "No normal failure-path handler accepted " +
                        request.failureMode + " for " + request.targetId + ".";
                    if (request.critical) errors.Add(message);
                    else setupWarnings.Add(message);
                }
            }
            return errors.Count == 0;
        }

        private bool ApplyRuntimeOverrides(List<string> errors)
        {
            V3ControlledRuntimeOverride runtimeOverride =
                definition.RuntimeOverride;
            if (!runtimeOverride.HasAnyOverride)
                return true;
            if (runtimeOverride.warnWhenBuildIsNotTestOnly &&
                SelectedBuild.Ownership != V3BuildOwnership.TestOnly)
            {
                setupWarnings.Add(
                    "Transient aerodynamic override is being applied to selected " +
                    "non-TestOnly craft " + SelectedBuild.StableId +
                    "; the source asset is not modified.");
            }
            if (runtimeOverride.disableChassisAerodynamics)
            {
                aerodynamics = craft.GetComponent<V3CraftAerodynamicsRuntime>();
                if (aerodynamics == null)
                {
                    errors.Add("Chassis-aero override requested but no aero runtime exists.");
                    return false;
                }
                aerodynamicsWasEnabled = aerodynamics.ChassisAerodynamicsEnabled;
                aerodynamics.SetChassisAerodynamicsEnabled(false);
                setupWarnings.Add(
                    "Controlled runtime override disabled chassis aerodynamics.");
            }
            if (runtimeOverride.disableFinAerodynamics)
            {
                disabledFins.Clear();
                RuntimeAerodynamicFinInstance[] fins =
                    craft.GetComponentsInChildren<RuntimeAerodynamicFinInstance>(true);
                for (int i = 0; i < fins.Length; i++)
                {
                    RuntimeAerodynamicFinInstance fin = fins[i];
                    if (fin == null || !fin.IsEnabled) continue;
                    fin.SetEnabled(false);
                    disabledFins.Add(fin);
                }
                setupWarnings.Add(
                    "Controlled runtime override disabled " + disabledFins.Count +
                    " aerodynamic fin device(s).");
            }
            return true;
        }

        private void ConfigureRecorder()
        {
            string assertionSet = definition.StableTestId + ".assertions.v" +
                definition.Version;
            var metadata = new V3ControlledRecorderMetadata
            {
                testId = definition.StableTestId,
                testVersion = definition.Version,
                expectedBuildId = SelectedBuild.StableId,
                expectedBuildGuid = SelectedBuild.BuildAssetGuid,
                expectedOwnership = SelectedBuild.Ownership.ToString(),
                expectedControlPath = definition.RequiredControlPath.ToString(),
                initialConditionJson = JsonUtility.ToJson(
                    definition.InitialCondition),
                controlledFailuresJson = SerializeFailures(definition.Failures),
                expectedActiveSystems = "Pilot,Drive,Hover,Stabilizer,Traction,Aerodynamics,Telemetry",
                assertionSetId = assertionSet,
                comparisonGroupId = definition.Output.comparisonGroupId,
                calibrationDecisionId = definition.Output.calibrationDecisionId
            };
            string sessionName = definition.Output.sessionNameTemplate
                .Replace("{testId}", definition.StableTestId)
                .Replace("{utc}", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            recorder.ConfigureControlledCapture(
                metadata,
                definition.MeasuredSeconds + definition.CooldownSeconds + 1f,
                definition.Output.outputFolder,
                sessionName,
                definition.Output.rawDataEnabled);
            recorder.SetControlledTestPhase(Phase);
        }

        private bool VerifyFinalControlPath()
        {
            V3ControllerPipeline pipeline = craft != null
                ? craft.GetComponent<V3ControllerPipeline>() : null;
            if (pipeline == null) return false;
            bool matches = pipeline.ActiveControlPath ==
                definition.RequiredControlPath;
            if (!matches)
            {
                setupWarnings.Add("Final control path was " +
                    pipeline.ActiveControlPath + "; expected " +
                    definition.RequiredControlPath + ".");
            }
            return matches;
        }

        private void SetPhase(V3ControlledTestPhase phase)
        {
            Phase = phase;
            recorder?.SetControlledTestPhase(phase);
        }

        private void CleanupRuntimeState()
        {
            pilotInput?.EndControlledInput(this);
            trackFollower?.Dispose();
            trackFollower = null;
            for (int i = 0; i < failureTargets.Count; i++)
                failureTargets[i]?.ClearControlledFailures();
            failureTargets.Clear();
            if (failureController != null)
                Destroy(failureController);
            failureController = null;
            if (aerodynamics != null)
                aerodynamics.SetChassisAerodynamicsEnabled(
                    aerodynamicsWasEnabled);
            aerodynamics = null;
            for (int i = 0; i < disabledFins.Count; i++)
                if (disabledFins[i] != null) disabledFins[i].SetEnabled(true);
            disabledFins.Clear();
            if (fixtureRoot != null) Destroy(fixtureRoot);
            fixtureRoot = null;
            if (controlledResetAnchor != null)
                Destroy(controlledResetAnchor);
            controlledResetAnchor = null;
        }

        private void CompleteAbortedResult(
            string reason,
            string started = null)
        {
            LastResult = BuildResult(
                false,
                reason,
                started ?? DateTime.UtcNow.ToString("O"),
                Array.Empty<V3ControlledAssertionResult>());
            WriteResultPackage(LastResult);
        }

        private V3ControlledTestResult BuildResult(
            bool passed,
            string abortReason,
            string started,
            V3ControlledAssertionResult[] assertionResults)
        {
            V3ControllerPipeline pipeline = craft != null
                ? craft.GetComponent<V3ControllerPipeline>() : null;
            V3DiagnosticSession session = recorder != null
                ? recorder.CurrentSession : null;
            return new V3ControlledTestResult
            {
                testId = definition != null ? definition.StableTestId : string.Empty,
                testVersion = definition != null ? definition.Version : 0,
                sessionId = session != null ? session.Manifest.sessionId : string.Empty,
                buildId = ResultBuild != null ? ResultBuild.StableId : string.Empty,
                buildGuid = ResultBuild != null
                    ? ResultBuild.BuildAssetGuid : string.Empty,
                buildOwnership = ResultBuild != null
                    ? ResultBuild.Ownership.ToString() : string.Empty,
                controlPath = pipeline != null
                    ? pipeline.ActiveControlPath.ToString() : string.Empty,
                comparisonGroupId = definition != null
                    ? definition.Output.comparisonGroupId : string.Empty,
                finalPhase = Phase,
                passed = passed,
                abortReason = abortReason ?? string.Empty,
                utcStarted = started,
                utcEnded = DateTime.UtcNow.ToString("O"),
                runnerOverheadMilliseconds =
                    runnerOverhead.Elapsed.TotalMilliseconds,
                droppedSamples = session != null
                    ? session.Manifest.droppedSampleCount : 0,
                droppedEvents = session != null
                    ? session.Manifest.droppedEventCount : 0,
                recorderOutputDirectory = recorder != null &&
                    recorder.LastExport != null
                        ? recorder.LastExport.outputDirectory : string.Empty,
                assertions = assertionResults,
                setupWarnings = setupWarnings.ToArray()
            };
        }

        private CraftBuildDefinition ResultBuild =>
            craft != null && craft.Build != null ? craft.Build : SelectedBuild;

        private void WriteResultPackage(V3ControlledTestResult result)
        {
            if (result == null) return;
            string directory = result.recorderOutputDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                string root = definition != null
                    ? definition.Output.outputFolder
                    : "TestDriveReports/HovercraftV3/Controlled";
                directory = Path.Combine(
                    Path.GetFullPath(root),
                    "Aborted_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(directory);
                result.recorderOutputDirectory = directory;
            }
            File.WriteAllText(
                Path.Combine(directory, "controlled_test_result.json"),
                JsonUtility.ToJson(result, true));
            File.WriteAllText(
                Path.Combine(directory, "controlled_test_result.md"),
                V3ControlledResultMarkdown.Build(result));
        }

        private static bool AllPassed(V3ControlledAssertionResult[] results)
        {
            if (results == null || results.Length == 0) return false;
            for (int i = 0; i < results.Length; i++)
                if (results[i] == null || !results[i].passed) return false;
            return true;
        }

        private static V3PilotCommand NeutralCommand()
        {
            return new V3PilotCommand { StabilizationEnabled = true };
        }

        private static string SerializeFailures(
            V3ControlledFailurePathRequest[] failures)
        {
            if (failures == null || failures.Length == 0) return "[]";
            var wrapper = new FailureWrapper { items = failures };
            return JsonUtility.ToJson(wrapper);
        }

        [Serializable]
        private sealed class FailureWrapper
        {
            public V3ControlledFailurePathRequest[] items;
        }
    }

    /// <summary>
    /// Deterministic, track-frame-driven pilot for controlled tests. It follows
    /// the generated centerline, anticipates curvature, and limits entry speed
    /// instead of relying on a fixed open-loop throttle command.
    /// </summary>
    public sealed class V3ControlledTrackFollower : IDisposable
    {
        private const int CurvaturePreviewSamples = 6;

        private Rigidbody body;
        private CraftBuildDefinition build;
        private V3ControlledTrackFollowerSettings settings;
        private TrackGenerator generator;
        private float currentArc;
        private bool isTracking;

        public bool TryInitialize(
            GameObject owner,
            Rigidbody craftBody,
            CraftBuildDefinition selectedBuild,
            V3ControlledTrackFollowerSettings followerSettings,
            out string error)
        {
            body = craftBody;
            build = selectedBuild;
            settings = followerSettings ??
                new V3ControlledTrackFollowerSettings();
            generator = UnityEngine.Object.FindAnyObjectByType<TrackGenerator>(
                FindObjectsInactive.Exclude);
            if (owner == null || body == null)
            {
                error = "Track follower requires a runner owner and craft Rigidbody.";
                return false;
            }
            if (generator == null || generator.TrackRoot == null ||
                generator.CurrentMacroSections == null ||
                generator.CurrentMacroSections.Count == 0)
            {
                error = "Track follower requires a generated track with macro sections.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public V3PilotCommand BuildCommand()
        {
            var command = new V3PilotCommand
            {
                StabilizationEnabled = true
            };
            if (body == null || generator == null ||
                generator.TrackRoot == null || !TryUpdateTrackPosition())
            {
                command.Throttle = Mathf.Clamp(
                    settings.feedForwardThrottle,
                    settings.minimumThrottle,
                    settings.maximumThrottle);
                return command;
            }

            float speed = body.linearVelocity.magnitude;
            float lookAhead = Mathf.Clamp(
                settings.minimumLookAheadMeters +
                speed * settings.lookAheadSeconds,
                settings.minimumLookAheadMeters,
                settings.maximumLookAheadMeters);
            if (!TryWorldFrame(currentArc, out _,
                out Vector3 currentPoint,
                out Vector3 currentForward,
                out Vector3 currentRight) ||
                !TryWorldFrame(currentArc + lookAhead,
                    out _,
                    out Vector3 targetPoint,
                    out Vector3 targetForward,
                    out Vector3 targetRight))
                return command;

            float rideHeight = build != null && build.HoverConfiguration != null
                ? build.HoverConfiguration.TargetHoverHeight
                : 0f;
            Vector3 targetUp = Vector3.Cross(
                targetForward,
                targetRight).normalized;
            targetPoint += targetUp * rideHeight;
            Vector3 aimDirection = targetPoint - body.worldCenterOfMass;
            if (aimDirection.sqrMagnitude < 0.01f)
                aimDirection = targetForward;
            else
                aimDirection.Normalize();

            Vector3 localAim = body.transform.InverseTransformDirection(
                aimDirection);
            float yawError = Mathf.Atan2(localAim.x, localAim.z) *
                Mathf.Rad2Deg;
            float planar = Mathf.Sqrt(
                localAim.x * localAim.x + localAim.z * localAim.z);
            float pitchError = Mathf.Atan2(localAim.y, planar) *
                Mathf.Rad2Deg;
            float lateralError = Vector3.Dot(
                body.worldCenterOfMass - currentPoint,
                currentRight);

            float targetSpeed = PreviewTargetSpeed(currentArc);
            float forwardSpeed = Vector3.Dot(
                body.linearVelocity,
                currentForward);
            command.Throttle = CalculateThrottle(
                forwardSpeed,
                targetSpeed,
                settings);
            command.Yaw = CalculateAxisCommand(
                yawError,
                settings.yawFullScaleDegrees,
                1f);
            // Positive V3 pitch commands nose-down authority.
            command.Pitch = CalculateAxisCommand(
                -pitchError,
                settings.pitchFullScaleDegrees,
                settings.maximumPitch);
            command.Strafe = CalculateAxisCommand(
                -lateralError,
                settings.lateralFullScaleMeters,
                settings.maximumStrafe);
            return command;
        }

        private float PreviewTargetSpeed(float currentArc)
        {
            float target = Mathf.Max(
                settings.minimumCurveSpeedMetersPerSecond,
                settings.targetSpeedMetersPerSecond);
            float preview = Mathf.Max(1f, settings.curvaturePreviewMeters);
            for (int i = 0; i < CurvaturePreviewSamples; i++)
            {
                float distance = preview * i /
                    (CurvaturePreviewSamples - 1f);
                if (!MacroTrackSampler.TrySampleFrame(
                    generator.CurrentMacroSections,
                    currentArc + distance,
                    out TrackConnectionFrame frame))
                    continue;
                float curvature = Mathf.Max(
                    Mathf.Abs(frame.HorizontalCurvature),
                    Mathf.Abs(frame.VerticalCurvature));
                if (curvature <= 0.000001f) continue;
                float curveSpeed = Mathf.Sqrt(
                    Mathf.Max(0.1f, settings.maximumCurveAcceleration) /
                    curvature);
                target = Mathf.Min(target, curveSpeed);
            }
            return Mathf.Clamp(
                target,
                settings.minimumCurveSpeedMetersPerSecond,
                settings.targetSpeedMetersPerSecond);
        }

        private bool TryUpdateTrackPosition()
        {
            Transform root = generator.TrackRoot != null
                ? generator.TrackRoot
                : generator.transform;
            Vector3 localPosition = root.InverseTransformPoint(
                body.worldCenterOfMass);
            float window = Mathf.Max(
                120f,
                settings.maximumLookAheadMeters * 1.5f);
            SearchNearestFrame(
                localPosition,
                currentArc,
                isTracking ? window : float.PositiveInfinity,
                out float bestArc,
                out float bestDistanceSqr);
            float reacquireDistance = Mathf.Max(
                1f,
                settings.reacquireDistanceMeters);
            if (isTracking && bestDistanceSqr >
                reacquireDistance * reacquireDistance)
            {
                SearchNearestFrame(
                    localPosition,
                    0f,
                    float.PositiveInfinity,
                    out bestArc,
                    out bestDistanceSqr);
            }
            currentArc = bestArc;
            isTracking = bestDistanceSqr <=
                reacquireDistance * reacquireDistance;
            return isTracking;
        }

        private void SearchNearestFrame(
            Vector3 localPosition,
            float aroundArc,
            float window,
            out float bestArc,
            out float bestDistanceSqr)
        {
            bestArc = aroundArc;
            bestDistanceSqr = float.PositiveInfinity;
            IReadOnlyList<GeneratedTrackSection> sections =
                generator.CurrentMacroSections;
            float totalLength = MacroTrackSampler.GetTotalLength(sections);
            for (int i = 0; i < sections.Count; i++)
            {
                GeneratedTrackSection section = sections[i];
                TrackConnectionFrame[] frames = section?.SubdivisionFrames;
                if (section == null || section.RoadId == 1 ||
                    frames == null || frames.Length < 2)
                    continue;
                for (int frameIndex = 0;
                    frameIndex < frames.Length - 1;
                    frameIndex++)
                {
                    TrackConnectionFrame a = frames[frameIndex];
                    TrackConnectionFrame b = frames[frameIndex + 1];
                    if (!float.IsPositiveInfinity(window) &&
                        WrappedArcDistance(
                            aroundArc,
                            a.ArcLength,
                            totalLength) > window &&
                        WrappedArcDistance(
                            aroundArc,
                            b.ArcLength,
                            totalLength) > window)
                        continue;
                    Vector3 segment = b.Position - a.Position;
                    float lengthSqr = segment.sqrMagnitude;
                    float t = lengthSqr > 0.0001f
                        ? Mathf.Clamp01(Vector3.Dot(
                            localPosition - a.Position,
                            segment) / lengthSqr)
                        : 0f;
                    Vector3 nearest = a.Position + segment * t;
                    float distanceSqr =
                        (localPosition - nearest).sqrMagnitude;
                    if (distanceSqr >= bestDistanceSqr) continue;
                    bestDistanceSqr = distanceSqr;
                    bestArc = Mathf.Lerp(a.ArcLength, b.ArcLength, t);
                }
            }
        }

        private static float WrappedArcDistance(
            float a,
            float b,
            float totalLength)
        {
            if (totalLength <= 0.01f) return Mathf.Abs(a - b);
            float distance = Mathf.Abs(Mathf.Repeat(b - a, totalLength));
            return Mathf.Min(distance, totalLength - distance);
        }

        private bool TryWorldFrame(
            float arc,
            out TrackConnectionFrame frame,
            out Vector3 point,
            out Vector3 forward,
            out Vector3 right)
        {
            if (!MacroTrackSampler.TrySampleFrame(
                generator.CurrentMacroSections,
                arc,
                out frame))
            {
                point = Vector3.zero;
                forward = Vector3.forward;
                right = Vector3.right;
                return false;
            }
            Transform root = generator.TrackRoot != null
                ? generator.TrackRoot
                : generator.transform;
            point = root.TransformPoint(frame.Position);
            forward = root.TransformDirection(frame.Forward).normalized;
            right = root.TransformDirection(frame.Right).normalized;
            return true;
        }

        public static float CalculateAxisCommand(
            float error,
            float fullScaleError,
            float maximumMagnitude)
        {
            return Mathf.Clamp(
                error / Mathf.Max(0.001f, fullScaleError),
                -Mathf.Clamp01(maximumMagnitude),
                Mathf.Clamp01(maximumMagnitude));
        }

        public static float CalculateThrottle(
            float forwardSpeed,
            float targetSpeed,
            V3ControlledTrackFollowerSettings followerSettings)
        {
            if (followerSettings == null) return 0f;
            float request = followerSettings.feedForwardThrottle +
                (targetSpeed - forwardSpeed) *
                followerSettings.speedProportionalGain;
            return Mathf.Clamp(
                request,
                followerSettings.minimumThrottle,
                followerSettings.maximumThrottle);
        }

        public void Dispose()
        {
            generator = null;
            body = null;
            build = null;
            isTracking = false;
            currentArc = 0f;
        }
    }

    public static class V3ControlledAssertionEvaluator
    {
        public static V3ControlledAssertionResult EvaluatePersistence(
            V3ControlledTestAssertion assertion,
            bool persisted,
            string measured,
            string evidenceFile)
        {
            if (assertion == null)
                throw new ArgumentNullException(nameof(assertion));
            return new V3ControlledAssertionResult
            {
                assertionId = assertion.assertionId,
                passed = persisted,
                expected = assertion.expectedText ?? string.Empty,
                measured = measured ?? string.Empty,
                tolerance = assertion.tolerance,
                sampleTimeRange = "Editor asset workflow",
                supportingChannels = assertion.supportingChannels ??
                    Array.Empty<string>(),
                evidenceFile = string.IsNullOrWhiteSpace(evidenceFile)
                    ? "asset_persistence_report.md"
                    : evidenceFile
            };
        }

        public static V3ControlledAssertionResult[] Evaluate(
            V3ControlledTestDefinition definition,
            V3DiagnosticSession session)
        {
            return Evaluate(definition, session,
                definition != null ? definition.Build : null);
        }

        public static V3ControlledAssertionResult[] Evaluate(
            V3ControlledTestDefinition definition,
            V3DiagnosticSession session,
            CraftBuildDefinition expectedBuild)
        {
            V3ControlledTestAssertion[] assertions = definition.Assertions;
            var results = new V3ControlledAssertionResult[assertions.Length];
            for (int i = 0; i < assertions.Length; i++)
                results[i] = EvaluateOne(
                    assertions[i], definition, session, expectedBuild);
            return results;
        }

        private static V3ControlledAssertionResult EvaluateOne(
            V3ControlledTestAssertion assertion,
            V3ControlledTestDefinition definition,
            V3DiagnosticSession session,
            CraftBuildDefinition expectedBuild)
        {
            var result = new V3ControlledAssertionResult
            {
                assertionId = assertion.assertionId,
                tolerance = assertion.tolerance,
                sampleTimeRange = assertion.windowStartSeconds.ToString("0.###") +
                    "-" + assertion.windowEndSeconds.ToString("0.###") + " s",
                supportingChannels = assertion.supportingChannels ??
                    Array.Empty<string>(),
                evidenceFile = "samples_compact.bin"
            };
            if (session == null)
            {
                result.expected = "Recorded schema-6 session";
                result.measured = "No session";
                return result;
            }
            switch (assertion.type)
            {
                case V3ControlledAssertionType.EventOccurs:
                case V3ControlledAssertionType.EventAbsent:
                    int count = CountEvents(
                        session,
                        assertion.eventType,
                        assertion.windowStartSeconds,
                        assertion.windowEndSeconds);
                    bool absent = assertion.type ==
                        V3ControlledAssertionType.EventAbsent;
                    result.passed = absent ? count == 0 :
                        count == assertion.expectedEventCount;
                    result.expected = absent ? "0 events" :
                        assertion.expectedEventCount + " events";
                    result.measured = count + " events";
                    result.evidenceFile = "events.json";
                    return result;
                case V3ControlledAssertionType.StateTransition:
                    result.passed = HasOrderedStates(
                        session,
                        assertion.stateSequence,
                        out string observed);
                    result.expected = string.Join(" -> ",
                        assertion.stateSequence ?? Array.Empty<string>());
                    result.measured = observed;
                    return result;
                case V3ControlledAssertionType.BuildIdentity:
                    result.passed = expectedBuild != null &&
                        session.Manifest.craftBuildStableId ==
                        expectedBuild.StableId &&
                        session.Manifest.craftBuildAssetGuid ==
                        expectedBuild.BuildAssetGuid &&
                        session.Manifest.craftBuildOwnership ==
                        expectedBuild.Ownership.ToString();
                    result.expected = expectedBuild != null
                        ? expectedBuild.StableId + " / " +
                          expectedBuild.BuildAssetGuid + " / " +
                          expectedBuild.Ownership
                        : "Selected craft build identity";
                    result.measured = session.Manifest.craftBuildStableId + " / " +
                        session.Manifest.craftBuildAssetGuid + " / " +
                        session.Manifest.craftBuildOwnership;
                    result.evidenceFile = "session_manifest.json";
                    return result;
                case V3ControlledAssertionType.Persistence:
                    result.passed = false;
                    result.expected = assertion.expectedText;
                    result.measured = "Persistence assertions require the editor asset workflow.";
                    return result;
            }

            List<float> values = CollectValues(assertion, session);
            if (values.Count == 0)
            {
                result.expected = "Samples for " + assertion.signalId;
                result.measured = "No comparable samples";
                return result;
            }
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            float sum = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                min = Mathf.Min(min, values[i]);
                max = Mathf.Max(max, values[i]);
                sum += values[i];
            }
            float mean = sum / values.Count;
            if (assertion.type == V3ControlledAssertionType.NumericRange)
            {
                result.passed = min >= assertion.minimum &&
                    max <= assertion.maximum;
                result.expected = assertion.minimum.ToString("0.#####") +
                    ".." + assertion.maximum.ToString("0.#####");
                result.measured = "min=" + min.ToString("0.#####") +
                    ", max=" + max.ToString("0.#####");
            }
            else if (assertion.type ==
                V3ControlledAssertionType.SignalReachesMinimum)
            {
                result.passed = max >= assertion.minimum;
                result.expected = assertion.signalId + " reaches >= " +
                    assertion.minimum.ToString("0.#####");
                result.measured = "maximum=" + max.ToString("0.#####");
            }
            else if (assertion.type ==
                V3ControlledAssertionType.ContactFreeResidual)
            {
                values.Sort();
                float p95 = values[Mathf.Clamp(
                    Mathf.CeilToInt(values.Count * 0.95f) - 1,
                    0,
                    values.Count - 1)];
                result.passed = p95 <= assertion.maximum;
                result.expected = "p95 <= " + assertion.maximum.ToString("0.#####");
                result.measured = "p95=" + p95.ToString("0.#####");
                result.evidenceFile = "force_summary.csv";
            }
            else if (assertion.type == V3ControlledAssertionType.Deadline)
            {
                result.passed = HasDeadlineValue(assertion, session,
                    out float reachedAt);
                result.expected = assertion.signalId + " <= " +
                    assertion.maximum.ToString("0.#####") + " by " +
                    (assertion.windowStartSeconds +
                     assertion.deadlineSeconds).ToString("0.###") +
                    " s (within " +
                    assertion.deadlineSeconds.ToString("0.###") +
                    " s after " +
                    assertion.windowStartSeconds.ToString("0.###") + " s)";
                result.measured = result.passed
                    ? "reached at " + reachedAt.ToString("0.###") + " s"
                    : "deadline not reached";
            }
            else
            {
                result.passed = Mathf.Abs(mean - assertion.target) <=
                    assertion.tolerance;
                result.expected = assertion.target.ToString("0.#####") +
                    " +/- " + assertion.tolerance.ToString("0.#####");
                result.measured = "mean=" + mean.ToString("0.#####");
            }
            return result;
        }

        private static List<float> CollectValues(
            V3ControlledTestAssertion assertion,
            V3DiagnosticSession session)
        {
            var values = new List<float>(Mathf.Max(1, session.SampleCount));
            for (int i = 0; i < session.SampleCount; i++)
            {
                ref V3DiagnosticSample sample = ref session.GetSample(i);
                double time = sample.identity.sessionElapsed;
                if (time < assertion.windowStartSeconds ||
                    time > assertion.windowEndSeconds ||
                    !sample.identity.dynamicsValid)
                    continue;
                if (assertion.type ==
                    V3ControlledAssertionType.ContactFreeResidual &&
                    sample.world.contactCount != 0)
                    continue;
                if (V3ControlledSignalRegistry.TryRead(
                    ref sample,
                    assertion.signalId,
                    out float value))
                    values.Add(value);
            }
            return values;
        }

        private static int CountEvents(
            V3DiagnosticSession session,
            V3DiagnosticEventType type,
            float windowStart,
            float windowEnd)
        {
            int count = 0;
            for (int i = 0; i < session.EventCount; i++)
            {
                V3DiagnosticEvent item = session.Events[i];
                if (item != null && item.type == type &&
                    item.timestamp >= windowStart &&
                    item.timestamp <= windowEnd)
                    count++;
            }
            return count;
        }

        private static bool HasOrderedStates(
            V3DiagnosticSession session,
            string[] expected,
            out string observed)
        {
            expected ??= Array.Empty<string>();
            int next = 0;
            string last = string.Empty;
            var states = new List<string>(8);
            for (int i = 0; i < session.SampleCount; i++)
            {
                string state = session.Samples[i].belief.surfaceState ?? string.Empty;
                if (state == last) continue;
                last = state;
                states.Add(state);
                if (next < expected.Length && string.Equals(
                    state,
                    expected[next],
                    StringComparison.Ordinal)) next++;
            }
            observed = string.Join(" -> ", states);
            return expected.Length > 0 && next == expected.Length;
        }

        private static bool HasDeadlineValue(
            V3ControlledTestAssertion assertion,
            V3DiagnosticSession session,
            out float reachedAt)
        {
            float deadline = assertion.windowStartSeconds +
                assertion.deadlineSeconds;
            for (int i = 0; i < session.SampleCount; i++)
            {
                ref V3DiagnosticSample sample = ref session.GetSample(i);
                float time = (float)sample.identity.sessionElapsed;
                if (time < assertion.windowStartSeconds || time > deadline)
                    continue;
                if (V3ControlledSignalRegistry.TryRead(
                    ref sample,
                    assertion.signalId,
                    out float value) && value <= assertion.maximum)
                {
                    reachedAt = time;
                    return true;
                }
            }
            reachedAt = -1f;
            return false;
        }
    }

    public static class V3ControlledResultMarkdown
    {
        public static string Build(V3ControlledTestResult result)
        {
            var text = new System.Text.StringBuilder(2048);
            text.AppendLine("# Hovercraft V3 Controlled Test Result");
            text.AppendLine();
            text.AppendLine("- Test: `" + result.testId + "` v" + result.testVersion);
            text.AppendLine("- Result: **" + (result.passed ? "PASS" : "FAIL") + "**");
            text.AppendLine("- Phase: " + result.finalPhase);
            text.AppendLine("- Build: `" + result.buildId + "` (`" +
                result.buildGuid + "`, " + result.buildOwnership + ")");
            text.AppendLine("- Control path: " + result.controlPath);
            text.AppendLine("- Session: `" + result.sessionId + "`");
            text.AppendLine("- Runner overhead: " +
                result.runnerOverheadMilliseconds.ToString("0.###") + " ms");
            text.AppendLine("- Dropped samples: " + result.droppedSamples);
            text.AppendLine("- Dropped events: " + result.droppedEvents);
            if (!string.IsNullOrEmpty(result.abortReason))
                text.AppendLine("- Abort reason: " + result.abortReason);
            text.AppendLine();
            text.AppendLine("## Assertions");
            text.AppendLine();
            V3ControlledAssertionResult[] assertions = result.assertions ??
                Array.Empty<V3ControlledAssertionResult>();
            for (int i = 0; i < assertions.Length; i++)
            {
                V3ControlledAssertionResult item = assertions[i];
                text.AppendLine("- " + (item.passed ? "PASS" : "FAIL") +
                    " `" + item.assertionId + "`: expected " + item.expected +
                    "; measured " + item.measured + "; evidence `" +
                    item.evidenceFile + "`.");
            }
            if (result.setupWarnings != null && result.setupWarnings.Length > 0)
            {
                text.AppendLine();
                text.AppendLine("## Setup evidence/warnings");
                text.AppendLine();
                for (int i = 0; i < result.setupWarnings.Length; i++)
                    text.AppendLine("- " + result.setupWarnings[i]);
            }
            return text.ToString();
        }
    }
}
