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
        }

        public static void Apply(List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            if (sections == null || sections.Count == 0 || cfg?.RoadProfile == null) return;

            foreach (List<GeneratedTrackSection> chain in CollectChains(sections))
            {
                List<Node> nodes = BuildNodes(chain, cfg);
                if (nodes.Count < 2) continue;

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

                float transition = Mathf.Max(1f,
                    Mathf.Max(cfg.WidthTransitionLength, cfg.CrossSectionTransitionLength));
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
                            SideHeightLocked = protectedShape
                        };
                        nodes.Add(node);
                        previous = node;
                    }

                    node.Locations.Add(new Location { Section = section, Ring = r });
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
