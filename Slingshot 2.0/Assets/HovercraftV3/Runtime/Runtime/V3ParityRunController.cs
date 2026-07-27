using System;
using System.Reflection;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class V3ParityRunController : MonoBehaviour
    {
        [SerializeField] private V3ParityCommandProfile profile;
        [SerializeField] private GameObject v2CraftRoot;
        [SerializeField] private V3CraftAssembler v3Assembler;
        [SerializeField] private bool autoRun = true;
        [SerializeField] private bool requireV2ReferenceFingerprint = true;
        [SerializeField] private bool exportAutomatically = true;
        [SerializeField] private LayerMask trackSurfaceMask = 1 << 8;

        private const string V2ForceUnitNote =
            "V2 reference instance translates configured newtons to its legacy " +
            "ForceMode.Acceleration units (force / 11000 kg); V3 uses ForceMode.Force.";

        private V3ControllerPipeline v3Pipeline;
        private V3PowerDistributor v3Power;
        private Rigidbody v2Body;
        private Rigidbody v3Body;
        private Component v2PilotInput;
        private Component v2EnergyCore;
        private PropertyInfo v2CommandProperty;
        private Type v2CommandType;
        private V3ParityTrackRecorder v2Recorder;
        private V3ParityTrackRecorder v3Recorder;
        private Vector3 v2SpawnPosition;
        private Quaternion v2SpawnRotation;
        private Vector3 v3SpawnPosition;
        private Quaternion v3SpawnRotation;
        private float elapsedSeconds;
        private int activeSegmentIndex = -1;
        private int v2SampleFrame;
        private bool started;

        public bool IsRunning { get; private set; }
        public string LastStatus { get; private set; }
        public string LastExportDirectory { get; private set; }
        public V3ParityRunSummary LastV2Summary { get; private set; }
        public V3ParityRunSummary LastV3Summary { get; private set; }

        private void Start()
        {
            if (autoRun)
            {
                TryBeginRun();
            }
        }

        private void FixedUpdate()
        {
            if (!IsRunning)
            {
                if (autoRun && !started)
                {
                    TryBeginRun();
                }

                return;
            }

            if (!profile.TryEvaluate(
                elapsedSeconds,
                out V3PilotCommand command,
                out int segmentIndex,
                out float segmentElapsed))
            {
                CompleteRun();
                return;
            }

            V3ParityCommandSegment segment = profile.Segments[segmentIndex];
            if (segmentIndex != activeSegmentIndex)
            {
                if (activeSegmentIndex >= 0 &&
                    Contains(
                        profile.Segments[activeSegmentIndex].Label,
                        "Idle Hover"))
                {
                    CalibrateResetHeightsFromIdle();
                }

                activeSegmentIndex = segmentIndex;
                if (segment.ResetCraftsBeforeSegment)
                {
                    ResetV2DynamicState();
                    v3Pipeline?.ResetDynamicState();
                    ResetCraft(
                        v2Body,
                        v2SpawnPosition,
                        v2SpawnRotation,
                        segment.InitialForwardSpeedKmh);
                    ResetCraft(
                        v3Body,
                        v3SpawnPosition,
                        v3SpawnRotation,
                        segment.InitialForwardSpeedKmh);

                    // Rigidbody teleports become authoritative at the physics
                    // boundary. Prime both controller paths now, then begin
                    // recording on the next tick so the previous segment's
                    // position cannot leak into this segment's first sample.
                    ApplyV2Command(command);
                    v3Pipeline?.Tick(command, Time.fixedDeltaTime);
                    return;
                }
            }

            ApplyV2Command(command);
            v3Pipeline?.Tick(command, Time.fixedDeltaTime);

            ReadV2Power(out float v2Requested, out float v2Granted);
            v2Recorder?.Capture(
                elapsedSeconds,
                segment.Label,
                segmentElapsed,
                v2Requested,
                v2Granted,
                trackSurfaceMask);
            v3Recorder?.Capture(
                elapsedSeconds,
                segment.Label,
                segmentElapsed,
                v3Power != null ? v3Power.RequestedPropulsionPower : 0f,
                v3Power != null ? v3Power.GrantedPropulsionPower : 0f,
                trackSurfaceMask);

            elapsedSeconds += Time.fixedDeltaTime;
        }

        [ContextMenu("Begin Parity Run")]
        public bool TryBeginRun()
        {
            started = true;
            if (profile == null || profile.Segments.Count == 0)
            {
                LastStatus = "Parity run needs a non-empty command profile.";
                Debug.LogError(LastStatus, this);
                return false;
            }

            ResolveV3();
            ResolveV2();
            if (v3Body == null || v3Pipeline == null)
            {
                LastStatus = "V3 assembled runtime is not ready.";
                started = false;
                return false;
            }

            if (v2CraftRoot != null &&
                requireV2ReferenceFingerprint &&
                !ValidateV2ReferenceFingerprint(v2CraftRoot, out string fingerprint))
            {
                LastStatus = fingerprint;
                Debug.LogError(LastStatus, this);
                return false;
            }

            v3Pipeline.enabled = false;
            v3SpawnPosition = v3Body.position;
            v3SpawnRotation = v3Body.rotation;
            v3Recorder = new V3ParityTrackRecorder("V3", v3Body);

            if (v2Body != null)
            {
                v2SpawnPosition = v2Body.position;
                v2SpawnRotation = v2Body.rotation;
                v2Recorder = new V3ParityTrackRecorder("V2", v2Body);
            }

            elapsedSeconds = 0f;
            activeSegmentIndex = -1;
            v2SampleFrame = 0;
            LastV2Summary = null;
            LastV3Summary = null;
            IsRunning = true;
            LastStatus = $"Running '{profile.name}' for {profile.TotalDurationSeconds:0.##} seconds.";
            return true;
        }

        [ContextMenu("Stop And Export Parity Run")]
        public void CompleteRun()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            string runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string outputDirectory = System.IO.Path.Combine(
                Application.persistentDataPath,
                "HovercraftV3",
                "Parity");
            LastExportDirectory = outputDirectory;
            LastV2Summary = BuildSummary(v2Recorder);
            LastV3Summary = BuildSummary(v3Recorder);

            if (exportAutomatically)
            {
                ExportRecorder(
                    v2Recorder,
                    LastV2Summary,
                    outputDirectory,
                    runId);
                ExportRecorder(
                    v3Recorder,
                    LastV3Summary,
                    outputDirectory,
                    runId);
            }

            LastStatus =
                $"Parity run complete. V2 samples: {v2Recorder?.Samples.Count ?? 0}; " +
                $"V3 samples: {v3Recorder?.Samples.Count ?? 0}. " +
                $"Output: {outputDirectory}";
            Debug.Log(LastStatus, this);
        }

        public static bool ValidateV2ReferenceFingerprint(
            GameObject root,
            out string status)
        {
            if (root == null)
            {
                status = "No V2 craft was assigned; the run will record V3 only.";
                return true;
            }

            Rigidbody body = root.GetComponent<Rigidbody>();
            if (body == null)
            {
                status = "V2 reference has no root Rigidbody.";
                return false;
            }

            if (Mathf.Abs(body.mass - 11000f) > 0.1f ||
                Vector3.Distance(body.centerOfMass, new Vector3(0f, -0.5f, 0f)) > 0.001f)
            {
                status =
                    $"V2 reference fingerprint mismatch: mass={body.mass:0.###} kg, " +
                    $"COM={body.centerOfMass}. Expected 11000 kg and (0,-0.5,0).";
                return false;
            }

            string[] requiredThrusters =
            {
                "Hover_FL", "Hover_FR", "Hover_RL", "Hover_RR",
                "Roof_FL", "Roof_FR", "Roof_RL", "Roof_RR",
                "Main_Rear", "Brake_Front",
                "Strafe_Front_Left", "Strafe_Front_Right",
                "Strafe_Back_Left", "Strafe_Back_Right"
            };
            for (int i = 0; i < requiredThrusters.Length; i++)
            {
                if (FindChild(root.transform, requiredThrusters[i]) == null)
                {
                    status =
                        $"V2 reference fingerprint mismatch: missing '{requiredThrusters[i]}'.";
                    return false;
                }
            }

            status = "V2 reference fingerprint accepted. " + V2ForceUnitNote;
            return true;
        }

        private void ResolveV3()
        {
            if (v3Assembler == null || v3Assembler.AssembledRoot == null)
            {
                return;
            }

            GameObject root = v3Assembler.AssembledRoot;
            v3Body = root.GetComponent<Rigidbody>();
            v3Pipeline = root.GetComponent<V3ControllerPipeline>();
            v3Power = root.GetComponent<V3PowerDistributor>();
        }

        private void ResolveV2()
        {
            if (v2CraftRoot == null)
            {
                return;
            }

            v2Body = v2CraftRoot.GetComponent<Rigidbody>();
            v2PilotInput = FindComponentByTypeName(v2CraftRoot, "PilotCommandInterface");
            v2EnergyCore = FindComponentByTypeName(v2CraftRoot, "EnergyCore");
            if (v2PilotInput == null)
            {
                return;
            }

            v2CommandProperty = v2PilotInput.GetType().GetProperty(
                "CurrentCommand",
                BindingFlags.Instance | BindingFlags.Public);
            v2CommandType = v2CommandProperty?.PropertyType;
        }

        private void ApplyV2Command(V3PilotCommand command)
        {
            if (v2PilotInput == null ||
                v2CommandProperty == null ||
                v2CommandType == null)
            {
                return;
            }

            object legacyCommand = Activator.CreateInstance(v2CommandType);
            SetField(legacyCommand, "sampleFrame", ++v2SampleFrame);
            SetField(legacyCommand, "throttle", command.Throttle);
            SetField(legacyCommand, "edgeShift", command.Strafe);
            SetField(legacyCommand, "steer", command.Yaw);
            SetField(legacyCommand, "pitch", command.Pitch);
            SetField(legacyCommand, "roofThrustersHeld", command.Downforce > 0.5f);
            SetField(legacyCommand, "bottomThrustersHeld", command.Lift > 0.5f);
            SetField(legacyCommand, "gripBreakerHeld", command.GripBreaker);
            v2CommandProperty.SetValue(v2PilotInput, legacyCommand);
        }

        private void ReadV2Power(out float requested, out float granted)
        {
            requested = 0f;
            granted = 0f;
            if (v2EnergyCore == null)
            {
                return;
            }

            PropertyInfo currentEnergy = v2EnergyCore.GetType().GetProperty(
                "CurrentEnergy",
                BindingFlags.Instance | BindingFlags.Public);
            object state = currentEnergy?.GetValue(v2EnergyCore);
            if (state == null)
            {
                return;
            }

            requested = ReadFloatField(state, "totalRequested");
            granted = ReadFloatField(state, "totalGranted");
        }

        private void CalibrateResetHeightsFromIdle()
        {
            if (v2Body != null)
            {
                v2SpawnPosition.y = v2Body.position.y;
            }

            if (v3Body != null)
            {
                v3SpawnPosition.y = v3Body.position.y;
            }
        }

        private void ResetV2DynamicState()
        {
            ResetV2Component(
                "DriveCore",
                ("_currentMainThrottle", (object)0f),
                ("_currentBrakeThrottle", 0f));
            ResetV2Component(
                "HoverStabilizerArray",
                ("_leanTargetRoll", (object)0f),
                ("_leanTargetPitch", 0f),
                ("_pitchCommandTarget", 0f),
                ("_hoverThrottleFL", 0f),
                ("_hoverThrottleFR", 0f),
                ("_hoverThrottleRL", 0f),
                ("_hoverThrottleRR", 0f),
                ("_roofThrottleFL", 0f),
                ("_roofThrottleFR", 0f),
                ("_roofThrottleRL", 0f),
                ("_roofThrottleRR", 0f),
                ("_gravityThrottle", 0f),
                ("_speedStiffness", 1f),
                ("_curvatureAccel", 0f),
                ("_surfaceAngularVelocity", Vector3.zero),
                ("_prevGroundNormal", Vector3.up),
                ("_hasPrevGroundNormal", false),
                ("_predictedSurfaceNormal", Vector3.up),
                ("_predictedSurfaceDistance", 0f),
                ("_hasPredictedSurface", false),
                ("_smoothSurfaceNormal", Vector3.up),
                ("_hasSmoothSurfaceNormal", false),
                ("_smoothAlignTarget", Vector3.up),
                ("_hasSmoothAlignTarget", false));

            Component traction = FindComponentByTypeName(v2CraftRoot, "TractionCore");
            MethodInfo start = traction?.GetType().GetMethod(
                "Start",
                BindingFlags.Instance | BindingFlags.NonPublic);
            start?.Invoke(traction, null);

            Component[] components = v2CraftRoot.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    string.Equals(
                        components[i].GetType().Name,
                        "ThrusterNode",
                        StringComparison.Ordinal))
                {
                    PropertyInfo throttle = components[i].GetType().GetProperty(
                        "Throttle",
                        BindingFlags.Instance | BindingFlags.Public);
                    throttle?.SetValue(components[i], 0f);
                }
            }
        }

        private void ResetV2Component(
            string typeName,
            params (string fieldName, object value)[] values)
        {
            Component component = FindComponentByTypeName(v2CraftRoot, typeName);
            if (component == null)
            {
                return;
            }

            Type type = component.GetType();
            for (int i = 0; i < values.Length; i++)
            {
                FieldInfo field = type.GetField(
                    values[i].fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                field?.SetValue(component, values[i].value);
            }
        }

        private void ExportRecorder(
            V3ParityTrackRecorder recorder,
            V3ParityRunSummary summary,
            string directory,
            string runId)
        {
            if (recorder == null || summary == null)
            {
                return;
            }

            recorder.Export(directory, runId, summary);
        }

        private V3ParityRunSummary BuildSummary(
            V3ParityTrackRecorder recorder)
        {
            return recorder?.BuildSummary(
                profile.name,
                recorder.CraftLabel == "V2"
                    ? V2ForceUnitNote
                    : "Native V3 force units.");
        }

        private static void ResetCraft(
            Rigidbody body,
            Vector3 position,
            Quaternion rotation,
            float initialForwardSpeedKmh)
        {
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity =
                rotation * Vector3.forward * (initialForwardSpeedKmh / 3.6f);
            Physics.SyncTransforms();
        }

        private static Component FindComponentByTypeName(
            GameObject root,
            string typeName)
        {
            MonoBehaviour[] components =
                root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    string.Equals(
                        components[i].GetType().Name,
                        typeName,
                        StringComparison.Ordinal))
                {
                    return components[i];
                }
            }

            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, name, StringComparison.Ordinal))
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            field?.SetValue(target, value);
        }

        private static float ReadFloatField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            return field != null ? Convert.ToSingle(field.GetValue(target)) : 0f;
        }

        private static bool Contains(string value, string fragment)
        {
            return value != null &&
                value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
