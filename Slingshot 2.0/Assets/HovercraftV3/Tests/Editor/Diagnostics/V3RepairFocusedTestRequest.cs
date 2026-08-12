using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    [InitializeOnLoad]
    internal static class V3RepairFocusedTestRequest
    {
        // Deliberately marker-gated so normal Editor sessions never auto-run tests.
        private const string RequestPath =
            "Temp/V3RepairFocusedTests.request";
        private const string ResultPath =
            "Temp/V3RepairFocusedTests.xml";
        private const string SummaryPath =
            "Temp/V3RepairFocusedTests.summary";
        private static TestRunnerApi runner;

        static V3RepairFocusedTestRequest()
        {
            EditorApplication.delayCall += TryRun;
        }

        private static void TryRun()
        {
            if (!File.Exists(Path.GetFullPath(RequestPath)))
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }

            string requestText = File.ReadAllText(
                Path.GetFullPath(RequestPath));
            bool playSmoke = requestText.IndexOf(
                "play-smoke",
                StringComparison.OrdinalIgnoreCase) >= 0;
            string[] testNames = playSmoke
                ? new[]
                {
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3TrackTestSceneIntegrationTests." +
                    "TrackTestScene_PlayModeAdoptsCachedTrackAndPlacesCraft"
                }
                : new[]
                {
                    "TrackGeneration.Tests.TrackStartSpawnPointTests." +
                    "TrackWorldBounds_IncludeDeepGeneratedGeometry",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3PlayerFeedbackAndRecoveryTests." +
                    "FreeDriveSession_BindsAndResetsReferenceCraft",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3DiagnosticTruthAndSensorTests." +
                    "WorldTruth_ResetBoundaryDoesNotCreateTeleportAcceleration",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3DiagnosticTruthAndSensorTests." +
                    "WorldContact_UsesConfiguredEightMetreHoverEnvelope",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3DiagnosticTruthAndSensorTests." +
                    "EventDetector_UsesLocalTrackNormalForLoopRollover",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3DiagnosticTruthAndSensorTests." +
                    "EventDetector_IgnoresRoundingOnlyPowerDeficits",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3DiagnosticLedgerEventReportTests." +
                    "ReportBuilder_SplitsAttemptsAndExcludesResetDiscontinuity",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3TrackTestSceneIntegrationTests." +
                    "TrackTestScene_UsesGeneratorOwnedV3StartPose",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3HandlingControllerTests." +
                    "SurfaceTracking_ConvertsSensedNormalRotationIntoBoundedAcceleration",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3HandlingControllerTests." +
                    "RoofCapture_RespondsToSeparationWithoutVirtualAdhesion",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3HandlingControllerTests." +
                    "SystemsCraft_RoofCaptureSubmitsOnlyPhysicalThrusterRequests",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3HandlingControllerTests." +
                    "HandlingControllers_CannotApplyForcesDirectlyToRigidbody",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3ProjectReadabilitySprintTests." +
                    "PresentationPanels_CanBeCollapsedAndRestored",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3HandlingControllerTests." +
                    "FinCoefficientModel_UsesAoAStallDragAndReverseFlow",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3HandlingControllerTests." +
                    "InertiaHelpers_RoundTripTorqueAndAddDistributedMass",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3WorldClockAndHoverConfigurationTests." +
                    "CaptureEnvelope_DecaysAndFallbackCannotReachFullAuthority",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3RuntimeSystemsRepairSprintTests." +
                    "FullDynamicReset_ClearsAuthorityAndNeutralizesFirstTick",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3RuntimeSystemsRepairSprintTests." +
                    "CompleteBuild_DoesNotSilentlyUseLegacyFallback",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3CraftRegistryTests." +
                    "AuthoredVariant_DeepCopiesLocalTuningAndSharesProducts",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3DiagnosticTruthAndSensorTests." +
                    "EventDetector_UsesPhysicalTakeoffAndContactLandingVocabulary",
                    "Lunarlight.Hovercraft.V3.Tests." +
                    "V3ProjectReadabilitySprintTests." +
                    "StableIdAudit_FindsNoDuplicateAssetsOrInvalidWorldRoots"
                };

            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Callbacks());
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = testNames
            }));
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string resultPath = Path.GetFullPath(ResultPath);
                TestRunnerApi.SaveResultToFile(result, resultPath);
                File.WriteAllText(
                    Path.GetFullPath(SummaryPath),
                    $"status={result.TestStatus}{Environment.NewLine}" +
                    $"passed={result.PassCount}{Environment.NewLine}" +
                    $"failed={result.FailCount}{Environment.NewLine}" +
                    $"skipped={result.SkipCount}{Environment.NewLine}" +
                    $"inconclusive={result.InconclusiveCount}{Environment.NewLine}" +
                    $"durationSeconds={result.Duration:0.###}{Environment.NewLine}");
                string request = Path.GetFullPath(RequestPath);
                if (File.Exists(request)) File.Delete(request);
                runner = null;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed)
                    Debug.LogError(result.FullName + ": " + result.Message);
            }
        }
    }
}
