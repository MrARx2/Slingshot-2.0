using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public sealed class V3ControlDecisionRecorder : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;
        private V3HoverController hover;
        private V3TractionController traction;
        private V3VectorController vector;
        private V3PowerDistributor power;

        public string SourceId => "control_decision";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
            GameObject craft = value != null && value.Craft != null
                ? value.Craft.gameObject : null;
            hover = craft != null ? craft.GetComponent<V3HoverController>() : null;
            traction = craft != null
                ? craft.GetComponent<V3TractionController>() : null;
            vector = craft != null ? craft.GetComponent<V3VectorController>() : null;
            power = craft != null ? craft.GetComponent<V3PowerDistributor>() : null;
        }

        public void OnSessionStarted(V3DiagnosticSession session)
        {
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            V3ControlRouter router = context != null && context.Mainframe != null
                ? context.Mainframe.ControlRouter : null;
            V3RouterProfileDefinition profile = router != null
                ? router.Profile : null;
            IReadOnlyList<V3SystemRequest> requests = router != null
                ? router.Requests : null;
            int requestCount = requests != null ? requests.Count : 0;
            sample.router.activeProfileId = profile != null
                ? profile.StableId : string.Empty;
            sample.router.activeProfileName = profile != null
                ? profile.DisplayName : "Unprofiled";
            sample.router.domainWeights = CaptureWeights(profile);
            sample.router.incomingRequestCount = requestCount;

            float authorityTotal = 0f;
            int validCount = 0;
            int rejectedCount = 0;
            int conflicts = 0;
            for (int i = 0; i < requestCount; i++)
            {
                V3SystemRequest request = requests[i];
                bool valid = request.IsValid(sample.identity.simulationTimestamp);
                if (!valid)
                {
                    rejectedCount++;
                    continue;
                }
                validCount++;
                authorityTotal += request.Authority;
                for (int j = i + 1; j < requestCount; j++)
                {
                    V3SystemRequest other = requests[j];
                    if (other.IsValid(sample.identity.simulationTimestamp) &&
                        request.Domain == other.Domain &&
                        !string.Equals(request.SourceSystemId,
                            other.SourceSystemId, StringComparison.Ordinal) &&
                        request.RequestedState * other.RequestedState < -0.05f)
                    {
                        conflicts++;
                    }
                }
            }
            sample.router.rejectedRequestCount = rejectedCount;
            sample.router.conflictCount = conflicts;
            sample.router.grantedAuthority = validCount > 0
                ? authorityTotal / validCount : 0f;

            V3ActuatorAllocationResult hoverAllocation = hover != null
                ? hover.SurfaceAlignmentAllocation : default;
            V3ActuatorAllocationResult tractionAllocation = traction != null
                ? traction.GripAllocation : default;
            V3ActuatorAllocationResult vectorAllocation = vector != null
                ? vector.YawDampingAllocation : default;
            sample.router.resolvedRequestedForce =
                hoverAllocation.RequestedForceN +
                tractionAllocation.RequestedForceN +
                vectorAllocation.RequestedForceN;
            sample.router.resolvedRequestedTorque =
                hoverAllocation.RequestedTorqueNm +
                tractionAllocation.RequestedTorqueNm +
                vectorAllocation.RequestedTorqueNm;
            sample.router.allocatedForce =
                hoverAllocation.AllocatedForceN +
                tractionAllocation.AllocatedForceN +
                vectorAllocation.AllocatedForceN;
            sample.router.allocatedTorque =
                hoverAllocation.AllocatedTorqueNm +
                tractionAllocation.AllocatedTorqueNm +
                vectorAllocation.AllocatedTorqueNm;
            sample.router.residualForce =
                sample.router.resolvedRequestedForce -
                sample.router.allocatedForce;
            sample.router.residualTorque =
                sample.router.resolvedRequestedTorque -
                sample.router.allocatedTorque;

            int constraints = CountHardConstraints();
            if (power != null &&
                (power.AreSystemsPowerLimited || power.IsPowerLimited))
            {
                constraints++;
            }
            sample.router.hardConstraintCount = constraints;
            sample.router.clippedOrSaturated =
                sample.router.residualForce.sqrMagnitude > 0.01f ||
                sample.router.residualTorque.sqrMagnitude > 0.01f;
            sample.router.reason = rejectedCount > 0
                ? "One or more incoming requests were outside their validity window."
                : constraints > 0
                    ? "Physical, thermal, firmware, or power constraints were active."
                    : sample.router.clippedOrSaturated
                        ? "The physical actuator set could not allocate the full requested wrench."
                        : "No diagnostic router constraint was observed.";
        }

        public void OnSessionEnded(V3DiagnosticSession session)
        {
        }

        private int CountHardConstraints()
        {
            IReadOnlyList<RuntimePartInstance> parts =
                context != null && context.Craft != null
                    ? context.Craft.InstalledParts : null;
            int count = 0;
            int sourceCount = parts != null ? parts.Count : 0;
            for (int i = 0; i < sourceCount; i++)
            {
                RuntimePartInstance part = parts[i];
                if (part != null &&
                    (!part.IsEnabled || part.IsThermallyLockedOut ||
                     part.ThermalOutputLimit < 0.9999f))
                {
                    count++;
                }
            }
            return count;
        }

        private static V3RouterDomainWeightSample CaptureWeights(
            V3RouterProfileDefinition profile)
        {
            return new V3RouterDomainWeightSample
            {
                propulsion = Weight(profile, V3ControlDomain.Propulsion),
                braking = Weight(profile, V3ControlDomain.Braking),
                steering = Weight(profile, V3ControlDomain.Steering),
                strafe = Weight(profile, V3ControlDomain.Strafe),
                rideHeight = Weight(profile, V3ControlDomain.RideHeight),
                attitudeStability = Weight(
                    profile, V3ControlDomain.AttitudeStability),
                traction = Weight(profile, V3ControlDomain.Traction),
                aerodynamics = Weight(profile, V3ControlDomain.Aerodynamics),
                recovery = Weight(profile, V3ControlDomain.Recovery),
                manualPilot = Weight(profile, V3ControlDomain.ManualPilot)
            };
        }

        private static float Weight(
            V3RouterProfileDefinition profile, V3ControlDomain domain)
        {
            return profile != null ? profile.GetWeight(domain) : 1f;
        }
    }
}
