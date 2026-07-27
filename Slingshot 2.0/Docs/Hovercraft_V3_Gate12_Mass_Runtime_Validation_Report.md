# Hovercraft V3 Gate 12 Mass and Runtime Validation Report

## Status

**Gate 12: passed.**

Gate 12 extracts mass calculation and post-assembly validation into the
explicit runtime-kernel services required by GDD section 11.1. The original
mass equation and parity calibration are unchanged.

V2 source, prefabs, scenes, and behavior remain untouched.

## Craft Mass Calculator

`V3CraftMassCalculator` now owns the calculation previously embedded in
`V3CraftAssembler`.

```text
Total Mass =
Chassis Base Mass
+ Sum of installed connector and endpoint masses

Uncalibrated Center of Mass =
Sum of each mass multiplied by its root-local mass center
/ Total Mass

Final Center of Mass =
Uncalibrated Center of Mass
+ Authored parity calibration
```

The calculator applies only the resulting `Rigidbody.mass` and
`Rigidbody.centerOfMass`. It does not apply forces, alter inertia manually, or
move transforms.

Its immutable `V3MassCalculationResult` exposes:

- chassis base mass;
- installed-parts mass;
- total applied mass;
- uncalibrated center of mass;
- visible parity calibration;
- final center of mass;
- one readonly `V3MassContribution` for every runtime connector or endpoint.

Each contribution retains socket ID, stable part ID, display name, mass, and
root-local mass center. This makes the calculation auditable and prevents
connector mass from being hidden in chassis or endpoint values.

## Verified reference totals

| Build | Chassis | Installed parts | Total | Contributions |
| --- | ---: | ---: | ---: | ---: |
| V2 reference | 8,430 kg | 2,570 kg | 11,000 kg | 16 |
| Main gimbal variation | 8,430 kg | 2,690 kg | 11,120 kg | 17 |
| Hover spring variation | 8,430 kg | 2,605 kg | 11,035 kg | 17 |

The gimbal ledger contains its 120 kg connector exactly once. The spring
ledger contains its 35 kg connector exactly once.

## Runtime Build Validator

`V3RuntimeBuildValidator` runs after the craft has been instantiated and the
Gate 11 registries have been populated. It preserves the authoring validator
report and adds an independent runtime report.

The runtime pass verifies:

- all required kernel services exist;
- exactly one Rigidbody exists on the assembled root;
- the runtime socket registry matches the chassis socket count;
- every saved socket has the expected runtime chain length;
- connector and endpoint definitions match their saved selections;
- registered and exposed installed-part counts match the build;
- capability requirements remain satisfied after instantiation;
- calculator, Rigidbody, and `V3CraftRuntime` mass values agree;
- calculator, Rigidbody, and runtime center-of-mass values agree.

Assembly is rejected and the temporary runtime root is removed if this pass
fails. The validator can also be refreshed later for diagnostics.

## Corruption and recovery proof

The automated corruption test changes the assembled reference Rigidbody from
11,000 kg to 11,010 kg. The runtime validator reports
`RUNTIME_MASS_MISMATCH`, and the telemetry snapshot becomes invalid with one
diagnostic issue.

Reapplying the authoritative mass calculation restores 11,000 kg. A refreshed
validation passes and telemetry returns to a valid zero-issue state.

This proves that validation is inspecting actual runtime state rather than
merely repeating the saved build's values.

## Telemetry integration

Gate 10 snapshots now include:

- chassis base mass;
- installed-parts mass;
- parity center-of-mass calibration;
- runtime-build validity;
- combined authoring/runtime issue count.

The full contribution ledger remains available through
`V3CraftMassCalculator.Result` for detailed diagnostic and future builder
views.

## Validation

- Gate 12 fast suite:
  `Temp/CodexV3UnityProject/TestResults-gate12-fast.xml`
  (`60` passed, `1` opt-in capture skipped, `0` failed)
- Full direct-build parity regression:
  `Temp/CodexV3UnityProject/TestResults-gate12-parity-regression.xml`
  (`1/1` passed)
- Regression run ID: `20260727_100656`
- Archived regression telemetry:
  `Temp/Gate3ParityCapture/20260727_100656`

All compared V3 scalar metrics are exactly unchanged from Gate 11:

- 3.4050248 m idle clearance;
- 7,075.7573 km/h maximum speed;
- 2.2000504 s to 500 km/h;
- 4.3000984 s to 1,000 km/h;
- 792.3162 m braking distance;
- 413.4055 degrees/s peak yaw rate;
- 13.077149 km/h peak side speed;
- 919.6492 requested and 919.6490 granted peak power.

## Gate decision

Gate 12 is accepted. Mass and post-assembly validity now have explicit,
auditable runtime owners, closing the remaining kernel-service extraction
without changing the signed-off physical craft.
