using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3HandlingControllerTests
    {
        [Test]
        public void HandlingControllers_CannotApplyForcesDirectlyToRigidbody()
        {
            string runtimePath = Path.Combine(
                Application.dataPath,
                "HovercraftV3",
                "Runtime");
            string[] controllerFiles =
            {
                "Systems/Drive/V3DriveController.cs",
                "Systems/Hover/V3HoverController.cs",
                "Systems/Stabilization/V3VectorController.cs",
                "Systems/Traction/V3TractionController.cs",
                "Systems/Stabilization/V3GimbalController.cs",
                "Core/Routing/V3ControllerPipeline.cs"
            };

            for (int i = 0; i < controllerFiles.Length; i++)
            {
                string source = File.ReadAllText(
                    Path.Combine(runtimePath, controllerFiles[i]));
                Assert.That(
                    source,
                    Does.Not.Contain(".AddForce"),
                    $"{controllerFiles[i]} bypasses installed actuators.");
                Assert.That(
                    source,
                    Does.Not.Contain(".AddTorque"),
                    $"{controllerFiles[i]} bypasses installed actuators.");
            }
        }

        [Test]
        public void SurfaceAlignment_ProducesBoundedRollPitchCorrectionWithoutYaw()
        {
            Vector3 tiltedUp =
                Quaternion.AngleAxis(10f, Vector3.forward) * Vector3.up;

            Vector3 correction =
                V3HoverController.CalculateSurfaceAlignmentAcceleration(
                    tiltedUp,
                    Vector3.up,
                    Vector3.zero,
                    4f,
                    0.8f,
                    12f,
                    1f);

            Assert.That(correction.magnitude, Is.GreaterThan(0f));
            Assert.That(correction.magnitude, Is.LessThanOrEqualTo(12.0001f));
            Assert.That(
                Vector3.Dot(correction, tiltedUp),
                Is.Zero.Within(0.0001f));
        }

        [Test]
        public void SurfaceAlignment_DampsExistingRollPitchRate()
        {
            Vector3 correction =
                V3HoverController.CalculateSurfaceAlignmentAcceleration(
                    Vector3.up,
                    Vector3.up,
                    Vector3.right * 2f,
                    4f,
                    0.8f,
                    12f,
                    1f);

            Assert.That(correction.x, Is.EqualTo(-1.6f).Within(0.0001f));
            Assert.That(correction.y, Is.Zero.Within(0.0001f));
            Assert.That(correction.z, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void HoverFallback_ProjectsMissedProbeOntoDetectedSurface()
        {
            bool found = V3HoverController.TryCalculateSurfacePlaneDistance(
                new Vector3(3f, 4f, -2f),
                Vector3.down,
                Vector3.zero,
                Vector3.up,
                7f,
                out float distance);

            Assert.That(found, Is.True);
            Assert.That(distance, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void HoverFallback_RejectsSurfaceBehindProbe()
        {
            bool found = V3HoverController.TryCalculateSurfacePlaneDistance(
                Vector3.up,
                Vector3.up,
                Vector3.zero,
                Vector3.up,
                7f,
                out _);

            Assert.That(found, Is.False);
        }

        [Test]
        public void HoverDamping_IgnoresForwardSpeedParallelToSurface()
        {
            float surfaceVelocity =
                V3HoverController.CalculateSurfaceNormalVelocity(
                    new Vector3(0f, 0f, 1800f),
                    Vector3.up);

            Assert.That(surfaceVelocity, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void HoverDamping_UsesVelocityAlongDetectedSurfaceNormal()
        {
            Vector3 normal = new Vector3(0f, 1f, 1f).normalized;
            float surfaceVelocity =
                V3HoverController.CalculateSurfaceNormalVelocity(
                    normal * 7.5f + Vector3.right * 100f,
                    normal);

            Assert.That(surfaceVelocity, Is.EqualTo(7.5f).Within(0.0001f));
        }

        [Test]
        public void SurfaceTracking_ConvertsSensedNormalRotationIntoBoundedAcceleration()
        {
            Vector3 currentNormal =
                Quaternion.AngleAxis(-5f, Vector3.right) * Vector3.up;
            float acceleration =
                V3HoverController.CalculateSurfaceTrackingAcceleration(
                    Vector3.forward * 360f,
                    Vector3.up,
                    currentNormal,
                    0.01f,
                    1.1f,
                    340f);

            Assert.That(acceleration, Is.EqualTo(340f).Within(0.001f));
            Assert.That(
                V3HoverController.CalculateSurfaceTrackingAcceleration(
                    Vector3.forward * 360f,
                    Vector3.up,
                    Quaternion.AngleAxis(5f, Vector3.right) * Vector3.up,
                    0.01f,
                    1.1f,
                    340f),
                Is.EqualTo(-340f).Within(0.001f));
            Assert.That(
                V3HoverController.CalculateSurfaceTrackingAcceleration(
                    Vector3.forward * 360f,
                    Vector3.up,
                    Vector3.up,
                    0.01f,
                    1.1f,
                    340f),
                Is.Zero.Within(0.0001f));
        }

        [Test]
        public void RoofCapture_RespondsToSeparationWithoutVirtualAdhesion()
        {
            float acceleration =
                V3HoverController.CalculateRoofCaptureAcceleration(
                    20f,
                    8f,
                    10f,
                    7f,
                    5f,
                    0.35f,
                    180f);

            Assert.That(acceleration, Is.EqualTo(131.55f).Within(0.001f));
            Assert.That(
                V3HoverController.CalculateRoofCaptureAcceleration(
                    8.2f,
                    8f,
                    -10f,
                    7f,
                    5f,
                    0.35f,
                    180f),
                Is.Zero.Within(0.0001f));
        }

        [Test]
        public void SystemsCraft_RoofCaptureSubmitsOnlyPhysicalThrusterRequests()
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "SystemsIntegration/Builds/" +
                    "ApexV3_SystemsIntegration.asset");
            var host = new GameObject("Roof Capture Assembly Host");
            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                surface.name = "Physical Track Surface";
                surface.layer = 8;
                surface.transform.position = new Vector3(0f, -0.5f, 0f);
                surface.transform.localScale = new Vector3(200f, 1f, 200f);

                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue = build;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(assembler.Rebuild(), Is.True);

                GameObject root = assembler.AssembledRoot;
                root.transform.position = Vector3.up * 24f;
                Rigidbody body = root.GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.up * 8f;
                Vector3 velocityBefore = body.linearVelocity;
                Physics.SyncTransforms();

                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                V3HoverController hover =
                    root.GetComponent<V3HoverController>();
                router.BeginFrame();
                hover.Submit(new V3PilotCommand
                {
                    StabilizationEnabled = true
                }, 0.01f);

                Assert.That(hover.SurfaceDetected, Is.True);
                Assert.That(hover.IsNearSurface, Is.False,
                    "A surface in the decaying capture envelope is not near-hover truth.");
                Assert.That(hover.CaptureAuthorityMultiplier,
                    Is.GreaterThan(0f));
                Assert.That(hover.RoofCaptureOutput, Is.GreaterThan(0f));
                Assert.That(body.linearVelocity, Is.EqualTo(velocityBefore));

                var snapshots = new V3ActuatorRouterEntrySnapshot[32];
                int count = router.CopyEntrySnapshots(snapshots);
                int activeRoofThrusters = 0;
                for (int i = 0; i < count; i++)
                {
                    if (snapshots[i].SocketId.StartsWith("Control.") &&
                        snapshots[i].SocketId.EndsWith(".Top") &&
                        snapshots[i].Stabilization > 0f)
                    {
                        activeRoofThrusters++;
                    }
                }
                Assert.That(activeRoofThrusters, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Vectoring_SeparatesPilotYawFromPhysicalRateDampingIntent()
        {
            float demand = V3VectorController.CalculateYawDemand(
                0.65f,
                0.31525f,
                2.8f);
            float damping =
                V3VectorController.CalculateYawDampingAcceleration(2f, 5.35f);

            Assert.That(demand, Is.EqualTo(0.573755f).Within(0.0001f));
            Assert.That(damping, Is.EqualTo(-10.7f).Within(0.0001f));
        }

        [Test]
        public void Vectoring_StrafeCalibrationPreservesReferenceForce()
        {
            float demand =
                V3VectorController.CalculateStrafeDemand(1f, 0.19f);

            Assert.That(demand, Is.EqualTo(0.19f).Within(0.0001f));
            Assert.That(
                demand * 800000f,
                Is.EqualTo(0.95f * 160000f).Within(1f));
        }

        [Test]
        public void ActuatorAllocator_ProducesYawTorqueFromBalancedStrafeThrusters()
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "V2Parity/Builds/" +
                    "ApexV3_V2Reference.asset");
            var host = new GameObject("Physical Allocation Test Host");
            try
            {
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue = build;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(assembler.Rebuild(), Is.True);

                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3ActuatorCommandRouter router =
                    root.GetComponent<V3ActuatorCommandRouter>();
                string[] strafeSockets =
                {
                    "Strafe.Left.Front",
                    "Strafe.Right.Front",
                    "Strafe.Left.Rear",
                    "Strafe.Right.Rear"
                };

                router.BeginFrame();
                var result = router.SubmitWrench(
                    strafeSockets,
                    V3ActuatorChannel.Stabilization,
                    Vector3.zero,
                    root.transform.up * 250000f,
                    V3ActuatorPriority.Stabilization,
                    1f,
                    1f);
                router.Resolve(0.02f);

                Assert.That(result.AllocatedTorqueNm.y, Is.GreaterThan(0f));
                Assert.That(
                    Mathf.Abs(result.AllocatedForceN.x),
                    Is.LessThan(100f));

                int firingCount = 0;
                Vector3 physicalForce = Vector3.zero;
                Vector3 physicalTorque = Vector3.zero;
                for (int i = 0; i < strafeSockets.Length; i++)
                {
                    RuntimeThrusterInstance thruster =
                        router.FindThruster(strafeSockets[i]);
                    if (thruster.CurrentAppliedForceN <= 0f)
                    {
                        continue;
                    }

                    firingCount++;
                    Vector3 origin = thruster.transform.TransformPoint(
                        thruster.ThrusterDefinition.LocalForceOrigin);
                    physicalForce += thruster.CurrentWorldForce;
                    physicalTorque += Vector3.Cross(
                        origin - body.worldCenterOfMass,
                        thruster.CurrentWorldForce);
                }

                Assert.That(firingCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(Mathf.Abs(physicalForce.x), Is.LessThan(100f));
                Assert.That(physicalTorque.y, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AngularAcceleration_IsConvertedThroughTheRigidbodyInertiaTensor()
        {
            var root = new GameObject("Inertia Conversion Test");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.inertiaTensor = new Vector3(10f, 20f, 30f);
                body.inertiaTensorRotation = Quaternion.identity;
                body.angularVelocity = Vector3.zero;

                Vector3 torque =
                    V3ActuatorCommandRouter.CalculateRequiredWorldTorque(
                        body,
                        new Vector3(2f, 3f, 4f));

                Assert.That(torque.x, Is.EqualTo(20f).Within(0.0001f));
                Assert.That(torque.y, Is.EqualTo(60f).Within(0.0001f));
                Assert.That(torque.z, Is.EqualTo(120f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FinCoefficientModel_UsesAoAStallDragAndReverseFlow()
        {
            V3AerodynamicFinDefinition fin =
                ScriptableObject.CreateInstance<V3AerodynamicFinDefinition>();
            try
            {
                float zero = fin.EvaluateLiftCoefficient(0f);
                float positive = fin.EvaluateLiftCoefficient(10f);
                float negative = fin.EvaluateLiftCoefficient(-10f);
                float atStall = fin.EvaluateLiftCoefficient(18f);
                float postStall = fin.EvaluateLiftCoefficient(60f);
                float reverse = fin.EvaluateLiftCoefficient(150f);

                Assert.That(zero, Is.Zero.Within(0.0001f));
                Assert.That(positive, Is.GreaterThan(0f));
                Assert.That(negative, Is.EqualTo(-positive).Within(0.0001f));
                Assert.That(postStall, Is.LessThan(atStall));
                Assert.That(Mathf.Abs(reverse), Is.LessThan(Mathf.Abs(postStall)));
                Assert.That(
                    fin.EvaluateDragCoefficient(60f, postStall),
                    Is.GreaterThan(fin.EvaluateDragCoefficient(0f, zero)));
            }
            finally
            {
                Object.DestroyImmediate(fin);
            }
        }

        [Test]
        public void InertiaHelpers_RoundTripTorqueAndAddDistributedMass()
        {
            var root = new GameObject("Inertia Round Trip Test");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.inertiaTensor = new Vector3(10f, 20f, 30f);
                body.inertiaTensorRotation = Quaternion.identity;
                body.angularVelocity = Vector3.zero;
                Vector3 acceleration = new Vector3(2f, 3f, 4f);
                Vector3 torque =
                    V3ActuatorCommandRouter.CalculateRequiredWorldTorque(
                        body,
                        acceleration);
                Vector3 recovered = V3ActuatorCommandRouter
                    .CalculateExpectedWorldAngularAcceleration(body, torque);
                Assert.That((recovered - acceleration).magnitude,
                    Is.LessThan(0.0001f));

                var contributions = new[]
                {
                    new V3MassContribution(
                        "Left", "part.left", "Left", 5f,
                        new Vector3(-2f, 0f, 0f)),
                    new V3MassContribution(
                        "Right", "part.right", "Right", 5f,
                        new Vector3(2f, 0f, 0f))
                };
                V3DistributedInertiaResult distributed =
                    V3DistributedInertiaCalculator.Calculate(
                        100f,
                        new Vector3(2f, 1f, 4f),
                        Vector3.zero,
                        Vector3.zero,
                        contributions);
                Assert.That(distributed.InstalledPointMassTensor.y,
                    Is.EqualTo(40f).Within(0.0001f));
                Assert.That(distributed.InstalledPointMassTensor.z,
                    Is.EqualTo(40f).Within(0.0001f));
                Assert.That(distributed.CombinedTensor.y,
                    Is.GreaterThan(distributed.ChassisTensor.y));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Traction_CoastsLaterallyAndLongitudinally()
        {
            Vector3 acceleration = CalculateTraction(
                new Vector3(10f, 0f, 20f),
                0f);

            Assert.That(acceleration.x, Is.EqualTo(-80f).Within(0.0001f));
            Assert.That(acceleration.z, Is.EqualTo(-6f).Within(0.0001f));
        }

        [Test]
        public void Traction_OnlyAddsLongitudinalGripAgainstTrajectory()
        {
            Vector3 forward = CalculateTraction(
                new Vector3(10f, 0f, 20f),
                1f);
            Vector3 braking = CalculateTraction(
                new Vector3(10f, 0f, 20f),
                -1f);

            Assert.That(forward.z, Is.Zero.Within(0.0001f));
            Assert.That(braking.z, Is.EqualTo(-120f).Within(0.0001f));
        }

        private static Vector3 CalculateTraction(
            Vector3 localVelocity,
            float signedThrottle)
        {
            return V3TractionController.CalculateLocalGripAcceleration(
                localVelocity,
                signedThrottle,
                false,
                0f,
                8f,
                7f,
                0.3f,
                0.4f,
                1f,
                120f,
                0.2f);
        }
    }
}
