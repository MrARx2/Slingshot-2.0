# Hovercraft V3 Controlled Dynamics Validation Report

## Completion summary

The permanent controlled-dynamics framework is implemented and verified. It assembles serialized builds through the normal V3 craft path, applies deterministic initial conditions, injects scripted commands through `V3PilotInputAdapter`, uses declared runtime failure paths, records schema 6 telemetry, evaluates typed assertions, exports result packages, compares compatible runs, and stores calibration decisions.

The sprint does not establish that every physical validation gate is closed. Current evidence supports:

- Scenario A1 ballistic with chassis aero: pass.
- Scenario A2 guarded gravity-only approximation: pass.
- Scenario B production-fin runtime matrix: pass (360 points).
- Scenario C race-speed clean-edge departure: pass.
- Scenario E reset-neutrality contract: pass.
- Scenario G authored-variant regeneration persistence: pass.
- Scenario D descending-surface preset: pass, but the required loop/crest/bank/corkscrew matrix is incomplete.
- Scenario F yaw/pitch inertia preset: pass, but roll isolation and multi-model selection are incomplete.
- Scenario H primary full-track gate: fail. The craft did not complete the track and recovery/incident assertions failed.

No physics tuning was changed blindly. The current fin coefficients were retained from measured evidence. A proposed full-track throttle increase was rejected without application because the existing 0.6 command already produced excessive speed and recovery events.

## Previous verification closure

The final current baseline is:

- Complete V3 EditMode assembly: 167 total, 164 passed, 0 failed, 3 intentionally skipped capture tests.
- Serialized CraftLab PlayMode integration: pass inside the complete assembly.
- Serialized HandlingTrack PlayMode integration: pass inside the complete assembly.
- Serialized TrackTest adoption/placement integration: pass inside the complete assembly.
- Enforced parity: 1 passed, 0 failed, 78.281 seconds, 7,801 V2 and 7,801 V3 samples.
- Stable-ID/world-root audit: pass.
- V3 meta/GUID audit: 936 asset/folder items, 936 metas, 0 missing metas, 0 orphan metas, 0 duplicate GUIDs.

One final-suite failure was found and repaired during closure: canonical systems regeneration cloned `V3_HandlingTrack` into CraftLab before choosing the destination world-root ID, producing a duplicate `world.root.v3_handlingtrack`. `V3WorldSimulationInstaller.InstallIntoScene` now accepts an explicit stable root ID and the systems generator writes `world.root.v3_craftlab`.

## Test framework architecture

Primary runtime types:

- `V3ControlledTestDefinition`: serialized identity, build/world requirements, initial condition, overrides, failures, commands, assertions, and output settings.
- `V3ControlledDynamicsTestRunner`: deterministic lifecycle and result production.
- `V3ControlledCraftFailureController`: normal device/computer/power failure-path adapter.
- `V3ControlledSignalRegistry`: stable assertion signal IDs.
- `V3ControlledAssertionEvaluator`: reusable assertion evaluation over the bounded recorder session.
- `V3ControlledTestResult`: machine-readable outcome, assertion details, drop counts, and package reference.

Primary editor types:

- `V3ControlledDynamicsTestWindow` and definition inspector.
- `V3ControlledTestAssetUtility` for A-H/A2 presets and test-only build generation.
- `V3ControlledDynamicsBatchRunner`.
- `V3ControlledComparisonReportWriter` with CLI entry point.
- `V3ControlledFinSweepEvidenceWriter`.
- `V3ControlledAuthoredVariantEvidenceWriter`.
- `V3ControlledDynamicsSceneGenerator`.

The runner never applies continuous hidden force or torque. Commands enter through the test-only exclusive input owner and then traverse Pilot Intent, Mainframe, scheduler, Router, power, firmware, thermal limits, and installed devices. Diagnostics remain read-only.

## Test definitions

Definitions live under `Assets/HovercraftV3/Content/ControlledTests/Definitions`:

- `A_Ballistic.asset`
- `A2_BallisticGravityOnly.asset`
- `B_FinSweep.asset`
- `C_SurfaceDeparture.asset`
- `D_DescendingCapture.asset`
- `E_ResetNeutrality.asset`
- `F_Inertia.asset`
- `G_AuthoredVariants.asset`
- `H_PrimaryFullTrack.asset`

