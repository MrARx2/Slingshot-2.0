using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public sealed class V3ScheduledSoftwareTask
    {
        internal V3ScheduledSoftwareTask(
            RuntimePartInstance computer,
            V3SystemComputerDefinition definition,
            V3SystemComputerRuntime computerRuntime,
            IV3CraftSystemRuntime systemRuntime)
        {
            Computer = computer;
            ComputerDefinition = definition;
            Software = definition.BundledSoftware;
            ComputerRuntime = computerRuntime;
            SystemRuntime = systemRuntime;
            TaskId = Software != null
                ? Software.StableId
                : definition.StableId;
        }

        public string TaskId { get; }
        public RuntimePartInstance Computer { get; }
        public V3SystemComputerDefinition ComputerDefinition { get; }
        public V3SystemComputerRuntime ComputerRuntime { get; }
        public IV3CraftSystemRuntime SystemRuntime { get; }
        public V3SystemSoftwareDefinition Software { get; }
        public float RequestedRateHz { get; internal set; }
        public float CapacityLimitedRateHz { get; internal set; }
        public float PowerLimitedRateHz { get; internal set; }
        public float GrantedRateHz { get; internal set; }
        public float RequestedPower { get; internal set; }
        public float GrantedPower { get; internal set; }
        public string Bottleneck { get; internal set; }
        public float AccumulatorSeconds { get; internal set; }
        public long ExecutionCount { get; internal set; }
        public long SkippedExecutionCount { get; internal set; }
        public double LastExecutionTimestamp { get; internal set; } = -1d;
        public float LastExecutionDurationSeconds { get; internal set; }
        public string LastFailure { get; internal set; } = string.Empty;
        public bool IsFaulted { get; internal set; }
        public float MeasuredActualRateHz { get; internal set; }
        internal int MeasurementExecutions;
        internal float MeasurementWindow;
        public bool HasExecutableCallback => SystemRuntime != null;
        public bool HasPublishedOutput =>
            SystemRuntime != null &&
            SystemRuntime.CaptureSnapshot().OutputPublished;
        public bool IsOperational =>
            Software != null &&
            Computer != null &&
            Computer.IsEnabled &&
            HasExecutableCallback &&
            !IsFaulted &&
            ExecutionCount > 0 &&
            GrantedRateHz + 0.001f >= Software.MinimumUsefulRateHz;
    }

    public sealed class V3SoftwareScheduler
    {
        private readonly List<V3ScheduledSoftwareTask> tasks =
            new List<V3ScheduledSoftwareTask>(7);

        public IReadOnlyList<V3ScheduledSoftwareTask> Tasks => tasks;
        public float RequestedPower { get; private set; }
        public float GrantedPower { get; private set; }
        public float UsedComputePerSecond { get; private set; }

        public void Initialize(V3CraftRuntime runtime)
        {
            tasks.Clear();
            if (runtime == null)
            {
                return;
            }

            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = runtime.InstalledParts[i];
                if (part.Definition is V3SystemComputerDefinition computer &&
                    computer.BundledSoftware != null)
                {
                    V3SystemComputerRuntime computerRuntime =
                        part.GetComponent<V3SystemComputerRuntime>();
                    IV3CraftSystemRuntime systemRuntime = null;
                    MonoBehaviour[] behaviours =
                        part.GetComponents<MonoBehaviour>();
                    for (int j = 0; j < behaviours.Length; j++)
                    {
                        if (behaviours[j] is
                            IV3CraftSystemRuntime candidate &&
                            candidate.Role == computer.ComputerRole)
                        {
                            systemRuntime = candidate;
                            break;
                        }
                    }

                    tasks.Add(new V3ScheduledSoftwareTask(
                        part,
                        computer,
                        computerRuntime,
                        systemRuntime));
                }
            }

            tasks.Sort((a, b) =>
                GetSurvivalRank(a.Software.Role).CompareTo(
                    GetSurvivalRank(b.Software.Role)));
        }

        public void BeginFrame(
            V3MainframeDefinition mainframe,
            float physicsRateHz)
        {
            RequestedPower = 0f;
            GrantedPower = 0f;
            UsedComputePerSecond = 0f;
            float remainingMainframeCompute =
                mainframe != null
                    ? mainframe.ComputeCapacityPerSecond
                    : 0f;
            for (int i = 0; i < tasks.Count; i++)
            {
                V3ScheduledSoftwareTask task = tasks[i];
                V3SystemSoftwareDefinition software = task.Software;
                if (task.Computer == null || !task.Computer.IsEnabled)
                {
                    task.RequestedRateHz = 0f;
                    task.CapacityLimitedRateHz = 0f;
                    task.PowerLimitedRateHz = 0f;
                    task.GrantedRateHz = 0f;
                    task.RequestedPower = 0f;
                    task.GrantedPower = 0f;
                    task.Bottleneck = "ComputerOffline";
                    task.Computer?.SetRuntimeState(0f, 0f, 0f, 0f);
                    task.ComputerRuntime?.ApplyTaskState(task);
                    continue;
                }
                float requested = Mathf.Min(
                    software.RequestedTickRateHz,
                    software.MaximumUsefulRateHz);
                task.RequestedRateHz = requested;
                float computerRate =
                    task.ComputerDefinition.ComputeCapacityPerSecond /
                    Mathf.Max(0.0001f, software.ComputeCostPerTick);
                float mainframeRate =
                    remainingMainframeCompute /
                    Mathf.Max(0.0001f, software.ComputeCostPerTick);
                task.CapacityLimitedRateHz = Mathf.Min(
                    requested,
                    computerRate,
                    mainframeRate,
                    240f);
                float compute = task.CapacityLimitedRateHz *
                    software.ComputeCostPerTick;
                remainingMainframeCompute = Mathf.Max(
                    0f,
                    remainingMainframeCompute - compute);
                UsedComputePerSecond += compute;
                task.RequestedPower = software.IdlePower +
                    task.CapacityLimitedRateHz * software.PowerPerTick;
                task.GrantedPower = 0f;
                task.PowerLimitedRateHz = 0f;
                task.GrantedRateHz = 0f;
                task.Bottleneck = task.CapacityLimitedRateHz + 0.001f <
                    requested
                        ? "ComputeCapacity"
                        : "SystemsPower";
                RequestedPower += task.RequestedPower;
            }
        }

        public void GrantRank(int rank, ref float remainingPower)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                V3ScheduledSoftwareTask task = tasks[i];
                if (GetSurvivalRank(task.Software.Role) != rank)
                {
                    continue;
                }
                if (task.Computer == null || !task.Computer.IsEnabled)
                {
                    task.GrantedPower = 0f;
                    task.PowerLimitedRateHz = 0f;
                    task.GrantedRateHz = 0f;
                    task.Bottleneck = "ComputerOffline";
                    task.ComputerRuntime?.ApplyTaskState(task);
                    continue;
                }

                task.GrantedPower = Mathf.Min(
                    task.RequestedPower,
                    Mathf.Max(0f, remainingPower));
                remainingPower = Mathf.Max(
                    0f,
                    remainingPower - task.GrantedPower);
                float tickPower = Mathf.Max(
                    0f,
                    task.GrantedPower - task.Software.IdlePower);
                task.PowerLimitedRateHz =
                    task.Software.PowerPerTick <= 0f
                        ? task.CapacityLimitedRateHz
                        : Mathf.Min(
                            task.CapacityLimitedRateHz,
                            tickPower / task.Software.PowerPerTick);
                task.GrantedRateHz = task.PowerLimitedRateHz;
                if (task.GrantedRateHz + 0.001f >=
                    task.RequestedRateHz)
                {
                    task.Bottleneck = "None";
                }
                else if (task.GrantedRateHz + 0.001f <
                    task.CapacityLimitedRateHz)
                {
                    task.Bottleneck = "SystemsPower";
                }

                task.Computer.SetRuntimeState(
                    task.RequestedRateHz,
                    task.GrantedRateHz,
                    task.RequestedPower,
                    task.GrantedPower);
                task.ComputerRuntime?.ApplyTaskState(task);
                GrantedPower += task.GrantedPower;
            }
        }

        public void Execute(
            float deltaTime,
            double timestamp,
            int perTaskSafetyCap = 4,
            int globalSafetyCap = 24)
        {
            int totalExecutions = 0;
            float step = Mathf.Max(0f, deltaTime);
            for (int i = 0; i < tasks.Count; i++)
            {
                V3ScheduledSoftwareTask task = tasks[i];
                if (task.IsFaulted ||
                    task.Computer == null ||
                    !task.Computer.IsEnabled ||
                    task.SystemRuntime == null ||
                    task.Software == null ||
                    task.GrantedRateHz + 0.001f <
                    task.Software.MinimumUsefulRateHz)
                {
                    task.ComputerRuntime?.ApplyTaskState(task);
                    continue;
                }

                float interval =
                    1f / Mathf.Max(1f, task.GrantedRateHz);
                task.AccumulatorSeconds += step;
                int taskExecutions = 0;
                while (task.AccumulatorSeconds + 0.000001f >=
                       interval &&
                       taskExecutions <
                       Mathf.Max(1, perTaskSafetyCap) &&
                       totalExecutions <
                       Mathf.Max(1, globalSafetyCap))
                {
                    double started =
                        Time.realtimeSinceStartupAsDouble;
                    try
                    {
                        task.SystemRuntime.ScheduledTick(interval);
                        task.ExecutionCount++;
                        task.MeasurementExecutions++;
                        task.LastExecutionTimestamp = timestamp;
                        task.LastExecutionDurationSeconds =
                            (float)(
                                Time.realtimeSinceStartupAsDouble -
                                started);
                        task.LastFailure = string.Empty;
                    }
                    catch (System.Exception exception)
                    {
                        task.IsFaulted = true;
                        task.LastFailure =
                            exception.GetType().Name + ": " +
                            exception.Message;
                        task.Bottleneck = "TaskFault";
                        break;
                    }

                    task.AccumulatorSeconds -= interval;
                    taskExecutions++;
                    totalExecutions++;
                }

                if (task.AccumulatorSeconds + 0.000001f >=
                    interval)
                {
                    task.SkippedExecutionCount++;
                    task.AccumulatorSeconds = Mathf.Min(
                        task.AccumulatorSeconds,
                        interval);
                }

                task.MeasurementWindow += step;
                if (task.MeasurementWindow >= 1f)
                {
                    task.MeasuredActualRateHz =
                        task.MeasurementExecutions /
                        task.MeasurementWindow;
                    task.MeasurementWindow = 0f;
                    task.MeasurementExecutions = 0;
                }

                task.ComputerRuntime?.ApplyTaskState(task);
            }
        }

        public bool IsRoleOperational(V3SystemComputerRole role)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].Software.Role == role)
                {
                    return tasks[i].IsOperational;
                }
            }

            return false;
        }

        public void ResetDynamicState()
        {
            RequestedPower = 0f;
            GrantedPower = 0f;
            UsedComputePerSecond = 0f;
            for (int i = 0; i < tasks.Count; i++)
            {
                V3ScheduledSoftwareTask task = tasks[i];
                task.RequestedRateHz = 0f;
                task.CapacityLimitedRateHz = 0f;
                task.PowerLimitedRateHz = 0f;
                task.GrantedRateHz = 0f;
                task.RequestedPower = 0f;
                task.GrantedPower = 0f;
                task.Bottleneck = "DynamicReset";
                task.AccumulatorSeconds = 0f;
                task.ExecutionCount = 0;
                task.SkippedExecutionCount = 0;
                task.LastExecutionTimestamp = -1d;
                task.LastExecutionDurationSeconds = 0f;
                task.LastFailure = string.Empty;
                task.IsFaulted = false;
                task.MeasuredActualRateHz = 0f;
                task.MeasurementExecutions = 0;
                task.MeasurementWindow = 0f;
                task.ComputerRuntime?.ApplyTaskState(task);
            }
        }

        public static int GetSurvivalRank(V3SystemComputerRole role)
        {
            switch (role)
            {
                case V3SystemComputerRole.PilotInterface:
                    return 0;
                case V3SystemComputerRole.Hover:
                    return 1;
                case V3SystemComputerRole.Stabilizer:
                    return 2;
                case V3SystemComputerRole.Drive:
                    return 4;
                case V3SystemComputerRole.Traction:
                    return 5;
                case V3SystemComputerRole.Aerodynamics:
                    return 6;
                default:
                    return 8;
            }
        }
    }
}
