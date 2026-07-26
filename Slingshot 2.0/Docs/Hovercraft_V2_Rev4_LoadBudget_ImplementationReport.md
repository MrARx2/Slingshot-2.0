# Hovercraft V2 — Rev4 Load Budget, Operating Bands & Curvature Calibration — Implementation Report

2026-07-23. Built on evidence from Rev2 session `7a07073a4890` and Rev3 session `2450c12572f6` (emergency hover 78% of grounded time; cruise load ~270 kN vs 220 kN continuous; failures section-dependent, not purely speed-dependent).

## Files changed / created

| File | Change |
|---|---|
| `CraftConfigs/Hovercraft_V2_Medium_7_5T_Rev4_Test.json` | New calibration config (Rev3 preserved untouched) |
| `HoverStabilizerArray.cs` | `CurvatureDebug` exposure: raw / clamped / smoothed curvature accel + surface-normal rotation rate |
| `TestDriveRecorderData.cs` | Per-frame load-budget terms, operating band, curvature diagnostics; SectionAggregate: band times, reserves, geometry, contact/probe/landing counters; new thresholds |
| `TestDriveRecorderEvents.cs` | Band interval + reserve-depleted events; launch/bottoming classification evidence flags |
| `TestDriveRecorder.cs` | Load-term sampling, band state machine + timers + longest intervals, authoritative section geometry, launch classification, Phase 11 steering detection, section load aggregation |
| `TestDriveRecorderExporter.cs` | `hover_operating_bands.csv`, `section_load_budget.csv`, `section_compatibility.csv`; compact JSON: `loadBudgetSummary`, `operatingBandSummary`, `sectionCompatibility`, `recommendedSectionSpeeds`; band block in the markdown report |

## Rev4 config (`Hovercraft_V2_Medium_7_5T_Rev4_Test.json`)

| Value | Rev3 | Rev4 | Derivation |
|---|---:|---:|---|
| Continuous per node | 55 kN | **80 kN** (320 kN total) | recorded ordinary 1000–1200 km/h load ≈ 270 kN → ~50 kN cruise reserve; static equilibrium ≈ **0.23** |
| High-load band (cmd 1–2) | — | **640 kN** | 2× continuous; ordinary banked/curved high-speed sections |
| Emergency (cmd 2–4, overdrive ×4) | 880 kN | **1,280 kN** | 170.7 m/s² surface-normal — in the 140–170 target range |
| hoverMaxCommand | 4 | 4 | unchanged |
| totalPowerOutput | 1200 | **1600** | full emergency hover ≈ 1,280 units; hardware limits must not be confused with reactor limits |
| Aero (CdA 2.5, wing/GE 2, 3 g) | — | **unchanged** | isolates hover capacity vs track load |
| Steering / traction / COM / inertia / dimensions | — | **unchanged** | per restrictions |

## Load-budget model (Phase 1)

Per sampled physics frame, from **owning-system snapshots only** (CornerDebug commands × node continuous force; aero `Last*`; roof applied force): `loadGravityN, loadCurvatureN, loadSpringN, loadDampingN, loadAttitudeN, loadRoofOppositionN` and
`loadTotalRequiredN = gravity + aero + groundEffect + roofOpposition + max(0, curvature)`,
plus `loadTotalDynamicN` (adds positive spring/damping) — labeled measured, not predictive. Capacities and per-band reserves (continuous / high-load / emergency) are in every band CSV row; negative reserve = demand exceeds the band.

## Operating bands (Phase 7)

From average applied node command: **Continuous ≤1 < HighLoad ≤2 < Emergency <4 ≤ Saturated** (saturated = ≥3 nodes at ≥98% of ceiling). Timers per band, longest high-load/emergency/saturation intervals, and interval events with hysteresis: `HighLoadHoverInterval`, `EmergencyHoverInterval`, `HoverSaturationInterval` (entry = event start, exit = event end), plus `Continuous/HighLoad/EmergencyReserveDepleted` (0.25 s min). Semantic/diagnostic only — no heat or duty gameplay.

