# Hovercraft V3 Complete Architecture Audit

**Audit date:** 2026-08-01  
**Project:** Slingshot 2.0  
**Unity:** 6000.5.0f1  
**Primary build requested for review:** `build.apex.v3.systems_integration.balanced.01`  
**Overall verdict:** **Architecture intact with identified prototype debt**

The core rule still holds: normal player-facing motion reaches the one non-kinematic craft Rigidbody through installed thrusters, installed aerodynamic fins, passive chassis aerodynamics, authoritative world gravity, and PhysX contacts. No handling controller directly writes velocity, snaps the body to a surface, or applies an uninstalled control force.

The current kite-like behavior is nevertheless real and mostly explainable. The strongest confirmed contributor in the available Track Test evidence is not weak gravity. It is continued, physically applied surface-following force while the recorder already calls the craft “losing surface”: bottom hover force reaches 3.4–4.5 MN and roof capture reaches about 2.35 MN, against a craft weight of about 114 kN. Once all physical surface probes are lost, those forces clear. Four fins then remain a large, orientation-dependent aerodynamic system: at the recorded peak dynamic pressure their fixed-coefficient model can produce about 0.42 MN in aggregate. The fin model has no angle-of-attack or stall law and produces its full configured lift coefficient even at its nominal angle. This is a physically applied but simplified aerodynamic model and a credible kite-like-flight contributor.

## Actual architecture map

```text
Scene-owned World Simulation Root (-2000 FixedUpdate)
  -> authoritative clock, gravity, atmosphere, wind, zones
  -> craft-local V3WorldEnvironmentProvider
       -> ForceMode.Acceleration gravity on the one Rigidbody
       -> local samples for chassis aero, fins, and sensors

Scene-owned Pilot Input Adapter
  -> V3ControllerPipeline.FixedUpdate (-1000)
  -> Mainframe.TickObservations
       -> Intent Bus + direct chassis observation + six physical sensor tasks
       -> software power/capacity grants
  -> Mainframe/ControlRouter resolves installed-software authority
  -> cached outputs from scheduled system runtimes
       -> shared controller mathematics (Drive/Hover/Stabilizer/Traction)
       -> Aerodynamics actuator targets
  -> V3ActuatorCommandRouter / wrench allocation
  -> firmware availability + systems/propulsion power + thermal/output limits
  -> installed thrusters AddForceAtPosition
  -> installed fins AddForceAtPosition
  -> passive chassis aero AddForceAtPosition
  -> PhysX contacts
  -> one non-kinematic Rigidbody
  -> post-physics recorder (read-only observation/export)

Parallel/bypass paths, explicitly classified:
  * Legacy fallback, only when Mainframe scheduled control cannot run:
      Pipeline -> shared controllers -> actuator router -> physical devices.
    This is a compatibility path, not active on the complete systems build.
  * Hover surface probes are controller-owned physical SphereCasts. They are
    intentionally separate from the six directional sensor/Observation-Bus devices.
  * Chassis state and several control algorithms read the Rigidbody directly;
    the six directional sensors currently support readiness/telemetry/diagnostics,
    not the hover control law.
  * Track spline/section/centerline truth flows to diagnostics/race infrastructure,
    not to craft control.
  * Reset and parity utilities may write pose/velocity, but are explicit recovery/test paths.
```

## Executive answers

| # | Direct answer |
|---:|---|
| 1 | **Yes.** V3 is one non-kinematic Rigidbody with one chassis collider. Installed part definitions determine mass, COM, devices, force origins, firmware, power demand, and actual output. |
| 2 | **No normal control system can bypass installed output hardware.** Controllers submit requests. Thrusters/fins/chassis aero/world gravity/PhysX are the normal motion authorities. |
| 3 | Normal sites are thruster `AddForceAtPosition`, fin `AddForceAtPosition`, chassis-aero `AddForceAtPosition`, world-provider `AddForce(..., Acceleration)`, and PhysX contacts. Recovery and parity utilities are the only craft pose/velocity writers. Optional world-root `Physics.gravity` synchronization is global configuration and is disabled in all three development scenes. |
| 4 | **Yes architecturally, but not necessarily well bounded.** Curvature and roof capture use real collider probes and real installed thrusters through allocation/power/firmware/thermal. The 80 m capture envelope and force caps are tuning/product-model concerns, not hidden adhesion. |
| 5 | **Control uses colliders only.** Track spline, centerline, section, expected-airborne and ideal-frame data are confined to diagnostics, the race bridge, editor tooling and tests. |
| 6 | **Yes.** Seven `IV3CraftSystemRuntime` implementations exist and execute. Drive, Hover, Stabilizer and Traction wrap shared controller mathematics; Pilot, Aerodynamics and Telemetry have their own scheduled logic. |
| 7 | **Yes.** The scheduler owns system callback cadence and operational state. |
| 8 | **Yes.** Granted frequency drives an accumulator and real callbacks. 30/60/120 Hz produce distinct execution counts. |
| 9 | **Conditionally yes.** Seven physical computer prefabs and `V3SystemComputerRuntime`s exist with mass, software, compute/task state and console/recorder state. Their physical part power and thermal profiles are placeholders; software power is metered centrally. |
| 10 | **Yes.** Six chassis-mounted visible runtime sensor prefabs perform real raycasts, sample local environment/airflow and publish observations. They are not just Mainframe records. |
| 11 | One scene-owned `V3WorldSimulationRoot` using `Assets/HovercraftV3/Content/World/EarthStandard.asset` defines gravity, atmosphere, temperature, density, wind and thermal environment. Three scene zones modify wind/temperature where entered. |
| 12 | **Exactly once when the world is valid.** The provider disables `Rigidbody.useGravity` and applies sampled gravity as acceleration. If world sampling fails, it restores the original built-in-gravity state. Development scenes do not globally synchronize `Physics.gravity`. |
| 13 | Chassis aero is airflow-, density-, speed-squared- and craft-orientation-derived. It never reads track truth. It is not hidden track adhesion. Its constant craft-down term can become world-up when the craft is inverted. |
| 14 | Confirmed causes/contributors: very large physical hover/roof forces retained while a collider remains within the 80 m envelope; high upward/departure velocity in recorded transitions; large fixed-coefficient fin forces; and potentially over-responsive automatic inertia/torque behavior. Incorrect gravity and Rigidbody drag are disproven. |
| 15 | **Mixed.** Gravity and post-probe-loss flight are physically consistent, and contact-free force residuals are only single-digit/tens of newtons. The long capture range and the fin model’s full lift without angle-of-attack/stall are prototype/tuning defects that can create an unnatural kite feel despite using legitimate physical force sites. |
| 16 | Hover/surface values are embedded per `CraftBuildDefinition` and survive Play Mode. Many product, controller and software values remain shared definition assets. |
| 17 | Independent variants are technically possible but the workflow is incomplete. `CopySelectionsFrom` deep-copies embedded hover configuration but shares chassis, parts, software and Router assets. `Content/Builds` is empty. |
| 18 | **Yes for generated builds.** Canonical/system generators call `CreateOrLoad` and rewrite generated build identity, selections and hover configuration. An authored asset outside generated ownership would not be targeted, but no guarded authored-variant workflow exists. |
| 19 | **Mostly yes.** Builder filtering and runtime assembly use `CraftBuildValidator`; socket/connector category, family, size, mass, force, power/data services, chain limits and catalog membership fail closed. Functional `PartRole`, firmware and required-system compatibility are not general compatibility dimensions. |
| 20 | The recorder is broad enough to prove force ownership, world state, sensors, tasks, requests, power and actual output. It does **not** by itself supply the missing controlled clean-ballistic and failure-path comparison runs. Its first dynamics sample and takeoff/landing event semantics have defects described below. |
| 21 | Claims of only two force-authority sites are obsolete after world gravity and chassis aero were added. Claims that all reset history clears, that current handling tests passed in Unity, that persistent authored variants are complete, and that the latest report proves clean airborne behavior are not accurate. |
| 22 | **Architecture intact with identified prototype debt.** No material physical-authority violation was found. The debt is concentrated in aerodynamic fidelity, surface-capture bounds, reset state, authored-variant ownership, hardware-state depth, diagnostics semantics and current end-to-end verification. |

## Audit method and confidence boundary

The audit used the authority order requested: current source/serialized state, current Unity compilation/log evidence, raw recorder samples, repository Unity result XML, then handoffs/reports. No scene, asset, tuning value or generator was saved or invoked by this audit. The only intended project write is this report.

Evidence inspected included all three development scenes, active/generated build assets and metadata, chassis/part/software/firmware/world assets, project physics settings, runtime/editor/test source, four recent `V3_TrackTest` report packages, current `Library/ScriptAssemblies` timestamps, the project `Logs/Editor.log`, and repository test XML.

Confidence labels used below:

- **Fresh static/current:** current source, YAML, metadata, build settings or current editor log.
- **Recorded live:** the four 60-second `V3_TrackTest` packages created 2026-08-01 in Unity 6000.5.0f1.
- **Historical Unity test:** repository XML dated 2026-07-29, before the world/handling changes.
- **Documented only:** a report claims a run but its result artifact is not in the current repository.
- **Not freshly verified:** behavior needing a new controlled Play Mode run.

Important limitations:

- The project was open in Unity process 120072, with worker processes also active. Starting a second Unity instance would have risked the live project and was not done.
- The current project log shows successful assembly reloads and no `error CS` entries; V3 runtime/diagnostics DLLs are dated 13:36 and editor/test DLLs 14:49. This is compilation evidence, not test success.
- The newest repository Unity XML is from 2026-07-29. The physical-handling handoff explicitly says its new Unity tests were not executed because the project was open.
- No clean-ballistic, normal-failure-path matrix, or rebuild/variant live run exists in the four latest packages. Those required runs remain outstanding.
- The requested latest GDD was not found by filename or content in the project; only references to “GDD v0.5” exist. GDD conformance beyond the supplied architectural rule is therefore not verifiable.
- Several requested document paths do not exist under those exact names. The physical-handling handoff is under `Assets/HovercraftV3/`; the recorder/readability/Mainframe reports use longer current filenames. Those actual files were read.
- `Docs/Complete_Project_Folder_Structure.md` is dated 2026-07-29 and contains none of the later Track Test/world/recorder additions. It is useful historical inventory, not a current inventory.

During the audit the active Unity editor imported/regenerated `Hovercraft_Baseline.asset` at 14:38. Before that import, the serialized Track Test GUID had no matching on-disk metadata; afterward GUID `a2946245bc904c19a0827a4308c729be` resolved correctly. Conclusions use the final current on-disk state. That asset change was external to this read-only audit.

## Current project and scene state

