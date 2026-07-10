using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;
using TrackGeneration.Core;
using TrackGeneration.Splines;

namespace TrackGeneration.Mesh
{
    /// <summary>
    /// Converts splines into renderable and collidable track meshes.
    /// Plain C# class (not a MonoBehaviour).
    /// </summary>
    public class TrackMeshBuilder
    {
        // Shortcut junction tuning (shared so the mesh width-gore and the weld stay in lockstep).
        // MinGoreWidth: fraction of full shortcut width the tip keeps instead of pinching to 0 (Phase 2).
        // EdgeOverlap:  metres the welded inner edge runs ONTO the main road so the two separate
        //               meshes overlap instead of meeting at a knife-edge (kills the T-junction crack).
        // OverlapLift:  metres the overlapped strip sits above the road surface (avoids z-fighting).
        private const float MinGoreWidth = 0.3f;
        private const float EdgeOverlap = 0.6f;
        private const float OverlapLift = 0.08f;

        /// <summary>
        /// Main entry point to build a track mesh from a spline container.
        /// </summary>
        public GameObject BuildTrackMesh(SplineContainer splineContainer, float roadWidth, TrackConfig config, Material roadMaterial, Material wallMaterial, Transform parent, string meshName, List<TrackBranch> shortcuts = null, TrackBranch selfBranch = null, SplineContainer mainCircuit = null)
        {
            if (splineContainer == null || splineContainer.Splines.Count == 0)
            {
                Debug.LogError("[TrackMeshBuilder] SplineContainer is null or empty.");
                return null;
            }

            // Step a: Compute adaptive subdivision count based on track length
            float trackLength = SplineUtilities.GetSplineLength(splineContainer);
            int adaptiveSubdivisions = Mathf.Max(config.SplineSubdivisions, Mathf.CeilToInt(trackLength / config.MeshSegmentLength));

            SplineFrame[] frames = SplineUtilities.SampleEvenFrames(splineContainer, adaptiveSubdivisions, config.BankingMultiplier, config.MaxBankAngle, selfBranch?.StartUp, selfBranch?.EndUp, config.BankTransitionLength);
            if (frames == null || frames.Length < 2)
            {
                Debug.LogError("[TrackMeshBuilder] Failed to sample spline frames.");
                return null;
            }

            // Weld the shortcut's merge-zone frames to the main road's edge so the join is a single
            // aligned surface (no overlapping tubes). Only for shortcuts (selfBranch + mainCircuit).
            if (selfBranch != null && mainCircuit != null)
            {
                WeldShortcutFramesToMainRoad(frames, selfBranch, mainCircuit, config, trackLength);
            }

            // Step c: Create TrackCrossSection
            TrackCrossSection crossSection = new TrackCrossSection
            {
                RoadWidth = roadWidth,
                WallHeight = config.WallHeight,
                HasLaneDivider = roadWidth > 5f // Assuming >5m is multi-lane
            };

            int profileVertCount = crossSection.GetVertexCount();
            int totalVerts = frames.Length * profileVertCount;

            Vector3[] vertices = new Vector3[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];
            Color[] colors = new Color[totalVerts];

            float tileSize = 10f; // Tiling length along the track
            float currentDistance = 0f;
            float taperDist = config.ShortcutMergeDistance;

            // ── Pre-compute per-frame wall-height and road-width scales ──
            // Wall height tapers to 0 across a merge zone so the hovercraft can flow across the
            // opening. In addition, a shortcut's road WIDTH gores to 0 at each end so it grows out
            // of / pinches back into the main-road edge instead of ending in an abrupt flat face.
            float[] leftWallScale = new float[frames.Length];
            float[] rightWallScale = new float[frames.Length];
            float[] roadWidthScale = new float[frames.Length];

            // Resolve each shortcut junction's arc-length position on THIS spline once.
            // junctionDir = which arc direction points INTO the shortcut's span (where the
            // shortcut runs alongside the road): +1 at the peel-off, -1 at the merge.
            List<float> junctionArc = null;
            List<float> junctionSide = null;
            List<float> junctionDir = null;
            if (shortcuts != null && shortcuts.Count > 0)
            {
                junctionArc = new List<float>(shortcuts.Count * 2);
                junctionSide = new List<float>(shortcuts.Count * 2);
                junctionDir = new List<float>(shortcuts.Count * 2);
                foreach (var branch in shortcuts)
                {
                    junctionArc.Add(ArcAtT(frames, branch.MainStartT)); junctionSide.Add(branch.StartSideSign); junctionDir.Add(1f);
                    junctionArc.Add(ArcAtT(frames, branch.MainEndT));   junctionSide.Add(branch.EndSideSign);   junctionDir.Add(-1f);
                }
            }

            for (int i = 0; i < frames.Length; i++)
            {
                float lw = 1f, rw = 1f, ws = 1f;
                float arc = frames[i].ArcLength;

                // Main circuit: open the wall on the side each shortcut peels off / merges to.
                // The opening is ASYMMETRIC: the shortcut runs alongside the road for one merge
                // distance INTO the span (its lead-in / lead-out), so the wall stays fully open
                // there and only ramps back once the shortcut has actually pulled away. A wall
                // popping up between the two adjacent lanes was the strip seen in the junctions.
                if (junctionArc != null)
                {
                    for (int k = 0; k < junctionArc.Count; k++)
                    {
                        float d = arc - junctionArc[k];
                        if (d > trackLength * 0.5f) d -= trackLength;   // wrap-around on the
                        if (d < -trackLength * 0.5f) d += trackLength;  // closed loop
                        float inSpan = d * junctionDir[k];

                        float taper;
                        if (inSpan >= 0f && inSpan <= taperDist)
                            taper = 0f; // shortcut is alongside: fully open
                        else if (inSpan < 0f)
                            taper = math.smoothstep(0f, 1f, -inSpan / (taperDist * 0.5f)); // approach side
                        else
                            taper = math.smoothstep(0f, 1f, (inSpan - taperDist) / (taperDist * 0.75f)); // pulling away

                        if (junctionSide[k] < 0) lw = math.min(lw, taper);
                        else rw = math.min(rw, taper);
                    }
                }

                // Shortcut itself: open its INNER wall (the one facing the main road) at both ends.
                // sideSign > 0 => shortcut sits on the main road's right => its inner edge is its LEFT.
                // The peel-off and merge ends can sit on different sides on a non-convex loop, so each
                // end uses its own side sign.
                if (selfBranch != null)
                {
                    float distEnd = math.min(arc, trackLength - arc);
                    float goreDist = taperDist * 0.5f;

                    // Road width gores toward the edge over a SHORT distance at each end, but keeps a
                    // small MINIMUM width at the tip (Phase 2). A literal zero-width point produced a
                    // fragile curled sliver; a small stub tucks under the road edge instead. This floor
                    // MUST match the ws floor in WeldShortcutFramesToMainRoad so mesh + centerline agree.
                    ws = math.lerp(MinGoreWidth, 1f, math.smoothstep(0f, 1f, distEnd / math.max(goreDist, 0.01f)));

                    // Outer wall HEIGHT must reach a TRUE zero at the tip — tying it to the floored
                    // width left a 30%-height wall fin standing at the merge point. Same ramp, no floor.
                    float wallGore = math.smoothstep(0f, 1f, distEnd / math.max(goreDist, 0.01f));

                    // Inner wall (facing the main road) stays fully open through the whole lead-in
                    // (the shortcut is right beside the road there) and closes over the next 3/4 merge
                    // distance as the shortcut pulls away. Mirrors the main road's asymmetric opening.
                    float innerOpen = math.smoothstep(0f, 1f, (distEnd - taperDist) / (taperDist * 0.75f));
                    float endSide = (arc <= trackLength - arc) ? selfBranch.StartSideSign : selfBranch.EndSideSign;

                    if (endSide > 0f) { lw = math.min(lw, innerOpen); rw = math.min(rw, wallGore); }
                    else              { rw = math.min(rw, innerOpen); lw = math.min(lw, wallGore); }
                }

                leftWallScale[i] = lw;
                rightWallScale[i] = rw;
                roadWidthScale[i] = ws;
            }

            // Build vertices and UVs (road surface always full width).
            for (int i = 0; i < frames.Length; i++)
            {
                SplineFrame frame = frames[i];

                Vector3[] localProfile = crossSection.GetProfile(0f, roadWidthScale[i], roadWidthScale[i], leftWallScale[i], rightWallScale[i]);
                Vector3[] worldProfile = TransformProfile(localProfile, frame);

                if (i > 0)
                {
                    currentDistance += Vector3.Distance(frames[i].Position, frames[i - 1].Position);
                }

                float distNormalized = (float)i / (frames.Length - 1);
                Color vertColor = Color.white;
                if (selfBranch != null)
                {
                    vertColor = Color.Lerp(Color.green, new Color(0.6f, 0.2f, 0.2f), distNormalized);
                }

                for (int j = 0; j < profileVertCount; j++)
                {
                    int vertIndex = i * profileVertCount + j;
                    vertices[vertIndex] = worldProfile[j];

                    // Standard UV mapping
                    float u = (float)j / (profileVertCount - 1);
                    uvs[vertIndex] = new Vector2(u, currentDistance / tileSize);
                    colors[vertIndex] = vertColor;
                }
            }

            // Build index buffer (Triangle Strip Quads)
            // Separate triangles into submeshes: 0 = road, 1 = walls
            List<int> roadIndices = new List<int>();
            List<int> wallIndices = new List<int>();

            // A wall quad is only omitted where it has fully tapered away at BOTH of its rings,
            // leaving a clean opening; everywhere else the (possibly shortened) wall is drawn.
            const float wallOmitThreshold = 0.02f;

            for (int i = 0; i < frames.Length - 1; i++)
            {
                int currentRing = i;
                int nextRing = i + 1;

                bool skipLeftWall = leftWallScale[currentRing] < wallOmitThreshold && leftWallScale[nextRing] < wallOmitThreshold;
                bool skipRightWall = rightWallScale[currentRing] < wallOmitThreshold && rightWallScale[nextRing] < wallOmitThreshold;

                if (!skipLeftWall)
                {
                    // Wall left (outer and inner)
                    AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 0, 1);
                    AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 1, 2);
                }

