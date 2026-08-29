# Slingshot 2.0 — Track Generator V2: Full Context Handoff

Self-contained briefing. Everything needed to continue the work without prior conversation.
Companion docs: `TRACK_GENERATOR_STABLE_AUDIT.md` (audit), `TRACK_GENERATOR_V2_SPEC.md` (spec),
`TRACK_GENERATOR_V2_ROADMAP.md` (task board), `TRACK_GENERATOR_V2_1_PLAN.md` (wall work),
`TRACK_GENERATOR_V2_1B_CURVATURE_FLOW.md` (curvature investigation).

## Current implementation update — 2026-08-26

Track Editor V2 is no longer a preview-only experiment. Its designer flow is now **choose a section
→ preview a design → Build & Apply**. Selection automatically frames the section, preview geometry is
non-destructive, and Build & Apply rebuilds the authoritative route with its connectors and all final
validation. The current track is replaced only when the edited route succeeds; failure preserves the
accepted geometry and restores the complete previous recipe/Inspector state.

Applied section choices are normalized topology overrides inside the Exact Recipe. The base seed
identity stays stable, while the edited recipe receives its own deterministic identity. Consequently,
Save, Load and Exact Replay can reproduce both the generated layout and the designer's accepted
section substitutions. Multiple planned substitutions are applied together and are labelled as such
in the editor. The focused V2 gate includes override persistence/identity coverage; runtime and editor
assemblies compile with zero errors. Unity Test Runner and manual visual sign-off remain the final
acceptance steps for this milestone.

Track Editor edits now persist a structural demand anchor in addition to the human-readable finished
slot ID. This fixes a discovered phase-boundary mismatch where a valid Horseshoe, Immelmann or Wide
Turnaround preview could fail before geometry solving because connector/closure insertion had shifted
the slot's route-order token. The fallback matches quarter, road, topology role, signed heading and
original realization strictly; it does not widen any safety tolerance. A future fitting-freedom
control should be added only when distinct replan neighborhoods are truly implemented.

Vertical Profiles are now implemented as a separate, recipe-persisted design axis. The Track Profile
card offers **Subtle / Balanced / Extreme** elevation while Style, Difficulty, Size and feature
amounts keep their existing responsibilities. Balanced maps to the historical elevation constants
exactly; Subtle and Extreme steer amplitude, major-event count, sustained-grade length and recovery
rhythm without raising any safety ceiling. Debug reports include a measured vertical silhouette so
the three modes can be calibrated from evidence instead of screenshots alone. Runtime and editor
assemblies compile cleanly; Unity `V2FastGate`, yield comparison and visual sign-off remain.

---

## 1. The project

Unity 6000.x hovercraft racing game. Design speed **1300 km/h ≈ 361 m/s** — this speed is the reason
almost every quality bound is tight. A ~27,000-line procedural track generator lives in
`Assets/Scripts/TrackGeneration/`. Branch: `Ariel-TrackGeneratorV2`.

The generator is **mature, not prototype**. A full forensic audit found it already has: deterministic
per-subsystem seed streams, plan → build → validate → score → select, `TrackConnectionFrame` as a
single geometric source of truth, quaternion + unwrapped orientation transport, a global banking
field, a unified RotationalEvent model (loops/corkscrews/spirals share one implementation), a
chain-level vertical planner, a cross-section planner, spatial validation with a broad-phase hash,
24 structured failure reasons, and ~190 editor tests.

**Therefore V2 is stabilisation and refinement — explicitly NOT a rewrite.**

---

## 2. V2 product goals

Pressing **Generate New Track** repeatedly should produce tracks that look *intentionally designed*,
and a designer should drive the tool without understanding the algorithm. Five focus areas:

1. **Clean transitions between sections** (no "wavy walls", no visible seams).
2. **Stronger deliberate vertical variation** on ordinary road (not just stunt features).
3. **A clean designer-facing Inspector.**
4. **Understandable feature quantity control** (Min/Max per feature).
5. **Generation-quality stability** — prefer rejecting a candidate over shipping bad geometry,
   without collapsing yield.

**Definition of V2 Stable:** select Subtle/Balanced/Extreme, set feature Min/Max, optionally lock a
seed, assign project materials, press Generate → clean, stable, vertically interesting track with
smooth transitions and predictable quality. Verified by a 100-seed sweep with no quality-invariant
violations, deterministic reproduction, zero crashes, plus visual review.

---

