using System;
using System.Collections.Generic;
using System.Linq;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using UnityEngine;

namespace TrackGeneration.Definitions
{
    /// <summary>How the definition compiler resolves the core maneuver length.</summary>
    public enum FeatureLengthResolution
    {
        AuthoredRange = 0,
        DerivedFromRadii = 1,
        PhysicsAndRollRateSolved = 2,
        ComposedFromPrimitives = 3
    }

    /// <summary>
    /// Common distance contract owned by every feature definition. Values are meters at
    /// ReferenceSpeedMps; speed-scaled approach/recovery contracts preserve their authored
    /// duration as the design speed changes.
    /// </summary>
    [Serializable]
    public sealed class FeatureDistanceParameter
    {
        [Min(0f)] public float Minimum;
        [Min(0f)] public float Preferred;
        [Min(0f)] public float Maximum;
        public FeatureLengthResolution Resolution = FeatureLengthResolution.AuthoredRange;
        public bool MayAutoExpand;
        public bool ScaleWithDesignSpeed;
        [Min(0.01f)] public float ReferenceSpeedMps = 361.1111f;

        public void Sanitize()
        {
            Minimum = Mathf.Max(0f, Minimum);
            Maximum = Mathf.Max(Minimum, Maximum);
            Preferred = Mathf.Clamp(Preferred, Minimum, Maximum);
            ReferenceSpeedMps = Mathf.Max(0.01f, ReferenceSpeedMps);
        }

        public float Resolve(float authoredMeters, float designSpeedMps)
        {
            Sanitize();
            float scale = ScaleWithDesignSpeed
                ? Mathf.Max(0.01f, designSpeedMps) / ReferenceSpeedMps
                : 1f;
            return Mathf.Clamp(authoredMeters, Minimum, Maximum) * scale;
        }

        public float ResolvedMinimum(float designSpeedMps) => Resolve(Minimum, designSpeedMps);
        public float ResolvedPreferred(float designSpeedMps) => Resolve(Preferred, designSpeedMps);
        public float ResolvedMaximum(float designSpeedMps) => Resolve(Maximum, designSpeedMps);
    }

    [Serializable]
    public sealed class FeatureScalarParameter
    {
        public float Minimum;
        public float Preferred;
        public float Maximum;

        public void Sanitize()
        {
            Maximum = Mathf.Max(Minimum, Maximum);
            Preferred = Mathf.Clamp(Preferred, Minimum, Maximum);
        }
    }

    public enum FeatureGeometryPrimitiveType
    {
        Straight = 0,
        Recovery = 1,
        EasedTurn = 2,
        HalfLoop = 3,
        HalfCorkscrew = 4,
        VerticalArc = 5,
        RoadRoll = 6,
        AirGap = 7,
        Landing = 8,
        WidthTransition = 9,
        CrossSectionTransition = 10,
        Pipe = 11,
        ElevatedEasedTurn = 12,
        SpiralHelix = 13,
        CrestStraight = 14,
        DipStraight = 15,
        HalfHelixTurn = 16
    }

    public enum FeatureRadiusSource
    {
        None = 0,
        FirstHalfRadius = 1,
        SecondHalfRadius = 2,
        TurnRadius = 3
    }

    /// <summary>One reusable phase invocation inside a Feature Definition.</summary>
    [Serializable]
    public sealed class FeatureGeometryPrimitive
    {
        public string PrimitiveId = "";
        public string DisplayName = "";
        public FeatureGeometryPrimitiveType PrimitiveType;
        public TrackOrientationTag EntryOrientation = TrackOrientationTag.Any;
        public TrackOrientationTag ExitOrientation = TrackOrientationTag.Any;
        [Range(0f, 1f)] public float LengthFraction = 1f;
        public FeatureRadiusSource RadiusSource;
        public bool ContinueCurvature = true;
        public bool ContinueRollRate = true;
        public bool AllowGenericConnectorBefore;

        public void Sanitize()
        {
            PrimitiveId = (PrimitiveId ?? "").Trim();
            DisplayName = (DisplayName ?? "").Trim();
            LengthFraction = Mathf.Clamp01(LengthFraction);
        }
    }

