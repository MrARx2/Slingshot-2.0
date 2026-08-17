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
    /// UNIFIED PARAMETRIC CROSS-SECTION: every ring evaluates ONE ordered 2D chain
    /// (<see cref="TrackCrossSection"/>) that continuously expresses the plain
    /// half-pipe, dynamically rounded turn bowls, outside catch walls curling past
    /// vertical, wallride support and full pipe closure. The ring is that chain
    /// (drivable inner surface) plus an outer shell offset along the chain's outward
    /// normals — so overhangs and closed pipes are real solids, and the collider
    /// evaluates the SAME chain (render and collision cannot disagree).
    ///
    /// JUNCTION / AIR-GAP OPENINGS: per-frame wall multipliers (set by the layout's
    /// wall-mask pass) suppress the INNER walls through split/merge throats so forked
    /// half-pipes never cross through each other, and air-gap boundaries (jump lip,
    /// landing mouth) are OPEN edges — no caps or geometry across the flight path.
    ///
    /// STRUCTURE: render meshes are fixed-arc CHUNKS with LODGroups (per-section
    /// meshes are km-long, so screen-height LOD never engages on them); collision is
    /// continuous chain colliders chunked under the PhysX 2^21 triangle limit. Both
    /// slice one shared ring stream per chain, so every boundary ring is bit-exact on
    /// both sides and chunks join with zero gaps and zero lighting seams.
    /// </summary>
    public class BoxPrismTrackMeshBuilder
    {
        // Road shell thickness in meters (drivable inner surface to outer skin).
        private const float RoadThickness = 1.2f;

        // Collision shell thickness (collider only, thicker than the visual shell): at
        // 420 m/s a heavy frame can step the craft body more than a thin shell in one
        // discrete physics tick — the deeper collision volume catches it instead of
        // letting it tunnel through the road.
        private const float ColliderThickness = 5f;

        // UV tiling length along the track in meters.
        private const float TileLength = 10f;

        // Cross-section resolution used for COLLISION only. Collision does not need
        // visual fidelity: it needs the right shape to within far less than the craft's
        // ride height. Sampling the SAME parametric chain at fewer points yields an
        // inscribed polyline whose worst deviation is the arc sagitta — at this
        // resolution roughly 8 cm on a ~100 m wall radius, and smaller still on the
        // walls themselves because the profile's warped distribution already
        // concentrates samples where curvature is highest.
        //
        // This matters far more than it looks: collider triangles scale linearly with
        // it, and PhysX must RE-COOK every one of them whenever Unity restores the
        // scene (which it does on every Play Mode exit). At render resolution the
        // track cooks ~8.7 M triangles per restore; the old fixed 10 cut it to ~3 M —
        // but that made the floor→wall shoulder a ~35° collider 'cliff' the hover nodes
        // feel (measured). It is now designer-tunable via
        // TrackRoadProfileSettings.ColliderProfileResolution (default 40), still capped
        // by the render resolution and never finer than it.

        private readonly TrackRoadProfileSettings _profile;
        private readonly TrackRoadProfileSettings _colliderProfile;
        private bool _warnedMissingProfile;

        // Per-ring scratch buffers (builder is single-threaded).
        private Vector2[] _pts;
        private float[] _cross;
        private Vector2[] _outward;

        // Separate buffers for the coarser collision chain.
        private Vector2[] _colPts;
        private Vector2[] _colOutward;

        public BoxPrismTrackMeshBuilder() : this(null) { }

        public BoxPrismTrackMeshBuilder(TrackRoadProfileSettings profile)
        {
            _profile = profile ?? new TrackRoadProfileSettings();

            // Same shape, fewer samples. Never coarser than the render profile, so a
            // deliberately low-resolution road cannot end up with a FINER collider.
            _colliderProfile = new TrackRoadProfileSettings
            {
                Shape = _profile.Shape,
                SideHeight = _profile.SideHeight,
                WallCurve01 = _profile.WallCurve01,
                CenterFlatWidthRatio = _profile.CenterFlatWidthRatio,
                SafetyLipHeight = _profile.SafetyLipHeight,
                MinTurnCenterFlatRatio = _profile.MinTurnCenterFlatRatio,
                MaxOverhangAngleDeg = _profile.MaxOverhangAngleDeg,
                OverhangRadius = _profile.OverhangRadius,
                ProfileResolution = Mathf.Min(Mathf.Max(8, _profile.ColliderProfileResolution), _profile.ProfileResolution)
            };

            int n = InnerPointCount;
            _pts = new Vector2[n];
            _cross = new float[n];
            _outward = new Vector2[n];

            int cn = ColliderPointCount;
            _colPts = new Vector2[cn];
            _colOutward = new Vector2[cn];
        }

        /// <summary>
        /// Builds the track as CHAIN-CHUNKED render meshes with LODs plus continuous
        /// chain colliders. AirGap sections emit nothing (they ARE the hole).
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

        // ──────────────────────────── Ring cross-section ────────────────────────────

        /// <summary>Points of the drivable inner chain (parametric — includes wall extensions).</summary>
        private int InnerPointCount => TrackCrossSection.PointCount(_profile);

        /// <summary>Render verts per ring: inner chain + offset outer shell + 4 duplicated tip-cap verts.</summary>
        private int VertsPerRing => InnerPointCount * 2 + 4;

        /// <summary>Points of the COARSER collision chain (same shape, fewer samples).</summary>
        private int ColliderPointCount => TrackCrossSection.PointCount(_colliderProfile);

        /// <summary>Collider verts per ring: inner chain + outer shell (no hard-edge duplicates needed).</summary>
        private int ColliderVertsPerRing => ColliderPointCount * 2;

        /// <summary>
        /// Evaluates the parametric chain and its per-point OUTWARD 2D normals into the
        /// scratch buffers. Outward is perpendicular to the local chain tangent, away
        /// from the road interior — the outer shell offsets along it, so overhangs and
        /// closed pipes become real solids without self-intersection.
        /// </summary>
        private void EvaluateRing(in TrackConnectionFrame f)
            => EvaluateChain(_profile, f, _pts, _cross, _outward);

        /// <summary>Same evaluation against the coarser collision profile.</summary>
        private void EvaluateColliderRing(in TrackConnectionFrame f)
            => EvaluateChain(_colliderProfile, f, _colPts, null, _colOutward);

        private static void EvaluateChain(TrackRoadProfileSettings profile, in TrackConnectionFrame f,
            Vector2[] points, float[] crossParams, Vector2[] outward)
        {
            int n = TrackCrossSection.PointCount(profile);
            TrackCrossSection.Evaluate(profile, f, points, crossParams);

            Vector2 lastOutward = new Vector2(0f, -1f);
            for (int k = 0; k < n; k++)
            {
                Vector2 t = points[Mathf.Min(k + 1, n - 1)] - points[Mathf.Max(k - 1, 0)];
                if (t.sqrMagnitude < 1e-10f)
                {
                    outward[k] = lastOutward; // degenerate (collapsed extension) — carry on
                    continue;
                }
                t.Normalize();
                lastOutward = new Vector2(t.y, -t.x); // interior is on the LEFT of the chain direction
                outward[k] = lastOutward;
            }
        }

        // ──────────────────────────── Chain colliders ────────────────────────────

        // PhysX's BVH34 midphase is broken above 2,097,152 (2^21) triangles per mesh —
        // Unity warns and collisions can be MISSED (the craft falls through the road).
        // Size chunks from the active cross-section resolution instead of assuming
        // a fixed number of rings: a 241-point half-pipe emits 964 triangles for
        // every longitudinal ring span, so the old 2,000-span chunk sat near the
        // failure threshold. Staying below one million also satisfies the integrity
        // test's "less than half the PhysX limit" margin.
        private const int MaxTrianglesPerColliderChunk = 1000000;

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
            int trianglesPerRingSpan = Mathf.Max(1, 4 * ColliderPointCount);
            int maxRingSpansPerChunk = Mathf.Max(1, MaxTrianglesPerColliderChunk / trianglesPerRingSpan);
            int chunkCount = Mathf.CeilToInt(lastRing / (float)maxRingSpansPerChunk);
            for (int c = 0; c < chunkCount; c++)
            {
                int r0 = c * maxRingSpansPerChunk;
                int r1 = Mathf.Min(lastRing, r0 + maxRingSpansPerChunk);
                bool capHead = c == 0 && !circular && head.CapStart && !head.OpenStart;
                bool capTail = c == chunkCount - 1 && !circular && tail.CapEnd && !tail.OpenEnd;
                BuildColliderChunk(frames, r0, r1, $"TrackCollider_{index:D2}_{c:D2}", root, capHead, capTail);
            }
        }

        private void BuildColliderChunk(List<TrackConnectionFrame> frames, int r0, int r1,
            string name, Transform root, bool capHead, bool capTail)
        {
            int n = ColliderPointCount;
            int vpr = ColliderVertsPerRing;
            int rings = r1 - r0 + 1;
            if (rings < 2) return;

            Vector3 origin = frames[r0 % frames.Count].Position;
            var vertices = new List<Vector3>(rings * vpr + n * 2 + 8);
            for (int r = r0; r <= r1; r++)
            {
                var f = frames[r % frames.Count];
                EvaluateColliderRing(f);
                for (int k = 0; k < n; k++)
                    vertices.Add(f.Position + f.Right * _colPts[k].x + f.Up * _colPts[k].y - origin);
                for (int k = 0; k < n; k++)
                {
                    Vector2 o = _colPts[k] + _colOutward[k] * ColliderThickness;
                    vertices.Add(f.Position + f.Right * o.x + f.Up * o.y - origin);
                }
            }

            var tris = new List<int>((rings - 1) * n * 12);
            for (int r = 0; r < rings - 1; r++)
            {
                int a = r * vpr;
                int b = (r + 1) * vpr;

                for (int i = 0; i < n - 1; i++)
                {
                    // Inner drivable surface (faces the interior).
                    tris.Add(a + i); tris.Add(b + i); tris.Add(b + i + 1);
                    tris.Add(a + i); tris.Add(b + i + 1); tris.Add(a + i + 1);
                    // Outer shell (faces away — reversed winding).
                    tris.Add(a + n + i); tris.Add(b + n + i + 1); tris.Add(b + n + i);
                    tris.Add(a + n + i); tris.Add(a + n + i + 1); tris.Add(b + n + i + 1);
                }

                // Tip edge strips sealing the shell (left tip: inner0↔outer0; right tip).
                tris.Add(a); tris.Add(a + n); tris.Add(b + n);
                tris.Add(a); tris.Add(b + n); tris.Add(b);
                tris.Add(a + n - 1); tris.Add(b + n - 1); tris.Add(b + 2 * n - 1);
                tris.Add(a + n - 1); tris.Add(b + 2 * n - 1); tris.Add(a + 2 * n - 1);
            }

            if (capHead) AddColliderCap(vertices, tris, frames[r0 % frames.Count], origin, facingForward: false);
            if (capTail) AddColliderCap(vertices, tris, frames[r1 % frames.Count], origin, facingForward: true);

            var mesh = new UnityEngine.Mesh { name = name };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            obj.transform.localPosition = origin;
            // Drivable-surface layer so hover/surface probes can filter to real
            // track only (Rev1.1 probe continuity: no Everything-mask probing).
            obj.layer = TrackSurfacePhysics.SurfaceLayer;
            var collider = obj.AddComponent<MeshCollider>();
            // Do not use BVH34/Fast Midphase for generated racing surfaces. If an
            // unusually dense profile ever slips past the chunk budget, the safer
            // midphase still gives correct contacts instead of silently missing them.
            // Cooking options must be assigned before sharedMesh triggers cooking.
            collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                    | MeshColliderCookingOptions.EnableMeshCleaning
                                    | MeshColliderCookingOptions.WeldColocatedVertices;
            collider.sharedMesh = mesh;
            // Track-wide surface material: Minimum combine lets the craft's
            // low-friction hull win at contacts (no violent wall grabs).
            collider.sharedMaterial = TrackSurfacePhysics.Surface;
        }

        /// <summary>Cap closing an exposed boundary: an annulus between the inner chain and the outer shell.</summary>
        private void AddColliderCap(List<Vector3> vertices, List<int> tris, TrackConnectionFrame f, Vector3 origin, bool facingForward)
        {
            int n = ColliderPointCount;
            EvaluateColliderRing(f);

            int b0 = vertices.Count;
            for (int k = 0; k < n; k++)
                vertices.Add(f.Position + f.Right * _colPts[k].x + f.Up * _colPts[k].y - origin);
            for (int k = 0; k < n; k++)
            {
                Vector2 o = _colPts[k] + _colOutward[k] * ColliderThickness;
                vertices.Add(f.Position + f.Right * o.x + f.Up * o.y - origin);
            }

            for (int k = 0; k < n - 1; k++)
            {
                if (facingForward)
                {
                    tris.Add(b0 + k); tris.Add(b0 + k + 1); tris.Add(b0 + n + k + 1);
                    tris.Add(b0 + k); tris.Add(b0 + n + k + 1); tris.Add(b0 + n + k);
                }
                else
                {
                    tris.Add(b0 + k); tris.Add(b0 + n + k + 1); tris.Add(b0 + k + 1);
                    tris.Add(b0 + k); tris.Add(b0 + n + k); tris.Add(b0 + n + k + 1);
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

            int n = InnerPointCount;
            int vpr = VertsPerRing;
            int ringCount = frames.Count;
            Vector3 chainOrigin = frames[0].Position;

            // ── Chain-wide vertex data ──
            var positions = new Vector3[ringCount * vpr];
            var uvs = new Vector2[ringCount * vpr];
            var colors = new Color[ringCount * vpr];
            for (int r = 0; r < ringCount; r++)
                EmitRenderRing(frames[r], chainOrigin, r * vpr, positions, uvs, colors);

            // ── Chain-wide accumulated normals (area-weighted, wrap-aware) ──
            var normals = new Vector3[ringCount * vpr];
            int ringPairs = circular ? ringCount : ringCount - 1;
            for (int r = 0; r < ringPairs; r++)
            {
                int a = r * vpr;
                int b = ((r + 1) % ringCount) * vpr;

                void Quad(int col, bool reversed)
                {
                    int a0 = a + col, a1 = a + col + 1, b0 = b + col, b1 = b + col + 1;
                    Vector3 face = Vector3.Cross(positions[b0] - positions[a0], positions[b1] - positions[a0]);
                    if (reversed) face = -face;
                    normals[a0] += face; normals[b0] += face; normals[b1] += face;
                    face = Vector3.Cross(positions[b1] - positions[a0], positions[a1] - positions[a0]);
                    if (reversed) face = -face;
                    normals[a0] += face; normals[b1] += face; normals[a1] += face;
                }

                for (int i = 0; i < n - 1; i++) Quad(i, false);          // inner surface
                for (int i = 0; i < n - 1; i++) Quad(n + i, true);       // outer shell
                Quad(2 * n, false);                                       // left tip cap
                Quad(2 * n + 2, false);                                   // right tip cap
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
                BuildRenderChunk(frames, positions, uvs, colors, normals, chainOrigin, r0, r1,
                    $"Track_{index:D2}_{c:D2}", roadMaterial, sideMaterial, root, capHead, capTail);
            }
        }

        private void BuildRenderChunk(List<TrackConnectionFrame> frames, Vector3[] positions, Vector2[] uvs,
            Color[] colors, Vector3[] normals, Vector3 chainOrigin, int r0, int r1, string name,
            Material roadMaterial, Material sideMaterial, Transform root, bool capHead, bool capTail)
        {
            int n = InnerPointCount;
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

                var verts = new List<Vector3>(ringIdx.Count * vpr + n * 2 + 8);
                var uv = new List<Vector2>(ringIdx.Count * vpr + n * 2 + 8);
                var col = new List<Color>(ringIdx.Count * vpr + n * 2 + 8);
                var norm = new List<Vector3>(ringIdx.Count * vpr + n * 2 + 8);
                foreach (int r in ringIdx)
                {
                    int baseIdx = (r % ringCount) * vpr;
                    for (int v = 0; v < vpr; v++)
                    {
                        verts.Add(positions[baseIdx + v] + delta);
                        uv.Add(uvs[baseIdx + v]);
                        col.Add(colors[baseIdx + v]);
                        norm.Add(normals[baseIdx + v]);
                    }
                }

                var roadTris = new List<int>((ringIdx.Count - 1) * (n - 1) * 6);
                var sideTris = new List<int>((ringIdx.Count - 1) * (n + 1) * 6 + 64);
                for (int r = 0; r < ringIdx.Count - 1; r++)
                {
                    int a = r * vpr;
                    int b = (r + 1) * vpr;
                    for (int i = 0; i < n - 1; i++)
                    {
                        // Inner drivable surface.
                        roadTris.Add(a + i); roadTris.Add(b + i); roadTris.Add(b + i + 1);
                        roadTris.Add(a + i); roadTris.Add(b + i + 1); roadTris.Add(a + i + 1);
                        // Outer shell (reversed winding — faces away from the road).
                        sideTris.Add(a + n + i); sideTris.Add(b + n + i + 1); sideTris.Add(b + n + i);
                        sideTris.Add(a + n + i); sideTris.Add(a + n + i + 1); sideTris.Add(b + n + i + 1);
                    }
                    // Tip cap strips (duplicated verts — crisp edges).
                    for (int s = 0; s < 2; s++)
                    {
                        int p = 2 * n + s * 2;
                        sideTris.Add(a + p); sideTris.Add(b + p); sideTris.Add(b + p + 1);
                        sideTris.Add(a + p); sideTris.Add(b + p + 1); sideTris.Add(a + p + 1);
                    }
                }

                if (capHead)
                {
                    AddCap(verts, uv, col, sideTris, frames[r0 % ringCount], chunkOrigin, facingForward: false);
                    while (norm.Count < verts.Count) norm.Add(-frames[r0 % ringCount].Forward);
                }
                if (capTail)
                {
                    AddCap(verts, uv, col, sideTris, frames[r1 % ringCount], chunkOrigin, facingForward: true);
                    while (norm.Count < verts.Count) norm.Add(frames[r1 % ringCount].Forward);
                }

                var mesh = new UnityEngine.Mesh { name = $"{name}_LOD{lod}" };
                if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uv);
                mesh.SetColors(col);
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

        /// <summary>
        /// One render ring written into the chain-wide arrays at <paramref name="baseIdx"/>.
        /// Layout per ring (base index r*vpr):
        ///  0..n-1     : drivable parametric chain, left tip → right tip (road submesh)
        ///  n..2n-1    : outer shell, inner chain offset outward (side submesh)
        ///  2n / 2n+1  : left tip cap strip (outer tip, inner tip — duplicated, crisp edge)
        ///  2n+2 / 2n+3: right tip cap strip (inner tip, outer tip)
        /// Vertex colors carry guidance data for the road shaders:
        ///  R = signed cross parameter remapped to 0..1 (0.5 = center, ±0.25 offset = flat boundary)
        ///  G = height above the floor baseline, normalized by road width
        ///  B = pipe closure blend
        /// </summary>
        private void EmitRenderRing(in TrackConnectionFrame f, Vector3 origin, int baseIdx,
            Vector3[] positions, Vector2[] uvs, Color[] colors)
        {
            int n = InnerPointCount;
            float v = f.ArcLength / TileLength;
            int w = baseIdx;

            EvaluateRing(f);

            for (int k = 0; k < n; k++)
            {
                positions[w + k] = f.Position + f.Right * _pts[k].x + f.Up * _pts[k].y - origin;
                uvs[w + k] = new Vector2(k / (float)(n - 1), v);
                colors[w + k] = RingColor(f, k);
            }
            for (int k = 0; k < n; k++)
            {
                Vector2 o = _pts[k] + _outward[k] * RoadThickness;
                positions[w + n + k] = f.Position + f.Right * o.x + f.Up * o.y - origin;
                uvs[w + n + k] = new Vector2(k / (float)(n - 1), v);
                colors[w + n + k] = RingColor(f, k);
            }

            // Duplicated tip verts for the crisp cap strips.
            positions[w + 2 * n] = positions[w + n];         // left outer tip
            positions[w + 2 * n + 1] = positions[w];         // left inner tip
            positions[w + 2 * n + 2] = positions[w + n - 1]; // right inner tip
            positions[w + 2 * n + 3] = positions[w + 2 * n - 1]; // right outer tip
            for (int d = 0; d < 4; d++)
            {
                uvs[w + 2 * n + d] = new Vector2(d < 2 ? 0f : 1f, v);
                colors[w + 2 * n + d] = RingColor(f, d < 2 ? 0 : n - 1);
            }
        }

        private Color RingColor(in TrackConnectionFrame f, int k)
        {
            float heightFrac = Mathf.Clamp01(_pts[k].y / Mathf.Max(1f, f.Width));
            return new Color(
                Mathf.Clamp01((_cross[k] + 2f) * 0.25f),
                heightFrac,
                Mathf.Clamp01(f.PipeClosure),
                1f);
        }

        /// <summary>
        /// Cap closing the exposed cross-section of a boundary: an annulus between the
        /// inner chain and the outer shell. NEVER generated on open boundaries (jump
        /// lip / landing mouth / air-gap edges) — those must stay clear of any geometry
        /// across the flight path. Driven purely by the section's CapStart/CapEnd metadata.
        /// </summary>
        private void AddCap(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors,
            List<int> tris, in TrackConnectionFrame f, Vector3 origin, bool facingForward)
        {
            int n = InnerPointCount;
            EvaluateRing(f);

            int b0 = vertices.Count;
            for (int k = 0; k < n; k++)
            {
                vertices.Add(f.Position + f.Right * _pts[k].x + f.Up * _pts[k].y - origin);
                uvs.Add(new Vector2(k / (float)(n - 1), 0f));
                colors.Add(RingColor(f, k));
            }
            for (int k = 0; k < n; k++)
            {
                Vector2 o = _pts[k] + _outward[k] * RoadThickness;
                vertices.Add(f.Position + f.Right * o.x + f.Up * o.y - origin);
                uvs.Add(new Vector2(k / (float)(n - 1), 1f));
                colors.Add(RingColor(f, k));
            }

            for (int k = 0; k < n - 1; k++)
            {
                if (facingForward)
                {
                    tris.Add(b0 + k); tris.Add(b0 + k + 1); tris.Add(b0 + n + k + 1);
                    tris.Add(b0 + k); tris.Add(b0 + n + k + 1); tris.Add(b0 + n + k);
                }
                else
                {
                    tris.Add(b0 + k); tris.Add(b0 + n + k + 1); tris.Add(b0 + k + 1);
                    tris.Add(b0 + k); tris.Add(b0 + n + k); tris.Add(b0 + n + k + 1);
                }
            }
        }
    }
}
