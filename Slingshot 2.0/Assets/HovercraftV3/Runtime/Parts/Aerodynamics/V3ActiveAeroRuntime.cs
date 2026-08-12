using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class RuntimeRotaryActuatorInstance :
        RuntimePartInstance,
        IV3OrientationActuatorDevice
    {
        private ConnectorChildMount childMount;
        private Quaternion neutralRotation;
        private float pendingVelocity;

        public V3RotaryActuatorDefinition ActuatorDefinition =>
            Definition as V3RotaryActuatorDefinition;
        public float CurrentAngleDegrees { get; private set; }
        public float TargetAngleDegrees { get; private set; }
        public bool IsAtPhysicalLimit
        {
            get
            {
                V3RotaryActuatorDefinition actuator =
                    ActuatorDefinition;
                return actuator != null &&
                    (Mathf.Approximately(
                        CurrentAngleDegrees,
                        actuator.MinimumAngleDegrees) ||
                     Mathf.Approximately(
                        CurrentAngleDegrees,
                        actuator.MaximumAngleDegrees));
            }
        }

        public override void Initialize(
            PartDefinition definition,
            string parentSocketId)
        {
            base.Initialize(definition, parentSocketId);
            childMount = GetComponentInChildren<ConnectorChildMount>(true);
            neutralRotation = childMount != null
                ? childMount.MountTransform.localRotation
                : Quaternion.identity;
            CurrentAngleDegrees = 0f;
            TargetAngleDegrees = 0f;
            pendingVelocity = 0f;
        }

        public float PreparePowerRequest(
            float targetAngleDegrees,
            float deltaTime)
        {
            V3RotaryActuatorDefinition actuator = ActuatorDefinition;
            if (actuator == null || !IsEnabled || IsThermallyLockedOut)
            {
                TargetAngleDegrees = 0f;
                pendingVelocity = 0f;
                SetRuntimeState(0f, 0f, DisabledDemand, 0f);
                return DisabledDemand;
            }

            TargetAngleDegrees = Mathf.Clamp(
                targetAngleDegrees,
                actuator.MinimumAngleDegrees,
                actuator.MaximumAngleDegrees);
            float desiredVelocity = deltaTime <= 0f
                ? 0f
                : Mathf.Clamp(
                    (TargetAngleDegrees - CurrentAngleDegrees) / deltaTime,
                    -actuator.SpeedDegreesPerSecond,
                    actuator.SpeedDegreesPerSecond);
            pendingVelocity = Mathf.MoveTowards(
                pendingVelocity,
                desiredVelocity,
                actuator.AccelerationDegreesPerSecondSquared *
                    Mathf.Max(0f, deltaTime));
            float motion = actuator.SpeedDegreesPerSecond <= 0f
                ? 0f
                : Mathf.Clamp01(
                    Mathf.Abs(pendingVelocity) /
                    actuator.SpeedDegreesPerSecond);
            float demand = Mathf.Lerp(
                Definition.Power.idleDemand,
                Mathf.Max(
                    Definition.Power.idleDemand,
                    Definition.Power.maximumDemand),
                motion);
            SetRuntimeState(
                Mathf.InverseLerp(
                    actuator.MinimumAngleDegrees,
                    actuator.MaximumAngleDegrees,
                    TargetAngleDegrees),
                CurrentOutput,
                demand,
                0f);
            return demand;
        }

        public void ApplyPowerGrant(float grantedPower, float deltaTime)
        {
            V3RotaryActuatorDefinition actuator = ActuatorDefinition;
            if (actuator == null || childMount == null)
            {
                return;
            }

            float fraction = RequestedPower <= 0.0001f
                ? 1f
                : Mathf.Clamp01(grantedPower / RequestedPower);
            CurrentAngleDegrees = Mathf.MoveTowards(
                CurrentAngleDegrees,
                TargetAngleDegrees,
                Mathf.Abs(pendingVelocity) *
                    fraction *
                    Mathf.Max(0f, deltaTime));
            childMount.MountTransform.localRotation =
                neutralRotation *
                Quaternion.AngleAxis(
                    CurrentAngleDegrees,
                    actuator.LocalAxis);
            SetRuntimeState(
                RequestedOutput,
                fraction,
                RequestedPower,
                grantedPower);
        }

        public void ResetDynamicState()
        {
            CurrentAngleDegrees = 0f;
            TargetAngleDegrees = 0f;
            pendingVelocity = 0f;
            if (childMount != null)
            {
                childMount.MountTransform.localRotation = neutralRotation;
            }
            SetRuntimeState(0f, 0f, DisabledDemand, 0f);
        }

        private float DisabledDemand =>
            Definition != null
                ? Mathf.Max(0f, Definition.Power.disabledDemand)
                : 0f;
    }

}
