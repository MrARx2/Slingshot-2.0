using UnityEngine;

namespace TrackGeneration
{
    /// <summary>
    /// Provenance attached to an editor-baked procedural track prefab.
    /// Runtime uses the normal MacroTrackDebugVisualizer data for adoption;
    /// this component makes cache age and geometry cost inspectable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackRuntimeCacheIdentity : MonoBehaviour
    {
        [SerializeField] private int seed;
        [SerializeField] private string generatedUtc;
        [SerializeField] private int profileResolution;
        [SerializeField] private int meshCount;
        [SerializeField] private int vertexCount;
        [SerializeField] private int triangleCount;

        public int Seed => seed;
        public string GeneratedUtc => generatedUtc;
        public int ProfileResolution => profileResolution;
        public int MeshCount => meshCount;
        public int VertexCount => vertexCount;
        public int TriangleCount => triangleCount;

        public void Configure(
            int sourceSeed,
            string utc,
            int resolution,
            int meshes,
            int vertices,
            int triangles)
        {
            seed = sourceSeed;
            generatedUtc = utc ?? string.Empty;
            profileResolution = Mathf.Max(1, resolution);
            meshCount = Mathf.Max(0, meshes);
            vertexCount = Mathf.Max(0, vertices);
            triangleCount = Mathf.Max(0, triangles);
        }
    }
}
