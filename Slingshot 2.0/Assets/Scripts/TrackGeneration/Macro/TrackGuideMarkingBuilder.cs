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
            float designRoadWidth = 0f, Material persistentWallMarkerMaterial = null)
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
            // Never invent in-memory materials here: they disappear across editor cache
            // recovery and exact replay, which used to leave magenta guide meshes.
            Material guideMat = persistentMaterial;
            Material markerMat = persistentWallMarkerMaterial != null
                ? persistentWallMarkerMaterial
                : persistentMaterial;

            var chains = CollectChains(sections);
            int chunkIndex = 0;
            foreach (var chain in chains)
            {
                var frames = ChainFrames(chain, out List<float> rollingWeights);
                if (frames.Count < 2) continue;

                if (visual.CenterGuideEnabled)
                    BuildCenterGuides(frames, rollingWeights, profile, visual,
                        markingRoot.transform, guideMat, ref chunkIndex, referenceWidth);
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

        private static List<TrackConnectionFrame> ChainFrames(
            List<GeneratedTrackSection> chain, out List<float> rollingWeights)
        {
            var frames = new List<TrackConnectionFrame>();
            rollingWeights = new List<float>();
            foreach (var sec in chain)
            {
                var f = sec.SubdivisionFrames;
                for (int i = frames.Count == 0 ? 0 : 1; i < f.Length; i++)
                {
                    frames.Add(f[i]);
                    rollingWeights.Add(RollingFeatureWeight(sec, f[i]));
                }
            }
            return frames;
        }

        private static float RollingFeatureWeight(
            GeneratedTrackSection section, in TrackConnectionFrame frame)
        {
            if (section?.Definition == null || !HasRoadRoll(section.Definition))
                return 0f;

            float start = section.StartFrame.ArcLength;
            float length = Mathf.Max(0.01f,
                section.EndFrame.ArcLength - section.StartFrame.ArcLength);
            float u = Mathf.Clamp01((frame.ArcLength - start) / length);

            // Keep the weld itself unchanged, then merge rapidly through the entry
            // and hold one center stripe across the actual corkscrew body. The same
            // easing reverses at the exit, so adjacent wide roads branch cleanly.
            float edgeDistance01 = Mathf.Min(u, 1f - u) / 0.18f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edgeDistance01));
        }

        private static bool HasRoadRoll(TrackMacroSectionDefinition definition)
        {
            if (definition.SectionType == TrackMacroSectionType.Corkscrew)
                return true;

            var phases = definition.RotationalPhases;
            if (phases == null) return false;
            for (int i = 0; i < phases.Count; i++)
            {
                var phase = phases[i];
                if (phase != null && phase.Axis == RotationalPhaseAxis.RoadRoll &&
                    phase.RotationUnits > 0)
                    return true;
            }
            return false;
        }

        // ─────────────────────────── Center guide lines ───────────────────────────

        private static void BuildCenterGuides(List<TrackConnectionFrame> frames,
            List<float> rollingWeights, TrackRoadProfileSettings profile,
            TrackVisualSettings visual, Transform root, Material mat,
            ref int chunkIndex, float referenceWidth)
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

            // Width alone is not the usable road width. Dynamic turn rounding can
            // remove almost the entire flat floor while the frame still reports the
            // full design width; wallrides and pipes reshape it further. Classify the
            // actual evaluated shoulder-to-shoulder span at every ring, then spread
            // narrow detections into their neighbours so topology changes form one
            // readable Y transition instead of flickering ring by ring.
            float[] guideSplits = ResolveGuideSplits(frames, rollingWeights, profile,
                mergeWidth, splitWidth, referenceWidth);

            int prevBase = -1;
            for (int r = 0; r < frames.Count; r++)
            {
                var f = frames[r];
                // 0 = fully merged (single centerline), 1 = fully split to the edges.
                float split01 = guideSplits[r];

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

                // A narrow road must read as ONE center stripe, not two full-width
                // strips stacked on top of each other. At the merged end each side
                // owns exactly half of the center stripe. As the road widens those
                // halves separate and grow into two full-width edge stripes. This
                // keeps a continuous Y-shaped transition without doubled emission.
                float mergedHalfOffset = width * 0.25f * (1f - split01);
                float halfLineWidth = width * Mathf.Lerp(0.25f, 0.5f, split01);
                Vector2 leftStripCenter = leftCenter - leftTangent * mergedHalfOffset;
                Vector2 rightStripCenter = rightCenter + rightTangent * mergedHalfOffset;

                int baseIdx = verts.Count;
                // The two quads meet edge-to-edge as one center stripe when merged;
                // they become independent full-width edge lines after branching.
                verts.Add(ProfileToWorld(f, leftStripCenter - leftTangent * halfLineWidth + leftNormal * SurfaceOffset) - origin);
                verts.Add(ProfileToWorld(f, leftStripCenter + leftTangent * halfLineWidth + leftNormal * SurfaceOffset) - origin);
                verts.Add(ProfileToWorld(f, rightStripCenter - rightTangent * halfLineWidth + rightNormal * SurfaceOffset) - origin);
                verts.Add(ProfileToWorld(f, rightStripCenter + rightTangent * halfLineWidth + rightNormal * SurfaceOffset) - origin);

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

        private static float[] ResolveGuideSplits(
            List<TrackConnectionFrame> frames,
            List<float> rollingWeights,
            TrackRoadProfileSettings profile,
            float mergeWidth,
            float splitWidth,
            float referenceWidth)
        {
            int count = frames.Count;
            var splits = new float[count];
            var profilePoints = new Vector2[TrackCrossSection.PointCount(profile)];
            var crossParameters = new float[profilePoints.Length];

            // Evaluate a neutral design-width ring once. It is the correct baseline
            // for usable floor width, rather than assuming the entire half-pipe width
            // is flat/drivable road.
            TrackConnectionFrame referenceFrame = TrackConnectionFrame.Origin(referenceWidth);
            TrackCrossSection.Evaluate(profile, referenceFrame, profilePoints, crossParameters);
            float referenceUsableWidth = MeasureUsableFloorWidth(profilePoints, crossParameters);
            float usableMergeWidth = referenceUsableWidth * (mergeWidth / referenceWidth);
            float usableSplitWidth = referenceUsableWidth * (splitWidth / referenceWidth);

            for (int r = 0; r < count; r++)
            {
                TrackConnectionFrame frame = frames[r];
                TrackCrossSection.Evaluate(profile, frame, profilePoints, crossParameters);
                float usableWidth = MeasureUsableFloorWidth(profilePoints, crossParameters);

                float totalWidthSplit = SmoothWidthSplit(frame.Width, mergeWidth, splitWidth);
                float usableWidthSplit = SmoothWidthSplit(
                    usableWidth, usableMergeWidth, usableSplitWidth);

                // The most restrictive real measurement wins. A road cannot support
                // two readable lanes merely because its outer walls are far apart.
                float split = Mathf.Min(totalWidthSplit, usableWidthSplit);

                // Closed pipes and wallrides each expose one primary driving surface,
                // so a single orientation line is unambiguous even if their chord
                // measurement happens to remain large during the morph.
                split *= 1f - Mathf.SmoothStep(0.08f, 0.62f,
                    Mathf.Clamp01(frame.PipeClosure));
                split *= 1f - Mathf.SmoothStep(0.08f, 0.55f,
                    Mathf.Clamp01(frame.WallrideMorph));

                // Corkscrews and every authored road-roll phase keep the explicit
                // semantic override established for legacy-width presets.
                if (rollingWeights != null && r < rollingWeights.Count)
                    split *= 1f - Mathf.Clamp01(rollingWeights[r]);

                splits[r] = Mathf.Clamp01(split);
            }

            // Propagate the need for a center line over a physical distance. Two
            // passes catch both approach and exit while remaining O(n), important for
            // generated tracks with tens of thousands of rings.
            float transitionDistance = Mathf.Max(18f, referenceWidth * 1.5f);
            PropagateNarrowness(frames, splits, transitionDistance, forward: true);
            PropagateNarrowness(frames, splits, transitionDistance, forward: false);
            return splits;
        }

        private static float SmoothWidthSplit(float width, float mergeWidth, float splitWidth)
        {
            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(mergeWidth, splitWidth, width));
        }

        private static float MeasureUsableFloorWidth(
            Vector2[] profilePoints, float[] crossParameters)
        {
            SampleAtCrossParameter(profilePoints, crossParameters, -1f,
                out Vector2 leftShoulder, out _);
            SampleAtCrossParameter(profilePoints, crossParameters, 1f,
                out Vector2 rightShoulder, out _);
            return Vector2.Distance(leftShoulder, rightShoulder);
        }

        private static void PropagateNarrowness(
            List<TrackConnectionFrame> frames,
            float[] splits,
            float transitionDistance,
            bool forward)
        {
            int start = forward ? 1 : frames.Count - 2;
            int end = forward ? frames.Count : -1;
            int step = forward ? 1 : -1;

            for (int i = start; i != end; i += step)
            {
                int previous = i - step;
                float distance = Mathf.Abs(frames[i].ArcLength - frames[previous].ArcLength);
                if (distance <= 0.0001f)
                    distance = Vector3.Distance(frames[i].Position, frames[previous].Position);

                // Work in merge weight (1 = one center line). Subtracting distance
                // gives a finite linear envelope which cleanly reaches zero.
                float previousMerge = 1f - splits[previous];
                float propagatedMerge = Mathf.Max(0f,
                    previousMerge - distance / transitionDistance);
                splits[i] = Mathf.Min(splits[i], 1f - propagatedMerge);
            }
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

    }
}
