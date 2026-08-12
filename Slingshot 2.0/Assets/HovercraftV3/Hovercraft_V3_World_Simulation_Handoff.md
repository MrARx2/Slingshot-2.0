# Hovercraft V3 World Simulation Handoff

Implementation date: 2026-07-31  
Unity version used for isolated validation: 6000.5.0f1  
World configuration schema: 1  
Diagnostic schema: 3 / `3.0`

## 1. Implementation Summary

Hovercraft V3 now has one scene-authoritative World Simulation that owns gravity truth, altitude, atmosphere, local air velocity, deterministic gusts and turbulence, thermal environment values, and blended local environment zones. The scene world never decides how a craft should move. It publishes physical conditions; V3 craft parts and systems respond through Rigidbody forces, physical thrusters, fins, and thermal state.

The existing craft-local `V3WorldEnvironmentProvider` is a bridge and per-physics-tick cache. `V3WorldSimulationRoot` owns the authoritative world simulation time and world physics tick. The Mainframe keeps a separate craft-local tick for scheduling and diagnostics, but it cannot provide time to procedural environmental fields. Gravity, chassis aerodynamics, hover weight compensation, telemetry, thermal cooling, runtime debug UI, and the diagnostic recorder consume the cached world-tick truth. The six physical directional sensors and four fins may query at their own world positions, and every query in a Unity physics step reads the same root-owned world clock.

Complete features:

- Reusable world profile and Earth-like default asset.
- Exactly-one-active-root scene query and duplicate/missing validation.
- Lapse-rate atmosphere with pressure, density, temperature, speed of sound, and viscosity.
- Deterministic, spatially and temporally smooth wind, gust, and turbulence fields.
- Box and sphere zones with smooth blending, priority, and override/additive/multiplier operations.
- V3-only physical gravity, chassis drag/downforce/torque, fin airflow integration, and transonic drag approximation.
- World-aware sensors, telemetry, passive/active cooling inputs, recorder schema, CSV/binary/report exports, debug overlay, gizmos, inspectors, installer, scene content, and automated tests.

Intentionally deferred or limited:

- No speculative weather fronts, humidity, precipitation, planetary curvature, rotating reference frames, or CFD.
- The atmosphere is a bounded gameplay/engineering approximation and clamps its evaluated altitude to the configured 20 km ceiling.
- Thrusters are currently modeled as sealed electromagnetic devices, so density does not derate their thrust. A future air-breathing thruster must implement that response in its own part/firmware, not in the world.
- The default profile has zero wind, gust amplitude, and turbulence amplitude. The systems are active and testable when authored values are raised; the baseline remains calm.
- No automatic replay system was added. Deterministic seeds and same-tick identities make later replay work possible.

### 1.1 Focused architecture correction (2026-08-01)

- `V3WorldSimulationRoot` now advances one global `V3WorldClockState` in its `FixedUpdate` at execution order `-2000`, before any craft Mainframe.
- `V3WorldQueryService`, providers, sensors, and fins no longer accept craft-provided environment time/tick arguments. Procedural wind, gust, turbulence, atmosphere, and zones use only `V3WorldSimulationRoot.CurrentClock`.
- `V3CraftMainframe.CraftPhysicsTickId` remains craft-local for scheduler and diagnostic identity. Telemetry and schema-3 diagnostics record both craft and world ticks, plus world simulation time.
- `V3WorldSimulationRoot.SetClockState` is the explicit deterministic test/replay seam; production progression never depends on craft spawn time.
- Hover configuration now lives as serialized `V3HoverConfiguration` data embedded in every `CraftBuildDefinition`. Runtime `V3HoverController` fields are state/outputs, not the source of authored tuning.
- The active systems build stores an 8 m target, 1 m minimum clearance, and 14 m operational range. The three V2-parity comparison builds retain 3.075 m / 0.5 m / 7 m, proving builds can differ.
- No aerodynamic or general handling coefficients were changed in this correction.

No legacy V2 script, V2 prefab, project physics setting, or shared gravity setting was changed. The World Simulation does not contain stabilization, traction, pilot-intent, or hidden correction logic.

## 2. Files Created

Unity `.meta` files exist beside all new scripts and assets but are omitted from this responsibility list.

