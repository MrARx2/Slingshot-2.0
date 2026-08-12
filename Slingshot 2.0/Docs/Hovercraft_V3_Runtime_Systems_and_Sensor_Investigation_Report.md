# Hovercraft V3 Runtime Systems and Sensor Investigation

## Executive summary

### Direct answers

1. **Why does the craft not move from local player input?** The scene-owned `V3PilotInputAdapter` only reads and publishes a non-neutral command while its authority state is `Active`. It becomes `Active` only when both a `V3ControllerPipeline` and a `V3CraftMainframe` are bound. The V2-reference craft has a pipeline but no Mainframe, so the adapter remains `Connected`, `TryReadAuthoritativeCommand` returns a neutral command, `LastIntentPublicationTime` remains `-1`, and the physical chain never receives throttle. This is the first proven stop in the input chain. The physical propulsion path itself is not proven broken: the direct-pipeline test reaches `Rigidbody.AddForceAtPosition`.
2. **Why does the systems console show only two pages?** Both serialized scene spawners select `ApexV3_V2Reference.asset`. That build has no Mainframe, scheduler tasks, or directional sensor definitions. The console correctly discovers only **Pilot Interface** and **Power and Cooling** for that craft. It omits the eight Mainframe/task/sensor-dependent pages.
3. **Are six directional sensors actually instantiated and functioning?** On the complete systems build, six definition-backed `V3DirectionalSensorRuntime` C# objects are created and ticked by the Mainframe. They can publish surface, atmosphere, and health observations. They are **not** physical runtime devices: no sensor prefab is instantiated, no sensor GameObject receives a runtime component, no sensor firmware is bound, and no device registry entry exists. Six authored mount transforms exist on the systems chassis. On the V2 craft selected by the scene spawners, no sensor runtimes exist and `Electronics/Chassis Sensors` is empty.
4. **Are the seven computers real operational runtime parts?** They are real installed visual parts in the complete build: each has a definition, prefab, socket installation, mass, GameObject, and generic `RuntimePartInstance`, and each is registered in the Part Registry. They have no computer-specific runtime component and provide no capability flags. Their operational software state is represented indirectly by `V3ScheduledSoftwareTask` records held by the Mainframe scheduler.
5. **Does each system have real executable logic?** No. Drive, hover, stabilization, traction, input, and active-aero behavior have executable logic, but mostly in shared controllers or physical part runtimes rather than scheduler callbacks. Telemetry is a shared hub. Scheduler tasks contain allocation/state data only; the scheduler does not execute seven system callbacks at their granted rates.
6. **Where is the logic for each system?** Pilot: `V3PilotInputAdapter`, `V3ControllerPipeline`, and `V3ControlRouter`. Drive: `V3DriveController`. Hover: `V3HoverController`. Stabilization: `V3HoverController`, `V3VectorController`, and device-level `V3GimbalController`. Traction: `V3TractionController`. Aerodynamics: fin targets in `V3CraftMainframe.TickManagedDevices`, movement in `RuntimeRotaryActuatorInstance`, and force in `RuntimeAerodynamicFinInstance`. Telemetry: `V3CraftTelemetryHub`. The scheduler tasks gate authority but do not own or call those algorithms.
7. **Is the Mainframe correctly determining Ready/Degraded/Faulted?** Only partially. It faults on missing core construction data and degrades when the directional sensor count is not six. It does not require seven computers/tasks, executable task handlers, valid sensor mounts, initial power grants, router validity, or successful observation publication. It can therefore report `Ready` for an architecturally incomplete craft.
8. **Which documentation claims are inaccurate?** “Complete craft is playable” is false for the current serialized Craft Lab startup result; “six sensor children” is misleading because the children are mount transforms, not sensor devices; “seven software systems” is partial; “scheduler executes all systems” is false; “Mainframe reaches Ready correctly” is overstated; and “ten pages” is true only when the console is bound to the complete systems build. Claims about seven physical computer parts, rebuild rebinding, observation publication on the systems build, and authority removal for missing/underpowered tasks are substantially accurate.
9. **What is the smallest safe repair?** Make each scene have one unambiguous selected build and keep the spawner and assembler synchronized; Craft Lab should serialize the systems build in both places. Separately, allow a valid Mainframe-less V2 craft to accept local input through its pipeline, or explicitly make Mainframe presence mandatory for every playable build. Because Handling Track is documented to use V2 by default and direct pipeline control is supported, treating a valid bound pipeline as sufficient for local input is the smaller compatibility-preserving behavior. Add visible diagnostics for build ID, craft binding, input authority, Mainframe, and discovered page names.
10. **What architecture work is still genuinely missing?** Physical sensor instances and firmware/device registration; dedicated computer hardware runtimes; scheduler callbacks with real cadence and execution timestamps; explicit dedicated system runtimes or a documented shared-controller architecture; stronger Mainframe boot/readiness contracts; environment-backed sensor data; telemetry gating/history/export; and serialized-scene Play Mode tests that use the real Input System and verify actual Rigidbody acceleration.

The two observed symptoms have one immediate common context: the running craft is the V2-reference build, not a complete systems craft. Craft Lab additionally contains conflicting serialized build selections: its assembler prefab override points at the complete systems build, while its scene spawner points at V2. Unity does not define `Start` ordering between these same-order components, so startup is order-dependent; whenever `V3CraftTestSpawner.Assemble` runs, it overwrites the assembler selection with V2. The scene generator recreates this conflict.

No project repair was implemented during this investigation.

## Investigation method

The investigation treated source, serialized scenes/assets, prefabs, and runtime logs as authoritative over prose documentation. It included:

- reading all five requested documents;
- resolving scene GUIDs to build and prefab assets;
- tracing scene startup, assembly, reconnect, local input, routing, allocation, power, and physical force code;
- inventorying all sensor, computer, software, firmware, chassis, build, and prefab assets;
- inspecting console page-discovery conditions and rebuild binding;
- reviewing existing Unity test source and XML results;
- checking the active Unity process, project lock, last scene, and Editor log.

The project was already open in a human-controlled Unity 6000.5.0f1 Editor. `Library/EditorInstance.json` identified the live Editor process, and `Library/LastSceneManagerSetup.txt` identified Craft Lab. A second batch Editor could not safely open the locked project. The investigation therefore did not steal, terminate, or manipulate the user's open Editor and did not save either scene. “Observed” below means the user's supplied Play Mode observation or an existing test/log result. “Source-predicted” means deterministic behavior proven from serialized state and code but not freshly driven through a second Editor during this pass.

