using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// Scene-view debug visualization for a generated macro track. Added to the track root
    /// by <see cref="TrackGeneration.TrackGenerator"/> so a generated track stays inspectable:
    /// section labels, entry/exit frames, banking and turn direction, jump landing targets,
    /// section bounds, seed value, and the full generated section list (in the inspector).
    /// </summary>
    [AddComponentMenu("Track Generation/Macro Track Debug Visualizer")]
    public class MacroTrackDebugVisualizer : MonoBehaviour
    {
        [Header("Track Data (Read Only)")]
        [Tooltip("Seed the layout was generated from.")]
        public int Seed;

        [Tooltip("The generated macro section list, in track order.")]
        public List<GeneratedTrackSection> Sections = new List<GeneratedTrackSection>();

        [Tooltip("The resolved half-pipe road profile the mesh was built with (for cross-section debug drawing).")]
        public TrackRoadProfileSettings RoadProfile;

        [Header("Draw Toggles")]
        public bool ShowLabels = true;
        public bool ShowFrames = true;
        public bool ShowBounds = false;
        public bool ShowBanking = true;
        public bool ShowTurnDirection = true;
        public bool ShowJumpTargets = true;

        [Header("Half-Pipe / Blend Debug")]
        [Tooltip("Draw the half-pipe cross-section profile at intervals along the track.")]
        public bool ShowCrossSection = true;
        [Tooltip("Draw bank angle and half-pipe depth over distance (blend zone visualization).")]
        public bool ShowBlendZones = false;
        [Tooltip("Label the staged route split zones (lateral separation, vertical divergence, ...).")]
        public bool ShowRouteZones = true;

        [Tooltip("Draw per-side wall multipliers along split routes and highlight suppressed inner-wall (open throat) zones.")]
        public bool ShowWallMasks = true;

        [Tooltip("Mark OPEN boundaries: jump lip, landing mouth, and air-gap edges where no cap/wall may cross the flight path.")]
        public bool ShowOpenEdges = true;

        [Header("Branch / Pattern / Progress Debug")]
        [Tooltip("Label feature-pattern groups (Loop→Corkscrew, HalfLoopToCorkscrew, …).")]
        public bool ShowPatternLabels = true;

        [Tooltip("Label branch groups and draw their entry/merge gates.")]
        public bool ShowBranchGates = true;

        [Tooltip("Color branch route centerlines per route (A cyan / B orange).")]
        public bool ShowRouteColors = true;

        [Tooltip("Display the estimated route-time table of each branch group.")]
        public bool ShowRouteTimes = true;

        [Tooltip("Mark logical lap progress every 10% along the driving line.")]
        public bool ShowLapProgress = false;

        [Tooltip("Highlight closure-reserve sections (adjustable straights the closure solver used).")]
        public bool ShowClosureSections = false;

        [Tooltip("Estimated route-time tables per branch group (filled by the generator).")]
        public List<TrackGeneration.Branching.BranchBalanceMetrics> BranchBalances = new List<TrackGeneration.Branching.BranchBalanceMetrics>();

        [Header("Colors")]
        public Color FrameForwardColor = Color.blue;
        public Color FrameRightColor = Color.red;
        public Color FrameUpColor = Color.green;
        public Color BoundsColor = new Color(1f, 1f, 0f, 0.4f);
        public Color BankingColor = new Color(0f, 1f, 1f, 0.8f);
        public Color JumpTargetColor = new Color(1f, 0.4f, 0f, 1f);
        public Color CrossSectionColor = new Color(0.3f, 1f, 0.5f, 0.9f);
        public Color BlendZoneColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        public Color RouteZoneColor = new Color(0.9f, 0.3f, 1f, 1f);
        public Color WallFullColor = new Color(0.2f, 0.9f, 0.2f, 0.9f);
        public Color WallSuppressedColor = new Color(1f, 0.15f, 0.15f, 0.9f);
        public Color OpenEdgeColor = new Color(0.2f, 0.9f, 1f, 1f);

        /// <summary>Called by the generator after a successful macro generation.</summary>
        public void Initialize(int seed, List<GeneratedTrackSection> sections, TrackRoadProfileSettings roadProfile = null)
        {
            Seed = seed;
            Sections = sections;
            RoadProfile = roadProfile;
        }

        /// <summary>Stores branch balance tables from the authoritative layout for scene display.</summary>
        public void SetLayout(TrackGeneration.Planning.GeneratedTrackLayout layout)
        {
            BranchBalances.Clear();
            if (layout == null) return;
            foreach (var group in layout.BranchGroups)
                if (group.Balance != null) BranchBalances.Add(group.Balance);
        }

        /// <summary>
        /// Gizmo stride in RING INDICES for a target arc distance. Strides must be
        /// arc-based, never fixed indices: ring density is a mesh-quality setting, and a
        /// fixed index stride multiplies the gizmo line count (and tanks editor FPS on
        /// big tracks) every time the density is raised.
        /// </summary>
        private static int StrideFor(GeneratedTrackSection sec, float meters)
        {
            int rings = sec.SubdivisionFrames?.Length ?? 0;
            if (rings < 2) return 1;
            float ds = (sec.EndFrame.ArcLength - sec.StartFrame.ArcLength) / (rings - 1);
            return Mathf.Max(1, Mathf.RoundToInt(meters / Mathf.Max(0.1f, ds)));
        }

        private void OnDrawGizmos()
        {
            if (Sections == null || Sections.Count == 0) return;

            Matrix4x4 toWorld = transform.localToWorldMatrix;

            for (int i = 0; i < Sections.Count; i++)
            {
                var section = Sections[i];
                if (section?.Definition == null) continue;

                Vector3 startPos = toWorld.MultiplyPoint3x4(section.StartFrame.Position);
                Vector3 endPos = toWorld.MultiplyPoint3x4(section.EndFrame.Position);

                if (ShowFrames)
                {
                    DrawFrame(section.StartFrame, toWorld, 4f);
                    DrawFrame(section.EndFrame, toWorld, 2.5f);
                }

                if (ShowBounds)
                {
                    Gizmos.color = BoundsColor;
                    Bounds b = section.SectionBounds;
                    Gizmos.matrix = toWorld;
                    Gizmos.DrawWireCube(b.center, b.size);
                    Gizmos.matrix = Matrix4x4.identity;
                }

                if (ShowTurnDirection && section.Definition.TurnSign != 0 &&
                    section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 2)
                {
                    // Arrow from the mid frame toward the inside of the turn.
                    var mid = section.SubdivisionFrames[section.SubdivisionFrames.Length / 2];
                    Vector3 midPos = toWorld.MultiplyPoint3x4(mid.Position);
                    Vector3 inward = toWorld.MultiplyVector(mid.Right) * section.Definition.TurnSign;
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(midPos + Vector3.up, midPos + Vector3.up + inward * 6f);
                    Gizmos.DrawSphere(midPos + Vector3.up + inward * 6f, 0.6f);
                }

                if (ShowBanking && section.SubdivisionFrames != null)
                {
                    Gizmos.color = BankingColor;
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += StrideFor(section, 20f))
                    {
                        var fr = section.SubdivisionFrames[f];
                        if (Mathf.Abs(fr.BankAngle) < 2f) continue;
                        Vector3 p = toWorld.MultiplyPoint3x4(fr.Position);
                        Gizmos.DrawLine(p, p + toWorld.MultiplyVector(fr.Up) * 4f);
                    }
                }

                if (ShowCrossSection && RoadProfile != null && RoadProfile.IsHalfPipe &&
                    section.SubdivisionFrames != null)
                {
                    // Half-pipe cross-section polylines: center baseline + rising side walls.
                    Gizmos.color = CrossSectionColor;
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += StrideFor(section, 80f))
                    {
                        DrawCrossSection(section.SubdivisionFrames[f], toWorld);
                    }
                }

                if (ShowBlendZones && section.SubdivisionFrames != null)
                {
                    // Bank angle (orange) and half-pipe depth (green) over distance — makes
                    // blend zones and any abrupt transitions immediately visible.
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += StrideFor(section, 12f))
                    {
                        var fr = section.SubdivisionFrames[f];
                        Vector3 p = toWorld.MultiplyPoint3x4(fr.Position);
                        Gizmos.color = BlendZoneColor;
                        Gizmos.DrawLine(p, p + Vector3.up * (Mathf.Abs(fr.BankAngle) * 0.1f));
                        Gizmos.color = CrossSectionColor;
                        Vector3 side = toWorld.MultiplyVector(fr.Right) * 0.5f;
                        Gizmos.DrawLine(p + side, p + side + Vector3.up * (fr.SideHeight * 0.5f));
                    }
                }

                if (ShowWallMasks && section.SubdivisionFrames != null &&
                    section.Definition.SectionType == TrackMacroSectionType.SplitRoute)
                {
                    // Per-side wall multiplier bars at each road edge: green = full wall,
                    // red = suppressed (open throat). Bar height tracks the multiplier, so
                    // the fade in/out of the inner wall is directly visible in the scene.
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += StrideFor(section, 12f))
                    {
                        var fr = section.SubdivisionFrames[f];
                        float halfW = fr.Width * 0.5f;

                        DrawWallMaskBar(fr, toWorld, -halfW, fr.LeftWallMultiplier);
                        DrawWallMaskBar(fr, toWorld, halfW, fr.RightWallMultiplier);
                    }
                }

                if (ShowOpenEdges)
                {
                    // Open boundaries: a bright crossbar at the boundary marks "nothing may
                    // block this direction" — jump lip (exit), landing mouth (entry), air gap.
                    Gizmos.color = OpenEdgeColor;
                    if (section.OpenEnd && section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 0)
                        DrawOpenEdge(section.SubdivisionFrames[section.SubdivisionFrames.Length - 1], toWorld, forwardSign: 1f);
                    if (section.OpenStart && section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 0)
                        DrawOpenEdge(section.SubdivisionFrames[0], toWorld, forwardSign: -1f);
                    if (section.IsEmptySpace)
                    {
                        // Air gap: mark both open faces of the empty space.
                        DrawOpenEdge(section.StartFrame, toWorld, forwardSign: 1f);
                        DrawOpenEdge(section.EndFrame, toWorld, forwardSign: -1f);
                    }
                }

                if (ShowRouteColors && section.RouteId >= 0 && section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 1)
                {
                    // Distinct route colors: A cyan, B orange.
                    Gizmos.color = section.RouteId == 0 ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.6f, 0.15f);
                    int routeStride = StrideFor(section, 10f);
                    for (int f = routeStride; f < section.SubdivisionFrames.Length; f += routeStride)
                    {
                        Gizmos.DrawLine(
                            toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f - routeStride].Position + section.SubdivisionFrames[f - routeStride].Up),
                            toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f].Position + section.SubdivisionFrames[f].Up));
                    }
                }

                if (ShowClosureSections && section.Definition.IsClosure && section.SubdivisionFrames != null)
                {
                    Gizmos.color = Color.yellow;
                    int closureStride = StrideFor(section, 10f);
                    for (int f = closureStride; f < section.SubdivisionFrames.Length; f += closureStride)
                    {
                        Gizmos.DrawLine(
                            toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f - closureStride].Position + Vector3.up * 2f),
                            toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f].Position + Vector3.up * 2f));
                    }
                }

                if (ShowLapProgress && section.SubdivisionFrames != null)
                {
                    Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
                    for (int f = 1; f < section.SubdivisionFrames.Length; f++)
                    {
                        float p0 = section.SubdivisionFrames[f - 1].LapProgress;
                        float p1 = section.SubdivisionFrames[f].LapProgress;
                        int tick0 = Mathf.FloorToInt(p0 * 10f);
                        int tick1 = Mathf.FloorToInt(p1 * 10f);
                        if (tick1 > tick0 && tick1 <= 10)
                        {
                            Vector3 p = toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f].Position);
                            Gizmos.DrawWireSphere(p + Vector3.up * 2f, 1.2f);
#if UNITY_EDITOR
                            UnityEditor.Handles.Label(p + Vector3.up * 4f, $"{tick1 * 10}%");
#endif
                        }
                    }
                }