| Scene | Effective selected build | Generated/authored | Runtime authority and environment | Track/recorder/UI state | Assessment |
|---|---|---|---|---|---|
| `Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity` | `ApexV3_SystemsIntegration.asset`; `build.apex.v3.systems_integration.balanced.01` | Generated | One spawner, assembler, input adapter and `world.root.v3_craftlab`; spawner and assembler refs agree; preview defaults off | One recorder, console, HUD/debug; three zones. Scene is extremely large and contains serialized track geometry as well as lab/handling content | Valid systems-craft laboratory, but no longer a small focused scene and not the cleanest track-test authority |
| `Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity` | `ApexV3_SystemsIntegration.asset`; primary systems build | Generated | One spawner/assembler/input and `world.root.v3_handlingtrack`; refs agree; preview off | Physical handling course, one recorder, one console, three zones; console/controls start closed | **Best current scene for primary systems-build handling validation** |
| `Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity` | `Hovercraft_Baseline.asset`; `build.hovercraft.baseline.01` | Generated | One spawner/assembler/input and one world root; baseline GUID is currently valid; preview off | Cached 48.23 km Track Test, one recorder, console, race bridge and three zones | **Best full-track forensic scene, but it does not assemble the requested primary gimballed systems build** |

`ProjectSettings/EditorBuildSettings.asset` enables `SampleScene`, `V3_HandlingTrack`, and `V3_TrackTest`; Craft Lab is not in build settings. `SampleScene` is not a V3-authoritative development scene.

The spawner is the development-scene build authority. In `Awake` and `OnValidate`, `V3CraftTestSpawner` calls `assembler.SetBuild(selectedBuild)` and marks it spawner-controlled. A stale assembler fallback can be serialized, but it is overwritten before normal assembly. Current scene references agree.

No active scene uses `ApexV3_SystemsIntegration2.asset`. That asset is nevertheless a defect/risk: it duplicates stable ID `build.apex.v3.systems_integration.balanced.01`, is in the generated build folder, lacks the serialized `hoverConfiguration` block present in the current asset, and is not an intentional authored variant. GUID identity prevents immediate assembly ambiguity, but stable-ID telemetry/report selection is ambiguous.

The Track Test world root has serialized ID `world.root.` rather than a scene-specific stable ID. It is non-empty and therefore works, but is low-quality forensic identity. Craft Lab and Handling Track have useful stable IDs.

Scene generators can recreate their intended split: systems build for the systems scenes and fixed-adapter baseline for Track Test. This is not obsolete, but it means there is currently no canonical full-track scene for the requested gimballed systems build. The generated baseline and primary build share the same 80 m hover/roof configuration, sensors, systems and aero; they differ principally in rear propulsion connector and 60 kg of mass.

## Current craft build

Authoritative asset: `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset` (GUID `70217178c6d526f41827ae2c1f83eff8`).

| Item | Current value/evidence |
|---|---|
| Chassis | `chassis.apex.v3.systems_integration.01`; base mass 8,520 kg; base COM `(0, -0.65262157, 0.004151838)` m |
| Calculated systems-build mass | 8,520 kg chassis + 3,155 kg installed connector/endpoint mass + 4.5 kg six sensors = **11,679.5 kg** |
| Rigidbody | One dynamic body; interpolation enabled; Continuous Dynamic collision; no constraints; runtime mass/COM overwritten by `V3CraftMassCalculator` |
| Collider | One `BoxCollider`, size `(3.6, 1.55, 7.8)` m, center zero, default physics material |
| Inertia | Unity automatic tensor from the one chassis collider; installed part locations change mass/COM but not compound collision shape/tensor geometry |
| Propulsion/braking | Rear gimbal + 480 kN main thruster with 1.5 normal multiplier; front brake/reverse thruster |
| Surface hardware | Four spring-mounted bottom hover thrusters, four roof thrusters, four lateral thrusters |
| Aero hardware | Four powered rotary actuators + four 0.8 m² fins |
| Core/cooling | One Energy Core and one active cooling module |
| Computers/software | Seven distinct computer assets/prefabs, seven software definitions and seven system runtimes/tasks |
| Sensors | Six integrated directional sensor definitions/prefabs, 0.75 kg each |
| Router | `router.balanced.01`, normal domain weights 1.0 |
| Hover config | `hover.apex.systems.01` v2: target 8 m; minimum 1 m; operational range 80 m; 0.1 m probes; 0.35 m fallback; strength 0.45; damping 0.12; gravity compensation ×1; curvature 1.1/12/cap 340 m/s²; roof 7/5/cap 180 m/s²/deadband 0.35 m |
| Chassis aero | 10 m², Cd 0.015; downforce area 8 m², coefficient 0.005; CoP `(0, 0.15, 0.25)` m; Mach drag curve 1.0 to 1.4 |
| World | Earth Standard profile; scene-owned, not build-owned |

The latest Track Test recorder snapshots are the fixed-adapter baseline: **11,619.5 kg**, calculated/applied COM `(-0.001807, -0.475617, -0.041037)` m, inertia tensor `(63885.21, 71480.52, 17502.93)` kg·m² and nearly identity tensor rotation. Replacing the baseline 60 kg fixed adapter with the systems build’s 120 kg gimbal gives the verified 60 kg mass difference. A fresh runtime snapshot of the systems build’s COM/inertia was not captured in this audit.

## Architecture vision compliance

| Layer | Actual current implementation | Compliance |
|---|---|---|
| Product definition | Shared ScriptableObject definitions for chassis, connectors, endpoints, computers, sensors, software, firmware, Router, world | Intact; many are generated prototype products |
| Build configuration | `CraftBuildDefinition` selects chassis/catalog/installations and embeds hover config | Intact, but generated ownership is unsafe for tuning |
| Installation calibration | Socket transforms, connector child mounts, force origins and local axes in prefabs/definitions | Intact |
| Runtime hardware | One hierarchy of instantiated part prefabs; one Rigidbody/collider | Intact |
| Firmware | Thruster/gimbal/fin-actuator/cooling/sensor firmware gates rates/safe state; passive fins need no actuator power to experience air load | Intact with simplified/global grant paths |
| Software/system | Seven executable runtimes; scheduler callbacks; cached commands | Intact with shared-controller wrappers |
| Physical output | Forces only at installed device/CoP sites plus gravity/contacts | Intact |
| World/craft truth separation | World sample, sensors, hover probes, belief and track diagnostics are separate channels | Intact; control bypasses the directional sensor bus in several places |
| Failure behavior | Missing/under-rate tasks stop scheduled system physics; missing world fails to built-in gravity and invalid aero/sensors; build validation fails closed | Intact with incomplete hardware damage/thermal depth |

No material violation of the installed-parts rule was found.

## Physical authority audit

| Site | File/method | Operation | Normal gameplay? | Physical source | Status |
|---|---|---|---|---|---|
| Installed thrusters | `Runtime/Parts/Thrusters/RuntimeThrusterInstance.cs`, `ApplyPowerGrant` | `Rigidbody.AddForceAtPosition(..., ForceMode.Force)` | Yes | Installed thruster at serialized local force origin/direction, after spool/power/thermal limit | Compliant |
| Installed fins | `Runtime/Parts/Aerodynamics/RuntimeAerodynamicFinInstance.cs`, `ApplyAerodynamicForce` | `AddForceAtPosition` | Yes | Density/airflow/speed², installed fin area/coefficient/axis at fin CoP | Compliant force site; simplified aero law |
| Chassis aero | `Runtime/Parts/Aerodynamics/V3CraftAerodynamicsRuntime.cs`, `ApplyReferenceSample` | `AddForceAtPosition` | Yes | Passive chassis drag/downforce at chassis CoP | Compliant |
| World gravity | `Runtime/Parts/Sensors/V3WorldEnvironmentProvider.cs`, `BeginPhysicsTick` | `AddForce(GravityVector, ForceMode.Acceleration)` and `useGravity=false` | Yes | Scene world profile | Compliant |
| PhysX | Unity solver/colliders | Contact impulses | Yes | Physical collider/material/solver | Compliant |
| Optional global gravity sync | `V3WorldSimulationRoot.SynchronizeGravityIfRequested` | `Physics.gravity = profile.GravityVector` | Configuration only; off in all V3 scenes | Global world setting | Compliant but unnecessary for current V3 bodies |
| Recovery | `V3FreeDriveSession.ResetCraft` | body position/rotation/linearVelocity/angularVelocity writes; sleep/wake; sync transforms | Explicit reset only | Recovery utility | Allowed and recorder-notified |
| Parity harness | `V3ParityRunController.ResetCraft` | pose/velocity writes | Regression only | Test harness | Allowed |
| Camera | `V3FreeDriveCamera` | camera transform writes | Yes, presentation only | Camera | Not craft authority |

There are no normal runtime `AddTorque`, `MovePosition`, `MoveRotation`, craft-root transform snap, `isKinematic` write, or controller velocity write sites. Surface alignment converts desired angular acceleration through the actual inertia tensor to a requested wrench, then allocates that wrench across eight real bottom/roof thrusters. The controller does not apply the torque itself.

No helper in Diagnostics applies force or changes craft state. The recorder adds a component with collision callbacks when a session begins, but that component has no collider/Rigidbody and only consumes callback data.

Force attribution is strong in contact-free windows. At latest-run samples 1816–1864, with zero contact estimate, ledger residual magnitudes were typically 2–19 N while device forces were in the meganeuton range. This is direct evidence against an unrecorded control force. Aggregate residual statistics are not reliable at contacts: sample 0 contains a 348.76 MN contact estimate while derived acceleration is intentionally zero on its first history sample. That one sample contributes 53.3% of the reported summed residual magnitude and lowers the residual mean from 109.1 kN to about 50.9 kN if removed. The remaining contact-window residual includes Unity solver timing/impulse-estimation effects.

## World physics and environment

| Setting | Current value | Authority |
|---|---:|---|
| World root count | Exactly one active root in each inspected V3 development scene | Scene YAML + `V3WorldQueryService` |
| Profile | `world.earth_standard`, configuration v1 | `EarthStandard.asset` |
| Gravity | `(0, -9.80665, 0)` m/s² | World profile |
| Project fallback gravity | `(0, -9.81, 0)` m/s² | `ProjectSettings/DynamicsManager.asset` |
| Atmosphere | Standard lapse model, 15 °C and 101,325 Pa at sea level, custom reference density 1.225 kg/m³ | World profile/field |
| Wind | Global speed 0; deterministic wind seed 314159; turbulence strength/gust strength 0 | World profile |
| Zones | Crosswind +22 m/s, hot-zone temperature operation +35 setting, tunnel wind multiplier 0.2 | Three scene zone components |
| Thermal environment | Enabled; baseline cooling ×1, min 0.1, max 5, reference airspeed 100 m/s | World profile |
| Units | Unity metres/seconds, kg, N, Pa, °C/K | Source/assets consistently use explicit unit names |
| Fixed timestep | **0.01 s / 100 Hz**, not 50 Hz | `ProjectSettings/TimeManager.asset` |
| Maximum allowed timestep | 0.33333334 s | TimeManager |
| Solver iterations | 6 position / 1 velocity | DynamicsManager |
| Contact offset | 0.01 m | DynamicsManager |
| Max depenetration velocity | 10 m/s | DynamicsManager |
| Sleep threshold | 0.005 | DynamicsManager |
| Max angular speed | 50 rad/s | DynamicsManager |
| Auto Sync Transforms | Off; reset explicitly calls `Physics.SyncTransforms` | DynamicsManager/reset utility |
| Collision mode | Continuous Dynamic | Chassis prefab |
| Rigidbody damping | Linear 0, angular 0 | Chassis prefab |

