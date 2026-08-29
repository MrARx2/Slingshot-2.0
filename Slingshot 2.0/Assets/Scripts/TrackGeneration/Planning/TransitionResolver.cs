using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>How one feature boundary should be joined (Stage C of the dynamic-topology plan).</summary>
    public enum TransitionKind
    {
        /// <summary>Boundary states compatible — the pair could share one exact frame with no inserted geometry.</summary>
        DirectWeld = 0,
        /// <summary>Reconcilable with a rate-limited blend (which may curve/climb/bank — never automatically a straight).</summary>
        AdaptiveBlend = 1,
        /// <summary>A physically required recovery (jump landings, wallride/hairpin exits) — deliberate, justified.</summary>
        ExplicitRecovery = 2,
        /// <summary>The required transition cannot fit the available run — recorded, never silently padded.</summary>
        Rejected = 3,
    }

    /// <summary>One resolved boundary decision with its per-channel reasoning.</summary>
    [Serializable]
    public sealed class TransitionDecision
    {
        public TransitionKind Kind;
        public string From = "", To = "";

        /// <summary>Stable section identities used to correlate this shadow decision with the built layout.</summary>
        public int FromSectionIndex = -1, ToSectionIndex = -1;

        /// <summary>Every connector section crossed between From and To, in track order.</summary>
        public List<int> ConnectorSectionIndices = new List<int>();

        /// <summary>Snapshot of the connector authority that actually shaped those sections.</summary>
        public List<ConnectorBehavior> ActualConnectorBehaviors = new List<ConnectorBehavior>();

        /// <summary>Shortest legal transition (m). 0 for DirectWeld.</summary>
        public float MinBlendLength;

        /// <summary>Run currently available between the pair (connector straights), meters.</summary>
        public float AvailableLength;

        // Physical boundary state. These structured values keep diagnostics useful to
        // tools/tests without requiring them to parse the human-readable channel text.
        public float ExitBankDeg, EntryBankDeg;
        public float ExitPitchDeg, EntryPitchDeg;
        public float ExitRollRateDegPerM, EntryRollRateDegPerM;
        public float ExitWidth, EntryWidth;
        public float ExitHorizontalCurvature, EntryHorizontalCurvature;

        /// <summary>Per-channel demands, e.g. "bank 34.2° → 155m". Empty for DirectWeld.</summary>
        public List<string> ChannelDemands = new List<string>();

        public string Reason = "";

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{From} → {To}: {Kind}");
            if (Kind != TransitionKind.DirectWeld)
                sb.Append($" | need {MinBlendLength:F0}m, have {AvailableLength:F0}m");
            if (ChannelDemands.Count > 0)
                sb.Append($" | {string.Join(", ", ChannelDemands)}");
            if (!string.IsNullOrEmpty(Reason))
                sb.Append($" | {Reason}");
            sb.Append($" | state bank {ExitBankDeg:F1}->{EntryBankDeg:F1}deg, pitch {ExitPitchDeg:F1}->{EntryPitchDeg:F1}deg, " +
                      $"rollRate {ExitRollRateDegPerM:F3}->{EntryRollRateDegPerM:F3}deg/m, " +
                      $"width {ExitWidth:F1}->{EntryWidth:F1}m, curvature {ExitHorizontalCurvature:F5}->{EntryHorizontalCurvature:F5}/m");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Stage C: the family-agnostic boundary resolver — REPORTING ONLY. It walks the
    /// built candidate's sections, finds every content→content boundary (including
    /// pairs separated by connector straights), and classifies how that pair COULD be
    /// joined: the dry run for Stage D's straight-dropping, produced from real built
    /// frames and the Stage B capability contracts, never from element names.
    ///
    /// Nothing here changes geometry. The records land in the generation report so
    /// designers (and Stage D) can see, per boundary, exactly which channels demand
    /// length and which straights are structurally unnecessary.
    /// </summary>
    public static class TransitionResolver
    {
        /// <summary>Channels within these of zero delta weld directly (weld window).</summary>
        private const float WeldBankDeg = 3f, WeldPitchDeg = 3f, WeldWidth = 2f, WeldRollRate = 0.02f;

        public static List<TransitionDecision> Resolve(IReadOnlyList<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            var decisions = new List<TransitionDecision>();
            if (sections == null || sections.Count == 0) return decisions;

            int n = sections.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = sections[i];
                if (prev?.Definition == null || !IsContent(prev)) continue;

                // Scan forward over connector straights to the next content section.
                float run = 0f;
                int j = (i + 1) % n;
                int guard = 0;
                bool crossedAirGap = false;
                var connectorIndices = new List<int>();
                var connectorBehaviors = new List<ConnectorBehavior>();
                while (guard++ < n)
                {
                    var s = sections[j];
                    if (s?.Definition == null) break;
                    if (s.IsEmptySpace) { crossedAirGap = true; break; }   // jumps own their boundary
                    if (IsContent(s)) break;
                    run += s.Definition.Length;
                    if (s.Definition.IsStraightFamily)
                    {
                        connectorIndices.Add(s.SectionIndex);
                        connectorBehaviors.Add(s.Definition.ConnectorBehavior);
                    }
                    j = (j + 1) % n;
                }
                if (crossedAirGap || guard >= n || j == i) continue;

                var next = sections[j];

                // Dual-road branch awareness: an alternate-road (RoadId 1) section and a
                // canonical-road (RoadId 0) section are NOT physically adjacent — the
                // def list interleaves the two roads of a dual quarter, so a flat-list
                // walk pairs a road-A exit with a road-B entry that never touch. Skip
                // any boundary that straddles two roads, or crosses an OPEN edge (a
                // jump lip / landing mouth that welds to a flight, not to its list
                // neighbour).
                if (prev.RoadId != next.RoadId || prev.OpenEnd || next.OpenStart)
                    continue;

                decisions.Add(Decide(prev, next, run, cfg, connectorIndices, connectorBehaviors));
            }

            return decisions;
        }

        private static bool IsContent(GeneratedTrackSection s)
            => !s.Definition.IsStraightFamily &&
               s.Definition.SectionType != TrackMacroSectionType.AirGap;

        private static TransitionDecision Decide(GeneratedTrackSection prev, GeneratedTrackSection next,
            float availableRun, ResolvedTrackGenerationConfig cfg,
            List<int> connectorIndices, List<ConnectorBehavior> connectorBehaviors)
        {
            var d = new TransitionDecision
            {
                From = Name(prev),
                To = Name(next),
                FromSectionIndex = prev.SectionIndex,
                ToSectionIndex = next.SectionIndex,
                AvailableLength = availableRun,
                ConnectorSectionIndices = connectorIndices ?? new List<int>(),
                ActualConnectorBehaviors = connectorBehaviors ?? new List<ConnectorBehavior>(),
                ExitBankDeg = prev.EndFrame.BankAngle,
                EntryBankDeg = next.StartFrame.BankAngle,
                ExitPitchDeg = prev.EndFrame.PitchAngle,
                EntryPitchDeg = next.StartFrame.PitchAngle,
                ExitRollRateDegPerM = prev.EndFrame.RoadRollRate,
                EntryRollRateDegPerM = next.StartFrame.RoadRollRate,
                ExitWidth = prev.EndFrame.Width,
                EntryWidth = next.StartFrame.Width,
                ExitHorizontalCurvature = prev.EndFrame.HorizontalCurvature,
                EntryHorizontalCurvature = next.StartFrame.HorizontalCurvature
            };

            // 1. Physically required recovery wins outright — deliberate, not padding.
            var prevCap = FeatureCapabilities.Get(prev.Definition.SemanticElement);
            var nextCap = FeatureCapabilities.Get(next.Definition.SemanticElement);
            if (prevCap != null && prevCap.RequiresRecoveryAfter)
            {
                d.Kind = TransitionKind.ExplicitRecovery;
                d.MinBlendLength = cfg.DefaultRecoveryLength;
                d.Reason = $"{prev.Definition.SemanticElement} physically requires recovery";
                return d;
            }

            // 2. Per-channel deltas: the prev section's real exit state vs what the
            // next element ACCEPTS (its capability window), not vs its built entry —
            // the question is "could next start directly from prev's exit".
            var exit = prev.EndFrame;
            float bankTarget = nextCap != null ? Mathf.Min(Mathf.Abs(exit.BankAngle), nextCap.MaxEntryBankDeg) : 0f;
            float pitchTarget = nextCap != null ? Mathf.Min(Mathf.Abs(exit.PitchAngle), nextCap.MaxEntryPitchDeg) : 0f;

            float dBank = Mathf.Abs(exit.BankAngle) - bankTarget;
            float dPitch = Mathf.Abs(exit.PitchAngle) - pitchTarget;
            float dWidth = Mathf.Abs(exit.Width - next.StartFrame.Width);
            float dRoll = Mathf.Abs(exit.RoadRollRate);
            if (nextCap != null) dRoll = Mathf.Max(0f, dRoll - nextCap.MaxEntryRollRateDegPerM);

            float need = 0f;
            void Channel(float delta, float weld, float fullScale, float fullLength, string name, string unit)
            {
                if (delta <= weld) return;
                float len = fullScale > 0.001f ? (delta / fullScale) * fullLength : fullLength;
                len = Mathf.Max(len, cfg.MinimumConnectorLength * 0.5f);
                need = Mathf.Max(need, len);
                d.ChannelDemands.Add($"{name} {delta:F1}{unit} → {len:F0}m");
            }

            // Banking is NOT reconciled by an inserted connector — the global banking
            // field blends bank across a blur radius wider than any single link (the
            // same radius the report prints: max(60, ½·bankBlend, ½·bankReversal)). Two
            // banked curves sharing at least that much run between them therefore hand
            // their bank over through the field, not a blend. Only charge the bank
            // channel when the available run is SHORTER than the field can smooth over.
            float fieldBlurRadius = Mathf.Max(60f,
                Mathf.Max(cfg.BankTransitionLength * 0.5f, cfg.MinimumBankReversalLength * 0.5f));
            if (availableRun < fieldBlurRadius)
                Channel(dBank, WeldBankDeg, cfg.MaxBankAngle, cfg.BankTransitionLength, "bank", "°");
            Channel(dPitch, WeldPitchDeg, Mathf.Max(cfg.MaxClimbAngle, cfg.MaxDropAngle), cfg.PitchTransitionLength, "pitch", "°");
            Channel(dWidth, WeldWidth, cfg.RoadWidth, cfg.WidthTransitionLength, "width", "m");
            Channel(dRoll, WeldRollRate, 0.5f, cfg.RollTransitionLength, "rollRate", "°/m");

            // Upright requirement: an inversion-family successor needs a near-upright,
            // low-rate entry — if prev exits heavily banked, the blend above already
            // demands the un-banking length; note the reason explicitly.
            if (nextCap != null && nextCap.RequiresUprightEntry && Mathf.Abs(exit.BankAngle) > nextCap.MaxEntryBankDeg)
                d.Reason = $"{next.Definition.SemanticElement} requires upright entry";

            d.MinBlendLength = need;

            if (need <= 0.001f)
            {
                d.Kind = TransitionKind.DirectWeld;
                return d;
            }

            d.Kind = need <= Mathf.Max(availableRun, 0.001f)
                ? TransitionKind.AdaptiveBlend
                : TransitionKind.Rejected;
            if (d.Kind == TransitionKind.Rejected)
                d.Reason = string.IsNullOrEmpty(d.Reason)
                    ? "blend does not fit the available run"
                    : d.Reason + "; blend does not fit the available run";
            return d;
        }

        private static string Name(GeneratedTrackSection s)
        {
            var e = s.Definition.SemanticElement;
            string id = e != SemanticElementId.None ? e.ToString() : s.Definition.SectionType.ToString();
            return string.IsNullOrEmpty(s.Definition.DebugName) ? id : $"{id}({s.Definition.DebugName})";
        }
    }
}
