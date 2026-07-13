using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Plan-time parameters of one reserved branch group (frames are built later).</summary>
    public class PlannedBranchGroup
    {
        public int GroupId;
        public float CorridorLength;         // fork → merge along the main axis
        public float LateralSeparation;      // peak centerline separation
        public float VerticalSeparation;     // peak vertical separation (0 = level routes)
        public BranchPairingMode PairingMode;
        public BranchInteractionPattern InteractionPattern;
        public BranchRouteSettings RouteASpec;
        public BranchRouteSettings RouteBSpec;
        public int Crossovers;
        public uint RouteASeed;
        public uint RouteBSeed;
    }

    /// <summary>The full plan of one candidate: definitions plus bookkeeping for validation.</summary>
    public class TopologyPlan
    {
        public List<TrackMacroSectionDefinition> Defs = new List<TrackMacroSectionDefinition>();
        public List<PlannedBranchGroup> BranchGroups = new List<PlannedBranchGroup>();
        public Dictionary<TrackPatternType, int> PlacedPatterns = new Dictionary<TrackPatternType, int>();
        public int TurnCount;

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

        // ─────────────────────────── Corner slot model ───────────────────────────

        private class CornerSlot
        {
            public int SignedAngle;                       // degrees, sign = direction
            public TrackPatternType Realization = TrackPatternType.Hairpin; // meaningful only when special
            public bool IsHalfLoop;                       // realized as a half-loop pattern (vertical reversal)
            public TrackPatternType HalfLoopType;
            public bool IsSpecial;                        // corner pattern realization (double apex, …)
            public bool AllowHairpinMagnitude;            // may reach 150–180° (counts against the hairpin rule)
            public int Magnitude => Mathf.Abs(SignedAngle);
            public int Sign => SignedAngle >= 0 ? 1 : -1;
        }

        /// <summary>Plans one candidate. Returns a plan whose Failure explains any rejection.</summary>
        public TopologyPlan Plan(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng)
        {
            var plan = new TopologyPlan();

            // ── 1. Corner plan ──
            List<CornerSlot> corners = PlanCorners(cfg, plan, ref rng);
            if (plan.Failed) return plan;
            plan.TurnCount = corners.Count;

            // ── 2/3. Gap budget, then feature instances (required first, then optional) ──
            int gapCount = corners.Count;
            int closureGaps = Mathf.Clamp(Mathf.CeilToInt(gapCount * cfg.ClosureReserveFraction), 2, gapCount - 1);
            int usableGaps = gapCount - closureGaps;

            // Length budget for features: what remains of the cap after the corner arcs,
            // the minimum straights and the branch minimums. Optional content stops when
            // the budget runs out instead of blowing the cap and failing later.
            float cornerArcEstimate = 0f;
            foreach (var c in corners)
            {
                float radius = Mathf.Max(Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, c.Magnitude / 180f), cfg.MinCurveRadius);
                cornerArcEstimate += SectionFrameBuilders.EasedArcLength(c.Magnitude, radius);
            }
            float branchEstimate = cfg.MinBranchGroups * (cfg.MinRouteLength + cfg.DecisionPreviewLength + cfg.PostMergeRecoveryLength);
            float straightsEstimate = gapCount * Mathf.Max(MinAdjustableStraight, cfg.MinStraightLength * 0.6f);
            float featureBudget = cfg.MaxTrackLength * 0.85f - cornerArcEstimate - branchEstimate - straightsEstimate;

            List<TrackPatternType> gapFeatures = PlanGapFeatures(cfg, plan, usableGaps - cfg.MinBranchGroups, featureBudget, ref rng);
            if (plan.Failed) return plan;

            int branchGroups = cfg.MaxBranchGroups > 0
                ? rng.NextInt(cfg.MinBranchGroups, cfg.MaxBranchGroups + 1)
                : 0;

            if (gapFeatures.Count + branchGroups > usableGaps)
            {
                if (gapFeatures.Count + cfg.MinBranchGroups > usableGaps)
                {
                    plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                        $"{gapFeatures.Count} feature groups + {cfg.MinBranchGroups} branch groups need more gaps than the {usableGaps} available between {gapCount} corners (closure reserves {closureGaps}). Raise turn count or lower feature/branch minimums.");
                    return plan;
                }
                branchGroups = Mathf.Max(cfg.MinBranchGroups, usableGaps - gapFeatures.Count);
            }

            // Deterministic shuffled gap order; first N get features, next M get branches.
            var gapOrder = new List<int>();
            for (int i = 0; i < usableGaps; i++) gapOrder.Add(i);
            Shuffle(gapOrder, ref rng);

            var featureByGap = new Dictionary<int, TrackPatternType>();
            var branchGaps = new HashSet<int>();
            int cursor = 0;
            foreach (var f in gapFeatures) featureByGap[gapOrder[cursor++]] = f;
            for (int b = 0; b < branchGroups && cursor < gapOrder.Count; b++) branchGaps.Add(gapOrder[cursor++]);

            // ── 4. Emit definitions gap-by-gap, corner-by-corner ──
            EmitDefinitions(cfg, plan, corners, featureByGap, branchGaps, closureGaps, ref rng);
            if (plan.Failed) return plan;

            // ── 5. 2D closure solve on adjustable straights ──
            SolveClosure2D(cfg, plan);
            if (plan.Failed) return plan;

            // ── 6. Elevation plan (BEFORE the proximity check: planned climbs are what
            // legally separate folded mountain-pass legs) ──
            PlanElevation(cfg, plan, ref rng);
            if (plan.Failed) return plan;

            // ── 7. Cheap elevation-aware 2D self-proximity check ──
            string collision = Validate2DWalk(cfg, plan);
            if (collision != null)
            {
                plan.Fail(GenerationFailureReason.SelfIntersection,
                    $"Plan-view walk brings unrelated track segments closer than the safe corridor without vertical separation: {collision}.");
                return plan;
            }

            return plan;
        }

        // ═══════════════════════════ 1. Corner plan ═══════════════════════════

        private List<CornerSlot> PlanCorners(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, ref Unity.Mathematics.Random rng)
        {
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

            // The hairpin RULE caps how many corners may reach hairpin magnitudes
            // (150–180°) — the turn-family weights select candidates, never override
            // the rule's maximum count.
            int hairpinBudget = cfg.Hairpins.Enabled ? cfg.Hairpins.MaximumCount : 0;
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
                if (rng.NextFloat() < Mathf.Clamp01(cfg.HalfLoops.OptionalWeight * 0.25f))
                    requiredHalfLoopTypes.Add(TrackPatternType.HalfLoopRollout);
            }

            if (requiredHalfLoopTypes.Count > 0)
            {
                // Convert the sharpest non-special corners into half-loop reversals.
                var byMag = new List<CornerSlot>(corners);
                byMag.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));
                int assigned = 0;
                foreach (var c in byMag)
                {
                    if (assigned >= requiredHalfLoopTypes.Count) break;
                    if (c.IsSpecial || c.IsHalfLoop) continue;
                    c.IsHalfLoop = true;
                    c.HalfLoopType = requiredHalfLoopTypes[assigned];
                    c.SignedAngle = c.Sign * 180;
                    assigned++;
                }
                if (assigned < requiredHalfLoopTypes.Count)
                {
                    plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                        $"Needed {requiredHalfLoopTypes.Count} half-loop reversal slots but only {assigned} corners were available.");
                    return corners;
                }
            }

            // Balance the signed sum to exactly ±360 so the lap closes in heading.
            if (!BalanceCornerSum(corners, ref rng))
            {
                plan.Fail(GenerationFailureReason.ClosureHeadingFailure,
                    $"Could not balance {corners.Count} signed corners to ±360°.");
                return corners;
            }

            // Intentional corner sequences on eligible plain corners.
            foreach (var c in corners)
            {
                if (c.IsSpecial || c.IsHalfLoop) continue;
                if (rng.NextFloat() >= cfg.CornerSequenceChance) continue;

                int mag = c.Magnitude;
                if (mag >= 150)
                {
                    c.Realization = TrackPatternType.Hairpin;
                    plan.CountPattern(TrackPatternType.Hairpin);
                }
                else if (mag >= 90)
                {
                    c.Realization = rng.NextBool() ? TrackPatternType.DoubleApex
                        : (rng.NextBool() ? TrackPatternType.TighteningCorner : TrackPatternType.OpeningCorner);
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
                if (rng.NextFloat() >= cfg.CornerSequenceChance) continue;

                c.Realization = TrackPatternType.SweeperIntoHairpin;
                plan.CountPattern(TrackPatternType.SweeperIntoHairpin);
                break; // one per lap is plenty
            }

            return corners;
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
                if (c.IsHalfLoop) continue; // half-loops stay exactly 180°

                int minMag = c.IsSpecial && c.Realization == TrackPatternType.Hairpin ? 150 : 20;
                int maxMag = c.AllowHairpinMagnitude ? 180 : 145; // balancing never mints extra hairpins

                int step = Mathf.Clamp(residual * c.Sign, -20, 20);
                int newMag = Mathf.Clamp(c.Magnitude + step, minMag, maxMag);

                if (newMag == c.Magnitude && pass > corners.Count * 3)
                {
                    // Saturated: flip the non-special corner whose flip best approaches the target.
                    int bestIdx = -1, bestErr = Mathf.Abs(residual);
                    for (int i = 0; i < corners.Count; i++)
                    {
                        if (corners[i].IsHalfLoop) continue;
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
            int loopsUsed = 0, corksUsed = 0, spiralsUsed = 0, jumpsUsed = 0, chicanesUsed = 0, sCurvesUsed = 0;

            bool TryConsume(TrackPatternType t)
            {
                switch (t)
                {
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
                if (cfg.Chicanes.Enabled && chicanesUsed < cfg.Chicanes.MaximumCount) pool.Add((TrackPatternType.Chicane, cfg.Chicanes.OptionalWeight));
                if (cfg.SCurves.Enabled && sCurvesUsed < cfg.SCurves.MaximumCount) pool.Add((TrackPatternType.SCurve, cfg.SCurves.OptionalWeight));

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

        // ═══════════════════════════ 3/4. Definition emission ═══════════════════════════

        private void EmitDefinitions(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, List<CornerSlot> corners,
            Dictionary<int, TrackPatternType> featureByGap, HashSet<int> branchGaps, int closureGaps,
            ref Unity.Mathematics.Random rng)
        {
            var defs = plan.Defs;
            float width = cfg.RoadWidth;
            int patternCounter = 0;
            int branchCounter = 0;

            for (int gap = 0; gap < corners.Count; gap++)
            {
                bool inClosureReserve = gap >= corners.Count - closureGaps;

                // Pacing: straights alternate short/long more strongly with higher variation.
                float wave = Mathf.PingPong(gap * 0.618f, 1f);
                float pacedT = Mathf.Lerp(rng.NextFloat(), wave, cfg.PacingVariation * 0.7f);
                float pacedLength = Mathf.Lerp(cfg.MinStraightLength, cfg.MaxStraightLength, pacedT);

                // Every gap opens with an ADJUSTABLE plain straight (the closure solver's levers).
                var lead = SectionDefs.Straight(TrackMacroSectionType.Straight, pacedLength, width,
                    $"Straight_{gap:D2}");
                lead.IsClosure = inClosureReserve;
                defs.Add(lead);

                // Gap content: one feature pattern OR one branch group (closure gaps stay plain).
                if (!inClosureReserve && featureByGap.TryGetValue(gap, out TrackPatternType feature))
                {
                    string patternId = $"{feature}_{patternCounter++}";
                    if (!FeaturePatternLibrary.TryGet(feature, out var pattern) ||
                        !pattern.TryPlan(cfg, patternId, ref rng, defs))
                    {
                        plan.Fail(GenerationFailureReason.RequiredPatternMissing,
                            $"Pattern {feature} could not find a legal parameterization under the resolved config.");
                        return;
                    }
                    plan.CountPattern(feature);
                }
                else if (!inClosureReserve && branchGaps.Contains(gap))
                {
                    EmitBranchGroup(cfg, plan, branchCounter++, ref rng);
                    if (plan.Failed) return;
                }

                // The corner that closes this gap.
                EmitCorner(cfg, plan, corners[gap], patternCounter++, inClosureReserve, ref rng);
                if (plan.Failed) return;
            }
        }

        private void EmitBranchGroup(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, int groupIndex,
            ref Unity.Mathematics.Random rng)
        {
            var defs = plan.Defs;
            float width = cfg.RoadWidth;

            float corridor = rng.NextFloat(cfg.MinRouteLength, cfg.MaxRouteLength);
            float lateral = rng.NextFloat(cfg.MinLateralSeparation, cfg.MaxLateralSeparation);

            // Interaction pattern by weight.
            var w = cfg.InteractionWeights;
            (BranchInteractionPattern p, float weight)[] pool =
            {
                (BranchInteractionPattern.Separated, w.Separated),
                (BranchInteractionPattern.Parallel, w.Parallel),
                (BranchInteractionPattern.Converging, w.Converging),
                (BranchInteractionPattern.AlternatingCrossover, w.AlternatingCrossover),
                (BranchInteractionPattern.Braided, w.Braided),
                (BranchInteractionPattern.SharedAxis, w.SharedAxis),
                (BranchInteractionPattern.PairedFeature, w.PairedFeature)
            };
            float pick = rng.NextFloat(0f, w.Total);
            BranchInteractionPattern interaction = BranchInteractionPattern.Separated;
            foreach (var entry in pool)
            {
                pick -= entry.weight;
                if (pick <= 0f) { interaction = entry.p; break; }
            }

            int crossovers = interaction switch
            {
                BranchInteractionPattern.AlternatingCrossover => Mathf.Min(1 + rng.NextInt(0, 2), cfg.MaxCrossovers),
                BranchInteractionPattern.Braided => Mathf.Min(2 + rng.NextInt(0, 2), cfg.MaxCrossovers),
                _ => 0
            };
            bool vertical = crossovers > 0 || interaction == BranchInteractionPattern.SharedAxis ||
                            (interaction == BranchInteractionPattern.Separated && rng.NextBool());
            float verticalSep = vertical
                ? rng.NextFloat(cfg.MinRouteVerticalSeparation, cfg.MaxRouteVerticalSeparation)
                : 0f;

            // Route corridor must fit its internal zones.
            float zoneBudget = cfg.SplitLength + cfg.VerticalDivergenceDelay + cfg.VerticalDivergenceLength * 2f + cfg.MergeLength;
            corridor = Mathf.Max(corridor, zoneBudget / 0.85f);
            if (corridor > cfg.MaxRouteLength * 1.5f)
            {
                plan.Fail(GenerationFailureReason.BranchMergeFailure,
                    $"Branch zone budget ({zoneBudget:F0}m) cannot fit the allowed route corridor ({cfg.MaxRouteLength:F0}m).");
                return;
            }

            var group = new PlannedBranchGroup
            {
                GroupId = groupIndex,
                CorridorLength = corridor,
                LateralSeparation = lateral,
                VerticalSeparation = verticalSep,
                PairingMode = cfg.PairingMode,
                InteractionPattern = interaction,
                RouteASpec = cfg.RouteASettings.Clone(),
                RouteBSpec = cfg.RouteBSettings.Clone(),
                Crossovers = crossovers,
                RouteASeed = rng.NextUInt(),
                RouteBSeed = rng.NextUInt()
            };
            Branching.BranchRoutePlanner.ApplyPairingMode(group, ref rng);
            plan.BranchGroups.Add(group);

            // Decision preview approach (locked).
            defs.Add(SectionDefs.Straight(TrackMacroSectionType.Straight,
                Mathf.Max(cfg.DecisionPreviewLength, cfg.DefaultApproachLength), width,
                $"BranchApproach_{groupIndex}", locked: true));

            for (int route = 0; route < 2; route++)
            {
                defs.Add(new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.SplitRoute,
                    Length = corridor,
                    Width = width * (route == 0 ? group.RouteASpec.WidthScale : group.RouteBSpec.WidthScale),
                    Direction = route == 0 ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                    RouteLateralStart = route == 0 ? lateral * 0.5f : -lateral * 0.5f,
                    RouteLateralEnd = (crossovers % 2 == 1)
                        ? (route == 0 ? -lateral * 0.5f : lateral * 0.5f)
                        : (route == 0 ? lateral * 0.5f : -lateral * 0.5f),
                    HillHeight = route == 0 ? verticalSep : 0f,
                    // Shared-axis routes DECLARE their orbital roll — the twin-corkscrew
                    // pattern owns its orientation contract (validators skip generic roll).
                    RollChange = interaction == BranchInteractionPattern.SharedAxis ? 360f : 0f,
                    SpeedIntent = SectionSpeedIntent.Fast,
                    RiskLevel = route == 0 ? SectionRiskLevel.Risky : SectionRiskLevel.Normal,
                    LockLength = true,
                    DebugName = $"BranchRoute{(route == 0 ? "A" : "B")}_{groupIndex}_{interaction}",
                    PatternId = $"Branch_{groupIndex}",
                    Contract = SectionConnectionContract.Level()
                });
            }

            defs.Add(SectionDefs.Straight(TrackMacroSectionType.RecoveryStraight, cfg.PostMergeRecoveryLength, width,
                "PostMergeRecovery", locked: true));
        }

        private void EmitCorner(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, CornerSlot corner,
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
                    if (defs[i].SectionType == TrackMacroSectionType.HalfLoopTwist && defs[i].PatternId == patternId)
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

            switch (corner.IsSpecial ? corner.Realization : TrackPatternType.SCurve /* marker for plain */)
            {
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
                default:
                {
                    bool hairpin = angle >= 150;
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

            for (int round = 0; round <= 3; round++)
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

                if (round == 0 && !FitLengthBudget(cfg, plan, defs, straightIdx, cornerIdx, cornerBaseRadius))
                    return;

                Vector2 gap = -RewalkEndPos(defs);
                if (initialGap < 0f) initialGap = gap.magnitude;

                gap = RunActiveSetSolve(cfg, defs, straightIdx, straightDirs, cornerIdx, cornerDirs, cornerBaseRadius, gap);

                if (gap.magnitude <= cfg.ClosurePositionTolerance)
                {
                    float total = TotalPlanLength(defs);
                    if (total > cfg.MaxTrackLength)
                    {
                        plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                            $"Solved lap length {total / 1000f:F1}km exceeds the maximum {cfg.MaxTrackLength / 1000f:F1}km.");
                    }
                    return;
                }

                if (round == 3 || !TryInsertClosureSBend(cfg, defs, straightIdx, straightDirs, gap))
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
                Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);

                switch (d.SectionType)
                {
                    case TrackMacroSectionType.SplitRoute:
                    {
                        bool isSecondOfPair = i > 0 && defs[i - 1].SectionType == TrackMacroSectionType.SplitRoute;
                        if (!isSecondOfPair) pos += fwd * d.Length;
                        break;
                    }
                    case TrackMacroSectionType.Loop:
                    {
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        pos += fwd * SectionFrameBuilders.LoopForwardDisplacement(d.Length)
                             + right * SectionFrameBuilders.LoopLateralOffset(d.Width);
                        break;
                    }
                    case TrackMacroSectionType.Spiral:
                        break;
                    case TrackMacroSectionType.HalfLoopTwist:
                    {
                        float halfArc = SectionFrameBuilders.HalfLoopArcLength(d.Radius);
                        pos += fwd * SectionFrameBuilders.HalfLoopForwardDisplacement(halfArc);
                        heading += 180f;
                        pos += SectionFrameBuilders.HeadingToDir(heading) * Mathf.Max(0f, d.Length - halfArc);
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
                    {
                        // Plain corners double as CLOSURE CURVES: an eased arc's
                        // displacement is still exactly linear in its radius while the
                        // heading delta stays fixed, so the unit-radius end offset is the
                        // derivative. Corners inside atomic patterns are never touched.
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

        /// <summary>Total planned lap length (branch pairs counted once).</summary>
        private static float TotalPlanLength(List<TrackMacroSectionDefinition> defs)
        {
            float t = 0f;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                bool isSecondOfPair = d.SectionType == TrackMacroSectionType.SplitRoute &&
                                      i > 0 && defs[i - 1].SectionType == TrackMacroSectionType.SplitRoute;
                if (!isSecondOfPair) t += d.Length;
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
            float budgetCeiling = cfg.MaxTrackLength * 0.88f; // headroom: closure may grow lengths
            if (total <= budgetCeiling) return true;

            float adjustableTotal = 0f;
            foreach (int idx in straightIdx) adjustableTotal += defs[idx].Length;
            float excess = total - budgetCeiling;
            float straightShrinkable = Mathf.Max(0f, adjustableTotal - straightIdx.Count * MinAdjustableStraight);

            float cornerShrinkable = 0f;
            for (int c = 0; c < cornerIdx.Count; c++)
            {
                var def = defs[cornerIdx[c]];
                cornerShrinkable += SectionFrameBuilders.EasedArcLength(def.TurnAngle, def.Radius) - SectionFrameBuilders.EasedArcLength(def.TurnAngle, Mathf.Min(def.Radius, cfg.MinCurveRadius));
            }

            if (excess > straightShrinkable + cornerShrinkable * 0.8f)
            {
                plan.Fail(GenerationFailureReason.InsufficientLengthBudget,
                    $"Locked content alone needs {(total - adjustableTotal - cornerShrinkable) / 1000f:F1}km — the {cfg.MaxTrackLength / 1000f:F1}km cap cannot fit this plan.");
                return false;
            }

            float fromStraights = Mathf.Min(excess, straightShrinkable);
            if (fromStraights > 0f)
            {
                float scale = 1f - fromStraights / Mathf.Max(1f, straightShrinkable);
                foreach (int idx in straightIdx)
                    defs[idx].Length = Mathf.Max(MinAdjustableStraight,
                        MinAdjustableStraight + (defs[idx].Length - MinAdjustableStraight) * scale);
            }

            float fromCorners = excess - fromStraights;
            if (fromCorners > 0f && cornerShrinkable > 1f)
            {
                float cornerScale = 1f - Mathf.Clamp01(fromCorners / cornerShrinkable);
                for (int c = 0; c < cornerIdx.Count; c++)
                {
                    var def = defs[cornerIdx[c]];
                    float newRadius = cfg.MinCurveRadius + (def.Radius - cfg.MinCurveRadius) * cornerScale;
                    def.Radius = newRadius;
                    def.Length = SectionFrameBuilders.EasedArcLength(def.TurnAngle, newRadius);
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
        private Vector2 RunActiveSetSolve(ResolvedTrackGenerationConfig cfg, List<TrackMacroSectionDefinition> defs,
            List<int> straightIdx, List<Vector2> straightDirs,
            List<int> cornerIdx, List<Vector2> cornerDirs, List<float> cornerBaseRadius, Vector2 gap)
        {
            float LenCap(TrackMacroSectionDefinition d) => d.IsClosure
                ? cfg.SecondsToDistance(8f)
                : cfg.MaxStraightLength * 1.5f;

            int nStraights = straightIdx.Count;
            int varCount = nStraights + cornerIdx.Count;

            Vector2 DirOf(int k) => k < nStraights ? straightDirs[k] : cornerDirs[k - nStraights];

            float ValueOf(int k) => k < nStraights
                ? defs[straightIdx[k]].Length
                : defs[cornerIdx[k - nStraights]].Radius;

            // Styled corners flex within a fraction of their designed radius (character
            // survives); closure-reserve corners are TRUE closure curves and may sweep
            // the full rulebook radius band.
            float MinOf(int k) => k < nStraights
                ? MinAdjustableStraight
                : defs[cornerIdx[k - nStraights]].IsClosure
                    ? cfg.MinCurveRadius
                    : Mathf.Max(cfg.MinCurveRadius, cornerBaseRadius[k - nStraights] * 0.6f);

            float MaxOf(int k) => k < nStraights
                ? LenCap(defs[straightIdx[k]])
                : defs[cornerIdx[k - nStraights]].IsClosure
                    ? cfg.MaxCurveRadius
                    : Mathf.Min(cfg.MaxCurveRadius, cornerBaseRadius[k - nStraights] * 1.8f);

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
                    def.Length = SectionFrameBuilders.EasedArcLength(def.TurnAngle, newValue);
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
        /// Inserts one analytic closure S-bend (two opposed eased arcs, net-zero heading)
        /// into the longest closure straight to supply PERPENDICULAR displacement the
        /// straights and curves cannot. The bend's offset is linear in radius, so the
        /// radius is fit from the measured per-unit-radius eased-S offset. Returns false
        /// when no host straight exists or the residual is purely longitudinal.
        /// </summary>
        private static bool TryInsertClosureSBend(ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> defs, List<int> straightIdx, List<Vector2> straightDirs, Vector2 gap)
        {
            // Host selection: the S-bend displaces PERPENDICULAR to its host, so pick the
            // straight whose sideways axis best aligns with the residual (closure-reserve
            // straights strongly preferred, longer hosts break ties).
            Vector2 gapDir = gap.normalized;
            int host = -1;
            float bestScore = 0f;
            for (int a = 0; a < straightIdx.Count; a++)
            {
                var d = defs[straightIdx[a]];
                Vector2 r = new Vector2(straightDirs[a].y, -straightDirs[a].x);
                float alignment = Mathf.Abs(Vector2.Dot(gapDir, r));
                float score = alignment * (d.IsClosure ? 2f : 1f) * Mathf.Clamp01(d.Length / 400f);
                if (score > bestScore)
                {
                    host = a;
                    bestScore = score;
                }
            }
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
            float radius = Mathf.Clamp(Mathf.Abs(lateral) / (2f * 0.45f), cfg.MinCurveRadius, cfg.MaxCurveRadius);
            float oneMinusCos = Mathf.Clamp(Mathf.Abs(lateral) / (2f * radius), 0.005f, maxOneMinusCos);
            float alpha = SectionFrameBuilders.QuantizeArcAngle(Mathf.Acos(1f - oneMinusCos) * Mathf.Rad2Deg);

            Vector2 unitOffset = SectionFrameBuilders.EasedSBendUnitOffset(alpha);
            radius = Mathf.Clamp(Mathf.Abs(lateral) / Mathf.Max(unitOffset.y, 1e-4f),
                cfg.MinCurveRadius, cfg.MaxCurveRadius);

            var hostDef = defs[straightIdx[host]];
            float forwardRun = radius * unitOffset.x;
            hostDef.Length = Mathf.Max(MinAdjustableStraight, hostDef.Length - forwardRun);

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
                Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);

                switch (d.SectionType)
                {
                    case TrackMacroSectionType.SplitRoute:
                    {
                        bool isSecondOfPair = i > 0 && defs[i - 1].SectionType == TrackMacroSectionType.SplitRoute;
                        if (!isSecondOfPair) pos += fwd * d.Length;
                        break;
                    }
                    case TrackMacroSectionType.Loop:
                    {
                        Vector2 right = new Vector2(fwd.y, -fwd.x);
                        pos += fwd * SectionFrameBuilders.LoopForwardDisplacement(d.Length)
                             + right * SectionFrameBuilders.LoopLateralOffset(d.Width);
                        break;
                    }
                    case TrackMacroSectionType.Spiral:
                        break;
                    case TrackMacroSectionType.HalfLoopTwist:
                    {
                        float halfArc = SectionFrameBuilders.HalfLoopArcLength(d.Radius);
                        pos += fwd * SectionFrameBuilders.HalfLoopForwardDisplacement(halfArc);
                        heading += 180f;
                        pos += SectionFrameBuilders.HeadingToDir(heading) * Mathf.Max(0f, d.Length - halfArc);
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
                        SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    default:
                        pos += fwd * d.HorizontalRun;
                        break;
                }
            }

            return pos;
        }

        // ═══════════════════════════ 6. Cheap 2D self-proximity ═══════════════════════════

        /// <summary>Returns null when the plan-view walk is clear, else a description of the first collision.</summary>
        private string Validate2DWalk(ResolvedTrackGenerationConfig cfg, TopologyPlan plan)
        {
            var defs = plan.Defs;
            var pts = new List<Vector2>(1024);
            var arcs = new List<float>(1024);
            var heights = new List<float>(1024);
            var owners = new List<int>(1024);

            Vector2 pos = Vector2.zero;
            float heading = 0f;
            float arc = 0f;
            float fixedHeight = 0f;
            const float step = 50f;

            void Emit(Vector2 p, float a) { pts.Add(p); arcs.Add(a); }

            void WalkStraight(float length)
            {
                Vector2 dir = SectionFrameBuilders.HeadingToDir(heading);
                for (float s = 0f; s < length; s += step) Emit(pos + dir * s, arc + s);
                pos += dir * length;
                arc += length;
            }

            void WalkArc(float signedAngleDeg, float radius)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(signedAngleDeg) / 8f));
                float sub = signedAngleDeg / n;
                float subLen = Mathf.Abs(sub) * Mathf.Deg2Rad * radius;
                for (int k = 0; k < n; k++)
                {
                    Emit(pos, arc);
                    SectionFrameBuilders.ApplyArc2D(ref pos, ref heading, sub, radius);
                    arc += subLen;
                }
            }

            // Corners follow the same eased-arc profile the builder uses (spirals stay circular).
            void WalkEased(float signedAngleDeg, float radius)
            {
                SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, signedAngleDeg, radius, step,
                    (p, a) => Emit(p, a), ref arc);
            }

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                int firstSample = pts.Count;

                switch (d.SectionType)
                {
                    case TrackMacroSectionType.SplitRoute:
                    {
                        bool isSecondOfPair = i > 0 && defs[i - 1].SectionType == TrackMacroSectionType.SplitRoute;
                        if (!isSecondOfPair) WalkStraight(d.Length);
                        break;
                    }
                    case TrackMacroSectionType.Loop:
                    {
                        WalkStraight(SectionFrameBuilders.LoopForwardDisplacement(d.Length));
                        Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);
                        pos += new Vector2(fwd.y, -fwd.x) * SectionFrameBuilders.LoopLateralOffset(d.Width);
                        arc += d.Length - SectionFrameBuilders.LoopForwardDisplacement(d.Length);
                        break;
                    }
                    case TrackMacroSectionType.Spiral:
                        WalkArc(d.TurnSign * d.TurnAngle, Mathf.Max(1f, d.Radius));
                        break;
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
                        WalkEased(d.TurnAngle * d.TurnSign, d.Radius);
                        break;
                    default:
                        WalkStraight(d.HorizontalRun);
                        break;
                }

                // Height stamps: EVERY section's planned elevation counts — fixed-height
                // features (spirals, half-loops), the elevation plan's major climbs on
                // straights, and net-zero crest/bridge bumps. Planned vertical separation
                // is what legally rescues folded mountain-pass legs.
                float delta = d.SectionType == TrackMacroSectionType.Spiral ||
                              d.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                              d.IsStraightFamily
                    ? d.ElevationChange
                    : 0f;
                float bump = d.IsStraightFamily ? d.HillHeight : 0f;
                int emitted = pts.Count - firstSample;
                for (int k = 0; k < emitted; k++)
                {
                    float t = emitted > 1 ? (float)k / (emitted - 1) : 1f;
                    heights.Add(fixedHeight + delta * t + bump * SectionFrameBuilders.Bump(t));
                    owners.Add(i);
                }
                fixedHeight += delta;
            }

            float totalArc = arc;
            if (totalArc < 1f || pts.Count < 8) return "degenerate walk";

            float minClear = cfg.RoadWidth * 1.3f;
            float minClearSq = minClear * minClear;
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);
            const float alongWindow = 600f;

            for (int i = 0; i < pts.Count; i++)
            {
                for (int j = i + 1; j < pts.Count; j++)
                {
                    float along = Mathf.Abs(arcs[j] - arcs[i]);
                    along = Mathf.Min(along, totalArc - along);
                    if (along < alongWindow) continue;
                    if ((pts[i] - pts[j]).sqrMagnitude >= minClearSq) continue;
                    if (Mathf.Abs(heights[i] - heights[j]) >= verticalOk) continue;

                    // A vertical feature owns its self-proximity (loop apex fold, spiral
                    // coils, half-loop limbs) — its clearance contract is validated on
                    // the real 3D frames, where the 2D approximation cannot judge it.
                    if (owners[i] == owners[j])
                    {
                        var ownerType = defs[owners[i]].SectionType;
                        if (ownerType == TrackMacroSectionType.Loop ||
                            ownerType == TrackMacroSectionType.Spiral ||
                            ownerType == TrackMacroSectionType.HalfLoopTwist)
                            continue;
                    }

                    return $"'{defs[owners[i]].DebugName}' (arc {arcs[i]:F0}m, h {heights[i]:F0}m) vs '{defs[owners[j]].DebugName}' (arc {arcs[j]:F0}m, h {heights[j]:F0}m) at {Mathf.Sqrt((pts[i] - pts[j]).sqrMagnitude):F0}m apart";
                }
            }

            return null;
        }

        // ═══════════════════════════ 7. Elevation plan ═══════════════════════════

        /// <summary>
        /// Assigns intentional vertical content AFTER lengths are final: major climbs and
        /// drops (a level walk that must return to zero for closure), then bridges,
        /// underpasses and crests on remaining straights. All elevation lives inside
        /// straight sections with flat ends, so weld contracts and corners stay untouched.
        /// </summary>
        private void PlanElevation(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, ref Unity.Mathematics.Random rng)
        {
            var defs = plan.Defs;

            float fixedElevation = 0f;
            foreach (var d in defs)
            {
                if (d.SectionType == TrackMacroSectionType.Spiral || d.SectionType == TrackMacroSectionType.HalfLoopTwist)
                    fixedElevation += d.ElevationChange;
            }
            bool needCompensation = Mathf.Abs(fixedElevation) > 1f;

            if (cfg.TargetElevationAmplitude < 6f && !needCompensation) return;

            // Carriers: plain (non-safety) straights.
            var eligible = new List<int>();
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                bool carrier = (d.SectionType == TrackMacroSectionType.Straight ||
                                d.SectionType == TrackMacroSectionType.WideStraight ||
                                d.SectionType == TrackMacroSectionType.BoostStraight)
                               && !d.LockLength && d.Length >= 100f;
                if (carrier) eligible.Add(i);
            }

            if (eligible.Count < 2)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    if (d.SectionType == TrackMacroSectionType.RecoveryStraight && d.Length >= 150f && !eligible.Contains(i))
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

                float Capacity(int idx, bool up) =>
                    defs[idx].Length * Mathf.Tan((up ? cfg.MaxClimbAngle : cfg.MaxDropAngle) * Mathf.Deg2Rad) / 1.5f;

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
                        float target = level < (lo + hi) * 0.5f
                            ? rng.NextFloat(0.4f, 1f) * hi
                            : rng.NextFloat(lo, level * 0.4f);
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
