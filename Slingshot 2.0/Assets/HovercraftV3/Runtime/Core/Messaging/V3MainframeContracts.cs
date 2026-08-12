using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3MainframeBootState
    {
        Uninitialized,
        Discovering,
        Validating,
        BootingSystems,
        Ready,
        Degraded,
        Faulted
    }

    public enum V3ObservationCategory
    {
        ChassisState,
        EnvironmentSurface,
        EnvironmentAtmosphere,
        DeviceState,
        PowerState,
        ThermalState,
        TopologyState,
        SystemHealth
    }

    public enum V3ControlDomain
    {
        Propulsion,
        Braking,
        Steering,
        Strafe,
        RideHeight,
        AttitudeStability,
        Traction,
        Aerodynamics,
        Recovery,
        ManualPilot
    }

    public enum V3SystemRequestType
    {
        Force,
        Torque,
        Angle,
        State
    }

    public enum V3RequestFrame
    {
        World,
        ChassisLocal,
        DeviceLocal
    }

    public readonly struct V3Observation
    {
        public V3Observation(
            V3ObservationCategory category,
            string sourceId,
            double timestamp,
            Vector3 primaryVector,
            Vector3 secondaryVector,
            float primaryScalar,
            float secondaryScalar,
            float confidence,
            bool state)
        {
            Category = category;
            SourceId = sourceId ?? string.Empty;
            Timestamp = timestamp;
            PrimaryVector = primaryVector;
            SecondaryVector = secondaryVector;
            PrimaryScalar = primaryScalar;
            SecondaryScalar = secondaryScalar;
            Confidence = Mathf.Clamp01(confidence);
            State = state;
        }

        public V3ObservationCategory Category { get; }
        public string SourceId { get; }
        public double Timestamp { get; }
        public Vector3 PrimaryVector { get; }
        public Vector3 SecondaryVector { get; }
        public float PrimaryScalar { get; }
        public float SecondaryScalar { get; }
        public float Confidence { get; }
        public bool State { get; }
        public double AgeSeconds(double now) =>
            Math.Max(0d, now - Timestamp);
    }

    public readonly struct V3PilotIntent
    {
        public V3PilotIntent(V3PilotCommand command, double timestamp)
        {
            Command = command;
            Timestamp = timestamp;
        }

        public V3PilotCommand Command { get; }
        public double Timestamp { get; }
    }

    public readonly struct V3SystemRequest
    {
        public V3SystemRequest(
            string requestId,
            string sourceSystemId,
            V3ControlDomain domain,
            V3SystemRequestType requestType,
            V3RequestFrame frame,
            Vector3 requestedForce,
            Vector3 requestedTorque,
            float requestedAngleDegrees,
            float requestedState,
            PartCapability eligibleDevices,
            float confidence,
            float authority,
            float maximumContribution,
            int priority,
            double timestamp,
            float validitySeconds,
            string reason)
        {
            RequestId = requestId ?? string.Empty;
            SourceSystemId = sourceSystemId ?? string.Empty;
            Domain = domain;
            RequestType = requestType;
            Frame = frame;
            RequestedForce = requestedForce;
            RequestedTorque = requestedTorque;
            RequestedAngleDegrees = requestedAngleDegrees;
            RequestedState = requestedState;
            EligibleDevices = eligibleDevices;
            Confidence = Mathf.Clamp01(confidence);
            Authority = Mathf.Clamp01(authority);
            MaximumContribution = Mathf.Max(0f, maximumContribution);
            Priority = priority;
            Timestamp = timestamp;
            ValiditySeconds = Mathf.Max(0f, validitySeconds);
            Reason = reason ?? string.Empty;
        }

        public string RequestId { get; }
        public string SourceSystemId { get; }
        public V3ControlDomain Domain { get; }
        public V3SystemRequestType RequestType { get; }
        public V3RequestFrame Frame { get; }
        public Vector3 RequestedForce { get; }
        public Vector3 RequestedTorque { get; }
        public float RequestedAngleDegrees { get; }
        public float RequestedState { get; }
        public PartCapability EligibleDevices { get; }
        public float Confidence { get; }
        public float Authority { get; }
        public float MaximumContribution { get; }
        public int Priority { get; }
        public double Timestamp { get; }
        public float ValiditySeconds { get; }
        public string Reason { get; }
        public bool IsValid(double now) =>
            now - Timestamp <= ValiditySeconds;
    }

    public sealed class V3ObservationBus
    {
        private readonly Dictionary<string, V3Observation> latest =
            new Dictionary<string, V3Observation>(StringComparer.Ordinal);
        private readonly List<V3Observation> snapshot =
            new List<V3Observation>(32);

        public IReadOnlyList<V3Observation> Snapshot => snapshot;

        public void Publish(V3Observation observation)
        {
            string key = ((int)observation.Category).ToString() +
                ":" + observation.SourceId;
            latest[key] = observation;
            RebuildSnapshot();
        }

        public bool TryGetLatest(
            V3ObservationCategory category,
            string sourceId,
            out V3Observation observation)
        {
            string key = ((int)category).ToString() +
                ":" + (sourceId ?? string.Empty);
            return latest.TryGetValue(key, out observation);
        }

        public void Clear()
        {
            latest.Clear();
            snapshot.Clear();
        }

        private void RebuildSnapshot()
        {
            snapshot.Clear();
            foreach (V3Observation observation in latest.Values)
            {
                snapshot.Add(observation);
            }
        }
    }

    public sealed class V3IntentBus
    {
        public bool HasPilotIntent { get; private set; }
        public V3PilotIntent LatestPilotIntent { get; private set; }

        public void PublishPilotIntent(
            V3PilotCommand command,
            double timestamp)
        {
            LatestPilotIntent = new V3PilotIntent(command, timestamp);
            HasPilotIntent = true;
        }

        public void Clear()
        {
            HasPilotIntent = false;
            LatestPilotIntent = default;
        }
    }
}
