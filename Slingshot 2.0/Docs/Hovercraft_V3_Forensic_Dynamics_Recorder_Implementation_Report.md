# Hovercraft V3 Forensic Dynamics Recorder and Track Analyzer — Implementation Report

> Historical implementation report. Schema/event/storage behavior was superseded by the 2026-08-01 architecture repair. See `Hovercraft_V3_Architecture_Aero_SurfaceCapture_Authoring_Repair_Report.md` and the canonical handoff before relying on verification claims below.

Completed: 2026-07-31  
Unity: 6000.5.0f1  
Current schema after repair: 6.0

## Router/allocation/device recording

The recorder captures the complete evidence chain without becoming part of
control authority:

- raw and processed pilot input, route, authority, and timestamps;
- actual scheduler execution, requested/capacity/power/granted/measured rates,
  skips, duration, fault, bottleneck, and publication state;
- every current `V3SystemRequest`, including wrench/state/angle, frame,
  eligibility, confidence, authority, maximum contribution, priority,
  timestamp, validity window, validity result, and reason;
- active router profile and all ten domain weights;
- request/rejection/conflict/hard-constraint counts;
- the combined hover-alignment, traction, and yaw-damping requested, allocated,
  and residual wrench;
- every installed physical part with socket and connector identity;
- every thruster channel separately (Drive, BaseHover, Stabilization, Vectoring,
  Manual), combined allocator command, direction, requested force/torque,
  authority cap, selection/allocation result, and limiting evidence;
- power request/grant, firmware identity/requested/granted/minimum rate and safe
  state, thermal limit, actual output, force, application point, and torque;
- actual gimbal, spring, rotary-actuator, fin, thermal, limit, and fault state.

Read-only snapshot APIs were added to the actuator router, input adapter, hover
controller, and thruster firmware runtime. They expose existing state and do not
change resolution, power, or physics behavior.

## Force and torque ledger

The ledger sums physical device forces by gravity, propulsion, braking, hover,
roof, lateral, and aerodynamic categories. Aerodynamics are also decomposed
into gravity-frame lift, velocity-axis drag, and remaining side force. Collision
impulse divided by fixed delta is explicitly labeled an estimate.

Expected force is compared with mass times derived linear acceleration.
Expected torque (thruster, aerodynamic, and estimated contact moment) is
compared with the Rigidbody inertia-based angular effect. Residuals remain
visible rather than being falsely assigned.

Expected/observed/residual force and expected/residual torque are recorded in
world, craft-local, track, and gravity frames. Hover, aero-lift, upward-thruster,
total-upward, and net-vertical force-to-weight ratios are included.

No diagnostic code calls `AddForce`, `AddForceAtPosition`, `AddTorque`, or any
velocity/pose write. The only normal V3 physical force sites remain the
thruster and aerodynamic-fin runtimes.

## Event detection

Motion, track, sensor, Observation Bus, scheduler, Mainframe, router, power,
thermal, gimbal, spring, fin, device, and data-quality events are detected with
cooldowns. Every event contains canonical time/tick/sample, track location,
world classification, craft belief, speed/position, evidence text, possible
contributing domains, and manual/automatic identity.

Pre/post context is configurable and defaults to two seconds before and three
seconds after. References are clamped on completion and synchronized sample
references are exported to `event_context.ndjson`; payloads remain authoritative
in the sample streams and are not duplicated per event. Event-buffer overflow is counted
and warned; it is never silent.

## Reports and exports

Each session creates a unique package with:

- manifest, typed channel schema, craft/world/track snapshots;
- trimmed NDJSON without unused preallocated records;
- compact binary with magic/version/endian flag, an exact ordered field table,
  component counts, sample count, and CRC32 sidecar;
- events plus a materialized context index;
- core, track, section, device, system, sensor, force, event, and repeated
  10-m-location-cluster CSV summaries;
- real speed, ride-height, vertical-force, power, task-rate, and event-timeline
  chart data;
- machine-readable report JSON and human-first Markdown.

Reports contain lap/section/device/system/sensor/force/event summaries,
world-versus-belief disagreement, repeated locations, data-quality warnings,
and possible investigation areas. They explicitly identify evidence windows
and never claim a cause.

## Editor/runtime workflow

Both development scenes contain exactly one scene-owned
`[Hovercraft V3 Diagnostics]` root. The installer can add/repair it in other
scenes. The inspector, Recorder Window, runtime overlay, Report Viewer, and
schema validator are available under `Tools > Hovercraft V3 > Diagnostics`.

Hotkeys are F7 start/stop, F8 marker, and F9 export. Full/Standard capture every
physics tick, Endurance captures every fifth tick, and Custom selects source
groups plus stride. The manifest records stride, nominal rate, and enabled
sources. New recordings default to Forensic Compact with compact binary/CSV; NDJSON is opt-in. Existing serialized recorder components retain their authored profile.

F7/F9 export is incremental rather than a synchronous main-thread transaction.
Unity JSON work stays on Unity's thread, but bounded record batches and CSV files
yield between frames. Both the runtime overlay and Recorder Window expose export
stage/progress, and concurrent recording/export is rejected to keep session data
immutable during serialization.

## Performance

