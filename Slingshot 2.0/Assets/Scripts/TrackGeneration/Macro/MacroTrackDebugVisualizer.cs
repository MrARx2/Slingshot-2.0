using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// How much scene-view debug drawing a generated track performs. Higher levels
    /// draw more per-sample detail; the per-lap displayed-sample cap always applies.
    /// </summary>
    public enum TrackDebugVisualizationLevel
    {
        Off,
        Summary,   // quarter boundaries + road identity
        Normal,    // + open edges, jump targets, turn direction, pattern/section labels
        Detailed,  // + banking, cross-sections, blend zones, wall masks, lap progress, closure
        Full       // + every boundary frame axis
    }

    /// <summary>
    /// Scene-view debug visualization for a generated macro track. Added to the track root
    /// by <see cref="TrackGeneration.TrackGenerator"/> so a generated track stays inspectable:
    /// section labels, entry/exit frames, banking and turn direction, jump landing targets,
    /// section bounds, seed value, and the full generated section list (in the inspector).
    /// </summary>
    [AddComponentMenu("Track Generation/Macro Track Debug Visualizer")]
    public class MacroTrackDebugVisualizer : MonoBehaviour
    {
        [Header("Visualization Level")]
        [Tooltip("Master detail level. Individual toggles below still apply within the level; the sample cap always applies. Editor responsiveness first: Summary/Normal for everyday work.")]
        public TrackDebugVisualizationLevel Level = TrackDebugVisualizationLevel.Normal;

        [Tooltip("Hard cap on displayed per-sample gizmos across the whole lap — per-section strides scale up to honor it on huge tracks.")]
        [Range(200, 20000)] public int MaxDisplayedSamples = 1500;

        [Tooltip("Scene labels draw only within this distance of the scene camera (thousands of Handles.Label calls stall the editor).")]
        [Range(100f, 10000f)] public float LabelDrawDistance = 1500f;
        [Header("Track Data (Read Only)")]
        [Tooltip("Seed the layout was generated from.")]
        public int Seed;

        // Serialized so the track survives scene reload/play-mode adoption, but HIDDEN:
        // default-inspector traversal of tens of thousands of ring frames would stall
        // the editor whenever this component is selected.
        [HideInInspector]
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

        [Tooltip("Draw per-side wall multipliers and highlight suppressed inner-wall zones.")]
        public bool ShowWallMasks = true;

        [Tooltip("Mark OPEN boundaries: jump lip, landing mouth, and air-gap edges where no cap/wall may cross the flight path.")]
        public bool ShowOpenEdges = true;

        [Header("Quarter / Pattern / Progress Debug")]
        [Tooltip("Label feature-pattern groups (Loop→Corkscrew, HalfLoopToCorkscrew, …).")]
        public bool ShowPatternLabels = true;

        [Tooltip("Mark logical 90-degree control stations inside continuous rotational events.")]
        public bool ShowRotationalControlStations = true;

        [Tooltip("Draw the 4 quarter boundaries with their type labels (SingleRoad / DualRoad).")]
        public bool ShowQuarterBoundaries = true;

        [Tooltip("Color dual-quarter road centerlines per road (A cyan / B orange).")]
        public bool ShowRouteColors = true;

        [Tooltip("Display the estimated road-time table of each dual quarter.")]
        public bool ShowRouteTimes = true;

        [Tooltip("Mark logical lap progress every 10% along the driving line.")]
        public bool ShowLapProgress = false;

        [Tooltip("Highlight closure-reserve sections (adjustable straights the closure solver used).")]
        public bool ShowClosureSections = false;

        [Tooltip("The authoritative quarter records of the generated lap (filled by the generator).")]
        public List<TrackGeneration.Planning.GeneratedTrackQuarter> Quarters = new List<TrackGeneration.Planning.GeneratedTrackQuarter>();

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

        /// <summary>Stores the quarter records from the authoritative layout for scene display.</summary>
        public void SetLayout(TrackGeneration.Planning.GeneratedTrackLayout layout)
        {
            Quarters.Clear();
            if (layout == null) return;
            Quarters.AddRange(layout.Quarters);
        }

        /// <summary>Extra stride multiplier that keeps the WHOLE lap under MaxDisplayedSamples.</summary>
        private float _strideBoost = 1f;

        private void UpdateStrideBoost()
        {
            float totalArc = Sections.Count > 0 ? Sections[Sections.Count - 1].EndFrame.ArcLength : 0f;
            // The densest per-feature stride targets ~10 m between gizmo samples.
            _strideBoost = Mathf.Max(1f, totalArc / 10f / Mathf.Max(200, MaxDisplayedSamples));
        }

        /// <summary>True when a scene label at this world position is close enough to draw.</summary>
        private bool LabelVisible(Vector3 worldPos)
        {
            var cam = Camera.current;
            if (cam == null) return true;
            return (cam.transform.position - worldPos).sqrMagnitude <= LabelDrawDistance * LabelDrawDistance;
        }

        /// <summary>
        /// Gizmo stride in RING INDICES for a target arc distance. Strides must be
        /// arc-based, never fixed indices: ring density is a mesh-quality setting, and a
        /// fixed index stride multiplies the gizmo line count (and tanks editor FPS on
        /// big tracks) every time the density is raised.
        /// </summary>
        private int StrideFor(GeneratedTrackSection sec, float targetMeters)
        {
            float meters = targetMeters * _strideBoost;
            int rings = sec.SubdivisionFrames?.Length ?? 0;
            if (rings < 2) return 1;
            float ds = (sec.EndFrame.ArcLength - sec.StartFrame.ArcLength) / (rings - 1);
            return Mathf.Max(1, Mathf.RoundToInt(meters / Mathf.Max(0.1f, ds)));
        }

        private void OnDrawGizmos()
        {
            if (Level == TrackDebugVisualizationLevel.Off) return;
            if (Sections == null || Sections.Count == 0) return;

            UpdateStrideBoost();

            // Level gates: a toggle draws only when its detail tier is active.
            bool tierNormal = Level >= TrackDebugVisualizationLevel.Normal;
            bool tierDetailed = Level >= TrackDebugVisualizationLevel.Detailed;
            bool tierFull = Level >= TrackDebugVisualizationLevel.Full;

            bool showFrames = ShowFrames && tierFull;
            bool showBounds = ShowBounds && tierDetailed;
            bool showTurnDirection = ShowTurnDirection && tierNormal;
            bool showBanking = ShowBanking && tierDetailed;
            bool showCrossSection = ShowCrossSection && tierDetailed;
            bool showBlendZones = ShowBlendZones && tierDetailed;
            bool showWallMasks = ShowWallMasks && tierDetailed;
            bool showOpenEdges = ShowOpenEdges && tierNormal;
            bool showClosure = ShowClosureSections && tierDetailed;
            bool showLapProgress = ShowLapProgress && tierDetailed;
            bool showPatternLabels = ShowPatternLabels && tierNormal;
            bool showJumpTargets = ShowJumpTargets && tierNormal;
            bool showSectionLabels = ShowLabels && tierNormal;

            Matrix4x4 toWorld = transform.localToWorldMatrix;

            DrawQuarterBoundaries(toWorld);

            for (int i = 0; i < Sections.Count; i++)
            {
                var section = Sections[i];
                if (section?.Definition == null) continue;

                Vector3 startPos = toWorld.MultiplyPoint3x4(section.StartFrame.Position);
                Vector3 endPos = toWorld.MultiplyPoint3x4(section.EndFrame.Position);

                if (showFrames)
                {
                    DrawFrame(section.StartFrame, toWorld, 4f);
                    DrawFrame(section.EndFrame, toWorld, 2.5f);
                }

                if (showBounds)
                {
                    Gizmos.color = BoundsColor;
                    Bounds b = section.SectionBounds;
                    Gizmos.matrix = toWorld;
                    Gizmos.DrawWireCube(b.center, b.size);
                    Gizmos.matrix = Matrix4x4.identity;
                }

                if (showTurnDirection && section.Definition.TurnSign != 0 &&
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

                if (showBanking && section.SubdivisionFrames != null)
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

                if (showCrossSection && RoadProfile != null && RoadProfile.IsHalfPipe &&
                    section.SubdivisionFrames != null)
                {
                    // Half-pipe cross-section polylines: center baseline + rising side walls.
                    Gizmos.color = CrossSectionColor;
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += StrideFor(section, 80f))
                    {
                        DrawCrossSection(section.SubdivisionFrames[f], toWorld);
                    }
                }

                if (showBlendZones && section.SubdivisionFrames != null)
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

                if (showWallMasks && section.SubdivisionFrames != null && section.QuarterIndex >= 0 &&
                    section.RoadId == 1)
                {
                    // Per-side wall multiplier bars at each road edge of the alternate
                    // road: green = full wall, red = suppressed. Bar height tracks the
                    // multiplier, so wall fades are directly visible in the scene.
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += StrideFor(section, 12f))
                    {
                        var fr = section.SubdivisionFrames[f];
                        float halfW = fr.Width * 0.5f;

                        DrawWallMaskBar(fr, toWorld, -halfW, fr.LeftWallMultiplier);
                        DrawWallMaskBar(fr, toWorld, halfW, fr.RightWallMultiplier);
                    }
                }

                if (showOpenEdges)
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

                bool inDualQuarter = section.QuarterIndex >= 0 && section.QuarterIndex < Quarters.Count &&
                                     Quarters[section.QuarterIndex] != null &&
                                     Quarters[section.QuarterIndex].QuarterType == TrackGeneration.Design.TrackQuarterType.DualRoad;
                if (ShowRouteColors && inDualQuarter /* Summary+ */ && section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 1)
                {
                    // Distinct road colors: A cyan, B orange.
                    Gizmos.color = section.RoadId == 0 ? new Color(0.2f, 0.9f, 1f) : new Color(1f, 0.6f, 0.15f);
                    int routeStride = StrideFor(section, 10f);
                    for (int f = routeStride; f < section.SubdivisionFrames.Length; f += routeStride)
                    {
                        Gizmos.DrawLine(
                            toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f - routeStride].Position + section.SubdivisionFrames[f - routeStride].Up),
                            toWorld.MultiplyPoint3x4(section.SubdivisionFrames[f].Position + section.SubdivisionFrames[f].Up));
                    }
                }

                if (showClosure && section.Definition.IsClosure && section.SubdivisionFrames != null)
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

                if (showLapProgress && section.SubdivisionFrames != null)
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

                if (ShowRotationalControlStations &&
                    section.Definition.SectionType == TrackMacroSectionType.RotationalEvent &&
                    section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 1)
                {
                    float startVertical = section.SubdivisionFrames[0].AccumulatedVerticalRotation;
                    float startRoll = section.SubdivisionFrames[0].AccumulatedRoadRoll;
                    int previousStation = 0;
                    for (int f = 1; f < section.SubdivisionFrames.Length; f++)
                    {
                        var fr = section.SubdivisionFrames[f];
                        int station = Mathf.Max(
                            Mathf.FloorToInt(Mathf.Abs(fr.AccumulatedVerticalRotation - startVertical) / 90f + 0.001f),
                            Mathf.FloorToInt(Mathf.Abs(fr.AccumulatedRoadRoll - startRoll) / 90f + 0.001f));
                        if (station <= previousStation) continue;
                        previousStation = station;
                        Vector3 p = toWorld.MultiplyPoint3x4(fr.Position);
                        Gizmos.color = new Color(1f, 0.25f, 1f, 0.9f);
                        Gizmos.DrawWireSphere(p, 2f);
#if UNITY_EDITOR
                        if (LabelVisible(p)) UnityEditor.Handles.Label(p + Vector3.up * 3f, $"{station}u");
#endif
                    }
                }

