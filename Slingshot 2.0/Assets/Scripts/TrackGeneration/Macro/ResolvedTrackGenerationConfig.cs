using UnityEngine;
using TrackGeneration.Core;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// Internal generation values derived from the 10 designer parameters
    /// (<see cref="TrackDesignerProfile"/>) clamped by the TrackConfig rulebook.
    ///
    ///     TrackConfig limits + designer parameters = ResolvedTrackGenerationConfig
    ///
    /// The macro generator uses ONLY this class internally. Resolution is a pure
    /// function of (limits, profile) — no randomness, determinism stays intact.
    /// When designer intent is disallowed by the rulebook, Resolve() logs loudly
    /// instead of failing silently.
    /// </summary>
    public class ResolvedTrackGenerationConfig
    {
        // ── Size ──
        public int TargetMacroSectionCount;
        public float TargetTrackLength;
        public float MaxTrackLength;   // absolute rulebook cap — explicit TurnCount may grow the lap up to this

        // ── Road ──
        public float RoadWidth;
        public float MinStraightLength;
        public float MaxStraightLength;

        // ── Corners ──
        public float MinCurveRadius;
        public float MaxCurveRadius;
        public float[] CornerAngleOptions;
        public float MaxBankAngle;
        public float BankTransitionLength;

        // Explicit corner count (0 = automatic). Above 0 the layout plans MIXED
        // left/right corners (mountain-pass style) whose signed sum closes the lap.
        public int TargetTurnCount;

        // Chance that a near-180° corner is realized as a half-loop + half-twist
        // (Immelmann) instead of a flat hairpin. Requires loops AND corkscrews allowed.
        public float HalfLoopTwistChance;

        // Chance per corner gap of a full-revolution climbing/descending spiral.
        public float SpiralChance;

        // ── Feature chances (pre-gated by TrackConfig Allowed Features) ──
        public float JumpChance;
        public float BoostChance;
        public float RouteSplitChance;
        public float ChicaneChance;
        public float HairpinChance;
        public float SCurveChance;
        public float LoopChance;
        public float CorkscrewChance;

        // ── Verticality ──
        public float TargetElevationAmplitude;
        public float ElevationSectionChance;
        public float ClimbChance;
        public float DropChance;
        public float CrestChance;
        public float BridgeChance;
        public float UnderpassChance;
        public float LayeredRouteChance;
        public float OverUnderCrossingChance;

        public int MinMajorElevationSections;
        public int MaxMajorElevationSections;
        public int MinLayeredRouteGroups;
        public int MaxLayeredRouteGroups;

        public bool ForceAtLeastOneOverpass;
        public bool ForceAtLeastOneLayeredRoute;

        public float MinElevationStep;
        public float MaxElevationStep;
        public float MinRollingHillHeight;
        public float MaxRollingHillHeight;
        public float MinBridgeHeight;
        public float MaxBridgeHeight;
        public float MinUnderpassDepth;
        public float MaxUnderpassDepth;
        public float MinClimbLength;
        public float MaxClimbLength;
        public float MaxClimbAngle;   // degrees, comfortable or aggressive by difficulty/verticality
        public float MaxDropAngle;

        // ── Layered route groups ──
        public float RouteWidth;
        public float LayeredRouteApproachLength;
        public float MinLayeredRouteLength;
        public float MaxLayeredRouteLength;
        public float LayeredRouteRecoveryLength;
        public float LayeredHeightSeparation;
        public float OverpassClearance;

        // ── Jumps ──
        public float MaxJumpHeight;
        public float JumpApproachLength;
        public float JumpRecoveryLength;

        // ── Boost straights ──
        public float MinBoostStraightLength;
        public float MaxBoostStraightLength;

        // ── Safety ──
        public float RecoveryLength;
        public float VerticalClearance;

        // ── Loops ──
        public float MinLoopRadius;
        public float MaxLoopRadius;
        public float LoopApproachLength;
        public float LoopRecoveryLength;
        public float LoopMetersPerRing;

        // ── Corkscrews ──
        public float MinCorkscrewLength;
        public float MaxCorkscrewLength;
        public float MinCorkscrewRadius;
        public float MaxCorkscrewRadius;
        public float CorkscrewRollDegrees;
        public float CorkscrewApproachLength;
        public float CorkscrewRecoveryLength;
        public float CorkscrewMetersPerRing;

        // ── Global half-pipe road cross-section ──
        // Half-pipe is the GLOBAL road shape, not a feature: every generated road
        // section uses it. FlatWithWalls only appears when the rulebook disallows it.
        public bool HalfPipeEnabled;
        public TrackRoadProfileSettings RoadProfile;

        // ── Section transition blending ──
        public TrackTransitionSmoothness TransitionSmoothness;
        public TrackBlendCurve BlendCurve;
        public float TransitionBlendLength;
        public float BankBlendLength;
        public float PitchBlendLength;
        public float WidthBlendLength;
        public float CrossSectionBlendLength;
        public float MaxBankRampAngle; // degrees — bank blends auto-expand to respect this

        // ── Route split structure (sideways FIRST, then up/down) ──
        public float RouteSplitApproachLength;
        public float LateralSeparationLength;
        public float VerticalDivergenceDelay;
        public float VerticalDivergenceLength;
        public float VerticalConvergenceLength;
        public float LateralMergeLength;
        public float PostMergeRecoveryLength;
        public float RouteLateralSeparation;

        // ── Junction wall masks (split/merge open throats — inner half-pipe walls) ──
        public float SplitInnerWallFadeOutLength;  // open-throat length after the split before the inner wall may regrow
        public float SplitInnerWallFadeInLength;   // ease length of the inner wall regrowing after lateral separation
        public float MergeInnerWallFadeOutLength;  // ease length of the inner wall fading before the merge
        public float MergeInnerWallFadeInLength;   // open-throat length before the merge where the inner wall must be gone
        public float WallMaskSafetyMargin;         // extra centerline separation (m) required before inner walls regrow

        // ── Mesh ──
        public float MeshMetersPerRing;
        public int MaxRingsPerSection;
        public int MaxTotalRings;

        // Maximum bend angle between consecutive rings (degrees). Curved sections
        // (loops, corkscrews, banked arcs) get extra rings until no facet exceeds
        // this — the physical "bumpiness" of curved track is the facet angle, and
        // at racing speed every degree of facet reads as ±3.5 m/s of phantom
        // vertical velocity to the hover suspension.
        public float MaxRingFacetAngle;

        // ── Debug labels (for the §7 summary output) ──
        public string DebugSpeedLabel;
        public string DebugVerticalityLabel;

        public static ResolvedTrackGenerationConfig Resolve(TrackConfig limits, TrackDesignerProfile profile)
        {
            var r = new ResolvedTrackGenerationConfig();

            int diff = (int)profile.Difficulty;
            int speed = (int)profile.SpeedProfile;
            int stunts = (int)profile.StuntDensity;
            int vert = (int)profile.Verticality;
            int branch = (int)profile.Branching;
            int loops = (int)profile.LoopFrequency;
            int corks = (int)profile.CorkscrewFrequency;

            r.DebugSpeedLabel = profile.SpeedProfile.ToString();
            r.DebugVerticalityLabel = profile.Verticality.ToString();

            // ══ 2. Track Length ══
            float[] lengthByPreset = { 1800f, 3200f, 5200f, 8000f };
            r.TargetTrackLength = Mathf.Clamp(lengthByPreset[(int)profile.Length], limits.MinTrackLength, limits.MaxTrackLength);
            r.MaxTrackLength = limits.MaxTrackLength;
            r.TargetMacroSectionCount = Mathf.Clamp(Mathf.RoundToInt(r.TargetTrackLength / 200f), 8, 40);

            // ══ 5. Track Width ══
            float[] widthByPreset = { 9f, 14f, 20f, 28f };
            float width = widthByPreset[(int)profile.Width];
            if (speed == 3) width *= 1.2f; // InsaneSpeed: wider roads
            r.RoadWidth = Mathf.Clamp(width, limits.MinRoadWidth, limits.MaxRoadWidth);

            // ══ 4. Speed Profile — uses the rulebook's dedicated support ranges ══
            switch (speed)
            {
                case 0: // Flowing: long straights, huge banked sweepers, few interruptions
                    r.MinStraightLength = Mathf.Lerp(limits.MinStraightLength, limits.MaxStraightLength, 0.2f);
                    r.MaxStraightLength = Mathf.Lerp(limits.MinStraightLength, limits.MaxStraightLength, 0.6f);
                    r.MinCurveRadius = limits.MinFlowingCurveRadius;
                    r.MaxCurveRadius = limits.MaxFlowingCurveRadius;
                    r.BoostChance = 0.3f;
                    break;
                case 1: // Balanced: mid band between technical and flowing
                    r.MinStraightLength = Mathf.Lerp(limits.MinTechnicalStraightLength, limits.MinInsaneSpeedStraightLength, 0.35f);
                    r.MaxStraightLength = Mathf.Lerp(limits.MaxTechnicalStraightLength, limits.MaxInsaneSpeedStraightLength, 0.3f);
                    r.MinCurveRadius = Mathf.Lerp(limits.MinTechnicalCurveRadius, limits.MinFlowingCurveRadius, 0.5f);
                    r.MaxCurveRadius = Mathf.Lerp(limits.MaxTechnicalCurveRadius, limits.MaxFlowingCurveRadius, 0.4f);
                    r.BoostChance = 0.35f;
                    break;
                case 2: // Technical: tight but readable
                    r.MinStraightLength = limits.MinTechnicalStraightLength;
                    r.MaxStraightLength = limits.MaxTechnicalStraightLength;
                    r.MinCurveRadius = limits.MinTechnicalCurveRadius;
                    r.MaxCurveRadius = limits.MaxTechnicalCurveRadius;
                    r.BoostChance = 0.2f;
                    break;
                default: // InsaneSpeed: enormous straights and sweepers
                    r.MinStraightLength = limits.MinInsaneSpeedStraightLength;
                    r.MaxStraightLength = limits.MaxInsaneSpeedStraightLength;
                    r.MinCurveRadius = Mathf.Lerp(limits.MinFlowingCurveRadius, limits.MaxFlowingCurveRadius, 0.25f);
                    r.MaxCurveRadius = limits.MaxFlowingCurveRadius;
                    r.BoostChance = 0.6f;
                    break;
            }

            // Global rulebook clamps on top of the profile ranges.
            r.MinStraightLength = Mathf.Clamp(r.MinStraightLength, limits.MinStraightLength, limits.MaxStraightLength);
            r.MaxStraightLength = Mathf.Clamp(r.MaxStraightLength, r.MinStraightLength, limits.MaxStraightLength);
            r.MinCurveRadius = Mathf.Clamp(r.MinCurveRadius, limits.MinCurveRadius, limits.MaxCurveRadius);
            r.MaxCurveRadius = Mathf.Clamp(r.MaxCurveRadius, r.MinCurveRadius, limits.MaxCurveRadius);

            float[] bankTransBySpeed = { 45f, 28f, 16f, 60f };
            r.BankTransitionLength = Mathf.Clamp(bankTransBySpeed[speed], limits.MinBankTransitionLength, limits.MaxBankTransitionLength);

            // ══ 3. Difficulty ══
            float[] hairpinByDiff = { 0f, 0.15f, 0.3f, 0.45f };
            float[] chicaneByDiff = { 0.05f, 0.15f, 0.3f, 0.4f };
            float[] sCurveByDiff = { 0.15f, 0.2f, 0.3f, 0.35f };
            float[] bankScaleByDiff = { 0.5f, 0.65f, 0.85f, 1f };
            float[] recoveryByDiff = { 130f, 100f, 85f, 75f };
            float[] jumpHeightByDiff = { 3f, 4f, 5f, 6f };

            float chicaneSpeedScale = speed == 2 ? 1.5f : (speed == 0 ? 0.5f : (speed == 3 ? 0.4f : 1f));
            float hairpinSpeedScale = speed == 0 || speed == 3 ? 0.4f : 1f;
            float sCurveSpeedScale = speed == 0 ? 1.3f : 1f;

            r.HairpinChance = Mathf.Clamp01(hairpinByDiff[diff] * hairpinSpeedScale);
            r.ChicaneChance = Mathf.Clamp01(chicaneByDiff[diff] * chicaneSpeedScale);
            r.SCurveChance = Mathf.Clamp01(sCurveByDiff[diff] * sCurveSpeedScale);
            r.MaxBankAngle = limits.MaxBankAngle * bankScaleByDiff[diff] * (speed == 3 ? 1f / Mathf.Max(0.01f, bankScaleByDiff[diff]) : 1f);
            r.MaxBankAngle = Mathf.Min(r.MaxBankAngle, limits.MaxBankAngle); // InsaneSpeed banks at the legal max
            r.RecoveryLength = Mathf.Clamp(Mathf.Max(limits.MinRecoveryLength, recoveryByDiff[diff]),
                                           limits.MinRecoveryStraightLength, limits.MaxRecoveryStraightLength);
            r.MaxJumpHeight = jumpHeightByDiff[diff];

            // Corner menu, filtered by the rulebook's legal angle window. 45/60/90 keep the
            // 360° plan composable; the layout falls back safely if the filter breaks that.
            float[][] anglesByDiff =
            {
                new[] { 45f, 60f, 90f },
                new[] { 45f, 60f, 90f, 120f },
                new[] { 45f, 60f, 90f, 120f, 135f },
                new[] { 45f, 60f, 90f, 120f, 180f }
            };
            var filtered = new System.Collections.Generic.List<float>();
            foreach (float a in anglesByDiff[diff])
            {
                if (a >= limits.MinCurveAngle && a <= limits.MaxCurveAngle) filtered.Add(a);
            }
            r.CornerAngleOptions = filtered.Count > 0 ? filtered.ToArray() : new[] { 45f, 60f, 90f };

            // Explicit designer turn count (0 = automatic circle-composition planner).
            r.TargetTurnCount = Mathf.Clamp(profile.TurnCount, 0, 30);

            // Immelmann (half loop + half twist) replaces some near-180° corners when
            // both loops and corkscrews are legal; scaled by the loop frequency preset.
            float[] halfLoopByFreq = { 0f, 0.25f, 0.4f, 0.6f };
            r.HalfLoopTwistChance = limits.AllowLoops && limits.AllowCorkscrews ? halfLoopByFreq[loops] : 0f;

            // Spirals (parking-garage helix) have their OWN designer control —
            // independent of verticality, like loops and corkscrews.
            float[] spiralByFreq = { 0f, 0.2f, 0.35f, 0.55f };
            r.SpiralChance = spiralByFreq[(int)profile.SpiralFrequency];

            // ══ 8. Stunt density ══
            float[] jumpByDensity = { 0f, 0.2f, 0.4f, 0.65f };
            r.JumpChance = limits.AllowJumps ? jumpByDensity[stunts] : 0f;
            if (stunts > 0 && !limits.AllowJumps)
                Debug.LogWarning("[Resolve] Jump density requested, but AllowJumps is false in TrackConfig — no jumps will be generated.");
            if (stunts == 0) r.BoostChance = 0f;
            r.JumpApproachLength = limits.MinJumpApproachLength;
            r.JumpRecoveryLength = Mathf.Max(limits.MinJumpExitRecoveryLength, r.RecoveryLength);
            r.MinBoostStraightLength = limits.MinBoostStraightLength;
            r.MaxBoostStraightLength = limits.MaxBoostStraightLength;

            // ══ 6. Verticality ══
            // Amplitudes per preset target the spec's expected height ranges:
            // Flat 0-5m, Rolling 15-45m, Layered 50-120m, RainbowRoad 100-220m.
            float[] amplitudeByVert = { 0f, 35f, 95f, 170f };
            r.TargetElevationAmplitude = Mathf.Min(amplitudeByVert[vert], limits.MaxElevationChange);

            float[] elevSectionByVert = { 0f, 0.45f, 0.6f, 0.75f };
            float[] climbByVert = { 0f, 0.35f, 0.55f, 0.7f };
            float[] crestByVert = { 0f, 0.45f, 0.3f, 0.35f };
            float[] bridgeByVert = { 0f, 0f, 0.5f, 0.7f };
            float[] underByVert = { 0f, 0f, 0.4f, 0.6f };
            float[] layeredByVert = { 0f, 0f, 0.6f, 0.85f };
            float[] crossingByVert = { 0f, 0f, 0.5f, 0.8f };

            r.ElevationSectionChance = elevSectionByVert[vert];
            r.ClimbChance = climbByVert[vert];
            r.DropChance = climbByVert[vert];
            r.CrestChance = crestByVert[vert];
            r.BridgeChance = limits.AllowOverpasses ? bridgeByVert[vert] : 0f;
            r.UnderpassChance = limits.AllowUnderpasses ? underByVert[vert] : 0f;
            // DECOUPLED: Branching alone decides IF the track splits; verticality only
            // decides whether existing splits are height-separated / crossing.
            r.LayeredRouteChance = 0f;
            r.OverUnderCrossingChance = limits.AllowLayeredRouteCrossings && branch > 0 ? crossingByVert[vert] : 0f;

            int[] minMajorByVert = { 0, 1, 2, 3 };
            int[] maxMajorByVert = { 0, 3, 4, 6 };
            r.MinMajorElevationSections = Mathf.Min(minMajorByVert[vert], limits.MaxMajorElevationChangesPerTrack);
            r.MaxMajorElevationSections = Mathf.Min(maxMajorByVert[vert], limits.MaxMajorElevationChangesPerTrack);

            // DECOUPLED: verticality never forces splits into a track whose Branching
            // preset is None — splits belong to Branching alone.
            r.ForceAtLeastOneLayeredRoute = branch >= 2 && limits.AllowRouteSplits;
            r.ForceAtLeastOneOverpass = vert == 3 && branch > 0 && limits.AllowLayeredRouteCrossings;

            // Step / hill / bridge magnitudes from the rulebook, scaled by preset intensity.
            float vertIntensity = vert switch { 0 => 0f, 1 => 0.45f, 2 => 0.75f, _ => 1f };
            r.MinElevationStep = limits.MinElevationStep;
            r.MaxElevationStep = Mathf.Lerp(limits.MinElevationStep, limits.MaxElevationStep, vertIntensity);
            r.MinRollingHillHeight = limits.MinRollingHillHeight;
            r.MaxRollingHillHeight = Mathf.Lerp(limits.MinRollingHillHeight, limits.MaxRollingHillHeight, Mathf.Max(0.4f, vertIntensity));
            r.MinBridgeHeight = limits.MinBridgeHeight;
            r.MaxBridgeHeight = Mathf.Lerp(limits.MinBridgeHeight, limits.MaxBridgeHeight, vertIntensity);
            r.MinUnderpassDepth = limits.MinUnderpassDepth;
            r.MaxUnderpassDepth = Mathf.Lerp(limits.MinUnderpassDepth, limits.MaxUnderpassDepth, vertIntensity);
            r.MinClimbLength = limits.MinClimbLength;
            r.MaxClimbLength = limits.MaxClimbLength;

            bool aggressive = diff >= 2 || vert == 3;
            r.MaxClimbAngle = Mathf.Min(aggressive ? limits.MaxAggressiveClimbAngle : limits.MaxComfortableClimbAngle, limits.MaxSlopeAngle);
            r.MaxDropAngle = Mathf.Min(aggressive ? limits.MaxAggressiveDropAngle : limits.MaxComfortableDropAngle, limits.MaxSlopeAngle);

            // ══ 7. Branching ══
            float[] splitByPreset = { 0f, 0.2f, 0.4f, 0.6f };
            r.RouteSplitChance = limits.AllowRouteSplits ? splitByPreset[branch] : 0f;
            if (branch > 0 && !limits.AllowRouteSplits)
                Debug.LogWarning("[Resolve] Branching requested, but AllowRouteSplits is false in TrackConfig — no route splits will be generated.");

            int[] minGroupsByBranch = { 0, 0, 1, 1 };
            int[] maxGroupsByBranch = { 0, 1, 2, 3 };
            int groupCap = Mathf.Min(limits.MaxRouteGroupsPerTrack, limits.MaxLayeredSectionsPerTrack);
            r.MinLayeredRouteGroups = limits.AllowRouteSplits ? Mathf.Min(Mathf.Max(minGroupsByBranch[branch], r.ForceAtLeastOneLayeredRoute ? 1 : 0), groupCap) : 0;
            r.MaxLayeredRouteGroups = limits.AllowRouteSplits ? Mathf.Clamp(Mathf.Max(maxGroupsByBranch[branch], r.MinLayeredRouteGroups), 0, groupCap) : 0;

            // Layered group geometry.
            r.RouteWidth = Mathf.Max(limits.MinRoadWidth, r.RoadWidth * 0.75f);
            r.LayeredRouteApproachLength = Mathf.Max(limits.MinLayeredRouteApproachLength, limits.MinRouteSplitApproachLength);
            r.MinLayeredRouteLength = Mathf.Max(limits.MinLayeredRouteLength, limits.MinRouteSplitLength);
            r.MaxLayeredRouteLength = Mathf.Min(limits.MaxLayeredRouteLength, limits.MaxRouteSplitLength);
            r.MaxLayeredRouteLength = Mathf.Max(r.MaxLayeredRouteLength, r.MinLayeredRouteLength);
            r.LayeredRouteRecoveryLength = Mathf.Max(limits.MinLayeredRouteRecoveryLength, limits.MinPostMergeRecoveryLength);

            float sepT = vert == 3 ? 0.7f : 0.35f;
            r.OverpassClearance = Mathf.Max(limits.MinOverpassClearance, limits.MinUnderpassClearance);
            r.LayeredHeightSeparation = Mathf.Clamp(
                Mathf.Lerp(limits.MinLayeredRouteHeightSeparation, limits.MaxLayeredRouteHeightSeparation, sepT),
                Mathf.Max(limits.MinLayeredRouteHeightSeparation, r.OverpassClearance + 4f),
                Mathf.Min(limits.MaxLayeredRouteHeightSeparation, limits.MaxRouteElevationOffset));

            r.VerticalClearance = limits.MinVerticalClearance;

            // ══ 9. Loops ══
            float[] loopByFreq = { 0f, 0.12f, 0.3f, 0.55f };
            r.LoopChance = limits.AllowLoops ? loopByFreq[loops] : 0f;
            if (loops > 0 && !limits.AllowLoops)
                Debug.LogWarning("[Resolve] Loop frequency requested, but AllowLoops is false in TrackConfig — no loops will be generated.");

            float pitchRateMinRadius = 360f / (2f * Mathf.PI * Mathf.Max(0.5f, limits.MaxLoopPitchRate));
            r.MinLoopRadius = Mathf.Max(limits.MinLoopRadius, pitchRateMinRadius);
            r.MaxLoopRadius = Mathf.Max(r.MinLoopRadius, limits.MaxLoopRadius);
            r.LoopApproachLength = Mathf.Max(limits.MinLoopApproachLength, limits.MinLoopEntrySpeedStraightLength);
            r.LoopRecoveryLength = Mathf.Max(limits.MinLoopExitRecoveryLength, r.RecoveryLength);
            r.LoopMetersPerRing = limits.LoopSubdivisionDensity;

            // ══ 10. Corkscrews ══
            float[] corkByFreq = { 0f, 0.12f, 0.3f, 0.5f };
            r.CorkscrewChance = limits.AllowCorkscrews ? corkByFreq[corks] : 0f;
            if (corks > 0 && !limits.AllowCorkscrews)
                Debug.LogWarning("[Resolve] Corkscrew frequency requested, but AllowCorkscrews is false in TrackConfig — no corkscrews will be generated.");

            r.CorkscrewRollDegrees = Mathf.Min(360f, limits.MaxCorkscrewRollDegrees); // V1: one full roll

            // Roll rate is per 10 m; smoothstepped roll peaks at 1.5× average.
            float rollRateMinLength = r.CorkscrewRollDegrees / Mathf.Max(1f, limits.MaxCorkscrewRollRate) * 10f * 1.5f;
            r.MinCorkscrewLength = Mathf.Max(limits.MinCorkscrewLength, rollRateMinLength);
            r.MaxCorkscrewLength = Mathf.Max(r.MinCorkscrewLength, limits.MaxCorkscrewLength);
            r.MinCorkscrewRadius = Mathf.Max(limits.MinCorkscrewRadius, r.RoadWidth * 0.6f);
            r.MaxCorkscrewRadius = Mathf.Max(r.MinCorkscrewRadius, limits.MaxCorkscrewRadius);
            r.CorkscrewApproachLength = Mathf.Max(limits.MinCorkscrewApproachLength, limits.MinCorkscrewEntrySpeedStraightLength);
            r.CorkscrewRecoveryLength = Mathf.Max(limits.MinCorkscrewExitRecoveryLength, r.RecoveryLength);
            r.CorkscrewMetersPerRing = limits.CorkscrewSubdivisionDensity;

            // ══ Global half-pipe road cross-section ══
            // The WHOLE track uses the half-pipe / water-slide shape by default.
            // Any section without it is a bug unless the rulebook disabled half-pipe roads.
            r.HalfPipeEnabled = limits.AllowHalfPipeRoads;
            if (!limits.AllowHalfPipeRoads)
                Debug.LogWarning("[Resolve] AllowHalfPipeRoads is false in TrackConfig — falling back to legacy FlatWithWalls roads. This is a debug/legacy mode, not the intended hovercraft track shape.");

            // Depth scales with difficulty + verticality; width and speed shape the bowl.
            float depthT = Mathf.Clamp01(0.35f + diff * 0.12f + vert * 0.08f);
            float wallT = Mathf.Clamp01(0.4f + diff * 0.15f);
            float flatT = speed == 3 ? 0.7f : (speed == 2 ? 0.3f : 0.5f); // insane speed = wider stable center

            r.RoadProfile = new TrackRoadProfileSettings
            {
                Shape = r.HalfPipeEnabled ? RoadCrossSectionShape.HalfPipe : RoadCrossSectionShape.FlatWithWalls,
                SideHeight = Mathf.Lerp(limits.MinHalfPipeSideHeight, limits.MaxHalfPipeSideHeight, depthT),
                CurveStrength = Mathf.Lerp(limits.MinHalfPipeCurveStrength, limits.MaxHalfPipeCurveStrength, 0.35f),
                WallAngle = Mathf.Lerp(limits.MinHalfPipeWallAngle, limits.MaxHalfPipeWallAngle, wallT),
                CenterFlatWidthRatio = Mathf.Lerp(limits.MinHalfPipeCenterFlatWidthRatio, limits.MaxHalfPipeCenterFlatWidthRatio, flatT),
                ProfileResolution = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(limits.MinHalfPipeProfileResolution, limits.MaxHalfPipeProfileResolution, r.RoadWidth / Mathf.Max(1f, limits.MaxRoadWidth))), limits.MinHalfPipeProfileResolution, limits.MaxHalfPipeProfileResolution),
                SafetyLipHeight = 1.0f
            };

            // ══ Section transition blending ══
            // Smoothness is folded into Speed Profile (no extra designer control):
            // Flowing = Smooth, Balanced = Smooth, Technical = Normal, InsaneSpeed = VerySmooth.
            r.TransitionSmoothness = speed switch
            {
                2 => TrackTransitionSmoothness.Normal,
                3 => TrackTransitionSmoothness.VerySmooth,
                _ => TrackTransitionSmoothness.Smooth
            };
            r.BlendCurve = TrackBlendCurve.SmootherStep;

            float smoothT = r.TransitionSmoothness switch
            {
                TrackTransitionSmoothness.Snappy => 0.1f,
                TrackTransitionSmoothness.Normal => 0.35f,
                TrackTransitionSmoothness.Smooth => 0.6f,
                _ => 0.85f
            };
            r.TransitionBlendLength = Mathf.Lerp(limits.MinTransitionBlendLength, limits.MaxTransitionBlendLength, smoothT);
            r.BankBlendLength = Mathf.Lerp(limits.MinBankBlendLength, limits.MaxBankBlendLength, smoothT);
            r.PitchBlendLength = Mathf.Lerp(limits.MinPitchBlendLength, limits.MaxPitchBlendLength, smoothT);
            r.WidthBlendLength = Mathf.Lerp(limits.MinWidthBlendLength, limits.MaxWidthBlendLength, smoothT);
            r.CrossSectionBlendLength = Mathf.Lerp(limits.MinCrossSectionBlendLength, limits.MaxCrossSectionBlendLength, smoothT);
            r.MaxBankRampAngle = limits.MaxBankRampAngle;

            // ══ Route split structure: sideways first, then up/down ══
            float splitT = Mathf.Clamp01(0.3f + branch * 0.15f + vert * 0.1f);
            r.RouteSplitApproachLength = Mathf.Max(limits.MinRouteSplitApproachLength, limits.MinLayeredRouteApproachLength);
            r.LateralSeparationLength = Mathf.Lerp(limits.MinLateralSeparationLength, limits.MaxLateralSeparationLength, smoothT);
            r.VerticalDivergenceDelay = Mathf.Lerp(limits.MinVerticalDivergenceDelay, limits.MaxVerticalDivergenceDelay, smoothT * 0.6f);
            r.VerticalDivergenceLength = Mathf.Lerp(limits.MinVerticalDivergenceLength, limits.MaxVerticalDivergenceLength, splitT);
            r.VerticalConvergenceLength = r.VerticalDivergenceLength;
            r.LateralMergeLength = r.LateralSeparationLength;
            r.PostMergeRecoveryLength = Mathf.Max(limits.MinPostMergeRecoveryLength, limits.MinLayeredRouteRecoveryLength);
            r.RouteLateralSeparation = Mathf.Clamp(r.RouteWidth * 0.5f + r.RoadWidth * 0.5f + 3f,
                limits.MinRouteLateralSeparation, limits.MaxRouteLateralSeparation);

            // ══ Junction wall masks: split/merge inner-wall open throats ══
            // The split reads as a shared open throat first — the inner half-pipe walls stay
            // suppressed until the branches are laterally separated, then regrow smoothly.
            // Smoother presets get slightly longer eases; the throat lengths stay fixed.
            r.SplitInnerWallFadeOutLength = 40f;
            r.SplitInnerWallFadeInLength = Mathf.Lerp(60f, 100f, smoothT);
            r.MergeInnerWallFadeOutLength = Mathf.Lerp(60f, 100f, smoothT);
            r.MergeInnerWallFadeInLength = 40f;
            r.WallMaskSafetyMargin = 3f;

            // ══ Physical vertical clearance ══
            // A road is not a flat line: its rideable half-pipe walls occupy SideHeight
            // above the floor (up to 2× when bobsled-banked) plus the slab below.
            // Any over/under crossing must clear ALL of that, not just the craft —
            // otherwise "vertically separated" layouts still physically intersect.
            float wallClearance = r.RoadProfile.SideHeight * 2f + 6f;
            r.VerticalClearance = Mathf.Max(r.VerticalClearance, limits.MinVerticalClearance + wallClearance);
            r.OverpassClearance = Mathf.Max(r.OverpassClearance, r.VerticalClearance);

            // ══ Mesh ══
            float[] ringSpacingBySpeed = { 2.5f, 2.5f, 2f, 3f };
            r.MeshMetersPerRing = Mathf.Clamp(ringSpacingBySpeed[speed], limits.MinMetersPerRing, limits.MaxMetersPerRing);
            r.MaxRingsPerSection = limits.MaxRingsPerMacroSection;
            r.MaxTotalRings = limits.MaxTotalTrackRings;

            // Faster speed presets get finer curved-track facets (higher speed makes
            // the same facet angle feel bumpier: phantom vertVel = v·Δθ/2).
            r.MaxRingFacetAngle = speed >= 3 ? 1.2f : 1.5f;

            // §7 debug: resolved speed profile summary.
            Debug.Log($"[Resolve] Speed profile: {r.DebugSpeedLabel} | straights {r.MinStraightLength:F0}-{r.MaxStraightLength:F0}m | curve radius {r.MinCurveRadius:F0}-{r.MaxCurveRadius:F0}m | max bank {r.MaxBankAngle:F0}° | width {r.RoadWidth:F0}m\n" +
                      $"[Resolve] Verticality: {r.DebugVerticalityLabel} | target amplitude {r.TargetElevationAmplitude:F0}m | major sections {r.MinMajorElevationSections}-{r.MaxMajorElevationSections} | layered groups {r.MinLayeredRouteGroups}-{r.MaxLayeredRouteGroups} | force layered: {r.ForceAtLeastOneLayeredRoute} | force crossing: {r.ForceAtLeastOneOverpass}\n" +
                      $"[Resolve] Road shape: {r.RoadProfile.Shape} | side height {r.RoadProfile.SideHeight:F1}m | wall angle {r.RoadProfile.WallAngle:F0}° | flat center {r.RoadProfile.CenterFlatWidthRatio:P0} | resolution {r.RoadProfile.ProfileResolution}/side\n" +
                      $"[Resolve] Transitions: {r.TransitionSmoothness} ({r.BlendCurve}) | bank blend {r.BankBlendLength:F0}m | pitch {r.PitchBlendLength:F0}m | width {r.WidthBlendLength:F0}m | cross-section {r.CrossSectionBlendLength:F0}m | max bank ramp {r.MaxBankRampAngle:F0}°");

            return r;
        }
    }
}
