# Track Generator V2 — Roadmap & Task Board

Living status document. Source of truth for scope: `TRACK_GENERATOR_V2_SPEC.md`.
Detail for the wall work: `TRACK_GENERATOR_V2_1_PLAN.md`. Audit: `TRACK_GENERATOR_STABLE_AUDIT.md`.

---

## Where we are

```
V2.0 Baseline            ██████████ DONE      frozen 8-track corpus, determinism green
V2.1 Wall stability      █████████░ 90%       fix verified; needs rebaseline + visual sign-off
V2.1b Curvature flow     ████░░░░░░ AUDITED   semantic shadow audit live; legacy geometry still authoritative
V2.3b Generation Recipe  ██████████ DONE      exact replay + hashes + save/load manually verified
V2.2 Vertical profiles   █████████░ 90%       Subtle / Balanced / Extreme implemented; Unity calibration/sign-off remains
V2.2b Spatial intent     ░░░░░░░░░░ IDEATION  ratio-based layout/elevation volume; deliberately not implemented yet
V2.3 Inspector V2        █████████░ 90%       primary workflow redesigned; retained advanced groups remain reachable
V2.3c Topology editing   █████████░ INTEGRATED preview + automatic route rebuild + transactional apply + exact-recipe overrides; Unity sign-off remains
V2.4 Feature Min/Max     ██████████ DONE      controls, planner/validator enforcement and focused acceptance matrix complete
V2.5 Materials           █████████░ 90%       persistent role contract integrated; Unity visual/domain-reload sign-off remains
V2.6 Stability pass      ██████░░░░ 60%       ground clearance + timing + bounded seed audit integrated; hard-gate/corpus work remains
V2.7 Definition system   ██████████ DONE      28/28 pattern assets + 2 selectable semantic assets; typed editor and coverage gate
V2.7 Advanced features   ███████░░░ OPT-IN    Camelback, Cutback, 2 transported rolls and 2 moderate inversions integrated; drive sweeps remain
V2.8 Polish              ░░░░░░░░░░
V2.9 Performance         ░░░░░░░░░░           includes the Apply-Presets crash
V2.10 Stable validation  ░░░░░░░░░░
```

**Two product goals from the original brief:** *(a)* clean, intentional-looking tracks —
V2.1 delivered the wall half; *(b)* a designer-facing generator you can drive without knowing the
algorithm — V2.2/V2.3/V2.4 deliver that.

---

## 0. Close out V2.1 (do first — small)

| # | Task | Why | Who |
|---|---|---|---|
| 0.1 | Run suite; confirm the only `Frozen_*` failures are **hash mismatches**, not "no longer generates" | Distinguishes intended geometry change from a real break | You |
| 0.2 | Run `[Explicit] RebaselineFrozenCorpus`, paste block → I commit new hashes | Re-arms the regression net | You + me |
| 0.3 | Confirm `PassThroughSectionsDoNotCreateIncidentalWallValleys` is green | Locks the fix in | You |
| 0.4 | **Visual sign-off**: walls now hold height through short connectors instead of dipping ~2 m | Only you can judge if it *reads* right | You |

---

## 1b. V2.1b — Curvature flow through connectors  ← one of the core V2 reasons

**The complaint:** `Left curve → tiny straight → same-direction left curve` reads as one continuous
turn but the connector forces curvature to zero. Measured example: a **38 m** (≈0.1 s at 361 m/s)
straight *inside* a span the generator itself labels `SameDirectionTurnBridge / TurnComplex_1`.

**Why it happens:** `ConnectorAnalyzer` already detects the case and absorbs it into a turn complex,
but absorption carries `"bank, outside wall support, depth, turn rounding"` — **surface properties
only**. `SectionType` stays `Straight` → `BuildStraight` → curvature exactly 0. Same architectural
bug as the V2.1 SideHeight valley, one layer down (path instead of profile). Continuity today:
C0 ✓, C1 ✓, **C2 ✗**.

**Why it is NOT trivial:** those straights are the **closure solver's levers**
(`RunActiveSetSolve` adjusts straight lengths + corner radii). The code already limits removal —
`CanDropLeadStraight` permits at most one drop per quarter — because dropping levers starves closure.
**Any change here must be judged on generation yield, not only on appearance.**

