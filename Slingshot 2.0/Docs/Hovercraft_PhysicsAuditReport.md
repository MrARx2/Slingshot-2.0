# Hovercraft V2 — Physics Audit Report

Generated 2026-07-23. Covers every variable that affects craft movement, where it lives, its current value, and whether it is force-based or kinematic. Sources: full code sweep of `Assets/Scripts/Hovercraft_Setup/` + `Assets/Scripts/World/`, prefab/scene YAML extraction, project settings.

## Answers to the spec's direct questions

- **Hover force is PER NODE.** Current prefab: 4 hover nodes × **40,000 N each = 160,000 N total** (the "25,000" baseline in the task doc is outdated — the prefab has since been retuned).
- All craft propulsion/control is **force-based** (`ForceMode.Force` for thrusters and aero, `ForceMode.Acceleration` for traction/torques/extraGravity). **Zero kinematic manipulation** exists in the craft runtime (see Kinematic Behavior Report).

## 1. Rigidbody & physical body (prefab `HovercraftRootV2`)

| Property | Value | Note |
|---|---:|---|
| Mass | 1000 kg | |
| Linear damping | 0 | drag comes from CraftAerodynamics only |
| Angular damping | 2 | |
| Center of mass | automatic (implicit) | from the single box collider → ~(0,0,0) |
| Inertia tensor | automatic (implicit) | |
| Constraints | none | |
| Interpolation | Interpolate | |
| Collision detection | Continuous Dynamic | |
| Max angular velocity | project default 50 rad/s | not overridden |

**Collider:** one BoxCollider on root, size **2 × 0.5 × 3 m**, center (0,0,0), **no PhysicsMaterial** (project contains zero physics material assets → Unity defaults: friction 0.6/0.6, bounce 0, Average combine).

**Project settings:** gravity (0, −9.81, 0); fixed timestep **0.01 s (100 Hz physics)**; solver 6/1 iterations; default max angular speed 50 rad/s.

## 2. Craft dimensions (computed)

- Collider body: 2 m wide × 0.5 m tall × 3 m long; bottom face at y −0.25.
- Hover footprint: **1.6 m (X) × 2.2 m (Z)**, nodes at (±0.8, −0.2, ±1.1).
- Roof nodes at (±0.8, −0.139, ±1.1); Main_Rear (0,0,−1.5); Brake_Front (0,0,1.5); strafes (±1.0, 0, ±1.1).
- Target ride height (`hoverHeight`): 3 m → static clearance ≈ 2.95 m under collider bottom.
- Visual model: "Meshy" mesh child (scale 189.27); legacy Body/Nose/Engine/Fin cubes are inactive.

## 3. Gravity & downward forces (classified)

| Effect | Where | Type | Current value |
|---|---|---|---:|
| Project gravity | Physics settings | real gravity | 9.81 m/s² |
| extraGravity | `CraftCore.cs` (`Vector3.down`, ForceMode.Acceleration) | **non-physical helper** | prefab 0, **scene override 75** |
| Wing downforce | `CraftAerodynamics` (−craft.up, ForceMode.Force) | surface-independent aero force | Cl·A 2 m² |
| Ground effect | `CraftAerodynamics` (−groundNormal, fades with groundedFactor) | surface-relative aero force | Cl·A 2 m² |
| Downforce clamp | `CraftAerodynamics.maxDownforceG` | force clamp (0 = off) | 6 g |
| Vertical velocity edits | — | **none exist** | |

⚠ The scene's `extraGravity 75` contradicts the stated "no fake multipliers" direction — effective weight becomes 84,810 N and hover equilibrium throttle ≈ 0.53. Recommend setting it to 0 and letting downforce plant the craft.

## 4. Force application inventory (every Rigidbody write)

| Source | Call | ForceMode | Mass-scaled |
|---|---|---|---|
| ThrusterNode.ApplyThrust (all 14 nodes) | AddForceAtPosition | **Force** (newtons) | yes |
| CraftAerodynamics drag / downforce | AddForce | **Force** | yes |
| CraftCore extraGravity | AddForce | Acceleration | no (by design, gravity-like) |
| TractionCore grip (clamped to maxGripAcceleration 90 m/s²) | AddForce | Acceleration | no |
| HoverStabilizerArray surface-align torque | AddTorque | Acceleration | no (inertia-independent) |
| VectorThrusterArray yaw damping | AddTorque | Acceleration | no |

No velocity/angularVelocity assignments, no MovePosition/MoveRotation, no COM/tensor writes anywhere in the craft runtime.

## 5. Thruster hardware (prefab values)

