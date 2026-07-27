# Hovercraft V3 — V2 Parity Mapping

**Baseline:** `CraftConfigs/Hovercraft_V2_Apex_Hyperclass_11T_Rev1_1.json`  
**Behavior reference:** `Assets/Prefabs/HovercraftRootV2.prefab` plus the V2 runtime scripts under `Assets/Scripts/Hovercraft_Setup/Hovercraft/`  
**Status:** Implemented through Gate 12; final physical-only audit passed on
2026-07-27

## Baseline authority

The Rev1.1 config is the source of truth for physical tuning. The prefab contains
values that are subsequently applied by the existing V2 configuration tooling
(for example, its serialized Rigidbody mass is still a placeholder). V3 parity
calibration must therefore cite the config value and never silently copy a
placeholder prefab value.

V2 remains untouched. V3 code and assets live under `Assets/HovercraftV3/` and
use the `Lunarlight.Hovercraft.V3` namespace.

## Physical inventory

| V2 hardware | Count | Position(s), local metres | V2 force | V3 representation |
|---|---:|---|---:|---|
| Chassis/root Rigidbody | 1 | COM `(0, -0.5, 0)` | 11,000 kg configured mass | `ChassisDefinition`, chassis prefab, `V3CraftRuntime` |
| Main rear thruster | 1 | `(0, 0, -3.9)` | 480 kN, 1.5× overload reserve | Direct `ThrusterDefinition` endpoint on `HeavyPropulsion` |
| Front brake thruster | 1 | `(0, 0, 3.9)` | 380 kN drive plus legacy counter-trajectory grip | Direct `ThrusterDefinition` endpoint with a visible 1.70 MN combined physical ceiling on `HeavyPropulsion` |
| Bottom hover thrusters | 4 | `x ±1.5, y -0.725, z ±3` | 160 kN each, 7× emergency authority | `ThrusterDefinition` endpoints on `HoverVerticalControl` |
| Roof control thrusters | 4 | `x ±1.5, y -0.665, z ±3` | 160 kN each, 7× emergency authority | `ThrusterDefinition` endpoints on `HoverVerticalControl` |
| Lateral thrusters | 4 | `x ±1.8, y 0, z ±3` | 160 kN nominal plus legacy direct yaw damping | 800 kN physical-equivalent `ThrusterDefinition` endpoints; full strafe commands 152 kN each and steering/damping use the remaining authority |
| Energy core | 1 | internal | 4,500 power units continuous | `EnergyCoreDefinition` endpoint on `EnergyBay` |
| Cockpit/control rack | 1 | internal | input and controller services | `CockpitDefinition` plus V3-only controller components |

Initial authored socket IDs:

- `Propulsion.Rear.Center`
- `Braking.Front.Center`
- `Hover.Front.Left.Bottom`
- `Hover.Front.Right.Bottom`
- `Hover.Rear.Left.Bottom`
- `Hover.Rear.Right.Bottom`
- `Control.Front.Left.Top`
- `Control.Front.Right.Top`
- `Control.Rear.Left.Top`
- `Control.Rear.Right.Top`
- `Strafe.Left.Front`
- `Strafe.Right.Front`
- `Strafe.Left.Rear`
- `Strafe.Right.Rear`
- `Core.Main`
- `Cockpit.Main`

## Responsibility mapping

