using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public class RuntimePartInstance : MonoBehaviour
    {
        public PartDefinition Definition { get; private set; }
        public string ParentSocketId { get; private set; }
        public float RequestedOutput { get; private set; }
        public float CurrentOutput { get; private set; }
        public float RequestedPower { get; private set; }
        public float GrantedPower { get; private set; }
        public float CurrentTemperatureC { get; private set; }
        public float GeneratedHeatPerSecond { get; private set; }
        public float PassiveCoolingPerSecond { get; private set; }
        public bool IsEnabled { get; private set; }
        public bool IsOverheated { get; private set; }
        public bool IsThermallyLockedOut { get; private set; }
        public V3ThermalProtectionState ThermalProtectionState { get; private set; }
        public float ThermalOutputLimit { get; private set; } = 1f;
        private float coolingLockoutRemainingSeconds;
        public bool IsAboveSafeTemperature =>
            Definition != null &&
            Definition.Thermal.enabled &&
            CurrentTemperatureC >= Definition.Thermal.maximumSafeTemperatureC;
        public float NormalizedTemperature
        {
            get
            {
                if (Definition == null || !Definition.Thermal.enabled)
                {
                    return 0f;
                }

                ThermalProfile thermal = Definition.Thermal;
                return Mathf.InverseLerp(
                    thermal.ambientTemperatureC,
                    Mathf.Max(
                        thermal.ambientTemperatureC + 0.001f,
                        thermal.overheatTemperatureC),
                    CurrentTemperatureC);
            }
        }

        public virtual void Initialize(PartDefinition definition, string parentSocketId)
        {
            Definition = definition;
            ParentSocketId = parentSocketId;
            RequestedOutput = 0f;
            CurrentOutput = 0f;
            RequestedPower = 0f;
            GrantedPower = 0f;
            CurrentTemperatureC = definition != null && definition.Thermal.enabled
                ? definition.Thermal.ambientTemperatureC
                : 0f;
            GeneratedHeatPerSecond = 0f;
            PassiveCoolingPerSecond = 0f;
            IsEnabled = true;
            IsOverheated = false;
            IsThermallyLockedOut = false;
            ThermalProtectionState = V3ThermalProtectionState.Normal;
            ThermalOutputLimit = 1f;
            coolingLockoutRemainingSeconds = 0f;
        }

        public void SetRuntimeState(
            float requestedOutput,
            float currentOutput,
            float requestedPower,
            float grantedPower)
        {
            RequestedOutput = requestedOutput;
            CurrentOutput = currentOutput;
            RequestedPower = Mathf.Max(0f, requestedPower);
            GrantedPower = Mathf.Clamp(grantedPower, 0f, RequestedPower);
        }

        public void SetEnabled(bool value)
        {
            IsEnabled = value;
        }

        public void TickThermal(float deltaTime)
        {
            if (Definition == null || !Definition.Thermal.enabled || deltaTime <= 0f)
            {
                GeneratedHeatPerSecond = 0f;
                PassiveCoolingPerSecond = 0f;
                return;
            }

            ThermalProfile thermal = Definition.Thermal;
            float normalOutputLimit = 1f;
            float overloadOutputLimit = 1f;
            if (Definition is ThrusterDefinition thruster)
            {
                normalOutputLimit = thruster.NormalOutputMultiplier;
                overloadOutputLimit = thruster.OverloadOutputMultiplier;
            }

            GeneratedHeatPerSecond =
                CalculateGeneratedHeatPerSecond(
                    thermal,
                    CurrentOutput,
                    normalOutputLimit,
                    overloadOutputLimit);
            PassiveCoolingPerSecond =
                CalculatePassiveCoolingPerSecond(
                    thermal,
                    CurrentTemperatureC);
            CurrentTemperatureC +=
                (GeneratedHeatPerSecond - PassiveCoolingPerSecond) /
                Mathf.Max(0.001f, thermal.thermalCapacity) *
                deltaTime;
            CurrentTemperatureC = Mathf.Max(thermal.ambientTemperatureC, CurrentTemperatureC);

            IsOverheated = CurrentTemperatureC >= thermal.overheatTemperatureC;
            UpdateThermalProtection(thermal, deltaTime);
        }

        public void ResetThermalState()
        {
            CurrentTemperatureC =
                Definition != null && Definition.Thermal.enabled
                    ? Definition.Thermal.ambientTemperatureC
                    : 0f;
            GeneratedHeatPerSecond = 0f;
            PassiveCoolingPerSecond = 0f;
            IsOverheated = false;
            IsThermallyLockedOut = false;
            ThermalProtectionState = V3ThermalProtectionState.Normal;
            ThermalOutputLimit = 1f;
            coolingLockoutRemainingSeconds = 0f;
        }

        public static float CalculateGeneratedHeatPerSecond(
            ThermalProfile thermal,
            float currentOutput,
            float normalOutputLimit = 1f,
            float overloadOutputLimit = 1f)
        {
            float magnitude = Mathf.Abs(currentOutput);
            float normalLimit = Mathf.Max(0.001f, normalOutputLimit);
            float overloadLimit = Mathf.Max(normalLimit, overloadOutputLimit);
            float normalOutput = Mathf.Clamp01(magnitude / normalLimit);
            float normalHeat = Mathf.Lerp(
                Mathf.Max(0f, thermal.idleHeatPerSecond),
                Mathf.Max(0f, thermal.maximumOutputHeatPerSecond),
                normalOutput);
            if (magnitude <= normalLimit)
            {
                return normalHeat;
            }

            float overload01 = overloadLimit <= normalLimit
                ? 1f
                : Mathf.InverseLerp(normalLimit, overloadLimit, magnitude);
            float maximumHeat =
                Mathf.Max(0f, thermal.maximumOutputHeatPerSecond);
            return Mathf.Lerp(
                maximumHeat,
                maximumHeat * Mathf.Max(1f, thermal.overloadHeatMultiplier),
                overload01);
        }

        public static float CalculatePassiveCoolingPerSecond(
            ThermalProfile thermal,
            float temperatureC)
        {
            return Mathf.Max(
                0f,
                temperatureC - thermal.ambientTemperatureC) *
                Mathf.Max(0f, thermal.passiveCoolingPerSecond);
        }

        private void UpdateThermalProtection(
            ThermalProfile thermal,
            float deltaTime)
        {
            if (IsThermallyLockedOut)
            {
                coolingLockoutRemainingSeconds = Mathf.Max(
                    0f,
                    coolingLockoutRemainingSeconds - deltaTime);
                if (CurrentTemperatureC <= thermal.restartTemperatureC &&
                    coolingLockoutRemainingSeconds <= 0f)
                {
                    IsThermallyLockedOut = false;
                    ThermalProtectionState =
                        V3ThermalProtectionState.Recovered;
                    ThermalOutputLimit = 1f;
                    return;
                }

                if (ThermalProtectionState ==
                    V3ThermalProtectionState.Overheated)
                {
                    ThermalProtectionState =
                        V3ThermalProtectionState.CoolingLockout;
                }

                ThermalOutputLimit = 0f;
                return;
            }

            if (CurrentTemperatureC >= thermal.overheatTemperatureC)
            {
                IsThermallyLockedOut = true;
                coolingLockoutRemainingSeconds =
                    Mathf.Max(0f, thermal.coolingLockoutSeconds);
                ThermalProtectionState =
                    V3ThermalProtectionState.Overheated;
                ThermalOutputLimit = 0f;
                return;
            }

            if (ThermalProtectionState ==
                V3ThermalProtectionState.Recovered)
            {
                ThermalProtectionState =
                    V3ThermalProtectionState.Normal;
            }

            if (CurrentTemperatureC >= thermal.maximumSafeTemperatureC)
            {
                ThermalProtectionState =
                    V3ThermalProtectionState.Warning;
                ThermalOutputLimit = Mathf.Lerp(
                    1f,
                    Mathf.Clamp01(thermal.hotOutputLimitAtOverheat),
                    Mathf.InverseLerp(
                        thermal.maximumSafeTemperatureC,
                        thermal.overheatTemperatureC,
                        CurrentTemperatureC));
                return;
            }

            ThermalProtectionState = V3ThermalProtectionState.Normal;
            ThermalOutputLimit = 1f;
        }
    }
}
