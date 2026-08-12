# Hovercraft V3 Architecture and Physics Repair Report

Date: 2026-08-01  
Unity: 6000.5.0f1  
Branch observed: `Jack_Hovercraft_v3`

## Completion summary

The source repair is implemented and the production runtime, diagnostics, editor, and track-integration assemblies compile. The sprint corrected fin aerodynamics, surface-state meaning, capture falloff, reset coverage, recorder schema/event semantics, build ownership, stable IDs, generated-versus-authored workflow, explicit legacy fallback, Track Test build selection, and the systems console.

This is not a claim that the complete sprint is empirically closed. The already-open Unity editor remained in Play Mode while source changed, so the post-repair Unity suite and controlled runtime matrix could not be completed without interrupting the active session. Inertia production selection also remains evidence-gated. Those gates are listed explicitly below.

## Gates completed

- Gate 0 — baseline and safety: repository/editor state, Unity version, prior reports, and dirty migration state recorded; unknown changes preserved.
- Gate 1 — asset identity and world ID cleanup: stale duplicate quarantined with a unique ID/ownership, Track Test world ID fixed, cloned balanced product IDs made unique, and a reusable audit added.
- Gate 2 — full dynamic reset: input, controller histories, routers, scheduler grants/accumulators, system runtimes, actuators, firmware state, and cached authority reset; first post-reset physics tick is deliberately neutral.
- Gate 3 — recorder schema and dynamics validity: schema is consistently 6/6.0 and invalid derivative samples are excluded.
- Gate 4 — recorder event semantics: physical contact/capture transitions replace ambiguous takeoff/landing generation; out-of-bounds remains separate.
- Gate 5 — fin aerodynamic model: local-frame AoA, coefficient curves, stall, reverse flow, drag, CoP application, and structural state are implemented.
- Gate 6 — surface-state architecture: detection, near-surface hover, contact, capture, separation, loss, and free flight are distinct.
- Gate 7 — capture authority envelope: distance curve, probe consensus, normal rejection, stale-state clearing, fallback limits, and automatic authority caps are implemented.
- Gate 8 — legacy fallback policy: complete builds fail neutral instead of silently falling back; V2 remains explicitly permitted.
- Gate 12 — full-track primary-build workflow: the single Track Test scene supports baseline, primary systems, and selected authored-build presets; its default is the primary systems build.
- Gate 13 — UI diagnostic completion: hover request-to-actual chain, product/build/system limits, surface state, build ownership/GUID/control path, schema, and per-fin aero state are visible.
- Gate 15 — documentation and canonical handoff: this report and the canonical handoff were updated honestly.

## Gates incomplete

- Gate 9 — authored variants is functionally usable for build identity, hover/capture configuration, per-installation caps/calibration, product sharing, Undo/Save/Revert/Compare/Apply, and generated-asset protection. Stabilizer, traction, drive, aero, Router, and software-rate local override containers are not yet all migrated into the build asset.
- Gate 10 — inertia investigation has an analytical distributed point-mass comparison and recorder channels, but roll/pitch/yaw controlled torque sessions have not selected a production tensor. Unity’s current tensor remains authoritative.
- Gate 11 — all recorded discontinuities were classified as out-of-envelope mapping ambiguity. A triangle-by-triangle visual mesh/collider inspection was not justified by those events and was not performed. No track fix was made.
- Gate 14 — production assemblies compile, but the post-repair Unity EditMode, PlayMode, serialized-scene, parity, and runtime matrix are pending while the open editor is in Play Mode.

## Baseline preserved

The repair preserves one non-kinematic root Rigidbody, one intended chassis collider, installed-parts-driven authority, physical `AddForceAtPosition` sites, Mainframe scheduling, power/firmware/thermal/spool limits, six physical sensors, seven computer/system runtimes, and read-only diagnostics. No controller force, torque, velocity, pose, spline-guided, or hidden-adhesion path was added.

The workspace already contained a large active flat-to-organized V3 migration. No cleanup, reset, checkout, deletion of unknown files, commit, or push was performed.

## Asset identity cleanup

