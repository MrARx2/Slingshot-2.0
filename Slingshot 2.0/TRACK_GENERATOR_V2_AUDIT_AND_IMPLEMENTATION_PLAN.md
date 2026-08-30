# Track Generator V2 — Audit and Implementation Plan

**Project:** Slingshot 2.0  
**Audit date:** 2026-08-23  
**Status:** Proposed implementation authority  
**Scope:** Track-generation architecture, connectors, deterministic sharing, feature planning, validation, and authoring workflow

---

## 1. Executive verdict

The current Track Generator is a strong foundation and should **not** be rewritten.

Its best qualities are already the difficult ones:

- a staged plan → build → validate → score → select pipeline;
- good generated-track driveability;
- explicit connection frames and orientation handling;
- deterministic subsystem random streams;
- transactional generation that keeps the last valid track when a new candidate fails;
- semantic feature identifiers and partial feature-capability contracts;
- exact post-build feature exit measurements;
- a large editor test suite and a frozen seed corpus.

The main architectural weakness is that several newer abstractions are present but are not yet authoritative. Connector decisions still depend on hard-coded section-type lists, while `FeatureCapabilities`, `FeaturePlanResult`, and `TransitionResolver` contain more accurate semantic information but are partly advisory or reporting-only.

The correct V2 path is therefore:

1. freeze and measure the current good behavior;
2. make a complete, versioned **Generation Recipe** the shareable unit instead of a bare integer seed;
3. unify connector and transition decisions behind one semantic system;
4. separate **what topology the route needs** from **which track element realizes it**;
5. add new features through a catalog of compatible realizations, starting with simple reusable shapes;
6. let the designer replace individual generated realizations while preserving the surrounding layout wherever possible;
7. store every handpicked replacement as a deterministic, shareable recipe variant;
8. preserve ordinary curves and straights as reliable fallbacks;
9. expand only when yield, generation time, determinism, and driveability stay within explicit gates.

This gives us a V2 that is cleaner and more extensible without sacrificing what already feels good to drive.

---

## 2. Audit boundaries

This audit treats the current working implementation as evidence, including active experimental V2 curvature-flow work. It does **not** assume every uncommitted experiment is production-safe.

The audit covers:

- generation entry points and setup ownership;
- topology planning and feature assignment;
- connectors, transitions, and recovery;
- seed streams and reproducibility;
- validation, scoring, failure policy, and test strategy;
- Inspector workflow;
- a scalable architecture for future roller-coaster elements.

It does not redesign the hovercraft physics, camera, HUD, materials, or track art except where those systems consume track metadata.

---

## 3. Current system map

### 3.1 Generation flow

The current generator broadly performs the following sequence:

1. `TrackGenerator` receives a generation command.
2. Designer settings, preset choices, feature rules, locks, and seed streams are resolved.
3. `TrackTopologyPlanner` creates a macro layout and feature allocation.
4. `ConnectorAnalyzer` classifies and adjusts straight connector sections.
5. Closure and elevation planning refine the route.
6. Builders create the actual section frames and mesh data.
7. Validators reject unsafe or invalid candidates.
8. Valid candidates are scored and the best candidate is selected.
9. Meshes, markings, materials, race metadata, checkpoints, and start data are built.
10. The new root replaces the old root transactionally.

This separation is fundamentally sound.

### 3.2 Existing section vocabulary

The macro vocabulary already includes:

- straights, wide straights, boost straights, and recovery straights;
- banked curves and hairpins;
- S-curves and chicanes;
- jump ramps, air gaps, and landing ramps;
- loops, corkscrews, spirals, half-loop twists, full pipes, and wallride turns;
- generic rotational events;
- tunnel and bridge variants.

The semantic vocabulary is richer than the raw section-type vocabulary. It already identifies ordinary curves, specialized corner realizations, transfers, inversions, compounds, jumps, full pipes, recovery sections, and directional corkscrews. Several future elements are also reserved in the semantic enum.

### 3.3 Existing foundations for V2

The current code already contains the beginnings of the V2 design:

- `CornerSlot` represents a route demand before choosing a corner realization.
- `SemanticElementId` distinguishes gameplay meaning from low-level section type.
- `FeatureCapabilities` describes entry constraints, topology roles, recovery needs, and whether a feature accepts curved entry.
- `FeaturePlanResult` records the exact achieved relative exit position, rotation, heading, elevation, footprint, and roll from the real builder.
- `TransitionResolver` evaluates boundaries using actual built states and semantic capabilities.
- `TrackConnectionFrame` is the geometric source of truth at section boundaries.

V2 should generalize and connect these systems rather than create parallel replacements.

---

## 4. What is already strong

### 4.1 Driveability-first candidate selection

The generator rejects complete candidates instead of forcing invalid geometry into a track. This is a good policy for a high-speed hovercraft game. It makes generation slower than a decorative spline generator, but it protects playability.

### 4.2 Transactional replacement

A failed generation does not need to destroy the last valid track. The existing transactional swap behavior should remain a hard V2 invariant.

### 4.3 Orientation representation

Quaternion-based frames, unwrapped roll, and explicit feature exit records are the right basis for loops, corkscrews, wallrides, and compound inversions.

### 4.4 Deterministic subsystem streams

Separate layout, feature, elevation, quarter, surface, visual, and preset-resolution streams are much better than using Unity's global random state. They allow one category to change without intentionally randomizing every other category.

### 4.5 Existing semantic contracts

The feature-capability table already expresses the correct idea: transitions should reason about an element's entry/exit behavior, not only its C# enum value.

### 4.6 Exact feature result measurement

The builder can report what a feature actually did. This is essential for closure, compound elements, and future network/client verification.

---

## 5. Audit findings

### P0 — A bare seed is not a shareable deterministic recipe

