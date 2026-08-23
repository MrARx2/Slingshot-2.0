# Track Generator V2 — Specification & Implementation Plan

**Status:** SPEC ONLY. No production code has been modified. This document transforms
`TRACK_GENERATOR_STABLE_AUDIT.md` + the V2 product priorities into the authoritative V2 plan.
Await approval before V2.0.

**Branch:** `Ariel-TrackGeneratorV2`. **Backup taken:** `_Backups/TrackGeneration_V1_Backup.zip`
(complete snapshot of `Assets/Scripts/TrackGeneration`, 183 files) before any change.

**Evidence base:** the two supplied reports (`TrackReport_-1813449219`, `TrackReport_1651702061`)
plus direct code reads this pass. Confidence labels: CONFIRMED / HIGH / POSSIBLE / VERIFY.

---

## 1. V2 Mission

Make **"Generate New Track" produce a clean, stable, intentional-looking, vertically interesting
track the overwhelming majority of the time** — without the designer needing to understand the
procedural algorithm. V2 is **stabilisation + refinement of the existing generator**, not a rewrite.
The five focus areas: (1) clean section transitions / no wavy walls, (2) deliberate vertical
composition, (3) a clean designer Inspector, (4) understandable feature Min/Max, (5) generation
quality that prefers rejection over ugly geometry.

**Definition of done (target):** *"I select Subtle / Balanced / Extreme; set how many loops,
corkscrews, wallrides, jumps, etc. I want; optionally lock a seed; assign our materials; press
Generate New Track; and get a clean, stable, vertically interesting track with smooth transitions
and predictable quality — without understanding the internals."*

---

## 2. Non-Negotiable V2 Requirements

1. Wall/section-transition waviness fixed **in the field architecture**, not patched per-join.
2. Ordinary track (not just stunt features) has a real vertical silhouette
   (climb→crest→descent→valley→level→climb).
3. Exactly three vertical **profiles**: Subtle, Balanced (default), Extreme — same generator, same
   quality rules; Extreme allocates distance for smoothness, never adds kinks.
4. Profiles and feature counts are **independent**; feature Min/Max is authoritative designer intent.
5. A clean Inspector: Profile → Seed(+Lock) → Generate → Feature Min/Max → grouped Designer
   Settings → Materials → Debug/Advanced. Advanced regeneration preserved but demoted.
6. Materials are **supplied**, not generated; remove generator-side material creation/repair from
   the normal path.
7. Quality over yield: reject bad candidates with explicit reasons; but don't make normal configs
   fail to generate.
8. Determinism preserved exactly. Serialized data preserved (no casual field renames).

---

## 3. Systems That Must Be Preserved (locked by approval)

CONFIRMED strong; keep unless direct evidence blocks a V2 goal:

- `TrackConnectionFrame` as the single geometric source of truth (exit≡entry invariant).
- Deterministic **per-subsystem seed streams** (`TrackSeed`/`TrackSeedStreams`/`PlanRandomStreams`,
  `Unity.Mathematics.Random`).
- **plan → build → validate → score → select** pipeline (`TrackGenerationPipeline.Run`).
- Quaternion + **unwrapped** orientation transport (`AccumulatedRoadRoll`/`AccumulatedVerticalRotation`).
  *Centralisation allowed; replacement not desired.*
- `VerticalProfilePlanner` (chain-level, exact arc-length κ, mesh-independent validation, weld-exact).
  *Improve the intent feeding it.*
- `CrossSectionPlanner` — *the wall fix goes through/around this architecture, not a mesh patch.*
- `RotationalEvent` unified loop/corkscrew/spiral/half-loop model.
- **Whole-candidate** generation/rejection recovery (no per-section backtracking unless yield data
  demands it).
- Structured failure reasons (`GenerationFailureReason`, 24) + exportable report.
- Separate planning (pure data) vs mesh realisation.
- Separate coarser colliders with PhysX chunking.

---

## 4. V2 Architecture (delta over V1)

Same architecture; four surgical additions and one extraction. Nothing in the pipeline shell
changes shape.

```
Authoring: TrackDesignerSettings (10 groups) + NEW GenerationProfile enum + NEW TrackMaterialSet ref
    ↓ resolve
ResolvedTrackGenerationConfig (+ profile-applied vertical intent; + material set carried through)
    ↓
Determinism: seed streams (UNCHANGED)
    ↓
Planning:
  TrackTopologyPlanner
    └ elevation walk  ← NEW: profile-driven vertical intent (amplitude, majors, sustained length, rhythm)
  TrackCandidateBuilder
    ├ ApplyGlobalBankingField   ─┐  V2.1: these channels become
    ├ catch-wall / overhang pass ─┤  ONE unified cross-section field
    ├ CrossSectionPlanner        ─┘  with a JOINT composed-wall-top rate limit
    ├ VerticalProfilePlanner (UNCHANGED core)
    └ TrackFrame.Transport (NEW shared helper; refactor only)
  TrackValidators (+ NEW cross-section transition-quality ERROR; + feature Min/Max enforcement)
  Scoring/Selection (+ small vertical-silhouette term)
    ↓
Realisation:
  BoxPrismTrackMeshBuilder / GuideMarking / RaceCourse  ← assign SUPPLIED materials (no new Material)
    ↓
TrackGeneratorHost (SLIMMED MonoBehaviour) + NEW TrackSceneLifecycle (extracted repair/reconcile/place)
    ↓
Inspector V2 (TrackGeneratorEditor rewrite — presentation only, same serialized data)
```

