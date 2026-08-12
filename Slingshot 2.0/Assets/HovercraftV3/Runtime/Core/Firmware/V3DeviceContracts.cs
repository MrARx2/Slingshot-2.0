using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public interface IV3ForceGeneratorDevice
    {
        float MaximumForwardForceN { get; }
        Vector3 WorldForceOrigin { get; }
        Vector3 WorldForceDirection { get; }
        float CurrentAppliedForceN { get; }
    }

    public interface IV3OrientationActuatorDevice
    {
        float CurrentAngleDegrees { get; }
        float TargetAngleDegrees { get; }
        bool IsAtPhysicalLimit { get; }
    }

    public interface IV3AerodynamicSurfaceDevice
    {
        float CurrentAerodynamicForceN { get; }
        Vector3 CurrentWorldAerodynamicForce { get; }
    }

    public interface IV3CoolingDevice
    {
        float CurrentHeatRemovalPerSecond { get; }
    }

    public sealed class V3ThrusterFirmwareRuntime
    {
        private readonly RuntimeThrusterInstance device;
        private readonly V3DeviceFirmwareDefinition definition;
        private float heldOutput;
        private bool heldOverload;
        private float commandAge;
        private float accumulator;
        private float systemsGrantFraction = 1f;

        public V3ThrusterFirmwareRuntime(RuntimeThrusterInstance value)
        {
            device = value;
            definition = value != null
                ? value.ThrusterDefinition?.Firmware
                : null;
        }

        public RuntimeThrusterInstance Device => device;
        public float RequestedRateHz =>
            definition != null ? definition.RequestedCommandRateHz : 0f;
        public float MinimumRateHz =>
            definition != null ? definition.MinimumCommandRateHz : 0f;
        public string FirmwareId =>
            definition != null ? definition.StableId : string.Empty;
        public float GrantedRateHz { get; private set; }
        public bool IsSafeState { get; private set; }
        public void SetSystemsGrantFraction(float value)
        {
            systemsGrantFraction = Mathf.Clamp01(value);
        }

        public float Prepare(
            float requestedOutput,
            float deltaTime,
            bool overload)
        {
            if (device == null)
            {
                return 0f;
            }

            if (definition == null)
            {
                return device.PreparePowerRequest(
                    requestedOutput,
                    deltaTime,
                    overload);
            }

            float physicsRate = deltaTime <= 0f ? 50f : 1f / deltaTime;
            GrantedRateHz = Mathf.Min(
                definition.RequestedCommandRateHz *
                    systemsGrantFraction,
                physicsRate);
            accumulator += Mathf.Max(0f, deltaTime);
            commandAge += Mathf.Max(0f, deltaTime);
            float interval = 1f / Mathf.Max(1f, GrantedRateHz);
            if (accumulator + 0.000001f >= interval)
            {
                accumulator = Mathf.Min(accumulator - interval, interval);
                heldOutput = requestedOutput;
                heldOverload = overload;
                commandAge = 0f;
            }

            IsSafeState = commandAge > definition.CommandTimeoutSeconds ||
                GrantedRateHz + 0.001f < definition.MinimumCommandRateHz;
            return device.PreparePowerRequest(
                IsSafeState ? 0f : heldOutput,
                deltaTime,
                !IsSafeState && heldOverload);
        }

        public void Apply(float grantedPower, float deltaTime)
        {
            device?.ApplyPowerGrant(grantedPower, deltaTime);
        }
    }

    public sealed class V3GimbalFirmwareRuntime
    {
        private readonly RuntimeGimbalInstance device;
        private readonly V3DeviceFirmwareDefinition definition;
        private float heldPitch;
        private float heldYaw;
        private float commandAge;
        private float accumulator;
        private float systemsGrantFraction = 1f;

        public V3GimbalFirmwareRuntime(RuntimeGimbalInstance value)
        {
            device = value;
            definition = value != null
                ? value.GimbalDefinition?.Firmware
                : null;
        }

        public RuntimeGimbalInstance Device => device;
        public bool IsSafeState { get; private set; }
        public void SetSystemsGrantFraction(float value)
        {
            systemsGrantFraction = Mathf.Clamp01(value);
        }

        public float Prepare(float pitch, float yaw, float deltaTime)
        {
            if (device == null)
            {
                return 0f;
            }

            if (definition == null)
            {
                return device.PreparePowerRequest(pitch, yaw, deltaTime);
            }

            float physicsRate = deltaTime <= 0f ? 50f : 1f / deltaTime;
            float rate = Mathf.Min(
                definition.RequestedCommandRateHz *
                    systemsGrantFraction,
                physicsRate);
            accumulator += Mathf.Max(0f, deltaTime);
            commandAge += Mathf.Max(0f, deltaTime);
            if (accumulator + 0.000001f >= 1f / Mathf.Max(1f, rate))
            {
                accumulator = 0f;
                heldPitch = pitch;
                heldYaw = yaw;
                commandAge = 0f;
            }

            IsSafeState = commandAge > definition.CommandTimeoutSeconds ||
                rate + 0.001f < definition.MinimumCommandRateHz;
            return device.PreparePowerRequest(
                IsSafeState ? 0f : heldPitch,
                IsSafeState ? 0f : heldYaw,
                deltaTime);
        }

        public void Apply(float grantedPower, float deltaTime)
        {
            device?.ApplyPowerGrant(grantedPower, deltaTime);
        }
    }
}
