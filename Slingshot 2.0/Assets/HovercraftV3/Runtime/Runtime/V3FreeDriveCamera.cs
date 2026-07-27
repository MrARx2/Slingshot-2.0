using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Horizon-stable chase camera for the V3 free-drive scene.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class V3FreeDriveCamera : MonoBehaviour
    {
        [SerializeField] private V3FreeDriveSession session;
        [SerializeField] private Vector3 chaseOffset =
            new Vector3(0f, 5.5f, -13.5f);
        [SerializeField] private float lookHeight = 1.25f;
        [SerializeField] private float lookAheadDistance = 6f;
        [SerializeField] private float velocityLookAheadSeconds = 0.08f;
        [SerializeField] private float maximumVelocityLookAhead = 18f;
        [SerializeField] private float positionSmoothTime = 0.12f;
        [SerializeField] private float rotationSharpness = 10f;
        [SerializeField] private float baseFieldOfView = 68f;
        [SerializeField] private float highSpeedFieldOfView = 84f;
        [SerializeField] private float highSpeedKmh = 500f;

        private Camera chaseCamera;
        private Vector3 positionVelocity;
        private int observedResetSequence = -1;
        private Rigidbody observedBody;

        private void Awake()
        {
            chaseCamera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (session == null ||
                !session.RefreshCraftBinding() ||
                session.CraftBody == null)
            {
                return;
            }

            Rigidbody body = session.CraftBody;
            if (body != observedBody ||
                observedResetSequence != session.ResetSequence)
            {
                observedBody = body;
                observedResetSequence = session.ResetSequence;
                ResetCameraImmediate();
                return;
            }

            CalculatePose(
                body,
                out Vector3 desiredPosition,
                out Quaternion desiredRotation);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref positionVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            float rotationBlend =
                1f - Mathf.Exp(-rotationSharpness * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationBlend);
            UpdateFieldOfView(body);
        }

        public void ResetCameraImmediate()
        {
            if (session == null || session.CraftBody == null)
            {
                return;
            }

            CalculatePose(
                session.CraftBody,
                out Vector3 desiredPosition,
                out Quaternion desiredRotation);
            transform.SetPositionAndRotation(
                desiredPosition,
                desiredRotation);
            positionVelocity = Vector3.zero;
            UpdateFieldOfView(session.CraftBody);
        }

        private void CalculatePose(
            Rigidbody body,
            out Vector3 desiredPosition,
            out Quaternion desiredRotation)
        {
            Transform target = body.transform;
            Vector3 heading = Vector3.ProjectOnPlane(
                target.forward,
                Vector3.up);
            if (heading.sqrMagnitude < 0.001f)
            {
                heading = Vector3.forward;
            }

            Quaternion horizonHeading =
                Quaternion.LookRotation(heading.normalized, Vector3.up);
            Vector3 velocityLead = Vector3.ClampMagnitude(
                body.linearVelocity * velocityLookAheadSeconds,
                maximumVelocityLookAhead);
            desiredPosition =
                target.position +
                horizonHeading * chaseOffset +
                velocityLead * 0.35f;
            Vector3 lookPoint =
                target.position +
                Vector3.up * lookHeight +
                heading.normalized * lookAheadDistance +
                velocityLead;
            Vector3 lookDirection = lookPoint - desiredPosition;
            desiredRotation =
                lookDirection.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(
                        lookDirection.normalized,
                        Vector3.up)
                    : transform.rotation;
        }

        private void UpdateFieldOfView(Rigidbody body)
        {
            if (chaseCamera == null)
            {
                return;
            }

            float speedKmh = body.linearVelocity.magnitude * 3.6f;
            float targetFov = Mathf.Lerp(
                baseFieldOfView,
                highSpeedFieldOfView,
                Mathf.InverseLerp(0f, highSpeedKmh, speedKmh));
            chaseCamera.fieldOfView = Mathf.Lerp(
                chaseCamera.fieldOfView,
                targetFov,
                1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        }
    }
}
