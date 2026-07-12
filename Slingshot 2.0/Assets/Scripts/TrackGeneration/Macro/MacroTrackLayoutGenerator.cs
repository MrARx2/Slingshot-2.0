using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// The "designer brain" of the macro track generator.
    ///
    /// Consumes ONLY a <see cref="ResolvedTrackGenerationConfig"/> (designer intent already
    /// translated and clamped by the TrackConfig rulebook) and produces a closed loop of
    /// readable macro race sections with resolved connection frames.
    ///
    /// Enforced rules:
    /// <list type="bullet">
    /// <item>Jump groups are atomic: Boost Straight → Jump Ramp → Air Gap → Landing Ramp → Recovery Straight.</item>
    /// <item>Loops and corkscrews are ONE macro section each, always wrapped in
    /// Approach Straight → feature → Recovery Straight, never after a sharp turn or jump.</item>
    /// <item>Recovery straights are mandatory after hairpins, chicanes, loops, corkscrews.</item>
    /// <item>Hairpins max one per track; chicanes/S-curves limited; corkscrews never back-to-back.</item>
    /// <item>All corners banked by default; fully deterministic from the seeded RNG.</item>
    /// </list>
    ///
    /// Loop closure: corner angles sum to EXACTLY ±360°, straights absorb the position gap,
    /// and a residual pass welds the loop shut.
    /// </summary>
    public class MacroTrackLayoutGenerator
    {
        // High turn counts make valid (non-self-intersecting) layouts rarer — attempts
        // are cheap (layout math only, no meshes), so search harder before falling back.
        private const int MaxAttempts = 24;
        private const float MinAdjustableStraight = 30f;
        private const float JumpRampLength = 15f;
        private const float LandingRampLength = 16f;

        // Fallback bank ease fraction when a blend length cannot be honored.
        private const float MinBankEaseFraction = 0.15f;
        private const float MaxBankEaseFraction = 0.5f;

        // ── Bobsled banking (flat floor, asymmetric walls) ──
        // At full bank (90°) the outside half-pipe wall grows by this multiplier
        // fraction (1 = double height) and the inside wall shrinks by the trim
        // fraction. The road floor itself never leaves grade.
        private const float BobsledOuterWallBoost = 1.0f;
        private const float BobsledInnerWallTrim = 0.45f;

        // ── Clothoid-style loop profile ──
        // Fraction of the loop arc spent easing curvature in/out at each end.
        // A perfect circle has a curvature DISCONTINUITY at entry/exit (0 → 1/R in
        // one ring) — at racing speed that kink bumps the craft off the road no
        // matter how good the suspension is. Easing the curvature (like real
        // roller-coaster clothoid loops) removes the kink entirely.
        private const float LoopCurvatureEaseFraction = 0.18f;
        private const int LoopProfileSamples = 512;

        // Lazily built normalized loop tables (deterministic, RNG-free).
        private static float[] _loopThetaTable;      // pitch angle (radians) over u ∈ [0,1]
        private static Vector2[] _loopPlaneTable;    // (forward, up) position / loop length
        private static float _loopForwardDisplacementFactor; // net forward displacement / length

        // Half-loop (Immelmann) profile tables: pitch 0→180° with the same eased curvature.
        private static float[] _halfLoopThetaTable;
        private static Vector2[] _halfLoopPlaneTable;

        // Ring budget (set from the resolved config each generation).
        private int _maxRingsPerSection = 512;

        // ──────────────────────────── Public API ────────────────────────────

        public List<GeneratedTrackSection> Generate(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng)
        {
            _maxRingsPerSection = Mathf.Max(8, cfg.MaxRingsPerSection);

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                uint attemptSeed = rng.NextUInt();
                if (attemptSeed == 0) attemptSeed = 1;
                var attemptRng = new Unity.Mathematics.Random(attemptSeed);

                List<TrackMacroSectionDefinition> defs = BuildSequence(cfg, ref attemptRng, attempt);
                SolveStraightLengths(defs, cfg);
                PlanElevation(defs, cfg, ref attemptRng);
                List<GeneratedTrackSection> sections = LayoutFrames(defs, cfg);
                DistributeResidual(sections);
                ApplyCrossSectionBlend(sections, cfg);
                ApplyWallMasks(sections, cfg);

                if (Validate(sections, cfg))
                {
                    Debug.Log($"[MacroTrackLayoutGenerator] Layout OK on attempt {attempt + 1}: {defs.Count} sections, {sections[sections.Count - 1].EndFrame.ArcLength:F0}m.");
                    LogTrackSummary(sections, cfg);
                    return sections;
                }
            }

            Debug.LogWarning("[MacroTrackLayoutGenerator] All attempts failed validation — using template layout.");
            List<TrackMacroSectionDefinition> template = BuildTemplateSequence(cfg);
            SolveStraightLengths(template, cfg);
            List<GeneratedTrackSection> fallback = LayoutFrames(template, cfg);
            DistributeResidual(fallback);
            ApplyCrossSectionBlend(fallback, cfg);
            ApplyWallMasks(fallback, cfg);
            LogTrackSummary(fallback, cfg);
            return fallback;
        }

        // ──────────────────────────── 1. Sequence (the grammar) ────────────────────────────

        private List<TrackMacroSectionDefinition> BuildSequence(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng, int attempt = 0)
        {
            var defs = new List<TrackMacroSectionDefinition>();
            float width = cfg.RoadWidth;

            // ── Corner plan ──
            // Automatic (TurnCount = 0): classic same-direction circuit — unsigned
            // angles composing exactly 360°.
            // Explicit TurnCount: mountain-pass mode — MIXED left/right corners
            // (switchback-biased) whose SIGNED sum closes the lap at ±360°.
            int[] angleOptions = SanitizeAngleOptions(cfg.CornerAngleOptions);
            List<int> signedAngles;

            if (cfg.TargetTurnCount > 0)
            {
                signedAngles = PlanSignedCorners(cfg, angleOptions, ref rng);
            }
            else
            {
                int dirSign = rng.NextBool() ? 1 : -1;
                List<int> plain = PlanCornerAngles(angleOptions, cfg.TargetMacroSectionCount, ref rng);
                signedAngles = new List<int>(plain.Count);
                foreach (int a in plain) signedAngles.Add(a * dirSign);
            }

            bool mixedDirections = cfg.TargetTurnCount > 0;

            // ── Room-driven radii (mountain-pass mode) ──
            // An explicit TurnCount is a COMMAND, and turns deserve ROOM: corner radii
            // honor the rulebook band (sharper corner → tighter end of the band) and
            // the LAP GROWS to fit them — the length preset is a starting point, not a
            // cage. Radii only shrink when the absolute MaxTrackLength cap forces it,
            // plus a gentle per-attempt shrink that helps a colliding layout untangle.
            float radiusScale = 1f;
            if (mixedDirections)
            {
                float totalArc = 0f;
                foreach (int a in signedAngles)
                {
                    float mag = Mathf.Abs(a);
                    totalArc += mag * Mathf.Deg2Rad * Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, mag / 180f);
                }

                float requiredLength = totalArc * 2f; // corners ≈ half the lap
                float lengthCap = Mathf.Max(cfg.TargetTrackLength, cfg.MaxTrackLength);

                if (requiredLength > lengthCap)
                {
                    radiusScale = lengthCap / requiredLength;
                    if (attempt == 0)
                        Debug.LogWarning($"[MacroTrackLayoutGenerator] TurnCount {cfg.TargetTurnCount}: even at MaxTrackLength {lengthCap:F0}m the turns must scale to {radiusScale:P0} of rulebook radii. Raise MaxTrackLength in TrackConfig for wider turns.");
                }
                else if (attempt == 0 && requiredLength > cfg.TargetTrackLength)
                {
                    Debug.Log($"[MacroTrackLayoutGenerator] TurnCount {cfg.TargetTurnCount}: lap grows to ~{requiredLength:F0}m so all {signedAngles.Count} turns keep rulebook-sized radii.");
                }

                radiusScale *= Mathf.Pow(0.95f, attempt);
            }

            var corners = new List<TrackMacroSectionDefinition>();
            bool hairpinPlaced = false;
            int halfLoopTwistsPlaced = 0;

            foreach (int signedAngle in signedAngles)
            {
                int angle = Mathf.Abs(signedAngle);
                SectionTurnDirection dir = signedAngle >= 0 ? SectionTurnDirection.Right : SectionTurnDirection.Left;

                // A near-reversal corner may become a half-loop + half-twist (Immelmann):
                // same 180° heading change, delivered as a vertical spectacle.
                if (angle >= 170 && halfLoopTwistsPlaced < 2 && cfg.HalfLoopTwistChance > 0f
                    && rng.NextFloat() < cfg.HalfLoopTwistChance)
                {
                    halfLoopTwistsPlaced++;
                    corners.Add(MakeHalfLoopTwist(cfg, dir, width, ref rng));
                    continue;
                }

                // Mountain-pass mode allows MANY sharp corners; the classic mode keeps
                // the original single-hairpin rule.
                bool isHairpin = mixedDirections
                    ? angle >= 130
                    : !hairpinPlaced && angle >= 120 && rng.NextFloat() < cfg.HairpinChance;
                if (isHairpin && !mixedDirections) hairpinPlaced = true;

                // Sharper corners bind to the tighter end of the rulebook band so
                // switchbacks read as switchbacks; sweepers stay grand.
                float radius = mixedDirections
                    ? Mathf.Max(Mathf.Lerp(cfg.MaxCurveRadius, cfg.MinCurveRadius, angle / 180f) * radiusScale * (isHairpin ? 0.8f : 1f), cfg.RoadWidth * 1.5f)
                    : PickRadius(cfg, ref rng, isHairpin);
                float bank = ComputeBankAngle(cfg, angle, isHairpin);

                corners.Add(new TrackMacroSectionDefinition
                {
                    SectionType = isHairpin ? TrackMacroSectionType.BankedHairpin : TrackMacroSectionType.BankedCurve,
                    Length = Mathf.Deg2Rad * angle * radius,
                    Width = width,
                    Direction = dir,
                    TurnAngle = angle,
                    Radius = radius,
                    BankingAngle = bank,
                    SpeedIntent = isHairpin ? SectionSpeedIntent.Slow : (angle >= 90 ? SectionSpeedIntent.Medium : SectionSpeedIntent.Fast),
                    RiskLevel = isHairpin ? SectionRiskLevel.Risky : SectionRiskLevel.Normal,
                    RequiresRecoveryAfter = isHairpin,
                    DebugName = $"{(isHairpin ? "BankedHairpin" : "BankedCurve")}_{angle}deg_{dir}"
                });
            }

            // Corner arc budget sanity (automatic mode only — mountain-pass mode grows
            // the lap deliberately and reports its own scaling above).
            if (!mixedDirections)
            {
                float totalCornerArc = 0f;
                foreach (var c in corners) totalCornerArc += c.Length;
                if (totalCornerArc > cfg.TargetTrackLength * 0.8f)
                    Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: {corners.Count} corners need {totalCornerArc:F0}m of arc vs target track length {cfg.TargetTrackLength:F0}m — the lap will run long. Lower TurnCount or reduce MinCurveRadius in TrackConfig.");
            }

            // ── Fill the gaps between corners ──
            int jumpsPlaced = 0, sCurvesPlaced = 0, chicanesPlaced = 0, loopsPlaced = 0, corksPlaced = 0, layeredPlaced = 0, crossingsPlaced = 0, spiralsPlaced = 0;
            int lastCorkGap = -10;
            int maxJumps = cfg.JumpChance > 0.5f ? 3 : 2;
            int maxLoops = cfg.LoopChance >= 0.5f ? 3 : 2;
            int maxSpirals = cfg.SpiralChance >= 0.5f ? 3 : 2;
            const int maxCorks = 2;

            for (int i = 0; i < corners.Count; i++)
            {
                var prevCorner = corners[(i - 1 + corners.Count) % corners.Count];

                if (prevCorner.RequiresRecoveryAfter)
                {
                    defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.RecoveryLength, width, "RecoveryStraight", locked: true));
                }

                float mainLen = rng.NextFloat(cfg.MinStraightLength, cfg.MaxStraightLength);

                // Feature roll — one feature per gap, priority: layered route > loop > corkscrew > jump > chicane/s-curve.
                // Layered groups are FORCED into the remaining gaps when the verticality preset
                // demands them (do not rely on weak random chance — spec rule).
                int gapsLeft = corners.Count - i;
                bool mustPlaceLayered = layeredPlaced < cfg.MinLayeredRouteGroups &&
                                        gapsLeft <= (cfg.MinLayeredRouteGroups - layeredPlaced);
                float layeredChance = Mathf.Max(cfg.LayeredRouteChance, cfg.RouteSplitChance);

                bool wantLayered = cfg.MaxLayeredRouteGroups > 0 && layeredPlaced < cfg.MaxLayeredRouteGroups &&
                                   (mustPlaceLayered || rng.NextFloat() < layeredChance);
                bool wantLoop = !wantLayered && loopsPlaced < maxLoops && rng.NextFloat() < cfg.LoopChance;
                bool wantCork = !wantLayered && !wantLoop && corksPlaced < maxCorks && (i - lastCorkGap) >= 2 && rng.NextFloat() < cfg.CorkscrewChance;
                bool wantSpiral = !wantLayered && !wantLoop && !wantCork && spiralsPlaced < maxSpirals && rng.NextFloat() < cfg.SpiralChance;
                bool wantJump = !wantLayered && !wantLoop && !wantCork && !wantSpiral && jumpsPlaced < maxJumps && rng.NextFloat() < cfg.JumpChance;
                bool wantSCurve = !wantLayered && !wantLoop && !wantCork && !wantSpiral && !wantJump && sCurvesPlaced < 2 && rng.NextFloat() < cfg.SCurveChance;
                bool wantChicane = !wantLayered && !wantLoop && !wantCork && !wantSpiral && !wantJump && !wantSCurve && chicanesPlaced < 2 && rng.NextFloat() < cfg.ChicaneChance;

                if (wantLayered)
                {
                    // Approach → [Route A high ∥ Route B low] → Recovery. One readable two-route
                    // group: both routes span the same split/merge frames.
                    layeredPlaced++;

                    // Long routes read as two genuinely separate roads, not a forked line.
                    float groupLen = rng.NextFloat(
                        Mathf.Lerp(cfg.MinLayeredRouteLength, cfg.MaxLayeredRouteLength, 0.4f),
                        cfg.MaxLayeredRouteLength);

                    // DECOUPLED: height separation between the routes comes from the
                    // VERTICALITY preset. Low verticality = flat side-by-side branches.
                    float sep = cfg.TargetElevationAmplitude >= 12f ? cfg.LayeredHeightSeparation : 0f;

                    // Staged split structure (sideways FIRST, then up/down) needs room for:
                    // lateral separation → vertical divergence → body → vertical convergence
                    // → lateral merge. Stretch the group to fit the resolved zone budget.
                    float zoneBudget = Mathf.Max(cfg.VerticalDivergenceDelay, cfg.LateralSeparationLength)
                                     + cfg.VerticalDivergenceLength + cfg.VerticalConvergenceLength
                                     + cfg.LateralMergeLength;
                    float minGroupLen = zoneBudget / 0.9f; // keep ≥10% of the group as level body
                    if (groupLen < minGroupLen)
                        groupLen = Mathf.Min(cfg.MaxLayeredRouteLength, Mathf.Max(groupLen, minGroupLen));

                    // Peak climb slope of the smootherstep divergence ≈ 1.875·sep/vLen —
                    // clamp separation to the legal climb angle.
                    float maxSepBySlope = cfg.VerticalDivergenceLength * Mathf.Tan(cfg.MaxClimbAngle * Mathf.Deg2Rad) / 1.875f;
                    if (sep > maxSepBySlope)
                    {
                        Debug.LogWarning($"[MacroTrackLayoutGenerator] Route split too short for requested height separation: {sep:F0}m needs a longer vertical divergence — clamped to {maxSepBySlope:F0}m.");
                        sep = maxSepBySlope;
                    }

                    bool crossing = sep > 0.01f &&
                                    cfg.OverUnderCrossingChance > 0f &&
                                    crossingsPlaced < 4 &&
                                    (rng.NextFloat() < cfg.OverUnderCrossingChance || (cfg.ForceAtLeastOneOverpass && crossingsPlaced == 0));

                    if (crossing && sep < cfg.OverpassClearance + 3f)
                    {
                        Debug.LogWarning($"[MacroTrackLayoutGenerator] Could not place over/under crossing: achievable height separation {sep:F0}m is below required clearance {cfg.OverpassClearance:F0}m — using side-by-side high/low routes instead.");
                        crossing = false;
                    }
                    if (crossing) crossingsPlaced++;

                    // Separation proportional to road width: at least a full road width of
                    // daylight between the two routes so they read as separate roads.
                    float lateral = Mathf.Max(
                        Mathf.Max(cfg.RouteLateralSeparation, cfg.RouteWidth * 0.5f + width * 0.5f + 3f),
                        width * 1.25f);

                    defs.Add(MakeStraight(TrackMacroSectionType.Straight, Mathf.Max(Mathf.Max(cfg.LayeredRouteApproachLength, cfg.RouteSplitApproachLength), mainLen * 0.35f), width, "SplitApproach", locked: true));

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.SplitRoute,
                        Length = groupLen,
                        Width = cfg.RouteWidth,
                        Direction = SectionTurnDirection.Right,
                        HillHeight = sep,
                        RouteLateralStart = lateral,
                        RouteLateralEnd = crossing ? -lateral : lateral,
                        SpeedIntent = SectionSpeedIntent.Fast,
                        RiskLevel = SectionRiskLevel.Risky,
                        LockLength = true,
                        DebugName = crossing ? $"RouteHigh_Cross_{sep:F0}m" : $"RouteHigh_{sep:F0}m"
                    });

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.SplitRoute,
                        Length = groupLen,
                        Width = cfg.RouteWidth,
                        Direction = SectionTurnDirection.Left,
                        HillHeight = 0f,
                        RouteLateralStart = -lateral,
                        RouteLateralEnd = crossing ? lateral : -lateral,
                        SpeedIntent = SectionSpeedIntent.Fast,
                        RiskLevel = SectionRiskLevel.Normal,
                        LockLength = true,
                        DebugName = crossing ? "RouteLow_Cross" : "RouteLow"
                    });

                    defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.LayeredRouteRecoveryLength, width, "RecoveryStraight", locked: true));
                }
                else if (wantLoop)
                {
                    // Approach Straight → Loop → Recovery Straight (approach length is a safety
                    // requirement, so the closure solver may not shrink it).
                    loopsPlaced++;
                    float loopRadius = rng.NextFloat(cfg.MinLoopRadius, cfg.MaxLoopRadius);

                    defs.Add(MakeStraight(TrackMacroSectionType.Straight, Mathf.Max(cfg.LoopApproachLength, mainLen * 0.4f), width, "LoopApproach", locked: true));

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.Loop,
                        // Eased (clothoid-style) curvature spends part of the arc below peak
                        // curvature, so the loop needs extra length for the MID-loop radius
                        // to stay at the requested value.
                        Length = 2f * Mathf.PI * loopRadius / (1f - LoopCurvatureEaseFraction),
                        Width = width,
                        Radius = loopRadius,
                        Direction = SectionTurnDirection.Right, // lateral exit offset side
                        PitchChange = 360f,
                        BankingAngle = 0f,
                        SpeedIntent = SectionSpeedIntent.FullThrottle,
                        RiskLevel = SectionRiskLevel.Extreme,
                        RequiresRecoveryAfter = true,
                        LockLength = true,
                        DebugName = $"Loop_R{loopRadius:F0}m"
                    });

                    defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.LoopRecoveryLength, width, "RecoveryStraight", locked: true));
                }
                else if (wantCork)
                {
                    // Approach Straight → Corkscrew → Recovery Straight.
                    corksPlaced++;
                    lastCorkGap = i;
                    float corkLen = rng.NextFloat(cfg.MinCorkscrewLength, cfg.MaxCorkscrewLength);
                    float corkRadius = rng.NextFloat(cfg.MinCorkscrewRadius, cfg.MaxCorkscrewRadius);
                    SectionTurnDirection rollDir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

                    defs.Add(MakeStraight(TrackMacroSectionType.Straight, Mathf.Max(cfg.CorkscrewApproachLength, mainLen * 0.4f), width, "CorkscrewApproach", locked: true));

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.Corkscrew,
                        Length = corkLen,
                        Width = width,
                        Radius = corkRadius,
                        Direction = rollDir,
                        RollChange = cfg.CorkscrewRollDegrees * (rollDir == SectionTurnDirection.Right ? 1f : -1f),
                        SpeedIntent = SectionSpeedIntent.Fast,
                        RiskLevel = SectionRiskLevel.Extreme,
                        RequiresRecoveryAfter = true,
                        LockLength = true,
                        DebugName = $"Corkscrew_{rollDir}_{corkLen:F0}m"
                    });

                    defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.CorkscrewRecoveryLength, width, "RecoveryStraight", locked: true));
                }
                else if (wantSpiral)
                {
                    // Parking-garage helix: full revolutions exit directly above/below the
                    // entry (zero net 2D displacement, heading unchanged) — closure-free.
                    spiralsPlaced++;

                    float spiralRadius = Mathf.Max(cfg.RoadWidth * 2f, cfg.MinCurveRadius * rng.NextFloat(0.45f, 0.75f));

                    // Coil-to-coil spacing: VerticalClearance is already wall-aware
                    // (includes the boosted half-pipe walls), plus a slab margin.
                    float climbPerRev = cfg.VerticalClearance + 8f;

                    // Spirals only CLIMB: the track baseline is ground level, so a
                    // descending spiral would drill below grade. The elevation plan
                    // brings the gained height back down on later straights instead.
                    int revs = rng.NextInt(1, 3); // 1 or 2 full revolutions
                    if (climbPerRev * revs > 240f) revs = 1;
                    SectionTurnDirection spiralDir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

                    defs.Add(MakeStraight(TrackMacroSectionType.Straight, Mathf.Max(cfg.RecoveryLength, mainLen * 0.35f), width, "SpiralApproach", locked: true));

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.Spiral,
                        Length = 2f * Mathf.PI * spiralRadius * revs,
                        Width = width,
                        Radius = spiralRadius,
                        Direction = spiralDir,
                        TurnAngle = 360f * revs,
                        BankingAngle = ComputeBankAngle(cfg, 90, false) * 0.8f,
                        ElevationChange = climbPerRev * revs,
                        SpeedIntent = SectionSpeedIntent.Medium,
                        RiskLevel = SectionRiskLevel.Normal,
                        RequiresRecoveryAfter = true,
                        LockLength = true,
                        DebugName = $"Spiral_{revs}rev_Up_{spiralDir}"
                    });

                    defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.RecoveryLength, width, "RecoveryStraight", locked: true));
                }
                else if (wantJump)
                {
                    jumpsPlaced++;

                    // Readable approach (rulebook minimum, locked) then a boost straight.
                    float approach = Mathf.Max(cfg.JumpApproachLength, mainLen * 0.35f);
                    defs.Add(MakeStraight(TrackMacroSectionType.Straight, approach, width, "JumpApproach", locked: true));

                    float boostLen = Mathf.Clamp(mainLen * 0.5f, cfg.MinBoostStraightLength, cfg.MaxBoostStraightLength);
                    var boost = MakeStraight(TrackMacroSectionType.BoostStraight, boostLen, width, "BoostStraight");
                    boost.AllowsBoost = true;
                    boost.SpeedIntent = SectionSpeedIntent.FullThrottle;
                    defs.Add(boost);

                    float rampHeight = rng.NextFloat(2.5f, Mathf.Max(3f, cfg.MaxJumpHeight));
                    float landingPeak = rampHeight * 0.85f;
                    float airGapLength = rng.NextFloat(12f, 26f);

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.JumpRamp,
                        Length = JumpRampLength,
                        Width = width,
                        ElevationChange = rampHeight,
                        PitchChange = Mathf.Rad2Deg * Mathf.Atan(2f * rampHeight / JumpRampLength),
                        SpeedIntent = SectionSpeedIntent.FullThrottle,
                        RiskLevel = SectionRiskLevel.Risky,
                        AllowsJump = true,
                        LockLength = true,
                        DebugName = $"JumpRamp_H{rampHeight:F1}m"
                    });

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.AirGap,
                        Length = airGapLength,
                        Width = width,
                        ElevationChange = landingPeak - rampHeight,
                        RiskLevel = SectionRiskLevel.Extreme,
                        LockLength = true,
                        DebugName = $"AirGap_{airGapLength:F0}m"
                    });

                    defs.Add(new TrackMacroSectionDefinition
                    {
                        SectionType = TrackMacroSectionType.LandingRamp,
                        Length = LandingRampLength,
                        Width = width,
                        ElevationChange = -landingPeak,
                        PitchChange = -Mathf.Rad2Deg * Mathf.Atan(2f * landingPeak / LandingRampLength),
                        SpeedIntent = SectionSpeedIntent.Fast,
                        RiskLevel = SectionRiskLevel.Risky,
                        LockLength = true,
                        DebugName = $"LandingRamp_H{landingPeak:F1}m"
                    });

                    defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.JumpRecoveryLength, width, "RecoveryStraight", locked: true));
                }
                else if (wantSCurve || wantChicane)
                {
                    float lead = Mathf.Max(MinAdjustableStraight, mainLen * 0.5f);
                    defs.Add(MakeStraight(TrackMacroSectionType.Straight, lead, width, $"Straight_{lead:F0}m"));

                    SectionTurnDirection firstDir = rng.NextBool() ? SectionTurnDirection.Left : SectionTurnDirection.Right;
                    float featureRadius = PickRadius(cfg, ref rng, tight: true);

                    if (wantSCurve)
                    {
                        sCurvesPlaced++;
                        float arcAngle = 45f;
                        defs.Add(new TrackMacroSectionDefinition
                        {
                            SectionType = TrackMacroSectionType.SCurve,
                            Length = 2f * Mathf.Deg2Rad * arcAngle * featureRadius,
                            Width = width,
                            Direction = firstDir,
                            TurnAngle = arcAngle,
                            Radius = featureRadius,
                            BankingAngle = ComputeBankAngle(cfg, (int)arcAngle, false) * 0.7f,
                            SpeedIntent = SectionSpeedIntent.Fast,
                            RiskLevel = SectionRiskLevel.Normal,
                            DebugName = $"SCurve_{arcAngle:F0}deg_{firstDir}First"
                        });
                    }
                    else
                    {
                        chicanesPlaced++;
                        float arcAngle = 35f;
                        float chicaneRadius = Mathf.Max(30f, featureRadius * 0.6f);
                        defs.Add(new TrackMacroSectionDefinition
                        {
                            SectionType = TrackMacroSectionType.Chicane,
                            Length = 4f * Mathf.Deg2Rad * arcAngle * chicaneRadius,
                            Width = width,
                            Direction = firstDir,
                            TurnAngle = arcAngle,
                            Radius = chicaneRadius,
                            BankingAngle = ComputeBankAngle(cfg, (int)arcAngle, false) * 0.5f,
                            SpeedIntent = SectionSpeedIntent.Medium,
                            RiskLevel = SectionRiskLevel.Risky,
                            RequiresRecoveryAfter = true,
                            DebugName = $"Chicane_{firstDir}First"
                        });
                        defs.Add(MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.RecoveryLength, width, "RecoveryStraight", locked: true));
                    }
                }
                else
                {
                    // Plain straight run: wide after hairpins, occasional boost tail before big corners.
                    bool wide = (prevCorner.SectionType == TrackMacroSectionType.BankedHairpin || rng.NextFloat() < 0.2f)
                                && i < corners.Count - 1;
                    float tailLen = Mathf.Clamp(mainLen * 0.45f, cfg.MinBoostStraightLength, cfg.MaxBoostStraightLength);
                    bool boostTail = !wide && corners[i].TurnAngle >= 90 && mainLen >= tailLen + MinAdjustableStraight && rng.NextFloat() < cfg.BoostChance;

                    if (boostTail)
                    {
                        defs.Add(MakeStraight(TrackMacroSectionType.Straight, mainLen - tailLen, width, $"Straight_{mainLen - tailLen:F0}m"));
                        var boost = MakeStraight(TrackMacroSectionType.BoostStraight, tailLen, width, "BoostStraight");
                        boost.AllowsBoost = true;
                        boost.SpeedIntent = SectionSpeedIntent.FullThrottle;
                        defs.Add(boost);
                    }
                    else if (wide)
                    {
                        defs.Add(MakeStraight(TrackMacroSectionType.WideStraight, mainLen, width * 1.5f, $"WideStraight_{mainLen:F0}m"));
                    }
                    else
                    {
                        defs.Add(MakeStraight(TrackMacroSectionType.Straight, mainLen, width, $"Straight_{mainLen:F0}m"));
                    }
                }

                // A half-loop twist launches vertically — give it a locked, readable approach.
                if (corners[i].SectionType == TrackMacroSectionType.HalfLoopTwist)
                {
                    defs.Add(MakeStraight(TrackMacroSectionType.Straight,
                        Mathf.Max(cfg.LoopApproachLength * 0.6f, 150f), width, "HalfLoopApproach", locked: true));
                }

                defs.Add(corners[i]);
            }

            return defs;
        }

        private static TrackMacroSectionDefinition MakeStraight(TrackMacroSectionType type, float length, float width, string name, bool locked = false)
        {
            return new TrackMacroSectionDefinition
            {
                SectionType = type,
                Length = length,
                Width = width,
                Direction = SectionTurnDirection.None,
                SpeedIntent = SectionSpeedIntent.Fast,
                RiskLevel = SectionRiskLevel.Safe,
                LockLength = locked,
                DebugName = name
            };
        }

        /// <summary>
        /// Mountain-pass corner planner: exactly TargetTurnCount corners with MIXED
        /// left/right directions (switchback-biased sign flips) whose SIGNED angles sum
        /// to exactly ±360° so the lap closes. Magnitudes are nudged (and signs flipped
        /// when saturated) until the residual vanishes; fully deterministic via the
        /// seeded rng. Falls back to the automatic planner if it cannot converge.
        /// </summary>
        private List<int> PlanSignedCorners(ResolvedTrackGenerationConfig cfg, int[] options, ref Unity.Mathematics.Random rng)
        {
            int count = Mathf.Max(3, cfg.TargetTurnCount);

            int minA = int.MaxValue, maxA = int.MinValue;
            foreach (int o in options) { minA = Mathf.Min(minA, o); maxA = Mathf.Max(maxA, o); }
            minA = Mathf.Clamp(minA, 15, 180);
            maxA = Mathf.Clamp(maxA, minA, 180);

            var signed = new List<int>(count);
            int prevSign = rng.NextBool() ? 1 : -1;
            for (int i = 0; i < count; i++)
            {
                int mag = Mathf.Clamp(Mathf.RoundToInt(rng.NextFloat(minA, maxA) / 5f) * 5, minA, maxA);
                int sign = rng.NextFloat() < 0.65f ? -prevSign : prevSign; // switchback bias
                prevSign = sign;
                signed.Add(sign * mag);
            }

            int sum = 0;
            foreach (int a in signed) sum += a;
            int target = sum >= 0 ? 360 : -360;

            for (int pass = 0; pass < 96 && sum != target; pass++)
            {
                int residual = target - sum;

                int idx = rng.NextInt(0, signed.Count);
                int sign = signed[idx] >= 0 ? 1 : -1;
                int mag = Mathf.Abs(signed[idx]);

                // Grow/shrink this corner toward the target in gentle steps (keeps variety).
                int step = Mathf.Clamp(residual * sign, -20, 20);
                int newMag = Mathf.Clamp(mag + step, minA, maxA);

                if (newMag == mag && pass > count * 2)
                {
                    // Magnitudes saturated: flip the corner whose flip best approaches the target.
                    int bestIdx = -1, bestErr = Mathf.Abs(residual);
                    for (int i = 0; i < signed.Count; i++)
                    {
                        int err = Mathf.Abs(target - (sum - 2 * signed[i]));
                        if (err < bestErr) { bestErr = err; bestIdx = i; }
                    }
                    if (bestIdx >= 0) { sum -= 2 * signed[bestIdx]; signed[bestIdx] = -signed[bestIdx]; }
                    continue;
                }

                sum += (newMag - mag) * sign;
                signed[idx] = sign * newMag;
            }

            if (sum != target)
            {
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Could not close {count} signed corners to ±360° (residual {target - sum}°) — using the automatic corner plan instead.");
                int dirSign = target >= 0 ? 1 : -1;
                var fallback = new List<int>();
                foreach (int a in PlanCornerAngles(options, cfg.TargetMacroSectionCount, ref rng)) fallback.Add(a * dirSign);
                return fallback;
            }

            return signed;
        }

        /// <summary>
        /// A near-180° corner realized as a half loop + half twist (Immelmann):
        /// same heading reversal as a hairpin, delivered vertically. Exits at the
        /// half-loop top height; the elevation plan pays that back elsewhere.
        /// </summary>
        private TrackMacroSectionDefinition MakeHalfLoopTwist(ResolvedTrackGenerationConfig cfg, SectionTurnDirection rollDir, float width, ref Unity.Mathematics.Random rng)
        {
            EnsureHalfLoopProfile();

            float radius = Mathf.Lerp(cfg.MinLoopRadius, cfg.MaxLoopRadius, rng.NextFloat(0.35f, 0.85f));
            float halfArc = HalfLoopArcLength(radius);

            // The roll-out is the "half corkscrew" leg: generously long so the 180°
            // roll unwinds gradually at racing speed instead of snapping — it blends
            // the inverted loop top into level flight over several seconds of travel.
            float rollOut = Mathf.Max(radius * 2.5f, 800f);

            return new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.HalfLoopTwist,
                Length = halfArc + rollOut,
                Width = width,
                Radius = radius,
                Direction = rollDir,
                TurnAngle = 180f,
                PitchChange = 180f,
                RollChange = rollDir == SectionTurnDirection.Right ? 180f : -180f,
                ElevationChange = HalfLoopTopHeight(halfArc),
                SpeedIntent = SectionSpeedIntent.FullThrottle,
                RiskLevel = SectionRiskLevel.Extreme,
                RequiresRecoveryAfter = true,
                LockLength = true,
                DebugName = $"HalfLoopTwist_R{radius:F0}m_{rollDir}"
            };
        }

        private List<int> PlanCornerAngles(int[] options, int segmentCountHint, ref Unity.Mathematics.Random rng)
        {
            bool[] composable = new bool[361];
            composable[0] = true;
            for (int v = 1; v <= 360; v++)
            {
                foreach (int o in options)
                {
                    if (o <= v && composable[v - o]) { composable[v] = true; break; }
                }
            }

            if (!composable[360])
            {
                Debug.LogWarning("[MacroTrackLayoutGenerator] Corner angle options cannot compose 360° — using default {45,60,90}.");
                return PlanCornerAngles(new[] { 45, 60, 90 }, segmentCountHint, ref rng);
            }

            int targetCorners = Mathf.Clamp(Mathf.RoundToInt(segmentCountHint * 0.4f), 3, 12);
            var angles = new List<int>();
            int remaining = 360;

            while (remaining > 0)
            {
                var valid = new List<int>();
                foreach (int o in options)
                {
                    if (o <= remaining && composable[remaining - o]) valid.Add(o);
                }

                int pick;
                if (angles.Count >= targetCorners)
                {
                    pick = valid[0];
                    foreach (int o in valid) if (o > pick) pick = o;
                }
                else
                {
                    pick = valid[rng.NextInt(0, valid.Count)];
                }

                angles.Add(pick);
                remaining -= pick;
            }

            return angles;
        }

        private static int[] SanitizeAngleOptions(float[] raw)
        {
            var list = new List<int>();
            if (raw != null)
            {
                foreach (float f in raw)
                {
                    int a = Mathf.Clamp(Mathf.RoundToInt(f), 15, 180);
                    if (!list.Contains(a)) list.Add(a);
                }
            }
            if (list.Count == 0) { list.Add(45); list.Add(60); list.Add(90); }
            return list.ToArray();
        }

        /// <summary>Corner radii are quantized to three readable values inside the resolved range.</summary>
        private float PickRadius(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng, bool isHairpin = false, bool tight = false)
        {
            float t = rng.NextInt(0, 3) * 0.5f; // 0, 0.5, 1
            float radius = Mathf.Lerp(cfg.MinCurveRadius, cfg.MaxCurveRadius, tight ? t * 0.5f : t);

            if (isHairpin) radius = Mathf.Max(30f, radius * 0.5f);
            return Mathf.Max(radius, cfg.RoadWidth * 1.5f);
        }

        private static float ComputeBankAngle(ResolvedTrackGenerationConfig cfg, int angle, bool isHairpin)
        {
            float scale = angle >= 90 ? 1f : (angle >= 60 ? 0.8f : 0.6f);
            float bank = cfg.MaxBankAngle * scale;
            if (isHairpin) bank = Mathf.Min(bank * 1.2f, cfg.MaxBankAngle);
            return bank;
        }

        private static float LoopLateralOffset(TrackMacroSectionDefinition def) => def.Width * 2f;

        private List<TrackMacroSectionDefinition> BuildTemplateSequence(ResolvedTrackGenerationConfig cfg)
        {
            float w = cfg.RoadWidth;
            float r = Mathf.Lerp(cfg.MinCurveRadius, cfg.MaxCurveRadius, 0.5f);
            float bank = ComputeBankAngle(cfg, 90, false);
            float rampHeight = 4f;
            float landingPeak = rampHeight * 0.85f;

            TrackMacroSectionDefinition Curve(string name) => new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.BankedCurve,
                Length = Mathf.Deg2Rad * 90f * r,
                Width = w,
                Direction = SectionTurnDirection.Right,
                TurnAngle = 90f,
                Radius = r,
                BankingAngle = bank,
                SpeedIntent = SectionSpeedIntent.Medium,
                DebugName = name
            };

            return new List<TrackMacroSectionDefinition>
            {
                MakeStraight(TrackMacroSectionType.Straight, 120f, w, "Straight_120m"),
                Curve("BankedCurve_90deg_A"),
                MakeStraight(TrackMacroSectionType.Straight, 180f, w, "Straight_180m"),
                MakeStraight(TrackMacroSectionType.BoostStraight, Mathf.Clamp(150f, cfg.MinBoostStraightLength, cfg.MaxBoostStraightLength), w, "BoostStraight"),
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.JumpRamp,
                    Length = JumpRampLength, Width = w, ElevationChange = rampHeight,
                    PitchChange = Mathf.Rad2Deg * Mathf.Atan(2f * rampHeight / JumpRampLength),
                    AllowsJump = true, RiskLevel = SectionRiskLevel.Risky, LockLength = true,
                    DebugName = $"JumpRamp_H{rampHeight:F1}m"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.AirGap,
                    Length = 18f, Width = w, ElevationChange = landingPeak - rampHeight,
                    RiskLevel = SectionRiskLevel.Extreme, LockLength = true, DebugName = "AirGap_18m"
                },
                new TrackMacroSectionDefinition
                {
                    SectionType = TrackMacroSectionType.LandingRamp,
                    Length = LandingRampLength, Width = w, ElevationChange = -landingPeak,
                    PitchChange = -Mathf.Rad2Deg * Mathf.Atan(2f * landingPeak / LandingRampLength),
                    RiskLevel = SectionRiskLevel.Risky, LockLength = true,
                    DebugName = $"LandingRamp_H{landingPeak:F1}m"
                },
                MakeStraight(TrackMacroSectionType.RecoveryStraight, cfg.RecoveryLength, w, "RecoveryStraight", locked: true),
                Curve("BankedCurve_90deg_B"),
                MakeStraight(TrackMacroSectionType.WideStraight, 130f, w * 1.5f, "WideStraight_130m"),
                Curve("BankedCurve_90deg_C"),
                MakeStraight(TrackMacroSectionType.Straight, 110f, w, "Straight_110m"),
                Curve("BankedCurve_90deg_D")
            };
        }

        // ──────────────────────────── 1b. Elevation plan ────────────────────────────

        /// <summary>
        /// Assigns intentional vertical content to the layout AFTER lengths are final:
        /// major climbs/drops (a level walk that must return to 0 for loop closure),
        /// then bridges, underpasses, and rolling crests/dips on the remaining straights.
        /// All elevation lives INSIDE straight sections with flat ends — every weld
        /// contract, the 2D closure solve, and the banked corners stay untouched.
        /// Not random Y noise: each change is one readable macro feature.
        /// </summary>
        private void PlanElevation(List<TrackMacroSectionDefinition> defs, ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng)
        {
            // Fixed elevation carried by geometry sections (spirals, half-loop twists)
            // must be paid back by the major climbs/drops for the lap to close flat.
            float fixedElevation = 0f;
            foreach (var d in defs)
            {
                if (d.SectionType == TrackMacroSectionType.Spiral || d.SectionType == TrackMacroSectionType.HalfLoopTwist)
                    fixedElevation += d.ElevationChange;
            }
            bool needCompensation = Mathf.Abs(fixedElevation) > 1f;

            if (cfg.TargetElevationAmplitude < 6f && !needCompensation) return; // Flat preset: 0-5 m by design

            // Carriers: plain (non-safety) straights. Approaches/recoveries stay flat so
            // loops, corkscrews, jumps and merges always launch from level ground.
            var eligible = new List<int>();
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                bool carrier = (d.SectionType == TrackMacroSectionType.Straight ||
                                d.SectionType == TrackMacroSectionType.WideStraight ||
                                d.SectionType == TrackMacroSectionType.BoostStraight)
                               && !d.LockLength && d.Length >= 60f;
                if (carrier) eligible.Add(i);
            }

            if (eligible.Count < 2)
            {
                if (needCompensation)
                    Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Spirals/half-loops carry {fixedElevation:F0}m of net elevation but no straights can pay it back — the residual pass will tilt the whole lap to close it.");
                else
                    Debug.LogWarning($"[MacroTrackLayoutGenerator] Verticality preset requested {cfg.DebugVerticalityLabel}, but no eligible straights can carry elevation — track will stay flat.");
                return;
            }

            // ── Major climbs/drops ──
            int majors = Mathf.Clamp(rng.NextInt(cfg.MinMajorElevationSections, cfg.MaxMajorElevationSections + 1), 0, eligible.Count);
            if (needCompensation) majors = Mathf.Clamp(Mathf.Max(majors, 2), 0, eligible.Count);
            var majorSet = new HashSet<int>();

            if (majors >= 2)
            {
                // Longest straights make the best climbs (slope-limited capacity); respect the
                // rulebook's minimum climb length where possible.
                var byLength = new List<int>(eligible);
                byLength.Sort((a, b) => defs[b].Length.CompareTo(defs[a].Length));
                foreach (int idx in byLength)
                {
                    if (majorSet.Count >= majors) break;
                    if (defs[idx].Length >= cfg.MinClimbLength * 0.6f) majorSet.Add(idx);
                }
                // Fall back to shorter straights only if the long ones ran out.
                foreach (int idx in byLength)
                {
                    if (majorSet.Count >= majors) break;
                    majorSet.Add(idx);
                }
                majors = majorSet.Count;

                float Capacity(int idx, bool up) =>
                    defs[idx].Length * Mathf.Tan((up ? cfg.MaxClimbAngle : cfg.MaxDropAngle) * Mathf.Deg2Rad) / 1.5f;

                // Forward pass: walk levels inside [0, amplitude].
                var order = new List<int>();
                foreach (int idx in eligible) if (majorSet.Contains(idx)) order.Add(idx);

                var deltas = new float[order.Count];

                // Seeding the walk with the fixed elevation makes the majors' deltas sum
                // to exactly -fixedElevation: the lap returns to its start height even
                // with climbing spirals / half-loop twists on it.
                float level = fixedElevation;
                for (int k = 0; k < order.Count; k++)
                {
                    float delta;
                    if (k == order.Count - 1)
                    {
                        delta = -level; // must return to base
                    }
                    else
                    {
                        float target = level < cfg.TargetElevationAmplitude * 0.55f
                            ? rng.NextFloat(0.35f, 1f) * cfg.TargetElevationAmplitude
                            : rng.NextFloat(0f, level * 0.4f);
                        delta = Mathf.Clamp(target - level, -cfg.MaxElevationStep, cfg.MaxElevationStep);
                        if (Mathf.Abs(delta) < cfg.MinElevationStep)
                            delta = (delta >= 0f ? 1f : -1f) * cfg.MinElevationStep;
                    }

                    delta = Mathf.Clamp(delta, -Capacity(order[k], false), Capacity(order[k], true));
                    deltas[k] = delta;
                    level += delta;
                }

                // Residual bleed: closure is mandatory (the lap must weld shut), so any
                // leftover level is pushed into majors that still have slope capacity.
                for (int pass = 0; pass < 3 && Mathf.Abs(level) > 0.5f; pass++)
                {
                    for (int k = order.Count - 1; k >= 0 && Mathf.Abs(level) > 0.5f; k--)
                    {
                        float cap = Mathf.Min(Capacity(order[k], true), Capacity(order[k], false));
                        float take = Mathf.Clamp(-level, -cap - deltas[k], cap - deltas[k]);
                        deltas[k] += take;
                        level += take;
                    }
                }
                if (Mathf.Abs(level) > 0.5f)
                {
                    // Force closure on the longest major and say so — a slightly over-limit
                    // slope beats a lap that cannot close.
                    Debug.LogWarning($"[MacroTrackLayoutGenerator] Elevation closure forced: {level:F1}m residual exceeded slope capacity — steepest section slightly over the comfort limit.");
                    deltas[deltas.Length - 1] -= level;
                }

                for (int k = 0; k < order.Count; k++)
                {
                    var d = defs[order[k]];
                    d.ElevationChange = deltas[k];
                    if (Mathf.Abs(deltas[k]) >= 1f)
                        d.DebugName += deltas[k] > 0f ? $"_Climb{deltas[k]:F0}m" : $"_Drop{-deltas[k]:F0}m";
                }
            }

            // ── Bridges / underpasses / rolling crests on the remaining carriers ──
            foreach (int idx in eligible)
            {
                if (majorSet.Contains(idx)) continue;
                if (rng.NextFloat() > cfg.ElevationSectionChance) continue;

                var d = defs[idx];
                float hillCap = d.Length * Mathf.Tan(cfg.MaxClimbAngle * Mathf.Deg2Rad) / 3.1f; // bump peak slope ≈ 3.04·H/L
                float roll = rng.NextFloat();

                if (roll < cfg.BridgeChance && d.Length >= 120f)
                {
                    d.SectionType = TrackMacroSectionType.BridgeVariant;
                    d.HillHeight = Mathf.Min(rng.NextFloat(cfg.MinBridgeHeight, cfg.MaxBridgeHeight), hillCap);
                    d.DebugName = $"Bridge_H{d.HillHeight:F0}m";
                }
                else if (roll < cfg.BridgeChance + cfg.UnderpassChance && d.Length >= 100f)
                {
                    d.SectionType = TrackMacroSectionType.TunnelVariant;
                    d.HillHeight = -Mathf.Min(rng.NextFloat(cfg.MinUnderpassDepth, cfg.MaxUnderpassDepth), hillCap);
                    d.DebugName = $"Underpass_D{-d.HillHeight:F0}m";
                }
                else if (roll < cfg.BridgeChance + cfg.UnderpassChance + cfg.CrestChance)
                {
                    float h = Mathf.Min(rng.NextFloat(cfg.MinRollingHillHeight, cfg.MaxRollingHillHeight), hillCap);
                    bool dip = rng.NextFloat() < 0.3f;
                    d.HillHeight = dip ? -h : h;
                    d.DebugName += dip ? $"_Dip{h:F0}m" : $"_Crest{h:F0}m";
                }
            }
        }

        // ──────────────────────────── 2. Closure: straight-length solve ────────────────────────────

        private void SolveStraightLengths(List<TrackMacroSectionDefinition> defs, ResolvedTrackGenerationConfig cfg)
        {
            Vector2 endPos = Vector2.zero;
            float heading = 0f;
            var adjustableDirs = new List<Vector2>();
            var adjustableIdx = new List<int>();

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                Vector2 fwd = HeadingToDir(heading);

                if (d.SectionType == TrackMacroSectionType.SplitRoute)
                {
                    // A split pair (Route A + Route B) spans ONE forward displacement: both
                    // routes share the same split and merge frames. Only the first of the
                    // pair advances the walk.
                    bool isSecondOfPair = i > 0 && defs[i - 1].SectionType == TrackMacroSectionType.SplitRoute;
                    if (!isSecondOfPair) endPos += fwd * d.Length;
                }
                else if (d.IsStraightFamily || d.IsJumpFamily || d.SectionType == TrackMacroSectionType.Corkscrew)
                {
                    // Corkscrews displace exactly like a straight of their axis length; the roll
                    // happens around the travel axis and returns to upright.
                    endPos += fwd * d.Length;

                    bool adjustable = !d.LockLength &&
                        (d.SectionType == TrackMacroSectionType.Straight ||
                         d.SectionType == TrackMacroSectionType.WideStraight ||
                         d.SectionType == TrackMacroSectionType.BoostStraight);

                    if (adjustable)
                    {
                        adjustableDirs.Add(fwd);
                        adjustableIdx.Add(i);
                    }
                }
                else if (d.SectionType == TrackMacroSectionType.Loop)
                {
                    // The eased-curvature loop is nearly straight at entry/exit, so unlike a
                    // perfect circle it carries a small net FORWARD displacement, plus the
                    // lateral exit offset that keeps it clear of its own entry. Heading unchanged.
                    Vector2 right = new Vector2(fwd.y, -fwd.x);
                    endPos += fwd * LoopForwardDisplacement(d.Length) + right * LoopLateralOffset(d);
                }
                else if (d.SectionType == TrackMacroSectionType.Spiral)
                {
                    // Full revolutions exit directly above/below the entry:
                    // zero net 2D displacement, heading unchanged.
                }
                else if (d.SectionType == TrackMacroSectionType.HalfLoopTwist)
                {
                    // Half loop reverses the heading; the twist roll-out then travels
                    // along the NEW heading at the top height.
                    float halfArc = HalfLoopArcLength(d.Radius);
                    endPos += fwd * HalfLoopForwardDisplacement(halfArc);
                    heading += 180f;
                    endPos += HeadingToDir(heading) * Mathf.Max(0f, d.Length - halfArc);
                }
                else if (d.SectionType == TrackMacroSectionType.SCurve)
                {
                    ApplyArc2D(ref endPos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                    ApplyArc2D(ref endPos, ref heading, -d.TurnAngle * d.TurnSign, d.Radius);
                }
                else if (d.SectionType == TrackMacroSectionType.Chicane)
                {
                    ApplyArc2D(ref endPos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                    ApplyArc2D(ref endPos, ref heading, -2f * d.TurnAngle * d.TurnSign, d.Radius);
                    ApplyArc2D(ref endPos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                }
                else // BankedCurve / BankedHairpin
                {
                    ApplyArc2D(ref endPos, ref heading, d.TurnAngle * d.TurnSign, d.Radius);
                }
            }

            Vector2 gap = -endPos;
            float maxLen = cfg.MaxStraightLength * 1.6f;

            for (int iter = 0; iter < 32 && gap.magnitude > 0.01f; iter++)
            {
                for (int a = 0; a < adjustableIdx.Count; a++)
                {
                    var def = defs[adjustableIdx[a]];
                    Vector2 dir = adjustableDirs[a];

                    float delta = Vector2.Dot(gap, dir) * 0.5f;
                    float newLen = Mathf.Clamp(def.Length + delta, MinAdjustableStraight, maxLen);
                    gap -= dir * (newLen - def.Length);
                    def.Length = newLen;
                }
            }
        }

        private static Vector2 HeadingToDir(float headingDeg)
        {
            float rad = headingDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        private static void ApplyArc2D(ref Vector2 pos, ref float heading, float signedAngleDeg, float radius)
        {
            float a = Mathf.Abs(signedAngleDeg) * Mathf.Deg2Rad;
            float side = Mathf.Sign(signedAngleDeg);

            Vector2 fwd = HeadingToDir(heading);
            Vector2 right = new Vector2(fwd.y, -fwd.x);

            pos += fwd * (radius * Mathf.Sin(a)) + right * (side * radius * (1f - Mathf.Cos(a)));
            heading += signedAngleDeg;
        }

        // ──────────────────────────── 3. Frame layout ────────────────────────────

        private List<GeneratedTrackSection> LayoutFrames(List<TrackMacroSectionDefinition> defs, ResolvedTrackGenerationConfig cfg)
        {
            var sections = new List<GeneratedTrackSection>(defs.Count);
            float density = Mathf.Max(0.5f, cfg.MeshMetersPerRing);

            TrackConnectionFrame frame = TrackConnectionFrame.Origin(defs.Count > 0 ? defs[0].Width : cfg.RoadWidth);
            TrackConnectionFrame splitEntryFrame = frame;

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var section = new GeneratedTrackSection
                {
                    Definition = def,
                    SectionIndex = i,
                    StartFrame = frame
                };

                // ── Split pair: both routes span the SAME split→merge frames ──
                if (def.SectionType == TrackMacroSectionType.SplitRoute)
                {
                    bool isSecondOfPair = i > 0 && defs[i - 1].SectionType == TrackMacroSectionType.SplitRoute;
                    if (!isSecondOfPair) splitEntryFrame = frame;

                    section.StartFrame = splitEntryFrame;
                    section.SubdivisionFrames = LayoutRoute(splitEntryFrame, def, density, cfg);
                    section.EndFrame = section.SubdivisionFrames[section.SubdivisionFrames.Length - 1];

                    // Only advance the main chain after the SECOND route — both end at the merge.
                    if (isSecondOfPair) frame = section.EndFrame;

                    def.SubdivisionCount = section.SubdivisionFrames.Length - 1;
                    section.RecalculateBounds();
                    sections.Add(section);
                    continue;
                }

                switch (def.SectionType)
                {
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                        section.SubdivisionFrames = LayoutArc(frame, def.TurnAngle * def.TurnSign, def.Radius, def.BankingAngle, density, cfg);
                        break;

                    case TrackMacroSectionType.SCurve:
                        section.SubdivisionFrames = LayoutComposedArcs(frame, def.Radius, def.BankingAngle, density, cfg,
                            new[] { def.TurnAngle * def.TurnSign, -def.TurnAngle * def.TurnSign });
                        break;

                    case TrackMacroSectionType.Chicane:
                        section.SubdivisionFrames = LayoutComposedArcs(frame, def.Radius, def.BankingAngle, density, cfg,
                            new[] { def.TurnAngle * def.TurnSign, -2f * def.TurnAngle * def.TurnSign, def.TurnAngle * def.TurnSign });
                        break;

                    case TrackMacroSectionType.JumpRamp:
                        section.SubdivisionFrames = LayoutRamp(frame, def.Length, def.ElevationChange, density, ascending: true);
                        break;

                    case TrackMacroSectionType.LandingRamp:
                        section.SubdivisionFrames = LayoutRamp(frame, def.Length, -def.ElevationChange, density, ascending: false);
                        break;

                    case TrackMacroSectionType.Loop:
                        section.SubdivisionFrames = LayoutLoop(frame, def, Mathf.Max(0.5f, cfg.LoopMetersPerRing), cfg.MaxRingFacetAngle);
                        break;

                    case TrackMacroSectionType.Corkscrew:
                        section.SubdivisionFrames = LayoutCorkscrew(frame, def, Mathf.Max(0.5f, cfg.CorkscrewMetersPerRing), cfg.MaxRingFacetAngle);
                        break;

                    case TrackMacroSectionType.Spiral:
                        section.SubdivisionFrames = LayoutSpiral(frame, def, density, cfg);
                        break;

                    case TrackMacroSectionType.HalfLoopTwist:
                        section.SubdivisionFrames = LayoutHalfLoopTwist(frame, def, Mathf.Max(0.5f, cfg.LoopMetersPerRing), cfg.MaxRingFacetAngle);
                        break;

                    case TrackMacroSectionType.AirGap:
                        section.SubdivisionFrames = new TrackConnectionFrame[0];
                        break;

                    default:
                        section.SubdivisionFrames = LayoutStraight(frame, def, density, cfg);
                        break;
                }

                // ── Boundary open/closed metadata ──
                // Any boundary that touches an AirGap is an OPEN edge: no end caps, no
                // geometry across the flight path. Normal section boundaries stay uncapped
                // too — connected meshes share identical rings, so nothing is exposed.
                switch (def.SectionType)
                {
                    case TrackMacroSectionType.JumpRamp:
                        section.OpenEnd = true;          // launch lip: nothing may cross the launch direction
                        section.ConnectsToAirGap = true;
                        break;
                    case TrackMacroSectionType.LandingRamp:
                        section.OpenStart = true;        // landing mouth: open to the incoming craft
                        section.ConnectsToAirGap = true;
                        break;
                    case TrackMacroSectionType.AirGap:
                        section.OpenStart = true;        // the gap itself has no road mesh at all
                        section.OpenEnd = true;
                        break;
                }

                if (def.SectionType == TrackMacroSectionType.AirGap)
                {
                    // Ground level is the height the JUMP started at (the track can be elevated
                    // here) — the entry frame sits at ground + lip height.
                    float lipHeight = i > 0 ? defs[i - 1].ElevationChange : 0f;
                    float groundY = frame.Position.y - lipHeight;
                    float landingPeak = i + 1 < defs.Count ? -defs[i + 1].ElevationChange : 0f;
                    Vector3 fwdH = Flatten(frame.Forward);
                    frame.Position = new Vector3(
                        frame.Position.x + fwdH.x * def.Length,
                        groundY + landingPeak,
                        frame.Position.z + fwdH.z * def.Length);
                    frame.Forward = fwdH;
                    frame.Right = Flatten(frame.Right);
                    frame.Up = Vector3.up;
                    frame.PitchAngle = 0f;
                    frame.BankAngle = 0f;
                    frame.ArcLength += def.Length;
                    section.EndFrame = frame;
                }
                else
                {
                    frame = section.SubdivisionFrames[section.SubdivisionFrames.Length - 1];
                    section.EndFrame = frame;
                }

                def.SubdivisionCount = Mathf.Max(0, (section.SubdivisionFrames?.Length ?? 1) - 1);
                section.RecalculateBounds();
                sections.Add(section);
            }

            return sections;
        }

        /// <summary>
        /// Straight-family section (incl. Bridge/Tunnel variants). Supports the elevation plan:
        /// a net elevation change (climb/drop, smoothstepped) plus a net-zero hill/dip bump.
        /// Both profiles have zero slope at the ends, so every straight still welds flat.
        /// </summary>
        private TrackConnectionFrame[] LayoutStraight(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density, ResolvedTrackGenerationConfig cfg)
        {
            float length = def.Length;
            float delta = def.IsJumpFamily ? 0f : def.ElevationChange; // jump pieces have their own layout
            float hill = def.HillHeight;

            int rings = Mathf.Clamp(Mathf.CeilToInt(length / density) + 1, 2, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            // Width changes blend over the resolved width blend length (never a snap).
            float blendLen = Mathf.Min(cfg.WidthBlendLength, length * 0.5f);

            // Pitch safety: the smoothstepped climb peaks at 1.5·delta/L, the bump at
            // ~3.04·hill/L. Warn when a section transition would exceed the legal slope.
            float peakSlope = (Mathf.Abs(delta) * 1.5f + Mathf.Abs(hill) * 3.04f) / Mathf.Max(length, 0.01f);
            float maxSlope = Mathf.Tan(Mathf.Max(cfg.MaxClimbAngle, cfg.MaxDropAngle) * Mathf.Deg2Rad);
            if (peakSlope > maxSlope * 1.05f)
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Pitch blend too steep in '{def.DebugName}': section transition creates vertical delta greater than allowed slope (peak {Mathf.Atan(peakSlope) * Mathf.Rad2Deg:F0}° > {Mathf.Atan(maxSlope) * Mathf.Rad2Deg:F0}°).");

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float s = length * u;

                float h = delta * Smooth01(u) + hill * Bump(u);

                // dh/ds — derivatives of Smooth01 (6u(1-u)) and Bump, scaled by 1/L.
                float slope = (delta * 6f * u * (1f - u) + hill * BumpDerivative(u)) / Mathf.Max(length, 0.01f);

                Vector3 fwd = (fwdH + Vector3.up * slope).normalized;
                Vector3 up = Vector3.Cross(fwd, rightH).normalized;

                float widthT = blendLen > 0.001f ? TrackBlend.Evaluate(cfg.BlendCurve, s / blendLen) : 1f;

                frames[i] = new TrackConnectionFrame
                {
                    Position = new Vector3(entry.Position.x + fwdH.x * s, entry.Position.y + h, entry.Position.z + fwdH.z * s),
                    Forward = fwd,
                    Right = rightH,
                    Up = up,
                    Width = Mathf.Lerp(entry.Width, def.Width, widthT),
                    BankAngle = 0f,
                    PitchAngle = Mathf.Rad2Deg * Mathf.Atan(slope),
                    ArcLength = entry.ArcLength + s
                };
            }

            // Exact flat exit at entry height + delta (weld contract).
            var last = frames[rings - 1];
            last.Position = new Vector3(entry.Position.x + fwdH.x * length, entry.Position.y + delta, entry.Position.z + fwdH.z * length);
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        /// <summary>
        /// One route of a two-route split group. Both routes span the same split→merge frames.
        ///
        /// STAGED STRUCTURE — the split reads as a route choice FIRST, vertical spectacle second:
        ///   Split Entry → Lateral Separation Zone → Vertical Divergence Zone →
        ///   Independent Route Body → Vertical Convergence Zone → Lateral Merge Zone.
        /// Routes move sideways apart at the same elevation; only AFTER the lateral split is
        /// readable does the high route climb / low route stay down. Before the merge, the
        /// vertical difference resolves first, then the routes come back together sideways.
        /// All profiles use easing curves with zero end derivatives, so welds stay kink-free.
        /// Opposite-signed lateral start/end makes the routes CROSS mid-body — the high route
        /// passes directly over the low route with the resolved clearance.
        /// </summary>
        private TrackConnectionFrame[] LayoutRoute(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density, ResolvedTrackGenerationConfig cfg)
        {
            float L = def.Length;
            int rings = Mathf.Clamp(Mathf.CeilToInt(L / density) + 1, 8, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            bool crossing = !Mathf.Approximately(Mathf.Sign(def.RouteLateralStart), Mathf.Sign(def.RouteLateralEnd));
            TrackBlendCurve curve = cfg.BlendCurve;

            // ── Zone budget (meters), compressed proportionally if the group is short ──
            float latSep = cfg.LateralSeparationLength;
            float latMerge = cfg.LateralMergeLength;
            float vDelay = cfg.VerticalDivergenceDelay;
            float vLen = cfg.VerticalDivergenceLength;
            float vConv = cfg.VerticalConvergenceLength;

            // KEY RULE: vertical divergence must not start until the lateral split is readable.
            if (vDelay < latSep)
            {
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Route vertical divergence would start before lateral separation completed — delaying divergence from {vDelay:F0}m to {latSep:F0}m.");
                vDelay = latSep;
            }

            float budget = vDelay + vLen + vConv + latMerge;
            if (budget > L * 0.9f)
            {
                float scale = (L * 0.9f) / budget;
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Route split too short ({L:F0}m) for the requested zone budget ({budget:F0}m) — zones compressed by {scale:P0}. Not enough route length to satisfy slope/clearance constraints comfortably.");
                latSep *= scale; latMerge *= scale; vDelay *= scale; vLen *= scale; vConv *= scale;
            }

            // Normalized zone breakpoints.
            float uLat1 = Mathf.Clamp01(latSep / L);                 // lateral separation done
            float uVD0 = Mathf.Clamp01(vDelay / L);                  // vertical divergence starts
            float uVD1 = Mathf.Clamp01(uVD0 + vLen / L);             // full height reached
            float uLM0 = Mathf.Clamp01(1f - latMerge / L);           // lateral merge starts
            float uVC1 = uLM0;                                       // vertical resolved BEFORE lateral merge
            float uVC0 = Mathf.Clamp(uVC1 - vConv / L, uVD1, uVC1);  // vertical convergence starts

            // Stash zone breakpoints for debug visualization.
            def.RouteZoneBoundaries = new[] { uLat1, uVD0, uVD1, uVC0, uVC1, uLM0 };

            // Slope safety on the divergence/convergence ramps (smootherstep peak = 1.875·H/len).
            float H = def.HillHeight;
            if (Mathf.Abs(H) > 0.01f)
            {
                float ramp = Mathf.Max(1f, Mathf.Min(uVD1 - uVD0, uVC1 - uVC0) * L);
                float peakAngle = Mathf.Atan(1.875f * Mathf.Abs(H) / ramp) * Mathf.Rad2Deg;
                float legal = H > 0f ? cfg.MaxClimbAngle : cfg.MaxDropAngle;
                if (peakAngle > legal * 1.05f)
                    Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Route split too short for requested height separation — divergence slope {peakAngle:F0}° exceeds {legal:F0}°.");
            }

            // Envelope: 0 → 1 across the lateral separation zone, 1 → 0 across the merge zone.
            float LateralEnvelope(float u)
            {
                float sepT = uLat1 > 0.0001f ? TrackBlend.Evaluate(curve, u / uLat1) : 1f;
                float mergeT = uLM0 < 0.9999f ? TrackBlend.Evaluate(curve, (1f - u) / (1f - uLM0)) : 1f;
                return Mathf.Min(sepT, mergeT);
            }

            float LateralAt(float u)
            {
                if (!crossing)
                    return def.RouteLateralStart * LateralEnvelope(u);

                // Crossing routes swap sides in the middle of the independent body,
                // while the vertical separation is at its maximum.
                float x0 = uVD1;
                float x1 = uVC0;
                float crossT = x1 > x0 ? TrackBlend.Evaluate(curve, (u - x0) / (x1 - x0)) : (u >= x0 ? 1f : 0f);
                return Mathf.Lerp(def.RouteLateralStart, def.RouteLateralEnd, crossT) * LateralEnvelope(u);
            }

            // Height: flat through the lateral separation zone, ramps up across the
            // divergence zone, holds, ramps down across the convergence zone, flat into merge.
            float HeightAt(float u)
            {
                float rise = uVD1 > uVD0 ? TrackBlend.Evaluate(curve, (u - uVD0) / (uVD1 - uVD0)) : (u >= uVD0 ? 1f : 0f);
                float fall = uVC1 > uVC0 ? TrackBlend.Evaluate(curve, (uVC1 - u) / (uVC1 - uVC0)) : (u <= uVC1 ? 1f : 0f);
                return def.HillHeight * Mathf.Min(rise, fall);
            }

            Vector3 PosAt(float u)
            {
                float h = HeightAt(u);
                float lat = LateralAt(u);
                return new Vector3(
                    entry.Position.x + fwdH.x * (L * u) + rightH.x * lat,
                    entry.Position.y + h,
                    entry.Position.z + fwdH.z * (L * u) + rightH.z * lat);
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);

                float eps = 0.25f / rings;
                Vector3 fwd = (PosAt(Mathf.Min(1f, u + eps)) - PosAt(Mathf.Max(0f, u - eps))).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 up = Vector3.Cross(fwd, right).normalized;

                // Width forks from the shared entry width to the route width and back.
                float widthT = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.15f);
                float w = Mathf.Lerp(entry.Width, def.Width, Mathf.SmoothStep(0f, 1f, widthT));

                frames[i] = new TrackConnectionFrame
                {
                    Position = PosAt(u),
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = w,
                    BankAngle = 0f,
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + L * u
                };
            }

            // Exact shared merge frame (identical for both routes of the pair).
            var last = frames[rings - 1];
            last.Position = entry.Position + fwdH * L;
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.Width = entry.Width;
            frames[rings - 1] = last;

            // Exact shared split frame.
            var first = frames[0];
            first.Position = entry.Position;
            first.Forward = fwdH;
            first.Right = rightH;
            first.Up = Vector3.up;
            first.Width = entry.Width;
            frames[0] = first;

            return frames;
        }

        private TrackConnectionFrame[] LayoutArc(TrackConnectionFrame entry, float signedAngleDeg, float radius, float bankDeg, float density, ResolvedTrackGenerationConfig cfg)
        {
            float angleAbs = Mathf.Abs(signedAngleDeg);
            float side = Mathf.Sign(signedAngleDeg);
            float arcLen = Mathf.Deg2Rad * angleAbs * radius;

            // Facet-angle bound: tight corners must not become visible/physical polygons.
            int facetRings = Mathf.CeilToInt(angleAbs / Mathf.Max(0.2f, cfg.MaxRingFacetAngle)) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(arcLen / density) + 1, facetRings), 9, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 center = entry.Position + rightH * (side * radius);
            Vector3 toStart = entry.Position - center;

            // ── BOBSLED BANKING ──
            // Corners no longer roll the road geometrically (rolling + baseline lift
            // turned wide, steeply banked corners into literal ramps — at 100 m width
            // and 75° the outside edge climbed ~90 m). Instead the FLOOR stays at
            // grade and the banking is expressed through the half-pipe cross-section:
            // the OUTSIDE wall grows taller/deeper, the INSIDE wall shrinks, and the
            // craft banks by riding up the outside wall — exactly how a bobsled track
            // corners. BankAngle on the frame is kept as informational metadata.
            float blendLen = cfg.BankBlendLength;
            float easeFrac = Mathf.Clamp(blendLen / Mathf.Max(1f, arcLen), MinBankEaseFraction, MaxBankEaseFraction);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float theta = side * angleAbs * u;

                Quaternion yaw = Quaternion.AngleAxis(theta, Vector3.up);
                Vector3 pos = center + yaw * toStart;
                Vector3 fwd = yaw * fwdH;
                Vector3 right = yaw * rightH;

                float bank = bankDeg * BankProfile(u, easeFrac, cfg.BlendCurve);
                float bank01 = Mathf.Clamp01(bank / 90f);

                // Outside wall boost / inside wall trim (negative suppression = boost).
                // Right turn (side > 0): outside is the LEFT side, and vice versa.
                float outsideBoost = -bank01 * BobsledOuterWallBoost;
                float insideTrim = bank01 * BobsledInnerWallTrim;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = right,
                    Up = Vector3.up,
                    Width = entry.Width,
                    BankAngle = side * bank,
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + arcLen * u,
                    LeftWallSuppression = side > 0f ? outsideBoost : insideTrim,
                    RightWallSuppression = side > 0f ? insideTrim : outsideBoost
                };
            }

            return frames;
        }

        private TrackConnectionFrame[] LayoutComposedArcs(TrackConnectionFrame entry, float radius, float bankDeg, float density, ResolvedTrackGenerationConfig cfg, float[] signedAngles)
        {
            var all = new List<TrackConnectionFrame>();
            TrackConnectionFrame current = entry;

            for (int a = 0; a < signedAngles.Length; a++)
            {
                TrackConnectionFrame[] arc = LayoutArc(current, signedAngles[a], radius, bankDeg, density, cfg);
                int startIdx = a == 0 ? 0 : 1;
                for (int i = startIdx; i < arc.Length; i++) all.Add(arc[i]);
                current = arc[arc.Length - 1];
            }

            return all.ToArray();
        }

        private TrackConnectionFrame[] LayoutRamp(TrackConnectionFrame entry, float length, float height, float density, bool ascending)
        {
            int rings = Mathf.Clamp(Mathf.CeilToInt(length / density) + 1, 8, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            float baseY = ascending ? entry.Position.y : entry.Position.y - height;

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float s = length * u;

                float h, slope;
                if (ascending)
                {
                    h = height * u * u;
                    slope = 2f * height * u / length;
                }
                else
                {
                    h = height * (1f - u) * (1f - u);
                    slope = -2f * height * (1f - u) / length;
                }

                Vector3 fwd = (fwdH + Vector3.up * slope).normalized;
                Vector3 up = Vector3.Cross(fwd, rightH).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = new Vector3(entry.Position.x + fwdH.x * s, baseY + h, entry.Position.z + fwdH.z * s),
                    Forward = fwd,
                    Right = rightH,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = Mathf.Rad2Deg * Mathf.Atan(slope),
                    ArcLength = entry.ArcLength + s
                };
            }

            return frames;
        }

        /// <summary>
        /// One vertical loop as ONE macro section: pitch rotates a full 360° through clean
        /// prism subdivisions with EASED curvature (clothoid-style). A perfect circle has a
        /// curvature DISCONTINUITY at entry/exit (0 → 1/R in one ring) — at racing speed
        /// that kink bumps the craft no matter how good the suspension is. Here curvature
        /// ramps 0 → 1/R → 0, so the craft transitions smoothly on and off the loop.
        /// The exit is laterally offset (two road widths to the right) so the loop never
        /// intersects its own entry — the classic offset loop. Unlike a circle, the eased
        /// loop carries a small net forward displacement (accounted for in the closure solve).
        /// </summary>
        private TrackConnectionFrame[] LayoutLoop(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density, float maxFacetAngle)
        {
            EnsureLoopProfile();

            float arcLen = def.Length;
            float lateral = LoopLateralOffset(def);

            // Facet-angle bound: the loop's bumpiness IS its facet angle, so ring
            // count must satisfy the max facet angle regardless of the configured
            // meters-per-ring. Peak pitch rate per ring is (360° / easeIntegral) /
            // rings, so rings ≥ 360 / (easeIntegral · maxFacet).
            int facetRings = Mathf.CeilToInt(360f / (Mathf.Max(0.2f, maxFacetAngle) * (1f - LoopCurvatureEaseFraction))) + 1;
            int lengthRings = Mathf.CeilToInt(arcLen / density) + 1;

            int rings = Mathf.Clamp(Mathf.Max(facetRings, lengthRings), 24, _maxRingsPerSection);
            if (rings < facetRings)
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Loop '{def.DebugName}' ring budget caps facets at {360f / ((1f - LoopCurvatureEaseFraction) * rings):F1}°/ring (target {maxFacetAngle:F1}°) — raise MaxRingsPerMacroSection for a smoother loop.");

            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            Vector3 PosAt(float u)
            {
                Vector2 pl = LoopPlaneAt(u) * arcLen;
                // Lateral offset is smoothstepped so its derivative is zero at entry/exit —
                // the loop's tangent is then exactly the flat approach direction at both ends,
                // and the weld to the neighboring straights is kink-free.
                return basePos
                     + fwdH * pl.x
                     + Vector3.up * pl.y
                     + rightH * (lateral * Smooth01(u));
            }

            // Analytic tangent of PosAt. A finite difference here is NOT good enough: its
            // one-sided error at the end rings leaves a sub-degree tilt that reads as a
            // physical bump where the loop welds back onto the road at racing speed.
            // The plane path is the integral of the unit tangent (cos θ, sin θ), so its
            // derivative is exact by construction.
            Vector3 TanAt(float u)
            {
                float theta = LoopThetaAt(u);
                return fwdH * (arcLen * Mathf.Cos(theta))
                     + Vector3.up * (arcLen * Mathf.Sin(theta))
                     + rightH * (lateral * 6f * u * (1f - u));
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float thetaDeg = LoopThetaAt(u) * Mathf.Rad2Deg;

                Vector3 fwd = TanAt(u).normalized;

                // Up rotates with pitch around the (constant) right axis; orthonormalize vs fwd.
                Vector3 up = Quaternion.AngleAxis(-thetaDeg, rightH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = PosAt(u),
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = thetaDeg,
                    ArcLength = entry.ArcLength + arcLen * u
                };
            }

            // Weld contract: the entry ring is bit-identical to the incoming frame, and the
            // exit ring is the exact flat exit pose. With analytic tangents both are already
            // correct to float precision, so these snaps close the weld without creating a
            // kink against the neighbouring rings.
            var first = frames[0];
            frames[0] = entry;
            frames[0].PitchAngle = first.PitchAngle;
            frames[0].ArcLength = first.ArcLength;

            var last = frames[rings - 1];
            last.Position = basePos + fwdH * LoopForwardDisplacement(arcLen) + rightH * lateral;
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        /// <summary>
        /// Builds the shared normalized loop profile tables. Pitch θ(u) is the cumulative
        /// integral of the eased curvature profile (normalized to exactly 360°), and the
        /// (forward, up) plane path is the cumulative integral of the unit tangent
        /// (cos θ, sin θ). The symmetric ease guarantees the loop returns to entry height;
        /// the leftover is the net forward displacement factor. Deterministic and RNG-free.
        /// </summary>
        private static void EnsureLoopProfile()
        {
            if (_loopThetaTable != null) return;
            BuildPitchProfile(2f * Mathf.PI, LoopCurvatureEaseFraction, LoopProfileSamples, out _loopThetaTable, out _loopPlaneTable);
            _loopForwardDisplacementFactor = _loopPlaneTable[LoopProfileSamples].x;
        }

        private static void EnsureHalfLoopProfile()
        {
            if (_halfLoopThetaTable != null) return;
            BuildPitchProfile(Mathf.PI, LoopCurvatureEaseFraction, LoopProfileSamples, out _halfLoopThetaTable, out _halfLoopPlaneTable);
        }

        /// <summary>
        /// Builds an eased-curvature pitch profile of the given total angle: pitch θ(u)
        /// is the cumulative integral of the eased curvature (normalized to exactly the
        /// total), and the (forward, up) plane path is the cumulative integral of the
        /// unit tangent, normalized by arc length. Shared by full loops (360°) and
        /// half-loop twists (180°). Deterministic and RNG-free.
        /// </summary>
        private static void BuildPitchProfile(float totalRadians, float easeFrac, int n, out float[] thetaTable, out Vector2[] planeTable)
        {
            var theta = new float[n + 1];
            var raw = new float[n + 1];

            for (int i = 0; i <= n; i++)
            {
                raw[i] = BankProfile((float)i / n, easeFrac, TrackBlendCurve.SmootherStep);
            }

            float acc = 0f;
            for (int i = 1; i <= n; i++)
            {
                acc += (raw[i - 1] + raw[i]) * 0.5f / n;
                theta[i] = acc;
            }

            float scale = totalRadians / theta[n];
            for (int i = 0; i <= n; i++) theta[i] *= scale;

            var plane = new Vector2[n + 1];
            Vector2 p = Vector2.zero;
            for (int i = 1; i <= n; i++)
            {
                Vector2 t0 = new Vector2(Mathf.Cos(theta[i - 1]), Mathf.Sin(theta[i - 1]));
                Vector2 t1 = new Vector2(Mathf.Cos(theta[i]), Mathf.Sin(theta[i]));
                p += (t0 + t1) * (0.5f / n);
                plane[i] = p;
            }

            thetaTable = theta;
            planeTable = plane;
        }

        private static float TableThetaAt(float[] table, float u)
        {
            float x = Mathf.Clamp01(u) * LoopProfileSamples;
            int i = Mathf.Min((int)x, LoopProfileSamples - 1);
            return Mathf.Lerp(table[i], table[i + 1], x - i);
        }

        private static Vector2 TablePlaneAt(Vector2[] table, float u)
        {
            float x = Mathf.Clamp01(u) * LoopProfileSamples;
            int i = Mathf.Min((int)x, LoopProfileSamples - 1);
            return Vector2.Lerp(table[i], table[i + 1], x - i);
        }

        /// <summary>Loop pitch angle (radians) at normalized arc position u.</summary>
        private static float LoopThetaAt(float u) => TableThetaAt(_loopThetaTable, u);

        /// <summary>Loop (forward, up) plane position at u, normalized by loop length.</summary>
        private static Vector2 LoopPlaneAt(float u) => TablePlaneAt(_loopPlaneTable, u);

        /// <summary>Arc length of the eased half loop for a given mid-arc radius.</summary>
        private static float HalfLoopArcLength(float radius)
            => Mathf.PI * radius / (1f - LoopCurvatureEaseFraction);

        /// <summary>Net forward displacement of the half loop (entry → top), from its arc length.</summary>
        private static float HalfLoopForwardDisplacement(float halfArcLength)
        {
            EnsureHalfLoopProfile();
            return _halfLoopPlaneTable[LoopProfileSamples].x * halfArcLength;
        }

        /// <summary>Top height of the half loop (the exit elevation), from its arc length.</summary>
        private static float HalfLoopTopHeight(float halfArcLength)
        {
            EnsureHalfLoopProfile();
            return _halfLoopPlaneTable[LoopProfileSamples].y * halfArcLength;
        }

        /// <summary>Net forward displacement of an eased loop of the given arc length.</summary>
        private static float LoopForwardDisplacement(float length)
        {
            EnsureLoopProfile();
            return _loopForwardDisplacementFactor * length;
        }

        /// <summary>
        /// Parking-garage spiral: full revolutions around a vertical axis while climbing
        /// or descending, exiting directly above/below the entry with the entry heading
        /// (zero net 2D displacement — closure-free by construction). Uses bobsled
        /// banking like every corner: flat floor, boosted outside wall.
        /// </summary>
        private TrackConnectionFrame[] LayoutSpiral(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density, ResolvedTrackGenerationConfig cfg)
        {
            float totalAngle = Mathf.Max(360f, def.TurnAngle);
            float side = def.TurnSign != 0 ? def.TurnSign : 1f;
            float radius = Mathf.Max(cfg.RoadWidth, def.Radius);
            float arcLen = def.Length;
            float climb = def.ElevationChange;

            int facetRings = Mathf.CeilToInt(totalAngle / Mathf.Max(0.2f, cfg.MaxRingFacetAngle)) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(arcLen / density) + 1, facetRings), 24, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 center = entry.Position + rightH * (side * radius);
            Vector3 toStart = entry.Position - center;

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float theta = side * totalAngle * u;

                Quaternion yaw = Quaternion.AngleAxis(theta, Vector3.up);
                Vector3 pos = center + yaw * toStart + Vector3.up * (climb * Smooth01(u));

                // dh/ds of the smoothstepped climb (zero at both ends → flat welds).
                float slope = climb * 6f * u * (1f - u) / Mathf.Max(arcLen, 0.01f);
                Vector3 flatFwd = yaw * fwdH;
                Vector3 fwd = (flatFwd + Vector3.up * slope).normalized;
                Vector3 right = yaw * rightH;
                Vector3 up = Vector3.Cross(fwd, right).normalized;

                float bankNow = def.BankingAngle * BankProfile(u, 0.1f, cfg.BlendCurve);
                float bank01 = Mathf.Clamp01(bankNow / 90f);
                float outsideBoost = -bank01 * BobsledOuterWallBoost;
                float insideTrim = bank01 * BobsledInnerWallTrim;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = side * bankNow,
                    PitchAngle = Mathf.Rad2Deg * Mathf.Atan(slope),
                    ArcLength = entry.ArcLength + arcLen * u,
                    LeftWallSuppression = side > 0f ? outsideBoost : insideTrim,
                    RightWallSuppression = side > 0f ? insideTrim : outsideBoost
                };
            }

            // Exact exit: directly above/below the entry, entry heading, level and unbanked.
            var last = frames[rings - 1];
            last.Position = entry.Position + Vector3.up * climb;
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            last.BankAngle = 0f;
            last.LeftWallSuppression = 0f;
            last.RightWallSuppression = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        /// <summary>
        /// Half loop + half twist (Immelmann): pitches up and over to inverted with the
        /// eased clothoid profile, then rolls 180° back to upright while flying level at
        /// the top height. Exits with the heading REVERSED — the corner plan treats it
        /// as a 180° turn; the elevation plan pays back the exit height elsewhere.
        /// </summary>
        private TrackConnectionFrame[] LayoutHalfLoopTwist(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density, float maxFacetAngle)
        {
            EnsureHalfLoopProfile();

            float halfArc = HalfLoopArcLength(def.Radius);
            float rollLen = Mathf.Max(10f, def.Length - halfArc);
            float topHeight = HalfLoopTopHeight(halfArc);
            float rollSign = def.RollChange >= 0f ? 1f : -1f;

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            float facet = Mathf.Max(0.2f, maxFacetAngle);
            int ringsA = Mathf.Clamp(
                Mathf.Max(Mathf.CeilToInt(halfArc / density), Mathf.CeilToInt(180f / ((1f - LoopCurvatureEaseFraction) * facet))) + 1,
                16, _maxRingsPerSection / 2);
            int ringsB = Mathf.Clamp(
                Mathf.Max(Mathf.CeilToInt(rollLen / density), Mathf.CeilToInt(180f * 1.5f / facet)) + 1,
                16, _maxRingsPerSection / 2);

            var frames = new TrackConnectionFrame[ringsA + ringsB - 1]; // phases share the top ring

            // ── Phase A: half loop, upright → inverted ──
            for (int i = 0; i < ringsA; i++)
            {
                float u = (float)i / (ringsA - 1);
                float theta = TableThetaAt(_halfLoopThetaTable, u); // 0..π
                Vector2 pl = TablePlaneAt(_halfLoopPlaneTable, u) * halfArc;

                Vector3 pos = basePos + fwdH * pl.x + Vector3.up * pl.y;
                Vector3 fwd = (fwdH * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
                Vector3 up = Quaternion.AngleAxis(-theta * Mathf.Rad2Deg, rightH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = theta * Mathf.Rad2Deg,
                    ArcLength = entry.ArcLength + halfArc * u
                };
            }

            // ── Phase B: level roll-out, inverted → upright, heading reversed ──
            Vector3 topPos = frames[ringsA - 1].Position;
            Vector3 exitFwd = -fwdH;

            for (int j = 1; j < ringsB; j++)
            {
                float t = (float)j / (ringsB - 1);
                float roll = 180f * Smooth01(t); // zero roll-rate at both ends → clean welds

                Vector3 up = Quaternion.AngleAxis(rollSign * roll, exitFwd) * (-Vector3.up);
                up = (up - Vector3.Dot(up, exitFwd) * exitFwd).normalized;
                Vector3 right = Vector3.Cross(up, exitFwd).normalized;

                frames[ringsA - 1 + j] = new TrackConnectionFrame
                {
                    Position = topPos + exitFwd * (rollLen * t),
                    Forward = exitFwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = rollSign * (180f - roll), // informational: remaining inversion
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + halfArc + rollLen * t
                };
            }

            // Exact exit: level, reversed heading, at the top height.
            int lastIdx = frames.Length - 1;
            var last = frames[lastIdx];
            last.Position = basePos + fwdH * HalfLoopForwardDisplacement(halfArc) + exitFwd * rollLen + Vector3.up * topHeight;
            last.Forward = exitFwd;
            last.Right = Vector3.Cross(Vector3.up, exitFwd).normalized;
            last.Up = Vector3.up;
            last.BankAngle = 0f;
            last.PitchAngle = 0f;
            frames[lastIdx] = last;

            return frames;
        }

        /// <summary>
        /// One corkscrew as ONE macro section: the road rolls gradually around the travel
        /// axis while moving forward (helix centerline), entering and exiting upright.
        /// </summary>
        private TrackConnectionFrame[] LayoutCorkscrew(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density, float maxFacetAngle)
        {
            float L = def.Length;
            float r = def.Radius;
            float rollTotal = def.RollChange; // signed

            // Facet-angle bound on the roll: smoothstepped roll peaks at 1.5× the
            // average rate, so budget rings for the PEAK roll per ring.
            int facetRings = Mathf.CeilToInt(Mathf.Abs(rollTotal) * 1.5f / Mathf.Max(0.2f, maxFacetAngle)) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(L / density) + 1, facetRings), 24, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 basePos = entry.Position;

            // Roll is smoothstepped: zero roll-rate at entry and exit means the tangent is
            // exactly the flat travel direction at both ends (kink-free welds), and the roll
            // reads as a deliberate wind-up → full twist → unwind, not an abrupt spin.
            Vector3 PosAt(float u)
            {
                float roll = rollTotal * Smooth01(u);
                Vector3 radial = Quaternion.AngleAxis(roll, fwdH) * (-Vector3.up);
                return basePos + fwdH * (L * u) + Vector3.up * r + radial * r;
            }

            // Analytic tangent of PosAt (d(radial)/du = rollRate · fwdH × radial). A finite
            // difference leaves a sub-degree tilt at the end rings that reads as a physical
            // bump where the corkscrew welds back onto the road at racing speed.
            Vector3 TanAt(float u)
            {
                float roll = rollTotal * Smooth01(u);
                Vector3 radial = Quaternion.AngleAxis(roll, fwdH) * (-Vector3.up);
                float rollRateRad = rollTotal * 6f * u * (1f - u) * Mathf.Deg2Rad;
                return fwdH * L + Vector3.Cross(fwdH, radial) * (r * rollRateRad);
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float roll = rollTotal * Smooth01(u);

                Vector3 fwd = TanAt(u).normalized;

                Vector3 up = Quaternion.AngleAxis(roll, fwdH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = PosAt(u),
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = Mathf.DeltaAngle(0f, roll),
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + L * u // axis distance; close enough for UV flow
                };
            }

            // Weld contract: entry ring bit-identical to the incoming frame, exit ring the
            // exact upright pose at entry + forward * L. With analytic tangents both are
            // already correct to float precision — no kink against the neighbouring rings.
            var first = frames[0];
            frames[0] = entry;
            frames[0].BankAngle = first.BankAngle;
            frames[0].ArcLength = first.ArcLength;

            var last = frames[rings - 1];
            last.Position = basePos + fwdH * L;
            last.Forward = fwdH;
            last.Right = Flatten(entry.Right);
            last.Up = Vector3.up;
            last.BankAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        /// <summary>Smoothstep 0→1 with zero derivative at both ends.</summary>
        private static float Smooth01(float u) => u * u * (3f - 2f * u);

        /// <summary>Net-zero bump: 0→1→0 with zero derivative at both ends. Peak at u=0.5.</summary>
        private static float Bump(float u) => 4f * Smooth01(u) * Smooth01(1f - u);

        /// <summary>
        /// Analytic derivative of <see cref="Bump"/>: d/du [4·S(u)·S(1-u)] where S'(u) = S'(1-u)
        /// = 6u(1-u), which simplifies to 24·u·(1-u)·(S(1-u) - S(u)). Peak magnitude ≈ 3.04.
        /// </summary>
        private static float BumpDerivative(float u)
        {
            return 24f * u * (1f - u) * (Smooth01(1f - u) - Smooth01(u));
        }

        /// <summary>
        /// Bank ease profile: 0 → 1 over the entry ease fraction, hold, 1 → 0 over the exit
        /// ease fraction, using the resolved blend curve (SmootherStep by default — no
        /// abrupt derivative changes at high speed).
        /// </summary>
        private static float BankProfile(float u, float easeFrac, TrackBlendCurve curve)
        {
            if (u < easeFrac) return TrackBlend.Evaluate(curve, u / easeFrac);
            if (u > 1f - easeFrac) return TrackBlend.Evaluate(curve, (1f - u) / easeFrac);
            return 1f;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        // ──────────────────────────── 4. Residual distribution ────────────────────────────

        private void DistributeResidual(List<GeneratedTrackSection> sections)
        {
            if (sections.Count == 0) return;

            var first = sections[0];
            var last = sections[sections.Count - 1];

            Vector3 residual = first.StartFrame.Position - last.EndFrame.Position;
            float totalArc = last.EndFrame.ArcLength;
            if (totalArc <= 1f) return;

            for (int s = 0; s < sections.Count; s++)
            {
                var sec = sections[s];

                var start = sec.StartFrame;
                start.Position += residual * (start.ArcLength / totalArc);
                sec.StartFrame = start;

                if (sec.SubdivisionFrames != null)
                {
                    for (int i = 0; i < sec.SubdivisionFrames.Length; i++)
                    {
                        var f = sec.SubdivisionFrames[i];
                        f.Position += residual * (f.ArcLength / totalArc);
                        sec.SubdivisionFrames[i] = f;
                    }
                }

                var end = sec.EndFrame;
                end.Position += residual * (end.ArcLength / totalArc);
                sec.EndFrame = end;

                sec.RecalculateBounds();
            }

            if (last.SubdivisionFrames != null && last.SubdivisionFrames.Length > 0)
            {
                var closing = last.SubdivisionFrames[last.SubdivisionFrames.Length - 1];
                closing.Position = first.StartFrame.Position;
                closing.Forward = first.StartFrame.Forward;
                closing.Right = first.StartFrame.Right;
                closing.Up = first.StartFrame.Up;
                closing.Width = first.StartFrame.Width;
                last.SubdivisionFrames[last.SubdivisionFrames.Length - 1] = closing;
                last.EndFrame = closing;
            }
        }

        // ──────────────────────────── 4b. Global half-pipe depth blend ────────────────────────────

        /// <summary>
        /// Assigns the half-pipe side height to EVERY frame of EVERY section — the half-pipe
        /// is the global road cross-section, not a feature. Depth varies slightly by section
        /// type (deeper channel in banked corners, shallower on wide straights) and those
        /// depth changes blend smoothly across section boundaries over the resolved
        /// cross-section blend length. Half-pipe never turns off — it only changes depth.
        ///
        /// Boundary transitions are centered on the shared boundary arc length with a width
        /// computed from BOTH neighbors, so the two sections sharing a frame compute the
        /// identical value (weld contract preserved).
        /// </summary>
        private void ApplyCrossSectionBlend(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            int n = sections.Count;
            if (n == 0 || cfg.RoadProfile == null) return;

            // Legacy FlatWithWalls debug mode: SideHeight stays 0 everywhere.
            if (!cfg.HalfPipeEnabled) return;

            float baseH = cfg.RoadProfile.SideHeight;
            float blendLen = Mathf.Max(1f, cfg.CrossSectionBlendLength);

            float SectionLen(GeneratedTrackSection s) => Mathf.Max(0.01f, s.EndFrame.ArcLength - s.StartFrame.ArcLength);

            // Neighbor lookup that skips the sibling of a split pair (both routes share
            // the same arc range, so the "previous"/"next" is outside the pair).
            int Neighbor(int i, int step)
            {
                int j = (i + step + n) % n;
                if (sections[i].Definition.SectionType == TrackMacroSectionType.SplitRoute &&
                    sections[j].Definition.SectionType == TrackMacroSectionType.SplitRoute &&
                    Mathf.Approximately(sections[j].StartFrame.ArcLength, sections[i].StartFrame.ArcLength))
                    j = (j + step + n) % n;
                return j;
            }

            for (int i = 0; i < n; i++)
            {
                var sec = sections[i];
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0) continue;

                float m = DepthMultiplier(sec.Definition.SectionType);
                int pi = Neighbor(i, -1);
                int ni = Neighbor(i, +1);
                float mPrev = DepthMultiplier(sections[pi].Definition.SectionType);
                float mNext = DepthMultiplier(sections[ni].Definition.SectionType);

                float len = SectionLen(sec);
                float w0 = Mathf.Max(0.01f, Mathf.Min(blendLen, Mathf.Min(len, SectionLen(sections[pi]))));
                float w1 = Mathf.Max(0.01f, Mathf.Min(blendLen, Mathf.Min(len, SectionLen(sections[ni]))));
                float s0 = sec.StartFrame.ArcLength;
                float s1 = sec.EndFrame.ArcLength;

                for (int f = 0; f < sec.SubdivisionFrames.Length; f++)
                {
                    var fr = sec.SubdivisionFrames[f];
                    float s = fr.ArcLength;

                    float mult = m;
                    if (s < s0 + w0 * 0.5f)
                        mult = Mathf.Lerp(mPrev, m, TrackBlend.Evaluate(cfg.BlendCurve, (s - (s0 - w0 * 0.5f)) / w0));
                    else if (s > s1 - w1 * 0.5f)
                        mult = Mathf.Lerp(m, mNext, TrackBlend.Evaluate(cfg.BlendCurve, (s - (s1 - w1 * 0.5f)) / w1));

                    fr.SideHeight = baseH * mult;
                    sec.SubdivisionFrames[f] = fr;
                }

                var sf = sec.StartFrame;
                sf.SideHeight = sec.SubdivisionFrames[0].SideHeight;
                sec.StartFrame = sf;

                var ef = sec.EndFrame;
                ef.SideHeight = sec.SubdivisionFrames[sec.SubdivisionFrames.Length - 1].SideHeight;
                sec.EndFrame = ef;
            }
        }

        /// <summary>
        /// Half-pipe depth multiplier per section type. The half-pipe NEVER turns off —
        /// every road piece keeps rideable curved walls; only the depth varies.
        /// </summary>
        private static float DepthMultiplier(TrackMacroSectionType t) => t switch
        {
            TrackMacroSectionType.BankedCurve => 1.2f,     // deeper channel supports the banked wall-ride
            TrackMacroSectionType.BankedHairpin => 1.25f,
            TrackMacroSectionType.WideStraight => 0.85f,   // wide straights are a touch shallower
            TrackMacroSectionType.JumpRamp => 0.9f,        // slightly shallower for clean lips
            TrackMacroSectionType.AirGap => 0.9f,
            TrackMacroSectionType.LandingRamp => 0.9f,     // forgiving, stylish half-pipe landing
            TrackMacroSectionType.Spiral => 1.15f,         // deeper channel holds the helix line
            TrackMacroSectionType.HalfLoopTwist => 1.1f,
            _ => 1f
        };

        // ──────────────────────────── 4c. Junction wall masks (split/merge open throats) ────────────────────────────

        /// <summary>
        /// Suppresses the INNER half-pipe walls of split-route pairs near the split and
        /// merge so the fork reads as one shared open throat instead of two full
        /// half-pipes crossing through each other.
        ///
        /// Rules:
        /// <list type="bullet">
        /// <item>Each branch keeps its OUTER wall — those continue the approach road's walls.</item>
        /// <item>The inner wall (facing the sibling route) starts fully open at the split,
        /// and may only regrow AFTER the open throat length AND once the route centerlines
        /// are separated by at least width + 2·sideHeight + margin (or stacked with vertical
        /// clearance for crossing routes).</item>
        /// <item>Before the merge the inner wall eases back down to fully open, completing
        /// before separation is lost and before the merge throat begins.</item>
        /// <item>All transitions use the resolved blend curve — the channel opens into a
        /// fork, never a sudden wall cut.</item>
        /// </list>
        /// The half-pipe never turns off: floors and outer walls stay untouched.
        /// </summary>
        private void ApplyWallMasks(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            if (!cfg.HalfPipeEnabled || cfg.RoadProfile == null) return;

            for (int i = 0; i < sections.Count - 1; i++)
            {
                if (sections[i].Definition.SectionType != TrackMacroSectionType.SplitRoute ||
                    sections[i + 1].Definition.SectionType != TrackMacroSectionType.SplitRoute)
                    continue;

                MaskSplitPair(sections[i], sections[i + 1], cfg);
                i++; // consumed the pair
            }
        }

        /// <summary>Masks the inner walls of one split-route pair (both span the same split→merge frames).</summary>
        private void MaskSplitPair(GeneratedTrackSection a, GeneratedTrackSection b, ResolvedTrackGenerationConfig cfg)
        {
            var fa = a.SubdivisionFrames;
            var fb = b.SubdivisionFrames;
            if (fa == null || fb == null || fa.Length < 2 || fb.Length < 2) return;

            int rings = Mathf.Min(fa.Length, fb.Length);
            float L = Mathf.Max(0.01f, a.EndFrame.ArcLength - a.StartFrame.ArcLength);

            // Spec rule: inner walls may only be full once the centerlines are at least
            // roadWidth + halfPipeSideHeight·2 + safetyMargin apart — otherwise the two
            // half-pipe channels physically intersect. Crossing routes are also legal
            // when stacked with the resolved vertical clearance.
            float required = Mathf.Max(a.Definition.Width, b.Definition.Width)
                           + cfg.RoadProfile.SideHeight * 2f
                           + Mathf.Max(0f, cfg.WallMaskSafetyMargin);
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);

            var separated = new bool[rings];
            for (int r = 0; r < rings; r++)
            {
                Vector3 d = fa[r].Position - fb[r].Position;
                float horizSq = d.x * d.x + d.z * d.z;
                separated[r] = horizSq >= required * required || Mathf.Abs(d.y) >= verticalOk;
            }

            int firstOk = -1, lastOk = -1;
            for (int r = 0; r < rings; r++) if (separated[r]) { firstOk = r; break; }
            for (int r = rings - 1; r >= 0; r--) if (separated[r]) { lastOk = r; break; }

            if (firstOk < 0)
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Split routes '{a.Definition.DebugName}' / '{b.Definition.DebugName}' never reach the required separation ({required:F0}m) — inner half-pipe walls stay open across the whole split.");

            float ArcOf(int r) => fa[Mathf.Clamp(r, 0, fa.Length - 1)].ArcLength - a.StartFrame.ArcLength;

            // Regrowth may start only after the open split throat AND real separation;
            // the merge fade must complete before separation is lost AND before the merge throat.
            float sGrow = firstOk >= 0 ? Mathf.Max(cfg.SplitInnerWallFadeOutLength, ArcOf(firstOk)) : float.MaxValue;
            float sFadeEnd = lastOk >= 0 ? Mathf.Min(L - cfg.MergeInnerWallFadeInLength, ArcOf(lastOk)) : float.MinValue;

            MaskRoute(a, sGrow, sFadeEnd, cfg);
            MaskRoute(b, sGrow, sFadeEnd, cfg);

            // ── Overlap audit: facing walls must never both be up while too close ──
            for (int r = 0; r < rings; r++)
            {
                if (separated[r]) continue;

                Vector3 toSibling = fb[r].Position - fa[r].Position;
                if (toSibling.sqrMagnitude < 0.25f) continue; // routes not yet diverged: shared throat, outer walls only

                float faceA = Vector3.Dot(toSibling, fa[r].Right) >= 0f ? fa[r].RightWallMultiplier : fa[r].LeftWallMultiplier;
                float faceB = Vector3.Dot(-toSibling, fb[r].Right) >= 0f ? fb[r].RightWallMultiplier : fb[r].LeftWallMultiplier;

                if (faceA > 0.55f && faceB > 0.55f)
                {
                    Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Branch inner walls overlap near split '{a.Definition.DebugName}' — inner wall regrew before route separation was sufficient (ring {r}, separation < {required:F0}m).");
                    break;
                }
            }
        }

        /// <summary>
        /// Applies the inner-wall suppression profile to one route. For crossing routes the
        /// inner side swaps mid-body: the start-inner side only obeys the split regrowth and
        /// the end-inner side only obeys the merge fade (mid-body they are vertically stacked).
        /// </summary>
        private void MaskRoute(GeneratedTrackSection route, float sGrow, float sFadeEnd, ResolvedTrackGenerationConfig cfg)
        {
            var def = route.Definition;
            var frames = route.SubdivisionFrames;

            // A route offset to the RIGHT of the group centerline faces its sibling on its LEFT.
            bool startInnerIsLeft = def.RouteLateralStart > 0f;
            bool endInnerIsLeft = def.RouteLateralEnd > 0f;
            bool crossing = startInnerIsLeft != endInnerIsLeft;

            for (int r = 0; r < frames.Length; r++)
            {
                var f = frames[r];
                float s = f.ArcLength - route.StartFrame.ArcLength;

                // 0 at the split, eases to 1 once the branches are separated…
                float grow = TrackBlend.Evaluate(cfg.BlendCurve,
                    (s - sGrow) / Mathf.Max(1f, cfg.SplitInnerWallFadeInLength));

                // …and eases back to 0 approaching the merge throat.
                float fade = TrackBlend.Evaluate(cfg.BlendCurve,
                    (sFadeEnd - s) / Mathf.Max(1f, cfg.MergeInnerWallFadeOutLength));

                if (crossing)
                {
                    SetSideSuppression(ref f, startInnerIsLeft, 1f - grow);
                    SetSideSuppression(ref f, endInnerIsLeft, 1f - fade);
                }
                else
                {
                    SetSideSuppression(ref f, startInnerIsLeft, 1f - Mathf.Min(grow, fade));
                }

                frames[r] = f;
            }

            var sf = route.StartFrame;
            sf.LeftWallSuppression = frames[0].LeftWallSuppression;
            sf.RightWallSuppression = frames[0].RightWallSuppression;
            route.StartFrame = sf;

            var ef = route.EndFrame;
            ef.LeftWallSuppression = frames[frames.Length - 1].LeftWallSuppression;
            ef.RightWallSuppression = frames[frames.Length - 1].RightWallSuppression;
            route.EndFrame = ef;
        }

        private static void SetSideSuppression(ref TrackConnectionFrame f, bool leftSide, float suppression)
        {
            suppression = Mathf.Clamp01(suppression);
            if (leftSide) f.LeftWallSuppression = Mathf.Max(f.LeftWallSuppression, suppression);
            else f.RightWallSuppression = Mathf.Max(f.RightWallSuppression, suppression);
        }

        // ──────────────────────────── 5. Validation ────────────────────────────

        /// <summary>
        /// Rejects layouts where two unrelated parts of the track come too close.
        /// Pairs separated vertically by at least the required clearance are allowed —
        /// that's a legitimate over/under (loop tops, corkscrew helices, future layered routes).
        /// </summary>
        private bool Validate(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            // Sample the driving line every ~8 m of ARC (not every Nth frame — ring
            // density varies wildly between section types, and index-based sampling
            // both over-samples dense loops and makes the O(n²) check explode).
            const float sampleStep = 8f;
            var pts = new List<Vector3>();
            var arcs = new List<float>();
            float nextSampleArc = 0f;

            foreach (var sec in sections)
            {
                if (sec.SubdivisionFrames == null) continue;
                foreach (var f in sec.SubdivisionFrames)
                {
                    if (f.ArcLength < nextSampleArc) continue;
                    pts.Add(f.Position);
                    arcs.Add(f.ArcLength);
                    nextSampleArc = f.ArcLength + sampleStep;
                }
            }

            if (pts.Count < 8) return false;

            float total = arcs[arcs.Count - 1];
            float minClearance = cfg.RoadWidth * 1.6f;
            float minClearanceSq = minClearance * minClearance;
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);

            for (int i = 0; i < pts.Count; i++)
            {
                for (int j = i + 1; j < pts.Count; j++)
                {
                    float alongDist = Mathf.Abs(arcs[j] - arcs[i]);
                    alongDist = Mathf.Min(alongDist, total - alongDist);
                    if (alongDist < minClearance * 3f) continue;

                    float dx = pts[i].x - pts[j].x;
                    float dz = pts[i].z - pts[j].z;
                    if (dx * dx + dz * dz >= minClearanceSq) continue;

                    // Horizontally close: only legal if vertically separated (over/under).
                    if (Mathf.Abs(pts[i].y - pts[j].y) < verticalOk) return false;
                }
            }

            return true;
        }

        // ──────────────────────────── 6. Debug summary (§7) ────────────────────────────

        /// <summary>
        /// Logs what was ACTUALLY generated so designer knobs can be verified against output,
        /// and warns loudly when a preset's intent was not achieved.
        /// </summary>
        private void LogTrackSummary(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            int totalRings = 0;
            int majors = 0, bridges = 0, underpasses = 0, crests = 0, loops = 0, corks = 0, jumps = 0, hairpins = 0, chicanes = 0;
            int splitRoutes = 0, crossings = 0;

            foreach (var sec in sections)
            {
                if (sec.SubdivisionFrames != null)
                {
                    totalRings += sec.SubdivisionFrames.Length;
                    foreach (var f in sec.SubdivisionFrames)
                    {
                        if (f.Position.y < minY) minY = f.Position.y;
                        if (f.Position.y > maxY) maxY = f.Position.y;
                    }
                }

                var d = sec.Definition;
                switch (d.SectionType)
                {
                    case TrackMacroSectionType.BridgeVariant: bridges++; break;
                    case TrackMacroSectionType.TunnelVariant: underpasses++; break;
                    case TrackMacroSectionType.Loop: loops++; break;
                    case TrackMacroSectionType.Corkscrew: corks++; break;
                    case TrackMacroSectionType.JumpRamp: jumps++; break;
                    case TrackMacroSectionType.BankedHairpin: hairpins++; break;
                    case TrackMacroSectionType.Chicane: chicanes++; break;
                    case TrackMacroSectionType.SplitRoute:
                        splitRoutes++;
                        if (!Mathf.Approximately(Mathf.Sign(d.RouteLateralStart), Mathf.Sign(d.RouteLateralEnd))) crossings++;
                        break;
                }

                if (d.IsStraightFamily && Mathf.Abs(d.ElevationChange) >= 1f) majors++;
                if (d.IsStraightFamily && Mathf.Abs(d.HillHeight) >= 1f &&
                    d.SectionType != TrackMacroSectionType.BridgeVariant &&
                    d.SectionType != TrackMacroSectionType.TunnelVariant) crests++;
            }

            if (minY > maxY) { minY = 0f; maxY = 0f; }
            int layeredGroups = splitRoutes / 2;
            int crossingGroups = crossings / 2;
            float heightRange = maxY - minY;

            Debug.Log(
                $"[TrackSummary] Verticality preset: {cfg.DebugVerticalityLabel}\n" +
                $"  Target elevation amplitude: {cfg.TargetElevationAmplitude:F0}m\n" +
                $"  Actual min height: {minY:F0}m | max height: {maxY:F0}m | range: {heightRange:F0}m\n" +
                $"  Major elevation sections: {majors} | crests/dips: {crests} | bridges: {bridges} | underpasses: {underpasses}\n" +
                $"  Layered route groups: {layeredGroups} (crossing: {crossingGroups})\n" +
                $"[TrackSummary] Speed profile: {cfg.DebugSpeedLabel} | jumps: {jumps} | loops: {loops} | corkscrews: {corks} | hairpins: {hairpins} | chicanes: {chicanes}\n" +
                $"[TrackSummary] Total rings: {totalRings} (budget {cfg.MaxTotalRings})");

            if (cfg.TargetElevationAmplitude >= 15f && heightRange < cfg.TargetElevationAmplitude * 0.35f)
                Debug.LogWarning($"[TrackSummary] WARNING: Verticality preset requested {cfg.DebugVerticalityLabel} (amplitude {cfg.TargetElevationAmplitude:F0}m), but actual height range was only {heightRange:F0}m.");

            if (cfg.MinLayeredRouteGroups > 0 && layeredGroups == 0)
                Debug.LogWarning("[TrackSummary] WARNING: Could not place a layered route group — check AllowRouteSplits and MaxRouteGroupsPerTrack in TrackConfig.");

            if (cfg.ForceAtLeastOneOverpass && crossingGroups == 0)
                Debug.LogWarning("[TrackSummary] WARNING: Could not place an over/under crossing — height separation vs clearance limits likely too tight in TrackConfig.");

            if (totalRings > cfg.MaxTotalRings)
                Debug.LogWarning($"[TrackSummary] WARNING: Total rings {totalRings} exceed the rulebook budget {cfg.MaxTotalRings} — consider larger MetersPerRing or a shorter track.");

            // ── Open-boundary audit: air-gap boundaries must never carry closed geometry ──
            foreach (var sec in sections)
            {
                var d = sec.Definition;
                if (d.SectionType == TrackMacroSectionType.JumpRamp && (!sec.OpenEnd || sec.CapEnd))
                    Debug.LogWarning($"[TrackSummary] WARNING: JumpRamp exit generated a blocking cap ('{d.DebugName}') — the launch lip must be an open edge.");
                if (d.SectionType == TrackMacroSectionType.LandingRamp && (!sec.OpenStart || sec.CapStart))
                    Debug.LogWarning($"[TrackSummary] WARNING: LandingRamp entry generated a blocking cap ('{d.DebugName}') — the landing mouth must be an open edge.");
                if (d.SectionType == TrackMacroSectionType.AirGap && (sec.CapStart || sec.CapEnd || !sec.OpenStart || !sec.OpenEnd))
                    Debug.LogWarning($"[TrackSummary] WARNING: AirGap boundary has closed geometry ('{d.DebugName}').");
            }

            // ── Global half-pipe audit: any road section without the half-pipe profile is
            // a bug unless the generator is intentionally in legacy/debug mode. ──
            if (cfg.HalfPipeEnabled)
            {
                foreach (var sec in sections)
                {
                    if (sec.IsEmptySpace || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0) continue;
                    foreach (var f in sec.SubdivisionFrames)
                    {
                        if (f.SideHeight <= 0.001f)
                        {
                            Debug.LogWarning($"[TrackSummary] WARNING: Generated section '{sec.Definition.DebugName}' missing half-pipe profile while not in legacy/debug mode.");
                            break;
                        }
                    }
                }
            }
        }
    }
}
