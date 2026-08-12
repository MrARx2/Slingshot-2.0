# Slingshot 2.0 — Performance Audit & Implementation Plan

**Scope:** editor play, in‑game (play mode) runtime, and exiting play mode.

**Method & honesty note:** this is a *static* audit — reading the code and project
settings for known‑costly patterns. I did **not** run the Unity Profiler, so there are
no measured millisecond numbers here. Every item below tells you exactly where to point
the real Profiler (Deep Profile, and "Profile Editor" for the exit case) to confirm the
cost before you spend effort on it. Treat the ranking as "highest expected value," not
proven wins.

---

## TL;DR — the three that matter

1. **Physics runs at 100 Hz** (`Fixed Timestep: 0.01`). Every hover raycast, stabilizer
   PD pass, drive/vectoring/traction step and thruster loop runs *twice as often as the
   50 Hz default*. This is the single biggest in‑game CPU lever.
2. **Exiting play mode** is dominated by tearing down / snapshot‑restoring the generated
   track (domain + scene reload are already disabled, so it's not reload — it's object
   teardown). Fewer, larger track objects is the structural fix.
3. **IMGUI/GL overlays** (`CraftHUD`, `CraftDebugHUD`, `HoverProbeTelemetry`, `RaceHUD`)
   run `OnGUI` and allocate every frame. Cheap to tame; `HoverProbeTelemetry` also
   auto‑records work even when hidden.

---

## Priority table

| # | Item | Where | Impact | Effort | Risk |
|---|------|-------|--------|--------|------|
| 1 | Physics timestep 0.01 → test 0.0166/0.02 | `ProjectSettings/TimeManager.asset` | High (in‑game) | Low | Feel/tuning |
| 2 | Max Allowed Timestep 0.333 → ~0.1 | same | Med (hitch safety) | Low | Low |
| 3 | Play‑exit teardown of track | `TrackGenerator`, mesh builder | High (exit) | Med | Low |
| 4 | Gate debug overlays out of build + when hidden | `HoverProbeTelemetry`, `CraftDebugHUD` | Med | Low | Low |
| 5 | Kill per‑frame allocs in `CraftHUD` | `CraftHUD.cs` | Low–Med (GC) | Low | Low |
| 6 | Combine track section meshes / instance markers | mesh builder | Med (draw calls + exit) | Med–High | Med |
| 7 | Cache material property lookups | `CraftFeedbackSystem` | Low | Low | Low |
| 8 | Batch hover raycasts (`RaycastCommand`) | `ThrusterBus`/`ThrusterNode` | Low–Med | Med | Low |

---

## 1. In‑game runtime (play mode)

### 1a. Fixed Timestep = 0.01 (100 Hz) — biggest lever
`ProjectSettings/TimeManager.asset` → `Fixed Timestep: 0.01`. The whole hovercraft
pipeline is FixedUpdate‑driven (`CraftCore.FixedUpdate` → telemetry, vectoring, hover
stabilizer, drive, vectoring, traction, thruster bus, boost). Per tick that's roughly
**~10 physics raycasts** (4 hover + 4 roof via `ThrusterBus.CastGroundRaysForList`, +2
in `HoverStabilizerArray` surface align) and **~8 passes over the ~14 thruster nodes**.
At 100 Hz that all runs 2× more than a 50 Hz project.

- **Action:** try `Fixed Timestep = 0.0166` (60 Hz) or `0.02` (50 Hz) and playtest at top
  speed. Because everything already scales by `Time.fixedDeltaTime`, tuning still holds;
  only *feel* and tunnelling resistance change.
- **If 100 Hz is truly needed** for 250 m/s stability, keep `Rigidbody.interpolation =
  Interpolate` and `collisionDetectionMode = ContinuousDynamic` (already set in
  `CraftCore.Awake`) and settle at ~0.0133 (75 Hz) as the compromise.
- **Profiler:** CPU module, look at the `FixedUpdate` group's total ms/sec; halving the
  rate should roughly halve it.

### 1b. Maximum Allowed Timestep = 0.333 — hitch death‑spiral
Combined with a 0.01 step, a single 0.33s hitch asks physics to catch up **33 substeps in
one frame**, which causes a worse hitch → spiral. Lower it to ~0.1.