- `world.root.` in Track Test is now `world.root.v3_tracktest`.
- The stale `ApexV3_SystemsIntegration2.asset` is retained as an explicit regression/quarantine asset with its own build ID, GUID, current hover shape, and non-authored ownership.
- Balanced systems hardware cloned from V2 now has balanced-product stable IDs instead of duplicating V2 IDs.
- Generated primary/baseline/V2 builds carry build GUID, ownership, and fallback policy.
- Static serialized audit result: 68 stable asset/world-root IDs, 0 duplicate groups, 0 missing IDs.

## Dynamic reset

`V3CraftRuntime.ResetDynamicState(V3DynamicResetContext)` owns the public phase-aware reset entry. The `BeforePoseReset` phase clears the pilot adapter, pipeline, Mainframe, observation/intent/control buses, scheduler grants and accumulators, system-runtime command caches, controllers, probe and normal history, routers, thruster spool/output, aero actuators, gimbals, and thermal recovery state. `V3FreeDriveSession` invokes before/after phases around the authorized pose/velocity recovery.

The pipeline latches one neutral post-reset physics tick, so held input cannot recreate stale authority on the discontinuity sample.

## Recorder schema and events

- Canonical schema identity: integer `6`, text `6.0`.
- `identity.dynamicsValid` is false for first/discontinuity samples; residual maxima and aggregates exclude invalid derivatives.
- Contact-free residual mean, p95, maximum, and count are exported.
- Physical events include `SurfaceEnvelopeDeparture`, `NearSurfaceExit`, `ProbeLoss`, `ContactLoss`, `PhysicalTakeoff`, `FreeFlightEntered`, `ContactLanding`, and `SurfaceReacquired`.
- Legacy enum values remain for compatibility, but the detector no longer emits generic `Takeoff`/`Landing`.
- `OutOfBounds` is independent of landing.
- Default new profile is `ForensicCompact`; NDJSON is opt-in. CSV exports now include `device_samples.csv` with per-fin airflow, AoA, Cl, Cd, stall/reverse, lift/drag, and structural state.
- Inertia tensor/rotation, applied torque, measured/expected angular acceleration, and error are recorded in schema 6.

## Fin aerodynamic model

Each fin derives chord, lift-normal, and span axes from its physical transform; samples point airflow; calculates signed AoA; evaluates bounded attached lift; reduces lift after positive/negative stall; suppresses reverse-flow lift; computes base, induced, and separated-flow drag; and applies the resultant at the authored center of pressure.

Optional force/moment envelopes report warning and limit state. Existing fin assets migrate deterministically to the generated balanced curve parameters. Force application remains passive and physical.

## Surface-state model

`V3SurfaceState` distinguishes `NoSurface`, `SurfaceDetected`, `NearSurfaceHover`, `SurfaceCaptured`, `SurfaceSeparating`, `CaptureLimited`, `ProbeLost`, and `FreeFlight`. Detection, near-hover eligibility, capture authority, and collider contact are separate truths. Compatibility `IsGrounded` now means near-surface hover eligibility, not any distant hit.

Probe diagnostics expose primary/fallback count, confidence, normal consensus, data age, capture authority, and limit reason. Reset clears all cached probe and normal state.

## Capture authority

Automatic authority is the product of a distance curve and robust probe consensus. The generated profile retains full authority through the near range, decays through the capture range, and reaches zero beyond 28 m. A fallback-only result is capped at 0.15, one/two/three consistent primary probes are capped independently, and normals outside the consensus angle are rejected. Curvature feed-forward, gravity support, height correction, alignment, and roof capture are scaled by this authority.

Roof capture remains installed-thruster-driven and available inside the envelope. Automatic systems do not request emergency overload.

## Product versus craft limits

Thruster products now expose nominal, maximum-normal, and maximum-emergency force semantics while retaining physical spool, mass, thermal, firmware, and structural properties. Each socket installation can own automatic cap, explicit-manual/emergency cap, emergency permission, and local calibration; zero caps inherit the established product limits for migration compatibility. Hover system tuning separately owns desired automatic hover/roof caps and the capture curve.