| V2 type or concept | Current responsibility | V3 destination |
|---|---|---|
| `CraftCore` | FixedUpdate orchestration and dependency wiring | V3 craft runtime plus explicit controller pipeline |
| `PilotCommandInterface` | Input System adapter and one-shot input latches | V3-only input adapter; same bindings during parity |
| `DriveCore` | Forward/brake command shaping | V3 drive controller submitting actuator requests |
| `HoverStabilizerArray` | Hover probes, cushion, roof retention, pitch/roll contribution | V3 hover controller and stabilizer submitting requests |
| `VectorThrusterArray` | Differential strafe/yaw and yaw damping | V3 vector controller submitting requests |
| `ThrusterBus` | Single command path, channel combination, power scaling | V3 actuator command router and power grants |
| `ThrusterNode` | Spool, probing, gimbal direction, force application | Runtime thruster instance plus separate connector behavior |
| `EnergyCore` | Central power budget and priority/shedding rules | Energy-core part plus V3 power distributor |
| `TelemetryMainframe` | Craft and actuator measurements | V3 telemetry hub |
| `TractionCore` | Physical traction forces | V3 traction intent solved onto installed thrusters; the controller applies no force |
| `OverchargeCore` | Temporary command/power behavior | Explicit V3 emergency-overload command, power, heat, derating, lockout, and recovery path |
| `AttitudeControlArray` / `VectoringComputer` | Control request derivation | V3 controllers; never direct writes to actuators |
| Aerodynamics and track surface | World/vehicle interaction | Remains runtime/world logic; not a chassis part |

## Locked starting families

- `HeavyPropulsion`
- `HoverVerticalControl`
- `LateralControl`
- `Cockpit`
- `EnergyBay`
- `InternalEquipment`

These are deliberately limited to the observed V2 inventory.

## Known parity caveats

1. The V2 `ThrusterNode` currently combines endpoint thrust with gimbal behavior
   on hover and roof nodes. V3 must separate those responsibilities. The first
   saved reference build remains direct-mounted as required by GDD v0.5; a
   connector variation is introduced only after direct parity is measurable.
2. Several V2 thrusters use a serialized rise/fall rate of zero. In V2 this is
   effectively immediate response. V3 definitions must preserve that behavior
   for the reference assets even though the architecture supports finite spool.
3. The repeatable flat-ground profile is now enforced by
   `V3ParityHarnessTests.ParityScene_CompletesFullProfileAndExportsTelemetry`.
   Final post-outrigger run `20260727_160919` passed all declared tolerances.

## Generated Gate 1/2 reference assets

The repeatable generator is available at:

`Tools > Hovercraft V3 > Generate V2 Reference Prototype`

It owns `Assets/HovercraftV3/Prototype/` and creates:

- the primitive Apex V3 reference chassis prefab with 16 authored sockets;
- V2-reference main, brake, hover, roof, and strafe thruster definitions;
- EnergyCore and cockpit definitions;
- fixed-adapter and main-gimbal connector definitions;
- the central V2-reference part catalog;
- the direct-mounted `ApexV3_V2Reference` build;
- the `ApexV3_MainGimbalDemo` build, which reuses the same main-thruster endpoint;
- ready-to-place assembler prefabs for both builds.

The generated visuals are deliberately primitive. They establish socket
positions, force orientations, serialization, compatibility, and runtime
assembly without coupling the architecture to final art.

All 14 thruster sockets expose their construction outside the chassis:

`chassis -> socket block -> optional connector -> thruster`

The socket, connector, and thruster bodies extend opposite local thrust
direction, so rear, front, side, roof, and belly hardware all project away
from the hull. The four vertical hover/control pairs use thin, drone-style
outriggers at the front-left, front-right, rear-left, and rear-right stations.
Each extension carries a hover socket underneath, a roof-control socket on
top, and a strafe socket on its outer end. The direct-mount vertical thrusters
are shallow enough to remain within the chassis's original vertical
silhouette.

Connector endpoint visuals and their thrusters share the moving child mount,
making gimbal and spring articulation visible. Prototype hardware visuals are
collider-free; the chassis owns the single collision shape while part
definitions and their runtime transforms remain authoritative for mass, force
origin, thrust direction, power, and thermal behavior.

### Explicit prototype mass allocation

| Item | Count | Mass each | Total |
|---|---:|---:|---:|
| Chassis base | 1 | 8,430 kg | 8,430 kg |
| Main thruster | 1 | 300 kg | 300 kg |
| Brake thruster | 1 | 250 kg | 250 kg |
| Bottom hover thruster | 4 | 100 kg | 400 kg |
| Roof control thruster | 4 | 80 kg | 320 kg |
| Lateral thruster | 4 | 75 kg | 300 kg |
| EnergyCore | 1 | 600 kg | 600 kg |
| Cockpit/control rack | 1 | 400 kg | 400 kg |
| **Reference total** |  |  | **11,000 kg** |