                if (crossSection.HasLaneDivider)
                {
                    // Road left and right
                    AddQuad(roadIndices, currentRing, nextRing, profileVertCount, 2, 3);
                    AddQuad(roadIndices, currentRing, nextRing, profileVertCount, 3, 4);
                    
                    if (!skipRightWall)
                    {
                        // Wall right
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 4, 5);
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 5, 6);
                    }
                }
                else
                {
                    // Road full
                    AddQuad(roadIndices, currentRing, nextRing, profileVertCount, 2, 3);
                    
                    if (!skipRightWall)
                    {
                        // Wall right
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 3, 4);
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 4, 5);
                    }
                }
            }

            // For closed loop, connect last ring to first ring
            if (splineContainer.Spline.Closed)
            {
                int currentRing = frames.Length - 1;
                int nextRing = 0;

                bool skipLeftWall = leftWallScale[currentRing] < wallOmitThreshold && leftWallScale[nextRing] < wallOmitThreshold;
                bool skipRightWall = rightWallScale[currentRing] < wallOmitThreshold && rightWallScale[nextRing] < wallOmitThreshold;

                if (!skipLeftWall)
                {
                    AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 0, 1);
                    AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 1, 2);
                }

                if (crossSection.HasLaneDivider)
                {
                    AddQuad(roadIndices, currentRing, nextRing, profileVertCount, 2, 3);
                    AddQuad(roadIndices, currentRing, nextRing, profileVertCount, 3, 4);
                    if (!skipRightWall)
                    {
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 4, 5);
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 5, 6);
                    }
                }
                else
                {
                    AddQuad(roadIndices, currentRing, nextRing, profileVertCount, 2, 3);
                    if (!skipRightWall)
                    {
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 3, 4);
                        AddQuad(wallIndices, currentRing, nextRing, profileVertCount, 4, 5);
                    }
                }
            }

            // Create Mesh
            UnityEngine.Mesh trackMesh = new UnityEngine.Mesh();
            trackMesh.name = meshName;
            
            if (totalVerts > 65535)
            {
                trackMesh.indexFormat = IndexFormat.UInt32;
            }

            trackMesh.SetVertices(vertices);
            trackMesh.SetUVs(0, uvs);
            trackMesh.SetColors(colors);
            
            trackMesh.subMeshCount = 2;
            trackMesh.SetTriangles(roadIndices, 0);
            trackMesh.SetTriangles(wallIndices, 1);

            trackMesh.RecalculateNormals();
            trackMesh.RecalculateBounds();
            trackMesh.RecalculateTangents();

            // Create GameObject
            GameObject trackObj = new GameObject(meshName);
            if (parent != null)
            {
                trackObj.transform.SetParent(parent, false);
            }

            SetupMeshComponents(trackObj, trackMesh, roadMaterial, wallMaterial);

            return trackObj;
        }

        /// <summary>
        /// Welds a shortcut's merge-zone frames to the main road. Within one merge distance of each
        /// end, each frame is blended toward a main-road-aligned frame whose centerline is placed so
        /// the shortcut's INNER edge lands exactly on the main road's edge. The blend fades from fully
        /// aligned at the tip (weight 0) to the shortcut's own path at the end of the zone (weight 1).
        /// Combined with the width-gore, the shortcut therefore grows out of the main road edge as one
        /// aligned, coplanar surface — no overlapping tubes, no gap, no hidden faces at the seam.
        /// </summary>
        private void WeldShortcutFramesToMainRoad(SplineFrame[] frames, TrackBranch branch, SplineContainer mainCircuit, TrackConfig config, float scLen)
        {
            float mergeDist = config.ShortcutMergeDistance;
            float goreDist = mergeDist * 0.5f;
            float mainHalf = config.MainRoadWidth * 0.5f;
            float scHalf = config.ShortcutRoadWidth * 0.5f;
            float mainLen = SplineUtilities.GetSplineLength(mainCircuit);
            if (mainLen <= 1e-3f) return;

            for (int i = 0; i < frames.Length; i++)
            {
                float arc = frames[i].ArcLength;
                float distEnd = math.min(arc, scLen - arc);
                if (distEnd >= mergeDist) continue;

                bool atStart = arc <= scLen - arc;
                float side = atStart ? branch.StartSideSign : branch.EndSideSign;
                if (side == 0f) side = 1f;

                // Weld target: MARCH along the main road from the junction by this frame's own
                // distance from the tip. (The old per-frame nearest-point query was non-monotonic
                // near the junction, folding the welded centerline into hooks/flaps at the tips.)
                // The lead-in/lead-out follow the road, so shortcut arc ≈ main-road arc there.
                float nT = atStart
                    ? branch.MainStartT + distEnd / mainLen
                    : branch.MainEndT - distEnd / mainLen;
                nT = math.saturate(nT);
                SplineUtilities.EvaluateSplineFrame(mainCircuit, nT, out float3 mPos, out float3 mTan, out float3 mUp, out float3 mRight);

                // Replicate the main mesh's banking at nT (same lateral-curvature formula as
                // SampleEvenFrames, sampled at the mesh segment spacing) so the welded strip lies
                // in the plane of the actual banked road surface, not the unbanked frame.
                if (config.BankingMultiplier > 0f)
                {
                    float sampleArc = math.max(config.MeshSegmentLength, 1f);
                    float dtArc = sampleArc / mainLen;
                    SplineUtilities.EvaluateSplineFrame(mainCircuit, math.saturate(nT - dtArc), out _, out float3 tanPrev, out _, out _);
                    SplineUtilities.EvaluateSplineFrame(mainCircuit, math.saturate(nT + dtArc), out _, out float3 tanNext, out _, out _);
                    // Same per-meter formula as SampleEvenFrames (via the shared helper), so the
                    // welded strip lies in the plane of the actual banked road surface.
                    float bank = SplineUtilities.ComputeSignedBankAngle(tanPrev, tanNext, mRight, sampleArc * 2f, config.BankingMultiplier, config.MaxBankAngle);
                    quaternion bankRot = quaternion.AxisAngle(mTan, bank);
                    mUp = math.rotate(bankRot, mUp);
                    mRight = math.rotate(bankRot, mRight);
                }

                // Width gore (matches the mesh's roadWidthScale): keeps MinGoreWidth at the tip -> full
                // past goreDist. Must stay identical to the roadWidthScale formula in BuildTrackMesh.
                float ws = math.lerp(MinGoreWidth, 1f, math.smoothstep(0f, 1f, distEnd / math.max(goreDist, 0.01f)));

                // Centerline placed so the shortcut's inner edge stays on the main road edge as it
                // widens: at the tip it sits on the edge; at full width it is one shortcut-half outboard.
                // Phase 1 overlap: additionally pull the centerline EdgeOverlap inboard so the inner edge
                // runs a little ONTO the road, and lift it OverlapLift above the surface — the two
                // separate meshes now overlap instead of meeting at a knife-edge, so no dark T-junction
                // crack forms regardless of tessellation mismatch. Both fade out with the weld weight w.
                // Lift ramps DOWN to nearly zero at the tip so the stub end lies flush on the
                // road (a constant 8cm lift left a visible step + shadow line at the tip); a
                // 1.5cm floor stays to prevent z-fighting on the overlapped strip.
                float lift = math.max(0.015f, OverlapLift * math.smoothstep(0f, 1f, distEnd / math.max(goreDist, 0.01f)));

                float3 alignedPos = mPos
                                  + mRight * (side * (mainHalf + scHalf * ws - EdgeOverlap))
                                  + mUp * lift;

                float w = math.smoothstep(0f, 1f, distEnd / mergeDist); // 0 = fully welded, 1 = own path
                frames[i].Position = math.lerp(alignedPos, frames[i].Position, w);

                // Fade orientation toward the main road at the tip so the surfaces stay coplanar.
                // Both roads travel FORWARD through the junction, so the road tangent is used
                // as-is: the old per-frame sign flip inverted the profile whenever the raw
                // tangent swung past perpendicular, crumpling the tip.
                frames[i].Tangent = math.normalizesafe(math.lerp(mTan, frames[i].Tangent, w), frames[i].Tangent);
                frames[i].Up = math.normalizesafe(math.lerp(mUp, frames[i].Up, w), frames[i].Up);
                frames[i].Right = math.normalizesafe(math.cross(frames[i].Up, frames[i].Tangent), frames[i].Right);
            }
        }

        /// <summary>
        /// Converts a normalized spline parameter (t) into an arc-length position along the
        /// sampled frames. Frames are ordered by increasing arc length with monotonically
        /// increasing t, so we bracket the target t and lerp the stored arc lengths.
        /// </summary>
        private float ArcAtT(SplineFrame[] frames, float targetT)
        {
            if (frames == null || frames.Length == 0) return 0f;
            if (targetT <= frames[0].T) return frames[0].ArcLength;

            for (int i = 0; i < frames.Length - 1; i++)
            {
                if (targetT >= frames[i].T && targetT <= frames[i + 1].T)
                {
                    float span = frames[i + 1].T - frames[i].T;
                    float f = span > 1e-6f ? (targetT - frames[i].T) / span : 0f;
                    return math.lerp(frames[i].ArcLength, frames[i + 1].ArcLength, f);
                }
            }

            return frames[frames.Length - 1].ArcLength;
        }

        private void AddQuad(List<int> indices, int currentRing, int nextRing, int numVerts, int v1, int v2)
        {
            int current1 = currentRing * numVerts + v1;
            int current2 = currentRing * numVerts + v2;
            int next1 = nextRing * numVerts + v1;
            int next2 = nextRing * numVerts + v2;

            // Triangle 1
            indices.Add(current1);
            indices.Add(next1);
            indices.Add(next2);

            // Triangle 2
            indices.Add(current1);
            indices.Add(next2);
            indices.Add(current2);
        }

        /// <summary>
        /// Helper to set up MeshFilter, MeshRenderer, and MeshCollider.
        /// </summary>
        public void SetupMeshComponents(GameObject trackObj, UnityEngine.Mesh mesh, Material roadMat, Material wallMat)
        {
            MeshFilter filter = trackObj.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = trackObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new Material[] { roadMat, wallMat };

            MeshCollider collider = trackObj.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        private Vector3[] TransformProfile(Vector3[] localProfile, SplineFrame frame)
        {
            Vector3[] worldProfile = new Vector3[localProfile.Length];
            Quaternion rotation = Quaternion.LookRotation(frame.Tangent, frame.Up);
            Vector3 position = frame.Position;

            for (int i = 0; i < localProfile.Length; i++)
            {
                worldProfile[i] = position + rotation * localProfile[i];
            }

            return worldProfile;
        }
    }
}