## 3. Locked architectural decisions (do not revisit without evidence)

- `TrackConnectionFrame` stays the single geometric source of truth (exit frame ≡ next entry frame).
- Deterministic per-subsystem seed streams stay (`Unity.Mathematics.Random`; no `UnityEngine.Random`
  anywhere in generation).
- plan → build → validate → score → select pipeline stays.
- Quaternion + unwrapped orientation transport stays (centralisation OK, replacement not wanted).
- `VerticalProfilePlanner` stays — improve the *intent* feeding it, not the realisation.
- `CrossSectionPlanner` stays — cross-section fixes go through it.
- `RotationalEvent` model stays (loops/corkscrews/etc. share geometry concepts).
- Whole-candidate rejection stays as the recovery model (no per-section backtracking).
- Balanced is the default vertical profile; the three profiles are Subtle / Balanced / Extreme.
- Feature Min/Max is a primary designer interface.
- Materials are persistent authored assets — the runtime generator must never create or silently
  retune materials. Editor tooling may create missing defaults, but repairing the palette must preserve
  every existing material's authored color, emission, texture, and render settings.
- Pacing/director logic and full speed-state simulation are **deferred** out of V2.

---

## 4. Status

```
V2.0 Baseline            DONE       frozen 8-track golden corpus + determinism, all green
V2.1 Wall stability      DONE*      SideHeight fix implemented + verified (*needs rebaseline + visual sign-off)
V2.1b Curvature flow     INVESTIGATED, not implemented — see §7
V2.2 Vertical profiles   90%        implemented; Unity calibration/sign-off remains
V2.3 Inspector V2        90%        primary workflow redesigned; advanced groups retained
V2.3c Topology editing   90%        transactional apply + automatic connectors + exact overrides
V2.4 Feature Min/Max     80%        primary controls and enforcement; acceptance matrix remains
V2.5 Materials           90%        persistent material contract integrated; Unity visual/domain-reload sign-off remains
V2.6 Stability pass      60%        #6 fixed; timing + bounded deterministic seed audit integrated
V2.7 Advanced features   TODO       includes #36 dual roads, Stage F
V2.8 Polish              TODO
V2.9 Performance         TODO       includes #28 crash
V2.10 Stable validation  TODO
```

---

## 5. V2.0 — Golden corpus (DONE)

8 frozen entries with **committed seed + expected canonical hash** in
`Tests/Editor/GoldenSeedCorpus.cs`: `normal_racing(1000)`, `flowing(2000)`,
`technical_curves(3002)`, `velocity(4000)`, `switchback_banking(5006)`, `rollercoaster(6000)`,
`wallride(10000)`, `compound(15061)`.

Canonical data compared per seed: lap length; per-section type/name/length/width/turn/radius/banking/
elevation/pitch/roll/quarter/road/patternId/semanticElement/ring-count; a hash over **every ring
frame** (position, forward, right, up, width, bank, pitch, side height, accumulated roll/vertical,
curvatures, wall multipliers, overhang, pipe, wallride, rounding); quarter fingerprint; the full
metrics block; and the worst wall-top rate.

Tests (`Tests/Editor/GoldenSeedCorpusTests.cs`):
- `CoreDeterminism_SameSeedSameSettings_ProducesIdenticalCanonicalData`
- `FrozenSeedMatchesV1BaselineAndReproduces` (8 cases: run-vs-run determinism **and** match to the
  committed V1 hash)
- `CorpusIsFullyFrozen`, `CorpusEntriesAreWellFormed`
- `[Explicit] DiscoverAndFreeze` (scans — **never run on an unfrozen entry**)
- `[Explicit] RebaselineFrozenCorpus` (pinned seeds, no scan — the approved way to re-baseline)

**Dropped from the corpus deliberately:** a dual-road entry and a `major_elevation` entry (both
depended on the unreliable mandatory-dual-quarter path). Feature coverage is still complete:
loops/corkscrews/spirals/jumps via `rollercoaster` + `compound`, pipes via `switchback_banking`,
wallrides via the dedicated entry.

---

## 6. V2.1 — Wall stability (DONE, verified)

### The real problem (as clarified by the designer)
Not "walls are rough". The walls are smooth. The defect is that **neighbouring segments with
different widths AND wall heights don't transition as one coordinated cross-section morph**, so
segment boundaries announce themselves. Width and wall height are both first-class; think
`CrossSection A → transition envelope → CrossSection B`, not per-section targets.