The authored chassis base center is `(0, -0.59516, 0.001779)` metres. After
installed-part mass positions are included, the reference build calculates the
V2 target center of mass `(0, -0.5, 0)` without a hidden calibration offset.
The optional rear gimbal adds 120 kg, producing an 11,120 kg variation.

## Gate 0 measurement contract

Use Unity's configured fixed timestep and the same flat test scene for both
crafts. Record:

- total mass and center of mass;
- idle clearance and oscillation over 20 seconds;
- 0–500 km/h and 0–1,000 km/h times;
- maximum speed on the available straight;
- braking time and distance from 500 km/h;
- yaw rate after identical steering pulses at 500 and 1,000 km/h;
- sustained strafe acceleration;
- pitch/roll response and airborne command decay;
- continuous and peak power demand;
- Rigidbody interpolation, collision detection, damping, and solver settings.

## Gate 3 runtime control contract

Gate 3 uses one physical actuator path. Drive, hover, stabilization, vectoring,
and manual controls only submit normalized requests to
`V3ActuatorCommandRouter`. `V3PowerDistributor` reserves installed systems idle
demand, applies the EnergyCore continuous and propulsion ceilings, and grants
power by priority. Only `RuntimeThrusterInstance` converts a granted request
into external craft-control force. Every V3 thruster uses
`Rigidbody.AddForceAtPosition(..., ForceMode.Force)`, so every craft-control
moment is the real cross product of its installed force origin and force. The
former V2-reference lateral compatibility moment was removed on 2026-07-27.

Surface alignment, yaw damping, and grounded grip now submit desired physical
force/torque to `V3ActuatorCommandRouter`. The router solves non-negative
thruster output subject to installed direction, lever arm, output, thermal, and
power limits. Controllers never call `AddForce` or `AddTorque`.

The initial priority order is critical base hover, stabilization, vectoring,
drive, then optional/manual requests. Requests at the same priority are scaled
proportionally when the tier cannot be fully powered. This avoids
installation-order bias between paired actuators.

| Control | Gate 3 reference value |
|---|---:|
| Hover target | 3.075 m from the external hover-thruster force origin |
| Hover probe | 0.1 m sphere, 7 m range, TrackSurface layer 8 |
| Drive shaping | 4.5/s rise, 5/s fall |
| Strafe sensitivity | 0.19 against an 800 kN physical ceiling (152 kN commanded per endpoint) |
| Steering sensitivity | 0.2975 |
| Steering yaw authority | 2.8 |
| Yaw damping | 5.35 rad/s² per rad/s, requested through lateral thrusters |
| Grounded pitch sensitivity | 0.589 |
| Mouse yaw/pitch | 0.03 / 0.02 |

Reference input bindings are W/S drive and brake, A/D strafe, mouse X/Y
yaw/pitch, E manual lift, Q roof downforce, R stabilization toggle, and either
Shift key for grip breaker. Grip breaker changes the traction intent submitted
to the same physical actuator allocator; it never creates a separate force
path.

The generated reference build installs the controller pipeline automatically.
The direct and gimbal builds share the same main `ThrusterDefinition`; force
direction follows the instantiated endpoint transform, so connector motion can
change thrust direction later without creating a second actuator path.

### Gate 3 parity harness

Generate or refresh the measurement scene with:

`Tools > Hovercraft V3 > Generate Gate 3 Parity Harness`

The generator owns:

- `Assets/HovercraftV3/Parity/Scenes/V2_V3_FlatGround_Parity.unity`;
- `Assets/HovercraftV3/Parity/Profiles/V2_V3_Parity_Command_Profile.asset`.

The 78-second profile runs settle, idle-hover, acceleration, braking from
500 km/h, yaw pulses at 500 and 1,000 km/h, strafe at 500 km/h, and a pitch
pulse. Scenario boundaries reset both craft to identical positions, rotations,
linear velocities, and angular velocities.

