using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [Serializable]
    public struct V3CraftAerodynamicState
    {
        public bool IsValid;
        public long PhysicsTickId;
        public Vector3 GroundVelocity;
        public Vector3 RelativeAirVelocity;
        public float AirspeedMetersPerSecond;
        public float MachNumber;
        public float DynamicPressurePa;
        public float CompressibilityDragMultiplier;
        public Vector3 DragForce;
        public Vector3 Downforce;
        public Vector3 SideForce;
        public Vector3 ChassisForce;
        public Vector3 ChassisTorque;
        public Vector3 FinForce;
        public Vector3 FinTorque;
        public float FinAuthority;
        public bool FinSaturated;
        public Vector3 TotalAerodynamicForce => ChassisForce + FinForce;
        public Vector3 TotalAerodynamicTorque => ChassisTorque + FinTorque;
    }

    /// <summary>
    /// Applies physical chassis aerodynamic forces using the exact reference
    /// world sample cached for the current V3 craft physics tick.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3CraftAerodynamicsRuntime : MonoBehaviour
    {
        private V3CraftRuntime runtime;
        private Rigidbody body;
        private V3WorldEnvironmentProvider environment;
        private ChassisDefinition chassis;
        private bool chassisAerodynamicsEnabled = true;

        public V3CraftAerodynamicState State { get; private set; }
        public bool ChassisAerodynamicsEnabled => chassisAerodynamicsEnabled;

        public static Vector3 CalculateRelativeAirVelocity(
            Vector3 groundVelocity,
            Vector3 localAirVelocity)
        {
            return groundVelocity - localAirVelocity;
        }

        public static float CalculateDynamicPressure(
            float airDensityKgPerCubicMeter,
            Vector3 relativeAirVelocity)
        {
            return 0.5f * Mathf.Max(0f, airDensityKgPerCubicMeter) *
                relativeAirVelocity.sqrMagnitude;
        }

        public void Initialize(
            V3CraftRuntime craftRuntime,
            V3WorldEnvironmentProvider provider)
        {
            runtime = craftRuntime;
            body = runtime != null ? runtime.RootRigidbody : null;
            environment = provider;
            chassis = runtime != null && runtime.Build != null
                ? runtime.Build.Chassis
                : null;
            State = default;
        }

        public void ApplyReferenceSample()
        {
            if (!chassisAerodynamicsEnabled)
            {
                State = default;
                return;
            }
            if (body == null || chassis == null || environment == null)
            {
                State = default;
                return;
            }

            V3WorldEnvironmentSample sample = environment.CurrentSample;
            if (!sample.IsValid || !sample.AtmosphereEnabled)
            {
                State = default;
                return;
            }

            Vector3 point = transform.TransformPoint(
                chassis.AerodynamicCenterOfPressure);
            Vector3 pointVelocity = body.GetPointVelocity(point);
            Vector3 relativeAirVelocity = CalculateRelativeAirVelocity(
                pointVelocity,
                sample.LocalAirVelocity);
            float airspeed = relativeAirVelocity.magnitude;
            float speedOfSound = sample.SpeedOfSoundMetersPerSecond;
            float mach = speedOfSound > 0.001f
                ? airspeed / speedOfSound
                : 0f;
            float dynamicPressure = CalculateDynamicPressure(
                sample.AirDensityKgPerCubicMeter,
                relativeAirVelocity);
            float compressibility =
                chassis.EvaluateCompressibilityDragMultiplier(mach);
            Vector3 drag = airspeed > 0.001f
                ? -relativeAirVelocity / airspeed *
                  (dynamicPressure *
                   chassis.AerodynamicReferenceArea *
                   chassis.DragCoefficient *
                   compressibility)
                : Vector3.zero;
            Vector3 downforce = -transform.up *
                (dynamicPressure *
                 chassis.DownforceReferenceArea *
                 chassis.DownforceCoefficient);
            Vector3 forwardDrag = Vector3.Project(drag, transform.forward);
            Vector3 verticalDrag = Vector3.Project(drag, transform.up);
            Vector3 sideForce = drag - forwardDrag - verticalDrag;
            Vector3 force = drag + downforce;
            Vector3 torque = Vector3.Cross(
                point - body.worldCenterOfMass,
                force);
            body.AddForceAtPosition(force, point, ForceMode.Force);
            State = new V3CraftAerodynamicState
            {
                IsValid = true,
                PhysicsTickId = sample.PhysicsTickId,
                GroundVelocity = body.linearVelocity,
                RelativeAirVelocity = relativeAirVelocity,
                AirspeedMetersPerSecond = airspeed,
                MachNumber = mach,
                DynamicPressurePa = dynamicPressure,
                CompressibilityDragMultiplier = compressibility,
                DragForce = drag,
                Downforce = downforce,
                SideForce = sideForce,
                ChassisForce = force,
                ChassisTorque = torque
            };
        }

        public void SetFinState(
            Vector3 force,
            Vector3 torque,
            float authority,
            bool saturated)
        {
            V3CraftAerodynamicState value = State;
            value.FinForce = force;
            value.FinTorque = torque;
            value.FinAuthority = Mathf.Clamp01(authority);
            value.FinSaturated = saturated;
            State = value;
        }

        public void ResetDynamicState()
        {
            State = default;
        }

        /// <summary>
        /// Explicit configuration hook used only by guarded TestOnly builds.
        /// Disabling the MonoBehaviour is insufficient because the Mainframe
        /// invokes ApplyReferenceSample directly.
        /// </summary>
        public void SetChassisAerodynamicsEnabled(bool value)
        {
            chassisAerodynamicsEnabled = value;
            if (!value) State = default;
        }
    }
}
