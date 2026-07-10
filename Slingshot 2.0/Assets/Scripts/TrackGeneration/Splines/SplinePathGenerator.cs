using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using TrackGeneration.Core;

namespace TrackGeneration.Splines
{
    /// <summary>
    /// Procedural generator that creates a closed-loop circuit spline from a
    /// <see cref="TrackConfig"/>. This is a plain C# class (not a MonoBehaviour).
    /// </summary>
    public class SplinePathGenerator
    {
        // Minimum ground clearance for any control point (meters).
        private const float MinGroundClearance = 5f;

        // Control-point spans converted into F1-style straights this generation.
        // (startIndex, span) pairs recorded so elevation can be linearized on them later.
        private readonly List<(int start, int span)> _straightSpans = new List<(int, int)>();

        // ──────────────────────────── Public API ────────────────────────────

        /// <summary>
        /// Generates the main closed-loop circuit spline, attaches it to
        /// <paramref name="parent"/> via a <see cref="SplineContainer"/>, and returns the container.
        /// </summary>
        /// <param name="parent">GameObject to receive the SplineContainer component.</param>
        /// <param name="config">Track configuration ScriptableObject.</param>
        /// <param name="rng">Seeded RNG struct (passed by ref for determinism).</param>
        /// <returns>The SplineContainer holding the generated circuit spline.</returns>
        public SplineContainer GenerateMainCircuit(GameObject parent, TrackConfig config, Unity.Mathematics.Random rng)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // Step 1: Generate base distorted-ellipse shape.
            List<float3> points = GenerateBaseShape(config, ref rng);

            // Step 1b: Convert some spans into true F1-style straights.
            CreateStraightSections(points, config, ref rng);

            // Step 2: Apply layered elevation noise.
            ApplyElevation(points, config, ref rng);

            // Step 3: Enforce maximum slope angle.
            EnforceSlopeLimit(points, config);

            // Step 4: Smooth elevation for natural rolling hills.
            SmoothElevation(points, config);

            // Step 4b: Straights get a constant gradient — no bumps mid-straight, so they
            // read as clean flat-out sections and are safe homes for ramps / boost pads.
            LinearizeStraightElevation(points);

            // Step 3d: Enforce vertical clearance at intersections.
            EnforceClearance(points, config);

            // Step 4-5: Build spline and attach to container.
            SplineContainer container = parent.GetComponent<SplineContainer>();
            if (container == null)
                container = parent.AddComponent<SplineContainer>();

            Spline spline = BuildSpline(points, config);

            // Step 6: Smoothed and closed
            SmoothAndClose(spline);

            container.Spline = spline;

            return container;
        }

        // ──────────────────────────── Step 1: Base Shape ────────────────────────────

