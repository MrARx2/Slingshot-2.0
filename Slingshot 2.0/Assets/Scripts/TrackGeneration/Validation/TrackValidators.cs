using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Validation
{
    /// <summary>One structured validation finding.</summary>
    public class ValidationIssue
    {
        public GenerationFailureReason Reason;
        public bool IsError;                 // errors reject the candidate; warnings are reported
        public string Validator;
        public string Subject;
        public string Message;
        public Vector3 Position;
        public float RequestedValue;
        public float AchievedValue;

        public override string ToString() => $"[{Validator}] {(IsError ? "ERROR" : "warn")} {Reason}: {Message}";
    }

    /// <summary>
    /// Independent validators over a built candidate layout. Each returns structured
    /// issues instead of a bare bool; the pipeline records every rejection reason.
    /// </summary>
    public static class TrackValidators
    {
        /// <summary>Runs the full validator suite. Errors reject the candidate.</summary>
        public static List<ValidationIssue> RunAll(GeneratedTrackLayout layout, TopologyPlan plan,
            ResolvedTrackGenerationConfig cfg)
        {
            var issues = new List<ValidationIssue>();
            ValidateRequiredFeatures(layout, plan, cfg, issues);
            ValidateFrameContinuity(layout, cfg, issues);
            ValidateRingQuality(layout, cfg, issues);
            ValidateTransitionRates(layout, cfg, issues);
            ValidateRingBudget(layout, cfg, issues);
            ValidateSelfIntersection(layout, cfg, issues);
            ValidateBranchGroups(layout, cfg, issues);
            return issues;
        }

        public static bool HasErrors(List<ValidationIssue> issues)
        {
            foreach (var i in issues) if (i.IsError) return true;
            return false;
        }

        // ─────────────────────── Required features/patterns ───────────────────────

        public static void ValidateRequiredFeatures(GeneratedTrackLayout layout, TopologyPlan plan,
            ResolvedTrackGenerationConfig cfg, List<ValidationIssue> issues)
        {
            // Count what was ACTUALLY built (never trust the plan's bookkeeping alone).
            int loops = 0, corks = 0, spirals = 0, halfLoops = 0, jumps = 0, hairpins = 0, chicanes = 0, sCurves = 0;
            foreach (var sec in layout.Sections)
            {
                switch (sec.Definition.SectionType)
                {
                    case TrackMacroSectionType.Loop: loops++; break;
                    case TrackMacroSectionType.Corkscrew: corks++; break;
                    case TrackMacroSectionType.Spiral: spirals++; break;
                    case TrackMacroSectionType.HalfLoopTwist: halfLoops++; break;
                    case TrackMacroSectionType.JumpRamp: jumps++; break;
                    case TrackMacroSectionType.BankedHairpin: hairpins++; break;
                    case TrackMacroSectionType.Chicane: chicanes++; break;
                    case TrackMacroSectionType.SCurve:
                        if (!sec.Definition.IsClosure) sCurves++; // closure S-bends are solver primitives, not designer S-curves
                        break;
                }
            }

            void Check(ResolvedFeatureRule rule, int actual, string name)
            {
                if (!rule.Enabled) return;
                if (actual < rule.MinimumCount)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.RequiredFeatureMissing,
                        IsError = true,
                        Validator = "RequiredFeatures",
                        Subject = name,
                        RequestedValue = rule.MinimumCount,
                        AchievedValue = actual,
                        Message = $"Required at least {rule.MinimumCount} {name}, built {actual}."
                    });
                if (actual > rule.MaximumCount)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.RequiredFeatureMissing,
                        IsError = true,
                        Validator = "RequiredFeatures",
                        Subject = name,
                        RequestedValue = rule.MaximumCount,
                        AchievedValue = actual,
                        Message = $"Maximum {rule.MaximumCount} {name} exceeded: built {actual}."
                    });
            }

            Check(cfg.Loops, loops, "loops");
            Check(cfg.Corkscrews, corks, "corkscrews");
            Check(cfg.Spirals, spirals, "spirals");
            Check(cfg.Jumps, jumps, "jump groups");
            Check(cfg.Hairpins, hairpins, "hairpins");
            Check(cfg.Chicanes, chicanes, "chicanes");
            Check(cfg.SCurves, sCurves, "S-curves");

            foreach (var p in cfg.RequiredPatterns)
            {
                plan.PlacedPatterns.TryGetValue(p.Pattern, out int placed);
                if (placed < p.Count)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.RequiredPatternMissing,
                        IsError = true,
                        Validator = "RequiredPatterns",
                        Subject = p.Pattern.ToString(),
                        RequestedValue = p.Count,
                        AchievedValue = placed,
                        Message = $"Required pattern {p.Pattern} ×{p.Count}, placed {placed}."
                    });
            }

            int branchCount = layout.BranchGroups.Count;
            if (branchCount < cfg.MinBranchGroups)
                issues.Add(new ValidationIssue
                {
                    Reason = GenerationFailureReason.RequiredFeatureMissing,
                    IsError = true,
                    Validator = "RequiredFeatures",
                    Subject = "branch groups",
                    RequestedValue = cfg.MinBranchGroups,
                    AchievedValue = branchCount,
                    Message = $"Required at least {cfg.MinBranchGroups} branch groups, built {branchCount}."
                });
        }

        // ─────────────────────── Frame continuity ───────────────────────

        public static void ValidateFrameContinuity(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            var sections = layout.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                var cur = sections[i];
                if (cur.SubdivisionFrames == null || cur.SubdivisionFrames.Length == 0) continue;

                // The next connected section (skip route-B siblings; air-gap boundaries are open by design).
                int j = (i + 1) % sections.Count;
                if (sections[j].RouteId == 1) j = (j + 1) % sections.Count;
                var next = sections[j];

                if (cur.OpenEnd || next.OpenStart) continue;
                if (cur.RouteId == 1) continue; // route B ends at the shared merge gate — checked via route A

                var a = cur.EndFrame;
                var b = next.StartFrame;

                float posErr = Vector3.Distance(a.Position, b.Position);
                if (posErr > 0.02f)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.OrientationMismatch,
                        IsError = true,
                        Validator = "FrameContinuity",
                        Subject = $"{cur.Definition.DebugName} → {next.Definition.DebugName}",
                        Position = a.Position,
                        AchievedValue = posErr,
                        Message = $"Weld position gap {posErr:F3}m between sections {cur.SectionIndex} and {next.SectionIndex}."
                    });

                float fwdErr = Vector3.Angle(a.Forward, b.Forward);
                float upErr = Vector3.Angle(a.Up, b.Up);
                if (fwdErr > 0.5f || upErr > 0.5f)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.OrientationMismatch,
                        IsError = true,
                        Validator = "FrameContinuity",
                        Subject = $"{cur.Definition.DebugName} → {next.Definition.DebugName}",
                        Position = a.Position,
                        AchievedValue = Mathf.Max(fwdErr, upErr),
                        Message = $"Weld orientation kink (forward {fwdErr:F2}°, up {upErr:F2}°) between sections {cur.SectionIndex} and {next.SectionIndex}."
                    });

                if (Mathf.Abs(a.Width - b.Width) > 0.05f)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.OrientationMismatch,
                        IsError = true,
                        Validator = "FrameContinuity",
                        Subject = $"{cur.Definition.DebugName} → {next.Definition.DebugName}",
                        Position = a.Position,
                        AchievedValue = Mathf.Abs(a.Width - b.Width),
                        Message = $"Width discontinuity {Mathf.Abs(a.Width - b.Width):F2}m at section weld {cur.SectionIndex}→{next.SectionIndex}."
                    });

                // Wall-height continuity: the physical wall a craft rides is
                // sideHeight × multiplier — any step here is a ramp at 361 m/s.
                // Branch gates are exempt: the fork intentionally suppresses each
                // route's INNER wall at the shared ring (the two routes' rings union
                // to cover the approach ring — that is the open throat, not a seam).
                bool gateBoundary = cur.Definition.SectionType == TrackMacroSectionType.SplitRoute ||
                                    next.Definition.SectionType == TrackMacroSectionType.SplitRoute;
                float wallErrL = gateBoundary ? 0f : Mathf.Abs(a.LeftWallMultiplier - b.LeftWallMultiplier);
                float wallErrR = gateBoundary ? 0f : Mathf.Abs(a.RightWallMultiplier - b.RightWallMultiplier);
                float sideErr = Mathf.Abs(a.SideHeight - b.SideHeight);
                if (wallErrL > 0.03f || wallErrR > 0.03f || sideErr > 0.1f)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.OrientationMismatch,
                        IsError = true,
                        Validator = "FrameContinuity",
                        Subject = $"{cur.Definition.DebugName} → {next.Definition.DebugName}",
                        Position = a.Position,
                        AchievedValue = Mathf.Max(Mathf.Max(wallErrL, wallErrR), sideErr),
                        Message = $"Wall discontinuity at section weld {cur.SectionIndex}→{next.SectionIndex} (multipliers Δ{Mathf.Max(wallErrL, wallErrR):F3}, side height Δ{sideErr:F2}m)."
                    });
            }
        }

        // ─────────────────────── Ring quality ───────────────────────

        public static void ValidateRingQuality(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            foreach (var sec in layout.Sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null) continue;

                for (int i = 0; i < frames.Length; i++)
                {
                    var f = frames[i];
                    if (float.IsNaN(f.Position.x) || float.IsNaN(f.Position.y) || float.IsNaN(f.Position.z) ||
                        float.IsNaN(f.Forward.x) || float.IsNaN(f.Up.x))
                    {
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.MeshBuildFailure,
                            IsError = true,
                            Validator = "RingQuality",
                            Subject = sec.Definition.DebugName,
                            Message = $"NaN frame value in '{sec.Definition.DebugName}' ring {i}."
                        });
                        return; // one NaN report is enough
                    }

                    if (f.Width < 1f)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.MeshBuildFailure,
                            IsError = true,
                            Validator = "RingQuality",
                            Subject = sec.Definition.DebugName,
                            Position = f.Position,
                            AchievedValue = f.Width,
                            Message = $"Degenerate ring width {f.Width:F2}m in '{sec.Definition.DebugName}'."
                        });

                    if (Mathf.Abs(f.Forward.sqrMagnitude - 1f) > 0.05f || Mathf.Abs(f.Up.sqrMagnitude - 1f) > 0.05f)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.MeshBuildFailure,
                            IsError = true,
                            Validator = "RingQuality",
                            Subject = sec.Definition.DebugName,
                            Position = f.Position,
                            Message = $"Non-unit basis vector in '{sec.Definition.DebugName}' ring {i}."
                        });
                }

                // Air-gap boundaries must never carry caps.
                if (sec.Definition.SectionType == TrackMacroSectionType.JumpRamp && (sec.CapEnd || !sec.OpenEnd))
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.MeshBuildFailure,
                        IsError = true,
                        Validator = "RingQuality",
                        Subject = sec.Definition.DebugName,
                        Message = "Jump lip must be an OPEN uncapped edge."
                    });
                if (sec.Definition.SectionType == TrackMacroSectionType.LandingRamp && (sec.CapStart || !sec.OpenStart))
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.MeshBuildFailure,
                        IsError = true,
                        Validator = "RingQuality",
                        Subject = sec.Definition.DebugName,
                        Message = "Landing mouth must be an OPEN uncapped edge."
                    });
            }
        }

        // ─────────────────────── Transition / slope / roll rates ───────────────────────

        public static void ValidateTransitionRates(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            float maxRollRate = cfg.MaxRollRateDegPerMeter * 1.35f; // smoothstep peak margin
            float maxSlope = Mathf.Tan(Mathf.Max(cfg.MaxClimbAngle, cfg.MaxDropAngle) * Mathf.Deg2Rad) * 1.15f;
            float maxFacet = cfg.MaxRingFacetAngle * 2f;

            foreach (var sec in layout.Sections)
            {
                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 3) continue;

                bool isPitchFeature = sec.Definition.SectionType == TrackMacroSectionType.Loop ||
                                      sec.Definition.SectionType == TrackMacroSectionType.HalfLoopTwist ||
                                      // Shared-axis branch routes declare their orbital roll —
                                      // the paired pattern owns that orientation contract.
                                      (sec.Definition.SectionType == TrackMacroSectionType.SplitRoute &&
                                       Mathf.Abs(sec.Definition.RollChange) > 90f);
                // A corkscrew's "slope" is its helix — meaningless as a climb; its roll
                // rate is the governing limit and IS checked below.
                bool skipSlope = isPitchFeature || sec.Definition.SectionType == TrackMacroSectionType.Corkscrew;

                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.05f, frames[i].ArcLength - frames[i - 1].ArcLength);

                    // Roll rate = TWIST about the travel axis. Comparing raw Up vectors
                    // would count pitch changes (crests, ramps) as roll — transport the
                    // previous Up across the forward rotation first.
                    if (!isPitchFeature)
                    {
                        Quaternion transport = Quaternion.FromToRotation(frames[i - 1].Forward, frames[i].Forward);
                        float rollDelta = Vector3.Angle(transport * frames[i - 1].Up, frames[i].Up);
                        if (rollDelta / ds > maxRollRate)
                        {
                            issues.Add(new ValidationIssue
                            {
                                Reason = GenerationFailureReason.RollRateViolation,
                                IsError = true,
                                Validator = "TransitionRates",
                                Subject = sec.Definition.DebugName,
                                Position = frames[i].Position,
                                RequestedValue = maxRollRate,
                                AchievedValue = rollDelta / ds,
                                Message = $"Roll rate {rollDelta / ds:F2}°/m exceeds the {maxRollRate:F2}°/m limit in '{sec.Definition.DebugName}'."
                            });
                            break;
                        }
                    }

                    // Slope (skip vertical/helix features).
                    if (!skipSlope && sec.Definition.SectionType != TrackMacroSectionType.AirGap)
                    {
                        float rise = Mathf.Abs(frames[i].Position.y - frames[i - 1].Position.y);
                        float run = new Vector2(frames[i].Position.x - frames[i - 1].Position.x,
                                                frames[i].Position.z - frames[i - 1].Position.z).magnitude;
                        if (run > 0.1f && rise / run > maxSlope)
                        {
                            issues.Add(new ValidationIssue
                            {
                                Reason = GenerationFailureReason.SlopeViolation,
                                IsError = true,
                                Validator = "TransitionRates",
                                Subject = sec.Definition.DebugName,
                                Position = frames[i].Position,
                                RequestedValue = maxSlope,
                                AchievedValue = rise / run,
                                Message = $"Slope {Mathf.Atan(rise / run) * Mathf.Rad2Deg:F1}° exceeds the limit in '{sec.Definition.DebugName}'."
                            });
                            break;
                        }
                    }

                    // Facet angle (warning: quality, not legality).
                    float facet = Vector3.Angle(frames[i - 1].Forward, frames[i].Forward);
                    if (facet > maxFacet)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.TransitionRateExceeded,
                            IsError = false,
                            Validator = "TransitionRates",
                            Subject = sec.Definition.DebugName,
                            Position = frames[i].Position,
                            RequestedValue = cfg.MaxRingFacetAngle,
                            AchievedValue = facet,
                            Message = $"Facet angle {facet:F2}° above target in '{sec.Definition.DebugName}' (quality warning)."
                        });
                        break;
                    }
                }
            }
        }

        // ─────────────────────── Ring budget ───────────────────────

        public static void ValidateRingBudget(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            int total = 0;
            foreach (var sec in layout.Sections)
            {
                int rings = sec.SubdivisionFrames?.Length ?? 0;
                total += rings;
                if (rings > cfg.MaxRingsPerSection)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.RingBudgetExceeded,
                        IsError = true,
                        Validator = "RingBudget",
                        Subject = sec.Definition.DebugName,
                        RequestedValue = cfg.MaxRingsPerSection,
                        AchievedValue = rings,
                        Message = $"Section '{sec.Definition.DebugName}' uses {rings} rings (limit {cfg.MaxRingsPerSection})."
                    });
            }

            layout.Metrics.TotalRings = total;
            if (total > cfg.MaxTotalRings)
                issues.Add(new ValidationIssue
                {
                    Reason = GenerationFailureReason.RingBudgetExceeded,
                    IsError = true,
                    Validator = "RingBudget",
                    Subject = "track",
                    RequestedValue = cfg.MaxTotalRings,
                    AchievedValue = total,
                    Message = $"Track uses {total} rings (budget {cfg.MaxTotalRings})."
                });
        }

        // ─────────────────────── 3D self-intersection / clearance ───────────────────────

        /// <summary>
        /// Broad-phase spatial hash + precise centerline clearance. Distinguishes legal
        /// proximity: same pattern (loop self-proximity), same branch group (intended
        /// paired routes), adjacent arc positions — from illegal unrelated overlap.
        /// </summary>
        public static void ValidateSelfIntersection(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            const float sampleStep = 8f;

            var pts = new List<Vector3>(4096);
            var arcs = new List<float>(4096);
            var groupIds = new List<int>(4096);
            var patternIds = new List<string>(4096);

            float mainTotal = layout.LapLength;

            foreach (var sec in layout.Sections)
            {
                if (sec.SubdivisionFrames == null) continue;
                float nextSample = float.MinValue;
                foreach (var f in sec.SubdivisionFrames)
                {
                    if (f.ArcLength < nextSample) continue;
                    pts.Add(f.Position);
                    arcs.Add(f.ArcLength);
                    groupIds.Add(sec.BranchGroupId);
                    patternIds.Add(sec.PatternId ?? "");
                    nextSample = f.ArcLength + sampleStep;
                }
            }

            if (pts.Count < 8) return;

            float minClearance = cfg.RoadWidth * 1.6f;
            float minClearanceSq = minClearance * minClearance;
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);

            // Broad phase: XZ spatial hash with cell size = clearance.
            var grid = new Dictionary<long, List<int>>();
            float cell = minClearance;
            long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

            for (int i = 0; i < pts.Count; i++)
            {
                long key = CellKey(Mathf.FloorToInt(pts[i].x / cell), Mathf.FloorToInt(pts[i].z / cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }

            float minObservedClearance = float.MaxValue;

            for (int i = 0; i < pts.Count; i++)
            {
                int cx = Mathf.FloorToInt(pts[i].x / cell);
                int cz = Mathf.FloorToInt(pts[i].z / cell);

                for (int dxc = -1; dxc <= 1; dxc++)
                for (int dzc = -1; dzc <= 1; dzc++)
                {
                    if (!grid.TryGetValue(CellKey(cx + dxc, cz + dzc), out var bucket))
                        continue;

                    foreach (int j in bucket)
                    {
                        if (j <= i) continue;

                        // Legal proximity classes.
                        float along = Mathf.Abs(arcs[j] - arcs[i]);
                        along = Mathf.Min(along, mainTotal - along);
                        if (along < minClearance * 3f) continue;                                  // adjacent track
                        if (groupIds[i] >= 0 && groupIds[i] == groupIds[j]) continue;             // paired branch routes
                        if (patternIds[i].Length > 0 && patternIds[i] == patternIds[j]) continue; // same-pattern proximity

                        float dx = pts[i].x - pts[j].x;
                        float dz = pts[i].z - pts[j].z;
                        float horizSq = dx * dx + dz * dz;
                        if (horizSq >= minClearanceSq) continue;

                        float dy = Mathf.Abs(pts[i].y - pts[j].y);
                        if (dy >= verticalOk)
                        {
                            minObservedClearance = Mathf.Min(minObservedClearance, dy);
                            continue; // legal over/under
                        }

                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.SelfIntersection,
                            IsError = true,
                            Validator = "SelfIntersection",
                            Subject = "track",
                            Position = pts[i],
                            RequestedValue = minClearance,
                            AchievedValue = Mathf.Sqrt(horizSq),
                            Message = $"Unrelated track segments pass {Mathf.Sqrt(horizSq):F1}m apart (need {minClearance:F1}m or {verticalOk:F1}m vertical) at arc {arcs[i]:F0}m / {arcs[j]:F0}m."
                        });
                        if (issues.Count > 40) return; // one candidate's report doesn't need thousands
                    }
                }
            }

            layout.Metrics.MinObservedClearance = minObservedClearance == float.MaxValue ? verticalOk : minObservedClearance;
        }

        // ─────────────────────── Branch groups ───────────────────────

        public static void ValidateBranchGroups(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            foreach (var group in layout.BranchGroups)
            {
                GeneratedTrackSection secA = null, secB = null;
                foreach (var sec in layout.Sections)
                {
                    if (sec.BranchGroupId != group.BranchGroupId) continue;
                    if (sec.RouteId == 0) secA = sec;
                    if (sec.RouteId == 1) secB = sec;
                }

                if (secA == null || secB == null)
                {
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.BranchMergeFailure,
                        IsError = true,
                        Validator = "BranchGroups",
                        Subject = $"group {group.BranchGroupId}",
                        Message = $"Branch group {group.BranchGroupId} is missing a route section."
                    });
                    continue;
                }

                // Both routes must connect to the shared gates.
                float entryGapA = Vector3.Distance(secA.StartFrame.Position, group.EntryGate.Position);
                float entryGapB = Vector3.Distance(secB.StartFrame.Position, group.EntryGate.Position);
                float mergeGapA = Vector3.Distance(secA.EndFrame.Position, group.MergeGate.Position);
                float mergeGapB = Vector3.Distance(secB.EndFrame.Position, group.MergeGate.Position);

                if (Mathf.Max(Mathf.Max(entryGapA, entryGapB), Mathf.Max(mergeGapA, mergeGapB)) > 0.05f)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.BranchMergeFailure,
                        IsError = true,
                        Validator = "BranchGroups",
                        Subject = $"group {group.BranchGroupId}",
                        Position = group.EntryGate.Position,
                        Message = $"Branch group {group.BranchGroupId}: routes do not meet their gates (max gap {Mathf.Max(Mathf.Max(entryGapA, entryGapB), Mathf.Max(mergeGapA, mergeGapB)):F2}m)."
                    });

                // Balance acceptance.
                if (group.Balance != null)
                {
                    if (group.Balance.NeutralTimeDifference > cfg.TimeBalanceTolerance * 1.5f &&
                        group.PairingMode != Design.BranchPairingMode.SafeVersusRisky)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.BranchBalanceFailure,
                            IsError = true,
                            Validator = "BranchGroups",
                            Subject = $"group {group.BranchGroupId}",
                            RequestedValue = cfg.TimeBalanceTolerance,
                            AchievedValue = group.Balance.NeutralTimeDifference,
                            Message = $"Branch group {group.BranchGroupId}: neutral time difference {group.Balance.NeutralTimeDifference:P1} exceeds tolerance {cfg.TimeBalanceTolerance:P1}."
                        });

                    if (!group.Balance.SpecializationValid)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.BranchBalanceFailure,
                            IsError = false, // reported, softly scored — not a hard rejection
                            Validator = "BranchGroups",
                            Subject = $"group {group.BranchGroupId}",
                            Message = $"Branch group {group.BranchGroupId}: one route is preferred by every craft archetype (weak specialization)."
                        });
                }

                // Pair-internal clearance: where the routes are close they must be vertically stacked.
                ValidatePairClearance(secA, secB, cfg, group.BranchGroupId, issues);
            }
        }

        private static void ValidatePairClearance(GeneratedTrackSection a, GeneratedTrackSection b,
            ResolvedTrackGenerationConfig cfg, int groupId, List<ValidationIssue> issues)
        {
            var fa = a.SubdivisionFrames;
            var fb = b.SubdivisionFrames;
            if (fa == null || fb == null || fa.Length < 4 || fb.Length < 4) return;

            // The pattern's own clearance contract: routes need EITHER enough lateral
            // separation for both half-pipes OR the wall-aware vertical clearance —
            // except inside the shared split/merge throats where walls are suppressed.
            float requiredLateral = Mathf.Max(a.Definition.Width, b.Definition.Width)
                                  + cfg.RoadProfile.SideHeight * 2f + cfg.WallMaskSafetyMargin;
            float requiredVertical = cfg.VerticalClearance * 0.85f;

            // Only the independent BODY is subject to the pair-clearance contract: the
            // staged split/divergence and convergence/merge zones are intended shared or
            // transitioning space. Zone boundaries are stored on the route definition:
            // [uSplit, uV0, uV1, uC0, uC1, uMergeStart].
            float bodyStart = 0.15f, bodyEnd = 0.85f;
            var zb = a.Definition.RouteZoneBoundaries;
            if (zb != null && zb.Length >= 6)
            {
                bodyStart = Mathf.Clamp01(zb[2] + 0.02f); // vertical divergence complete
                bodyEnd = Mathf.Clamp(zb[3] - 0.02f, bodyStart, 1f); // before vertical convergence
            }

            int samples = 24;
            for (int s = 0; s < samples; s++)
            {
                float t = Mathf.Lerp(bodyStart, bodyEnd, (float)s / (samples - 1));
                var pa = fa[Mathf.RoundToInt(t * (fa.Length - 1))];
                var pb = fb[Mathf.RoundToInt(t * (fb.Length - 1))];

                Vector3 d = pa.Position - pb.Position;
                float horiz = new Vector2(d.x, d.z).magnitude;
                float vert = Mathf.Abs(d.y);

                if (horiz < requiredLateral && vert < requiredVertical)
                {
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.VerticalClearanceFailure,
                        IsError = true,
                        Validator = "BranchGroups",
                        Subject = $"group {groupId}",
                        Position = pa.Position,
                        RequestedValue = requiredLateral,
                        AchievedValue = horiz,
                        Message = $"Branch group {groupId}: routes pass {horiz:F1}m apart with only {vert:F1}m vertical separation outside the merge throat."
                    });
                    return;
                }
            }
        }
    }
}