The current seed export stores the base integer seed, while the generated result also depends on:

- all subsystem stream seeds;
- preset-resolution seed and resolved preset values;
- complete designer settings and feature rules;
- algorithm and content-catalog versions;
- maximum attempts, relaxation policy, and selection/scoring behavior;
- starting position and orientation;
- the successful attempt and chosen candidate;
- any settings or layout locks;
- the exact code version that interprets those values.

Therefore, the same integer can reproduce the same result only while the surrounding local setup and algorithm remain unchanged. It is not yet a safe player-shareable contract.

**Required correction:** introduce a versioned `GenerationRecipe` and a resulting `TrackResultManifest`. Keep the friendly integer seed as one field in the recipe, not as the complete identity of the track.

### P0 — Connector logic has multiple competing authorities

`ConnectorAnalyzer` performs real planning changes, while `TransitionResolver` performs more semantic boundary analysis but is currently reporting-only. This creates two partially overlapping definitions of a valid transition.

The hard-coded connector helpers also omit valid categories. For example:

- connector candidates list four straight types instead of consuming the existing straight-family definition;
- turn-sign logic recognizes only a subset of turning elements;
- orientation-feature detection omits generic rotational events and other orientation-changing types;
- feature detection omits some live feature families.

As new elements are added, these lists will drift further and produce incorrect connector assignment.

**Required correction:** make semantic capabilities plus realized entry/exit state the single authority. Section-type switches may remain only inside the individual builders or compatibility adapters.

### P0 — New features cannot safely scale while placement is “feature-first”

The planner currently has a useful corner-slot abstraction, but many features are still allocated by choosing a named feature in a usable gap. That approach becomes fragile with dozens of elements because the chosen feature may not satisfy the heading change, elevation change, footprint, speed, or exit orientation the route actually needs.

**Required correction:** plan a `TopologyDemand` first, then choose a compatible `FeatureRealization`.

### P1 — Transition contracts are descriptive but not fully consumed

`FeatureCapabilities` says whether a feature requires an upright entry, accepts curvature, owns recovery, or realizes a turn. Those constraints need to filter planning candidates before expensive geometry is built.

`TransitionResolver` should first run in shadow mode against existing decisions, then become the planning authority once parity and yield are proven.

### P1 — Exact feature exits are measured too late to guide every decision

`FeaturePlanResult` accurately reports built behavior, but the planner does not yet consume that information everywhere it would help closure and downstream placement.

**Required correction:** every realization must supply a deterministic preview result before final emission, and the final builder must verify that its actual exit matches that preview within tolerance.

### P1 — Curvature continuity and connector ownership are related but distinct

The current generator generally provides positional and tangent continuity. The remaining “curve → short straight → same-direction curve” issue is a curvature-flow problem, not a reason to delete every straight connector.

Some straights are intentional closure levers, recovery zones, pacing tools, or feature approaches. V2 should merge or reshape only connectors proven to be redundant, and keep explicit closure/recovery ownership visible.

### P1 — Inspector setup has duplicate or unclear authority

The generator and seed manager both expose configuration references even though generation resolves through the generator's configuration. This can make the visible setup disagree with the actual setup.

The Inspector also mixes everyday controls, experimental settings, diagnostics, materials, and deep tuning in one large surface.

**Required correction:** one configuration authority, one recipe panel, a small default workflow, and an Advanced/Diagnostics mode.

### P1 — Long-running tests are mixed with routine validation

The editor test suite is valuable, but explicit discovery/freezing tests can run for an hour or more. They must not be part of the normal change loop.

**Required correction:** tier the suite into compile, smoke, deterministic corpus, geometry regression, and explicit long-running discovery.

### P2 — Cross-version exactness needs an explicit policy

Even with deterministic random streams, changing planners, validators, scoring weights, candidate counts, or floating-point behavior can select a different candidate.

V2 must define whether a recipe is:

- **Strict:** exact algorithm/catalog version required;
- **Compatible:** allowed only across declared compatible versions;
- **Best effort:** regenerate using current rules without exact-hash promise.

For multiplayer or archival parity across versions, a compact canonical section plan may be needed as a fallback in addition to the recipe.

---

## 6. Connector and transition model for V2

### 6.1 One boundary authority

Replace the current overlapping decisions with one `TransitionPlanner` that consumes:

- previous element exit frame and exit state;
- next element required entry frame and entry state;
- semantic capabilities of both elements;
- available longitudinal distance;
- closure and recovery ownership;
- speed and intensity targets;
- width, banking, wall, pitch, roll-rate, and curvature limits.

It should produce one of four results:

1. `DirectWeld` — states already match within tolerance;
2. `AdaptiveTransition` — a blend interval can safely reconcile them;
3. `ExplicitRecovery` — a designed recovery section is required;
4. `Incompatible` — this realization cannot be placed here.

### 6.2 A connector is not generic spare road

Every connector must have an explicit owner and purpose:

- approach owned by the next feature;
- recovery owned by the previous feature;
- neighbor blend owned by both;
- closure lever owned by the closure solver;
- pacing straight owned by the macro rhythm plan.

This prevents accidental short straights and makes debugging understandable.

### 6.3 Connector acceptance rules

A connector is valid only if it satisfies all relevant limits:

- bank angle and bank-rate change;
- pitch and pitch-rate change;
- roll and roll-rate change;
- width and wall-height slope;
- centerline curvature and curvature-rate change;
- minimum recovery time at expected speed;
- feature-specific upright/inverted and curved-entry constraints;
- collider and mesh subdivision requirements.

### 6.4 Curvature-flow treatment

For same-direction curves separated by a short connector:

- classify the pair as one turn complex;
- preserve the connector when closure, recovery, or pacing needs it;
- otherwise allow a safe plan-time merge or a curvature-carry transition;
- do not mutate finished geometry as an unverified visual-only patch;
- keep the behavior behind a yield gate until the golden corpus proves it does not break generation.

