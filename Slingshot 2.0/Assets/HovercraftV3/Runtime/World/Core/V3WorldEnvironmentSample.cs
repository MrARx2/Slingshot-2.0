using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public readonly struct V3WorldClockState
    {
        public V3WorldClockState(double simulationTime, long physicsTickId)
        {
            SimulationTime = simulationTime;
            PhysicsTickId = physicsTickId;
        }

        public double SimulationTime { get; }
        public long PhysicsTickId { get; }
        public bool IsValid => PhysicsTickId >= 0;
    }

    [Flags]
    public enum V3WorldSampleFlags
    {
        None = 0,
        MissingWorld = 1 << 0,
        DuplicateWorld = 1 << 1,
        MissingProfile = 1 << 2,
        InvalidProfile = 1 << 3,
        AtmosphereDisabled = 1 << 4,
        AltitudeClamped = 1 << 5,
        ZoneCapacityExceeded = 1 << 6
    }

    [Flags]
    public enum V3WorldZoneOperations
    {
        None = 0,
        WindOverride = 1 << 0,
        WindAdditive = 1 << 1,
        WindMultiplier = 1 << 2,
        TemperatureOverride = 1 << 3,
        TemperatureAdditive = 1 << 4,
        TemperatureMultiplier = 1 << 5,
        PressureOverride = 1 << 6,
        PressureAdditive = 1 << 7,
        PressureMultiplier = 1 << 8,
        DensityOverride = 1 << 9,
        DensityAdditive = 1 << 10,
        DensityMultiplier = 1 << 11,
        CoolingOverride = 1 << 12,
        CoolingAdditive = 1 << 13,
        CoolingMultiplier = 1 << 14,
        TurbulenceOverride = 1 << 15,
        TurbulenceAdditive = 1 << 16,
        TurbulenceMultiplier = 1 << 17
    }

    [Serializable]
    public struct V3WorldEnvironmentSample
    {
        public bool IsValid;
        public V3WorldSampleFlags Flags;
        public string WorldRootId;
        public string WorldProfileId;
        public string WorldProfileName;
        public int WorldConfigurationVersion;
        public Vector3 WorldPosition;
        public double SimulationTime;
        public long PhysicsTickId;
        public float RawWorldY;
        public float AltitudeMeters;
        public float AtmosphereAltitudeMeters;
        public Vector3 GravityVector;
        public float GravityMagnitude;
        public bool AtmosphereEnabled;
        public float AmbientTemperatureC;
        public float AmbientTemperatureK;
        public float AtmosphericPressurePa;
        public float AirDensityKgPerCubicMeter;
        public float ReferenceAirDensityKgPerCubicMeter;
        public float SpeedOfSoundMetersPerSecond;
        public float DynamicViscosityPascalSeconds;
        public Vector3 BaseWindVelocity;
        public Vector3 ZoneWindContribution;
        public Vector3 GustVelocity;
        public Vector3 TurbulenceVelocity;
        public Vector3 LocalAirVelocity;
        public float TurbulenceIntensity;
        public int WindDiagnosticSeed;
        public float EnvironmentalCoolingMultiplier;
        public float LocalThermalOffsetC;
        public float CoolingReferenceAirspeed;
        public float MaximumAirflowCoolingMultiplier;
        public int ActiveZoneCount;
        public string DominantZoneId;
        public int DominantZonePriority;
        public float DominantZoneWeight;
        public string Zone0Id;
        public float Zone0Weight;
        public string Zone1Id;
        public float Zone1Weight;
        public string Zone2Id;
        public float Zone2Weight;
        public string Zone3Id;
        public float Zone3Weight;
        public V3WorldZoneOperations AppliedZoneOperations;

        public float DensityRatio => ReferenceAirDensityKgPerCubicMeter > 0f
            ? AirDensityKgPerCubicMeter /
              ReferenceAirDensityKgPerCubicMeter
            : 0f;

        public string GetActiveZoneId(int index)
        {
            return index switch
            {
                0 => Zone0Id,
                1 => Zone1Id,
                2 => Zone2Id,
                3 => Zone3Id,
                _ => string.Empty
            };
        }

        public float GetActiveZoneWeight(int index)
        {
            return index switch
            {
                0 => Zone0Weight,
                1 => Zone1Weight,
                2 => Zone2Weight,
                3 => Zone3Weight,
                _ => 0f
            };
        }

        public void RecordZone(string id, float weight)
        {
            int index = ActiveZoneCount;
            ActiveZoneCount++;
            switch (index)
            {
                case 0:
                    Zone0Id = id;
                    Zone0Weight = weight;
                    break;
                case 1:
                    Zone1Id = id;
                    Zone1Weight = weight;
                    break;
                case 2:
                    Zone2Id = id;
                    Zone2Weight = weight;
                    break;
                case 3:
                    Zone3Id = id;
                    Zone3Weight = weight;
                    break;
                default:
                    Flags |= V3WorldSampleFlags.ZoneCapacityExceeded;
                    break;
            }
        }

        public static V3WorldEnvironmentSample Invalid(
            Vector3 position,
            V3WorldSampleFlags flags,
            V3WorldClockState clock = default)
        {
            return new V3WorldEnvironmentSample
            {
                IsValid = false,
                Flags = flags,
                WorldPosition = position,
                RawWorldY = position.y,
                SimulationTime = clock.SimulationTime,
                PhysicsTickId = clock.IsValid ? clock.PhysicsTickId : -1,
                DominantZoneId = string.Empty,
                Zone0Id = string.Empty,
                Zone1Id = string.Empty,
                Zone2Id = string.Empty,
                Zone3Id = string.Empty
            };
        }
    }

    public readonly struct V3ThermalEnvironmentInput
    {
        public V3ThermalEnvironmentInput(
            float ambientTemperatureC,
            float densityRatio,
            float relativeAirspeed,
            float environmentMultiplier,
            float coolingReferenceAirspeed,
            float maximumAirflowMultiplier)
        {
            AmbientTemperatureC = ambientTemperatureC;
            DensityRatio = Mathf.Max(0f, densityRatio);
            RelativeAirspeed = Mathf.Max(0f, relativeAirspeed);
            EnvironmentMultiplier = Mathf.Max(0f, environmentMultiplier);
            CoolingReferenceAirspeed = Mathf.Max(0.01f, coolingReferenceAirspeed);
            MaximumAirflowMultiplier = Mathf.Max(1f, maximumAirflowMultiplier);
        }

        public float AmbientTemperatureC { get; }
        public float DensityRatio { get; }
        public float RelativeAirspeed { get; }
        public float EnvironmentMultiplier { get; }
        public float CoolingReferenceAirspeed { get; }
        public float MaximumAirflowMultiplier { get; }
        public float AirflowMultiplier => Mathf.Min(
            MaximumAirflowMultiplier,
            1f + RelativeAirspeed / CoolingReferenceAirspeed);
        public float EffectiveCoolingMultiplier =>
            EnvironmentMultiplier * DensityRatio * AirflowMultiplier;
    }
}
