namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3HoverSystemRuntime :
        V3CraftSystemRuntimeBase
    {
        private V3PilotCommand scheduledCommand;

        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.Hover;

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            scheduledCommand = Context.ScheduledCommand;
            OutputPublished = Context.Hover != null;
        }

        public override void ApplyPhysicsTick(float physicsDeltaTime)
        {
            Context.Hover?.Submit(
                scheduledCommand,
                physicsDeltaTime);
        }

        protected override void OnDynamicStateReset()
        {
            scheduledCommand = default;
        }
    }
}