### 1c. IMGUI overlays each frame
`CraftHUD`, `CraftDebugHUD`, `HoverProbeTelemetry`, `RaceHUD` all use `OnGUI`, which
processes at least twice per frame (Layout + Repaint) and is the slow UI path.
- `CraftHUD` (the new GL HUD) allocates arrays every repaint — see 1e.
- `CraftDebugHUD` is F2‑gated (good) but still an `OnGUI` component.
- **`HoverProbeTelemetry` auto‑records every frame even when the HUD is hidden** — pure
  waste in normal play. See item 4.

### 1d. `CraftFeedbackSystem` per‑frame micro‑costs
`UpdateGripBreakerVisuals` calls `Material.HasProperty("_BaseColor")` /`"_Color"` by
string every frame (`CraftFeedbackSystem.cs:315‑321`). Cache the resolved property ID
once. Minor, but it's on the per‑frame path. (The `GetComponentsInChildren` calls in this
file are in setup/resolve methods, not per‑frame — those are fine.)

### 1e. `CraftHUD` GC churn (self‑inflicted, new)
The GL HUD builds fresh `Vector2[8]` arrays via `Octa(...)` ~15×/repaint plus a couple of
`ToString`/interpolated strings. With incremental GC on (`gcIncremental: 1`) this won't
spike, but it's avoidable:
- Reuse a static `Vector2[8]` scratch buffer in `Octa`/`FillPoly` instead of `new[]`.
- Only rebuild the speed string when the rounded value changes.
- `GUIStyle`s are already cached by scale — good.

---

## 2. Exiting play mode

`ProjectSettings/EditorSettings.asset` shows `m_EnterPlayModeOptionsEnabled: 1`,
`m_EnterPlayModeOptions: 3` → **both domain reload and scene reload are already
disabled.** So enter/exit is *not* paying for a domain reload; the remaining exit cost is:

1. **Undoing everything play mode changed.** With scene reload disabled, Unity restores a
   snapshot on exit. If the track is generated *during* play (`generateOnStart` in
   `TrackGenerator.Start`), exit must destroy that entire runtime hierarchy (many section
   GameObjects, each with a `MeshFilter` + `MeshCollider`) and GC the meshes
   (`TrackGenerator.ReleaseMesh`, lines ~578‑585). Object count is the cost driver.
2. **Editor redraw** of a huge hierarchy (Scene view + Inspector) during the transition.

**Actions (in order):**
- **Generate at edit time, adopt on play.** `TrackGenerator` already has
  `keepEditorTrackOnPlay` + `TryAdoptExistingTrack()`. If you bake the track in the editor
  and enable that flag, play mode won't rebuild it and exit has far less to undo.
- **Cut object count** (also helps rendering — item 6): combine each continuous road run
  into one mesh/one GameObject instead of many section objects.
- **Profiler:** Window ▸ Analysis ▸ Profiler, enable **"Profile Editor"**, record, and
  scrub the exit frame. If the spike is `DestroyImmediate`/`Mesh.Destroy`/scene‑restore,
  it's teardown (do the two actions above). If it's `Inspector`/`SceneView` repaint, the
  hierarchy is just too big for the editor to redraw.
- **Collider cook is an ENTER cost, not exit.** `BoxPrismTrackMeshBuilder.cs:347‑355`
  cooks each `MeshCollider` when `sharedMesh` is assigned; at `ColliderProfileResolution =
  40` that's ~4× the triangles of the old 10. If *entering* play (or Generate) feels slow,
  that's the knob — keep 40 only where the surface actually curves (corkscrews) and drop
  straight/gentle regions back toward 12–16.

---

## 3. Editor (non‑play) costs

- **Generation** runs on demand (menu / Generate button) and at `Start`; `Update` is a
  cheap keypress check — no per‑frame editor cost from `TrackGenerator` itself.
- **Inspector auto‑enumeration** of the Generation group is one‑time per repaint of that
  inspector; not a runtime concern.
- The read‑only **diagnostics/telemetry** (`TrackMeshIntegrityDiagnostics`,
  `HoverProbeTelemetry`) are analysis tools — make sure they're off / excluded from
  builds (item 4).

---

## 4. Debug overlays — gate them

`HoverProbeTelemetry` records node data and draws `OnGUI` every frame, and it self‑records
even when the HUD is hidden. `CraftDebugHUD` is an `OnGUI` component too.
- Wrap their per‑frame work behind an explicit `enabled`/`showHUD` check *before* doing
  any recording (telemetry currently records unconditionally).
- Exclude them from shipping builds with `#if UNITY_EDITOR || DEVELOPMENT_BUILD` around
  the component bodies, or strip the GameObjects in `SceneBootstrapper` for release.

