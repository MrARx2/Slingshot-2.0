# Hovercraft V3 Project Readability Sprint Report

> Historical UI/readability report. Generated builds are now explicitly generator-owned, authored variants live outside `Generated`, and current force authority includes thrusters, passive fins, airflow-based chassis aero, and world gravity. Its prior test results predate the 2026-08-01 architecture repair; see the canonical handoff.

## Completion summary

All production gates 0–12 are implemented. Runtime/editor/tests compile in
Unity 6000.5, canonical assets were regenerated, the complete V3 suite has zero
failures, focused integration and sprint contracts pass, and enforced V2/V3
parity passes.

## Gates completed

- Gate 0: baseline and repository audit
- Gate 1: input authority investigation
- Gate 2: input lifecycle correction
- Gate 3: runtime hierarchy refactor
- Gate 4: runtime folder migration
- Gate 5: editor and test folder migration
- Gate 6: generated asset migration
- Gate 7: shared scene consolidation
- Gate 8: craft test spawner
- Gate 9: Edit Mode preview
- Gate 10: systems console foundation
- Gate 11: ten live systems pages
- Gate 12: regeneration, cleanup, and final audit

## Gates incomplete

None.

## Input authority findings

Before this sprint, `V3CraftAssembler` added `V3PilotInputAdapter` directly to
the assembled physical craft root. `V3FreeDriveSession` then toggled the
adapter's `enabled` property whenever cursor capture changed. The disabled
component therefore represented cursor lifecycle control, not a second input
source. No duplicate local publisher was found. Input Actions and intent
publication were part of the same local adapter → controller pipeline path.

The complete craft could still be driven by direct scripted
`V3ControllerPipeline.Tick` calls without the adapter, but it could not receive
local keyboard/gamepad input without an active adapter. A craft observed after
cursor release could coast or continue neutral stabilization even though the
local adapter displayed as disabled.

## Input lifecycle changes

`V3PilotInputAdapter` is now scene/player-owned. It binds explicitly to the
current `V3ControllerPipeline` and Mainframe, remains enabled while authority is
suspended, and exposes:

- active source and target craft;
- `V3InputAuthorityState`;
- cursor/UI suspension;
- Input Actions enabled state;
- Intent Bus connection and publication state;
- last publication time.

States are `Uninitialized`, `AwaitingCraft`, `Connected`, `Active`,
`SuspendedByCursor`, `SuspendedByUI`, `NoLocalPilot`, and `Faulted`.
`V3CraftTestSpawner` reconnects the one local source after every rebuild.
Existing controls are preserved. O/P are new Input System actions for systems
page navigation.

## Runtime hierarchy

Every assembled craft now exposes:

```text
Craft root
├── Chassis
├── Electronics
│   ├── Mainframe
│   └── Chassis Sensors (six)
├── Cockpit
│   ├── Runtime Cockpit
│   └── System Computers (seven on the complete craft)
├── Hardware
│   ├── Energy Core
│   ├── Cooling
│   ├── Rear Propulsion
│   ├── Front Brake Reverse
│   ├── Hover Assemblies
│   ├── Roof Thrusters
│   ├── Lateral Thrusters
│   └── Active Aero
└── Presentation
    ├── Systems Console
    ├── Warning Display
    └── Debug Visualization
```

Organizational reparenting preserves world transforms. Socket positions,
connector child mounts, force origins, orientations, mass positions, and
prefab identities are unchanged. Mainframe execution remains centralized;
hierarchy service children are ownership markers, not new update loops.

## Folder migration

`Runtime/Runtime`, `Runtime/Data`, `Runtime/Assembly`, and `Runtime/Build` were
removed after verified-empty cleanup. Runtime source is now under `Core`,
`Parts`, `Systems`, `Input`, `Presentation`, and `Development`. Editor and
tests use their responsibility folders while preserving one runtime asmdef,
one editor asmdef, and one test asmdef.

## Migration map

The complete old-path → new-path table is in
`Docs/Hovercraft_V3_Migration_Map.md`.

## Generated asset ownership

- V2 reference generator:
  `Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/`
- Systems integration generator:
  `Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/`
- Parity harness generator:
  `Assets/HovercraftV3/Generated/RegressionAssets/Parity/`

`V3CanonicalAssetRegenerator` regenerates all owned assets in dependency order.

## Scene consolidation

- `Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity`
- `Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity`

Both saved scenes contain one local input source, spawner, HUD, camera binding,
and systems console. The Handling Track preserves the flat field, acceleration,
slalom, chicane, jump, banked deck, washboard, and alternating articulation
surfaces. Craft variants are build assets, not scenes.

## Craft test spawner

