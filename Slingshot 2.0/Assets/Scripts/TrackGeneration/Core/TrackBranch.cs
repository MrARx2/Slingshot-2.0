using UnityEngine.Splines;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Represents a generated shortcut branch of the main track.
    /// </summary>
    public class TrackBranch
    {
        /// <summary>The spline path of this branch.</summary>
        public SplineContainer Spline { get; set; }

        /// <summary>The length of this branch in meters.</summary>
        public float Length { get; set; }

        /// <summary>The T parameter on the main circuit where this branch begins (0-1).</summary>
        public float MainStartT { get; set; }

        /// <summary>The T parameter on the main circuit where this branch merges back (0-1).</summary>
        public float MainEndT { get; set; }

        /// <summary>Whether this branch is considered "hard" (e.g. fewer lanes, more stunts).</summary>
        public bool IsHardDifficulty { get; set; }

        /// <summary>Sign (+1 for right, -1 for left) indicating which side the shortcut peels off from at the start.</summary>
        public float StartSideSign { get; set; }

        /// <summary>Sign (+1 for right, -1 for left) indicating which side the shortcut merges back into at the end.</summary>
        public float EndSideSign { get; set; }

        /// <summary>The world up vector of the main track where this branch begins (for banking alignment).</summary>
        public Unity.Mathematics.float3 StartUp { get; set; }

        /// <summary>The world up vector of the main track where this branch merges back (for banking alignment).</summary>
        public Unity.Mathematics.float3 EndUp { get; set; }
    }
}