The console shows product maximum, craft installation cap, system cap/request chain, grants, thermal state, and actual force. Current routing distinguishes normal automatic authority from explicit emergency authority; richer per-source manual caps remain a known follow-up.

## Legacy fallback policy

`V3ControllerPipeline.ActiveControlPath` reports `MainframeScheduled`, `LegacyFallback`, `FaultedNoFallback`, or `None` with a reason. Primary and baseline complete builds disable legacy fallback. If their Mainframe path is unavailable, the router resolves neutral. V2 explicitly permits the legacy pipeline.

## Authored variants

`CraftBuilderWindow` identifies generated builds as read-only and provides **Create Authored Variant** under `Assets/HovercraftV3/Content/Builds`. Variants receive a new stable ID/GUID, `AuthoredVariant` ownership, no silent fallback, deep-copied hover/capture configuration and installation tuning, and shared immutable product references. Save, Revert, Undo, runtime comparison, deliberate runtime calibration apply, and generator-source navigation are present.

Canonical regeneration writes generated ownership metadata and does not target the authored-build folder.

## Inertia investigation

`V3DistributedInertiaCalculator` provides an axis-aligned chassis-box plus installed point-mass model using the parallel-axis theorem. Tests cover the tensor contribution and torque/acceleration round trip. The recorder provides the quantities needed for roll/pitch/yaw comparison.

No tensor is hardcoded or applied in production. Controlled torque evidence is still required to choose between Unity’s current compound tensor, the distributed approximation, a compound-primitive approximation, or an authored chassis tensor plus installed contributions.

## Track collider audit

The four pre-repair benchmark packages contain eight `TrackNormalDiscontinuity` events at 4674.7, 5676.5, 5839.0, 5916.7, 5928.3, 6627.6, 6651.4, and 7419.4 m. At every event the craft had zero contact, lateral offset at approximately the 58 m half-width boundary, and vertical offset between 62.6 and 277.5 m. One mapping confidence was zero; the others were nearest-centerline mappings after departure.

Classification: mapping ambiguity/out-of-bounds trajectory, not proven mesh/collider discontinuity. Future events under this evidence pattern are emitted as `MappingAmbiguity`; only in-bounds normal jumps retain `TrackNormalDiscontinuity`. Placeholder vocabulary for geometry/collider mismatch, ramp spike, thin edge, and overlapping candidates is reserved, but no unsupported detector claim or geometry fix was added.

Evidence packages:

- `TestDriveReports/HovercraftV3/2026-08-01_121614_BenchmarkRun_0984a83b9f1e`
- `TestDriveReports/HovercraftV3/2026-08-01_121753_BenchmarkRun_faa0eb85d093`
- `TestDriveReports/HovercraftV3/2026-08-01_121929_BenchmarkRun_be71be81b2fa`
- `TestDriveReports/HovercraftV3/2026-08-01_122119_BenchmarkRun_e5e1b6e15bc0`

These are baseline schema-5/baseline-build evidence, not post-repair controlled runs.

## Track Test primary-build workflow

The canonical scene remains `Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity`. Generator menu presets select baseline, primary systems, or the currently selected authored build by editing the same spawner/assembler references. The scene now defaults to `ApexV3_SystemsIntegration.asset`; no scene fork was created. Recorder snapshots/manifests include exact build stable ID, asset GUID, ownership, inferred preset, and actual control path.

## UI diagnostics

The systems console header exposes build ownership, build GUID, actual control route/reason, and schema. Hover exposes surface state, detection/near/contact distinction, probe counts/confidence/age, capture authority/reason, and pre/post-limit requests. Bottom/roof tables show request, routed/allocated command, power, firmware, thermal, product/build/system caps, and actual output/force. Aero exposes per-fin airflow response, AoA, Cl, Cd, stall/reverse state, lift, and drag.

## Automated tests

Added/updated focused coverage includes:

- fin AoA, symmetry, stall, reverse flow, and drag;
- capture distance and fallback authority;
- full reset and neutral first tick;
- complete-build no-fallback and explicit V2 fallback;
- authored variant deep-copy/product-sharing behavior;
- schema 6 identity and first-sample validity;
- physical takeoff/contact landing vocabulary;
- inertia calculation and torque/acceleration inversion;
- stable-ID audit.

