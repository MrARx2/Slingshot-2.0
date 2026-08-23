# V2.1 — Cross-Section Transition Quality (revised)

**Status:** PLAN ONLY. No production code modified. Supersedes the V2.1 section of
`TRACK_GENERATOR_V2_SPEC.md` where they differ.

**Restated goal (yours):** the walls are already smooth. The defect is that *neighbouring
segments with different widths AND wall heights* don't transition as one coordinated
cross-section morph — so segment boundaries announce themselves as width/height waves.

---

## 0. Correction to my earlier diagnosis

I previously called the report's **0.220 m/m at `Wallride_90deg_Left`** "the bug." Under your
clarification that is **wrong**: those samples are at rings 127/638 **inside** the wallride — that's
the authored wallride morph (the riding wall rising to become the driving surface). You explicitly
want that preserved. Chasing it would have flattened a feature on purpose.

The real defect is the **ordinary road around features and between corners** — the 0.147–0.149 m/m
rates at `SpiralApproach` / `RecoveryStraight`, and the connector depth dips
(`28.1 → 26.0 → 28.1`, dip ratios 0.93–0.96) in the connector audit. That is exactly your
"tall → connector → tall" diagram.

---

## 1. What causes the visible WALL-HEIGHT waviness (dominant cause) — CONFIRMED

Two facts in the live code combine into the wave:

**(a) Wall height is targeted per SECTION TYPE.**
`TrackCandidateBuilder.DepthMultiplier` (`:1153`):

| Type | ×SideHeight | @26 m |
|---|---|---|
| BankedCurve | 1.08 | 28.1 |
| BankedHairpin / **WallrideTurn** | 1.10 | 28.6 |
| Spiral | 1.06 | 27.6 |
| RotationalEvent / HalfLoopTwist | 1.04 | 27.0 |
| Jump family (Ramp/Gap/Landing) | 0.95 | 24.7 |
| **everything else (Straight, RecoveryStraight, approaches…)** | **1.00** | **26.0** |

`CrossSectionPlanner.BuildNodes` (`:129`) sets each ring's target to
`cfg.RoadProfile.SideHeight * TargetMultiplier(...)`. So an ordinary connector between two banked
curves *asserts its own 26.0 m target* — precisely the `28.1 → 26.0 → 28.1` valley.

**(b) The only existing escape hatch is too narrow.**
`TargetMultiplier` (`:171`) blends toward neighbours **only when `BridgeCarry > 0.001`**, and
`ConnectorAnalyzer` sets `BridgeCarry` **only** for same-direction-turn-bridge / opposite-direction
transfer cases (`:99`, `:239`). Therefore the default `BlendNeighbours`, plus **`PrepareNext`
(feature approaches)** and **`OrientationRecovery` (post-feature recovery)** connectors all get
`carry = 0` → `own = 1.0` → the dip. That is exactly why the two worst ordinary hotspots in the
report are named `SpiralApproach` and `RecoveryStraight`.

**This is a target-selection defect, not a smoothing-radius defect.** `ProjectRateLimits` (`:207`)
is a *rate limiter*: it clamps each step toward its neighbour but faithfully tracks an oscillating
target set. That reproduces your bad case exactly — every step obeys the local limit while the
silhouette still goes `40 → 35 → 37 → 31 → 28 → 24`. **More blur cannot fix a wrong target; it
only smears the feature shapes you want kept.**

## 2. What causes the visible WIDTH waviness

Width is *mostly uniform* — nearly every emitter uses `Width = cfg.RoadWidth`. The non-default
widths are few and **structural**:

- `QuarterCatchWidth` = `max(RoadWidth×CatchScale, LaneSeparation + 24)` → ~183 m vs a 100 m road
  (`TrackTopologyPlanner:1465,1492`). This is the report's **worst width rate, 0.575 m/m at
  `PostCatchRecovery`** — a 183 m catch collapsing back to 100 m.
- `FullPipe` = `RoadWidth × FullPipeRadiusScale` (1.10) (`FeaturePatterns:1208`).
- `DualRoadWidth` (dual quarters).