**Split into two stages so the safe half can land early:**

| # | Stage | Risk | Task |
|---|---|---|---|
| 1b.1 | **Diagnostics only** | **none** | Curvature-flow block in the report: incoming/connector/outgoing curvature, shortest same-direction straight, dκ/ds spike at joins, count of unnecessary zero-curvature interruptions. Quantifies how often and how short |
| 1b.2 | Geometry | **medium** | Opt-in, capacity-guarded plan-time corner merge (45°+straight+45° → one 90° corner), following the existing "Stage D opt-in capacity-guarded lead-straight drop" precedent. Gate on yield + frozen corpus |

**Files (1b.2):** `TrackTopologyPlanner`, `ConnectorAnalyzer`, possibly `SectionFrameBuilders`.
**Not** `CrossSectionPlanner` — that work is done. Long intentional straights stay straight; only
connectors below the bridge window between same-direction turns are candidates.

---

## 1. V2.2 — Vertical profiles (Subtle / Balanced / Extreme)

The second half of the "intentional-looking tracks" goal. Entry point is already identified:
`TrackTopologyPlanner.AssignElevation` (~`:2754`), gated by `TargetElevationAmplitude`.
`VerticalProfilePlanner` (realisation) is **not** touched.

| # | Task |
|---|---|
| 1.1 | Add `GenerationProfile` enum (Subtle / **Balanced** / Extreme) + serialized field, defaulting to Balanced on existing assets |
| 1.2 | Profile → vertical-intent mapper (amplitude, major-event count, sustained climb/drop length, level-recovery tendency). Quality ceilings (`MaxClimb/DropAngle`, curvature) are **never** raised by profile |
| 1.3 | Two new intent fields: sustained climb/drop length + rhythm/recovery, with safe deserialization defaults |
| 1.4 | Calibrate **Balanced ≈ today's output** against the frozen corpus (this is the acceptance test) |
| 1.5 | Diagnostic: vertical silhouette metric (elevation range, major-event count, longest sustained grade) in the report |
| 1.6 | Tests: Balanced reproduces baseline; Subtle is gentler but **not flat**; Extreme is bigger with **zero** curvature/slope violations; profiles independent of feature counts |

**Risk:** Extreme requesting infeasible amplitude → rejects. Mitigate by allocating distance, not by
raising limits. Watch yield.

**Implemented checkpoint (2026-08-24):** the Track Profile card now exposes an **Elevation** choice
with Subtle, Balanced and Extreme. Balanced is serialized as the compatibility default and feeds the
historical planner constants exactly. The two other profiles change only vertical design intent:
target amplitude, major carrier count, preferred sustained-grade length and level-recovery rhythm.
They do not own or raise climb angle, drop angle, curvature, clearance or closure limits.

The resolved profile is part of the designer settings captured by Exact Recipe, so Save, Load and
Exact Replay preserve it. Exported debug reports now include both the requested/effective vertical
intent and a measured **Vertical Silhouette** block: actual elevation range, planned major carrier
count and longest continuous grade. Continuous-grade measurement stops at air gaps, road changes,
open/non-welded boundaries, flat spans and grade reversals.

Runtime and editor assemblies compile with zero C# errors. Focused policy, fallback, clamping,
recipe round-trip and silhouette-measurement tests are present. Remaining acceptance is deliberately
empirical: refresh Unity, run `V2FastGate`, then compare reports and generation yield for the same
profile/size/feature settings across Subtle, Balanced and Extreme. Balanced must remain the visual
and deterministic compatibility baseline.

---

## 1c. V2.2b — Spatial Intent Volume (future investigation stage)

**Status:** concept accepted for investigation only. Do not begin geometry implementation until the
designer contract, scale-resolution rule and visual workflow have been reviewed together.

**Product goal:** give the designer a tunable Scene-view volume that expresses the intended spatial
character of a generated track — its footprint, orientation and elevation usage — without becoming
a manual spline editor. The volume guides the planner; the generator remains responsible for legal,
connected and driveable geometry.

### Designer contract

