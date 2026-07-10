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
        private const int MaxAttempts = 8;
        private const float MinAdjustableStraight = 30f;
        private const float JumpRampLength = 15f;
        private const float LandingRampLength = 16f;

        // Fallback bank ease fraction when a blend length cannot be honored.
        private const float MinBankEaseFraction = 0.15f;
        private const float MaxBankEaseFraction = 0.5f;

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

                List<TrackMacroSectionDefinition> defs = BuildSequence(cfg, ref attemptRng);
                SolveStraightLengths(defs, cfg);
                PlanElevation(defs, cfg, ref attemptRng);
                List<GeneratedTrackSection> sections = LayoutFrames(defs, cfg);
                DistributeResidual(sections);
                ApplyCrossSectionBlend(sections, cfg);

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
            LogTrackSummary(fallback, cfg);
            return fallback;
        }

        // ──────────────────────────── 1. Sequence (the grammar) ────────────────────────────

        private List<TrackMacroSectionDefinition> BuildSequence(ResolvedTrackGenerationConfig cfg, ref Unity.Mathematics.Random rng)
        {
            var defs = new List<TrackMacroSectionDefinition>();
            float width = cfg.RoadWidth;

            // ── Corner plan: angles sum to exactly 360° ──
            int[] angleOptions = SanitizeAngleOptions(cfg.CornerAngleOptions);
            List<int> cornerAngles = PlanCornerAngles(angleOptions, cfg.TargetMacroSectionCount, ref rng);

            SectionTurnDirection loopDir = rng.NextBool() ? SectionTurnDirection.Right : SectionTurnDirection.Left;

            var corners = new List<TrackMacroSectionDefinition>();
            bool hairpinPlaced = false;
            foreach (int angle in cornerAngles)
            {
                bool isHairpin = !hairpinPlaced && angle >= 120 && rng.NextFloat() < cfg.HairpinChance;
                if (isHairpin) hairpinPlaced = true;

                float radius = PickRadius(cfg, ref rng, isHairpin);
                float bank = ComputeBankAngle(cfg, angle, isHairpin);

                corners.Add(new TrackMacroSectionDefinition
                {
                    SectionType = isHairpin ? TrackMacroSectionType.BankedHairpin : TrackMacroSectionType.BankedCurve,
                    Length = Mathf.Deg2Rad * angle * radius,
                    Width = width,
                    Direction = loopDir,
                    TurnAngle = angle,
                    Radius = radius,
                    BankingAngle = bank,
                    SpeedIntent = isHairpin ? SectionSpeedIntent.Slow : (angle >= 90 ? SectionSpeedIntent.Medium : SectionSpeedIntent.Fast),
                    RiskLevel = isHairpin ? SectionRiskLevel.Risky : SectionRiskLevel.Normal,
                    RequiresRecoveryAfter = isHairpin,
                    DebugName = $"{(isHairpin ? "BankedHairpin" : "BankedCurve")}_{angle}deg_{loopDir}"
                });
            }

            // ── Fill the gaps between corners ──
            int jumpsPlaced = 0, sCurvesPlaced = 0, chicanesPlaced = 0, loopsPlaced = 0, corksPlaced = 0, layeredPlaced = 0, crossingsPlaced = 0;
            int lastCorkGap = -10;
            int maxJumps = cfg.JumpChance > 0.5f ? 3 : 2;
            int maxLoops = cfg.LoopChance >= 0.5f ? 3 : 2;
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
                bool wantJump = !wantLayered && !wantLoop && !wantCork && jumpsPlaced < maxJumps && rng.NextFloat() < cfg.JumpChance;
                bool wantSCurve = !wantLayered && !wantLoop && !wantCork && !wantJump && sCurvesPlaced < 2 && rng.NextFloat() < cfg.SCurveChance;
                bool wantChicane = !wantLayered && !wantLoop && !wantCork && !wantJump && !wantSCurve && chicanesPlaced < 2 && rng.NextFloat() < cfg.ChicaneChance;

                if (wantLayered)
                {
                    // Approach → [Route A high ∥ Route B low] → Recovery. One readable two-route
                    // group: both routes span the same split/merge frames.
                    layeredPlaced++;

                    float groupLen = rng.NextFloat(cfg.MinLayeredRouteLength, cfg.MaxLayeredRouteLength);
                    float sep = cfg.LayeredHeightSeparation;

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

                    bool crossing = cfg.OverUnderCrossingChance > 0f &&
                                    crossingsPlaced < 4 &&
                                    (rng.NextFloat() < cfg.OverUnderCrossingChance || (cfg.ForceAtLeastOneOverpass && crossingsPlaced == 0));

                    if (crossing && sep < cfg.OverpassClearance + 3f)
                    {
                        Debug.LogWarning($"[MacroTrackLayoutGenerator] Could not place over/under crossing: achievable height separation {sep:F0}m is below required clearance {cfg.OverpassClearance:F0}m — using side-by-side high/low routes instead.");
                        crossing = false;
                    }
                    if (crossing) crossingsPlaced++;

                    float lateral = Mathf.Max(cfg.RouteLateralSeparation, cfg.RouteWidth * 0.5f + width * 0.5f + 3f);

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
                        Length = 2f * Mathf.PI * loopRadius,
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
            if (cfg.TargetElevationAmplitude < 6f) return; // Flat preset: 0-5 m by design

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
                Debug.LogWarning($"[MacroTrackLayoutGenerator] Verticality preset requested {cfg.DebugVerticalityLabel}, but no eligible straights can carry elevation — track will stay flat.");
                return;
            }

            // ── Major climbs/drops ──
            int majors = Mathf.Clamp(rng.NextInt(cfg.MinMajorElevationSections, cfg.MaxMajorElevationSections + 1), 0, eligible.Count);
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
                float level = 0f;
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
                    // A loop returns to its entry point but exits laterally offset (to the right)
                    // so it cannot collide with its own entry. Heading is unchanged.
                    Vector2 right = new Vector2(fwd.y, -fwd.x);
                    endPos += right * LoopLateralOffset(d);
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
                        section.SubdivisionFrames = LayoutLoop(frame, def, Mathf.Max(0.5f, cfg.LoopMetersPerRing));
                        break;

                    case TrackMacroSectionType.Corkscrew:
                        section.SubdivisionFrames = LayoutCorkscrew(frame, def, Mathf.Max(0.5f, cfg.CorkscrewMetersPerRing));
                        break;

                    case TrackMacroSectionType.AirGap:
                        section.SubdivisionFrames = new TrackConnectionFrame[0];
                        break;

                    default:
                        section.SubdivisionFrames = LayoutStraight(frame, def, density, cfg);
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

            int rings = Mathf.Clamp(Mathf.CeilToInt(arcLen / density) + 1, 9, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 center = entry.Position + rightH * (side * radius);
            Vector3 toStart = entry.Position - center;
            float halfWidth = entry.Width * 0.5f;

            // ── Bank blend length + ramp safety ──
            // Banking raises the outside edge by tan(bank)·width. If that height change
            // happens over too little track length, the bank-in behaves like a launch ramp.
            // The blend auto-expands so its effective ramp angle stays legal.
            float requestedBlend = cfg.BankBlendLength;
            float bankHeightDelta = Mathf.Tan(Mathf.Abs(bankDeg) * Mathf.Deg2Rad) * entry.Width;
            float rampPeak = TrackBlend.PeakDerivative(cfg.BlendCurve);
            float minSafeBlend = bankHeightDelta * rampPeak / Mathf.Tan(Mathf.Max(1f, cfg.MaxBankRampAngle) * Mathf.Deg2Rad);
            float blendLen = Mathf.Max(requestedBlend, minSafeBlend);
            if (minSafeBlend > requestedBlend + 0.5f)
                Debug.LogWarning($"[MacroTrackLayoutGenerator] WARNING: Bank blend too short; auto-expanded from {requestedBlend:F0}m to {blendLen:F0}m (bank {bankDeg:F0}°, width {entry.Width:F0}m).");

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
                Quaternion roll = Quaternion.AngleAxis(-side * bank, fwd);

                // BASELINE RULE: banking must never dip the inside edge below the section
                // baseline (that made mini-drops entering corners and bumps exiting them).
                // Rotating around the centerline lowers the inside edge by halfWidth·sin(bank),
                // so the whole cross-section is lifted by exactly that amount: the inside edge
                // stays at baseline and only the outside edge rises.
                float baselineLift = halfWidth * Mathf.Sin(bank * Mathf.Deg2Rad);
                pos += Vector3.up * baselineLift;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = roll * right,
                    Up = roll * Vector3.up,
                    Width = entry.Width,
                    BankAngle = side * bank,
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + arcLen * u
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
        /// prism subdivisions. The exit is laterally offset (two road widths to the right)
        /// so the loop never intersects its own entry — the classic offset loop.
        /// </summary>
        private TrackConnectionFrame[] LayoutLoop(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density)
        {
            float R = def.Radius;
            float lateral = LoopLateralOffset(def);
            float arcLen = 2f * Mathf.PI * R;

            int rings = Mathf.Clamp(Mathf.CeilToInt(arcLen / density) + 1, 24, _maxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            Vector3 PosAt(float u)
            {
                float t = u * 2f * Mathf.PI;
                // Lateral offset is smoothstepped so its derivative is zero at entry/exit —
                // the loop's tangent is then exactly the flat approach direction at both ends,
                // and the weld to the neighboring straights is kink-free.
                return basePos
                     + fwdH * (R * Mathf.Sin(t))
                     + Vector3.up * (R * (1f - Mathf.Cos(t)))
                     + rightH * (lateral * Smooth01(u));
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float thetaDeg = 360f * u;

                // Tangent by finite difference of the analytic path (robust at every pitch).
                float eps = 0.25f / rings;
                Vector3 fwd = (PosAt(Mathf.Min(1f, u + eps)) - PosAt(Mathf.Max(0f, u - eps))).normalized;

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

            // Force a clean, flat exit frame for the connection contract.
            var last = frames[rings - 1];
            last.Position = basePos + rightH * lateral;
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        /// <summary>
        /// One corkscrew as ONE macro section: the road rolls gradually around the travel
        /// axis while moving forward (helix centerline), entering and exiting upright.
        /// </summary>
        private TrackConnectionFrame[] LayoutCorkscrew(TrackConnectionFrame entry, TrackMacroSectionDefinition def, float density)
        {
            float L = def.Length;
            float r = def.Radius;
            float rollTotal = def.RollChange; // signed

            int rings = Mathf.Clamp(Mathf.CeilToInt(L / density) + 1, 24, _maxRingsPerSection);
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

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float roll = rollTotal * Smooth01(u);

                float eps = 0.25f / rings;
                Vector3 fwd = (PosAt(Mathf.Min(1f, u + eps)) - PosAt(Mathf.Max(0f, u - eps))).normalized;

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

            // Clean upright exit at exactly entry + forward * L.
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
            _ => 1f
        };

        // ──────────────────────────── 5. Validation ────────────────────────────

        /// <summary>
        /// Rejects layouts where two unrelated parts of the track come too close.
        /// Pairs separated vertically by at least the required clearance are allowed —
        /// that's a legitimate over/under (loop tops, corkscrew helices, future layered routes).
        /// </summary>
        private bool Validate(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            var pts = new List<Vector3>();
            var arcs = new List<float>();

            foreach (var sec in sections)
            {
                if (sec.SubdivisionFrames == null) continue;
                for (int i = 0; i < sec.SubdivisionFrames.Length; i += 3)
                {
                    pts.Add(sec.SubdivisionFrames[i].Position);
                    arcs.Add(sec.SubdivisionFrames[i].ArcLength);
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
