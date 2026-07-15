using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration
{
    /// <summary>
    /// Exports a COMPLETE text diagnostic report of the current generated track —
    /// enough to reconstruct the track from the file alone:
    ///
    ///  • the settings AS SERIALIZED IN THE SCENE and the resolved config,
    ///  • the chain map (where the road physically breaks),
    ///  • EVERY section with its full definition, and EVERY ring frame with position,
    ///    heading/pitch/tilt, bank, wall multipliers, depth, rounding, overhangs,
    ///    pipe closure, width and lap progress,
    ///  • self-analysis passes that point INTO that data: connector support-dip audit
    ///    (including WHY a straight was or was not bridged), wall-wave hotspots,
    ///    discontinuity scan, and a structural dissection of every quarter,
    ///  • the plan-time connector decisions, subdivision regions, warnings, failures.
    ///
    /// Everything is locatable by [section index] + ring + arc meters. The file is a
    /// few MB for a full lap — intended to be attached to a debugging conversation.
    /// </summary>
    public static class TrackDebugReportExporter
    {
        // ─────────────────────────── Entry points ───────────────────────────

        /// <summary>Builds the full report text for the generator's current track.</summary>
        public static string BuildReport(TrackGenerator generator)
        {
            var sb = new StringBuilder(1 << 21);

            var sections = generator.CurrentMacroSections;
            if (sections == null || sections.Count == 0)
            {
                sb.AppendLine("NO TRACK: generate a track first (or the section list did not survive a scene reload).");
                return sb.ToString();
            }

            ResolvedTrackGenerationConfig resolved = null;
            if (generator.Config != null && generator.Designer != null)
                resolved = ResolvedTrackGenerationConfig.Resolve(generator.Config, generator.Designer.Clone());

            Header(sb, generator, resolved);
            SceneSettingsDump(sb, generator);
            ResolvedDump(sb, resolved);
            ChainMap(sb, sections);
            ConnectorAudit(sb, sections, resolved);
            WallWaveHotspots(sb, sections, resolved);
            DiscontinuityScan(sb, sections);
            QuarterDissection(sb, generator, sections);
            SectionDump(sb, sections);
            ReportTail(sb, generator);

            return sb.ToString();
        }

        /// <summary>Writes the report to <paramref name="path"/> (default: project root, seed-stamped). Returns the path.</summary>
        public static string Export(TrackGenerator generator, string path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                int seed = generator.LastReport?.Seed ?? 0;
                path = System.IO.Path.Combine(Application.dataPath, "..", $"TrackReport_{seed}.txt");
            }
            string text = BuildReport(generator);
            System.IO.File.WriteAllText(path, text);
            Debug.Log($"[TrackDebugReportExporter] Report written to {System.IO.Path.GetFullPath(path)} ({text.Length / 1024} KB)");
            return path;
        }

        // ─────────────────────────── Header / settings ───────────────────────────

        private static void Header(StringBuilder sb, TrackGenerator generator, ResolvedTrackGenerationConfig resolved)
        {
            var report = generator.LastReport;
            var m = generator.LastMetrics;

            sb.AppendLine("════════════════════ TRACK DEBUG REPORT (FULL) ════════════════════");
            if (report != null)
            {
                sb.AppendLine($"Seed {report.Seed} | {(report.Success ? "SUCCESS" : "FAILED")} | Command: {report.RegenerationCommand}");
                sb.AppendLine($"Attempts {report.AttemptsEvaluated} | Candidates {report.ValidCandidateCount} | Score {report.SelectedCandidateScore:F1}");
                if (report.PreservedStreams.Count > 0)
                    sb.AppendLine($"Streams preserved: {string.Join(",", report.PreservedStreams)} | changed: {string.Join(",", report.ChangedStreams)}");
                if (report.LockedSettingsGroups.Count > 0)
                    sb.AppendLine($"Locked groups: {string.Join(", ", report.LockedSettingsGroups)} | Layout lock: {report.LayoutLockMode}");
            }
            if (m != null)
            {
                sb.AppendLine($"Lap {m.LapLengthMeters / 1000f:F2}km | est {m.EstimatedNeutralLapTimeSeconds:F1}s | turns {m.TurnCount} | rings {m.TotalRings} | max facet {m.MaxFacetAngleObserved:F2}°");
                sb.AppendLine($"loops {m.LoopCount} corks {m.CorkscrewCount} spirals {m.SpiralCount} halfloops {m.HalfLoopCount} jumps {m.JumpCount} pipes {m.FullPipeCount} wallrides {m.WallrideCount} dualQuarters {m.DualRoadQuarterCount}");
                sb.AppendLine($"elevation {m.MinElevation:F0}..{m.MaxElevation:F0}m");
            }
            sb.AppendLine($"Designer provenance: {generator.Designer?.AppliedPresetName ?? "?"}{(generator.Designer?.ModifiedSincePreset == true ? " (modified)" : "")}");
            sb.AppendLine();
        }

        /// <summary>The settings AS SERIALIZED IN THE SCENE — stale values survive here even when code defaults moved.</summary>
        private static void SceneSettingsDump(StringBuilder sb, TrackGenerator generator)
        {
            var d = generator.Designer;
            if (d == null) return;
            sb.AppendLine("── SCENE SETTINGS (actual serialized values) ──");
            sb.AppendLine($"Scale: speed {d.Scale.DesignSpeedKph:F0}km/h, lap {d.Scale.TargetLapTimeSeconds:F1}s, cap {d.Scale.MaxTrackLengthMeters / 1000f:F1}km, pacing {d.Scale.PacingVariation:F2}");
            sb.AppendLine($"Layout: turns {d.Layout.MinTurnCount}-{d.Layout.MaxTurnCount} {d.Layout.DirectionPattern}, straights {d.Layout.MinStraightSeconds:F2}-{d.Layout.MaxStraightSeconds:F2}s, seqChance {d.Layout.CornerSequenceChance:F2}");
            sb.AppendLine($"Corners: R {d.Corners.MinCurveRadius:F0}-{d.Corners.MaxCurveRadius:F0}m, bankStrength {d.Corners.BankingStrength:F2}, maxBank {d.Corners.MaxBankAngle:F0}°, floorTilt {d.Corners.FloorTiltStrength:F2}");
            sb.AppendLine($"Road: width {d.Road.RoadWidth:F0}m, sideH {d.Road.HalfPipeSideHeight:F1}m, flat {d.Road.CenterFlatWidthRatio:F2}, curve {d.Road.WallCurveStrength:F2}, wallAngle {d.Road.MaxWallAngle:F0}°, lip {d.Road.SafetyLipHeight:F1}m, res {d.Road.ProfileResolution}");
            sb.AppendLine($"Road dynamics: rounding {(d.Road.DynamicTurnRounding ? $"ON {d.Road.TurnRoundingStrength:F2} minFlat {d.Road.MinimumTurnCenterFlatRatio:F2}" : "OFF")}, catchWall {(d.Road.OutsideCatchWall ? $"ON {d.Road.CatchWallStrength:F2} maxOver {d.Road.MaxOverhangAngle:F0}° r{d.Road.OverhangRadius:F0}m demand {d.Road.CatchWallMinimumDemand:F2}" : "OFF")}");
            sb.AppendLine($"Transitions: generic {d.Transitions.GenericTransitionSeconds:F2}s bank {d.Transitions.BankTransitionSeconds:F2}s pitch {d.Transitions.PitchTransitionSeconds:F2}s roll {d.Transitions.RollTransitionSeconds:F2}s width {d.Transitions.WidthTransitionSeconds:F2}s cross {d.Transitions.CrossSectionTransitionSeconds:F2}s");
            sb.AppendLine($"Connectors: minDur {d.Transitions.MinimumConnectorSeconds:F2}s | INHERITANCE {d.Transitions.ConnectorInheritanceStrength:F2} | bankReversal {d.Transitions.MinimumBankReversalSeconds:F2}s | BRIDGE {d.Transitions.SameDirectionBridgeSeconds:F2}s | expand {d.Transitions.AllowConnectorExpansion} absorb {d.Transitions.AllowConnectorAbsorption}");
            sb.AppendLine($"Quarters: dual {d.Quarters.MinimumDualQuarterCount}-{d.Quarters.MaximumDualQuarterCount} | choice {d.Quarters.ChoiceType} | laneSep {d.Quarters.LaneSeparationMeters:F0}m | catchScale {d.Quarters.CatchWidthScale:F2} | roadB width×{d.Quarters.DualRoadWidthScale:F2} | lenTol {d.Quarters.RoadLengthTolerance:P0} | balanceTol {d.Quarters.NeutralTimeTolerancePercent:F1}% policy {d.Quarters.BalancePolicy}{(d.Quarters.PreventAdjacentDualQuarters ? " | noAdjacent" : "")}{(d.Quarters.AllowQ1Dual ? " | Q1ok" : "")}{(d.Quarters.AllowQ4Dual ? " | Q4ok" : "")}");
            sb.AppendLine($"Features: groups {d.Features.MinFeatureGroups}-{d.Features.MaxFeatureGroups} | wallrides {RuleStr(d.Features.Wallrides)} | pipes {RuleStr(d.Features.FullPipes)} | jumps {RuleStr(d.Features.Jumps)} | loops {RuleStr(d.Features.Loops)} | corks {RuleStr(d.Features.Corkscrews)} | spirals {RuleStr(d.Features.Spirals)} | halfloops {RuleStr(d.Features.HalfLoops)} | hairpins {RuleStr(d.Features.Hairpins)} | chicanes {RuleStr(d.Features.Chicanes)} | scurves {RuleStr(d.Features.SCurves)}");
            sb.AppendLine($"Elevation: amp {d.Elevation.TargetElevationAmplitude:F0}m majors {d.Elevation.MinMajorElevationSections}-{d.Elevation.MaxMajorElevationSections} climb ≤{d.Elevation.MaxClimbAngle:F0}° drop ≤{d.Elevation.MaxDropAngle:F0}° policy {d.Elevation.GroundLevelPolicy}");
            sb.AppendLine($"Generation: attempts {d.Generation.MaxAttempts} {d.Generation.SelectionMode} score {d.Generation.CandidatesToScore} | mpr {d.Generation.MetersPerRing:F2} facet {d.Generation.MaxFacetAngleDegrees:F2}° ringBudget {d.Generation.TargetTotalRings} | closureReserve {d.Generation.ClosureReserveFraction:F2}");
            sb.AppendLine();
        }

        private static string RuleStr(TrackFeatureRule r)
            => r == null ? "null" : r.Enabled ? $"{r.MinimumCount}-{r.MaximumCount}(w{r.OptionalWeight:F1})" : "off";

        private static void ResolvedDump(StringBuilder sb, ResolvedTrackGenerationConfig r)
        {
            if (r == null) return;
            sb.AppendLine("── RESOLVED (rulebook-clamped, meters) ──");
            sb.AppendLine($"speed {r.DesignSpeedMps:F0}m/s | straights {r.MinStraightLength:F0}-{r.MaxStraightLength:F0}m | R {r.MinCurveRadius:F0}-{r.MaxCurveRadius:F0}m | width {r.RoadWidth:F0}m sideH {r.RoadProfile.SideHeight:F1}m flat {r.RoadProfile.CenterFlatWidthRatio:F2} wallAngle {r.RoadProfile.WallAngle:F0}° lip {r.RoadProfile.SafetyLipHeight:F1}m");
            sb.AppendLine($"bankBlend {r.BankTransitionLength:F0}m | minConnector {r.MinimumConnectorLength:F0}m | BRIDGE {r.SameDirectionBridgeLength:F0}m | bankReversal {r.MinimumBankReversalLength:F0}m | inherit {r.ConnectorInheritanceStrength:F2}");
            sb.AppendLine($"field blur radius (bank) = max(60, {r.BankTransitionLength * 0.5f:F0}, {r.MinimumBankReversalLength * 0.5f:F0}) = {Mathf.Max(60f, Mathf.Max(r.BankTransitionLength * 0.5f, r.MinimumBankReversalLength * 0.5f)):F0}m");
            sb.AppendLine($"quarters: dual {r.MinDualQuarters}-{r.MaxDualQuarters} {r.QuarterChoiceType} | laneSep {r.LaneSeparation:F0}m | catch {r.QuarterCatchWidth:F0}m | roadB width {r.DualRoadWidth:F0}m | lenTol {r.RoadLengthTolerance:P0} | balanceTol {r.NeutralTimeTolerance:P1}");
            sb.AppendLine($"jumps: approach {r.JumpApproachLength:F0}m launch {r.MinLaunchTransitionLength:F0}-{r.MaxLaunchTransitionLength:F0}m airtime {r.MinJumpAirtimeSeconds:F2}-{r.MaxJumpAirtimeSeconds:F2}s landing {r.MinLandingTransitionLength:F0}-{r.MaxLandingTransitionLength:F0}m lip {r.MinJumpHeight:F0}-{r.MaxJumpHeight:F0}m");
            sb.AppendLine($"spirals: {r.MinSpiralRevolutions}-{r.MaxSpiralRevolutions} rev | quantized climb {r.MinSpiralClimbPerRevolution:F0}-{r.MaxSpiralClimbPerRevolution:F0}m/rev | built-layer clearance {r.SpiralClearance:F0}m");
            sb.AppendLine($"pipes: len {r.MinFullPipeLength:F0}-{r.MaxFullPipeLength:F0}m transition {r.PipeTransitionLength:F0}m radiusScale {r.FullPipeRadiusScale:F2} | rounding {(r.DynamicTurnRoundingEnabled ? r.TurnRoundingStrength.ToString("F2") : "off")} catchWall {(r.CatchWallEnabled ? r.CatchWallStrength.ToString("F2") : "off")}");
            foreach (var issue in r.Issues)
                sb.AppendLine($"  [{issue.Severity}] {issue.Field}: {issue.Message}");
            sb.AppendLine();
        }

        // ─────────────────────────── Chain map ───────────────────────────

        /// <summary>Where the road is physically continuous and where it breaks (air gaps, forks, open edges).</summary>
        private static void ChainMap(StringBuilder sb, List<GeneratedTrackSection> sections)
        {
            sb.AppendLine("── CHAIN MAP (contiguous physical road; breaks = air gaps / forks / open edges) ──");
            int chainStart = -1;
            GeneratedTrackSection prev = null;

            void CloseChain(int endIdx)
            {
                if (chainStart < 0) return;
                var s0 = sections[chainStart];
                var s1 = sections[endIdx];
                sb.AppendLine($"  chain [{s0.SectionIndex:D3}..{s1.SectionIndex:D3}] arc {s0.StartFrame.ArcLength:F0}→{s1.EndFrame.ArcLength:F0}m ({s1.EndFrame.ArcLength - s0.StartFrame.ArcLength:F0}m)");
                chainStart = -1;
            }

            for (int i = 0; i < sections.Count; i++)
            {
                var sec = sections[i];
                if (sec.IsEmptySpace || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2)
                {
                    CloseChain(i - 1);
                    if (sec.IsEmptySpace)
                        sb.AppendLine($"  AIR GAP [{sec.SectionIndex:D3}] {sec.Definition.DebugName}: {sec.StartFrame.Position} → {sec.EndFrame.Position} ({sec.Definition.Length:F0}m flight)");
                    prev = null;
                    continue;
                }

                bool connects = prev != null && !prev.OpenEnd && !sec.OpenStart &&
                    (prev.EndFrame.Position - sec.StartFrame.Position).sqrMagnitude <= 0.0025f;
                if (!connects)
                {
                    CloseChain(i - 1);
                    chainStart = i;
                }
                prev = sec;
            }
            CloseChain(sections.Count - 1);
            sb.AppendLine("  (the last chain welds to the first when the lap closes; route B sections are separate chains at their gates)");
            sb.AppendLine();
        }

        // ─────────────────────────── Full section + ring dump ───────────────────────────

        /// <summary>
        /// EVERYTHING: every section's full definition, then EVERY ring frame.
        /// heading = yaw (0° = +Z, clockwise); pitch = nose up; tilt = roll of Up about
        /// Forward vs world up; multL/R = wall height multipliers (1 = full wall,
        /// 0 = open, >1 = boosted); sh = side height (m); rd = turn rounding 0..1;
        /// ovL/R = overhang engagement 0..1; pc = pipe closure 0..1; lp = lap progress.
        /// </summary>
        private static void SectionDump(StringBuilder sb, List<GeneratedTrackSection> sections)
        {
            sb.AppendLine("════════ FULL SECTION + RING DUMP ════════");
            foreach (var sec in sections)
            {
                var d = sec.Definition;
                sb.AppendLine($"[{sec.SectionIndex:D3}] {d.SectionType} \"{d.DebugName}\"");

                var meta = new List<string>
                {
                    $"len {d.Length:F1}m",
                    $"width {d.Width:F1}m"
                };
                if (d.TurnSign != 0) meta.Add($"turn {d.TurnSign * d.TurnAngle:+0.#;-0.#}° R{d.Radius:F0}m bank {d.BankingAngle:F1}°");
                if (d.SectionType == TrackMacroSectionType.Corkscrew && d.SecondaryRadius > 0.001f)
                    meta.Add($"halfR {d.Radius:F0}/{d.SecondaryRadius:F0}m");
                if (Mathf.Abs(d.ElevationChange) > 0.01f) meta.Add($"elev {d.ElevationChange:+0.#;-0.#}m");
                if (Mathf.Abs(d.HillHeight) > 0.01f) meta.Add($"hill {d.HillHeight:+0.#;-0.#}m");
                if (Mathf.Abs(d.PitchChange) > 0.01f) meta.Add($"pitchΔ {d.PitchChange:F1}°");
                if (Mathf.Abs(d.RollChange) > 0.01f) meta.Add($"rollΔ {d.RollChange:F0}°");
                if (d.FeatureEntryStraightFraction > 0.001f || d.FeatureExitStraightFraction > 0.001f)
                    meta.Add($"straight shoulders {d.FeatureEntryStraightFraction * 100f:F1}%/{d.FeatureExitStraightFraction * 100f:F1}%");
                if (Mathf.Abs(d.SecondaryPitchDeg) > 0.01f) meta.Add($"secPitch {d.SecondaryPitchDeg:F1}°");
                if (d.AirtimeSeconds > 0.001f) meta.Add($"airtime {d.AirtimeSeconds:F2}s");
                if (d.PlanHorizontalLength > 0.001f) meta.Add($"planRun {d.PlanHorizontalLength:F0}m");
                if (d.PipeCloseFraction > 0.001f) meta.Add($"pipeClose@{d.PipeCloseFraction:F2} open@{d.PipeOpenFraction:F2}");
                if (d.MinimumLength > 0.1f) meta.Add($"minLen {d.MinimumLength:F0}m");
                meta.Add($"intent {d.SpeedIntent}/{d.RiskLevel}");
                sb.AppendLine($"      def: {string.Join(" | ", meta)}");

                var flags = new List<string>();
                if (d.LockLength) flags.Add("lockLength");
                if (d.IsClosure) flags.Add("closure");
                if (d.RequiresRecoveryAfter) flags.Add("needsRecovery");
                if (sec.OpenStart) flags.Add("OPEN-START");
                if (sec.OpenEnd) flags.Add("OPEN-END");
                if (sec.CapStart) flags.Add("capStart");
                if (sec.CapEnd) flags.Add("capEnd");
                if (sec.ConnectsToAirGap) flags.Add("airGapAdj");
                string ids = $"conn {d.ConnectorBehavior}" +
                             (string.IsNullOrEmpty(d.PatternId) ? "" : $" | pattern {d.PatternId}") +
                             (string.IsNullOrEmpty(d.TurnComplexId) ? "" : $" | complex {d.TurnComplexId}") +
                             ($" | Q{sec.QuarterIndex + 1}{(sec.RoadId == 1 ? "/roadB" : "")}");
                sb.AppendLine($"      {ids}{(flags.Count > 0 ? " | " + string.Join(",", flags) : "")}");
                sb.AppendLine($"      contract: entry {d.Contract.RequiredEntryOrientation} exit {d.Contract.ExitOrientation} headingΔ {d.Contract.HeadingDeltaDegrees:F0}° elevΔ {d.Contract.ElevationDelta:F0}m");

                if (d.SectionType == TrackMacroSectionType.RotationalEvent && d.RotationalPhases != null)
                {
                    for (int p = 0; p < d.RotationalPhases.Count; p++)
                    {
                        var phase = d.RotationalPhases[p];
                        if (phase == null) continue;
                        sb.AppendLine($"      phase {p}: {phase.Axis}/{phase.Direction} {phase.RotationUnits}u=" +
                                      $"{phase.RotationUnits * 90}deg | halves {phase.FirstHalfLength:F0}/{phase.SecondHalfLength:F0}m " +
                                      $"R{phase.FirstHalfRadius:F0}/R{phase.SecondHalfRadius:F0} | heading {phase.HorizontalTurnDegrees:+0;-0}deg " +
                                      $"yawBias {phase.FirstHalfYawBiasDegrees:+0;-0}/{phase.SecondHalfYawBiasDegrees:+0;-0}deg " +
                                      $"pitchDrift {phase.VerticalDriftDegrees:+0;-0}deg bias {phase.FirstHalfPitchBiasDegrees:+0;-0}/{phase.SecondHalfPitchBiasDegrees:+0;-0}deg " +
                                      $"orbit {phase.CenterlineOrbitDegrees:F0}deg | " +
                                      $"exit kH/kV/rollRate {phase.ExitHorizontalCurvature:F5}/{phase.ExitVerticalCurvature:F5}/{phase.ExitRoadRollRate:F3} | blend {phase.BlendToNext}");
                    }
                }

                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length == 0)
                {
                    sb.AppendLine($"      NO RINGS (air gap). start {V(sec.StartFrame.Position)} pitch {sec.StartFrame.PitchAngle:F1}° → end {V(sec.EndFrame.Position)} pitch {sec.EndFrame.PitchAngle:F1}°");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"      rings: {frames.Length} (subdivisions {frames.Length - 1}) arc {frames[0].ArcLength:F1}→{frames[frames.Length - 1].ArcLength:F1}m");
                sb.AppendLine("      ring |   arc   |     x       y       z    |  head  pitch  tilt  bank | multL multR |  sh   |  rd  | ovL  ovR |  pc  | width |  lp");
                for (int i = 0; i < frames.Length; i++)
                    sb.AppendLine("      " + RingRow(i, frames[i]));
                sb.AppendLine();
            }
        }

        private static string V(Vector3 p) => $"({p.x:F1}, {p.y:F1}, {p.z:F1})";

        private static string RingRow(int i, in TrackConnectionFrame f)
        {
            float heading = Mathf.Atan2(f.Forward.x, f.Forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(f.Forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 refUp = Vector3.ProjectOnPlane(Vector3.up, f.Forward);
            float tilt = refUp.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(refUp.normalized, f.Up, f.Forward) : 0f;

            return $"{i,4} | {f.ArcLength,7:F1} | {f.Position.x,8:F1} {f.Position.y,7:F1} {f.Position.z,8:F1} | {heading,6:F1} {pitch,5:F1} {tilt,5:F1} {f.BankAngle,5:F1} | accV {f.AccumulatedVerticalRotation,5:F0} accR {f.AccumulatedRoadRoll,5:F0} rateR {f.RoadRollRate,5:F2} | {f.LeftWallMultiplier,5:F2} {f.RightWallMultiplier,5:F2} | {f.SideHeight,5:F2} | {f.TurnRounding,4:F2} | {f.LeftOverhang,4:F2} {f.RightOverhang,4:F2} | {f.PipeClosure,4:F2} | {f.Width,5:F1} | {f.LapProgress:F4}";
        }

        // ─────────────────────────── Connector audit ───────────────────────────

        /// <summary>
        /// EVERY straight between two other sections gets audited — classified or not —
        /// with the peak/min support across the prev→connector→next complex, so a wavy
        /// wall shows up as a dip ratio and a reason ("not classified because …").
        /// Full frame data lives in the SECTION DUMP under the same [index].
        /// </summary>
        private static void ConnectorAudit(StringBuilder sb, List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig resolved)
        {
            sb.AppendLine("── CONNECTOR AUDIT (dip = min/min(neighbor peaks); 1.00 = perfectly held) ──");
            float bridgeLen = resolved?.SameDirectionBridgeLength ?? 0f;

            int audited = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                var sec = sections[i];
                var d = sec.Definition;
                bool straightFamily = d.SectionType == TrackMacroSectionType.Straight ||
                                      d.SectionType == TrackMacroSectionType.WideStraight ||
                                      d.SectionType == TrackMacroSectionType.BoostStraight ||
                                      d.SectionType == TrackMacroSectionType.RecoveryStraight;
                if (!straightFamily || sec.SubdivisionFrames == null || sec.SubdivisionFrames.Length < 2) continue;

                var prev = sections[(i - 1 + sections.Count) % sections.Count];
                var next = sections[(i + 1) % sections.Count];
                if (prev.SubdivisionFrames == null || next.SubdivisionFrames == null) continue;

                bool prevTurn = prev.Definition.TurnSign != 0;
                bool nextTurn = next.Definition.TurnSign != 0;
                if (!prevTurn && !nextTurn) continue; // straight between straights — nothing to hold
                audited++;

                (float boost, float bank, float depth) PeakOf(GeneratedTrackSection s)
                {
                    float pb = 0f, pk = 0f, pd = 0f;
                    foreach (var f in s.SubdivisionFrames)
                    {
                        pb = Mathf.Max(pb, Mathf.Max(f.LeftWallMultiplier, f.RightWallMultiplier) - 1f);
                        pk = Mathf.Max(pk, Mathf.Abs(f.BankAngle));
                        pd = Mathf.Max(pd, f.SideHeight);
                    }
                    return (pb, pk, pd);
                }

                var pPeak = PeakOf(prev);
                var nPeak = PeakOf(next);
                float carryBoost = Mathf.Min(pPeak.boost, nPeak.boost);
                float carryBank = Mathf.Min(pPeak.bank, nPeak.bank);
                float carryDepth = Mathf.Min(pPeak.depth, nPeak.depth);

                float minBoost = float.MaxValue, minBank = float.MaxValue, minDepth = float.MaxValue;
                foreach (var f in sec.SubdivisionFrames)
                {
                    minBoost = Mathf.Min(minBoost, Mathf.Max(f.LeftWallMultiplier, f.RightWallMultiplier) - 1f);
                    minBank = Mathf.Min(minBank, Mathf.Abs(f.BankAngle));
                    minDepth = Mathf.Min(minDepth, f.SideHeight);
                }

                float len = sec.EndFrame.ArcLength - sec.StartFrame.ArcLength;
                string why = d.ConnectorBehavior.ToString();
                if (d.ConnectorBehavior == ConnectorBehavior.BlendNeighbours ||
                    d.ConnectorBehavior == ConnectorBehavior.NeutralTransition)
                {
                    if (prevTurn && nextTurn)
                        why += len >= bridgeLen
                            ? $" (len {len:F0}m ≥ bridge window {bridgeLen:F0}m)"
                            : " (signs?/pattern neighbor?)";
                    else
                        why += " (only one turning neighbor)";
                }

                string dip(float mn, float carry) => carry > 0.05f ? (mn / carry).ToString("F2") : "n/a";
                bool suspicious = carryBoost > 0.15f && minBoost / Mathf.Max(0.001f, carryBoost) < 0.75f;

                sb.AppendLine($"[{sec.SectionIndex:D3}] {d.DebugName} len {len:F0}m arc {sec.StartFrame.ArcLength:F0} | {prev.Definition.SectionType}({prev.Definition.TurnSign * prev.Definition.TurnAngle:+0;-0;0}°) → this → {next.Definition.SectionType}({next.Definition.TurnSign * next.Definition.TurnAngle:+0;-0;0}°)");
                sb.AppendLine($"      class: {why}{(string.IsNullOrEmpty(d.TurnComplexId) ? "" : $" complex {d.TurnComplexId}")}");
                sb.AppendLine($"      boost peakP/min/peakN {pPeak.boost:F2}/{minBoost:F2}/{nPeak.boost:F2} dip {dip(minBoost, carryBoost)} | bank {pPeak.bank:F0}/{minBank:F0}/{nPeak.bank:F0}° dip {dip(minBank, carryBank)} | depth {pPeak.depth:F1}/{minDepth:F1}/{nPeak.depth:F1}m dip {dip(minDepth, carryDepth)}{(suspicious ? "   << SUSPICIOUS DIP (see ring dump)" : "")}");
            }
            if (audited == 0) sb.AppendLine("(no turn-adjacent straights found)");
            sb.AppendLine();
        }

        // ─────────────────────────── Wall-wave hotspots ───────────────────────────

        /// <summary>
        /// Scans the whole lap for the worst wall-top height rates (m per m of track) —
        /// the physical "wavy wall" signal. Rows point into the SECTION DUMP.
        /// </summary>
        private static void WallWaveHotspots(StringBuilder sb, List<GeneratedTrackSection> sections,
            ResolvedTrackGenerationConfig resolved)
        {
            sb.AppendLine("── WALL-WAVE HOTSPOTS (top 12 wall-top rates; test bound is 0.12 m/m) ──");
            var profile = resolved?.RoadProfile ?? new TrackRoadProfileSettings();

            float WallTop(in TrackConnectionFrame f, bool left)
            {
                float side = f.SideHeight > 0.001f ? f.SideHeight : profile.SideHeight;
                float mult = left ? f.LeftWallMultiplier : f.RightWallMultiplier;
                return profile.HeightAt(1f, f.Width * 0.5f, side) * mult + profile.SafetyLipHeight * mult;
            }

            var hotspots = new List<(float rate, GeneratedTrackSection sec, int ring)>();
            foreach (var sec in sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;
                var t = sec.Definition.SectionType;
                if (t == TrackMacroSectionType.Loop || t == TrackMacroSectionType.Corkscrew ||
                    t == TrackMacroSectionType.HalfLoopTwist ||
                    t == TrackMacroSectionType.RotationalEvent) continue;

                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.5f, frames[i].ArcLength - frames[i - 1].ArcLength);
                    float rate = Mathf.Max(
                        Mathf.Abs(WallTop(frames[i], true) - WallTop(frames[i - 1], true)),
                        Mathf.Abs(WallTop(frames[i], false) - WallTop(frames[i - 1], false))) / ds;
                    hotspots.Add((rate, sec, i));
                }
            }
            hotspots.Sort((a, b) => b.rate.CompareTo(a.rate));

            for (int h = 0; h < Mathf.Min(12, hotspots.Count); h++)
            {
                var (rate, sec, ring) = hotspots[h];
                if (rate < 0.03f) break; // rest is noise-flat
                var f = sec.SubdivisionFrames[ring];
                sb.AppendLine($"#{h + 1}: {rate:F3} m/m at [{sec.SectionIndex:D3}] {sec.Definition.DebugName} ring {ring} arc {f.ArcLength:F0}m");
            }
            sb.AppendLine();
        }

        // ─────────────────────────── Discontinuity scan ───────────────────────────

        private static void DiscontinuityScan(StringBuilder sb, List<GeneratedTrackSection> sections)
        {
            sb.AppendLine("── DISCONTINUITY SCAN (worst adjacent-ring deltas, incl. across section joins) ──");
            float worstBank = 0f, worstSide = 0f, worstWidth = 0f, worstRound = 0f;
            string atBank = "", atSide = "", atWidth = "", atRound = "";

            TrackConnectionFrame? prevF = null;
            foreach (var sec in sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 2) { prevF = null; continue; }

                // Chain breaks: an alternate road starts its own chain at its landing
                // mouth, and open boundaries (jump lips/mouths) never join physically.
                if ((sec.RoadId == 1 && sec.OpenStart) || sec.OpenStart ||
                    (prevF.HasValue && (prevF.Value.Position - frames[0].Position).sqrMagnitude > 0.01f))
                    prevF = null;

                for (int i = 0; i < frames.Length; i++)
                {
                    if (prevF.HasValue)
                    {
                        float ds = frames[i].ArcLength - prevF.Value.ArcLength;
                        if (ds > 0.01f && ds < 50f) // skip duplicated boundary rings and arc resets
                        {
                            void Check(float delta, ref float worst, ref string at)
                            {
                                float r = Mathf.Abs(delta) / ds;
                                if (r > worst) { worst = r; at = $"[{sec.SectionIndex:D3}] {sec.Definition.DebugName} ring {i} arc {frames[i].ArcLength:F0}m"; }
                            }
                            Check(Mathf.DeltaAngle(prevF.Value.BankAngle, frames[i].BankAngle), ref worstBank, ref atBank);
                            Check(frames[i].SideHeight - prevF.Value.SideHeight, ref worstSide, ref atSide);
                            Check(frames[i].Width - prevF.Value.Width, ref worstWidth, ref atWidth);
                            Check(frames[i].TurnRounding - prevF.Value.TurnRounding, ref worstRound, ref atRound);
                        }
                    }
                    prevF = frames[i];
                }
                if (sec.OpenEnd) prevF = null;
            }

            sb.AppendLine($"bank rate      : {worstBank:F3} °/m  at {atBank}");
            sb.AppendLine($"sideHeight rate: {worstSide:F4} m/m  at {atSide}");
            sb.AppendLine($"width rate     : {worstWidth:F3} m/m  at {atWidth}");
            sb.AppendLine($"rounding rate  : {worstRound:F4} /m   at {atRound}");
            sb.AppendLine();
        }

        // ─────────────────────────── Quarter dissection ───────────────────────────

        private static void QuarterDissection(StringBuilder sb, TrackGenerator generator,
            List<GeneratedTrackSection> sections)
        {
            sb.AppendLine("── QUARTER DISSECTION ──");
            var layout = generator.CurrentLayout;
            if (layout == null || layout.Quarters.Count == 0)
            {
                sb.AppendLine("(CurrentLayout unavailable — scene was reloaded; quarter records are not serialized on the sections)");
                sb.AppendLine();
                return;
            }

            foreach (var q in layout.Quarters)
            {
                bool dual = q.IsDual && q.RouteB != null;
                sb.AppendLine($"QUARTER {q.QuarterIndex + 1} ({q.LogicalProgressStart:P0}–{q.LogicalProgressEnd:P0}): {(dual ? "DUAL ROAD" : "SingleRoad")}");
                if (q.RouteA != null && q.RouteA.FirstSectionIndex >= 0)
                    sb.AppendLine($"  road A: sections [{q.RouteA.FirstSectionIndex}..{q.RouteA.LastSectionIndex}] " +
                                  $"arc {q.RouteA.EntryFrame.ArcLength:F0}→{q.RouteA.ExitFrame.ArcLength:F0}m " +
                                  $"({q.RouteA.PhysicalLengthMeters:F0}m{(q.RouteA.EstimatedNeutralTimeSeconds > 0f ? $", est {q.RouteA.EstimatedNeutralTimeSeconds:F2}s" : "")})" +
                                  $" | features: {(q.RouteA.HasFeatures ? string.Join(", ", q.RouteA.FeaturePatternIds) : "none")}");

                if (!dual)
                {
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"  road B: sections [{q.RouteB.FirstSectionIndex}..{q.RouteB.LastSectionIndex}] " +
                              $"({q.RouteB.PhysicalLengthMeters:F0}m{(q.RouteB.EstimatedNeutralTimeSeconds > 0f ? $", est {q.RouteB.EstimatedNeutralTimeSeconds:F2}s" : "")})" +
                              $" | features: {(q.RouteB.HasFeatures ? string.Join(", ", q.RouteB.FeaturePatternIds) : "none")}");
                sb.AppendLine($"  choice: {q.ChoiceType}, lane separation {q.LaneSeparationMeters:F0}m");

                float mouthSep = Vector3.Distance(q.RouteA.EntryFrame.Position, q.RouteB.EntryFrame.Position);
                sb.AppendLine($"  mouths {mouthSep:F1}m apart | lips {Vector3.Distance(sections[q.RouteB.FirstSectionIndex - 1].EndFrame.Position, q.RouteB.ExitFrame.Position):F1}m apart");

                sb.AppendLine("  gate sequence:");
                int lipIdx = q.RouteB.FirstSectionIndex - 1;
                for (int k = Mathf.Max(0, lipIdx - 1); k <= Mathf.Min(sections.Count - 1, q.RouteB.LastSectionIndex + 3); k++)
                {
                    var s = sections[k];
                    string marker = s.RoadId == 1 ? " <ROAD B>" : "";
                    sb.AppendLine($"    [{s.SectionIndex:D3}] {s.Definition.SectionType,-16} {s.Definition.DebugName,-34} len {s.Definition.Length,6:F0}m width {s.StartFrame.Width,4:F0}→{s.EndFrame.Width,4:F0} {(s.OpenStart ? "openS " : "")}{(s.OpenEnd ? "openE" : "")}{marker}");
                }

                if (q.Balance != null)
                    sb.AppendLine("  " + q.Balance.ToString().Replace("\n", "\n  "));
                sb.AppendLine();
            }
        }

        // ─────────────────────────── Tail ───────────────────────────

        private static void ReportTail(StringBuilder sb, TrackGenerator generator)
        {
            var report = generator.LastReport;
            if (report == null) return;

            if (report.ConnectorDecisions.Count > 0)
            {
                sb.AppendLine("── PLAN-TIME CONNECTOR DECISIONS ──");
                foreach (var d in report.ConnectorDecisions) sb.AppendLine("  " + d);
                sb.AppendLine();
            }
            if (report.SubdivisionRegions.Count > 0)
            {
                sb.AppendLine("── SUBDIVISION REGIONS ──");
                foreach (var r in report.SubdivisionRegions) sb.AppendLine("  " + r);
                sb.AppendLine();
            }
            if (report.Warnings.Count > 0)
            {
                sb.AppendLine("── WARNINGS ──");
                foreach (var w in report.Warnings) sb.AppendLine("  " + w);
                sb.AppendLine();
            }
            if (report.Failures.Count > 0)
            {
                sb.AppendLine($"── FAILURES ({report.Failures.Count}; last 12) ──");
                for (int i = Mathf.Max(0, report.Failures.Count - 12); i < report.Failures.Count; i++)
                    sb.AppendLine("  " + report.Failures[i]);
                sb.AppendLine();
            }
            sb.AppendLine("════════════════════ END OF REPORT ════════════════════");
        }
    }
}
