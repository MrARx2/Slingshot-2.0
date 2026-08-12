using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration
{
    /// <summary>
    /// READ-ONLY builder-equivalent surface diagnostics. Reconstructs the mesh builder's
    /// FINAL inner (drivable) triangles from the already-built subdivision frames, using the
    /// SAME per-frame <see cref="TrackCrossSection"/> evaluation and the SAME triangle winding
    /// as <see cref="Macro.BoxPrismTrackMeshBuilder"/>, for BOTH the render profile and the
    /// coarse collider profile. It measures where the drivable surface is discontinuous.
    ///
    /// This is a <b>builder-equivalent pre-cooking reconstruction</b>: it does not read PhysX's
    /// internally cooked <c>MeshCollider.sharedMesh</c> — that would be a separate runtime step.
    ///
    /// Changes NOTHING: no frames, cross-sections, widths, walls, corkscrew geometry,
    /// retopology/collider density, or physics are touched — this only reads and measures.
    /// Threshold flags below are PROVISIONAL readability markers, not generation gates.
    /// </summary>
    public static class TrackMeshIntegrityDiagnostics
    {

        // Provisional readability flags (NOT generation gates).
        private const float ProvNonPlanarityM = 0.35f;
        private const float ProvNormalJumpDeg = 6f;

        // Floor lanes as signed cross-parameters (0 = centre … ±1 = flat/wall shoulder).
        private static readonly float[] LaneParam = { 0f, 0.25f, -0.25f, 0.5f, -0.5f, 1f, -1f };
        private static readonly string[] LaneName = { "centre", "R+25%", "L-25%", "R+50%", "L-50%", "R-shoulder", "L-shoulder" };

        public static void Append(StringBuilder sb, List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            if (sb == null || sections == null || cfg == null || cfg.RoadProfile == null) return;

            var render = cfg.RoadProfile;
            var collider = ColliderProfile(render);

            long summedFrames = 0; int builtSections = 0;
            foreach (var s in sections)
                if (s.SubdivisionFrames != null && s.SubdivisionFrames.Length > 0) { summedFrames += s.SubdivisionFrames.Length; builtSections++; }

            sb.AppendLine();
            sb.AppendLine("── Mesh Integrity (builder-equivalent pre-cooking reconstruction, read-only) ──");
            sb.AppendLine($"  render profile: resolution {render.ProfileResolution} | bowl {render.ProfilePointCount} pts | " +
                          $"extension {TrackCrossSection.ExtensionPointCount(render)}/side | total {TrackCrossSection.PointCount(render)} pts/ring");
            sb.AppendLine($"  collider profile: resolution {collider.ProfileResolution} | bowl {collider.ProfilePointCount} pts | " +
                          $"extension {TrackCrossSection.ExtensionPointCount(collider)}/side | total {TrackCrossSection.PointCount(collider)} pts/ring");
            sb.AppendLine($"  rings: {summedFrames} = sum of per-section frame counts across {builtSections} sections " +
                          "(INCLUDES duplicated section-boundary frames; not de-duplicated unique chain rings)");
            sb.AppendLine($"  budgets: perf render ring budget {cfg.RenderRingBudget} | hard cap MaxTotalRings {cfg.MaxTotalRings}");

            CorkscrewLongitudinal(sb, sections, render, collider);
            FloorToWallCollider(sb, sections, render, collider);
        }

        /// <summary>Test/diagnostic hook: floor→first-wall-facet normal step (deg), max adjacent wall
        /// step on THIS side only, and physical boundary miss (m), for one shoulder of one frame+profile.
        /// side +1 = right, −1 = left.</summary>
        public static bool MeasureFloorToWall(in TrackConnectionFrame f, int side,
            TrackRoadProfileSettings profile, out float floorToFirstWall, out float maxAdjacent, out float boundaryMissM)
        {
            int n = TrackCrossSection.PointCount(profile);
            var pts = new Vector2[n]; var cross = new float[n];
            TrackCrossSection.Evaluate(profile, f, pts, cross);
            var r = Shoulder(pts, cross, f, side, profile);
            floorToFirstWall = r.floorToFirstWall; maxAdjacent = r.maxAdjacent; boundaryMissM = r.boundaryMissM;
            return r.valid;
        }

        /// <summary>Test hook: the coarse collider profile the diagnostic reconstructs against.</summary>
        public static TrackRoadProfileSettings ColliderProfileFor(TrackRoadProfileSettings render) => ColliderProfile(render);

        // ═══════════════ A. Corkscrew Longitudinal Surface Integrity ═══════════════

        private static void CorkscrewLongitudinal(StringBuilder sb, List<GeneratedTrackSection> sections,
            TrackRoadProfileSettings render, TrackRoadProfileSettings collider)
        {
            sb.AppendLine();
            sb.AppendLine("── Corkscrew Longitudinal Surface Integrity ──");
            sb.AppendLine($"  builder winding (a+k,b+k,b+k+1)+(a+k,b+k+1,a+k+1); normals oriented to the drivable side.");
            sb.AppendLine($"  provisional flags: quad non-planarity > {ProvNonPlanarityM:F2}m, longitudinal normal step > {ProvNormalJumpDeg:F0}°");

            int corkscrews = 0;
            foreach (var sec in sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;
                if (!IsCorkscrewSection(sec, frames)) continue;
                corkscrews++;

                float rollSpan = Mathf.Abs(frames[frames.Length - 1].AccumulatedRoadRoll - frames[0].AccumulatedRoadRoll);
                float len = frames[frames.Length - 1].ArcLength - frames[0].ArcLength;
                sb.AppendLine($"  [{Name(sec)}] {SecType(sec)} roll {rollSpan:F0}° over {len:F0}m — {frames.Length} rings");

                MeasureLongitudinal(sb, frames, render, "render  ");
                MeasureLongitudinal(sb, frames, collider, "collider");  // the PHYSICAL surface
            }
            if (corkscrews == 0) sb.AppendLine("  (no corkscrew sections on this track)");
            sb.AppendLine();
        }

        /// <summary>Reconstructs one profile's inner surface across the section, per frame, and reports maxima.</summary>
        private static void MeasureLongitudinal(StringBuilder sb, TrackConnectionFrame[] frames,
            TrackRoadProfileSettings profile, string label)
        {
            int n = TrackCrossSection.PointCount(profile);
            var ptsA = new Vector2[n]; var crossA = new float[n];
            var ptsB = new Vector2[n]; var crossB = new float[n];
            var prevStripNormal = new Vector3[n]; bool havePrev = false;

            float maxNonPlanar = 0f; string npWhere = "";
            float maxTwoTri = 0f;
            float maxLongNormal = 0f; string lnWhere = "";
            float maxNormalPerM = 0f;
            float maxShoulderDisp = 0f; string shWhere = "";

            for (int s = 0; s + 1 < frames.Length; s++)
            {
                float ds = frames[s + 1].ArcLength - frames[s].ArcLength;
                if (ds < 1e-3f) { havePrev = false; continue; }

                // Per-frame evaluation (width/rounding/multipliers/overhang/closure vary along the barrel).
                TrackCrossSection.Evaluate(profile, frames[s], ptsA, crossA);
                TrackCrossSection.Evaluate(profile, frames[s + 1], ptsB, crossB);
                Vector3 drivable = (frames[s].Up + frames[s + 1].Up).normalized;

                for (int k = 0; k + 1 < n; k++)
                {
                    float cp = 0.5f * (crossA[k] + crossA[k + 1]);
                    if (Mathf.Abs(cp) > 1.15f) continue; // floor lanes only; walls are section B
                    int lane = NearestLane(cp);

                    Vector3 a0 = World(frames[s], ptsA[k]),     a1 = World(frames[s], ptsA[k + 1]);
                    Vector3 b0 = World(frames[s + 1], ptsB[k]), b1 = World(frames[s + 1], ptsB[k + 1]);

                    // Builder winding: tri1 (a+k,b+k,b+k+1), tri2 (a+k,b+k+1,a+k+1).
                    Vector3 nT1 = OrientedNormal(a0, b0, b1, drivable);
                    Vector3 nT2 = OrientedNormal(a0, b1, a1, drivable);
                    maxTwoTri = Mathf.Max(maxTwoTri, Vector3.Angle(nT1, nT2));

                    // Non-planarity: distance of the 4th quad vertex from the plane of the first three.
                    float np = PointPlaneDistance(a1, a0, b0, b1);
                    if (np > maxNonPlanar) { maxNonPlanar = np; npWhere = $"{LaneName[lane]} @arc {frames[s].ArcLength:F0}m"; }

                    // Longitudinal normal change: this strip's normal vs the same strip in the previous span.
                    Vector3 strip = (nT1 + nT2).normalized;
                    if (havePrev)
                    {
                        float ang = Vector3.Angle(prevStripNormal[k], strip);
                        if (ang > maxLongNormal) { maxLongNormal = ang; lnWhere = $"{LaneName[lane]} @arc {frames[s].ArcLength:F0}m"; }
                        maxNormalPerM = Mathf.Max(maxNormalPerM, ang / ds);
                    }
                    prevStripNormal[k] = strip;

                    // Shoulder transverse displacement per ring (roll-driven sweep of the floor edge).
                    if (Mathf.Abs(cp) >= 0.9f)
                    {
                        Vector3 move = b0 - a0;
                        float transverse = (move - Vector3.Project(move, frames[s].Forward)).magnitude;
                        if (transverse > maxShoulderDisp) { maxShoulderDisp = transverse; shWhere = $"{LaneName[lane]} @arc {frames[s].ArcLength:F0}m"; }
                    }
                }
                havePrev = true;
            }

            sb.AppendLine($"      {label}: non-planarity max {maxNonPlanar:F3}m {Flag(maxNonPlanar > ProvNonPlanarityM)} @ {npWhere} | " +
                          $"two-triangle disagree max {maxTwoTri:F2}°");
            sb.AppendLine($"      {label}: longitudinal normal step max {maxLongNormal:F2}° {Flag(maxLongNormal > ProvNormalJumpDeg)} @ {lnWhere} | " +
                          $"per-m max {maxNormalPerM:F3}°/m | shoulder move/ring max {maxShoulderDisp:F2}m @ {shWhere}");
        }

        // ═══════════════ B. Floor-to-Wall Collider Integrity ═══════════════

        private static void FloorToWallCollider(StringBuilder sb, List<GeneratedTrackSection> sections,
            TrackRoadProfileSettings render, TrackRoadProfileSettings collider)
        {
            sb.AppendLine("── Floor-to-Wall Collider Integrity ──");
            sb.AppendLine($"  coarse collider ({TrackCrossSection.PointCount(collider)} pts/ring) vs render " +
                          $"({TrackCrossSection.PointCount(render)} pts/ring); same analytical chain, discretisation only.");
            sb.AppendLine($"  provisional flag: floor→first-wall-facet normal step > {ProvNormalJumpDeg:F0}°");

            int pcC = TrackCrossSection.PointCount(collider), pcR = TrackCrossSection.PointCount(render);
            var cPts = new Vector2[pcC]; var cCross = new float[pcC];
            var rPts = new Vector2[pcR]; var rCross = new float[pcR];

            var colliderCrossJumps = new List<float>();
            float worstColliderCross = 0f, worstRenderCross = 0f, worstAdj = 0f, worstMissM = 0f;
            string worstWhere = "";
            int measured = 0;

            foreach (var sec in sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 2) continue;
                if (!IsOrdinaryLevelRoad(sec, frames)) continue;
                var f = frames[frames.Length / 2];
                TrackCrossSection.Evaluate(collider, f, cPts, cCross);
                TrackCrossSection.Evaluate(render, f, rPts, rCross);
                measured++;

                foreach (int side in new[] { +1, -1 })
                {
                    var c = Shoulder(cPts, cCross, f, side, collider);
                    var r = Shoulder(rPts, rCross, f, side, render);
                    if (!c.valid) continue;
                    colliderCrossJumps.Add(c.floorToFirstWall);
                    if (c.floorToFirstWall > worstColliderCross)
                    {
                        worstColliderCross = c.floorToFirstWall;
                        worstRenderCross = r.valid ? r.floorToFirstWall : 0f;
                        worstAdj = c.maxAdjacent; worstMissM = c.boundaryMissM;
                        worstWhere = $"{Name(sec)} {(side > 0 ? "R" : "L")}-shoulder @arc {f.ArcLength:F0}m";
                    }
                }
            }

            if (measured == 0) { sb.AppendLine("  (no ordinary level road sections found)"); sb.AppendLine(); return; }

            colliderCrossJumps.Sort();
            float p95 = colliderCrossJumps.Count > 0
                ? colliderCrossJumps[Mathf.Clamp(Mathf.CeilToInt(0.95f * colliderCrossJumps.Count) - 1, 0, colliderCrossJumps.Count - 1)]
                : 0f;

            sb.AppendLine($"  sampled shoulders: {colliderCrossJumps.Count} across {measured} level sections");
            sb.AppendLine($"  collider floor→first-wall-facet normal step: max {worstColliderCross:F2}° {Flag(worstColliderCross > ProvNormalJumpDeg)} | p95 {p95:F2}°");
            sb.AppendLine($"  worst @ {worstWhere}: collider {worstColliderCross:F2}° vs render {worstRenderCross:F2}° " +
                          $"(discretisation gap {worstColliderCross - worstRenderCross:F2}°)");
            sb.AppendLine($"  worst-case max ADJACENT wall-facet step (collider, same side only): {worstAdj:F2}°");
            sb.AppendLine($"  flat-boundary vertex miss (collider): {worstMissM:F3}m from the analytical shoulder");
            sb.AppendLine("  floor/wall positional continuity: exact by construction (one shared chain; only sample COUNT differs)");
            sb.AppendLine();
        }

        private struct ShoulderResult { public bool valid; public float floorToFirstWall; public float maxAdjacent; public float boundaryMissM; }

        /// <summary>Scans ONE shoulder outward on its own side only. side +1 = right (walls toward higher
        /// index, up to N-1), side -1 = left (walls toward index 0). Never crosses the centre.</summary>
        private static ShoulderResult Shoulder(Vector2[] pts, float[] cross, in TrackConnectionFrame f,
            int side, TrackRoadProfileSettings profile)
        {
            var res = new ShoulderResult();
            int n = pts.Length;

            // Find the segment straddling |crossParam| = 1 on the requested side.
            int cs = -1;
            for (int k = 0; k + 1 < n; k++)
            {
                float a = cross[k], b = cross[k + 1];
                bool onSide = side > 0 ? (a >= 0f && b >= 0f) : (a <= 0f && b <= 0f);
                if (!onSide) continue;
                if ((Mathf.Abs(a) - 1f) * (Mathf.Abs(b) - 1f) <= 0f) { cs = k; break; }
            }
            if (cs < 0) return res;

            // Outward = away from centre. For the right side that's +index; for the left, -index.
            int outward = side > 0 ? +1 : -1;
            int floorFacet = cs - outward;   // last fully-floor facet (toward centre)
            int firstWall = cs + outward;    // first fully-wall facet (past the crossing)
            if (floorFacet < 0 || floorFacet + 1 >= n || firstWall < 0 || firstWall + 1 >= n) return res;

            Vector3 nFloor = FacetNormal(pts, floorFacet, f);
            Vector3 nCross = FacetNormal(pts, cs, f);
            Vector3 nWall1 = FacetNormal(pts, firstWall, f);

            res.valid = true;
            res.floorToFirstWall = Vector3.Angle(nFloor, nWall1);           // broad floor→first-wall change
            res.maxAdjacent = Mathf.Max(Vector3.Angle(nFloor, nCross), Vector3.Angle(nCross, nWall1));

            // Continue outward to that side's wall tip, immediate-adjacent steps only.
            Vector3 prev = nWall1;
            for (int k = firstWall + outward; k >= 0 && k + 1 < n; k += outward)
            {
                Vector3 cur = FacetNormal(pts, k, f);
                res.maxAdjacent = Mathf.Max(res.maxAdjacent, Vector3.Angle(prev, cur));
                prev = cur;
            }

            // Physical boundary miss: analytical shoulder x = flat·halfW (the evaluator's exact flat
            // ratio for this frame); nearest actual collider vertex x.
            float halfW = 0.5f * Mathf.Max(1f, f.Width);
            float flat = Mathf.Lerp(Mathf.Clamp01(profile.CenterFlatWidthRatio),
                                    Mathf.Clamp01(profile.MinTurnCenterFlatRatio), Mathf.Clamp01(f.TurnRounding));
            float shoulderX = side * flat * halfW;
            res.boundaryMissM = Mathf.Min(Mathf.Abs(pts[cs].x - shoulderX), Mathf.Abs(pts[cs + 1].x - shoulderX));
            return res;
        }

        // ═══════════════ helpers ═══════════════

        private static TrackRoadProfileSettings ColliderProfile(TrackRoadProfileSettings p) =>
            new TrackRoadProfileSettings
            {
                Shape = p.Shape, SideHeight = p.SideHeight, WallCurve01 = p.WallCurve01,
                CenterFlatWidthRatio = p.CenterFlatWidthRatio, SafetyLipHeight = p.SafetyLipHeight,
                MinTurnCenterFlatRatio = p.MinTurnCenterFlatRatio, MaxOverhangAngleDeg = p.MaxOverhangAngleDeg,
                OverhangRadius = p.OverhangRadius,
                // Mirror the builder exactly: collider resolution, floored at 8, capped by render.
                ProfileResolution = Mathf.Min(Mathf.Max(8, p.ColliderProfileResolution), p.ProfileResolution)
            };

        private static Vector3 World(in TrackConnectionFrame f, Vector2 p) => f.Position + p.x * f.Right + p.y * f.Up;

        /// <summary>Triangle normal for the builder winding, flipped to face the drivable side.</summary>
        private static Vector3 OrientedNormal(Vector3 a, Vector3 b, Vector3 c, Vector3 drivable)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (nrm.sqrMagnitude < 1e-12f) return drivable;
            nrm.Normalize();
            return Vector3.Dot(nrm, drivable) < 0f ? -nrm : nrm;
        }

        /// <summary>3D normal of a level-road cross-section facet (k,k+1), oriented to the drivable side.</summary>
        private static Vector3 FacetNormal(Vector2[] pts, int k, in TrackConnectionFrame f)
        {
            if (k < 0 || k + 1 >= pts.Length) return f.Up;
            Vector2 d = (pts[k + 1] - pts[k]).normalized;
            Vector3 world = -d.y * f.Right + d.x * f.Up; // 2D normal (-d.y, d.x) embedded in Right/Up
            if (Vector3.Dot(world, f.Up) < 0f) world = -world;
            return world.normalized;
        }

        private static float PointPlaneDistance(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            return nrm.sqrMagnitude < 1e-12f ? 0f : Mathf.Abs(Vector3.Dot(p - a, nrm.normalized));
        }

        private static int NearestLane(float cp)
        {
            int best = 0; float e = float.MaxValue;
            for (int l = 0; l < LaneParam.Length; l++) { float d = Mathf.Abs(cp - LaneParam[l]); if (d < e) { e = d; best = l; } }
            return best;
        }

        private static bool IsCorkscrewSection(GeneratedTrackSection sec, TrackConnectionFrame[] frames)
        {
            var d = sec.Definition;
            if (d != null)
            {
                if (d.SemanticElement == SemanticElementId.InlineCorkscrew ||
                    d.SemanticElement == SemanticElementId.DirectionalCorkscrew ||
                    d.SemanticElement == SemanticElementId.DoubleCorkscrew) return true;
                if (!string.IsNullOrEmpty(d.DebugName) && d.DebugName.Contains("Corkscrew")) return true;
            }
            if (!string.IsNullOrEmpty(sec.PatternId) && sec.PatternId.Contains("Corkscrew")) return true;
            float span = Mathf.Abs(frames[frames.Length - 1].AccumulatedRoadRoll - frames[0].AccumulatedRoadRoll);
            return span >= 90f; // localised to this section's own frames
        }

        private static bool IsOrdinaryLevelRoad(GeneratedTrackSection sec, TrackConnectionFrame[] frames)
        {
            var d = sec.Definition;
            if (d == null) return false;
            if (d.SectionType != TrackMacroSectionType.Straight &&
                d.SectionType != TrackMacroSectionType.WideStraight &&
                d.SectionType != TrackMacroSectionType.BoostStraight &&
                d.SectionType != TrackMacroSectionType.BankedCurve) return false;
            float span = Mathf.Abs(frames[frames.Length - 1].AccumulatedRoadRoll - frames[0].AccumulatedRoadRoll);
            return span < 20f;
        }

        private static string Name(GeneratedTrackSection sec) =>
            !string.IsNullOrEmpty(sec.Definition?.DebugName) ? sec.Definition.DebugName :
            !string.IsNullOrEmpty(sec.PatternId) ? sec.PatternId : $"section_{sec.SectionIndex}";

        private static string SecType(GeneratedTrackSection sec) =>
            sec.Definition != null ? sec.Definition.SectionType.ToString() : "?";

        private static string Flag(bool bad) => bad ? "⚠" : "ok";
    }
}
