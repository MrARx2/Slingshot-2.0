# Hovercraft V3 Mainframe Systems Integration — Implementation Report

> Historical completion report. The canonical 2026-08-01 handoff supersedes its force-site count, reset, generated-authoring, fallback, and test-status claims.

**Completed:** 2026-07-29  
**Unity:** 6000.5.0f1  
**Build:** `build.apex.v3.systems_integration.balanced.01`  
**Result:** Gates 0–12 complete; final audits pass

## Outcome

The complete systems craft is generated under:

`Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/`

Playable scene:

`Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity`

Assembler prefab:

`Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/Prefabs/ApexV3_SystemsIntegration_Assembler.prefab`

Regenerate it with:

`Tools > Hovercraft V3 > Generate Systems Integration Prototype`

The permanent V2-reference craft remains a separate regression benchmark.
Its mass, COM, inertia, sockets, tuning, and parity profile were not changed.

## Gate implementation

### Gates 1–3

- Integrated `V3CraftMainframe` with all required boot states.
- Focused observation, intent, chassis-state, topology, health, scheduling,
  routing, and power services.
- Typed timestamped `V3ObservationBus` and high-level `V3IntentBus`.
- Complete standard system-request contract.
- Six integrated directional sensors.
- Front/Bottom request 120 Hz; Rear/Left/Right/Top request 60 Hz.
- FixedUpdate-bounded sampling with rates, age, confidence, power, and
  bottleneck telemetry.

### Gates 4–6

- Balanced Systems Cockpit with seven computer roles, four display
  placeholders, future HUD, mass anchor, and power/data throughput metadata.
- Seven physical computers: Pilot Interface, Drive, Hover, Stabilizer,
  Traction, Aerodynamics, Telemetry.
- Separate software definitions and deterministic virtual scheduler.
- Requested/minimum rates, compute cost, tick power, capacity and power
  bottlenecks, and safe 240 Hz global virtual-rate cap.
- Capability-oriented device contracts.
- Stock firmware for thrusters, gimbal, fin actuator, cooling, and sensors.
- Metered firmware rates, timeout, safe state, power, hardware, and thermal
  enforcement.
- Existing proven controllers are wrapped as scheduled software authority.

### Gates 7–9

- `V3ControlRouter` resolves sibling requests before the existing physical
  `V3ActuatorCommandRouter`.
- One Balanced profile; every normal domain weight is 1.0.
- Propulsion, Braking, Steering, Strafe, Ride Height, Attitude Stability,
  Traction, Aerodynamics, Recovery, and Manual Pilot domains.
- Four hover thrusters through four Balanced passive spring connectors.
- Four `Aero Socket -> Rotary Actuator -> Fin` chains.
- Real actuator angles and real aerodynamic forces at each fin's center of
  pressure.

### Gates 10–12

- Finite active cooling consumes systems power and changes authoritative
  per-part thermal state.
- Starvation preserves Mainframe, Pilot, Hover, Stabilizer, sensors and
  firmware, Drive, Traction, Aero, active devices, Cooling, then Telemetry.
- Complete craft includes rear gimballed main, front brake/reverse, four
  spring hover thrusters, four roof thrusters, four lateral thrusters,
  four active fins, Balanced EnergyCore, cooling, seven computers, six
  sensors, and integrated Mainframe.
- F4 cycles Mainframe, scheduler, sensor, Router, device, suspension,
  aero, and cooling debug pages.
- Dedicated playable free-drive scene.

## Physical authority

Normal motion authority remains physical: powered thruster runtimes, passive fin runtimes, and airflow-based chassis aerodynamics apply force/torque to the root Rigidbody. World gravity remains a separate world/Rigidbody authority:

1. `RuntimeThrusterInstance`
2. `RuntimeAerodynamicFinInstance`

Both use `Rigidbody.AddForceAtPosition(..., ForceMode.Force)`.

No Mainframe, software, Router, sensor, firmware, spring, cooling, or
telemetry service sets chassis pose/velocity or applies hidden torque.
Direct Rigidbody state writes remain limited to recovery and test reset.

The systems craft has one root Rigidbody and one chassis collider. Computers,
sensors, connectors, fins, cooling hardware, and thrusters have no additional
Rigidbody or collider.

## Audit record

Three-gate audits were performed after gates 1–3, 4–6, 7–9, and 10–12.

Final full suite — `TestResults-V3-Mainframe-Final.xml`:

- 92 discovered
- 91 passed
- 0 failed
- 1 intentionally skipped opt-in parity capture
- no C# warning/error, null/missing reference, or GUID mismatch

Focused integration/endurance —
`TestResults-V3-SystemsIntegration-Endurance.xml`:

- 7 passed
- 0 failed
- 30,000 fixed ticks / 10 virtual minutes

Enforced parity — `TestResults-V3-Mainframe-Parity.xml`:

- 1 passed
- 0 failed
- 7,801 samples per craft
- every parity tolerance assertion passed

Asset/source audit:

- 215 V3 assets/directories and 215 metadata files
- 0 missing metadata
- 0 orphan metadata
- 0 duplicate GUID entries
- 0 missing scripts
- 0 TODO/FIXME/HACK/unimplemented markers
- only the two documented force-authority sites

## Audit fixes

1. Gate 0 found stale legacy generated assets carrying four springs and eight
   failing tests. The canonical legacy generator restored the exact reference,
   gimbal, and single-spring builds before new work began.
2. Unity authored some new ScriptableObjects with missing script references
   when several asset classes shared one source file. Every asset definition
   was split into a matching file and regenerated.
3. The fin prefab had the same multi-MonoBehaviour source issue. Its runtime
   was split into `RuntimeAerodynamicFinInstance.cs` and regenerated.
4. Software virtual rates were initially capped to the 50 Hz physics rate,
   which made 60 Hz minimum tasks inoperable. Virtual representation is now
   safely independent while real evaluation remains FixedUpdate-bounded.
5. Firmware initially reported power without joining the shared systems
   budget. Mainframe allocation now meters firmware and enforces safe state.

## Deliberately deferred

Phase-Vector technology, Flight Reserve, track charging, damage, detachment,
part hitboxes, multiple Router profiles, player software, final garage and
cockpit UI, final art/audio, and multiplayer remain out of scope rather than
unfinished integration work.
