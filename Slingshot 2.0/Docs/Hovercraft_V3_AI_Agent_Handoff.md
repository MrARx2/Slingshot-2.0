# Hovercraft V3 - Complete AI Agent Handoff

Last updated: 2026-07-27

## 1. Purpose of this document

This is the canonical handoff for the Hovercraft V3 implementation in the
Slingshot 2.0 Unity project. It is intended to let another AI agent continue
work without reconstructing prior decisions from chat history.

This document describes:

- what is implemented;
- the non-negotiable physics architecture;
- how craft data, sockets, connectors, and runtime assembly work;
- the current generated Apex reference craft and its variants;
- controls, debug tools, scenes, and editor workflows;
- the authoritative automated test and parity state;
- known limitations and safe extension points.

When documentation and code disagree, use this precedence:

1. Current source under `Assets/HovercraftV3/`.
2. Current generated assets under `Assets/HovercraftV3/Prototype/`.
3. The newest passing test artifacts named in this document.
4. This handoff.
5. Older gate reports and mapping documents.

Some older reports contain values from before the external-hardware and
side-outrigger refactors. Never restore an old number merely because it appears
in an earlier report.

## 2. Project and repository state

- Unity version: `6000.5.0f1`.
- Project directory:
  `D:\Projects\Unity\Slingshot2\Slingshot-2.0\Slingshot 2.0`
- V3 runtime assembly: `Lunarlight.Hovercraft.V3`
- V3 Editor assembly: `Lunarlight.Hovercraft.V3.Editor`
- V3 test assembly: `Lunarlight.Hovercraft.V3.EditorTests`
- Input dependency: Unity Input System.

As of this handoff, Git reports `Assets/HovercraftV3/` and the newest V3
documents as untracked. Do not reset, clean, replace, or delete them on the
assumption that Git can recover them.

`Temp/CodexV3UnityProject/` is an isolated Unity test mirror. It is not the
source of truth. Source edits belong in the main project. The mirror is useful
when the main project is open in Unity or when batch tests must not touch the
user's active Library.

## 3. Completion status

The gated V3 foundation is complete. There is no open mandatory implementation
gate.

| Gate | Result | Delivered capability |
| --- | --- | --- |
| 0 | Pass | Measured V2 baseline, repeatable command profile, CSV/JSON telemetry |
| 1 | Pass | Data definitions, sockets, compatibility validation, craft builder |
| 2 | Pass | Modular V2-equivalent Apex hardware reconstruction |
| 3 | Pass | Enforced physical behavioral parity |
| 4 | Pass | Per-part thermal simulation and telemetry |
| 5 | Pass | Powered rear-main gimbal using the same thruster endpoint |
| 6 | Pass | Emergency overload, thermal derating, shutdown, lockout, recovery |
| 7 | Pass | Player input, HUD/warnings, reset/recovery, free-drive scene |
| 8 | Pass | Passive spring connector with a moving physical force origin |
| 9 | Pass | Balanced, Propulsion, Stability, and Recovery power modes |
| 10 | Pass | Unified read-only craft telemetry hub |
| 11 | Pass | Socket, part, and capability registries |
| 12 | Pass | Runtime mass service and post-assembly validation |

Work after Gate 12 added:

- inspector vehicle cards and written preset descriptions;
- runtime thruster direction/firing/temperature visualization;
- surface-normal versus craft-up visualization;
- inclined, stepped, and suspension-test surfaces in free drive;
- physical-only stabilization, yaw damping, and traction allocation;
- visible external socket/connector/thruster construction;
- a four-outrigger, drone-style vertical-thruster layout.

## 4. Non-negotiable architecture rules

### 4.1 All normal craft motion is actuator-authoritative

The production control path is:

```text
player or scripted V3PilotCommand
    -> controller intent
    -> V3ActuatorCommandRouter
    -> V3PowerDistributor
    -> RuntimeThrusterInstance
    -> Rigidbody.AddForceAtPosition(..., ForceMode.Force)
```

Within `Assets/HovercraftV3`, `RuntimeThrusterInstance` is the only normal
craft-control force application site.

Controllers must not call:

- `Rigidbody.AddForce`;
- `Rigidbody.AddTorque`;
- direct position or rotation writes;
- direct linear or angular velocity writes.

Hover stabilization, surface alignment, yaw damping, and grounded traction all
request a desired force/torque wrench from `V3ActuatorCommandRouter`. The router
allocates that request over installed thrusters using their real directions,
force ceilings, and lever arms.

The only intentional direct Rigidbody state changes are:

- free-drive manual/automatic recovery reset;
- parity-harness scenario reset.

These are explicit scenario transitions, not normal driving physics.

### 4.2 One craft, one Rigidbody

Every assembled craft has exactly one root Rigidbody. Connectors, thrusters,
the cockpit, and the EnergyCore are children of that body and do not receive
independent Rigidbodies.

The root body uses:

- gravity enabled;
- interpolation enabled;
- continuous dynamic collision detection;
- zero authored linear damping;
- zero authored angular damping;
- maximum angular velocity 50 rad/s.

