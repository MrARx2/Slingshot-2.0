# Apex Hyperclass Rev1.1 — Implementation Report
## Curvature Anticipation, Probe Continuity, and Airborne Carryover Fix

Baseline preserved: `CraftConfigs/Hovercraft_V2_Apex_Hyperclass_11T_Prototype.json`
is untouched. Rev1.1 lives in `Hovercraft_V2_Apex_Hyperclass_11T_Rev1_1.json`
plus targeted runtime/recorder changes. **No force, thrust, power, steering or
traction value changed** (Phase 8 verified — the Rev1.1 config is byte-equivalent
to the prototype outside the listed deltas).

## 1. Files changed

| File | Change |
|---|---|
| `ProjectSettings/TagManager.asset` | NEW layer 8 `TrackSurface` |
| `Assets/Scripts/TrackGeneration/Macro/TrackSurfacePhysics.cs` | `SurfaceLayerName` / `SurfaceLayer` / `SurfaceMask` (warns once if the layer is missing) |
| `Assets/Scripts/TrackGeneration/Macro/BoxPrismTrackMeshBuilder.cs` | Generated collider chunks are placed on TrackSurface |
| `Assets/Scripts/Hovercraft_Setup/Hovercraft/ThrusterNode.cs` | Probe modes, grace window, full probe diagnostics |
| `Assets/Scripts/Hovercraft_Setup/Hovercraft/HoverStabilizerArray.cs` | Bounded automatic-hover loss decay, stale-probe no-growth rule, grace-gated landing catch |
| `Assets/Scripts/Hovercraft_Setup/Aerodynamics/CraftAerodynamics.cs` (+ snapshot) | Legacy-bridge GE live-surface requirement |
| `Assets/Scripts/Hovercraft_Setup/Diagnostics/TestDriveRecorder*.cs` (4 files) | Curvature/probe events, per-node probe stream, acceleration classification, band-semantic cleanup, section limitation report |
| `Assets/Prefabs/HovercraftRootV2_ApexHyperclass.prefab` | maxCurvatureAccel 220, SphereCast probes, TrackSurface masks, loss-decay field |
| `CraftConfigs/Hovercraft_V2_Apex_Hyperclass_11T_Rev1_1.json` | NEW Rev1.1 config |

Medium Rev4 craft, prefab and configs: untouched. The Medium prefab keeps its
Everything probe mask (its own baseline); the layer restriction is an Apex
Rev1.1 setting.

## 2. Curvature feed-forward (Phase 1)

`maxCurvatureAccel` 120 → **220 m/s²** (config + prefab). The controller may now
anticipate ~76% of the 290.9 m/s² emergency hover envelope proactively, leaving
~71 m/s² of reserve for gravity, downforce, damping, attitude imbalance and
recovery. Kp/Kd/hoverMaxCommand/smoothing/look-ahead unchanged per spec.

Diagnostics per core frame: `curvatureRawAccelMs2` (requested),
`curvatureAccelMs2` (applied), `curvatureClamped`, `availableHoverAccelMs2`
(emergency capacity / mass), `hoverAccelReserveMs2`. New event
**CurvatureFeedForwardClamped** (interval, stamped on open with requested /
applied / available / reserve / command / clearance / probe count; speed and
section ride on the event itself).

## 3. Probe continuity (Phase 2)

**Audit result:** origin = node position; direction = −thrust axis (follows the
craft, banked/inverted safe); triggers always ignored; own hull explicitly
skipped (not treated as a failed cast); NonAlloc buffers, unsorted-hit handling
correct. Weaknesses found: Everything layer mask (could read walls/props/other
craft as ground — the wrong-surface failure), zero-width ray can drop through
compound-collider seams at speed, and backface hits were accepted.

**Fixes:**
- `TrackSurface` layer (8): generated track collider chunks are assigned to it;
  Apex hover/roof `groundLayers` and the stabilizer's `surfaceProbeLayers` are
  restricted to it (mask 256). Craft colliders, decoration, props and helper
  geometry can no longer read as ground.
- `HoverProbeMode { Raycast, SphereCast }` per node; Apex Rev1.1 default
  **SphereCast, radius 0.1 m** (distance reported ray-equivalent: hit distance
  + radius). Sphere-start overlap is detected and flagged
  (`castStartedInsideGeometry`), never silently ignored.
- Backface candidates (normal pointing with the cast) are rejected and flagged
  (`castHitBackface`).
- **Probe grace** `probeLossGraceTime = 0.045 s` (spec range 0.03–0.06): on a
  missed cast the node holds its last surface data, marked stale
  (`UsingProbeGrace`). Stale data cannot increase force (controller clamps the
  target to the current command while grace is active), the window is
  hard-capped, any fresh surface ends it immediately, and the emergency landing
  catch is disabled during grace. This is sensor continuity, not adhesion.
- Per-node diagnostics recorded: probe mode, grace state, data age, collider
  change, normal delta, inside-geometry and backface flags (`hover_nodes.csv`).
- New events: **ProbeGraceEntered / Recovered / Expired** (per node, instant),
  **ProbeSurfaceChanged**, **ProbeNormalDiscontinuity** (>25°/frame, threshold
  configurable).

## 4. Airborne carryover (Phase 3)