`CrossSectionPlanner` takes `DesiredWidth = frame.Width` as given and only rate-limits it — again,
no A→B envelope.

## 3. Same root cause, or two?

**One shared mechanism, two trigger sets.**
Mechanism (identical for both channels): *each section asserts its own cross-section target; the
planner then rate-limits between those targets instead of building one A→B transition.*
Triggers differ: **height** waves are pervasive (driven by the per-type `DepthMultiplier` on
ordinary road); **width** waves are localized (catch/pipe/dual). Fixing the mechanism fixes both;
the height side is where most of the visible benefit is.

## 4. Which targets stay authoritative (HARD) vs may be blended through (SOFT)

**The distinction already exists — I am not inventing an abstraction.**

- **HARD / authored (keep locked):** the 9 types `CrossSectionPlanner.IsProtected` already locks —
  JumpRamp, LandingRamp, Loop, Corkscrew, Spiral, HalfLoopTwist, FullPipe, WallrideTurn,
  RotationalEvent. Plus structural widths: `QuarterCatchWidth` (validator requires it to swallow
  both lanes) and pipe width. These are the transition **endpoints**, never smoothed away.
- **SOFT / connector:** already classified by **`ConnectorBehavior`** (`Macro/ConnectorBehavior.cs`),
  whose own doc-comment says: *"A connector is NOT an independent road: its purpose is to blend the
  surface properties of the sections around it, and the global surface-resolution passes read this
  classification to decide what to carry through it."*

**The gap:** `CrossSectionPlanner` **never reads `ConnectorBehavior`** (verified — zero references).
The information needed to stop the wave is already computed and simply isn't consulted. That is the
minimum-change lever.

## 5. How width and wall height will transition together

Today each channel is limited independently (`maxWidthRate` 0.12, `maxHeightRate` 0.08) — they can
finish at different distances, which is your "width done at 100 m, height still moving at 250 m"
complaint. Fix: between two consecutive **hard anchors**, compute one **shared normalized progress
`t = 0→1`** over the span and evaluate every soft channel against it with an easing curve, so the
whole cross-section arrives together as one morph. Rate limits remain as a **safety clamp**, not as
the shaping mechanism.

## 6. Left/right asymmetry

`SideHeight` and `Width` are single scalars per frame; the L/R difference comes from
`Left/RightWallSuppression`, overhang and bank — all written by `ApplyGlobalBankingField`, which is
already double-box-blurred (C¹) and is **not** a wave source. So: the planner keeps owning the
symmetric channels (width, side-height); the banking field keeps owning the asymmetric ones. The
**diagnostics measure the composed per-side wall-top separately (left and right)** so an asymmetric
regression is visible even though the fix targets shared channels.

## 7. How authored features stay intact

Unchanged locks + one addition: an approach/recovery transition targets the **feature's actual
entry/exit cross-section sample** as its A/B endpoint (instead of a neutral 1.0), so the road
arrives already shaped like the feature wants. Feature interiors are never touched — no monotonicity
is forced on wallride/pipe/rotational interiors.

---

## 8. Exact changes (minimum-change, all inside the existing planner)

**Stage 1 — Diagnostics first (read-only, no geometry change).**
Extend `TrackGeometryDiagnostics` / the report with, per A→B transition span:
`WidthTransitionReversals`, `WallHeightTransitionReversals`, `WidthOvershoot`,
`WallHeightOvershoot`, plus existing `LateralWallRate`, `VerticalWallTopRate`,
`CombinedWallTopRate` **measured per side**. Run over the frozen corpus → record the "before"
numbers. *(A reversal = the channel's derivative changes sign between two hard anchors; an overshoot
= it leaves the `[min(A,B), max(A,B)]` band without an authored anchor.)*

