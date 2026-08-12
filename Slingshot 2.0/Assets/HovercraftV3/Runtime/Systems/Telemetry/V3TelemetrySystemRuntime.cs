namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3TelemetrySystemRuntime :
        V3CraftSystemRuntimeBase
    {
        public override V3SystemComputerRole Role =>
            V3SystemComputerRole.Telemetry;

        protected override void OnScheduledTick(float scheduledDeltaTime)
        {
            if (Context.Telemetry == null)
            {
                OutputPublished = false;
                return;
            }

            Context.Telemetry.RefreshLiveTelemetry();
            OutputPublished = true;
        }
    }
}
