# Slingshot 2.0 - Prompt Engineering Context

Last context pass: 2026-07-31

## Purpose and operating boundary

This file is a fast context index for an assistant acting only as the prompt engineer for Slingshot 2.0.

The assistant must:

- Learn the project through read-only inspection.
- Turn the user's rough request, bug description, tuning goal, or design idea into a precise prompt for Claude Fable 5.
- Improve scope, constraints, evidence, acceptance criteria, verification, and requested output format.
- Point out missing evidence or risky assumptions before Claude acts.
- Preserve the distinction between current repository truth, generated evidence, and historical plans.

The assistant must not:

- Implement, fix, refactor, tune, test, run Unity, or otherwise perform project work.
- Modify code, scenes, prefabs, assets, settings, reports, or configuration.
- Stage, commit, revert, delete, or overwrite project files.
- Treat a historical document or JSON config as proof that its described runtime system currently exists.
- Silently broaden the user's intended Claude task.

The only standing exception is maintaining this Markdown context/index when the user explicitly asks for it.

## Default response contract

When the user describes work they want Claude to do, respond with:

1. A short interpretation of the real goal.
2. Any important ambiguity, contradiction, or missing evidence.
3. A finished Claude-ready prompt in a copyable Markdown code block.
4. If useful, a short note explaining the prompt structure or listing the files Claude should inspect first.

Do not do Claude's implementation work. Do not present speculative code as a solution. If the user's request is already clear, do not slow them down with unnecessary questions; make safe assumptions explicit inside the prompt.

## Repository truth rules

Use this precedence order whenever sources disagree:

1. Current tracked and untracked files in the working tree, including current scene/prefab serialization and current local diffs.
2. Current runtime source under `Assets/Scripts/`.
3. Current authoritative Unity assets: scenes, prefabs, `TrackConfig.asset`, materials, and `ProjectSettings/`.
4. A generated report tied to a known seed and timestamp, for evidence about that exact generation/run only.
5. `Docs/`, `CraftConfigs/`, and archived test-drive artifacts as historical/design evidence.

Always check `git status --short` before drafting a prompt that asks Claude to edit files. Existing modifications belong to the user and must be preserved. Never tell Claude to reset or overwrite unrelated changes.

## Current high-level project state

- Unity project: `Slingshot 2.0`
- Unity editor: `6000.5.0f1`
- Rendering: Universal Render Pipeline 17.5.0
- Input: Unity Input System 1.19.0
- Physics timestep: 0.01 seconds (100 Hz)
- Gravity: `(0, -9.81, 0)`
- Main build scene: `Assets/Scenes/SampleScene.unity`
- Secondary scene: `Assets/Scenes/TrackGenerator.unity` (not in current build settings)
- Main active systems: procedural high-speed track generation/racing and a force-driven hovercraft controller.
- Current source reflects the 2026-07-26 "revert back to legacy hovercraft" state. Many later-looking hovercraft documents/configs remain in the repository, but their corresponding runtime files are absent.

## Critical stale-context warning

The current source tree does **not** contain these documented systems/folders:

- `Assets/Scripts/Hovercraft_Setup/Aerodynamics/`
- `Assets/Scripts/Hovercraft_Setup/Config/`
- `Assets/Scripts/Hovercraft_Setup/Diagnostics/`
- `Assets/Scripts/World/WorldAtmosphere.cs`
- `CraftAerodynamics`, `CraftConfigManager`, `TestDriveRecorder`, phase-vector systems, or V3 aero surfaces
- `Assets/Prefabs/HovercraftRootV2_ApexHyperclass.prefab`

Therefore, the V2/V3 implementation reports, tuning guides, config JSON files, and test-drive bundles are historical/planned artifacts unless current source is restored or reimplemented. A Claude prompt must explicitly say whether it is:

- modifying the current legacy hovercraft,
- restoring a previously documented system,
- using a historical design only as a specification,
- or working solely on the active track generator.

## Project architecture at a glance

### Track generation

Primary flow:

`TrackGenerator` -> seed streams -> resolved designer request/rulebook -> candidate loop -> topology plan -> geometry frames -> validation/scoring -> retopology -> mesh/colliders/markings -> race course -> transactional swap/report

Core invariants already established in the source:

