# Slingshot 2.0 — Track Generator Stable Audit

**Scope:** procedural track generation and the systems required to build a usable track.
Hovercraft controller, vehicle physics, art, shaders, VFX, UI, audio, camera are **out of
scope** and treated as external systems the generator must expose clean contracts to.

**Rule honoured:** this is an audit. **No production code was modified.** Files were read
only. Where I state a fact I give the evidence (`file:line`); where I could not fully verify I
say so with a confidence label.

**Confidence labels used:** CONFIRMED (read directly in code), HIGH CONFIDENCE (strongly implied
by read code + consistent naming), POSSIBLE (plausible, not fully traced), UNKNOWN / REQUIRES
VALIDATION (needs a runtime check or a file I did not exhaust).

**Headline:** the Track Generator is **not prototype-quality**. It is a mature, deliberately
layered system of ~27,000 lines across a clean `TrackGeneration` assembly tree. Most of the
architectural ideas this brief asks whether to *introduce* — deterministic seed streams,
explainable rejection, multi-category validation, a single geometric source of truth, segment
capability contracts, a global orientation/banking field, a chain-level vertical planner, a
canonical cross-section planner, width-aware spatial clearance — **already exist in the codebase**.
The Stable-Version work is therefore mostly **consolidation, wiring, and closing half-finished
migrations**, not a rewrite. Identifying what to *keep* is as important as what to change.

---

## 1. Executive Summary

The generator runs a deterministic **candidate loop** (`TrackGenerationPipeline.Run`): plan →
build → validate → score → select, with each attempt drawing from independent per-subsystem RNG
streams, a 60 s watchdog, and every rejection recorded with a structured reason. The authoritative
geometric representation is a stream of **`TrackConnectionFrame`** structs (position + forward /
right / up + curvature / roll / width metadata) where each section's exit frame *is* the next
section's entry frame — a genuine single source of truth for geometry. Orientation is propagated
by quaternion transport with explicit re-orthonormalisation and **unwrapped** accumulated roll /
vertical rotation, which structurally avoids the classic 180° flip and gimbal problems. Banking is
a **global field**, not per-section mesh rotation. Loops, corkscrews, spirals and half-loops are
unified under one **RotationalEvent** phase-integration model rather than independent hacks.
Validation is already split into eight categories including a broad-phase spatial hash with
width-aware, exemption-aware clearance and ballistic jump-flight sampling.

The real weaknesses are not correctness-of-shape; they are **maturity and coupling**:

- Several strong subsystems (segment **capability contracts**, the **transition resolver**,
  canonical token solver, directional corkscrew) are **declared and partially wired but not yet
  fully consumed by candidate selection** — the code says so honestly in its own comments. The
  system knows a lot about itself that it does not yet *act on*.
- `TrackGenerator` (the MonoBehaviour) carries a heavy load of **scene / editor lifecycle**
  responsibility (root reconciliation, material repair, race-course repair, mesh release,
  hovercraft placement, cache) that is separate from generation but lives in one 1,496-line class.
- **Speed is estimated, not propagated as a constraint** — the generator scores lap time but does
  not carry per-segment min/max entry-speed windows into selection.
- **Two overlapping vehicle-data sources** (`ITrackRaceCraft` + `CraftArchetypeProfile` +
  scattered `cfg` speeds) are not unified into one vehicle-capability contract object.
