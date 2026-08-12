# Slingshot 2 — Universal Dynamic Feature Topology
## Architecture, Feature Catalog & Implementation Plan — consolidated corrected edition (planning only)

All references verified against current project code. Completed geometry work is closed.
Corkscrew is a **pilot**, not the organizing architecture. This edition is fully
self-contained: universal topology roles, family-agnostic transitions, single-pass
accepted results, shared primitives, capability waves, complete audit, full traces, and
the full roadmap matrix.

**Keystone insight:** the demand/realization architecture already exists in embryo in
current project code — `CornerSlot.IsHalfLoop` realizes a corner *as a feature* and the
closure walk credits its 180° exactly once (`TrackTopologyPlanner.cs:67,343-410,1461`);
six further corner realizations dispatch through the same `EmitCorner` switch
(`:1276-1340`). The universal system generalizes that mechanism.

**Classification discipline (every catalog row):** net plan-view heading, bank angle,
track roll, rider inversion, intermediate heading changes, and final entry-to-exit
direction are **six independent quantities — none is ever inferred from another.**
A wave turn's ~90° *bank* says nothing about its *heading*; a corkscrew's 360° *roll*
nets 0° *heading*; a cobra roll passes through ±90 intermediate headings on its way to
a ±180 net.

---

# PART I — UNIVERSAL ARCHITECTURE

## 1. Topology roles

| Role | Consumes | Definition | Examples |
|---|---|---|---|
| **HeadingNeutral** | nothing | Net heading 0; may change elevation/roll/position | Loop, inline corkscrew, camelback, full pipe, sea serpent, roll over |
| **TurnRealization** | one demand | Replaces the ordinary curve for that demand | Curve, hairpin, wallride; future: directional corkscrew, Immelmann, sidewinder, top hat, wave turn, overbanked turn |
| **DirectionNeutralTransfer** | nothing | Net 0 with lateral/elevation/bank state change | S-curve, chicane, closure S-bend, quarter transfer |
| **CompoundTurnRealization** | an explicitly reserved ordered demand sequence (degenerate: one) | Multiple internal elements satisfying complete demands | Batwing, cobra roll, bent Cuban eight, pretzel family |
| **OrientationTransition** | nothing | Primarily pitch/roll/bank change | Blends, heartline roll, dive drop, raven turn |
| **RecoveryRealization** | nothing | Physically or gameplay-required only | Jump landing recovery, wallride exit recovery |

## 2. Capability / Request / Result — three separated concepts

Never one structure for two purposes (final definitions; also end-matter item 3):

```csharp
// STATIC — registry entry, per semantic element. Never contains generated data.
public sealed class FeatureCapability {
    public SemanticElementId Element;
    public TopologyRole Role;
    public int[][] SupportedDemandSequences;   // Batwing {{+180},{-180}}; CobraRoll adds {{+90,+90}},{{-90,-90}}
    public HandednessSupport Handedness;        // TurnMirror × RollMirror
    public EntryStateRanges AcceptedEntry;      // orientation tags + windows: bank, pitch, κH, κV, roll rate, width, speed intents
    public ExitClass[] PossibleExits;           // Upright, HighUpright, DivingUpright, …
    public ExitPlacement[] PlacementModes;      // Fixed | Solved | Region
    public SuccessorPolicy Recovery;            // None | RequiresRecovery | RequiresElement(id)
    public GeometryPrimitiveId[] RequiredPrimitives;
}

// PER-CANDIDATE — one concrete planning request.
public sealed class FeaturePlanRequest {
    public TrackConnectionFrame Entry;          // actual state, never assumed
    public SectionSpeedIntent EntrySpeed;
    public DemandRef[] ReservedDemands;         // identity-carrying (§5), ordered
    public SpatialExitGoal Goal;                // §3
    public ElevationEnvelope Elevation;
    public FootprintCorridor Corridor;
    public float ArcLengthBudget;
    public ClosureConstraints Closure;          // remaining winding tokens; lever-capacity snapshot (§7)
    public EntryContract ReservedSuccessor;     // or null
    public bool Required;
    public CandidateId Candidate;               // deterministic identity: (element, variant, mirror flags, draw index)
}

// ACHIEVED — binding result; its Definitions ARE the emission (single-pass, §6).
public sealed class FeaturePlanResult {
    public List<TrackMacroSectionDefinition> Definitions;
    public TrackConnectionFrame ExitStateAbsolute;
    public Pose RelativeExit;
    public Vector2 PlanDisplacement;
    public float ElevationChange, MinElevation, MaxElevation;
    public int HeadingContributionToken;        // asserted == Σ consumed demand tokens
    public DemandRef[] ConsumedDemands;         // identities, not values
    public Bounds SweptFootprint;
    public float ArcLength;
    public WeldWindow TransitionCompatibility;
    public ValidationSummary Physical;          // g-load, roll-rate, clearance at plan time
    public string FailureReason;                // structured; null on success
}
```

## 3. `SpatialExitGoal` — tokens are necessary, not sufficient

```csharp
public sealed class SpatialExitGoal {
    public int RequiredHeadingToken;                 // HARD
    public Vector2 PreferredExitPosition;            // SOFT — where the replaced geometry ended
    public Region PermittedExitRegion;               // HARD — corridor around the replaced segment
    public Vector2 PreferredPlanDisplacement;        // SOFT
    public FloatRange RadiusRange;                   // HARD (rulebook)
    public FloatRange ElevationTargetRange;          // HARD bounds, SOFT midpoint
    public FloatRange ExitPitchRange, ExitBankRange; // HARD (successor ∩ rulebook)
    public FloatRange ExitCurvatureWindow, ExitRollRateWindow; // HARD
    public FootprintCorridor Corridor;               // HARD (incl. quarter AABBs)
    public QuarterRestrictions Quarter;              // HARD
    public EntryContract ReservedSuccessor;          // HARD when present
}
```

