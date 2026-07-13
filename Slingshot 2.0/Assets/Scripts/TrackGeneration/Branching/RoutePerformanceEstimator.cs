using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Branching
{
    /// <summary>
    /// Lightweight route-performance estimator: consistent enough to reject obviously
    /// dominant branch routes, deliberately NOT a simulation of the real craft.
    ///
    /// Model: per-frame curvature/pitch/roll demands set a local target speed from the
    /// archetype's capabilities; a forward acceleration pass and a backward braking pass
    /// produce the classic attainable speed profile, which integrates to a time.
    /// </summary>
    public static class RoutePerformanceEstimator
    {
        /// <summary>Estimated traversal time (seconds) of a frame sequence for one archetype.</summary>
        public static float EstimateTime(TrackConnectionFrame[] frames, ResolvedTrackGenerationConfig cfg,
            CraftArchetypeProfile archetype, float entrySpeedFraction = 1f)
        {
            if (frames == null || frames.Length < 3) return 0f;

            int n = frames.Length;
            var target = new float[n];

            float topSpeed = cfg.ReferenceTopSpeedMps * archetype.TopSpeed;
            float latAccel = cfg.ReferenceLateralAcceleration * archetype.Cornering;
            float accel = cfg.ReferenceAcceleration * archetype.Acceleration;
            float braking = cfg.ReferenceBraking * archetype.Braking;

            for (int i = 0; i < n; i++)
            {
                int prev = Mathf.Max(0, i - 1);
                int next = Mathf.Min(n - 1, i + 1);
                float ds = Mathf.Max(0.1f, frames[next].ArcLength - frames[prev].ArcLength);

                // Curvature from the heading change of the flattened forward.
                Vector3 f0 = frames[prev].Forward; f0.y = 0f;
                Vector3 f1 = frames[next].Forward; f1.y = 0f;
                float headingChange = Vector3.Angle(f0.sqrMagnitude > 1e-6f ? f0 : Vector3.forward,
                                                    f1.sqrMagnitude > 1e-6f ? f1 : Vector3.forward) * Mathf.Deg2Rad;
                float curvature = headingChange / ds;

                // Banking raises effective lateral capability (wall-riding support).
                float bank01 = Mathf.Clamp01(Mathf.Abs(frames[i].BankAngle) / 90f);
                float effLat = latAccel * (1f + bank01 * 1.2f);

                float v = topSpeed;
                if (curvature > 1e-5f)
                    v = Mathf.Min(v, Mathf.Sqrt(effLat / curvature));

                // Roll demand penalizes unstable archetypes.
                float rollRate = Mathf.Abs(Mathf.DeltaAngle(frames[prev].BankAngle, frames[next].BankAngle)) / ds;
                if (rollRate > 0.05f)
                {
                    float rollPenalty = Mathf.Clamp01(rollRate / 2f) * (1f - cfg.ReferenceRollStability * archetype.RollStability);
                    v *= 1f - Mathf.Clamp(rollPenalty, 0f, 0.35f);
                }

                // Pitch (climb) demand: heavy climbs bleed speed for weak retention.
                float pitch = Mathf.Abs(frames[i].PitchAngle);
                if (pitch > 8f)
                    v *= Mathf.Lerp(1f, 0.92f, Mathf.InverseLerp(8f, 40f, pitch)) * archetype.SpeedRetention;

                // Narrow roads demand precision — mild speed cost.
                float widthFactor = Mathf.Clamp01(frames[i].Width / Mathf.Max(1f, cfg.RoadWidth));
                v *= Mathf.Lerp(0.94f, 1f, widthFactor);

                target[i] = Mathf.Clamp(v, 20f, topSpeed);
            }

            // Forward acceleration pass.
            var speed = new float[n];
            speed[0] = Mathf.Min(target[0], topSpeed * Mathf.Clamp01(entrySpeedFraction));
            for (int i = 1; i < n; i++)
            {
                float ds = Mathf.Max(0.1f, frames[i].ArcLength - frames[i - 1].ArcLength);
                float reachable = Mathf.Sqrt(speed[i - 1] * speed[i - 1] + 2f * accel * ds);
                speed[i] = Mathf.Min(target[i], reachable);
            }

            // Backward braking pass.
            for (int i = n - 2; i >= 0; i--)
            {
                float ds = Mathf.Max(0.1f, frames[i + 1].ArcLength - frames[i].ArcLength);
                float allowed = Mathf.Sqrt(speed[i + 1] * speed[i + 1] + 2f * braking * ds);
                speed[i] = Mathf.Min(speed[i], allowed);
            }

            // Integrate time.
            float time = 0f;
            for (int i = 1; i < n; i++)
            {
                float ds = Mathf.Max(0.1f, frames[i].ArcLength - frames[i - 1].ArcLength);
                float v = Mathf.Max(10f, (speed[i] + speed[i - 1]) * 0.5f);
                time += ds / v;
            }

            return time;
        }

        /// <summary>Aggregated demand profile of a route (for specialization diagnostics).</summary>
        public static RouteDemandProfile MeasureDemand(TrackConnectionFrame[] frames, ResolvedTrackGenerationConfig cfg, float risk)
        {
            var demand = new RouteDemandProfile { Risk = risk };
            if (frames == null || frames.Length < 3) return demand;

            float fullThrottle = 0f, total = 0f;

            for (int i = 1; i < frames.Length - 1; i++)
            {
                float ds = Mathf.Max(0.1f, frames[i + 1].ArcLength - frames[i - 1].ArcLength);

                Vector3 f0 = frames[i - 1].Forward; f0.y = 0f;
                Vector3 f1 = frames[i + 1].Forward; f1.y = 0f;
                float curvature = Vector3.Angle(f0.sqrMagnitude > 1e-6f ? f0 : Vector3.forward,
                                                f1.sqrMagnitude > 1e-6f ? f1 : Vector3.forward) * Mathf.Deg2Rad / ds;

                demand.CorneringDemand += curvature * ds * 100f;
                demand.RollDemand += Mathf.Abs(Mathf.DeltaAngle(frames[i - 1].BankAngle, frames[i + 1].BankAngle)) / 360f;
                demand.PitchDemand += Mathf.Abs(frames[i].PitchAngle) * ds / 5000f;

                total += ds;
                if (curvature < 0.0005f) fullThrottle += ds;
            }

            demand.FullThrottleFraction = total > 1f ? fullThrottle / total : 0f;
            return demand;
        }

        /// <summary>
        /// Builds the per-archetype time table and evaluates the acceptance rules:
        /// neutral difference within tolerance, at least one archetype preferring each
        /// route, and neither route dominating every archetype.
        /// </summary>
        public static BranchBalanceMetrics Evaluate(int groupId, TrackConnectionFrame[] framesA, TrackConnectionFrame[] framesB,
            ResolvedTrackGenerationConfig cfg)
        {
            var metrics = new BranchBalanceMetrics { BranchGroupId = groupId };
            var archetypes = CraftArchetypeProfile.Defaults();

            int prefersA = 0, prefersB = 0;
            float neutralA = 0f, neutralB = 0f;

            foreach (var arch in archetypes)
            {
                float tA = EstimateTime(framesA, cfg, arch);
                float tB = EstimateTime(framesB, cfg, arch);

                metrics.TimeTable.Add(new RouteTimeEstimate
                {
                    Archetype = arch.Archetype,
                    RouteASeconds = tA,
                    RouteBSeconds = tB
                });

                if (arch.Archetype == CraftArchetype.Neutral) { neutralA = tA; neutralB = tB; }
                else if (tA < tB) prefersA++;
                else prefersB++;
            }

            float mean = Mathf.Max(0.5f, (neutralA + neutralB) * 0.5f);
            metrics.NeutralTimeDifference = Mathf.Abs(neutralA - neutralB) / mean;
            metrics.SpecializationValid = prefersA > 0 && prefersB > 0;

            return metrics;
        }
    }
}
