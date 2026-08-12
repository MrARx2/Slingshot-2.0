using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class RuntimeAerodynamicFinInstance :
        RuntimePartInstance,
        IV3AerodynamicSurfaceDevice
    {
        private Rigidbody rootBody;
        public V3AerodynamicFinDefinition FinDefinition =>
            Definition as V3AerodynamicFinDefinition;
        public float CurrentAerodynamicForceN { get; private set; }
        public Vector3 CurrentWorldAerodynamicForce { get; private set; }
        public Vector3 CurrentWorldAerodynamicTorque { get; private set; }
        public Vector3 CurrentRelativeAirVelocity { get; private set; }
        public float CurrentDynamicPressurePa { get; private set; }
        public float CurrentMachNumber { get; private set; }
        public float CurrentAngleOfAttackDegrees { get; private set; }
        public float CurrentLiftCoefficient { get; private set; }
        public float CurrentDragCoefficient { get; private set; }
        public Vector3 CurrentWorldLiftForce { get; private set; }
        public Vector3 CurrentWorldDragForce { get; private set; }
        public Vector3 CurrentForceApplicationPoint { get; private set; }
        public bool IsStalled { get; private set; }
        public bool IsReverseFlow { get; private set; }
        public bool IsStructurallyLimited { get; private set; }
        public bool HasStructuralWarning { get; private set; }
        public string StructuralLimitReason { get; private set; } = string.Empty;

        public void BindRigidbody(Rigidbody value)
        {
            rootBody = value;
        }

        public void ApplyAerodynamicForce(V3WorldEnvironmentSample environment)
        {
            V3AerodynamicFinDefinition fin = FinDefinition;
            if (fin == null || rootBody == null || !IsEnabled)
            {
                ClearAerodynamicState(Vector3.zero);
                return;
            }

            Vector3 point = transform.TransformPoint(
                fin.LocalCenterOfPressure);
            Vector3 relativeAir =
                environment.LocalAirVelocity -
                rootBody.GetPointVelocity(point);
            float speed = relativeAir.magnitude;
            if (speed <= 0.01f)
            {
                ClearAerodynamicState(relativeAir);
                CurrentForceApplicationPoint = point;
                return;
            }

            Vector3 airflowDirection = relativeAir / speed;
            Vector3 chord = transform.TransformDirection(fin.LocalChordAxis).normalized;
            Vector3 normal = Vector3.ProjectOnPlane(
                transform.TransformDirection(fin.LocalLiftAxis), chord).normalized;
            if (chord.sqrMagnitude <= 0.5f || normal.sqrMagnitude <= 0.5f)
            {
                ClearAerodynamicState(relativeAir);
                StructuralLimitReason = "Invalid aerodynamic frame";
                return;
            }
            Vector3 span = Vector3.Cross(chord, normal).normalized;
            Vector3 incoming = -airflowDirection;
            Vector3 incomingInPlane = Vector3.ProjectOnPlane(incoming, span);
            if (incomingInPlane.sqrMagnitude <= 0.000001f)
            {
                ClearAerodynamicState(relativeAir);
                CurrentForceApplicationPoint = point;
                return;
            }
            incomingInPlane.Normalize();
            float angleOfAttack = Mathf.Atan2(
                Vector3.Dot(incomingInPlane, normal),
                Vector3.Dot(incomingInPlane, chord)) * Mathf.Rad2Deg;
            float liftCoefficient = fin.EvaluateLiftCoefficient(angleOfAttack);
            float dragCoefficient = fin.EvaluateDragCoefficient(
                angleOfAttack,
                liftCoefficient);
            Vector3 liftAxis = Vector3.Cross(airflowDirection, span).normalized;
            float dynamicPressure =
                0.5f * Mathf.Max(
                    0f,
                    environment.AirDensityKgPerCubicMeter) *
                speed * speed;
            Vector3 lift = liftAxis *
                (dynamicPressure *
                 fin.AreaSquareMeters *
                 liftCoefficient);
            Vector3 drag = airflowDirection *
                (dynamicPressure *
                 fin.AreaSquareMeters *
                 dragCoefficient);
            Vector3 force = lift + drag;
            IsStructurallyLimited = false;
            StructuralLimitReason = string.Empty;
            float peakStructuralFraction = 0f;
            float maximumForce = fin.MaximumAerodynamicForceN;
            if (maximumForce > 0f)
            {
                float forceFraction = force.magnitude / maximumForce;
                peakStructuralFraction = Mathf.Max(
                    peakStructuralFraction,
                    forceFraction);
                if (forceFraction > 1f)
                {
                    float scale = 1f / forceFraction;
                    lift *= scale;
                    drag *= scale;
                    force = lift + drag;
                    IsStructurallyLimited = true;
                    StructuralLimitReason = "Maximum aerodynamic force";
                }
            }
            Vector3 torque = Vector3.Cross(
                point - rootBody.worldCenterOfMass,
                force);
            float maximumMoment = fin.MaximumAerodynamicMomentNm;
            if (maximumMoment > 0f)
            {
                float momentFraction = torque.magnitude / maximumMoment;
                peakStructuralFraction = Mathf.Max(
                    peakStructuralFraction,
                    momentFraction);
                if (momentFraction > 1f)
                {
                    float scale = 1f / momentFraction;
                    lift *= scale;
                    drag *= scale;
                    force = lift + drag;
                    torque = Vector3.Cross(
                        point - rootBody.worldCenterOfMass,
                        force);
                    IsStructurallyLimited = true;
                    StructuralLimitReason = string.IsNullOrEmpty(StructuralLimitReason)
                        ? "Maximum aerodynamic moment"
                        : StructuralLimitReason + "; maximum aerodynamic moment";
                }
            }
            HasStructuralWarning = IsStructurallyLimited ||
                peakStructuralFraction >=
                fin.StructuralWarningFraction;
            CurrentWorldLiftForce = lift;
            CurrentWorldDragForce = drag;
            CurrentWorldAerodynamicForce = force;
            CurrentRelativeAirVelocity = relativeAir;
            CurrentDynamicPressurePa = dynamicPressure;
            CurrentMachNumber = environment.SpeedOfSoundMetersPerSecond > 0.001f
                ? speed / environment.SpeedOfSoundMetersPerSecond
                : 0f;
            CurrentWorldAerodynamicTorque = torque;
            CurrentAerodynamicForceN =
                CurrentWorldAerodynamicForce.magnitude;
            CurrentAngleOfAttackDegrees = angleOfAttack;
            CurrentLiftCoefficient = liftCoefficient;
            CurrentDragCoefficient = dragCoefficient;
            CurrentForceApplicationPoint = point;
            IsReverseFlow = Vector3.Dot(incomingInPlane, chord) < 0f;
            IsStalled = angleOfAttack > fin.PositiveStallAngleDegrees ||
                angleOfAttack < fin.NegativeStallAngleDegrees;
            rootBody.AddForceAtPosition(
                CurrentWorldAerodynamicForce,
                point,
                ForceMode.Force);
            SetRuntimeState(
                speed,
                CurrentAerodynamicForceN,
                0f,
                0f);
        }

        private void ClearAerodynamicState(Vector3 relativeAir)
        {
            CurrentAerodynamicForceN = 0f;
            CurrentWorldAerodynamicForce = Vector3.zero;
            CurrentWorldAerodynamicTorque = Vector3.zero;
            CurrentWorldLiftForce = Vector3.zero;
            CurrentWorldDragForce = Vector3.zero;
            CurrentRelativeAirVelocity = relativeAir;
            CurrentDynamicPressurePa = 0f;
            CurrentMachNumber = 0f;
            CurrentAngleOfAttackDegrees = 0f;
            CurrentLiftCoefficient = 0f;
            CurrentDragCoefficient = 0f;
            IsStalled = false;
            IsReverseFlow = false;
            IsStructurallyLimited = false;
            HasStructuralWarning = false;
            StructuralLimitReason = string.Empty;
        }
    }
}