**Stage 2 — Target interpretation (the actual fix).**
In `CrossSectionPlanner`: classify each node as HARD (protected/structural) or SOFT
(ordinary + connector, using `ConnectorBehavior`). A SOFT node no longer asserts
`DepthMultiplier(ownType)`; it takes its target from the A→B interpolation between the enclosing
hard anchors. `PrepareNext` / `OrientationRecovery` / `BlendNeighbours` therefore stop pulling the
wall to neutral. `BridgeCarry` stays as the finer-grained graded control it already is.

**Stage 3 — Shared envelope.**
Between hard anchors, drive width + side-height from one shared eased `t`, keeping the existing rate
limits as a clamp. Preserves determinism (pure function of the same inputs).

**Stage 4 — Validate + rebaseline.** (§9, §10)

---

## 9. Tests that prove the visual issue is solved

1. **Extend `WallSmoothnessTests.WallTopsChangeGradually`** (already exists, bound 0.12 m/m) to run
   the **corpus** presets (rollercoaster / compound / wallride), not just Balanced+Switchback — it
   currently only tests configs that already pass.
2. **New `CrossSectionTransitionTests`** asserting, on ordinary spans between hard anchors:
   *zero unnecessary reversals* and *no overshoot* for width and side-height; and that a
   `tall → soft connector → tall` case holds its height (no 28.1→26→28.1 valley).
3. **Feature-preservation guards:** existing `AdvancedRoadFeatureTests` (pipe closes fully & welds
   open; wallride reaches full boost/rounding/overhang) must stay green — these are the "don't
   flatten the authored interior" tests, and they already exist.
4. **Manual:** one editor generation + the report's wall-wave hotspots and connector-audit dip
   ratios (dips should read ~1.00 where both neighbours are tall).

## 10. Regression risks, and how the baseline catches them

| Risk | Detected by |
|---|---|
| Feature interiors flattened | `AdvancedRoadFeatureTests` (pipe/wallride peaks), visual review |
| Holding walls tall everywhere (over-correction) | overshoot diagnostic + visual review |
| Width envelope breaks the quarter catch requirement | `QuarterTests` catch-width validator |
| Guide-line merge changes (marking follows width) | `GuideMarkingTests` |
| Yield regression | corpus regeneration + `PresetGeneratesReliably` |
| Determinism break | `CoreDeterminism` + `Frozen_*` |
| Unintended geometry drift | **8 frozen corpus hashes will change — expected**; re-baseline via `[Explicit] RebaselineFrozenCorpus` (pinned seeds, no scan) and review the diff before committing |

**Explicitly NOT doing:** more tessellation, generic wall smoothing, bigger blur radius, reduced
banking, softened stunt geometry, or a new parallel wall system.

---

## 11. STAGE 1 RESULTS — measured before-data (root cause CONFIRMED)

**Frozen corpus aggregate (8 tracks, 105 spans):**
`widthReversals=0 · worstWidthExcursion=0.00 m · wallHeightReversals=19 ·
worstWallHeightExcursion=1.38 m · worstLateral=0.253 · worstVertical=0.421 · worstCombined=0.421 m/m`

**Editor-fidelity (full-res) worst spans — the smoking gun:**

| span | len | height A→B | observed | rev/excursion |
|---|---|---|---|---|
| `BankedCurve_45deg_R → Wallride_90deg_R` | **78 m** | 28.1 → 28.5 | **[26.0 .. 28.5]** | 1 / **2.08 m** |
| `BankedCurve_90deg_L → Wallride_90deg_R` | 487 m | 28.1 → 28.6 | **[26.0 .. 28.6]** | 1 / **2.08 m** |
| `BankedCurve_90deg_L → Loop_4u` | 617 m | 28.0 → 27.0 | **[26.0 .. 28.0]** | 1 / 1.04 m |
| `Corkscrew → Corkscrew` | 583 m | 27.0 → 27.0 | **[26.0 .. 27.0]** | 1 / 1.04 m |

**Finding 1 — cause confirmed beyond doubt.** *Every* excursion bottoms at **exactly 26.0 m** =
`SideHeight × DepthMultiplier(1.0)`, the neutral pass-through default. It is not rate limiting, not
noise: the connector is **asserting the neutral target**. A 78 m connector between two tall anchors
(28.1 → 28.5) dives to 26.0 and back — your diagram, measured.

