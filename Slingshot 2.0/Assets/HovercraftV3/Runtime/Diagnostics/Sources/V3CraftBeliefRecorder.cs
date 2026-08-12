using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public sealed class V3CraftBeliefRecorder : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;
        private V3HoverController hover;
        private V3TractionController traction;

        public string SourceId => "belief";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
            hover = value != null && value.Craft != null
                ? value.Craft.GetComponent<V3HoverController>() : null;
            traction = value != null && value.Craft != null
                ? value.Craft.GetComponent<V3TractionController>() : null;
        }

        public void OnSessionStarted(V3DiagnosticSession session)
        {
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            V3CraftMainframe mainframe = context != null
                ? context.Mainframe : null;
            V3PilotInputAdapter input = context != null
                ? context.PilotInput : null;
            sample.belief.mainframeState = mainframe != null
                ? BootStateName(mainframe.BootState) : "Unavailable";
            sample.belief.mainframeFault = mainframe != null
                ? mainframe.FaultReason : "Mainframe is not installed.";
            sample.belief.connectedDevices = mainframe != null &&
                mainframe.DeviceRegistry != null
                    ? mainframe.DeviceRegistry.Count : 0;
            sample.belief.connectedComputers = mainframe != null
                ? mainframe.Computers.Count : 0;
            sample.belief.activeTasks = CountActiveTasks(mainframe);
            sample.belief.inputAuthority = input != null
                ? AuthorityName(input.AuthorityState) : "Unavailable";
            sample.belief.inputRoute = input != null
                ? RouteName(input.Route) : "None";
            sample.belief.commandAgeSeconds = input != null &&
                input.LastCommandReadTime >= 0d
                    ? (float)Math.Max(0d,
                        sample.identity.simulationTimestamp -
                        input.LastCommandReadTime)
                    : 0f;
            sample.belief.hoverGrounded = hover != null && hover.IsGrounded;
            sample.belief.surfaceState = hover != null
                ? hover.SurfaceState.ToString() : V3SurfaceState.NoSurface.ToString();
            sample.belief.surfaceDetected = hover != null && hover.SurfaceDetected;
            sample.belief.nearSurface = hover != null && hover.IsNearSurface;
            sample.belief.physicalContact = sample.world.contactCount > 0;
            sample.belief.primaryProbeCount = hover != null
                ? hover.PrimaryProbeCount : 0;
            sample.belief.fallbackProbeCount = hover != null
                ? hover.FallbackProbeCount : 0;
            sample.belief.surfaceConfidence = hover != null
                ? hover.SurfaceConfidence : 0f;
            sample.belief.surfaceAgeSeconds = hover != null
                ? hover.SurfaceAgeSeconds : float.PositiveInfinity;
            sample.belief.captureAuthorityMultiplier = hover != null
                ? hover.CaptureAuthorityMultiplier : 0f;
            sample.belief.distanceAuthorityMultiplier = hover != null
                ? hover.DistanceAuthorityMultiplier : 0f;
            sample.belief.captureLimitReason = hover != null
                ? hover.CaptureLimitReason : "Hover runtime unavailable";
            sample.belief.bottomRequestBeforeAuthorityLimit = hover != null
                ? hover.BottomRequestBeforeAuthorityLimit : 0f;
            sample.belief.bottomRequestAfterAuthorityLimit = hover != null
                ? hover.BottomRequestAfterAuthorityLimit : 0f;
            sample.belief.roofRequestBeforeAuthorityLimit = hover != null
                ? hover.RoofRequestBeforeAuthorityLimit : 0f;
            sample.belief.roofRequestAfterAuthorityLimit = hover != null
                ? hover.RoofRequestAfterAuthorityLimit : 0f;
            sample.belief.hoverGroundedProbeCount = hover != null
                ? hover.GroundedProbeCount : 0;
            sample.belief.hoverConfigurationId = hover != null &&
                hover.Configuration != null
                    ? hover.Configuration.StableId : string.Empty;
            sample.belief.hoverConfigurationVersion = hover != null &&
                hover.Configuration != null
                    ? hover.Configuration.ConfigurationVersion : 0;
            sample.belief.targetRideHeight = hover != null
                ? hover.TargetHeight : 0f;
            sample.belief.minimumHoverClearance = hover != null
                ? hover.MinimumClearance : 0f;
            sample.belief.maximumHoverRange = hover != null
                ? hover.ProbeRange : 0f;
            sample.belief.believedSurfaceDistance = hover != null
                ? hover.AverageSurfaceDistance : 0f;
            sample.belief.believedSurfaceSeparationVelocity = hover != null
                ? hover.AverageSurfaceNormalVelocity : 0f;
            sample.belief.believedSurfaceNormal = hover != null
                ? hover.GroundNormal : Vector3.zero;
            sample.belief.desiredSurfaceAlignmentAcceleration = hover != null
                ? hover.SurfaceAlignmentAcceleration : Vector3.zero;
            sample.belief.surfaceTrackingAcceleration = hover != null
                ? hover.SurfaceTrackingAcceleration : 0f;
            sample.belief.roofCaptureAcceleration = hover != null
                ? hover.RoofCaptureAcceleration : 0f;
            sample.belief.roofCaptureOutput = hover != null
                ? hover.RoofCaptureOutput : 0f;
            sample.belief.gripBreakerAmount = traction != null
                ? traction.GripBreakerAmount : 0f;
            sample.belief.relativeAirflow = FindLatestAirflow(mainframe);
        }

        public void OnSessionEnded(V3DiagnosticSession session)
        {
        }

        private static int CountActiveTasks(V3CraftMainframe mainframe)
        {
            if (mainframe == null) return 0;
            IReadOnlyList<V3ScheduledSoftwareTask> tasks =
                mainframe.Scheduler.Tasks;
            int count = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].IsOperational) count++;
            }
            return count;
        }

        private static Vector3 FindLatestAirflow(V3CraftMainframe mainframe)
        {
            if (mainframe == null) return Vector3.zero;
            IReadOnlyList<V3Observation> observations =
                mainframe.Observations.Snapshot;
            double newest = double.MinValue;
            Vector3 airflow = Vector3.zero;
            for (int i = 0; i < observations.Count; i++)
            {
                V3Observation observation = observations[i];
                if (observation.Category ==
                    V3ObservationCategory.EnvironmentAtmosphere &&
                    observation.Timestamp >= newest)
                {
                    newest = observation.Timestamp;
                    airflow = observation.PrimaryVector;
                }
            }
            return airflow;
        }

        private static string BootStateName(V3MainframeBootState value)
        {
            return value switch
            {
                V3MainframeBootState.Uninitialized => "Uninitialized",
                V3MainframeBootState.Discovering => "Discovering",
                V3MainframeBootState.Validating => "Validating",
                V3MainframeBootState.BootingSystems => "BootingSystems",
                V3MainframeBootState.Ready => "Ready",
                V3MainframeBootState.Degraded => "Degraded",
                V3MainframeBootState.Faulted => "Faulted",
                _ => "Unknown"
            };
        }

        private static string AuthorityName(V3InputAuthorityState value)
        {
            return value switch
            {
                V3InputAuthorityState.Uninitialized => "Uninitialized",
                V3InputAuthorityState.AwaitingCraft => "AwaitingCraft",
                V3InputAuthorityState.Connected => "Connected",
                V3InputAuthorityState.Active => "Active",
                V3InputAuthorityState.SuspendedByCursor => "SuspendedByCursor",
                V3InputAuthorityState.SuspendedByUI => "SuspendedByUI",
                V3InputAuthorityState.NoLocalPilot => "NoLocalPilot",
                V3InputAuthorityState.Faulted => "Faulted",
                _ => "Unknown"
            };
        }

        internal static string RouteName(V3InputRoute value)
        {
            return value switch
            {
                V3InputRoute.MainframeIntentBus => "MainframeIntentBus",
                V3InputRoute.LegacyPipeline => "LegacyPipeline",
                _ => "None"
            };
        }
    }
}
