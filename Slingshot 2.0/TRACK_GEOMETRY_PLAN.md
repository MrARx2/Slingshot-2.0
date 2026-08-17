# Track Geometry Implementation Plan — Elevation, Air-Gap Launches, Cross-Section/Walls

**Single authoritative plan.** Supersedes all earlier drafts. Internally consistent. The
vertical fix is a **chain-level jerk-limited pitch profile** solved once per continuous eligible
chain over cumulative horizontal plan-centreline distance (see §1); smootherstep and per-section
solving are both rejected. Implementation is **paused** pending acceptance; only the diagnostic
groundwork in §1.7 has been touched and is audited there.

**Repro:** seed `-157394052`, design speed ≈ **361 m/s (1300 km/h)**, gravity from `cfg.Gravity`.

**Honesty / limits:** I can't compile or run the generator here. Root causes are read from the
code (file:line); the math below is stated explicitly and the apex + curvature-rate figures were
verified numerically so they can be re-checked.

---

## 0. Reproduction evidence

| Item | Value |
|---|---|
| Design speed | ≈361 m/s |
| `[017] Straight_04_Climb120m` | plan len 267.9 m, Δelev +120.5 m, pitch 0°→~34°→0° |
| `[019] JumpRamp_H16m_4.8deg` | len 417.4 m, lip rise 15.8 m, exit pitch **4.8°** |
| `[020] AirGap_1349m_3.75s` | gap 1349 m, airtime 3.75 s |
| Apex at 4.8° / 8° / 14° (v=361, g=9.81) | **46.5 m / 128.7 m / 388.7 m** above lip (verified) |
| Ordinary wall-top limit (scalar height, legacy) | 0.12 m/m |
| Worst wall-top rate (scalar, legacy metric) | **0.220 m/m** |
| Worst width rate | **0.575 m/m** at `PostCatchRecovery` |

The apex numbers show why a fixed launch-pitch **band** cannot be a default (see §2): at this
speed each additional degree adds enormous apex height and gap length.

---

## 1. Vertical geometry (ordinary elevation) — chain-level planner

### 1.1 Root cause (confirmed)
`SectionFrameBuilders.BuildStraight` (`:509`) builds height with a cubic `Smooth01(u)` **per
section**, parametrised by that section's horizontal ring fraction, ramping from entry grade to a
local shape and back to exit grade. Peak slope `delta·1.5/length = 120.5·1.5/267.9 = 0.675` →
`atan ≈ 34°`. Cubic is C1 only (curvature jump at welds). `Capacity()` in `TrackTopologyPlanner`
bounds by **max pitch only** — curvature/jerk uncontrolled at 361 m/s. **Because each section
solves its own entry→hold→exit shape, redistributing one harsh climb across several sections would
produce repeated humps** (`ramp up/down → ramp up/down → …`) instead of one sustained climb. That
is the specific failure mode this section is redesigned to prevent.

### 1.2 The `VerticalProfilePlanner` — one solve per continuous eligible chain
Introduce an explicit **`VerticalProfilePlanner`** that runs **after horizontal closure** and
**before** any section frame builder writes height. It operates on **each complete eligible
ordinary-elevation chain** (definition in §1.5), not on individual sections. Per chain it must:

1. Build a **cumulative horizontal plan-centreline arc-distance** coordinate `x` spanning the
   whole chain (§1.3) — start `x=0` at the chain's first eligible ring, accumulate the plan
   centreline length across every internal macro-section boundary without resetting.
2. Solve **one continuous field** `θ(x)`, height `h(x)`, curvature `κ(x)`, and curvature-rate
   `dκ/ds(x)` over that whole `x` domain (§1.4 profile, §1.6 exact curvature math).
3. Impose pitch/curvature **boundary conditions only** at (a) the two ends of the chain and (b)
   **locked feature boundaries** (jump lips, bridge/underpass mouths, loop/corkscrew/wallride
   entries) that the chain abuts. **No boundary condition is imposed at an ordinary internal
   macro-section boundary.**
4. Allow ordinary internal section boundaries to fall **anywhere** inside a ramp phase or a hold
   phase — the boundary is just a sampling location on the continuous field.
5. **Never reset pitch or curvature because a macro-section boundary was crossed.** Crossing a
   boundary continues the same clothoid/hold phase.