- Capture runs post-physics on the main thread.
- Samples and nested sensor/task/observation/request/device arrays are
  preallocated and bounded.
- Sources cache runtime bindings at session start.
- Non-allocating physics queries and contact arrays are used.
- The track search uses last-segment locality plus coarse fallback.
- No capture-time LINQ, reflection, file I/O, or background Unity API access is
  present.
- Section IDs/types and device identity strings are cached outside capture.
- Export allocation and file I/O occur after recording.
- Backpressure increments explicit counters/warnings and stops the recorder;
  it never blocks or changes the craft.

## Tests

Final isolated-project validation on Unity 6000.5.0f1:

- fresh Unity import/script compilation: no C# errors or warnings;
- focused diagnostics: 16/16 passed;
- serialized Craft Lab and Handling Track: 2/2 passed;
- complete Hovercraft V3 suite: 127 discovered, 126 passed, 0 failed,
  1 intentional opt-in parity-capture skip;
- metadata audit: zero missing `.meta` files and zero duplicate V3 GUIDs;
- static authority audit: zero diagnostic force/torque/pose/velocity writes;
- physical authority audit: exactly the expected thruster and fin force sites;
- static hot-path audit: no LINQ or reflection in Diagnostics;
- both scenes: exactly one serialized recorder component and one diagnostics
  root, also verified through the serialized-scene tests.

Primary tests:

- `Assets/HovercraftV3/Tests/Editor/Diagnostics/V3DiagnosticFoundationTests.cs`
- `Assets/HovercraftV3/Tests/Editor/Diagnostics/V3DiagnosticTruthAndSensorTests.cs`
- `Assets/HovercraftV3/Tests/Editor/Diagnostics/V3DiagnosticLedgerEventReportTests.cs`
- `Assets/HovercraftV3/Tests/Editor/Integration/V3SerializedScenePlayModeTests.cs`

## Example report package

`KnownMismatchBenchmark_ExportsEvidencePackageWithoutClaimingCause` builds and
exports a complete controlled package for a known mismatch at 1,864.2 m in
section S06:

- world truth is unexpected-airborne;
- craft hover belief remains grounded;
- bottom-sensor distance differs from independent truth and is aged;
- upward hover force and aero lift evidence are present;
- repeated disagreement events share a track-location cluster.

The test validates report wording, section/location evidence, six materialized
event-context index records, binary/checksum, and required package files, then cleans
its temporary output. The serialized Handling Track test independently records
and exports a real assembled-craft scene package.

## Known limitations

- Collision forces are estimated from solver impulse over fixed delta; residual
  force/torque may include solver effects that Unity does not expose as forces.
- Aerodynamic lift/drag/side decomposition is diagnostic projection, not a
  second aerodynamic model.
- Full Forensic preallocates its bounded session in memory and is intended for
  short runs; Endurance is the long-run profile.
- Export is synchronous after capture rather than streamed on a writer thread.
  This cannot stall an active physics tick because recording is stopped first.
- Custom profiles may intentionally omit dependencies. The recorder publishes
  enabled sources and warns when a force ledger is requested without devices.
- Automated thresholds are review aids, not handling-tuning conclusions.

## Files added

- `Assets/HovercraftV3/Runtime/Diagnostics/` assembly, core, sources, track,
  events, export, UI, and metadata;
- `Assets/HovercraftV3/Editor/Diagnostics/` installer, inspector/window, report
  viewer, and schema validator;
- `Assets/HovercraftV3/Tests/Editor/Diagnostics/` focused contracts;
- this report plus the recorder usage and schema documents.

## Files changed

- core read-only diagnostic surfaces in `V3ActuatorCommandRouter`,
  `V3DeviceContracts`, `V3PilotInputAdapter`, and `V3HoverController`;
- editor/test assembly definitions and canonical scene generators;
- both serialized development scenes;
- serialized scene integration tests;
- `Docs/Hovercraft_V3_AI_Agent_Handoff.md`.

The existing migration worktree contained unrelated moves and generated-content
changes before this sprint; they were preserved and not reverted.

## Safe next steps

1. Record controlled Full Forensic runs for each craft build and compare report
   evidence before changing tuning.
2. Use Standard Dynamics for normal lap analysis and Endurance for long soak
   runs.
3. Add new channels through `IV3DiagnosticSource`, stable schema IDs, explicit
   units/frames, preallocated capture, and focused tests.
4. Keep all diagnostic conclusions evidentiary; perform tuning only in a
   separate, explicitly authorized change.
5. Rerun focused diagnostics, both serialized scenes, and the full V3 suite
   after any recorder, physics, routing, sensor, power, or track change.

## 2026-08-01 track-test repair addendum

Schema 4 adds explicit reset reason, driving-attempt identity, and external
state discontinuities. Reset teleports no longer contaminate acceleration,
force residuals, or continuity-based events. Reports and exports include
per-attempt summaries. Hover/contact classification reads the active serialized
hover envelope; loop orientation uses the local track frame; power-limited
classification ignores float-rounding-only deficits.

The dedicated Track Test scene now uses an Editor-baked persistent track cache
and a track-bounds-derived kill plane. Full details and final test results are
in `Docs/Hovercraft_V3_Track_Test_Infrastructure_Repair.md`.
