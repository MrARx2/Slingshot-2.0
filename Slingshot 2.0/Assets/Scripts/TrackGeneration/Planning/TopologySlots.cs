using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Whether a topology demand is still automatic or carries a designer choice.</summary>
    public enum TopologySlotOverrideState
    {
        Automatic,
        Requested,
        Applied
    }

    /// <summary>
    /// Stable identity for one gameplay demand on one route. This is deliberately
    /// independent of generated section indices, connector subdivisions, GameObjects,
    /// and the realization currently satisfying the demand.
    /// </summary>
    [Serializable]
    public sealed class TopologySlotRecord
    {
        public string TopologySlotId = "";
        public int CanonicalOrder = -1;
        public int RouteOrder = -1;
        public int QuarterIndex = -1;
        public int RoadId;
        public TopologyRole DemandType;
        public float SignedHeadingDelta;
        public string EntryAnchorId = "";
        public string ExitAnchorId = "";
        public SemanticElementId OriginalRealization = SemanticElementId.None;
        public SemanticElementId CurrentRealization = SemanticElementId.None;
        public TopologySlotOverrideState OverrideState = TopologySlotOverrideState.Automatic;
        public LocalReplanScope LocalReplanPolicy = LocalReplanScope.OwnedConnectors;
        public int FirstDefinitionIndex = -1;
        public int LastDefinitionIndex = -1;
        public string PatternId = "";

        public override string ToString() =>
            $"{TopologySlotId} | Q{QuarterIndex + 1}/R{RoadId} #{RouteOrder} | " +
            $"{DemandType} {SignedHeadingDelta:+0.#;-0.#;0}deg | {CurrentRealization} | " +
            $"defs {FirstDefinitionIndex}-{LastDefinitionIndex}";
    }

    /// <summary>
    /// Builds stable topology-slot records after the complete definition plan exists.
    /// It is reporting-only in V2 Stage 2: no consumer changes geometry or selection.
    /// </summary>
    public static class TopologySlotCatalog
    {
        public static string BuildStructuralAnchor(TopologySlotRecord slot)
        {
            if (slot == null || slot.QuarterIndex < 0) return "";
            return $"{slot.QuarterIndex}|{slot.RoadId}|{(int)slot.DemandType}|" +
                   $"{Mathf.RoundToInt(slot.SignedHeadingDelta)}|" +
                   $"{(int)slot.OriginalRealization}|{slot.RouteOrder}";
        }

        private static bool TryReadStructuralAnchor(
            string value,
            out int quarter,
            out int road,
            out TopologyRole role,
            out int heading,
            out SemanticElementId original,
            out int routeOrder)
        {
            quarter = road = heading = routeOrder = -1;
            role = TopologyRole.HeadingNeutral;
            original = SemanticElementId.None;
            string[] parts = (value ?? "").Split('|');
            if (parts.Length != 6 ||
                !int.TryParse(parts[0], out quarter) ||
                !int.TryParse(parts[1], out road) ||
                !int.TryParse(parts[2], out int roleValue) ||
                !int.TryParse(parts[3], out heading) ||
                !int.TryParse(parts[4], out int originalValue) ||
                !int.TryParse(parts[5], out routeOrder))
                return false;
            role = (TopologyRole)roleValue;
            original = (SemanticElementId)originalValue;
            return quarter >= 0 && routeOrder >= 0;
        }

        /// <summary>
        /// Resolves an authored override against the current planning phase. Exact ID
        /// is preferred, but the pre-connector plan may legitimately have a different
        /// route-order token than the finished route shown in Track Editor. Structural
        /// anchors identify the same demand without weakening geometry validation.
        /// </summary>
        public static bool TryResolveOverride(
            IReadOnlyList<TopologySlotRecord> records,
            TopologySlotOverride request,
            out TopologySlotRecord resolved,
            out string reason,
            bool appliedRealization = false)
        {
            resolved = null;
            reason = "";
            if (records == null || request == null ||
                string.IsNullOrWhiteSpace(request.TopologySlotId))
            {
                reason = "The topology override has no stable slot identity.";
                return false;
            }

            bool hasAnchor = TryReadStructuralAnchor(request.StructuralAnchor,
                out int quarter, out int road, out TopologyRole role, out int heading,
                out SemanticElementId original, out int routeOrder);
            SemanticElementId expectedRealization = appliedRealization
                ? request.RequestedRealization
                : original;
            for (int i = 0; i < records.Count; i++)
            {
                TopologySlotRecord candidate = records[i];
                if (string.Equals(candidate?.TopologySlotId, request.TopologySlotId,
                        StringComparison.Ordinal) &&
                    (!hasAnchor ||
                     (candidate.QuarterIndex == quarter &&
                      candidate.RoadId == road &&
                      candidate.DemandType == role &&
                      Mathf.Abs(candidate.SignedHeadingDelta - heading) <= 0.51f &&
                      candidate.OriginalRealization == expectedRealization)))
                {
                    resolved = candidate;
                    return true;
                }
            }

            var structural = new List<TopologySlotRecord>();
            if (hasAnchor)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    TopologySlotRecord candidate = records[i];
                    if (candidate == null ||
                        candidate.QuarterIndex != quarter ||
                        candidate.RoadId != road ||
                        candidate.DemandType != role ||
                        Mathf.Abs(candidate.SignedHeadingDelta - heading) > 0.51f ||
                        candidate.OriginalRealization != expectedRealization)
                        continue;
                    structural.Add(candidate);
                }
            }

            // Compatibility path for recipes authored before structural anchors were
            // serialized. It remains strict: canonical order and original realization
            // must select exactly one demand.
            if (structural.Count == 0 && !hasAnchor && request.CanonicalOrder >= 0)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    TopologySlotRecord candidate = records[i];
                    if (candidate != null &&
                        candidate.CanonicalOrder == request.CanonicalOrder &&
                        candidate.OriginalRealization == (appliedRealization
                            ? request.RequestedRealization
                            : request.OriginalRealization))
                        structural.Add(candidate);
                }
            }

            if (structural.Count == 1)
            {
                resolved = structural[0];
                return true;
            }

            if (structural.Count > 1 && hasAnchor)
            {
                int bestDistance = int.MaxValue;
                TopologySlotRecord best = null;
                bool tied = false;
                for (int i = 0; i < structural.Count; i++)
                {
                    int distance = Mathf.Abs(structural[i].RouteOrder - routeOrder);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = structural[i];
                        tied = false;
                    }
                    else if (distance == bestDistance)
                    {
                        tied = true;
                    }
                }
                if (!tied)
                {
                    resolved = best;
                    return true;
                }
            }

            reason = structural.Count == 0
                ? $"Edited segment '{request.TopologySlotId}' has no matching structural demand in this route."
                : $"Edited segment '{request.TopologySlotId}' matches {structural.Count} structural demands and cannot be resolved safely.";
            return false;
        }

        public static List<TopologySlotRecord> Assign(IReadOnlyList<TrackMacroSectionDefinition> definitions)
        {
            var records = new List<TopologySlotRecord>();
            if (definitions == null) return records;

            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null) continue;
                definition.TopologySlotId = "";
                definition.TopologySlotOrder = -1;
                definition.TopologySlotRouteOrder = -1;
            }

            var routes = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = definitions[i];
                if (definition == null || definition.QuarterIndex < 0) continue;
                int routeKey = definition.QuarterIndex * 10 + Mathf.Max(0, definition.RoadId);
                if (!routes.TryGetValue(routeKey, out List<int> indices))
                    routes.Add(routeKey, indices = new List<int>());
                indices.Add(i);
            }

            int canonicalOrder = 0;
            foreach (KeyValuePair<int, List<int>> route in routes)
            {
                int routeOrder = 0;
                List<int> indices = route.Value;
                for (int cursor = 0; cursor < indices.Count;)
                {
                    int firstIndex = indices[cursor];
                    TrackMacroSectionDefinition first = definitions[firstIndex];
                    SemanticElementId realization = ResolveSemantic(first);
                    if (!IsDemand(realization))
                    {
                        cursor++;
                        continue;
                    }

                    string ownedPattern = IsOwnedFeaturePattern(first.PatternId) ? first.PatternId : "";
                    int endCursor = cursor + 1;
                    if (ownedPattern.Length > 0)
                    {
                        while (endCursor < indices.Count &&
                               string.Equals(definitions[indices[endCursor]]?.PatternId, ownedPattern,
                                   StringComparison.Ordinal))
                            endCursor++;
                    }

                    // Prefer a non-structural identity found inside the owned group.
                    for (int p = cursor; p < endCursor; p++)
                    {
                        SemanticElementId candidate = ResolveSemantic(definitions[indices[p]]);
                        if (IsDemand(candidate))
                        {
                            realization = candidate;
                            break;
                        }
                    }

                    FeatureCapability capability = FeatureCapabilities.Get(realization);
                    TopologyRole role = capability != null
                        ? capability.Role
                        : TopologyRole.HeadingNeutral;
                    float heading = 0f;
                    for (int p = cursor; p < endCursor; p++)
                        heading += SignedHeading(definitions[indices[p]]);

                    int quarter = first.QuarterIndex;
                    int road = first.RoadId;
                    string roleToken = RoleToken(role);
                    int quantizedHeading = Mathf.RoundToInt(heading);
                    string headingToken = quantizedHeading > 0
                        ? $"p{quantizedHeading:000}"
                        : quantizedHeading < 0
                            ? $"n{Mathf.Abs(quantizedHeading):000}"
                            : "z000";
                    string slotId = $"slot-q{quarter + 1}-r{road}-{roleToken}-{routeOrder:000}-{headingToken}";
                    int lastIndex = indices[endCursor - 1];

                    var record = new TopologySlotRecord
                    {
                        TopologySlotId = slotId,
                        CanonicalOrder = canonicalOrder,
                        RouteOrder = routeOrder,
                        QuarterIndex = quarter,
                        RoadId = road,
                        DemandType = role,
                        SignedHeadingDelta = heading,
                        EntryAnchorId = slotId + ":entry",
                        ExitAnchorId = slotId + ":exit",
                        OriginalRealization = realization,
                        CurrentRealization = realization,
                        FirstDefinitionIndex = firstIndex,
                        LastDefinitionIndex = lastIndex,
                        PatternId = ownedPattern
                    };
                    records.Add(record);

                    for (int p = cursor; p < endCursor; p++)
                    {
                        TrackMacroSectionDefinition definition = definitions[indices[p]];
                        definition.TopologySlotId = slotId;
                        definition.TopologySlotOrder = canonicalOrder;
                        definition.TopologySlotRouteOrder = routeOrder;
                    }

                    canonicalOrder++;
                    routeOrder++;
                    cursor = endCursor;
                }
            }

            return records;
        }

        /// <summary>
        /// Finds semantic travel neighbors. Canonical Road A continues across quarter
        /// boundaries; alternate-road branches remain isolated inside their quarter.
        /// </summary>
        public static bool TryGetNeighbours(IReadOnlyList<TopologySlotRecord> records, string topologySlotId,
            out TopologySlotRecord previous, out TopologySlotRecord next)
        {
            previous = null;
            next = null;
            if (records == null || string.IsNullOrEmpty(topologySlotId)) return false;

            TopologySlotRecord current = null;
            for (int i = 0; i < records.Count; i++)
            {
                if (!string.Equals(records[i]?.TopologySlotId, topologySlotId, StringComparison.Ordinal)) continue;
                current = records[i];
                break;
            }
            if (current == null) return false;

            var route = new List<TopologySlotRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                TopologySlotRecord candidate = records[i];
                if (candidate == null || !TrackEditorImpact.SharesTravelPath(current, candidate))
                    continue;
                route.Add(candidate);
            }
            route.Sort((left, right) =>
            {
                int quarter = left.QuarterIndex.CompareTo(right.QuarterIndex);
                if (quarter != 0) return quarter;
                int road = left.RoadId.CompareTo(right.RoadId);
                return road != 0 ? road : left.RouteOrder.CompareTo(right.RouteOrder);
            });
            int currentIndex = route.FindIndex(candidate => string.Equals(
                candidate.TopologySlotId, topologySlotId, StringComparison.Ordinal));
            if (currentIndex > 0) previous = route[currentIndex - 1];
            if (currentIndex >= 0 && currentIndex + 1 < route.Count)
                next = route[currentIndex + 1];
            return true;
        }

        private static bool IsOwnedFeaturePattern(string patternId) =>
            !string.IsNullOrEmpty(patternId) &&
            !patternId.StartsWith("Quarter_", StringComparison.Ordinal);

        private static bool IsDemand(SemanticElementId element) =>
            element != SemanticElementId.None &&
            element != SemanticElementId.RecoverySection &&
            element != SemanticElementId.ClosureTransfer &&
            element != SemanticElementId.QuarterTransfer;

        private static SemanticElementId ResolveSemantic(TrackMacroSectionDefinition definition)
        {
            if (definition == null) return SemanticElementId.None;
            if (definition.SemanticElement != SemanticElementId.None) return definition.SemanticElement;

            // Alternate-quarter roads predate semantic stamping. Infer only their
            // unambiguous gameplay primitives; neutral straights stay connectors.
            switch (definition.SectionType)
            {
                case TrackMacroSectionType.BankedCurve: return SemanticElementId.OrdinaryCurve;
                case TrackMacroSectionType.BankedHairpin: return SemanticElementId.Hairpin;
                case TrackMacroSectionType.SCurve: return SemanticElementId.SCurve;
                case TrackMacroSectionType.Chicane: return SemanticElementId.Chicane;
                case TrackMacroSectionType.Loop: return SemanticElementId.VerticalLoop;
                case TrackMacroSectionType.Corkscrew: return SemanticElementId.InlineCorkscrew;
                case TrackMacroSectionType.Spiral: return SemanticElementId.Spiral;
                case TrackMacroSectionType.HalfLoopTwist: return SemanticElementId.Immelmann;
                case TrackMacroSectionType.FullPipe: return SemanticElementId.FullPipe;
                case TrackMacroSectionType.WallrideTurn: return SemanticElementId.WallrideTurn;
                case TrackMacroSectionType.RecoveryStraight: return SemanticElementId.RecoverySection;
                default: return SemanticElementId.None;
            }
        }

        private static float SignedHeading(TrackMacroSectionDefinition definition)
        {
            if (definition == null) return 0f;
            float contractHeading = definition.Contract.HeadingDeltaDegrees;
            if (Mathf.Abs(contractHeading) > 0.001f) return contractHeading;

            // These composed features deliberately cancel their internal turns. Their
            // TurnAngle describes one lobe, not a net route-heading demand.
            if (definition.SectionType == TrackMacroSectionType.SCurve ||
                definition.SectionType == TrackMacroSectionType.Chicane)
                return 0f;

            return definition.TurnSign * definition.TurnAngle;
        }

        private static string RoleToken(TopologyRole role)
        {
            switch (role)
            {
                case TopologyRole.TurnRealization: return "turn";
                case TopologyRole.DirectionNeutralTransfer: return "transfer";
                case TopologyRole.CompoundTurnRealization: return "compound";
                case TopologyRole.OrientationTransition: return "orientation";
                case TopologyRole.RecoveryRealization: return "recovery";
                default: return "neutral";
            }
        }
    }
}
