using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration
{
    /// <summary>
    /// V2.1 STAGE 1.5 — READ-ONLY measurement of cross-section TRANSITION quality.
    ///
    /// Never mutates frames, never consumes random values, never participates in candidate
    /// selection. It exists to quantify the "wavy wall" defect before any geometry change and
    /// to attribute it to a specific channel.
    ///
    /// Per transition span (anchor → anchor) it measures, for every channel:
    ///   • REVERSALS — derivative sign changes (a clean single A→B morph has none)
    ///   • EXCURSION — how far the span left the [A,B] band with no authored reason
    ///                 (the 28.1 → 26.0 → 28.1 valley)
    ///
    /// Channels (the attribution the V2.1 scope decision needs):
    ///   Width, SideHeight            — the planner's own scalars
    ///   BoostL / BoostR              — banking-field wall multiplier per side (containment)
    ///   Bank                         — driving-surface tilt (measured only; V2.1 does NOT carry it)
    ///   TopL/TopR (vert + lat)       — the FINAL COMPOSED wall-top, taken from the real evaluated
    ///                                  cross-section (<see cref="TrackCrossSection.Evaluate"/>), so
    ///                                  width, side height, wall multipliers, overhang, rounding,
    ///                                  pipe closure and wallride morph are all included. This is
    ///                                  measured geometry, NOT a SideHeight×(1+boost) approximation.
    ///
    /// All composed values are in the frame's own local basis (x = lateral along Right,
    /// y = vertical along Up), so forward progress and frame rotation are excluded by construction.
    /// </summary>
    public static class CrossSectionTransitionDiagnostics
    {
        public enum Channel
        {
            Width = 0,
            SideHeight = 1,
            BoostL = 2,
            BoostR = 3,
            Bank = 4,
            TopLVertical = 5,
            TopLLateral = 6,
            TopRVertical = 7,
            TopRLateral = 8
        }

        public const int ChannelCount = 9;

        private static readonly string[] ChannelNames =
        {
            "Width", "SideHeight", "BoostL", "BoostR", "Bank",
            "TopL(vert)", "TopL(lat)", "TopR(vert)", "TopR(lat)"
        };

        private static readonly string[] ChannelUnits =
        { "m", "m", "x", "x", "deg", "m", "m", "m", "m" };

        /// <summary>Noise floor per channel (same order as <see cref="Channel"/>).</summary>
        private static readonly float[] ChannelEpsilon =
        { 0.05f, 0.05f, 0.01f, 0.01f, 0.5f, 0.05f, 0.05f, 0.05f, 0.05f };

        /// <summary>
        /// How a section's cross-section target should be treated. Evaluated PER CHANNEL GROUP —
        /// a section can be authoritative for one channel and incidental for another (an SCurve
        /// owns its path curvature and banking, but requests no distinct wall height).
        /// </summary>
        public enum CrossSectionRole
        {
            /// <summary>Authored feature geometry / structural width. Must remain exact.</summary>
            AuthoredAnchor,

            /// <summary>Ordinary road INTENTIONALLY requesting a distinct target (banked curve, hairpin).</summary>
            DesignTarget,

            /// <summary>Incidental default — must not automatically become a visual destination.</summary>
            PassThrough
        }

        private const float WidthAnchorEpsilon = 1.0f;

        // ─────────────────────────── Classification (per channel group) ───────────────────────────

        /// <summary>Mirrors <c>CrossSectionPlanner.IsProtected</c> (private there). Keep in sync.</summary>
        private static bool IsAuthoredShape(TrackMacroSectionType t)
        {
            switch (t)
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

        private static float SectionWidth(GeneratedTrackSection s)
            => s.SubdivisionFrames is { Length: > 0 } ? s.SubdivisionFrames[0].Width : s.Definition.Width;

        /// <summary>WIDTH channel role.</summary>
        public static CrossSectionRole RoleForWidth(GeneratedTrackSection s, ResolvedTrackGenerationConfig cfg)
        {
            if (s?.Definition == null) return CrossSectionRole.PassThrough;
            if (IsAuthoredShape(s.Definition.SectionType)) return CrossSectionRole.AuthoredAnchor;
            if (Mathf.Abs(SectionWidth(s) - cfg.RoadWidth) > WidthAnchorEpsilon)
                return CrossSectionRole.AuthoredAnchor;   // structural (quarter catch, alternate road)
            return CrossSectionRole.PassThrough;
        }

        /// <summary>
        /// CONTAINMENT channel role (wall height + wall boost). SCurve/Chicane land in PassThrough
        /// here by design: they request no distinct wall height, while remaining authoritative for
        /// their own path curvature and banking elsewhere in the generator.
        /// </summary>
        public static CrossSectionRole RoleForContainment(GeneratedTrackSection s, ResolvedTrackGenerationConfig cfg)
        {
            if (s?.Definition == null) return CrossSectionRole.PassThrough;
            TrackMacroSectionType t = s.Definition.SectionType;
            if (IsAuthoredShape(t)) return CrossSectionRole.AuthoredAnchor;
            if (Mathf.Abs(TrackCandidateBuilder.DepthMultiplier(t) - 1f) > 0.001f)
                return CrossSectionRole.DesignTarget;     // banked curve 1.08, hairpin 1.10
            return CrossSectionRole.PassThrough;
        }

        /// <summary>
        /// Span-decomposition role: a section that is authoritative in ANY channel bounds a span.
        /// Conservative on purpose — measurement must never merge across a real boundary.
        /// </summary>
        public static CrossSectionRole Classify(GeneratedTrackSection s, ResolvedTrackGenerationConfig cfg)
        {
            CrossSectionRole w = RoleForWidth(s, cfg);
            CrossSectionRole c = RoleForContainment(s, cfg);
            if (w == CrossSectionRole.AuthoredAnchor || c == CrossSectionRole.AuthoredAnchor)
                return CrossSectionRole.AuthoredAnchor;
            if (c == CrossSectionRole.DesignTarget) return CrossSectionRole.DesignTarget;
            return CrossSectionRole.PassThrough;
        }

        // ─────────────────────────── Data ───────────────────────────

        public struct ChannelSpan
        {
            public float A, B, Min, Max;
            public int Reversals;
            public float Excursion;
        }

        public sealed class Span
        {
            public string FromAnchor = "", ToAnchor = "", PassThroughNames = "";
            public float LengthMeters;
            public int RingCount;
            public readonly ChannelSpan[] Channels = new ChannelSpan[ChannelCount];
            public float WorstExcursion;

            // ── V2.1 legitimacy classification (SideHeight channel) ──
            /// <summary>Span is long enough for: transition out → meaningful neutral plateau → transition in.</summary>
            public bool QualifiesForNeutralRun;
            /// <summary>Both bounding anchors sit above the neutral wall height.</summary>
            public bool BothAnchorsAboveNeutral;
            /// <summary>Distance this span would need to justify a neutral plateau (3 × transition length).</summary>
            public float MinLengthForNeutralRun;
            /// <summary>Reversals beyond the legitimate allowance (1 for a qualifying long run, else 0).</summary>
            public int UnnecessarySideHeightReversals;
            /// <summary>Excursion with no design justification: an incidental dip on a short span, or
            /// any drop BELOW neutral / rise above both anchors on a qualifying long run.</summary>
            public float UnnecessarySideHeightExcursion;

            public string Classification =>
                !BothAnchorsAboveNeutral ? "straddles-neutral"
                : QualifiesForNeutralRun ? "LONG (neutral run allowed)"
                : "SHORT (no neutral justified)";
        }

        public sealed class Summary
        {
            public int SpanCount;
            public readonly int[] Reversals = new int[ChannelCount];
            public readonly float[] WorstExcursion = new float[ChannelCount];
            public readonly string[] WorstAt = new string[ChannelCount];
            public float WorstLateralRate, WorstVerticalRate, WorstCombinedRate;
            public string WorstLateralAt = "", WorstVerticalAt = "", WorstCombinedAt = "";
            public int Anchors, DesignTargets, PassThroughs;
            public readonly List<Span> Spans = new List<Span>();

            // ── The metrics Stage 2 is judged against ──
            /// <summary>Total SideHeight reversals with no design justification.</summary>
            public int UnnecessarySideHeightReversals;
            /// <summary>Worst SideHeight excursion with no design justification.</summary>
            public float WorstUnnecessarySideHeightExcursion;
            public string WorstUnnecessaryAt = "";
            /// <summary>Span classification counts.</summary>
            public int ShortSpans, LegitimateNeutralRuns, StraddlingSpans;
            /// <summary>Neutral wall height and transition length actually used (from config).</summary>
            public float NeutralSideHeight, TransitionLength, MinLengthForNeutralRun;

            public string Compact()
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                return $"spans={SpanCount}(short {ShortSpans}/long {LegitimateNeutralRuns}/straddle {StraddlingSpans}) " +
                       $"UNNECESSARY[rev={UnnecessarySideHeightReversals} " +
                       $"exc={WorstUnnecessarySideHeightExcursion.ToString("F2", c)}m] " +
                       $"raw exc[SideH={WorstExcursion[(int)Channel.SideHeight].ToString("F2", c)}m " +
                       $"BoostL={WorstExcursion[(int)Channel.BoostL].ToString("F2", c)}x " +
                       $"BoostR={WorstExcursion[(int)Channel.BoostR].ToString("F2", c)}x " +
                       $"TopL={WorstExcursion[(int)Channel.TopLVertical].ToString("F2", c)}m " +
                       $"TopR={WorstExcursion[(int)Channel.TopRVertical].ToString("F2", c)}m " +
                       $"Width={WorstExcursion[(int)Channel.Width].ToString("F2", c)}m " +
                       $"Bank={WorstExcursion[(int)Channel.Bank].ToString("F1", c)}deg] " +
                       $"rev[SideH={Reversals[(int)Channel.SideHeight]} " +
                       $"BoostL={Reversals[(int)Channel.BoostL]} BoostR={Reversals[(int)Channel.BoostR]} " +
                       $"TopL={Reversals[(int)Channel.TopLVertical]} TopR={Reversals[(int)Channel.TopRVertical]}]";
            }
        }

        private struct Ring
        {
            public GeneratedTrackSection Section;
            public CrossSectionRole Role;
            public float Arc;
            public float[] V;                 // channel values
            public Vector2 LeftTop, RightTop; // for rate metrics
            public bool BreaksChain;
        }

        // ─────────────────────────── Measurement ───────────────────────────

        public static Summary Measure(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            var summary = new Summary();
            for (int i = 0; i < ChannelCount; i++) summary.WorstAt[i] = "";
            if (sections == null || sections.Count == 0 || cfg?.RoadProfile == null) return summary;

            TrackRoadProfileSettings profile = cfg.RoadProfile;

            // Legitimacy scale, derived from the EXISTING transition configuration — the same
            // expression CrossSectionPlanner uses for its own transition distance. A neutral
            // plateau is only justified when the span can fit
            //   transition out (Lt) + a meaningful neutral plateau (Lt) + transition in (Lt).
            // 2 x Lt alone would only produce a V-shaped dip with no real neutral road.
            summary.NeutralSideHeight = profile.SideHeight;
            summary.TransitionLength = Mathf.Max(1f,
                Mathf.Max(cfg.WidthTransitionLength, cfg.CrossSectionTransitionLength));
            summary.MinLengthForNeutralRun = 3f * summary.TransitionLength;

            int pointCount = TrackCrossSection.PointCount(profile);
            var buffer = new Vector2[Mathf.Max(8, pointCount)];
            int last = pointCount - 1;

            var rings = new List<Ring>(4096);
            foreach (GeneratedTrackSection sec in sections)
            {
                if (sec?.Definition == null) continue;
                if (sec.IsEmptySpace || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2)
                {
                    if (rings.Count > 0) { Ring b = rings[rings.Count - 1]; b.BreaksChain = true; rings[rings.Count - 1] = b; }
                    continue;
                }

                CrossSectionRole role = Classify(sec, cfg);
                if (role == CrossSectionRole.AuthoredAnchor) summary.Anchors++;
                else if (role == CrossSectionRole.DesignTarget) summary.DesignTargets++;
                else summary.PassThroughs++;

                TrackConnectionFrame[] frames = sec.SubdivisionFrames;
                for (int i = 0; i < frames.Length; i++)
                {
                    TrackConnectionFrame f = frames[i];

                    // FINAL COMPOSED geometry — real evaluated cross-section, not an approximation.
                    TrackCrossSection.Evaluate(profile, f, buffer);
                    Vector2 lTop = buffer[0], rTop = buffer[last];

                    var v = new float[ChannelCount];
                    v[(int)Channel.Width] = f.Width;
                    v[(int)Channel.SideHeight] = f.SideHeight > 0.001f ? f.SideHeight : profile.SideHeight;
                    v[(int)Channel.BoostL] = f.LeftWallMultiplier;
                    v[(int)Channel.BoostR] = f.RightWallMultiplier;
                    v[(int)Channel.Bank] = f.BankAngle;
                    v[(int)Channel.TopLVertical] = lTop.y;
                    v[(int)Channel.TopLLateral] = lTop.x;
                    v[(int)Channel.TopRVertical] = rTop.y;
                    v[(int)Channel.TopRLateral] = rTop.x;

                    rings.Add(new Ring
                    {
                        Section = sec, Role = role, Arc = f.ArcLength, V = v,
                        LeftTop = lTop, RightTop = rTop,
                        BreaksChain = (i == frames.Length - 1) && sec.OpenEnd
                    });
                }
            }
            if (rings.Count < 3) return summary;

            // Composed wall-top RATES (kept: shows the authored features are the rate hotspots).
            for (int i = 1; i < rings.Count; i++)
            {
                if (rings[i - 1].BreaksChain) continue;
                float ds = Mathf.Max(0.5f, rings[i].Arc - rings[i - 1].Arc);
                if (ds > 200f) continue;

                void Track(Vector2 a, Vector2 b, string side)
                {
                    float lat = Mathf.Abs(b.x - a.x) / ds, vert = Mathf.Abs(b.y - a.y) / ds;
                    float comb = (b - a).magnitude / ds;
                    string at = $"[{rings[i].Section.SectionIndex:D3}] {rings[i].Section.Definition.DebugName} {side} arc {rings[i].Arc:F0}m";
                    if (lat > summary.WorstLateralRate) { summary.WorstLateralRate = lat; summary.WorstLateralAt = at; }
                    if (vert > summary.WorstVerticalRate) { summary.WorstVerticalRate = vert; summary.WorstVerticalAt = at; }
                    if (comb > summary.WorstCombinedRate) { summary.WorstCombinedRate = comb; summary.WorstCombinedAt = at; }
                }
                Track(rings[i - 1].LeftTop, rings[i].LeftTop, "L");
                Track(rings[i - 1].RightTop, rings[i].RightTop, "R");
            }

            // Spans: runs of PassThrough rings bounded by anchors/design targets.
            int idx = 0;
            while (idx < rings.Count)
            {
                if (rings[idx].Role == CrossSectionRole.PassThrough) { idx++; continue; }

                int anchorEnd = idx;
                while (anchorEnd + 1 < rings.Count &&
                       rings[anchorEnd + 1].Role != CrossSectionRole.PassThrough &&
                       !rings[anchorEnd].BreaksChain) anchorEnd++;

                int spanStart = anchorEnd + 1;
                if (spanStart >= rings.Count || rings[anchorEnd].BreaksChain) { idx = anchorEnd + 1; continue; }

                int spanEnd = spanStart;
                bool broke = false;
                while (spanEnd < rings.Count && rings[spanEnd].Role == CrossSectionRole.PassThrough)
                {
                    if (rings[spanEnd].BreaksChain) { broke = true; break; }
                    spanEnd++;
                }
                if (broke || spanEnd >= rings.Count) { idx = spanEnd + 1; continue; }

                AddSpan(summary, rings, anchorEnd, spanStart, spanEnd);
                idx = spanEnd;
            }

            return summary;
        }

        private static void AddSpan(Summary summary, List<Ring> rings, int aIdx, int spanStart, int bIdx)
        {
            var span = new Span
            {
                FromAnchor = $"[{rings[aIdx].Section.SectionIndex:D3}] {rings[aIdx].Section.Definition.DebugName}",
                ToAnchor = $"[{rings[bIdx].Section.SectionIndex:D3}] {rings[bIdx].Section.Definition.DebugName}",
                LengthMeters = Mathf.Abs(rings[bIdx].Arc - rings[aIdx].Arc),
                RingCount = bIdx - spanStart
            };

            var names = new List<string>();
            for (int i = spanStart; i < bIdx; i++)
            {
                string n = rings[i].Section.Definition.DebugName;
                if (names.Count == 0 || names[names.Count - 1] != n) names.Add(n);
            }
            span.PassThroughNames = string.Join(" → ", names);

            for (int ch = 0; ch < ChannelCount; ch++)
            {
                ChannelSpan cs = MeasureChannel(rings, aIdx, spanStart, bIdx, ch);
                span.Channels[ch] = cs;
                summary.Reversals[ch] += cs.Reversals;
                if (cs.Excursion > summary.WorstExcursion[ch])
                {
                    summary.WorstExcursion[ch] = cs.Excursion;
                    summary.WorstAt[ch] = $"{span.FromAnchor} → {span.ToAnchor}";
                }
                // Rank spans by the FINAL COMPOSED wall excursion — that is what is visible.
                if (ch == (int)Channel.TopLVertical || ch == (int)Channel.TopRVertical)
                    span.WorstExcursion = Mathf.Max(span.WorstExcursion, cs.Excursion);
            }

            // ── V2.1 legitimacy: separate an intentional neutral run from an incidental valley ──
            ChannelSpan sh = span.Channels[(int)Channel.SideHeight];
            float neutral = summary.NeutralSideHeight;
            float eps = ChannelEpsilon[(int)Channel.SideHeight];

            span.MinLengthForNeutralRun = summary.MinLengthForNeutralRun;
            span.BothAnchorsAboveNeutral = Mathf.Min(sh.A, sh.B) > neutral + eps;
            span.QualifiesForNeutralRun = span.LengthMeters >= summary.MinLengthForNeutralRun;

            float dropBelowBand = Mathf.Max(0f, Mathf.Min(sh.A, sh.B) - sh.Min);
            float riseAboveBand = Mathf.Max(0f, sh.Max - Mathf.Max(sh.A, sh.B));

            if (span.BothAnchorsAboveNeutral && span.QualifiesForNeutralRun)
            {
                // Legitimate: transition out → hold neutral → transition in. Reaching neutral is
                // free; only going BELOW neutral is unjustified. One down/up cycle is allowed.
                span.UnnecessarySideHeightExcursion =
                    Mathf.Max(Mathf.Max(0f, neutral - sh.Min), riseAboveBand);
                span.UnnecessarySideHeightReversals = Mathf.Max(0, sh.Reversals - 1);
                summary.LegitimateNeutralRuns++;
            }
            else
            {
                // No neutral plateau is physically justified — any incidental dip counts.
                span.UnnecessarySideHeightExcursion = Mathf.Max(dropBelowBand, riseAboveBand);
                span.UnnecessarySideHeightReversals = sh.Reversals;
                if (span.BothAnchorsAboveNeutral) summary.ShortSpans++;
                else summary.StraddlingSpans++;
            }

            summary.UnnecessarySideHeightReversals += span.UnnecessarySideHeightReversals;
            if (span.UnnecessarySideHeightExcursion > summary.WorstUnnecessarySideHeightExcursion)
            {
                summary.WorstUnnecessarySideHeightExcursion = span.UnnecessarySideHeightExcursion;
                summary.WorstUnnecessaryAt = $"{span.FromAnchor} → {span.ToAnchor} ({span.Classification})";
            }

            summary.SpanCount++;
            summary.Spans.Add(span);
        }

        private static ChannelSpan MeasureChannel(List<Ring> rings, int aIdx, int spanStart, int bIdx, int ch)
        {
            float eps = ChannelEpsilon[ch];
            var cs = new ChannelSpan { A = rings[aIdx].V[ch], B = rings[bIdx].V[ch] };
            cs.Min = Mathf.Min(cs.A, cs.B);
            cs.Max = Mathf.Max(cs.A, cs.B);

            float lo = Mathf.Min(cs.A, cs.B), hi = Mathf.Max(cs.A, cs.B);
            float previous = cs.A;
            int sign = 0;

            for (int i = spanStart; i <= bIdx; i++)
            {
                float v = rings[i].V[ch];
                cs.Min = Mathf.Min(cs.Min, v);
                cs.Max = Mathf.Max(cs.Max, v);

                float d = v - previous;
                if (Mathf.Abs(d) > eps)
                {
                    int s = d > 0f ? 1 : -1;
                    if (sign != 0 && s != sign) cs.Reversals++;
                    sign = s;
                    previous = v;
                }
            }

            cs.Excursion = Mathf.Max(0f, Mathf.Max(lo - cs.Min, cs.Max - hi));
            return cs;
        }

        // ─────────────────────────── Reporting ───────────────────────────

        public static void AppendReport(StringBuilder sb, List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig cfg)
        {
            if (sb == null || sections == null || cfg?.RoadProfile == null) return;
            Summary s = Measure(sections, cfg);
            CultureInfo c = CultureInfo.InvariantCulture;

            sb.AppendLine("── V2.1 CROSS-SECTION TRANSITION QUALITY (read-only; influences nothing) ──");
            sb.AppendLine("Composed TopL/TopR are measured from the REAL evaluated cross-section (width, side height,");
            sb.AppendLine("wall multipliers, overhang, rounding, pipe closure, wallride morph), in the frame's local basis.");
            sb.AppendLine($"sections: anchor {s.Anchors} | design-target {s.DesignTargets} | pass-through {s.PassThroughs} | spans {s.SpanCount}");
            sb.AppendLine($"composed wall-top worst RATES (m/m): lateral {s.WorstLateralRate.ToString("F3", c)} at {s.WorstLateralAt}");
            sb.AppendLine($"                                     vertical {s.WorstVerticalRate.ToString("F3", c)} at {s.WorstVerticalAt}");
            sb.AppendLine($"                                     combined {s.WorstCombinedRate.ToString("F3", c)} at {s.WorstCombinedAt}");
            sb.AppendLine();
            sb.AppendLine("  CHANNEL ATTRIBUTION (how much of the wave each channel contributes)");
            sb.AppendLine("  channel      | reversals | worst excursion | at");
            for (int ch = 0; ch < ChannelCount; ch++)
            {
                sb.AppendLine($"  {ChannelNames[ch],-12} | {s.Reversals[ch],9} | " +
                              $"{s.WorstExcursion[ch].ToString("F2", c),9} {ChannelUnits[ch],-3} | {s.WorstAt[ch]}");
            }
            sb.AppendLine();
            sb.AppendLine("  ══ V2.1 STAGE-2 METRICS (what the SideHeight fix is judged against) ══");
            sb.AppendLine($"  neutral SideHeight {s.NeutralSideHeight.ToString("F1", c)}m (from cfg.RoadProfile.SideHeight) | " +
                          $"transition Lt {s.TransitionLength.ToString("F0", c)}m | " +
                          $"neutral run needs >= 3xLt = {s.MinLengthForNeutralRun.ToString("F0", c)}m");
            sb.AppendLine($"  span classification: SHORT(no neutral justified) {s.ShortSpans} | " +
                          $"LONG(neutral run allowed) {s.LegitimateNeutralRuns} | straddles-neutral {s.StraddlingSpans}");
            sb.AppendLine($"  >> UnnecessarySideHeightReversals = {s.UnnecessarySideHeightReversals}");
            sb.AppendLine($"  >> UnnecessarySideHeightExcursion  = {s.WorstUnnecessarySideHeightExcursion.ToString("F2", c)}m " +
                          $"at {s.WorstUnnecessaryAt}");
            sb.AppendLine();
            sb.AppendLine("  SPANS RANKED BY UNNECESSARY SIDEHEIGHT EXCURSION (the actual defect list)");
            sb.AppendLine("  class | span | len (need) | SideH A→B [min..max] | raw rev/exc | UNNECESSARY rev/exc | pass-through");

            var byUnnecessary = new List<Span>(s.Spans);
            byUnnecessary.Sort((x, y) =>
            {
                int cmp = y.UnnecessarySideHeightExcursion.CompareTo(x.UnnecessarySideHeightExcursion);
                return cmp != 0 ? cmp : y.UnnecessarySideHeightReversals.CompareTo(x.UnnecessarySideHeightReversals);
            });
            int listed = 0;
            foreach (Span sp in byUnnecessary)
            {
                if (listed++ >= 14) break;
                if (sp.UnnecessarySideHeightExcursion < 0.01f && sp.UnnecessarySideHeightReversals == 0) continue;
                ChannelSpan h = sp.Channels[(int)Channel.SideHeight];
                sb.AppendLine(
                    $"  {(sp.QualifiesForNeutralRun && sp.BothAnchorsAboveNeutral ? "LONG " : sp.BothAnchorsAboveNeutral ? "SHORT" : "strad")} | " +
                    $"{sp.FromAnchor} → {sp.ToAnchor} | " +
                    $"{sp.LengthMeters.ToString("F0", c)}m (need {sp.MinLengthForNeutralRun.ToString("F0", c)}m) | " +
                    $"{h.A.ToString("F1", c)}→{h.B.ToString("F1", c)} [{h.Min.ToString("F1", c)}..{h.Max.ToString("F1", c)}] | " +
                    $"{h.Reversals}/{h.Excursion.ToString("F2", c)}m | " +
                    $"**{sp.UnnecessarySideHeightReversals}/{sp.UnnecessarySideHeightExcursion.ToString("F2", c)}m** | " +
                    $"{sp.PassThroughNames}");
            }
            sb.AppendLine();
            sb.AppendLine("  WORST SPANS (ranked by FINAL COMPOSED wall-top excursion — diagnostic only)");
            sb.AppendLine("  span | len | SideH A→B [min..max] rev/exc | BoostL A→B rev/exc | BoostR A→B rev/exc | TopL A→B rev/exc | TopR A→B rev/exc | pass-through");

            s.Spans.Sort((x, y) => y.WorstExcursion.CompareTo(x.WorstExcursion));
            int shown = 0;
            foreach (Span sp in s.Spans)
            {
                if (shown++ >= 12) break;
                if (sp.WorstExcursion < 0.05f) continue;

                string Ch(int ch, string fmt) =>
                    $"{sp.Channels[ch].A.ToString(fmt, c)}→{sp.Channels[ch].B.ToString(fmt, c)} " +
                    $"[{sp.Channels[ch].Min.ToString(fmt, c)}..{sp.Channels[ch].Max.ToString(fmt, c)}] " +
                    $"{sp.Channels[ch].Reversals}/{sp.Channels[ch].Excursion.ToString(fmt, c)}";

                sb.AppendLine($"  {sp.FromAnchor} → {sp.ToAnchor} | {sp.LengthMeters.ToString("F0", c)}m | " +
                              $"SideH {Ch((int)Channel.SideHeight, "F1")} | " +
                              $"BoostL {Ch((int)Channel.BoostL, "F2")} | " +
                              $"BoostR {Ch((int)Channel.BoostR, "F2")} | " +
                              $"TopL {Ch((int)Channel.TopLVertical, "F1")} | " +
                              $"TopR {Ch((int)Channel.TopRVertical, "F1")} | " +
                              $"{sp.PassThroughNames}");
            }
            sb.AppendLine();
        }
    }
}