| Setting | Contract |
|---|---|
| **Minimum Track Length (km)** | Hard lower bound for an accepted track |
| **Maximum Track Length (km)** | Hard upper bound for an accepted track |
| **Volume Ratio X:Y:Z** | Proportional layout shape only; never exposed as absolute world size |
| **Height Influence** | Soft 0–100% control over how strongly the planner uses the available vertical character |
| **Volume Orientation** | Designer-controlled world orientation, persisted in the recipe |

Track length is **not suggestive**. A candidate outside the authored minimum/maximum kilometer range
must fail validation. Setting Min equal to Max remains available when near-exact length is desired,
subject only to a documented mesh-sampling tolerance.

The volume is ratio-based rather than measured in kilometers or Unity units. Because ratios plus a
length range do not define one unique physical scale, V2 must first specify a deterministic
**automatic comfortable-scale solver**. It should resolve the smallest comfortably usable world
envelope that can satisfy road clearance, feature footprints, curve radii, transitions, connectors
and closure, with a controlled breathing-space margin. An optional dimensionless Compact ↔ Expansive
control may be considered later only if automatic scale resolution is not artistically sufficient.

Height Ratio and Height Influence are separate concepts: Height Ratio defines the proportional
vertical capacity of the resolved envelope; Height Influence controls how strongly the route plan
tries to explore that capacity. Height Influence may affect elevation-event count, high/low region
separation, crossing strategy and feature-level placement, but it must never raise slope, curvature,
transition or driveability limits.

### Investigation and implementation gates

| # | Task |
|---|---|
| 1c.1 | Freeze the terminology and hard/soft contract: length range is hard; spatial/elevation intent is soft inside a hard resolved envelope |
| 1c.2 | Design the deterministic comfortable-scale rule and prove that identical recipe inputs resolve identical world dimensions |
| 1c.3 | Prototype an editor-only normalized cage: ratio handles, orientation, elevation bands, Height Influence and read-only resolved dimensions; no generation changes |
| 1c.4 | Define a coarse intent-route representation for broad heading, elevation rhythm, quarter allocation, closure direction and feature opportunities |
| 1c.5 | Add range-aware length budgeting so every planning stage reserves and reports distance while the final track remains within Min/Max km |
| 1c.6 | Feed the coarse route into topology/elevation planning without replacing the authoritative feature, connector, geometry or validation builders |
| 1c.7 | Add containment, clearance, closure, length-range and vertical-intent diagnostics before making the system designer-facing |
| 1c.8 | Extend Exact Recipe with ratios, orientation, Height Influence, Min/Max km, resolved scale and coarse intent plan so replay survives future solver changes |
| 1c.9 | Acceptance: determinism, generation yield, volume containment, length-range compliance, smooth elevation, closure and representative visual review |

**Recommended sequencing:** investigate and visually prototype 1c.1–1c.4 after the current V2
stability work is trustworthy. Do not replace the existing vertical profiles first. The intent volume
must become a precise planning language before elevation generation is adapted to follow it.

---

## 2. V2.3 — Inspector V2

No geometry risk; pure presentation over existing serialized data. High daily value.

| # | Task |
|---|---|
| 2.1 | Top block: Generation Profile → Lock Seed + Seed → **GENERATE NEW TRACK** → status line |
| 2.2 | Seed workflow: unlocked = new seed, **written back** to the box; locked = reproduce exactly |
| 2.3 | FEATURE AMOUNTS block: Min/Max per feature hoisted to the top level |
| 2.4 | Designer Settings foldouts: Track Layout / Vertical / Geometry / Banking / Feature Rules / Validation / Advanced Generation |
| 2.5 | Move partial-regeneration commands into **Advanced Regeneration** (keep all of them) |
| 2.6 | Materials + Debug/Diagnostics foldouts |
| 2.7 | Verify every V1 command is still reachable; no serialized field renamed (use `[FormerlySerializedAs]` if forced) |

**Implemented checkpoint (2026-08-23):** the primary Inspector now separates Track Profile,
Feature Amounts, Generation, Exact Recipe and Track Editor into bounded cards. Feature Amounts
edits the existing serialized `TrackFeatureRule` values directly (no duplicate settings), keeps
advanced weights in the retained settings tree, and explains the explicit-failure behavior.

