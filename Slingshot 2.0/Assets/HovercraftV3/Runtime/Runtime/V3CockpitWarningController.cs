using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3CockpitAlertLevel
    {
        Clear,
        Advisory,
        Warning,
        Critical
    }

    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    public sealed class V3CockpitWarningController : MonoBehaviour
    {
        private V3ThermalController thermal;
        private V3ActuatorCommandRouter router;
        private V3PowerDistributor power;

        public V3CockpitAlertLevel AlertLevel { get; private set; }
        public string AlertMessage { get; private set; } = "SYSTEMS NOMINAL";
        public bool IsEmergencyOverloadRequested { get; private set; }
        public float MaximumTemperatureC { get; private set; }
        public float LowestHotOutputLimit { get; private set; } = 1f;
        public int WarningPartCount { get; private set; }
        public int LockedOutPartCount { get; private set; }
        public V3PowerAllocationMode PowerAllocationMode { get; private set; }
        public bool IsPropulsionPowerLimited { get; private set; }
        public bool AreSystemsPowerLimited { get; private set; }
        public float PropulsionPowerGrantFraction { get; private set; } = 1f;

        public void Initialize(
            V3ThermalController thermalController,
            V3ActuatorCommandRouter commandRouter,
            V3PowerDistributor powerDistributor = null)
        {
            thermal = thermalController;
            router = commandRouter;
            power = powerDistributor != null
                ? powerDistributor
                : commandRouter?.PowerDistributor;
            Refresh();
        }

        private void FixedUpdate()
        {
            Refresh();
        }

        public void Refresh()
        {
            IsEmergencyOverloadRequested =
                router != null && router.EmergencyOverloadRequested;
            MaximumTemperatureC =
                thermal != null ? thermal.MaximumTemperatureC : 0f;
            LowestHotOutputLimit = 1f;
            WarningPartCount = 0;
            LockedOutPartCount = 0;
            PowerAllocationMode =
                power != null
                    ? power.AllocationMode
                    : V3PowerAllocationMode.Balanced;
            IsPropulsionPowerLimited =
                power != null && power.IsPowerLimited;
            AreSystemsPowerLimited =
                power != null && power.AreSystemsPowerLimited;
            PropulsionPowerGrantFraction =
                power != null ? power.PropulsionGrantFraction : 1f;

            if (thermal != null)
            {
                for (int i = 0; i < thermal.Telemetry.Count; i++)
                {
                    V3PartThermalTelemetry part = thermal.Telemetry[i];
                    if (part.isThermallyLockedOut)
                    {
                        LockedOutPartCount++;
                    }

                    if (part.protectionState ==
                        V3ThermalProtectionState.Warning)
                    {
                        WarningPartCount++;
                        LowestHotOutputLimit = Mathf.Min(
                            LowestHotOutputLimit,
                            part.outputLimit);
                    }
                }
            }

            if (LockedOutPartCount > 0)
            {
                AlertLevel = V3CockpitAlertLevel.Critical;
                AlertMessage =
                    $"THERMAL LOCKOUT  {LockedOutPartCount} PART" +
                    (LockedOutPartCount == 1 ? string.Empty : "S");
                return;
            }

            if (thermal != null && thermal.OverheatedPartCount > 0)
            {
                AlertLevel = V3CockpitAlertLevel.Critical;
                AlertMessage = "OVERHEAT — OUTPUT SHUTDOWN";
                return;
            }

            if (AreSystemsPowerLimited)
            {
                AlertLevel = V3CockpitAlertLevel.Critical;
                AlertMessage = "SYSTEMS POWER LIMITED";
                return;
            }

            if (WarningPartCount > 0)
            {
                AlertLevel = V3CockpitAlertLevel.Warning;
                AlertMessage =
                    $"THERMAL LIMIT  {LowestHotOutputLimit * 100f:0}% OUTPUT";
                return;
            }

            if (IsPropulsionPowerLimited)
            {
                AlertLevel = V3CockpitAlertLevel.Warning;
                AlertMessage =
                    $"POWER SHED  {PowerAllocationMode.ToString().ToUpperInvariant()} " +
                    $"{PropulsionPowerGrantFraction * 100f:0}%";
                return;
            }

            if (IsEmergencyOverloadRequested)
            {
                AlertLevel = V3CockpitAlertLevel.Advisory;
                AlertMessage = "EMERGENCY OVERLOAD";
                return;
            }

            AlertLevel = V3CockpitAlertLevel.Clear;
            AlertMessage = "SYSTEMS NOMINAL";
        }
    }
}