`V3WorldQueryService` requires exactly one active root in the craft’s scene. Zero roots, a missing profile or duplicate roots returns an invalid sample. On an invalid sample the environment provider restores the original `useGravity` state (true on the chassis prefab), so gravity does not disappear. Chassis aero stops, fin samples have zero density/default state, sensors publish invalid environment state, and Mainframe readiness degrades. More than one root cannot double-apply custom gravity because resolution fails rather than choosing both.

Sensors and fins request the same root at their own positions; chassis aero and gravity use the provider’s cached current-tick reference sample; telemetry and recorder read those same runtime states. The overload `V3DirectionalSensorRuntime.Tick` accepting raw airflow/density is a test/compatibility entry point and is not the assembled sensor-device path.

All loops, walls, inverted sections and free flight retain world-down gravity. Track orientation never rotates gravity. This is the correct world rule and explains why surface-normal support and gravity can point in very different directions inside loops.

## Gravity audit

Gravity is not the source of the floaty symptom:

- Latest live report: mean gravity 113,944.7 N, peak 113,948.4 N on a 11,619.5 kg baseline, equal to `m × 9.80665` within sampling/float precision.
- All 5,999 aggregate samples had a valid world; recorded gravity was constant world-down.
- The provider uses `ForceMode.Acceleration`, so gravitational acceleration is mass-independent.
- Built-in gravity is disabled only while a valid custom sample exists and is restored on failure.
- `synchronizeUnityGravity` is false in Craft Lab, Handling Track and Track Test, preventing project-global side effects.
- There is no gravity cancellation force outside actual thruster/aero outputs.

Fresh clean-ballistic acceleration with every device/aero contribution neutral was **not run**, so a direct measured `-9.80665 m/s²` isolated fall remains required. Available contact-free samples do prove `observed ≈ gravity + recorded device/aero forces`; they do not prove an aero-disabled ballistic case.

## Mass, COM, inertia, and scale

The systems build is approximately 11 tonnes as designed: 11,679.5 kg. The baseline report mass of 11,619.5 kg is also correct for its 60 kg lighter fixed rear adapter. `V3CraftMassCalculator` applies both `Rigidbody.mass` and explicit `Rigidbody.centerOfMass`; its calculation includes connector and endpoint masses, all seven computer masses, and six integrated 0.75 kg sensors. The latest live snapshot reports calculated COM and Rigidbody COM as exactly equal.

Computer masses are 22 kg each except Telemetry at 18 kg (150 kg total). Sensor mass is 4.5 kg total. All other installation masses were resolved from the active build; there was no evidence of duplicate physical part instances.

The main physical limitation is inertia fidelity. The Rigidbody uses an automatic inertia tensor derived from one `(3.6 × 1.55 × 7.8) m` box collider. Installed part positions affect COM but do not create compound collider mass geometry. The historical baseline tensor has much lower roll-axis inertia (`z ≈ 17,503`) than pitch (`x ≈ 63,885`) or yaw (`y ≈ 71,481`). This can make roll response comparatively quick and contribute to a kite-like rotational presentation under fin/offset-thruster torques. It is **possible**, not proven as the primary cause; controlled Test D was not run.

The collider uses the default project material. Track colliders/materials determine contacts. Project/world scale is consistently metres; the 48.23 km track, 8 m hover target, 80 m probe range, 7.8 m chassis and recorded 490 m/s peak are numerically consistent. No 10×/100× scale error was found.

Editor preview calls the same build validator, assembler/socket hierarchy and mass/COM calculator, removes Rigidbodies, disables colliders/behaviours, and is EditorOnly. Tests and prior reports cover parity, but current systems-build preview-vs-runtime COM was not freshly exercised.

## Off-track floatiness investigation

### Available controlled-test status

| Requested test | Evidence available | Status/conclusion |
|---|---|---|
| A — clean ballistic fall | No current run with hover, stabilization, fins and chassis aero all neutral/disabled | **Not testable from current packages.** Gravity force is correct; a clean acceleration measurement is still required. |
| B — off-track coast | Six recorded `Takeoff` transitions and raw per-tick forces from latest full report | **Partially satisfied.** Events are not literal takeoff/contact events; raw samples reveal the actual mechanism. |
| C — normal failure paths | Unit/integration source covers under-rate/missing software and device limiting; no comparative live packages | **Not freshly live-verified.** Architecture paths exist; handling comparison absent. |
| D — rotation/inertia | Inertia conversion unit test source, live inertia/torque channels, no controlled applied-torque run | **Possible contribution, not isolated.** |

### Raw event trace

The latest package `2026-08-01_122119_BenchmarkRun_e5e1b6e15bc0` reports six Takeoff and six Landing events. Raw data changes their interpretation:

- Sample 1816, immediately before Takeoff: world state `HoveringNearSurface`, true distance 7.82 m, hover belief grounded at 1.32 m, separation 6.62 m/s, bottom hover **4.467 MN**, roof 18.2 kN, aero 345.9 kN, gravity 113.95 kN.
- Sample 1817, Takeoff: state changes to `LosingSurface`, true distance 8.46 m, but hover belief remains grounded. Bottom hover remains **4.466 MN**; residual is 12 N.
- Sample 1842: true distance 34.39 m, probes still grounded, bottom hover 3.201 MN and roof **0.905 MN**. The craft speed is 447 m/s.
- Sample 1864: probes finally lost, bottom hover is zero; one-tick remaining roof force is 18.8 kN, aero 335.6 kN, gravity 113.95 kN.
- Sample 1865 is labeled Landing by the event detector even though contact force/count are zero. It entered `OutOfTrackBounds`, which the detector treats as “not airborne.”

A second transition is even clearer:

- Sample 5717 Takeoff: true distance 8.91 m, hover belief grounded at 1.29 m, bottom hover **3.398 MN**, roof 0.682 MN, speed 433 m/s.
- Sample 5730: true distance 20.57 m, believed distance 13.01 m, bottom hover 0.659 MN, roof **2.354 MN**, aero 343.0 kN.
- By sample 5741, all bottom/roof output is zero; aero is 345.4 kN and gravity remains 113.95 kN.

This is not untracked adhesion: residuals are 6–18 N and every force is attributed to installed devices. It is a long physical capture envelope with very high product headroom.

### Candidate diagnosis

| Candidate | Status | Evidence |
|---|---|---|
| Upward/departure velocity | **Confirmed contributor** | Recorded transitions have world vertical velocities of 44–149 m/s and high surface-normal separation. |
| Stale surface belief | **Confirmed while probes/fallback still find a collider; possible after reset** | `IsGrounded` means ≥1 hit within 80 m, not contact. When all hits are lost, forces clear. `lastGroundNormal` is not reset and can direct a remembered-normal fallback cast after reset. |
| Continued bottom support | **Confirmed major contributor** | 3.4–4.5 MN at recorded Takeoff transitions, about 30–39× weight in magnitude, normally directed along sensed track normal. |
| Roof capture | **Confirmed major contributor** | Up to 2.35 MN in sampled departure windows and 3.09 MN package peak. It is real downward/capture thrust relative to craft/surface, not necessarily world-down. |
| Stabilizer | **Possible secondary contributor** | Surface alignment requests real wrench allocation up to speed-stiffened caps while a surface is believed. No clean on/off flight comparison. |
| Chassis aero lift/downforce | **Disproven as sole cause; possible small contributor** | At peak q, configured downforce is only ≈5.8 kN (~0.05 weight) and drag ≈30 kN with max compressibility. Orientation can turn craft-down into world-up. |
| Fin lift/drag | **Confirmed significant contributor** | Four fins each reach ≈104 kN; package peak aggregate aero 427.7 kN. Full Cl applies without AoA/stall. |
| Excessive aerodynamic drag | **Possible contributor to “hanging” visual** | Aggregate aero mean 204 kN and peak 428 kN; most comes from fins. It slows/reorients flight, but gravity remains present. |
| Rigidbody drag/angular drag | **Disproven** | Both serialized damping values are zero. |
| Incorrect/duplicate/cancelled gravity | **Disproven** | Correct constant world gravity and single application recorded. |
| Incorrect mass | **Disproven** | Calculated and Rigidbody mass agree; acceleration response remains force/mass. |
| Inertia/COM | **Possible rotational contributor** | Explicit COM is plausible; automatic single-box inertia has low roll component and omits installed mass geometry. |
| World scale | **Disproven** | Serialized dimensions, ranges, track lengths and units are internally consistent. |
| Recovery | **Not the flight cause; reset transient risk confirmed** | Resets are explicit and recorder-marked. Mainframe/system cached commands and `lastGroundNormal` are not fully reset. |
| Track geometry | **Possible trigger** | Three normal-discontinuity events, crashes and large offsets exist. Current failure-point collider continuity was not freshly visually/physically inspected. |
| Sensor fallback | **Confirmed contributor to extended surface availability** | A real 0.35 m SphereCast can find a surface up to `range + target = 88 m`; missed corner probes project to that hit plane. |
| Hidden force residual | **Disproven in contact-free windows** | Residual 2–19 N against MN forces. Contact/first-sample aggregates are not clean. |
| Camera/presentation | **Possible perception contributor only** | Chase camera smoothing cannot affect craft motion; no A/B visual test was run. |

## Chassis aerodynamics

`V3CraftAerodynamicsRuntime.ApplyReferenceSample` uses point velocity at the chassis CoP minus local air velocity, `q = 0.5 ρ v²`, a drag vector opposite airflow, and a constant `-transform.up` downforce. It applies the sum at `(0, 0.15, 0.25)` m, producing a physical torque from the CoP/COM lever arm.

| Property | Value/behavior |
|---|---|
| Reference area / Cd | 10 m² / 0.015 |
| Downforce area / coefficient | 8 m² / 0.005 |
| Angle dependency | Drag follows airflow; downforce follows craft down. No AoA lift curve or stall. |
| Speed law | Density × speed², with Mach drag multiplier 1.0–1.4 |
| Clamp | Non-negative asset values/curve; no explicit force cap |
| Active state | Only with valid atmosphere sample; continues in free flight |
| Track dependency | None |
| Recorder | Chassis drag/downforce/side, total chassis+fin force and torque are present in world channels; device records expose fins |

