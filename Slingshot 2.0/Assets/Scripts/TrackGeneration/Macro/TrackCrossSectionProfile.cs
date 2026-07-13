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

        [Tooltip("How high the sides rise above the center floor (meters). Per-frame SideHeight overrides this when set.")]
        public float SideHeight = 3.5f;

        [Tooltip("Curve power controlling how smoothly/aggressively the road curves upward (higher = flatter center, steeper sides).")]
        public float CurveStrength = 0.75f;

        [Tooltip("Maximum side wall tilt angle in degrees. Side height is clamped so the profile never exceeds this slope.")]
        public float WallAngle = 55f;

        [Tooltip("Ratio of the road width that stays flat in the center for stable driving.")]
        public float CenterFlatWidthRatio = 0.35f;

        [Tooltip("Number of cross-section sample points per SIDE (total profile points = 2*resolution + 1). Points are distributed toward the curved walls, where the resolution is actually visible.")]
        public int ProfileResolution = 32;

        [Tooltip("Height of the optional outer safety lip at the very top edge (meters). 0 = none.")]
        public float SafetyLipHeight = 1.0f;

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
        /// Curve exponent from CurveStrength: strength 0.35 → gentle bowl (power ~1.6),
        /// strength 1.5 → sharp late rise (power ~3.9). Power ≥ 1 keeps the center flat-ish.
        /// </summary>
        public float CurvePower => Mathf.Max(1.1f, 1f + CurveStrength * 2f);

        /// <summary>
        /// Height of the rideable side profile at a normalized cross position.
        /// x in [-1, 1] (0 = center). Center stays AT baseline; sides rise upward.
        /// Never returns a negative value — no hidden dips below the section baseline.
        /// </summary>
        public float HeightAt(float normalizedX, float halfWidth, float sideHeight)
        {
            if (!IsHalfPipe || sideHeight <= 0f) return 0f;

            float absX = Mathf.Abs(Mathf.Clamp(normalizedX, -1f, 1f));
            float flat = Mathf.Clamp01(CenterFlatWidthRatio);
            if (absX <= flat) return 0f;

            float t = (absX - flat) / Mathf.Max(0.0001f, 1f - flat);
            float h = Mathf.Pow(t, CurvePower) * ClampedSideHeight(halfWidth, sideHeight);
            return Mathf.Max(0f, h);
        }

        /// <summary>
        /// Side height clamped so the steepest profile slope never exceeds WallAngle.
        /// Max slope of h = H·t^p over the rising span W·(1-flat) is p·H / (W·(1-flat)).
        /// </summary>
        public float ClampedSideHeight(float halfWidth, float sideHeight)
        {
            float risingSpan = Mathf.Max(0.01f, halfWidth * (1f - Mathf.Clamp01(CenterFlatWidthRatio)));
            float maxBySlope = Mathf.Tan(Mathf.Clamp(WallAngle, 5f, 85f) * Mathf.Deg2Rad) * risingSpan / CurvePower;
            return Mathf.Min(sideHeight * ShapeDepthScale, maxBySlope);
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
        {
            if (count <= 1) return 0f;
            float u = -1f + 2f * i / (count - 1);
            if (!IsHalfPipe) return u;

            float flat = Mathf.Clamp01(CenterFlatWidthRatio);
            float uFlat = flat * 0.5f; // the flat center gets half its proportional sample share
            float au = Mathf.Abs(u);

            float x = au <= uFlat
                ? flat * (au / Mathf.Max(0.0001f, uFlat))
                : flat + (1f - flat) * Mathf.Pow((au - uFlat) / (1f - uFlat), 0.6f);
            return Mathf.Sign(u) * Mathf.Min(x, 1f);
        }
    }
}
