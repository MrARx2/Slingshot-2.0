using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3AtmosphereCalculationMode
    {
        StandardLapseRate,
        Constant,
        CustomDensity
    }

    [CreateAssetMenu(
        menuName = "Hovercraft V3/World/World Profile",
        fileName = "V3WorldProfile")]
    public sealed class V3WorldProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string stableId = "world.earth_standard";
        [SerializeField] private string displayName = "Earth Standard";
        [TextArea(2, 5), SerializeField] private string description =
            "Earth-like baseline atmosphere for Hovercraft V3 development.";
        [Min(1), SerializeField] private int configurationVersion = 1;

        [Header("Reference Frame (meters)")]
        [Tooltip("World-space Y treated as the altitude datum.")]
        [SerializeField] private float referenceWorldY;
        [Tooltip("Additional altitude added after subtracting the datum.")]
        [SerializeField] private float altitudeOffsetMeters;

        [Header("Uniform Gravity (m/s²)")]
        [SerializeField] private bool gravityEnabled = true;
        [SerializeField] private Vector3 gravityDirection = Vector3.down;
        [Min(0f), SerializeField] private float gravityMagnitude = 9.80665f;
        [SerializeField] private string gravityModelId = "uniform_directional.v1";

        [Header("Atmosphere")]
        [SerializeField] private bool atmosphereEnabled = true;
        [SerializeField] private V3AtmosphereCalculationMode atmosphereMode =
            V3AtmosphereCalculationMode.StandardLapseRate;
        [Tooltip("Reference temperature at the altitude datum, in Celsius.")]
        [SerializeField] private float seaLevelTemperatureC = 15f;
        [Tooltip("Reference pressure at the altitude datum, in Pascals.")]
        [Min(1f), SerializeField] private float seaLevelPressurePa = 101325f;
        [Tooltip("Used only by Custom Density mode, in kg/m³.")]
        [Min(0.0001f), SerializeField] private float customSeaLevelDensity = 1.225f;
        [Tooltip("Temperature decrease per meter of altitude, in K/m.")]
        [Min(0f), SerializeField] private float temperatureLapseRateKPerMeter =
            0.0065f;
        [Tooltip("Lowest permitted atmospheric temperature, in Kelvin.")]
        [Min(1f), SerializeField] private float minimumTemperatureK = 180f;
        [Min(1f), SerializeField] private float maximumAltitudeMeters = 20000f;

        [Header("Wind (m/s)")]
        [SerializeField] private bool windEnabled = true;
        [SerializeField] private Vector3 globalWindDirection = Vector3.forward;
        [Min(0f), SerializeField] private float globalWindSpeed;
        [Min(0f), SerializeField] private float gustStrength;
        [Min(0.001f), SerializeField] private float gustTimeScale = 0.08f;
        [Min(0.001f), SerializeField] private float gustSpatialScale = 0.002f;
        [SerializeField] private bool turbulenceEnabled = true;
        [Min(0f), SerializeField] private float turbulenceStrength;
        [Min(0.001f), SerializeField] private float turbulenceSpatialScale = 0.01f;
        [Min(0.001f), SerializeField] private float turbulenceTemporalScale = 0.2f;
        [Range(0f, 1f), SerializeField] private float turbulenceVerticalContribution =
            0.35f;
        [SerializeField] private int deterministicWindSeed = 314159;

        [Header("Thermal Environment")]
        [SerializeField] private bool thermalEnvironmentEnabled = true;
        [Min(0f), SerializeField] private float baselineCoolingMultiplier = 1f;
        [Min(0f), SerializeField] private float minimumCoolingMultiplier = 0.1f;
        [Min(0f), SerializeField] private float maximumCoolingMultiplier = 5f;
        [Min(0.01f), SerializeField] private float coolingReferenceAirspeed = 100f;
        [Min(1f), SerializeField] private float maximumAirflowCoolingMultiplier = 4f;

        [Header("Features")]
        [SerializeField] private bool environmentZonesEnabled = true;
        [SerializeField] private bool debuggingEnabled = true;
        [SerializeField] private bool recorderIntegrationEnabled = true;

        public string StableId => stableId ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name : displayName;
        public string Description => description ?? string.Empty;
        public int ConfigurationVersion => Mathf.Max(1, configurationVersion);
        public float ReferenceWorldY => referenceWorldY;
        public float AltitudeOffsetMeters => altitudeOffsetMeters;
        public bool GravityEnabled => gravityEnabled;
        public Vector3 GravityDirection => gravityDirection.sqrMagnitude > 0.000001f
            ? gravityDirection.normalized : Vector3.down;
        public float GravityMagnitude => Mathf.Max(0f, gravityMagnitude);
        public Vector3 GravityVector => GravityEnabled
            ? GravityDirection * GravityMagnitude : Vector3.zero;
        public string GravityModelId => gravityModelId ?? string.Empty;
        public bool AtmosphereEnabled => atmosphereEnabled;
        public V3AtmosphereCalculationMode AtmosphereMode => atmosphereMode;
        public float SeaLevelTemperatureC => seaLevelTemperatureC;
        public float SeaLevelTemperatureK => Mathf.Max(
            minimumTemperatureK,
            seaLevelTemperatureC + 273.15f);
        public float SeaLevelPressurePa => Mathf.Max(1f, seaLevelPressurePa);
        public float CustomSeaLevelDensity => Mathf.Max(
            0.0001f,
            customSeaLevelDensity);
        public float TemperatureLapseRateKPerMeter => Mathf.Max(
            0f,
            temperatureLapseRateKPerMeter);
        public float MinimumTemperatureK => Mathf.Max(1f, minimumTemperatureK);
        public float MaximumAltitudeMeters => Mathf.Max(1f, maximumAltitudeMeters);
        public bool WindEnabled => windEnabled;
        public Vector3 GlobalWindDirection => globalWindDirection.sqrMagnitude >
            0.000001f ? globalWindDirection.normalized : Vector3.forward;
        public float GlobalWindSpeed => Mathf.Max(0f, globalWindSpeed);
        public float GustStrength => Mathf.Max(0f, gustStrength);
        public float GustTimeScale => Mathf.Max(0.001f, gustTimeScale);
        public float GustSpatialScale => Mathf.Max(0.001f, gustSpatialScale);
        public bool TurbulenceEnabled => turbulenceEnabled;
        public float TurbulenceStrength => Mathf.Max(0f, turbulenceStrength);
        public float TurbulenceSpatialScale => Mathf.Max(
            0.001f,
            turbulenceSpatialScale);
        public float TurbulenceTemporalScale => Mathf.Max(
            0.001f,
            turbulenceTemporalScale);
        public float TurbulenceVerticalContribution => Mathf.Clamp01(
            turbulenceVerticalContribution);
        public int DeterministicWindSeed => deterministicWindSeed;
        public bool ThermalEnvironmentEnabled => thermalEnvironmentEnabled;
        public float BaselineCoolingMultiplier => Mathf.Clamp(
            baselineCoolingMultiplier,
            MinimumCoolingMultiplier,
            MaximumCoolingMultiplier);
        public float MinimumCoolingMultiplier => Mathf.Max(
            0f,
            minimumCoolingMultiplier);
        public float MaximumCoolingMultiplier => Mathf.Max(
            MinimumCoolingMultiplier,
            maximumCoolingMultiplier);
        public float CoolingReferenceAirspeed => Mathf.Max(
            0.01f,
            coolingReferenceAirspeed);
        public float MaximumAirflowCoolingMultiplier => Mathf.Max(
            1f,
            maximumAirflowCoolingMultiplier);
        public bool EnvironmentZonesEnabled => environmentZonesEnabled;
        public bool DebuggingEnabled => debuggingEnabled;
        public bool RecorderIntegrationEnabled => recorderIntegrationEnabled;

        public float DerivedSeaLevelDensity => AtmosphereMode ==
            V3AtmosphereCalculationMode.CustomDensity
                ? CustomSeaLevelDensity
                : SeaLevelPressurePa /
                  (V3AtmosphereField.SpecificGasConstantAir *
                   SeaLevelTemperatureK);

        private void OnValidate()
        {
            configurationVersion = Mathf.Max(1, configurationVersion);
            gravityMagnitude = Mathf.Max(0f, gravityMagnitude);
            if (gravityDirection.sqrMagnitude <= 0.000001f)
            {
                gravityDirection = Vector3.down;
            }
            seaLevelPressurePa = Mathf.Max(1f, seaLevelPressurePa);
            customSeaLevelDensity = Mathf.Max(0.0001f, customSeaLevelDensity);
            minimumTemperatureK = Mathf.Max(1f, minimumTemperatureK);
            maximumAltitudeMeters = Mathf.Max(1f, maximumAltitudeMeters);
            minimumCoolingMultiplier = Mathf.Max(0f, minimumCoolingMultiplier);
            maximumCoolingMultiplier = Mathf.Max(
                minimumCoolingMultiplier,
                maximumCoolingMultiplier);
            baselineCoolingMultiplier = Mathf.Clamp(
                baselineCoolingMultiplier,
                minimumCoolingMultiplier,
                maximumCoolingMultiplier);
        }
    }
}