At the recorded q=144,685 Pa, chassis downforce is about 5.79 kN and maximum-curve drag about 30.38 kN. These are modest relative to 113.95 kN weight. Chassis aero cannot by itself explain multi-meganeuton capture or prolonged support. It can produce positive world lift when craft-down points upward and can add attitude torque, but the current magnitude makes it a secondary contributor.

There is no double counting in application: chassis and fins each apply once. `V3CraftAerodynamicsRuntime.State` aggregates them for telemetry/recorder; the ledger uses that aggregate instead of adding fin device records again.

## Fin aerodynamics

Each installed `Balanced Active Fin` has area 0.8 m², lift coefficient 0.9, drag coefficient 0.08, local lift axis `(0,-1,0)` and local CoP zero at the fin transform. Its powered rotary actuator moves around local X between ±25° at 120°/s and 480°/s², has a 10,000 N·m declared load, and uses `firmware.stock_finactuator_firmware` (120 Hz requested, 30 Hz minimum).

The Aerodynamics system schedules at 60 Hz. Above 120 m/s it commands a neutral high-speed target of `-6°`, plus left/right yaw differential up to ±10°. This means fins remain deliberately deflected in free flight even with neutral yaw.

The more important shortcut is the force law: lift is always `q × area × Cl` along the projected local lift axis. There is no chord/AoA calculation despite a serialized chord axis, no zero-lift angle, no induced drag, no stall and no coefficient variation with actuator angle. At the latest run’s peak q, each fin’s theoretical lift term is about 104.2 kN; four fins can exceed 3.6× craft weight in aggregate force. The device summary confirms ≈50 kN mean per fin and 104–105 kN peaks.

This is fully physical in application and visible in diagnostics, but it is not a credible final aerodynamic surface model. It is the strongest aerodynamic explanation for kite-like free-flight response. Passive air loading itself correctly remains when actuator/software power is unavailable; power/firmware controls the fin angle, not whether air can act on a physical fin. Fin thermal state and structural load are placeholders and do not limit the current force.

## Surface following and roof capture

Actual current flow:

```text
Four installed bottom-thruster force origins
  -> physical SphereCast (0.1 m, TrackSurface layer, up to 80 m)
  -> optional physical fallback SphereCast from COM (0.35 m, up to 88 m)
  -> plane projection for corner probes that missed the same collider-derived plane
  -> average distance/normal/normal velocity and normal history
  -> signed curvature acceleration from sensed normal rotation and tangential speed
  -> positive curvature + gravity compensation + per-probe height/damping -> bottom request
  -> distance/separation + negative-curvature magnitude -> roof request
  -> actuator/wrench Router -> priority allocation -> firmware/power/thermal/spool
  -> four bottom and/or four roof thrusters at real force origins
```

`V3HoverController` calculates signed curvature. Positive signed curvature contributes only to bottom support; negative signed curvature is not automatically roof capture by itself but is added as a non-negative term to the roof-capture acceleration. Distance above target and separating velocity can also request roof capture on flat/positive curvature.

Probe disagreement is handled by averaging all hits; each bottom thruster retains its own distance/normal-velocity height correction. There is no robust outlier rejection. With one hit, the craft is “grounded,” although curvature history requires at least two hits. A fallback surface can fill corner misses using a collider-derived plane.

When all probe evidence is lost, `hasSurfaceNormalHistory` clears, signed tracking acceleration becomes zero, automatic bottom support becomes zero and roof capture becomes zero. The next Router frame clears old requests. A one-tick physical/output lag can remain because the scheduler publishes cached commands after physics application and the thruster spool has state.

Fallback remains physical, but its range and plane extrapolation are permissive. It never reads a track spline or ideal normal. The remembered `lastGroundNormal` cannot directly generate a force; it is only a direction for a new real SphereCast. However, `ResetDynamicState` does not reset `lastGroundNormal`, so a reset can try the pre-reset surface direction if the primary craft-down cast misses. This contradicts the handoff’s claim that all relevant surface history clears.

Automatic hover cannot enter emergency overload. Only the pilot command passed to `ActuatorRouter.BeginFrame` enables overload. Bottom and roof requests are clamped to each installed thruster’s `NormalOutputMultiplier`. The current hover/roof product definitions serialize both normal and overload multiplier as 7, so 1.12 MN per device is treated as normal product capability. This multiplier was raised as a handling fix and currently functions as both a product specification and tuning headroom; that ownership should be separated in future work.

The current smart roof use is architecturally compliant. The defect is envelope/fidelity, not authority.

## Directional sensors

All six definitions use the visible `V3_DirectionalSensor_Prototype.prefab` with `V3DirectionalSensorDeviceRuntime`, attached under `Electronics/Chassis Sensors` at chassis mounts named `Sensor.<Direction>`. Assembly fails if a required mount is absent; there is no silent root fallback. Each is 0.75 kg, has 40 m range, uses a **Raycast** (`castType: 0`), ignores triggers, requests explicit systems power and uses `firmware.stock_directionalsensor_firmware`.

| Sensor | Axis from mount | Requested/min/max Hz | Latest measured/granted | Latest accuracy evidence |
|---|---|---:|---:|---|
| Front | `mount.forward` | 120 / 15 / 120 | 120 / 120 | 660 hits; 0.965 m mean comparable error |
| Rear | `-mount.forward` | 60 / 15 / 120 | 60 / 60 | 871 hits; 1.334 m mean error |
| Left | `-mount.right` | 60 / 15 / 120 | 60 / 60 | 4,961 hits; 0.287 m mean error |
| Right | `mount.right` | 60 / 15 / 120 | 60 / 60 | 4,625 hits; 0.349 m mean error |
| Top | `mount.up` | 60 / 15 / 120 | 60 / 60 | No hits in latest run; valid samples remained present |
| Bottom | `-mount.up` | 120 / 15 / 120 | 120 / 120 | 5,280 hits; 0.027 m mean error |

Each sample computes craft point velocity, hit-body point velocity and their true relative velocity; local airflow is `environment.LocalAirVelocity - body.GetPointVelocity(sensor origin)`. It publishes `EnvironmentSurface`, `EnvironmentAtmosphere` and `SystemHealth` observations with timestamps/confidence/rates. Mainframe readiness requires every sensor to publish successfully and remain online. The console has a Sensors page and the recorder captures device sample, age, grant, hit truth, airflow and comparison against an independent non-allocating world query.

These sensors are distinct from the four hover probes. The hover controller does not consume their Observation Bus output; it casts directly from installed hover force origins and reads Rigidbody/environment state directly. Drive, traction, stabilization and aerodynamics likewise mostly read cached command/Rigidbody state rather than directional observations. Thus the sensors are currently meaningful physical readiness/telemetry/diagnostic devices but are mostly unused by control. That is an intentional prototype separation, not a fake-device defect, but it falls short of a sensor-driven onboard-control vision.

## Hover probes

The four bottom hover probes are not installed sensor parts and do not have independent firmware/power/sample scheduling. They are controller-owned physics queries colocated with installed bottom-thruster force origins. Their samples, hit/fallback flags, distance and normals are recorded separately under socket IDs. This is a legitimate control sensor shortcut, but should not be conflated with the six chassis devices.

The latest package reports 5,333–5,337 hits per hover probe and nearly all samples comparable. The diagnostic comparison reports zero error for hover probes because it is comparing/exporting the same controller probe snapshots, not an independent six-device measurement. Independent track truth remains separately recorded.

## System computers

All seven build installations instantiate distinct prefabs under `Cockpit/System Computers/System.<Role>`. Each prefab has `RuntimePartInstance`, `V3SystemComputerRuntime` and the matching system runtime component. Each computer definition bundles one software definition and exposes 300 compute units/s and one task slot.

| Role | Computer ID / mass | Software / requested-min Hz | Runtime classification | Main output |
|---|---|---|---|---|
| Pilot Interface | `computer.pilotinterface.balanced.01`, 22 kg | `software.pilotinterface.stock`, 120/60 | Dedicated runtime | Intent Bus command cache/publication |
| Drive | `computer.drive.balanced.01`, 22 kg | `software.drive.stock`, 60/30 | Shared controller wrapped by runtime | Propulsion/braking requests; power-mode cycle |
| Hover | `computer.hover.balanced.01`, 22 kg | `software.hover.stock`, 120/60 | Shared controller wrapped by runtime | Bottom/roof/surface alignment requests |
| Stabilizer | `computer.stabilizer.balanced.01`, 22 kg | `software.stabilizer.stock`, 120/60 | Shared controllers wrapped by runtime | Gimbal/vector/yaw/attitude requests |
| Traction | `computer.traction.balanced.01`, 22 kg | `software.traction.stock`, 120/60 | Shared controller wrapped by runtime | Lateral/yaw traction requests |
| Aerodynamics | `computer.aerodynamics.balanced.01`, 22 kg | `software.aerodynamics.stock`, 60/30 | Dedicated scheduled algorithm | Fin actuator target angle |
| Telemetry | `computer.telemetry.balanced.01`, 18 kg | `software.telemetry.stock`, 30/10 | Dedicated runtime | Live telemetry refresh |

Installed hardware and software both matter. Scheduler discovery starts from installed computer runtime parts; missing/mismatched software/runtime faults validation or leaves no executable task. Capacity and power determine granted rate. Under-minimum tasks are not executed and `ApplySystemPhysics` refuses their controller authority. A task cannot be operational until it has an executable callback, at least one execution, no fault and a rate at/above minimum.

Prototype shortcut: computer part definitions have `requiresPower: false`, zero part-level power and thermal disabled. Software task power (5 W for 120 Hz roles, 3.5 W for 60 Hz, 2.75 W for Telemetry in the recorded run) is centrally metered and computer runtime state reflects task grant. This is meaningful compute/software state, but not a complete physical computer electrical/thermal/damage model.

## System runtimes

All seven system folders are meaningful. None is definition/task-only and none bypasses its system runtime on the complete build. The shared controllers remain the actual controller-math owners for Drive/Hover/Stabilizer/Traction; the scheduled runtimes cache the last scheduled command and invoke those controllers during the next physics application. This is explicit hybrid ownership, not duplicated simultaneous authority.

The Pilot runtime is scheduled but the same current pilot command also reaches `FilterCommandByInstalledSoftware` before Pilot’s newly scheduled callback; Pilot operational presence gates the Router profile. Aerodynamics contains the fin target algorithm directly. Telemetry calls the hub. Mainframe contains allocation/ordering/readiness infrastructure and direct chassis/environment sampling, not the domain control laws.

