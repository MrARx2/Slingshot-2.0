using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [Serializable]
    public sealed class V3ParitySample
    {
        public float elapsedSeconds;
        public string segment;
        public float segmentElapsedSeconds;
        public Vector3 position;
        public Vector3 localVelocity;
        public Vector3 localAngularVelocity;
        public float speedKmh;
        public float groundClearanceM;
        public float pitchDegrees;
        public float rollDegrees;
        public float yawRateDegreesPerSecond;
        public float requestedPower;
        public float grantedPower;
    }

    [Serializable]
    public sealed class V3ParityRunSummary
    {
        public string craftLabel;
        public string profileName;
        public string generatedUtc;
        public string baselineNote;
        public int sampleCount;
        public float massKg;
        public Vector3 centerOfMass;
        public Vector3 inertiaTensor;
        public Quaternion inertiaTensorRotation;
        public float idleClearanceMeanM;
        public float idleClearanceStandardDeviationM;
        public float maximumSpeedKmh;
        public float timeTo500Kmh = -1f;
        public float timeTo1000Kmh = -1f;
        public float brakeStartSpeedKmh;
        public float brakeEndSpeedKmh;
        public float brakeDistanceM;
        public float peakAbsoluteYawRateDegreesPerSecond;
        public float peakAbsoluteSideSpeedKmh;
        public float peakAbsolutePitchDegrees;
        public float peakRequestedPower;
        public float peakGrantedPower;
    }

    public sealed class V3ParityTrackRecorder
    {
        private readonly List<V3ParitySample> samples =
            new List<V3ParitySample>(4096);

        public V3ParityTrackRecorder(string craftLabel, Rigidbody body)
        {
            CraftLabel = craftLabel;
            Body = body;
        }

        public string CraftLabel { get; }
        public Rigidbody Body { get; }
        public IReadOnlyList<V3ParitySample> Samples => samples;

        public void Capture(
            float elapsedSeconds,
            string segment,
            float segmentElapsedSeconds,
            float requestedPower,
            float grantedPower,
            LayerMask trackSurfaceMask)
        {
            if (Body == null)
            {
                return;
            }

            Transform root = Body.transform;
            Vector3 localVelocity =
                root.InverseTransformDirection(Body.linearVelocity);
            Vector3 localAngularVelocity =
                root.InverseTransformDirection(Body.angularVelocity);
            samples.Add(new V3ParitySample
            {
                elapsedSeconds = elapsedSeconds,
                segment = segment,
                segmentElapsedSeconds = segmentElapsedSeconds,
                position = root.position,
                localVelocity = localVelocity,
                localAngularVelocity = localAngularVelocity,
                speedKmh = Body.linearVelocity.magnitude * 3.6f,
                groundClearanceM = MeasureGroundClearance(root, trackSurfaceMask),
                pitchDegrees = NormalizeSignedAngle(root.eulerAngles.x),
                rollDegrees = NormalizeSignedAngle(root.eulerAngles.z),
                yawRateDegreesPerSecond =
                    localAngularVelocity.y * Mathf.Rad2Deg,
                requestedPower = requestedPower,
                grantedPower = grantedPower
            });
        }

        public V3ParityRunSummary BuildSummary(
            string profileName,
            string baselineNote)
        {
            var summary = new V3ParityRunSummary
            {
                craftLabel = CraftLabel,
                profileName = profileName,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                baselineNote = baselineNote,
                sampleCount = samples.Count,
                massKg = Body != null ? Body.mass : 0f,
                centerOfMass = Body != null ? Body.centerOfMass : Vector3.zero,
                inertiaTensor =
                    Body != null ? Body.inertiaTensor : Vector3.zero,
                inertiaTensorRotation =
                    Body != null ? Body.inertiaTensorRotation : Quaternion.identity
            };

            float idleSum = 0f;
            float idleSquaredSum = 0f;
            int idleCount = 0;
            V3ParitySample brakeFirst = null;
            V3ParitySample brakeLast = null;
            Vector3 brakeStartPosition = Vector3.zero;
            float accelerationStart = -1f;

            for (int i = 0; i < samples.Count; i++)
            {
                V3ParitySample sample = samples[i];
                summary.peakRequestedPower =
                    Mathf.Max(summary.peakRequestedPower, sample.requestedPower);
                summary.peakGrantedPower =
                    Mathf.Max(summary.peakGrantedPower, sample.grantedPower);

                if (Contains(sample.segment, "Yaw"))
                {
                    summary.peakAbsoluteYawRateDegreesPerSecond = Mathf.Max(
                        summary.peakAbsoluteYawRateDegreesPerSecond,
                        Mathf.Abs(sample.yawRateDegreesPerSecond));
                }

                if (Contains(sample.segment, "Strafe"))
                {
                    summary.peakAbsoluteSideSpeedKmh = Mathf.Max(
                        summary.peakAbsoluteSideSpeedKmh,
                        Mathf.Abs(sample.localVelocity.x) * 3.6f);
                }

                if (Contains(sample.segment, "Pitch"))
                {
                    summary.peakAbsolutePitchDegrees = Mathf.Max(
                        summary.peakAbsolutePitchDegrees,
                        Mathf.Abs(sample.pitchDegrees));
                }

                if (Contains(sample.segment, "Idle") && sample.groundClearanceM >= 0f)
                {
                    idleSum += sample.groundClearanceM;
                    idleSquaredSum +=
                        sample.groundClearanceM * sample.groundClearanceM;
                    idleCount++;
                }

                if (Contains(sample.segment, "Acceleration"))
                {
                    summary.maximumSpeedKmh =
                        Mathf.Max(summary.maximumSpeedKmh, sample.speedKmh);
                    if (accelerationStart < 0f)
                    {
                        accelerationStart = sample.elapsedSeconds;
                    }

                    float accelerationElapsed =
                        sample.elapsedSeconds - accelerationStart;
                    if (summary.timeTo500Kmh < 0f && sample.speedKmh >= 500f)
                    {
                        summary.timeTo500Kmh = accelerationElapsed;
                    }

                    if (summary.timeTo1000Kmh < 0f && sample.speedKmh >= 1000f)
                    {
                        summary.timeTo1000Kmh = accelerationElapsed;
                    }
                }

                if (Contains(sample.segment, "Brake"))
                {
                    if (brakeFirst == null)
                    {
                        brakeFirst = sample;
                        brakeStartPosition = sample.position;
                    }

                    brakeLast = sample;
                }
            }

            if (idleCount > 0)
            {
                summary.idleClearanceMeanM = idleSum / idleCount;
                float variance =
                    idleSquaredSum / idleCount -
                    summary.idleClearanceMeanM * summary.idleClearanceMeanM;
                summary.idleClearanceStandardDeviationM =
                    Mathf.Sqrt(Mathf.Max(0f, variance));
            }

            if (brakeFirst != null && brakeLast != null)
            {
                summary.brakeStartSpeedKmh = brakeFirst.speedKmh;
                summary.brakeEndSpeedKmh = brakeLast.speedKmh;
                summary.brakeDistanceM =
                    Vector3.Distance(brakeStartPosition, brakeLast.position);
            }

            return summary;
        }

        public void Export(
            string directory,
            string runId,
            V3ParityRunSummary summary)
        {
            Directory.CreateDirectory(directory);
            string safeLabel = SanitizeFileName(CraftLabel);
            string prefix = Path.Combine(directory, $"{runId}_{safeLabel}");
            File.WriteAllText(prefix + ".csv", BuildCsv(), Encoding.UTF8);
            File.WriteAllText(
                prefix + "_summary.json",
                JsonUtility.ToJson(summary, true),
                Encoding.UTF8);
        }

        private string BuildCsv()
        {
            var builder = new StringBuilder(samples.Count * 180);
            builder.AppendLine(
                "elapsed_s,segment,segment_elapsed_s,pos_x,pos_y,pos_z," +
                "local_vx,local_vy,local_vz,speed_kmh,clearance_m,pitch_deg," +
                "roll_deg,yaw_rate_deg_s,requested_power,granted_power");
            for (int i = 0; i < samples.Count; i++)
            {
                V3ParitySample s = samples[i];
                builder.Append(
                    s.elapsedSeconds.ToString(
                        "0.######",
                        CultureInfo.InvariantCulture));
                builder.Append(',').Append(EscapeCsv(s.segment));
                Append(builder, s.segmentElapsedSeconds);
                Append(builder, s.position.x);
                Append(builder, s.position.y);
                Append(builder, s.position.z);
                Append(builder, s.localVelocity.x);
                Append(builder, s.localVelocity.y);
                Append(builder, s.localVelocity.z);
                Append(builder, s.speedKmh);
                Append(builder, s.groundClearanceM);
                Append(builder, s.pitchDegrees);
                Append(builder, s.rollDegrees);
                Append(builder, s.yawRateDegreesPerSecond);
                Append(builder, s.requestedPower);
                Append(builder, s.grantedPower);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static float MeasureGroundClearance(
            Transform root,
            LayerMask trackSurfaceMask)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                root.position + root.up * 0.05f,
                -root.up,
                20f,
                trackSurfaceMask,
                QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider != null &&
                    !hits[i].collider.transform.IsChildOf(root))
                {
                    closest = Mathf.Min(closest, hits[i].distance - 0.05f);
                }
            }

            return float.IsPositiveInfinity(closest) ? -1f : Mathf.Max(0f, closest);
        }

        private static bool Contains(string value, string fragment)
        {
            return value != null &&
                value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private static void Append(StringBuilder builder, float value)
        {
            builder.Append(',').Append(
                value.ToString("0.######", CultureInfo.InvariantCulture));
        }

        private static string EscapeCsv(string value)
        {
            string safe = value ?? string.Empty;
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        }

        private static string SanitizeFileName(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? "Craft" : value;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                safe = safe.Replace(invalid[i], '_');
            }

            return safe;
        }
    }
}
