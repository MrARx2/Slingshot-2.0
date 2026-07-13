using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// GLOBAL RETOPOLOGY: the final layout is resampled so rings sit at ONE uniform
    /// spacing along the entire track. Builders are free to emit whatever ring density
    /// their geometry needs (facet limits, roll fidelity) — this pass then rebuilds the
    /// topology from the finished driving line, so no section boundary, feature entry or
    /// ramp can ever show a ring-density step in the mesh.
    ///
    /// The spacing is the base ring spacing tightened to the strictest facet demand
    /// anywhere on the track (a corkscrew's roll, a loop's curvature), so the uniform
    /// grid never under-samples a feature.
    ///
    /// Rings that other chains weld to are ANCHORS and are preserved bit-exactly:
    /// chain endpoints (the start/finish weld), branch gate rings (route B meshes join
    /// the fork rings there) and air-gap lip/mouth rings (ballistically matched).
    /// Between anchors the spacing is exactly uniform; across an anchor it may differ
    /// by at most half a ring over the whole span.
    /// </summary>
    public static class TrackRetopology
    {
        /// <summary>Resamples every chain of the layout onto the uniform ring grid.</summary>
        public static void Apply(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg)
        {
            var segments = CollectSegments(layout);
            if (segments.Count == 0) return;

            float spacing = ChooseSpacing(segments, cfg);
            if (spacing <= 0.01f) return;

            foreach (var seg in segments)
                ResampleSegment(seg, spacing);
        }

        // ─────────────────────────── Segment collection ───────────────────────────

        /// <summary>
        /// Contiguous runs of meshed sections whose interior rings may be moved. Runs
        /// break at air gaps (no road across) and at branch gates (the fork rings are
        /// welded to another chain), so every break boundary is an anchor.
        /// </summary>
        private static List<List<GeneratedTrackSection>> CollectSegments(GeneratedTrackLayout layout)
        {
            var segments = new List<List<GeneratedTrackSection>>();
            var run = new List<GeneratedTrackSection>();

            void CloseRun()
            {
                if (run.Count > 0) segments.Add(run);
                run = new List<GeneratedTrackSection>();
            }

            GeneratedTrackSection prev = null;
            foreach (var sec in layout.Sections)
            {
                if (sec.RouteId == 1) continue; // route B chains handled below

                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2)
                {
                    // Air gap (or degenerate): physical break — lip/mouth become anchors.
                    CloseRun();
                    prev = null;
                    continue;
                }

                // Gate boundaries: entering or leaving a branch group anchors the fork ring.
                if (prev != null && (prev.BranchGroupId != sec.BranchGroupId || prev.RouteId != sec.RouteId))
                    CloseRun();

                run.Add(sec);
                prev = sec;
            }
            CloseRun();

            foreach (var sec in layout.Sections)
            {
                if (sec.RouteId != 1 || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2) continue;
                segments.Add(new List<GeneratedTrackSection> { sec }); // endpoints are the gate rings
            }

            return segments;
        }

        // ─────────────────────────── Spacing selection ───────────────────────────

        /// <summary>
        /// One spacing for the whole track: the base ring spacing, tightened until the
        /// uniform grid satisfies the strictest orientation-change demand any source
        /// ring pair exhibits (turn, pitch or roll per meter vs the facet limit), then
        /// relaxed as needed to respect the per-section and total ring budgets.
        ///
        /// Every builder already honors the facet limit at its own density, so a pair
        /// whose jump EXCEEDS the limit is a discrete snap (closure weld, feature exit
        /// flatten) — densifying the whole track cannot smooth a snap, so such outliers
        /// are ignored, and the legitimate demand can never undercut the densest
        /// builder spacing.
        /// </summary>
        private static float ChooseSpacing(List<List<GeneratedTrackSection>> segments, ResolvedTrackGenerationConfig cfg)
        {
            float baseSpacing = Mathf.Max(0.5f, cfg.MeshMetersPerRing);
            float facetLimit = Mathf.Max(0.1f, cfg.MaxRingFacetAngle);
            float spacing = baseSpacing;
            float totalLength = 0f;
            float maxSectionLength = 0f;
            int sectionCount = 0;

            foreach (var seg in segments)
            {
                foreach (var sec in seg)
                {
                    var frames = sec.SubdivisionFrames;
                    for (int i = 1; i < frames.Length; i++)
                    {
                        float ds = frames[i].ArcLength - frames[i - 1].ArcLength;
                        if (ds <= 1e-3f) continue;

                        float ang = Mathf.Max(
                            Vector3.Angle(frames[i - 1].Forward, frames[i].Forward),
                            Vector3.Angle(frames[i - 1].Up, frames[i].Up));
                        if (ang < 1e-3f || ang > facetLimit) continue; // above-limit = snap outlier

                        spacing = Mathf.Min(spacing, ds * facetLimit / ang);
                    }

                    maxSectionLength = Mathf.Max(maxSectionLength,
                        frames[frames.Length - 1].ArcLength - frames[0].ArcLength);
                    sectionCount++;
                }

                var first = seg[0].SubdivisionFrames;
                var last = seg[seg.Count - 1].SubdivisionFrames;
                totalLength += last[last.Length - 1].ArcLength - first[0].ArcLength;
            }

            spacing = Mathf.Clamp(spacing, Mathf.Max(0.5f, cfg.FeatureMetersPerRing), baseSpacing);

            // Ring budgets: relax the spacing rather than overflow the rulebook caps
            // (boundary rings are duplicated between sections, hence the headroom).
            if (cfg.MaxRingsPerSection > 2)
                spacing = Mathf.Max(spacing, maxSectionLength / (cfg.MaxRingsPerSection - 2));
            int totalBudget = cfg.MaxTotalRings - sectionCount - 8;
            if (totalBudget > 0)
                spacing = Mathf.Max(spacing, totalLength / totalBudget);

            // Designer PERFORMANCE budget: a small track keeps the full designed
            // density; a huge lap (long Rollercoaster tracks) spreads the ring budget
            // instead of exploding the vertex count and the frame rate with it.
            if (cfg.RenderRingBudget > 0)
                spacing = Mathf.Max(spacing, totalLength / cfg.RenderRingBudget);

            return spacing;
        }

        // ─────────────────────────── Resampling ───────────────────────────

        private static void ResampleSegment(List<GeneratedTrackSection> seg, float spacing)
        {
            // Flatten to one source ring list; boundary rings are duplicated between
            // adjacent sections, so skip each later section's entry ring.
            var src = new List<TrackConnectionFrame>();
            var boundaryArcs = new float[seg.Count]; // exit arc of each section
            for (int s = 0; s < seg.Count; s++)
            {
                var frames = seg[s].SubdivisionFrames;
                for (int i = s == 0 ? 0 : 1; i < frames.Length; i++)
                {
                    // Defensive: drop non-advancing rings so interpolation stays monotonic.
                    if (src.Count > 0 && frames[i].ArcLength - src[src.Count - 1].ArcLength <= 1e-4f)
                        continue;
                    src.Add(frames[i]);
                }
                boundaryArcs[s] = frames[frames.Length - 1].ArcLength;
            }
            if (src.Count < 2) return;

            float s0 = src[0].ArcLength;
            float segLen = src[src.Count - 1].ArcLength - s0;
            if (segLen < spacing * 2f) return; // too short to re-grid — endpoints already anchored

            int n = Mathf.Max(seg.Count + 1, Mathf.RoundToInt(segLen / spacing));
            float step = segLen / n;

            // ── Uniform target rings (endpoints are the untouched anchor frames) ──
            var target = new TrackConnectionFrame[n + 1];
            target[0] = src[0];
            target[n] = src[src.Count - 1];

            int cursor = 0;
            for (int k = 1; k < n; k++)
            {
                float a = s0 + step * k;
                while (cursor < src.Count - 2 && src[cursor + 1].ArcLength < a) cursor++;
                target[k] = InterpolateFrame(src, cursor, a);
            }

            // ── Re-split onto the sections: each boundary snaps to the nearest ring,
            // adjacent sections share that ring exactly (the weld invariant) ──
            int prevIdx = 0;
            for (int s = 0; s < seg.Count; s++)
            {
                int endIdx = s == seg.Count - 1
                    ? n
                    : Mathf.Clamp(Mathf.RoundToInt((boundaryArcs[s] - s0) / step),
                        prevIdx + 1, n - (seg.Count - 1 - s));

                var frames = new TrackConnectionFrame[endIdx - prevIdx + 1];
                for (int i = 0; i < frames.Length; i++) frames[i] = target[prevIdx + i];

                var sec = seg[s];
                sec.SubdivisionFrames = frames;
                sec.StartFrame = frames[0];
                sec.EndFrame = frames[frames.Length - 1];
                prevIdx = endIdx;
            }
        }

        /// <summary>
        /// One resampled ring at arc <paramref name="a"/> inside source interval
        /// <paramref name="i"/>: C1 Hermite position (finite-difference tangents),
        /// slerped orientation, lerped per-ring signals.
        /// </summary>
        private static TrackConnectionFrame InterpolateFrame(List<TrackConnectionFrame> src, int i, float a)
        {
            var f0 = src[i];
            var f1 = src[i + 1];
            float ds = f1.ArcLength - f0.ArcLength;
            float t = Mathf.Clamp01((a - f0.ArcLength) / ds);

            // Catmull-Rom style tangents on the non-uniform arc parameter.
            Vector3 pPrev = i > 0 ? src[i - 1].Position : f0.Position - (f1.Position - f0.Position);
            float sPrev = i > 0 ? src[i - 1].ArcLength : f0.ArcLength - ds;
            Vector3 pNext = i + 2 < src.Count ? src[i + 2].Position : f1.Position + (f1.Position - f0.Position);
            float sNext = i + 2 < src.Count ? src[i + 2].ArcLength : f1.ArcLength + ds;

            Vector3 m0 = (f1.Position - pPrev) / Mathf.Max(1e-4f, f1.ArcLength - sPrev) * ds;
            Vector3 m1 = (pNext - f0.Position) / Mathf.Max(1e-4f, sNext - f0.ArcLength) * ds;

            float t2 = t * t, t3 = t2 * t;
            Vector3 pos = (2f * t3 - 3f * t2 + 1f) * f0.Position + (t3 - 2f * t2 + t) * m0
                        + (-2f * t3 + 3f * t2) * f1.Position + (t3 - t2) * m1;

            Quaternion rot = Quaternion.Slerp(
                Quaternion.LookRotation(f0.Forward, f0.Up),
                Quaternion.LookRotation(f1.Forward, f1.Up), t);

            Vector3 fwd = rot * Vector3.forward;
            Vector3 up = rot * Vector3.up;
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            up = Vector3.Cross(fwd, right).normalized;

            return new TrackConnectionFrame
            {
                Position = pos,
                Forward = fwd,
                Right = right,
                Up = up,
                Width = Mathf.Lerp(f0.Width, f1.Width, t),
                BankAngle = Mathf.LerpAngle(f0.BankAngle, f1.BankAngle, t),
                PitchAngle = Mathf.LerpAngle(f0.PitchAngle, f1.PitchAngle, t),
                ArcLength = a,
                LapProgress = Mathf.Lerp(f0.LapProgress, f1.LapProgress, t),
                SideHeight = Mathf.Lerp(f0.SideHeight, f1.SideHeight, t),
                LeftWallSuppression = Mathf.Lerp(f0.LeftWallSuppression, f1.LeftWallSuppression, t),
                RightWallSuppression = Mathf.Lerp(f0.RightWallSuppression, f1.RightWallSuppression, t)
            };
        }
    }
}