## Scene and selected-build findings

| Scene | Spawner-selected build | Build ID | Complete? | Assemble on start | Assembler prefab | Assembler build field | Runtime root | Mainframe/computers/sensors |
|---|---|---|---|---:|---|---|---|---|
| `V3_CraftLab.unity` | `Generated/ReferenceCrafts/V2Parity/Builds/ApexV3_V2Reference.asset` | empty `stableId`; display name `Apex V3 Reference` | No | Yes | `Generated/ReferenceCrafts/V2Parity/Prefabs/ApexV3_V2Reference_Assembler.prefab` | Prefab override points to `ApexV3_SystemsIntegration.asset` | V2: `Apex V3 — V2 Reference (V3 Runtime)`; systems if explicitly rebuilt: `Apex V3 Systems Chassis (V3 Runtime)` | V2 has none; systems has Mainframe, seven computer installations, six sensor definitions |
| `V3_HandlingTrack.unity` | same V2 asset | empty `stableId`; display name `Apex V3 Reference` | No | Yes | same V2 assembler prefab | V2 | `Apex V3 — V2 Reference (V3 Runtime)` | None |

Evidence:

- Both scenes serialize spawner `selectedBuild` GUID `8cfe9877c8be94144a1796609fb6ce52` at scene line 9896 and `assembleOnStart: 1` at line 9897. The GUID resolves to `ApexV3_V2Reference.asset`.
- Craft Lab serializes an assembler `build` override to GUID `70217178c6d526f41827ae2c1f83eff8` at lines 10784–10786. That GUID resolves to `ApexV3_SystemsIntegration.asset`.
- Both scenes instantiate assembler prefab GUID `e1310a6063ed2894380d5ef911828b41`, the V2-reference assembler.
- `V3CraftAssembler.Rebuild` instantiates the selected chassis prefab and names the root from the chassis display name (`V3CraftAssembler.cs:32–59`).
- `V3CraftTestSpawner.Assemble` assigns its own `selectedBuild` to the assembler before rebuilding (`V3CraftTestSpawner.cs:66–79`).
- `V3FreeDriveSceneGenerator` creates the scene with the V2 assembler and copies that assembler build into the spawner. `V3SystemsIntegrationPrototypeGenerator.CreateSystemsTestScene` later changes only the assembler build override, not `V3CraftTestSpawner.SelectedBuild`. Canonical regeneration therefore recreates the split configuration.

Answers:

- Both development scenes default to V2 at the spawner level. Craft Lab is additionally internally inconsistent.
- Yes, V2 completely explains the two-page console.
- Yes, V2 plus the adapter's Mainframe requirement explains the missing local movement.
- Explicitly selecting the systems build changes the assembled topology: it creates a Mainframe, seven scheduler tasks, six sensor runtime records, and the inputs needed for all ten console pages. Source indicates the adapter then reaches `Active`; this exact scene mutation was not saved or freshly played during the locked-Editor investigation.

## Input chain findings

| Stage | Owner / initialization | Binding / runtime reference | State for observed V2 craft | Passes data? / early returns |
|---|---|---|---|---|
| Input actions | Scene object `Local Pilot Input Source`, `V3PilotInputAdapter.OnEnable` | Adapter owns and enables generated `V3PilotActions` | Component and actions enabled | Actions alone do not imply authority |
| Craft binding | `V3CraftTestSpawner.Reconnect` and `V3FreeDriveSession.RefreshCraftBinding` | Current root's `V3ControllerPipeline` and optional `V3CraftMainframe` | Current pipeline bound; Mainframe null | Binding is current, not stale |
| Authoritative read | `V3PilotInputAdapter.TryReadAuthoritativeCommand` | Requires `AuthorityState == Active` | `Connected`, not `Active` | Returns neutral command |
| Intent publication | `V3PilotInputAdapter.NotifyIntentPublished` / pipeline | Mainframe Intent Bus when present | No Mainframe; last publication stays `-1` | No non-neutral intent reaches bus |
| Controller pipeline | `V3ControllerPipeline.FixedUpdate` and `Tick` | Controllers, Mainframe, router, actuator router | Present and initialized on V2 | Receives neutral scene command; direct `Tick` works |
| Mainframe / pilot task | `V3CraftMainframe.Initialize` | Intent/Observation buses and scheduler | Absent on V2 | Entire onboard layer absent |
| Drive request | `V3DriveController.Submit` | Pipeline calls controller after router filtering | Controller exists | Neutral throttle yields zero main request |
| Control Router | `V3ControlRouter.Resolve` | Mainframe task states gate domains when Mainframe exists | V2 fallback route, but neutral input | No granted propulsion demand |
| Actuator allocation | `V3ActuatorCommandRouter.ResolveAndApply` | Indexed installed thrusters | Initialized | Zero requested output |
| Power | `V3PowerDistributor` | Allocates grants to registered consumers | Initialized | No propulsion demand to grant |
| Thruster | `RuntimeThrusterInstance.ApplyPowerGrant` | Bound root Rigidbody | Installed and bound | Zero output; force not applied |
| Physics | Root Rigidbody | `AddForceAtPosition` | Valid | Unreached for local throttle |

Key source evidence:

- `V3PilotInputAdapter.OnEnable` creates/enables actions (`V3PilotInputAdapter.cs:87–92`).
- `TryReadAuthoritativeCommand` reads real input only in `Active`; otherwise it returns neutral (`:179–192`).
- `BindCraft` records pipeline and Mainframe references (`:194–214`).
- `RefreshAuthorityState` reports `AwaitingCraft` without a pipeline, `SuspendedByUi` for UI/cursor suspension, `Connected` without a Mainframe, and only then `Active` (`:381–407`).
- The pipeline's `FixedUpdate` invokes its input provider then `Tick` (`V3ControllerPipeline.cs:46–58`).
- `V3CraftTestSpawner.Reconnect` and `V3FreeDriveSession.RefreshCraftBinding` fetch components from the current assembled root and bind both input and console. There is no evidence of a stale/destroyed root.
- `RuntimeThrusterInstance.ApplyPowerGrant` ends at `Rigidbody.AddForceAtPosition` when definition, Rigidbody, enabled/thermal state, requested force, and power grant permit it (`RuntimeThrusterInstance.cs:95–145`).

Requested Play Mode values for the observed V2 result:

| Probe | Result |
|---|---|
| Input authority | `Connected` (source-determined from pipeline present + Mainframe null) |
| Target craft | Current V2 runtime root |
| Controller pipeline | Assigned and initialized |
| Mainframe | null |
| Input Actions | enabled |
| UI/cursor suspension | No evidence that suspension caused the failure; state signature is Mainframe-less `Connected` |
| Intent Bus | not connected |
| Last intent publication | `-1` until an `Active` publication |
| Throttle intent | neutral / 0 |
| Drive request | 0 |
| Router propulsion grant | 0 |
| Main-thruster requested/granted/actual output | 0 / 0 / 0 for scene local-input path |
| Rigidbody velocity change under W | none, matching the user's observation |

The precise first divergence is the adapter's authority gate, before Mainframe intent publication. Existing tests pass because they do not exercise this path. The propulsion test calls `V3ControllerPipeline.Tick(command, 0.02f)` directly and observes 64,800 N at the main thruster. That bypasses the real Input System actions, adapter authority state, Intent Bus publication, scheduler, scene startup ordering, and reconnect lifecycle. The input and spawner tests assert binding/state with synthetic objects but do not simulate W and assert acceleration. The console test assembles the systems build directly rather than opening either serialized scene.

## Physical movement failure

The evidence does **not** justify rewriting the physical movement systems. The direct pipeline test proves that a forward command can be converted into a main-thruster request and physical force. Source inspection also confirms that craft propulsion is applied by installed thrusters through `Rigidbody.AddForceAtPosition`; no direct craft-level propulsion force was found.

For the current scene-local path:

```text
Input action enabled
→ adapter bound to current V2 pipeline
→ Mainframe is null
→ authority remains Connected
→ authoritative read returns Neutral
→ no pilot intent publication
→ zero Drive/Router/actuator demand
→ zero thrust/power/force
```

The systems build is expected to remove this particular gate because it adds a Mainframe, but the serialized scene split must first be resolved. A functioning systems-build path would still require the Mainframe and its pipeline to initialize successfully.

## Sensor architecture findings

`V3DirectionalSensorDefinition` is a ScriptableObject definition. It has no prefab reference and no firmware field. `V3DirectionalSensorRuntime` is a plain sealed C# object, not a `MonoBehaviour`, `Component`, or `RuntimePartInstance`.

When a complete chassis definition is present, `V3CraftMainframe.Initialize` loops its integrated sensor definitions, locates a named mount, and constructs six `V3DirectionalSensorRuntime` objects. Those objects are stored in the Mainframe's list and ticked there. The assembler never instantiates sensor prefabs, because none exist.

The systems chassis prefab contains six Transform-only mounts:

- `Sensor.Front` at local `(0, 0, 6.1)`
- `Sensor.Rear` at `(0, 0, -6.1)`
- `Sensor.Left` at `(-4.2, 0, 0)`
- `Sensor.Right` at `(4.2, 0, 0)`
- `Sensor.Top` at `(0, 1, 0)`
- `Sensor.Bottom` at `(0, -1, 0)`

`V3RuntimeHierarchy` reparents those transforms under `Electronics/Chassis Sensors`. This is correct reparenting, not a disappearing-object bug. The V2 chassis contains none of the six mounts; the hierarchy organizer still creates the empty group, explaining the user's empty object.

Current sensing implementation:

- **Surface distance/normal:** `Physics.Raycast` along each mount's forward direction, up to the definition range. It is not a SphereCast.
- **Relative surface velocity:** records the craft Rigidbody's `GetPointVelocity` at the hit point. It does not subtract a hit/moving-surface Rigidbody velocity, so the field is not truly relative on moving geometry.
- **Airflow:** shared `-body.linearVelocity`, supplied by the Mainframe to every sensor.
- **Air density:** shared hardcoded `1.225`.
- **Ambient temperature:** shared hardcoded `20`.
- **Health/rates:** requested/granted rate, power, and ambient temperature are published.
- **Observation publication:** surface, atmosphere, and sensor-health snapshots are published to the Mainframe Observation Bus when the complete Mainframe ticks the sensor.

The 60 Hz and 120 Hz requested assets are capped to the physics rate, normally 50 Hz. All six therefore request/grant 50 Hz at full systems power and request 3.25 units of sensor power each. The limiting reason is physics rate, not compute or power in the reference configuration.

Missing sensor architecture:

- sensor prefabs and visible prototype bodies;
- a component attached to each mount;
- sensor-specific runtime part/device identity;
- registration in a device/part/capability registry;
- assigned sensor firmware and firmware execution;
- individual physical/data/power connectors;
- editor direction/range gizmos;
- environment provider for airflow, density, and temperature;
- correct relative velocity against moving hit bodies;
- failure validation when a named mount is missing (current lookup falls back to the craft root);
- tests through a serialized scene and console page.

## Sensor runtime inventory

All six definitions live under `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Definitions/`.

| Sensor | Definition | Prefab / GameObject component | Authored mount / complete-build runtime path | Requested / granted | Publication |
|---|---|---|---|---|---|
| Front | `Sensor_Front.asset` | none / none | `<root>/Electronics/Chassis Sensors/Sensor.Front` | 120 requested, capped to physics rate (normally 50); 3.25 power at 50 Hz | Surface, atmosphere, health |
| Rear | `Sensor_Rear.asset` | none / none | `<root>/Electronics/Chassis Sensors/Sensor.Rear` | 60 → normally 50; 3.25 | same |
| Left | `Sensor_Left.asset` | none / none | `<root>/Electronics/Chassis Sensors/Sensor.Left` | 60 → normally 50; 3.25 | same |
| Right | `Sensor_Right.asset` | none / none | `<root>/Electronics/Chassis Sensors/Sensor.Right` | 60 → normally 50; 3.25 | same |
| Top | `Sensor_Top.asset` | none / none | `<root>/Electronics/Chassis Sensors/Sensor.Top` | 60 → normally 50; 3.25 | same |
| Bottom | `Sensor_Bottom.asset` | none / none | `<root>/Electronics/Chassis Sensors/Sensor.Bottom` | 120 → normally 50; 3.25 | same |

Those paths exist only for the complete systems chassis. For the V2 craft selected by both scene spawners, **none of the six paths exists**; only `<root>/Electronics/Chassis Sensors` exists and is empty.

Answers:

