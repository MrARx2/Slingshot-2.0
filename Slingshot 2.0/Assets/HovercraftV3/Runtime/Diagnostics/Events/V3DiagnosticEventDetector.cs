using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public sealed class V3DiagnosticEventDetector : IV3DiagnosticSource
    {
        private readonly double[] lastEventTimes =
            new double[Enum.GetValues(typeof(V3DiagnosticEventType)).Length];
        private V3DiagnosticContext context;
        private V3DiagnosticSession session;
        private V3WorldContactState previousWorldState;
        private string previousSectionId = string.Empty;
        private Vector3 previousPosition;
        private Vector3 previousTrackNormal;
        private bool hasPrevious;
        private int previousContactCount;
        private bool previousSurfaceDetected;
        private bool previousNearSurface;
        private float previousCaptureAuthority;
        private bool previousFreeFlight;
        private long[] previousTaskSkips = Array.Empty<long>();
        private int previousDroppedSamples;
        private readonly int preContextSamples;
        private readonly int postContextSamples;

        public string SourceId => "events";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public V3DiagnosticEventDetector(
            int preContextSamples = 100,
            int postContextSamples = 150)
        {
            this.preContextSamples = Math.Max(0, preContextSamples);
            this.postContextSamples = Math.Max(0, postContextSamples);
        }

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
        }

        public void OnSessionStarted(V3DiagnosticSession value)
        {
            session = value;
            for (int i = 0; i < lastEventTimes.Length; i++)
                lastEventTimes[i] = double.NegativeInfinity;
            previousWorldState = V3WorldContactState.Unknown;
            previousSectionId = string.Empty;
            previousPosition = context != null && context.Body != null
                ? context.Body.position : Vector3.zero;
            previousTrackNormal = Vector3.zero;
            hasPrevious = false;
            previousContactCount = 0;
            previousSurfaceDetected = false;
            previousNearSurface = false;
            previousCaptureAuthority = 0f;
            previousFreeFlight = false;
            int taskCapacity = value != null && value.Samples != null &&
                value.Samples.Length > 0 && value.Samples[0].tasks != null
                    ? value.Samples[0].tasks.Length : 0;
            previousTaskSkips = new long[taskCapacity];
            previousDroppedSamples = value != null
                ? value.Manifest.droppedSampleCount : 0;
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            if (sample.identity.externalStateDiscontinuity)
            {
                ResetContinuity(ref sample);
                return;
            }

            if (sample.identity.dynamicsValid)
            {
                DetectMotion(ref sample);
            }
            DetectTrack(ref sample);
            DetectSensors(ref sample);
            DetectSystems(ref sample);
            DetectDevices(ref sample);
            DetectDataQuality(ref sample);
            previousWorldState = sample.worldContactState;
            previousSectionId = sample.track.sectionId ?? string.Empty;
            previousPosition = sample.world.position;
            previousTrackNormal = sample.track.trueSurfaceNormal;
            previousContactCount = sample.world.contactCount;
            previousSurfaceDetected = sample.belief.surfaceDetected;
            previousNearSurface = sample.belief.nearSurface;
            previousCaptureAuthority =
                sample.belief.captureAuthorityMultiplier;
            previousFreeFlight = IsFreeFlight(ref sample);
            hasPrevious = true;
        }

        public void OnSessionEnded(V3DiagnosticSession value)
        {
            session = null;
        }

        private void DetectMotion(ref V3DiagnosticSample sample)
        {
            bool freeFlight = IsFreeFlight(ref sample);
            bool previouslySupported = previousContactCount > 0 ||
                previousNearSurface || previousCaptureAuthority > 0.01f;
            if (hasPrevious && previousSurfaceDetected &&
                !sample.belief.surfaceDetected)
            {
                Emit(V3DiagnosticEventType.SurfaceEnvelopeDeparture, 0,
                    ref sample, "hover,probes",
                    "Current physical surface evidence left the sensing envelope.", 0.1d);
                Emit(V3DiagnosticEventType.ProbeLoss, 1,
                    ref sample, "hover,probes",
                    "All current hover-probe evidence was lost.", 0.1d);
            }
            if (hasPrevious && previousNearSurface &&
                !sample.belief.nearSurface)
            {
                Emit(V3DiagnosticEventType.NearSurfaceExit, 0,
                    ref sample, "hover",
                    "Craft left the configured near-surface envelope.", 0.1d);
            }
            if (hasPrevious && previousContactCount > 0 &&
                sample.world.contactCount == 0)
            {
                Emit(V3DiagnosticEventType.ContactLoss, 0,
                    ref sample, "contact",
                    "Physical collider contact ended.", 0.1d);
            }
            if (hasPrevious && previouslySupported && freeFlight &&
                !previousFreeFlight)
            {
                Emit(V3DiagnosticEventType.PhysicalTakeoff,
                    sample.track.expectedAirborne ? 0 : 1,
                    ref sample, "contact,hover,probes",
                    "Physical support and automatic capture authority ended before free flight.", 0.25d);
                if (sample.track.expectedAirborne)
                {
                    Emit(V3DiagnosticEventType.ExpectedJump, 0,
                        ref sample, "track,contact,hover",
                        "Physical takeoff occurred in an expected-airborne track section.", 0.25d);
                }
            }
            if (hasPrevious && !previousFreeFlight && freeFlight)
            {
                Emit(V3DiagnosticEventType.FreeFlightEntered, 0,
                    ref sample, "world,hover,aerodynamics",
                    "Craft entered contact-free flight with no meaningful capture authority.", 0.1d);
            }
            if (hasPrevious && previousContactCount == 0 &&
                sample.world.contactCount > 0)
            {
                Emit(V3DiagnosticEventType.ContactLanding, 0,
                    ref sample, "contact,hover",
                    "New physical collider contact began.", 0.1d);
            }
            if (hasPrevious && !previousSurfaceDetected &&
                sample.belief.surfaceDetected)
            {
                Emit(V3DiagnosticEventType.SurfaceReacquired, 0,
                    ref sample, "hover,probes",
                    "Current physical surface evidence was reacquired.", 0.1d);
            }
            if (sample.worldContactState ==
                V3WorldContactState.AirborneUnexpected)
            {
                Emit(V3DiagnosticEventType.UnexpectedAirborne, 2,
                    ref sample, "track,hover,sensors,execution",
                    "World truth is airborne where track metadata does not expect flight.", 1d);
            }
            float mass = context != null && context.Body != null
                ? Mathf.Max(0.001f, context.Body.mass) : 1f;
            float impulsePerMass = sample.world.contactImpulseNs / mass;
            if (hasPrevious && previousContactCount == 0 &&
                sample.world.contactCount > 0 &&
                impulsePerMass >= 8f)
            {
                Emit(V3DiagnosticEventType.HardLanding, 2, ref sample,
                    "contact,hover", "Landing impulse exceeded the diagnostic hard-landing threshold.", 1d);
            }
            if (sample.track.mapped && sample.track.trueSurfaceDistance < 0.25f)
            {
                Emit(V3DiagnosticEventType.BottomingOut, 2, ref sample,
                    "track,ride_height", "Craft center-of-mass surface distance is critically low.", 0.5d);
            }
            Vector3 up = DiagnosticReferenceUp(ref sample);
            if (Mathf.Abs(Vector3.Dot(sample.world.angularVelocity, up)) > 3f)
            {
                Emit(V3DiagnosticEventType.Spin, 2, ref sample,
                    "stabilizer,traction,contact", "Surface-normal angular rate exceeded the spin threshold.", 1d);
            }
            if (context != null && context.CraftTransform != null &&
                Vector3.Dot(context.CraftTransform.up, up) < 0f)
            {
                Emit(V3DiagnosticEventType.Rollover, 3, ref sample,
                    "stabilizer,contact,track", "Craft up axis points away from the local track surface.", 1d);
            }
            if (impulsePerMass >= 15f)
            {
                Emit(V3DiagnosticEventType.Crash, 3, ref sample,
                    "contact,track,execution", "Contact impulse exceeded the diagnostic crash threshold.", 1d);
            }
            if (hasPrevious &&
                Vector3.Distance(previousPosition, sample.world.position) > 25f &&
                sample.world.linearVelocity.magnitude < 5f)
            {
                Emit(V3DiagnosticEventType.Reset, 1, ref sample,
                    "external_state", "Position discontinuity resembles a craft reset or teleport.", 0.5d);
            }
        }

        private void DetectTrack(ref V3DiagnosticSample sample)
        {
            if (sample.track.reverseTravel)
                Emit(V3DiagnosticEventType.ReverseTravel, 1, ref sample,
                    "pilot,track", "Velocity opposes the mapped track tangent.", 1d);
            if (sample.worldContactState == V3WorldContactState.OutOfTrackBounds)
                Emit(V3DiagnosticEventType.OutOfBounds, 2, ref sample,
                    "track,pilot,traction", "Mapped center-of-mass is outside track width.", 1d);
            string currentSectionId = sample.track.sectionId ?? string.Empty;
            if (hasPrevious &&
                !string.IsNullOrEmpty(previousSectionId) &&
                !string.IsNullOrEmpty(currentSectionId) &&
                !string.Equals(previousSectionId,
                    currentSectionId,
                    StringComparison.Ordinal))
            {
                Emit(V3DiagnosticEventType.SectionExit, 0, ref sample,
                    "track", "Exited " + previousSectionId + ".", 0d);
                Emit(V3DiagnosticEventType.SectionEntry, 0, ref sample,
                    "track", "Entered " + currentSectionId + ".", 0d);
            }
            if (hasPrevious && previousTrackNormal.sqrMagnitude > 0.1f &&
                sample.track.trueSurfaceNormal.sqrMagnitude > 0.1f &&
                Vector3.Angle(previousTrackNormal,
                    sample.track.trueSurfaceNormal) > 35f)
            {
                bool ambiguousMapping = !sample.track.insideTrackBounds ||
                    sample.track.mappingConfidence < 0.5f;
                Emit(
                    ambiguousMapping
                        ? V3DiagnosticEventType.MappingAmbiguity
                        : V3DiagnosticEventType.TrackNormalDiscontinuity,
                    ambiguousMapping ? 1 : 2,
                    ref sample,
                    ambiguousMapping ? "track_mapping" : "track_collider",
                    ambiguousMapping
                        ? "Mapped normal changed by more than 35 degrees while the craft was outside the reliable centerline-mapping envelope."
                        : "In-bounds independent surface normal changed by more than 35 degrees in one physics tick.",
                    0.5d);
            }
        }

        private void DetectSensors(ref V3DiagnosticSample sample)
        {
            for (int i = 0; i < sample.sensorCount; i++)
            {
                V3SensorDiagnosticRecord sensor = sample.sensors[i];
                if (sensor.hasSample && !sensor.hit && sensor.trueHit)
                    Emit(V3DiagnosticEventType.SensorMiss, 1, ref sample,
                        "sensor,world_truth", "A sensor missed an independently observed surface.", 0.5d);
                if (sensor.hasSample && sensor.ageSeconds > 0.25f)
                    Emit(V3DiagnosticEventType.StaleObservation, 1, ref sample,
                        "sensor,scheduler,power", "Sensor sample age exceeded 250 ms.", 0.5d);
                if (sensor.comparisonQuality == V3DiagnosticDataQuality.High &&
                    (Mathf.Abs(sensor.distanceError) > 0.5f ||
                     sensor.normalAngleError > 15f))
                    Emit(V3DiagnosticEventType.SensorWorldDisagreement, 1,
                        ref sample, "sensor,world_truth",
                        "Sensor and independent world truth exceeded comparison tolerances.", 0.5d);
                if (sensor.grantedRateHz > 0f &&
                    sensor.measuredRateHz + 0.01f < sensor.grantedRateHz * 0.75f)
                    Emit(V3DiagnosticEventType.SampleRateDrop, 1, ref sample,
                        "sensor,scheduler,power", "Measured sensor rate fell below 75% of its grant.", 1d);
            }
            for (int i = 0; i < sample.observationCount; i++)
            {
                if (sample.observations[i].ageSeconds > 0.25f)
                    Emit(V3DiagnosticEventType.StaleObservation, 1, ref sample,
                        "observation_bus,scheduler", "Observation Bus payload age exceeded 250 ms.", 0.5d);
            }
        }

        private void DetectSystems(ref V3DiagnosticSample sample)
        {
            for (int i = 0; i < sample.taskCount; i++)
            {
                V3TaskDiagnosticRecord task = sample.tasks[i];
                if (task.minimumUsefulRateHz > 0f &&
                    task.measuredRateHz + 0.01f < task.minimumUsefulRateHz)
                    Emit(V3DiagnosticEventType.TaskUnderMinimum, 2, ref sample,
                        "scheduler,power", "A software task measured below its minimum useful rate.", 1d);
                if (i < previousTaskSkips.Length &&
                    task.skippedCount > previousTaskSkips[i])
                    Emit(V3DiagnosticEventType.TaskSkipped, 1, ref sample,
                        "scheduler", "A scheduler task reported a new skipped execution.", 0.25d);
                if (i < previousTaskSkips.Length)
                    previousTaskSkips[i] = task.skippedCount;
                if (task.faulted)
                    Emit(V3DiagnosticEventType.TaskFault, 3, ref sample,
                        "scheduler,software", "A scheduler task is faulted.", 1d);
            }
            if (sample.belief.mainframeState == "Degraded")
                Emit(V3DiagnosticEventType.MainframeDegraded, 2, ref sample,
                    "mainframe,topology,power,scheduler", "Mainframe reported Degraded state.", 1d);
            if (sample.belief.mainframeState == "Faulted")
                Emit(V3DiagnosticEventType.MainframeFaulted, 3, ref sample,
                    "mainframe,topology,power,scheduler", "Mainframe reported Faulted state.", 1d);
            for (int i = 0; i < sample.requestCount; i++)
            for (int j = i + 1; j < sample.requestCount; j++)
            {
                V3SystemRequestDiagnosticRecord a = sample.requests[i];
                V3SystemRequestDiagnosticRecord b = sample.requests[j];
                if (a.valid && b.valid && a.domain == b.domain &&
                    a.sourceSystemId != b.sourceSystemId &&
                    a.requestedState * b.requestedState < -0.05f)
                {
                    Emit(V3DiagnosticEventType.ControlConflict, 2, ref sample,
                        "control_router", "Concurrent valid requests oppose one another in the same control domain.", 0.5d);
                    break;
                }
            }
            if (IsMeaningfullyPowerLimited(
                    sample.power.requestedSystemsPower,
                    sample.power.grantedSystemsPower) ||
                IsMeaningfullyPowerLimited(
                    sample.power.requestedPropulsionPower,
                    sample.power.grantedPropulsionPower))
                Emit(V3DiagnosticEventType.PowerStarvation, 2, ref sample,
                    "power", "Requested power exceeded granted power.", 0.5d);
        }

        private void DetectDevices(ref V3DiagnosticSample sample)
        {
            for (int i = 0; i < sample.deviceCount; i++)
            {
                V3DeviceDiagnosticRecord device = sample.devices[i];
                if (device.deviceType == "Thruster" &&
                    device.routerCombinedRequest >= 0.999f &&
                    device.actualOutput + 0.01f < device.routerCombinedRequest)
                    Emit(V3DiagnosticEventType.ThrusterSaturation, 1, ref sample,
                        "router,power,thermal,thruster", "Thruster request is saturated above executed output.", 0.5d);
                if (device.thermallyDerated)
                    Emit(V3DiagnosticEventType.ThermalDerating, 2, ref sample,
                        "thermal", "A physical part is thermally derated.", 0.5d);
                if (device.gimbalAtLimit)
                    Emit(V3DiagnosticEventType.GimbalLimit, 1, ref sample,
                        "gimbal", "A gimbal reached a physical angle limit.", 0.5d);
                if (device.springAtCompressionLimit ||
                    device.springAtExtensionLimit)
                    Emit(V3DiagnosticEventType.SpringTravelLimit, 1, ref sample,
                        "spring_mount", "A spring mount reached a physical travel limit.", 0.5d);
                if (device.rotaryAtLimit)
                    Emit(V3DiagnosticEventType.FinLimit, 1, ref sample,
                        "aerodynamics,rotary_actuator", "A rotary aerodynamic actuator reached its limit.", 0.5d);
                if (device.faulted)
                    Emit(V3DiagnosticEventType.DeviceFault, 3, ref sample,
                        "device,power,thermal", "A physical runtime part is disabled or locked out.", 0.5d);
            }
        }

        private void DetectDataQuality(ref V3DiagnosticSample sample)
        {
            if (session == null) return;
            int dropped = session.Manifest.droppedSampleCount;
            if (dropped > previousDroppedSamples)
            {
                Emit(V3DiagnosticEventType.DroppedSamples, 3, ref sample,
                    "recorder", "The bounded capture buffer rejected one or more samples.", 0d);
                previousDroppedSamples = dropped;
            }
        }

        private void Emit(
            V3DiagnosticEventType type,
            int severity,
            ref V3DiagnosticSample sample,
            string domains,
            string notes,
            double cooldownSeconds)
        {
            if (session == null) return;
            int index = (int)type;
            double now = sample.identity.sessionElapsed;
            if (index >= 0 && index < lastEventTimes.Length &&
                now - lastEventTimes[index] < cooldownSeconds)
                return;
            if (index >= 0 && index < lastEventTimes.Length)
                lastEventTimes[index] = now;
            session.TryAddEvent(new V3DiagnosticEvent
            {
                eventId = type + "_" + sample.identity.sampleIndex.ToString("D8"),
                type = type,
                severity = severity,
                timestamp = now,
                tick = sample.identity.physicsTick,
                sampleIndex = sample.identity.sampleIndex,
                attemptIndex = sample.identity.attemptIndex,
                resetReason = sample.identity.resetReason,
                trackDistance = sample.track.distanceAlongTrack,
                sectionId = sample.track.sectionId,
                craftPosition = sample.world.position,
                speedMetersPerSecond = sample.world.linearVelocity.magnitude,
                worldState = sample.worldContactState,
                craftBeliefState = sample.belief.hoverGrounded
                    ? "Grounded" : "NotGrounded",
                possibleContributingDomains = domains,
                preEventStartSample = Math.Max(0L,
                    sample.identity.sampleIndex - preContextSamples),
                postEventEndSample = sample.identity.sampleIndex +
                    postContextSamples,
                notes = notes,
                manual = false
            });
        }

        private void ResetContinuity(ref V3DiagnosticSample sample)
        {
            previousWorldState = sample.worldContactState;
            previousSectionId = sample.track.sectionId ?? string.Empty;
            previousPosition = sample.world.position;
            previousTrackNormal = sample.track.trueSurfaceNormal;
            previousContactCount = sample.world.contactCount;
            previousSurfaceDetected = sample.belief.surfaceDetected;
            previousNearSurface = sample.belief.nearSurface;
            previousCaptureAuthority =
                sample.belief.captureAuthorityMultiplier;
            previousFreeFlight = IsFreeFlight(ref sample);
            hasPrevious = false;
        }

        private static Vector3 DiagnosticReferenceUp(
            ref V3DiagnosticSample sample)
        {
            if (sample.track.mapped && sample.track.normal.sqrMagnitude > 0.1f)
                return sample.track.normal.normalized;

            return sample.world.gravityVector.sqrMagnitude > 0.000001f
                ? -sample.world.gravityVector.normalized
                : Vector3.up;
        }

        internal static bool IsMeaningfullyPowerLimited(
            float requested,
            float granted)
        {
            float safeRequested = Mathf.Max(0f, requested);
            float tolerance = Mathf.Max(0.01f, safeRequested * 0.0001f);
            return safeRequested - Mathf.Max(0f, granted) > tolerance;
        }

        private static bool IsFreeFlight(ref V3DiagnosticSample sample)
        {
            return sample.world.contactCount == 0 &&
                !sample.belief.nearSurface &&
                sample.belief.captureAuthorityMultiplier <= 0.01f &&
                (sample.worldContactState == V3WorldContactState.FreeFlight ||
                 sample.worldContactState == V3WorldContactState.AirborneExpected ||
                 sample.worldContactState == V3WorldContactState.AirborneUnexpected);
        }
    }
}
