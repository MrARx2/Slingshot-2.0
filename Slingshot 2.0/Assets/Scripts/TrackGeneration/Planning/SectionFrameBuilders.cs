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
            FloorTiltFraction = cfg.FloorTiltFraction
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

        /// <summary>Net-zero bump 0→1→0 with zero end derivatives, peak at u = 0.5.</summary>
        public static float Bump(float u) => 4f * Smooth01(u) * Smooth01(1f - u);

        /// <summary>Analytic derivative of <see cref="Bump"/>; peak magnitude ≈ 3.04.</summary>
        public static float BumpDerivative(float u) => 24f * u * (1f - u) * (Smooth01(1f - u) - Smooth01(u));

        /// <summary>Ease 0→1 over easeFrac, hold, 1→0 over the exit easeFrac.</summary>
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

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float s = length * u;

                float h = delta * Smooth01(u) + hill * Bump(u);
                float slope = (delta * 6f * u * (1f - u) + hill * BumpDerivative(u)) / Mathf.Max(length, 0.01f);

                Vector3 fwd = (fwdH + Vector3.up * slope).normalized;
                Vector3 up = Vector3.Cross(fwd, rightH).normalized;

                float widthT = blendLen > 0.001f ? TrackBlend.Evaluate(ctx.BlendCurve, s / blendLen) : 1f;

                frames[i] = new TrackConnectionFrame
                {
                    Position = new Vector3(entry.Position.x + fwdH.x * s, entry.Position.y + h, entry.Position.z + fwdH.z * s),
                    Forward = fwd,
                    Right = rightH,
                    Up = up,
                    Width = Mathf.Lerp(entry.Width, def.Width, widthT),
                    BankAngle = 0f,
                    PitchAngle = Mathf.Rad2Deg * Mathf.Atan(slope),
                    ArcLength = entry.ArcLength + s
                };
            }

            // Exact flat exit at entry height + delta (weld contract).
            var last = frames[rings - 1];
            last.Position = new Vector3(entry.Position.x + fwdH.x * length, entry.Position.y + delta, entry.Position.z + fwdH.z * length);
            last.Forward = fwdH;
            last.Right = rightH;
            last.Up = Vector3.up;
            last.PitchAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Arcs (bobsled banked) ───────────────────────────

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
        {
            float side = Mathf.Sign(signedAngleDeg);
            float angleAbs = QuantizeArcAngle(Mathf.Abs(signedAngleDeg));
            var profile = GetArcProfile(angleAbs);
            float arcLen = EasedArcLength(angleAbs, radius);

            // Peak yaw rate matches the circular arc (peak κ = 1/R), so the facet bound
            // uses the eased-equivalent angle budget, like the loop builder.
            int facetRings = Mathf.CeilToInt(angleAbs / ((1f - ArcCurvatureEaseFraction) * ctx.MaxFacetAngle)) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(arcLen / ctx.MetersPerRing) + 1, facetRings), 9, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 rightH = Flatten(entry.Right);
            Vector3 basePos = entry.Position;

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float thetaDeg = side * SampleTable(profile.theta, u) * Mathf.Rad2Deg;
                Vector2 pl = SampleTable(profile.plane, u) * arcLen;

                Quaternion yaw = Quaternion.AngleAxis(thetaDeg, Vector3.up);

                frames[i] = new TrackConnectionFrame
                {
                    Position = basePos + fwdH * pl.x + rightH * (side * pl.y),
                    Forward = yaw * fwdH,
                    Right = yaw * rightH,
                    Up = Vector3.up,
                    Width = entry.Width,
                    BankAngle = 0f,
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + arcLen * u
                };
            }

            // Exact eased end pose (identical to the planner's 2D model).
            Vector2 endPl = profile.plane[profile.plane.Length - 1] * arcLen;
            Quaternion endYaw = Quaternion.AngleAxis(side * angleAbs, Vector3.up);
            var last = frames[rings - 1];
            last.Position = basePos + fwdH * endPl.x + rightH * (side * endPl.y);
            last.Forward = endYaw * fwdH;
            last.Right = endYaw * rightH;
            last.Up = Vector3.up;
            frames[rings - 1] = last;

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

        /// <summary>Keyframes for a jump launch ramp: level → climb pitch → shallow launch pitch at the lip.</summary>
        public static (float u, float pitch)[] LaunchRampKeys(float climbPitchDeg, float launchPitchDeg)
            => new[] { (0f, 0f), (0.6f, climbPitchDeg), (1f, launchPitchDeg) };

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
        /// One corkscrew: the road rolls smoothly around the travel axis on a helix
        /// centerline, entering and exiting upright, with analytic tangents.
        /// </summary>
        public static TrackConnectionFrame[] BuildCorkscrew(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            float L = def.Length;
            float r = def.Radius;
            float rollTotal = def.RollChange; // signed

            int facetRings = Mathf.CeilToInt(Mathf.Abs(rollTotal) * 1.5f / ctx.MaxFacetAngle) + 1;
            int rings = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(L / ctx.FeatureMetersPerRing) + 1, facetRings), 24, ctx.MaxRingsPerSection);
            var frames = new TrackConnectionFrame[rings];

            Vector3 fwdH = Flatten(entry.Forward);
            Vector3 basePos = entry.Position;

            Vector3 RadialAt(float u)
            {
                float roll = rollTotal * Smooth01(u);
                return Quaternion.AngleAxis(roll, fwdH) * (-Vector3.up);
            }

            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1);
                float roll = rollTotal * Smooth01(u);
                Vector3 radial = RadialAt(u);

                float rollRateRad = rollTotal * 6f * u * (1f - u) * Mathf.Deg2Rad;
                Vector3 fwd = (fwdH * L + Vector3.Cross(fwdH, radial) * (r * rollRateRad)).normalized;

                Vector3 up = Quaternion.AngleAxis(roll, fwdH) * Vector3.up;
                up = (up - Vector3.Dot(up, fwd) * fwd).normalized;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                frames[i] = new TrackConnectionFrame
                {
                    Position = basePos + fwdH * (L * u) + Vector3.up * r + radial * r,
                    Forward = fwd,
                    Right = right,
                    Up = up,
                    Width = entry.Width,
                    BankAngle = Mathf.DeltaAngle(0f, roll),
                    PitchAngle = 0f,
                    ArcLength = entry.ArcLength + L * u
                };
            }

            var first = frames[0];
            frames[0] = entry;
            frames[0].BankAngle = first.BankAngle;
            frames[0].ArcLength = first.ArcLength;

            var last = frames[rings - 1];
            last.Position = basePos + fwdH * L;
            last.Forward = fwdH;
            last.Right = Flatten(entry.Right);
            last.Up = Vector3.up;
            last.BankAngle = 0f;
            frames[rings - 1] = last;

            return frames;
        }

        // ─────────────────────────── Spiral ───────────────────────────

        /// <summary>
        /// Climbing/descending helix (parking-garage spiral): full revolutions around a
        /// vertical axis, exiting directly above/below the entry with the entry heading —
        /// zero net 2D displacement by construction. Bobsled-banked like every corner.
        /// </summary>
        public static TrackConnectionFrame[] BuildSpiral(TrackConnectionFrame entry, TrackMacroSectionDefinition def,
            in FrameBuildContext ctx)
        {
            float totalAngle = Mathf.Max(360f, def.TurnAngle);
            float side = def.TurnSign != 0 ? def.TurnSign : 1f;
            float radius = Mathf.Max(def.Width, def.Radius);
            float arcLen = def.Length;
            float climb = def.ElevationChange; // may be negative (descending spiral)

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
                Vector3 pos = center + yaw * toStart + Vector3.up * (climb * Smooth01(u));

                float slope = climb * 6f * u * (1f - u) / Mathf.Max(arcLen, 0.01f);
                Vector3 flatFwd = yaw * fwdH;
                Vector3 fwd = (flatFwd + Vector3.up * slope).normalized;
                Vector3 right = yaw * rightH;
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
                    PitchAngle = Mathf.Rad2Deg * Mathf.Atan(slope),
                    ArcLength = entry.ArcLength + arcLen * u
                };
            }

            // Exact exit: directly above/below the entry, entry heading, level, unbanked.
            var last = frames[rings - 1];
            last.Position = entry.Position + Vector3.up * climb;
            last.Forward = fwdH;
            last.Right = rightH;
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