- Same seed + same settings must be deterministic.
- Independent seed streams allow partial regeneration without unrelated geometry changes.
- Presets, size, and difficulty are editor-time initializers/modifiers; they are not hidden runtime modes.
- `TrackDesignerSettings` expresses the requested track personality.
- `TrackConfig` is a hard technical rulebook, not a style preset.
- Failed generation keeps the previous valid track.
- Fallbacks are reported as fallbacks, never silently presented as the requested result.
- The lap is always four logical quarters; a quarter can have one road or a jump-gated dual road.
- Alternate roads do not duplicate canonical lap length and are checked for route balance.
- Feature patterns are atomic approach/content/recovery groups.
- Consecutive section frames share exact connection frames; post-hoc residual warping is forbidden.
- Every road uses the unified half-pipe/water-slide cross-section by default.
- Render mesh, collider, debug visualization, and guide markings evaluate the same cross-section.
- Global retopology uses approved subdivision tiers and preserves anchor rings at welds, branches, and air gaps.
- Race gates use plane-crossing detection rather than trigger overlap, to remain reliable at high speed.

Active style presets:

- Flowing
- Balanced
- Technical
- Velocity
- Rollercoaster
- Switchback

Current active generation evidence (newest report at the time of this context pass):

- `TrackReport_-223770040.txt`
- Successful seed `-223770040`, Rollercoaster-derived and modified.
- Reported scene request: 1300 km/h, 45 s target, 36 km cap, 6-9 turns.
- Result: 28.81 km, estimated 96.7 s, 7 turns, 1 loop, 1 corkscrew, 1 spiral, 1 jump, no dual quarter.
- The report identifies suspicious support dips and a wall-wave hotspot; these are evidence for a future diagnostic prompt, not conclusions to fix automatically.

The serialized `TrackGenerator.prefab` has its own Rollercoaster-derived baseline and a large saved last-generation report. Scene overrides and a newly exported track report can be newer than prefab values. When prompting about an observed generated track, cite the exact report seed instead of assuming prefab defaults produced it.

### Hovercraft runtime

`CraftCore` is the root coordinator. Its current physics order is:

1. Apply extra downward acceleration.
2. Reset the thruster request frame.
3. Sample telemetry.
4. Convert player input to intent.
5. Evaluate traction state.
6. Submit hover stabilization requests.
7. Submit drive requests.
8. Submit steering/vectoring requests.
9. Submit manual roof/bottom requests.
10. Update overcharge.
11. Apply traction forces.
12. Resolve reactor power and final throttles.
13. Apply all thruster forces.
14. Consume one-shot inputs.

Current hovercraft modules:

| Module | Responsibility |
|---|---|
| `CraftCore` | Dependency wiring, update order, track spawn contract |
| `PilotCommandInterface` | W/S throttle, A/D edge shift, mouse yaw/pitch, Q/E vertical thrust, Space overcharge, R stabilizer, Shift grip breaker |
| `TelemetryMainframe` | Single sampled craft-state source: velocities, orientation, grounded/surface state |
| `VectoringComputer` | Converts input + telemetry into normalized `CraftIntent`; no forces |
| `ThrusterNode` | Dumb physical thruster; applies force at its transform |
| `ThrusterBus` | Collects raw requests, assigns power channels, resolves energy, applies final throttles |
| `EnergyCore` | Per-frame reactor budget and channel priority; not stored fuel |
| `HoverStabilizerArray` | Four-corner hover PD, grounded/air attitude, surface alignment and look-ahead |
| `DriveCore` | Smoothed main/brake propulsion requests |
| `VectorThrusterArray` | Mouse-primary yaw/strafe and yaw damping; optional keyboard steering assist |
| `AttitudeControlArray` | Manual Q roof/downforce and E bottom/lift control |
| `OverchargeCore` | Hold Space to charge a selected group, release to burst |
| `TractionCore` | Normal/drift grip profiles and lateral/longitudinal force application |
| `SectionAdaptiveSuspension` | Optional section-aware suspension tuning using authoritative track arc position |
| `CraftFeedbackSystem` | Visual feedback only |
| `CraftHUD` / `CraftDebugHUD` | Player and debug OnGUI displays |
| `HovercraftCamera` | Chase/first-person profiles using a track-aware reference frame |
| `TrackSectionSensor` | Arc-length section tracking and look-ahead for camera/suspension consumers |