The harness does not link the V3 assembly to V2 code. It injects the public V2
`PilotCommand` data contract and reads V2 EnergyCore telemetry through a
reflection adapter. The generated V2 scene instance is a prefab clone; the V2
prefab and scripts are never modified.

The authoritative JSON expresses thruster output in newtons, while the current
V2 `ThrusterNode` applies its `maxForce` with `ForceMode.Acceleration`. The
generated V2 clone therefore stores `configured force / 11,000 kg` in each
legacy node and multiplies the JSON power-cost coefficient by 11,000. This
preserves both physical acceleration and configured power demand while V3 uses
native newtons with `ForceMode.Force`.

One V2 limitation remains explicit in every report: the JSON specifies separate
drive rise/fall values of 4.5/s and 5/s, but the current V2 `DriveCore` exposes
one `throttleRampSpeed`. The harness sets it to the authoritative 4.5/s rise
value; braking/fall comparisons must retain this caveat until the baseline
owner chooses how to resolve it.

On completion, each craft writes a CSV trace and JSON summary beneath:

`Application.persistentDataPath/HovercraftV3/Parity/`

Summaries include mass, center of mass, inertia tensor and tensor rotation,
idle clearance and oscillation,
maximum speed, times to 500/1,000 km/h, braking start/end speed and distance,
peak yaw rate, peak side speed, and requested/granted power.

Initial acceptance tolerances should be set before tuning. A practical first
proposal is ±2% for mass, acceleration times, and top speed; ±5 cm for steady
hover height; and ±5% for yaw/strafe response.

The controlled captures and Gate 3 sign-off are recorded in
`Docs/Hovercraft_V3_Gate3_Parity_Report.md`. Final physical-only run
`20260727_160919` passes the predeclared flat-ground tolerances for mass,
center of mass, hover height, acceleration, top speed, braking/reverse, yaw,
strafe, and commanded pitch.

The parity controller installs surface-normal alignment and the V2 grounded
traction model through the controller pipeline. It preserves the V2
`Main_Rear` bus multiplier as a 1.5x installed-output request bounded by the
main thruster definition's existing output ceiling. Hull-plane hover fallback
and surface-normal point-velocity damping close the high-speed clearance gap.
Brake traction, yaw authority, yaw damping, and surface alignment are all
resolved onto visible installed thrusters; the former compatibility moment and
all controller-level Rigidbody force/torque calls are gone.

## Gate 4 thermal telemetry contract

Gate 4 centralizes thermal updates in `V3ThermalController`. Every thermally
enabled installed part is ticked exactly once per physics step and publishes a
`V3PartThermalTelemetry` snapshot containing identity, live temperature,
generated heat, passive cooling, safe/overheat/restart thresholds, normalized
temperature, and state flags.

`V3ThermalDebugDisplay` renders those snapshots directly. It does not maintain a
second thermal model or change part availability.

Gate 4 intentionally stopped at telemetry. Gate 6 subsequently implemented the
overload command, limiting, shutdown, cooling lockout, and restart behavior,
and Gate 7 connected those states to cockpit feedback.

Gate 4 evidence and regression results are recorded in
`Docs/Hovercraft_V3_Gate4_Thermal_Report.md`.

## Gate 5 connector behavior contract

The first modular departure uses `ApexV3_MainGimbalDemo`. Its rear chain installs
`V2Reference_MainGimbal` followed by the exact same main-thruster definition as
the direct reference build.

`RuntimeGimbalInstance` moves the connector child mount within the authored
angle, speed, and acceleration limits. The unchanged child
`RuntimeThrusterInstance` therefore inherits a new physical force direction
without a duplicate thruster product or a second force path.

Gimbal motion requests and receives live systems power through
`V3PowerDistributor`; its output and heat are reported through the existing
runtime-part telemetry. Builds with no installed gimbal retain the signed-off
Gate 3 behavior.

Gate 5 evidence and the direct-build regression are recorded in
`Docs/Hovercraft_V3_Gate5_Gimbal_Report.md`.