6. Assign **each section, and each ring within it,** a slice of the solved canonical profile by
   its `x`-interval; the section reads `θ`, `h`, `κ`, `dκ/ds` from the field at its rings.
7. Derive each section's final `ElevationChange` (`def.ElevationChange` / equivalent) **from the
   solved profile** (`h(x_end) − h(x_start)`), not from an independent per-section target.
8. **Guarantee identical height, tangent, curvature, and curvature-rate on duplicated section
   boundaries** (welds / alternate-route mirrors): both representations sample the field at the
   same `x`, so they are byte-for-byte equal.

**Result — a distributed climb behaves as one long climb:**
`ramp into grade → sustained grade held across every internal section boundary → gradual ramp
out`, and **not** `ramp up/down → ramp up/down → ramp up/down`.

**Geometry consumers that must sample the chain-level field (all of them):**
- `SectionFrameBuilders.BuildStraight` (`:509`) → replaced by a field-sampling grade builder.
- **`SectionFrameBuilders` curve/turn builders** — any builder that emits an ordinary curved
  section that can carry redistributed elevation (the elevation walk may push grade onto curves,
  so curves must read the same `θ(x)`/`h(x)`/`κ(x)` field, not a separate per-curve height rule).
  The implementation path is **not** limited to `BuildStraight`.
- `BuildKeyframedPitchRamp` / any keyframed-pitch consumer, where a chain hands grade into a
  feature approach.
