using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests;
using TrackGeneration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class V3DiagnosticRecorder : MonoBehaviour
    {
        [Header("Recording")]
        [SerializeField] private V3DiagnosticRecordingProfile recordingProfile =
            V3DiagnosticRecordingProfile.ForensicCompact;
        [SerializeField] private V3DiagnosticCustomChannelSelection customChannels =
            new V3DiagnosticCustomChannelSelection();
        [SerializeField] private string outputRoot = "TestDriveReports/HovercraftV3";
        [SerializeField] private string sessionName = "BenchmarkRun";
        [SerializeField] private bool autoStart;
        [SerializeField] private bool startOnMovement;
        [SerializeField, Min(0.01f)] private float movementThresholdMetersPerSecond = 0.25f;
        [SerializeField] private bool stopOnLapComplete;
        [SerializeField, Min(1f)] private float maximumDurationSeconds = 60f;
        [SerializeField, Min(0f)] private float eventPreContextSeconds = 2f;
        [SerializeField, Min(0f)] private float eventPostContextSeconds = 3f;
        [SerializeField] private bool autoExportOnStop = true;
        [SerializeField] private bool showRuntimeOverlay = true;

        [Header("Bindings")]
        [SerializeField] private V3CraftRuntime craft;
        [SerializeField] private V3PilotInputAdapter pilotInput;
        [SerializeField] private TrackGenerator trackAnalyzer;
        [SerializeField] private V3FreeDriveSession resetSource;
        [SerializeField, Min(0.1f)] private float trackCenterlineSampleIntervalMeters = 1f;

        [Header("Exports")]
        [SerializeField] private bool includeBinary = true;
        [SerializeField] private bool includeNdjson;
        [SerializeField] private bool includeCsv = true;
        [SerializeField] private bool generateMarkdown = true;
        [SerializeField] private bool generateReportJson = true;
        [SerializeField] private V3DiagnosticCaptureCapacities capacities =
            new V3DiagnosticCaptureCapacities();

        private readonly V3DiagnosticContext context = new V3DiagnosticContext();
        private readonly V3DiagnosticClock clock = new V3DiagnosticClock();
        private readonly List<IV3DiagnosticSource> sources =
            new List<IV3DiagnosticSource>(12);
        private readonly Stopwatch captureOverhead = new Stopwatch();
        private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        private Coroutine captureRoutine;
        private Coroutine exportRoutine;
        private V3DiagnosticSession currentSession;
        private V3DiagnosticExportResult lastExport;
        private bool recording;
        private bool exporting;
        private float exportProgress;
        private string exportStage = string.Empty;
        private int physicsTicksSinceStart;
        private int attemptIndex;
        private int pendingDiscontinuitySamples;
        private string pendingResetReason = string.Empty;
        private V3ControlledRecorderMetadata controlledMetadata;
        private V3ControlledTestPhase controlledTestPhase =
            V3ControlledTestPhase.Setup;

        public bool IsRecording => recording;
        public bool IsExporting => exporting;
        public float ExportProgress => exportProgress;
        public string ExportStage => exportStage;
        public V3DiagnosticSession CurrentSession => currentSession;
        public V3DiagnosticExportResult LastExport => lastExport;
        public V3DiagnosticClock Clock => clock;
        public V3CraftRuntime Craft => craft;
        public TrackGenerator TrackAnalyzer => trackAnalyzer;
        public string OutputRoot => ResolveOutputRoot();

        private void Awake()
        {
            ResolveBindings();
            BuildSources();
            if (showRuntimeOverlay)
            {
                V3DiagnosticRecorderOverlay overlay =
                    GetComponent<V3DiagnosticRecorderOverlay>();
                if (overlay == null)
                    overlay = gameObject.AddComponent<V3DiagnosticRecorderOverlay>();
                overlay.Bind(this);
            }
        }

        private void Start()
        {
            if (autoStart)
            {
                StartRecording();
            }
        }

        private void Update()
        {
            HandleHotkeys();
            if (!recording && startOnMovement &&
                context.Body != null &&
                context.Body.linearVelocity.magnitude >= movementThresholdMetersPerSecond)
            {
                StartRecording();
            }
        }

        private void OnDisable()
        {
            if (recording)
            {
                StopRecording(false);
            }
            if (exportRoutine != null)
            {
                StopCoroutine(exportRoutine);
                exportRoutine = null;
                exporting = false;
                exportStage = "Export interrupted because the recorder was disabled";
                if (lastExport != null && !lastExport.success)
                    lastExport.error = exportStage;
            }
        }

        private void OnDestroy()
        {
            BindResetSource(null);
        }

        [ContextMenu("Start Recording")]
        public bool StartRecording()
        {
            if (recording || exporting)
            {
                return false;
            }
            if (!ResolveBindings())
            {
                UnityEngine.Debug.LogWarning(
                    "Hovercraft V3 diagnostics could not start: no assembled craft Rigidbody is bound.",
                    this);
                return false;
            }
            BuildSources();
            float fixedDelta = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            int captureStride = CaptureStride;
            int sampleCapacity = Mathf.Max(1,
                Mathf.CeilToInt(maximumDurationSeconds /
                    (fixedDelta * captureStride)) + 2);
            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            currentSession = new V3DiagnosticSession();
            currentSession.Initialize(
                sessionId,
                sessionName,
                recordingProfile,
                sampleCapacity,
                maximumDurationSeconds,
                capacities,
                V3CraftDiagnosticSnapshotExporter.Capture(context),
                V3WorldDiagnosticSnapshotExporter.Capture(context),
                captureStride,
                eventPreContextSeconds,
                eventPostContextSeconds);
            currentSession.SetEnabledSources(sources);
            currentSession.SetControlledTestMetadata(
                controlledMetadata,
                controlledTestPhase);
            if (recordingProfile == V3DiagnosticRecordingProfile.Custom &&
                customChannels.forceTorqueLedger && !customChannels.devices)
            {
                currentSession.AddDataWarning(
                    "Custom profile enables the force ledger without device execution; device-attributed force categories will be incomplete.");
            }
            clock.Reset(Time.fixedTimeAsDouble, Time.realtimeSinceStartupAsDouble);
            physicsTicksSinceStart = 0;
            attemptIndex = 0;
            pendingDiscontinuitySamples = 0;
            pendingResetReason = string.Empty;
            captureOverhead.Reset();
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].OnSessionStarted(currentSession);
            }
            recording = true;
            captureRoutine = StartCoroutine(CaptureAfterPhysics());
            return true;
        }

        [ContextMenu("Stop Recording")]
        public void StopRecording()
        {
            StopRecording(autoExportOnStop);
        }

        public void StopRecording(bool export)
        {
            if (!recording)
            {
                return;
            }
            recording = false;
            if (captureRoutine != null)
            {
                StopCoroutine(captureRoutine);
                captureRoutine = null;
            }
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].OnSessionEnded(currentSession);
            }
            currentSession.Complete(
                clock.Current.sessionElapsedSeconds,
                captureOverhead.Elapsed.TotalMilliseconds);
            if (export)
            {
                ExportNow();
            }
        }

        [ContextMenu("Export Now")]
        public bool ExportNow()
        {
            if (exporting)
            {
                return false;
            }
            if (recording)
            {
                StopRecording(false);
            }
            if (currentSession == null)
            {
                return false;
            }
            exporting = true;
            exportProgress = 0f;
            exportStage = "Preparing export";
            lastExport = new V3DiagnosticExportResult();
            exportRoutine = StartCoroutine(ExportSessionIncrementally(
                currentSession, lastExport));
            return true;
        }

        public void ConfigureControlledCapture(
            V3ControlledRecorderMetadata metadata,
            float durationSeconds,
            string requestedOutputRoot,
            string requestedSessionName,
            bool includeRawNdjson)
        {
            if (recording || exporting)
                throw new InvalidOperationException(
                    "Controlled capture cannot be reconfigured while recording or exporting.");
            controlledMetadata = metadata;
            maximumDurationSeconds = Mathf.Max(0.01f, durationSeconds);
            if (!string.IsNullOrWhiteSpace(requestedOutputRoot))
                outputRoot = requestedOutputRoot;
            if (!string.IsNullOrWhiteSpace(requestedSessionName))
                sessionName = requestedSessionName;
            recordingProfile = V3DiagnosticRecordingProfile.ForensicCompact;
            includeBinary = true;
            includeCsv = true;
            includeNdjson = includeRawNdjson;
            generateMarkdown = true;
            generateReportJson = true;
            autoExportOnStop = false;
        }

        public void SetControlledTestPhase(V3ControlledTestPhase phase)
        {
            controlledTestPhase = phase;
            currentSession?.SetControlledTestPhase(phase);
        }

        public void SetActualControlPath(V3ControlExecutionPath path)
        {
            currentSession?.SetActualControlPath(path);
        }

        private IEnumerator ExportSessionIncrementally(
            V3DiagnosticSession session,
            V3DiagnosticExportResult result)
        {
            var elapsed = Stopwatch.StartNew();
            IEnumerator work = V3DiagnosticExportService.ExportIncremental(
                session,
                ResolveOutputRoot(),
                new V3DiagnosticExportOptions
                {
                    includeBinary = includeBinary ||
                        recordingProfile == V3DiagnosticRecordingProfile.FullForensic,
                    includeNdjson = includeNdjson ||
                        recordingProfile == V3DiagnosticRecordingProfile.FullForensic,
                    includeCsv = includeCsv,
                    generateMarkdown = generateMarkdown,
                    generateReportJson = generateReportJson
                },
                result,
                SetExportProgress);

            bool failed = false;
            try
            {
                while (true)
                {
                    bool hasNext = false;
                    object yielded = null;
                    try
                    {
                        hasNext = work.MoveNext();
                        if (hasNext)
                            yielded = work.Current;
                    }
                    catch (Exception exception)
                    {
                        failed = true;
                        result.success = false;
                        result.error = exception.ToString();
                        result.elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds;
                        break;
                    }
                    if (!hasNext)
                        break;
                    yield return yielded;
                }
            }
            finally
            {
                (work as IDisposable)?.Dispose();
            }
            elapsed.Stop();
            exporting = false;
            exportRoutine = null;
            if (failed || !result.success)
            {
                exportStage = "Export failed";
                UnityEngine.Debug.LogError(
                    "Hovercraft V3 diagnostic export failed: " + result.error,
                    this);
            }
            else
            {
                exportProgress = 1f;
                exportStage = "Export complete";
            }
        }

        private void SetExportProgress(float progress, string stage)
        {
            exportProgress = Mathf.Clamp01(progress);
            exportStage = stage ?? string.Empty;
        }

        public bool MarkManualEvent(string notes = "Manual marker")
        {
            if (currentSession == null)
            {
                return false;
            }
            V3DiagnosticSample sample = currentSession.SampleCount > 0
                ? currentSession.Samples[currentSession.SampleCount - 1]
                : default;
            return currentSession.TryAddEvent(new V3DiagnosticEvent
            {
                eventId = "manual_" + (currentSession.EventCount + 1).ToString("0000"),
                type = V3DiagnosticEventType.ManualMarker,
                severity = 0,
                timestamp = clock.Current.sessionElapsedSeconds,
                tick = clock.Current.physicsTick,
                sampleIndex = clock.Current.sampleIndex,
                attemptIndex = attemptIndex,
                trackDistance = sample.track.distanceAlongTrack,
                sectionId = sample.track.sectionId,
                craftPosition = context.Body != null ? context.Body.position : Vector3.zero,
                speedMetersPerSecond = context.Body != null
                    ? context.Body.linearVelocity.magnitude
                    : 0f,
                worldState = sample.worldContactState,
                craftBeliefState = sample.belief.hoverGrounded ? "Grounded" : "NotGrounded",
                possibleContributingDomains = string.Empty,
                preEventStartSample = Math.Max(0L,
                    clock.Current.sampleIndex -
                    EventContextSamples(eventPreContextSeconds)),
                postEventEndSample = clock.Current.sampleIndex +
                    EventContextSamples(eventPostContextSeconds),
                notes = notes ?? string.Empty,
                manual = true
            });
        }

        private IEnumerator CaptureAfterPhysics()
        {
            while (recording)
            {
                yield return waitForFixedUpdate;
                if (!recording)
                {
                    yield break;
                }
                bool captureThisTick = physicsTicksSinceStart % CaptureStride == 0;
                physicsTicksSinceStart++;
                CaptureOnePhysicsTick(captureThisTick);
                if (clock.Current.sessionElapsedSeconds >= maximumDurationSeconds)
                {
                    StopRecording(autoExportOnStop);
                }
            }
        }

        private void CaptureOnePhysicsTick(bool captureSample)
        {
            captureOverhead.Start();
            V3DiagnosticClockSnapshot time = clock.Advance(
                Time.fixedTimeAsDouble,
                Time.realtimeSinceStartupAsDouble,
                Time.fixedDeltaTime,
                V3DiagnosticPhase.PostPhysics,
                captureSample);
            if (!captureSample)
            {
                captureOverhead.Stop();
                return;
            }
            if (!currentSession.TryBeginSample(out int index))
            {
                captureOverhead.Stop();
                RecordDroppedSampleEvent();
                StopRecording(autoExportOnStop);
                return;
            }
            ref V3DiagnosticSample sample = ref currentSession.GetSample(index);
            sample.identity = new V3DiagnosticSampleIdentity
            {
                sessionId = currentSession.Manifest.sessionId,
                sampleIndex = time.sampleIndex,
                physicsTick = time.physicsTick,
                simulationTimestamp = time.simulationSeconds,
                unscaledRealTimestamp = time.unscaledRealSeconds,
                sessionElapsed = time.sessionElapsedSeconds,
                fixedDelta = time.fixedDeltaSeconds,
                phase = time.phase,
                controlledTestPhase = controlledTestPhase.ToString(),
                sceneName = currentSession.Manifest.sceneName,
                craftRuntimeInstanceId = craft != null
                    ? craft.GetEntityId().GetHashCode()
                    : 0,
                craftBuildStableId = craft != null && craft.Build != null
                    ? craft.Build.StableId : string.Empty,
                trackInstanceId = trackAnalyzer != null
                    ? trackAnalyzer.GetEntityId().GetHashCode()
                    : 0,
                attemptIndex = attemptIndex,
                externalStateDiscontinuity =
                    pendingDiscontinuitySamples > 0,
                resetReason = pendingDiscontinuitySamples > 0
                    ? pendingResetReason : string.Empty
            };
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Capture(ref sample);
            }
            if (pendingDiscontinuitySamples > 0)
            {
                pendingDiscontinuitySamples--;
                if (pendingDiscontinuitySamples == 0)
                    pendingResetReason = string.Empty;
            }
            captureOverhead.Stop();
            if (stopOnLapComplete && sample.track.lap > 0)
            {
                StopRecording(autoExportOnStop);
            }
        }

        private bool ResolveBindings()
        {
            if (craft == null)
            {
                craft = UnityEngine.Object.FindAnyObjectByType<V3CraftRuntime>();
            }
            if (pilotInput == null)
            {
                pilotInput = UnityEngine.Object.FindAnyObjectByType<V3PilotInputAdapter>();
            }
            if (trackAnalyzer == null)
            {
                trackAnalyzer = UnityEngine.Object.FindAnyObjectByType<TrackGenerator>();
            }
            if (resetSource == null)
            {
                resetSource = UnityEngine.Object.FindAnyObjectByType<
                    V3FreeDriveSession>();
            }
            BindResetSource(resetSource);
            return context.Bind(craft, pilotInput, trackAnalyzer);
        }

        private void BindResetSource(V3FreeDriveSession source)
        {
            if (resetSource != null)
                resetSource.CraftResetPerformed -= OnCraftResetPerformed;

            resetSource = source;
            if (resetSource != null)
                resetSource.CraftResetPerformed += OnCraftResetPerformed;
        }

        private void OnCraftResetPerformed(
            V3CraftResetNotification notification)
        {
            if (!recording || currentSession == null)
                return;

            attemptIndex++;
            pendingDiscontinuitySamples = 1;
            pendingResetReason = notification.Reason.ToString();

            V3DiagnosticSample previous = currentSession.SampleCount > 0
                ? currentSession.Samples[currentSession.SampleCount - 1]
                : default;
            long boundarySample = clock.Current.sampleIndex + 1;
            currentSession.TryAddEvent(new V3DiagnosticEvent
            {
                eventId = "Reset_" + boundarySample.ToString("D8"),
                type = V3DiagnosticEventType.Reset,
                severity = 0,
                timestamp = clock.Current.sessionElapsedSeconds,
                tick = clock.Current.physicsTick,
                sampleIndex = boundarySample,
                attemptIndex = attemptIndex,
                resetReason = notification.Reason.ToString(),
                trackDistance = previous.track.distanceAlongTrack,
                sectionId = previous.track.sectionId,
                craftPosition = notification.ResetPosition,
                speedMetersPerSecond = 0f,
                worldState = previous.worldContactState,
                craftBeliefState = previous.belief.hoverGrounded
                    ? "Grounded" : "NotGrounded",
                possibleContributingDomains = "external_state",
                preEventStartSample = Math.Max(0L,
                    boundarySample -
                    EventContextSamples(eventPreContextSeconds)),
                postEventEndSample = boundarySample +
                    EventContextSamples(eventPostContextSeconds),
                notes = notification.Label + " [" +
                    notification.Reason + "] from " +
                    notification.PreviousPosition + " to " +
                    notification.ResetPosition + ".",
                manual = notification.IsManual
            });
        }

        private void RecordDroppedSampleEvent()
        {
            if (currentSession == null) return;
            currentSession.AddDataWarning(
                "The bounded sample buffer reached capacity; recording stopped without affecting simulation.");
            V3DiagnosticSample last = currentSession.SampleCount > 0
                ? currentSession.Samples[currentSession.SampleCount - 1]
                : default;
            currentSession.TryAddEvent(new V3DiagnosticEvent
            {
                eventId = "dropped_" + currentSession.EventCount,
                type = V3DiagnosticEventType.DroppedSamples,
                severity = 3,
                timestamp = clock.Current.sessionElapsedSeconds,
                tick = clock.Current.physicsTick,
                sampleIndex = clock.Current.sampleIndex,
                attemptIndex = attemptIndex,
                trackDistance = last.track.distanceAlongTrack,
                sectionId = last.track.sectionId,
                craftPosition = last.world.position,
                speedMetersPerSecond = last.world.linearVelocity.magnitude,
                worldState = last.worldContactState,
                craftBeliefState = last.belief.hoverGrounded
                    ? "Grounded" : "NotGrounded",
                possibleContributingDomains = "recorder_backpressure",
                preEventStartSample = Math.Max(0L,
                    clock.Current.sampleIndex -
                    EventContextSamples(eventPreContextSeconds)),
                postEventEndSample = clock.Current.sampleIndex,
                notes = "A sample could not enter the bounded recording buffer.",
                manual = false
            });
        }

        private void BuildSources()
        {
            sources.Clear();
            customChannels ??= new V3DiagnosticCustomChannelSelection();
            bool custom = recordingProfile == V3DiagnosticRecordingProfile.Custom;
            if (!custom || customChannels.worldTruth)
                AddSource(new V3WorldTruthRecorder());
            if (!custom || customChannels.trackTruth)
                AddSource(new V3TrackDiagnosticAnalyzer(
                    trackCenterlineSampleIntervalMeters));
            if (!custom || customChannels.worldContacts)
                AddSource(new V3WorldContactRecorder());
            if (!custom || customChannels.sensorsAndObservations)
                AddSource(new V3CraftSensorRecorder());
            if (!custom || customChannels.craftBelief)
                AddSource(new V3CraftBeliefRecorder());
            if (!custom || customChannels.pilotSystemsAndPower)
                AddSource(new V3PilotAndSystemExecutionRecorder());
            if (!custom || customChannels.controlRouter)
                AddSource(new V3ControlDecisionRecorder());
            if (!custom || customChannels.devices)
                AddSource(new V3ActuatorExecutionRecorder());
            if (!custom || customChannels.forceTorqueLedger)
                AddSource(new V3ForceTorqueLedger());
            if (!custom || customChannels.events)
                AddSource(new V3DiagnosticEventDetector(
                    EventContextSamples(eventPreContextSeconds),
                    EventContextSamples(eventPostContextSeconds)));
        }

        private int EventContextSamples(float seconds)
        {
            float sampleInterval = Mathf.Max(0.0001f,
                Time.fixedDeltaTime * CaptureStride);
            return Mathf.Max(0, Mathf.CeilToInt(
                Mathf.Max(0f, seconds) / sampleInterval));
        }

        private int CaptureStride
        {
            get
            {
                if (recordingProfile == V3DiagnosticRecordingProfile.Endurance)
                    return 5;
                if (recordingProfile == V3DiagnosticRecordingProfile.Custom)
                    return Mathf.Max(1, customChannels.physicsTickStride);
                return 1;
            }
        }

        private void AddSource(IV3DiagnosticSource source)
        {
            source.Initialize(context);
            sources.Add(source);
        }

        private void HandleHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.f7Key.wasPressedThisFrame)
            {
                if (recording) StopRecording();
                else if (!exporting) StartRecording();
            }
            if (keyboard.f8Key.wasPressedThisFrame)
            {
                MarkManualEvent();
            }
            if (keyboard.f9Key.wasPressedThisFrame)
            {
                ExportNow();
            }
        }

        private string ResolveOutputRoot()
        {
            if (Path.IsPathRooted(outputRoot))
            {
                return Path.GetFullPath(outputRoot);
            }
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty,
                string.IsNullOrWhiteSpace(outputRoot)
                    ? "TestDriveReports/HovercraftV3"
                    : outputRoot));
        }
    }
}
