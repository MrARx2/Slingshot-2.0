using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class RuntimeCoolingModuleInstance :
        RuntimePartInstance,
        IV3CoolingDevice
    {
        private readonly List<RuntimePartInstance> cooledParts =
            new List<RuntimePartInstance>(24);

        public V3CoolingModuleDefinition CoolingDefinition =>
            Definition as V3CoolingModuleDefinition;
        public float CurrentHeatRemovalPerSecond { get; private set; }

        public void BindCraft(V3CraftRuntime runtime)
        {
            cooledParts.Clear();
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                if (part != this &&
                    part.Definition != null &&
                    part.Definition.Thermal.enabled)
                {
                    cooledParts.Add(part);
                }
            }
        }

        public float PreparePowerRequest()
        {
            V3CoolingModuleDefinition cooling = CoolingDefinition;
            if (cooling == null || !IsEnabled || IsThermallyLockedOut)
            {
                SetRuntimeState(0f, 0f, DisabledDemand, 0f);
                return DisabledDemand;
            }

            float hottest = cooling.ActivationTemperatureC;
            for (int i = 0; i < cooledParts.Count; i++)
            {
                hottest = Mathf.Max(
                    hottest,
                    cooledParts[i].CurrentTemperatureC);
            }

            float output = Mathf.InverseLerp(
                cooling.ActivationTemperatureC,
                cooling.ActivationTemperatureC + 45f,
                hottest);
            float demand = Mathf.Lerp(
                Definition.Power.idleDemand,
                Mathf.Max(
                    Definition.Power.idleDemand,
                    Definition.Power.maximumDemand),
                output);
            SetRuntimeState(output, CurrentOutput, demand, 0f);
            return demand;
        }

        public void ApplyPowerGrant(float grantedPower, float deltaTime)
        {
            V3CoolingModuleDefinition cooling = CoolingDefinition;
            if (cooling == null)
            {
                return;
            }

            float fraction = RequestedPower <= 0.0001f
                ? 1f
                : Mathf.Clamp01(grantedPower / RequestedPower);
            float output = RequestedOutput * fraction;
            CurrentHeatRemovalPerSecond =
                cooling.MaximumHeatRemovalPerSecond * output;
            int hotCount = 0;
            for (int i = 0; i < cooledParts.Count; i++)
            {
                if (cooledParts[i].CurrentTemperatureC >
                    cooledParts[i].CurrentAmbientTemperatureC +
                    0.01f)
                {
                    hotCount++;
                }
            }

            float perPart = hotCount > 0
                ? CurrentHeatRemovalPerSecond / hotCount
                : 0f;
            for (int i = 0; i < cooledParts.Count; i++)
            {
                cooledParts[i].ApplyActiveCooling(
                    perPart,
                    deltaTime);
            }

            SetRuntimeState(
                RequestedOutput,
                output,
                RequestedPower,
                grantedPower);
        }

        private float DisabledDemand =>
            Definition != null
                ? Mathf.Max(0f, Definition.Power.disabledDemand)
                : 0f;
    }
}
