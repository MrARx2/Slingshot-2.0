using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Systems/System Software")]
    public sealed class V3SystemSoftwareDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private V3SystemComputerRole role;
        [Min(1f), SerializeField] private float requestedTickRateHz = 60f;
        [Min(1f), SerializeField] private float minimumUsefulRateHz = 30f;
        [Min(1f), SerializeField] private float maximumUsefulRateHz = 120f;
        [Min(0f), SerializeField] private float computeCostPerTick = 1f;
        [Min(0f), SerializeField] private float idlePower = 2f;
        [Min(0f), SerializeField] private float powerPerTick = 0.02f;
        [Min(0), SerializeField] private int priority;
        public string StableId => stableId;
        public V3SystemComputerRole Role => role;
        public float RequestedTickRateHz => requestedTickRateHz;
        public float MinimumUsefulRateHz => minimumUsefulRateHz;
        public float MaximumUsefulRateHz => maximumUsefulRateHz;
        public float ComputeCostPerTick => computeCostPerTick;
        public float IdlePower => idlePower;
        public float PowerPerTick => powerPerTick;
        public int Priority => priority;
    }
}
