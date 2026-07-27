# Hovercraft V3 Gate 9 Power Distribution Report

## Status

**Gate 9: passed.**

Gate 9 turns the fixed V2-parity load order into a pilot-selectable power
distribution system. The signed-off order remains the default, so builds that
never select another mode retain the existing physical behavior.

V2 source, prefabs, scenes, and behavior remain untouched.

## Allocation modes

The EnergyCore still reserves systems demand before exposing the remaining
continuous output to propulsion. Within that propulsion budget, requests in
the same tier are scaled proportionally and the selected mode controls only
the order in which tiers receive power.

| Mode | Allocation order |
| --- | --- |
| Balanced | Critical hover, stabilization, vectoring, drive, optional |
| Propulsion | Critical hover, drive, vectoring, stabilization, optional |
| Stability | Stabilization, critical hover, vectoring, drive, optional |
| Recovery | Critical hover, stabilization, drive, vectoring, optional |

Balanced exactly matches the Gate 3 through Gate 8 policy. Propulsion preserves
base hover and then favors forward output. Stability is an explicit
attitude-first brownout choice and may trade altitude authority under an
extreme shortage. Recovery restores base hover first, then attitude control,
then forward authority.

The distributor exposes the selected mode, the ordered policy, requested and
granted power per actuator priority, total shed propulsion power, and the
overall propulsion grant fraction. No mode can create output that the
EnergyCore did not grant.

## Pilot and cockpit contract

`V3PilotCommand.CyclePowerAllocationMode` is an edge-triggered command. The
runtime Input System map binds it to:

- Tab on keyboard;
- D-pad up on gamepad.

The modes cycle in the table order and can also be selected directly through
`V3PowerDistributor.SetAllocationMode`.

The cockpit now always shows the active power mode, including while all systems
are nominal. During a propulsion shortage it reports a warning containing the
selected mode and total granted percentage. A systems-channel shortage is a
critical alert and is prioritized above ordinary thermal limiting; thermal
lockout and overheat remain the highest critical conditions.

## Constrained-core proof

The integration test clones the reference build in memory and reduces its
EnergyCore to 850 units. After the unchanged 45-unit systems reservation, 805
units remain for a simultaneous 1,440-unit active actuator request.

The observed allocations are:

| Mode | Critical | Stabilization | Vectoring | Drive |
| --- | ---: | ---: | ---: | ---: |
| Balanced | 640 | 160 | 5 | 0 |
| Propulsion | 640 | 0 | 0 | 165 |
| Recovery | 640 | 160 | 0 | 5 |

A second stability-focused case reduces total output to 700 units, leaving 655
for propulsion. Stability grants its 160-unit attitude request first and
proportionally grants the remaining 495 units across the four critical hover
thrusters. The test also verifies mode cycling, per-tier telemetry, total
shedding, grant fraction, and the cockpit warning/readout.

## Shared-actuator priority correctness

One installed thruster may receive work from more than one controller in the
same physics step. For example, a hover endpoint can carry both critical base
hover and stabilization demand. The router retains those contributions by
priority, decomposes the endpoint's power demand proportionally, and lets the
selected mode allocate each contribution independently. It then accumulates
the grants and applies the physical thruster once.

The constrained shared-thruster test submits equal critical and stabilization
loads to one endpoint. Balanced grants the critical contribution first, while
Stability grants the stabilization contribution first. This closes the former
priority-promotion bug where the highest priority on an endpoint incorrectly
promoted all of that endpoint's work.

## Validation

- Final complete suite:
  `Temp/CodexV3UnityProject/TestResults-final-audit.xml`
  (`76` passed, `1` opt-in capture skipped, `0` failed)
- Full balanced-mode physical parity:
  `Temp/CodexV3UnityProject/TestResults-physical-parity-capture-r7.xml`
  (`1/1` passed)
- Final run ID: `20260727_150026`
- Archived telemetry:
  `Temp/Gate3PhysicalCapture/20260727_150026_*`

The final Balanced reference metrics include:

- 3.4050248 m idle clearance;
- 7,075.7891 km/h maximum speed;
- 2.2000504 s to 500 km/h;
- 4.3000984 s to 1,000 km/h;
- 792.8210 m brake/reverse distance;
- 412.2789 degrees/s peak yaw rate;
- 12.6680 km/h peak side speed;
- 3,646.9114 requested and granted peak power.

## Gate decision

Gate 9 is accepted. Power scarcity is now a visible, testable gameplay
decision instead of an implicit fixed rule, while Balanced remains a
zero-regression default for the V2-parity foundation.
