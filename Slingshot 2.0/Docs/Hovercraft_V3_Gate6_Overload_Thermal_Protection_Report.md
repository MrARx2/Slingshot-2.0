# Hovercraft V3 Gate 6 Overload and Thermal Protection Report

## Status

**Gate 6: passed.**

V3 now has an explicit emergency-overload command and a complete thermal
protection cycle. Normal commands retain the signed-off V2-parity behavior.
The overload path is opt-in, power-limited through the existing distributor,
heat-producing through the shared thermal model, and unavailable while a part
is cooling from an overheat event.

V2 source, prefabs, and scenes remain untouched.

## Player command

- Hold either Ctrl key to request emergency overload.
- Either Shift key remains reserved exclusively for Grip Breaker.
- `V3PilotCommand.EmergencyOverload` carries the command through the existing
  controller pipeline.
- `V3ActuatorCommandRouter` owns the per-frame overload authorization; actuator
  requests cannot reach their emergency ceiling without it.

The reference drive controller applies overload only to positive rear-main
thrust. Braking, hover recovery authority, stabilization, vectoring, and
manual vertical commands keep their established behavior.

## Output, power, and heat

The V2-reference main thruster now distinguishes its normal installed ceiling
from its emergency ceiling:

| Property | Normal | Full emergency overload |
| --- | ---: | ---: |
| Normalized rear-main output | 1.500 | 1.875 |
| Relative output | 100% | 125% |
| Applied force | 720,000 N | 900,000 N |
| Requested propulsion power | 720 | 1,116 |
| Relative full-output power | 100% | 155% |
| Generated heat | 25/s | 55/s |
| Relative full-output heat | 100% | 220% |

`RuntimeThrusterInstance` clamps ordinary requests to
`NormalOutputMultiplier`. An authorized overload request may reach
`OverloadOutputMultiplier`. Power demand rises from the normal-ceiling demand
to that demand multiplied by the authored `overloadDemandMultiplier`; heat
rises from normal maximum heat to maximum heat multiplied by the authored
`overloadHeatMultiplier`.

The rear chassis socket, fixed adapter, and gimbal are authored for the complete
900,000 N overload load. Build validation continues to evaluate the maximum
possible endpoint load and rejects an underspecified support chain.

## Thermal protection state machine

Every thermally enabled runtime part reports one of:

```text
Normal
Warning
Overheated
Cooling Lockout
Recovered
```

- Below the safe threshold, the part has full output.
- From the safe threshold to the overheat threshold, output is progressively
  derated to the authored hot-output limit.
- Reaching the overheat threshold immediately sets output availability to zero.
- Cooling lockout remains active for at least the authored lockout duration and
  until temperature is at or below the restart threshold.
- Crossing the restart condition emits `Recovered`; the following thermal tick
  returns the part to `Normal`.

Manual enable state is independent of thermal protection. A thermal shutdown
does not rewrite `IsEnabled`; operation requires both manual enable and thermal
availability.

`V3PartThermalTelemetry` now includes protection state, lockout state, and the
live output limit. `V3ThermalDebugDisplay` renders warning percentage,
overheated, cooling-lockout, and recovered indications. Gate 7 subsequently
delivered the cockpit warning controller and display that consume this state.

## Validation

- Gate 6 fast suite:
  `Temp/CodexV3UnityProject/TestResults-gate6-fast-r3.xml`
  (`36` passed, `1` opt-in capture skipped, `0` failed)
- Direct-build full parity regression:
  `Temp/CodexV3UnityProject/TestResults-gate6-parity-regression.xml`
  (`1/1` passed)
- Regression run ID: `20260727_063231`
- Archived regression telemetry:
  `Temp/Gate3ParityCapture/20260727_063231`

The behavioral suite directly verifies the 125% output, 155% power, and 220%
heat targets, plus warning derating, shutdown, minimum lockout, cooling below
restart temperature, and successful restart.

The no-overload regression summary remains within the Gate 3 acceptance
tolerances for idle clearance, top speed, acceleration times, braking distance,
yaw, strafe, and pitch.

## Gate decision

Gate 6 is accepted. Emergency performance is now a deliberate player trade:
temporary additional thrust consumes disproportionate power and heat, and
misuse produces visible derating followed by a recoverable thermal shutdown.