---

## 7. Shareable Generation Recipe

### 7.1 `GenerationRecipeV1`

The recipe should contain at least:

```text
schemaVersion
generatorAlgorithmVersion
featureCatalogVersion
rulebookId + rulebookHash
masterSeed
allSubsystemSeeds
resolvedPresetSelections
canonicalResolvedSettings
featureRules
layoutLock + settingsLocks
startPosition + startRotation + scale
candidateSelectionPolicy
validationProfile
platformDeterminismProfile
expectedLayoutHash (optional on first creation)
baseRecipeHash (for edited variants)
recipeRevision
topologySlotOverrides
```

The serialized settings must be canonical: stable field order, explicit units, no locale-dependent numbers, no editor-only object instance IDs, and asset references by stable GUID/content hash.

### 7.2 `TrackResultManifest`

After generation, store:

```text
recipeHash
selectedAttemptIndex
selectedCandidateIndex
canonicalLayoutHash
orderedSemanticElementSequence
quantizedConnectionFrameChecksum
featureParameterChecksum
appliedOverrideChecksum
validationSummary
generationMetrics
```

The canonical layout hash should be based on quantized, mesh-independent planning data. It must not depend on object instance IDs or scene hierarchy order.

### 7.3 Client-side reproduction contract

The player flow should be:

1. receive/import a recipe;
2. verify supported schema, algorithm, catalog, and rulebook hashes;
3. generate locally;
4. calculate the canonical layout hash;
5. accept only if it matches the expected hash;
6. otherwise report a version mismatch or request a canonical-plan fallback.

**Important:** a friendly seed can remain what players copy and discuss, but the shared payload behind it must identify the complete recipe. A seed alone cannot guarantee the same track after generator rules change.

### 7.4 Base seed, exact recipe, and edited variants

Hand-editing a generated feature must not silently create an unrelated random seed.

V2 should preserve three related identities:

1. **Base seed** — the original procedural starting point;
2. **Base recipe** — the complete settings and rules used to interpret that seed;
3. **Recipe variant** — the base recipe plus deterministic topology-slot overrides.

Example:

```text
Base Seed: 741380678
Base Recipe Hash: 51C8-29A4
Variant Revision: 3

Overrides:
  Slot turn-012: Hairpin -> Hammerhead Turn [Locked]
  Slot roll-027: Vertical Loop -> Immelmann [Locked]
  Slot turn-041: Automatic -> Wide Turnaround [Unlocked Parameters]

Expected Final Layout Hash: 8F42-A19C
```

Sharing only the base seed requests the original automatic generation. Sharing the exact recipe/share code requests the customized result, including every handpicked edit and the expected final hash.

The share UI must label these actions unambiguously:

- `Copy Base Seed`;
- `Copy Base Recipe`;
- `Copy Exact Customized Track`;
- `Create Variant From Current Track`.

The imported recipe must never quietly discard unsupported overrides. It must either reproduce and verify them or report a clear incompatibility.

---

## 8. Topology-demand feature planning

### 8.1 Separate route need from visual realization

V2 should first describe what the route needs:

```text
TopologyDemand
  signedHeadingDelta        // 0, ±45, ±90, ±180, or bounded range
  elevationDeltaRange
  entryOrientation          // upright, inverted, or any
  requiredExitOrientation
  entryBank/Pitch/Curvature windows
  exitBank/Pitch/Curvature windows
  footprintEnvelope
  designSpeedRange
  intensityTarget
  recoveryBudget
  closureLeverPolicy
  allowedFeatureFamilies
```

It should then choose a realization that advertises compatible behavior:

```text
FeatureRealizationDescriptor
  semanticElementId
  realizationVersion
  supportedTopologyDemands
  parameterRanges
  entry/exit capabilities
  builder/preview recipe
  swept-volume estimator
  recovery ownership
  scoring tags
```

The selection order is:

1. filter incompatible realizations;
2. preview exact relative exit and footprint;
3. score compatible realizations against style, pacing, repetition, risk, and closure value;
4. select deterministically from the scored set;
5. fall back to an ordinary curve or straight when optional feature realization fails;
6. fail clearly when a required feature rule cannot be satisfied.

### 8.2 Example: a 180-degree route demand

A signed 180-degree heading demand should not automatically mean “hairpin.” Its compatible catalog could include:

| Realization | Route result | Typical use | Important constraints |
|---|---|---|---|
| Hairpin | ±180°, upright | compact, technical, low-speed | tight radius, strong braking/recovery |
| Wide Turnaround | ±180°, upright | fast, broad change of direction | large footprint, ordinary or overbanked support |
| Horseshoe | ±180°, upright | elevated, highly banked turnaround | high bank near the top, more height/footprint |
| Half Helix Turnaround | ±180° plus elevation | climbing or descending direction change | continuous elevation budget and larger footprint |
| Hammerhead Turn | approximately ±180°, upright exit | dramatic rise-and-turn feature | entry speed, vertical clearance, controlled banking |
| Immelmann | ±180°, upright exit after inversion | inversion plus direction reversal | upright/roll constraints and recovery |
| Dive Loop | ±180°, upright exit after inversion | downward inversion and reversal | height, approach orientation, exit speed |
| Top Hat | approximately ±180° with major elevation | signature vertical feature | very large height, speed, and landing/recovery budget |

The planner chooses among these only after checking the available footprint, elevation, entry state, intended speed, style, repetition, and closure needs.

### 8.3 Stable topology-slot identity

Manual replacement requires a durable identity for the **route demand**, not for a temporary GameObject or generated mesh section.

Each planned demand receives a stable `TopologySlotId` derived from canonical planning information and stored in the recipe. The ID must survive mesh rebuilding, connector subdivision, object recreation, and editor reloads.

