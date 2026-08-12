# Hovercraft V3 Controlled Test Result

- Test: `test.v3.reset_neutrality.primary_systems.01` v1
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `47c32bea57c7`
- Runner overhead: 1.451 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `e.reset.event`: expected 1 events; measured 1 events; evidence `events.json`.
- PASS `e.bottom.neutral`: expected forces.bottom <= 1 by 1.1 s (within 0.1 s after 1 s); measured reached at 1.02 s; evidence `samples_compact.bin`.
- PASS `e.roof.neutral`: expected forces.roof <= 1 by 1.1 s (within 0.1 s after 1 s); measured reached at 1.02 s; evidence `samples_compact.bin`.
- PASS `e.propulsion.neutral`: expected forces.propulsion <= 1 by 1.1 s (within 0.1 s after 1 s); measured reached at 1.02 s; evidence `samples_compact.bin`.
- PASS `e.discontinuity.invalid`: expected identity.dynamicsValid <= 0 by 1.1 s (within 0.1 s after 1 s); measured reached at 1.02 s; evidence `samples_compact.bin`.
- PASS `e.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