    [Serializable]
    public sealed class DefinitionEntryContract
    {
        public TrackOrientationTag Orientation = TrackOrientationTag.Upright;
        public bool RequiresUprightEntry;
        [Min(0f)] public float MaximumBankDegrees;
        [Min(0f)] public float MaximumPitchDegrees;
        [Min(0f)] public float MaximumHorizontalCurvature;
        [Min(0f)] public float MaximumRollRateDegreesPerMeter;
        public bool AcceptsCurvedEntry = true;
    }

    [Serializable]
    public sealed class DefinitionExitContract
    {
        public TrackOrientationTag Orientation = TrackOrientationTag.Upright;
        public float HeadingChangeDegrees;
        public float PitchChangeDegrees;
        public float RollChangeDegrees;
        public float ElevationChangeMeters;
    }

    [Serializable]
    public sealed class LoopDefinitionParameters
    {
        public FeatureScalarParameter FirstHalfRadius = new FeatureScalarParameter();
        public FeatureScalarParameter SecondHalfRadius = new FeatureScalarParameter();
        [Min(1)] public int VerticalRotationUnits = 4;
        public List<int> AllowedVerticalRotationUnits = new List<int>();
        [Min(1f)] public float DegreesPerUnit = 90f;
        public FeatureScalarParameter LimbSeparationYawDegrees = new FeatureScalarParameter();
        public List<float> LimbSeparationYawCandidates = new List<float>();
        public FeatureScalarParameter CurvatureEase = new FeatureScalarParameter();

        public void Sanitize()
        {
            FirstHalfRadius ??= new FeatureScalarParameter();
            SecondHalfRadius ??= new FeatureScalarParameter();
            AllowedVerticalRotationUnits ??= new List<int>();
            LimbSeparationYawDegrees ??= new FeatureScalarParameter();
            LimbSeparationYawCandidates ??= new List<float>();
            CurvatureEase ??= new FeatureScalarParameter();
            FirstHalfRadius.Sanitize();
            SecondHalfRadius.Sanitize();
            LimbSeparationYawDegrees.Sanitize();
            CurvatureEase.Sanitize();
            LimbSeparationYawCandidates.RemoveAll(value => value <= 0f);
            AllowedVerticalRotationUnits.RemoveAll(value => value <= 0);
            AllowedVerticalRotationUnits.Sort();
            VerticalRotationUnits = Mathf.Max(1, VerticalRotationUnits);
            DegreesPerUnit = Mathf.Max(1f, DegreesPerUnit);
        }
    }

    [Serializable]
    public sealed class CorkscrewDefinitionParameters
    {
        public FeatureScalarParameter FirstHalfRadius = new FeatureScalarParameter();
        public FeatureScalarParameter SecondHalfRadius = new FeatureScalarParameter();
        [Min(1)] public int RoadRollUnits = 4;
        public List<int> AllowedRoadRollUnits = new List<int>();
        [Min(1f)] public float DegreesPerUnit = 90f;
        public FeatureScalarParameter PhaseSplit = new FeatureScalarParameter();
        [Min(1f)] public float BarrelClearanceMultiplier = 2.5f;
        [Range(0.05f, 1f)] public float TargetArrivalSpeedFraction = 0.55f;
        [Min(0f)] public float MinimumInvertedContactLoadG = 0.5f;
        [Min(0.1f)] public float MaximumPeakLoadG = 12f;
        public bool AutomaticHandedness = true;

        public void Sanitize()
        {
            FirstHalfRadius ??= new FeatureScalarParameter();
            SecondHalfRadius ??= new FeatureScalarParameter();
            AllowedRoadRollUnits ??= new List<int>();
            PhaseSplit ??= new FeatureScalarParameter();
            FirstHalfRadius.Sanitize();
            SecondHalfRadius.Sanitize();
            PhaseSplit.Sanitize();
            AllowedRoadRollUnits.RemoveAll(value => value <= 0);
            AllowedRoadRollUnits.Sort();
            RoadRollUnits = Mathf.Max(1, RoadRollUnits);
            DegreesPerUnit = Mathf.Max(1f, DegreesPerUnit);
            BarrelClearanceMultiplier = Mathf.Max(1f, BarrelClearanceMultiplier);
            TargetArrivalSpeedFraction = Mathf.Clamp(TargetArrivalSpeedFraction, 0.05f, 1f);
            MinimumInvertedContactLoadG = Mathf.Max(0f, MinimumInvertedContactLoadG);
            MaximumPeakLoadG = Mathf.Max(0.1f, MaximumPeakLoadG);
        }
    }

