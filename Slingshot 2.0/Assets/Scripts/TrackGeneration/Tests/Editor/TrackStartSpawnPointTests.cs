using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TrackGeneration.Macro;
using TrackGeneration.Race;
using UnityEngine;

namespace TrackGeneration.Tests
{
    public sealed class TrackStartSpawnPointTests
    {
        [Test]
        public void RefreshStartSpawnPoint_UsesAuthoredForwardUpAndRideHeight()
        {
            var host = new GameObject("TrackStartSpawnPointTest");
            try
            {
                TrackGenerator generator = host.AddComponent<TrackGenerator>();
                host.transform.SetPositionAndRotation(
                    new Vector3(10f, 20f, 30f),
                    Quaternion.Euler(7f, 31f, -4f));

                var frame = TrackConnectionFrame.Origin(32f);
                frame.Position = new Vector3(2f, 3f, 4f);
                frame.Up = new Vector3(0.2f, 1f, 0.1f).normalized;
                frame.Forward =
                    Vector3.ProjectOnPlane(Vector3.forward, frame.Up).normalized;
                SetSections(generator, frame);

                Assert.That(
                    generator.SetTrackStartSpawnRideHeight(8f),
                    Is.True);
                Transform spawn = generator.TrackStartSpawnPoint;
                Assert.That(spawn, Is.Not.Null);
                Assert.That(spawn.parent, Is.SameAs(host.transform));

                Vector3 expectedUp =
                    host.transform.TransformDirection(frame.Up).normalized;
                Vector3 expectedForward = Vector3.ProjectOnPlane(
                    host.transform.TransformDirection(frame.Forward),
                    expectedUp).normalized;
                Vector3 expectedPosition =
                    host.transform.TransformPoint(frame.Position) +
                    expectedUp * 8f;

                Assert.That(
                    Vector3.Distance(spawn.position, expectedPosition),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Angle(spawn.forward, expectedForward),
                    Is.LessThan(0.01f));
                Assert.That(
                    Vector3.Angle(spawn.up, expectedUp),
                    Is.LessThan(0.01f));

                Transform persistentAnchor = spawn;
                frame.Position += new Vector3(50f, 6f, -20f);
                frame.Forward = Vector3.right;
                frame.Up = Vector3.up;
                SetSections(generator, frame);

                Assert.That(generator.RefreshTrackStartSpawnPoint(), Is.True);
                Assert.That(
                    generator.TrackStartSpawnPoint,
                    Is.SameAs(persistentAnchor),
                    "Regeneration must update the existing referenced anchor.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TrackWorldBounds_IncludeDeepGeneratedGeometry()
        {
            var host = new GameObject("TrackBoundsTest");
            var root = new GameObject("Generated Track Root");
            var geometry = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                TrackGenerator generator = host.AddComponent<TrackGenerator>();
                root.transform.position = new Vector3(0f, -200f, 0f);
                geometry.transform.SetParent(root.transform, false);
                geometry.transform.localScale = new Vector3(20f, 100f, 20f);
                typeof(TrackGenerator).GetField(
                    "trackRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(generator, root.transform);

                Assert.That(
                    generator.TryGetTrackWorldBounds(out Bounds bounds),
                    Is.True);
                Assert.That(bounds.min.y, Is.LessThan(-249f));
                Assert.That(bounds.max.y, Is.GreaterThan(-151f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MacroSampler_PreservesInterpolatedCurvatureAndRoadRoll()
        {
            TrackConnectionFrame a = TrackConnectionFrame.Origin(32f);
            a.ArcLength = 0f;
            a.HorizontalCurvature = 0.001f;
            a.VerticalCurvature = 0.002f;
            a.RoadRollRate = 0.1f;
            TrackConnectionFrame b = TrackConnectionFrame.Origin(32f);
            b.Position = Vector3.forward * 100f;
            b.ArcLength = 100f;
            b.HorizontalCurvature = 0.003f;
            b.VerticalCurvature = 0.006f;
            b.RoadRollRate = 0.3f;
            var sections = new List<GeneratedTrackSection>
            {
                new GeneratedTrackSection
                {
                    StartFrame = a,
                    EndFrame = b,
                    SubdivisionFrames = new[] { a, b }
                }
            };

            Assert.That(MacroTrackSampler.TrySampleFrame(
                sections, 50f, out TrackConnectionFrame sampled), Is.True);
            Assert.That(sampled.HorizontalCurvature,
                Is.EqualTo(0.002f).Within(0.000001f));
            Assert.That(sampled.VerticalCurvature,
                Is.EqualTo(0.004f).Within(0.000001f));
            Assert.That(sampled.RoadRollRate,
                Is.EqualTo(0.2f).Within(0.000001f));
        }

        private static void SetSections(
            TrackGenerator generator,
            TrackConnectionFrame start)
        {
            PropertyInfo property = typeof(TrackGenerator).GetProperty(
                nameof(TrackGenerator.CurrentMacroSections),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null);
            property.SetValue(
                generator,
                new List<GeneratedTrackSection>
                {
                    new GeneratedTrackSection { StartFrame = start }
                });
        }
    }
}
