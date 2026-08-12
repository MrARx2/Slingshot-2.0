using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [Serializable]
    public struct V3PartThermalTelemetry
    {
        public string socketId;
        public string stablePartId;
        public string displayName;
        public float currentTemperatureC;
        public float ambientTemperatureC;
        public float maximumSafeTemperatureC;
        public float overheatTemperatureC;
        public float restartTemperatureC;
        public float normalizedTemperature;
        public float generatedHeatPerSecond;
        public float passiveCoolingPerSecond;
        public bool isAboveSafeTemperature;
        public bool isOverheated;
        public bool isThermallyLockedOut;
        public V3ThermalProtectionState protectionState;
        public float outputLimit;
        public bool isEnabled;
    }

    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class V3ThermalController : MonoBehaviour
    {
        private readonly List<V3PartThermalTelemetry> telemetry =
            new List<V3PartThermalTelemetry>(16);
        private V3CraftRuntime runtime;
        private V3WorldEnvironmentProvider environment;

        public IReadOnlyList<V3PartThermalTelemetry> Telemetry => telemetry;
        public float MaximumTemperatureC { get; private set; }
        public int HotPartCount { get; private set; }
        public int OverheatedPartCount { get; private set; }
        public int LockedOutPartCount { get; private set; }

        public void Initialize(V3CraftRuntime craftRuntime)
        {
            runtime = craftRuntime;
            environment = runtime != null
                ? runtime.GetComponent<V3WorldEnvironmentProvider>()
                : null;
            ResetDynamicState();
        }

        private void FixedUpdate()
        {
            Tick(Time.fixedDeltaTime);
        }

        public void Tick(float deltaTime)
        {
            if (runtime == null)
            {
                telemetry.Clear();
                MaximumTemperatureC = 0f;
                HotPartCount = 0;
                OverheatedPartCount = 0;
                LockedOutPartCount = 0;
                return;
            }

            V3WorldEnvironmentSample sample = environment != null
                ? environment.CurrentSample
                : default;
            Rigidbody body = runtime.RootRigidbody;
            float relativeAirspeed = sample.IsValid && body != null
                ? (body.linearVelocity - sample.LocalAirVelocity).magnitude
                : 0f;
            V3ThermalEnvironmentInput thermalEnvironment = sample.IsValid
                ? new V3ThermalEnvironmentInput(
                    sample.AmbientTemperatureC,
                    sample.DensityRatio,
                    relativeAirspeed,
                    sample.EnvironmentalCoolingMultiplier,
                    sample.CoolingReferenceAirspeed,
                    sample.MaximumAirflowCoolingMultiplier)
                : default;
            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                if (part == null)
                {
                    continue;
                }
                if (sample.IsValid)
                {
                    part.TickThermal(deltaTime, thermalEnvironment);
                }
                else
                {
                    part.TickThermal(deltaTime);
                }
            }

            RefreshTelemetry();
        }

        public void ResetDynamicState()
        {
            if (runtime != null)
            {
                for (int i = 0; i < runtime.InstalledParts.Count; i++)
                {
                    runtime.InstalledParts[i]?.ResetThermalState();
                }
            }

            RefreshTelemetry();
        }

        public void RefreshTelemetry()
        {
            telemetry.Clear();
            MaximumTemperatureC = 0f;
            HotPartCount = 0;
            OverheatedPartCount = 0;
            LockedOutPartCount = 0;
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                if (part == null ||
                    part.Definition == null ||
                    !part.Definition.Thermal.enabled)
                {
                    continue;
                }

                ThermalProfile thermal = part.Definition.Thermal;
                telemetry.Add(new V3PartThermalTelemetry
                {
                    socketId = part.ParentSocketId,
                    stablePartId = part.Definition.StableId,
                    displayName = part.Definition.DisplayName,
                    currentTemperatureC = part.CurrentTemperatureC,
                    ambientTemperatureC = part.CurrentAmbientTemperatureC,
                    maximumSafeTemperatureC =
                        thermal.maximumSafeTemperatureC,
                    overheatTemperatureC = thermal.overheatTemperatureC,
                    restartTemperatureC = thermal.restartTemperatureC,
                    normalizedTemperature = part.NormalizedTemperature,
                    generatedHeatPerSecond = part.GeneratedHeatPerSecond,
                    passiveCoolingPerSecond = part.PassiveCoolingPerSecond,
                    isAboveSafeTemperature = part.IsAboveSafeTemperature,
                    isOverheated = part.IsOverheated,
                    isThermallyLockedOut = part.IsThermallyLockedOut,
                    protectionState = part.ThermalProtectionState,
                    outputLimit = part.ThermalOutputLimit,
                    isEnabled = part.IsEnabled
                });

                MaximumTemperatureC = Mathf.Max(
                    MaximumTemperatureC,
                    part.CurrentTemperatureC);
                if (part.IsAboveSafeTemperature)
                {
                    HotPartCount++;
                }

                if (part.IsOverheated)
                {
                    OverheatedPartCount++;
                }

                if (part.IsThermallyLockedOut)
                {
                    LockedOutPartCount++;
                }
            }
        }
    }
}
