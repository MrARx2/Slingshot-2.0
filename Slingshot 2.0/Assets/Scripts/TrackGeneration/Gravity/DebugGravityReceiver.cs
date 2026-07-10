using UnityEngine;

namespace TrackGeneration.Gravity
{
    /// <summary>
    /// Temporary stand-in for testing gravity zones before the real hovercraft controller is imported.
    /// Attach to a simple Rigidbody cube or sphere to observe gravity field effects.
    /// </summary>
    [AddComponentMenu("Track Generation/Gravity/Debug Gravity Receiver")]
    [RequireComponent(typeof(Rigidbody))]
    public class DebugGravityReceiver : MonoBehaviour, IGravityReceiver
    {
        // ──────────────────────────────────────────────
        //  Configuration
        // ──────────────────────────────────────────────

        [Header("Debug Settings")]
        [Tooltip("Key to hold for releasing gravitational pull (simulates hovercraft ability).")]
        [SerializeField]
        private KeyCode releaseKey = KeyCode.Space;

        [Tooltip("Visual indicator color when gravity is being applied.")]
        [SerializeField]
        private Color activeForceColor = Color.cyan;

        [Tooltip("Visual indicator color when gravity release is active.")]
        [SerializeField]
        private Color releaseColor = Color.red;

        [Header("Debug Display (Read Only)")]
        [Tooltip("Last computed gravity force vector (for Inspector debugging).")]
        [SerializeField]
        private Vector3 lastAppliedForce;

        [Tooltip("Current gravity force magnitude.")]
        [SerializeField]
        private float currentForceMagnitude;

        [Tooltip("Whether the release key is currently held.")]
        [SerializeField]
        private bool isReleasing;

        // ──────────────────────────────────────────────
        //  IGravityReceiver Implementation
        // ──────────────────────────────────────────────

        private Rigidbody _rb;

        /// <inheritdoc/>
        public Rigidbody ReceiverRigidbody => _rb;

        /// <inheritdoc/>
        public float Mass => _rb != null ? _rb.mass : 1f;

        /// <inheritdoc/>
        public Vector3 Position => transform.position;

        /// <inheritdoc/>
        public Vector3 Velocity => _rb != null ? _rb.linearVelocity : Vector3.zero;

        /// <inheritdoc/>
        public bool IsReleasingGravity => isReleasing;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            isReleasing = Input.GetKey(releaseKey);
        }

        /// <inheritdoc/>
        public void ApplyGravityForce(Vector3 force, GravityFieldType sourceType)
        {
            if (_rb == null) return;

            lastAppliedForce = force;
            currentForceMagnitude = force.magnitude;
            _rb.AddForce(force, ForceMode.Force);
        }

        // ──────────────────────────────────────────────
        //  Editor Visualization
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (lastAppliedForce.sqrMagnitude < 0.01f) return;

            Gizmos.color = isReleasing ? releaseColor : activeForceColor;
            Gizmos.DrawRay(transform.position, lastAppliedForce.normalized * 3f);
            Gizmos.DrawSphere(transform.position + lastAppliedForce.normalized * 3f, 0.15f);
        }
    }
}
