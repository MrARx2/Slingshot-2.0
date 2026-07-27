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
                "Runtime",
                "Runtime");
            string[] controllerFiles =
            {
                "V3DriveController.cs",
                "V3HoverController.cs",
                "V3VectorController.cs",
                "V3TractionController.cs",
                "V3GimbalController.cs",
                "V3ControllerPipeline.cs"
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
                    "Assets/HovercraftV3/Prototype/Builds/" +
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
