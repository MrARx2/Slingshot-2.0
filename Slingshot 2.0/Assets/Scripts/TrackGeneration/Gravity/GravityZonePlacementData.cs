using System;
using UnityEngine;

namespace TrackGeneration.Gravity
{
    /// <summary>
    /// Data describing where and how a gravity zone is placed on the track.
    /// </summary>
    [Serializable]
    public struct GravityZonePlacement
    {
        [Tooltip("Type of gravity zone (Centering = invisible road, Orbital = orbit emitter).")]
        public GravityZoneType Type;

        [Tooltip("Index of the spline this zone is on. 0 = main circuit, 1+ = shortcut index.")]
        public int SplineIndex;

        [Tooltip("Normalized position along the spline (0-1) where the zone begins.")]
        [Range(0f, 1f)]
        public float T;

        [Tooltip("Length of the gravity zone along the track (meters).")]
        public float Length;

        [Tooltip("Gravity field strength for emitters in this zone.")]
        public float Strength;

        [Tooltip("Gravity field influence radius for emitters in this zone.")]
        public float Radius;

        /// <summary>Whether this zone is on a shortcut.</summary>
        public bool IsOnShortcut => SplineIndex > 0;

        public override string ToString()
        {
            string splineName = SplineIndex == 0 ? "Main" : $"Shortcut_{SplineIndex}";
            return $"[{Type}] {splineName} @ T={T:F3}, Len={Length:F1}m, Str={Strength:F0}";
        }
    }
}
