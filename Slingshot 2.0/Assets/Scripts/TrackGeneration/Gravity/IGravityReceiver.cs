using UnityEngine;

namespace TrackGeneration.Gravity
{
    /// <summary>
    /// Defines the type of gravitational force a <see cref="GravityField"/> emits.
    /// </summary>
    public enum GravityFieldType
    {
        /// <summary>Pulls objects toward the emitter center.</summary>
        Attractor,

        /// <summary>Pushes objects away from the emitter center.</summary>
        Repulsor,

        /// <summary>Applies force along a fixed world-space direction.</summary>
        Directional
    }

    /// <summary>
    /// Defines the operational mode of a <see cref="GravityZone"/>.
    /// </summary>
    public enum GravityZoneType
    {
        /// <summary>
        /// Two emitters on opposite sides of the road create a centering force.
        /// Acts as an "invisible road" — no physical surface needed.
        /// </summary>
        Centering,

        /// <summary>
        /// A single emitter that causes the hovercraft to orbit it
        /// until the player activates the gravity-release ability.
        /// </summary>
        Orbital
    }

    /// <summary>
    /// Contract that any object affected by gravity zones must implement.
    /// The hovercraft controller (imported from external project) will implement this.
    /// Gravity zones query all IGravityReceiver instances in their trigger volume.
    /// </summary>
    public interface IGravityReceiver
    {
        /// <summary>The Rigidbody component receiving forces.</summary>
        Rigidbody ReceiverRigidbody { get; }

        /// <summary>Mass of the receiver in kg.</summary>
        float Mass { get; }

        /// <summary>Current world-space position.</summary>
        Vector3 Position { get; }

        /// <summary>Current world-space velocity.</summary>
        Vector3 Velocity { get; }

        /// <summary>
        /// Called by <see cref="GravityField"/> each FixedUpdate while the receiver is in range.
        /// </summary>
        /// <param name="force">The gravitational force vector to apply.</param>
        /// <param name="sourceType">The type of gravity field applying the force.</param>
        void ApplyGravityForce(Vector3 force, GravityFieldType sourceType);

        /// <summary>
        /// Player ability: when true, the receiver is releasing itself from gravitational pull.
        /// Used in <see cref="GravityZoneType.Orbital"/> mode to escape orbit.
        /// </summary>
        bool IsReleasingGravity { get; }
    }
}