#if UNITY_EDITOR
                if (ShowBranchGates && section.RouteId == 0 && section.BranchGroupId >= 0)
                {
                    Gizmos.color = new Color(0.4f, 1f, 0.4f);
                    Gizmos.DrawWireSphere(startPos + Vector3.up * 2f, 2.5f);
                    Gizmos.DrawWireSphere(endPos + Vector3.up * 2f, 2.5f);
                    UnityEditor.Handles.Label(startPos + Vector3.up * 8f, $"Branch {section.BranchGroupId} ENTRY GATE");
                    UnityEditor.Handles.Label(endPos + Vector3.up * 8f, $"Branch {section.BranchGroupId} MERGE GATE");

                    if (ShowRouteTimes && BranchBalances != null)
                    {
                        foreach (var balance in BranchBalances)
                        {
                            if (balance.BranchGroupId != section.BranchGroupId) continue;
                            UnityEditor.Handles.Label(startPos + Vector3.up * 12f, balance.ToString());
                            break;
                        }
                    }
                }

                if (ShowPatternLabels && !string.IsNullOrEmpty(section.Definition.PatternId) &&
                    section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 2)
                {
                    // Label once per pattern: only on the first section of the group.
                    bool firstOfPattern = i == 0 || Sections[i - 1]?.Definition == null ||
                                          Sections[i - 1].Definition.PatternId != section.Definition.PatternId;
                    if (firstOfPattern)
                    {
                        var mid = section.SubdivisionFrames[section.SubdivisionFrames.Length / 2];
                        UnityEditor.Handles.Label(
                            toWorld.MultiplyPoint3x4(mid.Position) + Vector3.up * 10f,
                            $"◆ {section.Definition.PatternId}");
                    }
                }