No per-system `Update` or `FixedUpdate` loop was found. The one controller pipeline FixedUpdate is the physics entry point.

## Scheduler execution

`V3SoftwareScheduler.Execute` is a real callback scheduler. It accumulates fixed-step time, calls `SystemRuntime.ScheduledTick(interval)`, records execution/failure/duration, allows up to four executions per task and 24 globally per physics step, and counts/clamps skipped backlog. Tasks are deterministically sorted by survival rank and installed-list order for equal ranks.

The current project is **100 Hz physics**, although source defaults to 50 Hz only when a zero delta is passed. A 120 Hz task therefore runs once most ticks and twice on the required fraction; 60 Hz and 30 Hz skip physics ticks. If physics were 50 Hz, 120 Hz would perform two or sometimes three bounded virtual callbacks per step.

Latest 60-second live task evidence:

| Task | Requested/granted/measured Hz | Execution delta | Skip delta | Mean/peak recorded last-callback cost | Final accumulator | State |
|---|---:|---:|---:|---:|---:|---|
| Pilot | 120/120/120.007 | 7,200 | 0 | 0.145/1.2 µs | 6.394 ms | Operational/published |
| Hover | 120/120/120.007 | 7,200 | 0 | 0.073/0.3 µs | 6.394 ms | Operational/published |
| Stabilizer | 120/120/120.007 | 7,200 | 0 | 0.068/0.2 µs | 6.394 ms | Operational/published |
| Drive | 60/60/60.006 | 3,600 | 0 | 0.075/0.4 µs | 6.394 ms | Operational/published |
| Traction | 120/120/120.007 | 7,200 | 0 | 0.060/0.3 µs | 6.394 ms | Operational/published |
| Aerodynamics | 60/60/60.006 | 3,600 | 0 | 0.190/0.8 µs | 6.394 ms | Operational/published |
| Telemetry | 30/30/30.003 | 1,800 | 0 | 24.144/301.3 µs | 23.087 ms | Operational/published |

Callback-duration evidence is the last callback observed per physics sample, not a profiler-grade inclusive average. It is adequate to show these callbacks are real and bounded.

One-tick latency is deliberate: `SubmitScheduledControls` applies cached system physics, resolves devices, then `TickManagedDevices` calls the scheduler near the end of the tick. Newly published scheduled commands take effect next physics tick. The direct-controller fallback executes only if Mainframe cannot submit scheduled controls; it is not also executed on the complete build.

## Mainframe and controller pipeline

Normal path by method/timing:

1. `V3PilotInputAdapter` reads Input System state and publishes an authoritative command.
2. `V3ControllerPipeline.FixedUpdate` calls `Tick` once per physics step.
3. `V3CraftMainframe.TickObservations` advances craft tick, asks the environment provider to apply gravity/sample world, publishes intent/chassis observations, computes task/sensor/firmware power grants, and ticks six sensors.
4. `FilterCommandByInstalledSoftware` uses the Control Router and scheduler operational state to remove unavailable domains.
5. `SubmitScheduledControls` begins a fresh actuator frame and invokes cached Stabilizer, Drive, Hover and Traction system physics only if their scheduled roles are operational.
6. `V3ActuatorCommandRouter.Resolve` combines channel requests, requests power and applies actual thruster forces.
7. `TickManagedDevices` powers/moves fin actuators/cooling, runs scheduler callbacks, updates readiness, applies chassis aero and then fin aero.
8. PhysX integrates contacts and motion.
9. The recorder captures after `WaitForFixedUpdate` at default execution order 10000.

The Mainframe genuinely owns buses, system discovery, task/sensor/firmware power allocation, readiness, execution order, managed fin/cooling devices and aero aggregation. The pipeline owns one physics entry and legacy fallback. The scheduler owns callback cadence/state, not the physical application moment. Shared controllers own domain math. This is understandable but Mainframe is an 859-line high-coupling coordinator and should be watched as prototype God-class debt.

Systems do not continue through the legacy path merely because individual software is offline: as long as Mainframe as a whole is operational, under-minimum roles are skipped. If the entire Mainframe path cannot submit, the pipeline compatibility fallback may use capability-registry providers. That fallback preserves old/V2 operation and is an architectural bypass that should remain explicitly test-scoped or be policy-gated for future production builds.

Reset is incomplete: `V3ControllerPipeline.ResetDynamicState` clears shared controllers, router/spool, gimbal and thermal state, but it does not reset Mainframe scheduler accumulators, task execution state, latest routed command or each system runtime’s cached command. The first post-reset physics application can therefore use a pre-reset cached command before the scheduler republishes. This is a confirmed latent transient.

## Router, power, firmware, and thermal

| Command | Real chain | Bypass/failure result |
|---|---|---|
| Main propulsion/brake | Drive runtime -> Drive controller -> actuator channels/priorities -> thruster firmware -> propulsion power tier -> thermal/spool -> force at installed origin | Missing/under-rate Drive removes request; grant scales actual output; no direct force |
| Bottom hover | Hover runtime -> physical probes/controller -> BaseHover + Stabilization channels -> Router -> critical/stability power -> thruster firmware/thermal/spool | No surface/no Hover task clears automatic support; manual lift still requires Hover authority and hardware |
| Roof capture | Hover runtime -> roof stabilization/manual channels -> Router -> physical roof thrusters | Clamped to normal multiplier; no automatic overload |
| Strafe/yaw | Stabilizer/Traction -> Vector/Traction controllers -> wrench/channel allocation -> four lateral thrusters | Missing roles remove relevant authority; allocation residual recorded |
| Gimbal | Stabilizer -> gimbal controller -> powered firmware-limited actuator orientation -> main thruster force follows real transform | Missing firmware/power holds safe state; no separate torque force |
| Aero fin | Aerodynamics callback -> cached `-6° + yaw` target -> powered rotary actuator/firmware -> passive fin force at current transform | Loss of aero/firmware returns actuator target to 0; passive air load remains physically valid |
| Cooling | Mainframe -> firmware grant -> requested module power -> cooling output -> part thermal state | No power/firmware means no active cooling |

Controllers cannot bypass actuator allocation for thrusters. The passive aero forces and gravity correctly do not pass through the thrust Router. Fin angle actuation is power/firmware limited; fin air load is passive. Chassis aero is passive. Thermal output limit multiplies the actual thruster command before power demand/grant and force.

Denied/limited behavior is visible in request, Router, allocation, grant, firmware, thermal and actual-device recorder channels. Latest report: 2,557 samples were meaningfully power-limited; zero device samples were thermally derated; there were no task skips/faults.

Physical product limits and craft tuning are technically distinct: force/multipliers live on thruster products; hover gains/range live on the build; Router weights live on a Router Profile. In practice the hover/roof `NormalOutputMultiplier = 7` was introduced as a handling correction, so the current product asset is carrying a tuning patch. Generators, runtime and recorder now agree on 7, but older documents that assumed nominal 1× capacity are obsolete.

Damage/detachment and most structural/thermal behavior remain deferred. Several computer, fin and connector thermal profiles are disabled. This is prototype scope, not silent enforcement.

## Track/craft boundary

The track provides collider meshes/materials, cached/generator section and centerline data, curvature/frame/expected-jump metadata, race infrastructure and recorder mapping. The craft-control source tree has no reference to `TrackGenerator`, section ID, spline, centerline, expected airborne, intended speed or ideal track normal.

Track data consumers found in V3 are:

- `V3TrackDiagnosticAnalyzer` and report/event logic;
- `V3TrackRaceCraftBridge` and free-drive recovery/race placement;
- editor Track Test generation/cache tooling;
- tests and UI/overlays that display recorded state.

Surface following works on any collider in layer 8 reachable by the installed thruster/fallback casts. Walls, loops and corkscrews are followed from sensed collider normals and actual point motion. The fallback is still collider-derived.

The latest report contains three >35° one-tick independent normal-discontinuity events, 33 sensor-miss events, four crashes, large lateral offsets and repeated bottoming/rollover locations. This is evidence that track mapping/collider geometry deserves targeted inspection, but not proof of a collider defect at a specific section. No fresh visual collision-continuity sweep was performed. Craft Lab’s large embedded track geometry makes it a poor clean lab baseline and increases the chance of misleading scene content, although it does not feed control truth.

## Persistent tuning and variants

| Value class | Current owner | Per build? | Regeneration/variant consequence |
|---|---|---:|---|
| Hover height/gains/range/curvature/roof/alignment | Embedded `V3HoverConfiguration` inside build | Yes | Deep-copied by `CopySelectionsFrom`; rewritten for generated builds |
| Part force, spool, normal/overload, power, thermal | Shared part definitions | No | Affects every build referencing product |
| Drive/stabilizer/traction controller gains | Runtime source/default serialized component fields in generated prefab | Generally shared | Not an independent build tuning object |
| Chassis aero/COM/base mass | Shared chassis definition | No | Shared across primary/baseline builds |
| Fin coefficients/actuator limits | Shared fin/actuator definitions | No | Shared across variants |
| Software rates/cost/power | Shared software definitions | No | Shared across variants |
| Router weights | Shared Router Profile | No | Shared across variants |
| Connector/endpoint choices | Build installations | Yes | Safely different per build if asset identity is unique |
| World | Scene root/profile/zones | No, scene/world | Global environmental content |
| Track metadata/materials | Track generator/cache/scene | No, track | Not craft tuning |
| Runtime accumulator/spool/thermal/history | Runtime instances | No | Must reset; never intended to persist |

The persistent-authoring vision is **partial**.

- The builder can create a blank `CraftBuildDefinition` at a user-selected path, edit serialized selections/configuration with Undo, mark dirty and save assets.
- It can reset the current build to the V2 reference and rebuild a selected assembler.
- It does not offer “Duplicate/Create Authored Variant from selected build,” default to `Content/Builds`, protect generated assets, show generated ownership, or provide explicit Revert.
- It has no deliberate runtime-to-authored applyback workflow.
- `Assets/HovercraftV3/Content/Builds/` is empty.
- Embedded hover configuration is deep-copied. Chassis, product, software, firmware and Router objects are shared references.
- Both current primary and Track Test baseline assets are in generated ownership and can be rewritten by regeneration.
- `ApexV3_SystemsIntegration2.asset` demonstrates the identity/migration risk: duplicate stable ID, stale serialized shape, no scene consumer.

Two properly duplicated authored build assets can own independent embedded hover values without changing each other. They cannot independently tune shared fin/chassis/software/Router products unless those products/profiles are also intentionally duplicated and referenced. Canonical generators do not target arbitrary assets outside their hardcoded generated paths, so an authored asset under `Content/Builds` would be safe from direct overwrite; the UI does not establish or enforce that safety.

## Compatibility architecture

