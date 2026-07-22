using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Fits the alternate road (road B) of a Dual Road Quarter: a real chain of plain
    /// corners and straights from the quarter's second landing mouth (one lane left of
    /// road A's mouth) to its second launch lip (one lane left of road A's lip), solved
    /// with the same active-set least-squares machinery as the lap closure.
    ///
    /// Runs at PLAN time, after the closure solve and the elevation plan, so both gate
    /// frames are final. The fitted chain is inserted into the def list between road A's
    /// exit lip and the exit air gap, tagged RoadId = 1 — every canonical walker skips
    /// it, so lap length and closure never see the alternate road.
    /// </summary>
    public static class QuarterRoadFitter
    {
        public struct FitOutcome
        {
            public bool Success;
            public bool BalanceFailure;
            public string FailureMessage;
        }

        private const float MinFitStraight = 40f;
        // Match the ordinary elevation planner's carrier floor. The active-set solver
        // is allowed to leave 40 m closure stubs, but a smoothstep climb on one of
        // those stubs is a sharp road hump even when its peak pitch is technically
        // below MaxClimbAngle.
        private const float MinElevationCarrierLength = 100f;
        private const float PositionTolerance = 0.05f;

        public static FitOutcome Fit(ResolvedTrackGenerationConfig cfg, TopologyPlan plan, PlannedQuarter q,
            ref Unity.Mathematics.Random rng)
        {
            var defs = plan.Defs;

            // ── Locate the gate defs ──
            int entryGapIdx = q.FirstDefIndex;
            int exitGapIdx = q.RoadBInsertIndex;
            if (entryGapIdx < 0 || exitGapIdx < 0 ||
                defs[entryGapIdx].SectionType != TrackMacroSectionType.AirGap ||
                defs[exitGapIdx].SectionType != TrackMacroSectionType.AirGap ||
                defs[exitGapIdx - 1].SectionType != TrackMacroSectionType.JumpRamp)
                return Failed("quarter gate defs could not be located");

            int flareAIdx = entryGapIdx + 1;
            int lipAIdx = exitGapIdx - 1;
            if (defs[flareAIdx].SectionType != TrackMacroSectionType.LandingRamp)
                return Failed("road A landing flare not found after the entry gap");

            // ── Canonical 2D walk up to the exit gap, recording the gate anchors and
            // road A's body samples (DENSE, ~100 m — corners here run kilometers, and
            // boundary-only samples let close approaches slip straight through to the
            // built validators) for the plan-time clearance check against road B ──
            Vector2 pos = Vector2.zero;
            float heading = 0f;
            Vector2 mouthA2D = default, lipARampStart2D = default;
            float hIn = 0f, hOut = 0f;
            float elev = 0f, y0 = 0f, y1 = 0f;
            var roadAPts = new List<Vector2>();
            var roadAHeights = new List<float>();
            int roadAFirstCornerSign = 0;

            for (int i = 0; i < exitGapIdx; i++)
            {
                var d = defs[i];
                if (d.RoadId == 1) continue;

                if (i == lipAIdx)
                {
                    lipARampStart2D = pos;
                    hOut = heading;
                    y1 = elev;
                }

                bool body = i > entryGapIdx && i < lipAIdx;
                int first = roadAPts.Count;
                if (body) WalkDense(d, ref pos, ref heading, roadAPts);
                else Walk2D(d, ref pos, ref heading);

                // Plan-model elevation: intentional climbs live on straight-family
                // sections (plus fixed-feature deltas); jump chains net to zero.
                float delta = d.IsStraightFamily || d.SectionType == TrackMacroSectionType.Spiral ||
                              d.SectionType == TrackMacroSectionType.Corkscrew ||
                              d.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                              d.SectionType == TrackMacroSectionType.RotationalEvent
                    ? d.ElevationChange
                    : 0f;

                if (body)
                {
                    int emitted = roadAPts.Count - first;
                    for (int k = 0; k < emitted; k++)
                        roadAHeights.Add(elev + delta * (emitted > 1 ? (float)(k + 1) / emitted : 1f));
                    if (roadAFirstCornerSign == 0 && d.TurnSign != 0)
                        roadAFirstCornerSign = d.TurnSign;
                }
                elev += delta;

                if (i == entryGapIdx)
                {
                    mouthA2D = pos;
                    hIn = heading;
                    y0 = elev;
                }
            }

            // ── Gate frames of road B (mirrored one lane off the flight midline) ──
            Vector2 dirIn = SectionFrameBuilders.HeadingToDir(hIn);
            Vector2 dirOut = SectionFrameBuilders.HeadingToDir(hOut);
            Vector2 rightIn = new Vector2(dirIn.y, -dirIn.x);
            Vector2 rightOut = new Vector2(dirOut.y, -dirOut.x);

            Vector2 mouthB2D = mouthA2D - rightIn * q.LaneSeparation;
            Vector2 lipBRampStart2D = lipARampStart2D - rightOut * q.LaneSeparation;

            // The fitted chain runs between road B's own flare and lip (same ballistic
            // solutions as road A, so the same horizontal runs).
            Vector2 fitStart = mouthB2D + dirIn * Mathf.Max(0.01f, q.EntryJump.LandingHorizontal);
            Vector2 fitTarget = lipBRampStart2D;

            float deltaHeading = hOut - hIn; // raw accumulated — winding preserved

            // ── Road A content length (flare end → lip start) ──
            float roadALength = 0f;
            for (int i = flareAIdx + 1; i < lipAIdx; i++)
            {
                if (defs[i].RoadId == 1) continue;
                roadALength += defs[i].Length;
            }
            if (roadALength < MinFitStraight * 2f)
                return Failed($"road A content is only {roadALength:F0}m — nothing to pair against");

            // ── Whole-lap 2D corridor samples (once per fit): road B must clear every
            // UNRELATED part of the planned lap or the candidate dies later in the
            // expensive built validators — checking here turns those rejections into
            // cheap plan-time retries. Own-quarter geometry is exempt (the pair
            // clearance check owns that pairing). ──
            var lapPts = new List<Vector2>(1024);
            var lapHeights = new List<float>(1024);
            var lapOwn = new List<bool>(1024);
            SampleLap2D(defs, q, lapPts, lapHeights, lapOwn);

            // ── Attempt loop ──
            string zoneId = $"Quarter_{q.Index}";
            // Fit slightly INSIDE the designer tolerance: the validator re-measures the
            // BUILT roads (with both gate ramps included) and drift of a few percent is
            // normal — a fit accepted at the raw edge fails there.
            float tolerance = Mathf.Max(0.02f, cfg.RoadLengthTolerance) * 0.9f;
            string lastFailure = "no attempt ran";
            QuarterRouteBalance lastBalance = null;

            // Character skew gives the archetype estimator something to differentiate:
            // a tighter, slightly shorter road vs a sweeping, slightly longer one.
            // Without the differentiation requirement the skew only widens the
            // neutral-time gap the balance check then rejects — stay neutral.
            bool tighter = rng.NextBool();
            float radiusScale = cfg.RequireArchetypeDifferentiation ? (tighter ? 0.72f : 1.3f) : 1f;
            float lengthSkew = cfg.RequireArchetypeDifferentiation ? Mathf.Min(0.08f, tolerance * 0.5f) : 0f;
            float targetLength = roadALength * (tighter ? 1f - lengthSkew : 1f + lengthSkew);

            // Adaptive attempt state: corner count scales with the QUARTER's size (the
            // old fixed cap of 6 could not span 10+ km quarters at huge-radius scale),
            // shrinks when corner arcs overflow the length target, grows when the 2D
            // solve runs out of reach, and clearance failures force the first corner to
            // diverge AWAY from road A.
            int kBase = Mathf.Clamp(Mathf.RoundToInt(targetLength / Mathf.Max(900f, cfg.MinCurveRadius * 1.5f)), 2, 8);
            int kDelta = 0;
            int forcedFirstSign = 0;
            int balanceRejects = 0;

            for (int attempt = 0; attempt < 18; attempt++)
            {
                // Late attempts may unwind a heavily wound quarter the other way.
                // (Alternating wound/unwound from attempt 0 was measured HARMFUL —
                // the hail-mary unwound attempts pollute the adaptive kDelta state
                // and halve the tries at the usually-correct wound target.)
                float windedTarget = deltaHeading;
                if (attempt >= 9 && Mathf.Abs(deltaHeading) > 180f)
                    windedTarget = deltaHeading - Mathf.Sign(deltaHeading) * 360f;

                int k = Mathf.Clamp(kBase + kDelta, 2, 10);

                if (!TryDrawCorners(k, windedTarget, forcedFirstSign, ref rng, out var cornerAngles))
                {
                    lastFailure = $"could not balance {k} corners to Δ{windedTarget:F0}°";
                    continue;
                }

                // ── Skeleton: S0, C1, S1, …, Ck, Sk ──
                var chain = new List<TrackMacroSectionDefinition>();
                float cornerArcTotal = 0f;
                var cornerDefs = new List<TrackMacroSectionDefinition>();
                foreach (float signedAngle in cornerAngles)
                {
                    float mag = Mathf.Abs(signedAngle);
                    float radius = Mathf.Clamp(
                        Mathf.Max(Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, mag / 180f), cfg.MinCurveRadius) * radiusScale,
                        cfg.MinCurveRadius, cfg.MaxCurveRadius);
                    var c = new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.BankedCurve,
                        Length = SectionFrameBuilders.EasedArcLength(mag, radius),
                        Width = cfg.DualRoadWidth,
                        Direction = signedAngle >= 0f ? SectionTurnDirection.Right : SectionTurnDirection.Left,
                        TurnAngle = mag,
                        Radius = radius,
                        BankingAngle = SectionDefs.RecommendedBank(cfg, radius),
                        SpeedIntent = mag >= 90 ? SectionSpeedIntent.Medium : SectionSpeedIntent.Fast,
                        RiskLevel = tighter ? SectionRiskLevel.Risky : SectionRiskLevel.Normal,
                        Contract = SectionConnectionContract.Level(signedAngle)
                    };
                    cornerDefs.Add(c);
                    cornerArcTotal += c.Length;
                }

                // Corner arcs must leave room for the straights that give the solver
                // its reach: small angles drawn at huge radii produce multi-km arcs
                // that blow straight past the length target. Scale radii down until
                // corners consume at most 60% of it.
                if (cornerArcTotal > targetLength * 0.6f)
                {
                    float arcScale = targetLength * 0.6f / cornerArcTotal;
                    cornerArcTotal = 0f;
                    foreach (var c in cornerDefs)
                    {
                        c.Radius = Mathf.Max(cfg.MinCurveRadius, c.Radius * arcScale);
                        c.Length = SectionFrameBuilders.EasedArcLength(c.TurnAngle, c.Radius);
                        c.BankingAngle = SectionDefs.RecommendedBank(cfg, c.Radius);
                        cornerArcTotal += c.Length;
                    }
                }

                if (cornerArcTotal > targetLength * 1.15f)
                {
                    lastFailure = $"corner arcs ({cornerArcTotal:F0}m) exceed the {targetLength:F0}m length target";
                    kDelta = Mathf.Max(kDelta - 1, -4); // fewer corners next attempt
                    continue;
                }

                float straightInit = Mathf.Max(MinFitStraight, (targetLength - cornerArcTotal) / (k + 1));
                for (int s = 0; s <= k; s++)
                {
                    // RoadId is set at CREATION: the active-set solver grants alternate-
                    // road primitives their own (wider) bounds — quarter spans run tens
                    // of km at this scale and the ordinary straight ceiling starves the
                    // solver of reach.
                    chain.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.Straight,
                        Length = straightInit,
                        Width = cfg.DualRoadWidth,
                        SpeedIntent = SectionSpeedIntent.Fast,
                        RoadId = 1,
                        QuarterIndex = q.Index,
                        Contract = SectionConnectionContract.Level()
                    });
                    if (s < k)
                    {
                        cornerDefs[s].RoadId = 1;
                        cornerDefs[s].QuarterIndex = q.Index;
                        chain.Add(cornerDefs[s]);
                    }
                }

                // ── 2D active-set solve onto the lip anchor ──
                var straightIdx = new List<int>();
                var straightDirs = new List<Vector2>();
                var cornerIdx = new List<int>();
                var cornerDirs = new List<Vector2>();
                var cornerBaseRadius = new List<float>();
                CollectChainVariables(chain, fitStart, hIn, straightIdx, straightDirs, cornerIdx, cornerDirs, cornerBaseRadius,
                    out Vector2 chainEnd);

                Vector2 gap = fitTarget - chainEnd;
                gap = TrackTopologyPlanner.RunActiveSetSolve(cfg, chain, straightIdx, straightDirs,
                    cornerIdx, cornerDirs, cornerBaseRadius, gap);

                if (gap.magnitude > PositionTolerance)
                {
                    lastFailure = $"2D residual {gap.magnitude:F2}m with {k} corners";
                    kDelta = Mathf.Min(kDelta + 1, 4); // more corners = more reach next attempt
                    continue;
                }

                float roadBLength = 0f;
                foreach (var d in chain) roadBLength += d.Length;

                // The active-set solve GROWS straights to reach the anchor and nothing
                // ever pulls them back — a converged-but-long chain is its most common
                // outcome. Shrink the straight slack toward the target and let one more
                // solve re-close the residual before judging the length.
                if (roadBLength > roadALength * (1f + tolerance))
                {
                    float shrinkable = 0f;
                    foreach (var d in chain)
                        if (d.SectionType == TrackMacroSectionType.Straight)
                            shrinkable += Mathf.Max(0f, d.Length - MinFitStraight);

                    if (shrinkable > 1f)
                    {
                        float scale = 1f - Mathf.Clamp01((roadBLength - targetLength) / shrinkable);
                        foreach (var d in chain)
                        {
                            if (d.SectionType != TrackMacroSectionType.Straight) continue;
                            d.Length = MinFitStraight + (d.Length - MinFitStraight) * scale;
                        }

                        straightIdx.Clear(); straightDirs.Clear();
                        cornerIdx.Clear(); cornerDirs.Clear(); cornerBaseRadius.Clear();
                        CollectChainVariables(chain, fitStart, hIn, straightIdx, straightDirs,
                            cornerIdx, cornerDirs, cornerBaseRadius, out chainEnd);
                        gap = fitTarget - chainEnd;
                        gap = TrackTopologyPlanner.RunActiveSetSolve(cfg, chain, straightIdx, straightDirs,
                            cornerIdx, cornerDirs, cornerBaseRadius, gap);
                        if (gap.magnitude > PositionTolerance)
                        {
                            lastFailure = $"rebalance residual {gap.magnitude:F2}m with {k} corners";
                            kDelta = Mathf.Min(kDelta + 1, 4);
                            continue;
                        }

                        roadBLength = 0f;
                        foreach (var d in chain) roadBLength += d.Length;
                    }
                }

                if (Mathf.Abs(roadBLength - roadALength) > tolerance * roadALength)
                {
                    lastFailure = $"road B fitted to {roadBLength:F0}m vs road A {roadALength:F0}m (tolerance ±{tolerance * roadALength:F0}m)";
                    // Over-length means the chain wound around too much — drop a corner;
                    // under-length means it ran too direct — add one for more arc.
                    kDelta = roadBLength > roadALength
                        ? Mathf.Max(kDelta - 1, -4)
                        : Mathf.Min(kDelta + 1, 4);
                    continue;
                }

                // ── Elevation: replicate road A's net grade change across the straights ──
                if (!TryDistributeElevation(cfg, chain, y1 - y0, out string elevFailure))
                {
                    lastFailure = elevFailure;
                    continue;
                }

                // ── Plan-time body clearance: where the two roads pass near each other
                // (outside the gate throats) they need the full road envelope laterally
                // or real vertical separation — the validator enforces exactly this on
                // the built frames, so reject unbuildable pairings here and retry with
                // the first corner forced AWAY from road A.
                if (!ChainClearsRoadA(cfg, chain, fitStart, hIn, y0, roadAPts, roadAHeights, out string clearFailure))
                {
                    lastFailure = clearFailure;
                    forcedFirstSign = roadAFirstCornerSign != 0
                        ? -roadAFirstCornerSign
                        : (attempt % 2 == 0 ? 1 : -1);
                    continue;
                }

                if (!ChainClearsLap(cfg, chain, fitStart, hIn, y0, lapPts, lapHeights, lapOwn, out string lapFailure))
                {
                    lastFailure = lapFailure;
                    continue;
                }

                // ── Provisional frames + archetype balance (validation only) ──
                var framesA = BuildChainFrames(defs, flareAIdx, lipAIdx, cfg);
                var chainB = new List<TrackMacroSectionDefinition>();
                var flareB = MakeGateRamp(q, cfg, entry: true);
                var lipB = MakeGateRamp(q, cfg, entry: false);
                chainB.Add(flareB);
                chainB.AddRange(chain);
                chainB.Add(lipB);
                // Road B is fitted after the plan-wide ConnectorAnalyzer pass. Classify
                // its locked turn links now so the surface fields do not treat every
                // fitted straight as an unrelated neutral section.
                ConnectorAnalyzer.AnalyzeFittedChain(chainB, cfg);
                var framesB = BuildChainFrames(chainB, 0, chainB.Count - 1, cfg);

                var balance = RouteTimeEstimator.Evaluate(q.Index, framesA, framesB, cfg);
                lastBalance = balance;
                if (!balance.Acceptable(cfg.RequireArchetypeDifferentiation))
                {
                    lastFailure = $"balance rejected (neutral Δ {balance.NeutralTimeDifferencePercent:F1}%, " +
                                  $"A preferred: {balance.RouteAPreferredBySomeArchetype}, B preferred: {balance.RouteBPreferredBySomeArchetype})";

                    // Repeated rejections mean the CHARACTER skew (tight/slow vs
                    // sweeping/fast) is what length retargeting can't bridge — walk the
                    // radius scale back toward neutral so the estimator can converge.
                    balanceRejects++;
                    if (balanceRejects >= 2)
                        radiusScale = Mathf.MoveTowards(radiusScale, 1f, 0.15f);

                    // Retarget: pull road B's length toward equal NEUTRAL time for the
                    // next attempt (a slower road shortens, a faster one lengthens).
                    foreach (var row in balance.TimeTable)
                    {
                        if (row.Archetype != CraftArchetype.Neutral || row.RouteBSeconds <= 0.1f) continue;
                        targetLength *= Mathf.Clamp(row.RouteASeconds / row.RouteBSeconds, 0.85f, 1.15f);
                        targetLength = Mathf.Clamp(targetLength, roadALength * (1f - tolerance), roadALength * (1f + tolerance));
                        break;
                    }
                    continue;
                }

                // ── Accept: tag and insert ──
                for (int i = 0; i < chainB.Count; i++)
                {
                    var d = chainB[i];
                    d.RoadId = 1;
                    d.QuarterIndex = q.Index;
                    d.PatternId = zoneId;
                    d.LockLength = true;
                    if (string.IsNullOrEmpty(d.DebugName) || i > 0 && i < chainB.Count - 1)
                        d.DebugName = $"Q{q.Index}RoadB_{i:D2}_{d.SectionType}";
                }

                defs.InsertRange(q.RoadBInsertIndex, chainB);

                // Later quarters' recorded def indices shift by the insertion.
                foreach (var other in plan.Quarters)
                {
                    if (other == q) continue;
                    if (other.FirstDefIndex > q.RoadBInsertIndex) other.FirstDefIndex += chainB.Count;
                    if (other.RoadBInsertIndex > q.RoadBInsertIndex) other.RoadBInsertIndex += chainB.Count;
                }

                q.RoadALength = roadALength;
                q.RoadBLength = roadBLength;
                q.Balance = balance;
                return new FitOutcome { Success = true };
            }

            bool balanceOnly = lastBalance != null && lastFailure.StartsWith("balance rejected");
            return new FitOutcome
            {
                Success = false,
                BalanceFailure = balanceOnly,
                FailureMessage = lastFailure
            };
        }

        private static FitOutcome Failed(string message)
            => new FitOutcome { Success = false, FailureMessage = message };

        // ─────────────────────────── helpers ───────────────────────────

        /// <summary>
        /// Draws k signed corner magnitudes (5° grid, 20–150°) whose sum is nudged to
        /// exactly targetDeg. A non-zero forcedFirstSign pins the FIRST corner's
        /// direction (≥60°) so road B diverges away from road A after a clearance
        /// failure — the balancing passes never flip or shrink it below that.
        /// </summary>
        private static bool TryDrawCorners(int k, float targetDeg, int forcedFirstSign,
            ref Unity.Mathematics.Random rng, out float[] angles)
        {
            angles = new float[k];
            int sign = forcedFirstSign != 0 ? forcedFirstSign : (rng.NextBool() ? 1 : -1);
            float sum = 0f;
            for (int i = 0; i < k; i++)
            {
                int mag = 20 + 5 * rng.NextInt(0, 21); // 20..120
                if (i == 0 && forcedFirstSign != 0) mag = Mathf.Max(mag, 60);
                if (i > 0 && rng.NextFloat() < 0.6f) sign = -sign;
                angles[i] = sign * mag;
                sum += angles[i];
            }

            float MinMagOf(int i) => i == 0 && forcedFirstSign != 0 ? 60f : 20f;
            bool Flippable(int i) => !(i == 0 && forcedFirstSign != 0);

            // Distribute the residual across corners within their magnitude bounds.
            for (int pass = 0; pass < 128 && Mathf.Abs(targetDeg - sum) > 0.01f; pass++)
            {
                float residual = targetDeg - sum;
                int idx = rng.NextInt(0, k);
                float s = Mathf.Sign(angles[idx]);
                float mag = Mathf.Abs(angles[idx]);
                float step = Mathf.Clamp(residual * s, -25f, 25f);
                float newMag = Mathf.Clamp(mag + step, MinMagOf(idx), 150f);

                if (Mathf.Approximately(newMag, mag) && pass > k * 4)
                {
                    // Saturated: flip the corner whose flip best approaches the target.
                    int best = -1;
                    float bestErr = Mathf.Abs(residual);
                    for (int i = 0; i < k; i++)
                    {
                        if (!Flippable(i)) continue;
                        float err = Mathf.Abs(targetDeg - (sum - 2f * angles[i]));
                        if (err < bestErr) { bestErr = err; best = i; }
                    }
                    if (best >= 0)
                    {
                        sum -= 2f * angles[best];
                        angles[best] = -angles[best];
                    }
                    continue;
                }

                sum += (newMag - mag) * s;
                angles[idx] = s * newMag;
            }

            return Mathf.Abs(targetDeg - sum) <= 0.01f;
        }

        /// <summary>
        /// Samples the whole planned lap in 2D at ~100 m (canonical road only), with
        /// plan-model heights and an "own quarter" flag per sample — the same model as
        /// the planner's self-proximity walk, coarser and reusable per fit attempt.
        /// </summary>
        private static void SampleLap2D(List<TrackMacroSectionDefinition> defs, PlannedQuarter q,
            List<Vector2> pts, List<float> heights, List<bool> own)
        {
            string zoneId = $"Quarter_{q.Index}";
            Vector2 pos = Vector2.zero;
            float heading = 0f;
            float elev = 0f;

            foreach (var d in defs)
            {
                if (d.RoadId == 1) continue;

                bool isOwn = d.QuarterIndex == q.Index || d.PatternId == zoneId;
                int first = pts.Count;
                WalkDense(d, ref pos, ref heading, pts);

                // Plan-model elevation, linear across the def's samples.
                float delta = d.IsStraightFamily || d.SectionType == TrackMacroSectionType.Spiral ||
                              d.SectionType == TrackMacroSectionType.Corkscrew ||
                              d.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                              d.SectionType == TrackMacroSectionType.RotationalEvent
                    ? d.ElevationChange
                    : 0f;
                int emitted = pts.Count - first;
                for (int k = 0; k < emitted; k++)
                {
                    float t = emitted > 1 ? (float)(k + 1) / emitted : 1f;
                    heights.Add(elev + delta * t);
                    own.Add(isOwn);
                }
                elev += delta;
            }
        }

        /// <summary>
        /// Advances the walk by one canonical def like <see cref="Walk2D"/>, emitting
        /// samples ~every 100 m along the way (vertical features own their airspace
        /// and emit only their endpoints). Shared with the planner's S-bend host check.
        /// </summary>
        internal static void WalkDense(TrackMacroSectionDefinition d, ref Vector2 pos, ref float heading, List<Vector2> pts)
        {
            const float step = 100f;
            float arcDummy = 0f;

            switch (d.SectionType)
            {
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                case TrackMacroSectionType.WallrideTurn:
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                    break;
                case TrackMacroSectionType.SCurve:
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, -d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                    break;
                case TrackMacroSectionType.Chicane:
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, -2f * d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                    break;
                case TrackMacroSectionType.Loop:
                case TrackMacroSectionType.Spiral:
                case TrackMacroSectionType.HalfLoopTwist:
                case TrackMacroSectionType.RotationalEvent:
                    Walk2D(d, ref pos, ref heading);
                    pts.Add(pos);
                    break;
                default:
                {
                    Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);
                    float run = d.HorizontalRun;
                    for (float s = step; s < run; s += step) pts.Add(pos + fwd * s);
                    pos += fwd * run;
                    if (d.PlanLateralOffset != 0f)
                        pos += new Vector2(fwd.y, -fwd.x) * d.PlanLateralOffset;
                    pts.Add(pos);
                    break;
                }
            }
        }

        /// <summary>
        /// Samples a candidate road-B chain (straights + banked curves only) ~every
        /// 100 m with linear per-def elevation — the shared source for both plan-time
        /// clearance checks.
        /// </summary>
        private static void SampleChain2D(List<TrackMacroSectionDefinition> chain, Vector2 start, float startHeading,
            float startElevation, List<Vector2> pts, List<float> heights)
        {
            const float step = 100f;
            Vector2 pos = start;
            float heading = startHeading;
            float elev = startElevation;
            float arcDummy = 0f;

            foreach (var d in chain)
            {
                int first = pts.Count;
                if (d.SectionType == TrackMacroSectionType.Straight)
                {
                    Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);
                    for (float s = step; s < d.Length; s += step) pts.Add(pos + fwd * s);
                    pos += fwd * d.Length;
                    pts.Add(pos);
                }
                else
                {
                    SectionFrameBuilders.WalkEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius, step,
                        (p, a) => { pts.Add(p); }, ref arcDummy);
                }

                int emitted = pts.Count - first;
                for (int k = 0; k < emitted; k++)
                {
                    float t = emitted > 1 ? (float)(k + 1) / emitted : 1f;
                    heights.Add(elev + d.ElevationChange * t);
                }
                elev += d.ElevationChange;
            }
        }

        /// <summary>
        /// The fitted chain must clear every unrelated lap sample by the road corridor
        /// (same thresholds as the planner's 2D self-proximity walk) or gain vertical
        /// separation — otherwise the built candidate would die in the validators.
        /// </summary>
        private static bool ChainClearsLap(ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> chain, Vector2 start, float startHeading, float startElevation,
            List<Vector2> lapPts, List<float> lapHeights, List<bool> lapOwn, out string failure)
        {
            failure = null;
            if (lapPts.Count < 8) return true;

            var pts = new List<Vector2>(256);
            var heights = new List<float>(256);
            SampleChain2D(chain, start, startHeading, startElevation, pts, heights);

            float minClear = cfg.UnrelatedCorridor * 1.05f;
            float minClearSq = minClear * minClear;
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);

            for (int i = 0; i < pts.Count; i++)
            {
                for (int j = 0; j < lapPts.Count; j++)
                {
                    if (lapOwn[j]) continue;
                    if ((pts[i] - lapPts[j]).sqrMagnitude >= minClearSq) continue;
                    if (Mathf.Abs(heights[i] - lapHeights[j]) >= verticalOk) continue;
                    failure = $"road B passes {(pts[i] - lapPts[j]).magnitude:F0}m from an unrelated lap segment " +
                              $"(need {minClear:F0}m or {verticalOk:F0}m vertical)";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Plan-time clearance between the fitted chain and road A's body samples:
        /// the middle 70% of both roads must keep the full lateral road envelope OR
        /// meaningful vertical separation (mirrors ValidateRoadPairClearance).
        /// </summary>
        private static bool ChainClearsRoadA(ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> chain, Vector2 start, float startHeading, float startElevation,
            List<Vector2> roadAPts, List<float> roadAHeights, out string failure)
        {
            failure = null;
            if (roadAPts.Count < 3) return true;

            var pts = new List<Vector2>(256);
            var heights = new List<float>(256);
            SampleChain2D(chain, start, startHeading, startElevation, pts, heights);
            if (pts.Count < 3) return true;

            // +3 m over the built validator's requirement: built geometry drifts ~1-2 m
            // from this 2D model, and the validator's own tolerance is only ~0.5 m —
            // a chain approved at the exact threshold dies after paying for the build.
            float requiredLateral = Mathf.Max(cfg.RoadWidth, cfg.DualRoadWidth)
                                  + cfg.RoadProfile.SideHeight * 2f + cfg.WallMaskSafetyMargin + 3f;
            float requiredVertical = cfg.VerticalClearance * 0.85f;
            float requiredSq = requiredLateral * requiredLateral;

            int aLo = Mathf.RoundToInt(roadAPts.Count * 0.15f);
            int aHi = Mathf.RoundToInt(roadAPts.Count * 0.85f);
            int bLo = Mathf.RoundToInt(pts.Count * 0.15f);
            int bHi = Mathf.RoundToInt(pts.Count * 0.85f);

            for (int a = aLo; a < aHi; a++)
            {
                for (int b = bLo; b < bHi; b++)
                {
                    if ((roadAPts[a] - pts[b]).sqrMagnitude >= requiredSq) continue;
                    if (Mathf.Abs(roadAHeights[a] - heights[b]) >= requiredVertical) continue;
                    float dist = (roadAPts[a] - pts[b]).magnitude;
                    failure = $"road B passes {dist:F0}m from road A (need {requiredLateral:F0}m lateral or {requiredVertical:F0}m vertical)";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Advances the 2D walk by one canonical def (same model as the planner's walkers).</summary>
        private static void Walk2D(TrackMacroSectionDefinition d, ref Vector2 pos, ref float heading)
        {
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
                    pos += fwd * d.PlanHorizontalLength;
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

        /// <summary>Collects the chain's closure variables (all straights + all corners) walking from an arbitrary start.</summary>
        private static void CollectChainVariables(List<TrackMacroSectionDefinition> chain, Vector2 start, float startHeading,
            List<int> straightIdx, List<Vector2> straightDirs,
            List<int> cornerIdx, List<Vector2> cornerDirs, List<float> cornerBaseRadius, out Vector2 end)
        {
            Vector2 pos = start;
            float heading = startHeading;

            for (int i = 0; i < chain.Count; i++)
            {
                var d = chain[i];
                Vector2 fwd = SectionFrameBuilders.HeadingToDir(heading);

                if (d.SectionType == TrackMacroSectionType.Straight)
                {
                    straightIdx.Add(i);
                    straightDirs.Add(fwd);
                    pos += fwd * d.Length;
                }
                else
                {
                    Vector2 right = new Vector2(fwd.y, -fwd.x);
                    Vector2 unitOffset = SectionFrameBuilders.EasedArcEndOffset(d.TurnAngle, 1f);
                    cornerIdx.Add(i);
                    cornerDirs.Add(fwd * unitOffset.x + right * (d.TurnSign * unitOffset.y));
                    cornerBaseRadius.Add(d.Radius);
                    SectionFrameBuilders.ApplyEasedArc2D(ref pos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                }
            }

            end = pos;
        }

        /// <summary>Distributes the quarter's net grade change across the chain's straights, capacity-capped.</summary>
        public static bool TryDistributeElevation(ResolvedTrackGenerationConfig cfg,
            List<TrackMacroSectionDefinition> chain, float deltaY, out string failure)
        {
            failure = null;

            var straights = new List<TrackMacroSectionDefinition>();
            float lengthTotal = 0f;
            foreach (var d in chain)
            {
                if (d.SectionType != TrackMacroSectionType.Straight) continue;
                // Each attempt owns fresh definitions, but clearing here also makes the
                // allocator safe to rerun during diagnostics/tests.
                d.ElevationChange = 0f;
                if (d.Length < MinElevationCarrierLength) continue;
                straights.Add(d);
                lengthTotal += d.Length;
            }
            if (Mathf.Abs(deltaY) < 0.25f) return true;
            if (straights.Count == 0 || lengthTotal < 1f)
            {
                failure = $"no straights at least {MinElevationCarrierLength:F0}m long to carry the {deltaY:F0}m grade change";
                return false;
            }

            float maxAngle = deltaY > 0f ? cfg.MaxClimbAngle : cfg.MaxDropAngle;
            float remaining = deltaY;
            foreach (var d in straights)
            {
                float share = deltaY * (d.Length / lengthTotal);
                float cap = d.Length * Mathf.Tan(maxAngle * Mathf.Deg2Rad) / 1.5f;
                d.ElevationChange = Mathf.Clamp(share, -cap, cap);
                remaining -= d.ElevationChange;
            }

            // Bleed any capped residual into straights with spare capacity.
            for (int pass = 0; pass < 4 && Mathf.Abs(remaining) > 0.25f; pass++)
            {
                foreach (var d in straights)
                {
                    if (Mathf.Abs(remaining) <= 0.25f) break;
                    float cap = d.Length * Mathf.Tan(maxAngle * Mathf.Deg2Rad) / 1.5f;
                    float take = Mathf.Clamp(remaining, -cap - d.ElevationChange, cap - d.ElevationChange);
                    d.ElevationChange += take;
                    remaining -= take;
                }
            }

            if (Mathf.Abs(remaining) > 0.5f)
            {
                failure = $"grade residual {remaining:F1}m exceeds road B's slope capacity";
                return false;
            }
            if (Mathf.Abs(remaining) > 0.001f)
                straights[straights.Count - 1].ElevationChange += remaining;
            return true;
        }

        /// <summary>Road B's own landing flare / launch lip def (same ballistic solutions as road A's).</summary>
        private static TrackMacroSectionDefinition MakeGateRamp(PlannedQuarter q, ResolvedTrackGenerationConfig cfg, bool entry)
        {
            var tmp = new List<TrackMacroSectionDefinition>(1);
            if (entry)
                JumpGapPattern.EmitLandingRamp(in q.EntryJump, cfg.DualRoadWidth, $"Quarter_{q.Index}", "QuarterEntryFlareB_", tmp);
            else
                JumpGapPattern.EmitJumpRamp(in q.ExitJump, cfg.DualRoadWidth, $"Quarter_{q.Index}", "QuarterExitB_", tmp);
            return tmp[0];
        }

        /// <summary>
        /// Builds provisional frames for a def range so the archetype estimator can
        /// compare the two roads at plan time. Shape only — the banking field and
        /// retopology later refine the real frames; both roads are treated identically,
        /// which is what a balance comparison needs.
        /// </summary>
        private static TrackConnectionFrame[] BuildChainFrames(List<TrackMacroSectionDefinition> defs,
            int first, int last, ResolvedTrackGenerationConfig cfg)
        {
            // FIXED sampling density: the balance verdict is a LAYOUT decision, so it
            // must never depend on the designer's mesh density setting.
            var ctx = FrameBuildContext.From(cfg);
            ctx.MetersPerRing = 4f;
            ctx.FeatureMetersPerRing = 4f;
            var all = new List<TrackConnectionFrame>();
            TrackConnectionFrame frame = TrackConnectionFrame.Origin(defs[first].Width);

            for (int i = first; i <= last; i++)
            {
                var def = defs[i];
                TrackConnectionFrame[] frames;
                switch (def.SectionType)
                {
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                        frames = SectionFrameBuilders.BuildArc(frame, def.TurnAngle * def.TurnSign, def.Radius, def.BankingAngle, ctx);
                        break;
                    case TrackMacroSectionType.SCurve:
                        frames = SectionFrameBuilders.BuildComposedArcs(frame,
                            new[] { def.TurnAngle * def.TurnSign, -def.TurnAngle * def.TurnSign },
                            new[] { def.Radius, def.Radius }, def.BankingAngle, ctx);
                        break;
                    case TrackMacroSectionType.Chicane:
                        frames = SectionFrameBuilders.BuildComposedArcs(frame,
                            new[] { def.TurnAngle * def.TurnSign, -2f * def.TurnAngle * def.TurnSign, def.TurnAngle * def.TurnSign },
                            new[] { def.Radius, def.Radius, def.Radius }, def.BankingAngle, ctx);
                        break;
                    case TrackMacroSectionType.JumpRamp:
                        frames = SectionFrameBuilders.BuildKeyframedPitchRamp(frame, def.Length,
                            SectionFrameBuilders.LaunchRampKeys(def.SecondaryPitchDeg, def.PitchChange), ctx);
                        break;
                    case TrackMacroSectionType.LandingRamp:
                        frames = SectionFrameBuilders.BuildKeyframedPitchRamp(frame, def.Length,
                            SectionFrameBuilders.LandingRampKeys(def.PitchChange, def.SecondaryPitchDeg), ctx);
                        break;
                    case TrackMacroSectionType.AirGap:
                    {
                        // No road mesh spans the gap, but the provisional timing cursor
                        // must land at the real ballistic endpoint. Otherwise an internal
                        // Road-A jump makes every following feature start at its launch lip.
                        Vector3 dirH = SectionFrameBuilders.Flatten(frame.Forward);
                        var landing = frame;
                        landing.Position = frame.Position + dirH * def.PlanHorizontalLength
                                           + SectionFrameBuilders.Flatten(frame.Right) * def.PlanLateralOffset
                                           + Vector3.up * def.ElevationChange;
                        float arrivalRad = def.PitchChange * Mathf.Deg2Rad;
                        Vector3 arrivalFwd = (dirH * Mathf.Cos(arrivalRad) +
                                              Vector3.up * Mathf.Sin(arrivalRad)).normalized;
                        landing.Forward = arrivalFwd;
                        landing.Right = SectionFrameBuilders.Flatten(frame.Right);
                        landing.Up = Vector3.Cross(arrivalFwd, landing.Right).normalized;
                        landing.PitchAngle = def.PitchChange;
                        landing.BankAngle = 0f;
                        landing.ArcLength = frame.ArcLength + def.Length;
                        frame = landing;
                        continue;
                    }
                    case TrackMacroSectionType.Loop:
                        frames = SectionFrameBuilders.BuildLoop(frame, def, ctx);
                        break;
                    case TrackMacroSectionType.Corkscrew:
                        frames = SectionFrameBuilders.BuildCorkscrew(frame, def, ctx);
                        break;
                    case TrackMacroSectionType.Spiral:
                        frames = SectionFrameBuilders.BuildSpiral(frame, def, ctx);
                        break;
                    case TrackMacroSectionType.HalfLoopTwist:
                        frames = SectionFrameBuilders.BuildHalfLoopRollout(frame, def, ctx);
                        break;
                    case TrackMacroSectionType.RotationalEvent:
                        frames = SectionFrameBuilders.BuildRotationalEvent(frame, def, ctx);
                        break;
                    case TrackMacroSectionType.FullPipe:
                        frames = SectionFrameBuilders.BuildFullPipe(frame, def, ctx);
                        break;
                    case TrackMacroSectionType.WallrideTurn:
                        frames = SectionFrameBuilders.BuildWallrideTurn(frame, def, ctx);
                        break;
                    default:
                        frames = SectionFrameBuilders.BuildStraight(frame, def, ctx);
                        break;
                }

                if (frames == null || frames.Length == 0) continue;
                int skip = all.Count > 0 ? 1 : 0; // shared boundary ring
                for (int f = skip; f < frames.Length; f++) all.Add(frames[f]);
                frame = frames[frames.Length - 1];
            }

            return all.ToArray();
        }
    }
}
