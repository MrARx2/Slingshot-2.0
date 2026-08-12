using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Parts/Aerodynamic Fin")]
    public sealed class V3AerodynamicFinDefinition : EndpointDefinition
    {
        [Min(0.01f), SerializeField] private float areaSquareMeters = 0.8f;
        [SerializeField] private int aerodynamicModelVersion;
        [HideInInspector, SerializeField] private float liftCoefficient = 0.9f;
        [HideInInspector, SerializeField] private float dragCoefficient = 0.08f;

        [Header("Aerodynamic Coefficients")]
        [SerializeField] private float zeroLiftAngleDegrees;
        [Tooltip("Lift coefficient gained per radian in the attached-flow region.")]
        [Min(0f), SerializeField] private float liftSlopePerRadian = 4.5f;
        [Min(0f), SerializeField] private float maximumLiftCoefficient = 0.9f;
        [Range(1f, 89f), SerializeField] private float positiveStallAngleDegrees = 18f;
        [Range(-89f, -1f), SerializeField] private float negativeStallAngleDegrees = -18f;
        [Min(0f), SerializeField] private float postStallLiftCoefficient = 0.2f;
        [Range(0f, 1f), SerializeField] private float reverseFlowScale = 0.2f;
        [Min(0f), SerializeField] private float baseDragCoefficient = 0.04f;
        [Min(0f), SerializeField] private float inducedDragFactor = 0.12f;
        [Min(0f), SerializeField] private float stalledDragCoefficient = 0.8f;

        [Header("Structural Envelope")]
        [Tooltip("Zero disables the force clamp.")]
        [Min(0f), SerializeField] private float maximumAerodynamicForceN;
        [Tooltip("Zero disables the moment warning/clamp state.")]
        [Min(0f), SerializeField] private float maximumAerodynamicMomentNm;
        [Range(0f, 1f), SerializeField] private float structuralWarningFraction = 0.85f;

        [Header("Geometry")]
        [SerializeField] private float neutralAngleDegrees;
        [SerializeField] private Vector3 localCenterOfPressure;
        [SerializeField] private Vector3 localLiftAxis = Vector3.up;
        [SerializeField] private Vector3 localChordAxis = Vector3.forward;
        public override EndpointCategory Category => EndpointCategory.Fin;
        public float AreaSquareMeters => areaSquareMeters;
        public float LiftCoefficient => maximumLiftCoefficient;
        public float DragCoefficient => baseDragCoefficient;
        public float ZeroLiftAngleDegrees => zeroLiftAngleDegrees;
        public float LiftSlopePerRadian => Mathf.Max(0f, liftSlopePerRadian);
        public float MaximumLiftCoefficient => Mathf.Max(0f, maximumLiftCoefficient);
        public float PositiveStallAngleDegrees => Mathf.Clamp(positiveStallAngleDegrees, 1f, 89f);
        public float NegativeStallAngleDegrees => Mathf.Clamp(negativeStallAngleDegrees, -89f, -1f);
        public float PostStallLiftCoefficient => Mathf.Max(0f, postStallLiftCoefficient);
        public float ReverseFlowScale => Mathf.Clamp01(reverseFlowScale);
        public float BaseDragCoefficient => Mathf.Max(0f, baseDragCoefficient);
        public float InducedDragFactor => Mathf.Max(0f, inducedDragFactor);
        public float StalledDragCoefficient => Mathf.Max(BaseDragCoefficient, stalledDragCoefficient);
        public float MaximumAerodynamicForceN => Mathf.Max(0f, maximumAerodynamicForceN);
        public float MaximumAerodynamicMomentNm => Mathf.Max(0f, maximumAerodynamicMomentNm);
        public float StructuralWarningFraction => Mathf.Clamp01(structuralWarningFraction);
        public float NeutralAngleDegrees => neutralAngleDegrees;
        public Vector3 LocalCenterOfPressure => localCenterOfPressure;
        public Vector3 LocalLiftAxis => localLiftAxis.normalized;
        public Vector3 LocalChordAxis => localChordAxis.normalized;

        public float EvaluateLiftCoefficient(float angleOfAttackDegrees)
        {
            float angle = Mathf.DeltaAngle(zeroLiftAngleDegrees, angleOfAttackDegrees);
            bool reversed = Mathf.Abs(angle) > 90f;
            float magnitude = Mathf.Abs(angle);
            float stall = angle >= 0f
                ? PositiveStallAngleDegrees
                : Mathf.Abs(NegativeStallAngleDegrees);
            float attached = Mathf.Min(
                MaximumLiftCoefficient,
                LiftSlopePerRadian * magnitude * Mathf.Deg2Rad);
            float coefficient;
            if (magnitude <= stall)
            {
                coefficient = attached;
            }
            else
            {
                float postStall01 = Mathf.InverseLerp(stall, 90f, Mathf.Min(90f, magnitude));
                coefficient = Mathf.Lerp(
                    Mathf.Min(MaximumLiftCoefficient,
                        LiftSlopePerRadian * stall * Mathf.Deg2Rad),
                    PostStallLiftCoefficient,
                    postStall01);
            }

            if (reversed)
            {
                coefficient *= ReverseFlowScale;
            }
            return Mathf.Sign(angle) * coefficient;
        }

        public float EvaluateDragCoefficient(float angleOfAttackDegrees, float lift)
        {
            float angle = Mathf.Abs(Mathf.DeltaAngle(
                zeroLiftAngleDegrees,
                angleOfAttackDegrees));
            float sine = Mathf.Sin(Mathf.Min(90f, angle) * Mathf.Deg2Rad);
            return BaseDragCoefficient +
                InducedDragFactor * lift * lift +
                (StalledDragCoefficient - BaseDragCoefficient) * sine * sine;
        }

        private void OnValidate()
        {
            if (aerodynamicModelVersion < 2)
            {
                maximumLiftCoefficient = Mathf.Max(
                    0f,
                    liftCoefficient);
                baseDragCoefficient = Mathf.Max(0f, dragCoefficient);
                aerodynamicModelVersion = 2;
            }
        }
    }
}
