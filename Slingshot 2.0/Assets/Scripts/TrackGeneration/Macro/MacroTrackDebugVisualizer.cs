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

        /// <summary>Called by the generator after a successful macro generation.</summary>
        public void Initialize(int seed, List<GeneratedTrackSection> sections, TrackRoadProfileSettings roadProfile = null)
        {
            Seed = seed;
            Sections = sections;
            RoadProfile = roadProfile;
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
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += 8)
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
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += 12)
                    {
                        DrawCrossSection(section.SubdivisionFrames[f], toWorld);
                    }
                }

                if (ShowBlendZones && section.SubdivisionFrames != null)
                {
                    // Bank angle (orange) and half-pipe depth (green) over distance — makes
                    // blend zones and any abrupt transitions immediately visible.
                    for (int f = 0; f < section.SubdivisionFrames.Length; f += 4)
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
                float xNorm = -1f + 2f * i / (n - 1);
                float h = RoadProfile.HeightAt(xNorm, halfW, sideH);
                Vector3 p = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * (xNorm * halfW) + frame.Up * h);
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }

            // Safety lip markers at the top edges.
            float hEdge = RoadProfile.HeightAt(1f, halfW, sideH);
            Vector3 lipL = toWorld.MultiplyPoint3x4(frame.Position - frame.Right * halfW + frame.Up * (hEdge + RoadProfile.SafetyLipHeight));
            Vector3 lipR = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * halfW + frame.Up * (hEdge + RoadProfile.SafetyLipHeight));
            Vector3 edgeL = toWorld.MultiplyPoint3x4(frame.Position - frame.Right * halfW + frame.Up * hEdge);
            Vector3 edgeR = toWorld.MultiplyPoint3x4(frame.Position + frame.Right * halfW + frame.Up * hEdge);
            Gizmos.DrawLine(edgeL, lipL);
            Gizmos.DrawLine(edgeR, lipR);
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