The isolated serialized scene is `Assets/HovercraftV3/Scenes/Diagnostics/V3_ControlledDynamics.unity`, with world root `world.root.v3_controlleddynamics`. It has one world root, no track, and no unrelated colliders. H deliberately uses `V3_TrackTest.unity` and `world.root.v3_tracktest`.

## Deterministic setup

The runner validates the scene, exactly one active world root, world profile, gravity, build stable ID/GUID/ownership, fallback policy, and required control path. It selects and assembles the declared build through `V3CraftTestSpawner`, resolves world- or track-relative pose, creates only the declared fixture, applies normal failure requests, performs the authorized dynamic reset, sets exact pose/linear/angular velocity, synchronizes transforms, waits warmup, and starts recording at the measured boundary.

Phases are `Setup`, `Warmup`, `Measured`, `Cooldown`, `Complete`, and `Aborted`. `identity.controlled_test_phase` is present in compact binary and `samples_core.csv`.

## Assertion framework

Implemented assertion types:

- numeric range;
- target tolerance;
- ordered state transition;
- deadline after a configured measured-time anchor;
- event count;
- event absence;
- contact-free residual P95;
- scheduler task rate;
- build identity;
- editor asset persistence;
- signal reaches minimum (used by the full-lap distance gate).

Every result records expected and measured values, tolerance/window, supporting channels, and evidence filename. A controlled run fails if any assertion fails or any sample/event was dropped.

The editor workflow now selects the craft independently from the test definition. Definitions retain a default craft for unattended and backwards-compatible runs, but an explicit craft selection is carried unchanged through assembly, track-relative ride-height placement, build-identity assertions, recorder metadata, result packages, batches, and calibration-decision targeting. This permits the same scenario and assertion settings to be applied to any compatible V3 craft without duplicating test assets. Runtime physics overrides are temporary, are restored after the run, and emit an explicit evidence warning when applied to a non-`TestOnly` craft instead of rejecting that craft.

## Recorder integration

All serialized scenario runs use schema 6 `ForensicCompact` evidence. The manifest records controlled test ID/version/phase, exact initial-condition JSON, declared failures, expected active systems, assertion set, comparison group, calibration decision ID, build ID/GUID/ownership, expected and actual control path, sample/event capacities and counts, drop counts, and overhead.

The event detector was corrected to normalize null/empty section IDs and to emit section transitions only between two real section IDs. This removed false per-tick `SectionEntry`/`SectionExit` spam in the collider-free fixture. Clean controlled packages require zero dropped samples and events.

## Scenario A — Ballistic

A1 (`test.v3.ballistic.primary_systems.01`) passed 9/9 assertions in the isolated scene:

- 427 samples, 5 events, 0 sample/event drops.
- maximum speed 41.440 m/s.
- contact-free residual P95 1.719 N.
- capture, bottom, roof, and contact remained zero within tolerance.
- measured vertical acceleration matched gravity within 1.5 m/s2.
- `FreeFlight` occurred and no false `ContactLanding` was emitted.
- actual control path was `MainframeScheduled`.

A2 (`test.v3.ballistic.gravity_only.01`) uses `build.test.v3.ballistic.gravity_only.01`, owned `TestOnly`. Its generated controlled build removes fin endpoints and allows an explicit guarded chassis-aero configuration. It passed 7/7 assertions:

- 427 samples, 4 events, 0 sample/event drops.
- chassis aero and fin force both remained at zero within 0.001 N.
- no contacts.
- mean measured vertical acceleration matched -9.80665 m/s2 within 0.25 m/s2.
- contact-free residual P95 2.086 N.

The first A2 attempt failed honestly because disabling the MonoBehaviour did not stop the Mainframe's direct `ApplyReferenceSample()` call. The repair added an explicit `SetChassisAerodynamicsEnabled` configuration hook; it defaults enabled and is reachable from the runner only after TestOnly ownership validation.

## Scenario B — Fin sweep

The serialized B preset passed 3/3 bounded-runtime assertions with 327 samples and no drops. The stronger matrix was generated separately through the production `RuntimeAerodynamicFinInstance.ApplyAerodynamicForce` implementation and the generated `fin.aero.balanced.01` definition:

- 360 points.
- base AoA -180 to +180 degrees.
- speeds 0, 25, 100, and 250 m/s.
- density 1.225 and 0.6 kg/m3.
- fin deflection -10, 0, and +10 degrees.

