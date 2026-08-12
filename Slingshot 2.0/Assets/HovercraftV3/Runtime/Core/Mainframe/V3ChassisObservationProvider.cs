using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3ChassisObservationProvider
    {
        private Rigidbody body;
        private Vector3 previousVelocity;
        private Vector3 previousAngularVelocity;
        private bool hasPrevious;

        public void Initialize(Rigidbody value)
        {
            body = value;
            previousVelocity = value != null ? value.linearVelocity : Vector3.zero;
            previousAngularVelocity =
                value != null ? value.angularVelocity : Vector3.zero;
            hasPrevious = value != null;
        }

        public void Sample(
            V3ObservationBus bus,
            double timestamp,
            float deltaTime)
        {
            if (body == null || bus == null)
            {
                return;
            }

            float safeDelta = Mathf.Max(0.0001f, deltaTime);
            Vector3 acceleration = hasPrevious
                ? (body.linearVelocity - previousVelocity) / safeDelta
                : Vector3.zero;
            Vector3 angularAcceleration = hasPrevious
                ? (body.angularVelocity - previousAngularVelocity) / safeDelta
                : Vector3.zero;
            bus.Publish(new V3Observation(
                V3ObservationCategory.ChassisState,
                "chassis.motion",
                timestamp,
                body.linearVelocity,
                body.angularVelocity,
                body.mass,
                body.worldCenterOfMass.y,
                1f,
                body.IsSleeping()));
            bus.Publish(new V3Observation(
                V3ObservationCategory.ChassisState,
                "chassis.acceleration",
                timestamp,
                acceleration,
                angularAcceleration,
                body.inertiaTensor.magnitude,
                0f,
                1f,
                true));
            previousVelocity = body.linearVelocity;
            previousAngularVelocity = body.angularVelocity;
            hasPrevious = true;
        }
    }
}