#if UNITY_EDITOR
                if (showPatternLabels && !string.IsNullOrEmpty(section.Definition.PatternId) &&
                    section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 2)
                {
                    // Label once per pattern: only on the first section of the group.
                    bool firstOfPattern = i == 0 || Sections[i - 1]?.Definition == null ||
                                          Sections[i - 1].Definition.PatternId != section.Definition.PatternId;
                    if (firstOfPattern)
                    {
                        var mid = section.SubdivisionFrames[section.SubdivisionFrames.Length / 2];
                        Vector3 labelWorld = toWorld.MultiplyPoint3x4(mid.Position) + Vector3.up * 10f;
                        if (LabelVisible(labelWorld))
                            UnityEditor.Handles.Label(labelWorld, $"◆ {section.Definition.PatternId}");
                    }
                }
#endif

                if (showJumpTargets && section.Definition.SectionType == TrackMacroSectionType.AirGap)
                {
                    Gizmos.color = JumpTargetColor;
                    Gizmos.DrawLine(startPos, endPos);
                    Gizmos.DrawWireSphere(endPos, 1.5f);
                }

#if UNITY_EDITOR
                if (showSectionLabels)
                {
                    Vector3 labelPos = startPos + toWorld.MultiplyVector(section.StartFrame.Up) * 3f;
                    if (LabelVisible(labelPos))
                    {
                        string label = $"[{i:D2}] {section.Definition.DebugName}";
                        if (section.Definition.TurnSign != 0)
                            label += $"  R={section.Definition.Radius:F0} bank={section.Definition.BankingAngle:F0}°";
                        UnityEditor.Handles.Label(labelPos, label);
                    }
                }