    /// <summary>
    /// Authored visual-height choices for a spiral. Story count describes the total
    /// vertical rise/drop, while Revolutions describes the number of complete coils.
    /// Keeping those concepts separate permits a compact 1.5-storey variant without
    /// giving a heading-neutral feature an invalid half-revolution exit.
    /// </summary>
    [Serializable]
    public sealed class SpiralDefinitionParameters
    {
        public FeatureScalarParameter RadiusMeters = new FeatureScalarParameter();
        public FeatureScalarParameter ClimbPerStoryMeters = new FeatureScalarParameter();
        public List<float> AllowedStoryCounts = new List<float>();
        [Range(0f, 1f)] public float DescendingChance = 0.35f;

        public void Sanitize()
        {
            RadiusMeters ??= new FeatureScalarParameter();
            ClimbPerStoryMeters ??= new FeatureScalarParameter();
            AllowedStoryCounts ??= new List<float>();
            RadiusMeters.Sanitize();
            ClimbPerStoryMeters.Sanitize();
            AllowedStoryCounts.RemoveAll(value => value < 1f || value > 2f);
            for (int i = 0; i < AllowedStoryCounts.Count; i++)
                AllowedStoryCounts[i] = Mathf.Round(AllowedStoryCounts[i] * 2f) * 0.5f;
            AllowedStoryCounts = AllowedStoryCounts.Distinct().OrderBy(value => value).ToList();
            DescendingChance = Mathf.Clamp01(DescendingChance);
        }
    }

    [Serializable]
    public sealed class TurnDefinitionParameters
    {
        public FeatureScalarParameter HeadingDegrees = new FeatureScalarParameter();
        public FeatureScalarParameter RadiusMultiplier = new FeatureScalarParameter();
        public FeatureScalarParameter AbsoluteRadiusMeters = new FeatureScalarParameter();
        public FeatureScalarParameter CrestHeightMeters = new FeatureScalarParameter();
        public FeatureScalarParameter BankMultiplier = new FeatureScalarParameter();
        public TrackMacroSectionType CoreSectionType = TrackMacroSectionType.BankedCurve;
        public bool UseRecommendedBank = true;

        public void Sanitize()
        {
            HeadingDegrees ??= new FeatureScalarParameter();
            RadiusMultiplier ??= new FeatureScalarParameter();
            AbsoluteRadiusMeters ??= new FeatureScalarParameter();
            CrestHeightMeters ??= new FeatureScalarParameter();
            BankMultiplier ??= new FeatureScalarParameter();
            HeadingDegrees.Sanitize();
            RadiusMultiplier.Sanitize();
            AbsoluteRadiusMeters.Sanitize();
            CrestHeightMeters.Sanitize();
            BankMultiplier.Sanitize();
        }
    }

    /// <summary>
    /// Signed vertical displacement for a partial helical reversal.
    /// Positive values climb; negative values descend.
    /// </summary>
    [Serializable]
    public sealed class HalfHelixDefinitionParameters
    {
        public FeatureScalarParameter ElevationChangeMeters = new FeatureScalarParameter();

        public void Sanitize()
        {
            ElevationChangeMeters ??= new FeatureScalarParameter();
            ElevationChangeMeters.Sanitize();
        }
    }

    /// <summary>One or more net-zero elevation lobes whose entry and exit remain level.</summary>
    [Serializable]
    public sealed class VerticalBumpDefinitionParameters
    {
        public FeatureScalarParameter HeightMeters = new FeatureScalarParameter();
        [Min(1)] public int LobeCount = 1;
        public bool StartsWithDip;
        public bool AlternateLobeSign;

        public void Sanitize()
        {
            HeightMeters ??= new FeatureScalarParameter();
            HeightMeters.Minimum = Mathf.Max(0f, HeightMeters.Minimum);
            HeightMeters.Sanitize();
            LobeCount = Mathf.Clamp(LobeCount, 1, 4);
        }
    }

    /// <summary>
    /// Data for compact continuous rotational events. Primitive order owns the
    /// choreography (roll/vertical arc); these parameters own physical shape.
    /// </summary>
    [Serializable]
    public sealed class RotationalFeatureDefinitionParameters
    {
        public const float DefaultMaximumCoreRollRateDegreesPerSecond = 120f;

