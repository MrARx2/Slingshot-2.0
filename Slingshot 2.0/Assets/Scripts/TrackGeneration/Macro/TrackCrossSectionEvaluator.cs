using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// THE unified parametric road cross-section: one ordered 2D point chain (in the
    /// frame's Right/Up plane) that continuously expresses every road shape the game
    /// uses — circular flat-centered half-pipe, dynamically deeper circular turn bowl,
    /// explicitly-authored wallride support, three-quarter pipe and fully closed pipe.
    /// The mesh builder, the collider builder and the debug visualizer
    /// all evaluate THIS chain, so render and collision can never disagree.
    ///
    /// Chain order (fixed topology — the point count never changes per track):
    ///   [0 .. ext-1]                left wall extension, TIP first, down to the wall top
    ///   [ext .. ext+bowl-1]         bowl profile, left wall top → flat center → right wall top
    ///   [ext+bowl .. N-1]           right wall extension, wall top up to the TIP
    ///
    /// A height function h(x) cannot represent an overhang (one x needs two heights);
    /// this chain is parametric, so the wall extension continues the wall PAST
    /// vertical as a circular capture curl. The safety lip is the same mechanism at
    /// minimum engagement — a small curl instead of a raised fin — and the catch wall
    /// and wallride simply engage it further. Pipe closure morphs the whole chain
    /// toward a circle whose tips meet at the roof.
    /// </summary>
    public static class TrackCrossSection
    {
        /// <summary>Extension points per side (fixed per profile — stable topology).</summary>
        public static int ExtensionPointCount(TrackRoadProfileSettings p)
            => p.IsHalfPipe ? Mathf.Clamp(p.ProfileResolution / 3, 4, 24) : 0;

        /// <summary>Total chain point count (bowl + both wall extensions).</summary>
        public static int PointCount(TrackRoadProfileSettings p)
            => p.ProfilePointCount + 2 * ExtensionPointCount(p);

        /// <summary>
        /// Evaluates the chain for one frame into <paramref name="points"/> (length ≥
        /// <see cref="PointCount"/>), in frame-local 2D: x along Right, y along Up.
        /// Optional <paramref name="crossParams"/> receives the signed guidance
        /// parameter per point: 0 = road center, ±1 = center-flat boundary, ±2 = wall
        /// tip (drives the center guide lines and wall markers without geometry).
        /// </summary>
        public static void Evaluate(TrackRoadProfileSettings p, in TrackConnectionFrame f,
            Vector2[] points, float[] crossParams = null)
        {
            int ext = ExtensionPointCount(p);
            int bowl = p.ProfilePointCount;
            int total = bowl + 2 * ext;

            float halfW = Mathf.Max(0.01f, f.Width * 0.5f);
            float sideH = f.SideHeight > 0.001f ? f.SideHeight : p.SideHeight;
            p.ResolveCircularBowl(halfW, sideH, f.TurnRounding,
                out float flat, out float curve, out float evaluatedSideH);
            float baseH = p.ClampedSideHeight(halfW, evaluatedSideH, flat, curve);
            float closure = Mathf.Clamp01(f.PipeClosure);
            float closureW = closure * closure * (3f - 2f * closure); // smoothstep

            // ── Bowl (left → right), one authored warped distribution ──
            // Vertex columns stay at fixed lateral coordinates for the entire track.
            // Turn rounding changes the evaluated HEIGHT profile, never the topology's
            // x parameterization; otherwise a longitudinal texture column slides
            // sideways between rings and produces diagonal UV shear.
            for (int i = 0; i < bowl; i++)
            {
                float xNorm = p.ProfileXAt(i, bowl);
                float mult = xNorm < 0f ? f.LeftWallMultiplier : f.RightWallMultiplier;
                float h = p.HeightAt(xNorm, halfW, evaluatedSideH, flat, curve) * mult;
                points[ext + i] = new Vector2(xNorm * halfW, h);
                if (crossParams != null)
                    crossParams[ext + i] = Mathf.Sign(xNorm) * Mathf.Min(2f, Mathf.Abs(xNorm) / Mathf.Max(flat, 0.02f));
            }

            // ── Wall extensions: safety-lip curl → catch wall → wallride support ──
            if (ext > 0)
            {
                EvaluateExtension(p, ext, halfW, flat, curve, evaluatedSideH, baseH,
                    f.LeftWallMultiplier, Mathf.Clamp01(f.LeftOverhang), closureW, left: true, points, crossParams);
                EvaluateExtension(p, ext, halfW, flat, curve, evaluatedSideH, baseH,
                    f.RightWallMultiplier, Mathf.Clamp01(f.RightOverhang), closureW, left: false, points, crossParams);
            }

            // ── Wallride morph: fold the whole chain onto the OUTSIDE wall. ──
            // A wallride is JUST the outside wall of the turn — no flat center, no
            // inside wall. The chain keeps its fixed topology; every point is
            // resampled along the outside-wall polyline (wall base → curl tip), so
            // entry/exit blend smoothly and slow craft fall off the wall's lower
            // edge into the turn interior.
            float wallrideW = Mathf.Clamp01(f.WallrideMorph);
            if (wallrideW > 0.0001f)
            {
                // Which wall is ridden. A bare >= tie-breaks to LEFT whenever the two
                // overhangs are equal, so a tie occurring while the morph is already
                // engaged would flip the entire cross-section from one wall to the
                // other in a single ring. Break ties on the wall multiplier instead —
                // during a wallride the riding wall is the boosted one, which is an
                // unambiguous and continuously varying signal.
                float overhangBias = f.LeftOverhang - f.RightOverhang;
                bool rideLeft = Mathf.Abs(overhangBias) > 1e-4f
                    ? overhangBias > 0f
                    : f.LeftWallMultiplier >= f.RightWallMultiplier;
                // NO extra easing here. WallrideMorph already arrives fully eased
                // from the section builder's HoldProfile (SmootherStep, zero slope
                // at both welds). Squaring a second smoothstep on top multiplied the
                // peak engagement rate by 1.5× for nothing — the fold happened in the
                // middle third of a ramp that was paid for in full length.
                MorphOntoWall(points, total, ext, bowl, halfW, flat, rideLeft, wallrideW);
            }

            // ── Pipe closure: morph the whole chain toward a circle whose tips meet
            // at the roof. γ is the remaining opening half-angle; at closure 0 the
            // target is a plain U (never visible — weight 0), at 1 a full circle. ──
            if (closureW > 0.0001f)
            {
                float R = halfW;
                float gamma = (Mathf.PI * 0.5f) * (1f - closure) + 0.0001f;
                float psi0 = Mathf.PI * 0.5f + gamma;
                float sweep = Mathf.PI * 2f - 2f * gamma;

                for (int k = 0; k < total; k++)
                {
                    float t = total > 1 ? (float)k / (total - 1) : 0.5f;
                    float psi = psi0 + t * sweep;
                    var pipePoint = new Vector2(Mathf.Cos(psi) * R, R + Mathf.Sin(psi) * R);
                    points[k] = Vector2.Lerp(points[k], pipePoint, closureW);
                }
            }
        }

        /// <summary>
        /// One side's wall extension: a circular arc continuing the wall's top tangent
        /// and curling inward over the road. Sweep grows from the safety-lip minimum to
        /// the full catch-wall angle with engagement; radius blends lip → overhang.
        /// Points run TIP→base for the left side, base→TIP for the right side.
        /// </summary>
        private static void EvaluateExtension(TrackRoadProfileSettings p, int ext, float halfW,
            float flat, float curve, float sideH, float baseH, float wallMult, float engagement, float closureW,
            bool left, Vector2[] points, float[] crossParams)
        {
            float mult = Mathf.Clamp(wallMult, 0f, 3f);
            float presence = Mathf.Clamp01(mult);         // suppressed walls lose their curl
            float hTop = baseH * mult;

            // Wall-top tangent angle from the arc model (vertical at full curve).
            float thetaTop = p.TipTangentRad(halfW, sideH * mult, flat, curve);
            float thetaTopDeg = thetaTop * Mathf.Rad2Deg;

            // Safety lip: a minimal capture curl (same mechanism, small engagement).
            float lipSweepDeg = p.SafetyLipHeight > 0.01f
                ? Mathf.Min(25f, Mathf.Max(0f, 90f - thetaTopDeg) + 10f)
                : 0f;
            float lipRadius = Mathf.Max(0.4f, p.SafetyLipHeight);

            float overSweepDeg = engagement * (Mathf.Max(0f, 90f - thetaTopDeg) + Mathf.Max(0f, p.MaxOverhangAngleDeg));
            float sweepDeg = Mathf.Max(lipSweepDeg * presence, overSweepDeg * presence);
            sweepDeg *= 1f - closureW; // the closing pipe replaces lips and curls entirely
            float radius = Mathf.Lerp(lipRadius, Mathf.Max(lipRadius, p.OverhangRadius), engagement);

            float xTop = left ? -halfW : halfW;
            var p0 = new Vector2(xTop, hTop);

            // Arc center: perpendicular (inward-up) from the wall-top tangent.
            float sinT = Mathf.Sin(thetaTop), cosT = Mathf.Cos(thetaTop);
            Vector2 center = left
                ? p0 + radius * new Vector2(sinT, cosT)
                : p0 + radius * new Vector2(-sinT, cosT);
            Vector2 rv = p0 - center;

            for (int k = 1; k <= ext; k++)
            {
                float phi = sweepDeg * Mathf.Deg2Rad * k / ext;
                float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
                // Left side curls clockwise (tangent angle decreasing), right side CCW.
                Vector2 rot = left
                    ? new Vector2(rv.x * c + rv.y * s, -rv.x * s + rv.y * c)
                    : new Vector2(rv.x * c - rv.y * s, rv.x * s + rv.y * c);
                Vector2 pt = center + rot;

                int idx = left ? ext - k : ext + p.ProfilePointCount - 1 + k;
                points[idx] = pt;
                if (crossParams != null) crossParams[idx] = left ? -2f : 2f;
            }
        }

        // Scratch buffers for the wallride morph (generation is single-threaded and
        // the buffers are fully consumed within one Evaluate call).
        private static Vector2[] _wallPoly = new Vector2[512];
        private static float[] _wallCum = new float[512];

        /// <summary>
        /// Folds the chain onto the riding-side wall by SLIDING THE SAMPLING WINDOW
        /// along the real cross-section — never by lerping point positions.
        ///
        /// The chain already traces the true road profile, and a wallride is that
        /// same profile with the inside floor simply no longer sampled. So the morph
        /// is a reparametrisation: at weight 0 every point keeps its own arc position
        /// (identically the ordinary road), at weight 1 the points spread evenly
        /// across the riding wall's arc window, and in between each point sits at a
        /// blend of the two. Every intermediate cross-section is therefore an EXACT
        /// sub-arc of a genuine road profile.
        ///
        /// The previous version lerped each point straight toward its wall target.
        /// Those chords cut across the profile's curvature, so the section deflated
        /// mid-blend: chain length fell to 170 m between endpoints of 227 m and
        /// 195 m — a 25 m collapse that reads as the pinch/crease at both welds.
        /// Sliding the window keeps the length monotonic between its endpoints.
        /// </summary>
        private static void MorphOntoWall(Vector2[] points, int total, int ext, int bowl,
            float halfW, float flat, bool left, float weight)
        {
            if (_wallPoly.Length < total) { _wallPoly = new Vector2[total * 2]; _wallCum = new float[total * 2]; }

            if (total < 2) return;

            // Arc length along the whole chain (left tip → right tip).
            _wallCum[0] = 0f;
            for (int k = 1; k < total; k++)
                _wallCum[k] = _wallCum[k - 1] + Vector2.Distance(points[k - 1], points[k]);

            float span = _wallCum[total - 1];
            if (span < 0.001f) return;

            // Arc position of the riding wall's base — the flat-center boundary.
            //
            // Placed by EXACT interpolation between the two samples straddling it.
            // Snapping to a whole sample instead makes the window POP by a full
            // sample spacing whenever `flat` (which moves every ring under dynamic
            // turn rounding) crosses one: an 8.2 m shift in a single ~1 m ring —
            // the stair-step that used to fling the craft.
            float flatX = left ? -flat * halfW : flat * halfW;
            float boundary = left ? 0f : span;
            for (int k = 1; k < total; k++)
            {
                bool straddles = left
                    ? points[k - 1].x <= flatX && points[k].x > flatX
                    : points[k - 1].x < flatX && points[k].x >= flatX;
                if (!straddles) continue;

                float dx = points[k].x - points[k - 1].x;
                float cross = Mathf.Abs(dx) > 1e-6f ? (flatX - points[k - 1].x) / dx : 0f;
                boundary = Mathf.Lerp(_wallCum[k - 1], _wallCum[k], Mathf.Clamp01(cross));
                break;
            }

            // Full-morph window = the riding wall alone. The left wall occupies the
            // head of the chain (its tip at arc 0), the right wall the tail (tip at
            // arc span), so the wall-side end of the chain stays put either way.
            float winStart = left ? 0f : boundary;
            float winEnd = left ? boundary : span;
            if (winEnd - winStart < 0.001f) return;

            // Resample into scratch: the source chain is still being read below.
            int seg = 1;
            for (int k = 0; k < total; k++)
            {
                float u = (float)k / (total - 1);

                // Blend this point's OWN arc position toward its position in the
                // wall window. Weight 0 reproduces the ordinary road exactly; weight
                // 1 spreads the chain evenly across the wall; everything between is
                // a real sub-arc of the profile rather than a chord across it.
                float s = Mathf.Lerp(_wallCum[k], Mathf.Lerp(winStart, winEnd, u), weight);

                seg = 1;
                while (seg < total - 1 && _wallCum[seg] < s) seg++;
                float segLen = Mathf.Max(1e-6f, _wallCum[seg] - _wallCum[seg - 1]);
                float t = Mathf.Clamp01((s - _wallCum[seg - 1]) / segLen);
                _wallPoly[k] = Vector2.Lerp(points[seg - 1], points[seg], t);
            }

            for (int k = 0; k < total; k++) points[k] = _wallPoly[k];
        }
    }
}
