using System.Collections.Generic;
using Unity.Mathematics;
using TrackGeneration.Core;

namespace TrackGeneration.Splines
{
    /// <summary>
    /// Helper for keeping stunts and gravity zones out of shortcut merge zones — the entrances and
    /// exits where a shortcut joins the main road. Placing a wall-ride or ramp there would block the
    /// connection, so those parameter ranges are treated as exclusion zones.
    /// </summary>
    public static class MergeZoneUtility
    {
        /// <summary>
        /// Returns true if normalized parameter <paramref name="t"/> on the given spline lies inside a
        /// shortcut merge zone (a shortcut's own entrance/exit for shortcut splines, or any main-road
        /// junction for the main circuit) and should therefore stay clear of stunts/gravity zones.
        /// </summary>
        /// <param name="splineIndex">0 for the main circuit; 1-based index for shortcuts.</param>
        /// <param name="t">Normalized parameter on that spline.</param>
        /// <param name="splineLength">Arc length of that spline (meters).</param>
        /// <param name="shortcuts">All shortcut branches (used for main-circuit junctions).</param>
        /// <param name="mainLength">Arc length of the main circuit (meters).</param>
        /// <param name="mergeDistance">Configured shortcut merge distance (meters).</param>
        public static bool IsInMergeZone(int splineIndex, float t, float splineLength,
            List<TrackBranch> shortcuts, float mainLength, float mergeDistance)
        {
            // A little extra margin beyond the mesh taper so the stunt body never clips the opening.
            float margin = mergeDistance * 1.5f;

            if (splineIndex == 0)
            {
                if (shortcuts == null || mainLength <= 1f) return false;
                float mT = margin / mainLength;
                foreach (var b in shortcuts)
                {
                    if (NearWrapped(t, b.MainStartT, mT)) return true;
                    if (NearWrapped(t, b.MainEndT, mT)) return true;
                }
                return false;
            }

            if (splineLength <= 1f) return false;
            float endMargin = margin / splineLength;
            return t < endMargin || t > 1f - endMargin;
        }

        // Distance between two normalized parameters, accounting for wrap-around on a closed loop.
        private static bool NearWrapped(float t, float c, float m)
        {
            float d = math.abs(t - c);
            d = math.min(d, 1f - d);
            return d < m;
        }
    }
}
