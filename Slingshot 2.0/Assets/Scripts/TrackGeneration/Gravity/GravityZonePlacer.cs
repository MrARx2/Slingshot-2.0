using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Splines;
using Unity.Mathematics;

namespace TrackGeneration.Gravity
{
    /// <summary>
    /// Determines the placement of gravity zones along the track splines.
    /// Does not instantiate game objects; only produces placement data.
    /// </summary>
    public class GravityZonePlacer
    {
        public List<GravityZonePlacement> PlaceGravityZones(TrackConfig config, ref Unity.Mathematics.Random rng, float mainSplineLength, List<float> shortcutLengths, List<TrackBranch> shortcuts = null)
        {
            var placements = new List<GravityZonePlacement>();

            // Main Circuit
            PlaceZonesOnSpline(0, mainSplineLength, config, ref rng, placements, shortcuts, mainSplineLength);

            // Shortcuts
            if (shortcutLengths != null)
            {
                for (int i = 0; i < shortcutLengths.Count; i++)
                {
                    int splineIndex = i + 1;
                    PlaceZonesOnSpline(splineIndex, shortcutLengths[i], config, ref rng, placements, shortcuts, mainSplineLength);
                }
            }

            return placements;
        }

        private void PlaceZonesOnSpline(int splineIndex, float splineLength, TrackConfig config, ref Unity.Mathematics.Random rng, List<GravityZonePlacement> placements, List<TrackBranch> shortcuts, float mainLength)
        {
            float segmentLength = 150f; // Check every ~150m
            int numSegments = Mathf.FloorToInt(splineLength / segmentLength);

            for (int i = 0; i < numSegments; i++)
            {
                if (rng.NextFloat() < config.GravityZoneProbability)
                {
                    float tBase = (float)i / numSegments;
                    float tRange = 1f / numSegments;
                    float t = tBase + rng.NextFloat(0.1f, 0.9f) * tRange;

                    // Keep gravity zones out of shortcut merge zones so they don't disturb the join.
                    if (MergeZoneUtility.IsInMergeZone(splineIndex, t, splineLength, shortcuts, mainLength, config.ShortcutMergeDistance))
                        continue;

                    GravityZoneType zoneType = (rng.NextFloat() < 0.5f) ? GravityZoneType.Centering : GravityZoneType.Orbital;

                    GravityZonePlacement zone = new GravityZonePlacement
                    {
                        Type = zoneType,
                        SplineIndex = splineIndex,
                        T = t,
                        Length = rng.NextFloat(30f, 100f),
                        Strength = config.GravityFieldStrength * rng.NextFloat(0.8f, 1.2f),
                        Radius = config.GravityFieldRadius * rng.NextFloat(0.8f, 1.2f)
                    };

                    placements.Add(zone);
                }
            }
        }
    }
}
