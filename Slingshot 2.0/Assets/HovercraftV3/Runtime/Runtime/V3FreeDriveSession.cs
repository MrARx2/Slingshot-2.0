using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Owns the small amount of scene-level state needed for hands-on V3 testing:
    /// craft binding, recovery, cursor capture, and HUD visibility.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3FreeDriveSession : MonoBehaviour
    {
        [SerializeField] private V3CraftAssembler assembler;
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
        public V3PilotInputAdapter PilotInput => pilotInput;
        public V3CockpitWarningController Warnings => warnings;
        public V3ThrusterDebugView ThrusterDebug => thrusterDebug;
        public Rigidbody CraftBody => craftBody;
        public bool IsCraftReady => craftBody != null;
        public bool IsPilotInputEnabled => pilotInputEnabled;
        public bool ShowHud { get; private set; } = true;
        public int ResetSequence { get; private set; }
        public string StatusMessage =>
            Time.unscaledTime <= statusMessageUntil
                ? statusMessage
                : IsCraftReady
                    ? "FREE DRIVE READY"
                    : "ASSEMBLING CRAFT";

        private void Start()
        {
            SetCursorLocked(lockCursorOnStart);
            RefreshCraftBinding();
        }

        private void Update()
        {
            RefreshCraftBinding();
            ReadSessionInput();

            if (autoRecoverBelowKillPlane &&
                craftBody != null &&
                craftBody.position.y < killPlaneY)
            {
                ResetCraft("AUTO RECOVERY");
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
            pilotInput = assembledRoot.GetComponent<V3PilotInputAdapter>();
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
                capturedSpawnPosition =
                    spawnPoint != null
                        ? spawnPoint.position
                        : craftBody.position;
                capturedSpawnRotation =
                    spawnPoint != null
                        ? spawnPoint.rotation
                        : craftBody.rotation;
                hasCapturedSpawn = true;
            }

            if (pilotInput != null)
            {
                pilotInput.enabled = pilotInputEnabled;
            }

            SetStatus("CRAFT ONLINE", 2f);
            return true;
        }

        [ContextMenu("Reset Free Drive Craft")]
        public bool ResetCraft()
        {
            return ResetCraft("MANUAL RESET");
        }

        public bool ResetCraft(string reason)
        {
            if (!RefreshCraftBinding() ||
                craftBody == null ||
                !hasCapturedSpawn)
            {
                SetStatus("RESET UNAVAILABLE", 2f);
                return false;
            }

            pipeline?.ResetDynamicState();
            craftBody.position =
                capturedSpawnPosition + Vector3.up * resetLiftMetres;
            craftBody.rotation = capturedSpawnRotation;
            craftBody.linearVelocity = Vector3.zero;
            craftBody.angularVelocity = Vector3.zero;
            craftBody.Sleep();
            craftBody.WakeUp();
            Physics.SyncTransforms();
            telemetry?.RefreshLiveTelemetry();

            ResetSequence++;
            SetStatus(reason, 2f);
            return true;
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState =
                locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            pilotInputEnabled = locked;
            if (pilotInput != null)
            {
                pilotInput.enabled = locked;
            }

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
            craftRuntime = null;
            telemetry = null;
            pipeline = null;
            pilotInput = null;
            warnings = null;
            thrusterDebug = null;
            craftBody = null;
        }

        private void SetStatus(string message, float duration)
        {
            statusMessage = message;
            statusMessageUntil =
                Time.unscaledTime + Mathf.Max(0f, duration);
        }
    }
}
