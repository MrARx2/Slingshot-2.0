using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lunarlight.Hovercraft.V3.Diagnostics;
using Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public sealed class V3ControlledDynamicsTestWindow : EditorWindow
    {
        [SerializeField] private V3ControlledTestDefinition definition;
        [SerializeField] private CraftBuildDefinition selectedBuild;
        private Vector2 scroll;
        private string validation = "Select a controlled test definition.";

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Controlled Dynamics Tests")]
        public static void Open()
        {
            GetWindow<V3ControlledDynamicsTestWindow>(
                "V3 Controlled Dynamics");
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Controlled Dynamics Tests",
                EditorStyles.boldLabel);
            V3ControlledTestDefinition previousDefinition = definition;
            definition = (V3ControlledTestDefinition)EditorGUILayout.ObjectField(
                "Test Definition",
                definition,
                typeof(V3ControlledTestDefinition),
                false);
            if (definition != previousDefinition && selectedBuild == null &&
                definition != null)
                selectedBuild = definition.Build;
            selectedBuild = (CraftBuildDefinition)EditorGUILayout.ObjectField(
                "Craft Definition",
                selectedBuild,
                typeof(CraftBuildDefinition),
                false);
            if (definition != null)
            {
                EditorGUILayout.LabelField("Selected craft",
                    selectedBuild != null
                        ? selectedBuild.StableId : "<missing>");
                EditorGUILayout.LabelField("Selected ownership / GUID",
                    selectedBuild != null
                        ? selectedBuild.Ownership + " / " +
                          selectedBuild.BuildAssetGuid
                        : "<missing>");
                EditorGUILayout.LabelField("Definition default",
                    definition.Build != null
                        ? definition.Build.StableId : "<missing>");
                EditorGUILayout.LabelField("Scene", definition.ScenePath);
                EditorGUILayout.LabelField("Recorder", "Schema " +
                    definition.RequiredSchemaVersion + " / ForensicCompact");
                EditorGUILayout.LabelField("Duration",
                    definition.ExpectedDurationSeconds.ToString("0.###") + " s");
                EditorGUILayout.LabelField("Fixture", definition.FixtureKind.ToString());
                EditorGUILayout.LabelField("Failures",
                    definition.Failures.Length.ToString());
                EditorGUILayout.LabelField("Assertions",
                    definition.Assertions.Length.ToString());
                EditorGUILayout.LabelField("Output", definition.Output.outputFolder);
                EditorGUILayout.LabelField("Comparison group",
                    definition.Output.comparisonGroupId);
            }
            EditorGUILayout.HelpBox(validation, MessageType.Info);

            using (new EditorGUI.DisabledScope(
                definition == null || selectedBuild == null))
            {
                if (GUILayout.Button("Validate Definition"))
                    ValidateSelected();
                if (GUILayout.Button("Open Required Scene"))
                    OpenRequiredScene();
                if (GUILayout.Button("Prepare Test"))
                    Prepare(false);
                if (GUILayout.Button("Run Selected"))
                    Prepare(true);
                if (GUILayout.Button("Run Batch"))
                    V3ControlledTestBatchRunner.QueueSelectedOrAll(
                        definition, selectedBuild);
                if (GUILayout.Button("Abort"))
                    AbortActive();
                if (GUILayout.Button("Open Output"))
                    EditorUtility.RevealInFinder(
                        Path.GetFullPath(definition.Output.outputFolder));
                if (GUILayout.Button("Compare Results"))
                    CompareTwoResults();
                if (GUILayout.Button("Create Calibration Decision"))
                    V3ControlledTestAssetUtility.CreateCalibrationDecision(
                        definition, selectedBuild);
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Create / Update Scenario A-H Presets"))
                V3ControlledTestAssetUtility.CreateOrUpdatePresets();
            using (new EditorGUI.DisabledScope(
                EditorApplication.isPlayingOrWillChangePlaymode ||
                V3ControlledTestBatchRunner.IsQueued))
            {
                if (GUILayout.Button("Clear Previous Test Reports..."))
                    ClearPreviousReports();
            }
            EditorGUILayout.EndScrollView();
        }

        private void ClearPreviousReports()
        {
            const string allReports = "TestDriveReports";
            string controlledReports = definition != null
                ? definition.Output.outputFolder
                : "TestDriveReports/HovercraftV3/Controlled";
            bool controlledMeasured =
                V3ControlledReportCleanupUtility.TryMeasure(
                    controlledReports,
                    false,
                    out string controlledPath,
                    out int controlledFiles,
                    out long controlledBytes,
                    out string controlledError);
            bool allMeasured = V3ControlledReportCleanupUtility.TryMeasure(
                    allReports,
                    true,
                    out string allPath,
                    out int allFiles,
                    out long allBytes,
                    out string allError);
            if (!controlledMeasured || !allMeasured)
            {
                EditorUtility.DisplayDialog(
                    "Report cleanup unavailable",
                    string.IsNullOrEmpty(controlledError)
                        ? allError : controlledError,
                    "OK");
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "Clear previous test reports",
                "Controlled output:\n" + controlledPath + "\n" +
                controlledFiles + " files / " +
                V3ControlledReportCleanupUtility.FormatBytes(controlledBytes) +
                "\n\nAll test-drive output:\n" + allPath + "\n" +
                allFiles + " files / " +
                V3ControlledReportCleanupUtility.FormatBytes(allBytes) +
                "\n\nReport packages are generated evidence. Deleting them " +
                "does not modify craft assets, scenes, or test definitions, " +
                "but deleted evidence cannot be recovered by this tool.",
                "Clear Controlled Only",
                "Cancel",
                "Clear All TestDriveReports");
            if (choice == 1) return;

            bool clearAll = choice == 2;
            string target = clearAll ? allReports : controlledReports;
            int fileCount = clearAll ? allFiles : controlledFiles;
            long byteCount = clearAll ? allBytes : controlledBytes;
            if (clearAll && !EditorUtility.DisplayDialog(
                    "Confirm full report cleanup",
                    "Permanently delete the contents of:\n" + allPath +
                    "\n\n" + fileCount + " files / " +
                    V3ControlledReportCleanupUtility.FormatBytes(byteCount) +
                    "\n\nThis includes controlled, benchmark, V2, V3, ZIP, " +
                    "and hover-drive evidence under TestDriveReports.",
                    "Delete All Reports",
                    "Cancel"))
                return;

            try
            {
                V3ControlledReportCleanupUtility.ClearContents(target, clearAll);
                validation = "Deleted " + fileCount + " report files (" +
                    V3ControlledReportCleanupUtility.FormatBytes(byteCount) +
                    ") from " + (clearAll ? allPath : controlledPath) + ".";
                Repaint();
            }
            catch (Exception exception)
            {
                validation = "Report cleanup failed: " + exception.Message;
                EditorUtility.DisplayDialog(
                    "Report cleanup failed", exception.Message, "OK");
            }
        }

        private void ValidateSelected()
        {
            var errors = new List<string>();
            definition.ValidateForBuild(selectedBuild, errors);
            validation = errors.Count == 0
                ? "Definition is valid. Runtime preflight still verifies the " +
                  "scene, world, fixture, selected-craft identity, recorder, and control path."
                : string.Join("\n", errors);
        }

        private void OpenRequiredScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
        }

        private void Prepare(bool run)
        {
            ValidateSelected();
            var errors = new List<string>();
            if (!definition.ValidateForBuild(selectedBuild, errors))
            {
                EditorUtility.DisplayDialog(
                    "Invalid controlled test",
                    string.Join("\n", errors),
                    "OK");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = EditorSceneManager.OpenScene(
                definition.ScenePath,
                OpenSceneMode.Single);
            GameObject existing = GameObject.Find("[V3 Controlled Dynamics Runner]");
            if (existing != null) DestroyImmediate(existing);
            var root = new GameObject("[V3 Controlled Dynamics Runner]");
            SceneManager.MoveGameObjectToScene(root, scene);
            V3ControlledDynamicsTestRunner runner =
                root.AddComponent<V3ControlledDynamicsTestRunner>();
            runner.Configure(definition, selectedBuild, run);
            Selection.activeObject = root;
            if (run) EditorApplication.EnterPlaymode();
        }

        private static void AbortActive()
        {
            V3ControlledDynamicsTestRunner runner =
                FindAnyObjectByType<V3ControlledDynamicsTestRunner>(
                    FindObjectsInactive.Include);
            runner?.Abort("Aborted from Controlled Dynamics Tests window.");
        }

        private static void CompareTwoResults()
        {
            string left = EditorUtility.OpenFilePanel(
                "Select baseline controlled result",
                Path.GetFullPath("TestDriveReports/HovercraftV3/Controlled"),
                "json");
            if (string.IsNullOrEmpty(left)) return;
            string right = EditorUtility.OpenFilePanel(
                "Select candidate controlled result",
                Path.GetDirectoryName(left),
                "json");
            if (string.IsNullOrEmpty(right)) return;
            string output = V3ControlledComparisonReportWriter.Write(
                new[] { left, right });
            EditorUtility.RevealInFinder(output);
        }
    }

    [CustomEditor(typeof(V3ControlledTestDefinition))]
    public sealed class V3ControlledTestDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var definition = (V3ControlledTestDefinition)target;
            var errors = new List<string>();
            bool valid = definition.Validate(errors);
            EditorGUILayout.HelpBox(
                valid ? "Definition contract is valid." : string.Join("\n", errors),
                valid ? MessageType.Info : MessageType.Error);
            if (GUILayout.Button("Open Controlled Dynamics Tests"))
                V3ControlledDynamicsTestWindow.Open();
        }
    }

    public static class V3ControlledTestAssetUtility
    {
        public const string DefinitionRoot =
            "Assets/HovercraftV3/Content/ControlledTests/Definitions";
        public const string CalibrationRoot =
            "Assets/HovercraftV3/Content/ControlledTests/Calibration";
        public const string TestBuildRoot =
            "Assets/HovercraftV3/Content/ControlledTests/Builds";
        private const string TrackScene =
            "Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity";
        public const string ControlledScene =
            "Assets/HovercraftV3/Scenes/Diagnostics/" +
            "V3_ControlledDynamics.unity";
        private const string PrimaryBuild =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset";

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Controlled Tests/Create Or Update Scenario Presets")]
        public static void CreateOrUpdatePresets()
        {
            EnsureFolder(DefinitionRoot);
            CraftBuildDefinition build = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(PrimaryBuild);
            if (build == null)
                throw new InvalidOperationException("Missing primary systems build.");
            V3WorldProfile world = V3WorldSimulationInstaller.EnsureEarthProfile();
            CraftBuildDefinition gravityOnlyBuild =
                CreateGravityOnlyTestBuild(build);

            CreateScenario(build, "A_Ballistic",
                "test.v3.ballistic.primary_systems.01",
                "Scenario A - Clean Ballistic Fall",
                V3ControlledTestCategory.PhysicsCore,
                V3ControlledFixtureKind.None,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 250f, -500f),
                    linearVelocity = Vector3.zero,
                    requireNoInitialContact = true
                },
                4f,
                new[]
                {
                    Range("a.capture.zero", "hover.captureAuthority", 0f, 0.001f),
                    Range("a.bottom.zero", "forces.bottom", 0f, 1f),
                    Range("a.roof.zero", "forces.roof", 0f, 1f),
                    Range("a.no.contact", "world.contactCount", 0f, 0f),
                    Target("a.gravity.response", "world.acceleration.y",
                        -9.80665f, 1.5f, 0.05f, 0.5f),
                    Transition("a.freeflight", "FreeFlight"),
                    Residual("a.residual.p95", 250f),
                    NoEvent("a.no.false.landing", V3DiagnosticEventType.ContactLanding),
                    BuildIdentity("a.build.identity")
                },
                "comparison.ballistic_v1");

            V3ControlledTestDefinition gravityOnly = CreateScenario(
                gravityOnlyBuild, "A2_BallisticGravityOnly",
                "test.v3.ballistic.gravity_only.01",
                "Scenario A2 - Gravity Only Approximation",
                V3ControlledTestCategory.PhysicsCore,
                V3ControlledFixtureKind.None,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 250f, -500f),
                    linearVelocity = Vector3.zero,
                    requireNoInitialContact = true
                },
                4f,
                new[]
                {
                    Range("a2.aero.zero", "forces.aero", 0f, 0.001f),
                    Range("a2.fin.zero", "forces.fin", 0f, 0.001f),
                    Range("a2.no.contact", "world.contactCount", 0f, 0f),
                    Target("a2.gravity.response", "world.acceleration.y",
                        -9.80665f, 0.25f, 0.05f, 0.5f),
                    Residual("a2.residual.p95", 100f),
                    NoEvent("a2.no.false.landing",
                        V3DiagnosticEventType.ContactLanding),
                    BuildIdentity("a2.build.identity")
                },
                "comparison.gravity_only_v2");
            gravityOnly.ConfigureRuntimeOverride(
                true,
                "Transiently disables every aerodynamic force producer so the " +
                "same gravity-only definition is valid for any selected craft.",
                true);

            CreateScenario(build, "B_FinSweep",
                "test.v3.fin_sweep.primary_systems.01",
                "Scenario B - Fin Aerodynamic Sweep",
                V3ControlledTestCategory.Aerodynamics,
                V3ControlledFixtureKind.None,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 300f, -800f),
                    linearVelocity = Vector3.forward * 100f
                },
                3f,
                new[]
                {
                    Reaches("b.fin.force.present", "forces.fin", 100f),
                    Range("b.fin.force.bounded", "forces.fin", 0f, 250000f),
                    Range("b.no.contact", "world.contactCount", 0f, 0f),
                    Residual("b.residual.p95", 100f),
                    BuildIdentity("b.build.identity")
                },
                "comparison.fin_stall_curve_v2");

            CreateScenario(build, "C_SurfaceDeparture",
                "test.v3.surface_departure.race_speed.01",
                "Scenario C - Surface Departure",
                V3ControlledTestCategory.SurfaceControl,
                V3ControlledFixtureKind.CleanSurfaceEdge,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 8f, -30f),
                    linearVelocity = Vector3.forward * 30f
                },
                2.5f,
                new[]
                {
                    Transition("c.transition", "NearSurfaceHover",
                        "CaptureLimited", "FreeFlight"),
                    Deadline("c.bottom.clears", "hover.bottomRequest", 0.001f, 0.25f, 1.36f),
                    Deadline("c.roof.clears", "hover.roofRequest", 0.001f, 0.25f, 1.36f),
                    EventWindow("c.probe.loss", V3DiagnosticEventType.ProbeLoss,
                        1, 1f, 2f),
                    EventWindow("c.envelope.departure",
                        V3DiagnosticEventType.SurfaceEnvelopeDeparture,
                        1, 1f, 2f),
                    NoEventWindow("c.no.reacquire.after.departure",
                        V3DiagnosticEventType.SurfaceReacquired, 1.5f, 2.5f),
                    NoEvent("c.no.reset", V3DiagnosticEventType.Reset),
                    BuildIdentity("c.build.identity")
                },
                "comparison.capture_range_v2");

            CreateScenario(build, "D_DescendingCapture",
                "test.v3.descending_capture.primary_systems.01",
                "Scenario D - Loop and Descending Capture",
                V3ControlledTestCategory.SurfaceControl,
                V3ControlledFixtureKind.DescendingSurface,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 8f, -25f),
                    linearVelocity = Vector3.forward * 35f
                },
                4f,
                new[]
                {
                    Range("d.capture.bounded", "hover.captureAuthority", 0f, 1f),
                    Reaches("d.capture.established", "hover.captureAuthority", 0.9f),
                    EventWindow("d.surface.reacquired",
                        V3DiagnosticEventType.SurfaceReacquired, 1, 0f, 0.5f),
                    NoEvent("d.no.probe.loss", V3DiagnosticEventType.ProbeLoss),
                    NoEvent("d.no.reset", V3DiagnosticEventType.Reset),
                    Residual("d.residual.p95", 500f),
                    BuildIdentity("d.build.identity")
                },
                "comparison.descending_capture_v2");

            V3ControlledTestDefinition reset = CreateScenario(
                build, "E_ResetNeutrality",
                "test.v3.reset_neutrality.primary_systems.01",
                "Scenario E - Reset Neutrality",
                V3ControlledTestCategory.Reset,
                V3ControlledFixtureKind.FlatSurface,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 8f, 0f)
                },
                3f,
                new[]
                {
                    Event("e.reset.event", V3DiagnosticEventType.Reset, 1),
                    Deadline("e.bottom.neutral", "forces.bottom", 1f, 0.1f, 1f),
                    Deadline("e.roof.neutral", "forces.roof", 1f, 0.1f, 1f),
                    Deadline("e.propulsion.neutral", "forces.propulsion", 1f, 0.1f, 1f),
                    Deadline("e.discontinuity.invalid", "identity.dynamicsValid",
                        0f, 0.1f, 1f),
                    BuildIdentity("e.build.identity")
                },
                "comparison.reset_neutrality_v1",
                new[]
                {
                    new V3ControlledCommandWindow
                    {
                        startSeconds = 0f,
                        endSeconds = 3f,
                        throttle = 0.8f,
                        strafe = 0.5f,
                        yaw = 0.5f,
                        pitch = 0.5f,
                        lift = 1f,
                        downforce = 0.5f,
                        stabilizationEnabled = true,
                        gripBreaker = true
                    }
                });
            reset.ConfigureMeasuredReset(1f, "CONTROLLED RESET NEUTRALITY");

            CreateScenario(build, "F_Inertia",
                "test.v3.inertia.primary_systems.01",
                "Scenario F - Inertia and Angular Response",
                V3ControlledTestCategory.Inertia,
                V3ControlledFixtureKind.None,
                new V3ControlledInitialCondition
                {
                    worldPosition = new Vector3(0f, 300f, -1000f)
                },
                3f,
                new[]
                {
                    Range("f.angular.error", "inertia.angularAccelerationError",
                        0f, 10f),
                    BuildIdentity("f.build.identity")
                },
                "comparison.inertia_roll_pitch_yaw_v1",
                new[]
                {
                    new V3ControlledCommandWindow
                    {
                        startSeconds = 0.1f, endSeconds = 0.8f,
                        yaw = 0.7f, stabilizationEnabled = false
                    },
                    new V3ControlledCommandWindow
                    {
                        startSeconds = 1.1f, endSeconds = 1.8f,
                        pitch = 0.7f, stabilizationEnabled = false
                    }
                });

            CreateScenario(build, "G_AuthoredVariants",
                "test.v3.selected_craft.integrity.01",
                "Scenario G - Selected Craft Integrity",
                V3ControlledTestCategory.Authoring,
                V3ControlledFixtureKind.None,
                new V3ControlledInitialCondition(),
                1f,
                new[]
                {
                    BuildIdentity("g.build.identity"),
                    Range("g.no.contact", "world.contactCount", 0f, 0f),
                    Residual("g.residual.p95", 100f),
                    NoEvent("g.no.reset", V3DiagnosticEventType.Reset),
                    NoEvent("g.no.mainframe.degraded",
                        V3DiagnosticEventType.MainframeDegraded)
                },
                "comparison.selected_craft_integrity_v1");

            V3ControlledTestDefinition fullTrack = CreateScenario(
                build, "H_PrimaryFullTrack",
                "test.v3.full_track.primary_systems.01",
                "Scenario H - Track-Following Endurance",
                V3ControlledTestCategory.FullTrack,
                V3ControlledFixtureKind.TrackTestScene,
                new V3ControlledInitialCondition
                {
                    worldPosition = Vector3.zero,
                    useTrackRelation = true,
                    trackDistanceMeters = 0f
                },
                60f,
                new[]
                {
                    BuildIdentity("h.build.identity"),
                    Range("h.capture.bounded", "hover.captureAuthority", 0f, 1f),
                    Residual("h.residual.p95", 1000f),
                    Reaches("h.track.minimum_progress", "track.distance", 6000f),
                    Range("h.track.inside_bounds", "track.insideBounds", 1f, 1f),
                    Range("h.track.lateral_containment", "track.lateralOffset",
                        -55f, 55f),
                    NoEvent("h.no.reset", V3DiagnosticEventType.Reset),
                    NoEvent("h.no.crash", V3DiagnosticEventType.Crash),
                    NoEvent("h.no.out_of_bounds", V3DiagnosticEventType.OutOfBounds),
                    NoEvent("h.no.reverse", V3DiagnosticEventType.ReverseTravel),
                    NoEvent("h.no.rollover", V3DiagnosticEventType.Rollover),
                    NoEvent("h.no.spin", V3DiagnosticEventType.Spin)
                },
                "comparison.track_following_endurance_schema6_v2");
            fullTrack.ConfigureTrackFollower(
                new V3ControlledTrackFollowerSettings
                {
                    targetSpeedMetersPerSecond = 160f,
                    minimumCurveSpeedMetersPerSecond = 80f,
                    maximumCurveAcceleration = 55f,
                    maximumThrottle = 0.75f,
                    minimumThrottle = -0.35f,
                    feedForwardThrottle = 0.18f,
                    speedProportionalGain = 0.012f,
                    minimumLookAheadMeters = 35f,
                    lookAheadSeconds = 0.7f,
                    maximumLookAheadMeters = 180f,
                    curvaturePreviewMeters = 450f,
                    yawFullScaleDegrees = 30f,
                    pitchFullScaleDegrees = 30f,
                    lateralFullScaleMeters = 45f,
                    maximumStrafe = 0.4f,
                    maximumPitch = 0.85f,
                    reacquireDistanceMeters = 250f
                });
            fullTrack.ConfigureWorldRequirement(
                TrackScene,
                "world.root.v3_tracktest",
                world);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateAllOrThrow();
            Debug.Log("Hovercraft V3 controlled scenario A-H presets updated.");
        }

        public static void CreateOrUpdatePresetsFromCommandLine()
        {
            CreateOrUpdatePresets();
        }

        public static void ValidateAllOrThrow()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:V3ControlledTestDefinition",
                new[] { "Assets/HovercraftV3" });
            var definitions = new List<V3ControlledTestDefinition>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                V3ControlledTestDefinition item = AssetDatabase.LoadAssetAtPath<
                    V3ControlledTestDefinition>(
                        AssetDatabase.GUIDToAssetPath(guids[i]));
                if (item != null) definitions.Add(item);
            }
            var errors = new List<string>();
            if (!V3ControlledTestRegistry.ValidateUnique(definitions, errors))
                throw new InvalidOperationException(string.Join("\n", errors));
        }

        private static CraftBuildDefinition CreateGravityOnlyTestBuild(
            CraftBuildDefinition source)
        {
            EnsureFolder(TestBuildRoot);
            string path = TestBuildRoot + "/ScenarioA_GravityOnly.asset";
            CraftBuildDefinition build = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(path);
            if (build == null)
            {
                build = ScriptableObject.CreateInstance<CraftBuildDefinition>();
                AssetDatabase.CreateAsset(build, path);
                AssetDatabase.SaveAssets();
            }
            build.CopySelectionsFrom(source);
            build.CopyPresentationFrom(source);
            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation != null && installation.Endpoint != null &&
                    installation.Endpoint.Category == EndpointCategory.Fin)
                    installation.SetEmpty();
            }
            build.ConfigureAuthoringIdentity(
                "build.test.v3.ballistic.gravity_only.01",
                AssetDatabase.AssetPathToGUID(path),
                V3BuildOwnership.TestOnly,
                false);
            EditorUtility.SetDirty(build);
            return build;
        }

        public static void CreateCalibrationDecision(
            V3ControlledTestDefinition definition)
        {
            CreateCalibrationDecision(definition,
                definition != null ? definition.Build : null);
        }

        public static void CreateCalibrationDecision(
            V3ControlledTestDefinition definition,
            CraftBuildDefinition selectedBuild)
        {
            EnsureFolder(CalibrationRoot);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                CalibrationRoot + "/CalibrationDecision.asset");
            var decision = ScriptableObject.CreateInstance<V3CalibrationDecision>();
            decision.decisionId = "calibration." +
                (definition != null ? definition.StableTestId : "v3") + ".01";
            decision.dateUtc = DateTime.UtcNow.ToString("O");
            decision.sourceTestGroup = definition != null
                ? definition.Output.comparisonGroupId : string.Empty;
            decision.targetAsset = selectedBuild;
            AssetDatabase.CreateAsset(decision, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = decision;
            EditorGUIUtility.PingObject(decision);
        }

        public static void CreateSprintCalibrationDecisionsFromCommandLine()
        {
            EnsureFolder(CalibrationRoot);
            V3AerodynamicFinDefinition fin = AssetDatabase.LoadAssetAtPath<
                V3AerodynamicFinDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "SystemsIntegration/Definitions/Balanced_AeroFin.asset");
            V3ControlledTestDefinition fullTrack = AssetDatabase.LoadAssetAtPath<
                V3ControlledTestDefinition>(DefinitionRoot +
                    "/H_PrimaryFullTrack.asset");
            string finPackage = FindLatestEvidencePackage(
                "TestDriveReports/HovercraftV3/Controlled/FinSweeps",
                "*_scenario_b_production_fin_matrix");
            string trackPackage = FindLatestEvidencePackage(
                "TestDriveReports/HovercraftV3/Controlled",
                "*test.v3.full_track.primary_systems.01*");

            V3CalibrationDecision retained = CreateOrLoadDecision(
                CalibrationRoot + "/FinCoefficients_Retained.asset");
            retained.decisionId = "calibration.v3.fin_coefficients.retain.01";
            retained.dateUtc = DateTime.UtcNow.ToString("O");
            retained.systemDomain = "Aerodynamics / fin coefficients";
            retained.sourceTestGroup = "comparison.fin_stall_curve_v1";
            retained.oldValue = fin != null
                ? "ClMax=" + fin.MaximumLiftCoefficient +
                  ", ClPostStall=" + fin.PostStallLiftCoefficient +
                  ", reverseScale=" + fin.ReverseFlowScale +
                  ", Cd0=" + fin.BaseDragCoefficient +
                  ", CdStall=" + fin.StalledDragCoefficient
                : "Current production fin coefficients";
            retained.newValue = "Unchanged (retained)";
            retained.expectedEffect =
                "Preserve the validated AoA sign, attached-flow slope, " +
                "post-stall decay, drag rise, and bounded reverse flow.";
            retained.measuredEffect =
                "360 production-runtime samples; all seven matrix assertions passed.";
            retained.accepted = true;
            retained.rationale =
                "No correctness-driven coefficient change was supported by the evidence.";
            retained.targetAsset = fin;
            retained.author = "Codex controlled-dynamics sprint";
            retained.reportPackageReferences = new[] { finPackage };
            EditorUtility.SetDirty(retained);

            V3CalibrationDecision rejected = CreateOrLoadDecision(
                CalibrationRoot + "/FullTrackThrottleIncrease_Rejected.asset");
            rejected.decisionId = "calibration.v3.full_track.throttle_increase.reject.01";
            rejected.dateUtc = DateTime.UtcNow.ToString("O");
            rejected.systemDomain = "Primary full-track command profile";
            rejected.sourceTestGroup =
                "comparison.primary_full_track_schema6_v1";
            rejected.oldValue = "Scripted throttle=0.6";
            rejected.newValue =
                "Proposed throttle=0.7 (rejected without application)";
            rejected.expectedEffect =
                "Shorter lap time, at the cost of higher speed and authority demand.";
            rejected.measuredEffect =
                "The 0.6 baseline already reached 550.568 m/s, reset once, " +
                "crashed once, produced 18 OutOfBounds events, and reached only " +
                "10,968.02 m of the 48,230.965 m track.";
            rejected.accepted = false;
            rejected.rationale =
                "Increasing throttle would move the failed gate in the wrong " +
                "direction; no value was changed.";
            rejected.targetAsset = fullTrack;
            rejected.author = "Codex controlled-dynamics sprint";
            rejected.reportPackageReferences = new[] { trackPackage };
            EditorUtility.SetDirty(rejected);
            AssetDatabase.SaveAssets();
            Debug.Log("Controlled calibration decisions updated.");
        }

        private static V3CalibrationDecision CreateOrLoadDecision(string path)
        {
            V3CalibrationDecision decision = AssetDatabase.LoadAssetAtPath<
                V3CalibrationDecision>(path);
            if (decision != null) return decision;
            decision = ScriptableObject.CreateInstance<V3CalibrationDecision>();
            AssetDatabase.CreateAsset(decision, path);
            return decision;
        }

        private static string FindLatestEvidencePackage(
            string root, string pattern)
        {
            string fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot)) return string.Empty;
            string[] directories = Directory.GetDirectories(fullRoot, pattern);
            string latest = string.Empty;
            DateTime latestTime = DateTime.MinValue;
            for (int i = 0; i < directories.Length; i++)
            {
                DateTime time = Directory.GetLastWriteTimeUtc(directories[i]);
                if (time <= latestTime) continue;
                latestTime = time;
                latest = directories[i];
            }
            return latest;
        }

        private static V3ControlledTestDefinition CreateScenario(
            CraftBuildDefinition build,
            string assetName,
            string id,
            string displayName,
            V3ControlledTestCategory category,
            V3ControlledFixtureKind fixture,
            V3ControlledInitialCondition initial,
            float measuredSeconds,
            V3ControlledTestAssertion[] assertions,
            string comparison,
            V3ControlledCommandWindow[] commands = null)
        {
            string path = DefinitionRoot + "/" + assetName + ".asset";
            V3ControlledTestDefinition definition = AssetDatabase.LoadAssetAtPath<
                V3ControlledTestDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<
                    V3ControlledTestDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.Configure(
                id,
                displayName,
                category,
                build,
                ControlledScene,
                fixture,
                initial,
                0.25f,
                measuredSeconds,
                0.25f,
                commands ?? Array.Empty<V3ControlledCommandWindow>(),
                assertions,
                comparison);
            definition.ConfigureWorldRequirement(
                ControlledScene,
                "world.root.v3_controlleddynamics",
                V3WorldSimulationInstaller.EnsureEarthProfile());
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static V3ControlledTestAssertion Range(
            string id, string signal, float min, float max)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.NumericRange,
                signalId = signal,
                minimum = min,
                maximum = max,
                supportingChannels = new[] { signal }
            };
        }

        private static V3ControlledTestAssertion Residual(string id, float p95)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.ContactFreeResidual,
                signalId = "forces.residual",
                maximum = p95,
                supportingChannels = new[]
                {
                    "forces.residual", "world.contactCount",
                    "identity.dynamicsValid"
                }
            };
        }

        private static V3ControlledTestAssertion Reaches(
            string id, string signal, float minimum)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.SignalReachesMinimum,
                signalId = signal,
                minimum = minimum,
                supportingChannels = new[] { signal }
            };
        }

        private static V3ControlledTestAssertion Target(
            string id,
            string signal,
            float target,
            float tolerance,
            float windowStart,
            float windowEnd)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.TargetTolerance,
                signalId = signal,
                target = target,
                tolerance = tolerance,
                windowStartSeconds = windowStart,
                windowEndSeconds = windowEnd,
                supportingChannels = new[] { signal }
            };
        }

        private static V3ControlledTestAssertion Event(
            string id, V3DiagnosticEventType type, int count)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.EventOccurs,
                eventType = type,
                expectedEventCount = count
            };
        }

        private static V3ControlledTestAssertion EventWindow(
            string id,
            V3DiagnosticEventType type,
            int count,
            float start,
            float end)
        {
            V3ControlledTestAssertion assertion = Event(id, type, count);
            assertion.windowStartSeconds = start;
            assertion.windowEndSeconds = end;
            return assertion;
        }

        private static V3ControlledTestAssertion NoEvent(
            string id, V3DiagnosticEventType type)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.EventAbsent,
                eventType = type,
                expectedEventCount = 0
            };
        }

        private static V3ControlledTestAssertion NoEventWindow(
            string id,
            V3DiagnosticEventType type,
            float start,
            float end)
        {
            V3ControlledTestAssertion assertion = NoEvent(id, type);
            assertion.windowStartSeconds = start;
            assertion.windowEndSeconds = end;
            return assertion;
        }

        private static V3ControlledTestAssertion Transition(
            string id, params string[] states)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.StateTransition,
                stateSequence = states
            };
        }

        private static V3ControlledTestAssertion Deadline(
            string id, string signal, float maximum, float seconds)
        {
            return Deadline(id, signal, maximum, seconds, 0f);
        }

        private static V3ControlledTestAssertion Deadline(
            string id,
            string signal,
            float maximum,
            float seconds,
            float windowStart)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.Deadline,
                signalId = signal,
                maximum = maximum,
                deadlineSeconds = seconds,
                windowStartSeconds = windowStart
            };
        }

        private static V3ControlledTestAssertion BuildIdentity(string id)
        {
            return new V3ControlledTestAssertion
            {
                assertionId = id,
                type = V3ControlledAssertionType.BuildIdentity
            };
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }

    public static class V3ControlledDynamicsSceneGenerator
    {
        private const string PrimaryBuild =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset";

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Controlled Tests/" +
            "Generate Isolated Test Scene")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Exit Play Mode before generating the controlled scene.");
            EnsureFolder("Assets/HovercraftV3/Scenes/Diagnostics");
            CraftBuildDefinition build = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(PrimaryBuild);
            if (build == null)
                throw new InvalidOperationException("Missing primary systems build.");

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var worldObject = new GameObject("WorldSimulationRoot");
            SceneManager.MoveGameObjectToScene(worldObject, scene);
            V3WorldSimulationRoot world =
                worldObject.AddComponent<V3WorldSimulationRoot>();
            world.Configure(
                V3WorldSimulationInstaller.EnsureEarthProfile(),
                "world.root.v3_controlleddynamics");

            var spawnObject = new GameObject("Controlled Test Spawn");
            SceneManager.MoveGameObjectToScene(spawnObject, scene);
            spawnObject.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);

            var assemblerObject = new GameObject("Controlled Craft Assembler");
            SceneManager.MoveGameObjectToScene(assemblerObject, scene);
            V3CraftAssembler assembler =
                assemblerObject.AddComponent<V3CraftAssembler>();
            assembler.SetBuild(build);
            SetBoolean(assembler, "assembleOnStart", false);

            var sessionObject = new GameObject("Controlled Test Session");
            SceneManager.MoveGameObjectToScene(sessionObject, scene);
            V3FreeDriveSession session =
                sessionObject.AddComponent<V3FreeDriveSession>();
            var inputObject = new GameObject("Controlled Pilot Input Source");
            inputObject.transform.SetParent(sessionObject.transform, false);
            V3PilotInputAdapter input =
                inputObject.AddComponent<V3PilotInputAdapter>();
            session.Configure(assembler, spawnObject.transform, input);

            V3CraftTestSpawner spawner =
                sessionObject.AddComponent<V3CraftTestSpawner>();
            spawner.SelectedBuild = build;
            SetReference(spawner, "assembler", assembler);
            SetReference(spawner, "inputSource", input);
            SetReference(spawner, "spawnPoint", spawnObject.transform);
            SetReference(spawner, "session", session);

            V3DiagnosticSceneInstaller.EnsureRecorderInScene();
            EditorSceneManager.SaveScene(
                scene,
                V3ControlledTestAssetUtility.ControlledScene);
            EnsureBuildSettings(
                V3ControlledTestAssetUtility.ControlledScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Generated isolated Hovercraft V3 controlled scene: " +
                V3ControlledTestAssetUtility.ControlledScene);
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
            V3ControlledTestAssetUtility.CreateOrUpdatePresets();
        }

        private static void SetReference(
            UnityEngine.Object target,
            string property,
            UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field = serialized.FindProperty(property);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, property);
            field.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetBoolean(
            UnityEngine.Object target,
            string property,
            bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field = serialized.FindProperty(property);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, property);
            field.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void EnsureBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
                if (string.Equals(scenes[i].path, path,
                    StringComparison.OrdinalIgnoreCase))
                    return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    [InitializeOnLoad]
    public static class V3ControlledTestBatchRunner
    {
        private const string QueueKey = "V3.Controlled.Batch.Queue";
        private const string IndexKey = "V3.Controlled.Batch.Index";
        private const string ResultsKey = "V3.Controlled.Batch.Results";
        private const string BuildKey = "V3.Controlled.Batch.Build";
        private static bool transitionRequested;

        public static bool IsQueued => !string.IsNullOrEmpty(
            SessionState.GetString(QueueKey, string.Empty));

        static V3ControlledTestBatchRunner()
        {
            EditorApplication.update += Tick;
        }

        public static void QueueSelectedOrAll(
            V3ControlledTestDefinition fallback)
        {
            QueueSelectedOrAll(fallback,
                fallback != null ? fallback.Build : null);
        }

        public static void QueueSelectedOrAll(
            V3ControlledTestDefinition fallback,
            CraftBuildDefinition selectedBuild)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (selectedBuild == null)
            {
                EditorUtility.DisplayDialog(
                    "No craft selected",
                    "Choose a Craft Definition before running a controlled batch.",
                    "OK");
                return;
            }
            var definitions = new List<V3ControlledTestDefinition>();
            foreach (UnityEngine.Object item in Selection.objects)
                if (item is V3ControlledTestDefinition test)
                    definitions.Add(test);
            if (definitions.Count == 0 && fallback != null)
                definitions.Add(fallback);
            if (definitions.Count == 0)
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:V3ControlledTestDefinition",
                    new[] { V3ControlledTestAssetUtility.DefinitionRoot });
                for (int i = 0; i < guids.Length; i++)
                {
                    V3ControlledTestDefinition test =
                        AssetDatabase.LoadAssetAtPath<V3ControlledTestDefinition>(
                            AssetDatabase.GUIDToAssetPath(guids[i]));
                    if (test != null) definitions.Add(test);
                }
            }
            if (definitions.Count == 0) return;
            var paths = new string[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
                paths[i] = AssetDatabase.GetAssetPath(definitions[i]);
            SessionState.SetString(QueueKey, string.Join("|", paths));
            SessionState.SetInt(IndexKey, 0);
            SessionState.SetString(ResultsKey, string.Empty);
            SessionState.SetString(BuildKey,
                AssetDatabase.GetAssetPath(selectedBuild));
            transitionRequested = false;
            PrepareCurrent();
        }

        private static void Tick()
        {
            string queue = SessionState.GetString(QueueKey, string.Empty);
            if (string.IsNullOrEmpty(queue)) return;
            if (EditorApplication.isPlaying)
            {
                V3ControlledDynamicsTestRunner runner =
                    UnityEngine.Object.FindAnyObjectByType<
                        V3ControlledDynamicsTestRunner>(
                            FindObjectsInactive.Include);
                if (runner != null && !runner.IsRunning &&
                    runner.LastResult != null && !transitionRequested)
                {
                    string result = runner.LastResult.testId + "=" +
                        (runner.LastResult.passed ? "PASS" : "FAIL") + "@" +
                        runner.LastResult.buildId + "@" +
                        runner.LastResult.recorderOutputDirectory;
                    string previous = SessionState.GetString(
                        ResultsKey, string.Empty);
                    SessionState.SetString(ResultsKey,
                        string.IsNullOrEmpty(previous)
                            ? result : previous + "\n" + result);
                    SessionState.SetInt(IndexKey,
                        SessionState.GetInt(IndexKey, 0) + 1);
                    transitionRequested = true;
                    EditorApplication.ExitPlaymode();
                }
                return;
            }
            if (transitionRequested && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                transitionRequested = false;
                PrepareCurrent();
            }
        }

        private static void PrepareCurrent()
        {
            string[] paths = SessionState.GetString(
                QueueKey, string.Empty).Split('|');
            int index = SessionState.GetInt(IndexKey, 0);
            if (index >= paths.Length)
            {
                WriteBatchSummary(
                    SessionState.GetString(ResultsKey, string.Empty));
                SessionState.EraseString(QueueKey);
                SessionState.EraseInt(IndexKey);
                SessionState.EraseString(ResultsKey);
                SessionState.EraseString(BuildKey);
                return;
            }
            V3ControlledTestDefinition definition = AssetDatabase.LoadAssetAtPath<
                V3ControlledTestDefinition>(paths[index]);
            CraftBuildDefinition selectedBuild = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(SessionState.GetString(
                    BuildKey, string.Empty));
            if (definition == null || selectedBuild == null)
            {
                SessionState.SetInt(IndexKey, index + 1);
                PrepareCurrent();
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(
                definition.ScenePath, OpenSceneMode.Single);
            GameObject existing = GameObject.Find("[V3 Controlled Dynamics Runner]");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            var root = new GameObject("[V3 Controlled Dynamics Runner]");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<V3ControlledDynamicsTestRunner>()
                .Configure(definition, selectedBuild, true);
            EditorApplication.EnterPlaymode();
        }

        private static void WriteBatchSummary(string results)
        {
            string directory = Path.GetFullPath(
                "TestDriveReports/HovercraftV3/Controlled/Batches");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                "batch_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".md");
            File.WriteAllText(path,
                "# Hovercraft V3 Controlled Batch\n\n" +
                (results ?? string.Empty).Replace("\n", "\n\n- ").Insert(0, "- "));
            Debug.Log("Controlled batch complete: " + path);
        }
    }

    public static class V3ControlledComparisonReportWriter
    {
        public static void WriteFromCommandLine()
        {
            string raw = Environment.GetEnvironmentVariable(
                "HOVERCRAFT_V3_CONTROLLED_RESULTS");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException(
                    "HOVERCRAFT_V3_CONTROLLED_RESULTS must contain at least " +
                    "two result paths separated by '|'.");
            string[] paths = raw.Split(new[] { '|' },
                StringSplitOptions.RemoveEmptyEntries);
            string output = Write(paths);
            Debug.Log("Controlled comparison report: " + output);
        }

        public static string Write(IReadOnlyList<string> resultFiles)
        {
            if (resultFiles == null || resultFiles.Count < 2)
                throw new ArgumentException("At least two controlled results are required.");
            var results = new List<V3ControlledTestResult>(resultFiles.Count);
            var manifests = new List<V3DiagnosticSessionManifest>(
                resultFiles.Count);
            var reports = new List<V3DiagnosticAnalysisReport>(
                resultFiles.Count);
            for (int i = 0; i < resultFiles.Count; i++)
            {
                string json = File.ReadAllText(resultFiles[i]);
                V3ControlledTestResult result = JsonUtility.FromJson<
                    V3ControlledTestResult>(json);
                if (result == null)
                    throw new InvalidDataException("Invalid result: " + resultFiles[i]);
                results.Add(result);
                string package = Path.GetDirectoryName(resultFiles[i]);
                manifests.Add(ReadJson<V3DiagnosticSessionManifest>(
                    Path.Combine(package, "session_manifest.json")));
                reports.Add(ReadJson<V3DiagnosticAnalysisReport>(
                    Path.Combine(package, "report.json")));
            }
            string directory = Path.GetFullPath(
                "TestDriveReports/HovercraftV3/Controlled/Comparisons");
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory,
                "comparison_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".md");
            var text = new StringBuilder(4096);
            text.AppendLine("# Hovercraft V3 Controlled Comparison");
            text.AppendLine();
            string expectedTest = results[0].testId;
            string expectedGroup = results[0].comparisonGroupId;
            for (int i = 0; i < results.Count; i++)
            {
                V3ControlledTestResult item = results[i];
                V3DiagnosticSessionManifest manifest = manifests[i];
                V3DiagnosticAnalysisReport report = reports[i];
                text.AppendLine("## Run " + (i + 1));
                text.AppendLine();
                text.AppendLine("- Test/group: `" + item.testId + "` / `" +
                    item.comparisonGroupId + "`");
                text.AppendLine("- Build: `" + item.buildId + "` / `" +
                    item.buildGuid + "` / " + item.buildOwnership);
                text.AppendLine("- Result: " + (item.passed ? "PASS" : "FAIL"));
                text.AppendLine("- Control path: " + item.controlPath);
                text.AppendLine("- Definition version / schema: " +
                    item.testVersion + " / " +
                    (manifest != null ? manifest.schemaVersion : 0));
                text.AppendLine("- Scene / recorder profile: " +
                    (manifest != null ? manifest.sceneName : "<missing>") +
                    " / " +
                    (manifest != null ? manifest.profile : "<missing>"));
                text.AppendLine("- Calibration decision: `" +
                    (manifest != null
                        ? manifest.calibrationDecisionId
                        : string.Empty) + "`");
                text.AppendLine("- Runner overhead: " +
                    item.runnerOverheadMilliseconds.ToString("0.###") + " ms");
                text.AppendLine("- Recorder/export overhead: " +
                    (manifest != null
                        ? manifest.recordingOverheadMilliseconds.ToString("0.###") +
                          " / " + manifest.exportDurationMilliseconds.ToString("0.###")
                        : "<missing>") + " ms");
                text.AppendLine("- Dropped samples / events: " +
                    item.droppedSamples + " / " + item.droppedEvents);
                if (report != null)
                {
                    text.AppendLine("- Speed max / contact-free residual P95 / " +
                        "events: " +
                        report.maximumSpeedMetersPerSecond.ToString("0.###") +
                        " m/s / " +
                        report.contactFreeResidualP95N.ToString("0.###") +
                        " N / " + report.eventCount);
                    text.AppendLine("- Section summaries: " +
                        (report.sections != null ? report.sections.Length : 0));
                }
                text.AppendLine("- Package: `" + item.recorderOutputDirectory + "`");
                if (item.testId != expectedTest ||
                    item.testVersion != results[0].testVersion ||
                    item.comparisonGroupId != expectedGroup)
                    text.AppendLine("- WARNING: test/version comparison identity mismatch.");
                if (manifest != null &&
                    (manifest.craftBuildStableId != results[0].buildId ||
                     manifest.craftBuildAssetGuid != results[0].buildGuid))
                    text.AppendLine("- WARNING: build identity differs from baseline.");
                text.AppendLine();
            }
            text.AppendLine("## Signal and event deltas");
            text.AppendLine();
            V3DiagnosticAnalysisReport baselineReport = reports[0];
            if (baselineReport != null)
            {
                for (int i = 1; i < reports.Count; i++)
                {
                    V3DiagnosticAnalysisReport candidate = reports[i];
                    if (candidate == null)
                    {
                        text.AppendLine("- Run " + (i + 1) +
                            ": missing `report.json`.");
                        continue;
                    }
                    text.AppendLine("- Run " + (i + 1) +
                        " minus baseline: max speed " +
                        (candidate.maximumSpeedMetersPerSecond -
                         baselineReport.maximumSpeedMetersPerSecond)
                            .ToString("+0.###;-0.###;0") +
                        " m/s; residual P95 " +
                        (candidate.contactFreeResidualP95N -
                         baselineReport.contactFreeResidualP95N)
                            .ToString("+0.###;-0.###;0") +
                        " N; events " +
                        (candidate.eventCount - baselineReport.eventCount)
                            .ToString("+0;-0;0") + ".");
                }
            }
            else
            {
                text.AppendLine("- Baseline `report.json` is missing.");
            }
            text.AppendLine();
            text.AppendLine("## Assertion deltas");
            text.AppendLine();
            V3ControlledAssertionResult[] baseline = results[0].assertions ??
                Array.Empty<V3ControlledAssertionResult>();
            for (int i = 0; i < baseline.Length; i++)
            {
                text.Append("- `").Append(baseline[i].assertionId).Append("`: ")
                    .Append(baseline[i].passed ? "PASS" : "FAIL");
                for (int run = 1; run < results.Count; run++)
                {
                    V3ControlledAssertionResult match = Array.Find(
                        results[run].assertions ??
                            Array.Empty<V3ControlledAssertionResult>(),
                        value => value.assertionId == baseline[i].assertionId);
                    text.Append(" -> ").Append(match != null
                        ? (match.passed ? "PASS" : "FAIL") + " (" +
                          match.measured + ")"
                        : "MISSING");
                }
                text.AppendLine();
            }
            File.WriteAllText(output, text.ToString());
            return output;
        }

        private static T ReadJson<T>(string path) where T : class
        {
            return File.Exists(path)
                ? JsonUtility.FromJson<T>(File.ReadAllText(path))
                : null;
        }
    }

    public static class V3ControlledReportCleanupUtility
    {
        public static bool TryMeasure(
            string path,
            bool allowReportsRoot,
            out string fullPath,
            out int fileCount,
            out long byteCount,
            out string error)
        {
            fileCount = 0;
            byteCount = 0L;
            if (!TryResolveTarget(path, allowReportsRoot, out fullPath, out error))
                return false;
            if (!Directory.Exists(fullPath)) return true;
            try
            {
                string[] files = Directory.GetFiles(
                    fullPath, "*", SearchOption.AllDirectories);
                fileCount = files.Length;
                for (int i = 0; i < files.Length; i++)
                    byteCount += new FileInfo(files[i]).Length;
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not inspect report folder " + fullPath + ": " +
                    exception.Message;
                return false;
            }
        }

        public static void ClearContents(string path, bool allowReportsRoot)
        {
            if (!TryResolveTarget(path, allowReportsRoot,
                    out string fullPath, out string error))
                throw new InvalidOperationException(error);
            if (!Directory.Exists(fullPath)) return;
            string[] entries = Directory.GetFileSystemEntries(fullPath);
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i];
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    bool reparsePoint =
                        (attributes & FileAttributes.ReparsePoint) != 0;
                    Directory.Delete(entry, !reparsePoint);
                }
                else
                {
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(entry,
                            attributes & ~FileAttributes.ReadOnly);
                    File.Delete(entry);
                }
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
                return (bytes / (1024d * 1024d * 1024d)).ToString("0.##") +
                    " GiB";
            if (bytes >= 1024L * 1024L)
                return (bytes / (1024d * 1024d)).ToString("0.##") + " MiB";
            if (bytes >= 1024L)
                return (bytes / 1024d).ToString("0.##") + " KiB";
            return bytes + " bytes";
        }

        private static bool TryResolveTarget(
            string path,
            bool allowReportsRoot,
            out string fullPath,
            out string error)
        {
            string projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
            string reportsRoot = Path.GetFullPath(
                Path.Combine(projectRoot, "TestDriveReports"));
            fullPath = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path));
            error = string.Empty;
            if (string.IsNullOrEmpty(fullPath))
            {
                error = "The report cleanup path is empty.";
                return false;
            }
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            bool isReportsRoot = string.Equals(
                fullPath, reportsRoot, comparison);
            bool isChild = fullPath.StartsWith(
                reportsRoot + Path.DirectorySeparatorChar, comparison);
            if ((!allowReportsRoot && isReportsRoot) || (!isReportsRoot && !isChild))
            {
                error = "Refusing to clear a path outside the project report root: " +
                    fullPath;
                return false;
            }
            return true;
        }
    }
}
