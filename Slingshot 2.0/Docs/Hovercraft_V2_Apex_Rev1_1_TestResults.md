# Apex Hyperclass Rev1.1 — Test Results

Status: **prepared — live runs pending.** Setup for every test:
`HovercraftRootV2_ApexHyperclass` + `Hovercraft_V2_Apex_Hyperclass_11T_Rev1_1.json`
(applied after Apply Physical Definition To Craft), track regenerated so
colliders carry the TrackSurface layer, recorder FullDiagnostic. Confirm at
start: band scheme logs ApexHyperclass; hover probes report mode SphereCast;
probe masks are TrackSurface-only (a craft that suddenly cannot hover means the
layer/mask setup is wrong — check TagManager layer 8 and regenerate the track).

## Test A — Static Hover (20 s)

| Check | Expected | Measured |
|---|---|---|
| Clearance | 2.8 m stable | |
| Band | 100% Continuous | |
| Probe loss | none (probe masks working) | |
| Oscillation growth | none | |
| Carryover events | none | |
| Invalid accel spikes | none in ValidFreeDriving/GroundedContact classes | |

## Test B — Section 1 Speed Sweep (controlled entry: 800 / 1,000 / 1,200 / 1,400 km/h)

Record per run: requested vs applied curvature acceleration, clamp events,
reserve, probe validity, clearance, raw vs applied command, band, contact,
bottoming (all in `hover_operating_bands.csv` + events).

| Entry speed | CurvatureFeedForwardClamped | Max raw cmd | Probe losses | Bottoming | Notes |
|---|---|---|---|---|---|
| 800 km/h | | | | | |
| 1,000 km/h | | | | | |
| 1,200 km/h | | | | | |
| 1,400 km/h | | | | | |

Pass: late raw-demand spikes meaningfully reduced vs baseline; clamp events
now report exactly where 220 m/s² is still insufficient (geometry data for
Phase 7, not an automatic fix).

## Test C — Clean Jump (no manual roof/bottom, no Grip Breaker, clear edge)

| Check | Expected | Measured |
|---|---|---|
| Automatic hover after full surface loss | zero within 0.10 s (bounded 0.075 s decay) | |
| Ground effect with no live surface | zero immediately | |
| Manual command | remains zero | |
| AirborneAutomaticHoverCarryover | none | |
| GroundEffectWithoutLiveSurface | none | |

## Test D — Manual Bottom-Thruster Jump

| Check | Expected | Measured |
|---|---|---|
| Manual bottom force airborne | active (recorded in manualBottomCommand) | |
| Automatic force | zero | |
| False carryover events | none (manual excluded by event logic) | |

## Test E — Probe Seam Test (slow + high speed across compound-collider seams)

| Check | Expected | Measured |
|---|---|---|
| False complete surface loss | none (sphere cast bridges seams) | |
| Grace | brief ProbeGraceEntered→Recovered pairs only, never > 0.045 s | |
| Force growth during grace | impossible (no-growth clamp) — verify commands flat | |
| Collider transitions | ProbeSurfaceChanged recorded at seams | |
| Wrong-surface detection | none (TrackSurface-only mask) | |

## Test F — Full Route (compare against baseline session 3ee461c97d39)

| Metric | Baseline | Rev1.1 target | Measured |
|---|---|---|---|
| Max speed | ~1,449.5 km/h | ≥ baseline (no perf identity change) | |
| Bottoming events | 4 | reduced | |
| Confirmed bottoming duration | ~10.0 s | ≥ 50% improvement (≤ 5 s) | |
| Probe-loss count | repeated in S1 | substantially improved | |
| Chassis-contact duration | major in S1 | reduced | |
| Unexpected launches | repeated in S1 | reduced | |
| AirborneAutomaticHoverCarryover | present | zero in clean segments | |
| GroundEffectWithoutLiveSurface | present | zero | |
| Emergency band share (grounded) | ~17% | reduced (earlier anticipation shifts load into Performance/Extreme) | |
| Saturated share | ~4% | reduced | |
| Sections 2–5 | clean ≥1,200 km/h | remain clean and stable | |
| Power limitation (reactor/hover/vectoring) | 0 s | 0 s | |
| Recorder overhead | negligible | negligible (no per-frame allocations added) | |

## Section 1 verdict (Phase 7)

After Test B + F, read `section_compatibility.csv` limitation column for
Section 1: Controller-limited cases should convert to clean or clamp-reported;
what remains classifies as Probe-limited / Transition-discontinuous /
Geometry-incompatible. Geometry-incompatible at 1,400+ km/h entry is a track
design decision (larger vertical-compression radii or an intended braking
challenge) — not a craft change.
