# Hovercraft V3 — Canonical AI Agent Handoff

Last source verification: 2026-08-01  
Unity: 6000.5.0f1  
Branch observed: `Jack_Hovercraft_v3`

This is the canonical handoff for Hovercraft V3. Read the repair report for gate-level evidence and incomplete verification:

`Docs/Hovercraft_V3_Architecture_Aero_SurfaceCapture_Authoring_Repair_Report.md`

## Current status

The source repair for architecture debt, fin aerodynamics, surface capture, reset, recorder semantics, build ownership, authoring, Track Test selection, and diagnostic UI is implemented. Runtime, diagnostics, editor, and track-integration assemblies compile. Static stable-ID and GUID/meta audits pass.

Do not claim the post-repair Unity suite, parity, or controlled Runs A–H have passed. The open Unity editor remained in Play Mode during the repair. A marker-gated focused run is queued and must complete after Play Mode exits.

## Non-negotiable physical architecture

The assembled craft has one non-kinematic root Rigidbody and one intended chassis collider. Installed parts do not add competing rigidbodies or gameplay colliders.

Normal force authority is limited to physical runtimes:

- `Runtime/Parts/Thrusters/RuntimeThrusterInstance.cs` applies powered thruster force at physical force origins.
- `Runtime/Parts/Aerodynamics/RuntimeAerodynamicFinInstance.cs` applies passive aerodynamic force at each fin center of pressure.
- `Runtime/Parts/Aerodynamics/V3CraftAerodynamicsRuntime.cs` applies airflow-derived chassis aerodynamic force/torque.

Controllers, system runtimes, Mainframe software, diagnostics, UI, and track analysis must not directly apply force/torque, write normal gameplay velocity/pose, guide along the spline, or add hidden adhesion. Recovery/reset is the explicit pose/velocity-write exception.

World gravity authority is `V3WorldEnvironmentProvider`/world environment sampling and the Rigidbody gravity configuration. Gravity must be applied exactly once.

## Execution and control flow

```text
Pilot/replay/test intent
  -> V3ControllerPipeline
  -> Mainframe observation and Intent Bus
  -> callback-backed software scheduler
  -> Drive/Hover/Stabilizer/Traction/Aero system runtimes
  -> V3ControlRouter
  -> V3ActuatorCommandRouter
  -> power + firmware + thermal + spool/actuator state
  -> physical thruster/fin/chassis-aero force
  -> root Rigidbody motion
```

Six physical directional sensors and seven physical computer/system runtimes remain required for the complete Mainframe topology. Scheduler cadence is real and bounded; a task definition is not execution.

## Mainframe fallback policy

`V3ControllerPipeline.ActiveControlPath` is the source of truth:

- `MainframeScheduled`: complete scheduled path executed.
- `LegacyFallback`: the selected build explicitly permits legacy execution.
- `FaultedNoFallback`: the complete build could not execute Mainframe control and resolved neutral.
- `None`: uninitialized/reset-neutral state.

Primary systems and complete baseline builds disable silent fallback. V2 reference builds explicitly permit legacy control. Never “repair” a complete-build fault by making fallback implicit.

## Fin aerodynamic model

Fins use their physical local chord, lift-normal, and span frame. Runtime samples point airflow, calculates signed AoA, evaluates a bounded lift slope, positive/negative stall, post-stall lift decay, reverse-flow suppression, base/induced/separated drag, and optional structural force/moment limits. Force remains physical and is applied at the authored center of pressure.

Recorder and Aero UI expose relative airflow, airspeed, AoA, Cl, Cd, stall, reverse flow, lift/drag vectors, CoP, and structural state.

## Surface-state and capture semantics

`V3SurfaceState` distinguishes:

- `NoSurface`
- `SurfaceDetected`
- `NearSurfaceHover`
- `SurfaceCaptured`
- `SurfaceSeparating`
- `CaptureLimited`
- `ProbeLost`
- `FreeFlight`

Keep these truths separate:

- surface evidence exists;
- the surface is inside the near-hover envelope;
- automatic capture authority is available;
- a collider contact exists.

Compatibility `V3HoverController.IsGrounded` means near-surface hover eligibility, not any distant ray hit.

Capture authority is distance-curve authority multiplied by robust probe consensus. It reaches zero beyond the configured capture range (28 m in the generated systems profile). Fallback-only authority is capped, one/two/three primary-probe confidence is capped separately, disagreeing normals are rejected, and stale histories clear. Curvature feed-forward, gravity support, height control, surface alignment, and roof capture scale with the same authority. Roof capture remains installed-thruster-driven.

## Product, installation, and system limits

Keep ownership explicit:

- Product definitions own nominal/normal/emergency physical force, spool, mass, structure, thermal limits, and firmware compatibility.
- `SocketInstallation` owns automatic cap, explicit-manual/emergency cap, emergency permission, and local calibration. Zero cap inherits the established product limit for migration compatibility.
- `V3HoverConfiguration` owns desired automatic hover/roof caps and capture authority tuning.
- Runtime objects own temperature, current output/angle, sensor samples, task accumulators, and faults.

The console exposes product maximum, installation cap, system cap/request, grants/limits, and actual output. Do not move runtime state into authored assets.

## Dynamic reset contract

Public entry:

`V3CraftRuntime.ResetDynamicState(V3DynamicResetContext)`

`BeforePoseReset` deterministically clears pilot input queues/current command, controllers, hover probes/normals/history, control and actuator routers, Mainframe buses, scheduler grants/accumulators/counters, system-runtime cached commands, firmware/device state, thruster spool/output, aero actuators, gimbals, and recovery thermal state. `V3FreeDriveSession` invokes before/after phases around the authorized pose/velocity reset.

The pipeline deliberately suppresses authority on the first post-reset physics tick. Held input may resume on a later tick, but the discontinuity tick is neutral.

## Recorder schema and event meanings

Canonical identity is schema integer `6`, text `6.0`.

`identity.dynamicsValid` is false on the first sample and external discontinuities. Force/torque residual aggregates exclude invalid derivative samples. Reports include contact-free residual count, mean, p95, and maximum.

Physical transition vocabulary:

- `SurfaceEnvelopeDeparture`: current surface evidence left sensing range.
- `NearSurfaceExit`: near-hover eligibility ended.
- `ProbeLoss`: all current probe evidence was lost.
- `ContactLoss`: collider contact ended.
- `PhysicalTakeoff`: prior support/capture ended and free flight began.
- `FreeFlightEntered`: no contact, no near surface, and negligible capture authority.
- `ContactLanding`: a new collider contact began.
- `SurfaceReacquired`: surface evidence returned.
- `OutOfBounds`: mapped craft position is outside track bounds; never a landing by itself.
- `MappingAmbiguity`: normal jumped while the craft was outside reliable mapping confidence/bounds.
- `TrackNormalDiscontinuity`: an in-bounds independent normal jump that warrants collider/geometry inspection.

Legacy `Takeoff`/`Landing` enum values remain for compatibility but are not emitted by the detector.

New recordings default to `ForensicCompact`; NDJSON is opt-in. CSV includes raw `device_samples.csv`. Controlled-run metadata includes build stable ID, asset GUID, ownership, Track Test preset, and actual control path. Diagnostics remain read-only.

## Build identity and generated ownership

`CraftBuildDefinition` includes:

- stable build ID;
- Unity asset GUID;
- `V3BuildOwnership` (`GeneratedReference`, `AuthoredVariant`, `TestOnly`, `Regression`);
- explicit legacy-fallback permission.

Generated assets are generator-owned. Change their generator/source, not the generated file as normal tuning. The quarantined stale systems build is regression-owned with a unique ID; do not silently restore its old duplicate identity.

Run `Tools > Hovercraft V3 > Validation > Audit Stable IDs` after build/product/scene identity changes.

## Authored build workflow

Open `CraftBuilderWindow`, select a generated or authored build, and use **Create Authored Variant**. Variants are created under:

`Assets/HovercraftV3/Content/Builds`

They receive authored ownership, a new stable ID/GUID, and fallback disabled. Hover/capture configuration and per-installation authority/calibration are deep-copied; immutable product assets remain shared references. The window supports Undo, Save, Revert, Compare Runtime to Source, and deliberate Apply Runtime Calibration. Generated builds are visibly protected from normal authored editing.

Current limitation: not every stabilizer, traction, drive, aero, Router, and software-rate local override has moved into a build-owned override container.

## Inertia model

Production still uses Unity’s current Rigidbody/compound-collider inertia. `V3DistributedInertiaCalculator` is a comparison model only: an axis-aligned chassis box plus installed point masses through the parallel-axis theorem. The recorder captures tensor/rotation, applied torque, measured and expected angular acceleration, and error.

Do not apply or hardcode a new tensor until controlled roll/pitch/yaw torque runs compare Unity automatic, distributed point-mass, compound primitive, and optional authored models.

## Track Test build selection

Canonical full-track scene:

`Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity`

It defaults to `ApexV3_SystemsIntegration.asset`. Use the Track Test generator presets for:

- baseline;
- primary systems;
- selected authored build.

All presets update the same scene/spawner/assembler; do not create scene forks merely to select a build. The world root ID is `world.root.v3_tracktest`.

