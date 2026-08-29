using System;
using System.Collections.Generic;
using System.Text;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Read-only parity result between semantic transition need and current connector authority.</summary>
    public enum ConnectorShadowStatus
    {
        Agree = 0,
        DirectWeldOpportunity = 1,
        MissingRequiredTransition = 2,
        InsufficientTransition = 3,
        AcceptedRejectedBoundary = 4,
        RecoveryPurposeMismatch = 5
    }

    /// <summary>
    /// One Stage-2 shadow comparison. It never changes definitions or geometry; it
    /// explains where the current connector ownership agrees with the semantic
    /// boundary contract and where a later consolidation must be careful.
    /// </summary>
    [Serializable]
    public sealed class ConnectorShadowRecord
    {
        public int FromSectionIndex = -1, ToSectionIndex = -1;
        public string Boundary = "";
        public TransitionKind RequiredKind;
        public ConnectorShadowStatus Status;
        public float AvailableLength;
        public float RequiredLength;
        public int ConnectorCount;
        public string ActualPurposes = "";
        public string Reason = "";

        public bool IsMismatch => Status == ConnectorShadowStatus.MissingRequiredTransition ||
                                  Status == ConnectorShadowStatus.InsufficientTransition ||
                                  Status == ConnectorShadowStatus.AcceptedRejectedBoundary ||
                                  Status == ConnectorShadowStatus.RecoveryPurposeMismatch;

        public override string ToString()
            => $"{Boundary}: semantic {RequiredKind}, current [{ActualPurposes}] — {Status} " +
               $"(have {AvailableLength:F0}m, need {RequiredLength:F0}m)" +
               (string.IsNullOrEmpty(Reason) ? "" : $" | {Reason}");
    }

    [Serializable]
    public sealed class ConnectorShadowSummary
    {
        public int BoundaryCount;
        public int AgreementCount;
        public int OpportunityCount;
        public int MismatchCount;
        public int RejectedBoundaryCount;

        public bool HasBlockingMismatch => MismatchCount > 0;

        public override string ToString()
            => $"{BoundaryCount} boundaries: {AgreementCount} agree, {OpportunityCount} direct-weld opportunities, " +
               $"{MismatchCount} mismatches ({RejectedBoundaryCount} semantically rejected)";
    }

    /// <summary>Stage-2 shadow planner: observes only. Current connector decisions remain authoritative.</summary>
    public static class ConnectorShadowAudit
    {
        public static List<ConnectorShadowRecord> Compare(IReadOnlyList<TransitionDecision> transitions,
            out ConnectorShadowSummary summary)
        {
            var records = new List<ConnectorShadowRecord>();
            summary = new ConnectorShadowSummary();
            if (transitions == null) return records;

            foreach (TransitionDecision transition in transitions)
            {
                if (transition == null) continue;
                var record = CompareOne(transition);
                records.Add(record);
                summary.BoundaryCount++;
                if (record.Status == ConnectorShadowStatus.Agree) summary.AgreementCount++;
                else if (record.Status == ConnectorShadowStatus.DirectWeldOpportunity) summary.OpportunityCount++;
                else summary.MismatchCount++;
                if (record.Status == ConnectorShadowStatus.AcceptedRejectedBoundary)
                    summary.RejectedBoundaryCount++;
            }
            return records;
        }

        private static ConnectorShadowRecord CompareOne(TransitionDecision transition)
        {
            int count = transition.ConnectorSectionIndices?.Count ?? 0;
            string purposes = Purposes(transition.ActualConnectorBehaviors);
            var record = new ConnectorShadowRecord
            {
                FromSectionIndex = transition.FromSectionIndex,
                ToSectionIndex = transition.ToSectionIndex,
                Boundary = $"{transition.From} -> {transition.To}",
                RequiredKind = transition.Kind,
                AvailableLength = transition.AvailableLength,
                RequiredLength = transition.MinBlendLength,
                ConnectorCount = count,
                ActualPurposes = purposes
            };

            switch (transition.Kind)
            {
                case TransitionKind.DirectWeld:
                    record.Status = count == 0
                        ? ConnectorShadowStatus.Agree
                        : ConnectorShadowStatus.DirectWeldOpportunity;
                    if (count > 0)
                        record.Reason = "The built boundary is compatible without a dedicated run; later V2 may remove or absorb this connector.";
                    break;

                case TransitionKind.AdaptiveBlend:
                    if (count == 0)
                    {
                        record.Status = ConnectorShadowStatus.MissingRequiredTransition;
                        record.Reason = "A rate-limited blend is required but no connector owns the transition.";
                    }
                    else if (transition.AvailableLength + 0.01f < transition.MinBlendLength)
                    {
                        record.Status = ConnectorShadowStatus.InsufficientTransition;
                        record.Reason = "The current connector run is shorter than the semantic blend demand.";
                    }
                    else record.Status = ConnectorShadowStatus.Agree;
                    break;

                case TransitionKind.ExplicitRecovery:
                    if (HasRecoveryPurpose(transition.ActualConnectorBehaviors))
                        record.Status = ConnectorShadowStatus.Agree;
                    else
                    {
                        record.Status = ConnectorShadowStatus.RecoveryPurposeMismatch;
                        record.Reason = count == 0
                            ? "The feature contract requires recovery but no connector owns it."
                            : "A connector exists, but its current purpose is not recovery.";
                    }
                    break;

                case TransitionKind.Rejected:
                    record.Status = ConnectorShadowStatus.AcceptedRejectedBoundary;
                    record.Reason = "The selected track contains a boundary the semantic resolver says cannot fit. Geometry is unchanged; investigate before enabling V2 authority.";
                    break;
            }

            return record;
        }

        private static bool HasRecoveryPurpose(IReadOnlyList<ConnectorBehavior> behaviors)
        {
            if (behaviors == null) return false;
            for (int i = 0; i < behaviors.Count; i++)
                if (behaviors[i] == ConnectorBehavior.OrientationRecovery) return true;
            return false;
        }

        private static string Purposes(IReadOnlyList<ConnectorBehavior> behaviors)
        {
            if (behaviors == null || behaviors.Count == 0) return "none";
            var seen = new HashSet<ConnectorBehavior>();
            var text = new StringBuilder();
            for (int i = 0; i < behaviors.Count; i++)
            {
                ConnectorBehavior behavior = behaviors[i];
                if (!seen.Add(behavior)) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(behavior);
            }
            return text.ToString();
        }
    }
}
