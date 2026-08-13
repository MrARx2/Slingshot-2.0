using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TrackGeneration.Design;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// Builds the road guidance visuals as thin NON-COLLIDING overlay meshes floating
    /// a few centimeters above the drivable surface:
    ///
    ///  • CENTER GUIDE LINES — two longitudinal strips near the road edges that smoothly
    ///    slide together into a single centerline as the road NARROWS (corkscrews, loops,
    ///    tight features) and branch back apart where it widens. The split is a
    ///    continuous function of width — the strips converge to the same position and
    ///    read as one line, with no popping. Inside full pipes they collapse to the
    ///    single floor guide marking which surface is the normal road floor.
    ///  • WALL MARKERS — transverse bands across the walls at a VISUAL spacing
    ///    (independent of ring density; high-resolution geometry never becomes marker
    ///    noise). Inside pipes the bands wrap the full circumference as orientation
    ///    rings.
    ///
    /// Markings evaluate THE SAME parametric cross-section as the mesh and collider,
    /// so they follow rounding, catch walls, wallrides and pipe closure exactly. They
    /// are continuous across sections/connectors/turn complexes and break only where
    /// the road breaks (air gaps, jump lips, open boundaries).
    /// </summary>
    public static class TrackGuideMarkingBuilder
    {
        private const float SurfaceOffset = 0.07f;   // meters above the surface (no z-fighting, no ridge)

        public static void Build(List<GeneratedTrackSection> sections, TrackRoadProfileSettings profile,
            TrackVisualSettings visual, Transform root, Material persistentMaterial = null,
            float designRoadWidth = 0f)
        {
            if (visual == null || (!visual.CenterGuideEnabled && !visual.WallMarkersEnabled)) return;
            if (profile == null || !profile.IsHalfPipe) return;

            // Reference width for the lane branch/merge decision. The configured design
            // road width is the intuitive anchor ("narrow" = narrow relative to what the
            // designer set, and it re-scales automatically if that number changes). When
            // it isn't supplied, fall back to the widest road anywhere in the layout
            // (features only ever narrow the road, so the global max ≈ the design width).
            float referenceWidth = designRoadWidth;
            if (referenceWidth <= 0.01f)
            {
                if (sections != null)
                    foreach (var sec in sections)
                    {
                        if (sec == null || sec.SubdivisionFrames == null) continue;
                        foreach (var fr in sec.SubdivisionFrames)
                            referenceWidth = Mathf.Max(referenceWidth, fr.Width);
                    }
            }
            referenceWidth = Mathf.Max(0.01f, referenceWidth);

            var markingRoot = new GameObject("TrackGuideMarkings");
            markingRoot.transform.SetParent(root, false);

            // Generated previews are deliberately excluded from scene serialization.
            // A material created only in memory can therefore lose its shader/reference
            // during editor recovery and render magenta. Production tracks receive a
            // persistent project material; the runtime material remains a test/fallback.
            Material guideMat = persistentMaterial != null
                ? persistentMaterial
                : MakeMaterial(visual.CenterGuideColor, visual.CenterGuideEmission);
            Material markerMat = persistentMaterial != null
                ? persistentMaterial
                : MakeMaterial(visual.WallMarkerColor, 1f);

            var chains = CollectChains(sections);
            int chunkIndex = 0;
            foreach (var chain in chains)
            {
                var frames = ChainFrames(chain);
                if (frames.Count < 2) continue;

                if (visual.CenterGuideEnabled)
                    BuildCenterGuides(frames, profile, visual, markingRoot.transform, guideMat, ref chunkIndex, referenceWidth);
                if (visual.WallMarkersEnabled)
                    BuildWallMarkers(frames, profile, visual, markingRoot.transform, markerMat, ref chunkIndex);
            }
        }

        // ─────────────────────────── Chains (same breaks as the road mesh) ───────────────────────────

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

            return chains;
        }

        private static List<TrackConnectionFrame> ChainFrames(List<GeneratedTrackSection> chain)
        {
            var frames = new List<TrackConnectionFrame>();
            foreach (var sec in chain)
            {
                var f = sec.SubdivisionFrames;
                for (int i = frames.Count == 0 ? 0 : 1; i < f.Length; i++)
                    frames.Add(f[i]);
            }
            return frames;
        }

        // ─────────────────────────── Center guide lines ───────────────────────────

        private static void BuildCenterGuides(List<TrackConnectionFrame> frames, TrackRoadProfileSettings profile,
            TrackVisualSettings visual, Transform root, Material mat, ref int chunkIndex, float referenceWidth)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 origin = frames[0].Position;
            var profilePoints = new Vector2[TrackCrossSection.PointCount(profile)];
            var crossParameters = new float[profilePoints.Length];

            // The two lines fully split near the design-width road and slide together into
            // a single centerline where the road narrows (corkscrews, loops, tight
            // features), then branch apart again as it opens up. The reference is the
            // configured design road width, so "narrow" is absolute and consistent
            // everywhere — and re-scales automatically if the road width setting changes.
            float mergeFrac = Mathf.Clamp01(visual.LaneMergeFraction);
            float splitFrac = Mathf.Clamp(visual.LaneSplitFraction, mergeFrac + 0.02f, 1f);
            float edgePosition = Mathf.Clamp(visual.LaneEdgePosition, 0.4f, 2f);
            float mergeWidth = referenceWidth * mergeFrac;
            float splitWidth = referenceWidth * splitFrac;

            int prevBase = -1;
            for (int r = 0; r < frames.Count; r++)
            {
                var f = frames[r];
                float closure = Mathf.Clamp01(f.PipeClosure);

                // 0 = fully merged (single centerline), 1 = fully split to the edges.
                float split01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mergeWidth, splitWidth, f.Width));
                // A closing pipe collapses to a single floor line regardless of width.
                split01 *= 1f - Mathf.SmoothStep(0.1f, 0.6f, closure);

                TrackCrossSection.Evaluate(profile, f, profilePoints, crossParameters);

                // Cross-section position 1 is the exact road/wall shoulder. Sampling the
                // evaluated profile removes the old artificial gap and keeps the marking
                // attached to rounded, banked and wall-ride surfaces. Values up to 2 allow
                // the same marking to be moved along the wall to its tip when desired.
                float guidePosition = split01 * edgePosition;
                SampleProfileSurface(profilePoints, crossParameters, -guidePosition,
                    out Vector2 leftCenter, out Vector2 leftTangent, out Vector2 leftNormal);
                SampleProfileSurface(profilePoints, crossParameters, guidePosition,
                    out Vector2 rightCenter, out Vector2 rightTangent, out Vector2 rightNormal);

                float width = visual.CenterGuideWidth;
                float halfLineWidth = width * 0.5f;

                int baseIdx = verts.Count;
                // Always two strips. When guidePosition reaches 0 they coincide and render as one
                // centerline; no hard swap, so the branch/merge is seamless.
                verts.Add(ProfileToWorld(f, leftCenter - leftTangent * halfLineWidth + leftNormal * SurfaceOffset) - origin);
                verts.Add(ProfileToWorld(f, leftCenter + leftTangent * halfLineWidth + leftNormal * SurfaceOffset) - origin);
                verts.Add(ProfileToWorld(f, rightCenter - rightTangent * halfLineWidth + rightNormal * SurfaceOffset) - origin);
                verts.Add(ProfileToWorld(f, rightCenter + rightTangent * halfLineWidth + rightNormal * SurfaceOffset) - origin);

                if (prevBase >= 0)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        int a = prevBase + s * 2;
                        int b = baseIdx + s * 2;
                        tris.Add(a); tris.Add(b); tris.Add(b + 1);
                        tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
                    }
                }
                prevBase = baseIdx;
            }

            EmitMesh(verts, tris, origin, $"CenterGuide_{chunkIndex++:D2}", root, mat);
        }

        private static Vector3 ProfileToWorld(TrackConnectionFrame frame, Vector2 point)
        {
            return frame.Position + frame.Right * point.x + frame.Up * point.y;
        }

        private static void SampleProfileSurface(
            Vector2[] points,
            float[] crossParameters,
            float signedPosition,
            out Vector2 point,
            out Vector2 tangent,
            out Vector2 inwardNormal)
        {
            float magnitude = Mathf.Clamp(Mathf.Abs(signedPosition), 0f, 2f);
            float side = signedPosition < 0f ? -1f : 1f;
            float shoulderParameter = side * Mathf.Min(magnitude, 1f);

            SampleAtCrossParameter(points, crossParameters, shoulderParameter,
                out point, out int segmentIndex);

            if (magnitude > 1f)
            {
                SampleFromShoulderToTip(points, point, segmentIndex, side, magnitude - 1f,
                    out point, out segmentIndex);
            }

            tangent = SurfaceTangent(points, segmentIndex);
            inwardNormal = new Vector2(-tangent.y, tangent.x);
            if (inwardNormal.sqrMagnitude < 0.000001f)
                inwardNormal = Vector2.up;
            else
                inwardNormal.Normalize();
        }

        private static void SampleAtCrossParameter(
            Vector2[] points,
            float[] crossParameters,
            float target,
            out Vector2 point,
            out int segmentIndex)
        {
            const float epsilon = 0.0001f;
            int count = Mathf.Min(points.Length, crossParameters.Length);
            for (int i = 0; i < count - 1; i++)
            {
                float a = crossParameters[i];
                float b = crossParameters[i + 1];
                if (Mathf.Abs(b - a) <= epsilon)
                    continue;
                if (target < Mathf.Min(a, b) - epsilon || target > Mathf.Max(a, b) + epsilon)
                    continue;

                point = Vector2.Lerp(points[i], points[i + 1],
                    Mathf.Clamp01((target - a) / (b - a)));
                segmentIndex = i;
                return;
            }

            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float distance = Mathf.Abs(crossParameters[i] - target);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = i;
            }

            point = points[nearest];
            segmentIndex = Mathf.Clamp(nearest, 0, Mathf.Max(0, points.Length - 2));
        }

        private static void SampleFromShoulderToTip(
            Vector2[] points,
            Vector2 shoulder,
            int shoulderSegment,
            float side,
            float fraction,
            out Vector2 point,
            out int segmentIndex)
        {
            bool rightSide = side >= 0f;
            int firstIndex = rightSide ? shoulderSegment + 1 : shoulderSegment;
            int tipIndex = rightSide ? points.Length - 1 : 0;
            int step = rightSide ? 1 : -1;

            float totalLength = Vector2.Distance(shoulder, points[firstIndex]);
            for (int i = firstIndex; i != tipIndex; i += step)
                totalLength += Vector2.Distance(points[i], points[i + step]);

            float remaining = Mathf.Clamp01(fraction) * totalLength;
            Vector2 from = shoulder;
            int toIndex = firstIndex;
            while (true)
            {
                Vector2 to = points[toIndex];
                float length = Vector2.Distance(from, to);
                if (remaining <= length || toIndex == tipIndex)
                {
                    float t = length > 0.000001f ? remaining / length : 0f;
                    point = Vector2.Lerp(from, to, Mathf.Clamp01(t));
                    segmentIndex = rightSide
                        ? Mathf.Clamp(toIndex - 1, 0, points.Length - 2)
                        : Mathf.Clamp(toIndex, 0, points.Length - 2);
                    return;
                }

                remaining -= length;
                from = to;
                toIndex += step;
            }
        }

        private static Vector2 SurfaceTangent(Vector2[] points, int segmentIndex)
        {
            int index = Mathf.Clamp(segmentIndex, 0, Mathf.Max(0, points.Length - 2));
            Vector2 surfaceTangent = points[index + 1] - points[index];
            return surfaceTangent.sqrMagnitude > 0.000001f
                ? surfaceTangent.normalized
                : Vector2.right;
        }

        // ─────────────────────────── Wall markers ───────────────────────────

        private static void BuildWallMarkers(List<TrackConnectionFrame> frames, TrackRoadProfileSettings profile,
            TrackVisualSettings visual, Transform root, Material mat, ref int chunkIndex)
        {
            int n = TrackCrossSection.PointCount(profile);
            var pts = new Vector2[n];
            var cross = new float[n];

            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 origin = frames[0].Position;

            float spacing = Mathf.Max(4f, visual.WallMarkerSpacingMeters);
            float halfBand = Mathf.Max(0.05f, visual.WallMarkerWidth * 0.5f);

            float arc0 = frames[0].ArcLength;
            float arc1 = frames[frames.Count - 1].ArcLength;
            int cursor = 0;

            for (float a = Mathf.Ceil(arc0 / spacing) * spacing; a < arc1; a += spacing)
            {
                while (cursor < frames.Count - 1 && frames[cursor + 1].ArcLength < a) cursor++;
                var f = frames[Mathf.Min(cursor + 1, frames.Count - 1)];

                TrackCrossSection.Evaluate(profile, f, pts, cross);
                bool fullRing = f.PipeClosure > 0.5f; // orientation rings inside pipes

                // Band along the profile chain, extruded ± half the marker width along Forward.
                int stripStart = -1;
                for (int k = 0; k < n; k++)
                {
                    bool onWall = fullRing || Mathf.Abs(cross[k]) >= 0.98f;
                    if (onWall && stripStart < 0) stripStart = k;
                    bool flush = (!onWall || k == n - 1) && stripStart >= 0;
                    if (!flush) continue;

                    int end = onWall ? k : k - 1;
                    if (end > stripStart)
                    {
                        int baseIdx = verts.Count;
                        for (int i = stripStart; i <= end; i++)
                        {
                            // Inward normal (toward the road interior) lifts the band off the surface.
                            Vector2 t = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
                            Vector2 inward = t.sqrMagnitude > 1e-10f
                                ? new Vector2(-t.y, t.x).normalized
                                : Vector2.up;
                            Vector3 p = f.Position
                                + f.Right * (pts[i].x + inward.x * SurfaceOffset)
                                + f.Up * (pts[i].y + inward.y * SurfaceOffset)
                                - origin;
                            verts.Add(p - f.Forward * halfBand);
                            verts.Add(p + f.Forward * halfBand);
                        }
                        for (int i = 0; i < end - stripStart; i++)
                        {
                            int v0 = baseIdx + i * 2;
                            tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 3);
                            tris.Add(v0); tris.Add(v0 + 3); tris.Add(v0 + 2);
                        }
                    }
                    stripStart = -1;
                }
            }

            EmitMesh(verts, tris, origin, $"WallMarkers_{chunkIndex++:D2}", root, mat);
        }

        // ─────────────────────────── Shared ───────────────────────────

        private static void EmitMesh(List<Vector3> verts, List<int> tris, Vector3 origin, string name,
            Transform root, Material mat)
        {
            if (verts.Count < 3 || tris.Count < 3) return;

            var mesh = new UnityEngine.Mesh { name = name };
            if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            obj.transform.localPosition = origin;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Deliberately NO collider: markings must never create physical geometry.
        }

        private static Material MakeMaterial(Color color, float emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "TrackGuideMarking" };
            Color hdr = color * (1f + Mathf.Max(0f, emission));
            hdr.a = color.a;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdr);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", hdr);
            return mat;
        }
    }
}