        [Min(0)] public int RoadRollUnits = 4;
        [Min(0)] public int VerticalRotationUnits;
        [Tooltip("Maximum intentional core roll speed. Entry/exit connector roll-rate limits remain governed by the shared rulebook.")]
        [Min(1f)] public float MaximumCoreRollRateDegreesPerSecond =
            DefaultMaximumCoreRollRateDegreesPerSecond;
        public bool DescendingVerticalPhase;
        public bool AutomaticHandedness = true;
        public FeatureScalarParameter FirstHalfRadius = new FeatureScalarParameter();
        public FeatureScalarParameter SecondHalfRadius = new FeatureScalarParameter();
        public FeatureScalarParameter PhaseSplit = new FeatureScalarParameter();
        public FeatureScalarParameter CenterlineOrbitDegrees = new FeatureScalarParameter();
        public FeatureScalarParameter FirstHalfPitchBiasDegrees = new FeatureScalarParameter();
        public FeatureScalarParameter SecondHalfPitchBiasDegrees = new FeatureScalarParameter();
        public FeatureScalarParameter LimbYawBiasDegrees = new FeatureScalarParameter();

        public void Sanitize()
        {
            RoadRollUnits = Mathf.Max(0, RoadRollUnits);
            VerticalRotationUnits = Mathf.Max(0, VerticalRotationUnits);
            // Definition assets created before this field existed deserialize it as
            // zero on some Unity versions. Treat that as "use the safe authored
            // default", not 1 degree/second: the latter makes a single roll require
            // hundreds of kilometres and silently rejects every topology candidate.
            if (float.IsNaN(MaximumCoreRollRateDegreesPerSecond) ||
                float.IsInfinity(MaximumCoreRollRateDegreesPerSecond) ||
                MaximumCoreRollRateDegreesPerSecond <= 0f)
            {
                MaximumCoreRollRateDegreesPerSecond =
                    DefaultMaximumCoreRollRateDegreesPerSecond;
            }
            else
            {
                MaximumCoreRollRateDegreesPerSecond = Mathf.Max(1f,
                    MaximumCoreRollRateDegreesPerSecond);
            }
            FirstHalfRadius ??= new FeatureScalarParameter();
            SecondHalfRadius ??= new FeatureScalarParameter();
            PhaseSplit ??= new FeatureScalarParameter();
            CenterlineOrbitDegrees ??= new FeatureScalarParameter();
            FirstHalfPitchBiasDegrees ??= new FeatureScalarParameter();
            SecondHalfPitchBiasDegrees ??= new FeatureScalarParameter();
            LimbYawBiasDegrees ??= new FeatureScalarParameter();
            FirstHalfRadius.Sanitize();
            SecondHalfRadius.Sanitize();
            PhaseSplit.Minimum = Mathf.Clamp01(PhaseSplit.Minimum);
            PhaseSplit.Maximum = Mathf.Clamp01(PhaseSplit.Maximum);
            PhaseSplit.Sanitize();
            CenterlineOrbitDegrees.Minimum = Mathf.Max(0f, CenterlineOrbitDegrees.Minimum);
            CenterlineOrbitDegrees.Sanitize();
            FirstHalfPitchBiasDegrees.Sanitize();
            SecondHalfPitchBiasDegrees.Sanitize();
            LimbYawBiasDegrees.Sanitize();
        }
    }

    /// <summary>
    /// Implementation adapter used while specialized geometry math is migrated behind
    /// the common primitive compiler. It is versioned data, never a central planner switch.
    /// </summary>
    public enum FeatureDefinitionSolver
    {
        PrimitiveSequence = 0,
        FullLoopV1 = 1,
        InlineCorkscrewV1 = 2,
        EasedTurnV1 = 3,
        SpiralV1 = 4,
        VerticalBumpV1 = 5,
        RotationalSequenceV1 = 6
    }

    /// <summary>Serializable, engine-independent content of one named feature realization.</summary>
    [Serializable]
    public sealed class TrackFeatureDefinition
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string StableId = "";
        public int DefinitionVersion = 1;
        public string DisplayName = "";
        public string Family = "";
        public bool Enabled = true;
        public SemanticElementId SemanticElement;
        public bool HasPatternType;
        public TrackPatternType PatternType;
        public TopologyRole TopologyRole;
        public FeatureDefinitionSolver Solver;
        public SectionSpeedIntent SpeedIntent = SectionSpeedIntent.Fast;
        public SectionRiskLevel RiskLevel = SectionRiskLevel.Normal;
        public bool RequiresRecoveryAfter;
        public bool EmitsOwnApproach;
        public bool EmitsOwnRecovery;

