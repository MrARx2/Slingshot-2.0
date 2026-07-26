# Hovercraft Test Drive Recorder — Usage

Passive observer that records real drives for tuning analysis. It never applies forces, modifies inputs, or changes any craft value — it only reads the systems' own read-only debug snapshots after each physics tick.

## Setup (one-time)

1. Open the HovercraftRootV2 prefab and add **Test Drive Recorder** (and optionally **Test Drive Recorder HUD**) to the root.
2. Pick a mode:
   - **Full Diagnostic** — 100 Hz everything; focused 30 s – 5 min tests.
   - **Standard Test Drive** — 50 Hz core/nodes/thrusters; normal lap driving (default).
   - **Lightweight Endurance** — 20 Hz core, per-node/thruster streams off; long sessions.
3. Optionally set a session name and tester notes. Click **Validate Signals** to see what's available (warnings don't block; missing signals are reported as Unknown in the bundle, never fabricated).

## Recording workflow

1. Enter play mode, drive to where you want to start.
2. **F9** (or Inspector → Start Recording) starts. The HUD shows a red REC state.
3. Drive normally. Drop markers when something feels wrong:
   - **F8** — quick "Unexpected behavior" marker
   - **Shift+F8** — category menu (1-9 selects: steering numb, bottomed out, too bouncy, feels good, …)
4. **F9** again = stop + export. The full bundle lands in `TestDriveReports/<date>_<craft>_<session>/` at the project root, plus a `.zip` and a single-file `.hoverdrive.json`.

## What to upload for analysis

The single `.hoverdrive.json` is usually enough: it contains metadata, the summary, all events with evidence flags, section and speed-band aggregates, markers, decimated frames, and the active config hash. Attach the full ZIP when per-tick hover-node or thruster detail is needed.

## Reading the report

`session_summary.md` leads with the executive summary and the **suspected constraint ranking** — evidence statements like "emergency hover capacity was reached in 71% of bottoming events", never tuning commands. Events carry evidence FLAGS (multiple may be true); the speed-band tables show where steering response drops and clearance collapses.

## Comparing sessions

Compare only sessions with matching craft config hash, mode, and timestep (all in `session_metadata.json`). Re-run the same route after a config change and diff the summary/speed-band tables. (Automated comparison tooling is a planned follow-up — see schema doc.)

## Performance

Targets: < 1 ms/step Full Diagnostic, < 0.25 ms Standard. The actual measured overhead of every session is written into its own metadata and summary (`recorderOverheadMsPerSample`), along with dropped-sample counts. Files are written only after recording stops.

## Known limitations (v1.0)

- Endurance mode has no separate high-frequency pre-event ring buffer; event windows reference mode-rate frames (Full/Standard rates are high enough that this only matters for endurance runs).
- Lap/checkpoint identity, track seed/hash and quarter IDs are recorded as Unknown (no lap system exposes them yet); section index, arc length and centerline offset come from TrackSectionSensor when present.
- Session comparison and the timeline viewer are not implemented yet.
- Response-latency (input-to-response delay) is derivable from the CSVs but not auto-computed.