Physical damping effects come from installed actuators, not Rigidbody damping.

### 4.3 Connectors move endpoints, not the craft root

A connector hierarchy is:

```text
V3Socket.MountTransform
    -> connector RuntimePartInstance
        -> ConnectorChildMount.MountTransform
            -> endpoint RuntimePartInstance
```

The gimbal rotates its child mount. The spring translates its child mount. The
child thruster inherits that transform, so its actual force direction or force
origin changes. The connector does not apply a hidden craft force.

### 4.4 Generated assets have one owner

`V2ReferencePrototypeGenerator` owns
`Assets/HovercraftV3/Prototype/`.

Do not make durable fixes only in a generated prefab or generated
ScriptableObject. The next generator run will overwrite them. Change the
generator, regenerate, then verify the generated result.

## 5. High-level runtime architecture

```text
CraftBuildDefinition
  + ChassisDefinition
  + PartCatalog
  + SocketInstallation[]
          |
          v
CraftBuildValidator
          |
          v
V3CraftAssembler
  1. instantiate chassis
  2. discover stable sockets
  3. instantiate connector/endpoint chains
  4. calculate and apply mass + COM
  5. initialize runtime and registries
  6. validate instantiated topology
  7. bind all thrusters to the root Rigidbody
  8. install thermal/debug systems
  9. optionally install reference controllers
 10. initialize telemetry hub
          |
          v
V3ControllerPipeline.FixedUpdate
  -> gimbal intent/power
  -> drive requests
  -> hover/manual/stabilization requests
  -> vectoring/yaw-damping requests
  -> traction wrench request
  -> router resolve
  -> power allocation
  -> thruster AddForceAtPosition
```

Deterministic execution-order attributes currently are:

- `V3ControllerPipeline`: `-1000`;
- `V3ThermalController`: `-500`;
- `V3CockpitWarningController`: `-250`;
- `V3CraftTelemetryHub`: `-100`.

## 6. Repository map

### Runtime data and authoring contracts

- `Assets/HovercraftV3/Runtime/Data/V3AssemblyTypes.cs`
  - socket families, sizes, roles, endpoint and connector types;
  - power, thermal, physical, and capability enums/profiles.
- `Assets/HovercraftV3/Runtime/Data/PartDefinition.cs`
  - shared part identity, prefab, mass, power, thermal, compatibility, and
    capability data.
- `Assets/HovercraftV3/Runtime/Data/ThrusterDefinition.cs`
  - force, spool, local direction/origin, normal and overload ceilings.
- `Assets/HovercraftV3/Runtime/Data/ConnectorDefinition.cs`
  - connector kind and child endpoint compatibility/limits.
- `Assets/HovercraftV3/Runtime/Data/GimbalDefinition.cs`
- `Assets/HovercraftV3/Runtime/Data/SpringMountDefinition.cs`
- `Assets/HovercraftV3/Runtime/Data/EnergyCoreDefinition.cs`
- `Assets/HovercraftV3/Runtime/Data/CockpitDefinition.cs`
- `Assets/HovercraftV3/Runtime/Data/ChassisDefinition.cs`
- `Assets/HovercraftV3/Runtime/Data/PartCatalog.cs`

### Builds, sockets, and validation

- `Assets/HovercraftV3/Runtime/Assembly/V3Socket.cs`
- `Assets/HovercraftV3/Runtime/Assembly/ConnectorChildMount.cs`
- `Assets/HovercraftV3/Runtime/Build/CraftBuildDefinition.cs`
- `Assets/HovercraftV3/Runtime/Build/CraftBuildValidator.cs`

### Runtime assembly and services

- `Assets/HovercraftV3/Runtime/Runtime/V3CraftAssembler.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3CraftRuntime.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3CraftMassCalculator.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3RuntimeBuildValidator.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3CraftRegistries.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3CraftTelemetryHub.cs`

### Runtime parts

- `Assets/HovercraftV3/Runtime/Runtime/RuntimePartInstance.cs`
- `Assets/HovercraftV3/Runtime/Runtime/RuntimeThrusterInstance.cs`
- `Assets/HovercraftV3/Runtime/Runtime/RuntimeGimbalInstance.cs`
- `Assets/HovercraftV3/Runtime/Runtime/RuntimeSpringMountInstance.cs`

### Control, power, and feedback

- `Assets/HovercraftV3/Runtime/Runtime/V3ControllerPipeline.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3ActuatorCommandRouter.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3PowerDistributor.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3DriveController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3HoverController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3VectorController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3TractionController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3GimbalController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3ThermalController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3CockpitWarningController.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3PilotInputAdapter.cs`

### Runtime test-drive and debug presentation

- `Assets/HovercraftV3/Runtime/Runtime/V3FreeDriveSession.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3FreeDriveCamera.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3FreeDriveHud.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3ThrusterDebugView.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3ThermalDebugDisplay.cs`
- `Assets/HovercraftV3/Runtime/Runtime/V3CockpitWarningDisplay.cs`

### Editor tools and generators