- **Bounded loss decay:** new `automaticHoverLossDecayTime = 0.075 s` — when a
  node has no valid current surface, its command moves to target linearly at
  `hoverMaxCommand / decayTime`, guaranteeing zero from ANY state within the
  window (the old exponential decay left a long tail: from command 5 at rate
  40/s, ~0.4 still remained after 60 ms — the recorded 0.85 carryover events).
  Automatic suspension and curvature terms are already zero without a live
  surface; curvature state releases at 100/s when the grounded factor drops.
- **Command separation:** recorded per frame — `autoHoverCommand`
  (baseHover+stabilizer channels), `airControlCommand` (pilot air-attitude
  differential, |attitude| sum), `manualBottomCommand`, `manualRoofCommand`,
  `overchargeHoverCommand`, plus per-corner channel columns; `finalApplied` =
  `hoverTotalAppliedThrottle`. Landing catch is part of the automatic path by
  design (it only exists with a live reacquired surface — see below).
- **Landing catch gating:** requires the hover cushion active on a FRESH probe
  hit (valid layer, valid normal, in capture range), downward closing speed —
  and now explicitly `!UsingProbeGrace`, so stale data can never trigger it.
- **Event logic per spec:** `AirborneAutomaticHoverCarryover` fires only when
  `groundedNodeCount == 0` ∧ automatic (minus air-control) command > threshold
  ∧ manual bottom == 0 ∧ manual roof == 0, ≥ 0.05 s; detail carries the
  auto/air-control/manual commands, vertical speed and GE force. Manual
  airborne thrust is never flagged.

## 5. Legacy ground effect (Phase 4)

In DirectionalBodyV3 bridge mode GE now requires a **current valid surface**:
at least one hover probe grounded this frame (`telemetry.groundedCount > 0`)
with a valid normal — the smoothed groundedFactor and stale normals are no
longer authority. **Immediate zero** without one (no fade; the snapshot records
`groundEffectUsingFade`-equivalent state as always-off). Snapshot diagnostics:
`groundEffectHasLiveSurface`, `groundEffectLiveProbeCount`,
`groundEffectSurfaceNormal`. The `GroundEffectWithoutLiveSurface` event now
checks the authoritative flag in bridge mode and should never fire. Pure
LegacyV2 (Medium) behavior is untouched.

## 6. Recorder semantics (Phase 5)

- Legacy `timeInEmergencyOverdrive` / `timeContinuousSaturated` /
  `timeEmergencySaturated` remain for old reports but are explicitly marked
  (`legacyMetric`, "NOT valid for Apex band interpretation") in the compact
  JSON and moved to a labeled legacy line in the markdown.
- The band-scheme report is the single source of truth and now includes per
  band: time, share, longest interval, average speed, **maximum speed** and
  **average clearance** (markdown + `operatingBandSummary`).

## 7. Acceleration classification (Phase 6)

Every sample is classified before aggregation (`AccelSampleClass`):
ValidFreeDriving, GroundedContact, ChassisScrape, CollisionImpact,
LandingImpact (0.2 s window after landing), Teleport/Respawn,
NumericalDiscontinuity, Unknown (plausibility ceiling 40 g, configurable).
Per-class maxima are recorded (`MaxAccelGByClass`, `accelerationByClass` in the
compact JSON, class breakdown line in the markdown). The old contaminated
metric is kept but labeled "NOT valid continuous driving". Nothing is deleted —
invalid samples are classified and reported separately. The 587 g artifact now
lands in ChassisScrape/Collision/Discontinuity, never in free driving.

## 8. Section 1 report (Phase 7)

`section_compatibility.csv` gains a **limitation** column classifying troubled
sections: Hardware-limited → Transition-discontinuous → Probe-limited →
Controller-limited → Geometry-incompatible → Unknown (evidence-ordered), plus
max normal-rate and vertical-curvature columns. Per-speed load/reserve data is
in `hover_operating_bands.csv` (now with requested/applied curvature, available
acceleration and reserve); probe-loss locations come from the probe events +
per-node stream. No track geometry is altered — design guidance only.

## 9. Migration / compatibility

- New serialized fields default safely everywhere: existing prefabs keep
  Raycast probes, zero grace, and (Medium) the old GE gating — nothing changes
  for Medium Rev4 without opting in.
- `automaticHoverLossDecayTime` defaults to 0.075 s on all craft; it only
  affects the no-valid-surface case that was previously a long exponential
  tail. Set 0 to restore the pure legacy decay.
- The TagManager edit adds layer 8; regenerate the track (or reload the scene)
  so new collider chunks pick up the layer. If the layer is missing,
  TrackSurfacePhysics warns once and falls back to Default — with restricted
  probe masks that would read as probes never hitting, which is loud and
  obvious, not silent.
- Recorder schema: new columns are appended; event enum values appended (old
  ints stable).

## 10. Compliance (Restrictions)

No hover/main/power increases, no AeroSurface/SurfaceBasedV3, no active aero,
no fake gravity, no speed caps, no velocity writes, no transform rotation, no
hidden adhesion (grace is capped, stale-marked, growth-forbidden and fully
recorded), Kp/Kd untouched, steering/traction untouched, no automatic track
modification, no telemetry suppression, manual airborne thrust treated as a
valid input everywhere.

## 11. Test status

Live runs pending — matrix and pass criteria in
`Hovercraft_V2_Apex_Rev1_1_TestResults.md`, including the required
session-3ee461c97d39 comparison for Test F.