        /// <summary>
        /// Generates control points on a randomly distorted ellipse in the XZ plane.
        /// </summary>
        private List<float3> GenerateBaseShape(TrackConfig config, ref Unity.Mathematics.Random rng)
        {
            int count = math.max(config.ControlPointCount, 4);
            float baseRadius = config.TrackRadius;
            var points = new List<float3>(count);

            float[] radii = new float[count];
            for (int i = 0; i < count; i++)
            {
                // Perturb radius for organic track shape. Range kept moderate (0.7-1.3):
                // wilder perturbation created snaking micro-corners that fought the
                // hovercraft's flow; large-scale character now comes from the straight
                // sections and the banked corners between them.
                radii[i] = baseRadius * rng.NextFloat(0.7f, 1.3f);
            }

            // Smooth radii to prevent extreme sharp V-corners (Laplacian smoothing)
            float[] smoothedRadii = new float[count];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    int prev = (i - 1 + count) % count;
                    int next = (i + 1) % count;
                    smoothedRadii[i] = (radii[prev] + radii[i] * 2f + radii[next]) * 0.25f;
                }
                System.Array.Copy(smoothedRadii, radii, count);
            }

            for (int i = 0; i < count; i++)
            {
                float angle = (2f * math.PI * i) / count;
                float x = math.cos(angle) * radii[i];
                float z = math.sin(angle) * radii[i];
                points.Add(new float3(x, 0f, z));
            }

            return points;
        }

        // ──────────────────────────── Step 1b: F1 Straights ────────────────────────────

        /// <summary>
        /// Converts randomly chosen spans of consecutive control points into collinear
        /// runs, producing genuine F1-style straights instead of one continuous snake.
        /// Spans never overlap and keep a 1-point buffer between them so the corners
        /// connecting straight → curve stay smooth. Recorded spans are used later to
        /// linearize elevation along each straight.
        /// </summary>
        private void CreateStraightSections(List<float3> points, TrackConfig config, ref Unity.Mathematics.Random rng)
        {
            _straightSpans.Clear();

            int count = points.Count;
            int numStraights = rng.NextInt(config.MinStraightSections, config.MaxStraightSections + 1);
            if (numStraights <= 0 || count < 8) return;

            bool[] reserved = new bool[count];
            int placed = 0;
            int attempts = 0;

            while (placed < numStraights && attempts++ < 60)
            {
                // Span = number of SEGMENTS made collinear (span+1 points). 2-3 segments
                // over a ~300 m radius loop gives roughly 90-200 m straights.
                int span = rng.NextInt(2, 4);
                int start = rng.NextInt(0, count);

                // The span plus a 1-point buffer on each side must be untouched.
                bool free = true;
                for (int o = -1; o <= span + 1 && free; o++)
                {
                    if (reserved[(start + o + count) % count]) free = false;
                }
                if (!free) continue;

                float3 a = points[start];
                float3 b = points[(start + span) % count];

                // Collinearize the interior points in XZ (Y handled after elevation).
                for (int o = 1; o < span; o++)
                {
                    int idx = (start + o) % count;
                    float f = (float)o / span;
                    float3 p = math.lerp(a, b, f);
                    points[idx] = new float3(p.x, points[idx].y, p.z);
                }

                for (int o = -1; o <= span + 1; o++)
                {
                    reserved[(start + o + count) % count] = true;
                }

                _straightSpans.Add((start, span));
                placed++;
            }
        }

        /// <summary>
        /// Re-linearizes elevation along each recorded straight span so a straight has one
        /// constant gradient end-to-end. Runs AFTER noise/slope/smoothing so the straight's
        /// endpoints stay consistent with the surrounding terrain.
        /// </summary>
        private void LinearizeStraightElevation(List<float3> points)
        {
            int count = points.Count;
            foreach (var (start, span) in _straightSpans)
            {
                float yA = points[start].y;
                float yB = points[(start + span) % count].y;

                for (int o = 1; o < span; o++)
                {
                    int idx = (start + o) % count;
                    float3 p = points[idx];
                    p.y = math.max(math.lerp(yA, yB, (float)o / span), MinGroundClearance);
                    points[idx] = p;
                }
            }
        }

        // ──────────────────────────── Step 2: Elevation ────────────────────────────

        /// <summary>
        /// Applies layered Perlin-like noise to the Y coordinates of all control points.
        /// Uses three frequency layers (low, mid, high) for natural terrain variation.
        /// </summary>
        private void ApplyElevation(List<float3> points, TrackConfig config, ref Unity.Mathematics.Random rng)
        {
            float maxElev = config.MaxElevationChange;

            // Seeded offsets for mid and high frequency layers.
            float2 offset1 = rng.NextFloat2() * 1000f;
            float2 offset2 = rng.NextFloat2() * 1000f;

            for (int i = 0; i < points.Count; i++)
            {
                float3 p = points[i];
                float2 xz = new float2(p.x, p.z);

                // Low-frequency layer: broad terrain undulation.
                float yLow = noise.cnoise(xz * 0.005f) * maxElev * 0.6f;

                // Mid-frequency layer: moderate hills.
                float yMid = noise.cnoise(xz * 0.02f + offset1) * maxElev * 0.3f;

                // High-frequency layer: fine detail.
                float yHigh = noise.cnoise(xz * 0.08f + offset2) * maxElev * 0.1f;

                p.y = yLow + yMid + yHigh;

                // Enforce minimum ground clearance.
                p.y = math.max(p.y, MinGroundClearance);

                points[i] = p;
            }
        }

        // ──────────────────────────── Step 3b: Slope Enforcement ────────────────────────────

        /// <summary>
        /// Flattens sections of the track that exceed the maximum allowed slope angle.
        /// </summary>
        private void EnforceSlopeLimit(List<float3> points, TrackConfig config)
        {
            float maxSlopeRad = math.radians(config.MaxSlopeAngle);
            
            // Forward and backward passes to propagate corrections
            for (int pass = 0; pass < 2; pass++)
            {
                // Forward pass
                for (int i = 0; i < points.Count; i++)
                {
                    int nextIdx = (i + 1) % points.Count;

                    float3 current = points[i];
                    float3 next = points[nextIdx];

                    float2 xzCurrent = new float2(current.x, current.z);
                    float2 xzNext = new float2(next.x, next.z);
                    float distXZ = math.distance(xzCurrent, xzNext);
                    
                    if (distXZ < 0.001f) continue;

                    float yDelta = next.y - current.y;
                    float currentSlope = math.atan(math.abs(yDelta) / distXZ);

                    if (currentSlope > maxSlopeRad)
                    {
                        float allowedYDelta = math.tan(maxSlopeRad) * distXZ;
                        next.y = current.y + (yDelta > 0 ? allowedYDelta : -allowedYDelta);
                        next.y = math.max(next.y, MinGroundClearance);
                        points[nextIdx] = next;
                    }
                }
                
                // Backward pass
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    int prevIdx = (i - 1 + points.Count) % points.Count;

                    float3 current = points[i];
                    float3 prev = points[prevIdx];

                    float2 xzCurrent = new float2(current.x, current.z);
                    float2 xzPrev = new float2(prev.x, prev.z);
                    float distXZ = math.distance(xzCurrent, xzPrev);
                    
                    if (distXZ < 0.001f) continue;

                    float yDelta = prev.y - current.y;
                    float currentSlope = math.atan(math.abs(yDelta) / distXZ);

                    if (currentSlope > maxSlopeRad)
                    {
                        float allowedYDelta = math.tan(maxSlopeRad) * distXZ;
                        prev.y = current.y + (yDelta > 0 ? allowedYDelta : -allowedYDelta);
                        prev.y = math.max(prev.y, MinGroundClearance);
                        points[prevIdx] = prev;
                    }
                }
            }
        }

        // ──────────────────────────── Step 3c: Elevation Smoothing ────────────────────────────

        /// <summary>
        /// Applies Laplacian smoothing to the Y coordinates for gentler hills.
        /// </summary>
        private void SmoothElevation(List<float3> points, TrackConfig config)
        {
            int passes = config.ElevationSmoothingPasses;
            if (passes <= 0) return;

            float[] tempY = new float[points.Count];

            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    int prevIdx = (i - 1 + points.Count) % points.Count;
                    int nextIdx = (i + 1) % points.Count;

                    float prevY = points[prevIdx].y;
                    float currentY = points[i].y;
                    float nextY = points[nextIdx].y;

                    // Laplacian smoothing
                    tempY[i] = (prevY + currentY * 2f + nextY) * 0.25f;
                    tempY[i] = math.max(tempY[i], MinGroundClearance);
                }

                for (int i = 0; i < points.Count; i++)
                {
                    float3 p = points[i];
                    p.y = tempY[i];
                    points[i] = p;
                }
            }
        }

        // ──────────────────────────── Step 3d: Enforce Clearance ────────────────────────────

        /// <summary>
        /// Detects points that cross each other in XZ space and forces a minimum Y separation
        /// to create proper overpasses instead of self-intersections.
        /// </summary>
        private void EnforceClearance(List<float3> points, TrackConfig config)
        {
            float minClearance = 15f;
            float checkDistSq = math.pow(config.MainRoadWidth * 2f, 2);

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 5; j < points.Count; j++)
                {
                    // If points are on the opposite side of the closed loop, they aren't physically "adjacent" along the track
                    // but they could be crossing. (i+5 ensures we don't check immediate neighbors).
                    if (points.Count - (j - i) < 5) continue;

                    float3 p1 = points[i];
                    float3 p2 = points[j];

                    float2 xz1 = new float2(p1.x, p1.z);
                    float2 xz2 = new float2(p2.x, p2.z);

                    if (math.distancesq(xz1, xz2) < checkDistSq)
                    {
                        float yDiff = math.abs(p1.y - p2.y);
                        if (yDiff < minClearance)
                        {
                            float pushAmount = (minClearance - yDiff) * 0.5f + 1f;
                            
                            // Apply smoothed push to prevent sharp vertical spikes
                            ApplySmoothedElevation(points, i, p1.y > p2.y ? pushAmount : -pushAmount);
                            ApplySmoothedElevation(points, j, p2.y > p1.y ? pushAmount : -pushAmount);
                        }
                    }
                }
            }
        }

        private void ApplySmoothedElevation(List<float3> points, int centerIndex, float amount)
        {
            int spread = 2; // Affects center +/- 2 points
            for (int offset = -spread; offset <= spread; offset++)
            {
                int idx = (centerIndex + offset + points.Count) % points.Count;
                
                // Use smoothstep to create perfectly rounded peaks instead of sharp triangle waves
                float distanceFrac = math.abs(offset) / (float)(spread + 1);
                float weight = math.smoothstep(1f, 0f, distanceFrac);
                
                float3 p = points[idx];
                p.y += amount * weight;
                p.y = math.max(p.y, MinGroundClearance); // Keep above ground
                points[idx] = p;
            }
        }

        // ──────────────────────────── Step 4: Build Spline ────────────────────────────

        /// <summary>
        /// Converts a list of control points into a closed <see cref="Spline"/> with
        /// Catmull-Rom style tangents.
        /// </summary>
        private Spline BuildSpline(List<float3> points, TrackConfig config)
        {
            int count = points.Count;
            if (count < 2)
            {
                Debug.LogWarning("[SplinePathGenerator] Too few points to build spline.");
                var fallback = new Spline();
                fallback.Closed = true;
                return fallback;
            }

            float tension = math.clamp(config.CurveSmoothingTension, 0.01f, 1f);
            var spline = new Spline(count);

            for (int i = 0; i < count; i++)
            {
                // Previous and next indices with wrap-around for closed spline.
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;

                float3 pPrev = points[prev];
                float3 pCurr = points[i];
                float3 pNext = points[next];

                // Catmull-Rom tangent direction.
                float3 tangentDir = pNext - pPrev;
                float tangentLen = math.length(tangentDir);
                float3 tangentNorm = tangentLen > 1e-6f
                    ? tangentDir / tangentLen
                    : new float3(0f, 0f, 1f);

                // Segment length determines tangent magnitude.
                float segLen = math.length(pNext - pCurr);
                float tangentMag = segLen * tension;
                float3 tangent = tangentNorm * tangentMag;

                var knot = new BezierKnot(
                    position: pCurr,
                    tangentIn: -tangent,
                    tangentOut: tangent,
                    rotation: quaternion.identity
                );

                spline.Add(knot);
            }

            spline.Closed = true;
            return spline;
        }



        // ──────────────────────────── Step 6: Smooth & Close ────────────────────────────

        /// <summary>
        /// Ensures the first and last knots of the closed spline connect smoothly
        /// with averaged tangents for C1 continuity at the closure point.
        /// </summary>
        private void SmoothAndClose(Spline spline)
        {
            int count = spline.Count;
            if (count < 3) return;

            // For a closed spline, ensure the tangents at the junction (first/last knots)
            // are consistent with their neighbors across the wrap boundary.

            // Average tangent at knot 0 (neighbors: last and 1).
            {
                BezierKnot first = spline[0];
                BezierKnot last = spline[count - 1];
                BezierKnot second = spline[1];

                float3 tangentFromNeighbors = math.normalizesafe(
                    (float3)second.Position - (float3)last.Position,
                    new float3(0f, 0f, 1f)
                );

                // Existing tangent direction.
                float3 existingTanOut = math.normalizesafe(
                    (float3)first.TangentOut,
                    tangentFromNeighbors
                );

                // Average the two directions.
                float3 blended = math.normalizesafe(
                    existingTanOut + tangentFromNeighbors,
                    tangentFromNeighbors
                );

                // Preserve original tangent magnitude.
                float mag = math.length((float3)first.TangentOut);
                mag = math.max(mag, 1f);

                BezierKnot smoothedFirst = first;
                smoothedFirst.TangentOut = blended * mag;
                smoothedFirst.TangentIn = -blended * mag;
                spline[0] = smoothedFirst;
            }

            // Average tangent at last knot (neighbors: second-to-last and 0).
            {
                BezierKnot last = spline[count - 1];
                BezierKnot prev = spline[count - 2];
                BezierKnot first = spline[0];

                float3 tangentFromNeighbors = math.normalizesafe(
                    (float3)first.Position - (float3)prev.Position,
                    new float3(0f, 0f, 1f)
                );

                float3 existingTanOut = math.normalizesafe(
                    (float3)last.TangentOut,
                    tangentFromNeighbors
                );

                float3 blended = math.normalizesafe(
                    existingTanOut + tangentFromNeighbors,
                    tangentFromNeighbors
                );

                float mag = math.length((float3)last.TangentOut);
                mag = math.max(mag, 1f);

                BezierKnot smoothedLast = last;
                smoothedLast.TangentOut = blended * mag;
                smoothedLast.TangentIn = -blended * mag;
                spline[count - 1] = smoothedLast;
            }
        }
    }
}