**Topology editing checkpoint (2026-08-24):** Track Editor is now a three-step designer workflow:
choose a section, preview another design, then **Build & Apply**. Selecting a section frames it in the
Scene view automatically. Previewing creates a non-destructive ghost at the real route location.
Build & Apply sends every queued choice through the authoritative whole-route generation pipeline,
including connector analysis, closure, elevation, geometry and final validation. Connector mismatch
is therefore rebuild work, not a dead-end manual task.

The live track swaps only after the complete edited route succeeds. A failed edit keeps the accepted
track and restores the complete previously accepted recipe/Inspector state. Successful choices are
stored as normalized topology overrides in the Exact Recipe: they change the exact edited recipe
identity while preserving the original base-seed identity. Save, Load and Exact Replay therefore
carry hand-authored choices forward deterministically. Multiple queued edits are shown explicitly and
are applied together by one clearly labelled action.

**Designer-first authoring checkpoint (2026-08-26):** Build & Apply now targets the exact deterministic
attempt that produced the visible accepted track, instead of searching unrelated candidate routes.
The procedural lap-length cap is advisory during this focused rebuild; safe closure straights and
large-radius curves receive additional fitting authority while physical validators remain strict.
Exact Recipe persists the authoring baseline. **Area of Impact is now implemented** with independent
Backward/Before and Forward/After feature reach on the selected quarter route. Zero protects that
side; non-zero reach highlights and names the exact neighboring features that may be consumed. Build
requires an explicit override confirmation, offers direct navigation to edit an affected feature and
shows compatible replacement suggestions. The stable impact identities and accepted removals are
part of Exact Recipe.

The local preview foundation remains deterministic and uses each topology family's authoritative
builder: turn realizations use the corner emitter, while heading-neutral and direction-neutral
features use the feature-pattern registry. The focused V2 gate now also covers exact-recipe override
serialization, normalization and stable identities. Runtime and editor assemblies compile cleanly;
the original focused authoring path has passed its in-Unity gate and manual Build & Apply → Save →
Load → Exact Replay confirmation. Area of Impact and original-feature restoration now require the corresponding 66-case gate and one
manual directional-window application.

Topology slots describe route opportunities, not permanent feature families. A heading-neutral slot
that currently contains an inline corkscrew may legitimately become a jump gap, loop, pipe or other
heading-neutral realization when its surrounding route provides a safe fit. The owned-connector
solver must therefore measure and, when permitted, consume eligible adjacent straight/connector
capacity while preserving the unchanged outer entry and exit boundaries. A candidate is rejected
for insufficient runway only after that local route window is measured; the size of the current
feature mesh alone is not a compatibility verdict.

---

## 2b. V2.3b — Generation Recipe (reproducible design, not just a random number)

A bare seed only reproduces a track when every setting **and the generator version** match — proven
during V2.0 when changing `MaxAttempts` 200 → 80 made frozen seeds "no longer generate". No geometry
risk; pairs with the V2.3 seed workflow.

| # | Task |
|---|---|
| 2b.1 | `TrackGenerationRecipe` asset/JSON: seed + per-stream seeds, generator version, profile, feature rules, full designer settings, start position/orientation, lock state, settings hash |
| 2b.2 | Save current generation as a recipe; restore a recipe exactly |
| 2b.3 | Version + settings-hash mismatch detection → clear warning instead of a silently different track |
| 2b.4 | Reuse existing pieces (`TrackSeedManager` hash, `TrackSeedStreams`, `SettingsLockState`, `TrackDesignerSettings.Clone`) — do not duplicate |
| 2b.5 | Test: save → mutate settings → restore reproduces the original exactly |

---

## 3. V2.4 — Feature Min/Max enforcement

| # | Task |
|---|---|
| 3.1 | UI Min/Max → placement targets (data model `TrackFeatureRule{Min,Max}` already exists) |
| 3.2 | Enforce minimums; **explicit failure** (`RequiredFeatureMissing`) when impossible — never silent violation |
| 3.3 | Confirm maximums respected by placement |
| 3.4 | Tests: `Min=Max=2` → exactly 2 or explicit failure; `0/0` → none; impossible min → clear reason |