- They are functional definition-backed software sensor records, not physical runtime devices.
- The observed group is empty because the running V2 chassis has no sensor mounts.
- On a complete build, runtime records are stored inside `V3CraftMainframe.Sensors`; they are not elsewhere in the hierarchy.
- No sensor objects are created and then incorrectly reparented.
- Yes, definitions create Mainframe-owned records without sensor GameObjects.
- Observations publish only when a complete, initialized Mainframe ticks.
- The console Sensors page reads Mainframe sensor records and Observation Bus snapshots; it has nothing to show on V2 and is omitted.
- Sensor firmware is generated as an asset but is not referenced or executed by the sensor definition/runtime.

## System-computer inventory

The exact complete-build path pattern is:

`<root>/Cockpit/System Computers/System.<Role>/<Computer display name>`

| Role | Definition / prefab | Socket / runtime path suffix | Mass / compute | Software task at full reference resources | Runtime and registration |
|---|---|---|---|---|---|
| Pilot Interface | `Computer_PilotInterface.asset` / `.prefab` | `System.PilotInterface/Balanced PilotInterface Computer` | 22 kg / 300 | 120 Hz requested and granted; minimum 60; power 5; rank 0; ManualPilot | Generic `RuntimePartInstance`; Part Registry yes; capability flags none |
| Drive | `Computer_Drive.asset` / `.prefab` | `System.Drive/Balanced Drive Computer` | 22 / 300 | 60/60; min 30; power 3.5; rank 4; Propulsion, Braking | same |
| Hover | `Computer_Hover.asset` / `.prefab` | `System.Hover/Balanced Hover Computer` | 22 / 300 | 120/120; min 60; power 5; rank 1; RideHeight | same |
| Stabilizer | `Computer_Stabilizer.asset` / `.prefab` | `System.Stabilizer/Balanced Stabilizer Computer` | 22 / 300 | 120/120; min 60; power 5; rank 2; Steering, Attitude | same |
| Traction | `Computer_Traction.asset` / `.prefab` | `System.Traction/Balanced Traction Computer` | 22 / 300 | 120/120; min 60; power 5; rank 5; Strafe, Traction | same |
| Aerodynamics | `Computer_Aerodynamics.asset` / `.prefab` | `System.Aerodynamics/Balanced Aerodynamics Computer` | 22 / 300 | 60/60; min 30; power 3.5; rank 6; Aerodynamics | same |
| Telemetry | `Computer_Telemetry.asset` / `.prefab` | `System.Telemetry/Balanced Telemetry Computer` | 18 / 300 | 30/30; min 10; power 2.75; rank 8; no control domain | same |

Each prefab contains Transform, MeshFilter, MeshRenderer, and generic `RuntimePartInstance`; no Rigidbody, collider, or dedicated computer component. The definition assets themselves declare zero hardware idle/max power and `requiresPower: false`, but require data and bundle a software definition. Software task power is allocated separately.

The seven objects are not mere ownership markers. They are instantiated parts, contribute mass through `V3CraftMassCalculator`, appear in `V3CraftRuntime.InstalledParts`, and are registered in `V3PartRegistry`. They do not register capabilities because their `providedCapabilities` sets are empty.

No `V3SystemComputerRuntime` or `RuntimeSystemComputerInstance` class exists. Runtime computer state is split between:

- generic `RuntimePartInstance` for presence and requested/granted rate/power state;
- `V3SystemComputerDefinition` for bundled software and compute capacity;
- `V3ScheduledSoftwareTask` for requested/granted software rate, power, and operational gate.

The Mainframe can distinguish installed hardware, bundled software, a task above/below its minimum rate, and a missing task. It cannot model a distinct hardware boot/fault/runtime beyond the generic part state. On the observed V2 craft, all seven are **missing**. On the complete reference craft with full resources, source calculations make all seven tasks operational.

## System logic inventory

| System | Actual logic and runtime owner | Scheduler relationship | Classification |
|---|---|---|---|
| Pilot Interface | Scene input read in `V3PilotInputAdapter`; intent flow in `V3ControllerPipeline`; onboard authority gate in `V3ControlRouter` | Pilot task gates ManualPilot; no callback | Implemented through shared controller/input only |
| Drive | `V3DriveController.Submit` shapes throttle/braking and submits main/brake actuator demands | Drive task gates Propulsion/Braking | Implemented through shared controller only |
| Hover | `V3HoverController.Submit` performs per-thruster SphereCasts, weight share, height error, point-velocity damping, and support demand | Hover task gates RideHeight | Implemented through shared controller only |
| Stabilization | Craft-level surface alignment in `V3HoverController`, yaw/strafe/damping in `V3VectorController`; device articulation in `V3GimbalController` | Stabilizer task gates Steering/Attitude | Shared controllers plus physical device runtime |
| Traction | `V3TractionController.Submit` implements Grip Breaker/traction wrench and uses `V3HoverController.IsGrounded` | Traction task gates Strafe/Traction | Implemented through shared controller only |
| Aerodynamics | Fin targets calculated in `V3CraftMainframe.TickManagedDevices`; `RuntimeRotaryActuatorInstance` moves mounts; `RuntimeAerodynamicFinInstance.ApplyAerodynamicForce` applies forces | Aero task gates Aerodynamics; no callback and no directional-sensor input | Implemented through Mainframe shared logic and physical part runtime |
| Telemetry | `V3CraftTelemetryHub.FixedUpdate` refreshes current telemetry state | Telemetry task exists but does not gate the hub or its update cadence | Shared hub; scheduler entry does not execute it |

Important distinctions:

- The scene input source represents the human/controller edge. The onboard Pilot Interface Computer is only represented by its installed generic part and scheduler task; there is no onboard Pilot runtime that consumes raw device input.
- Throttle becomes a propulsion actuator request in `V3DriveController.Submit`.
- Hover support does not consume the six chassis sensor observations. It uses local per-thruster SphereCast probes.
- Craft stabilization and gimbal execution are separate: controllers request a wrench; installed gimbal runtime changes an attached thruster's physical direction.
- Traction uses hover grounded state rather than Observation Bus surface observations.
- Aerodynamics is not implemented as a sibling software algorithm. Target fin angles are a hardcoded Mainframe managed-device calculation, and physical fin runtimes execute them.
- Telemetry has a real shared hub but no installed computer-specific runtime, history, or export. Computer presence does not control recording capability or rate.

The empty `Runtime/Systems/Pilot`, `Runtime/Systems/Aerodynamics`, and `Runtime/Systems/Telemetry` folders are organizational scaffolding, not locations containing dedicated system implementations. The behavior is found in input, core Mainframe, shared telemetry, and physical-part runtime files. If the intended architecture requires seven sibling onboard runtimes, those three folders expose missing implementation rather than harmless organization.