---

## Where we can squeeze MORE (after the quick wins)

These are bigger structural gains, worth it only once 1–5 are done and the Profiler
confirms the remaining hot spots:

- **Combine track meshes + GPU‑instance the markers.** The wall‑marker rings and repeated
  decoration are ideal for GPU instancing / static batching; combining section meshes cuts
  draw calls *and* play‑exit teardown. Point the **Frame Debugger** at the draw‑call count
  first — if it's in the thousands, this is the top GPU/CPU‑render win.
- **Batch the hover raycasts** with `RaycastCommand` (Job‑scheduled) instead of ~10
  individual `Physics.Raycast` calls per tick. Small now, but it scales if you add craft
  or probes.
- **Track LOD / streaming.** For long tracks, only keep colliders + full‑res meshes near
  the craft active; swap distant sections to low‑res or disable their colliders.
- **Migrate the player HUD to UI Toolkit / Canvas.** Removes `OnGUI` entirely and its
  per‑frame allocation/event overhead — the clean long‑term answer for `CraftHUD`.
- **Adaptive collider resolution.** Drive `ColliderProfileResolution` per region from
  curvature so only corkscrews/loops pay for 40; straights cook at ~12.

---

## Suggested order of execution

1. Timestep experiment (item 1a/1b) — 10 minutes, then playtest. Biggest single win.
2. Gate `HoverProbeTelemetry` recording + build‑strip debug HUDs (item 4).
3. `CraftHUD` alloc cleanup (item 1e) + `CraftFeedbackSystem` property‑ID cache (1d).
4. Profile the play‑exit frame with "Profile Editor"; apply item 3 actions based on what
   it shows.
5. Only then: mesh combination / instancing (item 6) and the "squeeze more" list.

---
---

# Part 2 — Across‑the‑board sweep

Broader pass covering rendering (URP), visual FX, memory, script dispatch, physics
config, quality/lighting, build settings, and generation cost. Same honesty caveat: no
measured numbers — confirm each with the Profiler / Frame Debugger before investing.

## Expanded priority table (new items)

| # | Item | Where | Impact | Effort | Risk |
|---|------|-------|--------|--------|------|
| 9  | FX material writes per frame (`SetColor`/`HasProperty` by string, `.material` instancing) | `CraftFeedbackSystem` | High (CPU + batching) | Med | Low |
| 10 | Confirm SRP Batcher on + shader compatibility | URP asset, track/craft shaders | High (draw calls) | Low | Low |
| 11 | Combine section meshes + static batch / instance markers | mesh builder, `TrackGenerator` | High (draw calls + exit) | Med–High | Med |
| 12 | `UploadMeshData(true)` on render meshes | `BoxPrismTrackMeshBuilder` | Med (RAM + upload) | Low | Low |
| 13 | Consolidate/limit `GravityField` FixedUpdate instances | `GravityField` | Med (if many) | Med | Low |
| 14 | Throttle FX to ~30 Hz + `MaterialPropertyBlock` + cached property IDs | `CraftFeedbackSystem` | Med | Med | Low |
| 15 | Trim layer collision matrix | `DynamicsManager` | Low–Med | Low | Low |
| 16 | Build: IL2CPP + managed stripping | Player settings | Med (build only) | Low | Med |
| 17 | Review URP asset (shadow dist, add‑lights, MSAA, HDR) | URP asset | Med (GPU) | Low | Low |

## 9 & 14. Visual FX — `CraftFeedbackSystem` is the second CPU hot spot
`UpdateVisuals` runs **every Update** and the file has ~200 particle/material calls: many
`ParticleSystem`/`TrailRenderer` updates plus `Material.SetColor`/`SetFloat` per frame,
and `Material.HasProperty("_BaseColor"/"_Color")` **string lookups every frame**
(`CraftFeedbackSystem.cs:315‑321`).

Two problems: (a) string‑keyed material writes are slow and allocate; (b) writing to
`renderer.material` (vs `sharedMaterial`) **instantiates a per‑renderer material**, which
breaks the SRP Batcher and multiplies draw calls.

**Actions:**
- Cache property IDs once with `Shader.PropertyToID` and use the `int` overloads.
- Drive per‑frame color/emission through a **`MaterialPropertyBlock`** on the renderer
  instead of mutating a material instance — keeps batching intact, no allocation.
