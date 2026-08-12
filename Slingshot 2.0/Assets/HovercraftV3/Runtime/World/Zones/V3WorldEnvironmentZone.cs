using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3EnvironmentZoneShape
    {
        Box,
        Sphere
    }

    public enum V3EnvironmentZoneOperation
    {
        None,
        Override,
        Additive,
        Multiplier
    }

    [DisallowMultipleComponent]
    public sealed class V3WorldEnvironmentZone : MonoBehaviour
    {
        [Header("Identity and Volume")]
        [SerializeField] private string stableId = "zone.environment";
        [SerializeField] private V3EnvironmentZoneShape shape =
            V3EnvironmentZoneShape.Box;
        [SerializeField] private int priority;
        [Min(0f), SerializeField] private float blendDistance = 10f;
        [Range(0f, 1f), SerializeField] private float interiorStrength = 1f;
        [SerializeField] private AnimationCurve blendCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private Vector3 boxSize = new Vector3(50f, 20f, 50f);
        [Min(0.01f), SerializeField] private float sphereRadius = 25f;

        [Header("Wind")]
        [SerializeField] private V3EnvironmentZoneOperation windOperation =
            V3EnvironmentZoneOperation.None;
        [SerializeField] private Vector3 windVelocity;
        [Min(0f), SerializeField] private float windMultiplier = 1f;

        [Header("Temperature (°C)")]
        [SerializeField] private V3EnvironmentZoneOperation temperatureOperation =
            V3EnvironmentZoneOperation.None;
        [SerializeField] private float temperatureValue;

        [Header("Pressure (Pa)")]
        [SerializeField] private V3EnvironmentZoneOperation pressureOperation =
            V3EnvironmentZoneOperation.None;
        [SerializeField] private float pressureValue;

        [Header("Density (kg/m³)")]
        [SerializeField] private V3EnvironmentZoneOperation densityOperation =
            V3EnvironmentZoneOperation.None;
        [SerializeField] private float densityValue;

        [Header("Cooling")]
        [SerializeField] private V3EnvironmentZoneOperation coolingOperation =
            V3EnvironmentZoneOperation.None;
        [SerializeField] private float coolingValue = 1f;

        [Header("Turbulence")]
        [SerializeField] private V3EnvironmentZoneOperation turbulenceOperation =
            V3EnvironmentZoneOperation.None;
        [Min(0f), SerializeField] private float turbulenceValue = 1f;
        [SerializeField] private bool showGizmos = true;

        public string StableId => string.IsNullOrWhiteSpace(stableId)
            ? gameObject.name : stableId;
        public V3EnvironmentZoneShape Shape => shape;
        public int Priority => priority;
        public float BlendDistance => Mathf.Max(0f, blendDistance);
        public Vector3 BoxSize => new Vector3(
            Mathf.Max(0.01f, boxSize.x),
            Mathf.Max(0.01f, boxSize.y),
            Mathf.Max(0.01f, boxSize.z));
        public float SphereRadius => Mathf.Max(0.01f, sphereRadius);

        private void OnEnable()
        {
            V3WorldQueryService.NotifyZoneConfigurationChanged(gameObject.scene);
        }

        private void OnDisable()
        {
            V3WorldQueryService.NotifyZoneConfigurationChanged(gameObject.scene);
        }

        private void OnValidate()
        {
            blendDistance = Mathf.Max(0f, blendDistance);
            interiorStrength = Mathf.Clamp01(interiorStrength);
            sphereRadius = Mathf.Max(0.01f, sphereRadius);
            boxSize = BoxSize;
            windMultiplier = Mathf.Max(0f, windMultiplier);
            turbulenceValue = Mathf.Max(0f, turbulenceValue);
            V3WorldQueryService.NotifyZoneConfigurationChanged(gameObject.scene);
        }

        public float EvaluateWeight(Vector3 worldPosition)
        {
            float signedDistance = shape == V3EnvironmentZoneShape.Sphere
                ? SphereSignedDistance(worldPosition)
                : BoxSignedDistance(worldPosition);
            if (signedDistance <= 0f)
            {
                return interiorStrength;
            }

            float distance = BlendDistance;
            if (distance <= 0f || signedDistance >= distance)
            {
                return 0f;
            }

            float normalized = 1f - signedDistance / distance;
            float curved = blendCurve != null
                ? blendCurve.Evaluate(normalized)
                : Mathf.SmoothStep(0f, 1f, normalized);
            return Mathf.Clamp01(curved) * interiorStrength;
        }

        public void Apply(
            ref V3WorldEnvironmentSample sample,
            float weight,
            ref bool densityExplicitlyModified)
        {
            weight = Mathf.Clamp01(weight);
            if (weight <= 0f)
            {
                return;
            }

            Vector3 airVelocityBefore = sample.LocalAirVelocity;
            sample.LocalAirVelocity = ApplyWind(
                sample.LocalAirVelocity,
                weight,
                ref sample.AppliedZoneOperations);

            float temperatureBefore = sample.AmbientTemperatureC;
            sample.AmbientTemperatureC = ApplyScalar(
                sample.AmbientTemperatureC,
                temperatureOperation,
                temperatureValue,
                weight,
                V3WorldZoneOperations.TemperatureOverride,
                V3WorldZoneOperations.TemperatureAdditive,
                V3WorldZoneOperations.TemperatureMultiplier,
                ref sample.AppliedZoneOperations);
            sample.LocalThermalOffsetC +=
                sample.AmbientTemperatureC - temperatureBefore;
            sample.AmbientTemperatureK = Mathf.Max(
                1f,
                sample.AmbientTemperatureC + 273.15f);

            sample.AtmosphericPressurePa = Mathf.Max(0f, ApplyScalar(
                sample.AtmosphericPressurePa,
                pressureOperation,
                pressureValue,
                weight,
                V3WorldZoneOperations.PressureOverride,
                V3WorldZoneOperations.PressureAdditive,
                V3WorldZoneOperations.PressureMultiplier,
                ref sample.AppliedZoneOperations));

            if (densityOperation != V3EnvironmentZoneOperation.None)
            {
                densityExplicitlyModified = true;
            }
            sample.AirDensityKgPerCubicMeter = Mathf.Max(0f, ApplyScalar(
                sample.AirDensityKgPerCubicMeter,
                densityOperation,
                densityValue,
                weight,
                V3WorldZoneOperations.DensityOverride,
                V3WorldZoneOperations.DensityAdditive,
                V3WorldZoneOperations.DensityMultiplier,
                ref sample.AppliedZoneOperations));

            sample.EnvironmentalCoolingMultiplier = Mathf.Max(0f, ApplyScalar(
                sample.EnvironmentalCoolingMultiplier,
                coolingOperation,
                coolingValue,
                weight,
                V3WorldZoneOperations.CoolingOverride,
                V3WorldZoneOperations.CoolingAdditive,
                V3WorldZoneOperations.CoolingMultiplier,
                ref sample.AppliedZoneOperations));

            float turbulenceScale = ApplyScalar(
                1f,
                turbulenceOperation,
                turbulenceValue,
                weight,
                V3WorldZoneOperations.TurbulenceOverride,
                V3WorldZoneOperations.TurbulenceAdditive,
                V3WorldZoneOperations.TurbulenceMultiplier,
                ref sample.AppliedZoneOperations);
            Vector3 oldTurbulence = sample.TurbulenceVelocity;
            sample.TurbulenceVelocity *= Mathf.Max(0f, turbulenceScale);
            sample.LocalAirVelocity +=
                sample.TurbulenceVelocity - oldTurbulence;
            sample.TurbulenceIntensity *= Mathf.Max(0f, turbulenceScale);
            sample.ZoneWindContribution +=
                sample.LocalAirVelocity - airVelocityBefore;
        }

        private Vector3 ApplyWind(
            Vector3 current,
            float weight,
            ref V3WorldZoneOperations operations)
        {
            switch (windOperation)
            {
                case V3EnvironmentZoneOperation.Override:
                    operations |= V3WorldZoneOperations.WindOverride;
                    return Vector3.Lerp(current, windVelocity, weight);
                case V3EnvironmentZoneOperation.Additive:
                    operations |= V3WorldZoneOperations.WindAdditive;
                    return current + windVelocity * weight;
                case V3EnvironmentZoneOperation.Multiplier:
                    operations |= V3WorldZoneOperations.WindMultiplier;
                    return current * Mathf.Lerp(1f, windMultiplier, weight);
                default:
                    return current;
            }
        }

        private static float ApplyScalar(
            float current,
            V3EnvironmentZoneOperation operation,
            float value,
            float weight,
            V3WorldZoneOperations overrideFlag,
            V3WorldZoneOperations additiveFlag,
            V3WorldZoneOperations multiplierFlag,
            ref V3WorldZoneOperations operations)
        {
            switch (operation)
            {
                case V3EnvironmentZoneOperation.Override:
                    operations |= overrideFlag;
                    return Mathf.Lerp(current, value, weight);
                case V3EnvironmentZoneOperation.Additive:
                    operations |= additiveFlag;
                    return current + value * weight;
                case V3EnvironmentZoneOperation.Multiplier:
                    operations |= multiplierFlag;
                    return current * Mathf.Lerp(1f, value, weight);
                default:
                    return current;
            }
        }

        private float SphereSignedDistance(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float scale = Mathf.Max(
                0.0001f,
                Mathf.Max(
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Max(
                        Mathf.Abs(transform.lossyScale.y),
                        Mathf.Abs(transform.lossyScale.z))));
            return local.magnitude * scale - SphereRadius * scale;
        }

        private float BoxSignedDistance(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = BoxSize * 0.5f;
            Vector3 outside = new Vector3(
                Mathf.Max(Mathf.Abs(local.x) - half.x, 0f),
                Mathf.Max(Mathf.Abs(local.y) - half.y, 0f),
                Mathf.Max(Mathf.Abs(local.z) - half.z, 0f));
            Vector3 scaled = Vector3.Scale(outside, Abs(transform.lossyScale));
            if (scaled.sqrMagnitude > 0f)
            {
                return scaled.magnitude;
            }

            float inside = Mathf.Min(
                half.x - Mathf.Abs(local.x),
                Mathf.Min(
                    half.y - Mathf.Abs(local.y),
                    half.z - Mathf.Abs(local.z)));
            float minimumScale = Mathf.Max(
                0.0001f,
                Mathf.Min(
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Min(
                        Mathf.Abs(transform.lossyScale.y),
                        Mathf.Abs(transform.lossyScale.z))));
            return -inside * minimumScale;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos)
            {
                return;
            }

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.75f);
            if (shape == V3EnvironmentZoneShape.Sphere)
            {
                Gizmos.DrawWireSphere(Vector3.zero, SphereRadius);
                if (BlendDistance > 0f)
                {
                    Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.25f);
                    Gizmos.DrawWireSphere(Vector3.zero, SphereRadius + BlendDistance);
                }
            }
            else
            {
                Gizmos.DrawWireCube(Vector3.zero, BoxSize);
                if (BlendDistance > 0f)
                {
                    Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.25f);
                    Gizmos.DrawWireCube(
                        Vector3.zero,
                        BoxSize + Vector3.one * (BlendDistance * 2f));
                }
            }
            Gizmos.matrix = previous;
        }
    }
}
