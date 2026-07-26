# Hovercraft V3 — Aerodynamics Architecture

Implementation report for the CraftAerodynamics + WorldAtmosphere V3 upgrade.
Companion tuning document: `Hovercraft_V3_Aerodynamics_TuningGuide.md`.

## 1. Overview

```text
WorldAtmosphere            defines the air (density, wind, multipliers, turbulence)
        ↓
CraftAerodynamics          owns one craft's aero state and solver coordination
        ↓
AeroBodyDefinition         chassis-level directional behavior (CdA per axis, CoP)
        ↓
AeroSurface (×N)           wings, fins, stabilizers, flaps, ground-effect plates
        ↓
Unity Rigidbody            receives forces via AddForceAtPosition
```

- The world defines the environment; each craft defines its own reaction. There
  is no global script pushing identical forces onto every craft.
- All force math lives once in the static `AerodynamicsSolver`.
- All forces are applied in `FixedUpdate` (CraftCore pipeline step 2b), once per
  physics step, allocation-free. No kinematic rotation, no velocity writes, no
  orientation snapping anywhere.

## 2. Files

| File | Role |
|---|---|
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/WorldAtmosphere.cs` | Scene-wide atmosphere: density, wind, global multipliers, deterministic turbulence, presets, `AtmosphereState`, `GetAirVelocityAtPosition` |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/CraftAerodynamics.cs` | Per-craft coordinator; modes LegacyV2 / DirectionalBodyV3 / SurfaceBasedV3; safety clamps; debug snapshot |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/AeroBodyDefinition.cs` | Serializable chassis model: signed CdA per axis, center of pressure, optional stability damping + body lift |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/AeroSurface.cs` | One physical surface (child GameObject); AoA lift/drag or ground-effect probe; control-surface API |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/AeroSurfaceProfile.cs` | ScriptableObject with shareable coefficient/stall/GE-curve sets |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/AerodynamicsSolver.cs` | Stateless equations: dynamic pressure, directional drag, AoA, stall, surface evaluation, GE falloff |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/AerodynamicsDebugSnapshot.cs` | `AerodynamicsDebugSnapshot` (craft aggregate) + `AeroSurfaceState` (per surface) |
| `Assets/Scripts/Hovercraft_Setup/Editor/CraftAerodynamicsEditor.cs` | Mode-aware sectioned inspector, surface summary, validation, live debug |
| `Assets/Scripts/Hovercraft_Setup/Editor/WorldAtmosphereEditor.cs` | Sectioned inspector, preset apply, live debug (wind/turbulence at scene camera, craft count) |
| `Assets/Scripts/Hovercraft_Setup/Editor/AeroSurfaceEditor.cs` | Validation, stable-ID regenerate, live per-surface state |

`WorldAtmosphere.cs` moved from `Assets/Scripts/World/` and
`CraftAerodynamics.cs` from `Hovercraft_Setup/Hovercraft/` into the new
`Aerodynamics/` folder **with their .meta files**, so all prefab/scene
references survive.

## 3. Modes and compatibility

```csharp
public enum CraftAerodynamicsMode { LegacyV2, DirectionalBodyV3, SurfaceBasedV3 }
```

- **LegacyV2** (default, current Medium Rev4 craft): the original code path,
  byte-for-byte — isotropic drag `½ρv²CdA`, wing downforce along −up, ground
  effect along the telemetry surface normal, `maxDownforceG` clamp. The new
  `globalGroundEffectMultiplier` is deliberately NOT applied on this path so
  legacy behavior cannot drift.
- **DirectionalBodyV3**: signed per-axis body drag applied at the center of
  pressure. Two migration toggles (`useLegacyWingDownforceInBodyMode`,
  `useLegacyGroundEffectInBodyMode`, both default on) keep the aggregate
  wing/GE forces alive until surfaces replace them (migration step 2).
- **SurfaceBasedV3**: body model + all `AeroSurface` children. Legacy bridge is
  off; wings/GE must exist as surfaces.

Legacy readouts (`LastDragForce`, `LastWingDownforce`,
`LastGroundEffectDownforce`, `LastDownforceG`) stay populated in **every** mode,
so the Test Drive Recorder, HUD and config diagnostics keep working unchanged.

## 4. WorldAtmosphere

- One authoritative instance per scene (extra instances log a warning and take
  over, matching the previous behavior). Absence of an instance falls back to
  safe defaults: 1.225 kg/m³, zero wind, all multipliers 1, turbulence off.
- `AtmosphereState CurrentState` — immutable readonly struct; crafts read
  this, never individual fields.
- `GetAirVelocityAtPosition(worldPos)` = wind + turbulence. Turbulence is
  seeded, scrolled Perlin noise — smooth, deterministic, bounded to
  ±`turbulenceStrengthMs` per axis. Never per-frame random forces. Default off.
- Presets (`ApplyPreset`): EarthStandard, ThinAtmosphere, DenseAtmosphere,
  HighAltitude, Storm, Vacuum, Custom. Presets set environment values only —
  never craft coefficients.
- `airDensity` was renamed `airDensityKgM3` with `[FormerlySerializedAs]`
  (scene/prefab data migrates automatically) and a JSON-key migration on config
  import (old configs still apply).
- Static accessors preserved (`AirDensity`, `DragMultiplier`,
  `DownforceMultiplier`) plus new ones (`LiftMultiplier`,
  `GroundEffectMultiplier`, `ControlSurfaceMultiplier`, `WindVelocity`,
  `State`, `GetAirVelocityAt`).

## 5. Relative airflow

Every calculation point uses:

```text
point velocity  = Rigidbody.GetPointVelocity(worldPoint)
air velocity    = WorldAtmosphere.GetAirVelocityAtPosition(worldPoint)
relative airflow = point velocity − air velocity
```

The body model samples at the COM; every surface samples at its own transform
position, so angular motion changes each surface's airflow individually (a
yawing craft sees different flow at nose fin vs tail fin). There is no single
craft-wide AoA fed to surfaces.

## 6. Directional body drag and center of pressure

Local airflow decomposes into signed axes; each axis has its own CdA
(`forward/reverse/side/top/bottom`), each scaling with speed²:
`F_axis = −½ρ·|v_axis|·v_axis·CdA_axis`. The summed body force (plus optional
body lift) is applied at `centerOfPressureLocalM` via `AddForceAtPosition` —
never at the COM. CoP behind COM = passive weathervane stability; ahead = agile
but twitchy; above/below couples drag into pitch/roll. Scene gizmos draw COM,
CoP, airflow, body force and total torque.

Optional `pitch/yaw/rollStability` coefficients add explicit aerodynamic
damping torque (scaled by dynamic pressure, applied with `AddTorque`) — default
0 to avoid double-counting the natural CoP torque.

## 7. AeroSurface

A child GameObject's transform defines the geometry: **+Z chord, +Y lift axis,
+X span**. Identity uses a stable auto-generated `surfaceId` (config/recorder
never rely on GameObject names alone). Per surface:

- Signed AoA from airflow projected into the chord–lift plane.
- Lift perpendicular to airflow in that plane; drag opposing airflow; both from
  either a shared `AeroSurfaceProfile` asset or inline coefficients; custom
  absolute Cl/Cd-vs-AoA curves override the analytic model.
- Smooth stall: attached lift blends to `postStallLiftMultiplier` between 1.0×
  and 1.4× `stallAngleDeg`; drag grows with sin²(AoA) past stall. No hard
  discontinuity.
- Control surfaces: `SetControlDeflection(deg)` rate-limited by
  `controlResponseSpeedDegPerSec`, scaled by `controlAuthorityMultiplier` and
  the world's `globalControlSurfaceMultiplier`. Nothing drives them yet — the
  API exists for player input / VectoringComputer / future predictive systems.
- Ground effect (`surfaceType == GroundEffect`): raycast along the surface's
  own −up (follows the craft, so banked/inverted track works) against
  `groundEffectLayers`; no hit inside `groundEffectRangeM` = exactly zero force
  (no stale normals, force can never persist past a track edge); force =
  `q_planar · area · |Cl| · falloff(height) · globalGroundEffectMultiplier`,
  pressing toward the actual hit surface along −hit.normal. Hit distance and
  normal are exposed in `AeroSurfaceState`.

Surfaces never apply forces themselves — `CraftAerodynamics` owns application
and clamping.

## 8. Numerical safety

- `minimumAirflowSpeedMs` (default 2) gates all V3 force generation.
- NaN/infinite airflow or force results are rejected and counted
  (`rejectedInvalidValues` in the snapshot — never hidden).
- Coefficients clamped to |4| (`AerodynamicsSolver.MaxCoefficient`).
- `maximumAeroAccelerationG` (default 12) scales ALL applied forces down when
  the total exceeds it; `forceClamped` flag recorded.
- `maximumAeroTorqueNm` (default 0 = off) shrinks lever arms toward COM
  (preserving total force) when estimated torque exceeds it; `torqueClamped`
  flag recorded.
- Direction vectors normalized via `SafeNormalize`; no divisions by near-zero
  speed.

## 9. Config system integration

- The `CraftAerodynamics` component (mode, legacy fields, `bodyDefinition`,
  safety values) exports/imports through the existing generic `components`
  section — no mirror schema.
- Surfaces export as a top-level `aeroSurfaces` array (like `thrusters`):
  stable `surfaceId`, GameObject name (fallback matching only), record-only
  position/rotation, full settings tree. Import matches by `surfaceId` first,
  warns on duplicates/missing IDs, and never moves or creates hardware.
- The `worldAtmosphere` section is still exported for reference only and is
  applied **only** when `Apply World Atmosphere Section` is explicitly enabled
  on the CraftConfigManager (existing policy, kept). Legacy `airDensity` keys
  migrate to `airDensityKgM3` on import.

## 10. Test Drive Recorder integration

Schema bumped to **1.1**; two new streams (files only appear when data exists):

- **Stream I — `aero_frames.csv`** (core rate): world data (density, wind,
  turbulence, global multipliers) + craft aggregate (mode, relative air
  velocity world/local, airspeed, dynamic pressure, body drag/lift, surface
  drag/lift totals, GE total, total force/torque, CoP, AoA, side-slip, aero G,
  clamp flags, rejected-value count).
- **Stream J — `aero_surfaces.csv`** (node rate, SurfaceBasedV3 only): per
  surface — stable-ID table in the header, position, airflow, AoA, Cl/Cd,
  lift/drag/total force, estimated torque, stall, deflection, GE
  distance/multiplier, efficiency.
- Airborne events (Takeoff, Landing, UnexpectedLaunch) carry the full aero
  force balance in their detail: velocity, vertical velocity, gravity, hover
  force, GE, body aero, surface aero, torque, AoA, side-slip, probe state,
  up-vs-surface-normal, forward-vs-airflow.
- `session_metadata.json` gains a `worldAtmosphere` block, `aeroMode`, and the
  aero surface identity table.

## 11. Performance

- No reflection, LINQ or per-step allocations in any physics path.
- Transforms, surface arrays and force buffers cached/preallocated
  (`RefreshSurfaces` resizes once).
- Atmosphere access is a static instance reference — no scene searches in
  FixedUpdate.
- Per step: DirectionalBodyV3 ≈ one transform of a vector + a handful of
  multiplies; SurfaceBasedV3 adds one `GetPointVelocity`, one solver
  evaluation and (GE surfaces only) one raycast per surface — comfortably
  within the 0.1 / 0.25 ms targets at 100 Hz.

## 12. Validation checklist (spec tests 1–10)

| # | Test | How to run |
|---|---|---|
| 1 | Zero airflow → zero force/torque | Craft parked, no wind; inspector Debug shows 0 N / 0 N·m (min-airflow gate) |
| 2 | Constant headwind | Set `windVelocityWorldMs` opposite craft forward; drag opposes wind, CoP torque yaws nose into it |
| 3 | Forward motion | Drive straight; only forward drag, no side force |
| 4 | Backward motion | Reverse; drag uses `reverseCdA` (higher) |
| 5 | Sideways motion | Strafe/slide; strong `sideCdA` drag + rear-fin yaw alignment |
| 6 | Nose-up airborne | Jump with pitch input; AoA, lift and pitch torque respond in Debug/recorder |
| 7 | Inverted motion | Loop/ceiling; GE probes along craft −up, hit normal drives force direction |
| 8 | GE fade | Vary height over track; `aero_surfaces.csv` shows smooth falloff, zero past range |
| 9 | Wind + turbulence | Two craft, same storm preset, different definitions → different reactions; same seed → same gusts |
| 10 | Legacy comparison | Rev4 craft in LegacyV2 vs pre-upgrade build: identical forces |

## 13. Explicitly not done (per spec restrictions)

No CFD, no heat/damage gameplay, no predictive active aero, no Rev4 hover
retuning, no removal of LegacyV2, no kinematic orientation correction, no
craft coefficients in WorldAtmosphere, no wind in craft configs.