- **Throttle FX to ~30 Hz** (or update on‑change) rather than every frame; visual
  feedback doesn't need 100/60 Hz updates.
- Cap `ParticleSystem` max particle counts and disable wake/filament/ghost/cinematic
  systems when off‑screen or below a speed threshold.
- **Profiler:** CPU module → look for `CraftFeedbackSystem.UpdateVisuals` and
  `Material.SetColor`; GPU/Frame Debugger → watch draw‑call count with FX on vs off.

## 10 & 11. Rendering (URP) — draw calls
The project uses **URP** (`GraphicsSettings` has a custom render pipeline asset).
- **SRP Batcher** is URP's main draw‑call win. Confirm it's enabled in the URP asset and
  that track/craft materials are SRP‑Batcher **compatible** (URP Lit / Shader Graph are;
  per‑frame `.material` writes from item 9 break it). Frame Debugger shows "SRP Batch"
  groups vs many individual draws.
- The track is many section GameObjects with individual meshes → lots of draws even
  batched. **Combine each continuous road run into one mesh/object**, mark static track
  geometry for **static batching**, and **GPU‑instance** the repeated wall‑marker rings.
  This is the top render win *and* it shrinks play‑exit teardown (Part 1, item 3).
- **URP asset settings** (not QualitySettings) own shadow distance/cascades, MSAA, HDR,
  and per‑object additional light count — review and trim those in the URP asset.

## 12 & 8. Mesh memory / upload
`BoxPrismTrackMeshBuilder` sets `IndexFormat.UInt32` for >65k‑vert meshes (good) but never
calls `UploadMeshData(true)` or sets `isReadable=false`. Render meshes therefore keep a
**CPU copy in RAM** for the life of the track. After building a *render* mesh (the
collider uses its own coarse mesh), call `mesh.UploadMeshData(true)` to free the managed
copy and speed the GPU upload — provided nothing reads its verts at runtime (the
integrity diagnostics reconstruct their own geometry, so they don't).

## 13. Script `Update`/`FixedUpdate` dispatch & many‑instance components
14 component types implement `Update`/`FixedUpdate`; cost scales with **instance count**.
`GravityField.FixedUpdate` runs per field at 100 Hz and iterates its in‑range receivers.
If gravity fields are authored **per track section** (many in the scene), that's real
per‑tick dispatch + iteration even when a field is empty. Consolidate to a single manager
that ticks only fields near the craft, or disable inactive fields' components. (Most other
per‑frame scripts — `CraftCore`, camera, HUD, sensor — are single‑instance and fine.)

## 15. Physics configuration
- **Layer collision matrix is fully enabled** (all layers collide with all —
  `DynamicsManager` `m_LayerCollisionMatrix` all `ff`). If you use dedicated layers (craft
  / track / triggers / markers), disable pairs that never interact to cut broadphase pairs.
- Ensure the **craft uses primitive colliders**, not a MeshCollider; the track's coarse
  MeshCollider is already the right call.
- `m_AutoSyncTransforms: 0` (good), solver iterations 6/1 (default, fine).

## 16 & 17. Quality, lighting, build
- QualitySettings look reasonable: `shadowCascades 2`, `softParticles 0`,
  `realtimeReflectionProbes 0`, `billboardsFaceCameraPosition 1`. `lodBias 2` on the high
  tier makes LODs switch late — lower toward 1 once you add LOD groups.
- **Bake static lighting** where possible; keep realtime shadow‑casting lights minimal
  (`SceneBootstrapper` scans scene `Light`s — verify how many are realtime).
- For shipping builds: **IL2CPP** backend + **managed stripping (Medium)** for CPU/size
  (add a `link.xml` if reflection‑used types get stripped). `stripEngineCode` is already
  on. Exclude the debug/telemetry components from the build (Part 1, item 4).

## 18. Generation‑time (editor only, not gameplay FPS)
Generation uses LINQ + iterative solvers (`QuarterRoadFitter` up to 40 attempts under
`RobustDualQuarterFit`, the closure solver, `TrackRetopology`, `TrackCandidateBuilder`).
This is on‑demand, so it never touches gameplay frame rate — but it's the "Generate takes
a while" cost. If that bothers you: cap solver attempts, cache candidate frame walks, and
skip rebuilding markers on partial regenerations. **Low priority** unless generation time
is annoying you.

