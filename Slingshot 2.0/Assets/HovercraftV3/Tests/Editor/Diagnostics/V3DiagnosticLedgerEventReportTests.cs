using System;
using System.IO;
using Lunarlight.Hovercraft.V3.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3DiagnosticLedgerEventReportTests
    {
        [Test]
        public void ForceLedger_ReconcilesKnownPhysicalForcesWithObservedAcceleration()
        {
            var source = new V3ForceTorqueLedger();
            source.Initialize(null);
            V3DiagnosticSample sample = PreparedSample();
            sample.identity.fixedDelta = 0.02f;
            sample.world.gravityVector = new Vector3(0f, -10f, 0f);
            sample.world.gravityForce = new Vector3(0f, -10f, 0f);
            sample.world.linearAcceleration = new Vector3(0f, -10f, 20f);
            sample.deviceCount = 1;
            sample.devices[0] = new V3DeviceDiagnosticRecord
            {
                deviceType = "Thruster",
                socketId = "Propulsion.Rear.Center",
                actualForce = Vector3.forward * 20f,
                torqueContribution = Vector3.zero
            };

            source.Capture(ref sample);

            Assert.That(sample.forces.propulsionForce,
                Is.EqualTo(Vector3.forward * 20f));
            Assert.That(sample.forces.expectedNetForce,
                Is.EqualTo(new Vector3(0f, -10f, 20f)));
            Assert.That(sample.forces.residualForce.sqrMagnitude,
                Is.LessThan(0.000001f));
        }

        [Test]
        public void EventDetector_RecordsTransitionWithRollingSampleContext()
        {
            V3DiagnosticSession session = CreateSession(4);
            var detector = new V3DiagnosticEventDetector();
            detector.Initialize(null);
            detector.OnSessionStarted(session);
            V3DiagnosticSample first = PreparedSample();
            first.identity.sampleIndex = 0;
            first.identity.physicsTick = 0;
            first.worldContactState = V3WorldContactState.HoveringNearSurface;
            detector.Capture(ref first);
            V3DiagnosticSample second = PreparedSample();
            second.identity.sampleIndex = 1;
            second.identity.physicsTick = 1;
            second.identity.sessionElapsed = 0.02d;
            second.worldContactState = V3WorldContactState.AirborneUnexpected;
            second.track.sectionId = "section_001_road_0";

            detector.Capture(ref second);

            Assert.That(session.EventCount, Is.GreaterThanOrEqualTo(2));
            bool foundUnexpected = false;
            for (int i = 0; i < session.EventCount; i++)
            {
                V3DiagnosticEvent item = session.Events[i];
                if (item.type != V3DiagnosticEventType.UnexpectedAirborne) continue;
                foundUnexpected = true;
                Assert.That(item.preEventStartSample, Is.EqualTo(0));
                Assert.That(item.postEventEndSample, Is.GreaterThan(item.sampleIndex));
                Assert.That(item.possibleContributingDomains, Is.Not.Empty);
            }
            Assert.That(foundUnexpected, Is.True);
        }

        [Test]
        public void ReportBuilder_ProducesSectionDeviceSystemSensorAndForceEvidence()
        {
            V3DiagnosticSession session = CreateSession(2);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(session.TryBeginSample(out int index), Is.True);
                ref V3DiagnosticSample sample = ref session.GetSample(index);
                sample.identity.sampleIndex = i;
                sample.identity.fixedDelta = 0.02f;
                sample.identity.sessionElapsed = i * 0.02d;
                sample.identity.dynamicsValid = true;
                sample.track.mapped = true;
                sample.track.sectionId = "section_000_road_0";
                sample.track.sectionType = "Straight";
                sample.track.trueSurfaceDistance = 3f;
                sample.world.linearVelocity = Vector3.forward * (10f + i);
                sample.world.gravityVector = Physics.gravity;
                sample.belief.hoverGrounded = true;
                sample.worldContactState = V3WorldContactState.HoveringNearSurface;
                sample.deviceCount = 1;
                sample.devices[0] = new V3DeviceDiagnosticRecord
                {
                    deviceId = "drive", deviceType = "Thruster",
                    role = "Propulsion", requestedOutput = 1f,
                    actualOutput = 0.8f, grantFraction = 0.8f,
                    actualForceMagnitudeN = 100f, temperatureC = 40f
                };
                sample.taskCount = 1;
                sample.tasks[0] = new V3TaskDiagnosticRecord
                {
                    taskId = "drive-task", role = "Drive",
                    requestedRateHz = 50f, grantedRateHz = 50f,
                    measuredRateHz = 50f, minimumUsefulRateHz = 20f
                };
                sample.sensorCount = 1;
                sample.sensors[0] = new V3SensorDiagnosticRecord
                {
                    sensorId = "bottom", direction = "Bottom",
                    hasSample = true, hit = true, trueHit = true,
                    comparisonQuality = V3DiagnosticDataQuality.High,
                    distanceError = 0.1f, normalAngleError = 2f
                };
                sample.forces.residualForce = Vector3.right * i;
            }
            for (int i = 0; i < 2; i++)
            {
                Assert.That(session.TryAddEvent(new V3DiagnosticEvent
                {
                    eventId = "repeat_" + i,
                    type = V3DiagnosticEventType.SensorWorldDisagreement,
                    severity = 2,
                    trackDistance = 14f + i,
                    sectionId = "section_000_road_0"
                }), Is.True);
            }
            session.Complete(0.04d, 0.1d);

            V3DiagnosticAnalysisReport report =
                V3DiagnosticReportBuilder.Build(session);

            Assert.That(report.sections, Has.Length.EqualTo(1));
            Assert.That(report.devices, Has.Length.EqualTo(1));
            Assert.That(report.systems, Has.Length.EqualTo(1));
            Assert.That(report.sensors, Has.Length.EqualTo(1));
            Assert.That(report.forces.Length, Is.GreaterThan(5));
            Assert.That(report.repeatedLocationClusters, Has.Length.EqualTo(1));
            Assert.That(report.repeatedLocationClusters[0].eventCount,
                Is.EqualTo(2));
            Assert.That(report.executiveSummary, Does.Contain("not causes"));
        }

        [Test]
        public void ReportBuilder_SplitsAttemptsAndExcludesResetDiscontinuity()
        {
            V3DiagnosticSession session = CreateSession(4);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(session.TryBeginSample(out int index), Is.True);
                ref V3DiagnosticSample sample = ref session.GetSample(index);
                sample.identity.sampleIndex = i;
                sample.identity.fixedDelta = 0.02f;
                sample.identity.sessionElapsed = i * 0.02d;
                sample.identity.attemptIndex = i < 2 ? 0 : 1;
                sample.identity.externalStateDiscontinuity = i == 2;
                sample.identity.dynamicsValid = i != 2;
                sample.track.sectionId = "section_000_road_0";
                sample.track.trueSurfaceDistance = 8f;
                sample.world.gravityVector = Physics.gravity;
                sample.world.linearVelocity = Vector3.forward * 10f;
                sample.worldContactState =
                    V3WorldContactState.HoveringNearSurface;
                sample.belief.hoverGrounded = true;
                sample.forces.residualForce = i == 2
                    ? Vector3.right * 1000000f
                    : Vector3.right * 5f;
            }
            session.Complete(0.08d, 0d);

            V3DiagnosticAnalysisReport report =
                V3DiagnosticReportBuilder.Build(session);

            Assert.That(report.attempts, Has.Length.EqualTo(2));
            Assert.That(report.attempts[1].discontinuitySamples,
                Is.EqualTo(1));
            Assert.That(report.maximumResidualForceN,
                Is.EqualTo(5f).Within(0.001f));
            V3DiagnosticForceSummary residual = Array.Find(
                report.forces,
                value => value.category == "residual");
            Assert.That(residual.peakMagnitudeN,
                Is.EqualTo(5f).Within(0.001f));
            Assert.That(residual.sampleCount, Is.EqualTo(3));
        }

        [Test]
        public void KnownMismatchBenchmark_ExportsEvidencePackageWithoutClaimingCause()
        {
            V3DiagnosticSession session = CreateSession(3);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(session.TryBeginSample(out int index), Is.True);
                ref V3DiagnosticSample sample = ref session.GetSample(index);
                sample.identity.sampleIndex = i;
                sample.identity.physicsTick = i;
                sample.identity.fixedDelta = 0.02f;
                sample.identity.sessionElapsed = i * 0.02d;
                sample.identity.dynamicsValid = true;
                sample.track.mapped = true;
                sample.track.distanceAlongTrack = 1864.2f + i * 0.1f;
                sample.track.sectionId = "section_S06";
                sample.track.sectionType = "Straight";
                sample.track.trueSurfaceDistance = 2.4f;
                sample.world.linearVelocity = Vector3.forward * 40f;
                sample.world.linearAcceleration = new Vector3(0f, -6.8f, 0f);
                sample.world.gravityVector = Physics.gravity;
                sample.world.gravityForce = Physics.gravity * 1000f;
                sample.worldContactState =
                    V3WorldContactState.AirborneUnexpected;
                sample.belief.hoverGrounded = true;
                sample.sensorCount = 1;
                sample.sensors[0] = new V3SensorDiagnosticRecord
                {
                    sensorId = "bottom",
                    direction = "Bottom",
                    hasSample = true,
                    hit = true,
                    trueHit = true,
                    ageSeconds = 0.24f,
                    measuredDistance = 0.72f,
                    trueDistance = 2.4f,
                    distanceError = -1.68f,
                    comparisonQuality = V3DiagnosticDataQuality.High
                };
                sample.forces.hoverForce = Vector3.up * 89000f;
                sample.forces.aeroLiftToWeight = 0.18f;
            }
            for (int i = 0; i < 2; i++)
            {
                Assert.That(session.TryAddEvent(new V3DiagnosticEvent
                {
                    eventId = "known_mismatch_" + i,
                    type = V3DiagnosticEventType.SensorWorldDisagreement,
                    severity = 2,
                    sampleIndex = i,
                    tick = i,
                    trackDistance = 1864.2f + i * 0.1f,
                    sectionId = "section_S06",
                    worldState = V3WorldContactState.AirborneUnexpected,
                    craftBeliefState = "Grounded",
                    possibleContributingDomains =
                        "sensor,world_truth,observation_bus",
                    preEventStartSample = 0,
                    postEventEndSample = 2,
                    notes = "Known test mismatch; evidence label only."
                }), Is.True);
            }
            session.Complete(0.06d, 0.1d);

            string root = Path.Combine(Application.temporaryCachePath,
                "V3KnownMismatch_" + Guid.NewGuid().ToString("N"));
            try
            {
                V3DiagnosticExportResult result = V3DiagnosticExportService.Export(
                    session, root, new V3DiagnosticExportOptions());
                Assert.That(result.success, Is.True, result.error);
                string report = File.ReadAllText(
                    Path.Combine(result.outputDirectory, "report.md"));
                Assert.That(report, Does.Contain("3 samples disagreed"));
                Assert.That(report, Does.Contain("section_S06"));
                Assert.That(report, Does.Contain("do not assert causation"));
                Assert.That(File.ReadAllLines(Path.Combine(
                    result.outputDirectory, "event_context.ndjson")),
                    Has.Length.EqualTo(6));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static V3DiagnosticSession CreateSession(int capacity)
        {
            var session = new V3DiagnosticSession();
            session.Initialize("analysis-test", "Analysis",
                V3DiagnosticRecordingProfile.FullForensic, capacity, 1f,
                new V3DiagnosticCaptureCapacities
                {
                    sensors = 4, tasks = 4, observations = 4,
                    requests = 4, devices = 4, events = 32
                }, new V3DiagnosticCraftSnapshot(), new V3DiagnosticWorldSnapshot());
            return session;
        }

        private static V3DiagnosticSample PreparedSample()
        {
            var sample = new V3DiagnosticSample();
            sample.Prepare(4, 4, 4, 4, 4);
            sample.identity.fixedDelta = 0.02f;
            sample.identity.dynamicsValid = true;
            return sample;
        }
    }
}
