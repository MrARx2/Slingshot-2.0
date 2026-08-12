using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class RuntimeGimbalInstance : RuntimePartInstance
    {
        private ConnectorChildMount childMount;
        private Quaternion neutralLocalRotation = Quaternion.identity;
        private float targetPitchDegrees;
        private float targetYawDegrees;
        private float pitchVelocityDegreesPerSecond;
        private float yawVelocityDegreesPerSecond;
        private float pendingPitchVelocityDegreesPerSecond;
        private float pendingYawVelocityDegreesPerSecond;

        public GimbalDefinition GimbalDefinition =>
            Definition as GimbalDefinition;
        public Transform ActuatedMount =>
            childMount != null ? childMount.MountTransform : null;
        public float CurrentPitchDegrees { get; private set; }
        public float CurrentYawDegrees { get; private set; }
        public float TargetPitchDegrees => targetPitchDegrees;
        public float TargetYawDegrees => targetYawDegrees;
        public float PitchVelocityDegreesPerSecond =>
            pitchVelocityDegreesPerSecond;
        public float YawVelocityDegreesPerSecond =>
            yawVelocityDegreesPerSecond;
        public float CurrentActuatorTorqueNm { get; private set; }

        public override void Initialize(
            PartDefinition definition,
            string parentSocketId)
        {
            base.Initialize(definition, parentSocketId);
            childMount = GetComponentInChildren<ConnectorChildMount>(true);
            if (childMount != null)
            {
                neutralLocalRotation = childMount.MountTransform.localRotation;
            }

            ResetActuatorState();
        }

        public float PreparePowerRequest(
            float pitchInput,
            float yawInput,
            float deltaTime)
        {
            GimbalDefinition gimbal = GimbalDefinition;
            if (gimbal == null || !IsEnabled || IsThermallyLockedOut)
            {
                pendingPitchVelocityDegreesPerSecond = 0f;
                pendingYawVelocityDegreesPerSecond = 0f;
                SetRuntimeState(0f, 0f, DisabledDemand, 0f);
                return DisabledDemand;
            }

            float requestedPitch = gimbal.SupportsPitch
                ? Mathf.Clamp(pitchInput, -1f, 1f)
                : 0f;
            float requestedYaw = gimbal.SupportsYaw
                ? Mathf.Clamp(yawInput, -1f, 1f)
                : 0f;
            targetPitchDegrees =
                requestedPitch * gimbal.MaximumPitchDegrees;
            // Rear thrust deflection uses the opposite mount yaw so positive
            // pilot yaw produces positive craft yaw torque.
            targetYawDegrees =
                -requestedYaw * gimbal.MaximumYawDegrees;

            float maximumSpeed =
                Mathf.Max(0f, gimbal.RotationSpeedDegreesPerSecond);
            float acceleration =
                Mathf.Max(0f, gimbal.AngularAccelerationDegreesPerSecondSquared);
            pendingPitchVelocityDegreesPerSecond = CalculateRequestedVelocity(
                CurrentPitchDegrees,
                targetPitchDegrees,
                pitchVelocityDegreesPerSecond,
                maximumSpeed,
                acceleration,
                deltaTime);
            pendingYawVelocityDegreesPerSecond = CalculateRequestedVelocity(
                CurrentYawDegrees,
                targetYawDegrees,
                yawVelocityDegreesPerSecond,
                maximumSpeed,
                acceleration,
                deltaTime);

            float motion01 = maximumSpeed <= 0f
                ? 0f
                : Mathf.Clamp01(
                    Mathf.Max(
                        Mathf.Abs(pendingPitchVelocityDegreesPerSecond),
                        Mathf.Abs(pendingYawVelocityDegreesPerSecond)) /
                    maximumSpeed);
            float idle = Mathf.Max(0f, Definition.Power.idleDemand);
            float maximum = Mathf.Max(idle, Definition.Power.maximumDemand);
            float demand = Mathf.Lerp(idle, maximum, motion01);
            float requestedOutput =
                Mathf.Max(Mathf.Abs(requestedPitch), Mathf.Abs(requestedYaw));
            requestedOutput *= ThermalOutputLimit;
            pendingPitchVelocityDegreesPerSecond *= ThermalOutputLimit;
            pendingYawVelocityDegreesPerSecond *= ThermalOutputLimit;
            demand = Mathf.Lerp(idle, maximum, motion01 * ThermalOutputLimit);
            SetRuntimeState(requestedOutput, CurrentOutput, demand, 0f);
            return demand;
        }

        public void ApplyPowerGrant(float grantedPower, float deltaTime)
        {
            GimbalDefinition gimbal = GimbalDefinition;
            if (gimbal == null ||
                !IsEnabled ||
                IsThermallyLockedOut ||
                childMount == null)
            {
                SetRuntimeState(
                    RequestedOutput,
                    0f,
                    RequestedPower,
                    Mathf.Clamp(grantedPower, 0f, RequestedPower));
                return;
            }

            float idle = Mathf.Max(0f, Definition.Power.idleDemand);
            float variableDemand = Mathf.Max(0f, RequestedPower - idle);
            float usableGrant = Mathf.Max(0f, grantedPower - idle);
            float poweredFraction = variableDemand <= 0.0001f
                ? (grantedPower >= RequestedPower ? 1f : 0f)
                : Mathf.Clamp01(usableGrant / variableDemand);

            float actualPitchVelocity =
                pendingPitchVelocityDegreesPerSecond * poweredFraction;
            float actualYawVelocity =
                pendingYawVelocityDegreesPerSecond * poweredFraction;
            CurrentPitchDegrees = MoveAngleToward(
                CurrentPitchDegrees,
                targetPitchDegrees,
                actualPitchVelocity,
                deltaTime);
            CurrentYawDegrees = MoveAngleToward(
                CurrentYawDegrees,
                targetYawDegrees,
                actualYawVelocity,
                deltaTime);
            pitchVelocityDegreesPerSecond =
                Mathf.Approximately(CurrentPitchDegrees, targetPitchDegrees)
                    ? 0f
                    : actualPitchVelocity;
            yawVelocityDegreesPerSecond =
                Mathf.Approximately(CurrentYawDegrees, targetYawDegrees)
                    ? 0f
                    : actualYawVelocity;

            childMount.MountTransform.localRotation =
                neutralLocalRotation *
                Quaternion.Euler(
                    CurrentPitchDegrees,
                    CurrentYawDegrees,
                    0f);
            float maximumSpeed =
                Mathf.Max(0.001f, gimbal.RotationSpeedDegreesPerSecond);
            float actualOutput = Mathf.Clamp01(
                Mathf.Max(
                    Mathf.Abs(actualPitchVelocity),
                    Mathf.Abs(actualYawVelocity)) /
                maximumSpeed);
            CurrentActuatorTorqueNm =
                actualOutput * Mathf.Max(0f, gimbal.ActuatorTorqueNm);
            SetRuntimeState(
                RequestedOutput,
                actualOutput,
                RequestedPower,
                grantedPower);
        }

        public void ResetActuatorState()
        {
            targetPitchDegrees = 0f;
            targetYawDegrees = 0f;
            CurrentPitchDegrees = 0f;
            CurrentYawDegrees = 0f;
            pitchVelocityDegreesPerSecond = 0f;
            yawVelocityDegreesPerSecond = 0f;
            pendingPitchVelocityDegreesPerSecond = 0f;
            pendingYawVelocityDegreesPerSecond = 0f;
            CurrentActuatorTorqueNm = 0f;
            if (childMount != null)
            {
                childMount.MountTransform.localRotation = neutralLocalRotation;
            }

            SetRuntimeState(0f, 0f, IdleDemand, 0f);
        }

        public static float CalculateRequestedVelocity(
            float currentDegrees,
            float targetDegrees,
            float currentVelocityDegreesPerSecond,
            float maximumSpeedDegreesPerSecond,
            float accelerationDegreesPerSecondSquared,
            float deltaTime)
        {
            if (deltaTime <= 0f || maximumSpeedDegreesPerSecond <= 0f)
            {
                return 0f;
            }

            float desiredVelocity = Mathf.Clamp(
                (targetDegrees - currentDegrees) / deltaTime,
                -maximumSpeedDegreesPerSecond,
                maximumSpeedDegreesPerSecond);
            return accelerationDegreesPerSecondSquared <= 0f
                ? desiredVelocity
                : Mathf.MoveTowards(
                    currentVelocityDegreesPerSecond,
                    desiredVelocity,
                    accelerationDegreesPerSecondSquared * deltaTime);
        }

        private static float MoveAngleToward(
            float current,
            float target,
            float velocity,
            float deltaTime)
        {
            return Mathf.MoveTowards(
                current,
                target,
                Mathf.Abs(velocity) * Mathf.Max(0f, deltaTime));
        }

        private float IdleDemand =>
            Definition != null ? Mathf.Max(0f, Definition.Power.idleDemand) : 0f;
        private float DisabledDemand =>
            Definition != null
                ? Mathf.Max(0f, Definition.Power.disabledDemand)
                : 0f;
    }
}
