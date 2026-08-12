using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public readonly struct V3MassContribution
    {
        public V3MassContribution(
            string socketId,
            string stablePartId,
            string displayName,
            float massKg,
            Vector3 rootLocalCenterOfMass)
        {
            SocketId = socketId ?? string.Empty;
            StablePartId = stablePartId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            MassKg = Mathf.Max(0f, massKg);
            RootLocalCenterOfMass = rootLocalCenterOfMass;
        }

        public string SocketId { get; }
        public string StablePartId { get; }
        public string DisplayName { get; }
        public float MassKg { get; }
        public Vector3 RootLocalCenterOfMass { get; }
    }

    public sealed class V3MassCalculationResult
    {
        internal V3MassCalculationResult(
            float chassisBaseMassKg,
            float installedPartsMassKg,
            float totalMassKg,
            Vector3 uncalibratedCenterOfMass,
            Vector3 parityCenterOfMassCalibration,
            Vector3 finalCenterOfMass,
            V3MassContribution[] contributions)
        {
            ChassisBaseMassKg = chassisBaseMassKg;
            InstalledPartsMassKg = installedPartsMassKg;
            TotalMassKg = totalMassKg;
            UncalibratedCenterOfMass = uncalibratedCenterOfMass;
            ParityCenterOfMassCalibration =
                parityCenterOfMassCalibration;
            FinalCenterOfMass = finalCenterOfMass;
            Contributions = new ReadOnlyCollection<V3MassContribution>(
                contributions ?? Array.Empty<V3MassContribution>());
        }

        public float ChassisBaseMassKg { get; }
        public float InstalledPartsMassKg { get; }
        public float TotalMassKg { get; }
        public Vector3 UncalibratedCenterOfMass { get; }
        public Vector3 ParityCenterOfMassCalibration { get; }
        public Vector3 FinalCenterOfMass { get; }
        public IReadOnlyList<V3MassContribution> Contributions { get; }
    }

    public readonly struct V3DistributedInertiaResult
    {
        public V3DistributedInertiaResult(
            Vector3 chassisTensor,
            Vector3 installedPointMassTensor,
            Vector3 combinedTensor)
        {
            ChassisTensor = chassisTensor;
            InstalledPointMassTensor = installedPointMassTensor;
            CombinedTensor = combinedTensor;
        }

        public Vector3 ChassisTensor { get; }
        public Vector3 InstalledPointMassTensor { get; }
        public Vector3 CombinedTensor { get; }
    }

    /// <summary>
    /// Development-time comparison model for the inertia investigation. It is
    /// deliberately not applied to the Rigidbody until controlled torque runs
    /// show that it is a better production model than Unity's compound tensor.
    /// </summary>
    public static class V3DistributedInertiaCalculator
    {
        public static V3DistributedInertiaResult Calculate(
            float chassisMassKg,
            Vector3 chassisBoxSizeMeters,
            Vector3 chassisCenter,
            Vector3 combinedCenterOfMass,
            IReadOnlyList<V3MassContribution> installedParts)
        {
            float mass = Mathf.Max(0f, chassisMassKg);
            Vector3 size = new Vector3(
                Mathf.Max(0.001f, Mathf.Abs(chassisBoxSizeMeters.x)),
                Mathf.Max(0.001f, Mathf.Abs(chassisBoxSizeMeters.y)),
                Mathf.Max(0.001f, Mathf.Abs(chassisBoxSizeMeters.z)));
            Vector3 chassis = BoxTensor(mass, size) +
                PointMassTensor(mass, chassisCenter - combinedCenterOfMass);
            Vector3 points = Vector3.zero;
            if (installedParts != null)
            {
                for (int i = 0; i < installedParts.Count; i++)
                {
                    V3MassContribution contribution = installedParts[i];
                    points += PointMassTensor(
                        contribution.MassKg,
                        contribution.RootLocalCenterOfMass - combinedCenterOfMass);
                }
            }

            return new V3DistributedInertiaResult(chassis, points, chassis + points);
        }

        public static Vector3 BoxTensor(float massKg, Vector3 sizeMeters)
        {
            float mass = Mathf.Max(0f, massKg);
            float x2 = sizeMeters.x * sizeMeters.x;
            float y2 = sizeMeters.y * sizeMeters.y;
            float z2 = sizeMeters.z * sizeMeters.z;
            return mass / 12f * new Vector3(y2 + z2, x2 + z2, x2 + y2);
        }

        public static Vector3 PointMassTensor(float massKg, Vector3 offset)
        {
            float mass = Mathf.Max(0f, massKg);
            return mass * new Vector3(
                offset.y * offset.y + offset.z * offset.z,
                offset.x * offset.x + offset.z * offset.z,
                offset.x * offset.x + offset.y * offset.y);
        }
    }

    [DisallowMultipleComponent]
    public sealed class V3CraftMassCalculator : MonoBehaviour
    {
        private CraftBuildDefinition build;
        private Rigidbody rootRigidbody;
        private IReadOnlyList<RuntimePartInstance> installedParts;

        public CraftBuildDefinition Build => build;
        public Rigidbody RootRigidbody => rootRigidbody;
        public V3MassCalculationResult Result { get; private set; }
        public bool IsInitialized =>
            build != null &&
            build.Chassis != null &&
            rootRigidbody != null &&
            installedParts != null;
        public string LastError { get; private set; } = string.Empty;

        public bool Initialize(
            CraftBuildDefinition craftBuild,
            Rigidbody body,
            IReadOnlyList<RuntimePartInstance> parts)
        {
            build = craftBuild;
            rootRigidbody = body;
            installedParts = parts;
            return RecalculateAndApply();
        }

        public bool RecalculateAndApply()
        {
            if (!TryCalculate(
                build,
                rootRigidbody,
                installedParts,
                out V3MassCalculationResult result,
                out string error))
            {
                Result = null;
                LastError = error;
                return false;
            }

            Result = result;
            LastError = string.Empty;
            rootRigidbody.mass = result.TotalMassKg;
            rootRigidbody.centerOfMass = result.FinalCenterOfMass;
            return true;
        }

        public static bool TryCalculate(
            CraftBuildDefinition craftBuild,
            Rigidbody body,
            IReadOnlyList<RuntimePartInstance> parts,
            out V3MassCalculationResult result,
            out string error)
        {
            result = null;
            if (craftBuild == null || craftBuild.Chassis == null)
            {
                error = "Mass calculation requires a chassis build.";
                return false;
            }

            if (body == null)
            {
                error = "Mass calculation requires a root Rigidbody.";
                return false;
            }

            if (parts == null)
            {
                error = "Mass calculation requires installed parts.";
                return false;
            }

            float chassisMass = craftBuild.Chassis.BaseMassKg;
            float installedMass = 0f;
            float totalMass = chassisMass;
            Vector3 weightedCenter =
                craftBuild.Chassis.BaseCenterOfMass * chassisMass;
            Transform root = body.transform;
            var contributions =
                new List<V3MassContribution>(parts.Count + 6);

            for (int i = 0; i < parts.Count; i++)
            {
                RuntimePartInstance instance = parts[i];
                PartDefinition definition = instance != null
                    ? instance.Definition
                    : null;
                if (instance == null || definition == null)
                {
                    error =
                        $"Installed mass entry {i} is missing runtime data.";
                    return false;
                }

                float mass = definition.Physical.massKg;
                Vector3 worldCenter = instance.transform.TransformPoint(
                    definition.Physical.localCenterOfMass);
                Vector3 rootLocalCenter =
                    root.InverseTransformPoint(worldCenter);
                installedMass += mass;
                totalMass += mass;
                weightedCenter += rootLocalCenter * mass;
                contributions.Add(new V3MassContribution(
                    instance.ParentSocketId,
                    definition.StableId,
                    definition.DisplayName,
                    mass,
                    rootLocalCenter));
            }

            IReadOnlyList<V3DirectionalSensorDefinition> sensors =
                craftBuild.Chassis.IntegratedSensors;
            for (int i = 0; i < sensors.Count; i++)
            {
                V3DirectionalSensorDefinition sensor = sensors[i];
                if (sensor == null || sensor.MassKg <= 0f)
                {
                    continue;
                }

                Transform mount = FindNamedTransform(
                    root,
                    "Sensor." + sensor.Direction);
                if (mount == null)
                {
                    error =
                        $"Required sensor mount Sensor.{sensor.Direction} " +
                        "is missing during mass calculation.";
                    return false;
                }

                Vector3 rootLocalCenter =
                    root.InverseTransformPoint(mount.position);
                installedMass += sensor.MassKg;
                totalMass += sensor.MassKg;
                weightedCenter +=
                    rootLocalCenter * sensor.MassKg;
                contributions.Add(new V3MassContribution(
                    "Sensor." + sensor.Direction,
                    sensor.StableId,
                    sensor.DisplayName,
                    sensor.MassKg,
                    rootLocalCenter));
            }

            float appliedMass = Mathf.Max(0.001f, totalMass);
            Vector3 uncalibratedCenter = weightedCenter / appliedMass;
            Vector3 calibration =
                craftBuild.Chassis.ParityCenterOfMassCalibration;
            result = new V3MassCalculationResult(
                chassisMass,
                installedMass,
                appliedMass,
                uncalibratedCenter,
                calibration,
                uncalibratedCenter + calibration,
                contributions.ToArray());
            error = null;
            return true;
        }

        private static Transform FindNamedTransform(
            Transform root,
            string name)
        {
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == name)
                {
                    return transforms[i];
                }
            }

            return null;
        }
    }
}
