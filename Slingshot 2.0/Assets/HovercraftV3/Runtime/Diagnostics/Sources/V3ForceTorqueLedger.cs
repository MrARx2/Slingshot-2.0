using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public sealed class V3ForceTorqueLedger : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;

        public string SourceId => "force_ledger";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
        }

        public void OnSessionStarted(V3DiagnosticSession session)
        {
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            Vector3 propulsion = Vector3.zero;
            Vector3 braking = Vector3.zero;
            Vector3 hover = Vector3.zero;
            Vector3 roof = Vector3.zero;
            Vector3 lateral = Vector3.zero;
            Vector3 aerodynamic = Vector3.zero;
            Vector3 thrusterTorque = Vector3.zero;
            Vector3 aerodynamicTorque = Vector3.zero;
            bool hasIntegratedAerodynamics = context != null &&
                context.Aerodynamics != null &&
                context.Aerodynamics.State.IsValid;
            for (int i = 0; i < sample.deviceCount; i++)
            {
                V3DeviceDiagnosticRecord device = sample.devices[i];
                if (device.deviceType == "AerodynamicFin")
                {
                    if (!hasIntegratedAerodynamics)
                    {
                        aerodynamic += device.actualForce;
                        aerodynamicTorque += device.torqueContribution;
                    }
                    continue;
                }
                if (device.deviceType != "Thruster") continue;
                thrusterTorque += device.torqueContribution;
                if (StartsWith(device.socketId, "Propulsion."))
                    propulsion += device.actualForce;
                else if (StartsWith(device.socketId, "Braking."))
                    braking += device.actualForce;
                else if (StartsWith(device.socketId, "Hover."))
                    hover += device.actualForce;
                else if (StartsWith(device.socketId, "Control."))
                    roof += device.actualForce;
                else if (StartsWith(device.socketId, "Strafe."))
                    lateral += device.actualForce;
                else
                    propulsion += device.actualForce;
            }
            if (hasIntegratedAerodynamics)
            {
                aerodynamic = sample.world.aerodynamicForce;
                aerodynamicTorque = sample.world.aerodynamicTorque;
            }

            float delta = Mathf.Max(0.000001f, sample.identity.fixedDelta);
            Vector3 contactForce = sample.world.contactImpulseVectorNs / delta;
            Vector3 up = sample.world.gravityVector.sqrMagnitude > 0.000001f
                ? -sample.world.gravityVector.normalized : Vector3.up;
            Vector3 velocityDirection = sample.world.linearVelocity.sqrMagnitude >
                0.000001f ? sample.world.linearVelocity.normalized : Vector3.zero;
            Vector3 aerodynamicLift = up * Vector3.Dot(aerodynamic, up);
            Vector3 aerodynamicRemainder = aerodynamic - aerodynamicLift;
            Vector3 aerodynamicDrag = velocityDirection.sqrMagnitude > 0f
                ? velocityDirection * Vector3.Dot(
                    aerodynamicRemainder, velocityDirection)
                : Vector3.zero;
            Vector3 aerodynamicSide = aerodynamicRemainder - aerodynamicDrag;
            Vector3 deviceForce = propulsion + braking + hover + roof +
                lateral + aerodynamic;
            Vector3 expected = sample.world.gravityForce + deviceForce + contactForce;
            float mass = context != null && context.Body != null
                ? Mathf.Max(0.0001f, context.Body.mass) : 1f;
            Vector3 observed = sample.world.linearAcceleration * mass;
            Vector3 observedTorque = context != null && context.Body != null
                ? V3ActuatorCommandRouter.CalculateRequiredWorldTorque(
                    context.Body, sample.world.angularAcceleration)
                : Vector3.zero;
            Vector3 centerOfMass = context != null && context.Body != null
                ? context.Body.worldCenterOfMass : sample.world.position;
            Vector3 contactTorque = sample.world.contactCount > 0
                ? Vector3.Cross(
                    sample.world.averageContactPoint - centerOfMass,
                    contactForce)
                : Vector3.zero;
            Vector3 expectedTorque = thrusterTorque + aerodynamicTorque +
                contactTorque;
            Rigidbody body = context != null ? context.Body : null;
            Vector3 expectedAngularAcceleration = body != null
                ? V3ActuatorCommandRouter.CalculateExpectedWorldAngularAcceleration(
                    body,
                    expectedTorque)
                : Vector3.zero;
            Vector3 gravity = sample.world.gravityVector;
            float weight = Mathf.Max(0.0001f, sample.world.gravityForce.magnitude);

            sample.forces.gravityForce = sample.world.gravityForce;
            sample.forces.propulsionForce = propulsion;
            sample.forces.brakingForce = braking;
            sample.forces.hoverForce = hover;
            sample.forces.roofForce = roof;
            sample.forces.lateralForce = lateral;
            sample.forces.aerodynamicForce = aerodynamic;
            sample.forces.aerodynamicLiftForce = aerodynamicLift;
            sample.forces.aerodynamicDragForce = aerodynamicDrag;
            sample.forces.aerodynamicSideForce = aerodynamicSide;
            sample.forces.contactForceEstimate = contactForce;
            sample.forces.expectedNetForce = expected;
            sample.forces.observedNetForce = observed;
            sample.forces.residualForce = observed - expected;
            sample.forces.thrusterTorque = thrusterTorque;
            sample.forces.aerodynamicTorque = aerodynamicTorque;
            sample.forces.contactTorqueEstimate = contactTorque;
            sample.forces.expectedTorque = expectedTorque;
            sample.forces.observedTorqueEstimate = observedTorque;
            sample.forces.residualTorque = observedTorque - expectedTorque;
            sample.forces.inertiaTensor = body != null
                ? body.inertiaTensor
                : Vector3.zero;
            sample.forces.inertiaTensorRotation = body != null
                ? body.inertiaTensorRotation
                : Quaternion.identity;
            sample.forces.appliedTorque = expectedTorque;
            sample.forces.measuredAngularAcceleration =
                sample.world.angularAcceleration;
            sample.forces.expectedAngularAcceleration =
                expectedAngularAcceleration;
            sample.forces.angularAccelerationError =
                sample.world.angularAcceleration - expectedAngularAcceleration;
            CaptureFrames(ref sample, expected, observed, expectedTorque,
                observedTorque - expectedTorque, up);
            sample.forces.hoverToWeight = Vector3.Dot(hover, up) / weight;
            sample.forces.aeroLiftToWeight = Vector3.Dot(aerodynamic, up) / weight;
            sample.forces.upwardThrusterToWeight = Vector3.Dot(
                propulsion + braking + hover + roof + lateral, up) / weight;
            sample.forces.totalUpwardToWeight =
                Vector3.Dot(deviceForce, up) / weight;
            sample.forces.netVerticalToWeight = Vector3.Dot(expected, up) / weight;
        }

        public void OnSessionEnded(V3DiagnosticSession session)
        {
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value != null && value.StartsWith(
                prefix, System.StringComparison.Ordinal);
        }

        private void CaptureFrames(
            ref V3DiagnosticSample sample,
            Vector3 expectedForce,
            Vector3 observedForce,
            Vector3 expectedTorque,
            Vector3 residualTorque,
            Vector3 gravityUp)
        {
            Vector3 residualForce = observedForce - expectedForce;
            Transform craft = context != null ? context.CraftTransform : null;
            sample.forces.expectedForceCraftFrame = craft != null
                ? craft.InverseTransformDirection(expectedForce) : expectedForce;
            sample.forces.observedForceCraftFrame = craft != null
                ? craft.InverseTransformDirection(observedForce) : observedForce;
            sample.forces.residualForceCraftFrame = craft != null
                ? craft.InverseTransformDirection(residualForce) : residualForce;
            sample.forces.expectedTorqueCraftFrame = craft != null
                ? craft.InverseTransformDirection(expectedTorque) : expectedTorque;
            sample.forces.residualTorqueCraftFrame = craft != null
                ? craft.InverseTransformDirection(residualTorque) : residualTorque;

            Vector3 trackRight = sample.track.right.sqrMagnitude > 0.000001f
                ? sample.track.right.normalized : Vector3.right;
            Vector3 trackUp = sample.track.normal.sqrMagnitude > 0.000001f
                ? sample.track.normal.normalized : gravityUp;
            Vector3 trackForward = sample.track.tangent.sqrMagnitude > 0.000001f
                ? sample.track.tangent.normalized : Vector3.forward;
            sample.forces.expectedForceTrackFrame = ToBasis(
                expectedForce, trackRight, trackUp, trackForward);
            sample.forces.observedForceTrackFrame = ToBasis(
                observedForce, trackRight, trackUp, trackForward);
            sample.forces.residualForceTrackFrame = ToBasis(
                residualForce, trackRight, trackUp, trackForward);
            sample.forces.expectedTorqueTrackFrame = ToBasis(
                expectedTorque, trackRight, trackUp, trackForward);
            sample.forces.residualTorqueTrackFrame = ToBasis(
                residualTorque, trackRight, trackUp, trackForward);

            Vector3 gravityForward = craft != null
                ? Vector3.ProjectOnPlane(craft.forward, gravityUp)
                : Vector3.ProjectOnPlane(Vector3.forward, gravityUp);
            if (gravityForward.sqrMagnitude <= 0.000001f)
                gravityForward = Vector3.ProjectOnPlane(Vector3.right, gravityUp);
            gravityForward.Normalize();
            Vector3 gravityRight = Vector3.Cross(
                gravityUp, gravityForward).normalized;
            sample.forces.expectedForceGravityFrame = ToBasis(
                expectedForce, gravityRight, gravityUp, gravityForward);
            sample.forces.observedForceGravityFrame = ToBasis(
                observedForce, gravityRight, gravityUp, gravityForward);
            sample.forces.residualForceGravityFrame = ToBasis(
                residualForce, gravityRight, gravityUp, gravityForward);
            sample.forces.expectedTorqueGravityFrame = ToBasis(
                expectedTorque, gravityRight, gravityUp, gravityForward);
            sample.forces.residualTorqueGravityFrame = ToBasis(
                residualTorque, gravityRight, gravityUp, gravityForward);
        }

        private static Vector3 ToBasis(
            Vector3 value, Vector3 right, Vector3 up, Vector3 forward)
        {
            return new Vector3(
                Vector3.Dot(value, right),
                Vector3.Dot(value, up),
                Vector3.Dot(value, forward));
        }
    }
}
