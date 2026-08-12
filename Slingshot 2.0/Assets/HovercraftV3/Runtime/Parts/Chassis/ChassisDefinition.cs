using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Data/Chassis", fileName = "Chassis")]
    public sealed class ChassisDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;
        [Min(0.001f), SerializeField] private float baseMassKg = 1f;
        [SerializeField] private Vector3 baseCenterOfMass;
        [Tooltip("Documented parity-only offset applied after calculated mass distribution.")]
        [SerializeField] private Vector3 parityCenterOfMassCalibration;
        [Header("Integrated Electronics")]
        [SerializeField] private V3MainframeDefinition integratedMainframe;
        [SerializeField] private V3DirectionalSensorDefinition[] integratedSensors =
            System.Array.Empty<V3DirectionalSensorDefinition>();
        [SerializeField] private V3RouterProfileDefinition routerProfile;
        [Header("Physical Aerodynamics")]
        [Tooltip("Reference frontal area used for chassis drag, in m².")]
        [Min(0f), SerializeField] private float aerodynamicReferenceArea = 10f;
        [Min(0f), SerializeField] private float dragCoefficient = 0.015f;
        [Tooltip("Reference planform area used for passive chassis downforce, in m².")]
        [Min(0f), SerializeField] private float downforceReferenceArea = 8f;
        [Min(0f), SerializeField] private float downforceCoefficient = 0.005f;
        [Tooltip("Craft-local point where chassis aerodynamic force is applied.")]
        [SerializeField] private Vector3 aerodynamicCenterOfPressure =
            new Vector3(0f, 0.15f, 0.25f);
        [Tooltip("Drag multiplier by Mach number. This is a bounded gameplay approximation, not CFD.")]
        [SerializeField] private AnimationCurve compressibilityDragMultiplier =
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.65f, 1f),
                new Keyframe(0.85f, 1.15f),
                new Keyframe(1f, 1.3f),
                new Keyframe(1.25f, 1.4f));

        public string StableId => stableId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public float BaseMassKg => baseMassKg;
        public Vector3 BaseCenterOfMass => baseCenterOfMass;
        public Vector3 ParityCenterOfMassCalibration => parityCenterOfMassCalibration;
        public V3MainframeDefinition IntegratedMainframe => integratedMainframe;
        public System.Collections.Generic.IReadOnlyList<V3DirectionalSensorDefinition>
            IntegratedSensors => integratedSensors;
        public V3RouterProfileDefinition RouterProfile => routerProfile;
        public float AerodynamicReferenceArea => Mathf.Max(
            0f,
            aerodynamicReferenceArea);
        public float DragCoefficient => Mathf.Max(0f, dragCoefficient);
        public float DownforceReferenceArea => Mathf.Max(
            0f,
            downforceReferenceArea);
        public float DownforceCoefficient => Mathf.Max(
            0f,
            downforceCoefficient);
        public Vector3 AerodynamicCenterOfPressure =>
            aerodynamicCenterOfPressure;
        public float EvaluateCompressibilityDragMultiplier(float mach)
        {
            return compressibilityDragMultiplier != null &&
                compressibilityDragMultiplier.length > 0
                    ? Mathf.Max(
                        0f,
                        compressibilityDragMultiplier.Evaluate(
                            Mathf.Max(0f, mach)))
                    : 1f;
        }
    }
}
