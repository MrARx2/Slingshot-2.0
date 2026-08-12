namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3TractionSystemRuntime :
        V3CraftSystemRuntimeBase
    {
        private V3PilotCommand scheduledCommand;

        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.Traction;

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            scheduledCommand = Context.ScheduledCommand;
            OutputPublished = Context.Traction != null;
        }

        public override void ApplyPhysicsTick(float physicsDeltaTime)
        {
            Context.Traction?.Submit(
                scheduledCommand,
                physicsDeltaTime);
        }

        protected override void OnDynamicStateReset()
        {
            scheduledCommand = default;
        }
    }
}
