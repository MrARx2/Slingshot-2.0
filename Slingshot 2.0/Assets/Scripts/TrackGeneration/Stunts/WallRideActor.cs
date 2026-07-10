using UnityEngine;
using TrackGeneration.Splines;

namespace TrackGeneration.Stunts
{
    /// <summary>
    /// A Wall Ride section on the track. Replaces the normal road surface with two vertical walls.
    /// The hovercraft must magnetically attach to one wall, drive along it, and optionally transition.
    /// </summary>
    [AddComponentMenu("Track Generation/Stunts/Wall Ride Actor")]
    public class WallRideActor : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Geometry
        // ──────────────────────────────────────────────

        [Header("Geometry")]
        [Tooltip("Length of the wall ride section along the track (meters).")]
        [Range(20f, 100f)]
        public float SectionLength = 40f;

        [Tooltip("Height of each vertical wall (meters). Must fit the hovercraft sideways.")]
        [Range(5f, 30f)]
        public float WallHeight = 12f;

        [Tooltip("Gap between the two walls (meters). Matches road width.")]
        [Range(4f, 20f)]
        public float WallSeparation = 8f;

        [Tooltip("Thickness of each wall (meters).")]
        [Range(0.3f, 3f)]
        public float WallThickness = 1f;

        // ──────────────────────────────────────────────
        //  Transition
        // ──────────────────────────────────────────────

        [Header("Transition")]
        [Tooltip("Length where road curves up into the wall (meters).")]
        [Range(5f, 30f)]
        public float EntryRampLength = 10f;

        [Tooltip("Length where wall curves back to road (meters).")]
        [Range(5f, 30f)]
        public float ExitRampLength = 10f;

        // ──────────────────────────────────────────────
        //  Gameplay
        // ──────────────────────────────────────────────

        [Header("Gameplay")]
        [Tooltip("How close hovercraft must be to magnetically attach (meters).")]
        [Range(1f, 8f)]
        public float MagneticAttachRadius = 3f;

        [Tooltip("Speed multiplier while wall riding.")]
        [Range(1f, 2f)]
        public float WallRideSpeedBoost = 1.2f;

        [Tooltip("Can the hovercraft jump from one wall to the other?")]
        public bool AllowWallTransition = true;

        // ──────────────────────────────────────────────
        //  References (Auto-Populated)
        // ──────────────────────────────────────────────

        [Header("References (Auto-Populated)")]
        [SerializeField] private MeshFilter leftWallMesh;
        [SerializeField] private MeshFilter rightWallMesh;
        [SerializeField] private MeshCollider leftWallCollider;
        [SerializeField] private MeshCollider rightWallCollider;
        [SerializeField] private BoxCollider leftWallTrigger;
        [SerializeField] private BoxCollider rightWallTrigger;

        // ──────────────────────────────────────────────
        //  Debug
        // ──────────────────────────────────────────────

        [Header("Debug")]
        public bool showDebugGizmos = true;
        public Color leftWallColor = Color.cyan;
        public Color rightWallColor = Color.magenta;

        // ──────────────────────────────────────────────
        //  State Properties
        // ──────────────────────────────────────────────

        public enum WallSide { None, Left, Right }

        public bool IsHovercraftOnWall => ActiveWallSide != WallSide.None;
        public WallSide ActiveWallSide { get; private set; } = WallSide.None;

        // ──────────────────────────────────────────────
        //  Generation
        // ──────────────────────────────────────────────

