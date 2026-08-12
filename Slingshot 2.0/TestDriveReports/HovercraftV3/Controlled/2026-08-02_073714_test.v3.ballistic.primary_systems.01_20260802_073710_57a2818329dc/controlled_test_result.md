# Hovercraft V3 Controlled Test Result

- Test: `test.v3.ballistic.primary_systems.01` v1
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `57a2818329dc`
- Runner overhead: 0.172 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `a.capture.zero`: expected 0..0.001; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a.bottom.zero`: expected 0..1; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a.roof.zero`: expected 0..1; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a.no.contact`: expected 0..0; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a.gravity.response`: expected -9.80665 +/- 1.5; measured mean=-9.80553; evidence `samples_compact.bin`.
- PASS `a.freeflight`: expected FreeFlight; measured NoSurface -> FreeFlight; evidence `samples_compact.bin`.
- PASS `a.residual.p95`: expected p95 <= 250; measured p95=1.83594; evidence `force_summary.csv`.
- PASS `a.no.false.landing`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `a.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