The only genuinely new code: `GenerationProfile` (enum + a small parameter mapper), a **unified
cross-section field pass** (or a joint limiter layered over the existing passes), a
`TrackMaterialSet`, and a shared `TrackFrame.Transport`. Everything else is *wiring existing data
and reorganising the Inspector*.

---

## 5. Cross-Section / Wall Transition Strategy (Priority 1)

### 5.1 Root cause (CONFIRMED from code + reports)

The visible **wall-top is a composed vector** driven by several `TrackConnectionFrame` channels
that are written by **different passes with different smoothing laws** and are **never jointly
rate-limited**:

| Channel | Written by | Smoothing today |
|---|---|---|
| `BankAngle`, floor tilt | `ApplyGlobalBankingField` (`TrackCandidateBuilder.cs:535`) | blurred field, **253 m** radius, double-box (C1) |
| `LeftWallSuppression`/`RightWallSuppression`, `TurnRounding`, `LeftOverhang`/`RightOverhang` | same banking field pass (`:1066-1075`) | same blur |
| `Width`, `SideHeight` | `CrossSectionPlanner.Apply` (`:34`) | per-channel **scalar** rate limit (width ≤0.12, side-height ≤0.08), **hard-locked** on protected shapes (`IsProtected :188`) |
| `WallrideMorph`, `PipeClosure` | feature builders (authored) | locked anchors |

Two concrete failure mechanisms, matching the reports:

1. **Per-section-type depth targets oscillate.** `CrossSectionPlanner` sets each ring's target
   side-height from `DepthMultiplier(SectionType)` (`CrossSectionPlanner.cs:129,174`): banked
   curves push a boosted ~28.1 m wall, neutral straights sit at 26.0 m. A short connector between a
   banked curve and a feature-approach reaches the neutral 26 m and rebounds — the report's
   *"depth 28.1/26.0/28.1 dip 0.94/0.96"* connector dips (Report 1 [003],[005],[027]; Report 2
   [006],[010],[013],[030]). The rate limiter faithfully tracks an **oscillating target** — the
   field is continuous but the *targets* wave. `BridgeCarry` only partly counters this and only for
   non-protected, non-zero-carry sections (`TargetMultiplier :171-186`).
2. **Composed wall-top spikes at locked features.** At a wallride, `WallrideMorph`/overhang is
   authored and **locked** in `CrossSectionPlanner`, while the banking field independently boosts
   the wall on the same rings. Their sum exceeds the 0.12 m/m wall-top bound — Report 1's
   **0.220 m/m at `Wallride_90deg_Left`** (rings 127-133, 632-639). Because each channel is limited
   *separately*, nothing limits their *vector sum*.

Additional: the report bound (0.12 m/m) measures the **built wall-top vector** (side-height ×
suppression multiplier, rotated by bank + rounding + overhang), whereas `CrossSectionPlanner` limits
only the **scalar side-height** (≤0.08) and width (≤0.12). So the planner can be "within limits"
per channel while the measured composite exceeds bound. (This is the audit's transported-wall-top
vs scalar-side-height point, now confirmed against live geometry.)

### 5.2 V2 strategy — one continuous composed field

Principle: **the cross-section behaves as one continuous field; a boundary must not announce itself
through wall oscillation.** Approach, in order of preference:

1. **Unify the depth target.** Replace per-section-type `DepthMultiplier` targets with a **single
   continuous depth/width intent field** sampled per ring, so neutral connectors between two
   elevated-wall sections **inherit the held wall depth** instead of dropping to neutral and back.
   The banking field already proves a lap-wide blurred field works; extend that pattern to
   side-height/width so all cross-section channels come from **one blurred+rate-limited field
   source**, with feature anchors as hard points the field eases into (not steps into).
2. **Joint composed-wall-top limiter.** After all channel passes, compute the **built wall-top
   vector** per ring (the same quantity the report measures) and rate-limit the *composite* (lateral
   + vertical) — not each scalar channel independently. Feature edges get an **eased approach band**
   so the transition *into* a locked wallride/pipe is spread over distance on the ordinary-road side
   (the feature interior stays authored). This directly removes the 0.22 m/m wallride spike.
3. **Single transition length.** Banking blur radius (253 m) and cross-section transition length
   currently differ; unify them (or make them share a max) so bank and depth ease over the **same**
   distance and cannot beat against each other.
4. **Retire the legacy adapter.** `ApplyCrossSectionBlend` (`TrackCandidateBuilder.cs:1141`) is now
   a 2-line pass-through to `CrossSectionPlanner`; fold it in during V2.1 (confirmed dead wrapper).

### 5.3 New metric/validator (V2.1 acceptance)

Add a **cross-section transition-quality** diagnostic + validator: per ring, the composed wall-top
lateral rate, vertical rate, combined magnitude rate, and 2nd derivative, in the parallel-transported
frame (already specified in the audit). Category limits: ordinary road (target ≤0.12 m/m composite,
tuned from measurement), wallride-prep / active-wallride / pipe / recovery separate. Promote from
report-only to **ERROR** once the golden corpus still passes (§14). Explicitly test the four cases
the brief names: normal→banked→straight; normal→wallride→normal; normal→pipe→normal; wide→standard.