| Node(s) | Role | maxForce (N) | Detection range |
|---|---|---:|---:|
| Hover_FL/FR/RL/RR | Hover | 40,000 each | 6 m |
| Roof_FL/FR/RL/RR | Roof | 40,000 each | 6 m |
| Main_Rear | Main | 100,000 | 4 m |
| Brake_Front | Brake | 40,000 | 4 m |
| Strafe ×4 | Strafe | 30,000 each | 4 m |

All: efficiency 1, powerDrawMultiplier 1, groundLayers Everything. Max Force is a **hard limit** (throttle clamps at 1 in `ThrusterNode.ApplyThrust` and in EnergyCore power accounting).

## 6. Current tuning highlights (prefab + scene overrides)

Scene (`SampleScene.unity`) overrides on the craft instance: `extraGravity 75`, `Main_Rear forceMultiplier 1.5`, `hoverHeight 3`, `stiffenFullSpeed 400`, `surfaceLookAheadBlend 1`, `roofStabilizerMaxThrottle 10`, `hoverThrusterResponseSpeed 20`, `maxSurfaceLookAheadDistance 50`.

Full per-component values are captured by the **config export** (see Config System). Key physics numbers as of this audit: EnergyCore `totalPowerOutput 1500`, `powerCostPerForce 0.001`, `priorityPowerDistribution ON` but **both protect flags OFF**; HoverStabilizerArray `hoverKp 1`, `hoverKd 0.28`, `hoverMaxThrottle 2.25`; TractionCore grips 7/6/0.25 (drift 0.35/0.25/0.05), `maxGripAcceleration 90`; OverchargeCore `minBurstThrottle 10`, `maxBurstThrottle 50`; CraftAerodynamics CdA 1.5, wing Cl·A 2, ground-effect Cl·A 2, clamp 6 g.

## 7. Discrepancies & latent issues found

1. **Main_Rear `forceMultiplier 1.5` (scene) does NOT give 150 kN.** Throttle hard-clamps at 1, so peak force is still 100,000 N — the multiplier only reaches saturation earlier. Remove it or raise `maxForce` instead.
2. **Overcharge burst throttles 10/50 clamp to 1.0.** Charge level currently changes only burst *duration* (0.2–0.5 s), not strength. Same for `roofStabilizerMaxThrottle 10` (acts as 1.0).
3. **`priorityPowerDistribution` is ON but both `protectBaseHover`/`protectStabilizer` are OFF** → nothing is actually prioritized; power shares proportionally under overload.
4. **Roof node labels duplicate hover labels** (e.g. Roof_FR has label "Hover_FR"). `FindByLabel` currently resolves to the hover node only because of hierarchy order — rename the roof labels.
5. **Roof nodes are not in the serialized bus list** — added at runtime by autoDiscover (works, but prefab-serialized entries would let you set per-entry multipliers).
6. **No WorldAtmosphere in the scene** — aero uses Earth defaults (ρ 1.225, multipliers 1). Add one to tune air density per track.
7. **`CraftDebugHUD` is not in the scene** — the F2 per-thruster panel is unavailable until you add it.
8. **`SectionAdaptiveSuspension` script exists but is not on the craft.**
9. **`TractionState.Normal` fallback (HovercraftData.cs) duplicates TractionCore defaults and has already drifted** (fallback carveBite 1 vs tuned 5).
10. **Throttle-headroom systems still saturate at 1.0** (hover ceiling 2.25, curvature feed-forward, hard-landing catch, steering authority 2.25 → saturates at ~52% deflection at prefab sensitivity). Known tuning debt from the Max-Force-hard-limit change.

## 8. Hardcoded values worth exposing (config-worthy)

| Location | Value | What it does |
|---|---|---|
| TelemetryMainframe.cs | −0.35 | uprightDot "inverted" threshold (~110°) |
| TelemetryMainframe.cs | 0.5 | groundedFactor → isGrounded cutoff |
| TractionCore.cs | Lerp(1, **0.3**) | carve-lean floor at full drift (only unexposed drift-profile member) |
| OverchargeCore.cs | Lerp(1, **0.65**) | burst envelope end-taper |
| HoverStabilizerArray.cs | ×2 | hidden faster lean-return while disarmed |
| HoverStabilizerArray.cs | **4f** ×4 sites | hardcoded 4-corner load share — breaks silently for ≠4 hover nodes |
| HoverStabilizerArray.cs | 25 rad/s clamp; 0.5 probe weights | curvature spike clamp; look-ahead blend weights |
| CraftCore.cs | +0.05 / 2.05 | spawn ride-height pad / fallback |
| SceneBootstrapper.cs | mass 1000, all maxForce values, frictions | editor-bootstrap hardware spec, duplicated from prefab |

Full per-field tables for every component (60+ stabilizer fields, traction, drive, vectoring, input, telemetry, overcharge, attitude) are reproduced in the **exported config file itself** — every serialized field appears there with its live value, which supersedes maintaining a separate table here.
