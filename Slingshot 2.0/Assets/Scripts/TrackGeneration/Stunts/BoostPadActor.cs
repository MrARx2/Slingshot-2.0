using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Stunts
{
    /// <summary>
    /// A boost pad lying flush on the road surface. Driving over it applies a forward
    /// impulse along the pad's facing direction (the road tangent at placement).
    /// Reusable gameplay actor: place it on any generated section's connection frame
    /// (boost straights advertise themselves via AllowsBoost on their definitions).
    /// </summary>
    [AddComponentMenu("Track Generation/Stunts/Boost Pad Actor")]
    public class BoostPadActor : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Geometry
        // ──────────────────────────────────────────────

        [Header("Geometry")]
        [Tooltip("Length of the pad along the track (meters).")]
        [Range(3f, 15f)]
        public float PadLength = 6f;

        [Tooltip("Width of the pad (meters). Auto-computed from road width and lane.")]
        public float PadWidth;

        [Tooltip("How far the pad surface sits above the road to avoid z-fighting (meters).")]
        [Range(0.01f, 0.2f)]
        public float SurfaceLift = 0.06f;

        [Header("Visuals")]
        [Tooltip("Persistent project material used by the generated pad surface.")]
        public Material SurfaceMaterial;

        // ──────────────────────────────────────────────
        //  Placement
        // ──────────────────────────────────────────────

        [Header("Placement")]
        [Tooltip("Lane placement. 0 = center/full width, 1 = left lane, 2 = right lane.")]
        [Range(0, 2)]
        public int Lane = 0;

        // ──────────────────────────────────────────────
        //  Gameplay
        // ──────────────────────────────────────────────

        [Header("Gameplay")]
        [Tooltip("Forward speed added when crossing the pad (m/s).")]
        [Range(5f, 100f)]
        public float BoostStrength = 30f;

        [Tooltip("Cooldown before the same craft can be boosted again (seconds).")]
        [Range(0f, 5f)]
        public float ReTriggerCooldown = 1f;

        // ──────────────────────────────────────────────
        //  References (Auto-Populated)
        // ──────────────────────────────────────────────

        [Header("References (Auto-Populated)")]
        [SerializeField] private MeshFilter padMeshFilter;
        [SerializeField] private BoxCollider boostTrigger;

        // ──────────────────────────────────────────────
        //  Debug
        // ──────────────────────────────────────────────

        [Header("Debug")]
        public bool showDebugGizmos = true;
        public Color padColor = new Color(0f, 0.8f, 1f, 0.6f);

        /// <summary>World-space boost direction (the road tangent at placement).</summary>
        public Vector3 BoostDirection => transform.forward;

        public float ComputedLateralOffset { get; private set; }

        // ──────────────────────────────────────────────
        //  Generation
        // ──────────────────────────────────────────────

        /// <summary>
        /// Procedurally generates the pad mesh and trigger, oriented to the given road frame.
        /// </summary>
        public void Generate(TrackConnectionFrame trackFrame, float roadWidth, int targetLane, float boostStrength,
            Material surfaceMaterial = null)
        {
            Lane = targetLane;
            BoostStrength = boostStrength;
            if (surfaceMaterial != null) SurfaceMaterial = surfaceMaterial;

            if (Lane == 0)
            {
                PadWidth = roadWidth * 0.9f;
                ComputedLateralOffset = 0f;
            }
            else
            {
                PadWidth = roadWidth * 0.45f;
                ComputedLateralOffset = (Lane == 1) ? -roadWidth * 0.25f : roadWidth * 0.25f;
            }

            // Orient with the road: +Z = travel direction, up = banked road normal.
            transform.position = trackFrame.Position;
            transform.rotation = Quaternion.LookRotation(trackFrame.Forward, trackFrame.Up);

            // Mesh — a thin glowing slab slightly above the road surface.
            GameObject padObj = new GameObject("BoostPadMesh");
            padObj.transform.SetParent(transform, false);
            padObj.transform.localPosition = new Vector3(ComputedLateralOffset, SurfaceLift, 0f);

            padMeshFilter = padObj.AddComponent<MeshFilter>();
            var renderer = padObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SurfaceMaterial;
            if (SurfaceMaterial == null)
                Debug.LogWarning("[BoostPadActor] No persistent BoostSurface material was supplied.", this);

            padMeshFilter.sharedMesh = BuildPadMesh();

            // Trigger — generous vertical size so a hovering craft still registers.
            boostTrigger = padObj.AddComponent<BoxCollider>();
            boostTrigger.isTrigger = true;
            boostTrigger.center = new Vector3(0f, 1.5f, PadLength * 0.5f);
            boostTrigger.size = new Vector3(PadWidth, 3f, PadLength);
        }

        private UnityEngine.Mesh BuildPadMesh()
        {
            float hw = PadWidth * 0.5f;

            // Chevron-tipped slab: the taper at the front telegraphs boost direction.
            float tip = Mathf.Min(1.5f, PadLength * 0.25f);
            var vertices = new Vector3[]
            {
                new Vector3(-hw, 0f, 0f),
                new Vector3(hw, 0f, 0f),
                new Vector3(-hw, 0f, PadLength - tip),
                new Vector3(hw, 0f, PadLength - tip),
                new Vector3(0f, 0f, PadLength)
            };
            var uvs = new Vector2[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0.8f), new Vector2(1f, 0.8f),
                new Vector2(0.5f, 1f)
            };
            var triangles = new int[]
            {
                0, 2, 3,  0, 3, 1,   // body
                2, 4, 3              // arrow tip
            };

            var mesh = new UnityEngine.Mesh { name = "GeneratedBoostPadMesh" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        // ──────────────────────────────────────────────
        //  Editor Debug
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            Gizmos.color = padColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(ComputedLateralOffset, 0.5f, PadLength * 0.5f), new Vector3(PadWidth, 1f, PadLength));
            Gizmos.DrawLine(new Vector3(ComputedLateralOffset, 0.5f, PadLength * 0.5f), new Vector3(ComputedLateralOffset, 0.5f, PadLength * 0.5f + 4f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
