# Hovercraft V3 Controlled Test Result

- Test: `test.v3.ballistic.gravity_only.01` v2
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `0fad09878206`
- Runner overhead: 0.17 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `a2.aero.zero`: expected 0..0.001; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a2.fin.zero`: expected 0..0.001; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a2.no.contact`: expected 0..0; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `a2.gravity.response`: expected -9.80665 +/- 0.25; measured mean=-9.80664; evidence `samples_compact.bin`.
- PASS `a2.residual.p95`: expected p95 <= 100; measured p95=2.09375; evidence `force_summary.csv`.
- PASS `a2.no.false.landing`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `a2.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.

## Setup evidence/warnings

- Transient aerodynamic override is being applied to selected non-TestOnly craft build.hovercraft.baseline.01; the source asset is not modified.
- Controlled runtime override disabled chassis aerodynamics.
- Controlled runtime override disabled 4 aerodynamic fin device(s).
