# Hovercraft V2 — Apex Hyperclass 11T Prototype: Implementation Report

New top-tier craft class built alongside (never replacing) the Medium Rev4
reference vehicle. Companion document: `Hovercraft_V2_Apex_Hyperclass_TestResults.md`.

## 1. Files changed / created

| File | Change |
|---|---|
| `Assets/Prefabs/HovercraftRootV2_ApexHyperclass.prefab` (+ .meta, new GUID) | NEW — full Apex craft prefab, independent of `HovercraftRootV2.prefab` |
| `CraftConfigs/Hovercraft_V2_Apex_Hyperclass_11T_Prototype.json` | NEW — authoritative Apex Rev0 config |
| `CraftConfigs/Hovercraft_V2_Medium_7_5T_Rev4_Test.json` | UNTOUCHED (Phase 0 requirement) |
| `Assets/Scripts/Hovercraft_Setup/Diagnostics/TestDriveRecorder.cs` | Class-aware operating-band scheme, Apex events, airborne command-source recording, band metadata |
| `Assets/Scripts/Hovercraft_Setup/Diagnostics/TestDriveRecorderData.cs` | Apex thresholds; CoreFrame/HoverNodeFrame command-source fields; SectionAggregate Performance/Extreme time |
| `Assets/Scripts/Hovercraft_Setup/Diagnostics/TestDriveRecorderEvents.cs` | 9 new event types (appended — serialized ints stable) |
| `Assets/Scripts/Hovercraft_Setup/Diagnostics/TestDriveRecorderExporter.cs` | Dynamic band names in CSV/markdown/compact JSON; command-source columns; speed-by-band |

No Medium prefab, config, or tuning value was modified. Both craft remain
independently selectable (each prefab has its own CraftConfigManager identity).

## 2. Prefab / object

`HovercraftRootV2_ApexHyperclass` — byte-level copy of the Medium prefab with a
fresh asset GUID, then re-authored in place. Root renamed, transform zeroed.
Known prefab-copy fix included: the copied roof thruster nodes carried wrong
labels (`Hover_XX`); the Apex prefab labels them correctly `Roof_FL/FR/RL/RR`.
`downwardAssistMs2` is 0 (the stale Medium prefab still carried 75 — Apex is
fully physical, per the no-fake-gravity restriction).

## 3. Physical definition

| Property | Value |
|---|---|
| Craft class | ApexHyperclass |
| Mass | 11,000 kg |
| Length × width × height | 7.8 × 3.6 × 1.55 m |
| Target clearance | 2.8 m |
| Hover footprint | 6.0 × 3.0 m |
| COM (manual) | (0, −0.50, 0) |
| Inertia | automatic (from compound colliders) — deliberately untuned in this pass |
| Rigidbody | damping 0 / 2.0, maxAngVel 50 rad/s, Interpolate, ContinuousDynamic |

## 4. Collider setup (applier-formula values for the 7.8×3.6×1.55 body)

| Collider | Center | Size |
|---|---|---|
| Chassis (root) | (0, 0, 0) | (3.312, 1.55, 4.29) |
| Collider_Nose | (0, −0.186, 2.808) | (2.232, 1.023, 2.34) |
| Collider_Tail | (0, −0.0775, −2.808) | (2.808, 1.209, 2.34) |

