# Hovercraft V3 Controlled Test Result

- Test: `test.v3.fin_sweep.primary_systems.01` v2
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `7a12e44727fd`
- Runner overhead: 0.164 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `b.fin.force.present`: expected forces.fin reaches >= 100; measured maximum=17898.48; evidence `samples_compact.bin`.
- PASS `b.fin.force.bounded`: expected 0..250000; measured min=761.6255, max=17898.48; evidence `samples_compact.bin`.
- PASS `b.no.contact`: expected 0..0; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `b.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
- PASS `b.residual.p95`: expected p95 <= 100; measured p95=4.22425; evidence `force_summary.csv`.
