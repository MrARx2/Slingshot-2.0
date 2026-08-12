using System;
using System.IO;
using Lunarlight.Hovercraft.V3.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3DiagnosticFoundationTests
    {
        [Test]
        public void ChannelRegistry_CanonicalIdsAreUniqueAndVersioned()
        {
            var registry = new V3DiagnosticChannelRegistry();
            registry.RegisterCanonicalChannels();

            Assert.That(registry.Count, Is.GreaterThan(20));
            for (int i = 0; i < registry.Channels.Count; i++)
            {
                V3DiagnosticChannelDefinition channel = registry.Channels[i];
                Assert.That(channel.id, Is.Not.Empty);
                Assert.That(channel.schemaVersion, Is.EqualTo(V3DiagnosticSchema.Version));
                Assert.That(registry.TryGet(channel.id, out var found), Is.True);
                Assert.That(found, Is.SameAs(channel));
            }
            Assert.That(registry.Register(registry.Channels[0]), Is.False);
            Assert.That(V3DiagnosticSchema.Version, Is.EqualTo(6));
            Assert.That(V3DiagnosticSchema.VersionText, Is.EqualTo("6.0"));
            Assert.That(V3DiagnosticSchema.Version,
                Is.EqualTo(V3ForensicSchemaIdentity.Version));
        }

        [Test]
        public void Clock_ProducesMonotonicCanonicalIdentityAndPhaseStamp()
        {
            var clock = new V3DiagnosticClock();
            clock.Reset(10d, 20d);
            V3DiagnosticClockSnapshot first = clock.Advance(10.02d, 20.03d, 0.02f);
            V3DiagnosticClockSnapshot second = clock.Advance(10.04d, 20.05d, 0.02f);

            Assert.That(first.physicsTick, Is.EqualTo(0));
            Assert.That(first.sampleIndex, Is.EqualTo(0));
            Assert.That(second.physicsTick, Is.EqualTo(1));
            Assert.That(second.sampleIndex, Is.EqualTo(1));
            Assert.That(second.sessionElapsedSeconds, Is.EqualTo(0.04d).Within(0.000001d));
            Assert.That(clock.StampPhase(V3DiagnosticPhase.PostAllocation).phase,
                Is.EqualTo(V3DiagnosticPhase.PostAllocation));
        }

        [Test]
        public void Clock_ReducedRateSamplesRetainPhysicalTickIdentity()
        {
            var clock = new V3DiagnosticClock();
            clock.Reset(0d, 0d);
            V3DiagnosticClockSnapshot first = clock.Advance(
                0.02d, 0.02d, 0.02f, V3DiagnosticPhase.PostPhysics, true);
            V3DiagnosticClockSnapshot skipped = clock.Advance(
                0.04d, 0.04d, 0.02f, V3DiagnosticPhase.PostPhysics, false);
            V3DiagnosticClockSnapshot second = clock.Advance(
                0.06d, 0.06d, 0.02f, V3DiagnosticPhase.PostPhysics, true);

            Assert.That(first.sampleIndex, Is.EqualTo(0));
            Assert.That(skipped.physicsTick, Is.EqualTo(1));
            Assert.That(skipped.sampleIndex, Is.EqualTo(0));
            Assert.That(second.physicsTick, Is.EqualTo(2));
            Assert.That(second.sampleIndex, Is.EqualTo(1));
        }

        [Test]
        public void Session_ReducedRateProfilePublishesSamplingMetadata()
        {
            var session = new V3DiagnosticSession();
            session.Initialize(
                "reduced", "Endurance", V3DiagnosticRecordingProfile.Endurance,
                10, 5f, new V3DiagnosticCaptureCapacities(),
                new V3DiagnosticCraftSnapshot(),
                new V3DiagnosticWorldSnapshot { fixedDeltaTime = 0.02f }, 5);

            Assert.That(session.Manifest.physicsTickStride, Is.EqualTo(5));
            Assert.That(session.Manifest.nominalSampleRateHz,
                Is.EqualTo(10f).Within(0.0001f));
            Assert.That(session.Channels.Channels[0].samplingMode,
                Is.EqualTo(V3DiagnosticSamplingMode.ReducedRate));
        }

        [Test]
        public void Session_UsesBoundedPreallocatedSlotsAndReportsOverflow()
        {
            V3DiagnosticSession session = CreateSession(2);

            Assert.That(session.TryBeginSample(out int first), Is.True);
            Assert.That(session.TryBeginSample(out int second), Is.True);
            Assert.That(first, Is.EqualTo(0));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(session.TryBeginSample(out _), Is.False);
            Assert.That(session.Manifest.droppedSampleCount, Is.EqualTo(1));
            Assert.That(session.Samples[0].sensors.Length, Is.EqualTo(2));
            for (int i = 0; i < 4; i++)
                Assert.That(session.TryAddEvent(new V3DiagnosticEvent
                    { eventId = "bounded_" + i }), Is.True);
            Assert.That(session.TryAddEvent(new V3DiagnosticEvent
                { eventId = "overflow" }), Is.False);
            Assert.That(session.Manifest.droppedEventCount, Is.EqualTo(1));
            Assert.That(session.DataWarnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Export_WritesRequiredFoundationPackageAndChecksum()
        {
            V3DiagnosticSession session = CreateSession(2);
            Assert.That(session.TryBeginSample(out int index), Is.True);
            ref V3DiagnosticSample sample = ref session.GetSample(index);
            sample.identity.sampleIndex = 0;
            sample.identity.physicsTick = 0;
            sample.identity.sessionElapsed = 0.02d;
            sample.identity.fixedDelta = 0.02f;
            sample.world.position = new Vector3(1f, 2f, 3f);
            sample.world.linearVelocity = Vector3.forward * 10f;
            Assert.That(session.TryAddEvent(new V3DiagnosticEvent
            {
                eventId = "export-context",
                type = V3DiagnosticEventType.ManualMarker,
                sampleIndex = 0,
                preEventStartSample = 0,
                postEventEndSample = 0,
                manual = true
            }), Is.True);
            session.Complete(0.02d, 0.1d);

            string root = Path.Combine(Application.temporaryCachePath,
                "V3DiagnosticFoundationTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                V3DiagnosticExportResult result = V3DiagnosticExportService.Export(
                    session, root, new V3DiagnosticExportOptions());
                Assert.That(result.success, Is.True, result.error);
                string output = result.outputDirectory;
                string[] required =
                {
                    "session_manifest.json", "channel_schema.json",
                    "craft_snapshot.json",
                    "world_snapshot.json", "track_report.json",
                    "samples.ndjson", "samples_compact.bin",
                    "samples_compact.bin.crc32", "events.json",
                    "event_context.ndjson", "events.csv",
                    "event_location_clusters.csv", "report.md", "report.json"
                };
                for (int i = 0; i < required.Length; i++)
                {
                    Assert.That(File.Exists(Path.Combine(output, required[i])),
                        Is.True, required[i]);
                }
                Assert.That(File.ReadAllLines(Path.Combine(output, "samples.ndjson")).Length,
                    Is.EqualTo(1));
                string ndjson = File.ReadAllText(
                    Path.Combine(output, "samples.ndjson"));
                Assert.That(ndjson, Does.Contain("\"sensors\":[]"));
                Assert.That(ndjson, Does.Contain("\"devices\":[]"));
                string[] eventContext = File.ReadAllLines(
                    Path.Combine(output, "event_context.ndjson"));
                Assert.That(eventContext, Has.Length.EqualTo(1));
                Assert.That(eventContext[0], Does.Contain("\"contextSampleIndex\""));
                Assert.That(eventContext[0], Does.Not.Contain("\"sample\":"),
                    "Event context must reference the canonical sample instead of duplicating its full payload.");
                Assert.That(result.binaryChecksum, Has.Length.EqualTo(8));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static V3DiagnosticSession CreateSession(int capacity)
        {
            var session = new V3DiagnosticSession();
            session.Initialize(
                "test-session",
                "Foundation",
                V3DiagnosticRecordingProfile.FullForensic,
                capacity,
                1f,
                new V3DiagnosticCaptureCapacities
                {
                    sensors = 2,
                    tasks = 2,
                    observations = 2,
                    requests = 2,
                    devices = 2,
                    events = 4
                },
                new V3DiagnosticCraftSnapshot { buildStableId = "test-craft" },
                new V3DiagnosticWorldSnapshot { sceneName = "test-scene" });
            return session;
        }
    }
}