- `BoxPrismTrackMeshBuilder` and collider generation (they consume the frames' solved Y/tangent).
- `TrackGuideMarkingBuilder` and `MacroTrackDebugVisualizer` (they read the same frames).
- `TrackConnectionFrame` boundary hand-off (`Position`, `PitchAngle`, `VerticalCurvature`,
  `VerticalCurvatureRate`) is populated from the field at the boundary `x`, so a section's entry
  frame equals the previous section's exit frame exactly.

Any consumer that currently computes height/pitch/curvature on its own must instead **sample the
canonical vertical field**; none may re-derive elevation independently.

### 1.3 Plan-distance coordinate (defined correctly)
`x` is **cumulative horizontal plan-centreline arc length** — **not** global world X, and **not**
straight-line endpoint displacement. `TrackMacroSectionDefinition.Length` remains the
**already-closed horizontal / plan displacement** the horizontal solver used; we do not
reinterpret it as 3D arc length. For ordinary elevation over an **existing** horizontal plan path:

- **Retain the finalized horizontal plan centreline** exactly (positions, horizontal curves,
  horizontal closure) — the vertical stage never moves points horizontally.
- Compute height along that centreline by **`dh/dx = tan(θ(x))`**, so
  **`Δelevation = ∫ tan(θ(x)) dx`** and the horizontal displacement is untouched → horizontal
  closure stays intact.
- Derive the **final 3D tangent** from the horizontal plan tangent `T_plan(x)` (unit, in the
  horizontal plane after curves) tilted by the vertical slope: the 3D forward is
  `normalize(T_plan·cosθ + up·sinθ)` — i.e. the plan heading is preserved and only pitched up by
  `θ(x)`.
- **Apply banking after** the 3D tangent/frame is constructed (bank rotates the cross-section
  about the 3D forward axis; it is not part of the grade solve).
- **True 3D arc length** is a derived quantity: `s(x) = ∫ sec(θ(x)) dx` (used only for curvature
  limits and diagnostics, never fed back into horizontal length).

**When the eligible chain contains horizontal curves:** `x` still accumulates along the curved
plan centreline (arc length of the horizontal path), so a curve simply contributes its horizontal
arc length to `x`; `θ(x)` and `h(x)` are shared with the straights on either side, and the curve's
3D tangent is its horizontal (curving) tangent pitched by `θ(x)`. Horizontal curvature and vertical
curvature are independent and both retained — the combined 3D curvature is checked in §1.6/§1.8.

**When the chain's entry pitch is nonzero** (the chain begins mid-grade, e.g. off a feature exit):
the boundary condition at `x=0` is `θ(0)=θ_entry`, `κ(0)=κ_entry` (the neighbour's true outgoing
curvature), not 0. The clothoid phases start from that state and blend to the hold grade, so there
is no artificial "reset to level" at the chain start. The tangent-pitch used everywhere is read
from `Forward` (`asin(Forward.y)`), which is consistent at any entry pitch (see §1.7 audit).

### 1.4 Jerk-limited pitch profile — full phase definition
The continuous `θ(x)` for a level→grade→level climb of chain length `L` and rise `Δ` is a
**symmetric S-curve** built from a piecewise **curvature-vs-distance** profile whose rate (jerk)
is bounded:

1. **Ramp-up-in** `[0,a]`: curvature-rate `> 0`; the pitch gradient rises from `κ_entry`.
2. **Ramp-up-out** `[a,2a]`: curvature-rate `< 0`; the gradient returns to 0 as pitch reaches the
   hold grade `θ_hold`. (Phases 1+2 form one clothoid pulse; a **constant-curvature middle** may
   be inserted between them → **trapezoidal**, not triangular, curvature pulse.)
3. **Hold** (optional): `θ = θ_hold`, `κ = 0`, constant grade — this is the span that stays
   sustained across internal section boundaries.
4. **Ramp-down-in**: mirrored curvature pulse begins the grade exit.
5. **Ramp-down-out**: mirrored pulse returns pitch and curvature to the exit state
   `θ_exit`, `κ_exit`.

Every clothoid pulse **ramps curvature up and then back to zero**; the hold has `κ=0`. There is
one such profile for the **entire chain**, so at most one climb pulse and one descent pulse exist
regardless of how many macro-sections the chain spans.

**Solver** (deterministic, numerical) finds `θ_hold` and the phase fractions to satisfy
simultaneously, over the whole chain: exact horizontal length `L`; exact elevation
`Δ = ∫ tan θ dx`; exact entry/exit pitch; **compatible entry/exit curvature** (start/end at the
neighbour's `κ`); `|θ| ≤ θ_max`; and the **true arc-length** bounds `|κ| ≤ κ_max`,
`|dκ/ds| ≤ κrate_max` (§1.6). Method: represent `q(x)=dθ/dx` as piecewise-linear segments, build
`θ(x)` by analytic integration of those segments, integrate `h=∫tanθ` and `s=∫secθ` on
deterministic solver samples, and bisection-search `θ_hold`/phase split to hit `Δ`. If no feasible
`θ_hold ≤ θ_max` fits in `L`, the chain can't carry `Δ` → §1.5 fallback.

### 1.5 Atomic feasibility, closure, and protected relationships
Horizontal closure runs **before** elevation; the vertical stage must **not** lengthen sections
after closure. **Fallback order (one deterministic policy, applied to the whole chain):**
1. **Redistribute** `Δ` across the chain's **eligible ordinary-elevation span** as one continuous
   profile (this is automatic — the planner already solves the whole chain, so redistribution is
   just choosing hold length vs pulse length within limits, never splitting into repeated humps).
2. **Reduce** `Δ` (uniform scale of the chain's elevation; vertical closure `Σδ=0` preserved).
3. **Reject and resample** deterministically.
No partial elevation is committed before the whole chain is feasible.

**Eligible ordinary-elevation span** = contiguous ordinary straights/curves that are **not**:
air-gap launch/landing sections, bridge/underpass clearance sections, loops/corkscrews/wallrides/
authored rotational features, mandatory-height features, or alternate-route weld sections.
Redistribution/scaling **may not alter** those relationships.

After any redistribution/scaling, **re-run**: vertical closure; physical-clearance validation;
self-intersection; feature entry/exit; jump-trajectory validation. If a mandatory clearance can't
survive the reduction, **reject deterministically** — never scale through it.

### 1.6 Curvature and curvature-rate (exact arc-length formulation)
With `q(x) = dθ/dx`, `ds/dx = sec θ`, and `κ = dθ/ds = q·cos θ`, the **true arc-length curvature
rate** is

```
dκ/ds = q′·cos²θ − q²·sinθ·cosθ
```

(verified numerically against finite differences). **Consequence:** bounding `q` and `q′` does
**not** by itself bound `κ` or `dκ/ds` — the `cos²θ` and `q²·sinθcosθ` terms matter, especially as
`θ` grows. Therefore the solver and validators do **one** of:
- **(chosen)** keep the plan-distance (`x`) formulation but evaluate `κ = q·cosθ` and
  `dκ/ds = q′cos²θ − q²sinθcosθ` with the **exact** equations above when solving and validating,
  constraining the *true* `κ`/`dκ/ds`, not the raw `q`/`q′`; **or**
- formulate the profile **directly in true arc-length curvature** `κ(s)` while satisfying the
  plan-length and elevation integrals.

**Mesh-independent validation.** Curvature and curvature-rate must **not** be judged only at final
mesh rings — mesh tessellation must never decide whether geometry is safe. Validation evaluates
either the **analytical per-phase extrema** of `κ` and `dκ/ds` (each clothoid pulse has a known
maximum) **or** a **deterministic high-resolution / adaptive solver sampling** that is independent
of the mesh ring count. Mesh rings then merely sample an already-proven-safe field.

### 1.7 Audit of the already-added metadata (diagnostic groundwork only)
The `VerticalCurvature`/`VerticalCurvatureRate` pass I added to `BuildStraight`:

| Requirement | Status |
|---|---|
| True 3D arc distance for `ds` | ✅ `\|P[i+1]−P[i−1]\|` |
| Deterministic | ✅ |
| Safe signed angular difference | ⚠️ raw subtraction; use `Mathf.DeltaAngle` |
| Works when incoming frame not world-level | ❌ derives from `PitchAngle` (interior = `atan(addedSlope)`, exit = `asin(Fwd.y)`) — inconsistent unless entry level |
| Incoming vs outgoing one-sided curvature | ❌ copies `entry.VerticalCurvature` into frame 0 |
| Report boundary discontinuity, don't conceal | ❌ same |

**Corrections (part of the §1.2 planner, not before):** derive tangent pitch from `Forward`
(`asin(Fwd.y)`) so it's consistent at any entry pitch; frame-0 = true outgoing one-sided
curvature; record incoming separately so the join jump is measurable. With the chain-level planner
these boundaries become continuous by construction, so the metadata reports agreement rather than
concealing a step. **This is groundwork, not the fix.**

### 1.8 Files/functions
New **`VerticalProfilePlanner`** (chain-level solve, invoked after horizontal closure, before
section frame builders); `SectionFrameBuilders.BuildStraight` (`:509`) + **curve/turn builders**
→ field-sampling; `TrackTopologyPlanner` `Capacity()` + elevation walk (`~:2739–2791`) feed chain
spans to the planner; `TrackDesignerSettings.Elevation` + `ResolvedTrackGenerationConfig` (new
`MaxCurvatureInducedG`, `MaxVerticalCurvatureRate`, `θ_max`, separate inversion limits);
`TrackValidators` (§6, analytic/high-res curvature checks).

---

## 2. Air-gap launches — objective-driven (solved AFTER §1, BEFORE §4)

### 2.1 Root cause (confirmed)
`JumpBallistics.TrySolve` (`FeaturePatterns.cs:96`): `launchPitch = asin(g·t·(1−k)/v)` bounded by
`Min/MaxJumpLaunchPitchDegrees`; airtime/gap caps pull it to ~4.8°. `LaunchRampKeys` (`:820`) exits
at that shallow angle. No apex/arc requirement, no landing-capture guarantee.

### 2.2 No fixed launch-pitch default — design from objectives
**Remove the previously proposed 8–14° working band as a default.** At 361 m/s and 9.81 m/s²,
apex above the lip is `(v·sinθ)²/(2g)`: **4.8° → 46.5 m, 8° → 128.7 m, 14° → 389 m** (verified). A
fixed 8–14° band would conflict with the airtime caps and produce enormous gaps — launch pitch is
an **output**, not a tuning input.

**Jump design is objective-driven.** Per jump:
1. Choose a **readable target apex** relative to the lip and the landing.
2. Choose an **acceptable airtime range**.
3. Ensure the **apex occurs at the intended point before landing** (for a standard, non-stall
   jump — apex ahead of the landing mouth, not past it).
4. Constrain **vertical velocity at landing** and **arrival tangent mismatch** to the landing
   surface.
5. Validate **capture at minimum, nominal, and maximum entry speed**.
6. **Derive** launch pitch, lip rise, landing position, and landing shape **from those
   objectives** — solved, not set.
7. Apply launch pitch only as a **configurable hard safety range** (a clamp/rejection guard), not
   as the design target.

**Trajectory model must match the actual airborne craft.** Use `cfg.Gravity` and the **actual
airborne hovercraft force model** (any gravity scaling, airborne thrust, drag, assist). If a
simplified ballistic model is used for the first solve, apply a **conservative capture margin** for
the model mismatch (documented + tested), and validate the final capture against the real airborne
model.

**Lip endpoint (a ramp, not a kicker)** — explicit acceptance limits on: curvature **at** the lip;
curvature **rate** approaching the lip; **pitch** at the lip; **induced load** just before release.
**Default: curvature returns ≈0 at the open edge while holding the solved exit pitch.** If nonzero
lip curvature is intentional, it gets its **own small hard limit + diagnostic**.

**Re-solve as a unit** on any launch/apex change: launch length, launch pitch, lip rise, airtime,
gap, gap Δelev, arrival v/pitch, landing mouth, landing transition, landing capture length×width.

**Reported per jump (diagnostics):**
- **time to apex**;
- **apex position within the gap** (fraction of gap, must be before the landing mouth for a
  standard jump);
- **vertical velocity at landing**;
- **arrival tangent mismatch** vs the landing surface;
- **capture result at min / nominal / max speed** (all three must intersect the usable capture
  surface, length×width; else adjust the launch/landing pair or **reject**).

### 2.3 Files/functions
`JumpBallistics.TrySolve` (`:96`) → objective solver (apex/airtime/capture in, pitch/lip/landing
out), `EmitJumpRamp/EmitAirGap/EmitLandingRamp`; `LaunchRampKeys` (`:820`), `LandingRampKeys`
(`:829`), `BuildKeyframedPitchRamp` (`:771`); `TrackConfig` jump objectives (target apex, airtime
range, capture length/width) + **hard-safety pitch range** + `cfg.Gravity` + airborne force model.

---

## 3. Cross-section / walls (planned over FINAL geometry)

### 3.1 Root cause (confirmed)
`TrackCandidateBuilder.ApplyCrossSectionBlend` (`:1131`) blends a per-section wall-depth multiplier
over immediate `Neighbor(i,±1)` in a local window — no arc-length look-ahead, no width planning,
no wall-top rate limit, no wallride staging. Hence 0.220 m/m wall-top and 0.575 m/m width spikes
pass.

### 3.2 Canonical cross-section field
Add a `CrossSectionPlanner` (feeding/replacing `ApplyCrossSectionBlend`, `:255`) that, per
**continuous physical chain over finalized geometry**, produces **one canonical cross-section field
over arc length** (total width, flat/floor width, shoulder, side/wall height, rounding,
catch/overhang, banking support, wallride morph, feature entry/exit). Mesh rings, colliders, and
markings all **sample this field**, with **distance-based forward+backward influence**.

**Wall-top measured in a transported local frame.** Do **not** differentiate absolute world
wall-top (forward progress alone ≈ 1 m/m). Per ring: (1) compute L/R wall-top world positions;
(2) **subtract the centreline**; (3) transport the resulting cross-section vectors into a shared
**parallel-transported** local frame; (4) measure lateral + vertical change per metre; (5) 2nd
derivatives in the transported frame. **Report separately:** centreline geometry, frame rotation,
cross-section-relative wall motion — so smooth track rotation isn't flagged as a wall defect.

**Authored constraints are locked** (planner prepares the surrounding ordinary road to meet them,
never smooths them away): active wallride morphs, pipe/enclosure closure, corkscrew rotational
shaping, loop shaping, feature-specific wall envelopes.

### 3.3 Wall metric recalibration (the legacy 0.12 is NOT reused blindly)
The legacy `0.12 m/m` limit measured **scalar wall-height change** only. The new transported
wall-top vector metric also captures **lateral width and shape change**, so it is **not
numerically the same quantity** and 0.12 cannot be carried over to it unchanged. During migration,
report **all** of these as **separate** fields (don't collapse them into one prematurely):

| Metric | What it measures |
|---|---|
| **Scalar wall-height rate** (legacy, kept) | d(wall height)/ds — the old 0.12 quantity, retained as a comparable diagnostic |
| **Lateral wall-top rate** | transported cross-section lateral change per metre |
| **Vertical wall-top rate** | transported cross-section vertical change per metre |
| **Combined transported cross-section rate** | magnitude of the transported wall-top vector change per metre |
| **2nd derivative / acceleration** of each relevant field | jerk/acceleration of the above |

The **read-only measurement pass (§7 / step 0)** measures every one of these across existing
playable geometry and **proposes category-specific limits** for the new metrics. The legacy 0.12
stays only as the scalar-height diagnostic; it is **not** applied as the bound for the combined
transported metric.

**Category-specific limits:** ordinary road; wallride prep; active wallride; recovery; inversion
features — each with its own justified bound (from §7), and the ordinary limit must never reject
intentional wallride/inversion motion. Width depends on curvature magnitude + speed, **not**
handedness (inside/outside may mirror). Connectors that can't contain the blend **expand into
neighbours or raise min length** — never squeeze. Never blend across air gaps, open edges, or
alternate-route chains (keep guards `:1150–1154`).

### 3.4 Weld invariant (corrected)
Boundary cross-sections **may** be adjusted by the canonical planner. The invariant is **"no
independent movement and no seam,"** not "boundary samples can never change": every duplicated
representation of the same physical boundary receives the **identical canonical sample**, welded
copies **move together**, and position, tangent, width, wall shape, bank, and relevant derivatives
**agree across the weld**.

### 3.5 Files/functions
New `CrossSectionPlanner`; `TrackCandidateBuilder.ApplyCrossSectionBlend` (`:1131` → adapter/
replaced); `TrackCrossSection.Evaluate`; `TrackValidators` (scalar height + transported L/R lateral
+ vertical + combined + 2nd-derivative + width-rate, all categorised); `TrackGuideMarkingBuilder`
(§5).

---

## 4. Road-width semantics
Verified: **`Width` = total driveable width** (`BuildStraight:546`; cross-section/marking use
`halfW = Width*0.5`). New formulas use `halfWidth = Width/2` explicitly. The 2-line↔centreline
marking transition follows the **same continuous width samples** as the mesh (canonical field /
frame `Width`), not labels.

---

## 5. Determinism & testing
**Determinism (corrected):** no old-hash requirement. Require: same seed + settings + revised code
→ identical repeats; failed candidates consume deterministic retries; diagnostics never influence
generation; geometry changes are **expected** to change the hash.

**Test seeds/cases:** `Straight_04_Climb120m`; **a multi-section distributed climb** (assert one
sustained grade, not repeated humps, and identical boundary `θ/κ` across internal section joins);
consecutive elevation sections; **elevation carried onto a curve** (assert the curve samples the
shared field); climb→jump approach; jump landing→curve; straight→narrow-curve; curve→wallride prep;
active wallride + recovery; corkscrew narrow-width; left AND right curves; dual-road quarters +
chain boundaries.

---

## 6. Diagnostics + acceptance table
Extend `TrackDebugReportExporter`; gate SUCCESS in `TrackValidators` (no SUCCESS when ordinary
geometry violates configured limits). Curvature checks use analytic/high-res samples (§1.6), **not**
mesh rings.

| Metric | Diagnostic | Acceptance |
|---|---|---|
| Max pitch | worst ring on chain field | ≤ `θ_max` |
| Max vertical curvature `κ` (true arc-length) | analytic/high-res, per chain | ≤ `κ_max(v)` |
| Max curvature rate `dκ/ds` (exact eq.) | analytic/high-res, per chain | ≤ `κrate_max` |
| Curvature-induced G (@ min/nom/max v) | value + speed | ≤ `MaxCurvatureInducedG` (ordinary) |
| Chain continuity | internal section joins | identical `θ/κ/κrate` across every internal boundary; single climb pulse, no repeated humps |
| Jump: launch pitch, lip rise, lip κ, **time-to-apex, apex position in gap, landing vy, arrival tangent mismatch, capture @3 speeds** | all | all 3 speeds hit capture surface; apex before landing mouth; lip κ ≤ lip limit; pitch within hard-safety range |
| Max road-width rate | value + section | ≤ width-rate cap |
| Scalar wall-height rate (legacy) | both sides | reported (comparable to old 0.12) |
| Transported wall-top: lateral / vertical / combined + 2nd deriv | both sides + category | ≤ category limit (from §7) |
| Wall transition expanded into neighbours? | yes/no | — |
| Elevation redistributed / reduced / rejected | which | reported |
| Welds | — | identical canonical sample, no seam |
| Determinism / unrelated topology / authored shapes | — | repeats identical; unchanged |

---

## 7. Tuning bootstrap — STEP ZERO (before selecting limits or changing geometry)
Ship a **read-only measurement pass** (safe, no geometry change) and produce a **baseline report**
**before** any default limit is chosen and before any geometry is touched. Across current playable
loops/corkscrews/curves/ordinary roads **and** seed `-157394052`, it reports per section/chain:
current **pitch, true arc-length curvature `κ`, curvature rate `dκ/ds`, curvature-induced G**, jump
**launch pitch / apex / time-to-apex / capture**, and the **full wall metric set** (scalar height
rate + transported lateral/vertical/combined + 2nd derivatives). From that baseline: distinguish
acceptable sections from the harsh climb; **propose conservative ordinary-road defaults**
(`MaxCurvatureInducedG`, `κrate_max`, `θ_max`); **propose category-specific wall limits for the new
transported metrics** (not a reused 0.12); derive jump objective ranges (target apex / airtime /
capture) from measured readable jumps; keep feature/inversion limits separate. This removes any need
to pre-guess numeric limits — **no default is selected until this pass has run.**

## 8. Execution order (corrected — measurement is step 0)
0. **Read-only measurement pass + baseline report** (§7) — before any default limit or geometry
   change.
1. **Chain-level ordinary vertical-profile planner** (§1 `VerticalProfilePlanner`).
2. **Jump** launch/gap/landing/recovery objective solver (§2).
3. **Final chain + feature-boundary classification.**
4. **Canonical cross-section + wall planning over the finalized geometry** (§3).
5. **Road markings** from the canonical width field (§4).
6. **Mesh + collider construction.**
7. **Final validators + diagnostics** (§6, analytic/high-res curvature; full wall metric set).
8. **Deterministic test matrix + before/after comparison**; regenerate seed `-157394052`; record
   before/after.

(The cross-section planner consumes **final** jump+feature geometry — never geometry that changes
afterward.)

## 9. Risks / open decisions (to confirm from the step-0 measurement, not guessed)
- `MaxCurvatureInducedG` ordinary default, `κrate_max`, `θ_max`; separate inversion limits.
- Jump objective ranges (target apex, airtime, capture length×width) + hard-safety pitch clamp.
- Category wall-top limits for the **new transported metrics** (legacy 0.12 is scalar-height only).
- Airborne-model fidelity vs the ballistic solver → capture margin size.
- Yield vs stricter limits — every limit paired with redistribution/re-solve/expansion or
  generation collapses (the dual-road regression is the cautionary case).

---

## Confirmations (per review)
- **Elevation is solved once per continuous eligible chain** — YES: `VerticalProfilePlanner`
  (§1.2) runs one solve over cumulative plan-centreline distance for the whole chain; sections and
  rings sample slices of that single field; each section's `ElevationChange` is derived from it.
- **Internal section boundaries do not reset the profile** — YES: boundary conditions are imposed
  only at chain ends and locked feature boundaries; ordinary internal boundaries are just sampling
  points; a distributed climb stays one sustained grade, never repeated humps.
- **Curves can consume the same vertical field** — YES: curve/turn builders sample the same
  `θ(x)/h(x)/κ(x)` field; the implementation path is not limited to `BuildStraight` (§1.2).
- **True `κ` and `dκ/ds` are bounded independently of mesh resolution** — YES: exact equations
  `κ=q cosθ`, `dκ/ds=q′cos²θ−q²sinθcosθ` (§1.6, verified numerically); validation uses analytic
  per-phase extrema or deterministic high-res solver samples, not mesh rings.
- **Jump pitch is derived from apex/airtime/capture objectives** — YES: the 8–14° default is
  removed; launch pitch/lip/landing are solved outputs; pitch survives only as a hard-safety
  clamp; apex/airtime/landing-vy/tangent-mismatch/3-speed capture are reported (§2.2).
- **Wall limits are recalibrated for the new metric** — YES: legacy scalar 0.12 kept only as the
  scalar-height diagnostic; separate lateral/vertical/combined transported rates + 2nd derivatives
  reported; category limits proposed from the step-0 measurement, not reused from 0.12 (§3.3).
- **Measurement is executed before geometry changes** — YES: the read-only measurement pass +
  baseline report is **step 0** in the execution order (§7, §8), before any default limit or
  geometry change.
