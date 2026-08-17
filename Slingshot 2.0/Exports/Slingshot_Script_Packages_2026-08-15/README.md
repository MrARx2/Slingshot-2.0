# Slingshot 2.0 Script Packages

Snapshot prepared on 2026-08-15. The original project files were not moved or changed.

## Packages

- `Track_Generator` — production track generation runtime/editor scripts, assembly definitions, Unity metadata, and current configuration assets. Editor tests are intentionally excluded.
- `Camera` — the hovercraft camera and track-section sensor scripts.
- `HUD` — the main craft HUD, F2 diagnostic HUD, hover-probe telemetry overlay, and race/time-attack HUD.

Each folder preserves its original `Assets/Scripts/...` layout so it can be inspected or copied into another Unity project more easily. Matching ZIP archives are provided beside the folders.

These packages are related but not fully standalone. Camera and HUD scripts reference Slingshot hovercraft systems such as `CraftCore`; the race HUD references `RaceCourse`. The Track Generator assembly references Unity Mathematics and Unity Input System.