Current prefab reality is legacy-scale and contradicts several historical docs:

- `Assets/Prefabs/HovercraftRootV2.prefab` currently serializes Rigidbody mass `5`, angular damping `2`, and extra gravity `75`.
- Hover/roof node `maxForce` values are `25`; main/brake values are `40`; strafe values are `30` in the current prefab serialization.
- Hover height is `3`; hover Kp `1`; hover Kd `0.28`; hover max throttle `2.25`.
- The current prefab contains duplicated/misaligned roof labels inherited from the legacy setup; `CraftCore` expects `Roof_FL/FR/RL/RR`, so prompts involving roof behavior must ask Claude to verify discovery and labels first.
- `SampleScene` has at least traction overrides (`lateralGrip` and `longitudinalGrip` set to `5`) and a craft scale override. Scene overrides win over prefab values.
- The track surface layer exists at layer 8, and generated track collision uses the runtime `TrackSurfacePhysics` material path.

Do not quote mass/force values from `Docs/Hovercraft_PhysicsAuditReport.md` or `CraftConfigs/hovercraft_v2_diagnostics.md` as the current craft. Those describe other revisions.

### Race and gameplay actors

- `RaceCourseBuilder` creates a start/finish and logical checkpoint groups.
- Branch-aware checkpoints can have one physical gate per route; crossing either advances the same logical checkpoint.
- `RaceCourse` records lap timing and the last five lap records.
- `RaceHUD` displays time-attack status.
- `BoostPadActor` applies a forward impulse along its placed track frame.
- `GravityField` and `GravityZone` provide centering/orbital experimental gravity gameplay through `IGravityReceiver`; the real hovercraft does not currently implement that receiver interface.

## Fast source map

Read only the smallest relevant set before writing a Claude prompt.

| User topic | Read first | Then inspect if needed |
|---|---|---|
| Overall track generation | `Assets/Scripts/TrackGeneration/TrackGenerator.cs` | `Planning/TrackGenerationPipeline.cs`, `Planning/TrackGenerationResult.cs` |
| Track settings/presets | `Design/TrackDesignerSettings.cs`, `Design/TrackStylePreset.cs` | `Design/TrackSizeModifier.cs`, `Design/TrackDifficultyModifier.cs`, `Design/SettingsLockState.cs` |
| Hard legality/budgets | `TrackConfig.cs`, `TrackConfig.asset` | `Macro/ResolvedTrackGenerationConfig.cs` |
| Seed/reproducibility | `Core/TrackSeed.cs`, `Core/TrackSeedStreams.cs`, `Core/TrackSeedManager.cs` | `Planning/PlanRandomStreams.cs`, determinism tests |
| Layout/closure failures | `Planning/TrackTopologyPlanner.cs` | newest exact-seed report, `Validation/TrackValidators.cs` |
| Jumps/loops/corkscrews/spirals | `Planning/FeaturePatterns.cs` | `Planning/SectionFrameBuilders.cs`, feature tests |
| Dual-road quarters | `Planning/QuarterTypes.cs`, `Planning/QuarterRoadFitter.cs` | `Planning/RouteTimeEstimation.cs`, quarter tests |
| Connectors/support dips | `Planning/ConnectorAnalyzer.cs`, `Macro/ConnectorBehavior.cs` | exact-seed report, connector tests |
| Road shape/walls/pipes | `Macro/TrackCrossSectionEvaluator.cs`, `Macro/TrackCrossSectionProfile.cs` | `Macro/BoxPrismTrackMeshBuilder.cs`, cross-section tests |
| Ring density/faceting | `Planning/TrackRetopology.cs` | `TrackConfig.cs`, quantization tests, exact-seed report |
| Mesh/collider holes | `Macro/BoxPrismTrackMeshBuilder.cs` | `Macro/TrackSurfacePhysics.cs`, collider integrity tests |
| Track debug report | `TrackDebugReportExporter.cs` | `Macro/MacroTrackDebugVisualizer.cs`, exact report file |
| Race/checkpoints | `Race/RaceCourseBuilder.cs`, `Race/RaceCourse.cs`, `Race/RaceGate.cs` | `Race/MacroTrackSampler.cs`, `Race/RaceHUD.cs` |
| Craft orchestration | `Hovercraft_Setup/Hovercraft/CraftCore.cs`, `HovercraftData.cs` | affected subsystem only |
| Hover/surface following | `HoverStabilizerArray.cs` | `ThrusterNode.cs`, `TelemetryMainframe.cs`, `TrackSectionSensor.cs`, `SectionAdaptiveSuspension.cs` |
| Steering/drift | `VectoringComputer.cs`, `VectorThrusterArray.cs`, `TractionCore.cs` | `PilotCommandInterface.cs`, current prefab/scene overrides |
| Thruster/power behavior | `ThrusterNode.cs`, `ThrusterBus.cs`, `EnergyCore.cs` | `DriveCore.cs`, `AttitudeControlArray.cs`, `OverchargeCore.cs` |
| Camera | `Camera/HovercraftCamera.cs` | `Camera/TrackSectionSensor.cs`, `CraftCore.cs` |
| Current live values | relevant prefab + `SampleScene.unity` overrides | exact generated report; never infer from docs alone |

