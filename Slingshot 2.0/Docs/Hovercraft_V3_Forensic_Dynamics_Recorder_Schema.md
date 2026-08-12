# Hovercraft V3 Forensic Dynamics Recorder — Schema

Schema version: `6.0` (`V3DiagnosticSchema.Version == 6`)

Schema 6 adds explicit first/discontinuity dynamics validity, physical surface/contact event vocabulary, contact-free residual metrics, surface capture evidence, per-fin AoA/coefficient/stall evidence, build ownership/GUID/preset/control-path metadata, raw device CSV, and inertia/torque/expected-angular-acceleration channels. Legacy enum members remain readable but are not the current detector semantics.

## Identity and timing

Every retained sample has:

- session ID;
- sample index;
- physical tick index;
- simulation and unscaled-real timestamps;
- session elapsed time;
- fixed delta;
- capture phase (`PostPhysics`);
- scene, craft runtime/build, and track identity;
- track distance and section identity.
- driving-attempt index;
- external-state discontinuity flag and reset reason;
- separate craft-local and world-owned physics tick identities where an
  environmental sample is present.

Full/Standard normally have matching consecutive physical ticks and sample
indices. Reduced-rate profiles preserve physical tick gaps while sample indices
remain consecutive.

## Sample perspectives

Each `V3DiagnosticSample` contains:

1. **World truth** — pose, velocities, derived accelerations, gravity frame,
   kinetic energy, contacts, collision impulse vector/point, and gravity force.
2. **Track truth** — centerline mapping, stable section, lap/progress, tangent,
   normal/right basis, offsets, surface ray truth, collider identity, curvature,
   torsion, bank, slope, bounds, and expected-airborne metadata.
3. **Sensors/observations** — physical mount and direction, requested/granted/
   measured rate, power, health, payload, age/confidence, independent ray truth,
   error, and explicit unavailability.
4. **Craft belief** — Mainframe, input authority/route, hover belief/probes,
   target ride height, believed surface normal, stabilization request, traction,
   and airflow belief.
5. **Pilot/system execution** — raw and processed pilot command, intent timing,
   scheduler task execution/rates/skips/faults, system requests, and power.
6. **Router decision** — active profile, all domain weights, incoming/rejected
   requests, conflicts, hard constraints, requested/allocated/residual wrench,
   granted authority, clipping, and evidence reason.
7. **Device execution** — identity, socket/connector, channel mask, requested
   direction/force/torque/orientation, allocator command/cap/allocation,
   firmware rates/safe state, power, actual force/application point/torque,
   gimbal/spring/rotary/fin state, thermal state, limits, and fault.
8. **Force/torque ledger** — gravity, propulsion, braking, hover, roof, lateral,
   aerodynamic lift/drag/side, contact estimate, expected/observed/residual force
   and torque, world/craft/track/gravity frames, and force-to-weight ratios.
9. **Independent classification/events** — world contact state is computed from
   collision and track truth independently from hover belief.

`sourceChannelMask` bits are:

```text
bit 0 = Drive
bit 1 = BaseHover
bit 2 = Stabilization
bit 3 = Vectoring
bit 4 = Manual
```

## Static snapshots

- Craft snapshot: build/chassis identity, layout, mass, COM/inertia, all parts,
  sensors/computers/tasks, Mainframe, and router profile.
- World snapshot: scene, gravity, fixed/maximum timestep, solver/contact
  settings, simulation mode, and track identity.
- Track snapshot: transformed full-track section/centerline data, configurable
  sampling interval, stable section IDs, bounds, collider audit, and quality.

## Events

Events store severity, canonical time/tick/sample, track section/distance,
craft position/speed, independent world state, craft belief, possible domains,
manual flag, driving-attempt index, reset reason, notes, and clamped pre/post
sample references. Context membership
is materialized in `event_context.ndjson` as canonical sample-index references;
the authoritative sample payload is not duplicated for every overlapping event.

## Binary format

`samples_compact.bin` begins with:

1. magic `V3DR` (`0x56334452`);
2. schema version;
3. byte-order flag;
4. exact ordered compact-field count;
5. for each field: ID, unit, data type, coordinate frame, component count;
6. sample count;
7. packed samples in the declared field order.

Schema-6 controlled captures include `identity.controlled_test_phase` as an
explicit compact string field. `samples_core.csv` exposes the same value in
the `controlled_test_phase` column, so phase boundaries remain visible even
when raw NDJSON is disabled.

The CRC32 sidecar covers the complete binary payload. The binary table is
deliberately separate from `channel_schema.json`, which describes the broader
typed recorder schema.

## Data quality

The recorder uses fixed capacities. Overflow increments dropped counts, emits
a warning/event when possible, and stops recording without blocking physics or
changing craft behavior. Missing comparison truth and capacity truncation are
explicit; unknown solver/contact effects remain in force/torque residuals.

Craft reset boundaries are explicit discontinuities. Acceleration, force
residual, state-transition, and event-continuity calculations do not interpret
the reset teleport as vehicle dynamics. Per-attempt summaries are exported in
`attempt_summary.csv` and in the JSON/Markdown reports.