`CraftBuildValidator` is used before runtime assembly. Craft Builder calls the same public connector/endpoint validation functions to filter choices before selection and displays incompatible existing/manual selections with actionable codes/messages. Editor preview uses runtime assembly/validation, and tests cover the resolver.

Current dimensions:

- parent socket allowed connector kinds and allowed direct endpoint categories;
- socket family and part-compatible parent families;
- size class;
- parent and connector-chain mass/force limits;
- power/data availability and maximum demand;
- connector child endpoint category/family/services;
- catalog membership;
- exactly one Energy Core and Cockpit plus provided/required capabilities;
- prefab presence, no child Rigidbody, and connector child-mount presence.

A spring cannot be offered for rear propulsion unless that socket explicitly allows the spring connector kind and the chain satisfies family/category/limits. The current rear socket/product choices use gimbal/fixed adapter; hover sockets allow spring. A hand-edited invalid build fails closed with an error.

Limitations: `PartRole` and product-family strings are descriptive rather than general compatibility gates. Firmware kind, installed software and required system roles are validated later by Mainframe/runtime readiness rather than at every part-choice edge. Compatibility is family/category/capability based, not individual pair exception based, which is the correct direction but not the full functional-role vision.

## Recorder integrity

The recorder is read-only with respect to control and physics. It captures post-physics on the main thread into bounded, preallocated arrays. There is no capture-time file I/O, LINQ or reflection. Export begins only after recording stops and yields between bounded batches. Concurrent recording/export is rejected. Drops and event overflow are explicit.

Latest package evidence:

- 6,001 samples / 60.01 s at nominal 100.000 Hz; zero dropped samples/events;
- capture overhead 729.8 ms total, about **0.122 ms/sample** or 1.2% of a 10 ms fixed-step budget if evenly distributed;
- export 28.38 s after capture;
- NDJSON 706.9 MB, event context 35.0 MB, compact binary 1.79 MB;
- world valid throughout, Mach peak 1.442, q peak 144,685 Pa;
- all seven task rates measured exactly as granted, no skips/faults;
- full request/device/power/firmware/thermal/force channels present.

It distinguishes world truth, track truth, six sensor observations, hover probe snapshots, craft belief, pilot intent, system requests, Router allocation, requested/granted/actual device output, gravity, chassis/fin aero and contact estimate. Reset notifications create attempts and mark the first post-reset sample as a discontinuity, which report aggregates exclude.

Confirmed recorder defects/limits:

1. `V3DiagnosticSchema.Version` is 5 while `VersionText` is still `"4.0"`; manifests report both values. This is a schema identity inconsistency.
2. The first session sample has no previous-velocity history, so observed acceleration is zero, but it is not marked/excluded as a dynamics discontinuity. In the latest run that creates a 348.76 MN residual and dominates aggregate residual statistics.
3. `Takeoff` means transition into `LosingSurface`, which can occur with probes still grounded and multiple MN of hover thrust. It is not physical loss of support/contact.
4. `Landing` means leaving any of the three “airborne” enum states. Transition to `OutOfTrackBounds` emits Landing even with zero contacts, as sample 1865 proves. The Markdown’s six Landings cannot be read as six physical impacts.
5. Contact force is solver impulse/fixed delta and contact torque uses one average point. Contact/overlap windows can have large residuals and are estimates.
6. Full Forensic storage is very large; four one-minute packages occupy multiple gigabytes. Compact binary is efficient, but default NDJSON/event context are expensive.
7. The latest packages all cover `build.hovercraft.baseline.01`, not the requested primary systems build.
8. There is no controlled clean ballistic/failure-mode metadata or a channel that asserts a deliberately disabled chassis-aero state. Device/world channels are sufficient if such a run is created.

Raw-to-report trace is otherwise sound: sample 1817 contains state 3, distances/belief, device forces and 12 N residual; `events.csv` references sample 1817 as Takeoff; `event_context.ndjson` indexes the surrounding samples; `report.md` counts the event. The defect is event semantics, not data loss.

## UI and diagnostics

The scene-owned runtime UI is consolidated:

- F1 toggles the pilot HUD through `V3FreeDriveSession`.
- F2 toggles physical debug display state.
- F3 toggles controls; it starts closed in all scenes.
- F4 toggles one `V3SystemsConsoleController`; it starts closed.
- F7 starts/stops recording; F8 marks; F9 exports.
- O/P use Input System actions to navigate ten pages; I minimizes.
- warning/status, input authority and power mode are runtime-derived.

When a Mainframe exists, the registry always adds all ten complete-build pages, so an offline role remains visible rather than disappearing. Pages read actual Mainframe/tasks/buses/telemetry/parts/sensors. The console refreshes at 12 Hz and does not publish commands.

The Hover page shows configuration, grounded/probe count, average normal, distance/separation, curvature/alignment and roof target output, and lists actual bottom-hover part output/power/temperature. It does **not** list the four roof (`Control.*`) devices on the Hover page and does not present a clean requested -> allocated -> granted -> actual roof/bottom table. The recorder does. The UI therefore only partially satisfies the requested forensic clarity.

The header/page model shows craft name, system/manufacturer/status and a craft summary. Input route/Mainframe/sensor/computer/task counts are available across relevant pages, not guaranteed in one always-visible summary. Page count is navigation state rather than a prominent vehicle-health datum.

Cursor/UI suspension changes local input authority but keeps the one adapter enabled; it publishes neutral/suspended state rather than leaving a stale second input source. Debug/world/thruster views read state only. Reset and recorder hotkeys are deliberate utilities; debug display itself does not alter simulation.

## Performance

Static/runtime evidence shows bounded execution:

- 100 physics steps/s.
- Six directional sensors: 120+120+4×60 = 480 scheduled raycasts/s at full grants.
- Hover: four primary SphereCasts every physics step (400/s) plus up to one primary and one remembered-direction fallback per step (100–200/s). Plane projection adds no cast.
- Recorder independent sensor truth adds non-allocating casts while recording; track truth adds bounded non-allocating raycasts/search.
- Directional sensors and hover probes duplicate some surface work intentionally because they are different devices/ownership paths. Control does not reuse the six-device samples.
- Chassis state is sampled centrally but several controllers still read Rigidbody motion directly; this duplicates simple reads, not physics queries.
- Scheduler callback loops are capped at 4/task and 24/global per physics step. Latest run had no skips.
- Only pipeline, world root, telemetry hub, thermal controller, warning presentation and recorder/presentation services have centralized loops; seven system runtimes have no independent loops.
- UI refresh is 12 Hz and cached. Recorder capture has no file I/O and measured no drops.

The recorder’s ~0.122 ms/sample overhead is acceptable for current development runs but is not zero. Full NDJSON export is storage- and time-heavy. A Unity Profiler capture of actual fixed-step distribution, GC allocation and worst-case overlapping-zone/track costs was not available; source inspection cannot replace it.

## Test coverage

Repository results:

- `RuntimeSystemsRepair-Final.xml` (2026-07-29): 108 discovered, 107 passed, 0 failed, 1 opt-in skip.
- `RuntimeSystemsRepair-SerializedScenes.xml`: 2/2 passed.
- `RuntimeSystemsRepair-EnforcedParity.xml`: 1/1 passed, 78.28 s.
- `ReadabilitySprint-Final.xml`: 98 discovered, 97 passed, 0 failed, 1 skip.

Later documents claim an isolated-project 127/126+1 recorder run and world-focused runs, but their XML is not present in the current repository. The handling pass added tests in `V3HandlingControllerTests.cs` but explicitly did not execute Unity tests in this project. Current ScriptAssemblies compile and reload; that is not Unity test success.

| Behavior | Unit/source test | Integration test | Serialized-scene test | Recorded live evidence | Current confidence |
|---|---|---|---|---|---|
| No direct controller force/velocity | Static/reflection test source | Repair suite historical | Indirect | Strong force residual/ledger | High |
| Real input -> propulsion force | Some direct controller tests bypass input | `V3SerializedScenePlayModeTests` source | Historical 2/2 | Track Test live packages | High for baseline; not current systems build |
| Six physical sensors | Sensor unit/runtime tests | Repair focused historical | Historical scenes | Six live rates/hits/errors | High |
| Seven callbacks/cadence | Scheduler unit/integration | Repair focused historical | Historical scene path | Exact 30/60/120 live counts | High |
| World gravity exactly once | World provider/source tests | New world tests documented | No current XML | Constant valid gravity in live package | High for valid-world baseline; failover not live |
| Chassis/fin aero | Formula tests/source | Documented later suite | No current XML | Full live aero forces | Medium-high |
| Surface curvature/roof chain | New unit/source tests | Physical roof test directly invokes setup/controller | No current XML after change | Live MN bottom/roof force | High authority, medium intended behavior |
| Clean ballistic fall | None proving serialized player path | None | None | None | Missing |
| Normal failure-path flight matrix | Scheduler/device unit tests | No current comparative run | None | None | Missing |
| Reset discontinuity | Unit/source and documented focused run | Track infrastructure report | Claimed 1/1 artifact absent | Two attempts marked live | Medium-high; cached-command reset blind spot |
| Build/variant persistence | Definition/Builder source tests | Readability tests historical | Preview contracts historical | No live rebuild variant package | Partial |
| Track collider continuity | Track generator tests/reports | Cached smoke documented | Artifact absent | Normal-discontinuity events only | Partial |
| Recorder export/read-only | Extensive diagnostics unit source | Documented 16/16 | Historical/claimed | Four successful packages, no drops | High with semantic defects |
| Parity | Unit/source | Enforced parity | Harness | Historical XML, not post-handling | Historical only |
| GUID/meta health | Static audits claimed | N/A | N/A | Current targeted GUIDs resolve | Full current audit not rerun |

Many unit tests call controller methods, build temporary GameObjects, set Rigidbody state, or call `pipeline.Tick` directly. Those tests correctly prove math/contracts but bypass some combination of real Input System, serialized scene, Mainframe scheduling, power, firmware and post-physics integration. They must not be cited as end-to-end evidence. The four live recorder packages are the strongest current end-to-end evidence, but only for the baseline build and uncontrolled high-speed driving.

### Required live validation status

| Required run | Status |
|---|---|
| Flat-ground systems check | Historical/live report start contains flat hover and full systems, but sample 0 is a collision/residual discontinuity; not a clean current primary-build run |
| Loop/descending section | Latest baseline reports capture curvature, bottom/roof, power, actual output and distance; useful and analyzed |
| Off-track free flight | Partially captured, but event semantics and ongoing surface capture prevent a clean ballistic phase comparison |
| Rebuild/variant check | Not run; current asset state reveals duplicate stable ID and no authored build workflow |

## Test blind spots

