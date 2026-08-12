using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [Serializable]
    public sealed class V3DiagnosticLapSummary
    {
        public int lap;
        public int sampleCount;
        public double startTimeSeconds;
        public double endTimeSeconds;
        public double durationSeconds;
        public float maximumSpeedMetersPerSecond;
        public float meanSpeedMetersPerSecond;
        public int eventCount;
    }

    [Serializable]
    public sealed class V3DiagnosticAttemptSummary
    {
        public int attemptIndex;
        public int sampleCount;
        public int discontinuitySamples;
        public double startTimeSeconds;
        public double endTimeSeconds;
        public double durationSeconds;
        public float startTrackDistanceMeters;
        public float maximumTrackDistanceMeters;
        public float maximumSpeedMetersPerSecond;
        public float meanSpeedMetersPerSecond;
        public float maximumAbsoluteLateralOffsetMeters;
        public float minimumRideHeightMeters;
        public int eventCount;
    }

    [Serializable]
    public sealed class V3DiagnosticSectionSummary
    {
        public string sectionId;
        public string sectionType;
        public int sampleCount;
        public double durationSeconds;
        public float minimumSpeedMetersPerSecond;
        public float maximumSpeedMetersPerSecond;
        public float meanSpeedMetersPerSecond;
        public float maximumAbsoluteLateralOffsetMeters;
        public float minimumRideHeightMeters;
        public float maximumRideHeightMeters;
        public float maximumAbsoluteVerticalAcceleration;
        public int unexpectedAirborneSamples;
        public int powerLimitedSamples;
        public int eventCount;
    }

    [Serializable]
    public sealed class V3DiagnosticDeviceSummary
    {
        public string deviceId;
        public string deviceType;
        public string role;
        public int sampleCount;
        public float meanRequestedOutput;
        public float meanActualOutput;
        public float meanGrantFraction;
        public float peakForceN;
        public float peakTemperatureC;
        public int deratedSamples;
        public int faultSamples;
        public int physicalLimitSamples;
    }

    [Serializable]
    public sealed class V3DiagnosticSystemSummary
    {
        public string taskId;
        public string role;
        public int sampleCount;
        public float meanRequestedRateHz;
        public float meanGrantedRateHz;
        public float meanMeasuredRateHz;
        public long maximumSkippedCount;
        public int belowMinimumSamples;
        public int faultSamples;
    }

    [Serializable]
    public sealed class V3DiagnosticSensorSummary
    {
        public string sensorId;
        public string direction;
        public int sampleCount;
        public int validSampleCount;
        public int hitCount;
        public int comparableSampleCount;
        public float meanAgeSeconds;
        public float meanAbsoluteDistanceErrorMeters;
        public float maximumAbsoluteDistanceErrorMeters;
        public float meanNormalErrorDegrees;
        public int missCount;
    }

    [Serializable]
    public sealed class V3DiagnosticForceSummary
    {
        public string category;
        public int sampleCount;
        public float meanMagnitudeN;
        public float peakMagnitudeN;
        public float meanGravityVerticalN;
    }

    [Serializable]
    public sealed class V3DiagnosticEventTypeSummary
    {
        public string eventType;
        public int count;
        public int maximumSeverity;
    }

    [Serializable]
    public sealed class V3DiagnosticLocationCluster
    {
        public string sectionId;
        public float startDistanceMeters;
        public float endDistanceMeters;
        public int eventCount;
        public int maximumSeverity;
        public string eventTypes;
    }

    [Serializable]
    public sealed class V3DiagnosticAnalysisReport
    {
        public int schemaVersion;
        public string sessionId;
        public string generatedUtc;
        public string executiveSummary;
        public string dataQuality;
        public int sampleCount;
        public int droppedSampleCount;
        public int droppedEventCount;
        public int eventCount;
        public double durationSeconds;
        public float maximumSpeedMetersPerSecond;
        public float maximumResidualForceN;
        public float maximumResidualTorqueNm;
        public int contactFreeResidualSampleCount;
        public float contactFreeResidualMeanN;
        public float contactFreeResidualP95N;
        public float contactFreeResidualMaxN;
        public int unexpectedAirborneSamples;
        public int worldBeliefDisagreementSamples;
        public int powerLimitedSamples;
        public int thermalDeratingSamples;
        public int invalidWorldEnvironmentSamples;
        public int samplesInsideEnvironmentZones;
        public float minimumAirDensityKgPerCubicMeter;
        public float maximumAirDensityKgPerCubicMeter;
        public float minimumAmbientTemperatureC;
        public float maximumAmbientTemperatureC;
        public float maximumMachNumber;
        public float maximumDynamicPressurePa;
        public V3DiagnosticLapSummary[] laps;
        public V3DiagnosticAttemptSummary[] attempts;
        public V3DiagnosticSectionSummary[] sections;
        public V3DiagnosticDeviceSummary[] devices;
        public V3DiagnosticSystemSummary[] systems;
        public V3DiagnosticSensorSummary[] sensors;
        public V3DiagnosticForceSummary[] forces;
        public V3DiagnosticEventTypeSummary[] eventTypes;
        public V3DiagnosticLocationCluster[] repeatedLocationClusters;
        public string[] evidenceNotes;
        public string[] potentialInvestigationAreas;
        public string[] rawDataFiles;
    }

    internal sealed class SectionAccumulator
    {
        public V3DiagnosticSectionSummary value = new V3DiagnosticSectionSummary
        {
            minimumSpeedMetersPerSecond = float.PositiveInfinity,
            minimumRideHeightMeters = float.PositiveInfinity
        };
        public float speedSum;
    }

    internal sealed class LapAccumulator
    {
        public V3DiagnosticLapSummary value = new V3DiagnosticLapSummary();
        public float speedSum;
    }

    internal sealed class AttemptAccumulator
    {
        public V3DiagnosticAttemptSummary value =
            new V3DiagnosticAttemptSummary
            {
                minimumRideHeightMeters = float.PositiveInfinity
            };
        public float speedSum;
    }

    internal sealed class DeviceAccumulator
    {
        public V3DiagnosticDeviceSummary value = new V3DiagnosticDeviceSummary();
        public float requestedSum;
        public float actualSum;
        public float grantSum;
    }

    internal sealed class SystemAccumulator
    {
        public V3DiagnosticSystemSummary value = new V3DiagnosticSystemSummary();
        public float requestedSum;
        public float grantedSum;
        public float measuredSum;
    }

    internal sealed class SensorAccumulator
    {
        public V3DiagnosticSensorSummary value = new V3DiagnosticSensorSummary();
        public float ageSum;
        public float distanceErrorSum;
        public float normalErrorSum;
    }

    internal sealed class LocationClusterAccumulator
    {
        public V3DiagnosticLocationCluster value =
            new V3DiagnosticLocationCluster();
        public readonly HashSet<string> eventTypes =
            new HashSet<string>(StringComparer.Ordinal);
    }

    public static class V3DiagnosticReportBuilder
    {
        public static V3DiagnosticAnalysisReport Build(V3DiagnosticSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var sections = new Dictionary<string, SectionAccumulator>(StringComparer.Ordinal);
            var laps = new Dictionary<int, LapAccumulator>();
            var attempts = new Dictionary<int, AttemptAccumulator>();
            var devices = new Dictionary<string, DeviceAccumulator>(StringComparer.Ordinal);
            var systems = new Dictionary<string, SystemAccumulator>(StringComparer.Ordinal);
            var sensors = new Dictionary<string, SensorAccumulator>(StringComparer.Ordinal);
            float maxSpeed = 0f;
            float maxResidualForce = 0f;
            float maxResidualTorque = 0f;
            int unexpectedAirborne = 0;
            int beliefDisagreement = 0;
            int powerLimited = 0;
            int thermalDerating = 0;
            int invalidEnvironment = 0;
            int zonedSamples = 0;
            float minimumDensity = float.PositiveInfinity;
            float maximumDensity = 0f;
            float minimumTemperature = float.PositiveInfinity;
            float maximumTemperature = float.NegativeInfinity;
            float maximumMach = 0f;
            float maximumDynamicPressure = 0f;
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample sample = session.Samples[i];
                float speed = sample.world.linearVelocity.magnitude;
                AccumulateAttempt(attempts, sample, speed);
                if (sample.world.environmentValid)
                {
                    minimumDensity = Mathf.Min(
                        minimumDensity,
                        sample.world.airDensityKgPerCubicMeter);
                    maximumDensity = Mathf.Max(
                        maximumDensity,
                        sample.world.airDensityKgPerCubicMeter);
                    minimumTemperature = Mathf.Min(
                        minimumTemperature,
                        sample.world.ambientTemperatureC);
                    maximumTemperature = Mathf.Max(
                        maximumTemperature,
                        sample.world.ambientTemperatureC);
                    if (sample.world.activeZoneCount > 0)
                    {
                        zonedSamples++;
                    }
                }
                else
                {
                    invalidEnvironment++;
                }
                maximumMach = Mathf.Max(
                    maximumMach,
                    sample.world.machNumber);
                maximumDynamicPressure = Mathf.Max(
                    maximumDynamicPressure,
                    sample.world.dynamicPressurePa);
                if (sample.identity.externalStateDiscontinuity ||
                    !sample.identity.dynamicsValid)
                    continue;

                maxSpeed = Mathf.Max(maxSpeed, speed);
                maxResidualForce = Mathf.Max(maxResidualForce,
                    sample.forces.residualForce.magnitude);
                maxResidualTorque = Mathf.Max(maxResidualTorque,
                    sample.forces.residualTorque.magnitude);
                bool unexpected = sample.worldContactState ==
                    V3WorldContactState.AirborneUnexpected;
                if (unexpected) unexpectedAirborne++;
                bool worldGrounded = sample.worldContactState ==
                    V3WorldContactState.OnSurface ||
                    sample.worldContactState == V3WorldContactState.HoveringNearSurface ||
                    sample.worldContactState == V3WorldContactState.Landing;
                if (worldGrounded != sample.belief.hoverGrounded) beliefDisagreement++;
                bool limited = IsMeaningfullyPowerLimited(sample);
                if (limited) powerLimited++;
                AccumulateSection(sections, sample, speed, unexpected, limited);
                AccumulateLap(laps, sample, speed);
                AccumulateDevices(devices, sample, ref thermalDerating);
                AccumulateSystems(systems, sample);
                AccumulateSensors(sensors, sample);
            }
            CountEvents(session, sections, laps, attempts,
                out V3DiagnosticEventTypeSummary[] eventTypes);
            V3DiagnosticForceSummary[] forces = BuildForceSummaries(session);
            BuildContactFreeResidualSummary(
                session,
                out int contactFreeCount,
                out float contactFreeMean,
                out float contactFreeP95,
                out float contactFreeMax);
            var notes = new List<string>(8);
            var investigation = new List<string>(8);
            notes.Add("All classifications are evidence labels; no event is asserted as a single-cause diagnosis.");
            notes.Add("World truth, craft belief, requests, execution, and forces share one physics-tick identity.");
            if (beliefDisagreement > 0)
                investigation.Add("Review world-contact versus hover-belief disagreement samples and their sensor ages.");
            if (maxResidualForce > 0f)
                investigation.Add("Review high force-residual windows for unmodeled contacts or incomplete force attribution.");
            if (powerLimited > 0)
                investigation.Add("Review power-limited windows alongside scheduler grants and device execution.");
            if (unexpectedAirborne > 0)
                investigation.Add("Review unexpected-airborne locations against track surface truth and hover probes.");
            if (thermalDerating > 0)
                investigation.Add("Review thermally derated devices and the request-to-execution gap.");
            if (invalidEnvironment > 0)
                investigation.Add(
                    "World environment sampling was invalid for one or more samples; review root/profile validation flags.");
            if (investigation.Count == 0)
                investigation.Add("No threshold-based investigation area was automatically raised; inspect raw evidence for context.");
            string quality = session.Manifest.droppedSampleCount == 0 &&
                session.Manifest.droppedEventCount == 0 &&
                invalidEnvironment == 0
                ? "Nominal" : "Degraded";
            return new V3DiagnosticAnalysisReport
            {
                schemaVersion = V3DiagnosticSchema.Version,
                sessionId = session.Manifest.sessionId,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                executiveSummary = BuildExecutiveSummary(session, quality,
                    unexpectedAirborne, beliefDisagreement, powerLimited),
                dataQuality = quality,
                sampleCount = session.SampleCount,
                droppedSampleCount = session.Manifest.droppedSampleCount,
                droppedEventCount = session.Manifest.droppedEventCount,
                eventCount = session.EventCount,
                durationSeconds = session.Manifest.capturedDurationSeconds,
                maximumSpeedMetersPerSecond = maxSpeed,
                maximumResidualForceN = maxResidualForce,
                maximumResidualTorqueNm = maxResidualTorque,
                contactFreeResidualSampleCount = contactFreeCount,
                contactFreeResidualMeanN = contactFreeMean,
                contactFreeResidualP95N = contactFreeP95,
                contactFreeResidualMaxN = contactFreeMax,
                unexpectedAirborneSamples = unexpectedAirborne,
                worldBeliefDisagreementSamples = beliefDisagreement,
                powerLimitedSamples = powerLimited,
                thermalDeratingSamples = thermalDerating,
                invalidWorldEnvironmentSamples = invalidEnvironment,
                samplesInsideEnvironmentZones = zonedSamples,
                minimumAirDensityKgPerCubicMeter =
                    float.IsPositiveInfinity(minimumDensity) ? 0f : minimumDensity,
                maximumAirDensityKgPerCubicMeter = maximumDensity,
                minimumAmbientTemperatureC =
                    float.IsPositiveInfinity(minimumTemperature)
                        ? 0f : minimumTemperature,
                maximumAmbientTemperatureC =
                    float.IsNegativeInfinity(maximumTemperature)
                        ? 0f : maximumTemperature,
                maximumMachNumber = maximumMach,
                maximumDynamicPressurePa = maximumDynamicPressure,
                laps = FinalizeLaps(laps),
                attempts = FinalizeAttempts(attempts),
                sections = FinalizeSections(sections),
                devices = FinalizeDevices(devices),
                systems = FinalizeSystems(systems),
                sensors = FinalizeSensors(sensors),
                forces = forces,
                eventTypes = eventTypes,
                repeatedLocationClusters = BuildRepeatedLocationClusters(session),
                evidenceNotes = notes.ToArray(),
                potentialInvestigationAreas = investigation.ToArray(),
                rawDataFiles = new[]
                {
                    "channel_schema.json", "samples.ndjson",
                    "samples_compact.bin", "samples_core.csv",
                    "events.json", "event_context.ndjson",
                    "track_report.json"
                }
            };
        }

        private static V3DiagnosticLocationCluster[] BuildRepeatedLocationClusters(
            V3DiagnosticSession session)
        {
            const float binSize = 10f;
            var map = new Dictionary<string, LocationClusterAccumulator>(
                StringComparer.Ordinal);
            for (int i = 0; i < session.EventCount; i++)
            {
                V3DiagnosticEvent item = session.Events[i];
                if (item == null) continue;
                float start = Mathf.Floor(item.trackDistance / binSize) * binSize;
                string section = string.IsNullOrEmpty(item.sectionId)
                    ? "unmapped" : item.sectionId;
                string key = section + "|" + start.ToString("R");
                if (!map.TryGetValue(key, out LocationClusterAccumulator value))
                {
                    value = new LocationClusterAccumulator();
                    value.value.sectionId = section;
                    value.value.startDistanceMeters = start;
                    value.value.endDistanceMeters = start + binSize;
                    map.Add(key, value);
                }
                value.value.eventCount++;
                value.value.maximumSeverity = Mathf.Max(
                    value.value.maximumSeverity, item.severity);
                value.eventTypes.Add(item.type.ToString());
            }
            int repeatedCount = 0;
            foreach (LocationClusterAccumulator item in map.Values)
            {
                if (item.value.eventCount > 1) repeatedCount++;
            }
            var result = new V3DiagnosticLocationCluster[repeatedCount];
            int index = 0;
            foreach (LocationClusterAccumulator item in map.Values)
            {
                if (item.value.eventCount <= 1) continue;
                var types = new string[item.eventTypes.Count];
                item.eventTypes.CopyTo(types);
                Array.Sort(types, StringComparer.Ordinal);
                item.value.eventTypes = string.Join(", ", types);
                result[index++] = item.value;
            }
            Array.Sort(result, (a, b) =>
            {
                int section = string.CompareOrdinal(a.sectionId, b.sectionId);
                return section != 0
                    ? section
                    : a.startDistanceMeters.CompareTo(b.startDistanceMeters);
            });
            return result;
        }

        private static string BuildExecutiveSummary(
            V3DiagnosticSession session, string quality,
            int unexpected, int disagreement, int powerLimited)
        {
            return "Captured " + session.SampleCount +
                " synchronized physics samples over " +
                session.Manifest.capturedDurationSeconds.ToString("0.###") +
                " seconds. Data quality: " + quality + ". Evidence includes " +
                unexpected + " unexpected-airborne samples, " + disagreement +
                " world/belief disagreement samples, and " + powerLimited +
                " power-limited samples. These counts identify review windows, not causes.";
        }

        private static void AccumulateSection(
            Dictionary<string, SectionAccumulator> map,
            V3DiagnosticSample sample, float speed, bool unexpected, bool limited)
        {
            string key = string.IsNullOrEmpty(sample.track.sectionId)
                ? "unmapped" : sample.track.sectionId;
            if (!map.TryGetValue(key, out SectionAccumulator accumulator))
            {
                accumulator = new SectionAccumulator();
                accumulator.value.sectionId = key;
                accumulator.value.sectionType = sample.track.sectionType;
                map.Add(key, accumulator);
            }
            V3DiagnosticSectionSummary value = accumulator.value;
            value.sampleCount++;
            value.durationSeconds += sample.identity.fixedDelta;
            value.minimumSpeedMetersPerSecond = Mathf.Min(value.minimumSpeedMetersPerSecond, speed);
            value.maximumSpeedMetersPerSecond = Mathf.Max(value.maximumSpeedMetersPerSecond, speed);
            value.maximumAbsoluteLateralOffsetMeters = Mathf.Max(
                value.maximumAbsoluteLateralOffsetMeters,
                Mathf.Abs(sample.track.signedLateralOffset));
            value.minimumRideHeightMeters = Mathf.Min(
                value.minimumRideHeightMeters, sample.track.trueSurfaceDistance);
            value.maximumRideHeightMeters = Mathf.Max(
                value.maximumRideHeightMeters, sample.track.trueSurfaceDistance);
            value.maximumAbsoluteVerticalAcceleration = Mathf.Max(
                value.maximumAbsoluteVerticalAcceleration,
                Mathf.Abs(sample.world.gravityFrameVerticalAcceleration));
            if (unexpected) value.unexpectedAirborneSamples++;
            if (limited) value.powerLimitedSamples++;
            accumulator.speedSum += speed;
        }

        private static void AccumulateLap(
            Dictionary<int, LapAccumulator> map,
            V3DiagnosticSample sample, float speed)
        {
            int key = sample.track.lap;
            if (!map.TryGetValue(key, out LapAccumulator accumulator))
            {
                accumulator = new LapAccumulator();
                accumulator.value.lap = key;
                accumulator.value.startTimeSeconds = sample.identity.sessionElapsed;
                map.Add(key, accumulator);
            }
            accumulator.value.sampleCount++;
            accumulator.value.endTimeSeconds = sample.identity.sessionElapsed;
            accumulator.value.maximumSpeedMetersPerSecond = Mathf.Max(
                accumulator.value.maximumSpeedMetersPerSecond, speed);
            accumulator.speedSum += speed;
        }

        private static void AccumulateAttempt(
            Dictionary<int, AttemptAccumulator> map,
            V3DiagnosticSample sample,
            float speed)
        {
            int key = Mathf.Max(0, sample.identity.attemptIndex);
            if (!map.TryGetValue(key, out AttemptAccumulator accumulator))
            {
                accumulator = new AttemptAccumulator();
                accumulator.value.attemptIndex = key;
                accumulator.value.startTimeSeconds =
                    sample.identity.sessionElapsed;
                accumulator.value.startTrackDistanceMeters =
                    sample.track.distanceAlongTrack;
                map.Add(key, accumulator);
            }

            V3DiagnosticAttemptSummary value = accumulator.value;
            value.sampleCount++;
            value.endTimeSeconds = sample.identity.sessionElapsed;
            if (sample.identity.externalStateDiscontinuity)
            {
                value.discontinuitySamples++;
                return;
            }

            value.maximumTrackDistanceMeters = Mathf.Max(
                value.maximumTrackDistanceMeters,
                sample.track.distanceAlongTrack);
            value.maximumSpeedMetersPerSecond = Mathf.Max(
                value.maximumSpeedMetersPerSecond,
                speed);
            value.maximumAbsoluteLateralOffsetMeters = Mathf.Max(
                value.maximumAbsoluteLateralOffsetMeters,
                Mathf.Abs(sample.track.signedLateralOffset));
            value.minimumRideHeightMeters = Mathf.Min(
                value.minimumRideHeightMeters,
                sample.track.trueSurfaceDistance);
            accumulator.speedSum += speed;
        }

        private static void AccumulateDevices(
            Dictionary<string, DeviceAccumulator> map,
            V3DiagnosticSample sample, ref int thermalDerating)
        {
            for (int i = 0; i < sample.deviceCount; i++)
            {
                V3DeviceDiagnosticRecord device = sample.devices[i];
                string key = string.IsNullOrEmpty(device.deviceId)
                    ? "device_" + i : device.deviceId;
                if (!map.TryGetValue(key, out DeviceAccumulator accumulator))
                {
                    accumulator = new DeviceAccumulator();
                    accumulator.value.deviceId = key;
                    accumulator.value.deviceType = device.deviceType;
                    accumulator.value.role = device.role;
                    map.Add(key, accumulator);
                }
                accumulator.value.sampleCount++;
                accumulator.requestedSum += device.requestedOutput;
                accumulator.actualSum += device.actualOutput;
                accumulator.grantSum += device.grantFraction;
                accumulator.value.peakForceN = Mathf.Max(
                    accumulator.value.peakForceN, device.actualForceMagnitudeN);
                accumulator.value.peakTemperatureC = Mathf.Max(
                    accumulator.value.peakTemperatureC, device.temperatureC);
                if (device.thermallyDerated)
                {
                    accumulator.value.deratedSamples++;
                    thermalDerating++;
                }
                if (device.faulted) accumulator.value.faultSamples++;
                if (device.gimbalAtLimit || device.springAtCompressionLimit ||
                    device.springAtExtensionLimit || device.rotaryAtLimit)
                    accumulator.value.physicalLimitSamples++;
            }
        }

        private static void AccumulateSystems(
            Dictionary<string, SystemAccumulator> map,
            V3DiagnosticSample sample)
        {
            for (int i = 0; i < sample.taskCount; i++)
            {
                V3TaskDiagnosticRecord task = sample.tasks[i];
                string key = string.IsNullOrEmpty(task.taskId)
                    ? "task_" + i : task.taskId;
                if (!map.TryGetValue(key, out SystemAccumulator accumulator))
                {
                    accumulator = new SystemAccumulator();
                    accumulator.value.taskId = key;
                    accumulator.value.role = task.role;
                    map.Add(key, accumulator);
                }
                accumulator.value.sampleCount++;
                accumulator.requestedSum += task.requestedRateHz;
                accumulator.grantedSum += task.grantedRateHz;
                accumulator.measuredSum += task.measuredRateHz;
                accumulator.value.maximumSkippedCount = Math.Max(
                    accumulator.value.maximumSkippedCount, task.skippedCount);
                if (task.minimumUsefulRateHz > 0f &&
                    task.measuredRateHz < task.minimumUsefulRateHz)
                    accumulator.value.belowMinimumSamples++;
                if (task.faulted) accumulator.value.faultSamples++;
            }
        }

        private static void AccumulateSensors(
            Dictionary<string, SensorAccumulator> map,
            V3DiagnosticSample sample)
        {
            for (int i = 0; i < sample.sensorCount; i++)
            {
                V3SensorDiagnosticRecord sensor = sample.sensors[i];
                string key = string.IsNullOrEmpty(sensor.sensorId)
                    ? "sensor_" + i : sensor.sensorId;
                if (!map.TryGetValue(key, out SensorAccumulator accumulator))
                {
                    accumulator = new SensorAccumulator();
                    accumulator.value.sensorId = key;
                    accumulator.value.direction = sensor.direction;
                    map.Add(key, accumulator);
                }
                accumulator.value.sampleCount++;
                accumulator.ageSum += sensor.ageSeconds;
                if (sensor.hasSample) accumulator.value.validSampleCount++;
                if (sensor.hit) accumulator.value.hitCount++;
                if (sensor.trueHit && !sensor.hit) accumulator.value.missCount++;
                if (sensor.comparisonQuality == V3DiagnosticDataQuality.High)
                {
                    float error = Mathf.Abs(sensor.distanceError);
                    accumulator.value.comparableSampleCount++;
                    accumulator.distanceErrorSum += error;
                    accumulator.normalErrorSum += sensor.normalAngleError;
                    accumulator.value.maximumAbsoluteDistanceErrorMeters =
                        Mathf.Max(accumulator.value.maximumAbsoluteDistanceErrorMeters, error);
                }
            }
        }

        private static void CountEvents(
            V3DiagnosticSession session,
            Dictionary<string, SectionAccumulator> sections,
            Dictionary<int, LapAccumulator> laps,
            Dictionary<int, AttemptAccumulator> attempts,
            out V3DiagnosticEventTypeSummary[] summaries)
        {
            var map = new Dictionary<V3DiagnosticEventType, V3DiagnosticEventTypeSummary>();
            for (int i = 0; i < session.EventCount; i++)
            {
                V3DiagnosticEvent item = session.Events[i];
                if (item == null) continue;
                if (!map.TryGetValue(item.type, out V3DiagnosticEventTypeSummary summary))
                {
                    summary = new V3DiagnosticEventTypeSummary
                    {
                        eventType = item.type.ToString()
                    };
                    map.Add(item.type, summary);
                }
                summary.count++;
                summary.maximumSeverity = Math.Max(summary.maximumSeverity, item.severity);
                if (!string.IsNullOrEmpty(item.sectionId) &&
                    sections.TryGetValue(item.sectionId, out SectionAccumulator section))
                    section.value.eventCount++;
                int lap = 0;
                if (item.sampleIndex >= 0 && item.sampleIndex < session.SampleCount)
                    lap = session.Samples[item.sampleIndex].track.lap;
                if (laps.TryGetValue(lap, out LapAccumulator lapAccumulator))
                    lapAccumulator.value.eventCount++;
                if (attempts.TryGetValue(
                    Mathf.Max(0, item.attemptIndex),
                    out AttemptAccumulator attemptAccumulator))
                    attemptAccumulator.value.eventCount++;
            }
            summaries = new V3DiagnosticEventTypeSummary[map.Count];
            int index = 0;
            foreach (V3DiagnosticEventTypeSummary value in map.Values)
                summaries[index++] = value;
        }

        private static V3DiagnosticLapSummary[] FinalizeLaps(
            Dictionary<int, LapAccumulator> map)
        {
            var result = new V3DiagnosticLapSummary[map.Count];
            int index = 0;
            foreach (LapAccumulator item in map.Values)
            {
                item.value.durationSeconds = Math.Max(0d,
                    item.value.endTimeSeconds - item.value.startTimeSeconds);
                item.value.meanSpeedMetersPerSecond = item.value.sampleCount > 0
                    ? item.speedSum / item.value.sampleCount : 0f;
                result[index++] = item.value;
            }
            Array.Sort(result, (a, b) => a.lap.CompareTo(b.lap));
            return result;
        }

        private static V3DiagnosticAttemptSummary[] FinalizeAttempts(
            Dictionary<int, AttemptAccumulator> map)
        {
            var result = new V3DiagnosticAttemptSummary[map.Count];
            int index = 0;
            foreach (AttemptAccumulator item in map.Values)
            {
                int validSamples = Mathf.Max(
                    0,
                    item.value.sampleCount -
                    item.value.discontinuitySamples);
                item.value.durationSeconds = Math.Max(
                    0d,
                    item.value.endTimeSeconds -
                    item.value.startTimeSeconds);
                item.value.meanSpeedMetersPerSecond = validSamples > 0
                    ? item.speedSum / validSamples
                    : 0f;
                if (float.IsPositiveInfinity(
                    item.value.minimumRideHeightMeters))
                    item.value.minimumRideHeightMeters = 0f;
                result[index++] = item.value;
            }
            Array.Sort(result,
                (a, b) => a.attemptIndex.CompareTo(b.attemptIndex));
            return result;
        }

        private static V3DiagnosticSectionSummary[] FinalizeSections(
            Dictionary<string, SectionAccumulator> map)
        {
            var result = new V3DiagnosticSectionSummary[map.Count];
            int index = 0;
            foreach (SectionAccumulator item in map.Values)
            {
                item.value.meanSpeedMetersPerSecond = item.value.sampleCount > 0
                    ? item.speedSum / item.value.sampleCount : 0f;
                if (float.IsPositiveInfinity(item.value.minimumSpeedMetersPerSecond))
                    item.value.minimumSpeedMetersPerSecond = 0f;
                if (float.IsPositiveInfinity(item.value.minimumRideHeightMeters))
                    item.value.minimumRideHeightMeters = 0f;
                result[index++] = item.value;
            }
            return result;
        }

        private static V3DiagnosticDeviceSummary[] FinalizeDevices(
            Dictionary<string, DeviceAccumulator> map)
        {
            var result = new V3DiagnosticDeviceSummary[map.Count]; int index = 0;
            foreach (DeviceAccumulator item in map.Values)
            {
                float n = Mathf.Max(1, item.value.sampleCount);
                item.value.meanRequestedOutput = item.requestedSum / n;
                item.value.meanActualOutput = item.actualSum / n;
                item.value.meanGrantFraction = item.grantSum / n;
                result[index++] = item.value;
            }
            return result;
        }

        private static V3DiagnosticSystemSummary[] FinalizeSystems(
            Dictionary<string, SystemAccumulator> map)
        {
            var result = new V3DiagnosticSystemSummary[map.Count]; int index = 0;
            foreach (SystemAccumulator item in map.Values)
            {
                float n = Mathf.Max(1, item.value.sampleCount);
                item.value.meanRequestedRateHz = item.requestedSum / n;
                item.value.meanGrantedRateHz = item.grantedSum / n;
                item.value.meanMeasuredRateHz = item.measuredSum / n;
                result[index++] = item.value;
            }
            return result;
        }

        private static V3DiagnosticSensorSummary[] FinalizeSensors(
            Dictionary<string, SensorAccumulator> map)
        {
            var result = new V3DiagnosticSensorSummary[map.Count]; int index = 0;
            foreach (SensorAccumulator item in map.Values)
            {
                float samples = Mathf.Max(1, item.value.sampleCount);
                float comparable = Mathf.Max(1, item.value.comparableSampleCount);
                item.value.meanAgeSeconds = item.ageSum / samples;
                item.value.meanAbsoluteDistanceErrorMeters =
                    item.distanceErrorSum / comparable;
                item.value.meanNormalErrorDegrees = item.normalErrorSum / comparable;
                result[index++] = item.value;
            }
            return result;
        }

        private static V3DiagnosticForceSummary[] BuildForceSummaries(
            V3DiagnosticSession session)
        {
            string[] names = { "gravity", "propulsion", "braking", "hover", "roof", "lateral", "aerodynamic", "contact", "expected_net", "observed_net", "residual" };
            var result = new V3DiagnosticForceSummary[names.Length];
            for (int category = 0; category < names.Length; category++)
            {
                float sum = 0f, peak = 0f, vertical = 0f;
                for (int i = 0; i < session.SampleCount; i++)
                {
                    V3DiagnosticSample sample = session.Samples[i];
                    if (sample.identity.externalStateDiscontinuity ||
                        !sample.identity.dynamicsValid)
                        continue;
                    Vector3 force = ForceAt(sample.forces, category);
                    sum += force.magnitude;
                    peak = Mathf.Max(peak, force.magnitude);
                    Vector3 up = sample.world.gravityVector.sqrMagnitude > 0.000001f
                        ? -sample.world.gravityVector.normalized : Vector3.up;
                    vertical += Vector3.Dot(force, up);
                }
                int validSampleCount = 0;
                for (int i = 0; i < session.SampleCount; i++)
                    if (!session.Samples[i].identity.externalStateDiscontinuity &&
                        session.Samples[i].identity.dynamicsValid)
                        validSampleCount++;
                float n = Mathf.Max(1, validSampleCount);
                result[category] = new V3DiagnosticForceSummary
                {
                    category = names[category], sampleCount = validSampleCount,
                    meanMagnitudeN = sum / n, peakMagnitudeN = peak,
                    meanGravityVerticalN = vertical / n
                };
            }
            return result;
        }

        private static void BuildContactFreeResidualSummary(
            V3DiagnosticSession session,
            out int count,
            out float mean,
            out float p95,
            out float maximum)
        {
            var values = new List<float>(session.SampleCount);
            float sum = 0f;
            maximum = 0f;
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample sample = session.Samples[i];
                if (!sample.identity.dynamicsValid ||
                    sample.identity.externalStateDiscontinuity ||
                    sample.world.contactCount > 0 ||
                    sample.world.contactImpulseNs > 0.0001f)
                {
                    continue;
                }
                float residual = sample.forces.residualForce.magnitude;
                values.Add(residual);
                sum += residual;
                maximum = Mathf.Max(maximum, residual);
            }
            values.Sort();
            count = values.Count;
            mean = count > 0 ? sum / count : 0f;
            int p95Index = count > 0
                ? Mathf.Clamp(Mathf.CeilToInt(count * 0.95f) - 1, 0, count - 1)
                : 0;
            p95 = count > 0 ? values[p95Index] : 0f;
        }

        private static bool IsMeaningfullyPowerLimited(
            V3DiagnosticSample sample)
        {
            return V3DiagnosticEventDetector.IsMeaningfullyPowerLimited(
                       sample.power.requestedSystemsPower,
                       sample.power.grantedSystemsPower) ||
                   V3DiagnosticEventDetector.IsMeaningfullyPowerLimited(
                       sample.power.requestedPropulsionPower,
                       sample.power.grantedPropulsionPower);
        }

        private static Vector3 ForceAt(V3ForceTorqueDiagnosticSample value, int index)
        {
            return index switch
            {
                0 => value.gravityForce, 1 => value.propulsionForce,
                2 => value.brakingForce, 3 => value.hoverForce,
                4 => value.roofForce, 5 => value.lateralForce,
                6 => value.aerodynamicForce, 7 => value.contactForceEstimate,
                8 => value.expectedNetForce, 9 => value.observedNetForce,
                _ => value.residualForce
            };
        }
    }
}