### Diagnostics built (read-only)
`CrossSectionTransitionDiagnostics.cs` — 9 channels, each with **excursion** (how far a span left the
`[A,B]` band with no authored reason) and **reversals** (derivative sign changes):
`Width, SideHeight, BoostL, BoostR, Bank, TopL/TopR (vertical + lateral)`.
The composed TopL/TopR come from the **real evaluated cross-section**
(`TrackCrossSection.Evaluate`) so width, side height, wall multipliers, overhang, rounding, pipe
closure and wallride morph are all included — measured in the frame's local basis (forward progress
and frame rotation excluded by construction). Wired into the debug report + an `[Explicit]`
corpus measurement test.

### Three-tier per-channel classification
- **AuthoredAnchor** — the 9 protected types (JumpRamp, LandingRamp, Loop, Corkscrew, Spiral,
  HalfLoopTwist, FullPipe, WallrideTurn, RotationalEvent) + structural widths (quarter catch, dual road).
- **DesignTarget** — ordinary road intentionally asking for a distinct height:
  BankedCurve (×1.08), BankedHairpin (×1.10). **Kept** — `Normal → BankedCurve → Normal` must still rise.
- **PassThrough** — plain straights, connectors, approaches, recoveries (×1.00). SCurve/Chicane are
  pass-through *for the height channel* while remaining authoritative for path curvature/banking.

### Root cause (confirmed by measurement, not inference)
`CrossSectionPlanner.BuildNodes` set each ring's wall-height target from
`TrackCandidateBuilder.DepthMultiplier(SectionType)`. Pass-through sections therefore **asserted the
neutral 26.0 m target**, so `28.1 → 26.0 → 28.5` valleys appeared between tall anchors. Every measured
excursion bottomed at *exactly* the neutral value. `ProjectRateLimits` is a rate limiter — it
faithfully tracks oscillating targets, so more smoothing could never fix it. **It was a target-selection
defect, not a smoothing defect.**

### Two corrections the measurements forced (worth knowing)
1. An early inference that the **wall-boost channel dominated (~86%)** was **wrong** — it compared
   worst-per-channel figures from *different spans*. Per-span decomposition showed SideHeight is the
   pervasive driver; boost excursion is usually ~0 and is large only where the outside wall
   **legitimately swaps sides** (S-bends). Boost carry was therefore dropped from scope.
2. The first implementation used only a *local* distance weight and **failed on the data**: a 510 m
   run still reached `Smooth01(255/361) = 0.79` → dipped to 26.5 m, exactly as measured.

### The fix (`CrossSectionPlanner.BlendPassThroughHeights`, runs BEFORE the rate limiter)
```
neutralWeight = Smooth01(min(distFromA, distFromB) / Lt) × Smooth01(clamp01((runLength − 2·Lt) / Lt))
                └── local: a full Lt from both anchors ──┘   └── the run can hold a real plateau ──┘
target        = Lerp(easedAnchorMorph(A→B), ownNeutralTarget, neutralWeight)
```
`Lt = max(cfg.WidthTransitionLength, cfg.CrossSectionTransitionLength)` — the same expression the
planner already used (≈361 m at design speed). Both factors are required. Continuous in run length
(no cliff): a short run holds its anchors; a long run ramps to neutral, holds, ramps back — one
deliberate decrease and one deliberate increase.

### Acceptance metric (deliberately not "zero excursion everywhere")
A long ordinary run **may** legitimately go to neutral, hold, and return. Legitimacy requires
`transition out (Lt) + meaningful plateau (Lt) + transition in (Lt)` = `3 × Lt`. So the metrics are:
- `UnnecessarySideHeightReversals` = `max(0, observed − 1)` for a qualifying long run with both
  anchors above neutral; otherwise `observed`.
- `UnnecessarySideHeightExcursion` = only below-neutral (or above-both) for a qualifying run;
  the whole excursion for a short run.

### Results

| metric | before | after |
|---|---|---|
| `UnnecessarySideHeightReversals` (corpus) | 10 | **0** |
| `UnnecessarySideHeightExcursion` (corpus) | 0.94 m | **0.04 m** (below the 0.05 m noise floor) |
| editor-fidelity | 5 rev / 2.06 m | **0 rev / 0.00 m**, defect list empty |
| wallride interior rates | 0.274/0.227/0.289 | **identical** — nothing authored was flattened |
| Width / lateral / Bank | 0.00 / 0.00 / 75.3° | unchanged |
| RAW SideHeight excursion | 1.38 m | 1.40 m — *intentionally unchanged*: legitimate neutral runs kept |