**Finding 2 — WIDTH IS ALREADY CLEAN.** Every span, all 8 corpus tracks **and** both editor reports:
`W 100.0→100.0 [100.0..100.0] 0 rev / 0.00 m`. Width transitions into the anchors that do change
width (the full pipe in report 1) are monotone with zero reversals. **V2.1 must therefore not
re-engineer the width machinery** — it only needs to keep width coordinated with height when width
does change (pipe / catch / dual).

**Finding 3 — the worst composed wall-top rates are AUTHORED, not defects.** Report 1's worst
(lat 0.321 / vert 0.492 / comb 0.548) is entirely inside `FullPipe_2221m_R55m`; report 2's
(0.275 / 0.227 / 0.289) entirely inside `Wallride_90deg_Right`. These are the intentional pipe
closure and wallride morph — **preserve, do not smooth**. Confirms the §0 correction.

**Finding 4 — span length sets legitimacy.** The dip goes to 26.0 regardless of run length. At
361 m/s the transition scale is `cross 1.00 s ≈ 361 m`, `width 0.90 s ≈ 325 m`. So a **78 m** run
(~0.2 s) reaching neutral is clearly incidental; a **2,278 m** run (~6 s) returning to normal road
is legitimate design. This yields the rule below without an arbitrary threshold.

### Proposed rule (distance-weighted, no cliff)

A pass-through's own neutral target takes effect **only in the part of the run that is more than one
transition length from both bounding anchors**; nearer than that it blends toward the anchors.
Consequences, both desired:
- **short run** (78 m) → never reaches neutral; reads as `28.1 → hold → 28.5`.
- **long run** (2,278 m) → ramps down to neutral, holds, ramps back = one deliberate decrease and
  one deliberate increase.

Design targets (banked curve 1.08 / hairpin 1.10) remain anchors, so
`Normal 26 → BankedCurve 28.1 → Normal 26` still visibly rises.

---

## 12. STAGE 1.5 RESULTS — per-channel attribution (CORRECTS §11's inference)

**Correction.** Between Stage 1 and 1.5 I inferred that the wall *boost* channel dominated the
composed wave (~86%). **That inference was wrong**, and it was wrong for a methodological reason
worth recording: the aggregate reports *worst-per-channel across different spans*, so comparing
`SideHeight 1.38 m` against `TopL 9.99 m` compared two unrelated spans. The per-span decomposition
(editor-fidelity worst-span tables) contradicts it.

**Per-span decomposition, editor-fidelity report A (`-737775984`):**

| span | SideH exc | BoostL/R exc | TopL exc | TopR exc |
|---|---|---|---|---|
| Hairpin → Corkscrew (582 m) | 1.0 | 0.00 / 0.11 | 2.0 | 4.0 |
| DoubleApex → Spiral (1835 m) | 1.6 | 0.00 / 0.00 | 0.8 | 1.6 |
| OpenOut → Loop (1623 m) | 1.0 | 0.00 / 0.00 | 1.0 | 1.5 |
| BankedCurve → Corkscrew (62 m) | 1.0 | 0.00 / 0.00 | 0.8 | 1.3 |
| BankedCurve → Corkscrew (5766 m) | 1.0 | 0.00 / 0.00 | 1.0 | 1.3 |

**Findings:**

1. **SideHeight is the pervasive driver.** Nearly every span dips to *exactly* 26.0 and returns —
   1.0–2.1 m — independent of span length (62 m … 5766 m). The incidental-neutral-target bug,
   confirmed again.
2. **Boost's own excursion is usually ≈ 0** (0.00–0.11×) in editor tracks. Where it is large
   (0.35× on `BankedCurve → …ClosureSBend… → Wallride`) it coincides with a genuine turn-direction
   change, where the outside wall **legitimately swaps sides**. Carrying boost through such a
   connector would damage intended design.
