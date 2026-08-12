using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3PowerAllocationMode
    {
        Balanced,
        Propulsion,
        Stability,
        Recovery
    }

    [DisallowMultipleComponent]
    public sealed class V3PowerDistributor : MonoBehaviour
    {
        private static readonly V3ActuatorPriority[] BalancedOrder =
        {
            V3ActuatorPriority.Critical,
            V3ActuatorPriority.Stabilization,
            V3ActuatorPriority.Vectoring,
            V3ActuatorPriority.Drive,
            V3ActuatorPriority.Optional
        };

        private static readonly V3ActuatorPriority[] PropulsionOrder =
        {
            V3ActuatorPriority.Critical,
            V3ActuatorPriority.Drive,
            V3ActuatorPriority.Vectoring,
            V3ActuatorPriority.Stabilization,
            V3ActuatorPriority.Optional
        };

        private static readonly V3ActuatorPriority[] StabilityOrder =
        {
            V3ActuatorPriority.Stabilization,
            V3ActuatorPriority.Critical,
            V3ActuatorPriority.Vectoring,
            V3ActuatorPriority.Drive,
            V3ActuatorPriority.Optional
        };

        private static readonly V3ActuatorPriority[] RecoveryOrder =
        {
            V3ActuatorPriority.Critical,
            V3ActuatorPriority.Stabilization,
            V3ActuatorPriority.Drive,
            V3ActuatorPriority.Vectoring,
            V3ActuatorPriority.Optional
        };

        public readonly struct ThrusterPowerRequest
        {
            public ThrusterPowerRequest(
                RuntimeThrusterInstance thruster,
                V3ActuatorPriority priority,
                float demand)
            {
                Thruster = thruster;
                Priority = priority;
                Demand = Mathf.Max(0f, demand);
            }

            public RuntimeThrusterInstance Thruster { get; }
            public V3ActuatorPriority Priority { get; }
            public float Demand { get; }
        }

        private readonly List<RuntimePartInstance> systemsParts =
            new List<RuntimePartInstance>();
        private readonly float[] requestedPowerByPriority = new float[5];
        private readonly float[] grantedPowerByPriority = new float[5];
        private EnergyCoreDefinition core;
        private RuntimePartInstance coreInstance;
        private float externalSystemsRequested;
        private float externalSystemsGranted;

        public EnergyCoreDefinition Core => core;
        public V3PowerAllocationMode AllocationMode { get; private set; }
        public float SystemsIdleDemand { get; private set; }
        public float RequestedSystemsPower { get; private set; }
        public float GrantedSystemsPower { get; private set; }
        public float AvailablePropulsionPower { get; private set; }
        public float RequestedPropulsionPower { get; private set; }
        public float GrantedPropulsionPower { get; private set; }
        public bool IsPowerLimited =>
            IsMeaningfullyLimited(
                RequestedPropulsionPower,
                GrantedPropulsionPower);
        public bool AreSystemsPowerLimited =>
            IsMeaningfullyLimited(
                RequestedSystemsPower,
                GrantedSystemsPower);
        public float ShedPropulsionPower =>
            Mathf.Max(0f, RequestedPropulsionPower - GrantedPropulsionPower);
        public float PropulsionGrantFraction =>
            RequestedPropulsionPower <= 0.0001f
                ? 1f
                : Mathf.Clamp01(
                    GrantedPropulsionPower / RequestedPropulsionPower);

        private static bool IsMeaningfullyLimited(
            float requested,
            float granted)
        {
            float safeRequested = Mathf.Max(0f, requested);
            float tolerance = Mathf.Max(0.01f, safeRequested * 0.0001f);
            return safeRequested - Mathf.Max(0f, granted) > tolerance;
        }

        public void Initialize(V3CraftRuntime runtime)
        {
            core = null;
            coreInstance = null;
            AllocationMode = V3PowerAllocationMode.Balanced;
            systemsParts.Clear();
            externalSystemsRequested = 0f;
            externalSystemsGranted = 0f;
            SystemsIdleDemand = 0f;
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                if (part.Definition is EnergyCoreDefinition installedCore)
                {
                    core = installedCore;
                    coreInstance = part;
                }

                if (!(part is RuntimeThrusterInstance) &&
                    !(part.Definition is V3SystemComputerDefinition) &&
                    part.Definition != null &&
                    part.Definition.Power.domain == PowerDomain.Systems)
                {
                    systemsParts.Add(part);
                    SystemsIdleDemand += Mathf.Max(
                        0f,
                        part.Definition.Power.idleDemand);
                }
            }

            RecalculateAvailablePower();
        }

        public void ResolveSystemsPower()
        {
            RecalculateAvailablePower();
            UpdateCoreRuntimeState(
                RequestedSystemsPower,
                GrantedSystemsPower);
        }

        public void SetExternalSystemsLoad(float requested, float granted)
        {
            externalSystemsRequested = Mathf.Max(0f, requested);
            externalSystemsGranted = Mathf.Clamp(
                granted,
                0f,
                externalSystemsRequested);
            RecalculateAvailablePower();
        }

        public void SetAllocationMode(V3PowerAllocationMode mode)
        {
            AllocationMode = Enum.IsDefined(typeof(V3PowerAllocationMode), mode)
                ? mode
                : V3PowerAllocationMode.Balanced;
        }

        public void CycleAllocationMode()
        {
            int next =
                ((int)AllocationMode + 1) %
                Enum.GetValues(typeof(V3PowerAllocationMode)).Length;
            SetAllocationMode((V3PowerAllocationMode)next);
        }

        public float GetRequestedPower(V3ActuatorPriority priority)
        {
            int index = (int)priority;
            return index >= 0 && index < requestedPowerByPriority.Length
                ? requestedPowerByPriority[index]
                : 0f;
        }

        public float GetGrantedPower(V3ActuatorPriority priority)
        {
            int index = (int)priority;
            return index >= 0 && index < grantedPowerByPriority.Length
                ? grantedPowerByPriority[index]
                : 0f;
        }

        public IReadOnlyList<V3ActuatorPriority> GetAllocationOrder()
        {
            return GetAllocationOrder(AllocationMode);
        }

        public void Allocate(
            IReadOnlyList<ThrusterPowerRequest> requests,
            Action<RuntimeThrusterInstance, float> grant)
        {
            RecalculateAvailablePower();
            RequestedPropulsionPower = 0f;
            GrantedPropulsionPower = 0f;
            Array.Clear(
                requestedPowerByPriority,
                0,
                requestedPowerByPriority.Length);
            Array.Clear(
                grantedPowerByPriority,
                0,
                grantedPowerByPriority.Length);

            if (requests == null || grant == null)
            {
                UpdateCoreRuntimeState(
                    RequestedSystemsPower,
                    GrantedSystemsPower);
                return;
            }

            for (int i = 0; i < requests.Count; i++)
            {
                RequestedPropulsionPower += requests[i].Demand;
                int priorityIndex = (int)requests[i].Priority;
                if (priorityIndex >= 0 &&
                    priorityIndex < requestedPowerByPriority.Length)
                {
                    requestedPowerByPriority[priorityIndex] +=
                        requests[i].Demand;
                }
            }

            float remaining = AvailablePropulsionPower;
            IReadOnlyList<V3ActuatorPriority> allocationOrder =
                GetAllocationOrder();
            for (int tier = 0; tier < allocationOrder.Count; tier++)
            {
                V3ActuatorPriority priority = allocationOrder[tier];
                float tierDemand = 0f;
                for (int i = 0; i < requests.Count; i++)
                {
                    if (requests[i].Priority == priority)
                    {
                        tierDemand += requests[i].Demand;
                    }
                }

                float tierScale = tierDemand <= 0f
                    ? 0f
                    : Mathf.Clamp01(remaining / tierDemand);
                for (int i = 0; i < requests.Count; i++)
                {
                    ThrusterPowerRequest request = requests[i];
                    if (request.Priority != priority)
                    {
                        continue;
                    }

                    float granted = request.Demand * tierScale;
                    grant(request.Thruster, granted);
                    GrantedPropulsionPower += granted;
                    grantedPowerByPriority[(int)priority] += granted;
                }

                remaining = Mathf.Max(0f, remaining - tierDemand * tierScale);
            }

            UpdateCoreRuntimeState(
                RequestedSystemsPower + RequestedPropulsionPower,
                GrantedSystemsPower + GrantedPropulsionPower);
        }

        private static IReadOnlyList<V3ActuatorPriority> GetAllocationOrder(
            V3PowerAllocationMode mode)
        {
            switch (mode)
            {
                case V3PowerAllocationMode.Propulsion:
                    return PropulsionOrder;
                case V3PowerAllocationMode.Stability:
                    return StabilityOrder;
                case V3PowerAllocationMode.Recovery:
                    return RecoveryOrder;
                default:
                    return BalancedOrder;
            }
        }

        private void RecalculateAvailablePower()
        {
            if (core == null)
            {
                RequestedSystemsPower = externalSystemsRequested;
                GrantedSystemsPower = 0f;
                AvailablePropulsionPower = 0f;
                for (int i = 0; i < systemsParts.Count; i++)
                {
                    RuntimePartInstance part = systemsParts[i];
                    float demand = GetSystemsDemand(part);
                    RequestedSystemsPower += demand;
                    part.SetRuntimeState(
                        part.RequestedOutput,
                        part.CurrentOutput,
                        demand,
                        0f);
                }

                return;
            }

            float usableCoreOutput = GetUsableCoreOutput();
            float systemsBudget = Mathf.Min(
                usableCoreOutput,
                core.SystemsChannelCeiling);
            RequestedSystemsPower = externalSystemsRequested;
            GrantedSystemsPower = Mathf.Min(
                externalSystemsGranted,
                systemsBudget);
            for (int i = 0; i < systemsParts.Count; i++)
            {
                RuntimePartInstance part = systemsParts[i];
                float demand = GetSystemsDemand(part);
                RequestedSystemsPower += demand;
                float granted = Mathf.Min(
                    demand,
                    Mathf.Max(0f, systemsBudget - GrantedSystemsPower));
                if (part != coreInstance)
                {
                    part.SetRuntimeState(
                        part.RequestedOutput,
                        part.CurrentOutput,
                        demand,
                        granted);
                }
                GrantedSystemsPower += granted;
            }

            float afterSystems =
                Mathf.Max(0f, usableCoreOutput - GrantedSystemsPower);
            AvailablePropulsionPower =
                Mathf.Min(afterSystems, core.PropulsionChannelCeiling);
        }

        private float GetSystemsDemand(RuntimePartInstance part)
        {
            if (part == null || part.Definition == null)
            {
                return 0f;
            }

            if (!part.IsEnabled || part.IsThermallyLockedOut)
            {
                return Mathf.Max(
                    0f,
                    part.Definition.Power.disabledDemand);
            }

            float idle = Mathf.Max(
                0f,
                part.Definition.Power.idleDemand);
            return part == coreInstance
                ? idle
                : Mathf.Max(idle, part.RequestedPower);
        }

        private float GetUsableCoreOutput()
        {
            if (core == null)
            {
                return 0f;
            }

            if (coreInstance == null)
            {
                return Mathf.Max(0f, core.ContinuousOutput);
            }

            if (!coreInstance.IsEnabled ||
                coreInstance.IsThermallyLockedOut)
            {
                return 0f;
            }

            return Mathf.Max(0f, core.ContinuousOutput) *
                coreInstance.ThermalOutputLimit;
        }

        private void UpdateCoreRuntimeState(
            float requestedTotalPower,
            float grantedTotalPower)
        {
            if (coreInstance == null || core == null)
            {
                return;
            }

            float output = Mathf.Max(0.001f, core.ContinuousOutput);
            coreInstance.SetRuntimeState(
                Mathf.Max(0f, requestedTotalPower) / output,
                Mathf.Max(0f, grantedTotalPower) / output,
                Mathf.Max(0f, requestedTotalPower),
                Mathf.Max(0f, grantedTotalPower));
        }
    }
}