**Completed implementation:** placement and final validation enforce minimums and maximums;
impossible requirements report `RequiredFeatureMissing`, and the designer-facing Min/Max table is
hoisted into the primary Inspector. Focused acceptance covers exact `2/2`, disabled `0/0`, authored
range preservation while disabled, and an impossible minimum with its explicit structured failure
reason. The broader generation regression continues to guard maximum-count enforcement.

---

## 4. V2.5 — Material simplification

| # | Task |
|---|---|
| 4.1 | `TrackMaterialSet`: RoadSurface, WallSide, GuideMarking, WallMarker, BoostSurface, RaceGate, StartFinish, StartGatePillar, Checkpoint |
| 4.2 | Assign from the set in mesh/marking/boost/gate builders |
| 4.3 | Remove runtime `new Material` from the normal path (`TrackGuideMarkingBuilder:581`, `RaceCourseBuilder:341-366`, `BoostPadActor:108`) |
| 4.4 | Retire generator-side material **repair** (`TrackGenerator:1042/1060`) → editor-only warning if a ref is missing |
| 4.5 | Test: materials survive a domain reload / scene rebuild |

**Implemented checkpoint (2026-08-24):** `TrackMaterialSet` is now the persistent source of truth
for all normal track, marking, boost and race-course roles. Builders consume one resolved material
contract, while the legacy serialized references remain as a migration fallback for existing scenes.
Cached previews and Exact Replay reapply the resolved roles instead of trusting transient renderer
state. Missing required roles are shown in the Inspector and exported debug report.

The editor command/Inspector action creates or repairs persistent assets under
`Assets/Materials/TrackGeneration`. Repair reconnects the palette and creates only missing assets;
it deliberately preserves the shader, textures, colors and emission of every existing authored
material. The normal generation path no longer creates ad-hoc materials. Focused tests cover role
precedence, migration fallback, designer-facing missing-role diagnostics and AssetDatabase reload
persistence. Remaining acceptance is a Unity domain reload plus visual review of road, wall, guide,
wall-marker, boost, start/finish, pillars and checkpoints.

---

## 5. V2.6 — Generation stability pass

| # | Task |
|---|---|
| 5.1 | **Fix #6** — track spawning below ground (`KeepAboveStart` y<0) |
| 5.2 | Promote cross-section transition guard to a hard validator (already a test; decide if it gates candidates) |
| 5.3 | Selective sequence-contract enforcement (`TransitionResolver` advisory → gate) — corpus-gated, WARNING→ERROR |
| 5.4 | Instrumentation: failure rate, dominant rejection reasons, generation time, strict vs relaxed pass usage |
| 5.5 | 100-seed sweep harness with the automated quality checks from the spec |

**Implemented checkpoint (2026-08-24):** the below-ground placement defect is protected by a
renderer-bounds measurement with a centreline fallback and a focused regression test. Generation
reports now record wall-clock duration, pipeline-pass count and the accepted pass. The bounded V2
stability window runs deterministic planning-only seed sequences without touching the scene, obeys
a scheduling time limit, can stop on the first failure, and writes a reproducible report containing
per-seed timing, strict/non-strict acceptance, dominant rejection reasons and failing seed IDs.

The bounded audit intentionally defaults to a small overnight-safe sample rather than invoking the
historically runaway full corpus. Remaining work is to promote only evidence-backed cross-section
and sequence checks into hard gates, then expand the accepted corpus/sweep toward 100 seeds after
yield and timing remain healthy.

---

## 6. V2.7 — Advanced feature stabilization

| # | Task |
|---|---|
| 6.1 | **#36 dual-road yield** — the real fix is vertical-separated lanes (road B above/below road A) so it stays short and balanced. Tolerance widening is already available as a stopgap |
| 6.2 | #13/#15 directional corkscrew — finish + enable live selection |
| 6.3 | #14 dual-quarter fitter attempt budget |
| 6.4 | #16 corkscrew floor bulge / ring-density protection |

**Note:** no new spectacle features in V2 — make the existing ones reliable.

---

## 7. V2.8 — Polish · 8. V2.9 — Performance · 9. V2.10 — Stable validation