#endif
            }

#if UNITY_EDITOR
            if (showSectionLabels)
            {
                Vector3 seedPos = toWorld.MultiplyPoint3x4(Sections[0].StartFrame.Position) + Vector3.up * 8f;
                if (LabelVisible(seedPos))
                    UnityEditor.Handles.Label(seedPos, $"Seed: {Seed}   Sections: {Sections.Count}   Length: {Sections[Sections.Count - 1].EndFrame.ArcLength:F0}m");
            }
#endif
        }

        // Scratch buffer for the parametric cross-section polyline.
        private Vector2[] _profileScratch;

        /// <summary>
        /// Draws one cross-section profile polyline at a frame using THE SAME
        /// parametric chain as the mesh and collider (turn rounding, catch-wall curls,
        /// wallride support and pipe closure all draw exactly as they will build).
        /// </summary>
        private void DrawCrossSection(TrackConnectionFrame frame, Matrix4x4 toWorld)
        {
            int n = TrackCrossSection.PointCount(RoadProfile);
            if (n < 2) return;
            if (_profileScratch == null || _profileScratch.Length < n)
                _profileScratch = new Vector2[n];

            TrackCrossSection.Evaluate(RoadProfile, frame, _profileScratch);

            Vector3 prev = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = toWorld.MultiplyPoint3x4(frame.Position
                    + frame.Right * _profileScratch[i].x + frame.Up * _profileScratch[i].y);
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        /// <summary>
        /// The 4 quarter boundaries: a crossbar + label at each quarter's entry frame,
        /// plus the estimated road-time table on dual quarters.
        /// </summary>
        private void DrawQuarterBoundaries(Matrix4x4 toWorld)
        {
            if (!ShowQuarterBoundaries || Quarters == null || Quarters.Count == 0) return;

            foreach (var q in Quarters)
            {
                if (q == null || q.RouteA == null) continue;
                var f = q.EntryFrame;
                Vector3 p = toWorld.MultiplyPoint3x4(f.Position);
                Vector3 right = toWorld.MultiplyVector(f.Right);
                float halfW = Mathf.Max(6f, f.Width * 0.75f);

                bool dual = q.QuarterType == TrackGeneration.Design.TrackQuarterType.DualRoad;
                Gizmos.color = dual ? new Color(1f, 0.55f, 0.1f) : new Color(0.35f, 0.85f, 1f);
                Gizmos.DrawLine(p - right * halfW, p + right * halfW);
                Gizmos.DrawLine(p - right * halfW, p - right * halfW + Vector3.up * 10f);
                Gizmos.DrawLine(p + right * halfW, p + right * halfW + Vector3.up * 10f);

#if UNITY_EDITOR
                if (LabelVisible(p))
                {
                    UnityEditor.Handles.Label(p + Vector3.up * 12f,
                        $"Q{q.QuarterIndex + 1} {(dual ? "DUAL ROAD" : "SingleRoad")} " +
                        $"({q.LogicalProgressStart:P0}–{q.LogicalProgressEnd:P0})");
                    if (dual && ShowRouteTimes && q.Balance != null)
                        UnityEditor.Handles.Label(p + Vector3.up * 16f, q.Balance.ToString());
                }
#endif
            }
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
