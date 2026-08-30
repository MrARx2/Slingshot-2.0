# Track Generator V2 — Implementation Status

Last updated: 2026-08-29

## Current state

Track Generator V2 is an integrated evolution of the accepted generator, not a rewrite. Exact Recipe,
semantic topology slots, post-generation Track Editor replacements, the redesigned primary Inspector,
feature amount controls and vertical generation profiles are implemented. The accepted generation
pipeline, connection frames, validation and deterministic seed streams remain authoritative.

Persistent visual materials and a bounded stability-audit workflow are now integrated as well. These
add operational safety around the accepted generator; they do not alter route planning or driveability.

## Proven workflow

- **Exact Recipe:** captures seed streams, complete designer settings, settings/layout locks, content
  identities, candidate policy, root transform and normalized topology overrides.
- **Exact Replay:** performs bounded deterministic regeneration and verifies the canonical layout hash
  before replacing the current valid track.
- **Track Editor:** choose a stable semantic section, preview an alternative at its actual route
  location, choose independent backward/forward Area of Impact reach, then Build & Apply through the
  authoritative whole-route pipeline.
- **Automatic route repair:** edited features rebuild their connectors and closure automatically. The
  live track swaps only after clearance, closure and final validation succeed.
- **Transactional safety:** failed edits keep the previous accepted track and restore the complete
  previous recipe/Inspector state.
- **Edited recipe identity:** successful hand-authored choices become normalized topology overrides.
  The base-seed identity remains stable while the edited recipe receives its own deterministic variant
  identity.

The designer manually verified the complete path: generate → replace a feature in Track Editor → save
recipe → delete generated mesh → load recipe → Exact Replay. The same edited layout, including the
Track Editor substitution, was restored.

## Deterministic and topology foundations

- Base-recipe and exact-variant SHA-256 identities.
- Canonical layout hashing over macro geometry, connection frames, jump/rotation state, connector
  metadata, boundaries and dual-road topology. Subdivision density is intentionally excluded.
- Stable topology-slot identity based on quarter, route, semantic order, topology role and demanded
  heading change—not GameObjects, mesh, connector subdivision or realization name.
- Semantic-neighbor lookup remains on the same road and skips connectors/recovery sections.
- Conservative compatibility filtering plus authoritative local candidate construction and owned
  connector solving.
- Connector shadow diagnostics remain read-only; the existing accepted connector analyzer is still the
  default generation authority.
- Focused `V2FastGate` coverage for recipe identity/replay, connector shadow decisions, topology slots,
  compatibility, candidate builders, geometry probes, replacement requests, rollback and persistence.

## Designer-facing Inspector

- Primary Track Profile controls: Style, Elevation, Difficulty and Size.
- Large Generate New Track action, compact same-seed/setup actions.
- Feature Amounts with direct Min/Max rule editing and explicit failure semantics.
- Exact Recipe Save, Load and full-width Exact Replay actions.
- Dedicated child `TrackEditor` GameObject for post-generation topology work.
- Advanced regeneration, diagnostics, materials and retained settings stay reachable without competing
  with the daily workflow.
- Export Debug Report remains available during development.

## Vertical Profiles (new)

- Serialized profiles: **Subtle / Balanced / Extreme**; Balanced is value `0` and the compatibility
  default for existing scenes, presets and recipes.
- Balanced reproduces the historical elevation-planning constants exactly.
- Profiles steer only design intent: target amplitude, major carrier count, preferred sustained-grade
  length and level-recovery rhythm.
- Max climb/drop angles, curvature, clearance, closure and every other safety ceiling remain owned by
  the rulebook and are never relaxed by a profile.
- Exact Recipe persists the selected profile.
- Exported reports include resolved vertical intent and measured Vertical Silhouette metrics: elevation
  range, planned major carrier count and longest sustained grade.
- Runtime and editor assemblies compile with zero C# errors. Focused policy, compatibility fallback,
  clamping, recipe round-trip and silhouette tests are included.

## Persistent Material Contract (new)

- A `TrackMaterialSet` project asset owns every generated visual role: road surface, wall side, guide
  marking, wall marker, boost surface, race-gate fallback, start/finish, start pillars and checkpoints.
- The Track Generator Inspector now shows a clear Ready/Missing status and can create or repair the
  default persistent palette with one action. Repair creates only missing assets/links and preserves
  existing role assignments plus every authored color, emission, texture and render setting.
