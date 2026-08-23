# V2.1b — Wall Shape Controls & Centreline Curvature Flow (investigation)

**No production code changed.** Answers the two questions raised from the editor screenshot
(`[06] BankedCurve_45deg_Left R=650 → [07] Straight_01_Climb19m → [08] BankedCurve_45deg_Left R=578`).

---

## PART A — Where wall shape is actually controlled

**All of it is designer-configurable.** Nothing about wall steepness is hardcoded.
Everything lives in **Designer Settings → Road** (`TrackRoadSettings`,
`TrackDesignerSettings.cs:245-310`), resolved into `TrackRoadProfileSettings`
(`TrackCrossSectionProfile.cs:78-165`) and evaluated by `TrackCrossSection.Evaluate`.

### The authoritative shape controls

| Control (Inspector) | Range / default | What it does |
|---|---|---|
| **`WallCurve`** | 0–1, **1.0** | **THE steepness dial.** 0 = walls are flat straight extensions of the floor; 1 = perfect quarter circle with a vertical tip. In between it is a circular arc over the same footprint, tip height = `WallHeight × tan(curve × 45°)` |
| `WallHeight` | 24–48 m, **24** | Wall horizontal footprint, and at curve = 1 also its exact height (quarter-circle radius) |
| `FlatCenterWidth` | 4–48 m, **16** | Flat floor width. Total road = `(FlatCenterWidth + 2 × WallHeight) × RoadScale` |
| `RoadScale` | 1–5, **1** | Uniform scale of the whole cross-section |
| `SafetyLipHeight` | 0–4 m, **1** | Inward capture curl at the top edge |
| `ProfileResolution` / `ColliderProfileResolution` | 32 / 40 | Render vs collision sample counts |

### Why some sections look **much steeper** than others

Two dynamic systems reshape the profile *in turns* — this is the "steeper half-pipe in some parts"
you are seeing, and it is intentional-by-design, not a bug:

**1. Dynamic Turn Rounding** (`TrackCrossSection.Evaluate:50-54`)
```csharp
rounding = f.TurnRounding;
flat  = Lerp(CenterFlatWidthRatio, MinTurnCenterFlatRatio, rounding);  // 0.48 → 0.05
curve = Lerp(WallCurve01, 1f, rounding);                               // → full quarter circle
```
In a committed turn the flat floor nearly disappears (**0.48 → 0.05**) *and* the wall is forced to
full curvature. That is a dramatically deeper, steeper bowl than the straight-section profile.
Controls: **`DynamicTurnRounding`** (on/off), **`TurnRoundingStrength`** (0.85),
**`MinimumTurnCenterFlatRatio`** (0.05).

**2. Outside Catch Wall** — the outside wall curls *past vertical* on demanding corners.
Controls: **`OutsideCatchWall`**, **`CatchWallStrength`** (0.6), **`MaxOverhangAngle`** (18°),
**`OverhangRadius`** (10 m), **`CatchWallMinimumDemand`** (0.35).

**If you want a gentler/more uniform bowl**, the dials in order of effect:
`TurnRoundingStrength` ↓ → `MinimumTurnCenterFlatRatio` ↑ → `MaxOverhangAngle` ↓ →
`CatchWallStrength` ↓ → `WallCurve` ↓.

### Per-frame channels (set by passes, not directly authored)
`SideHeight` (CrossSectionPlanner — the V2.1 fix), `Left/RightWallMultiplier` (banking field),
`Left/RightOverhang`, `TurnRounding`, `PipeClosure`, `WallrideMorph` — all on
`TrackConnectionFrame`, all composed in `TrackCrossSection.Evaluate`.

---

## PART B — The straight between two same-direction curves

### 1. Why `Straight_01` exists
Straights between corners are the **closure solver's levers**. `TrackTopologyPlanner` emits an
alternating corner/straight skeleton and `RunActiveSetSolve` adjusts *straight lengths* (and corner
radii) to close the lap in 2D. The code guards this budget explicitly — `CanDropLeadStraight`
permits **at most one drop per quarter** and only "while enough full-length adjustable straights
remain to keep the closure solver's lever budget healthy".

**So the straight is structural, not decorative.** It is not there because the design wanted a
straightaway.

### 2. Does it assert zero curvature? **Yes.**
`def.SectionType = Straight` → `SectionFrameBuilders.BuildStraight` advances the plan tangent in a
straight line. Horizontal curvature is exactly 0 for the whole connector.

### 3. Is the existing machinery already handling it? **Only halfway — and this is the finding.**

`ConnectorAnalyzer` *does* detect this exact case
(`ConnectorAnalyzer.cs:101-114`): same-direction exit/entry signs inside the bridge window →
`SameDirectionTurnBridge`, and with `AllowConnectorAbsorption` it marks all three sections with a
shared `TurnComplexId` and notes *"treated as one turn complex — no reset between the turns"*.

