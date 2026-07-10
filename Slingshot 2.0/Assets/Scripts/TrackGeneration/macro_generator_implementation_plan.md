# Macro Section Generator — Implementation Plan

Companion to `macro_baseline_track_generator_prompt.md`. This is the execution plan for turning the current spline-first generator into a macro-section generator that is game-ready: performant, deterministic, and **editable after generation** (remove/replace/re-theme sections, drop the track into desert / jungle / city scenes).

---

## 1. Where we are, and what actually changes

The current pipeline is spline-first: a distorted ellipse of control points becomes a spline, the spline becomes one continuous extruded mesh per road, and `TrackSectionAnalyzer` *diagnoses* what kind of road emerged so `StuntPlacer` can decorate it. That work is not wasted — the analyzer thresholds, the ramp-chain rule (Jump → Gap → Landing → Recovery), the banking math (per-meter curvature, speed factor, eased transitions), and the seed system all carry over directly. But the brain is inverted:

```
Today:  noise shape → spline → mesh → "what did we get?" → decorate
Target: designer grammar → section list → per-section prism geometry → decorate
```

Two consequences matter most:

1. **Sections are known by construction, not diagnosed.** The layout generator *decides* "90° banked curve, R=140, bank 32°" — nothing needs to be inferred afterward. `TrackSectionAnalyzer` becomes a legacy/validation tool.
2. **One GameObject + one mesh per macro section** instead of one giant mesh per road. This is what unlocks the editability requirement: deleting, swapping, or re-theming a section is an operation on one child object, and it is also *better* for performance (per-section frustum culling) than today's single mesh.

---

## 2. The one hard problem: closing the loop

Everything else in the macro doc is straightforward engineering. The hard part is that a sequence of independently-chosen pieces must return exactly to its start point with matching heading, elevation, and bank. This is where piece-based generators usually die, so it gets solved first, in 2D, before any mesh exists.

**Chosen approach — turn budget + closure solver (no splines involved):**

1. **Grammar pass.** Generate the section sequence from the weighted grammar (rules in §4). Curves get angles from a quantized set (30/45/60/90/120/180). Running heading is tracked; the generator steers total signed turn toward ±360° as it approaches the target section count.
2. **Heading closure.** Whatever residual angle error remains is distributed proportionally across the curve sections (a 90° curve might become 94°). Distribution is capped (±10% per curve) so pieces stay readable.
3. **Position closure.** After heading closes, the endpoint misses the start by some 2D vector. Solve by adjusting the lengths of the straights: straights pointing in different directions form a basis that can absorb any 2D gap (least-squares over all straights, bounded by min/max straight length). This is a tiny linear solve, fully deterministic.
4. **Elevation closure.** Elevation changes are assigned per-section (small, slope-capped); the running sum is corrected the same way — distributed over eligible sections so the loop returns to start height.
5. **Validation.** The closed 2D centerline is tested for self-intersection with a road-width buffer, min-radius violations, and rule violations. On failure: deterministic re-roll (`seed + attemptIndex`), max N attempts, then fall back to a known-good template layout (the "Suggested First Test Layout" from the macro doc) so generation never hard-fails.

Bank and pitch continuity are *not* solver problems: they're handled by construction, because every curve owns its bank-in / hold / bank-out internally, and every section starts from the exact `TrackConnectionFrame` the previous section ended with.

---

## 3. New data model

Four new types, matching the macro doc's proposals, plus one MonoBehaviour that makes sections editable:

**`TrackMacroSectionType`** (enum) — Straight, WideStraight, BoostStraight, RecoveryStraight, BankedCurve, BankedHairpin, SCurve, Chicane, JumpRamp, AirGap, LandingRamp, TunnelVariant, BridgeVariant.

**`MacroSectionDef`** (plain serializable struct/class) — the *plan* for one section: type, length, width, turn direction, turn angle, radius, banking angle, elevation/pitch change, subdivision count, speed intent, risk level, `RequiresRecoveryAfter`, `AllowsBoost`, `AllowsJump`, theme variant tag, debug name.

