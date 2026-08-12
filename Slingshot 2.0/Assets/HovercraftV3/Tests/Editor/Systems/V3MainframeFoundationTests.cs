using NUnit.Framework;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3MainframeFoundationTests
    {
        [Test]
        public void ObservationBus_ReplacesOnlyMatchingTypedSource()
        {
            var bus = new V3ObservationBus();
            bus.Publish(new V3Observation(
                V3ObservationCategory.EnvironmentSurface,
                "sensor.front",
                1d,
                Vector3.up,
                Vector3.zero,
                12f,
                60f,
                1f,
                true));
            bus.Publish(new V3Observation(
                V3ObservationCategory.EnvironmentSurface,
                "sensor.front",
                2d,
                Vector3.right,
                Vector3.zero,
                8f,
                50f,
                0.8f,
                true));

            Assert.That(bus.Snapshot, Has.Count.EqualTo(1));
            Assert.That(
                bus.TryGetLatest(
                    V3ObservationCategory.EnvironmentSurface,
                    "sensor.front",
                    out V3Observation observation),
                Is.True);
            Assert.That(observation.Timestamp, Is.EqualTo(2d));
            Assert.That(observation.PrimaryScalar, Is.EqualTo(8f));
            Assert.That(observation.Confidence, Is.EqualTo(0.8f));
        }

        [Test]
        public void IntentBus_PreservesHighLevelPilotIntent()
        {
            var bus = new V3IntentBus();
            var command = new V3PilotCommand
            {
                Throttle = 0.75f,
                Strafe = -0.25f,
                StabilizationEnabled = true
            };

            bus.PublishPilotIntent(command, 4.5d);

            Assert.That(bus.HasPilotIntent, Is.True);
            Assert.That(
                bus.LatestPilotIntent.Command.Throttle,
                Is.EqualTo(0.75f));
            Assert.That(bus.LatestPilotIntent.Timestamp, Is.EqualTo(4.5d));
        }

        [Test]
        public void SystemRequest_ExpiresAtDeclaredValidityBoundary()
        {
            var request = new V3SystemRequest(
                "request.drive.1",
                "software.drive.stock",
                V3ControlDomain.Propulsion,
                V3SystemRequestType.Force,
                V3RequestFrame.ChassisLocal,
                Vector3.forward * 1000f,
                Vector3.zero,
                0f,
                0f,
                PartCapability.DriveControl,
                1f,
                1f,
                1000f,
                10,
                2d,
                0.1f,
                "Pilot throttle");

            Assert.That(request.IsValid(2.1d), Is.True);
            Assert.That(request.IsValid(2.1001d), Is.False);
        }

        [Test]
        public void DirectionalSensor_UsesSixDistinctChassisAxes()
        {
            var root = new GameObject("SensorAxes");
            try
            {
                Assert.That(
                    V3DirectionalSensorRuntime.GetWorldDirection(
                        root.transform,
                        V3SensorDirection.Front),
                    Is.EqualTo(Vector3.forward));
                Assert.That(
                    V3DirectionalSensorRuntime.GetWorldDirection(
                        root.transform,
                        V3SensorDirection.Rear),
                    Is.EqualTo(Vector3.back));
                Assert.That(
                    V3DirectionalSensorRuntime.GetWorldDirection(
                        root.transform,
                        V3SensorDirection.Left),
                    Is.EqualTo(Vector3.left));
                Assert.That(
                    V3DirectionalSensorRuntime.GetWorldDirection(
                        root.transform,
                        V3SensorDirection.Right),
                    Is.EqualTo(Vector3.right));
                Assert.That(
                    V3DirectionalSensorRuntime.GetWorldDirection(
                        root.transform,
                        V3SensorDirection.Top),
                    Is.EqualTo(Vector3.up));
                Assert.That(
                    V3DirectionalSensorRuntime.GetWorldDirection(
                        root.transform,
                        V3SensorDirection.Bottom),
                    Is.EqualTo(Vector3.down));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LegacyReferenceChassis_HasNoIntegratedMainframe()
        {
            ChassisDefinition chassis =
                UnityEditor.AssetDatabase.LoadAssetAtPath<ChassisDefinition>(
                    "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
                    "V2Parity/Definitions/" +
                    "ApexV3_V2Reference_Chassis.asset");

            Assert.That(chassis, Is.Not.Null);
            Assert.That(chassis.IntegratedMainframe, Is.Null);
            Assert.That(chassis.IntegratedSensors, Is.Empty);
        }
    }
}
