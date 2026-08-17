using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Design
{
    /// <summary>
    /// An editor-time INITIALIZER for <see cref="TrackDesignerSettings"/>: applying a
    /// preset copies its values into the generator's settings, after which every field
    /// is freely editable. The generator never reads the preset name at runtime —
    /// a preset is a starting point, not a mode.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackStylePreset", menuName = "Track/Style Preset")]
    public class TrackStylePreset : ScriptableObject
    {
        [Tooltip("Display name shown in the inspector ('Custom — based on <this>').")]
        public string DisplayName = "Custom Preset";

        [TextArea(2, 4)]
        [Tooltip("What kind of track this preset produces (designer documentation).")]
        public string Description = "";

        [Tooltip("The full designer settings this preset copies into the generator.")]
        public TrackDesignerSettings Settings = new TrackDesignerSettings();

        /// <summary>Returns a deep copy of the preset's settings, stamped with provenance.</summary>
        public TrackDesignerSettings CreateSettings()
        {
            TrackDesignerSettings s = Settings.Clone();
            s.AppliedPresetName = DisplayName;
            s.ModifiedSincePreset = false;
            s.Sanitize();
            s.AppliedPresetSnapshotJson = s.ToJson();
            return s;
        }

        private void OnValidate()
        {
            Settings?.Sanitize();
        }
    }

    /// <summary>
    /// The built-in style presets, expressed in code so they exist without assets.
    /// The inspector offers them directly and can also save any of them (or the
    /// current settings) out as an editable <see cref="TrackStylePreset"/> asset.
    /// All durations are seconds at the preset's design speed (1300 km/h).
    /// </summary>
    public static class TrackStylePresetLibrary
    {
        public const string Flowing = "Flowing";
        public const string Balanced = "Balanced";
        public const string Technical = "Technical";
        public const string Velocity = "Velocity";
        public const string Rollercoaster = "Rollercoaster";
        public const string Switchback = "Switchback";

        public static readonly string[] BuiltInNames =
            { Flowing, Balanced, Technical, Velocity, Rollercoaster, Switchback };

        /// <summary>Creates the named built-in preset's settings (deep copy, provenance stamped).</summary>
        public static TrackDesignerSettings Create(string presetName)
        {
            TrackDesignerSettings s = presetName switch
            {
                Flowing => BuildFlowing(),
                Technical => BuildTechnical(),
                Velocity => BuildVelocity(),
                Rollercoaster => BuildRollercoaster(),
                Switchback => BuildSwitchback(),
                _ => BuildBalanced()
            };

            s.AppliedPresetName = presetName;
            s.ModifiedSincePreset = false;
            s.Sanitize();
            s.AppliedPresetSnapshotJson = s.ToJson();
            return s;
        }

        // ── Shared base: fields common to all presets start from Balanced ──

        private static TrackDesignerSettings Base()
        {
            var s = new TrackDesignerSettings();

            s.Scale.DesignSpeedKph = 1300f;
            s.Scale.MaxTrackLengthMeters = 45000f;

            s.Road.WallCurve = 1f;   // perfect quarter-circle walls
            s.Road.RoadScale = 1f;
            s.Road.SafetyLipHeight = 1f;
            s.Road.ProfileResolution = 32;

            s.Generation.MaxAttempts = 96;
            s.Generation.SelectionMode = CandidateSelectionMode.BestValid;
            s.Generation.CandidatesToScore = 4;
            s.Generation.FailurePolicy = GenerationFailurePolicy.KeepPreviousValidTrack;
            s.Generation.ClosureReserveFraction = 0.2f;
            s.Generation.MetersPerRing = 1.5f;
            s.Generation.MaxFacetAngleDegrees = 0.5f;

            return s;
        }

        private static void SetTurnWeights(TrackDesignerSettings s, float gentle, float sweeper, float standard, float sharp, float hairpin)
        {
            s.Corners.TurnWeights.GentleBend = gentle;
            s.Corners.TurnWeights.Sweeper = sweeper;
            s.Corners.TurnWeights.StandardCorner = standard;
            s.Corners.TurnWeights.SharpCorner = sharp;
            s.Corners.TurnWeights.Hairpin = hairpin;
        }

        // ── Flowing: momentum-focused broad connected curves ──

        private static TrackDesignerSettings BuildFlowing()
        {
            var s = Base();

            s.Scale.TargetLapTimeSeconds = 60f;
            s.Scale.PacingVariation = 0.45f;

            s.Layout.MinTurnCount = 8; s.Layout.MaxTurnCount = 12;
            s.Layout.DirectionPattern = TurnDirectionPattern.Mixed;
            s.Layout.MinStraightSeconds = 0.8f; s.Layout.MaxStraightSeconds = 3.2f;
            s.Layout.CornerSequenceChance = 0.55f;

            s.Corners.MinCurveRadius = 700f; s.Corners.MaxCurveRadius = 2200f;
            SetTurnWeights(s, 3f, 5f, 2f, 0.5f, 0f);
            s.Corners.BankingStrength = 0.9f;
            s.Corners.MaxBankAngle = 75f;

            s.Road.FlatCenterWidth = 12f;
            s.Road.WallHeight = 24f;

            s.Transitions.BankTransitionSeconds = 1.2f;
            s.Transitions.PitchTransitionSeconds = 1.3f;
            s.Transitions.RollTransitionSeconds = 1.5f;
            s.Transitions.GenericTransitionSeconds = 1.1f;
            s.Transitions.WidthTransitionSeconds = 0.9f;
            s.Transitions.CrossSectionTransitionSeconds = 1f;
            s.Transitions.DefaultApproachSeconds = 1.6f;
            s.Transitions.DefaultRecoverySeconds = 1.2f;
            s.Transitions.DangerousSpacingSeconds = 2f;
            s.Transitions.VisualPreviewSeconds = 1.8f;

            s.Elevation.TargetElevationAmplitude = 120f;
            s.Elevation.MinMajorElevationSections = 2; s.Elevation.MaxMajorElevationSections = 5;
            s.Elevation.MaxClimbAngle = 24f; s.Elevation.MaxDropAngle = 26f;

            s.Features.MinFeatureGroups = 1; s.Features.MaxFeatureGroups = 3;
            s.Features.Loops = new TrackFeatureRule(true, 0, 1, 0.4f);
            s.Features.Corkscrews = new TrackFeatureRule(true, 0, 1, 0.4f);
            s.Features.Spirals = new TrackFeatureRule(true, 0, 1, 0.4f);
            s.Features.Jumps = new TrackFeatureRule(true, 0, 1, 0.8f);
            s.Features.HalfLoops = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.Hairpins = new TrackFeatureRule(false, 0, 0, 0f);
            s.Features.Chicanes = new TrackFeatureRule(true, 0, 1, 0.6f);
            s.Features.SCurves = new TrackFeatureRule(true, 0, 2, 1.2f);
            s.Features.CompoundFeatureChance = 0.15f;

            s.Quarters.MinimumDualQuarterCount = 0; s.Quarters.MaximumDualQuarterCount = 1;

            return s;
        }

        // ── Balanced: general-purpose contrast between fast/technical/vertical ──

        private static TrackDesignerSettings BuildBalanced()
        {
            var s = Base();

            s.Scale.TargetLapTimeSeconds = 60f;
            s.Scale.PacingVariation = 0.6f;

            s.Layout.MinTurnCount = 10; s.Layout.MaxTurnCount = 15;
            s.Layout.DirectionPattern = TurnDirectionPattern.Mixed;
            s.Layout.MinStraightSeconds = 0.6f; s.Layout.MaxStraightSeconds = 2.4f;
            s.Layout.CornerSequenceChance = 0.4f;

            s.Corners.MinCurveRadius = 400f; s.Corners.MaxCurveRadius = 1600f;
            SetTurnWeights(s, 2f, 4f, 3f, 1f, 0.5f);
            s.Corners.BankingStrength = 0.8f;
            s.Corners.MaxBankAngle = 72f;

            s.Road.FlatCenterWidth = 10f;
            s.Road.WallHeight = 24f;

            s.Transitions.BankTransitionSeconds = 0.9f;
            s.Transitions.PitchTransitionSeconds = 1f;
            s.Transitions.RollTransitionSeconds = 1.3f;
            s.Transitions.GenericTransitionSeconds = 0.9f;
            s.Transitions.WidthTransitionSeconds = 0.7f;
            s.Transitions.CrossSectionTransitionSeconds = 0.8f;
            s.Transitions.DefaultApproachSeconds = 1.4f;
            s.Transitions.DefaultRecoverySeconds = 1f;
            s.Transitions.DangerousSpacingSeconds = 1.8f;
            s.Transitions.VisualPreviewSeconds = 1.6f;

            s.Elevation.TargetElevationAmplitude = 180f;
            s.Elevation.MinMajorElevationSections = 3; s.Elevation.MaxMajorElevationSections = 6;
            s.Elevation.MaxClimbAngle = 30f; s.Elevation.MaxDropAngle = 32f;

            s.Features.MinFeatureGroups = 2; s.Features.MaxFeatureGroups = 5;
            s.Features.Loops = new TrackFeatureRule(true, 0, 2, 1f);
            s.Features.Corkscrews = new TrackFeatureRule(true, 0, 2, 1f);
            s.Features.Spirals = new TrackFeatureRule(true, 0, 1, 0.8f);
            s.Features.Jumps = new TrackFeatureRule(true, 0, 2, 1f);
            s.Features.HalfLoops = new TrackFeatureRule(true, 0, 1, 0.6f);
            s.Features.Hairpins = new TrackFeatureRule(true, 0, 1, 0.5f);
            s.Features.Chicanes = new TrackFeatureRule(true, 0, 2, 1f);
            s.Features.SCurves = new TrackFeatureRule(true, 0, 2, 1f);
            s.Features.CompoundFeatureChance = 0.35f;

            s.Quarters.MinimumDualQuarterCount = 1; s.Quarters.MaximumDualQuarterCount = 2;

            return s;
        }

        // ── Technical: precision-focused, still scaled for ~1300 km/h ──

        private static TrackDesignerSettings BuildTechnical()
        {
            var s = Base();

            s.Scale.TargetLapTimeSeconds = 50f;
            s.Scale.PacingVariation = 0.55f;

            s.Layout.MinTurnCount = 16; s.Layout.MaxTurnCount = 24;
            s.Layout.DirectionPattern = TurnDirectionPattern.Alternating;
            s.Layout.MinStraightSeconds = 0.35f; s.Layout.MaxStraightSeconds = 1.3f;
            s.Layout.CornerSequenceChance = 0.7f;

            s.Corners.MinCurveRadius = 220f; s.Corners.MaxCurveRadius = 700f;
            SetTurnWeights(s, 0.5f, 1.5f, 4f, 3f, 1.5f);
            s.Corners.BankingStrength = 0.9f;
            s.Corners.MaxBankAngle = 78f;

            // Dense multi-turn layouts self-collide often; attempts are cheap plan math.
            s.Generation.MaxAttempts = 384;

            s.Road.FlatCenterWidth = 8f;
            s.Road.WallHeight = 24f;

            s.Transitions.BankTransitionSeconds = 0.55f;
            s.Transitions.PitchTransitionSeconds = 0.75f;
            s.Transitions.RollTransitionSeconds = 1.1f;
            s.Transitions.GenericTransitionSeconds = 0.6f;
            s.Transitions.WidthTransitionSeconds = 0.5f;
            s.Transitions.CrossSectionTransitionSeconds = 0.6f;
            s.Transitions.DefaultApproachSeconds = 1.1f;
            s.Transitions.DefaultRecoverySeconds = 0.8f;
            s.Transitions.DangerousSpacingSeconds = 1.4f;
            s.Transitions.VisualPreviewSeconds = 1.3f;

            s.Elevation.TargetElevationAmplitude = 120f;
            s.Elevation.MinMajorElevationSections = 2; s.Elevation.MaxMajorElevationSections = 5;
            s.Elevation.MaxClimbAngle = 28f; s.Elevation.MaxDropAngle = 30f;

            // Large spectacle features disrupt the technical rhythm — keep them uncommon.
            s.Features.MinFeatureGroups = 1; s.Features.MaxFeatureGroups = 3;
            s.Features.Loops = new TrackFeatureRule(true, 0, 1, 0.2f);
            s.Features.Corkscrews = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.Spirals = new TrackFeatureRule(true, 0, 1, 0.4f);
            s.Features.Jumps = new TrackFeatureRule(true, 0, 1, 0.5f);
            s.Features.HalfLoops = new TrackFeatureRule(true, 0, 1, 0.2f);
            s.Features.Hairpins = new TrackFeatureRule(true, 1, 4, 1.5f);
            s.Features.Chicanes = new TrackFeatureRule(true, 1, 3, 1.5f);
            s.Features.SCurves = new TrackFeatureRule(true, 1, 4, 1.5f);
            s.Features.CompoundFeatureChance = 0.15f;

            s.Quarters.MinimumDualQuarterCount = 0; s.Quarters.MaximumDualQuarterCount = 1;

            return s;
        }

        // ── Velocity: sustained extreme speed, monumental scale ──

        private static TrackDesignerSettings BuildVelocity()
        {
            var s = Base();

            s.Scale.TargetLapTimeSeconds = 75f;
            s.Scale.PacingVariation = 0.5f;

            s.Layout.MinTurnCount = 5; s.Layout.MaxTurnCount = 8;
            s.Layout.DirectionPattern = TurnDirectionPattern.Circuit;
            s.Layout.MinStraightSeconds = 1.8f; s.Layout.MaxStraightSeconds = 6f;
            s.Layout.CornerSequenceChance = 0.2f;

            s.Corners.MinCurveRadius = 1600f; s.Corners.MaxCurveRadius = 5000f;
            SetTurnWeights(s, 4f, 5f, 1f, 0f, 0f);
            s.Corners.BankingStrength = 1f;
            s.Corners.MaxBankAngle = 82f;

            s.Road.FlatCenterWidth = 20f;
            s.Road.WallHeight = 28f;

            s.Transitions.BankTransitionSeconds = 1.8f;
            s.Transitions.PitchTransitionSeconds = 2f;
            s.Transitions.RollTransitionSeconds = 2.2f;
            s.Transitions.GenericTransitionSeconds = 1.5f;
            s.Transitions.WidthTransitionSeconds = 1.2f;
            s.Transitions.CrossSectionTransitionSeconds = 1.3f;
            s.Transitions.DefaultApproachSeconds = 2.2f;
            s.Transitions.DefaultRecoverySeconds = 1.8f;
            s.Transitions.DangerousSpacingSeconds = 3f;
            s.Transitions.VisualPreviewSeconds = 2.5f;

            s.Elevation.TargetElevationAmplitude = 150f;
            s.Elevation.MinMajorElevationSections = 2; s.Elevation.MaxMajorElevationSections = 4;
            s.Elevation.MaxClimbAngle = 18f; s.Elevation.MaxDropAngle = 20f;

            // Rare but exceptionally large features.
            s.Features.MinFeatureGroups = 1; s.Features.MaxFeatureGroups = 2;
            s.Features.Loops = new TrackFeatureRule(true, 0, 1, 0.6f);
            s.Features.Corkscrews = new TrackFeatureRule(true, 0, 1, 0.5f);
            s.Features.Spirals = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.Jumps = new TrackFeatureRule(true, 0, 1, 0.7f);
            s.Features.HalfLoops = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.Hairpins = new TrackFeatureRule(false, 0, 0, 0f);
            s.Features.Chicanes = new TrackFeatureRule(false, 0, 0, 0f);
            s.Features.SCurves = new TrackFeatureRule(true, 0, 1, 0.5f);
            s.Features.CompoundFeatureChance = 0.2f;

            s.Quarters.MinimumDualQuarterCount = 0; s.Quarters.MaximumDualQuarterCount = 1;

            return s;
        }

        // ── Rollercoaster: 3D orientation-changing feature sequences ──

        private static TrackDesignerSettings BuildRollercoaster()
        {
            var s = Base();

            s.Scale.TargetLapTimeSeconds = 75f;
            s.Scale.PacingVariation = 0.7f;

            s.Layout.MinTurnCount = 8; s.Layout.MaxTurnCount = 13;
            s.Layout.DirectionPattern = TurnDirectionPattern.Mixed;
            s.Layout.MinStraightSeconds = 0.8f; s.Layout.MaxStraightSeconds = 3f;
            s.Layout.CornerSequenceChance = 0.35f;

            s.Corners.MinCurveRadius = 550f; s.Corners.MaxCurveRadius = 1800f;
            SetTurnWeights(s, 1.5f, 3f, 3f, 1f, 0.2f);
            s.Corners.BankingStrength = 0.95f;
            s.Corners.MaxBankAngle = 80f;

            s.Road.FlatCenterWidth = 16f;
            s.Road.WallHeight = 26f;

            s.Transitions.BankTransitionSeconds = 1.4f;
            s.Transitions.PitchTransitionSeconds = 1.6f;
            s.Transitions.RollTransitionSeconds = 1.8f;
            s.Transitions.GenericTransitionSeconds = 1.2f;
            s.Transitions.WidthTransitionSeconds = 0.9f;
            s.Transitions.CrossSectionTransitionSeconds = 1f;
            s.Transitions.DefaultApproachSeconds = 2f;
            s.Transitions.DefaultRecoverySeconds = 1.5f;
            s.Transitions.DangerousSpacingSeconds = 1.8f;
            s.Transitions.VisualPreviewSeconds = 2.2f;

            s.Elevation.TargetElevationAmplitude = 500f;
            s.Elevation.MinMajorElevationSections = 5; s.Elevation.MaxMajorElevationSections = 10;
            s.Elevation.MaxClimbAngle = 34f; s.Elevation.MaxDropAngle = 36f;

            s.Generation.MaxAttempts = 192; // the time watchdog, not attempt count, bounds editor generation
            // Preserve every required minimum, but if the ornamental first pass cannot
            // close, retry with optional counts/weights reduced before giving up.
            s.Generation.FailurePolicy = GenerationFailurePolicy.RelaxOptionalSettings;
            s.Scale.MaxTrackLengthMeters = 60000f; // monumental feature footprints need the full rulebook cap

            s.Features.MinFeatureGroups = 4; s.Features.MaxFeatureGroups = 8;
            // Air gaps must read as deliberate launches at hovercraft speed: a taller
            // progressive ramp, then a clearly upward terminal lip rather than a long
            // road that happens to end only a few degrees above level.
            s.Features.TargetJumpApexHeight = 100f;
            s.Features.JumpLipEmphasis = 0.7f;
            s.Features.Loops = new TrackFeatureRule(true, 1, 3, 1.5f);
            s.Features.Corkscrews = new TrackFeatureRule(true, 1, 3, 1.5f);
            s.Features.Spirals = new TrackFeatureRule(true, 1, 2, 1f);
            s.Features.Jumps = new TrackFeatureRule(true, 1, 3, 1f);
            s.Features.HalfLoops = new TrackFeatureRule(true, 0, 2, 1f);
            s.Features.Hairpins = new TrackFeatureRule(true, 0, 1, 0.2f);
            s.Features.Chicanes = new TrackFeatureRule(true, 0, 1, 0.4f);
            s.Features.SCurves = new TrackFeatureRule(true, 0, 2, 0.8f);
            s.Features.CompoundFeatureChance = 0.65f;
            s.Features.MaxCompoundElements = 3;

            s.Quarters.MinimumDualQuarterCount = 1; s.Quarters.MaximumDualQuarterCount = 2;

            return s;
        }

        // ── Switchback: mountain-road reversals, climbing and layered routes ──

        private static TrackDesignerSettings BuildSwitchback()
        {
            var s = Base();

            s.Scale.TargetLapTimeSeconds = 65f;
            s.Scale.PacingVariation = 0.65f;

            s.Layout.MinTurnCount = 16; s.Layout.MaxTurnCount = 24;
            s.Layout.DirectionPattern = TurnDirectionPattern.Switchback;
            s.Layout.MinStraightSeconds = 0.45f; s.Layout.MaxStraightSeconds = 1.8f;
            s.Layout.CornerSequenceChance = 0.65f;

            s.Corners.MinCurveRadius = 250f; s.Corners.MaxCurveRadius = 850f;
            SetTurnWeights(s, 0.5f, 1f, 2.5f, 4f, 4f);
            s.Corners.BankingStrength = 0.9f;
            s.Corners.MaxBankAngle = 78f;

            // Reversal-heavy walks need a deep search and a bigger closure reserve
            // (nearly collinear straights leave closure to the curves and S-bends),
            // and 16–24 sharp corners at these radii carry a lot of arc — give the
            // lap the full rulebook length cap.
            s.Generation.MaxAttempts = 384;
            s.Generation.ClosureReserveFraction = 0.25f;
            s.Scale.MaxTrackLengthMeters = 60000f;

            s.Road.FlatCenterWidth = 10f;
            s.Road.WallHeight = 24f;

            s.Transitions.BankTransitionSeconds = 0.7f;
            s.Transitions.PitchTransitionSeconds = 0.9f;
            s.Transitions.RollTransitionSeconds = 1.2f;
            s.Transitions.GenericTransitionSeconds = 0.8f;
            s.Transitions.WidthTransitionSeconds = 0.6f;
            s.Transitions.CrossSectionTransitionSeconds = 0.7f;
            s.Transitions.DefaultApproachSeconds = 1.3f;
            s.Transitions.DefaultRecoverySeconds = 1f;
            s.Transitions.DangerousSpacingSeconds = 1.6f;
            s.Transitions.VisualPreviewSeconds = 1.5f;

            s.Elevation.TargetElevationAmplitude = 350f;
            s.Elevation.MinMajorElevationSections = 6; s.Elevation.MaxMajorElevationSections = 12;
            s.Elevation.MaxClimbAngle = 32f; s.Elevation.MaxDropAngle = 34f;

            // Loops and jumps uncommon; spirals support the mountain structure.
            s.Features.MinFeatureGroups = 1; s.Features.MaxFeatureGroups = 3;
            s.Features.Loops = new TrackFeatureRule(true, 0, 1, 0.15f);
            s.Features.Corkscrews = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.Spirals = new TrackFeatureRule(true, 0, 2, 1.2f);
            s.Features.Jumps = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.HalfLoops = new TrackFeatureRule(true, 0, 1, 0.3f);
            s.Features.Hairpins = new TrackFeatureRule(true, 3, 7, 2f);
            s.Features.Chicanes = new TrackFeatureRule(true, 0, 2, 0.8f);
            s.Features.SCurves = new TrackFeatureRule(true, 0, 3, 1f);
            s.Features.CompoundFeatureChance = 0.2f;

            s.Quarters.MinimumDualQuarterCount = 1; s.Quarters.MaximumDualQuarterCount = 2;

            return s;
        }
    }
}
