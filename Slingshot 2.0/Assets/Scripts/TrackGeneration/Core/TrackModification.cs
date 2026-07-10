using System;
using UnityEngine;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Enumerates every kind of discrete modification that can be applied to a generated track.
    /// </summary>
    public enum ModificationType
    {
        /// <summary>Reposition an existing control point.</summary>
        MoveControlPoint,

        /// <summary>Change the elevation at a specific control point.</summary>
        AdjustElevation,

        /// <summary>Insert a new stunt element.</summary>
        AddStunt,

        /// <summary>Remove an existing stunt element.</summary>
        RemoveStunt,

        /// <summary>Reposition an existing stunt element.</summary>
        MoveStunt,

        /// <summary>Insert a new gravity zone.</summary>
        AddGravityZone,

        /// <summary>Remove an existing gravity zone.</summary>
        RemoveGravityZone,

        /// <summary>Change the road width at a specific section.</summary>
        AdjustRoadWidth,

        /// <summary>Add a branching shortcut path.</summary>
        AddBranch,

        /// <summary>Remove a branching shortcut path.</summary>
        RemoveBranch
    }

    /// <summary>
    /// Immutable record of a single modification applied to a track.
    /// Stored in <see cref="TrackSeed.Modifications"/> for deterministic replay.
    /// </summary>
    [Serializable]
    public struct TrackModification
    {
        /// <summary>The category of modification.</summary>
        [Tooltip("The category of modification to apply.")]
        public ModificationType Type;

        /// <summary>
        /// Index of the target element (control point, stunt, branch, etc.)
        /// that this modification affects.
        /// </summary>
        [Tooltip("Index of the target element this modification affects.")]
        public int TargetIndex;

        /// <summary>
        /// Positional or dimensional delta applied by this modification.
        /// Interpretation depends on <see cref="Type"/>.
        /// </summary>
        [Tooltip("Positional or dimensional delta applied by this modification.")]
        public Vector3 Delta;

        /// <summary>
        /// Optional JSON or other serialized payload for complex modifications
        /// that cannot be expressed with a single <see cref="Vector3"/> delta.
        /// </summary>
        [Tooltip("Optional serialized payload for complex modification data.")]
        public string SerializedData;
    }
}