- `Assets/HovercraftV3/Editor/V2ReferencePrototypeGenerator.cs`
- `Assets/HovercraftV3/Editor/CraftBuilderWindow.cs`
- `Assets/HovercraftV3/Editor/V3CraftAssemblerEditor.cs`
- `Assets/HovercraftV3/Editor/V3FreeDriveSceneGenerator.cs`
- `Assets/HovercraftV3/Editor/V3ParityHarnessGenerator.cs`

### Tests

All V3 tests are in `Assets/HovercraftV3/Tests/Editor/`.

## 7. Data model and compatibility

### 7.1 Socket contract

Every `V3Socket` has:

- a stable, unique socket ID;
- family and size;
- a `MountTransform`;
- supported mass, force, and power limits;
- power/data availability;
- allowed direct endpoint categories;
- allowed connector kinds;
- optional paired-socket and configuration-group identity.

Compatibility is checked before assembly by `CraftBuildValidator`.

Supported socket families currently include:

- `HeavyPropulsion`;
- `HoverVerticalControl`;
- `LateralControl`;
- `Cockpit`;
- `EnergyBay`;
- `InternalEquipment`.

### 7.2 Build contract

`CraftBuildDefinition` is a saved ScriptableObject containing:

- vehicle identity and written driving description;
- a `ChassisDefinition`;
- a `PartCatalog`;
- one `SocketInstallation` per authored socket selection.

An installation can be:

- empty;
- direct endpoint;
- connector plus child endpoint.

The same endpoint definition may be installed at multiple sockets. Runtime
instances are distinct and are indexed by both stable part ID and socket ID.

### 7.3 Capabilities

Capabilities are flags:

- drive control;
- hover control;
- stabilization;
- vectoring control;
- thermal telemetry;
- power distribution.

The cockpit provides drive/hover/stabilization/vectoring capability. The
EnergyCore provides power distribution. If a required capability has no
installed provider, authoring validation rejects the build. If a provider is
disabled at runtime, the controller pipeline removes the corresponding pilot
authority instead of continuing through an abstract fallback.

## 8. Current generated Apex V3 layout

### 8.1 Chassis

- Visual/collision size: `3.6 x 1.55 x 7.8 m`.
- Chassis base mass: `8,430 kg`.
- Current authored base COM:
  `(0, -0.65262157, 0.004151838)`.
- Parity COM calibration: `(0, 0, 0)`.
- Final reference runtime mass: `11,000 kg`.
- Final reference runtime COM: `(0, -0.5, 0)`.
- One root BoxCollider.

Socket, connector, thruster, cockpit, EnergyCore, and outrigger primitive
visuals are collider-free. The single chassis collider prevents hidden
overlapping compound shapes and keeps the V3 inertia tensor equal to the V2
reference.

Outrigger visuals are part of the chassis presentation and do not have
independent part mass entries. Their structural mass is implicitly included in
the chassis base mass.

### 8.2 Hardware orientation rule

`ThrusterDefinition.LocalThrustDirection` is local `+Z`.

The visible hardware extends along local `-Z`, opposite the applied thrust.
This rule works for front, rear, side, upper, and lower hardware and is
important for future art replacement.

### 8.3 Four drone-style outriggers

There are four thin side extensions:

- front-left;
- front-right;
- rear-left;
- rear-right.

Each extension is approximately:

- `0.85 m` across the craft;
- `0.22 m` tall;
- `0.95 m` long.

Their centers are at approximately:

- `x = +/-2.175 m`;
- `y = 0`;
- `z = +/-3 m`.

Each carries:

- a hover socket underneath;
- a roof/downforce socket on top;
- a strafe socket on the outer end.

The direct hover and roof thruster bodies are intentionally shallow. Their
complete socket-plus-thruster stacks remain inside the chassis's original
vertical silhouette, keeping the craft wide, long, and low. The spring variant
may extend slightly below this envelope because it intentionally adds moving
suspension travel.

### 8.4 Stable socket IDs

Heavy propulsion:

- `Propulsion.Rear.Center`
- `Braking.Front.Center`

Bottom hover:

- `Hover.Front.Left.Bottom`
- `Hover.Front.Right.Bottom`
- `Hover.Rear.Left.Bottom`
- `Hover.Rear.Right.Bottom`

Top control/downforce:

- `Control.Front.Left.Top`
- `Control.Front.Right.Top`
- `Control.Rear.Left.Top`
- `Control.Rear.Right.Top`

Lateral:

- `Strafe.Left.Front`
- `Strafe.Right.Front`
- `Strafe.Left.Rear`
- `Strafe.Right.Rear`

Internal:

- `Core.Main`
- `Cockpit.Main`

Total sockets: 16. Thruster sockets: 14.

### 8.5 Prototype colors

- chassis: dark blue-gray;
- outriggers: lighter slate;
- sockets: yellow;
- connectors: orange;
- thrusters: blue;
- EnergyCore: green;
- cockpit: cyan.

These are orientation/readability aids, not final art.

## 9. Reference hardware

