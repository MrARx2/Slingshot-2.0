using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public static class V3AtmosphereField
    {
        public const float SpecificGasConstantAir = 287.05f;
        private const float HeatCapacityRatio = 1.4f;
        private const float SutherlandConstantK = 110.4f;
        private const float ReferenceViscosity = 0.00001716f;
        private const float ReferenceViscosityTemperatureK = 273.15f;

        public static void Sample(
            V3WorldProfile profile,
            float altitudeMeters,
            ref V3WorldEnvironmentSample sample)
        {
            float clampedAltitude = Mathf.Clamp(
                altitudeMeters,
                0f,
                profile.MaximumAltitudeMeters);
            sample.AtmosphereAltitudeMeters = clampedAltitude;
            if (!Mathf.Approximately(clampedAltitude, altitudeMeters))
            {
                sample.Flags |= V3WorldSampleFlags.AltitudeClamped;
            }

            sample.ReferenceAirDensityKgPerCubicMeter =
                profile.DerivedSeaLevelDensity;
            if (!profile.AtmosphereEnabled)
            {
                sample.Flags |= V3WorldSampleFlags.AtmosphereDisabled;
                sample.AtmosphereEnabled = false;
                sample.AmbientTemperatureC = profile.SeaLevelTemperatureC;
                sample.AmbientTemperatureK = profile.SeaLevelTemperatureK;
                sample.AtmosphericPressurePa = 0f;
                sample.AirDensityKgPerCubicMeter = 0f;
                sample.SpeedOfSoundMetersPerSecond = 0f;
                sample.DynamicViscosityPascalSeconds = 0f;
                return;
            }

            sample.AtmosphereEnabled = true;
            float baseTemperature = profile.SeaLevelTemperatureK;
            float lapse = profile.TemperatureLapseRateKPerMeter;
            float temperature = profile.AtmosphereMode ==
                V3AtmosphereCalculationMode.Constant
                    ? baseTemperature
                    : Mathf.Max(
                        profile.MinimumTemperatureK,
                        baseTemperature - lapse * clampedAltitude);
            float gravity = profile.GravityMagnitude;
            float pressure;
            if (profile.AtmosphereMode ==
                V3AtmosphereCalculationMode.Constant || lapse <= 0.0000001f)
            {
                pressure = profile.SeaLevelPressurePa * Mathf.Exp(
                    -gravity * clampedAltitude /
                    (SpecificGasConstantAir * temperature));
            }
            else
            {
                float exponent = gravity /
                    (SpecificGasConstantAir * lapse);
                pressure = profile.SeaLevelPressurePa * Mathf.Pow(
                    Mathf.Clamp01(temperature / baseTemperature),
                    exponent);
            }

            pressure = Mathf.Max(0f, pressure);
            float density = profile.AtmosphereMode ==
                V3AtmosphereCalculationMode.CustomDensity
                    ? profile.CustomSeaLevelDensity *
                      pressure / profile.SeaLevelPressurePa *
                      baseTemperature / temperature
                    : pressure /
                      (SpecificGasConstantAir * temperature);
            sample.AmbientTemperatureK = temperature;
            sample.AmbientTemperatureC = temperature - 273.15f;
            sample.AtmosphericPressurePa = Mathf.Max(0f, pressure);
            sample.AirDensityKgPerCubicMeter = Mathf.Max(0f, density);
            sample.SpeedOfSoundMetersPerSecond =
                CalculateSpeedOfSound(temperature);
            sample.DynamicViscosityPascalSeconds =
                CalculateDynamicViscosity(temperature);
        }

        public static float CalculateSpeedOfSound(float temperatureK)
        {
            return Mathf.Sqrt(
                HeatCapacityRatio * SpecificGasConstantAir *
                Mathf.Max(1f, temperatureK));
        }

        public static float CalculateDynamicViscosity(float temperatureK)
        {
            float temperature = Mathf.Max(1f, temperatureK);
            return ReferenceViscosity *
                Mathf.Pow(temperature / ReferenceViscosityTemperatureK, 1.5f) *
                (ReferenceViscosityTemperatureK + SutherlandConstantK) /
                (temperature + SutherlandConstantK);
        }
    }

    public static class V3WindField
    {
        public static void Sample(
            V3WorldProfile profile,
            Vector3 position,
            double simulationTime,
            ref V3WorldEnvironmentSample sample)
        {
            sample.WindDiagnosticSeed = profile.DeterministicWindSeed;
            if (!profile.WindEnabled)
            {
                sample.BaseWindVelocity = Vector3.zero;
                sample.GustVelocity = Vector3.zero;
                sample.TurbulenceVelocity = Vector3.zero;
                sample.LocalAirVelocity = Vector3.zero;
                return;
            }

            Vector3 direction = profile.GlobalWindDirection;
            sample.BaseWindVelocity = direction * profile.GlobalWindSpeed;
            float seed = profile.DeterministicWindSeed * 0.001371f;
            float time = (float)simulationTime;
            float gustNoise = SignedNoise(
                position.x * profile.GustSpatialScale + seed,
                position.z * profile.GustSpatialScale +
                time * profile.GustTimeScale - seed);
            sample.GustVelocity = direction *
                (gustNoise * profile.GustStrength);

            if (!profile.TurbulenceEnabled ||
                profile.TurbulenceStrength <= 0f)
            {
                sample.TurbulenceVelocity = Vector3.zero;
                sample.TurbulenceIntensity = 0f;
                sample.LocalAirVelocity = sample.BaseWindVelocity +
                    sample.GustVelocity;
                return;
            }

            float spatial = profile.TurbulenceSpatialScale;
            float temporal = time * profile.TurbulenceTemporalScale;
            float x = SignedNoise(
                position.y * spatial + seed + 11.7f,
                position.z * spatial + temporal + 3.1f);
            float y = SignedNoise(
                position.z * spatial + seed + 31.9f,
                position.x * spatial - temporal + 7.4f) *
                profile.TurbulenceVerticalContribution;
            float z = SignedNoise(
                position.x * spatial - seed + 53.2f,
                position.y * spatial + temporal + 19.6f);
            Vector3 normalizedNoise = new Vector3(x, y, z);
            sample.TurbulenceVelocity = normalizedNoise *
                profile.TurbulenceStrength;
            sample.TurbulenceIntensity = Mathf.Clamp01(
                normalizedNoise.magnitude / 1.7320508f);
            sample.LocalAirVelocity = sample.BaseWindVelocity +
                sample.GustVelocity + sample.TurbulenceVelocity;
        }

        private static float SignedNoise(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 2f - 1f;
        }
    }
}
