using Lunarlight.Hovercraft.V3.Diagnostics;
using NUnit.Framework;
using TrackGeneration;
using TrackGeneration.Macro;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3DiagnosticTruthAndSensorTests
    {
        private const string SystemsBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset";
        private GameObject assemblerHost;
        private GameObject trackHost;
        private GameObject worldHost;
        private V3WorldProfile worldProfile;

        [TearDown]
        public void TearDown()
        {
            if (assemblerHost != null) Object.DestroyImmediate(assemblerHost);
            if (trackHost != null) Object.DestroyImmediate(trackHost);
            if (worldHost != null) Object.DestroyImmediate(worldHost);
            if (worldProfile != null) Object.DestroyImmediate(worldProfile);
        }

        [Test]
        public void WorldTruth_DerivesAccelerationFromCanonicalFixedDelta()
        {
            V3DiagnosticContext context = AssembleContext(null);
            var source = new V3WorldTruthRecorder();
            source.Initialize(context);
            source.OnSessionStarted(null);
            V3DiagnosticSample first = PreparedSample();
            first.identity.fixedDelta = 0.02f;
            context.Body.linearVelocity = Vector3.zero;
            source.Capture(ref first);

            V3DiagnosticSample second = PreparedSample();
            second.identity.fixedDelta = 0.02f;
            context.Body.linearVelocity = Vector3.forward * 2f;
            source.Capture(ref second);

            Assert.That(second.world.linearAcceleration.z,
                Is.EqualTo(100f).Within(0.001f));
            Assert.That(second.world.gravityVector, Is.EqualTo(Physics.gravity));
            Assert.That(second.world.gravityForce,
                Is.EqualTo(Physics.gravity * context.Body.mass));
        }

        [Test]
        public void WorldTruth_PreservesDistinctCraftAndWorldTickIdentities()
        {
            V3DiagnosticContext context = AssembleContext(null);
            worldProfile = ScriptableObject.CreateInstance<V3WorldProfile>();
            worldHost = new GameObject("Diagnostic World");
            worldHost.SetActive(false);
            V3WorldSimulationRoot world =
                worldHost.AddComponent<V3WorldSimulationRoot>();
            world.Configure(worldProfile, "world.diagnostic.tests");
            worldHost.SetActive(true);
            const long worldPhysicsTick = 77;
            const long craftPhysicsTick = 12;
            const double simulationTime = 12.5d;
            world.SetClockState(simulationTime, worldPhysicsTick);
            Assert.That(context.EnvironmentProvider.BeginPhysicsTick(
                context.Body.worldCenterOfMass,
                craftPhysicsTick), Is.True);
            V3WorldEnvironmentSample expected =
                context.EnvironmentProvider.CurrentSample;

            var source = new V3WorldTruthRecorder();
            source.Initialize(context);
            V3DiagnosticSample sample = PreparedSample();
            source.Capture(ref sample);

            Assert.That(sample.world.environmentValid, Is.True);
            Assert.That(sample.world.craftPhysicsTickId,
                Is.EqualTo(craftPhysicsTick));
            Assert.That(sample.world.worldPhysicsTickId,
                Is.EqualTo(worldPhysicsTick));
            Assert.That(sample.world.worldSimulationTime,
                Is.EqualTo(simulationTime));
            Assert.That(sample.world.environmentSamplePosition,
                Is.EqualTo(expected.WorldPosition));
            Assert.That(sample.world.gravityVector,
                Is.EqualTo(expected.GravityVector));
            Assert.That(sample.world.ambientTemperatureC,
                Is.EqualTo(expected.AmbientTemperatureC));
            Assert.That(sample.world.atmosphericPressurePa,
                Is.EqualTo(expected.AtmosphericPressurePa));
            Assert.That(sample.world.airDensityKgPerCubicMeter,
                Is.EqualTo(expected.AirDensityKgPerCubicMeter));
            Assert.That(sample.world.localAirVelocity,
                Is.EqualTo(expected.LocalAirVelocity));
        }

        [Test]
        public void WorldTruth_ResetBoundaryDoesNotCreateTeleportAcceleration()
        {
            V3DiagnosticContext context = AssembleContext(null);
            var source = new V3WorldTruthRecorder();
            source.Initialize(context);
            source.OnSessionStarted(null);
            V3DiagnosticSample first = PreparedSample();
            context.Body.linearVelocity = Vector3.forward * 10f;
            source.Capture(ref first);
            Assert.That(first.identity.dynamicsValid, Is.False);

            V3DiagnosticSample reset = PreparedSample();
            reset.identity.externalStateDiscontinuity = true;
            reset.identity.resetReason = "ManualInput";
            context.Body.linearVelocity = Vector3.zero;
            source.Capture(ref reset);

            Assert.That(
                reset.world.linearAcceleration.sqrMagnitude,
                Is.LessThan(0.000001f));
            Assert.That(
                reset.world.angularAcceleration.sqrMagnitude,
                Is.LessThan(0.000001f));
            Assert.That(reset.identity.dynamicsValid, Is.False);
        }

        [Test]
        public void EventDetector_UsesPhysicalTakeoffAndContactLandingVocabulary()
        {
            V3DiagnosticSession session = CreateSession();
            var detector = new V3DiagnosticEventDetector();
            detector.Initialize(null);
            detector.OnSessionStarted(session);

            V3DiagnosticSample supported = PreparedSample();
            supported.identity.dynamicsValid = true;
            supported.world.contactCount = 1;
            supported.belief.surfaceDetected = true;
            supported.belief.nearSurface = true;
            supported.belief.captureAuthorityMultiplier = 1f;
            supported.worldContactState =
                V3WorldContactState.HoveringNearSurface;
            detector.Capture(ref supported);

            V3DiagnosticSample flight = PreparedSample();
            flight.identity.sampleIndex = 1;
            flight.identity.physicsTick = 1;
            flight.identity.sessionElapsed = 1d;
            flight.identity.dynamicsValid = true;
            flight.world.contactCount = 0;
            flight.belief.surfaceDetected = false;
            flight.belief.nearSurface = false;
            flight.belief.captureAuthorityMultiplier = 0f;
            flight.worldContactState = V3WorldContactState.FreeFlight;
            detector.Capture(ref flight);

            V3DiagnosticSample contact = flight;
            contact.identity.sampleIndex = 2;
            contact.identity.physicsTick = 2;
            contact.identity.sessionElapsed = 2d;
            contact.world.contactCount = 1;
            contact.worldContactState = V3WorldContactState.OnSurface;
            detector.Capture(ref contact);

            Assert.That(HasEvent(
                session, V3DiagnosticEventType.PhysicalTakeoff), Is.True);
            Assert.That(HasEvent(
                session, V3DiagnosticEventType.ContactLanding), Is.True);
            Assert.That(HasEvent(
                session, V3DiagnosticEventType.Takeoff), Is.False);
            Assert.That(HasEvent(
                session, V3DiagnosticEventType.Landing), Is.False);
        }

        [Test]
        public void WorldContact_UsesConfiguredEightMetreHoverEnvelope()
        {
            V3DiagnosticContext context = AssembleContext(null);
            var source = new V3WorldContactRecorder();
            source.Initialize(context);
            source.OnSessionStarted(null);
            V3DiagnosticSample sample = PreparedSample();
            sample.track.mapped = true;
            sample.track.insideTrackBounds = true;
            sample.track.expectedAirborne = false;
            sample.track.trueSurfaceDistance = 8f;
            sample.track.normal = Vector3.up;
            sample.world.linearVelocity = Vector3.forward * 100f;

            source.Capture(ref sample);

            Assert.That(
                sample.worldContactState,
                Is.EqualTo(V3WorldContactState.HoveringNearSurface));
        }

        [Test]
        public void EventDetector_UsesLocalTrackNormalForLoopRollover()
        {
            V3DiagnosticContext context = AssembleContext(null);
            context.CraftTransform.rotation =
                Quaternion.Euler(180f, 0f, 0f);
            V3DiagnosticSession session = CreateSession();
            var detector = new V3DiagnosticEventDetector();
            detector.Initialize(context);
            detector.OnSessionStarted(session);
            V3DiagnosticSample aligned = PreparedSample();
            aligned.identity.dynamicsValid = true;
            aligned.track.mapped = true;
            aligned.track.normal = Vector3.down;
            aligned.track.trueSurfaceNormal = Vector3.down;
            aligned.world.gravityVector = Physics.gravity;
            aligned.worldContactState =
                V3WorldContactState.HoveringNearSurface;

            detector.Capture(ref aligned);

            Assert.That(
                HasEvent(session, V3DiagnosticEventType.Rollover),
                Is.False,
                "Being globally inverted is correct while aligned to a loop.");

            V3DiagnosticSample misaligned = PreparedSample();
            misaligned.identity.sampleIndex = 1;
            misaligned.identity.physicsTick = 1;
            misaligned.identity.sessionElapsed = 1d;
            misaligned.identity.dynamicsValid = true;
            misaligned.track.mapped = true;
            misaligned.track.normal = Vector3.up;
            misaligned.track.trueSurfaceNormal = Vector3.up;
            misaligned.world.gravityVector = Physics.gravity;
            misaligned.worldContactState =
                V3WorldContactState.HoveringNearSurface;
            detector.Capture(ref misaligned);

            Assert.That(
                HasEvent(session, V3DiagnosticEventType.Rollover),
                Is.True);
        }

        [Test]
        public void EventDetector_IgnoresRoundingOnlyPowerDeficits()
        {
            V3DiagnosticSession session = CreateSession();
            var detector = new V3DiagnosticEventDetector();
            detector.Initialize(null);
            detector.OnSessionStarted(session);
            V3DiagnosticSample rounding = PreparedSample();
            rounding.power.requestedPropulsionPower = 1500f;
            rounding.power.grantedPropulsionPower = 1499.999f;
            rounding.power.propulsionPowerLimited = true;
            rounding.worldContactState =
                V3WorldContactState.HoveringNearSurface;
            detector.Capture(ref rounding);
            Assert.That(
                HasEvent(session, V3DiagnosticEventType.PowerStarvation),
                Is.False);

            V3DiagnosticSample limited = PreparedSample();
            limited.identity.sampleIndex = 1;
            limited.identity.physicsTick = 1;
            limited.identity.sessionElapsed = 1d;
            limited.power.requestedPropulsionPower = 5000f;
            limited.power.grantedPropulsionPower = 4500f;
            limited.power.propulsionPowerLimited = true;
            limited.worldContactState =
                V3WorldContactState.HoveringNearSurface;
            detector.Capture(ref limited);
            Assert.That(
                HasEvent(session, V3DiagnosticEventType.PowerStarvation),
                Is.True);
        }

        [Test]
        public void TrackAnalyzer_MapsWorldPoseIntoTrackFrameWithoutMutatingBody()
        {
            TrackGenerator generator = CreateStraightTrack();
            V3DiagnosticContext context = AssembleContext(generator);
            context.Body.position = new Vector3(2f, 3f, 5f);
            Vector3 originalVelocity = new Vector3(1f, 0f, 4f);
            context.Body.linearVelocity = originalVelocity;
            var session = CreateSession();
            var analyzer = new V3TrackDiagnosticAnalyzer();
            analyzer.Initialize(context);
            analyzer.OnSessionStarted(session);
            V3DiagnosticSample sample = PreparedSample();
            Vector3 expectedCenter = context.Body.worldCenterOfMass;

            analyzer.Capture(ref sample);

            Assert.That(sample.track.mapped, Is.True);
            Assert.That(sample.track.distanceAlongTrack,
                Is.EqualTo(expectedCenter.z).Within(0.01f));
            Assert.That(sample.track.signedLateralOffset,
                Is.EqualTo(expectedCenter.x).Within(0.01f));
            Assert.That(sample.track.signedVerticalOffset,
                Is.EqualTo(expectedCenter.y).Within(0.01f));
            Assert.That(sample.track.tangent, Is.EqualTo(Vector3.forward));
            Assert.That(session.TrackSnapshot.sectionCount, Is.EqualTo(1));
            Assert.That(context.Body.linearVelocity, Is.EqualTo(originalVelocity));
        }

        [Test]
        public void SensorRecorder_RecordsPhysicalMountsAndExplicitMissingTruth()
        {
            V3DiagnosticContext context = AssembleContext(null);
            var session = CreateSession();
            var source = new V3CraftSensorRecorder();
            source.Initialize(context);
            source.OnSessionStarted(session);
            V3DiagnosticSample sample = PreparedSample();
            sample.identity.simulationTimestamp = 1d;

            source.Capture(ref sample);

            Assert.That(sample.sensorCount, Is.GreaterThan(0));
            Assert.That(sample.sensors[0].sensorId, Is.Not.Empty);
            Assert.That(sample.sensors[0].mountRotation,
                Is.Not.EqualTo(default(Quaternion)));
            if (!sample.sensors[0].hasSample || !sample.sensors[0].trueHit)
            {
                Assert.That(sample.sensors[0].unavailableReason, Is.Not.Empty);
            }
        }

        [Test]
        public void BeliefRecorder_CapturesMainframeAndHoverInterpretedState()
        {
            V3DiagnosticContext context = AssembleContext(null);
            var source = new V3CraftBeliefRecorder();
            source.Initialize(context);
            V3DiagnosticSample sample = PreparedSample();

            source.Capture(ref sample);

            V3HoverController hover =
                context.Craft.GetComponent<V3HoverController>();
            Assert.That(sample.belief.mainframeState, Is.Not.Empty);
            Assert.That(sample.belief.connectedComputers,
                Is.EqualTo(context.Mainframe.Computers.Count));
            Assert.That(sample.belief.targetRideHeight,
                Is.EqualTo(hover.TargetHeight));
            Assert.That(sample.belief.hoverConfigurationId,
                Is.EqualTo(hover.Configuration.StableId));
            Assert.That(sample.belief.minimumHoverClearance,
                Is.EqualTo(hover.MinimumClearance));
            Assert.That(sample.belief.maximumHoverRange,
                Is.EqualTo(hover.ProbeRange));
            Assert.That(sample.belief.believedSurfaceNormal,
                Is.EqualTo(hover.GroundNormal));
        }

        [Test]
        public void ExecutionRecorder_CapturesSchedulerRequestsAndPower()
        {
            V3DiagnosticContext context = AssembleContext(null);
            TickPipeline(context.Craft, new V3PilotCommand
            {
                Throttle = 0.75f,
                Strafe = 0.2f,
                Yaw = 0.1f,
                StabilizationEnabled = true
            });
            var source = new V3PilotAndSystemExecutionRecorder();
            source.Initialize(context);
            V3DiagnosticSession session = CreateSession();
            source.OnSessionStarted(session);
            V3DiagnosticSample sample = PreparedSample();
            sample.identity.simulationTimestamp = 0.08d;

            source.Capture(ref sample);

            Assert.That(sample.taskCount, Is.GreaterThan(0));
            Assert.That(sample.tasks[0].taskId, Is.Not.Empty);
            Assert.That(sample.tasks[0].requestedRateHz, Is.GreaterThan(0f));
            Assert.That(sample.requestCount, Is.GreaterThan(0));
            Assert.That(sample.requests[0].sourceSystemId, Is.Not.Empty);
            Assert.That(sample.power.requestedSystemsPower,
                Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void DeviceRecorder_PreservesRequestAllocationExecutionChain()
        {
            V3DiagnosticContext context = AssembleContext(null);
            TickPipeline(context.Craft, new V3PilotCommand
            {
                Throttle = 1f,
                Lift = 0.3f,
                StabilizationEnabled = true
            });
            var source = new V3ActuatorExecutionRecorder();
            source.Initialize(context);
            V3DiagnosticSession session = CreateSession();
            source.OnSessionStarted(session);
            V3DiagnosticSample sample = PreparedSample();

            source.Capture(ref sample);

            Assert.That(sample.deviceCount, Is.GreaterThan(0));
            bool foundThruster = false;
            for (int i = 0; i < sample.deviceCount; i++)
            {
                if (sample.devices[i].deviceType != "Thruster") continue;
                foundThruster = true;
                Assert.That(sample.devices[i].socketId, Is.Not.Empty);
                Assert.That(sample.devices[i].routerCombinedRequest,
                    Is.GreaterThanOrEqualTo(0f));
                Assert.That(sample.devices[i].actualForceMagnitudeN,
                    Is.EqualTo(sample.devices[i].actualForce.magnitude)
                        .Within(0.001f));
            }
            Assert.That(foundThruster, Is.True);
        }

        private V3DiagnosticContext AssembleContext(TrackGenerator generator)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(SystemsBuildPath);
            Assert.That(build, Is.Not.Null);
            assemblerHost = new GameObject("Diagnostic Test Assembler");
            V3CraftAssembler assembler =
                assemblerHost.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.FindProperty("assembleOnStart").boolValue = false;
            serialized.FindProperty("installReferenceControllers").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            V3CraftRuntime craft =
                assembler.AssembledRoot.GetComponent<V3CraftRuntime>();
            var context = new V3DiagnosticContext();
            Assert.That(context.Bind(craft, null, generator), Is.True);
            return context;
        }

        private TrackGenerator CreateStraightTrack()
        {
            trackHost = new GameObject("Diagnostic Straight Track");
            TrackGenerator generator = trackHost.AddComponent<TrackGenerator>();
            var trackRootObject = new GameObject("Track Root");
            trackRootObject.transform.SetParent(trackHost.transform, false);
            var visualizer = trackRootObject.AddComponent<MacroTrackDebugVisualizer>();
            var definition = new TrackMacroSectionDefinition
            {
                SectionType = TrackMacroSectionType.Straight,
                DebugName = "Diagnostic Straight",
                Length = 10f,
                Width = 10f
            };
            TrackConnectionFrame start = TrackConnectionFrame.Origin(10f);
            start.ArcLength = 0f;
            TrackConnectionFrame end = start;
            end.Position = Vector3.forward * 10f;
            end.ArcLength = 10f;
            end.LapProgress = 1f;
            var section = new GeneratedTrackSection
            {
                Definition = definition,
                SectionIndex = 0,
                StartFrame = start,
                EndFrame = end,
                SubdivisionFrames = new[] { start, end },
                RoadId = 0
            };
            section.RecalculateBounds();
            visualizer.Sections.Add(section);
            var serialized = new SerializedObject(generator);
            serialized.FindProperty("trackRoot").objectReferenceValue =
                trackRootObject.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return generator;
        }

        private static V3DiagnosticSession CreateSession()
        {
            var session = new V3DiagnosticSession();
            session.Initialize("truth-test", "Truth", V3DiagnosticRecordingProfile.FullForensic,
                2, 1f, new V3DiagnosticCaptureCapacities
                {
                    sensors = 16, tasks = 16, observations = 32,
                    requests = 16, devices = 64, events = 16
                }, new V3DiagnosticCraftSnapshot(), new V3DiagnosticWorldSnapshot());
            return session;
        }

        private static V3DiagnosticSample PreparedSample()
        {
            var sample = new V3DiagnosticSample();
            sample.Prepare(16, 16, 32, 16, 64);
            sample.identity.fixedDelta = 0.02f;
            return sample;
        }

        private static bool HasEvent(
            V3DiagnosticSession session,
            V3DiagnosticEventType type)
        {
            for (int i = 0; i < session.EventCount; i++)
                if (session.Events[i].type == type)
                    return true;
            return false;
        }

        private static void TickPipeline(
            V3CraftRuntime craft, V3PilotCommand command)
        {
            V3ControllerPipeline pipeline =
                craft.GetComponent<V3ControllerPipeline>();
            Assert.That(pipeline, Is.Not.Null);
            for (int i = 0; i < 4; i++)
            {
                pipeline.Tick(command, 0.02f);
            }
        }
    }
}
