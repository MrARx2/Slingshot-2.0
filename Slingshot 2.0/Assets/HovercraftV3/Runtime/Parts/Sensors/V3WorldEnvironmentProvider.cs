using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public readonly struct V3EnvironmentSample
    {
        public V3EnvironmentSample(V3WorldEnvironmentSample truth)
        {
            Truth = truth;
        }

        public V3WorldEnvironmentSample Truth { get; }
        public bool IsValid => Truth.IsValid;
        public Vector3 WindVelocity => Truth.LocalAirVelocity;
        public float AirDensity => Truth.AirDensityKgPerCubicMeter;
        public float AmbientTemperatureC => Truth.AmbientTemperatureC;
    }

    public interface IV3EnvironmentProvider
    {
        V3WorldEnvironmentSample CurrentSample { get; }
        long CurrentCraftPhysicsTickId { get; }
        bool TrySampleEnvironment(
            Vector3 worldPosition,
            out V3WorldEnvironmentSample sample);
    }

    /// <summary>
    /// Craft-local bridge to the authoritative scene World Simulation. It owns
    /// the reference sample used by craft systems during the current physics
    /// tick and applies only the sampled physical gravity to the V3 Rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3WorldEnvironmentProvider :
        MonoBehaviour,
        IV3EnvironmentProvider
    {
        private V3CraftRuntime runtime;
        private Rigidbody body;
        private bool originalUseGravity;
        private bool capturedOriginalGravityState;
        private bool loggedMissingWorld;
        private V3WorldEnvironmentSample currentSample;

        public V3WorldEnvironmentSample CurrentSample => currentSample;
        public long CurrentCraftPhysicsTickId { get; private set; } = -1;
        public bool HasValidWorld => currentSample.IsValid;
        public Vector3 WindVelocity => currentSample.LocalAirVelocity;
        public float AirDensity => currentSample.AirDensityKgPerCubicMeter;
        public float AmbientTemperatureC => currentSample.AmbientTemperatureC;

        public void Initialize(V3CraftRuntime craftRuntime)
        {
            runtime = craftRuntime;
            body = runtime != null ? runtime.RootRigidbody : null;
            if (body != null && !capturedOriginalGravityState)
            {
                originalUseGravity = body.useGravity;
                capturedOriginalGravityState = true;
            }
            currentSample = default;
            CurrentCraftPhysicsTickId = -1;
            loggedMissingWorld = false;
        }

        public bool BeginPhysicsTick(
            Vector3 referencePosition,
            long craftPhysicsTickId)
        {
            CurrentCraftPhysicsTickId = craftPhysicsTickId;
            bool valid = TrySampleEnvironment(
                referencePosition,
                out currentSample);
            if (body == null)
            {
                return valid;
            }

            if (valid)
            {
                body.useGravity = false;
                body.AddForce(
                    currentSample.GravityVector,
                    ForceMode.Acceleration);
                loggedMissingWorld = false;
            }
            else
            {
                body.useGravity = capturedOriginalGravityState
                    ? originalUseGravity
                    : true;
                if (!loggedMissingWorld)
                {
                    loggedMissingWorld = true;
                    Debug.LogWarning(
                        "Hovercraft V3 could not sample an authoritative World " +
                        "Simulation. World-dependent sensors and aerodynamics " +
                        "are invalid; Unity Rigidbody gravity remains as the " +
                        "documented compatibility fallback.",
                        this);
                }
            }
            return valid;
        }

        public bool TrySampleEnvironment(
            Vector3 worldPosition,
            out V3WorldEnvironmentSample sample)
        {
            return V3WorldQueryService.TrySample(
                gameObject.scene,
                worldPosition,
                out sample);
        }

        public V3EnvironmentSample SampleEnvironment(Vector3 worldPosition)
        {
            TrySampleEnvironment(worldPosition, out var sample);
            return new V3EnvironmentSample(sample);
        }

        private void OnDisable()
        {
            if (body != null && capturedOriginalGravityState)
            {
                body.useGravity = originalUseGravity;
            }
        }
    }
}
