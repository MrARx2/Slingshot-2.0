using System;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>Controls overall lap size.</summary>
    public enum TrackLengthPreset { Short, Medium, Long, Huge }

    /// <summary>Controls how demanding and varied the track is.</summary>
    public enum TrackDifficultyPreset { Easy, Normal, Hard, Extreme }

    /// <summary>Controls how open or technical the track feels.</summary>
    public enum TrackSpeedProfile { Flowing, Balanced, Technical, InsaneSpeed }

    /// <summary>Controls general road width.</summary>
    public enum TrackWidthPreset { Narrow, Standard, Wide, SuperWide }

    /// <summary>Controls elevation and over/under track behavior.</summary>
    public enum TrackVerticalityPreset { Flat, Rolling, Layered, RainbowRoad }

    /// <summary>Controls how often the track splits into multiple readable routes (V1: two routes max).</summary>
    public enum TrackBranchingPreset { None, Low, Medium, High }

    /// <summary>Controls how many jump/stunt sections appear.</summary>
    public enum TrackStuntDensity { None, Low, Medium, High }

    /// <summary>Controls how often vertical loops appear (only if TrackConfig allows loops).</summary>
    public enum TrackLoopFrequency { None, Rare, Occasional, Frequent }

    /// <summary>Controls how often corkscrews appear (only if TrackConfig allows corkscrews).</summary>
    public enum TrackCorkscrewFrequency { None, Rare, Occasional, Frequent }

    /// <summary>Controls how often climbing spirals (parking-garage helixes) appear.</summary>
    public enum TrackSpiralFrequency { None, Rare, Occasional, Frequent }

    /// <summary>
    /// The level designer's control panel: the ONLY parameters needed to define a track's
    /// personality. Everything else is derived internally (ResolvedTrackGenerationConfig)
    /// and clamped by the TrackConfig rulebook.
    ///
    /// Seed (control #1) lives on TrackSeedManager: Use Random Seed / Manual Seed.
    /// </summary>
    [Serializable]
    public class TrackDesignerProfile
    {
        [Tooltip("2. Overall lap size.")]
        public TrackLengthPreset Length = TrackLengthPreset.Medium;

        [Tooltip("3. How demanding and varied the track is. Even Extreme stays readable macro sections.")]
        public TrackDifficultyPreset Difficulty = TrackDifficultyPreset.Normal;

        [Tooltip("4. How open (long straights, sweeping corners) or technical (tight, interrupted) the track feels.")]
        public TrackSpeedProfile SpeedProfile = TrackSpeedProfile.Balanced;

        [Tooltip("5. General road width. Clamped by TrackConfig limits.")]
        public TrackWidthPreset Width = TrackWidthPreset.Standard;

        [Tooltip("6. Elevation and over/under behavior. (V1: reserved — macro layout is flat except jumps/loops/corkscrews.)")]
        public TrackVerticalityPreset Verticality = TrackVerticalityPreset.Flat;

        [Tooltip("7. How often the track splits into two readable routes. (V1: reserved — split sections land in the next milestone.)")]
        public TrackBranchingPreset Branching = TrackBranchingPreset.None;

        [Tooltip("8. How many jump/stunt sections appear.")]
        public TrackStuntDensity StuntDensity = TrackStuntDensity.Medium;

        [Tooltip("9. How often vertical loops appear. Only if the TrackConfig rulebook allows loops.")]
        public TrackLoopFrequency LoopFrequency = TrackLoopFrequency.Rare;

        [Tooltip("10. How often corkscrews appear. Only if the TrackConfig rulebook allows corkscrews.")]
        public TrackCorkscrewFrequency CorkscrewFrequency = TrackCorkscrewFrequency.Rare;

        [Tooltip("11. Number of corners on the lap. 0 = automatic (derived from track length; classic same-direction circuit). Above 0, the planner mixes LEFT and RIGHT turns — including near-180° switchbacks — like a mountain pass, while the signed turn total still closes the lap. High counts need enough track length and small enough curve radii (TrackConfig).")]
        [Range(0, 30)]
        public int TurnCount = 0;

        [Tooltip("12. How often climbing spirals (parking-garage helixes) appear. Independent of Verticality — the spiral's gained height is paid back on later straights.")]
        public TrackSpiralFrequency SpiralFrequency = TrackSpiralFrequency.Rare;
    }
}
