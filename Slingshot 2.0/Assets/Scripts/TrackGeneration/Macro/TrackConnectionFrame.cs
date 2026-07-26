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

        [Tooltip("Unwrapped intentional road roll. 0, 360, 720 and 1080 remain distinct.")]
        public float AccumulatedRoadRoll;

        [Tooltip("Unwrapped vertical-centerline rotation accumulated by rotational events.")]
        public float AccumulatedVerticalRotation;

        [Tooltip("Signed plan-view curvature in radians per meter.")]
        public float HorizontalCurvature;

        [Tooltip("Change of horizontal curvature per meter.")]
        public float HorizontalCurvatureRate;

        [Tooltip("Signed vertical curvature in radians per meter.")]
        public float VerticalCurvature;

        [Tooltip("Change of vertical curvature per meter.")]
        public float VerticalCurvatureRate;

        [Tooltip("Unwrapped intentional road-roll rate in degrees per meter.")]
        public float RoadRollRate;

        [Tooltip("Change of road-roll rate in degrees per square meter.")]
        public float RoadRollAcceleration;

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

        [Tooltip("Dynamic turn rounding 0..1: 0 = configured center-flat ratio, 1 = fully rounded bowl (flat center gone). Set by the global turn-rounding field.")]
        public float TurnRounding;

        [Tooltip("LEFT wall overhang engagement 0..1: 0 = ordinary wall (safety-lip curl only), 1 = full catch-wall curl past vertical. Set by the catch-wall/wallride passes.")]
        public float LeftOverhang;

        [Tooltip("RIGHT wall overhang engagement 0..1.")]
        public float RightOverhang;

        [Tooltip("Pipe closure 0..1: 0 = open half-pipe road, 1 = fully closed tube. Set by full-pipe feature sections; the cross-section morphs smoothly between them.")]
        public float PipeClosure;

        [Tooltip("Wallride morph 0..1: 0 = ordinary road, 1 = the cross-section IS the outside wall only (no flat center, no inside wall). Set by the wallride builder; the riding side is the side with the higher overhang engagement.")]
        public float WallrideMorph;

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
