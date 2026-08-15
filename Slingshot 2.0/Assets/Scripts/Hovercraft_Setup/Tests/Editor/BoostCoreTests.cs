using NUnit.Framework;
using UnityEngine;

namespace HovercraftV2.Tests
{
    /// <summary>Acceptance tests for held, BUS-coupled Overcharge.</summary>
    public class BoostCoreTests
    {
        private GameObject _go;
        private OverchargeCore _nitro;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("NitroTestCraft");
            _nitro = _go.AddComponent<OverchargeCore>();
            _nitro.fullBurnDuration = 0.4f;
            _nitro.regenerationDelay = 0.06f;
            _nitro.rechargeDuration = 0.4f;
            _nitro.sustainedThrottle = 1.35f;
            _nitro.ignitionThrottleBonus = 1.15f;
            _nitro.ignitionPunchDuration = 0.12f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private void Tick(bool held, bool pressed = false, float grantPower01 = 1f,
                          CraftTelemetry telemetry = default, float gripBreakerAmount = 0f,
                          bool gripBreakerHeld = false)
        {
            EnergyState energy = EnergyState.Full;
            energy.overchargePower01 = grantPower01;
            energy.overchargeBurstPower01 = grantPower01;
            energy.overchargeChargePower01 = grantPower01;
            _nitro.TickOvercharge(new CraftIntent
            {
                boostHeld = held,
                boostPressed = pressed,
                wantsGripBreaker = gripBreakerHeld
            }, telemetry, energy, gripBreakerAmount);
        }

        private void TickFor(int count, bool held, float grantPower01 = 1f)
        {
            for (int i = 0; i < count; i++)
                Tick(held, i == 0 && held, grantPower01);
        }

        [Test]
        public void FreshTank_IsFullAndReady()
        {
            Assert.AreEqual(1f, _nitro.NitroReserve01, 1e-4f);
            Assert.IsTrue(_nitro.CanBoost);
            Assert.IsFalse(_nitro.IsBoosting);
        }

        [Test]
        public void Hold_StartsBurnAndIncrementsIgnitionSequence()
        {
            int sequence = _nitro.BoostSequenceId;
            Tick(true, true);

            Assert.IsTrue(_nitro.IsBoosting);
            Assert.AreEqual(sequence + 1, _nitro.BoostSequenceId);
            Assert.Greater(_nitro.RequestedBoostStrength, 0f);
        }

        [Test]
        public void HeldBurn_ConsumesReserveContinuously()
        {
            TickFor(5, true);
            Assert.Less(_nitro.NitroReserve01, 1f);
            Assert.Greater(_nitro.NitroReserve01, 0f);
        }

        [Test]
        public void Release_StopsThrustAndPreservesReserveDuringDelay()
        {
            TickFor(5, true);
            float reserve = _nitro.NitroReserve01;

            Tick(false);

            Assert.IsFalse(_nitro.IsBoosting);
            Assert.AreEqual(0f, _nitro.RequestedBoostStrength, 1e-5f);
            Assert.AreEqual(reserve, _nitro.NitroReserve01, 1e-4f);
            Assert.Greater(_nitro.RechargeDelayRemaining, 0f);
        }

        [Test]
        public void ReleasingAndPressingAgain_ProducesAnotherIgnitionKick()
        {
            Tick(true, true);
            Tick(false);
            int sequence = _nitro.BoostSequenceId;

            Tick(true, true);

            Assert.AreEqual(sequence + 1, _nitro.BoostSequenceId);
            Assert.Greater(_nitro.Ignition01, 0f);
        }

        [Test]
        public void EmptyTank_StopsBurning()
        {
            int ticksToEmpty = Mathf.CeilToInt(
                _nitro.fullBurnDuration / Mathf.Max(0.001f, Time.fixedDeltaTime)) + 2;
            TickFor(ticksToEmpty, true);
            Tick(true);

            Assert.AreEqual(0f, _nitro.NitroReserve01, 1e-4f);
            Assert.IsFalse(_nitro.IsBoosting);
            Assert.IsFalse(_nitro.CanBoost);
        }

