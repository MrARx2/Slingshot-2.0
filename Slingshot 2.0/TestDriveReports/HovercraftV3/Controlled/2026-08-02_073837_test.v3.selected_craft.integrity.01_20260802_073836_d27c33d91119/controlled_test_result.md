# Hovercraft V3 Controlled Test Result

- Test: `test.v3.selected_craft.integrity.01` v2
- Result: **PASS**
- Phase: Complete
- Build: `build.hovercraft.baseline.01` (`a2946245bc904c19a0827a4308c729be`, GeneratedReference)
- Control path: MainframeScheduled
- Session: `d27c33d91119`
- Runner overhead: 0.109 ms
- Dropped samples: 0
- Dropped events: 0

## Assertions

- PASS `g.build.identity`: expected build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; measured build.hovercraft.baseline.01 / a2946245bc904c19a0827a4308c729be / GeneratedReference; evidence `session_manifest.json`.
- PASS `g.no.contact`: expected 0..0; measured min=0, max=0; evidence `samples_compact.bin`.
- PASS `g.residual.p95`: expected p95 <= 100; measured p95=0.4375; evidence `force_summary.csv`.
- PASS `g.no.reset`: expected 0 events; measured 0 events; evidence `events.json`.
- PASS `g.no.mainframe.degraded`: expected 0 events; measured 0 events; evidence `events.json`.
