using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public sealed class V3WorldTruthRecorder : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;
        private Vector3 previousVelocity;
        private Vector3 previousAngularVelocity;
        private Vector3 filteredAcceleration;
        private bool hasPrevious;

        public string SourceId => "world";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
            ResetHistory();
        }

        public void OnSessionStarted(V3DiagnosticSession session)
        {
            ResetHistory();
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            Rigidbody body = context != null ? context.Body : null;
            Transform craft = context != null ? context.CraftTransform : null;
            if (body == null || craft == null)
            {
                return;
            }

            if (sample.identity.externalStateDiscontinuity)
                ResetHistory();

            float delta = Mathf.Max(0.000001f, sample.identity.fixedDelta);
            Vector3 velocity = body.linearVelocity;
            Vector3 angularVelocity = body.angularVelocity;
            Vector3 acceleration = hasPrevious
                ? (velocity - previousVelocity) / delta
                : Vector3.zero;
            sample.identity.dynamicsValid = hasPrevious &&
                !sample.identity.externalStateDiscontinuity;
            Vector3 angularAcceleration = hasPrevious
                ? (angularVelocity - previousAngularVelocity) / delta
                : Vector3.zero;
            filteredAcceleration = hasPrevious
                ? Vector3.Lerp(filteredAcceleration, acceleration, 0.25f)
                : acceleration;
            V3WorldEnvironmentSample environment =
                context.EnvironmentProvider != null
                    ? context.EnvironmentProvider.CurrentSample
                    : default;
            Vector3 gravity = environment.IsValid
                ? environment.GravityVector
                : Physics.gravity;
            Vector3 gravityUp = gravity.sqrMagnitude > 0.000001f
                ? -gravity.normalized
                : Vector3.up;
            float mass = Mathf.Max(0f, body.mass);

            sample.world.position = body.position;
            sample.world.rotation = body.rotation;
            sample.world.linearVelocity = velocity;
            sample.world.localVelocity = craft.InverseTransformDirection(velocity);
            sample.world.angularVelocity = angularVelocity;
            sample.world.linearAcceleration = acceleration;
            sample.world.angularAcceleration = angularAcceleration;
            sample.world.filteredLinearAcceleration = filteredAcceleration;
            sample.world.gravityFrameVerticalVelocity = Vector3.Dot(velocity, gravityUp);
            sample.world.gravityFrameVerticalAcceleration = Vector3.Dot(acceleration, gravityUp);
            sample.world.forwardVelocity = Vector3.Dot(velocity, craft.forward);
            sample.world.lateralVelocity = Vector3.Dot(velocity, craft.right);
            sample.world.kineticEnergyJ = 0.5f * mass * velocity.sqrMagnitude;
            sample.world.sleeping = body.IsSleeping();
            sample.world.gravityVector = gravity;
            sample.world.gravityForce = gravity * mass;
            sample.world.craftPhysicsTickId = context.EnvironmentProvider != null
                ? context.EnvironmentProvider.CurrentCraftPhysicsTickId
                : context.Mainframe != null
                    ? context.Mainframe.CraftPhysicsTickId
                    : -1;
            CopyEnvironment(environment, ref sample.world);
            V3CraftAerodynamicState aerodynamics =
                context.Aerodynamics != null
                    ? context.Aerodynamics.State
                    : default;
            sample.world.relativeAirVelocity =
                aerodynamics.RelativeAirVelocity;
            sample.world.airspeedMetersPerSecond =
                aerodynamics.AirspeedMetersPerSecond;
            sample.world.machNumber = aerodynamics.MachNumber;
            sample.world.dynamicPressurePa = aerodynamics.DynamicPressurePa;
            sample.world.aerodynamicDragForce = aerodynamics.DragForce;
            sample.world.aerodynamicDownforce = aerodynamics.Downforce;
            sample.world.aerodynamicSideForce = aerodynamics.SideForce;
            sample.world.aerodynamicForce =
                aerodynamics.TotalAerodynamicForce;
            sample.world.aerodynamicTorque =
                aerodynamics.TotalAerodynamicTorque;
            sample.world.finForce = aerodynamics.FinForce;
            sample.world.finAuthority = aerodynamics.FinAuthority;
            sample.world.finSaturated = aerodynamics.FinSaturated;
            sample.forces.gravityForce = sample.world.gravityForce;

            previousVelocity = velocity;
            previousAngularVelocity = angularVelocity;
            hasPrevious = true;
        }

        public void OnSessionEnded(V3DiagnosticSession session)
        {
        }

        private void ResetHistory()
        {
            Rigidbody body = context != null ? context.Body : null;
            previousVelocity = body != null ? body.linearVelocity : Vector3.zero;
            previousAngularVelocity = body != null ? body.angularVelocity : Vector3.zero;
            filteredAcceleration = Vector3.zero;
            hasPrevious = false;
        }

        private static void CopyEnvironment(
            V3WorldEnvironmentSample source,
            ref V3WorldTruthSample destination)
        {
            destination.environmentValid = source.IsValid;
            destination.environmentFlags = (int)source.Flags;
            destination.worldRootId = source.WorldRootId;
            destination.worldProfileId = source.WorldProfileId;
            destination.worldProfileName = source.WorldProfileName;
            destination.worldConfigurationVersion =
                source.WorldConfigurationVersion;
            destination.worldPhysicsTickId = source.PhysicsTickId;
            destination.worldSimulationTime = source.SimulationTime;
            destination.environmentSamplePosition = source.WorldPosition;
            destination.rawWorldY = source.RawWorldY;
            destination.altitudeMeters = source.AltitudeMeters;
            destination.atmosphereAltitudeMeters =
                source.AtmosphereAltitudeMeters;
            destination.ambientTemperatureC = source.AmbientTemperatureC;
            destination.ambientTemperatureK = source.AmbientTemperatureK;
            destination.atmosphericPressurePa =
                source.AtmosphericPressurePa;
            destination.airDensityKgPerCubicMeter =
                source.AirDensityKgPerCubicMeter;
            destination.speedOfSoundMetersPerSecond =
                source.SpeedOfSoundMetersPerSecond;
            destination.dynamicViscosityPascalSeconds =
                source.DynamicViscosityPascalSeconds;
            destination.baseWindVelocity = source.BaseWindVelocity;
            destination.zoneWindContribution = source.ZoneWindContribution;
            destination.gustVelocity = source.GustVelocity;
            destination.turbulenceVelocity = source.TurbulenceVelocity;
            destination.localAirVelocity = source.LocalAirVelocity;
            destination.turbulenceIntensity = source.TurbulenceIntensity;
            destination.environmentalCoolingMultiplier =
                source.EnvironmentalCoolingMultiplier;
            destination.dominantZoneId = source.DominantZoneId;
            destination.dominantZonePriority = source.DominantZonePriority;
            destination.dominantZoneWeight = source.DominantZoneWeight;
            destination.activeZoneCount = source.ActiveZoneCount;
            destination.activeZone0Id = source.Zone0Id;
            destination.activeZone0Weight = source.Zone0Weight;
            destination.activeZone1Id = source.Zone1Id;
            destination.activeZone1Weight = source.Zone1Weight;
            destination.activeZone2Id = source.Zone2Id;
            destination.activeZone2Weight = source.Zone2Weight;
            destination.activeZone3Id = source.Zone3Id;
            destination.activeZone3Weight = source.Zone3Weight;
            destination.appliedZoneOperations =
                (int)source.AppliedZoneOperations;
        }
    }
}
