using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Branching
{
    /// <summary>
    /// Builds the two routes of a branch group as genuinely different paths between a
    /// shared entry gate and a shared merge gate:
    ///
    ///   shared readable approach → fork → Route A ∥ Route B → controlled merge → recovery
    ///
    /// Routes use an offset-path model around the group corridor: staged lateral split
    /// (sideways FIRST), delayed vertical divergence, independent bodies (weaving,
    /// elevation, width, crossovers, shared-axis orbits), vertical convergence, then a
    /// broad lateral merge. Gate connection is guaranteed by construction; route
    /// character comes from the pairing mode and per-route settings.
    /// </summary>
    public static class BranchRoutePlanner
    {
        /// <summary>
        /// Initializes the two route specs from the pairing mode. Custom keeps the
        /// designer's per-route settings verbatim.
        /// </summary>
        public static void ApplyPairingMode(PlannedBranchGroup group, ref Unity.Mathematics.Random rng)
        {
            var a = group.RouteASpec;
            var b = group.RouteBSpec;

            switch (group.PairingMode)
            {
                case BranchPairingMode.Contrasting:
                    a.Style = BranchRouteStyle.Flowing;
                    a.WeaveIntensity = 0.2f; a.WidthScale = 1.1f; a.RiskTarget = 0.35f; a.ElevationBias = 0.3f;
                    b.Style = BranchRouteStyle.Technical;
                    b.WeaveIntensity = 0.85f; b.WidthScale = 0.85f; b.RiskTarget = 0.7f; b.ElevationBias = -0.3f;
                    break;

                case BranchPairingMode.Similar:
                    float w = rng.NextFloat(0.35f, 0.6f);
                    a.Style = BranchRouteStyle.InheritTrack; b.Style = BranchRouteStyle.InheritTrack;
                    a.WeaveIntensity = w; b.WeaveIntensity = Mathf.Clamp01(w + rng.NextFloat(-0.1f, 0.1f));
                    a.WidthScale = 1f; b.WidthScale = 1f;
                    a.RiskTarget = 0.5f; b.RiskTarget = 0.5f;
                    break;

                case BranchPairingMode.Mirrored:
                    a.Style = BranchRouteStyle.InheritTrack; b.Style = BranchRouteStyle.InheritTrack;
                    b.WeaveIntensity = a.WeaveIntensity;
                    b.WidthScale = a.WidthScale;
                    b.ElevationBias = a.ElevationBias;
                    b.RiskTarget = a.RiskTarget;
                    break;

                case BranchPairingMode.AlternatingAdvantage:
                    a.Style = BranchRouteStyle.Technical; b.Style = BranchRouteStyle.Technical;
                    a.WeaveIntensity = 0.6f; b.WeaveIntensity = 0.6f;
                    a.WidthScale = 0.95f; b.WidthScale = 0.95f;
                    a.RiskTarget = 0.55f; b.RiskTarget = 0.55f;
                    break;

                case BranchPairingMode.SafeVersusRisky:
                    a.Style = BranchRouteStyle.Safe;
                    a.WeaveIntensity = 0.25f; a.WidthScale = 1.15f; a.RiskTarget = 0.2f; a.ElevationBias = -0.5f;
                    b.Style = BranchRouteStyle.Risky;
                    b.WeaveIntensity = 0.7f; b.WidthScale = 0.8f; b.RiskTarget = 0.9f; b.ElevationBias = 0.5f;
                    b.AllowRollFeature = true;
                    break;

                case BranchPairingMode.FeatureVersusGround:
                    a.Style = BranchRouteStyle.HighFeature;
                    a.WeaveIntensity = 0.4f; a.WidthScale = 1f; a.RiskTarget = 0.75f; a.ElevationBias = 1f;
                    a.AllowRollFeature = true;
                    b.Style = BranchRouteStyle.LowGround;
                    b.WeaveIntensity = 0.45f; b.WidthScale = 1.05f; b.RiskTarget = 0.35f; b.ElevationBias = -1f;
                    b.AllowRollFeature = false;
                    break;
            }
        }

        // ─────────────────────────── Route pair building ───────────────────────────

        /// <summary>Result of building one branch pair.</summary>
        public class PairResult
        {
            public TrackConnectionFrame[] FramesA;
            public TrackConnectionFrame[] FramesB;
            public GeneratedBranchGroup Group;
            public bool BalanceValid;
            public string FailureMessage = "";
        }

        /// <summary>
        /// Builds both routes and iterates on the slower/faster route until the neutral
        /// time difference fits the tolerance (or attempts run out). Deterministic via
        /// the group's stored route seeds.
        /// </summary>
        public static PairResult BuildPair(TrackConnectionFrame entry, PlannedBranchGroup group,
            TrackMacroSectionDefinition defA, TrackMacroSectionDefinition defB,
            ResolvedTrackGenerationConfig cfg, in FrameBuildContext ctx)
        {
            var result = new PairResult();

            float weaveAdjustA = 0f, weaveAdjustB = 0f;
            BranchBalanceMetrics balance = null;

            for (int iteration = 0; iteration < 5; iteration++)
            {
                result.FramesA = BuildRoute(entry, group, defA, routeIndex: 0, weaveAdjustA, cfg, ctx);
                result.FramesB = BuildRoute(entry, group, defB, routeIndex: 1, weaveAdjustB, cfg, ctx);

                balance = RoutePerformanceEstimator.Evaluate(group.GroupId, result.FramesA, result.FramesB, cfg);

                float tolerance = cfg.TimeBalanceTolerance;

                // A safe/risky pairing may give the RISKY route a small theoretical edge.
                bool riskyEdgeOk = false;
                if (group.PairingMode == BranchPairingMode.SafeVersusRisky && balance.TimeTable.Count > 0)
                {
                    var neutral = balance.TimeTable[0];
                    bool riskyIsA = group.RouteASpec.RiskTarget > group.RouteBSpec.RiskTarget;
                    float advantageRisky = riskyIsA ? neutral.AdvantageA : -neutral.AdvantageA;
                    float mean = Mathf.Max(0.5f, (neutral.RouteASeconds + neutral.RouteBSeconds) * 0.5f);
                    riskyEdgeOk = advantageRisky > 0f && advantageRisky / mean <= cfg.SpecializationTarget + tolerance;
                }

                if (balance.NeutralTimeDifference <= tolerance || riskyEdgeOk)
                    break;

                // Slow the faster route down: more weave (extra path length + curvature)
                // and a slightly narrower road (precision cost in the estimator).
                var neutralRow = balance.TimeTable[0];
                if (neutralRow.RouteASeconds < neutralRow.RouteBSeconds)
                {
                    weaveAdjustA += 0.18f;
                    group.RouteASpec.WidthScale = Mathf.Max(0.6f, group.RouteASpec.WidthScale * 0.95f);
                }
                else
                {
                    weaveAdjustB += 0.18f;
                    group.RouteBSpec.WidthScale = Mathf.Max(0.6f, group.RouteBSpec.WidthScale * 0.95f);
                }
            }

            result.Group = new GeneratedBranchGroup
            {
                BranchGroupId = group.GroupId,
                PairingMode = group.PairingMode,
                InteractionPattern = group.InteractionPattern,
                EntryGate = result.FramesA[0],
                MergeGate = result.FramesA[result.FramesA.Length - 1],
                Balance = balance,
                RouteA = DescribeRoute(0, group, result.FramesA, cfg),
                RouteB = DescribeRoute(1, group, result.FramesB, cfg)
            };

            result.BalanceValid = balance != null &&
                (balance.NeutralTimeDifference <= cfg.TimeBalanceTolerance * 1.2f ||
                 group.PairingMode == BranchPairingMode.SafeVersusRisky);
            if (!result.BalanceValid)
                result.FailureMessage =
                    $"Branch group {group.GroupId}: neutral route time difference {balance?.NeutralTimeDifference:P1} exceeds the {cfg.TimeBalanceTolerance:P1} tolerance after balancing iterations.";

            return result;
        }

        private static GeneratedRoute DescribeRoute(int index, PlannedBranchGroup group, TrackConnectionFrame[] frames,
            ResolvedTrackGenerationConfig cfg)
        {
            var spec = index == 0 ? group.RouteASpec : group.RouteBSpec;
            float length = frames[frames.Length - 1].ArcLength - frames[0].ArcLength;

            var route = new GeneratedRoute
            {
                RouteId = index,
                DisplayName = index == 0 ? $"RouteA_{spec.Style}" : $"RouteB_{spec.Style}",
                Style = spec.Style,
                PhysicalLength = length,
                Demand = RoutePerformanceEstimator.MeasureDemand(frames, cfg, spec.RiskTarget)
            };

            var neutral = CraftArchetypeProfile.Defaults()[0];
            route.EstimatedNeutralTime = RoutePerformanceEstimator.EstimateTime(frames, cfg, neutral);
            return route;
        }

        // ─────────────────────────── Single route ───────────────────────────

        /// <summary>
        /// Builds one route's frames. The route is a parameterized offset path around the
        /// corridor axis: staged split/merge envelopes guarantee the shared gates, and the
        /// body expresses the route's personality (weave, elevation, crossovers, orbit).
        /// </summary>
        private static TrackConnectionFrame[] BuildRoute(TrackConnectionFrame entry, PlannedBranchGroup group,
            TrackMacroSectionDefinition def, int routeIndex, float weaveBoost,
            ResolvedTrackGenerationConfig cfg, in FrameBuildContext ctx)
        {
            var spec = routeIndex == 0 ? group.RouteASpec : group.RouteBSpec;
            var rng = new Unity.Mathematics.Random(routeIndex == 0 ? group.RouteASeed : group.RouteBSeed);

            float L = group.CorridorLength;
            float halfSep = group.LateralSeparation * 0.5f;
            float vSep = group.VerticalSeparation;
            int crossovers = group.Crossovers;
            bool sharedAxis = group.InteractionPattern == BranchInteractionPattern.SharedAxis;
            bool pairedFeature = group.InteractionPattern == BranchInteractionPattern.PairedFeature;
            bool converging = group.InteractionPattern == BranchInteractionPattern.Converging;
            TrackBlendCurve curve = ctx.BlendCurve;

            int rings = Mathf.Clamp(Mathf.CeilToInt(L / ctx.MetersPerRing) + 1, 32, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = SectionFrameBuilders.Flatten(entry.Forward);
            Vector3 rightH = SectionFrameBuilders.Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            // ── Zone breakpoints (normalized) ──
            float uSplit = Mathf.Clamp01(cfg.SplitLength / L);
            float uMergeStart = Mathf.Clamp01(1f - cfg.MergeLength / L);
            float uV0 = Mathf.Clamp01(cfg.VerticalDivergenceDelay / L);
            // KEY RULE: vertical divergence never starts before the lateral split is readable.
            uV0 = Mathf.Max(uV0, uSplit);
            float uV1 = Mathf.Clamp01(uV0 + cfg.VerticalDivergenceLength / L);
            float uC1 = uMergeStart;
            float uC0 = Mathf.Clamp(uC1 - cfg.VerticalDivergenceLength / L, uV1, uC1);

            def.RouteZoneBoundaries = new[] { uSplit, uV0, uV1, uC0, uC1, uMergeStart };

            float Envelope(float u)
            {
                float rise = uSplit > 0.0001f ? TrackBlend.Evaluate(curve, u / uSplit) : 1f;
                float fall = uMergeStart < 0.9999f ? TrackBlend.Evaluate(curve, (1f - u) / (1f - uMergeStart)) : 1f;
                return Mathf.Min(rise, fall);
            }

            float VerticalEnvelope(float u)
            {
                float rise = uV1 > uV0 ? TrackBlend.Evaluate(curve, (u - uV0) / (uV1 - uV0)) : (u >= uV0 ? 1f : 0f);
                float fall = uC1 > uC0 ? TrackBlend.Evaluate(curve, (uC1 - u) / (uC1 - uC0)) : (u <= uC1 ? 1f : 0f);
                return Mathf.Min(rise, fall);
            }

            // ── Personality parameters (deterministic per route seed) ──
            float sideSign = routeIndex == 0 ? 1f : -1f;
            float weave = Mathf.Clamp01(spec.WeaveIntensity + weaveBoost);

            // Weave must never consume the wall-aware clearance corridor between the
            // routes. Vertical separation only licenses bigger weave when the routes hold
            // DISJOINT height bands through the whole body — a High/Low exchange passes
            // through equal heights mid-swap, so it keeps the lateral corridor intact.
            float clearanceFloor = cfg.RoadWidth + cfg.RoadProfile.SideHeight * 2f + cfg.WallMaskSafetyMargin;
            bool verticalRescue = vSep > 0.01f && (crossovers > 0 || sharedAxis) && !pairedFeature;
            float weaveRoom = verticalRescue
                ? halfSep * 0.45f
                : Mathf.Max(0f, (group.LateralSeparation - clearanceFloor) * 0.35f);

            // Gentle weave: enough curvature to read as a different line and shape the
            // demand profile, never enough to cripple the route's speed at 1300 km/h.
            float weaveAmp = Mathf.Min(4f + weave * 10f, weaveRoom);
            int weaveCycles = 2 + Mathf.RoundToInt(weave * 2f) + rng.NextInt(0, 2);
            float weavePhase = rng.NextFloat(0f, Mathf.PI * 2f);

            // Converging routes may drift together, but never below the clearance corridor.
            float minConvergeScale = Mathf.Clamp01((clearanceFloor + 6f) / Mathf.Max(1f, group.LateralSeparation));
            float convergeScale = converging ? Mathf.Max(rng.NextFloat(0.45f, 0.65f), minConvergeScale) : 1f;

            // Crossover flip profile: X(u) transitions +1 → −1 (→ +1 …) inside the body.
            float CrossoverSign(float u)
            {
                if (crossovers <= 0) return 1f;
                float body0 = uV1;
                float body1 = uC0;
                if (body1 <= body0 + 0.01f) return 1f;

                float t = Mathf.Clamp01((u - body0) / (body1 - body0));
                float x = 1f;
                for (int k = 0; k < crossovers; k++)
                {
                    float center = (k + 1f) / (crossovers + 1f);
                    const float halfWidth = 0.12f;
                    float f = TrackBlend.Evaluate(curve, (t - (center - halfWidth)) / (2f * halfWidth));
                    x *= 1f - 2f * f; // smooth +1 → −1 flip around each crossover center
                }
                return x;
            }

            // Vertical layers: crossovers/braids stack route A above route B for safety;
            // PairedFeature exchanges the high line mid-body; otherwise elevation bias decides.
            float HeightAt(float u)
            {
                if (vSep <= 0.01f) return 0f;
                float env = VerticalEnvelope(u);

                if (pairedFeature)
                {
                    // High/Low exchange: A holds the high line first, B second.
                    float body0 = uV1;
                    float body1 = uC0;
                    float t = body1 > body0 ? Mathf.Clamp01((u - body0) / (body1 - body0)) : 0f;
                    float exchange = TrackBlend.Evaluate(curve, Mathf.Clamp01((t - 0.4f) / 0.2f));
                    float highA = 1f - exchange;
                    return vSep * env * (routeIndex == 0 ? highA : 1f - highA);
                }

                if (crossovers > 0 || sharedAxis)
                    return routeIndex == 0 ? vSep * env : 0f;

                bool thisHigh = (routeIndex == 0 ? spec.ElevationBias : spec.ElevationBias) >= 0f
                    ? routeIndex == 0
                    : routeIndex == 1;
                return thisHigh ? vSep * env : 0f;
            }

            // ── Shared-axis orbit (twin corkscrew / double helix) ──
            // Orbit turns are clamped by the rulebook roll rate: the road's twist about
            // the travel axis peaks near 1.9× the average with the eased profile.
            float maxOrbitTurns = cfg.MaxRollRateDegPerMeter * L / (360f * 1.9f);
            float orbitTurns = sharedAxis ? Mathf.Min(rng.NextFloat(0.75f, 1.5f), maxOrbitTurns) : 0f;
            bool orbitOpposite = sharedAxis && rng.NextBool();

            Vector3 PosAt(float u)
            {
                float s = L * u;

                if (sharedAxis)
                {
                    float env = Envelope(u);
                    float R = halfSep * env;
                    float profile = TrackBlend.Evaluate(curve, u); // orbit progresses across the corridor
                    float dir = orbitOpposite && routeIndex == 1 ? -1f : 1f;
                    float phi = (routeIndex == 0 ? 0f : Mathf.PI) + dir * orbitTurns * 2f * Mathf.PI * profile;

                    float axisLift = (R + 4f) * env; // keeps the low point of the orbit above grade
                    return basePos + fwdH * s
                         + rightH * (Mathf.Cos(phi) * R)
                         + Vector3.up * (axisLift + Mathf.Sin(phi) * R);
                }

                float lat = sideSign * halfSep * CrossoverSign(u) * Envelope(u) * convergeScale
                          + Mathf.Sin((u * weaveCycles * 2f * Mathf.PI) + weavePhase) * weaveAmp * Envelope(u);
                float h = HeightAt(u);

                return basePos + fwdH * s + rightH * lat + Vector3.up * h;
            }

            // ── Frame generation ──
            float widthTarget = def.Width;
            float arc = entry.ArcLength;
            Vector3 prevPos = PosAt(0f);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);

                float eps = 0.35f / rings;
                Vector3 tangent = PosAt(Mathf.Min(1f, u + eps)) - PosAt(Mathf.Max(0f, u - eps));
                Vector3 fwd = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : fwdH;

                Vector3 up;
                float bankMeta = 0f;
                if (sharedAxis)
                {
                    // The road faces the orbit axis: centripetal support at speed.
                    float env = Envelope(u);
                    Vector3 pos = PosAt(u);
                    Vector3 axisPoint = basePos + fwdH * (L * u) + Vector3.up * ((halfSep * env + 4f) * env);
                    Vector3 toAxis = axisPoint - pos;
                    Vector3 axisUp = toAxis.sqrMagnitude > 0.01f ? toAxis.normalized : Vector3.up;
                    up = Vector3.Slerp(Vector3.up, axisUp, env).normalized;
                    bankMeta = Vector3.SignedAngle(Vector3.up, up, fwd);
                }
                else
                {
                    up = Vector3.up;

                    // Analytic weave bank METADATA for the balance estimator only (the
                    // weave is a known sine, so its curvature is exact and smooth).
                    // Wall suppressions are NOT set here — the candidate builder's
                    // global banking field derives them from the final frames, so
                    // branch walls follow the same blurred lap-wide signal as the
                    // main line instead of a route-local one.
                    float omega = weaveCycles * 2f * Mathf.PI;
                    float ampEff = weaveAmp * Envelope(u);
                    float signedKappa = -ampEff * omega * omega / (L * L) * Mathf.Sin(u * omega + weavePhase);
                    float kappaAbs = Mathf.Abs(signedKappa);

                    const float kappaLo = 1f / 3500f, kappaHi = 1f / 900f;
                    float support = SectionFrameBuilders.Smooth01(Mathf.Clamp01((kappaAbs - kappaLo) / (kappaHi - kappaLo)));
                    if (support > 0.001f)
                        bankMeta = Mathf.Sign(signedKappa) * support * 60f;
                }

                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                Vector3 position = PosAt(u);
                arc += (position - prevPos).magnitude;
                prevPos = position;

                // Width forks from the shared entry width to the route width and back.
                float widthT = Mathf.Clamp01(Mathf.Min(u, 1f - u) / Mathf.Max(0.05f, uSplit));
                float wNow = Mathf.Lerp(entry.Width, widthTarget, TrackBlend.Evaluate(curve, widthT));

                frames[i] = new TrackConnectionFrame
                {
                    Position = position,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = wNow,
                    BankAngle = bankMeta,
                    PitchAngle = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg,
                    ArcLength = arc
                };
            }

            // Exact shared gates (identical for both routes of the pair).
            var first = frames[0];
            first.Position = entry.Position;
            first.Forward = fwdH;
            first.Right = rightH;
            first.Up = Vector3.up;
            first.Width = entry.Width;
            first.BankAngle = 0f;
            first.PitchAngle = 0f;
            first.ArcLength = entry.ArcLength;
            frames[0] = first;

            var last = frames[rings - 1];
            last.Position = entry.Position + fwdH * L;
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.Width = entry.Width;
            last.BankAngle = 0f;
            last.PitchAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Junction wall masks ───────────────────────────

        /// <summary>
        /// Suppresses the INNER half-pipe walls of a split-route pair near the split and
        /// merge so the fork reads as one shared open throat. Each branch keeps its outer
        /// wall; inner walls regrow only after real separation, and fade before the merge.
        /// </summary>
        public static void MaskSplitPair(GeneratedTrackSection a, GeneratedTrackSection b, ResolvedTrackGenerationConfig cfg)
        {
            var fa = a.SubdivisionFrames;
            var fb = b.SubdivisionFrames;
            if (fa == null || fb == null || fa.Length < 2 || fb.Length < 2) return;

            int rings = Mathf.Min(fa.Length, fb.Length);
            float L = Mathf.Max(0.01f, a.EndFrame.ArcLength - a.StartFrame.ArcLength);

            float required = Mathf.Max(a.Definition.Width, b.Definition.Width)
                           + cfg.RoadProfile.SideHeight * 2f
                           + Mathf.Max(0f, cfg.WallMaskSafetyMargin);
            float verticalOk = Mathf.Max(1f, cfg.VerticalClearance);

            var separated = new bool[rings];
            for (int r = 0; r < rings; r++)
            {
                // Compare frames at matching normalized progress (routes have different ring counts).
                var pa = fa[Mathf.Min(fa.Length - 1, Mathf.RoundToInt((float)r / (rings - 1) * (fa.Length - 1)))];
                var pb = fb[Mathf.Min(fb.Length - 1, Mathf.RoundToInt((float)r / (rings - 1) * (fb.Length - 1)))];
                Vector3 d = pa.Position - pb.Position;
                float horizSq = d.x * d.x + d.z * d.z;
                separated[r] = horizSq >= required * required || Mathf.Abs(d.y) >= verticalOk;
            }

            int firstOk = -1, lastOk = -1;
            for (int r = 0; r < rings; r++) if (separated[r]) { firstOk = r; break; }
            for (int r = rings - 1; r >= 0; r--) if (separated[r]) { lastOk = r; break; }

            float ArcOf(int r) => (float)r / (rings - 1) * L;

            float sGrow = firstOk >= 0 ? Mathf.Max(cfg.SplitInnerWallFadeOutLength, ArcOf(firstOk)) : float.MaxValue;
            float sFadeEnd = lastOk >= 0 ? Mathf.Min(L - cfg.MergeInnerWallFadeInLength, ArcOf(lastOk)) : float.MinValue;

            MaskRoute(a, sGrow, sFadeEnd, cfg);
            MaskRoute(b, sGrow, sFadeEnd, cfg);
        }

        private static void MaskRoute(GeneratedTrackSection route, float sGrow, float sFadeEnd, ResolvedTrackGenerationConfig cfg)
        {
            var def = route.Definition;
            var frames = route.SubdivisionFrames;

            bool startInnerIsLeft = def.RouteLateralStart > 0f;
            bool endInnerIsLeft = def.RouteLateralEnd > 0f;
            bool crossing = startInnerIsLeft != endInnerIsLeft;

            for (int r = 0; r < frames.Length; r++)
            {
                var f = frames[r];
                float s = f.ArcLength - route.StartFrame.ArcLength;

                float grow = TrackBlend.Evaluate(cfg.BlendCurve,
                    (s - sGrow) / Mathf.Max(1f, cfg.SplitInnerWallFadeInLength));
                float fade = TrackBlend.Evaluate(cfg.BlendCurve,
                    (sFadeEnd - s) / Mathf.Max(1f, cfg.MergeInnerWallFadeOutLength));

                if (crossing)
                {
                    SetSideSuppression(ref f, startInnerIsLeft, 1f - grow);
                    SetSideSuppression(ref f, endInnerIsLeft, 1f - fade);
                }
                else
                {
                    SetSideSuppression(ref f, startInnerIsLeft, 1f - Mathf.Min(grow, fade));
                }

                frames[r] = f;
            }

            var sf = route.StartFrame;
            sf.LeftWallSuppression = frames[0].LeftWallSuppression;
            sf.RightWallSuppression = frames[0].RightWallSuppression;
            route.StartFrame = sf;

            var ef = route.EndFrame;
            ef.LeftWallSuppression = frames[frames.Length - 1].LeftWallSuppression;
            ef.RightWallSuppression = frames[frames.Length - 1].RightWallSuppression;
            route.EndFrame = ef;
        }

        private static void SetSideSuppression(ref TrackConnectionFrame f, bool leftSide, float suppression)
        {
            suppression = Mathf.Clamp01(suppression);
            if (leftSide) f.LeftWallSuppression = Mathf.Max(f.LeftWallSuppression, suppression);
            else f.RightWallSuppression = Mathf.Max(f.RightWallSuppression, suppression);
        }
    }
}
