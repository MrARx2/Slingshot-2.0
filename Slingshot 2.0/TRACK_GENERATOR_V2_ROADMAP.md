# Track Generator V2 — Roadmap & Task Board

Living status document. Source of truth for scope: `TRACK_GENERATOR_V2_SPEC.md`.
Detail for the wall work: `TRACK_GENERATOR_V2_1_PLAN.md`. Audit: `TRACK_GENERATOR_STABLE_AUDIT.md`.

---

## Where we are

```
V2.0 Baseline            ██████████ DONE      frozen 8-track corpus, determinism green
V2.1 Wall stability      █████████░ 90%       fix verified; needs rebaseline + visual sign-off
V2.1b Curvature flow     ██░░░░░░░░ INVESTIGATED  connector zero-curvature interruption — see §1b
V2.3b Generation Recipe  ░░░░░░░░░░           reproducible design, not just a seed
V2.2 Vertical profiles   ░░░░░░░░░░           Subtle / Balanced / Extreme
V2.3 Inspector V2        ░░░░░░░░░░
V2.4 Feature Min/Max     ░░░░░░░░░░
V2.5 Materials           ░░░░░░░░░░
V2.6 Stability pass      ░░░░░░░░░░
V2.7 Advanced features   ░░░░░░░░░░
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

---

## 4. V2.5 — Material simplification

| # | Task |
|---|---|
| 4.1 | `TrackMaterialSet`: RoadSurface, WallSide, GuideMarking, WallMarker, BoostSurface, RaceGate |
| 4.2 | Assign from the set in mesh/marking/boost/gate builders |
| 4.3 | Remove runtime `new Material` from the normal path (`TrackGuideMarkingBuilder:581`, `RaceCourseBuilder:341-366`, `BoostPadActor:108`) |
| 4.4 | Retire generator-side material **repair** (`TrackGenerator:1042/1060`) → editor-only warning if a ref is missing |
| 4.5 | Test: materials survive a domain reload / scene rebuild |

---

## 5. V2.6 — Generation stability pass

| # | Task |
|---|---|
| 5.1 | **Fix #6** — track spawning below ground (`KeepAboveStart` y<0) |
| 5.2 | Promote cross-section transition guard to a hard validator (already a test; decide if it gates candidates) |
| 5.3 | Selective sequence-contract enforcement (`TransitionResolver` advisory → gate) — corpus-gated, WARNING→ERROR |
| 5.4 | Instrumentation: failure rate, dominant rejection reasons, generation time, strict vs relaxed pass usage |
| 5.5 | 100-seed sweep harness with the automated quality checks from the spec |

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
| 9 | V2.5 materials → V2.6 stability (+#6) → V2.7 advanced (+#36 dual roads) → V2.8/9/10 | | Riskiest subsystem work last, with the strongest nets |

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
