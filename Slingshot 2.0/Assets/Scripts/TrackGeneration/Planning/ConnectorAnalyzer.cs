using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Plan-time connector analysis: classifies every straight between two content
    /// sections, computes the length it actually needs to blend its neighbours'
    /// surface intent, and resolves too-short connectors by lengthening them (via a
    /// per-definition minimum the closure solver respects) or absorbing them into a
    /// turn complex whose values carry straight through.
    ///
    /// A connector is never an independent road. The classification stored on the
    /// definition is consumed by the global surface-resolution passes (banking field,
    /// turn-rounding field): a SameDirectionTurnBridge keeps its neighbours' bank,
    /// outside-wall support and rounding alive across the link; an
    /// OppositeDirectionTransfer keeps overall wall support elevated while the bank
    /// crosses zero. The minimum bank-reversal duration is enforced globally by the
    /// banking field's blur radius, not by stretching individual connectors.
    /// </summary>
    public static class ConnectorAnalyzer
    {
        /// <summary>
        /// Average wall-top slope the depth blend may demand of a connector (the
        /// validator's hard bound is 0.12 m/m; 0.10 leaves margin for the blend
        /// curve's peak and ring quantization).
        /// </summary>
        private const float MaxWallTopSlope = 0.10f;


        /// <summary>
        /// Analyzes and resolves all connectors in the plan. Runs after definition
        /// emission and BEFORE the closure solve, so expanded minimum lengths become
        /// solver constraints instead of post-hoc warps.
        /// </summary>
        public static void Analyze(TopologyPlan plan, ResolvedTrackGenerationConfig cfg)
        {
            var defs = plan.Defs;
            int n = defs.Count;
            if (n < 3) return;

            int complexCounter = 0;

            for (int i = 0; i < n; i++)
            {
                var def = defs[i];
                if (!IsConnectorCandidate(def)) continue;

                var prev = defs[(i - 1 + n) % n];
                var next = defs[(i + 1) % n];

                float original = def.Length;
                int exitSign = ExitTurnSign(prev);
                int entrySign = EntryTurnSign(next);

                // ── Required blend length: the MAX of the simultaneous property blends,
                // never their sum (they overlap in time).
                float exitBank = prev.BankingAngle * exitSign;
                float entryBank = next.BankingAngle * entrySign;
                float bankDelta01 = Mathf.Clamp01(Mathf.Abs(entryBank - exitBank) / Mathf.Max(1f, cfg.MaxBankAngle));
                float requiredBank = cfg.BankTransitionLength * bankDelta01;

                float widthDelta01 = Mathf.Clamp01(Mathf.Abs(next.Width - prev.Width) / Mathf.Max(1f, cfg.RoadWidth));
                float requiredWidth = cfg.WidthTransitionLength * widthDelta01;

                float crossDelta = Mathf.Abs(TrackCandidateBuilder.DepthMultiplier(next.SectionType)
                                           - TrackCandidateBuilder.DepthMultiplier(prev.SectionType));
                float requiredCross = cfg.CrossSectionTransitionLength * Mathf.Clamp01(crossDelta / 0.15f);

                float requiredOrientation = IsOrientationFeature(prev) ? cfg.RollTransitionLength * 0.4f : 0f;

                float required = Mathf.Max(
                    cfg.MinimumConnectorLength,
                    Mathf.Max(requiredBank, Mathf.Max(requiredWidth, Mathf.Max(requiredCross, requiredOrientation))));

                // ── Classification ──
                ConnectorBehavior behavior;
                bool absorbed = false;
                string absorbedInto = "";
                string inherited = "";
                string notes = "";

                if (IsOrientationFeature(prev))
                {
                    // Stable upright recovery outranks inheriting feature values; the
                    // banking field already feathers to zero at feature boundaries.
                    behavior = ConnectorBehavior.OrientationRecovery;
                }
                else if (exitSign != 0 && entrySign != 0 &&
                         BridgeCarryFor(original, cfg.SameDirectionBridgeLength) > 0.01f)
                {
                    // GRADED turn complex: full carry up to the bridge window, fading
                    // smoothly to zero by ~2.5× it. A binary threshold made a 795m link
                    // collapse completely while a 793m one held perfectly — the cliff
                    // itself read as a wavy wall on layouts whose straights cluster
                    // near the window.
                    float carry = BridgeCarryFor(original, cfg.SameDirectionBridgeLength);
                    def.BridgeCarry = carry;

                    if (exitSign == entrySign)
                    {
                        behavior = ConnectorBehavior.SameDirectionTurnBridge;
                        inherited = "bank, outside wall support, depth, turn rounding";
                        if (cfg.AllowConnectorAbsorption && original < cfg.SameDirectionBridgeLength)
                        {
                            absorbed = true;
                            string complexId = $"TurnComplex_{complexCounter++}";
                            absorbedInto = complexId;
                            prev.TurnComplexId = complexId;
                            def.TurnComplexId = complexId;
                            next.TurnComplexId = complexId;
                            notes = "treated as one turn complex — no reset between the turns";
                        }
                        else
                        {
                            notes = $"graded carry {carry:F2} (longer than the bridge window — partial relax)";
                        }
                    }
                    else
                    {
                        behavior = ConnectorBehavior.OppositeDirectionTransfer;
                        inherited = "overall wall support (emphasis hands over sides)";
                        notes = $"bank crosses zero at the field's minimum reversal duration; carry {carry:F2}";
                        if (cfg.AllowConnectorExpansion && !def.LockLength && original < cfg.MinimumConnectorLength)
                        {
                            def.MinimumLength = cfg.MinimumConnectorLength;
                            def.Length = Mathf.Max(def.Length, def.MinimumLength);
                        }
                    }
                }
                else if (IsFeature(next))
                {
                    behavior = ConnectorBehavior.PrepareNext;
                }
                else if (exitSign == 0 && entrySign == 0 && original >= required * 2f)
                {
                    behavior = ConnectorBehavior.NeutralTransition;
                }
                else
                {
                    behavior = ConnectorBehavior.BlendNeighbours;
                }

                // ── Anti-crush FLOOR (non-bridge): the closure solver may later shrink
                // any adjustable straight, so carry the WALL-SLOPE need as a
                // MinimumLength floor. (The solver crushing 1.8 km connectors to its
                // bare 40 m floor is what squeezed the half-pipe depth blend into a
                // visible wall wave at corner entries.) The floor is the PHYSICAL bound
                // only — wall-top rise per meter — never the seconds-based blend
                // lengths: flooring every connector at those locks tens of km at
                // hovercraft scale and starves the solver (budget + closure failures
                // across every preset). Bank needs no floor here — the banking field's
                // blur radius enforces its minimum duration globally. Bridges
                // deliberately stay short — their carry holds the neighbors' values
                // through the link.
                if (!absorbed && behavior != ConnectorBehavior.SameDirectionTurnBridge && !def.LockLength)
                {
                    float mConn = TrackCandidateBuilder.DepthMultiplier(def.SectionType);
                    float depthStep = Mathf.Max(
                        Mathf.Abs(mConn - TrackCandidateBuilder.DepthMultiplier(prev.SectionType)),
                        Mathf.Abs(TrackCandidateBuilder.DepthMultiplier(next.SectionType) - mConn));
                    float sideH = cfg.RoadProfile != null ? cfg.RoadProfile.SideHeight : 0f;
                    // Peak blend slope ≈ 1.5× the average; boundary-window overlap and
                    // ring quantization roughly double that again → ×3 on the average.
                    // (No BANK floor here: reversal rate is owned by the banking
                    // field's blur-radius floor — length floors for it locked
                    // kilometers at hovercraft bank angles and starved the closure
                    // solver; measured Balanced 90%→78%.)
                    float waveFloor = depthStep * sideH * 3f / MaxWallTopSlope;
                    if (IsOrientationFeature(prev)) waveFloor = Mathf.Max(waveFloor, requiredOrientation);

                    def.MinimumLength = Mathf.Max(def.MinimumLength, waveFloor);

                    if (cfg.AllowConnectorExpansion && def.Length < Mathf.Max(required, def.MinimumLength) &&
                        (exitSign != 0 || entrySign != 0 || IsFeature(next) || IsOrientationFeature(prev)))
                    {
                        // Expanded starting point for the full blend; the solver may
                        // still trade it back down to the physical floor.
                        def.Length = Mathf.Max(required, def.MinimumLength);
                        if (def.Length > original) notes = AppendNote(notes, "lengthened to blend requirement");
                    }
                }
                else if (!absorbed && original < required && (def.LockLength || !cfg.AllowConnectorExpansion))
                {
                    notes = AppendNote(notes, "kept short (locked/expansion disabled) — dominant values preserved by the field");
                }

                def.ConnectorBehavior = behavior;

                // ── Record non-trivial decisions ──
                bool trivial = behavior == ConnectorBehavior.NeutralTransition ||
                               (behavior == ConnectorBehavior.BlendNeighbours &&
                                Mathf.Approximately(def.Length, original));
                if (!trivial)
                {
                    plan.ConnectorDecisions.Add(new ConnectorDecisionRecord
                    {
                        Name = string.IsNullOrEmpty(def.DebugName) ? $"Connector_{i:D2}" : def.DebugName,
                        Behavior = behavior,
                        OriginalLength = original,
                        ResolvedLength = def.Length,
                        RequiredLength = required,
                        Absorbed = absorbed,
                        AbsorbedInto = absorbedInto,
                        InheritedProperties = inherited,
                        Notes = notes
                    });
                }
            }
        }

        /// <summary>
        /// Classifies the locked connector links in a fitted alternate-road chain.
        /// Alternate roads are created after <see cref="Analyze"/>, so without this
        /// focused pass their turn-to-turn straights retain the enum default
        /// NeutralTransition and the surface fields visibly drop wall support through
        /// even a solver-minimum link. Lengths are deliberately not changed here: the
        /// chain has already been solved onto its two gate anchors.
        /// </summary>
        public static void AnalyzeFittedChain(List<TrackMacroSectionDefinition> chain,
            ResolvedTrackGenerationConfig cfg)
        {
            if (chain == null || chain.Count < 3) return;

            for (int i = 1; i < chain.Count - 1; i++)
            {
                var def = chain[i];
                if (!IsConnectorCandidate(def)) continue;

                int exitSign = ExitTurnSign(chain[i - 1]);
                int entrySign = EntryTurnSign(chain[i + 1]);
                if (exitSign == 0 || entrySign == 0)
                    continue;

                float carry = BridgeCarryFor(def.Length, cfg.SameDirectionBridgeLength);
                if (carry <= 0.01f) continue;

                def.BridgeCarry = carry;
                def.ConnectorBehavior = exitSign == entrySign
                    ? ConnectorBehavior.SameDirectionTurnBridge
                    : ConnectorBehavior.OppositeDirectionTransfer;
            }
        }

        private static string AppendNote(string notes, string add)
            => string.IsNullOrEmpty(notes) ? add : notes + "; " + add;

        /// <summary>
        /// Graded carry: 1 up to the bridge window, smoothstep-fading to 0 at 2.5× it.
        /// The surface passes multiply their turn-complex fills by this, so support
        /// relaxes PROPORTIONALLY with connector length instead of cliff-dropping.
        /// </summary>
        public static float BridgeCarryFor(float connectorLength, float bridgeWindow)
        {
            if (bridgeWindow <= 1f) return 0f;
            if (connectorLength <= bridgeWindow) return 1f;
            float t = Mathf.Clamp01((connectorLength - bridgeWindow) / (bridgeWindow * 1.5f));
            return 1f - (t * t * (3f - 2f * t));
        }

        /// <summary>Straight-family sections that sit between content and may act as connectors.</summary>
        private static bool IsConnectorCandidate(TrackMacroSectionDefinition def)
        {
            switch (def.SectionType)
            {
                case TrackMacroSectionType.Straight:
                case TrackMacroSectionType.WideStraight:
                case TrackMacroSectionType.BoostStraight:
                case TrackMacroSectionType.RecoveryStraight:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Signed direction the previous section EXITS with (0 for non-corners).</summary>
        private static int ExitTurnSign(TrackMacroSectionDefinition def)
        {
            switch (def.SectionType)
            {
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                case TrackMacroSectionType.Chicane: // [+a, −2a, +a] ends turning back toward the entry sign
                    return def.TurnSign;
                case TrackMacroSectionType.SCurve:  // [+a, −a] ends on the opposite sign
                    return -def.TurnSign;
                default:
                    return 0;
            }
        }

        /// <summary>Signed direction the next section ENTERS with (0 for non-corners).</summary>
        private static int EntryTurnSign(TrackMacroSectionDefinition def)
        {
            switch (def.SectionType)
            {
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                case TrackMacroSectionType.SCurve:
                case TrackMacroSectionType.Chicane:
                    return def.TurnSign;
                default:
                    return 0;
            }
        }

        /// <summary>Sections that change the craft's orientation and demand stable recovery after them.</summary>
        private static bool IsOrientationFeature(TrackMacroSectionDefinition def)
        {
            switch (def.SectionType)
            {
                case TrackMacroSectionType.Loop:
                case TrackMacroSectionType.Corkscrew:
                case TrackMacroSectionType.HalfLoopTwist:
                case TrackMacroSectionType.LandingRamp:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Feature sections a connector may need to prepare the road for.</summary>
        private static bool IsFeature(TrackMacroSectionDefinition def)
        {
            switch (def.SectionType)
            {
                case TrackMacroSectionType.Loop:
                case TrackMacroSectionType.Corkscrew:
                case TrackMacroSectionType.Spiral:
                case TrackMacroSectionType.HalfLoopTwist:
                case TrackMacroSectionType.JumpRamp:
                    return true;
                default:
                    return false;
            }
        }
    }
}