        /// <summary>
        /// Procedurally generates the wall ride geometry based on the spline frames.
        /// </summary>
        public void Generate(SplineFrame[] trackFrames, float roadWidth)
        {
            if (trackFrames == null || trackFrames.Length < 2) return;

            // Ensure separation matches road width conceptually
            WallSeparation = roadWidth;

            // Set actor transform from the first frame so child meshes can use local space.
            transform.position = trackFrames[0].Position;
            transform.rotation = Quaternion.LookRotation(trackFrames[0].Tangent, trackFrames[0].Up);

            // Generate Left Wall
            GameObject leftGO = CreateWallObject("LeftWall", out leftWallMesh, out leftWallCollider, out leftWallTrigger);
            GenerateWallMesh(leftWallMesh, trackFrames, -WallSeparation * 0.5f, true);
            SetupWallTrigger(leftWallTrigger, true);

            // Generate Right Wall
            GameObject rightGO = CreateWallObject("RightWall", out rightWallMesh, out rightWallCollider, out rightWallTrigger);
            GenerateWallMesh(rightWallMesh, trackFrames, WallSeparation * 0.5f, false);
            SetupWallTrigger(rightWallTrigger, false);
        }

        private GameObject CreateWallObject(string objName, out MeshFilter mFilter, out MeshCollider mCollider, out BoxCollider bCollider)
        {
            GameObject wallObj = new GameObject(objName);
            wallObj.transform.SetParent(this.transform, false);

            mFilter = wallObj.AddComponent<MeshFilter>();
            var renderer = wallObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); // Default material

            mCollider = wallObj.AddComponent<MeshCollider>();
            
            bCollider = wallObj.AddComponent<BoxCollider>();
            bCollider.isTrigger = true;

