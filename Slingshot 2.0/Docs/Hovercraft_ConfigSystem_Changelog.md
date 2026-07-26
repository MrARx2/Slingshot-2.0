# Hovercraft V2 Config System — Changelog & Usage

2026-07-23. Physics audit + config system delivery.

## Files created

| File | Purpose |
|---|---|
| `Assets/Scripts/Hovercraft_Setup/Config/CraftConfigJson.cs` | Minimal JSON parser/writer (culture-invariant, pretty, prunes Unity instanceID stubs, tree diff) |
| `Assets/Scripts/Hovercraft_Setup/Config/CraftConfigManager.cs` | Export / import / apply / validate / reset / change-log; the component to put on the craft root |
| `Assets/Scripts/Hovercraft_Setup/Config/CraftConfigDiagnostics.cs` | Computed diagnostics (weight, hover reserve, TWRs, accel/brake estimates, scale, warnings) |
| `Assets/Scripts/Hovercraft_Setup/Editor/CraftConfigManagerEditor.cs` | Custom inspector: live summary + Export/Import/Validate/Diagnostics buttons |
| `Docs/Hovercraft_PhysicsAuditReport.md` | Deliverable 1 — full physics inventory + discrepancies |
| `Docs/Hovercraft_MissingParameters.md` | Deliverable 6 — absent parameters, V2/V3 priority |
| `Docs/Hovercraft_KinematicBehaviorReport.md` | Deliverable 7 — kinematic manipulation inventory (result: none craft-side) |
| `Docs/Hovercraft_ConfigSystem_Changelog.md` | This file |

## Existing behavior

**No physics values were changed by this task.** The config system is passive: it reads the live components on export and writes only the fields present in a file on import. No mass/force/gravity/COM/collider/aero retuning was performed. The audit's findings (e.g. scene `extraGravity 75`, saturating burst throttles, dead force multiplier) are *reported*, not changed.

## Setup (one-time)

1. Open the `HovercraftRootV2` prefab, add **Craft Config Manager** to the root, save.
2. Select the craft → inspector shows the Physical Summary → click **Export…** to write the first config (defaults to `<project>/CraftConfigs/hovercraft_v2.json`).
3. Optional: **Write Diagnostics Report** produces `hovercraft_v2_diagnostics.md` next to it.

## How the system works

- **Format:** one human-readable JSON per craft. Sections: `metadata` (version 1, identity, units convention, gravity/timestep at export), `rigidbody`, `thrusterBus`, `thrusters[]` (per node: role, position [record-only], settings, bus entry), `components{}` (every physics component's full serialized field set, captured generically via JsonUtility — the file automatically stays in sync with script changes), `worldAtmosphere` (reference snapshot; applied only if the manager's toggle is on), `dimensions` + `diagnostics` (computed, read-only).
- **Source of truth:** component inspector values. Export reads them; import overwrites them (with editor Undo support). Prefab values remain the baseline; a config apply in edit mode creates normal prefab-instance overrides you can revert.
- **Import safety:** validation runs first (version check, NaN/Infinity scan, mass/damping sanity, hover-capacity-vs-weight cross-check, craft-ID mismatch warning) — errors abort, warnings log. Only fields present in the file are applied; unknown fields/components are logged as "not applied", never silently dropped into other values. Every changed value is logged as `path: old → new`. After apply, derived wiring recomputes (gravity throttle, telemetry contact distance, bus categorization).
- **Positions are never applied.** Thruster/hover node positions are exported for the record; a mismatch logs a note. Moving hardware is a prefab edit, not a config apply.
- **Reset:** play mode captures a startup snapshot on Awake → "Reset To Startup Values". In edit mode, use prefab Revert.
- **Versioning:** `metadata.configVersion` (currently 1). Older files import with a best-effort migration hook; newer files are rejected.

## Known limitations (documented by design)

- `AnimationCurve` fields would not serialize via JsonUtility — the audit confirmed **no component has any**, so nothing is lost today. If curves are added later, they need explicit handling.
- Unknown future fields in a config are ignored on apply and are not preserved on the next export (no Newtonsoft dependency; JsonUtility round-trip).
- Cosmetic components (CraftHUD, CraftDebugHUD, CraftFeedbackSystem) and the camera are intentionally outside the config.
- Physics materials: none exist in the project (engine defaults everywhere) — flagged in the Missing Parameters report.