That last row is the point: **raw variation stays, incidental variation goes.**

Guard test: `PassThroughSectionsDoNotCreateIncidentalWallValleys`.

### Also established (useful, from measurement)
- **Width transitions are already clean** — 0 reversals / 0.00 m excursion in all 8 corpus tracks and
  both editor tracks, including into a full pipe. V2.1 correctly did not touch width machinery.
- **The worst composed wall-top *rates* are inside authored features** (FullPipe 0.548, Wallride
  0.289) — those are the intended pipe closure and wallride morph, not defects.

---

## 7. V2.1b — Centreline curvature flow (INVESTIGATED, not implemented)

**Observed:** `BankedCurve_45deg_Left R=650 → Straight_01_Climb19m → BankedCurve_45deg_Left R=578`
reads as a continuous left-hander interrupted by a straight.

**Why the straight exists:** straights between corners are the **closure solver's levers**
(`RunActiveSetSolve` adjusts straight lengths + corner radii to close the lap). The code guards this
budget — `CanDropLeadStraight` allows at most one drop per quarter. Structural, not decorative.

**It does assert zero curvature:** `SectionType = Straight` → `BuildStraight` → curvature exactly 0.

**Key finding:** `ConnectorAnalyzer` *already detects this case* — same-direction signs inside the
bridge window → `SameDirectionTurnBridge`, shared `TurnComplexId`, note *"treated as one turn complex
— no reset between the turns"*. But absorption carries
`inherited = "bank, outside wall support, depth, turn rounding"` — **surface properties only. The path
is untouched.** Evidence: `Straight_02 len 38m … SameDirectionTurnBridge complex TurnComplex_1` —
a 38 m (~0.1 s) zero-curvature interruption inside a declared turn complex.

**So it is the SideHeight bug one layer down:** incidental connector state overriding neighbouring
design intent — `Tall → 26.0 → Tall` (fixed) vs `R650 → ∞ → R578` (open).

**Continuity today:** C0 ✓, C1 ✓, **C2 ✗** — curvature steps instantaneously at each join.

**Why it is NOT a small fix:** wall height doesn't affect topology; **curvature does** — connector
curvature changes heading, and heading feeds closure. Options: (A) plan-time corner merge
(45+45 → one 90° corner) — medium risk, costs a closure lever, has precedent in the completed
"Stage D opt-in capacity-guarded lead-straight drop"; (B) curved connector with a radius ramp — high
risk, changes the solver's variable set; (C) tuning only — low risk, doesn't fix it.

**Recommendation:** separate stage (**V2.1b**), diagnostics first (curvature-flow metrics: incoming/
connector/outgoing curvature, shortest same-direction straight, dκ/ds spike at joins, unnecessary
curvature state changes), then option A behind a config flag, judged on **generation yield** as much
as on appearance. Files that would change: `TrackTopologyPlanner`, `ConnectorAnalyzer`, possibly
`SectionFrameBuilders`. **Not** `CrossSectionPlanner` — that work is done.

---

## 8. Wall shape controls (designer reference — all configurable, nothing hardcoded)

**Designer Settings → Road** (`TrackRoadSettings`), resolved into `TrackRoadProfileSettings`:

| Control | Default | Effect |
|---|---|---|
| **`WallCurve`** | 1.0 | **The steepness dial.** 0 = flat straight extensions of the floor; 1 = perfect quarter circle (vertical tip). Tip height = `WallHeight × tan(curve × 45°)` |
| `WallHeight` | 24 m | Wall footprint, and at curve 1 also its height |
| `FlatCenterWidth` | 16 m | Flat floor. Total road = `(Flat + 2×WallHeight) × RoadScale` |
| `RoadScale`, `SafetyLipHeight`, `ProfileResolution`, `ColliderProfileResolution` | 1 / 1 m / 32 / 40 | |

**Why some sections look far steeper (by design, two systems):**
1. **Dynamic Turn Rounding** — in a committed turn the flat ratio is lerped **0.48 → 0.05** *and* wall
   curvature forced to 1.0 → a much deeper bowl. Dials: `TurnRoundingStrength` (0.85),
   `MinimumTurnCenterFlatRatio` (0.05), `DynamicTurnRounding` on/off.
