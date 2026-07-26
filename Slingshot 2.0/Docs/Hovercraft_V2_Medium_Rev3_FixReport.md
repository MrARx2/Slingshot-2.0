# Hovercraft V2 — Medium Rev3 Hover & Recorder Fix Report

2026-07-23. Corrective pass driven by recorded session `7a07073a4890` (Full Diagnostic, 37.39 s, config hash `318e8450a29d3833`).

## Files changed

| File | Change |
|---|---|
| `HoverStabilizerArray.cs` | **Anti-windup command pipeline** + extended `HoverCornerDebug` (clampedTarget, activeMaxCommand) |
| `VectorThrusterArray.cs` | Snapshot gains `finalYawCommand` — the canonical signed steering command |
| `TestDriveRecorderData.cs` | New thresholds (slip speed gate, prolonged-emergency, aero-dominance); CoreFrame: 3 distinct steering signals, raw/clamped/smoothed/applied hover commands, discontinuity + collision-active classification, vectoring power scale; NaN clearance sentinel |
| `TestDriveRecorderEvents.cs` | New events: `HoverCommandWindup`, `ProlongedEmergencyHover`, `AeroLoadDominance`, `ContactConstrainedSteering`; new evidence: `ChassisContactConstraint`, `NearBottoming`, `ReactorSaturation` |
| `TestDriveRecorder.cs` | Sampling + detection updates (see below) |
| `TestDriveRecorderExporter.cs` | Corrected summaries: clearance split, accel classification, hover command naming, collision terminology, power terminology |
| `CraftConfigs/Hovercraft_V2_Medium_7_5T_Rev2_PreTest.json` | Byte-identical copy of the pre-fix baseline (preserved) |
| `CraftConfigs/Hovercraft_V2_Medium_7_5T_Rev3_Test.json` | New test tuning (below). Baseline NOT overwritten |

## 1. Hover anti-windup (the launch bug)

**Before:** `rawDemand → clamp to cushionMaxThrottle → smooth`. `cushionMaxThrottle = hoverMaxCommand × stiffness + curvatureHeadroom` could reach ~9–12, far above what hardware can output (overdrive ×3). The smoothed state chased that inflated demand; when compression ended, the stored command had to decay from ~9 down through 3 before output dropped below maximum — thrusters stayed pinned and fired the craft off the track (the recorded `UnexpectedLaunch` events near compression recovery).

**After (per spec):**
```
rawDemand  (unbounded — preserved for diagnostics as CornerDebug.preClampTotal)
  → clampedTarget = Clamp(rawDemand, 0, min(cushionCeiling, hoverMaxCommand))
  → smoothed state moves toward clampedTarget; stored state hard-clamped ≤ hoverMaxCommand
    (ceiling reductions at runtime/config-import clamp stored state immediately)
  → final safety clamp ≤ hoverMaxCommand
  → ThrusterNode
```
Per-node state (the four corner smoothed values were already per-node). No integrator added. The recorder validates the invariant every sample: a stored command above its ceiling emits a severity-3 `HoverCommandWindup` event — after this fix it must never fire.

## 2. Rev3 test tuning (`Hovercraft_V2_Medium_7_5T_Rev3_Test.json`)

| Value | Rev2 | Rev3 | Why |
|---|---:|---:|---|
| wing Cl·A / GE Cl·A | 8 / 8 | **2 / 2** | 8+8 hit the 4 g clamp (~294 kN) by ~620 km/h — above the entire 220 kN continuous hover capacity; emergency became the normal high-speed state |
| maxDownforceG | 4 | **3** | caps aero load at ~221 kN ≈ continuous capacity |
| hover overdrive | 3 | **4** (emergency 220 kN/node, 880 kN total) | high-speed compression reserve; continuous force deliberately unchanged (low-speed hover was stable) |
| hoverMaxCommand | 3 | **4** | matches the new overdrive rating |
| totalPowerOutput | 800 | **1200** | temporary diagnostic budget: emergency hover (880) must not strip steering/propulsion while the suspension is validated; reduce again later |
| steering / traction / COM / inertia / dimensions | — | **unchanged** | next test isolates hover/aero/power corrections |

