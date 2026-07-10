using UnityEngine;
using TrackGeneration.Splines;

namespace TrackGeneration.Stunts
{
    /// <summary>Which half of a jump-gap chain this ramp is.</summary>
    public enum RampMode
    {
        /// <summary>Ascending wedge — throws the hovercraft airborne at the lip.</summary>
        Launch,

        /// <summary>Descending wedge (motocross landing) — catches the craft and eases it back onto the road.</summary>
        Landing
    }

    /// <summary>
    /// A Ramp actor: one half of a Jump Ramp → Air Gap → Landing Ramp chain.
    /// Can spawn on one lane or across the full width.
    /// </summary>
    [AddComponentMenu("Track Generation/Stunts/Ramp Actor")]
    public class RampActor : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Geometry
        // ──────────────────────────────────────────────

        [Header("Geometry")]
        [Tooltip("Launch or Landing half of the jump chain.")]
        public RampMode Mode = RampMode.Launch;

        [Tooltip("Length of the ramp along the track (meters).")]
        [Range(4f, 25f)]
        public float RampLength = 12f;

        [Tooltip("Peak height at the lip (meters).")]
        [Range(1f, 10f)]
        public float RampHeight = 3f;

        [Tooltip("Width of the ramp (meters). Auto-computed but can be overridden.")]
        public float RampWidth;

        [Tooltip("Height curve from start to peak (0 to 1).")]
        public AnimationCurve RampProfile = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // ──────────────────────────────────────────────
        //  Placement
        // ──────────────────────────────────────────────

        [Header("Placement")]
        [Tooltip("Lane placement. 0 = center/full, 1 = left, 2 = right.")]
        [Range(0, 2)]
        public int Lane = 0;

        // ──────────────────────────────────────────────
        //  Gameplay
        // ──────────────────────────────────────────────

        [Header("Gameplay")]
        [Tooltip("Multiplier for upward impulse applied at the lip.")]
        [Range(0.5f, 3f)]
        public float LaunchForceMultiplier = 1f;

        [Tooltip("Whether there's a trick scoring zone above the ramp.")]
        public bool HasTrickZone = true;

        [Tooltip("Height of the invisible trick detection box (meters).")]
        [Range(5f, 50f)]
        public float TrickZoneHeight = 20f;

        [Tooltip("Score multiplier when performing tricks in the zone.")]
        [Range(1f, 5f)]
        public float TrickZoneScoreMultiplier = 1.5f;

        // ──────────────────────────────────────────────
        //  References (Auto-Populated)
        // ──────────────────────────────────────────────

        [Header("References (Auto-Populated)")]
        [SerializeField] private MeshFilter rampMeshFilter;
        [SerializeField] private MeshCollider rampCollider;
        [SerializeField] private BoxCollider trickZoneTrigger;
        [SerializeField] private BoxCollider launchTrigger;

        // ──────────────────────────────────────────────
        //  Debug
        // ──────────────────────────────────────────────

        [Header("Debug")]
        public bool showDebugGizmos = true;
        public Color rampColor = new Color(1f, 0.6f, 0f, 0.5f);
        public Color trickZoneColor = new Color(0f, 1f, 0f, 0.15f);

        // ──────────────────────────────────────────────
        //  State Properties
        // ──────────────────────────────────────────────

        public bool IsFullWidth => Lane == 0;
        public float ComputedLateralOffset { get; private set; }
        public float ComputedRampWidth { get; private set; }

        // ──────────────────────────────────────────────
        //  Generation
        // ──────────────────────────────────────────────