But look at what absorption actually carries:
```
inherited = "bank, outside wall support, depth, turn rounding"
```
**Surface properties only.** The *path* is untouched — the section stays `Straight` and still
generates zero-curvature geometry. Your reports confirm it: `[010] Straight_02 len 38m …
SameDirectionTurnBridge complex TurnComplex_1` — a **38 m** (0.1 s) zero-curvature interruption
inside something the generator itself already labels "one turn complex".

### 4. This is the SideHeight bug again, one layer down

| | V2.1 (fixed) | V2.1b (this) |
|---|---|---|
| Channel | wall height (across) | horizontal curvature (along) |
| Defect | connector asserts **neutral SideHeight** | connector asserts **zero curvature** |
| Result | `Tall → 26.0 → Tall` valley | `R650 → ∞ → R578` kink |
| Detected by | — | `ConnectorAnalyzer` already flags it as one turn complex |
| Carried through? | now yes | **no — surface only** |

Same architectural principle: *incidental connector state overriding neighbouring design intent.*

### 5. Continuity guarantees today
- **C0 position** — guaranteed (exit frame ≡ entry frame).
- **C1 tangent** — guaranteed (heading is continuous; the straight leaves along the corner's exit tangent).
- **C2 curvature** — **not guaranteed between sections.** Curvature steps `1/650 → 0 → 1/578`
  instantaneously at each join. Arcs are eased internally, but nothing eases *across* a connector.

At 361 m/s a 38 m connector is a ~0.1 s steering-demand collapse and re-application. That is what
reads as "the road stops turning for a moment".

---

## PART C — Why this is NOT a small fix (honest risk assessment)

The SideHeight fix was safe because wall height doesn't affect topology. **Curvature does.**
Any curvature in the connector changes heading, and heading feeds closure. Options:

| Option | What it does | Risk |
|---|---|---|
| **A. Plan-time corner merge** | When an absorbed complex has a connector below the bridge window, emit **one** corner of the combined angle (45+45 = 90°) with a radius spanning the distance, instead of corner+straight+corner | **Medium.** Closure runs *after* planning, so it can still solve — but the lap loses a lever. The code warns this starves closure (Stage D limited lead-straight drops to 1/quarter for exactly this reason). Yield must be measured |
| **B. Curved connector (radius transition)** | Keep three sections, but give the connector an eased curvature ramp `R650 → R578` | **High.** Adds heading change → the closure solver's variable set and corner budget both change. Not surgical |
| **C. Do nothing structural; only lengthen/shorten** | Tune `SameDirectionBridgeLength`, min connector length | **Low, but doesn't fix it** — a straight is still a straight |

**Precedent for A:** the codebase already has an opt-in, capacity-guarded structural change of
exactly this shape — *"Stage D: opt-in capacity-guarded corkscrew lead-straight drop"* (completed),
with `CanDropLeadStraight` protecting the lever budget. Option A is the same pattern applied to
same-direction turn complexes.

---

## PART D — Recommendation

**This is a separate stage, not part of V2.1.** V2.1 (cross-section) is implemented, verified and
guarded; reopening it to add topology work would mix a safe pass with a risky one. Propose
**V2.1b — Curvature Flow**, scheduled after V2.2/V2.3 unless you want it sooner, because it touches
the same subsystem (topology/closure) that the dual-road work does.

### Staged plan (mirrors what worked for V2.1)

**Stage 1 — diagnostics only (safe, ~1 test run).** Extend the report with a curvature-flow block:
- incoming curvature, connector curvature, outgoing curvature per span
- same-direction pairs separated by a connector shorter than the bridge window (the defect list)
- shortest straight between same-direction turns
- steering-rate (dκ/ds) spike at each join
- count of curvature sign/state changes with no design reason

This tells us **how often** the pattern occurs and how short those connectors really are, before
changing any geometry. My expectation from your reports: frequent, with connectors of 38–250 m.

**Stage 2 — Option A behind a config flag**, capacity-guarded like Stage D, measured on:
1. the new curvature-flow metric (should collapse),
2. **generation yield** (must not collapse — the real risk),
3. the frozen corpus (expect hash changes → review → rebaseline).

**Files that would change (Stage 2):** `TrackTopologyPlanner` (corner emission / merge),
`ConnectorAnalyzer` (absorption already identifies the trio), possibly `SectionFrameBuilders`
(eased radius transition), plus diagnostics + tests. **Not** `CrossSectionPlanner` — that work is
done and should stay untouched.

### What will NOT change
Authored features (wallride, pipe, loop, corkscrew, jump) keep their exact geometry. Long
intentional straights stay straight — only connectors *below the bridge window* between
same-direction turns are candidates. `Corner → 1000 m straight → Corner` remains a real straightaway.