            return wallObj;
        }

        private void GenerateWallMesh(MeshFilter filter, SplineFrame[] frames, float lateralOffset, bool isLeft)
        {
            // Simple generation to form a vertical wall curving along the spline.
            int numFrames = frames.Length;
            int numVerts = numFrames * 4; // Inner bottom, inner top, outer top, outer bottom
            
            Vector3[] vertices = new Vector3[numVerts];
            Vector2[] uvs = new Vector2[numVerts];
            int[] triangles = new int[(numFrames - 1) * 18];

            float sign = isLeft ? -1f : 1f;

            // Pre-compute inverse transform for world→local conversion.
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

            float currentDist = 0f;

            for (int i = 0; i < numFrames; i++)
            {
                SplineFrame frame = frames[i];
                Quaternion frameRotation = Quaternion.LookRotation(frame.Tangent, frame.Up);
                Vector3 worldPosition = (Vector3)frame.Position;

                // For entry/exit ramps: smoothly interpolate wall height
                float t = (float)i / (numFrames - 1);
                float distAlong = t * SectionLength;

                float currentHeight = WallHeight;
                if (distAlong < EntryRampLength)
                {
                    currentHeight = Mathf.Lerp(0f, WallHeight, distAlong / EntryRampLength);
                }
                else if (distAlong > SectionLength - ExitRampLength)
                {
                    currentHeight = Mathf.Lerp(WallHeight, 0f, (distAlong - (SectionLength - ExitRampLength)) / ExitRampLength);
                }

                // Local offsets relative to the frame
                Vector3 localInBot = new Vector3(lateralOffset, 0f, 0f);
                Vector3 localInTop = new Vector3(lateralOffset, currentHeight, 0f);
                Vector3 localOutTop = new Vector3(lateralOffset + WallThickness * sign, currentHeight, 0f);
                Vector3 localOutBot = new Vector3(lateralOffset + WallThickness * sign, 0f, 0f);

                // Compute world-space positions then convert to actor local-space
                vertices[i * 4 + 0] = worldToLocal.MultiplyPoint3x4(worldPosition + frameRotation * localInBot);
                vertices[i * 4 + 1] = worldToLocal.MultiplyPoint3x4(worldPosition + frameRotation * localInTop);
                vertices[i * 4 + 2] = worldToLocal.MultiplyPoint3x4(worldPosition + frameRotation * localOutTop);
                vertices[i * 4 + 3] = worldToLocal.MultiplyPoint3x4(worldPosition + frameRotation * localOutBot);

                if (i > 0)
                {
                    currentDist += Vector3.Distance(frames[i].Position, frames[i - 1].Position);
                }

                uvs[i * 4 + 0] = new Vector2(0f, currentDist / 10f);
                uvs[i * 4 + 1] = new Vector2(1f, currentDist / 10f);
                uvs[i * 4 + 2] = new Vector2(1f, currentDist / 10f);
                uvs[i * 4 + 3] = new Vector2(0f, currentDist / 10f);
            }

            int triIndex = 0;
            for (int i = 0; i < numFrames - 1; i++)
            {
                int curr = i * 4;
                int next = (i + 1) * 4;

                // Inner Face (faces the track)
                if (isLeft)
                {
                    triangles[triIndex++] = curr + 0; triangles[triIndex++] = curr + 1; triangles[triIndex++] = next + 1;
                    triangles[triIndex++] = curr + 0; triangles[triIndex++] = next + 1; triangles[triIndex++] = next + 0;
                }
                else
                {
                    triangles[triIndex++] = curr + 0; triangles[triIndex++] = next + 1; triangles[triIndex++] = curr + 1;
                    triangles[triIndex++] = curr + 0; triangles[triIndex++] = next + 0; triangles[triIndex++] = next + 1;
                }

                // Top Face
                if (isLeft)
                {
                    triangles[triIndex++] = curr + 1; triangles[triIndex++] = curr + 2; triangles[triIndex++] = next + 2;
                    triangles[triIndex++] = curr + 1; triangles[triIndex++] = next + 2; triangles[triIndex++] = next + 1;
                }
                else
                {
                    triangles[triIndex++] = curr + 1; triangles[triIndex++] = next + 2; triangles[triIndex++] = curr + 2;
                    triangles[triIndex++] = curr + 1; triangles[triIndex++] = next + 1; triangles[triIndex++] = next + 2;
                }

                // Outer Face
                if (isLeft)
                {
                    triangles[triIndex++] = curr + 2; triangles[triIndex++] = curr + 3; triangles[triIndex++] = next + 3;
                    triangles[triIndex++] = curr + 2; triangles[triIndex++] = next + 3; triangles[triIndex++] = next + 2;
                }
                else
                {
                    triangles[triIndex++] = curr + 2; triangles[triIndex++] = next + 3; triangles[triIndex++] = curr + 3;
                    triangles[triIndex++] = curr + 2; triangles[triIndex++] = next + 2; triangles[triIndex++] = next + 3;
                }
            }

            UnityEngine.Mesh wallMesh = new UnityEngine.Mesh();
            wallMesh.name = isLeft ? "LeftWallMesh" : "RightWallMesh";
            wallMesh.vertices = vertices;
            wallMesh.uv = uvs;
            wallMesh.triangles = triangles;
            wallMesh.RecalculateNormals();

            filter.sharedMesh = wallMesh;
            filter.GetComponent<MeshCollider>().sharedMesh = wallMesh;
        }

        private void SetupWallTrigger(BoxCollider trigger, bool isLeft)
        {
            // Simple box collider bounds over the wall for magnetic attach detection
            float sign = isLeft ? -1f : 1f;
            trigger.center = new Vector3(WallSeparation * 0.5f * sign, WallHeight * 0.5f, SectionLength * 0.5f);
            trigger.size = new Vector3(MagneticAttachRadius, WallHeight, SectionLength);
        }

        // ──────────────────────────────────────────────
        //  Runtime API
        // ──────────────────────────────────────────────

        public void OnHovercraftEnterWallZone(bool isLeftWall)
        {
            ActiveWallSide = isLeftWall ? WallSide.Left : WallSide.Right;
        }

        public void OnHovercraftExitWallZone()
        {
            ActiveWallSide = WallSide.None;
        }

        // ──────────────────────────────────────────────
        //  Editor Debug
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            Vector3 center = transform.position;

            Gizmos.color = leftWallColor;
            Gizmos.DrawWireCube(center + new Vector3(-WallSeparation * 0.5f, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, SectionLength));

            Gizmos.color = rightWallColor;
            Gizmos.DrawWireCube(center + new Vector3(WallSeparation * 0.5f, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, SectionLength));
        }
    }
}