        /// <summary>
        /// Procedurally generates the ramp mesh and triggers.
        /// </summary>
        public void Generate(SplineFrame trackFrame, float roadWidth, int targetLane)
        {
            Lane = targetLane;

            // Compute Width and Offset
            if (IsFullWidth)
            {
                ComputedRampWidth = roadWidth;
                ComputedLateralOffset = 0f;
            }
            else
            {
                ComputedRampWidth = roadWidth * 0.5f; // Half road = one lane width (e.g., 4m)
                ComputedLateralOffset = (Lane == 1) ? -roadWidth * 0.25f : roadWidth * 0.25f;
            }
            RampWidth = ComputedRampWidth;

            // Create Mesh Object
            GameObject rampMeshObj = new GameObject("RampMesh");
            rampMeshObj.transform.SetParent(this.transform, false);

            rampMeshFilter = rampMeshObj.AddComponent<MeshFilter>();
            var renderer = rampMeshObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            rampCollider = rampMeshObj.AddComponent<MeshCollider>();

            // Generate Mesh
            GenerateRampMesh(rampMeshFilter);

            // Orient based on spline frame
            transform.position = trackFrame.Position;
            transform.rotation = Quaternion.LookRotation(trackFrame.Tangent, trackFrame.Up);
            // Apply lateral offset
            rampMeshObj.transform.localPosition = new Vector3(ComputedLateralOffset, 0f, 0f);

            // Triggers — only the LAUNCH half gets a trick zone and launch trigger.
            // The landing half is passive geometry that catches the craft.
            if (Mode == RampMode.Launch)
            {
                if (HasTrickZone)
                {
                    GameObject trickZoneObj = new GameObject("TrickZoneTrigger");
                    trickZoneObj.transform.SetParent(rampMeshObj.transform, false);
                    trickZoneTrigger = trickZoneObj.AddComponent<BoxCollider>();
                    trickZoneTrigger.isTrigger = true;
                    trickZoneTrigger.center = new Vector3(0f, RampHeight + TrickZoneHeight * 0.5f, RampLength * 0.5f);
                    trickZoneTrigger.size = new Vector3(RampWidth, TrickZoneHeight, RampLength);
                }

                GameObject launchObj = new GameObject("LaunchTrigger");
                launchObj.transform.SetParent(rampMeshObj.transform, false);
                launchTrigger = launchObj.AddComponent<BoxCollider>();
                launchTrigger.isTrigger = true;
                // Place thin trigger exactly at the lip of the ramp
                launchTrigger.center = new Vector3(0f, RampHeight + 0.5f, RampLength);
                launchTrigger.size = new Vector3(RampWidth, 1f, 0.5f);
            }
        }

        private void GenerateRampMesh(MeshFilter filter)
        {
            // Builds a CLOSED wedge: top surface + two side skirts + vertical cap at the
            // tall end. A closed solid reads correctly from every camera angle and gives
            // the physics hovercraft no open edge to catch on.
            int numSegments = 20; // Resolution along the ramp length
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            float halfWidth = RampWidth * 0.5f;

            // ── Top surface ──
            for (int i = 0; i <= numSegments; i++)
            {
                float t = (float)i / numSegments;
                float z = t * RampLength;
                float y = EvaluateRampHeight(t);

                vertices.Add(new Vector3(-halfWidth, y, z));
                vertices.Add(new Vector3(halfWidth, y, z));
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
            }

            for (int i = 0; i < numSegments; i++)
            {
                int currLeft = i * 2;
                int currRight = i * 2 + 1;
                int nextLeft = (i + 1) * 2;
                int nextRight = (i + 1) * 2 + 1;

                triangles.Add(currLeft); triangles.Add(nextLeft); triangles.Add(nextRight);
                triangles.Add(currLeft); triangles.Add(nextRight); triangles.Add(currRight);
            }

            // ── Side skirts (duplicate verts for hard-edged normals) ──
            // Each side: quads from the top edge straight down to y=0.
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -halfWidth : halfWidth;
                int baseIdx = vertices.Count;

                for (int i = 0; i <= numSegments; i++)
                {
                    float t = (float)i / numSegments;
                    float z = t * RampLength;
                    float y = EvaluateRampHeight(t);
                    vertices.Add(new Vector3(x, y, z));   // top
                    vertices.Add(new Vector3(x, 0f, z));  // bottom
                    uvs.Add(new Vector2(t, 1f));
                    uvs.Add(new Vector2(t, 0f));
                }

                for (int i = 0; i < numSegments; i++)
                {
                    int topA = baseIdx + i * 2;
                    int botA = baseIdx + i * 2 + 1;
                    int topB = baseIdx + (i + 1) * 2;
                    int botB = baseIdx + (i + 1) * 2 + 1;

                    if (side == 0)
                    {
                        // Left skirt faces -X
                        triangles.Add(topA); triangles.Add(botB); triangles.Add(topB);
                        triangles.Add(topA); triangles.Add(botA); triangles.Add(botB);
                    }
                    else
                    {
                        // Right skirt faces +X
                        triangles.Add(topA); triangles.Add(topB); triangles.Add(botB);
                        triangles.Add(topA); triangles.Add(botB); triangles.Add(botA);
                    }
                }
            }

