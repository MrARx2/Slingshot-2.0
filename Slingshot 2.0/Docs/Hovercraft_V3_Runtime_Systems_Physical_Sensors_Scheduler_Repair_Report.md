# Hovercraft V3 Runtime Systems Repair Report

Verified: 2026-07-29
Unity: 6000.5.0f1
Branch observed during verification: `Jack_Hovercraft_v3`

## Completion summary

Production Gates 0 through 13 are complete. The serialized development scenes
now boot the complete systems craft through one build-selection authority,
scene-owned W input reaches physical thrust, the six sensors are real powered
devices, the seven computers host seven executable system runtimes, scheduler
rates govern actual callback cadence, Mainframe readiness is strict, and the
console/preview reflect the real topology.

Final verification:

- complete V3 suite: 108 discovered, 107 passed, 0 failed, 1 opt-in skip;
- focused runtime repair tests: 8/8 passed;
- serialized-scene Play Mode tests: 2/2 passed;
- enforced 78-second parity capture: 1/1 passed;
- parity telemetry: 7,801 V2 samples and 7,801 V3 samples;
- missing metadata: 0;
- duplicate project asset GUIDs: 0;
- canonical generated/scene hash differences after regeneration: 0;
- compiler/internal/GUID errors in final logs: 0.

Evidence is stored under `TestResults/HovercraftV3/`.

## Gates completed

- Gate 0: baseline, dirty-worktree preservation, scene/build inventory.
- Gate 1: single scene build-selection authority and corrected defaults.
- Gate 2: Mainframe and explicit legacy input compatibility.
- Gate 3: truthful console/input/build diagnostics.
- Gate 4: physical sensor definitions and generated assets.
- Gate 5: six physical runtime sensor devices and environment sampling.
- Gate 6: seven physical computer runtimes.
- Gate 7: seven executable system runtimes.
- Gate 8: real callback-backed scheduler execution.
- Gate 9: controller flow migrated to scheduled outputs.
- Gate 10: strict Mainframe topology and operational readiness.
- Gate 11: ten-page complete console and explicit legacy console behavior.
- Gate 12: editor preview, generator updates, canonical regeneration.
- Gate 13: serialized Play Mode, real input, rebuild, endurance, parity,
  metadata, GUID, force-authority, and full-suite verification.

Self-audits were performed after Gates 1-3, 4-6, 7-9, and 10-12. The final
Gate 13 audit found and repaired one remaining legacy
`UnityEngine.Input.GetKeyDown` call in `V3SystemsDebugDisplay`; the serialized
Play Mode tests then passed.

## Gates incomplete

None.

## Scene build-selection repair

`V3CraftTestSpawner.SelectedBuild` is the development-scene authority.
`V3CraftAssembler` is marked spawner-controlled by the scene spawner and only
uses its serialized build as a matching fallback/preview source.

Both scenes select:

`Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset`

Scene paths:

- `Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity`
- `Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity`

Both serialize systems-build GUID
`70217178c6d526f41827ae2c1f83eff8`. Canonical regeneration preserves the
selection. Each scene has exactly one spawner and one active local input
adapter.

## Input authority and legacy compatibility

`V3PilotInputAdapter` owns the rebindable Unity Input System actions and
exposes its route and authority state.

Supported routes:

- `MainframeIntentBus`: complete craft; input is published to the Mainframe
  Intent Bus and consumed by scheduled Pilot/Drive software.
- `LegacyPipeline`: a V2-reference craft with no Mainframe; the same adapter
  submits to the legacy controller pipeline explicitly.
- `None`: no bound craft or inactive authority.

The adapter exposes current command, target craft, build ID, action state,
authority state, publication/submission timestamps, and whether Mainframe
intent publication is active. Rebuilds rebind the adapter to the new runtime
root. Cursor/UI suspension changes authority without creating a second input
publisher.

`V3SystemsDebugDisplay` now uses `UnityEngine.InputSystem.Keyboard`; no
legacy `UnityEngine.Input` polling remains under `Assets/HovercraftV3`.

## Physical sensor implementation

The systems chassis owns six visible physical sensor GameObjects:

- `Sensor.Front`
- `Sensor.Rear`
- `Sensor.Left`
- `Sensor.Right`
- `Sensor.Top`
- `Sensor.Bottom`

