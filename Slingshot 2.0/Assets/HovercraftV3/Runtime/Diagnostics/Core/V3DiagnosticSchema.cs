using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public static class V3DiagnosticSchema
    {
        public const int Version = V3ForensicSchemaIdentity.Version;
        public const string VersionText = V3ForensicSchemaIdentity.VersionText;
        public const uint BinaryMagic = 0x56334452; // V3DR
    }

    public enum V3DiagnosticRecordingProfile
    {
        FullForensic,
        StandardDynamics,
        Endurance,
        Custom,
        ForensicCompact
    }

    [Serializable]
    public sealed class V3DiagnosticCustomChannelSelection
    {
        [Min(1)] public int physicsTickStride = 1;
        public bool worldTruth = true;
        public bool trackTruth = true;
        public bool worldContacts = true;
        public bool sensorsAndObservations = true;
        public bool craftBelief = true;
        public bool pilotSystemsAndPower = true;
        public bool controlRouter = true;
        public bool devices = true;
        public bool forceTorqueLedger = true;
        public bool events = true;
    }

    public enum V3DiagnosticPhase
    {
        PreControl,
        PostControl,
        PostAllocation,
        PostForceApplication,
        PostPhysics
    }

    public enum V3DiagnosticDataType
    {
        Boolean,
        Integer,
        Long,
        Float,
        Double,
        Vector3,
        Quaternion,
        String,
        Enum,
        Record,
        RecordArray
    }

    public enum V3DiagnosticSamplingMode
    {
        SessionStart,
        EveryPhysicsTick,
        ReducedRate,
        EventDetail,
        SessionEnd
    }

    public enum V3DiagnosticCoordinateFrame
    {
        None,
        World,
        CraftLocal,
        Track,
        Gravity,
        DeviceLocal
    }

    public enum V3DiagnosticDataQuality
    {
        High,
        Medium,
        Low
    }

    public enum V3WorldContactState
    {
        Unknown,
        OnSurface,
        HoveringNearSurface,
        LosingSurface,
        AirborneExpected,
        AirborneUnexpected,
        Landing,
        ContactRecovery,
        OutOfTrackBounds,
        FreeFlight
    }

    public enum V3DiagnosticEventType
    {
        ManualMarker,
        Takeoff,
        Landing,
        UnexpectedAirborne,
        ExpectedJump,
        HardLanding,
        BottomingOut,
        Spin,
        Rollover,
        Crash,
        Reset,
        ReverseTravel,
        OutOfBounds,
        SectionEntry,
        SectionExit,
        ColliderDiscontinuity,
        TrackNormalDiscontinuity,
        SensorMiss,
        StaleObservation,
        SensorWorldDisagreement,
        SampleRateDrop,
        TaskUnderMinimum,
        TaskSkipped,
        TaskFault,
        MainframeDegraded,
        MainframeFaulted,
        RouterClipping,
        ControlConflict,
        ThrusterSaturation,
        PowerStarvation,
        ThermalDerating,
        GimbalLimit,
        SpringTravelLimit,
        FinLimit,
        DeviceFault,
        DroppedSamples,
        DataQuality,
        SurfaceEnvelopeDeparture,
        NearSurfaceExit,
        ProbeLoss,
        PhysicalTakeoff,
        ContactLoss,
        FreeFlightEntered,
        ContactLanding,
        SurfaceReacquired,
        MappingAmbiguity,
        GeometryColliderMismatch,
        LocalRampSpike,
        ThinColliderEdge,
        OverlappingSurfaceCandidates
    }

    [Serializable]
    public sealed class V3DiagnosticChannelDefinition
    {
        public string id;
        public string displayName;
        public string unit;
        public V3DiagnosticDataType dataType;
        public string sourceId;
        public V3DiagnosticSamplingMode samplingMode;
        public string description;
        public V3DiagnosticCoordinateFrame coordinateFrame;
        public int schemaVersion;
    }

    [Serializable]
    public sealed class V3DiagnosticChannelRegistry
    {
        [SerializeField] private List<V3DiagnosticChannelDefinition> channels =
            new List<V3DiagnosticChannelDefinition>(128);
        private readonly Dictionary<string, V3DiagnosticChannelDefinition> byId =
            new Dictionary<string, V3DiagnosticChannelDefinition>(
                StringComparer.Ordinal);

        public IReadOnlyList<V3DiagnosticChannelDefinition> Channels => channels;
        public int Count => channels.Count;

        public bool Register(V3DiagnosticChannelDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id) ||
                byId.ContainsKey(definition.id))
            {
                return false;
            }

            if (definition.schemaVersion <= 0)
            {
                definition.schemaVersion = V3DiagnosticSchema.Version;
            }

            byId.Add(definition.id, definition);
            channels.Add(definition);
            return true;
        }

        public bool TryGet(
            string id,
            out V3DiagnosticChannelDefinition definition)
        {
            return byId.TryGetValue(id ?? string.Empty, out definition);
        }

        public void Clear()
        {
            channels.Clear();
            byId.Clear();
        }

        public void RegisterCanonicalChannels()
        {
            Clear();
            RegisterCore("craft.position.world", "Craft Position", "m",
                V3DiagnosticDataType.Vector3, "world",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.velocity.world", "Craft Velocity", "m/s",
                V3DiagnosticDataType.Vector3, "world",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.acceleration.world", "Craft Acceleration", "m/s2",
                V3DiagnosticDataType.Vector3, "world",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.velocity.local", "Craft Local Velocity", "m/s",
                V3DiagnosticDataType.Vector3, "world",
                V3DiagnosticCoordinateFrame.CraftLocal);
            RegisterCore("craft.angular_velocity.world", "Angular Velocity", "rad/s",
                V3DiagnosticDataType.Vector3, "world",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.acceleration.gravity_frame.vertical",
                "Vertical Acceleration", "m/s2", V3DiagnosticDataType.Float,
                "world", V3DiagnosticCoordinateFrame.Gravity);
            RegisterCore("world.gravity.vector", "Gravity", "m/s2",
                V3DiagnosticDataType.Vector3, "world",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("world.gravity.magnitude", "Gravity Magnitude", "m/s2",
                V3DiagnosticDataType.Float, "world",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("world.environment.valid", "World Sample Valid", "bool",
                V3DiagnosticDataType.Boolean, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("world.environment.altitude", "Altitude", "m",
                V3DiagnosticDataType.Float, "world_environment",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("world.environment.temperature", "Ambient Temperature", "C",
                V3DiagnosticDataType.Float, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("world.environment.pressure", "Atmospheric Pressure", "Pa",
                V3DiagnosticDataType.Float, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("world.environment.density", "Air Density", "kg/m3",
                V3DiagnosticDataType.Float, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("world.environment.speed_of_sound", "Speed of Sound", "m/s",
                V3DiagnosticDataType.Float, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("world.environment.air_velocity", "Local Air Velocity", "m/s",
                V3DiagnosticDataType.Vector3, "world_environment",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.aerodynamics.relative_air", "Relative Air Velocity", "m/s",
                V3DiagnosticDataType.Vector3, "aerodynamics",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.aerodynamics.mach", "Mach Number", "Mach",
                V3DiagnosticDataType.Float, "aerodynamics",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("craft.aerodynamics.dynamic_pressure", "Dynamic Pressure", "Pa",
                V3DiagnosticDataType.Float, "aerodynamics",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("track.distance", "Track Distance", "m",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.lateral_offset", "Track Lateral Offset", "m",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.vertical_offset", "Track Vertical Offset", "m",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.curvature.horizontal", "Horizontal Curvature", "rad/m",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.curvature.vertical", "Vertical Curvature", "rad/m",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.bank", "Track Bank", "deg",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.slope", "Track Slope", "deg",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.true_distance", "True Surface Distance", "m",
                V3DiagnosticDataType.Float, "track_truth",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("sensor.distance_error", "Sensor Distance Error", "m",
                V3DiagnosticDataType.Float, "sensors",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("sensor.normal_error", "Sensor Normal Error", "deg",
                V3DiagnosticDataType.Float, "sensors",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("sensor.sample_age", "Sensor Sample Age", "s",
                V3DiagnosticDataType.Float, "sensors",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("system.hover.grounded", "Hover Grounded Belief", "bool",
                V3DiagnosticDataType.Boolean, "belief",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("system.hover.target_height", "Target Ride Height", "m",
                V3DiagnosticDataType.Float, "belief",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("input.command", "Processed Pilot Command", "normalized",
                V3DiagnosticDataType.Record, "pilot",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("power.requested", "Requested Power", "units",
                V3DiagnosticDataType.Float, "power",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("power.granted", "Granted Power", "units",
                V3DiagnosticDataType.Float, "power",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("force.gravity", "Gravity Force", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("force.thrusters", "Thruster Force", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("force.aerodynamics", "Aerodynamic Force", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("force.residual", "Unresolved Force", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("contact.world_state", "World Contact State", "state",
                V3DiagnosticDataType.Enum, "world_contact",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("identity.physics_tick", "Physical Tick", "tick",
                V3DiagnosticDataType.Long, "recorder",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("identity.craft_physics_tick", "Craft Physics Tick", "tick",
                V3DiagnosticDataType.Long, "mainframe",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("identity.world_physics_tick", "World Physics Tick", "tick",
                V3DiagnosticDataType.Long, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("identity.world_simulation_time", "World Simulation Time", "s",
                V3DiagnosticDataType.Double, "world_environment",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("identity.sample_index", "Sample Index", "index",
                V3DiagnosticDataType.Long, "recorder",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("identity.simulation_time", "Simulation Time", "s",
                V3DiagnosticDataType.Double, "recorder",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("craft.rotation.world", "Craft Rotation", "quaternion",
                V3DiagnosticDataType.Quaternion, "world",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.angular_acceleration.world",
                "Angular Acceleration", "rad/s2", V3DiagnosticDataType.Vector3,
                "world", V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.angular_acceleration.expected",
                "Expected Angular Acceleration", "rad/s2",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.angular_acceleration.error",
                "Angular Acceleration Error", "rad/s2",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("craft.inertia_tensor", "Inertia Tensor", "kg*m2",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("force.applied_torque", "Applied Torque", "N*m",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("contact.count", "Contact Count", "count",
                V3DiagnosticDataType.Integer, "world_contact",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("contact.impulse", "Contact Impulse", "N*s",
                V3DiagnosticDataType.Vector3, "world_contact",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("track.section_id", "Track Section", "id",
                V3DiagnosticDataType.String, "track",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("track.lap", "Lap", "index",
                V3DiagnosticDataType.Integer, "track",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("track.surface_normal", "True Surface Normal", "unit",
                V3DiagnosticDataType.Vector3, "track_truth",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("track.torsion", "Track Torsion", "deg/m",
                V3DiagnosticDataType.Float, "track",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("track.expected_airborne", "Expected Airborne", "bool",
                V3DiagnosticDataType.Boolean, "track",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("sensor.records", "Physical Sensor Records", "records",
                V3DiagnosticDataType.RecordArray, "sensors",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("observation.records", "Observation Bus Records", "records",
                V3DiagnosticDataType.RecordArray, "sensors",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("belief.mainframe_state", "Mainframe Belief State", "state",
                V3DiagnosticDataType.String, "belief",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("belief.surface_normal", "Believed Surface Normal", "unit",
                V3DiagnosticDataType.Vector3, "belief",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("belief.command_age", "Believed Command Age", "s",
                V3DiagnosticDataType.Float, "belief",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("belief.surface_state", "Surface State", "state",
                V3DiagnosticDataType.String, "belief",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("belief.surface_capture_authority",
                "Surface Capture Authority", "normalized",
                V3DiagnosticDataType.Float, "belief",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("input.raw", "Raw Pilot Input", "record",
                V3DiagnosticDataType.Record, "pilot",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("input.route", "Input Route", "route",
                V3DiagnosticDataType.String, "pilot",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("scheduler.tasks", "Scheduler Task Execution", "records",
                V3DiagnosticDataType.RecordArray, "intent_execution",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("system.requests", "System Requests", "records",
                V3DiagnosticDataType.RecordArray, "intent_execution",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("router.profile", "Router Profile", "id",
                V3DiagnosticDataType.String, "control_decision",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("router.domain_weights", "Router Domain Weights", "record",
                V3DiagnosticDataType.Record, "control_decision",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("router.allocated_force", "Router Allocated Force", "N",
                V3DiagnosticDataType.Vector3, "control_decision",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("router.allocated_torque", "Router Allocated Torque", "N*m",
                V3DiagnosticDataType.Vector3, "control_decision",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("router.residual_force", "Router Residual Force", "N",
                V3DiagnosticDataType.Vector3, "control_decision",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("router.residual_torque", "Router Residual Torque", "N*m",
                V3DiagnosticDataType.Vector3, "control_decision",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("device.records", "Physical Device Execution", "records",
                V3DiagnosticDataType.RecordArray, "device_execution",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("device.allocator_command", "Device Allocator Command",
                "normalized", V3DiagnosticDataType.Float, "device_execution",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("device.actual_force", "Device Actual Force", "N",
                V3DiagnosticDataType.Vector3, "device_execution",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("device.actual_torque", "Device Actual Torque", "N*m",
                V3DiagnosticDataType.Vector3, "device_execution",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("device.fin_angle_of_attack", "Fin Angle of Attack", "deg",
                V3DiagnosticDataType.Float, "device_execution",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("device.fin_lift_coefficient", "Fin Lift Coefficient", "Cl",
                V3DiagnosticDataType.Float, "device_execution",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("device.fin_drag_coefficient", "Fin Drag Coefficient", "Cd",
                V3DiagnosticDataType.Float, "device_execution",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("device.firmware_rate", "Device Firmware Rate", "Hz",
                V3DiagnosticDataType.Float, "device_execution",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("device.gimbal_state", "Gimbal Mechanics", "record",
                V3DiagnosticDataType.Record, "device_execution",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("device.spring_state", "Spring Mechanics", "record",
                V3DiagnosticDataType.Record, "device_execution",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("device.aerodynamic_state", "Aerodynamic Device State",
                "record", V3DiagnosticDataType.Record, "device_execution",
                V3DiagnosticCoordinateFrame.DeviceLocal);
            RegisterCore("power.systems_requested", "Systems Power Requested", "units",
                V3DiagnosticDataType.Float, "power",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("power.systems_granted", "Systems Power Granted", "units",
                V3DiagnosticDataType.Float, "power",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("power.propulsion_requested", "Propulsion Power Requested",
                "units", V3DiagnosticDataType.Float, "power",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("power.propulsion_granted", "Propulsion Power Granted",
                "units", V3DiagnosticDataType.Float, "power",
                V3DiagnosticCoordinateFrame.None);
            RegisterCore("force.contact", "Contact Force Estimate", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("torque.expected", "Expected Torque", "N*m",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("torque.residual", "Residual Torque", "N*m",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.World);
            RegisterCore("force.residual.craft", "Residual Force Craft Frame", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.CraftLocal);
            RegisterCore("force.residual.track", "Residual Force Track Frame", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.Track);
            RegisterCore("force.residual.gravity", "Residual Force Gravity Frame", "N",
                V3DiagnosticDataType.Vector3, "force_ledger",
                V3DiagnosticCoordinateFrame.Gravity);
            RegisterCore("force.to_weight", "Force To Weight Ratios", "record",
                V3DiagnosticDataType.Record, "force_ledger",
                V3DiagnosticCoordinateFrame.Gravity);
            RegisterWithMode("snapshot.craft", "Craft Snapshot", "record",
                V3DiagnosticDataType.Record, "snapshot",
                V3DiagnosticCoordinateFrame.CraftLocal,
                V3DiagnosticSamplingMode.SessionStart);
            RegisterWithMode("snapshot.world", "World Snapshot", "record",
                V3DiagnosticDataType.Record, "snapshot",
                V3DiagnosticCoordinateFrame.World,
                V3DiagnosticSamplingMode.SessionStart);
            RegisterWithMode("snapshot.track", "Track Snapshot", "record",
                V3DiagnosticDataType.Record, "track",
                V3DiagnosticCoordinateFrame.Track,
                V3DiagnosticSamplingMode.SessionStart);
            RegisterWithMode("event.records", "Diagnostic Events", "records",
                V3DiagnosticDataType.RecordArray, "events",
                V3DiagnosticCoordinateFrame.None,
                V3DiagnosticSamplingMode.EventDetail);
        }

        private void RegisterCore(
            string id,
            string displayName,
            string unit,
            V3DiagnosticDataType type,
            string source,
            V3DiagnosticCoordinateFrame frame)
        {
            RegisterWithMode(id, displayName, unit, type, source, frame,
                V3DiagnosticSamplingMode.EveryPhysicsTick);
        }

        private void RegisterWithMode(
            string id,
            string displayName,
            string unit,
            V3DiagnosticDataType type,
            string source,
            V3DiagnosticCoordinateFrame frame,
            V3DiagnosticSamplingMode samplingMode)
        {
            Register(new V3DiagnosticChannelDefinition
            {
                id = id,
                displayName = displayName,
                unit = unit,
                dataType = type,
                sourceId = source,
                samplingMode = samplingMode,
                description = displayName + " captured by Hovercraft V3 diagnostics.",
                coordinateFrame = frame,
                schemaVersion = V3DiagnosticSchema.Version
            });
        }
    }

    [Serializable]
    public struct V3DiagnosticClockSnapshot
    {
        public long physicsTick;
        public long sampleIndex;
        public double simulationSeconds;
        public double unscaledRealSeconds;
        public double sessionElapsedSeconds;
        public float fixedDeltaSeconds;
        public V3DiagnosticPhase phase;
    }

    public sealed class V3DiagnosticClock
    {
        private double sessionStartSimulation;
        private double sessionStartReal;
        private long physicsTick = -1;
        private long sampleIndex = -1;

        public V3DiagnosticClockSnapshot Current { get; private set; }

        public void Reset(double simulationSeconds, double unscaledRealSeconds)
        {
            sessionStartSimulation = simulationSeconds;
            sessionStartReal = unscaledRealSeconds;
            physicsTick = -1;
            sampleIndex = -1;
            Current = new V3DiagnosticClockSnapshot
            {
                physicsTick = -1,
                sampleIndex = -1,
                simulationSeconds = simulationSeconds,
                unscaledRealSeconds = unscaledRealSeconds,
                sessionElapsedSeconds = 0d,
                fixedDeltaSeconds = 0f,
                phase = V3DiagnosticPhase.PreControl
            };
        }

        public V3DiagnosticClockSnapshot Advance(
            double simulationSeconds,
            double unscaledRealSeconds,
            float fixedDeltaSeconds,
            V3DiagnosticPhase phase = V3DiagnosticPhase.PostPhysics,
            bool sampleCaptured = true)
        {
            physicsTick++;
            if (sampleCaptured)
            {
                sampleIndex++;
            }
            Current = new V3DiagnosticClockSnapshot
            {
                physicsTick = physicsTick,
                sampleIndex = sampleIndex,
                simulationSeconds = simulationSeconds,
                unscaledRealSeconds = unscaledRealSeconds,
                sessionElapsedSeconds =
                    Math.Max(0d, simulationSeconds - sessionStartSimulation),
                fixedDeltaSeconds = Mathf.Max(0f, fixedDeltaSeconds),
                phase = phase
            };
            return Current;
        }

        public V3DiagnosticClockSnapshot StampPhase(V3DiagnosticPhase phase)
        {
            V3DiagnosticClockSnapshot value = Current;
            value.phase = phase;
            Current = value;
            return value;
        }

        public double RealElapsedSeconds(double unscaledRealSeconds)
        {
            return Math.Max(0d, unscaledRealSeconds - sessionStartReal);
        }
    }

    [Serializable]
    public struct V3DiagnosticSampleIdentity
    {
        public string sessionId;
        public long sampleIndex;
        public long physicsTick;
        public double simulationTimestamp;
        public double unscaledRealTimestamp;
        public double sessionElapsed;
        public float fixedDelta;
        public V3DiagnosticPhase phase;
        public string controlledTestPhase;
        public string sceneName;
        public int craftRuntimeInstanceId;
        public string craftBuildStableId;
        public int trackInstanceId;
        public int attemptIndex;
        public bool externalStateDiscontinuity;
        public bool dynamicsValid;
        public string resetReason;
        public float trackDistance;
        public string sectionId;
    }

    [Serializable]
    public struct V3WorldTruthSample
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 localVelocity;
        public Vector3 angularVelocity;
        public Vector3 linearAcceleration;
        public Vector3 angularAcceleration;
        public Vector3 filteredLinearAcceleration;
        public float gravityFrameVerticalVelocity;
        public float gravityFrameVerticalAcceleration;
        public float forwardVelocity;
        public float lateralVelocity;
        public float kineticEnergyJ;
        public bool sleeping;
        public int contactCount;
        public int collisionCount;
        public float contactImpulseNs;
        public Vector3 contactImpulseVectorNs;
        public Vector3 averageContactNormal;
        public Vector3 averageContactPoint;
        public Vector3 gravityVector;
        public Vector3 gravityForce;
        public bool environmentValid;
        public int environmentFlags;
        public string worldRootId;
        public string worldProfileId;
        public string worldProfileName;
        public int worldConfigurationVersion;
        public long craftPhysicsTickId;
        public long worldPhysicsTickId;
        public double worldSimulationTime;
        public Vector3 environmentSamplePosition;
        public float rawWorldY;
        public float altitudeMeters;
        public float atmosphereAltitudeMeters;
        public float ambientTemperatureC;
        public float ambientTemperatureK;
        public float atmosphericPressurePa;
        public float airDensityKgPerCubicMeter;
        public float speedOfSoundMetersPerSecond;
        public float dynamicViscosityPascalSeconds;
        public Vector3 baseWindVelocity;
        public Vector3 zoneWindContribution;
        public Vector3 gustVelocity;
        public Vector3 turbulenceVelocity;
        public Vector3 localAirVelocity;
        public float turbulenceIntensity;
        public float environmentalCoolingMultiplier;
        public string dominantZoneId;
        public int dominantZonePriority;
        public float dominantZoneWeight;
        public int activeZoneCount;
        public string activeZone0Id;
        public float activeZone0Weight;
        public string activeZone1Id;
        public float activeZone1Weight;
        public string activeZone2Id;
        public float activeZone2Weight;
        public string activeZone3Id;
        public float activeZone3Weight;
        public int appliedZoneOperations;
        public Vector3 relativeAirVelocity;
        public float airspeedMetersPerSecond;
        public float machNumber;
        public float dynamicPressurePa;
        public Vector3 aerodynamicDragForce;
        public Vector3 aerodynamicDownforce;
        public Vector3 aerodynamicSideForce;
        public Vector3 aerodynamicForce;
        public Vector3 aerodynamicTorque;
        public Vector3 finForce;
        public float finAuthority;
        public bool finSaturated;
    }

    [Serializable]
    public struct V3TrackTruthSample
    {
        public bool mapped;
        public float mappingConfidence;
        public float distanceAlongTrack;
        public float sectionProgress;
        public int lap;
        public bool reverseTravel;
        public string sectionId;
        public string sectionType;
        public Vector3 nearestCenterlinePoint;
        public Vector3 tangent;
        public Vector3 normal;
        public Vector3 right;
        public float signedLateralOffset;
        public float signedVerticalOffset;
        public float trueSurfaceDistance;
        public Vector3 trueSurfacePoint;
        public Vector3 trueSurfaceNormal;
        public int trueSurfaceColliderId;
        public float bankDegrees;
        public float slopeDegrees;
        public float horizontalCurvature;
        public float verticalCurvature;
        public float torsionDegreesPerMeter;
        public bool insideTrackBounds;
        public bool expectedAirborne;
    }

    [Serializable]
    public struct V3SensorDiagnosticRecord
    {
        public string sensorId;
        public string direction;
        public Vector3 mountPosition;
        public Quaternion mountRotation;
        public Vector3 forward;
        public float requestedRateHz;
        public float grantedRateHz;
        public float measuredRateHz;
        public float minimumUsefulRateHz;
        public float requestedPower;
        public float grantedPower;
        public string health;
        public string fault;
        public bool hasSample;
        public bool hit;
        public float measuredDistance;
        public Vector3 measuredPoint;
        public Vector3 measuredNormal;
        public int colliderId;
        public Vector3 relativeVelocity;
        public Vector3 airflow;
        public float airDensity;
        public float ambientTemperatureC;
        public float atmosphericPressurePa;
        public float speedOfSoundMetersPerSecond;
        public Vector3 gravityVector;
        public Vector3 localAirVelocity;
        public bool environmentValid;
        public long craftPhysicsTickId;
        public long environmentPhysicsTickId;
        public string environmentProfileId;
        public string dominantZoneId;
        public double timestamp;
        public float ageSeconds;
        public float confidence;
        public float trueDistance;
        public bool hasWorldTruth;
        public bool trueHit;
        public float distanceError;
        public float normalAngleError;
        public V3DiagnosticDataQuality comparisonQuality;
        public string unavailableReason;
    }

    [Serializable]
    public struct V3TaskDiagnosticRecord
    {
        public string taskId;
        public string role;
        public string computerId;
        public string softwareId;
        public float requestedRateHz;
        public float capacityLimitedRateHz;
        public float powerLimitedRateHz;
        public float grantedRateHz;
        public float measuredRateHz;
        public float minimumUsefulRateHz;
        public long executionCount;
        public long skippedCount;
        public double lastExecutionTimestamp;
        public float lastExecutionDurationSeconds;
        public float accumulatorSeconds;
        public float requestedPower;
        public float grantedPower;
        public string bottleneck;
        public bool faulted;
        public string fault;
        public bool publishedThisTick;
    }

    [Serializable]
    public struct V3ObservationDiagnosticRecord
    {
        public string category;
        public string sourceId;
        public double timestamp;
        public float ageSeconds;
        public float confidence;
        public Vector3 primaryVector;
        public Vector3 secondaryVector;
        public float primaryScalar;
        public float secondaryScalar;
        public bool state;
        public int payloadHash;
    }

    [Serializable]
    public struct V3SystemRequestDiagnosticRecord
    {
        public string requestId;
        public string sourceSystemId;
        public string domain;
        public string requestType;
        public string frame;
        public Vector3 requestedForce;
        public Vector3 requestedTorque;
        public float requestedAngleDegrees;
        public float requestedState;
        public int eligibleDevices;
        public float confidence;
        public float authority;
        public float maximumContribution;
        public int priority;
        public double timestamp;
        public float validitySeconds;
        public bool valid;
        public string reason;
    }

    [Serializable]
    public struct V3RouterDomainWeightSample
    {
        public float propulsion;
        public float braking;
        public float steering;
        public float strafe;
        public float rideHeight;
        public float attitudeStability;
        public float traction;
        public float aerodynamics;
        public float recovery;
        public float manualPilot;
    }

    [Serializable]
    public struct V3ControlDecisionDiagnosticSample
    {
        public string activeProfileId;
        public string activeProfileName;
        public V3RouterDomainWeightSample domainWeights;
        public int incomingRequestCount;
        public int rejectedRequestCount;
        public int conflictCount;
        public int hardConstraintCount;
        public Vector3 resolvedRequestedForce;
        public Vector3 resolvedRequestedTorque;
        public Vector3 allocatedForce;
        public Vector3 allocatedTorque;
        public Vector3 residualForce;
        public Vector3 residualTorque;
        public float grantedAuthority;
        public bool clippedOrSaturated;
        public string reason;
    }

    [Serializable]
    public struct V3DeviceDiagnosticRecord
    {
        public string deviceId;
        public string partId;
        public string displayName;
        public string role;
        public string manufacturer;
        public string socketId;
        public string connectorId;
        public string deviceType;
        public float requestedOutput;
        public Vector3 requestedDirection;
        public Vector3 requestedForce;
        public Vector3 requestedTorqueContribution;
        public Quaternion requestedOrientation;
        public int sourceChannelMask;
        public float allocatorCommand;
        public float authorityCap;
        public float allocationPercentage;
        public bool selected;
        public string limitingReason;
        public float routerDriveRequest;
        public float routerHoverRequest;
        public float routerStabilizationRequest;
        public float routerVectoringRequest;
        public float routerManualRequest;
        public float routerCombinedRequest;
        public float actualOutput;
        public float requestedPower;
        public float grantedPower;
        public float grantFraction;
        public string firmwareId;
        public float firmwareRequestedRateHz;
        public float firmwareGrantedRateHz;
        public float firmwareMinimumRateHz;
        public bool firmwareSafeState;
        public float actualForceMagnitudeN;
        public Vector3 actualForce;
        public Vector3 forceApplicationPoint;
        public Vector3 torqueContribution;
        public float gimbalPitchDegrees;
        public float gimbalYawDegrees;
        public float gimbalTargetPitchDegrees;
        public float gimbalTargetYawDegrees;
        public float gimbalPitchVelocityDegreesPerSecond;
        public float gimbalYawVelocityDegreesPerSecond;
        public float gimbalActuatorTorqueNm;
        public bool gimbalAtLimit;
        public float springDisplacementMeters;
        public float springVelocityMetersPerSecond;
        public float springEndpointLoadN;
        public float springForceN;
        public float springPotentialEnergyJ;
        public float springDampingLossW;
        public bool springAtCompressionLimit;
        public bool springAtExtensionLimit;
        public float rotaryAngleDegrees;
        public float rotaryTargetAngleDegrees;
        public bool rotaryAtLimit;
        public float finForceN;
        public Vector3 finRelativeAirflow;
        public float finAirspeedMetersPerSecond;
        public float finAngleOfAttackDegrees;
        public float finLiftCoefficient;
        public float finDragCoefficient;
        public bool finStalled;
        public bool finReverseFlow;
        public Vector3 finLiftForce;
        public Vector3 finDragForce;
        public bool finStructuralWarning;
        public bool finStructurallyLimited;
        public string finStructuralLimitReason;
        public float temperatureC;
        public float thermalLimit;
        public bool thermallyDerated;
        public bool faulted;
    }

    [Serializable]
    public struct V3CraftBeliefSample
    {
        public string mainframeState;
        public string mainframeFault;
        public int connectedDevices;
        public int connectedComputers;
        public int activeTasks;
        public string inputAuthority;
        public string inputRoute;
        public float commandAgeSeconds;
        public bool hoverGrounded;
        public string surfaceState;
        public bool surfaceDetected;
        public bool nearSurface;
        public bool physicalContact;
        public int primaryProbeCount;
        public int fallbackProbeCount;
        public float surfaceConfidence;
        public float surfaceAgeSeconds;
        public float captureAuthorityMultiplier;
        public float distanceAuthorityMultiplier;
        public string captureLimitReason;
        public float bottomRequestBeforeAuthorityLimit;
        public float bottomRequestAfterAuthorityLimit;
        public float roofRequestBeforeAuthorityLimit;
        public float roofRequestAfterAuthorityLimit;
        public int hoverGroundedProbeCount;
        public string hoverConfigurationId;
        public int hoverConfigurationVersion;
        public float targetRideHeight;
        public float minimumHoverClearance;
        public float maximumHoverRange;
        public float believedSurfaceDistance;
        public float believedSurfaceSeparationVelocity;
        public Vector3 believedSurfaceNormal;
        public Vector3 desiredSurfaceAlignmentAcceleration;
        public float surfaceTrackingAcceleration;
        public float roofCaptureAcceleration;
        public float roofCaptureOutput;
        public float gripBreakerAmount;
        public Vector3 relativeAirflow;
    }

    [Serializable]
    public struct V3PilotDiagnosticSample
    {
        public string inputDevice;
        public float rawThrottle;
        public float rawStrafe;
        public Vector2 rawMouseLook;
        public Vector2 rawGamepadLook;
        public bool rawLift;
        public bool rawDownforce;
        public bool rawGripBreaker;
        public bool rawEmergencyOverload;
        public double rawInputTimestamp;
        public V3PilotCommand processedCommand;
        public string route;
        public string authority;
        public bool intentPublished;
        public double commandTimestamp;
        public double intentTimestamp;
        public float commandAgeSeconds;
    }

    [Serializable]
    public struct V3PowerDiagnosticSample
    {
        public string allocationMode;
        public float requestedSystemsPower;
        public float grantedSystemsPower;
        public float requestedPropulsionPower;
        public float grantedPropulsionPower;
        public float availablePropulsionPower;
        public bool systemsPowerLimited;
        public bool propulsionPowerLimited;
    }

    [Serializable]
    public struct V3ForceTorqueDiagnosticSample
    {
        public Vector3 gravityForce;
        public Vector3 propulsionForce;
        public Vector3 brakingForce;
        public Vector3 hoverForce;
        public Vector3 roofForce;
        public Vector3 lateralForce;
        public Vector3 aerodynamicForce;
        public Vector3 aerodynamicLiftForce;
        public Vector3 aerodynamicDragForce;
        public Vector3 aerodynamicSideForce;
        public Vector3 contactForceEstimate;
        public Vector3 expectedNetForce;
        public Vector3 observedNetForce;
        public Vector3 residualForce;
        public Vector3 thrusterTorque;
        public Vector3 aerodynamicTorque;
        public Vector3 contactTorqueEstimate;
        public Vector3 expectedTorque;
        public Vector3 observedTorqueEstimate;
        public Vector3 residualTorque;
        public Vector3 inertiaTensor;
        public Quaternion inertiaTensorRotation;
        public Vector3 appliedTorque;
        public Vector3 measuredAngularAcceleration;
        public Vector3 expectedAngularAcceleration;
        public Vector3 angularAccelerationError;
        public Vector3 expectedForceCraftFrame;
        public Vector3 observedForceCraftFrame;
        public Vector3 residualForceCraftFrame;
        public Vector3 expectedForceTrackFrame;
        public Vector3 observedForceTrackFrame;
        public Vector3 residualForceTrackFrame;
        public Vector3 expectedForceGravityFrame;
        public Vector3 observedForceGravityFrame;
        public Vector3 residualForceGravityFrame;
        public Vector3 expectedTorqueCraftFrame;
        public Vector3 residualTorqueCraftFrame;
        public Vector3 expectedTorqueTrackFrame;
        public Vector3 residualTorqueTrackFrame;
        public Vector3 expectedTorqueGravityFrame;
        public Vector3 residualTorqueGravityFrame;
        public float hoverToWeight;
        public float aeroLiftToWeight;
        public float upwardThrusterToWeight;
        public float totalUpwardToWeight;
        public float netVerticalToWeight;
    }

    [Serializable]
    public struct V3DiagnosticSample
    {
        public V3DiagnosticSampleIdentity identity;
        public V3WorldTruthSample world;
        public V3TrackTruthSample track;
        public V3CraftBeliefSample belief;
        public V3PilotDiagnosticSample pilot;
        public V3PowerDiagnosticSample power;
        public V3ControlDecisionDiagnosticSample router;
        public V3ForceTorqueDiagnosticSample forces;
        public V3WorldContactState worldContactState;
        public V3SensorDiagnosticRecord[] sensors;
        public int sensorCount;
        public V3TaskDiagnosticRecord[] tasks;
        public int taskCount;
        public V3ObservationDiagnosticRecord[] observations;
        public int observationCount;
        public V3SystemRequestDiagnosticRecord[] requests;
        public int requestCount;
        public V3DeviceDiagnosticRecord[] devices;
        public int deviceCount;

        public void Prepare(
            int sensorCapacity,
            int taskCapacity,
            int observationCapacity,
            int requestCapacity,
            int deviceCapacity)
        {
            sensors = Ensure(sensors, sensorCapacity);
            tasks = Ensure(tasks, taskCapacity);
            observations = Ensure(observations, observationCapacity);
            requests = Ensure(requests, requestCapacity);
            devices = Ensure(devices, deviceCapacity);
            sensorCount = 0;
            taskCount = 0;
            observationCount = 0;
            requestCount = 0;
            deviceCount = 0;
        }

        private static T[] Ensure<T>(T[] values, int capacity)
        {
            capacity = Mathf.Max(0, capacity);
            return values != null && values.Length == capacity
                ? values
                : new T[capacity];
        }
    }

    [Serializable]
    public sealed class V3DiagnosticEvent
    {
        public string eventId;
        public V3DiagnosticEventType type;
        public int severity;
        public double timestamp;
        public long tick;
        public long sampleIndex;
        public int attemptIndex;
        public string resetReason;
        public float trackDistance;
        public string sectionId;
        public Vector3 craftPosition;
        public float speedMetersPerSecond;
        public V3WorldContactState worldState;
        public string craftBeliefState;
        public string possibleContributingDomains;
        public long preEventStartSample;
        public long postEventEndSample;
        public string notes;
        public bool manual;
    }

    public interface IV3DiagnosticSource
    {
        string SourceId { get; }
        int SchemaVersion { get; }
        void Initialize(V3DiagnosticContext context);
        void Capture(ref V3DiagnosticSample sample);
        void OnSessionStarted(V3DiagnosticSession session);
        void OnSessionEnded(V3DiagnosticSession session);
    }
}