- Builders consume one resolved material contract. Legacy Inspector fields remain migration fallbacks,
  but generated systems no longer need to invent scene-only materials.
- Track construction validates required roles before committing a replacement. A missing road, wall,
  guide, start-line or checkpoint material fails visibly instead of producing purple geometry later.
- Cached-preview restoration reapplies road, guide and race-course materials.
- Export Debug Report now includes a `MATERIAL CONTRACT` block with the resolved asset names and any
  missing roles, so visual regressions can be diagnosed from the same report as geometry failures.

## Bounded Stability Audit (new)

- `Track > V2 > Bounded Stability Sweep` runs deterministic strict-planning checks without building
  meshes or modifying the scene.
- The audit is intentionally bounded to 1–20 seeds and a 15–300 second scheduling budget, supports
  cancellation between seeds and can stop on the first failure.
- Each run writes a timestamped report under `Library/SlingshotDiagnostics` with per-seed timing,
  attempts, candidate counts, pass/fail/error status and an aggregated failure distribution.
- This is a manual development audit rather than part of the ordinary focused test gate, preventing
  another accidental hour-long Test Runner session.

## Definition-driven Feature Architecture (foundation slice implemented)

- The V2 plan now requires one versioned Feature Definition per realization instead of adding further
  feature-specific branches to the generator.
- Every definition owns at least core `Length`, `EntryLength` and `RecoveryLength`; total occupied
  length is their sum. Unique ranges such as radius, handedness, rotation units and phase split remain
  definition-specific, while global safety ceilings stay in the rulebook.
- Full Loop now composes two reusable Half Loop primitives; Inline Corkscrew now composes two reusable
  Half Corkscrew primitives. Compound definitions such as Batwing reuse those primitives and are
  solved atomically without generic connectors at internal phase boundaries.
- The source-controlled definitions are `vertical-loop-v1.json` and `inline-corkscrew-v1.json`, loaded
  by a deterministic runtime catalog. Malformed and duplicate IDs fail explicitly.
- Both single-feature pattern paths now enter the definition compiler, which preserves their proven
  geometry solvers and random draw order while taking authored radius, unit, yaw, phase-split and
  corkscrew physics limits from definition data.
- Generated sections store definition ID/version/content hash, primitive sequence and resolved entry /
  recovery budgets. Exact Recipe stores the catalog identities and strict replay rejects content drift;
  pre-definition recipes remain readable through the existing catalog-version path.
- Full debug reports now include the loaded catalog, common length budgets and primitive sequences.

## Individual Feature Definition migration (complete for the current catalog)

- Every current serialized `TrackPatternType` now resolves to exactly one enabled, versioned JSON
  asset under `Assets/Resources/TrackFeatureDefinitions`; the current coverage contract is **29/29**.
- Ordinary Curve and Directional Corkscrew also have individual semantic definition assets even
  though they are selectable realizations rather than standalone procedural pattern enums. The
  runtime catalog therefore contains **31 individual assets** in total.
- Legacy builders retain their accepted geometry while their emitted sections are stamped with the
  exact definition stable ID, version, content hash, primitive sequence and entry/recovery budgets.
  Specialized definition-native solvers keep their own more precise stamps.
- The Track Generator Inspector reports definition coverage and links directly to
  `Track > V2 > Feature Definitions`. The window provides typed parameter editing, selected-asset
  validation, save/version guidance and a synchronous **Validate All** import/audit.
- Coverage tests require every serialized designer pattern to have one matching semantic asset and
  protect Ordinary Curve plus Directional Corkscrew as selectable non-pattern definitions.
- Adding new local definition assets remains compatible with older Exact Recipes: strict replay
  validates the identities captured by that recipe, while unrelated later additions are ignored.

## Additive definition-native catalog wave (opt-in stability stage)

- **Camelback** is an individual asset backed by the reusable C² vertical bump compiler. It exits
  at its exact entry altitude and heading with zero authored end slope.
- Their compiler preflights the built rings against climb/drop angle, curvature-induced load and
  vertical-curvature-rate limits with headroom. It may expand only within the asset's hard length
  maximum and reduce only within the asset's height range; otherwise the candidate is rejected.
- **Cutback** is an individual 120–150° definition-native turn centered on the canonical 135° token.
  Explicit Required Pattern requests now claim a compatible corner slot and remain inside the
  definition's heading window through continuous and canonical closure solving.