## Track audit status

The eight schema-5 baseline normal-discontinuity events were all zero-contact, out-of-envelope nearest-centerline mappings at the track half-width boundary and 63–278 m vertical offset. They are classified as mapping ambiguity, not proven collider seams. No geometry was changed.

Inspect mesh and collider triangles only for an in-bounds schema-6 event or direct geometry/collider mismatch. Keep craft-control defects separate from track defects.

## Systems console

The scene-owned read-only console exposes:

- build ownership, GUID, schema, and actual control path/reason;
- Mainframe/scheduler/bus/power/thermal state;
- surface state, detection/near/contact distinction, probe confidence/age, authority and limit reason;
- bottom/roof request → route → allocation → power → firmware → thermal → actual chain;
- product, installation, and system authority caps;
- per-fin AoA, Cl, Cd, stall/reverse, lift, and drag.

It must remain read-only and bounded-rate.

## Controlled dynamics validation workflow

The permanent controlled suite lives under:

- runtime: `Assets/HovercraftV3/Runtime/Diagnostics/ControlledTests`;
- editor: `Assets/HovercraftV3/Editor/Diagnostics/ControlledTests`;
- definitions/builds/variants/decisions: `Assets/HovercraftV3/Content/ControlledTests`;
- isolated scene: `Assets/HovercraftV3/Scenes/Diagnostics/V3_ControlledDynamics.unity`;
- evidence: `TestDriveReports/HovercraftV3/Controlled`.

Open `Tools > Hovercraft V3 > Diagnostics > Controlled Dynamics Tests`, select a test definition and an independent craft definition, validate them, and run the selected or batch workflow. The test definition owns the repeatable scene, fixture, initial conditions, commands, failures, and assertions; the craft selector owns the build under test. A batch keeps the same selected craft across every queued definition. The result and recorder manifest store that selected build's stable ID, GUID, and ownership. CLI evidence capture uses the definition's default craft through the environment-gated `V3ControlledDynamicsTests.SelectedScenario_RunsThroughSerializedSceneAndExports` test with:

`Clear Previous Test Reports...` previews file counts and sizes, then offers either the selected definition's controlled-output folder or the complete project `TestDriveReports` root. Full-root cleanup requires a second confirmation, is unavailable during active runs/batches, and never removes files outside that report root. Report packages are disposable generated evidence, but deleting them invalidates any documentation links that point to those exact captures.

```text
HOVERCRAFT_V3_RUN_CONTROLLED_CAPTURE=1
HOVERCRAFT_V3_CONTROLLED_DEFINITION=Assets/HovercraftV3/Content/ControlledTests/Definitions/<definition>.asset
```

Lifecycle phases are `Setup`, `Warmup`, `Measured`, `Cooldown`, `Complete`, and `Aborted`. Test commands acquire the exclusive test-only `V3PilotInputAdapter` owner and then use the real Mainframe/scheduler/Router/power/firmware/device path. Failure requests use `V3ControlledCraftFailureController` and normal device/computer/power states. Unsupported software/firmware/rate failures are rejected rather than emulated by script disabling.

Controlled results fail on any assertion failure or recorder sample/event drop. Packages contain schema 6 recorder outputs plus `controlled_test_result.json` and `controlled_test_result.md`. Compare compatible results with `V3ControlledComparisonReportWriter`; its CLI entry point reads `HOVERCRAFT_V3_CONTROLLED_RESULTS` as `resultA|resultB`.

The A2 gravity-only approximation is guarded: it uses `build.test.v3.ballistic.gravity_only.01` with `TestOnly` ownership, omits fins, and explicitly disables only chassis aerodynamics through `SetChassisAerodynamicsEnabled`. Production defaults remain enabled, and non-TestOnly definitions are rejected.

Calibration history is stored as `V3CalibrationDecision` assets under `Assets/HovercraftV3/Content/ControlledTests/Calibration`. Each decision records old/new value, expected/measured effect, accepted state, rationale, target, and evidence package references.

Current controlled status is documented in `Docs/Hovercraft_V3_Controlled_Dynamics_Validation_and_Calibration_Report.md`. Do not treat a serialized preset pass as completion of a broader matrix. D and F remain partial, and H is an explicit failed gate.

## Verification status and paths

Source-level compilation succeeded for:

- `Lunarlight.Hovercraft.V3.csproj`
- `Lunarlight.Hovercraft.V3.Diagnostics.csproj`
- `Lunarlight.Hovercraft.V3.Editor.csproj`
- `Lunarlight.Hovercraft.V3.TrackIntegration.csproj`
- `Lunarlight.Hovercraft.V3.EditorTests.csproj` as a syntax/reference check with .NET Framework 4.7.2; this is not Unity test execution.