2. **Outside Catch Wall** — outside wall curls **past vertical**. Dials: `CatchWallStrength` (0.6),
   `MaxOverhangAngle` (18°), `OverhangRadius` (10 m), `CatchWallMinimumDemand` (0.35).

To soften, in order of effect: `TurnRoundingStrength` ↓ → `MinimumTurnCenterFlatRatio` ↑ →
`MaxOverhangAngle` ↓ → `CatchWallStrength` ↓ → `WallCurve` ↓.

---

## 9. Remaining stages (what "done" means for each)

**V2.2 Vertical profiles.** `GenerationProfile` enum (Subtle / **Balanced** / Extreme) feeding
**vertical intent** at `TrackTopologyPlanner.AssignElevation` (~`:2754`, gated by
`TargetElevationAmplitude`). `VerticalProfilePlanner` (realisation) is NOT touched. Profiles set
amplitude, major-event count, sustained climb/drop length, level-recovery rhythm — and **never raise
quality ceilings**; Extreme buys amplitude by allocating distance, not by relaxing limits. Balanced
must reproduce today's baseline (that's the acceptance test). Profiles and feature counts are
independent.

**V2.3 Inspector V2.** Presentation only over existing serialized data: Profile → Lock Seed + Seed →
GENERATE; seed **written back** after an unlocked generate; Feature Amounts (Min/Max) hoisted;
Designer Settings foldouts (Track Layout / Vertical / Geometry / Banking / Feature Rules / Validation /
Advanced Generation); partial-regeneration commands preserved but demoted to Advanced Regeneration;
Materials + Debug. **No serialized field renames** (use `[FormerlySerializedAs]` if unavoidable).

**V2.4 Feature Min/Max.** Data model already exists (`TrackFeatureRule{Enabled,Min,Max,Weight}`).
Enforce minimums with explicit `RequiredFeatureMissing` failure; respect maximums; weights/lengths/
radii move to advanced.

**V2.5 Materials.** Implemented as a persistent `TrackMaterialSet` contract with explicit roles for
RoadSurface, WallSide, GuideMarking, WallMarker, BoostSurface, RaceGate, StartFinish,
StartGatePillar, and Checkpoint. Runtime builders and cache restore resolve through that contract;
legacy references remain a migration fallback. Exact Replay and cached previews rebind the persistent
assets, debug reports expose the resolved contract, and the inspector can create/repair the default
palette without overwriting existing authored material tuning. Remaining acceptance work is a Unity
domain-reload/cache-reload visual pass plus confirmation of every race-course marker role.

**V2.6 Stability.** The rendered-bounds ground-clearance fix and generation timing instrumentation are
implemented. `Track/V2/Bounded Stability Sweep` provides a deterministic, cancellable planning-only
audit with strict/non-strict counts, dominant failures, average/slowest timing, and reproducible failing
seeds. Remaining work is evidence-led: run bounded sweeps first, then promote only proven sequence or
cross-section warnings into hard gates and expand toward the full corpus/100-seed acceptance sweep.

**V2.7 Advanced features.** #36 dual roads, #13–16 Stage F. No new spectacle features.

**V2.8/9/10.** Polish; #28 crash + collider cook cost; large sweeps + visual review.

---

## 9b. Generation Recipe (ADDED REQUIREMENT — reproducibility beyond a bare seed)

**Problem.** An integer seed alone does not reproduce a track. It only reproduces the same track when
*every* setting **and the generator version** match. This is not theoretical — it was observed during
V2.0: changing the harness's `MaxAttempts` from 200 → 80 made three frozen seeds report
*"seed 1000 no longer generates a valid track."* Same seed, same settings asset, different generator
behaviour → different result.

**Requirement.** A saved **Generation Recipe** must capture the complete reproducible design:

| field | notes |
|---|---|
| Seed | plus the per-subsystem stream seeds (`TrackSeedStreams`) for partial regeneration |
| Generator version | so an incompatible replay is detected rather than silently wrong |
| Generation profile | Subtle / Balanced / Extreme (V2.2) |
| Feature rules | Min/Max/weights per feature |
| Layout + geometry settings | the full `TrackDesignerSettings` payload |
| Start position + orientation | track root transform + start anchor |
| Locked/unlocked setting groups | `SettingsLockState` |
| Settings hash | detects "these settings drifted since this recipe was saved" |

