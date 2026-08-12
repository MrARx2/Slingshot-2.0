using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Connectors/Rotary Actuator")]
    public sealed class V3RotaryActuatorDefinition : ConnectorDefinition
    {
        [SerializeField] private Vector3 localAxis = Vector3.right;
        [SerializeField] private float minimumAngleDegrees = -25f;
        [SerializeField] private float maximumAngleDegrees = 25f;
        [Min(0f), SerializeField] private float speedDegreesPerSecond = 120f;
        [Min(0f), SerializeField] private float accelerationDegreesPerSecondSquared = 480f;
        [Min(0f), SerializeField] private float maximumLoadNm = 10000f;
        [SerializeField] private V3DeviceFirmwareDefinition firmware;
        public Vector3 LocalAxis => localAxis.normalized;
        public float MinimumAngleDegrees => minimumAngleDegrees;
        public float MaximumAngleDegrees => maximumAngleDegrees;
        public float SpeedDegreesPerSecond => speedDegreesPerSecond;
        public float AccelerationDegreesPerSecondSquared =>
            accelerationDegreesPerSecondSquared;
        public float MaximumLoadNm => maximumLoadNm;
        public V3DeviceFirmwareDefinition Firmware => firmware;
    }
}
