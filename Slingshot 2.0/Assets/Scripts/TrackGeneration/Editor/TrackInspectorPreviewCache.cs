#if UNITY_EDITOR
using System.Collections.Generic;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using UnityEngine;

namespace TrackGeneration.Editor
{
    /// <summary>
    /// Cached data for the TrackGenerator inspector. The inspector's normal job is to
    /// DRAW this cache — it must never resolve settings, traverse the report or build
    /// summary strings during ordinary repaint events. Refreshes happen only when:
    ///   • the designer settings actually change (ApplyModifiedProperties / undo-redo),
    ///   • a generation result is stored (GenerationRevision changes),
    ///   • an explicit command (Validate) marks the cache dirty.
    /// Plain class with no GUI calls so edit-mode tests can prove that contract.
    /// </summary>
    public sealed class TrackInspectorPreviewCache
    {
        // ── Diagnostics: proof that expensive work is NOT per-repaint. ──
        public int ResolveCount { get; private set; }
        public int SummaryRebuildCount { get; private set; }

        // ── Resolved settings preview (built by RefreshResolvedIfNeeded) ──
        public string ResolvedSummary { get; private set; } = "";
        public readonly List<ResolvedConfigIssue> ResolvedIssues = new List<ResolvedConfigIssue>();
        public bool HasResolved { get; private set; }

        // ── Report card summary (built by RefreshReportIfNeeded) ──
        public bool HasReport { get; private set; }
        public bool ReportSuccess { get; private set; }
        public bool ReportUsedFallback { get; private set; }
        public string ReportHeadline { get; private set; } = "";
        public string ReportBody { get; private set; } = "";
        public string ReportStreamsLine { get; private set; } = "";
        public string ReportLocksLine { get; private set; } = "";
        public string ReportLayoutLockLine { get; private set; } = "";
        public string PrimaryFailure { get; private set; } = "";

        private bool _resolvedDirty = true;
        private int _reportRevision = int.MinValue;

        /// <summary>Call when designer settings changed (inspector edit, undo/redo, preset apply).</summary>
        public void MarkSettingsDirty() => _resolvedDirty = true;

        /// <summary>True when the resolved preview needs a rebuild before drawing.</summary>
        public bool ResolvedDirty => _resolvedDirty;

        /// <summary>
        /// Rebuilds the resolved-settings preview when dirty. Returns true when work ran.
        /// This is the ONLY place the inspector resolves settings — once per real change,
        /// never per repaint.
        /// </summary>
        public bool RefreshResolvedIfNeeded(TrackConfig config, TrackDesignerSettings designer)
        {
            if (!_resolvedDirty) return false;
            _resolvedDirty = false;
            ResolvedIssues.Clear();

            if (config == null || designer == null)
            {
                ResolvedSummary = "";
                HasResolved = false;
                return true;
            }

            ResolveCount++;
            var r = ResolvedTrackGenerationConfig.Resolve(config, designer.Clone());

            var sb = new System.Text.StringBuilder(1024);
            sb.AppendLine($"Design speed: {r.DesignSpeedKph:F0} km/h  ({r.DesignSpeedMps:F0} m/s)");
            sb.AppendLine($"Requested lap: {r.TargetLapTimeSeconds:F0} s  → target length ≈ {r.TargetTrackLength / 1000f:F1} km (cap {r.MaxTrackLength / 1000f:F1} km)");
            sb.AppendLine($"Straights: {r.MinStraightLength:F0}–{r.MaxStraightLength:F0} m | curve radius {r.MinCurveRadius:F0}–{r.MaxCurveRadius:F0} m | turns {r.MinTurnCount}–{r.MaxTurnCount} ({r.DirectionPattern})");
            sb.AppendLine($"Connectors: min {r.MinimumConnectorLength:F0} m | inheritance {r.ConnectorInheritanceStrength:P0} | bank reversal ≥ {r.MinimumBankReversalLength:F0} m");
            sb.AppendLine($"Bank blend {r.BankTransitionLength:F0} m | pitch {r.PitchTransitionLength:F0} m | roll {r.RollTransitionLength:F0} m | max bank {r.MaxBankAngle:F0}°");
            sb.AppendLine($"Turn rounding: {(r.DynamicTurnRoundingEnabled ? $"on ({r.TurnRoundingStrength:P0}, min flat {r.RoadProfile.MinTurnCenterFlatRatio:F2})" : "off")} | catch wall: {(r.CatchWallEnabled ? $"on ({r.RoadProfile.MaxOverhangAngleDeg:F0}° past vertical)" : "off")}");
            sb.AppendLine($"Feature groups: {r.MinFeatureGroups}–{r.MaxFeatureGroups} | full pipes {r.FullPipes.MinimumCount}–{r.FullPipes.MaximumCount} | wallrides {r.Wallrides.MinimumCount}–{r.Wallrides.MaximumCount}");
            sb.AppendLine($"Quarters: {r.MinDualQuarters}–{r.MaxDualQuarters} dual ({r.QuarterChoiceType}) | lane sep {r.LaneSeparation:F0} m | catch {r.QuarterCatchWidth:F0} m | road B width {r.DualRoadWidth:F0} m");
            sb.AppendLine($"Elevation: amplitude {r.TargetElevationAmplitude:F0} m | majors {r.MinMajorElevationSections}–{r.MaxMajorElevationSections} | climb ≤ {r.MaxClimbAngle:F0}°");
            sb.Append($"Subdivision ladder: {r.SubdivisionLadder.Count} tiers ({r.SubdivisionLadder[0]}…{r.SubdivisionLadder[r.SubdivisionLadder.Count - 1]}) | facet target {r.MaxRingFacetAngle:F2}°");
            ResolvedSummary = sb.ToString();
            ResolvedIssues.AddRange(r.Issues);
            HasResolved = true;
            return true;
        }