All seven matrix assertions passed: zero-flow force, near-zero-AoA lift, lift sign, attached-region monotonicity, post-stall lift/drag shape, bounded reverse flow, and CoP/torque consistency. CSV artifacts include all per-point runtime fields plus focused AoA/Cl and AoA/Cd curves.

## Scenario C — Surface departure

The race-speed clean-edge preset passed 4/4 assertions:

- 427 samples, 18 events, 0 sample/event drops.
- ordered `NearSurfaceHover -> CaptureLimited -> FreeFlight` observed.
- bottom and roof post-authority requests reached <=0.001 within 0.25 seconds after the deterministic 1.36-second probe-loss point.
- maximum speed 31.042 m/s.
- contact-free residual P95 1.135 N.

An earlier contaminated TrackTest capture and an isolated capture with 354 dropped events are retained as failed/degraded diagnostic history; neither is indexed as canonical pass evidence.

Coverage limitation: only the target/race-speed edge case is currently captured. Low, medium, and optional high-stress speed definitions remain to be added.

## Scenario D — Loop and descending capture

The deterministic descending-surface preset passed 3/3 implemented assertions with 427 samples, zero drops, bounded capture authority, and residual P95 1.093 N.

This is not complete Scenario D evidence. Separate controlled loop, convex crest, banked curve, and corkscrew fixtures/section tables have not been captured. No statement about the complete D matrix is supported yet.

## Scenario E — Reset

The isolated reset-neutrality run passed 6/6 assertions:

- 327 samples, 22 events, 0 sample/event drops.
- exactly one authorized `Reset` event.
- bottom, roof, and propulsion force were <=1 N within 0.1 seconds of the 1.0-second reset trigger.
- `identity.dynamicsValid` was false on the reset discontinuity tick.
- build identity and Mainframe-scheduled control path matched.

The command before reset includes throttle, strafe, yaw, pitch, lift, downforce, stabilization, and grip-breaker intent. Reset uses `V3FreeDriveSession.ResetCraft(TestAutomation)` and the normal dynamic-reset contract.

## Scenario F — Inertia

The current F preset passed its two implemented assertions with 327 samples, zero drops, and contact-free residual P95 1.730 N. It records Unity's current tensor/rotation, applied torque, expected angular acceleration, measured angular acceleration, and error while running isolated yaw and pitch command windows.

This does not select a new inertia model. A controlled roll input/torque case and explicit Unity-automatic versus distributed point-mass versus compound-primitive comparison report remain open. Production continues to use the current Unity/compound-collider inertia.

## Scenario G — Authored variants

Two persistent assets were created:

- `ScenarioG_VariantA.asset`: target height 3.75 m, capture range 24 m, automatic cap 0.8, manual cap 1.0, emergency allowed, calibration `(0.10, 0.00, 0.00)`.
- `ScenarioG_VariantB.asset`: target height 5.25 m, capture range 30 m, automatic cap 0.55, manual cap 0.7, emergency disallowed, calibration `(-0.10, 0.05, 0.00)`.

After `V3SystemsIntegrationPrototypeGenerator.Generate`, both assets were force-reloaded. All five persistence assertions passed: A values persisted, B values persisted, local tuning stayed independent, product/chassis/catalog references remained shared, and IDs/GUIDs/ownership/fallback policy remained valid.

## Scenario H — Primary full track

The strengthened H gate failed and must remain open:

- 6,027 samples over 60.270 seconds; 0 sample/event drops.
- 306 events.
- maximum speed 550.568 m/s.
- maximum track distance 10,968.02 m versus the 48,000 m assertion threshold (track length 48,230.965 m).
- one automatic reset.
- one crash.
- 18 `OutOfBounds` events.
- residual P95 17.366 N.
- 3/7 assertions passed; full-lap, no-reset, no-crash, and no-out-of-bounds failed.

An earlier weak 3-assertion H preset reported pass; it is superseded by the strengthened package and is not proof of a full lap.

## Comparison reports

The comparison writer loads sibling controlled result, manifest, and analysis report files. It reports test/group/build identity, schema, scene/profile, calibration ID, runner/recorder/export overhead, drop counts, speed, residual P95, event counts, section counts, signal deltas, and assertion deltas. It warns on test/version/group/build incompatibility.

The generated ballistic comparison contrasts the earlier TrackTest package with the isolated package. Maximum speed and residual P95 were unchanged; the isolated fixture reduced events from 10 to 5 while preserving all nine assertion outcomes.