- **Heartline Roll** and **Zero-G Roll** add compact heading-neutral transported-roll options. Their
  individual definitions own roll units, centerline orbit, pitch shaping and 0.9–2.2 km envelopes.
- **Dive Loop** and **Sidewinder** add moderate 180° and 90° inversion turns. Their definitions own
  phase order, vertical radii, handedness, recovery and 1.45–2.8 km envelopes.
- The four rotational additions use the reusable `RotationalSequenceV1` compiler and are stamped
  with their exact definition identity just like the established catalog.
- Their version-2 contracts size intentional core rotation from an authored degrees-per-second
  envelope. Ordinary connector roll-rate limits remain unchanged at feature boundaries; this avoids
  the original impossible 6–14 km sizing while keeping every core inside its authored hard cap.
- Required wallrides reserve compatible 90° corner demand before optional and definition-native turn
  families are allocated, preventing enabled moderate inversions from starving an unrelated minimum.
- These six additions have independent ON/MIN/MAX rows in the Generator and are also available to
  explicit recipes and the Track Editor. Their normal procedural rules default to off, preserving
  existing preset rhythm and saved-seed behavior until Unity-focused driveability sweeps approve
  enabling them in a preset. Large compound inversions remain deferred to the next feature session.
- `Track > V2 > Bounded Stability Sweep` now has a **Require One Feature** admission mode. It adds
  the selected pattern only to a cloned settings profile and counts a seed as passing only when the
  accepted layout contains that exact semantic element.

## Inspector command-boundary hardening (complete)

- Native Save, Load and debug-report file dialogs now run after the current Inspector layout pass.
  This removes the repeated `EndLayoutGroup: BeginLayoutGroup must be called first` regression caused
  by opening a Windows file panel from inside a UIElements-backed IMGUI scope.
- Exact Replay already used the same deferred boundary. Recipe I/O and report export now follow one
  consistent rule and disable their buttons while a dialog is queued.

## Stage 5 Turn Realizations (second definition-native realization implemented)

- **Wide Turnaround** is the first additional shape for an existing 150–180° reversal demand.
- It is additive and designer-opt-in through Track Editor, so automatic generation and accepted seed
  behavior do not change merely because the realization exists.
- `wide-turnaround-v1.json` now owns its 150–180° heading window, 1.35× radius, banked core,
  speed/risk intent, common length contracts, recovery ownership and Eased Turn primitive identity.
- The authoritative corner emitter asks the generic definition compiler for the realization; it no
  longer contains a Wide-specific radius, core-section or recovery formula.
- The compiled result preserves the accepted hairpin feature-count semantics, exact signed heading,
  radius/arc formula and physical recovery space.
- Compatibility filtering keeps it out of non-reversal slots. Focused coverage compares its achieved
  exit and footprint against the compatibility hairpin using the real frame builder.
- **Horseshoe** is now the second Track Editor–only reversal realization. Its definition composes one
  reusable Elevated Eased Turn primitive and owns a 2× preferred radius, an 80 m net-zero crest,
  stronger banking, common length contracts and recovery.
- The elevated-turn builder combines eased horizontal curvature with a smooth 0→crest→0 vertical
  profile. It exits level at its original elevation and exact signed heading; its driving-line length,
  vertical curvature and curvature rate are measured rather than hidden from the safety validators.
- The crest carrier is C2 at both owned boundaries: elevation, slope and vertical curvature all return
  to zero before the recovery weld. Ring-level cumulative distance is normalized to the definition's
  fixed-resolution driving-line budget, so subdivision density cannot change the authored Length or
  Exact Recipe identity.
- The topology planner contains no Horseshoe-specific geometry branch. Catalog capability discovery,
  heading compatibility, candidate construction, recipe identity and reporting all use the same
  generic definition path established by Wide Turnaround.
- Definition-native turn compilation now returns an exact failure explanation for missing, disabled,
  malformed, wrong-solver and out-of-heading-window definitions. The topology failure report includes
  that explanation and unsuccessful compilation leaves the caller's section list untouched.

## Track Editor structural slot resolution (new)

- A runtime edit exposed that connector/closure insertion can change the route-order token between
  the finished slot selected by Track Editor and the earlier planning phase where its override is
  applied. Horseshoe, Immelmann and Wide Turnaround could therefore preview correctly but every
  Build & Apply attempt ended as `RecipeCompatibilityFailure` before connector or clearance solving.
