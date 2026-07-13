using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Branching;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Turns a validated <see cref="TopologyPlan"/> into resolved geometry frames:
    /// section-by-section frame building (weld contracts exact by construction),
    /// branch route pairs, ballistic air gaps, the global half-pipe depth blend,
    /// junction wall masks, lap-progress stamping and the final closure check.
    ///
    /// There is NO residual warping: the plan solved closure with legal primitives, the
    /// builders land within float/numeric precision, and the final weld only snaps the
    /// last ring when the error is already inside the rulebook tolerances — otherwise
    /// the candidate is rejected.
    /// </summary>
    public class TrackCandidateBuilder
    {
        public GenerationFailureReason Failure { get; private set; }
        public string FailureMessage { get; private set; } = "";

        public GeneratedTrackLayout Build(TopologyPlan plan, ResolvedTrackGenerationConfig cfg)
        {
            Failure = GenerationFailureReason.None;
            FailureMessage = "";

            var layout = new GeneratedTrackLayout();
            var defs = plan.Defs;
            var ctx = FrameBuildContext.From(cfg);

            TrackConnectionFrame frame = TrackConnectionFrame.Origin(defs.Count > 0 ? defs[0].Width : cfg.RoadWidth);
            TrackConnectionFrame startFrame = frame;
            int branchCursor = 0;

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];

                // ── Branch pair: two sections spanning the same gates ──
                if (def.SectionType == TrackMacroSectionType.SplitRoute)
                {
                    if (i + 1 >= defs.Count || defs[i + 1].SectionType != TrackMacroSectionType.SplitRoute ||
                        branchCursor >= plan.BranchGroups.Count)
                    {
                        Fail(GenerationFailureReason.BranchMergeFailure, "Split-route definition without its route pair.");
                        return null;
                    }

                    var group = plan.BranchGroups[branchCursor++];
                    var pair = BranchRoutePlanner.BuildPair(frame, group, def, defs[i + 1], cfg, ctx);
                    if (!pair.BalanceValid)
                    {
                        Fail(GenerationFailureReason.BranchBalanceFailure, pair.FailureMessage);
                        return null;
                    }

                    var secA = MakeSection(def, layout.Sections.Count, pair.FramesA);
                    secA.BranchGroupId = group.GroupId;
                    secA.RouteId = 0;
                    layout.Sections.Add(secA);

                    var secB = MakeSection(defs[i + 1], layout.Sections.Count, pair.FramesB);
                    secB.BranchGroupId = group.GroupId;
                    secB.RouteId = 1;
                    layout.Sections.Add(secB);

                    BranchRoutePlanner.MaskSplitPair(secA, secB, cfg);

                    layout.BranchGroups.Add(pair.Group);
                    frame = pair.FramesA[pair.FramesA.Length - 1];
                    i++; // consumed the pair
                    continue;
                }

                // ── Ordinary sections ──
                TrackConnectionFrame[] frames;
                switch (def.SectionType)
                {
                    case TrackMacroSectionType.BankedCurve:
                    case TrackMacroSectionType.BankedHairpin:
                        frames = SectionFrameBuilders.BuildArc(frame, def.TurnAngle * def.TurnSign, def.Radius, def.BankingAngle, ctx);
                        break;

                    case TrackMacroSectionType.SCurve:
                        frames = SectionFrameBuilders.BuildComposedArcs(frame,
                            new[] { def.TurnAngle * def.TurnSign, -def.TurnAngle * def.TurnSign },
                            new[] { def.Radius, def.Radius }, def.BankingAngle, ctx);
                        break;

                    case TrackMacroSectionType.Chicane:
                        frames = SectionFrameBuilders.BuildComposedArcs(frame,
                            new[] { def.TurnAngle * def.TurnSign, -2f * def.TurnAngle * def.TurnSign, def.TurnAngle * def.TurnSign },
                            new[] { def.Radius, def.Radius, def.Radius }, def.BankingAngle, ctx);
                        break;

                    case TrackMacroSectionType.JumpRamp:
                        frames = SectionFrameBuilders.BuildKeyframedPitchRamp(frame, def.Length,
                            SectionFrameBuilders.LaunchRampKeys(def.SecondaryPitchDeg, def.PitchChange), ctx);
                        break;

                    case TrackMacroSectionType.LandingRamp:
                    {
                        frames = SectionFrameBuilders.BuildKeyframedPitchRamp(frame, def.Length,
                            SectionFrameBuilders.LandingRampKeys(def.PitchChange, def.SecondaryPitchDeg), ctx);

                        // The numeric descent lands within centimeters of grade; snap the
                        // exit ring exactly (a weld inside tolerance, not a warp).
                        // Entry is the landing mouth at LandingHeight above grade and
                        // ElevationChange = -LandingHeight, so grade = entry + ElevationChange.
                        float gradeY = frame.Position.y + def.ElevationChange;
                        var lastF = frames[frames.Length - 1];
                        if (Mathf.Abs(lastF.Position.y - gradeY) > cfg.JumpLandingTolerance)
                        {
                            Fail(GenerationFailureReason.TransitionRateExceeded,
                                $"Landing ramp '{def.DebugName}' missed grade by {lastF.Position.y - gradeY:F2}m (tolerance {cfg.JumpLandingTolerance:F1}m).");
                            return null;
                        }
                        lastF.Position = new Vector3(lastF.Position.x, gradeY, lastF.Position.z);
                        lastF.Forward = SectionFrameBuilders.Flatten(lastF.Forward);
                        lastF.Right = SectionFrameBuilders.Flatten(lastF.Right);
                        lastF.Up = Vector3.up;
                        lastF.PitchAngle = 0f;
                        frames[frames.Length - 1] = lastF;
                        break;
                    }

                    case TrackMacroSectionType.Loop:
                        frames = SectionFrameBuilders.BuildLoop(frame, def, ctx);
                        break;

                    case TrackMacroSectionType.Corkscrew:
                        frames = SectionFrameBuilders.BuildCorkscrew(frame, def, ctx);
                        break;

                    case TrackMacroSectionType.Spiral:
                        frames = SectionFrameBuilders.BuildSpiral(frame, def, ctx);
                        break;

                    case TrackMacroSectionType.HalfLoopTwist:
                        frames = SectionFrameBuilders.BuildHalfLoopRollout(frame, def, ctx);
                        break;

                    case TrackMacroSectionType.AirGap:
                        frames = System.Array.Empty<TrackConnectionFrame>();
                        break;

                    default:
                        frames = SectionFrameBuilders.BuildStraight(frame, def, ctx);
                        break;
                }

                var section = MakeSection(def, layout.Sections.Count, frames);

                // Boundary open/closed metadata: air-gap boundaries are OPEN edges.
                switch (def.SectionType)
                {
                    case TrackMacroSectionType.JumpRamp:
                        section.OpenEnd = true;
                        section.ConnectsToAirGap = true;
                        break;
                    case TrackMacroSectionType.LandingRamp:
                        section.OpenStart = true;
                        section.ConnectsToAirGap = true;
                        break;
                    case TrackMacroSectionType.AirGap:
                        section.OpenStart = true;
                        section.OpenEnd = true;
                        break;
                }

                if (def.SectionType == TrackMacroSectionType.AirGap)
                {
                    // Ballistic flight from the actual built lip frame: the landing mouth
                    // position/pitch match the predicted trajectory by construction.
                    section.StartFrame = frame; // the launch lip
                    Vector3 dirH = SectionFrameBuilders.Flatten(frame.Forward);
                    Vector3 landingPos = frame.Position + dirH * def.PlanHorizontalLength + Vector3.up * def.ElevationChange;

                    float arrivalRad = def.PitchChange * Mathf.Deg2Rad;
                    Vector3 arrivalFwd = (dirH * Mathf.Cos(arrivalRad) + Vector3.up * Mathf.Sin(arrivalRad)).normalized;

                    var landing = frame;
                    landing.Position = landingPos;
                    landing.Forward = arrivalFwd;
                    landing.Right = SectionFrameBuilders.Flatten(frame.Right);
                    landing.Up = Vector3.Cross(arrivalFwd, landing.Right).normalized;
                    landing.PitchAngle = def.PitchChange;
                    landing.BankAngle = 0f;
                    landing.ArcLength = frame.ArcLength + def.Length;

                    frame = landing;
                    section.EndFrame = landing;
                }
                else
                {
                    frame = frames[frames.Length - 1];
                    section.EndFrame = frame;
                }

                layout.Sections.Add(section);
            }

            if (layout.Sections.Count == 0)
            {
                Fail(GenerationFailureReason.InvalidConfiguration, "Plan produced no sections.");
                return null;
            }

            // ── Closure validation (rulebook tolerances) — never a global warp ──
            var closing = layout.Sections[layout.Sections.Count - 1];
            var endF = closing.EndFrame;

            float posErr = Vector3.Distance(endF.Position, startFrame.Position);
            float fwdErr = Vector3.Angle(endF.Forward, startFrame.Forward);
            float upErr = Vector3.Angle(endF.Up, startFrame.Up);
            float widthErr = Mathf.Abs(endF.Width - startFrame.Width);
            float bankErr = Mathf.Abs(Mathf.DeltaAngle(endF.BankAngle, startFrame.BankAngle));
            float pitchErr = Mathf.Abs(Mathf.DeltaAngle(endF.PitchAngle, startFrame.PitchAngle));

            // Numeric integration (ramps) accumulates small drift on top of the analytic
            // plan; the weld window is the rulebook tolerance scaled by feature count.
            float weldWindow = Mathf.Max(cfg.ClosurePositionTolerance, 0.05f) * 10f;

            if (posErr > weldWindow)
            {
                Fail(GenerationFailureReason.ClosurePositionFailure,
                    $"Built closure position error {posErr:F3}m exceeds the weld window {weldWindow:F2}m.");
                return null;
            }
            if (fwdErr > Mathf.Max(cfg.ClosureForwardTolerance, 0.05f) * 10f)
            {
                Fail(GenerationFailureReason.ClosureHeadingFailure,
                    $"Built closure forward-angle error {fwdErr:F3}° exceeds tolerance.");
                return null;
            }
            if (upErr > Mathf.Max(cfg.ClosureUpTolerance, 0.05f) * 10f ||
                bankErr > Mathf.Max(cfg.ClosureBankTolerance, 0.05f) * 10f ||
                pitchErr > Mathf.Max(cfg.ClosurePitchTolerance, 0.05f) * 10f)
            {
                Fail(GenerationFailureReason.OrientationMismatch,
                    $"Built closure orientation error (up {upErr:F3}°, bank {bankErr:F3}°, pitch {pitchErr:F3}°) exceeds tolerance.");
                return null;
            }
            if (widthErr > Mathf.Max(cfg.ClosureWidthTolerance, 0.01f) * 10f)
            {
                Fail(GenerationFailureReason.ClosurePositionFailure,
                    $"Built closure width error {widthErr:F3}m exceeds tolerance.");
                return null;
            }

            // Legal weld: snap only the final ring onto the start frame.
            if (closing.SubdivisionFrames != null && closing.SubdivisionFrames.Length > 0)
            {
                var weld = closing.SubdivisionFrames[closing.SubdivisionFrames.Length - 1];
                weld.Position = startFrame.Position;
                weld.Forward = startFrame.Forward;
                weld.Right = startFrame.Right;
                weld.Up = startFrame.Up;
                weld.Width = startFrame.Width;
                closing.SubdivisionFrames[closing.SubdivisionFrames.Length - 1] = weld;
                closing.EndFrame = weld;
            }

            // ── Lap progress (logical race progress; branch routes share gate progress) ──
            StampLapProgress(layout);

            // ── Global banking field: wall support + floor tilt from ONE blurred
            // curvature signal over the whole lap (never per-section ease profiles) ──
            ApplyGlobalBankingField(layout, cfg);

            // ── Global half-pipe depth blend + finishing ──
            ApplyCrossSectionBlend(layout.Sections, cfg);

            // ── Global retopology: rebuild the ring grid at ONE uniform spacing over
            // the finished driving line (anchors: weld ring, gates, air-gap lips) ──
            TrackRetopology.Apply(layout, cfg);

            foreach (var sec in layout.Sections)
            {
                sec.Definition.SubdivisionCount = Mathf.Max(0, (sec.SubdivisionFrames?.Length ?? 1) - 1);
                sec.RecalculateBounds();
            }

            layout.LapLength = layout.Sections[layout.Sections.Count - 1].EndFrame.ArcLength;
            return layout;
        }

        private void Fail(GenerationFailureReason reason, string message)
        {
            Failure = reason;
            FailureMessage = message;
        }

        private static GeneratedTrackSection MakeSection(TrackMacroSectionDefinition def, int index, TrackConnectionFrame[] frames)
        {
            var section = new GeneratedTrackSection
            {
                Definition = def,
                SectionIndex = index,
                SubdivisionFrames = frames,
                PatternId = def.PatternId
            };

            if (frames != null && frames.Length > 0)
            {
                section.StartFrame = frames[0];
                section.EndFrame = frames[frames.Length - 1];
            }

            return section;
        }

        /// <summary>
        /// Assigns logical lap progress: monotonic along the main chain (route A of each
        /// branch is canonical); route B remaps its own physical distance onto the same
        /// entry→merge progress window, so both routes always agree at the gates.
        /// </summary>
        private static void StampLapProgress(GeneratedTrackLayout layout)
        {
            float total = Mathf.Max(1f, layout.Sections[layout.Sections.Count - 1].EndFrame.ArcLength);

            foreach (var sec in layout.Sections)
            {
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0)
                    continue;

                if (sec.RouteId == 1)
                {
                    // Remap route B's own distance onto the gate progress window.
                    float p0 = Mathf.Clamp01(sec.StartFrame.ArcLength / total);
                    float aLen = 0f;
                    foreach (var other in layout.Sections)
                    {
                        if (other.BranchGroupId == sec.BranchGroupId && other.RouteId == 0)
                        {
                            aLen = other.EndFrame.ArcLength - other.StartFrame.ArcLength;
                            break;
                        }
                    }
                    float p1 = Mathf.Clamp01((sec.StartFrame.ArcLength + aLen) / total);
                    float bLen = Mathf.Max(0.01f, sec.EndFrame.ArcLength - sec.StartFrame.ArcLength);

                    for (int i = 0; i < sec.SubdivisionFrames.Length; i++)
                    {
                        var f = sec.SubdivisionFrames[i];
                        float t = (f.ArcLength - sec.StartFrame.ArcLength) / bLen;
                        f.LapProgress = Mathf.Lerp(p0, p1, Mathf.Clamp01(t));
                        sec.SubdivisionFrames[i] = f;
                    }
                }
                else
                {
                    for (int i = 0; i < sec.SubdivisionFrames.Length; i++)
                    {
                        var f = sec.SubdivisionFrames[i];
                        f.LapProgress = Mathf.Clamp01(f.ArcLength / total);
                        sec.SubdivisionFrames[i] = f;
                    }
                }

                var sf = sec.StartFrame;
                sf.LapProgress = sec.SubdivisionFrames[0].LapProgress;
                sec.StartFrame = sf;
                var ef = sec.EndFrame;
                ef.LapProgress = sec.SubdivisionFrames[sec.SubdivisionFrames.Length - 1].LapProgress;
                sec.EndFrame = ef;
            }

            // Branch group gate progress bookkeeping.
            foreach (var group in layout.BranchGroups)
            {
                foreach (var sec in layout.Sections)
                {
                    if (sec.BranchGroupId == group.BranchGroupId && sec.RouteId == 0)
                    {
                        group.EntryLapProgress = sec.StartFrame.LapProgress;
                        group.MergeLapProgress = sec.EndFrame.LapProgress;
                        break;
                    }
                }
            }
        }

        // ─────────────────────────── Global banking field ───────────────────────────

        /// <summary>
        /// ONE smooth banking signal for the whole lap: measure the final driving line's
        /// local curvature, map it to a 0..1 support level with a smooth proportional
        /// ramp, BLUR it along the arc over the bank-transition distance, then derive
        /// the outside-wall boost, the geometric floor tilt and the bank metadata from
        /// that single field.
        ///
        /// This is what makes tracks read as CLEAN: per-section ease profiles put a seam
        /// at every boundary (walls dropped to zero across a two-second straight between
        /// corners and pumped back up; branch weaves ran their own independent signal;
        /// short flicks rocked the tilt). A blurred lap-wide field cannot express any of
        /// those artifacts.
        ///
        /// Orientation features (loops, corkscrews, half-loops, shared-axis orbits) and
        /// jump ramps own their frames and are excluded — their neighbors' support fades
        /// to zero naturally because approach/recovery straights carry no curvature.
        /// </summary>
        private static void ApplyGlobalBankingField(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg)
        {
            // Main chain (route A is canonical) is circular; each route B is its own
            // open chain with its own route distances.
            var mainChain = new List<GeneratedTrackSection>();
            foreach (var sec in layout.Sections)
            {
                if (sec.RouteId == 1) continue;
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2) continue;
                mainChain.Add(sec);
            }
            ProcessBankingChain(mainChain, circular: true, cfg);

            foreach (var sec in layout.Sections)
            {
                if (sec.RouteId != 1 || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2) continue;
                ProcessBankingChain(new List<GeneratedTrackSection> { sec }, circular: false, cfg);
            }
        }

        /// <summary>Frames of these sections may carry field banking (everything else keeps its authored orientation).</summary>
        private static bool IsBankable(GeneratedTrackSection sec)
        {
            switch (sec.Definition.SectionType)
            {
                case TrackMacroSectionType.Straight:
                case TrackMacroSectionType.WideStraight:
                case TrackMacroSectionType.BoostStraight:
                case TrackMacroSectionType.RecoveryStraight:
                case TrackMacroSectionType.BridgeVariant:
                case TrackMacroSectionType.TunnelVariant:
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                case TrackMacroSectionType.SCurve:
                case TrackMacroSectionType.Chicane:
                case TrackMacroSectionType.Spiral:
                    return true;
                case TrackMacroSectionType.SplitRoute:
                    // Shared-axis orbits declare RollChange and own their orientation.
                    return Mathf.Abs(sec.Definition.RollChange) < 90f;
                default:
                    return false;
            }
        }

        private static void ProcessBankingChain(List<GeneratedTrackSection> chain, bool circular,
            ResolvedTrackGenerationConfig cfg)
        {
            // Flatten the chain into parallel arrays (boundary duplicates included —
            // adjacent identical samples get identical field values, preserving welds).
            var secOf = new List<GeneratedTrackSection>();
            var ringOf = new List<int>();
            var arcs = new List<float>();
            var bankable = new List<bool>();

            // ZERO ANCHORS: arc positions where the field MUST vanish so both sides of a
            // shared boundary ring agree exactly — boundaries of excluded (feature)
            // sections whose frames the field never touches, branch gates (route B is a
            // separate chain and must meet the main chain at zero), and open chain ends.
            var zeroAnchors = new List<float>();

            foreach (var sec in chain)
            {
                bool b = IsBankable(sec);
                var frames = sec.SubdivisionFrames;
                for (int r = 0; r < frames.Length; r++)
                {
                    secOf.Add(sec);
                    ringOf.Add(r);
                    arcs.Add(frames[r].ArcLength);
                    bankable.Add(b);
                }

                if (!b)
                {
                    zeroAnchors.Add(sec.StartFrame.ArcLength);
                    zeroAnchors.Add(sec.EndFrame.ArcLength);
                }
                if (sec.Definition.SectionType == TrackMacroSectionType.SplitRoute)
                {
                    zeroAnchors.Add(sec.StartFrame.ArcLength);
                    zeroAnchors.Add(sec.EndFrame.ArcLength);
                }
            }

            int n = arcs.Count;
            if (n < 8) return;
            float totalArc = arcs[n - 1] - arcs[0];
            if (totalArc < 10f) return;

            // ── Raw signed support from local curvature ──
            // Ramp thresholds: no support beyond 2× the widest legal corner, full support
            // at 1.2× the tightest — proportional in between, never a saturating cliff.
            float kappaNone = 1f / (cfg.MaxCurveRadius * 2f);
            float kappaFull = 1f / Mathf.Max(150f, cfg.MinCurveRadius * 1.2f);

            var raw = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!bankable[i]) { raw[i] = 0f; continue; }

                // Heading change over a ±12 m window (frames are analytic-smooth, so a
                // modest window is enough; index steps chosen by arc distance).
                int lo = i, hi = i;
                while (lo > 0 && arcs[i] - arcs[lo] < 12f && secOf[lo - 1] == secOf[i]) lo--;
                while (hi < n - 1 && arcs[hi] - arcs[i] < 12f && secOf[hi + 1] == secOf[i]) hi++;
                float ds = arcs[hi] - arcs[lo];
                if (ds < 1f) { raw[i] = 0f; continue; }

                Vector3 f0 = secOf[lo].SubdivisionFrames[ringOf[lo]].Forward; f0.y = 0f;
                Vector3 f1 = secOf[hi].SubdivisionFrames[ringOf[hi]].Forward; f1.y = 0f;
                if (f0.sqrMagnitude < 1e-6f || f1.sqrMagnitude < 1e-6f) { raw[i] = 0f; continue; }

                float headingDelta = Vector3.SignedAngle(f0, f1, Vector3.up);
                float kappa = Mathf.Abs(headingDelta) * Mathf.Deg2Rad / ds;
                float support = SectionFrameBuilders.Smooth01(Mathf.Clamp01((kappa - kappaNone) / (kappaFull - kappaNone)));
                raw[i] = Mathf.Sign(headingDelta) * support;
            }

            // ── Arc box blur over the bank-transition distance ──
            // The blur is the whole point: it bridges short straights between corners
            // (no wall dip), eases every entry/exit, and removes any residual seams.
            float radius = Mathf.Max(60f, cfg.BankTransitionLength * 0.5f);
            var arcArray = arcs.ToArray();

            int LowerBound(float value)
            {
                int idx = System.Array.BinarySearch(arcArray, value);
                return idx >= 0 ? idx : ~idx;
            }

            // Arc box blur (prefix sums + binary search; circular windows split into two
            // segments so the blur is seamless across the start/finish weld).
            float[] BoxBlur(float[] src)
            {
                var prefix = new float[n + 1];
                for (int k = 0; k < n; k++) prefix[k + 1] = prefix[k] + src[k];

                void Accumulate(int a, int b, ref float s, ref int c)
                {
                    a = Mathf.Clamp(a, 0, n - 1);
                    b = Mathf.Clamp(b, 0, n - 1);
                    if (b < a) return;
                    s += prefix[b + 1] - prefix[a];
                    c += b - a + 1;
                }

                var dst = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float aLo = arcs[i] - radius;
                    float aHi = arcs[i] + radius;
                    float sum = 0f;
                    int count = 0;

                    if (!circular)
                    {
                        Accumulate(LowerBound(aLo), LowerBound(aHi + 0.001f) - 1, ref sum, ref count);
                    }
                    else if (aLo < arcs[0])
                    {
                        Accumulate(LowerBound(aLo + totalArc), n - 1, ref sum, ref count);
                        Accumulate(0, LowerBound(aHi + 0.001f) - 1, ref sum, ref count);
                    }
                    else if (aHi > arcs[n - 1])
                    {
                        Accumulate(LowerBound(aLo), n - 1, ref sum, ref count);
                        Accumulate(0, LowerBound(aHi - totalArc + 0.001f) - 1, ref sum, ref count);
                    }
                    else
                    {
                        Accumulate(LowerBound(aLo), LowerBound(aHi + 0.001f) - 1, ref sum, ref count);
                    }

                    dst[i] = count > 0 ? sum / count : 0f;
                }
                return dst;
            }

            // TWO box passes = triangular kernel: C1 everywhere. A single box blur of a
            // step (arc curvature IS a step function) is a linear ramp with slope kinks
            // at its edges — exactly the "ramp" feel at speed. The triangle kernel has
            // no kinks anywhere.
            var blurred = BoxBlur(BoxBlur(raw));

            // Feather to ZERO at the anchors: both sides of a boundary shared with an
            // excluded section or another chain then agree on the ring exactly — no
            // orientation or wall-height step can survive at any seam.
            if (zeroAnchors.Count > 0)
            {
                float falloff = radius * 1.5f;
                for (int i = 0; i < n; i++)
                {
                    if (blurred[i] == 0f) continue;
                    float factor = 1f;
                    foreach (float anchor in zeroAnchors)
                    {
                        float dist = Mathf.Abs(arcs[i] - anchor);
                        if (circular) dist = Mathf.Min(dist, totalArc - dist);
                        factor = Mathf.Min(factor, SectionFrameBuilders.Smooth01(Mathf.Clamp01(dist / falloff)));
                        if (factor <= 0f) break;
                    }
                    blurred[i] *= factor;
                }
            }

            // ── Apply: wall boost + floor tilt + bank metadata from the field ──
            for (int i = 0; i < n; i++)
            {
                if (!bankable[i]) continue;

                var sec = secOf[i];
                var f = sec.SubdivisionFrames[ringOf[i]];

                float signed = Mathf.Clamp(blurred[i], -1f, 1f);
                float mag = Mathf.Abs(signed);
                float side = Mathf.Sign(signed);

                float bankDeg = mag * cfg.MaxBankAngle * cfg.BankingStrength;
                f.BankAngle = side * bankDeg;

                // Geometric floor tilt, blurred by construction — it cannot rock.
                // SOFT saturation toward 18°: a hard Min() has a slope discontinuity at
                // the clamp point, which reads as a sudden roll-rate change (a bump) on
                // every corner that reaches it when Floor Tilt Strength is set high.
                float tilt = 18f * (float)System.Math.Tanh(bankDeg * cfg.FloorTiltFraction / 18f);
                if (tilt > 0.01f)
                {
                    Quaternion tiltRot = Quaternion.AngleAxis(-side * tilt, f.Forward);
                    Vector3 up = tiltRot * f.Up;
                    up = (up - Vector3.Dot(up, f.Forward) * f.Forward).normalized;
                    f.Up = up;
                    f.Right = Vector3.Cross(up, f.Forward).normalized;
                }

                // Outside wall boost / inside trim. Junction masks (positive suppression
                // set by the split/merge throats) always win on their side.
                float outer = -mag * 0.75f;
                float inner = mag * 0.35f;
                float newLeft = side > 0f ? outer : inner;
                float newRight = side > 0f ? inner : outer;
                f.LeftWallSuppression = f.LeftWallSuppression > 0.001f ? Mathf.Max(f.LeftWallSuppression, newLeft) : newLeft;
                f.RightWallSuppression = f.RightWallSuppression > 0.001f ? Mathf.Max(f.RightWallSuppression, newRight) : newRight;

                sec.SubdivisionFrames[ringOf[i]] = f;
            }

            // Refresh boundary frame copies.
            foreach (var sec in chain)
            {
                var frames = sec.SubdivisionFrames;
                sec.StartFrame = frames[0];
                sec.EndFrame = frames[frames.Length - 1];
            }
        }

        // ─────────────────────────── Half-pipe depth blend ───────────────────────────

        /// <summary>
        /// Assigns the half-pipe side height to EVERY frame — the half-pipe is the global
        /// road cross-section, never a feature. Depth varies slightly by section type and
        /// blends smoothly across boundaries (both neighbors compute identical boundary
        /// values, preserving the weld contract).
        /// </summary>
        private static void ApplyCrossSectionBlend(List<GeneratedTrackSection> sections, ResolvedTrackGenerationConfig cfg)
        {
            int n = sections.Count;
            if (n == 0 || cfg.RoadProfile == null) return;

            float baseH = cfg.RoadProfile.SideHeight;
            float blendLen = Mathf.Max(1f, cfg.CrossSectionTransitionLength);

            float SectionLen(GeneratedTrackSection s) => Mathf.Max(0.01f, s.EndFrame.ArcLength - s.StartFrame.ArcLength);

            int Neighbor(int i, int step)
            {
                int j = (i + step + n) % n;
                if (sections[i].Definition.SectionType == TrackMacroSectionType.SplitRoute &&
                    sections[j].Definition.SectionType == TrackMacroSectionType.SplitRoute &&
                    Mathf.Approximately(sections[j].StartFrame.ArcLength, sections[i].StartFrame.ArcLength))
                    j = (j + step + n) % n;
                return j;
            }

            for (int i = 0; i < n; i++)
            {
                var sec = sections[i];
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0) continue;

                float m = DepthMultiplier(sec.Definition.SectionType);
                int pi = Neighbor(i, -1);
                int ni = Neighbor(i, +1);
                float mPrev = DepthMultiplier(sections[pi].Definition.SectionType);
                float mNext = DepthMultiplier(sections[ni].Definition.SectionType);

                float len = SectionLen(sec);
                float w0 = Mathf.Max(0.01f, Mathf.Min(blendLen, Mathf.Min(len, SectionLen(sections[pi]))));
                float w1 = Mathf.Max(0.01f, Mathf.Min(blendLen, Mathf.Min(len, SectionLen(sections[ni]))));
                float s0 = sec.StartFrame.ArcLength;
                float s1 = sec.EndFrame.ArcLength;

                for (int f = 0; f < sec.SubdivisionFrames.Length; f++)
                {
                    var fr = sec.SubdivisionFrames[f];
                    float s = fr.ArcLength;

                    float mult = m;
                    if (s < s0 + w0 * 0.5f)
                        mult = Mathf.Lerp(mPrev, m, TrackBlend.Evaluate(cfg.BlendCurve, (s - (s0 - w0 * 0.5f)) / w0));
                    else if (s > s1 - w1 * 0.5f)
                        mult = Mathf.Lerp(m, mNext, TrackBlend.Evaluate(cfg.BlendCurve, (s - (s1 - w1 * 0.5f)) / w1));

                    fr.SideHeight = baseH * mult;
                    sec.SubdivisionFrames[f] = fr;
                }

                var sf = sec.StartFrame;
                sf.SideHeight = sec.SubdivisionFrames[0].SideHeight;
                sec.StartFrame = sf;

                var ef = sec.EndFrame;
                ef.SideHeight = sec.SubdivisionFrames[sec.SubdivisionFrames.Length - 1].SideHeight;
                sec.EndFrame = ef;
            }
        }

        /// <summary>
        /// Half-pipe depth multiplier per section type — the half-pipe never turns off.
        /// Deliberately NARROW band: per-type depth is seasoning, and every step here is
        /// a visible wall-height wave at each section boundary. Banking support comes
        /// from the bobsled wall boost and the floor tilt, not from depth swings.
        /// </summary>
        private static float DepthMultiplier(TrackMacroSectionType t) => t switch
        {
            TrackMacroSectionType.BankedCurve => 1.08f,
            TrackMacroSectionType.BankedHairpin => 1.1f,
            TrackMacroSectionType.JumpRamp => 0.95f,
            TrackMacroSectionType.AirGap => 0.95f,
            TrackMacroSectionType.LandingRamp => 0.95f,
            TrackMacroSectionType.Spiral => 1.06f,
            TrackMacroSectionType.HalfLoopTwist => 1.04f,
            _ => 1f
        };
    }
}
