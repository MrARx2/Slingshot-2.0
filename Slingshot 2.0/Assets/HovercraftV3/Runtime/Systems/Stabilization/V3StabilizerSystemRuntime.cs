namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3StabilizerSystemRuntime :
        V3CraftSystemRuntimeBase
    {
        private V3PilotCommand scheduledCommand;

        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.Stabilizer;

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            scheduledCommand = Context.ScheduledCommand;
            OutputPublished =
                Context.Vector != null || Context.Gimbal != null;
        }

        public override void ApplyPhysicsTick(float physicsDeltaTime)
        {
            Context.Gimbal?.Submit(
                scheduledCommand,
                physicsDeltaTime);
            Context.Vector?.Submit(
                scheduledCommand,
                physicsDeltaTime);
        }

        protected override void OnDynamicStateReset()
        {
            scheduledCommand = default;
        }
    }
}
