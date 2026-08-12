using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3SystemRuntimeState
    {
        Uninitialized,
        Booting,
        Operational,
        Degraded,
        Faulted,
        Shutdown
    }

    public readonly struct V3SystemValidationResult
    {
        public V3SystemValidationResult(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason ?? string.Empty;
        }

        public bool IsValid { get; }
        public string Reason { get; }
    }

    public readonly struct V3SystemRuntimeSnapshot
    {
        public V3SystemRuntimeSnapshot(
            V3SystemComputerRole role,
            V3SystemRuntimeState state,
            long executionCount,
            double lastExecutionTime,
            bool outputPublished,
            string reason)
        {
            Role = role;
            State = state;
            ExecutionCount = executionCount;
            LastExecutionTime = lastExecutionTime;
            OutputPublished = outputPublished;
            Reason = reason ?? string.Empty;
        }

        public V3SystemComputerRole Role { get; }
        public V3SystemRuntimeState State { get; }
        public long ExecutionCount { get; }
        public double LastExecutionTime { get; }
        public bool OutputPublished { get; }
        public string Reason { get; }
    }

    public sealed class V3SystemRuntimeContext
    {
        public V3CraftMainframe Mainframe { get; internal set; }
        public V3CraftRuntime Craft { get; internal set; }
        public V3ObservationBus Observations { get; internal set; }
        public V3IntentBus Intents { get; internal set; }
        public V3ControlRouter ControlRouter { get; internal set; }
        public V3ActuatorCommandRouter ActuatorRouter
        {
            get;
            internal set;
        }
        public V3CraftTelemetryHub Telemetry { get; internal set; }
        public V3DriveController Drive { get; internal set; }
        public V3HoverController Hover { get; internal set; }
        public V3VectorController Vector { get; internal set; }
        public V3TractionController Traction { get; internal set; }
        public V3GimbalController Gimbal { get; internal set; }
        public V3PilotCommand PilotCommand { get; internal set; }
        public V3PilotCommand ScheduledCommand { get; internal set; }
        public V3PilotCommand PhysicsCommand { get; internal set; }
        public double Time { get; internal set; }
    }

    public interface IV3CraftSystemRuntime
    {
        V3SystemComputerRole Role { get; }
        V3SystemComputerRuntime Computer { get; }
        V3SystemSoftwareDefinition Software { get; }
        V3SystemRuntimeState State { get; }
        void AttachComputer(V3SystemComputerRuntime computer);
        void Initialize(V3SystemRuntimeContext context);
        V3SystemValidationResult Validate();
        void ScheduledTick(float scheduledDeltaTime);
        void ApplyPhysicsTick(float physicsDeltaTime);
        void ResetDynamicState();
        void EnterDegradedState(string reason);
        void Shutdown();
        V3SystemRuntimeSnapshot CaptureSnapshot();
    }

    public abstract class V3CraftSystemRuntimeBase :
        MonoBehaviour,
        IV3CraftSystemRuntime
    {
        private V3SystemComputerRuntime computer;
        protected V3SystemRuntimeContext Context { get; private set; }
        protected string StateReason { get; private set; } =
            string.Empty;
        protected bool OutputPublished { get; set; }
        protected long ExecutionCount { get; private set; }
        protected double LastExecutionTime { get; private set; } = -1d;

        public abstract V3SystemComputerRole Role { get; }
        public V3SystemComputerRuntime Computer => computer;
        public V3SystemSoftwareDefinition Software =>
            computer != null ? computer.InstalledSoftware : null;
        public V3SystemRuntimeState State { get; private set; } =
            V3SystemRuntimeState.Uninitialized;

        public void AttachComputer(V3SystemComputerRuntime value)
        {
            computer = value;
        }

        public virtual void Initialize(V3SystemRuntimeContext context)
        {
            Context = context;
            ExecutionCount = 0;
            LastExecutionTime = -1d;
            OutputPublished = false;
            V3SystemValidationResult validation = Validate();
            State = validation.IsValid
                ? V3SystemRuntimeState.Booting
                : V3SystemRuntimeState.Faulted;
            StateReason = validation.Reason;
        }

        public virtual V3SystemValidationResult Validate()
        {
            if (computer == null)
            {
                return new V3SystemValidationResult(
                    false,
                    "Matching computer runtime is missing.");
            }

            if (computer.Role != Role)
            {
                return new V3SystemValidationResult(
                    false,
                    "Computer role does not match system runtime.");
            }

            if (Software == null || Software.Role != Role)
            {
                return new V3SystemValidationResult(
                    false,
                    "Compatible software is missing.");
            }

            if (Context == null || Context.Mainframe == null)
            {
                return new V3SystemValidationResult(
                    false,
                    "Mainframe context is missing.");
            }

            return new V3SystemValidationResult(true, string.Empty);
        }

        public void ScheduledTick(float scheduledDeltaTime)
        {
            if (State == V3SystemRuntimeState.Faulted ||
                State == V3SystemRuntimeState.Shutdown)
            {
                return;
            }

            OnScheduledTick(Mathf.Max(0f, scheduledDeltaTime));
            ExecutionCount++;
            LastExecutionTime =
                Context != null ? Context.Time : Time.timeAsDouble;
            State = V3SystemRuntimeState.Operational;
            StateReason = string.Empty;
        }

        protected abstract void OnScheduledTick(
            float scheduledDeltaTime);

        public virtual void ApplyPhysicsTick(float physicsDeltaTime)
        {
        }

        public virtual void ResetDynamicState()
        {
            ExecutionCount = 0;
            LastExecutionTime = -1d;
            OutputPublished = false;
            V3SystemValidationResult validation = Validate();
            State = validation.IsValid
                ? V3SystemRuntimeState.Booting
                : V3SystemRuntimeState.Faulted;
            StateReason = validation.Reason;
            OnDynamicStateReset();
        }

        protected virtual void OnDynamicStateReset()
        {
        }

        public void EnterDegradedState(string reason)
        {
            State = V3SystemRuntimeState.Degraded;
            StateReason = reason ?? string.Empty;
        }

        public void Shutdown()
        {
            State = V3SystemRuntimeState.Shutdown;
            StateReason = "System shutdown.";
            OutputPublished = false;
        }

        public V3SystemRuntimeSnapshot CaptureSnapshot()
        {
            return new V3SystemRuntimeSnapshot(
                Role,
                State,
                ExecutionCount,
                LastExecutionTime,
                OutputPublished,
                StateReason);
        }
    }
}
