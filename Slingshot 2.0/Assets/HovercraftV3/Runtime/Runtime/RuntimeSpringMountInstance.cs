using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class RuntimeSpringMountInstance : RuntimePartInstance
    {
        private const float MaximumSimulationStep = 1f / 240f;

        private ConnectorChildMount childMount;
        private Vector3 neutralLocalPosition;
        private float displacementM;
        private float velocityMPerSecond;

        public SpringMountDefinition SpringDefinition =>
            Definition as SpringMountDefinition;
        public Transform ActuatedMount =>
            childMount != null ? childMount.MountTransform : null;
        public float DisplacementM => displacementM;
        public float VelocityMPerSecond => velocityMPerSecond;
        public float CurrentEndpointLoadN { get; private set; }
        public float CurrentSpringForceN { get; private set; }
        public float SpringPotentialEnergyJ { get; private set; }
        public float DampingLossPowerW { get; private set; }
        public bool IsAtCompressionLimit { get; private set; }
        public bool IsAtExtensionLimit { get; private set; }

        public override void Initialize(
            PartDefinition definition,
            string parentSocketId)
        {
            base.Initialize(definition, parentSocketId);
            childMount = GetComponentInChildren<ConnectorChildMount>(true);
            if (childMount != null)
            {
                neutralLocalPosition = childMount.MountTransform.localPosition;
            }

            ResetSpringState();
        }

        public void TickFromEndpointForce(
            Vector3 endpointWorldForce,
            float endpointMassKg,
            float deltaTime)
        {
            SpringMountDefinition spring = SpringDefinition;
            if (spring == null ||
                childMount == null ||
                !IsEnabled ||
                deltaTime <= 0f)
            {
                return;
            }

            Vector3 localForce =
                transform.InverseTransformDirection(endpointWorldForce);
            CurrentEndpointLoadN =
                Vector3.Dot(localForce, spring.LocalMovementAxis);

            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(deltaTime / MaximumSimulationStep));
            float step = deltaTime / steps;
            IsAtCompressionLimit = false;
            IsAtExtensionLimit = false;
            for (int i = 0; i < steps; i++)
            {
                IntegrateStep(
                    CurrentEndpointLoadN,
                    Mathf.Max(0.001f, endpointMassKg),
                    spring.SpringStiffness,
                    spring.Damping,
                    spring.CompressionLimitM,
                    spring.ExtensionLimitM,
                    step,
                    ref displacementM,
                    ref velocityMPerSecond,
                    out bool compressionStop,
                    out bool extensionStop);
                IsAtCompressionLimit |= compressionStop;
                IsAtExtensionLimit |= extensionStop;
            }

            CurrentSpringForceN =
                spring.SpringStiffness * displacementM +
                spring.Damping * velocityMPerSecond;
            SpringPotentialEnergyJ =
                0.5f *
                Mathf.Max(0f, spring.SpringStiffness) *
                displacementM *
                displacementM;
            DampingLossPowerW =
                Mathf.Max(0f, spring.Damping) *
                velocityMPerSecond *
                velocityMPerSecond;
            childMount.MountTransform.localPosition =
                neutralLocalPosition +
                spring.LocalMovementAxis * displacementM;

            float displacement01 = displacementM >= 0f
                ? SafeRatio(displacementM, spring.CompressionLimitM)
                : -SafeRatio(-displacementM, spring.ExtensionLimitM);
            float load01 = SafeRatio(
                Mathf.Abs(CurrentEndpointLoadN),
                spring.SupportedEndpointForceN);
            SetRuntimeState(load01, displacement01, 0f, 0f);
        }

        public void ResetSpringState()
        {
            displacementM = 0f;
            velocityMPerSecond = 0f;
            CurrentEndpointLoadN = 0f;
            CurrentSpringForceN = 0f;
            SpringPotentialEnergyJ = 0f;
            DampingLossPowerW = 0f;
            IsAtCompressionLimit = false;
            IsAtExtensionLimit = false;
            if (childMount != null)
            {
                childMount.MountTransform.localPosition =
                    neutralLocalPosition;
            }

            SetRuntimeState(0f, 0f, 0f, 0f);
        }

        public static void IntegrateStep(
            float endpointLoadN,
            float endpointMassKg,
            float stiffnessNPerM,
            float dampingNsPerM,
            float compressionLimitM,
            float extensionLimitM,
            float deltaTime,
            ref float displacementM,
            ref float velocityMPerSecond,
            out bool atCompressionLimit,
            out bool atExtensionLimit)
        {
            atCompressionLimit = false;
            atExtensionLimit = false;
            if (deltaTime <= 0f || endpointMassKg <= 0f)
            {
                return;
            }

            float acceleration =
                (endpointLoadN -
                 Mathf.Max(0f, stiffnessNPerM) * displacementM -
                 Mathf.Max(0f, dampingNsPerM) * velocityMPerSecond) /
                endpointMassKg;
            velocityMPerSecond += acceleration * deltaTime;
            displacementM += velocityMPerSecond * deltaTime;

            float compression = Mathf.Max(0f, compressionLimitM);
            float extension = Mathf.Max(0f, extensionLimitM);
            if (displacementM > compression)
            {
                displacementM = compression;
                velocityMPerSecond = Mathf.Min(0f, velocityMPerSecond);
                atCompressionLimit = true;
            }
            else if (displacementM < -extension)
            {
                displacementM = -extension;
                velocityMPerSecond = Mathf.Max(0f, velocityMPerSecond);
                atExtensionLimit = true;
            }
        }

        private static float SafeRatio(float value, float maximum)
        {
            return maximum > 0.000001f
                ? Mathf.Clamp01(value / maximum)
                : 0f;
        }
    }
}
