using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    internal static class V3StringBuilderExtensions
    {
        public static StringBuilder Append(
            this StringBuilder builder,
            float value,
            string format)
        {
            return builder.Append(value.ToString(format));
        }
    }

    public enum V3SystemsPageId
    {
        Mainframe,
        PilotInterface,
        Drive,
        Hover,
        Stabilizer,
        Traction,
        Aerodynamics,
        Telemetry,
        PowerAndCooling,
        Sensors
    }

    public sealed class V3SystemsPageModel
    {
        public string CraftName { get; internal set; } = string.Empty;
        public string SystemName { get; internal set; } = string.Empty;
        public string Manufacturer { get; internal set; } = string.Empty;
        public string Status { get; internal set; } = string.Empty;
        public string Body { get; internal set; } = string.Empty;
    }

    public interface IV3SystemsPageProvider
    {
        V3SystemsPageId Id { get; }
        string Title { get; }
        void Refresh(
            V3SystemsConsoleContext context,
            V3SystemsPageModel model);
    }

    public sealed class V3SystemsConsoleContext
    {
        public GameObject Craft { get; private set; }
        public V3PilotInputAdapter Input { get; private set; }
        public V3CraftRuntime Runtime { get; private set; }
        public V3CraftMainframe Mainframe { get; private set; }
        public V3CraftTelemetryHub Telemetry { get; private set; }
        public V3PowerDistributor Power { get; private set; }
        public V3ThermalController Thermal { get; private set; }
        public V3HoverController Hover { get; private set; }
        public V3TractionController Traction { get; private set; }
        public V3ActuatorCommandRouter ActuatorRouter { get; private set; }
        public V3ControllerPipeline Pipeline { get; private set; }

        public void Bind(
            GameObject craft,
            V3PilotInputAdapter input)
        {
            Craft = craft;
            Input = input;
            Runtime = Get<V3CraftRuntime>(craft);
            Mainframe = Get<V3CraftMainframe>(craft);
            Telemetry = Get<V3CraftTelemetryHub>(craft);
            Power = Get<V3PowerDistributor>(craft);
            Thermal = Get<V3ThermalController>(craft);
            Hover = Get<V3HoverController>(craft);
            Traction = Get<V3TractionController>(craft);
            ActuatorRouter = Get<V3ActuatorCommandRouter>(craft);
            Pipeline = Get<V3ControllerPipeline>(craft);
        }

        public V3ScheduledSoftwareTask FindTask(
            V3SystemComputerRole role)
        {
            if (Mainframe == null)
            {
                return null;
            }

            IReadOnlyList<V3ScheduledSoftwareTask> tasks =
                Mainframe.Scheduler.Tasks;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].Software != null &&
                    tasks[i].Software.Role == role)
                {
                    return tasks[i];
                }
            }

            return null;
        }

        private static T Get<T>(GameObject target)
            where T : Component
        {
            return target != null ? target.GetComponent<T>() : null;
        }
    }

    public sealed class V3SystemsPageRegistry
    {
        private readonly List<IV3SystemsPageProvider> providers =
            new List<IV3SystemsPageProvider>(10);

        public IReadOnlyList<IV3SystemsPageProvider> Providers =>
            providers;
        public int Count => providers.Count;

        public void Rebuild(V3SystemsConsoleContext context)
        {
            providers.Clear();
            if (context == null || context.Craft == null)
            {
                return;
            }

            if (context.Mainframe != null)
            {
                Add(V3SystemsPageId.Mainframe);
                Add(V3SystemsPageId.PilotInterface);
                Add(V3SystemsPageId.Drive);
                Add(V3SystemsPageId.Hover);
                Add(V3SystemsPageId.Stabilizer);
                Add(V3SystemsPageId.Traction);
                Add(V3SystemsPageId.Aerodynamics);
                Add(V3SystemsPageId.Telemetry);
                Add(V3SystemsPageId.PowerAndCooling);
                Add(V3SystemsPageId.Sensors);
                return;
            }

            if (context.Input != null)
            {
                Add(V3SystemsPageId.PilotInterface);
            }

            if (context.Power != null || context.Thermal != null)
            {
                Add(V3SystemsPageId.PowerAndCooling);
            }
        }

        private void AddRole(
            V3SystemsConsoleContext context,
            V3SystemComputerRole role)
        {
            if (context.FindTask(role) != null)
            {
                Add((V3SystemsPageId)((int)role + 1));
            }
        }

        private void Add(V3SystemsPageId id)
        {
            providers.Add(new V3LiveSystemsPageProvider(id));
        }
    }

    internal sealed class V3LiveSystemsPageProvider :
        IV3SystemsPageProvider
    {
        private readonly StringBuilder text = new StringBuilder(1024);
        private readonly V3ActuatorRouterEntrySnapshot[] routerEntries =
            new V3ActuatorRouterEntrySnapshot[32];

        public V3LiveSystemsPageProvider(V3SystemsPageId id)
        {
            Id = id;
        }

        public V3SystemsPageId Id { get; }
        public string Title =>
            Id == V3SystemsPageId.PowerAndCooling
                ? "Power and Cooling"
                : Id == V3SystemsPageId.PilotInterface
                    ? "Pilot Interface"
                    : Id.ToString();

        public void Refresh(
            V3SystemsConsoleContext context,
            V3SystemsPageModel model)
        {
            text.Clear();
            model.CraftName =
                context.Runtime != null &&
                context.Runtime.Build != null
                    ? context.Runtime.Build.DisplayName
                    : context.Craft != null
                        ? context.Craft.name
                        : "NO CRAFT";
            model.SystemName = Title;
            model.Manufacturer = ResolveManufacturer(context);
            model.Status = ResolveStatus(context);
            AppendCraftSummary(context);

            switch (Id)
            {
                case V3SystemsPageId.Mainframe:
                    Mainframe(context);
                    break;
                case V3SystemsPageId.PilotInterface:
                    Pilot(context);
                    break;
                case V3SystemsPageId.Drive:
                    Computer(context, V3SystemComputerRole.Drive);
                    Drive(context);
                    break;
                case V3SystemsPageId.Hover:
                    Computer(context, V3SystemComputerRole.Hover);
                    Hover(context);
                    break;
                case V3SystemsPageId.Stabilizer:
                    Computer(
                        context,
                        V3SystemComputerRole.Stabilizer);
                    Stabilizer(context);
                    break;
                case V3SystemsPageId.Traction:
                    Computer(context, V3SystemComputerRole.Traction);
                    Traction(context);
                    break;
                case V3SystemsPageId.Aerodynamics:
                    Computer(
                        context,
                        V3SystemComputerRole.Aerodynamics);
                    Aerodynamics(context);
                    break;
                case V3SystemsPageId.Telemetry:
                    Computer(context, V3SystemComputerRole.Telemetry);
                    Telemetry(context);
                    break;
                case V3SystemsPageId.PowerAndCooling:
                    PowerAndCooling(context);
                    break;
                case V3SystemsPageId.Sensors:
                    Sensors(context);
                    break;
            }

            model.Body = text.ToString();
        }

        private void AppendCraftSummary(
            V3SystemsConsoleContext context)
        {
            CraftBuildDefinition build =
                context.Runtime != null ? context.Runtime.Build : null;
            text.AppendLine("CRAFT TOPOLOGY");
            text.Append("Build: ").AppendLine(
                build != null ? build.DisplayName : "Unavailable");
            text.Append("Build ID: ").AppendLine(
                build != null &&
                !string.IsNullOrWhiteSpace(build.StableId)
                    ? build.StableId
                    : "UNASSIGNED");
            text.Append("Ownership / Asset GUID: ")
                .Append(build != null ? build.Ownership.ToString() : "Unavailable")
                .Append(" / ")
                .AppendLine(build != null && !string.IsNullOrEmpty(build.BuildAssetGuid)
                    ? build.BuildAssetGuid : "UNASSIGNED");
            text.Append("Runtime Root: ").AppendLine(
                context.Craft != null
                    ? context.Craft.name
                    : "None");
            text.Append("Control Route: ").AppendLine(
                context.Pipeline != null
                    ? context.Pipeline.ActiveControlPath.ToString()
                    : "Unavailable");
            text.Append("Route Reason: ").AppendLine(
                context.Pipeline != null
                    ? context.Pipeline.ControlPathReason
                    : "No controller pipeline");
            text.Append("Input Route: ").AppendLine(
                context.Input != null
                    ? context.Input.Route.ToString()
                    : V3InputRoute.None.ToString());
            text.Append("Mainframe: ").AppendLine(
                context.Mainframe != null
                    ? context.Mainframe.BootState.ToString()
                    : "Not Installed");
            text.Append("Parts / Tasks / Sensors: ")
                .Append(
                    context.Runtime != null
                        ? context.Runtime.InstalledParts.Count
                        : 0)
                .Append(" / ")
                .Append(
                    context.Mainframe != null
                        ? context.Mainframe.Scheduler.Tasks.Count
                        : 0)
                .Append(" / ")
                .AppendLine(
                    (context.Mainframe != null
                        ? context.Mainframe.Sensors.Count
                        : 0).ToString());
            text.Append("Computers / Systems / Devices: ")
                .Append(
                    context.Mainframe != null
                        ? context.Mainframe.Computers.Count
                        : 0)
                .Append(" / ")
                .Append(
                    context.Mainframe != null
                        ? context.Mainframe.SystemRuntimes.Count
                        : 0)
                .Append(" / ")
                .AppendLine(
                    (context.Mainframe != null &&
                     context.Mainframe.DeviceRegistry != null
                        ? context.Mainframe.DeviceRegistry.Count
                        : 0).ToString());
            text.Append("Recorder Schema: ")
                .Append(V3ForensicSchemaIdentity.Version)
                .Append(" / ")
                .AppendLine(V3ForensicSchemaIdentity.VersionText);
            text.AppendLine();
        }

        private void Mainframe(V3SystemsConsoleContext context)
        {
            V3CraftMainframe mainframe = context.Mainframe;
            text.Append("Boot State: ").AppendLine(
                mainframe.BootState.ToString());
            text.Append("Identity: ").AppendLine(
                mainframe.Definition != null
                    ? mainframe.Definition.DisplayName
                    : "Unavailable");
            text.Append("Connected Devices: ").AppendLine(
                mainframe.DeviceRegistry != null
                    ? mainframe.DeviceRegistry.Count.ToString()
                    : "0");
            text.Append("Active Software: ").AppendLine(
                mainframe.Scheduler.Tasks.Count.ToString());
            text.Append("Scheduler Compute: ")
                .Append(mainframe.Scheduler.UsedComputePerSecond, "0.0")
                .AppendLine(" units/s");
            text.Append("Observation Bus Entries: ").AppendLine(
                mainframe.Observations.Snapshot.Count.ToString());
            text.Append("Command Bus Requests: ").AppendLine(
                mainframe.ControlRouter.Requests.Count.ToString());
            text.Append("Router Profile: ").AppendLine(
                mainframe.ControlRouter.Profile != null
                    ? mainframe.ControlRouter.Profile.DisplayName
                    : "Default");
            AppendPower(context);
            text.Append("Health: ").AppendLine(
                string.IsNullOrEmpty(mainframe.FaultReason)
                    ? "Nominal"
                    : mainframe.FaultReason);
            if (mainframe.FaultReasons.Count > 0)
            {
                text.AppendLine("Fault Reasons:");
                for (int i = 0;
                     i < mainframe.FaultReasons.Count;
                     i++)
                {
                    text.Append("  - ").AppendLine(
                        mainframe.FaultReasons[i]);
                }
            }
            if (mainframe.DegradedReasons.Count > 0)
            {
                text.AppendLine("Readiness Reasons:");
                for (int i = 0;
                     i < mainframe.DegradedReasons.Count;
                     i++)
                {
                    text.Append("  - ").AppendLine(
                        mainframe.DegradedReasons[i]);
                }
            }
        }

        private void Pilot(V3SystemsConsoleContext context)
        {
            V3PilotInputAdapter input = context.Input;
            text.Append("Input Source: ").AppendLine(
                input != null ? input.ActiveInputSource : "None");
            text.Append("Authority: ").AppendLine(
                input != null
                    ? input.AuthorityState.ToString()
                    : V3InputAuthorityState.NoLocalPilot.ToString());
            text.Append("Route: ").AppendLine(
                input != null
                    ? input.Route.ToString()
                    : V3InputRoute.None.ToString());
            text.Append("Target Craft: ").AppendLine(
                input != null && input.TargetCraft != null
                    ? input.TargetCraft.name
                    : "None");
            text.Append("Cursor: ").AppendLine(
                Cursor.lockState.ToString());
            text.Append("Input Actions: ").AppendLine(
                input != null && input.AreInputActionsEnabled
                    ? "Enabled"
                    : "Disabled");
            text.Append("Intent Bus: ").AppendLine(
                input != null && input.IsIntentBusConnected
                    ? "Connected"
                    : "Disconnected");
            text.Append("Publishing: ").AppendLine(
                input != null && input.IsPublishingIntent
                    ? "Active"
                    : "Suspended");

            V3PilotCommand command = context.Mainframe != null &&
                context.Mainframe.Intents.HasPilotIntent
                    ? context.Mainframe.Intents.LatestPilotIntent.Command
                    : input != null
                        ? input.CurrentCommand
                        : default;
            text.Append("Throttle / Strafe: ")
                .Append(command.Throttle, "0.00").Append(" / ")
                .AppendLine(command.Strafe.ToString("0.00"));
            text.Append("Yaw / Pitch: ")
                .Append(command.Yaw, "0.00").Append(" / ")
                .AppendLine(command.Pitch.ToString("0.00"));
            text.Append("Lift / Downforce: ")
                .Append(command.Lift, "0.00").Append(" / ")
                .AppendLine(command.Downforce.ToString("0.00"));
            text.Append("Grip Breaker / Overload: ")
                .Append(command.GripBreaker ? "ON" : "OFF")
                .Append(" / ")
                .AppendLine(command.EmergencyOverload ? "ON" : "OFF");
            text.Append("Stabilization: ").AppendLine(
                command.StabilizationEnabled ? "ON" : "OFF");
            text.Append("Last Command / Intent / Legacy: ")
                .Append(
                    input != null
                        ? input.LastCommandReadTime.ToString("0.000")
                        : "-1")
                .Append(" / ")
                .Append(
                    input != null
                        ? input.LastIntentPublicationTime.ToString("0.000")
                        : "-1")
                .Append(" / ")
                .AppendLine(
                    input != null
                        ? input.LastLegacySubmissionTime.ToString("0.000")
                        : "-1");
            Computer(context, V3SystemComputerRole.PilotInterface);
        }

        private void Drive(V3SystemsConsoleContext context)
        {
            V3PilotCommand command = LatestCommand(context);
            text.Append("Requested Propulsion: ")
                .Append(command.Throttle, "0.00").AppendLine();
            text.Append("Router Allocation: ").AppendLine(
                context.ActuatorRouter != null
                    ? context.ActuatorRouter.PowerAllocationMode.ToString()
                    : "Unavailable");
            RuntimeThrusterInstance main =
                FindStrongestThruster(context);
            AppendPart("Main Thruster", main);
            text.Append("Emergency Overload: ").AppendLine(
                command.EmergencyOverload ? "Requested" : "Off");
        }

        private void Hover(V3SystemsConsoleContext context)
        {
            V3HoverController hover = context.Hover;
            V3HoverConfiguration configuration = hover != null
                ? hover.Configuration : null;
            text.Append("Configuration: ").AppendLine(
                configuration != null
                    ? configuration.StableId + " v" +
                      configuration.ConfigurationVersion
                    : "Unavailable");
            text.Append("Target / Minimum: ")
                .Append(hover != null ? hover.TargetHeight : 0f, "0.00")
                .Append(" / ")
                .Append(hover != null ? hover.MinimumClearance : 0f, "0.00")
                .AppendLine(" m");
            text.Append("Maximum Range: ")
                .Append(hover != null ? hover.ProbeRange : 0f, "0.00")
                .AppendLine(" m");
            text.Append("Strength / Damping: ")
                .Append(configuration != null ? configuration.HoverStrength : 0f,
                    "0.000")
                .Append(" / ")
                .Append(configuration != null ? configuration.HoverDamping : 0f,
                    "0.000")
                .AppendLine();
            text.Append("Gravity Compensation: ").AppendLine(
                configuration != null &&
                configuration.GravityCompensationEnabled
                    ? "Enabled x" +
                      configuration.GravityCompensationMultiplier.ToString("0.00")
                    : "Disabled");
            text.Append("Grounded: ").AppendLine(
                hover != null && hover.IsGrounded ? "Yes" : "No");
            text.Append("Surface State: ").AppendLine(
                hover != null ? hover.SurfaceState.ToString() : "Unavailable");
            text.Append("Detected / Near / Contact: ")
                .Append(hover != null && hover.SurfaceDetected ? "YES" : "NO")
                .Append(" / ")
                .Append(hover != null && hover.IsNearSurface ? "YES" : "NO")
                .AppendLine(" / see physical contact ledger");
            text.Append("Grounded Probes: ").AppendLine(
                hover != null
                    ? hover.GroundedProbeCount.ToString()
                    : "0");
            text.Append("Primary / Fallback / Confidence: ")
                .Append(hover != null ? hover.PrimaryProbeCount : 0)
                .Append(" / ")
                .Append(hover != null ? hover.FallbackProbeCount : 0)
                .Append(" / ")
                .Append(hover != null ? hover.SurfaceConfidence : 0f, "0.00")
                .AppendLine();
            text.Append("Capture Distance / Combined Authority: ")
                .Append(hover != null ? hover.DistanceAuthorityMultiplier : 0f,
                    "0.00")
                .Append(" / ")
                .Append(hover != null ? hover.CaptureAuthorityMultiplier : 0f,
                    "0.00")
                .Append("  ")
                .AppendLine(hover != null ? hover.CaptureLimitReason : "Unavailable");
            text.Append("Average Surface Normal: ").AppendLine(
                hover != null
                    ? hover.GroundNormal.ToString("F3")
                    : "Unavailable");
            text.Append("Surface Alignment Request: ").AppendLine(
                hover != null
                    ? hover.SurfaceAlignmentAcceleration.ToString("F3")
                    : "Unavailable");
            text.Append("Surface Distance / Separation: ")
                .Append(hover != null ? hover.AverageSurfaceDistance : 0f, "0.00")
                .Append(" m / ")
                .Append(hover != null ? hover.AverageSurfaceNormalVelocity : 0f,
                    "0.00")
                .AppendLine(" m/s");
            text.Append("Curvature Follow (+hover / -roof): ")
                .Append(hover != null ? hover.SurfaceTrackingAcceleration : 0f,
                    "0.0")
                .AppendLine(" m/s2 via bottom thrusters");
            text.Append("Roof Capture: ")
                .Append(hover != null ? hover.RoofCaptureAcceleration : 0f,
                    "0.0")
                .Append(" m/s2 / output ")
                .AppendLine(
                    (hover != null ? hover.RoofCaptureOutput : 0f)
                    .ToString("0.00"));
            AppendHoverAuthorityTable(context, hover);
            AppendPartsBySocket(context, "Hover.", "Hover Assembly");
        }

        private void Stabilizer(V3SystemsConsoleContext context)
        {
            V3PilotCommand command = LatestCommand(context);
            text.Append("Pitch Intent: ")
                .Append(command.Pitch, "0.00").AppendLine();
            text.Append("Yaw Intent: ")
                .Append(command.Yaw, "0.00").AppendLine();
            text.Append("Stabilization State: ").AppendLine(
                command.StabilizationEnabled ? "Active" : "Off");
            text.Append("Requested Surface Alignment: ").AppendLine(
                context.Hover != null
                    ? context.Hover.SurfaceAlignmentAcceleration
                        .ToString("F3")
                    : "Unavailable");
            text.Append("Eligible Actuators: ").AppendLine(
                context.ActuatorRouter != null
                    ? context.ActuatorRouter.ThrusterCount.ToString()
                    : "0");
        }

        private void Traction(V3SystemsConsoleContext context)
        {
            V3CraftTelemetryHub telemetry = context.Telemetry;
            text.Append("Grounded: ").AppendLine(
                context.Hover != null && context.Hover.IsGrounded
                    ? "Yes"
                    : "No");
            text.Append("Local Lateral Velocity: ")
                .Append(
                    telemetry != null ? telemetry.LocalVelocity.x : 0f,
                    "0.00")
                .AppendLine(" m/s");
            text.Append("Local Longitudinal Velocity: ")
                .Append(
                    telemetry != null ? telemetry.LocalVelocity.z : 0f,
                    "0.00")
                .AppendLine(" m/s");
            text.Append("Grip Breaker: ").AppendLine(
                context.Traction != null &&
                context.Traction.GripBreakerAmount > 0.01f
                    ? "Active"
                    : "Off");
            if (context.Traction != null)
            {
                text.Append("Requested Force: ").AppendLine(
                    context.Traction.GripAllocation.RequestedForceN
                        .ToString("F2"));
                text.Append("Granted Force: ").AppendLine(
                    context.Traction.GripAllocation.AllocatedForceN
                        .ToString("F2"));
            }
        }

        private void Aerodynamics(V3SystemsConsoleContext context)
        {
            V3WorldEnvironmentSample environment =
                context.Telemetry != null
                    ? context.Telemetry.Environment
                    : default;
            V3CraftAerodynamicState aerodynamics =
                context.Telemetry != null
                    ? context.Telemetry.Aerodynamics
                    : default;
            Vector3 airflow = aerodynamics.RelativeAirVelocity;
            text.Append("Relative Airflow: ").AppendLine(
                airflow.ToString("F2"));
            text.Append("Air Density: ")
                .Append(environment.AirDensityKgPerCubicMeter, "0.000")
                .AppendLine(" kg/m3");
            text.Append("Mach: ")
                .Append(aerodynamics.MachNumber, "0.000")
                .Append("  Dynamic Pressure: ")
                .Append(aerodynamics.DynamicPressurePa / 1000f, "0.00")
                .AppendLine(" kPa");
            text.Append("Ambient: ")
                .Append(environment.AmbientTemperatureC, "0.0")
                .Append(" C  Pressure: ")
                .Append(environment.AtmosphericPressurePa / 1000f, "0.00")
                .AppendLine(" kPa");
            float totalForce = 0f;
            int fins = 0;
            if (context.Runtime != null)
            {
                for (int i = 0;
                     i < context.Runtime.InstalledParts.Count;
                     i++)
                {
                    if (context.Runtime.InstalledParts[i] is
                        RuntimeAerodynamicFinInstance fin)
                    {
                        fins++;
                        totalForce += fin.CurrentAerodynamicForceN;
                        text.Append("Fin ")
                            .Append(fins)
                            .Append(": ")
                            .Append("AoA ")
                            .Append(fin.CurrentAngleOfAttackDegrees, "0.0")
                            .Append(" deg | Cl ")
                            .Append(fin.CurrentLiftCoefficient, "0.000")
                            .Append(" | Cd ")
                            .Append(fin.CurrentDragCoefficient, "0.000")
                            .Append(" | ")
                            .Append(fin.IsStalled ? "STALL" : "attached")
                            .Append(fin.IsReverseFlow ? " / REVERSE" : string.Empty)
                            .Append(" | lift ")
                            .Append(fin.CurrentWorldLiftForce.magnitude, "0.0")
                            .Append(" N | drag ")
                            .Append(fin.CurrentWorldDragForce.magnitude, "0.0")
                            .AppendLine(" N");
                    }
                }
            }

            text.Append("Total Aero Force: ")
                .Append(totalForce, "0.0").AppendLine(" N");
            text.Append("Fallback State: ").AppendLine(
                fins == 4 ? "Nominal" : "Incomplete fin set");
        }

        private void AppendHoverAuthorityTable(
            V3SystemsConsoleContext context,
            V3HoverController hover)
        {
            text.AppendLine("Device | Product Max | Craft Auto Cap | System Cap | Actual Force");
            int authorityCount = context.ActuatorRouter != null
                ? context.ActuatorRouter.CopyEntrySnapshots(routerEntries)
                : 0;
            for (int i = 0; i < authorityCount; i++)
            {
                V3ActuatorRouterEntrySnapshot entry = routerEntries[i];
                if (!(entry.SocketId.StartsWith("Hover.") ||
                      entry.SocketId.StartsWith("Control.")))
                    continue;
                RuntimeThrusterInstance thruster = entry.Thruster;
                bool roof = entry.SocketId.StartsWith("Control.");
                float systemCap = hover == null ? 0f : roof
                    ? hover.Configuration.AutomaticRoofAuthorityLimit
                    : hover.Configuration.AutomaticHoverAuthorityLimit;
                text.Append(entry.SocketId).Append(" | ")
                    .Append(thruster != null && thruster.ThrusterDefinition != null
                        ? thruster.ThrusterDefinition.MaximumEmergencyForceN
                        : 0f, "0")
                    .Append(" N | ")
                    .Append(thruster != null
                        ? thruster.InstallationAutomaticAuthorityCap
                        : 0f, "0.00")
                    .Append("x | ").Append(systemCap, "0.00")
                    .Append("x | ")
                    .Append(thruster != null ? thruster.CurrentAppliedForceN : 0f, "0")
                    .AppendLine(" N");
            }

            text.AppendLine("Device | Request | Routed | Alloc | Power | FW | Thermal | Actual");
            int count = context.ActuatorRouter != null
                ? context.ActuatorRouter.CopyEntrySnapshots(routerEntries)
                : 0;
            for (int i = 0; i < count; i++)
            {
                V3ActuatorRouterEntrySnapshot entry = routerEntries[i];
                if (!(entry.SocketId.StartsWith("Hover.") ||
                      entry.SocketId.StartsWith("Control.")))
                    continue;
                RuntimeThrusterInstance thruster = entry.Thruster;
                bool roof = entry.SocketId.StartsWith("Control.");
                float preLimit = hover == null ? 0f : roof
                    ? hover.RoofRequestBeforeAuthorityLimit
                    : hover.BottomRequestBeforeAuthorityLimit;
                float grant = thruster != null && thruster.RequestedPower > 0.0001f
                    ? Mathf.Clamp01(thruster.GrantedPower / thruster.RequestedPower)
                    : 1f;
                text.Append(entry.SocketId).Append(" | ")
                    .Append(preLimit, "0.00").Append(" | ")
                    .Append(entry.Combined, "0.00").Append(" | ")
                    .Append(thruster != null ? thruster.RequestedOutput : 0f, "0.00")
                    .Append(" | ").Append(grant, "0.00")
                    .Append(" | ").Append(entry.FirmwareSafeState ? "SAFE" : "OK")
                    .Append(" | ")
                    .Append(thruster != null ? thruster.ThermalOutputLimit : 0f, "0.00")
                    .Append(" | ")
                    .Append(thruster != null ? thruster.CurrentOutput : 0f, "0.00")
                    .AppendLine();
            }
        }

        private void Telemetry(V3SystemsConsoleContext context)
        {
            V3CraftTelemetryHub telemetry = context.Telemetry;
            text.Append("Recording State: ").AppendLine(
                telemetry != null ? "Live Snapshot" : "Unavailable");
            text.Append("Snapshot Sequence: ").AppendLine(
                telemetry != null ? telemetry.Sequence.ToString() : "0");
            text.Append("Craft / World Tick: ")
                .Append(telemetry != null
                    ? telemetry.CraftPhysicsTickId.ToString() : "-1")
                .Append(" / ")
                .AppendLine(telemetry != null
                    ? telemetry.Environment.PhysicsTickId.ToString() : "-1");
            text.Append("World Time: ")
                .Append(telemetry != null
                    ? telemetry.Environment.SimulationTime.ToString("0.000")
                    : "0.000")
                .AppendLine(" s");
            text.Append("Active Part Channels: ").AppendLine(
                telemetry != null
                    ? telemetry.Parts.Count.ToString()
                    : "0");
            text.AppendLine("Dropped / Skipped Updates: 0 / 0");
            text.AppendLine("Latest Data Age: < one physics step");
            text.AppendLine("Export: Reserved for future tooling");
        }

        private void PowerAndCooling(V3SystemsConsoleContext context)
        {
            V3PowerDistributor power = context.Power;
            text.Append("Energy Core: ").AppendLine(
                power != null && power.Core != null
                    ? power.Core.DisplayName
                    : "Unavailable");
            if (power != null && power.Core != null)
            {
                text.Append("Continuous Output: ")
                    .Append(power.Core.ContinuousOutput, "0.0")
                    .AppendLine(" power");
                text.Append("Propulsion Ceiling: ")
                    .Append(power.Core.PropulsionChannelCeiling, "0.0")
                    .AppendLine();
                text.Append("Systems Ceiling: ")
                    .Append(power.Core.SystemsChannelCeiling, "0.0")
                    .AppendLine();
            }

            AppendPower(context);
            V3CraftTelemetryHub telemetry = context.Telemetry;
            text.Append("Hottest Part: ")
                .Append(
                    telemetry != null
                        ? telemetry.MaximumTemperatureC
                        : 0f,
                    "0.0")
                .AppendLine(" C");
            text.Append("Hot / Locked Out: ")
                .Append(telemetry != null ? telemetry.HotPartCount : 0)
                .Append(" / ")
                .AppendLine(
                    (telemetry != null
                        ? telemetry.LockedOutPartCount
                        : 0).ToString());
            text.Append("Active Power Mode: ").AppendLine(
                power != null
                    ? power.AllocationMode.ToString()
                    : "Unavailable");
        }

        private void Sensors(V3SystemsConsoleContext context)
        {
            IReadOnlyList<V3DirectionalSensorDeviceRuntime> sensors =
                context.Mainframe.Sensors;
            for (int i = 0; i < sensors.Count; i++)
            {
                V3DirectionalSensorDeviceRuntime sensor = sensors[i];
                V3DirectionalSensorDefinition definition =
                    sensor.Definition;
                string id =
                    definition != null
                        ? definition.StableId
                        : "sensor.unknown";
                text.Append('[')
                    .Append(
                        definition != null
                            ? definition.Direction.ToString()
                            : "Unknown")
                    .Append("] ")
                    .Append(sensor.GrantedRateHz, "0.0")
                    .Append('/')
                    .Append(sensor.RequestedRateHz, "0.0")
                    .Append(" Hz | ")
                    .Append(sensor.GrantedPower, "0.0")
                    .Append('/')
                    .Append(sensor.RequestedPower, "0.0")
                    .Append(" power | ")
                    .Append(sensor.HealthState)
                    .Append(" | ")
                    .AppendLine(sensor.Bottleneck);
                text.Append("  Actual ")
                    .Append(sensor.MeasuredRateHz, "0.0")
                    .Append(" Hz | samples ")
                    .Append(sensor.SampleCount)
                    .Append(" | firmware ")
                    .AppendLine(
                        sensor.Firmware != null
                            ? sensor.Firmware.StableId
                            : "Missing");
                if (context.Mainframe.Observations.TryGetLatest(
                        V3ObservationCategory.EnvironmentSurface,
                        id,
                        out V3Observation surface))
                {
                    text.Append("  Hit ")
                        .Append(surface.State ? "YES" : "NO")
                        .Append(" | ")
                        .Append(surface.PrimaryScalar, "0.00")
                        .Append(" m | normal ")
                        .AppendLine(surface.PrimaryVector.ToString("F2"));
                }
                else
                {
                    text.AppendLine("  Surface observation awaiting sample");
                }
            }
        }

        private void Computer(
            V3SystemsConsoleContext context,
            V3SystemComputerRole role)
        {
            V3ScheduledSoftwareTask task = context.FindTask(role);
            if (task == null)
            {
                text.AppendLine("Computer: Not installed");
                return;
            }

            text.Append("Computer: ").AppendLine(
                task.ComputerDefinition.DisplayName);
            text.Append("Software: ").AppendLine(
                task.Software != null
                    ? task.Software.name
                    : "None");
            text.Append("Requested / Granted: ")
                .Append(task.RequestedRateHz, "0.0")
                .Append(" / ")
                .Append(task.GrantedRateHz, "0.0")
                .AppendLine(" Hz");
            text.Append("Systems Power: ")
                .Append(task.GrantedPower, "0.0")
                .Append(" / ")
                .Append(task.RequestedPower, "0.0")
                .AppendLine();
            text.Append("Bottleneck: ").AppendLine(task.Bottleneck);
            text.Append("Hardware State: ").AppendLine(
                task.ComputerRuntime != null
                    ? task.ComputerRuntime.State.ToString()
                    : "Runtime Missing");
            text.Append("Callback / Output: ")
                .Append(task.HasExecutableCallback ? "BOUND" : "MISSING")
                .Append(" / ")
                .AppendLine(task.HasPublishedOutput ? "PUBLISHED" : "WAITING");
            text.Append("Actual Rate / Runs / Skips: ")
                .Append(task.MeasuredActualRateHz, "0.0")
                .Append(" Hz / ")
                .Append(task.ExecutionCount)
                .Append(" / ")
                .AppendLine(task.SkippedExecutionCount.ToString());
            text.Append("Last Run: ")
                .Append((float)task.LastExecutionTimestamp, "0.000")
                .Append(" s | ")
                .Append(task.LastExecutionDurationSeconds * 1000f, "0.000")
                .AppendLine(" ms");
            if (task.IsFaulted || !string.IsNullOrEmpty(task.LastFailure))
            {
                text.Append("Failure: ").AppendLine(
                    string.IsNullOrEmpty(task.LastFailure)
                        ? "Faulted"
                        : task.LastFailure);
            }
        }

        private void AppendPower(V3SystemsConsoleContext context)
        {
            V3PowerDistributor power = context.Power;
            text.Append("Systems Demand / Grant: ")
                .Append(
                    power != null ? power.RequestedSystemsPower : 0f,
                    "0.0")
                .Append(" / ")
                .Append(
                    power != null ? power.GrantedSystemsPower : 0f,
                    "0.0")
                .AppendLine();
            text.Append("Propulsion Demand / Grant: ")
                .Append(
                    power != null ? power.RequestedPropulsionPower : 0f,
                    "0.0")
                .Append(" / ")
                .Append(
                    power != null ? power.GrantedPropulsionPower : 0f,
                    "0.0")
                .AppendLine();
        }

        private void AppendPart(
            string label,
            RuntimePartInstance part)
        {
            text.Append(label).Append(": ");
            if (part == null)
            {
                text.AppendLine("Unavailable");
                return;
            }

            text.Append(part.Definition.DisplayName)
                .Append(" | output ")
                .Append(part.CurrentOutput, "0.00")
                .Append(" | power ")
                .Append(part.GrantedPower, "0.0")
                .Append('/')
                .Append(part.RequestedPower, "0.0")
                .Append(" | ")
                .Append(part.CurrentTemperatureC, "0.0")
                .AppendLine(" C");
        }

        private void AppendPartsBySocket(
            V3SystemsConsoleContext context,
            string prefix,
            string label)
        {
            if (context.Runtime == null)
            {
                return;
            }

            int count = 0;
            for (int i = 0;
                 i < context.Runtime.InstalledParts.Count;
                 i++)
            {
                RuntimePartInstance part =
                    context.Runtime.InstalledParts[i];
                if (part.ParentSocketId.StartsWith(
                        prefix,
                        System.StringComparison.Ordinal))
                {
                    count++;
                    AppendPart(label + " " + count, part);
                }
            }
        }

        private RuntimeThrusterInstance FindStrongestThruster(
            V3SystemsConsoleContext context)
        {
            RuntimeThrusterInstance result = null;
            float maximum = float.MinValue;
            if (context.Runtime == null)
            {
                return null;
            }

            for (int i = 0;
                 i < context.Runtime.InstalledParts.Count;
                 i++)
            {
                if (context.Runtime.InstalledParts[i] is
                    RuntimeThrusterInstance thruster &&
                    thruster.ThrusterDefinition != null &&
                    thruster.ThrusterDefinition.MaximumOutputForceN >
                    maximum)
                {
                    result = thruster;
                    maximum =
                        thruster.ThrusterDefinition.MaximumOutputForceN;
                }
            }

            return result;
        }

        private string ResolveManufacturer(
            V3SystemsConsoleContext context)
        {
            V3SystemComputerRole role =
                Id == V3SystemsPageId.PilotInterface
                    ? V3SystemComputerRole.PilotInterface
                    : Id >= V3SystemsPageId.Drive &&
                      Id <= V3SystemsPageId.Telemetry
                        ? (V3SystemComputerRole)((int)Id - 1)
                        : V3SystemComputerRole.PilotInterface;
            V3ScheduledSoftwareTask task = context.FindTask(role);
            return task != null &&
                   task.ComputerDefinition.Manufacturer != null
                ? task.ComputerDefinition.Manufacturer.DisplayName
                : context.Runtime != null &&
                  context.Runtime.Build != null
                    ? context.Runtime.Build.ManufacturerName
                    : "Generic";
        }

        private string ResolveStatus(V3SystemsConsoleContext context)
        {
            if (context.Mainframe == null)
            {
                return context.Runtime != null
                    ? "LEGACY"
                    : "OFFLINE";
            }

            if (Id >= V3SystemsPageId.PilotInterface &&
                Id <= V3SystemsPageId.Telemetry)
            {
                V3SystemComputerRole role =
                    (V3SystemComputerRole)((int)Id - 1);
                V3ScheduledSoftwareTask task = context.FindTask(role);
                if (task == null)
                {
                    return "MISSING";
                }

                if (task.IsFaulted)
                {
                    return "FAULTED";
                }

                if (task.ExecutionCount == 0)
                {
                    return "BOOTING";
                }

                return task.IsOperational
                    ? "OPERATIONAL"
                    : "DEGRADED";
            }

            return context.Mainframe.BootState ==
                V3MainframeBootState.Ready
                    ? "OPERATIONAL"
                    : context.Mainframe.BootState
                        .ToString()
                        .ToUpperInvariant();
        }

        private static V3PilotCommand LatestCommand(
            V3SystemsConsoleContext context)
        {
            return context.Mainframe != null &&
                   context.Mainframe.Intents.HasPilotIntent
                ? context.Mainframe.Intents.LatestPilotIntent.Command
                : default;
        }
    }

    [DisallowMultipleComponent]
    public sealed class V3SystemsConsoleController : MonoBehaviour
    {
        [SerializeField] private bool showConsole;
        [SerializeField] private bool minimized;
        [Range(5f, 30f), SerializeField] private float refreshRateHz = 12f;
        [SerializeField] private V3SystemUIThemeDefinition theme;
        [SerializeField] private V3PilotInputAdapter inputSource;
        [SerializeField] private Vector2 panelSize = new Vector2(460f, 560f);

        private readonly V3SystemsConsoleContext context =
            new V3SystemsConsoleContext();
        private readonly V3SystemsPageRegistry registry =
            new V3SystemsPageRegistry();
        private readonly V3SystemsPageModel model =
            new V3SystemsPageModel();
        private int pageIndex;
        private float nextRefresh;
        private GUIStyle headerStyle;
        private GUIStyle statusStyle;
        private GUIStyle bodyStyle;
        private GUIStyle footerStyle;

        public bool ShowConsole
        {
            get => showConsole;
            set => showConsole = value;
        }

        public int CurrentPageIndex => pageIndex;
        public int PageCount => registry.Count;
        public bool IsMinimized => minimized;
        public V3SystemsPageModel CurrentPage => model;
        public V3SystemsPageRegistry Registry => registry;
        public GameObject BoundCraft => context.Craft;
        public V3PilotInputAdapter BoundInput => context.Input;

        public void BindCraft(
            GameObject craft,
            V3PilotInputAdapter input)
        {
            inputSource = input;
            context.Bind(craft, input);
            registry.Rebuild(context);
            pageIndex = Wrap(pageIndex);
            RefreshVisiblePage();
        }

        public void NextPage()
        {
            if (registry.Count == 0)
            {
                return;
            }

            pageIndex = Wrap(pageIndex + 1);
            RefreshVisiblePage();
        }

        public void PreviousPage()
        {
            if (registry.Count == 0)
            {
                return;
            }

            pageIndex = Wrap(pageIndex - 1);
            RefreshVisiblePage();
        }

        public void ToggleMinimized()
        {
            minimized = !minimized;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
            {
                showConsole = !showConsole;
            }
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame)
            {
                ToggleMinimized();
            }

            if (inputSource != null)
            {
                if (inputSource.ConsumePreviousSystemPageRequest())
                {
                    PreviousPage();
                }

                if (inputSource.ConsumeNextSystemPageRequest())
                {
                    NextPage();
                }
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh =
                    Time.unscaledTime +
                    1f / Mathf.Max(1f, refreshRateHz);
                RefreshVisiblePage();
            }
        }

        private void RefreshVisiblePage()
        {
            if (registry.Count == 0)
            {
                model.CraftName = "NO CRAFT";
                model.SystemName = "SYSTEMS CONSOLE";
                model.Manufacturer = "Generic";
                model.Status = "AWAITING CRAFT";
                model.Body =
                    "Select and assemble a valid CraftBuildDefinition.";
                return;
            }

            registry.Providers[Wrap(pageIndex)].Refresh(context, model);
        }

        private int Wrap(int value)
        {
            if (registry.Count <= 0)
            {
                return 0;
            }

            return (value % registry.Count + registry.Count) %
                registry.Count;
        }

        private void OnGUI()
        {
            if (!showConsole)
            {
                return;
            }

            EnsureStyles();
            if (minimized)
            {
                DrawMinimizedConsole();
                return;
            }

            Rect panel = new Rect(
                Screen.width - panelSize.x - 18f,
                18f,
                panelSize.x,
                Mathf.Min(panelSize.y, Screen.height - 36f));
            Color old = GUI.color;
            GUI.color =
                theme != null
                    ? theme.PanelColor
                    : new Color(0.025f, 0.045f, 0.06f, 0.94f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = old;

            GUILayout.BeginArea(
                new Rect(
                    panel.x + 18f,
                    panel.y + 14f,
                    panel.width - 36f,
                    panel.height - 28f));
            GUILayout.Label(model.CraftName, headerStyle);
            GUILayout.Label(model.SystemName, headerStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(model.Manufacturer, statusStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(model.Status, statusStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                registry.Count > 0
                    ? $"PAGE {pageIndex + 1} / {registry.Count}"
                    : "PAGE 0 / 0",
                statusStyle);
            GUILayout.Space(10f);
            GUILayout.Label(model.Body, bodyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "O  PREVIOUS    I  MINIMIZE    P  NEXT    F4  CLOSE",
                footerStyle);
            GUILayout.EndArea();
        }

        private void DrawMinimizedConsole()
        {
            const float width = 260f;
            const float height = 46f;
            Rect panel = new Rect(
                Screen.width - width - 18f,
                18f,
                width,
                height);
            Color old = GUI.color;
            GUI.color = theme != null
                ? theme.PanelColor
                : new Color(0.025f, 0.045f, 0.06f, 0.94f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = old;
            GUI.Label(
                panel,
                "I  OPEN INFO    F4  CLOSE",
                footerStyle);
        }

        private void EnsureStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            Color header =
                theme != null
                    ? theme.HeaderColor
                    : new Color(0.08f, 0.75f, 0.95f);
            Color text =
                theme != null
                    ? theme.TextColor
                    : new Color(0.82f, 0.94f, 0.98f);
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = header }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = header }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = false,
                normal = { textColor = text }
            };
            footerStyle = new GUIStyle(statusStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };
        }
    }
}
