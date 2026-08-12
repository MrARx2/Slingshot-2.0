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
    ///  • CENTER GUIDE LINES — two longitudinal strips marking the edges of the
    ///    center-flat region. As dynamic turn rounding removes the flat zone they move
    ///    inward and merge into one central guide line; inside full pipes the single
    ///    floor guide marks which surface becomes the normal road floor again.
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
            TrackVisualSettings visual, Transform root, Material persistentMaterial = null)
        {
            if (visual == null || (!visual.CenterGuideEnabled && !visual.WallMarkersEnabled)) return;
            if (profile == null || !profile.IsHalfPipe) return;

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
                    BuildCenterGuides(frames, profile, visual, markingRoot.transform, guideMat, ref chunkIndex);
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
            TrackVisualSettings visual, Transform root, Material mat, ref int chunkIndex)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 origin = frames[0].Position;

            // Two strips (left/right flat boundary) that merge into one central strip
            // when the flat zone collapses (full rounding) or the pipe closes.
            int prevBase = -1;
            for (int r = 0; r < frames.Count; r++)
            {
                var f = frames[r];
                float halfW = f.Width * 0.5f;
                float rounding = Mathf.Clamp01(f.TurnRounding);
                float flat = Mathf.Lerp(Mathf.Clamp01(profile.CenterFlatWidthRatio),
                    Mathf.Clamp01(profile.MinTurnCenterFlatRatio), rounding);
                float closure = Mathf.Clamp01(f.PipeClosure);

                float guideX = flat * halfW;
                float width = visual.CenterGuideWidth * Mathf.Clamp01(1.15f - rounding); // fades toward full rounding
                bool merged = closure > 0.1f || guideX < width;
                if (merged) guideX = 0f;

                Vector3 up = f.Up * SurfaceOffset;
                int baseIdx = verts.Count;
                if (merged)
                {
                    // One central floor guide (the pipe's floor bottom stays at the frame baseline).
                    verts.Add(f.Position + up + f.Right * (-width * 0.5f) - origin);
                    verts.Add(f.Position + up + f.Right * (width * 0.5f) - origin);
                    verts.Add(f.Position + up + f.Right * (-width * 0.5f) - origin);
                    verts.Add(f.Position + up + f.Right * (width * 0.5f) - origin);
                }
                else
                {
                    verts.Add(f.Position + up + f.Right * (-guideX - width * 0.5f) - origin);
                    verts.Add(f.Position + up + f.Right * (-guideX + width * 0.5f) - origin);
                    verts.Add(f.Position + up + f.Right * (guideX - width * 0.5f) - origin);
                    verts.Add(f.Position + up + f.Right * (guideX + width * 0.5f) - origin);
                }

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