- New replacement requests persist a compact structural demand anchor: quarter, road, topology role,
  signed heading, original realization and route order. The planner prefers an exact ID only when it
  also represents that demand, then resolves a shifted pre-connector ID from the structural anchor.
- Resolution is deliberately strict and cannot relax clearance, closure, curvature or driveability.
  Ambiguous or absent demands still fail transactionally and keep the previous valid track.
- When no edited candidate is accepted, Track Editor now prefers the deepest concrete fitting failure
  that reached closure or validation and appends the top failure counts. A final unrelated slot-miss
  attempt can no longer hide the useful reason the compatible candidate was rejected.
- The bounded report now retains one representative detail row per failure mode independently of its
  300-row chronological tail. Rare closure failures can no longer be evicted by hundreds of later
  compatibility failures, which is exactly what obscured the supplied 600-attempt edit report.
- Replacement ownership now consumes an immediately adjacent recovery generated for the old feature
  when the new definition emits its own recovery and the request allows `OwnedConnectors`. This avoids
  stacking two locked recovery straights, returns that length to closure solving and leaves unrelated
  following features untouched.
- Successful rebuilds normalize the override to the rebuilt finished-route slot identity while
  retaining the original structural demand anchor, so recipe save/replay and later Track Editor
  sessions remain stable. Recipes created before structural anchors retain their exact hash
  compatibility through the legacy empty-field hash shape.
- Track Editor now rebuilds the exact accepted attempt index instead of searching 600 unrelated
  candidate routes. This keeps the visible topology slot present, removes the dominant false
  `RecipeCompatibilityFailure` population and makes authored edits substantially faster.
- Procedural lap-length limits are advisory for Track Editor rebuilds. Authoring mode grants generous
  length headroom plus wider safe closure-variable authority, while physical continuity,
  driveability, clearance, collision and mesh-budget validators remain strict.
- Exact Recipe stores the authoring baseline attempt and lap length. These fields survive save/load,
  participate in exact-variant identity, preserve base-recipe identity and retain compatibility with
  recipes hashed before the fields existed.
- **Area of Impact is implemented as designer authority.** Independent Backward/Before and
  Forward/After controls select neighboring semantic features on the same quarter route. The Scene
  view highlights those exact features, the Inspector lists them and suggests compatible designs,
  and an affected feature can be selected for editing before proceeding.
- Zero on either side protects that side. A non-zero window is captured by stable slot and structural
  identities, requires the explicit **Override Features Anyway** confirmation, and may consume those
  features plus the road between them so closure can rebuild the area cleanly. It never weakens
  continuity, driveability, clearance, collision or mesh validators.
- Overlapping imported edit windows fail explicitly. Accepted removals supersede any earlier recipe
  choices inside the window, persist through later edits and Exact Replay, and retain compatibility
  with recipes hashed before Area of Impact metadata existed.
- Returning an applied replacement to its recorded procedural original with a focused impact window
  is now a true recipe revert. The replacement override is removed and the original sampled feature,
  recovery, closure and route regenerate together; the editor no longer compiles a new generic shape
  merely sharing the original feature name. Accepted Area of Impact removals remain preserved when
  they still belong to that override.
- Focused authoring rebuilds now use otherwise-unused recovery straights as a final vertical-balance
  reserve. Residual elevation is distributed with the same conservative eased-grade capacity as major
  climbs/drops; maximum climb/drop angles and level weld contracts remain unchanged.
- Area of Impact on canonical Road A now follows the real lap across quarter boundaries, so Before
  and After can be enabled together even when the selected feature is the final editable slot of its
  quarter. Alternate-road branches remain protected and require their own explicit edit. Focused
  designer rebuilds also receive a larger deterministic elevation-separation rescue sweep while
  preserving the accepted layout, feature and quarter streams.
- The cheap plan-view safety gate now uses the same feature-ownership rule as the final 3D validator:
  a stamped approach, feature and recovery are one feature pattern and are not falsely treated as
  unrelated roads. For a genuinely unrelated crossing, designer rebuilds can translate a complete
  vertical feature block up or down using equal/opposite legal grades on its own approach and recovery.
  This keeps both welds fixed, preserves lap elevation and still requires final 3D validation.
- If that narrow feature-owned bridge lacks sufficient legal slope capacity, designer authoring now
  builds a broader route-level elevation window from the nearest suitable straight roads on either
  side of one collision. The other road remains outside the window; grades remain inside the same
  rulebook caps and the complete lap returns to its original endpoint height. Applied replacement
  geometry also carries an exact marker so cross-quarter After windows cannot lose its stable identity.
