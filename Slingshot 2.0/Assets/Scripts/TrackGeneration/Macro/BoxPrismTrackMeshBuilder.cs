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
    /// JUNCTION / AIR-GAP OPENINGS: per-frame wall multipliers (set by the layout's
    /// wall-mask pass) suppress the INNER walls through split/merge throats so forked
    /// half-pipes never cross through each other, and air-gap boundaries (jump lip,
    /// landing mouth) are OPEN edges — no caps or geometry across the flight path.
    ///
    /// Geometry stays clean prism/ring-based: each subdivision frame becomes one
    /// cross-section ring (curved top profile + safety lip + outer walls + bottom
    /// slab), and consecutive rings are stitched with quads. No n-gons, no messy
    /// topology.
    ///
    /// STRUCTURE: render meshes are fixed-arc CHUNKS with LODGroups (per-section
    /// meshes are km-long, so screen-height LOD never engages on them); collision is
    /// continuous chain colliders chunked under the PhysX 2^21 triangle limit. Both
    /// slice one shared ring stream per chain, so every boundary ring is bit-exact on
    /// both sides and chunks join with zero gaps and zero lighting seams.
    /// </summary>
    public class BoxPrismTrackMeshBuilder
    {
        // Road slab thickness in meters (center floor surface to bottom face).
        private const float RoadThickness = 1.2f;

        // Collision slab thickness (collider only, thicker than the visual slab): at
        // 420 m/s a heavy frame can step the craft body more than a thin slab in one
        // discrete physics tick — the deeper collision volume catches it instead of
        // letting it tunnel through the road.
        private const float ColliderThickness = 5f;

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
        /// Builds the track as CHAIN-CHUNKED render meshes with LODs plus continuous
        /// chain colliders. AirGap sections emit nothing (they ARE the hole).
        ///
        /// Render meshes are fixed-arc chunks (not per-section): a 40 km lap otherwise
        /// draws millions of triangles with no level of detail — sections are km-long,
        /// so per-section LOD never engages. Normals are computed ONCE over the whole
        /// chain (wrap-aware at the start/finish weld), so no chunk or section boundary
        /// can ever show a lighting seam.
        /// </summary>
        public void Build(List<GeneratedTrackSection> sections, Material roadMaterial, Material sideMaterial, Transform root)
        {
            if (!_profile.IsHalfPipe && !_warnedMissingProfile)
            {
                _warnedMissingProfile = true;
                Debug.LogWarning("[BoxPrismTrackMeshBuilder] Building with FlatWithWalls cross-section — this is a legacy/debug mode. The hovercraft track should use the global half-pipe road shape.");
            }

            var chains = CollectChains(sections);
            for (int c = 0; c < chains.Count; c++)
            {
                BuildRenderChain(chains[c], c, roadMaterial, sideMaterial, root);
                BuildChainCollider(chains[c], c, root);
            }
        }

        // ──────────────────────────── Chain collection ────────────────────────────

        /// <summary>
        /// Contiguous runs of meshed sections. Runs break only where the road genuinely
        /// breaks: air gaps and branch forks/merges (route B starts at the split gate,
        /// not where route A ended). The start/finish weld is merged so the main lap is
        /// one chain.
        /// </summary>
        private static List<List<GeneratedTrackSection>> CollectChains(List<GeneratedTrackSection> sections)
        {
            var chains = new List<List<GeneratedTrackSection>>();
            List<GeneratedTrackSection> run = null;
            GeneratedTrackSection prev = null;

            foreach (var sec in sections)
            {
                if (sec.IsEmptySpace || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2)
                {
                    run = null;
                    prev = null;
                    continue;
                }

                bool connects = prev != null && !prev.OpenEnd && !sec.OpenStart &&
                    (prev.EndFrame.Position - sec.StartFrame.Position).sqrMagnitude <= 0.0025f;
                if (!connects)
                {
                    run = new List<GeneratedTrackSection>();
                    chains.Add(run);
                }

                run.Add(sec);
                prev = sec;
            }

            // The list starts mid-lap relative to the closure: if the LAST chain flows
            // into the FIRST (the start/finish weld), merge them into one run.
            if (chains.Count > 1)
            {
                var firstChain = chains[0];
                var lastChain = chains[chains.Count - 1];
                var tail = lastChain[lastChain.Count - 1];
                var head = firstChain[0];
                if (!tail.OpenEnd && !head.OpenStart &&
                    (tail.EndFrame.Position - head.StartFrame.Position).sqrMagnitude <= 0.0025f)
                {
                    lastChain.AddRange(firstChain);
                    chains.RemoveAt(0);
                }
            }

            return chains;
        }

        /// <summary>Flattens a chain to one ring stream (boundary duplicates skipped) and detects circularity.</summary>
        private static List<TrackConnectionFrame> ChainFrames(List<GeneratedTrackSection> chain, out bool circular)
        {
            var frames = new List<TrackConnectionFrame>();
            foreach (var sec in chain)
            {
                var f = sec.SubdivisionFrames;
                for (int i = frames.Count == 0 ? 0 : 1; i < f.Length; i++)
                    frames.Add(f[i]);
            }

            circular = frames.Count > 2 &&
                (frames[frames.Count - 1].Position - frames[0].Position).sqrMagnitude <= 0.0025f;
            if (circular) frames.RemoveAt(frames.Count - 1); // ring 0 IS the final ring
            return frames;
        }

        // PhysX's BVH34 midphase is broken above 2,097,152 (2^21) triangles per mesh —
        // Unity warns and collisions can be MISSED (the craft falls through the road).
        // Chunk size is chosen so a chunk stays far below the limit: 2000 rings at the
        // widest profile ≈ 350k triangles, and cooking stays fast per chunk.
        private const int MaxRingsPerColliderChunk = 2000;

        private void BuildChainCollider(List<GeneratedTrackSection> chain, int index, Transform root)
        {
            var frames = ChainFrames(chain, out bool circular);
            if (frames.Count < 2) return;

            var head = chain[0];
            var tail = chain[chain.Count - 1];

            // Virtual ring stream: 0..N-1 for open chains, 0..N for circular ones
            // (index N ≡ ring 0 — the start/finish weld is just another shared chunk
            // boundary). Chunks share their boundary ring EXACTLY, so the collision
            // surface is geometrically continuous across every chunk.
            int lastRing = circular ? frames.Count : frames.Count - 1;
            int chunkCount = Mathf.CeilToInt(lastRing / (float)MaxRingsPerColliderChunk);
            for (int c = 0; c < chunkCount; c++)
            {
                int r0 = c * MaxRingsPerColliderChunk;
                int r1 = Mathf.Min(lastRing, r0 + MaxRingsPerColliderChunk);
                bool capHead = c == 0 && !circular && head.CapStart && !head.OpenStart;
                bool capTail = c == chunkCount - 1 && !circular && tail.CapEnd && !tail.OpenEnd;
                BuildColliderChunk(frames, r0, r1, $"TrackCollider_{index:D2}_{c:D2}", root, capHead, capTail);
            }
        }

        private void BuildColliderChunk(List<TrackConnectionFrame> frames, int r0, int r1,
            string name, Transform root, bool capHead, bool capTail)
        {
            int n = TopPointCount;
            int vpr = VertsPerRing;
            int rings = r1 - r0 + 1;
            if (rings < 2) return;

            Vector3 origin = frames[r0 % frames.Count].Position;
            var vertices = new List<Vector3>(rings * vpr + 64);
            for (int r = r0; r <= r1; r++)
                EmitColliderRing(frames[r % frames.Count], origin, vertices);

            var tris = new List<int>((rings - 1) * (n + 7) * 6);
            for (int r = 0; r < rings - 1; r++)
            {
                int a = r * vpr;
                int b = (r + 1) * vpr;

                for (int i = 0; i < n - 1; i++)
                {
                    tris.Add(a + i); tris.Add(b + i); tris.Add(b + i + 1);
                    tris.Add(a + i); tris.Add(b + i + 1); tris.Add(a + i + 1);
                }
                for (int s = 0; s < 7; s++)
                {
                    int p = n + s * 2;
                    tris.Add(a + p); tris.Add(b + p); tris.Add(b + p + 1);
                    tris.Add(a + p); tris.Add(b + p + 1); tris.Add(a + p + 1);
                }
            }

            if (capHead) AddColliderCap(vertices, tris, frames[r0], origin, facingForward: false);
            if (capTail) AddColliderCap(vertices, tris, frames[r1], origin, facingForward: true);

            var mesh = new UnityEngine.Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            obj.transform.localPosition = origin;
            var collider = obj.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        /// <summary>Same ring vertex layout as the render mesh (n top points + 14 hard-edge verts), positions only.</summary>
        private void EmitColliderRing(TrackConnectionFrame f, Vector3 origin, List<Vector3> vertices)
        {
            int n = TopPointCount;
            float halfW = f.Width * 0.5f;
            float lip = LipHeight;

            for (int i = 0; i < n; i++)
                vertices.Add(TopPoint(f, _profile.ProfileXAt(i, n)) - origin);

            Vector3 edgeTopL = TopPoint(f, -1f) - origin;
            Vector3 edgeTopR = TopPoint(f, 1f) - origin;
            Vector3 lipTopL = edgeTopL + f.Up * (lip * f.LeftWallMultiplier);
            Vector3 lipTopR = edgeTopR + f.Up * (lip * f.RightWallMultiplier);
            Vector3 lipTopOuterL = lipTopL - f.Right * LipThickness;
            Vector3 lipTopOuterR = lipTopR + f.Right * LipThickness;
            Vector3 botOuterL = f.Position - f.Right * (halfW + LipThickness) - f.Up * ColliderThickness - origin;
            Vector3 botOuterR = f.Position + f.Right * (halfW + LipThickness) - f.Up * ColliderThickness - origin;

            vertices.Add(lipTopL); vertices.Add(edgeTopL);
            vertices.Add(lipTopOuterL); vertices.Add(lipTopL);
            vertices.Add(botOuterL); vertices.Add(lipTopOuterL);
            vertices.Add(edgeTopR); vertices.Add(lipTopR);
            vertices.Add(lipTopR); vertices.Add(lipTopOuterR);
            vertices.Add(lipTopOuterR); vertices.Add(botOuterR);
            vertices.Add(botOuterR); vertices.Add(botOuterL);
        }

        /// <summary>Positions-only version of <see cref="AddCap"/> for chain collider end caps.</summary>
        private void AddColliderCap(List<Vector3> vertices, List<int> tris, TrackConnectionFrame f, Vector3 origin, bool facingForward)
        {
            int n = TopPointCount;
            float halfW = f.Width * 0.5f;
            float lip = LipHeight;

            Vector3 lipTopL = TopPoint(f, -1f) + f.Up * (lip * f.LeftWallMultiplier) - origin;
            Vector3 lipTopR = TopPoint(f, 1f) + f.Up * (lip * f.RightWallMultiplier) - origin;
            Vector3 lipTopOuterL = lipTopL - f.Right * LipThickness;
            Vector3 lipTopOuterR = lipTopR + f.Right * LipThickness;
            Vector3 botOuterL = f.Position - f.Right * (halfW + LipThickness) - f.Up * ColliderThickness - origin;
            Vector3 botOuterR = f.Position + f.Right * (halfW + LipThickness) - f.Up * ColliderThickness - origin;

            var boundary = new List<Vector3> { botOuterL, lipTopOuterL, lipTopL };
            for (int i = 0; i < n; i++)
                boundary.Add(TopPoint(f, _profile.ProfileXAt(i, n)) - origin);
            boundary.Add(lipTopR);
            boundary.Add(lipTopOuterR);
            boundary.Add(botOuterR);

            Vector3 center = (botOuterL + botOuterR) * 0.5f;
            int c = vertices.Count;
            vertices.Add(center);
            int b0 = vertices.Count;
            vertices.AddRange(boundary);

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

        // ──────────────────────────── Render chunks + LODs ────────────────────────────

        // Render chunk size in rings (~300-500 m of road). Small enough that LODGroup
        // screen-height selection actually engages; large enough to keep draw calls low.
        private const int RingsPerRenderChunk = 160;

        // LOD ring strides and screen-height thresholds. LOD1 = quarter density, LOD2 =
        // ~8% density and casts no shadows — far track costs almost nothing.
        private static readonly int[] LodRingStrides = { 1, 4, 12 };
        private static readonly float[] LodScreenHeights = { 0.18f, 0.035f, 0f };

        /// <summary>
        /// Builds one chain as fixed-arc render chunks, each with a 3-level LODGroup.
        /// Vertex data and SMOOTH NORMALS are computed once for the whole chain
        /// (wrap-aware for circular laps), then sliced per chunk/LOD — a lighting seam
        /// is impossible at any chunk, section or weld boundary because both sides use
        /// the identical accumulated normal.
        /// </summary>
        private void BuildRenderChain(List<GeneratedTrackSection> chain, int index,
            Material roadMaterial, Material sideMaterial, Transform root)
        {
            var frames = ChainFrames(chain, out bool circular);
            if (frames.Count < 2) return;

            int n = TopPointCount;
            int vpr = VertsPerRing;
            int ringCount = frames.Count;
            Vector3 chainOrigin = frames[0].Position;

            // ── Chain-wide vertex data ──
            var positions = new Vector3[ringCount * vpr];
            var uvs = new Vector2[ringCount * vpr];
            for (int r = 0; r < ringCount; r++)
                EmitRenderRing(frames[r], chainOrigin, r * vpr, positions, uvs);

            // ── Chain-wide accumulated normals (area-weighted, wrap-aware) ──
            var normals = new Vector3[ringCount * vpr];
            int ringPairs = circular ? ringCount : ringCount - 1;
            for (int r = 0; r < ringPairs; r++)
            {
                int a = r * vpr;
                int b = ((r + 1) % ringCount) * vpr;

                void Quad(int col)
                {
                    int a0 = a + col, a1 = a + col + 1, b0 = b + col, b1 = b + col + 1;
                    Vector3 face = Vector3.Cross(positions[b0] - positions[a0], positions[b1] - positions[a0]);
                    normals[a0] += face; normals[b0] += face; normals[b1] += face;
                    face = Vector3.Cross(positions[b1] - positions[a0], positions[a1] - positions[a0]);
                    normals[a0] += face; normals[b1] += face; normals[a1] += face;
                }

                for (int i = 0; i < n - 1; i++) Quad(i);
                for (int s = 0; s < 7; s++) Quad(n + s * 2);
            }
            for (int v = 0; v < normals.Length; v++)
                normals[v] = normals[v].sqrMagnitude > 1e-12f ? normals[v].normalized : Vector3.up;

            // ── Slice into chunks (virtual end index N ≡ ring 0 on circular chains) ──
            var head = chain[0];
            var tail = chain[chain.Count - 1];
            int lastRing = circular ? ringCount : ringCount - 1;
            int chunkCount = Mathf.CeilToInt(lastRing / (float)RingsPerRenderChunk);
            for (int c = 0; c < chunkCount; c++)
            {
                int r0 = c * RingsPerRenderChunk;
                int r1 = Mathf.Min(lastRing, r0 + RingsPerRenderChunk);
                bool capHead = c == 0 && !circular && head.CapStart && !head.OpenStart;
                bool capTail = c == chunkCount - 1 && !circular && tail.CapEnd && !tail.OpenEnd;
                BuildRenderChunk(frames, positions, uvs, normals, chainOrigin, r0, r1,
                    $"Track_{index:D2}_{c:D2}", roadMaterial, sideMaterial, root, capHead, capTail);
            }
        }

        private void BuildRenderChunk(List<TrackConnectionFrame> frames, Vector3[] positions, Vector2[] uvs,
            Vector3[] normals, Vector3 chainOrigin, int r0, int r1, string name,
            Material roadMaterial, Material sideMaterial, Transform root, bool capHead, bool capTail)
        {
            int n = TopPointCount;
            int vpr = VertsPerRing;
            int ringCount = frames.Count;
            if (r1 - r0 < 1) return;

            Vector3 chunkOrigin = frames[r0 % ringCount].Position;
            Vector3 delta = chainOrigin - chunkOrigin;

            var chunkObj = new GameObject(name);
            chunkObj.transform.SetParent(root, false);
            chunkObj.transform.localPosition = chunkOrigin;

            var lods = new LOD[LodRingStrides.Length];
            for (int lod = 0; lod < LodRingStrides.Length; lod++)
            {
                // Ring index list: stride through the range, ALWAYS including both
                // boundary rings so adjacent chunks stay watertight at every LOD.
                var ringIdx = new List<int>();
                for (int r = r0; r < r1; r += LodRingStrides[lod]) ringIdx.Add(r);
                ringIdx.Add(r1);

                var verts = new List<Vector3>(ringIdx.Count * vpr + 64);
                var uv = new List<Vector2>(ringIdx.Count * vpr + 64);
                var norm = new List<Vector3>(ringIdx.Count * vpr + 64);
                foreach (int r in ringIdx)
                {
                    int baseIdx = (r % ringCount) * vpr;
                    for (int v = 0; v < vpr; v++)
                    {
                        verts.Add(positions[baseIdx + v] + delta);
                        uv.Add(uvs[baseIdx + v]);
                        norm.Add(normals[baseIdx + v]);
                    }
                }

                var roadTris = new List<int>((ringIdx.Count - 1) * (n - 1) * 6);
                var sideTris = new List<int>((ringIdx.Count - 1) * 42 + 64);
                for (int r = 0; r < ringIdx.Count - 1; r++)
                {
                    int a = r * vpr;
                    int b = (r + 1) * vpr;
                    for (int i = 0; i < n - 1; i++)
                    {
                        roadTris.Add(a + i); roadTris.Add(b + i); roadTris.Add(b + i + 1);
                        roadTris.Add(a + i); roadTris.Add(b + i + 1); roadTris.Add(a + i + 1);
                    }
                    for (int s = 0; s < 7; s++)
                    {
                        int p = n + s * 2;
                        sideTris.Add(a + p); sideTris.Add(b + p); sideTris.Add(b + p + 1);
                        sideTris.Add(a + p); sideTris.Add(b + p + 1); sideTris.Add(a + p + 1);
                    }
                }

                if (capHead)
                {
                    AddCap(verts, uv, sideTris, frames[r0], chunkOrigin, facingForward: false);
                    while (norm.Count < verts.Count) norm.Add(-frames[r0].Forward);
                }
                if (capTail)
                {
                    AddCap(verts, uv, sideTris, frames[r1 % ringCount], chunkOrigin, facingForward: true);
                    while (norm.Count < verts.Count) norm.Add(frames[r1 % ringCount].Forward);
                }

                var mesh = new UnityEngine.Mesh { name = $"{name}_LOD{lod}" };
                if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uv);
                mesh.SetNormals(norm);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(roadTris, 0);
                mesh.SetTriangles(sideTris, 1);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();

                var lodObj = new GameObject($"LOD{lod}");
                lodObj.transform.SetParent(chunkObj.transform, false);
                var filter = lodObj.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = lodObj.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { roadMaterial, sideMaterial };
                if (lod == LodRingStrides.Length - 1)
                    renderer.shadowCastingMode = ShadowCastingMode.Off; // far track: no shadow passes

                lods[lod] = new LOD(LodScreenHeights[lod], new Renderer[] { renderer });
            }

            var group = chunkObj.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
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
        /// Per-side wall multipliers (split/merge open throats) scale each side's rise:
        /// a suppressed inner wall flattens smoothly to the road baseline.
        /// </summary>
        private Vector3 TopPoint(TrackConnectionFrame f, float normalizedX)
        {
            float halfW = f.Width * 0.5f;
            float sideMult = normalizedX < 0f ? f.LeftWallMultiplier : f.RightWallMultiplier;
            float h = _profile.HeightAt(normalizedX, halfW, SideHeightFor(f)) * sideMult;
            return f.Position + f.Right * (normalizedX * halfW) + f.Up * h;
        }

        private float EdgeHeight(TrackConnectionFrame f)
            => _profile.HeightAt(1f, f.Width * 0.5f, SideHeightFor(f));

        private float LipHeight => Mathf.Max(0.05f, _profile.SafetyLipHeight);

        // ──────────────────────────── Section mesh ────────────────────────────

        /// <summary>
        /// One render ring written into the chain-wide arrays at <paramref name="baseIdx"/>.
        /// Layout per ring (base index r*vpr):
        ///  0..n-1 : drivable half-pipe top profile, left → right (SMOOTH, road submesh)
        ///  n+0/1  : left inner lip strip   (lipTopL, edgeTopL)         faces +Right
        ///  n+2/3  : left lip cap strip     (lipTopOuterL, lipTopL)     faces +Up
        ///  n+4/5  : left outer wall strip  (botOuterL, lipTopOuterL)   faces -Right
        ///  n+6/7  : right inner lip strip  (edgeTopR, lipTopR)         faces -Right
        ///  n+8/9  : right lip cap strip    (lipTopR, lipTopOuterR)     faces +Up
        ///  n+10/11: right outer wall strip (lipTopOuterR, botOuterR)   faces +Right
        ///  n+12/13: bottom strip           (botOuterR, botOuterL)      faces -Up
        /// Safety lips scale with the per-side wall multiplier: a suppressed inner wall
        /// (split/merge throat) has no lip fin poking out of the open surface.
        /// </summary>
        private void EmitRenderRing(TrackConnectionFrame f, Vector3 origin, int baseIdx,
            Vector3[] positions, Vector2[] uvs)
        {
            int n = TopPointCount;
            float halfW = f.Width * 0.5f;
            float lip = LipHeight;
            float v = f.ArcLength / TileLength;
            int w = baseIdx;

            for (int i = 0; i < n; i++)
            {
                float xNorm = _profile.ProfileXAt(i, n);
                positions[w] = TopPoint(f, xNorm) - origin;
                uvs[w] = new Vector2((xNorm + 1f) * 0.5f, v);
                w++;
            }

            Vector3 edgeTopL = TopPoint(f, -1f) - origin;
            Vector3 edgeTopR = TopPoint(f, 1f) - origin;
            Vector3 lipTopL = edgeTopL + f.Up * (lip * f.LeftWallMultiplier);
            Vector3 lipTopR = edgeTopR + f.Up * (lip * f.RightWallMultiplier);
            Vector3 lipTopOuterL = lipTopL - f.Right * LipThickness;
            Vector3 lipTopOuterR = lipTopR + f.Right * LipThickness;
            Vector3 botOuterL = f.Position - f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;
            Vector3 botOuterR = f.Position + f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;

            positions[w] = lipTopL; uvs[w] = new Vector2(v, 1f); w++;
            positions[w] = edgeTopL; uvs[w] = new Vector2(v, 0f); w++;
            positions[w] = lipTopOuterL; uvs[w] = new Vector2(v, 0f); w++;
            positions[w] = lipTopL; uvs[w] = new Vector2(v, 1f); w++;
            positions[w] = botOuterL; uvs[w] = new Vector2(v, 0f); w++;
            positions[w] = lipTopOuterL; uvs[w] = new Vector2(v, 1f); w++;
            positions[w] = edgeTopR; uvs[w] = new Vector2(v, 0f); w++;
            positions[w] = lipTopR; uvs[w] = new Vector2(v, 1f); w++;
            positions[w] = lipTopR; uvs[w] = new Vector2(v, 1f); w++;
            positions[w] = lipTopOuterR; uvs[w] = new Vector2(v, 0f); w++;
            positions[w] = lipTopOuterR; uvs[w] = new Vector2(v, 1f); w++;
            positions[w] = botOuterR; uvs[w] = new Vector2(v, 0f); w++;
            positions[w] = botOuterR; uvs[w] = new Vector2(0f, v); w++;
            positions[w] = botOuterL; uvs[w] = new Vector2(1f, v);
        }

        /// <summary>
        /// Cap closing the exposed cross-section of a boundary. Fans over the full
        /// half-pipe boundary from the bottom center, which sees every boundary point.
        /// NEVER generated on open boundaries (jump lip / landing mouth / air-gap edges) —
        /// those must stay clear of any geometry across the flight path. Driven purely by
        /// the section's CapStart/CapEnd metadata.
        /// </summary>
        private void AddCap(List<Vector3> vertices, List<Vector2> uvs, List<int> tris, TrackConnectionFrame f, Vector3 origin, bool facingForward)
        {
            int n = TopPointCount;
            float halfW = f.Width * 0.5f;
            float lip = LipHeight;

            Vector3 lipTopL = TopPoint(f, -1f) + f.Up * (lip * f.LeftWallMultiplier) - origin;
            Vector3 lipTopR = TopPoint(f, 1f) + f.Up * (lip * f.RightWallMultiplier) - origin;
            Vector3 lipTopOuterL = lipTopL - f.Right * LipThickness;
            Vector3 lipTopOuterR = lipTopR + f.Right * LipThickness;
            Vector3 botOuterL = f.Position - f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;
            Vector3 botOuterR = f.Position + f.Right * (halfW + LipThickness) - f.Up * RoadThickness - origin;

            // Boundary chain: bottom-left, up the left wall, across the drivable profile,
            // up/over the right lip, down to bottom-right.
            var boundary = new List<Vector3> { botOuterL, lipTopOuterL, lipTopL };
            for (int i = 0; i < n; i++)
                boundary.Add(TopPoint(f, _profile.ProfileXAt(i, n)) - origin);
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