- No current serialized-scene run proves the primary systems build across flat ground, loop/descending track, clean off-track ballistic flight and rebuild in one controlled sequence.
- Controller-focused tests often call math or pipeline entry points directly, so they do not prove the complete Input System -> scheduler -> Router -> power/firmware -> physical-device chain.
- The live reports use the baseline build at uncontrolled extreme speed; they prove force attribution and cadence but do not isolate clean gravity, individual system failure paths or primary-build behavior.
- No current controlled torque experiment validates the automatically derived single-box inertia tensor against the assembled craft's expected angular response.
- Recorder tests do not catch the semantic mismatch between state-transition Takeoff/Landing events and literal support/contact transitions, or the first-sample residual contamination.
- Build-definition tests do not prove a safe user-facing authored-variant workflow, regeneration guard or deliberate runtime-to-asset applyback path.
- Current track evidence does not localize collider continuity/orientation at the three reported surface-normal discontinuities.

## Documentation claim audit

| Claim | Source | Current implementation/runtime evidence | Status |
|---|---|---|---|
| Physical-device-only handling; no kinematic driving | AI handoff, repair/readability/Mainframe reports | Current authority search and tiny contact-free residuals support it | **Accurate** |
| Exactly two normal force-authority sites | Mainframe/readability/recorder reports | World gravity and chassis aero are now additional legitimate normal sites | **False/outdated** |
| Surface following uses no track truth | Physical handling handoff | `V3HoverController` uses only Physics casts, colliders, body/environment | **Accurate** |
| Roof capture is physical, not adhesion | Physical handling handoff | Real Control.* thrusters through Router/power/firmware | **Accurate** |
| Six physical sensors | Repair/AI handoff | Six prefabs/devices, real raycasts, live rates/errors | **Accurate** |
| Seven physical computers and executable systems | Mainframe/repair/AI handoff | Seven prefabs/runtimes/callbacks; live execution counts | **Accurate with prototype power/thermal conditions** |
| Scheduler cadence is real | Repair/AI handoff | Accumulator callbacks and exact live 30/60/120 Hz deltas | **Accurate** |
| Mainframe readiness proves sensor/task operation | Repair/Mainframe report | Requires successful sensor sample and task operational state | **Accurate** |
| All system logic is owned by dedicated runtimes | Mainframe report wording | Four runtimes wrap shared controllers; Mainframe still coordinates physics application | **Partially accurate** |
| All dynamic surface history clears on reset | Physical handling handoff | `lastGroundNormal`, scheduler accumulators and cached system commands are not reset | **False** |
| Persistent per-build tuning | Physical handling handoff | Embedded hover config is per build; many other values shared; active assets generated | **Accurate with conditions** |
| Authored variants are ready | Readability “future Content” implication / requested vision | Empty `Content/Builds`, no duplicate/guard/applyback/revert workflow | **False if read as complete** |
| Canonical regeneration is safe for current tuning | Handoffs advise regeneration | Generators explicitly rewrite generated build config/selections; primary build is generated | **Misleading** |
| Chassis aero is physical/world-backed | World handoff/physical handling handoff | Current source and recorded aero confirm | **Accurate** |
| Gravity is authoritative and once | World handoff | Valid-world source and recorded constant gravity confirm | **Accurate with built-in fallback condition** |
| Latest reports prove off-track flight | Recorder/handling handoffs | Raw reports prove forces but event takeoff/landing semantics are not physical contact events; no clean ballistic test | **Misleading** |
| Recorder schema is current/consistent | Recorder report says schema 4.0; handoff says schema 5 | Code/manifest are integer 5 and text 4.0 | **False/inconsistent** |
| Reset samples do not contaminate aggregates | Track repair | Explicit resets are excluded; initial session sample is not and dominates residual | **Partially accurate** |
| Full post-handling Unity tests passed | Physical handling handoff | It explicitly says no Unity test claim; current XML predates changes | **False if claimed elsewhere** |
| Current project inventory is complete | `Complete_Project_Folder_Structure.md` | Dated before Track Test/world/recorder files and lacks them | **False/outdated** |
| Latest GDD conformance | Requested evidence | Latest GDD not found | **Not verifiable** |

## Documentation corrections

Documents requiring correction or addendum:

1. `Docs/Hovercraft_V3_AI_Agent_Handoff.md`: update force-authority count, 100 Hz project timestep, current Track Test baseline split, generated tuning risk and latest test boundary.
2. `Docs/Hovercraft_V3_Forensic_Dynamics_Recorder_Implementation_Report.md`: replace “two force sites,” reconcile schema 5/4.0, document first-sample and event-state semantics.
3. `Docs/Hovercraft_V3_Project_Readability_EditorPreview_SystemsConsole_Report.md`: replace two-site claim; clarify generated vs authored builds and current Craft Lab size/content.
4. `Docs/Hovercraft_V3_Mainframe_Systems_Integration_Implementation_Report.md`: replace two-site claim; clarify wrapper runtimes and physical-computer power/thermal placeholders.
5. `Assets/HovercraftV3/Hovercraft_V3_Physical_Handling_UI_Handoff.md`: correct reset-history claim, distinguish “grounded” probe envelope from contact, and add that its Unity tests were still unexecuted at audit time.
6. `Docs/Hovercraft_V3_Track_Test_Infrastructure_Repair.md`: clarify recorder Takeoff/Landing semantics and Track Test `world.root.` identity.
7. `Docs/Complete_Project_Folder_Structure.md`: regenerate only in a separately authorized task after the current migration settles.

## Final subsystem assessment

| Subsystem | Classification | Reason |
|---|---|---|
| World physics | Intact | One authoritative scene root/profile/clock/zones |
| Gravity | Intact | Correct, once, fail-safe fallback |
| Atmosphere | Intact with prototype shortcuts | Consistent standard field; simple zones/no weather strength in current profile |
| Track boundary | Intact with prototype shortcuts | Control isolated from track truth; current collider continuity not fully verified |
| Rigidbody authority | Intact | No hidden kinematic/direct controller path |
| Thrusters | Intact | Real origins, firmware/power/thermal/spool |
| Hover | Intact with prototype shortcuts | Physical, but “grounded” is an 80 m sensor envelope and force headroom is extreme |
| Roof capture | Intact with prototype shortcuts | Physical and well-attributed; permissive/strong envelope |
| Stabilization | Intact with prototype shortcuts | Physical wrench allocation; sensor bus mostly bypassed |
| Traction | Intact with prototype shortcuts | Scheduled wrapper over shared controller |
| Aerodynamics | Partial drift | Physical authority intact, but fin law is not AoA/stall based and coefficients act at full lift |
| Sensors | Intact with prototype shortcuts | Real devices; mostly not control inputs; no thermal/damage model |
| Computers | Intact with prototype shortcuts | Real runtime/mass/software state; part-level power/thermal placeholder |
| Software systems | Intact | Seven executable runtimes, four wrapper-owned algorithms |
| Scheduler | Intact | Real, bounded, measured callbacks |
| Mainframe | Intact with prototype shortcuts | Genuine coordinator but highly coupled; reset incomplete |
| Router | Intact | Requests/weights/wrench allocation visible and enforced |
| Power | Intact | Meaningful task/firmware/device grants and starvation |
| Firmware | Intact with prototype shortcuts | Real gating, some global grant aggregation |
| Thermal | Intact with prototype shortcuts | Thruster path real; many prototype parts have thermal disabled; damage absent |
| Persistent tuning | Partial drift | Hover per-build, most tuning shared, active asset generated |
| Authored variants | Missing/partial | Technical assets possible; workflow/content/guardrails absent |
| Compatibility | Intact with prototype shortcuts | Shared fail-closed resolver; role/firmware/system semantics incomplete |
| Recorder | Intact with defects | Broad/read-only/live; first-sample and event/schema semantics need repair |
| UI | Intact with prototype shortcuts | Consolidated/live; Hover page lacks complete bottom+roof request/grant/actual chain |
| Tests | Not verified/currently partial | Strong historical/source coverage; no post-handling full Unity XML or controlled flight matrix |

## Confirmed strengths

- One dynamic craft body and collider; no kinematic or velocity-driving controller.
- All normal forces map to explicit physical/environment/contact authorities.
- Roof capture and curvature following preserve the intended installed-parts chain.
- Track truth does not leak into craft control.
- Six physical sensors and seven physical computers/system callbacks are real.
- Scheduler rates are real simulation mechanics, not display values.
- Power, firmware, thermal/spool and installed capacity limit actual thruster output.
- World gravity/atmosphere/wind are centralized and consistently recorded.
- Contact-free recorder residuals strongly support complete force attribution.
- Recorder, systems console, input ownership and scene spawner are consolidated and understandable.
- Compatibility fails closed and is shared across builder/runtime paths.

## Confirmed defects

1. Fin lift is constant-coefficient and independent of angle of attack/stall; serialized chord axis is unused in force calculation.
2. Surface capture can remain active anywhere within an 80 m primary range/88 m fallback reach and produce multi-meganeuton support/capture forces.
3. `ResetDynamicState` does not clear `lastGroundNormal`, Mainframe scheduler/cached command state or system runtime cached commands.
4. Recorder Takeoff/Landing events describe enum transitions, not literal support loss/contact. An OutOfBounds transition can produce a false Landing with zero contact.
5. First recorder sample is used in dynamics aggregates despite deliberately zero acceleration history, badly contaminating force residuals.
6. Recorder schema integer/text versions disagree (5 versus 4.0).
7. `ApexV3_SystemsIntegration2.asset` duplicates the primary stable build ID and has stale serialized build shape.
8. Track Test world root identity is malformed/low-quality (`world.root.`).
9. There is no current full-track scene for the requested primary systems build; latest live packages are the fixed-adapter baseline.
10. Current post-handling Unity tests are not represented by repository XML.

## Prototype shortcuts

- Controller-owned hover probes rather than installed sensor/firmware devices.
- Directional sensor observations mostly unused by control.
- Four system runtimes wrap shared controller classes and use one-tick cached commands.
- Computer physical part power/thermal models are placeholders; software power is central.
- Passive fins have no structural/thermal force limiting; actuator firmware/power controls only angle.
- Single-box automatic inertia ignores distributed installed-part geometry.
- Mainframe is a large coordinator with direct device/aero lifecycle ownership.
- Full-forensic recording is memory/storage heavy.
- Compatibility does not use functional role/firmware/system prerequisites at every selection boundary.

## Architectural drift

No material authority drift was found. Partial ownership drift exists where the design language suggests sensor-driven, independently owned systems but current controllers read the Rigidbody/environment/Physics directly and four scheduled runtimes are thin wrappers. Persistent authoring also drifts from the intended per-craft vision because critical aero/software/Router/controller tuning is shared and the active build remains generator-owned.

These are repairable ownership/fidelity issues, not a reason to discard the current architecture.

## Floatiness findings

