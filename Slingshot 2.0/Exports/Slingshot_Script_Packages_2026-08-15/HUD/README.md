# HUD Package

Contains:

- `CraftHUD.cs` — player-facing top-center racing HUD.
- `CraftDebugHUD.cs` — F2 diagnostic HUD.
- `HoverProbeTelemetry.cs` — hover-probe diagnostic overlay.
- `RaceHUD.cs` — race/time-attack HUD.
- Matching Unity `.meta` files.

The craft HUD scripts expect the Slingshot hovercraft runtime (`CraftCore`, telemetry, overcharge, energy, traction, and stabilizer systems). `RaceHUD` expects `RaceCourse` from the Track Generator package. Diagnostic input uses Unity Input System.
