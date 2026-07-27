# Hovercraft V3 Gate 10 Craft Telemetry Hub Report

## Status

**Gate 10: passed.**

Gate 10 implements the craft-level Telemetry Hub required by GDD section
11.1. It consolidates existing runtime truth into one read-only interface
without adding a controller, force path, protection rule, or mutable shortcut
back into the simulation.

V2 source, prefabs, scenes, and behavior remain untouched.

## Runtime kernel contract

Every craft assembled by `V3CraftAssembler` now receives a
`V3CraftTelemetryHub`, including builds assembled with the reference controller
pipeline disabled. The hub is therefore runtime infrastructure rather than an
equippable gameplay control module.

The hub receives references to:

- `V3CraftRuntime` for build identity, installed parts, mass, and Rigidbody;
- `V3PowerDistributor` when a power system is present;
- `V3ThermalController` for authoritative aggregate protection telemetry.

It has no API for writing commands, applying forces, changing part state, or
selecting protection outcomes.

## Live telemetry

`RefreshLiveTelemetry` updates reusable internal lists so ordinary fixed-step
observation does not allocate after list capacity is established.

Craft-level telemetry includes:

- build and chassis identity;
- calculated mass and center of mass;
- world position and rotation;
- craft-local linear and angular velocity;
- speed in km/h;
- selected power-allocation mode;
- requested and granted systems and propulsion power;
- available propulsion budget and power-limit flags;
- requested and granted power for every actuator priority in current
  allocation order;
- maximum temperature and hot, overheated, and locked-out part counts.

Per-part telemetry includes:

- socket, stable part ID, display name, role, and connector identity;
- mass and craft-local transform;
- requested and actual output;
- requested and granted power;
- thermal state, output limit, enabled state, and lockout;
- applied force for thrusters;
- pitch/yaw deflection, velocity, and torque for gimbals;
- displacement, velocity, and endpoint load for spring mounts.

Connector and endpoint records remain separate even when both share the same
parent socket ID.

## Frozen snapshot contract

`CaptureSnapshot` refreshes the live data, copies both telemetry lists, and
returns a `V3CraftTelemetrySnapshot`. Snapshot fields are get-only, each part
record is a readonly value, and collections are exposed through
`ReadOnlyCollection`.

This gives future cockpit screens, diagnostics, recording, remote telemetry,
and save/export code a stable point-in-time view while the live craft
continues to change.

The snapshot provides a combined requested/granted total and a lookup helper
that can match a socket, stable part ID, or both. Stable part identity is
necessary because a connector chain can legitimately produce multiple records
for one socket.

## Automated proof

The four Gate 10 tests verify:

1. The reference snapshot reports its 16 installed parts, 11,000 kg mass,
   calibrated center of mass, local motion, power totals, thermal aggregate,
   and the rear main thruster's 1.5 output, 720 power, and 720,000 N force.
2. A captured snapshot remains unchanged after the live craft resets and
   switches from Balanced to Propulsion mode; the new snapshot reports the new
   state and updated priority order.
3. The gimbal variation reports 17 installed parts and preserves both the
   moving gimbal and unchanged main thruster at `Propulsion.Rear.Center`.
4. A craft assembled without reference controllers still installs a functioning
   telemetry kernel with part, mass, and thermal data and no fabricated power
   values.

## Validation

- Gate 10 fast suite:
  `Temp/CodexV3UnityProject/TestResults-gate10-fast-r2.xml`
  (`51` passed, `1` opt-in capture skipped, `0` failed)
- Full direct-build parity regression:
  `Temp/CodexV3UnityProject/TestResults-gate10-parity-regression.xml`
  (`1/1` passed)
- Regression run ID: `20260727_093058`
- Archived regression telemetry:
  `Temp/Gate3ParityCapture/20260727_093058`

All compared V3 scalar metrics are exactly unchanged from Gate 9:

- 3.4050248 m idle clearance;
- 7,075.7573 km/h maximum speed;
- 2.2000504 s to 500 km/h;
- 4.3000984 s to 1,000 km/h;
- 792.3162 m braking distance;
- 413.4055 degrees/s peak yaw rate;
- 13.077149 km/h peak side speed;
- 919.6492 requested and 919.6490 granted peak power.

## Gate decision

Gate 10 is accepted. V3 now has one authoritative observation surface for
craft, power, thermal, actuator, and connector state. Future displays and
diagnostic systems can consume snapshots without reaching into controllers or
creating parallel simulation logic.