#endif

#if UNITY_EDITOR
                if (ShowRouteZones && section.Definition.SectionType == TrackMacroSectionType.SplitRoute &&
                    section.Definition.RouteZoneBoundaries != null &&
                    section.Definition.RouteZoneBoundaries.Length >= 6 &&
                    section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 2)
                {
                    var zb = section.Definition.RouteZoneBoundaries;
                    string[] zoneNames = { "Lateral Separation ends", "Vertical Divergence starts", "Vertical Divergence ends", "Vertical Convergence starts", "Vertical Convergence ends", "Lateral Merge starts" };
                    Gizmos.color = RouteZoneColor;
                    for (int z = 0; z < 6; z++)
                    {
                        int fi = Mathf.Clamp(Mathf.RoundToInt(zb[z] * (section.SubdivisionFrames.Length - 1)), 0, section.SubdivisionFrames.Length - 1);
                        var fr = section.SubdivisionFrames[fi];
                        Vector3 p = toWorld.MultiplyPoint3x4(fr.Position);
                        Gizmos.DrawLine(p, p + Vector3.up * 5f);
                        UnityEditor.Handles.Label(p + Vector3.up * 5.5f, zoneNames[z]);
                    }
                }
#endif

                if (ShowJumpTargets && section.Definition.SectionType == TrackMacroSectionType.AirGap)
                {
                    Gizmos.color = JumpTargetColor;
                    Gizmos.DrawLine(startPos, endPos);
                    Gizmos.DrawWireSphere(endPos, 1.5f);
                }

