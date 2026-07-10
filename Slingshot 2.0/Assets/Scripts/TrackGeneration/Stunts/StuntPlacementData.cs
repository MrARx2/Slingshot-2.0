using UnityEngine;
using System;

namespace TrackGeneration.Stunts
{
    /// <summary>
    /// The type of stunt feature that can be placed on the track.
    /// </summary>
    public enum StuntType
    {
        /// <summary>Two vertical walls replacing the road surface. Hovercraft must wall-ride between them.</summary>
        WallRide,

        /// <summary>A launch ramp + air gap + landing ramp chain that sends the hovercraft airborne.</summary>
        Ramp,

        /// <summary>A boost pad on the road surface. Not counted against the stunt budget.</summary>
        BoostPad
    }

    /// <summary>
    /// Difficulty tier for a stunt, affecting geometry scale and gameplay demands.
    /// </summary>
    public enum StuntDifficulty
    {
        /// <summary>Gentle geometry, forgiving timing.</summary>
        Easy,

        /// <summary>Moderate challenge, standard geometry.</summary>
        Medium,

        /// <summary>Aggressive geometry, tight timing, more rewarding.</summary>
        Hard
    }

    /// <summary>
    /// Data describing where and how a stunt is placed on the track.
    /// Produced by <see cref="StuntPlacer"/> and consumed by stunt actor generation.
    /// </summary>
    [Serializable]
    public struct StuntPlacement
    {
        [Tooltip("Type of stunt (WallRide or Ramp).")]
        public StuntType Type;

        [Tooltip("Index of the spline this stunt is on. 0 = main circuit, 1+ = shortcut index.")]
        public int SplineIndex;

        [Tooltip("Normalized position along the spline (0-1) where the stunt begins.")]
        [Range(0f, 1f)]
        public float T;

        [Tooltip("Length of the stunt section along the track (meters).")]
        public float Length;

        [Tooltip("Lane placement. 0 = center/full width, 1 = left lane, 2 = right lane.")]
        public int Lane;

        [Tooltip("Difficulty tier of this stunt.")]
        public StuntDifficulty Difficulty;

        [Tooltip("Ramp only: peak height of the launch lip (meters).")]
        public float Height;

        [Tooltip("Ramp only: empty air distance between launch lip and landing ramp (meters).")]
        public float AirGapLength;

        [Tooltip("Ramp only: length of the landing/recovery ramp (meters).")]
        public float LandingLength;

        /// <summary>Ramp only: total footprint along the road = launch + air gap + landing.</summary>
        public float TotalFootprint => Length + AirGapLength + LandingLength;

        /// <summary>Whether this stunt spans the full road width.</summary>
        public bool IsFullWidth => Lane == 0;

        /// <summary>Whether this stunt is on a shortcut (not the main circuit).</summary>
        public bool IsOnShortcut => SplineIndex > 0;

        public override string ToString()
        {
            string splineName = SplineIndex == 0 ? "Main" : $"Shortcut_{SplineIndex}";
            return $"[{Type}] {splineName} @ T={T:F3}, Lane={Lane}, Diff={Difficulty}, Len={Length:F1}m";
        }
    }

}