**Construction:** when a demand slot is realized, the planner first computes the
*ordinary-curve baseline* of that slot with current project code's exact eased-arc math
(`EasedArcLength`/`ApplyEasedArc2D` — closure-exact). The baseline yields the preferred
exit position/displacement, the corridor (baseline swept AABB inflated by the rulebook
clearance), and the elevation window from that gap's elevation plan. A replacing
feature must land inside the permitted region with the exact token heading — a
replacement never silently relocates downstream geometry beyond the corridor.
**Closure consumption:** the 2D walk consumes `FeaturePlanResult.PlanDisplacement` +
`RelativeExit` — achieved values, never estimates; the existing special cases
(`HalfLoopTwist heading += 180` at `:1461`, quarter-gate lateral offsets at
`:1117,1142`) retire into this uniform path.

## 4. Universal contracts & transition resolver

Connection state = `TrackConnectionFrame` (position, axes, width, bank, pitch,
accumulated rotations, curvatures + rates, roll rate/accel, cross-section channels —
`TrackConnectionFrame.cs:16-88`) + `SectionSpeedIntent` alongside. A parallel
plan-time-lite type is rejected: it recreates plan/runtime divergence.

| Decision | Rule |
|---|---|
| **DirectWeld** | position/tangent exact (already guaranteed at welds); every channel delta inside `prev.WeldWindow ∩ next.AcceptedEntry` |
| **AdaptiveBlend** | reconcilable: length = max over violated channels of Δ/rate·v (resolved rates: bank/pitch/roll/width/cross-section seconds). Carries neighbor-mean curvature; may climb/bank/roll — **never defaults to straight** |
| **ExplicitRecovery** | either side's `Recovery == RequiresRecovery` — the currently dead `RequiresRecoveryAfter` flag (set by six patterns, read only by the exporter, `TrackDebugReportExporter.cs:268`) becomes this live signal |
| **Rejected** | transition exceeds footprint or limits → recorded reason → bounded backtracking. Never a silent straight |

**Transition-before-feature ordering:** read previous exact exit → read candidate's
entry contract → resolve & build the transition → obtain its exit → **plan the feature
from that state** → validate exit vs topology/space/reserved successor → accept /
alternate / backtrack. A feature is never planned first with geometry inserted before
it afterward. The resolver compares frames and contracts, never element names.

## 5. Compound-demand semantics

**Signed token sum is NOT proof of topological equivalence.** `{+90,+90}` and `{+180}`
share net winding but differ in intermediate headings, corner locations, footprints,
quarter ownership, adjacency opportunities, and closure behavior.

1. **Demand identity.** `DemandRef { SequenceIndex, Token, GapIndex, QuarterIndex,
   ClosureReserved }` — consumption tracked by identity, not value.
2. **Order.** Compounds consume **ordered adjacent** `DemandRef`s (consecutive
   `SequenceIndex`, same quarter; none span gates in v1).
