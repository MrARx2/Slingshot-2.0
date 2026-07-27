using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class RuntimeThrusterInstance : RuntimePartInstance
    {
        private Rigidbody rootRigidbody;
        private RuntimeSpringMountInstance springMount;
        private float spoolOutput;

        public ThrusterDefinition ThrusterDefinition => Definition as ThrusterDefinition;
        public Rigidbody RootRigidbody => rootRigidbody;
        public float SpoolOutput => spoolOutput;
        public float CurrentAppliedForceN { get; private set; }
        public Vector3 CurrentWorldForce { get; private set; }
        public bool IsEmergencyOverloadActive { get; private set; }

        public void BindRigidbody(Rigidbody value)
        {
            rootRigidbody = value;
            springMount = GetComponentInParent<RuntimeSpringMountInstance>();
        }

        public void ResetDynamicState()
        {
            spoolOutput = 0f;
            CurrentAppliedForceN = 0f;
            CurrentWorldForce = Vector3.zero;
            IsEmergencyOverloadActive = false;
            springMount?.ResetSpringState();
            SetRuntimeState(0f, 0f, DisabledDemand, 0f);
        }

        public float PreparePowerRequest(
            float requestedOutput,
            float deltaTime,
            bool emergencyOverload = false)
        {
            ThrusterDefinition thruster = ThrusterDefinition;
            if (thruster == null || !IsEnabled || IsThermallyLockedOut)
            {
                spoolOutput = 0f;
                IsEmergencyOverloadActive = false;
                SetRuntimeState(requestedOutput, 0f, DisabledDemand, 0f);
                return DisabledDemand;
            }

            float limitedRequest = LimitRequestedOutput(
                thruster,
                requestedOutput,
                emergencyOverload);
            limitedRequest *= ThermalOutputLimit;
            IsEmergencyOverloadActive =
                emergencyOverload &&
                Mathf.Abs(limitedRequest) >
                thruster.NormalOutputMultiplier + 0.0001f;
            float rate = Mathf.Abs(limitedRequest) > Mathf.Abs(spoolOutput)
                ? thruster.ThrustRisePerSecond
                : thruster.ThrustFallPerSecond;
            spoolOutput = rate <= 0f
                ? limitedRequest
                : Mathf.MoveTowards(spoolOutput, limitedRequest, rate * Mathf.Max(0f, deltaTime));

            if (Mathf.Abs(spoolOutput) > 0f &&
                Mathf.Abs(spoolOutput) < thruster.MinimumControllableOutput)
            {
                spoolOutput =
                    Mathf.Sign(spoolOutput) * thruster.MinimumControllableOutput;
            }

            float demand = CalculatePowerDemand(
                thruster,
                Mathf.Abs(spoolOutput));
            SetRuntimeState(limitedRequest, 0f, demand, 0f);
            return demand;
        }

        public void ApplyPowerGrant(float grantedPower, float deltaTime)
        {
            ThrusterDefinition thruster = ThrusterDefinition;
            if (thruster == null ||
                rootRigidbody == null ||
                !IsEnabled ||
                IsThermallyLockedOut)
            {
                springMount?.TickFromEndpointForce(
                    Vector3.zero,
                    Definition != null ? Definition.Physical.massKg : 0f,
                    deltaTime);
                CurrentAppliedForceN = 0f;
                CurrentWorldForce = Vector3.zero;
                SetRuntimeState(RequestedOutput, 0f, RequestedPower, 0f);
                return;
            }

            float idle = Mathf.Max(0f, thruster.Power.idleDemand);
            float variableDemand = Mathf.Max(0f, RequestedPower - idle);
            float usableGrant = Mathf.Max(0f, grantedPower - idle);
            float poweredFraction = variableDemand <= 0.0001f
                ? (grantedPower >= RequestedPower ? 1f : 0f)
                : Mathf.Clamp01(usableGrant / variableDemand);
            float actualOutput = spoolOutput * poweredFraction;

            float maximumForce = actualOutput >= 0f
                ? thruster.MaximumForwardForceN
                : thruster.MaximumReverseForceN;
            CurrentAppliedForceN = Mathf.Abs(actualOutput) * maximumForce;
            Vector3 direction = transform.TransformDirection(thruster.LocalThrustDirection);
            CurrentWorldForce = direction * (Mathf.Sign(actualOutput) * CurrentAppliedForceN);
            springMount?.TickFromEndpointForce(
                CurrentWorldForce,
                Definition.Physical.massKg,
                deltaTime);

            if (CurrentAppliedForceN > 0f)
            {
                Vector3 origin = transform.TransformPoint(thruster.LocalForceOrigin);
                rootRigidbody.AddForceAtPosition(
                    CurrentWorldForce,
                    origin,
                    ForceMode.Force);
            }

            SetRuntimeState(
                RequestedOutput,
                actualOutput,
                RequestedPower,
                grantedPower);
        }

        private float CalculatePowerDemand(
            ThrusterDefinition thruster,
            float output01)
        {
            PowerProfile power = Definition.Power;
            float idle = Mathf.Max(0f, power.idleDemand);
            float maximum = Mathf.Max(idle, power.maximumDemand);
            float magnitude = Mathf.Abs(output01);
            float normalLimit = thruster.NormalOutputMultiplier;
            float normalDemand = normalLimit <= 1f
                ? maximum
                : maximum * normalLimit;
            if (magnitude <= normalLimit)
            {
                return magnitude <= 1f
                ? Mathf.Lerp(idle, maximum, magnitude)
                : maximum * magnitude;
            }

            float overload01 = Mathf.InverseLerp(
                normalLimit,
                thruster.OverloadOutputMultiplier,
                magnitude);
            return normalDemand * Mathf.Lerp(
                1f,
                Mathf.Max(1f, power.overloadDemandMultiplier),
                overload01);
        }

        private static float LimitRequestedOutput(
            ThrusterDefinition thruster,
            float requestedOutput,
            bool emergencyOverload)
        {
            float minimum = thruster.MaximumReverseForceN > 0f ? -1f : 0f;
            float maximum = emergencyOverload
                ? thruster.OverloadOutputMultiplier
                : thruster.NormalOutputMultiplier;
            return Mathf.Clamp(
                requestedOutput,
                minimum,
                maximum);
        }

        private float DisabledDemand =>
            Definition != null ? Mathf.Max(0f, Definition.Power.disabledDemand) : 0f;
    }
}
