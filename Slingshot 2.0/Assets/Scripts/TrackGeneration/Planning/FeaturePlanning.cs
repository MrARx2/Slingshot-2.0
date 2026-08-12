using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Stage A of the dynamic-topology plan: the exact, achieved result of planning a
    /// feature — measured by BUILDING the definitions with the same section builders
    /// the candidate uses (<see cref="SectionFrameBuilders.BuildSectionFrames"/>).
    /// One calculation; the definitions in here are the ones emitted.
    ///
    /// Stage A is additive truth: nothing consumes these values to change placement
    /// yet. They are asserted against the legacy stamped plan scalars, stored on the
    /// plan, and reported — the foundation Stages B–E build on.
    /// </summary>
    public sealed class FeaturePlanResult
    {
        public SemanticElementId Element;
        public string PatternId = "";

        /// <summary>The definitions that will actually be emitted (not a copy).</summary>
        public List<TrackMacroSectionDefinition> Definitions;

        /// <summary>Exit frame in the ENTRY frame's coordinates (entry at origin, +Z forward).</summary>
        public TrackConnectionFrame ExitState;

        /// <summary>Exit position/rotation relative to the entry pose.</summary>
        public Vector3 RelativeExitPosition;
        public Quaternion RelativeExitRotation;

        /// <summary>Plan-view displacement in the entry frame: x = lateral (right+), y = forward.</summary>
        public Vector2 PlanDisplacement;

        /// <summary>Net plan-view heading change entry→exit, degrees (signed, CW+).</summary>
        public float HeadingContributionDeg;

        public float ElevationChange;
        public float MinElevation, MaxElevation;   // entry-relative
        public float ArcLength;

        /// <summary>Entry-relative AABB of every built ring, inflated by half the road width.</summary>
        public Bounds SweptFootprint;

        /// <summary>Unwrapped roll accumulated across the feature (degrees).</summary>
        public float AccumulatedRollDeg;

        public string FailureReason;
        public bool Failed => !string.IsNullOrEmpty(FailureReason);

        public override string ToString() =>
            $"{Element} '{PatternId}': heading {HeadingContributionDeg:+0.0;-0.0}° | " +
            $"disp fwd {PlanDisplacement.y:F0}m lat {PlanDisplacement.x:F0}m | " +
            $"elev {ElevationChange:+0.0;-0.0}m ({MinElevation:F0}..{MaxElevation:F0}) | " +
            $"arc {ArcLength:F0}m | roll {AccumulatedRollDeg:F0}° | " +
            $"exit bank {ExitState.BankAngle:F1}° pitch {ExitState.PitchAngle:F1}°";
    }

    /// <summary>Stage A helpers: semantic identity stamping and plan-result measurement.</summary>
    public static class FeaturePlanning
    {
        // ─────────────────────────── Semantic identity ───────────────────────────

        /// <summary>Registry pattern type → semantic element.</summary>
        public static SemanticElementId ElementOf(TrackPatternType pattern) => pattern switch
        {
            TrackPatternType.JumpGap => SemanticElementId.JumpGap,
            TrackPatternType.FullLoop => SemanticElementId.VerticalLoop,
            TrackPatternType.Corkscrew => SemanticElementId.InlineCorkscrew,
            TrackPatternType.Spiral => SemanticElementId.Spiral,
            TrackPatternType.HalfLoopRollout => SemanticElementId.Immelmann,
            TrackPatternType.HalfLoopToCorkscrew => SemanticElementId.HalfLoopToCorkscrew,
            TrackPatternType.SpiralToCorkscrew => SemanticElementId.SpiralToCorkscrew,
            TrackPatternType.LoopToCorkscrew => SemanticElementId.LoopToCorkscrew,
            TrackPatternType.DoubleCorkscrew => SemanticElementId.DoubleCorkscrew,
            TrackPatternType.JumpToBankedLanding => SemanticElementId.JumpToBankedLanding,
            TrackPatternType.SCurve => SemanticElementId.SCurve,
            TrackPatternType.Chicane => SemanticElementId.Chicane,
            TrackPatternType.DoubleApex => SemanticElementId.DoubleApex,
            TrackPatternType.TighteningCorner => SemanticElementId.TighteningCorner,
            TrackPatternType.OpeningCorner => SemanticElementId.OpeningCorner,
            TrackPatternType.SweeperIntoHairpin => SemanticElementId.SweeperIntoHairpin,
            TrackPatternType.Hairpin => SemanticElementId.Hairpin,
            TrackPatternType.AlternatingRadiusSequence => SemanticElementId.AlternatingRadiusSequence,
            TrackPatternType.FullPipe => SemanticElementId.FullPipe,
            TrackPatternType.WallrideTurn => SemanticElementId.WallrideTurn,
            _ => SemanticElementId.None
        };

        /// <summary>
        /// Stamps defs[from..] with the element identity. Only fills
        /// <see cref="SemanticElementId.None"/> — a def a pattern stamped more
        /// precisely itself is never overwritten. Recovery straights keep their own
        /// structural identity.
        /// </summary>
        public static void StampRange(List<TrackMacroSectionDefinition> defs, int from, SemanticElementId element)
        {
            for (int i = from; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null || d.SemanticElement != SemanticElementId.None) continue;
                d.SemanticElement = d.SectionType == TrackMacroSectionType.RecoveryStraight
                    ? SemanticElementId.RecoverySection
                    : element;
            }
        }

        // ─────────────────────────── Plan-result measurement ───────────────────────────

        /// <summary>
        /// Measures the exact achieved result of defs[from..to) by building their
        /// frames from <paramref name="entry"/> with the SAME dispatch the candidate
        /// builder uses. Deterministic, zero rng draws, no side effects on the defs.
        ///
        /// Air gaps advance the frame via the shared ballistic-arrival math
        /// (<see cref="SectionFrameBuilders.AirGapLanding"/>). The landing-ramp grade
        /// snap is a candidate-level weld correction (centimeters, inside rulebook
        /// tolerance) and is intentionally not replicated here.
        /// </summary>
        public static FeaturePlanResult ComputePlanResult(
            List<TrackMacroSectionDefinition> defs, int from, int to,
            in TrackConnectionFrame entry, in FrameBuildContext ctx,
            SemanticElementId element, string patternId)
        {
            var result = new FeaturePlanResult
            {
                Element = element,
                PatternId = patternId ?? "",
                Definitions = defs
            };

            if (defs == null || from < 0 || to > defs.Count || from >= to)
            {
                result.FailureReason = "Empty or invalid definition range.";
                return result;
            }

            // Entry basis for relative measurement (plan view uses the FLATTENED axes:
            // the topology contract is about plan heading, not the entry's pitch).
            Vector3 entryPos = entry.Position;
            Vector3 fwdH = SectionFrameBuilders.Flatten(entry.Forward);
            Vector3 rightH = SectionFrameBuilders.Flatten(entry.Right);
            Quaternion entryRot = Quaternion.LookRotation(entry.Forward, entry.Up);

            var frame = entry;
            float minY = 0f, maxY = 0f, arc = 0f;
            float rollStart = frame.AccumulatedRoadRoll;
            bool boundsInit = false;
            Bounds bounds = default;

            void Encapsulate(in TrackConnectionFrame f)
            {
                Vector3 local = Quaternion.Inverse(entryRot) * (f.Position - entryPos);
                Vector3 half = new Vector3(f.Width * 0.5f, f.Width * 0.5f, 0f);
                if (!boundsInit) { bounds = new Bounds(local, Vector3.zero); boundsInit = true; }
                bounds.Encapsulate(local + half);
                bounds.Encapsulate(local - half);
            }

            for (int i = from; i < to; i++)
            {
                var def = defs[i];
                if (def == null) continue;

                if (def.SectionType == TrackMacroSectionType.AirGap)
                {
                    Encapsulate(frame);
                    frame = SectionFrameBuilders.AirGapLanding(frame, def);
                    arc += def.Length;
                    Encapsulate(frame);
                    minY = Mathf.Min(minY, frame.Position.y - entryPos.y);
                    maxY = Mathf.Max(maxY, frame.Position.y - entryPos.y);
                    continue;
                }

                TrackConnectionFrame[] frames = SectionFrameBuilders.BuildSectionFrames(frame, def, ctx);
                if (frames == null || frames.Length == 0)
                {
                    result.FailureReason = $"Builder produced no frames for '{def.DebugName}' ({def.SectionType}).";
                    return result;
                }

                for (int k = 0; k < frames.Length; k++)
                {
                    float y = frames[k].Position.y - entryPos.y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                    Encapsulate(frames[k]);
                }

                arc += frames[frames.Length - 1].ArcLength - frames[0].ArcLength;
                frame = frames[frames.Length - 1];
            }

            // Relative exit.
            Vector3 delta = frame.Position - entryPos;
            result.RelativeExitPosition = Quaternion.Inverse(entryRot) * delta;
            result.RelativeExitRotation = Quaternion.Inverse(entryRot) *
                Quaternion.LookRotation(frame.Forward, frame.Up);

            var exitLocal = frame;
            exitLocal.Position = result.RelativeExitPosition;
            exitLocal.Forward = Quaternion.Inverse(entryRot) * frame.Forward;
            exitLocal.Right = Quaternion.Inverse(entryRot) * frame.Right;
            exitLocal.Up = Quaternion.Inverse(entryRot) * frame.Up;
            result.ExitState = exitLocal;

            result.PlanDisplacement = new Vector2(Vector3.Dot(delta, rightH), Vector3.Dot(delta, fwdH));
            result.HeadingContributionDeg = Vector3.SignedAngle(
                fwdH, SectionFrameBuilders.Flatten(frame.Forward), Vector3.up);
            result.ElevationChange = delta.y;
            result.MinElevation = minY;
            result.MaxElevation = maxY;
            result.ArcLength = arc;
            result.SweptFootprint = bounds;
            result.AccumulatedRollDeg = frame.AccumulatedRoadRoll - rollStart;
            return result;
        }
    }
}
