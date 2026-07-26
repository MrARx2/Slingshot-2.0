using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// THE unified parametric road cross-section: one ordered 2D point chain (in the
    /// frame's Right/Up plane) that continuously expresses every road shape the game
    /// uses — flat-centered half-pipe, dynamically rounded turn bowl, outside catch
    /// wall curling past vertical, wallride support, three-quarter pipe and fully
    /// closed pipe. The mesh builder, the collider builder and the debug visualizer
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
            float rounding = Mathf.Clamp01(f.TurnRounding);
            float flat = Mathf.Lerp(Mathf.Clamp01(p.CenterFlatWidthRatio),
                Mathf.Clamp01(p.MinTurnCenterFlatRatio), rounding);
            // Turn rounding pulls the wall bend toward a full quarter circle.
            float curve = Mathf.Lerp(Mathf.Clamp01(p.WallCurve01), 1f, rounding);
            float sideH = f.SideHeight > 0.001f ? f.SideHeight : p.SideHeight;
            float baseH = p.ClampedSideHeight(halfW, sideH, flat, curve);
            float closure = Mathf.Clamp01(f.PipeClosure);
            float closureW = closure * closure * (3f - 2f * closure); // smoothstep

            // ── Bowl (left → right), the classic warped distribution ──
            for (int i = 0; i < bowl; i++)
            {
                float xNorm = p.ProfileXAt(i, bowl, flat);
                float mult = xNorm < 0f ? f.LeftWallMultiplier : f.RightWallMultiplier;
                float h = p.HeightAt(xNorm, halfW, sideH, flat, curve) * mult;
                points[ext + i] = new Vector2(xNorm * halfW, h);
                if (crossParams != null)
                    crossParams[ext + i] = Mathf.Sign(xNorm) * Mathf.Min(2f, Mathf.Abs(xNorm) / Mathf.Max(flat, 0.02f));
            }

            // ── Wall extensions: safety-lip curl → catch wall → wallride support ──
            if (ext > 0)
            {
                EvaluateExtension(p, ext, halfW, flat, curve, sideH, baseH,
                    f.LeftWallMultiplier, Mathf.Clamp01(f.LeftOverhang), closureW, left: true, points, crossParams);
                EvaluateExtension(p, ext, halfW, flat, curve, sideH, baseH,
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
                bool rideLeft = f.LeftOverhang >= f.RightOverhang;
                MorphOntoWall(points, total, ext, bowl, halfW, flat, rideLeft,
                    wallrideW * wallrideW * (3f - 2f * wallrideW));
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
        /// Folds the whole chain onto the riding-side wall: collects that wall's
        /// polyline (flat-center boundary → bowl wall → extension curl tip), then
        /// resamples every chain point uniformly along it, oriented so the wall-side
        /// end of the chain barely moves. Weight blends from the ordinary road.
        /// </summary>
        private static void MorphOntoWall(Vector2[] points, int total, int ext, int bowl,
            float halfW, float flat, bool left, float weight)
        {
            if (_wallPoly.Length < total) { _wallPoly = new Vector2[total * 2]; _wallCum = new float[total * 2]; }

            float flatX = flat * halfW;
            int count = 0;
            int center = ext + bowl / 2;

            // Wall polyline in base→tip order.
            if (left)
            {
                for (int i = center; i >= ext; i--)
                    if (points[i].x <= -flatX + 0.001f) _wallPoly[count++] = points[i];
                for (int k = ext - 1; k >= 0; k--) _wallPoly[count++] = points[k];
            }
            else
            {
                for (int i = center; i < ext + bowl; i++)
                    if (points[i].x >= flatX - 0.001f) _wallPoly[count++] = points[i];
                for (int k = ext + bowl; k < total; k++) _wallPoly[count++] = points[k];
            }
            if (count < 2) return;

            _wallCum[0] = 0f;
            for (int i = 1; i < count; i++)
                _wallCum[i] = _wallCum[i - 1] + Vector2.Distance(_wallPoly[i - 1], _wallPoly[i]);

            float totalLen = _wallCum[count - 1];
            if (totalLen < 0.001f) return;

            int seg = 1;
            for (int k = 0; k < total; k++)
            {
                float u = total > 1 ? (float)k / (total - 1) : 0f;
                // Orient so the riding-side chain end lands on the wall tip: for a
                // LEFT wall the chain starts at the left tip (s = 1 at k = 0).
                float s = left ? 1f - u : u;
                float d = s * totalLen;

                // d is monotonic per orientation; simple forward scan per point.
                seg = 1;
                while (seg < count - 1 && _wallCum[seg] < d) seg++;
                float segLen = Mathf.Max(0.0001f, _wallCum[seg] - _wallCum[seg - 1]);
                float t = Mathf.Clamp01((d - _wallCum[seg - 1]) / segLen);
                Vector2 target = Vector2.Lerp(_wallPoly[seg - 1], _wallPoly[seg], t);

                points[k] = Vector2.Lerp(points[k], target, weight);
            }
        }
    }
}
