# Hovercraft V2 — Medium Baseline Implementation Report

2026-07-23. Implements the Medium Craft Physical Baseline plan (Phases 0–13). Phase 14 (test protocol) requires driving the craft — see `Hovercraft_V2_MediumBaseline_TestResults.md` and the checklist at the end.

## Phase 0 — Preserved states (snapshot report)

| State | Where | Key values |
|---|---|---|
| 10-ton test (unsaved live tuning) | `CraftConfigs/Hovercraft_V2_10T_HighForce_Test.json` (metadata stamped: unsaved test, not baseline) | 10,000 kg, 500 kN thrusters, assist 25, angular damping 0 |
| Legacy 1-ton (saved prefab+scene) | `CraftConfigs/Hovercraft_V2_Legacy_1T.json` (authored from audited YAML; prefab on disk remains authoritative) | 1,000 kg, hover/roof 40 kN, main 100 kN, strafe 30 kN, scene assist 75 |
| New medium baseline | `CraftConfigs/Hovercraft_V2_Medium_7_5T_Baseline.json` | see below |

Unsaved-value warning from the audit still applies: **the prefab/scene on disk were NOT modified by this task** — the baseline arrives via the apply tool + config import (your checklist below).

## Final baseline definition

- **Dimensions** (authoritative, `CraftPhysicalDefinition`): 6.5 L × 3.2 W × 1.4 H m; hover footprint 4.8 × 2.6 m; ground clearance 2.5 m; class Medium.
- **Mass** 7,500 kg. **Gravity policy:** Earth gravity only — `downwardAssistMs2 = 0` (the renamed, honestly-documented former `extraGravity` is kept as an optional assist).
- **Collider layout:** compound 3-box (root chassis ~2.94×1.4×3.58 m + nose + tail), built by the apply tool; Continuous Dynamic; craft body physics material.
- **COM:** manual, (0, −0.35, 0) — 0.35 m above the body bottom plane, longitudinal center; optional ballast (offset + kg) shifts it. Applied to the Rigidbody at Awake and after config import; gizmo shown.
- **Inertia policy (decided):** automatic tensor from the compound colliders as the physical base, with optional per-axis multipliers (Option B). Baseline multipliers (1,1,1).
- **Physics materials:** craft hull 0.05 friction / 0.05 bounce / combine **Minimum** (`HoverPhysicsMaterials.CraftBody`); every generated track collider now gets 0.35 friction / Minimum (`TrackSurfacePhysics.Surface`, assigned in `BoxPrismTrackMeshBuilder`). Contacts resolve to the craft's low value — no more default-0.6 wall grabs. Runtime-created (no asset/GUID dependency); values are conservative first guesses.

### Force targets → derived values (NOT copied from the 500 kN test)

Weight = 7,500 × 9.81 = **73,575 N**.

| System | Target | Derived | Result |
|---|---|---|---|
| Hover (×4) | equilibrium 0.30–0.50 | **45,000 N** continuous/node (180 kN total) | equilibrium **0.409**; overdrive ×3 → 135 kN/node emergency (540 kN, 72 m/s² curvature/landing reserve) |
| Main | 2.0–3.0 g | **180,000 N** | 2.45 g; overdrive ×1.5 → 270 kN burst (3.67 g) |
| Brake | meaningful share of 2.0–3.5 g total | **120,000 N** | 1.63 g mechanical (67% of main — no longer 8%); traction + drag supply the rest |
| Strafe (×4) | 0.8–1.5 g usable one-direction | **60,000 N**/node | usable 2×60×0.75 gain = 90 kN = **1.22 g**; installed 240 kN |
| Roof (×4) | inverted support + stabilization, deliberately not hover-sized | **40,000 N**/node, overdrive ×1.5 | 160 kN = 2.2× weight inverted support |
| Energy | budget that bites under combined load | **450** (cost 0.001/N) | hover equilibrium 73.6 + main 180 + steering fits; burst/full-load overdraws → priority matters |
| Aero | plausible for 4.5 m² frontal / 20.8 m² plan | CdA 2.5, wing Cl·A 8, GE Cl·A 8, clamp 4 g | drag-limited ~1,510 km/h; ~0.46 g downforce at 300 km/h |

## Systems added / changed (code)

**New:** `Config/CraftPhysicalDefinition.cs` (authoritative dims/COM/ballast/inertia, runtime mass-property apply, gizmos) · `Editor/CraftPhysicalDefinitionEditor.cs` (Validate/Report + **Apply Physical Definition To Craft**: Undo, change log, node layout, compound colliders, hover height, probe ranges, visual scale, material assignment; never runs at runtime) · `Config/HoverPhysicsMaterials.cs` · `TrackGeneration/Macro/TrackSurfacePhysics.cs`.

**Force semantics (Phase 6)** — every misleading field renamed with `FormerlySerializedAs` (Unity data survives) + JSON key migration on config import (old configs still apply):

| Old (lied) | New (honest) | Behavior |
|---|---|---|
| `ThrusterNode.maxForce` | unchanged — now documented as **continuous** rating | hard limit for normal ops |
| — | `ThrusterNode.overdriveForceMultiplier` (NEW) | real force ceiling above continuous, reachable ONLY by overcharge bursts and hover/stabilizer emergency demand; billed by the power system; drive/steering can never overdrive |
| `minBurstThrottle 10 / maxBurstThrottle 50` | `min/maxBurstCommand01` (0–1 of overdrive ceiling) | charge now scales real burst force again |
| hardcoded 0.65 burst taper | `burstEndTaper01` field | exposed |
| `forceMultiplier` / `masterThrottleMultiplier` | `commandGain` / `masterCommandGain` | tooltips state they shape input and never raise peak force |
| `hoverMaxThrottle` / `roofStabilizerMaxThrottle` | `hoverMaxCommand` / `roofStabilizerMaxCommand` | documented: 1 = continuous; >1 = overdrive demand (now real, clamped at the hardware overdrive rating) |
| `extraGravity` | `downwardAssistMs2` | documented non-physical, always-on, world-down, mass-independent; baseline 0 |
| `DriveCore.throttleRampSpeed` | `throttleRiseRate` + new `throttleFallRate` | separate rise/fall (fall 4 shortens W→S overlap) |

