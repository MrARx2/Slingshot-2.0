using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Planning
{
    /// <summary>Virtual craft archetypes used by the route-time estimator.</summary>
    public enum CraftArchetype
    {
        Neutral,
        Velocity,
        Grip,
        Acceleration,
        Stability,
        AirControl
    }

    /// <summary>
    /// Relative capabilities of one virtual craft archetype. These are ESTIMATION
    /// values for dual-quarter route balancing only — they never touch the real craft
    /// physics. All values are multipliers on the rulebook's reference performance model.
    /// </summary>
    [Serializable]
    public class CraftArchetypeProfile
    {
        public CraftArchetype Archetype;
        public float TopSpeed = 1f;
        public float Acceleration = 1f;
        public float Braking = 1f;
        public float Cornering = 1f;
        public float SpeedRetention = 1f;
        public float RollStability = 1f;
        public float PitchStability = 1f;
        public float LandingRecovery = 1f;
        public float AirControl = 1f;

        /// <summary>The default archetype set. Deterministic, code-defined.</summary>
        public static CraftArchetypeProfile[] Defaults() => new[]
        {
            new CraftArchetypeProfile { Archetype = CraftArchetype.Neutral },
            new CraftArchetypeProfile
            {
                Archetype = CraftArchetype.Velocity,
                TopSpeed = 1.08f, Acceleration = 0.92f, Braking = 0.95f,
                Cornering = 0.92f, SpeedRetention = 1.05f, RollStability = 0.95f,
                PitchStability = 0.95f, LandingRecovery = 0.95f, AirControl = 0.9f
            },
            new CraftArchetypeProfile
            {
                Archetype = CraftArchetype.Grip,
                TopSpeed = 0.95f, Acceleration = 1f, Braking = 1.05f,
                Cornering = 1.12f, SpeedRetention = 1f, RollStability = 1.05f,
                PitchStability = 1f, LandingRecovery = 1f, AirControl = 0.95f
            },
            new CraftArchetypeProfile
            {
                Archetype = CraftArchetype.Acceleration,
                TopSpeed = 0.96f, Acceleration = 1.15f, Braking = 1.05f,
                Cornering = 1f, SpeedRetention = 0.95f, RollStability = 1f,
                PitchStability = 1f, LandingRecovery = 1.05f, AirControl = 1f
            },
            new CraftArchetypeProfile
            {
                Archetype = CraftArchetype.Stability,
                TopSpeed = 0.97f, Acceleration = 0.98f, Braking = 1f,
                Cornering = 1.02f, SpeedRetention = 1.02f, RollStability = 1.15f,
                PitchStability = 1.12f, LandingRecovery = 1.05f, AirControl = 1f
            },
            new CraftArchetypeProfile
            {
                Archetype = CraftArchetype.AirControl,
                TopSpeed = 0.97f, Acceleration = 1f, Braking = 1f,
                Cornering = 0.98f, SpeedRetention = 1f, RollStability = 1.05f,
                PitchStability = 1.05f, LandingRecovery = 1.12f, AirControl = 1.2f
            }
        };
    }

    /// <summary>Aggregated demands one route places on a craft (specialization diagnostics).</summary>
    [Serializable]
    public class RouteDemandProfile
    {
        [Tooltip("Integrated cornering demand (higher = tighter/longer corners).")]
        public float CorneringDemand;

        [Tooltip("Integrated roll-rate demand (corkscrew twists, banked weaves).")]
        public float RollDemand;

        [Tooltip("Integrated pitch/elevation demand.")]
        public float PitchDemand;

        [Tooltip("Airtime demand from jumps/gaps.")]
        public float AirDemand;

        [Tooltip("Fraction of the route spent at full throttle.")]
        public float FullThrottleFraction;

        [Tooltip("Risk character 0..1.")]
        public float Risk;
    }

    /// <summary>One archetype row of a dual quarter's route time table.</summary>
    [Serializable]
    public class QuarterRouteTimeRow
    {
        public CraftArchetype Archetype;
        public float RouteASeconds;
        public float RouteBSeconds;

        /// <summary>Positive = road A is faster for this archetype.</summary>
        public float AdvantageASeconds => RouteBSeconds - RouteASeconds;
    }

    /// <summary>
    /// Balance verdict for one Dual Road Quarter, measured by predicted traversal time
    /// across craft archetypes (production doc §10). Validation/reporting only — no
    /// iterative geometry repair; failures are handled by the configured
    /// DualQuarterBalancePolicy (demote to Single Road, or reject the candidate).
    /// </summary>
    [Serializable]
    public class QuarterRouteBalance
    {
        public int QuarterIndex;

        [Tooltip("Relative neutral-craft time difference between the roads, in percent of the mean.")]
        public float NeutralTimeDifferencePercent;

        public bool NeutralWithinTolerance;

        [Tooltip("At least one archetype predicts a faster traversal through road A.")]
        public bool RouteAPreferredBySomeArchetype;

        [Tooltip("At least one archetype predicts a faster traversal through road B.")]
        public bool RouteBPreferredBySomeArchetype;

        public List<QuarterRouteTimeRow> TimeTable = new List<QuarterRouteTimeRow>();

        /// <summary>
        /// Doc §10 acceptance: neutral times close, and (when differentiation is
        /// required) each road preferred by at least one archetype — which together
        /// imply neither road dominates every archetype.
        /// </summary>
        public bool Acceptable(bool requireDifferentiation) => NeutralWithinTolerance
            && (!requireDifferentiation || (RouteAPreferredBySomeArchetype && RouteBPreferredBySomeArchetype));

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Quarter {QuarterIndex}: neutral Δ {NeutralTimeDifferencePercent:F1}%, " +
                          $"differentiation {(RouteAPreferredBySomeArchetype && RouteBPreferredBySomeArchetype ? "OK" : "WEAK")}");
            foreach (var t in TimeTable)
                sb.AppendLine($"  {t.Archetype,-12} A {t.RouteASeconds:F2}s  B {t.RouteBSeconds:F2}s  ({(t.AdvantageASeconds >= 0 ? "A" : "B")} +{Mathf.Abs(t.AdvantageASeconds):F2}s)");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Lightweight route-time estimator: consistent enough to reject obviously dominant
    /// dual-quarter roads and to estimate the neutral lap time, deliberately NOT a
    /// simulation of the real craft.
    ///
    /// Model: per-frame curvature/pitch/roll demands set a local target speed from the
    /// archetype's capabilities; a forward acceleration pass and a backward braking pass
    /// produce the classic attainable speed profile, which integrates to a time.
    /// </summary>
    public static class RouteTimeEstimator
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
        /// Builds the per-archetype time table for a dual quarter's two roads and
        /// evaluates the acceptance rules: neutral difference within tolerance and, when
        /// required, at least one archetype preferring each road.
        /// </summary>
        public static QuarterRouteBalance Evaluate(int quarterIndex, TrackConnectionFrame[] framesA,
            TrackConnectionFrame[] framesB, ResolvedTrackGenerationConfig cfg)
        {
            var metrics = new QuarterRouteBalance { QuarterIndex = quarterIndex };
            var archetypes = CraftArchetypeProfile.Defaults();

            int prefersA = 0, prefersB = 0;
            float neutralA = 0f, neutralB = 0f;

            foreach (var arch in archetypes)
            {
                float tA = EstimateTime(framesA, cfg, arch);
                float tB = EstimateTime(framesB, cfg, arch);

                metrics.TimeTable.Add(new QuarterRouteTimeRow
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
            metrics.NeutralTimeDifferencePercent = Mathf.Abs(neutralA - neutralB) / mean * 100f;
            metrics.NeutralWithinTolerance = metrics.NeutralTimeDifferencePercent <= cfg.NeutralTimeTolerance * 100f;
            metrics.RouteAPreferredBySomeArchetype = prefersA > 0;
            metrics.RouteBPreferredBySomeArchetype = prefersB > 0;

            return metrics;
        }
    }
}
