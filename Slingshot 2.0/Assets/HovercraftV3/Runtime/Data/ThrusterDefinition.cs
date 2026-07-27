using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Thruster", fileName = "Thruster")]
    public sealed class ThrusterDefinition : EndpointDefinition
    {
        [Header("Thrust")]
        [Min(0f), SerializeField] private float maximumForwardForceN;
        [Min(0f), SerializeField] private float maximumReverseForceN;
        [Range(0f, 1f), SerializeField] private float minimumControllableOutput;
        [Min(0f), SerializeField] private float thrustRisePerSecond;
        [Min(0f), SerializeField] private float thrustFallPerSecond;
        [Min(1f), SerializeField] private float normalOutputMultiplier = 1f;
        [Min(1f), SerializeField] private float overloadOutputMultiplier = 1f;
        [SerializeField] private Vector3 localThrustDirection = Vector3.forward;
        [SerializeField] private Vector3 localForceOrigin;

        public override EndpointCategory Category => EndpointCategory.Thruster;
        public override float MaximumOutputForceN =>
            maximumForwardForceN * Mathf.Max(1f, overloadOutputMultiplier);
        public float MaximumForwardForceN => maximumForwardForceN;
        public float MaximumReverseForceN => maximumReverseForceN;
        public float MinimumControllableOutput => minimumControllableOutput;
        public float ThrustRisePerSecond => thrustRisePerSecond;
        public float ThrustFallPerSecond => thrustFallPerSecond;
        public float NormalOutputMultiplier => Mathf.Max(1f, normalOutputMultiplier);
        public float OverloadOutputMultiplier =>
            Mathf.Max(NormalOutputMultiplier, overloadOutputMultiplier);
        public Vector3 LocalThrustDirection => localThrustDirection.sqrMagnitude > 0f
            ? localThrustDirection.normalized
            : Vector3.forward;
        public Vector3 LocalForceOrigin => localForceOrigin;
    }
}