A slot record should contain:

```text
TopologySlotId
canonicalOrder
demandType
signedHeadingDelta
entryAnchorId
exitAnchorId
originalRealization
currentRealization
overrideState
parameterOverrides
localReplanPolicy
```

Generated hierarchy names and section array indices may display the slot ID, but they must not be the authoritative identity because local replanning can change section counts.

### 8.4 Interactive feature replacement

The Scene view and Inspector should support both selection methods:

- click a generated feature or its topology-slot gizmo;
- enter/search a stable topology-slot ID.

For the selected slot, the Inspector shows:

- the route demand and required entry/exit behavior;
- the current realization;
- compatible alternatives;
- alternatives that are currently incompatible and the exact reason;
- footprint, height, approach, recovery, expected speed, and intensity comparison;
- whether surrounding layout, connectors, or closure need adjustment.

Available actions:

- `Reroll Compatible Realization`;
- `Replace With...`;
- `Preview Replacement`;
- `Apply and Replan Neighborhood`;
- `Lock Realization`;
- `Lock Realization and Parameters`;
- `Return Slot to Automatic`;
- `Revert Variant Changes`.

### 8.5 Safe local replanning

A Hammerhead cannot be treated as a cosmetic mesh swap for a Hairpin. It can require different length, height, footprint, banking, approach, recovery, and exit position.

Replacement therefore follows a transactional local-replan process:

1. preserve the original accepted track and recipe;
2. resolve the requested realization against the selected topology demand;
3. compute its exact preview exit and swept volume;
4. determine the smallest valid replan neighborhood;
5. treat the unaffected topology anchors outside that neighborhood as locked boundary conditions;
6. rebuild the feature and its owned approach/recovery connectors;
7. allow closure-lever adjustment only within the declared policy;
8. run clearance, transition, geometry, collider, and driveability validators;
9. accept transactionally and write the override only on success;
10. otherwise preserve the original track and report why the edit cannot fit.

Local replanning should use an escalation ladder:

```text
Level 0: Feature only; entry and exit frames already match
Level 1: Feature + owned approach/recovery connectors
Level 2: Feature + adjacent turn complex or pacing interval
Level 3: Quarter-local replan with outer anchors locked
Level 4: Whole-layout constrained replan, only after explicit approval
```

The tool must show the required level before applying the edit. It must never unexpectedly reshape the entire track after a request that appears local.

### 8.6 Replacement compatibility and failure messages

The compatible list should be computed, not manually curated per screen. It must use the same realization catalog and transition constraints as normal generation.

An unavailable alternative remains visible when that information helps the designer, with reasons such as:

```text
Hammerhead Turn unavailable for turn-012:
- needs 82 m more vertical clearance;
- approach is 34 m shorter than the minimum at the predicted speed;
- Level 2 replanning is disabled by a locked neighboring feature;
- required exit frame cannot reconnect within tolerance.
```

The designer may deliberately unlock the conflicting neighbor or allow a broader replan, but the generator must not bypass safety rules merely because the feature was manually requested.

### 8.7 Naming note

“Half Helix Turnaround” is a useful game-specific name for a 180-degree rising or descending half-helix. A conventional roller-coaster helix is usually described as a spiral exceeding one full turn, so the game should not rely on “180° helix” as an industry-standard label.

### 8.8 Definition-driven features and reusable geometry primitives (locked direction)

Every feature realization must have its own versioned definition instead of being added as another
hard-coded branch inside the topology planner or generator. The central generator asks for topology,
loads compatible definitions from a catalog, resolves one definition into concrete geometry, and then
uses the existing authoritative builders and validators. A definition may initially reference a
code-backed solver for genuinely specialized math, but identity, parameters, capabilities, phase
composition, approach/recovery ownership and recipe versioning belong to the definition.

The model has three layers:

1. **Feature Definition** — one asset/file per named realization, with stable ID/version, designer
   metadata, topology capabilities, authored parameter ranges, geometry-phase recipe and safety
   declarations;
2. **Reusable Geometry Primitives** — generic straight, recovery, eased turn, half-loop,
   half-corkscrew, vertical arc, road-roll, air-gap, landing, width/cross-section and pipe phases;
3. **Resolved Feature Instance** — the exact selected lengths, radii, directions, handedness, phase
   splits and exits emitted as `TrackMacroSectionDefinition` data for one generated track.

Every Feature Definition has these common spatial parameters at minimum:

```text
Length          // core maneuver only; excludes entry and recovery
EntryLength     // owned preparation/transition capacity before the core
RecoveryLength  // owned stabilization/transition capacity after the core

TotalOccupiedLength = EntryLength + Length + RecoveryLength
```

Each parameter supports Minimum, Preferred and Maximum values plus a `MayAutoExpand`/resolution
policy where appropriate. `Length` is a budget and resolved output, not an independent value that may
contradict radius, roll rate or curvature. For a loop, the selected radii determine the exact legal
core length inside the authored Length range. For a corkscrew, barrel radius, rotation, roll-rate and
craft-retention constraints jointly resolve the exact legal Length. The definition may add unique
parameters such as radius, height, handedness, rotation units, yaw separation, phase split or airtime.
Global driveability ceilings remain inherited from the rulebook and cannot be weakened by a feature
asset.

Illustrative Full Loop definition contract:

```text
FullLoopDefinition
  Id / Version / Family / TopologyRole
  Length              { Min, Preferred, Max, Resolution = DerivedFromRadii }
  EntryLength         { Min, Preferred, Max, MayAutoExpand = true }
  RecoveryLength      { Min, Preferred, Max, MayAutoExpand = true }
  FirstHalfRadius     { Min, Preferred, Max }
  SecondHalfRadius    { Min, Preferred, Max }
  VerticalRotationUnits = 4 × 90°
  LimbSeparationYaw  { Min, Preferred, Max }
  CurvatureEase      { Min, Preferred, Max }
  Phases
    HalfLoop          Upright → Inverted, uses FirstHalfRadius
    HalfLoop          Inverted → Upright, uses SecondHalfRadius, continuous blend
  Exit                same heading, upright, level, zero net elevation
  Clearance / speed / load constraints inherited from definition + rulebook
```

Illustrative Inline Corkscrew definition contract:

```text
InlineCorkscrewDefinition
  Id / Version / Family / TopologyRole
  Length              { Min, Preferred, Max, Resolution = PhysicsAndRollRateSolved }
  EntryLength         { Min, Preferred, Max, MayAutoExpand = true }
  RecoveryLength      { Min, Preferred, Max, MayAutoExpand = true }
  FirstHalfRadius     { Min, Preferred, Max }
  SecondHalfRadius    { Min, Preferred, Max }
  RoadRollUnits       = 4 × 90°
  Handedness          = Left | Right | Automatic
  PhaseSplit          { Min, Preferred, Max }
  BarrelClearanceMultiplier
  Phases
    HalfCorkscrew     Upright → Inverted
    HalfCorkscrew     Inverted → Upright, same handedness, continuous blend
  Exit                same heading, upright, level, one accumulated full roll
  Clearance / roll-rate / contact-load constraints inherited from definition + rulebook
```

Full Loop and Corkscrew are therefore themselves compositions of reusable half primitives. Batwing,
Cobra Roll, Sea Serpent and other compound elements reuse the same half-loop and half-corkscrew
primitives in definition-authored sequences. Internal primitive boundaries must never receive generic
connectors or independently reset curvature/roll. The complete compound is length-budgeted, solved,
previewed and validated atomically as one continuous feature with one entry/exit contract.

Designer counting remains at the feature-definition level: a Batwing is one Batwing for Min/Max,
weighting and repetition. Diagnostics may report its internal primitive inventory, but two half-loops
and two half-corkscrews must not accidentally count as four separately placed features.

Exact Recipe stores the stable definition ID, definition version/content hash, ordered primitive
sequence and every resolved parameter. Replaying an older recipe must either use the compatible
definition version and reproduce its canonical hash or report an explicit catalog incompatibility;
editing a project asset must never silently change an already shared track.

---

## 9. Future feature catalog

The linked roller-coaster-element list is useful as a shape and naming taxonomy, not as a physics specification. Every element still needs Slingshot-specific high-speed driveability, collider, wall, width, and recovery validation.

### Tier A — Low architectural risk

These can reuse much of the current elevation, roll, and curve machinery:

- Wide Turnaround;
- Horseshoe;
- Half Helix Turnaround;
- Camelback;
- Inline Twist;
- Heartline Roll;
- Zero-G Roll;
- Cutback;
- Sidewinder.

### Tier B — Moderate compound risk

- Hammerhead Turn;
- Immelmann;
- Dive Loop;
- Top Hat;
- Wave Turn;
- Inclined Loop;
- Zero-G Stall;
- Banana Roll;
- Dive Drop;
- Raven Turn.

### Tier C — High compound/clearance risk

- Cobra Roll;
- Sea Serpent;
- Batwing;
- Butterfly;
- Bowtie variant;
- Norwegian Loop;
- Pretzel Loop;
- Twisted Horseshoe Roll;
- Pretzel Knot;
- Bent Cuban Eight;
- Finnish Loop.

Tier C elements should be composed from proven phase primitives only after exact-exit preview, swept-volume validation, and compound recovery rules are stable.

### Shape behavior references

The reference taxonomy describes, among other examples:

- a horseshoe as a highly banked 180-degree turnaround;
- an Immelmann as a half-loop followed by a half-twist, exiting in the opposite direction;
- a dive loop as the reverse order of an Immelmann;
- a sidewinder as a half-loop followed by a half-corkscrew, commonly exiting roughly perpendicular to entry;
- a cobra roll as two inversions that exit in the opposite direction;
- a sea serpent as two inversions that exit in the same direction;
- a heartline roll as a straight-heading roll around the rider's approximate centerline;
- an inline twist as a roll where track elevation remains broadly constant.