| Part | Count | Mass each | Base force/output |
| --- | ---: | ---: | --- |
| Rear main thruster | 1 | 300 kg | 480 kN base; 720 kN normal installed ceiling; 900 kN overload |
| Front brake thruster | 1 | 250 kg | 1.7 MN physical ceiling; 380 kN normal drive request plus traction authority |
| Bottom hover thruster | 4 | 100 kg | 160 kN base; up to 7x installed output |
| Top control thruster | 4 | 80 kg | 160 kN base; up to 7x installed output |
| Lateral thruster | 4 | 75 kg | 800 kN physical ceiling; normal strafe command is 0.19 = 152 kN |
| EnergyCore | 1 | 600 kg | 4,500 continuous; 4,500 propulsion; 1,000 systems |
| Cockpit/control rack | 1 | 400 kg | drive, hover, stabilization, vectoring capabilities |

Reference installed part mass is `2,570 kg`, producing the `11,000 kg` total
with the `8,430 kg` chassis.

The EnergyCore has no temporary peak output in the reference asset.

## 10. Current craft presets

### Apex V3 Reference

- Asset:
  `Assets/HovercraftV3/Prototype/Builds/ApexV3_V2Reference.asset`
- Assembler prefab:
  `Assets/HovercraftV3/Prototype/Prefabs/ApexV3_V2Reference_Assembler.prefab`
- Layout: direct-mount / balanced.
- Mass: `11,000 kg`.
- Installed runtime parts: 16.
- Purpose: parity baseline and control craft for comparisons.

### Apex V3 Vector

- Asset:
  `Assets/HovercraftV3/Prototype/Builds/ApexV3_MainGimbalDemo.asset`
- Assembler prefab:
  `Assets/HovercraftV3/Prototype/Prefabs/ApexV3_MainGimbalDemo_Assembler.prefab`
- Layout: rear-main gimbal.
- Mass: `11,120 kg`.
- Installed runtime parts: 17.
- Reuses the exact same main thruster definition.
- Gimbal:
  - mass `120 kg`;
  - pitch and yaw `+/-30 degrees`;
  - speed `180 deg/s`;
  - angular acceleration `720 deg/s^2`;
  - actuator torque telemetry ceiling `25,000 Nm`;
  - powered systems part.

The main thruster is a child of the gimbal's actuated mount. Both the moving
connector visual and the thruster visibly swivel.

### Apex V3 Terrain

- Asset:
  `Assets/HovercraftV3/Prototype/Builds/ApexV3_HoverSpringDemo.asset`
- Assembler prefab:
  `Assets/HovercraftV3/Prototype/Prefabs/ApexV3_HoverSpringDemo_Assembler.prefab`
- Layout: front-left spring hover.
- Mass: `11,035 kg`.
- Installed runtime parts: 17.
- Reuses the exact same hover thruster definition.
- Spring:
  - mass `35 kg`;
  - compression travel `0.30 m`;
  - extension travel `0.12 m`;
  - total travel `0.42 m`;
  - stiffness `600,000 N/m`;
  - damping `18,000 Ns/m`;
  - maximum integration step `1/240 s`.

Only the front-left hover corner uses a spring. This is intentionally a
single-corner demonstrator, not a complete four-corner suspension package.

## 11. Assembly lifecycle

`V3CraftAssembler.Rebuild()` performs the following:

1. Validate the saved build.
2. Destroy the previous assembled runtime root.
3. Instantiate the chassis under `runtimeParent`, or under the assembler if
   `runtimeParent` is null.
4. Discover all `V3Socket` children and index them by stable socket ID.
5. Instantiate each selected connector at the socket mount.
6. Resolve its `ConnectorChildMount`.
7. Instantiate the selected endpoint directly under the socket mount or under
   the connector child mount.
8. Calculate and apply mass and center of mass.
9. Initialize `V3CraftRuntime`.
10. Initialize socket, part, and capability registries.
11. Perform independent runtime topology and mass validation.
12. Bind every runtime thruster to the one root Rigidbody.
13. Initialize thruster debug and thermal systems.
14. Optionally install the reference controller pipeline.
15. Initialize the telemetry hub.

The Runtime Parent field is optional. Leave it empty unless the generated craft
must be placed under a specific transform.

## 12. Mass, registries, validation, and telemetry

### Mass

`V3CraftMassCalculator` calculates:

```text
total mass =
    chassis base mass
    + every installed connector mass
    + every installed endpoint mass
```

Center of mass is a mass-weighted sum of the chassis base COM and every
instantiated part's transformed local COM. It then adds the explicitly visible
`ParityCenterOfMassCalibration`, which is zero for the current reference.

The generator assembles a temporary reference craft and recalibrates the
chassis base COM so regenerating after socket geometry changes still lands on
the required final COM.

### Registries

- `V3SocketRegistry`: stable socket lookup, pairing, groups.
- `V3PartRegistry`: all runtime instances by socket and stable part ID;
  connector and typed endpoint resolution.
- `V3CapabilityRegistry`: aggregate provided/required capabilities and active
  operational providers.