## Confirmed clean (don't waste time here)
- The **craft physics path is LINQ‑free**; LINQ only appears in generation/editor/telemetry.
- `ThrusterBus` loops use `List<T>` struct enumerators (no per‑tick GC).
- `CraftIntent`/`PilotCommand`/`EnergyState` are structs (no heap alloc per tick).
- Camera uses `SphereCastNonAlloc` for collision avoidance (good).
- `GetComponentsInChildren` calls are in setup/resolve methods, not per‑frame.

## Recommended profiling session (to turn this into numbers)
1. Window ▸ Analysis ▸ **Profiler**, Deep Profile OFF first. Watch the CPU module's
   `FixedUpdate` vs `Update` vs `Rendering` split at speed. Confirms item 1 (physics) and
   item 9 (FX) shares.
2. **Frame Debugger** — read the draw‑call count and whether draws are "SRP Batch"ed.
   Confirms items 10/11.
3. **Memory Profiler** — snapshot to see render‑mesh CPU copies and material‑instance
   count. Confirms items 12 and 9.
4. **"Profile Editor"** enabled, scrub the play‑exit frame. Confirms Part 1 item 3.

---
---

# Part 3 — Correctness bug: track spawns below ground (y < 0)

Not a performance item, but tracked here per request. **Priority: high** — it's visible in
most generations and undermines the `KeepAboveStart` promise. (This is the pre‑existing
"KeepAboveStart ground violation" already on the task list.)

## Symptom
In most generations the track sits partly **below world ground level 0**, even though the
ground policy is `KeepAboveStart` (`TrackDesignerSettings.cs:416`, default), which is
documented as "the track never dips below its start elevation."

## Root cause (from the elevation solver)
`TrackTopologyPlanner` builds the lap's elevation as a walk of per‑section
`ElevationChange` deltas (around lines 2739‑2791):

- The level **window** `lo=0 … hi=amplitude` for `KeepAboveStart`
  (`TrackTopologyPlanner.cs:2742‑2743`) is only used to **bias the random target** each
  step (line 2755‑2758). It is *not* a hard constraint on the realized running height.
- After the walk, the **residual‑bleed passes** (2766‑2776) and the **final closing
  correction** `deltas[last] -= level` (2783) adjust deltas so the lap returns to its
  start height (sum of deltas ≈ 0). These closure adjustments can push the **cumulative**
  height **negative** partway around the lap.
- There is **no post‑solve check** that `min(cumulative height) ≥ 0`. So whenever closure
  forces a net dip, sections realize below the start plane — hence "most generations."

Underpasses are *not* the cause: they're correctly gated to `FreeFloating` only
(`ResolvedTrackGenerationConfig.cs:453‑459`), and `KeepAboveStart` crests never dip
(`2844`). The dip comes purely from the unconstrained closure of the major‑elevation walk.

## Recommended fix (robust, low‑risk)
Because the lap is closed, its elevation profile is only defined **up to a constant
vertical offset** — shifting the whole track up/down doesn't affect closure. So:

1. After all `ElevationChange` deltas (and bridge/crest local hills) are finalized,
   compute the **running cumulative height** across the whole lap and take its **minimum**
   (include the deepest point of the rideable surface, i.e. account for half‑pipe depth if
   the floor can sit below centerline).
2. If `GroundLevelPolicy == KeepAboveStart` and that minimum is below a configured
   clearance, **lift the entire track by the deficit** — apply a constant offset to the
   base/start elevation (or the track‑root Y) so the lowest rideable point sits at `0`
   (or `+clearance`). Deterministic, seed‑independent, and it can't reopen the loop.
3. Keep the existing soft window bias as‑is; the global lift is the guarantee.

Optionally add a generation **assertion/report line**: after building, verify
`min surface Y ≥ 0` under `KeepAboveStart` and log a warning if violated, so regressions
are caught in the debug report (same place other closure failures are reported, ~2779).

## Where
- Solve/offset: end of the elevation pass in `TrackTopologyPlanner` (after line ~2791),
  or when the base elevation is applied to frames in the candidate/mesh build.
- Verification: `TrackDebugReportExporter` (add a "min surface Y" line next to the
  existing Elevation report at line ~159).

## Verify
- Regenerate several seeds with `KeepAboveStart`; confirm no geometry below y=0 and that
  the craft still spawns on the surface at the start line.
- Confirm `FreeFloating` behaviour is unchanged (no lift applied).
- Add/extend an editor test: for N seeds under `KeepAboveStart`, assert the lowest built
  vertex Y ≥ 0 (mirrors the existing closure/elevation tests).
