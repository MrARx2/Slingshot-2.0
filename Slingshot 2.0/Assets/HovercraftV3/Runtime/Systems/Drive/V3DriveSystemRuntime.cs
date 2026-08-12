namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3DriveSystemRuntime :
        V3CraftSystemRuntimeBase
    {
        private V3PilotCommand scheduledCommand;

        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.Drive;

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            scheduledCommand = Context.ScheduledCommand;
            OutputPublished = Context.Drive != null;
        }

        public override void ApplyPhysicsTick(float physicsDeltaTime)
        {
            if (scheduledCommand.CyclePowerAllocationMode)
            {
                Context.ActuatorRouter?.CyclePowerAllocationMode();
            }

            Context.Drive?.Submit(
                scheduledCommand,
                physicsDeltaTime);
            scheduledCommand.CyclePowerAllocationMode = false;
        }

        protected override void OnDynamicStateReset()
        {
            scheduledCommand = default;
        }
    }
}
