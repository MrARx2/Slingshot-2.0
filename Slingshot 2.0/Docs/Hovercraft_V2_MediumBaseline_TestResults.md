# Hovercraft V2 — Medium Baseline Test Results

Protocol from the baseline plan Phase 14. **Measured columns are blank — fill them in while driving** (F2 debug HUD + CraftAerodynamics/HoverStabilizerArray inspector debug fields + Energy panel). "Expected" values are the derived predictions from the implementation report; a large mismatch means either a bug or a tuning follow-up.

Craft state for a valid run: prefab after Apply Physical Definition + Medium baseline config import, scene overrides reverted, downward assist 0.

## Test 1 — Static hover (flat ground)

| Metric | Expected | Measured |
|---|---|---|
| Equilibrium hover command (per corner, CornerDebug.finalCommand) | ~0.41 | |
| Height error at rest | < 0.1 m | |
| Oscillation amplitude | ± a few cm (bob 0.035) | |
| Per-node force (F2 panel) | ~18,400 N | |
| Hover power draw | ~74 of 450 | |
| Drift/roll bias | none (COM centered) | |

## Test 2 — Forward acceleration (straight)

| Metric | Expected (hardware / power-limited) | Measured |
|---|---|---|
| 0–100 km/h | 1.2 s | |
| 0–300 km/h | 3.5 s | |
| 0–500 km/h | 5.8 s (drag rising) | |
| 0–1000 km/h | ~14 s+ (drag heavy) | |
| Peak accel | 24 m/s² (2.45 g) | |
| Power saturation while cruising | none expected (254/450) | |
| Top speed (drag-limited) | ~1,400–1,510 km/h | |

## Test 3 — Braking

| From | Expected (brake only) | Measured time | Measured distance |
|---|---|---|---|
| 300 km/h | 5.2 s / 217 m (traction+drag will shorten this substantially) | | |
| 500 km/h | 8.7 s / 603 m | | |
| 1000 km/h | 17.4 s / 2,411 m | | |

Also record: brake vs coasting-grip vs drag share if separable (toggle TractionCore off for one run).

## Test 4 — Strafe

| Metric | Expected | Measured |
|---|---|---|
| Left / right lateral accel | ~1.2 g each way | |
| Symmetry | equal | |
| Yaw coupling during pure strafe | minimal | |
| Tilt-safety reduction on banks | fades in 18°→42° | |
| Power draw at full strafe | ~90 | |

## Test 5 — Jumps (each: low speed, high speed, nose-up, nose-down, stabilizer on/off; assist stays 0)

| Metric | Expected | Measured |
|---|---|---|
| Air time / horizontal travel (log speed at launch) | ballistic + wing downforce pull-down at speed | |
| High-speed jump: wing downforce (aero debug, N and g) | multiple g at 300+ km/h | |
| Pitch/roll response airborne | player air control only, no self-leveling | |
| Landing attitude | pre-rotated by look-ahead alignment when probes see the surface | |

## Test 6 — Hard landing

| Metric | Expected | Measured |
|---|---|---|
| Impact speed where hull touches | should exceed ~12–15 m/s falls before chassis contact | |
| Peak hover command (CornerDebug.preClampTotal) | >1 (overdrive engaged, up to 3) | |
| Overdrive force per node (F2 panel) | up to ~135 kN | |
| Rebound | arrested ≤ maxCushionRecoverySpeed 2.5 m/s | |
| Power spike | protected hover wins vs other channels | |

## Test 7 — Banked turn

| Metric | Expected | Measured |
|---|---|---|
| Surface alignment on bank | follows bank, no wobble | |
| Hover load split outer/inner (CornerDebug) | outer corners higher | |
| Lateral grip hold | no slide-off at moderate speed | |
| Steering authority at speed | responsive; check tilt-safety isn't over-cutting | |

## Test 8 — Loop / inverted

| Metric | Expected | Measured |
|---|---|---|
| Contact continuity through loop | maintained (curvature feed-forward + overdrive) | |
| Speed above which the craft peels off | v²/r ≈ 72 m/s² boundary (e.g. ~60 m/s on an r=50 m loop) | |
| Roof-node contribution inverted | active, ≤ 0.85 command | |
| Ground effect direction | along surface normal (into track) | |
| Power budget through loop | hover protected; drive may starve — note feel | |
| Recovery on exit | clean, no launch | |

## Verdict / retune notes

- [ ] Baseline accepted as-is
- [ ] Retune list: …
