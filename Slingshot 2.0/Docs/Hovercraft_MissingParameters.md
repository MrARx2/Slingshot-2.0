# Hovercraft V2 — Missing Parameters Report

Important physical values that are absent or only represented indirectly, why they matter, and when they're needed. None of these were silently added — the config system exports the craft exactly as it exists.

## Needed for V2 (recommended soon)

| Parameter | Why | Where it would live |
|---|---|---|
| **Explicit center of mass / ballast offset** | Weight distribution is a stated design goal; currently COM is implicit from one box collider, so it can't be tuned at all | Rigidbody `automaticCenterOfMass=false` + `centerOfMass` (already supported by the config file's rigidbody section) |
| **Physics materials (craft + track)** | Project has zero physics material assets — wall scrapes use default 0.6 friction, which will grab hard at racing speed | `.physicMaterial` assets on craft collider + track meshes |
| **Carve-lean drift floor** (hardcoded 0.3) | Only member of the drift profile you can't tune | `TractionCore` serialized field |
| **Overcharge burst envelope** (hardcoded 0.65 end-taper) | Burst feel knob; natural AnimationCurve candidate | `OverchargeCore` field |
| **Inverted threshold** (−0.35) and **isGrounded cutoff** (0.5) | Gate recovery override and many grounded behaviors | `TelemetryMainframe` fields |
| **Hover corner load-share from node count** (hardcoded `4f`) | Adding a 5th/6th hover node today silently mis-scales PD, curvature and anti-launch math — directly blocks the "add two more hover thrusters" idea | `HoverStabilizerArray` — derive from `HoverNodes.Count` |
| **De-duplicate `TractionState.Normal`** | Fallback values already drifted from tuned values (carveBite 1 vs 5) | Build Normal from the TractionCore instance |
| **Roof node labels** | Duplicate "Hover_*" labels are a lookup hazard | Prefab data fix |
| **Per-node thruster response time** | Only DriveCore has a ramp (3/s); strafe/attitude are instantaneous — limits feel tuning and V3 spool-up | `ThrusterNode` field (default = instant to preserve behavior) |

## Useful later (V3 aero/handling depth)

| Parameter | Why |
|---|---|
| Directional drag (frontal/side/vertical CdA) | Current single CdA is isotropic; sideways airflow should brake differently |
| Separate reference areas + coefficients (instead of Cd·A products) | Lets craft dimensions drive aero automatically |
| Center of pressure + aerodynamic torque | High-speed pitch/yaw stability, spin recovery realism |
| Lift model / angle-of-attack | Nose-up lift, jumps that respond to attitude |
| Wind / moving air volumes | Track hazards, slipstream |
| Explicit craft dimension fields (length/width/height) | Currently computed from colliders; V3 crafts should declare them |
| Per-axis inertia scaling | Handling feel decoupled from collider shape |
| Heat / energy-heat limits | Spec placeholder — no heat system exists |
| Max safe landing speed (structural) | Damage model hook; hard-landing *damping* fields exist already |

## Already covered (no action)

- Equilibrium hover force, hover reserve, force-to-weight ratios → computed in the diagnostics section of every export.
- Hover probe lost-ground behavior → verified safe (explicit reset on miss; smoothed decay only, ~100 ms).
- Grounded state timeout → `groundedFactorRiseSpeed`/`FallSpeed` exist.
- Speed limits → intentionally none (emergent from drag); the config schema documents this as a deliberate absence, not a gap.
