# Hovercraft V3 Forensic Dynamics Recorder — Usage

## Purpose

> Updated behavior note (2026-08-01): schema 6 uses Forensic Compact as the default for newly added recorders, with NDJSON opt-in. Existing scene components retain their serialized profile. Treat `PhysicalTakeoff`, `ContactLanding`, `SurfaceEnvelopeDeparture`, `ProbeLoss`, `ContactLoss`, `FreeFlightEntered`, and `OutOfBounds` as distinct events.

The recorder is a scene-owned, read-only observer for Hovercraft V3. It records
world and track truth, craft sensing and belief, pilot/system decisions,
router/allocation state, physical device execution, force reconciliation, and
resulting Rigidbody motion on one canonical physics-tick identity.

It does not submit commands, tune parameters, apply forces, change power, or
change the track. The only component it may add at runtime is a passive
collision callback probe used to read collision contacts and impulses.

## Location in the scenes

The development driving scenes contain exactly one root object named:

```text
[Hovercraft V3 Diagnostics]
└── V3DiagnosticRecorder
```

Scenes:

- `Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity`
- `Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity`
- `Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity`

If a scene does not contain it, use:

```text
Tools > Hovercraft V3 > Diagnostics > Install Recorder In Current Scene
```

The recorder binds to the assembled `V3CraftRuntime`, scene-owned
`V3PilotInputAdapter`, and `TrackGenerator`. Explicit inspector references are
optional; missing references are resolved when recording starts.

## Recording

In Play Mode:

- `F7`: start or stop recording;
- `F8`: add a manual evidence marker;
- `F9`: stop if necessary and export;
- inspector buttons: Start Recording, Stop Recording, Export Now;
- `Tools > Hovercraft V3 > Diagnostics > Recorder Window`: shared status and
  controls.

Stopping and runtime export are non-blocking. The recorder closes capture first,
then writes the package incrementally across frames. The runtime overlay and
Recorder Window show the active stage and percentage; recording cannot restart
until that session's export finishes.

The runtime overlay shows recording state, canonical session time, sample and
dropped counts, track distance/section, independent world state, craft hover
belief, events, and output root.

## Profiles

- **Full Forensic** records every enabled source every physics tick. It is intended for short benchmark
  runs.
- **Forensic Compact** is the new-recorder default. It retains compact binary and CSV evidence while NDJSON is optional.
- **Standard Dynamics** records the complete major world, system, router,
  device, and force chain every physics tick. Optional export toggles are
  honored. It is intended for laps.
- **Endurance** records the complete chain every fifth physics tick. The
  manifest records the stride and nominal sample rate. It is intended for long
  runs and event/summary analysis.
- **Custom** exposes source-group toggles and a physics-tick stride. Disabled
  sources are listed by omission from `enabledSources` in the manifest.

The canonical `physicsTick` continues through skipped Endurance/Custom ticks;
`sampleIndex` advances only when a sample is retained.

## Output

Default output root:

```text
TestDriveReports/HovercraftV3
```

Each export creates a unique timestamped session directory containing:

- `session_manifest.json`;
- `channel_schema.json`;
- `craft_snapshot.json`;
- `world_snapshot.json`;
- `track_report.json`;
- `samples.ndjson` when enabled;
- `samples_compact.bin` and `.crc32` when enabled;
- `events.json`, `events.csv`, and `event_context.ndjson`;
- core, track, section, device, system, sensor, force, and location-cluster
  CSV files;
- `attempt_summary.csv`, which splits statistics at every explicit craft
  reset;
- `samples_core.csv`, including schema-6 reset validity and controlled-test
  phase columns;
- chart-ready CSV files under `charts/`;
- `report.json` and `report.md` when enabled;
- `logs/recorder.log`.

Event context defaults to two seconds before and three seconds after each
event. Export clamps context references to retained samples and writes a compact
index to `event_context.ndjson`: event ID, event sample, relative offset, and
the referenced canonical sample/tick/time. The full synchronized payload exists
once in `samples.ndjson` (and its compact counterparts), avoiding exponential
duplication when event windows overlap.

## Reading a report

Start with `report.md`, then use `report.json`/CSV for comparison and NDJSON for
individual samples. Automated findings identify evidence windows and possible
contributing domains; they do not assert a root cause.

Backspace resets are recorded as explicit `ManualInput` reset events. Kill-plane
recovery is recorded separately as `KillPlane`. The reset sample is marked as
an external-state discontinuity, so teleport velocity/acceleration and force
residuals are excluded from physical conclusions while the complete recording
remains available. The Markdown report lists each driving attempt separately.

Use `Tools > Hovercraft V3 > Diagnostics > Report Viewer` to inspect Markdown
or JSON in Unity. Use `Tools > Hovercraft V3 > Diagnostics > Validate Schema`
after changing channel definitions.

## Safe extension rules

1. A source implements `IV3DiagnosticSource` and receives the existing sample
   by reference after physics.
2. Capture methods may read Unity objects only on the main thread.
3. Preallocate all per-session buffers in `OnSessionStarted`; do not allocate,
   use reflection, or use LINQ in `Capture`.
4. Never call gameplay command, force, power-allocation, sensor-publication, or
   track-mutation APIs from diagnostics.
5. Register stable, versioned channel IDs and document units/frames.
6. Label unavailable or inferred evidence honestly.
7. Keep export-time allocation and file I/O outside the physics capture loop.
8. Add focused tests and rerun the full V3 and serialized-scene suites.