## Calibration decisions

Decision assets live under `Assets/HovercraftV3/Content/ControlledTests/Calibration` and contain decision ID/date/domain/source group, old/new values, expected/measured effect, accepted state, rationale, target asset, author, and package references.

Current decisions:

- `calibration.v3.fin_coefficients.retain.01`: accepted retention/no-op based on the 360-point production-runtime matrix.
- `calibration.v3.full_track.throttle_increase.reject.01`: proposed 0.6 -> 0.7 throttle increase rejected without application because the 0.6 baseline already failed stability/completion evidence.

## Accepted tuning changes

No numerical physics tuning change was accepted. The fin decision explicitly retains current coefficients. Correctness and observability repairs were accepted, including the explicit guarded A2 aero configuration, event-drop gate, section-event normalization, H timeout correction, full-lap assertion, and CraftLab stable-root regeneration fix.

## Rejected tuning changes

The proposed H scripted throttle increase from 0.6 to 0.7 was rejected without being applied. The expected speed/lap-time gain is not worth testing until the 0.6 profile avoids recovery and out-of-bounds behavior. No generated physical asset value was changed for this proposal.

## Performance overhead

Representative runner overhead is below 2.2 ms per complete run:

| Scenario | Runner ms | Recorder capture ms | Export ms | Samples |
|---|---:|---:|---:|---:|
| A1 | 0.157 | 39.055 | 124.980 | 427 |
| A2 | 0.167 | 36.019 | 102.510 | 427 |
| B serialized | 0.148 | 32.819 | 106.319 | 327 |
| C | 0.159 | 40.859 | 144.624 | 427 |
| D | 0.269 | 41.458 | 135.602 | 427 |
| E | 1.830 | 33.277 | 129.564 | 327 |
| F | 0.319 | 31.825 | 108.682 | 327 |
| H | 2.164 | 957.381 | 2005.344 | 6027 |

Recorder and export times are aggregate wall-clock instrumentation for each package, not per-tick simulation cost. All indexed packages have zero dropped samples and events.

## Automated tests

Final XML:

- `TestResults/HovercraftV3/Fast/ControlledSuite-FrameworkFocused-Final.xml`: 8 total, 6 passed, 0 failed, 2 intentionally skipped real-capture gates.
- `TestResults/HovercraftV3/Fast/ControlledSuite-Final-V3EditMode-Rerun.xml`: 167 total, 164 passed, 0 failed, 3 intentionally skipped capture gates.
- `TestResults/HovercraftV3/Fast/ControlledSuite-StableIdAudit-Final.xml`: 1 passed.
- Scenario-specific XML is stored beside other files under `TestResults/HovercraftV3/Fast`.

The controlled assertion unit test covers range, event, transition, build identity, target tolerance, deadline, residual, rate, absence, reaches-minimum, and persistence evaluation.

## Parity

`TestResults/HovercraftV3/Parity/ControlledSuite-EnforcedParity-Final.xml` passed 1/1 in 78.281 seconds. The exported profile contains 7,801 V2 and 7,801 V3 samples under the Unity LocalLow parity directory. The parity test remains a regression comparison, not proof that V3 must reproduce V2 physics in every controlled scenario.

## GUID/meta/stable-ID audit

Final read-only audit of `Assets/HovercraftV3`:

- asset/folder items: 939;
- meta files with GUIDs: 939;
- missing metas: 0;
- orphan metas: 0;
- duplicate meta GUIDs: 0.

The automated stable-ID/world-root test also passes. Controlled definitions, the A2 TestOnly build, authored variants, and both calibration decisions have Unity-generated metas.

## Known limitations

- H does not complete a full benchmark lap and currently fails recovery/incident requirements.
- C has one speed rather than the required multi-speed matrix.
- D has only the descending fixture; loop, crest, bank, and corkscrew coverage is missing.
- F lacks an isolated roll case and formal multi-model selection report.
- The serialized B craft run is not itself the 360-point matrix; the production-runtime matrix is an editor evidence fixture with its own manifest/report/CSVs.
- G's serialized one-second craft preset only proves build identity; regeneration persistence is proven by the dedicated editor evidence package.
- The isolated fixture intentionally has no track centerline, so recorder reports note unavailable track-relative channels where applicable.
- H's current command sequence is throttle-only and is not a validated autonomous driver.

