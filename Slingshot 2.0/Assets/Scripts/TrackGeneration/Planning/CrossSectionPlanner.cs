using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Produces one canonical width/side-height sample at every physical ring. Changes
    /// are constrained by travelled distance in both directions, so a narrow curve or
    /// deep turn prepares its neighbours instead of creating a one-section wall wave.
    /// Authored inversion/wallride/pipe samples remain hard anchors.
    /// </summary>
    public static class CrossSectionPlanner
    {
        private const float WeldToleranceSqr = 0.0025f;

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

                float transitionLength = Mathf.Max(1f,
                    Mathf.Max(cfg.WidthTransitionLength, cfg.CrossSectionTransitionLength));

                // V2.1: resolve pass-through wall-height targets from the anchors around them
                // BEFORE rate limiting — a rate limiter faithfully tracks whatever targets it is
                // given, so an incidental neutral target survives it as a visible valley.
                BlendPassThroughHeights(nodes, transitionLength);

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
            return output;
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

                // V2.1: an authored shape, or ordinary road asking for a distinct wall height,
                // is a real destination. Everything else is pass-through (blended, not asserted).
                bool heightAnchor = protectedShape ||
                    Mathf.Abs(TrackCandidateBuilder.DepthMultiplier(section.Definition.SectionType) - 1f) > 0.001f;

                for (int r = 0; r < section.SubdivisionFrames.Length; r++)
                {
                    TrackConnectionFrame frame = section.SubdivisionFrames[r];
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
                            SideHeightLocked = protectedShape,
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
                        node.DesiredWidth = frame.Width;
                        node.DesiredSideHeight = targetHeight;
                        node.WidthLocked = true;
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
