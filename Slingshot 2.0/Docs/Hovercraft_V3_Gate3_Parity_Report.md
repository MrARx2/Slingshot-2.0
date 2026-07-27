# Hovercraft V3 Gate 3 Physical Parity Report

## Status

**Gate 3: passed on 2026-07-27 with physical-actuator-only authority.**

Every normal craft-control force and moment is produced by an installed
`RuntimeThrusterInstance` through
`Rigidbody.AddForceAtPosition(..., ForceMode.Force)`. Drive, hover, surface
alignment, yaw damping, traction, vectoring, and manual controls only submit
requests to `V3ActuatorCommandRouter`. No V3 controller calls `AddForce` or
`AddTorque`, and the root Rigidbody has zero authored angular damping.

The only direct Rigidbody state assignments in V3 runtime code are explicit
spawn/recovery resets and parity-scenario resets. They are not active handling
paths.

## Final sign-off capture

- Profile: `V2_V3_Parity_Command_Profile`
- Run ID: `20260727_150026`
- Fixed timestep: `0.02 s`
- Samples: `3,901` per craft
- Profile duration: `78 s`
- Test result:
  `Temp/CodexV3UnityProject/TestResults-physical-parity-capture-r7.xml`
  (`1/1` passed)
- Archived telemetry: `Temp/Gate3PhysicalCapture/20260727_150026_*`
- Final complete suite:
  `Temp/CodexV3UnityProject/TestResults-final-audit.xml`
  (`76` passed, `1` opt-in capture skipped, `0` failed)

The enforced capture test asserts ±2% for mass, maximum speed, acceleration
times, and brake/reverse distance; 5 cm for center of mass and idle clearance;
and ±5% for yaw, strafe, and pitch response. Power remains informational
because V3 accounts for the real physical stabilization and traction
thruster work that V2 applies through unmetered direct Rigidbody operations.

## Final comparison

| Metric | V2 | V3 | Delta | Result |
| --- | ---: | ---: | ---: | --- |
| Mass | 11,000 kg | 11,000 kg | 0% | Pass |
| COM | `(0, -0.5, 0)` m | `(0, -0.5, ~0)` m | <0.001 mm | Pass |
| Idle clearance mean | 3.4080 m | 3.4050 m | -0.0030 m | Pass |
| 30 s maximum speed | 7,063.40 km/h | 7,075.79 km/h | +0.18% | Pass |
| Time to 500 km/h | 2.2001 s | 2.2001 s | 0% | Pass |
| Time to 1,000 km/h | 4.3201 s | 4.3001 s | -0.46% | Pass |
| Brake/reverse distance | 788.06 m | 792.82 m | +0.60% | Pass |
| Peak yaw rate | 413.4054 deg/s | 412.2789 deg/s | -0.27% | Pass |
| Peak side speed | 13.0950 km/h | 12.6680 km/h | -3.26% | Pass |
| Pitch-pulse maximum | 3.5569 deg | 3.6870 deg | +3.66% | Pass |
| Peak requested/granted power | 850.15 | 3,646.91 | +329% | Informational |

No V3 power was shed during the sign-off profile. The higher V3 request is
expected and visible: V3's brake, traction, yaw damping, and surface alignment
all consume EnergyCore power through installed physical thrusters, whereas
the corresponding V2 traction and damping helpers apply direct unmetered
Rigidbody acceleration.

## Physical calibration

- The front brake exposes a 1.70 MN physical ceiling. Its ordinary drive
  contribution remains 380 kN; the remaining authority is available to the
  traction allocator while thrust opposes the craft's trajectory.
- Each lateral thruster exposes an 800 kN physical ceiling. Full strafe
  commands 19% output, preserving the calibrated 152 kN per-thruster lateral
  force. Steering and physical rate damping can use the remaining hardware
  authority.
- Yaw damping is requested as a desired torque and solved onto the lateral
  thrusters. It is never applied directly to the Rigidbody.
- Surface alignment and grounded grip use the same wrench allocator and are
  limited by installed direction, force, lever arm, thermal state, and power.
- The main engine retains its V2-reference 1.5× installed normal output and
  1.875× emergency ceiling.

## Gate decision

Gate 3 is accepted. The flat-ground reference craft retains the measured V2
handling identity inside the agreed tolerances, and every active V3 handling
correction is traceable to a visible, powered, thermally tracked physical
part.