Each mount instantiates `V3_DirectionalSensor_Prototype.prefab` and has:

- one `V3DirectionalSensorDeviceRuntime`;
- a direction-specific `V3DirectionalSensorDefinition`;
- stock directional-sensor firmware;
- authored mass included in craft mass/COM;
- a visible body, facing arrow, range/cast debug, hit point, and normal;
- requested, granted, and measured sample rates;
- requested/granted power and health/bottleneck state;
- bounded Raycast or SphereCast sensing;
- observation-bus publication.

Sensors are components of the one craft body. They add no Rigidbody and no
gameplay collider.

Surface-relative data uses the craft Rigidbody's
`GetPointVelocity(sensorPosition)` and, for moving hit bodies,
`hitBody.GetPointVelocity(hitPoint)`. Relative airflow uses local craft point
velocity rather than only root linear velocity.

## Environment provider

`V3WorldEnvironmentProvider` implements `IV3EnvironmentProvider` and supplies
wind velocity, air density, and ambient temperature at a world position. It
supports optional altitude density falloff. Sensors and aerodynamic fins read
the provider; they do not invent environment values independently.

## Computer runtime implementation

Seven installed computer prefabs contain `V3SystemComputerRuntime`:

- Pilot Interface
- Drive
- Hover
- Stabilizer
- Traction
- Aerodynamics
- Telemetry

Each runtime binds its physical part definition, bundled software, computer
role, capability set, Mainframe connection, task registration, requested and
granted rate, actual measured rate, last execution time, output publication,
state, and reason. A definition or prefab alone is not considered an
operational computer.

## System runtime implementation

Seven role runtimes implement `IV3CraftSystemRuntime`:

- `V3PilotInterfaceSystemRuntime`
- `V3DriveSystemRuntime`
- `V3HoverSystemRuntime`
- `V3StabilizerSystemRuntime`
- `V3TractionSystemRuntime`
- `V3AerodynamicsSystemRuntime`
- `V3TelemetrySystemRuntime`

Shared runtime contracts provide initialization, validation, state, scheduled
execution, physics application, degradation, shutdown, and snapshots.
Runtimes must have a matching physical computer and compatible software.
There is no per-system `Update` or `FixedUpdate`; execution remains
Mainframe-owned.

## Scheduler execution implementation

`V3SoftwareScheduler` now stores a real `IV3CraftSystemRuntime` callback for
each task. A task without a callback cannot be operational.

The scheduler:

- sorts roles by survival priority;
- calculates requested, compute-limited, power-limited, and granted rates;
- clamps work to software, computer, Mainframe, and 240 Hz safety limits;
- grants systems power by priority rank;
- accumulates elapsed time per task;
- executes callbacks at the granted cadence;
- applies per-task and global catch-up caps;
- records execution count, skipped count, last time, duration, failure,
  publication state, bottleneck, and measured actual Hz;
- isolates callback failures and keeps later tasks schedulable.

The Mainframe remains the only execution owner.

## Control-flow migration

Pilot intent is captured by the Pilot system. The Control Router creates
role/domain requests. Drive, Hover, Stabilizer, and Traction systems cache the
command produced by their last successful scheduled callback. Physics ticks
apply those cached outputs to existing controllers and then to the actuator
router.

Power-mode cycling is consumed once. A newly read command cannot bypass a
missed or underpowered software task.

Normal physics authority remains:

- `RuntimeThrusterInstance.AddForceAtPosition`;
- `RuntimeAerodynamicFinInstance.AddForceAtPosition`.

Controllers and Mainframe software add no force, torque, velocity, or pose.
The only pose/velocity writes are explicit development recovery and parity
scenario resets.

## Mainframe readiness

The complete Mainframe validates all mandatory topology:

- valid energy core, router profile, pipeline, and actuator router;
- six unique directional sensor devices at exact mounts;
- sensor definitions, prefabs, firmware, and successful samples;
- seven unique physical computers with software and required capabilities;
- seven matching system runtimes;
- seven callback-backed scheduler tasks;
- device/task capacity.

State semantics:

- `Faulted`: invalid or incomplete mandatory topology.
- `BootingSystems`: valid topology awaiting initial sensor samples or task
  execution.
- `Ready`: all required devices and tasks have become operational.
- `Degraded`: previously bootable topology is underpowered, offline, or has a
  runtime task/device fault.