New load picture: at the 3 g clamp, aero + weight ≈ 294 kN vs 880 kN emergency (3.4× margin) and the clamp isn't reached until far later thanks to Cl·A 2+2 (~1000+ km/h).

## 3. Recorder corrections

- **Steering signals (4.1):** three distinct signals everywhere (core CSV, compact, bands): `rawMouseYawInput`, `processedYawIntent`, `finalSteeringCommand` (+ `steerCommandRatio01` = |final| / corner cap, the normalized authority request). Never conflated as "steer" again.
- **Numb steering (4.2):** input condition now uses the normalized final command ratio (the old check used intent vs a 0.6 threshold that the actual command scale could never reach — that's why zero events fired); grounded via `groundedFactor ≥ 0.3`; response = yaw acceleration per unit command (not yaw rate). Evidence now includes chassis contact, near-bottoming, emergency saturation, reactor saturation, vectoring power scale, tilt/roll-rate safety, thruster saturation, yaw damping, contact loss. **Chassis-contact intervals emit `ContactConstrainedSteering` instead** — scraping-limited steering is explicitly distinguished from free-running weakness.
- **Acceleration spikes (4.3):** samples are classified (never deleted): recording start, respawn ±0.2 s, and position discontinuities are `discontinuity`; open-collision samples are `collisionActive`. Summary reports `maxContinuousDrivingAccelG` / `maxCollisionAccelG` / `maxTeleportDiscontinuityG` — no more 2,644 g "maximums".
- **Clearance (4.4):** sentinel is now NaN ("no surface reading"), never −1; negative clearance is VALID penetration data and is included. Summary splits `minimumValidFreeClearanceM`, `minimumPenetrationClearanceM`, `timeNearBottomingS`, `timeConfirmedBottomingS`, `timeBelowWarningIncludingBottomingS`.
- **Hover command naming (4.5):** `avgRawDemand`, `p95RawDemand`, `avgClampedTarget`, `avgSmoothedCommand`, `avgAppliedCommand`, `timeContinuousSaturated`, `timeEmergencySaturated` — with the explicit note that raw demand may exceed the hardware ceiling.
- **Slip (4.6):** slip events gated by `minimumSlipEventSpeedKmh = 50` (raw slip still recorded every frame).
- **Collisions (4.7):** `distinctImpactEvents` / `contactIntervals` / `scrapeIntervals` / `chassisImpactEvents` / `wallImpactEvents` — the single ambiguous count is gone.
- **Power terms (4.8):** `reactorOverloadTime` (total demand > budget) is now separate from `hoverPowerLimitedTime` and `vectoringPowerLimitedTime`; the report states explicitly that overload ≠ hover starvation.
- **New events (Phase 5):** `HoverCommandWindup` (validation, severity 3), `ProlongedEmergencyHover` (warn > 1 s, critical > 3 s at close), `AeroLoadDominance` (aero > 50%/100% of continuous hover capacity), `ContactConstrainedSteering`.

## 4. Validation performed / to perform

- **Pipeline invariants** (6.1): enforced in code (target and stored state mathematically cannot exceed the ceiling; ceiling reduction clamps stored state on the next tick) and watched at runtime by the `HoverCommandWindup` recorder validation. A standalone unit test is deferred (no test asmdef covers Assembly-CSharp) — documented.
- **Your drive tests** (6.2–6.4): import Rev3_Test → 20 s static hover (expect: no emergency, no bottoming, command ≈ 0.33, no windup event) → straight-line speed run (expect: clearance holds, `AeroLoadDominance` far later than before, reactor not saturated on a straight) → repeat the original route, Full Diagnostic, and compare the `.hoverdrive.json` against session `7a07073a4890` using the Phase-7 targets (bottoming 4→0/1, near-bottoming −75%, scrape duration −90%, emergency time < 20% of grounded time).

## Known remaining / deferred

- The 1200 power budget is diagnostic, not balance — plan to re-tighten after the suspension validates.
- Steering authority deliberately untouched; judge it only from the Rev3 session's contact-free steering metrics.
- Unit-test harness for the command pipeline, heading-rate-based response metric, and per-corner rise/fall response of the emergency catch remain future items.
- Endurance-mode HF event buffer still not implemented (unchanged from recorder v1.0).