## Gate 6 overload and protection contract

Gate 6 adds `V3PilotCommand.EmergencyOverload` as an explicit Ctrl-held command;
the existing Shift-held Grip Breaker reservation is unchanged. The command
travels through the controller pipeline and authorizes the actuator router to
use an endpoint's emergency ceiling for that frame.

`ThrusterDefinition` now distinguishes the normal installed-output ceiling from
the emergency-overload ceiling. The reference rear-main path remains 1.5x in
ordinary operation and reaches 1.875x only during overload. Full overload is
therefore 125% of normal output, requests 155% of normal-ceiling power, and
generates 220% of normal maximum heat. The rear socket and both supported
connectors are rated for the resulting 900,000 N possible load.

`RuntimePartInstance` owns the shared thermal-protection state machine:
`Normal`, `Warning`, `Overheated`, `CoolingLockout`, and `Recovered`. Warning
temperature progressively derates output; overheat prevents operation; restart
requires both the authored minimum lockout time and cooling to the authored
restart threshold. Manual enable state remains separate from protection state.

Protection state, lockout, and live output limit are exported by the existing
thermal telemetry and rendered by `V3ThermalDebugDisplay`. Gate 7 adds the
cockpit-facing warning controller and display.

Gate 6 evidence and the no-overload direct-build regression are recorded in
`Docs/Hovercraft_V3_Gate6_Overload_Thermal_Protection_Report.md`.

## Gate 7 player feedback and recovery contract

Gate 7 is a post-v0.5 production-readiness extension. `V3PilotInputAdapter` now
creates rebindable Input System actions for the existing keyboard/mouse scheme
and an equivalent gamepad layout. It continues to output the device-neutral
`V3PilotCommand`, so controllers and scripted parity commands share one public
contract.

`V3CockpitWarningController` prioritizes the existing overload and thermal
telemetry into advisory, warning, and critical cockpit states.
`V3CockpitWarningDisplay` renders that state without owning protection logic.
The reference main-thruster thermal response makes sustained overload warning
and shutdown reachable while sustained normal output remains below its safe
temperature.

Automated physical scenarios now validate grounded recovery on a 12-degree
slope and manual lift authority during a correctly ungrounded airborne start.
These scenarios supplement rather than replace the flat-ground V2 parity
capture.

Gate 7 evidence and regression results are recorded in
`Docs/Hovercraft_V3_Gate7_Player_Feedback_Recovery_Report.md`.

## Gate 8 spring connector contract

Gate 8 resolves the initial spring-mount experiment with a passive,
single-authored-axis local simulation. `RuntimeSpringMountInstance` projects
the child endpoint's real force onto the movement axis and integrates endpoint
mass, stiffness, damping, compression travel, and extension travel using
substeps no larger than 1/240 second.

The connector moves only its child mount. It neither moves the root craft
directly nor filters the net external endpoint force, because all installed
mass remains part of the one root Rigidbody. The unchanged child
`RuntimeThrusterInstance` continues to own the sole force-application path at
the spring-shifted physical origin.

`ApexV3_HoverSpringDemo` replaces only the front-left direct hover installation
with `V2Reference_HoverSpringMount` followed by the exact same hover-thruster
definition. Bottom hover sockets advertise the spring connector through the
existing compatible-part builder and validation rules.

Gate 8 evidence and the direct-build parity regression are recorded in
`Docs/Hovercraft_V3_Gate8_Spring_Mount_Report.md`.

## Gate 9 player-configurable power distribution contract

Gate 9 keeps the original systems-first reservation and Balanced propulsion
order as the default, then adds Propulsion, Stability, and Recovery allocation
modes. The selected mode changes only the order in which actuator priority
tiers receive a constrained propulsion budget. Requests within one tier remain
proportional, and actual endpoint output continues to derive solely from
granted power.