Static audits: no missing/orphan meta, duplicate meta GUID, duplicate stable ID, missing stable ID, or invalid world-root ID in the audited V3 scope.

Final Unity evidence on 2026-08-01:

- `TestResults/HovercraftV3/Fast/ControlledSuite-FrameworkFocused-Final.xml`: 6 passed, 0 failed, 2 intentionally skipped capture gates.
- `TestResults/HovercraftV3/Fast/ControlledSuite-Final-V3EditMode-Rerun.xml`: 164 passed, 0 failed, 3 intentionally skipped capture gates out of 167. Serialized CraftLab, HandlingTrack, and TrackTest PlayMode integrations pass inside this assembly.
- `TestResults/HovercraftV3/Fast/ControlledSuite-StableIdAudit-Final.xml`: pass.
- `TestResults/HovercraftV3/Parity/ControlledSuite-EnforcedParity-Final.xml`: pass in 78.281 seconds; 7,801 V2 and 7,801 V3 samples.

Final static audit of `Assets/HovercraftV3`: 939 asset/folder items, 939 metas, 0 missing metas, 0 orphan metas, and 0 duplicate GUIDs. The automated stable-ID/world-root audit passes. CraftLab's generator-owned root is `world.root.v3_craftlab`; HandlingTrack remains `world.root.v3_handlingtrack`.

Controlled evidence summary:

- A1 ballistic: pass, 9/9, zero drops.
- A2 gravity-only TestOnly build: pass, 7/7, zero drops.
- B production-fin matrix: pass, 360 runtime points and seven matrix assertions.
- C race-speed edge departure: pass, 4/4, zero drops; other speeds pending.
- D descending fixture: pass, 3/3; loop/crest/bank/corkscrew pending.
- E reset neutrality: pass, 6/6, zero drops.
- F yaw/pitch inertia preset: pass, 2/2; roll and model selection pending.
- G authored variants: regeneration persistence pass for both assets.
- H full track: fail. Maximum 10,968.02 m of 48,230.965 m, one reset, one crash, and 18 OutOfBounds events.

The canonical package index and superseded/degraded-run distinctions are in the controlled validation report. Older schema-5 packages remain baseline history, not post-repair proof.

## Known prototype limitations

- Current capture defaults still need controlled tuning.
- Scenario H does not complete the benchmark and must not be reported as a passing full lap.
- Scenario C multi-speed, Scenario D multi-section, and Scenario F roll/multi-model matrices remain incomplete.
- Production inertia is not yet empirically selected.
- Authored local override coverage is incomplete beyond hover/capture and installation authority/calibration.
- Installation authority does not yet distinguish every arbitrary request source.
- Hover probes remain controller-owned; directional sensors are still mostly readiness/diagnostic inputs.
- Mainframe coupling and computer physical power/thermal fidelity remain prototype-level.
- Track geometry mismatch/thin-edge/overlap detection awaits direct evidence.
- Final cockpit art, manufacturer presentation, damage, multiplayer, audio, and other production features remain out of scope.

## Safe modification rules

1. Preserve one root Rigidbody and one intended chassis collider.
2. Preserve physical thruster, fin, and airflow-based chassis-aero authority.
3. Never add controller force/torque, normal velocity/pose writes, spline guidance, or hidden adhesion.
4. Keep gravity single-authority.
5. Keep power, firmware, thermal, spool, and scheduler cadence authoritative.
6. Keep six physical sensors and seven complete system runtimes for the Mainframe build.
7. Keep diagnostics and UI read-only.
8. Change generated content through generators; keep authored variants outside `Generated`.
9. Preserve `.meta` files and unique stable IDs/GUIDs.
10. Rerun focused, full, serialized-scene, parity, and controlled evidence after physical/control changes.

## Related documents

- `Docs/Hovercraft_V3_Controlled_Dynamics_Validation_and_Calibration_Report.md`
- `Docs/Hovercraft_V3_Architecture_Aero_SurfaceCapture_Authoring_Repair_Report.md`
- `Docs/Hovercraft_V3_Forensic_Dynamics_Recorder_Implementation_Report.md`
- `Docs/Hovercraft_V3_Forensic_Dynamics_Recorder_Usage.md`
- `Docs/Hovercraft_V3_Forensic_Dynamics_Recorder_Schema.md`
- `Docs/Hovercraft_V3_Mainframe_Systems_Integration_Implementation_Report.md`
- `Docs/Hovercraft_V3_Runtime_Systems_Physical_Sensors_Scheduler_Repair_Report.md`
- `Docs/Hovercraft_V3_V2_Mapping.md`