- There is **no explicit pacing / director** stage; pacing is implicit in scoring penalties.
- In-flight **Stage F** feature work (directional corkscrew, dual-quarter fitter yield) is
  incomplete (tasks #13–16), and two known defects are open: **spawn-below-ground** (#6) and the
  **Apply-Presets→Generate crash** (#28).

None of these threaten the fundamentals. The Stable Version should **preserve the pipeline,
frame model, determinism, orientation field, spatial validator, and rotational-event model**, and
concentrate on (a) finishing the contract→selection wiring, (b) extracting scene/editor lifecycle
out of the generator MonoBehaviour, (c) unifying the vehicle contract, and (d) adding speed-state
and (optionally, minimally) pacing on top of the existing scoring.

---

## 2. Current Track Architecture (layered view)

```
Design layer (authoring intent — ScriptableObjects & settings)
  TrackDesignerSettings, TrackStylePreset, TrackFeatureRule, Difficulty/Size modifiers,
  SettingsLockState, PresetRandomizer
        ↓ resolve + clamp
Resolved config (immutable per-run)
  ResolvedTrackGenerationConfig  (all limits, DesignSpeed, Gravity, feature rules)
        ↓
Core (determinism)
  TrackSeed, TrackSeedManager, TrackSeedStreams, PlanRandomStreams
        ↓
Planning (the algorithm — pure data, no scene objects)
  TrackTopologyPlanner → TrackCandidateBuilder → TrackValidators → scoring/selection
  supported by: FeaturePatterns, FeaturePlanning, FeatureCapabilities, QuarterRoadFitter,
  QuarterTypes, SectionFrameBuilders, VerticalProfilePlanner, CrossSectionPlanner,
  ConnectorAnalyzer, TransitionResolver, TrackRetopology, RouteTimeEstimation
        ↓
Macro (data model + geometry realisation)
  TrackMacroSectionDefinition, GeneratedTrackSection, TrackConnectionFrame,
  TrackCrossSectionProfile/Evaluator, BoxPrismTrackMeshBuilder, TrackGuideMarkingBuilder,
  MacroTrackDebugVisualizer, ResolvedTrackGenerationConfig, SemanticElementId, ConnectorBehavior
        ↓
Orchestration (scene lifecycle — MonoBehaviour)
  TrackGenerator  (+ TrackGeneratorEditor, TrackInspectorPreviewCache, TrackMaterialGenerator)
        ↓
Race / runtime consumers
  RaceCourseBuilder, RaceCourse, RaceGate, RaceHUD, MacroTrackSampler
Runtime support: Gravity (field/zone/receiver), Stunts (BoostPadActor), TrackSurfacePhysics
Diagnostics: TrackDebugReportExporter, TrackGeometryDiagnostics, TrackMeshIntegrityDiagnostics
```

**Observation (CONFIRMED):** the Planning layer produces **pure data** (`GeneratedTrackLayout`
of `GeneratedTrackSection`/frames); the Macro/mesh layer turns that into GameObjects. Planning
never touches scene objects. That separation is a genuine architectural strength and should be a
non-negotiable invariant of the Stable Version.

---

## 3. File / Class Map (primary files)

Sizes are lines of code. Roles: **E** editor-only, **R** runtime, **P** planning (pure data),
**D** data model, **X** diagnostics.

| File | Lines | Role | Responsibility | Key dependencies |
|---|---|---|---|---|
| `Planning/TrackTopologyPlanner.cs` | 3113 | P | Horizontal skeleton, corner/closure solve, elevation walk, capacity, quarter stamping, feature placement | Feature*, Quarter*, PlanRandomStreams, cfg |
| `Planning/SectionFrameBuilders.cs` | 1943 | P | Turns definitions → ring frames: arcs, ramps, loops, corkscrews, rotational events, pitch profiles, orientation transport | TrackConnectionFrame, cfg |
| `TrackGenerator.cs` | 1496 | R/E | **Orchestration + scene/editor lifecycle**: run pipeline, build GOs, reconcile roots, repair materials/race course, place craft, cache | Pipeline, MeshBuilder, RaceCourseBuilder |
| `Editor/TrackGeneratorEditor.cs` | 1402 | E | Inspector: seed entry, generate buttons, preview cache, preset UI | TrackGenerator, PreviewCache |
| `Planning/FeaturePatterns.cs` | 1380 | P | Feature recipes (loop, corkscrew, spiral, half-loop, jump chain, pipe, wallride, compounds) + `JumpBallistics` | cfg, rng, section defs |
| `Validation/TrackValidators.cs` | 1173 | P | 8 validators incl. spatial hash clearance, transition rates, rotational-event 3D clearance, dual-road pair clearance, ballistic capture | layout, plan, cfg |
| `Planning/TrackCandidateBuilder.cs` | 1167 | P | Definitions → built layout: frame walk, **global banking field**, wall-mask, vertical & cross-section planners, subdivision | FrameBuilders, Vertical/CrossSection planners |
| `Planning/QuarterRoadFitter.cs` | 987 | P | Dual-road quarter fitting (two balanced routes), archetype balance | QuarterTypes, RouteTime |
| `Macro/ResolvedTrackGenerationConfig.cs` | 931 | D | Immutable resolved limits/speeds/gravity/feature rules; clamp + issue reporting | TrackDesignerSettings |
| `Design/TrackDesignerSettings.cs` | 920 | D | Authoring settings (scale, elevation, features, road profile) | ScriptableObject data |
| `TrackConfig.cs` | 824 | D | Static limits + jump/length budget math | — |
| `Macro/BoxPrismTrackMeshBuilder.cs` | 674 | R | Solid prism mesh + **separate coarser chain colliders** (PhysX 2²¹ chunking) | TrackCrossSection, frames |
| `TrackDebugReportExporter.cs` | 624 | X | Full text report: failures, metrics, transitions, connector decisions | Result/Report |
| `Macro/TrackGuideMarkingBuilder.cs` | 589 | R | Lane/edge line meshes from the width field | frames, cfg |
| `Macro/MacroTrackDebugVisualizer.cs` | 528 | X | Scene gizmos/labels: annotations, frames | GeneratedTrackSection |
| `Planning/TrackRetopology.cs` | 516 | P | Ring redistribution / resampling | frames |
| `Planning/VerticalProfilePlanner.cs` | 495 | P | **Chain-level continuous vertical field** (jerk-limited, exact arc-length κ, mesh-independent validation, weld-exact) | frames, cfg |
| `Design/TrackStylePreset.cs` | 459 | D | Named style presets | settings |
| `Race/RaceCourseBuilder.cs` | 392 | R | Build start/finish + branch-aware checkpoint groups | MacroTrackSampler, RaceGate |
| `Planning/TrackGenerationResult.cs` | 389 | D | Result + **`GenerationFailureReason` (24 reasons)** + metrics + records | — |
| `Planning/TrackGenerationPipeline.cs` | 387 | P | **The candidate loop**: plan/build/validate/score/select, elevation rerolls, watchdog | Planner, Builder, Validators |
| `TrackMeshIntegrityDiagnostics.cs` | 375 | X | Read-only mesh integrity baseline | mesh |
| `Macro/TrackMacroSectionDefinition.cs` | 352 | D | **The section plan** (type, length, radius, bank, contract, rotational phases, jump diagnostics) | enums, contract |
| `Planning/ConnectorAnalyzer.cs` | 339 | P | Classify straights between content (bridge/blend/absorb) | defs |
| `Planning/RouteTimeEstimation.cs` | 323 | P | Neutral lap-time estimate per archetype | CraftArchetypeProfile |
| `Race/RaceCourse.cs` | 320 | R | Lap timing, branch-aware checkpoint groups | RaceGate |
| `Macro/TrackCrossSectionEvaluator.cs` | 271 | R | Evaluate cross-section point ring | profile, frame |
| `Planning/CrossSectionPlanner.cs` | 237 | P | **Canonical width/side-height field**, distance-limited, anchor-locked | frames, cfg |
| `Macro/TrackCrossSectionProfile.cs` | 231 | D | Cross-section shape parameters | — |
| `Planning/FeaturePlanning.cs` | 221 | P | Feature plan result type + helpers | — |
| `Planning/FeatureCapabilities.cs` | 218 | P | **Static capability registry** (entry windows, recovery, upright) — Stage B | SemanticElementId |
| `Planning/TransitionResolver.cs` | 207 | P | Classify every content boundary (Stage C, reporting) | contracts, frames |
| `Race/MacroTrackSampler.cs` | 105 | R | Interpolate a frame at arbitrary arc length (gate placement) | frames |
| `Macro/TrackConnectionFrame.cs` | 113 | D | **The geometric source of truth** (per-frame basis + metadata) | — |
| `Macro/GeneratedTrackSection.cs` | 93 | D | One built section: def + frames + bounds + open/closed metadata | frame, def |
| `Core/TrackSeed*.cs` | 115/193/134 | P | Deterministic seed + per-subsystem streams | Unity.Mathematics.Random |
| `ITrackRaceCraft.cs` | 39 | R | **Runtime craft contract** (transform, rigidbody, ride height, placed callback) | — |

(20 test files under `Planning/Tests` and `Tests/Editor` — see §22.)

---

## 4. Current Generation Pipeline (traced, not assumed)

Source: `TrackGenerationPipeline.Run` (`TrackGenerationPipeline.cs:44-209`), invoked by
`TrackGenerator.RunPipelineWithPolicy` (`:418`) inside `GenerateInternalCore` (`:359`).

| Stage | Class.method | Input | Output | Failure modes (structured) |
|---|---|---|---|---|
| Config resolve | `ResolvedTrackGenerationConfig` (from settings) | `TrackDesignerSettings` | immutable `cfg` + issues | `InvalidConfiguration` (hard errors abort, `:60`) |
| Seed / streams | `TrackSeed.CreateSubsystemRandom`, `TrackSeedStreams.AttemptRandom` | seed + attempt index | per-subsystem `Random` | — (deterministic) |
| Candidate loop | `Pipeline.Run` `for attempts < cfg.MaxAttempts` (`:81`) | cfg, seed | best candidate | `GenerationTimeBudgetExceeded` (60 s watchdog `:83`) |
| Topology plan | `TrackTopologyPlanner.Plan` (`:106`) | cfg, `PlanRandomStreams` | `TopologyPlan` (corners, features, quarters, elevation) | Closure*, `SelfIntersection`, `InsufficientLengthBudget`, `RequiredFeatureMissing`, `QuarterGateFailure`, `DualRoadFit*` |
| Elevation reroll | `Pipeline` inner loop (`:115-128`) | plan that folds in 2D | plan with vertical separation | up to 6 Elevation-stream rerolls; keeps layout/feature/quarter streams |
| Candidate build | `TrackCandidateBuilder.Build` (`:136`) | plan, cfg | `GeneratedTrackLayout` (frames) | `MeshBuildFailure`, vertical-infeasible, dual-road fit |
| — banking field | `TrackCandidateBuilder` global banking pass | frames | banked frames | — |
| — vertical field | `VerticalProfilePlanner.TryApply` (`TrackCandidateBuilder.cs:170`) | chain frames | continuous vertical | `SlopeViolation`/curvature infeasible → attempt rejected |
| — cross-section | `CrossSectionPlanner.Apply` (`:265` via `ApplyCrossSectionBlend :1141`) | frames | canonical width/side-height | — |
| Validate | `TrackValidators.RunAll` (`:143`) → 8 validators | layout, plan, cfg | `List<ValidationIssue>` | any of the 24 reasons; error ⇒ attempt rejected `:154` |
| Metrics | `Pipeline.FillMetrics` (`:213`) | layout | counts, elevation, rings, neutral lap time | — |
| Score | `Pipeline.ScoreCandidate` (`:322`) | valid candidate | float | — |
| Select | best-score scan (`:184-192`); `FirstValid`/`CandidatesToScore` early-out (`:166`) | candidates | winner | `candidates==0 ⇒ Success=false` |
| Realise (scene) | `TrackGenerator.TryBuildTransactional` (`:610`) → `BoxPrismTrackMeshBuilder` | layout | GameObjects, meshes, colliders | `MeshBuildFailure` |
| Race course | `RaceCourseBuilder` (via TrackGenerator) | layout | start/finish + checkpoint groups | `RaceCourseBuildFailure` |
| Place craft | `TrackGenerator.TryPlaceHovercraftAtTrackStart` (`:1224`) | `ITrackRaceCraft` | craft teleported | (soft) |

**Side effects live only in the realise stage and beyond** (GameObject creation, mesh cooking,
material assignment, craft teleport). Planning/validation are pure. (CONFIRMED.)

**Policy wrapper (CONFIRMED, `TrackGenerator.RunPipelineWithPolicy :418` + `RelaxOptionalSettings
:471`):** if a strict pass yields no candidate, an optional **relaxed pass** loosens non-essential
settings and retries — a deliberate two-tier strategy rather than infinite retry.

---

## 5. Source of Truth / Data Model

**CONFIRMED — there is one geometric source of truth: the `TrackConnectionFrame` stream.**

- `TrackConnectionFrame` (`TrackConnectionFrame.cs:13-112`) carries `Position, Forward, Right, Up,
  Width, BankAngle, PitchAngle, AccumulatedRoadRoll, AccumulatedVerticalRotation,
  Horizontal/VerticalCurvature(+Rate), RoadRollRate/Accel, ArcLength, LapProgress, SideHeight,
  wall suppression/overhang/pipe/wallride morph`. The tilt is **baked into Right/Up** — the mesh
  reads the basis directly.
- The connection invariant is explicit: *"a section's exit frame IS the next section's entry
  frame"* (`TrackConnectionFrame.cs:8-9`), enforced in `GeneratedTrackSection` (`StartFrame`
  ≡ previous `EndFrame`, `:20-24`) and re-copied canonically in `VerticalProfilePlanner`
  (`:244-251`).
- `TrackMacroSectionDefinition` is the **plan** (intent: type/length/radius/bank/contract/phases),
  explicitly *"the plan, not the geometry"* (`TrackMacroSectionDefinition.cs:195-196`). Frames are
  the geometry. This is a clean plan/realisation split.
- Downstream consumers (mesh, markings, colliders, race gates via `MacroTrackSampler`) all read
  the **same frames**. `MacroTrackSampler.TrySampleFrame` (`MacroTrackSampler.cs:28`) interpolates
  frames for gate placement — checkpoints are derived from the frame stream, **not** a parallel
  control-point list.

**Divergence risk (the classic failure the brief warns about) — LOW but not zero:**
- `Definition.ElevationChange` / `Contract.ElevationDelta` are **derived** from frames after the
  vertical solve (`VerticalProfilePlanner.cs:253-257`), so def and geometry are reconciled. Good.
- HIGH CONFIDENCE risk: some `Definition` fields (e.g. `Radius`, `BankingAngle`, `TurnAngle`) are
  *inputs* consumed once by the frame builder and **not** re-derived back from the built frames, so
  after field passes (banking blur, wall mask, vertical solve) the **definition can lag the actual
  geometry**. Consumers that read the definition instead of frames (some scoring, some reporting)
  could see stale values. This is a *metadata-consistency* concern, not a geometry-integrity one.
  → Stable Version should define, per definition field, whether frames or def is authoritative, and
  make the direction explicit (§ Stage 2).

---

## 6. Path & Spline Architecture

**CONFIRMED — the path is NOT a spline object.** There are no Bézier/Catmull-Rom curve objects as
the authoritative path. The driving line is produced **directly as ring frames** by
`SectionFrameBuilders` (`:265-306` dispatch): straights, arcs (`BuildArc`), keyframed pitch ramps,
loops (`BuildLoop`), corkscrews (`BuildCorkscrew`), half-loop rollout, and the unified
`BuildRotationalEvent`. Curves are analytic arcs sampled into frames; there is no global spline
that the mesh re-samples.

Continuity guarantees (CONFIRMED via `TrackValidators.ValidateFrameContinuity :177` and the frame
builders):
- **Position continuity:** enforced — exit ≡ next entry (`:206` distance check), spatial-hash
  self-intersection ensures the *rest* of the track doesn't collide.
- **Tangent continuity:** frames carry `Forward`; continuity validated (`Forward.sqrMagnitude≈1`
  check `:303`); orientation transport keeps `Forward` continuous across joins.
- **Curvature continuity:** partially guaranteed. Loops/corkscrews use **jerk-limited pitch
  profiles** (`BuildPitchProfile`, `LoopCurvatureEaseFraction 0.18` `SectionFrameBuilders.cs:61`)
  that ease κ from ~0; the new `VerticalProfilePlanner` bounds true arc-length κ and dκ/ds
  (`:396-412`). But ordinary curve↔straight joins are C¹ arcs (curvature step at the join is
  possible) — `ValidateTransitionRates` (`:520`) polices the *rate*, so abrupt steps are rejected
  rather than smoothed. HIGH CONFIDENCE.
- **Roll continuity:** banking is a **global field** (blurred), and `RoadRollRate`/`Acceleration`
  are tracked and rate-limited (`MaxRollRateDegPerMeter`, config `:532`). Corkscrew roll is
  unwrapped/accumulated so it composes smoothly.

**Racing-gameplay lens (HIGH CONFIDENCE):** the system already evaluates the path as a *drivable*
thing, not just a mesh — neutral lap-time estimation (`RouteTimeEstimation`), transition-rate
limits scaled by design speed, and per-feature entry windows exist. What is missing is **carried
speed state** (see §18).

---

## 7. Orientation / Coordinate Frame Architecture

This is the area the brief flags for deepest scrutiny; it is **a relative strength**, not a
weakness.

**Method (CONFIRMED):** quaternion transport with explicit re-orthonormalisation, plus **unwrapped
accumulated rotation channels**. Evidence:
- `VerticalProfilePlanner.cs:222-229`: new forward = `(planForward·cosθ + up·sinθ).normalized`;
  `transport = Quaternion.FromToRotation(oldForward, newForward)`; right is transported then
  **Gram-Schmidt-orthogonalised against forward**, with a degenerate-case fallback
  (`Cross(up, forward)`), then `up = Cross(forward, right)` and right re-crossed. This is textbook
  stable frame propagation (parallel-transport-style, minimal-rotation), not naïve `LookRotation`.
- `TrackConnectionFrame` stores **`AccumulatedRoadRoll`** and **`AccumulatedVerticalRotation`**
  *unwrapped* — the tooltip explicitly notes *"0, 360, 720 and 1080 remain distinct"*
  (`:36-40`). This is exactly what prevents 180°/360° flip ambiguity in loops and corkscrews.
- Loops/corkscrews build from analytic pitch/roll programs integrated as continuous phase streams
  (`BuildRotationalEvent`, `SectionFrameBuilders.cs:306`), so orientation is *authored by the
  feature's program*, not re-derived by a global Frenet normal that would flip at inflections.

**Why the classic failures are structurally avoided (HIGH CONFIDENCE):**
- *Sudden flips / 180° instability:* avoided by unwrapped accumulators + `FromToRotation` minimal
  transport rather than world-up `LookRotation`.
- *Accumulated roll error:* roll is intentional and tracked as a rate/accumulation, validated by
  `ValidateRotationalEvents` (`:348`) and `MaxRollRateDegPerMeter`.
- *Frame discontinuities / gimbal:* re-orthonormalisation every ring; no Euler integration of the
  basis.

**Residual concerns (POSSIBLE — need runtime confirmation):**
- The orientation transport logic exists in **more than one place** (`VerticalProfilePlanner`,
  `SectionFrameBuilders`, `TrackCandidateBuilder`, `TrackRetopology`, `FeaturePlanning` all appear
  in the quaternion grep). If these implement subtly different transport conventions, a feature
  built in one and re-touched in another could drift. → Stable Version should centralise a single
  `TrackFrame.Transport(...)` helper that every builder calls (§ Stage 3). This is the **single
  highest-value orientation action** and it is consolidation, not redesign.
- Surface-normal continuity **through** inverted sections (what the hovercraft needs) is produced
  by the feature program; that it is *usable by the craft grounding* is a contract requirement to
  confirm with the vehicle owner (§30), not a generator bug.

---

## 8. Mesh Generation

`BoxPrismTrackMeshBuilder` (`:1-674`). CONFIRMED facts:
- Builds **solid prisms** (real thickness, real overhangs, closed pipes) — not a ribbon
  (`:19-21`). Cross-section from `TrackCrossSection` sampled per ring frame.
- Path and mesh are **cleanly separated**: the builder consumes finished frames; it does no
  planning. (Matches the ideal in the brief.)
- Segment seams: because exit≡entry frame, consecutive section meshes **share identical ring
  geometry** — no cracks by construction. Open/closed boundary metadata (`GeneratedTrackSection`
  `CapStart/CapEnd/OpenStart/OpenEnd/ConnectsToAirGap :32-46`) controls caps so jump lips/landing
  mouths stay open and everything else is capped.
- Air gaps emit no geometry (`IsEmptySpace`, `BoxPrismTrackMeshBuilder.cs:112`).

Risks (HIGH CONFIDENCE / POSSIBLE):
- Geometry volume is large (mesh integrity diagnostics + collider chunking exist precisely because
  triangle counts are high). Not a correctness bug; a cost (see §23/perf).
- Cross-section rotation smoothness now depends on `CrossSectionPlanner`; before it existed the old
  per-section blend produced wall-top spikes (documented history). CONFIRMED the planner is wired
  (`:265`), so this is largely addressed — but the **old `ApplyCrossSectionBlend` name is now a
  thin adapter** (`:1141-1143`) and the legacy path should be confirmed dead (§24).

---

## 9. Collider Generation

CONFIRMED (`BoxPrismTrackMeshBuilder.cs:40-126, 205-225`):
- Colliders are **built separately from the visual mesh** with their own **coarser profile
  resolution** (`ColliderProfileResolution` default 40, capped ≤ visual resolution `:97`) and a
  thicker shell (`ColliderThickness 5f :44`).
- Continuous **chain colliders chunked under the PhysX 2²¹ triangle limit** (`:31, :126`).
- Comment documents the real cost driver: *"PhysX must RE-COOK every one whenever Unity restores
  the track… ~8.7 M triangles per restore"* (`:57-63`) — the resolution was deliberately tuned to
  balance a `~35°` collider "cliff" at the floor→wall shoulder against cook cost.

Assessment: rendering and collision are **not** unnecessarily coupled (separate resolution,
thickness, chunking) — a KEEP. The **cook cost on domain reload / play-exit is the main perf
liability** and is already a tracked concern (perf tasks #24-27, crash task #28). CONFIRMED.

---

## 10. Segment Architecture

CONFIRMED — the generator is **explicitly segment-based** at two levels:

1. **Macro section types** (`TrackMacroSectionType`, `TrackMacroSectionDefinition.cs:12-46`):
   Straight/Wide/Boost/Recovery, BankedCurve, BankedHairpin, SCurve, Chicane, JumpRamp/AirGap/
   LandingRamp, Loop, Corkscrew, Spiral, HalfLoopTwist, FullPipe, WallrideTurn, Tunnel/Bridge
   variants, and a unified **RotationalEvent**. Enum values are **explicitly numbered with holes
   preserved** for scene-serialization safety (`:14-16`) — mature data-migration discipline.
2. **Semantic elements** (`SemanticElementId`) + **capability contracts** (`FeatureCapabilities`)
   — a *higher* abstraction than raw type, describing the gameplay role.

Per-segment metadata is rich (`TrackMacroSectionDefinition`): speed intent, risk level,
`RequiresRecoveryAfter`, `AllowsBoost/Jump`, `LockLength`, `MinimumLength`, quarter/road identity,
pattern grouping (`PatternId` = atomic feature group), connector behaviour, semantic element,
**and a `SectionConnectionContract`** (entry/exit orientation + heading/pitch/roll/elevation
deltas). Rotational features carry ordered `RotationalPhaseDefinition` phases.

Conclusion: the "should we introduce segment concepts?" question is **already answered yes**; the
Stable Version refines, not invents.

---

## 11. Banked Turns

CONFIRMED:
- Banking is a **global field** computed in `TrackCandidateBuilder` and blurred, *not* per-section
  mesh rotation — `SectionFrameBuilders.cs:52-58` explicitly notes banking/wall support "come from
  the candidate builder's GLOBAL banking field." The tilt is baked into frame `Right/Up`.
- `def.BankingAngle` seeds the intent (`BuildArc(..., def.BankingAngle, ...)` `:273-283`); the
  field smooths entry→hold→exit across neighbours (banking-field blur, task #9). Roll rate is
  bounded (`MaxRollRateDegPerMeter`).
- Entry windows per feature declare `MaxEntryBankDeg` (corners 35°, inversions 8°, jumps 5°,
  recovery 60° — `FeatureCapabilities.cs:83,118,185,209`).
- Wallride is an extreme-bank corner sharing the **same eased horizontal arc as a banked curve**
  (`SectionFrameBuilders.cs:980-1003`) — features compose on shared geometry (a KEEP).

Assessment: banking is already "deliberate track geometry" as the brief wants. Gap: the **bank
amount is not yet chosen from curvature × carried speed** (it is intent + field smoothing); tying
it to a speed-state model is a §18 enhancement, not a fix.

---

## 12. Loops

CONFIRMED — loops are a **natural extension of the frame representation**, not a special-case hack:
- `BuildLoop` (`SectionFrameBuilders.cs:294`) integrates a precomputed **jerk-limited pitch
  profile** over 2π (`EnsureLoopProfile → BuildPitchProfile(2π, LoopCurvatureEaseFraction=0.18)`
  `:386-389`), giving approach-ease → climb → apex → descent → exit-ease with κ easing from ~0.
- Analytic helpers: `LoopArcLength`, `LoopForwardDisplacement`, `LoopLateralOffset` (`:455-465`) —
  the loop's footprint is derived, feeding closure and clearance.
- Clearance: `ValidateRotationalEvents` measures **3D internal clearance**
  (`MeasureRotationalEventClearance`, `TrackValidators.cs:500`) so the loop's own tunnel-through
  doesn't self-collide, with the same-pattern exemption handled deliberately (`:494-513`).
- Orientation through inversion uses unwrapped `AccumulatedVerticalRotation`.

The full loop gameplay chain (approach/entry/apex/descent/exit/recovery) exists: inversion
features declare `RequiresUprightEntry` and recovery obligations (`FeatureCapabilities.cs:113-127`).
KEEP.

## 13. Corkscrews

CONFIRMED — corkscrews are **more than "forward spline + rotate mesh around tangent"**:
- Built via `BuildCorkscrew` / unified `BuildRotationalEvent` (`:297,306`). Roll is a real
  program: `RotationalPhaseDefinition` carries `CenterlineOrbitDegrees` — the tooltip states a
  positive value makes *"a true rising/falling corkscrew whose curvature rotates with the road
  surface and returns to a level exit"* (`TrackMacroSectionDefinition.cs:108`), i.e. the centreline
  actually orbits; it is not a flat barrel with a spinning cross-section.
- Twist distribution: phases are *shaping regions* with blend presets (`RotationalBlendPreset`),
  first/second-half lengths/radii, yaw/pitch bias that returns to zero at the midpoint
  (`:82-123`) — controlled entry/exit/recovery.
- Roll accumulation unwrapped; roll rate validated; entry window declared
  (`InlineCorkscrew`/`DoubleCorkscrew` 25° bank, `FeatureCapabilities.cs:132-148`).
- **Directional corkscrew** (turn-consuming barrel) is **in-flight, incomplete** (Stage F, tasks
  #13/#15/#16; `FeatureCapabilities.cs:155-163` notes *"live selection pending in-engine craft-
  retention tuning"*). CONFIRMED as declared-but-not-live.

## 14. Jumps

CONFIRMED — jumps are a complete procedural feature chain, solved ballistically:
- Chain = `JumpRamp` → `AirGap` → `LandingRamp` (`IsJumpFamily`, `:342-345`).
- **`JumpBallistics.TrySolve`** (`FeaturePatterns.cs:96-102`) solves the launch from gravity and
  airtime; the **gap distance is derived from the trajectory, never random**
  (`TrackMacroSectionDefinition.cs:273`).
- Jump diagnostics are now first-class on the definition: `AirtimeSeconds`, `JumpApexHeight`,
  `JumpTimeToApex`, and **three-speed capture flags** `JumpCapturesMinimum/Nominal/MaximumSpeed`
  (`:273-285`) — matching the objective-driven jump design from the geometry plan.
- `ValidateSelfIntersection` appends **ballistic flight samples** for the gap
  (`AppendBallisticFlightSamples`, `TrackValidators.cs:1147`) so the flight path is included in
  clearance.
- Landing capture width/length and recovery are modelled (config length budget
  `ResolvedTrackGenerationConfig.cs:761`; `Jump` capability `RequiresRecoveryAfter + EmitsOwnApproach/
  Recovery`, `FeatureCapabilities.cs:180-191`).

Vehicle information the jump solver needs (already consumed): `DesignSpeedMps`, `Gravity`,
`MaxJumpAirtimeSeconds`, launch-pitch safety range. See §30 for the contract.

---

## 15. Spatial Validation

CONFIRMED — this is a strength. `TrackValidators.ValidateSelfIntersection` (`:734`):
- **Broad phase:** XZ **spatial hash** with cell size = clearance (`:771-779`).
- **Narrow phase:** precise centreline clearance + **vertical clearance** (`VerticalClearance`
  `:769`), so it is *not* centreline-only — `UnrelatedCorridor` (`:767`) encodes required lateral
  separation, i.e. **full-corridor** separation.
- **Exemption logic distinguishes legal proximity from illegal overlap** (`:730-732`): same pattern
  (loop self-proximity), same branch group (intended paired dual-road routes), adjacent arc
  positions are exempt; unrelated overlap is rejected.
- **Overhead / tunnel-through** handled via the vertical-clearance test + rotational-event 3D
  internal clearance (`:500`).
- **Ballistic flight** included for jumps (`:1147`).
- Dual-road pair clearance is a dedicated check (`ValidateRoadPairClearance :1044`).

Gap (HIGH CONFIDENCE): clearance is measured on centreline + corridor width; it does **not** appear
to test the *full solid collider mesh* overlap (that would be expensive). For near-passes this is
adequate given the corridor margin, but the exact clearance value's relationship to **actual
mesh/collider width including banking/overhang** should be validated (§ Stage 6). This is a
precision question, not a missing capability.

## 16. Multi-Layer Validation Model

The brief's seven validity levels, mapped to what exists:

| Level | Exists? | Evidence |
|---|---|---|
| 1 Structural | CONFIRMED | config resolve issues; `InvalidConfiguration`; frame `sqrMagnitude≈1` checks (`:303`) |
| 2 Geometric | CONFIRMED | `ValidateFrameContinuity`, `ValidateRingQuality`, `ValidateTransitionRates`, curvature/κ bounds |
| 3 Spatial | CONFIRMED | `ValidateSelfIntersection` (hash + corridor + vertical), rotational 3D clearance, dual-road pair |
| 4 Traversability | PARTIAL | transition-rate/curve-radius/slope/roll-rate limits scaled by design speed; feature entry windows exist. But **no carried-speed feasibility** (a segment isn't rejected for being un-enterable at its actual arrival speed) |
| 5 Sequence | PARTIAL | contracts + `TransitionResolver` classify boundaries (**reporting only, Stage C** — `TransitionResolver` comment); capabilities declare recovery/upright needs, but **selection does not yet reject on contract mismatch** |
| 6 Gameplay readability | IMPLICIT | scoring penalises repetition, facet spikes, ring overrun; no explicit readability validator |
| 7 Pacing quality | IMPLICIT | scoring nudges toward requested feature groups / lap time; **no explicit pacing model** |

So levels 1–3 are solid and observable; **4–7 are the maturation frontier** and are exactly what
the declared-but-unconsumed contracts were built to enable. The Stable Version's core value is
**promoting 4–5 from reporting to enforcement**, then adding 6–7 minimally.

## 17. Generation Failures / Retry Logic

CONFIRMED — already structured and observable:
- **`GenerationFailureReason`** enum, 24 reasons (`TrackGenerationResult.cs:9-35`).
- Every failed attempt records `AttemptIndex, Reason, Subject, QuarterOrRoad, RequestedValue,
  AchievedValue, Position, Message` (`GenerationAttemptFailure :42-64`). No silent failure.
- Retry strategy is **bounded and deterministic**: `MaxAttempts`; a **targeted elevation reroll**
  (6×) that only re-rolls the Elevation stream to vertically separate 2D-folded layouts
  (`Pipeline :115-128`); a **60 s watchdog** (`:83`); and a policy-level **relaxed second pass**
  (`RunPipelineWithPolicy` + `RelaxOptionalSettings`). Validation order **never touches the random
  streams** (`:66-68`) — a deliberate determinism guarantee.

Gap: it does **not** backtrack per-segment (see §18). The report is exportable
(`TrackDebugReportExporter`, 624 lines). KEEP the whole failure-observability design.

## 18. Backtracking / Recovery Strategy

CONFIRMED — the generator is **whole-track candidate-based, not incremental-with-backtracking**.
When a layout can't be completed it **rejects the entire attempt and resamples** (next attempt
seed), except for the one targeted elevation-reroll optimisation. There is no "regenerate segment
4 and keep 1-3."

Assessment: for this architecture (global horizontal closure solved up front, then features/quarters
placed) whole-attempt resampling is a **reasonable and deterministic** choice — it avoids the
partial-state corruption that segment backtracking risks, and the elevation-reroll shows targeted
recovery is added *only* where it pays. The risk the brief warns about ("relying on luck") is
mitigated by: structured closure solving (not random walk), the relaxed second pass, and scored
selection. HIGH CONFIDENCE this is adequate; a future per-quarter resample could improve yield on
dual-road failures (open decision §34), but is **not** required for stability.

## 19. Determinism

CONFIRMED — determinism is a first-class, well-engineered property:
- All generation randomness uses **`Unity.Mathematics.Random`** (a deterministic PRNG), seeded from
  `TrackSeed` via **per-subsystem streams** (`TrackSeed.CreateSubsystemRandom :53`,
  `TrackSeedStreams.AttemptRandom :101`, `PlanRandomStreams` Layout/Feature/Elevation/Quarter).
- **No `UnityEngine.Random`** is used in generation (grep: only *comments* referencing it in
  `TrackSeedManager`). 
- Nondeterminism sources are **intentional and isolated**: minting a *fresh* random seed uses
  `Environment.TickCount ^ Guid.NewGuid()` (`TrackSeedManager.cs:190`, `TrackSeedStreams.cs:70`) —
  only when the user asks for a new seed, not during reproduction. Editor temp-file names use a
  Guid (`TrackGeneratorEditor.cs:266`) — irrelevant to geometry.
- Explicit invariant in code: *"mesh density or validation changes can never alter which attempt
  seeds are drawn"* (`Pipeline :66-68`); *"the same seed + settings always produce the same
  selected result"* (`:14-16`).

Conclusion: **Seed 482914 reproduces the same track.** (HIGH CONFIDENCE — behavioural test
recommended in the matrix to lock it.) This is a KEEP and a model for the rest of the system.

## 20. (see §17 — Generation failures/retry)

## 21. Determinism — see §19.

## 22. Debugging / Editor Workflow

CONFIRMED — strong for a system this complex:
- **Editor inspector** (`TrackGeneratorEditor`, 1402 lines): seed entry, generate buttons, and
  **granular partial regeneration** — `GenerateNewEverything`, `RegenerateSameSettings`,
  `SameLayoutNewContent`, `SameGeometryNewSurface`, `NewFeaturesOnly`, `NewQuarterContentOnly`,
  `NewVisualsOnly`, `RandomizeAllUnlocked` (`TrackGenerator.cs:252-317`). This *is* the
  "enter seed → generate → inspect → tweak → regenerate same seed" loop the brief wants.
- **Preview cache** (`TrackInspectorPreviewCache`, `TryGetEditorPreviewCacheSnapshot :767`) so a
  generated track survives inspector reloads.
- **Scene visualisation** (`MacroTrackDebugVisualizer`, 528 lines): annotations/labels/frames,
  restored via `RestoreDebugAnnotations` (`:1112`). (Recent history: annotation duplication was
  fixed; the always-on overlay file is now intentionally empty.)
- **Text report** (`TrackDebugReportExporter`, 624 lines; `ExportDebugReport :740`): failures with
  reasons, metrics, connector decisions, transition classifications.
- **20 test files** under `Planning/Tests` + `Tests/Editor` (feature contracts, fuzz, geometry).

Gap vs the brief's wish-list: rejected-candidate geometry, collision-test visualisation, jump-arc
overlays, and speed-requirement colouring are **not** all visualised (the *data* exists in the
report; the *scene gizmos* are mostly the selected track). Low-priority enhancement (§ Stage 10).

## 23. Performance

Two categories (CONFIRMED concerns, already partly tracked in tasks #24-28):

**Generation-time:**
- Candidate loop cost is bounded by `MaxAttempts` + 60 s watchdog. Each attempt does a full plan
  + build + 8 validators; the spatial hash keeps clearance near-linear.
- `TrackTopologyPlanner` (3113 lines) is the heaviest planning stage; elevation rerolls add up to
  6 replans per attempt (cheap vs a full build, by design).
- **Collider cook** dominates realise cost — *~8.7 M triangles per restore* documented
  (`BoxPrismTrackMeshBuilder.cs:60`); the **Apply-Presets→Generate crash (#28)** is HIGH CONFIDENCE
  an OOM/native-cook spike here or in mesh allocation.

**Runtime:**
- Colliders are static mesh colliders (no per-frame path math for physics).
- `MacroTrackSampler` is O(sections) per query (gate placement / progress) — fine at current gate
  counts; if queried per-frame per-craft it would need an arc-length index (POSSIBLE, low impact
  now).
- `GravityField/Zone` receivers — runtime cost UNKNOWN / REQUIRES VALIDATION (not deeply read; out
  of core scope).

Ranking by likely impact: **(1) collider cook / restore (crash-level, #28)** ≫ (2) topology-planner
allocations per attempt ≫ (3) mesh triangle volume ≫ (4) sampler queries. Do not optimise 2-4
before 1 is resolved.

## 24. Dead / Legacy Track Code

Classified (do not delete during audit):

| Item | Evidence | Class |
|---|---|---|
| `Editor/TrackAnnotationSceneOverlay.cs` | intentionally empty; comment *"Safe to delete this file and its .meta"* (`:1-8`) | **REMOVE** (confirmed obsolete) |
| Old split/merge section types (enum 11-13) | holes preserved deliberately (`TrackMacroSectionDefinition.cs:14-16`) | **KEEP holes** (serialization safety — do NOT reuse values) |
| `ApplyCrossSectionBlend` (`TrackCandidateBuilder.cs:1141`) | now a 2-line adapter delegating to `CrossSectionPlanner` | **MERGE/VERIFY** — inline the adapter or confirm callers |
| `RollChange` "0 in V1" tooltip (`:231`) | may be superseded by rotational phases | **UNKNOWN / VERIFY** |
| Directional corkscrew (Stage F) | declared, "live selection pending" | **KEEP** (in-flight, not dead) |
| `TrackRetopology` (516) | usage not fully traced this pass | **UNKNOWN / VERIFY** it is on the live path |
| Various `Definition` fields possibly stale post-field-passes | §5 divergence risk | **UNKNOWN / VERIFY** authority per field |

No abandoned parallel *generator* was found — the codebase is a single coherent generator, which is
itself notable (many procedural systems accrete two).

## 25. Root Causes (grouped, not a bug list)

**ROOT CAUSE A — Rich self-knowledge is computed but not yet enforced.**
Contracts (`FeatureCapabilities`), boundary classification (`TransitionResolver`), semantic
elements, and the canonical token solver are **declared and reported but not consumed by candidate
selection** (their own comments: *"Nothing CONSUMES them to change planning yet"*
`FeatureCapabilities.cs:22-24`; `TransitionResolver` "reporting only, the dry run for Stage D"
`Pipeline :202-205`).
*Symptoms:* traversability/sequence validity are advisory; incompatible sequences can pass because
nothing rejects on contract mismatch; Stage F features exist but can't be safely selected.

**ROOT CAUSE B — The generator MonoBehaviour owns scene/editor lifecycle *and* orchestration.**
`TrackGenerator` (1496) does root reconciliation, material repair, race-course repair, mesh
release, hovercraft placement, cache, *and* pipeline orchestration (§4 method surface).
*Symptoms:* the crash risk (#28) and repair complexity concentrate in one class; editor and runtime
concerns interleave; hard to test the "build a track" path in isolation from scene management.

**ROOT CAUSE C — Definition vs frame authority is not explicitly declared per field.**
Frames are authoritative geometry, but several `Definition` fields are write-once inputs that field
passes then supersede (§5).
*Symptoms:* potential stale radius/bank/elevation in def-reading consumers; report vs geometry
mismatches; the exact class of subtle inconsistency the brief warns about (kept LOW because
`ElevationChange` is reconciled and geometry itself stays consistent).

**ROOT CAUSE D — Speed is estimated, never carried.**
Lap time is estimated post-hoc (`RouteTimeEstimation`) and scored, but there is no per-segment
carried entry/exit speed state feeding selection or banking/jump sizing.
*Symptoms:* a loop after a hairpin isn't rejected for arriving too slow; banking isn't sized to
actual carried speed; pacing can't reason about speed build/bleed.

**ROOT CAUSE E — Orientation transport is implemented in several builders.**
(§7) Multiple files carry near-identical quaternion transport.
*Symptoms:* low risk today, but any future feature touched by two builders could drift; hard to
guarantee one convention.

## 26. KEEP — Systems Worth Preserving (explicit)

- **The candidate pipeline** (plan→build→validate→score→select) with per-subsystem seed streams,
  elevation reroll, watchdog, relaxed second pass. *(CONFIRMED strong.)*
- **`TrackConnectionFrame` as single geometric source of truth**, exit≡entry invariant.
- **Determinism design** (Unity.Mathematics.Random streams; validation never touches RNG).
- **Orientation model** (quaternion transport + unwrapped accumulators) — centralise, don't
  replace.
- **Global banking field** and **RotationalEvent** unified loop/corkscrew/spiral/half-loop model.
- **Spatial validator** (hash + corridor + vertical + exemptions + ballistic samples).
- **Structured failure model** + exportable report.
- **Chain-level `VerticalProfilePlanner`** and **`CrossSectionPlanner`** (recent, correct-by-design).
- **Segment/type + semantic-element + capability model** and the **granular partial-regeneration**
  editor workflow.
- **Separate coarser colliders** with PhysX chunking.
- **Plan/realisation separation** (planning is pure data).

## 27. Findings by Severity

**CRITICAL** (threatens reliable generation *today*):
- C1. Apply-Presets→Generate **crash** (#28) — HIGH CONFIDENCE collider-cook/mesh OOM. Blocks a
  core workflow.

**HIGH** (blocks safe future development):
- H1. Contracts/transition-resolver **not consumed by selection** (Root Cause A) — traversability &
  sequence validity are advisory.
- H2. `TrackGenerator` **scene/editor lifecycle coupling** (Root Cause B) — testability + crash
  concentration.
- H3. **No carried speed-state** (Root Cause D) — limits traversability, banking sizing, pacing.
- H4. **Spawn-below-ground** (#6, `KeepAboveStart` y<0) — a validity guarantee currently violable.

**MEDIUM** (stabilisation):
- M1. Definition vs frame **authority undeclared per field** (Root Cause C).
- M2. Orientation transport **duplicated across builders** (Root Cause E) — centralise.
- M3. **Stage F** directional corkscrew / dual-quarter fitter yield unfinished (#13-16).
- M4. Spatial clearance vs **true banked/overhang width** precision (§15 gap) — verify.
- M5. `ApplyCrossSectionBlend` adapter + legacy path confirm-dead (§24).

**LOW** (quality of life):
- L1. Remove empty `TrackAnnotationSceneOverlay.cs`.
- L2. Debug-viz for rejected candidates / jump arcs / speed colouring.
- L3. Arc-length index for `MacroTrackSampler` if it ever goes per-frame.
- L4. Explicit pacing model (only if gameplay demands it).

**KEEP:** everything in §26.

---

## 28. Proposed Track Generator Stable Architecture

The current architecture is close to the target. The Stable Version is a **consolidation** of it,
with responsibilities named as *boundaries to enforce*, not new classes to mint:

```
Authoring:      TrackDesignerSettings / Presets / Rules        (unchanged)
Resolve:        ResolvedTrackGenerationConfig                   (unchanged; add unified VehicleCapability)
Determinism:    TrackSeed / SeedStreams / PlanRandomStreams     (KEEP)
Vehicle contract: VehicleCapability (NEW, unifies ITrackRaceCraft + CraftArchetypeProfile + cfg speeds)

Generation (pure data — KEEP the pipeline shell):
  TrackTopologyPlanner        (plan skeleton + closure + elevation walk + quarters + features)
  ├ FeatureCapabilities       (contracts — PROMOTE from reporting to a selection gate)
  ├ TransitionResolver        (boundary classification — PROMOTE to enforcement)
  ├ SpeedStateModel (NEW/thin) (carry entry/exit speed windows; feeds banking + traversability)
  TrackCandidateBuilder       (frames + banking field + vertical + cross-section planners)
  ├ TrackFrame.Transport (NEW) (single orientation-transport helper every builder calls)
  ├ VerticalProfilePlanner    (KEEP)
  ├ CrossSectionPlanner       (KEEP)
  TrackValidators             (KEEP; add carried-speed traversability + contract-mismatch as ERRORS)
  Scoring/Selection           (KEEP; optionally add a minimal PacingScore term)

Realisation (scene):
  BoxPrismTrackMeshBuilder + collider (KEEP)
  TrackGuideMarkingBuilder (KEEP)

Orchestration:
  TrackGenerationService (NEW, pure): request → GeneratedTrack (no MonoBehaviour)
  TrackGeneratorHost (SLIMMED MonoBehaviour): scene lifecycle only, delegates to the service
  TrackSceneLifecycle (NEW, extracted): root reconciliation, material/race repair, mesh release, craft placement

Consumers: RaceCourseBuilder / RaceCourse / MacroTrackSampler / Gravity / Stunts (KEEP)
Diagnostics: exporter + visualizers (KEEP; extend viz)
```

Principle applied: **smallest architecture that solves the real problems.** The only genuinely new
pieces are (1) a unified `VehicleCapability`, (2) a thin `SpeedStateModel`, (3) one shared
`TrackFrame.Transport`, and (4) extraction of scene lifecycle out of the MonoBehaviour. Everything
else is *promoting existing declared systems into enforcement*.

## 29. Proposed Final Generation Pipeline

```
Seed + Resolved Config + VehicleCapability
   ↓
Topology Plan (horizontal closure, elevation walk, quarters)
   ↓
Feature placement  ──uses──▶ FeatureCapabilities (entry windows, recovery, upright)   [GATE, not report]
   ↓
Segment sequence check ──uses──▶ TransitionResolver + contracts                        [GATE, not report]
   ↓
Speed-state pass (carry entry/exit speed windows)                                      [NEW, thin]
   ↓
Candidate build → frames (single Transport helper) → banking field → vertical → cross-section
   ↓
Validation:  Structural · Geometric · Spatial · Traversability(+carried speed) · Sequence(contracts)
   ↓ each rejection carries a structured reason (unchanged)
Score (request match + clearance + repetition + minimal pacing term) → Select best
   ↓
Realise: mesh + separate collider + markings
   ↓
Race metadata (start/finish, branch-aware checkpoints) + runtime sampler
   ↓
GeneratedTrack
```

Every stage that is **new or promoted** exists to move validity levels 4-5 (traversability,
sequence) from *advisory* to *enforced*, and to let banking/jumps size themselves from real carried
speed. Stages that are unchanged are the ones §26 says to keep.

## 30. Vehicle Integration Contract (only what the generator needs)

The generator already consumes vehicle-ish data from **three** places; unify them into one
read-only `VehicleCapability` the hovercraft owner provides (no dependency on controller internals):

| Field | Already used as | Source today |
|---|---|---|
| `NominalSpeedMps` (design/race speed) | feature sizing, jump gap, transition-rate scaling | `cfg.DesignSpeedMps` (`ResolvedTrackGenerationConfig.cs:74`) |
| `MinSpeedMps` / `MaxSpeedMps` | 3-speed jump capture; speed-state windows | jump capture flags (`TrackMacroSectionDefinition.cs:283-285`); needs explicit min/max |
| `EffectiveGravity` / airborne model | ballistic jump solve | `cfg.Gravity` (`:211`), `JumpBallistics` |
| `MinTurnRadius` / `ComfortableRadius` | curve radius validation, banking | `CurveRadiusViolation` limit + banking field |
| `MaxUsableBankDeg` | banking cap, wallride | feature entry windows (`FeatureCapabilities`) |
| `MaxRollRateDegPerM` | corkscrew/loop roll rate | `cfg.MaxRollRateDegPerMeter` (`:532`) |
| `Acceleration` / `BrakingDistance` | lap-time estimate, future speed-state | `CraftArchetypeProfile` (`RouteTimeEstimation`) |
| `SafeLandingSlopeDeg` | landing ramp shape | jump landing planning |
| `RequiredTrackWidth` / `SafetyMargin` | width floor, clearance corridor | `cfg` road profile + `UnrelatedCorridor` |
| Runtime placement: `CraftTransform, CraftRigidbody, SpawnRideHeight, OnPlacedAtTrackStart()` | spawn/reset | **`ITrackRaceCraft`** (`ITrackRaceCraft.cs:10-23`) — keep as-is |

**Requirement to document for the hovercraft owner (not to fix here):** the craft needs a
*continuous usable surface normal through inverted/vertical sections*. The generator supplies this
via baked frame `Up`/`AccumulatedVerticalRotation`; the contract is that the craft's grounding
reads the track surface normal, not world-up. (This is a *contract note*, per the brief's example.)

Keep the split: **`ITrackRaceCraft`** = runtime placement (dynamic), **`VehicleCapability`** =
static generation inputs. The generator depends only on these, never on `CraftCore` internals.

## 31. Implementation / Migration Plan (ordered; DO NOT implement yet)

For each stage: **Goal · Why here · Files · Proposed change · Preserve · Risks · Dependencies ·
Validation · Architectural impact (LOW/MED/HIGH).**

**STAGE 0 — Baseline preservation.**
Goal: make current behaviour reproducible before touching anything. Why here: nothing is safe to
change until we can prove we didn't regress. Files: none modified — add a seed corpus + captured
reports. Change: record N working seeds + their exported reports + a few known-good generated
layouts; snapshot current parameters. Preserve: everything. Risks: none. Deps: none. Validation:
re-running a seed reproduces byte-identical layout data. Impact: **LOW**.

**STAGE 1 — Remove proven-dead code only.**
Goal: delete confirmed-obsolete artifacts. Files: `TrackAnnotationSceneOverlay.cs` (+.meta);
inline/confirm `ApplyCrossSectionBlend` adapter; verify `TrackRetopology` liveness. Change: delete
empty overlay; resolve M5. Preserve: enum holes (never reuse 11-13). Risks: deleting something
still referenced — mitigated by "confirmed dead" gate. Deps: Stage 0. Validation: compiles, seed
corpus unchanged. Impact: **LOW**.

**STAGE 2 — Declare data ownership (Root Cause C).**
Goal: for every `Definition` field, declare frames-authoritative or def-authoritative, and make
def-reading consumers read frames where frames win. Files: `TrackMacroSectionDefinition`,
consumers in scoring/reporting. Change: documentation + targeted redirects; optionally a
`ReconcileDefinitionFromFrames` pass. Preserve: `ElevationChange` reconciliation (already correct).
Risks: behaviour shift in scoring if a consumer silently relied on stale def. Deps: Stage 0.
Validation: report values match built geometry on the seed corpus. Impact: **MEDIUM**.

**STAGE 3 — Centralise orientation transport (Root Cause E).**
Goal: one `TrackFrame.Transport(...)` used by every builder. Files: `SectionFrameBuilders`,
`VerticalProfilePlanner`, `TrackCandidateBuilder`, `TrackRetopology`, `FeaturePlanning`. Change:
extract the shared Gram-Schmidt transport; replace call sites. Preserve: exact current numeric
result (this is a refactor, not a change) — pin with a golden-frame test. Risks: subtle numeric
drift flipping a validation result. Deps: Stage 0 (golden frames). Validation: frame outputs
identical to baseline within 1e-5. Impact: **MEDIUM**.

**STAGE 4 — Extract scene lifecycle from the MonoBehaviour (Root Cause B, enables #28 fix).**
Goal: split `TrackGenerator` into a pure `TrackGenerationService` + slim `TrackGeneratorHost` +
`TrackSceneLifecycle`. Files: `TrackGenerator`, editor. Change: move root reconciliation / material
& race repair / mesh release / craft placement / cache into `TrackSceneLifecycle`; the service
returns a `GeneratedTrack` data object. Preserve: all editor buttons + partial regen semantics.
Risks: touching the crash-adjacent code (#28) — do it carefully with the crash repro. Deps: Stages
0-3. Validation: every editor command still works; seed corpus unchanged; **#28 no longer crashes**
(target this here). Impact: **HIGH**.

**STAGE 5 — Promote contracts to a selection gate (Root Cause A, level 5).**
Goal: `FeatureCapabilities` + `TransitionResolver` reject incompatible sequences instead of only
reporting. Files: `TransitionResolver`, `TrackValidators`, planner selection. Change: add a
sequence-validity validator that emits ERRORS on contract mismatch (upright-entry, recovery,
entry-window); wire into `RunAll`. Preserve: current pass rate — start as WARNING, flip to ERROR
once the seed corpus still passes. Risks: over-rejection collapsing yield (the dual-road regression
is the cautionary tale) — gate behind measured pass rates. Deps: Stage 2. Validation: no seed that
passed before fails without a real contract violation; new bad sequences now rejected with reasons.
Impact: **HIGH**.

**STAGE 6 — Spatial validation precision (M4).**
Goal: confirm/parametrise clearance vs true banked/overhang corridor width. Files: `TrackValidators`
spatial + dual-road. Change: derive corridor half-width from actual cross-section (incl. bank/
overhang) rather than nominal width where it matters. Preserve: exemption logic. Risks: stricter
clearance reduces yield — measure. Deps: Stage 2. Validation: near-pass cases still legal, true
overlaps still caught, on an extended spatial-stress seed set. Impact: **MEDIUM**.

**STAGE 7 — Finish advanced elements on the shared architecture (M3).**
Goal: complete Stage F (directional corkscrew) + dual-quarter fitter yield (#13-16) using the same
frame/rotational-event/contract machinery. Files: `FeaturePatterns`, `SectionFrameBuilders`,
`QuarterRoadFitter`, `FeatureCapabilities`. Change: enable live selection once craft-retention
tuning + containment roll handedness are proven. Preserve: existing loop/corkscrew/jump behaviour.
Risks: new features widen the search space → yield/latency. Deps: Stages 3, 5. Validation: feature
matrix (§33) passes; clearance holds. Impact: **MEDIUM**.

**STAGE 8 — Unify the vehicle capability contract (§30, Root Cause D groundwork).**
Goal: one `VehicleCapability` object; generator reads only it + `ITrackRaceCraft`. Files: new
`VehicleCapability`, `ResolvedTrackGenerationConfig`, `RouteTimeEstimation`, jump solver. Change:
route `DesignSpeed/Gravity/archetype/radii/bank` through the object. Preserve: current numeric
values (populate the object from today's sources). Risks: accidental value change. Deps: Stage 2.
Validation: seed corpus unchanged. Impact: **MEDIUM**.

**STAGE 9 — Speed-state model (Root Cause D, level 4).**
Goal: carry per-segment entry/exit speed windows; use them for traversability rejection and to size
banking/jumps. Files: new thin `SpeedStateModel`, planner, validators, banking field. Change:
forward-simulate a coarse speed profile from `VehicleCapability`; reject segments un-enterable at
arrival speed; feed banking amount. Preserve: lap-time estimate (reuse `RouteTimeEstimation` math).
Risks: over-constraint; tuning. Deps: Stages 5, 8. Validation: a loop-after-hairpin that arrives
too slow is now rejected with a reason; banking scales with speed. Impact: **HIGH**.

**STAGE 10 — Pacing (level 7), minimal (optional).**
Goal: a small pacing score term (intensity/recovery rhythm) — NOT a director AI. Files: `Pipeline`
scoring. Change: add a bounded pacing term using existing risk/speed-intent metadata. Preserve:
current scoring weights (add, don't replace). Risks: over-engineering — keep it one term. Deps:
Stage 9. Validation: tracks show deliberate pressure/recovery without yield loss. Impact: **LOW-MED**.

**STAGE 11 — Debug visualisation (L2).**
Goal: visualise rejected candidates, jump arcs, clearance tests, speed colouring. Files:
`MacroTrackDebugVisualizer`, exporter. Change: scene gizmos from data already in the report. Deps:
Stages 5, 9. Validation: a rejected attempt can be inspected visually. Impact: **LOW**.

**STAGE 12 — Performance, verified hot paths only.**
Goal: fix collider-cook/restore cost (§23) and any measured planner allocation. Files:
`BoxPrismTrackMeshBuilder`, `TrackTopologyPlanner`. Change: only what profiling proves. Deps:
Stage 4 (crash resolved first). Validation: restore cook time + peak memory down; no geometry
change. Impact: **MEDIUM**.

**STAGE 13 — Stable release validation.**
Run the full regression matrix (§33) across the seed corpus + random seeds; confirm determinism,
graceful failure, and no yield regression. Impact: **LOW** (process).

## 32. (folded into §31)

## 33. Regression Test Matrix

Extend the existing 20-test suite. Each case: fixed seed(s), assert *structured* outcome (built +
which validators passed, or rejected + expected reason).

- **Basic geometry:** long straight; gentle turn; sharp turn; climb; descent — assert continuity +
  no self-intersection.
- **Banking:** shallow/medium/high bank; bank entry; bank exit; reversed direction — assert roll
  rate ≤ limit, bank baked in frames, smooth field.
- **Vertical:** steep climb; steep descent; near-vertical transition — assert `VerticalProfilePlanner`
  κ/dκ-ds within limits (mesh-independent), or structured `SlopeViolation`.
- **Loops:** small/large; approach; exit; loop→recovery — assert 3D internal clearance, upright
  entry contract, unwrapped vertical rotation.
- **Corkscrews:** left/right; multiple roll amounts; entry; exit — assert roll rate, centreline
  orbit, level exit.
- **Jumps:** low/nominal/high speed; landing tolerance; recovery — assert 3-speed capture flags,
  apex/time-to-apex, ballistic flight clearance, open lip/mouth boundaries.
- **Sequences:** turn→straight; straight→jump; turn→jump; jump→turn; loop→recovery;
  corkscrew→recovery; banked turn→loop; compounds — after Stage 5, assert contract-legal or
  rejected with `OrientationMismatch`/sequence reason.
- **Spatial stress:** track approaches itself; crosses above itself (vertical clearance); narrow
  vertical clearance; intentional close pass (exempt); illegal intersection (rejected
  `SelfIntersection`).
- **Determinism:** seed 482914 (and corpus) → identical layout data across runs and across mesh/
  validation changes.
- **Failure:** intentionally impossible config → fails gracefully with the correct structured
  reason (e.g. `InsufficientLengthBudget`, `RequiredFeatureMissing`), never hangs (watchdog).
- **Spawn:** every generated track's start frame is above ground (guards #6).

## 34. Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Contract gate (Stage 5) over-rejects → yield collapse | Med | High | Ship as WARNING first; flip to ERROR only when seed corpus pass-rate holds; the dual-road regression is the documented precedent |
| R2 | Extracting scene lifecycle (Stage 4) perturbs crash-adjacent code | Med | High | Do it against the #28 repro; golden-seed corpus; incremental extraction |
| R3 | Orientation-transport refactor (Stage 3) drifts numerics | Low | High | Golden-frame test at 1e-5 before/after |
| R4 | Speed-state (Stage 9) over-constrains combos | Med | Med | Bounds tuned from measured tracks; start advisory |
| R5 | Definition-authority redirects (Stage 2) change scoring silently | Low | Med | Compare full reports on corpus |
| R6 | Collider-cook fix (Stage 12) changes collision shape | Low | Med | Compare collider bounds/normals; no visual-mesh change |
| R7 | Stage F features widen search → latency/yield | Med | Med | Behind capacity guards; watchdog already caps time |
| R8 | Hidden nondeterminism surfaces under new code | Low | High | Determinism test in CI on every stage |

## 35. Open Architectural Decisions (discuss before implementation)

1. **Per-quarter resample vs whole-attempt only** (§18): add targeted dual-road backtracking, or
   keep whole-attempt resampling + relaxed pass? (Recommend: keep, revisit only if yield data
   demands.)
2. **How hard should contract enforcement be** (Stage 5): reject on any mismatch, or auto-insert a
   recovery/transition to satisfy the contract? (Recommend: auto-insert where a capability declares
   `EmitsOwnRecovery`, reject otherwise.)
3. **Speed-state fidelity** (Stage 9): coarse forward-sim vs full per-archetype pass reusing
   `RouteTimeEstimation`. (Recommend: coarse first.)
4. **Pacing** (Stage 10): implement now or defer until gameplay feedback exists? (Recommend: defer;
   it's the only stage with no current correctness driver.)
5. **VehicleCapability ownership**: authored ScriptableObject vs derived from the hovercraft at
   runtime. (Recommend: authored SO the hovercraft owner maintains, so generation stays
   deterministic and offline.)
6. **Definition-vs-frame authority** (Stage 2): reconcile def from frames after every pass, or
   redirect consumers to frames? (Recommend: redirect; cheaper, no extra pass.)

---

## Recommended First Implementation Step (after approval)

**Stage 0 — Baseline preservation.** Before any change: capture a **seed corpus** (10-20 seeds
spanning flat/banked/vertical/loop/corkscrew/jump/dual-road) with their exported debug reports and
serialized layout data, and add a **determinism + reproduction test** that regenerates each seed and
asserts identical layout data. This is safe (no production code touched), and it is the harness that
makes every later stage provably non-regressing — which, given how much of this system is already
good, is exactly what protects the parts worth keeping.

*(No production code was modified in producing this audit.)*
