using System.Collections.Generic;
using TrackGeneration.Definitions;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Topology role of a semantic element (Stage B of the dynamic-topology plan).</summary>
    public enum TopologyRole
    {
        HeadingNeutral = 0,
        TurnRealization = 1,
        DirectionNeutralTransfer = 2,
        CompoundTurnRealization = 3,
        OrientationTransition = 4,
        RecoveryRealization = 5,
    }

    /// <summary>
    /// STATIC capability declaration for one semantic element: what entry states it
    /// truthfully accepts and what obligations it carries. Never contains generated
    /// data (that is <see cref="FeaturePlanResult"/>'s job).
    ///
    /// Stage B scope: these contracts exist, are honest to the §8 audit, and are
    /// validated by the contract fuzz tests. Nothing CONSUMES them to change planning
    /// yet — the Stage C transition resolver is their first consumer.
    ///
    /// Entry windows are initial conservative declarations. The fuzz tests build real
    /// frames at the window edges and assert sane geometry; widen a window only with
    /// a passing fuzz case proving the builder tolerates it.
    /// </summary>
    public sealed class FeatureCapability
    {
        public SemanticElementId Element;
        public TopologyRole Role;

        /// <summary>Physical upright-entry requirement (inversion entries, pipe morphs). Planner-convenience upright is FALSE here.</summary>
        public bool RequiresUprightEntry;

        // Accepted entry windows (degrees / per-meter). The builder must produce sane
        // geometry anywhere inside them — asserted by FeatureContractTests.
        public float MaxEntryBankDeg;
        public float MaxEntryPitchDeg;
        public float MaxEntryHorizontalCurvature;   // 1/m
        public float MaxEntryRollRateDegPerM;

        public bool AcceptsCurvedEntry;

        /// <summary>Physically requires recovery after (jump landings, wallride/hairpin exits).</summary>
        public bool RequiresRecoveryAfter;

        /// <summary>Emits its own approach/recovery sections inside the pattern (jumps, spiral).</summary>
        public bool EmitsOwnApproach, EmitsOwnRecovery;

        public string Notes = "";
    }

    /// <summary>Registry of Stage B capability declarations for every current realization.</summary>
    public static class FeatureCapabilities
    {
        public static bool TryGet(SemanticElementId element, out FeatureCapability capability)
        {
            if (Table.TryGetValue(element, out capability)) return true;
            if (!TrackFeatureDefinitionCatalog.TryGet(element, out TrackFeatureDefinition definition) ||
                !definition.Enabled)
                return false;
            capability = FromDefinition(definition);
            return true;
        }

        public static FeatureCapability Get(SemanticElementId element)
            => TryGet(element, out FeatureCapability capability) ? capability : null;

        public static IEnumerable<FeatureCapability> All
        {
            get
            {
                foreach (FeatureCapability capability in Table.Values) yield return capability;
                foreach (TrackFeatureDefinition definition in TrackFeatureDefinitionCatalog.All)
                {
                    if (definition.Enabled && !Table.ContainsKey(definition.SemanticElement))
                        yield return FromDefinition(definition);
                }
            }
        }

        private static readonly Dictionary<SemanticElementId, FeatureCapability> Table = Build();

        private static Dictionary<SemanticElementId, FeatureCapability> Build()
        {
            var t = new Dictionary<SemanticElementId, FeatureCapability>();
            void Add(FeatureCapability c) => t[c.Element] = c;

            // ── Corner realizations: field-driven surfaces, no orientation demands.
            // Their eased arcs start/end at κ≈0 by construction; entry bank is
            // whatever the banking field blended to — generous windows.
            FeatureCapability Corner(SemanticElementId id, bool recovery = false, string notes = "") =>
                new FeatureCapability
                {
                    Element = id, Role = TopologyRole.TurnRealization,
                    RequiresUprightEntry = false,
                    MaxEntryBankDeg = 35f, MaxEntryPitchDeg = 25f,
                    MaxEntryHorizontalCurvature = 1f / 400f, MaxEntryRollRateDegPerM = 0.10f,
                    AcceptsCurvedEntry = true, RequiresRecoveryAfter = recovery, Notes = notes
                };
            Add(Corner(SemanticElementId.OrdinaryCurve));
            Add(Corner(SemanticElementId.DoubleApex));
            Add(Corner(SemanticElementId.TighteningCorner));
            Add(Corner(SemanticElementId.OpeningCorner));
            Add(Corner(SemanticElementId.Hairpin, recovery: true, notes: "reversal at speed — physical recovery"));
            Add(Corner(SemanticElementId.SweeperIntoHairpin, recovery: true,
                notes: "VERIFIED Stage B: single-demand recipe; adjacent sweeper slot NOT absorbed (selection heuristic only)"));
            Add(Corner(SemanticElementId.WallrideTurn, recovery: true, notes: "extreme-bank exit — physical recovery"));
            // ── Transfers ──
            FeatureCapability Transfer(SemanticElementId id) => new FeatureCapability
            {
                Element = id, Role = TopologyRole.DirectionNeutralTransfer,
                RequiresUprightEntry = false,
                MaxEntryBankDeg = 35f, MaxEntryPitchDeg = 25f,
                MaxEntryHorizontalCurvature = 1f / 400f, MaxEntryRollRateDegPerM = 0.10f,
                AcceptsCurvedEntry = true
            };
            Add(Transfer(SemanticElementId.SCurve));
            Add(Transfer(SemanticElementId.Chicane));
            // The authored pattern is two S-curves separated by a link and has
            // net-zero heading. It is a transfer, not a corner realization.
            Add(Transfer(SemanticElementId.AlternatingRadiusSequence));
            Add(Transfer(SemanticElementId.ClosureTransfer));
            Add(Transfer(SemanticElementId.QuarterTransfer));

            // ── Inversion family: upright entry is PHYSICAL (the roll/pitch program
            // integrates from the entry basis; a rolled entry starts the inversion
            // mid-orientation). Small windows; endpoint corrections blend rates.
            FeatureCapability Inversion(SemanticElementId id, TopologyRole role, string notes = "") =>
                new FeatureCapability
                {
                    Element = id, Role = role,
                    RequiresUprightEntry = true,
                    MaxEntryBankDeg = 8f, MaxEntryPitchDeg = 12f,
                    MaxEntryHorizontalCurvature = 1f / 1200f, MaxEntryRollRateDegPerM = 0.05f,
                    AcceptsCurvedEntry = true, Notes = notes
                };
            Add(Inversion(SemanticElementId.VerticalLoop, TopologyRole.HeadingNeutral));
            Add(Inversion(SemanticElementId.Immelmann, TopologyRole.TurnRealization,
                "±180 — the existing IsHalfLoop turn realization"));
            Add(Inversion(SemanticElementId.HalfLoopToCorkscrew, TopologyRole.TurnRealization, "±180 variant"));
            Add(Inversion(SemanticElementId.LoopToCorkscrew, TopologyRole.HeadingNeutral));
            Add(Inversion(SemanticElementId.SpiralToCorkscrew, TopologyRole.HeadingNeutral));

            // ── Inline corkscrew: the audit's best-prepared feature. Entry-relative
            // basis preserves entry roll/pitch; EndpointRateCorrection blends entry
            // curvature and roll rate. Contract is 'entry Any' in current project code.
            Add(new FeatureCapability
            {
                Element = SemanticElementId.InlineCorkscrew, Role = TopologyRole.HeadingNeutral,
                RequiresUprightEntry = false,
                MaxEntryBankDeg = 25f, MaxEntryPitchDeg = 20f,
                MaxEntryHorizontalCurvature = 1f / 800f, MaxEntryRollRateDegPerM = 0.08f,
                AcceptsCurvedEntry = true,
                Notes = "Stage C/D adjacency pilot"
            });
            Add(new FeatureCapability
            {
                Element = SemanticElementId.DoubleCorkscrew, Role = TopologyRole.HeadingNeutral,
                RequiresUprightEntry = false,
                MaxEntryBankDeg = 25f, MaxEntryPitchDeg = 20f,
                MaxEntryHorizontalCurvature = 1f / 800f, MaxEntryRollRateDegPerM = 0.08f,
                AcceptsCurvedEntry = true
            });

            // ── Stage F: directional corkscrew. Same barrel physics as the inline
            // corkscrew (identical entry window), but its axis also turns by a ±45/±90
            // token, so it is a TurnRealization that CONSUMES a turn demand. Its plan-view
            // is the barrel's stamped eased arc — closure agrees with the built geometry by
            // construction (StampRotationalEventPlan derives both from one frame walk).
            Add(new FeatureCapability
            {
                Element = SemanticElementId.DirectionalCorkscrew, Role = TopologyRole.TurnRealization,
                RequiresUprightEntry = false,
                MaxEntryBankDeg = 25f, MaxEntryPitchDeg = 20f,
                MaxEntryHorizontalCurvature = 1f / 800f, MaxEntryRollRateDegPerM = 0.08f,
                AcceptsCurvedEntry = true,
                Notes = "Stage F foundation: geometry via MakeCorkscrewDef(horizontalTurn); live selection pending in-engine craft-retention tuning"
            });

            // ── Spiral: upright today only because its own approach straight enforces
            // it (planner convenience, NOT physics) — declared honestly.
            Add(new FeatureCapability
            {
                Element = SemanticElementId.Spiral, Role = TopologyRole.HeadingNeutral,
                RequiresUprightEntry = false,
                MaxEntryBankDeg = 15f, MaxEntryPitchDeg = 15f,
                MaxEntryHorizontalCurvature = 1f / 800f, MaxEntryRollRateDegPerM = 0.05f,
                AcceptsCurvedEntry = true,
                EmitsOwnApproach = true, EmitsOwnRecovery = true,
                Notes = "approach straight is planner convenience — reclassify to AdaptiveBlend at Stage D"
            });

            // ── Jumps: level launch is PHYSICAL (ballistics solved from a level lip);
            // approach/recovery straights are physically justified.
            FeatureCapability Jump(SemanticElementId id) => new FeatureCapability
            {
                Element = id, Role = TopologyRole.HeadingNeutral,
                RequiresUprightEntry = true,
                MaxEntryBankDeg = 5f, MaxEntryPitchDeg = 5f,
                MaxEntryHorizontalCurvature = 1f / 2000f, MaxEntryRollRateDegPerM = 0.02f,
                AcceptsCurvedEntry = false,
                RequiresRecoveryAfter = true, EmitsOwnApproach = true, EmitsOwnRecovery = true,
                Notes = "boost run-up + landing recovery are physical at design speed"
            };
            Add(Jump(SemanticElementId.JumpGap));
            Add(Jump(SemanticElementId.JumpToBankedLanding));

            // ── Full pipe: upright is physical — the cross-section closure morph is
            // authored from an upright open profile.
            Add(new FeatureCapability
            {
                Element = SemanticElementId.FullPipe, Role = TopologyRole.HeadingNeutral,
                RequiresUprightEntry = true,
                MaxEntryBankDeg = 5f, MaxEntryPitchDeg = 15f,
                MaxEntryHorizontalCurvature = 1f / 1200f, MaxEntryRollRateDegPerM = 0.03f,
                AcceptsCurvedEntry = true
            });

            // ── Structural ──
            Add(new FeatureCapability
            {
                Element = SemanticElementId.RecoverySection, Role = TopologyRole.RecoveryRealization,
                RequiresUprightEntry = false,
                MaxEntryBankDeg = 60f, MaxEntryPitchDeg = 30f,
                MaxEntryHorizontalCurvature = 1f / 400f, MaxEntryRollRateDegPerM = 0.15f,
                AcceptsCurvedEntry = true,
                Notes = "exists to absorb whatever the preceding feature exits with"
            });

            return t;
        }

        private static FeatureCapability FromDefinition(TrackFeatureDefinition definition) =>
            new FeatureCapability
            {
                Element = definition.SemanticElement,
                Role = definition.TopologyRole,
                RequiresUprightEntry = definition.EntryContract.RequiresUprightEntry,
                MaxEntryBankDeg = definition.EntryContract.MaximumBankDegrees,
                MaxEntryPitchDeg = definition.EntryContract.MaximumPitchDegrees,
                MaxEntryHorizontalCurvature = definition.EntryContract.MaximumHorizontalCurvature,
                MaxEntryRollRateDegPerM = definition.EntryContract.MaximumRollRateDegreesPerMeter,
                AcceptsCurvedEntry = definition.EntryContract.AcceptsCurvedEntry,
                RequiresRecoveryAfter = definition.RequiresRecoveryAfter,
                EmitsOwnApproach = definition.EmitsOwnApproach,
                EmitsOwnRecovery = definition.EmitsOwnRecovery,
                Notes = $"Definition-native capability: {definition.StableId} v{definition.DefinitionVersion}"
            };
    }
}
