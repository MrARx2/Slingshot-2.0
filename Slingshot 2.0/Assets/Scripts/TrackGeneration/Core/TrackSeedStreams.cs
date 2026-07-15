using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace TrackGeneration.Core
{
    /// <summary>The independent random subsystems of track generation.</summary>
    public enum SeedStream
    {
        Layout,      // corner plan geometry, gap order, pacing, closure
        Feature,     // which features/patterns fill the gaps, corner realizations
        Elevation,   // major climbs, crests, bridges, underpasses
        Quarter,     // quarter partition, dual-quarter selection, alternate road content
        Surface,     // cross-section/surface variation (reserved for surface passes)
        Visual,      // marker spacing, colors, decoration (never affects geometry)
        PresetResolution // curated Random preset selection
    }

    /// <summary>
    /// Independent deterministic seed streams: one subsystem may be re-randomized
    /// without altering unrelated content. "Same layout, new features" = keep the
    /// Layout and Quarter seeds, randomize the Feature seed. Mesh resolution, visual
    /// spacing and inspector state never touch layout randomness because they draw
    /// from separate streams (or from none at all).
    /// Streams derive by hashing the enum NAME, so renaming/adding a stream never
    /// shifts the derived seeds of the other streams.
    /// </summary>
    [Serializable]
    public class TrackSeedStreams
    {
        [Tooltip("Layout stream: corner plan, gap order, pacing, closure.")]
        public int LayoutSeed;

        [Tooltip("Feature stream: which features fill the gaps, corner realizations, pattern parameters.")]
        public int FeatureSeed;

        [Tooltip("Elevation stream: major climbs/drops, crests, bridges, underpasses.")]
        public int ElevationSeed;

        [FormerlySerializedAs("BranchSeed")]
        [Tooltip("Quarter stream: quarter partition, dual-quarter selection, alternate road content.")]
        public int QuarterSeed;

        [Tooltip("Surface stream: cross-section/surface variation (reserved).")]
        public int SurfaceSeed;

        [Tooltip("Visual stream: marker spacing, colors, decoration. Never affects geometry.")]
        public int VisualSeed;

        [Tooltip("Preset-resolution stream: curated Random preset selection.")]
        public int PresetResolutionSeed;

        /// <summary>Derives every stream deterministically from one master seed.</summary>
        public void DeriveAllFrom(int masterSeed)
        {
            LayoutSeed = Derive(masterSeed, SeedStream.Layout);
            FeatureSeed = Derive(masterSeed, SeedStream.Feature);
            ElevationSeed = Derive(masterSeed, SeedStream.Elevation);
            QuarterSeed = Derive(masterSeed, SeedStream.Quarter);
            SurfaceSeed = Derive(masterSeed, SeedStream.Surface);
            VisualSeed = Derive(masterSeed, SeedStream.Visual);
            PresetResolutionSeed = Derive(masterSeed, SeedStream.PresetResolution);
        }

        /// <summary>Re-randomizes only the given streams (from entropy), leaving the rest untouched.</summary>
        public void Randomize(params SeedStream[] streams)
        {
            foreach (var s in streams)
            {
                int fresh = Environment.TickCount ^ Guid.NewGuid().GetHashCode() ^ (int)s * 7919;
                Set(s, fresh);
            }
        }

        public int Get(SeedStream stream) => stream switch
        {
            SeedStream.Layout => LayoutSeed,
            SeedStream.Feature => FeatureSeed,
            SeedStream.Elevation => ElevationSeed,
            SeedStream.Quarter => QuarterSeed,
            SeedStream.Surface => SurfaceSeed,
            SeedStream.Visual => VisualSeed,
            _ => PresetResolutionSeed
        };

        public void Set(SeedStream stream, int value)
        {
            switch (stream)
            {
                case SeedStream.Layout: LayoutSeed = value; break;
                case SeedStream.Feature: FeatureSeed = value; break;
                case SeedStream.Elevation: ElevationSeed = value; break;
                case SeedStream.Quarter: QuarterSeed = value; break;
                case SeedStream.Surface: SurfaceSeed = value; break;
                case SeedStream.Visual: VisualSeed = value; break;
                default: PresetResolutionSeed = value; break;
            }
        }

        /// <summary>Deterministic per-stream RNG for one generation attempt.</summary>
        public Unity.Mathematics.Random AttemptRandom(SeedStream stream, int attemptIndex)
        {
            uint h = (uint)CombineHash(Get(stream), CombineHash((int)stream * 31 + 17, attemptIndex));
            if (h == 0u) h = 1u;
            return new Unity.Mathematics.Random(h);
        }

        public TrackSeedStreams Clone() => (TrackSeedStreams)MemberwiseClone();

        private static int Derive(int master, SeedStream stream)
            => CombineHash(master, StableStringHash(stream.ToString()));

        private static int StableStringHash(string s)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }

        private static int CombineHash(int h1, int h2)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + h1;
                hash = hash * 31 + h2;
                return hash;
            }
        }
    }
}
