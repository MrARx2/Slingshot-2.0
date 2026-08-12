using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3EnvironmentZoneRegistry : MonoBehaviour
    {
        private readonly List<V3WorldEnvironmentZone> zones =
            new List<V3WorldEnvironmentZone>(16);
        private bool dirty = true;

        public IReadOnlyList<V3WorldEnvironmentZone> Zones => zones;
        public int Count => zones.Count;

        public void MarkDirty()
        {
            dirty = true;
        }

        public void Refresh()
        {
            zones.Clear();
            V3WorldEnvironmentZone[] found =
                Object.FindObjectsByType<V3WorldEnvironmentZone>(
                    FindObjectsInactive.Exclude);
            Scene scene = gameObject.scene;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].gameObject.scene == scene)
                {
                    zones.Add(found[i]);
                }
            }
            zones.Sort(CompareZones);
            dirty = false;
        }

        public void Apply(ref V3WorldEnvironmentSample sample)
        {
            if (dirty)
            {
                Refresh();
            }

            bool densityExplicitlyModified = false;
            int dominantPriority = int.MinValue;
            float dominantWeight = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                V3WorldEnvironmentZone zone = zones[i];
                if (zone == null || !zone.isActiveAndEnabled)
                {
                    continue;
                }

                float weight = zone.EvaluateWeight(sample.WorldPosition);
                if (weight <= 0f)
                {
                    continue;
                }

                sample.RecordZone(zone.StableId, weight);
                if (zone.Priority > dominantPriority ||
                    (zone.Priority == dominantPriority &&
                     weight > dominantWeight))
                {
                    dominantPriority = zone.Priority;
                    dominantWeight = weight;
                    sample.DominantZoneId = zone.StableId;
                    sample.DominantZonePriority = zone.Priority;
                    sample.DominantZoneWeight = weight;
                }

                zone.Apply(
                    ref sample,
                    weight,
                    ref densityExplicitlyModified);
            }

            if (sample.AtmosphereEnabled && !densityExplicitlyModified)
            {
                sample.AirDensityKgPerCubicMeter =
                    sample.AtmosphericPressurePa /
                    (V3AtmosphereField.SpecificGasConstantAir *
                     Mathf.Max(1f, sample.AmbientTemperatureK));
            }
        }

        private static int CompareZones(
            V3WorldEnvironmentZone left,
            V3WorldEnvironmentZone right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0)
            {
                return priority;
            }
            int id = string.CompareOrdinal(left.StableId, right.StableId);
            return id != 0
                ? id
                : left.GetEntityId().GetHashCode().CompareTo(
                    right.GetEntityId().GetHashCode());
        }
    }
}