Current source-level compilation evidence:

- `Lunarlight.Hovercraft.V3.csproj`: succeeded;
- `Lunarlight.Hovercraft.V3.Diagnostics.csproj`: succeeded;
- `Lunarlight.Hovercraft.V3.Editor.csproj`: succeeded;
- `Lunarlight.Hovercraft.V3.TrackIntegration.csproj`: succeeded;
- `Lunarlight.Hovercraft.V3.EditorTests.csproj`: succeeded as a syntax/reference check when targeted to NUnit-compatible .NET Framework 4.7.2.

The EditorTests build is not counted as test execution. A marker-gated focused Unity run is queued at `Temp/V3RepairFocusedTests.request`; it waits until the open editor leaves Play Mode and compilation is idle.

## Controlled runtime runs

Post-repair Runs A–H (ballistic, fin, departure, loop, inertia, reset, variant, and primary full track) are not yet exported. The active editor session prevented safe execution without user disruption. Prior benchmark packages are retained only as baseline/track-classification evidence.

## Parity

No physical authority bypass was introduced. V2 remains explicitly legacy-enabled. New fin and capture behavior intentionally change physical response, so the prior parity result must not be represented as post-repair parity. Enforced parity needs a current rerun after Unity compilation.

## GUID/meta audit

Static audit of `Assets/HovercraftV3` after the repair:

- missing `.meta`: 0;
- orphan `.meta`: 0;
- duplicate meta GUIDs: 0;
- duplicate serialized asset/world-root stable IDs: 0;
- missing serialized stable IDs in audited scope: 0.

## Known limitations

- Full Unity tests and controlled runtime matrix are pending.
- No production inertia model change was selected without torque evidence.
- Not every local stabilizer/traction/drive/aero/Router/software-rate setting has moved into a per-build override container.
- Installation caps currently distinguish normal automatic versus explicit emergency authority; arbitrary per-request-source manual authority is not yet modeled.
- Surface capture values are engineering defaults awaiting controlled tuning.
- Track geometry mismatch/thin-edge/overlap detection needs direct collider/mesh evidence before implementation.
- Directional sensors remain primarily readiness/diagnostic devices; hover probes remain controller-owned.
- Computer physical power/thermal models and Mainframe coupling remain prototype-level.

## Files added

- `Docs/Hovercraft_V3_Architecture_Aero_SurfaceCapture_Authoring_Repair_Report.md`.

## Files changed

Major changed areas:

- runtime build, assembly, routing, scheduling, input, reset, system-runtime, thruster, aero, hover, and UI sources;
- diagnostics schema, world/contact/belief/device/ledger/event/report/export sources;
- Craft Builder (including the stable-ID audit), canonical generators, and Track Test scene generator;
- generated build/fin/product assets and `V3_TrackTest.unity`;
- focused EditMode test sources;
- canonical handoff.

## Files removed

None from the pre-existing project. The stale duplicate build was quarantined rather than deleted because the workspace was already in an active migration and deletion provenance was not safe to assume.

## Documentation updated

- `Docs/Hovercraft_V3_AI_Agent_Handoff.md` is the canonical current handoff.
- This report records implemented behavior, evidence, and incomplete gates.

Recorder usage/schema implementation documents still contain pre-schema-6 detail in places and should be refreshed after the first successful schema-6 export so filenames and sample payloads can be verified against an actual package.

## Safe next steps

1. Leave Play Mode and allow Unity to compile; collect `Temp/V3RepairFocusedTests.xml` and summary.
2. Run the complete V3 EditMode and serialized PlayMode suites, then enforced parity.
3. Execute and export controlled Runs A–H with schema 6, preserving exact build GUID/ownership/preset.
4. Use roll/pitch/yaw torque evidence to select—or explicitly retain—the production inertia model.
5. Tune capture ranges/curve/consensus caps from departure and loop evidence.
6. Expand authored per-build override containers only after the current build-level semantics are validated.
7. Inspect track triangles/colliders only if an in-bounds schema-6 discontinuity or direct geometry mismatch is recorded.