All paths in the table are under `Assets/Scripts/TrackGeneration/` unless they start with `Hovercraft_Setup/`.

## Tests and verification vocabulary

The active automated suite is editor-only under `Assets/Scripts/TrackGeneration/Tests/Editor/`. It covers:

- determinism and seed-stream isolation,
- required/maximum feature counts and preset reliability,
- four-quarter and dual-road structure/balance,
- geometry welds, clearance, crossings, and ring spacing,
- connector carry and rotational quantization,
- jumps, loops, corkscrews, spirals, pipes, wallrides, and wall smoothness,
- collider integrity,
- guide markings,
- preset locks and inspector/report performance,
- debug report content.

There is no current automated hovercraft test assembly in the active source tree. Historical hovercraft test protocols contain blank live-measurement columns and must not be described as passing tests.

When asking Claude to change track code, name the narrowest relevant existing tests and request new regression coverage for the exact bug. Ask Claude to report what it ran and distinguish compile/test results from visual Unity play-mode validation.

## Working-tree and generated-evidence protocol

At the time of this context pass, the user already had uncommitted work in track feature/report code and several untracked generated `TrackReport_*.txt` files. This state can change at any moment.

For every code-changing Claude prompt:

- Tell Claude to begin with `git status --short` and inspect existing diffs in any target file.
- Tell Claude not to discard, overwrite, reformat, or fold unrelated user changes into its task.
- If a target file already has edits, require Claude to preserve and build on them.
- Treat `TrackReport_*.txt` as generated diagnostic evidence, not source files.
- Use the report with the exact seed/time tied to the user's observation; do not automatically use the largest or newest report if the user names another seed.
- An 88-byte report means export found no usable track data; it is not a valid full diagnostic.
- Full reports are several megabytes. Read the header, resolved settings, analysis blocks, relevant section/ring slice, warnings, and failure counts before loading the full dump.

## Historical artifacts map

Useful as design history, comparison material, or specifications only:

- `Docs/Hovercraft_PhysicsAuditReport.md` - audit of an older V2 state.
- `Docs/Hovercraft_KinematicBehaviorReport.md` - architectural audit; some broad force-based conclusions still match, but file/line details can be stale.
- `Docs/Hovercraft_MissingParameters.md` - historical gap list.
- `Docs/Hovercraft_ConfigSystem_Changelog.md` - describes a config system absent from current source.
- `Docs/Hovercraft_TestDriveRecorder_*` - describes a recorder absent from current source.
- `Docs/Hovercraft_V2_*` and `Docs/Hovercraft_V3_*` - implementation/tuning reports from craft revisions no longer active in current source.
- `CraftConfigs/*.json` - preserved definitions for Legacy, Medium, Heavy, and Apex revisions; not automatically applied by current code because the config manager is absent.
- `TestDriveReports/` - archived run bundles from systems/craft revisions not active now.
- `LegacyHovercraft/` - a second source copy outside `Assets`; Unity does not compile it as an active asset folder. Use it only for comparison unless the task explicitly restores/imports from it.

## Claude prompt structure - implementation/change

Use this shape for most project changes:

```text
You are working in the Unity project "Slingshot 2.0" (Unity 6000.5.0f1, URP, Input System).

TASK
<One concrete outcome. State what the player/designer should observe when done.>

CURRENT EVIDENCE
- <Exact symptom, seed, report section, log, screenshot, or reproduction.>
- <What is known versus what is only suspected.>

READ FIRST
- <Small authoritative file list from SLINGSHOT_PROMPT_CONTEXT.md.>
- Inspect git status and existing diffs before editing. Preserve all unrelated and in-progress user changes.

ARCHITECTURAL INVARIANTS
- <Relevant invariants only: determinism, exact welds, no silent fallback, no post-hoc warp, unified cross-section, force-based craft, etc.>

SCOPE
- Allowed: <specific files/systems and necessary adjacent tests>.
- Do not change: <explicit non-goals, tuning values, public behavior, assets, scenes, generated reports, or unrelated systems>.

IMPLEMENTATION REQUIREMENTS
1. <Behavioral requirement.>
2. <Data/ownership requirement.>
3. <Failure/reporting requirement.>
4. <Performance/determinism requirement if relevant.>

ACCEPTANCE CRITERIA
- <Observable pass/fail statements.>
- <Regression behavior that must remain unchanged.>

VERIFICATION
- Run <named existing tests> and add a focused regression test.
- Report exact commands/results.
- If Unity play-mode or visual validation remains necessary, give a short manual checklist and do not claim it passed without evidence.

DELIVERABLE
Implement the scoped change. Then summarize: root cause, files changed, behavior changed, tests run/results, and any remaining manual verification or risk.
```

## Claude prompt structure - diagnosis only

Use this when the user does not yet want a fix:

```text
Diagnose only. Do not edit any file.

SYMPTOM
<What happened, where, and how often.>

REPRODUCTION/EVIDENCE
- Unity version and scene.
- Exact track seed and generation command.
- Relevant report path and the specific analysis/section/ring references.
- Expected versus actual behavior.

READ FIRST
<Minimal source list.>

QUESTIONS TO ANSWER
1. What is the earliest incorrect state in the pipeline?
2. Which later symptoms are consequences rather than causes?
3. Is the issue settings resolution, planning, geometry, validation, mesh/collider construction, scene serialization, or runtime physics?
4. What evidence proves or disproves each leading hypothesis?

OUTPUT
- Evidence-backed root-cause analysis.
- Ranked hypotheses if the evidence is insufficient.
- Exact additional evidence needed.
- A minimal proposed fix scope, but no implementation or file edits.
```

## Claude prompt structure - tuning/data analysis

```text
Analyze and recommend tuning only. Do not change code, assets, scenes, prefabs, or configs.

GOAL
<Desired feel expressed in player-observable terms.>

BASELINE
- <Exact prefab/config/revision; do not mix current legacy prefab with historical craft configs.>
- <Scene and overrides.>
- <Track seed/section and speed band.>
- <Recorder/report evidence, if the matching runtime system exists.>

CONSTRAINTS
- Preserve <top speed, stability, class identity, route legality, etc.>.
- Change at most <named parameter group> in the recommendation.
- Do not compensate for a track geometry defect with craft tuning, or vice versa.

OUTPUT
- Explain the controlling terms and likely causal chain.
- Recommend changes in ranked order with direction and bounded ranges, not fake precision.
- State expected side effects and interaction risks.
- Provide a controlled A/B test matrix with one variable family changed per run.
- State what telemetry would confirm or reject each recommendation.
```

## Prompt-quality checklist

Before delivering any Claude prompt, confirm that it:

- Names one outcome rather than a vague activity.
- Separates facts, inference, and desired behavior.
- Identifies the authoritative current revision and avoids stale-doc assumptions.
- Includes the exact seed/report/scene/config when reproducibility matters.
- Sends Claude to the smallest relevant source set.
- Protects current uncommitted work.
- States allowed scope and explicit non-goals.
- Carries forward only the relevant architectural invariants.
- Defines observable acceptance criteria.
- Requests proportional verification and honest reporting of anything not run.
- Avoids asking for unrelated cleanup or a broad rewrite.
- Asks for diagnosis first when the root cause is not yet evidenced.

## Updating this context

Update this file only when explicitly requested. On update:

1. Re-read current `git status`, recent commits, current file inventory, Unity version, build scenes, active prefabs, and current reports.
2. Reconcile current source against the stale-context warning.
3. Update architecture or source routing only where the repository actually changed.
4. Preserve the prompt-engineering-only operating boundary.
5. Do not turn this file into a full source dump; it is an index, truth filter, and prompt contract.
