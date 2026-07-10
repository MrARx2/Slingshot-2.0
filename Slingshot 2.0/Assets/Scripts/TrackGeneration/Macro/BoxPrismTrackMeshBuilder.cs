using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// Builds the actual track geometry from macro sections.
    ///
    /// GLOBAL HALF-PIPE RULE: every road section is generated with the half-pipe /
    /// water-slide cross-section — center driving floor, smooth curved rising side
    /// walls the hovercraft can ride. This is the global road shape, not a feature:
    /// straights, banked curves, splits, bridges, jumps, loops, and corkscrews all
    /// use it. FlatWithWalls exists only as a legacy/debug fallback.
    ///
    /// Geometry stays clean prism/ring-based: each subdivision frame becomes one
    /// cross-section ring (curved top profile + safety lip + outer walls + bottom
    /// slab), and consecutive rings are stitched with quads. No n-gons, no messy
    /// topology. The MeshCollider uses the SAME mesh, so collision follows the
    /// rideable half-pipe surface exactly — no invisible vertical walls.
    ///
    /// Because a section's first ring IS the previous section's last ring (the
    /// connection frame contract), separate meshes join with zero gaps.
    /// </summary>
    public class BoxPrismTrackMeshBuilder
    {
        // Road slab thickness in meters (center floor surface to bottom face).
        private const float RoadThickness = 1.2f;

        // UV tiling length along the track in meters.
        private const float TileLength = 10f;

        // Outward thickness of the top safety lip (meters). Keeps the barrier a real
        // solid instead of a zero-thickness fin (bad for collision).
        private const float LipThickness = 0.4f;

        private readonly TrackRoadProfileSettings _profile;
        private bool _warnedMissingProfile;

        public BoxPrismTrackMeshBuilder() : this(null) { }

        public BoxPrismTrackMeshBuilder(TrackRoadProfileSettings profile)
        {
            _profile = profile ?? new TrackRoadProfileSettings();
        }

        /// <summary>
        /// Builds one mesh GameObject per macro section under <paramref name="root"/>.
        /// AirGap sections emit nothing (they ARE the hole).
        /// </summary>
        public void Build(List<GeneratedTrackSection> sections, Material roadMaterial, Material sideMaterial, Transform root)
        {
            if (!_profile.IsHalfPipe && !_warnedMissingProfile)
            {
                _warnedMissingProfile = true;
                Debug.LogWarning("[BoxPrismTrackMeshBuilder] Building with FlatWithWalls cross-section — this is a legacy/debug mode. The hovercraft track should use the global half-pipe road shape.");
            }

            foreach (var section in sections)
            {
                if (section.IsEmptySpace || section.SubdivisionFrames == null || section.SubdivisionFrames.Length < 2)
                    continue;

                section.SectionObject = BuildSection(section, roadMaterial, sideMaterial, root);
            }
        }

        // ──────────────────────────── Ring cross-section ────────────────────────────

        /// <summary>Number of drivable top-surface profile points across the width.</summary>
        private int TopPointCount => _profile.ProfilePointCount;

        /// <summary>Verts per ring: N smooth top points + 14 duplicated hard-edge verts (lip/walls/bottom).</summary>
        private int VertsPerRing => TopPointCount + 14;

        private float SideHeightFor(TrackConnectionFrame f)
            => f.SideHeight > 0.001f ? f.SideHeight : _profile.SideHeight;

        /// <summary>
        /// Top-surface point of the half-pipe profile in root-local space.
        /// The center floor stays AT the frame baseline; sides rise upward along the frame's
        /// Up axis — the banked/pitched frame tilts the whole bowl, so banking is an
        /// additional transformation ON TOP of the global half-pipe shape.
        /// Heights are never negative: no hidden dips below the section baseline.
        /// </summary>
        private Vector3 TopPoint(TrackConnectionFrame f, float normalizedX)
        {
            float halfW = f.Width * 0.5f;
            float h = _profile.HeightAt(normalizedX, halfW, SideHeightFor(f));
            return f.Position + f.Right * (normalizedX * halfW) + f.Up * h;
        }

        private float EdgeHeight(TrackConnectionFrame f)
            => _profile.HeightAt(1f, f.Width * 0.5f, SideHeightFor(f));

        private float LipHeight => Mathf.Max(0.05f, _profile.SafetyLipHeight);

        // ──────────────────────────── Section mesh ────────────────────────────

        private GameObject BuildSection(GeneratedTrackSection section, Material roadMaterial, Material sideMaterial, Transform root)
        {
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            int rings = frames.Length;
            int n = TopPointCount;
            int vpr = VertsPerRing;

            // Section pivot sits at the entry frame; verts are stored pivot-local so the
            // hierarchy has meaningful positions.
            Vector3 origin = section.StartFrame.Position;

            var vertices = new List<Vector3>(rings * vpr + n * 2 + 16);
            var uvs = new List<Vector2>(rings * vpr + n * 2 + 16);
            var roadTris = new List<int>((rings - 1) * (n - 1) * 6);
            var sideTris = new List<int>((rings - 1) * 42 + 64);

            // ── Ring vertices ──
            // Layout per ring (base index r*vpr):
            //  0..n-1 : drivable half-pipe top profile, left → right (SMOOTH, road submesh)
            //  n+0/1  : left inner lip strip   (lipTopL, edgeTopL)         faces +Right
            //  n+2/3  : left lip cap strip     (lipTopOuterL, lipTopL)     faces +Up
            //  n+4/5  : left outer wall strip  (botOuterL, lipTopOuterL)   faces -Right
            //  n+6/7  : right inner lip strip  (edgeTopR, lipTopR)         faces -Right
            //  n+8/9  : right lip cap strip    (lipTopR, lipTopOuterR)     faces +Up
            //  n+10/11: right outer wall strip (lipTopOuterR, botOuterR)   faces +Right
            //  n+12/13: bottom strip           (botOuterR, botOuterL)      faces -Up
            for (int r = 0; r < rings; r++)
            {
                TrackConnectionFrame f = frames[r];
                float halfW = f.Width * 0.5f;
                float lip = LipHeight;
                float v = f.ArcLength / TileLength;

                // Smooth drivable profile (road submesh).
                for (int i = 0; i < n; i++)
                {
                    float xNorm = n > 1 ? -1f + 2f * i / (n - 1) : 0f;
                    vertices.Add(TopPoint(f, xNorm) - origin);
                    uvs.Add(new Vector2((xNorm + 1f) * 0.5f, v));
                }

                Vector3 edgeTopL = TopPoint(f, -1f) - origin;
                Vector3 edgeTopR = TopPoint(f, 1f) - origin;
                Vector3 lipTopL = edgeTopL + f.Up * lip;
                Vector3 lipTopR = edgeTopR + f.Up * lip;
                Vector3 lipTopOuterL = lipTopL - f.Right * LipThickness;
                Vector3 lipTopOuterR = lipTopR + f.Right * LipThickness;
                Vector3 botOuterL = f.Position - f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;
                Vector3 botOuterR = f.Position + f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;

                // Left inner lip (P, Q): normal = Forward × (Q−P) → Q−P must be -Up for +Right.
                vertices.Add(lipTopL); uvs.Add(new Vector2(v, 1f));
                vertices.Add(edgeTopL); uvs.Add(new Vector2(v, 0f));

                // Left lip cap: Q−P = +Right → +Up.
                vertices.Add(lipTopOuterL); uvs.Add(new Vector2(v, 0f));
                vertices.Add(lipTopL); uvs.Add(new Vector2(v, 1f));

                // Left outer wall: Q−P = +Up → -Right.
                vertices.Add(botOuterL); uvs.Add(new Vector2(v, 0f));
                vertices.Add(lipTopOuterL); uvs.Add(new Vector2(v, 1f));

                // Right inner lip: Q−P = +Up → -Right (faces back toward the road).
                vertices.Add(edgeTopR); uvs.Add(new Vector2(v, 0f));
                vertices.Add(lipTopR); uvs.Add(new Vector2(v, 1f));

                // Right lip cap: Q−P = +Right → +Up.
                vertices.Add(lipTopR); uvs.Add(new Vector2(v, 1f));
                vertices.Add(lipTopOuterR); uvs.Add(new Vector2(v, 0f));

                // Right outer wall: Q−P = -Up → +Right.
                vertices.Add(lipTopOuterR); uvs.Add(new Vector2(v, 1f));
                vertices.Add(botOuterR); uvs.Add(new Vector2(v, 0f));

                // Bottom: Q−P = -Right → -Up.
                vertices.Add(botOuterR); uvs.Add(new Vector2(0f, v));
                vertices.Add(botOuterL); uvs.Add(new Vector2(1f, v));
            }

            // ── Stitch consecutive rings ──
            for (int r = 0; r < rings - 1; r++)
            {
                int a = r * vpr;
                int b = (r + 1) * vpr;

                // Drivable half-pipe surface: one smooth quad strip across the full width.
                // The walls ARE road surface — the craft can ride up the sides everywhere.
                for (int i = 0; i < n - 1; i++)
                {
                    roadTris.Add(a + i); roadTris.Add(b + i); roadTris.Add(b + i + 1);
                    roadTris.Add(a + i); roadTris.Add(b + i + 1); roadTris.Add(a + i + 1);
                }

                // Hard-edge strips (side submesh): 7 strips of duplicated vert pairs.
                for (int s = 0; s < 7; s++)
                {
                    int p = n + s * 2;
                    sideTris.Add(a + p); sideTris.Add(b + p); sideTris.Add(b + p + 1);
                    sideTris.Add(a + p); sideTris.Add(b + p + 1); sideTris.Add(a + p + 1);
                }
            }

            // ── End caps where the road stops in mid-air (jump lip / landing peak) ──
            if (section.Definition.SectionType == TrackMacroSectionType.JumpRamp)
            {
                AddCap(vertices, uvs, sideTris, frames[rings - 1], origin, facingForward: true);
            }
            else if (section.Definition.SectionType == TrackMacroSectionType.LandingRamp)
            {
                AddCap(vertices, uvs, sideTris, frames[0], origin, facingForward: false);
            }

            // ── Mesh ──
            // Fully qualified: "Mesh" alone would resolve to the TrackGeneration.Mesh NAMESPACE.
            var mesh = new UnityEngine.Mesh { name = $"MacroSection_{section.SectionIndex:D2}" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(roadTris, 0);
            mesh.SetTriangles(sideTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            // ── GameObject ──
            var obj = new GameObject($"S{section.SectionIndex:D2}_{section.Definition.DebugName}");
            obj.transform.SetParent(root, false);
            obj.transform.localPosition = origin;

            var filter = obj.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { roadMaterial, sideMaterial };

            // Collision follows the half-pipe shape exactly: the same mesh is the collider,
            // so the hovercraft can physically ride up the curved sides. The only "wall"
            // is the small safety lip at the very top edge of the half-pipe.
            var collider = obj.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            return obj;
        }

        /// <summary>
        /// Cap closing the exposed cross-section at a jump lip (faces travel direction)
        /// or a landing peak (faces against travel direction). Fans over the full
        /// half-pipe boundary from the bottom center, which sees every boundary point.
        /// </summary>
        private void AddCap(List<Vector3> vertices, List<Vector2> uvs, List<int> tris, TrackConnectionFrame f, Vector3 origin, bool facingForward)
        {
            int n = TopPointCount;
            float halfW = f.Width * 0.5f;
            float lip = LipHeight;

            Vector3 lipTopL = TopPoint(f, -1f) + f.Up * lip - origin;
            Vector3 lipTopR = TopPoint(f, 1f) + f.Up * lip - origin;
            Vector3 lipTopOuterL = lipTopL - f.Right * LipThickness;
            Vector3 lipTopOuterR = lipTopR + f.Right * LipThickness;
            Vector3 botOuterL = f.Position - f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;
            Vector3 botOuterR = f.Position + f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;

            // Boundary chain: bottom-left, up the left wall, across the drivable profile,
            // up/over the right lip, down to bottom-right.
            var boundary = new List<Vector3> { botOuterL, lipTopOuterL, lipTopL };
            for (int i = 0; i < n; i++)
            {
                float xNorm = n > 1 ? -1f + 2f * i / (n - 1) : 0f;
                boundary.Add(TopPoint(f, xNorm) - origin);
            }
            boundary.Add(lipTopR);
            boundary.Add(lipTopOuterR);
            boundary.Add(botOuterR);

            Vector3 center = (botOuterL + botOuterR) * 0.5f;

            int c = vertices.Count;
            vertices.Add(center); uvs.Add(new Vector2(0.5f, 0f));
            int b0 = vertices.Count;
            for (int i = 0; i < boundary.Count; i++)
            {
                vertices.Add(boundary[i]);
                uvs.Add(new Vector2((float)i / (boundary.Count - 1), 1f));
            }

            for (int k = 0; k < boundary.Count - 1; k++)
            {
                if (facingForward)
                {
                    tris.Add(c); tris.Add(b0 + k + 1); tris.Add(b0 + k);
                }
                else
                {
                    tris.Add(c); tris.Add(b0 + k); tris.Add(b0 + k + 1);
                }
            }
        }
    }
}
