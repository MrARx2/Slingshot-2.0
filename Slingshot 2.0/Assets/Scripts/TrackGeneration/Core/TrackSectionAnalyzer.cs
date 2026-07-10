using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using TrackGeneration.Splines;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Classification of a contiguous stretch of road by horizontal curvature.
    /// Mirrors the piece types in the Track Tuning Context: straights are where
    /// ramps / boost pads / recovery live, curves are where banking and wall rides live.
    /// </summary>
    public enum SectionType
    {
        /// <summary>Near-zero curvature. Eligible for ramps, boost pads, recovery.</summary>
        Straight,

        /// <summary>Slight curvature. Safe for boost pads, NOT for ramps or wall rides.</summary>
        GentleCurve,

        /// <summary>Clear corner. Banked by the mesh; eligible for wall rides.</summary>
        MediumCurve,

        /// <summary>Hard corner / hairpin territory. Eligible for wall rides; nothing else.</summary>
        SharpCurve
    }

    /// <summary>
    /// A contiguous typed stretch of a spline, in both normalized-t and arc-length space.
    /// </summary>
    public struct TrackSection
    {
        public SectionType Type;

        /// <summary>Normalized spline parameter where the section begins.</summary>
        public float StartT;

        /// <summary>Normalized spline parameter where the section ends.</summary>
        public float EndT;

        /// <summary>Arc length (meters from spline start) where the section begins.</summary>
        public float StartArc;

        /// <summary>Arc length (meters from spline start) where the section ends.</summary>
        public float EndArc;

        /// <summary>Section length in meters.</summary>
        public float Length => EndArc - StartArc;

        /// <summary>Dominant turn direction: -1 = left, +1 = right, 0 = none/mixed.</summary>
        public int TurnSign;

        /// <summary>Peak absolute curvature (1/m) inside the section.</summary>
        public float MaxCurvature;

        public override string ToString()
            => $"[{Type}] {StartArc:F0}m → {EndArc:F0}m ({Length:F0}m, turn={TurnSign}, κmax={MaxCurvature:F4})";
    }

    /// <summary>
    /// Samples a spline at even arc-length intervals and classifies it into typed
    /// <see cref="TrackSection"/>s. This is the single source of truth for "what kind of
    /// road is here" — every placer (stunts, boost pads, gravity) should query this
    /// instead of rolling dice on arbitrary distance segments.
    /// </summary>
    public class TrackSectionAnalyzer
    {
        // Curvature thresholds (1/m). radius = 1/κ.
        // Straight:   R > 900 m   — effectively no steering input.
        // Gentle:     R 900-300 m — flowing curve, no commitment.
        // Medium:     R 300-120 m — a real corner, banked by the mesh.
        // Sharp:      R < 120 m   — hard corner / hairpin.
        private const float StraightMaxCurvature = 1f / 900f;
        private const float GentleMaxCurvature = 1f / 300f;
        private const float MediumMaxCurvature = 1f / 120f;

        // Sections shorter than this are absorbed into their larger neighbor to
        // avoid a jittery classification (a 6 m "straight" between two curves is noise).
        private const float MinSectionLength = 20f;

        private SplineFrame[] _frames;
        private float[] _curvature;      // signed κ per sample (+ = right turn)
        private float _totalLength;
        private bool _closed;

        public List<TrackSection> Sections { get; private set; } = new List<TrackSection>();
        public float TotalLength => _totalLength;

        /// <summary>
        /// Analyzes the spline. <paramref name="sampleSpacing"/> is the arc distance
        /// between curvature samples in meters.
        /// </summary>
        public void Analyze(SplineContainer container, float sampleSpacing = 5f)
        {
            Sections.Clear();
            _totalLength = SplineUtilities.GetSplineLength(container);
            if (_totalLength <= 1f) return;

            _closed = container.Spline.Closed;
            int count = math.max(16, Mathf.CeilToInt(_totalLength / math.max(sampleSpacing, 1f)));

            // No banking here — we want the raw horizontal geometry of the path.
            _frames = SplineUtilities.SampleEvenFrames(container, count);
            if (_frames == null || _frames.Length < 4) return;

            ComputeSignedCurvature();
            SmoothCurvature(passes: 3);
            BuildSections();
            MergeSmallSections();
        }

        // ──────────────────────────── Queries ────────────────────────────

        /// <summary>Returns the section containing arc position <paramref name="arc"/> (wrapped).</summary>
        public TrackSection GetSectionAtArc(float arc)
        {
            arc = WrapArc(arc);
            foreach (var s in Sections)
            {
                if (arc >= s.StartArc && arc < s.EndArc) return s;
            }
            return Sections.Count > 0 ? Sections[Sections.Count - 1] : default;
        }

        /// <summary>Peak absolute curvature (1/m) over an arc range (wrap-aware).</summary>
        public float MaxCurvatureInRange(float startArc, float endArc)
        {
            if (_frames == null || _frames.Length == 0) return 0f;
            float spacing = _totalLength / (_frames.Length - 1);
            float maxK = 0f;

            float span = endArc - startArc;
            int steps = math.max(1, (int)(span / math.max(spacing, 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float a = WrapArc(startArc + span * i / steps);
                int idx = math.clamp((int)math.round(a / spacing), 0, _curvature.Length - 1);
                maxK = math.max(maxK, math.abs(_curvature[idx]));
            }
            return maxK;
        }

        /// <summary>
        /// Total heading change in degrees over an arc range (wrap-aware).
        /// A ramp footprint should have near-zero heading change so the jump
        /// follows the flow of the road instead of firing the craft off the map.
        /// </summary>
        public float HeadingChangeDegrees(float startArc, float endArc)
        {
            float3 t0 = TangentAtArc(startArc);
            float3 t1 = TangentAtArc(endArc);
            float dot = math.clamp(math.dot(t0, t1), -1f, 1f);
            return math.degrees(math.acos(dot));
        }

        /// <summary>Vertical rise/drop in meters over an arc range (wrap-aware).</summary>
        public float ElevationChange(float startArc, float endArc)
        {
            return PositionAtArc(endArc).y - PositionAtArc(startArc).y;
        }

        /// <summary>Interpolated normalized t at arc position (wrap-aware).</summary>
        public float TAtArc(float arc)
        {
            arc = WrapArc(arc);
            float spacing = _totalLength / (_frames.Length - 1);
            float f = arc / spacing;
            int i0 = math.clamp((int)math.floor(f), 0, _frames.Length - 2);
            float frac = math.saturate(f - i0);
            return math.lerp(_frames[i0].T, _frames[i0 + 1].T, frac);
        }

        /// <summary>Interpolated position at arc position (wrap-aware).</summary>
        public float3 PositionAtArc(float arc)
        {
            arc = WrapArc(arc);
            float spacing = _totalLength / (_frames.Length - 1);
            float f = arc / spacing;
            int i0 = math.clamp((int)math.floor(f), 0, _frames.Length - 2);
            float frac = math.saturate(f - i0);
            return math.lerp(_frames[i0].Position, _frames[i0 + 1].Position, frac);
        }

        /// <summary>Interpolated tangent at arc position (wrap-aware).</summary>
        public float3 TangentAtArc(float arc)
        {
            arc = WrapArc(arc);
            float spacing = _totalLength / (_frames.Length - 1);
            float f = arc / spacing;
            int i0 = math.clamp((int)math.floor(f), 0, _frames.Length - 2);
            float frac = math.saturate(f - i0);
            return math.normalizesafe(math.lerp(_frames[i0].Tangent, _frames[i0 + 1].Tangent, frac), _frames[i0].Tangent);
        }

        private float WrapArc(float arc)
        {
            if (_totalLength <= 0f) return 0f;
            if (_closed)
            {
                arc %= _totalLength;
                if (arc < 0f) arc += _totalLength;
                return arc;
            }
            return math.clamp(arc, 0f, _totalLength);
        }

        // ──────────────────────────── Internals ────────────────────────────

        private void ComputeSignedCurvature()
        {
            int n = _frames.Length;
            _curvature = new float[n];
            float spacing = _totalLength / (n - 1);

            for (int i = 0; i < n; i++)
            {
                int prev, next;
                if (_closed)
                {
                    prev = (i - 1 + n) % n;
                    next = (i + 1) % n;
                }
                else
                {
                    prev = math.max(i - 1, 0);
                    next = math.min(i + 1, n - 1);
                }

                float arcDist = spacing * (_closed ? 2 : (next - prev));
                if (arcDist < 1e-4f) { _curvature[i] = 0f; continue; }

                // Signed horizontal curvature: project the tangent delta onto Right.
                // Vertical (pitch) changes contribute ~nothing, which is what we want —
                // a hill crest is not a corner.
                float3 delta = _frames[next].Tangent - _frames[prev].Tangent;
                _curvature[i] = math.dot(delta, _frames[i].Right) / arcDist;
            }
        }

        private void SmoothCurvature(int passes)
        {
            int n = _curvature.Length;
            float[] tmp = new float[n];
            for (int p = 0; p < passes; p++)
            {
                for (int i = 0; i < n; i++)
                {
                    int prev = _closed ? (i - 1 + n) % n : math.max(i - 1, 0);
                    int next = _closed ? (i + 1) % n : math.min(i + 1, n - 1);
                    tmp[i] = (_curvature[prev] + _curvature[i] * 2f + _curvature[next]) * 0.25f;
                }
                System.Array.Copy(tmp, _curvature, n);
            }
        }

        private static SectionType Classify(float absCurvature)
        {
            if (absCurvature < StraightMaxCurvature) return SectionType.Straight;
            if (absCurvature < GentleMaxCurvature) return SectionType.GentleCurve;
            if (absCurvature < MediumMaxCurvature) return SectionType.MediumCurve;
            return SectionType.SharpCurve;
        }

        private void BuildSections()
        {
            int n = _frames.Length;
            float spacing = _totalLength / (n - 1);

            int runStart = 0;
            SectionType runType = Classify(math.abs(_curvature[0]));

            for (int i = 1; i <= n; i++)
            {
                SectionType t = i < n ? Classify(math.abs(_curvature[i])) : runType;
                bool flush = (i == n) || (t != runType);
                if (!flush) continue;

                var section = new TrackSection
                {
                    Type = runType,
                    StartT = _frames[runStart].T,
                    EndT = _frames[math.min(i, n - 1)].T,
                    StartArc = runStart * spacing,
                    EndArc = math.min(i, n - 1) * spacing
                };

                float sum = 0f, maxAbs = 0f;
                for (int k = runStart; k < math.min(i, n); k++)
                {
                    sum += _curvature[k];
                    maxAbs = math.max(maxAbs, math.abs(_curvature[k]));
                }
                section.MaxCurvature = maxAbs;
                section.TurnSign = math.abs(sum) < 1e-5f ? 0 : (sum > 0f ? 1 : -1);

                Sections.Add(section);

                if (i < n)
                {
                    runStart = i;
                    runType = t;
                }
            }
        }

        private void MergeSmallSections()
        {
            // Absorb sub-minimum sections into the neighbor with the closer type,
            // repeating until stable. Keeps the classification readable and prevents
            // the placer from treating a 10 m blip as a valid stunt site.
            bool merged = true;
            while (merged && Sections.Count > 1)
            {
                merged = false;
                for (int i = 0; i < Sections.Count; i++)
                {
                    if (Sections[i].Length >= MinSectionLength) continue;

                    int target = i == 0 ? 1 : (i == Sections.Count - 1 ? i - 1 : (Sections[i - 1].Length >= Sections[i + 1].Length ? i - 1 : i + 1));

                    var absorbed = Sections[i];
                    var host = Sections[target];

                    host.StartArc = math.min(host.StartArc, absorbed.StartArc);
                    host.EndArc = math.max(host.EndArc, absorbed.EndArc);
                    host.StartT = math.min(host.StartT, absorbed.StartT);
                    host.EndT = math.max(host.EndT, absorbed.EndT);
                    host.MaxCurvature = math.max(host.MaxCurvature, absorbed.MaxCurvature);

                    Sections[target] = host;
                    Sections.RemoveAt(i);
                    merged = true;
                    break;
                }
            }
        }
    }
}