The pilot cycles modes through the device-neutral
`V3PilotCommand.CyclePowerAllocationMode` command, bound by default to keyboard
Tab and gamepad D-pad up. The cockpit continuously exposes the selected mode
and promotes propulsion shedding to a warning or systems-channel shedding to a
critical alert.

`V3PowerDistributor` now reports requested and granted power per actuator
priority, total shed propulsion power, and the total propulsion grant fraction.
An in-memory constrained-core integration build proves that each mode grants
the intended tiers under a real shortage without introducing an alternate
force path.

Gate 9 evidence and the Balanced direct-build parity regression are recorded in
`Docs/Hovercraft_V3_Gate9_Power_Distribution_Report.md`.

## Gate 10 craft telemetry hub contract

Gate 10 adds the non-equippable `V3CraftTelemetryHub` required by the runtime
kernel design. Every assembled craft receives the hub, even when the reference
controller pipeline is disabled.

The hub consolidates build identity, mass, center of mass, Rigidbody motion,
systems and propulsion power, selected power mode, per-priority allocation,
thermal protection aggregates, and every installed part's live output and
power state. Thruster force, gimbal motion/torque, and spring
motion/load are carried as part-specific telemetry without changing their
authoritative runtime owners.

Reusable live lists support allocation-free fixed-step refresh after capacity
is established. Explicit captures return frozen `V3CraftTelemetrySnapshot`
instances with readonly part values and readonly collections. Connector and
endpoint records retain stable part identity when they share a parent socket.

The hub exposes no command, force, enable, power-grant, or protection mutation
API. It is an observation boundary for future cockpit screens, diagnostics,
recording, and runtime builder tooling, not a second simulation path.

Gate 10 evidence and the direct-build parity regression are recorded in
`Docs/Hovercraft_V3_Gate10_Craft_Telemetry_Hub_Report.md`.

## Gate 11 runtime registry contract

Gate 11 adds the remaining identity registries from the GDD runtime kernel.
`V3SocketRegistry` owns stable socket, paired-socket, and configuration-group
lookup. `V3PartRegistry` owns installed-part lookup by socket and stable
definition identity, with separate connector and typed endpoint resolution.

The part registry preserves multiplicity: installing the same reusable
definition four times creates four indexed runtime instances. It also
preserves connector topology: a connector and child endpoint remain two
records at their shared parent socket.

`V3CapabilityRegistry` aggregates the existing provided and required
capability flags, indexes providers for each individual flag, and reports
missing global requirements. `CraftBuildValidator` now rejects an asset with a
`CAPABILITY_MISSING` error before assembly, and the runtime registry repeats
that check against instantiated parts.

Gate 10 telemetry snapshots now expose socket count plus provided, required,
and missing capabilities. Registry components do not apply forces or issue
actuator commands.

Gate 11 evidence and the direct-build parity regression are recorded in
`Docs/Hovercraft_V3_Gate11_Runtime_Registries_Report.md`.

## Gate 12 mass and runtime-validation kernel contract

Gate 12 extracts the existing mass equation from `V3CraftAssembler` into
`V3CraftMassCalculator`. The service owns chassis mass, one contribution per
installed connector or endpoint, weighted center-of-mass calculation, visible
parity calibration, and application to the single root Rigidbody.

Its immutable result distinguishes chassis and installed mass and exposes a
readonly per-part ledger keyed by socket and stable part ID. The reference,
gimbal, and spring builds calculate 11,000 kg, 11,120 kg, and 11,035 kg
respectively.

`V3RuntimeBuildValidator` independently checks the instantiated result against
the saved build after Gate 11 registries are populated. It verifies kernel
presence, the one-Rigidbody rule, socket and chain counts, exact connector and
endpoint selections, capabilities, mass, and center of mass. An invalid
runtime root is rejected before controllers are installed.

Gate 10 telemetry snapshots now expose chassis mass, installed-parts mass,
parity calibration, runtime validation state, and issue count. Neither service
submits actuator commands or applies physical forces.

Gate 12 evidence and the direct-build parity regression are recorded in
`Docs/Hovercraft_V3_Gate12_Mass_Runtime_Validation_Report.md`.
