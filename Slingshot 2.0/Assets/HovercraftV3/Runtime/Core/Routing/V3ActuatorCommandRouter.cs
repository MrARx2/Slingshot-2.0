using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public readonly struct V3ActuatorRouterEntrySnapshot
    {
        public V3ActuatorRouterEntrySnapshot(
            string socketId,
            RuntimeThrusterInstance thruster,
            float drive,
            float baseHover,
            float stabilization,
            float vectoring,
            float manual,
            string firmwareId,
            float firmwareRequestedRateHz,
            float firmwareGrantedRateHz,
            float firmwareMinimumRateHz,
            bool firmwareSafeState)
        {
            SocketId = socketId;
            Thruster = thruster;
            Drive = drive;
            BaseHover = baseHover;
            Stabilization = stabilization;
            Vectoring = vectoring;
            Manual = manual;
            FirmwareId = firmwareId;
            FirmwareRequestedRateHz = firmwareRequestedRateHz;
            FirmwareGrantedRateHz = firmwareGrantedRateHz;
            FirmwareMinimumRateHz = firmwareMinimumRateHz;
            FirmwareSafeState = firmwareSafeState;
        }

        public string SocketId { get; }
        public RuntimeThrusterInstance Thruster { get; }
        public float Drive { get; }
        public float BaseHover { get; }
        public float Stabilization { get; }
        public float Vectoring { get; }
        public float Manual { get; }
        public string FirmwareId { get; }
        public float FirmwareRequestedRateHz { get; }
        public float FirmwareGrantedRateHz { get; }
        public float FirmwareMinimumRateHz { get; }
        public bool FirmwareSafeState { get; }
        public float Combined => Drive + BaseHover + Stabilization + Vectoring + Manual;
    }

    public readonly struct V3ActuatorAllocationResult
    {
        public V3ActuatorAllocationResult(
            Vector3 requestedForceN,
            Vector3 requestedTorqueNm,
            Vector3 allocatedForceN,
            Vector3 allocatedTorqueNm)
        {
            RequestedForceN = requestedForceN;
            RequestedTorqueNm = requestedTorqueNm;
            AllocatedForceN = allocatedForceN;
            AllocatedTorqueNm = allocatedTorqueNm;
        }

        public Vector3 RequestedForceN { get; }
        public Vector3 RequestedTorqueNm { get; }
        public Vector3 AllocatedForceN { get; }
        public Vector3 AllocatedTorqueNm { get; }
        public Vector3 ResidualForceN => RequestedForceN - AllocatedForceN;
        public Vector3 ResidualTorqueNm => RequestedTorqueNm - AllocatedTorqueNm;
    }

    public enum V3ActuatorPriority
    {
        Optional = 0,
        Drive = 1,
        Vectoring = 2,
        Stabilization = 3,
        Critical = 4
    }

    public enum V3ActuatorChannel
    {
        Drive,
        BaseHover,
        Stabilization,
        Vectoring,
        Manual
    }

    [DisallowMultipleComponent]
    public sealed class V3ActuatorCommandRouter : MonoBehaviour
    {
        private sealed class AllocationCandidate
        {
            public string SocketId;
            public Entry Entry;
            public Vector3 ForcePerOutputN;
            public Vector3 TorquePerOutputNm;
            public float MaximumAdditionalOutput;
            public float Output;
        }

        private sealed class Entry
        {
            public RuntimeThrusterInstance Thruster;
            public V3ThrusterFirmwareRuntime Firmware;
            public readonly float[] ChannelRequests = new float[5];
            public readonly float[] PriorityRequests = new float[5];
        }

        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<V3PowerDistributor.ThrusterPowerRequest> powerRequests =
            new List<V3PowerDistributor.ThrusterPowerRequest>(32);
        private readonly Dictionary<RuntimeThrusterInstance, float> powerGrants =
            new Dictionary<RuntimeThrusterInstance, float>();
        private readonly List<AllocationCandidate> allocationCandidates =
            new List<AllocationCandidate>(16);
        private V3PowerDistributor powerDistributor;
        private Action<RuntimeThrusterInstance, float> powerGrantAccumulator;
        private bool emergencyOverloadRequested;

        public int ThrusterCount => entries.Count;
        public bool EmergencyOverloadRequested => emergencyOverloadRequested;
        public V3PowerDistributor PowerDistributor => powerDistributor;
        public V3PowerAllocationMode PowerAllocationMode =>
            powerDistributor != null
                ? powerDistributor.AllocationMode
                : V3PowerAllocationMode.Balanced;

        public void Initialize(
            V3CraftRuntime runtime,
            V3PowerDistributor distributor)
        {
            entries.Clear();
            powerDistributor = distributor;
            powerGrantAccumulator ??= AccumulatePowerGrant;
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                if (runtime.InstalledParts[i] is RuntimeThrusterInstance thruster &&
                    !string.IsNullOrWhiteSpace(thruster.ParentSocketId))
                {
                    entries[thruster.ParentSocketId] = new Entry
                    {
                        Thruster = thruster,
                        Firmware =
                            new V3ThrusterFirmwareRuntime(thruster)
                    };
                }
            }
        }

        public RuntimeThrusterInstance FindThruster(string socketId)
        {
            return socketId != null && entries.TryGetValue(socketId, out Entry entry)
                ? entry.Thruster
                : null;
        }

        public int CopyEntrySnapshots(V3ActuatorRouterEntrySnapshot[] destination)
        {
            if (destination == null || destination.Length == 0)
            {
                return 0;
            }
            int count = 0;
            foreach (KeyValuePair<string, Entry> pair in entries)
            {
                if (count >= destination.Length) break;
                Entry entry = pair.Value;
                destination[count++] = new V3ActuatorRouterEntrySnapshot(
                    pair.Key,
                    entry.Thruster,
                    entry.ChannelRequests[(int)V3ActuatorChannel.Drive],
                    entry.ChannelRequests[(int)V3ActuatorChannel.BaseHover],
                    entry.ChannelRequests[(int)V3ActuatorChannel.Stabilization],
                    entry.ChannelRequests[(int)V3ActuatorChannel.Vectoring],
                    entry.ChannelRequests[(int)V3ActuatorChannel.Manual],
                    entry.Firmware != null
                        ? entry.Firmware.FirmwareId : string.Empty,
                    entry.Firmware != null
                        ? entry.Firmware.RequestedRateHz : 0f,
                    entry.Firmware != null
                        ? entry.Firmware.GrantedRateHz : 0f,
                    entry.Firmware != null
                        ? entry.Firmware.MinimumRateHz : 0f,
                    entry.Firmware != null && entry.Firmware.IsSafeState);
            }
            return count;
        }

        public void BeginFrame(bool emergencyOverload = false)
        {
            emergencyOverloadRequested = emergencyOverload;
            foreach (Entry entry in entries.Values)
            {
                Array.Clear(
                    entry.ChannelRequests,
                    0,
                    entry.ChannelRequests.Length);
                Array.Clear(
                    entry.PriorityRequests,
                    0,
                    entry.PriorityRequests.Length);
            }
        }

        public void CyclePowerAllocationMode()
        {
            powerDistributor?.CycleAllocationMode();
        }

        public void SetFirmwareSystemsGrantFraction(float fraction)
        {
            foreach (Entry entry in entries.Values)
            {
                entry.Firmware.SetSystemsGrantFraction(fraction);
            }
        }

        public void ResetDynamicState()
        {
            BeginFrame();
            powerRequests.Clear();
            foreach (Entry entry in entries.Values)
            {
                entry.Thruster.ResetDynamicState();
            }
        }

        public bool Submit(
            string socketId,
            V3ActuatorChannel channel,
            float normalizedOutput,
            V3ActuatorPriority priority)
        {
            if (!entries.TryGetValue(socketId, out Entry entry))
            {
                return false;
            }

            entry.ChannelRequests[(int)channel] += normalizedOutput;
            entry.PriorityRequests[(int)priority] += normalizedOutput;

            return true;
        }

        public V3ActuatorAllocationResult SubmitWrench(
            IReadOnlyList<string> socketIds,
            V3ActuatorChannel channel,
            Vector3 requestedWorldForceN,
            Vector3 requestedWorldTorqueNm,
            V3ActuatorPriority priority,
            float forceWeight = 1f,
            float torqueWeight = 1f)
        {
            allocationCandidates.Clear();
            if (socketIds == null)
            {
                return new V3ActuatorAllocationResult(
                    requestedWorldForceN,
                    requestedWorldTorqueNm,
                    Vector3.zero,
                    Vector3.zero);
            }

            float characteristicLength = 0f;
            for (int i = 0; i < socketIds.Count; i++)
            {
                string socketId = socketIds[i];
                if (string.IsNullOrWhiteSpace(socketId) ||
                    !entries.TryGetValue(socketId, out Entry entry) ||
                    entry.Thruster == null ||
                    entry.Thruster.ThrusterDefinition == null)
                {
                    continue;
                }

                RuntimeThrusterInstance thruster = entry.Thruster;
                ThrusterDefinition definition = thruster.ThrusterDefinition;
                if (!thruster.IsEnabled ||
                    thruster.IsThermallyLockedOut ||
                    thruster.ThermalOutputLimit <= 0f ||
                    thruster.RootRigidbody == null)
                {
                    continue;
                }

                float maximumOutput = emergencyOverloadRequested
                    ? definition.OverloadOutputMultiplier
                    : definition.NormalOutputMultiplier;
                maximumOutput *= thruster.ThermalOutputLimit;
                float existingOutput = 0f;
                for (int channelIndex = 0;
                    channelIndex < entry.ChannelRequests.Length;
                    channelIndex++)
                {
                    existingOutput += entry.ChannelRequests[channelIndex];
                }

                float availableOutput =
                    Mathf.Max(0f, maximumOutput - Mathf.Max(0f, existingOutput));
                if (availableOutput <= 0f ||
                    definition.MaximumForwardForceN <= 0f)
                {
                    continue;
                }

                Vector3 direction = thruster.transform.TransformDirection(
                    definition.LocalThrustDirection).normalized;
                Vector3 force =
                    direction * definition.MaximumForwardForceN;
                Vector3 origin = thruster.transform.TransformPoint(
                    definition.LocalForceOrigin);
                Vector3 leverArm =
                    origin - thruster.RootRigidbody.worldCenterOfMass;
                allocationCandidates.Add(new AllocationCandidate
                {
                    SocketId = socketId,
                    Entry = entry,
                    ForcePerOutputN = force,
                    TorquePerOutputNm = Vector3.Cross(leverArm, force),
                    MaximumAdditionalOutput = availableOutput
                });
                characteristicLength += leverArm.magnitude;
            }

            if (allocationCandidates.Count == 0)
            {
                return new V3ActuatorAllocationResult(
                    requestedWorldForceN,
                    requestedWorldTorqueNm,
                    Vector3.zero,
                    Vector3.zero);
            }

            characteristicLength = Mathf.Max(
                1f,
                characteristicLength / allocationCandidates.Count);
            forceWeight = Mathf.Max(0f, forceWeight);
            torqueWeight = Mathf.Max(0f, torqueWeight);
            Vector3 weightedTargetForce = requestedWorldForceN * forceWeight;
            Vector3 weightedTargetTorque =
                requestedWorldTorqueNm / characteristicLength * torqueWeight;
            Vector3 weightedAllocatedForce = Vector3.zero;
            Vector3 weightedAllocatedTorque = Vector3.zero;

            const int iterationCount = 32;
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                bool reverse = (iteration & 1) != 0;
                for (int step = 0;
                    step < allocationCandidates.Count;
                    step++)
                {
                    int index = reverse
                        ? allocationCandidates.Count - 1 - step
                        : step;
                    AllocationCandidate candidate =
                        allocationCandidates[index];
                    Vector3 weightedForce =
                        candidate.ForcePerOutputN * forceWeight;
                    Vector3 weightedTorque =
                        candidate.TorquePerOutputNm /
                        characteristicLength *
                        torqueWeight;
                    float denominator =
                        Vector3.Dot(weightedForce, weightedForce) +
                        Vector3.Dot(weightedTorque, weightedTorque);
                    if (denominator <= 0.000001f)
                    {
                        continue;
                    }

                    Vector3 forceResidual =
                        weightedTargetForce - weightedAllocatedForce;
                    Vector3 torqueResidual =
                        weightedTargetTorque - weightedAllocatedTorque;
                    float delta =
                        (Vector3.Dot(weightedForce, forceResidual) +
                        Vector3.Dot(weightedTorque, torqueResidual)) /
                        denominator;
                    float nextOutput = Mathf.Clamp(
                        candidate.Output + delta,
                        0f,
                        candidate.MaximumAdditionalOutput);
                    float appliedDelta = nextOutput - candidate.Output;
                    if (Mathf.Abs(appliedDelta) <= 0.000001f)
                    {
                        continue;
                    }

                    candidate.Output = nextOutput;
                    weightedAllocatedForce += weightedForce * appliedDelta;
                    weightedAllocatedTorque += weightedTorque * appliedDelta;
                }
            }

            Vector3 allocatedForce = Vector3.zero;
            Vector3 allocatedTorque = Vector3.zero;
            for (int i = 0; i < allocationCandidates.Count; i++)
            {
                AllocationCandidate candidate = allocationCandidates[i];
                if (candidate.Output <= 0.000001f)
                {
                    continue;
                }

                candidate.Entry.ChannelRequests[(int)channel] +=
                    candidate.Output;
                candidate.Entry.PriorityRequests[(int)priority] +=
                    candidate.Output;

                allocatedForce +=
                    candidate.ForcePerOutputN * candidate.Output;
                allocatedTorque +=
                    candidate.TorquePerOutputNm * candidate.Output;
            }

            return new V3ActuatorAllocationResult(
                requestedWorldForceN,
                requestedWorldTorqueNm,
                allocatedForce,
                allocatedTorque);
        }

        public static Vector3 CalculateRequiredWorldTorque(
            Rigidbody body,
            Vector3 desiredWorldAngularAcceleration)
        {
            if (body == null)
            {
                return Vector3.zero;
            }

            Quaternion principalToWorld =
                body.rotation * body.inertiaTensorRotation;
            Quaternion worldToPrincipal = Quaternion.Inverse(principalToWorld);
            Vector3 principalAcceleration =
                worldToPrincipal * desiredWorldAngularAcceleration;
            Vector3 principalAngularVelocity =
                worldToPrincipal * body.angularVelocity;
            Vector3 principalAngularMomentum = Vector3.Scale(
                body.inertiaTensor,
                principalAngularVelocity);
            Vector3 principalTorque =
                Vector3.Scale(body.inertiaTensor, principalAcceleration) +
                Vector3.Cross(
                    principalAngularVelocity,
                    principalAngularMomentum);
            return principalToWorld * principalTorque;
        }

        public static Vector3 CalculateExpectedWorldAngularAcceleration(
            Rigidbody body,
            Vector3 appliedWorldTorque)
        {
            if (body == null)
            {
                return Vector3.zero;
            }

            Quaternion principalToWorld =
                body.rotation * body.inertiaTensorRotation;
            Quaternion worldToPrincipal = Quaternion.Inverse(principalToWorld);
            Vector3 principalTorque = worldToPrincipal * appliedWorldTorque;
            Vector3 principalAngularVelocity =
                worldToPrincipal * body.angularVelocity;
            Vector3 principalAngularMomentum = Vector3.Scale(
                body.inertiaTensor,
                principalAngularVelocity);
            Vector3 accelerationNumerator = principalTorque - Vector3.Cross(
                principalAngularVelocity,
                principalAngularMomentum);
            Vector3 tensor = body.inertiaTensor;
            Vector3 principalAcceleration = new Vector3(
                tensor.x > 0.000001f ? accelerationNumerator.x / tensor.x : 0f,
                tensor.y > 0.000001f ? accelerationNumerator.y / tensor.y : 0f,
                tensor.z > 0.000001f ? accelerationNumerator.z / tensor.z : 0f);
            return principalToWorld * principalAcceleration;
        }

        public void Resolve(float deltaTime)
        {
            powerRequests.Clear();
            powerGrants.Clear();
            foreach (Entry entry in entries.Values)
            {
                float combinedOutput = 0f;
                for (int i = 0; i < entry.ChannelRequests.Length; i++)
                {
                    combinedOutput += entry.ChannelRequests[i];
                }

                float demand = entry.Firmware.Prepare(
                    combinedOutput,
                    deltaTime,
                    emergencyOverloadRequested);
                AddPriorityPowerRequests(entry, demand);
                powerGrants[entry.Thruster] = 0f;
            }

            if (powerDistributor == null)
            {
                foreach (Entry entry in entries.Values)
                {
                    entry.Firmware.Apply(0f, deltaTime);
                }

                return;
            }

            powerDistributor.Allocate(
                powerRequests,
                powerGrantAccumulator);

            foreach (KeyValuePair<RuntimeThrusterInstance, float> grant
                in powerGrants)
            {
                if (entries.TryGetValue(
                    grant.Key.ParentSocketId,
                    out Entry entry))
                {
                    entry.Firmware.Apply(grant.Value, deltaTime);
                }
            }
        }

        private void AccumulatePowerGrant(
            RuntimeThrusterInstance thruster,
            float granted)
        {
            if (thruster != null)
            {
                powerGrants[thruster] += granted;
            }
        }

        private void AddPriorityPowerRequests(Entry entry, float demand)
        {
            RuntimeThrusterInstance thruster = entry.Thruster;
            if (thruster == null)
            {
                return;
            }

            float totalWeight = 0f;
            int highestActivePriority = (int)V3ActuatorPriority.Optional;
            for (int i = 0; i < entry.PriorityRequests.Length; i++)
            {
                float weight = Mathf.Abs(entry.PriorityRequests[i]);
                totalWeight += weight;
                if (weight > 0.000001f)
                {
                    highestActivePriority = i;
                }
            }

            float idleDemand = thruster.Definition != null
                ? Mathf.Min(
                    Mathf.Max(0f, thruster.Definition.Power.idleDemand),
                    demand)
                : 0f;
            float variableDemand = Mathf.Max(0f, demand - idleDemand);

            if (totalWeight <= 0.000001f)
            {
                powerRequests.Add(
                    new V3PowerDistributor.ThrusterPowerRequest(
                        thruster,
                        V3ActuatorPriority.Optional,
                        demand));
                return;
            }

            for (int i = 0; i < entry.PriorityRequests.Length; i++)
            {
                float weight = Mathf.Abs(entry.PriorityRequests[i]);
                float priorityDemand =
                    variableDemand * (weight / totalWeight);
                if (i == highestActivePriority)
                {
                    priorityDemand += idleDemand;
                }

                if (priorityDemand <= 0f)
                {
                    continue;
                }

                powerRequests.Add(
                    new V3PowerDistributor.ThrusterPowerRequest(
                        thruster,
                        (V3ActuatorPriority)i,
                        priorityDemand));
            }
        }
    }
}