Fault and degraded reasons are retained and exposed. Incomplete topology
cannot silently report Ready.

## Systems console

The complete systems build exposes ten persistent read-only pages:

1. Mainframe
2. Pilot Interface
3. Drive
4. Hover
5. Stabilizer
6. Traction
7. Aerodynamics
8. Telemetry
9. Power and Cooling
10. Sensors

Required pages remain visible when a system is offline so failure is
inspectable. Mainframe status reports Booting, Ready, Degraded, or Faulted
honestly. Input route/authority, build selection, topology, tasks, sensors,
requested/granted/measured rates, and reasons are observable.

A V2 craft is labeled as legacy topology and exposes its explicit legacy
summary pages rather than pretending to have a complete Mainframe. Console
models are read-only. Public `BoundCraft` and `BoundInput` diagnostics allow
rebuild tests to verify rebinding.

## Editor preview

The non-simulating editor preview resolves the spawner-authoritative build and
shows the real socket, connector, endpoint, six-sensor, seven-computer, and
seven-system topology. Sensor range/facing and physical computers are
inspectable. Preview construction adds no active Rigidbody, collider, or
runtime behavior.

Inspector diagnostics show preview sensor/computer/system counts and the
loaded craft's player-facing mass, size, power, balance, layout, and handling
information.

## Generator changes

The systems generator now authors:

- the directional sensor prototype prefab;
- six sensor definitions with prefab and firmware bindings;
- seven computer prefabs with hardware runtime and matching role runtime;
- computer capabilities and software assignments;
- corrected complete chassis/build topology;
- both serialized development scene selections.

V2 generation preserves stable IDs and connector/endpoint transform chains.
The canonical regenerator finishes by regenerating the development scenes
after systems assets, preventing later generator stages from restoring old
build selections.

Because the main project was already open in Unity, regeneration and batch
tests ran against an isolated copy using the same Unity version. Only
deterministic generator-owned outputs were synchronized back. SHA-256
comparison afterward found 0 differences across all 200 generated files and
19 scene files.

## Automated tests

New/expanded coverage includes:

- one authoritative serialized scene build;
- real keyboard W through Mainframe and legacy routes;
- physical acceleration of systems and V2 builds;
- granted scheduler cadence and execution counts;
- missing sensor and missing callback readiness failures;
- local airflow and moving-surface point velocity;
- both development scenes in Play Mode;
- complete topology and ten-page console;
- live rebuild, old-root destruction, input/console rebind, task execution,
  and post-rebuild W acceleration.

Final suite evidence:

- `TestResults/HovercraftV3/Fast/RuntimeSystemsRepair-Final.xml`
- `TestResults/HovercraftV3/Integration/RuntimeSystemsRepair-Focused.xml`
- `TestResults/HovercraftV3/Integration/RuntimeSystemsRepair-SerializedScenes.xml`
- `TestResults/HovercraftV3/Parity/RuntimeSystemsRepair-EnforcedParity.xml`

## Serialized scene Play Mode results

`V3SerializedScenePlayModeTests` opens each serialized scene, enters Play Mode,
and verifies:

- the complete selected build;
- one scene spawner and one input adapter;
- runtime root and Mainframe;
- active Mainframe input route;
- six sensors, seven computers, seven system runtimes, seven executable tasks;
- ten console pages and Ready state;
- one root Rigidbody;
- scene-owned W input through the complete runtime chain.

Craft Lab additionally rebuilds while playing and verifies old-root
destruction, new-root assignment, input and console rebind, sensor/task
registration, readiness, and post-rebuild acceleration.

Result: 2/2 passed.

## Real Input System acceleration result

The direct Input System tests use Unity's official isolated input test runtime
and a real keyboard state event. Both systems and V2 routes read W as throttle
and physically accelerate.

The serialized Play Mode tests do not call
`V3ControllerPipeline.Tick`. They drive the scene-owned action asset and
verify, in order:

- throttle action value;
- active adapter authority and publication timestamp;
- Pilot and Drive task execution;
- Mainframe pilot intent;
- Propulsion Control Router request;
- main-thruster requested output;
- positive power grant;
- positive actual output and applied force;
- increased forward Rigidbody velocity.

Result: passed in both development scenes and after a live Craft Lab rebuild.

## Parity result

The opt-in parity test was enforced with:

