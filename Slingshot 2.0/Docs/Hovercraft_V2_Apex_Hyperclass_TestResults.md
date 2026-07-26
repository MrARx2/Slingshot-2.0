# Hovercraft V2 — Apex Hyperclass 11T Prototype: Test Results

Status: **prepared — live runs pending.** The controlled matrix below requires
play-mode driving with the Test Drive Recorder; analytic expectations are
filled in so every run has a pass criterion to compare against. Fill the
"Measured" columns from the exported session bundles.

Setup for every test: `HovercraftRootV2_ApexHyperclass` instance, config
`Hovercraft_V2_Apex_Hyperclass_11T_Prototype.json` applied after
Apply Physical Definition To Craft, WorldAtmosphere = Earth Standard (neutral),
recorder mode FullDiagnostic. Verify the recorder logs
`Operating-band scheme: ApexHyperclass (Continuous/Performance/Extreme/Emergency/Saturated)`.

## Test A — Static Hover (20 s)

| Check | Expected | Measured |
|---|---|---|
| Equilibrium avg applied command | ≈ 0.169 (640 kN continuous vs 107.9 kN weight; spec's 0.115 doesn't match its own hardware numbers) | |
| Clearance | 2.8 m ± deadband, stable | |
| Operating band | 100% Continuous | |
| Power limitation | none (hoverPower01 = 1.0 throughout) | |
| Oscillation growth | none (bob amplitude constant) | |

## Test B — Flat Acceleration

No-drag analytic times (720 kN peak / 11 t = 65.5 m/s²): 0–500 ≈ 2.1 s,
0–1000 ≈ 4.2 s. Spec's ~3.2/~6.4 s assume continuous 480 kN. Real times land
between continuous-with-drag and peak-with-drag; target 0–1000 ≈ 7–9 s.

| Run | Expected | Measured time | Peak drag N | Max speed | Force clamps |
|---|---|---|---|---|---|
| 0–500 km/h | 3–4 s | | | | expect none |
| 0–1000 km/h | 7–9 s | | | | |
| 0–1500 km/h | reachable on a long straight | | | | |
| Max-speed run | >1,500 km/h; credible route to 1,900–2,000+ (drag-limited theory ≈ 2,600) | | | | |

Also record: power use (must stay < 4,500), hover command (Continuous at
cruise), CoP torque (aero_frames.csv totalTorqueNm — should center near zero
nose-first).

## Test C — Straight Braking (380 kN ≈ 3.5 g + aero drag)

| From | Expected stop time (brake only) | Expected distance | Measured t / d | Peak decel | Pitch / clearance notes |
|---|---|---|---|---|---|
| 500 km/h | ≈ 4.0 s | ≈ 280 m | | | nose-down moment is physical — do not cancel |
| 1000 km/h | ≈ 8.0 s | ≈ 1,120 m | | | |
| 1500 km/h | ≈ 12.1 s | ≈ 2,520 m | | | aero drag shortens these materially at speed |

## Test D — Steering Sweep (controlled pulses at 500 / 1000 / 1500 km/h)

Record per speed: yaw acceleration per unit input, heading response, side-slip
peak, broadside drag loss (AeroBroadsidePenalty events), roll, safety limiting
(tilt/roll-rate safety01 in core stream).

Pass: sharper than Medium (yaw authority 2.8 vs 2.25, damping 3.2 vs 4), no
speed-driven collapse through 1,500 km/h, no automatic rotation snapping,
ExcessiveSideSlip events only when deliberately provoked.

## Test E — Clean Jump (no manual roof/bottom input)

| Check | Expected | Measured |
|---|---|---|
| autoHoverCommand while airborne | decays to < 0.3 within ~0.1 s (decay speed 40) | |
| AirborneAutomaticHoverCarryover events | zero | |
| GroundEffectWithoutLiveSurface events | zero | |
| GE force after leaving surface | fades with grounded factor (< ~0.1 s) | |
| Top/bottom drag airborne | visible in aero stream (top 5.5 / bottom 6.0 CdA — stronger than Medium) | |
| CoP behavior | nose settles toward airflow, no kite-float | |
| Landing | landing-catch damps; hard landings have consequences (no 60–100 m/s freebies) | |

## Test F — Curvature Sweep (radii 600 / 900 / 1200 / 1600 m)

Predicted curvature load = m·v²/r (+ gravity component + aero downforce).
Examples at 11 t: 600 m @ 900 km/h ≈ 1,146 kN (Extreme); 1200 m @ 1300 km/h
≈ 1,195 kN (Extreme); 1600 m @ 1600 km/h ≈ 1,358 kN (Emergency edge);
600 m @ 1500 km/h ≈ 3,182 kN (≈ envelope — expect PhysicallyIncompatibleSection
at higher speed).

| Radius | Speeds tested | Predicted load | Measured applied force | Band observed | Clearance / bottoming | Compatibility class |
|---|---|---|---|---|---|---|
| 600 m | | | | | | |
| 900 m | | | | | | |
| 1200 m | | | | | | |
| 1600 m | | | | | | |

Pass: predicted vs applied within tolerance, band transitions match the
1/2/3.5/5 command edges, section_compatibility.csv classes track the physics.

## Test G — Full Route

Only after A–F pass. Acceptance targets (Phase 15): Continuous dominates flat
running; Performance normal on demanding sections; Extreme on severe elements;
Emergency short (EmergencyIntervalProlonged rare); Saturation rare and
meaningful; no prolonged chassis contact on compatible sections; sideways
flight loses speed much faster than nose-first; no power starvation
(< 4,500 peak); manual air control available; no unexplained top-speed
collapse.

## Recorder outputs to attach per run

Session bundle folder name, `session_summary.md` band section,
`hover_operating_bands.csv`, `aero_frames.csv`, `events.csv` (filter the nine
Apex event types), `section_compatibility.csv`.