Running **Apply Physical Definition To Craft** reproduces exactly this layout
(the prefab was authored with the applier's formulas). VisualModel scale
1.995 (Medium 1.663 × 7.8/6.5).

## 5. Node positions

| Node | Position | Notes |
|---|---|---|
| Hover FL/FR/RL/RR | (±1.5, −0.725, ±3.0) | applier formula (bottom + 0.05) |
| Roof FL/FR/RL/RR | (±1.5, −0.665, ±3.0) | hover Y + 0.06 |
| Main_Rear | (0, 0, −3.9) | thrust axis unchanged from Medium (verified rotation) |
| Brake_Front | (0, 0, +3.9) | keeps its physical nose-down pitch moment |
| Strafe ×4 | (±1.8, 0, ±3.0) | |

Spec suggested hover Y −0.75 / roof −0.68; the prefab uses the applier's
derived −0.725 / −0.665 so a definition re-apply never moves hardware.
Ground-detection range authored 7 m per spec (a definition re-apply computes
clearance+3.5 = 6.3 m; importing the config afterwards restores 7 — noted in
the config metadata).

## 6. Force values

| System | Value | Acceleration @ 11 t |
|---|---|---|
| Hover continuous | 160 kN/node → 640 kN | 5.9 g |
| Hover high-load (cmd 2) | 1,280 kN | 11.9 g |
| Hover emergency (×5, cmd 5) | 800 kN/node → 3,200 kN | 29.7 g (hardware envelope) |
| Main continuous | 480 kN (overdrive ×1.5 → 720 kN) | 4.45 g / 6.67 g |
| Brake | 380 kN | 3.5 g |
| Strafe | 160 kN ×4 (320 kN usable one-direction) | 2.97 g |
| Roof | 40 kN ×4 (×1.5) | matches the spec's 160-power roof budget |

Static hover equilibrium: 107.9 kN weight / 640 kN = **0.169 command**
(comfortably Continuous). Response: drive rise/fall 4.5/5.0, strafe rise/fall 12,
hover response 24 / fall 30 / airborne decay 40.

## 7. Power values (EnergyCore)

Total 4,500 @ 0.001/N — calibration budget. Peak demands: emergency hover
~3,200 + main ~480 + one-direction strafe ~320 + roof ~160 = 4,160 < 4,500, so
no hidden starvation during ordinary extreme operation. protectBaseHover,
protectStabilizer, priority distribution on; vectoring bias 1.5. Gameplay power
competition intentionally deferred.

## 8. Aero values (DirectionalBodyV3, zero surfaces)

Forward/reverse/side/top/bottom CdA = 2.2 / 3.8 / 8.0 / 5.5 / 6.0;
CoP (0, +0.15, −1.0) — strong nose-first alignment, physical only;
pitch/yaw/roll stability 0 (CoP does the work until tested);
body lift disabled; min airflow 2 m/s; force clamp 14 g; torque clamp off.
Legacy bridge on: wing Cl·A 2.0, GE Cl·A 2.0, max downforce 3 g.
WorldAtmosphere stays Earth Standard / neutral multipliers / turbulence off.

Drag-limited top speed (½ρv²·CdA = 720 kN): v = √(2·720000/(1.225·2.2)) ≈
731 m/s ≈ **2,630 km/h** theoretical — the >2,000 km/h envelope exists; real
tracks will sit far below it (wing/GE drag, curvature load, steering losses).

## 9. Operating-band implementation (Phase 2/13)

The recorder now selects a band scheme at StartRecording from
`CraftPhysicalDefinition.craftClass` (or hoverMaxCommand ≥ 4.5):

| Scheme | Bands (avg applied node command) |
|---|---|
| Medium (unchanged) | Continuous ≤1, HighLoad 1–2, Emergency >2, Saturated |
| **ApexHyperclass** | Continuous ≤1, **Performance 1–2, Extreme 2–3.5, Emergency 3.5–5**, Saturated |

Commands above 1 are no longer blanket-labeled emergency on Apex. Band names
flow through `hover_operating_bands.csv`, `section_load_budget.csv`
(tPerf/tExtreme columns added), the compact JSON (`operatingBandSummary` with
time / longest interval / avg speed per band) and the markdown summary.
Session metadata carries the full scheme + hoverMaxCommand.

## 10. Recorder changes (Phase 13)

**New events:** PerformanceHoverInterval, ExtremeHoverInterval,
ExtremeIntervalProlonged (>4 s), EmergencyIntervalProlonged (>1.5 s),
AeroBroadsidePenalty (side-slip >20° above 300 km/h), ExcessiveSideSlip
(>35° above 400 km/h), AirborneAutomaticHoverCarryover (auto hover channels
>0.3 summed while fully airborne), GroundEffectWithoutLiveSurface
(legacy/body-mode GE force with zero grounded factor — validation),
PhysicallyIncompatibleSection (section's required load first exceeds the
emergency envelope). All thresholds are inspector-configurable.

**Airborne command sources (Phase 10):** per core frame —
`autoHoverCommand` (baseHover+stabilizer channels), `manualBottomCommand`,
`manualRoofCommand`, `overchargeHoverCommand`; per hover-node row —
autoHoverCmd / manualBottomCmd / overchargeCmd. `finalAppliedCommand` =
existing `hoverTotalAppliedThrottle`. Landing-catch output is part of the
automatic hover channels (HoverStabilizerArray does not expose it separately);
air control is torque-based, not a thruster command — both documented
limitations, not silent gaps.

**Summary additions:** time per band, longest interval per band, average speed
per band, emergency/saturated share; existing sections cover clearance by
speed band, propulsion/brake, steering response, side-slip, aero loads, CoP
torque (aero stream), section compatibility and braking-required sections.

## 11. Airborne carryover status (Phase 10 gate)

- Legacy ground effect scales with the smoothed grounded factor
  (fall speed 10 → zero within ~0.1 s of losing the cushion) and V3 GE
  surfaces are zero-force without a live probe hit by construction —
  "immediately or very short controlled fade" is met.
- Automatic hover throttle decays per node at `airborneHoverThrottleDecaySpeed`
  (Apex 40 → ~25 ms time constant) whenever the node loses the surface or the
  craft leaves the hover cushion (verified in HoverStabilizerArray.SmoothHoverThrottle).
- Manual bottom-thruster input is an independent bus channel and recorded
  separately.
- The two validation events above will flag any regression in a real run.
- No fake gravity anywhere: `downwardAssistMs2 = 0`.

## 12. Controlled test results

**Live runs pending** — they require play-mode driving. The full Test A–G
matrix, expected values and pass criteria are prepared in
`Hovercraft_V2_Apex_Hyperclass_TestResults.md`. Analytic predictions
(equilibrium 0.169 command, 0–1000 km/h ≈ 6.4 s no-drag / 7–9 s expected real,
drag-limited ~2,600 km/h theoretical, brake 3.5 g) are recorded there for
comparison against the recorder output.

## 13. Known remaining issues

1. Live test matrix not yet driven (section 12).
2. Static equilibrium ≈ 0.169 command, not the spec's illustrative 0.115 —
   the spec's own hardware numbers (640 kN continuous vs 107.9 kN weight)
   give 0.169; Test A should verify against 0.169.
3. `groundDetectionRange` 7 m (spec) vs applier formula 6.3 m — config wins
   when applied after the definition; harmless either way.
4. Inertia tensor automatic per spec — expect the first steering sweep to
   inform a manual-inertia pass later.
5. Roof stabilizer max command kept at Medium's 0.6 (spec silent) — flagged
   for tuning.
6. OverchargeCore left at inherited values (spec silent).

## 14. Values intentionally left for later tuning

Hover Kp/Kd beyond the 2.5/0.3 first pass (recorder decides), pitch/yaw/roll
aero stability torque (CoP first), manual inertia tensor, power competition
below 4,500, aero surface migration (SurfaceBasedV3 explicitly out of scope),
final grip/carve feel, overcharge behavior, roof stabilizer authority.

## 15. Restrictions compliance

No speed caps, no velocity writes, no transform rotation, no kinematic
stabilizers, no hidden adhesion, no fake gravity, no AeroSurface components,
no SurfaceBasedV3, no lift/stall, safety systems intact (tilt/roll-rate safety
untouched), force clamps not raised to hide coefficients (14 g per spec),
Medium Rev4 untouched, no automatic track alteration or slowdown.