Connector and endpoint instances deliberately share the same parent socket ID.
The registry preserves both records.

### Runtime validation

`V3RuntimeBuildValidator` independently verifies:

- all required runtime services exist;
- exactly one root Rigidbody exists;
- runtime socket count matches the chassis;
- connector/endpoint chain counts and definitions match the saved build;
- endpoints are children of connector moving mounts;
- required capabilities are satisfied;
- runtime mass and COM match the calculator.

An invalid runtime assembly is destroyed before reference controllers are
installed.

### Telemetry

`V3CraftTelemetryHub` is the read-only observation boundary. It exposes:

- build identity;
- mass and COM;
- Rigidbody motion;
- socket/capability/validation state;
- systems and propulsion power;
- power mode and allocation by priority;
- thermal aggregates;
- per-part runtime state;
- thruster forces;
- gimbal motion and torque telemetry;
- spring displacement, load, and energy telemetry.

Do not add command or mutation responsibilities to the telemetry hub.

## 13. Controller and force behavior

### Drive

`V3DriveController` maps signed throttle to:

- rear main for positive drive;
- front brake thruster for reverse/braking.

Current calibration:

- throttle rise `4.5/s`;
- throttle fall `5/s`;
- main installed multiplier `1.5`;
- brake drive multiplier `0.22352941`;
- emergency main multiplier `1.25` on top of the normal request.

### Hover and surface stabilization

`V3HoverController` probes from each installed bottom hover-thruster force
origin.

Current values:

- target distance from the external force origin: `3.075 m`;
- sphere radius `0.1 m`;
- range `7 m`;
- fallback sphere radius `0.35 m`;
- track mask: layer 8;
- height gain `0.45`;
- vertical damping `0.12`;
- corner angular damping `0.08`;
- pitch sensitivity `0.589`;
- surface alignment strength `60`;
- surface alignment damping `4`;
- maximum alignment acceleration `60`;
- speed stiffness from `80` to `600`;
- maximum stiffness multiplier `3.5`.

Ground normals are averaged from the four bottom probes. Desired alignment
acceleration is converted to required world torque, then submitted across the
installed bottom and roof thrusters. There is no direct stabilization torque.

### Vectoring and yaw damping

`V3VectorController` uses the four lateral thrusters.

Current values:

- strafe sensitivity `0.19`;
- steering sensitivity `0.2975`;
- steering yaw authority `2.8`;
- yaw damping `5.35`.

Yaw damping is converted to required world torque and solved over the lateral
thrusters.

### Traction

`V3TractionController` acts only when grounded. It computes desired local
lateral and longitudinal acceleration from velocity and grip-breaker state,
converts that to a force request, and submits the wrench over:

- four lateral thrusters;
- rear main;
- front brake.

It does not apply drag or modify velocity directly.

### Gimbal

`V3GimbalController` sends pitch/yaw intent to installed gimbals, resolves
their systems power, and advances the powered mount. It uses a `0.2 s`
transient input hold and returns at `4 input units/s`.

The gimbal itself does not torque the root craft. Its physical effect comes
from the child main thruster's changed direction.

## 14. Actuator routing and power

### Router

`V3ActuatorCommandRouter` owns one entry per thruster socket and aggregates
requests by:

- channel: drive, base hover, stabilization, vectoring, manual;
- priority: optional, drive, vectoring, stabilization, critical.

For general force/torque requests, `SubmitWrench` builds candidates from the
actual installed thrusters and their:

- world force directions;
- force origins;
- COM-relative lever arms;
- force ceilings;
- existing requests;
- thermal limits;
- overload authorization.

It uses a bounded non-negative iterative allocation. It never invents a force
direction that the installed hardware cannot produce.

### Power modes

`V3PowerDistributor` reserves systems power, applies EnergyCore ceilings, and
allocates propulsion demand in priority tiers. Demand within one tier is
scaled proportionally, avoiding installation-order bias.

| Mode | Priority order |
| --- | --- |
| Balanced | Critical -> Stability -> Steering -> Drive -> Optional |
| Propulsion | Critical -> Drive -> Steering -> Stability -> Optional |
| Stability | Stability -> Critical -> Steering -> Drive -> Optional |
| Recovery | Critical -> Stability -> Drive -> Steering -> Optional |

Modes may feel identical while the core has sufficient headroom. Their effect
appears when total requested power exceeds available power.

Tab cycles:

```text
Balanced -> Propulsion -> Stability -> Recovery -> Balanced
```

## 15. Thermal and overload behavior

Each `RuntimePartInstance` owns its thermal state:

- current temperature;
- generated heat;
- passive cooling;
- output limit;
- protection state;
- lockout timer.

Protection states:

- Normal;
- Warning;
- Overheated;
- CoolingLockout;
- Recovered.

Generated heat depends on actual output. Passive cooling is proportional to
temperature above ambient. Above the safe threshold, output progressively
derates. At overheat, the part locks out. Restart requires both:

- cooling to the restart temperature;
- expiry of the minimum cooling lockout.

Default generated thermal profile:

- ambient `20 C`;
- safe `90 C`;
- overheat `120 C`;
- restart `75 C`;
- minimum lockout `2 s`;
- overload heat multiplier `2.2`.

The main thruster has deliberately lower thermal capacity and cooling plus a
less severe hot output limit so sustained overload can visibly reach warning
and shutdown while normal driving remains viable.

Emergency overload is explicit pilot intent. Holding Ctrl authorizes the
router to use the overload ceiling for that frame. It does not bypass power or
thermal limits.

## 16. Player controls

Keyboard/mouse:

| Input | Action |
| --- | --- |
| W / S | Forward throttle / braking-reverse |
| A / D | Strafe left / right |
| Mouse X / Y | Yaw / pitch |
| Arrow keys | Held gimbal/yaw test through gamepad-look action |
| E | Manual lift |
| Q | Roof downforce |
| Shift | Grip breaker / drift |
| Ctrl | Emergency overload |
| R | Toggle stabilization |
| Tab | Cycle power allocation mode |
| Backspace | Reset craft |
| F1 | Toggle free-drive HUD |
| F2 | Toggle thruster debug visualization |
| Escape | Release cursor and pause pilot input |
| Left click | Recapture cursor and resume pilot input |

Gamepad:

- triggers: signed throttle;
- left stick X: strafe;
- right stick: yaw/pitch;
- right shoulder: lift;
- left shoulder: downforce;
- east face button: grip breaker;
- south face button: overload;
- Start: stabilization;
- D-pad up: power mode;
- Select: reset.

Input actions are created at runtime by `V3PilotInputAdapter`. Binding overrides
can be serialized to and restored from JSON.

## 17. Debugging and free-drive testing

### Free-drive scene

Scene:

`Assets/HovercraftV3/FreeDrive/Scenes/V3_FreeDrive_Test.unity`

Generate or refresh it with:

`Tools > Hovercraft V3 > Generate Free Drive Test Scene`

It includes:

- flat driving space;
- lane guides;
- inclined surfaces;
- stepped and bumpy suspension-test features;
- spawn/recovery support;
- camera and HUD;
- test controls described above.

The default generated scene uses the reference assembler. To test another
preset, change the assembler's `Build` field or place the desired assembler
prefab.

### Thruster debug

Press F2 in free drive.

- Thin arrow: installed facing/thrust direction.
- Thick/long arrow: actual force while firing.
- Arrow length/width: force magnitude.
- Blue -> green -> yellow -> red: individual temperature.
- Red/locked state: thermal lockout.
- Purple: averaged ground/surface normal.
- White: current craft up direction.
- `G pitch/yaw`: live gimbal angles.

World labels show socket ID, output, force in kN, and temperature.

### Inspector vehicle card

`V3CraftAssemblerEditor` displays:

- name, class, layout, summary, and driving notes;
- mass;
- dimensions;
- forward/braking/hover/lateral force;
- power;
- balance/COM information;
- installed hardware;
- all power-mode descriptions.

This is the intended "car selection" information surface for the current
prototype stage.

## 18. Editor authoring workflow

### Regenerate canonical prototype assets

Unity menu:

`Tools > Hovercraft V3 > Generate V2 Reference Prototype`

This regenerates:

- chassis and part prefabs;
- materials;
- definitions;
- part catalog;
- all three build assets;
- all three assembler prefabs.

### Build editor

Unity menu:

`Tools > Hovercraft V3 > Craft Builder`

The window supports:

- creating/loading a build asset;
- editing vehicle identity and written description;
- selecting direct endpoints or connector/endpoint chains;
- compatibility explanations;
- mass and power summaries;
- complete build validation;
- resetting selections to the V2 reference;
- saving the build;
- rebuilding a selected assembler.

### Runtime assembler

Place one of the assembler prefabs or add `V3CraftAssembler` to a GameObject.

- Assign `Build`.
- Keep `Assemble On Start` enabled for normal use.
- Keep `Install Reference Controllers` enabled for a player-drivable craft.
- `Runtime Parent` is optional.

Use the component context menu `Rebuild V3 Craft` when required.

## 19. Automated verification

### Current passing state

Fast EditMode suite:

`Temp/CodexV3UnityProject/TestResults-outriggers-fast.xml`

- total: 80;
- passed: 79;
- failed: 0;
- skipped: 1.

The skipped test is the intentionally opt-in, 78-second parity capture.

Enforced parity capture:

`Temp/CodexV3UnityProject/TestResults-outriggers-parity.xml`

- total: 1;
- passed: 1;
- failed: 0.

Latest telemetry run ID: `20260727_160919`.

| Metric | V2 | V3 |
| --- | ---: | ---: |
| Mass | 11,000 kg | 11,000 kg |
| COM | `(0,-0.5,0)` | `(0,-0.5,~0)` |
| Inertia tensor | `(60722.30,67650,16832.29)` | identical |
| Idle clearance | 3.4080 m | 3.4050 m |
| Maximum speed | 7063.40 km/h | 7077.86 km/h |
| Time to 500 km/h | 2.2001 s | 2.2001 s |
| Time to 1000 km/h | 4.3201 s | 4.3001 s |
| Brake/reverse distance | 788.06 m | 794.73 m |
| Peak yaw rate | 413.405 deg/s | 413.152 deg/s |
| Peak side speed | 13.095 km/h | 12.610 km/h |
| Peak pitch | 3.5569 deg | 3.5590 deg |

