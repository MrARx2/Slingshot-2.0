using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// GLOBAL RETOPOLOGY: the final layout is resampled so every independent physical
    /// road region (an anchored segment) carries a subdivision count from the APPROVED
    /// LADDER, distributed at exactly uniform arc spacing. Builders are free to emit
    /// whatever ring density their geometry needs — this pass rebuilds the topology
    /// from the finished driving line, so no section boundary, feature entry or ramp
    /// can ever show a ring-density step in the mesh.
    ///
    /// Terminology: Subdivision Count = number of intervals; Frame Count = intervals + 1.
    ///
    /// Per region, the RAW requirement comes from the greatest of: physical length at
    /// the base ring spacing, the strictest orientation-change (facet) demand any of
    /// its source rings exhibits, and the per-section ring cap — then relaxed for the
    /// global ring budgets. The selected tier is the first ladder entry ≥ the raw
    /// requirement (never rounded down below the physical requirement). Regions that
    /// share an anchor ring (section runs across the lap, branch gates, the
    /// start/finish weld) are reconciled so their ring spacings never step by more
    /// than ~1.22× across the shared ring.
    ///
    /// Rings that other chains weld to are ANCHORS and are preserved bit-exactly:
    /// chain endpoints (the start/finish weld), branch gate rings (route B meshes join
    /// the fork rings there) and air-gap lip/mouth rings (ballistically matched).
    /// </summary>
    public static class TrackRetopology
    {
        private const float MaxNeighborSpacingRatio = 1.22f;

        /// <summary>Resamples every chain of the layout onto its region's quantized uniform grid.</summary>
        public static void Apply(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg)
        {
            var segments = CollectSegments(layout);
            if (segments.Count == 0) return;

            var regions = PlanRegions(segments, cfg);
            if (regions == null) return;

            layout.SubdivisionRegions.Clear();
            for (int i = 0; i < segments.Count; i++)
            {
                ResampleSegment(segments[i], regions[i].Intervals);
                RefreshVerticalMetrics(segments[i]);
                layout.SubdivisionRegions.Add(regions[i].ToRecord(i));
            }
        }

        /// <summary>
        /// Remeasures vertical curvature and its spatial derivative from the final
        /// retopologized driving line. Interpolating the source metadata is not
        /// authoritative: a section boundary can retain a builder's coarse one-ring
        /// derivative even when the final Hermite road is smooth, producing a false
        /// TransitionRateExceeded result. Shared section-boundary rings receive one
        /// canonical measurement.
        /// </summary>
        public static void RefreshVerticalMetrics(List<GeneratedTrackSection> sections)
        {
            if (sections == null || sections.Count == 0) return;

            bool IsMeasuredOrdinary(GeneratedTrackSection section)
            {
                TrackMacroSectionDefinition definition = section?.Definition;
                if (definition == null || section.SubdivisionFrames == null ||
                    section.SubdivisionFrames.Length < 2) return false;
                bool ordinary;
                switch (definition.SectionType)
                {
                    case TrackMacroSectionType.Straight:
                    case TrackMacroSectionType.WideStraight:
                    case TrackMacroSectionType.BoostStraight:
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                    case TrackMacroSectionType.SCurve:
                    case TrackMacroSectionType.Chicane:
                        ordinary = true;
                        break;
                    default:
                        ordinary = false;
                        break;
                }
                return ordinary && (Mathf.Abs(definition.HillHeight) < 0.001f ||
                                    definition.SemanticElement == SemanticElementId.Horseshoe ||
                                    definition.SemanticElement == SemanticElementId.HalfHelixTurnaround);
            }

            void RefreshRun(List<GeneratedTrackSection> run)
            {
                if (run.Count == 0) return;
                var frames = new List<TrackConnectionFrame>();
                var ringMap = new List<int[]>(run.Count);
                for (int s = 0; s < run.Count; s++)
                {
                    TrackConnectionFrame[] source = run[s].SubdivisionFrames;
                    var map = new int[source.Length];
                    for (int i = 0; i < source.Length; i++)
                    {
                        if (i == 0 && frames.Count > 0 &&
                            (source[i].Position - frames[frames.Count - 1].Position).sqrMagnitude <= 0.25f)
                        {
                            map[i] = frames.Count - 1;
                            continue;
                        }
                        map[i] = frames.Count;
                        frames.Add(source[i]);
                    }
                    ringMap.Add(map);
                }
                if (frames.Count < 2) return;

                int count = frames.Count;
                var pitch = new float[count];
                var curvature = new float[count];
                var rate = new float[count];
                for (int i = 0; i < count; i++)
                    pitch[i] = Mathf.Asin(Mathf.Clamp(frames[i].Forward.normalized.y, -1f, 1f));

                float Distance(int a, int b)
                {
                    float measured = Vector3.Distance(frames[a].Position, frames[b].Position);
                    return Mathf.Max(0.05f, measured);
                }

                curvature[0] = (pitch[1] - pitch[0]) / Distance(0, 1);
                for (int i = 1; i < count - 1; i++)
                    curvature[i] = (pitch[i + 1] - pitch[i - 1]) / Distance(i - 1, i + 1);
                curvature[count - 1] = (pitch[count - 1] - pitch[count - 2]) /
                                       Distance(count - 2, count - 1);

                rate[0] = (curvature[1] - curvature[0]) / Distance(0, 1);
                for (int i = 1; i < count - 1; i++)
                    rate[i] = (curvature[i + 1] - curvature[i - 1]) / Distance(i - 1, i + 1);
                rate[count - 1] = (curvature[count - 1] - curvature[count - 2]) /
                                  Distance(count - 2, count - 1);

                for (int s = 0; s < run.Count; s++)
                {
                    int[] map = ringMap[s];
                    TrackConnectionFrame[] target = run[s].SubdivisionFrames;
                    for (int i = 0; i < target.Length; i++)
                    {
                        TrackConnectionFrame frame = target[i];
                        frame.VerticalCurvature = curvature[map[i]];
                        frame.VerticalCurvatureRate = rate[map[i]];
                        target[i] = frame;
                    }
                    run[s].StartFrame = target[0];
                    run[s].EndFrame = target[target.Length - 1];
                }
            }

            var current = new List<GeneratedTrackSection>();
            for (int i = 0; i < sections.Count; i++)
            {
                GeneratedTrackSection section = sections[i];
                if (IsMeasuredOrdinary(section))
                {
                    current.Add(section);
                    continue;
                }
                RefreshRun(current);
                current.Clear();
            }
            RefreshRun(current);
        }

        /// <summary>Selects the first approved tier ≥ the raw requirement (spec: never round down below the physical quality requirement).</summary>
        public static int SelectSubdivisionTier(int required, IReadOnlyList<int> allowedTiers)
        {
            if (allowedTiers == null || allowedTiers.Count == 0) return Mathf.Max(8, required);

            required = Mathf.Max(required, allowedTiers[0]);
            for (int i = 0; i < allowedTiers.Count; i++)
            {
                if (allowedTiers[i] >= required)
                    return allowedTiers[i];
            }
            return allowedTiers[allowedTiers.Count - 1];
        }

        // ─────────────────────────── Segment collection ───────────────────────────

        /// <summary>
        /// Contiguous runs of meshed sections whose interior rings may be moved. Runs
        /// break at air gaps (no road across) and at road transitions (a dual quarter's
        /// alternate road is its own open chain), so every break boundary is an anchor.
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
                if (sec.RoadId == 1) continue; // alternate road chains handled below

                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2)
                {
                    // Air gap (or degenerate): physical break — lip/mouth become anchors.
                    CloseRun();
                    prev = null;
                    continue;
                }

                run.Add(sec);
                prev = sec;
            }
            CloseRun();

            // Each dual quarter's alternate road is ONE open segment (mouth → lip).
            var alternate = new List<GeneratedTrackSection>();
            foreach (var sec in layout.Sections)
            {
                if (sec.RoadId == 1 && sec.SubdivisionFrames != null && sec.SubdivisionFrames.Length >= 2)
                {
                    alternate.Add(sec);
                    continue;
                }
                if (alternate.Count > 0)
                {
                    segments.Add(alternate);
                    alternate = new List<GeneratedTrackSection>();
                }
            }
            if (alternate.Count > 0) segments.Add(alternate);

            return segments;
        }

        // ─────────────────────────── Region planning ───────────────────────────

        private class Region
        {
            public List<GeneratedTrackSection> Sections;
            public float Length;
            public float DemandSpacing;
            public int RawRequirement;
            public int Intervals;
            public string LimitingFactor = "base spacing";
            public bool IsRollHeavy;   // inversion roll: wide floor rotates → protect its density

            public SubdivisionRegionRecord ToRecord(int index)
            {
                var names = new List<string>();
                foreach (var sec in Sections)
                {
                    if (names.Count == 4) { names.Add("…"); break; }
                    names.Add(sec.Definition.DebugName);
                }
                return new SubdivisionRegionRecord
                {
                    RegionName = $"Region {index:D2}",
                    Sections = string.Join(", ", names),
                    LengthMeters = Length,
                    RawRequirement = RawRequirement,
                    SelectedTier = Intervals,
                    SpacingMeters = Length / Mathf.Max(1, Intervals),
                    LimitingFactor = LimitingFactor
                };
            }
        }

        /// <summary>
        /// Per-region spacing demand → global budget relaxation → ladder tier
        /// selection → shared-anchor reconciliation.
        /// </summary>
        private static List<Region> PlanRegions(List<List<GeneratedTrackSection>> segments,
            ResolvedTrackGenerationConfig cfg)
        {
            float baseSpacing = Mathf.Max(0.5f, cfg.MeshMetersPerRing);
            float textureSpacing = Mathf.Max(0.5f, cfg.TextureTopologyMetersPerRing);
            float facetLimit = Mathf.Max(0.1f, cfg.MaxRingFacetAngle);
            float featureFloor = Mathf.Max(0.5f, cfg.FeatureMetersPerRing);
            var ladder = cfg.SubdivisionLadder;

            var regions = new List<Region>(segments.Count);
            float totalLength = 0f;
            int totalSectionCount = 0;

            foreach (var seg in segments)
            {
                float spacing = baseSpacing;
                string limiting = "base spacing";
                float maxSectionLength = 0f;
                float rollMin = float.MaxValue, rollMax = float.MinValue;

                foreach (var sec in seg)
                {
                    var frames = sec.SubdivisionFrames;
                    for (int i = 1; i < frames.Length; i++)
                    {
                        rollMin = Mathf.Min(rollMin, frames[i].AccumulatedRoadRoll);
                        rollMax = Mathf.Max(rollMax, frames[i].AccumulatedRoadRoll);
                        float ds = frames[i].ArcLength - frames[i - 1].ArcLength;
                        if (ds <= 1e-3f) continue;

                        // Every builder already honors the facet limit at its own
                        // density, so a pair whose jump EXCEEDS the limit is a discrete
                        // snap (closure weld, feature exit flatten) — densifying cannot
                        // smooth a snap, so such outliers are ignored.
                        float ang = Mathf.Max(
                            Vector3.Angle(frames[i - 1].Forward, frames[i].Forward),
                            Vector3.Angle(frames[i - 1].Up, frames[i].Up));
                        ang = Mathf.Max(ang, Mathf.Abs(frames[i].AccumulatedRoadRoll -
                                                       frames[i - 1].AccumulatedRoadRoll));
                        if (ang < 1e-3f || ang > facetLimit) continue;

                        float demand = ds * facetLimit / ang;
                        if (demand < spacing)
                        {
                            spacing = demand;
                            limiting = "curvature/orientation";
                        }
                    }

                    maxSectionLength = Mathf.Max(maxSectionLength,
                        frames[frames.Length - 1].ArcLength - frames[0].ArcLength);
                }

                if (cfg.ConsistentTextureTopology)
                {
                    // Production texture topology: geometry may bend and roll, but its
                    // final longitudinal grid never changes density by feature, score,
                    // candidate, or performance budget.
                    spacing = textureSpacing;
                    limiting = "fixed texture topology";
                }
                else if (spacing < featureFloor)
                {
                    spacing = featureFloor;
                    limiting = "feature ring floor";
                }

                // Per-section ring cap (boundary rings duplicated, hence headroom).
                if (!cfg.ConsistentTextureTopology && cfg.MaxRingsPerSection > 2)
                {
                    float capSpacing = maxSectionLength / (cfg.MaxRingsPerSection - 2);
                    if (spacing < capSpacing)
                    {
                        spacing = capSpacing;
                        limiting = "section ring cap";
                    }
                }

                var first = seg[0].SubdivisionFrames;
                var last = seg[seg.Count - 1].SubdivisionFrames;
                float length = last[last.Length - 1].ArcLength - first[0].ArcLength;
                totalLength += length;
                totalSectionCount += seg.Count;

                // A region whose road roll spans ≥120° is an inversion roll: its wide
                // floor rotates, so relaxing its facet launches the craft off the floor edge.
                bool rollHeavy = rollMax > rollMin && (rollMax - rollMin) >= 120f;

                regions.Add(new Region
                {
                    Sections = seg,
                    Length = Mathf.Max(0.01f, length),
                    DemandSpacing = spacing,
                    LimitingFactor = limiting,
                    IsRollHeavy = rollHeavy
                });
            }

            // ── Global ring budgets: relax every region's spacing proportionally
            // rather than overflow the rulebook cap or the designer performance budget.
            float expectedRings = 0f;
            foreach (var r in regions) expectedRings += r.Length / r.DemandSpacing;

            float hardBudget = cfg.MaxTotalRings - totalSectionCount - 8;
            float perfBudget = cfg.RenderRingBudget > 0 ? cfg.RenderRingBudget : float.MaxValue;
            float budget = Mathf.Min(hardBudget > 0 ? hardBudget : float.MaxValue, perfBudget);

            if (!cfg.ConsistentTextureTopology && expectedRings > budget)
            {
                bool anyRollHeavy = false;
                foreach (var r in regions) if (r.IsRollHeavy) { anyRollHeavy = true; break; }

                float protectedRings = 0f, relaxableRings = 0f;
                foreach (var r in regions)
                {
                    float rings = r.Length / r.DemandSpacing;
                    if (r.IsRollHeavy) protectedRings += rings; else relaxableRings += rings;
                }

                // Only protect the barrels if doing so still leaves the other regions a
                // workable share of the budget — otherwise protecting them would silently
                // blow the HARD ring cap, so fall back to relaxing everything uniformly.
                bool protectRoll = cfg.SmoothCorkscrewFloor && anyRollHeavy
                                   && protectedRings <= budget * 0.85f;
                if (protectRoll)
                {
                    // Keep inversion floors at their demanded density — relaxing THEM
                    // is what turns a small facet into a multi-metre twist on the wide rolling
                    // floor (the felt launch). Absorb the overflow on the other regions.
                    // NOTE (known limitation): a "region" is a whole continuous road run, so
                    // this preserves density across the region CONTAINING the barrel, not just
                    // the barrel span. True localization needs sub-region density (split at the
                    // rotational-event boundaries) — a separate change.
                    float otherBudget = Mathf.Max(1f, budget - protectedRings);
                    if (relaxableRings > otherBudget)
                    {
                        float relax = relaxableRings / otherBudget;
                        foreach (var r in regions)
                            if (!r.IsRollHeavy)
                            {
                                r.DemandSpacing *= relax;
                                r.LimitingFactor = "ring budget";
                            }
                    }
                }
                else
                {
                    float relax = expectedRings / budget;
                    foreach (var r in regions)
                    {
                        r.DemandSpacing *= relax;
                        r.LimitingFactor = "ring budget";
                    }
                }
            }

            // ── Ladder tiers ──
            foreach (var r in regions)
            {
                r.RawRequirement = Mathf.Max(1, Mathf.CeilToInt(r.Length / r.DemandSpacing));
                r.Intervals = cfg.ConsistentTextureTopology
                    ? r.RawRequirement
                    : SelectSubdivisionTier(r.RawRequirement, ladder);

                // Structural floor: each section needs at least one interval of its own.
                int structural = r.Sections.Count + 1;
                if (r.Intervals < structural)
                {
                    r.Intervals = structural;
                    r.LimitingFactor = "section count";
                }
            }

            // ── Shared-anchor reconciliation: regions that meet at an anchor ring
            // (section runs, branch gates, the start/finish weld) may use different
            // tiers, but their spacings must not step across the shared ring.
            if (!cfg.ConsistentTextureTopology)
                ReconcileNeighbors(regions, ladder);

            return regions;
        }

        /// <summary>
        /// Finds region pairs sharing an endpoint anchor and bumps the sparser one up
        /// the ladder until every shared boundary's spacing ratio is within bounds.
        /// Air-gap boundaries never pair (the lip and mouth are different positions),
        /// so flight gaps legally separate density domains.
        /// </summary>
        private static void ReconcileNeighbors(List<Region> regions, IReadOnlyList<int> ladder)
        {
            Vector3 StartPos(Region r) => r.Sections[0].SubdivisionFrames[0].Position;
            Vector3 EndPos(Region r)
            {
                var f = r.Sections[r.Sections.Count - 1].SubdivisionFrames;
                return f[f.Length - 1].Position;
            }

            long Key(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 4f) & 0x1FFFFF)
                                 | (((long)Mathf.RoundToInt(p.y * 4f) & 0x1FFFFF) << 21)
                                 | (((long)Mathf.RoundToInt(p.z * 4f) & 0x1FFFFF) << 42);

            var byAnchor = new Dictionary<long, List<int>>();
            void Register(long key, int idx)
            {
                if (!byAnchor.TryGetValue(key, out var list)) byAnchor[key] = list = new List<int>();
                if (!list.Contains(idx)) list.Add(idx);
            }

            for (int i = 0; i < regions.Count; i++)
            {
                Register(Key(StartPos(regions[i])), i);
                Register(Key(EndPos(regions[i])), i);
            }

            for (int pass = 0; pass < 6; pass++)
            {
                bool changed = false;
                foreach (var kv in byAnchor)
                {
                    var list = kv.Value;
                    for (int a = 0; a < list.Count; a++)
                    {
                        for (int b = a + 1; b < list.Count; b++)
                        {
                            var ra = regions[list[a]];
                            var rb = regions[list[b]];
                            float sa = ra.Length / ra.Intervals;
                            float sb = rb.Length / rb.Intervals;
                            if (Mathf.Max(sa, sb) / Mathf.Min(sa, sb) <= MaxNeighborSpacingRatio) continue;

                            var sparser = sa > sb ? ra : rb;
                            int bumped = SelectSubdivisionTier(sparser.Intervals + 1, ladder);
                            if (bumped <= sparser.Intervals) continue; // top of the ladder
                            sparser.Intervals = bumped;
                            sparser.LimitingFactor = "neighbor continuity";
                            changed = true;
                        }
                    }
                }
                if (!changed) break;
            }
        }

        // ─────────────────────────── Resampling ───────────────────────────

        private static void ResampleSegment(List<GeneratedTrackSection> seg, int intervals)
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
            if (segLen < 0.5f) return; // degenerate — endpoints already anchored

            int n = Mathf.Max(seg.Count + 1, intervals);
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
                AccumulatedRoadRoll = Mathf.Lerp(f0.AccumulatedRoadRoll, f1.AccumulatedRoadRoll, t),
                AccumulatedVerticalRotation = Mathf.Lerp(f0.AccumulatedVerticalRotation, f1.AccumulatedVerticalRotation, t),
                HorizontalCurvature = Mathf.Lerp(f0.HorizontalCurvature, f1.HorizontalCurvature, t),
                HorizontalCurvatureRate = Mathf.Lerp(f0.HorizontalCurvatureRate, f1.HorizontalCurvatureRate, t),
                VerticalCurvature = Mathf.Lerp(f0.VerticalCurvature, f1.VerticalCurvature, t),
                VerticalCurvatureRate = Mathf.Lerp(f0.VerticalCurvatureRate, f1.VerticalCurvatureRate, t),
                RoadRollRate = Mathf.Lerp(f0.RoadRollRate, f1.RoadRollRate, t),
                RoadRollAcceleration = Mathf.Lerp(f0.RoadRollAcceleration, f1.RoadRollAcceleration, t),
                ArcLength = a,
                LapProgress = Mathf.Lerp(f0.LapProgress, f1.LapProgress, t),
                SideHeight = Mathf.Lerp(f0.SideHeight, f1.SideHeight, t),
                LeftWallSuppression = Mathf.Lerp(f0.LeftWallSuppression, f1.LeftWallSuppression, t),
                RightWallSuppression = Mathf.Lerp(f0.RightWallSuppression, f1.RightWallSuppression, t),
                TurnRounding = Mathf.Lerp(f0.TurnRounding, f1.TurnRounding, t),
                LeftOverhang = Mathf.Lerp(f0.LeftOverhang, f1.LeftOverhang, t),
                RightOverhang = Mathf.Lerp(f0.RightOverhang, f1.RightOverhang, t),
                PipeClosure = Mathf.Lerp(f0.PipeClosure, f1.PipeClosure, t),
                WallrideMorph = Mathf.Lerp(f0.WallrideMorph, f1.WallrideMorph, t)
            };
        }
    }
}
