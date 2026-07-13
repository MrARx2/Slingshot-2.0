using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Macro;

namespace TrackGeneration.Branching
{
    /// <summary>Virtual craft archetypes used by the route-performance estimator.</summary>
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
    /// values for branch balancing only — they never touch the real craft physics.
    /// All values are multipliers on the rulebook's reference performance model.
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

    /// <summary>Aggregated demands one route places on a craft (used for specialization checks).</summary>
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

        [Tooltip("Risk character 0..1 (from the route settings).")]
        public float Risk;
    }

    /// <summary>Per-archetype estimated route time for one route of a branch group.</summary>
    [Serializable]
    public class RouteTimeEstimate
    {
        public CraftArchetype Archetype;
        public float RouteASeconds;
        public float RouteBSeconds;

        /// <summary>Positive = Route A faster (advantage A), negative = Route B faster.</summary>
        public float AdvantageA => RouteBSeconds - RouteASeconds;
    }

    /// <summary>Balance verdict + the estimated time table for one branch group.</summary>
    [Serializable]
    public class BranchBalanceMetrics
    {
        public int BranchGroupId;

        [Tooltip("Neutral-archetype time difference as a fraction of route time.")]
        public float NeutralTimeDifference;

        [Tooltip("True when at least one archetype prefers each route and neither dominates all archetypes.")]
        public bool SpecializationValid;

        public List<RouteTimeEstimate> TimeTable = new List<RouteTimeEstimate>();

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Branch group {BranchGroupId}: neutral Δ {NeutralTimeDifference:P1}, specialization {(SpecializationValid ? "OK" : "WEAK")}");
            foreach (var t in TimeTable)
                sb.AppendLine($"  {t.Archetype,-12} A {t.RouteASeconds:F2}s  B {t.RouteBSeconds:F2}s  ({(t.AdvantageA >= 0 ? "A" : "B")} +{Mathf.Abs(t.AdvantageA):F2}s)");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// One generated route of a branch group: its own section sequence, physical
    /// length (independent of logical lap progress) and demand profile.
    /// </summary>
    [Serializable]
    public class GeneratedRoute
    {
        public int RouteId;
        public string DisplayName;

        [Tooltip("Style this route was initialized from.")]
        public BranchRouteStyle Style;

        [NonSerialized] public List<GeneratedTrackSection> Sections = new List<GeneratedTrackSection>();

        [Tooltip("Actual physical distance along this route (meters). May differ from the sibling route.")]
        public float PhysicalLength;

        [Tooltip("Estimated neutral-archetype traversal time (seconds).")]
        public float EstimatedNeutralTime;

        public RouteDemandProfile Demand = new RouteDemandProfile();
    }

    /// <summary>
    /// One branch group: a temporary two-route race between a shared entry gate and
    /// a shared merge gate. The authoritative layout keeps this relationship; the
    /// flattened section list is only a rendering convenience.
    /// </summary>
    [Serializable]
    public class GeneratedBranchGroup
    {
        public int BranchGroupId;

        [Tooltip("Shared frame where the routes fork.")]
        public TrackConnectionFrame EntryGate;

        [Tooltip("Shared frame where the routes rejoin.")]
        public TrackConnectionFrame MergeGate;

        public GeneratedRoute RouteA = new GeneratedRoute();
        public GeneratedRoute RouteB = new GeneratedRoute();

        public BranchPairingMode PairingMode;
        public BranchInteractionPattern InteractionPattern;

        public BranchBalanceMetrics Balance = new BranchBalanceMetrics();

        [Tooltip("Logical lap progress at the entry gate (0..1).")]
        public float EntryLapProgress;

        [Tooltip("Logical lap progress at the merge gate (0..1).")]
        public float MergeLapProgress;
    }
}
