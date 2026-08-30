using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Shared parameters for frame building, resolved once per candidate.</summary>
    public struct FrameBuildContext
    {
        public float MetersPerRing;          // ordinary sections
        public float FeatureMetersPerRing;   // loops / corkscrews / ramps
        public float MaxFacetAngle;          // degrees per ring, hard quality bound
        public int MaxRingsPerSection;
        public TrackBlendCurve BlendCurve;
        public float BankTransitionLength;   // meters
        public float WidthTransitionLength;  // meters
        public float MaxClimbAngle;          // degrees
        public float MaxDropAngle;           // degrees
        public float FloorTiltFraction;      // fraction of the bank realized as geometric floor tilt
        public float RotationUnitDegrees;
        public float MaxRotationalSampleDistance;
        public float MaxRotationalForwardAngle;
        public float MaxRotationalRollAngle;
        public int MinSamplesPerRotationUnit;

        public static FrameBuildContext From(ResolvedTrackGenerationConfig cfg) => new FrameBuildContext
        {
            MetersPerRing = Mathf.Max(0.5f, cfg.MeshMetersPerRing),
            FeatureMetersPerRing = Mathf.Max(0.5f, cfg.FeatureMetersPerRing),
            MaxFacetAngle = Mathf.Max(0.1f, cfg.MaxRingFacetAngle),
            MaxRingsPerSection = Mathf.Max(32, cfg.MaxRingsPerSection),
            BlendCurve = cfg.BlendCurve,
            BankTransitionLength = cfg.BankTransitionLength,
            WidthTransitionLength = cfg.WidthTransitionLength,
            MaxClimbAngle = cfg.MaxClimbAngle,
            MaxDropAngle = cfg.MaxDropAngle,
            FloorTiltFraction = cfg.FloorTiltFraction,
            RotationUnitDegrees = Mathf.Max(1f, cfg.RotationUnitDegrees),
            MaxRotationalSampleDistance = Mathf.Max(0.25f, cfg.MaxRotationalSampleDistance),
            MaxRotationalForwardAngle = Mathf.Max(0.05f, cfg.MaxRotationalForwardAngle),
            MaxRotationalRollAngle = Mathf.Max(0.05f, cfg.MaxRotationalRollAngle),
            MinSamplesPerRotationUnit = Mathf.Max(4, cfg.MinSamplesPerRotationUnit)
        };
    }

    /// <summary>
    /// Pure geometry builders: each turns one section definition into subdivision ring
    /// frames, honoring the weld contract (a section's exit frame IS the next section's
    /// entry frame, exact to float precision — no post-hoc warping anywhere).
    ///
    /// The math ports the proven pieces of the previous generator: eased clothoid-style
    /// loop curvature, analytic corkscrew tangents, bobsled banking (flat floor, boosted
    /// outside wall), smoothstepped elevation carriers with zero end slopes.
    /// </summary>
    public static class SectionFrameBuilders
    {
        // (Per-section bobsled banking removed: wall support, floor tilt and bank
        // metadata all come from the candidate builder's GLOBAL banking field.)

        // ── Eased (clothoid-like) loop curvature ──
        public const float LoopCurvatureEaseFraction = 0.18f;
        private const int LoopProfileSamples = 512;

        private static float[] _loopThetaTable;
        private static Vector2[] _loopPlaneTable;
        private static float _loopForwardDisplacementFactor;

        private static float[] _halfLoopThetaTable;
        private static Vector2[] _halfLoopPlaneTable;

        // ── Eased HORIZONTAL arc curvature ──
        // A circular arc has a curvature DISCONTINUITY where it meets a straight
        // (κ: 0 → 1/R in one ring) — at racing speed the lateral-acceleration step is a
        // physical bump at every section connection. Corners therefore use the same
        // clothoid-style ease as loops: κ ramps 0 → 1/R → 0, the peak radius keeps its
        // designed value, and the arc grows by 1/(1−ease) in length. Profiles are cached
        // per 0.5°-quantized angle and shared by the PLANNER and the BUILDER, so the 2D
        // closure model and the built geometry agree to float precision.
        public const float ArcCurvatureEaseFraction = 0.18f;
        /// <summary>
        /// Hard per-section size ceiling for ordinary authored road. Long routes are
        /// composed from more sections instead of hiding multi-kilometre primitives.
        /// </summary>
        public const float MaxAuthoredRoadSectionLength = 1000f;
        public const float MaxDesignerSCurveLength = MaxAuthoredRoadSectionLength;
        public const float MaxOrdinaryCurveLength = MaxAuthoredRoadSectionLength;
        public const float MaxStraightSectionLength = MaxAuthoredRoadSectionLength;
        private const int ArcProfileSamples = 256;
        private static readonly System.Collections.Generic.Dictionary<int, (float[] theta, Vector2[] plane)> ArcProfiles =
            new System.Collections.Generic.Dictionary<int, (float[], Vector2[])>();

        /// <summary>Quantizes an arc angle to the 0.5° profile grid (plan and build MUST use identical keys).</summary>
        public static float QuantizeArcAngle(float angleDeg)
            => Mathf.Clamp(Mathf.Round(angleDeg * 2f) * 0.5f, 1f, 360f);

        private static (float[] theta, Vector2[] plane) GetArcProfile(float angleDeg)
        {
            int key = Mathf.RoundToInt(QuantizeArcAngle(angleDeg) * 2f);
            if (!ArcProfiles.TryGetValue(key, out var profile))
            {
                BuildPitchProfile(key * 0.5f * Mathf.Deg2Rad, ArcCurvatureEaseFraction, ArcProfileSamples,
                    out float[] theta, out Vector2[] plane);
                profile = (theta, plane);
                ArcProfiles[key] = profile;
            }
            return profile;
        }

        private static float SampleTable(float[] table, float u)
        {
            float x = Mathf.Clamp01(u) * (table.Length - 1);
            int i = Mathf.Min((int)x, table.Length - 2);
            return Mathf.Lerp(table[i], table[i + 1], x - i);
        }

        private static Vector2 SampleTable(Vector2[] table, float u)
        {
            float x = Mathf.Clamp01(u) * (table.Length - 1);
            int i = Mathf.Min((int)x, table.Length - 2);
            return Vector2.Lerp(table[i], table[i + 1], x - i);
        }

        /// <summary>Arc length of an eased arc: the circular length grown by 1/(1−ease) so the PEAK radius keeps its designed value.</summary>
        public static float EasedArcLength(float angleDeg, float radius)
            => Mathf.Deg2Rad * QuantizeArcAngle(angleDeg) * radius / (1f - ArcCurvatureEaseFraction);

        /// <summary>
        /// Keeps both opposed sweeps inside the designer S-curve length ceiling while
        /// preserving the selected angle. This feature-local radius may be tighter than
        /// the ordinary-corner style band; S-curves are authored features, not ordinary
        /// route corners.
        /// </summary>
        public static float ClampDesignerSCurveRadius(float angleDeg, float desiredRadius)
        {
            float maximumRadius = MaximumRadiusForEasedLength(angleDeg, 2f,
                MaxDesignerSCurveLength);
            return Mathf.Clamp(desiredRadius, 1f, maximumRadius);
        }

        /// <summary>Caps a single ordinary eased curve without changing its heading.</summary>
        public static float ClampOrdinaryCurveRadius(float angleDeg, float desiredRadius)
        {
            float maximumRadius = MaximumRadiusForEasedLength(angleDeg, 1f,
                MaxOrdinaryCurveLength);
            return Mathf.Clamp(desiredRadius, 1f, maximumRadius);
        }

        /// <summary>Largest radius whose composed eased arcs fit inside a length ceiling.</summary>
        public static float MaximumRadiusForEasedLength(float angleDeg, float arcCount,
            float maximumLength)
        {
            float lengthAtUnitRadius = Mathf.Max(0.0001f, arcCount) *
                                       EasedArcLength(angleDeg, 1f);
            return Mathf.Max(1f, maximumLength /
                                 Mathf.Max(0.0001f, lengthAtUnitRadius));
        }

        /// <summary>
        /// Driving-line length of an eased horizontal arc carrying a net-zero vertical
        /// crest. The horizontal curve is parameterized by arc length, so integrating
        /// sqrt(1 + verticalSlope²) gives the shared definition/build budget.
        /// </summary>
        public static float ElevatedEasedArcLength(float angleDeg, float radius, float crestHeight)
        {
            float horizontalLength = EasedArcLength(angleDeg, radius);
            if (Mathf.Abs(crestHeight) <= 0.001f) return horizontalLength;

            const int steps = 256;
            float length = 0f;
            float ds = horizontalLength / steps;
            float previousHeight = crestHeight * CrestBump(0f);
            for (int i = 1; i <= steps; i++)
            {
                float height = crestHeight * CrestBump((float)i / steps);
                float dh = height - previousHeight;
                length += Mathf.Sqrt(ds * ds + dh * dh);
                previousHeight = height;
            }
            return length;
        }

        /// <summary>End offset of an eased arc in entry-local axes: (forward run, lateral toward the turn side). Linear in radius.</summary>
        public static Vector2 EasedArcEndOffset(float angleDeg, float radius)
        {
            var profile = GetArcProfile(angleDeg);
            float length = EasedArcLength(angleDeg, radius);
            Vector2 end = profile.plane[profile.plane.Length - 1];
            return new Vector2(end.x * length, end.y * length);
        }

        /// <summary>Advances a 2D walk through an EASED arc (heading delta identical to the circular arc).</summary>
        public static void ApplyEasedArc2D(ref Vector2 pos, ref float heading, float signedAngleDeg, float radius)
        {
            float side = Mathf.Sign(signedAngleDeg);
            Vector2 offset = EasedArcEndOffset(Mathf.Abs(signedAngleDeg), radius);

            Vector2 fwd = HeadingToDir(heading);
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            pos += fwd * offset.x + right * (side * offset.y);
            heading += side * QuantizeArcAngle(Mathf.Abs(signedAngleDeg));
        }

        /// <summary>Walks an eased arc emitting plan-view samples roughly every <paramref name="step"/> meters.</summary>
        public static void WalkEasedArc2D(ref Vector2 pos, ref float heading, float signedAngleDeg, float radius,
            float step, System.Action<Vector2, float> emit, ref float arc)
        {
            float side = Mathf.Sign(signedAngleDeg);
            float angleAbs = QuantizeArcAngle(Mathf.Abs(signedAngleDeg));
            var profile = GetArcProfile(angleAbs);
            float length = EasedArcLength(angleAbs, radius);

            Vector2 fwd = HeadingToDir(heading);
            Vector2 right = new Vector2(fwd.y, -fwd.x);

            int samples = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(1f, step)));
            for (int k = 0; k < samples; k++)
            {
                float u = (float)k / samples;
                Vector2 pl = SampleTable(profile.plane, u) * length;
                emit(pos + fwd * pl.x + right * (side * pl.y), arc + length * u);
            }

            Vector2 end = profile.plane[profile.plane.Length - 1] * length;
            pos += fwd * end.x + right * (side * end.y);
            heading += side * angleAbs;
            arc += length;
        }

        /// <summary>
        /// Displacement of an eased S-bend (arc +α then −α, equal radii) per unit
        /// radius, in entry-local axes: (forward run, |lateral|). The circular
        /// closed form 2R(1−cos α) does not hold for eased arcs, so the closure
        /// solver measures the offset from the same profile the builder uses.
        /// </summary>
        public static Vector2 EasedSBendUnitOffset(float angleDeg)
        {
            Vector2 pos = Vector2.zero;
            float heading = 0f;
            ApplyEasedArc2D(ref pos, ref heading, angleDeg, 1f);
            ApplyEasedArc2D(ref pos, ref heading, -angleDeg, 1f);
            Vector2 fwd = HeadingToDir(0f);
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            return new Vector2(Vector2.Dot(pos, fwd), Mathf.Abs(Vector2.Dot(pos, right)));
        }

        // ─────────────────────────── Easing helpers ───────────────────────────

        /// <summary>Smoothstep 0→1 with zero derivative at both ends.</summary>
        public static float Smooth01(float u) => u * u * (3f - 2f * u);

        /// <summary>
        /// Minimum nominal climb per revolution needed when a multi-level spiral eases
        /// its TOTAL elevation with <see cref="Smooth01"/>. The first/last layer pair
        /// receives less than the nominal step, so using clearance directly is unsafe.
        /// </summary>
        public static float RequiredSpiralClimbPerRevolution(float clearance, int revolutions)
        {
            if (revolutions <= 1) return Mathf.Max(0f, clearance);
            float easedLayerFactor = revolutions * Smooth01(1f / revolutions);
            return Mathf.Max(0f, clearance) / Mathf.Max(0.001f, easedLayerFactor);
        }

        /// <summary>Rounds a vertical design value upward; safety clearances must never round down.</summary>
        public static float QuantizeElevationUp(float meters, float quantum = 5f)
            => Mathf.Ceil(Mathf.Max(0f, meters) / Mathf.Max(0.01f, quantum)) * Mathf.Max(0.01f, quantum);

        /// <summary>Net-zero bump 0→1→0 with zero end derivatives, peak at u = 0.5.</summary>
        public static float Bump(float u) => 4f * Smooth01(u) * Smooth01(1f - u);

        /// <summary>Analytic derivative of <see cref="Bump"/>; peak magnitude ≈ 3.04.</summary>
        public static float BumpDerivative(float u) => 24f * u * (1f - u) * (Smooth01(1f - u) - Smooth01(u));

        /// <summary>
        /// Quintic smoothstep used by definition-owned elevated turns. Unlike the
        /// ordinary hill carrier, its first AND second derivatives are zero at both
        /// ends, so a net-zero crest can weld to level track without a vertical
        /// curvature step.
        /// </summary>
        private static float Smoother01(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * u * (u * (u * 6f - 15f) + 10f);
        }

        private static float Smoother01Derivative(float u)
        {
            u = Mathf.Clamp01(u);
            return 30f * u * u * (1f - u) * (1f - u);
        }

        /// <summary>
        /// C2 net-zero crest 0→1→0 for elevated curve primitives. The value, slope,
        /// and vertical curvature all return to zero at each owned boundary.
        /// </summary>
        public static float CrestBump(float u)
            => 4f * Smoother01(u) * Smoother01(1f - u);

        /// <summary>Analytic derivative of <see cref="CrestBump"/>.</summary>
        public static float CrestBumpDerivative(float u)
            => 4f * (Smoother01Derivative(u) * Smoother01(1f - u) -
                     Smoother01(u) * Smoother01Derivative(1f - u));

        /// <summary>Ease 0→1 over easeFrac, hold, 1→0 over the exit easeFrac.</summary>
        /// <summary>
        /// Node slopes for monotone cubic Hermite interpolation of a CDF
        /// (Fritsch–Carlson). Returns dy/dx at each node, limited so the resulting
        /// cubic cannot overshoot or reverse between nodes — essential here because
        /// the interpolated value is rotation PROGRESS: any non-monotonic wobble
        /// would briefly unroll the road mid-feature.
        /// </summary>
        private static float[] BuildMonotoneSlopes(float[] y, int intervals)
        {
            var m = new float[intervals + 1];
            var d = new float[intervals];
            float h = 1f / intervals;

            for (int k = 0; k < intervals; k++)
                d[k] = (y[k + 1] - y[k]) / h;

            m[0] = d[0];
            m[intervals] = d[intervals - 1];
            for (int k = 1; k < intervals; k++)
                m[k] = (d[k - 1] + d[k]) * 0.5f;

            for (int k = 0; k < intervals; k++)
            {
                if (Mathf.Abs(d[k]) < 1e-12f)
                {
                    // Flat span: pin both ends or the cubic bulges off the plateau.
                    m[k] = 0f;
                    m[k + 1] = 0f;
                    continue;
                }

                float a = m[k] / d[k];
                float b = m[k + 1] / d[k];
                float s = a * a + b * b;
                if (s > 9f)
                {
                    float scale = 3f / Mathf.Sqrt(s);
                    m[k] = scale * a * d[k];
                    m[k + 1] = scale * b * d[k];
                }
            }

            return m;
        }

        /// <summary>
        /// THE single section-geometry dispatch (Stage A). Maps a macro definition to
        /// its frame builder — the exact switch the candidate builder executes, factored
        /// out so plan-time result measurement and runtime candidate building are the
        /// same calculation by construction, never two implementations.
        ///
        /// Candidate-level concerns deliberately stay OUTSIDE: the landing-ramp
        /// grade snap/validation (a weld correction inside rulebook tolerance) and
        /// air-gap ballistics (<see cref="AirGapLanding"/> — air gaps build no frames).
        /// </summary>
        public static TrackConnectionFrame[] BuildSectionFrames(in TrackConnectionFrame entry,
            TrackMacroSectionDefinition def, in FrameBuildContext ctx)
        {
            switch (def.SectionType)
            {
                case TrackMacroSectionType.BankedCurve:
                case TrackMacroSectionType.BankedHairpin:
                    return Mathf.Abs(def.HillHeight) > 0.001f
                        ? BuildElevatedArc(entry, def.TurnAngle * def.TurnSign, def.Radius,
                            def.BankingAngle, def.HillHeight, ctx)
                        : BuildArc(entry, def.TurnAngle * def.TurnSign, def.Radius,
                            def.BankingAngle, ctx);

                case TrackMacroSectionType.SCurve:
                    return BuildComposedArcs(entry,
                        new[] { def.TurnAngle * def.TurnSign, -def.TurnAngle * def.TurnSign },
                        new[] { def.Radius, def.Radius }, def.BankingAngle, ctx);

                case TrackMacroSectionType.Chicane:
                    return BuildComposedArcs(entry,
                        new[] { def.TurnAngle * def.TurnSign, -2f * def.TurnAngle * def.TurnSign, def.TurnAngle * def.TurnSign },
                        new[] { def.Radius, def.Radius, def.Radius }, def.BankingAngle, ctx);

                case TrackMacroSectionType.JumpRamp:
                    return BuildKeyframedPitchRamp(entry, def.Length,
                        LaunchRampKeys(def.SecondaryPitchDeg, def.PitchChange), ctx);

                case TrackMacroSectionType.LandingRamp:
                    return BuildKeyframedPitchRamp(entry, def.Length,
                        LandingRampKeys(def.PitchChange, def.SecondaryPitchDeg), ctx);

                case TrackMacroSectionType.Loop:
                    return BuildLoop(entry, def, ctx);

                case TrackMacroSectionType.Corkscrew:
                    return BuildCorkscrew(entry, def, ctx);

                case TrackMacroSectionType.Spiral:
                    return BuildSpiral(entry, def, ctx);

                case TrackMacroSectionType.HalfLoopTwist:
                    return BuildHalfLoopRollout(entry, def, ctx);

                case TrackMacroSectionType.RotationalEvent:
                    return BuildRotationalEvent(entry, def, ctx);

                case TrackMacroSectionType.FullPipe:
                    return BuildFullPipe(entry, def, ctx);

                case TrackMacroSectionType.WallrideTurn:
                    return BuildWallrideTurn(entry, def, ctx);

                case TrackMacroSectionType.AirGap:
                    return System.Array.Empty<TrackConnectionFrame>();

                default:
                    return BuildStraight(entry, def, ctx);
            }
        }

        /// <summary>
        /// The landing frame an air gap delivers from its launch-lip frame — the exact
        /// ballistic-arrival math the candidate builder applies (position from the plan
        /// scalars, arrival pitch from <c>PitchChange</c>, bank zeroed, catch width).
        /// One implementation, used by candidate building AND plan-result measurement.
        /// </summary>
        public static TrackConnectionFrame AirGapLanding(in TrackConnectionFrame lip,
            TrackMacroSectionDefinition def)
        {
            Vector3 dirH = Flatten(lip.Forward);
            Vector3 landingPos = lip.Position + dirH * def.PlanHorizontalLength
                               + Flatten(lip.Right) * def.PlanLateralOffset
                               + Vector3.up * def.ElevationChange;

            float arrivalRad = def.PitchChange * Mathf.Deg2Rad;
            Vector3 arrivalFwd = (dirH * Mathf.Cos(arrivalRad) + Vector3.up * Mathf.Sin(arrivalRad)).normalized;

            var landing = lip;
            landing.Position = landingPos;
            landing.Forward = arrivalFwd;
            landing.Right = Flatten(lip.Right);
            landing.Up = Vector3.Cross(arrivalFwd, landing.Right).normalized;
            landing.PitchAngle = def.PitchChange;
            landing.BankAngle = 0f;
            landing.ArcLength = lip.ArcLength + def.Length;
            if (def.Width > 1f) landing.Width = def.Width;
            return landing;
        }

        public static float HoldProfile(float u, float easeFrac, TrackBlendCurve curve)
        {
            if (u < easeFrac) return TrackBlend.Evaluate(curve, u / easeFrac);
            if (u > 1f - easeFrac) return TrackBlend.Evaluate(curve, (1f - u) / easeFrac);
            return 1f;
        }

        public static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        /// <summary>2D heading (degrees, 0 = +Z) to a unit direction.</summary>
        public static Vector2 HeadingToDir(float headingDeg)
        {
            float rad = headingDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        /// <summary>Advances a 2D walk through a circular arc.</summary>
        public static void ApplyArc2D(ref Vector2 pos, ref float heading, float signedAngleDeg, float radius)
        {
            float a = Mathf.Abs(signedAngleDeg) * Mathf.Deg2Rad;
            float side = Mathf.Sign(signedAngleDeg);

            Vector2 fwd = HeadingToDir(heading);
            Vector2 right = new Vector2(fwd.y, -fwd.x);

            pos += fwd * (radius * Mathf.Sin(a)) + right * (side * radius * (1f - Mathf.Cos(a)));
            heading += signedAngleDeg;
        }

        /// <summary>
        /// Advances the canonical 2D plan walk through a spiral-family section.
        /// Complete spirals return to their entry heading and only contribute their
        /// optional forward drift. A HalfHelixTurnaround is a partial circular helix,
        /// so it also contributes the authored turn and its natural lateral footprint.
        /// Keep this endpoint model paired with <see cref="BuildSpiral"/>.
        /// </summary>
        public static void ApplySpiral2D(ref Vector2 pos, ref float heading,
            TrackMacroSectionDefinition definition)
        {
            if (definition == null) return;

            Vector2 entryForward = HeadingToDir(heading);
            if (definition.SemanticElement == SemanticElementId.HalfHelixTurnaround)
            {
                float signedAngle = Mathf.Abs(definition.TurnAngle) *
                                    (definition.TurnSign != 0 ? definition.TurnSign : 1f);
                ApplyArc2D(ref pos, ref heading, signedAngle,
                    Mathf.Max(definition.Width, definition.Radius));
            }

            // BuildSpiral applies drift along the fixed entry axis, not the exit axis.
            pos += entryForward * Mathf.Max(0f, definition.PlanHorizontalLength);
        }

        // ─────────────────────────── Loop profile tables ───────────────────────────

        private static void EnsureLoopProfile()
        {
            if (_loopThetaTable != null) return;
            BuildPitchProfile(2f * Mathf.PI, LoopCurvatureEaseFraction, LoopProfileSamples, out _loopThetaTable, out _loopPlaneTable);
            _loopForwardDisplacementFactor = _loopPlaneTable[LoopProfileSamples].x;
        }

        private static void EnsureHalfLoopProfile()
        {
            if (_halfLoopThetaTable != null) return;
            BuildPitchProfile(Mathf.PI, LoopCurvatureEaseFraction, LoopProfileSamples, out _halfLoopThetaTable, out _halfLoopPlaneTable);
        }

        /// <summary>
        /// Builds an eased-curvature pitch profile: θ(u) is the cumulative integral of the
        /// eased curvature (normalized to the total angle), and the (forward, up) plane
        /// path is the cumulative integral of the unit tangent, normalized by arc length.
        /// A perfect circle has a curvature discontinuity at entry/exit — at racing speed
        /// that kink bumps the craft off the road; the ease removes it entirely.
        /// </summary>
        private static void BuildPitchProfile(float totalRadians, float easeFrac, int n, out float[] thetaTable, out Vector2[] planeTable)
        {
            var theta = new float[n + 1];
            var raw = new float[n + 1];

            for (int i = 0; i <= n; i++)
                raw[i] = HoldProfile((float)i / n, easeFrac, TrackBlendCurve.SmootherStep);

            float acc = 0f;
            for (int i = 1; i <= n; i++)
            {
                acc += (raw[i - 1] + raw[i]) * 0.5f / n;
                theta[i] = acc;
            }

            float scale = totalRadians / theta[n];
            for (int i = 0; i <= n; i++) theta[i] *= scale;

            var plane = new Vector2[n + 1];
            Vector2 p = Vector2.zero;
            for (int i = 1; i <= n; i++)
            {
                Vector2 t0 = new Vector2(Mathf.Cos(theta[i - 1]), Mathf.Sin(theta[i - 1]));
                Vector2 t1 = new Vector2(Mathf.Cos(theta[i]), Mathf.Sin(theta[i]));
                p += (t0 + t1) * (0.5f / n);
                plane[i] = p;
            }

            thetaTable = theta;
            planeTable = plane;
        }

        private static float TableThetaAt(float[] table, float u)
        {
            float x = Mathf.Clamp01(u) * LoopProfileSamples;
            int i = Mathf.Min((int)x, LoopProfileSamples - 1);
            return Mathf.Lerp(table[i], table[i + 1], x - i);
        }

        private static Vector2 TablePlaneAt(Vector2[] table, float u)
        {
            float x = Mathf.Clamp01(u) * LoopProfileSamples;
            int i = Mathf.Min((int)x, LoopProfileSamples - 1);
            return Vector2.Lerp(table[i], table[i + 1], x - i);
        }

        // ── Plan-time displacement helpers (analytic/deterministic, used by the 2D walk) ──

        /// <summary>Arc length of an eased full loop for a mid-arc radius.</summary>
        public static float LoopArcLength(float radius) => 2f * Mathf.PI * radius / (1f - LoopCurvatureEaseFraction);

        /// <summary>Net forward displacement of an eased loop of the given arc length.</summary>
        public static float LoopForwardDisplacement(float length)
        {
            EnsureLoopProfile();
            return _loopForwardDisplacementFactor * length;
        }

        /// <summary>Lateral exit offset that keeps a loop clear of its own entry.</summary>
        public static float LoopLateralOffset(float width) => width * 2f;

        /// <summary>Arc length of the eased half loop for a mid-arc radius.</summary>
        public static float HalfLoopArcLength(float radius) => Mathf.PI * radius / (1f - LoopCurvatureEaseFraction);

        /// <summary>Net forward displacement of the half loop (entry → inverted top).</summary>
        public static float HalfLoopForwardDisplacement(float halfArcLength)
        {
            EnsureHalfLoopProfile();
            return _halfLoopPlaneTable[LoopProfileSamples].x * halfArcLength;
        }

        /// <summary>Top height of the half loop (its exit elevation).</summary>
        public static float HalfLoopTopHeight(float halfArcLength)
        {
            EnsureHalfLoopProfile();
            return _halfLoopPlaneTable[LoopProfileSamples].y * halfArcLength;
        }

        /// <summary>
        /// Plan-time profile of a pitch ramp easing from startPitch to endPitch over
        /// arc length L: returns the horizontal run and vertical rise (numeric, 64 steps).
        /// </summary>
        public static void PitchRampSpan(float startPitchDeg, float endPitchDeg, float length,
            out float horizontal, out float vertical)
        {
            const int steps = 64;
            horizontal = 0f; vertical = 0f;
            for (int i = 0; i < steps; i++)
            {
                float u = (i + 0.5f) / steps;
                float pitch = Mathf.Lerp(startPitchDeg, endPitchDeg, Smooth01(u)) * Mathf.Deg2Rad;
                horizontal += Mathf.Cos(pitch) * (length / steps);
                vertical += Mathf.Sin(pitch) * (length / steps);
            }
        }

        // ─────────────────────────── Straight family ───────────────────────────

        /// <summary>
        /// Straight-family section (incl. bridge/tunnel crest variants): supports a net
        /// elevation change (smoothstepped climb/drop) plus a net-zero hill/dip bump.
        /// Zero slope at both ends — every straight welds flat.
        /// </summary>
        public static TrackConnectionFrame[] BuildStraight(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            float length = def.Length;
            float delta = def.IsJumpFamily ? 0f : def.ElevationChange;
            float hill = def.HillHeight;

            int rings = Mathf.Clamp(Mathf.CeilToInt(length / ctx.MetersPerRing) + 1, 2, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            float blendLen = Mathf.Min(ctx.WidthTransitionLength, length * 0.5f);

            Vector3 entryForward = entry.Forward.normalized;
            Vector3 entryRight = (entry.Right - Vector3.Dot(entry.Right, entryForward) * entryForward).normalized;

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float s = length * u;

                bool definitionOwnedVerticalBump = def.FeatureVerticalLobeCount > 0;
                float bump;
                float bumpDerivative;
                if (definitionOwnedVerticalBump)
                    SampleDefinitionVerticalBump(def, u, out bump, out bumpDerivative);
                else
                {
                    bump = Bump(u);
                    bumpDerivative = BumpDerivative(u);
                }
                float h = delta * Smooth01(u) + hill * bump;
                float slope = (delta * 6f * u * (1f - u) + hill * bumpDerivative) / Mathf.Max(length, 0.01f);

                Vector3 fwd = (entryForward + Vector3.up * slope).normalized;
                Quaternion transport = Quaternion.FromToRotation(entryForward, fwd);
                Vector3 right = (transport * entryRight).normalized;
                Vector3 up = Vector3.Cross(fwd, right).normalized;
                right = Vector3.Cross(up, fwd).normalized;

                float widthT = blendLen > 0.001f ? TrackBlend.Evaluate(ctx.BlendCurve, s / blendLen) : 1f;

                frames[i] = new TrackConnectionFrame
                {
                    Position = entry.Position + entryForward * s + Vector3.up * h,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = Mathf.Lerp(entry.Width, def.Width, widthT),
                    BankAngle = entry.BankAngle,
                    PitchAngle = Mathf.Rad2Deg * Mathf.Atan(slope),
                    AccumulatedRoadRoll = entry.AccumulatedRoadRoll,
                    AccumulatedVerticalRotation = entry.AccumulatedVerticalRotation,
                    HorizontalCurvature = entry.HorizontalCurvature,
                    HorizontalCurvatureRate = entry.HorizontalCurvatureRate,
                    VerticalCurvature = 0f,
                    VerticalCurvatureRate = 0f,
                    RoadRollRate = entry.RoadRollRate,
                    RoadRollAcceleration = 0f,
                    ArcLength = entry.ArcLength + s
                };
            }

            // Exact natural exit: the straight preserves the incoming frame instead of
            // flattening or unrolling it. Optional elevation shaping has zero end slope.
            var last = frames[rings - 1];
            last.Position = entry.Position + entryForward * length + Vector3.up * delta;
            last.Forward = entryForward;
            last.Right = entryRight;
            last.Up = Vector3.Cross(entryForward, entryRight).normalized;
            last.PitchAngle = Mathf.Asin(Mathf.Clamp(entryForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            frames[rings - 1] = last;

            frames[0] = entry;

            // Honest vertical geometry. BuildStraight previously wrote VerticalCurvature =
            // 0 even while the pitch swept 0→peak→0, hiding the harsh grade from every
            // downstream system. Measure it from the TRUE 3D arc length (ring-to-ring
            // spatial distance) — never the horizontal parameter — so diagnostics and
            // validators see the real vertical curvature (rad/m) and its rate (rad/m²).
            // Sign convention: +curvature = nose pitching up (valley / concave up),
            // −curvature = crest. Geometry is unchanged; only the metadata is corrected.
            if (rings >= 3)
            {
                float PitchRad(int idx) => frames[idx].PitchAngle * Mathf.Deg2Rad;
                float Ds3D(int a, int b) => Mathf.Max(1e-4f, (frames[b].Position - frames[a].Position).magnitude);

                var vc = new float[rings];
                for (int i = 1; i < rings - 1; i++)
                    vc[i] = (PitchRad(i + 1) - PitchRad(i - 1)) / Ds3D(i - 1, i + 1);
                vc[0] = entry.VerticalCurvature; // preserve incoming continuity at the weld
                vc[rings - 1] = (PitchRad(rings - 1) - PitchRad(rings - 2)) / Ds3D(rings - 2, rings - 1);

                for (int i = 0; i < rings; i++)
                {
                    var f = frames[i];
                    f.VerticalCurvature = vc[i];
                    f.VerticalCurvatureRate = i == 0
                        ? entry.VerticalCurvatureRate
                        : (vc[i] - vc[i - 1]) / Ds3D(i - 1, i);
                    frames[i] = f;
                }
            }

            return frames;
        }

        private static void SampleDefinitionVerticalBump(
            TrackMacroSectionDefinition def, float u, out float value, out float derivative)
        {
            int lobes = Mathf.Clamp(def.FeatureVerticalLobeCount, 1, 4);
            float scaled = Mathf.Clamp01(u) * lobes;
            int lobe = Mathf.Min(lobes - 1, Mathf.FloorToInt(scaled));
            float localU = lobe == lobes - 1 && u >= 1f ? 1f : scaled - lobe;
            float sign = def.FeatureVerticalStartsWithDip ? -1f : 1f;
            if (def.FeatureVerticalAlternates && (lobe & 1) != 0) sign = -sign;
            value = sign * CrestBump(localU);
            derivative = sign * CrestBumpDerivative(localU) * lobes;
        }

        // ─────────────────────────── Arcs (bobsled banked) ───────────────────────────

        /// <summary>
        /// Separates the frame's current physical roll from its unwrapped historical
        /// rotation counters. A vertical half-loop plus a 180-degree road roll is
        /// physically upright even though both counters read 180; using the road-roll
        /// counter alone reverses the local turn plane and swaps inside/outside walls.
        /// </summary>
        private static void PhysicalRollBasis(in TrackConnectionFrame entry, out Vector3 forward,
            out Vector3 baseRight, out Vector3 baseUp, out float physicalRollDegrees)
        {
            forward = entry.Forward.sqrMagnitude > 1e-8f ? entry.Forward.normalized : Vector3.forward;

            baseUp = Vector3.up - Vector3.Dot(Vector3.up, forward) * forward;
            if (baseUp.sqrMagnitude < 1e-6f)
                baseUp = entry.Up - Vector3.Dot(entry.Up, forward) * forward;
            if (baseUp.sqrMagnitude < 1e-6f)
                baseUp = Vector3.Cross(forward,
                    Mathf.Abs(forward.x) < 0.9f ? Vector3.right : Vector3.forward);
            baseUp.Normalize();
            baseRight = Vector3.Cross(baseUp, forward).normalized;

            Vector3 physicalRight = entry.Right - Vector3.Dot(entry.Right, forward) * forward;
            if (physicalRight.sqrMagnitude < 1e-6f) physicalRight = baseRight;
            else physicalRight.Normalize();
            physicalRollDegrees = Vector3.SignedAngle(baseRight, physicalRight, forward);
        }

        /// <summary>
        /// Horizontal arc with EASED (clothoid-style) curvature as PURE geometry: level
        /// floor, no per-section banking (the global banking field supplies wall support
        /// and tilt afterwards). Curvature ramps 0 → 1/R → 0, so the lateral
        /// acceleration at both connections is continuous — no entry/exit bump — and the
        /// ring facet density blends in from zero instead of starting abruptly at the
        /// joint. The same cached profile drives the planner's 2D model, so plan and
        /// build agree to float precision.
        /// </summary>
        public static TrackConnectionFrame[] BuildArc(TrackConnectionFrame entry, float signedAngleDeg, float radius,
            float bankDeg, in FrameBuildContext ctx)
            => BuildArcInternal(entry, signedAngleDeg, radius, bankDeg, 0f, ctx);

        /// <summary>
        /// Eased turn with a definition-owned 0→crest→0 elevation profile. Both end
        /// slopes and vertical curvature return to zero, preserving the corresponding
        /// level turn's exact heading and elevation boundary.
        /// </summary>
        public static TrackConnectionFrame[] BuildElevatedArc(
            TrackConnectionFrame entry,
            float signedAngleDeg,
            float radius,
            float bankDeg,
            float crestHeight,
            in FrameBuildContext ctx)
            => BuildArcInternal(entry, signedAngleDeg, radius, bankDeg, crestHeight, ctx);

        private static TrackConnectionFrame[] BuildArcInternal(
            TrackConnectionFrame entry,
            float signedAngleDeg,
            float radius,
            float bankDeg,
            float crestHeight,
            in FrameBuildContext ctx)
        {
            float side = Mathf.Sign(signedAngleDeg);
            float angleAbs = QuantizeArcAngle(Mathf.Abs(signedAngleDeg));
            var profile = GetArcProfile(angleAbs);
            float horizontalArcLen = EasedArcLength(angleAbs, radius);
            float drivingArcLen = ElevatedEasedArcLength(angleAbs, radius, crestHeight);

            // Peak yaw rate matches the circular arc (peak κ = 1/R), so the facet bound
            // uses the eased-equivalent angle budget, like the loop builder.
            int facetRings = Mathf.CeilToInt(angleAbs / ((1f - ArcCurvatureEaseFraction) * ctx.MaxFacetAngle)) + 1;
            int rings = Mathf.Clamp(Mathf.Max(
                Mathf.CeilToInt(drivingArcLen / ctx.MetersPerRing) + 1,
                facetRings), 9, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];
            var heights = new float[rings];
            var slopes = new float[rings];
            var distances = new float[rings];

            float horizontalStep = horizontalArcLen / Mathf.Max(1, rings - 1);
            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                heights[i] = crestHeight * CrestBump(u);
                slopes[i] = crestHeight * CrestBumpDerivative(u) /
                            Mathf.Max(horizontalArcLen, 0.01f);
                if (i > 0)
                {
                    float dh = heights[i] - heights[i - 1];
                    distances[i] = distances[i - 1] +
                                   Mathf.Sqrt(horizontalStep * horizontalStep + dh * dh);
                }
            }

            // The definition compiler and builder share the fixed-resolution length
            // above. Normalize the ring-specific cumulative approximation so the
            // authored section budget and its final frame remain exactly identical.
            float distanceScale = drivingArcLen /
                                  Mathf.Max(0.001f, distances[rings - 1]);
            for (int i = 1; i < rings; i++) distances[i] *= distanceScale;

            PhysicalRollBasis(entry, out Vector3 baseForward, out Vector3 baseRight,
                out Vector3 baseUp, out float physicalRollDegrees);
            Vector3 basePos = entry.Position;

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float thetaDeg = side * SampleTable(profile.theta, u) * Mathf.Rad2Deg;
                Vector2 pl = SampleTable(profile.plane, u) * horizontalArcLen;
                float height = heights[i];
                float verticalSlope = slopes[i];
                float curvature;
                float verticalCurvature;
                if (i == 0 || i == rings - 1)
                {
                    curvature = 0f;
                    verticalCurvature = 0f;
                }
                else
                {
                    float du = 1f / (rings - 1);
                    float thetaBefore = SampleTable(profile.theta, u - du);
                    float thetaAfter = SampleTable(profile.theta, u + du);
                    float slopeBefore = slopes[i - 1];
                    float slopeAfter = slopes[i + 1];
                    float localDistance = distances[i + 1] - distances[i - 1];
                    curvature = side * (thetaAfter - thetaBefore) /
                                Mathf.Max(0.001f, localDistance);
                    verticalCurvature = (Mathf.Atan(slopeAfter) - Mathf.Atan(slopeBefore)) /
                                        Mathf.Max(0.001f, localDistance);
                }

                Quaternion yaw = Quaternion.AngleAxis(thetaDeg, baseUp);
                Vector3 horizontalForward = (yaw * baseForward).normalized;
                Vector3 forward = (horizontalForward + baseUp * verticalSlope).normalized;
                Vector3 unrolledRight = (yaw * baseRight).normalized;
                Quaternion explicitRoll = Quaternion.AngleAxis(physicalRollDegrees, forward);
                Vector3 right = (explicitRoll * unrolledRight).normalized;
                Vector3 up = Vector3.Cross(forward, right).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = basePos + baseForward * pl.x + baseRight * (side * pl.y) +
                               baseUp * height,
                    Forward = forward,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = entry.BankAngle,
                    PitchAngle = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg,
                    AccumulatedRoadRoll = entry.AccumulatedRoadRoll,
                    AccumulatedVerticalRotation = entry.AccumulatedVerticalRotation,
                    HorizontalCurvature = curvature,
                    HorizontalCurvatureRate = i > 0
                        ? (curvature - frames[i - 1].HorizontalCurvature) /
                          Mathf.Max(0.001f, distances[i] - distances[i - 1])
                        : 0f,
                    VerticalCurvature = verticalCurvature,
                    VerticalCurvatureRate = i > 0
                        ? (verticalCurvature - frames[i - 1].VerticalCurvature) /
                          Mathf.Max(0.001f, distances[i] - distances[i - 1])
                        : 0f,
                    RoadRollRate = entry.RoadRollRate,
                    ArcLength = entry.ArcLength + distances[i]
                };
            }

            // Exact eased end pose (identical to the planner's 2D model).
            Vector2 endPl = profile.plane[profile.plane.Length - 1] * horizontalArcLen;
            Quaternion endYaw = Quaternion.AngleAxis(side * angleAbs, baseUp);
            var last = frames[rings - 1];
            last.Position = basePos + baseForward * endPl.x + baseRight * (side * endPl.y);
            last.Forward = (endYaw * baseForward).normalized;
            Vector3 endBaseRight = (endYaw * baseRight).normalized;
            last.Right = (Quaternion.AngleAxis(physicalRollDegrees, last.Forward) * endBaseRight).normalized;
            last.Up = Vector3.Cross(last.Forward, last.Right).normalized;
            last.PitchAngle = Mathf.Asin(Mathf.Clamp(last.Forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            last.HorizontalCurvature = 0f;
            last.HorizontalCurvatureRate = 0f;
            last.VerticalCurvature = 0f;
            last.VerticalCurvatureRate = 0f;
            frames[rings - 1] = last;

            frames[0] = entry;

            return frames;
        }

        /// <summary>Composed consecutive arcs (S-curves, chicanes, double apex, …) as one frame set.</summary>
        public static TrackConnectionFrame[] BuildComposedArcs(TrackConnectionFrame entry, float[] signedAngles,
            float[] radii, float bankDeg, in FrameBuildContext ctx)
        {
            var all = new System.Collections.Generic.List<TrackConnectionFrame>();
            TrackConnectionFrame current = entry;

            for (int a = 0; a < signedAngles.Length; a++)
            {
                TrackConnectionFrame[] arc = BuildArc(current, signedAngles[a], radii[a], bankDeg, ctx);
                int startIdx = a == 0 ? 0 : 1;
                for (int i = startIdx; i < arc.Length; i++) all.Add(arc[i]);
                current = arc[arc.Length - 1];
            }

            return all.ToArray();
        }

        // ─────────────────────────── Ballistic jump ramps ───────────────────────────

        /// <summary>
        /// Launch ramp: pitch eases 0 → LaunchPitch over the ramp length. The exit frame
        /// keeps the launch pitch — the craft leaves the lip on the ballistic trajectory.
        /// </summary>
        public static TrackConnectionFrame[] BuildLaunchRamp(TrackConnectionFrame entry, float length,
            float launchPitchDeg, in FrameBuildContext ctx)
        {
            return BuildPitchRamp(entry, length, 0f, launchPitchDeg, ctx);
        }

        /// <summary>
        /// Landing ramp: starts at the predicted ballistic arrival pitch (descending) and
        /// eases to level. The entry frame is the landing mouth — an OPEN boundary.
        /// </summary>
        public static TrackConnectionFrame[] BuildLandingRamp(TrackConnectionFrame entry, float length,
            float arrivalPitchDeg, in FrameBuildContext ctx)
        {
            return BuildPitchRamp(entry, length, arrivalPitchDeg, 0f, ctx);
        }

        /// <summary>Piecewise-smoothstep pitch value over normalized position for a keyframed ramp.</summary>
        public static float KeyframedPitchAt(float u, (float u, float pitch)[] keys)
        {
            if (u <= keys[0].u) return keys[0].pitch;
            for (int i = 1; i < keys.Length; i++)
            {
                if (u <= keys[i].u)
                {
                    float t = Mathf.InverseLerp(keys[i - 1].u, keys[i].u, u);
                    return Mathf.Lerp(keys[i - 1].pitch, keys[i].pitch, Smooth01(t));
                }
            }
            return keys[keys.Length - 1].pitch;
        }

        /// <summary>Plan-time horizontal/vertical span of a keyframed pitch ramp (numeric, 256 steps).</summary>
        public static void KeyframedPitchSpan((float u, float pitch)[] keys, float length,
            out float horizontal, out float vertical)
        {
            const int steps = 256;
            horizontal = 0f; vertical = 0f;
            for (int i = 0; i < steps; i++)
            {
                float u = (i + 0.5f) / steps;
                float pitch = KeyframedPitchAt(u, keys) * Mathf.Deg2Rad;
                horizontal += Mathf.Cos(pitch) * (length / steps);
                vertical += Mathf.Sin(pitch) * (length / steps);
            }
        }

        /// <summary>
        /// Keyframed pitch ramp (jump launch: level → climb → shallow launch pitch;
        /// landing: shallow arrival → descent → level). Position integrates the tangent.
        /// </summary>
        public static TrackConnectionFrame[] BuildKeyframedPitchRamp(TrackConnectionFrame entry, float length,
            (float u, float pitch)[] keys, in FrameBuildContext ctx)
        {
            float maxPitch = 0f;
            foreach (var k in keys) maxPitch = Mathf.Max(maxPitch, Mathf.Abs(k.pitch));

            int facetRings = Mathf.CeilToInt(maxPitch * 3f / ctx.MaxFacetAngle) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(length / ctx.MetersPerRing) + 1, facetRings), 8, ctx.MaxRingsPerSection); // base density: ramp connections keep the track's ring rhythm
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);

            Vector3 pos = entry.Position;
            float ds = length / (rings - 1);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float pitch = KeyframedPitchAt(u, keys);
                float rad = pitch * Mathf.Deg2Rad;

                Vector3 fwd = (fwdH * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)).normalized;
                Vector3 up = Vector3.Cross(fwd, rightH).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = rightH,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = pitch,
                    ArcLength = entry.ArcLength + length * u
                };

                if (i < rings - 1)
                {
                    float uMid = (i + 0.5f) / (rings - 1);
                    float pitchMid = KeyframedPitchAt(uMid, keys) * Mathf.Deg2Rad;
                    pos += (fwdH * Mathf.Cos(pitchMid) + Vector3.up * Mathf.Sin(pitchMid)) * ds;
                }
            }

            return frames;
        }

        /// <summary>
        /// Keyframes for a jump launch ramp whose pitch never decreases before the lip.
        /// The approach stays low, then loads progressively harder near the open edge:
        /// this is a launch-ramp silhouette, rather than a long shallow hill.
        /// </summary>
        public static (float u, float pitch)[] LaunchRampKeys(float intermediatePitchDeg, float launchPitchDeg)
        {
            float lip = Mathf.Max(0f, launchPitchDeg);
            float middle = Mathf.Clamp(intermediatePitchDeg, 0f, lip);
            // Keep the first third level, delay most of the rotation until the final
            // third, and hold the terminal tangent briefly. Piecewise smoothstep gives
            // every join zero angular rate, including the open edge, so the pronounced
            // lip remains C1-smooth and cannot become a one-ring kicker.
            return new[] { (0f, 0f), (0.32f, 0f), (0.68f, middle), (0.9f, lip), (1f, lip) };
        }

        /// <summary>Keyframes for a jump landing ramp: shallow arrival pitch → descent pitch → level.</summary>
        public static (float u, float pitch)[] LandingRampKeys(float arrivalPitchDeg, float descentPitchDeg)
            => new[] { (0f, arrivalPitchDeg), (0.4f, -Mathf.Abs(descentPitchDeg)), (1f, 0f) };

        /// <summary>
        /// Generic pitch ramp: pitch eases start → end (smoothstep) while the position
        /// integrates the tangent. Ring count honors the facet-angle bound on the pitch change.
        /// </summary>
        public static TrackConnectionFrame[] BuildPitchRamp(TrackConnectionFrame entry, float length,
            float startPitchDeg, float endPitchDeg, in FrameBuildContext ctx)
        {
            float pitchSpan = Mathf.Abs(endPitchDeg - startPitchDeg);
            int facetRings = Mathf.CeilToInt(pitchSpan * 1.5f / ctx.MaxFacetAngle) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(length / ctx.MetersPerRing) + 1, facetRings), 8, ctx.MaxRingsPerSection); // base density: ramp connections keep the track's ring rhythm
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);

            Vector3 pos = entry.Position;
            float ds = length / (rings - 1);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float pitch = Mathf.Lerp(startPitchDeg, endPitchDeg, Smooth01(u));
                float rad = pitch * Mathf.Deg2Rad;

                Vector3 fwd = (fwdH * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)).normalized;
                Vector3 up = Vector3.Cross(fwd, rightH).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = rightH,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = pitch,
                    ArcLength = entry.ArcLength + length * u
                };

                if (i < rings - 1)
                {
                    // Midpoint integration keeps the numeric path within centimeters of
                    // the plan-time PitchRampSpan estimate over feature-scale lengths.
                    float uMid = (i + 0.5f) / (rings - 1);
                    float pitchMid = Mathf.Lerp(startPitchDeg, endPitchDeg, Smooth01(uMid)) * Mathf.Deg2Rad;
                    pos += (fwdH * Mathf.Cos(pitchMid) + Vector3.up * Mathf.Sin(pitchMid)) * ds;
                }
            }

            return frames;
        }

        // ─────────────────────────── Full pipe ───────────────────────────

        /// <summary>
        /// Straight full-pipe section: the cross-section closes gradually from the
        /// open half-pipe into a complete tube (PipeClosure 0 → 1 over the closure
        /// span), holds, and reopens before the exit — never over one or two samples.
        /// The centerline is a level straight; the craft may roll around the interior
        /// circumference. Width carries the pipe diameter (radius = width/2); the
        /// entry/exit rings stay at ROAD width so the weld contract holds, with the
        /// width blending alongside the closure.
        /// </summary>
        public static TrackConnectionFrame[] BuildFullPipe(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            float length = def.Length;
            float closeEnd = def.PipeCloseFraction > 0.001f ? Mathf.Clamp(def.PipeCloseFraction, 0.05f, 0.45f) : 0.3f;
            float openStart = def.PipeOpenFraction > 0.001f ? Mathf.Clamp(def.PipeOpenFraction, 0.55f, 0.95f) : 0.7f;

            int rings = Mathf.Clamp(Mathf.CeilToInt(length / ctx.FeatureMetersPerRing) + 1, 24, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);

                float closure = u < closeEnd
                    ? Smooth01(u / closeEnd)
                    : u > openStart
                        ? Smooth01((1f - u) / Mathf.Max(0.0001f, 1f - openStart))
                        : 1f;

                frames[i] = new TrackConnectionFrame
                {
                    Position = entry.Position + fwdH * (length * u),
                    Forward = fwdH,
                    Right = rightH,
                    Up = Vector3.up,
                    Width = Mathf.Lerp(entry.Width, def.Width, Smooth01(Mathf.Clamp01(closure * 1.5f))),
                    BankAngle = 0f,
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + length * u,
                    PipeClosure = closure
                };
            }

            // Exact weld exit: road width, open profile.
            var last = frames[rings - 1];
            last.Width = entry.Width;
            last.PipeClosure = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Wallride turn ───────────────────────────

        /// <summary>
        /// Intentional wallride corner: the same eased horizontal arc as a banked
        /// curve, but the section OWNS its cross-section — full turn rounding (no flat
        /// center), the outside wall boosted to primary-surface height with a full
        /// catch-curl past vertical, and the inside wall trimmed for visibility. All
        /// channels follow one smooth hold envelope, so entry and exit migrate the
        /// racing line on and off the wall gradually.
        /// </summary>
        /// <summary>
        /// Shortest wallride climb-on / climb-off ramp, as a fraction of the section.
        /// </summary>
        public const float MinWallrideEaseFraction = 0.15f;

        /// <summary>
        /// Longest wallride climb-on / climb-off ramp, as a fraction of the section.
        /// Spent at BOTH ends, so full wall engagement lasts (1 − 2×) of the section:
        /// 0.40 leaves 20%, 0.45 leaves 10%. Raise only if smoother welds are worth
        /// more than time on the wall — lengthening the wallride itself is better.
        /// </summary>
        public const float MaxWallrideEaseFraction = 0.4f;

        public static TrackConnectionFrame[] BuildWallrideTurn(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            var frames = BuildArc(entry, def.TurnAngle * def.TurnSign, def.Radius, def.BankingAngle, ctx);

            // Envelope ease spans: entry/exit fractions from the arc length vs the
            // roll transition (the craft needs real distance to climb the wall).
            //
            // This fraction is spent TWICE (once climbing on, once coming off), so
            // the wallride only holds full engagement over (1 − 2·easeFrac) of its
            // length. At the 0.40 cap that leaves just 20% of the section actually
            // riding the wall. Raising the cap buys smoother welds but spends the
            // part of the feature that is worth having — prefer lengthening the
            // section over raising this.
            float arcLen = frames[frames.Length - 1].ArcLength - frames[0].ArcLength;
            float easeFrac = Mathf.Clamp(ctx.BankTransitionLength / Mathf.Max(1f, arcLen),
                MinWallrideEaseFraction, MaxWallrideEaseFraction);

            bool rightTurn = def.TurnSign >= 0;
            for (int i = 0; i < frames.Length; i++)
            {
                float u = (float)i / (frames.Length - 1);
                float e = HoldProfile(u, easeFrac, TrackBlendCurve.SmootherStep);

                var f = frames[i];
                f.TurnRounding = e;
                // Outside wall becomes THE surface: boosted high (multiplier up to 3)
                // with a full catch-curl. The WallrideMorph then folds the whole
                // cross-section onto that wall — no flat center, no inside wall: a
                // slow craft drops off the wall's lower edge into the turn interior.
                float outsideSupp = -2f * e;   // multiplier 1 → 3
                float insideSupp = 1f * e;     // inside wall fully open at engagement
                if (rightTurn) // right turn rides the LEFT wall
                {
                    f.LeftWallSuppression = outsideSupp;
                    f.RightWallSuppression = insideSupp;
                    f.LeftOverhang = e;
                }
                else
                {
                    f.RightWallSuppression = outsideSupp;
                    f.LeftWallSuppression = insideSupp;
                    f.RightOverhang = e;
                }
                f.WallrideMorph = e;
                f.BankAngle = def.TurnSign * def.BankingAngle * e;
                frames[i] = f;
            }

            // Exact weld boundaries: neutral cross-section at both ends.
            var first = frames[0];
            first.TurnRounding = 0f; first.LeftOverhang = 0f; first.RightOverhang = 0f;
            first.LeftWallSuppression = 0f; first.RightWallSuppression = 0f; first.BankAngle = 0f;
            first.WallrideMorph = 0f;
            frames[0] = first;
            var last = frames[frames.Length - 1];
            last.TurnRounding = 0f; last.LeftOverhang = 0f; last.RightOverhang = 0f;
            last.LeftWallSuppression = 0f; last.RightWallSuppression = 0f; last.BankAngle = 0f;
            last.WallrideMorph = 0f;
            frames[frames.Length - 1] = last;

            return frames;
        }

        // ───────────────────── Continuous rotational road event ─────────────────────

        public static float RotationalBlendFraction(RotationalBlendPreset preset) => preset switch
        {
            RotationalBlendPreset.Short => 0.10f,
            RotationalBlendPreset.Medium => 0.20f,
            RotationalBlendPreset.Long => 0.30f,
            _ => 0f
        };

        /// <summary>Physical event length after approved adjacent phase overlaps.</summary>
        public static float RotationalEventLength(TrackMacroSectionDefinition def)
        {
            var phases = def.RotationalPhases;
            if (phases == null || phases.Count == 0) return Mathf.Max(1f, def.Length);

            float end = 0f;
            for (int i = 0; i < phases.Count; i++)
            {
                float length = phases[i]?.Length ?? 1f;
                if (i == 0) end = length;
                else
                {
                    var previous = phases[i - 1];
                    float overlap = RotationalBlendFraction(previous?.BlendToNext ?? RotationalBlendPreset.None) *
                                    Mathf.Min(previous?.Length ?? 1f, length);
                    end += length - overlap;
                }
            }
            return Mathf.Max(1f, end);
        }

        /// <summary>
        /// Cubic Hermite correction whose value is zero at both ends but whose distance
        /// derivative matches the requested entry/exit angular rates. It lets an event
        /// inherit and propagate curvature without changing any quantized phase total.
        /// </summary>
        private static float EndpointRateCorrection(float distance, float length,
            float entryDegreesPerMeter, float exitDegreesPerMeter)
        {
            float u = Mathf.Clamp01(distance / Mathf.Max(0.001f, length));
            float u2 = u * u;
            float u3 = u2 * u;
            float h10 = u3 - 2f * u2 + u;
            float h11 = u3 - u2;
            return length * (entryDegreesPerMeter * h10 + exitDegreesPerMeter * h11);
        }

        /// <summary>
        /// Integrates independent horizontal curvature, vertical curvature and explicit
        /// road roll into one propagated frame stream. No world-up reconstruction, entry
        /// flattening, exit snapping or rotation normalization occurs.
        /// </summary>
        public static TrackConnectionFrame[] BuildRotationalEvent(TrackConnectionFrame entry,
            TrackMacroSectionDefinition def, in FrameBuildContext ctx)
        {
            var phases = def.RotationalPhases;
            if (phases == null || phases.Count == 0)
                return BuildStraight(entry, def, ctx);

            int count = phases.Count;
            var starts = new float[count];
            var ends = new float[count];
            var cdf = new float[count][];
            var cdfSlope = new float[count][];
            const int profileSamples = 256;

            float cursor = 0f;
            int totalUnits = 0;
            float totalForwardDegrees = 0f;
            float totalRollDegrees = 0f;
            for (int p = 0; p < count; p++)
            {
                var phase = phases[p] ?? new RotationalPhaseDefinition();
                float length = phase.Length;
                if (p > 0)
                {
                    var previous = phases[p - 1];
                    cursor -= RotationalBlendFraction(previous?.BlendToNext ?? RotationalBlendPreset.None) *
                              Mathf.Min(previous?.Length ?? 1f, length);
                }
                starts[p] = cursor;
                ends[p] = cursor + length;
                cursor = ends[p];

                totalUnits += Mathf.Max(1, phase.RotationUnits);
                float degrees = Mathf.Abs(phase.Degrees(ctx.RotationUnitDegrees));
                if (phase.Axis == RotationalPhaseAxis.VerticalCenterline) totalForwardDegrees += degrees;
                else totalRollDegrees += degrees;
                totalForwardDegrees += Mathf.Abs(phase.HorizontalTurnDegrees) + Mathf.Abs(phase.VerticalDriftDegrees);
                // Each half bias rises from zero and returns to zero, so its forward-
                // direction demand is twice the selected peak angle.
                totalForwardDegrees += 2f * (Mathf.Abs(phase.FirstHalfYawBiasDegrees) +
                                             Mathf.Abs(phase.SecondHalfYawBiasDegrees) +
                                             Mathf.Abs(phase.FirstHalfPitchBiasDegrees) +
                                             Mathf.Abs(phase.SecondHalfPitchBiasDegrees));
                // A corkscrew orbit rotates a temporary tangent vector once per roll
                // unit. Budget its changing direction as centerline geometry, not roll.
                totalForwardDegrees += 2f * Mathf.Abs(phase.CenterlineOrbitDegrees) *
                                       Mathf.Max(1, phase.RotationUnits);

                // A smooth positive rate envelope, weighted by the two-half radius model.
                // Its integral is normalized, so shaping can never alter the exact target.
                var table = new float[profileSamples + 1];
                float firstLen = Mathf.Max(1f, phase.FirstHalfLength);
                float midpoint = firstLen / Mathf.Max(2f, phase.Length);
                float previousWeight = 0f;
                float integral = 0f;
                for (int k = 0; k <= profileSamples; k++)
                {
                    float x = (float)k / profileSamples;
                    float transition = Smooth01(Mathf.InverseLerp(
                        Mathf.Max(0f, midpoint - 0.20f), Mathf.Min(1f, midpoint + 0.20f), x));
                    float radius = Mathf.Lerp(Mathf.Max(1f, phase.FirstHalfRadius),
                                              Mathf.Max(1f, phase.SecondHalfRadius), transition);
                    float envelope = Mathf.Sin(Mathf.PI * x);
                    float weight = envelope * envelope / radius;
                    if (k > 0) integral += (previousWeight + weight) * 0.5f / profileSamples;
                    table[k] = integral;
                    previousWeight = weight;
                }
                float inv = integral > 1e-8f ? 1f / integral : 1f;
                for (int k = 0; k <= profileSamples; k++) table[k] *= inv;
                table[profileSamples] = 1f;
                cdf[p] = table;
                cdfSlope[p] = BuildMonotoneSlopes(table, profileSamples);
            }

            float lengthTotal = Mathf.Max(1f, cursor);
            RotationalPhaseDefinition finalPhase = null;
            for (int p = count - 1; p >= 0 && finalPhase == null; p--)
                finalPhase = phases[p];
            float exitHorizontalCurvature = finalPhase?.ExitHorizontalCurvature ?? 0f;
            float exitVerticalCurvature = finalPhase?.ExitVerticalCurvature ?? 0f;
            float exitRoadRollRate = finalPhase?.ExitRoadRollRate ?? 0f;
            // Endpoint-rate Hermite corrections have zero net angle, but their local
            // excursion still needs tessellation budget.
            totalForwardDegrees += lengthTotal * Mathf.Rad2Deg *
                (Mathf.Abs(entry.HorizontalCurvature) + Mathf.Abs(exitHorizontalCurvature) +
                 Mathf.Abs(entry.VerticalCurvature) + Mathf.Abs(exitVerticalCurvature));
            totalRollDegrees += lengthTotal *
                (Mathf.Abs(entry.RoadRollRate) + Mathf.Abs(exitRoadRollRate));
            int distanceIntervals = Mathf.CeilToInt(lengthTotal / ctx.MaxRotationalSampleDistance);
            // The normalized sin²/radius phase envelope can peak above its average;
            // fourfold headroom keeps the actual adjacent angular delta under the
            // configured limit even with the largest approved two-half radius contrast.
            int forwardIntervals = Mathf.CeilToInt(totalForwardDegrees * 4f / ctx.MaxRotationalForwardAngle);
            int rollIntervals = Mathf.CeilToInt(totalRollDegrees * 4f / ctx.MaxRotationalRollAngle);
            int unitIntervals = totalUnits * ctx.MinSamplesPerRotationUnit;
            int intervals = Mathf.Clamp(Mathf.Max(Mathf.Max(distanceIntervals, forwardIntervals),
                Mathf.Max(rollIntervals, unitIntervals)), 8, Mathf.Max(8, ctx.MaxRingsPerSection - 1));
            var frames = new TrackConnectionFrame[intervals + 1];
            frames[0] = entry;

            float SampleProgress(int p, float distance)
            {
                float x = Mathf.InverseLerp(starts[p], ends[p], distance);
                if (x <= 0f) return 0f;
                if (x >= 1f) return 1f;
                float tableX = x * profileSamples;
                int lo = Mathf.Min((int)tableX, profileSamples - 1);
                float t = tableX - lo;

                // MONOTONE CUBIC, not Lerp.
                //
                // This table is a progress CDF, and its DERIVATIVE is the roll rate —
                // which drives the centerline's orbit and therefore its curvature.
                // Linear interpolation makes progress piecewise-linear, so that
                // derivative is piecewise-CONSTANT and steps at every node. On a
                // 2677 m corkscrew with 256 intervals that is a curvature jolt every
                // 10.5 m; at 361 m/s the craft feels it as a ~35 Hz buzz through an
                // otherwise perfectly smooth path. Cubic Hermite makes the rate
                // continuous, so the road reads as smooth as its centerline is.
                //
                // The slopes are monotonicity-limited (Fritsch–Carlson), so progress
                // can never overshoot or run backwards — a reversal here would roll
                // the road the wrong way mid-feature.
                float h = 1f / profileSamples;
                float y0 = cdf[p][lo], y1 = cdf[p][lo + 1];
                float m0 = cdfSlope[p][lo], m1 = cdfSlope[p][lo + 1];

                float t2 = t * t;
                float t3 = t2 * t;
                return (2f * t3 - 3f * t2 + 1f) * y0
                     + (t3 - 2f * t2 + t) * h * m0
                     + (-2f * t3 + 3f * t2) * y1
                     + (t3 - t2) * h * m1;
            }

            float accumulatedRoll = entry.AccumulatedRoadRoll;
            float accumulatedVertical = entry.AccumulatedVerticalRotation;

            PhysicalRollBasis(entry, out Vector3 forward, out Vector3 baseRight,
                out Vector3 baseUp, out float entryPhysicalRollDegrees);
            Vector3 entryBaseForward = forward;
            Vector3 entryBaseRight = baseRight;
            Vector3 entryBaseUp = baseUp;
            Vector3 position = entry.Position;

            float previousHorizontal = 0f;
            float previousVerticalMotion = 0f;
            float previousVerticalIntent = 0f;
            float previousRoll = accumulatedRoll;
            float previousHorizontalCurvature = entry.HorizontalCurvature;
            float previousVerticalCurvature = entry.VerticalCurvature;
            float previousRollRate = entry.RoadRollRate;
            float previousWidth = entry.Width;

            for (int i = 1; i <= intervals; i++)
            {
                float distance = lengthTotal * i / intervals;
                float horizontal = 0f;
                float verticalMotion = 0f;
                float verticalIntent = entry.AccumulatedVerticalRotation;
                float roll = entry.AccumulatedRoadRoll;
                float width = entry.Width;
                float widthTarget = entry.Width;

                for (int p = 0; p < count; p++)
                {
                    var phase = phases[p] ?? new RotationalPhaseDefinition();
                    float progress = SampleProgress(p, distance);
                    horizontal += phase.HorizontalTurnDegrees * progress;
                    verticalMotion += phase.VerticalDriftDegrees * progress;
                    float local = Mathf.InverseLerp(starts[p], ends[p], distance);
                    float midpoint = Mathf.Max(0.001f, phase.FirstHalfLength) /
                                     Mathf.Max(0.002f, phase.Length);
                    float halfLocal;
                    float yawBias;
                    float pitchBias;
                    if (local <= midpoint)
                    {
                        halfLocal = Mathf.Clamp01(local / midpoint);
                        yawBias = phase.FirstHalfYawBiasDegrees;
                        pitchBias = phase.FirstHalfPitchBiasDegrees;
                    }
                    else
                    {
                        halfLocal = Mathf.Clamp01((local - midpoint) / Mathf.Max(0.001f, 1f - midpoint));
                        yawBias = phase.SecondHalfYawBiasDegrees;
                        pitchBias = phase.SecondHalfPitchBiasDegrees;
                    }
                    float biasEnvelope = Mathf.Sin(Mathf.PI * halfLocal);
                    biasEnvelope *= biasEnvelope;
                    float yawEnvelope = biasEnvelope;
                    if (phase.Axis == RotationalPhaseAxis.RoadRoll &&
                        phase.CenterlineOrbitDegrees > 0.001f)
                    {
                        // Corkscrew entry/exit steering belongs in upright shoulders,
                        // not at the 90/270-degree roll stations where an unrelated
                        // horizontal bend would pull away from the supporting surface.
                        const float shoulder = 0.18f;
                        if (local < shoulder)
                        {
                            float shoulderLocal = Mathf.Clamp01(local / shoulder);
                            yawEnvelope = Mathf.Sin(Mathf.PI * shoulderLocal);
                            yawEnvelope *= yawEnvelope;
                            yawBias = phase.FirstHalfYawBiasDegrees;
                        }
                        else if (local > 1f - shoulder)
                        {
                            float shoulderLocal = Mathf.Clamp01((local - (1f - shoulder)) / shoulder);
                            yawEnvelope = Mathf.Sin(Mathf.PI * shoulderLocal);
                            yawEnvelope *= yawEnvelope;
                            yawBias = phase.SecondHalfYawBiasDegrees;
                        }
                        else
                        {
                            yawEnvelope = 0f;
                            yawBias = 0f;
                        }
                    }
                    horizontal += yawBias * yawEnvelope;
                    verticalMotion += pitchBias * biasEnvelope;
                    float intentional = phase.Degrees(ctx.RotationUnitDegrees) * progress;
                    if (phase.Axis == RotationalPhaseAxis.VerticalCenterline)
                    {
                        verticalMotion += intentional;
                        verticalIntent += intentional;
                    }
                    else
                    {
                        roll += intentional;

                        // A real corkscrew's centerline tangent orbits in lockstep with
                        // its road roll. Its curvature therefore points along the rolled
                        // road normal (the surface-support direction), while the sinÂ²
                        // envelope gives straight, level welds at both ends.
                        float orbitDegrees = Mathf.Abs(phase.CenterlineOrbitDegrees);
                        if (orbitDegrees > 0.001f && local > 0f && local < 1f)
                        {
                            float envelope = Mathf.Sin(Mathf.PI * local);
                            envelope *= envelope;
                            float rollRadians = intentional * Mathf.Deg2Rad;
                            float rollSign = (float)phase.Direction;
                            horizontal += rollSign * orbitDegrees * Mathf.Cos(rollRadians) * envelope;
                            verticalMotion += rollSign * orbitDegrees * Mathf.Sin(rollRadians) * envelope;
                        }
                    }

                    float nextWidth = phase.ExitWidth > 0.01f ? phase.ExitWidth : widthTarget;
                    width += (nextWidth - widthTarget) * progress;
                    widthTarget = nextWidth;
                }

                horizontal += EndpointRateCorrection(distance, lengthTotal,
                    entry.HorizontalCurvature * Mathf.Rad2Deg,
                    exitHorizontalCurvature * Mathf.Rad2Deg);
                verticalMotion += EndpointRateCorrection(distance, lengthTotal,
                    entry.VerticalCurvature * Mathf.Rad2Deg,
                    exitVerticalCurvature * Mathf.Rad2Deg);
                roll += EndpointRateCorrection(distance, lengthTotal,
                    entry.RoadRollRate, exitRoadRollRate);

                float yawDelta = horizontal - previousHorizontal;
                float pitchDelta = verticalMotion - previousVerticalMotion;
                Vector3 oldForward = forward;

                // Evaluate the two centerline channels as absolute entry-relative
                // states. This keeps them independent: completing 360/720/1080 degrees
                // of vertical rotation cannot geometrically cancel a requested plan-
                // view heading change (the incremental minimal-transport version could).
                // The axes come from the inherited frame's physical-roll decomposition,
                // never from its historical counters, so completed inversions cannot
                // silently flip the following section's turn plane.
                Quaternion yaw = Quaternion.AngleAxis(horizontal, entryBaseUp);
                Vector3 yawedForward = (yaw * entryBaseForward).normalized;
                Vector3 yawedRight = (yaw * entryBaseRight).normalized;
                Quaternion pitch = Quaternion.AngleAxis(-verticalMotion, yawedRight);
                forward = (pitch * yawedForward).normalized;
                baseRight = (pitch * yawedRight).normalized;
                baseRight = (baseRight - Vector3.Dot(baseRight, forward) * forward).normalized;
                baseUp = Vector3.Cross(forward, baseRight).normalized;

                float ds = lengthTotal / intervals;
                Vector3 travel = oldForward + forward;
                if (travel.sqrMagnitude < 1e-8f) travel = forward;
                position += travel.normalized * ds;

                float physicalRoll = entryPhysicalRollDegrees + (roll - entry.AccumulatedRoadRoll);
                Quaternion explicitRoll = Quaternion.AngleAxis(physicalRoll, forward);
                Vector3 right = (explicitRoll * baseRight).normalized;
                Vector3 up = Vector3.Cross(forward, right).normalized;
                right = Vector3.Cross(up, forward).normalized;

                float horizontalCurvature = yawDelta * Mathf.Deg2Rad / ds;
                float verticalCurvature = pitchDelta * Mathf.Deg2Rad / ds;
                float rollRate = (roll - previousRoll) / ds;

                frames[i] = new TrackConnectionFrame
                {
                    Position = position,
                    Forward = forward,
                    Right = right,
                    Up = up,
                    Width = Mathf.Max(1f, width),
                    BankAngle = entry.BankAngle,
                    PitchAngle = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg,
                    AccumulatedRoadRoll = roll,
                    AccumulatedVerticalRotation = verticalIntent,
                    HorizontalCurvature = horizontalCurvature,
                    HorizontalCurvatureRate = (horizontalCurvature - previousHorizontalCurvature) / ds,
                    VerticalCurvature = verticalCurvature,
                    VerticalCurvatureRate = (verticalCurvature - previousVerticalCurvature) / ds,
                    RoadRollRate = rollRate,
                    RoadRollAcceleration = (rollRate - previousRollRate) / ds,
                    ArcLength = entry.ArcLength + distance
                };

                previousHorizontal = horizontal;
                previousVerticalMotion = verticalMotion;
                previousVerticalIntent = verticalIntent;
                previousRoll = roll;
                previousHorizontalCurvature = horizontalCurvature;
                previousVerticalCurvature = verticalCurvature;
                previousRollRate = rollRate;
                previousWidth = width;
            }

            // The geometry approaches these derivatives through the Hermite correction.
            // Stamp the analytical endpoint state so the next section receives it without
            // finite-difference drift from the last sample interval.
            var exactExit = frames[frames.Length - 1];
            var beforeExit = frames[frames.Length - 2];
            float endpointDs = lengthTotal / intervals;
            exactExit.HorizontalCurvature = exitHorizontalCurvature;
            exactExit.VerticalCurvature = exitVerticalCurvature;
            exactExit.RoadRollRate = exitRoadRollRate;
            exactExit.HorizontalCurvatureRate =
                (exactExit.HorizontalCurvature - beforeExit.HorizontalCurvature) / endpointDs;
            exactExit.VerticalCurvatureRate =
                (exactExit.VerticalCurvature - beforeExit.VerticalCurvature) / endpointDs;
            exactExit.RoadRollAcceleration =
                (exactExit.RoadRollRate - beforeExit.RoadRollRate) / endpointDs;
            frames[frames.Length - 1] = exactExit;

            return frames;
        }

        /// <summary>
        /// Measures the closest non-neighbouring centerline samples in a rotational
        /// event using the same sampling contract as the built-layout validator.
        /// Planning uses this to reject a folded shape before it reaches mesh work.
        /// </summary>
        public static float MeasureRotationalEventClearance(TrackConnectionFrame[] frames, float width,
            out float firstArc, out float secondArc)
        {
            firstArc = 0f;
            secondArc = 0f;
            if (frames == null || frames.Length < 2) return float.PositiveInfinity;

            float clearanceStep = Mathf.Max(8f, width * 0.20f);
            float ignoreAlong = width * 2.5f;
            var samples = new List<int>();
            float nextSampleArc = frames[0].ArcLength;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i].ArcLength + 0.001f < nextSampleArc && i < frames.Length - 1) continue;
                samples.Add(i);
                nextSampleArc = frames[i].ArcLength + clearanceStep;
            }

            float minimumSq = float.PositiveInfinity;
            for (int a = 0; a < samples.Count; a++)
            for (int b = a + 1; b < samples.Count; b++)
            {
                int ia = samples[a];
                int ib = samples[b];
                if (frames[ib].ArcLength - frames[ia].ArcLength < ignoreAlong) continue;
                float distanceSq = (frames[ib].Position - frames[ia].Position).sqrMagnitude;
                if (distanceSq >= minimumSq) continue;
                minimumSq = distanceSq;
                firstArc = frames[ia].ArcLength - frames[0].ArcLength;
                secondArc = frames[ib].ArcLength - frames[0].ArcLength;
            }

            return float.IsPositiveInfinity(minimumSq) ? float.PositiveInfinity : Mathf.Sqrt(minimumSq);
        }

        /// <summary>Stamps the planner's local footprint from the same builder used at runtime.</summary>
        public static void StampRotationalEventPlan(TrackMacroSectionDefinition def,
            ResolvedTrackGenerationConfig cfg)
        {
            def.Length = RotationalEventLength(def);
            var frames = BuildRotationalEvent(TrackConnectionFrame.Origin(def.Width), def,
                FrameBuildContext.From(cfg));
            var last = frames[frames.Length - 1];
            def.PlanHorizontalLength = last.Position.z;
            def.PlanLateralOffset = last.Position.x;
            def.ElevationChange = last.Position.y;
            Vector3 flat = Flatten(last.Forward);
            def.TurnAngle = Vector3.SignedAngle(Vector3.forward, flat, Vector3.up);
        }

        // ─────────────────────────── Loop ───────────────────────────

        /// <summary>
        /// One full vertical loop with eased curvature and a smoothstepped lateral exit
        /// offset (the classic offset loop, never intersecting its own entry). Analytic
        /// tangents — a finite difference leaves a sub-degree weld tilt that reads as a
        /// physical bump at racing speed.
        /// </summary>
        public static TrackConnectionFrame[] BuildLoop(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            EnsureLoopProfile();

            float arcLen = def.Length;
            float lateral = LoopLateralOffset(def.Width);

            int facetRings = Mathf.CeilToInt(360f / (ctx.MaxFacetAngle * (1f - LoopCurvatureEaseFraction))) + 1;
            int lengthRings = Mathf.CeilToInt(arcLen / ctx.FeatureMetersPerRing) + 1;
            int rings = Mathf.Clamp(Mathf.Max(facetRings, lengthRings), 24, ctx.MaxRingsPerSection);

            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            Vector3 PosAt(float u)
            {
                Vector2 pl = TablePlaneAt(_loopPlaneTable, u) * arcLen;
                return basePos + fwdH * pl.x + Vector3.up * pl.y + rightH * (lateral * Smooth01(u));
            }

            Vector3 TanAt(float u)
            {
                float theta = TableThetaAt(_loopThetaTable, u);
                return fwdH * (arcLen * Mathf.Cos(theta))
                     + Vector3.up * (arcLen * Mathf.Sin(theta))
                     + rightH * (lateral * 6f * u * (1f - u));
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float thetaDeg = TableThetaAt(_loopThetaTable, u) * Mathf.Rad2Deg;

                Vector3 fwd = TanAt(u).normalized;
                Vector3 up = Quaternion.AngleAxis(-thetaDeg, rightH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = PosAt(u),
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = thetaDeg,
                    ArcLength = entry.ArcLength + arcLen * u
                };
            }

            // Weld contract: entry ring bit-identical, exit ring the exact flat exit pose.
            var first = frames[0];
            frames[0] = entry;
            frames[0].PitchAngle = first.PitchAngle;
            frames[0].ArcLength = first.ArcLength;

            var last = frames[rings - 1];
            last.Position = basePos + fwdH * LoopForwardDisplacement(arcLen) + rightH * lateral;
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Corkscrew ───────────────────────────

        /// <summary>
        /// Corkscrew roll progress with optional straight entry/exit shoulders. The
        /// active roll uses cosine velocity ramps and a constant-rate middle, keeping
        /// the derivative continuous. With the planner's combined 0.15 shoulder cap,
        /// peak normalized roll rate remains at or below the classic smoothstep's 1.5.
        /// </summary>
        public static float CorkscrewRollProgress(float u, float entryStraightFraction,
            float exitStraightFraction, out float derivative)
        {
            u = Mathf.Clamp01(u);
            float entryHold = Mathf.Clamp(entryStraightFraction, 0f, 0.08f);
            float exitHold = Mathf.Clamp(exitStraightFraction, 0f, 0.08f);
            float holdTotal = entryHold + exitHold;

            // Existing serialized definitions have zero shoulders and retain the
            // original profile exactly.
            if (holdTotal < 0.0001f)
            {
                derivative = 6f * u * (1f - u);
                return Smooth01(u);
            }

            float active = Mathf.Max(0.84f, 1f - holdTotal);
            if (u <= entryHold) { derivative = 0f; return 0f; }
            if (u >= 1f - exitHold) { derivative = 0f; return 1f; }

            float x = Mathf.Clamp01((u - entryHold) / active);
            const float ease = 0.2f;
            float normalizer = 1f - ease;
            float progress;
            float velocity;

            if (x < ease)
            {
                float phase = Mathf.PI * x / ease;
                velocity = 0.5f * (1f - Mathf.Cos(phase));
                progress = (0.5f * x - 0.5f * ease / Mathf.PI * Mathf.Sin(phase)) / normalizer;
            }
            else if (x > 1f - ease)
            {
                float mirror = 1f - x;
                float phase = Mathf.PI * mirror / ease;
                velocity = 0.5f * (1f - Mathf.Cos(phase));
                float mirrorProgress = (0.5f * mirror - 0.5f * ease / Mathf.PI * Mathf.Sin(phase)) / normalizer;
                progress = 1f - mirrorProgress;
            }
            else
            {
                velocity = 1f;
                progress = (x - 0.5f * ease) / normalizer;
            }

            derivative = velocity / (normalizer * active);
            return Mathf.Clamp01(progress);
        }

        /// <summary>
        /// One corkscrew: the road rolls smoothly around the travel axis on a helix
        /// centerline, entering and exiting upright, with analytic tangents.
        /// </summary>
        public static TrackConnectionFrame[] BuildCorkscrew(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            float L = def.Length;
            float firstRadius = Mathf.Max(1f, def.Radius);
            float secondRadius = def.SecondaryRadius > 0.001f
                ? Mathf.Max(1f, def.SecondaryRadius)
                : firstRadius;
            float elevation = def.ElevationChange;
            float rollTotal = def.RollChange; // signed

            int facetRings = Mathf.CeilToInt(Mathf.Abs(rollTotal) * 1.5f / ctx.MaxFacetAngle) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(L / ctx.FeatureMetersPerRing) + 1, facetRings), 24, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 basePos = entry.Position;

            Vector3 RadialAt(float u)
            {
                float roll = rollTotal * CorkscrewRollProgress(u,
                    def.FeatureEntryStraightFraction, def.FeatureExitStraightFraction, out _);
                return Quaternion.AngleAxis(roll, fwdH) * (-Vector3.up);
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float rollProgress = CorkscrewRollProgress(u,
                    def.FeatureEntryStraightFraction, def.FeatureExitStraightFraction, out float rollDerivative);
                float roll = rollTotal * rollProgress;
                Vector3 radial = RadialAt(u);

                float rollRateRad = rollTotal * rollDerivative * Mathf.Deg2Rad;
                float radiusBlend = Smooth01(rollProgress);
                float radius = Mathf.Lerp(firstRadius, secondRadius, radiusBlend);
                float smoothDerivative = 6f * rollProgress * (1f - rollProgress) * rollDerivative;
                float radiusDerivative = (secondRadius - firstRadius) * smoothDerivative;
                float elevationDerivative = elevation * smoothDerivative;
                Vector3 radialDerivative = Vector3.Cross(fwdH, radial) * rollRateRad;
                Vector3 fwd = (fwdH * L
                               + (Vector3.up + radial) * radiusDerivative
                               + radialDerivative * radius
                               + Vector3.up * elevationDerivative).normalized;

                Vector3 up = Quaternion.AngleAxis(roll, fwdH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = basePos + fwdH * (L * u)
                               + (Vector3.up + radial) * radius
                               + Vector3.up * (elevation * radiusBlend),
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = Mathf.DeltaAngle(0f, roll),
                    PitchAngle = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg,
                    ArcLength = entry.ArcLength + L * u
                };
            }

            var first = frames[0];
            frames[0] = entry;
            frames[0].BankAngle = first.BankAngle;
            frames[0].ArcLength = first.ArcLength;

            var last = frames[rings - 1];
            last.Position = basePos + fwdH * L + Vector3.up * elevation;
            last.Forward = fwdH;
            last.Right = Flatten(entry.Right);
            last.Up = Vector3.up;
            last.BankAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Spiral ───────────────────────────

        /// <summary>Arc-length estimate for a spiral whose helix drifts forward while climbing.</summary>
        public static float EstimateDriftingSpiralLength(float radius, float turnAngleDegrees,
            float forwardDrift, float climb)
            => EstimatePartialHelixLength(radius,
                Mathf.Max(360f, Mathf.Abs(turnAngleDegrees)), forwardDrift, climb);

        /// <summary>
        /// Arc-length estimate for a level-ended partial or complete helix. Unlike the
        /// legacy spiral contract, this permits a 150–180° Half Helix Turnaround.
        /// </summary>
        public static float EstimatePartialHelixLength(float radius, float turnAngleDegrees,
            float forwardDrift, float climb)
        {
            const int steps = 256;
            float totalAngleRad = Mathf.Max(1f, Mathf.Abs(turnAngleDegrees)) * Mathf.Deg2Rad;
            float circularDerivative = Mathf.Max(1f, radius) * totalAngleRad;
            float length = 0f;
            for (int i = 0; i < steps; i++)
            {
                float u = (i + 0.5f) / steps;
                float theta = totalAngleRad * u;
                float smoothDerivative = 6f * u * (1f - u);
                // Circular tangent dot the fixed forward axis is cos(theta).
                float driftDerivative = forwardDrift * smoothDerivative;
                float verticalDerivative = climb * smoothDerivative;
                float horizontalSq = circularDerivative * circularDerivative
                                   + driftDerivative * driftDerivative
                                   + 2f * circularDerivative * driftDerivative * Mathf.Cos(theta);
                length += Mathf.Sqrt(Mathf.Max(0f, horizontalSq) + verticalDerivative * verticalDerivative) / steps;
            }
            return length;
        }

        /// <summary>
        /// Climbing/descending helix (parking-garage spiral): full revolutions around a
        /// vertical axis, optionally drifting forward to absorb its own recovery run,
        /// and exiting with the entry heading. Bobsled-banked like every corner.
        /// </summary>
        public static TrackConnectionFrame[] BuildSpiral(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            bool partialHelix = def.SemanticElement == SemanticElementId.HalfHelixTurnaround;
            float totalAngle = partialHelix
                ? Mathf.Clamp(Mathf.Abs(def.TurnAngle), 1f, 359.999f)
                : Mathf.Max(360f, Mathf.Abs(def.TurnAngle));
            float side = def.TurnSign != 0 ? def.TurnSign : 1f;
            float radius = Mathf.Max(def.Width, def.Radius);
            float arcLen = def.Length;
            float climb = def.ElevationChange; // may be negative (descending spiral)
            float forwardDrift = Mathf.Max(0f, def.PlanHorizontalLength);
            float circularDerivative = radius * totalAngle * Mathf.Deg2Rad;

            int facetRings = Mathf.CeilToInt(totalAngle / ctx.MaxFacetAngle) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(arcLen / ctx.FeatureMetersPerRing) + 1, facetRings), 24, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 center = entry.Position + rightH * (side * radius);
            Vector3 toStart = entry.Position - center;

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float theta = side * totalAngle * u;

                Quaternion yaw = Quaternion.AngleAxis(theta, Vector3.up);
                float smooth = Smooth01(u);
                float smoothDerivative = 6f * u * (1f - u);
                Vector3 pos = center + yaw * toStart
                              + fwdH * (forwardDrift * smooth)
                              + Vector3.up * (climb * smooth);

                Vector3 flatFwd = yaw * fwdH;
                Vector3 derivative = flatFwd * circularDerivative
                                     + fwdH * (forwardDrift * smoothDerivative)
                                     + Vector3.up * (climb * smoothDerivative);
                Vector3 fwd = derivative.normalized;
                Vector3 right = yaw * rightH;
                right = (right - Vector3.Dot(right, fwd) * fwd).normalized;
                Vector3 up = Vector3.Cross(fwd, right).normalized;

                // Pure helix geometry — wall support/tilt come from the global banking field.
                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg,
                    ArcLength = entry.ArcLength + arcLen * u
                };
            }

            // Exact exit: partial helices preserve their opposite-direction circular
            // endpoint; complete spirals reduce to the historical start-axis exit.
            var last = frames[rings - 1];
            Quaternion exitYaw = Quaternion.AngleAxis(side * totalAngle, Vector3.up);
            last.Position = center + exitYaw * toStart +
                            fwdH * forwardDrift + Vector3.up * climb;
            last.Forward = (exitYaw * fwdH).normalized;
            last.Right = (exitYaw * rightH).normalized;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            last.BankAngle = 0f;
            last.LeftWallSuppression = 0f;
            last.RightWallSuppression = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Half loop + rollout ───────────────────────────

        /// <summary>
        /// Half loop up to inverted, then a level roll-out back to upright while flying
        /// along the REVERSED heading at the top height. The rollout length comes from the
        /// definition (Length − half-loop arc); the roll profile is smoothstepped, so the
        /// roll rate is zero at both welds. Total roll may exceed 180° for the
        /// half-loop-into-corkscrew compound (RollChange carries the signed total).
        /// </summary>
        public static TrackConnectionFrame[] BuildHalfLoopRollout(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            EnsureHalfLoopProfile();

            float halfArc = HalfLoopArcLength(def.Radius);
            float rollLen = Mathf.Max(10f, def.Length - halfArc);
            float topHeight = HalfLoopTopHeight(halfArc);
            float totalRoll = Mathf.Max(180f, Mathf.Abs(def.RollChange));
            float rollSign = def.RollChange >= 0f ? 1f : -1f;

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            int ringsA = Mathf.Clamp(
                Mathf.Max(Mathf.CeilToInt(halfArc / ctx.FeatureMetersPerRing),
                          Mathf.CeilToInt(180f / ((1f - LoopCurvatureEaseFraction) * ctx.MaxFacetAngle))) + 1,
                16, ctx.MaxRingsPerSection / 2);
            int ringsB = Mathf.Clamp(
                Mathf.Max(Mathf.CeilToInt(rollLen / ctx.FeatureMetersPerRing),
                          Mathf.CeilToInt(totalRoll * 1.5f / ctx.MaxFacetAngle)) + 1,
                16, ctx.MaxRingsPerSection / 2);

            var frames = new TrackConnectionFrame[ringsA + ringsB - 1];

            // Phase A: half loop, upright → inverted.
            for (int i = 0; i < ringsA; i++)
            {
                float u = (float)i / (ringsA - 1);
                float theta = TableThetaAt(_halfLoopThetaTable, u);
                Vector2 pl = TablePlaneAt(_halfLoopPlaneTable, u) * halfArc;

                Vector3 pos = basePos + fwdH * pl.x + Vector3.up * pl.y;
                Vector3 fwd = (fwdH * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
                Vector3 up = Quaternion.AngleAxis(-theta * Mathf.Rad2Deg, rightH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = pos,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = theta * Mathf.Rad2Deg,
                    ArcLength = entry.ArcLength + halfArc * u
                };
            }

            // Phase B: level roll-out along the reversed heading, inverted → upright.
            Vector3 topPos = frames[ringsA - 1].Position;
            Vector3 exitFwd = -fwdH;

            for (int j = 1; j < ringsB; j++)
            {
                float t = (float)j / (ringsB - 1);
                float roll = totalRoll * Smooth01(t);

                Vector3 up = Quaternion.AngleAxis(rollSign * roll, exitFwd) * (-Vector3.up);
                up = (up - Vector3.Dot(up, exitFwd) * exitFwd).normalized;
                Vector3 right = Vector3.Cross(up, exitFwd).normalized;

                frames[ringsA - 1 + j] = new TrackConnectionFrame
                {
                    Position = topPos + exitFwd * (rollLen * t),
                    Forward = exitFwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = rollSign * Mathf.DeltaAngle(0f, totalRoll - roll),
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + halfArc + rollLen * t
                };
            }

            // Exact exit: level, reversed heading, top height, upright (total roll lands
            // the craft upright: 180° + n·360° for the corkscrew-extended variants).
            int lastIdx = frames.Length - 1;
            var last = frames[lastIdx];
            last.Position = basePos + fwdH * HalfLoopForwardDisplacement(halfArc) + exitFwd * rollLen + Vector3.up * topHeight;
            last.Forward = exitFwd;
            last.Right = Vector3.Cross(Vector3.up, exitFwd).normalized;
            last.Up = Vector3.up;
            last.BankAngle = 0f;
            last.PitchAngle = 0f;
            frames[lastIdx] = last;

            return frames;
        }
    }
}
