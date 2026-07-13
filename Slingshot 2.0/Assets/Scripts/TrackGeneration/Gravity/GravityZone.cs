using UnityEngine;
using System.Collections.Generic;
using TrackGeneration.Macro;

namespace TrackGeneration.Gravity
{
    /// <summary>
    /// A composed gravity zone actor that manages one or two <see cref="GravityField"/> emitters
    /// to create track sections with special gravitational behavior.
    /// <para>
    /// <b>Centering mode</b> (2 emitters): Both sides of the road pull the hovercraft inward,
    /// canceling lateral forces and keeping it centered. Acts as an "invisible road" with no physical surface.
    /// </para>
    /// <para>
    /// <b>Orbital mode</b> (1 emitter): A single emitter causes the hovercraft to orbit it
    /// until the player activates the "release gravitational pull" ability.
    /// </para>
    /// </summary>
    [AddComponentMenu("Track Generation/Gravity/Gravity Zone")]
    public class GravityZone : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Configuration
        // ──────────────────────────────────────────────

        [Header("Zone Configuration")]
        [Tooltip("Centering = 2 emitters (invisible road). Orbital = 1 emitter (orbit until release).")]
        [SerializeField]
        private GravityZoneType zoneType = GravityZoneType.Centering;

        [Tooltip("Length of this gravity zone along the track direction (meters).")]
        [SerializeField, Range(10f, 200f)]
        private float zoneLength = 50f;

        [Tooltip("The spline parameter (0-1) where this zone starts on its parent spline.")]
        [SerializeField, Range(0f, 1f)]
        private float splineStartT;

        [Tooltip("Index of the spline this zone belongs to. 0 = main circuit, 1+ = shortcut index.")]
        [SerializeField]
        private int splineIndex;

        // ──────────────────────────────────────────────
        //  Centering Mode (2 emitters)
        // ──────────────────────────────────────────────

        [Header("Centering Mode (2 Emitters)")]
        [Tooltip("Left-side gravity emitter. Active only in Centering mode.")]
        [SerializeField]
        private GravityField leftEmitter;

        [Tooltip("Right-side gravity emitter. Active only in Centering mode.")]
        [SerializeField]
        private GravityField rightEmitter;

        [Tooltip("Distance between the two centering emitters (meters). Should match or exceed road width.")]
        [SerializeField, Range(5f, 50f)]
        private float emitterSeparation = 20f;

        // ──────────────────────────────────────────────
        //  Orbital Mode (1 emitter)
        // ──────────────────────────────────────────────

        [Header("Orbital Mode (1 Emitter)")]
        [Tooltip("The single gravity emitter for orbital mode.")]
        [SerializeField]
        private GravityField singleEmitter;

        [Tooltip("Expected stable orbit radius (meters). Determines emitter placement distance from track.")]
        [SerializeField, Range(5f, 60f)]
        private float orbitRadius = 15f;

        // ──────────────────────────────────────────────
        //  Trigger References
        // ──────────────────────────────────────────────

        [Header("Zone Triggers")]
        [Tooltip("Trigger collider at the entry point of the gravity zone.")]
        [SerializeField]
        private Collider entryTrigger;

        [Tooltip("Trigger collider at the exit point of the gravity zone.")]
        [SerializeField]
        private Collider exitTrigger;

        // ──────────────────────────────────────────────
        //  Debug & Visualization
        // ──────────────────────────────────────────────

        [Header("Debug")]
        [Tooltip("Show debug gizmos for this gravity zone in the Scene view.")]
        [SerializeField]
        private bool showDebugGizmos = true;

        [Tooltip("Color for Centering mode gizmos.")]
        [SerializeField]
        private Color centeringGizmoColor = new Color(0f, 1f, 0.5f, 0.3f);

        [Tooltip("Color for Orbital mode gizmos.")]
        [SerializeField]
        private Color orbitalGizmoColor = new Color(1f, 0.3f, 0f, 0.3f);

        // ──────────────────────────────────────────────
        //  Public Properties
        // ──────────────────────────────────────────────

        /// <summary>The operational mode of this gravity zone.</summary>
        public GravityZoneType ZoneType => zoneType;

        /// <summary>Length of this zone along the track (meters).</summary>
        public float ZoneLength => zoneLength;

        /// <summary>Spline parameter where this zone starts.</summary>
        public float SplineStartT => splineStartT;

        /// <summary>Which spline this zone belongs to (0 = main, 1+ = shortcut).</summary>
        public int SplineIndex => splineIndex;

        /// <summary>Left emitter reference (Centering mode).</summary>
        public GravityField LeftEmitter => leftEmitter;

        /// <summary>Right emitter reference (Centering mode).</summary>
        public GravityField RightEmitter => rightEmitter;

        /// <summary>Single emitter reference (Orbital mode).</summary>
        public GravityField SingleEmitter => singleEmitter;

        // ──────────────────────────────────────────────
        //  Generation
        // ──────────────────────────────────────────────