        public FeatureDistanceParameter Length = new FeatureDistanceParameter();
        public FeatureDistanceParameter EntryLength = new FeatureDistanceParameter();
        public FeatureDistanceParameter RecoveryLength = new FeatureDistanceParameter();
        public DefinitionEntryContract EntryContract = new DefinitionEntryContract();
        public DefinitionExitContract ExitContract = new DefinitionExitContract();
        public LoopDefinitionParameters Loop = new LoopDefinitionParameters();
        public CorkscrewDefinitionParameters Corkscrew = new CorkscrewDefinitionParameters();
        public SpiralDefinitionParameters Spiral = new SpiralDefinitionParameters();
        public TurnDefinitionParameters Turn = new TurnDefinitionParameters();
        public HalfHelixDefinitionParameters HalfHelix = new HalfHelixDefinitionParameters();
        public VerticalBumpDefinitionParameters VerticalBump = new VerticalBumpDefinitionParameters();
        public RotationalFeatureDefinitionParameters Rotational = new RotationalFeatureDefinitionParameters();
        public List<FeatureGeometryPrimitive> Primitives = new List<FeatureGeometryPrimitive>();
        [Min(0f)] public float MinimumClearanceMeters;

        public float PreferredTotalOccupiedLength(float designSpeedMps)
        {
            Sanitize();
            return EntryLength.ResolvedPreferred(designSpeedMps) +
                   Length.ResolvedPreferred(designSpeedMps) +
                   RecoveryLength.ResolvedPreferred(designSpeedMps);
        }

        public void Sanitize()
        {
            SchemaVersion = Mathf.Max(1, SchemaVersion);
            StableId = (StableId ?? "").Trim();
            DefinitionVersion = Mathf.Max(1, DefinitionVersion);
            DisplayName = (DisplayName ?? "").Trim();
            Family = (Family ?? "").Trim();
            Length ??= new FeatureDistanceParameter();
            EntryLength ??= new FeatureDistanceParameter();
            RecoveryLength ??= new FeatureDistanceParameter();
            EntryContract ??= new DefinitionEntryContract();
            ExitContract ??= new DefinitionExitContract();
            Loop ??= new LoopDefinitionParameters();
            Corkscrew ??= new CorkscrewDefinitionParameters();
            Spiral ??= new SpiralDefinitionParameters();
            Turn ??= new TurnDefinitionParameters();
            HalfHelix ??= new HalfHelixDefinitionParameters();
            VerticalBump ??= new VerticalBumpDefinitionParameters();
            Rotational ??= new RotationalFeatureDefinitionParameters();
            Primitives ??= new List<FeatureGeometryPrimitive>();
            Length.Sanitize();
            EntryLength.Sanitize();
            RecoveryLength.Sanitize();
            Loop.Sanitize();
            Corkscrew.Sanitize();
            Spiral.Sanitize();
            Turn.Sanitize();
            HalfHelix.Sanitize();
            VerticalBump.Sanitize();
            Rotational.Sanitize();
            Primitives.RemoveAll(primitive => primitive == null);
            foreach (FeatureGeometryPrimitive primitive in Primitives) primitive.Sanitize();
            MinimumClearanceMeters = Mathf.Max(0f, MinimumClearanceMeters);
        }

