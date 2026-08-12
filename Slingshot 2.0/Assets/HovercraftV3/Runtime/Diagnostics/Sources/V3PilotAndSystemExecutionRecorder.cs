using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public sealed class V3PilotAndSystemExecutionRecorder : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;
        private V3DiagnosticSession session;
        private bool taskOverflowWarned;
        private bool requestOverflowWarned;

        public string SourceId => "intent_execution";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
        }

        public void OnSessionStarted(V3DiagnosticSession value)
        {
            session = value;
            taskOverflowWarned = false;
            requestOverflowWarned = false;
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            CapturePilot(ref sample);
            CaptureTasks(ref sample);
            CaptureRequests(ref sample);
            CapturePower(ref sample);
        }

        public void OnSessionEnded(V3DiagnosticSession value)
        {
            session = null;
        }

        private void CapturePilot(ref V3DiagnosticSample sample)
        {
            V3PilotInputAdapter input = context != null
                ? context.PilotInput : null;
            V3CraftMainframe mainframe = context != null
                ? context.Mainframe : null;
            if (input == null) return;
            V3PilotRawInputSnapshot raw = input.RawInputSnapshot;
            sample.pilot.inputDevice = input.ActiveInputSource;
            sample.pilot.rawThrottle = raw.Throttle;
            sample.pilot.rawStrafe = raw.Strafe;
            sample.pilot.rawMouseLook = raw.MouseLook;
            sample.pilot.rawGamepadLook = raw.GamepadLook;
            sample.pilot.rawLift = raw.Lift;
            sample.pilot.rawDownforce = raw.Downforce;
            sample.pilot.rawGripBreaker = raw.GripBreaker;
            sample.pilot.rawEmergencyOverload = raw.EmergencyOverload;
            sample.pilot.rawInputTimestamp = raw.Timestamp;
            sample.pilot.processedCommand = input.CurrentCommand;
            sample.pilot.route = V3CraftBeliefRecorder.RouteName(input.Route);
            sample.pilot.authority = sample.belief.inputAuthority;
            sample.pilot.intentPublished = mainframe != null &&
                mainframe.Intents.HasPilotIntent;
            sample.pilot.commandTimestamp = input.LastCommandReadTime;
            sample.pilot.intentTimestamp = mainframe != null &&
                mainframe.Intents.HasPilotIntent
                    ? mainframe.Intents.LatestPilotIntent.Timestamp
                    : -1d;
            sample.pilot.commandAgeSeconds = input.LastCommandReadTime >= 0d
                ? (float)Math.Max(0d,
                    sample.identity.simulationTimestamp -
                    input.LastCommandReadTime)
                : 0f;
        }

        private void CaptureTasks(ref V3DiagnosticSample sample)
        {
            V3CraftMainframe mainframe = context != null
                ? context.Mainframe : null;
            IReadOnlyList<V3ScheduledSoftwareTask> tasks = mainframe != null
                ? mainframe.Scheduler.Tasks : null;
            int sourceCount = tasks != null ? tasks.Count : 0;
            int capacity = sample.tasks != null ? sample.tasks.Length : 0;
            int count = Mathf.Min(sourceCount, capacity);
            sample.taskCount = count;
            for (int i = 0; i < count; i++)
            {
                V3ScheduledSoftwareTask task = tasks[i];
                V3SystemRuntimeSnapshot runtime = task.SystemRuntime != null
                    ? task.SystemRuntime.CaptureSnapshot() : default;
                sample.tasks[i] = new V3TaskDiagnosticRecord
                {
                    taskId = task.TaskId,
                    role = RoleName(task.Software != null
                        ? task.Software.Role : runtime.Role),
                    computerId = task.ComputerDefinition != null
                        ? task.ComputerDefinition.StableId : string.Empty,
                    softwareId = task.Software != null
                        ? task.Software.StableId : string.Empty,
                    requestedRateHz = task.RequestedRateHz,
                    capacityLimitedRateHz = task.CapacityLimitedRateHz,
                    powerLimitedRateHz = task.PowerLimitedRateHz,
                    grantedRateHz = task.GrantedRateHz,
                    measuredRateHz = task.MeasuredActualRateHz,
                    minimumUsefulRateHz = task.Software != null
                        ? task.Software.MinimumUsefulRateHz : 0f,
                    executionCount = task.ExecutionCount,
                    skippedCount = task.SkippedExecutionCount,
                    lastExecutionTimestamp = task.LastExecutionTimestamp,
                    lastExecutionDurationSeconds = task.LastExecutionDurationSeconds,
                    accumulatorSeconds = task.AccumulatorSeconds,
                    requestedPower = task.RequestedPower,
                    grantedPower = task.GrantedPower,
                    bottleneck = task.Bottleneck,
                    faulted = task.IsFaulted,
                    fault = task.LastFailure,
                    publishedThisTick = runtime.OutputPublished &&
                        Math.Abs(runtime.LastExecutionTime -
                            sample.identity.simulationTimestamp) <=
                        sample.identity.fixedDelta + 0.0001d
                };
            }
            if (sourceCount > capacity && !taskOverflowWarned && session != null)
            {
                taskOverflowWarned = true;
                session.AddDataWarning(
                    "The scheduler task count exceeded configured per-sample capacity; excess records were dropped.");
            }
        }

        private void CaptureRequests(ref V3DiagnosticSample sample)
        {
            V3CraftMainframe mainframe = context != null
                ? context.Mainframe : null;
            IReadOnlyList<V3SystemRequest> requests = mainframe != null
                ? mainframe.ControlRouter.Requests : null;
            int sourceCount = requests != null ? requests.Count : 0;
            int capacity = sample.requests != null ? sample.requests.Length : 0;
            int count = Mathf.Min(sourceCount, capacity);
            sample.requestCount = count;
            for (int i = 0; i < count; i++)
            {
                V3SystemRequest request = requests[i];
                sample.requests[i] = new V3SystemRequestDiagnosticRecord
                {
                    requestId = request.RequestId,
                    sourceSystemId = request.SourceSystemId,
                    domain = DomainName(request.Domain),
                    requestType = RequestTypeName(request.RequestType),
                    frame = FrameName(request.Frame),
                    requestedForce = request.RequestedForce,
                    requestedTorque = request.RequestedTorque,
                    requestedAngleDegrees = request.RequestedAngleDegrees,
                    requestedState = request.RequestedState,
                    eligibleDevices = (int)request.EligibleDevices,
                    confidence = request.Confidence,
                    authority = request.Authority,
                    maximumContribution = request.MaximumContribution,
                    priority = request.Priority,
                    timestamp = request.Timestamp,
                    validitySeconds = request.ValiditySeconds,
                    valid = request.IsValid(sample.identity.simulationTimestamp),
                    reason = request.Reason
                };
            }
            if (sourceCount > capacity && !requestOverflowWarned && session != null)
            {
                requestOverflowWarned = true;
                session.AddDataWarning(
                    "The system request count exceeded configured per-sample capacity; excess records were dropped.");
            }
        }

        private void CapturePower(ref V3DiagnosticSample sample)
        {
            V3PowerDistributor power = context != null && context.Craft != null
                ? context.Craft.GetComponent<V3PowerDistributor>() : null;
            if (power == null) return;
            sample.power.allocationMode = PowerModeName(power.AllocationMode);
            sample.power.requestedSystemsPower = power.RequestedSystemsPower;
            sample.power.grantedSystemsPower = power.GrantedSystemsPower;
            sample.power.requestedPropulsionPower = power.RequestedPropulsionPower;
            sample.power.grantedPropulsionPower = power.GrantedPropulsionPower;
            sample.power.availablePropulsionPower = power.AvailablePropulsionPower;
            sample.power.systemsPowerLimited = power.AreSystemsPowerLimited;
            sample.power.propulsionPowerLimited = power.IsPowerLimited;
        }

        private static string RoleName(V3SystemComputerRole value)
        {
            return value switch
            {
                V3SystemComputerRole.PilotInterface => "PilotInterface",
                V3SystemComputerRole.Drive => "Drive",
                V3SystemComputerRole.Hover => "Hover",
                V3SystemComputerRole.Stabilizer => "Stabilizer",
                V3SystemComputerRole.Traction => "Traction",
                V3SystemComputerRole.Aerodynamics => "Aerodynamics",
                V3SystemComputerRole.Telemetry => "Telemetry",
                _ => "Unknown"
            };
        }

        private static string DomainName(V3ControlDomain value) => value switch
        {
            V3ControlDomain.Propulsion => "Propulsion", V3ControlDomain.Braking => "Braking",
            V3ControlDomain.Steering => "Steering", V3ControlDomain.Strafe => "Strafe",
            V3ControlDomain.RideHeight => "RideHeight", V3ControlDomain.AttitudeStability => "AttitudeStability",
            V3ControlDomain.Traction => "Traction", V3ControlDomain.Aerodynamics => "Aerodynamics",
            V3ControlDomain.Recovery => "Recovery", V3ControlDomain.ManualPilot => "ManualPilot",
            _ => "Unknown"
        };

        private static string RequestTypeName(V3SystemRequestType value) => value switch
        {
            V3SystemRequestType.Force => "Force", V3SystemRequestType.Torque => "Torque",
            V3SystemRequestType.Angle => "Angle", V3SystemRequestType.State => "State",
            _ => "Unknown"
        };

        private static string FrameName(V3RequestFrame value) => value switch
        {
            V3RequestFrame.World => "World", V3RequestFrame.ChassisLocal => "ChassisLocal",
            V3RequestFrame.DeviceLocal => "DeviceLocal", _ => "Unknown"
        };

        private static string PowerModeName(V3PowerAllocationMode value) => value switch
        {
            V3PowerAllocationMode.Balanced => "Balanced", V3PowerAllocationMode.Propulsion => "Propulsion",
            V3PowerAllocationMode.Stability => "Stability", V3PowerAllocationMode.Recovery => "Recovery",
            _ => "Unknown"
        };
    }
}