| Project path | Class or asset | Purpose and responsibilities |
|---|---|---|
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldProfile.cs` | `V3WorldProfile` | Scriptable world identity/configuration for gravity, atmosphere, wind, deterministic seed, thermal environment, feature toggles, and debug/recorder flags. |
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldEnvironmentSample.cs` | `V3WorldEnvironmentSample`, flags, thermal input | Complete immutable-by-convention environmental truth payload, zone provenance, tick identity, and normalized thermal consumer input. |
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldFields.cs` | `V3AtmosphereField`, `V3WindField` | Coherent atmospheric equations, speed of sound, Sutherland viscosity, and deterministic smooth air-motion fields. |
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldQueryService.cs` | `V3WorldQueryService` | Scene-scoped registry, exactly-one-root resolution, explicit missing/duplicate/profile status, and sampling entry point. |
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldSimulationRoot.cs` | `V3WorldSimulationRoot` | Authoritative scene facade that creates samples, applies the profile and zones, validates topology, and optionally synchronizes Unity gravity in Play Mode only. |
| `Assets/HovercraftV3/Runtime/Systems/Hover/V3HoverConfiguration.cs` | `V3HoverConfiguration` | Serializable per-build hover target, clearance, sensing range, response, damping, gravity compensation, suspension travel, stabilizer request limits, and surface mask. |
| `Assets/HovercraftV3/Runtime/World/Zones/V3WorldEnvironmentZone.cs` | `V3WorldEnvironmentZone` | Box/sphere signed-distance volume, smooth weight, priority, property operations, stable identity, and scene gizmos. |
| `Assets/HovercraftV3/Runtime/World/Zones/V3EnvironmentZoneRegistry.cs` | `V3EnvironmentZoneRegistry` | Cached scene zone discovery, deterministic sorting, same-query application, dominant-zone selection, and coherent density recomputation. |
| `Assets/HovercraftV3/Runtime/World/Debug/V3WorldDebugDisplay.cs` | `V3WorldDebugDisplay` | F5 runtime truth/aerodynamics panel plus gravity, air-velocity, and relative-airflow gizmos. |
| `Assets/HovercraftV3/Runtime/Parts/Aerodynamics/V3CraftAerodynamicsRuntime.cs` | `V3CraftAerodynamicsRuntime` | Physical chassis-relative airflow, airspeed, Mach, dynamic pressure, bounded compressibility, drag, passive downforce, force-at-position, torque, and combined fin state. |
| `Assets/HovercraftV3/Editor/World/V3WorldSimulationInstaller.cs` | `V3WorldSimulationInstaller` | Idempotent menu installer/generator for profile, prefab, both scenes, three test zones, and explicit chassis aero serialization. |
| `Assets/HovercraftV3/Editor/World/V3WorldSimulationRootEditor.cs` | custom inspector | Root status, profile/zone visibility, validation feedback, and refresh/install authoring controls. |
| `Assets/HovercraftV3/Tests/Editor/World/V3WorldSimulationTests.cs` | world test fixture | Atmosphere, altitude, deterministic wind, zones, priority, missing/duplicate roots, thermal scaling, head/tailwind, scene topology, crosswind, and hot-zone acceptance coverage. |
| `Assets/HovercraftV3/Editor/Generation/V3HoverConfigurationAssetUtility.cs` | hover asset installer | Idempotently serializes explicit hover configurations into the four generated craft builds and is shared by future generator runs. |
| `Assets/HovercraftV3/Tests/Editor/Integration/V3WorldClockAndHoverConfigurationTests.cs` | correction integration fixture | Verifies serialized per-build differences, assembly, telemetry, diagnostics, spring operational limits, and exact reproduction after rebuild/import. |
| `Assets/HovercraftV3/Content/World/EarthStandard.asset` | Earth Standard | Immediately usable default Earth-like world configuration. |
| `Assets/HovercraftV3/Generated/World/V3WorldSimulationRoot.prefab` | root prefab | Active reusable root with Earth Standard assigned and Unity-global gravity synchronization disabled. |
| `Assets/HovercraftV3/Hovercraft_V3_World_Simulation_Handoff.md` | this document | Complete implementation, integration, validation, setup, and review handoff. |

## 3. Files Modified

| Project path | Change, reason, and serialization impact |
|---|---|
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldSimulationRoot.cs` | Now owns and advances authoritative world time/tick; exposes immutable `CurrentClock` and explicit deterministic test/replay clock control. |
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldEnvironmentSample.cs` | Added `V3WorldClockState`; valid and invalid samples carry root-clock identity when a root exists. |
| `Assets/HovercraftV3/Runtime/World/Core/V3WorldQueryService.cs` | Removed craft-supplied time/tick parameters; all valid samples resolve the selected root's current clock. |
| `Assets/HovercraftV3/Runtime/Parts/Sensors/V3WorldEnvironmentProvider.cs` | Replaced hardcoded local atmosphere with scene-world queries and one cached craft reference sample. Applies sampled V3 gravity physically and restores the original Rigidbody gravity fallback when invalid. Retains the small legacy facade/API wrapper. No prefab reference required; assembler installs it. |
| `Assets/HovercraftV3/Runtime/Core/Build/CraftBuildDefinition.cs` | Added embedded serialized `V3HoverConfiguration`; build copies deep-copy it and validation sanitizes it. Build assets now persist hover authority across Play Mode and rebuilds. |
| `Assets/HovercraftV3/Runtime/Parts/Sensors/V3DirectionalSensorRuntime.cs` | Publishes authoritative atmospheric observations and retains the prior overload for source compatibility. No serialized change. |
| `Assets/HovercraftV3/Runtime/Parts/Sensors/V3DirectionalSensorDeviceRuntime.cs` | Stores the exact positional `EnvironmentTruth` used for each device sample. No asset reference change. |
| `Assets/HovercraftV3/Runtime/Parts/Chassis/ChassisDefinition.cs` | Added serialized reference area, drag/downforce coefficients, center of pressure, and Mach multiplier curve. Existing generated chassis assets were reserialized with explicit defaults. |
| `Assets/HovercraftV3/Runtime/Parts/Aerodynamics/RuntimeAerodynamicFinInstance.cs` | Replaced fixed atmosphere assumptions with root-clock world samples; exposes airflow, Mach, dynamic pressure, force, and torque. No new serialized reference. |
| `Assets/HovercraftV3/Runtime/Core/Assembly/V3CraftAssembler.cs` | Installs environment/aero/debug components, passes the selected build's hover configuration into the controller, and applies build operational travel limits to physical spring mounts. |
| `Assets/HovercraftV3/Runtime/Core/Mainframe/V3CraftMainframe.cs` | Retains a craft-local scheduler/diagnostic tick, starts the reference sample without supplying environment time, and exposes craft tick identity separately from world truth. |
| `Assets/HovercraftV3/Runtime/Parts/Connectors/Springs/RuntimeSpringMountInstance.cs` | Accepts serialized per-build operational compression/extension limits clamped to installed connector hardware capability. |
| `Assets/HovercraftV3/Runtime/Parts/Common/RuntimePartInstance.cs` | Added environmental thermal tick overload and runtime ambient/airflow/cooling properties while preserving the old overload. No serialized change. |
| `Assets/HovercraftV3/Runtime/Core/Thermal/V3ThermalController.cs` | Feeds actual ambient, density ratio, relative airspeed, and zone cooling multiplier into part thermal updates. No serialized change. |
| `Assets/HovercraftV3/Runtime/Parts/Cooling/V3CoolingRuntime.cs` | Uses each part's current environmental ambient instead of authored ambient for active-cooling thresholds. No serialized change. |
| `Assets/HovercraftV3/Runtime/Core/Telemetry/V3CraftTelemetryHub.cs` | Exposes world and craft tick identity plus active serialized hover ID/version, target, clearance, range, strength, damping, and gravity-compensation state. |
| `Assets/HovercraftV3/Runtime/Systems/Hover/V3HoverController.cs` | Reads all authoritative hover/stabilizer values from the selected build configuration; sampled gravity is still converted only into physical thruster requests. |
| `Assets/HovercraftV3/Runtime/Presentation/UI/V3SystemsConsoleController.cs` | Hover page displays active serialized configuration; telemetry page distinguishes craft tick, world tick, and world time. |
| `Assets/HovercraftV3/Runtime/World/Debug/V3WorldDebugDisplay.cs` | F5 display now labels world tick/time and craft tick separately. |
| `Assets/HovercraftV3/Runtime/Presentation/UI/V3FreeDriveHud.cs` | Added F5 World Info help and preserved F4 Systems; retained prior HUD placement/toggle work. No serialized change. |
| `Assets/HovercraftV3/Runtime/Presentation/Debug/V3ThrusterDebugView.cs` | Temperature coloring now normalizes against the part's current environmental ambient. Existing static overload remains compatible. No serialized change. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Core/V3DiagnosticSchema.cs` | Advanced schema to 3/3.0; records recorder tick, craft tick, world tick, world time, and active hover configuration without conflating identities. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Core/V3DiagnosticSession.cs` | Craft snapshot now stores the complete authoritative hover configuration; world snapshot remains profile/configuration truth. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Sources/V3WorldTruthRecorder.cs` | Copies the exact cached craft sample and exact aero state; uses Unity gravity only when the sample is invalid. Recorded sample fields changed. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Sources/V3CraftSensorRecorder.cs` | Records each sensor's actual positional world truth and provenance. Recorded sensor fields changed. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Sources/V3DiagnosticSnapshotExporters.cs` | Exports root/profile/zone configuration alongside shared Physics settings. Snapshot format changed. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Sources/V3ForceTorqueLedger.cs` | Adds physical chassis and fin aerodynamic force/torque without double-counting fins. Ledger fields changed. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Export/V3DiagnosticExportService.cs` | Writes schema-3 aligned binary fields, dual-tick/world-time CSV columns, and explicit hover configuration in Markdown reports. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Export/V3DiagnosticReportBuilder.cs` | Summarizes invalid/zoned samples, density/temperature ranges, maximum Mach, and maximum dynamic pressure; invalid environment degrades data quality. Report model changed. |
| `Assets/HovercraftV3/Runtime/Diagnostics/Track/V3TrackDiagnosticAnalyzer.cs` | Track snapshot uses the resolved profile gravity when available. No scene reference change. |
| `Assets/HovercraftV3/Editor/SceneTools/V3FreeDriveSceneGenerator.cs` | Installs the authoritative world before saving generated development content. Generated scenes changed. |
| `Assets/HovercraftV3/Editor/Generation/V3SystemsIntegrationPrototypeGenerator.cs` | Installs the authoritative world before saving the systems scene. Generated scenes changed. |
| `Assets/HovercraftV3/Tests/Editor/Diagnostics/V3DiagnosticTruthAndSensorTests.cs` | Added proof that recorder truth exactly equals the craft's cached tick sample. Test-only change. |
| `Assets/HovercraftV3/Tests/Editor/World/V3WorldSimulationTests.cs` | Migrated sampling tests to root clock and added the two-craft/different-initialization-time environmental equality proof, including a blended zone. |
| `Assets/HovercraftV3/Tests/Editor/Integration/V3SerializedScenePlayModeTests.cs` | Asserts both Play Mode scene starts and runtime rebuilds reproduce the active serialized 8 m target. |
| `Assets/HovercraftV3/Editor/Generation/V2ReferencePrototypeGenerator.cs` | Future regeneration writes explicit independent hover configurations for reference, vector, and terrain builds. |
| `Assets/HovercraftV3/Editor/Generation/V3SystemsIntegrationPrototypeGenerator.cs` | Future regeneration writes the active systems build's explicit 8 m configuration. |
| `Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/Builds/*.asset` | Three build assets now contain serialized hover configuration data. |
| `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset` | Active build now persists its 8 m hover target and full response configuration. |
| `Assets/HovercraftV3/Tests/Editor/Integration/V3ProjectReadabilitySprintTests.cs` | Force-owner whitelist now explicitly includes V3 gravity and chassis aerodynamics. Test-only change. |
| `Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/Definitions/ApexV3_V2Reference_Chassis.asset` | Serialized the new chassis aero defaults; this is a V3 reference-build asset, not a legacy V2 prefab. |
| `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Definitions/ApexV3_SystemsIntegration_Chassis.asset` | Serialized the same chassis aero defaults used by the active systems craft. |
| `Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity` | Added one configured WorldSimulationRoot and three zones. Existing craft/spawner/recorder references remain. |
| `Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity` | Added one configured WorldSimulationRoot and three zones. Existing systems craft and recorder remain. |

## 4. Scene and Prefab Changes

Both `V3_CraftLab.unity` and `V3_HandlingTrack.unity` now contain this authored hierarchy:

```text
WorldSimulationRoot (active)
  V3EnvironmentZoneRegistry
  V3WorldSimulationRoot -> EarthStandard.asset
  EnvironmentZones
    Tunnel Wind Shelter
    Crosswind Test Zone
    Hot Thermal Test Zone
```

The reusable `V3WorldSimulationRoot.prefab` is active, contains the registry and root components, references Earth Standard, and deliberately contains no scene-specific zones. `synchronizeUnityGravity` is false.

The scenes still select `ApexV3_SystemsIntegration.asset` through the existing `V3CraftTestSpawner`. The existing `V3DiagnosticRecorder` remains scene-owned and binds the assembled craft dynamically. `V3WorldDebugDisplay` is craft-owned and installed by the assembler, so no manual debug object is necessary.

No manual setup is required for these two development scenes. For another playable V3 scene, use `Tools > Hovercraft V3 > World > Install World Simulation` or place the root prefab and ensure exactly one active root exists.

## 5. Final Runtime Data Flow

```text
EarthStandard WorldProfile
  -> V3WorldSimulationRoot.FixedUpdate -> authoritative WorldClock(time, tick)
  -> V3WorldSimulationRoot + cached V3EnvironmentZoneRegistry
  -> V3WorldQueryService (scene-scoped, explicit status)
  -> V3WorldEnvironmentProvider.BeginPhysicsTick(reference COM, craft tick)
     -> cached craft reference V3WorldEnvironmentSample
        (root-owned world time + world tick)
     -> V3-only Rigidbody gravity acceleration
     -> chassis observations / hover gravity compensation
     -> V3CraftAerodynamicsRuntime -> physical force at center of pressure
     -> V3ThermalController -> each RuntimePartInstance
     -> V3CraftTelemetryHub / systems UI / F5 world display
     -> V3WorldTruthRecorder -> binary, CSV, and Markdown report
  -> same root-clock positional queries
     -> six V3DirectionalSensorDeviceRuntime instances
     -> four RuntimeAerodynamicFinInstance instances
     -> sensor observations / fin physical forces
     -> sensor recorder / fin ledger / telemetry
```

`V3WorldSimulationRoot.TrySample` creates each truth payload. It calculates baseline atmosphere and wind, then applies the cached zone list in deterministic priority order. `V3WorldEnvironmentProvider` holds the craft reference copy for the full physics tick. Sensor device snapshots hold their exact positional copies. No recorder formula recreates temperature, density, pressure, wind, gravity, Mach, or dynamic pressure.

## 6. Physics Tick and Sampling Strategy

`V3WorldSimulationRoot.FixedUpdate` advances the authoritative world simulation time and world tick once per Unity physics step at execution order `-2000`. The existing `V3ControllerPipeline.FixedUpdate` path then calls `V3CraftMainframe.TickObservations`. Each Mainframe advances only its own craft-local scheduler/diagnostic time and tick, and calls `BeginPhysicsTick(referencePosition, craftPhysicsTickId)` before publishing observations or scheduling systems. It cannot inject those local values into the world.

One reference sample at the Rigidbody world center of mass is cached and reused by gravity, chassis aerodynamics, hover gravity compensation, telemetry, thermal systems, debug UI, and recorder. Each directional sensor and fin may sample at its own physical transform, but every query reads `V3WorldSimulationRoot.CurrentClock`. Therefore spatial conditions can differ, while two crafts at the same position in the same world tick receive byte-for-byte equal procedural environmental results regardless of their spawn times.

Aerodynamics does not choose a timestamp. Chassis aero reads the cached reference sample; fins use root-clock positional queries. The recorder reads component state after physics-system execution and copies world tick/time and craft tick into distinct fields. `SetClockState` exists as an explicit deterministic test/future-replay seam and is not tied to craft lifetime.

Execution-order assumptions are explicit: the root has `DefaultExecutionOrder(-2000)` and registers on enable; the Mainframe samples before systems use environment data. Root/profile validity is checked every query. There is no threaded mutation of profile or zone data during a physics tick.

## 7. Current Hovercraft State

- Active build: `Apex V3 Systems Integration Prototype`, layout `Rear Gimbal / Four-Corner Suspension / Active Aero`.
- Rigidbody mass: 11,679.5 kg (8,520 kg chassis + 3,155 kg installed parts + 4.5 kg six sensors). The mass calculator applies this at assembly.
- Center of mass: calculated from the chassis base COM `(-0.65262157 m Y, approximately centered X/Z)` plus every physical part and sensor transform. No parity calibration offset is applied. The final runtime value is applied to `Rigidbody.centerOfMass` and exposed in telemetry/recording rather than duplicated as a baked number.
- Main propulsion: one rear 480 kN base-force electromagnetic thruster on a 120 kg physical gimbal. Normal/overload part multipliers and power/thermal limits still govern actual output.
- Hover: four physical hover thrusters on spring mounts. The controller requests thrust from measured surface distance and sampled gravity; it never applies lift or stabilization torque directly. The active systems build serializes target 8 m, minimum clearance 1 m, maximum operational/sensing range 14 m, strength 0.45, damping 0.12, gravity compensation enabled x1, compression 0.3 m, and extension 0.12 m. Rebuild and Play Mode restart reproduce these values.
- Stabilizer: four roof thrusters plus hover thrusters receive router requests. Surface alignment is implemented through allocated physical thrusters only.
- Sensors: six physical directional mounts (front, rear, left, right, top, bottom), 40 m range. Front/bottom request 120 Hz; the others request 60 Hz; minimum is 15 Hz and maximum is 120 Hz. Actual grants depend on Mainframe schedule and systems power.
- Telemetry: live and immutable snapshots include build/mass/power/thermal/scheduler/sensor data plus full environment and aero state.
- Aerodynamics: chassis force is active whenever the cached world atmosphere is valid. Four active physical fins receive local same-tick airflow, actuator state, density, and dynamic pressure.
- Thermal: part heat, protection, lockout, passive cooling, and active cooling remain active. Ambient, density, airflow, and zone cooling now alter the physical thermal calculation.
- Energy: 5,200 continuous units, with 4,500 propulsion and 1,300 systems channel ceilings. Existing allocation, starvation, and overload behavior is unchanged.
- Router: Mainframe/system requests remain the only command route to thrusters and actuators. The world sends no actuator commands.
- Input: existing `V3PilotInputAdapter` and scene input routes remain connected.
- Recorder: scene recorder binds the spawned craft, samples the same runtime states, and exports schema 3. F7/F8/F9 behavior remains.
- Known disconnected systems: no air-breathing engine consumer exists; no humidity/precipitation consumer exists; no replay playback exists.
- Expected runtime warning: one warning if a V3 craft cannot find a valid authoritative world; it states that world-dependent truth/aero are invalid and Unity Rigidbody gravity is being used as compatibility fallback.

## 8. World Simulation Configuration

Active Earth Standard values in both development scenes:

| Setting | Value |
|---|---|
| Stable/profile ID | `world.earth_standard`, `Earth Standard`, version 1 |
| Reference world Y / altitude offset | 0 m / 0 m |
| Gravity | `(0, -9.80665, 0) m/s^2`, uniform directional |
| Sea-level temperature | 15 C / 288.15 K |
| Sea-level pressure | 101,325 Pa |
| Sea-level density | approximately 1.225 kg/m^3, derived from pressure and temperature in lapse mode |
| Atmosphere | Standard lapse-rate model; lapse 0.0065 K/m; minimum 180 K; maximum evaluated altitude 20,000 m |
| Speed of sound at sea level | approximately 340.3 m/s |
| Wind | enabled; direction `(0,0,1)`; speed 0 m/s |
| Gust | strength 0 m/s; temporal scale 0.08; spatial scale 0.002 |
| Turbulence | enabled; strength 0 m/s; spatial scale 0.01; temporal scale 0.2; vertical contribution 0.35 |
| Deterministic seed | 314159 |
| Thermal | enabled; baseline multiplier 1; clamp 0.1-5; airflow reference 100 m/s; maximum airflow multiplier 4 |
| Feature toggles | gravity, atmosphere, wind, turbulence, zones, thermal, debugging, and recorder integration enabled |
| Unity gravity synchronization | disabled |

Scene zones, all boxes with 20 m smooth blend distance:

- Tunnel Wind Shelter: center `(0,12,220)`, size `(90,30,100)`, priority 10, wind multiplier 0.2, turbulence multiplier 0.25.
- Crosswind Test Zone: center `(0,15,430)`, size `(120,40,120)`, priority 20, additive `(22,0,0) m/s` air velocity, additive turbulence scale 0.35.
- Hot Thermal Test Zone: center `(0,15,650)`, size `(120,40,120)`, priority 30, additive 35 C. Density is coherently recomputed from the resulting pressure/temperature unless a density operation explicitly overrides it.

## 9. Aerodynamics Integration

`V3CraftAerodynamicsRuntime` consumes local air velocity, density, speed of sound, atmosphere validity, and the reference tick identity. Relative airflow is `point velocity - local air velocity`; airspeed is its magnitude; Mach is airspeed divided by local speed of sound; dynamic pressure is `0.5 * density * airspeed^2`.

Chassis drag is opposite relative airflow and uses dynamic pressure, 10 m^2 area, Cd 0.015, and a bounded Mach multiplier. Passive downforce uses 8 m^2 and coefficient 0.005 along craft-down. The combined force is applied at local center of pressure `(0,0.15,0.25)`, so Rigidbody torque is physically produced by the lever arm. Reported chassis torque is the same cross product represented by `AddForceAtPosition`.

The compressibility curve is a gameplay approximation: 1.0 through Mach 0.65, 1.15 at 0.85, 1.30 at 1.0, and 1.40 at 1.25, with Unity curve extrapolation constrained by a nonnegative output. It is not CFD and does not model shocks, wave drag direction changes, or control-surface flutter.

Four active fins continue to calculate their own local angle/force using sampled density and local relative airflow. They apply force at their physical locations, generating torque. Fin authority telemetry is normalized from dynamic pressure; actuator physical limit reports saturation.

There is no fixed-density runtime fallback in V3 aerodynamics. Invalid/missing atmosphere disables chassis/fin environmental response and marks truth invalid. The only old atmosphere compatibility surface is the small `V3EnvironmentSample` facade around the authoritative sample for older callers.

## 10. Sensor and Telemetry Integration

The chassis has six directional sensor devices at the named physical transforms `Sensor.Front`, `Rear`, `Left`, `Right`, `Top`, and `Bottom`. They retain their physical raycast behavior, 40 m range, scheduler/power grants, firmware readiness, and explicit unavailable reasons. They capture full positional world truth using the authoritative root clock; sensor records expose both the environment's world tick and the sampling craft's local tick.

No artificial measurement noise or latency model is currently implemented. Differences from the reference sample are caused by actual sensor position, direction, physical hit state, sampling grant/rate, or unavailable state—not random perturbation.

Added telemetry includes root/profile identity, world tick/time, craft tick, active hover configuration and target, sample position, altitude, gravity, temperature, pressure, density, sound speed, viscosity, base/zone/gust/turbulence/local air vectors, cooling multiplier, zone IDs/weights/operations, chassis/fin forces and torques, airspeed, Mach, dynamic pressure, fin authority, and saturation.

Craft control systems consume their existing Mainframe observations. Chassis aero and thermal read the provider cache directly because they are same-craft physical consumers, not decision systems. Sensors query the world service directly only to obtain position-dependent truth with the reference tick/time. The recorder reads cached/snapshot state and does not query/reconstruct environment data.

## 11. Thermal Integration

Every runtime part with an enabled thermal profile receives current ambient temperature, density ratio, relative craft airspeed, profile/zone cooling multiplier, and airflow scaling. Passive cooling is based on temperature difference above the actual ambient, authored part cooling coefficient, density, airflow, and environmental multiplier. Hot air therefore reduces temperature delta and, through coherent density, can also reduce convective effectiveness. Faster airflow and denser air increase cooling up to the profile cap.

`V3CoolingRuntime` compares cooled parts against current environmental ambient and remains a finite, powered device. Existing generated heat, safe limit, overheat, output derating, lockout, restart, active cooling, and power-starvation behavior remains active rather than merely reported.

Authored `ThermalProfile.ambientTemperatureC` values remain in part assets as compatibility/default initialization values for code paths with no world sample. They are not the authoritative ambient while a valid V3 world is connected. The thruster debug color now also uses each part's current ambient.

## 12. Recorder and Report Changes

Diagnostic schema 3 contains all schema-2 world/aerodynamic truth and additionally adds:

- Separate recorder physics tick, craft-local physics tick, world physics tick, and world simulation time fields.
- Active serialized hover configuration ID/version, target, clearance, range, response/damping, gravity-compensation settings, and suspension travel in craft snapshots/reports.
- Per-sensor craft and world tick identities.

The full world/aerodynamic payload includes:

- World root/profile IDs, name, config version, validity flags, tick, timestamp, and query position.
- Raw Y, altitude, atmosphere altitude, gravity, temperature C/K, pressure Pa, density kg/m^3, speed of sound m/s, and viscosity Pa*s.
- Base wind, zone wind, gust, turbulence, local air velocity in m/s, turbulence intensity, and cooling multiplier.
- Dominant zone and up to four active zone ID/weight slots plus applied-operation bit flags.
- Ground velocity, relative air velocity, airspeed m/s, Mach, dynamic pressure Pa, chassis/fin/total aero force N, torque N*m, fin authority, and saturation.
- Each sensor's environmental truth and physical reading/unavailable state.

No earlier field was silently repurposed. Schema-3 compact binary field descriptors now exactly match the values written. CSV begins with recorder, craft, and world ticks plus world time. Markdown reports explicitly state the active hover configuration and retain the `Atmosphere and aerodynamics` section. Data quality is degraded if an invalid environment sample occurs.

Sampling remains recorder-profile dependent and physics-tick based; manual markers/events remain event-based; report ranges/maxima are session summaries. World truth is the provider's cached reference sample. Sensor truth is the device's root-clock positional sample. That distinction is explicit through positions, world/craft tick IDs, and per-sensor records.

Invalid samples use `environmentValid = false` plus flag bits such as MissingWorld, DuplicateWorld, or MissingProfile. Numeric data is kept finite/zeroed; it is not fabricated.

Example Markdown summary:

```text
Atmosphere and aerodynamics
- Invalid environment samples: 0
- Samples inside environment zones: <count>
- Air density range: <min> - <max> kg/m3
- Ambient temperature range: <min> - <max> C
- Maximum Mach: <value>
- Maximum dynamic pressure: <value> Pa
```

## 13. Existing System Migration

- Old `WorldAtmosphere`: there was no separate scene authority. The craft-local `V3WorldEnvironmentProvider` previously acted as a fixed atmosphere. It is now the compatibility bridge/cache backed only by `V3WorldSimulationRoot`.
- Hardcoded density: removed from runtime console, sensors, fins, and aero. The Earth profile retains 1.225 kg/m^3 only as the explicit custom-density-mode configuration/default reference—not as a consumer constant.
- Hardcoded gravity: V3 hover, diagnostic track frame, recorder truth, and physical gravity prefer the sampled vector. `Physics.gravity` remains only a documented invalid-world or shared-settings fallback/snapshot.
- Wind: no direct wind force exists. Moving air only affects consumers through relative airflow.
- Temperature: authored part ambient remains initialization/fallback; valid-world runtime cooling uses sampled ambient.
- Compatibility wrappers: `V3EnvironmentSample`, `SampleEnvironment(Vector3)`, and the legacy sensor tick/thermal overloads remain to keep existing tests/callers source-compatible.
- Duplicate source of truth: none for a valid V3 scene. The world root owns environment time/tick; the selected craft build owns hover configuration. `Physics.gravity` is still a project-wide V2/shared setting, but V3 bodies disable built-in gravity after a valid sample and receive profile gravity as physical acceleration. Optional synchronization is off.

## 14. V2 Impact Assessment

- V2 scripts changed: no.
- Legacy V2 prefabs/scenes changed: no.
- Project physics settings changed: no.
- `Physics.gravity` changed by default: no. The optional root setting is false and can run only during Play Mode.
- V2 behavior tested: the complete V3 regression suite includes V2 reference mass, topology, input-route, parity preparation, and compatibility tests. The long 78-second real-time parity capture remains opt-in and was not run in this validation pass.
- Risk: enabling `synchronizeUnityGravity` manually would alter the shared Physics gravity during Play Mode and could affect V2 objects in that same scene. Leave it disabled for mixed V2/V3 scenes. The root restores the prior value when disabled.

## 15. Validation and Test Results

Canonical isolated result file: `HovercraftV3_v3_final_results.xml` in the temporary validation project. Final result on 2026-08-01: 139 discovered, 138 passed, 0 failed, 1 intentionally skipped. The only skipped test is `V3ParityHarnessTests.ParityScene_CompletesFullProfileAndExportsTelemetry`, gated by `HOVERCRAFT_V3_RUN_PARITY_CAPTURE=1` because it takes 78 seconds in real time. The focused correction run also passed 17/17 before the final suite.

| Scenario | Setup and expected result | Actual result | Status |
|---|---|---|---|
| A Baseline | Earth profile at zero altitude, zero wind, no zone; stable sea-level truth | 15 C, 101,325 Pa, density within 0.002 of 1.225 kg/m^3, sound speed within 0.5 of 340.3 m/s, no zone | Pass automated |
| B Altitude | Sample at 1,000 m; temperature/pressure/density/sound speed decrease and remain finite | All four decreased coherently; no invalid/NaN data | Pass automated |
| C Headwind | 100 m/s ground velocity with 20 m/s opposing air | Relative airspeed 120 m/s; dynamic pressure greater than tailwind | Pass automated |
| D Tailwind | 100 m/s ground velocity with 20 m/s following air | Relative airspeed 80 m/s; dynamic pressure lower than headwind | Pass automated |
| E Crosswind zone | 30 m/s additive local crosswind with 10 m blend; dominant zone and smooth boundary | Full 30 m/s at center, intermediate blend, sub-0.1 m/s final boundary step, dominant ID correct | Pass automated |
| F Hot zone | +40 C local zone; density and cooling respond | Ambient 55 C at center, lower density and lower effective cooling multiplier than outside | Pass automated |
| G Turbulence | Seed 7123, nonzero gust/turbulence, repeat and nearby sample | Identical input produced identical vector; nearby space/time delta remained below 1 m/s | Pass automated |
| H Duplicate root | Two active configured roots in one scene | Explicit `DuplicateWorld`, clear validation error, no selected root | Pass automated |
| I Missing root | Destroy/disable only root | Query false with `MissingWorld`; runtime fallback is finite and documented | Pass automated |
| J Recorder consistency | Set world tick 77/time 12.5, begin craft tick 12, capture world recorder | Distinct craft/world identities plus position, gravity, temperature, pressure, density, and local air velocity exactly equal cached craft sample | Pass automated |
| K Shared world clock | Initialize two craft providers at different world times, assign different craft ticks, then sample the same zoned position at world tick 901/time 87.5 | World tick/time, base wind, gust, turbulence, local air, atmosphere, active-zone IDs/weights/operations are identical; craft ticks remain distinct | Pass automated |
| L Hover persistence/assembly | Import active and reference builds, assemble active build, inspect controller/springs/telemetry/diagnostics, rebuild and inspect again | Active target remains 8 m after import and rebuild; reference remains 3.075 m; all consumers match serialized build data | Pass automated |

Additional validated behavior includes both serialized scenes containing exactly one configured root and exactly three zones; serialized play-mode craft spawn/rebuild/input/diagnostic capture; force-owner authority; thermal behavior; systems scheduling; and report/export data.

No driving screenshot or new full-speed player recording was generated in this implementation run. The next user driving test should validate feel and tune coefficients; it should not be used to re-establish architectural correctness already covered here.

## 16. Known Problems

1. Simplified high-altitude atmosphere
   - Severity: low for current tracks.
   - Reproduction: move above the profile's 20 km maximum evaluated altitude.
   - Cause: intentionally bounded lapse-rate model.
   - Workaround: keep current test tracks below 20 km.
   - Next step: add layered atmosphere only if future tracks require it.

2. Transonic response is approximate
   - Severity: medium for final 1,300 km/h handling balance, low for architecture.
   - Reproduction: inspect Mach 0.65-1.25 drag response.
   - Cause: bounded animation curve rather than CFD/wave-drag simulation.
   - Workaround: tune the serialized curve from recorder data.
   - Next step: run controlled calm/headwind/tailwind passes before changing coefficients.

3. Default weather amplitudes are zero
   - Severity: informational.
   - Reproduction: run outside the authored zones with Earth Standard unchanged.
   - Cause: calm baseline chosen for repeatable testing.
   - Workaround: author wind/gust/turbulence on a profile copy.
   - Next step: create named weather profiles if repeated variants are needed.

4. No sensor noise/latency model
   - Severity: low; truth separation and scheduling are already explicit.
   - Reproduction: compare a granted sensor sample with world truth at the same position/tick.
   - Cause: deliberately outside this first world implementation.
   - Workaround: use scheduler rate/power loss to test availability degradation.
   - Next step: add deterministic firmware-level noise/delay only if gameplay requires it.

5. Play Mode controller edits are intentionally non-authoritative
   - Severity: informational.
   - Reproduction: change a spawned runtime controller value directly, stop, and rebuild.
   - Cause: `CraftBuildDefinition.hoverConfiguration` is the source of truth and assembly replaces transient state.
   - Correct workflow: edit the selected build asset's Hover Configuration block outside Play Mode, then rebuild or restart Play Mode.

## 17. Warnings and Console Output

- Compiler warnings from this implementation in the final isolated log: none.
- Compiler errors: none.
- Test failures: none in the canonical 138/139 regression run; one documented opt-in skip.
- Expected validation error: activating more than one root logs that Hovercraft V3 requires exactly one active World Simulation in the named scene.
- Expected missing-world warning: one warning per craft invalid period, followed by compatibility gravity behavior.
- Expected missing-profile error: an active root without a profile logs a clear error and queries return `MissingProfile`.
- Current scene runtime errors: none observed by serialized scene tests.
- Play Mode: both development scenes entered/exited cleanly in automated serialized tests.
- The earlier project-wide `unexpected guid mismatch` messages were not reproduced in the isolated import/generation/test cycle and are not emitted by this implementation.

## 18. Inspector Setup Guide

1. Open `Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity` for track testing or `V3_CraftLab.unity` for the lab.
2. Select root object `WorldSimulationRoot`. Confirm one active root, `Earth Standard` assigned, Synchronize Unity Gravity off, validation status valid, and zone count 3.
3. Inspect `Assets/HovercraftV3/Content/World/EarthStandard.asset` for gravity, atmosphere, wind, thermal, seed, and feature toggles.
4. Select the `CraftBuildDefinition` assigned to the scene spawner/assembler and edit its serialized Hover Configuration block outside Play Mode. The active systems build is `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset`.
5. Enter Play Mode and select the runtime craft created by `V3CraftTestSpawner`. On the craft root inspect `V3WorldEnvironmentProvider`, `V3CraftAerodynamicsRuntime`, `V3CraftMainframe`, `V3CraftTelemetryHub`, and `V3HoverController`; the controller should display the same build-owned configuration.
6. Expand the chassis and inspect `Sensor.Front`, `Sensor.Rear`, `Sensor.Left`, `Sensor.Right`, `Sensor.Top`, and `Sensor.Bottom`. Their device runtime snapshots should show valid same-world-tick environment truth when powered/scheduled.
7. Inspect the chassis definition referenced by the current build for its physical aerodynamics block: area, Cd, downforce settings, center of pressure, and Mach curve. Inspect the four runtime fins and actuators under their connector pieces.
8. Select the scene's `V3DiagnosticRecorder`; verify craft binding occurs after spawn, output root is writable, and desired recording profile/duration are selected.
9. Use F4 for the existing systems debug pages, F5 for World Truth, F2 for thruster vectors, F3 for controls, F7 to start/stop recording, F8 for a marker, and F9 to export.
10. Watch world tick/time separately from craft tick, configured hover target, altitude, temperature, pressure, density, local air velocity, dominant zone, airspeed, Mach, dynamic pressure, aero force, sensor count, fin authority/saturation, and part cooling/temperature.

## 19. Recommended Pre-Test Checklist

- [ ] `WorldSimulationRoot` is active.
- [ ] Exactly one active root exists in the loaded scene.
- [ ] Earth Standard or the intended profile is assigned.
- [ ] Synchronize Unity Gravity remains off unless a shared-scene impact is intentional.
- [ ] The expected craft build is selected and spawned.
- [ ] Its serialized Hover Configuration shows the expected target/clearance/range; the active systems baseline is 8/1/14 m.
- [ ] Six sensors are connected and telemetry reports valid world truth.
- [ ] Chassis aero is valid and dynamic pressure responds to relative airspeed.
- [ ] Four fins and their actuators are present.
- [ ] Recorder is bound, enabled, and its output path is writable.
- [ ] F5 world display shows valid world tick/time, craft tick, and profile.
- [ ] Console has no unexpected errors or warnings.
- [ ] Rigidbody mass is approximately 11,679.5 kg and COM is the calculated runtime value.
- [ ] The three known zones and their positions are understood before interpreting a run.
- [ ] Baseline profile wind/gust/turbulence settings are recorded with the test notes.

## Questions for ChatGPT and Project Owner Review

1. Do all current V3 environmental consumers now resolve through the root/provider flow, with any remaining `Physics.gravity` use limited to a clearly invalid-world/shared-settings fallback?
2. Is the distinction between craft reference truth, positional sensor truth, and measured surface hit data clear enough in UI and reports?
3. Does schema 3 make the recorder, craft, and world clock identities clear in exported evidence?
4. Are the 10 m^2 / Cd 0.015 chassis drag and transonic multiplier curve reasonable starting points for an 11,679.5 kg craft targeting roughly 1,300 km/h?
5. Is the small passive downforce coefficient appropriate, or should high-speed surface retention come primarily from physical hover/roof thruster authority and active fins?
6. During the next run, do fin force, authority, and saturation rise smoothly with dynamic pressure without overpowering the physical thrusters?
7. Does altitude produce useful but controlled changes in density, speed of sound, aero authority, and cooling on the intended tracks?
8. Do the tunnel, crosswind, and hot-zone boundaries feel smooth with no visible force/temperature discontinuity?
9. Are thermal effects visibly active in part temperatures and protection states, rather than only present in telemetry?
10. Does any remaining controller behavior look like hidden correction, or can every craft force/torque be traced to gravity, chassis aero, fins, or physical thrusters?
11. Should future craft variants keep embedded per-build hover configurations or graduate to separately reusable hover-profile assets once the variant count grows?
12. Is this setup ready for the next controlled driving matrix: calm baseline, altitude, headwind, tailwind, crosswind zone, hot zone, and turbulence?
13. For the next recorder report, should the primary review focus be dynamic pressure/drag balance, surface clearance, fin saturation, hover/roof thrust reserve, cooling effectiveness, or all five?
