using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration
{
    /// <summary>
    /// Designer-owned authoring surface for post-generation topology work.
    ///
    /// The TrackGenerator remains responsible for producing and validating a track.
    /// This component owns the editor selection that will later become a deterministic
    /// topology-override layer. It deliberately performs no generation in Update or
    /// OnValidate, so merely selecting it can never rebuild or mutate the track.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Track Generation/Track Editor")]
    public sealed class TrackEditor : MonoBehaviour
    {
        [SerializeField, Tooltip("The generated track this authoring tool inspects and will eventually edit.")]
        private TrackGenerator trackGenerator;

        [SerializeField, HideInInspector]
        private string selectedTopologySlotId = "";

        [SerializeField, HideInInspector]
        private SemanticElementId selectedAlternative = SemanticElementId.None;

        [SerializeField, HideInInspector, Range(0, TrackEditorImpact.MaximumFeatureReach)]
        private int impactBackwardFeatures;

        [SerializeField, HideInInspector, Range(0, TrackEditorImpact.MaximumFeatureReach)]
        private int impactForwardFeatures;

        [SerializeField, HideInInspector]
        private List<TopologySlotOverride> requestedOverrides = new List<TopologySlotOverride>();

        // Unity's ordinary object Undo cannot safely restore a generated hierarchy: the
        // accepted track is swapped transactionally and most of its objects are editor
        // previews. Keep the exact accepted recipe that preceded the latest successful
        // edit instead, so the Track Editor can rebuild that complete design on demand.
        [SerializeField, HideInInspector]
        private string lastAppliedEditUndoRecipe = "";

        [SerializeField, HideInInspector]
        private string lastAppliedEditUndoLabel = "";

        public TrackGenerator Generator => trackGenerator;
        public string SelectedTopologySlotId => selectedTopologySlotId;
        public SemanticElementId SelectedAlternative => selectedAlternative;
        public int ImpactBackwardFeatures => impactBackwardFeatures;
        public int ImpactForwardFeatures => impactForwardFeatures;
        public IReadOnlyList<TopologySlotOverride> RequestedOverrides => requestedOverrides;
        public bool CanUndoLastAppliedEdit => !string.IsNullOrWhiteSpace(lastAppliedEditUndoRecipe);
        public string LastAppliedEditUndoLabel => lastAppliedEditUndoLabel;
        public string LastAppliedEditUndoRecipe => lastAppliedEditUndoRecipe;

        public void Bind(TrackGenerator generator)
        {
            trackGenerator = generator;
        }

        public void SelectSlot(string topologySlotId)
        {
            selectedTopologySlotId = topologySlotId ?? "";
            selectedAlternative = SemanticElementId.None;
        }

        public void SelectAlternative(SemanticElementId alternative)
        {
            selectedAlternative = alternative;
        }

        public void SetImpactReach(int backwardFeatures, int forwardFeatures)
        {
            impactBackwardFeatures = Mathf.Clamp(backwardFeatures, 0,
                TrackEditorImpact.MaximumFeatureReach);
            impactForwardFeatures = Mathf.Clamp(forwardFeatures, 0,
                TrackEditorImpact.MaximumFeatureReach);
        }

        /// <summary>
        /// Records a designer's requested realization without rebuilding any geometry.
        /// The request is intentionally separate from an applied override until the
        /// local replacement check proves that the replacement fits and remains driveable.
        /// </summary>
        public bool TryRequestReplacement(TopologySlotRecord slot, SemanticElementId realization,
            out string reason)
        {
            if (slot == null)
            {
                reason = "The selected segment has no stable topology identity.";
                return false;
            }

            if (realization == SemanticElementId.None || realization == slot.CurrentRealization)
            {
                reason = "Choose a different compatible realization first.";
                return false;
            }

            return TryRecordRequest(slot, realization, false, "Planned", out reason);
        }

        public bool TryRequestReplacement(TopologySlotRecord slot, SemanticElementId realization,
            TrackEditorImpactPlan impactPlan, bool allowFeatureOverrides, out string reason)
        {
            if (!TryRequestReplacement(slot, realization, out reason)) return false;
            ConfigureImpact(slot, impactPlan, allowFeatureOverrides);
            return true;
        }

        /// <summary>
        /// Records an explicit rebuild of the realization already used by a slot.
        /// This is deliberately separate from replacement selection so the editor can
        /// preserve the rule that picking the current realization is not a change.
        /// </summary>
        public bool TryRequestRegeneration(TopologySlotRecord slot, out string reason)
        {
            if (slot == null || slot.CurrentRealization == SemanticElementId.None)
            {
                reason = "The selected segment has no generated design to rebuild.";
                return false;
            }

            return TryRecordRequest(slot, slot.CurrentRealization, true, "Queued a rebuild of", out reason);
        }

        public bool TryRequestRegeneration(TopologySlotRecord slot, TrackEditorImpactPlan impactPlan,
            bool allowFeatureOverrides, out string reason)
        {
            if (!TryRequestRegeneration(slot, out reason)) return false;
            ConfigureImpact(slot, impactPlan, allowFeatureOverrides);
            return true;
        }

        private void ConfigureImpact(TopologySlotRecord slot, TrackEditorImpactPlan impactPlan,
            bool allowFeatureOverrides)
        {
            TopologySlotOverride request = FindRequestedOverride(slot?.TopologySlotId);
            if (request == null) return;
            request.ImpactBackwardFeatures = impactBackwardFeatures;
            request.ImpactForwardFeatures = impactForwardFeatures;
            request.ImpactMembers = TrackEditorImpact.CaptureMembers(impactPlan);
            request.AllowFeatureOverrides = allowFeatureOverrides && request.ImpactMembers.Count > 0;
            if (request.ImpactMembers.Count > 0)
                request.MaximumReplanScope = LocalReplanScope.TurnComplex;
        }

        private bool TryRecordRequest(TopologySlotRecord slot, SemanticElementId realization,
            bool rebuildCurrent, string action, out string reason)
        {
            reason = "";
            if (slot == null || string.IsNullOrWhiteSpace(slot.TopologySlotId))
            {
                reason = "The selected segment has no stable topology identity.";
                return false;
            }

            if (realization == SemanticElementId.None)
            {
                reason = "The selected segment has no valid design to build.";
                return false;
            }

            requestedOverrides ??= new List<TopologySlotOverride>();
            TopologySlotOverride request = null;
            for (int i = 0; i < requestedOverrides.Count; i++)
            {
                if (requestedOverrides[i] != null && string.Equals(
                        requestedOverrides[i].TopologySlotId, slot.TopologySlotId,
                        StringComparison.Ordinal))
                {
                    request = requestedOverrides[i];
                    break;
                }
            }

            if (request == null)
            {
                request = new TopologySlotOverride();
                requestedOverrides.Add(request);
            }

            bool changedRealization = request.RequestedRealization != realization;
            request.TopologySlotId = slot.TopologySlotId;
            request.CanonicalOrder = slot.CanonicalOrder;
            request.StructuralAnchor = TopologySlotCatalog.BuildStructuralAnchor(slot);
            request.OriginalRealization = slot.OriginalRealization;
            request.RequestedRealization = realization;
            request.RebuildCurrentRealization = rebuildCurrent;
            request.LockRealization = true;
            request.MaximumReplanScope = slot.LocalReplanPolicy;
            if (changedRealization)
            {
                request.LockParameters = false;
                request.Parameters?.Clear();
            }

            SortRequests();
            reason = $"{action} {realization} for {slot.TopologySlotId}. Geometry is unchanged until the replacement check passes.";
            return true;
        }

        public TopologySlotOverride FindRequestedOverride(string topologySlotId)
        {
            if (requestedOverrides == null || string.IsNullOrWhiteSpace(topologySlotId)) return null;
            for (int i = 0; i < requestedOverrides.Count; i++)
            {
                TopologySlotOverride request = requestedOverrides[i];
                if (request != null && string.Equals(request.TopologySlotId, topologySlotId,
                        StringComparison.Ordinal))
                    return request;
            }
            return null;
        }

        public bool RemoveRequestedOverride(string topologySlotId)
        {
            if (requestedOverrides == null || string.IsNullOrWhiteSpace(topologySlotId)) return false;
            return requestedOverrides.RemoveAll(request => request != null && string.Equals(
                request.TopologySlotId, topologySlotId, StringComparison.Ordinal)) > 0;
        }

        public void ClearRequestedOverrides()
        {
            requestedOverrides?.Clear();
        }

        public void RememberLastAppliedEdit(string acceptedRecipeJson, string label)
        {
            lastAppliedEditUndoRecipe = acceptedRecipeJson ?? "";
            lastAppliedEditUndoLabel = string.IsNullOrWhiteSpace(label)
                ? "Last accepted Track Editor change"
                : label.Trim();
        }

        public void ClearLastAppliedEditUndo()
        {
            lastAppliedEditUndoRecipe = "";
            lastAppliedEditUndoLabel = "";
        }

        private void OnValidate()
        {
            requestedOverrides ??= new List<TopologySlotOverride>();
            requestedOverrides.RemoveAll(request => request == null ||
                                                    string.IsNullOrWhiteSpace(request.TopologySlotId) ||
                                                    request.RequestedRealization == SemanticElementId.None);
            impactBackwardFeatures = Mathf.Clamp(impactBackwardFeatures, 0,
                TrackEditorImpact.MaximumFeatureReach);
            impactForwardFeatures = Mathf.Clamp(impactForwardFeatures, 0,
                TrackEditorImpact.MaximumFeatureReach);
            lastAppliedEditUndoRecipe ??= "";
            lastAppliedEditUndoLabel ??= "";
            SortRequests();
        }

        private void SortRequests()
        {
            requestedOverrides.Sort((left, right) =>
            {
                int order = left.CanonicalOrder.CompareTo(right.CanonicalOrder);
                return order != 0
                    ? order
                    : string.CompareOrdinal(left.TopologySlotId, right.TopologySlotId);
            });
        }
    }

    /// <summary>Read-only preview of the designer-authorized rebuild window.</summary>
    public sealed class TrackEditorImpactPlan
    {
        public TopologySlotRecord Selected;
        public readonly List<TopologySlotRecord> Backward = new List<TopologySlotRecord>();
        public readonly List<TopologySlotRecord> Forward = new List<TopologySlotRecord>();

        public int FeatureOverrideCount => Backward.Count + Forward.Count;

        public IEnumerable<TopologySlotRecord> EnumerateAffected()
        {
            for (int i = 0; i < Backward.Count; i++) yield return Backward[i];
            for (int i = 0; i < Forward.Count; i++) yield return Forward[i];
        }
    }

    /// <summary>
    /// Route-aware Area of Impact calculations shared by the inspector and the
    /// serialized request. "Backward" and "forward" always mean track travel
    /// direction, never Scene-view or world-space direction.
    /// </summary>
    public static class TrackEditorImpact
    {
        public const int MaximumFeatureReach = 8;

        public static TrackEditorImpactPlan Analyze(
            IReadOnlyList<TopologySlotRecord> slots,
            string selectedTopologySlotId,
            int backwardFeatures,
            int forwardFeatures)
        {
            var plan = new TrackEditorImpactPlan();
            if (slots == null || string.IsNullOrWhiteSpace(selectedTopologySlotId)) return plan;

            for (int i = 0; i < slots.Count; i++)
            {
                TopologySlotRecord candidate = slots[i];
                if (candidate != null && string.Equals(candidate.TopologySlotId,
                        selectedTopologySlotId, StringComparison.Ordinal))
                {
                    plan.Selected = candidate;
                    break;
                }
            }
            if (plan.Selected == null) return plan;

            var route = new List<TopologySlotRecord>();
            for (int i = 0; i < slots.Count; i++)
            {
                TopologySlotRecord candidate = slots[i];
                if (candidate != null && SharesTravelPath(plan.Selected, candidate))
                    route.Add(candidate);
            }
            route.Sort(CompareTravelOrder);
            int selectedIndex = route.FindIndex(candidate => string.Equals(
                candidate.TopologySlotId, selectedTopologySlotId, StringComparison.Ordinal));
            if (selectedIndex < 0) return plan;

            int backward = Math.Max(0, Math.Min(MaximumFeatureReach, backwardFeatures));
            int forward = Math.Max(0, Math.Min(MaximumFeatureReach, forwardFeatures));
            for (int offset = backward; offset >= 1; offset--)
            {
                int index = selectedIndex - offset;
                if (index >= 0) plan.Backward.Add(route[index]);
            }
            for (int offset = 1; offset <= forward; offset++)
            {
                int index = selectedIndex + offset;
                if (index < route.Count) plan.Forward.Add(route[index]);
            }
            return plan;
        }

        /// <summary>
        /// Canonical Road A is one continuous lap, so an authoring window may cross a
        /// quarter boundary. Alternate roads remain isolated to their own branch;
        /// consuming across a split or merge would make the designer's scope ambiguous.
        /// </summary>
        public static bool SharesTravelPath(
            TopologySlotRecord selected, TopologySlotRecord candidate)
        {
            if (selected == null || candidate == null) return false;
            if (selected.RoadId == 0)
                return candidate.RoadId == 0;
            return candidate.QuarterIndex == selected.QuarterIndex &&
                   candidate.RoadId == selected.RoadId;
        }

        private static int CompareTravelOrder(
            TopologySlotRecord left, TopologySlotRecord right)
        {
            int quarter = left.QuarterIndex.CompareTo(right.QuarterIndex);
            if (quarter != 0) return quarter;
            int road = left.RoadId.CompareTo(right.RoadId);
            return road != 0 ? road : left.RouteOrder.CompareTo(right.RouteOrder);
        }

        public static List<TopologyImpactMember> CaptureMembers(TrackEditorImpactPlan plan)
        {
            var members = new List<TopologyImpactMember>();
            if (plan?.Selected == null) return members;
            for (int i = 0; i < plan.Backward.Count; i++)
            {
                TopologySlotRecord slot = plan.Backward[i];
                members.Add(Capture(slot, i - plan.Backward.Count));
            }
            for (int i = 0; i < plan.Forward.Count; i++)
            {
                TopologySlotRecord slot = plan.Forward[i];
                members.Add(Capture(slot, i + 1));
            }
            return members;
        }

        private static TopologyImpactMember Capture(TopologySlotRecord slot, int offset) =>
            new TopologyImpactMember
            {
                TopologySlotId = slot.TopologySlotId,
                StructuralAnchor = TopologySlotCatalog.BuildStructuralAnchor(slot),
                RelativeFeatureOffset = offset,
                OriginalRealization = slot.OriginalRealization
            };
    }
}