The floaty/kite symptom is not mass, missing gravity or hidden Rigidbody damping. It is a combination of:

1. high departure velocity at extreme test speeds;
2. “grounded” meaning a physical surface is sensed within 80 m, not contact;
3. very large bottom and roof forces while that physical evidence persists;
4. surface normals that may point sideways/upward relative to world gravity on loops/curves;
5. four fins capable of several times weight in orientation-dependent aero force with no AoA/stall law;
6. potentially quick roll response from single-box automatic inertia;
7. camera smoothing as a possible perception amplifier, not a physics cause.

Once all probes are lost, automatic bottom/roof force clears and the recorded force ledger remains consistent with gravity plus aero/devices. Therefore the behavior is partly expected from the current serialized physical rules and partly a defect in their fidelity/bounds. It is not an architecture-violating invisible adhesion force.

## Prioritized recommendations

No recommendation below was implemented by this audit.

### 1. Critical architecture repair

1. Add an authoritative full dynamic reset API to Mainframe/scheduler/system runtimes and reset `lastGroundNormal`; test the first post-reset tick for zero stale authority.
2. Remove/quarantine `ApexV3_SystemsIntegration2.asset` after resolving whether it contains user-authored intent; enforce unique stable build IDs in validation.
3. Make generated ownership explicit and prevent Craft Builder from saving normal tuning into generator-owned assets without a warning/duplicate step.
4. Decide whether production-complete builds may ever use the pipeline’s Mainframe-less legacy fallback; gate and test that policy.

### 2. Vehicle/world-physics correction

1. Replace fin force with an AoA-based lift/drag/stall model using the serialized chord axis; retain physical CoP application and passive loading.
2. Design separate “near surface,” “captured surface,” and “contact” states. Do not tune until a clean ballistic baseline exists.
3. Review 80/88 m surface reach and 340/180 m/s² caps against intended loop radius/speed and installed product capability.
4. Run an explicit inertia-design pass using the real physical envelope/distributed part assumptions; do not blame mass itself.
5. Inspect track collider/normal continuity at the three recorded discontinuity windows.

### 3. Authoring workflow

1. Add **Create Authored Variant** that deep-copies embedded build tuning, generates a new stable ID and defaults to `Assets/HovercraftV3/Content/Builds/`.
2. Display Generated/Authored ownership, dirty state and overwrite risk.
3. Add explicit Revert and deliberate Apply Runtime Calibration actions with Undo and validation.
4. Decide which profiles are per-craft (hover, controller, aero, Router, software rates) and which are immutable product specs; duplicate/profile-reference accordingly.
5. Create a canonical full-track systems-build scene or make build selection an explicit test parameter rather than a scene fork.

### 4. Diagnostics improvement

1. Mark the first sample as a dynamics discontinuity or seed previous velocity before the first captured physics step.
2. Split event vocabulary into `SurfaceEnvelopeDeparture`, `ProbeLoss`, `PhysicalTakeoff`, `ContactLanding`, and `OutOfBounds`; require contact for Landing.
3. Reconcile schema version 5/4.0 and add a migration/compatibility note.
4. Add controlled-run metadata: device/system disabled state, test phase, expected neutral output and aero enable state.
5. Add separate chassis/fin rows to force summary and a contact-free residual aggregate.
6. Offer a compact default forensic profile or compressed streaming export; keep drop reporting.

### 5. Tuning work (only after the above measurements)

1. Run clean ballistic, aero-only, hover-off, stabilizer-off and fin-neutral comparisons through normal failure paths.
2. Tune capture gains/range and fin coefficients from force-to-weight and trajectory evidence, not feel alone.
3. Separate real thruster product rating from surface-controller requested authority; reconsider whether normal multiplier 7 is a product spec.
4. Validate at intended race speed as well as the current 300–490 m/s benchmark extremes.

### 6. Content expansion

1. Add authored craft variants with distinct profiles and manufacturer ownership.
2. Replace generic computer/sensor/console visuals while preserving runtime components and evidence surfaces.
3. Add track sections designed for clean edge, ballistic arc, inverted free flight and controlled landing validation.
4. Extend computer/sensor/fin thermal, damage and structural state only after current failure paths are testable.

## Files inspected

Primary documents:

- `Docs/Hovercraft_V3_AI_Agent_Handoff.md`
- `Assets/HovercraftV3/Hovercraft_V3_Physical_Handling_UI_Handoff.md`
- `Docs/Hovercraft_V3_Runtime_Systems_and_Sensor_Investigation_Report.md`
- `Docs/Hovercraft_V3_Runtime_Systems_Physical_Sensors_Scheduler_Repair_Report.md`
- `Docs/Hovercraft_V3_Forensic_Dynamics_Recorder_Implementation_Report.md`
- `Docs/Hovercraft_V3_Project_Readability_EditorPreview_SystemsConsole_Report.md`
- `Docs/Hovercraft_V3_Mainframe_Systems_Integration_Implementation_Report.md`
- `Docs/Hovercraft_V3_Track_Test_Infrastructure_Repair.md`
- `Docs/Complete_Project_Folder_Structure.md`

Representative source/assets (the audit also searched the complete V3 runtime):

- `Runtime/Core/Assembly/V3CraftAssembler.cs`, `V3CraftMassCalculator.cs`, `V3CraftRuntime.cs`
- `Runtime/Core/Build/CraftBuildDefinition.cs`, `CraftBuildValidator.cs`
- `Runtime/Core/Mainframe/V3CraftMainframe.cs`
- `Runtime/Core/Scheduling/V3SoftwareScheduler.cs`
- `Runtime/Core/Routing/V3ControllerPipeline.cs`, `V3ControlRouter.cs`, `V3ActuatorCommandRouter.cs`
- `Runtime/Core/Power/V3PowerDistributor.cs`
- `Runtime/Core/Firmware/V3DeviceContracts.cs`
- all seven `Runtime/Systems/**/*SystemRuntime.cs` and shared controller files
- `Runtime/Systems/Hover/V3HoverController.cs`
- `Runtime/Parts/Thrusters/RuntimeThrusterInstance.cs`
- `Runtime/Parts/Aerodynamics/RuntimeAerodynamicFinInstance.cs`, `V3CraftAerodynamicsRuntime.cs`
- `Runtime/Parts/Sensors/V3DirectionalSensorRuntime.cs`, `V3DirectionalSensorDeviceRuntime.cs`, `V3WorldEnvironmentProvider.cs`
- `Runtime/World/Core/V3WorldSimulationRoot.cs`, `V3WorldQueryService.cs`, world field/profile/zone files
- recorder core, sources, event detector, ledger, track analyzer, export/report files
- runtime UI/HUD/debug/session/spawner/track-bridge files
- Builder/assembler inspectors and generator/scene-generator source
- all three development scenes and current generated definitions/prefabs/builds
- `ProjectSettings/DynamicsManager.asset`, `TimeManager.asset`, `EditorBuildSettings.asset`
- current project `Logs/Editor.log`, `Library/EditorInstance.json`, V3 ScriptAssemblies
- four packages under `TestDriveReports/HovercraftV3/2026-08-01_*`
- repository XML under `TestResults/HovercraftV3/`

## Tests and commands run

This audit performed read-only/static commands and data analysis only:

- recursive `rg` authority/reference/identity searches over runtime/editor/tests/assets/scenes;
- YAML inspection of build, world, prefab, physics and scene serialization;
- GUID-to-asset resolution and mass summation (11,679.5 kg systems build);
- current Unity process/editor-instance and ScriptAssemblies inspection;
- current project log scan for imports, assembly reload and compiler errors;
- XML result parsing for latest repository test counts;
- CSV/JSON/NDJSON streaming analysis of task cadence/cost, force summaries and selected takeoff windows;
- source-to-event-to-Markdown recorder trace.

No Unity tests were started, no Play Mode scene was saved, no asset was regenerated by this audit, and no tuning/build selection was changed. The live editor’s baseline-asset import during the audit is recorded in the confidence boundary.

## Evidence appendix

### A. Latest live package identity

`TestDriveReports/HovercraftV3/2026-08-01_122119_BenchmarkRun_e5e1b6e15bc0`

- Unity 6000.5.0f1 WindowsEditor
- `V3_TrackTest`
- `build.hovercraft.baseline.01`
- 11,619.5 kg
- fixed 0.01 s
- 6,001 samples, zero drops
- 60.00996 s
- three attempts/two resets
- mean/max speed 315.25/490.00 m/s
- mean/peak hover force 1.948/4.475 MN
- mean/peak roof force 0.166/3.088 MN
- mean/peak aero force 0.204/0.428 MN
- gravity 0.113945 MN mean
- 2,557 power-limited samples, zero thermal derating
- six reported Takeoffs and six reported Landings, with semantic caveats above

### B. Force-authority proof points

- `RuntimeThrusterInstance.ApplyPowerGrant`: lines 132–139 apply only granted, thermally allowed spool output at the installed origin.
- `RuntimeAerodynamicFinInstance.ApplyAerodynamicForce`: lines 39–87 compute local point airflow/q/lift/drag and apply at CoP.
- `V3CraftAerodynamicsRuntime.ApplyReferenceSample`: lines 87–137 compute/apply chassis drag/downforce.
- `V3WorldEnvironmentProvider.BeginPhysicsTick`: lines 79–91 disable duplicate built-in gravity only on a valid sample, apply acceleration, otherwise restore fallback.
- `V3HoverController.Submit`: lines 213–239 are physical probe/fallback evidence; lines 296–443 calculate and submit only actuator requests.
- `V3ControllerPipeline.Tick`: lines 76–86 use Mainframe scheduled path; lines 89–145 are explicit compatibility fallback.
- `V3FreeDriveSession.ResetCraft`: lines 262–272 are the only player-facing pose/velocity writes.

### C. Configuration identity proof points

- Primary build: `stableId` line 15; embedded hover configuration lines 28–59.
- Duplicate stale asset: `ApexV3_SystemsIntegration2.asset`, same stable ID, different GUID, no serialized hover block.
- Track Test build: baseline GUID `a2946245bc904c19a0827a4308c729be`, now resolved by `Hovercraft_Baseline.asset.meta`.
- Scene roots: Craft Lab `world.root.v3_craftlab`; Handling `world.root.v3_handlingtrack`; Track Test `world.root.`.
- All scene roots serialize `synchronizeUnityGravity: 0`.

### D. Fresh verification boundary

The strongest current claims are supported by current source/assets and the live baseline recorder packages. The following must not be represented as freshly passed:

- full post-handling V3 Unity suite;
- serialized primary systems-build full Track Test;
- clean ballistic gravity test;
- normal failure-path hover/stabilizer/aero comparison;
- controlled torque/inertia test;
- rebuild/authored-variant persistence run;
- current full GUID/meta audit;
- current failure-point track collider continuity test.