#if UNITY_EDITOR
                if (ShowLabels)
                {
                    Vector3 labelPos = startPos + toWorld.MultiplyVector(section.StartFrame.Up) * 3f;
                    string label = $"[{i:D2}] {section.Definition.DebugName}";
                    if (section.Definition.TurnSign != 0)
                        label += $"  R={section.Definition.Radius:F0} bank={section.Definition.BankingAngle:F0}°";
                    UnityEditor.Handles.Label(labelPos, label);
                }
#endif
            }

#if UNITY_EDITOR
            if (ShowLabels)
            {
                Vector3 seedPos = toWorld.MultiplyPoint3x4(Sections[0].StartFrame.Position) + Vector3.up * 8f;
                UnityEditor.Handles.Label(seedPos, $"Seed: {Seed}   Sections: {Sections.Count}   Length: {Sections[Sections.Count - 1].EndFrame.ArcLength:F0}m");
            }
#endif
        }

        /// <summary>
        /// Draws one half-pipe cross-section profile polyline at a frame: center floor at
        /// the baseline, curved side walls rising, plus the safety lip at the top edges.
        /// </summary>
        private void DrawCrossSection(TrackConnectionFrame frame, Matrix4x4 toWorld)
        {
            int n = RoadProfile.ProfilePointCount;
            if (n < 2) return;

            float halfW = frame.Width * 0.5f;
            float sideH = frame.SideHeight > 0.001f ? frame.SideHeight : RoadProfile.SideHeight;

            Vector3 prev = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float xNorm = RoadProfile.ProfileXAt(i, n);
                // Per-side wall multipliers: suppressed inner walls (split/merge throats)
                // draw flattened, exactly matching the generated mesh.
                float mult = xNorm < 0f ? frame.LeftWallMultiplier : frame.RightWallMultiplier;
                float h = RoadProfile.HeightAt(xNorm, halfW, sideH) * mult;
                Vector3 p = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * (xNorm * halfW) + frame.Up * h);
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }

            // Safety lip markers at the top edges (scaled per side like the mesh).
            float hEdge = RoadProfile.HeightAt(1f, halfW, sideH);
            float hEdgeL = hEdge * frame.LeftWallMultiplier;
            float hEdgeR = hEdge * frame.RightWallMultiplier;
            Vector3 lipL = toWorld.MultiplyPoint3x4(frame.Position - frame.Right * halfW + frame.Up * (hEdgeL + RoadProfile.SafetyLipHeight * frame.LeftWallMultiplier));
            Vector3 lipR = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * halfW + frame.Up * (hEdgeR + RoadProfile.SafetyLipHeight * frame.RightWallMultiplier));
            Vector3 edgeL = toWorld.MultiplyPoint3x4(frame.Position - frame.Right * halfW + frame.Up * hEdgeL);
            Vector3 edgeR = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * halfW + frame.Up * hEdgeR);
            Gizmos.DrawLine(edgeL, lipL);
            Gizmos.DrawLine(edgeR, lipR);
        }

        /// <summary>
        /// One wall-mask bar at a road edge: full-height green bar = full wall (multiplier 1),
        /// short red bar = suppressed wall (open throat). Lerped in between.
        /// </summary>
        private void DrawWallMaskBar(TrackConnectionFrame frame, Matrix4x4 toWorld, float lateralOffset, float multiplier)
        {
            Vector3 basePos = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * lateralOffset);
            Vector3 up = toWorld.MultiplyVector(frame.Up);

            Gizmos.color = Color.Lerp(WallSuppressedColor, WallFullColor, multiplier);
            Gizmos.DrawLine(basePos, basePos + up * Mathf.Max(0.3f, 3f * multiplier));

            // Suppressed zones get an extra baseline dot so the open throat reads at a glance.
            if (multiplier < 0.5f)
                Gizmos.DrawSphere(basePos, 0.25f);
        }

        /// <summary>
        /// Crossbar + direction chevron marking an OPEN boundary (jump lip, landing mouth,
        /// air-gap face): no cap or wall may cross the flight path here.
        /// </summary>
        private void DrawOpenEdge(TrackConnectionFrame frame, Matrix4x4 toWorld, float forwardSign)
        {
            float halfW = frame.Width * 0.5f;
            Vector3 l = toWorld.MultiplyPoint3x4(frame.Position - frame.Right * halfW + frame.Up * 0.5f);
            Vector3 r = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * halfW + frame.Up * 0.5f);
            Vector3 mid = (l + r) * 0.5f;
            Vector3 fwd = toWorld.MultiplyVector(frame.Forward) * forwardSign;

            Gizmos.color = OpenEdgeColor;
            Gizmos.DrawLine(l, r);
            Gizmos.DrawLine(mid, mid + fwd * 4f);
            Gizmos.DrawLine(mid + fwd * 4f, mid + fwd * 2.5f + toWorld.MultiplyVector(frame.Right) * 0.8f);
            Gizmos.DrawLine(mid + fwd * 4f, mid + fwd * 2.5f - toWorld.MultiplyVector(frame.Right) * 0.8f);
        }

        private void DrawFrame(TrackConnectionFrame frame, Matrix4x4 toWorld, float size)
        {
            Vector3 p = toWorld.MultiplyPoint3x4(frame.Position);

            Gizmos.color = FrameForwardColor;
            Gizmos.DrawLine(p, p + toWorld.MultiplyVector(frame.Forward) * size);

            Gizmos.color = FrameRightColor;
            Gizmos.DrawLine(p, p + toWorld.MultiplyVector(frame.Right) * size * 0.7f);

            Gizmos.color = FrameUpColor;
            Gizmos.DrawLine(p, p + toWorld.MultiplyVector(frame.Up) * size * 0.7f);
        }
    }
}