        [Test]
        public void HoldingEmptyTrigger_DoesNotSputterOrRegenerate()
        {
            int ticksToEmpty = Mathf.CeilToInt(
                _nitro.fullBurnDuration / Mathf.Max(0.001f, Time.fixedDeltaTime)) + 2;
            TickFor(ticksToEmpty, true);
            float empty = _nitro.NitroReserve01;

            TickFor(20, true);

            Assert.AreEqual(empty, _nitro.NitroReserve01, 1e-4f);
            Assert.IsFalse(_nitro.IsBoosting);
        }

        [Test]
        public void ReleasedTank_RegeneratesAfterDelay()
        {
            TickFor(5, true);
            float burnedReserve = _nitro.NitroReserve01;

            TickFor(4, false);

            Assert.Greater(_nitro.NitroReserve01, burnedReserve);
            Assert.IsTrue(_nitro.IsRecharging);
        }

        [Test]
        public void Recharge_StopsWhenBusDeniesPower()
        {
            TickFor(5, true);
            TickFor(4, false, 0f);
            float reserveWithoutPower = _nitro.OverchargeReserve01;

            TickFor(5, false, 0f);

            Assert.AreEqual(reserveWithoutPower, _nitro.OverchargeReserve01, 1e-4f);
            Assert.IsTrue(_nitro.IsRecharging);
            Assert.AreEqual(0f, _nitro.RechargePower01, 1e-4f);

            Tick(false, false, 1f);
            Assert.Greater(_nitro.OverchargeReserve01, reserveWithoutPower);
        }

        [Test]
        public void GripBreakSidewaysDrift_RechargesFasterAndRequestsMatchingBusPower()
        {
            _nitro.regenerationDelay = 0f;
            _nitro.fullDriftRechargeMultiplier = 2.25f;
            _nitro.driftRechargeMinimumSpeedKmh = 50f;
            _nitro.driftRechargeStartAngle = 5f;
            _nitro.driftRechargeFullAngle = 30f;

            TickFor(5, true);
            Tick(false);
            float normalStart = _nitro.OverchargeReserve01;
            Tick(false);
            float normalGain = _nitro.OverchargeReserve01 - normalStart;

            _nitro.ResetBoostState();
            TickFor(5, true);
            Tick(false);

            CraftTelemetry driftTelemetry = new CraftTelemetry
            {
                forwardSpeed = 35f,
                sideSpeed = 35f,
                speed = 49.5f,
                hasSurfaceContact = true,
                isGrounded = true
            };
            float driftStart = _nitro.OverchargeReserve01;
            Tick(false, false, 1f, driftTelemetry, 1f, true);
            float driftGain = _nitro.OverchargeReserve01 - driftStart;

            Assert.Greater(driftGain, normalGain * 2f);
            Assert.AreEqual(2.25f, _nitro.DriftRechargeMultiplier, 0.001f);
            Assert.AreEqual(
                _nitro.rechargeBusDemand * _nitro.DriftRechargeMultiplier,
                _nitro.RequestedRechargePower,
                0.001f);
        }

        [Test]
        public void GripBreakWithoutSidewaysSlip_GivesNoRechargeBonus()
        {
            CraftTelemetry straightTelemetry = new CraftTelemetry
            {
                forwardSpeed = 100f,
                sideSpeed = 0f,
                speed = 100f,
                hasSurfaceContact = true,
                isGrounded = true
            };

            Tick(false, false, 1f, straightTelemetry, 1f, true);

            Assert.AreEqual(0f, _nitro.DriftRechargeBonus01, 0.001f);
            Assert.AreEqual(1f, _nitro.DriftRechargeMultiplier, 0.001f);
        }

        [Test]
        public void IgnitionKick_IsStrongerThanSettledBurn()
        {
            Tick(true, true);
            float ignition = _nitro.RequestedBoostStrength;

            TickFor(12, true);
            float sustained = _nitro.RequestedBoostStrength;

            Assert.Greater(ignition, sustained);
        }

        [Test]
        public void GrantedStrengthAndCamera_ScaleWithBusGrant()
        {
            Tick(true, true, 0.5f);

            Assert.That(_nitro.GrantedBoostStrength,
                Is.EqualTo(_nitro.RequestedBoostStrength * 0.5f).Within(1e-3f));
            Assert.Greater(_nitro.CameraBoost01, 0f);
            Assert.LessOrEqual(_nitro.CameraBoost01, 1f);
        }

        [Test]
        public void Tick_WithNullBus_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TickFor(8, true));
        }
    }
}