**`TrackConnectionFrame`** — position, forward, right, up, width, bank angle, pitch angle, accumulated arc length. The contract between consecutive sections: section *i*'s exit frame **is** section *i+1*'s entry frame (same object, not a copy), so joins are weldless by definition. Also carries accumulated UV offset so texturing is continuous across section meshes.

**`GeneratedTrackSection`** — the *result*: its def, entry/exit frames, the array of subdivision ring frames, computed bounds, and (after mesh build) a reference to its GameObject and mesh.

**`TrackSectionComponent`** (MonoBehaviour, on each section's GameObject) — serializes its `GeneratedTrackSection` data so a generated track is self-describing *without* the generator. This is the editability anchor: inspector shows the section's type/parameters, gizmos draw its label/frames/bounds, and editor tooling (§6) operates on it.

---

## 4. New systems

**`MacroTrackLayoutGenerator`** — the designer brain. Consumes `TrackConfig` + seeded RNG, outputs a validated, closed `List<MacroSectionDef>`. Implements the grammar:

- Weighted successor table per section type (frequencies from config: common straights/curves, occasional S-curve/chicane/jump, rare hairpin).
- Hard rules from the macro doc, enforced structurally: jump sequences are inserted as an **atomic group** (BoostStraight? → JumpRamp → AirGap → LandingRamp → RecoveryStraight) — it is impossible to generate a jump into a hairpin because the group carries its own recovery. Hairpins and chicanes auto-append RecoveryStraight. Cooldown counters prevent hairpin/chicane chaining.
- Turn-budget steering and the closure solver from §2.

**`BoxPrismTrackMeshBuilder`** — geometry from macro sections. For each section it emits subdivision **rings** (a ring = one cross-section: road top verts, side walls, skirt/bottom verts for thickness — all quads, no n-gons), then stitches consecutive rings into quads. Ring placement per type:

- *Straight family:* linear interpolation between entry/exit frames. Wide straights lerp width across a short blend zone at each end so width changes are never a step.
- *Banked curve / hairpin:* arc sweep about a computed center; bank applied as smoothstep ease-in → hold → ease-out **inside the section** (the entry and exit frames are always at the neighbor's bank, normally 0).
- *S-curve / chicane:* two/three composed arcs with a shared tangent at the inflection — one macro section, one mesh.
- *JumpRamp / LandingRamp:* pitch profile applied to ring positions (reusing the current `RampProfile` easing); the AirGap section emits **no** geometry, just advances the frame and records the landing target for debug.
- Subdivision density from config (`meters per ring`, more rings on curves by angle), uncapped upward — smooth is fine, *structurally* it stays one section.

Output: **one child GameObject per section** under the track root — `MeshFilter`, `MeshRenderer` (road/wall submeshes), `MeshCollider`, `TrackSectionComponent`, named like `S07_BankedCurve_L90_R140`. Loops and corkscrews are future ring-placement functions in this same builder; nothing else changes.

**`MacroTrackDebugView`** (editor-only) — gizmo labels per section (type, angle, radius), entry/exit frame axes, banking/turn direction arrows, jump landing target, section bounds, and a scene-view list of the generated sequence with the seed. Most of it lives on `TrackSectionComponent.OnDrawGizmos` so it works on saved tracks too.

---

## 5. File-by-file disposition

| File | Fate |
|---|---|
| `TrackGenerator` | **Modified.** Gains `GenerationMode` enum (`MacroSections` / `SplineLegacy`). Orchestration, seed subsystem RNGs, clear/rebuild flow all stay. |
| `TrackConfig` | **Extended.** New "Macro Layout" header: section count range, straight min/max length, curve angle set, radius range, banking strength, subdivision density, jump/hairpin/chicane/s-curve frequencies, recovery length. Existing stunt/gravity/mesh settings stay. |
| `TrackSeed` / `TrackSeedManager` | **Preserved untouched.** Add one subsystem RNG name: `"MacroLayout"`. |
| `TrackData` | **Extended.** Stores the section list alongside existing fields. |
| `RampActor` | **Preserved, role narrowed.** Ramp *geometry* moves into the section mesh; `RampActor` keeps triggers/trick-zone/launch logic and attaches to JumpRamp sections. (If we prefer, V1 can keep spawning the current wedge meshes on top of a flat section — decision point, see §9.) |
| `BoostPadActor`, `WallRideActor`, `GravityZone/Field` | **Preserved.** Re-anchored to sections in Phase 3/5 (BoostStraight spawns pads; wall rides attach to sharp curves; gravity zones tag sections). |
| `StuntPlacer` | **Shrinks.** Jump placement logic is absorbed by the layout grammar (a jump *is* a section). What remains: choosing which BoostStraights actually get pads, wall-ride selection on curve sections, lane picks — all trivially driven by section tags instead of curvature analysis. |
| `TrackSectionAnalyzer` | **Kept as legacy + validator.** Not needed in macro mode (sections are known), still used by legacy mode; later, useful to sanity-check hand-edited tracks. |
| `SplinePathGenerator`, `BranchPathGenerator`, `TrackMeshBuilder`, `MergeZoneUtility`, `BranchTransition` | **Frozen as legacy path.** Untouched, reachable via `SplineLegacy` mode until macro mode is stable and shortcuts are ported. Then archived. |
| Editor scripts | **Extended.** Generate button works for both modes; new section-list panel + per-section operations (§6). |

New files: `Core/TrackMacroSectionType.cs`, `Core/MacroSectionDef.cs`, `Core/TrackConnectionFrame.cs`, `Core/GeneratedTrackSection.cs`, `Layout/MacroTrackLayoutGenerator.cs`, `Mesh/BoxPrismTrackMeshBuilder.cs`, `Sections/TrackSectionComponent.cs`, `Themes/TrackThemeProfile.cs`, `Editor/TrackSectionEditor.cs`.

---

## 6. Editability & theming (the "place it in desert / jungle / city" requirement)

Because every section is a self-describing child GameObject, the following become cheap, well-defined operations exposed on `TrackSectionComponent`'s inspector and a track-level editor window:

- **Delete section** — two modes: *leave gap* (neighbors keep their frames; the hole becomes a jumpable gap or death drop — legitimate gameplay) or *reconnect* (re-run the closure solver on the remaining sequence and rebuild only affected meshes).
- **Replace / mutate section** — swap Straight ↔ TunnelVariant/BridgeVariant, change a curve's radius or bank, resize a straight; only that section's mesh (and its exit-side neighbors' entry frames, if length changed) rebuilds. No full regeneration.
- **Relocate the whole track** — all geometry hangs off one root; move/rotate the root into any environment scene. Sections store frames in root-local space specifically to make this free.
- **Theme system** — `TrackThemeProfile` ScriptableObject: materials (road, wall, ramp, pad), fog/skybox hints, and per-section-type decoration rules (prop prefabs + spawn density: cacti for desert, foliage for jungle, buildings/barriers for city). Applying a theme touches materials and a decoration pass only — geometry never rebuilds, so one generated layout ships as three themed variants.
- **Bake to asset** — serialize the section list + parameters into a `TrackAsset` (ScriptableObject). A baked track can be re-instantiated, hand-edited, versioned, and shipped as level content with the generator stripped from builds entirely.

---

## 7. Performance plan

- **Meshes:** per-section meshes are small (typ. 1–6k verts); everything static. Mark all section objects static → Unity static batching collapses draw calls per material; theme = shared materials, so a themed track is a handful of draw calls. Optional `StaticBatchingUtility.Combine` on the root at bake time.
- **Culling:** per-section renderers give free frustum culling — an improvement over today's single track-length mesh whose bounds are always visible.
- **Colliders:** one `MeshCollider` per section, static, quad-clean topology (this was the point of prism geometry — predictable collision for the physics-sensitive hovercraft). No runtime cooking: colliders bake at generation/bake time.
- **Generation cost:** layout solve is trivial (hundreds of floats); mesh build allocates with pre-sized `List`s / arrays, no LINQ, no per-ring GameObjects. Target: full regenerate < 100 ms in-editor for a 20-section track.
- **Runtime:** zero per-frame cost from the track itself; actors keep their trigger-only physics. Generator code can be excluded from builds once tracks are baked (asmdef split: `TrackGeneration.Runtime` vs `TrackGeneration.Authoring`).

---

## 8. Phased execution

**Phase 0 — Data model** (small): the four data types + `TrackSectionComponent` + config fields. No behavior change. *Done when: project compiles with new types, legacy mode still generates.*

**Phase 1 — Layout brain**: `MacroTrackLayoutGenerator` with grammar, turn budget, closure solver, validation, deterministic re-roll. Debug: 2D gizmo polyline + section labels drawn from the layout alone, before any mesh exists. *Done when: any seed produces a valid closed, self-intersection-free, rule-conforming section list, visible in scene view.*

**Phase 2 — Prism mesh builder** (the milestone): `BoxPrismTrackMeshBuilder` for Straight family, BankedCurve, JumpRamp/AirGap/LandingRamp; per-section GameObjects; frame-contract welding; UV continuity. Generate the macro doc's "Suggested First Test Layout" and drive it. *Done when: a complete drivable closed loop — fast, readable, banked by default, collision-stable — generates from a seed in macro mode.*

**Phase 3 — Sections that fight back**: SCurve, Chicane, BankedHairpin geometry; `RampActor` triggers attached to jump sections; `BoostPadActor` on BoostStraights; full debug view. *Done when: the full V1 piece list generates and plays.*

**Phase 4 — Editability & themes**: section inspector operations (delete/replace/mutate + partial rebuild), `TrackThemeProfile` + decoration pass, bake-to-asset. *Done when: you can generate, delete two sections, re-theme to desert, move the root into a test scene, and bake.*

**Phase 5 — Reintegration & performance**: wall rides on sharp curve sections, gravity zone tagging, static batching/bake path, profiling pass. Shortcuts return here as **branch layouts**: a sub-sequence of macro sections spanning exit-frame → entry-frame between two main-line sections (the NFS shortcut design and merge-zone rules from the current system port over conceptually intact). *Done when: feature parity with today's generator, minus nothing.*

Each phase is independently shippable and legacy mode remains selectable until Phase 5 lands.

---

## 9. Open decisions & risks

- **Ramp geometry: in-mesh vs actor-mesh.** Building ramps into the section mesh is cleaner (one collision surface, macro-doc-conformant); keeping the current `RampActor` wedges on flat sections is faster to ship in Phase 2. Recommendation: actor-mesh in Phase 2, in-mesh in Phase 3.
- **Closure solver corner cases.** Very few straights, or straights with near-parallel headings, weaken position closure. Mitigations: grammar guarantees ≥3 straights with heading diversity; radius nudging as a second lever; deterministic re-roll as the backstop. This is the riskiest code — it gets unit-style edit-mode tests (assert closure error < 1 cm across 1,000 seeds).
- **Section-boundary collision seams.** Physics can catch on coincident collider edges. Mitigation: exit/entry rings share *identical* vertex positions by contract; if the hovercraft still finds seams, add a 2 cm collider-only overlap lip (same trick as the shortcut weld overlap).
- **Elevation + banking interacting at joins.** Contract says sections meet at bank 0 / pitch matched by default; curves that *want* to hand off banked-to-banked (compound corners) are a Phase 3+ feature via explicit frame negotiation, not an accident.

---

## 10. Summary

```
Grammar decides the track. Solver closes the loop. Prisms build the road.
One GameObject per section — editable, themeable, cullable, bakeable.
Legacy spline path stays until the new brain provably wins.
```
