using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(menuName = "Hovercraft V3/Systems/Router Profile")]
    public sealed class V3RouterProfileDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName = "Balanced";
        [SerializeField] private float[] domainWeights = CreateBalancedWeights();
        public string StableId => stableId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name : displayName;
        public float GetWeight(V3ControlDomain domain)
        {
            int index = (int)domain;
            return domainWeights != null && index >= 0 &&
                index < domainWeights.Length
                    ? Mathf.Max(0f, domainWeights[index]) : 1f;
        }
        private static float[] CreateBalancedWeights()
        {
            var values = new float[
                Enum.GetValues(typeof(V3ControlDomain)).Length];
            for (int i = 0; i < values.Length; i++) values[i] = 1f;
            return values;
        }
    }
}
