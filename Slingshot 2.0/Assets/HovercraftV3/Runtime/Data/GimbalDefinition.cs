using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Connectors/Gimbal", fileName = "Gimbal")]
    public sealed class GimbalDefinition : ConnectorDefinition
    {
        [SerializeField] private bool supportsPitch = true;
        [SerializeField] private bool supportsYaw = true;
        [Min(0f), SerializeField] private float maximumPitchDegrees;
        [Min(0f), SerializeField] private float maximumYawDegrees;
        [Min(0f), SerializeField] private float rotationSpeedDegreesPerSecond;
        [Min(0f), SerializeField] private float angularAccelerationDegreesPerSecondSquared;
        [Min(0f), SerializeField] private float actuatorTorqueNm;

        public bool SupportsPitch => supportsPitch;
        public bool SupportsYaw => supportsYaw;
        public float MaximumPitchDegrees => maximumPitchDegrees;
        public float MaximumYawDegrees => maximumYawDegrees;
        public float RotationSpeedDegreesPerSecond => rotationSpeedDegreesPerSecond;
        public float AngularAccelerationDegreesPerSecondSquared =>
            angularAccelerationDegreesPerSecondSquared;
        public float ActuatorTorqueNm => actuatorTorqueNm;
    }
}
