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

        public string StableId => stableId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public float BaseMassKg => baseMassKg;
        public Vector3 BaseCenterOfMass => baseCenterOfMass;
        public Vector3 ParityCenterOfMassCalibration => parityCenterOfMassCalibration;
    }
}
