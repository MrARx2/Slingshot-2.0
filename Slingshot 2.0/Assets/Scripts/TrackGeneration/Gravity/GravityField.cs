using UnityEngine;
using System.Collections.Generic;

namespace TrackGeneration.Gravity
{
    /// <summary>
    /// A single gravity emitter that applies gravitational force to nearby <see cref="IGravityReceiver"/> objects.
    /// Place one on each side of the road for centering (invisible road) behavior,
    /// or use a single one for orbital behavior.
    /// </summary>
    [AddComponentMenu("Track Generation/Gravity/Gravity Field")]
    public class GravityField : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Force Properties
        // ──────────────────────────────────────────────

        [Header("Force Properties")]
        [Tooltip("The base magnitude of the gravitational force (Newtons at 1m distance).")]
        [SerializeField, Range(1f, 500f)]
        private float fieldStrength = 50f;

        [Tooltip("Maximum radius of influence (meters). Objects beyond this distance are unaffected.")]
        [SerializeField, Range(1f, 200f)]
        private float fieldRadius = 30f;

        [Tooltip("Exponent for distance-based falloff. 2 = inverse-square (realistic). Lower = gentler falloff.")]
        [SerializeField, Range(0.5f, 4f)]
        private float falloffExponent = 2f;

        [Tooltip("Optional: custom falloff curve overriding the exponent. X = normalized distance (0=center, 1=radius edge), Y = force multiplier (0-1). Leave null to use exponent-based falloff.")]
        [SerializeField]
        private AnimationCurve falloffCurve;

        // ──────────────────────────────────────────────
        //  Behavior
        // ──────────────────────────────────────────────

        [Header("Behavior")]
        [Tooltip("The type of gravitational force: Attractor pulls toward center, Repulsor pushes away, Directional applies constant direction.")]
        [SerializeField]
        private GravityFieldType fieldType = GravityFieldType.Attractor;

        [Tooltip("Whether this field is currently active and applying forces.")]
        [SerializeField]
        private bool isActive = true;

        [Tooltip("Whether the player can use the 'release gravitational pull' ability to escape this field.")]
        [SerializeField]
        private bool canBeReleasedByPlayer = true;

        [Tooltip("For Directional type: the world-space direction of the force.")]
        [SerializeField]
        private Vector3 directionalForceDirection = Vector3.down;

        // ──────────────────────────────────────────────
        //  Visual Feedback
        // ──────────────────────────────────────────────

        [Header("Visual Feedback")]
        // [Tooltip("Speed of the visual pulse effect (cycles per second).")]
        // [SerializeField, Range(0.1f, 5f)]
        // private float visualPulseSpeed = 1f;

        [Tooltip("Color used for debug gizmos and VFX tinting.")]
        [SerializeField]
        private Color fieldColor = new Color(0f, 0.8f, 1f, 1f); // Cyan

        [Tooltip("Optional particle system for visualizing the gravity influence area.")]
        [SerializeField]
        private ParticleSystem fieldParticles;

        // ──────────────────────────────────────────────
        //  Runtime State (not serialized)
        // ──────────────────────────────────────────────

        private readonly List<IGravityReceiver> _receiversInRange = new List<IGravityReceiver>();
        private SphereCollider _triggerCollider;

        // ──────────────────────────────────────────────
        //  Public Properties (Inspector-friendly read access)
        // ──────────────────────────────────────────────

        /// <summary>The base magnitude of the gravitational force.</summary>
        public float FieldStrength
        {
            get => fieldStrength;
            set => fieldStrength = Mathf.Clamp(value, 1f, 500f);
        }

        /// <summary>Maximum radius of influence in meters.</summary>
        public float FieldRadius
        {
            get => fieldRadius;
            set
            {
                fieldRadius = Mathf.Clamp(value, 1f, 200f);
                if (_triggerCollider != null) _triggerCollider.radius = fieldRadius;
            }
        }

        /// <summary>The type of gravitational force this field emits.</summary>
        public GravityFieldType FieldType
        {
            get => fieldType;
            set => fieldType = value;
        }

        /// <summary>Whether this field is currently active.</summary>
        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        /// <summary>Whether the player can release from this field's pull.</summary>
        public bool CanBeReleasedByPlayer => canBeReleasedByPlayer;

