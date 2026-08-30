using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
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
        DualRoadFitFailure,
        DualRoadBalanceFailure,
        QuarterGateFailure,
        OrientationMismatch,
        TransitionRateExceeded,
        CurveRadiusViolation,
        SlopeViolation,
        RollRateViolation,
        PitchRateViolation,
        RingBudgetExceeded,
        GenerationTimeBudgetExceeded,
        FeatureEnvelopeCollision,
        RaceCourseBuildFailure,
        MeshBuildFailure,
        RecipeCompatibilityFailure,
        ProceduralRhythmViolation
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

        [Tooltip("Quarter/road involved, empty for main-line failures.")]
        public string QuarterOrRoad;

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
        public int HalfHelixTurnaroundCount;
        public int HalfLoopCount;
        public int JumpCount;
        public int HairpinCount;
        public int ChicaneCount;
        public int SCurveCount;
        public int FullPipeCount;
        public int WallrideCount;
        public int CompoundPatternCount;
        public int DualRoadQuarterCount;
        public float MinElevation;
        public float MaxElevation;
        public float MinObservedClearance;
        public float MaxFacetAngleObserved;
        public int TotalRings;
        public float SelectedCandidateScore;
        public int EncounterCount;
        public int DistinctEncounterFamilies;
        public int LongestEncounterFamilyStreak;
        public int LongestSCurveFamilyStreak;
        public int MaxSCurveFamilyInWindow;
        public int RhythmViolationCount;
        public float RhythmScore;

        public float ElevationRange => MaxElevation - MinElevation;
    }

    /// <summary>
    /// One connector decision made by the connector analysis: how a straight between
    /// two content sections was classified and what was done about its length.
    /// </summary>
    [Serializable]
    public class ConnectorDecisionRecord
    {
        public string Name;
        public ConnectorBehavior Behavior;
        public float OriginalLength;
        public float ResolvedLength;
        public float RequiredLength;
        public bool Absorbed;
        public string AbsorbedInto = "";
        public string InheritedProperties = "";
        public string Notes = "";

        public override string ToString()
        {
            string s = $"{Name}: {Behavior}, {OriginalLength:F0}m → {ResolvedLength:F0}m (required {RequiredLength:F0}m)";
            if (Absorbed) s += $" — absorbed into {AbsorbedInto}";
            if (!string.IsNullOrEmpty(InheritedProperties)) s += $" | inherits: {InheritedProperties}";
            if (!string.IsNullOrEmpty(Notes)) s += $" | {Notes}";
            return s;
        }
    }

    /// <summary>
    /// One independent physical road region (an anchored retopology segment) and the
    /// subdivision tier it received. Subdivision Count = intervals; Frame Count = +1.
    /// </summary>
    [Serializable]
    public class SubdivisionRegionRecord
    {
        public string RegionName;
        public string Sections;
        public float LengthMeters;
        public int RawRequirement;
        public int SelectedTier;
        public float SpacingMeters;
        public string LimitingFactor = "";

        public override string ToString()
            => $"{RegionName} ({LengthMeters:F0}m): raw {RawRequirement} → tier {SelectedTier} ({SpacingMeters:F2} m/ring, limited by {LimitingFactor}) [{Sections}]";
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
        [Range(0, 100)] public int TrackRating;
        public string RhythmSummary = "";
        public List<string> EncounterTimelines = new List<string>();

        [Tooltip("Total wall-clock time spent across every pipeline pass used by this generation request.")]
        public float GenerationDurationSeconds;

        [Tooltip("Number of strict, relaxed, or template pipeline passes attempted for this request.")]
        public int PipelinePassCount = 1;

        [Tooltip("The pass that produced the accepted result, or 'No accepted pass' when generation failed.")]
        public string AcceptedPass = "Strict";

        [Tooltip("Zero-based generation attempt that produced the accepted candidate. -1 when no candidate was accepted.")]
        public int SelectedAttemptIndex = -1;

        [Tooltip("Zero-based position of the accepted candidate in the valid-candidate list. -1 when no candidate was accepted.")]
        public int SelectedCandidateIndex = -1;

        [Tooltip("Closure angle relief (§7): whether the flag was on this run, how many candidates invoked it, and how many it closed to tolerance. 0/0 with the flag on means no candidate reached the closure solve with ≥2 eligible plain corners.")]
        public bool AngleReliefEnabled;
        public int AngleReliefAttempts;
        public int AngleReliefClosures;

        public List<GenerationAttemptFailure> Failures = new List<GenerationAttemptFailure>();
        public List<string> Warnings = new List<string>();
        public List<RelaxedSettingRecord> RelaxedSettings = new List<RelaxedSettingRecord>();

        [Tooltip("Generate command this run came from (Generate New Everything, Same Layout New Content, …).")]
        public string RegenerationCommand = "";

        [Tooltip("Seed streams preserved by the command.")]
        public List<string> PreservedStreams = new List<string>();

        [Tooltip("Seed streams re-randomized by the command.")]
        public List<string> ChangedStreams = new List<string>();

        [Tooltip("Settings groups that were locked while this track was generated.")]
        public List<string> LockedSettingsGroups = new List<string>();

        [Tooltip("Layout lock mode active for this run.")]
        public string LayoutLockMode = "";

        [Tooltip("Resolved Random preset selections, with requested values and reasons.")]
        public string ResolvedSelectionSummary = "";

        [Tooltip("Connector-analysis decisions of the selected candidate (non-trivial connectors only).")]
        public List<ConnectorDecisionRecord> ConnectorDecisions = new List<ConnectorDecisionRecord>();

        [Tooltip("Per-region subdivision tiers of the selected candidate.")]
        public List<SubdivisionRegionRecord> SubdivisionRegions = new List<SubdivisionRegionRecord>();

        [Tooltip("Stage A: exact measured exit of every feature group (heading/displacement/elevation/roll), computed by the same builders the candidate uses.")]
        public List<string> FeatureExitRecords = new List<string>();

        [Tooltip("Stage C: per-boundary transition classification (DirectWeld / AdaptiveBlend / ExplicitRecovery / Rejected) with per-channel blend demands. Reporting only — no geometry changes.")]
        public List<string> TransitionRecords = new List<string>();

        [Tooltip("Structured Stage C boundary decisions used by the V2 connector shadow audit. Reporting only — no geometry changes.")]
        public List<TransitionDecision> TransitionDecisions = new List<TransitionDecision>();

        [Tooltip("Stage 2 read-only comparison between semantic boundary requirements and current connector authority.")]
        public List<ConnectorShadowRecord> ConnectorShadowRecords = new List<ConnectorShadowRecord>();

        [Tooltip("Compact parity totals for the Stage 2 connector shadow audit.")]
        public ConnectorShadowSummary ConnectorShadowSummary = new ConnectorShadowSummary();

        [Tooltip("V2 stable topology-demand identities for the selected candidate. Reporting only until interactive replacement is enabled.")]
        public List<TopologySlotRecord> TopologySlots = new List<TopologySlotRecord>();

        [Tooltip("Per-dual-quarter route time estimates for each craft archetype.")]
        public List<QuarterRouteBalance> QuarterBalance = new List<QuarterRouteBalance>();

        /// <summary>
        /// Detailed failure entries are CAPPED: the report keeps the most recent ones
        /// while the per-reason counts stay exact. An unbounded list (a 60-attempt run
        /// can produce thousands of entries) bloats scene serialization and makes the
        /// inspector's SerializedObject traversal pay for every string on every repaint.
        /// </summary>
        public const int MaxStoredFailures = 300;

        [SerializeField] private List<FailureReasonCount> failureCounts = new List<FailureReasonCount>();
        [SerializeField] private List<GenerationAttemptFailure> representativeFailures =
            new List<GenerationAttemptFailure>();

        [Serializable]
        public class FailureReasonCount
        {
            public GenerationFailureReason Reason;
            public int Count;
        }

        public void AddFailure(int attempt, GenerationFailureReason reason, string subject, string message,
            float requested = 0f, float achieved = 0f, Vector3 position = default, string quarterOrRoad = "")
        {
            var failure = new GenerationAttemptFailure
            {
                AttemptIndex = attempt,
                Reason = reason,
                Subject = subject,
                Message = message,
                RequestedValue = requested,
                AchievedValue = achieved,
                Position = position,
                QuarterOrRoad = quarterOrRoad
            };
            Failures.Add(failure);
            RememberRepresentative(failure);
            if (Failures.Count > MaxStoredFailures)
                Failures.RemoveAt(0); // keep the most recent entries

            foreach (var rc in failureCounts)
            {
                if (rc.Reason != reason) continue;
                rc.Count++;
                return;
            }
            failureCounts.Add(new FailureReasonCount { Reason = reason, Count = 1 });
        }

        /// <summary>
        /// Prepends failures from an earlier pipeline pass without allowing the serialized
        /// detail list to exceed <see cref="MaxStoredFailures"/>. Aggregate counts remain
        /// exact even when older details have to be discarded.
        /// </summary>
        public void PrependFailuresFrom(TrackGenerationReport earlier)
        {
            if (earlier == null || ReferenceEquals(earlier, this)) return;

            EnsureFailureCounts();
            foreach (var entry in earlier.FailureCountsByReason())
                AddFailureCount(entry.reason, entry.count);

            foreach (var entry in earlier.FailureCountsByReason())
            {
                if (RepresentativeFailure(entry.reason) != null) continue;
                GenerationAttemptFailure representative = earlier.RepresentativeFailure(entry.reason);
                if (representative != null) RememberRepresentative(representative);
            }

            int available = Mathf.Max(0, MaxStoredFailures - Failures.Count);
            int take = Mathf.Min(available, earlier.Failures.Count);
            if (take > 0)
                Failures.InsertRange(0, earlier.Failures.GetRange(earlier.Failures.Count - take, take));

            TrimStoredFailuresToLimit();
        }

        /// <summary>
        /// Migrates an older serialized report whose detailed list predates the cap.
        /// Returns the number of discarded detail rows so the editor can diagnose it.
        /// </summary>
        public int TrimStoredFailuresToLimit()
        {
            if (Failures.Count <= MaxStoredFailures) return 0;

            EnsureFailureCounts();
            int removed = Failures.Count - MaxStoredFailures;
            Failures.RemoveRange(0, removed);
            return removed;
        }

        private void EnsureFailureCounts()
        {
            if (failureCounts.Count > 0 || Failures.Count == 0) return;
            foreach (var failure in Failures)
                AddFailureCount(failure.Reason, 1);
        }

        private void AddFailureCount(GenerationFailureReason reason, int count)
        {
            foreach (var rc in failureCounts)
            {
                if (rc.Reason != reason) continue;
                rc.Count += count;
                return;
            }
            failureCounts.Add(new FailureReasonCount { Reason = reason, Count = count });
        }

        private void RememberRepresentative(GenerationAttemptFailure failure)
        {
            if (failure == null) return;
            representativeFailures ??= new List<GenerationAttemptFailure>();
            int existing = representativeFailures.FindIndex(item =>
                item != null && item.Reason == failure.Reason);
            if (existing >= 0) representativeFailures[existing] = failure;
            else representativeFailures.Add(failure);
        }

        /// <summary>
        /// Returns a retained detail row for a failure reason even when the bounded
        /// chronological tail has discarded every occurrence of that rare reason.
        /// </summary>
        public GenerationAttemptFailure RepresentativeFailure(GenerationFailureReason reason)
        {
            if (representativeFailures != null)
            {
                for (int i = representativeFailures.Count - 1; i >= 0; i--)
                {
                    GenerationAttemptFailure failure = representativeFailures[i];
                    if (failure != null && failure.Reason == reason) return failure;
                }
            }

            // Compatibility for reports serialized before representative rows existed.
            if (Failures != null)
            {
                for (int i = Failures.Count - 1; i >= 0; i--)
                {
                    GenerationAttemptFailure failure = Failures[i];
                    if (failure != null && failure.Reason == reason) return failure;
                }
            }
            return null;
        }

        /// <summary>Exact failure counts grouped by reason, most frequent first (unaffected by the detail cap).</summary>
        public List<(GenerationFailureReason reason, int count)> FailureCountsByReason()
        {
            var list = new List<(GenerationFailureReason, int)>();
            if (failureCounts.Count > 0)
            {
                foreach (var rc in failureCounts) list.Add((rc.Reason, rc.Count));
            }
            else
            {
                // Reports serialized before the aggregate existed: derive from the entries.
                var counts = new Dictionary<GenerationFailureReason, int>();
                foreach (var f in Failures)
                {
                    counts.TryGetValue(f.Reason, out int c);
                    counts[f.Reason] = c + 1;
                }
                foreach (var kv in counts) list.Add((kv.Key, kv.Value));
            }
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }

        /// <summary>Total failures recorded (exact even when the detailed entries were capped).</summary>
        public int TotalFailureCount
        {
            get
            {
                if (failureCounts.Count == 0) return Failures.Count;
                int total = 0;
                foreach (var rc in failureCounts) total += rc.Count;
                return total;
            }
        }
    }

    /// <summary>
    /// The authoritative generated layout: the ordered flattened section list used by
    /// mesh building and camera systems, PLUS the four quarters that retain the
    /// road relationships the flat list cannot express.
    /// </summary>
    [Serializable]
    public sealed class GeneratedTrackLayout
    {
        /// <summary>
        /// All sections in track order. A dual quarter's alternate road (RoadId 1)
        /// appears as a contiguous run inserted between road A's launch lip and the
        /// exit air gap of that quarter.
        /// </summary>
        public List<GeneratedTrackSection> Sections = new List<GeneratedTrackSection>();

        /// <summary>The authoritative 4 quarters of the lap (always exactly 4).</summary>
        public List<GeneratedTrackQuarter> Quarters = new List<GeneratedTrackQuarter>();

        /// <summary>Per-region subdivision tier decisions recorded by the retopology pass.</summary>
        public List<SubdivisionRegionRecord> SubdivisionRegions = new List<SubdivisionRegionRecord>();

        /// <summary>Canonical lap length in meters — road A only; a Dual Road Quarter never duplicates length.</summary>
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
        public int SelectedAttemptIndex = -1;
        public int SelectedCandidateIndex = -1;

        public GeneratedTrackLayout Layout;
        /// <summary>
        /// Sanitized settings that actually produced the accepted candidate. This can
        /// differ from the requested settings when an explicit failure policy performs
        /// a relaxed or simple-template recovery pass.
        /// </summary>
        public TrackDesignerSettings EffectiveDesignerSettings;
        public TrackGenerationMetrics Metrics => Layout?.Metrics;
        public TrackGenerationReport Report = new TrackGenerationReport();
    }
}