## Scheduler task inventory

`V3SoftwareScheduler.Initialize` scans installed generic parts, selects `V3SystemComputerDefinition` instances with bundled software, creates `V3ScheduledSoftwareTask` data records, and sorts them by survival rank. `BeginFrame` calculates requested rates and costs; `GrantRank` writes granted rate/power and generic part state.

| Task ID (de facto software stable ID/role) | Source | Handler/callback | Requested / granted / min Hz | Compute cost | Power | State | Last execution | Domains |
|---|---|---|---|---:|---:|---|---|---|
| Pilot Interface | Pilot computer/software | none | 120/120/60 | 120 | 5 | Operational | not stored | ManualPilot |
| Hover | Hover computer/software | none | 120/120/60 | 120 | 5 | Operational | not stored | RideHeight |
| Stabilizer | Stabilizer computer/software | none | 120/120/60 | 120 | 5 | Operational | not stored | Steering, Attitude |
| Drive | Drive computer/software | none | 60/60/30 | 60 | 3.5 | Operational | not stored | Propulsion, Braking |
| Traction | Traction computer/software | none | 120/120/60 | 120 | 5 | Operational | not stored | Strafe, Traction |
| Aerodynamics | Aero computer/software | none | 60/60/30 | 60 | 3.5 | Operational | not stored | Aerodynamics |
| Telemetry | Telemetry computer/software | none | 30/30/10 | 30 | 2.75 | Operational | not stored | none |

The reference Mainframe has 1,400 compute units versus 630 total requested, and its energy core's systems ceiling is 1,300, so all tasks receive their requested values under the authored full-resource configuration.

The scheduler does not contain a callback field, accumulator, last-run timestamp, or dispatch loop. It does not execute algorithms at 30/60/120 Hz. `IsOperational` only means software exists and granted rate meets minimum. Role-to-controller behavior is hardcoded outside the scheduler, chiefly in `V3ControlRouter` and the pipeline. Therefore:

- there are seven task records on the complete build;
- none is itself an executing control task;
- all seven can be shown operational without a task callback;
- tasks are allocation/health/authority gates, not scheduled execution;
- the mapping is by role/domain and hardcoded controller flow, not callback registration.

## Mainframe readiness findings

Current boot conditions in `V3CraftMainframe.Initialize`:

- **Faulted:** missing craft runtime/build/chassis, missing root Rigidbody, or installed part count exceeds Mainframe device capacity.
- **Uninitialized:** no integrated Mainframe definition; in practice the V2 assembler does not add a `V3CraftMainframe` component at all.
- **Degraded:** initialization reaches sensor construction but the resulting directional sensor count is not exactly six.
- **Ready:** core references are present, device count is within capacity, and exactly six sensor runtime records were created.

The check does not require:

- seven computer installations;
- seven scheduler tasks or any particular role;
- executable task callbacks;
- valid unique sensor mounts (missing mounts silently use the craft root);
- router/profile validity;
- task power grants or `IsOperational`;
- sensor power grant;
- a successful sensor sample or Observation Bus publication;
- telemetry availability.

The complete reference asset will normally report `Ready`, but that label means “core object valid and sensor list count equals six,” not “all seven systems have booted and executed.” The observed V2 craft should not qualify as Ready and contains no Mainframe to report a boot state.

## Systems console findings

For the observed V2 craft the two pages are:

1. **Pilot Interface**
2. **Power and Cooling**

The complete registry can contain ten pages:

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

`V3SystemsConsoleController.RebuildPageRegistry` uses these availability rules:

- Mainframe: `V3CraftMainframe` exists.
- Pilot: scene input exists or Pilot task exists.
- Drive through Telemetry: corresponding scheduler task exists.
- Power/Cooling: power or thermal component exists.
- Sensors: Mainframe exists and `Sensors.Count > 0`.

With V2, Mainframe, all six role pages, and Sensors are omitted. Pilot remains because the scene input source exists; Power/Cooling remains because the craft has those runtime services.

`BindCraft` refreshes all craft component references and immediately rebuilds the registry. The spawner and free-drive session bind it to the current assembled root after assembly/rebuild. The assembler initializes the Mainframe synchronously before raising `AssembledCraftChanged`. There is no evidence that the console is stale, bound too early, or caching a partial systems-build list. The two pages are the correct result for the selected V2 topology.

The console discovers role pages by scheduler tasks, not controller components or computer GameObjects. It presently omits an absent/offline role rather than retaining a page with an offline explanation. Whether the complete reference should always display all ten pages while individual systems are offline is a product decision; for diagnostics and player comprehension, installed roles should preferably remain visible and report Missing/Offline/Underpowered. The authored complete reference with seven tasks and six sensors meets the current ten-page conditions.

Automated tests missed the issue because the console test directly assembles `ApexV3_SystemsIntegration.asset`, binds after initialization, and asserts ten pages. It never opens the conflicting serialized Craft Lab scene or allows its V2-selected spawner to run.

## Serialized scene reproduction results

The following matrix separates directly available evidence from source-predicted outcomes. The active human-controlled Editor/project lock prevented a fresh isolated batch Play Mode run without disrupting the user's session.

| Test | Build/topology | Result |
|---|---|---|
| A — Craft Lab serialized startup | Conflicting selections: assembler override systems, spawner V2 | User-observed result matches V2 winning: no local movement, two pages, empty sensor group. Startup is order-dependent until selections are unified. No reliable complete-build result can be claimed from this serialization. |
| B — Handling Track default | V2 consistently selected | Source-predicted and symptom-consistent: no Mainframe, adapter `Connected`, neutral local command, two console pages, no sensors/computers. |
| C — Handling Track explicitly changed to systems | Complete systems topology | Source predicts Mainframe `Ready`, adapter `Active`, seven task records, six sensor records/mounts, ten pages, and an intact propulsion chain. Not freshly executed because doing so would modify/save state in the locked Editor; this remains to be verified after repair. |
| D — runtime rebuild | Depends on selected build | Source and existing synthetic test show old root replacement and input/console rebinding to `CurrentCraft`. Rebuilding through the spawner currently forces its V2 selected build. No actual in-scene W/acceleration/page/sensor lifecycle test exists. |
| E — direct pipeline | Explicit command injected | Existing test passes and applies 64,800 N. First difference from scene input is adapter authority/read/publication, before controller execution. |