3. **The composed top ≈ SideHeight × multiplier.** Verified: `28.6 × 1.14 = 32.6` vs measured 32.8;
   `26.0 × 1.03 = 26.8` vs measured 26.9. So the SideHeight dip is *amplified* by the wall
   multiplier (1.3–1.7 at banked curves) into 1.3–6.7 m of visible wall movement — fixing
   SideHeight alone therefore buys roughly `mult ×` its raw 1–2 m.
4. **The lateral silhouette is already clean.** `TopL(lat)/TopR(lat)` excursion = **0.00–0.02 m**,
   `Width` = 0.00 m, 0 reversals. **The defect is purely the vertical silhouette.**
5. **The defect condition is precise:** excursion appears only when **both bounding anchors sit
   above the neutral default**. Spans that straddle neutral (e.g. `LandingRamp 24.7 → BankedCurve
   26.0`) already measure 0.0 excursion / 0 reversals — the machinery already produces a clean
   monotone morph there.

### Revised scope (narrower and better evidenced than §8)

- **IN:** `SideHeight` target interpretation (distance-weighted, per §11 rule).
- **OUT:** carrying wall **boost** through connectors — evidence does not support it and it risks
  breaking legitimate outside-wall swaps. Keep measuring it.
- **OUT:** banking (unchanged, per instruction).
- **OUT:** width / lateral machinery — already clean.

**Acceptance:** on spans whose anchors are both above neutral, `SideHeight` excursion → ~0 and
reversals → 0; composed `TopL/TopR` vertical excursion drops by roughly `mult ×` that; authored
interiors (`FullPipe`, `Wallride`) keep their existing rates; `Width`/lateral stay 0.00.

---

## 13. STAGE 2 — IMPLEMENTED AND VERIFIED

**Change:** one method, `CrossSectionPlanner.BlendPassThroughHeights`, run *before* the rate limiter
(ordering is essential — a rate limiter faithfully tracks whatever targets it is given, so an
incidental neutral target survived it as a valley). Nodes are tagged `HeightAnchor` when they are an
authored shape **or** ask for a distinct height (banked curve 1.08 / hairpin 1.10). Between two
anchors a pass-through node follows an eased A→B morph and relaxes toward its own neutral target
only by

```
neutralWeight = Smooth01(min(distFromA, distFromB) / Lt) × Smooth01(clamp01((runLength − 2·Lt) / Lt))
                └── local: a full Lt from both anchors ──┘   └── the run can hold a real plateau ──┘
```

Both factors are required. The first pass shipped only the local term and **failed on the data**: a
510 m run still reached `Smooth01(255/361) = 0.79` → dipped to 26.5 m, exactly as measured. The span
term (`plateau = runLength − 2·Lt`) is what makes a short run hold its anchors.

**Results (frozen corpus / editor-fidelity):**

| metric | before | after |
|---|---|---|
| `UnnecessarySideHeightReversals` | 10 | **0** |
| `UnnecessarySideHeightExcursion` | 0.94 m | **0.04 m** (straddle span, below the 0.05 m noise floor) |
| editor-fidelity | 5 rev / 2.06 m | **0 rev / 0.00 m**, defect list empty |
| wallride interior rates | 0.274 / 0.227 / 0.289 | **identical** — nothing flattened |
| Width / lateral / Bank | 0.00 / 0.00 / 75.3° | **unchanged** |
| RAW SideHeight excursion | 1.38 m | 1.40 m — *deliberately unchanged*: legitimate long-run neutral plateaus preserved |

The last row is the point of the whole exercise: **raw variation stays, incidental variation goes.**

**Guard:** `CrossSectionTransitionBaselineTests.PassThroughSectionsDoNotCreateIncidentalWallValleys`
asserts 0 unnecessary reversals and ≤ 0.10 m unnecessary excursion across the frozen corpus.

**Known, deliberate:** implementation is continuous while the metric uses a hard 3·Lt line, so runs
in the 722–1083 m band take partial neutral by design (no geometry cliff at 1083 m). Geometry
changes also shift candidate selection for some seeds (`normal_racing` moved 15 → 12 spans), so a
per-seed before/after is not always the same track.
