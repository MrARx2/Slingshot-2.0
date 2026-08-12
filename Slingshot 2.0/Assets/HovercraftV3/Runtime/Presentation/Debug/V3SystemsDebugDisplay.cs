using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3SystemsDebugDisplay : MonoBehaviour
    {
        private enum Page
        {
            Mainframe,
            Scheduler,
            Sensors,
            Router,
            Devices,
            Suspension,
            Aerodynamics
        }

        [SerializeField] private bool visible = true;
        [SerializeField] private Key cycleKey = Key.F4;
        private V3CraftMainframe mainframe;
        private V3CraftRuntime runtime;
        private Page page;

        public void Initialize(
            V3CraftMainframe value,
            V3CraftRuntime craft)
        {
            mainframe = value;
            runtime = craft;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard[cycleKey].wasPressedThisFrame)
            {
                page = (Page)(((int)page + 1) %
                    System.Enum.GetValues(typeof(Page)).Length);
            }
        }

        private void OnGUI()
        {
            if (!visible || mainframe == null)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(Screen.width - 430f, 12f, 418f, 520f),
                GUI.skin.box);
            GUILayout.Label(
                $"V3 SYSTEMS [{page}]  (F4 next)");
            switch (page)
            {
                case Page.Mainframe:
                    DrawMainframe();
                    break;
                case Page.Scheduler:
                    DrawScheduler();
                    break;
                case Page.Sensors:
                    DrawSensors();
                    break;
                case Page.Router:
                    DrawRouter();
                    break;
                case Page.Devices:
                    DrawDevices();
                    break;
                case Page.Suspension:
                    DrawSuspension();
                    break;
                case Page.Aerodynamics:
                    DrawAerodynamics();
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawMainframe()
        {
            GUILayout.Label($"Boot: {mainframe.BootState}");
            GUILayout.Label(
                $"Definition: {mainframe.Definition?.DisplayName ?? "None"}");
            GUILayout.Label(
                $"Observations: {mainframe.Observations.Snapshot.Count}");
            if (!string.IsNullOrEmpty(mainframe.FaultReason))
            {
                GUILayout.Label($"Health: {mainframe.FaultReason}");
            }
        }

        private void DrawScheduler()
        {
            V3SoftwareScheduler scheduler = mainframe.Scheduler;
            GUILayout.Label(
                $"Power {scheduler.GrantedPower:F1}/" +
                $"{scheduler.RequestedPower:F1}");
            GUILayout.Label(
                $"Compute {scheduler.UsedComputePerSecond:F1}/s");
            for (int i = 0; i < scheduler.Tasks.Count; i++)
            {
                V3ScheduledSoftwareTask task = scheduler.Tasks[i];
                GUILayout.Label(
                    $"{task.Software.Role}: {task.GrantedRateHz:F1}/" +
                    $"{task.RequestedRateHz:F1} Hz actual=" +
                    $"{task.MeasuredActualRateHz:F1} runs=" +
                    $"{task.ExecutionCount} [{task.Bottleneck}]");
            }
        }

        private void DrawSensors()
        {
            for (int i = 0; i < mainframe.Sensors.Count; i++)
            {
                V3DirectionalSensorDeviceRuntime sensor =
                    mainframe.Sensors[i];
                GUILayout.Label(
                    $"{sensor.Definition.Direction}: " +
                    $"{sensor.GrantedRateHz:F1}/" +
                    $"{sensor.RequestedRateHz:F1} Hz actual=" +
                    $"{sensor.MeasuredRateHz:F1} samples=" +
                    $"{sensor.SampleCount} {sensor.HealthState} " +
                    $"[{sensor.Bottleneck}]");
            }
        }

        private void DrawRouter()
        {
            GUILayout.Label(
                $"Profile: " +
                $"{mainframe.ControlRouter.Profile?.DisplayName ?? "Fallback Balanced"}");
            for (int i = 0;
                i < mainframe.ControlRouter.Requests.Count;
                i++)
            {
                V3SystemRequest request =
                    mainframe.ControlRouter.Requests[i];
                GUILayout.Label(
                    $"{request.Domain}: {request.SourceSystemId} " +
                    $"state={request.RequestedState:F2}");
            }
        }

        private void DrawDevices()
        {
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                GUILayout.Label(
                    $"{part.Definition.DisplayName}: " +
                    $"out {part.CurrentOutput:F2}, " +
                    $"P {part.GrantedPower:F1}/{part.RequestedPower:F1}, " +
                    $"{part.CurrentTemperatureC:F1} C");
            }
        }

        private void DrawSuspension()
        {
            RuntimeSpringMountInstance[] springs =
                GetComponentsInChildren<RuntimeSpringMountInstance>(true);
            for (int i = 0; i < springs.Length; i++)
            {
                GUILayout.Label(
                    $"{springs[i].ParentSocketId}: " +
                    $"{springs[i].DisplacementM:F3} m");
            }
        }

        private void DrawAerodynamics()
        {
            RuntimeAerodynamicFinInstance[] fins =
                GetComponentsInChildren<RuntimeAerodynamicFinInstance>(true);
            for (int i = 0; i < fins.Length; i++)
            {
                GUILayout.Label(
                    $"{fins[i].ParentSocketId}: " +
                    $"{fins[i].CurrentAerodynamicForceN:F0} N");
            }

            RuntimeCoolingModuleInstance[] cooling =
                GetComponentsInChildren<RuntimeCoolingModuleInstance>(true);
            for (int i = 0; i < cooling.Length; i++)
            {
                GUILayout.Label(
                    $"Cooling: " +
                    $"{cooling[i].CurrentHeatRemovalPerSecond:F1} heat/s");
            }
        }
    }
}
