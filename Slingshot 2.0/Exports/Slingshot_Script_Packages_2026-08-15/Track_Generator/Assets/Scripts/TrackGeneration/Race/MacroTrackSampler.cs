using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Race
{
    /// <summary>
    /// Samples an interpolated <see cref="TrackConnectionFrame"/> at an arbitrary arc length
    /// along a generated macro track. Used to place race gates (start/finish, checkpoints)
    /// anywhere on the lap without depending on section boundaries. Arc lengths wrap, so
    /// positions past the end of the loop land back at the start.
    /// </summary>
    public static class MacroTrackSampler
    {
        /// <summary>Total lap length in meters (the last section's exit arc length).</summary>
        public static float GetTotalLength(IReadOnlyList<GeneratedTrackSection> sections)
        {
            if (sections == null || sections.Count == 0) return 0f;
            return sections[sections.Count - 1].EndFrame.ArcLength;
        }

        /// <summary>
        /// Samples the driving-line frame at the given arc length (root-local space).
        /// Arc lengths falling inside an AirGap (no rideable geometry) are nudged forward
        /// to the next section that has geometry. Returns false only when the section list
        /// is unusable.
        /// </summary>
        public static bool TrySampleFrame(IReadOnlyList<GeneratedTrackSection> sections, float arcLength, out TrackConnectionFrame frame)
        {
            frame = default;
            if (sections == null || sections.Count == 0) return false;

            float total = GetTotalLength(sections);
            if (total <= 0.01f) return false;

            float arc = Mathf.Repeat(arcLength, total);

            // At most one hop per section: every air gap is bounded by real geometry.
            for (int attempts = 0; attempts <= sections.Count; attempts++)
            {
                GeneratedTrackSection section = FindSectionAt(sections, arc);
                if (section == null) return false;

                if (section.SubdivisionFrames != null && section.SubdivisionFrames.Length >= 2)
                {
                    frame = Interpolate(section.SubdivisionFrames, arc);
                    return true;
                }

                // Air gap: skip to just past its end (wrapping around the loop).
                arc = Mathf.Repeat(section.EndFrame.ArcLength + 1f, total);
            }

            return false;
        }

        private static GeneratedTrackSection FindSectionAt(IReadOnlyList<GeneratedTrackSection> sections, float arc)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                GeneratedTrackSection s = sections[i];
                if (s == null) continue;
                // Arbitrary-arc sampling always follows the CANONICAL road — a dual
                // quarter's alternate road overlaps road A's arc range by design.
                if (s.RoadId == 1) continue;
                if (arc >= s.StartFrame.ArcLength - 0.001f && arc <= s.EndFrame.ArcLength + 0.001f)
                    return s;
            }
            return sections[sections.Count - 1];
        }

        private static TrackConnectionFrame Interpolate(TrackConnectionFrame[] frames, float arc)
        {
            if (arc <= frames[0].ArcLength) return frames[0];
            if (arc >= frames[frames.Length - 1].ArcLength) return frames[frames.Length - 1];

            int hi = 1;
            while (hi < frames.Length - 1 && frames[hi].ArcLength < arc) hi++;

            TrackConnectionFrame a = frames[hi - 1];
            TrackConnectionFrame b = frames[hi];
            float t = Mathf.InverseLerp(a.ArcLength, b.ArcLength, arc);

            Vector3 forward = Vector3.Slerp(a.Forward, b.Forward, t).normalized;
            Vector3 up = Vector3.Slerp(a.Up, b.Up, t).normalized;
            // Re-orthonormalize so the frame stays a clean basis after interpolation.
            Vector3 right = Vector3.Cross(up, forward).normalized;
            up = Vector3.Cross(forward, right).normalized;

            return new TrackConnectionFrame
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Forward = forward,
                Right = right,
                Up = up,
                Width = Mathf.Lerp(a.Width, b.Width, t),
                BankAngle = Mathf.LerpAngle(a.BankAngle, b.BankAngle, t),
                PitchAngle = Mathf.LerpAngle(a.PitchAngle, b.PitchAngle, t),
                ArcLength = arc,
                LapProgress = Mathf.Lerp(a.LapProgress, b.LapProgress, t),
                SideHeight = Mathf.Lerp(a.SideHeight, b.SideHeight, t)
            };
        }
    }
}