        /// <summary>Color used for gizmos and VFX.</summary>
        public Color FieldColor => fieldColor;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            EnsureTriggerCollider();
        }

        private void FixedUpdate()
        {
            if (!isActive) return;

            for (int i = _receiversInRange.Count - 1; i >= 0; i--)
            {
                var receiver = _receiversInRange[i];

                // Null check for destroyed objects
                if (receiver == null || (receiver is MonoBehaviour mb && mb == null))
                {
                    _receiversInRange.RemoveAt(i);
                    continue;
                }

                // Skip if player is releasing gravity and this field allows it
                if (canBeReleasedByPlayer && receiver.IsReleasingGravity)
                    continue;

                Vector3 force = ComputeForce(receiver.Position, receiver.Mass);
                receiver.ApplyGravityForce(force, fieldType);
            }
        }

        // ──────────────────────────────────────────────
        //  Trigger Management
        // ──────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<IGravityReceiver>(out var receiver))
            {
                if (!_receiversInRange.Contains(receiver))
                    _receiversInRange.Add(receiver);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent<IGravityReceiver>(out var receiver))
            {
                _receiversInRange.Remove(receiver);
            }
        }

        // ──────────────────────────────────────────────
        //  Force Computation
        // ──────────────────────────────────────────────

        /// <summary>
        /// Computes the gravitational force vector acting on an object at the given position.
        /// </summary>
        /// <param name="hovercraftPosition">World-space position of the target object.</param>
        /// <param name="hovercraftMass">Mass of the target object in kg.</param>
        /// <returns>Force vector in world space (Newtons).</returns>
        public Vector3 ComputeForce(Vector3 hovercraftPosition, float hovercraftMass)
        {
            if (!isActive) return Vector3.zero;

            Vector3 toCenter = transform.position - hovercraftPosition;
            float distance = toCenter.magnitude;

            // Outside radius — no force
            if (distance > fieldRadius || distance < 0.01f)
                return Vector3.zero;

            // Compute falloff multiplier
            float normalizedDist = distance / fieldRadius;
            float falloff;

            if (falloffCurve != null && falloffCurve.length > 1)
            {
                falloff = falloffCurve.Evaluate(normalizedDist);
            }
            else
            {
                // Inverse-power falloff: stronger near center, zero at edge
                falloff = Mathf.Pow(1f - normalizedDist, falloffExponent);
            }

            float forceMagnitude = fieldStrength * hovercraftMass * falloff;

            switch (fieldType)
            {
                case GravityFieldType.Attractor:
                    return toCenter.normalized * forceMagnitude;

                case GravityFieldType.Repulsor:
                    return -toCenter.normalized * forceMagnitude;

                case GravityFieldType.Directional:
                    return directionalForceDirection.normalized * forceMagnitude;

                default:
                    return Vector3.zero;
            }
        }

        // ──────────────────────────────────────────────
        //  Setup Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Configures this gravity field programmatically (used by procedural generation).
        /// </summary>
        public void Configure(float strength, float radius, GravityFieldType type,
            bool releasable = true, Color? color = null)
        {
            fieldStrength = strength;
            fieldRadius = radius;
            fieldType = type;
            canBeReleasedByPlayer = releasable;
            if (color.HasValue) fieldColor = color.Value;
            EnsureTriggerCollider();
        }

        private void EnsureTriggerCollider()
        {
            if (_triggerCollider == null)
                _triggerCollider = GetComponent<SphereCollider>();

            if (_triggerCollider == null)
                _triggerCollider = gameObject.AddComponent<SphereCollider>();

            _triggerCollider.isTrigger = true;
            _triggerCollider.radius = fieldRadius;
        }

        // ──────────────────────────────────────────────
        //  Editor Visualization
        // ──────────────────────────────────────────────

        private void OnValidate()
        {
            if (_triggerCollider != null)
                _triggerCollider.radius = fieldRadius;
        }

        private void OnDrawGizmos()
        {
            // Outer influence sphere
            Gizmos.color = new Color(fieldColor.r, fieldColor.g, fieldColor.b, 0.1f);
            Gizmos.DrawSphere(transform.position, fieldRadius);

            // Wireframe edge
            Gizmos.color = new Color(fieldColor.r, fieldColor.g, fieldColor.b, 0.4f);
            Gizmos.DrawWireSphere(transform.position, fieldRadius);

            // Inner core
            Gizmos.color = fieldColor;
            Gizmos.DrawSphere(transform.position, 0.5f);

            // Direction indicator for Directional type
            if (fieldType == GravityFieldType.Directional)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, directionalForceDirection.normalized * fieldRadius * 0.5f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Show falloff rings when selected
            Gizmos.color = new Color(fieldColor.r, fieldColor.g, fieldColor.b, 0.3f);
            for (int i = 1; i <= 4; i++)
            {
                float r = fieldRadius * (i / 4f);
                Gizmos.DrawWireSphere(transform.position, r);
            }
        }
    }
}
