# Hovercraft V3 Gate 4 Thermal Foundation Report

## Status

**Gate 4: passed.**

V3 now tracks heat for every thermally enabled installed part, exposes
part-level and craft-level thermal telemetry, and provides an in-game debug
overlay. Temperature remains observational: safe and overheat thresholds do not
disable, limit, or lock out parts during Gate 4.

V2 source, prefabs, and scenes remain untouched.

## Implemented foundation

### Runtime thermal model

`RuntimePartInstance` owns per-installation state:

- current temperature;
- generated heat per second;
- passive cooling per second;
- normalized ambient-to-overheat temperature;
- above-safe and overheated flags;
- reset-to-ambient behavior.

Normal output interpolates between idle and maximum heat. Output above 100%
uses the authored overload heat multiplier. Passive cooling scales with
temperature above ambient and the authored cooling coefficient.

### Central thermal controller

`V3ThermalController` advances every thermally enabled installed part once per
physics tick. It publishes a snapshot for each relevant part containing:

- socket and stable part identity;
- current and ambient temperature;
- safe, overheat, and restart thresholds;
- generated heat and passive cooling rates;
- normalized temperature;
- safe/overheat/enabled state.

It also exposes maximum craft temperature and hot/overheated part counts.
Dynamic reset returns all parts to their authored ambient temperature, keeping
scripted captures deterministic.

### Debug display

`V3ThermalDebugDisplay` is installed on every assembled V3 craft. Its overlay
shows maximum craft temperature plus one row per thermally enabled part,
including socket, temperature, heat generation, cooling, and `HOT` or
`OVERHEATED` state.

The overlay can be disabled through `ShowOverlay`; it does not own or mutate
thermal state.

## Gate 4 pass-condition evidence

The focused tests prove that:

1. The reference craft publishes telemetry for all 16 installed relevant parts.
2. A thruster held at full output heats more than 5 °C above an idle thruster
   over the same interval.
3. The loaded thruster cools after its output is returned to zero.
4. A 150% output request uses the reserved 2.2× overload heat multiplier,
   producing 52.5 heat units/s versus 25 at normal maximum.
5. Crossing the safe and overheat thresholds sets telemetry flags while leaving
   the part enabled.
6. The overlay consumes the controller telemetry and includes stable socket
   identity.

## Validation

- Fast suite:
  `Temp/CodexV3UnityProject/TestResults-gate4-fast.xml`
  (`32` passed, `1` opt-in capture skipped, `0` failed)
- PlayMode parity-scene startup smoke: passed as part of the fast suite
- Full Gate 3 regression:
  `Temp/CodexV3UnityProject/TestResults-gate4-parity-regression.xml`
  (`1/1` passed)
- Regression run ID: `20260726_224753`
- Archived regression telemetry:
  `Temp/Gate3ParityCapture/20260726_224753`

The regression V3 summary exactly matches the Gate 3 sign-off for idle
clearance, top speed, 500/1,000 km/h times, brake distance, yaw rate, side
speed, and requested power.

## Historical Gate 4 boundary

Gate 4 intentionally deferred the following items. Gate 6 and Gate 7 have now
completed them:

- emergency overload input;
- automatic output limiting;
- thermal shutdown or reduced-output protection;
- cooling lockout and restart control;
- cockpit warning presentation.

## Gate decision

Gate 4 is accepted. A heavily used thruster heats faster than an idle thruster,
cools when unloaded, and exposes its authored thresholds and live thermal values
through the craft telemetry and debug display without affecting parity.
