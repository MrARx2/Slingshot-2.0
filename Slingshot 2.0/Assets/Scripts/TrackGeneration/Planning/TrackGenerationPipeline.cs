using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Validation;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Optional deterministic sweep controls. Track Editor uses an accepted track's
    /// exact attempt index so an authored edit rebuilds the visible route instead of
    /// searching unrelated random candidates for a similarly named topology slot.
    /// </summary>
    public sealed class TrackGenerationPipelineOptions
    {
        public int OnlyAttemptIndex = -1;
        public IReadOnlyList<AuthoringElevationBaselineEntry> AuthoringElevationBaseline;
    }

    /// <summary>
    /// Compact elevation state from the accepted layout that a focused Track Editor
    /// rebuild starts from. Accepted authored variants serialize this state so Exact
    /// Replay and Undo can reproduce the same vertical design after a domain reload.
    /// </summary>
    [System.Serializable]
    public sealed class AuthoringElevationBaselineEntry
    {
        public string MatchKey = "";
        public float ElevationChange;
        public float HillHeight;
        public float StartElevation;
        public float EndElevation;
        public int RoadId;
        public bool CanonicalLapEnd;
    }

    /// <summary>
    /// Converts a normal resolved generator request into a designer-first Track Editor
    /// rebuild. The visible accepted lap becomes the baseline; length is allowed to grow
    /// as required by the authored feature while all geometry and driveability checks
    /// remain authoritative.
    /// </summary>
    public static class TrackEditorAuthoringPolicy
    {
        private const float MinimumGrowthHeadroomMeters = 100000f;
        private const float BaselineGrowthMultiplier = 3f;
        private const float ClosureRadiusMultiplier = 4f;

        public static void Apply(ResolvedTrackGenerationConfig config, float baselineLapLength)
        {
            if (config == null) return;

            float baseline = Mathf.Max(config.TargetTrackLength, baselineLapLength);
            float numericalCeiling = Mathf.Max(
                baseline + MinimumGrowthHeadroomMeters,
                baseline * BaselineGrowthMultiplier);

            config.DesignerAuthoringMode = true;
            config.TargetTrackLength = Mathf.Max(config.TargetTrackLength, baselineLapLength);
            config.MaxTrackLength = Mathf.Max(config.MaxTrackLength, numericalCeiling);

            // A larger radius is always gentler to drive. It gives the closure solve
            // lateral reach without weakening minimum-radius or transition-rate rules.
            config.MaxClosureCurveRadius = Mathf.Max(
                config.MaxClosureCurveRadius,
                config.MaxCurveRadius * ClosureRadiusMultiplier);

            config.Issues.Add(new ResolvedConfigIssue
            {
                Severity = ResolvedIssueSeverity.Info,
                Field = "TrackEditor.AuthoringFreedom",
                Message = $"Designer authoring mode: the {baselineLapLength / 1000f:0.0}km accepted lap is the baseline; " +
                          "the procedural length cap is advisory while the edited route is fitted."
            });
        }
    }

    /// <summary>
    /// The candidate loop: plans candidates with deterministic per-attempt seeds,
    /// builds and validates them, scores the valid ones and selects the best.
    /// Every failed attempt records WHY. The same seed + settings always produce the
    /// same selected result; validation order never touches the random streams.
    /// </summary>
    public class TrackGenerationPipeline
    {
        /// <summary>
        /// Extra Elevation-stream rolls tried when a planned layout fails ONLY the 2D
        /// self-proximity walk. Each retry keeps the attempt's layout/feature/quarter
        /// streams untouched, so it purely searches for a climb placement that
        /// vertically separates the folded legs.
        /// </summary>
        private const int ElevationRerollsPerAttempt = 6;
        // Track Editor keeps the accepted layout/feature/quarter streams fixed. It can
        // therefore spend more cheap plan-only retries on elevation separation without
        // morphing the designer's track into an unrelated random candidate.
        private const int AuthoringElevationRerollsPerAttempt = 24;
        // One primary pass plus one optional relaxed pass is capped near the project's
        // established 1:20-2:00 generation window, instead of ever hanging for 12 min.
        private const double SynchronousTimeBudgetSeconds = 60.0;

        private class Candidate
        {
            public GeneratedTrackLayout Layout;
            public TopologyPlan Plan;
            public float Score;
            public int AttemptIndex;
        }

        /// <summary>
        /// Runs generation for one resolved config. Never throws; the result explains
        /// itself. When <paramref name="streams"/> is provided, each planning subsystem
        /// draws from its own independent seed stream (partial regeneration); otherwise
        /// all streams derive from the base seed.
        /// </summary>
        public TrackGenerationResult Run(
            ResolvedTrackGenerationConfig cfg,
            TrackSeed seed,
            TrackSeedStreams streams = null,
            IReadOnlyList<TopologySlotOverride> topologyOverrides = null,
            TrackGenerationPipelineOptions options = null)
        {
            var generationTimer = Stopwatch.StartNew();
            var result = new TrackGenerationResult
            {
                RequestedSeed = seed.BaseSeed,
                Report = { Seed = seed.BaseSeed }
            };

            foreach (var issue in cfg.Issues)
            {
                if (issue.Severity == ResolvedIssueSeverity.Error)
                    result.Report.AddFailure(-1, GenerationFailureReason.InvalidConfiguration, issue.Field, issue.Message);
                else if (issue.Severity == ResolvedIssueSeverity.Warning)
                    result.Report.Warnings.Add($"{issue.Field}: {issue.Message}");
            }

            if (cfg.HasHardErrors)
            {
                result.Success = false;
                return Finish(result, generationTimer);
            }

            // Dedicated candidate-variation stream: mesh density or validation changes
            // can never alter which attempt seeds are drawn. With explicit seed streams,
            // every subsystem gets its own per-attempt RNG instead.
            var attemptSeedRng = seed.CreateSubsystemRandom("CandidateVariation");

            var planner = new TrackTopologyPlanner();
            var builder = new TrackCandidateBuilder();
            var candidates = new List<Candidate>();

            // Closure-relief diagnostics accumulate across this sweep's candidates.
            TrackTopologyPlanner.AngleReliefAttempts = 0;
            TrackTopologyPlanner.AngleReliefClosures = 0;

            int onlyAttemptIndex = options?.OnlyAttemptIndex ?? -1;
            int sweepCount = onlyAttemptIndex >= 0 ? 1 : cfg.MaxAttempts;

            // The legacy single-stream path draws one candidate seed per absolute
            // attempt. Advance to the accepted attempt before the focused edit sweep.
            if (streams == null && onlyAttemptIndex > 0)
                for (int skipped = 0; skipped < onlyAttemptIndex; skipped++)
                    attemptSeedRng.NextUInt();

            int attempts = 0;
            for (int sweep = 0; sweep < sweepCount; sweep++)
            {
                if (attempts > 0 && generationTimer.Elapsed.TotalSeconds >= SynchronousTimeBudgetSeconds)
                {
                    int stoppedAttempt = onlyAttemptIndex >= 0 ? onlyAttemptIndex : sweep;
                    result.Report.AddFailure(stoppedAttempt,
                        GenerationFailureReason.GenerationTimeBudgetExceeded,
                        "GenerationWatchdog",
                        $"Stopped this generation pass after {generationTimer.Elapsed.TotalSeconds:F1}s and {attempts} attempts so the Unity editor remains responsive.");
                    break;
                }

                int attemptIndex = onlyAttemptIndex >= 0 ? onlyAttemptIndex : sweep;
                attempts++;

                uint attemptSeed = attemptSeedRng.NextUInt();
                if (attemptSeed == 0) attemptSeed = 1;
                var attemptRng = new Unity.Mathematics.Random(attemptSeed);

                PlanRandomStreams rngs = streams != null
                    ? new PlanRandomStreams
                    {
                        Layout = streams.AttemptRandom(SeedStream.Layout, attemptIndex),
                        Feature = streams.AttemptRandom(SeedStream.Feature, attemptIndex),
                        Elevation = streams.AttemptRandom(SeedStream.Elevation, attemptIndex),
                        Quarter = streams.AttemptRandom(SeedStream.Quarter, attemptIndex)
                    }
                    : PlanRandomStreams.FromSingle(ref attemptRng);

                TopologyPlan plan = planner.Plan(cfg, rngs, topologyOverrides,
                    options?.AuthoringElevationBaseline);

                // Elevation re-roll: a layout that closed but folds onto itself in plan
                // view is usually rescuable by a different climb/drop placement — planned
                // vertical separation is what legalizes folded legs. Re-rolling ONLY the
                // Elevation stream keeps the corner skeleton, features and quarter picks
                // (Plan runs its core from stream copies), and a plan is orders of
                // magnitude cheaper than the build the full attempt loop would spend
                // discovering a fresh layout.
                // A focused edit with an accepted elevation baseline is intentionally
                // deterministic: changing the Elevation RNG cannot change restored
                // sections, so retrying it would repeat the same plan for up to a
                // minute. Local clearance repair already owns the changed corridor.
                int elevationRerollLimit = options?.AuthoringElevationBaseline != null &&
                                           options.AuthoringElevationBaseline.Count > 0
                    ? 0
                    : cfg.DesignerAuthoringMode
                        ? AuthoringElevationRerollsPerAttempt
                        : ElevationRerollsPerAttempt;
                for (int reroll = 1;
                     plan.Failed && plan.Failure == GenerationFailureReason.SelfIntersection &&
                     generationTimer.Elapsed.TotalSeconds < SynchronousTimeBudgetSeconds &&
                     reroll <= elevationRerollLimit;
                     reroll++)
                {
                    // Deterministic retry seeds: stream indices beyond MaxAttempts can
                    // never collide with regular attempt draws; the single-seed path
                    // mixes the attempt seed with the re-roll index.
                    rngs.Elevation = streams != null
                        ? streams.AttemptRandom(SeedStream.Elevation, attemptIndex + reroll * cfg.MaxAttempts)
                        : new Unity.Mathematics.Random((attemptSeed ^ (0x9E3779B9u * (uint)reroll)) | 1u);
                    plan = planner.Plan(cfg, rngs, topologyOverrides,
                        options?.AuthoringElevationBaseline);
                }

                if (plan.Failed)
                {
                    result.Report.AddFailure(attemptIndex, plan.Failure, "TopologyPlanner", plan.FailureMessage);
                    continue;
                }

                GeneratedTrackLayout layout = builder.Build(plan, cfg);
                if (layout == null)
                {
                    result.Report.AddFailure(attemptIndex, builder.Failure, "CandidateBuilder", builder.FailureMessage);
                    continue;
                }

                List<ValidationIssue> issues = TrackValidators.RunAll(layout, plan, cfg);
                bool rejected = false;
                foreach (var issue in issues)
                {
                    if (issue.IsError)
                    {
                        result.Report.AddFailure(attemptIndex, issue.Reason, issue.Validator, issue.Message,
                            issue.RequestedValue, issue.AchievedValue, issue.Position, issue.Subject);
                        rejected = true;
                    }
                }
                if (rejected) continue;

                FillMetrics(layout, plan, cfg);

                candidates.Add(new Candidate
                {
                    Layout = layout,
                    Plan = plan,
                    Score = ScoreCandidate(layout, plan, cfg),
                    AttemptIndex = attemptIndex
                });

                if (cfg.SelectionMode == CandidateSelectionMode.FirstValid) break;
                if (candidates.Count >= cfg.CandidatesToScore) break;
            }

            result.AttemptsEvaluated = attempts;
            result.ValidCandidateCount = candidates.Count;
            result.Report.AttemptsEvaluated = attempts;
            result.Report.ValidCandidateCount = candidates.Count;
            result.Report.AngleReliefEnabled = cfg.ClosureAngleRelief;
            result.Report.AngleReliefAttempts = TrackTopologyPlanner.AngleReliefAttempts;
            result.Report.AngleReliefClosures = TrackTopologyPlanner.AngleReliefClosures;

            if (candidates.Count == 0)
            {
                result.Success = false;
                return Finish(result, generationTimer);
            }

            Candidate best = candidates[0];
            int bestCandidateIndex = 0;
            for (int i = 1; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                if (candidate.Score <= best.Score) continue;
                best = candidate;
                bestCandidateIndex = i;
            }

            best.Layout.Metrics.SelectedCandidateScore = best.Score;
            result.Layout = best.Layout;
            result.Success = true;
            result.Report.Success = true;
            result.Report.SelectedCandidateScore = best.Score;
            result.SelectedAttemptIndex = best.AttemptIndex;
            result.SelectedCandidateIndex = bestCandidateIndex;
            result.Report.SelectedAttemptIndex = best.AttemptIndex;
            result.Report.SelectedCandidateIndex = bestCandidateIndex;
            foreach (var quarter in best.Layout.Quarters)
                if (quarter.Balance != null) result.Report.QuarterBalance.Add(quarter.Balance);

            // Connector and subdivision decisions of the SELECTED candidate only,
            // plus any planning notes (dual-quarter demotions).
            result.Report.ConnectorDecisions.AddRange(best.Plan.ConnectorDecisions);
            result.Report.SubdivisionRegions.AddRange(best.Layout.SubdivisionRegions);
            result.Report.FeatureExitRecords.AddRange(best.Plan.FeatureExitRecords);
            result.Report.TopologySlots.AddRange(best.Plan.TopologySlots);
            TrackRhythmSummary selectedRhythm = TrackRhythm.AnalyzeBuiltLayout(best.Layout, cfg);
            result.Report.RhythmSummary = selectedRhythm.CompactDescription;
            result.Report.EncounterTimelines.AddRange(selectedRhythm.RouteTimelines);

            // Stage C: classify every content boundary of the SELECTED candidate from
            // its real built frames — reporting only, the dry run for Stage D.
            List<TransitionDecision> transitionDecisions = TransitionResolver.Resolve(best.Layout.Sections, cfg);
            result.Report.TransitionDecisions.AddRange(transitionDecisions);
            foreach (var decision in transitionDecisions)
                result.Report.TransitionRecords.Add(decision.ToString());
            result.Report.ConnectorShadowRecords.AddRange(
                ConnectorShadowAudit.Compare(transitionDecisions, out ConnectorShadowSummary shadowSummary));
            result.Report.ConnectorShadowSummary = shadowSummary;
            result.Report.Warnings.AddRange(best.Plan.Warnings);

            return Finish(result, generationTimer);
        }

        private static TrackGenerationResult Finish(TrackGenerationResult result, Stopwatch timer)
        {
            timer.Stop();
            result.Report.GenerationDurationSeconds = (float)timer.Elapsed.TotalSeconds;
            result.Report.AcceptedPass = result.Success ? "Strict" : "No accepted pass";
            return result;
        }

        // ─────────────────────────── Metrics ───────────────────────────

        private static void FillMetrics(GeneratedTrackLayout layout, TopologyPlan plan, ResolvedTrackGenerationConfig cfg)
        {
            var m = layout.Metrics;
            m.LapLengthMeters = layout.LapLength;
            m.TurnCount = plan.TurnCount;
            m.DualRoadQuarterCount = 0;
            foreach (var q in layout.Quarters)
                if (q.IsDual) m.DualRoadQuarterCount++;

            float minY = float.MaxValue, maxY = float.MinValue;
            float maxFacet = 0f;
            int rings = 0;

            foreach (var sec in layout.Sections)
            {
                if (sec.Definition.SemanticElement == SemanticElementId.HalfHelixTurnaround)
                {
                    m.HalfHelixTurnaroundCount++;
                }
                else
                switch (sec.Definition.SectionType)
                {
                    case TrackMacroSectionType.Loop: m.LoopCount++; break;
                    case TrackMacroSectionType.Corkscrew: m.CorkscrewCount++; break;
                    case TrackMacroSectionType.Spiral: m.SpiralCount++; break;
                    case TrackMacroSectionType.HalfLoopTwist: m.HalfLoopCount++; break;
                    case TrackMacroSectionType.RotationalEvent:
                    {
                        bool halfLoop = !string.IsNullOrEmpty(sec.PatternId) && sec.PatternId.StartsWith("HalfLoop");
                        int verticalUnits = 0, rollUnits = 0;
                        if (sec.Definition.RotationalPhases != null)
                        foreach (var phase in sec.Definition.RotationalPhases)
                        {
                            if (phase == null) continue;
                            if (phase.Axis == RotationalPhaseAxis.VerticalCenterline) verticalUnits += phase.RotationUnits;
                            else rollUnits += phase.RotationUnits;
                        }
                        if (halfLoop) m.HalfLoopCount++;
                        else if (verticalUnits > 0) m.LoopCount++;
                        if (rollUnits > 0)
                            m.CorkscrewCount += !string.IsNullOrEmpty(sec.PatternId) &&
                                                  sec.PatternId.StartsWith("DoubleCorkscrew") ? 2 : 1;
                        break;
                    }
                    case TrackMacroSectionType.JumpRamp:
                        // Structural quarter gate jumps are not designer jump features.
                        if (string.IsNullOrEmpty(sec.PatternId) || !sec.PatternId.StartsWith("Quarter_"))
                            m.JumpCount++;
                        break;
                    case TrackMacroSectionType.BankedHairpin: m.HairpinCount++; break;
                    case TrackMacroSectionType.Chicane: m.ChicaneCount++; break;
                    case TrackMacroSectionType.SCurve: m.SCurveCount++; break;
                    case TrackMacroSectionType.FullPipe: m.FullPipeCount++; break;
                    case TrackMacroSectionType.WallrideTurn: m.WallrideCount++; break;
                }

                var frames = sec.SubdivisionFrames;
                if (frames == null) continue;
                rings += frames.Length;

                for (int i = 0; i < frames.Length; i++)
                {
                    minY = Mathf.Min(minY, frames[i].Position.y);
                    maxY = Mathf.Max(maxY, frames[i].Position.y);
                    if (i > 0)
                        maxFacet = Mathf.Max(maxFacet, Vector3.Angle(frames[i - 1].Forward, frames[i].Forward));
                }
            }

            foreach (var kv in plan.PlacedPatterns)
            {
                switch (kv.Key)
                {
                    case TrackPatternType.LoopToCorkscrew:
                    case TrackPatternType.SpiralToCorkscrew:
                    case TrackPatternType.DoubleCorkscrew:
                    case TrackPatternType.HalfLoopToCorkscrew:
                    case TrackPatternType.JumpToBankedLanding:
                        m.CompoundPatternCount += kv.Value;
                        break;
                }
            }

            m.MinElevation = minY == float.MaxValue ? 0f : minY;
            m.MaxElevation = maxY == float.MinValue ? 0f : maxY;
            m.MaxFacetAngleObserved = maxFacet;
            m.TotalRings = rings;

            TrackRhythmSummary rhythm = TrackRhythm.AnalyzeBuiltLayout(layout, cfg);
            m.EncounterCount = rhythm.EncounterCount;
            m.DistinctEncounterFamilies = rhythm.DistinctFamilies;
            m.LongestEncounterFamilyStreak = rhythm.LongestRepeatedFamilyStreak;
            m.LongestSCurveFamilyStreak = rhythm.LongestSCurveStreak;
            m.MaxSCurveFamilyInWindow = rhythm.MaxSCurvesInWindow;
            m.RhythmViolationCount = rhythm.ViolationCount;
            m.RhythmScore = rhythm.Score;

            // Neutral lap time estimate over the canonical road.
            var neutral = CraftArchetypeProfile.Defaults()[0];
            float time = 0f;
            foreach (var sec in layout.Sections)
            {
                if (sec.RoadId == 1) continue;
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 3)
                {
                    // Air gaps traverse at design speed for their airtime.
                    if (sec.Definition.SectionType == TrackMacroSectionType.AirGap)
                        time += sec.Definition.AirtimeSeconds;
                    continue;
                }
                time += RouteTimeEstimator.EstimateTime(sec.SubdivisionFrames, cfg, neutral);
            }
            m.EstimatedNeutralLapTimeSeconds = time;
            layout.EstimatedNeutralLapTime = time;
        }

        // ─────────────────────────── Scoring ───────────────────────────

        /// <summary>
        /// Scores a VALID candidate (higher is better): request match (lap time, turn
        /// count, elevation), branch quality, pacing variation fit, clearance margins,
        /// ring-budget efficiency and repetition penalties.
        /// </summary>
        private static float ScoreCandidate(GeneratedTrackLayout layout, TopologyPlan plan, ResolvedTrackGenerationConfig cfg)
        {
            var m = layout.Metrics;
            float score = 100f;

            // Lap-time match (up to -30).
            float lapTimeError = Mathf.Abs(m.EstimatedNeutralLapTimeSeconds - cfg.TargetLapTimeSeconds) /
                                 Mathf.Max(1f, cfg.TargetLapTimeSeconds);
            score -= Mathf.Min(30f, lapTimeError * 60f);

            // Turn-count match to the requested window center (up to -10).
            float turnCenter = (cfg.MinTurnCount + cfg.MaxTurnCount) * 0.5f;
            score -= Mathf.Min(10f, Mathf.Abs(plan.TurnCount - turnCenter) * 1.5f);

            // Elevation match (up to -15).
            if (cfg.TargetElevationAmplitude > 5f)
            {
                float elevError = Mathf.Abs(m.ElevationRange - cfg.TargetElevationAmplitude) / cfg.TargetElevationAmplitude;
                score -= Mathf.Min(15f, elevError * 20f);
            }

            // Dual-quarter quality (up to -12): tight balance and real archetype
            // differentiation score well; demotions cost a little.
            foreach (var quarter in layout.Quarters)
            {
                if (quarter.Balance == null) continue;
                score -= Mathf.Min(6f, quarter.Balance.NeutralTimeDifferencePercent /
                                       Mathf.Max(0.1f, cfg.NeutralTimeTolerance * 100f) * 3f);
                if (!(quarter.Balance.RouteAPreferredBySomeArchetype && quarter.Balance.RouteBPreferredBySomeArchetype))
                    score -= 4f;
            }
            score -= Mathf.Min(6f, plan.Warnings.Count * 2f);

            // Feature richness toward the requested group window (up to -8).
            int featureGroups = m.LoopCount + m.CorkscrewCount + m.SpiralCount +
                                m.HalfHelixTurnaroundCount + m.HalfLoopCount + m.JumpCount;
            if (featureGroups < cfg.MinFeatureGroups)
                score -= (cfg.MinFeatureGroups - featureGroups) * 4f;

            // Ring-budget efficiency (up to -8).
            float ringUse = (float)m.TotalRings / Mathf.Max(1, cfg.MaxTotalRings);
            if (ringUse > 0.85f) score -= (ringUse - 0.85f) * 50f;

            // Facet quality (up to -6).
            if (m.MaxFacetAngleObserved > cfg.MaxRingFacetAngle)
                score -= Mathf.Min(6f, (m.MaxFacetAngleObserved - cfg.MaxRingFacetAngle) * 4f);

            // Repetition penalty: three identical consecutive corner magnitudes read as lazy.
            int repeats = 0;
            var sections = layout.Sections;
            for (int i = 2; i < sections.Count; i++)
            {
                var a = sections[i - 2].Definition;
                var b = sections[i - 1].Definition;
                var c = sections[i].Definition;
                if (a.SectionType == TrackMacroSectionType.BankedCurve &&
                    b.SectionType == TrackMacroSectionType.BankedCurve &&
                    c.SectionType == TrackMacroSectionType.BankedCurve &&
                    Mathf.Approximately(a.TurnAngle, b.TurnAngle) && Mathf.Approximately(b.TurnAngle, c.TurnAngle))
                    repeats++;
            }
            score -= Mathf.Min(5f, repeats * 2f);

            // Encounter-level pacing: unlike the legacy raw-angle check above, this
            // understands compounds and semantic feature families. Exact Track Editor
            // authoring remains unrestricted and therefore receives no procedural score.
            if (cfg.EnforceProceduralRhythm && !cfg.DesignerAuthoringMode)
            {
                score += (m.RhythmScore - 80f) * 0.25f;
                score -= m.RhythmViolationCount * 18f;
                score += Mathf.Min(5f, m.DistinctEncounterFamilies * 0.6f);
            }

            return score;
        }
    }
}
