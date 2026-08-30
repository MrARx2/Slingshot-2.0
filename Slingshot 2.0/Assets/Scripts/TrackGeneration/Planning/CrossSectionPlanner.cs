using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Produces one canonical width/side-height sample at every physical ring. Changes
    /// are constrained by travelled distance in both directions, so a narrow curve or
    /// deep turn prepares its neighbours instead of creating a one-section wall wave.
    /// Authored inversion/wallride/pipe interiors remain hard anchors; their mouth
    /// zones participate in the global blend so two protected features can meet
    /// without forcing a one-ring wall-height step.
    /// </summary>
    public static class CrossSectionPlanner
    {
        private const float WeldToleranceSqr = 0.0025f;

        private enum WallChannel
        {
            Width,
            SideHeight,
            LeftWallSuppression,
            RightWallSuppression,
            TurnRounding,
            LeftOverhang,
            RightOverhang,
            PipeClosure,
            WallrideMorph
        }

        private sealed class Location
        {
            public GeneratedTrackSection Section;
            public int Ring;
        }

        private sealed class Node
        {
            public readonly List<Location> Locations = new List<Location>(2);
            public Vector3 Position;
            public float Distance;
            public float DesiredWidth;
            public float DesiredSideHeight;
            public bool WidthLocked;
            public bool SideHeightLocked;

            /// <summary>
            /// V2.1: this node's wall height is a real design destination — an authored feature
            /// (locked) or ordinary road that intentionally asks for a distinct height (banked
            /// curve 1.08, hairpin 1.10). Pass-through nodes are NOT destinations and are blended
            /// between the anchors around them.
            /// </summary>
            public bool HeightAnchor;
        }

        public static void Apply(List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            if (sections == null || sections.Count == 0 || cfg?.RoadProfile == null) return;

            foreach (List<GeneratedTrackSection> chain in CollectChains(sections))
            {
                List<Node> nodes = BuildNodes(chain, cfg);
                if (nodes.Count < 2) continue;

                bool closed = IsClosedChain(chain);

                float transitionLength = Mathf.Max(1f,
                    Mathf.Max(cfg.WidthTransitionLength, cfg.CrossSectionTransitionLength));

                // V2.1: resolve pass-through wall-height targets from the anchors around them
                // BEFORE rate limiting — a rate limiter faithfully tracks whatever targets it is
                // given, so an incidental neutral target survives it as a visible valley.
                BlendPassThroughHeights(nodes, transitionLength);

                // A lap is cyclic, but its first and last subdivision frames are two
                // serialized copies of the same physical weld. Treat that seam as one
                // design boundary before rate limiting. Previously those copies could
                // independently request (for example) normal and banked wall heights,
                // producing the recurring 2.08 m section N -> 0 discontinuity.
                if (closed)
                    ShareClosedEndpointContract(nodes);

                var widths = new float[nodes.Count];
                var heights = new float[nodes.Count];
                var widthLocks = new bool[nodes.Count];
                var heightLocks = new bool[nodes.Count];
                for (int i = 0; i < nodes.Count; i++)
                {
                    widths[i] = nodes[i].DesiredWidth;
                    heights[i] = nodes[i].DesiredSideHeight;
                    widthLocks[i] = nodes[i].WidthLocked;
                    heightLocks[i] = nodes[i].SideHeightLocked;
                }

                float transition = transitionLength;
                float maxWidthRate = Mathf.Min(0.12f,
                    Mathf.Max(0.025f, cfg.RoadWidth / transition));
                float maxHeightRate = Mathf.Min(0.08f,
                    Mathf.Max(0.01f, cfg.RoadProfile.SideHeight / transition));

                ProjectRateLimits(nodes, widths, widthLocks, maxWidthRate);
                ProjectRateLimits(nodes, heights, heightLocks, maxHeightRate);

                for (int i = 0; i < nodes.Count; i++)
                {
                    Node node = nodes[i];
                    foreach (Location location in node.Locations)
                    {
                        TrackConnectionFrame frame = location.Section.SubdivisionFrames[location.Ring];
                        frame.Width = Mathf.Max(1f, widths[i]);
                        frame.SideHeight = Mathf.Max(0.1f, heights[i]);
                        location.Section.SubdivisionFrames[location.Ring] = frame;
                    }
                }

                foreach (GeneratedTrackSection section in chain)
                {
                    section.StartFrame = section.SubdivisionFrames[0];
                    section.EndFrame = section.SubdivisionFrames[section.SubdivisionFrames.Length - 1];
                }
            }
        }

        /// <summary>
        /// Final C1 finishing pass for the actual ring grid consumed by both the render
        /// mesh and collider. The canonical planner above guarantees matching values at
        /// a weld (C0), but a protected feature can hold a constant authored wall while
        /// its ordinary connector arrives with a non-zero slope. That is a legal weld
        /// which still reads and drives as a dent.
        ///
        /// This pass keeps the shared ring and every authored feature core exact. It
        /// changes only the ordinary road beside a protected feature; between two
        /// ordinary sections it shares the correction across both sides. Corrections
        /// taper to zero value and zero slope inside each section, so no new seam is
        /// moved farther away. Open ends, air gaps and route forks are separate chains
        /// and are intentionally untouched.
        /// </summary>
        public static void HarmonizeFinalWallJoins(List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            if (sections == null || sections.Count == 0 || cfg?.RoadProfile == null) return;

            float configuredWindow = Mathf.Clamp(
                Mathf.Max(cfg.WidthTransitionLength, cfg.CrossSectionTransitionLength) * 0.35f,
                12f, 120f);

            foreach (List<GeneratedTrackSection> chain in CollectChains(sections))
            {
                if (chain.Count < 2) continue;
                bool closed = IsClosedChain(chain);
                int joinCount = closed ? chain.Count : chain.Count - 1;

                for (int i = 0; i < joinCount; i++)
                {
                    GeneratedTrackSection before = chain[i];
                    GeneratedTrackSection after = chain[(i + 1) % chain.Count];
                    if (!CanWeld(before, after)) continue;

                    bool beforeProtected = IsProtected(before.Definition.SectionType);
                    bool afterProtected = IsProtected(after.Definition.SectionType);

                    // Retopology already emits a shared ring. Reconcile the scalar
                    // copies defensively because a later feature finishing pass may
                    // have touched only one serialized copy.
                    ShareBoundaryWallContract(before, after, beforeProtected, afterProtected);

                    float beforeLength = SectionArcLength(before);
                    float afterLength = SectionArcLength(after);
                    bool bothProtected = beforeProtected && afterProtected;
                    // Protected-to-protected wall DEPTH already owns a deliberately
                    // blendable mouth from the canonical pass. Finish that mouth, but
                    // never touch the feature-specific width/fold/overhang channels.
                    float sectionFraction = bothProtected ? 0.25f : 0.45f;
                    float beforeWindow = Mathf.Min(configuredWindow, beforeLength * sectionFraction);
                    float afterWindow = Mathf.Min(configuredWindow, afterLength * sectionFraction);

                    foreach (WallChannel channel in System.Enum.GetValues(typeof(WallChannel)))
                    {
                        if (bothProtected && channel != WallChannel.SideHeight) continue;

                        float slopeBefore = EndSlope(before, channel);
                        float slopeAfter = StartSlope(after, channel);
                        float sharedSlope = beforeProtected && !bothProtected
                            ? slopeBefore
                            : afterProtected && !bothProtected
                                ? slopeAfter
                                : 0.5f * (slopeBefore + slopeAfter);

                        if ((!beforeProtected || bothProtected) && beforeWindow >= 2f)
                            EaseSlopeAtEnd(before, channel, sharedSlope - slopeBefore, beforeWindow);
                        if ((!afterProtected || bothProtected) && afterWindow >= 2f)
                            EaseSlopeAtStart(after, channel, sharedSlope - slopeAfter, afterWindow);
                    }
                }

                // Corrections from neighboring joins can overlap on a very short
                // ordinary section. The 45% per-side window prevents those envelopes
                // from crossing; refresh the public boundary copies once both ends are
                // complete.
                foreach (GeneratedTrackSection section in chain)
                {
                    TrackConnectionFrame[] frames = section.SubdivisionFrames;
                    section.StartFrame = frames[0];
                    section.EndFrame = frames[frames.Length - 1];
                }
            }
        }

        private static float SectionArcLength(GeneratedTrackSection section)
        {
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            return Mathf.Max(0f, frames[frames.Length - 1].ArcLength - frames[0].ArcLength);
        }

        private static void ShareBoundaryWallContract(GeneratedTrackSection before,
            GeneratedTrackSection after, bool beforeProtected, bool afterProtected)
        {
            TrackConnectionFrame[] left = before.SubdivisionFrames;
            TrackConnectionFrame[] right = after.SubdivisionFrames;
            int last = left.Length - 1;
            TrackConnectionFrame a = left[last];
            TrackConnectionFrame b = right[0];

            foreach (WallChannel channel in System.Enum.GetValues(typeof(WallChannel)))
            {
                float value = afterProtected
                    ? Get(b, channel)
                    : beforeProtected
                        ? Get(a, channel)
                        : 0.5f * (Get(a, channel) + Get(b, channel));
                a = Set(a, channel, value);
                b = Set(b, channel, value);
            }

            left[last] = a;
            right[0] = b;
            before.EndFrame = a;
            after.StartFrame = b;
        }

        private static float EndSlope(GeneratedTrackSection section, WallChannel channel)
        {
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            int end = frames.Length - 1;
            float ds = Mathf.Max(0.001f, frames[end].ArcLength - frames[end - 1].ArcLength);
            return (Get(frames[end], channel) - Get(frames[end - 1], channel)) / ds;
        }

        private static float StartSlope(GeneratedTrackSection section, WallChannel channel)
        {
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            float ds = Mathf.Max(0.001f, frames[1].ArcLength - frames[0].ArcLength);
            return (Get(frames[1], channel) - Get(frames[0], channel)) / ds;
        }

        private static void EaseSlopeAtEnd(GeneratedTrackSection section, WallChannel channel,
            float slopeCorrection, float window)
        {
            if (Mathf.Abs(slopeCorrection) < 1e-7f) return;
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            float boundary = frames[frames.Length - 1].ArcLength;

            for (int i = frames.Length - 2; i >= 0; i--)
            {
                float distance = boundary - frames[i].ArcLength;
                if (distance >= window) break;
                float t = 1f - Mathf.Clamp01(distance / window); // far edge 0, weld 1
                float hermite = t * t * (t - 1f);                // h11: value 0 at both ends
                frames[i] = Set(frames[i], channel,
                    Get(frames[i], channel) + hermite * slopeCorrection * window);
            }
        }

        private static void EaseSlopeAtStart(GeneratedTrackSection section, WallChannel channel,
            float slopeCorrection, float window)
        {
            if (Mathf.Abs(slopeCorrection) < 1e-7f) return;
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            float boundary = frames[0].ArcLength;

            for (int i = 1; i < frames.Length; i++)
            {
                float distance = frames[i].ArcLength - boundary;
                if (distance >= window) break;
                float t = Mathf.Clamp01(distance / window);      // weld 0, far edge 1
                float hermite = t * (t - 1f) * (t - 1f);         // h10: value 0 at both ends
                frames[i] = Set(frames[i], channel,
                    Get(frames[i], channel) + hermite * slopeCorrection * window);
            }
        }

        private static float Get(in TrackConnectionFrame frame, WallChannel channel)
        {
            switch (channel)
            {
                case WallChannel.Width: return frame.Width;
                case WallChannel.SideHeight: return frame.SideHeight;
                case WallChannel.LeftWallSuppression: return frame.LeftWallSuppression;
                case WallChannel.RightWallSuppression: return frame.RightWallSuppression;
                case WallChannel.TurnRounding: return frame.TurnRounding;
                case WallChannel.LeftOverhang: return frame.LeftOverhang;
                case WallChannel.RightOverhang: return frame.RightOverhang;
                case WallChannel.PipeClosure: return frame.PipeClosure;
                case WallChannel.WallrideMorph: return frame.WallrideMorph;
                default: return 0f;
            }
        }

        private static TrackConnectionFrame Set(TrackConnectionFrame frame,
            WallChannel channel, float value)
        {
            switch (channel)
            {
                case WallChannel.Width:
                    frame.Width = Mathf.Max(1f, value);
                    break;
                case WallChannel.SideHeight:
                    frame.SideHeight = Mathf.Max(0.1f, value);
                    break;
                case WallChannel.LeftWallSuppression:
                    frame.LeftWallSuppression = Mathf.Clamp(value, -2f, 1f);
                    break;
                case WallChannel.RightWallSuppression:
                    frame.RightWallSuppression = Mathf.Clamp(value, -2f, 1f);
                    break;
                case WallChannel.TurnRounding:
                    frame.TurnRounding = Mathf.Clamp01(value);
                    break;
                case WallChannel.LeftOverhang:
                    frame.LeftOverhang = Mathf.Clamp01(value);
                    break;
                case WallChannel.RightOverhang:
                    frame.RightOverhang = Mathf.Clamp01(value);
                    break;
                case WallChannel.PipeClosure:
                    frame.PipeClosure = Mathf.Clamp01(value);
                    break;
                case WallChannel.WallrideMorph:
                    frame.WallrideMorph = Mathf.Clamp01(value);
                    break;
            }
            return frame;
        }

        private static List<List<GeneratedTrackSection>> CollectChains(
            List<GeneratedTrackSection> sections)
        {
            var output = new List<List<GeneratedTrackSection>>();
            List<GeneratedTrackSection> chain = null;
            GeneratedTrackSection previous = null;

            foreach (GeneratedTrackSection section in sections)
            {
                bool usable = section != null && !section.IsEmptySpace &&
                              section.SubdivisionFrames != null && section.SubdivisionFrames.Length > 1;
                bool welded = usable && previous != null && !previous.OpenEnd && !section.OpenStart &&
                              previous.RoadId == section.RoadId &&
                              (previous.EndFrame.Position - section.StartFrame.Position).sqrMagnitude <= WeldToleranceSqr;
                if (!usable)
                {
                    chain = null;
                    previous = null;
                    continue;
                }
                if (!welded)
                {
                    chain = new List<GeneratedTrackSection>();
                    output.Add(chain);
                }
                chain.Add(section);
                previous = section;
            }

            // The canonical road may be split at the list boundary even though the
            // physical road is continuous there. Join those pieces so blending and
            // rate limiting can see road on both sides of the lap seam.
            if (output.Count > 1)
            {
                List<GeneratedTrackSection> first = output[0];
                List<GeneratedTrackSection> last = output[output.Count - 1];
                if (CanWeld(last[last.Count - 1], first[0]))
                {
                    last.AddRange(first);
                    output.RemoveAt(0);
                }
            }
            return output;
        }

        private static bool CanWeld(GeneratedTrackSection before,
            GeneratedTrackSection after) =>
            before != null && after != null &&
            !before.IsEmptySpace && !after.IsEmptySpace &&
            before.SubdivisionFrames != null && before.SubdivisionFrames.Length > 1 &&
            after.SubdivisionFrames != null && after.SubdivisionFrames.Length > 1 &&
            !before.OpenEnd && !after.OpenStart &&
            before.RoadId == after.RoadId &&
            (before.EndFrame.Position - after.StartFrame.Position).sqrMagnitude <= WeldToleranceSqr;

        private static bool IsClosedChain(List<GeneratedTrackSection> chain) =>
            chain != null && chain.Count > 0 &&
            CanWeld(chain[chain.Count - 1], chain[0]);

        private static void ShareClosedEndpointContract(List<Node> nodes)
        {
            if (nodes == null || nodes.Count < 2) return;
            Node first = nodes[0];
            Node last = nodes[nodes.Count - 1];
            if ((first.Position - last.Position).sqrMagnitude > WeldToleranceSqr) return;

            float width = SharedValue(first.DesiredWidth, first.WidthLocked,
                last.DesiredWidth, last.WidthLocked);
            float height = SharedValue(first.DesiredSideHeight, first.SideHeightLocked,
                last.DesiredSideHeight, last.SideHeightLocked);

            first.DesiredWidth = last.DesiredWidth = width;
            first.DesiredSideHeight = last.DesiredSideHeight = height;
            // Lock both serialized copies to their shared contract. The alternating
            // projection then propagates the same legal transition inward from both
            // sides instead of letting either endpoint drift away again.
            first.WidthLocked = last.WidthLocked = true;
            first.SideHeightLocked = last.SideHeightLocked = true;
            first.HeightAnchor = last.HeightAnchor = true;
        }

        private static float SharedValue(float a, bool aLocked, float b, bool bLocked)
        {
            // Match the internal-weld ownership rule: the section after a weld owns
            // the shared boundary when it is authored. At the lap seam that is the
            // first section. This preserves its exact feature mouth rather than
            // averaging two protected contracts into a shape authored by neither.
            if (aLocked) return a;
            if (bLocked) return b;
            return 0.5f * (a + b);
        }

        private static List<Node> BuildNodes(List<GeneratedTrackSection> chain,
            ResolvedTrackGenerationConfig cfg)
        {
            var nodes = new List<Node>();
            float distance = 0f;
            Node previous = null;

            for (int s = 0; s < chain.Count; s++)
            {
                GeneratedTrackSection section = chain[s];
                bool protectedShape = IsProtected(section.Definition.SectionType);
                float multiplier = TargetMultiplier(chain, s);
                float targetHeight = cfg.RoadProfile.SideHeight * multiplier;
                TrackConnectionFrame[] sectionFrames = section.SubdivisionFrames;
                float sectionStart = sectionFrames[0].ArcLength;
                float sectionEnd = sectionFrames[sectionFrames.Length - 1].ArcLength;
                float sectionLength = Mathf.Max(0f, sectionEnd - sectionStart);
                float mouthLength = Mathf.Min(sectionLength * 0.25f,
                    Mathf.Clamp(Mathf.Max(cfg.WidthTransitionLength,
                        cfg.CrossSectionTransitionLength) * 0.35f, 12f, 120f));

                // V2.1: an authored shape, or ordinary road asking for a distinct wall height,
                // is a real destination. Everything else is pass-through (blended, not asserted).
                bool ordinaryHeightTarget = !protectedShape &&
                    Mathf.Abs(TrackCandidateBuilder.DepthMultiplier(section.Definition.SectionType) - 1f) > 0.001f;

                for (int r = 0; r < section.SubdivisionFrames.Length; r++)
                {
                    TrackConnectionFrame frame = section.SubdivisionFrames[r];
                    float fromMouth = Mathf.Min(frame.ArcLength - sectionStart,
                        sectionEnd - frame.ArcLength);
                    // The feature body is exact. Its entry/exit quarter is a transition
                    // mouth, not a separate wall-height destination; otherwise two
                    // directly welded protected features can differ by their complete
                    // depth step in one final ring.
                    bool protectedCore = protectedShape && fromMouth >= mouthLength - 0.001f;
                    bool heightAnchor = protectedCore || ordinaryHeightTarget;
                    bool duplicate = previous != null &&
                                     (previous.Position - frame.Position).sqrMagnitude <= WeldToleranceSqr;
                    Node node;
                    if (duplicate)
                    {
                        node = previous;
                    }
                    else
                    {
                        if (previous != null)
                            distance += Vector3.Distance(previous.Position, frame.Position);
                        node = new Node
                        {
                            Position = frame.Position,
                            Distance = distance,
                            DesiredWidth = frame.Width,
                            DesiredSideHeight = targetHeight,
                            WidthLocked = protectedShape,
                            SideHeightLocked = protectedCore,
                            HeightAnchor = heightAnchor
                        };
                        nodes.Add(node);
                        previous = node;
                    }

                    node.Locations.Add(new Location { Section = section, Ring = r });
                    // A welded boundary node shared with an anchor section stays an anchor.
                    if (heightAnchor) node.HeightAnchor = true;
                    if (protectedShape)
                    {
                        // Structural feature width remains exact all the way through
                        // the mouth. Only generic wall depth is allowed to blend.
                        node.DesiredWidth = frame.Width;
                        node.WidthLocked = true;
                    }
                    if (protectedCore)
                    {
                        node.DesiredSideHeight = targetHeight;
                        node.SideHeightLocked = true;
                    }
                }
            }
            return nodes;
        }

        /// <summary>
        /// V2.1 — removes INCIDENTAL wall-height destinations without removing intentional ones.
        ///
        /// A pass-through section (plain straight, connector, approach, recovery) has no design
        /// reason to reach the neutral wall height; asserting it turns
        ///   tall anchor → neutral → tall anchor
        /// into a visible valley, which is what made section boundaries readable as "waves".
        /// Between two height anchors a pass-through node therefore follows an eased A→B morph,
        /// and only relaxes toward its own (neutral) target where it is more than one transition
        /// length from BOTH anchors — i.e. exactly where a genuine neutral plateau fits:
        ///     transition out (Lt) → neutral plateau → transition in (Lt).
        ///
        /// The weight is distance-based and eased, so behaviour is continuous in run length:
        /// a 1082 m run and a 1084 m run produce almost identical geometry (no threshold cliff).
        ///
        /// Anchors are never modified — authored features keep their exact authored profile, and
        /// intentional variation (banked curve 1.08, hairpin 1.10) is preserved, so
        /// `Normal → BankedCurve → Normal` still visibly rises.
        /// </summary>
        private static void BlendPassThroughHeights(List<Node> nodes, float transitionLength)
        {
            float lt = Mathf.Max(1f, transitionLength);
            int n = nodes.Count;

            int i = 0;
            while (i < n)
            {
                if (nodes[i].HeightAnchor) { i++; continue; }

                int start = i;
                int end = i;
                while (end + 1 < n && !nodes[end + 1].HeightAnchor) end++;

                int aIdx = start - 1;
                int bIdx = end + 1;
                bool hasA = aIdx >= 0;
                bool hasB = bIdx < n;

                // No anchor anywhere on this chain — nothing to blend toward, keep own targets.
                if (!hasA && !hasB) { i = end + 1; continue; }

                float aVal = hasA ? nodes[aIdx].DesiredSideHeight : nodes[bIdx].DesiredSideHeight;
                float bVal = hasB ? nodes[bIdx].DesiredSideHeight : aVal;
                float aDist = hasA ? nodes[aIdx].Distance : nodes[start].Distance;
                float bDist = hasB ? nodes[bIdx].Distance : nodes[end].Distance;
                float runLength = Mathf.Max(0.001f, bDist - aDist);

                // Does a real neutral PLATEAU fit at all?  The run must pay for a transition out
                // and a transition in (2 x Lt) before any distance is left to hold neutral; a full
                // Lt of hold earns the neutral target completely. Without this the local distance
                // test alone lets a 510 m run reach ~79% of the way to neutral, which is precisely
                // the incidental valley V2.1 exists to remove. Eased, so behaviour stays continuous
                // in run length (no cliff at the 3 x Lt mark).
                float plateau = (hasA && hasB) ? runLength - 2f * lt : float.MaxValue;
                float spanEarnsNeutral = SectionFrameBuilders.Smooth01(Mathf.Clamp01(plateau / lt));

                for (int k = start; k <= end; k++)
                {
                    Node node = nodes[k];

                    // Distance to each bounding anchor (an open end constrains nothing).
                    float fromA = hasA ? node.Distance - aDist : float.MaxValue;
                    float fromB = hasB ? bDist - node.Distance : float.MaxValue;

                    // The single A→B morph this span should read as.
                    float anchorValue = hasA && hasB
                        ? Mathf.Lerp(aVal, bVal, SectionFrameBuilders.Smooth01(
                            Mathf.Clamp01((node.Distance - aDist) / runLength)))
                        : (hasA ? aVal : bVal);

                    // How much this node may relax to its own neutral target: 0 at an anchor,
                    // 1 only once a full transition length from BOTH sides — AND only if the run
                    // is long enough to hold a real neutral plateau at all.
                    float neutralWeight = SectionFrameBuilders.Smooth01(
                        Mathf.Clamp01(Mathf.Min(fromA, fromB) / lt)) * spanEarnsNeutral;

                    node.DesiredSideHeight =
                        Mathf.Lerp(anchorValue, node.DesiredSideHeight, neutralWeight);
                }

                i = end + 1;
            }
        }

        private static float TargetMultiplier(List<GeneratedTrackSection> chain, int index)
        {
            GeneratedTrackSection section = chain[index];
            float own = TrackCandidateBuilder.DepthMultiplier(section.Definition.SectionType);
            float carry = Mathf.Clamp01(section.Definition.BridgeCarry);
            if (carry < 0.001f) return own;

            float before = index > 0
                ? TrackCandidateBuilder.DepthMultiplier(chain[index - 1].Definition.SectionType)
                : own;
            float after = index + 1 < chain.Count
                ? TrackCandidateBuilder.DepthMultiplier(chain[index + 1].Definition.SectionType)
                : own;
            float supported = Mathf.Max(own, Mathf.Min(before, after));
            return Mathf.Lerp(own, supported, carry);
        }

        private static bool IsProtected(TrackMacroSectionType type)
        {
            switch (type)
            {
                case TrackMacroSectionType.JumpRamp:
                case TrackMacroSectionType.LandingRamp:
                case TrackMacroSectionType.Loop:
                case TrackMacroSectionType.Corkscrew:
                case TrackMacroSectionType.Spiral:
                case TrackMacroSectionType.HalfLoopTwist:
                case TrackMacroSectionType.FullPipe:
                case TrackMacroSectionType.WallrideTurn:
                case TrackMacroSectionType.RotationalEvent:
                    return true;
                default:
                    return false;
            }
        }

        private static void ProjectRateLimits(List<Node> nodes, float[] values,
            bool[] locked, float maxRate)
        {
            // Alternating projections allow a hard authored sample to influence both
            // approaches. Re-applying the lock after each sweep prevents smoothing an
            // inversion or wallride merely to satisfy an ordinary-road preference.
            var targets = (float[])values.Clone();
            for (int pass = 0; pass < 12; pass++)
            {
                for (int i = 1; i < values.Length; i++)
                {
                    float ds = Mathf.Max(0.001f, nodes[i].Distance - nodes[i - 1].Distance);
                    if (!locked[i])
                        values[i] = Mathf.Clamp(values[i], values[i - 1] - maxRate * ds,
                            values[i - 1] + maxRate * ds);
                    else
                        values[i] = targets[i];
                }
                for (int i = values.Length - 2; i >= 0; i--)
                {
                    float ds = Mathf.Max(0.001f, nodes[i + 1].Distance - nodes[i].Distance);
                    if (!locked[i])
                        values[i] = Mathf.Clamp(values[i], values[i + 1] - maxRate * ds,
                            values[i + 1] + maxRate * ds);
                    else
                        values[i] = targets[i];
                }
            }
        }
    }
}
