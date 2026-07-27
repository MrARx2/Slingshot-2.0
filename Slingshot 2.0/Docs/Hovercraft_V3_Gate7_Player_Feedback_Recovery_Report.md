# Hovercraft V3 Gate 7 Player Feedback and Recovery Report

## Status

**Gate 7: passed.**

Gate 7 is the first post-v0.5 production-readiness slice. It converts the
prototype's direct device polling into rebindable Input System actions, applies
the Gate 6 overload/thermal states to a cockpit-facing warning display, and
adds deterministic physical validation for sloped-ground and airborne
recovery.

V2 source, prefabs, scenes, and behavior remain untouched.

## Action-based pilot input

`V3PilotInputAdapter` now owns a runtime `InputActionAsset`. Gameplay code still
receives the same `V3PilotCommand`, so the controller pipeline and parity
harness do not depend on a specific device.

| Command | Keyboard/mouse | Gamepad |
| --- | --- | --- |
| Throttle / brake | W / S | Right / left trigger |
| Strafe | A / D | Left stick X |
| Yaw / pitch | Mouse delta | Right stick |
| Manual lift | E | Right shoulder |
| Downforce | Q | Left shoulder |
| Grip Breaker | Either Shift | East button |
| Emergency overload | Either Ctrl | South button |
| Toggle stabilization | R | Start |

The adapter exposes action lookup plus binding-override save/load methods for a
future settings screen. Clearing the stored JSON restores the authored default
bindings.

## Cockpit warning application

Every assembled reference craft now installs:

- `V3CockpitWarningController`, which reduces thermal and overload telemetry to
  one prioritized cockpit alert;
- `V3CockpitWarningDisplay`, which renders non-clear alerts at the top center of
  the screen.

Alert priority is:

```text
Thermal lockout / overheat
Thermal output limiting
Emergency overload active
Systems nominal
```

The display reports maximum installed-part temperature and, during warning
derating, the lowest live output limit. It consumes the authoritative thermal
and actuator telemetry and does not run a second protection model.

## Reachable thermal gameplay

The V2-reference main thruster's response is tuned so the protection path can
occur in the actual prototype:

- normal full output settles near 85.8 C, below the 90 C safe threshold;
- sustained full overload crosses the warning threshold after approximately
  35 seconds;
- sustained full overload reaches the 120 C shutdown threshold after
  approximately 79 seconds, including progressive warning derating;
- cooling lockout still requires both its minimum duration and cooling to the
  75 C restart threshold.

This changes thermal response only. The normal force, power, and handling
calibration remain unchanged.

## Recovery validation

The isolated physical suite adds two repeatable scenarios:

1. A 12-degree layer-8 slope. From a world-level starting orientation, the
   reference craft remains grounded and reduces surface-normal alignment error
   from 12 degrees to 6.59 degrees within five simulated seconds. The acceptance
   requirement is at least 40% error reduction without changing the signed-off
   hover force balance.
2. An airborne start at 15 m, with 15 degrees of roll and 10 m/s downward
   velocity. Ground probes remain correctly ungrounded while full manual lift
   improves vertical velocity over the next 0.5 seconds.

Perfect slope-normal convergence and automatic airborne leveling remain feel
tuning opportunities rather than parity-foundation requirements.

## Validation

- Gate 7 fast suite:
  `Temp/CodexV3UnityProject/TestResults-gate7-fast-r4.xml`
  (`40` passed, `1` opt-in capture skipped, `0` failed)
- Full no-overload parity regression:
  `Temp/CodexV3UnityProject/TestResults-gate7-parity-regression-r2.xml`
  (`1/1` passed)
- Regression run ID: `20260727_065203`
- Archived regression telemetry:
  `Temp/Gate3ParityCapture/20260727_065203`

## Gate decision

Gate 7 is accepted. The prototype now has device-independent, rebindable pilot
actions; visible overload and thermal consequences; and automated evidence for
basic slope and airborne recovery without regressing the V2-reference handling
profile.
