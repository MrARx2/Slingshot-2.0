# Hovercraft V3 Controlled Test Result

- Test: `test.v3.full_track.primary_systems.01` v2
- Result: **FAIL**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `60356f516de0`
- Runner overhead: 7469.153 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `h.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
- PASS `h.capture.bounded`: expected 0..1; measured min=0, max=1; evidence `samples_compact.bin`.
- PASS `h.residual.p95`: expected p95 <= 1000; measured p95=8.4707; evidence `force_summary.csv`.
- PASS `h.track.minimum_progress`: expected track.distance reaches >= 6000; measured maximum=9326.842; evidence `samples_compact.bin`.
- PASS `h.track.inside_bounds`: expected 1..1; measured min=1, max=1; evidence `samples_compact.bin`.
- PASS `h.track.lateral_containment`: expected -55..55; measured min=-30.44743, max=38.36437; evidence `samples_compact.bin`.
- FAIL `h.no.reset`: expected 0 events; measured 1 events; evidence `events.json`.
- PASS `h.no.crash`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `h.no.out_of_bounds`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `h.no.reverse`: expected 0 events; measured 0 events; evidence `events.json`.
- FAIL `h.no.rollover`: expected 0 events; measured 3 events; evidence `events.json`.
- FAIL `h.no.spin`: expected 0 events; measured 2 events; evidence `events.json`.

## Setup evidence/warnings

- Track-relative start resolved at 0 m.
- Controlled track follower owns steering, pitch, strafe, and speed commands.