The requested detailed live telemetry values cannot be honestly reported for a systems-build Play Mode run that was not performed. The current V2 values are deterministically zero/absent at the authority gate and match the supplied manual observations.

## Direct pipeline versus scene input comparison

| Step | Scene-owned adapter on V2 | Direct `pipeline.Tick` test |
|---|---|---|
| Serialized scene lifecycle | yes | no |
| Real Input System action | enabled but gated | bypassed |
| Current-root reconnect | yes | synthetic/direct |
| Mainframe required by adapter | yes; missing | bypassed |
| Authority | `Connected` | not consulted |
| Intent Bus | no publication | bypassed |
| Scheduler tasks | absent | bypassed |
| Pilot command | neutral | supplied non-neutral command |
| Drive/actuator/power | zero request | executes |
| Physical result | no acceleration | 64,800 N main-thruster force in test |

The first state difference is not in the thruster, power distributor, or Drive controller. It is `V3PilotInputAdapter.TryReadAuthoritativeCommand`: the real scene path rejects input because authority is not `Active`.

## Documentation claim audit

| Claimed feature | Documentation claim | Actual implementation | Runtime evidence | Accurate? |
|---|---|---|---|---|
| Complete craft is playable | Handoff describes complete/playable reference | Complete asset has a valid physical path, but Craft Lab startup is conflicted and observed local play does not move | Manual no-movement; V2 spawner selection; direct pipeline force test only | **False for current scene; asset not fully live-verified** |
| One local input publisher | Scene-owned local adapter | Exactly one enabled scene adapter was found in each scene | Serialized scene objects and adapter source | **Accurate** |
| Input reconnects after rebuild | Rebinds current runtime craft | Spawner/session fetch current root and rebind input/console | Source plus synthetic binding test | **Accurate, but no end-to-end W test** |
| Six sensor children | Six chassis sensors under Electronics | Six Transform mounts exist only on systems chassis; no sensor components/prefabs | Systems chassis prefab; V2 observed empty group | **Misleading** |
| Seven physical computers | Seven physical computer parts under cockpit | Seven real visual generic runtime parts, with mass and Part Registry entries | Build, prefabs, definitions, assembler/scheduler source | **Substantially accurate** |
| Seven software systems | Seven onboard systems | Seven software definitions/tasks, but behavior is shared/hardcoded and Telemetry is ungated | Scheduler and system source | **Partially accurate** |
| Scheduler executes all systems | Functioning scheduler | Scheduler allocates rate/power and states; it calls no algorithms | No callback/dispatch/cadence fields in scheduler | **False** |
| Mainframe reaches Ready correctly | Ready indicates integrated systems | Ready chiefly means six sensor records plus core validity | Mainframe initialization conditions | **Misleading/overstated** |
| Ten console pages | Complete craft exposes ten pages | True only when bound to complete topology; current scene V2 yields two | Registry conditions; console unit test; manual result | **Conditional, documentation omitted condition** |
| Console rebinds after rebuild | Reconnect and topology refresh | `BindCraft` rebuilds registry on current root | Spawner/session/console source | **Accurate** |
| Sensors publish observations | Six integrated sensors publish | Complete Mainframe-owned records do publish raycast/shared-environment snapshots | Sensor runtime/Mainframe source | **Accurate but incomplete architecture** |
| Missing/underpowered computers remove authority | Router degrades domains | Missing task or task below minimum makes role non-operational and removes its domain | Scheduler `IsOperational`; Control Router | **Accurate as a gate** |
| Hierarchy reflects operational ownership | Organized Electronics/Cockpit groups | It reflects authored ownership/installation, not necessarily execution; sensors are mounts and computers generic parts | Runtime hierarchy and component inventory | **Partially accurate** |
| Sensor firmware integrated | Handoff implies firmware-backed sensors | Firmware asset exists but sensor definition/runtime has no firmware binding | Definition/runtime fields and power calculation | **False** |
| Seven computers have dedicated runtimes | Architecture language implies onboard computers | No computer-specific runtime class exists | Source search and prefab components | **False if interpreted literally** |

## Confirmed root causes

| Severity | Issue / affected systems | Evidence | Why tests missed it | Blocks driving | Blocks UI | Blocks future content |
|---|---|---|---|---:|---:|---:|
| Critical | Craft Lab has competing build authorities; the spawner selects V2 while the assembler override selects systems | Scene lines 9896–9897 and 10784–10786; spawner overwrites assembler in `Assemble`; generator only changes assembler | Tests assemble asset directly; no serialized scene startup test | Yes in observed startup context | Yes, yields two pages | Yes |
| High | Local input requires a Mainframe even though V2 is a supported/default playable build | Adapter state/read code; V2 chassis has no Mainframe; Handling defaults V2 | Direct Tick bypasses adapter/Mainframe; binding test asserts references only | Yes | No direct block | Yes, all Mainframe-less craft |
| High | Canonical scene generation recreates the split Craft Lab selection | Free-drive generator creates V2 spawner; systems generator overrides only assembler | Generated-output tests do not assert spawner/assembler build equality | Yes | Yes | Yes |

## Contributing causes

| Severity | Issue | Evidence / impact |
|---|---|---|
| High | No serialized-scene Play Mode input/acceleration test | All relevant passing tests use synthetic objects or direct `Tick`; scene regressions remain invisible |
| High | `V3CraftAssembler` and `V3CraftTestSpawner` both serialize a build selection | Two sources of truth permit order-dependent startup and rebuild behavior |
| Medium | Console hides absent roles instead of explaining them | Two pages look like a UI failure instead of identifying “V2 / no Mainframe / no tasks / no sensors” |
| Medium | Weak runtime diagnostics | Build ID is empty on V2 and authority/Mainframe/page discovery are not obvious to a tester |
| Medium | Mainframe readiness semantics are too weak | “Ready” can conceal missing tasks, invalid mounts, or nonexecuting scheduler work |

## Incorrect assumptions

- **The console is bound to a stale or wrong destroyed root:** not supported. Reconnect fetches `CurrentCraft`, `BindCraft` refreshes references, and the two pages match the selected V2 topology.
- **The console binds before Mainframe initialization finishes:** not supported. Assembly initializes the Mainframe synchronously before change notification/reconnect.
- **The console caches a partial list:** not supported. It rebuilds its registry on every `BindCraft`.
- **Sensor GameObjects are created and then reparented incorrectly:** false. No sensor GameObjects are instantiated; only authored mount transforms are reparented.
- **The computer GameObjects are only empty hierarchy markers:** false. On the complete build they are installed generic runtime parts with mass and registry entries.
- **The physical propulsion implementation is broken:** not proven. Direct pipeline testing reaches an applied main-thruster force; the real scene path stops earlier.
- **The empty system folders prove there is no system logic anywhere:** false. Significant logic exists in shared controllers and physical part runtimes, though not in seven dedicated sibling runtimes.

