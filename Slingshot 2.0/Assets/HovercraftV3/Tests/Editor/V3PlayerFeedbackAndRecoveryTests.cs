using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3PlayerFeedbackAndRecoveryTests
    {
        private const string BuildPath =
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_V2Reference.asset";
        private const string FreeDriveScenePath =
            "Assets/HovercraftV3/FreeDrive/Scenes/V3_FreeDrive_Test.unity";

        [Test]
        public void PilotInput_UsesRebindableKeyboardAndGamepadActions()
        {
            var host = new GameObject("V3 Input Test");
            try
            {
                V3PilotInputAdapter input =
                    host.AddComponent<V3PilotInputAdapter>();

                AssertBinding(input, "Throttle", "<Keyboard>/w");
                AssertBinding(input, "Throttle", "<Gamepad>/rightTrigger");
                AssertBinding(input, "Strafe", "<Gamepad>/leftStick/x");
                AssertBinding(input, "Gamepad Look", "<Gamepad>/rightStick");
                AssertBinding(input, "Grip Breaker", "<Keyboard>/leftShift");
                AssertBinding(input, "Grip Breaker", "<Gamepad>/buttonEast");
                AssertBinding(
                    input,
                    "Emergency Overload",
                    "<Keyboard>/leftCtrl");
                AssertBinding(
                    input,
                    "Emergency Overload",
                    "<Gamepad>/buttonSouth");
                AssertBinding(input, "Cycle Power Mode", "<Keyboard>/tab");
                AssertBinding(input, "Cycle Power Mode", "<Gamepad>/dpad/up");

                string overrides = input.SaveBindingOverrides();
                Assert.That(overrides, Is.Not.Null);
                input.LoadBindingOverrides(overrides);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_V2Reference.asset",
            "Apex V3 Reference",
            "Direct-Mount / Balanced")]
        [TestCase(
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_MainGimbalDemo.asset",
            "Apex V3 Vector",
            "Rear-Gimbal Vectoring")]
        [TestCase(
            "Assets/HovercraftV3/Prototype/Builds/ApexV3_HoverSpringDemo.asset",
            "Apex V3 Terrain",
            "Front-Left Spring Hover")]
        public void PresetBuilds_ExposePlayerFacingVehicleIdentity(
            string assetPath,
            string displayName,
            string layoutName)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    assetPath);
            Assert.That(build, Is.Not.Null);
            Assert.That(build.DisplayName, Is.EqualTo(displayName));
            Assert.That(build.ManufacturerName, Is.EqualTo("Lunarlight"));
            Assert.That(build.LayoutName, Is.EqualTo(layoutName));
            Assert.That(build.Summary, Is.Not.Empty);
            Assert.That(build.DrivingNotes, Is.Not.Empty);
        }

        [Test]
        public void CockpitWarnings_ReportOverloadDeratingAndLockout()
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
                V3CockpitWarningController warnings =
                    root.GetComponent<V3CockpitWarningController>();
                V3CockpitWarningDisplay display =
                    root.GetComponent<V3CockpitWarningDisplay>();
                RuntimeThrusterInstance main =
                    router.FindThruster("Propulsion.Rear.Center");

                Assert.That(warnings, Is.Not.Null);
                Assert.That(display, Is.Not.Null);
                Assert.That(warnings.AlertLevel, Is.EqualTo(V3CockpitAlertLevel.Clear));
                Assert.That(
                    display.BuildDisplayText(),
                    Does.Contain("POWER MODE  BALANCED"));

                pipeline.Tick(
                    new V3PilotCommand
                    {
                        Throttle = 1f,
                        EmergencyOverload = true
                    },
                    1f);
                warnings.Refresh();
                Assert.That(
                    warnings.AlertLevel,
                    Is.EqualTo(V3CockpitAlertLevel.Advisory));
                Assert.That(warnings.AlertMessage, Does.Contain("OVERLOAD"));

                var overloadCommand = new V3PilotCommand
                {
                    Throttle = 1f,
                    EmergencyOverload = true
                };
                for (int i = 0; i < 80 && !main.IsAboveSafeTemperature; i++)
                {
                    pipeline.Tick(overloadCommand, 1f);
                    thermal.Tick(1f);
                }

                warnings.Refresh();
                Assert.That(main.IsAboveSafeTemperature, Is.True);
                Assert.That(
                    warnings.AlertLevel,
                    Is.EqualTo(V3CockpitAlertLevel.Warning));
                Assert.That(warnings.LowestHotOutputLimit, Is.LessThan(1f));
                Assert.That(display.BuildDisplayText(), Does.Contain("THERMAL LIMIT"));

                for (int i = 0; i < 100 && !main.IsThermallyLockedOut; i++)
                {
                    pipeline.Tick(overloadCommand, 1f);
                    thermal.Tick(1f);
                }

                warnings.Refresh();
                Assert.That(main.IsThermallyLockedOut, Is.True);
                Assert.That(
                    warnings.AlertLevel,
                    Is.EqualTo(V3CockpitAlertLevel.Critical));
                Assert.That(warnings.LockedOutPartCount, Is.EqualTo(1));
                Assert.That(display.BuildDisplayText(), Does.Contain("LOCKOUT"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SlopeRecovery_AlignsCraftTowardTwelveDegreeSurface()
        {
            SimulationMode previousMode = Physics.simulationMode;
            GameObject ground = null;
            GameObject host = null;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                const float slopeDegrees = 12f;
                Quaternion slopeRotation =
                    Quaternion.Euler(0f, 0f, slopeDegrees);
                Vector3 slopeNormal = slopeRotation * Vector3.up;

                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "V3 12 Degree Validation Slope";
                ground.layer = 8;
                ground.transform.rotation = slopeRotation;
                ground.transform.localScale = new Vector3(200f, 1f, 200f);
                ground.transform.position = new Vector3(0f, -0.5f, 0f);

                host = BuildReferenceCraft(out V3CraftAssembler assembler);
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3HoverController hover =
                    root.GetComponent<V3HoverController>();

                Vector3 surfacePoint =
                    ground.transform.TransformPoint(Vector3.up * 0.5f);
                body.position = surfacePoint + Vector3.up * 3.405f;
                body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();

                float initialError =
                    Vector3.Angle(root.transform.up, slopeNormal);
                for (int i = 0; i < 250; i++)
                {
                    pipeline.Tick(
                        new V3PilotCommand { StabilizationEnabled = true },
                        0.02f);
                    Physics.Simulate(0.02f);
                }

                float finalError =
                    Vector3.Angle(root.transform.up, slopeNormal);
                Assert.That(hover.IsGrounded, Is.True);
                Assert.That(initialError, Is.EqualTo(slopeDegrees).Within(0.01f));
                Assert.That(finalError, Is.LessThan(initialError));
                Assert.That(finalError, Is.LessThan(initialError * 0.6f));
            }
            finally
            {
                Physics.simulationMode = previousMode;
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }

                if (ground != null)
                {
                    Object.DestroyImmediate(ground);
                }
            }
        }

        [Test]
        public void AirborneRecovery_RejectsFalseGroundAndProvidesLiftAuthority()
        {
            SimulationMode previousMode = Physics.simulationMode;
            GameObject host = null;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                host = BuildReferenceCraft(out V3CraftAssembler assembler);
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3ControllerPipeline pipeline =
                    root.GetComponent<V3ControllerPipeline>();
                V3HoverController hover =
                    root.GetComponent<V3HoverController>();

                body.position = Vector3.up * 15f;
                body.rotation = Quaternion.Euler(0f, 0f, 15f);
                body.linearVelocity = Vector3.down * 10f;
                body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();

                for (int i = 0; i < 25; i++)
                {
                    pipeline.Tick(
                        new V3PilotCommand
                        {
                            Lift = 1f,
                            StabilizationEnabled = true
                        },
                        0.02f);
                    Physics.Simulate(0.02f);
                }

                Assert.That(hover.IsGrounded, Is.False);
                Assert.That(hover.GroundedProbeCount, Is.Zero);
                Assert.That(body.linearVelocity.y, Is.GreaterThan(-10f));
            }
            finally
            {
                Physics.simulationMode = previousMode;
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void FreeDriveSession_BindsAndResetsReferenceCraft()
        {
            GameObject host = null;
            GameObject spawn = null;
            GameObject sessionObject = null;
            try
            {
                host = BuildReferenceCraft(out V3CraftAssembler assembler);
                spawn = new GameObject("Free Drive Test Spawn");
                spawn.transform.SetPositionAndRotation(
                    new Vector3(12f, 6f, -18f),
                    Quaternion.Euler(0f, 35f, 0f));
                sessionObject = new GameObject("Free Drive Test Session");
                V3FreeDriveSession session =
                    sessionObject.AddComponent<V3FreeDriveSession>();
                var serialized = new SerializedObject(session);
                serialized.FindProperty("assembler").objectReferenceValue =
                    assembler;
                serialized.FindProperty("spawnPoint").objectReferenceValue =
                    spawn.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(session.RefreshCraftBinding(), Is.True);
                Assert.That(session.ThrusterDebug, Is.Not.Null);
                Assert.That(session.ToggleThrusterDebug(), Is.True);
                Assert.That(
                    session.ThrusterDebug.ShowVisualization,
                    Is.True);
                Assert.That(session.ToggleThrusterDebug(), Is.False);
                Rigidbody body = session.CraftBody;
                body.position = new Vector3(-30f, -5f, 90f);
                body.rotation = Quaternion.Euler(25f, 70f, -15f);
                body.linearVelocity = new Vector3(20f, -8f, 35f);
                body.angularVelocity = new Vector3(1f, 2f, 3f);

                Assert.That(session.ResetCraft("TEST RESET"), Is.True);
                Assert.That(session.ResetSequence, Is.EqualTo(1));
                Assert.That(
                    Vector3.Distance(
                        body.position,
                        spawn.transform.position + Vector3.up * 0.15f),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(body.rotation, spawn.transform.rotation),
                    Is.LessThan(0.01f));
                Assert.That(
                    body.linearVelocity.sqrMagnitude,
                    Is.LessThan(0.0001f));
                Assert.That(
                    body.angularVelocity.sqrMagnitude,
                    Is.LessThan(0.0001f));
            }
            finally
            {
                if (sessionObject != null)
                {
                    Object.DestroyImmediate(sessionObject);
                }

                if (spawn != null)
                {
                    Object.DestroyImmediate(spawn);
                }

                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }
            }
        }

        [Test]
        public void FreeDriveScene_HasPlayableCraftCameraHudAndCourse()
        {
            Scene scene = default;
            try
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        FreeDriveScenePath),
                    Is.Not.Null);
                scene = EditorSceneManager.OpenScene(
                    FreeDriveScenePath,
                    OpenSceneMode.Additive);

                V3FreeDriveSession session =
                    FindInScene<V3FreeDriveSession>(scene);
                Assert.That(session, Is.Not.Null);
                Assert.That(session.Assembler, Is.Not.Null);
                Assert.That(
                    FindInScene<V3FreeDriveCamera>(scene),
                    Is.Not.Null);
                Assert.That(
                    FindInScene<V3FreeDriveHud>(scene),
                    Is.Not.Null);

                GameObject course = FindRoot(
                    scene,
                    "Free Drive Handling Course");
                Assert.That(course, Is.Not.Null);
                Transform ground = FindChild(
                    course.transform,
                    "TrackSurface Test Field");
                Assert.That(ground, Is.Not.Null);
                Assert.That(ground.gameObject.layer, Is.EqualTo(8));
                Assert.That(
                    FindChild(course.transform, "Start Gate"),
                    Is.Not.Null);
                Assert.That(
                    FindChild(course.transform, "Finish Gate"),
                    Is.Not.Null);
                Assert.That(
                    FindChild(course.transform, "TrackSurface Jump Ramp"),
                    Is.Not.Null);
                Assert.That(
                    FindChild(
                        course.transform,
                        "TrackSurface Banked Stability Deck"),
                    Is.Not.Null);
                Assert.That(
                    FindChild(
                        course.transform,
                        "TrackSurface Washboard Ridge 01"),
                    Is.Not.Null);
                Assert.That(
                    FindChild(
                        course.transform,
                        "TrackSurface Articulation Left 01"),
                    Is.Not.Null);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void AssertBinding(
            V3PilotInputAdapter input,
            string actionName,
            string path)
        {
            InputAction action = input.FindAction(actionName);
            Assert.That(action, Is.Not.Null, actionName);
            Assert.That(
                action.bindings.Any(binding => binding.effectivePath == path),
                Is.True,
                $"{actionName} is missing {path}");
        }

        private static GameObject BuildReferenceCraft(
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(BuildPath);
            var host = new GameObject("V3 Gate 7 Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }

        private static T FindInScene<T>(Scene scene)
            where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] children =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