        /// <summary>
        /// Procedurally generates this gravity zone's emitters and triggers along the given track frames.
        /// Called by the track generator during procedural track building.
        /// </summary>
        /// <param name="trackFrames">Sampled track frames covering this zone's extent.</param>
        /// <param name="roadWidth">Width of the road at this section (meters).</param>
        /// <param name="strength">Gravity field strength to apply.</param>
        /// <param name="radius">Gravity field radius for each emitter.</param>
        public void Generate(TrackConnectionFrame[] trackFrames, float roadWidth, float strength, float radius)
        {
            if (trackFrames == null || trackFrames.Length < 2)
            {
                Debug.LogWarning($"[GravityZone] Not enough track frames to generate zone on {gameObject.name}");
                return;
            }

            // Use the midpoint frame for emitter placement
            TrackConnectionFrame midFrame = trackFrames[trackFrames.Length / 2];
            Vector3 center = midFrame.Position;
            Vector3 right = midFrame.Right;
            Vector3 forward = midFrame.Forward;

            switch (zoneType)
            {
                case GravityZoneType.Centering:
                    GenerateCenteringMode(center, right, forward, roadWidth, strength, radius);
                    break;

                case GravityZoneType.Orbital:
                    GenerateOrbitalMode(center, right, forward, strength, radius);
                    break;
            }

            GenerateTriggers(trackFrames, roadWidth);
        }

        private void GenerateCenteringMode(Vector3 center, Vector3 right, Vector3 forward,
            float roadWidth, float strength, float radius)
        {
            float halfSeparation = emitterSeparation * 0.5f;

            // Create or position left emitter
            if (leftEmitter == null)
            {
                var leftGO = new GameObject("LeftEmitter");
                leftGO.transform.SetParent(transform);
                leftEmitter = leftGO.AddComponent<GravityField>();
            }
            leftEmitter.transform.position = center - right * halfSeparation;
            leftEmitter.Configure(strength, radius, GravityFieldType.Attractor, true,
                new Color(0f, 0.8f, 1f, 1f));

            // Create or position right emitter
            if (rightEmitter == null)
            {
                var rightGO = new GameObject("RightEmitter");
                rightGO.transform.SetParent(transform);
                rightEmitter = rightGO.AddComponent<GravityField>();
            }
            rightEmitter.transform.position = center + right * halfSeparation;
            rightEmitter.Configure(strength, radius, GravityFieldType.Attractor, true,
                new Color(0f, 0.8f, 1f, 1f));
        }

        private void GenerateOrbitalMode(Vector3 center, Vector3 right, Vector3 forward,
            float strength, float radius)
        {
            if (singleEmitter == null)
            {
                var emitterGO = new GameObject("OrbitalEmitter");
                emitterGO.transform.SetParent(transform);
                singleEmitter = emitterGO.AddComponent<GravityField>();
            }

            // Place the orbital emitter offset to one side at the orbit radius distance
            singleEmitter.transform.position = center + right * orbitRadius;
            singleEmitter.Configure(strength, radius, GravityFieldType.Attractor, true,
                new Color(1f, 0.5f, 0f, 1f));
        }

        private void GenerateTriggers(TrackConnectionFrame[] trackFrames, float roadWidth)
        {
            // Entry trigger at the first frame
            TrackConnectionFrame entryFrame = trackFrames[0];
            if (entryTrigger == null)
            {
                var entryGO = new GameObject("EntryTrigger");
                entryGO.transform.SetParent(transform);
                var boxCollider = entryGO.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = new Vector3(roadWidth * 2f, 20f, 2f);
                entryTrigger = boxCollider;
            }
            entryTrigger.transform.position = entryFrame.Position;
            entryTrigger.transform.rotation = Quaternion.LookRotation(
                entryFrame.Forward, entryFrame.Up);

            // Exit trigger at the last frame
            TrackConnectionFrame exitFrame = trackFrames[trackFrames.Length - 1];
            if (exitTrigger == null)
            {
                var exitGO = new GameObject("ExitTrigger");
                exitGO.transform.SetParent(transform);
                var boxCollider = exitGO.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = new Vector3(roadWidth * 2f, 20f, 2f);
                exitTrigger = boxCollider;
            }
            exitTrigger.transform.position = exitFrame.Position;
            exitTrigger.transform.rotation = Quaternion.LookRotation(
                exitFrame.Forward, exitFrame.Up);
        }

        // ──────────────────────────────────────────────
        //  Runtime: Activate / Deactivate emitters
        // ──────────────────────────────────────────────

        /// <summary>Activates all emitters in this zone.</summary>
        public void ActivateZone()
        {
            SetEmittersActive(true);
        }

        /// <summary>Deactivates all emitters in this zone.</summary>
        public void DeactivateZone()
        {
            SetEmittersActive(false);
        }

        private void SetEmittersActive(bool active)
        {
            if (leftEmitter != null) leftEmitter.IsActive = active;
            if (rightEmitter != null) rightEmitter.IsActive = active;
            if (singleEmitter != null) singleEmitter.IsActive = active;
        }

        // ──────────────────────────────────────────────
        //  Editor Visualization
        // ──────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            Color gizmoColor = zoneType == GravityZoneType.Centering
                ? centeringGizmoColor
                : orbitalGizmoColor;

            Gizmos.color = gizmoColor;

            // Draw a box representing the zone volume
            Vector3 size = new Vector3(
                zoneType == GravityZoneType.Centering ? emitterSeparation : orbitRadius * 2f,
                10f,
                zoneLength
            );
            Gizmos.DrawWireCube(transform.position, size);

            // Label
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(transform.position, 0.3f);
        }

        private void OnValidate()
        {
            // Ensure emitter separation is reasonable relative to orbit radius
            if (zoneType == GravityZoneType.Centering && emitterSeparation < 5f)
                emitterSeparation = 5f;
            if (zoneType == GravityZoneType.Orbital && orbitRadius < 5f)
                orbitRadius = 5f;
        }
    }
}
