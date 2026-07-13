using System;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// A clean entry/exit frame for a macro section, and the per-subdivision ring frame.
    /// The contract that lets pieces connect without gaps: a section's exit frame IS the
    /// next section's entry frame, so consecutive meshes share identical ring geometry.
    /// All values are in track-root local space (move the root, move the track).
    /// </summary>
    [Serializable]
    public struct TrackConnectionFrame
    {
        [Tooltip("Position on the driving line (root-local).")]
        public Vector3 Position;

        [Tooltip("Travel direction (unit).")]
        public Vector3 Forward;

        [Tooltip("Cross-track direction (unit). Banked/pitched frames bake the tilt in here.")]
        public Vector3 Right;

        [Tooltip("Surface normal (unit). Banked/pitched frames bake the tilt in here.")]
        public Vector3 Up;

        [Tooltip("Road width at this frame in meters.")]
        public float Width;

        [Tooltip("Bank (roll) angle in degrees. Positive banks into a right turn.")]
        public float BankAngle;

        [Tooltip("Pitch angle in degrees. Positive = nose up.")]
        public float PitchAngle;

        [Tooltip("Accumulated arc length from track start (meters). Keeps UVs continuous across sections. Inside a branch route this is the ROUTE distance — physical distance along that route.")]
        public float ArcLength;

        [Tooltip("Logical race progress 0..1. Both branch routes share the same progress at their entry and merge gates regardless of physical length; monotonic within a route.")]
        public float LapProgress;

        [Tooltip("Half-pipe side height at this frame (meters). Set by the cross-section blend pass; the mesh builder reads it so depth changes blend smoothly.")]
        public float SideHeight;

        [Tooltip("Suppression of the LEFT half-pipe wall. 0 = full wall (default), 1 = fully open, NEGATIVE = boosted wall (bobsled-banked outside wall). Stored as suppression so default-constructed frames keep full walls. Set by the wall-mask pass (splits/merges) and the corner banking pass.")]
        public float LeftWallSuppression;

        [Tooltip("Suppression of the RIGHT half-pipe wall. 0 = full wall (default), 1 = fully open, negative = boosted.")]
        public float RightWallSuppression;

        /// <summary>Left wall height multiplier (1 = full half-pipe wall, 0 = open edge, above 1 = banked outside wall).</summary>
        public float LeftWallMultiplier => Mathf.Clamp(1f - LeftWallSuppression, 0f, 3f);

        /// <summary>Right wall height multiplier (1 = full half-pipe wall, 0 = open edge, above 1 = banked outside wall).</summary>
        public float RightWallMultiplier => Mathf.Clamp(1f - RightWallSuppression, 0f, 3f);

        /// <summary>An identity frame at the origin facing +Z with the given width.</summary>
        public static TrackConnectionFrame Origin(float width)
        {
            return new TrackConnectionFrame
            {
                Position = Vector3.zero,
                Forward = Vector3.forward,
                Right = Vector3.right,
                Up = Vector3.up,
                Width = width,
                BankAngle = 0f,
                PitchAngle = 0f,
                ArcLength = 0f,
                SideHeight = 0f
            };
        }
    }
}
