using UnityEngine;

namespace TrackGeneration.Race
{
    /// <summary>
    /// One race gate on the track: the start/finish line or a numbered checkpoint.
    /// <para>
    /// A gate is a detection PLANE, not a trigger collider: the craft covers ~2.8 m per
    /// physics tick at top speed, so <see cref="RaceCourse"/> detects the signed-distance
    /// sign flip across this gate's local XY plane instead of relying on overlap events
    /// that can tunnel. The gate's transform defines the plane (local +Z = race direction);
    /// this component only stores the detection window and identity.
    /// </para>
    /// </summary>
    public class RaceGate : MonoBehaviour
    {
        [Tooltip("True for the start/finish line, false for a checkpoint.")]
        public bool IsStartFinish;

        [Tooltip("0-based LOGICAL checkpoint order index. -1 for the start/finish line. Gates on alternative branch routes share the same index.")]
        public int CheckpointIndex = -1;

        [Tooltip("Arc length along the track where this gate sits (meters, for debugging).")]
        public float ArcLength;

        [Tooltip("Branch group this gate belongs to, -1 for main-line gates.")]
        public int BranchGroupId = -1;

        [Tooltip("Route within the branch group (0 = A, 1 = B), -1 for main-line gates.")]
        public int RouteId = -1;

        [Header("Detection Window (gate-local)")]
        [Tooltip("Half-width of the crossing window in meters. Wider than the road so wall-riding and offset lines still register.")]
        public float DetectionHalfWidth = 20f;

        [Tooltip("Window bottom in gate-local Y (below the floor so a surface-hugging craft can't slip under).")]
        public float DetectionBottom = -6f;

        [Tooltip("Window top in gate-local Y (generous so airborne crossings still count).")]
        public float DetectionTop = 45f;

        /// <summary>True when a gate-local point lies inside the crossing window.</summary>
        public bool IsInsideWindow(Vector3 localPoint)
        {
            return Mathf.Abs(localPoint.x) <= DetectionHalfWidth
                && localPoint.y >= DetectionBottom
                && localPoint.y <= DetectionTop;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsStartFinish ? new Color(1f, 1f, 1f, 0.6f) : new Color(0.2f, 0.9f, 1f, 0.6f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            float height = DetectionTop - DetectionBottom;
            Gizmos.DrawWireCube(new Vector3(0f, DetectionBottom + height * 0.5f, 0f),
                new Vector3(DetectionHalfWidth * 2f, height, 0.1f));
            Gizmos.matrix = previous;
        }
    }
}