## Architectural gaps

| Severity | Gap | Consequence |
|---|---|---|
| High | Sensors are Mainframe-owned C# records, not physical devices | No visible/damageable/replaceable device, firmware, per-device registry identity, or honest hierarchy |
| High | Scheduler tasks do not execute callbacks or cadence | Granted Hz and “Operational” do not mean code ran; the scheduler is an allocator/gate, not a scheduler |
| High | Mainframe Ready does not validate required systems | Invalid or incomplete integration can appear healthy |
| Medium | Computers use only generic part runtime | Hardware installed, powered, booted, faulted, and software-running states are conflated |
| Medium | System algorithms are distributed/hardcoded | Role ownership, testing, replacement, and scheduler coupling are unclear |
| Medium | Systems do not generally consume the directional Observation Bus | Sensor integration is mostly display/telemetry rather than control architecture |
| Medium | Environment data is shared/hardcoded | Airflow, density, and temperature are placeholders, not local sensing |
| Medium | Telemetry task does not gate hub behavior and has no history/export | Installed Telemetry Computer has little operational meaning |
| Low | V2 build has no stable ID | Diagnostics and build identity are weaker |

## Test coverage gaps

Existing XML results show 98 total, 97 passed, 0 failed, 1 skipped in the final suite; dedicated systems, contracts, and parity result sets also passed. These are useful component/EditMode tests but do not cover the reported failure.

Required additions:

- open `V3_CraftLab.unity` and `V3_HandlingTrack.unity` as serialized scenes in Play Mode;
- assert assembler and spawner select the same expected build;
- simulate real Input System keyboard/gamepad state through the scene-owned adapter;
- assert adapter authority, publication timestamp, Intent Bus intent, Drive request, router grant, actuator request/grant/actual output, and Rigidbody acceleration;
- assert default Handling V2 is playable or explicitly assert/document that it is not;
- explicitly select the systems build and assert Mainframe boot timing;
- assert seven installed computers, seven tasks, expected task states/rates, and task execution once callbacks exist;
- assert six physical sensor GameObjects/components once that architecture is implemented;
- assert sensor firmware/device registration, power, rate, and actual Observation Bus publication;
- assert ten console pages by name in the serialized complete scene;
- rebuild during Play Mode and assert old-root destruction, new-root binding, task/sensor registration, page refresh, and acceleration;
- negative readiness tests for missing computers/tasks/mounts/router/power/publication.

## Prioritized repair recommendations

### Immediate restoration

1. Establish one build source of truth. At minimum, serialize Craft Lab's spawner and assembler to `ApexV3_SystemsIntegration.asset` and update the generators to set both references together. Add validation that rejects disagreement.
2. Restore Mainframe-less V2 local control. The compatibility-preserving repair is to let a current initialized pipeline grant local input authority even without a Mainframe, while reporting Intent Bus connectivity separately. The alternative is to require a Mainframe in every playable build, but that is broader and changes the documented V2 baseline.
3. Make rebuild use the same authoritative build selection as startup. Do not leave the spawner able to silently replace an assembler selection.
4. Add an always-visible diagnostic summary: selected build asset/stable ID, runtime root instance ID, pipeline, Mainframe boot state, input authority/suspension reason, last publication, scheduler task count, sensor count, and console page names.
5. Keep working thruster, hover, gimbal, traction, and aero force code unchanged unless new end-to-end evidence identifies a defect.

### Architecture completion

1. Define and instantiate a sensor prefab/runtime component per chassis mount. Give it a stable device ID, visible body, direction/range gizmo, firmware reference, power/data state, health, and Part/Device Registry entry.
2. Replace root fallback for missing sensor mounts with a validation/Degraded fault that names the missing mount.
3. Add an environment provider and correct moving-surface relative velocity.
4. Add a dedicated system-computer runtime wrapper that distinguishes hardware installed, powered, booted, faulted, software present, software running, and underpowered.
5. Turn scheduler tasks into executable work: stable task ID, registered callback/handler, accumulator, actual cadence, last-run time, failure state, and measured output publication.
6. Move or adapt system algorithms behind explicit role runtimes/handlers, or formally document the shared-controller design and make scheduler execution honest. Avoid duplicating the working physical algorithms.
7. Strengthen Mainframe Ready requirements around required role set, valid mounts/devices, router profile, scheduler handlers, initial allocation, and observation health.
8. Make the console enumerate installed/expected systems and display Offline/Missing/Underpowered/Faulted rather than silently removing pages.
9. Tie Telemetry Computer/task state to capture cadence and add bounded history/export if those are intended features.

### Test corrections

Implement the scene, real-input, Mainframe timing, complete task, physical sensor, observation, ten-page, Rigidbody acceleration, and rebuild lifecycle tests listed under **Test coverage gaps**. Preserve lower-level direct-pipeline tests, but label them as physical/controller tests rather than end-to-end playability tests.

### Documentation corrections

After repairs, update:

- `Docs/Hovercraft_V3_AI_Agent_Handoff.md`
- `Docs/Hovercraft_V3_Project_Readability_EditorPreview_SystemsConsole_Report.md`
- `Docs/Hovercraft_V3_Mainframe_Systems_Integration_Implementation_Report.md`
- `Docs/Hovercraft_V3_Migration_Map.md`
- `Docs/Complete_Project_Folder_Structure.md` if files/folders are added

Until then, replace unconditional claims about complete playability, physical sensors, scheduler execution, Mainframe readiness, and ten-page scene behavior with the limitations proven in this report.

## Files inspected

Primary runtime source:

- `Assets/HovercraftV3/Runtime/Core/Assembly/V3CraftAssembler.cs`
- `Assets/HovercraftV3/Runtime/Core/Mainframe/V3CraftMainframe.cs`
- `Assets/HovercraftV3/Runtime/Core/Routing/V3ControllerPipeline.cs`
- `Assets/HovercraftV3/Runtime/Core/Routing/V3ControlRouter.cs`
- `Assets/HovercraftV3/Runtime/Core/Scheduling/V3SoftwareScheduler.cs`
- `Assets/HovercraftV3/Runtime/Input/V3PilotInputAdapter.cs`
- `Assets/HovercraftV3/Runtime/Development/TestDrive/V3CraftTestSpawner.cs`
- `Assets/HovercraftV3/Runtime/Development/TestDrive/V3FreeDriveSession.cs`
- `Assets/HovercraftV3/Runtime/Systems/Drive/V3DriveController.cs`
- `Assets/HovercraftV3/Runtime/Systems/Hover/V3HoverController.cs`
- `Assets/HovercraftV3/Runtime/Systems/Stabilization/V3VectorController.cs`
- `Assets/HovercraftV3/Runtime/Systems/Traction/V3TractionController.cs`
- `Assets/HovercraftV3/Runtime/Systems/Stabilization/V3GimbalController.cs`
- `Assets/HovercraftV3/Runtime/Parts/Thrusters/RuntimeThrusterInstance.cs`
- `Assets/HovercraftV3/Runtime/Parts/Aerodynamics/RuntimeAerodynamicFinInstance.cs`
- `Assets/HovercraftV3/Runtime/Parts/Aerodynamics/V3ActiveAeroRuntime.cs`
- `Assets/HovercraftV3/Runtime/Core/Routing/V3ActuatorCommandRouter.cs`
- `Assets/HovercraftV3/Runtime/Core/Power/V3PowerDistributor.cs`
- `Assets/HovercraftV3/Runtime/Parts/Sensors/V3DirectionalSensorDefinition.cs`
- `Assets/HovercraftV3/Runtime/Parts/Sensors/V3DirectionalSensorRuntime.cs`
- `Assets/HovercraftV3/Runtime/Presentation/UI/V3SystemsConsoleController.cs`
- `Assets/HovercraftV3/Runtime/Core/Telemetry/V3CraftTelemetryHub.cs`
- `Assets/HovercraftV3/Runtime/Core/Assembly/V3RuntimeHierarchy.cs`

Serialized/generated content:

- both requested development scenes;
- both V2 and systems build assets;
- V2 and systems chassis definitions/prefabs;
- systems Mainframe, router, computer, software, firmware, and six sensor definitions;
- seven computer prefabs;
- scene-generation/editor scripts for free-drive and systems-integration content.

Documentation and tests:

- all five requested documents;
- V2 parity, systems integration, project-readability, contracts, and final test sources/results;
- Unity project lock, Editor instance, last scene setup, and Editor log.

## Tests and commands run

- Read-only `rg`, `Get-Content`, `Get-ChildItem`, and `Select-String` searches over source, scenes, assets, prefabs, metadata, documentation, tests, XML results, and Unity logs.
- GUID resolution from serialized scene references to `.meta` files.
- Static source trace of the complete local-input-to-force chain.
- Static inventories of sensors, computers, software tasks, console pages, and hierarchy paths.
- Review of existing results: final suite 98 total / 97 passed / 0 failed / 1 skipped; systems 7/7; contracts 6/6; parity 6/6.
- No second Unity test process was started because the project was locked by a human-controlled Unity Editor. No scene or asset was saved, regenerated, or changed.

## Evidence appendix

### Key source locations

| Finding | Evidence |
|---|---|
| Adapter reads only while Active | `V3PilotInputAdapter.cs:179–192` |
| Pipeline without Mainframe is only Connected | `V3PilotInputAdapter.cs:381–407` |
| Adapter binds pipeline/Mainframe | `V3PilotInputAdapter.cs:194–214` |
| Pipeline FixedUpdate and Tick | `V3ControllerPipeline.cs:46–136` |
| Spawner overwrites assembler build and rebuilds | `V3CraftTestSpawner.cs:66–79` |
| Spawner reconnects current pipeline/Mainframe/console | `V3CraftTestSpawner.cs:190–211` |
| Assembler root creation/name and installation | `V3CraftAssembler.cs:32–112` |
| Part Registry initialization | `V3CraftAssembler.cs:166–174` |
| Mainframe/sensor creation | `V3CraftMainframe.Initialize`, especially its chassis sensor loop |
| Mainframe shared atmosphere inputs | `V3CraftMainframe.TickSensors` / managed tick |
| Sensor Raycast/point velocity/publication | `V3DirectionalSensorRuntime.Tick` |
| Scheduler task creation | `V3SoftwareScheduler.Initialize` |
| Scheduler allocation without dispatch | `V3SoftwareScheduler.BeginFrame` and `GrantRank` |
| Console page conditions | `V3SystemsConsoleController.RebuildPageRegistry:117–153` |
| Console fresh bind/rebuild | `V3SystemsConsoleController.BindCraft:787–796` |
| Thruster physical force | `RuntimeThrusterInstance.ApplyPowerGrant:95–145` |

### Serialized scene evidence

| Scene evidence | Location |
|---|---|
| Craft Lab spawner V2 GUID | `V3_CraftLab.unity:9896` |
| Craft Lab assemble-on-start | `V3_CraftLab.unity:9897` |
| Craft Lab assembler systems override | `V3_CraftLab.unity:10784–10786` |
| Handling spawner V2 GUID | `V3_HandlingTrack.unity:9896` |
| Handling assemble-on-start | `V3_HandlingTrack.unity:9897` |

### Asset identity evidence

- V2 build GUID `8cfe9877c8be94144a1796609fb6ce52` resolves to `Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/Builds/ApexV3_V2Reference.asset`.
- Systems build GUID `70217178c6d526f41827ae2c1f83eff8` resolves to `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset`.
- V2 chassis definition assigns no integrated Mainframe and no integrated sensors.
- Systems chassis definition assigns the generated Mainframe and six directional sensor definitions.
- Systems build contains seven `System.*` computer installations.

### Confidence boundary

Confirmed from serialization/source/manual evidence:

- V2 selection on both spawners;
- Craft Lab selection conflict;
- V2 has no Mainframe/tasks/sensors/computers;
- adapter returns neutral while Mainframe is absent;
- console produces exactly two pages for V2;
- sensor and computer architecture described above;
- scheduler has no execution callbacks;
- direct physical path works in the existing direct-Tick test.

Not freshly live-verified in this pass:

- the full systems build accelerating from simulated scene-owned W input;
- exact runtime values during an in-Play-Mode rebuild;
- ten pages and six observation streams after explicitly changing Handling Track to systems.

Those three checks should become automated serialized-scene Play Mode tests after the immediate build-selection/input repairs.