| # | Task |
|---|---|
| 7.1 | Labels, tooltips, error messages, profile clarity, report presentation |
| 8.1 | **Fix #28** — Apply Presets → Generate crash (OOM / native collider cook). *Workflow blocker* |
| 8.2 | Collider cook / restore cost (~8.7 M tris per restore is documented in the mesh builder) |
| 8.3 | Optimise only profiled hot paths |
| 9.1 | 100-seed sweep: success rate, rejection reasons, time, quality invariants, determinism |
| 9.2 | Visual review of a representative sample → V2 Stable |

---

## Open defects (tracked outside stage order)

| id | what | severity | lands in |
|---|---|---|---|
| #28 | Apply Presets → Generate crash | **blocks a workflow** | V2.9 (pull earlier if it bites) |
| #6 | Spawn below ground | correctness | V2.6 |
| #36 | Dual-road yield ~0 with Min≥1 | feature unusable | V2.7 |
| #13–16 | Stage F in-flight items | incomplete | V2.7 |

---

## Recommended order & rationale (REVISED)

**Principle: safe work early, geometry-risk work only once the safety nets and workflow tools
exist.** V2.1b is split so its zero-risk half lands early — the connector complaint gets measured
immediately without doing closure surgery before we can test it comfortably.

| # | Work | Risk | Why here |
|---|---|---|---|
| 1 | **Reality-check workspace vs docs** | none | ✅ done — 8 seeds frozen, V2.1 fix present, all files balanced |
| 2 | **Close V2.1**: rebaseline (`RebaselineFrozenCorpus`, no scanning) + visual sign-off | none | Never leave the regression net disarmed |
| 3 | **#28 Apply-Presets crash** — *confirm still reproducible first* | med | A workflow-blocking crash should not sit under several new systems |
| 4 | **V2.1b Stage 1** — curvature-flow diagnostics | **none** | Addresses a core V2 reason immediately, with data instead of guesswork |
| 5 | **V2.3 Inspector + V2.3b Generation Recipe** | none (presentation/serialisation) | Biggest daily-workflow win; makes everything after it easier to drive and reproduce |
| 6 | **V2.4 Feature Min/Max** | low | Completes the designer control surface |
| 7 | **V2.2 Vertical profiles** | med (geometry, self-contained) | Headline product feature; feeds intent to an existing planner |
| 8 | **V2.1b Stage 2** — connector merge | med (**topology/closure**) | Now measurable (step 4), testable (step 5), on a verified baseline |
| 9 | V2.5 materials → V2.6 stability (+#6) | | Establish a trustworthy generation baseline before adding another global planning layer |
| 10 | **V2.2b Spatial Intent Volume investigation** | med/high (layout + elevation planning) | First freeze the ratio, scale and Min/Max-length contracts; prototype the visualizer before changing generation |
| 11 | V2.7 advanced (+#36 dual roads) → V2.8/9/10 | | Riskiest subsystem work last, with the strongest nets |

**Changes from the first draft, and why:** V2.1b was missing entirely (a core V2 goal) — now
explicit and split; the crash moved from last to third; Inspector/Recipe moved ahead of vertical
profiles because they carry no geometry risk and make later stages easier to operate and reproduce.

---

## Definition of V2 Stable

Select **Subtle / Balanced / Extreme**; set feature Min/Max; optionally lock a seed; assign project
materials; press **Generate New Track** → a clean, stable, vertically interesting track with smooth
section transitions and predictable quality — **without needing to understand the algorithm**.
Measured by: 100-seed sweep with no quality-invariant violations, deterministic reproduction,
zero crashes, and a passing visual review.

---

## Working agreements (learned the hard way)

- A running EditMode test **blocks domain reload** — editing code cannot cancel a stuck run.
- Never run `[Explicit] DiscoverAndFreeze` on an unfrozen entry (it scans; it took 3.5 h once).
  Re-baseline with `RebaselineFrozenCorpus` — pinned seeds, no scan.
- Measure before implementing. It caught two wrong inferences of mine in V2.1 alone.
- Geometry changes shift candidate selection, so a per-seed before/after is not always the same
  track — compare span structure before concluding.
