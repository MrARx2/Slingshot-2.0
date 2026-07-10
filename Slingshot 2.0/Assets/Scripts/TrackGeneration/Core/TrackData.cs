using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using TrackGeneration.Stunts;
using TrackGeneration.Gravity;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Holds all generated data for the track, accessible at runtime by other systems (e.g. race manager).
    /// </summary>
    public class TrackData
    {
        public TrackSeed Seed { get; private set; }
        
        public SplineContainer MainCircuit { get; private set; }
        public float MainCircuitLength { get; private set; }
        
        public List<TrackBranch> Shortcuts { get; private set; }

        public List<StuntPlacement> StuntPlacements { get; private set; }
        public List<GravityZonePlacement> GravityZonePlacements { get; private set; }

        public TrackData(TrackSeed seed, SplineContainer mainCircuit, float mainLength, List<TrackBranch> shortcuts, List<StuntPlacement> stuntPlacements, List<GravityZonePlacement> gravityZonePlacements)
        {
            Seed = seed;
            MainCircuit = mainCircuit;
            MainCircuitLength = mainLength;
            Shortcuts = shortcuts ?? new List<TrackBranch>();
            StuntPlacements = stuntPlacements ?? new List<StuntPlacement>();
            GravityZonePlacements = gravityZonePlacements ?? new List<GravityZonePlacement>();
        }
    }
}