`V3CraftTestSpawner` selects a `CraftBuildDefinition`, validates it, assembles,
rebuilds, clears, resets through the session, and reconnects input, camera/HUD
session state, console, and debug options. Invalid builds report the first
actionable validation error. Rebuild events clear the prior runtime root before
publishing the new craft.

## Edit Mode preview

The `V3CraftAssembler` inspector now provides Build, Refresh, Clear, and
Validate Preview controls. `__V3_EDITOR_PREVIEW__` uses the same build,
sockets, connector-child mounts, prefabs, validation, mass calculation, and COM
calculation as runtime assembly. The preview removes all Rigidbodies, disables
colliders and Behaviours, contains no input source, and is tagged `EditorOnly`.
Scene handles show COM and thruster force directions. Preview is cleared before
Play Mode and can restore on returning to Edit Mode when the preview toggle is
enabled.

## Systems Console

`V3SystemsConsoleController` is read-only and refreshes at 12 Hz by default.
It caches the craft context and page registry, does not rebuild hierarchy every
frame, and does not publish control commands. O selects the previous page, P
the next page, and navigation wraps.

## UI pages

The dynamically discovered order is:

1. Mainframe
2. Pilot Interface
3. Drive
4. Hover
5. Stabilizer
6. Traction
7. Aerodynamics
8. Telemetry
9. Power and Cooling
10. Sensors

Pages read Mainframe, scheduler, Intent/Observation buses, Telemetry Hub,
registries, router, power, thermal, part runtime, hover/traction, aero, and
sensor snapshots. Missing systems are omitted or displayed as unavailable.
`GenericSystemsTheme.asset` separates visual theme from page data.

## Performance

- no page-owned `Update` loops;
- one bounded console refresh loop;
- cached component context and page providers;
- no string formatting in physics/control loops;
- no repeated scene search in hot paths;
- central Mainframe scheduler remains authoritative;
- system hierarchy markers add no update loop.

## Automated tests

- Final full suite: 98 discovered, 97 passed, 0 failed, 1 normal opt-in skip.
- Focused systems integration/endurance: 7 passed, 0 failed.
- Focused sprint contracts: 6 passed, 0 failed.
- Final enforced parity: 6 passed, 0 failed.
- Independent Roslyn audits: runtime, editor, and test assemblies compile with
  no C# errors or obsolete object-search warnings.

## Test result paths

- `TestResults/HovercraftV3/Fast/ReadabilitySprint-Final.xml`
- `TestResults/HovercraftV3/Integration/ReadabilitySprint-SystemsIntegration.xml`
- `TestResults/HovercraftV3/Integration/ReadabilitySprint-Contracts.xml`
- `TestResults/HovercraftV3/Parity/ReadabilitySprint-Final-EnforcedParity.xml`

## Parity status

Passed after canonical regeneration. The play-mode capture completed the full
profile with 7,801 V2 samples and 7,801 V3 samples.

## Asset/GUID audit

Post-regeneration audit:

- zero asset files missing metadata;
- zero orphan metadata files;
- zero duplicate GUIDs within Hovercraft V3;
- zero old generated/source paths in V3 code or serialized assets;
- zero missing V3 script references in development scenes;
- exactly two normal force-authority sites:
  `RuntimeThrusterInstance` and `RuntimeAerodynamicFinInstance`;
- complete craft contract verifies one Rigidbody and one collider.

## Known limitations

The console uses a generic temporary IMGUI presentation by design. Manufacturer
physical cockpit screens, final art, multiple router profiles, damage,
multiplayer, procedural audio, and final manufacturer UI themes remain outside
this sprint.

## Assumptions

- Unity 6000.5.0f1 and the Input System remain the project baseline.
- The V2 implementation under `Assets/Scripts/Hovercraft_Setup/` remains the
  behavioral reference.
- Runtime hierarchy readability must never supersede physical transform
  authority.

## Files moved

70 runtime sources, 21 editor/test sources, three generated subtrees, two
development scenes, and the shared environment materials. File `.meta`
identities were moved with their assets.

## Files added

Input authority state/debug lifecycle, runtime hierarchy/identity markers,
craft test spawner, systems console contracts/controller/theme, Edit Mode
preview utility, canonical regenerator, sprint regression tests, target folder
metadata, migration report/map, README clarity, and the clean canonical
handoff.

## Files removed

Only verified-empty legacy V3 folders and their folder metadata were removed
after migration: old runtime buckets, `FreeDrive/`, and the old generated
systems scene folder. No V2 source or V2 scene was moved.

## Safe next steps

Open `V3_CraftLab` to inspect/edit builds and use the assembler preview. Open
`V3_HandlingTrack` for hands-on testing. Future work should add authored
manufacturer content under `Content/` and retain the physical authority boundaries,
one root Rigidbody, one collider, scene-owned local input, and centralized
Mainframe execution.
