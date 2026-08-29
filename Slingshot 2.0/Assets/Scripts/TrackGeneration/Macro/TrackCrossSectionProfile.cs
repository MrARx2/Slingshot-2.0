using System;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// The global road cross-section shape.
    /// HalfPipe is the DEFAULT for the hovercraft generator — the entire track is a
    /// rideable water-slide / bobsled channel. FlatWithWalls exists for legacy/debug only.
    /// </summary>
    public enum RoadCrossSectionShape
    {
        FlatWithWalls,
        ShallowHalfPipe,
        HalfPipe,
        DeepHalfPipe
    }

    /// <summary>Easing curves used by the section transition blend system.</summary>
    public enum TrackBlendCurve
    {
        Linear,
        SmoothStep,
        SmootherStep,
        EaseInOut,
        Custom
    }

    /// <summary>High-level transition smoothness (folded into Speed Profile internally).</summary>
    public enum TrackTransitionSmoothness
    {
        Snappy,
        Normal,
        Smooth,
        VerySmooth
    }

    /// <summary>Shared easing evaluation for all track blends.</summary>
    public static class TrackBlend
    {
        /// <summary>Evaluates the blend curve at t (clamped to [0,1]).</summary>
        public static float Evaluate(TrackBlendCurve curve, float t)
        {
            t = Mathf.Clamp01(t);
            switch (curve)
            {
                case TrackBlendCurve.Linear:
                    return t;
                case TrackBlendCurve.SmoothStep:
                    return t * t * (3f - 2f * t);
                case TrackBlendCurve.EaseInOut:
                    return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
                case TrackBlendCurve.Custom: // custom falls back to smootherstep until a curve asset exists
                case TrackBlendCurve.SmootherStep:
                default:
                    return t * t * t * (t * (6f * t - 15f) + 10f);
            }
        }

        /// <summary>Peak derivative of the curve on [0,1] (used by ramp-safety checks).</summary>
        public static float PeakDerivative(TrackBlendCurve curve)
        {
            switch (curve)
            {
                case TrackBlendCurve.Linear: return 1f;
                case TrackBlendCurve.SmoothStep: return 1.5f;
                case TrackBlendCurve.EaseInOut: return Mathf.PI * 0.5f;
                default: return 1.875f; // SmootherStep
            }
        }
    }

    /// <summary>
    /// Resolved half-pipe cross-section settings consumed by the mesh builder and the
    /// debug visualizer. Values are already clamped by the TrackConfig rulebook.
    /// </summary>
    [Serializable]
    public class TrackRoadProfileSettings
    {
        [Tooltip("The global road shape. HalfPipe by default — FlatWithWalls is legacy/debug only.")]
        public RoadCrossSectionShape Shape = RoadCrossSectionShape.HalfPipe;

        [Tooltip("Wall SIZE in meters: the wall's horizontal footprint, and at WallCurve = 1 also its exact height (quarter-circle radius). Per-frame SideHeight overrides this when set.")]
        public float SideHeight = 24f;

        [Tooltip("Wall bend 0..1. 0 = walls lie flat, straight extensions of the floor. 1 = perfect quarter circle (vertical tip). In between: a circular arc over the same footprint, tip height = SideHeight × tan(curve × 45°).")]
        public float WallCurve01 = 1f;

        [Tooltip("Ratio of the road width that stays flat in the center for stable driving. Derived by the resolver from FlatCenterWidth / (FlatCenterWidth + 2 × WallHeight).")]
        public float CenterFlatWidthRatio = 0.25f;

        [Tooltip("Number of cross-section sample points per SIDE (total profile points = 2*resolution + 1). Points are distributed toward the curved walls, where the resolution is actually visible.")]
        public int ProfileResolution = 32;

        [Tooltip("Resolution used to build the COLLISION cross-section (capped by ProfileResolution). Coarse colliders give the hover system a normal 'cliff' at the floor→wall shoulder; the wall-concentrating warp puts most of these points where the surface curves.")]
        public int ColliderProfileResolution = 40;

        [Tooltip("Height of the optional outer safety lip at the very top edge (meters). 0 = none. Realized as a small inward capture curl, not a raised fin.")]
        public float SafetyLipHeight = 1.0f;

        [Tooltip("Center-flat ratio a fully rounded turn interior may reach (dynamic turn rounding blends the base ratio toward this).")]
        public float MinTurnCenterFlatRatio = 0.05f;

        [Tooltip("Maximum catch-wall angle past vertical (degrees). The outside wall of a demanding turn curls this far over the road at full engagement.")]
        public float MaxOverhangAngleDeg = 18f;

        [Tooltip("Radius of the catch-wall capture curl (meters).")]
        public float OverhangRadius = 10f;

        /// <summary>True when the resolved shape produces curved rideable walls.</summary>
        public bool IsHalfPipe => Shape != RoadCrossSectionShape.FlatWithWalls;

        /// <summary>Depth multiplier per shape preset.</summary>
        public float ShapeDepthScale => Shape switch
        {
            RoadCrossSectionShape.ShallowHalfPipe => 0.6f,
            RoadCrossSectionShape.DeepHalfPipe => 1.5f,
            RoadCrossSectionShape.FlatWithWalls => 0f,
            _ => 1f
        };

        /// <summary>
        /// Height of the rideable side profile at a normalized cross position.
        /// x in [-1, 1] (0 = center). Center stays AT baseline; sides rise upward.
        /// Never returns a negative value — no hidden dips below the section baseline.
        /// </summary>
        public float HeightAt(float normalizedX, float halfWidth, float sideHeight)
        {
            ResolveCircularBowl(halfWidth, sideHeight, 0f,
                out float flat, out float curve, out float evaluatedSideHeight);
            return HeightAt(normalizedX, halfWidth, evaluatedSideHeight, flat, curve);
        }

        /// <summary>
        /// Resolves the ordinary-road bowl as a true circular arc. Width/depth and turn
        /// rounding may move the floor-to-wall shoulder, but they cannot change the
        /// wall's 90-degree sweep or stretch the arc into an ellipse. Explicit feature
        /// signals (wallride/pipe morphs) are applied later by TrackCrossSection.
        /// </summary>
        public void ResolveCircularBowl(float halfWidth, float sideHeight, float rounding,
            out float flat, out float curve, out float evaluatedSideHeight)
        {
            halfWidth = Mathf.Max(0.01f, halfWidth);
            if (!IsHalfPipe)
            {
                flat = Mathf.Clamp01(CenterFlatWidthRatio);
                curve = Mathf.Clamp01(WallCurve01);
                evaluatedSideHeight = sideHeight;
                return;
            }

            float depthScale = Mathf.Max(0.0001f, ShapeDepthScale);
            float baseRadius = Mathf.Clamp(sideHeight * depthScale, 0.01f, halfWidth);
            float roundedRadius = Mathf.Clamp(
                halfWidth * (1f - Mathf.Clamp01(MinTurnCenterFlatRatio)),
                baseRadius, halfWidth);
            float radius = Mathf.Lerp(baseRadius, roundedRadius, Mathf.Clamp01(rounding));

            flat = Mathf.Clamp01(1f - radius / halfWidth);
            curve = 1f;
            evaluatedSideHeight = radius / depthScale;
        }

        /// <summary>
        /// Circular-arc wall height with explicit flat ratio and wall curve. Ordinary
        /// half-pipe roads pass coherent values from ResolveCircularBowl; the explicit
        /// arguments remain available for authored special-feature transitions.
        ///
        /// The wall is a CIRCULAR ARC of sweep θ = curve·90° spanning the rising span
        /// E = halfWidth·(1−flat): radius r = E/sinθ, h(x) = r − √(r² − x²). When
        /// sideHeight == E the wall is a true circle — at curve 1 a perfect quarter
        /// circle with a vertical tip.
        /// </summary>
        public float HeightAt(float normalizedX, float halfWidth, float sideHeight, float flat, float curve01)
        {
            if (!IsHalfPipe || sideHeight <= 0f) return 0f;

            float absX = Mathf.Abs(Mathf.Clamp(normalizedX, -1f, 1f));
            if (absX <= flat) return 0f;

            float curve = Mathf.Clamp01(curve01);
            if (curve <= 0.0001f) return 0f; // flat wall: a straight extension of the floor

            float t = (absX - flat) / Mathf.Max(0.0001f, 1f - flat); // 0..1 across the wall span
            float theta = curve * Mathf.PI * 0.5f;
            float sinT = Mathf.Sin(theta);

            // Unit-footprint arc: r = 1/sinθ, h(t) = r − √(r² − t²), tip = tan(θ/2).
            float r = 1f / sinT;
            float unitH = r - Mathf.Sqrt(Mathf.Max(0f, r * r - t * t));

            // Vertical scale so the designed wall (sideHeight == footprint) is a true
            // circle and boosted walls become taller ellipses with the same footprint.
            float footprint = Mathf.Max(0.01f, halfWidth * (1f - flat));
            float scale = sideHeight * ShapeDepthScale / footprint;

            return Mathf.Max(0f, unitH * footprint * scale);
        }

        /// <summary>Wall TIP height for the profile's own flat ratio and curve.</summary>
        public float ClampedSideHeight(float halfWidth, float sideHeight)
        {
            ResolveCircularBowl(halfWidth, sideHeight, 0f,
                out float flat, out float curve, out float evaluatedSideHeight);
            return ClampedSideHeight(halfWidth, evaluatedSideHeight, flat, curve);
        }

        /// <summary>
        /// Wall TIP height (name kept from the legacy wall-angle clamp API). The arc
        /// model has no hidden clamps: tip = sideHeight·tan(θ/2) — exactly sideHeight
        /// at full curve, smoothly down to 0 as the wall flattens.
        /// </summary>
        public float ClampedSideHeight(float halfWidth, float sideHeight, float flat, float curve01)
        {
            float curve = Mathf.Clamp01(curve01);
            if (curve <= 0.0001f) return 0f;
            return sideHeight * ShapeDepthScale * Mathf.Tan(curve * Mathf.PI * 0.25f);
        }

        /// <summary>
        /// Wall tangent angle at the TIP (radians from horizontal), for the given
        /// effective side height (frame override × wall multiplier). At full curve the
        /// tip is exactly vertical for any positive height (a vertically scaled circle
        /// keeps a vertical endpoint tangent).
        /// </summary>
        public float TipTangentRad(float halfWidth, float effectiveSideHeight, float flat, float curve01)
        {
            float curve = Mathf.Clamp01(curve01);
            if (curve <= 0.0001f || effectiveSideHeight <= 0f) return 0f;
            if (curve >= 0.9999f) return Mathf.PI * 0.5f;

            float theta = curve * Mathf.PI * 0.5f;
            float footprint = Mathf.Max(0.01f, halfWidth * (1f - flat));
            float scale = effectiveSideHeight * ShapeDepthScale / footprint;
            return Mathf.Atan(Mathf.Tan(theta) * scale);
        }

        /// <summary>Total top-surface profile point count across the road width.</summary>
        public int ProfilePointCount => IsHalfPipe ? Mathf.Clamp(ProfileResolution, 3, 96) * 2 + 1 : 2;

        /// <summary>
        /// Normalized cross position of profile point <paramref name="i"/> of
        /// <paramref name="count"/>: a WARPED distribution instead of uniform. The flat
        /// center is flat — it needs almost no points — so its samples are thinned and
        /// the budget concentrates on the rising walls, densest near the top where the
        /// profile curves hardest (h = t^p rises steepest at t = 1). Same vertex budget,
        /// roughly twice the effective wall smoothness. Symmetric; endpoints exact.
        /// </summary>
        public float ProfileXAt(int i, int count)
            => ProfileXAt(i, count, Mathf.Clamp01(CenterFlatWidthRatio));

        /// <summary>Warped profile position with an explicit flat ratio (per-frame turn rounding).</summary>
        public float ProfileXAt(int i, int count, float flat)
        {
            if (count <= 1) return 0f;
            float u = -1f + 2f * i / (count - 1);
            if (!IsHalfPipe) return u;

            float uFlat = flat * 0.5f; // the flat center gets half its proportional sample share
            float au = Mathf.Abs(u);

            float x = au <= uFlat
                ? flat * (au / Mathf.Max(0.0001f, uFlat))
                : flat + (1f - flat) * Mathf.Pow((au - uFlat) / Mathf.Max(0.0001f, 1f - uFlat), 0.6f);
            return Mathf.Sign(u) * Mathf.Min(x, 1f);
        }
    }
}