Asset audit after the outrigger regeneration:

- 132 V3 assets/directories and metadata GUIDs checked;
- zero missing `.meta` files;
- zero orphan `.meta` files;
- zero duplicate V3 GUIDs;
- four generated outriggers;
- 14 external socket visuals;
- 14 endpoint mounts;
- one chassis BoxCollider.

### Important test files

- `CraftBuildValidatorTests.cs`: compatibility and authoring errors.
- `V2ReferencePrototypeTests.cs`: reference topology, mass, COM, external
  hardware, outrigger layout, one-body/one-collider rules.
- `V3HandlingControllerTests.cs`: controller math and physical allocation.
- `V3ParityHarnessTests.cs`: generated scene and hard parity assertions.
- `V3ThermalFoundationTests.cs`: thermal behavior.
- `V3OverloadProtectionTests.cs`: overload and protection state machine.
- `V3GimbalVariationTests.cs`: moving gimbal hierarchy and physical effect.
- `V3SpringMountVariationTests.cs`: spring limits, hierarchy, and coupled hover.
- `V3PowerDistributionModeTests.cs`: allocation modes and constrained power.
- `V3CraftTelemetryHubTests.cs`: telemetry ownership/snapshots.
- `V3CraftRegistryTests.cs`: runtime identity and capabilities.
- `V3CraftKernelServicesTests.cs`: mass and runtime validation.
- `V3PlayerFeedbackAndRecoveryTests.cs`: input, warnings, reset, slope/air tests.

### Batch test guidance

Unity executable:

`C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe`

When using `-runTests`, do not add `-quit`; Unity's test runner exits itself.

Fast suite arguments:

```text
-batchmode
-nographics
-projectPath "<isolated project path>"
-runTests
-testPlatform EditMode
-assemblyNames Lunarlight.Hovercraft.V3.EditorTests
-testResults "<absolute results XML path>"
-logFile "<absolute log path>"
```

Parity capture additionally requires:

```text
environment:
HOVERCRAFT_V3_RUN_PARITY_CAPTURE=1

argument:
-testFilter Lunarlight.Hovercraft.V3.Tests.V3ParityHarnessTests.ParityScene_CompletesFullProfileAndExportsTelemetry
```

Parity output is written below:

`Application.persistentDataPath/HovercraftV3/Parity/`

For the isolated project on this machine that has been:

`C:\Users\Jack_\AppData\LocalLow\DefaultCompany\CodexV3UnityProject\HovercraftV3\Parity`

Unity sometimes emits an initial licensing-client handshake warning before
successfully resolving the installed entitlement. Judge the run by the Unity
process exit code, XML result, and subsequent licensing resolution—not by that
initial line alone.

## 20. Parity harness

Scene:

`Assets/HovercraftV3/Parity/Scenes/V2_V3_FlatGround_Parity.unity`

Profile:

`Assets/HovercraftV3/Parity/Profiles/V2_V3_Parity_Command_Profile.asset`

Generate/refresh:

`Tools > Hovercraft V3 > Generate Gate 3 Parity Harness`

The 78-second profile covers:

- settle;
- idle hover;
- acceleration;
- braking/reverse from 500 km/h;
- yaw pulses;
- strafe;
- pitch response.

Scenario boundaries deliberately reset both craft to equivalent state.

Hard assertion tolerances:

- mass, speed, acceleration time, brake distance: 2% relative;
- center of mass: 0.05 m absolute;
- idle clearance: 0.05 m absolute;
- yaw, strafe, pitch: 5% relative.

The V2 clone translates its configured forces into legacy
`ForceMode.Acceleration` units. V3 uses native newtons and
`ForceMode.Force`.

## 21. Known limitations and intentional non-goals

These are not unfinished gates, but future work must understand them:

1. Prototype visuals are cubes, not final vehicle art.
2. Only the chassis has a collider. External parts are real mass/power/thermal
   and actuator entities but do not have separate collision hitboxes.
3. Part damage, detachable hardware, and per-part collision responses are not
   implemented.
4. Outriggers are chassis visuals, not equippable mass-bearing definitions.
5. The terrain preset has one spring corner only.
6. The vector preset has one rear-main gimbal only.
7. Power modes intentionally feel identical until power is constrained.
8. The current hover target is calibrated from the external hover-thruster
   force origins. Moving those origins requires parity revalidation.
9. The free-drive recovery operation writes Rigidbody pose/velocity by design.
   Do not copy that behavior into normal controllers.
10. The separate aerodynamics documents describe systems under
    `Assets/Scripts/Hovercraft_Setup/Aerodynamics/`. The generated Apex V3
    reference chassis does not currently install that system. Do not assume
    those reports describe the active `Assets/HovercraftV3` controller stack.
