using NUnit.Framework;
using UnityEngine;

namespace HovercraftV2.Tests
{
    public class EnergyCoreTests
    {
        private GameObject _craft;
        private ThrusterBus _bus;
        private EnergyCore _energy;

        [SetUp]
        public void SetUp()
        {
            _craft = new GameObject("EnergyCoreTestCraft");
            _bus = _craft.AddComponent<ThrusterBus>();
            _bus.autoDiscover = false;
            _bus.thrusters.Clear();

            _energy = _craft.AddComponent<EnergyCore>();
            _energy.totalPowerOutput = 100f;
            _energy.powerCostPerForce = 1f;
            _energy.protectBaseHover = true;
            _energy.protectStabilizer = true;
            _energy.steeringReserve01 = 0.28f;
            _energy.boostReserve01 = 0.62f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_craft != null) Object.DestroyImmediate(_craft);
        }

        private ThrusterBus.ThrusterNodeEntry AddNode(string label, ThrusterNode.ThrusterRole role, float maxForce)
        {
            var go = new GameObject(label);
            go.transform.SetParent(_craft.transform);
            var node = go.AddComponent<ThrusterNode>();
            node.label = label;
            node.role = role;
            node.maxForce = maxForce;
            node.efficiency = 1f;
            node.powerDrawMultiplier = 1f;

            var entry = new ThrusterBus.ThrusterNodeEntry
            {
                node = node,
                label = label,
                enabled = true,
                forceMultiplier = 1f
            };
            _bus.thrusters.Add(entry);
            return entry;
        }

        [Test]
        public void SharedGrants_NeverExceedBusBudget()
        {
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 100f);
            var steer = AddNode("Steer", ThrusterNode.ThrusterRole.Strafe, 100f);
            var boost = AddNode("Boost", ThrusterNode.ThrusterRole.Main, 100f);
            drive.driveThrottle = 1f;
            steer.vectoringThrottle = 1f;
            boost.overchargeThrottle = 1f;

            EnergyState state = _energy.ResolvePower(_bus, null);

            Assert.LessOrEqual(state.totalGranted, state.totalBudget + 0.001f);
            Assert.AreEqual(100f, state.totalGranted, 0.001f);
        }

        [Test]
        public void ProtectedHover_RemainsFullOutsideSharedBus()
        {
            var hover = AddNode("Hover", ThrusterNode.ThrusterRole.Hover, 80f);
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 200f);
            hover.baseHoverThrottle = 1f;
            drive.driveThrottle = 1f;

            EnergyState state = _energy.ResolvePower(_bus, null);

            Assert.AreEqual(80f, state.baseHoverGranted, 0.001f);
            Assert.AreEqual(1f, state.baseHoverPower01, 0.001f);
            Assert.LessOrEqual(state.totalGranted - state.baseHoverGranted, state.totalBudget + 0.001f);
        }

        [Test]
        public void Overload_PreservesMoreSteeringThanDrive()
        {
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 100f);
            var steer = AddNode("Steer", ThrusterNode.ThrusterRole.Strafe, 100f);
            drive.driveThrottle = 1f;
            steer.vectoringThrottle = 1f;

            EnergyState state = _energy.ResolvePower(_bus, null);

            Assert.Greater(state.vectoringPower01, state.drivePower01);
            Assert.GreaterOrEqual(state.vectoringGranted, 28f - 0.001f);
        }

        [Test]
        public void ActiveBoost_GetsItsReservedShareButStillCompetes()
        {
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 100f);
            var boost = AddNode("Boost", ThrusterNode.ThrusterRole.Main, 100f);
            drive.driveThrottle = 1f;
            boost.overchargeThrottle = 1f;

            EnergyState state = _energy.ResolvePower(_bus, null);

            Assert.Greater(state.overchargePower01, state.drivePower01);
            Assert.GreaterOrEqual(state.overchargeGranted, 62f - 0.001f);
            Assert.Less(state.overchargeGranted, state.overchargeRequest);
        }

        [Test]
        public void OverchargeRecharge_IsARealSharedBusLoad()
        {
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 80f);
            drive.driveThrottle = 1f;

            var overcharge = _craft.AddComponent<OverchargeCore>();
            overcharge.fullBurnDuration = 0.4f;
            overcharge.regenerationDelay = 0f;
            overcharge.rechargeBusDemand = 50f;

            EnergyState fullPower = EnergyState.Full;
            overcharge.TickOvercharge(new CraftIntent
            {
                boostHeld = true,
                boostPressed = true
            }, default, fullPower);
            overcharge.TickOvercharge(default, default, fullPower);

            Assert.IsTrue(overcharge.IsRecharging);

            EnergyState state = _energy.ResolvePower(_bus, overcharge);

            Assert.AreEqual(50f, state.overchargeRequest, 0.001f);
            Assert.AreEqual(50f, state.overchargeGranted, 0.001f);
            Assert.AreEqual(1f, state.overchargeChargePower01, 0.001f);
            Assert.AreEqual(100f, state.totalGranted, 0.001f);
            Assert.Less(state.drivePower01, 1f);
        }

        [Test]
        public void UnusedReservations_ReturnToActiveChannels()
        {
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 70f);
            drive.driveThrottle = 1f;

            EnergyState state = _energy.ResolvePower(_bus, null);

            Assert.AreEqual(70f, state.driveGranted, 0.001f);
            Assert.AreEqual(1f, state.drivePower01, 0.001f);
        }

        [Test]
        public void ProtectedStabilizer_DoesNotStarveLoopPropulsion()
        {
            // Loop retention can legitimately ask for far more raw roof-thruster
            // power than the performance BUS. That automatic safety demand must
            // not reduce the main engine.
            var stabilizer = AddNode("Roof", ThrusterNode.ThrusterRole.Roof, 400f);
            var drive = AddNode("Main", ThrusterNode.ThrusterRole.Main, 100f);
            stabilizer.stabilizerThrottle = 1f;
            drive.driveThrottle = 1f;

            EnergyState state = _energy.ResolvePower(_bus, null);

            Assert.IsTrue(state.stabilizerProtected);
            Assert.AreEqual(400f, state.stabilizerGranted, 0.001f);
            Assert.AreEqual(1f, state.stabilizerPower01, 0.001f);
            Assert.AreEqual(100f, state.driveGranted, 0.001f);
            Assert.AreEqual(1f, state.drivePower01, 0.001f);
        }
    }
}