## Files added

- `Assets/HovercraftV3/Runtime/Diagnostics/ControlledTests/V3ControlledTestDefinition.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/ControlledTests/V3ControlledDynamicsTestRunner.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/ControlledTests/V3ControlledCraftFailureController.cs`
- `Assets/HovercraftV3/Editor/Diagnostics/ControlledTests/V3ControlledDynamicsTestWindow.cs`
- `Assets/HovercraftV3/Editor/Diagnostics/ControlledTests/V3ControlledFinSweepEvidenceWriter.cs`
- `Assets/HovercraftV3/Editor/Diagnostics/ControlledTests/V3ControlledAuthoredVariantEvidenceWriter.cs`
- `Assets/HovercraftV3/Scenes/Diagnostics/V3_ControlledDynamics.unity`
- controlled definitions, A2 TestOnly build, Scenario G variants, and calibration decision assets under `Assets/HovercraftV3/Content/ControlledTests`.
- `Assets/HovercraftV3/Tests/Editor/Diagnostics/V3ControlledDynamicsTests.cs`
- this report.

Unity generated and preserved corresponding `.meta` files.

## Files changed

Primary related changes include:

- `V3CraftAerodynamicsRuntime.cs`: explicit default-on chassis-aero configuration for the guarded A2 test path.
- `V3CraftMainframe.cs` and scheduler/runtime failure handling from the preceding repair closure.
- recorder session/schema/export/event files for controlled metadata, phase, actual control path, and event-drop correctness.
- `V3PilotInputAdapter.cs` for exclusive test-only controlled input.
- `V3WorldSimulationInstaller.cs` and `V3SystemsIntegrationPrototypeGenerator.cs` for explicit CraftLab world-root identity.
- `ProjectSettings/EditorBuildSettings.asset` for the isolated diagnostics scene.
- recorder usage/schema documentation and the canonical handoff.

The repository already contained a large migration/reorganization worktree. No unknown work was reset, cleaned, deleted, staged, committed, or pushed.

## Evidence package index

Canonical or qualifying packages:

- A1: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170408_test.v3.ballistic.primary_systems.01_20260801_170404_d34e62255d0c`
- A2: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_172605_test.v3.ballistic.gravity_only.01_20260801_172600_eb44be3c523d`
- B serialized: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170637_test.v3.fin_sweep.primary_systems.01_20260801_170633_e19a06e9ae4c`
- B matrix: `TestDriveReports/HovercraftV3/Controlled/FinSweeps/2026-08-01_171851_scenario_b_production_fin_matrix`
- C: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170236_test.v3.surface_departure.race_speed.01_20260801_170231_6ec1b451dd33`
- D partial: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170652_test.v3.descending_capture.primary_systems.01_20260801_170647_461c68f0aed8`
- E: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170457_test.v3.reset_neutrality.primary_systems.01_20260801_170454_f568662ae2ee`
- F partial: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170744_test.v3.inertia.primary_systems.01_20260801_170741_273c5e159251`
- G serialized identity: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_170756_test.v3.authored_variant.persistence.01_20260801_170755_850f50f4a5f5`
- G persistence: `TestDriveReports/HovercraftV3/Controlled/Authoring/2026-08-01_172117_scenario_g_variant_regeneration`
- H failed gate: `TestDriveReports/HovercraftV3/Controlled/2026-08-01_172807_test.v3.full_track.primary_systems.01_20260801_172706_a7cada42f5d9`
- ballistic comparison: `TestDriveReports/HovercraftV3/Controlled/Comparisons/comparison_20260801_172200.md`

## Safe next steps

1. Diagnose H by section using the failed package before changing physical values. Start with the first reset/crash/out-of-bounds cluster and the saturation/power evidence.
2. Develop a deterministic benchmark command/driver profile that can traverse the full track through normal Pilot/Mainframe authority. Keep the no-reset/no-crash/no-out-of-bounds assertions.
3. Add C low, medium, and high-stress speed definitions with the same post-probe-loss deadline semantics.
4. Add D loop, crest, banked-curve, and corkscrew fixtures plus section tables.
5. Add isolated roll, pitch, and yaw F definitions and compare Unity automatic, distributed point-mass, and compound-primitive tensors before selecting any authored inertia.
6. Rerun the full V3 assembly, serialized integrations, parity, audits, and affected controlled packages after any physical/control calibration.