`HOVERCRAFT_V3_RUN_PARITY_CAPTURE=1`

The full 78-second profile completed with 7,801 V2 and 7,801 V3 samples.
Result: 1/1 passed, 0 failures.

## Asset/GUID audit

- every non-meta file under `Assets/HovercraftV3` has a `.meta`: pass;
- duplicate GUID scan across all project `Assets`: 0 duplicates;
- final logs contain no unexpected GUID mismatch: pass;
- final logs contain no missing-script or compiler errors: pass;
- no obsolete `FindObjectsSortMode` or `FindFirstObjectByType` use remains in
  Hovercraft V3: pass;
- no legacy `UnityEngine.Input` polling remains: pass;
- main and regenerated `Generated`/`Scenes` trees hash-identical: pass;
- both scene build GUIDs are the complete systems build: pass.

## Known limitations

- Sensor and computer geometry is functional prototype art.
- The environment provider is a single craft-local provider with uniform base
  wind, temperature, and density plus optional altitude falloff.
- One stock software package exists per system role; player-authored software
  and multiple production router profiles are future work.
- The systems console is a generic diagnostic UI, not final manufacturer
  presentation.
- Damage, multiplayer, procedural audio, Phase-Vector systems, and final art
  remain outside this sprint.
- V2 intentionally has no Mainframe and continues through the explicit legacy
  route.
- Development recovery and parity setup may write pose/velocity; normal craft
  control may not.

## Assumptions

- The existing physical handling/parity baseline remains authoritative.
- The complete systems build is the default for both development scenes.
- A legacy V2 build remains supported for explicit compatibility and parity,
  but does not masquerade as complete topology.
- The open user Unity Editor was not closed or altered; isolated regeneration
  was used to avoid project-lock conflicts.
- Existing unrelated dirty-worktree changes are user-owned and were preserved.

## Files added

Runtime architecture:

- `Assets/HovercraftV3/Runtime/Core/Registries/V3RuntimeDeviceRegistry.cs`
- `Assets/HovercraftV3/Runtime/Parts/Sensors/V3DirectionalSensorDeviceRuntime.cs`
- `Assets/HovercraftV3/Runtime/Parts/Sensors/V3WorldEnvironmentProvider.cs`
- `Assets/HovercraftV3/Runtime/Parts/Computers/V3SystemComputerRuntime.cs`
- `Assets/HovercraftV3/Runtime/Systems/Common/V3SystemRuntimeContracts.cs`
- seven role runtime files under `Assets/HovercraftV3/Runtime/Systems/`

Tests and generated content:

- `Assets/HovercraftV3/Tests/Editor/Integration/V3RuntimeSystemsRepairSprintTests.cs`
- `Assets/HovercraftV3/Tests/Editor/Integration/V3SerializedScenePlayModeTests.cs`
- `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Prefabs/V3_DirectionalSensor_Prototype.prefab`
- matching `.meta` files for all added Unity assets/scripts;
- final result XML/log files under `TestResults/HovercraftV3/`.

Documentation:

- this report.

## Files changed

The principal changed groups are:

- craft assembly, mass, hierarchy, registries, controller pipeline, Mainframe,
  scheduling, routing, input, and device management;
- Drive, Hover, Stabilization, Traction, Aerodynamics, and Telemetry control
  flow;
- systems console, debug display, inspector, and editor preview;
- test spawner and both development scenes;
- V2, systems, scene, and canonical generators;
- generated systems chassis/build/computer/sensor assets and V2 connector
  variants;
- existing systems/readability tests and the EditorTests asmdef;
- `Docs/Hovercraft_V3_AI_Agent_Handoff.md`.

## Files removed

None as part of this repair sprint.

## Safe next steps

1. Open either development scene and let the active Editor refresh/import.
2. Test the complete systems build by hand with W/S, A/D, lift/downforce,
   gimbal/debug views, slopes, bumps, and power modes.
3. Tune authored software rates, power budgets, sensor ranges, and control
   gains only through definitions/generators.
4. Replace prototype visuals and generic console presentation without moving
   control authority out of the existing runtimes.
5. After any physical/control change, rerun the complete suite, both
   serialized Play Mode tests, and enforced parity.
6. Preserve the two normal force sites, one root Rigidbody, one intended
   chassis collider, generator ownership, and all `.meta` identities.
