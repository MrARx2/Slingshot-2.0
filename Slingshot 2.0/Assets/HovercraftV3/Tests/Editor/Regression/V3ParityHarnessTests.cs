using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3ParityHarnessTests
    {
        private const string ProfilePath =
            "Assets/HovercraftV3/Generated/RegressionAssets/Parity/" +
            "Profiles/V2_V3_Parity_Command_Profile.asset";
        private const string ScenePath =
            "Assets/HovercraftV3/Generated/RegressionAssets/Parity/" +
            "Scenes/V2_V3_FlatGround_Parity.unity";

        [Test]
        public void GeneratedProfile_CoversReferenceMeasurementSegments()
        {
            V3ParityCommandProfile profile =
                AssetDatabase.LoadAssetAtPath<V3ParityCommandProfile>(ProfilePath);

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Segments, Has.Count.EqualTo(8));
            Assert.That(profile.TotalDurationSeconds, Is.EqualTo(78f).Within(0.001f));
            Assert.That(profile.Segments[1].Label, Is.EqualTo("Idle Hover"));
            Assert.That(profile.Segments[2].Label, Is.EqualTo("Acceleration"));
            Assert.That(profile.Segments[3].InitialForwardSpeedKmh, Is.EqualTo(500f));
            Assert.That(profile.Segments[5].InitialForwardSpeedKmh, Is.EqualTo(1000f));
        }

        [Test]
        public void CommandProfile_EvaluatesSegmentBoundariesDeterministically()
        {
            var profile = ScriptableObject.CreateInstance<V3ParityCommandProfile>();
            try
            {
                profile.SetSegments(new[]
                {
                    new V3ParityCommandSegment(
                        "One",
                        1f,
                        new V3PilotCommand { Throttle = 0.25f }),
                    new V3ParityCommandSegment(
                        "Two",
                        2f,
                        new V3PilotCommand { Throttle = 0.75f })
                });

                Assert.That(
                    profile.TryEvaluate(0.999f, out V3PilotCommand first, out int firstIndex, out _),
                    Is.True);
                Assert.That(firstIndex, Is.EqualTo(0));
                Assert.That(first.Throttle, Is.EqualTo(0.25f));

                Assert.That(
                    profile.TryEvaluate(1f, out V3PilotCommand second, out int secondIndex, out float localTime),
                    Is.True);
                Assert.That(secondIndex, Is.EqualTo(1));
                Assert.That(second.Throttle, Is.EqualTo(0.75f));
                Assert.That(localTime, Is.Zero.Within(0.0001f));
                Assert.That(profile.TryEvaluate(3f, out _, out _, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void V2Fingerprint_RejectsPlaceholderAndAcceptsReferenceMassLayout()
        {
            var root = new GameObject("Synthetic V2");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 5f;
                Assert.That(
                    V3ParityRunController.ValidateV2ReferenceFingerprint(
                        root,
                        out string mismatch),
                    Is.False);
                Assert.That(mismatch, Does.Contain("mass=5"));

                body.mass = 11000f;
                body.centerOfMass = new Vector3(0f, -0.5f, 0f);
                string[] names =
                {
                    "Hover_FL", "Hover_FR", "Hover_RL", "Hover_RR",
                    "Roof_FL", "Roof_FR", "Roof_RL", "Roof_RR",
                    "Main_Rear", "Brake_Front",
                    "Strafe_Front_Left", "Strafe_Front_Right",
                    "Strafe_Back_Left", "Strafe_Back_Right"
                };
                for (int i = 0; i < names.Length; i++)
                {
                    new GameObject(names[i]).transform.SetParent(root.transform);
                }

                Assert.That(
                    V3ParityRunController.ValidateV2ReferenceFingerprint(
                        root,
                        out string accepted),
                    Is.True,
                    accepted);
                Assert.That(accepted, Does.Contain("ForceMode.Acceleration"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Summary_DoesNotTreatPresetScenarioSpeedAsAccelerationMaximum()
        {
            var root = new GameObject("Recorder Test");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                var recorder = new V3ParityTrackRecorder("Test", body);

                body.linearVelocity = Vector3.forward * (100f / 3.6f);
                recorder.Capture(0f, "Acceleration", 0f, 10f, 9f, 0);

                body.linearVelocity = Vector3.forward * (1000f / 3.6f);
                body.angularVelocity = Vector3.up * 2f;
                recorder.Capture(1f, "Yaw Pulse 1000", 0f, 20f, 18f, 0);

                V3ParityRunSummary summary =
                    recorder.BuildSummary("Synthetic", "Test");

                Assert.That(summary.maximumSpeedKmh, Is.EqualTo(100f).Within(0.01f));
                Assert.That(
                    summary.peakAbsoluteYawRateDegreesPerSecond,
                    Is.EqualTo(2f * Mathf.Rad2Deg).Within(0.01f));
                Assert.That(summary.peakRequestedPower, Is.EqualTo(20f));
                Assert.That(summary.peakGrantedPower, Is.EqualTo(18f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ParityScene_StartsScriptedRunInPlayMode()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            yield return new EnterPlayMode();

            V3ParityRunController controller =
                Object.FindAnyObjectByType<V3ParityRunController>();
            for (int i = 0;
                i < 20 && (controller == null || !controller.IsRunning);
                i++)
            {
                yield return new WaitForFixedUpdate();
                controller = Object.FindAnyObjectByType<V3ParityRunController>();
            }

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsRunning, Is.True, controller.LastStatus);
            Assert.That(controller.LastStatus, Does.Contain("Running"));

            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Category("ParityCapture")]
        public IEnumerator ParityScene_CompletesFullProfileAndExportsTelemetry()
        {
            if (System.Environment.GetEnvironmentVariable("HOVERCRAFT_V3_RUN_PARITY_CAPTURE") != "1")
            {
                Assert.Ignore(
                    "Set HOVERCRAFT_V3_RUN_PARITY_CAPTURE=1 to run the 78-second " +
                    "real-time Gate 3 capture.");
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            yield return new EnterPlayMode();

            V3ParityRunController controller =
                Object.FindAnyObjectByType<V3ParityRunController>();
            float deadline = Time.realtimeSinceStartup + 90f;
            while ((controller == null || controller.IsRunning) &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                controller = Object.FindAnyObjectByType<V3ParityRunController>();
            }

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsRunning, Is.False, controller.LastStatus);
            Assert.That(controller.LastStatus, Does.Contain("Parity run complete"));
            Assert.That(controller.LastExportDirectory, Is.Not.Null.And.Not.Empty);
            AssertParityWithinTolerance(
                controller.LastV2Summary,
                controller.LastV3Summary);
            Assert.That(
                System.IO.Directory.GetFiles(
                    controller.LastExportDirectory,
                    "*_V2.csv",
                    System.IO.SearchOption.TopDirectoryOnly),
                Is.Not.Empty);
            Assert.That(
                System.IO.Directory.GetFiles(
                    controller.LastExportDirectory,
                    "*_V3.csv",
                    System.IO.SearchOption.TopDirectoryOnly),
                Is.Not.Empty);

            Debug.Log($"Gate 3 parity telemetry: {controller.LastExportDirectory}");
            yield return new ExitPlayMode();
        }

        private static void AssertParityWithinTolerance(
            V3ParityRunSummary v2,
            V3ParityRunSummary v3)
        {
            Assert.That(v2, Is.Not.Null);
            Assert.That(v3, Is.Not.Null);
            AssertRelative(v2.massKg, v3.massKg, 0.02f, "mass");
            Assert.That(
                Vector3.Distance(v2.centerOfMass, v3.centerOfMass),
                Is.LessThanOrEqualTo(0.05f),
                "center of mass");
            Assert.That(
                Mathf.Abs(
                    v2.idleClearanceMeanM -
                    v3.idleClearanceMeanM),
                Is.LessThanOrEqualTo(0.05f),
                "idle clearance");
            AssertRelative(
                v2.maximumSpeedKmh,
                v3.maximumSpeedKmh,
                0.02f,
                "maximum speed");
            AssertRelative(
                v2.timeTo500Kmh,
                v3.timeTo500Kmh,
                0.02f,
                "time to 500 km/h");
            AssertRelative(
                v2.timeTo1000Kmh,
                v3.timeTo1000Kmh,
                0.02f,
                "time to 1,000 km/h");
            AssertRelative(
                v2.brakeDistanceM,
                v3.brakeDistanceM,
                0.02f,
                "brake/reverse distance");
            AssertRelative(
                v2.peakAbsoluteYawRateDegreesPerSecond,
                v3.peakAbsoluteYawRateDegreesPerSecond,
                0.05f,
                "yaw response");
            AssertRelative(
                v2.peakAbsoluteSideSpeedKmh,
                v3.peakAbsoluteSideSpeedKmh,
                0.05f,
                "strafe response");
            AssertRelative(
                v2.peakAbsolutePitchDegrees,
                v3.peakAbsolutePitchDegrees,
                0.05f,
                "pitch response");
        }

        private static void AssertRelative(
            float reference,
            float actual,
            float tolerance,
            string label)
        {
            float denominator = Mathf.Max(0.0001f, Mathf.Abs(reference));
            float relativeError =
                Mathf.Abs(actual - reference) / denominator;
            Assert.That(
                relativeError,
                Is.LessThanOrEqualTo(tolerance),
                $"{label}: V2={reference:0.####}, " +
                $"V3={actual:0.####}, delta={relativeError * 100f:0.##}%");
        }
    }
}