Source: [Wikipedia — List of roller coaster elements](https://en.wikipedia.org/wiki/List_of_roller_coaster_elements#Inverting_elements), consulted 2026-08-23.

---

## 10. Implementation stages

### Stage 0 — Freeze the trusted baseline

**Goal:** preserve the generator behavior that already drives well.

Deliverables:

- record the current algorithm and catalog version;
- freeze the current golden-seed corpus and expected canonical summaries;
- capture generation yield, median and P95 time, section counts, connector counts, and rejection causes;
- separate experimental curvature-flow changes from the trusted baseline;
- define a short smoke suite and mark discovery/freezing tests explicit-only;
- document the single supported scene/prefab setup.

Exit gate:

- smoke suite completes in minutes, not an hour;
- all baseline seeds have recorded outcomes;
- no implementation stage proceeds without comparable metrics.

### Stage 1 — Generation Recipe and deterministic replay

**Goal:** make track identity complete and shareable.

Deliverables:

- `GenerationRecipeV1` and canonical serializer;
- `TrackResultManifest` and quantized canonical layout hash;
- strict/compatible/best-effort replay modes;
- recipe export, import, copy, and validation UI;
- remove or synchronize duplicate configuration ownership;
- audit all generation randomness and prohibit Unity global random inside generation;
- deterministic replay tests across repeated runs in the same supported build.

Exit gate:

- every golden recipe reproduces the same canonical hash across at least 20 repeated runs on the supported build;
- changing unrelated visual randomness does not change topology hash;
- incompatible algorithm/catalog versions fail clearly instead of silently generating a different track.

### Stage 2 — Connector authority consolidation

**Goal:** fix connector assignment without destabilizing geometry.

Deliverables:

- semantic family/capability queries replace hard-coded feature lists;
- one `TransitionPlanner` result type and one purpose/owner per connector;
- shadow comparison between current `ConnectorAnalyzer` decisions and new semantic decisions;
- diagnostics for entry/exit bank, pitch, roll, width, wall height, curvature, available blend length, and rejection reason;
- plan-time enforcement only after shadow parity is understood;
- explicit preservation of closure and recovery straights.

Exit gate:

- no unidentified connector purpose;
- no connector shorter than its computed transition requirement;
- no regression in baseline yield beyond the agreed tolerance;
- visual and drive tests confirm no new snaps, wall waves, or recovery loss.

### Stage 3 — Topology demand and realization catalog

**Goal:** let the planner ask for route behavior and choose among compatible features.

Deliverables:

- `TopologyDemand`;
- versioned `FeatureDefinition` asset/file schema and catalog, superseding feature-specific central
  planner switch growth;
- reusable geometry-primitive schema/compiler, beginning with Half Loop and Half Corkscrew;
- common mandatory `Length`, `EntryLength` and `RecoveryLength` parameter contracts;
- definition-owned unique parameters and code-backed solver reference where specialized math remains
  necessary;
- migrate existing Loop and Corkscrew through definitions without changing accepted geometry;
- deterministic filter → preview → score → select process;
- exact-exit preview contract verified against final builder output;
- repetition, style, intensity, footprint, elevation, speed, and closure scoring;
- ordinary curve/straight fallbacks;
- Inspector preview of compatible realizations for each feature rule.

Implementation checkpoint (2026-08-24): the schema, deterministic JSON catalog, reusable Half Loop /
Half Corkscrew composition, Loop and Inline Corkscrew compiler adapters, generated-section identity,
Exact Recipe definition hashes, debug-report catalog dump and focused definition tests are implemented.
Wide Turnaround is also migrated from its prototype planner branch into a versioned definition using
the generic Eased Turn compiler path. The code-backed adapters deliberately preserve accepted
geometry/RNG behavior. Unity definition-gate refresh, remaining realization migration and the generic
compound primitive compiler are still pending.

Exit gate:

- existing hairpins, curves, wallrides, corkscrews, loops, and jumps can be selected through their own
  definitions without changing accepted baseline geometry;
- Full Loop composes two Half Loop primitives and Inline Corkscrew composes two Half Corkscrew
  primitives while keeping continuous internal curvature/orientation state;
- definitions expose correct total occupied length and cannot author a Length/radius/roll-rate
  combination the compiler cannot solve legally;
- exact preview and final exit agree within tolerance;
- optional feature failure falls back cleanly; required feature failure explains why.

### Stage 4 — Interactive replacement and recipe variants

**Goal:** let the designer replace a generated feature safely and share the exact edited track.

Deliverables:

- stable `TopologySlotId` assignment and visualization;
- Scene selection and Inspector slot search;
- compatible-alternative browser with comparison data;
- `Reroll`, `Preview`, `Replace`, `Lock`, `Automatic`, and `Revert` actions;
- transactional local-replan escalation levels;
- locked outer anchors and explicit replan scope preview;
- topology-slot override serialization in `GenerationRecipeV1`;
- base-seed, base-recipe, and exact-variant share actions;
- variant revision history sufficient to undo/revert applied edits;
- final canonical-hash verification after every accepted replacement.

Implementation checkpoint (2026-08-26): Track Editor replacement requests now carry both the
finished-route slot ID and a strict structural demand anchor. This resolves the same quarter/road/
role/heading/original-realization demand when connector or closure insertion shifts its earlier
route-order token, without weakening any geometric validator. Successful application normalizes the
request to the rebuilt finished-route ID; old recipes without the new anchor retain legacy hash
compatibility.
Transactional replan-scope escalation now includes the first designer-facing Area of Impact level:
independent Backward/Before and Forward/After semantic-feature reach within the selected quarter
route. The planner consumes only stable identities explicitly captured and confirmed by the editor;
count-only or overlapping imported windows fail instead of guessing.

Designer-authoring checkpoint (2026-08-26): Track Editor no longer treats an edit as a fresh random
generation search. Every accepted result records its absolute deterministic attempt index; an edited
variant rebuilds that exact visible design, preserving its route and selected topology demand instead
of spending hundreds of attempts on unrelated layouts. Procedural target/max lap length becomes an
authoring preference during this focused rebuild, with generous numerical headroom and larger safe
closure radii/straight authority. Minimum radius, slope, roll/pitch rate, weld continuity, clearance,
self-intersection and mesh budgets remain hard validators. The authoring baseline attempt and lap
length are versioned exact-recipe data, so Save/Load/Exact Replay restores the same edit policy.

The first explicit **Area of Impact** selector is implemented. Backward/Before and Forward/After are
independent feature-count controls in track travel direction. Zero protects nearby semantic features;
non-zero reach highlights the exact window in the Scene, lists every affected realization, exposes
compatible replacement suggestions and offers an Edit action. Build & Apply then requires
**Override Features Anyway** before the window can remove those features and rebuild the intervening
road. The selector is creative authority—not a safety tolerance slider. Future expansion may add
cross-quarter/whole-track reach and simultaneous per-feature replacement choices on top of the same
stable impact-member recipe data.

The first focused authoring run exposed a 94.1m vertical residual after the accepted route's ordinary
elevation carriers were exhausted. Authoring mode now opens unused, level-boundary recovery straights
as a final vertical-balance reserve. It distributes only the remaining displacement, uses the same
conservative eased-grade capacity as normal major carriers, and does not relax maximum climb/drop
angles or weld validation.

The first runtime failure report also proved that replacement ownership must include a superseded
adjacent recovery: a definition-native replacement that emits recovery now consumes the old feature's
immediately following recovery inside `OwnedConnectors`, instead of stacking two locked recoveries and
wasting closure reserve. Report storage retains one representative detail per failure mode so rare
closure evidence survives the bounded chronological tail.

Original-feature restoration is also distinct from replacement authoring. When a designer chooses
the recorded original realization after an accepted replacement and no neighboring impact is
requested, Track Editor removes the override and regenerates the deterministic procedural recipe.
This restores the original sampled parameters and surrounding recovery/closure as one transaction,
instead of emitting a new generic realization that can collide with the already-authored route.

Exit gate:

- replacing a compatible realization reproduces identically after scene reload and recipe import;
- a failed edit leaves the accepted track and recipe unchanged;
- local edits do not alter topology outside their declared replan neighborhood;
- exact customized share codes reproduce every override and final layout hash;
- unsupported or conflicting overrides fail clearly instead of reverting to automatic choices.

### Stage 5 — First new turn realizations

**Goal:** prove that one topology demand can choose multiple shapes.

**Prerequisite:** complete the definition/primitive foundation in §8.8 and migrate the existing Loop,
Corkscrew and Wide Turnaround paths before adding more realization-specific generator branches.

Recommended first set:

1. Wide Turnaround;
2. Horseshoe;
3. Half Helix Turnaround;
4. Hammerhead Turn.

**Implementation checkpoint (2026-08-24):** Wide Turnaround is integrated as the first additive,
designer-opt-in realization. A 150–180° topology slot offers a broad-radius alternative through the
same authoritative corner emitter and transactional Track Editor pipeline used by existing
replacements. Its stable, versioned definition owns heading range, radius multiplier, core section,
banking choice, common length contracts, recovery and primitive identity; the central planner contains
no Wide-specific geometry constants. It retains the accepted hairpin count/recovery behavior and exact
signed exit heading while using a measurably larger footprint. Automatic seed generation remains
unchanged. Focused catalog, definition identity, signed-exit, parity and footprint coverage is included.
Horseshoe begins only after the refreshed definition gate is green.

**Implementation checkpoint (2026-08-25):** the complete prerequisite gate passed 42/42 in Unity.
Horseshoe is now implemented as a second additive, designer-opt-in definition-native realization. It
uses a reusable Elevated Eased Turn primitive: definition-owned radius, crest height and bank
multiplier resolve into one continuous 0→crest→0 reversal with level/elevation-neutral exact exit,
owned recovery and measured vertical curvature. Generic catalog capability discovery makes it
available without a Horseshoe-specific planner branch. Its C2 crest carrier returns elevation, slope
and vertical curvature to zero at both owned boundaries, while ring-level distance is normalized to
the definition compiler's deterministic driving-line budget. Definition-native turn compilation also
propagates exact missing/disabled/malformed/solver/heading-window failure reasons without mutating the
caller's section list. Seven focused cases cover definition validity, both signed exits, global
vertical-load/rate limits, generic topology-candidate construction, the crest boundary contract,
definition/build length agreement and explicit failure diagnostics. Unity refresh and the resulting
49-case gate plus one visual Track Editor application remain the checkpoint before Half Helix
Turnaround.

**Implementation checkpoint (2026-08-30):** Half Helix Turnaround is implemented as the third
definition-native reversal and remains disabled by default. A dedicated `HalfHelixTurn` primitive
builds a genuine partial helical centerline rather than disguising an elevated ordinary turn or
truncating the heading-neutral Spiral. Its asset exposes an independent 60–130 m climb range alongside
heading, radius, banking, entry and recovery contracts. The compiler produces an exact signed reversal,
smooth climb and level exit at the new elevation; final metrics/count validation distinguish it from a
full Spiral, and recovery integration cannot rewrite it into a different revolution count. Procedural
allocation, rule controls, recipe migration, rhythm, Track Editor compatibility and focused signed-exit
and replacement tests are integrated. Runtime and Editor assemblies compile; the Unity focused gate,
bounded feature-required sweep and visual high-speed pass remain before preset admission.

All four target the user's 180-degree example and exercise footprint, banking, elevation, and speed constraints without immediately requiring the most complex compound inversions.

Exit gate:

- a ±180 demand can deterministically choose any enabled compatible realization;
- disabled or incompatible realizations never appear;
- closure consumes the exact chosen exit;
- each element has high-speed play validation and safe recovery behavior.

### Stage 6 — Simple elevation and roll features

Recommended order:

- Camelback;
- Inline Twist, Heartline Roll, Zero-G Roll;
- Cutback and Sidewinder;
- Immelmann and Dive Loop.

This order extends the reusable primitive vocabulary established in §8.8 before more complex
compounds.

### Stage 7 — Compound feature composition

Add compound elements only from verified primitives:

- Cobra Roll and Sea Serpent;
- Batwing/Butterfly/Bowtie family;
- Twisted Horseshoe Roll;
- Norwegian and Pretzel families.

Each compound is authored as a Feature Definition over reusable primitives, compiled and validated
atomically, and stores its phase sequence, resolved parameters, exact relative exit, swept-volume
envelope, and recovery contract as versioned recipe data.

### Stage 8 — Client generation and sharing

**Goal:** safely generate identical supported tracks at the client.

Deliverables:

- compact recipe/share code transport;
- supported-version handshake;
- local generation progress and cancellation;
- canonical-hash verification;
- mismatch reporting;
- optional canonical macro-plan fallback for cross-version sessions;
- security limits on recipe size, attempts, track length, and feature counts.

---

## 11. Inspector design for V2

### Default view

- Generator profile/style
- Track size and target pacing
- Seed / Recipe ID
- Generate New Track
- Regenerate Exact Recipe
- Import / Export Recipe
- Current result status and canonical hash
- Base seed, recipe revision, and customized-change count

### Feature palette

Group features by route behavior, not by implementation class:

- **Turnarounds:** hairpin, wide turnaround, horseshoe, half helix, hammerhead;
- **Transfers:** S-curve, chicane, cutback, sidewinder;
- **Elevation:** camelback, speed hill, double dip, top hat;
- **Rolls and inversions:** corkscrew, inline twist, heartline roll, zero-G roll, loop;
- **Compounds:** Immelmann, dive loop, cobra roll, batwing, sea serpent, pretzel family;
- **Special surfaces:** wallride, full pipe, bridge, tunnel, air gap.

Each rule should expose:

- enabled/required;
- minimum and maximum count;
- selection weight;
- compatible topology demands;
- minimum spacing;
- intensity range;
- short incompatibility explanation.

### Selected topology slot

When a generated feature is selected, show a focused edit card:

```text
TOPOLOGY SLOT      turn-012
ROUTE DEMAND       180 degrees left, upright exit
REALIZATION        Hairpin
STATUS             Automatic

[Reroll Compatible] [Replace With...] [Lock]
```

The replacement browser should compare only meaningful design information: route result, footprint, elevation, recommended speed, intensity, approach/recovery demand, and required replan scope.

After edits, the recipe card should show:

```text
BASE SEED          741380678
CUSTOM CHANGES     3
VARIANT REVISION   3
FINAL HASH         8F42-A19C
```

### Advanced view

- resolved settings;
- stream seeds;
- recipe versions and hashes;
- closure and transition tolerances;
- validator settings;
- generation metrics;
- diagnostics and long-running test tools.

The normal workflow should not expose temporary experiment controls.

---

## 12. Validation and performance gates

### Determinism

- canonical recipe round-trip is byte-stable;
- same recipe + supported algorithm/catalog produces the same quantized layout hash;
- topology hash ignores visual-only randomness;
- changing an individual unlocked stream changes only its intended category where practical;
- version mismatch is explicit.
- stable topology-slot overrides survive rebuild and reload;
- exact variants replay the same replacements and final hash.

### Generation health

- track success rate measured per profile and size;
- median and P95 generation time tracked;
- rejection reasons aggregated by stage;
- candidate/time budgets remain bounded;
- cancellation returns control safely;
- the previous valid track survives failure.

### Connector health

- all connector purposes identified;
- boundary deltas within declared limits;
- no required recovery accidentally absorbed;
- no short same-direction bridge unless validated as a turn complex;
- no abrupt width or wall-height slope;
- no unsupported orientation transition.

### Feature health

- preview exit matches final exit;
- swept-volume and clearance validation;
- safe wall and flat-center geometry;
- collider subdivision appropriate for rotation and speed;
- approach and recovery requirements satisfied;
- high-speed hovercraft play test completed.
- failed replacement is transactional and preserves the previous accepted result;
- accepted replacement changes only its declared replan neighborhood;
- locked realizations cannot be silently substituted.

### Test tiers

1. **Compile gate:** every change.
2. **Smoke tests:** core seed, connector, recipe, and one generation per main profile; target under two minutes.
3. **Golden corpus:** deterministic and geometry regressions; target under ten minutes.
4. **Extended corpus:** larger statistical yield/performance run; scheduled or manual.
5. **Discovery/freeze:** explicit-only, never part of routine iteration.

---

## 13. Risk controls

- Do not rewrite the working generator.
- Do not add many new elements before recipes and connectors are stable.
- Do not make advisory transition logic authoritative in one unmeasured jump.
- Do not promise cross-version equality from an integer seed.
- Do not remove straights solely because they look like connectors; closure and recovery ownership must be checked.
- Do not let new feature enums create new hard-coded switch lists throughout the codebase.
- Do not run hour-long corpus discovery during normal development.
- Do not accept visually correct geometry without collider and high-speed drive validation.

---

## 14. Recommended immediate work order

The first implementation sequence should be:

1. baseline metrics and test-tier split;
2. `GenerationRecipeV1` schema and canonical hash specification;
3. recipe round-trip and same-build deterministic replay;
4. connector classification audit diagnostics;
5. replace hard-coded connector family checks with semantic queries;
6. shadow-mode unified transition decisions;
7. enforce the unified planner behind a feature flag and yield gate;
8. generalize current corner slots into `TopologyDemand`;
9. register existing features in the realization catalog;
10. implement stable topology-slot selection, compatible reroll, and transactional local replacement;
11. persist replacements as exact recipe variants and verify their final hashes;
12. prove a 180-degree demand with Wide Turnaround, Horseshoe, Half Helix Turnaround, and Hammerhead;
13. expand the feature library in controlled tiers;
14. add client recipe transport and hash verification.

This ordering stabilizes identity and boundaries before multiplying the number of elements that depend on them.

---

## 15. Definition of V2 success

Track Generator V2 is ready when:

- its existing tracks remain at least as driveable as the current baseline;
- connectors are selected from real entry/exit requirements and have explicit ownership;
- a complete recipe reproduces the same supported track locally and at the client;
- the result is verified by a canonical layout hash;
- topology demands can choose among multiple compatible realizations;
- designers can replace, lock, reroll, and revert individual realizations with a visible and bounded replan scope;
- customized tracks keep their base seed while sharing every edit through an exact recipe variant;
- adding a feature requires one catalog entry, one preview/builder recipe, and focused tests—not edits to many unrelated switch statements;
- the Inspector is simple for everyday generation and deep only when requested;
- generation yield and time remain predictable;
- new feature expansion no longer threatens the stability of the core generator.

The central V2 principle is:

> **Plan the route behavior first, choose the feature second, and verify the exact result before accepting the track.**