**Thruster response (Phase 10):** per-node `throttleRiseRate`/`throttleFallRate` on ThrusterNode (0 = instantaneous = legacy). Baseline: strafes 8/s, roof 10/s, main/brake ramped by DriveCore as before, hover left to the controller's own smoothing.

**Hover architecture (Phase 7):** all hardcoded `4f` load-share math now derives from `HoverNodes.Count` (numerically identical for 4 nodes; 5–6 node craft no longer mis-scale). New `HoverCornerDebug[4] CornerDebug` exposes per-corner layer contributions (gravity / curvature / spring / damping / oscillation / attitude / pre-clamp / final) each tick.

**Energy priority (Phase 11, Option B):** baseline config enables `protectBaseHover` + `protectStabilizer` with priority ON (minimum safe hover first — the preferred order's top entries); `EnergyCore.OnValidate` warns when priority is enabled with no protected channels. Existing power diagnostics (per-role request/granted/saturation) already exist in `EnergyState` + HUD; the config diagnostics now also report hover-protected power headroom.

**Diagnostics (Phase 12)** — `CraftConfigDiagnostics` rewritten: roof force counted from child nodes (edit-mode correct, fixes `roofTotalForceN = 0`); strafe reported as installed / usable-left / usable-right / yaw couple (N·m); all weight ratios name their basis (`ToNormalWeight` vs `ToEffectiveWeight`); acceleration estimates split hardware-only vs power-limited; braking estimate labeled brake-thruster-only; dimensions reported craft-LOCAL (rotation-independent); automatic COM/inertia reported as "automatic (derived)"; new warnings: brake <50% of main, downward assist active, auto-COM without ballast control, single box on a large craft, no physics material, priority with no protected channels, inertia-independent torque info, runtime-discovered nodes missing from the serialized bus.

## Stabilizer ForceMode decision report (Phase 4.3 — no change made)

Surface-alignment (`HoverStabilizerArray`) and yaw-damping (`VectorThrusterArray`) torques use `ForceMode.Acceleration`:

- **Current (Acceleration):** rotation response identical for every mass/size/inertia — consistent handling across classes, tuned numbers stay valid. Chosen for the baseline.
- **If switched to Force:** response would divide by the (now much larger) automatic inertia of the 6.5 m compound body — all gains would need ~10–50× retuning, and future craft classes would rotate differently per chassis. Physically purer; defer to a deliberate V3 decision.
- Thruster-placement torques (AddForceAtPosition) already scale physically with mass/inertia in both cases.

## Intentionally deferred (unchanged from plan restrictions)

Full V3 aero (directional CdA, center of pressure, lift/AoA/stall, wind, slipstream), damage, heat/duty-cycle, battery storage, modular parts mass, dynamic COM, extra hover nodes, automatic runtime resizing, artificial speed caps. Extension points documented: aero values are all in `CraftAerodynamics` (component-level, config-exported); thruster ratings ready for a thermal layer (`overdriveForceMultiplier` is the peak-rating hook); `CraftPhysicalDefinition` is where per-axis inertia and ballast grow into parts-based mass.

## Known limitations

- Hover PD gains (`hoverKp 2.5`, `hoverKd 0.3`) and aero coefficients are **derived first guesses** — the test protocol exists to correct them. Expect a softer suspension than the 10-ton test state (that state ran 25× stronger hover hardware).
- Overdrive has no duty cycle yet — "emergency" force is not time-limited (heat is V3's job).
- Legacy scene overrides with old field names (`extraGravity 75`, `thrusters[4].forceMultiplier 1.5`, `roofStabilizerMaxThrottle 10`) may not migrate through prefab-instance override paths — the checklist reverts them deliberately.
- SceneBootstrapper's programmatic test craft still builds the legacy 1-ton layout (test scene only).

## YOUR IN-EDITOR CHECKLIST (one-time, ~5 minutes)

1. Let Unity recompile. Expect no errors; renamed fields keep their values via FormerlySerializedAs.
2. **Save/discard decision (Phase 0):** your 10-ton live tuning is preserved in `CraftConfigs/Hovercraft_V2_10T_HighForce_Test.json`. Do NOT save the scene/prefab if it still holds that unsaved tuning you don't want — or import that config later to get it back at any time.
3. Open **HovercraftRootV2 prefab** (prefab mode). Add **Craft Physical Definition** (defaults are the medium baseline). 
4. Click **Apply Physical Definition To Craft** → review the printed change log (nodes, colliders, visual scale, hover height). Undo works.
5. On **Craft Config Manager** click **Import…** → select `CraftConfigs/Hovercraft_V2_Medium_7_5T_Baseline.json`. Review the change log (every value old → new). Save the prefab.
6. In **SampleScene**: select the craft instance → Overrides dropdown → **Revert All** (kills the stale `extraGravity 75` / `forceMultiplier 1.5` / `roofStabilizerMaxThrottle 10` overrides). Save the scene.
7. Optional but recommended: add **CraftDebugHUD** to the scene (F2 panel) and a **WorldAtmosphere** object.
8. Export the result (**Export…** → `Hovercraft_V2_Medium_7_5T_Baseline_verified.json`) and run the test protocol.
