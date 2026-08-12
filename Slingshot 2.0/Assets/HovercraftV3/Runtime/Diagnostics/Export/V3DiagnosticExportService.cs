using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [Serializable]
    internal sealed class V3DiagnosticEventCollection
    {
        public int count;
        public V3DiagnosticEvent[] events;
    }

    [Serializable]
    internal sealed class V3DiagnosticChannelCollection
    {
        public int schemaVersion;
        public V3DiagnosticChannelDefinition[] channels;
    }

    [Serializable]
    internal sealed class V3DiagnosticEventContextRecord
    {
        public string eventId;
        public long eventSampleIndex;
        public long relativeSampleOffset;
        public long contextSampleIndex;
        public long contextPhysicsTick;
        public double contextSessionElapsed;
    }

    internal readonly struct V3DiagnosticBinaryFieldDefinition
    {
        public V3DiagnosticBinaryFieldDefinition(
            string id, string unit, V3DiagnosticDataType type,
            V3DiagnosticCoordinateFrame frame, int components = 1)
        {
            this.id = id;
            this.unit = unit;
            this.type = type;
            this.frame = frame;
            this.components = components;
        }

        public readonly string id;
        public readonly string unit;
        public readonly V3DiagnosticDataType type;
        public readonly V3DiagnosticCoordinateFrame frame;
        public readonly int components;
    }

    public sealed class V3DiagnosticExportOptions
    {
        public bool includeNdjson = true;
        public bool includeBinary = true;
        public bool includeCsv = true;
        public bool generateMarkdown = true;
        public bool generateReportJson = true;
    }

    public sealed class V3DiagnosticExportResult
    {
        public bool success;
        public string outputDirectory;
        public string binaryChecksum;
        public double elapsedMilliseconds;
        public string error;
    }

    public static class V3DiagnosticExportService
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly V3DiagnosticBinaryFieldDefinition[] BinaryFields =
        {
            new V3DiagnosticBinaryFieldDefinition("identity.sample_index", "index", V3DiagnosticDataType.Long, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.physics_tick", "tick", V3DiagnosticDataType.Long, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.simulation_time", "s", V3DiagnosticDataType.Double, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.session_elapsed", "s", V3DiagnosticDataType.Double, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.fixed_delta", "s", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.controlled_test_phase", "state", V3DiagnosticDataType.String, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.position", "m", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("world.rotation", "quaternion", V3DiagnosticDataType.Quaternion, V3DiagnosticCoordinateFrame.World, 4),
            new V3DiagnosticBinaryFieldDefinition("world.linear_velocity", "m/s", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("world.angular_velocity", "rad/s", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("world.linear_acceleration", "m/s^2", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("world.gravity", "m/s^2", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("world.environment_valid", "bool", V3DiagnosticDataType.Boolean, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.environment_flags", "flags", V3DiagnosticDataType.Integer, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.craft_physics_tick", "tick", V3DiagnosticDataType.Long, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.world_physics_tick", "tick", V3DiagnosticDataType.Long, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.world_simulation_time", "s", V3DiagnosticDataType.Double, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.altitude", "m", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.World),
            new V3DiagnosticBinaryFieldDefinition("world.environment.temperature", "C", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.environment.pressure", "Pa", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.environment.density", "kg/m3", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.environment.speed_of_sound", "m/s", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("world.environment.air_velocity", "m/s", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("craft.aerodynamics.relative_air", "m/s", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("craft.aerodynamics.mach", "Mach", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("craft.aerodynamics.dynamic_pressure", "Pa", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("craft.aerodynamics.force", "N", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("craft.aerodynamics.torque", "N*m", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("track.distance", "m", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.Track),
            new V3DiagnosticBinaryFieldDefinition("track.section_progress", "ratio", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.Track),
            new V3DiagnosticBinaryFieldDefinition("track.lateral_offset", "m", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.Track),
            new V3DiagnosticBinaryFieldDefinition("track.vertical_offset", "m", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.Track),
            new V3DiagnosticBinaryFieldDefinition("track.true_surface_distance", "m", V3DiagnosticDataType.Float, V3DiagnosticCoordinateFrame.Track),
            new V3DiagnosticBinaryFieldDefinition("contact.world_state", "state", V3DiagnosticDataType.Enum, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("force.expected_net", "N", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("force.observed_net", "N", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("force.residual", "N", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("record.sensor_count", "count", V3DiagnosticDataType.Integer, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("record.task_count", "count", V3DiagnosticDataType.Integer, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("record.request_count", "count", V3DiagnosticDataType.Integer, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("record.device_count", "count", V3DiagnosticDataType.Integer, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.attempt_index", "index", V3DiagnosticDataType.Integer, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.external_state_discontinuity", "bool", V3DiagnosticDataType.Boolean, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("identity.dynamics_valid", "bool", V3DiagnosticDataType.Boolean, V3DiagnosticCoordinateFrame.None),
            new V3DiagnosticBinaryFieldDefinition("craft.inertia_tensor", "kg*m2", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.DeviceLocal, 3),
            new V3DiagnosticBinaryFieldDefinition("craft.inertia_tensor_rotation", "quaternion", V3DiagnosticDataType.Quaternion, V3DiagnosticCoordinateFrame.DeviceLocal, 4),
            new V3DiagnosticBinaryFieldDefinition("force.applied_torque", "N*m", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("craft.angular_acceleration.expected", "rad/s2", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3),
            new V3DiagnosticBinaryFieldDefinition("craft.angular_acceleration.error", "rad/s2", V3DiagnosticDataType.Vector3, V3DiagnosticCoordinateFrame.World, 3)
        };

        public static V3DiagnosticExportResult Export(
            V3DiagnosticSession session,
            string outputRoot,
            V3DiagnosticExportOptions options)
        {
            var result = new V3DiagnosticExportResult();
            var timer = Stopwatch.StartNew();
            try
            {
                if (session == null || session.Manifest == null)
                {
                    throw new ArgumentNullException(nameof(session));
                }
                options ??= new V3DiagnosticExportOptions();
                string root = string.IsNullOrWhiteSpace(outputRoot)
                    ? Path.Combine(Directory.GetCurrentDirectory(), "TestDriveReports", "HovercraftV3")
                    : Path.GetFullPath(outputRoot);
                string folderName = BuildFolderName(session.Manifest);
                string output = CreateUniqueDirectory(root, folderName);
                string charts = Path.Combine(output, "charts");
                string logs = Path.Combine(output, "logs");
                Directory.CreateDirectory(charts);
                Directory.CreateDirectory(logs);
                V3DiagnosticAnalysisReport report =
                    V3DiagnosticReportBuilder.Build(session);
                report.rawDataFiles = BuildRawDataFiles(options);

                WriteJson(Path.Combine(output, "craft_snapshot.json"), session.CraftSnapshot);
                WriteJson(Path.Combine(output, "world_snapshot.json"), session.WorldSnapshot);
                WriteChannelSchema(Path.Combine(output, "channel_schema.json"), session);
                WriteTrackReport(Path.Combine(output, "track_report.json"), session);
                WriteEventsJson(Path.Combine(output, "events.json"), session);
                WriteEventContextNdjson(
                    Path.Combine(output, "event_context.ndjson"), session);

                if (options.includeNdjson)
                {
                    WriteNdjson(Path.Combine(output, "samples.ndjson"), session);
                }
                if (options.includeBinary)
                {
                    result.binaryChecksum = WriteBinary(
                        Path.Combine(output, "samples_compact.bin"), session);
                }
                if (options.includeCsv)
                {
                    WriteCsvPackage(output, charts, session, report);
                }
                if (options.generateReportJson)
                {
                    WriteJson(Path.Combine(output, "report.json"), report);
                }
                if (options.generateMarkdown)
                {
                    WriteMarkdown(Path.Combine(output, "report.md"), session, report);
                }
                File.WriteAllText(
                    Path.Combine(logs, "recorder.log"),
                    "Hovercraft V3 diagnostic export completed at " +
                    DateTime.UtcNow.ToString("O") + Environment.NewLine,
                    Utf8NoBom);

                timer.Stop();
                session.SetExportResult(output, timer.Elapsed.TotalMilliseconds,
                    result.binaryChecksum);
                WriteJson(Path.Combine(output, "session_manifest.json"), session.Manifest);
                result.success = true;
                result.outputDirectory = output;
                result.elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
            catch (Exception exception)
            {
                timer.Stop();
                result.success = false;
                result.error = exception.ToString();
                result.elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
            return result;
        }

        /// <summary>
        /// Performs the export in bounded pieces on Unity's main thread. JsonUtility
        /// remains on the main thread, while large sample streams yield regularly so
        /// stopping a recording never monopolizes the gameplay frame.
        /// </summary>
        public static IEnumerator ExportIncremental(
            V3DiagnosticSession session,
            string outputRoot,
            V3DiagnosticExportOptions options,
            V3DiagnosticExportResult result,
            Action<float, string> progress,
            int recordsPerFrame = 8)
        {
            if (session == null || session.Manifest == null)
                throw new ArgumentNullException(nameof(session));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            options ??= new V3DiagnosticExportOptions();
            recordsPerFrame = Math.Max(1, recordsPerFrame);
            var timer = Stopwatch.StartNew();
            string root = string.IsNullOrWhiteSpace(outputRoot)
                ? Path.Combine(Directory.GetCurrentDirectory(), "TestDriveReports", "HovercraftV3")
                : Path.GetFullPath(outputRoot);
            string output = CreateUniqueDirectory(root,
                BuildFolderName(session.Manifest));
            string charts = Path.Combine(output, "charts");
            string logs = Path.Combine(output, "logs");
            Directory.CreateDirectory(charts);
            Directory.CreateDirectory(logs);
            result.outputDirectory = output;

            progress?.Invoke(0.02f, "Analyzing synchronized samples");
            yield return null;
            V3DiagnosticAnalysisReport report =
                V3DiagnosticReportBuilder.Build(session);
            report.rawDataFiles = BuildRawDataFiles(options);

            progress?.Invoke(0.08f, "Writing session snapshots");
            WriteJson(Path.Combine(output, "craft_snapshot.json"), session.CraftSnapshot);
            yield return null;
            WriteJson(Path.Combine(output, "world_snapshot.json"), session.WorldSnapshot);
            yield return null;
            WriteChannelSchema(Path.Combine(output, "channel_schema.json"), session);
            yield return null;
            WriteTrackReport(Path.Combine(output, "track_report.json"), session);
            yield return null;
            WriteEventsJson(Path.Combine(output, "events.json"), session);
            yield return null;

            IEnumerator stage = WriteEventContextNdjsonIncremental(
                Path.Combine(output, "event_context.ndjson"), session,
                recordsPerFrame * 4,
                value => progress?.Invoke(0.14f + value * 0.08f,
                    "Writing event context index"));
            while (stage.MoveNext())
                yield return null;

            if (options.includeNdjson)
            {
                stage = WriteNdjsonIncremental(
                    Path.Combine(output, "samples.ndjson"), session,
                    recordsPerFrame,
                    value => progress?.Invoke(0.22f + value * 0.38f,
                        "Writing full sample records"));
                while (stage.MoveNext())
                    yield return null;
            }

            if (options.includeBinary)
            {
                stage = WriteBinaryIncremental(
                    Path.Combine(output, "samples_compact.bin"), session,
                    recordsPerFrame * 8,
                    value => progress?.Invoke(0.60f + value * 0.10f,
                        "Writing compact binary stream"),
                    checksum => result.binaryChecksum = checksum);
                while (stage.MoveNext())
                    yield return null;
            }

            if (options.includeCsv)
            {
                stage = WriteCsvPackageIncremental(
                    output, charts, session, report,
                    value => progress?.Invoke(0.72f + value * 0.14f,
                        "Writing analysis tables"));
                while (stage.MoveNext())
                    yield return null;
            }
            if (options.generateReportJson)
            {
                progress?.Invoke(0.86f, "Writing JSON report");
                WriteJson(Path.Combine(output, "report.json"), report);
                yield return null;
            }
            if (options.generateMarkdown)
            {
                progress?.Invoke(0.92f, "Writing Markdown report");
                WriteMarkdown(Path.Combine(output, "report.md"), session, report);
                yield return null;
            }

            File.WriteAllText(
                Path.Combine(logs, "recorder.log"),
                "Hovercraft V3 diagnostic export completed at " +
                DateTime.UtcNow.ToString("O") + Environment.NewLine,
                Utf8NoBom);
            timer.Stop();
            session.SetExportResult(output, timer.Elapsed.TotalMilliseconds,
                result.binaryChecksum);
            WriteJson(Path.Combine(output, "session_manifest.json"), session.Manifest);
            result.success = true;
            result.elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            progress?.Invoke(1f, "Export complete");
        }

        private static string BuildFolderName(V3DiagnosticSessionManifest manifest)
        {
            string label = string.IsNullOrWhiteSpace(manifest.sessionLabel)
                ? manifest.craftBuildStableId
                : manifest.sessionLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = "DiagnosticRun";
            }
            return DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss", Invariant) + "_" +
                SanitizeFileName(label) + "_" + SanitizeFileName(manifest.sessionId);
        }

        private static string CreateUniqueDirectory(string root, string folderName)
        {
            Directory.CreateDirectory(root);
            string candidate = Path.Combine(root, folderName);
            int suffix = 1;
            while (Directory.Exists(candidate))
            {
                candidate = Path.Combine(root, folderName + "_" + suffix.ToString("000", Invariant));
                suffix++;
            }
            Directory.CreateDirectory(candidate);
            return candidate;
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value ?? string.Empty);
            for (int i = 0; i < builder.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (builder[i] == invalid[j])
                    {
                        builder[i] = '_';
                        break;
                    }
                }
            }
            return builder.ToString().Replace(' ', '_');
        }

        private static void WriteJson(string path, object value)
        {
            File.WriteAllText(path, JsonUtility.ToJson(value, true), Utf8NoBom);
        }

        private static void WriteTrackReport(string path, V3DiagnosticSession session)
        {
            if (session.TrackSnapshot != null)
            {
                WriteJson(path, session.TrackSnapshot);
                return;
            }
            var value = new V3DiagnosticTrackExportHeader
            {
                schemaVersion = V3DiagnosticSchema.Version,
                trackInstanceId = session.WorldSnapshot != null
                    ? session.WorldSnapshot.trackInstanceId : 0,
                generationRevision = session.WorldSnapshot != null
                    ? session.WorldSnapshot.trackGenerationRevision : 0,
                sectionCount = session.WorldSnapshot != null
                    ? session.WorldSnapshot.trackSectionCount : 0,
                analyzerStatus = "Track geometry analysis is recorded when a track analyzer source is bound."
            };
            WriteJson(path, value);
        }

        private static void WriteEventsJson(string path, V3DiagnosticSession session)
        {
            var copied = new V3DiagnosticEvent[session.EventCount];
            Array.Copy(session.Events, copied, copied.Length);
            WriteJson(path, new V3DiagnosticEventCollection
            {
                count = copied.Length,
                events = copied
            });
        }

        private static void WriteNdjson(string path, V3DiagnosticSession session)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom, 65536);
            for (int i = 0; i < session.SampleCount; i++)
            {
                writer.WriteLine(JsonUtility.ToJson(
                    TrimSample(session.Samples[i]), false));
            }
        }

        private static IEnumerator WriteNdjsonIncremental(
            string path,
            V3DiagnosticSession session,
            int recordsPerFrame,
            Action<float> progress)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom, 65536);
            int count = Math.Max(1, session.SampleCount);
            for (int i = 0; i < session.SampleCount; i++)
            {
                writer.WriteLine(JsonUtility.ToJson(
                    TrimSample(session.Samples[i]), false));
                if ((i + 1) % recordsPerFrame == 0)
                {
                    progress?.Invoke((i + 1f) / count);
                    yield return null;
                }
            }
            progress?.Invoke(1f);
        }

        private static void WriteEventContextNdjson(
            string path, V3DiagnosticSession session)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom, 65536);
            for (int eventIndex = 0;
                 eventIndex < session.EventCount;
                 eventIndex++)
            {
                V3DiagnosticEvent item = session.Events[eventIndex];
                if (item == null || session.SampleCount == 0) continue;
                int start = (int)Math.Max(0L, Math.Min(
                    session.SampleCount - 1L, item.preEventStartSample));
                int end = (int)Math.Max(start, Math.Min(
                    session.SampleCount - 1L, item.postEventEndSample));
                for (int sampleIndex = start;
                     sampleIndex <= end;
                     sampleIndex++)
                {
                    writer.WriteLine(JsonUtility.ToJson(
                        new V3DiagnosticEventContextRecord
                        {
                            eventId = item.eventId,
                            eventSampleIndex = item.sampleIndex,
                            relativeSampleOffset = sampleIndex - item.sampleIndex,
                            contextSampleIndex =
                                session.Samples[sampleIndex].identity.sampleIndex,
                            contextPhysicsTick =
                                session.Samples[sampleIndex].identity.physicsTick,
                            contextSessionElapsed =
                                session.Samples[sampleIndex].identity.sessionElapsed
                        }, false));
                }
            }
        }

        private static IEnumerator WriteEventContextNdjsonIncremental(
            string path,
            V3DiagnosticSession session,
            int recordsPerFrame,
            Action<float> progress)
        {
            long total = 0;
            for (int eventIndex = 0; eventIndex < session.EventCount; eventIndex++)
            {
                V3DiagnosticEvent item = session.Events[eventIndex];
                if (item == null || session.SampleCount == 0) continue;
                int start = (int)Math.Max(0L, Math.Min(
                    session.SampleCount - 1L, item.preEventStartSample));
                int end = (int)Math.Max(start, Math.Min(
                    session.SampleCount - 1L, item.postEventEndSample));
                total += end - start + 1L;
            }

            using var writer = new StreamWriter(path, false, Utf8NoBom, 65536);
            long written = 0;
            for (int eventIndex = 0; eventIndex < session.EventCount; eventIndex++)
            {
                V3DiagnosticEvent item = session.Events[eventIndex];
                if (item == null || session.SampleCount == 0) continue;
                int start = (int)Math.Max(0L, Math.Min(
                    session.SampleCount - 1L, item.preEventStartSample));
                int end = (int)Math.Max(start, Math.Min(
                    session.SampleCount - 1L, item.postEventEndSample));
                for (int sampleIndex = start; sampleIndex <= end; sampleIndex++)
                {
                    V3DiagnosticSample sample = session.Samples[sampleIndex];
                    writer.WriteLine(JsonUtility.ToJson(
                        new V3DiagnosticEventContextRecord
                        {
                            eventId = item.eventId,
                            eventSampleIndex = item.sampleIndex,
                            relativeSampleOffset = sampleIndex - item.sampleIndex,
                            contextSampleIndex = sample.identity.sampleIndex,
                            contextPhysicsTick = sample.identity.physicsTick,
                            contextSessionElapsed = sample.identity.sessionElapsed
                        }, false));
                    written++;
                    if (written % recordsPerFrame == 0)
                    {
                        progress?.Invoke(total > 0 ? (float)written / total : 1f);
                        yield return null;
                    }
                }
            }
            progress?.Invoke(1f);
        }

        private static V3DiagnosticSample TrimSample(V3DiagnosticSample sample)
        {
            sample.sensors = CopyPrefix(sample.sensors, sample.sensorCount);
            sample.tasks = CopyPrefix(sample.tasks, sample.taskCount);
            sample.observations = CopyPrefix(
                sample.observations, sample.observationCount);
            sample.requests = CopyPrefix(sample.requests, sample.requestCount);
            sample.devices = CopyPrefix(sample.devices, sample.deviceCount);
            return sample;
        }

        private static T[] CopyPrefix<T>(T[] source, int count)
        {
            int safeCount = source != null
                ? Mathf.Clamp(count, 0, source.Length)
                : 0;
            if (safeCount == 0)
                return Array.Empty<T>();
            var result = new T[safeCount];
            Array.Copy(source, result, safeCount);
            return result;
        }

        private static string WriteBinary(string path, V3DiagnosticSession session)
        {
            var crc = new V3DiagnosticCrc32();
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            using (var checksumStream = new V3DiagnosticChecksumStream(stream, crc))
            using (var writer = new BinaryWriter(checksumStream, Utf8NoBom, true))
            {
                writer.Write(V3DiagnosticSchema.BinaryMagic);
                writer.Write(V3DiagnosticSchema.Version);
                writer.Write(BitConverter.IsLittleEndian);
                writer.Write(BinaryFields.Length);
                for (int i = 0; i < BinaryFields.Length; i++)
                {
                    V3DiagnosticBinaryFieldDefinition field = BinaryFields[i];
                    writer.Write(field.id ?? string.Empty);
                    writer.Write(field.unit ?? string.Empty);
                    writer.Write((int)field.type);
                    writer.Write((int)field.frame);
                    writer.Write(field.components);
                }
                writer.Write(session.SampleCount);
                for (int i = 0; i < session.SampleCount; i++)
                {
                    WriteCompactSample(writer, session.Samples[i]);
                }
                writer.Flush();
            }
            string checksum = crc.Value.ToString("X8", Invariant);
            File.WriteAllText(path + ".crc32", checksum + Environment.NewLine, Utf8NoBom);
            return checksum;
        }

        private static IEnumerator WriteBinaryIncremental(
            string path,
            V3DiagnosticSession session,
            int recordsPerFrame,
            Action<float> progress,
            Action<string> completed)
        {
            var crc = new V3DiagnosticCrc32();
            using (var stream = new FileStream(path, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 65536))
            using (var checksumStream = new V3DiagnosticChecksumStream(stream, crc))
            using (var writer = new BinaryWriter(checksumStream, Utf8NoBom, true))
            {
                writer.Write(V3DiagnosticSchema.BinaryMagic);
                writer.Write(V3DiagnosticSchema.Version);
                writer.Write(BitConverter.IsLittleEndian);
                writer.Write(BinaryFields.Length);
                for (int i = 0; i < BinaryFields.Length; i++)
                {
                    V3DiagnosticBinaryFieldDefinition field = BinaryFields[i];
                    writer.Write(field.id ?? string.Empty);
                    writer.Write(field.unit ?? string.Empty);
                    writer.Write((int)field.type);
                    writer.Write((int)field.frame);
                    writer.Write(field.components);
                }
                writer.Write(session.SampleCount);
                int count = Math.Max(1, session.SampleCount);
                for (int i = 0; i < session.SampleCount; i++)
                {
                    WriteCompactSample(writer, session.Samples[i]);
                    if ((i + 1) % recordsPerFrame == 0)
                    {
                        progress?.Invoke((i + 1f) / count);
                        yield return null;
                    }
                }
                writer.Flush();
            }
            string checksum = crc.Value.ToString("X8", Invariant);
            File.WriteAllText(path + ".crc32", checksum + Environment.NewLine,
                Utf8NoBom);
            completed?.Invoke(checksum);
            progress?.Invoke(1f);
        }

        private static void WriteCompactSample(BinaryWriter writer, V3DiagnosticSample sample)
        {
            writer.Write(sample.identity.sampleIndex);
            writer.Write(sample.identity.physicsTick);
            writer.Write(sample.identity.simulationTimestamp);
            writer.Write(sample.identity.sessionElapsed);
            writer.Write(sample.identity.fixedDelta);
            writer.Write(sample.identity.controlledTestPhase ?? string.Empty);
            WriteVector(writer, sample.world.position);
            WriteQuaternion(writer, sample.world.rotation);
            WriteVector(writer, sample.world.linearVelocity);
            WriteVector(writer, sample.world.angularVelocity);
            WriteVector(writer, sample.world.linearAcceleration);
            WriteVector(writer, sample.world.gravityVector);
            writer.Write(sample.world.environmentValid);
            writer.Write(sample.world.environmentFlags);
            writer.Write(sample.world.craftPhysicsTickId);
            writer.Write(sample.world.worldPhysicsTickId);
            writer.Write(sample.world.worldSimulationTime);
            writer.Write(sample.world.altitudeMeters);
            writer.Write(sample.world.ambientTemperatureC);
            writer.Write(sample.world.atmosphericPressurePa);
            writer.Write(sample.world.airDensityKgPerCubicMeter);
            writer.Write(sample.world.speedOfSoundMetersPerSecond);
            WriteVector(writer, sample.world.localAirVelocity);
            WriteVector(writer, sample.world.relativeAirVelocity);
            writer.Write(sample.world.machNumber);
            writer.Write(sample.world.dynamicPressurePa);
            WriteVector(writer, sample.world.aerodynamicForce);
            WriteVector(writer, sample.world.aerodynamicTorque);
            writer.Write(sample.track.distanceAlongTrack);
            writer.Write(sample.track.sectionProgress);
            writer.Write(sample.track.signedLateralOffset);
            writer.Write(sample.track.signedVerticalOffset);
            writer.Write(sample.track.trueSurfaceDistance);
            writer.Write((int)sample.worldContactState);
            WriteVector(writer, sample.forces.expectedNetForce);
            WriteVector(writer, sample.forces.observedNetForce);
            WriteVector(writer, sample.forces.residualForce);
            writer.Write(sample.sensorCount);
            writer.Write(sample.taskCount);
            writer.Write(sample.requestCount);
            writer.Write(sample.deviceCount);
            writer.Write(sample.identity.attemptIndex);
            writer.Write(sample.identity.externalStateDiscontinuity);
            writer.Write(sample.identity.dynamicsValid);
            WriteVector(writer, sample.forces.inertiaTensor);
            WriteQuaternion(writer, sample.forces.inertiaTensorRotation);
            WriteVector(writer, sample.forces.appliedTorque);
            WriteVector(writer, sample.forces.expectedAngularAcceleration);
            WriteVector(writer, sample.forces.angularAccelerationError);
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x); writer.Write(value.y);
            writer.Write(value.z); writer.Write(value.w);
        }

        private static void WriteChannelSchema(
            string path, V3DiagnosticSession session)
        {
            var values = new V3DiagnosticChannelDefinition[session.Channels.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = session.Channels.Channels[i];
            WriteJson(path, new V3DiagnosticChannelCollection
            {
                schemaVersion = V3DiagnosticSchema.Version,
                channels = values
            });
        }

        private static string[] BuildRawDataFiles(
            V3DiagnosticExportOptions options)
        {
            var files = new System.Collections.Generic.List<string>(8)
            {
                "channel_schema.json",
                "events.json",
                "event_context.ndjson",
                "track_report.json"
            };
            if (options.includeNdjson) files.Add("samples.ndjson");
            if (options.includeBinary)
            {
                files.Add("samples_compact.bin");
                files.Add("samples_compact.bin.crc32");
            }
            if (options.includeCsv)
            {
                files.Add("samples_core.csv");
                files.Add("device_samples.csv");
                files.Add("attempt_summary.csv");
            }
            return files.ToArray();
        }

        private static void WriteCsvPackage(
            string output, string charts, V3DiagnosticSession session,
            V3DiagnosticAnalysisReport report)
        {
            WriteCoreSamplesCsv(Path.Combine(output, "samples_core.csv"), session);
            WriteDeviceSamplesCsv(Path.Combine(output, "device_samples.csv"), session);
            WriteEventsCsv(Path.Combine(output, "events.csv"), session);
            WriteTrackSectionsCsv(Path.Combine(output, "track_sections.csv"), session);
            WriteSectionSummaryCsv(Path.Combine(output, "section_summary.csv"), report);
            WriteAttemptSummaryCsv(Path.Combine(output, "attempt_summary.csv"), report);
            WriteDeviceSummaryCsv(Path.Combine(output, "device_summary.csv"), report);
            WriteSystemSummaryCsv(Path.Combine(output, "system_summary.csv"), report);
            WriteSensorSummaryCsv(Path.Combine(output, "sensor_summary.csv"), report);
            WriteForceSummaryCsv(Path.Combine(output, "force_summary.csv"), report);
            WriteLocationClustersCsv(
                Path.Combine(output, "event_location_clusters.csv"), report);
            WriteSpeedChart(Path.Combine(charts, "speed_over_distance.csv"), session);
            WriteRideHeightChart(Path.Combine(charts, "ride_height_over_distance.csv"), session);
            WriteForceChart(Path.Combine(charts, "vertical_force_over_distance.csv"), session);
            WritePowerChart(Path.Combine(charts, "power_over_time.csv"), session);
            WriteSystemRatesChart(
                Path.Combine(charts, "system_rates_over_time.csv"), session);
            WriteEventTimeline(Path.Combine(charts, "event_timeline.csv"), session);
        }

        private static IEnumerator WriteCsvPackageIncremental(
            string output,
            string charts,
            V3DiagnosticSession session,
            V3DiagnosticAnalysisReport report,
            Action<float> progress)
        {
            const int fileCount = 17;
            int completed = 0;
            WriteCoreSamplesCsv(Path.Combine(output, "samples_core.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteDeviceSamplesCsv(Path.Combine(output, "device_samples.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteEventsCsv(Path.Combine(output, "events.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteTrackSectionsCsv(Path.Combine(output, "track_sections.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteSectionSummaryCsv(Path.Combine(output, "section_summary.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteAttemptSummaryCsv(Path.Combine(output, "attempt_summary.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteDeviceSummaryCsv(Path.Combine(output, "device_summary.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteSystemSummaryCsv(Path.Combine(output, "system_summary.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteSensorSummaryCsv(Path.Combine(output, "sensor_summary.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteForceSummaryCsv(Path.Combine(output, "force_summary.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteLocationClustersCsv(
                Path.Combine(output, "event_location_clusters.csv"), report);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteSpeedChart(Path.Combine(charts, "speed_over_distance.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteRideHeightChart(
                Path.Combine(charts, "ride_height_over_distance.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteForceChart(
                Path.Combine(charts, "vertical_force_over_distance.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WritePowerChart(Path.Combine(charts, "power_over_time.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteSystemRatesChart(
                Path.Combine(charts, "system_rates_over_time.csv"), session);
            progress?.Invoke(++completed / (float)fileCount); yield return null;
            WriteEventTimeline(Path.Combine(charts, "event_timeline.csv"), session);
            progress?.Invoke(++completed / (float)fileCount);
        }

        private static void WriteCoreSamplesCsv(string path, V3DiagnosticSession session)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("sample_index,recorder_physics_tick,craft_physics_tick,world_physics_tick,world_simulation_time_s,session_time_s,controlled_test_phase,attempt_index,external_state_discontinuity,dynamics_valid,reset_reason,track_distance_m,section_id,pos_x,pos_y,pos_z,vel_x,vel_y,vel_z,speed_mps,acc_x,acc_y,acc_z,environment_valid,altitude_m,temperature_c,pressure_pa,density_kg_m3,speed_of_sound_mps,air_vel_x,air_vel_y,air_vel_z,relative_air_x,relative_air_y,relative_air_z,mach,dynamic_pressure_pa,dominant_zone,world_state");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample s = session.Samples[i];
                writer.Write(s.identity.sampleIndex); writer.Write(',');
                writer.Write(s.identity.physicsTick); writer.Write(',');
                writer.Write(s.world.craftPhysicsTickId); writer.Write(',');
                writer.Write(s.world.worldPhysicsTickId); writer.Write(',');
                WriteNumber(writer, s.world.worldSimulationTime); writer.Write(',');
                WriteNumber(writer, s.identity.sessionElapsed); writer.Write(',');
                WriteCsvText(writer, s.identity.controlledTestPhase); writer.Write(',');
                writer.Write(s.identity.attemptIndex); writer.Write(',');
                writer.Write(s.identity.externalStateDiscontinuity
                    ? "true" : "false"); writer.Write(',');
                writer.Write(s.identity.dynamicsValid
                    ? "true" : "false"); writer.Write(',');
                WriteCsvText(writer, s.identity.resetReason); writer.Write(',');
                WriteNumber(writer, s.track.distanceAlongTrack); writer.Write(',');
                WriteCsvText(writer, s.track.sectionId); writer.Write(',');
                WriteVectorCsv(writer, s.world.position); writer.Write(',');
                WriteVectorCsv(writer, s.world.linearVelocity); writer.Write(',');
                WriteNumber(writer, s.world.linearVelocity.magnitude); writer.Write(',');
                WriteVectorCsv(writer, s.world.linearAcceleration); writer.Write(',');
                writer.Write(s.world.environmentValid ? "true" : "false");
                writer.Write(',');
                WriteNumber(writer, s.world.altitudeMeters); writer.Write(',');
                WriteNumber(writer, s.world.ambientTemperatureC); writer.Write(',');
                WriteNumber(writer, s.world.atmosphericPressurePa); writer.Write(',');
                WriteNumber(writer, s.world.airDensityKgPerCubicMeter); writer.Write(',');
                WriteNumber(writer, s.world.speedOfSoundMetersPerSecond); writer.Write(',');
                WriteVectorCsv(writer, s.world.localAirVelocity); writer.Write(',');
                WriteVectorCsv(writer, s.world.relativeAirVelocity); writer.Write(',');
                WriteNumber(writer, s.world.machNumber); writer.Write(',');
                WriteNumber(writer, s.world.dynamicPressurePa); writer.Write(',');
                WriteCsvText(writer, s.world.dominantZoneId); writer.Write(',');
                writer.WriteLine(s.worldContactState.ToString());
            }
        }

        private static void WriteEventsCsv(string path, V3DiagnosticSession session)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("event_id,type,severity,timestamp_s,tick,sample_index,attempt_index,reset_reason,track_distance_m,section_id,speed_mps,world_state,manual,notes");
            for (int i = 0; i < session.EventCount; i++)
            {
                V3DiagnosticEvent e = session.Events[i];
                WriteCsvText(writer, e.eventId); writer.Write(',');
                writer.Write(e.type); writer.Write(','); writer.Write(e.severity); writer.Write(',');
                WriteNumber(writer, e.timestamp); writer.Write(','); writer.Write(e.tick); writer.Write(',');
                writer.Write(e.sampleIndex); writer.Write(',');
                writer.Write(e.attemptIndex); writer.Write(',');
                WriteCsvText(writer, e.resetReason); writer.Write(',');
                WriteNumber(writer, e.trackDistance); writer.Write(',');
                WriteCsvText(writer, e.sectionId); writer.Write(','); WriteNumber(writer, e.speedMetersPerSecond); writer.Write(',');
                writer.Write(e.worldState); writer.Write(','); writer.Write(e.manual ? "true" : "false"); writer.Write(',');
                WriteCsvText(writer, e.notes); writer.WriteLine();
            }
        }

        private static void WriteDeviceSamplesCsv(
            string path,
            V3DiagnosticSession session)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("sample_index,session_time_s,device_id,device_type,socket_id,requested_output,actual_output,force_n,airflow_x,airflow_y,airflow_z,aoa_deg,cl,cd,stalled,reverse_flow,lift_n,drag_n,structural_warning,structural_limited");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample sample = session.Samples[i];
                for (int j = 0; j < sample.deviceCount; j++)
                {
                    V3DeviceDiagnosticRecord device = sample.devices[j];
                    writer.Write(sample.identity.sampleIndex); writer.Write(',');
                    WriteNumber(writer, sample.identity.sessionElapsed); writer.Write(',');
                    WriteCsvText(writer, device.deviceId); writer.Write(',');
                    WriteCsvText(writer, device.deviceType); writer.Write(',');
                    WriteCsvText(writer, device.socketId); writer.Write(',');
                    WriteNumber(writer, device.requestedOutput); writer.Write(',');
                    WriteNumber(writer, device.actualOutput); writer.Write(',');
                    WriteNumber(writer, device.actualForce.magnitude); writer.Write(',');
                    WriteVectorCsv(writer, device.finRelativeAirflow); writer.Write(',');
                    WriteNumber(writer, device.finAngleOfAttackDegrees); writer.Write(',');
                    WriteNumber(writer, device.finLiftCoefficient); writer.Write(',');
                    WriteNumber(writer, device.finDragCoefficient); writer.Write(',');
                    writer.Write(device.finStalled ? "true" : "false"); writer.Write(',');
                    writer.Write(device.finReverseFlow ? "true" : "false"); writer.Write(',');
                    WriteNumber(writer, device.finLiftForce.magnitude); writer.Write(',');
                    WriteNumber(writer, device.finDragForce.magnitude); writer.Write(',');
                    writer.Write(device.finStructuralWarning ? "true" : "false"); writer.Write(',');
                    writer.WriteLine(device.finStructurallyLimited ? "true" : "false");
                }
            }
        }

        private static void WriteSpeedChart(string path, V3DiagnosticSession session)
        {
            using var writer = CreateChart(path, "track_distance_m,speed_mps,session_time_s");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample s = session.Samples[i];
                WriteNumber(writer, s.track.distanceAlongTrack); writer.Write(',');
                WriteNumber(writer, s.world.linearVelocity.magnitude); writer.Write(',');
                WriteNumber(writer, s.identity.sessionElapsed); writer.WriteLine();
            }
        }

        private static void WriteRideHeightChart(string path, V3DiagnosticSession session)
        {
            using var writer = CreateChart(path,
                "track_distance_m,true_surface_distance_m,believed_surface_distance_m,target_height_m,surface_separation_velocity_mps,surface_tracking_acceleration_mps2,roof_capture_acceleration_mps2,roof_capture_output,session_time_s");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample s = session.Samples[i];
                WriteNumber(writer, s.track.distanceAlongTrack); writer.Write(',');
                WriteNumber(writer, s.track.trueSurfaceDistance); writer.Write(',');
                WriteNumber(writer, s.belief.believedSurfaceDistance); writer.Write(',');
                WriteNumber(writer, s.belief.targetRideHeight); writer.Write(',');
                WriteNumber(writer, s.belief.believedSurfaceSeparationVelocity); writer.Write(',');
                WriteNumber(writer, s.belief.surfaceTrackingAcceleration); writer.Write(',');
                WriteNumber(writer, s.belief.roofCaptureAcceleration); writer.Write(',');
                WriteNumber(writer, s.belief.roofCaptureOutput); writer.Write(',');
                WriteNumber(writer, s.identity.sessionElapsed); writer.WriteLine();
            }
        }

        private static void WriteForceChart(string path, V3DiagnosticSession session)
        {
            using var writer = CreateChart(path, "track_distance_m,expected_vertical_n,observed_vertical_n,residual_vertical_n,session_time_s");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample s = session.Samples[i];
                WriteNumber(writer, s.track.distanceAlongTrack); writer.Write(',');
                WriteNumber(writer, s.forces.expectedNetForce.y); writer.Write(',');
                WriteNumber(writer, s.forces.observedNetForce.y); writer.Write(',');
                WriteNumber(writer, s.forces.residualForce.y); writer.Write(',');
                WriteNumber(writer, s.identity.sessionElapsed); writer.WriteLine();
            }
        }

        private static void WritePowerChart(string path, V3DiagnosticSession session)
        {
            using var writer = CreateChart(path, "session_time_s,systems_requested,systems_granted,propulsion_requested,propulsion_granted");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample s = session.Samples[i];
                WriteNumber(writer, s.identity.sessionElapsed); writer.Write(',');
                WriteNumber(writer, s.power.requestedSystemsPower); writer.Write(',');
                WriteNumber(writer, s.power.grantedSystemsPower); writer.Write(',');
                WriteNumber(writer, s.power.requestedPropulsionPower); writer.Write(',');
                WriteNumber(writer, s.power.grantedPropulsionPower); writer.WriteLine();
            }
        }

        private static void WriteSystemRatesChart(
            string path, V3DiagnosticSession session)
        {
            using var writer = CreateChart(path,
                "session_time_s,task_id,requested_hz,granted_hz,measured_hz");
            for (int i = 0; i < session.SampleCount; i++)
            {
                V3DiagnosticSample sample = session.Samples[i];
                for (int taskIndex = 0;
                     taskIndex < sample.taskCount;
                     taskIndex++)
                {
                    V3TaskDiagnosticRecord task = sample.tasks[taskIndex];
                    WriteNumber(writer, sample.identity.sessionElapsed);
                    writer.Write(',');
                    WriteCsvText(writer, task.taskId); writer.Write(',');
                    WriteNumber(writer, task.requestedRateHz); writer.Write(',');
                    WriteNumber(writer, task.grantedRateHz); writer.Write(',');
                    WriteNumber(writer, task.measuredRateHz); writer.WriteLine();
                }
            }
        }

        private static void WriteEventTimeline(string path, V3DiagnosticSession session)
        {
            using var writer = CreateChart(path, "timestamp_s,track_distance_m,event_type,severity,section_id");
            for (int i = 0; i < session.EventCount; i++)
            {
                V3DiagnosticEvent e = session.Events[i];
                WriteNumber(writer, e.timestamp); writer.Write(',');
                WriteNumber(writer, e.trackDistance); writer.Write(',');
                writer.Write(e.type); writer.Write(','); writer.Write(e.severity); writer.Write(',');
                WriteCsvText(writer, e.sectionId); writer.WriteLine();
            }
        }

        private static StreamWriter CreateChart(string path, string header)
        {
            var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine(header);
            return writer;
        }

        private static void WriteTrackSectionsCsv(
            string path, V3DiagnosticSession session)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("section_id,section_index,section_type,debug_name,pattern_id,quarter,road,start_distance_m,end_distance_m,length_m,start_width_m,end_width_m,open_start,open_end,air_gap,allows_jump,centerline_samples");
            V3TrackSectionDiagnosticSnapshot[] sections =
                session.TrackSnapshot != null ? session.TrackSnapshot.sections : null;
            if (sections == null) return;
            for (int i = 0; i < sections.Length; i++)
            {
                V3TrackSectionDiagnosticSnapshot section = sections[i];
                if (section == null) continue;
                WriteCsvText(writer, section.sectionId); writer.Write(',');
                writer.Write(section.sectionIndex); writer.Write(',');
                WriteCsvText(writer, section.sectionType); writer.Write(',');
                WriteCsvText(writer, section.debugName); writer.Write(',');
                WriteCsvText(writer, section.patternId); writer.Write(',');
                writer.Write(section.quarterIndex); writer.Write(',');
                writer.Write(section.roadId); writer.Write(',');
                WriteNumber(writer, section.startDistance); writer.Write(',');
                WriteNumber(writer, section.endDistance); writer.Write(',');
                WriteNumber(writer, section.length); writer.Write(',');
                WriteNumber(writer, section.startWidth); writer.Write(',');
                WriteNumber(writer, section.endWidth); writer.Write(',');
                writer.Write(section.openStart ? "true" : "false"); writer.Write(',');
                writer.Write(section.openEnd ? "true" : "false"); writer.Write(',');
                writer.Write(section.airGap ? "true" : "false"); writer.Write(',');
                writer.Write(section.allowsJump ? "true" : "false"); writer.Write(',');
                writer.WriteLine(section.centerlineSampleCount);
            }
        }

        private static void WriteSectionSummaryCsv(
            string path, V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("section_id,section_type,sample_count,duration_s,min_speed_mps,max_speed_mps,mean_speed_mps,max_abs_lateral_offset_m,min_ride_height_m,max_ride_height_m,max_abs_vertical_acceleration_mps2,unexpected_airborne_samples,power_limited_samples,event_count");
            for (int i = 0; i < report.sections.Length; i++)
            {
                V3DiagnosticSectionSummary v = report.sections[i];
                WriteCsvText(writer, v.sectionId); writer.Write(',');
                WriteCsvText(writer, v.sectionType); writer.Write(',');
                writer.Write(v.sampleCount); writer.Write(',');
                WriteNumber(writer, v.durationSeconds); writer.Write(',');
                WriteNumber(writer, v.minimumSpeedMetersPerSecond); writer.Write(',');
                WriteNumber(writer, v.maximumSpeedMetersPerSecond); writer.Write(',');
                WriteNumber(writer, v.meanSpeedMetersPerSecond); writer.Write(',');
                WriteNumber(writer, v.maximumAbsoluteLateralOffsetMeters); writer.Write(',');
                WriteNumber(writer, v.minimumRideHeightMeters); writer.Write(',');
                WriteNumber(writer, v.maximumRideHeightMeters); writer.Write(',');
                WriteNumber(writer, v.maximumAbsoluteVerticalAcceleration); writer.Write(',');
                writer.Write(v.unexpectedAirborneSamples); writer.Write(',');
                writer.Write(v.powerLimitedSamples); writer.Write(',');
                writer.WriteLine(v.eventCount);
            }
        }

        private static void WriteAttemptSummaryCsv(
            string path,
            V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("attempt_index,sample_count,discontinuity_samples,start_time_s,end_time_s,duration_s,start_track_distance_m,max_track_distance_m,mean_speed_mps,max_speed_mps,max_abs_lateral_offset_m,min_ride_height_m,event_count");
            for (int i = 0; i < report.attempts.Length; i++)
            {
                V3DiagnosticAttemptSummary v = report.attempts[i];
                writer.Write(v.attemptIndex); writer.Write(',');
                writer.Write(v.sampleCount); writer.Write(',');
                writer.Write(v.discontinuitySamples); writer.Write(',');
                WriteNumber(writer, v.startTimeSeconds); writer.Write(',');
                WriteNumber(writer, v.endTimeSeconds); writer.Write(',');
                WriteNumber(writer, v.durationSeconds); writer.Write(',');
                WriteNumber(writer, v.startTrackDistanceMeters); writer.Write(',');
                WriteNumber(writer, v.maximumTrackDistanceMeters); writer.Write(',');
                WriteNumber(writer, v.meanSpeedMetersPerSecond); writer.Write(',');
                WriteNumber(writer, v.maximumSpeedMetersPerSecond); writer.Write(',');
                WriteNumber(writer, v.maximumAbsoluteLateralOffsetMeters); writer.Write(',');
                WriteNumber(writer, v.minimumRideHeightMeters); writer.Write(',');
                writer.WriteLine(v.eventCount);
            }
        }

        private static void WriteDeviceSummaryCsv(
            string path, V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("device_id,device_type,role,sample_count,mean_requested_output,mean_actual_output,mean_grant_fraction,peak_force_n,peak_temperature_c,derated_samples,fault_samples,physical_limit_samples");
            for (int i = 0; i < report.devices.Length; i++)
            {
                V3DiagnosticDeviceSummary v = report.devices[i];
                WriteCsvText(writer, v.deviceId); writer.Write(',');
                WriteCsvText(writer, v.deviceType); writer.Write(',');
                WriteCsvText(writer, v.role); writer.Write(',');
                writer.Write(v.sampleCount); writer.Write(',');
                WriteNumber(writer, v.meanRequestedOutput); writer.Write(',');
                WriteNumber(writer, v.meanActualOutput); writer.Write(',');
                WriteNumber(writer, v.meanGrantFraction); writer.Write(',');
                WriteNumber(writer, v.peakForceN); writer.Write(',');
                WriteNumber(writer, v.peakTemperatureC); writer.Write(',');
                writer.Write(v.deratedSamples); writer.Write(',');
                writer.Write(v.faultSamples); writer.Write(',');
                writer.WriteLine(v.physicalLimitSamples);
            }
        }

        private static void WriteSystemSummaryCsv(
            string path, V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("task_id,role,sample_count,mean_requested_hz,mean_granted_hz,mean_measured_hz,maximum_skipped_count,below_minimum_samples,fault_samples");
            for (int i = 0; i < report.systems.Length; i++)
            {
                V3DiagnosticSystemSummary v = report.systems[i];
                WriteCsvText(writer, v.taskId); writer.Write(',');
                WriteCsvText(writer, v.role); writer.Write(',');
                writer.Write(v.sampleCount); writer.Write(',');
                WriteNumber(writer, v.meanRequestedRateHz); writer.Write(',');
                WriteNumber(writer, v.meanGrantedRateHz); writer.Write(',');
                WriteNumber(writer, v.meanMeasuredRateHz); writer.Write(',');
                writer.Write(v.maximumSkippedCount); writer.Write(',');
                writer.Write(v.belowMinimumSamples); writer.Write(',');
                writer.WriteLine(v.faultSamples);
            }
        }

        private static void WriteSensorSummaryCsv(
            string path, V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("sensor_id,direction,sample_count,valid_samples,hit_count,comparable_samples,mean_age_s,mean_abs_distance_error_m,max_abs_distance_error_m,mean_normal_error_deg,miss_count");
            for (int i = 0; i < report.sensors.Length; i++)
            {
                V3DiagnosticSensorSummary v = report.sensors[i];
                WriteCsvText(writer, v.sensorId); writer.Write(',');
                WriteCsvText(writer, v.direction); writer.Write(',');
                writer.Write(v.sampleCount); writer.Write(',');
                writer.Write(v.validSampleCount); writer.Write(',');
                writer.Write(v.hitCount); writer.Write(',');
                writer.Write(v.comparableSampleCount); writer.Write(',');
                WriteNumber(writer, v.meanAgeSeconds); writer.Write(',');
                WriteNumber(writer, v.meanAbsoluteDistanceErrorMeters); writer.Write(',');
                WriteNumber(writer, v.maximumAbsoluteDistanceErrorMeters); writer.Write(',');
                WriteNumber(writer, v.meanNormalErrorDegrees); writer.Write(',');
                writer.WriteLine(v.missCount);
            }
        }

        private static void WriteForceSummaryCsv(
            string path, V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("category,sample_count,mean_force_n,peak_force_n,mean_gravity_vertical_n");
            for (int i = 0; i < report.forces.Length; i++)
            {
                V3DiagnosticForceSummary v = report.forces[i];
                WriteCsvText(writer, v.category); writer.Write(',');
                writer.Write(v.sampleCount); writer.Write(',');
                WriteNumber(writer, v.meanMagnitudeN); writer.Write(',');
                WriteNumber(writer, v.peakMagnitudeN); writer.Write(',');
                WriteNumber(writer, v.meanGravityVerticalN); writer.WriteLine();
            }
        }

        private static void WriteLocationClustersCsv(
            string path, V3DiagnosticAnalysisReport report)
        {
            using var writer = new StreamWriter(path, false, Utf8NoBom);
            writer.WriteLine("section_id,start_distance_m,end_distance_m,event_count,maximum_severity,event_types");
            for (int i = 0; i < report.repeatedLocationClusters.Length; i++)
            {
                V3DiagnosticLocationCluster value =
                    report.repeatedLocationClusters[i];
                WriteCsvText(writer, value.sectionId); writer.Write(',');
                WriteNumber(writer, value.startDistanceMeters); writer.Write(',');
                WriteNumber(writer, value.endDistanceMeters); writer.Write(',');
                writer.Write(value.eventCount); writer.Write(',');
                writer.Write(value.maximumSeverity); writer.Write(',');
                WriteCsvText(writer, value.eventTypes); writer.WriteLine();
            }
        }

        private static void WriteMarkdown(
            string path, V3DiagnosticSession session,
            V3DiagnosticAnalysisReport report)
        {
            var text = new StringBuilder(4096);
            text.AppendLine("# Hovercraft V3 Diagnostic Run Report").AppendLine();
            text.AppendLine("## Executive summary").AppendLine();
            text.AppendLine(report.executiveSummary).AppendLine();
            text.AppendLine("No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.").AppendLine();
            AppendSection(text, "Session identity", "Session: `" + session.Manifest.sessionId + "`  \nProfile: `" + session.Manifest.profile + "`");
            AppendSection(text, "Craft configuration", BuildCraftSummary(session));
            AppendSection(text, "World configuration", BuildWorldSummary(session));
            AppendSection(text, "Track overview", "See `track_report.json` and `track_sections.csv`.");
            AppendSection(text, "Data quality", report.dataQuality +
                ". Dropped samples: " + report.droppedSampleCount +
                "; dropped events: " + report.droppedEventCount + ".");
            AppendAttemptTable(text, report);
            AppendLapTable(text, report);
            AppendSectionTable(text, report);
            AppendEventTable(text, report);
            AppendSection(text, "World truth versus craft belief",
                report.worldBeliefDisagreementSamples + " samples disagreed on grounded/contact classification.");
            AppendSensorTable(text, report);
            AppendSystemTable(text, report);
            AppendDeviceTable(text, report);
            AppendForceTable(text, report);
            AppendSection(text, "Power and thermal analysis",
                report.powerLimitedSamples + " samples were power-limited; " +
                report.thermalDeratingSamples + " per-device samples were thermally derated.");
            AppendSection(text, "Atmosphere and aerodynamics",
                "Invalid world samples: " +
                report.invalidWorldEnvironmentSamples +
                "; samples inside zones: " +
                report.samplesInsideEnvironmentZones +
                ". Density range: " +
                report.minimumAirDensityKgPerCubicMeter.ToString("0.000", Invariant) +
                " to " +
                report.maximumAirDensityKgPerCubicMeter.ToString("0.000", Invariant) +
                " kg/m3. Temperature range: " +
                report.minimumAmbientTemperatureC.ToString("0.0", Invariant) +
                " to " +
                report.maximumAmbientTemperatureC.ToString("0.0", Invariant) +
                " C. Maximum Mach: " +
                report.maximumMachNumber.ToString("0.000", Invariant) +
                "; maximum dynamic pressure: " +
                report.maximumDynamicPressurePa.ToString("0", Invariant) +
                " Pa.");
            AppendSection(text, "Airborne/contact analysis",
                report.unexpectedAirborneSamples + " samples were classified unexpected-airborne.");
            AppendLocationClusterTable(text, report);
            AppendSection(text, "Evidence tables",
                "The tables below are derived from synchronized samples; raw records remain authoritative.");
            AppendStringList(text, "Potential investigation areas",
                report.potentialInvestigationAreas);
            AppendSection(text, "Raw-data files", report.rawDataFiles.Length > 0
                ? "`" + string.Join("`, `", report.rawDataFiles) + "`."
                : "No optional raw-data payloads were requested.");
            File.WriteAllText(path, text.ToString(), Utf8NoBom);
        }

        private static string BuildWorldSummary(V3DiagnosticSession session)
        {
            V3DiagnosticWorldSnapshot world = session.WorldSnapshot;
            if (world == null)
            {
                return "No world snapshot was captured.";
            }
            return "Scene: `" + session.Manifest.sceneName + "`  \n" +
                "World root: `" + world.worldRootId + "`  \n" +
                "Profile: `" + world.worldProfileName + "` (`" +
                world.worldProfileId + "`)  \n" +
                "Gravity: " + world.profileGravity + " m/s2  \n" +
                "Sea-level atmosphere: " +
                world.seaLevelTemperatureC.ToString("0.0", Invariant) +
                " C, " + world.seaLevelPressurePa.ToString("0", Invariant) +
                " Pa, " + world.seaLevelDensity.ToString("0.000", Invariant) +
                " kg/m3  \n" +
                "Fixed timestep: " +
                world.fixedDeltaTime.ToString("0.#####", Invariant) +
                " s; environment zones: " + world.environmentZoneCount + ".";
        }

        private static string BuildCraftSummary(V3DiagnosticSession session)
        {
            V3DiagnosticCraftSnapshot craft = session.CraftSnapshot;
            if (craft == null)
            {
                return "No craft snapshot was captured.";
            }
            return "Build: `" + session.Manifest.craftBuildStableId + "`  \n" +
                "Mass: " + craft.calculatedMassKg.ToString("0.###", Invariant) +
                " kg  \nHover configuration: `" + craft.hoverConfigurationId +
                "` v" + craft.hoverConfigurationVersion + "  \n" +
                "Target / minimum / maximum range: " +
                craft.targetHoverHeight.ToString("0.###", Invariant) + " / " +
                craft.minimumHoverClearance.ToString("0.###", Invariant) + " / " +
                craft.maximumHoverRange.ToString("0.###", Invariant) + " m  \n" +
                "Hover strength / damping: " +
                craft.hoverStrength.ToString("0.###", Invariant) + " / " +
                craft.hoverDamping.ToString("0.###", Invariant) +
                "; gravity compensation: " +
                (craft.gravityCompensationEnabled ? "enabled x" : "disabled x") +
                craft.gravityCompensationMultiplier.ToString("0.###", Invariant) +
                "  \nCompression / extension limits: " +
                craft.maximumCompressionMeters.ToString("0.###", Invariant) +
                " / " + craft.maximumExtensionMeters.ToString("0.###", Invariant) +
                " m.  \nSurface curvature feed-forward / response / cap: " +
                craft.surfaceCurvatureFeedForward.ToString("0.###", Invariant) +
                " / " + craft.surfaceTrackingResponse.ToString("0.###", Invariant) +
                " / " + craft.maximumSurfaceTrackingAcceleration.ToString("0.###", Invariant) +
                " m/s2.  \nRoof capture strength / damping / cap / deadband: " +
                craft.roofCaptureStrength.ToString("0.###", Invariant) +
                " / " + craft.roofCaptureDamping.ToString("0.###", Invariant) +
                " / " + craft.maximumRoofCaptureAcceleration.ToString("0.###", Invariant) +
                " m/s2 / " + craft.roofCaptureDeadband.ToString("0.###", Invariant) +
                " m.";
        }

        private static void AppendLapTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Lap summary").AppendLine();
            text.AppendLine("| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |");
            text.AppendLine("|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.laps.Length; i++)
            {
                V3DiagnosticLapSummary v = report.laps[i];
                text.Append('|').Append(v.lap).Append('|').Append(v.sampleCount).Append('|')
                    .Append(v.durationSeconds.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanSpeedMetersPerSecond.ToString("0.###", Invariant)).Append('|')
                    .Append(v.maximumSpeedMetersPerSecond.ToString("0.###", Invariant)).Append('|')
                    .Append(v.eventCount).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendAttemptTable(
            StringBuilder text,
            V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Driving attempt summary").AppendLine();
            text.AppendLine("Resets create a new attempt. Teleport discontinuity samples are retained in raw data but excluded from dynamics aggregates.").AppendLine();
            text.AppendLine("| Attempt | Samples | Duration (s) | Max distance (m) | Mean speed (m/s) | Max speed (m/s) | Min ride height (m) | Events |");
            text.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.attempts.Length; i++)
            {
                V3DiagnosticAttemptSummary v = report.attempts[i];
                text.Append('|').Append(v.attemptIndex).Append('|')
                    .Append(v.sampleCount).Append('|')
                    .Append(v.durationSeconds.ToString("0.###", Invariant)).Append('|')
                    .Append(v.maximumTrackDistanceMeters.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanSpeedMetersPerSecond.ToString("0.###", Invariant)).Append('|')
                    .Append(v.maximumSpeedMetersPerSecond.ToString("0.###", Invariant)).Append('|')
                    .Append(v.minimumRideHeightMeters.ToString("0.###", Invariant)).Append('|')
                    .Append(v.eventCount).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendSectionTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Section summary").AppendLine();
            text.AppendLine("| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.sections.Length; i++)
            {
                V3DiagnosticSectionSummary v = report.sections[i];
                text.Append('|').Append(v.sectionId).Append('|').Append(v.sampleCount).Append('|')
                    .Append(v.meanSpeedMetersPerSecond.ToString("0.###", Invariant)).Append('|')
                    .Append(v.maximumAbsoluteLateralOffsetMeters.ToString("0.###", Invariant)).Append('|')
                    .Append(v.minimumRideHeightMeters.ToString("0.###", Invariant)).Append('|')
                    .Append(v.eventCount).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendEventTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Event timeline").AppendLine();
            text.AppendLine("| Event type | Count | Max severity |");
            text.AppendLine("|---|---:|---:|");
            for (int i = 0; i < report.eventTypes.Length; i++)
            {
                V3DiagnosticEventTypeSummary v = report.eventTypes[i];
                text.Append('|').Append(v.eventType).Append('|').Append(v.count)
                    .Append('|').Append(v.maximumSeverity).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendSensorTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Sensor accuracy").AppendLine();
            text.AppendLine("| Sensor | Samples | Hits | Comparable | Mean abs distance error (m) | Mean normal error (deg) | Misses |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.sensors.Length; i++)
            {
                V3DiagnosticSensorSummary v = report.sensors[i];
                text.Append('|').Append(v.sensorId).Append('|').Append(v.sampleCount)
                    .Append('|').Append(v.hitCount).Append('|').Append(v.comparableSampleCount).Append('|')
                    .Append(v.meanAbsoluteDistanceErrorMeters.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanNormalErrorDegrees.ToString("0.###", Invariant)).Append('|')
                    .Append(v.missCount).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendLocationClusterTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Repeated issue locations").AppendLine();
            if (report.repeatedLocationClusters.Length == 0)
            {
                text.AppendLine(
                    "No 10 m track-distance bin contained more than one event.")
                    .AppendLine();
                return;
            }
            text.AppendLine("| Section | Distance window (m) | Events | Max severity | Types |");
            text.AppendLine("|---|---:|---:|---:|---|");
            for (int i = 0; i < report.repeatedLocationClusters.Length; i++)
            {
                V3DiagnosticLocationCluster value =
                    report.repeatedLocationClusters[i];
                text.Append('|').Append(value.sectionId).Append('|')
                    .Append(value.startDistanceMeters.ToString("0.###", Invariant))
                    .Append("–")
                    .Append(value.endDistanceMeters.ToString("0.###", Invariant))
                    .Append('|').Append(value.eventCount).Append('|')
                    .Append(value.maximumSeverity).Append('|')
                    .Append(value.eventTypes).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendSystemTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## System execution").AppendLine();
            text.AppendLine("| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |");
            text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.systems.Length; i++)
            {
                V3DiagnosticSystemSummary v = report.systems[i];
                text.Append('|').Append(v.taskId).Append('|').Append(v.role).Append('|')
                    .Append(v.meanRequestedRateHz.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanGrantedRateHz.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanMeasuredRateHz.ToString("0.###", Invariant)).Append('|')
                    .Append(v.maximumSkippedCount).Append('|').Append(v.belowMinimumSamples)
                    .Append('|').Append(v.faultSamples).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendDeviceTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Router and actuator behavior").AppendLine();
            text.AppendLine("| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |");
            text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.devices.Length; i++)
            {
                V3DiagnosticDeviceSummary v = report.devices[i];
                text.Append('|').Append(v.deviceId).Append('|').Append(v.deviceType).Append('|')
                    .Append(v.meanRequestedOutput.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanActualOutput.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanGrantFraction.ToString("0.###", Invariant)).Append('|')
                    .Append(v.peakForceN.ToString("0.###", Invariant)).Append('|')
                    .Append(v.peakTemperatureC.ToString("0.###", Invariant)).Append('|')
                    .Append(v.physicalLimitSamples).Append('|').Append(v.faultSamples).AppendLine("|");
            }
            text.AppendLine();
        }

        private static void AppendForceTable(
            StringBuilder text, V3DiagnosticAnalysisReport report)
        {
            text.AppendLine("## Force and torque analysis").AppendLine();
            text.AppendLine("| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |");
            text.AppendLine("|---|---:|---:|---:|");
            for (int i = 0; i < report.forces.Length; i++)
            {
                V3DiagnosticForceSummary v = report.forces[i];
                text.Append('|').Append(v.category).Append('|')
                    .Append(v.meanMagnitudeN.ToString("0.###", Invariant)).Append('|')
                    .Append(v.peakMagnitudeN.ToString("0.###", Invariant)).Append('|')
                    .Append(v.meanGravityVerticalN.ToString("0.###", Invariant)).AppendLine("|");
            }
            text.AppendLine().Append("Maximum residual force: ")
                .Append(report.maximumResidualForceN.ToString("0.###", Invariant))
                .Append(" N; maximum residual torque: ")
                .Append(report.maximumResidualTorqueNm.ToString("0.###", Invariant))
                .AppendLine(" Nm.");
            text.Append("Contact-free residual (valid dynamics only): mean ")
                .Append(report.contactFreeResidualMeanN.ToString("0.###", Invariant))
                .Append(" N; p95 ")
                .Append(report.contactFreeResidualP95N.ToString("0.###", Invariant))
                .Append(" N; max ")
                .Append(report.contactFreeResidualMaxN.ToString("0.###", Invariant))
                .Append(" N across ")
                .Append(report.contactFreeResidualSampleCount)
                .AppendLine(" samples.").AppendLine();
        }

        private static void AppendStringList(
            StringBuilder text, string heading, string[] values)
        {
            text.Append("## ").AppendLine(heading).AppendLine();
            for (int i = 0; i < values.Length; i++)
                text.Append("- ").AppendLine(values[i]);
            text.AppendLine();
        }

        private static void AppendSection(StringBuilder builder, string heading, string body)
        {
            builder.Append("## ").AppendLine(heading).AppendLine().AppendLine(body).AppendLine();
        }

        private static void WriteNumber(TextWriter writer, double value)
        {
            writer.Write(value.ToString("R", Invariant));
        }

        private static void WriteVectorCsv(TextWriter writer, Vector3 value)
        {
            WriteNumber(writer, value.x); writer.Write(',');
            WriteNumber(writer, value.y); writer.Write(',');
            WriteNumber(writer, value.z);
        }

        private static void WriteCsvText(TextWriter writer, string value)
        {
            value ??= string.Empty;
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\""));
            writer.Write('"');
        }
    }

    [Serializable]
    internal sealed class V3DiagnosticTrackExportHeader
    {
        public int schemaVersion;
        public int trackInstanceId;
        public int generationRevision;
        public int sectionCount;
        public string analyzerStatus;
    }

    internal sealed class V3DiagnosticChecksumStream : Stream
    {
        private readonly Stream inner;
        private readonly V3DiagnosticCrc32 checksum;
        public V3DiagnosticChecksumStream(Stream inner, V3DiagnosticCrc32 checksum)
        {
            this.inner = inner;
            this.checksum = checksum;
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)
        {
            checksum.Update(buffer, offset, count);
            inner.Write(buffer, offset, count);
        }
        public override void WriteByte(byte value)
        {
            checksum.Update(value);
            inner.WriteByte(value);
        }
    }

    internal sealed class V3DiagnosticCrc32
    {
        private uint value = 0xFFFFFFFFu;
        public uint Value => value ^ 0xFFFFFFFFu;
        public void Update(byte item)
        {
            value ^= item;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1u) != 0u
                    ? (value >> 1) ^ 0xEDB88320u
                    : value >> 1;
            }
        }
        public void Update(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Update(buffer[offset + i]);
            }
        }
    }
}
