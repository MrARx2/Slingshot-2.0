using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Connectors/Spring Mount", fileName = "SpringMount")]
    public sealed class SpringMountDefinition : ConnectorDefinition
    {
        [SerializeField] private Vector3 localMovementAxis = Vector3.up;
        [Min(0f), SerializeField] private float compressionLimitM;
        [Min(0f), SerializeField] private float extensionLimitM;
        [Min(0f), SerializeField] private float springStiffness;
        [Min(0f), SerializeField] private float damping;

        public Vector3 LocalMovementAxis => localMovementAxis.sqrMagnitude > 0f
            ? localMovementAxis.normalized
            : Vector3.up;
        public float CompressionLimitM => compressionLimitM;
        public float ExtensionLimitM => extensionLimitM;
        public float TravelDistanceM => compressionLimitM + extensionLimitM;
        public float SpringStiffness => springStiffness;
        public float Damping => damping;
    }
}
