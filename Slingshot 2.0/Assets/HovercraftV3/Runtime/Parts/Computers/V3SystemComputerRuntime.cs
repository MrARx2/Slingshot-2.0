using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3SystemComputerState
    {
        Uninitialized,
        Disconnected,
        Installed,
        Booting,
        Operational,
        Underpowered,
        SoftwareMissing,
        SoftwareIncompatible,
        Faulted,
        Shutdown
    }

    [DisallowMultipleComponent]
    public sealed class V3SystemComputerRuntime :
        MonoBehaviour,
        IV3RuntimeDevice
    {
        private RuntimePartInstance part;
        private V3SystemComputerDefinition definition;
        private V3CraftMainframe mainframe;

        public RuntimePartInstance Part => part;
        public V3SystemComputerDefinition Definition => definition;
        public V3SystemComputerRole Role =>
            definition != null
                ? definition.ComputerRole
                : default;
        public V3SystemSoftwareDefinition InstalledSoftware =>
            definition != null
                ? definition.BundledSoftware
                : null;
        public V3SystemComputerState State { get; private set; } =
            V3SystemComputerState.Uninitialized;
        public string StateReason { get; private set; } = string.Empty;
        public bool IsTaskRegistered { get; private set; }
        public float RequestedRateHz { get; private set; }
        public float GrantedRateHz { get; private set; }
        public float ActualExecutionRateHz { get; private set; }
        public double LastExecutionTime { get; private set; } = -1d;
        public bool HasPublishedOutput { get; private set; }
        public string RuntimeDeviceId =>
            definition != null
                ? definition.StableId
                : string.Empty;
        public string DeviceDisplayName =>
            definition != null
                ? definition.DisplayName
                : gameObject.name;
        public bool IsDeviceOnline =>
            State == V3SystemComputerState.Operational;
        public Component DeviceComponent => this;

        public bool Initialize(RuntimePartInstance runtimePart)
        {
            part = runtimePart;
            definition = part != null
                ? part.Definition as V3SystemComputerDefinition
                : null;
            IsTaskRegistered = false;
            RequestedRateHz = 0f;
            GrantedRateHz = 0f;
            ActualExecutionRateHz = 0f;
            LastExecutionTime = -1d;
            HasPublishedOutput = false;
            if (part == null || definition == null)
            {
                State = V3SystemComputerState.Faulted;
                StateReason =
                    "Computer runtime requires a computer part definition.";
                return false;
            }

            if (definition.BundledSoftware == null)
            {
                State = V3SystemComputerState.SoftwareMissing;
                StateReason = "No bundled software is installed.";
                return false;
            }

            if (definition.BundledSoftware.Role !=
                definition.ComputerRole)
            {
                State =
                    V3SystemComputerState.SoftwareIncompatible;
                StateReason =
                    "Bundled software role does not match the computer.";
                return false;
            }

            State = V3SystemComputerState.Installed;
            StateReason = "Awaiting Mainframe.";
            return true;
        }

        public void BindMainframe(V3CraftMainframe owner)
        {
            mainframe = owner;
            if (definition == null)
            {
                State = V3SystemComputerState.Faulted;
                return;
            }

            State = owner != null
                ? V3SystemComputerState.Booting
                : V3SystemComputerState.Disconnected;
            StateReason = owner != null
                ? "Registering software task."
                : "Mainframe disconnected.";
        }

        internal void ApplyTaskState(
            V3ScheduledSoftwareTask task)
        {
            IsTaskRegistered = task != null;
            if (task == null)
            {
                State = V3SystemComputerState.SoftwareMissing;
                StateReason = "No scheduler task is registered.";
                return;
            }

            RequestedRateHz = task.RequestedRateHz;
            GrantedRateHz = task.GrantedRateHz;
            ActualExecutionRateHz = task.MeasuredActualRateHz;
            LastExecutionTime = task.LastExecutionTimestamp;
            HasPublishedOutput = task.HasPublishedOutput;
            if (task.IsFaulted)
            {
                State = V3SystemComputerState.Faulted;
                StateReason = task.LastFailure;
            }
            else if (!task.IsOperational)
            {
                State = V3SystemComputerState.Underpowered;
                StateReason = task.Bottleneck;
            }
            else
            {
                State = V3SystemComputerState.Operational;
                StateReason = "Hardware and software operational.";
            }
        }
    }
}
