using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DefaultExecutionOrder(-1050)]
    [DisallowMultipleComponent]
    public sealed class V3CraftMainframe : MonoBehaviour
    {
        private readonly List<V3DirectionalSensorDeviceRuntime> sensors =
            new List<V3DirectionalSensorDeviceRuntime>(6);
        private readonly List<V3SystemComputerRuntime> computers =
            new List<V3SystemComputerRuntime>(7);
        private readonly List<IV3CraftSystemRuntime> systemRuntimes =
            new List<IV3CraftSystemRuntime>(7);
        private readonly V3ChassisObservationProvider chassisProvider =
            new V3ChassisObservationProvider();
        private readonly V3SoftwareScheduler scheduler =
            new V3SoftwareScheduler();
        private readonly V3ControlRouter controlRouter =
            new V3ControlRouter();
        private readonly List<RuntimeRotaryActuatorInstance> aeroActuators =
            new List<RuntimeRotaryActuatorInstance>(4);
        private readonly List<RuntimeAerodynamicFinInstance> aeroFins =
            new List<RuntimeAerodynamicFinInstance>(4);
        private readonly List<RuntimeCoolingModuleInstance> coolingModules =
            new List<RuntimeCoolingModuleInstance>(1);
        private readonly List<string> faultReasons =
            new List<string>(12);
        private readonly List<string> degradedReasons =
            new List<string>(12);
        private V3CraftRuntime runtime;
        private V3PowerDistributor power;
        private double elapsedTime;
        private V3PilotCommand latestRoutedCommand;
        private float remainingManagedSystemsPower;
        private float baseExternalSystemsRequested;
        private float baseExternalSystemsGranted;
        private V3ActuatorCommandRouter actuatorRouter;
        private V3GimbalController gimbalController;
        private float firmwareRequestedPower;
        private float firmwareGrantedPower;
        private float firmwareGrantFraction = 1f;
        private V3SystemRuntimeContext systemContext;
        private V3AerodynamicsSystemRuntime aerodynamicsSystem;
        private V3WorldEnvironmentProvider environment;
        private V3CraftAerodynamicsRuntime craftAerodynamics;
        private V3RuntimeDeviceRegistry deviceRegistry;
        private long physicsTickId = -1;

        public V3MainframeBootState BootState { get; private set; } =
            V3MainframeBootState.Uninitialized;
        public string FaultReason { get; private set; } = string.Empty;
        public IReadOnlyList<string> FaultReasons => faultReasons;
        public IReadOnlyList<string> DegradedReasons =>
            degradedReasons;
        public V3MainframeDefinition Definition { get; private set; }
        public V3ObservationBus Observations { get; } =
            new V3ObservationBus();
        public V3IntentBus Intents { get; } = new V3IntentBus();
        public IReadOnlyList<V3DirectionalSensorDeviceRuntime> Sensors =>
            sensors;
        public IReadOnlyList<V3SystemComputerRuntime> Computers =>
            computers;
        public IReadOnlyList<IV3CraftSystemRuntime> SystemRuntimes =>
            systemRuntimes;
        public V3SoftwareScheduler Scheduler => scheduler;
        public V3ControlRouter ControlRouter => controlRouter;
        public V3RuntimeDeviceRegistry DeviceRegistry =>
            deviceRegistry;
        public long CraftPhysicsTickId => physicsTickId;
        public double CraftSimulationTime => elapsedTime;
        public bool IsOperational =>
            BootState == V3MainframeBootState.BootingSystems ||
            BootState == V3MainframeBootState.Ready ||
            BootState == V3MainframeBootState.Degraded;

        public bool Initialize(
            V3CraftRuntime craftRuntime,
            V3PowerDistributor powerDistributor)
        {
            BootState = V3MainframeBootState.Discovering;
            FaultReason = string.Empty;
            faultReasons.Clear();
            degradedReasons.Clear();
            runtime = craftRuntime;
            power = powerDistributor;
            actuatorRouter =
                craftRuntime != null
                    ? craftRuntime.GetComponent<V3ActuatorCommandRouter>()
                    : null;
            gimbalController =
                craftRuntime != null
                    ? craftRuntime.GetComponent<V3GimbalController>()
                    : null;
            Observations.Clear();
            Intents.Clear();
            sensors.Clear();
            computers.Clear();
            systemRuntimes.Clear();
            aeroActuators.Clear();
            aeroFins.Clear();
            coolingModules.Clear();
            deviceRegistry = null;
            elapsedTime = 0d;
            physicsTickId = -1;

            if (runtime == null ||
                runtime.Build == null ||
                runtime.Build.Chassis == null)
            {
                return Fault("Missing assembled craft runtime or chassis.");
            }

            Definition = runtime.Build.Chassis.IntegratedMainframe;
            if (Definition == null)
            {
                BootState = V3MainframeBootState.Uninitialized;
                return false;
            }

            BootState = V3MainframeBootState.Validating;
            if (runtime.RootRigidbody == null)
            {
                return Fault("The Mainframe requires the root Rigidbody.");
            }

            if (runtime.InstalledParts.Count >
                Definition.MaximumRegisteredDevices)
            {
                return Fault("Installed device count exceeds Mainframe capacity.");
            }

            BootState = V3MainframeBootState.BootingSystems;
            chassisProvider.Initialize(runtime.RootRigidbody);
            controlRouter.Initialize(
                runtime.Build.Chassis.RouterProfile);
            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                if (runtime.InstalledParts[i] is
                    RuntimeRotaryActuatorInstance actuator)
                {
                    aeroActuators.Add(actuator);
                }
                else if (runtime.InstalledParts[i] is
                    RuntimeAerodynamicFinInstance fin)
                {
                    aeroFins.Add(fin);
                }
                else if (runtime.InstalledParts[i] is
                    RuntimeCoolingModuleInstance cooling)
                {
                    coolingModules.Add(cooling);
                }
            }

            environment =
                runtime.GetComponent<V3WorldEnvironmentProvider>();
            craftAerodynamics =
                runtime.GetComponent<V3CraftAerodynamicsRuntime>();
            V3DirectionalSensorDeviceRuntime[] sensorDevices =
                runtime.GetComponentsInChildren<
                    V3DirectionalSensorDeviceRuntime>(true);
            for (int i = 0; i < sensorDevices.Length; i++)
            {
                sensorDevices[i].BindMainframe(this);
                sensors.Add(sensorDevices[i]);
            }

            V3SystemComputerRuntime[] computerDevices =
                runtime.GetComponentsInChildren<
                    V3SystemComputerRuntime>(true);
            computers.AddRange(computerDevices);
            for (int i = 0; i < computers.Count; i++)
            {
                computers[i].BindMainframe(this);
            }

            systemContext = new V3SystemRuntimeContext
            {
                Mainframe = this,
                Craft = runtime,
                Observations = Observations,
                Intents = Intents,
                ControlRouter = controlRouter,
                ActuatorRouter = actuatorRouter,
                Telemetry =
                    runtime.GetComponent<V3CraftTelemetryHub>(),
                Drive = runtime.GetComponent<V3DriveController>(),
                Hover = runtime.GetComponent<V3HoverController>(),
                Vector = runtime.GetComponent<V3VectorController>(),
                Traction =
                    runtime.GetComponent<V3TractionController>(),
                Gimbal = gimbalController
            };
            systemContext.Telemetry?.SetSchedulerDriven(true);
            MonoBehaviour[] behaviours =
                runtime.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is
                    IV3CraftSystemRuntime system))
                {
                    continue;
                }

                system.Initialize(systemContext);
                systemRuntimes.Add(system);
                if (system is V3AerodynamicsSystemRuntime aero)
                {
                    aerodynamicsSystem = aero;
                }
            }

            scheduler.Initialize(runtime);

            firmwareRequestedPower =
                CalculateFirmwareRequestedPower(runtime);

            if (!ValidateMandatoryTopology())
            {
                BootState = V3MainframeBootState.Faulted;
                FaultReason = string.Join("; ", faultReasons);
                return false;
            }

            BootState = V3MainframeBootState.BootingSystems;
            return true;
        }

        public void TickObservations(
            V3PilotCommand command,
            float deltaTime)
        {
            if (!IsOperational || runtime == null)
            {
                return;
            }

            float step = Mathf.Max(0f, deltaTime);
            elapsedTime += step;
            physicsTickId++;
            environment?.BeginPhysicsTick(
                runtime.RootRigidbody.worldCenterOfMass,
                physicsTickId);
            Intents.PublishPilotIntent(command, elapsedTime);
            chassisProvider.Sample(
                Observations,
                elapsedTime,
                step);

            float physicsRate = step <= 0f ? 50f : 1f / step;
            float systemsBudget = GetSystemsBudget();
            float remaining = Mathf.Max(
                0f,
                systemsBudget - Definition.IdlePower);
            float requested = Definition.IdlePower;
            float granted = Mathf.Min(Definition.IdlePower, systemsBudget);
            scheduler.BeginFrame(Definition, physicsRate);
            requested += scheduler.RequestedPower;
            for (int rank = 0; rank <= 6; rank++)
            {
                if (rank == 3)
                {
                    for (int i = 0; i < sensors.Count; i++)
                    {
                        V3DirectionalSensorDeviceRuntime sensor =
                            sensors[i];
                        sensor.Grant(remaining, physicsRate);
                        requested += sensor.RequestedPower;
                        granted += sensor.GrantedPower;
                        remaining = Mathf.Max(
                            0f,
                            remaining - sensor.GrantedPower);
                    }

                    requested += firmwareRequestedPower;
                    firmwareGrantedPower = Mathf.Min(
                        firmwareRequestedPower,
                        remaining);
                    remaining = Mathf.Max(
                        0f,
                        remaining - firmwareGrantedPower);
                    granted += firmwareGrantedPower;
                    firmwareGrantFraction =
                        firmwareRequestedPower <= 0.0001f
                            ? 1f
                            : Mathf.Clamp01(
                                firmwareGrantedPower /
                                firmwareRequestedPower);
                    actuatorRouter?.SetFirmwareSystemsGrantFraction(
                        firmwareGrantFraction);
                    gimbalController?.SetFirmwareSystemsGrantFraction(
                        firmwareGrantFraction);
                }
                else
                {
                    float before = scheduler.GrantedPower;
                    scheduler.GrantRank(rank, ref remaining);
                    granted += scheduler.GrantedPower - before;
                }
            }

            for (int i = 0; i < sensors.Count; i++)
            {
                sensors[i].Tick(
                    Observations,
                    elapsedTime,
                    step);
            }

            remainingManagedSystemsPower = remaining;
            baseExternalSystemsRequested = requested;
            baseExternalSystemsGranted = granted;
            power?.SetExternalSystemsLoad(requested, granted);
            Observations.Publish(new V3Observation(
                V3ObservationCategory.PowerState,
                "mainframe.systems",
                elapsedTime,
                Vector3.zero,
                Vector3.zero,
                requested,
                granted,
                requested <= 0.0001f
                    ? 1f
                    : Mathf.Clamp01(granted / requested),
                granted + 0.001f >= Definition.IdlePower));
        }

        public V3PilotCommand FilterCommandByInstalledSoftware(
            V3PilotCommand command)
        {
            if (!IsOperational)
            {
                return command;
            }

            latestRoutedCommand = controlRouter.Resolve(
                command,
                scheduler,
                elapsedTime);
            return latestRoutedCommand;
        }

        public bool SubmitScheduledControls(
            V3PilotCommand command,
            float deltaTime)
        {
            if (!IsOperational ||
                actuatorRouter == null ||
                systemContext == null ||
                systemRuntimes.Count == 0)
            {
                return false;
            }

            systemContext.PhysicsCommand = command;
            actuatorRouter.BeginFrame(command.EmergencyOverload);
            ApplySystemPhysics(V3SystemComputerRole.Stabilizer, deltaTime);
            ApplySystemPhysics(V3SystemComputerRole.Drive, deltaTime);
            ApplySystemPhysics(V3SystemComputerRole.Hover, deltaTime);
            ApplySystemPhysics(V3SystemComputerRole.Traction, deltaTime);
            actuatorRouter.Resolve(deltaTime);
            TickManagedDevices(deltaTime);
            return true;
        }

        public void ResetDynamicState()
        {
            latestRoutedCommand = default;
            remainingManagedSystemsPower = 0f;
            baseExternalSystemsRequested = 0f;
            baseExternalSystemsGranted = 0f;
            firmwareRequestedPower = CalculateFirmwareRequestedPower(runtime);
            firmwareGrantedPower = 0f;
            firmwareGrantFraction = 0f;
            Observations.Clear();
            Intents.Clear();
            controlRouter.ResetDynamicState();
            scheduler.ResetDynamicState();
            if (systemContext != null)
            {
                systemContext.PilotCommand = default;
                systemContext.ScheduledCommand = default;
                systemContext.PhysicsCommand = default;
            }
            for (int i = 0; i < systemRuntimes.Count; i++)
            {
                systemRuntimes[i].ResetDynamicState();
            }
            for (int i = 0; i < aeroActuators.Count; i++)
            {
                aeroActuators[i].ResetDynamicState();
            }
            actuatorRouter?.SetFirmwareSystemsGrantFraction(0f);
            gimbalController?.SetFirmwareSystemsGrantFraction(0f);
        }

        public void TickManagedDevices(float deltaTime)
        {
            if (!IsOperational || runtime == null)
            {
                return;
            }

            bool aeroOperational = scheduler.IsRoleOperational(
                V3SystemComputerRole.Aerodynamics) &&
                firmwareGrantFraction > 0.25f;
            aerodynamicsSystem?.PrepareActuators(
                aeroOperational,
                deltaTime);

            float requested = baseExternalSystemsRequested;
            float granted = baseExternalSystemsGranted;
            for (int i = 0; i < aeroActuators.Count; i++)
            {
                RuntimeRotaryActuatorInstance actuator = aeroActuators[i];
                requested += actuator.RequestedPower;
                float actuatorGrant = Mathf.Min(
                    actuator.RequestedPower,
                    remainingManagedSystemsPower);
                remainingManagedSystemsPower = Mathf.Max(
                    0f,
                    remainingManagedSystemsPower - actuatorGrant);
                granted += actuatorGrant;
                actuator.ApplyPowerGrant(
                    actuatorGrant,
                    deltaTime);
            }

            for (int i = 0; i < coolingModules.Count; i++)
            {
                RuntimeCoolingModuleInstance cooling = coolingModules[i];
                float demand = firmwareGrantFraction > 0.1f
                    ? cooling.PreparePowerRequest()
                    : 0f;
                requested += demand;
                float coolingGrant = Mathf.Min(
                    demand,
                    remainingManagedSystemsPower);
                remainingManagedSystemsPower = Mathf.Max(
                    0f,
                    remainingManagedSystemsPower - coolingGrant);
                granted += coolingGrant;
                cooling.ApplyPowerGrant(coolingGrant, deltaTime);
            }

            float beforeTelemetry = scheduler.GrantedPower;
            scheduler.GrantRank(8, ref remainingManagedSystemsPower);
            granted += scheduler.GrantedPower - beforeTelemetry;
            systemContext.ScheduledCommand = latestRoutedCommand;
            systemContext.Time = elapsedTime;
            scheduler.Execute(deltaTime, elapsedTime);
            UpdateOperationalReadiness();
            power?.SetExternalSystemsLoad(requested, granted);
            power?.ResolveSystemsPower();

            craftAerodynamics?.ApplyReferenceSample();
            Vector3 totalFinForce = Vector3.zero;
            Vector3 totalFinTorque = Vector3.zero;
            bool finSaturated = false;
            for (int i = 0; i < aeroFins.Count; i++)
            {
                V3WorldEnvironmentSample sample = default;
                if (environment != null)
                {
                    environment.TrySampleEnvironment(
                        aeroFins[i].transform.position,
                        out sample);
                }
                aeroFins[i].ApplyAerodynamicForce(sample);
                totalFinForce +=
                    aeroFins[i].CurrentWorldAerodynamicForce;
                totalFinTorque +=
                    aeroFins[i].CurrentWorldAerodynamicTorque;
            }
            for (int i = 0; i < aeroActuators.Count; i++)
            {
                finSaturated |= aeroActuators[i].IsAtPhysicalLimit;
            }
            float dynamicPressure = craftAerodynamics != null
                ? craftAerodynamics.State.DynamicPressurePa
                : 0f;
            craftAerodynamics?.SetFinState(
                totalFinForce,
                totalFinTorque,
                dynamicPressure / (dynamicPressure + 5000f),
                finSaturated);
        }

        private void ApplySystemPhysics(
            V3SystemComputerRole role,
            float deltaTime)
        {
            if (!scheduler.IsRoleOperational(role))
            {
                return;
            }

            for (int i = 0; i < systemRuntimes.Count; i++)
            {
                if (systemRuntimes[i].Role == role)
                {
                    systemRuntimes[i].ApplyPhysicsTick(deltaTime);
                    return;
                }
            }
        }

        private float GetSystemsBudget()
        {
            if (power == null || power.Core == null)
            {
                return 0f;
            }

            return Mathf.Min(
                power.Core.SystemsChannelCeiling,
                power.Core.ContinuousOutput);
        }

        private static float CalculateFirmwareRequestedPower(
            V3CraftRuntime craft)
        {
            float demand = 0f;
            for (int i = 0; i < craft.InstalledParts.Count; i++)
            {
                V3DeviceFirmwareDefinition firmware = null;
                PartDefinition definition =
                    craft.InstalledParts[i].Definition;
                if (definition is ThrusterDefinition thruster)
                {
                    firmware = thruster.Firmware;
                }
                else if (definition is GimbalDefinition gimbal)
                {
                    firmware = gimbal.Firmware;
                }
                else if (definition is
                    V3RotaryActuatorDefinition actuator)
                {
                    firmware = actuator.Firmware;
                }
                else if (definition is
                    V3CoolingModuleDefinition cooling)
                {
                    firmware = cooling.Firmware;
                }

                if (firmware != null)
                {
                    demand += firmware.IdlePower +
                        Mathf.Min(
                            240f,
                            firmware.RequestedCommandRateHz) *
                        firmware.PowerPerCommand;
                }
            }

            return demand;
        }

        private bool Fault(string reason)
        {
            FaultReason = reason;
            faultReasons.Clear();
            faultReasons.Add(reason);
            BootState = V3MainframeBootState.Faulted;
            return false;
        }

        private bool ValidateMandatoryTopology()
        {
            faultReasons.Clear();
            if (power == null || power.Core == null)
            {
                faultReasons.Add("A valid EnergyCore is required.");
            }

            if (runtime.Build.Chassis.RouterProfile == null)
            {
                faultReasons.Add("A valid Router Profile is required.");
            }

            if (runtime.GetComponent<V3ControllerPipeline>() == null)
            {
                faultReasons.Add(
                    "The controller pipeline is missing.");
            }

            if (actuatorRouter == null)
            {
                faultReasons.Add(
                    "The actuator allocator is missing.");
            }

            deviceRegistry =
                runtime.GetComponent<V3RuntimeDeviceRegistry>();
            if (deviceRegistry == null ||
                !string.IsNullOrEmpty(
                    deviceRegistry.ValidationError))
            {
                faultReasons.Add(
                    "The runtime device registry is invalid.");
            }
            else if (deviceRegistry.Count >
                     Definition.MaximumRegisteredDevices)
            {
                faultReasons.Add(
                    $"Registered device count {deviceRegistry.Count} " +
                    "exceeds Mainframe capacity " +
                    $"{Definition.MaximumRegisteredDevices}.");
            }

            if (sensors.Count != 6)
            {
                faultReasons.Add(
                    $"Expected six physical sensors; found " +
                    $"{sensors.Count}.");
            }

            bool[] sensorDirections = new bool[6];
            for (int i = 0; i < sensors.Count; i++)
            {
                if (sensors[i].Definition == null)
                {
                    faultReasons.Add(
                        $"Sensor device {i} has no definition.");
                }
                else if (sensors[i].Firmware == null)
                {
                    faultReasons.Add(
                        $"{sensors[i].DeviceDisplayName} has no firmware.");
                }
                else if (sensors[i].Firmware.DeviceKind !=
                         V3FirmwareDeviceKind.DirectionalSensor)
                {
                    faultReasons.Add(
                        $"{sensors[i].DeviceDisplayName} has " +
                        "incompatible firmware.");
                }
                else if (string.IsNullOrWhiteSpace(
                    sensors[i].MountName))
                {
                    faultReasons.Add(
                        $"{sensors[i].DeviceDisplayName} has no mount.");
                }
                else
                {
                    int direction =
                        (int)sensors[i].Definition.Direction;
                    string expectedMount =
                        "Sensor." +
                        sensors[i].Definition.Direction;
                    if (direction < 0 ||
                        direction >= sensorDirections.Length)
                    {
                        faultReasons.Add(
                            $"Sensor device {i} has an invalid direction.");
                    }
                    else if (sensorDirections[direction])
                    {
                        faultReasons.Add(
                            "Duplicate sensor direction " +
                            sensors[i].Definition.Direction + ".");
                    }
                    else
                    {
                        sensorDirections[direction] = true;
                    }

                    if (sensors[i].MountName != expectedMount)
                    {
                        faultReasons.Add(
                            $"{sensors[i].DeviceDisplayName} is mounted " +
                            $"at '{sensors[i].MountName}', expected " +
                            $"'{expectedMount}'.");
                    }
                }
            }

            if (computers.Count != 7)
            {
                faultReasons.Add(
                    $"Expected seven computer runtimes; found " +
                    $"{computers.Count}.");
            }

            bool[] roles = new bool[7];
            for (int i = 0; i < computers.Count; i++)
            {
                int role = (int)computers[i].Role;
                if (role < 0 || role >= roles.Length)
                {
                    faultReasons.Add(
                        $"Computer {i} has an invalid role.");
                }
                else if (roles[role])
                {
                    faultReasons.Add(
                        $"Duplicate computer role " +
                        $"{computers[i].Role}.");
                }
                else
                {
                    roles[role] = true;
                }

                if (computers[i].Definition == null)
                {
                    faultReasons.Add(
                        $"Computer runtime {i} has no definition.");
                }
                else if (computers[i].InstalledSoftware == null)
                {
                    faultReasons.Add(
                        $"{computers[i].DeviceDisplayName} has no software.");
                }
                else if (computers[i].InstalledSoftware.Role !=
                         computers[i].Role)
                {
                    faultReasons.Add(
                        $"{computers[i].DeviceDisplayName} has " +
                        "incompatible software.");
                }
                else
                {
                    PartCapability expected =
                        GetExpectedComputerCapability(
                            computers[i].Role);
                    if ((computers[i].Definition
                             .ProvidedCapabilities & expected) !=
                        expected)
                    {
                        faultReasons.Add(
                            $"{computers[i].DeviceDisplayName} does not " +
                            $"provide its required {expected} capability.");
                    }
                }
            }

            if (systemRuntimes.Count != 7)
            {
                faultReasons.Add(
                    $"Expected seven executable system runtimes; found " +
                    $"{systemRuntimes.Count}.");
            }
            bool[] systemRoles = new bool[7];
            for (int i = 0; i < systemRuntimes.Count; i++)
            {
                IV3CraftSystemRuntime system = systemRuntimes[i];
                int role = (int)system.Role;
                if (role < 0 || role >= systemRoles.Length)
                {
                    faultReasons.Add(
                        $"System runtime {i} has an invalid role.");
                    continue;
                }

                if (systemRoles[role])
                {
                    faultReasons.Add(
                        $"Duplicate system runtime role {system.Role}.");
                }
                else
                {
                    systemRoles[role] = true;
                }

                V3SystemValidationResult validation =
                    system.Validate();
                if (!validation.IsValid)
                {
                    faultReasons.Add(
                        $"{system.Role} runtime is invalid: " +
                        validation.Reason);
                }
            }

            if (scheduler.Tasks.Count != 7)
            {
                faultReasons.Add(
                    $"Expected seven scheduler tasks; found " +
                    $"{scheduler.Tasks.Count}.");
            }
            if (scheduler.Tasks.Count >
                Definition.MaximumSoftwareTasks)
            {
                faultReasons.Add(
                    $"Task count {scheduler.Tasks.Count} exceeds " +
                    $"Mainframe capacity " +
                    $"{Definition.MaximumSoftwareTasks}.");
            }

            for (int i = 0; i < scheduler.Tasks.Count; i++)
            {
                if (!scheduler.Tasks[i].HasExecutableCallback)
                {
                    faultReasons.Add(
                        $"Task {scheduler.Tasks[i].TaskId} has no " +
                        "executable callback.");
                }
            }

            return faultReasons.Count == 0;
        }

        private static PartCapability GetExpectedComputerCapability(
            V3SystemComputerRole role)
        {
            switch (role)
            {
                case V3SystemComputerRole.PilotInterface:
                    return PartCapability.PilotInterface;
                case V3SystemComputerRole.Drive:
                    return PartCapability.DriveControl;
                case V3SystemComputerRole.Hover:
                    return PartCapability.HoverControl;
                case V3SystemComputerRole.Stabilizer:
                    return PartCapability.Stabilization |
                           PartCapability.VectoringControl;
                case V3SystemComputerRole.Traction:
                    return PartCapability.TractionControl;
                case V3SystemComputerRole.Aerodynamics:
                    return PartCapability.AerodynamicsControl;
                default:
                    return PartCapability.Telemetry |
                           PartCapability.ThermalTelemetry;
            }
        }

        private void UpdateOperationalReadiness()
        {
            if (BootState == V3MainframeBootState.Faulted)
            {
                return;
            }

            degradedReasons.Clear();
            for (int i = 0; i < sensors.Count; i++)
            {
                if (!sensors[i].HasSuccessfulSample)
                {
                    degradedReasons.Add(
                        $"{sensors[i].DeviceDisplayName} has not " +
                        "published a sample.");
                }
                else if (!sensors[i].IsDeviceOnline)
                {
                    degradedReasons.Add(
                        $"{sensors[i].DeviceDisplayName} is " +
                        sensors[i].HealthState + ".");
                }
            }

            for (int i = 0; i < scheduler.Tasks.Count; i++)
            {
                V3ScheduledSoftwareTask task = scheduler.Tasks[i];
                if (task.IsFaulted)
                {
                    degradedReasons.Add(
                        $"{task.TaskId} faulted: {task.LastFailure}");
                }
                else if (!task.IsOperational)
                {
                    degradedReasons.Add(
                        $"{task.TaskId} is not executing above its " +
                        "minimum rate.");
                }
            }

            if (degradedReasons.Count == 0)
            {
                BootState = V3MainframeBootState.Ready;
                FaultReason = string.Empty;
            }
            else if (elapsedTime < 1d)
            {
                BootState = V3MainframeBootState.BootingSystems;
                FaultReason = string.Join("; ", degradedReasons);
            }
            else
            {
                BootState = V3MainframeBootState.Degraded;
                FaultReason = string.Join("; ", degradedReasons);
            }
        }

    }
}
