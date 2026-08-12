using System.Collections;
using Lunarlight.Hovercraft.V3.TrackIntegration;
using NUnit.Framework;
using TrackGeneration;
using TrackGeneration.Macro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3TrackTestSceneIntegrationTests
    {
        private const string ScenePath =
            "Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity";

        [Test]
        public void TrackTestScene_UsesGeneratorOwnedV3StartPose()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            TrackGenerator generator =
                Object.FindAnyObjectByType<TrackGenerator>(
                    FindObjectsInactive.Include);
            V3CraftTestSpawner spawner =
                Object.FindAnyObjectByType<V3CraftTestSpawner>(
                    FindObjectsInactive.Include);
            V3FreeDriveSession session =
                Object.FindAnyObjectByType<V3FreeDriveSession>(
                    FindObjectsInactive.Include);
            V3TrackRaceCraftBridge bridge =
                Object.FindAnyObjectByType<V3TrackRaceCraftBridge>(
                    FindObjectsInactive.Include);

            Assert.That(generator, Is.Not.Null);
            Assert.That(spawner, Is.Not.Null);
            Assert.That(session, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);
            Assert.That(generator.TrackRoot, Is.Not.Null,
                "The Track Test scene must reference the reusable cached " +
                "track prefab instead of rebuilding it during Play Mode.");
            Assert.That(
                generator.TrackRoot.GetComponent<TrackRuntimeCacheIdentity>(),
                Is.Not.Null);
            MeshFilter[] cachedFilters =
                generator.TrackRoot.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(cachedFilters, Is.Not.Empty);
            Assert.That(
                cachedFilters[0].sharedMesh,
                Is.Not.Null);
            Assert.That(
                EditorUtility.IsPersistent(cachedFilters[0].sharedMesh),
                Is.True,
                "Cached track meshes must be project assets, not transient " +
                "scene objects.");
            Assert.That(generator.TrackStartSpawnPoint, Is.Not.Null);
            Assert.That(
                generator.TrackStartSpawnPoint.parent,
                Is.SameAs(generator.transform));

            Assert.That(spawner.SelectedBuild, Is.Not.Null);
            Assert.That(
                generator.TrackStartSpawnRideHeight,
                Is.EqualTo(
                    spawner.SelectedBuild.HoverConfiguration
                        .TargetHoverHeight).Within(0.001f));

            Assert.That(
                ObjectReference(session, "spawnPoint"),
                Is.SameAs(generator.TrackStartSpawnPoint));
            Assert.That(
                ObjectReference(spawner, "spawnPoint"),
                Is.SameAs(generator.TrackStartSpawnPoint));

            Transform spawn = generator.TrackStartSpawnPoint;
            MacroTrackDebugVisualizer visualizer =
                generator.TrackRoot.GetComponent<
                    MacroTrackDebugVisualizer>();
            Assert.That(visualizer, Is.Not.Null);
            Assert.That(visualizer.Sections, Is.Not.Null.And.Not.Empty);
            var startFrame = visualizer.Sections[0].StartFrame;
            Vector3 expectedUp = generator.transform.TransformDirection(
                startFrame.Up).normalized;
            Vector3 expectedForward = Vector3.ProjectOnPlane(
                generator.transform.TransformDirection(startFrame.Forward),
                expectedUp).normalized;
            Vector3 expectedPosition = generator.transform.TransformPoint(
                startFrame.Position) + expectedUp *
                generator.TrackStartSpawnRideHeight;
            Assert.That(
                Vector3.Distance(spawn.position, expectedPosition),
                Is.LessThan(0.01f));
            Assert.That(
                Vector3.Angle(spawn.forward, expectedForward),
                Is.LessThan(0.01f));
            Assert.That(
                Vector3.Angle(spawn.up, expectedUp),
                Is.LessThan(0.01f));

            Assert.That(Bool(generator, "generateOnStart"), Is.False);
            Assert.That(Bool(generator, "keepEditorTrackOnPlay"), Is.True);
            Assert.That(Bool(generator, "placeHovercraftOnStart"), Is.True);
        }

        [UnityTest]
        [Explicit("Generates the full procedural track and is intentionally " +
                  "reserved for focused Track Test scene validation.")]
        public IEnumerator TrackTestScene_PlayModeAdoptsCachedTrackAndPlacesCraft()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return null;

            TrackGenerator generator =
                Object.FindAnyObjectByType<TrackGenerator>(
                    FindObjectsInactive.Include);
            V3CraftTestSpawner spawner =
                Object.FindAnyObjectByType<V3CraftTestSpawner>(
                    FindObjectsInactive.Include);
            Assert.That(generator, Is.Not.Null);
            Assert.That(spawner, Is.Not.Null);
            Assert.That(generator.TrackRoot, Is.Not.Null);
            Assert.That(
                generator.CurrentMacroSections,
                Is.Not.Null.And.Not.Empty);
            Assert.That(spawner.CurrentCraft, Is.Not.Null);

            Transform spawn = generator.TrackStartSpawnPoint;
            Rigidbody body = spawner.CurrentCraft.GetComponent<
                V3CraftRuntime>().RootRigidbody;
            Assert.That(
                Vector3.Distance(body.position, spawn.position),
                Is.LessThan(0.1f));
            Assert.That(
                Quaternion.Angle(body.rotation, spawn.rotation),
                Is.LessThan(0.1f));

            yield return new ExitPlayMode();
        }

        private static Object ObjectReference(
            Object target,
            string propertyName)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            return property.objectReferenceValue;
        }

        private static bool Bool(Object target, string propertyName)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            return property.boolValue;
        }
    }
}