        public bool Validate(out string error)
        {
            Sanitize();
            if (SchemaVersion != CurrentSchemaVersion)
            {
                error = $"Definition '{StableId}' uses schema {SchemaVersion}; expected {CurrentSchemaVersion}.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(StableId))
            {
                error = "Feature definition has no stable ID.";
                return false;
            }
            if (SemanticElement == SemanticElementId.None)
            {
                error = $"Definition '{StableId}' has no semantic element identity.";
                return false;
            }
            if (Length.Maximum <= 0f)
            {
                error = $"Definition '{StableId}' has no positive core Length range.";
                return false;
            }
            if (EntryLength.Maximum <= 0f || RecoveryLength.Maximum <= 0f)
            {
                error = $"Definition '{StableId}' must own positive EntryLength and RecoveryLength ranges.";
                return false;
            }
            if (Primitives.Count == 0)
            {
                error = $"Definition '{StableId}' contains no reusable geometry primitives.";
                return false;
            }
            float fraction = 0f;
            var primitiveIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Primitives.Count; i++)
            {
                FeatureGeometryPrimitive primitive = Primitives[i];
                if (string.IsNullOrWhiteSpace(primitive.PrimitiveId))
                {
                    error = $"Definition '{StableId}' has a primitive without a stable ID.";
                    return false;
                }
                if (!primitiveIds.Add(primitive.PrimitiveId))
                {
                    error = $"Definition '{StableId}' repeats primitive ID '{primitive.PrimitiveId}'.";
                    return false;
                }
                if (i > 0 && primitive.AllowGenericConnectorBefore)
                {
                    error = $"Definition '{StableId}' requests a generic connector inside its atomic primitive sequence.";
                    return false;
                }
                if (i > 0)
                {
                    TrackOrientationTag previousExit = Primitives[i - 1].ExitOrientation;
                    if (previousExit != TrackOrientationTag.Any &&
                        primitive.EntryOrientation != TrackOrientationTag.Any &&
                        previousExit != primitive.EntryOrientation)
                    {
                        error = $"Definition '{StableId}' has an orientation break before primitive '{primitive.PrimitiveId}'.";
                        return false;
                    }
                }
                fraction += primitive.LengthFraction;
            }
            if (Mathf.Abs(fraction - 1f) > 0.001f)
            {
                error = $"Definition '{StableId}' primitive core-length fractions total {fraction:F3}; expected 1.000.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.FullLoopV1 &&
                (Primitives.Count != 2 ||
                 Primitives.Exists(p => p.PrimitiveType != FeatureGeometryPrimitiveType.HalfLoop) ||
                 Loop.AllowedVerticalRotationUnits.Count == 0 ||
                 Loop.LimbSeparationYawCandidates.Count == 0))
            {
                error = $"Definition '{StableId}' is not a complete two-HalfLoop FullLoopV1 recipe.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.InlineCorkscrewV1 &&
                (Primitives.Count != 2 ||
                 Primitives.Exists(p => p.PrimitiveType != FeatureGeometryPrimitiveType.HalfCorkscrew) ||
                 Corkscrew.AllowedRoadRollUnits.Count == 0))
            {
                error = $"Definition '{StableId}' is not a complete two-HalfCorkscrew InlineCorkscrewV1 recipe.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.SpiralV1 &&
                (TopologyRole != TopologyRole.HeadingNeutral ||
                 Primitives.Count != 1 ||
                 Primitives[0].PrimitiveType != FeatureGeometryPrimitiveType.SpiralHelix ||
                 Spiral.AllowedStoryCounts.Count == 0 ||
                 Spiral.RadiusMeters.Maximum <= 0f ||
                 Spiral.ClimbPerStoryMeters.Maximum <= 0f))
            {
                error = $"Definition '{StableId}' is not a complete single-SpiralHelix SpiralV1 recipe.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.EasedTurnV1 &&
                (TopologyRole != TopologyRole.TurnRealization ||
                 Primitives.Count != 1 ||
                 (Primitives[0].PrimitiveType != FeatureGeometryPrimitiveType.EasedTurn &&
                  Primitives[0].PrimitiveType != FeatureGeometryPrimitiveType.ElevatedEasedTurn &&
                  Primitives[0].PrimitiveType != FeatureGeometryPrimitiveType.HalfHelixTurn) ||
                 Turn.HeadingDegrees.Maximum < Turn.HeadingDegrees.Minimum ||
                 Turn.HeadingDegrees.Maximum <= 0f ||
                 Turn.RadiusMultiplier.Maximum <= 0f ||
                 (Turn.CoreSectionType != TrackMacroSectionType.BankedCurve &&
                  Turn.CoreSectionType != TrackMacroSectionType.BankedHairpin &&
                  Turn.CoreSectionType != TrackMacroSectionType.Spiral)))
            {
                error = $"Definition '{StableId}' is not a complete single-EasedTurn recipe.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.EasedTurnV1 &&
                Primitives[0].PrimitiveType == FeatureGeometryPrimitiveType.ElevatedEasedTurn &&
                Turn.CrestHeightMeters.Maximum <= 0f)
            {
                error = $"Definition '{StableId}' uses ElevatedEasedTurn without a positive crest-height range.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.EasedTurnV1 &&
                 Primitives[0].PrimitiveType == FeatureGeometryPrimitiveType.HalfHelixTurn &&
                (Turn.CoreSectionType != TrackMacroSectionType.Spiral ||
                 HalfHelix.ElevationChangeMeters.Maximum >= -0.001f ||
                 ExitContract.ElevationChangeMeters >= -0.001f ||
                 Mathf.Abs(ExitContract.ElevationChangeMeters -
                           HalfHelix.ElevationChangeMeters.Preferred) > 0.001f))
            {
                error = $"Definition '{StableId}' uses HalfHelixTurn without a descending exit contract.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.VerticalBumpV1 &&
                (TopologyRole != TopologyRole.HeadingNeutral ||
                 Primitives.Count != VerticalBump.LobeCount ||
                 Primitives.Exists(primitive =>
                     primitive.PrimitiveType != FeatureGeometryPrimitiveType.CrestStraight &&
                     primitive.PrimitiveType != FeatureGeometryPrimitiveType.DipStraight) ||
                 VerticalBump.HeightMeters.Minimum <= 0f ||
                 VerticalBump.HeightMeters.Maximum <= 0f ||
                 ExitContract.HeadingChangeDegrees != 0f ||
                 ExitContract.ElevationChangeMeters != 0f))
            {
                error = $"Definition '{StableId}' is not a complete level-exit VerticalBumpV1 recipe.";
                return false;
            }
            if (Solver == FeatureDefinitionSolver.VerticalBumpV1)
            {
                for (int i = 0; i < Primitives.Count; i++)
                {
                    bool expectedDip = VerticalBump.StartsWithDip;
                    if (VerticalBump.AlternateLobeSign && (i & 1) != 0)
                        expectedDip = !expectedDip;
                    FeatureGeometryPrimitiveType expected = expectedDip
                        ? FeatureGeometryPrimitiveType.DipStraight
                        : FeatureGeometryPrimitiveType.CrestStraight;
                    if (Primitives[i].PrimitiveType == expected) continue;
                    error = $"Definition '{StableId}' lobe {i + 1} declares " +
                            $"{Primitives[i].PrimitiveType} but its vertical sequence requires {expected}.";
                    return false;
                }
            }
            if (Solver == FeatureDefinitionSolver.RotationalSequenceV1)
            {
                if (Primitives.Count < 1 || Primitives.Count > 2 ||
                    Primitives.Exists(primitive =>
                        primitive.PrimitiveType != FeatureGeometryPrimitiveType.RoadRoll &&
                        primitive.PrimitiveType != FeatureGeometryPrimitiveType.VerticalArc))
                {
                    error = $"Definition '{StableId}' must contain one or two RoadRoll/VerticalArc primitives.";
                    return false;
                }
                bool hasRoadRoll = Primitives.Exists(primitive =>
                    primitive.PrimitiveType == FeatureGeometryPrimitiveType.RoadRoll);
                bool hasVerticalArc = Primitives.Exists(primitive =>
                    primitive.PrimitiveType == FeatureGeometryPrimitiveType.VerticalArc);
                if ((hasRoadRoll && Rotational.RoadRollUnits <= 0) ||
                    (hasVerticalArc && Rotational.VerticalRotationUnits <= 0) ||
                    (hasRoadRoll && Rotational.MaximumCoreRollRateDegreesPerSecond <= 0f) ||
                    Rotational.FirstHalfRadius.Maximum <= 0f ||
                    Rotational.SecondHalfRadius.Maximum <= 0f)
                {
                    error = $"Definition '{StableId}' has incomplete rotational units or radius ranges.";
                    return false;
                }
                if (TopologyRole == TopologyRole.HeadingNeutral &&
                    Mathf.Abs(ExitContract.HeadingChangeDegrees) > 0.001f)
                {
                    error = $"Definition '{StableId}' is heading-neutral but declares a non-zero exit heading.";
                    return false;
                }
                if (TopologyRole == TopologyRole.TurnRealization &&
                    Turn.HeadingDegrees.Maximum <= 0f)
                {
                    error = $"Definition '{StableId}' is a rotational turn without a positive heading window.";
                    return false;
                }
            }
            error = "";
            return true;
        }

        public TrackFeatureDefinition Clone()
        {
            TrackFeatureDefinition clone = JsonUtility.FromJson<TrackFeatureDefinition>(
                JsonUtility.ToJson(this, false));
            clone?.Sanitize();
            return clone;
        }

        public string ComputeContentHash()
        {
            TrackFeatureDefinition copy = Clone();
            return TrackGeneration.Core.GenerationHashUtility.Sha256Hex(JsonUtility.ToJson(copy, false));
        }
    }

}
