using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3ControlRouter
    {
        private readonly List<V3SystemRequest> requests =
            new List<V3SystemRequest>(12);
        private V3RouterProfileDefinition profile;

        public IReadOnlyList<V3SystemRequest> Requests => requests;
        public V3RouterProfileDefinition Profile => profile;

        public void Initialize(V3RouterProfileDefinition value)
        {
            profile = value;
            requests.Clear();
        }

        public void ResetDynamicState()
        {
            requests.Clear();
        }

        public V3PilotCommand Resolve(
            V3PilotCommand pilot,
            V3SoftwareScheduler scheduler,
            double timestamp)
        {
            requests.Clear();
            if (scheduler == null ||
                !scheduler.IsRoleOperational(
                    V3SystemComputerRole.PilotInterface))
            {
                return default;
            }

            V3PilotCommand routed = pilot;
            if (scheduler.IsRoleOperational(V3SystemComputerRole.Drive))
            {
                AddStateRequest(
                    pilot.Throttle >= 0f
                        ? V3ControlDomain.Propulsion
                        : V3ControlDomain.Braking,
                    "software.drive.stock",
                    pilot.Throttle,
                    timestamp,
                    "Pilot longitudinal intent");
                routed.Throttle *= Weight(
                    pilot.Throttle >= 0f
                        ? V3ControlDomain.Propulsion
                        : V3ControlDomain.Braking);
            }
            else
            {
                routed.Throttle = 0f;
                routed.EmergencyOverload = false;
            }

            if (scheduler.IsRoleOperational(V3SystemComputerRole.Hover))
            {
                AddStateRequest(
                    V3ControlDomain.RideHeight,
                    "software.hover.stock",
                    pilot.Lift - pilot.Downforce,
                    timestamp,
                    "Pilot lift and downforce intent");
                float weight = Weight(V3ControlDomain.RideHeight);
                routed.Lift *= weight;
                routed.Downforce *= weight;
            }
            else
            {
                routed.Lift = 0f;
                routed.Downforce = 0f;
            }

            if (scheduler.IsRoleOperational(
                V3SystemComputerRole.Stabilizer))
            {
                AddStateRequest(
                    V3ControlDomain.Steering,
                    "software.stabilizer.stock",
                    pilot.Yaw,
                    timestamp,
                    "Pilot yaw intent");
                AddStateRequest(
                    V3ControlDomain.AttitudeStability,
                    "software.stabilizer.stock",
                    pilot.StabilizationEnabled ? 1f : 0f,
                    timestamp,
                    "Attitude stability state");
                routed.Yaw *= Weight(V3ControlDomain.Steering);
                routed.Pitch *= Weight(
                    V3ControlDomain.AttitudeStability);
            }
            else
            {
                routed.StabilizationEnabled = false;
                routed.Pitch = 0f;
                routed.Yaw = 0f;
            }

            if (scheduler.IsRoleOperational(V3SystemComputerRole.Traction))
            {
                AddStateRequest(
                    V3ControlDomain.Strafe,
                    "software.traction.stock",
                    pilot.Strafe,
                    timestamp,
                    "Pilot lateral intent");
                AddStateRequest(
                    V3ControlDomain.Traction,
                    "software.traction.stock",
                    pilot.GripBreaker ? 0f : 1f,
                    timestamp,
                    "Traction state");
                routed.Strafe *= Weight(V3ControlDomain.Strafe);
            }
            else
            {
                routed.Strafe = 0f;
                routed.GripBreaker = false;
            }

            if (scheduler.IsRoleOperational(
                V3SystemComputerRole.Aerodynamics))
            {
                AddStateRequest(
                    V3ControlDomain.Aerodynamics,
                    "software.aerodynamics.stock",
                    1f,
                    timestamp,
                    "Balanced active aero enabled");
            }

            AddStateRequest(
                V3ControlDomain.ManualPilot,
                "software.pilot_interface.stock",
                1f,
                timestamp,
                "Manual pilot authority");
            return routed;
        }

        private void AddStateRequest(
            V3ControlDomain domain,
            string source,
            float state,
            double timestamp,
            string reason)
        {
            requests.Add(new V3SystemRequest(
                source + "." + domain,
                source,
                domain,
                V3SystemRequestType.State,
                V3RequestFrame.ChassisLocal,
                Vector3.zero,
                Vector3.zero,
                0f,
                state,
                PartCapability.None,
                1f,
                Weight(domain),
                1f,
                10,
                timestamp,
                0.1f,
                reason));
        }

        private float Weight(V3ControlDomain domain)
        {
            return profile != null ? profile.GetWeight(domain) : 1f;
        }
    }
}