## Track geometry & compatibility (Phases 2–5)

Section curvature comes from the generated track's own `SubdivisionFrames` (never trajectory-estimated when track data exists): length, min radius, max horizontal/vertical curvature per section, cached on first entry. Classification per section (evidence attached, never silent): **Green** ≤80% continuous · **Yellow** ≤80% high-load · **Orange** ≤90% emergency · **Red** beyond that OR observed bottoming / >0.25 s chassis contact / >1 s emergency saturation · **Purple** when geometry is missing or the section was mostly airborne/discontinuity. Recommended speeds per band: `v = sqrt(a_avail × r_min)` with `a_avail = (capacity − weight − section-average aero)/mass` — explicitly diagnostics for track-generator validation, never speed caps.

## Curvature feed-forward audit (Phase 8) — findings

- **Units/sign verified:** demand = −(dN/dt)·v, i.e. v·ω = v²/R in m/s²; concave positive, convex (crest) negative — consistent with its use in throttle (`curvatureThrottle = accel / (nodeCount × nodeAccel)`).
- **Clamp:** applied to the raw measurement BEFORE smoothing (`maxCurvatureAccel`), now exposed as raw vs clamped vs smoothed in `CurvatureDebug`. `maxCurvatureAccel = 80` was NOT reduced (per restrictions) — compare `curvatureRawAccelMs2` against the authoritative geometry columns in `section_load_budget.csv` to verify it in Test C/E.
- **Smoothing:** asymmetric attack/release (20/80 s⁻¹) + predictive pre-release from the look-ahead probes (loop exits release early). Release behavior is measurable via `CurvatureRelease` launch evidence.
- **Double counting: none found.** Curvature appears once as force demand; its other two appearances (command-ceiling headroom, anti-launch baseline) are limits, not forces.
- **Craft-motion contamination:** the measurement derives from probe-ground normals, which are surface facets — body roll/yaw does not rotate them directly, but facet transitions under the four probes can alias during aggressive attitude changes. Flagged for verification in Tests C/E by comparing measured `curvatureAccelMs2` against `speed²/geomMinRadius`.

## Probe/bottoming & launch classification (Phases 9–10)

Bottoming evidence extended with `TrackNormalDiscontinuity` (normal rate > 300°/s) and `CurvatureDemand` (curvature > 50% of applied hover force). `UnexpectedLaunch` now classifies: `EmergencyRelease` (overdrive ended <0.25 s ago), `CurvatureRelease` (curvature load halved within ~0.3 s), `CrestTransition` (convex demand < −5 m/s²), `CollisionRebound` (<0.3 s after contact start), `TrackNormalDiscontinuity`, `LegitimateJumpPossible` (section has significant vertical curvature — jump metadata does not exist yet, recorded honestly as "possible"). Target: ≥80% of launch events carry non-Unknown evidence.

## Steering detection (Phase 11)

Contact-constrained steering now has its own thresholds: command ratio ≥ 0.5, chassis contact, ≥250 km/h, ≥0.25 s, with low response = weak yaw accel per command **OR heading response opposite the commanded direction** (yaw rate is never required to be near zero). Free-running numb steering evaluates ONLY with no contact, no near-bottoming, and vectoring power ≥95%.

## Known limitations

- Banking/slope per section and element-type names are not yet extracted from track metadata (radius/curvature are); marked Unknown.
- `requiredCapacityAtMaxSpeed` in the compatibility CSV currently equals observed max required load (measured, not extrapolated).
- Recommended speeds use section-average aero (aero itself is speed-dependent — the estimate is conservative at low observed speeds).
- Element "is a jump" metadata doesn't exist; launch classification uses `LegitimateJumpPossible` instead.

## Next: run the test matrix (Tests A–G in `Hovercraft_V2_Rev4_TestResults.md`) and compare the full route against Rev3 session `2450c12572f6` using the Phase 13 targets.
