# Hovercraft V2 — Rev4 Test Results

Fill in from recorded sessions. Craft state: Rev4_Test config imported, Full Diagnostic mode, same track/scene/timestep as Rev3 session `2450c12572f6`.

## Test A — Static hover (20 s, no input, stabilizer armed)

| Metric | Expected | Measured |
|---|---|---|
| Equilibrium command (avgAppliedCommand) | ~0.23 | |
| Band | Continuous 100% | |
| Emergency time | 0 s | |
| Bottoming / unexpected launch / windup events | 0 | |
| Clearance stability | steady, no oscillation growth | |

## Test B — Flat straight speed sweep (0→1200 km/h)

| Band (km/h) | Expected cmd | Measured avg cmd | Clearance | Aero N | Notes |
|---|---:|---:|---:|---:|---|
| 0–200 | ~0.25 | | | | |
| 200–400 | ~0.3 | | | | |
| 400–600 | ~0.4 | | | | |
| 600–800 | ~0.55 | | | | |
| 800–1000 | ~0.75 | | | | |
| 1000–1200 | ~0.85–1.0 | | | | |

Validate: no chassis contact; continuous band dominates; no reactor limitation; `AeroLoadDominance` warning only near the top (aero > 160 kN = 50% of 320 kN).

## Test C — Constant-radius vertical curves

For each radius/speed pair record predicted `v²/r` vs measured `curvatureAccelMs2` vs geometry `speed²/geomMinRadius`, applied hover force, band, clearance, bottoming.

| Radius m | Speed km/h | Predicted a m/s² | Measured a | Applied force N | Band | Bottoming |
|---:|---:|---:|---:|---:|---|---|
| | | | | | | |

Curvature audit check: measured ≈ predicted within ~15%? If measured ≫ predicted, probe-facet aliasing is contaminating the measurement (Phase 8 follow-up).

## Test D — Constant-radius banked turn

| Metric | Measured |
|---|---|
| Normal load / band | |
| Steering response (yaw accel per command) | |
| Hover front/rear imbalance | |
| Lateral grip hold | |

## Test E — Compression and crest

| Metric | Expected | Measured |
|---|---|---|
| Curvature demand rise/release | smooth, pre-released at exit | |
| UnexpectedLaunch events | 0, or classified (CrestTransition/CurvatureRelease) | |
| Probe continuity | no losses | |

## Test F — Controlled hard landings (10/20/30/40 m/s)

| Impact m/s | Peak command | Band | Rebound ≤2.5 m/s? | Chassis contact? |
|---:|---:|---|---|---|
| 10 | | | | |
| 20 | | | | |
| 30 | | | | |
| 40 | | | | |

Capacity note: emergency 1,280 kN arrests ~1.7 m/s per physics tick; 40 m/s needs ~0.24 s of full emergency inside a 3.75 m cushion — expect contact ABOVE roughly this range, by design.

## Test G — Original full route vs Rev3 (`2450c12572f6`)

| Metric | Rev3 | Target | Rev4 measured |
|---|---:|---|---:|
| Confirmed bottoming count | 16 | ≤6 (−60%) | |
| Confirmed bottoming duration | 31.34 s | ≤7.8 s (−75%) | |
| Emergency time / grounded | 78% | < 35% | |
| Saturated time / grounded | ~72% | < 15% | |
| Reactor overload | 0 s | 0 s | |
| Launch events classified | — | ≥ 80% | |
| Contact-constrained steering detected | 0 (bug) | > 0 where visible | |

## Section verdicts (from section_compatibility.csv)

| Section | Class | Max required load | Emergency capacity | Verdict: craft limit or track too aggressive? |
|---|---|---:|---:|---|
| | | | | |

Final questions the data must answer:
1. Is the craft underpowered for the track? (Red sections with required load beyond 1,280 kN at reasonable speeds → yes for those sections)
2. Is the track too aggressive for the craft at observed speeds? (Sections whose emergency-compatible speed is far below the speeds the track invites → track-side issue)