            // ── Cap at the tall end (lip face) ──
            {
                bool tallAtEnd = Mode == RampMode.Launch; // Launch: peak at z=L. Landing: peak at z=0.
                float z = tallAtEnd ? RampLength : 0f;
                float y = tallAtEnd ? EvaluateRampHeight(1f) : EvaluateRampHeight(0f);

                int baseIdx = vertices.Count;
                vertices.Add(new Vector3(-halfWidth, y, z));
                vertices.Add(new Vector3(halfWidth, y, z));
                vertices.Add(new Vector3(-halfWidth, 0f, z));
                vertices.Add(new Vector3(halfWidth, 0f, z));
                uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));

                if (tallAtEnd)
                {
                    // Face points +Z (away from approach direction)
                    triangles.Add(baseIdx); triangles.Add(baseIdx + 2); triangles.Add(baseIdx + 3);
                    triangles.Add(baseIdx); triangles.Add(baseIdx + 3); triangles.Add(baseIdx + 1);
                }
                else
                {
                    // Face points -Z
                    triangles.Add(baseIdx); triangles.Add(baseIdx + 3); triangles.Add(baseIdx + 2);
                    triangles.Add(baseIdx); triangles.Add(baseIdx + 1); triangles.Add(baseIdx + 3);
                }
            }

            UnityEngine.Mesh mesh = new UnityEngine.Mesh();
            mesh.name = "GeneratedRampMesh";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();

            filter.sharedMesh = mesh;
            filter.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>
        /// Evaluates the ramp profile curve at a normalized position (0-1).
        /// Launch mode ascends toward the lip at t=1; Landing mode is the mirrored
        /// motocross catch ramp — peak at t=0, easing down to road level at t=1.
        /// </summary>
        public float EvaluateRampHeight(float normalizedPosition)
        {
            float t = Mode == RampMode.Landing ? 1f - normalizedPosition : normalizedPosition;
            if (RampProfile == null) return t * RampHeight;
            return RampProfile.Evaluate(t) * RampHeight;
        }

        // ──────────────────────────────────────────────
        //  Editor Debug
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            Gizmos.color = rampColor;
            
            // Draw simple profile line if no mesh
            if (rampMeshFilter == null)
            {
                Vector3 start = transform.position;
                Vector3 forward = transform.forward;
                Vector3 up = transform.up;
                
                Vector3 prevPos = start;
                int segments = 10;
                for (int i = 1; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float h = EvaluateRampHeight(t);
                    Vector3 pos = start + forward * (t * RampLength) + up * h;
                    Gizmos.DrawLine(prevPos, pos);
                    prevPos = pos;
                }
            }
            else
            {
                // Draw bounds
                Gizmos.matrix = rampMeshFilter.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(new Vector3(0, RampHeight * 0.5f, RampLength * 0.5f), new Vector3(RampWidth, RampHeight, RampLength));
                Gizmos.matrix = Matrix4x4.identity;
            }

            if (HasTrickZone && Mode == RampMode.Launch)
            {
                Gizmos.color = trickZoneColor;
                Vector3 tzCenter = transform.position + transform.forward * (RampLength * 0.5f) + transform.up * (RampHeight + TrickZoneHeight * 0.5f);
                // Adjust for lateral offset
                tzCenter += transform.right * ComputedLateralOffset;

                Gizmos.DrawWireCube(tzCenter, new Vector3(RampWidth, TrickZoneHeight, RampLength));
            }
        }
    }
}
