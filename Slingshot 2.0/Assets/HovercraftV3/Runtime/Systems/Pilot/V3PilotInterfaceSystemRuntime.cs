namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3PilotInterfaceSystemRuntime :
        V3CraftSystemRuntimeBase
    {
        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.PilotInterface;

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            if (Context.Intents != null &&
                Context.Intents.HasPilotIntent)
            {
                Context.PilotCommand =
                    Context.Intents.LatestPilotIntent.Command;
                OutputPublished = true;
            }
            else
            {
                Context.PilotCommand = default;
                OutputPublished = false;
            }
        }

        protected override void OnDynamicStateReset()
        {
            if (Context != null)
            {
                Context.PilotCommand = default;
                Context.ScheduledCommand = default;
                Context.PhysicsCommand = default;
            }
        }
    }
}