- Immelmann validation now follows its authored maneuver semantics: its vertical phase uses the
  half-loop radius envelope, and the 180-degree rollout that restores upright orientation does not
  consume a corkscrew feature allowance. Track Editor identity markers cannot change those rules.
  Owned recovery, ordinary elevation and clearance roads now reserve high-speed vertical-curvature
  and curvature-rate headroom as well as slope headroom. The deterministic clearance pass can resolve
  up to twenty-four successive real crossings on a feature-rich designer route, while the full built
  validator remains the final authority.
- Accepted Area of Impact candidates now use the same stamped replacement identity during final recipe
  normalization as they use inside topology planning. A valid Hairpin → Immelmann candidate can no
  longer be discarded afterward because removing the next feature renumbered its finished route slot.
  Resolution now also recognizes the post-acceptance state where the stable slot records Hairpin as its
  original realization and Immelmann as its current realization; this is the state handed to final
  normalization after topology overrides have been marked applied.
- Focused authoring uses the exact configured unrelated-road corridor in the cheap planning gate rather
  than the procedural generator's extra 5% drift margin. The complete built mesh is still checked at the
  hard corridor, avoiding false rejection of a legal 161 m separation against a 160 m rule.
- Designer clearance repair now requests the exact hard vertical-separation rule instead of adding an
  undocumented 12 m planning surcharge. Final validation still enforces the same hard rule. Ordinary-road
  vertical-profile planning also keeps 10% curvature and curvature-rate headroom in designer rebuilds so
  later retopology and mesh sampling do not turn an exactly-on-limit solution into a false final failure.
- Final retopology now remeasures vertical curvature and curvature rate from the finished ordinary-road
  frames instead of interpolating stale builder derivatives. Open road mouths beside an air gap still receive
  the continuous profile solve; "open" controls the mesh cap, not driveability validation. Area-of-Impact
  rebuilds also reconcile the complete canonical elevation sum before Road B fitting, preferring to soften an
  existing recovery. Broader clearance windows can distribute legal grade across ordinary curves and chicanes
  when the route does not contain enough unused long straights.
- Focused Track Editor rebuilds now capture the accepted route's elevation plan in memory and restore it on
  stable unchanged section identities. Replacement geometry and confirmed Area-of-Impact removals remain
  locally editable, while distant straights no longer receive unrelated climb/drop assignments merely because
  one feature changed the definition sequence. A majority-match guard rejects stale or incomplete preview
  baselines. The accepted sections' actual entry heights are also retained as altitude anchors, so replacement-
  owned roads reconnect between them without moving liked features. Canonical vertical closure is measured
  through the real section builders rather than inferred only from declared elevation amounts. Designer builds
  also run the vertical-profile solver once more on the final retopologized ring grid.

## Verification snapshot

- The latest complete designer-run `V2ReplayAndConnectorGateTests` shown in Unity has **81/81 green tests**.
  The designer also verified
  Hairpin → Wide Turnaround → Horseshoe, then Save → delete generated mesh → Load → Exact Replay with
  the same authored layout restored.
- The current runtime and editor-test project builds succeed with zero C# errors after the definition
  integration and Wide Turnaround migration.
- Existing external-build framework/reference warnings remain Unity project-file warnings, not new C#
  failures.
- Area of Impact adds five focused cases for directional route windows, confirmed stable identities,
  unconfirmed-override rejection, exact recipe persistence/hash identity and legacy recipe hashes.
  The original-feature restoration fix adds two more regressions, and the cross-quarter Area of
  Impact fix adds one and owned vertical-recovery authoring adds one. Pattern-owned proximity and
  deterministic feature-height bridging add two more. Route-level clearance windows and applied
  identity across a cross-quarter After window add two more. Immelmann semantic feature counting,
  half-loop radius validation and curvature-safe owned recovery add three more. Accepted-candidate
  normalization through a cross-quarter After window adds one more. Final-geometry vertical measurement,
  open-boundary profile ownership, canonical AOI elevation reconciliation and curve-carried clearance windows
  add four more. Accepted-elevation preservation and measured altitude-anchor reconnection add two more.
  After Unity refresh the focused gate should contain **82 tests** in the single
  `V2ReplayAndConnectorGateTests` fixture.
- The newest definition, material-contract, ground-clearance and report assertions require a Unity
  refresh and focused gate run because the open Unity editor owns the project lock and authoritative
  test environment.

