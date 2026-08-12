using System.Collections.Generic;
using TrackGeneration;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    public static class V3CraftDiagnosticSnapshotExporter
    {
        public static V3DiagnosticCraftSnapshot Capture(V3DiagnosticContext context)
        {
            V3CraftRuntime craft = context != null ? context.Craft : null;
            Rigidbody body = context != null ? context.Body : null;
            V3CraftMainframe mainframe = context != null ? context.Mainframe : null;
            CraftBuildDefinition build = craft != null ? craft.Build : null;
            ChassisDefinition chassis = build != null ? build.Chassis : null;
            V3HoverConfiguration hover = build != null
                ? build.HoverConfiguration : null;
            IReadOnlyList<RuntimePartInstance> parts =
                craft != null ? craft.InstalledParts : null;
            int count = parts != null ? parts.Count : 0;
            var partSnapshots = new V3DiagnosticInstalledPartSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                RuntimePartInstance part = parts[i];
                PartDefinition definition = part != null ? part.Definition : null;
                partSnapshots[i] = new V3DiagnosticInstalledPartSnapshot
                {
                    stableId = definition != null ? definition.StableId : string.Empty,
                    displayName = definition != null ? definition.DisplayName : string.Empty,
                    manufacturer = definition != null && definition.Manufacturer != null
                        ? definition.Manufacturer.DisplayName
                        : string.Empty,
                    role = definition != null ? definition.Role.ToString() : string.Empty,
                    socketId = part != null ? part.ParentSocketId : string.Empty,
                    massKg = definition != null ? definition.Physical.massKg : 0f,
                    localCenterOfMass = definition != null
                        ? definition.Physical.localCenterOfMass
                        : Vector3.zero,
                    requiresPower = definition != null && definition.RequiresPower,
                    requiresData = definition != null && definition.RequiresData
                };
            }

            return new V3DiagnosticCraftSnapshot
            {
                buildStableId = build != null ? build.StableId : string.Empty,
                buildAssetGuid = build != null ? build.BuildAssetGuid : string.Empty,
                buildOwnership = build != null ? build.Ownership.ToString() : string.Empty,
                trackTestPreset = BuildPreset(build),
                controlPath = craft != null &&
                    craft.GetComponent<V3ControllerPipeline>() != null
                        ? craft.GetComponent<V3ControllerPipeline>()
                            .ActiveControlPath.ToString()
                        : V3ControlExecutionPath.None.ToString(),
                buildDisplayName = build != null ? build.DisplayName : string.Empty,
                manufacturer = build != null ? build.ManufacturerName : string.Empty,
                vehicleClass = build != null ? build.VehicleClass : string.Empty,
                layout = build != null ? build.LayoutName : string.Empty,
                chassisStableId = chassis != null ? chassis.StableId : string.Empty,
                chassisDisplayName = chassis != null ? chassis.DisplayName : string.Empty,
                calculatedMassKg = craft != null ? craft.CalculatedMassKg : 0f,
                calculatedCenterOfMass = craft != null
                    ? craft.CalculatedCenterOfMass
                    : Vector3.zero,
                rigidbodyCenterOfMass = body != null ? body.centerOfMass : Vector3.zero,
                rigidbodyInertiaTensor = body != null ? body.inertiaTensor : Vector3.zero,
                rigidbodyInertiaTensorRotation = body != null
                    ? body.inertiaTensorRotation
                    : Quaternion.identity,
                automaticCenterOfMass = body != null && body.automaticCenterOfMass,
                automaticInertiaTensor = body != null && body.automaticInertiaTensor,
                installedPartCount = count,
                installedParts = partSnapshots,
                sensorCount = mainframe != null ? mainframe.Sensors.Count : 0,
                computerCount = mainframe != null ? mainframe.Computers.Count : 0,
                schedulerTaskCount = mainframe != null
                    ? mainframe.Scheduler.Tasks.Count
                    : 0,
                mainframeDefinition = mainframe != null && mainframe.Definition != null
                    ? mainframe.Definition.name
                    : string.Empty,
                routerProfile = chassis != null && chassis.RouterProfile != null
                    ? chassis.RouterProfile.name
                    : string.Empty,
                hoverConfigurationId = hover != null ? hover.StableId : string.Empty,
                hoverConfigurationVersion = hover != null
                    ? hover.ConfigurationVersion : 0,
                targetHoverHeight = hover != null ? hover.TargetHoverHeight : 0f,
                minimumHoverClearance = hover != null ? hover.MinimumClearance : 0f,
                maximumHoverRange = hover != null
                    ? hover.MaximumOperationalRange : 0f,
                nearHoverRange = hover != null ? hover.NearHoverRange : 0f,
                captureRange = hover != null ? hover.CaptureRange : 0f,
                fallbackRange = hover != null ? hover.FallbackRange : 0f,
                automaticHoverAuthorityLimit = hover != null
                    ? hover.AutomaticHoverAuthorityLimit : 0f,
                automaticRoofAuthorityLimit = hover != null
                    ? hover.AutomaticRoofAuthorityLimit : 0f,
                manualAuthorityLimit = hover != null
                    ? hover.ManualAuthorityLimit : 0f,
                hoverStrength = hover != null ? hover.HoverStrength : 0f,
                hoverDamping = hover != null ? hover.HoverDamping : 0f,
                gravityCompensationEnabled = hover != null &&
                    hover.GravityCompensationEnabled,
                gravityCompensationMultiplier = hover != null
                    ? hover.GravityCompensationMultiplier : 0f,
                maximumCompressionMeters = hover != null
                    ? hover.CompressionLimit : 0f,
                maximumExtensionMeters = hover != null
                    ? hover.ExtensionLimit : 0f,
                surfaceCurvatureFeedForward = hover != null
                    ? hover.SurfaceCurvatureFeedForward : 0f,
                surfaceTrackingResponse = hover != null
                    ? hover.SurfaceTrackingResponse : 0f,
                maximumSurfaceTrackingAcceleration = hover != null
                    ? hover.MaximumSurfaceTrackingAcceleration : 0f,
                roofCaptureStrength = hover != null
                    ? hover.RoofCaptureStrength : 0f,
                roofCaptureDamping = hover != null
                    ? hover.RoofCaptureDamping : 0f,
                maximumRoofCaptureAcceleration = hover != null
                    ? hover.MaximumRoofCaptureAcceleration : 0f,
                roofCaptureDeadband = hover != null
                    ? hover.RoofCaptureDeadband : 0f
            };
        }

        private static string BuildPreset(CraftBuildDefinition build)
        {
            if (build == null) return "Unassigned";
            if (build.StableId == "build.hovercraft.baseline.01")
                return "TrackTest_Baseline";
            if (build.StableId == "build.apex.v3.systems_integration.balanced.01")
                return "TrackTest_PrimarySystems";
            return build.Ownership == V3BuildOwnership.AuthoredVariant
                ? "TrackTest_SelectedAuthoredBuild"
                : "TrackTest_Custom";
        }
    }

    public static class V3WorldDiagnosticSnapshotExporter
    {
        public static V3DiagnosticWorldSnapshot Capture(V3DiagnosticContext context)
        {
            TrackGenerator track = context != null ? context.TrackGenerator : null;
            V3WorldSimulationRoot world = null;
            V3WorldQueryStatus status = context != null && context.Craft != null
                ? V3WorldQueryService.Resolve(
                    context.Craft.gameObject.scene,
                    out world)
                : V3WorldQueryStatus.MissingWorld;
            V3WorldProfile profile = status == V3WorldQueryStatus.Valid
                ? world.Profile
                : null;
            return new V3DiagnosticWorldSnapshot
            {
                sceneName = context != null ? context.SceneName : string.Empty,
                gravity = Physics.gravity,
                fixedDeltaTime = Time.fixedDeltaTime,
                maximumDeltaTime = Time.maximumDeltaTime,
                defaultSolverIterations = Physics.defaultSolverIterations,
                defaultSolverVelocityIterations =
                    Physics.defaultSolverVelocityIterations,
                defaultContactOffset = Physics.defaultContactOffset,
                bounceThreshold = Physics.bounceThreshold,
                sleepThreshold = Physics.sleepThreshold,
                physicsSimulationMode = Physics.simulationMode.ToString(),
                trackInstanceId = track != null
                    ? track.GetEntityId().GetHashCode()
                    : 0,
                trackGenerationRevision = track != null
                    ? track.GenerationRevision
                    : 0,
                trackSectionCount = track != null && track.CurrentMacroSections != null
                    ? track.CurrentMacroSections.Count
                    : 0,
                trackRootName = track != null && track.TrackRoot != null
                    ? track.TrackRoot.name
                    : string.Empty,
                hasWorldSimulation = profile != null,
                worldRootId = world != null ? world.StableRootId : string.Empty,
                worldProfileId = profile != null ? profile.StableId : string.Empty,
                worldProfileName = profile != null
                    ? profile.DisplayName : string.Empty,
                worldConfigurationVersion = profile != null
                    ? profile.ConfigurationVersion : 0,
                referenceWorldY = profile != null
                    ? profile.ReferenceWorldY : 0f,
                seaLevelTemperatureC = profile != null
                    ? profile.SeaLevelTemperatureC : 0f,
                seaLevelPressurePa = profile != null
                    ? profile.SeaLevelPressurePa : 0f,
                seaLevelDensity = profile != null
                    ? profile.DerivedSeaLevelDensity : 0f,
                atmosphereMode = profile != null
                    ? profile.AtmosphereMode.ToString() : string.Empty,
                lapseRateKPerMeter = profile != null
                    ? profile.TemperatureLapseRateKPerMeter : 0f,
                profileGravity = profile != null
                    ? profile.GravityVector : Vector3.zero,
                globalWind = profile != null
                    ? profile.GlobalWindDirection * profile.GlobalWindSpeed
                    : Vector3.zero,
                gustStrength = profile != null ? profile.GustStrength : 0f,
                turbulenceStrength = profile != null
                    ? profile.TurbulenceStrength : 0f,
                coolingMultiplier = profile != null
                    ? profile.BaselineCoolingMultiplier : 0f,
                windSeed = profile != null
                    ? profile.DeterministicWindSeed : 0,
                environmentZoneCount = world != null && world.ZoneRegistry != null
                    ? world.ZoneRegistry.Count : 0
            };
        }
    }
}
