using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Definitions;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>The full plan of one candidate: definitions plus bookkeeping for validation.</summary>
    public class TopologyPlan
    {
        public List<TrackMacroSectionDefinition> Defs = new List<TrackMacroSectionDefinition>();

        /// <summary>The 4 quarters of the lap (always exactly 4 after partition).</summary>
        public List<PlannedQuarter> Quarters = new List<PlannedQuarter>();

        public Dictionary<TrackPatternType, int> PlacedPatterns = new Dictionary<TrackPatternType, int>();
        public List<ConnectorDecisionRecord> ConnectorDecisions = new List<ConnectorDecisionRecord>();
        public int TurnCount;

        /// <summary>
        /// Corkscrew slots consumed by optional corner realizations before ordinary
        /// gap features are selected. It becomes a placed count only after emission.
        /// </summary>
        public int ReservedCornerCorkscrews;

        /// <summary>Non-fatal planning notes (dual-quarter demotions, infeasible selections).</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// Stage A: exact measured plan results for every emitted feature group
        /// (consecutive defs sharing a PatternId), built with the same section
        /// builders the candidate uses. Populated only for successful plans.
        /// </summary>
        public List<FeaturePlanResult> FeatureResults = new List<FeaturePlanResult>();

        /// <summary>Stage A: formatted result lines for the serialized report.</summary>
        public List<string> FeatureExitRecords = new List<string>();

        /// <summary>
        /// V2 stable gameplay-demand identities. Reporting-only until the interactive
        /// replacement stage consumes them transactionally.
        /// </summary>
        public List<TopologySlotRecord> TopologySlots = new List<TopologySlotRecord>();

        public GenerationFailureReason Failure = GenerationFailureReason.None;
        public string FailureMessage = "";

        public bool Failed => Failure != GenerationFailureReason.None;

        public void Fail(GenerationFailureReason reason, string message)
        {
            Failure = reason;
            FailureMessage = message;
        }

        public void CountPattern(TrackPatternType type)
        {
            PlacedPatterns.TryGetValue(type, out int c);
            PlacedPatterns[type] = c + 1;
        }

        public int DualQuarterCount
        {
            get
            {
                int c = 0;
                foreach (var q in Quarters) if (q.Dual) c++;
                return c;
            }
        }
    }

    /// <summary>
    /// Plans the topology of one candidate lap: the corner plan (turn families,
    /// direction pattern, intentional corner sequences), required-then-optional feature
    /// reservation, branch group reservation, pacing straights, the closure reserve,
    /// the 2D closure solve and the elevation plan. Pure data — no frames, no meshes.
    /// </summary>
    public class TrackTopologyPlanner
    {
        private const float MinAdjustableStraight = 40f;
        private const int MaxClosureRescueRounds = 12;
        private const int MaxAlternateDualPlacementsPerCandidate = 1;

        // ─────────────────────────── Corner slot model ───────────────────────────

        private class CornerSlot
        {
            public int SignedAngle;                       // degrees, sign = direction
            public TrackPatternType Realization = TrackPatternType.Hairpin; // meaningful only when special
            public bool IsHalfLoop;                       // realized as a half-loop pattern (vertical reversal)
            public TrackPatternType HalfLoopType;
            public bool IsSpecial;                        // corner pattern realization (double apex, …)
            public bool ForceOrdinary;                    // replacement preview may explicitly request an ordinary 150° curve
            public SemanticElementId DefinitionRealization; // definition-native turn; None for legacy corner emitters
            public bool AllowHairpinMagnitude;            // may reach 150–180° (counts against the hairpin rule)
            public int Magnitude => Mathf.Abs(SignedAngle);
            public int Sign => SignedAngle >= 0 ? 1 : -1;
        }

        /// <summary>Legacy single-RNG entry: forks the independent streams from one attempt RNG.</summary>
        public TopologyPlan Plan(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng)
            => Plan(cfg, PlanRandomStreams.FromSingle(ref rng), null);

        /// <summary>
        /// Plans one candidate. Returns a plan whose Failure explains any rejection.
        /// Every subsystem draws ONLY from its own stream, so re-randomizing one
        /// stream (features, elevation, quarter content) never perturbs the others.
        ///
        /// The deterministic planning core first uses the requested random selection.
        /// If that selection cannot close or its alternate road cannot fit, the planner
        /// tries the other legal quarter placements from the same random-stream state.
        /// This relocates a REQUIRED dual quarter instead of silently deleting it.
        /// </summary>
        public TopologyPlan Plan(
            ResolvedTrackGenerationConfig cfg,
            PlanRandomStreams rngs,
            IReadOnlyList<TopologySlotOverride> topologyOverrides = null,
            IReadOnlyList<AuthoringElevationBaselineEntry> authoringElevationBaseline = null)
        {
            TopologyPlan initial = PlanCore(cfg, rngs.Copy(), null, topologyOverrides,
                authoringElevationBaseline, out _);
            if (MeetsDualRequirement(initial, cfg)) return initial;

            // Re-running a whole candidate is expensive: it includes closure,
            // elevation and Road B fitting. Only retry when the candidate reached the
            // dual-road stage and that requirement itself failed. Closure, transition,
            // intersection and budget failures need a new candidate, not another copy
            // of the same one with up to sixteen quarter masks.
            bool dualSpecificFailure = !initial.Failed
                ? initial.DualQuarterCount < cfg.MinDualQuarters
                : initial.Failure == GenerationFailureReason.DualRoadFitFailure ||
                  (initial.Failure == GenerationFailureReason.DualRoadBalanceFailure &&
                   cfg.BalancePolicy == DualQuarterBalancePolicy.DemoteToSingleRoad);

            if (dualSpecificFailure && initial.Quarters.Count == 4)
            {
                int forcedMask = 0;
                int forbiddenMask = 0;
                int initialMask = 0;
                for (int q = 0; q < 4; q++)
                {
                    if (initial.Quarters[q].Dual) initialMask |= 1 << q;
                    if (cfg.QuarterTypeOverrides[q] == QuarterTypeOverride.DualRoad)
                        forcedMask |= 1 << q;
                    else if (cfg.QuarterTypeOverrides[q] == QuarterTypeOverride.SingleRoad)
                        forbiddenMask |= 1 << q;
                }
                if (!cfg.AllowQ1Dual) forbiddenMask |= 1;
                if (!cfg.AllowQ4Dual) forbiddenMask |= 1 << 3;

                int minimumMaskCount = Mathf.Max(cfg.MinDualQuarters, CountMaskBits(forcedMask));
                int alternatesTried = 0;
                for (int mask = 0; mask < 16; mask++)
                {
                    if (CountMaskBits(mask) != minimumMaskCount) continue;
                    if (mask == initialMask) continue;
                    if ((mask & forcedMask) != forcedMask || (mask & forbiddenMask) != 0) continue;

                    var explicitMask = new bool[4];
                    for (int q = 0; q < 4; q++) explicitMask[q] = (mask & (1 << q)) != 0;
                    TopologyPlan alternate = PlanCore(
                        cfg, rngs.Copy(), explicitMask, topologyOverrides,
                        authoringElevationBaseline, out _);
                    alternatesTried++;
                    if (MeetsDualRequirement(alternate, cfg))
                    {
                        alternate.Warnings.Insert(0,
                            $"Required Dual Road Quarter fallback selected mask {MaskLabel(mask)} after the initial placement failed.");
                        return alternate;
                    }
                    if (alternatesTried >= MaxAlternateDualPlacementsPerCandidate) break;
                }
            }

            if (!initial.Failed && initial.DualQuarterCount < cfg.MinDualQuarters)
            {
                string detail = null;
                for (int w = initial.Warnings.Count - 1; w >= 0 && detail == null; w--)
                    if (initial.Warnings[w].StartsWith("Dual selection") ||
                        initial.Warnings[w].Contains("demoted to SingleRoad"))
                        detail = initial.Warnings[w];
                initial.Fail(GenerationFailureReason.RequiredFeatureMissing,
                    $"Only {initial.DualQuarterCount} of the required {cfg.MinDualQuarters} Dual Road Quarters could be built " +
                    $"after the bounded alternate-placement retry ({detail ?? "no eligible quarters"}).");
            }

            return initial;
        }

        private static bool MeetsDualRequirement(TopologyPlan plan, ResolvedTrackGenerationConfig cfg)
            => !plan.Failed && plan.DualQuarterCount >= cfg.MinDualQuarters;

        private static int CountMaskBits(int mask)
        {
            int count = 0;
            for (; mask != 0; mask >>= 1) count += mask & 1;
            return count;
        }

        private static string MaskLabel(int mask)
        {
            var quarters = new List<string>(4);
            for (int q = 0; q < 4; q++)
                if ((mask & (1 << q)) != 0) quarters.Add($"Q{q + 1}");
            return quarters.Count == 0 ? "SingleRoad-only" : string.Join("+", quarters);
        }

        /// <summary>
        /// The deterministic planning core. <paramref name="dualMask"/> null = the
        /// Quarter stream selects which quarters are dual; non-null = exactly the masked
        /// quarters are dual (demotion re-runs). <paramref name="failedQuarter"/> is the
        /// quarter whose alternate road failed, -1 for non-quarter failures.
        /// </summary>
        private TopologyPlan PlanCore(
            ResolvedTrackGenerationConfig cfg,
            PlanRandomStreams rngs,
            bool[] dualMask,
            IReadOnlyList<TopologySlotOverride> topologyOverrides,
            IReadOnlyList<AuthoringElevationBaselineEntry> authoringElevationBaseline,
            out int failedQuarter)
        {
            failedQuarter = -1;
            var plan = new TopologyPlan();

            // ── 1. Corner plan (skeleton + realizations, quarter-agnostic) ──
            List<CornerSlot> corners = PlanCorners(cfg, plan, rngs);
            if (plan.Failed) return plan;
            plan.TurnCount = corners.Count;

            int gapCount = corners.Count;
            int closureGaps = Mathf.Clamp(Mathf.CeilToInt(gapCount * cfg.ClosureReserveFraction), 2, gapCount - 1);
            int usableGaps = gapCount - closureGaps;

            // ── 2. Quarter partition + dual selection (Quarter stream) ──
            PartitionQuarters(cfg, plan, corners, dualMask, ref rngs.Quarter);
            if (plan.Failed) return plan;

            // ── 3. Feature instances (required first, then optional) ──
            // Length budget: what remains of the cap after the corner arcs, the minimum
            // straights and the dual-quarter gate overhead. Optional content stops when
            // the budget runs out instead of blowing the cap and failing later.
            float cornerArcEstimate = 0f;
            float cornerRealizationExtra = 0f;
            foreach (var c in corners)
            {
                float radius = Mathf.Max(Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, c.Magnitude / 180f), cfg.MinCurveRadius);
                float drawnArc = SectionFrameBuilders.EasedArcLength(c.Magnitude, radius);
                float minArc = SectionFrameBuilders.EasedArcLength(c.Magnitude, cfg.MinCurveRadius);
                // The closure budget pass can reclaim corner arc by shrinking radii
                // toward the minimum (it counts 80% of that slack as usable) — charge
                // features only 35% of the drawn surplus, or feature-heavy presets fail
                // the budget check for length the solver would happily have found.
                float chargedArc = Mathf.Lerp(drawnArc, minArc, 0.65f);
                cornerArcEstimate += chargedArc;

                // Long, locked corner realizations replace an ordinary arc. Charge
                // their positive difference here so they cannot bypass the feature
                // budget while still avoiding double-counting the base corner.
                float realized = 0f;
                if (c.IsHalfLoop)
                    realized = cfg.EstimateFeatureFootprint(c.HalfLoopType);
                else if (c.IsSpecial && c.Realization == TrackPatternType.Corkscrew)
                    realized = cfg.EstimateFeatureFootprint(TrackPatternType.Corkscrew) +
                               cfg.DefaultRecoveryLength;
                else if (c.IsSpecial && c.Realization == TrackPatternType.WallrideTurn)
                    realized = drawnArc + cfg.DefaultRecoveryLength;
                else if (c.DefinitionRealization != SemanticElementId.None)
                    realized = cfg.EstimateFeatureFootprint(c.Realization);
                else if (c.IsSpecial && (c.Realization == TrackPatternType.Hairpin ||
                                         c.Realization == TrackPatternType.SweeperIntoHairpin))
                    realized = cfg.EstimateFeatureFootprint(c.Realization);
                cornerRealizationExtra += Mathf.Max(0f, realized - chargedArc);
            }
            // Gate overhead reserves the complete launch/airtime/landing capture
            // envelope. This is deliberately the same allowance the builder may use,
            // so a plan cannot become over-length merely by solving its landings.
            float gateOverhead = QuarterGateOverhead(cfg);
            float quarterEstimate = plan.DualQuarterCount * gateOverhead;
            float straightsEstimate = gapCount * Mathf.Max(MinAdjustableStraight, cfg.MinStraightLength * 0.6f);
            // Closure is not optional content. Protect its configured share before
            // admitting optional features so a visually rich open path does not arrive
            // at the final solve with no remaining distance in which to return home.
            float closureLengthReserve = cfg.MaxTrackLength * Mathf.Max(0.15f, cfg.ClosureReserveFraction);
            float featureBudget = cfg.MaxTrackLength - closureLengthReserve -
                                  cornerArcEstimate - cornerRealizationExtra -
                                  quarterEstimate - straightsEstimate;

            // Road A inside a dual quarter may carry ordinary feature patterns. Road B
            // is fitted independently and the completed pair still has to pass timing,
            // clearance, and feature validation.
            List<TrackPatternType> gapFeatures = PlanGapFeatures(cfg, plan, usableGaps, featureBudget, ref rngs.Feature);
            if (plan.Failed) return plan;

            var featureByGap = new Dictionary<int, TrackPatternType>();
            if (cfg.EnforceProceduralRhythm)
            {
                List<TrackPatternType> encounterOrder = TrackRhythm.ArrangeFeatureSequence(
                    gapFeatures, cfg, ref rngs.Layout, out int rhythmPenalty);
                List<int> selectedGaps = SelectRhythmicFeatureGaps(
                    usableGaps, gapCount, encounterOrder.Count, plan, ref rngs.Layout);
                if (selectedGaps.Count < encounterOrder.Count)
                {
                    plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                        $"{encounterOrder.Count} feature groups need more free gaps than remain between {gapCount} corners " +
                        $"(closure reserves {closureGaps}).");
                    return plan;
                }
                for (int i = 0; i < encounterOrder.Count; i++)
                    featureByGap[selectedGaps[i]] = encounterOrder[i];

                // Explicit required quantities always win. Warn when their multiset is
                // impossible to diversify instead of rejecting an otherwise valid lap.
                if (rhythmPenalty > 0)
                    plan.Warnings.Add("[Rhythm] Explicit feature quantities cannot fully satisfy the current " +
                                      "spacing rules; every requested feature was kept and spread as widely as possible.");
            }
            else
            {
                // Legacy placement path retained behind the toggle for exact comparisons.
                var gapOrder = new List<int>();
                for (int i = 0; i < usableGaps; i++) gapOrder.Add(i);
                Shuffle(gapOrder, ref rngs.Layout);
                for (int i = 0; i < gapFeatures.Count; i++)
                    featureByGap[gapOrder[i]] = gapFeatures[i];
            }

            // ── 4. Emit definitions gap-by-gap, corner-by-corner, with quarter gates ──
            EmitDefinitions(cfg, plan, corners, featureByGap, closureGaps, rngs);
            if (plan.Failed) return plan;

            // Exact Recipe V2 edits are authored against stable gameplay slots, not
            // section indices. Apply them before connector analysis and closure so the
            // normal whole-route solver owns every bridge, recovery and downstream
            // adjustment. A failed edited candidate is rejected transactionally by the
            // pipeline; the currently accepted scene track is never spliced in place.
            ApplyTopologyOverrides(cfg, plan, topologyOverrides);
            if (plan.Failed) return plan;

            // ── 4b. Connector analysis: classify straights between content, absorb
            // same-direction bridges into turn complexes, lift tiny connectors to
            // their blend requirement (a solver constraint, never a warp) ──
            ConnectorAnalyzer.Analyze(plan, cfg);

            // ── 5. 2D closure solve on adjustable straights ──
            SolveClosure2D(cfg, plan);
            if (plan.Failed) return plan;

            // ── 6. Elevation plan (BEFORE the proximity check: planned climbs are what
            // legally separate folded mountain-pass legs) ──
            int restoredElevationSections = cfg.DesignerAuthoringMode
                ? RestoreAuthoringElevationBaseline(plan.Defs, authoringElevationBaseline)
                : 0;
            if (restoredElevationSections == 0)
                PlanElevation(cfg, plan, ref rngs.Elevation);
            else
                plan.Warnings.Add($"Track Editor preserved the accepted elevation plan on " +
                                  $"{restoredElevationSections} unchanged route sections.");
            if (plan.Failed) return plan;

            // Removing an Area-of-Impact neighbour can also remove elevation that was
            // balancing the accepted lap. PlanElevation owns newly selected majors,
            // but an exact-recipe route may carry authored deltas on other definitions.
            // Reconcile the complete canonical definition stream before Road B is
            // fitted or geometry is built; this avoids discovering a pure Y closure
            // residual only after an otherwise-valid focused candidate is complete.
            if (cfg.DesignerAuthoringMode && restoredElevationSections == 0)
            {
                float elevationResidual = ReconcileAuthoringNetElevation(
                    cfg, plan.Defs, out float balancedElevation);
                if (Mathf.Abs(balancedElevation) > 0.01f)
                    plan.Warnings.Add($"Track Editor balanced {Mathf.Abs(balancedElevation):F1}m " +
                                      "of Area-of-Impact lap elevation on safe ordinary roads.");
                if (Mathf.Abs(elevationResidual) > 0.5f)
                {
                    plan.Fail(GenerationFailureReason.ClosureElevationFailure,
                        $"Area-of-Impact left {elevationResidual:F1}m of canonical lap elevation " +
                        "that cannot fit on the remaining safe roads.");
                    return plan;
                }
            }

            // ── 6b. Quarter-index stamping (closure/elevation inserted defs inherit) ──
            StampQuarterIndices(plan);

            // ── 6c. Alternate roads: fit road B of every dual quarter between its
            // ballistic gate frames (post-closure, post-elevation — final geometry) ──
            foreach (var q in plan.Quarters)
            {
                if (!q.Dual) continue;
                var fit = QuarterRoadFitter.Fit(cfg, plan, q, ref rngs.Quarter);
                if (!fit.Success)
                {
                    failedQuarter = q.Index;
                    plan.Fail(fit.BalanceFailure
                            ? GenerationFailureReason.DualRoadBalanceFailure
                            : GenerationFailureReason.DualRoadFitFailure,
                        $"Quarter {q.Index} road B: {fit.FailureMessage}");
                    return plan;
                }
            }

            // A focused edit is free to reshape its replacement and confirmed AOI,
            // but unchanged accepted features are altitude anchors. Reconcile against
            // actual frame-builder exits (not only Definition.ElevationChange):
            // rotational events and landing geometry can have authoritative vertical
            // displacement that is not represented by a single scalar definition.
            if (cfg.DesignerAuthoringMode && restoredElevationSections > 0)
            {
                float anchorResidual = ReconcileAuthoringElevationForEditor(
                    cfg, plan.Defs, authoringElevationBaseline,
                    out float anchoredElevation, out bool relaxedAcceptedLayers);
                if (Mathf.Abs(anchoredElevation) > 0.01f)
                    plan.Warnings.Add($"Track Editor reconnected {Mathf.Abs(anchoredElevation):F1}m " +
                                      "of vertical change between accepted altitude anchors.");
                if (relaxedAcceptedLayers)
                    plan.Warnings.Add("Track Editor kept the requested feature by adapting legal ordinary/recovery road " +
                                      "outside the exact accepted altitude layers. Feature identity and all final safety checks remain protected.");
                if (Mathf.Abs(anchorResidual) > 0.5f)
                {
                    plan.Fail(GenerationFailureReason.ClosureElevationFailure,
                        $"The edited route still needs {Mathf.Abs(anchorResidual):F1}m more legal vertical " +
                        "recovery after the Track Editor used every safe ordinary/recovery road available to it.");
                    return plan;
                }
            }

            if (cfg.DesignerAuthoringMode && restoredElevationSections == 0)
            {
                float builtResidual = ReconcileAuthoringBuiltElevation(
                    cfg, plan.Defs, out float builtBalance);
                if (Mathf.Abs(builtBalance) > 0.01f)
                    plan.Warnings.Add($"Track Editor balanced {Mathf.Abs(builtBalance):F1}m " +
                                      "of measured geometry elevation before closure.");
                if (Mathf.Abs(builtResidual) > 0.5f)
                {
                    plan.Fail(GenerationFailureReason.ClosureElevationFailure,
                        $"The rebuilt geometry retains {builtResidual:F1}m of vertical closure " +
                        "error after all legal authoring recovery roads were used.");
                    return plan;
                }
            }

            // ── 7. Cheap elevation-aware 2D self-proximity check (canonical road) ──
            // In designer mode, keep the authored geometry and use the level-welded
            // approach/recovery roads around an existing feature as a vertical bridge.
            // This is deterministic, preserves the lap endpoint and never weakens the
            // final 3D validator. A bounded pass per plausible route section allows one
            // repair to reveal the next genuinely unrelated crossing. Three passes were
            // too small for designer edits on feature-rich laps: the solver was stopping
            // after fixing three real crossings and reporting the fourth.
            PlanProximityCollision collisionDetails;
            string collision = Validate2DWalk(cfg, plan, out collisionDetails);
            if (cfg.DesignerAuthoringMode)
            {
                // A dense authored lap can expose a chain of independent crossings as
                // each earlier one is lifted. Spend up to one repair per two route
                // definitions (bounded at 24) before declaring the designer request
                // impossible; procedural generation never pays this authoring cost.
                int maximumClearanceRepairs = Mathf.Clamp(plan.Defs.Count / 2, 8, 24);
                for (int rescue = 0; collision != null && collisionDetails != null &&
                     rescue < maximumClearanceRepairs; rescue++)
                {
                    bool repaired = TryApplyAuthoringVerticalClearance(cfg, plan.Defs,
                            collisionDetails.FirstOwner, collisionDetails.FirstHeight,
                            collisionDetails.SecondOwner, collisionDetails.SecondHeight,
                            out string repairNote);
                    if (!repaired)
                        repaired = TryApplyAuthoringRouteClearance(cfg, plan.Defs,
                            collisionDetails.FirstOwner, collisionDetails.FirstHeight,
                            collisionDetails.SecondOwner, collisionDetails.SecondHeight,
                            out repairNote);
                    if (!repaired)
                        break;

                    plan.Warnings.Add(repairNote);
                    collision = Validate2DWalk(cfg, plan, out collisionDetails);
                }
            }
            if (collision != null)
            {
                plan.Fail(GenerationFailureReason.SelfIntersection,
                    $"Plan-view walk brings unrelated track segments closer than the safe corridor without vertical separation: {collision}.");
                return plan;
            }

            // ── 8. V2: assign stable gameplay-demand identities only after both
            // routes and every inserted definition have reached their final order.
            // Reporting only — no planning or geometry consumer reads these yet.
            plan.TopologySlots = TopologySlotCatalog.Assign(plan.Defs);
            MarkAppliedTopologyOverrides(plan, topologyOverrides);
            if (plan.Failed) return plan;

            // ── 9. Stage A: exact measured plan results for every feature group.
            // Runs only on plans that survived every gate above (≈ candidates), so the
            // extra frame builds cost a bounded handful per generation, not per attempt.
            ComputeFeatureResults(cfg, plan);

            return plan;
        }

        private static void ApplyTopologyOverrides(
            ResolvedTrackGenerationConfig cfg,
            TopologyPlan plan,
            IReadOnlyList<TopologySlotOverride> overrides)
        {
            if (overrides == null || overrides.Count == 0) return;

            plan.TopologySlots = TopologySlotCatalog.Assign(plan.Defs);
            var work = new List<(TopologySlotOverride Request, TopologySlotRecord Slot,
                int WindowFirst, int WindowLast, List<TopologySlotRecord> Impacted)>();
            for (int i = 0; i < overrides.Count; i++)
            {
                TopologySlotOverride request = overrides[i];
                if (request == null || string.IsNullOrWhiteSpace(request.TopologySlotId)) continue;

                if (!TopologySlotCatalog.TryResolveOverride(plan.TopologySlots, request,
                        out TopologySlotRecord slot, out string resolutionFailure))
                {
                    plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                        resolutionFailure + " " +
                        "The previous valid track was kept.");
                    return;
                }

                if (!string.Equals(slot.TopologySlotId, request.TopologySlotId,
                        System.StringComparison.Ordinal))
                {
                    plan.Warnings.Add($"Track Editor structurally resolved {request.TopologySlotId} " +
                                      $"to pre-connector slot {slot.TopologySlotId}.");
                }

                FeatureCapability capability = FeatureCapabilities.Get(request.RequestedRealization);
                if (request.RequestedRealization == SemanticElementId.None || capability == null ||
                    capability.Role != slot.DemandType)
                {
                    plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                        $"{request.RequestedRealization} cannot satisfy the {slot.DemandType} demand of " +
                        $"edited segment '{slot.TopologySlotId}'. The previous valid track was kept.");
                    return;
                }

                if (!TryResolveAuthoringImpactWindow(plan.TopologySlots, request, slot,
                        out int windowFirst, out int windowLast,
                        out List<TopologySlotRecord> impacted, out string impactFailure))
                {
                    plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                        impactFailure + " The previous valid track was kept.");
                    return;
                }

                work.Add((request, slot, windowFirst, windowLast, impacted));
            }

            // Two simultaneous authoring windows must never consume each other's
            // definition ownership. The inspector applies one change at a time, while
            // this guard keeps imported or hand-edited recipes transactional.
            for (int i = 0; i < work.Count; i++)
            for (int j = i + 1; j < work.Count; j++)
            {
                if (work[i].WindowFirst > work[j].WindowLast ||
                    work[j].WindowFirst > work[i].WindowLast)
                    continue;
                plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                    $"Track Editor impact windows for '{work[i].Slot.TopologySlotId}' and " +
                    $"'{work[j].Slot.TopologySlotId}' overlap. Apply those changes separately.");
                return;
            }

            // Work backwards so replacing an earlier group cannot invalidate the
            // definition indices captured for a later group.
            work.Sort((a, b) => b.WindowFirst.CompareTo(a.WindowFirst));
            for (int i = 0; i < work.Count; i++)
            {
                TopologySlotOverride request = work[i].Request;
                TopologySlotRecord slot = work[i].Slot;
                int first = work[i].WindowFirst;
                int count = work[i].WindowLast - first + 1;
                if (first < 0 || count <= 0 || first + count > plan.Defs.Count)
                {
                    plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                        $"Edited segment '{slot.TopologySlotId}' has stale ownership data. " +
                        "The previous valid track was kept.");
                    return;
                }

                float width = Mathf.Max(1f, plan.Defs[first]?.Width ?? cfg.RoadWidth);
                FeaturePlanResult candidate = BuildReplacementCandidate(
                    cfg, slot, request.RequestedRealization, TrackConnectionFrame.Origin(width));
                if (candidate == null || candidate.Failed || candidate.Definitions == null ||
                    candidate.Definitions.Count == 0)
                {
                    string reason = candidate?.FailureReason;
                    plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                        $"{request.RequestedRealization} could not be built for edited segment " +
                        $"'{slot.TopologySlotId}': {reason ?? "no geometry was produced"}. " +
                        "The previous valid track was kept.");
                    return;
                }

                bool hasImpact = work[i].Impacted.Count > 0;
                bool consumedSupersededRecovery = false;
                if (!hasImpact)
                {
                    int ownedCount = ComputeReplacementRemovalCount(
                        plan.Defs, slot, candidate.Definitions, request.MaximumReplanScope);
                    consumedSupersededRecovery = ownedCount > count;
                    count = ownedCount;
                }

                float authoredRecoveryLength = 0f;
                float authoredRecoveryElevation = 0f;
                if (cfg.DesignerAuthoringMode)
                    EnsureAuthoringVerticalRecovery(cfg, candidate.Definitions,
                        out authoredRecoveryLength, out authoredRecoveryElevation);

                // Carry an exact, generation-local identity through Area of Impact.
                // Removing a feature after the selection can legitimately renumber or
                // even re-quarter the finished route slot; the replacement geometry
                // itself is the authoritative identity in that case.
                string appliedPatternId = AppliedReplacementPatternId(request);

                for (int d = 0; d < candidate.Definitions.Count; d++)
                {
                    TrackMacroSectionDefinition definition = candidate.Definitions[d];
                    if (definition == null) continue;
                    definition.QuarterIndex = slot.QuarterIndex;
                    definition.RoadId = slot.RoadId;
                    definition.TopologySlotId = "";
                    definition.TopologySlotOrder = -1;
                    definition.TopologySlotRouteOrder = -1;
                    definition.PatternId = appliedPatternId;
                }

                plan.Defs.RemoveRange(first, count);
                plan.Defs.InsertRange(first, candidate.Definitions);
                if (hasImpact)
                {
                    var removedNames = new List<string>();
                    for (int affected = 0; affected < work[i].Impacted.Count; affected++)
                        removedNames.Add(work[i].Impacted[affected].CurrentRealization.ToString());
                    plan.Warnings.Add($"Track Editor Area of Impact removed " +
                                      $"{string.Join(", ", removedNames)} around {slot.TopologySlotId}; " +
                                      "the route and mesh were rebuilt across the authorized window.");
                }
                if (consumedSupersededRecovery)
                    plan.Warnings.Add($"Track Editor consumed the superseded recovery after " +
                                      $"{slot.TopologySlotId}; the replacement owns its recovery.");
                if (authoredRecoveryLength > 0f)
                    plan.Warnings.Add(
                        $"Track Editor added {authoredRecoveryLength:F0}m of owned recovery road " +
                        $"to return {Mathf.Abs(authoredRecoveryElevation):F1}m to grade after " +
                        $"{request.RequestedRealization}.");
                plan.Warnings.Add(request.RebuildCurrentRealization
                    ? $"Track Editor regenerated {slot.TopologySlotId} as " +
                      $"{request.RequestedRealization}; connectors and closure were solved automatically."
                    : $"Track Editor rebuilt {slot.TopologySlotId}: {slot.CurrentRealization} → " +
                      $"{request.RequestedRealization}; connectors and closure were solved automatically.");
            }

        }

        /// <summary>
        /// Resolves the exact feature identities authorized by an Area of Impact
        /// request and returns one contiguous definition window. No count-only fallback
        /// exists: a recipe may remove only the neighboring features its designer saw
        /// and explicitly confirmed.
        /// </summary>
        public static bool TryResolveAuthoringImpactWindow(
            IReadOnlyList<TopologySlotRecord> slots,
            TopologySlotOverride request,
            TopologySlotRecord selected,
            out int firstDefinitionIndex,
            out int lastDefinitionIndex,
            out List<TopologySlotRecord> impacted,
            out string reason)
        {
            firstDefinitionIndex = selected?.FirstDefinitionIndex ?? -1;
            lastDefinitionIndex = selected?.LastDefinitionIndex ?? -1;
            impacted = new List<TopologySlotRecord>();
            reason = "";
            if (selected == null || request == null)
            {
                reason = "The Track Editor impact request has no selected feature.";
                return false;
            }

            request.ImpactMembers ??= new List<TopologyImpactMember>();
            if (request.ImpactMembers.Count == 0) return true;
            if (!request.AllowFeatureOverrides)
            {
                reason = "Nearby features were included in the Area of Impact but were not confirmed for override.";
                return false;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < request.ImpactMembers.Count; i++)
            {
                TopologyImpactMember member = request.ImpactMembers[i];
                if (member == null || string.IsNullOrWhiteSpace(member.TopologySlotId)) continue;
                var identity = new TopologySlotOverride
                {
                    TopologySlotId = member.TopologySlotId,
                    StructuralAnchor = member.StructuralAnchor,
                    OriginalRealization = member.OriginalRealization
                };
                if (!TopologySlotCatalog.TryResolveOverride(slots, identity,
                        out TopologySlotRecord affected, out string resolutionFailure))
                {
                    reason = $"Area of Impact feature '{member.TopologySlotId}' is no longer " +
                             $"the feature that was confirmed: {resolutionFailure}";
                    return false;
                }
                if (!TrackEditorImpact.SharesTravelPath(selected, affected))
                {
                    reason = $"Area of Impact feature '{affected.TopologySlotId}' is outside " +
                             "the selected travel path.";
                    return false;
                }
                if (!seen.Add(affected.TopologySlotId)) continue;
                impacted.Add(affected);
                firstDefinitionIndex = Mathf.Min(firstDefinitionIndex, affected.FirstDefinitionIndex);
                lastDefinitionIndex = Mathf.Max(lastDefinitionIndex, affected.LastDefinitionIndex);
            }

            impacted.Sort((left, right) => left.FirstDefinitionIndex.CompareTo(
                right.FirstDefinitionIndex));
            return true;
        }

        /// <summary>
        /// Computes the replacement's legal removal scope. When a realization emits
        /// its own recovery, an immediately adjacent recovery generated for the old
        /// realization is superseded and belongs to OwnedConnectors. Keeping both
        /// wastes closure reserve and creates a false fit penalty.
        /// </summary>
        public static int ComputeReplacementRemovalCount(
            IReadOnlyList<TrackMacroSectionDefinition> existing,
            TopologySlotRecord slot,
            IReadOnlyList<TrackMacroSectionDefinition> replacement,
            LocalReplanScope scope)
        {
            if (slot == null) return 0;
            int baseCount = slot.LastDefinitionIndex - slot.FirstDefinitionIndex + 1;
            if (existing == null || replacement == null || baseCount <= 0 ||
                scope < LocalReplanScope.OwnedConnectors)
                return Mathf.Max(0, baseCount);

            bool replacementOwnsRecovery = false;
            for (int i = 0; i < replacement.Count; i++)
            {
                TrackMacroSectionDefinition definition = replacement[i];
                if (definition != null &&
                    definition.SectionType == TrackMacroSectionType.RecoveryStraight)
                {
                    replacementOwnsRecovery = true;
                    break;
                }
            }
            if (!replacementOwnsRecovery) return baseCount;

            bool originalRequiresRecovery = false;
            int ownedStart = Mathf.Max(0, slot.FirstDefinitionIndex);
            int ownedEnd = Mathf.Min(existing.Count - 1, slot.LastDefinitionIndex);
            for (int i = ownedStart; i <= ownedEnd; i++)
            {
                TrackMacroSectionDefinition definition = existing[i];
                if (definition != null && definition.RequiresRecoveryAfter)
                {
                    originalRequiresRecovery = true;
                    break;
                }
            }
            if (!originalRequiresRecovery) return baseCount;

            int adjacentIndex = slot.LastDefinitionIndex + 1;
            if (adjacentIndex < 0 || adjacentIndex >= existing.Count) return baseCount;
            TrackMacroSectionDefinition adjacent = existing[adjacentIndex];
            if (adjacent == null ||
                adjacent.SectionType != TrackMacroSectionType.RecoveryStraight ||
                (slot.QuarterIndex >= 0 && adjacent.QuarterIndex != slot.QuarterIndex) ||
                adjacent.RoadId != slot.RoadId ||
                (!string.IsNullOrEmpty(adjacent.PatternId) &&
                 !string.Equals(adjacent.PatternId, slot.PatternId,
                     System.StringComparison.Ordinal)))
                return baseCount;

            return baseCount + 1;
        }

        private static void MarkAppliedTopologyOverrides(
            TopologyPlan plan,
            IReadOnlyList<TopologySlotOverride> overrides)
        {
            if (plan?.TopologySlots == null || overrides == null) return;

            for (int i = 0; i < overrides.Count; i++)
            {
                TopologySlotOverride request = overrides[i];
                if (request == null) continue;
                if (TryResolveAppliedReplacementSlot(plan.TopologySlots, request,
                        out TopologySlotRecord slot))
                {
                    // Keep the rebuilt route's naturally assigned finished identity.
                    // The accepted recipe is normalized only after candidate selection,
                    // so rejected attempts can never mutate the shared request list.
                    slot.OriginalRealization = request.OriginalRealization;
                    slot.CurrentRealization = request.RequestedRealization;
                    slot.OverrideState = TopologySlotOverrideState.Applied;
                    slot.LocalReplanPolicy = request.MaximumReplanScope;
                    continue;
                }

                plan.Fail(GenerationFailureReason.RecipeCompatibilityFailure,
                    $"Applied Track Editor replacement '{request.TopologySlotId}' could not be " +
                    "identified in the rebuilt route. The previous valid track was kept.");
                return;
            }
        }

        private static string AppliedReplacementPatternId(TopologySlotOverride request) =>
            request == null
                ? ""
                : $"track-editor:{request.TopologySlotId}:{request.RequestedRealization}";

        /// <summary>
        /// Resolves an applied replacement by its structural demand first, then by the
        /// exact marker stamped on the replacement geometry. The marker fallback is
        /// required when Area of Impact removes a later cross-quarter feature and the
        /// finished route's reporting identity necessarily shifts.
        /// </summary>
        public static bool TryResolveAppliedReplacementSlot(
            IReadOnlyList<TopologySlotRecord> slots,
            TopologySlotOverride request,
            out TopologySlotRecord resolved)
        {
            if (TopologySlotCatalog.TryResolveOverride(slots, request, out resolved,
                    out _, appliedRealization: true))
                return true;

            resolved = null;
            if (slots == null || request == null) return false;
            string marker = AppliedReplacementPatternId(request);
            for (int i = 0; i < slots.Count; i++)
            {
                TopologySlotRecord candidate = slots[i];
                if (candidate == null || !string.Equals(candidate.PatternId, marker,
                        System.StringComparison.Ordinal) ||
                    (candidate.CurrentRealization != request.RequestedRealization &&
                     candidate.OriginalRealization != request.RequestedRealization))
                    continue;
                if (resolved != null) return false;
                resolved = candidate;
            }
            return resolved != null;
        }

        /// <summary>
        /// Stage A consumption: measures the achieved result of every feature group
        /// (consecutive defs sharing a non-empty PatternId) by building its frames from
        /// a canonical origin entry with the same builders the candidate uses, then
        /// cross-checks the legacy stamped plan scalars. Mismatches are WARNINGS in
        /// Stage A — behavior must not change; the editor tests assert them hard.
        /// Deterministic and rng-free.
        /// </summary>
        private void ComputeFeatureResults(ResolvedTrackGenerationConfig cfg, TopologyPlan plan)
        {
            var defs = plan.Defs;
            var ctx = FrameBuildContext.From(cfg);

            for (int i = 0; i < defs.Count;)
            {
                string pid = defs[i]?.PatternId;
                if (string.IsNullOrEmpty(pid)) { i++; continue; }

                int start = i;
                while (i < defs.Count && defs[i] != null && defs[i].PatternId == pid) i++;

                var element = defs[start].SemanticElement;
                var entry = TrackConnectionFrame.Origin(defs[start].Width);
                var result = FeaturePlanning.ComputePlanResult(defs, start, i, entry, ctx, element, pid);

                plan.FeatureResults.Add(result);
                plan.FeatureExitRecords.Add(result.ToString());
                if (result.Failed)
                {
                    plan.Warnings.Add($"[StageA] {pid}: plan-result measurement failed — {result.FailureReason}");
                    continue;
                }

                // Cross-check against the legacy stamped scalars where a single
                // rotational def carries them (loop/corkscrew/half-loop family).
                if (i - start == 1 && defs[start].SectionType == TrackMacroSectionType.RotationalEvent)
                {
                    var d = defs[start];
                    float dispErr = Mathf.Abs(result.PlanDisplacement.y - d.PlanHorizontalLength);
                    float latErr = Mathf.Abs(result.PlanDisplacement.x - d.PlanLateralOffset);
                    float elevErr = Mathf.Abs(result.ElevationChange - d.ElevationChange);
                    float headErr = Mathf.Abs(Mathf.DeltaAngle(result.HeadingContributionDeg, d.TurnAngle));
                    if (dispErr > 0.1f || latErr > 0.1f || elevErr > 0.1f || headErr > 0.1f)
                        plan.Warnings.Add(
                            $"[StageA] {pid}: measured exit diverges from stamped plan scalars " +
                            $"(fwd {dispErr:F3}m, lat {latErr:F3}m, elev {elevErr:F3}m, heading {headErr:F3}°) — " +
                            "plan/runtime disagreement, investigate before Stage B.");
                }
            }
        }

        /// <summary>Planned length of one dual quarter's gate suites (entry choice jump + exit convergence jump).</summary>
        internal static float QuarterGateOverhead(ResolvedTrackGenerationConfig cfg)
            => 2f * (cfg.MaxLaunchTransitionLength + cfg.DesignSpeedMps * cfg.MaxJumpAirtimeSeconds + cfg.JumpLandingPlanningLength)
               + cfg.JumpRecoveryLength + cfg.DefaultApproachLength;

        // ═══════════════════════════ 1. Corner plan ═══════════════════════════

        private List<CornerSlot> PlanCorners(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, PlanRandomStreams rngs)
        {
            // Corner GEOMETRY (count, magnitudes, signs, balance) draws from the Layout
            // stream; realizations (special patterns, half-loops, wallrides) draw from
            // the Feature stream — so feature re-rolls keep the same corner skeleton.
            ref Unity.Mathematics.Random rng = ref rngs.Layout;

            int count = rng.NextInt(cfg.MinTurnCount, cfg.MaxTurnCount + 1);
            var corners = new List<CornerSlot>(count);

            // Family magnitude windows (degrees).
            (float w, int min, int max)[] families =
            {
                (cfg.TurnWeights.GentleBend,      20,  45),
                (cfg.TurnWeights.Sweeper,         45,  80),
                (cfg.TurnWeights.StandardCorner,  80, 120),
                (cfg.TurnWeights.SharpCorner,    120, 150),
                (cfg.TurnWeights.Hairpin,        150, 180)
            };
            float totalWeight = cfg.TurnWeights.Total;

            // Direction signs by pattern.
            float flipChance = cfg.DirectionPattern switch
            {
                TurnDirectionPattern.Circuit => 0.12f,
                TurnDirectionPattern.Mixed => 0.5f,
                TurnDirectionPattern.Alternating => 0.8f,
                TurnDirectionPattern.Switchback => 0.68f,
                _ => 0.5f
            };

            // Reversal realizations share the 150–180° corner-demand pool. The turn
            // family only creates as many reversal-capable slots as the enabled
            // Hairpin/Wide Turnaround/Horseshoe/Half Helix rules can consume.
            int ReversalBudget(ResolvedFeatureRule rule, TrackPatternType pattern)
            {
                int explicitCount = 0;
                foreach (RequiredPatternEntry required in cfg.RequiredPatterns)
                    if (required != null && required.Pattern == pattern)
                        explicitCount = Mathf.Max(explicitCount, required.Count);
                int configured = rule != null && rule.Enabled ? rule.MaximumCount : 0;
                return Mathf.Max(configured, explicitCount);
            }

            int hairpinBudget =
                ReversalBudget(cfg.Hairpins, TrackPatternType.Hairpin) +
                ReversalBudget(cfg.WideTurnarounds, TrackPatternType.WideTurnaround) +
                ReversalBudget(cfg.Horseshoes, TrackPatternType.Horseshoe) +
                ReversalBudget(cfg.HalfHelixTurnarounds, TrackPatternType.HalfHelixTurnaround);
            int hairpinsUsed = 0;

            int prevSign = rng.NextBool() ? 1 : -1;
            for (int i = 0; i < count; i++)
            {
                float pick = rng.NextFloat(0f, totalWeight);
                int family = 0;
                for (int f = 0; f < families.Length; f++)
                {
                    pick -= families[f].w;
                    if (pick <= 0f) { family = f; break; }
                    if (f == families.Length - 1) family = f;
                }

                bool hairpinSlot = family == 4 && hairpinsUsed < hairpinBudget;
                if (family == 4 && !hairpinSlot) family = 3; // demote to sharp corner
                if (hairpinSlot) hairpinsUsed++;

                int mag = Mathf.Clamp(Mathf.RoundToInt(rng.NextFloat(families[family].min, families[family].max) / 5f) * 5,
                    20, hairpinSlot ? 180 : 145);
                int sign = rng.NextFloat() < flipChance ? -prevSign : prevSign;
                prevSign = sign;

                corners.Add(new CornerSlot { SignedAngle = sign * mag, AllowHairpinMagnitude = hairpinSlot });
            }

            // Required hairpins: promote the sharpest corners up to hairpin magnitude.
            int hairpinsNeeded = cfg.Hairpins.Enabled ? cfg.Hairpins.MinimumCount : 0;
            if (hairpinsNeeded > 0)
            {
                corners.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));
                for (int i = 0; i < hairpinsNeeded && i < corners.Count; i++)
                {
                    corners[i].SignedAngle = corners[i].Sign * Mathf.Max(150, corners[i].Magnitude);
                    corners[i].Realization = TrackPatternType.Hairpin;
                    corners[i].IsSpecial = true;
                    corners[i].AllowHairpinMagnitude = true;
                    plan.CountPattern(TrackPatternType.Hairpin);
                }
                Shuffle(corners, ref rng);
            }

            // Reserve required wallrides before the other special turn families
            // claim corner demand. Previously wallrides ran last, so enabling a
            // Dive Loop or Sidewinder minimum could consume every compatible corner
            // and make otherwise healthy layouts fail before geometry was attempted.
            int requiredWallridesAssigned = 0;
            if (cfg.Wallrides.Enabled && cfg.Wallrides.MinimumCount > 0)
            {
                var wallrideCandidates = new List<CornerSlot>();
                foreach (CornerSlot corner in corners)
                    if (!corner.IsSpecial && !corner.IsHalfLoop)
                        wallrideCandidates.Add(corner);
                wallrideCandidates.Sort((a, b) =>
                {
                    bool aInWindow = a.Magnitude >= 60 && a.Magnitude <= 140;
                    bool bInWindow = b.Magnitude >= 60 && b.Magnitude <= 140;
                    if (aInWindow != bInWindow) return aInWindow ? -1 : 1;
                    return Mathf.Abs(a.Magnitude - 90).CompareTo(
                        Mathf.Abs(b.Magnitude - 90));
                });

                foreach (CornerSlot corner in wallrideCandidates)
                {
                    if (requiredWallridesAssigned >= cfg.Wallrides.MinimumCount) break;
                    // Canonical heading closure can safely flip this token later,
                    // but it remains a physically appropriate 90-degree wallride.
                    corner.SignedAngle = corner.Sign * 90;
                    corner.AllowHairpinMagnitude = false;
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.WallrideTurn;
                    requiredWallridesAssigned++;
                    plan.CountPattern(TrackPatternType.WallrideTurn);
                }

                if (requiredWallridesAssigned < cfg.Wallrides.MinimumCount)
                {
                    plan.Fail(GenerationFailureReason.RequiredFeatureMissing,
                        $"Required {cfg.Wallrides.MinimumCount} wallride turns but only " +
                        $"{requiredWallridesAssigned} unclaimed corner demands were available.");
                    return corners;
                }
            }

            // Definition-native reversals are first-class procedural realizations.
            // Required counts claim the sharpest available corners; optional counts
            // use their rule weight. They stay exact 180° demands during heading
            // closure so the definition compiler receives a legal authored heading.
            bool AssignDefinitionReversals(ResolvedFeatureRule rule,
                TrackPatternType pattern, SemanticElementId semantic)
            {
                int explicitMinimum = 0;
                foreach (RequiredPatternEntry required in cfg.RequiredPatterns)
                    if (required != null && required.Pattern == pattern)
                        explicitMinimum = Mathf.Max(explicitMinimum, required.Count);

                bool normalEnabled = rule != null && rule.Enabled;
                int minimum = Mathf.Max(normalEnabled ? rule.MinimumCount : 0, explicitMinimum);
                int maximum = Mathf.Max(normalEnabled ? rule.MaximumCount : 0, minimum);
                if (maximum <= 0) return true;

                var eligible = new List<CornerSlot>();
                foreach (var c in corners)
                    if (!c.IsSpecial && !c.IsHalfLoop)
                        eligible.Add(c);
                eligible.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));

                int assigned = 0;
                foreach (var c in eligible)
                {
                    if (assigned >= maximum) break;
                    bool required = assigned < minimum;
                    if (!required && (!normalEnabled ||
                        rngs.Feature.NextFloat() >= Mathf.Clamp01(rule.OptionalWeight * 0.25f)))
                        continue;

                    c.SignedAngle = c.Sign * 180;
                    c.AllowHairpinMagnitude = true;
                    c.IsSpecial = true;
                    c.Realization = pattern;
                    c.DefinitionRealization = semantic;
                    assigned++;
                    plan.CountPattern(pattern);
                }

                if (assigned >= minimum) return true;
                plan.Fail(explicitMinimum > 0
                        ? GenerationFailureReason.RequiredPatternMissing
                        : GenerationFailureReason.RequiredFeatureMissing,
                    $"Required {minimum} {pattern} turns but only {assigned} reversal slots were available.");
                return false;
            }

            if (!AssignDefinitionReversals(cfg.WideTurnarounds,
                    TrackPatternType.WideTurnaround, SemanticElementId.WideTurnaround))
                return corners;
            if (!AssignDefinitionReversals(cfg.Horseshoes,
                    TrackPatternType.Horseshoe, SemanticElementId.Horseshoe))
                return corners;
            if (!AssignDefinitionReversals(cfg.HalfHelixTurnarounds,
                    TrackPatternType.HalfHelixTurnaround, SemanticElementId.HalfHelixTurnaround))
                return corners;

            bool AssignDefinitionCornerRule(ResolvedFeatureRule rule,
                TrackPatternType pattern, SemanticElementId semantic)
            {
                int explicitMinimum = 0;
                foreach (RequiredPatternEntry required in cfg.RequiredPatterns)
                    if (required != null && required.Pattern == pattern)
                        explicitMinimum = Mathf.Max(explicitMinimum, required.Count);

                bool normalEnabled = rule != null && rule.Enabled;
                int minimum = Mathf.Max(normalEnabled ? rule.MinimumCount : 0, explicitMinimum);
                int maximum = Mathf.Max(normalEnabled ? rule.MaximumCount : 0, minimum);
                if (maximum <= 0) return true;

                if (!TrackFeatureDefinitionCatalog.TryGet(pattern, out TrackFeatureDefinition definition) ||
                    !definition.Enabled || definition.SemanticElement != semantic ||
                    (definition.Solver != FeatureDefinitionSolver.EasedTurnV1 &&
                     definition.Solver != FeatureDefinitionSolver.RotationalSequenceV1))
                {
                    plan.Fail(GenerationFailureReason.RequiredFeatureMissing,
                        $"{pattern} has no enabled turn-definition solver.");
                    return false;
                }

                var eligible = new List<CornerSlot>();
                foreach (CornerSlot corner in corners)
                    if (!corner.IsSpecial && !corner.IsHalfLoop)
                        eligible.Add(corner);
                float preferred = definition.Turn.HeadingDegrees.Preferred;
                eligible.Sort((a, b) =>
                    Mathf.Abs(a.Magnitude - preferred).CompareTo(
                        Mathf.Abs(b.Magnitude - preferred)));

                int assigned = 0;
                foreach (CornerSlot corner in eligible)
                {
                    if (assigned >= maximum) break;
                    bool required = assigned < minimum;
                    if (!required && (!normalEnabled ||
                        rngs.Feature.NextFloat() >= Mathf.Clamp01(rule.OptionalWeight * 0.25f)))
                        continue;

                    int magnitude = Mathf.RoundToInt(Mathf.Clamp(preferred,
                        definition.Turn.HeadingDegrees.Minimum,
                        definition.Turn.HeadingDegrees.Maximum) / 5f) * 5;
                    corner.SignedAngle = corner.Sign * magnitude;
                    corner.AllowHairpinMagnitude = magnitude > 145;
                    corner.IsSpecial = true;
                    corner.Realization = pattern;
                    corner.DefinitionRealization = semantic;
                    assigned++;
                    plan.CountPattern(pattern);
                }

                if (assigned >= minimum) return true;
                plan.Fail(explicitMinimum > 0
                        ? GenerationFailureReason.RequiredPatternMissing
                        : GenerationFailureReason.RequiredFeatureMissing,
                    $"Required {minimum} {pattern} turns but only {assigned} compatible corner slots were available.");
                return false;
            }

            if (!AssignDefinitionCornerRule(cfg.Cutbacks,
                    TrackPatternType.Cutback, SemanticElementId.Cutback))
                return corners;
            if (!AssignDefinitionCornerRule(cfg.DiveLoops,
                    TrackPatternType.DiveLoop, SemanticElementId.DiveLoop))
                return corners;
            if (!AssignDefinitionCornerRule(cfg.Sidewinders,
                    TrackPatternType.Sidewinder, SemanticElementId.Sidewinder))
                return corners;

            // Required definition-native turn patterns claim ordinary corner demand
            // directly. They remain opt-in until a dedicated designer rule promotes
            // them into the normal optional pool, preserving established seed layouts.
            foreach (RequiredPatternEntry required in cfg.RequiredPatterns)
            {
                if (required == null || required.Count <= 0 ||
                    required.Pattern == TrackPatternType.WideTurnaround ||
                    required.Pattern == TrackPatternType.Horseshoe ||
                    required.Pattern == TrackPatternType.HalfHelixTurnaround ||
                    required.Pattern == TrackPatternType.Cutback ||
                    required.Pattern == TrackPatternType.DiveLoop ||
                    required.Pattern == TrackPatternType.Sidewinder)
                    continue;
                if (!FeaturePatternLibrary.IsCornerSlotPattern(required.Pattern) ||
                    !TrackFeatureDefinitionCatalog.TryGet(
                        required.Pattern, out TrackFeatureDefinition turnDefinition) ||
                    !turnDefinition.Enabled ||
                    (turnDefinition.Solver != FeatureDefinitionSolver.EasedTurnV1 &&
                     turnDefinition.Solver != FeatureDefinitionSolver.RotationalSequenceV1))
                    continue;

                for (int occurrence = 0; occurrence < required.Count; occurrence++)
                {
                    CornerSlot selected = null;
                    float bestDistance = float.PositiveInfinity;
                    foreach (CornerSlot candidate in corners)
                    {
                        if (candidate.IsSpecial || candidate.IsHalfLoop) continue;
                        float distance = Mathf.Abs(
                            candidate.Magnitude - turnDefinition.Turn.HeadingDegrees.Preferred);
                        if (distance >= bestDistance) continue;
                        selected = candidate;
                        bestDistance = distance;
                    }

                    if (selected == null)
                    {
                        plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                            $"Required pattern {required.Pattern} has no unclaimed corner slot.");
                        return corners;
                    }

                    int magnitude = Mathf.RoundToInt(
                        Mathf.Clamp(turnDefinition.Turn.HeadingDegrees.Preferred,
                            turnDefinition.Turn.HeadingDegrees.Minimum,
                            turnDefinition.Turn.HeadingDegrees.Maximum) / 5f) * 5;
                    selected.SignedAngle = selected.Sign * magnitude;
                    selected.AllowHairpinMagnitude = magnitude > 145;
                    selected.IsSpecial = true;
                    selected.Realization = required.Pattern;
                    selected.DefinitionRealization = turnDefinition.SemanticElement;
                    plan.CountPattern(required.Pattern);
                }
            }

            // Required half-loop patterns become exact 180° vertical reversals.
            int halfLoopPlain = cfg.HalfLoops.Enabled ? cfg.HalfLoops.MinimumCount : 0;
            var requiredHalfLoopTypes = new List<TrackPatternType>();
            for (int i = 0; i < halfLoopPlain; i++) requiredHalfLoopTypes.Add(TrackPatternType.HalfLoopRollout);
            foreach (var p in cfg.RequiredPatterns)
            {
                if (p.Pattern == TrackPatternType.HalfLoopRollout || p.Pattern == TrackPatternType.HalfLoopToCorkscrew)
                    for (int i = 0; i < p.Count; i++) requiredHalfLoopTypes.Add(p.Pattern);
            }

            // Optional half-loops by weight (up to the maximum).
            int optionalHalfLoopBudget = cfg.HalfLoops.Enabled
                ? Mathf.Max(0, cfg.HalfLoops.MaximumCount - halfLoopPlain)
                : 0;
            for (int i = 0; i < optionalHalfLoopBudget; i++)
            {
                if (rngs.Feature.NextFloat() < Mathf.Clamp01(cfg.HalfLoops.OptionalWeight * 0.25f))
                    requiredHalfLoopTypes.Add(TrackPatternType.HalfLoopRollout);
            }

            if (requiredHalfLoopTypes.Count > 0)
            {
                // Convert the sharpest non-special corners into half-loop reversals,
                // CLUSTERED so at least one provisional quarter stays half-loop-free:
                // a half-loop rollout owns the airspace over its ground path, which
                // makes its quarter ineligible to become a Dual Road Quarter — a
                // half-loop-heavy preset that scatters them across all four quarters
                // can never satisfy a dual-quarter minimum. Placement uses only corner
                // data, so seed-stream independence is preserved.
                int[] quarterOf = ProvisionalQuarters(corners, cfg);
                var dirty = new bool[4];
                // The clean quarter this pass protects must be one that dual-quarter
                // POLICY can actually use — keeping only Q1/Q4 clean is worthless when
                // the start/finish policies bar them from ever going dual.
                bool PolicyEligible(int qi) =>
                    cfg.QuarterTypeOverrides[qi] != QuarterTypeOverride.SingleRoad &&
                    (qi != 0 || cfg.AllowQ1Dual) &&
                    (qi != 3 || cfg.AllowQ4Dual);
                int CleanEligible()
                {
                    int clean = 0;
                    for (int qi = 0; qi < 4; qi++) if (!dirty[qi] && PolicyEligible(qi)) clean++;
                    return clean;
                }

                var indexOf = new Dictionary<CornerSlot, int>();
                for (int ci = 0; ci < corners.Count; ci++) indexOf[corners[ci]] = ci;

                var byMag = new List<CornerSlot>(corners);
                byMag.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));
                int assigned = 0;

                // Three preference tiers so half-loops CONCENTRATE: (0) quarters that
                // are already dirty or can never go dual by policy, (1) dirty a fresh
                // quarter but never the LAST clean policy-eligible one, (2) whatever
                // remains so required counts always land.
                for (int pass = 0; pass < 3 && assigned < requiredHalfLoopTypes.Count; pass++)
                {
                    foreach (var c in byMag)
                    {
                        if (assigned >= requiredHalfLoopTypes.Count) break;
                        if (c.IsSpecial || c.IsHalfLoop) continue;

                        int cq = quarterOf[indexOf[c]];
                        if (pass == 0 && !dirty[cq] && PolicyEligible(cq)) continue;
                        if (pass == 1 && !dirty[cq] && PolicyEligible(cq) && CleanEligible() <= 1) continue;

                        c.IsHalfLoop = true;
                        c.HalfLoopType = requiredHalfLoopTypes[assigned];
                        c.SignedAngle = c.Sign * 180;
                        dirty[cq] = true;
                        assigned++;
                    }
                }

                if (assigned < requiredHalfLoopTypes.Count)
                {
                    plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                        $"Needed {requiredHalfLoopTypes.Count} half-loop reversal slots but only {assigned} corners were available.");
                    return corners;
                }
            }

            // Balance the signed sum to exactly ±360 so the lap closes in heading.
            // Stage E (opt-in): exact canonical 45/90/180 token solve; else the legacy
            // continuous ±20°-step balancer (byte-identical old path when the flag is off).
            bool balanced = cfg.UseCanonicalTurns
                ? SolveCanonicalTokens(corners)
                : BalanceCornerSum(corners, ref rng);
            if (!balanced)
            {
                plan.Fail(GenerationFailureReason.ClosureHeadingFailure,
                    cfg.UseCanonicalTurns
                        ? $"No canonical 45/90/180 token sequence over {corners.Count} corners closes the lap to ±360°."
                        : $"Could not balance {corners.Count} signed corners to ±360°.");
                return corners;
            }

            // Required wallrides were reserved before competing turn families.
            // Add only optional wallrides here, after heading closure has produced
            // final compatible 90-degree tokens.
            if (cfg.Wallrides.Enabled && cfg.Wallrides.MaximumCount > 0)
            {
                var eligible = new List<CornerSlot>();
                foreach (var c in corners)
                    if (!c.IsSpecial && !c.IsHalfLoop && c.Magnitude >= 60 && c.Magnitude <= 140)
                        eligible.Add(c);
                eligible.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));

                int assigned = requiredWallridesAssigned;
                foreach (var c in eligible)
                {
                    if (assigned >= cfg.Wallrides.MaximumCount) break;
                    if (rngs.Feature.NextFloat() >= Mathf.Clamp01(
                            cfg.Wallrides.OptionalWeight * 0.3f)) continue;
                    c.IsSpecial = true;
                    c.Realization = TrackPatternType.WallrideTurn;
                    assigned++;
                    plan.CountPattern(TrackPatternType.WallrideTurn);
                }

                if (assigned < cfg.Wallrides.MinimumCount)
                {
                    plan.Fail(GenerationFailureReason.RequiredFeatureMissing,
                        $"Required {cfg.Wallrides.MinimumCount} wallride turns but only {assigned} corners in the 60–140° window were available.");
                    return corners;
                }
            }

            // ── Stage F (pilot): directional corkscrews. One eligible corner per lap is
            // realized as a barrel that ALSO turns by its own magnitude. Reuses the
            // IsSpecial/Realization path and MakeCorkscrewDef(horizontalTurn); the stamped
            // plan-view TurnAngle equals the corner, so heading closure is preserved by
            // construction. Runs AFTER the canonical solve so the magnitude is a clean
            // token when that flag is on. Flag-gated; off = no change.
            //
            // ±45 ONLY: a 90° turning barrel measured ~3.4km of arc (blowing the length cap
            // → InsufficientLengthBudget) and packed too much curvature into the ring budget
            // (max facet 3.1° vs 0.5° target). The gentle ±45 barrel stays smooth and
            // affordable. Widen this window only after the 90° geometry is stretched/eased.
            // Directional corkscrews are optional corner content. Reserve one only
            // when the rule has headroom above its required minimum. Previously a
            // min=1/max=1 preset emitted the required gap corkscrew and then added a
            // second corner barrel, making every completed candidate invalid.
            if (cfg.DirectionalCorkscrews && cfg.Corkscrews.Enabled &&
                cfg.Corkscrews.MaximumCount > cfg.Corkscrews.MinimumCount)
            {
                var dcoEligible = new List<CornerSlot>();
                foreach (var c in corners)
                    if (!c.IsSpecial && !c.IsHalfLoop && c.Magnitude >= 40 && c.Magnitude <= 50)
                        dcoEligible.Add(c);
                if (dcoEligible.Count > 0)
                {
                    var pick = dcoEligible[rngs.Feature.NextInt(dcoEligible.Count)];
                    pick.IsSpecial = true;
                    pick.Realization = TrackPatternType.Corkscrew;
                    plan.ReservedCornerCorkscrews++;
                }
            }

            // Intentional corner sequences on eligible plain corners.
            foreach (var c in corners)
            {
                if (c.IsSpecial || c.IsHalfLoop) continue;
                if (rngs.Feature.NextFloat() >= cfg.CornerSequenceChance) continue;

                int mag = c.Magnitude;
                if (mag >= 150)
                {
                    c.Realization = TrackPatternType.Hairpin;
                    plan.CountPattern(TrackPatternType.Hairpin);
                }
                else if (mag >= 90)
                {
                    c.Realization = rngs.Feature.NextBool() ? TrackPatternType.DoubleApex
                        : (rngs.Feature.NextBool() ? TrackPatternType.TighteningCorner : TrackPatternType.OpeningCorner);
                    plan.CountPattern(c.Realization);
                }
                else continue;

                c.IsSpecial = true;
            }

            // Sweeper-into-hairpin: merge an eligible sweeper directly before a hairpin.
            for (int i = 0; i < corners.Count; i++)
            {
                var c = corners[i];
                if (!c.IsSpecial || c.Realization != TrackPatternType.Hairpin || c.IsHalfLoop) continue;
                var prev = corners[(i - 1 + corners.Count) % corners.Count];
                if (prev.IsSpecial || prev.IsHalfLoop || prev.Sign != c.Sign || prev.Magnitude > 80) continue;
                if (rngs.Feature.NextFloat() >= cfg.CornerSequenceChance) continue;

                c.Realization = TrackPatternType.SweeperIntoHairpin;
                plan.CountPattern(TrackPatternType.SweeperIntoHairpin);
                break; // one per lap is plenty
            }

            return corners;
        }

        /// <summary>
        /// Provisional quarter (0..3) of each corner: quartile cuts over the cumulative
        /// corner-plan arc estimate — the same model PartitionQuarters uses, computed
        /// from pre-realization magnitudes so realization passes can be quarter-aware
        /// without touching the Quarter seed stream.
        /// </summary>
        private static int[] ProvisionalQuarters(List<CornerSlot> corners, ResolvedTrackGenerationConfig cfg)
        {
            int n = corners.Count;
            var result = new int[n];
            if (n == 0) return result;

            float avgStraight = (cfg.MinStraightLength + cfg.MaxStraightLength) * 0.5f;
            var estimate = new float[n];
            float total = 0f;
            for (int g = 0; g < n; g++)
            {
                float mag = corners[g].Magnitude;
                float radius = Mathf.Max(Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, mag / 180f), cfg.MinCurveRadius);
                estimate[g] = avgStraight + SectionFrameBuilders.EasedArcLength(mag, radius);
                total += estimate[g];
            }

            float prefix = 0f;
            for (int g = 0; g < n; g++)
            {
                prefix += estimate[g] * 0.5f; // classify by the gap's midpoint
                result[g] = Mathf.Clamp(Mathf.FloorToInt(prefix / total * 4f), 0, 3);
                prefix += estimate[g] * 0.5f;
            }
            return result;
        }

        /// <summary>Nudges/flips corner magnitudes until the signed sum is exactly ±360.</summary>
        private static bool BalanceCornerSum(List<CornerSlot> corners, ref Unity.Mathematics.Random rng)
        {
            int Sum()
            {
                int s = 0;
                foreach (var c in corners) s += c.SignedAngle;
                return s;
            }

            int sum = Sum();
            int target = sum >= 0 ? 360 : -360;

            for (int pass = 0; pass < 256 && sum != target; pass++)
            {
                int residual = target - sum;

                int idx = rng.NextInt(0, corners.Count);
                var c = corners[idx];
                if (c.IsHalfLoop || c.DefinitionRealization == SemanticElementId.WideTurnaround ||
                    c.DefinitionRealization == SemanticElementId.Horseshoe ||
                    c.DefinitionRealization == SemanticElementId.HalfHelixTurnaround) continue; // authored reversals stay exactly 180°

                int minMag = c.IsSpecial && (c.Realization == TrackPatternType.Hairpin ||
                    c.DefinitionRealization == SemanticElementId.WideTurnaround ||
                    c.DefinitionRealization == SemanticElementId.Horseshoe ||
                    c.DefinitionRealization == SemanticElementId.HalfHelixTurnaround) ? 150 : 20;
                int maxMag = c.AllowHairpinMagnitude ? 180 : 145; // balancing never mints extra hairpins
                if (c.IsSpecial && c.Realization == TrackPatternType.WallrideTurn)
                {
                    minMag = 60;
                    maxMag = 140;
                }
                if (c.DefinitionRealization != SemanticElementId.None &&
                    TrackFeatureDefinitionCatalog.TryGet(
                        c.DefinitionRealization, out TrackFeatureDefinition definition) &&
                    definition.TopologyRole == TopologyRole.TurnRealization &&
                    definition.Turn.HeadingDegrees.Maximum > 0f)
                {
                    minMag = Mathf.CeilToInt(definition.Turn.HeadingDegrees.Minimum);
                    maxMag = Mathf.FloorToInt(definition.Turn.HeadingDegrees.Maximum);
                }

                int step = Mathf.Clamp(residual * c.Sign, -20, 20);
                int newMag = Mathf.Clamp(c.Magnitude + step, minMag, maxMag);

                if (newMag == c.Magnitude && pass > corners.Count * 3)
                {
                    // Saturated: flip the non-special corner whose flip best approaches the target.
                    int bestIdx = -1, bestErr = Mathf.Abs(residual);
                    for (int i = 0; i < corners.Count; i++)
                    {
                        if (corners[i].IsHalfLoop ||
                            corners[i].DefinitionRealization == SemanticElementId.WideTurnaround ||
                            corners[i].DefinitionRealization == SemanticElementId.Horseshoe ||
                            corners[i].DefinitionRealization == SemanticElementId.HalfHelixTurnaround) continue;
                        int err = Mathf.Abs(target - (sum - 2 * corners[i].SignedAngle));
                        if (err < bestErr) { bestErr = err; bestIdx = i; }
                    }
                    if (bestIdx >= 0)
                    {
                        sum -= 2 * corners[bestIdx].SignedAngle;
                        corners[bestIdx].SignedAngle = -corners[bestIdx].SignedAngle;
                    }
                    continue;
                }

                sum += (newMag - c.Magnitude) * c.Sign;
                c.SignedAngle = c.Sign * newMag;
            }

            return sum == target;
        }

        /// <summary>
        /// Stage E: assigns each corner an exact canonical token (±45/±90/±180) whose
        /// signed sum closes the lap to ±360°, by bounded exact dynamic programming over
        /// residual in 45° units. Deterministic — no rng: among all closing assignments
        /// it picks, per corner, the token nearest the magnitude the layout drew, so
        /// quantized layouts stay close to the continuous ones. Half-loops and hairpin
        /// realizations are pinned to ±180 with their drawn sign; direction preference
        /// is honoured by targeting the sign of the drawn winding.
        /// </summary>
        private static bool SolveCanonicalTokens(List<CornerSlot> corners)
        {
            int n = corners.Count;
            if (n == 0) return false;

            const int U = 45;                       // one 45° unit
            int drawnSum = 0;
            foreach (var c in corners) drawnSum += c.SignedAngle;
            int targetUnits = drawnSum >= 0 ? 8 : -8;   // ±360°

            // Per-corner candidate signed tokens (in 45° units) and their cost = how far
            // the token is from the drawn magnitude (keeps the quantized plan faithful).
            var options = new List<(int units, int cost)>[n];
            for (int i = 0; i < n; i++)
            {
                var c = corners[i];
                var list = new List<(int, int)>();
                int drawnUnits = Mathf.Clamp(Mathf.RoundToInt(c.Magnitude / (float)U), 1, 4);

                void Add(int mag) // mag in units: 1,2,4
                {
                    int cost = Mathf.Abs(mag - drawnUnits);
                    list.Add((c.Sign * mag, cost));
                    list.Add((-c.Sign * mag, cost + 4));   // flipping is allowed but costlier
                }

                if (c.DefinitionRealization != SemanticElementId.None &&
                    TrackFeatureDefinitionCatalog.TryGet(
                        c.DefinitionRealization, out TrackFeatureDefinition definition) &&
                    definition.TopologyRole == TopologyRole.TurnRealization &&
                    definition.Turn.HeadingDegrees.Maximum > 0f)
                {
                    for (int magnitudeUnits = 1; magnitudeUnits <= 4; magnitudeUnits++)
                    {
                        float degrees = magnitudeUnits * U;
                        if (degrees < definition.Turn.HeadingDegrees.Minimum - 0.01f ||
                            degrees > definition.Turn.HeadingDegrees.Maximum + 0.01f)
                            continue;
                        Add(magnitudeUnits);
                    }
                }
                else if (c.IsHalfLoop || (c.IsSpecial && (c.Realization == TrackPatternType.Hairpin ||
                    c.DefinitionRealization == SemanticElementId.WideTurnaround ||
                    c.DefinitionRealization == SemanticElementId.Horseshoe ||
                    c.DefinitionRealization == SemanticElementId.HalfHelixTurnaround)))
                {
                    // Pinned reversal: exactly ±180, keep its drawn sign (no flip).
                    list.Add((c.Sign * 4, 0));
                }
                else if (c.IsSpecial && c.Realization == TrackPatternType.WallrideTurn)
                {
                    Add(2);                         // committed 90° wallride token
                }
                else if (c.IsSpecial)
                {
                    Add(2); Add(4);                 // double-apex / tighten / open: 90 or 180
                }
                else
                {
                    Add(1); Add(2);                 // ordinary: 45 or 90
                    if (c.AllowHairpinMagnitude) Add(4);
                }
                options[i] = list;
            }

            // DP over residual. Offset so index 0 = residual -4n.
            int span = 4 * n;
            int width = 2 * span + 1;
            var best = new int[n + 1, width];
            var pick = new int[n + 1, width];
            for (int s = 0; s < n + 1; s++)
                for (int r = 0; r < width; r++) best[s, r] = int.MaxValue;
            best[0, span] = 0;                       // residual 0 before any corner

            for (int i = 0; i < n; i++)
            {
                for (int r = 0; r < width; r++)
                {
                    if (best[i, r] == int.MaxValue) continue;
                    int baseCost = best[i, r];
                    var opts = options[i];
                    for (int o = 0; o < opts.Count; o++)
                    {
                        int nr = r + opts[o].units;
                        if (nr < 0 || nr >= width) continue;
                        int nc = baseCost + opts[o].cost;
                        if (nc < best[i + 1, nr]) { best[i + 1, nr] = nc; pick[i + 1, nr] = o; }
                    }
                }
            }

            int endR = span + targetUnits;
            if (endR < 0 || endR >= width || best[n, endR] == int.MaxValue) return false;

            // Reconstruct.
            int cur = endR;
            for (int i = n; i > 0; i--)
            {
                int o = pick[i, cur];
                int units = options[i - 1][o].units;
                corners[i - 1].SignedAngle = units * U;
                cur -= units;
            }
            return true;
        }

        // ═══════════════════════════ 2. Gap features ═══════════════════════════

        private List<TrackPatternType> PlanGapFeatures(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, int maxSlots,
            float lengthBudget, ref Unity.Mathematics.Random rng)
        {
            var features = new List<TrackPatternType>();
            var counts = new Dictionary<TrackPatternType, int>();
            float footprintUsed = 0f;
            void Count(TrackPatternType t)
            {
                counts.TryGetValue(t, out int c);
                counts[t] = c + 1;
                footprintUsed += cfg.EstimateFeatureFootprint(t);
            }

            // Feature-count budget per underlying element, respecting maxima across
            // both single and compound placements.
            int loopsUsed = 0, corksUsed = plan.ReservedCornerCorkscrews,
                spiralsUsed = 0, jumpsUsed = 0, chicanesUsed = 0,
                sCurvesUsed = 0, pipesUsed = 0, camelbacksUsed = 0,
                heartlineRollsUsed = 0, zeroGRollsUsed = 0;

            bool TryConsume(TrackPatternType t)
            {
                switch (t)
                {
                    case TrackPatternType.FullPipe:
                        if (!cfg.FullPipes.Enabled || pipesUsed >= cfg.FullPipes.MaximumCount) return false;
                        pipesUsed++; return true;
                    case TrackPatternType.Camelback:
                        // An explicit RequiredPattern remains authoritative even while
                        // the normal random rule is switched off. When enabled, its max
                        // is a hard cap shared by required and optional placements.
                        if (cfg.Camelbacks.Enabled && camelbacksUsed >= cfg.Camelbacks.MaximumCount) return false;
                        camelbacksUsed++; return true;
                    case TrackPatternType.HeartlineRoll:
                        if (cfg.HeartlineRolls.Enabled &&
                            heartlineRollsUsed >= cfg.HeartlineRolls.MaximumCount) return false;
                        heartlineRollsUsed++; return true;
                    case TrackPatternType.ZeroGRoll:
                        if (cfg.ZeroGRolls.Enabled &&
                            zeroGRollsUsed >= cfg.ZeroGRolls.MaximumCount) return false;
                        zeroGRollsUsed++; return true;
                    case TrackPatternType.FullLoop:
                        if (!cfg.Loops.Enabled || loopsUsed >= cfg.Loops.MaximumCount) return false;
                        loopsUsed++; return true;
                    case TrackPatternType.Corkscrew:
                        if (!cfg.Corkscrews.Enabled || corksUsed >= cfg.Corkscrews.MaximumCount) return false;
                        corksUsed++; return true;
                    case TrackPatternType.Spiral:
                        if (!cfg.Spirals.Enabled || spiralsUsed >= cfg.Spirals.MaximumCount) return false;
                        spiralsUsed++; return true;
                    case TrackPatternType.JumpGap:
                    case TrackPatternType.JumpToBankedLanding:
                        if (!cfg.Jumps.Enabled || jumpsUsed >= cfg.Jumps.MaximumCount) return false;
                        jumpsUsed++; return true;
                    case TrackPatternType.Chicane:
                        if (!cfg.Chicanes.Enabled || chicanesUsed >= cfg.Chicanes.MaximumCount) return false;
                        chicanesUsed++; return true;
                    case TrackPatternType.SCurve:
                        if (!cfg.SCurves.Enabled || sCurvesUsed >= cfg.SCurves.MaximumCount) return false;
                        sCurvesUsed++; return true;
                    case TrackPatternType.AlternatingRadiusSequence:
                        // Builds two S-curve sections — consumes two slots of the rule.
                        if (!cfg.SCurves.Enabled || sCurvesUsed + 2 > cfg.SCurves.MaximumCount) return false;
                        sCurvesUsed += 2; return true;
                    case TrackPatternType.LoopToCorkscrew:
                        if (!cfg.Loops.AllowInCompoundPatterns || !cfg.Corkscrews.AllowInCompoundPatterns) return false;
                        if (loopsUsed >= cfg.Loops.MaximumCount || corksUsed >= cfg.Corkscrews.MaximumCount) return false;
                        loopsUsed++; corksUsed++; return true;
                    case TrackPatternType.SpiralToCorkscrew:
                        if (!cfg.Spirals.AllowInCompoundPatterns || !cfg.Corkscrews.AllowInCompoundPatterns) return false;
                        if (spiralsUsed >= cfg.Spirals.MaximumCount || corksUsed >= cfg.Corkscrews.MaximumCount) return false;
                        spiralsUsed++; corksUsed++; return true;
                    case TrackPatternType.DoubleCorkscrew:
                        if (!cfg.Corkscrews.AllowInCompoundPatterns) return false;
                        if (corksUsed + 2 > cfg.Corkscrews.MaximumCount) return false;
                        corksUsed += 2; return true;
                    default:
                        return true;
                }
            }

            // 1. Required patterns (non-corner-slot, non-half-loop: those live in the corner plan).
            foreach (var p in cfg.RequiredPatterns)
            {
                if (FeaturePatternLibrary.IsCornerSlotPattern(p.Pattern)) continue;
                if (p.Pattern == TrackPatternType.HalfLoopRollout || p.Pattern == TrackPatternType.HalfLoopToCorkscrew) continue;

                for (int i = 0; i < p.Count; i++)
                {
                    if (!TryConsume(p.Pattern))
                    {
                        plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                            $"Required pattern {p.Pattern} exceeds the feature-count limits of its underlying features.");
                        return features;
                    }
                    features.Add(p.Pattern);
                    Count(p.Pattern);
                }
            }

            // 2. Required minimum counts of single features.
            void Require(ResolvedFeatureRule rule, TrackPatternType type)
            {
                if (!rule.Enabled) return;
                counts.TryGetValue(type, out int have);
                for (int i = have; i < rule.MinimumCount; i++)
                {
                    if (!TryConsume(type))
                    {
                        plan.Fail(GenerationFailureReason.RequiredFeatureMissing,
                            $"Required minimum for {type} could not be reserved (count limit reached).");
                        return;
                    }
                    features.Add(type);
                    Count(type);
                }
            }

            Require(cfg.Loops, TrackPatternType.FullLoop);
            if (plan.Failed) return features;
            Require(cfg.Corkscrews, TrackPatternType.Corkscrew);
            if (plan.Failed) return features;
            Require(cfg.Spirals, TrackPatternType.Spiral);
            if (plan.Failed) return features;
            Require(cfg.Jumps, TrackPatternType.JumpGap);
            if (plan.Failed) return features;
            Require(cfg.FullPipes, TrackPatternType.FullPipe);
            if (plan.Failed) return features;
            Require(cfg.Camelbacks, TrackPatternType.Camelback);
            if (plan.Failed) return features;
            Require(cfg.HeartlineRolls, TrackPatternType.HeartlineRoll);
            if (plan.Failed) return features;
            Require(cfg.ZeroGRolls, TrackPatternType.ZeroGRoll);
            if (plan.Failed) return features;
            Require(cfg.Chicanes, TrackPatternType.Chicane);
            if (plan.Failed) return features;
            Require(cfg.SCurves, TrackPatternType.SCurve);
            if (plan.Failed) return features;

            // Required content must fit the available gaps AND the length budget —
            // otherwise fail loudly now with the numbers.
            if (features.Count > maxSlots)
            {
                plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                    $"Required features need {features.Count} gaps but only {maxSlots} are available between corners (after the closure reserve and branch minimums).");
                return features;
            }
            if (footprintUsed > lengthBudget)
            {
                plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                    $"Required features need ≈{footprintUsed / 1000f:F1}km but only ≈{Mathf.Max(0f, lengthBudget) / 1000f:F1}km of the length cap remains after corners, straights and branch minimums.");
                return features;
            }

            // 3. Optional content by relative weight up to the feature-group budget,
            //    capped by the gaps that actually exist on this candidate.
            int groupTarget = Mathf.Min(rng.NextInt(cfg.MinFeatureGroups, cfg.MaxFeatureGroups + 1), maxSlots);
            int safety = 32;
            while (features.Count < groupTarget && safety-- > 0)
            {
                var pool = new List<(TrackPatternType type, float w)>();
                if (cfg.Loops.Enabled && loopsUsed < cfg.Loops.MaximumCount) pool.Add((TrackPatternType.FullLoop, cfg.Loops.OptionalWeight));
                if (cfg.Corkscrews.Enabled && corksUsed < cfg.Corkscrews.MaximumCount) pool.Add((TrackPatternType.Corkscrew, cfg.Corkscrews.OptionalWeight));
                if (cfg.Spirals.Enabled && spiralsUsed < cfg.Spirals.MaximumCount) pool.Add((TrackPatternType.Spiral, cfg.Spirals.OptionalWeight));
                if (cfg.Jumps.Enabled && jumpsUsed < cfg.Jumps.MaximumCount) pool.Add((TrackPatternType.JumpGap, cfg.Jumps.OptionalWeight));
                if (cfg.FullPipes.Enabled && pipesUsed < cfg.FullPipes.MaximumCount) pool.Add((TrackPatternType.FullPipe, cfg.FullPipes.OptionalWeight));
                if (cfg.Camelbacks.Enabled && camelbacksUsed < cfg.Camelbacks.MaximumCount) pool.Add((TrackPatternType.Camelback, cfg.Camelbacks.OptionalWeight));
                if (cfg.HeartlineRolls.Enabled && heartlineRollsUsed < cfg.HeartlineRolls.MaximumCount) pool.Add((TrackPatternType.HeartlineRoll, cfg.HeartlineRolls.OptionalWeight));
                if (cfg.ZeroGRolls.Enabled && zeroGRollsUsed < cfg.ZeroGRolls.MaximumCount) pool.Add((TrackPatternType.ZeroGRoll, cfg.ZeroGRolls.OptionalWeight));
                if (cfg.Chicanes.Enabled && chicanesUsed < cfg.Chicanes.MaximumCount) pool.Add((TrackPatternType.Chicane, cfg.Chicanes.OptionalWeight));
                if (cfg.SCurves.Enabled && sCurvesUsed < cfg.SCurves.MaximumCount) pool.Add((TrackPatternType.SCurve, cfg.SCurves.OptionalWeight));

                if (cfg.EnforceProceduralRhythm && pool.Count > 1)
                {
                    var familyUse = new Dictionary<TrackEncounterFamily, int>();
                    for (int i = 0; i < features.Count; i++)
                    {
                        TrackEncounterFamily family = TrackRhythm.FamilyOf(features[i]);
                        familyUse.TryGetValue(family, out int used);
                        familyUse[family] = used + 1;
                    }

                    bool hasNonSCurveChoice = pool.Exists(option =>
                        TrackRhythm.FamilyOf(option.type) != TrackEncounterFamily.SCurveFlow);
                    familyUse.TryGetValue(TrackEncounterFamily.SCurveFlow, out int sFlowUsed);
                    int otherUsed = features.Count - sFlowUsed;
                    for (int i = pool.Count - 1; i >= 0; i--)
                    {
                        TrackEncounterFamily family = TrackRhythm.FamilyOf(pool[i].type);
                        // When S-flow is already the dominant optional language, force
                        // the next choice to another family if one is physically allowed.
                        if (family == TrackEncounterFamily.SCurveFlow && hasNonSCurveChoice &&
                            sFlowUsed > otherUsed)
                        {
                            pool.RemoveAt(i);
                            continue;
                        }

                        familyUse.TryGetValue(family, out int used);
                        float diversityWeight = 1f / (1f + used * 0.85f);
                        pool[i] = (pool[i].type, pool[i].w * diversityWeight);
                    }
                }

                if (pool.Count == 0) break;

                float total = 0f;
                foreach (var p in pool) total += p.w;
                if (total <= 0f) break;

                float pick = rng.NextFloat(0f, total);
                TrackPatternType chosen = pool[pool.Count - 1].type;
                foreach (var p in pool)
                {
                    pick -= p.w;
                    if (pick <= 0f) { chosen = p.type; break; }
                }

                // Compound upgrade: an optional slot may become an intentional compound.
                if (rng.NextFloat() < cfg.CompoundFeatureChance && cfg.MaxCompoundElements >= 2)
                {
                    TrackPatternType compound = chosen switch
                    {
                        TrackPatternType.FullLoop => TrackPatternType.LoopToCorkscrew,
                        TrackPatternType.Spiral => TrackPatternType.SpiralToCorkscrew,
                        TrackPatternType.Corkscrew => TrackPatternType.DoubleCorkscrew,
                        TrackPatternType.JumpGap => TrackPatternType.JumpToBankedLanding,
                        _ => chosen
                    };
                    if (compound != chosen &&
                        footprintUsed + cfg.EstimateFeatureFootprint(compound) <= lengthBudget &&
                        TryConsume(compound))
                    {
                        features.Add(compound);
                        Count(compound);
                        continue;
                    }
                }

                // Optional content stops at the length budget — never silently blows the cap.
                if (footprintUsed + cfg.EstimateFeatureFootprint(chosen) > lengthBudget) break;

                if (!TryConsume(chosen)) continue;
                features.Add(chosen);
                Count(chosen);
            }

            return features;
        }

        // ═══════════════════════════ 2b. Quarter partition ═══════════════════════════

        /// <summary>
        /// Divides the corner-plan gaps into the 4 quarters by cumulative estimated arc
        /// (quartile cuts) and selects which quarters are dual: overrides first, then the
        /// Quarter stream within the resolved count window, respecting eligibility
        /// (span size, no half-loop/wallride realizations inside, Q1/Q4 policies,
        /// adjacency) and per-quarter ballistic feasibility of the gate jumps.
        /// </summary>
        private void PartitionQuarters(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, List<CornerSlot> corners,
            bool[] dualMask, ref Unity.Mathematics.Random rng)
        {
            int n = corners.Count;
            plan.Quarters.Clear();

            if (n < 4)
            {
                plan.Fail(GenerationFailureReason.InvalidConfiguration,
                    $"The 4-quarter topology needs at least 4 corners; this plan has {n}.");
                return;
            }

            // Per-gap arc estimate = corner arc + one average straight.
            float avgStraight = (cfg.MinStraightLength + cfg.MaxStraightLength) * 0.5f;
            var estimate = new float[n];
            float total = 0f;
            for (int g = 0; g < n; g++)
            {
                float mag = corners[g].Magnitude;
                float radius = Mathf.Max(Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, mag / 180f), cfg.MinCurveRadius);
                estimate[g] = avgStraight + SectionFrameBuilders.EasedArcLength(mag, radius);
                total += estimate[g];
            }

            // Quartile boundaries over the cumulative estimate.
            var b = new int[5];
            b[0] = 0;
            b[4] = n;
            float prefix = 0f;
            int k = 1;
            for (int g = 0; g < n && k < 4; g++)
            {
                prefix += estimate[g];
                while (k < 4 && prefix >= total * k / 4f)
                {
                    b[k] = g + 1;
                    k++;
                }
            }
            for (int q = 1; q < 4; q++)
                b[q] = Mathf.Clamp(b[q] <= 0 ? q : b[q], b[q - 1] + 1, n - (4 - q));

            for (int q = 0; q < 4; q++)
                plan.Quarters.Add(new PlannedQuarter { Index = q, GapStart = b[q], GapEnd = b[q + 1] });

            if (cfg.MaxDualQuarters <= 0 && dualMask == null) return;

            // ── Eligibility ──
            float SpanEstimate(PlannedQuarter q)
            {
                float s = 0f;
                for (int g = q.GapStart; g < q.GapEnd; g++) s += estimate[g];
                return s;
            }

            bool Eligible(PlannedQuarter q, out string why)
            {
                why = null;
                if (q.GapEnd - q.GapStart < 2) { why = "span shorter than 2 gaps"; return false; }
                if (q.Index == 0 && !cfg.AllowQ1Dual) { why = "AllowQ1Dual is off (start line)"; return false; }
                if (q.Index == 3 && !cfg.AllowQ4Dual) { why = "AllowQ4Dual is off (finish line)"; return false; }
                if (cfg.QuarterTypeOverrides[q.Index] == QuarterTypeOverride.SingleRoad) { why = "forced SingleRoad"; return false; }
                for (int g = q.GapStart; g < q.GapEnd; g++)
                {
                    // Half-loop rollouts fly back OVER the ground path — road B cannot
                    // safely share that airspace. Wallrides and hairpins are ordinary
                    // corners for eligibility: road B is a separate chain and never
                    // touches their cross-section channels.
                    if (corners[g].IsHalfLoop) { why = "contains a half-loop reversal"; return false; }
                }
                // The gate suites are ADDED at the boundaries — they never consume span.
                // The span only needs enough content for a meaningful alternate road.
                if (SpanEstimate(q) < 3f * avgStraight)
                {
                    why = "estimated span too short for meaningful alternate-road content";
                    return false;
                }
                return true;
            }

            // ── Selection ──
            var wantDual = new bool[4];
            if (dualMask != null)
            {
                for (int q = 0; q < 4; q++)
                    wantDual[q] = dualMask[q] && Eligible(plan.Quarters[q], out _);
            }
            else
            {
                int want = Mathf.Clamp(rng.NextInt(cfg.MinDualQuarters, cfg.MaxDualQuarters + 1), 0, 4);

                bool AdjacencyOk(int q)
                {
                    if (!cfg.PreventAdjacentDualQuarters) return true;
                    return !wantDual[(q + 1) % 4] && !wantDual[(q + 3) % 4];
                }

                int selected = 0;

                // Forced duals first (resolution already warned when they exceed the cap).
                for (int q = 0; q < 4 && selected < cfg.MaxDualQuarters; q++)
                {
                    if (cfg.QuarterTypeOverrides[q] != QuarterTypeOverride.DualRoad) continue;
                    if (!Eligible(plan.Quarters[q], out string why))
                    {
                        plan.Warnings.Add($"Quarter {q} is forced Dual but not eligible ({why}) — staying Single.");
                        continue;
                    }
                    if (!AdjacencyOk(q)) continue;
                    wantDual[q] = true;
                    selected++;
                }

                // Preference order [1,2,3,0] shuffled by the Quarter stream: quarter 0 is
                // last so the start line stays off gate pieces unless everything is dual.
                var order = new List<int> { 1, 2, 3 };
                Shuffle(order, ref rng);
                order.Add(0);
                foreach (int q in order)
                {
                    if (selected >= want) break;
                    if (wantDual[q]) continue;
                    if (cfg.QuarterTypeOverrides[q] == QuarterTypeOverride.DualRoad) continue; // handled above
                    if (!Eligible(plan.Quarters[q], out _)) continue;
                    if (!AdjacencyOk(q)) continue;
                    wantDual[q] = true;
                    selected++;
                }

                // Falling short is a per-attempt fact the failure report needs to
                // explain — record WHY each quarter was passed over.
                if (selected < want)
                {
                    var reasons = new List<string>(4);
                    for (int q = 0; q < 4; q++)
                    {
                        if (wantDual[q]) continue;
                        reasons.Add(Eligible(plan.Quarters[q], out string why)
                            ? $"Q{q + 1} eligible but skipped (quota/adjacency)"
                            : $"Q{q + 1} {why}");
                    }
                    plan.Warnings.Add($"Dual selection reached {selected}/{want}: {string.Join("; ", reasons)}");
                }
            }

            // ── Gate ballistics per selected dual quarter ──
            foreach (var q in plan.Quarters)
            {
                if (!wantDual[q.Index]) continue;

                if (!TrySolveGateJumps(cfg, ref rng, out var entry, out var exit, out float laneSep, out string reason))
                {
                    plan.Warnings.Add($"Quarter {q.Index} demoted to SingleRoad: {reason}.");
                    continue;
                }

                q.Dual = true;
                q.EntryJump = entry;
                q.ExitJump = exit;
                q.LaneSeparation = laneSep;
            }
        }

        /// <summary>
        /// Solves the entry choice jump and exit convergence jump for one dual quarter.
        /// The lane separation is bounded by the craft's real lateral aim authority
        /// during the flight (0.35 · ½·a_lat·t²) — the player must be able to REACH
        /// either lane after committing in the air.
        /// </summary>
        private static bool TrySolveGateJumps(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng,
            out JumpBallistics.Solution entry, out JumpBallistics.Solution exit, out float laneSep, out string reason)
        {
            entry = default;
            exit = default;
            laneSep = 0f;
            reason = "";

            float AimAuthority(float airtime) => 0.35f * 0.5f * cfg.ReferenceLateralAcceleration * airtime * airtime;

            // The resolved lane separation already carries the road-envelope FLOOR —
            // lanes may never land closer (they would physically overlap). Derive the
            // airtime the aim demands and raise the ballistic draw floor to it, so
            // every solved jump reaches the lanes instead of gambling on a high roll.
            float authorityCoef = 0.35f * 0.5f * Mathf.Max(0.01f, cfg.ReferenceLateralAcceleration);
            float neededAirtime = Mathf.Sqrt(cfg.LaneSeparation * 0.5f * 1.1f / authorityCoef);
            if (neededAirtime > cfg.MaxJumpAirtimeSeconds)
            {
                reason = $"lanes {cfg.LaneSeparation:F0} m apart need {neededAirtime:F1}s of mid-air aim time but the maximum airtime is {cfg.MaxJumpAirtimeSeconds:F1}s";
                return false;
            }

            bool solvedEntry = false;
            for (int attempt = 0; attempt < 8 && !solvedEntry; attempt++)
            {
                if (!JumpBallistics.TrySolve(cfg, ref rng, out entry, neededAirtime)) continue;
                if (AimAuthority(entry.AirtimeSeconds) < cfg.LaneSeparation * 0.5f * 1.1f) continue;
                laneSep = cfg.LaneSeparation;
                solvedEntry = true;
            }
            if (!solvedEntry)
            {
                reason = $"no entry jump offers enough mid-air aim authority to reach lanes {cfg.LaneSeparation:F0} m apart";
                return false;
            }

            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (!JumpBallistics.TrySolve(cfg, ref rng, out exit, neededAirtime)) continue;
                // Converging flights: each lane covers laneSep/2 laterally onto the shared catch.
                if (AimAuthority(exit.AirtimeSeconds) < laneSep * 0.5f * 1.1f) continue;
                return true;
            }

            reason = "no exit jump solution lets both lanes converge onto the shared catch";
            return false;
        }

        // ═══════════════════════════ 3/4. Definition emission ═══════════════════════════

        private static bool IsCorkscrewFeature(TrackPatternType f)
            => f == TrackPatternType.Corkscrew || f == TrackPatternType.DoubleCorkscrew;

        /// <summary>
        /// Stage D interim capacity floor (deterministic, structural). A lead straight
        /// may be dropped only while enough full-length adjustable straights remain to
        /// keep the closure solver's lever budget healthy: at most one drop per quarter,
        /// and never below half of the non-reserved gaps kept as full levers. The full
        /// residual-aware capacity model (architecture §7) supersedes this later.
        /// </summary>
        private static bool CanDropLeadStraight(int cornerCount, int closureGaps, int gap, TopologyPlan plan)
        {
            int nonReserved = Mathf.Max(0, cornerCount - closureGaps);
            int floor = Mathf.CeilToInt(nonReserved * 0.5f);

            int quarter = -1;
            foreach (var q in plan.Quarters)
                if (gap >= q.GapStart && gap < q.GapEnd) { quarter = q.Index; break; }

            int dropsTotal = 0, dropsThisQuarter = 0;
            foreach (var w in plan.Warnings)
            {
                if (!w.StartsWith("[StageD] gap ")) continue;
                dropsTotal++;
                // "[StageD] gap N: ..." — cheap quarter attribution via re-scan.
                int g = ParseStageDGap(w);
                if (g < 0) continue;
                foreach (var q in plan.Quarters)
                    if (g >= q.GapStart && g < q.GapEnd) { if (q.Index == quarter) dropsThisQuarter++; break; }
            }

            if (dropsThisQuarter >= 1) return false;
            return (nonReserved - dropsTotal - 1) >= floor;
        }

        private static int ParseStageDGap(string warning)
        {
            const string key = "[StageD] gap ";
            int start = key.Length;
            int end = warning.IndexOf(':', start);
            if (end < 0) return -1;
            return int.TryParse(warning.Substring(start, end - start), out int g) ? g : -1;
        }

        /// <summary>
        /// Structural gap roads are route-layout and closure controls. Feature fitting
        /// may compact authored entry/recovery roads, but it must never shrink these
        /// solver corridors or the generator loses separation and closure authority.
        /// </summary>
        public static float ResolveStructuralLeadLength(
            ResolvedTrackGenerationConfig cfg,
            float pacedT)
        {
            if (cfg == null) return MinAdjustableStraight;
            return Mathf.Max(MinAdjustableStraight,
                Mathf.Lerp(cfg.MinStraightLength, cfg.MaxStraightLength,
                    Mathf.Clamp01(pacedT)));
        }

        private void EmitDefinitions(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, List<CornerSlot> corners,
            Dictionary<int, TrackPatternType> featureByGap, int closureGaps, PlanRandomStreams rngs)
        {
            var defs = plan.Defs;
            float width = cfg.RoadWidth;
            int patternCounter = 0;

            int QuarterOfGap(int gap)
            {
                foreach (var q in plan.Quarters)
                    if (gap >= q.GapStart && gap < q.GapEnd)
                        return q.Index;
                return 3;
            }

            void StampFrom(int fromIndex, int quarter)
            {
                for (int i = fromIndex; i < defs.Count; i++)
                    if (defs[i].QuarterIndex < 0)
                        defs[i].QuarterIndex = quarter;
            }

            for (int gap = 0; gap < corners.Count; gap++)
            {
                bool inClosureReserve = gap >= corners.Count - closureGaps;

                // Quarter gate suites at this boundary, BEFORE the gap's lead straight.
                // A quarter ending here emits its exit convergence first; a quarter
                // starting here emits its entry choice second — adjacent dual quarters
                // compose into the single-road neck: catch → recovery → approach → ramp.
                foreach (var q in plan.Quarters)
                    if (q.Dual && q.GapEnd == gap)
                        EmitQuarterExitSuite(cfg, q, defs);
                foreach (var q in plan.Quarters)
                    if (q.Dual && q.GapStart == gap)
                        EmitQuarterEntrySuite(cfg, q, defs);

                int quarter = QuarterOfGap(gap);
                int firstDef = defs.Count;

                // Pacing: straights alternate short/long more strongly with higher variation.
                float wave = Mathf.PingPong(gap * 0.618f, 1f);
                float pacedT = Mathf.Lerp(rngs.Layout.NextFloat(), wave, cfg.PacingVariation * 0.7f);
                float pacedLength = ResolveStructuralLeadLength(cfg, pacedT);

                // ── Stage D (experimental, opt-in): drop the mandatory lead straight to
                // a bare weld connector when a corkscrew flows out of the preceding
                // corner AND the closure capacity floor still holds. Default OFF —
                // ships identical to before. The rng draw above is UNCONDITIONAL so
                // enabling the flag never shifts the deterministic stream elsewhere.
                bool droppedLead = false;
                TrackPatternType droppedBefore = default;
                if (cfg.DynamicFeatureAdjacency && gap > 0 && !inClosureReserve &&
                    featureByGap.TryGetValue(gap, out var peekFeature) &&
                    IsCorkscrewFeature(peekFeature) &&
                    CanDropLeadStraight(corners.Count, closureGaps, gap, plan))
                {
                    pacedLength = Mathf.Max(cfg.MinimumConnectorLength, MinAdjustableStraight);
                    droppedLead = true;
                    droppedBefore = peekFeature;
                }

                // Every gap retains its own adjustable structural corridor. A preceding
                // feature recovery stays separate: it is authored riding space, while
                // this road is layout pitch and a closure-solver degree of freedom.
                var lead = SectionDefs.Straight(TrackMacroSectionType.Straight, pacedLength, width,
                    droppedLead ? $"CorkscrewWeld_{gap:D2}" : $"Straight_{gap:D2}",
                    fitRole: FeatureFitRole.StructuralConnector);
                lead.IsClosure = inClosureReserve;
                defs.Add(lead);
                if (droppedLead)
                {
                    lead.SemanticElement = Macro.SemanticElementId.ClosureTransfer;
                    plan.Warnings.Add($"[StageD] gap {gap}: lead straight dropped to weld connector before {droppedBefore} (capacity floor held)");
                }

                // Gap content: one feature pattern. Dual-quarter Road A is eligible;
                // the alternate-road fit and validators decide whether the pair is safe.
                if (!inClosureReserve && featureByGap.TryGetValue(gap, out TrackPatternType feature))
                {
                    string patternId = $"{feature}_{patternCounter++}";
                    int defsBefore = defs.Count;
                    bool hasPattern = FeaturePatternLibrary.TryGet(feature, out var pattern);
                    string planningFailure = "";
                    bool patternPlanned;
                    if (hasPattern && pattern is DefinitionNativePattern)
                    {
                        patternPlanned = TrackFeatureDefinitionCompiler.TryPlanPattern(
                            feature, cfg, patternId, ref rngs.Feature, defs,
                            out planningFailure);
                    }
                    else
                    {
                        patternPlanned = hasPattern &&
                                         pattern.TryPlan(cfg, patternId, ref rngs.Feature, defs);
                    }
                    if (!patternPlanned)
                    {
                        string detail = string.IsNullOrWhiteSpace(planningFailure)
                            ? ""
                            : $" {planningFailure}";
                        plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                            $"Pattern {feature} could not find a legal parameterization under the resolved config." +
                            detail);
                        return;
                    }
                    // Stage A: authoritative identity, stamped at emission (no behavior change).
                    FeaturePlanning.StampRange(defs, defsBefore,
                        FeaturePlanning.ElementOf(feature), cfg);
                    plan.CountPattern(feature);
                }

                // The corner that closes this gap.
                int cornerDefsBefore = defs.Count;
                EmitCorner(cfg, plan, corners[gap], patternCounter++, inClosureReserve, ref rngs.Feature);
                if (plan.Failed) return;

                // Stage A: corner-realization identity. Mirrors EmitCorner's own
                // dispatch: half-loop reservation → its pattern; special realization →
                // that element; default → hairpin at hairpin magnitude, else ordinary.
                var cornerSlot = corners[gap];
                SemanticElementId cornerElement = cornerSlot.IsHalfLoop
                    ? FeaturePlanning.ElementOf(cornerSlot.HalfLoopType)
                    : cornerSlot.IsSpecial
                        ? (cornerSlot.Realization == TrackPatternType.Corkscrew
                            ? SemanticElementId.DirectionalCorkscrew   // Stage F: barrel realizing a turn
                            : FeaturePlanning.ElementOf(cornerSlot.Realization))
                        : (cornerSlot.Magnitude >= 150
                            ? SemanticElementId.Hairpin
                            : SemanticElementId.OrdinaryCurve);
                FeaturePlanning.StampRange(defs, cornerDefsBefore, cornerElement, cfg);

                StampFrom(firstDef, quarter);
            }

            // Quarter 3's exit boundary IS the lap seam: its exit suite closes the def list.
            var last = plan.Quarters[3];
            if (last.Dual && last.GapEnd == corners.Count)
                EmitQuarterExitSuite(cfg, last, defs);
        }

        /// <summary>
        /// Entry gate suite of a dual quarter: broad approach (both landing lanes are
        /// readable before takeoff) → choice jump ramp → air gap (the canonical cursor
        /// lands on lane A, +laneSep/2 off the flight midline) → road A's landing flare.
        /// The approach and ramp belong to the PREVIOUS quarter ("Q2 ends with a jump").
        /// </summary>
        private static void EmitQuarterEntrySuite(ResolvedTrackGenerationConfig cfg, PlannedQuarter q,
            List<TrackMacroSectionDefinition> defs)
        {
            string zoneId = $"Quarter_{q.Index}";
            // The approach + choice ramp belong to the previous quarter — except at the
            // lap start, where quarter 0's own intro must not be stamped quarter 3
            // (sections would read as Q3 at lap progress 0).
            int prevQuarter = q.Index == 0 ? 0 : q.Index - 1;
            float catchWidth = cfg.QuarterCatchWidth;

            int before = defs.Count;
            defs.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.DefaultApproachLength, catchWidth,
                $"QuarterApproach_{q.Index}", locked: true,
                fitRole: FeatureFitRole.EntryWarmup));
            JumpGapPattern.EmitJumpRamp(in q.EntryJump, catchWidth, zoneId, "QuarterEntry_", defs);
            for (int i = before; i < defs.Count; i++) defs[i].QuarterIndex = prevQuarter;

            before = defs.Count;
            JumpGapPattern.EmitAirGap(in q.EntryJump, cfg.RoadWidth, zoneId, "QuarterEntry_", defs);
            defs[defs.Count - 1].PlanLateralOffset = q.LaneSeparation * 0.5f;
            JumpGapPattern.EmitLandingRamp(in q.EntryJump, cfg.RoadWidth, zoneId, "QuarterEntryFlareA_", defs);
            for (int i = before; i < defs.Count; i++) defs[i].QuarterIndex = q.Index;

            q.FirstDefIndex = before;
        }

        /// <summary>
        /// Exit gate suite of a dual quarter: road A's launch lip → air gap back to the
        /// flight midline (−laneSep/2) → ONE broad shared catch both lanes converge onto
        /// → recovery that narrows back to the ordinary road. Road B's fitted chain is
        /// inserted between the lip and the air gap later.
        /// </summary>
        private static void EmitQuarterExitSuite(ResolvedTrackGenerationConfig cfg, PlannedQuarter q,
            List<TrackMacroSectionDefinition> defs)
        {
            string zoneId = $"Quarter_{q.Index}";
            float catchWidth = cfg.QuarterCatchWidth;

            int before = defs.Count;
            JumpGapPattern.EmitJumpRamp(in q.ExitJump, cfg.RoadWidth, zoneId, "QuarterExitA_", defs);

            q.RoadBInsertIndex = defs.Count;

            JumpGapPattern.EmitAirGap(in q.ExitJump, catchWidth, zoneId, "QuarterExit_", defs);
            defs[defs.Count - 1].PlanLateralOffset = -q.LaneSeparation * 0.5f;
            JumpGapPattern.EmitLandingRamp(in q.ExitJump, catchWidth, zoneId, "QuarterExitCatch_", defs);
            defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.JumpRecoveryLength, cfg.RoadWidth,
                "PostCatchRecovery", locked: true,
                fitRole: FeatureFitRole.ExitRecovery));

            for (int i = before; i < defs.Count; i++) defs[i].QuarterIndex = q.Index;
            q.LastDefIndex = defs.Count - 1;
        }

        /// <summary>
        /// Post-closure/post-elevation quarter bookkeeping: defs inserted by the closure
        /// solver (S-bends) inherit their neighbor's quarter, and every dual quarter's
        /// gate def indices are re-located by scan (insertions shift raw indices).
        /// </summary>
        private static void StampQuarterIndices(TopologyPlan plan)
        {
            var defs = plan.Defs;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].QuarterIndex >= 0) continue;
                defs[i].QuarterIndex = i > 0 ? defs[i - 1].QuarterIndex : plan.Quarters[0].Index;
            }

            foreach (var q in plan.Quarters)
            {
                if (!q.Dual) continue;
                string zoneId = $"Quarter_{q.Index}";
                q.FirstDefIndex = -1;
                q.RoadBInsertIndex = -1;
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    if (d.PatternId != zoneId || d.SectionType != TrackMacroSectionType.AirGap) continue;
                    if (d.DebugName.StartsWith("QuarterEntry_")) q.FirstDefIndex = i;
                    else if (d.DebugName.StartsWith("QuarterExit_")) q.RoadBInsertIndex = i;
                }
            }
        }

        private static void EmitCorner(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, CornerSlot corner,
            int patternCounter, bool inClosureReserve, ref Unity.Mathematics.Random rng)
        {
            var defs = plan.Defs;
            float width = cfg.RoadWidth;

            if (corner.IsHalfLoop)
            {
                string patternId = $"{corner.HalfLoopType}_{patternCounter}";
                if (!FeaturePatternLibrary.TryGet(corner.HalfLoopType, out var pattern) ||
                    !pattern.TryPlan(cfg, patternId, ref rng, defs))
                {
                    plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                        $"Half-loop pattern {corner.HalfLoopType} could not be planned.");
                    return;
                }
                // Tag the reversal sign on the half-loop def for the 2D walk.
                for (int i = defs.Count - 1; i >= 0; i--)
                {
                    if ((defs[i].SectionType == TrackMacroSectionType.HalfLoopTwist ||
                         defs[i].SectionType == TrackMacroSectionType.RotationalEvent) &&
                        defs[i].PatternId == patternId)
                    {
                        defs[i].TurnAngle = 180f;
                        break;
                    }
                }
                plan.CountPattern(corner.HalfLoopType);
                return;
            }

            int angle = corner.Magnitude;
            int sign = corner.Sign;
            SectionTurnDirection dir = sign >= 0 ? SectionTurnDirection.Right : SectionTurnDirection.Left;

            // Sharper corners bind to the tighter end of the radius band.
            float RadiusFor(float mag, float scale = 1f) => Mathf.Max(
                Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, mag / 180f) * scale,
                cfg.MinCurveRadius);

            TrackMacroSectionDefinition Corner(float mag, float radius, string name, bool hairpin = false)
            {
                if (!hairpin)
                    radius = SectionFrameBuilders.ClampOrdinaryCurveRadius(mag, radius);
                return new TrackMacroSectionDefinition
                {
                    SectionType = hairpin ? TrackMacroSectionType.BankedHairpin : TrackMacroSectionType.BankedCurve,
                    Length = SectionFrameBuilders.EasedArcLength(mag, radius),
                    Width = width,
                    Direction = dir,
                    TurnAngle = mag,
                    Radius = radius,
                    BankingAngle = SectionDefs.RecommendedBank(cfg, radius),
                    SpeedIntent = hairpin ? SectionSpeedIntent.Slow : (mag >= 90 ? SectionSpeedIntent.Medium : SectionSpeedIntent.Fast),
                    RiskLevel = hairpin ? SectionRiskLevel.Risky : SectionRiskLevel.Normal,
                    RequiresRecoveryAfter = hairpin,
                    DebugName = name,
                    IsClosure = inClosureReserve, // closure-reserve corners may flex across the full rulebook radius band
                    Contract = SectionConnectionContract.Level(sign * mag)
                };
            }

            if (corner.DefinitionRealization != SemanticElementId.None)
            {
                string pid = $"{corner.DefinitionRealization}_{patternCounter}";
                if (!TrackFeatureDefinitionCompiler.TryPlanTurn(
                        corner.DefinitionRealization, cfg, sign * angle, pid, defs,
                        out string definitionFailure))
                {
                    plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                        $"Definition-native turn {corner.DefinitionRealization} could not realize " +
                        $"the requested {sign * angle:+0;-0} degree heading: {definitionFailure}");
                }
                return;
            }

            switch (corner.IsSpecial ? corner.Realization : TrackPatternType.SCurve /* marker for plain */)
            {
                case TrackPatternType.WallrideTurn:
                {
                    // Tighter end of the radius band: a wallride wants real lateral
                    // demand, and the wall provides the support the bank cannot.
                    float radius = RadiusFor(angle, 0.9f);
                    var def = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.WallrideTurn,
                        Length = SectionFrameBuilders.EasedArcLength(angle, radius),
                        Width = width,
                        Direction = dir,
                        TurnAngle = angle,
                        Radius = radius,
                        BankingAngle = Mathf.Min(SectionDefs.RecommendedBank(cfg, radius) * 1.15f, cfg.MaxBankAngle),
                        SpeedIntent = SectionSpeedIntent.Fast,
                        RiskLevel = SectionRiskLevel.Risky,
                        RequiresRecoveryAfter = true,
                        LockLength = true,
                        PatternId = $"Wallride_{patternCounter}",
                        DebugName = $"Wallride_{angle}deg_{dir}",
                        IsClosure = false,
                        Contract = SectionConnectionContract.Level(sign * angle)
                    };
                    defs.Add(def);
                    defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, width,
                        "RecoveryStraight", locked: true, def.PatternId));
                    break;
                }
                case TrackPatternType.Hairpin:
                {
                    var def = Corner(angle, RadiusFor(angle, 0.85f), $"Hairpin_{angle}deg_{dir}", hairpin: true);
                    def.PatternId = $"Hairpin_{patternCounter}";
                    defs.Add(def);
                    defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, width,
                        "RecoveryStraight", locked: true, def.PatternId));
                    break;
                }
                case TrackPatternType.SweeperIntoHairpin:
                {
                    string pid = $"SweeperIntoHairpin_{patternCounter}";
                    int sweep = Mathf.Clamp(Mathf.RoundToInt(angle * 0.3f / 5f) * 5, 20, 60);
                    int pin = angle - sweep;
                    if (pin < 120) { sweep = Mathf.Max(20, angle - 150); pin = angle - sweep; }
                    var sweeper = Corner(sweep, RadiusFor(45f), $"Sweeper_{sweep}deg_{dir}");
                    sweeper.PatternId = pid;
                    defs.Add(sweeper);
                    defs.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.GenericTransitionLength, width,
                        "SweeperLink", locked: true, pid));
                    var pinDef = Corner(pin, RadiusFor(pin, 0.85f), $"Hairpin_{pin}deg_{dir}", hairpin: true);
                    pinDef.PatternId = pid;
                    defs.Add(pinDef);
                    defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, width,
                        "RecoveryStraight", locked: true, pid));
                    break;
                }
                case TrackPatternType.DoubleApex:
                {
                    string pid = $"DoubleApex_{patternCounter}";
                    int half = angle / 2;
                    float radius = RadiusFor(angle, 1.05f);
                    var a = Corner(half, radius, $"DoubleApexIn_{half}deg_{dir}");
                    a.PatternId = pid;
                    defs.Add(a);
                    defs.Add(SectionDefs.Straight(TrackMacroSectionType.Straight, cfg.GenericTransitionLength * 0.5f, width,
                        "ApexLink", locked: true, pid));
                    var b = Corner(angle - half, radius, $"DoubleApexOut_{angle - half}deg_{dir}");
                    b.PatternId = pid;
                    defs.Add(b);
                    break;
                }
                case TrackPatternType.TighteningCorner:
                case TrackPatternType.OpeningCorner:
                {
                    bool tightening = corner.Realization == TrackPatternType.TighteningCorner;
                    string pid = $"{corner.Realization}_{patternCounter}";
                    int first = Mathf.RoundToInt(angle * 0.45f);
                    float r1 = RadiusFor(angle) * (tightening ? 1.4f : 0.8f);
                    float r2 = RadiusFor(angle) * (tightening ? 0.8f : 1.4f);
                    r1 = Mathf.Max(r1, cfg.MinCurveRadius);
                    r2 = Mathf.Max(r2, cfg.MinCurveRadius);
                    var a = Corner(first, r1, $"{(tightening ? "Tighten" : "Open")}In_{first}deg_{dir}");
                    a.PatternId = pid;
                    defs.Add(a);
                    var b = Corner(angle - first, r2, $"{(tightening ? "Tighten" : "Open")}Out_{angle - first}deg_{dir}");
                    b.PatternId = pid;
                    defs.Add(b);
                    break;
                }
                case TrackPatternType.Corkscrew:
                {
                    // Stage F: directional corkscrew realizing this corner. The barrel's
                    // axis turns by sign*angle; MakeCorkscrewDef stamps a plan-view whose
                    // TurnAngle equals that, so the corner's heading contribution — and lap
                    // closure — is unchanged from the ordinary curve it replaces.
                    //
                    // Roll HANDEDNESS is a safety choice, not a random one. At the barrel's
                    // mid-phase the road is inverted and the turn's lateral load peaks; the
                    // wrong roll sign tilts that load off the road and ejects the craft.
                    // Build both handednesses from the SAME rng state and keep the one that
                    // presses the craft onto the road hardest (max of the min floor margin).
                    string pid = $"DirectionalCorkscrew_{patternCounter}";
                    float rollMag = cfg.CorkscrewRollDegrees;
                    var rngSaved = rng;
                    var rollPlus = CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, +rollMag, pid, sign * angle);
                    rng = rngSaved;
                    var rollMinus = CorkscrewPattern.MakeCorkscrewDef(cfg, ref rng, -rollMag, pid, sign * angle);
                    float marginPlus = DirectionalCorkscrewFloorMargin(rollPlus, cfg);
                    float marginMinus = DirectionalCorkscrewFloorMargin(rollMinus, cfg);
                    float bestMargin = Mathf.Max(marginPlus, marginMinus);

                    if (bestMargin <= 0f)
                    {
                        // Neither handedness keeps the craft on the road through the turn —
                        // degrade to an ordinary curve rather than emit an ejector. Closure
                        // is unaffected (same heading token).
                        defs.Add(Corner(angle, RadiusFor(angle), $"BankedCurve_{angle}deg_{dir}"));
                        break;
                    }

                    var dco = marginPlus >= marginMinus ? rollPlus : rollMinus;
                    defs.Add(dco);
                    defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, width,
                        "RecoveryStraight", locked: true, pid));
                    plan.CountPattern(TrackPatternType.Corkscrew);
                    break;
                }
                default:
                {
                    bool hairpin = !corner.ForceOrdinary && angle >= 150;
                    var def = Corner(angle, RadiusFor(angle, hairpin ? 0.85f : 1f),
                        $"{(hairpin ? "BankedHairpin" : "BankedCurve")}_{angle}deg_{dir}", hairpin);
                    defs.Add(def);
                    if (hairpin)
                        defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.DefaultRecoveryLength, width,
                            "RecoveryStraight", locked: true));
                    break;
                }
            }
        }

        /// <summary>
        /// Builds one requested realization through the same authoritative emitter used by
        /// normal generation, then measures its achieved geometry from the live slot entry.
        /// Turn slots use <see cref="EmitCorner"/>; heading-neutral and direction-neutral
        /// slots use <see cref="FeaturePatternLibrary"/>.
        /// This is a deterministic, zero-scene-mutation candidate source for Track Editor V2.
        /// Clearance, connector solving, driveability, and transactional application remain
        /// separate gates; this method only proves what geometry the authoritative builder emits.
        /// </summary>
        public static FeaturePlanResult BuildReplacementCandidate(
            ResolvedTrackGenerationConfig cfg,
            TopologySlotRecord slot,
            SemanticElementId realization,
            in TrackConnectionFrame entry)
        {
            FeaturePlanResult Failed(string reason) => new FeaturePlanResult
            {
                Element = realization,
                PatternId = slot?.TopologySlotId ?? "",
                FailureReason = reason
            };

            if (cfg == null) return Failed("No resolved track configuration is available.");
            if (slot == null || string.IsNullOrWhiteSpace(slot.TopologySlotId))
                return Failed("The replacement candidate has no stable topology segment.");
            if (slot.DemandType == TopologyRole.HeadingNeutral ||
                slot.DemandType == TopologyRole.DirectionNeutralTransfer)
                return BuildPatternReplacementCandidate(cfg, slot, realization, entry);
            if (slot.DemandType != TopologyRole.TurnRealization)
                return Failed($"{slot.DemandType} does not yet have an authoritative replacement builder.");

            int signedAngle = Mathf.RoundToInt(slot.SignedHeadingDelta);
            if (signedAngle == 0)
                return Failed("A turn realization requires a non-zero heading demand.");

            var corner = new CornerSlot { SignedAngle = signedAngle };
            if (TrackFeatureDefinitionCatalog.TryGet(
                    realization, out TrackFeatureDefinition authoredDefinition) &&
                authoredDefinition.Enabled &&
                authoredDefinition.TopologyRole == TopologyRole.TurnRealization)
            {
                corner.IsSpecial = true;
                corner.DefinitionRealization = realization;
            }
            else switch (realization)
            {
                case SemanticElementId.OrdinaryCurve:
                    corner.ForceOrdinary = true;
                    break;
                case SemanticElementId.DoubleApex:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.DoubleApex;
                    break;
                case SemanticElementId.TighteningCorner:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.TighteningCorner;
                    break;
                case SemanticElementId.OpeningCorner:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.OpeningCorner;
                    break;
                case SemanticElementId.Hairpin:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.Hairpin;
                    break;
                case SemanticElementId.SweeperIntoHairpin:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.SweeperIntoHairpin;
                    break;
                case SemanticElementId.WallrideTurn:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.WallrideTurn;
                    break;
                case SemanticElementId.DirectionalCorkscrew:
                    corner.IsSpecial = true;
                    corner.Realization = TrackPatternType.Corkscrew;
                    break;
                case SemanticElementId.Immelmann:
                    corner.IsHalfLoop = true;
                    corner.HalfLoopType = TrackPatternType.HalfLoopRollout;
                    break;
                case SemanticElementId.HalfLoopToCorkscrew:
                    corner.IsHalfLoop = true;
                    corner.HalfLoopType = TrackPatternType.HalfLoopToCorkscrew;
                    break;
                default:
                    return Failed($"{realization} is not an authored turn realization in the current generator.");
            }

            var plan = new TopologyPlan();
            uint seed = StableReplacementSeed(slot.TopologySlotId, realization);
            var rng = new Unity.Mathematics.Random(seed);
            int patternCounter = Mathf.Max(0, slot.CanonicalOrder);
            EmitCorner(cfg, plan, corner, patternCounter, false, ref rng);
            if (plan.Failed)
                return Failed(string.IsNullOrWhiteSpace(plan.FailureMessage)
                    ? $"{realization} could not produce legal corner geometry."
                    : plan.FailureMessage);
            if (plan.Defs.Count == 0)
                return Failed($"{realization} produced no corner definitions.");

            if (realization == SemanticElementId.DirectionalCorkscrew &&
                !plan.Defs.Exists(definition => definition != null &&
                    definition.SectionType == TrackMacroSectionType.RotationalEvent))
                return Failed("Directional corkscrew safety rejected both handednesses for this configuration; " +
                              "the normal generator would keep an ordinary curve here.");

            FeaturePlanning.StampRange(plan.Defs, 0, realization, cfg);
            FeaturePlanResult result = FeaturePlanning.ComputePlanResult(
                plan.Defs, 0, plan.Defs.Count, entry, FrameBuildContext.From(cfg),
                realization, $"replacement:{slot.TopologySlotId}:{realization}");
            // At an exact reversal the final forward vector alone cannot encode
            // whether the path arrived through +180° or -180°. This replacement
            // boundary owns the signed topology demand, so use it to disambiguate
            // only that mathematically identical endpoint.
            if (!result.Failed &&
                Mathf.Abs(Mathf.Abs(slot.SignedHeadingDelta) - 180f) <= 0.25f &&
                Mathf.Abs(Mathf.Abs(result.HeadingContributionDeg) - 180f) <= 0.25f)
            {
                result.HeadingContributionDeg = Mathf.Sign(slot.SignedHeadingDelta) *
                                                Mathf.Abs(result.HeadingContributionDeg);
            }
            return result;
        }

        private static FeaturePlanResult BuildPatternReplacementCandidate(
            ResolvedTrackGenerationConfig cfg,
            TopologySlotRecord slot,
            SemanticElementId realization,
            in TrackConnectionFrame entry)
        {
            FeaturePlanResult Failed(string reason) => new FeaturePlanResult
            {
                Element = realization,
                PatternId = slot?.TopologySlotId ?? "",
                FailureReason = reason
            };

            TrackPatternType patternType;
            switch (realization)
            {
                case SemanticElementId.VerticalLoop: patternType = TrackPatternType.FullLoop; break;
                case SemanticElementId.InlineCorkscrew: patternType = TrackPatternType.Corkscrew; break;
                case SemanticElementId.DoubleCorkscrew: patternType = TrackPatternType.DoubleCorkscrew; break;
                case SemanticElementId.Spiral: patternType = TrackPatternType.Spiral; break;
                case SemanticElementId.HalfHelixTurnaround:
                    patternType = TrackPatternType.HalfHelixTurnaround;
                    break;
                case SemanticElementId.LoopToCorkscrew: patternType = TrackPatternType.LoopToCorkscrew; break;
                case SemanticElementId.SpiralToCorkscrew: patternType = TrackPatternType.SpiralToCorkscrew; break;
                case SemanticElementId.JumpGap: patternType = TrackPatternType.JumpGap; break;
                case SemanticElementId.JumpToBankedLanding: patternType = TrackPatternType.JumpToBankedLanding; break;
                case SemanticElementId.FullPipe: patternType = TrackPatternType.FullPipe; break;
                case SemanticElementId.SCurve: patternType = TrackPatternType.SCurve; break;
                case SemanticElementId.Chicane: patternType = TrackPatternType.Chicane; break;
                case SemanticElementId.AlternatingRadiusSequence:
                    patternType = TrackPatternType.AlternatingRadiusSequence;
                    break;
                case SemanticElementId.Camelback: patternType = TrackPatternType.Camelback; break;
                case SemanticElementId.HeartlineRoll: patternType = TrackPatternType.HeartlineRoll; break;
                case SemanticElementId.ZeroGRoll: patternType = TrackPatternType.ZeroGRoll; break;
                default:
                    return Failed($"{realization} is not an authored feature-pattern realization in the current generator.");
            }

            if (!FeaturePatternLibrary.TryGet(patternType, out ITrackFeaturePattern pattern))
                return Failed($"The authoritative {patternType} pattern is not registered.");

            string patternId = $"replacement:{slot.TopologySlotId}:{realization}";
            uint seed = StableReplacementSeed(slot.TopologySlotId, realization);
            var rng = new Unity.Mathematics.Random(seed);
            var definitions = new List<TrackMacroSectionDefinition>();
            if (!pattern.TryPlan(cfg, patternId, ref rng, definitions) || definitions.Count == 0)
                return Failed($"{realization} could not produce a legal parameterization under the resolved track settings.");

            FeaturePlanning.StampRange(definitions, 0, realization, cfg);
            return FeaturePlanning.ComputePlanResult(
                definitions, 0, definitions.Count, entry, FrameBuildContext.From(cfg),
                realization, patternId);
        }

        private static uint StableReplacementSeed(string slotId, SemanticElementId realization)
        {
            // FNV-1a is intentionally used instead of string.GetHashCode(), whose result may
            // differ between runtimes. The same slot choice therefore builds identically on
            // every client before its concrete parameters are written into the recipe.
            uint hash = 2166136261u;
            string text = (slotId ?? "") + "|" + (int)realization;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash == 0u ? 1u : hash;
        }

        /// <summary>
        /// Worst-case floor-containment margin (m/s²) of a directional corkscrew: builds its
        /// real frames and, at every ring, measures how hard the effective load (centripetal
        /// from the built curvature + gravity) presses the craft onto the road surface. A
        /// positive minimum means the craft stays pressed against the road all the way
        /// through — including the inverted mid-barrel where the turn's lateral load peaks.
        /// A negative value means the load tilts off the surface and the craft is ejected.
        /// Convention-free: derived from Position/Forward/Up, so it needs no hardcoded sign.
        /// </summary>
        public static float DirectionalCorkscrewFloorMargin(TrackMacroSectionDefinition def,
            ResolvedTrackGenerationConfig cfg)
        {
            var frames = SectionFrameBuilders.BuildRotationalEvent(
                TrackConnectionFrame.Origin(def.Width), def, FrameBuildContext.From(cfg));
            if (frames == null || frames.Length < 3) return float.NegativeInfinity;

            // The craft meets a corkscrew below top speed — the same fraction the barrel is
            // sized against, so the containment test and the barrel sizing agree.
            float v = Mathf.Max(1f, cfg.DesignSpeedMps * CorkscrewPattern.CorkscrewHoldSpeedFraction);
            float g = Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
            Vector3 gVec = Vector3.up * -g;

            float minMargin = float.MaxValue;
            for (int i = 1; i < frames.Length - 1; i++)
            {
                float ds = 0.5f * (frames[i + 1].Position - frames[i - 1].Position).magnitude;
                if (ds < 1e-3f) continue;
                // v²·dT/ds ≈ centripetal acceleration vector (magnitude v²κ toward the centre
                // of curvature). Captures the TOTAL curvature — barrel orbit, turn, elevation.
                Vector3 aC = (v * v) * (frames[i + 1].Forward - frames[i - 1].Forward) / (2f * ds);
                Vector3 support = aC - gVec;               // force/mass the road must supply
                float margin = Vector3.Dot(support, frames[i].Up); // >0 ⇒ pressed onto the floor
                if (margin < minMargin) minMargin = margin;
            }
            return minMargin == float.MaxValue ? float.NegativeInfinity : minMargin;
        }

        // ═══════════════════════════ 5. 2D closure solve ═══════════════════════════

        /// <summary>
        /// Adjusts LEGAL closure primitives so the 2D walk returns exactly to the origin:
        /// adjustable straight lengths, plain corner radii (broad closure curves), and —
        /// when the layout's directions are nearly collinear (switchback laps) — analytic
        /// closure S-bends inserted into closure-reserve straights for perpendicular
        /// capacity. This is plan-time selection, never post-hoc warping of built
        /// geometry. Heading closes exactly by corner-sum construction.
        /// </summary>
        private void SolveClosure2D(ResolvedTrackGenerationConfig cfg, TopologyPlan plan)
        {
            var defs = plan.Defs;
            float initialGap = -1f;
            int straightCount = 0, curveCount = 0;

            for (int round = 0; round <= MaxClosureRescueRounds; round++)
            {
                var straightIdx = new List<int>();
                var straightDirs = new List<Vector2>();
                var cornerIdx = new List<int>();
                var cornerDirs = new List<Vector2>();
                var cornerBaseRadius = new List<float>();
                CollectClosureVariables(defs, straightIdx, straightDirs, cornerIdx, cornerDirs, cornerBaseRadius);
                straightCount = straightIdx.Count;
                curveCount = cornerIdx.Count;

                if (straightIdx.Count < 3)
                {
                    plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                        $"Only {straightIdx.Count} adjustable straights remain for closure — the feature plan consumed the closure reserve.");
                    return;
                }

                // Re-fit after every rescue insertion as well as on the first round.
                // An S-bend replaces a straight run but its two eased arcs can still
                // add net lap length; carrying that excess into the solve used to let
                // several individually legal radii combine into a 60–100km lap.
                if ((round == 0 || TotalPlanLength(defs) > cfg.MaxTrackLength * 0.95f) &&
                    !FitLengthBudget(cfg, plan, defs, straightIdx, cornerIdx, cornerBaseRadius))
                    return;

                Vector2 gap = -RewalkEndPos(defs);
                if (initialGap < 0f) initialGap = gap.magnitude;

                Vector2 predictedGap = RunActiveSetSolve(cfg, defs, straightIdx, straightDirs,
                    cornerIdx, cornerDirs, cornerBaseRadius, gap);
                // Curves are exactly linear in radius. Elevated straights are not quite
                // linear in physical length, so always trust a fresh exact walk here.
                gap = -RewalkEndPos(defs);

                // Closure angle relief (§7 extension): when radius/straight authority leaves
                // a residual, nudge plain corner angles in equal-and-opposite pairs (net
                // heading, and any canonical token, preserved) to absorb it before an S-bend.
                if (cfg.ClosureAngleRelief && gap.magnitude > cfg.ClosurePositionTolerance)
                {
                    AngleReliefAttempts++;
                    if (TryCloseWithAngleRelief(cfg, defs, cfg.ClosurePositionTolerance))
                        AngleReliefClosures++;
                    gap = -RewalkEndPos(defs);
                }

                if (gap.magnitude <= cfg.ClosurePositionTolerance)
                {
                    float total = TotalPlanLength(defs);
                    if (total > cfg.MaxTrackLength)
                    {
                        // The solver may have re-grown straights past the cap while
                        // closing position — shrink back to budget and solve again
                        // instead of failing a positionally perfect lap.
                        if (round < MaxClosureRescueRounds)
                        {
                            if (!FitLengthBudget(cfg, plan, defs, straightIdx, cornerIdx, cornerBaseRadius))
                                return;
                            continue;
                        }
                        plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                            $"Solved lap length {total / 1000f:F1}km exceeds the maximum {cfg.MaxTrackLength / 1000f:F1}km.");
                    }
                    return;
                }

                // If the linearized solve thought it had closed, first refine with
                // freshly collected post-elevation derivatives instead of spending a
                // new S-bend on numeric linearization error.
                if (predictedGap.magnitude <= cfg.ClosurePositionTolerance && round < 3)
                    continue;

                if (round == MaxClosureRescueRounds ||
                    !TryInsertClosureSBend(cfg, defs, straightIdx, straightDirs, gap))
                {
                    plan.Fail(GenerationFailureReason.ClosurePositionFailure,
                        $"2D closure residual {gap.magnitude:F1}m (initial {initialGap:F0}m) exceeds tolerance with {straightCount} straights + {curveCount} closure curves (S-bend rescue exhausted).");
                    return;
                }
            }
        }

        /// <summary>Walks the plan once, recording every legal closure variable and its end-position derivative.</summary>
        private static void CollectClosureVariables(List<TrackMacroSectionDefinition> defs,
            List<int> straightIdx, List<Vector2> straightDirs,
            List<int> cornerIdx, List<Vector2> cornerDirs, List<float> cornerBaseRadius)
        {
            Vector2 pos = Vector2.zero;
            float heading = 0f;

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d.RoadId == 1) continue; // alternate roads never drive the canonical walk
                Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);

                switch (d.SectionType)
                {
                    case TrackMacroSectionType.Loop:
                    {
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        pos += fwd * SectionFrameBuilders.LoopForwardDisplacement(d.Length)
                             + right * SectionFrameBuilders.LoopLateralOffset(d.Width);
                        break;
                    }
                    case TrackMacroSectionType.Spiral:
                        SectionFrameBuilders.ApplySpiral2D(ref pos, ref heading, d);
                        break;
                    case TrackMacroSectionType.HalfLoopTwist:
                    {
                        float halfArc = SectionFrameBuilders.HalfLoopArcLength(d.Radius);
                        pos += fwd * SectionFrameBuilders.HalfLoopForwardDisplacement(halfArc);
                        heading += 180f;
                        pos += SectionFrameBuilders.HeadingToDir(heading) * Mathf.Max(0f, d.Length - halfArc);
                        break;
                    }
                    case TrackMacroSectionType.RotationalEvent:
                    {
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        pos += fwd * d.PlanHorizontalLength + right * d.PlanLateralOffset;
                        heading += d.TurnAngle;
                        break;
                    }
                    case TrackMacroSectionType.SCurve:
                    {
                        // Closure S-bends remain legal solve variables after insertion.
                        // Their two opposed eased arcs keep net heading at zero, and
                        // their complete end offset is exactly linear in radius.
                        if (d.IsClosure)
                        {
                            Vector2 right = new Vector2(fwd.y, -fwd.x);
                            Vector2 unitOffset = SectionFrameBuilders.EasedSBendUnitOffset(d.TurnAngle);
                            cornerIdx.Add(i);
                            cornerDirs.Add(fwd * unitOffset.x + right * (d.TurnSign * unitOffset.y));
                            cornerBaseRadius.Add(d.Radius);
                        }
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, -d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    }
                    case TrackMacroSectionType.Chicane:
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, -2f * d.TurnAngle * d.TurnSign, d.Radius);
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                    case TrackMacroSectionType.WallrideTurn:
                    {
                        // Plain corners double as CLOSURE CURVES: an eased arc's
                        // displacement is still exactly linear in its radius while the
                        // heading delta stays fixed, so the unit-radius end offset is the
                        // derivative. Corners inside atomic patterns (incl. wallrides)
                        // are never touched.
                        if (string.IsNullOrEmpty(d.PatternId))
                        {
                            Vector2 right = new Vector2(fwd.y, -fwd.x);
                            Vector2 unitOffset = SectionFrameBuilders.EasedArcEndOffset(d.TurnAngle, 1f);
                            cornerIdx.Add(i);
                            cornerDirs.Add(fwd * unitOffset.x + right * (d.TurnSign * unitOffset.y));
                            cornerBaseRadius.Add(d.Radius);
                        }
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    }
                    default:
                    {
                        pos += fwd * d.HorizontalRun;
                        // Dual-quarter choice/convergence gaps land laterally off the
                        // flight midline (lane A in, midline out).
                        if (d.PlanLateralOffset != 0f)
                            pos += new Vector2(fwd.y, -fwd.x) * d.PlanLateralOffset;

                        bool adjustable = !d.LockLength &&
                            (d.SectionType == TrackMacroSectionType.Straight ||
                             d.SectionType == TrackMacroSectionType.WideStraight ||
                             d.SectionType == TrackMacroSectionType.BoostStraight);
                        if (adjustable)
                        {
                            straightIdx.Add(i);
                            straightDirs.Add(fwd);
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>Total planned lap length. Counts the canonical road only — the player
        /// rides ONE road through a dual quarter, so road B never adds lap length.</summary>
        private static float TotalPlanLength(List<TrackMacroSectionDefinition> defs)
        {
            float t = 0f;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].RoadId == 1) continue;
                t += defs[i].Length;
            }
            return t;
        }

        /// <summary>
        /// Pre-shrinks adjustable straights (and, when that is not enough, plain corner
        /// radii) when the plan overshoots the cap. Returns false on a hard budget failure.
        /// </summary>
        private bool FitLengthBudget(ResolvedTrackGenerationConfig cfg, TopologyPlan plan,
            List<TrackMacroSectionDefinition> defs, List<int> straightIdx, List<int> cornerIdx, List<float> cornerBaseRadius)
        {
            float total = TotalPlanLength(defs);
            float budgetCeiling = cfg.MaxTrackLength * 0.95f; // bounded solver below preserves the hard cap
            if (total <= budgetCeiling) return true;

            float FloorOf(TrackMacroSectionDefinition d) => Mathf.Max(MinAdjustableStraight, d.MinimumLength);

            float adjustableTotal = 0f;
            float floorTotal = 0f;
            foreach (int idx in straightIdx)
            {
                adjustableTotal += defs[idx].Length;
                floorTotal += FloorOf(defs[idx]);
            }
            float excess = total - budgetCeiling;
            float straightShrinkable = Mathf.Max(0f, adjustableTotal - floorTotal);

            float cornerShrinkable = 0f;
            for (int c = 0; c < cornerIdx.Count; c++)
            {
                var def = defs[cornerIdx[c]];
                float lengthScale = def.SectionType == TrackMacroSectionType.SCurve ? 2f : 1f;
                float minimumRadius = Mathf.Min(def.Radius,
                    cfg.LegalCurveRadiusFloor);
                cornerShrinkable += lengthScale *
                    (SectionFrameBuilders.EasedArcLength(def.TurnAngle, def.Radius) -
                     SectionFrameBuilders.EasedArcLength(def.TurnAngle, minimumRadius));
            }

            // Feasibility is judged against the CEILING, not the cap: plans that can
            // only squeeze between ceiling and cap leave the closure solver no growth
            // room and die expensively downstream (as ClosurePositionFailure or
            // solved-over-cap) — measured 90%→82% on Balanced when this was relaxed.
            if (excess > straightShrinkable + cornerShrinkable)
            {
                float lockedLength = total - adjustableTotal - cornerShrinkable;
                float minimumLegalLength = lockedLength + floorTotal;
                plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                    $"Minimum legal lap is {minimumLegalLength / 1000f:F1}km " +
                    $"(locked content {lockedLength / 1000f:F1}km + adjustable-road floors {floorTotal / 1000f:F1}km), " +
                    $"but the closure planning ceiling is {budgetCeiling / 1000f:F1}km " +
                    $"inside the {cfg.MaxTrackLength / 1000f:F1}km hard cap.");
                return false;
            }

            float fromStraights = Mathf.Min(excess, straightShrinkable);
            if (fromStraights > 0f)
            {
                float scale = 1f - fromStraights / Mathf.Max(1f, straightShrinkable);
                foreach (int idx in straightIdx)
                {
                    float floor = FloorOf(defs[idx]);
                    defs[idx].Length = Mathf.Max(floor, floor + (defs[idx].Length - floor) * scale);
                }
            }

            float fromCorners = excess - fromStraights;
            if (fromCorners > 0f && cornerShrinkable > 1f)
            {
                float cornerScale = 1f - Mathf.Clamp01(fromCorners / cornerShrinkable);
                for (int c = 0; c < cornerIdx.Count; c++)
                {
                    var def = defs[cornerIdx[c]];
                    float minimumRadius = Mathf.Min(def.Radius,
                        cfg.LegalCurveRadiusFloor);
                    float newRadius = minimumRadius +
                                      (def.Radius - minimumRadius) * cornerScale;
                    def.Radius = newRadius;
                    def.Length = (def.SectionType == TrackMacroSectionType.SCurve ? 2f : 1f) *
                                 SectionFrameBuilders.EasedArcLength(def.TurnAngle, newRadius);
                    def.BankingAngle = SectionDefs.RecommendedBank(cfg, newRadius);
                    cornerBaseRadius[c] = newRadius;
                }
            }

            return true;
        }

        /// <summary>
        /// Active-set least squares over straights and closure curves: variables that
        /// clamp at a bound leave the normal matrix so free variables absorb the full
        /// correction. Pins reset once mid-solve — a new λ direction can legally pull a
        /// pinned variable off its bound. Returns the remaining gap.
        /// </summary>
        internal static Vector2 RunActiveSetSolve(ResolvedTrackGenerationConfig cfg, List<TrackMacroSectionDefinition> defs,
            List<int> straightIdx, List<Vector2> straightDirs,
            List<int> cornerIdx, List<Vector2> cornerDirs, List<float> cornerBaseRadius, Vector2 gap)
        {
            float LenCap(TrackMacroSectionDefinition d) =>
                SectionFrameBuilders.MaxStraightSectionLength;

            int nStraights = straightIdx.Count;
            int varCount = nStraights + cornerIdx.Count;

            Vector2 DirOf(int k) => k < nStraights ? straightDirs[k] : cornerDirs[k - nStraights];

            float ValueOf(int k) => k < nStraights
                ? defs[straightIdx[k]].Length
                : defs[cornerIdx[k - nStraights]].Radius;

            float LengthDerivative(int k)
            {
                if (k < nStraights) return 1f;
                var def = defs[cornerIdx[k - nStraights]];
                float scale = def.SectionType == TrackMacroSectionType.SCurve ? 2f : 1f;
                return scale * SectionFrameBuilders.EasedArcLength(def.TurnAngle, 1f);
            }

            float ClampToTrackLengthBudget(int k, float value, float requested)
            {
                if (requested <= value) return requested;
                float derivative = Mathf.Max(0.0001f, LengthDerivative(k));
                float headroom = Mathf.Max(0f, cfg.MaxTrackLength - TotalPlanLength(defs));
                return value + Mathf.Min(requested - value, headroom / derivative);
            }

            // Styled corners flex within a fraction of their designed radius (character
            // survives); closure-reserve corners are TRUE closure curves and may sweep
            // the full rulebook radius band. Straights never shrink below their
            // connector-analysis minimum.
            float MaxOf(int k)
            {
                if (k < nStraights) return LenCap(defs[straightIdx[k]]);

                TrackMacroSectionDefinition definition = defs[cornerIdx[k - nStraights]];
                float configuredMaximum = definition.IsClosure || definition.RoadId == 1
                    ? (definition.IsClosure ? cfg.MaxClosureCurveRadius : cfg.MaxCurveRadius)
                    : Mathf.Min(cfg.MaxCurveRadius,
                        cornerBaseRadius[k - nStraights] * 1.8f);

                if (definition.SectionType == TrackMacroSectionType.SCurve)
                    return Mathf.Min(configuredMaximum,
                        SectionFrameBuilders.MaximumRadiusForEasedLength(
                            definition.TurnAngle, 2f,
                            SectionFrameBuilders.MaxDesignerSCurveLength));
                if (definition.SectionType == TrackMacroSectionType.BankedCurve)
                    return Mathf.Min(configuredMaximum,
                        SectionFrameBuilders.MaximumRadiusForEasedLength(
                            definition.TurnAngle, 1f,
                            SectionFrameBuilders.MaxOrdinaryCurveLength));
                return configuredMaximum;
            }

            float MinOf(int k)
            {
                if (k < nStraights)
                    return Mathf.Min(MaxOf(k),
                        Mathf.Max(MinAdjustableStraight,
                            defs[straightIdx[k]].MinimumLength));

                TrackMacroSectionDefinition definition = defs[cornerIdx[k - nStraights]];
                float requestedMinimum = definition.IsClosure || definition.RoadId == 1
                    ? cfg.LegalCurveRadiusFloor
                    : Mathf.Max(cfg.LegalCurveRadiusFloor,
                        cornerBaseRadius[k - nStraights] * 0.6f);
                return Mathf.Min(MaxOf(k), requestedMinimum);
            }

            void Apply(int k, float newValue)
            {
                if (k < nStraights)
                {
                    defs[straightIdx[k]].Length = newValue;
                }
                else
                {
                    var def = defs[cornerIdx[k - nStraights]];
                    def.Radius = newValue;
                    def.Length = (def.SectionType == TrackMacroSectionType.SCurve ? 2f : 1f) *
                                 SectionFrameBuilders.EasedArcLength(def.TurnAngle, newValue);
                    def.BankingAngle = SectionDefs.RecommendedBank(cfg, newValue);
                }
            }

            var pinned = new bool[varCount];
            bool pinsResetOnce = false;

            for (int pass = 0; pass < 40 && gap.magnitude > 0.003f; pass++)
            {
                float mxx = 0f, mxz = 0f, mzz = 0f;
                int freeCount = 0;
                for (int k = 0; k < varCount; k++)
                {
                    if (pinned[k]) continue;
                    Vector2 d = DirOf(k);
                    mxx += d.x * d.x;
                    mxz += d.x * d.y;
                    mzz += d.y * d.y;
                    freeCount++;
                }

                float appliedTotal = 0f;
                float det = mxx * mzz - mxz * mxz;

                if (freeCount == 0 || Mathf.Abs(det) < 0.001f)
                {
                    // Nearly collinear DOF (switchback laps): plain coordinate descent
                    // converges linearly, so loop it hard inside the pass.
                    for (int sub = 0; sub < 24 && gap.magnitude > 0.003f; sub++)
                    {
                        for (int k = 0; k < varCount; k++)
                        {
                            Vector2 dir = DirOf(k);
                            float value = ValueOf(k);
                            float delta = Vector2.Dot(gap, dir);
                            float newValue = Mathf.Clamp(value + delta, MinOf(k), MaxOf(k));
                            newValue = ClampToTrackLengthBudget(k, value, newValue);
                            gap -= dir * (newValue - value);
                            appliedTotal += Mathf.Abs(newValue - value);
                            Apply(k, newValue);
                        }
                    }
                }
                else
                {
                    float lx = (gap.x * mzz - gap.y * mxz) / det;
                    float lz = (gap.y * mxx - gap.x * mxz) / det;
                    var lambda = new Vector2(lx, lz);

                    for (int k = 0; k < varCount; k++)
                    {
                        if (pinned[k]) continue;
                        Vector2 dir = DirOf(k);
                        float value = ValueOf(k);
                        float delta = Vector2.Dot(lambda, dir);
                        float newValue = Mathf.Clamp(value + delta, MinOf(k), MaxOf(k));
                        newValue = ClampToTrackLengthBudget(k, value, newValue);
                        if (Mathf.Abs(newValue - value - delta) > 0.001f) pinned[k] = true;
                        gap -= dir * (newValue - value);
                        appliedTotal += Mathf.Abs(newValue - value);
                        Apply(k, newValue);
                    }
                }

                if (appliedTotal < 0.001f && gap.magnitude > 0.003f)
                {
                    if (pinsResetOnce) break; // genuinely out of capacity
                    System.Array.Clear(pinned, 0, varCount);
                    pinsResetOnce = true;
                }
            }

            return gap;
        }

        /// <summary>
        /// Track Editor-only measured closure refinement. The first candidate build is
        /// the authority for plan-vs-built drift (notably a stamped rotational feature
        /// entered from a non-level accepted pose). Ordinary straights/closure curves
        /// absorb the measured planar residual, while ordinary/recovery road absorbs
        /// the measured elevation residual. No feature body is warped and no closure
        /// tolerance is changed; the caller rebuilds and validates the complete track.
        /// </summary>
        public static bool TryRefineAuthoringBuiltClosure(
            ResolvedTrackGenerationConfig cfg,
            TopologyPlan plan,
            Vector3 builtClosureDelta,
            out string summary)
        {
            summary = "";
            if (cfg == null || plan?.Defs == null || !cfg.DesignerAuthoringMode)
                return false;

            // This pass is for accumulated reference-stamp drift, not a substitute for
            // a missing closure solution. Large misses remain explicit failures.
            const float maximumMeasuredRefinementMeters = 250f;
            if (!IsFinite(builtClosureDelta) ||
                builtClosureDelta.magnitude > maximumMeasuredRefinementMeters)
                return false;

            List<TrackMacroSectionDefinition> defs = plan.Defs;
            var straightIdx = new List<int>();
            var straightDirs = new List<Vector2>();
            var cornerIdx = new List<int>();
            var cornerDirs = new List<Vector2>();
            var cornerBaseRadius = new List<float>();
            CollectClosureVariables(defs, straightIdx, straightDirs,
                cornerIdx, cornerDirs, cornerBaseRadius);

            Vector2 currentPlanEnd = RewalkEndPos(defs);
            Vector2 targetPlanEnd = currentPlanEnd -
                                      new Vector2(builtClosureDelta.x,
                                          builtClosureDelta.z);
            Vector2 planarGap = targetPlanEnd - currentPlanEnd;
            Vector2 planarResidual = planarGap;
            if (planarGap.magnitude > 0.01f)
            {
                if (straightIdx.Count + cornerIdx.Count == 0) return false;
                planarResidual = RunActiveSetSolve(cfg, defs,
                    straightIdx, straightDirs, cornerIdx, cornerDirs,
                    cornerBaseRadius, planarGap);
            }

            // RunActiveSetSolve updates the linear residual as it applies variables.
            // A fresh exact walk protects this feedback path from stale derivatives.
            float exactPlanarResidual = Vector2.Distance(
                RewalkEndPos(defs), targetPlanEnd);
            if (planarResidual.magnitude > 1f || exactPlanarResidual > 1f)
                return false;

            float verticalResidual = ApplyAuthoringElevationDeltaInRange(
                cfg, defs, null, 0, defs.Count, -builtClosureDelta.y,
                "AuthoringMeasuredWeld", out float verticalApplied);
            if (Mathf.Abs(verticalResidual) > 0.5f) return false;

            summary = $"Track Editor measured-weld refinement absorbed " +
                      $"{new Vector2(builtClosureDelta.x, builtClosureDelta.z).magnitude:F1}m " +
                      $"of plan-view drift and {Mathf.Abs(verticalApplied):F1}m of elevation drift " +
                      "on legal ordinary/closure road before final validation.";
            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        /// <summary>
        /// Inserts one analytic closure S-bend (two opposed eased arcs, net-zero heading)
        /// into the longest closure straight to supply PERPENDICULAR displacement the
        /// straights and curves cannot. The bend's offset is linear in radius, so the
        /// radius is fit from the measured per-unit-radius eased-S offset. Returns false
        /// when no host straight exists or the residual is purely longitudinal.
        /// </summary>
        private static bool TryInsertClosureSBend(ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> defs, List<int> straightIdx, List<Vector2> straightDirs, Vector2 gap)
        {
            // Host selection: the S-bend displaces PERPENDICULAR to its host, so rank
            // straights by how well their sideways axis aligns with the residual
            // (closure-reserve straights strongly preferred, longer hosts break ties) —
            // then take the best-ranked host whose sideways SWING stays clear of the
            // rest of the lap. Scored-only choice happily swung the road straight
            // through geometry the plan walk then rejected wholesale.
            Vector2 gapDir = gap.normalized;
            var ranked = new List<(float score, int a)>();
            for (int a = 0; a < straightIdx.Count; a++)
            {
                var d = defs[straightIdx[a]];
                Vector2 r = new Vector2(straightDirs[a].y, -straightDirs[a].x);
                float alignment = Mathf.Abs(Vector2.Dot(gapDir, r));
                float score = alignment * (d.IsClosure ? 2f : 1f) * Mathf.Clamp01(d.Length / 400f);
                if (score > 0f) ranked.Add((score, a));
            }
            if (ranked.Count == 0) return false;
            ranked.Sort((p, q) => p.score != q.score ? q.score.CompareTo(p.score) : p.a.CompareTo(q.a));

            // One coarse 2D walk: lap samples for the swing check + candidate starts.
            var lapPts = new List<Vector2>(768);
            var lapDefOf = new List<int>(768);
            var startOf = new Dictionary<int, Vector2>();
            {
                var wanted = new HashSet<int>(straightIdx);
                Vector2 wPos = Vector2.zero;
                float wHeading = 0f;
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    if (d.RoadId == 1) continue;
                    if (wanted.Contains(i)) startOf[i] = wPos;
                    int first = lapPts.Count;
                    QuarterRoadFitter.WalkDense(d, ref wPos, ref wHeading, lapPts);
                    for (int k = first; k < lapPts.Count; k++) lapDefOf.Add(i);
                }
            }

            float swingClearance = cfg.RoadWidth * 1.3f * 1.05f;
            float swingClearanceSq = swingClearance * swingClearance;
            int defCount = defs.Count;

            bool SwingClear(int hostDefIndex, Vector2 bendStart, Vector2 bendDir, float bendLateral, float bendForward)
            {
                Vector2 bendRight = new Vector2(bendDir.y, -bendDir.x);
                const int steps = 12;
                for (int sIdx = 0; sIdx <= steps; sIdx++)
                {
                    float t = (float)sIdx / steps;
                    float swing = t * t * (3f - 2f * t); // smooth 0→1, like the eased S
                    Vector2 p = bendStart + bendDir * (bendForward * t) + bendRight * (bendLateral * swing);
                    for (int k = 0; k < lapPts.Count; k++)
                    {
                        int idxDist = Mathf.Abs(lapDefOf[k] - hostDefIndex);
                        if (Mathf.Min(idxDist, defCount - idxDist) <= 2) continue; // the road legitimately flows here
                        if ((lapPts[k] - p).sqrMagnitude < swingClearanceSq) return false;
                    }
                }
                return true;
            }

            int host = -1;
            foreach (var (_, a) in ranked)
            {
                Vector2 cDir = straightDirs[a];
                Vector2 cRight = new Vector2(cDir.y, -cDir.x);
                float cLateral = Vector2.Dot(gap, cRight);
                if (Mathf.Abs(cLateral) < 0.5f) continue;
                if (!startOf.TryGetValue(straightIdx[a], out Vector2 cStart)) continue;

                // Region estimate from the same closed form the insertion uses.
                float cRadius = Mathf.Clamp(Mathf.Abs(cLateral) / (2f * 0.45f),
                    cfg.MinCurveRadius, cfg.MaxClosureCurveRadius);
                float cOneMinusCos = Mathf.Clamp(Mathf.Abs(cLateral) / (2f * cRadius), 0.005f, 0.741f);
                float cAlpha = Mathf.Acos(1f - cOneMinusCos) * Mathf.Rad2Deg;
                float cForward = cRadius * SectionFrameBuilders.EasedSBendUnitOffset(
                    SectionFrameBuilders.QuantizeArcAngle(cAlpha)).x;

                // The host shrinks by the bend's forward run — the bend occupies the
                // LAST cForward meters of the host's original span.
                Vector2 bendStart = cStart + cDir * Mathf.Max(0f, defs[straightIdx[a]].Length - cForward);
                if (!SwingClear(straightIdx[a], bendStart, cDir, cLateral, cForward))
                    continue;
                host = a;
                break;
            }
            // A forced fallback here converted an honest closure-capacity miss into a
            // deterministic self-intersection on the following validation pass.
            if (host < 0) return false;

            Vector2 dir = straightDirs[host];
            Vector2 right = new Vector2(dir.y, -dir.x);
            float lateral = Vector2.Dot(gap, right);
            if (Mathf.Abs(lateral) < 0.5f) return false; // purely longitudinal residual

            // Radius that keeps the bend broad (α ≈ 50–60°) yet inside the rulebook.
            // α comes from the circular closed form as a first guess, is quantized to
            // the shared 0.5° profile grid, and the radius is then re-fit from the
            // MEASURED eased-S offset so the bend lands the residual exactly.
            const float maxOneMinusCos = 0.741f; // α ≤ 75°
            float radius = Mathf.Clamp(Mathf.Abs(lateral) / (2f * 0.45f),
                cfg.MinCurveRadius, cfg.MaxClosureCurveRadius);
            float oneMinusCos = Mathf.Clamp(Mathf.Abs(lateral) / (2f * radius), 0.005f, maxOneMinusCos);
            float alpha = SectionFrameBuilders.QuantizeArcAngle(Mathf.Acos(1f - oneMinusCos) * Mathf.Rad2Deg);

            Vector2 unitOffset = SectionFrameBuilders.EasedSBendUnitOffset(alpha);
            float radiusCeiling = Mathf.Min(cfg.MaxClosureCurveRadius,
                SectionFrameBuilders.MaximumRadiusForEasedLength(alpha, 2f,
                    SectionFrameBuilders.MaxDesignerSCurveLength));
            radius = Mathf.Clamp(Mathf.Abs(lateral) / Mathf.Max(unitOffset.y, 1e-4f),
                Mathf.Min(cfg.LegalCurveRadiusFloor, radiusCeiling), radiusCeiling);

            var hostDef = defs[straightIdx[host]];
            float forwardRun = radius * unitOffset.x;
            // Respect the connector-analysis floor: shrinking the host below its blend
            // requirement re-creates the squeezed-blend wall wave the floor prevents.
            hostDef.Length = Mathf.Max(Mathf.Max(MinAdjustableStraight, hostDef.MinimumLength),
                hostDef.Length - forwardRun);

            var sBend = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.SCurve,
                Length = 2f * SectionFrameBuilders.EasedArcLength(alpha, radius),
                Width = hostDef.Width,
                Direction = lateral >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                TurnAngle = alpha,
                Radius = radius,
                BankingAngle = SectionDefs.RecommendedBank(cfg, radius) * 0.7f,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Normal,
                LockLength = true,
                IsClosure = true,
                DebugName = $"ClosureSBend_{alpha:F0}deg",
                Contract = SectionConnectionContract.Level()
            };

            defs.Insert(straightIdx[host] + 1, sBend);
            return true;
        }

        /// <summary>Re-walks the whole plan in 2D and returns the end position (should be the origin when closed).</summary>
        private static Vector2 RewalkEndPos(List<TrackMacroSectionDefinition> defs)
        {
            Vector2 pos = Vector2.zero;
            float heading = 0f;

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d.RoadId == 1) continue; // alternate roads never drive the canonical walk
                Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);

                switch (d.SectionType)
                {
                    case TrackMacroSectionType.Loop:
                    {
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        pos += fwd * SectionFrameBuilders.LoopForwardDisplacement(d.Length)
                             + right * SectionFrameBuilders.LoopLateralOffset(d.Width);
                        break;
                    }
                    case TrackMacroSectionType.Spiral:
                        SectionFrameBuilders.ApplySpiral2D(ref pos, ref heading, d);
                        break;
                    case TrackMacroSectionType.HalfLoopTwist:
                    {
                        float halfArc = SectionFrameBuilders.HalfLoopArcLength(d.Radius);
                        pos += fwd * SectionFrameBuilders.HalfLoopForwardDisplacement(halfArc);
                        heading += 180f;
                        pos += SectionFrameBuilders.HeadingToDir(heading) * Mathf.Max(0f, d.Length - halfArc);
                        break;
                    }
                    case TrackMacroSectionType.RotationalEvent:
                    {
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        pos += fwd * d.PlanHorizontalLength + right * d.PlanLateralOffset;
                        heading += d.TurnAngle;
                        break;
                    }
                    case TrackMacroSectionType.SCurve:
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, -d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    case TrackMacroSectionType.Chicane:
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, -2f * d.TurnAngle * d.TurnSign, d.Radius);
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                    case TrackMacroSectionType.WallrideTurn:
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    default:
                        pos += fwd * d.HorizontalRun;
                        if (d.PlanLateralOffset != 0f)
                            pos += new Vector2(fwd.y, -fwd.x) * d.PlanLateralOffset;
                        break;
                }
            }

            return pos;
        }

        /// <summary>Diagnostics for closure angle relief: candidates that invoked it and how many
        /// it closed to tolerance this generation. Reset by the pipeline before each candidate sweep.</summary>
        internal static int AngleReliefAttempts;
        internal static int AngleReliefClosures;

        /// <summary>
        /// Closure angle relief (§7 extension). Plain corner angles are normally frozen closure inputs
        /// (the radius/straight solver moves only lengths). Here we grant a bounded ±4° of
        /// angular authority, applied strictly in EQUAL-AND-OPPOSITE pairs so the signed
        /// corner sum — and therefore lap heading closure and any canonical 45/90/180 token —
        /// is preserved exactly, while the intervening geometry rotates enough to walk the
        /// far end onto the origin. Each iteration finite-differences every eligible corner's
        /// end-position derivative, then applies the single antisymmetric pair that removes
        /// the most residual (closed-form scalar step, no matrix). Returns true once the walk
        /// closes within tolerance. Radius is never touched; feature-internal and alternate-road
        /// arcs are never touched. No-op geometry unless the plan actually closes tighter.
        /// </summary>
        private static bool TryCloseWithAngleRelief(ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> defs, float tol)
        {
            const float maxNudgeDeg = 4f;   // cumulative per-corner bound
            const float diffStepDeg = 0.5f; // finite-difference probe
            const int maxIters = 12;

            var idx = new List<int>();
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d.RoadId == 1) continue;                       // alternate roads don't drive the walk
                if (!string.IsNullOrEmpty(d.PatternId)) continue;  // never touch feature-internal arcs
                if (d.SectionType == TrackMacroSectionType.BankedCurve ||
                    d.SectionType == TrackMacroSectionType.BankedHairpin)
                    idx.Add(i);
            }
            if (idx.Count < 2) return false; // need a pair to stay heading-neutral

            var applied = new float[idx.Count]; // cumulative signed-angle delta (deg) per corner
            var col = new Vector2[idx.Count];   // dEnd/dSignedAngle per corner

            for (int iter = 0; iter < maxIters; iter++)
            {
                Vector2 end = RewalkEndPos(defs);
                Vector2 gap = -end;
                if (gap.magnitude <= tol) return true;

                for (int k = 0; k < idx.Count; k++)
                {
                    var d = defs[idx[k]];
                    float baseAngle = d.TurnAngle;
                    d.TurnAngle = baseAngle + diffStepDeg * d.TurnSign; // +diffStep of SIGNED angle
                    col[k] = (RewalkEndPos(defs) - end) / diffStepDeg;
                    d.TurnAngle = baseAngle;                            // exact restore
                }

                // Best antisymmetric pair: +delta on a, -delta on b keeps the corner sum
                // fixed; the resulting end shift is (col[a]-col[b])*delta.
                int bestA = -1, bestB = -1;
                float bestDelta = 0f, bestReduce = 1e-3f; // ignore negligible steps
                for (int a = 0; a < idx.Count; a++)
                    for (int b = a + 1; b < idx.Count; b++)
                    {
                        Vector2 dir = col[a] - col[b];
                        float dd = Vector2.Dot(dir, dir);
                        if (dd < 1e-9f) continue;

                        float delta = Vector2.Dot(dir, gap) / dd;
                        // Clamp so both cumulative nudges stay inside ±maxNudge.
                        float lo = Mathf.Max(-maxNudgeDeg - applied[a], applied[b] - maxNudgeDeg);
                        float hi = Mathf.Min(maxNudgeDeg - applied[a], applied[b] + maxNudgeDeg);
                        if (hi <= lo) continue;
                        delta = Mathf.Clamp(delta, lo, hi);

                        // Predicted squared-gap reduction: 2·delta·(dir·gap) − delta²·|dir|².
                        float reduce = 2f * delta * Vector2.Dot(dir, gap) - delta * delta * dd;
                        if (reduce > bestReduce)
                        {
                            bestReduce = reduce; bestA = a; bestB = b; bestDelta = delta;
                        }
                    }

                if (bestA < 0) return false; // no pair helps within the bound — hand back to S-bend

                ApplyAngleDelta(cfg, defs[idx[bestA]], +bestDelta);
                ApplyAngleDelta(cfg, defs[idx[bestB]], -bestDelta);
                applied[bestA] += bestDelta;
                applied[bestB] -= bestDelta;
            }

            return RewalkEndPos(defs).magnitude <= tol;
        }

        /// <summary>Shifts a plain corner's SIGNED angle by deltaDeg, keeping its turn sign,
        /// and re-derives the dependent arc length so lap-length accounting stays exact.</summary>
        private static void ApplyAngleDelta(ResolvedTrackGenerationConfig cfg,
            TrackMacroSectionDefinition d, float deltaDeg)
        {
            d.TurnAngle = Mathf.Max(1f, d.TurnAngle + deltaDeg * d.TurnSign);
            d.Length = SectionFrameBuilders.EasedArcLength(d.TurnAngle, d.Radius);
        }

        // ═══════════════════════════ 6. Cheap 2D self-proximity ═══════════════════════════

        private sealed class PlanProximityCollision
        {
            public int FirstOwner;
            public int SecondOwner;
            public float FirstHeight;
            public float SecondHeight;
        }

        /// <summary>
        /// The built validator exempts all sections stamped with the same non-empty
        /// pattern identity. The cheap planner must use that exact ownership rule too:
        /// an approach, feature and recovery are one authored feature, not unrelated
        /// roads merely because they are represented by separate definitions.
        /// </summary>
        public static bool IsSameFeaturePatternProximity(
            IReadOnlyList<TrackMacroSectionDefinition> definitions,
            int firstOwner,
            int secondOwner)
        {
            if (definitions == null || firstOwner < 0 || secondOwner < 0 ||
                firstOwner >= definitions.Count || secondOwner >= definitions.Count)
                return false;

            TrackMacroSectionDefinition first = definitions[firstOwner];
            TrackMacroSectionDefinition second = definitions[secondOwner];
            return first != null && second != null &&
                   !string.IsNullOrEmpty(first.PatternId) &&
                   string.Equals(first.PatternId, second.PatternId,
                       System.StringComparison.Ordinal);
        }

        /// <summary>Returns null when the plan-view walk is clear, else a description of the first collision.</summary>
        private string Validate2DWalk(ResolvedTrackGenerationConfig cfg, TopologyPlan plan,
            out PlanProximityCollision collisionDetails)
        {
            collisionDetails = null;
            var defs = plan.Defs;
            var pts = new List<Vector2>(1024);
            var arcs = new List<float>(1024);
            var heights = new List<float>(1024);
            var owners = new List<int>(1024);

            Vector2 pos = Vector2.zero;
            float heading = 0f;
            float arc = 0f;
            float fixedHeight = 0f;
            // Normal generation uses a cheap broad planning sample because it can try
            // another candidate. Track Editor has one exact accepted-route attempt and
            // the final validator samples every 8 m; use the same density here so a
            // narrow unsafe interval is repaired before expensive geometry is built.
            float step = cfg.DesignerAuthoringMode ? 8f : 50f;

            void Emit(Vector2 p, float a) { pts.Add(p); arcs.Add(a); }

            void WalkStraight(float length)
            {
                Vector2 dir = SectionFrameBuilders.HeadingToDir(heading);
                for (float s = 0f; s < length; s += step) Emit(pos + dir * s, arc + s);
                pos += dir * length;
                arc += length;
            }

            // Corners follow the same eased-arc profile the builder uses.
            void WalkEased(float signedAngleDeg, float radius)
            {
                SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, signedAngleDeg, radius, step,
                    (p, a) => Emit(p, a), ref arc);
            }

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d.RoadId == 1) continue; // road B proximity is validated on the real 3D frames
                int firstSample = pts.Count;

                switch (d.SectionType)
                {
                    case TrackMacroSectionType.Loop:
                    {
                        WalkStraight(SectionFrameBuilders.LoopForwardDisplacement(d.Length));
                        Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);
                        pos += new Vector2(fwd.y, -fwd.x) * SectionFrameBuilders.LoopLateralOffset(d.Width);
                        arc += d.Length - SectionFrameBuilders.LoopForwardDisplacement(d.Length);
                        break;
                    }
                    case TrackMacroSectionType.Spiral:
                    {
                        // A feature-integrated spiral may drift forward while completing
                        // whole revolutions. Sample that moving-center helix directly;
                        // treating it as a closed circle would hide its real corridor.
                        Vector2 start = pos;
                        Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        float signedAngle = d.TurnSign * d.TurnAngle;
                        float side = Mathf.Sign(signedAngle);
                        float radius = Mathf.Max(d.Width, d.Radius);
                        float totalRad = Mathf.Abs(signedAngle) * Mathf.Deg2Rad;
                        int samples = Mathf.Max(8, Mathf.CeilToInt(d.Length / step));
                        float ds = d.Length / samples;
                        for (int sample = 1; sample <= samples; sample++)
                        {
                            float u = (float)sample / samples;
                            float a = totalRad * u;
                            pos = start
                                  + fwd * (radius * Mathf.Sin(a) + d.PlanHorizontalLength * SectionFrameBuilders.Smooth01(u))
                                  + right * (side * radius * (1f - Mathf.Cos(a)));
                            arc += ds;
                            Emit(pos, arc);
                        }
                        heading += signedAngle;
                        break;
                    }
                    case TrackMacroSectionType.HalfLoopTwist:
                    {
                        // Two limbs with DIFFERENT height profiles: the half-loop climbs
                        // 0 → top height, then the rollout flies back at CONSTANT top
                        // height. A single linear ramp across both limbs invents phantom
                        // same-height overlaps between the rollout and the ground path.
                        float halfArc = SectionFrameBuilders.HalfLoopArcLength(d.Radius);
                        WalkStraight(SectionFrameBuilders.HalfLoopForwardDisplacement(halfArc));
                        int climbSamples = pts.Count - firstSample;
                        arc += halfArc - SectionFrameBuilders.HalfLoopForwardDisplacement(halfArc);
                        heading += 180f;
                        WalkStraight(Mathf.Max(0f, d.Length - halfArc));
                        int rolloutSamples = pts.Count - firstSample - climbSamples;

                        for (int k = 0; k < climbSamples; k++)
                        {
                            float t = climbSamples > 1 ? (float)k / (climbSamples - 1) : 1f;
                            heights.Add(fixedHeight + d.ElevationChange * t);
                            owners.Add(i);
                        }
                        for (int k = 0; k < rolloutSamples; k++)
                        {
                            heights.Add(fixedHeight + d.ElevationChange);
                            owners.Add(i);
                        }
                        fixedHeight += d.ElevationChange;
                        continue; // heights stamped above
                    }
                    case TrackMacroSectionType.RotationalEvent:
                    {
                        var localFrames = SectionFrameBuilders.BuildRotationalEvent(
                            TrackConnectionFrame.Origin(d.Width), d, FrameBuildContext.From(cfg));
                        Vector2 start = pos;
                        Vector2 eventFwd = SectionFrameBuilders.HeadingToDir(heading);
                        Vector2 eventRight = new Vector2(eventFwd.y, -eventFwd.x);
                        float eventArcStart = arc;
                        float nextArc = 0f;
                        for (int sample = 1; sample < localFrames.Length; sample++)
                        {
                            var local = localFrames[sample];
                            if (local.ArcLength + 0.001f < nextArc && sample < localFrames.Length - 1) continue;
                            pos = start + eventFwd * local.Position.z + eventRight * local.Position.x;
                            Emit(pos, eventArcStart + local.ArcLength);
                            heights.Add(fixedHeight + local.Position.y);
                            owners.Add(i);
                            nextArc = local.ArcLength + step;
                        }
                        var end = localFrames[localFrames.Length - 1];
                        pos = start + eventFwd * end.Position.z + eventRight * end.Position.x;
                        arc = eventArcStart + end.ArcLength;
                        heading += d.TurnAngle;
                        fixedHeight += end.Position.y;
                        continue;
                    }
                    case TrackMacroSectionType.SCurve:
                        WalkEased(d.TurnAngle * d.TurnSign, d.Radius);
                        WalkEased(-d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    case TrackMacroSectionType.Chicane:
                        WalkEased(d.TurnAngle * d.TurnSign, d.Radius);
                        WalkEased(-2f * d.TurnAngle * d.TurnSign, d.Radius);
                        WalkEased(d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                    case TrackMacroSectionType.WallrideTurn:
                        WalkEased(d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    default:
                        WalkStraight(d.HorizontalRun);
                        // Choice/convergence gaps land laterally off the flight midline.
                        if (d.PlanLateralOffset != 0f)
                        {
                            Vector2 fwdDir = SectionFrameBuilders.HeadingToDir(heading);
                            pos += new Vector2(fwdDir.y, -fwdDir.x) * d.PlanLateralOffset;
                        }
                        break;
                }

                // Height stamps: EVERY section's planned elevation counts — fixed-height
                // features (spirals, half-loops), the elevation plan's major climbs on
                // straights, and net-zero crest/bridge bumps. Planned vertical separation
                // is what legally rescues folded mountain-pass legs.
                float delta = d.SectionType == TrackMacroSectionType.Spiral ||
                              d.SectionType == TrackMacroSectionType.Corkscrew ||
                              d.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                              d.IsStraightFamily
                    ? d.ElevationChange
                    : 0f;
                float bump = d.HillHeight;
                int emitted = pts.Count - firstSample;
                for (int k = 0; k < emitted; k++)
                {
                    float t = emitted > 1 ? (float)k / (emitted - 1) : 1f;
                    // Elevated curve primitives use a C2 crest carrier so their
                    // geometry returns to zero vertical curvature at the boundary.
                    // Straight-family hills retain the established C1 hill profile.
                    float bumpShape = d.IsStraightFamily
                        ? SectionFrameBuilders.Bump(t)
                        : SectionFrameBuilders.CrestBump(t);
                    heights.Add(fixedHeight + delta * t + bump * bumpShape);
                    owners.Add(i);
                }
                fixedHeight += delta;
            }

            float totalArc = arc;
            if (totalArc < 1f || pts.Count < 8) return "degenerate walk";

            // Procedural generation keeps a small planning margin for built-geometry
            // drift. A focused Track Editor rebuild instead gets the exact published
            // rulebook corridor: its complete built candidate is validated immediately
            // afterwards, so rejecting a 161 m separation against a 160 m rule here
            // would be a false negative rather than an added safety check.
            float minClear = cfg.UnrelatedCorridor *
                             (cfg.DesignerAuthoringMode ? 1f : 1.05f);
            float minClearSq = minClear * minClear;
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);
            const float alongWindow = 600f;

            // The editor's 8 m sampling would make a full O(n²) pair walk needlessly
            // expensive on a 30-40 km lap. Use the same deterministic XZ broad phase as
            // final validation, then preserve the old ascending pair order inside the
            // nearby buckets so repair selection remains stable.
            float cellSize = Mathf.Max(1f, minClear);
            long CellKey(int cellX, int cellY) =>
                ((long)cellX << 32) ^ (uint)cellY;
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < pts.Count; i++)
            {
                int cellX = Mathf.FloorToInt(pts[i].x / cellSize);
                int cellY = Mathf.FloorToInt(pts[i].y / cellSize);
                long key = CellKey(cellX, cellY);
                if (!grid.TryGetValue(key, out List<int> bucket))
                    grid[key] = bucket = new List<int>();
                bucket.Add(i);
            }

            var nearby = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                nearby.Clear();
                int cellX = Mathf.FloorToInt(pts[i].x / cellSize);
                int cellY = Mathf.FloorToInt(pts[i].y / cellSize);
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                    if (grid.TryGetValue(CellKey(cellX + offsetX, cellY + offsetY),
                            out List<int> bucket))
                        nearby.AddRange(bucket);
                nearby.Sort();

                for (int candidateIndex = 0; candidateIndex < nearby.Count; candidateIndex++)
                {
                    int j = nearby[candidateIndex];
                    if (j <= i) continue;
                    float along = Mathf.Abs(arcs[j] - arcs[i]);
                    along = Mathf.Min(along, totalArc - along);
                    if (along < alongWindow) continue;
                    if ((pts[i] - pts[j]).sqrMagnitude >= minClearSq) continue;
                    if (Mathf.Abs(heights[i] - heights[j]) >= verticalOk) continue;

                    // Keep the cheap gate in lockstep with the final 3D validator.
                    // This includes a feature's separately emitted approach/recovery
                    // roads and the Track Editor's owned vertical recovery road.
                    if (IsSameFeaturePatternProximity(defs, owners[i], owners[j]))
                        continue;

                    // A vertical feature owns its self-proximity (loop apex fold, spiral
                    // coils, half-loop limbs) — its clearance contract is validated on
                    // the real 3D frames, where the 2D approximation cannot judge it.
                    if (owners[i] == owners[j])
                    {
                        var ownerType = defs[owners[i]].SectionType;
                        if (ownerType == TrackMacroSectionType.Loop ||
                            ownerType == TrackMacroSectionType.Spiral ||
                            ownerType == TrackMacroSectionType.HalfLoopTwist ||
                            ownerType == TrackMacroSectionType.RotationalEvent)
                            continue;
                    }

                    collisionDetails = new PlanProximityCollision
                    {
                        FirstOwner = owners[i],
                        SecondOwner = owners[j],
                        FirstHeight = heights[i],
                        SecondHeight = heights[j]
                    };
                    return $"'{defs[owners[i]].DebugName}' (arc {arcs[i]:F0}m, h {heights[i]:F0}m) vs '{defs[owners[j]].DebugName}' (arc {arcs[j]:F0}m, h {heights[j]:F0}m) at {Mathf.Sqrt((pts[i] - pts[j]).sqrMagnitude):F0}m apart";
                }
            }

            return null;
        }

        /// <summary>
        /// Designer-only clearance repair. A complete feature pattern may be translated
        /// vertically without moving either weld by adding equal and opposite elevation
        /// to its own straight approach and recovery. Both carriers retain legal eased
        /// grades and the final 3D validator still decides whether the result is safe.
        /// </summary>
        public static bool TryApplyAuthoringVerticalClearance(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            int firstOwner,
            float firstHeight,
            int secondOwner,
            float secondHeight,
            out string repairNote)
        {
            repairNote = "";
            if (cfg == null || definitions == null || !cfg.DesignerAuthoringMode ||
                firstOwner < 0 || secondOwner < 0 || firstOwner >= definitions.Count ||
                secondOwner >= definitions.Count || firstOwner == secondOwner ||
                IsSameFeaturePatternProximity(definitions, firstOwner, secondOwner))
                return false;

            bool TryOwner(int owner, float ownerHeight, int otherOwner, float otherHeight,
                out string note)
            {
                note = "";
                TrackMacroSectionDefinition feature = definitions[owner];
                if (feature == null || string.IsNullOrEmpty(feature.PatternId) ||
                    (feature.SectionType != TrackMacroSectionType.Spiral &&
                     feature.SectionType != TrackMacroSectionType.Loop &&
                     feature.SectionType != TrackMacroSectionType.HalfLoopTwist &&
                     feature.SectionType != TrackMacroSectionType.RotationalEvent))
                    return false;

                int before = -1;
                for (int i = owner - 1; i >= 0; i--)
                {
                    TrackMacroSectionDefinition candidate = definitions[i];
                    if (candidate == null || !string.Equals(candidate.PatternId,
                            feature.PatternId, System.StringComparison.Ordinal))
                        break;
                    if (candidate.IsStraightFamily && candidate.Length >= 150f &&
                        !IsTrackEditorVerticalRecovery(candidate))
                    {
                        before = i;
                        break;
                    }
                }

                int after = -1;
                for (int i = owner + 1; i < definitions.Count; i++)
                {
                    TrackMacroSectionDefinition candidate = definitions[i];
                    if (candidate == null || !string.Equals(candidate.PatternId,
                            feature.PatternId, System.StringComparison.Ordinal))
                        break;
                    if (candidate.IsStraightFamily && candidate.Length >= 150f &&
                        !IsTrackEditorVerticalRecovery(candidate))
                    {
                        after = i;
                        break;
                    }
                }

                // The unrelated road must stay outside the translated feature block;
                // otherwise both roads move together and no clearance is gained.
                if (before < 0 || after < 0 ||
                    (otherOwner > before && otherOwner <= after))
                    return false;

                TrackMacroSectionDefinition approach = definitions[before];
                TrackMacroSectionDefinition recovery = definitions[after];
                // The focused candidate is immediately checked against the complete
                // built mesh. Use the exact published clearance here; the old extra
                // 12 m planning pad could turn a solvable 37 m lift into an impossible
                // 49 m lift and reject the designer edit before final validation.
                float required = Mathf.Max(1f, cfg.VerticalClearance);
                float currentDifference = ownerHeight - otherHeight;
                float shiftUp = required - currentDifference;
                float shiftDown = -required - currentDifference;
                float firstShift = Mathf.Abs(shiftUp) <= Mathf.Abs(shiftDown)
                    ? shiftUp : shiftDown;
                float secondShift = firstShift == shiftUp ? shiftDown : shiftUp;

                bool Fits(float shift)
                {
                    return AvailableAuthoringElevationDelta(cfg, approach, shift) + 0.01f >=
                               Mathf.Abs(shift) &&
                           AvailableAuthoringElevationDelta(cfg, recovery, -shift) + 0.01f >=
                               Mathf.Abs(shift);
                }

                float applied = Fits(firstShift) ? firstShift :
                    (Fits(secondShift) ? secondShift : 0f);
                if (Mathf.Abs(applied) <= 0.01f) return false;

                approach.ElevationChange += applied;
                recovery.ElevationChange -= applied;
                approach.DebugName += applied > 0f
                    ? $"_AuthoringClearanceClimb{applied:F0}m"
                    : $"_AuthoringClearanceDrop{-applied:F0}m";
                recovery.DebugName += applied > 0f
                    ? $"_AuthoringClearanceDrop{applied:F0}m"
                    : $"_AuthoringClearanceClimb{-applied:F0}m";
                note = $"Track Editor vertically separated {feature.DebugName} by " +
                       $"{Mathf.Abs(applied):F1}m using its own approach and recovery roads.";
                return true;
            }

            if (TryOwner(firstOwner, firstHeight, secondOwner, secondHeight,
                    out repairNote))
                return true;
            return TryOwner(secondOwner, secondHeight, firstOwner, firstHeight,
                out repairNote);
        }

        /// <summary>
        /// Broader designer fallback for a real route-level crossing. When the two
        /// colliding sections do not have enough capacity in one feature's own mouth,
        /// distribute a climb/drop over the nearest ordinary straight roads before the
        /// target and the inverse grade over straights after it. The unrelated section
        /// stays outside the window, every carrier remains inside its eased-grade cap,
        /// and the complete lap returns to exactly the same elevation.
        /// </summary>
        public static bool TryApplyAuthoringRouteClearance(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            int firstOwner,
            float firstHeight,
            int secondOwner,
            float secondHeight,
            out string repairNote)
        {
            repairNote = "";
            if (cfg == null || definitions == null || !cfg.DesignerAuthoringMode ||
                firstOwner < 0 || secondOwner < 0 || firstOwner >= definitions.Count ||
                secondOwner >= definitions.Count || firstOwner == secondOwner ||
                IsSameFeaturePatternProximity(definitions, firstOwner, secondOwner))
                return false;

            float Available(TrackMacroSectionDefinition carrier, float direction)
            {
                if (!IsAuthoringElevationCarrier(carrier) || carrier.Length < 200f)
                    return 0f;
                return AvailableAuthoringElevationDelta(cfg, carrier, direction);
            }

            bool TryShift(int owner, float ownerHeight, int otherOwner, float otherHeight,
                float requestedShift, out string note)
            {
                note = "";
                if (Mathf.Abs(requestedShift) <= 0.01f) return false;
                TrackMacroSectionDefinition target = definitions[owner];
                if (target == null || target.RoadId != 0) return false;

                int beforeLimit = otherOwner < owner ? otherOwner + 1 : 0;
                int afterLimit = otherOwner > owner ? otherOwner - 1 : definitions.Count - 1;
                var before = new List<(int Index, float Capacity)>();
                var after = new List<(int Index, float Capacity)>();
                float beforeCapacity = 0f;
                float afterCapacity = 0f;
                float need = Mathf.Abs(requestedShift);

                for (int i = owner - 1; i >= beforeLimit && beforeCapacity + 0.01f < need; i--)
                {
                    TrackMacroSectionDefinition carrier = definitions[i];
                    if (carrier == null || carrier.RoadId != target.RoadId) continue;
                    float capacity = Available(carrier, requestedShift);
                    if (capacity <= 0.01f) continue;
                    before.Add((i, capacity));
                    beforeCapacity += capacity;
                }
                for (int i = owner + 1; i <= afterLimit && afterCapacity + 0.01f < need; i++)
                {
                    TrackMacroSectionDefinition carrier = definitions[i];
                    if (carrier == null || carrier.RoadId != target.RoadId) continue;
                    float capacity = Available(carrier, -requestedShift);
                    if (capacity <= 0.01f) continue;
                    after.Add((i, capacity));
                    afterCapacity += capacity;
                }
                if (beforeCapacity + 0.01f < need || afterCapacity + 0.01f < need)
                    return false;

                void Apply(List<(int Index, float Capacity)> carriers,
                    float totalCapacity, float totalDelta)
                {
                    float remaining = totalDelta;
                    for (int i = 0; i < carriers.Count; i++)
                    {
                        float delta = i == carriers.Count - 1
                            ? remaining
                            : totalDelta * carriers[i].Capacity / totalCapacity;
                        // Proportional allocation stays below each carrier's cap; the
                        // final clamp only absorbs floating-point accumulation.
                        float cap = carriers[i].Capacity;
                        delta = Mathf.Clamp(delta, -cap, cap);
                        TrackMacroSectionDefinition carrier = definitions[carriers[i].Index];
                        carrier.ElevationChange += delta;
                        carrier.DebugName += delta >= 0f
                            ? $"_AuthoringWindowClimb{delta:F0}m"
                            : $"_AuthoringWindowDrop{-delta:F0}m";
                        remaining -= delta;
                    }
                }

                Apply(before, beforeCapacity, requestedShift);
                Apply(after, afterCapacity, -requestedShift);
                note = $"Track Editor created a route-level vertical clearance window around " +
                       $"{target.DebugName}: {Mathf.Abs(requestedShift):F1}m across " +
                       $"{before.Count} entry and {after.Count} exit road sections; both lap welds stayed fixed.";
                return true;
            }

            bool TryOwner(int owner, float ownerHeight, int otherOwner, float otherHeight,
                out string note)
            {
                float required = Mathf.Max(1f, cfg.VerticalClearance);
                float difference = ownerHeight - otherHeight;
                float shiftUp = required - difference;
                float shiftDown = -required - difference;
                float firstShift = Mathf.Abs(shiftUp) <= Mathf.Abs(shiftDown)
                    ? shiftUp : shiftDown;
                float secondShift = firstShift == shiftUp ? shiftDown : shiftUp;
                if (TryShift(owner, ownerHeight, otherOwner, otherHeight,
                        firstShift, out note))
                    return true;
                return TryShift(owner, ownerHeight, otherOwner, otherHeight,
                    secondShift, out note);
            }

            if (TryOwner(firstOwner, firstHeight, secondOwner, secondHeight,
                    out repairNote))
                return true;
            return TryOwner(secondOwner, secondHeight, firstOwner, firstHeight,
                out repairNote);
        }

        /// <summary>
        /// Captures only the accepted route's vertical design. Geometry, length and
        /// connector ownership remain the responsibility of the focused rebuild.
        /// </summary>
        public static IReadOnlyList<AuthoringElevationBaselineEntry>
            CaptureAuthoringElevationBaseline(IReadOnlyList<GeneratedTrackSection> sections)
        {
            var result = new List<AuthoringElevationBaselineEntry>();
            if (sections == null || sections.Count == 0) return result;

            var definitions = new List<TrackMacroSectionDefinition>(sections.Count);
            for (int i = 0; i < sections.Count; i++)
                definitions.Add(sections[i]?.Definition);
            List<string> keys = BuildAuthoringElevationMatchKeys(definitions);
            int canonicalLapEnd = -1;
            for (int i = definitions.Count - 1; i >= 0; i--)
            {
                if (definitions[i] == null || definitions[i].RoadId != 0) continue;
                canonicalLapEnd = i;
                break;
            }
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(keys[i])) continue;
                GeneratedTrackSection section = sections[i];
                result.Add(new AuthoringElevationBaselineEntry
                {
                    MatchKey = keys[i],
                    ElevationChange = definition.ElevationChange,
                    HillHeight = definition.HillHeight,
                    StartElevation = section != null ? section.StartFrame.Position.y : 0f,
                    EndElevation = section != null ? section.EndFrame.Position.y : 0f,
                    RoadId = definition.RoadId,
                    CanonicalLapEnd = i == canonicalLapEnd
                });
            }
            return result;
        }

        /// <summary>
        /// Restores vertical values only where a stable section identity survived the
        /// edit. Replacement geometry and removed AOI members deliberately do not
        /// match; the normal authoring recovery then balances their local difference.
        /// </summary>
        public static int RestoreAuthoringElevationBaseline(
            IList<TrackMacroSectionDefinition> definitions,
            IReadOnlyList<AuthoringElevationBaselineEntry> baseline)
        {
            if (definitions == null || definitions.Count == 0 ||
                baseline == null || baseline.Count == 0)
                return 0;

            var byKey = new Dictionary<string, AuthoringElevationBaselineEntry>(
                StringComparer.Ordinal);
            for (int i = 0; i < baseline.Count; i++)
            {
                AuthoringElevationBaselineEntry entry = baseline[i];
                if (entry == null || string.IsNullOrEmpty(entry.MatchKey)) continue;
                byKey[entry.MatchKey] = entry;
            }

            List<string> keys = BuildAuthoringElevationMatchKeys(definitions);
            var matches = new List<(int Index, AuthoringElevationBaselineEntry Entry)>();
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null || !byKey.TryGetValue(keys[i], out var entry))
                    continue;
                matches.Add((i, entry));
            }

            // A partial cache from stale preview data must never flatten or hybridize a
            // route. Focused replacements normally retain nearly the whole sequence;
            // require at least half of the smaller stream before treating it as the
            // authoritative accepted elevation plan.
            int minimumMatches = Mathf.Max(2,
                Mathf.CeilToInt(Mathf.Min(definitions.Count, baseline.Count) * 0.5f));
            if (matches.Count < minimumMatches) return 0;

            for (int i = 0; i < matches.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[matches[i].Index];
                definition.ElevationChange = matches[i].Entry.ElevationChange;
                definition.HillHeight = matches[i].Entry.HillHeight;
            }
            return matches.Count;
        }

        private static List<string> BuildAuthoringElevationMatchKeys(
            IList<TrackMacroSectionDefinition> definitions)
        {
            var keys = new List<string>(definitions?.Count ?? 0);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            if (definitions == null) return keys;

            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null)
                {
                    keys.Add("");
                    continue;
                }

                string stem;
                if (!string.IsNullOrEmpty(definition.PatternId))
                {
                    stem = "pattern|" + definition.PatternId + "|" +
                           definition.SectionType + "|" +
                           NormalizeAuthoringElevationDebugName(definition.DebugName);
                }
                else if (!string.IsNullOrEmpty(definition.TopologySlotId))
                {
                    stem = "slot|" + definition.TopologySlotId + "|" +
                           definition.SectionType + "|" +
                           NormalizeAuthoringElevationDebugName(definition.DebugName);
                }
                else
                {
                    stem = "route|" + definition.QuarterIndex + "|" + definition.RoadId +
                           "|" + NormalizeAuthoringElevationSectionType(definition.SectionType) +
                           "|" + NormalizeAuthoringElevationDebugName(definition.DebugName);
                }

                occurrences.TryGetValue(stem, out int occurrence);
                occurrences[stem] = occurrence + 1;
                keys.Add(stem + "|#" + occurrence);
            }
            return keys;
        }

        private static TrackMacroSectionType NormalizeAuthoringElevationSectionType(
            TrackMacroSectionType sectionType)
        {
            return sectionType == TrackMacroSectionType.BridgeVariant ||
                   sectionType == TrackMacroSectionType.TunnelVariant
                ? TrackMacroSectionType.Straight
                : sectionType;
        }

        private static string NormalizeAuthoringElevationDebugName(string debugName)
        {
            string value = debugName ?? "";
            int cut = value.IndexOf("_Authoring", StringComparison.Ordinal);
            if (cut < 0) cut = value.IndexOf("_Climb", StringComparison.Ordinal);
            if (cut < 0) cut = value.IndexOf("_Drop", StringComparison.Ordinal);
            return cut >= 0 ? value.Substring(0, cut) : value;
        }

        // ═══════════════════════════ 7. Elevation plan ═══════════════════════════

        /// <summary>
        /// Assigns intentional vertical content AFTER closure: major climbs and drops
        /// (a level walk that must return to zero), then bridges, underpasses and crests.
        /// A spiral/corkscrew may subsequently absorb the complete run and elevation of
        /// its own recovery when legal; every resulting feature still has flat welds.
        /// </summary>
        private void PlanElevation(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, ref Unity.Mathematics.Random rng)
        {
            var defs = plan.Defs;

            // Directly-constructed resolved configs are used by focused tooling and
            // older tests. Preserve the historical Balanced constants when those
            // callers have not passed through ResolvedTrackGenerationConfig.Resolve.
            float preferredCarrierLength = cfg.ElevationPreferredCarrierLength > 0f
                ? cfg.ElevationPreferredCarrierLength
                : 100f;
            float risingTargetMinimum = cfg.ElevationRisingTargetMaximum > 0f
                ? cfg.ElevationRisingTargetMinimum
                : 0.4f;
            float risingTargetMaximum = cfg.ElevationRisingTargetMaximum > 0f
                ? cfg.ElevationRisingTargetMaximum
                : 1f;
            float recoveryThreshold = cfg.ElevationRecoveryThreshold > 0f
                ? cfg.ElevationRecoveryThreshold
                : 0.5f;
            float recoveryTargetFraction = cfg.ElevationRecoveryTargetFraction > 0f
                ? cfg.ElevationRecoveryTargetFraction
                : 0.4f;

            float fixedElevation = 0f;
            foreach (var d in defs)
            {
                if (d.SectionType == TrackMacroSectionType.Spiral ||
                    d.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                    d.SectionType == TrackMacroSectionType.RotationalEvent ||
                    IsTrackEditorVerticalRecovery(d))
                    fixedElevation += d.ElevationChange;
            }
            bool needCompensation = Mathf.Abs(fixedElevation) > 1f;

            if (cfg.TargetElevationAmplitude < 6f && !needCompensation) return;

            bool AuthoredVerticalBoundary(TrackMacroSectionDefinition definition) =>
                definition != null &&
                (Mathf.Abs(definition.HillHeight) > 0.001f ||
                 definition.SectionType == TrackMacroSectionType.JumpRamp ||
                 definition.SectionType == TrackMacroSectionType.AirGap ||
                 definition.SectionType == TrackMacroSectionType.LandingRamp ||
                 definition.SectionType == TrackMacroSectionType.Loop ||
                 definition.SectionType == TrackMacroSectionType.Corkscrew ||
                 definition.SectionType == TrackMacroSectionType.Spiral ||
                 definition.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                 definition.SectionType == TrackMacroSectionType.FullPipe ||
                 definition.SectionType == TrackMacroSectionType.RotationalEvent);

            bool NearAuthoredVerticalBoundary(int index)
            {
                // Feature approaches/recoveries are usually one definition long. A
                // two-section guard prevents a major climb from terminating directly
                // at a loop/corkscrew/spiral/jump mouth, which created both harsh
                // transitions and plan-vs-built feature-footprint drift.
                for (int offset = -2; offset <= 2; offset++)
                {
                    if (offset == 0) continue;
                    int neighbor = index + offset;
                    if (neighbor < 0 || neighbor >= defs.Count) continue;
                    if (AuthoredVerticalBoundary(defs[neighbor])) return true;
                }
                return false;
            }

            // Carriers: plain (non-safety) straights, with protected feature mouths
            // kept level. Ordinary elevation still has the remaining gaps to use.
            var eligible = new List<int>();
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                bool carrier = (d.SectionType == TrackMacroSectionType.Straight ||
                                d.SectionType == TrackMacroSectionType.WideStraight ||
                                d.SectionType == TrackMacroSectionType.BoostStraight)
                               && !d.LockLength && d.Length >= 100f;
                // Structural connectors now have independent compact entry/recovery
                // roads between them and an authored feature mouth. They are therefore
                // safe elevation carriers even when the old +/-2 definition guard sees
                // a nearby loop, jump, corkscrew or spiral. Keeping them excluded left
                // most dense-feature layouts with no legal way to close their altitude.
                carrier &= d.FeatureFitRole == FeatureFitRole.StructuralConnector ||
                           !NearAuthoredVerticalBoundary(i);
                if (carrier) eligible.Add(i);
            }

            if (eligible.Count < 2)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    if (d.SectionType == TrackMacroSectionType.RecoveryStraight &&
                        !IsTrackEditorVerticalRecovery(d) &&
                        d.Length >= 150f && !eligible.Contains(i))
                        eligible.Add(i);
                }
                eligible.Sort();
            }

            if (eligible.Count < 2)
            {
                if (needCompensation)
                    plan.Fail(GenerationFailureReason.ClosureElevationFailure,
                        $"Spirals/half-loops carry {fixedElevation:F0}m of net elevation but no straights can pay it back.");
                return;
            }

            // Profiles may prefer longer, more readable elevation carriers. This is a
            // preference rather than a new rejection rule: if the route does not offer
            // enough long straights, retain the original eligible set and stay valid.
            if (preferredCarrierLength > 100f)
            {
                var preferred = new List<int>();
                foreach (int idx in eligible)
                    if (defs[idx].Length >= preferredCarrierLength)
                        preferred.Add(idx);
                if (preferred.Count >= 2)
                    eligible = preferred;
            }

            // ── Major climbs/drops ──
            int majors = Mathf.Clamp(rng.NextInt(cfg.MinMajorElevationSections, cfg.MaxMajorElevationSections + 1), 0, eligible.Count);
            if (needCompensation) majors = Mathf.Clamp(Mathf.Max(majors, 2), 0, eligible.Count);
            var majorSet = new HashSet<int>();

            if (majors >= 2)
            {
                var byLength = new List<int>(eligible);
                byLength.Sort((a, b) => defs[b].Length.CompareTo(defs[a].Length));
                foreach (int idx in byLength)
                {
                    if (majorSet.Count >= majors) break;
                    majorSet.Add(idx);
                }

                // The chain-level vertical solver must ease into and out of the grade
                // while respecting curvature at design speed. The old /1.5 estimate
                // described a sustained slope, not a drivable eased profile, and was
                // allowing 300-450 m rises in ~800 m. Reserve four lengths of shaping
                // room so planned elevation reaches the builder legally and gradually.
                float Capacity(int idx, bool up) =>
                    AuthoringElevationMagnitudeLimit(cfg, defs[idx], up);

                var order = new List<int>();
                foreach (int idx in eligible) if (majorSet.Contains(idx)) order.Add(idx);

                var deltas = new float[order.Count];

                // Level window: KeepAboveStart walks [0, amplitude]; FreeFloating is symmetric.
                float lo = cfg.GroundLevelPolicy == TrackGroundLevelPolicy.KeepAboveStart ? 0f : -cfg.TargetElevationAmplitude * 0.5f;
                float hi = cfg.GroundLevelPolicy == TrackGroundLevelPolicy.KeepAboveStart ? cfg.TargetElevationAmplitude : cfg.TargetElevationAmplitude * 0.5f;

                float level = fixedElevation;
                for (int k = 0; k < order.Count; k++)
                {
                    float delta;
                    if (k == order.Count - 1)
                    {
                        delta = -level;
                    }
                    else
                    {
                        float target = level < Mathf.Lerp(lo, hi, recoveryThreshold)
                            ? rng.NextFloat(risingTargetMinimum, risingTargetMaximum) * hi
                            : rng.NextFloat(lo, level * recoveryTargetFraction);
                        delta = target - level;
                    }

                    delta = Mathf.Clamp(delta, -Capacity(order[k], false), Capacity(order[k], true));
                    deltas[k] = delta;
                    level += delta;
                }

                // Residual bleed: push leftover level into majors that still have capacity.
                for (int pass = 0; pass < 4 && Mathf.Abs(level) > 0.25f; pass++)
                {
                    for (int k = order.Count - 1; k >= 0 && Mathf.Abs(level) > 0.25f; k--)
                    {
                        float cap = Mathf.Min(Capacity(order[k], true), Capacity(order[k], false));
                        float take = Mathf.Clamp(-level, -cap - deltas[k], cap - deltas[k]);
                        deltas[k] += take;
                        level += take;
                    }
                }

                // Designer-authored replacements may consume or shorten one of the
                // ordinary carriers that balanced the accepted track. Recovery
                // straights already have level boundary contracts and eased profiles,
                // so unused recoveries are the safest final place to distribute a
                // small residual without weakening the slope limits or changing a
                // feature's internal geometry.
                if (cfg.DesignerAuthoringMode && Mathf.Abs(level) > 0.25f)
                {
                    float beforeRecovery = level;
                    level = ApplyAuthoringElevationRecovery(
                        cfg, defs, majorSet, level);
                    if (Mathf.Abs(level) + 0.01f < Mathf.Abs(beforeRecovery))
                        plan.Warnings.Add(
                            $"Track Editor distributed {Mathf.Abs(beforeRecovery - level):F1}m of elevation recovery across safe recovery straights.");
                }
                else if (Mathf.Abs(level) > 0.25f)
                {
                    float beforeRecovery = level;
                    level = ApplyProceduralElevationRecovery(
                        cfg, defs, majorSet, level);
                    if (Mathf.Abs(level) + 0.01f < Mathf.Abs(beforeRecovery))
                        plan.Warnings.Add(
                            $"Elevation closure distributed {Mathf.Abs(beforeRecovery - level):F1}m across unused safe route roads.");
                }
                if (Mathf.Abs(level) > 0.5f)
                {
                    plan.Fail(GenerationFailureReason.ClosureElevationFailure,
                        $"Elevation residual {level:F1}m exceeds slope capacity — the lap cannot return to its start height legally.");
                    return;
                }
                if (Mathf.Abs(level) > 0.001f) deltas[deltas.Length - 1] -= level;

                for (int k = 0; k < order.Count; k++)
                {
                    var d = defs[order[k]];
                    d.ElevationChange = deltas[k];
                    if (Mathf.Abs(deltas[k]) >= 1f)
                        d.DebugName += deltas[k] > 0f ? $"_Climb{deltas[k]:F0}m" : $"_Drop{-deltas[k]:F0}m";
                }
            }

            // ── Bridges / underpasses / crests on the remaining carriers ──
            int bridgesPlaced = 0, underPlaced = 0, crestsPlaced = 0;

            foreach (int idx in eligible)
            {
                if (majorSet.Contains(idx)) continue;

                var d = defs[idx];
                float hillCap = d.Length * Mathf.Tan(cfg.MaxClimbAngle * Mathf.Deg2Rad) / 3.1f;

                bool mustBridge = cfg.Bridges.Enabled && bridgesPlaced < cfg.Bridges.MinimumCount;
                bool mustUnder = cfg.Underpasses.Enabled && underPlaced < cfg.Underpasses.MinimumCount;
                bool mustCrest = cfg.Crests.Enabled && crestsPlaced < cfg.Crests.MinimumCount;

                float wBridge = cfg.Bridges.Enabled && bridgesPlaced < cfg.Bridges.MaximumCount ? cfg.Bridges.OptionalWeight : 0f;
                float wUnder = cfg.Underpasses.Enabled && underPlaced < cfg.Underpasses.MaximumCount ? cfg.Underpasses.OptionalWeight : 0f;
                float wCrest = cfg.Crests.Enabled && crestsPlaced < cfg.Crests.MaximumCount ? cfg.Crests.OptionalWeight : 0f;

                int choice = 0; // 0 none, 1 bridge, 2 underpass, 3 crest
                if (mustBridge) choice = 1;
                else if (mustUnder) choice = 2;
                else if (mustCrest) choice = 3;
                else
                {
                    float total = wBridge + wUnder + wCrest;
                    if (total > 0f && rng.NextFloat() < 0.45f)
                    {
                        float pick = rng.NextFloat(0f, total);
                        if (pick < wBridge) choice = 1;
                        else if (pick < wBridge + wUnder) choice = 2;
                        else choice = 3;
                    }
                }

                switch (choice)
                {
                    case 1 when d.Length >= 250f:
                        d.SectionType = TrackMacroSectionType.BridgeVariant;
                        d.HillHeight = Mathf.Min(rng.NextFloat(25f, 90f), hillCap);
                        d.DebugName = $"Bridge_H{d.HillHeight:F0}m";
                        bridgesPlaced++;
                        break;
                    case 2 when d.Length >= 220f:
                        d.SectionType = TrackMacroSectionType.TunnelVariant;
                        d.HillHeight = -Mathf.Min(rng.NextFloat(20f, 60f), hillCap);
                        d.DebugName = $"Underpass_D{-d.HillHeight:F0}m";
                        underPlaced++;
                        break;
                    case 3:
                        float h = Mathf.Min(rng.NextFloat(10f, 40f), hillCap);
                        bool dip = cfg.GroundLevelPolicy == TrackGroundLevelPolicy.FreeFloating && rng.NextFloat() < 0.3f;
                        d.HillHeight = dip ? -h : h;
                        d.DebugName += dip ? $"_Dip{h:F0}m" : $"_Crest{h:F0}m";
                        crestsPlaced++;
                        break;
                }
            }

            if (cfg.Bridges.Enabled && bridgesPlaced < cfg.Bridges.MinimumCount)
                plan.Fail(GenerationFailureReason.RequiredFeatureMissing,
                    $"Required {cfg.Bridges.MinimumCount} bridges, placed {bridgesPlaced} — not enough long straights.");
            else if (cfg.Underpasses.Enabled && underPlaced < cfg.Underpasses.MinimumCount)
                plan.Fail(GenerationFailureReason.RequiredFeatureMissing,
                    $"Required {cfg.Underpasses.MinimumCount} underpasses, placed {underPlaced} — not enough long straights.");

            if (!plan.Failed)
                IntegrateFeatureRecoveries(cfg, plan);
        }

        private const string TrackEditorVerticalRecoveryPrefix = "TrackEditorVerticalRecovery";

        private static bool IsAuthoringElevationCarrier(
            TrackMacroSectionDefinition definition)
        {
            if (definition == null || definition.RoadId != 0 ||
                Mathf.Abs(definition.HillHeight) > 0.001f)
                return false;
            switch (definition.SectionType)
            {
                case TrackMacroSectionType.Straight:
                case TrackMacroSectionType.WideStraight:
                case TrackMacroSectionType.BoostStraight:
                case TrackMacroSectionType.RecoveryStraight:
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                case TrackMacroSectionType.SCurve:
                case TrackMacroSectionType.Chicane:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsTrackEditorVerticalRecovery(
            TrackMacroSectionDefinition definition) =>
            definition != null &&
            definition.SectionType == TrackMacroSectionType.RecoveryStraight &&
            !string.IsNullOrEmpty(definition.DebugName) &&
                definition.DebugName.StartsWith(TrackEditorVerticalRecoveryPrefix,
                    System.StringComparison.Ordinal);

        /// <summary>
        /// Conservative elevation envelope for a level-to-level Smooth01 road carrier.
        /// Besides grade, this owns the same high-speed vertical-curvature and curvature-
        /// rate limits enforced by the final frame validator. Keeping one shared envelope
        /// prevents a clearance/elevation rescue from fixing topology and then failing the
        /// finished mesh on a transition it just introduced.
        /// </summary>
        private static float AuthoringElevationMagnitudeLimit(
            ResolvedTrackGenerationConfig cfg,
            TrackMacroSectionDefinition definition,
            bool climb)
        {
            if (cfg == null || definition == null || definition.Length <= 0f ||
                Mathf.Abs(definition.HillHeight) > 0.001f)
                return 0f;

            float length = definition.Length;
            float angle = climb ? cfg.MaxClimbAngle : cfg.MaxDropAngle;
            float limit = length * Mathf.Tan(Mathf.Max(0.1f, angle) * Mathf.Deg2Rad) / 4f;

            if (cfg.MaxCurvatureInducedG > 0f && cfg.Gravity > 0f && cfg.DesignSpeedMps > 0f)
            {
                float maxCurvature = cfg.MaxCurvatureInducedG * Mathf.Max(0.1f, cfg.Gravity) /
                                     Mathf.Max(1f, cfg.DesignSpeedMps * cfg.DesignSpeedMps);
                // Smooth01's analytic peak is 6*delta/L^2. Eight leaves room for
                // discrete rings and the incoming weld's existing curvature.
                limit = Mathf.Min(limit, maxCurvature * length * length / 8f);
            }
            if (cfg.MaxVerticalCurvatureRate > 0f)
            {
                // The analytic rate scale is 12*delta/L^3. Eighteen preserves the
                // validator's margin at coarse mesh densities.
                limit = Mathf.Min(limit, cfg.MaxVerticalCurvatureRate * length * length * length / 18f);
            }
            return Mathf.Max(0f, limit);
        }

        private static float AvailableAuthoringElevationDelta(
            ResolvedTrackGenerationConfig cfg,
            TrackMacroSectionDefinition definition,
            float requestedDirection)
        {
            if (definition == null) return 0f;
            if (requestedDirection >= 0f)
                return Mathf.Max(0f,
                    AuthoringElevationMagnitudeLimit(cfg, definition, climb: true) -
                    definition.ElevationChange);
            return Mathf.Max(0f,
                definition.ElevationChange +
                AuthoringElevationMagnitudeLimit(cfg, definition, climb: false));
        }

        /// <summary>
        /// Gives a designer-authored vertical replacement its own legal return-to-grade
        /// road before closure is solved. The road is locked because its length is the
        /// conservative eased-slope requirement, not optional procedural padding.
        /// Returns false when the candidate is already vertically self-contained.
        /// </summary>
        public static bool EnsureAuthoringVerticalRecovery(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            out float recoveryLength,
            out float recoveryElevation)
        {
            recoveryLength = 0f;
            recoveryElevation = 0f;
            if (cfg == null || definitions == null || definitions.Count == 0)
                return false;

            float netElevation = 0f;
            string patternId = "";
            float width = Mathf.Max(1f, cfg.RoadWidth);
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null) continue;
                netElevation += definition.ElevationChange;
                width = Mathf.Max(1f, definition.Width);
                if (string.IsNullOrEmpty(patternId) &&
                    !string.IsNullOrEmpty(definition.PatternId))
                    patternId = definition.PatternId;
            }
            if (Mathf.Abs(netElevation) <= 0.5f) return false;

            bool needsDrop = netElevation > 0f;
            float legalAngle = needsDrop ? cfg.MaxDropAngle : cfg.MaxClimbAngle;
            float tangent = Mathf.Tan(Mathf.Max(0.1f, legalAngle) * Mathf.Deg2Rad);
            // The elevation builder uses four shaping lengths for a level-to-level
            // eased grade. Five percent headroom prevents an exact-limit float fit.
            float elevationMagnitude = Mathf.Abs(netElevation);
            float requiredForSlope = elevationMagnitude * 4f /
                                     Mathf.Max(0.001f, tangent);
            float requiredForCurvature = 0f;
            if (cfg.MaxCurvatureInducedG > 0f && cfg.Gravity > 0f && cfg.DesignSpeedMps > 0f)
            {
                float maxCurvature = cfg.MaxCurvatureInducedG * Mathf.Max(0.1f, cfg.Gravity) /
                                     Mathf.Max(1f, cfg.DesignSpeedMps * cfg.DesignSpeedMps);
                requiredForCurvature = Mathf.Sqrt(8f * elevationMagnitude /
                                                   Mathf.Max(0.0000001f, maxCurvature));
            }
            float requiredForCurvatureRate = cfg.MaxVerticalCurvatureRate > 0f
                ? Mathf.Pow(18f * elevationMagnitude / cfg.MaxVerticalCurvatureRate, 1f / 3f)
                : 0f;
            recoveryLength = Mathf.Max(cfg.DefaultRecoveryLength,
                Mathf.Max(requiredForSlope,
                    Mathf.Max(requiredForCurvature, requiredForCurvatureRate)) * 1.05f);
            recoveryElevation = -netElevation;

            TrackMacroSectionDefinition recovery = SectionDefs.Straight(
                TrackMacroSectionType.RecoveryStraight, recoveryLength, width,
                $"{TrackEditorVerticalRecoveryPrefix}_" +
                $"{(needsDrop ? "Drop" : "Climb")}{Mathf.Abs(netElevation):F0}m",
                locked: true, patternId: patternId,
                fitRole: FeatureFitRole.VerticalRecovery);
            recovery.ElevationChange = recoveryElevation;
            definitions.Add(recovery);
            return true;
        }

        /// <summary>
        /// Balances the complete canonical definition stream after a focused Area of
        /// Impact edit. Prefer reducing an existing elevation carrier before adding
        /// new grade, then use the longest remaining ordinary roads. The same
        /// curvature/rate-aware capacity envelope used by authoring recovery remains
        /// authoritative. Returns the unassigned residual.
        /// </summary>
        public static float ReconcileAuthoringNetElevation(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            out float appliedElevation)
        {
            appliedElevation = 0f;
            if (cfg == null || definitions == null || !cfg.DesignerAuthoringMode)
                return 0f;

            float residual = 0f;
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition != null && definition.RoadId == 0)
                    residual += definition.ElevationChange;
            }
            if (Mathf.Abs(residual) <= 0.25f) return residual;

            var carriers = new List<int>();
            for (int i = 0; i < definitions.Count; i++)
                if (IsAuthoringElevationCarrier(definitions[i]) &&
                    definitions[i].Length >= 150f)
                    carriers.Add(i);

            carriers.Sort((a, b) =>
            {
                TrackMacroSectionDefinition da = definitions[a];
                TrackMacroSectionDefinition db = definitions[b];
                bool aReducesExisting = Mathf.Sign(da.ElevationChange) == Mathf.Sign(residual) &&
                                        Mathf.Abs(da.ElevationChange) > 0.01f;
                bool bReducesExisting = Mathf.Sign(db.ElevationChange) == Mathf.Sign(residual) &&
                                        Mathf.Abs(db.ElevationChange) > 0.01f;
                if (aReducesExisting != bReducesExisting)
                    return aReducesExisting ? -1 : 1;
                int priority = AuthoringCarrierPriority(da).CompareTo(
                    AuthoringCarrierPriority(db));
                if (priority != 0) return priority;
                return db.Length.CompareTo(da.Length);
            });

            for (int i = 0; i < carriers.Count && Mathf.Abs(residual) > 0.25f; i++)
            {
                TrackMacroSectionDefinition carrier = definitions[carriers[i]];
                float requested = -residual;
                float capacity = AvailableAuthoringElevationDelta(cfg, carrier, requested);
                float delta = Mathf.Sign(requested) *
                              Mathf.Min(Mathf.Abs(requested), capacity);
                if (Mathf.Abs(delta) <= 0.001f) continue;

                carrier.ElevationChange += delta;
                SectionConnectionContract contract = carrier.Contract;
                contract.ElevationDelta = carrier.ElevationChange;
                carrier.Contract = contract;
                carrier.DebugName += delta > 0f
                    ? $"_AuthoringLapBalanceClimb{delta:F0}m"
                    : $"_AuthoringLapBalanceDrop{-delta:F0}m";
                residual += delta;
                appliedElevation += delta;
            }

            return residual;
        }

        /// <summary>
        /// Measures the canonical road with the same section builders used by the
        /// candidate. This catches real rotational/ballistic displacement that cannot
        /// be inferred safely by summing Definition.ElevationChange.
        /// </summary>
        public static float MeasureCanonicalBuiltElevation(
            ResolvedTrackGenerationConfig cfg,
            IList<TrackMacroSectionDefinition> definitions)
        {
            if (!TryMeasureCanonicalBuiltElevations(cfg, definitions,
                    out _, out float finalElevation))
                return float.NaN;
            return finalElevation;
        }

        private static bool TryMeasureCanonicalBuiltElevations(
            ResolvedTrackGenerationConfig cfg,
            IList<TrackMacroSectionDefinition> definitions,
            out float[] startElevations,
            out float finalElevation)
        {
            startElevations = new float[definitions?.Count ?? 0];
            for (int i = 0; i < startElevations.Length; i++)
                startElevations[i] = float.NaN;
            finalElevation = 0f;
            if (cfg == null || definitions == null) return false;

            TrackConnectionFrame frame = TrackConnectionFrame.Origin(cfg.RoadWidth);
            FrameBuildContext context = FrameBuildContext.From(cfg);
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null || definition.RoadId != 0) continue;
                startElevations[i] = frame.Position.y;
                if (definition.SectionType == TrackMacroSectionType.AirGap)
                {
                    frame = SectionFrameBuilders.AirGapLanding(frame, definition);
                    continue;
                }

                TrackConnectionFrame[] frames = SectionFrameBuilders.BuildSectionFrames(
                    frame, definition, context);
                if (frames == null || frames.Length == 0) return false;
                frame = frames[frames.Length - 1];
            }
            finalElevation = frame.Position.y;
            return true;
        }

        private static float ApplyAuthoringElevationDeltaInRange(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            bool[] protectedDefinitions,
            int fromInclusive,
            int toExclusive,
            float requestedDelta,
            string debugTag,
            out float appliedDelta)
        {
            appliedDelta = 0f;
            float residual = requestedDelta;
            var carriers = new List<int>();
            int from = Mathf.Clamp(fromInclusive, 0, definitions.Count);
            int to = Mathf.Clamp(toExclusive, from, definitions.Count);
            for (int i = from; i < to; i++)
            {
                if (protectedDefinitions != null && i < protectedDefinitions.Length &&
                    protectedDefinitions[i])
                    continue;
                TrackMacroSectionDefinition definition = definitions[i];
                if (IsAuthoringElevationCarrier(definition) && definition.Length >= 150f)
                    carriers.Add(i);
            }
            carriers.Sort((a, b) =>
            {
                int priority = AuthoringCarrierPriority(definitions[a]).CompareTo(
                    AuthoringCarrierPriority(definitions[b]));
                return priority != 0
                    ? priority
                    : definitions[b].Length.CompareTo(definitions[a].Length);
            });

            for (int i = 0; i < carriers.Count && Mathf.Abs(residual) > 0.25f; i++)
            {
                TrackMacroSectionDefinition carrier = definitions[carriers[i]];
                float capacity = AvailableAuthoringElevationDelta(cfg, carrier, residual);
                float delta = Mathf.Sign(residual) *
                              Mathf.Min(Mathf.Abs(residual), capacity);
                if (Mathf.Abs(delta) <= 0.001f) continue;
                carrier.ElevationChange += delta;
                SectionConnectionContract contract = carrier.Contract;
                contract.ElevationDelta = carrier.ElevationChange;
                carrier.Contract = contract;
                carrier.DebugName += delta > 0f
                    ? $"_{debugTag}Climb{delta:F0}m"
                    : $"_{debugTag}Drop{-delta:F0}m";
                residual -= delta;
                appliedDelta += delta;
            }
            return residual;
        }

        private static int AuthoringCarrierPriority(TrackMacroSectionDefinition definition)
        {
            if (definition == null) return int.MaxValue;
            switch (definition.SectionType)
            {
                case TrackMacroSectionType.RecoveryStraight: return 0;
                case TrackMacroSectionType.Straight:
                case TrackMacroSectionType.WideStraight: return 1;
                case TrackMacroSectionType.BoostStraight: return 2;
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.SCurve:
                case TrackMacroSectionType.Chicane: return 3;
                case TrackMacroSectionType.BankedHairpin: return 4;
                default: return 5;
            }
        }

        /// <summary>
        /// Designer-first elevation reconciliation. Exact accepted altitude layers are
        /// attempted first. When their protected local window has insufficient capacity,
        /// the route remains continuous and the editor is allowed to redistribute legal
        /// grade over ordinary/recovery road instead of rejecting the requested feature.
        /// Final curvature, transition, clearance and closure validators remain unchanged.
        /// </summary>
        public static float ReconcileAuthoringElevationForEditor(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            IReadOnlyList<AuthoringElevationBaselineEntry> baseline,
            out float appliedElevation,
            out bool relaxedAcceptedLayers)
        {
            float strictResidual = ReconcileAuthoringElevationAnchors(
                cfg, definitions, baseline, out float strictApplied);
            appliedElevation = strictApplied;
            relaxedAcceptedLayers = Mathf.Abs(strictResidual) > 0.5f;
            if (!relaxedAcceptedLayers) return strictResidual;

            // Exact altitude layers are a preservation preference, not a driveability
            // requirement. The rebuilt definition stream is already continuous; balance
            // its measured lap endpoint using the broadest safe road capacity.
            float flexibleResidual = ReconcileAuthoringBuiltElevation(
                cfg, definitions, out float flexibleApplied);
            appliedElevation += flexibleApplied;
            return flexibleResidual;
        }

        /// <summary>
        /// Preserves the accepted route's actual altitude layers outside the edited
        /// window. Only definitions that no longer match the accepted route may carry
        /// the reconnecting grade, so an AOI edit cannot silently move a liked feature.
        /// Returns the first unassigned anchor error (or final lap weld error).
        /// </summary>
        public static float ReconcileAuthoringElevationAnchors(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            IReadOnlyList<AuthoringElevationBaselineEntry> baseline,
            out float appliedElevation)
        {
            appliedElevation = 0f;
            if (cfg == null || definitions == null || baseline == null ||
                !cfg.DesignerAuthoringMode)
                return 0f;

            var byKey = new Dictionary<string, AuthoringElevationBaselineEntry>(
                StringComparer.Ordinal);
            float desiredLapEnd = 0f;
            bool hasLapEnd = false;
            for (int i = 0; i < baseline.Count; i++)
            {
                AuthoringElevationBaselineEntry entry = baseline[i];
                if (entry == null || string.IsNullOrEmpty(entry.MatchKey)) continue;
                byKey[entry.MatchKey] = entry;
                if (entry.CanonicalLapEnd)
                {
                    desiredLapEnd = entry.EndElevation;
                    hasLapEnd = true;
                }
            }

            List<string> keys = BuildAuthoringElevationMatchKeys(definitions);
            var anchors = new List<(int Index, AuthoringElevationBaselineEntry Entry)>();
            var protectedDefinitions = new bool[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                if (!byKey.TryGetValue(keys[i], out var entry)) continue;
                protectedDefinitions[i] = true;
                if (definitions[i] != null && definitions[i].RoadId == 0 && entry.RoadId == 0)
                    anchors.Add((i, entry));
            }
            if (anchors.Count == 0) return 0f;

            int previousAnchor = -1;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (!TryMeasureCanonicalBuiltElevations(cfg, definitions,
                        out float[] starts, out _))
                    return float.PositiveInfinity;
                int index = anchors[i].Index;
                float error = anchors[i].Entry.StartElevation - starts[index];
                if (Mathf.Abs(error) > 0.25f)
                {
                    float residual = ApplyAuthoringElevationDeltaInRange(
                        cfg, definitions, protectedDefinitions, previousAnchor + 1, index,
                        error, "AuthoringAnchor", out float applied);
                    appliedElevation += applied;
                    if (Mathf.Abs(residual) > 0.5f) return residual;
                }
                previousAnchor = index;
            }

            if (!TryMeasureCanonicalBuiltElevations(cfg, definitions,
                    out _, out float lapEnd))
                return float.PositiveInfinity;
            float lapError = (hasLapEnd ? desiredLapEnd : 0f) - lapEnd;
            if (Mathf.Abs(lapError) <= 0.25f) return lapError;
            float lapResidual = ApplyAuthoringElevationDeltaInRange(
                cfg, definitions, protectedDefinitions, previousAnchor + 1,
                definitions.Count, lapError, "AuthoringWeld", out float lapApplied);
            appliedElevation += lapApplied;
            if (Mathf.Abs(lapResidual) > 0.5f) return lapResidual;
            return MeasureCanonicalBuiltElevation(cfg, definitions) -
                   (hasLapEnd ? desiredLapEnd : 0f);
        }

        /// <summary>
        /// Fallback for an authoring rebuild without an accepted altitude baseline.
        /// Reconciles the true frame-builder endpoint on any remaining legal carriers.
        /// </summary>
        public static float ReconcileAuthoringBuiltElevation(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            out float appliedElevation)
        {
            appliedElevation = 0f;
            if (cfg == null || definitions == null || !cfg.DesignerAuthoringMode)
                return 0f;
            float built = MeasureCanonicalBuiltElevation(cfg, definitions);
            if (float.IsNaN(built) || float.IsInfinity(built)) return built;
            float residual = ApplyAuthoringElevationDeltaInRange(
                cfg, definitions, null, 0, definitions.Count, -built,
                "AuthoringBuiltBalance", out float applied);
            appliedElevation = applied;
            if (Mathf.Abs(residual) > 0.5f) return -residual;
            return MeasureCanonicalBuiltElevation(cfg, definitions);
        }

        /// <summary>
        /// Pays back a focused Track Editor edit's remaining vertical displacement on
        /// unused recovery straights. Every carrier retains the same conservative eased
        /// slope capacity as an ordinary major climb/drop and remains level at its welds.
        /// Returns the residual that could not be assigned.
        /// </summary>
        public static float ApplyAuthoringElevationRecovery(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            HashSet<int> occupiedCarriers,
            float residual)
        {
            if (cfg == null || definitions == null || Mathf.Abs(residual) <= 0.25f)
                return residual;

            var recoveries = new List<int>();
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null ||
                    definition.SectionType != TrackMacroSectionType.RecoveryStraight ||
                    IsTrackEditorVerticalRecovery(definition) ||
                    definition.Length < 150f ||
                    (occupiedCarriers != null && occupiedCarriers.Contains(i)))
                    continue;
                recoveries.Add(i);
            }
            recoveries.Sort((a, b) => definitions[b].Length.CompareTo(definitions[a].Length));

            for (int i = 0; i < recoveries.Count && Mathf.Abs(residual) > 0.25f; i++)
            {
                int index = recoveries[i];
                TrackMacroSectionDefinition definition = definitions[index];
                float requested = -residual;
                float capacity = AvailableAuthoringElevationDelta(cfg, definition, requested);
                float delta = Mathf.Sign(requested) *
                              Mathf.Min(Mathf.Abs(requested), capacity);
                if (Mathf.Abs(delta) <= 0.001f) continue;

                definition.ElevationChange += delta;
                definition.DebugName += delta > 0f
                    ? $"_AuthoringClimb{delta:F0}m"
                    : $"_AuthoringDrop{-delta:F0}m";
                occupiedCarriers?.Add(index);
                residual += delta;
            }

            return residual;
        }

        /// <summary>
        /// Uses unused level-welded route roads as a final, legally eased altitude
        /// payback. Structural connectors are preferred, then ordinary/recovery roads;
        /// authored feature warmups and dedicated editor recoveries remain untouched.
        /// This restores closure capacity without lengthening or deforming a feature.
        /// The same conservative grade/curvature envelope used by Track Editor recovery
        /// applies here; any residual beyond that capacity still fails normally.
        /// </summary>
        public static float ApplyProceduralElevationRecovery(
            ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> definitions,
            HashSet<int> occupiedCarriers,
            float residual)
        {
            if (cfg == null || definitions == null || Mathf.Abs(residual) <= 0.25f)
                return residual;

            var corridors = new List<int>();
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                bool safeRole = definition != null &&
                    (definition.FeatureFitRole == FeatureFitRole.None ||
                     definition.FeatureFitRole == FeatureFitRole.StructuralConnector ||
                     definition.FeatureFitRole == FeatureFitRole.ExitRecovery ||
                     definition.FeatureFitRole == FeatureFitRole.SharedCorridor);
                if (definition == null || definition.RoadId != 0 ||
                    !safeRole ||
                    Mathf.Abs(definition.HillHeight) > 0.001f ||
                    IsTrackEditorVerticalRecovery(definition) ||
                    definition.Length < 100f ||
                    (occupiedCarriers != null && occupiedCarriers.Contains(i)))
                    continue;
                switch (definition.SectionType)
                {
                    case TrackMacroSectionType.Straight:
                    case TrackMacroSectionType.WideStraight:
                    case TrackMacroSectionType.BoostStraight:
                    case TrackMacroSectionType.RecoveryStraight:
                        corridors.Add(i);
                        break;
                }
            }
            corridors.Sort((a, b) =>
            {
                bool aStructural = definitions[a].FeatureFitRole == FeatureFitRole.StructuralConnector;
                bool bStructural = definitions[b].FeatureFitRole == FeatureFitRole.StructuralConnector;
                if (aStructural != bStructural) return aStructural ? -1 : 1;
                return definitions[b].Length.CompareTo(definitions[a].Length);
            });

            for (int i = 0; i < corridors.Count && Mathf.Abs(residual) > 0.25f; i++)
            {
                int index = corridors[i];
                TrackMacroSectionDefinition definition = definitions[index];
                float requested = -residual;
                float capacity = AvailableAuthoringElevationDelta(cfg, definition, requested);
                float delta = Mathf.Sign(requested) *
                              Mathf.Min(Mathf.Abs(requested), capacity);
                if (Mathf.Abs(delta) <= 0.001f) continue;

                definition.ElevationChange += delta;
                definition.DebugName += delta > 0f
                    ? $"_ClosureClimb{delta:F0}m"
                    : $"_ClosureDrop{-delta:F0}m";
                occupiedCarriers?.Add(index);
                residual += delta;
            }

            return residual;
        }

        /// <summary>
        /// Gives a spiral/corkscrew first refusal on elevation assigned to its own
        /// immediately-following recovery. The recovery is removed only when the whole
        /// horizontal run and vertical delta fit inside legal feature geometry.
        /// </summary>
        internal static int IntegrateFeatureRecoveries(ResolvedTrackGenerationConfig cfg, TopologyPlan plan)
        {
            var defs = plan.Defs;
            int integrated = 0;

            for (int i = 0; i + 1 < defs.Count; i++)
            {
                var feature = defs[i];
                var recovery = defs[i + 1];
                if (recovery.SectionType != TrackMacroSectionType.RecoveryStraight ||
                    Mathf.Abs(recovery.ElevationChange) < 1f ||
                    string.IsNullOrEmpty(feature.PatternId) ||
                    feature.PatternId != recovery.PatternId)
                    continue;

                bool absorbed = feature.SectionType == TrackMacroSectionType.Spiral &&
                                feature.SemanticElement != SemanticElementId.HalfHelixTurnaround
                    ? TryIntegrateSpiralRecovery(cfg, feature, recovery)
                    : feature.SectionType == TrackMacroSectionType.Corkscrew &&
                      TryIntegrateCorkscrewRecovery(cfg, feature, recovery);
                if (!absorbed) continue;

                plan.Warnings.Add($"Feature integration: '{feature.DebugName}' absorbed " +
                                  $"'{recovery.DebugName}' ({recovery.ElevationChange:+0;-0}m); " +
                                  "the next road section now connects directly.");
                defs.RemoveAt(i + 1);
                integrated++;
            }

            return integrated;
        }

        private static bool TryIntegrateSpiralRecovery(ResolvedTrackGenerationConfig cfg,
            TrackMacroSectionDefinition spiral, TrackMacroSectionDefinition recovery)
        {
            // Do not flatten or reverse an authored spiral merely to eliminate a piece.
            // Integration is for a same-direction continuation of its climb/drop.
            if (Mathf.Abs(spiral.ElevationChange) < 1f ||
                Mathf.Sign(spiral.ElevationChange) != Mathf.Sign(recovery.ElevationChange))
                return false;

            float targetClimb = spiral.ElevationChange + recovery.ElevationChange;
            int existingRevs = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(spiral.TurnAngle) / 360f));
            int requiredRevs = Mathf.CeilToInt(Mathf.Abs(targetClimb) /
                                               Mathf.Max(1f, cfg.MaxSpiralClimbPerRevolution));
            int revs = Mathf.Max(existingRevs, requiredRevs);
            if (revs > cfg.MaxSpiralRevolutions) return false;

            float radius = Mathf.Max(spiral.Width, spiral.Radius);
            float maxAngle = targetClimb >= 0f ? cfg.MaxClimbAngle : cfg.MaxDropAngle;
            float requiredCircularLength = Mathf.Abs(targetClimb) * 1.5f /
                                           Mathf.Max(0.01f, Mathf.Tan(maxAngle * Mathf.Deg2Rad));
            radius = Mathf.Max(radius, requiredCircularLength / (2f * Mathf.PI * revs));
            float maxRadius = Mathf.Max(spiral.Width, cfg.MaxSpiralRadius);
            if (radius > maxRadius + 0.01f) return false;

            float forwardDrift = recovery.HorizontalRun;
            float turnAngle = 360f * revs;
            spiral.Radius = radius;
            spiral.TurnAngle = turnAngle;
            spiral.ElevationChange = targetClimb;
            spiral.PlanHorizontalLength = forwardDrift;
            spiral.Length = SectionFrameBuilders.EstimateDriftingSpiralLength(
                radius, turnAngle, forwardDrift, targetClimb);
            spiral.RequiresRecoveryAfter = false;
            spiral.DebugName = $"Spiral_{revs}rev_{(targetClimb >= 0f ? "Up" : "Down")}_{spiral.Direction}_IntegratedExit";
            var contract = spiral.Contract;
            contract.ElevationDelta = targetClimb;
            spiral.Contract = contract;
            return true;
        }

        private static bool TryIntegrateCorkscrewRecovery(ResolvedTrackGenerationConfig cfg,
            TrackMacroSectionDefinition corkscrew, TrackMacroSectionDefinition recovery)
        {
            float targetDelta = corkscrew.ElevationChange + recovery.ElevationChange;
            float minRadius = cfg.MinCorkscrewRadius;
            float maxRadius = Mathf.Max(minRadius, cfg.MaxCorkscrewRadius);
            if (Mathf.Abs(targetDelta) > 2f * (maxRadius - minRadius) + 0.01f)
                return false;

            // For two half-turns, net elevation is 2*(firstRadius-secondRadius).
            // Start centered on the authored radius, then translate both radii together
            // if either side hits a rulebook bound; their difference stays exact.
            float firstRadius = Mathf.Clamp(corkscrew.Radius + targetDelta * 0.25f, minRadius, maxRadius);
            float secondRadius = firstRadius - targetDelta * 0.5f;
            if (secondRadius < minRadius)
            {
                float shift = minRadius - secondRadius;
                firstRadius += shift;
                secondRadius += shift;
            }
            if (secondRadius > maxRadius)
            {
                float shift = secondRadius - maxRadius;
                firstRadius -= shift;
                secondRadius -= shift;
            }
            if (firstRadius < minRadius - 0.01f || firstRadius > maxRadius + 0.01f ||
                secondRadius < minRadius - 0.01f || secondRadius > maxRadius + 0.01f)
                return false;

            corkscrew.Radius = firstRadius;
            corkscrew.SecondaryRadius = secondRadius;
            corkscrew.ElevationChange = targetDelta;
            corkscrew.Length += recovery.HorizontalRun;
            corkscrew.RequiresRecoveryAfter = false;
            corkscrew.DebugName = $"Corkscrew_{(corkscrew.RollChange >= 0f ? "R" : "L")}_" +
                                  $"H1R{firstRadius:F0}_H2R{secondRadius:F0}_IntegratedExit";
            var contract = corkscrew.Contract;
            contract.ElevationDelta = targetDelta;
            corkscrew.Contract = contract;
            return true;
        }

        /// <summary>
        /// Picks feature gaps with a farthest-point pass, then returns them in travel
        /// order so the rhythm-arranged feature sequence is preserved. The closure
        /// reserve participates in circular distance even though it cannot host content.
        /// This spreads required encounters before optional randomness can cluster them.
        /// </summary>
        private static List<int> SelectRhythmicFeatureGaps(
            int usableGaps,
            int totalGaps,
            int requested,
            TopologyPlan plan,
            ref Unity.Mathematics.Random rng)
        {
            var available = new List<int>(Mathf.Max(0, usableGaps));
            for (int gap = 0; gap < usableGaps; gap++) available.Add(gap);
            Shuffle(available, ref rng); // deterministic tie-breaking and seed variety

            var selected = new List<int>(Mathf.Min(requested, usableGaps));
            var quarterUse = new int[4];
            int QuarterOf(int gap)
            {
                if (plan?.Quarters != null)
                    for (int i = 0; i < plan.Quarters.Count; i++)
                        if (gap >= plan.Quarters[i].GapStart && gap < plan.Quarters[i].GapEnd)
                            return Mathf.Clamp(plan.Quarters[i].Index, 0, 3);
                return Mathf.Clamp(Mathf.FloorToInt(gap / (float)Mathf.Max(1, totalGaps) * 4f), 0, 3);
            }

            while (selected.Count < requested && available.Count > 0)
            {
                int bestListIndex = 0;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < available.Count; i++)
                {
                    int gap = available[i];
                    int minimumDistance = totalGaps;
                    for (int p = 0; p < selected.Count; p++)
                    {
                        int direct = Mathf.Abs(gap - selected[p]);
                        int circular = Mathf.Max(0, totalGaps - direct);
                        minimumDistance = Mathf.Min(minimumDistance, Mathf.Min(direct, circular));
                    }

                    int quarter = QuarterOf(gap);
                    float score = minimumDistance * 100f - quarterUse[quarter] * 35f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestListIndex = i;
                }

                int chosen = available[bestListIndex];
                selected.Add(chosen);
                quarterUse[QuarterOf(chosen)]++;
                available.RemoveAt(bestListIndex);
            }

            selected.Sort();
            return selected;
        }

        private static void Shuffle<T>(List<T> list, ref Unity.Mathematics.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