## Morning review checklist

1. Let Unity refresh and confirm there are no Console compilation errors.
2. Select `V2ReplayAndConnectorGateTests` and run it. Expect **82 tests** in that single fixture.
   The previously confirmed 59-case authoring/definition foundation should remain green; the five new
   cases cover directional impact windows, stable identities, explicit confirmation, recipe identity
   and legacy recipe compatibility.
3. In Track Editor, select a 150–180° reversal. Confirm **Horseshoe** appears beside Hairpin and Wide
   Turnaround, previews as a much broader rising-and-falling turn, and returns to a level exact exit.
4. On the same generated track that previously failed, Build & Apply Horseshoe, Immelmann and Wide
   Turnaround one at a time. The old message saying the edited segment no longer exists must not
   return. Each edit should either succeed or report a real connector/closure/clearance reason while
   retaining the previous valid track.
5. Retry the reported Hairpin → Immelmann edit in this order: Area of Impact 0/1, 0/0, 1/0, then 1/1.
   The 0/1 case must retain the accepted candidate through final identity normalization. The 0/0 case
   must no longer place `Chicane_RightFirst` exactly on its vertical-profile limit. For the two Backward
   cases, a successful designer clearance bridge may be reported in the Console; visually confirm its
   approach and recovery remain smooth and the complete lap stays connected.
6. Set Area of Impact to Backward 0 / Forward 1. Confirm the feature before the selection remains
   unhighlighted, the next feature is highlighted red in the Scene and listed under AFTER, and Build &
   Apply shows the override confirmation. Cancel once and verify the live track remains unchanged.
7. Repeat and choose **Override Features Anyway**. Confirm the named next feature is removed, the road
   and mesh reconnect cleanly, and the Console reports the Area of Impact removal. Save, delete the
   generated mesh, Load and Exact Replay; the removed feature must not return.
8. Expand **Materials**. Use **Create / Repair Default Set** once, then confirm the status reads Ready.
   If you already tuned a palette material, confirm its look and role assignment remain unchanged.
9. Generate a track and export a debug report. Confirm `MATERIAL CONTRACT` says Ready and names every
   road, guide, start-line and checkpoint material.
10. In Track Profile, confirm **Elevation** offers Subtle, Balanced and Extreme.
11. Generate one track for each profile using otherwise identical settings and export a debug report.
12. Compare the `VERTICAL SILHOUETTE` blocks. Subtle should read gentler but not flat; Extreme should
   allocate larger/longer vertical events without slope or curvature violations; Balanced should look
   like the accepted baseline.
13. Save an Extreme recipe, switch profile, load it and Exact Replay. Confirm Extreme and the same layout
   identity return.
14. For Track Editor regression, replace one compatible section, Build & Apply, save, delete generated
   mesh, then Load + Exact Replay. The accepted replacement must return.
15. Open `Track > V2 > Bounded Stability Sweep`, use 6 seeds / 120 seconds / stop on first failure, and
    review the saved report path shown by the tool.

## Next major slices

1. Review material persistence and bounded stability evidence in Unity, then fix any concrete failures.
2. Calibrate Vertical Profiles and generation yield from exported report evidence.
3. Add a small curated acceptance corpus only after the bounded sweep proves its useful runtime; keep
   broad stress tests out of the normal focused gate.
4. V2.1b curvature-flow geometry remains behind its separate opt-in/yield gate because connector
   straights are closure-solver capacity, not merely visual clutter.
5. Run the refreshed focused gate, enable **Half Helix Turnarounds** for a bounded seed sweep, and
   visually validate both handed variants plus Hairpin → Half Helix Track Editor replacement.

## Half Helix Turnaround implementation (2026-08-30)

Half Helix Turnaround is now integrated as an additive, disabled-by-default feature. Its individual
versioned definition owns heading, radius, bank, climb, entry window, recovery and primitive identity.
The geometry is a genuine 150–180° partial helix with a smooth climb and a level exact exit at the new
elevation; it is counted independently from full Spirals and protected from generic spiral-recovery
rewrites. Procedural rule controls, required-pattern placement, rhythm classification, Track Editor
compatibility/replacement, exact-recipe settings and reporting are wired. Focused tests cover catalog
identity, both signed exits, climb/level-exit geometry, compatibility and replacement construction.
Runtime and Editor assemblies compile; the remaining admission gate is the Unity focused suite plus
one visual/high-speed driveability pass before enabling it in presets.
