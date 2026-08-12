# Hovercraft V3 Controlled Test Result

- Test: `test.v3.descending_capture.primary_systems.01` v2
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `1963a5dcaaa6`
- Runner overhead: 0.177 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `d.capture.bounded`: expected 0..1; measured min=0, max=1; evidence `samples_compact.bin`.
- PASS `d.residual.p95`: expected p95 <= 500; measured p95=1.07392; evidence `force_summary.csv`.
- PASS `d.capture.established`: expected hover.captureAuthority reaches >= 0.9; measured maximum=1; evidence `samples_compact.bin`.
- PASS `d.surface.reacquired`: expected 1 events; measured 1 events; evidence `events.json`.
- PASS `d.no.probe.loss`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `d.no.reset`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `d.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