**Material that already exists to build on (do not duplicate):** `TrackSeedManager` already logs a
seed hash (`Seed_1302504738 (hash: 8f0bcf…)`); `TrackSeedStreams` holds per-subsystem seeds;
`SettingsLockState` already models lock groups; `TrackDesignerSettings.Clone()` exists; the debug
report already dumps "SCENE SETTINGS (actual serialized values)" and a "Designer provenance" line.

**Scope note.** This is a *save/restore + versioning* feature — no geometry risk — and it belongs
next to the V2.3 seed workflow (which currently only specifies seed lock + write-back). Treat the
seed box as the quick path and the recipe as the durable one.

---

## 10. Open defects

| id | what | notes |
|---|---|---|
| **#28** | Apply Presets → Generate crash (OOM / native collider cook) | Workflow blocker. Collider cook is ~8.7 M triangles per restore |
| **#6** | Track spawns below ground (`KeepAboveStart` y<0) | Correctness |
| **#36** | Dual-road quarters: ~0 yield when Min ≥ 1 | See below |
| #13–16 | Stage F in-flight (directional corkscrew, dual-quarter fitter budget, roll handedness, corkscrew floor bulge) | Incomplete |

### #36 dual roads — diagnosis (important)
With `MinimumDualQuarterCount ≥ 1`, 600 attempts × many seeds produced **0 valid candidates**.
Root cause is a **design tension, not a single bug**: road B must detour around road A (≥ ~158 m
clearance) yet stay within ~20 % of road A's *time* (balance gate). A detour makes road B 30–200 %
longer, so the two requirements are mutually unsatisfiable for most quarters. Raising lane separation
made it **worse** (longer road B → worse length failure) and was reverted.
**Durable fix: vertical-separated lanes** — road B rides above/below road A, staying laterally close
and short. Interim: tolerance ranges were widened (`NeutralTimeTolerancePercent` now up to 60,
`RoadLengthTolerance` up to 1.0, defaults unchanged) and `RequireArchetypeDifferentiation` can be
turned off, which permits intentionally imbalanced duals.

---

## 11. Key files

| file | role |
|---|---|
| `Planning/TrackTopologyPlanner.cs` (3113) | horizontal skeleton, closure, elevation walk, quarters, features |
| `Planning/SectionFrameBuilders.cs` (1943) | definitions → ring frames (arcs, ramps, loops, corkscrews, rotational events) |
| `Planning/TrackCandidateBuilder.cs` (1167) | frame walk, **global banking field**, vertical + cross-section planners |
| `Planning/CrossSectionPlanner.cs` | canonical width/side-height field — **V2.1 fix lives here** |
| `Planning/VerticalProfilePlanner.cs` | chain-level vertical field (do not touch; feed better intent) |
| `Planning/ConnectorAnalyzer.cs` | connector classification, `BridgeCarry`, turn-complex absorption |
| `Planning/TrackGenerationPipeline.cs` (387) | the candidate loop |
| `Validation/TrackValidators.cs` (1173) | 8 validators incl. spatial hash clearance |
| `Macro/TrackConnectionFrame.cs` | the geometric source of truth |
| `Macro/TrackCrossSectionEvaluator.cs` | `TrackCrossSection.Evaluate` — composed cross-section |
| `Macro/BoxPrismTrackMeshBuilder.cs` | solid prism mesh + separate coarser chain colliders |
| `CrossSectionTransitionDiagnostics.cs` | V2.1 read-only transition metrics |
| `TrackGenerator.cs` (1496) | orchestration + scene lifecycle (audit flagged this coupling) |

---

## 12. Working agreements (learned the hard way — please keep)

- **Measure before implementing.** It corrected two wrong inferences during V2.1 alone.
- **A running EditMode test blocks Unity's domain reload** — editing code cannot cancel a stuck run.
- **Never run `[Explicit] DiscoverAndFreeze` on an unfrozen corpus entry** — it scans and once ran
  3.5 hours. Re-baseline with `RebaselineFrozenCorpus` (pinned seeds, no scan).
- **Geometry changes shift candidate selection** — a per-seed before/after is not always the same
  track. Compare span structure before drawing conclusions.
- **Corpus hashes changing after an intentional geometry change is correct**, not a regression:
  review the diff, then re-baseline.
- Distinguish **rate limiting** from **target selection** — a rate limiter faithfully reproduces bad
  targets.
- Prefer **continuous/distance-weighted** rules over hard thresholds in geometry; keep hard
  thresholds in *metrics* only.
- Don't fix authored feature interiors (wallride/pipe/loop/corkscrew) — high measured rates there are
  intended.
