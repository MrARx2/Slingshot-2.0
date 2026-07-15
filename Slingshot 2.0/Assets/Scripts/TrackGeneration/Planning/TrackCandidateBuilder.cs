using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>
    /// Turns a validated <see cref="TopologyPlan"/> into resolved geometry frames:
    /// section-by-section frame building (weld contracts exact by construction),
    /// dual-quarter alternate roads, ballistic air gaps, the global half-pipe depth
    /// blend, lap-progress stamping and the final closure check.
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

            // Dual-quarter bookkeeping: the entry choice gap's landing frame IS road A's
            // landing mouth; road B's mouth mirrors it one lane across the flight midline.
            var mouthAOf = new Dictionary<int, TrackConnectionFrame>();
            TrackConnectionFrame mainRoadFrame = frame; // cursor parked at road A's lip while a road B chain builds
            bool onAlternateRoad = false;

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];

                // ── Road transitions (canonical ↔ alternate) ──
                if (def.RoadId == 1 && !onAlternateRoad)
                {
                    // Park the canonical cursor at road A's lip; road B starts at its
                    // own landing mouth, one lane left of road A's.
                    if (!mouthAOf.TryGetValue(def.QuarterIndex, out var mouthA))
                    {
                        Fail(GenerationFailureReason.DualRoadFitFailure,
                            $"Road B of quarter {def.QuarterIndex} appears before its entry gap.");
                        return null;
                    }

                    mainRoadFrame = frame;
                    var mouthB = mouthA;
                    mouthB.Position -= SectionFrameBuilders.Flatten(mouthA.Right) * plan.Quarters[def.QuarterIndex].LaneSeparation;
                    mouthB.Width = def.Width;
                    frame = mouthB;
                    onAlternateRoad = true;
                }
                else if (def.RoadId != 1 && onAlternateRoad)
                {
                    // Road B just ended at its launch lip: it must sit exactly one lane
                    // left of road A's lip (same ballistic solution, same grade).
                    var lipA = mainRoadFrame;
                    Vector3 expected = lipA.Position - SectionFrameBuilders.Flatten(lipA.Right) *
                        plan.Quarters[defs[i - 1].QuarterIndex].LaneSeparation;
                    Vector3 error = frame.Position - expected;
                    if (new Vector2(error.x, error.z).magnitude > 0.5f || Mathf.Abs(error.y) > 1.5f)
                    {
                        Fail(GenerationFailureReason.DualRoadFitFailure,
                            $"Road B of quarter {defs[i - 1].QuarterIndex} missed its launch lip by " +
                            $"{new Vector2(error.x, error.z).magnitude:F2}m horizontally / {error.y:F2}m vertically.");
                        return null;
                    }

                    frame = mainRoadFrame;
                    onAlternateRoad = false;
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

                    case TrackMacroSectionType.RotationalEvent:
                        frames = SectionFrameBuilders.BuildRotationalEvent(frame, def, ctx);
                        break;

                    case TrackMacroSectionType.FullPipe:
                        frames = SectionFrameBuilders.BuildFullPipe(frame, def, ctx);
                        break;

                    case TrackMacroSectionType.WallrideTurn:
                        frames = SectionFrameBuilders.BuildWallrideTurn(frame, def, ctx);
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
                    // Dual-quarter gaps land laterally off the flight midline (lane A on
                    // the choice jump in, back to the midline on the convergence out).
                    section.StartFrame = frame; // the launch lip
                    Vector3 dirH = SectionFrameBuilders.Flatten(frame.Forward);
                    Vector3 landingPos = frame.Position + dirH * def.PlanHorizontalLength
                                       + SectionFrameBuilders.Flatten(frame.Right) * def.PlanLateralOffset
                                       + Vector3.up * def.ElevationChange;

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
                    // The gap's own width defines the arrival road (branch-zone catches
                    // are BROAD two-lane sections even when the launch side was narrow).
                    if (def.Width > 1f) landing.Width = def.Width;

                    frame = landing;
                    section.EndFrame = landing;

                    // The choice gap's landing is road A's mouth — road B mirrors it.
                    if (def.PlanLateralOffset > 0.01f)
                        mouthAOf[def.QuarterIndex] = landing;
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
                Vector3 closureDelta = endF.Position - startFrame.Position;
                string worstStampedDrift = "none";
                float worstStampedDriftMeters = 0f;
                foreach (var builtSection in layout.Sections)
                {
                    var builtDef = builtSection.Definition;
                    if (builtDef == null || builtDef.RoadId == 1 ||
                        builtDef.PlanHorizontalLength <= 0.001f) continue;
                    Vector3 sectionDelta = builtSection.EndFrame.Position - builtSection.StartFrame.Position;
                    Vector3 localForward = SectionFrameBuilders.Flatten(builtSection.StartFrame.Forward);
                    Vector3 localRight = SectionFrameBuilders.Flatten(builtSection.StartFrame.Right);
                    var actualLocal = new Vector2(Vector3.Dot(sectionDelta, localRight),
                        Vector3.Dot(sectionDelta, localForward));
                    var plannedLocal = new Vector2(builtDef.PlanLateralOffset,
                        builtDef.PlanHorizontalLength);
                    float drift = Vector2.Distance(actualLocal, plannedLocal);
                    if (drift <= worstStampedDriftMeters) continue;
                    worstStampedDriftMeters = drift;
                    worstStampedDrift = $"{builtDef.DebugName} plan/actual local " +
                        $"{plannedLocal.x:F1},{plannedLocal.y:F1}/{actualLocal.x:F1},{actualLocal.y:F1}m";
                }
                Fail(GenerationFailureReason.ClosurePositionFailure,
                    $"Built closure position error {posErr:F3}m exceeds the weld window {weldWindow:F2}m " +
                    $"(delta x/y/z {closureDelta.x:F1}/{closureDelta.y:F1}/{closureDelta.z:F1}m; " +
                    $"largest stamped-section drift {worstStampedDriftMeters:F1}m: {worstStampedDrift}).");
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

            // ── Lap progress (logical race progress; both quarter roads share gate progress) ──
            StampLapProgress(layout);

            // ── Authoritative quarter records ──
            PopulateQuarters(layout, plan, cfg);

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
                PatternId = def.PatternId,
                QuarterIndex = def.QuarterIndex,
                RoadId = def.RoadId
            };

            if (frames != null && frames.Length > 0)
            {
                section.StartFrame = frames[0];
                section.EndFrame = frames[frames.Length - 1];
            }

            return section;
        }

        /// <summary>
        /// Assigns logical lap progress: monotonic along the canonical road; each dual
        /// quarter's road B remaps its own physical distance onto the same mouth→lip
        /// progress window as road A, so both roads always agree at the gate frames.
        /// </summary>
        private static void StampLapProgress(GeneratedTrackLayout layout)
        {
            var sections = layout.Sections;
            float total = Mathf.Max(1f, sections[sections.Count - 1].EndFrame.ArcLength);

            // Canonical road: progress IS normalized arc.
            foreach (var sec in sections)
            {
                if (sec.RoadId == 1) continue;

                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0)
                {
                    // Air gaps carry no rings but their boundary frames still need real
                    // progress — a dual quarter's canonical span STARTS at its entry gap,
                    // and quarter bookkeeping reads these frames.
                    var sf = sec.StartFrame;
                    sf.LapProgress = Mathf.Clamp01(sf.ArcLength / total);
                    sec.StartFrame = sf;
                    var ef = sec.EndFrame;
                    ef.LapProgress = Mathf.Clamp01(ef.ArcLength / total);
                    sec.EndFrame = ef;
                    continue;
                }

                for (int i = 0; i < sec.SubdivisionFrames.Length; i++)
                {
                    var f = sec.SubdivisionFrames[i];
                    f.LapProgress = Mathf.Clamp01(f.ArcLength / total);
                    sec.SubdivisionFrames[i] = f;
                }
                RefreshBoundaryProgress(sec);
            }

            // Alternate roads: each consecutive RoadId==1 run spans the window from its
            // mouth (same arc as road A's mouth) to road A's lip end.
            for (int i = 0; i < sections.Count; i++)
            {
                if (sections[i].RoadId != 1) continue;

                int runStart = i;
                int runEnd = i;
                while (runEnd + 1 < sections.Count && sections[runEnd + 1].RoadId == 1) runEnd++;

                // The canonical section right before the run is road A's launch lip.
                var lipA = sections[runStart - 1];
                float chainStartArc = sections[runStart].StartFrame.ArcLength;
                float chainEndArc = sections[runEnd].EndFrame.ArcLength;
                float bLen = Mathf.Max(0.01f, chainEndArc - chainStartArc);
                float p0 = Mathf.Clamp01(chainStartArc / total);
                float p1 = Mathf.Clamp01(lipA.EndFrame.ArcLength / total);

                for (int s = runStart; s <= runEnd; s++)
                {
                    var sec = sections[s];
                    if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0) continue;
                    for (int f = 0; f < sec.SubdivisionFrames.Length; f++)
                    {
                        var fr = sec.SubdivisionFrames[f];
                        float t = (fr.ArcLength - chainStartArc) / bLen;
                        fr.LapProgress = Mathf.Lerp(p0, p1, Mathf.Clamp01(t));
                        sec.SubdivisionFrames[f] = fr;
                    }
                    RefreshBoundaryProgress(sec);
                }

                i = runEnd;
            }
        }

        private static void RefreshBoundaryProgress(GeneratedTrackSection sec)
        {
            var sf = sec.StartFrame;
            sf.LapProgress = sec.SubdivisionFrames[0].LapProgress;
            sec.StartFrame = sf;
            var ef = sec.EndFrame;
            ef.LapProgress = sec.SubdivisionFrames[sec.SubdivisionFrames.Length - 1].LapProgress;
            sec.EndFrame = ef;
        }

        /// <summary>
        /// Fills the authoritative 4-quarter records from the built sections: section
        /// ranges, boundary frames, road lengths and the plan's balance verdicts.
        /// A quarter's physical length counts road A only.
        /// </summary>
        private static void PopulateQuarters(GeneratedTrackLayout layout, TopologyPlan plan,
            ResolvedTrackGenerationConfig cfg)
        {
            layout.Quarters.Clear();
            var sections = layout.Sections;

            foreach (var pq in plan.Quarters)
            {
                var gq = new GeneratedTrackQuarter
                {
                    QuarterIndex = pq.Index,
                    QuarterType = pq.Dual ? TrackQuarterType.DualRoad : TrackQuarterType.SingleRoad,
                    LogicalProgressStart = pq.Index * 0.25f,
                    LogicalProgressEnd = (pq.Index + 1) * 0.25f,
                    ChoiceType = cfg.QuarterChoiceType,
                    LaneSeparationMeters = pq.Dual ? pq.LaneSeparation : 0f,
                    Balance = pq.Balance
                };

                int firstA = -1, lastA = -1, firstB = -1, lastB = -1;
                for (int i = 0; i < sections.Count; i++)
                {
                    var sec = sections[i];
                    if (sec.QuarterIndex != pq.Index) continue;
                    if (sec.RoadId == 1)
                    {
                        if (firstB < 0) firstB = i;
                        lastB = i;
                    }
                    else
                    {
                        if (firstA < 0) firstA = i;
                        lastA = i;
                    }
                }

                // Exclusive-route length only. The shared choice/convergence AIR GAPS
                // are excluded from both routes, while an ordinary feature jump inside
                // Road A remains part of that route's physical/timing comparison.
                float RouteLength(int first, int last, int roadId)
                {
                    float sum = 0f;
                    for (int i = first; i <= last; i++)
                    {
                        var sec = sections[i];
                        if (sec.RoadId != roadId || sec.QuarterIndex != pq.Index) continue;
                        // Choice/convergence flights belong to the shared gate contract,
                        // so neither exclusive route owns their length. An ordinary
                        // feature jump inside Road A is route content and its flight arc
                        // must count just like the fitter counted it.
                        if (sec.IsEmptySpace && !string.IsNullOrEmpty(sec.PatternId) &&
                            sec.PatternId.StartsWith("Quarter_"))
                            continue;
                        sum += sec.EndFrame.ArcLength - sec.StartFrame.ArcLength;
                    }
                    return sum;
                }

                List<string> RouteFeatures(int first, int last, int roadId)
                {
                    var ids = new List<string>();
                    var seen = new HashSet<string>();
                    for (int i = first; i <= last; i++)
                    {
                        var sec = sections[i];
                        string id = sec.PatternId;
                        if (sec.RoadId != roadId || sec.QuarterIndex != pq.Index ||
                            string.IsNullOrEmpty(id) || id.StartsWith("Quarter_") || !seen.Add(id))
                            continue;
                        ids.Add(id);
                    }
                    return ids;
                }

                if (firstA >= 0)
                {
                    gq.EntryFrame = sections[firstA].StartFrame;
                    gq.ExitFrame = sections[lastA].EndFrame;
                    gq.RouteA = new GeneratedQuarterRoute
                    {
                        RoadId = 0,
                        FirstSectionIndex = firstA,
                        LastSectionIndex = lastA,
                        EntryFrame = sections[firstA].StartFrame,
                        ExitFrame = sections[lastA].EndFrame,
                        PhysicalLengthMeters = RouteLength(firstA, lastA, 0),
                        FeaturePatternIds = RouteFeatures(firstA, lastA, 0)
                    };
                }

                if (pq.Dual && firstB >= 0)
                {
                    gq.RouteB = new GeneratedQuarterRoute
                    {
                        RoadId = 1,
                        FirstSectionIndex = firstB,
                        LastSectionIndex = lastB,
                        EntryFrame = sections[firstB].StartFrame,
                        ExitFrame = sections[lastB].EndFrame,
                        PhysicalLengthMeters = RouteLength(firstB, lastB, 1),
                        FeaturePatternIds = RouteFeatures(firstB, lastB, 1)
                    };
                }

                if (pq.Balance != null)
                {
                    foreach (var row in pq.Balance.TimeTable)
                    {
                        if (row.Archetype != CraftArchetype.Neutral) continue;
                        if (gq.RouteA != null) gq.RouteA.EstimatedNeutralTimeSeconds = row.RouteASeconds;
                        if (gq.RouteB != null) gq.RouteB.EstimatedNeutralTimeSeconds = row.RouteBSeconds;
                        break;
                    }
                }

                layout.Quarters.Add(gq);
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
            // The canonical road is one circular chain; each dual quarter's road B is
            // its own open chain with its own route distances.
            var mainChain = new List<GeneratedTrackSection>();
            foreach (var sec in layout.Sections)
            {
                if (sec.RoadId == 1) continue;
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2) continue;
                mainChain.Add(sec);
            }
            ProcessBankingChain(mainChain, circular: true, cfg);

            var alternate = new List<GeneratedTrackSection>();
            foreach (var sec in layout.Sections)
            {
                if (sec.RoadId == 1 && sec.SubdivisionFrames != null && sec.SubdivisionFrames.Length >= 2)
                {
                    alternate.Add(sec);
                    continue;
                }
                if (alternate.Count > 0)
                {
                    ProcessBankingChain(alternate, circular: false, cfg);
                    alternate = new List<GeneratedTrackSection>();
                }
            }
            if (alternate.Count > 0)
                ProcessBankingChain(alternate, circular: false, cfg);
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
                case TrackMacroSectionType.RotationalEvent:
                    return true;
                // FullPipe and WallrideTurn OWN their cross-section channels — the
                // field feathers to zero at their boundaries like other features.
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
            var sampleStartOf = new Dictionary<GeneratedTrackSection, int>();
            var sampleCountOf = new Dictionary<GeneratedTrackSection, int>();

            // ZERO ANCHORS: arc positions where the field MUST vanish so both sides of a
            // shared boundary ring agree exactly — boundaries of excluded (feature)
            // sections whose frames the field never touches, branch gates (route B is a
            // separate chain and must meet the main chain at zero), and open chain ends.
            var zeroAnchors = new List<float>();

            foreach (var sec in chain)
            {
                bool b = IsBankable(sec);
                var frames = sec.SubdivisionFrames;
                sampleStartOf[sec] = arcs.Count;
                sampleCountOf[sec] = frames.Length;
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

            // ── Connector-aware signal fills (BEFORE the blur) ──
            // A SameDirectionTurnBridge carries its neighbours' SIGNED support through
            // the link (the two turns + link read as ONE turn complex — no bank reset,
            // no wall dip, no center-flat expansion). An OppositeDirectionTransfer
            // carries only the UNSIGNED support: the bank still crosses zero, but the
            // overall wall support stays elevated while the outside emphasis hands
            // over sides. Fills are field inputs — the blur then smooths everything.
            var rawMag = new float[n];
            for (int i = 0; i < n; i++) rawMag[i] = Mathf.Abs(raw[i]);

            float inherit = Mathf.Clamp01(cfg.ConnectorInheritanceStrength);
            if (inherit > 0.001f)
            {
                // Peak of the NEAR portion of an adjacent section — the lobe actually
                // touching the connector. Using the whole section would grab an
                // S-curve's far lobe with the wrong sign.
                (int idx, float peakAbs, float sign) NearPeakOf(GeneratedTrackSection sec, bool tailEnd)
                {
                    if (sec == null || !sampleStartOf.TryGetValue(sec, out int start)) return (-1, 0f, 0f);
                    int count = sampleCountOf[sec];
                    int lo = tailEnd ? start + (int)(count * 0.55f) : start;
                    int hi = tailEnd ? start + count : start + Mathf.Max(1, (int)(count * 0.45f));
                    int idx = -1;
                    float best = 0f, sign = 0f;
                    for (int k = lo; k < hi; k++)
                    {
                        float a = Mathf.Abs(raw[k]);
                        if (a > best) { best = a; sign = Mathf.Sign(raw[k]); idx = k; }
                    }
                    return (idx, best, sign);
                }

                for (int c = 0; c < chain.Count; c++)
                {
                    var sec = chain[c];
                    var behavior = sec.Definition.ConnectorBehavior;
                    if (behavior != ConnectorBehavior.SameDirectionTurnBridge &&
                        behavior != ConnectorBehavior.OppositeDirectionTransfer)
                        continue;

                    float carry = Mathf.Clamp01(sec.Definition.BridgeCarry);
                    if (carry < 0.01f) continue;

                    var prevSec = c > 0 ? chain[c - 1] : (circular ? chain[chain.Count - 1] : null);
                    var nextSec = c < chain.Count - 1 ? chain[c + 1] : (circular ? chain[0] : null);
                    var p0 = NearPeakOf(prevSec, tailEnd: true);
                    var p1 = NearPeakOf(nextSec, tailEnd: false);
                    if (p0.idx < 0 || p1.idx < 0) continue;
                    if (Mathf.Min(p0.peakAbs, p1.peakAbs) * inherit * carry < 0.01f) continue;

                    // Fill PEAK TO PEAK across the whole turn complex — not just the
                    // connector. The corners' eased curvature tails would otherwise dip
                    // on both sides of a filled plateau and blur into a W-shaped wall
                    // wave; interpolating between the two apex supports leaves the blur
                    // nothing to wave over.
                    int span = p1.idx - p0.idx;
                    if (circular && span <= 0) span += n;
                    if (span <= 0 || span >= n) continue;

                    float arc0 = arcs[p0.idx];
                    float arcSpan = arcs[p1.idx] - arc0;
                    if (circular && arcSpan <= 0f) arcSpan += totalArc;
                    if (arcSpan <= 1f) continue;

                    bool signedBridge = behavior == ConnectorBehavior.SameDirectionTurnBridge &&
                                        p0.sign == p1.sign && p0.sign != 0f;

                    for (int s = 0; s <= span; s++)
                    {
                        int k = (p0.idx + s) % n;
                        float ak = arcs[k] - arc0;
                        if (ak < 0f) ak += totalArc;
                        float target = Mathf.Lerp(p0.peakAbs, p1.peakAbs, Mathf.Clamp01(ak / arcSpan))
                                       * inherit * carry;

                        if (signedBridge && Mathf.Abs(raw[k]) < target)
                            raw[k] = p0.sign * target;
                        if (rawMag[k] < target)
                            rawMag[k] = target;
                    }
                }

                // Multi-section feature corners (tightening/opening/double-apex) can
                // meet directly with no connector object between them. Their individual
                // eased-curvature tails both reach zero at the shared ring; fill peak to
                // peak so one same-direction feature keeps one outside-wall height.
                int adjacencyCount = circular ? chain.Count : chain.Count - 1;
                for (int c = 0; c < adjacencyCount; c++)
                {
                    var prevSec = chain[c];
                    var nextSec = chain[(c + 1) % chain.Count];
                    string prevPattern = prevSec.PatternId;
                    string nextPattern = nextSec.PatternId;
                    string prevComplex = prevSec.Definition.TurnComplexId;
                    string nextComplex = nextSec.Definition.TurnComplexId;
                    bool samePattern = !string.IsNullOrEmpty(prevPattern) && prevPattern == nextPattern;
                    bool sameComplex = !string.IsNullOrEmpty(prevComplex) && prevComplex == nextComplex;
                    if ((!samePattern && !sameComplex) || !IsBankable(prevSec) || !IsBankable(nextSec)) continue;

                    var p0 = NearPeakOf(prevSec, tailEnd: true);
                    var p1 = NearPeakOf(nextSec, tailEnd: false);
                    if (p0.idx < 0 || p1.idx < 0 || p0.sign == 0f || p0.sign != p1.sign) continue;

                    int span = p1.idx - p0.idx;
                    if (circular && span <= 0) span += n;
                    if (span <= 0 || span >= n) continue;

                    float arc0 = arcs[p0.idx];
                    float arcSpan = arcs[p1.idx] - arc0;
                    if (circular && arcSpan <= 0f) arcSpan += totalArc;
                    if (arcSpan <= 1f) continue;

                    for (int s = 0; s <= span; s++)
                    {
                        int k = (p0.idx + s) % n;
                        float target = Mathf.Max(p0.peakAbs, p1.peakAbs) * inherit;
                        if (Mathf.Abs(raw[k]) < target) raw[k] = p0.sign * target;
                        if (rawMag[k] < target) rawMag[k] = target;
                    }
                }
            }

            // ── Arc box blur over the bank-transition distance ──
            // The blur is the whole point: it bridges short straights between corners
            // (no wall dip), eases every entry/exit, and removes any residual seams.
            // The radius also enforces the MINIMUM BANK-REVERSAL DURATION: a signed
            // swing from one side to the other can never happen faster than the blur
            // window allows, no matter how short the connector between opposed turns.
            // The floor is PHYSICS-derived: a full −max..+max swing through the
            // triangle kernel has peak slope swing/(2·radius), and the craft's roll
            // guard is 0.35°/m — a fixed 60 m floor let short-transition configs
            // reverse at 0.4°/m+ (stretching connectors instead starves the closure
            // solver; the field is the right owner of this bound).
            float fullSwing = 2f * cfg.MaxBankAngle * Mathf.Clamp01(cfg.BankingStrength);
            float rollRateFloor = fullSwing / (2f * 0.30f); // 0.30°/m leaves margin
            float radius = Mathf.Max(Mathf.Max(60f, rollRateFloor),
                Mathf.Max(cfg.BankTransitionLength * 0.5f, cfg.MinimumBankReversalLength * 0.5f));
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
            // no kinks anywhere. The SIGNED field drives bank/tilt/side emphasis; the
            // UNSIGNED field keeps overall wall support elevated through transfers.
            var blurred = BoxBlur(BoxBlur(raw));
            var blurredMag = BoxBlur(BoxBlur(rawMag));

            // Feather to ZERO at the anchors: both sides of a boundary shared with an
            // excluded section or another chain then agree on the ring exactly — no
            // orientation or wall-height step can survive at any seam.
            if (zeroAnchors.Count > 0)
            {
                float falloff = radius * 1.5f;
                for (int i = 0; i < n; i++)
                {
                    if (blurred[i] == 0f && blurredMag[i] == 0f) continue;
                    float factor = 1f;
                    foreach (float anchor in zeroAnchors)
                    {
                        float dist = Mathf.Abs(arcs[i] - anchor);
                        if (circular) dist = Mathf.Min(dist, totalArc - dist);
                        factor = Mathf.Min(factor, SectionFrameBuilders.Smooth01(Mathf.Clamp01(dist / falloff)));
                        if (factor <= 0f) break;
                    }
                    blurred[i] *= factor;
                    blurredMag[i] *= factor;
                }
            }

            // ── Apply: wall boost + floor tilt + bank metadata from the field ──
            for (int i = 0; i < n; i++)
            {
                if (!bankable[i]) continue;

                var sec = secOf[i];
                var f = sec.SubdivisionFrames[ringOf[i]];

                float signed = Mathf.Clamp(blurred[i], -1f, 1f);
                float mag = Mathf.Clamp01(Mathf.Max(blurredMag[i], Mathf.Abs(signed)));
                float side = Mathf.Sign(signed);
                float sAbs = Mathf.Abs(signed);

                float bankDeg = sAbs * cfg.MaxBankAngle * cfg.BankingStrength;
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

                // Outside wall boost / inside trim, split by side emphasis. In a full
                // corner (|signed| = mag) this is the classic bobsled boost/trim; through
                // an opposite-direction transfer (signed → 0 while mag stays up) BOTH
                // walls hold partial support and the emphasis hands over smoothly —
                // support never collapses on both sides before the new side rises.
                // wLeft is the LEFT wall's share of the outside emphasis (a right turn,
                // signed > 0, boosts the LEFT wall).
                float wLeft = mag > 1e-4f ? 0.5f * (1f + signed / mag) : 0.5f;
                float commit = mag > 1e-4f ? sAbs / mag : 0f; // 1 = committed to a side, 0 = mid-transfer
                float newLeft = -0.75f * mag * wLeft + 0.35f * mag * (1f - wLeft) * commit;
                float newRight = -0.75f * mag * (1f - wLeft) + 0.35f * mag * wLeft * commit;

                // Junction masks (positive suppression set by the split/merge throats)
                // always win on their side.
                f.LeftWallSuppression = f.LeftWallSuppression > 0.001f ? Mathf.Max(f.LeftWallSuppression, newLeft) : newLeft;
                f.RightWallSuppression = f.RightWallSuppression > 0.001f ? Mathf.Max(f.RightWallSuppression, newRight) : newRight;

                // Dynamic turn rounding: the same committed field drives how far the
                // flat center closes into a continuous bowl (full rounding at apex,
                // smooth in/out by construction — the field is C1). Bridges carry it
                // through turn complexes because their fills feed the same signal.
                if (cfg.DynamicTurnRoundingEnabled)
                    f.TurnRounding = Mathf.Clamp01(mag * cfg.TurnRoundingStrength);

                // Outside catch wall: past a demand threshold, the OUTSIDE wall curls
                // toward (and past) vertical to hold the craft in the bowl. Emphasis
                // follows the side weights, so transfers hand the curl over smoothly.
                if (cfg.CatchWallEnabled)
                {
                    float engage = Mathf.Clamp01((mag - cfg.CatchWallMinimumDemand) /
                                   Mathf.Max(0.05f, 1f - cfg.CatchWallMinimumDemand)) * cfg.CatchWallStrength;
                    if (engage > 0.001f)
                    {
                        f.LeftOverhang = Mathf.Max(f.LeftOverhang, engage * wLeft * commit);
                        f.RightOverhang = Mathf.Max(f.RightOverhang, engage * (1f - wLeft) * commit);
                    }
                }

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
                // Never blend across a physical break: list adjacency is not physical
                // adjacency around a dual quarter's alternate road (road A's lip is
                // followed in the LIST by road B's mouth, one lane away) — and open
                // air-gap boundaries blend to themselves.
                var a = sections[i];
                var b = sections[j];
                Vector3 aEdge = step > 0 ? a.EndFrame.Position : a.StartFrame.Position;
                Vector3 bEdge = step > 0 ? b.StartFrame.Position : b.EndFrame.Position;
                bool open = step > 0 ? (a.OpenEnd || b.OpenStart) : (a.OpenStart || b.OpenEnd);
                bool airGap = a.IsEmptySpace || b.IsEmptySpace;
                if (!airGap && (open || (aEdge - bEdge).sqrMagnitude > 0.25f)) return i;
                return j;
            }

            // Bridge/transfer connectors CARRY their turn complex's depth through,
            // GRADED by BridgeCarry: short links hold fully, longer ones relax
            // proportionally — a dip to plain-straight depth between two banked turns
            // is a visible wall-height wave on every borderline link.
            float MultiplierOf(int i)
            {
                var s = sections[i];
                float own = DepthMultiplier(s.Definition.SectionType);
                float carry = Mathf.Clamp01(s.Definition.BridgeCarry);
                if (carry < 0.001f) return own;

                float held = Mathf.Min(
                    DepthMultiplier(sections[Neighbor(i, -1)].Definition.SectionType),
                    DepthMultiplier(sections[Neighbor(i, +1)].Definition.SectionType));
                return Mathf.Lerp(own, held, carry);
            }

            for (int i = 0; i < n; i++)
            {
                var sec = sections[i];
                if (sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length == 0) continue;

                float m = MultiplierOf(i);
                int pi = Neighbor(i, -1);
                int ni = Neighbor(i, +1);
                float mPrev = MultiplierOf(pi);
                float mNext = MultiplierOf(ni);

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
        /// Public: the connector analyzer uses it to size cross-section blend needs.
        /// </summary>
        public static float DepthMultiplier(TrackMacroSectionType t) => t switch
        {
            TrackMacroSectionType.BankedCurve => 1.08f,
            TrackMacroSectionType.BankedHairpin => 1.1f,
            TrackMacroSectionType.JumpRamp => 0.95f,
            TrackMacroSectionType.AirGap => 0.95f,
            TrackMacroSectionType.LandingRamp => 0.95f,
            TrackMacroSectionType.Spiral => 1.06f,
            TrackMacroSectionType.HalfLoopTwist => 1.04f,
            TrackMacroSectionType.RotationalEvent => 1.04f,
            TrackMacroSectionType.WallrideTurn => 1.1f,
            _ => 1f
        };
    }
}