3. **Intermediate topology state.** After each consumed token: intermediate heading +
   spatial anchor (the baseline realization's boundary pose). Compounds must pass
   within declared tolerance of every anchor — two anchors for `{+90,+90}`, one for
   `{+180}`. That is the difference made concrete.
4. **Per-token corridor.** Each demand contributes its baseline corridor; the
   compound validates against the union.
5. **Single-demand compounds:** batwing, butterfly, banana roll, Immelmann, dive loop,
   cutback `{±180}`; sidewinder `{±90}`.
6. **Sequence-required:** bent Cuban eight `{±90,±90}` adjacent same-sign. Cobra roll
   declares both `{±180}` and `{±90,±90}` — chosen at reservation, never reinterpreted.
7. **Subdivision** (one +180 → `{+90,+90}`) is a **token-solver operation (§6) before
   feature selection**: creates two real demands with their own anchor and corridors;
   the original identity is destroyed; legal only where the gap fits two baseline
   corners plus connector; recorded in the plan.
8. **Removal of replaced curves:** realizing marks `DemandRef` consumed;
   `EmitDefinitions` emits ordinary corners only for unconsumed demands; a def-scan
   validator proves no consumed demand's gap carries an ordinary corner.
9. **Closure confirmation:** the walk sums consumed tokens **by identity** — every
   reserved demand consumed exactly once, none skipped, Σ = ±360.

## 6. Canonical token solver (exact, deterministic)

**Bounded dynamic programming over demand slots** (replaces all "repair" wording):

- **State:** `(slotIndex ≤ MaxTurnCount ≤ 40, windingResidual ∈ [−16,+16] in 45° units,
  reservationMask ≤ 2⁸, quarterCursor ≤ 4)` — ≤ ~1.4 M states, exhaustive and exact.
- **Transitions:** place a signed token {±1,±2,±4} (45° units) constrained by:
  family-weight preferences (deterministic tie-break scores), direction-pattern sign
  policy, hairpin (±4) budget, closure-reserved slots restricted to
  ordinary-realizable tokens, required-feature reservations binding slots to
  compatible `SupportedDemandSequences` (including adjacent pairs), quarter
  boundaries. Subdivision (§5.7) is a guarded transition (±4 → two ±2).
- **Goal:** residual ±8 (±360°), count within [Min,Max], all reservations placed.
- **Determinism:** rng draws one preference ordering; DP explores in fixed
  lexicographic order; first solution is unique per seed.
- **Failure:** frontier collapse identifies the binding constraint layer — emitted as
  a structured explanation ("no ±90 slot outside closure reserve remains for required
  Sidewinder after quarter 2"). **Proven infeasibility, never repair exhaustion.**

## 7. Closure-capacity protection (candidate-specific)

No universal thresholds from measurement sessions:

```
Capacity(candidate):
  g  = candidate's current exact 2D closure residual (current project code's walk)
  per remaining lever i:  dᵢ = direction (straight heading; ∂pos/∂R for radii from the
        active-set linearization; lateral unit for S-bend slots; axis for reserves)
        cᵢ = [minᵢ,maxᵢ] span
  C∥ = Σ |dᵢ·ĝ|·span/2 ;  C⊥ = Σ |dᵢ·ĝ⊥|·span/2
  PASS iff C∥ ≥ M·|g|  and  C⊥ ≥ M·|worst-case lateral feature drift|
```

`M` = explicit policy margin (1.5, tunable — a policy constant, not a measured
threshold). **Foundation-D interim policy:** a gap may drop its lead straight only if
(a) not closure-reserved, (b) capacity still PASSes with it removed, (c) ≤1 drop per
quarter. **Deterministic restoration:** on closure failure, restore dropped straights
in reverse-drop order, re-solving after each, stop at first success; all recorded.

---

# PART II — MIGRATION OF CURRENT FEATURES

## 8. Complete audit — 14 registered patterns + 7 corner-slot realizations

**The former "14 + 3" count was wrong.** Current project code contains **14 registered
gap patterns** and **7 corner-slot realizations** (plus the half-loop corner
reservation routing to registered patterns). Enumeration = end-matter item 2.

### 8a. Registered patterns

**AR** approach/recovery straights emitted · **ER** builder entry-relative ·
**UA** upright/level/zero-κ entry assumed · **XS** exact exit state reported today ·
**F←/→F** may directly follow a curve / be directly followed (post-Foundation C) ·
**GP** global post-processing alters authored frames · **Str** identity string-inferred.

| Pattern | AR | ER | UA | XS | F←/→F | GP | Str | Migration |
|---|---|---|---|---|---|---|---|---|
| FullLoop | no | yes | contract Upright; builder tolerant | partial (stamp scalars) | yes/yes | banking field gated | phase-count inference | HeadingNeutral |
| Corkscrew (inline) | no | yes (`PhysicalRollBasis` preserves entry roll/pitch; `EndpointRateCorrection` blends κ/roll-rate) | no — `entry Any` (`FeaturePatterns.cs:494`) | partial | yes/yes | gated | `PatternId` prefix | HeadingNeutral **pilot** |
| DoubleCorkscrew | internal glue only | yes | no | partial | yes/yes | gated | **yes** (`StartsWith("DoubleCorkscrew")`, `TrackGenerationPipeline.cs:221`) | HeadingNeutral; identity → `SemanticElementId` |
| Spiral | **yes** (`SpiralApproach`+`RecoveryStraight`, `:801-807`) | yes | approach enforces it (planner convenience, not physics) | partial | blend/blend | gated | no | Neutral; approach→AdaptiveBlend, recovery→ExplicitRecovery pending test |
| HalfLoopRollout (Immelmann) | no | yes | Upright — **physical** (inversion entry) | partial + `IsHalfLoop` heading | yes/yes | gated | `StartsWith("HalfLoop")` | **already a ±180 TurnRealization** — first registry migration proof |
| HalfLoopToCorkscrew | no | yes | Upright — physical | partial | yes/yes | gated | same | ±180 TurnRealization variant |
| LoopToCorkscrew | glue transitions | yes | Upright | partial | yes/yes | gated | compound naming | Compound Neutral |
| SpiralToCorkscrew | glue | yes | Upright | partial | blend/yes | gated | same | Compound |
| JumpGap | **yes — physical** (boost run-up; landing at speed; `:219,331`) | yes (ballistic from entry) | level launch — physical | partial (landing `Region`) | recovery/recovery | no | `Quarter_` disambiguation | Neutral, `Placement=Region`; straights = justified ExplicitRecovery |
| JumpToBankedLanding | yes — same | yes | same | partial | recovery/blend | no | same | same |
| SCurve | no | yes | no | yes (net-0 by construction) | yes/yes | field applies (correct) | no | Transfer |
| Chicane | no | yes | no | yes | yes/yes | field applies | no | Transfer |
| AlternatingRadiusSequence | no | yes | no | partial | yes/yes | field applies | no | TurnRealization (sums to its slot's token) |
| FullPipe | no | yes | Upright — physical (closure morph) | partial | blend/blend | pipe channel owns cross-section | no | Neutral |

**Summary:** every builder is entry-relative; **no world resets anywhere**; upright
entry is physical only for inversion/pipe entries; approach/recovery straights physical
only for jumps; string identity in 4 call sites (`TrackGenerationPipeline:208-227`,
`TrackCandidateBuilder:514,530`, `QuarterRoadFitter:528`) retired by `SemanticElementId`.

### 8b. Corner-slot realizations (`EmitCorner`, `TrackTopologyPlanner.cs:1276-1340`)

| Realization | Tokens (post-E) | Demands | Entry | Exit | Internal arcs | Special handling today | →Feature | Recovery | Migration |
|---|---|---|---|---|---|---|---|---|---|
| Ordinary curve (default) | ±45/90/180 | 1 | none | eased arc end: κ→0, bank→0; radius `Solved` | 1 | none | **yes** | at hairpin magnitude only | baseline TurnRealization |
| Hairpin | ±180 | 1 | none | arc end + locked `RecoveryStraight` (`:1340`) | 1 | `IsSpecial`; winding min 150 (`:560`) | after recovery | **yes** (physical) | ±180 TurnRealization, `RequiresRecovery` |
| DoubleApex | ±90/±180 | 1 | none | second arc end | 2 + locked `ApexLink` straight (`:1303-08`) | `EmitCorner` case | yes | no | TurnRealization recipe |
| TighteningCorner | ±45/90/180 | 1 | none | tighter second arc | 2, no internal straight (`:1313-30`; r×1.4→r×0.8) | `EmitCorner` case | yes | no | TurnRealization recipe |
| OpeningCorner | ±45/90/180 | 1 | none | wider second arc | 2 (reversed radii) | same | yes | no | same |
| SweeperIntoHairpin | ±180 | 1 | none | hairpin end + recovery | 2 arcs + `SweeperLink` straight + recovery (`:1280-96`) | **VERIFIED (Stage B):** the preceding sweeper slot is NOT absorbed — adjacency (`:555-567`) is a selection heuristic only; the realization splits its own slot's angle into sweep+link+pin, and `prev` keeps its own demand. Single-demand recipe with adjacency-aware selection — not a multi-demand precursor. | after recovery | yes | single-demand compound recipe |
| WallrideTurn | ±45/±90 (quantized from today's 60–140° window, `:442-453`) | 1 | none | wallride exit + recovery (`:1259`) | 1 (wall channel) | `EmitCorner` case + cross-section ownership | after recovery | **yes** (physical) | TurnRealization, `RequiresRecovery` |

---

# PART III — SHARED GEOMETRY PRIMITIVES

## 9a. Primitive vocabulary → current project code

| Primitive | Existing code | Status |
|---|---|---|
| Planar eased arc | `EasedArcLength`/`ApplyEasedArc2D` + arc profile cache | **done** — closure-exact |
| Vertical eased arc / half & full loop | `BuildLoop`, loop theta tables, `HalfLoopArcLength` | **done** |
| Half/full corkscrew roll | monotone-Hermite roll CDF, whole-revolution rule, hold-speed sizing | **done** |
| Authored roll on transported tangent | roll CDF exists; transport new | **partial** |
| Rotation-minimizing frame transport | — | **new** (double-reflection RMF; Family F cornerstone) |
| Climb/dive, crest & valley | elevation channels (`HillHeight`, `PitchChange`) | **partial** — first-class builder in Family G |
| Lateral transfer | `EasedSBendUnitOffset` | **done** |
| Bank/pitch/curvature/width transitions | resolved rate limits + eased envelopes | **done** — resolver's blend generators |
| Orientation recovery | recovery straights + field feather | **done** → ExplicitRecovery output |
| Compound sequencing | `ChainedComPattern` (`:959`) | **partial** → recipe executor |
| Mirroring/reversal | per-builder sign conventions | **partial** → formal `TurnMirror`/`RollMirror` |

## 9b. Element → recipe map (corrected)

- **Batwing** = half-loop(up) + roll transition + half-loop(down, opposed) — recipe only.
- **Cobra roll** = half-loop + half-corkscrew + mirrored half-corkscrew + half-loop.
- **Sea serpent** = half-loop + **opposing half-corkscrews** + half-loop; net 0; exits
  entry direction. **Roll over** = half-loop portions + **opposing half inline
  twists**; net 0. **Two identities, two recipes — shared primitives only.**
- **Camelback / speed hill / double dip** = crest/valley compositions (one builder,
  different amplitude/pitch envelopes).
- **Top hat (standard)** = crest carrying a **180° heading fold**: climb limb, vertical
  crest arc, reversed descend limb — unlike camelback's 0° crest. (Optional future
  **Inline Top Hat**, 0°, separate capability entry — not the default.)
- **Inline twist / heartline roll** = transported-roll primitive with pivot-offset
  parameter (track-center vs heartline) — answers the pivot question once.
- **Immelmann / dive loop** = same half-loop + half-roll parts, opposite order.
- **Sidewinder** = half-loop + half-corkscrew exiting ±90.
- **Cutback / directional corkscrew** = rolling-turn primitive, distinct contracts.
- **Wave turn** = ±180 crest turn, apex bank *outward* (bank sign opposes turn sign) —
  independent bank channel on the J-family arc.
- **Stengel dive** = camelback crest + rapid overbank (real-world definition;
  installations vary — no authoritative heading range claimed). Slingshot canonical:
  **StengelDive45** (±45) and **StengelDive90** (±90).

---

# PART IV — FEATURE CATALOG

**IH** = notable intermediate headings. Entry flags C/cl/B = accepts curved / climbing /
banked entry. Rec = normally needs recovery. Diff 1–5.

### 10a. Vertical-profile family (G)

| Element | Status | Tokens | IH | Entry→Exit | Elev | Inv | Roll | Role | C/cl/B | Rec | Diff | Wave |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Camelback | new | 0 | none | Upright→Upright | up-down | 0 | none | Neutral | y/y/part | no | 1 | G |
| Speed hill | new | 0 | none | same, flatter | small | 0 | none | Neutral | y/y/y | no | 1 | G |
| Double dip | new | 0 | none | same | two drops | 0 | none | Neutral | y/y/part | no | 1 | G |
| **Top hat (standard)** | new | **±180** | ±90 at vertical limbs | Upright climb→Upright descend, **opposite direction** | large | 0 | none | **TurnRealization** | y/cl-req/n | no | 3 | **G, requires E** |
| Top hat (inline variant) | deferred | 0 | ±90 | same direction | large | 0 | none | Neutral | y/cl-req/n | no | 3 | deferred |
| Dive drop | new | 0 | none | Upright high→VerticalDescending | −large | 0–1 | 0/180 | OrientationTransition | y/n/n | after | 3 | G |

### 10b. Transported-roll family (F)

| Element | Status | Tokens | IH | Entry→Exit | Inv | Roll | Role | C/cl/B | Rec | Diff | Wave |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Inline corkscrew | **exists** — name accurate | 0 | net-0 yaw envelope | Any→Upright | 1/rev | 360×N | Neutral | y/y/y | no | — | pilot |
| Double corkscrew | **exists** | 0 | same | Any→Upright | 2 | 720 | Neutral | y/y/y | no | — | done |
| Directional corkscrew | new | 0/±45/±90 | progressive | Upright-ish→Upright | 1 | 360 | TurnRealization | y/y/part | no | 4 | F |
| Cutback | new | ±180 | ±90 mid | Upright→Upright | 1 | 2×180 opposed | TurnRealization | y/part/part | no | 4 | F |
| In-line twist | new | 0 | none | Upright→Upright | 1 | 360 track-axis | Neutral | y/y/y | no | 2 | F |
| Heartline roll | new | 0 | none | Upright→Upright | 1 | 360 heartline | Neutral | y/y/y | no | 2 | F |

### 10c. Loop & partial-loop family (H)

| Element | Status | Tokens | IH | Entry→Exit | Elev | Inv | Roll | Role | C/cl/B | Rec | Diff | Wave |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Vertical loop | **exists** | 0 | none | Upright→Upright | net 0 | 1 | none | Neutral | y/part/part | no | — | done |
| Immelmann | **exists** (`HalfLoopRollout`) | ±180 | — | Upright→Upright high | +large | 1 | 180 | TurnRealization | part/n/n | no | — | done→E |
| Dive loop | new | ±180 | — | Upright high→Upright low | −large | 1 | 180 | TurnRealization | part/n/n | no | 2 | H |
| Sidewinder | new | ±90 | — | Upright→Upright | up-over | 1 | 180 | TurnRealization | part/n/n | no | 3 | H |
| Junior Immelmann | new | ±180 | — | Upright→Upright, no inversion | up | 0 | ≤90 overbank | TurnRealization | part/n/n | no | 2 | H |
| Inclined loop | new | 0 (rw: 0–45) | tilted plane | Upright→Upright | net 0 | 1 | plane tilt | Neutral | part/n/part | no | 3 | H |
| Inclined dive loop | new | ±180 (rw: 90–180) | — | high→low | −large | 1 | 180 tilted | TurnRealization | part/n/n | no | 3 | H |
| Non-inverting loop | new | 0 | — | Upright→Upright | net 0 | 0 | counter-twist at top | Neutral | part/n/n | no | 3 | H |
| Finnish loop | new | 0 | — | Upright→Upright | net 0 | 1 | heartline at crown | Neutral | n/n/n | no | 4 | I |
| Raven turn | new | 0 (rw varies) | — | Inverted↔Upright over crest | drop | boundary | 180 across crest | OrientationTransition | n/n/n | after | 5 | I |

### 10d. Compound inversions (I)

| Element | Status | Tokens | IH | Inv | Recipe | Role | Diff | Wave |
|---|---|---|---|---|---|---|---|---|
| Batwing | new | ±180 | ±90 between inversions | 2 | 2×half-loop + opposed rolls | Compound | 4 | I |
| Cobra roll | new | ±180 **or** {±90,±90} | ±90 anchor mid | 2 | half-loops + half-corkscrews | Compound | 4 | I |
| **Sea serpent** | new | 0 net | ±90 excursions | 2 | half-loop + opposing **half-corkscrews** + half-loop | Compound-Neutral | 4 | I |
| **Roll over** | new | 0 net | smaller IH | 2 | half-loop portions + opposing **half inline twists** | Compound-Neutral | 4 | I |
| Butterfly | new | ±180 | ±90 | 2 | crossed half-loops | Compound | 4 | I |
| Bent Cuban eight | new | {±90,±90} adjacent | +90 anchor | 2 | loops+rolls sequence | Compound | 5 | I |
| Pretzel loop | new | 0 | deep | 2 | dive-through loops | Compound-Neutral | 5 | I |
| Pretzel knot | new | 0 (rw varies) | interleaved | 2 | same family | Compound-Neutral | 5 | I |
| Norwegian loop | new | 0 | dive-through | 2 | dive loop + Immelmann | Compound-Neutral | 5 | I |
| Banana roll | new | ±180 (rw: 90–180) | — | 1 | tall in-out roll | Compound | 4 | I |

### 10e. Non-inverting specialty turns (J)

| Element | Status | Tokens | IH | Bank note | Role | Diff | Wave |
|---|---|---|---|---|---|---|---|
| Ordinary curve | **exists** | ±45/90/180 | none | per demand | TurnRealization | — | done |
| Hairpin | **exists** | ±180 | none | high | TurnRealization | — | done |
| Overbanked turn | new | ±45/90/180 | none | **bank >90° mid — bank ≠ heading** | TurnRealization | 2 | J |
| Horseshoe | new | ±180 | ±90 at crest | high | TurnRealization | 2 | J |
| Hammerhead | new | ±180 | overshoot-and-return internal motion; net exactly 180 | steep | TurnRealization | 3 | J |
| **Wave turn** | new | **±180** | ±90 | ~90° **outward** bank at apex — heading independent of bank | TurnRealization | 3 | J |
| **Stengel dive** | new | **±45 / ±90** (canonical variants) | crest fold | rapid overbank on camelback crest; no rw range claimed | TurnRealization | 3 | J (needs G crest) |
| Non-inv. cobra roll | new | ±180 | ±90 | shape without inversion | Compound | 4 | J |

### 10f. Existing infrastructure
S-curve/chicane/closure S-bend (transfers, net 0, **continuous internal angles
preserved** — load-bearing closure levers), jumps (`Region` placement), spiral, full
pipe, quarter transfers, recovery realizations — as audited in §8.

---

# PART V — IMPLEMENTATION ORDER

| Stage | Content | Depends | Failure policy | Completion criteria | Unlocks |
|---|---|---|---|---|---|
| **A — Authoritative results** | Capability/Request/Result for all **21** realizations; commit-rng probing (clone; commit accepted state; never re-run accepted); `SemanticElementId` (serialized int, default 0) | — | additive | **Corrected acceptance:** geometry-affecting definitions identical · frame geometry identical · feature selection identical · rng draw count **and final state** identical · existing report fields unchanged · new fields match approved output · zero placement/length/radius/heading/elevation/orientation changes | everything |
| **B — Contract audit** | Truthful `AcceptedEntry` for all 21; verify SweeperIntoHairpin slot-absorption semantics; **no adjacency assumed** — contracts encode found physical requirements | A | n/a | contract-vs-builder fuzz at window edges passes | C |
| **C — Universal resolver (reporting)** | classify every boundary; blends computed, not emitted; `RequiresRecoveryAfter` live | A,B | n/a | 0 Rejected on preset battery; per-channel reasoning reported | D |
| **D — Representative adjacency** | **Existing geometry only:** Curve→InlineCork→Curve · Curve→FullLoop→Curve · Spiral→InlineCork · HalfLoopRollout→Curve · SCurve→FullLoop · FullPipe→Curve · Curve→Jump · LoopToCorkscrew→Curve · one AdaptiveBlend pair · one ExplicitRecovery pair · one Rejected pair. §7 interim capacity policy governs straight drops. **Hill demos moved to G.** | C | Rejected→keep straight, report | closure failure ≤1.1× baseline, 128 seeds; all 11 demos as tests; welds bit-exact | E |
| **E — Canonical demands** | §6 token DP replaces the ±20°-step winding balancer (`:552-598`); `DemandRef` identities + subdivision; Immelmann migrates `IsHalfLoop`→registry (±180 proof); flag `useCanonicalTurns` (legacy path byte-identical) | A | DP infeasibility → structured | token-sum property 512 seeds; validity ≥0.8× baseline; single-count scan | F–J |
| **F — Transported-roll** | RMF transport + plan-arc composition (channel-model extension rejected: the orbit-envelope ×½ episode evidences angle-space coupling risk); directional corkscrew ±45/±90, in-line twist, heartline roll, cutback | E | structured per candidate | exit token exact 1e-3 × 4 mirror variants; retopology preserves unwrapped roll (asserted) | I |
| **G — Vertical-profile** | crest/valley builder; camelback, speed hill, double dip, dive drop (**0-token, E-independent**); **top hat (±180) requires E** | D (hills); E (top hat) | structured | pitch-rate + airtime at design speed validated | I, J |
| **H — Loop/partial-loop** | dive loop, sidewinder ±90, junior Immelmann, inclined/non-inverting loops | E, F | structured | exit tokens exact; inversion metrics correct | I |
| **I — Compound inversions** | recipe executor (upgraded `ChainedComPattern`); batwing, cobra (sequence machinery proven here first), **sea serpent and roll over as separate recipes**, butterfly; then bent Cuban eight, pretzel family, Finnish, raven | F,G,H + §5 | required: token-solver re-reserve then structured fail | **planner diff = registry entries only**; sequence-demand ledger test | full catalog |
| **J — Specialty turns** | bank >90° ceiling + catch-channel handling; overbanked, horseshoe, hammerhead, **wave turn ±180**, **Stengel variants (needs G crest)** | E (G for crests) | structured | bank >90° stable in retopology + camera tilt-cap interaction | — |

---

# PART VI — VALIDATION & WORKED TRACES

## 11. Validation strategy

Per stage: golden determinism (both flag states) · measured-vs-token assertion
(`StampRotationalEventPlan` becomes the verifier) · **demand-identity ledger** (every
`DemandRef` consumed exactly once) · subdivision corridor test · capacity-check
determinism (drop/restore replay) · token-DP exhaustiveness (infeasible configs yield
binding-constraint explanations, never timeouts) · weld bit-compare at every DirectWeld
· per-channel rate scan at every boundary · mirror-reflection (turn × roll) ·
no-world-reset frame audit · retopology unwrapped-rotation preservation ·
structured-failure non-emptiness · chain scan (every straight closure-reserved,
recovery-justified, or blend-derived). Family-specific: G airtime/pitch at 361 m/s;
I sequence winding; J bank>90° vs camera tilt-cap and catch-wall channels.

## 12. Worked planner traces

Notation: E entry → D demand(s) → R realization → T transition → X exit → H↓ downstream
heading · CC closure contribution. All welds position/tangent-exact.

1. **Curve → Inline Corkscrew → Curve.** E: eased-arc end (κ≈0, bank≈0). D: none —
   the next +90 belongs to the following slot. T: DirectWeld both sides (cork exits
   upright, rates 0). H↓: the curves' tokens only. CC: 0 from the cork.
2. **Camelback → 90° Curve** *(post-G)*. E: camelback exit, pitch inside the curve
   window, κv≈0 by crest envelope. D: +90 (curve's own). T: DirectWeld. CC: +90 once.
3. **Directional Corkscrew replacing +45.** D: `DemandRef{i,+45}` consumed — ordinary
   curve **not emitted** (def-scan). R: DirectionalCorkscrew(+45, roll L); plan-view
   component is an eased +45 arc → closure agreement by construction. X: upright,
   +45, rates 0. CC: +45 by identity.
4. **Immelmann replacing +180.** Today's `IsHalfLoop` behavior through the registry:
   D +180 → R HalfLoopRollout → X reversed, high. CC: +180 once; `:1461` special case
   retired. The Stage-E migration proof.
5. **Sidewinder replacing +90.** D +90 → R Sidewinder(right, roll R). X: upright,
   +90, elevated. T(out): AdaptiveBlend if successor's pitch window is tighter than
   the exit dive. CC: +90 once.
6. **Batwing replacing +180.** D +180 reserved (required) → R Batwing recipe; hairpin
   never instantiated. X: reversed, entry-level elevation. CC: +180 once.
7. **Cobra roll as compound.** Reserved form chosen at reservation: `{+90,+90}` (two
   adjacent `DemandRef`s, intermediate anchor between them) **or** one split +180
   (subdivision created the pair before selection). Def-scan: both replaced corners
   absent. X: reversed.
8. **Top Hat → Dive Loop** *(corrected)*. Top Hat consumes `DemandRef{i,+180}`; exits
   Upright, high, reversed, slow-intent. T: DirectWeld (dive loop *wants* high entry).
   Dive Loop consumes `DemandRef{i+1,−180}`; exits low, fast, reversed again. Net 0
   winding from the pair — an S-shaped double reversal trading altitude for speed.
   CC: two distinct identities consumed; neither ordinary corner emitted.
9. **Spiral → Inline Corkscrew.** E: spiral exit after reclassified blend (residual
   bank 6°, small κh). Resolver: inside cork's `entry Any` window → DirectWeld; the
   old `RecoveryStraight` is not emitted. CC: 0 net from both.
10. **AdaptiveBlend pair.** Overbanked turn exit (bank 95°) → camelback (bank ≤15°).
    Blend = Δbank/bankRate·v — a curving un-banking climb, not a straight; reported
    per-channel.
11. **ExplicitRecovery pair.** Jump landing → next: landing's `RequiresRecovery`
    (physical at design speed) keeps the recovery straight — justified, not
    structural.
12. **Required compound → deterministic backtrack.** Required cobra roll; reserved
    `{+90,+90}` fails footprint (quarter AABB). Order: mirrored signs → alternate
    adjacent pair → split-180 form → token-DP re-reservation → structured fail.
    Same seed replays identically.
13. **Required feature, no compatible demand.** Required sidewinder; all ±90 demands
    closure-reserved. DP proves infeasibility (frontier collapse at the reservation
    layer) → `RequiredFeatureMissing` naming the binding constraint. Never a silent
    curve.
14. **A full quarter, mixed families** *(post-G)*. Gate → 45 curve → camelback →
    directional cork (+90) → S-curve (0) → Immelmann (+180) → closure-reserved
    straight → 90 curve → gate. Exactly one straight, closure-owned; two
    AdaptiveBlends (camelback→cork pitch; Immelmann-exit→reserve); every boundary
    carries a recorded decision; demand ledger balances.

---

# PART VII — FEATURE ROADMAP MATRIX (complete)

| Feature | Family | Support | Primitives | Tokens | Entry restr. | Exit | Wave | Cx | Key validation risk |
|---|---|---|---|---|---|---|---|---|---|
| Ordinary curve | J | full | planar arc | ±45/90/180 | none | solved | done→E | — | token migration |
| Hairpin | J | full | planar arc | ±180 | none | solved | done→E | — | recovery policy |
| DoubleApex | J | full | 2 arcs+link | ±90/±180 | none | fixed | done→E | 1 | recipe migration |
| TighteningCorner | J | full | 2 arcs | ±45/90/180 | none | fixed | done→E | 1 | — |
| OpeningCorner | J | full | 2 arcs | ±45/90/180 | none | fixed | done→E | 1 | — |
| SweeperIntoHairpin | J | full | arcs+link+recovery | ±180 | none | fixed | done→E | 2 | slot-absorption semantics (verify at B) |
| WallrideTurn | J | full | arc+wall channel | ±45/±90 | none | fixed | done→E | 1 | angle quantization |
| S-curve / Chicane | transfer | full | arc pair | 0 | none | fixed | done | — | — |
| Vertical loop | H | full | full loop | 0 | Upright | fixed | done | — | — |
| Inline corkscrew | F | full | roll CDF | 0 | Any | fixed | pilot | — | — |
| Double corkscrew | F | full | roll×2 | 0 | Any | fixed | done | — | string identity |
| Immelmann | H | full | half-loop+half-roll | ±180 | Upright | fixed | done→E | — | registry migration |
| HalfLoopToCorkscrew | H | full | +roll | ±180 | Upright | fixed | done→E | 1 | — |
| Loop/Spiral compounds | mixed | full | glue | 0 | Upright | fixed | done | 1 | glue reclassification |
| Spiral | infra | full | helix | 0 | Upright* | fixed | D | 1 | approach reclassification |
| Jump family | infra | full | ballistic | 0 | level | region | done | — | recovery stays |
| Full pipe | infra | full | pipe morph | 0 | Upright | fixed | done | — | — |
| Directional corkscrew | F | none | RMF+roll+arc | 0/±45/±90 | Upright-ish | fixed | F | 4 | exit exactness; retopo roll |
| Cutback | F | none | RMF+opposed rolls | ±180 | Upright | fixed | F | 4 | roll-reversal rates |
| In-line twist | F | none | transported roll | 0 | Upright | fixed | F | 2 | axis-offset foldover |
| Heartline roll | F | none | transported roll (heartline) | 0 | Upright | fixed | F | 2 | pivot vs road width |
| Camelback | G | none | crest/valley | 0 | none | fixed | G | 1 | airtime g |
| Speed hill | G | none | crest | 0 | none | fixed | G | 1 | — |
| Double dip | G | none | crest×2 | 0 | none | fixed | G | 1 | — |
| **Top hat (standard)** | G | none | vertical crest + 180 fold | **±180** | climbing | fixed | **G (needs E)** | 3 | near-vertical camera/retopo |
| Top hat (inline) | G | none | vertical crest | 0 | climbing | fixed | deferred | 3 | variant scoping |
| Dive drop | G | none | dive+optional roll | 0 | Upright high | fixed | G | 3 | roll-while-diving |
| Dive loop | H | none | half-roll+half-loop | ±180 | high | fixed | H | 2 | elevation source |
| Sidewinder | H | none | half-loop+half-cork | ±90 | Upright | fixed | H | 3 | exit exactness |
| Junior Immelmann | H | none | overbank half-loop | ±180 | Upright | fixed | H | 2 | non-inversion bank cap |
| Inclined loop | H | none | tilted loop | 0 | Upright | fixed | H | 3 | plane-tilt validators |
| Inclined dive loop | H | none | tilted half-loop | ±180 | high | fixed | H | 3 | same |
| Non-inverting loop | H | none | loop+counter-twist | 0 | Upright | fixed | H | 3 | twist budget |
| Finnish loop | I | none | loop+heartline | 0 | Upright | fixed | I | 4 | crown roll rates |
| Raven turn | I | none | crest inversion boundary | 0 | inverted side | fixed | I | 5 | inverted-hold camera/physics |
| Batwing | I | none | 2×half-loop+roll | ±180 | Upright | fixed | I | 4 | elevation envelope @361 m/s |
| Cobra roll | I | none | half-loops+half-corks | ±180 or {±90,±90} | Upright | fixed | I | 4 | sequence-demand winding |
| **Sea serpent** | I | none | half-loops + opposing half-corkscrews | 0 | Upright | fixed | I | 4 | net-0 compound bounds |
| **Roll over** | I | none | half-loops + opposing half inline twists | 0 | Upright | fixed | I | 4 | twist-pair clearance |
| Butterfly | I | none | crossed half-loops | ±180 | Upright | fixed | I | 4 | self-clearance |
| Bent Cuban eight | I | none | loops+rolls seq | {±90,±90} | Upright | fixed | I | 5 | multi-demand closure |
| Pretzel loop / knot | I | none | dive-through loops | 0 | Upright | fixed | I | 5 | self-intersection corridor |
| Norwegian loop | I | none | dive loop+Immelmann | 0 | Upright | fixed | I | 5 | same |
| Banana roll | I | none | tall in-out roll | ±180 | Upright | fixed | I | 4 | envelope height |
| Overbanked turn | J | none | arc+bank>90 | ±45/90/180 | none | solved | J | 2 | camera tilt-cap; catch wall |
| Horseshoe | J | none | arc+crest | ±180 | none | fixed | J | 2 | crest-bank coupling |
| Hammerhead | J | none | climb-turn-dive | ±180 | none | fixed | J | 3 | overshoot netting exactly 180 |
| **Wave turn** | J | none | crest+outward bank | **±180** | none | fixed | J | 3 | outward-bank physics |
| **Stengel dive** | J | none | camelback crest+overbank | **±45 / ±90** | none | fixed | J (needs G) | 3 | crest-overbank coupling |
| Non-inv. cobra roll | J | none | cobra shape, no inversion | ±180 | Upright | fixed | J | 4 | — |

---

# END MATTER (required)

## 1. Factual catalog corrections made
1. **Top hat:** 0° → **±180** standard (exits opposite entry); inline/shuttle 0°
   variant separate and deferred; now consumes a demand ⇒ requires Stage E.
2. **Wave turn:** ±45/90 → **±180**; its ~90° *outward bank* had been conflated with
   heading — the channels are independent.
3. **Sea serpent / roll over:** one row → **two semantic identities** with distinct
   recipes (opposing half-corkscrews vs opposing half inline twists); both net 0.
4. **Stengel dive:** invented "rw: 30–90°" range removed; real-world definition
   (camelback + rapid overbank) recorded without a heading claim; canonical
   StengelDive45/StengelDive90 defined separately.
5. **Hammerhead:** internal overshoot-and-return documented as intermediate motion;
   net exactly ±180.
6. **Six-quantity discipline** (heading/bank/roll/inversion/intermediate/final) applied
   across all family tables (IH column + bank notes).

## 2. Complete pattern enumeration (audit-coverage proof)
**Registered (14):** JumpGap, JumpToBankedLanding, FullLoop, Corkscrew, Spiral,
HalfLoopRollout, HalfLoopToCorkscrew, LoopToCorkscrew, SpiralToCorkscrew,
DoubleCorkscrew, SCurve, Chicane, AlternatingRadiusSequence, FullPipe
(`FeaturePatternLibrary`, `FeaturePatterns.cs`).
**Corner-slot (7):** Ordinary curve (default case), Hairpin, DoubleApex,
TighteningCorner, OpeningCorner, SweeperIntoHairpin, WallrideTurn (`EmitCorner`,
`TrackTopologyPlanner.cs:1276-1340`; assignment `:335,453,475,480-498`).
**Cross-listed:** the half-loop corner reservation (`IsHalfLoop`) routes corner slots
to registered patterns HalfLoopRollout / HalfLoopToCorkscrew — audited under both.

## 3. Final data-model definitions
`FeatureCapability` (static registry), `FeaturePlanRequest` (per-candidate),
`FeaturePlanResult` (achieved, binding) — §2; `SpatialExitGoal` with HARD/SOFT tagging,
baseline-derived construction, and achieved-exit closure consumption — §3.

## 4. Canonical token-solver method
Bounded exact dynamic programming (§6): state (slot ≤40, winding residual ±16 in 45°
units, reservation mask ≤2⁸, quarter cursor) ≈1.4 M states; deterministic seed-fixed
preference ordering; subdivision as a guarded transition; returns a proven sequence or
the binding constraint layer as structured failure. No open-ended repair.

## 5. Compound-demand reservation rules
§5: identity-carrying `DemandRef`s; ordered **adjacent** sequences only; intermediate
heading + spatial anchor after every consumed token; per-token corridors validated as a
union; declared single-demand vs sequence-required forms; subdivision only via the
token solver **before** feature selection (destroys the original identity); consumed-
exactly-once ledger confirmed by the closure walk.

## 6. Candidate-specific closure-capacity policy
§7: directional capacities C∥/C⊥ from the candidate's actual remaining levers vs its
actual residual; margin M an explicit policy constant (1.5); Foundation-D interim rule
(non-reserved gaps only, capacity recomputed per drop, ≤1 drop/quarter); deterministic
reverse-order restoration on closure failure, all recorded. No session-derived
thresholds.

## 7. Corrected implementation-readiness checklist
- [ ] **A:** 21 realizations return Capability+Result; corrected acceptance passes
      (geometry/frames/selection/rng identical; existing fields unchanged; new fields
      approved).
- [ ] **B:** truthful entry contracts ×21; SweeperIntoHairpin absorption semantics
      verified and recorded.
- [ ] **C:** resolver classifies 100% of boundaries; zero Rejected on presets.
- [ ] **D:** 11 existing-geometry demos pass; interim capacity policy active; closure
      failure ≤1.1× baseline over 128 seeds.
- [ ] **E:** token DP live; `DemandRef` identities; Immelmann via registry; legacy
      flag path byte-identical.
- [ ] **F:** directional corkscrew/cutback/twists exit-exact ×4 mirror variants;
      retopology roll preserved.
- [ ] **G:** hills (0-token) independent of E; **top hat gated on E**.
- [ ] **H:** partial-loop family exit-exact; inversion metrics correct.
- [ ] **I:** sequence machinery proven on cobra `{±90,±90}` before bent Cuban eight;
      sea serpent and roll over as separate recipes; planner diff = registry only.
- [ ] **J:** wave turn ±180; Stengel variants after G's crest primitive; bank >90°
      stable against camera tilt-cap and catch-wall channels.

## Appendix — open designer questions
1. **Pacing floor** once chains legalize: minimum straight-per-quarter as a gameplay
   rule, or closure capacity as the only floor?
2. **Roll–turn correlation** for directional rolling elements: into the turn,
   independent, or per-preset?
3. **±180 market:** registry weights for hairpin vs batwing vs cutback vs cobra vs
   Immelmann vs top hat vs wave turn, per style preset?
4. **`useCanonicalTurns` default timing:** at Stage E, or held until F ships?
5. **AdaptiveBlend visual identity:** distinct guide markings, or indistinguishable
   road?
6. **Subdivision policy:** one +180 → `{+90,+90}` always legal, or per-preset (it
   changes corner rhythm)?

---
*End of consolidated corrected plan. No project code modified. Awaiting review.*
