using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3CraftResetReason
    {
        Unknown,
        ManualInput,
        KillPlane,
        TrackGenerator,
        TestAutomation
    }

    /// <summary>
    /// Explicit recovery boundary shared with diagnostics. A reset is an
    /// external state discontinuity, not craft dynamics.
    /// </summary>
    public readonly struct V3CraftResetNotification
    {
        public V3CraftResetNotification(
            int sequence,
            V3CraftResetReason reason,
            string label,
            Vector3 previousPosition,
            Vector3 resetPosition)
        {
            Sequence = Mathf.Max(0, sequence);
            Reason = reason;
            Label = label ?? string.Empty;
            PreviousPosition = previousPosition;
            ResetPosition = resetPosition;
            Timestamp = Time.unscaledTimeAsDouble;
        }

        public int Sequence { get; }
        public V3CraftResetReason Reason { get; }
        public string Label { get; }
        public Vector3 PreviousPosition { get; }
        public Vector3 ResetPosition { get; }
        public double Timestamp { get; }
        public bool IsManual => Reason == V3CraftResetReason.ManualInput;
    }

    /// <summary>
    /// Owns the small amount of scene-level state needed for hands-on V3 testing:
    /// craft binding, recovery, cursor capture, and HUD visibility.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3FreeDriveSession : MonoBehaviour
    {
        [SerializeField] private V3CraftAssembler assembler;
        [SerializeField] private V3PilotInputAdapter localPilotInput;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private LayerMask trackSurfaceMask = 1 << 8;
        [SerializeField] private bool lockCursorOnStart = true;
        [SerializeField] private bool autoRecoverBelowKillPlane = true;
        [SerializeField] private float killPlaneY = -20f;
        [SerializeField] private float resetLiftMetres = 0.15f;

        private V3CraftRuntime craftRuntime;
        private V3CraftTelemetryHub telemetry;
        private V3ControllerPipeline pipeline;
        private V3PilotInputAdapter pilotInput;
        private V3CockpitWarningController warnings;
        private V3ThrusterDebugView thrusterDebug;
        private Rigidbody craftBody;
        private Vector3 capturedSpawnPosition;
        private Quaternion capturedSpawnRotation = Quaternion.identity;
        private bool hasCapturedSpawn;
        private bool pilotInputEnabled = true;
        private string statusMessage = "ASSEMBLING CRAFT";
        private float statusMessageUntil;

        public V3CraftAssembler Assembler => assembler;
        public V3CraftRuntime CraftRuntime => craftRuntime;
        public V3CraftTelemetryHub Telemetry => telemetry;
        public V3PilotInputAdapter PilotInput => localPilotInput;
        public V3CockpitWarningController Warnings => warnings;
        public V3ThrusterDebugView ThrusterDebug => thrusterDebug;
        public Rigidbody CraftBody => craftBody;
        public bool IsCraftReady => craftBody != null;
        public bool IsPilotInputEnabled => pilotInputEnabled;
        public bool ShowHud { get; private set; } = true;
        public int ResetSequence { get; private set; }
        public float KillPlaneY => killPlaneY;
        public event Action<V3CraftResetNotification> CraftResetPerformed;
        public string StatusMessage =>
            Time.unscaledTime <= statusMessageUntil
                ? statusMessage
                : IsCraftReady
                    ? "FREE DRIVE READY"
                    : "ASSEMBLING CRAFT";

        private void Start()
        {
            EnsureLocalPilotInput();
            EnsureDevelopmentTools();
            SetCursorLocked(lockCursorOnStart);
            RefreshCraftBinding();
        }

        private void EnsureDevelopmentTools()
        {
            V3SystemsConsoleController console =
                GetComponent<V3SystemsConsoleController>();
            if (console == null)
            {
                console =
                    gameObject.AddComponent<V3SystemsConsoleController>();
            }

            V3CraftTestSpawner spawner =
                GetComponent<V3CraftTestSpawner>();
            if (spawner == null)
            {
                spawner = gameObject.AddComponent<V3CraftTestSpawner>();
            }
        }

        private void Update()
        {
            RefreshCraftBinding();
            ReadSessionInput();

            if (autoRecoverBelowKillPlane &&
                craftBody != null &&
                craftBody.position.y < killPlaneY)
            {
                ResetCraft(
                    V3CraftResetReason.KillPlane,
                    "AUTO RECOVERY");
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SetCursorLocked(false);
            }
        }

        public bool RefreshCraftBinding()
        {
            GameObject assembledRoot =
                assembler != null ? assembler.AssembledRoot : null;
            if (assembledRoot == null)
            {
                ClearBinding();
                return false;
            }

            V3CraftRuntime candidate =
                assembledRoot.GetComponent<V3CraftRuntime>();
            if (candidate == null || candidate.RootRigidbody == null)
            {
                ClearBinding();
                return false;
            }

            if (craftRuntime == candidate)
            {
                return true;
            }

            craftRuntime = candidate;
            craftBody = candidate.RootRigidbody;
            telemetry = assembledRoot.GetComponent<V3CraftTelemetryHub>();
            pipeline = assembledRoot.GetComponent<V3ControllerPipeline>();
            EnsureLocalPilotInput();
            pilotInput = localPilotInput;
            warnings =
                assembledRoot.GetComponent<V3CockpitWarningController>();
            thrusterDebug =
                assembledRoot.GetComponent<V3ThrusterDebugView>();

            V3ThermalDebugDisplay thermalDisplay =
                assembledRoot.GetComponent<V3ThermalDebugDisplay>();
            if (thermalDisplay != null)
            {
                thermalDisplay.ShowOverlay = false;
            }

            V3CockpitWarningDisplay warningDisplay =
                assembledRoot.GetComponent<V3CockpitWarningDisplay>();
            if (warningDisplay != null)
            {
                warningDisplay.ShowOverlay = false;
            }

            if (!hasCapturedSpawn)
            {
                RefreshCapturedSpawnPose();
            }

            pilotInput?.BindCraft(
                pipeline,
                assembledRoot.GetComponent<V3CraftMainframe>());
            pilotInput?.SetCursorCaptured(pilotInputEnabled);

            SetStatus("CRAFT ONLINE", 2f);
            return true;
        }

        public void Configure(
            V3CraftAssembler craftAssembler,
            Transform craftSpawnPoint,
            V3PilotInputAdapter inputSource)
        {
            if (assembler != craftAssembler)
            {
                ClearBinding();
            }

            assembler = craftAssembler;
            if (spawnPoint != craftSpawnPoint)
            {
                spawnPoint = craftSpawnPoint;
                hasCapturedSpawn = false;
            }
            localPilotInput = inputSource;
            EnsureLocalPilotInput();
            localPilotInput.SetCursorCaptured(pilotInputEnabled);
            RefreshCraftBinding();
        }

        [ContextMenu("Reset Free Drive Craft")]
        public bool ResetCraft()
        {
            return ResetCraft(
                V3CraftResetReason.ManualInput,
                "MANUAL RESET");
        }

        public bool ResetCraft(string reason)
        {
            return ResetCraft(V3CraftResetReason.Unknown, reason);
        }

        public bool ResetCraft(
            V3CraftResetReason resetReason,
            string reason)
        {
            if (!RefreshCraftBinding() ||
                craftBody == null)
            {
                SetStatus("RESET UNAVAILABLE", 2f);
                return false;
            }

            // The procedural track generator owns the authoritative spawn
            // transform. Re-read it for every recovery so regenerating a track
            // cannot leave the session using a stale captured pose.
            if (!RefreshCapturedSpawnPose())
            {
                SetStatus("RESET UNAVAILABLE", 2f);
                return false;
            }

            Vector3 previousPosition = craftBody.position;
            V3CraftRuntime craftRuntime =
                craftBody.GetComponent<V3CraftRuntime>();
            craftRuntime?.ResetDynamicState(new V3DynamicResetContext(
                V3DynamicResetPhase.BeforePoseReset,
                reason));
            if (craftRuntime == null)
            {
                pipeline?.ResetDynamicState();
            }
            craftBody.position =
                capturedSpawnPosition +
                capturedSpawnRotation * Vector3.up * resetLiftMetres;
            craftBody.rotation = capturedSpawnRotation;
            craftBody.linearVelocity = Vector3.zero;
            craftBody.angularVelocity = Vector3.zero;
            craftBody.Sleep();
            craftBody.WakeUp();
            Physics.SyncTransforms();
            craftRuntime?.ResetDynamicState(new V3DynamicResetContext(
                V3DynamicResetPhase.AfterPoseReset,
                reason));
            telemetry?.RefreshLiveTelemetry();

            ResetSequence++;
            SetStatus(reason, 2f);
            CraftResetPerformed?.Invoke(new V3CraftResetNotification(
                ResetSequence,
                resetReason,
                reason,
                previousPosition,
                craftBody.position));
            return true;
        }

        /// <summary>
        /// Uses generated geometry bounds rather than a fixed world assumption.
        /// The margin must be large enough for intentional drops and jump arcs.
        /// </summary>
        public void ConfigureKillPlaneFromTrackBounds(
            Bounds worldBounds,
            float safetyMargin = 250f)
        {
            if (worldBounds.size.sqrMagnitude <= 0f)
                return;

            killPlaneY = worldBounds.min.y - Mathf.Max(25f, safetyMargin);
        }

        private bool RefreshCapturedSpawnPose()
        {
            if (spawnPoint != null)
            {
                capturedSpawnPosition = spawnPoint.position;
                capturedSpawnRotation = spawnPoint.rotation;
                hasCapturedSpawn = true;
                return true;
            }

            if (craftBody != null)
            {
                capturedSpawnPosition = craftBody.position;
                capturedSpawnRotation = craftBody.rotation;
                hasCapturedSpawn = true;
                return true;
            }

            hasCapturedSpawn = false;
            return false;
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState =
                locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            pilotInputEnabled = locked;
            EnsureLocalPilotInput();
            pilotInput?.SetCursorCaptured(locked);

            if (!locked)
            {
                SetStatus("INPUT PAUSED - CLICK TO CAPTURE", 2f);
            }
        }

        public bool ToggleThrusterDebug()
        {
            if (!RefreshCraftBinding() || thrusterDebug == null)
            {
                SetStatus("THRUSTER DEBUG UNAVAILABLE", 2f);
                return false;
            }

            thrusterDebug.ToggleVisualization();
            SetStatus(
                thrusterDebug.ShowVisualization
                    ? "THRUSTER DEBUG ON - CYAN FACING / BRIGHT FIRING"
                    : "THRUSTER DEBUG OFF",
                2.5f);
            return thrusterDebug.ShowVisualization;
        }

        public bool TryGetAltitude(out float altitude)
        {
            altitude = 0f;
            if (craftBody == null)
            {
                return false;
            }

            if (!Physics.Raycast(
                    craftBody.worldCenterOfMass,
                    Vector3.down,
                    out RaycastHit hit,
                    1000f,
                    trackSurfaceMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            altitude = hit.distance;
            return true;
        }

        private void ReadSessionInput()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;

            if ((keyboard != null &&
                 keyboard.backspaceKey.wasPressedThisFrame) ||
                (gamepad != null &&
                 gamepad.selectButton.wasPressedThisFrame))
            {
                ResetCraft();
            }

            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                ShowHud = !ShowHud;
            }

            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
            {
                ToggleThrusterDebug();
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(false);
            }
            else if (!pilotInputEnabled &&
                     Mouse.current != null &&
                     Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
                SetStatus("PILOT INPUT ACTIVE", 1.5f);
            }
        }

        private void ClearBinding()
        {
            pilotInput?.UnbindCraft(pipeline);
            craftRuntime = null;
            telemetry = null;
            pipeline = null;
            pilotInput = localPilotInput;
            warnings = null;
            thrusterDebug = null;
            craftBody = null;
        }

        public void SetPilotInputSuspendedByUi(bool suspended)
        {
            EnsureLocalPilotInput();
            localPilotInput?.SetUiSuspended(suspended);
        }

        private void EnsureLocalPilotInput()
        {
            if (localPilotInput == null)
            {
                localPilotInput =
                    GetComponentInChildren<V3PilotInputAdapter>(true);
            }

            if (localPilotInput == null)
            {
                var inputObject =
                    new GameObject("Local Pilot Input Source");
                inputObject.transform.SetParent(transform, false);
                localPilotInput =
                    inputObject.AddComponent<V3PilotInputAdapter>();
            }

            pilotInput = localPilotInput;
        }

        private void SetStatus(string message, float duration)
        {
            statusMessage = message;
            statusMessageUntil =
                Time.unscaledTime + Mathf.Max(0f, duration);
        }
    }
}