        /// <summary>
        /// Rebuilds the report-card summary when the stored generation result changed.
        /// Returns true when work ran.
        /// </summary>
        public bool RefreshReportIfNeeded(TrackGenerationReport report, TrackGenerationMetrics metrics, int generationRevision)
        {
            if (generationRevision == _reportRevision) return false;
            _reportRevision = generationRevision;
            SummaryRebuildCount++;

            HasReport = report != null && (report.Success || report.Failures.Count > 0 || report.AttemptsEvaluated > 0);
            if (!HasReport)
            {
                ReportHeadline = "";
                ReportBody = "";
                return true;
            }

            ReportSuccess = report.Success;
            ReportUsedFallback = report.UsedFallback;

            var sb = new System.Text.StringBuilder(512);
            if (report.Success)
            {
                ReportHeadline = report.UsedFallback
                    ? "GENERATED SUCCESSFULLY   [TEMPLATE FALLBACK — request NOT satisfied]"
                    : "GENERATED SUCCESSFULLY";

                sb.AppendLine($"Seed <b>{report.Seed}</b>   Command: {report.RegenerationCommand}");
                if (metrics != null)
                {
                    sb.AppendLine($"Estimated lap <b>{metrics.EstimatedNeutralLapTimeSeconds:F1}s</b>   Length <b>{metrics.LapLengthMeters / 1000f:F2} km</b>   Turns <b>{metrics.TurnCount}</b>");
                    sb.AppendLine($"Loops {metrics.LoopCount}   Corkscrews {metrics.CorkscrewCount}   Spirals {metrics.SpiralCount}   Half-loops {metrics.HalfLoopCount}   Jumps {metrics.JumpCount}");
                    sb.AppendLine($"Full pipes <b>{metrics.FullPipeCount}</b>   Wallrides <b>{metrics.WallrideCount}</b>   Dual quarters <b>{metrics.DualRoadQuarterCount}</b>   Compounds {metrics.CompoundPatternCount}");
                    sb.AppendLine($"Rings {metrics.TotalRings}   Max facet {metrics.MaxFacetAngleObserved:F2}°   Elevation {metrics.MinElevation:F0}..{metrics.MaxElevation:F0} m");
                }
                sb.Append($"Attempts {report.AttemptsEvaluated}   Candidates {report.ValidCandidateCount}   Score {report.SelectedCandidateScore:F1}   Warnings {report.Warnings.Count}");
                PrimaryFailure = "";
            }
            else
            {
                ReportHeadline = "GENERATION FAILED";
                PrimaryFailure = report.Failures.Count > 0
                    ? report.Failures[report.Failures.Count - 1].Message
                    : "Unknown failure.";
                sb.Append($"Primary reason:\n<b>{PrimaryFailure}</b>\n\nAttempts: {report.AttemptsEvaluated}. Previous valid track preserved.");
            }
            ReportBody = sb.ToString();

            ReportStreamsLine = report.PreservedStreams.Count > 0 && report.PreservedStreams.Count < 6
                ? $"Preserved streams: {string.Join(", ", report.PreservedStreams)}   |   Changed: {string.Join(", ", report.ChangedStreams)}"
                : "";
            ReportLocksLine = report.LockedSettingsGroups.Count > 0
                ? $"Locked groups: {string.Join(", ", report.LockedSettingsGroups)}"
                : "";
            ReportLayoutLockLine = !string.IsNullOrEmpty(report.LayoutLockMode) && report.LayoutLockMode != "Unlocked"
                ? $"Layout lock: {report.LayoutLockMode}"
                : "";
            return true;
        }
    }
}
#endif
