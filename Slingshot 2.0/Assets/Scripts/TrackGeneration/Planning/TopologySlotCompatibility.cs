using System;
using System.Collections.Generic;
using TrackGeneration.Core;
using TrackGeneration.Definitions;
using TrackGeneration.Macro;
using UnityEngine;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Read-only preflight result for the V2 topology-slot browser. A PotentialFit is
    /// deliberately not an authorization to rebuild: geometry length, exit matching,
    /// clearance, and validation still belong to the future local dry-run planner.
    /// </summary>
    public enum TopologySlotCompatibilityStatus
    {
        Current,
        PotentialFit,
        Blocked
    }

    [Serializable]
    public sealed class TopologySlotCompatibilityResult
    {
        public SemanticElementId Candidate;
        public TopologySlotCompatibilityStatus Status;
        public string Reason = "";
    }

    /// <summary>
    /// Conservative semantic and entry-state compatibility check. Reporting only:
    /// this class never changes a recipe, a slot, or generated geometry.
    /// </summary>
    public static class TopologySlotCompatibility
    {
        private const float HeadingToleranceDeg = 5f;
        private const float CurvedEntryEpsilon = 0.0001f;

        public static List<TopologySlotCompatibilityResult> Evaluate(
            TopologySlotRecord slot, TrackConnectionFrame entryFrame)
        {
            var results = new List<TopologySlotCompatibilityResult>();
            if (slot == null) return results;

            var capabilities = new List<FeatureCapability>(FeatureCapabilities.All);
            capabilities.Sort((a, b) => ((int)a.Element).CompareTo((int)b.Element));
            for (int i = 0; i < capabilities.Count; i++)
            {
                FeatureCapability capability = capabilities[i];
                if (!IsDesignerRealization(capability.Element)) continue;
                results.Add(EvaluateCandidate(slot, entryFrame, capability));
            }
            return results;
        }

        public static TopologySlotCompatibilityResult EvaluateCandidate(
            TopologySlotRecord slot, TrackConnectionFrame entryFrame, FeatureCapability capability)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            if (capability == null) throw new ArgumentNullException(nameof(capability));

            if (capability.Element == slot.CurrentRealization)
            {
                return Result(capability.Element, TopologySlotCompatibilityStatus.Current,
                    "Current realization. It already owns this topology slot.");
            }

            if (capability.Role != slot.DemandType)
            {
                return Result(capability.Element, TopologySlotCompatibilityStatus.Blocked,
                    $"Role mismatch: slot requires {FriendlyRole(slot.DemandType)}, candidate is {FriendlyRole(capability.Role)}.");
            }

            if (!SupportsHeading(capability.Element, slot.SignedHeadingDelta, out string headingReason))
            {
                return Result(capability.Element, TopologySlotCompatibilityStatus.Blocked, headingReason);
            }

            if (Mathf.Abs(entryFrame.BankAngle) > capability.MaxEntryBankDeg)
                return Blocked(capability, $"Entry bank {entryFrame.BankAngle:+0.#;-0.#;0}° exceeds its {capability.MaxEntryBankDeg:0.#}° window.");
            if (Mathf.Abs(entryFrame.PitchAngle) > capability.MaxEntryPitchDeg)
                return Blocked(capability, $"Entry pitch {entryFrame.PitchAngle:+0.#;-0.#;0}° exceeds its {capability.MaxEntryPitchDeg:0.#}° window.");
            if (!capability.AcceptsCurvedEntry && Mathf.Abs(entryFrame.HorizontalCurvature) > CurvedEntryEpsilon)
                return Blocked(capability, "Requires a straight entry, but this slot begins on horizontal curvature.");
            if (Mathf.Abs(entryFrame.HorizontalCurvature) > capability.MaxEntryHorizontalCurvature)
                return Blocked(capability, "Entry curvature exceeds the candidate's declared capability window.");
            if (Mathf.Abs(entryFrame.RoadRollRate) > capability.MaxEntryRollRateDegPerM)
                return Blocked(capability, "Entry roll rate exceeds the candidate's declared capability window.");

            string obligations = capability.RequiresRecoveryAfter
                ? " Recovery space is also required."
                : "";
            return Result(capability.Element, TopologySlotCompatibilityStatus.PotentialFit,
                "Semantic demand and entry window match." + obligations +
                " Length, exit frame, clearance, and driveability still require a local dry-run.");
        }

        private static TopologySlotCompatibilityResult Blocked(FeatureCapability capability, string reason) =>
            Result(capability.Element, TopologySlotCompatibilityStatus.Blocked, reason);

        private static TopologySlotCompatibilityResult Result(SemanticElementId candidate,
            TopologySlotCompatibilityStatus status, string reason) =>
            new TopologySlotCompatibilityResult { Candidate = candidate, Status = status, Reason = reason };

        private static bool IsDesignerRealization(SemanticElementId element) =>
            element != SemanticElementId.None &&
            element != SemanticElementId.RecoverySection &&
            element != SemanticElementId.ClosureTransfer &&
            element != SemanticElementId.QuarterTransfer;

        private static bool SupportsHeading(SemanticElementId element, float signedHeading, out string reason)
        {
            float heading = Mathf.Abs(signedHeading);
            if (TrackFeatureDefinitionCatalog.TryGet(element, out TrackFeatureDefinition definition) &&
                definition.Enabled && definition.TopologyRole == TopologyRole.TurnRealization &&
                definition.Turn.HeadingDegrees.Maximum > 0f)
            {
                float minimum = definition.Turn.HeadingDegrees.Minimum;
                float maximum = definition.Turn.HeadingDegrees.Maximum;
                bool definitionSupported = heading >= minimum - HeadingToleranceDeg &&
                                           heading <= maximum + HeadingToleranceDeg;
                reason = $"{definition.DisplayName} requires a roughly {minimum:0}–{maximum:0}° heading demand.";
                return definitionSupported;
            }

            bool supported;
            switch (element)
            {
                case SemanticElementId.OrdinaryCurve:
                case SemanticElementId.DoubleApex:
                case SemanticElementId.TighteningCorner:
                case SemanticElementId.OpeningCorner:
                    supported = heading >= 20f - HeadingToleranceDeg && heading <= 145f + HeadingToleranceDeg;
                    reason = "This turn family currently supports roughly 20–145° heading demands.";
                    return supported;

                case SemanticElementId.Hairpin:
                case SemanticElementId.SweeperIntoHairpin:
                    supported = heading >= 150f - HeadingToleranceDeg && heading <= 180f + HeadingToleranceDeg;
                    reason = "This reversal family requires a roughly 150–180° heading demand.";
                    return supported;

                case SemanticElementId.WallrideTurn:
                    supported = heading >= 45f - HeadingToleranceDeg && heading <= 145f + HeadingToleranceDeg;
                    reason = "The current wallride-turn family is limited to substantial 45–145° turns.";
                    return supported;

                case SemanticElementId.Immelmann:
                case SemanticElementId.HalfLoopToCorkscrew:
                    supported = Mathf.Abs(heading - 180f) <= HeadingToleranceDeg;
                    reason = "This inversion realizes a 180° reversal.";
                    return supported;

                case SemanticElementId.DirectionalCorkscrew:
                    supported = Mathf.Abs(heading - 45f) <= HeadingToleranceDeg ||
                                Mathf.Abs(heading - 90f) <= HeadingToleranceDeg;
                    reason = "Directional corkscrews currently support 45° or 90° turn tokens.";
                    return supported;

                case SemanticElementId.SCurve:
                case SemanticElementId.Chicane:
                case SemanticElementId.AlternatingRadiusSequence:
                    supported = heading <= HeadingToleranceDeg;
                    reason = "Transfers preserve the route heading and require a heading-neutral slot.";
                    return supported;

                default:
                    supported = heading <= HeadingToleranceDeg;
                    reason = "This feature preserves heading and requires a heading-neutral slot.";
                    return supported;
            }
        }

        private static string FriendlyRole(TopologyRole role)
        {
            switch (role)
            {
                case TopologyRole.TurnRealization: return "a turn";
                case TopologyRole.DirectionNeutralTransfer: return "a direction-neutral transfer";
                case TopologyRole.CompoundTurnRealization: return "a compound turn";
                case TopologyRole.OrientationTransition: return "an orientation transition";
                case TopologyRole.RecoveryRealization: return "recovery";
                default: return "a heading-neutral feature";
            }
        }
    }

    /// <summary>The cheap checks that must pass before a requested slot replacement may build geometry.</summary>
    public enum TopologyReplacementPreflightStatus
    {
        ReadyForGeometryDryRun,
        BlockedStaleRequest,
        BlockedSemanticContract,
        BlockedMissingGeometry,
        BlockedInvalidBoundary,
        BlockedReplanScope
    }

    /// <summary>
    /// Result of validating replacement intent against the currently loaded track.
    /// Passing this preflight is not approval: it only means the request is coherent
    /// enough to enter the later local geometry dry-run.
    /// </summary>
    public sealed class TopologyReplacementPreflightResult
    {
        public TopologyReplacementPreflightStatus Status;
        public string Reason = "";
        public int OwnedSectionCount;
        public float OwnedLength;
        public LocalReplanScope RequiredScope;
        public TrackConnectionFrame EntryFrame;
        public TrackConnectionFrame ExitFrame;

        public bool Passed => Status == TopologyReplacementPreflightStatus.ReadyForGeometryDryRun;
    }

    /// <summary>
    /// Deterministic, zero-mutation gate between a designer request and the future
    /// geometry dry-run. It deliberately does not estimate or approve physical fit.
    /// </summary>
    public static class TopologyReplacementPreflight
    {
        public static TopologyReplacementPreflightResult Evaluate(
            TopologySlotRecord slot,
            TopologySlotOverride request,
            IReadOnlyList<GeneratedTrackSection> ownedSections)
        {
            var result = new TopologyReplacementPreflightResult();

            if (slot == null || request == null ||
                string.IsNullOrWhiteSpace(slot.TopologySlotId) ||
                !string.Equals(slot.TopologySlotId, request.TopologySlotId, StringComparison.Ordinal) ||
                (request.CanonicalOrder >= 0 && request.CanonicalOrder != slot.CanonicalOrder) ||
                (request.OriginalRealization != SemanticElementId.None &&
                 request.OriginalRealization != slot.OriginalRealization))
                return Block(result, TopologyReplacementPreflightStatus.BlockedStaleRequest,
                    "The request no longer matches this stable topology segment. Remove it or load the layout it was authored for.");

            if (request.RequestedRealization == SemanticElementId.None)
                return Block(result, TopologyReplacementPreflightStatus.BlockedSemanticContract,
                    "The request does not select a valid realization.");

            if (request.RebuildCurrentRealization &&
                request.RequestedRealization != slot.CurrentRealization)
                return Block(result, TopologyReplacementPreflightStatus.BlockedStaleRequest,
                    "This regeneration request no longer points to the design currently used by the segment.");

            if (!request.RebuildCurrentRealization &&
                request.RequestedRealization == slot.CurrentRealization)
                return Block(result, TopologyReplacementPreflightStatus.BlockedSemanticContract,
                    "The request does not select a different realization.");

            if (ownedSections == null || ownedSections.Count == 0)
                return Block(result, TopologyReplacementPreflightStatus.BlockedMissingGeometry,
                    "The segment has no live owned geometry. Repair or regenerate the cached preview first.");

            GeneratedTrackSection first = ownedSections[0];
            GeneratedTrackSection last = ownedSections[ownedSections.Count - 1];
            if (first?.Definition == null || last?.Definition == null ||
                !FrameIsUsable(first.StartFrame) || !FrameIsUsable(last.EndFrame))
                return Block(result, TopologyReplacementPreflightStatus.BlockedInvalidBoundary,
                    "The segment entry or exit frame is invalid, so a local rebuild cannot weld safely.");

            result.EntryFrame = first.StartFrame;
            result.ExitFrame = last.EndFrame;
            result.OwnedSectionCount = ownedSections.Count;
            for (int i = 0; i < ownedSections.Count; i++)
            {
                GeneratedTrackSection section = ownedSections[i];
                if (section?.Definition == null)
                    return Block(result, TopologyReplacementPreflightStatus.BlockedInvalidBoundary,
                        "One of the segment's owned sections has no definition.");
                result.OwnedLength += Mathf.Max(0f, section.Definition.Length);
            }

            List<TopologySlotCompatibilityResult> compatibility =
                TopologySlotCompatibility.Evaluate(slot, result.EntryFrame);
            TopologySlotCompatibilityResult candidate = compatibility.Find(item =>
                item.Candidate == request.RequestedRealization);
            if (candidate == null || candidate.Status == TopologySlotCompatibilityStatus.Blocked)
                return Block(result, TopologyReplacementPreflightStatus.BlockedSemanticContract,
                    candidate?.Reason ?? "The requested realization is not part of this segment's semantic family.");

            FeatureCapability capability = FeatureCapabilities.Get(request.RequestedRealization);
            result.RequiredScope = capability != null && capability.RequiresRecoveryAfter
                ? LocalReplanScope.OwnedConnectors
                : LocalReplanScope.FeatureOnly;
            if (request.MaximumReplanScope < result.RequiredScope)
                return Block(result, TopologyReplacementPreflightStatus.BlockedReplanScope,
                    $"{request.RequestedRealization} requires {result.RequiredScope}, but this request allows only " +
                    $"{request.MaximumReplanScope}.");

            result.Status = TopologyReplacementPreflightStatus.ReadyForGeometryDryRun;
            result.Reason = $"Preflight passed: {result.OwnedSectionCount} section(s), {result.OwnedLength:0.#} m owned, " +
                            $"up to {request.MaximumReplanScope}. Geometry fit is not approved yet.";
            return result;
        }

        private static TopologyReplacementPreflightResult Block(
            TopologyReplacementPreflightResult result,
            TopologyReplacementPreflightStatus status,
            string reason)
        {
            result.Status = status;
            result.Reason = reason ?? "Replacement preflight failed.";
            return result;
        }

        private static bool FrameIsUsable(in TrackConnectionFrame frame)
        {
            return VectorIsFinite(frame.Position) && VectorIsFinite(frame.Forward) &&
                   VectorIsFinite(frame.Up) && VectorIsFinite(frame.Right) &&
                   frame.Forward.sqrMagnitude > 0.5f && frame.Up.sqrMagnitude > 0.5f &&
                   frame.Right.sqrMagnitude > 0.5f && IsFinite(frame.Width) && frame.Width > 0f;
        }

        private static bool VectorIsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Outcome of comparing built replacement geometry with the live slot boundaries.
    /// Neither successful state authorizes mutation: clearance and final driveability
    /// validation still have to pass before a replacement may be committed.
    /// </summary>
    public enum TopologyReplacementGeometryStatus
    {
        ReadyForClearanceValidation,
        ReadyForConnectorSolve,
        BlockedPreflight,
        BlockedCandidatePlanning,
        BlockedBoundaryMismatch
    }

    /// <summary>Measured, deterministic output from the geometry portion of a local dry-run.</summary>
    public sealed class TopologyReplacementGeometryResult
    {
        public TopologyReplacementGeometryStatus Status;
        public string Reason = "";
        public float CandidateLength;
        public float PositionError;
        public float ForwardAngleError;
        public float UpAngleError;
        public float WidthError;

        public bool GeometryBuilt =>
            Status == TopologyReplacementGeometryStatus.ReadyForClearanceValidation ||
            Status == TopologyReplacementGeometryStatus.ReadyForConnectorSolve;
        public bool DirectBoundaryFit =>
            Status == TopologyReplacementGeometryStatus.ReadyForClearanceValidation;
    }

    /// <summary>
    /// Stage two of the non-mutating replacement dry-run. It consumes geometry built
    /// by the existing feature/corner builders and measures whether that geometry can
    /// weld directly to the owned exit boundary or needs the allowed connector scope.
    /// It never changes definitions, generated sections, scene objects, or recipes.
    /// </summary>
    public static class TopologyReplacementGeometryProbe
    {
        private const float DirectPositionTolerance = 0.25f;
        private const float DirectAngleToleranceDeg = 1f;
        private const float DirectWidthTolerance = 0.05f;

        public static TopologyReplacementGeometryResult Evaluate(
            TopologyReplacementPreflightResult preflight,
            TopologySlotOverride request,
            FeaturePlanResult candidate)
        {
            var result = new TopologyReplacementGeometryResult();
            if (preflight == null || !preflight.Passed || request == null)
                return Block(result, TopologyReplacementGeometryStatus.BlockedPreflight,
                    preflight?.Reason ?? "Replacement preflight has not passed.");

            if (candidate == null || candidate.Failed)
                return Block(result, TopologyReplacementGeometryStatus.BlockedCandidatePlanning,
                    candidate?.FailureReason ?? "The requested realization did not produce candidate geometry.");

            TrackConnectionFrame candidateExit = candidate.ExitState;
            if (!FrameIsUsable(candidateExit))
                return Block(result, TopologyReplacementGeometryStatus.BlockedCandidatePlanning,
                    "The requested realization produced an invalid exit frame.");

            // FeaturePlanResult exits are intentionally entry-relative. The loaded
            // section boundaries are world-space, so compare both in the slot entry's
            // local frame. Without this conversion, the same candidate would appear
            // to fail solely because the TrackGenerator object was moved or rotated.
            TrackConnectionFrame ownedExit = ToEntrySpace(preflight.EntryFrame, preflight.ExitFrame);

            result.CandidateLength = Mathf.Max(0f, candidate.ArcLength);
            result.PositionError = Vector3.Distance(candidateExit.Position, ownedExit.Position);
            result.ForwardAngleError = Vector3.Angle(candidateExit.Forward, ownedExit.Forward);
            result.UpAngleError = Vector3.Angle(candidateExit.Up, ownedExit.Up);
            result.WidthError = Mathf.Abs(candidateExit.Width - ownedExit.Width);

            bool direct = result.PositionError <= DirectPositionTolerance &&
                          result.ForwardAngleError <= DirectAngleToleranceDeg &&
                          result.UpAngleError <= DirectAngleToleranceDeg &&
                          result.WidthError <= DirectWidthTolerance;
            if (direct)
            {
                result.Status = TopologyReplacementGeometryStatus.ReadyForClearanceValidation;
                result.Reason = "Candidate geometry matches the owned exit boundary. Clearance and driveability validation remain.";
                return result;
            }

            if (request.MaximumReplanScope >= LocalReplanScope.OwnedConnectors)
            {
                result.Status = TopologyReplacementGeometryStatus.ReadyForConnectorSolve;
                result.Reason = $"Candidate geometry needs designer route adaptation: {DescribeErrors(result)} " +
                                "Build & Apply may redistribute legal connector, recovery, or ordinary road before " +
                                "running the complete safety validation. No live geometry has changed.";
                return result;
            }

            return Block(result, TopologyReplacementGeometryStatus.BlockedBoundaryMismatch,
                $"Feature-only replacement cannot reach the owned exit boundary: {DescribeErrors(result)}");
        }

        private static TopologyReplacementGeometryResult Block(
            TopologyReplacementGeometryResult result,
            TopologyReplacementGeometryStatus status,
            string reason)
        {
            result.Status = status;
            result.Reason = reason ?? "Replacement geometry probe failed.";
            return result;
        }

        private static string DescribeErrors(TopologyReplacementGeometryResult result) =>
            $"position {result.PositionError:0.##} m, forward {result.ForwardAngleError:0.##}°, " +
            $"up {result.UpAngleError:0.##}°, width {result.WidthError:0.##} m.";

        private static TrackConnectionFrame ToEntrySpace(
            in TrackConnectionFrame entry, in TrackConnectionFrame worldFrame)
        {
            Quaternion entryRotation = Quaternion.LookRotation(entry.Forward, entry.Up);
            Quaternion inverse = Quaternion.Inverse(entryRotation);
            TrackConnectionFrame local = worldFrame;
            local.Position = inverse * (worldFrame.Position - entry.Position);
            local.Forward = inverse * worldFrame.Forward;
            local.Right = inverse * worldFrame.Right;
            local.Up = inverse * worldFrame.Up;
            local.ArcLength = worldFrame.ArcLength - entry.ArcLength;
            return local;
        }

        private static bool FrameIsUsable(in TrackConnectionFrame frame) =>
            VectorIsFinite(frame.Position) && VectorIsFinite(frame.Forward) &&
            VectorIsFinite(frame.Up) && VectorIsFinite(frame.Right) &&
            frame.Forward.sqrMagnitude > 0.5f && frame.Up.sqrMagnitude > 0.5f &&
            frame.Right.sqrMagnitude > 0.5f && IsFinite(frame.Width) && frame.Width > 0f;

        private static bool VectorIsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
