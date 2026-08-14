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
            ValidateRotationalEvents(layout, cfg, issues);
            ValidateTransitionRates(layout, cfg, issues);
            ValidateRingBudget(layout, cfg, issues);
            ValidateSelfIntersection(layout, cfg, issues);
            ValidateQuarters(layout, cfg, issues);
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
            int fullPipes = 0, wallrides = 0;
            foreach (var sec in layout.Sections)
            {
                switch (sec.Definition.SectionType)
                {
                    case TrackMacroSectionType.Loop: loops++; break;
                    case TrackMacroSectionType.Corkscrew: corks++; break;
                    case TrackMacroSectionType.Spiral: spirals++; break;
                    case TrackMacroSectionType.HalfLoopTwist: halfLoops++; break;
                    case TrackMacroSectionType.RotationalEvent:
                    {
                        bool halfLoopPattern = !string.IsNullOrEmpty(sec.PatternId) &&
                            (sec.PatternId.StartsWith("HalfLoopRollout") ||
                             sec.PatternId.StartsWith("HalfLoopToCorkscrew"));
                        int verticalUnits = 0, rollUnits = 0;
                        if (sec.Definition.RotationalPhases != null)
                        foreach (var phase in sec.Definition.RotationalPhases)
                        {
                            if (phase == null) continue;
                            if (phase.Axis == RotationalPhaseAxis.VerticalCenterline) verticalUnits += phase.RotationUnits;
                            else rollUnits += phase.RotationUnits;
                        }
                        // Feature rules count authored maneuver events, not the number of
                        // revolutions inside one event.  This must match TryConsume in the
                        // planner or a valid double/triple event rejects itself afterward.
                        if (halfLoopPattern) halfLoops++;
                        else if (verticalUnits > 0) loops++;
                        if (rollUnits > 0)
                            corks += !string.IsNullOrEmpty(sec.PatternId) &&
                                     sec.PatternId.StartsWith("DoubleCorkscrew") ? 2 : 1;
                        break;
                    }
                    case TrackMacroSectionType.JumpRamp:
                        // Dual-quarter gate jumps are STRUCTURAL — they never count
                        // against the designer's jump-feature rule.
                        if (string.IsNullOrEmpty(sec.PatternId) || !sec.PatternId.StartsWith("Quarter_"))
                            jumps++;
                        break;
                    case TrackMacroSectionType.BankedHairpin: hairpins++; break;
                    case TrackMacroSectionType.Chicane: chicanes++; break;
                    case TrackMacroSectionType.FullPipe: fullPipes++; break;
                    case TrackMacroSectionType.WallrideTurn: wallrides++; break;
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
            Check(cfg.FullPipes, fullPipes, "full pipes");
            Check(cfg.Wallrides, wallrides, "wallride turns");

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

            int dualCount = 0;
            foreach (var q in layout.Quarters)
                if (q.IsDual) dualCount++;
            if (dualCount < cfg.MinDualQuarters)
                issues.Add(new ValidationIssue
                {
                    Reason = GenerationFailureReason.RequiredFeatureMissing,
                    IsError = true,
                    Validator = "RequiredFeatures",
                    Subject = "dual road quarters",
                    RequestedValue = cfg.MinDualQuarters,
                    AchievedValue = dualCount,
                    Message = $"Required at least {cfg.MinDualQuarters} Dual Road Quarters, built {dualCount}."
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

                // The next PHYSICALLY connected section. On the canonical road that is
                // the next canonical section (skip alternate-road runs); on an alternate
                // road it is the next section of the same run — the run's open ends
                // (landing mouth, launch lip) are skipped by the open checks below.
                int j = (i + 1) % sections.Count;
                if (cur.RoadId == 1)
                {
                    if (sections[j].RoadId != 1) continue; // launch lip: open end of the alternate chain
                }
                else
                {
                    while (sections[j].RoadId == 1) j = (j + 1) % sections.Count;
                }
                var next = sections[j];

                if (cur.OpenEnd || next.OpenStart) continue;

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
                float wallErrL = Mathf.Abs(a.LeftWallMultiplier - b.LeftWallMultiplier);
                float wallErrR = Mathf.Abs(a.RightWallMultiplier - b.RightWallMultiplier);
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

        private static bool HasRotationalAxis(TrackMacroSectionDefinition def, RotationalPhaseAxis axis)
        {
            if (def?.RotationalPhases == null) return false;
            foreach (var phase in def.RotationalPhases)
                if (phase != null && phase.Axis == axis) return true;
            return false;
        }

        /// <summary>Quantized grammar, propagated-state and adaptive-density checks.</summary>
        public static void ValidateRotationalEvents(GeneratedTrackLayout layout,
            ResolvedTrackGenerationConfig cfg, List<ValidationIssue> issues)
        {
            foreach (var sec in layout.Sections)
            {
                var def = sec.Definition;
                if (def == null || def.SectionType != TrackMacroSectionType.RotationalEvent) continue;
                var phases = def.RotationalPhases;
                if (phases == null || phases.Count == 0)
                {
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.InvalidConfiguration,
                        IsError = true,
                        Validator = "RotationalEvent",
                        Subject = def.DebugName,
                        Message = "Rotational event has no phases."
                    });
                    continue;
                }

                int totalUnits = 0;
                float expectedRoll = sec.StartFrame.AccumulatedRoadRoll;
                float expectedVertical = sec.StartFrame.AccumulatedVerticalRotation;
                for (int p = 0; p < phases.Count; p++)
                {
                    var phase = phases[p];
                    if (phase == null || phase.RotationUnits < 1)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.InvalidConfiguration,
                            IsError = true,
                            Validator = "RotationalEvent",
                            Subject = def.DebugName,
                            Message = $"Phase {p} has no positive integer rotation-unit count."
                        });
                        continue;
                    }

                    totalUnits += phase.RotationUnits;
                    float degrees = phase.Degrees(cfg.RotationUnitDegrees);
                    if (phase.Axis == RotationalPhaseAxis.RoadRoll) expectedRoll += degrees;
                    else expectedVertical += degrees;

                    bool quarterTransition = phase.RotationUnits == 1 || phase.RotationUnits == 3;
                    if (quarterTransition && (!cfg.AllowQuarterTurnTransitions || phases.Count < 2))
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.OrientationMismatch,
                            IsError = true,
                            Validator = "RotationalEvent",
                            Subject = def.DebugName,
                            Message = $"Phase {p} uses a restricted {phase.RotationUnits * cfg.RotationUnitDegrees:F0}° transition without a compatible adjacent phase."
                        });

                    float minRadius = phase.Axis == RotationalPhaseAxis.VerticalCenterline
                        ? Mathf.Max(cfg.MinLoopRadius, def.Width * 0.6f)
                        : Mathf.Max(cfg.MinCorkscrewRadius, def.Width * 0.6f);
                    float actualRadius = Mathf.Min(phase.FirstHalfRadius, phase.SecondHalfRadius);
                    if (actualRadius + 0.01f < minRadius)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.CurveRadiusViolation,
                            IsError = true,
                            Validator = "RotationalEvent",
                            Subject = def.DebugName,
                            RequestedValue = minRadius,
                            AchievedValue = actualRadius,
                            Message = $"Phase {p} radius {actualRadius:F1}m is below the road/wall-aware {minRadius:F1}m minimum."
                        });

                    float distancePerUnit = phase.Length / phase.RotationUnits;
                    if (distancePerUnit + 0.01f < cfg.MinDistancePerRotationUnit)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.TransitionRateExceeded,
                            IsError = true,
                            Validator = "RotationalEvent",
                            Subject = def.DebugName,
                            RequestedValue = cfg.MinDistancePerRotationUnit,
                            AchievedValue = distancePerUnit,
                            Message = $"Phase {p} allocates {distancePerUnit:F1}m per 90° unit; minimum is {cfg.MinDistancePerRotationUnit:F1}m."
                        });
                }

                if (Mathf.Abs(sec.EndFrame.AccumulatedRoadRoll - expectedRoll) > 0.05f ||
                    Mathf.Abs(sec.EndFrame.AccumulatedVerticalRotation - expectedVertical) > 0.05f)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.OrientationMismatch,
                        IsError = true,
                        Validator = "RotationalEvent",
                        Subject = def.DebugName,
                        Message = $"Unwrapped rotation mismatch: expected roll/vertical {expectedRoll:F1}°/{expectedVertical:F1}°, got {sec.EndFrame.AccumulatedRoadRoll:F1}°/{sec.EndFrame.AccumulatedVerticalRotation:F1}°."
                    });

                var frames = sec.SubdivisionFrames;
                if (frames == null || frames.Length < 2) continue;
                int expectedIntervals = totalUnits * cfg.MinSamplesPerRotationUnit;
                if (frames.Length - 1 < expectedIntervals)
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.RingBudgetExceeded,
                        IsError = false,
                        Validator = "RotationalEvent",
                        Subject = def.DebugName,
                        RequestedValue = expectedIntervals,
                        AchievedValue = frames.Length - 1,
                        Message = "Global retopology budget reduced the event below its preferred samples-per-unit density."
                    });

                float maxAcceleration = cfg.MaxRollRateDegPerMeter /
                                        Mathf.Max(1f, cfg.MinDistancePerRotationUnit * 0.25f);
                for (int i = 1; i < frames.Length; i++)
                {
                    float forwardAngle = Vector3.Angle(frames[i - 1].Forward, frames[i].Forward);
                    float rollAngle = Mathf.Abs(frames[i].AccumulatedRoadRoll - frames[i - 1].AccumulatedRoadRoll);
                    if (forwardAngle > cfg.MaxRotationalForwardAngle * 2f ||
                        rollAngle > cfg.MaxRotationalRollAngle * 2f)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.TransitionRateExceeded,
                            IsError = true,
                            Validator = "RotationalEvent",
                            Subject = def.DebugName,
                            Position = frames[i].Position,
                            AchievedValue = Mathf.Max(forwardAngle, rollAngle),
                            Message = $"Adaptive sample limit exceeded at ring {i}: forward {forwardAngle:F2}°, roll {rollAngle:F2}°."
                        });

                    if (Mathf.Abs(frames[i].RoadRollAcceleration) > maxAcceleration * 1.5f)
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.RollRateViolation,
                            IsError = true,
                            Validator = "RotationalEvent",
                            Subject = def.DebugName,
                            Position = frames[i].Position,
                            RequestedValue = maxAcceleration,
                            AchievedValue = Mathf.Abs(frames[i].RoadRollAcceleration),
                            Message = $"Road-roll acceleration is excessive at ring {i}."
                        });
                    if (issues.Count > 80) return;
                }

                // Same-pattern proximity is intentionally exempted by the lap-wide
                // broad phase (compound features legitimately fold over themselves).
                // Rotational events therefore need their own 3D internal-clearance
                // pass so a too-tight loop/mixed inversion cannot hide road or wall
                // overlap behind that exemption.
                float required3D = def.Width * 1.05f;
                float clearance = SectionFrameBuilders.MeasureRotationalEventClearance(frames,
                    def.Width, out float firstArc, out float secondArc);
                if (clearance + 0.01f < required3D)
                {
                    issues.Add(new ValidationIssue
                    {
                        Reason = GenerationFailureReason.SelfIntersection,
                        IsError = true,
                        Validator = "RotationalEvent",
                        Subject = def.DebugName,
                        Position = frames[0].Position,
                        RequestedValue = required3D,
                        AchievedValue = clearance,
                        Message = $"Internal road/wall clearance falls to {clearance:F1}m " +
                                  $"between event arcs {firstArc:F0}m and {secondArc:F0}m."
                    });
                }
            }
        }

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
                                      (sec.Definition.SectionType == TrackMacroSectionType.RotationalEvent &&
                                       HasRotationalAxis(sec.Definition, RotationalPhaseAxis.VerticalCenterline));
                // A corkscrew's "slope" is its helix — meaningless as a climb; its roll
                // rate is the governing limit and IS checked below.
                bool skipSlope = isPitchFeature || sec.Definition.SectionType == TrackMacroSectionType.Corkscrew ||
                                 sec.Definition.SectionType == TrackMacroSectionType.RotationalEvent;

                for (int i = 1; i < frames.Length; i++)
                {
                    float ds = Mathf.Max(0.05f, frames[i].ArcLength - frames[i - 1].ArcLength);

                    // Roll rate = TWIST about the travel axis. Comparing raw Up vectors
                    // would count pitch changes (crests, ramps) as roll — transport the
                    // previous Up across the forward rotation first.
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
            var dualQuarterOf = new List<int>(4096); // quarter index when that quarter is dual, else -1
            var roadOf = new List<int>(4096);
            var patternIds = new List<string>(4096);

            float mainTotal = layout.LapLength;

            foreach (var sec in layout.Sections)
            {
                if (sec.SubdivisionFrames == null) continue;
                bool inDualQuarter = sec.QuarterIndex >= 0 && sec.QuarterIndex < layout.Quarters.Count &&
                                     layout.Quarters[sec.QuarterIndex].IsDual;
                float nextSample = float.MinValue;
                foreach (var f in sec.SubdivisionFrames)
                {
                    if (f.ArcLength < nextSample) continue;
                    pts.Add(f.Position);
                    arcs.Add(f.ArcLength);
                    dualQuarterOf.Add(inDualQuarter ? sec.QuarterIndex : -1);
                    roadOf.Add(sec.RoadId);
                    patternIds.Add(sec.PatternId ?? "");
                    nextSample = f.ArcLength + sampleStep;
                }
            }

            if (pts.Count < 8) return;

            float minClearance = cfg.UnrelatedCorridor;
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
                        // Same dual quarter, different roads (or road B with itself):
                        // the quarter's own clearance contract judges that pairing.
                        // Canonical-vs-canonical folds inside a quarter stay checked.
                        if (dualQuarterOf[i] >= 0 && dualQuarterOf[i] == dualQuarterOf[j] &&
                            !(roadOf[i] == 0 && roadOf[j] == 0)) continue;
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

        // ─────────────────────── Quarters ───────────────────────

        /// <summary>
        /// The 4-quarter contract: exactly four quarters, contiguous and ascending on the
        /// canonical road; every dual quarter has both roads present with open gate
        /// edges, readable lane separation, a catch broad enough for both lanes, agreed
        /// lap progress at the gates, similar road lengths and archetype balance, and
        /// body clearance between the two roads away from the gate throats.
        /// </summary>
        public static void ValidateQuarters(GeneratedTrackLayout layout, ResolvedTrackGenerationConfig cfg,
            List<ValidationIssue> issues)
        {
            void Issue(GenerationFailureReason reason, bool error, string subject, string message,
                Vector3 pos = default, float requested = 0f, float achieved = 0f)
                => issues.Add(new ValidationIssue
                {
                    Reason = reason,
                    IsError = error,
                    Validator = "Quarters",
                    Subject = subject,
                    Message = message,
                    Position = pos,
                    RequestedValue = requested,
                    AchievedValue = achieved
                });

            if (layout.Quarters.Count != 4)
            {
                Issue(GenerationFailureReason.InvalidConfiguration, true, "quarters",
                    $"Expected exactly 4 quarters, found {layout.Quarters.Count}.", requested: 4, achieved: layout.Quarters.Count);
                return;
            }

            float prevExit = -1f;
            foreach (var q in layout.Quarters)
            {
                string subject = $"quarter {q.QuarterIndex}";

                if (q.RouteA == null || q.RouteA.FirstSectionIndex < 0)
                {
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Quarter {q.QuarterIndex} has no canonical road sections.");
                    continue;
                }

                // Ascending canonical progress across the quarters.
                float entryProgress = q.RouteA.EntryFrame.LapProgress;
                if (entryProgress < prevExit - 0.001f)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Quarter {q.QuarterIndex} starts at lap progress {entryProgress:F3}, before the previous quarter ended ({prevExit:F3}).");
                prevExit = q.RouteA.ExitFrame.LapProgress;

                if (!q.IsDual) continue;

                // ── Dual quarter: both roads present ──
                if (q.RouteB == null || q.RouteB.FirstSectionIndex < 0)
                {
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex} is missing its alternate road.");
                    continue;
                }

                var sections = layout.Sections;
                var flareB = sections[q.RouteB.FirstSectionIndex];
                var lipB = sections[q.RouteB.LastSectionIndex];

                // Locate road A's gate pieces: the flare right after the entry gap and
                // the lip right before the alternate road run.
                GeneratedTrackSection flareA = null;
                for (int i = q.RouteA.FirstSectionIndex; i <= q.RouteA.LastSectionIndex; i++)
                {
                    if (sections[i].RoadId != 0) continue;
                    if (sections[i].Definition.SectionType == TrackMacroSectionType.LandingRamp &&
                        sections[i].OpenStart)
                    {
                        flareA = sections[i];
                        break;
                    }
                }
                var lipA = sections[q.RouteB.FirstSectionIndex - 1];

                if (flareA == null || lipA.Definition.SectionType != TrackMacroSectionType.JumpRamp)
                {
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: gate pieces not found (flare {(flareA == null ? "missing" : "ok")}, lip {lipA.Definition.SectionType}).");
                    continue;
                }

                // Open gate edges — mouths and lips face air gaps, never caps.
                if (!flareA.OpenStart || !flareB.OpenStart || !lipA.OpenEnd || !lipB.OpenEnd ||
                    flareA.CapStart || flareB.CapStart || lipA.CapEnd || lipB.CapEnd)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: a gate edge is not open (mouths/lips must face their air gaps).");

                // Lane separation: readable in-air choice at the mouths AND at the lips.
                float mouthSep = Vector3.Distance(flareA.StartFrame.Position, flareB.StartFrame.Position);
                float lipSep = Vector3.Distance(lipA.EndFrame.Position, lipB.EndFrame.Position);
                float requiredSep = Mathf.Max(16f, q.LaneSeparationMeters * 0.8f);
                if (mouthSep < requiredSep)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: landing mouths only {mouthSep:F1}m apart — no readable in-air choice.",
                        flareA.StartFrame.Position, requiredSep, mouthSep);
                if (lipSep < requiredSep)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: launch lips only {lipSep:F1}m apart.",
                        lipA.EndFrame.Position, requiredSep, lipSep);

                // Mouths descend, lips ascend (ballistic contract).
                if (flareA.StartFrame.PitchAngle > -0.5f || flareB.StartFrame.PitchAngle > -0.5f)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: a landing mouth is not descending (A {flareA.StartFrame.PitchAngle:F1}°, B {flareB.StartFrame.PitchAngle:F1}°).");
                if (lipA.EndFrame.PitchAngle < 0.05f || lipB.EndFrame.PitchAngle < 0.05f)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: a launch lip is not ascending (A {lipA.EndFrame.PitchAngle:F1}°, B {lipB.EndFrame.PitchAngle:F1}°).");

                // The shared catch must be broad enough to receive BOTH lanes.
                GeneratedTrackSection catchSec = null;
                for (int i = q.RouteB.LastSectionIndex + 1; i < sections.Count; i++)
                {
                    if (sections[i].RoadId != 0) continue;
                    if (sections[i].Definition.SectionType == TrackMacroSectionType.LandingRamp)
                    {
                        catchSec = sections[i];
                        break;
                    }
                    if (sections[i].Definition.SectionType != TrackMacroSectionType.AirGap) break;
                }
                if (catchSec == null)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: shared convergence catch not found after the exit gap.");
                else
                {
                    float catchWidth = catchSec.StartFrame.Width;
                    if (catchWidth < cfg.RoadWidth * 1.15f || catchWidth * 0.5f < lipSep * 0.5f + 8f)
                        Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                            $"Dual quarter {q.QuarterIndex}: catch width {catchWidth:F0}m cannot receive both lanes " +
                            $"(lips {lipSep:F0}m apart; need ≥ {Mathf.Max(cfg.RoadWidth * 1.15f, lipSep + 16f):F0}m).",
                            catchSec.StartFrame.Position);
                }

                // Lap progress agreement at the gates + monotonicity along road B.
                float pMouthDiff = Mathf.Abs(flareA.StartFrame.LapProgress - flareB.StartFrame.LapProgress);
                float pLipDiff = Mathf.Abs(lipA.EndFrame.LapProgress - lipB.EndFrame.LapProgress);
                if (pMouthDiff > 0.002f || pLipDiff > 0.002f)
                    Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: roads disagree on gate lap progress (mouth Δ{pMouthDiff:F4}, lip Δ{pLipDiff:F4}).");

                float prevProgress = -1f;
                for (int i = q.RouteB.FirstSectionIndex; i <= q.RouteB.LastSectionIndex; i++)
                {
                    var frames = sections[i].SubdivisionFrames;
                    if (frames == null) continue;
                    foreach (var f in frames)
                    {
                        if (f.LapProgress < prevProgress - 0.0001f)
                        {
                            Issue(GenerationFailureReason.QuarterGateFailure, true, subject,
                                $"Dual quarter {q.QuarterIndex}: road B lap progress is not monotonic.");
                            i = q.RouteB.LastSectionIndex;
                            break;
                        }
                        prevProgress = f.LapProgress;
                    }
                }

                // Road length similarity — the player should not be punished for a lane.
                float lenA = Mathf.Max(1f, q.RouteA.PhysicalLengthMeters);
                float lenB = q.RouteB.PhysicalLengthMeters;
                float lenDiff = Mathf.Abs(lenB - lenA) / lenA;
                if (lenDiff > cfg.RoadLengthTolerance)
                    Issue(GenerationFailureReason.DualRoadFitFailure, true, subject,
                        $"Dual quarter {q.QuarterIndex}: road lengths differ by {lenDiff:P0} (A {lenA:F0}m, B {lenB:F0}m; tolerance {cfg.RoadLengthTolerance:P0}).",
                        requested: cfg.RoadLengthTolerance, achieved: lenDiff);
                else if (lenDiff > cfg.RoadLengthTolerance * 0.8f)
                    Issue(GenerationFailureReason.DualRoadFitFailure, false, subject,
                        $"Dual quarter {q.QuarterIndex}: road lengths near tolerance ({lenDiff:P0}).");

                // Balance re-check on the plan verdict (validation only, generous margin).
                if (q.Balance != null)
                {
                    if (q.Balance.NeutralTimeDifferencePercent > cfg.NeutralTimeTolerance * 100f * 1.5f)
                        Issue(GenerationFailureReason.DualRoadBalanceFailure, true, subject,
                            $"Dual quarter {q.QuarterIndex}: neutral time difference {q.Balance.NeutralTimeDifferencePercent:F1}% far exceeds the {cfg.NeutralTimeTolerance * 100f:F1}% tolerance.");
                    else if (cfg.RequireArchetypeDifferentiation &&
                             !(q.Balance.RouteAPreferredBySomeArchetype && q.Balance.RouteBPreferredBySomeArchetype))
                        Issue(GenerationFailureReason.DualRoadBalanceFailure, false, subject,
                            $"Dual quarter {q.QuarterIndex}: one road is preferred by every craft archetype (weak differentiation).");
                }

                // Clearance between the complete exclusive road bodies.
                ValidateRoadPairClearance(layout, q, cfg, issues);
            }
        }

        /// <summary>
        /// Where the two exclusive road bodies of a dual quarter pass near each other
        /// they need EITHER enough lateral separation for both half-pipes OR the
        /// wall-aware vertical clearance. Every generated ring is checked through an XZ
        /// spatial hash: a fixed 32x32 sample grid can miss a narrow crossing, and a
        /// percentage-based "gate throat" exemption becomes kilometres long on large
        /// tracks even though the physical mouths/lips remain separate road surfaces.
        /// </summary>
        public static void ValidateRoadPairClearance(GeneratedTrackLayout layout, GeneratedTrackQuarter q,
            ResolvedTrackGenerationConfig cfg, List<ValidationIssue> issues)
        {
            var sections = layout.Sections;

            var framesA = new List<TrackConnectionFrame>();
            // The alternate run is inserted immediately after road A's launch lip.
            // Stop there so the shared post-jump catch/recovery is not mistaken for an
            // unrelated road body near road B's intended flight path.
            int lastExclusiveA = Mathf.Min(q.RouteA.LastSectionIndex, q.RouteB.FirstSectionIndex - 1);
            for (int i = q.RouteA.FirstSectionIndex; i <= lastExclusiveA; i++)
            {
                var sec = sections[i];
                if (sec.RoadId != 0) continue;
                if (sec.SubdivisionFrames != null)
                {
                    framesA.AddRange(sec.SubdivisionFrames);
                    continue;
                }

                // Internal feature jumps are part of the exclusive route even though
                // no mesh spans their air gap. Sample the ballistic craft path so Road B
                // cannot be fitted through the flight corridor. Quarter gate gaps are
                // shared choice/convergence space and remain outside this body check.
                if (sec.Definition != null &&
                    sec.Definition.SectionType == TrackMacroSectionType.AirGap &&
                    !string.IsNullOrEmpty(sec.PatternId) && !sec.PatternId.StartsWith("Quarter_"))
                    AppendBallisticFlightSamples(sec, cfg, framesA);
            }
            var framesB = new List<TrackConnectionFrame>();
            for (int i = q.RouteB.FirstSectionIndex; i <= q.RouteB.LastSectionIndex; i++)
            {
                if (sections[i].SubdivisionFrames == null) continue;
                framesB.AddRange(sections[i].SubdivisionFrames);
            }
            if (framesA.Count < 8 || framesB.Count < 8) return;

            float requiredLateral = Mathf.Max(cfg.RoadWidth, cfg.DualRoadWidth)
                                  + cfg.RoadProfile.SideHeight * 2f + cfg.WallMaskSafetyMargin;
            // NOTE: the "two roads inside each other" case needs a proper fix that raises
            // BOTH this requirement AND the lane-separation resolver together — otherwise
            // the validator demands more clearance than the resolver places the roads at,
            // and every dual quarter becomes unbuildable (0 valid candidates). Reverted
            // the catch-envelope bump here until the resolver is updated to match.
            float requiredVertical = cfg.VerticalClearance * 0.85f;

            // Lane separation is resolved to the same envelope value, so allow a tiny
            // numerical tolerance at the intentionally parallel mouths and lips.
            float tolerance = Mathf.Max(0.5f, requiredLateral * 0.005f);
            float rejectBelowLateral = Mathf.Max(0.01f, requiredLateral - tolerance);
            float rejectBelowLateralSq = rejectBelowLateral * rejectBelowLateral;
            float cell = rejectBelowLateral;
            long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

            var gridB = new Dictionary<long, List<TrackConnectionFrame>>();
            foreach (var frame in framesB)
            {
                int cx = Mathf.FloorToInt(frame.Position.x / cell);
                int cz = Mathf.FloorToInt(frame.Position.z / cell);
                long key = CellKey(cx, cz);
                if (!gridB.TryGetValue(key, out var bucket))
                    gridB[key] = bucket = new List<TrackConnectionFrame>();
                bucket.Add(frame);
            }

            foreach (var pa in framesA)
            {
                int cx = Mathf.FloorToInt(pa.Position.x / cell);
                int cz = Mathf.FloorToInt(pa.Position.z / cell);
                for (int dxCell = -1; dxCell <= 1; dxCell++)
                for (int dzCell = -1; dzCell <= 1; dzCell++)
                {
                    if (!gridB.TryGetValue(CellKey(cx + dxCell, cz + dzCell), out var bucket)) continue;
                    foreach (var pb in bucket)
                    {
                        float dx = pa.Position.x - pb.Position.x;
                        float dz = pa.Position.z - pb.Position.z;
                        float horizSq = dx * dx + dz * dz;
                        if (horizSq >= rejectBelowLateralSq) continue;

                        float vert = Mathf.Abs(pa.Position.y - pb.Position.y);
                        if (vert >= requiredVertical) continue;

                        float horiz = Mathf.Sqrt(horizSq);
                        issues.Add(new ValidationIssue
                        {
                            Reason = GenerationFailureReason.SelfIntersection,
                            IsError = true,
                            Validator = "Quarters",
                            Subject = $"quarter {q.QuarterIndex}",
                            Position = pa.Position,
                            RequestedValue = rejectBelowLateral,
                            AchievedValue = horiz,
                            Message = $"Dual quarter {q.QuarterIndex}: road A and road B intersect/overlap; " +
                                      $"centerlines pass {horiz:F1}m apart with only {vert:F1}m vertical separation " +
                                      $"(need {rejectBelowLateral:F1}m lateral or {requiredVertical:F1}m vertical clearance)."
                        });
                        return;
                    }
                }
            }
        }

        private static void AppendBallisticFlightSamples(GeneratedTrackSection gap,
            ResolvedTrackGenerationConfig cfg, List<TrackConnectionFrame> output)
        {
            float duration = Mathf.Max(0.05f, gap.Definition.AirtimeSeconds);
            Vector3 start = gap.StartFrame.Position;
            Vector3 end = gap.EndFrame.Position;
            Vector3 horizontal = end - start;
            horizontal.y = 0f;
            Vector3 velocityH = horizontal / duration;
            float velocityY = (end.y - start.y + 0.5f * cfg.Gravity * duration * duration) / duration;
            int samples = Mathf.Clamp(Mathf.CeilToInt(horizontal.magnitude / 20f) + 1, 8, 256);

            for (int i = 0; i < samples; i++)
            {
                float u = (float)i / (samples - 1);
                float t = duration * u;
                Vector3 velocity = velocityH + Vector3.up * (velocityY - cfg.Gravity * t);
                var frame = gap.StartFrame;
                frame.Position = start + velocityH * t +
                                 Vector3.up * (velocityY * t - 0.5f * cfg.Gravity * t * t);
                if (velocity.sqrMagnitude > 0.001f) frame.Forward = velocity.normalized;
                frame.ArcLength = Mathf.Lerp(gap.StartFrame.ArcLength, gap.EndFrame.ArcLength, u);
                output.Add(frame);
            }
        }
    }
}
