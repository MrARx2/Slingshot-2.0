# Hovercraft V3 Track Test Infrastructure Repair

> 2026-08-01 architecture-repair update: the canonical world ID is `world.root.v3_tracktest`; the single Track Test scene now defaults to the primary systems build and supports baseline/primary/selected-authored presets. Recorder schema-6 physical takeoff/landing semantics supersede earlier generic transition labels.

Completed: 2026-08-01  
Scope: startup, recovery, and diagnostic correctness only  
Handling/aerodynamic tuning: intentionally unchanged

## Outcomes

The 48.23 km procedural Track Test geometry is now generated once in the
Editor and stored as a reusable nested prefab with persistent mesh assets.
`V3_TrackTest.unity` adopts that cache with `generateOnStart = false` and
`keepEditorTrackOnPlay = true`. Pressing Play no longer asks the main thread to
plan, mesh, import, and construct the complete track before the first craft
physics tick.

The cache lives at:

`Assets/HovercraftV3/Generated/TrackTestCache/`

It can be deliberately rebuilt from:

`Tools > Hovercraft V3 > Track Test > Rebuild Cached Track`

Then regenerate or upgrade the scene from:

`Tools > Hovercraft V3 > Generate V3 Track Test Scene`

If the Track Test scene is already open, the generator upgrades it in place so
other scene objects and references are preserved.

## Recovery boundary

The kill plane is no longer a fixed world-space assumption. `TrackGenerator`
computes the renderer bounds of the adopted/generated track and notifies the
V3 track bridge. `V3FreeDriveSession` places the recovery threshold 250 metres
below the lowest track geometry. For the recorded track, whose lowest geometry
was approximately -252.3 m, the operational boundary is therefore near -502.3
m. A normal dip below -20 m is no longer treated as a fall from the world.

## Reset-aware recording

Every V3 craft reset publishes a typed notification:

- `ManualInput` for Backspace;
- `KillPlane` for automatic below-world recovery;
- `TrackGenerator` and `TestAutomation` for explicit infrastructure callers;
- `Unknown` for legacy callers that provide no reason.

The recorder records the reason, increments `attemptIndex`, inserts an explicit
Reset event, and marks the first post-reset sample as an external-state
discontinuity. World acceleration history, event-transition history, and report
force/residual aggregates reset at this boundary. Teleport artifacts therefore
remain auditable without being diagnosed as takeoff, surface discontinuity,
extreme acceleration, or unexplained force.

Exports now include attempt identity in core/binary/event data plus
`attempt_summary.csv` and per-attempt JSON/Markdown summaries.

## Diagnostic classification corrections

- Near-surface classification reads the assembled craft's serialized hover
  target and operational range. It no longer assumes a 3 m craft when the
  active build is configured for 8 m.
- Surface-loss velocity is measured along the local track surface normal.
- Rollover/spin analysis uses the mapped local track normal in loops and
  corkscrews, with gravity-up only as a fallback when no track frame exists.
- Power starvation requires a meaningful absolute/relative deficit; harmless
  float rounding differences no longer create thousands of false limited
  samples.

## Main files changed

- `Assets/Scripts/TrackGeneration/TrackGenerator.cs`
- `Assets/Scripts/TrackGeneration/ITrackRaceCraft.cs`
- `Assets/HovercraftV3/Runtime/Development/TestDrive/V3FreeDriveSession.cs`
- `Assets/HovercraftV3/Runtime/Development/TrackIntegration/V3TrackRaceCraftBridge.cs`
- `Assets/HovercraftV3/Editor/SceneTools/V3TrackTestSceneGenerator.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Core/V3DiagnosticSchema.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Core/V3DiagnosticRecorder.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Sources/V3WorldTruthRecorder.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Sources/V3WorldContactRecorder.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Events/V3DiagnosticEventDetector.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Export/V3DiagnosticReportBuilder.cs`
- `Assets/HovercraftV3/Runtime/Diagnostics/Export/V3DiagnosticExportService.cs`
- `Assets/HovercraftV3/Runtime/Core/Power/V3PowerDistributor.cs`

## Regression coverage

Focused tests cover track bounds below zero, typed reset notification and
derived kill plane, reset acceleration suppression, the configured 8 m hover
envelope, loop-local rollover classification, floating-point power tolerance,
attempt/report segmentation, and the cached-scene contract.

Final validation on Unity 6000.5.0f1:

- affected production assemblies: 4/4 build successfully;
- focused Edit Mode regressions: 8/8 passed;
- cached Track Test Play Mode smoke test: 1/1 passed;
- measured Play Mode smoke-test case duration: 23.14 seconds, including entry,
  cached-track adoption, V3 craft assembly/placement, assertions, and exit;
- cache: 509 unique persistent mesh assets, approximately 1.27 GB;
- cache and scene: zero missing script references;
- project metadata: zero missing `.meta` files and zero duplicate GUIDs;
- scene: cached prefab reference present, runtime generation disabled, editor
  track preservation enabled;
- pending one-shot cache/test requests: none.

No spring, damping, thrust, drag, lift, authority, speed, or other handling
coefficient was changed in this repair pass.
