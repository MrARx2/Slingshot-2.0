using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Tests
{
    /// <summary>
    /// FULL-BUILD physics integrity: generates a big Rollercoaster track with meshes and
    /// chain colliders in the edit-mode physics scene, then raycasts down onto the road
    /// at many points along every meshed section. A miss = the craft would fall through.
    /// Uses the PROJECT TrackConfig asset so the test sees exactly what the game sees.
    /// </summary>
    public class ColliderIntegrityTests
    {
        private const string ConfigPath = "Assets/Scripts/TrackGeneration/TrackConfig.asset";

        [Test]
        public void RollercoasterTrackHasNoCollisionHoles()
        {
            var config = AssetDatabase.LoadAssetAtPath<TrackConfig>(ConfigPath);
            Assert.IsNotNull(config, $"TrackConfig asset not found at {ConfigPath}");

            var go = new GameObject("ColliderIntegrityTest_Generator");
            try
            {
                var seedManager = go.AddComponent<TrackGeneration.Core.TrackSeedManager>();
                var generator = go.AddComponent<TrackGenerator>();
                generator.Config = config;
                generator.Designer = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Rollercoaster);
                generator.Designer.Generation.SelectionMode = CandidateSelectionMode.FirstValid;

                generator.GenerateTrack();

                string firstFailure = generator.LastReport != null && generator.LastReport.Failures.Count > 0
                    ? generator.LastReport.Failures[generator.LastReport.Failures.Count - 1].Message
                    : "(no failure recorded)";
                Assert.IsTrue(generator.LastReport != null && generator.LastReport.Success,
                    $"Generation failed: {firstFailure}");
                Assert.IsNotNull(generator.TrackRoot, "No track root after generation.");

                // Collider inventory. PhysX's BVH34 midphase is BROKEN above 2^21
                // triangles per mesh (missed collisions + degenerate query cost) —
                // every collider chunk must stay far below it.
                const int physxTriangleLimit = 2097152;
                var colliders = generator.TrackRoot.GetComponentsInChildren<MeshCollider>();
                long colliderTris = 0;
                foreach (var mc in colliders)
                {
                    Assert.IsNotNull(mc.sharedMesh, $"{mc.name} has a null collider mesh.");
                    long tris = mc.sharedMesh.triangles.LongLength / 3;
                    colliderTris += tris;
                    Assert.Less(tris, physxTriangleLimit / 2,
                        $"{mc.name} has {tris} triangles — too close to the PhysX 2^21 midphase limit.");
                }
                Debug.Log($"[IntegrityTest] {colliders.Length} collider chunks, {colliderTris} collision triangles total.");
                Assert.Greater(colliders.Length, 0, "No chain colliders were built at all.");

                // Raycast the road along every meshed section.
                Physics.SyncTransforms();
                int misses = 0;
                int casts = 0;
                string firstMiss = null;

                foreach (var sec in generator.CurrentMacroSections)
                {
                    var frames = sec.SubdivisionFrames;
                    if (sec.IsEmptySpace || frames == null || frames.Length < 2) continue;

                    int step = Mathf.Max(1, frames.Length / 25);
                    for (int i = 0; i < frames.Length; i += step)
                    {
                        var f = frames[i];
                        Vector3 world = generator.TrackRoot.TransformPoint(f.Position);
                        Vector3 up = generator.TrackRoot.TransformDirection(f.Up);
                        casts++;
                        if (!Physics.Raycast(world + up * 8f, -up, out RaycastHit hit, 30f) ||
                            hit.distance > 12f)
                        {
                            misses++;
                            firstMiss ??= $"{sec.Definition.DebugName} ring {i} at {world}";
                        }
                    }
                }

                Debug.Log($"[IntegrityTest] {casts} probe casts, {misses} misses.");
                Assert.AreEqual(0, misses,
                    $"{misses}/{casts} hover probes found NO road collision — first miss: {firstMiss}");
            }
            finally
            {
                Object.DestroyImmediate(go); // the generated track root is parented under it
            }
        }
    }
}
