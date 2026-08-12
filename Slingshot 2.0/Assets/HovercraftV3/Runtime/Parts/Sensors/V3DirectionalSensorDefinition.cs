using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3SensorCastType
    {
        Raycast,
        SphereCast
    }

    [CreateAssetMenu(menuName = "Hovercraft V3/Systems/Directional Sensor")]
    public sealed class V3DirectionalSensorDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private string manufacturer = "Lunarlight";
        [TextArea, SerializeField] private string description;
        [SerializeField] private V3SensorDirection direction;
        [Min(0.1f), SerializeField] private float rangeMeters = 30f;
        [Min(1f), SerializeField] private float requestedSampleRateHz = 60f;
        [Min(1f), SerializeField] private float minimumSampleRateHz = 15f;
        [Min(1f), SerializeField] private float maximumSampleRateHz = 120f;
        [Min(0f), SerializeField] private float idlePower = 2f;
        [Min(0f), SerializeField] private float powerPerSample = 0.025f;
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private V3SensorCastType castType;
        [Min(0.001f), SerializeField] private float sphereRadius = 0.08f;
        [Min(0f), SerializeField] private float minimumValidDistance = 0.02f;
        [SerializeField] private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;
        [SerializeField] private V3DeviceFirmwareDefinition firmware;
        [SerializeField] private GameObject prefab;
        [Min(0f), SerializeField] private float massKg = 0.75f;
        [SerializeField] private Vector3 localVisualScale =
            new Vector3(0.28f, 0.18f, 0.38f);
        [SerializeField] private Color debugColor = Color.cyan;

        public string StableId => stableId;
        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName)
                ? direction + " Sensor"
                : displayName;
        public string Manufacturer => manufacturer;
        public string Description => description;
        public V3SensorDirection Direction => direction;
        public float RangeMeters => rangeMeters;
        public float RequestedSampleRateHz => requestedSampleRateHz;
        public float MinimumSampleRateHz => minimumSampleRateHz;
        public float MaximumSampleRateHz => maximumSampleRateHz;
        public float IdlePower => idlePower;
        public float PowerPerSample => powerPerSample;
        public LayerMask SurfaceMask => surfaceMask;
        public V3SensorCastType CastType => castType;
        public float SphereRadius => sphereRadius;
        public float MinimumValidDistance => minimumValidDistance;
        public QueryTriggerInteraction TriggerInteraction =>
            triggerInteraction;
        public V3DeviceFirmwareDefinition Firmware => firmware;
        public GameObject Prefab => prefab;
        public float MassKg => massKg;
        public Vector3 LocalVisualScale => localVisualScale;
        public Color DebugColor => debugColor;
    }
}
