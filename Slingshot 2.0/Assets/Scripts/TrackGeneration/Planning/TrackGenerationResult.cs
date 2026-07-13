using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Branching;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Structured reason a generation attempt (or the whole request) failed.</summary>
    public enum GenerationFailureReason
    {
        None,
        InvalidConfiguration,
        RequiredFeatureMissing,
        RequiredPatternMissing,
        InsufficientLengthBudget,
        ClosurePositionFailure,
        ClosureHeadingFailure,
        ClosureElevationFailure,
        SelfIntersection,
        VerticalClearanceFailure,
        BranchBalanceFailure,
        BranchMergeFailure,
        OrientationMismatch,
        TransitionRateExceeded,
        CurveRadiusViolation,
        SlopeViolation,
        RollRateViolation,
        PitchRateViolation,
        RingBudgetExceeded,
        FeatureEnvelopeCollision,
        RaceCourseBuildFailure,
        MeshBuildFailure
    }

    /// <summary>
    /// One recorded failure: which attempt, why, where, and the requested vs achieved
    /// numbers — every failed attempt stores its reason instead of silently continuing.
    /// </summary>
    [Serializable]
    public class GenerationAttemptFailure
    {
        public int AttemptIndex;
        public GenerationFailureReason Reason;

        [Tooltip("Section, pattern or validator that failed.")]
        public string Subject;

        [Tooltip("Route/branch involved, empty for main-line failures.")]
        public string RouteOrBranch;

        public float RequestedValue;
        public float AchievedValue;

        [Tooltip("Track-root-local position related to the failure (zero when not positional).")]
        public Vector3 Position;

        [Tooltip("Human-readable explanation of what went wrong.")]
        public string Message;

        public override string ToString()
            => $"[attempt {AttemptIndex}] {Reason}: {Message}";
    }

    /// <summary>Measured properties of a generated (or candidate) layout.</summary>
    [Serializable]
    public class TrackGenerationMetrics
    {
        public float LapLengthMeters;
        public float EstimatedNeutralLapTimeSeconds;
        public int TurnCount;
        public int LoopCount;
        public int CorkscrewCount;
        public int SpiralCount;
        public int HalfLoopCount;
        public int JumpCount;
        public int HairpinCount;
        public int ChicaneCount;
        public int SCurveCount;
        public int CompoundPatternCount;
        public int BranchGroupCount;
        public float MinElevation;
        public float MaxElevation;
        public float MinObservedClearance;
        public float MaxFacetAngleObserved;
        public int TotalRings;
        public float SelectedCandidateScore;

        public float ElevationRange => MaxElevation - MinElevation;
    }

    /// <summary>One relaxed setting recorded by the RelaxOptionalSettings policy.</summary>
    [Serializable]
    public class RelaxedSettingRecord
    {
        public string SettingName;
        public float OriginalValue;
        public float RelaxedValue;

        public override string ToString() => $"{SettingName}: {OriginalValue:G4} → {RelaxedValue:G4}";
    }

    /// <summary>
    /// Full report of one generation request: seed, outcome, attempt statistics,
    /// per-reason failure counts, warnings, relaxations and route balance tables.
    /// Serializable so the inspector can display the last report.
    /// </summary>
    [Serializable]
    public class TrackGenerationReport
    {
        public int Seed;
        public bool Success;
        public bool UsedFallback;
        public string FallbackDescription = "";
        public int AttemptsEvaluated;
        public int ValidCandidateCount;
        public float SelectedCandidateScore;

        public List<GenerationAttemptFailure> Failures = new List<GenerationAttemptFailure>();
        public List<string> Warnings = new List<string>();
        public List<RelaxedSettingRecord> RelaxedSettings = new List<RelaxedSettingRecord>();

        [Tooltip("Per-branch-group route time estimates for each craft archetype.")]
        public List<BranchBalanceMetrics> BranchBalance = new List<BranchBalanceMetrics>();

        public void AddFailure(int attempt, GenerationFailureReason reason, string subject, string message,
            float requested = 0f, float achieved = 0f, Vector3 position = default, string routeOrBranch = "")
        {
            Failures.Add(new GenerationAttemptFailure
            {
                AttemptIndex = attempt,
                Reason = reason,
                Subject = subject,
                Message = message,
                RequestedValue = requested,
                AchievedValue = achieved,
                Position = position,
                RouteOrBranch = routeOrBranch
            });
        }

        /// <summary>Failure counts grouped by reason, most frequent first.</summary>
        public List<(GenerationFailureReason reason, int count)> FailureCountsByReason()
        {
            var counts = new Dictionary<GenerationFailureReason, int>();
            foreach (var f in Failures)
            {
                counts.TryGetValue(f.Reason, out int c);
                counts[f.Reason] = c + 1;
            }
            var list = new List<(GenerationFailureReason, int)>();
            foreach (var kv in counts) list.Add((kv.Key, kv.Value));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }
    }

    /// <summary>
    /// The authoritative generated layout: the ordered flattened section list used by
    /// mesh building and camera systems, PLUS the branch groups that retain the
    /// two-route relationships the flat list cannot express.
    /// </summary>
    public sealed class GeneratedTrackLayout
    {
        /// <summary>All sections in track order. Branch route pairs appear consecutively (route A then route B) and span the same gates.</summary>
        public List<GeneratedTrackSection> Sections = new List<GeneratedTrackSection>();

        /// <summary>Branch groups with their gates, routes and balance data.</summary>
        public List<GeneratedBranchGroup> BranchGroups = new List<GeneratedBranchGroup>();

        /// <summary>Main-line lap length in meters (branch route A defines the canonical arc).</summary>
        public float LapLength;

        /// <summary>Estimated neutral-archetype lap time (seconds).</summary>
        public float EstimatedNeutralLapTime;

        public TrackGenerationMetrics Metrics = new TrackGenerationMetrics();
    }

    /// <summary>
    /// The full result of one generation request. Replaces the old bare
    /// List&lt;GeneratedTrackSection&gt; return value — failures explain themselves.
    /// </summary>
    public sealed class TrackGenerationResult
    {
        public bool Success;
        public bool UsedFallback;

        public int RequestedSeed;
        public int AttemptsEvaluated;
        public int ValidCandidateCount;

        public GeneratedTrackLayout Layout;
        public TrackGenerationMetrics Metrics => Layout?.Metrics;
        public TrackGenerationReport Report = new TrackGenerationReport();
    }
}