---

## 6. Vertical Profile Architecture (Priority 2)

### 6.1 Where profiles enter (CONFIRMED)

Vertical intent is decided in the **topology elevation walk**, `TrackTopologyPlanner.AssignElevation`
(~`:2754`, *"major climbs and drops (a level walk that must return to zero), then bridges,
underpasses and crests"*), gated by `TargetElevationAmplitude` (`:2773`). The realisation is already
smooth and quality-bounded by `VerticalProfilePlanner`. **Profiles therefore feed the intent
(topology), never the geometry (planner stays untouched).** This matches the brief exactly.

The intent parameters already exist in `TrackElevationSettings` (`TrackDesignerSettings.cs:390`):
`TargetElevationAmplitude` (0-1200), `MinMajorElevationSections`/`MaxMajorElevationSections` (0-16),
`MaxClimbAngle`/`MaxDropAngle`, `Crests`/`Bridges`/`Underpasses` (`TrackFeatureRule`),
`AllowDipBelowStart`, ground-level policy. V2 adds two intent controls: **sustained
climb/drop length** (so Extreme allocates distance for smooth big climbs) and **vertical rhythm /
level-tendency** (spacing/recovery between majors).

### 6.2 `GenerationProfile` = a parameter preset (not a new generator)

```
GenerationProfile ∈ { Subtle, Balanced (default), Extreme }
        ↓ maps to vertical intent parameters:
  TargetElevationAmplitude
  Min/MaxMajorElevationSections   (frequency of major elevation events)
  SustainedClimbDropLength        (NEW intent: distance allocated per major)
  VerticalSeparationBias          (existing separation logic)
  LevelRecoveryTendency           (NEW intent: rhythm/recovery between majors)
  MaxClimb/DropAngle              (KEEP as quality ceiling — profile may LOWER, never raise past rulebook)
```

The profile writes these onto the resolved config's vertical intent **before** the elevation walk.
It does **not** touch curvature/slope/smoothness limits (those stay rulebook-clamped for all
profiles). Extreme requesting a bigger amplitude simply asks the walk to **allocate more track
distance**; `VerticalProfilePlanner` then produces it smoothly or the candidate is rejected — no
kinks.

### 6.3 The three profiles (starting parameter intent — tune in V2.2 against the corpus)

| Parameter | Subtle | **Balanced (default)** | Extreme |
|---|---|---|---|
| Character | clearly 3D, gentle | current Slingshot rollercoaster baseline | aggressive rollercoaster silhouette |
| Target amplitude | low (e.g. ~0.3× Balanced) | current default (baseline) | high (e.g. ~1.8-2.5× Balanced, ≤1200 cap) |
| Major elevation events (min-max) | fewer | current | more frequent |
| Sustained climb/drop length | longer level runs, gentler | balanced | longer sustained verticals, deeper valleys / higher peaks |
| Level-recovery tendency | higher | balanced | lower (more continuous vertical movement) |
| Climb/drop angle ceiling | may be gentler | rulebook | **same** rulebook ceiling (NOT raised) |
| Quality rules | identical | identical | **identical** |
| Features (loops/jumps/…) | independent Min/Max | independent | independent |

**Balanced must reproduce the current intended baseline** (calibrate so a Balanced generation with
today's default settings ≈ today's output). **Subtle ≠ flat.** **Extreme ≠ relaxed quality.**
Profiles carry at most a *very small* optional secondary feature bias — default none; feature Min/Max
stays authoritative (Priority requirement).

---

## 7. Inspector V2 Specification (Priority 3)

Rewrite `TrackGeneratorEditor` presentation only (no serialized-data change). Target hierarchy:

```
TRACK GENERATOR V2
────────────────────────────
GENERATION
  Generation Profile:  [ Balanced ▼ ]        (Subtle / Balanced / Extreme)
  ☐ Lock Seed
  Seed: [ 482914 ]                            (updates to the seed actually used after Generate)
  [           GENERATE NEW TRACK           ]
  (status line: last result — success/score/attempts, or failure reason)

FEATURE AMOUNTS   (Min / Max per feature)
  Loops        Min [0] Max [3]
  Corkscrews   Min [0] Max [2]
  Wallrides    Min [0] Max [3]
  Jumps        Min [0] Max [3]
  Spirals      Min [0] Max [2]
  Half-loops   Min [0] Max [2]
  Pipes        Min [0] Max [1]
  Hairpins     Min [0] Max [1]
  Chicanes     Min [0] Max [1]
  S-Curves     Min [0] Max [2]
  Dual-road quarters Min [0] Max [2]
  Crests       Min [0] Max [4]      (net-zero hills)

DESIGNER SETTINGS   (foldouts)
  ▶ Track Layout       (Scale + Layout)
  ▶ Vertical Layout    (Elevation + profile overrides)
  ▶ Track Geometry     (Road)
  ▶ Banking            (Corners.Banking)
  ▶ Feature Rules      (Features advanced + Quarters)
  ▶ Validation         (quality/clearance limits)
  ▶ Advanced Generation(Generation: attempts, selection, relaxation)

MATERIALS
  ▶ Track Material Set  (single ScriptableObject/asset reference)

DEBUG / DEVELOPMENT
  ▶ Debug Tools         (visualization toggles, export report)
  ▶ Diagnostics         (mesh/geometry diagnostics)
  ▶ Advanced Regeneration (same-layout/new-content, new-features-only, surface-only, …)
```

The exact visuals may differ; **hierarchy and usability are the requirement**. Most designers only
touch Profile, Seed, Generate, Feature Amounts.

---

## 8. Seed Workflow Specification

Expose `☐ Lock Seed`, `Seed [ ]`, `[ GENERATE NEW TRACK ]`.

- **Lock Seed OFF:** Generate mints a **new** seed, runs, and **writes the used seed back into the
  textbox** (so a good track's seed is immediately visible/savable). Maps to the existing
  `GenerateNewEverything` path + display the used `TrackSeed.BaseSeed`.
- **Lock Seed ON:** Generate re-runs the **exact seed shown**; same seed + settings ⇒ identical
  track (existing determinism guarantee, `RegenerateSameSettings`/`GenerateTrack` with fixed seed).
- No other seed controls in the main UI. Determinism unchanged (CONFIRMED preserved).

---

## 9. Feature Min / Max Specification (Priority 4)

**Data model already exists** — `TrackFeatureRule { bool Enabled; int Min; int Max; float Weight }`
(`Design/TrackFeatureRule.cs`), one per feature in `TrackFeatureSettings`
(`TrackDesignerSettings.cs:444`), plus `Crests/Bridges/Underpasses` in `TrackElevationSettings` and
dual-quarter count in `TrackQuarterSettings`. The reports confirm the live feature set:
`wallrides, pipes, jumps, loops, corks, spirals, halfloops, hairpins, chicanes, scurves`. V2 does
**not** invent controls — it surfaces `Min`/`Max` prominently and moves `Weight` to advanced.

Mapping (UI feature → existing rule → semantic system):

| UI control | Serialized rule | Semantic element(s) |
|---|---|---|
| Loops | `Features.Loops` | `VerticalLoop`, `LoopToCorkscrew` |
| Corkscrews | `Features.Corkscrews` | `InlineCorkscrew`, `DoubleCorkscrew`, `DirectionalCorkscrew` |
| Wallrides | `Features.Wallrides` | `WallrideTurn` |
| Jumps | `Features.Jumps` | `JumpGap`, `JumpToBankedLanding` |
| Spirals | `Features.Spirals` | `Spiral`, `SpiralToCorkscrew` |
| Half-loops | `Features.HalfLoops` | `Immelmann`, `HalfLoopToCorkscrew` |
| Pipes | `Features.Pipes` | `FullPipe` |
| Hairpins | `Features.Hairpins` | `Hairpin`, `SweeperIntoHairpin` |
| Chicanes | `Features.Chicanes` | `Chicane` |
| S-Curves | `Features.SCurves` | `SCurve` |
| Dual-road quarters | `Quarters.DualCount` | route-B quarters |
| Crests | `Elevation.Crests` | net-zero hills |

**Rules:** `Min=Max=0` ⇒ none; `Min=Max=N` ⇒ exactly N; `Min=a,Max=b` ⇒ within [a,b]. Generator
satisfies bounds subject to space/compatibility/validity. **Minimums are enforced**: if a minimum
cannot be met, generation **fails with an explicit reason** — the machinery exists
(`RequiredFeatureMissing`, `ValidateRequiredFeatures` `TrackValidators.cs:53`). V2.4 wires the UI
Min into that enforcement and confirms Max is respected by placement. Weight/capacity/length/radius/
risk/recovery stay in **Feature Rules (advanced)** — not removed.

---

## 10. Designer Settings Organization (Priority 5)

Existing settings are **already grouped** into 10 serialized sub-objects
(`TrackDesignerSettings.cs:809-818`). V2 presents them under the new foldouts — **no field renames,
no duplicate serialized data**:

| V2 foldout | Existing source (unchanged serialized data) |
|---|---|
| Track Layout | `Scale` (size, target length, design speed, lap, cap, pacing) + `Layout` (turns, straights, sequence) |
| Vertical Layout | `Elevation` (amplitude, majors, climb/drop angles, crests/bridges/underpasses) + profile overrides |
| Track Geometry | `Road` (flat width, wall height, curve, scale, lip, resolution, rounding, catch wall) |
| Banking | `Corners.Banking` (bankStrength, maxBank, floorTilt) + transition banking |
| Feature Rules | `Features` (advanced per-feature weights/lengths/radii/air-gap objectives/pipe shape/grouping) + `Quarters` |
| Validation | quality/clearance limits (surface from `Generation` + rulebook `TrackConfig` where designer-exposed) |
| Advanced Generation | `Generation` (attempts, BestValid/FirstValid, relaxation, mesh quality, dynamic-topology experimental) |
| Materials | NEW `TrackMaterialSet` reference (+ `Visual` guide/marker colors if kept) |
| Debug | `Visual` debug + diagnostics toggles + export |

Where a group (e.g. Validation) currently draws from `Generation`/rulebook, V2 **re-presents** those
fields; it does not move the serialized storage.

---

## 11. Material Integration Specification (Priority 6)

### 11.1 What generator-side material work exists today (CONFIRMED)

- Editor menu utility that **creates .mat assets**: `TrackMaterialGenerator.GenerateMaterials`
  (`Editor/TrackMaterialGenerator.cs:9-23`) — road/shortcut/wall.
- **Runtime `new Material(...)`**: `TrackGuideMarkingBuilder.cs:579-581` (guide markings),
  `RaceCourseBuilder.cs:341-366` (gate fallbacks), `BoostPadActor.cs:108` (boost pad).
- **Assignment**: `BoxPrismTrackMeshBuilder.cs:563` assigns `{roadMaterial, sideMaterial}` (2
  submesh slots); collider physics material `TrackSurfacePhysics.Surface` (`:358`).
- **Repair on reload**: `TrackGenerator.RepairRoadMaterials` (`:1042`), `RepairGuideLineMaterial`
  (`:1060`) — reassign after domain reload.

### 11.2 V2 `TrackMaterialSet` (supplied, not generated)

Actual slots the generator uses (do not invent extras):

```
TrackMaterialSet (ScriptableObject or serialized struct on the generator)
  RoadSurface        (submesh 0)
  WallSide           (submesh 1)
  GuideMarking       (center/edge lines)          ← replaces runtime new Material
  WallMarker         (transverse markers)         ← replaces runtime new Material
  BoostSurface       (boost pads/lanes)           ← replaces BoostPadActor new Material
  RaceGate           (checker / pillar / checkpoint — group; keep as sub-set)
  (collider physics material stays TrackSurfacePhysics.Surface — physics, not visual)
```

**V2.5 change:** realisation reads material references from the supplied set and assigns them;
runtime `new Material` and the **repair passes are removed from the normal path** (repair may remain
only as an editor-safety fallback if a reference is missing, logging a clear warning). The
`TrackMaterialGenerator` menu utility can stay as an **optional editor helper** to author a starter
set, but is no longer part of generation. Care: extracting repair (§4, `TrackSceneLifecycle`) must
not break scene rebuild after domain reload — covered by the golden-corpus rebuild test.

---

## 12. Stability / Quality Rules (Priority 7)

Quality > yield, but not at the cost of normal configs failing. Invariants a V2 candidate must
satisfy (reject with a structured reason otherwise):

- Smooth section transitions: composed wall-top rate within category limit (§5.3) — **new**.
- Clean width changes; stable banking (already: `ValidateTransitionRates`, roll-rate).
- Stable vertical profile, no abrupt kinks (already: `VerticalProfilePlanner` κ/dκ-ds,
  `SlopeViolation`).
- No malformed loops/corkscrews (already: `ValidateRotationalEvents`, 3D clearance).
- No broken jump chains (already: 3-speed capture, ballistic clearance).
- No illegal overlaps / clearance (already: `ValidateSelfIntersection` hash+corridor+vertical).
- Valid start position / not below world bounds — **fix open defect #6** (`KeepAboveStart`).
- Valid feature counts vs Min/Max — **new enforcement** (§9).
- Closure + orientation continuity (already).

**Instrumentation to track every sweep:** candidate failure rate, dominant rejection reasons,
generation time, strict-pass success rate, relaxed-pass usage. **If a V2 change collapses yield,
diagnose the constraint conflict — do not just raise `MaxAttempts`.** (The dual-road regression is
the documented cautionary case.)

---

## 13. Validation Changes

| Validator | V1 state | V2 change |
|---|---|---|
| Cross-section transition quality | report-only hotspots (0.12 bound) | **NEW validator**, composite wall-top vector, category limits, ERROR after corpus calibration |
| Feature Min/Max | `RequiredFeatureMissing` exists; Max via placement | **enforce UI Min**, confirm Max respected, explicit failure when Min impossible |
| Start/world bounds | partial | **enforce** (#6) — reject/lift when start frame below policy |
| Vertical silhouette | none | **metric** (elevation range, major count) → small scoring term, not hard reject |
| Transition/sequence contracts | advisory (`TransitionResolver` reporting) | **selective promotion** to enforcement where safe (defer aggressive cases) |
| Everything else | 8 validators | UNCHANGED |

All new limits are **calibrated against the golden corpus first** and shipped as WARNING → ERROR
only when the corpus still passes.

---

## 14. Golden Seed / Regression Strategy (V2.0 — do first)

Before any behavioural change, capture a **V1 regression corpus** (10-20 seeds) covering: normal
racing track, curve-heavy, banking-heavy, major elevation, loop, corkscrew, wallride, jump, pipe,
dual-road, compound/advanced. For each: seed, full settings, serialized layout data, exported debug
report, key geometry metrics (lap, rings, elevation range, max facet, wall-top hotspots, feature
counts). Add a **deterministic reproduction test**: regenerate each seed → identical layout data
(and identical across mesh/validation changes). This corpus is the safety net that lets every later
stage prove non-regression. The supplied reports (`-1813449219`, `1651702061`) are the first two
corpus entries. (Backup already taken: `_Backups/TrackGeneration_V1_Backup.zip`.)

**V2 quality harness (target):** generate 100 seeds; auto-check closure, continuity, wall/cross-
section transition limits, banking limits, vertical curvature/slope, self-intersection, clearance,
feature Min/Max, deterministic reproduction, start validity, feature-specific validity, no crashes;
then pick a representative sample for manual visual review.

---

## 15. V2 Implementation Stages

Each stage: **Objective · Files · New files · Changes · Must stay unchanged · Risks · Tests ·
Completion.** Ordered per the brief; dependencies noted.

### V2.0 — Baseline (do first)
- **Objective:** reproducibility before change. **Files:** none modified. **New:** corpus + repro
  test (under `Tests/Editor`). **Changes:** capture corpus/reports/metrics. **Unchanged:**
  everything. **Risks:** none. **Tests:** determinism repro passes on all corpus seeds.
  **Completion:** corpus committed; repro green. Impact **LOW**.

### V2.1 — Cross-section / wall stability (PRIMARY)
- **Objective:** eliminate wavy walls via the field, per §5. **Files:** `CrossSectionPlanner`,
  `TrackCandidateBuilder` (`ApplyGlobalBankingField`, catch-wall/overhang, fold `ApplyCrossSectionBlend`),
  `TrackValidators`, `TrackDebugReportExporter`. **New:** possibly `CrossSectionField` helper +
  composite-wall-top metric. **Changes:** unify depth/width target into a continuous field; joint
  composite-wall-top rate limit; shared transition length; eased feature-approach bands; retire
  legacy adapter. **Unchanged:** feature interiors authored; frame model; mesh builder inputs.
  **Risks:** over-smoothing feature edges / yield dip — gate on corpus; wallride/pipe must still
  read as intended. **Tests:** the four named cases (banked, wallride, pipe, wide→standard) under a
  composite-rate bound; corpus hotspots drop below bound; no yield collapse. **Completion:** worst
  wall-top ≤ category limit on corpus; visual review clean. Impact **HIGH**.

### V2.2 — Vertical layout generation (profiles)
- **Objective:** Subtle/Balanced/Extreme feeding the elevation walk, per §6. **Files:**
  `ResolvedTrackGenerationConfig`, `TrackTopologyPlanner.AssignElevation`, `TrackElevationSettings`.
  **New:** `GenerationProfile` enum + profile→intent mapper; two intent fields (sustained length,
  level-recovery/rhythm). **Changes:** profile writes vertical intent before the walk; walk uses
  sustained-length + rhythm. **Unchanged:** `VerticalProfilePlanner` core; curvature/slope ceilings.
  **Risks:** Extreme requesting infeasible amplitude → rejects; calibrate so Balanced ≈ today.
  **Tests:** Balanced reproduces baseline metrics; Subtle non-flat but gentler; Extreme higher
  amplitude with **no** curvature/slope violations; profiles independent of feature counts.
  **Completion:** three profiles produce distinct silhouettes at equal quality. Impact **MEDIUM-HIGH**.

### V2.3 — Inspector V2
- **Objective:** clean hierarchy (§7-8). **Files:** `TrackGeneratorEditor` (rewrite), maybe
  `TrackInspectorPreviewCache`. **New:** none required. **Changes:** presentation only; seed
  workflow; foldouts; demote advanced regen. **Unchanged:** all serialized data; partial-regen
  capabilities (behind foldout). **Risks:** hiding a control a workflow needs — keep all behind
  Advanced. **Tests:** every V1 command reachable; seed write-back works; lock behaviour correct.
  **Completion:** designer can drive Profile→Seed→Generate→Features without internals. Impact **MEDIUM**.

### V2.4 — Feature Min/Max enforcement
- **Objective:** understandable, enforced counts (§9). **Files:** `TrackGeneratorEditor`,
  `TrackTopologyPlanner` (placement vs bounds), `TrackValidators` (`ValidateRequiredFeatures`).
  **New:** none. **Changes:** UI Min/Max → placement targets; Min enforced with explicit failure;
  Max respected. **Unchanged:** weights/capacities in advanced. **Risks:** aggressive minimums make
  small tracks impossible → must fail gracefully with reason, not silently. **Tests:** `Min=Max=2`
  yields exactly 2 or explicit failure; `Min=0,Max=0` yields none; impossible min fails with
  `RequiredFeatureMissing`. **Completion:** counts match intent or fail explainably. Impact **MEDIUM**.

### V2.5 — Material simplification
- **Objective:** supplied materials, no generation/repair (§11). **Files:** `BoxPrismTrackMeshBuilder`,
  `TrackGuideMarkingBuilder`, `BoostPadActor`, `RaceCourseBuilder`, `TrackGenerator` (repair),
  `TrackVisualSettings`. **New:** `TrackMaterialSet`. **Changes:** assign from set; remove runtime
  `new Material` + repair from normal path (keep editor-only missing-ref fallback with warning).
  **Unchanged:** collider physics material; submesh slot structure. **Risks:** post-reload
  reassignment breaks → covered by rebuild test; missing refs → clear warning. **Tests:** rebuild
  after domain reload keeps materials; all surfaces get correct slot. **Completion:** no generator-
  side material creation on the normal path. Impact **MEDIUM**.

### V2.6 — Generation stability pass
- **Objective:** promote quality checks to enforcement (§12-13). **Files:** `TrackValidators`,
  `TransitionResolver`, `TrackGenerator` (#6 start bounds). **New:** none. **Changes:** cross-section
  ERROR live; #6 fixed; selective sequence-contract enforcement; instrumentation of failure
  rate/reasons/time. **Unchanged:** whole-candidate recovery. **Risks:** yield collapse — corpus-
  gated, WARNING→ERROR. **Tests:** 100-seed sweep; yield ≥ threshold; dominant reasons sane; #6
  never spawns below bounds. **Completion:** quality invariants enforced without yield collapse.
  Impact **HIGH**.

### V2.7 — Advanced feature stabilization
- **Objective:** finish in-flight elements (Stage F directional corkscrew, dual-quarter fitter
  yield — tasks #13-16) on shared architecture. **Files:** `FeaturePatterns`, `SectionFrameBuilders`,
  `QuarterRoadFitter`, `FeatureCapabilities`. **New:** none. **Changes:** enable live selection once
  craft-retention/containment proven. **Unchanged:** existing loop/corkscrew/jump behaviour.
  **Risks:** search-space growth → latency/yield; behind capacity guards + watchdog. **Tests:**
  feature matrix; clearance holds. **Completion:** existing advanced features reliable; no new
  spectacle added. Impact **MEDIUM**.

### V2.8 — Inspector / designer polish
- **Objective:** labels, tooltips, categories, error messages, profile clarity, report presentation.
  **Files:** `TrackGeneratorEditor`, `TrackDebugReportExporter`. **Risks:** none structural.
  **Tests:** usability review. **Completion:** clear feedback + readable reports. Impact **LOW**.

### V2.9 — Performance
- **Objective:** fix collider cook/restore + **Apply-Presets→Generate crash (#28)**; profile hot
  paths only. **Files:** `BoxPrismTrackMeshBuilder`, `TrackTopologyPlanner`, `TrackGenerator`
  (post-V2.6 extraction helps). **Risks:** collider shape change — compare bounds/normals. **Tests:**
  restore cook time + peak memory down; **#28 no longer crashes**; no geometry change. **Completion:**
  crash gone; measured hot paths improved. Impact **MEDIUM**.

### V2.10 — Stable validation
- **Objective:** large sweeps + visual review. **Files:** test harness. **Changes:** compare success
  rate / reasons / time / quality / determinism vs corpus; representative visual review.
  **Completion:** V2-stable criteria met (§20). Impact **LOW** (process).

*(The audit's `TrackFrame.Transport` centralisation and the `TrackSceneLifecycle` extraction are
folded in where they enable a stage — Transport as a safe refactor during V2.1/V2.2, scene-lifecycle
extraction during V2.5/V2.9 to de-risk #28. Neither is a goal in itself.)*

---

## 16. Inspector Migration Map (old control → new location)

| V1 control / field | V2 location |
|---|---|
| Generate New Everything button | GENERATION → Generate New Track (Lock Seed OFF) |
| Regenerate Same Settings | Debug/Advanced → Advanced Regeneration (also = Lock Seed ON path) |
| Same Layout / New Content | Advanced Regeneration |
| Same Geometry / New Surface | Advanced Regeneration |
| New Features Only | Advanced Regeneration |
| New Quarter Content Only | Advanced Regeneration |
| New Visuals Only | Advanced Regeneration |
| Randomize All Unlocked | Advanced Regeneration |
| Seed field | GENERATION → Seed (+ Lock Seed toggle; write-back on generate) |
| `Scale`, `Layout` | Designer Settings → Track Layout |
| `Elevation` (+ profile) | Designer Settings → Vertical Layout (Profile at top of GENERATION) |
| `Road` | Designer Settings → Track Geometry |
| `Corners.Banking` | Designer Settings → Banking |
| `Features` (weights/lengths/radii) | Designer Settings → Feature Rules; **Min/Max hoisted** to FEATURE AMOUNTS |
| `Quarters` | Feature Rules (+ Dual-road Min/Max in FEATURE AMOUNTS) |
| `Generation` | Designer Settings → Advanced Generation (+ Validation subset) |
| `Visual` (guide/markers) | Materials + Debug |
| Material creation menu | Editor helper (optional), out of generation |
| Export Debug Report / diagnostics | Debug/Development |

---

## 17. Data Migration / Serialization Risks (Unity — critical)

- **Do NOT rename serialized fields.** Where V2 relabels a control, change only the **UI label /
  tooltip**, not the C# field name. If a rename is unavoidable, use `[FormerlySerializedAs]` (the
  codebase already uses it, e.g. `LaneEdgeInset`→`LaneEdgePosition`, `TrackDesignerSettings.cs`).
- **Enum values:** `TrackMacroSectionType` has intentional numbering holes (removed 11-13) — never
  reuse/renumber. Any new enum (`GenerationProfile`) starts fresh; if it must serialize on existing
  assets, default = Balanced and confirm old assets deserialize to Balanced.
- **New fields default safely:** `SustainedClimbDropLength`, `LevelRecoveryTendency`,
  `GenerationProfile`, `TrackMaterialSet` must deserialize on existing settings assets to values
  that reproduce **today's** behaviour (Balanced + current intent), guarded in `Sanitize()` like the
  existing `LaneEdgePosition<=0` guard.
- **`GeneratedTrackSection` lists are scene-serialized** — changing section/frame field layout risks
  breaking saved scenes; prefer additive fields.
- **Material set:** introduce as an added reference; missing → editor warning + fallback, never a
  hard null-crash during build.
- Golden corpus (V2.0) is the migration safety check: load old settings, regenerate, diff.

---

## 18. Known Issues Addressed by V2

- Wavy walls / section-transition oscillation (Priority 1, V2.1) — CONFIRMED in both reports.
- Weak/absent ordinary vertical silhouette; vertical dominated by stunt features (Priority 2, V2.2).
- Overloaded Inspector; advanced regen dominates (Priority 3, V2.3/8).
- Opaque feature control (Priority 4, V2.4).
- Generator-side material creation + repair coupling (Priority 6, V2.5).
- Spawn-below-ground `KeepAboveStart` violation (#6, V2.6).
- Apply-Presets → Generate crash / collider cook cost (#28, V2.9).
- Legacy `ApplyCrossSectionBlend` adapter + empty `TrackAnnotationSceneOverlay.cs` (cleanup, V2.1).
- In-flight Stage F directional corkscrew / dual-quarter yield (#13-16, V2.7).

---

## 19. Explicitly Deferred Work

- Full **speed-state simulation** (carried entry/exit speed) — useful later; must not delay V2.1-6.
- **Pacing / director** system — deferred until core reliably produces clean tracks.
- Large **vehicle architecture** changes — out of scope; maintain only the minimal contract
  (`ITrackRaceCraft` + the generation-facing capability values already in config).
- **New spectacle features** — none added in V2; stabilise existing ones.
- Complete generator **rewrite** — explicitly not happening.

---

## 20. Definition of Track Generator V2 Stable

V2 is "stable" when, on a 100-seed sweep with default (Balanced) settings and representative
feature Min/Max:
- ≥ target strict-pass success rate with no yield collapse vs V1 corpus;
- **zero** cross-section transition violations above category limits on accepted tracks;
- every accepted track: valid closure, continuity, banking, vertical curvature/slope,
  self-intersection/clearance, feature Min/Max, valid start (not below bounds), deterministic
  reproduction, no crashes;
- Subtle / Balanced / Extreme produce visibly distinct vertical silhouettes at identical geometry
  quality; Balanced ≈ the current intended baseline;
- materials come entirely from the supplied set;
- the Inspector is usable end-to-end without understanding the algorithm;
- `#6` and `#28` resolved;
- representative visual review confirms tracks look intentionally designed.

---

## Appendix — First-task inspection results (A–F), evidence

- **A. Wavy walls path (CONFIRMED):** `TrackCandidateBuilder.ApplyGlobalBankingField` (`:535`,
  blur radius 253 m per report) writes bank/tilt/suppression/rounding/overhang; `CrossSectionPlanner`
  (`:34`) writes width/side-height with scalar rate caps (width 0.12, side-height 0.08) and hard
  locks on protected shapes (`:188`); per-type `DepthMultiplier` targets oscillate (28.1↔26.0);
  composite wall-top never jointly limited → report hotspots 0.220 m/m (wallride) and 0.149 m/m
  (spiral-approach/recovery), connector depth dips 0.94-0.96. `ApplyCrossSectionBlend` (`:1141`) is
  a dead 2-line adapter.
- **B. Vertical intent (CONFIRMED):** `TrackTopologyPlanner.AssignElevation` (~`:2754`) gated by
  `TargetElevationAmplitude` (`:2773`); intent fields in `TrackElevationSettings` (`:390`). Profiles
  enter here; `VerticalProfilePlanner` (realisation) untouched.
- **C. Inspector (CONFIRMED):** settings pre-grouped into `Scale/Layout/Corners/Road/Transitions/
  Elevation/Features/Quarters/Generation/Visual` (`TrackDesignerSettings.cs:809-818`); editor is
  `TrackGeneratorEditor` (1402 lines) with partial-regen commands (`TrackGenerator.cs:252-317`).
- **D. Feature counts (CONFIRMED):** `TrackFeatureRule{Enabled,Min,Max,Weight}` per feature in
  `TrackFeatureSettings`; live set from reports = loops/corks/spirals/halfloops/jumps/pipes/
  wallrides/hairpins/chicanes/scurves (+ crests, dual quarters). Min enforcement via
  `ValidateRequiredFeatures`/`RequiredFeatureMissing`.
- **E. Materials (CONFIRMED):** editor menu creates 3 .mat (`TrackMaterialGenerator`); runtime
  `new Material` in `TrackGuideMarkingBuilder:581`, `RaceCourseBuilder:341-366`, `BoostPadActor:108`;
  assignment `BoxPrismTrackMeshBuilder:563` (`{road,side}`); repair `TrackGenerator:1042/1060`.
  Slots: RoadSurface, WallSide, GuideMarking, WallMarker, BoostSurface, RaceGate; collider physics
  material separate (`TrackSurfacePhysics.Surface`).
- **F. Stability (CONFIRMED):** 8 validators (`TrackValidators.RunAll:30`) + 20 test files. V2 adds
  composite cross-section transition validator, feature Min/Max enforcement, start-bounds, vertical-
  silhouette metric.

*(No production code modified. Backup: `_Backups/TrackGeneration_V1_Backup.zip`.)*