11. Final player balance and presentation still require broader hands-on
    playtesting.

## 22. Safe extension recipes

### Add a new endpoint part

1. Derive a ScriptableObject definition from `EndpointDefinition`.
2. Give it stable identity, role, size, physical/power/thermal profiles, and
   compatible parent families.
3. Create a prefab with the appropriate runtime instance component.
4. Add it to a `PartCatalog`.
5. Extend `V3CraftAssembler.InstantiatePart` only if it needs a new specialized
   runtime instance.
6. Add validation and registry/telemetry coverage.
7. Add tests before adding it to generated builds.

### Add a new connector

1. Derive from `ConnectorDefinition`.
2. Author a prefab containing `ConnectorChildMount`.
3. The endpoint must be instantiated below that mount.
4. Connector motion may change only the child mount.
5. Do not add a second Rigidbody or direct root force.
6. Extend validation, runtime type selection, telemetry, and tests.

### Change socket or outrigger geometry

1. Edit `V2ReferencePrototypeGenerator`.
2. Preserve stable socket IDs unless performing an explicit migration.
3. Preserve local `+Z` as thrust and local `-Z` as visible hardware direction.
4. Regenerate all prototype assets.
5. Confirm 16 sockets, 14 external mounts, one Rigidbody, and one collider.
6. Confirm reference mass and final COM.
7. Run the full fast suite.
8. Run the opt-in physical parity capture.
9. Retune controller intent only if the new real lever arms/inertia require it;
   never add compensating virtual force or torque.

### Add part hitboxes

This is future work. Do not simply restore the old primitive colliders. Design
explicit, non-overlapping compound hitboxes and decide:

- whether they affect Rigidbody inertia;
- whether part hitboxes are collision-only, damage-only, or both;
- how chassis base mass and COM should change;
- whether the one-root-Rigidbody rule remains;
- how parity will be retained or intentionally superseded.

### Add a new controller effect

1. Express the desired force/torque intent.
2. Choose eligible installed socket IDs.
3. Submit through `V3ActuatorCommandRouter`.
4. Let power and thermal systems constrain output.
5. Verify loss of authority when required parts are disabled.
6. Add a physical integration test proving there is no direct Rigidbody force.

## 23. Do-not-regress checklist

Before considering a Hovercraft V3 change complete, confirm:

- [ ] No controller-level `AddForce` or `AddTorque`.
- [ ] No normal-driving Rigidbody pose or velocity writes.
- [ ] Exactly one root Rigidbody.
- [ ] Exactly one current prototype chassis collider.
- [ ] Connector endpoint is below the moving child mount.
- [ ] Thruster visual moves with gimbal/spring articulation.
- [ ] Stable socket IDs are unchanged or explicitly migrated.
- [ ] Mass calculator includes every installed connector and endpoint.
- [ ] Runtime validator passes.
- [ ] Capability loss removes corresponding control authority.
- [ ] Power shortage is resolved by the selected allocation mode.
- [ ] Thermal derating and lockout constrain actual output.
- [ ] Debug/telemetry read authoritative runtime state only.
- [ ] Generator source and generated assets agree.
- [ ] No missing, orphan, or duplicate `.meta` GUIDs.
- [ ] Fast suite has zero failures.
- [ ] Physical parity capture passes after geometry, collider, force, mass, COM,
      or controller-gain changes.

## 24. Related reports

Current summary:

- `Docs/Hovercraft_V3_Final_Audit_2026-07-27.md`
- `Docs/Hovercraft_V3_V2_Mapping.md`

Gate reports:

- `Docs/Hovercraft_V3_Gate3_Parity_Report.md`
- `Docs/Hovercraft_V3_Gate4_Thermal_Report.md`
- `Docs/Hovercraft_V3_Gate5_Gimbal_Report.md`
- `Docs/Hovercraft_V3_Gate6_Overload_Thermal_Protection_Report.md`
- `Docs/Hovercraft_V3_Gate7_Player_Feedback_Recovery_Report.md`
- `Docs/Hovercraft_V3_Gate8_Spring_Mount_Report.md`
- `Docs/Hovercraft_V3_Gate9_Power_Distribution_Report.md`
- `Docs/Hovercraft_V3_Gate10_Craft_Telemetry_Hub_Report.md`
- `Docs/Hovercraft_V3_Gate11_Runtime_Registries_Report.md`
- `Docs/Hovercraft_V3_Gate12_Mass_Runtime_Validation_Report.md`

## 25. Recommended next phase

The foundation is ready for product work. Reasonable next tracks are:

- replace primitive art while preserving transforms and socket conventions;
- add deliberately designed per-part hitboxes and a damage model;
- expand the part catalog and create more genuinely distinct vehicle builds;
- upgrade the builder into a player-facing garage/configurator;
- install or adapt the project aerodynamics system for the Apex V3 chassis;
- perform broader player handling/balance tests.

Whichever track is selected, preserve the physical actuator authority rule and
run parity before and after any change that alters mass, COM, collider inertia,
force origin, force direction, or controller gains.
