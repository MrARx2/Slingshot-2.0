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
        public TrackGenerationResult Run(ResolvedTrackGenerationConfig cfg, TrackSeed seed, TrackSeedStreams streams = null)
        {
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
                return result;
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

            int attempts = 0;
            var generationTimer = Stopwatch.StartNew();
            for (; attempts < cfg.MaxAttempts; attempts++)
            {
                if (attempts > 0 && generationTimer.Elapsed.TotalSeconds >= SynchronousTimeBudgetSeconds)
                {
                    result.Report.AddFailure(attempts,
                        GenerationFailureReason.GenerationTimeBudgetExceeded,
                        "GenerationWatchdog",
                        $"Stopped this generation pass after {generationTimer.Elapsed.TotalSeconds:F1}s and {attempts} attempts so the Unity editor remains responsive.");
                    break;
                }

                uint attemptSeed = attemptSeedRng.NextUInt();
                if (attemptSeed == 0) attemptSeed = 1;
                var attemptRng = new Unity.Mathematics.Random(attemptSeed);

                PlanRandomStreams rngs = streams != null
                    ? new PlanRandomStreams
                    {
                        Layout = streams.AttemptRandom(SeedStream.Layout, attempts),
                        Feature = streams.AttemptRandom(SeedStream.Feature, attempts),
                        Elevation = streams.AttemptRandom(SeedStream.Elevation, attempts),
                        Quarter = streams.AttemptRandom(SeedStream.Quarter, attempts)
                    }
                    : PlanRandomStreams.FromSingle(ref attemptRng);

                TopologyPlan plan = planner.Plan(cfg, rngs);

                // Elevation re-roll: a layout that closed but folds onto itself in plan
                // view is usually rescuable by a different climb/drop placement — planned
                // vertical separation is what legalizes folded legs. Re-rolling ONLY the
                // Elevation stream keeps the corner skeleton, features and quarter picks
                // (Plan runs its core from stream copies), and a plan is orders of
                // magnitude cheaper than the build the full attempt loop would spend
                // discovering a fresh layout.
                for (int reroll = 1;
                     plan.Failed && plan.Failure == GenerationFailureReason.SelfIntersection &&
                     generationTimer.Elapsed.TotalSeconds < SynchronousTimeBudgetSeconds &&
                     reroll <= ElevationRerollsPerAttempt;
                     reroll++)
                {
                    // Deterministic retry seeds: stream indices beyond MaxAttempts can
                    // never collide with regular attempt draws; the single-seed path
                    // mixes the attempt seed with the re-roll index.
                    rngs.Elevation = streams != null
                        ? streams.AttemptRandom(SeedStream.Elevation, attempts + reroll * cfg.MaxAttempts)
                        : new Unity.Mathematics.Random((attemptSeed ^ (0x9E3779B9u * (uint)reroll)) | 1u);
                    plan = planner.Plan(cfg, rngs);
                }

                if (plan.Failed)
                {
                    result.Report.AddFailure(attempts, plan.Failure, "TopologyPlanner", plan.FailureMessage);
                    continue;
                }

                GeneratedTrackLayout layout = builder.Build(plan, cfg);
                if (layout == null)
                {
                    result.Report.AddFailure(attempts, builder.Failure, "CandidateBuilder", builder.FailureMessage);
                    continue;
                }

                List<ValidationIssue> issues = TrackValidators.RunAll(layout, plan, cfg);
                bool rejected = false;
                foreach (var issue in issues)
                {
                    if (issue.IsError)
                    {
                        result.Report.AddFailure(attempts, issue.Reason, issue.Validator, issue.Message,
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
                    AttemptIndex = attempts
                });

                if (cfg.SelectionMode == CandidateSelectionMode.FirstValid) { attempts++; break; }
                if (candidates.Count >= cfg.CandidatesToScore) { attempts++; break; }
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
                return result;
            }

            Candidate best = candidates[0];
            foreach (var c in candidates)
                if (c.Score > best.Score) best = c;

            best.Layout.Metrics.SelectedCandidateScore = best.Score;
            result.Layout = best.Layout;
            result.Success = true;
            result.Report.Success = true;
            result.Report.SelectedCandidateScore = best.Score;
            foreach (var quarter in best.Layout.Quarters)
                if (quarter.Balance != null) result.Report.QuarterBalance.Add(quarter.Balance);

            // Connector and subdivision decisions of the SELECTED candidate only,
            // plus any planning notes (dual-quarter demotions).
            result.Report.ConnectorDecisions.AddRange(best.Plan.ConnectorDecisions);
            result.Report.SubdivisionRegions.AddRange(best.Layout.SubdivisionRegions);
            result.Report.FeatureExitRecords.AddRange(best.Plan.FeatureExitRecords);

            // Stage C: classify every content boundary of the SELECTED candidate from
            // its real built frames — reporting only, the dry run for Stage D.
            foreach (var decision in TransitionResolver.Resolve(best.Layout.Sections, cfg))
                result.Report.TransitionRecords.Add(decision.ToString());
            result.Report.Warnings.AddRange(best.Plan.Warnings);

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
            int featureGroups = m.LoopCount + m.CorkscrewCount + m.SpiralCount + m.HalfLoopCount + m.JumpCount;
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

            return score;
        }
    }
}
