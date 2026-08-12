# Hovercraft V3 Controlled Test Result

- Test: `test.v3.surface_departure.race_speed.01` v2
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `1297e655a4c5`
- Runner overhead: 0.156 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `c.transition`: expected NearSurfaceHover -> CaptureLimited -> FreeFlight; measured NoSurface -> NearSurfaceHover -> CaptureLimited -> NearSurfaceHover -> ProbeLost -> NoSurface -> FreeFlight; evidence `samples_compact.bin`.
- PASS `c.bottom.clears`: expected hover.bottomRequest <= 0.001 by 1.61 s (within 0.25 s after 1.36 s); measured reached at 1.37 s; evidence `samples_compact.bin`.
- PASS `c.roof.clears`: expected hover.roofRequest <= 0.001 by 1.61 s (within 0.25 s after 1.36 s); measured reached at 1.37 s; evidence `samples_compact.bin`.
- PASS `c.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
- PASS `c.probe.loss`: expected 1 events; measured 1 events; evidence `events.json`.
- PASS `c.envelope.departure`: expected 1 events; measured 1 events; evidence `events.json`.
- PASS `c.no.reacquire.after.departure`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `c.no.reset`: expected 0 events; measured 0 events; evidence `events.json`.
