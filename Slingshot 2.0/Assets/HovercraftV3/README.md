# Hovercraft V3

This folder contains the active Hovercraft V3 implementation.

- `Runtime/` contains shared production/runtime code organized by responsibility.
- `Content/` contains authored product and UI data.
- `Generated/ReferenceCrafts/` contains generator-owned reference craft.
- `Generated/RegressionAssets/` contains generator-owned regression data and scenes.
- `Scenes/Development/` contains the two human-facing development scenes.
- `Editor/` contains authoring, generation, preview, and validation tools.
- `Tests/Editor/` contains the single organized Edit Mode test assembly.

Start with:

- `Scenes/Development/V3_CraftLab.unity` for assembly preview and systems inspection.
- `Scenes/Development/V3_HandlingTrack.unity` for driving and handling tests.

Craft variants are `CraftBuildDefinition` assets selected by
`V3CraftTestSpawner`; they do not require separate human-facing scenes.

Legacy ownership:

- Active Hovercraft V2 source: `Assets/Scripts/Hovercraft_Setup/`
- Non-compiling archive: `LegacyHovercraft/`
- Active Hovercraft V3 source: `Assets/HovercraftV3/`

Do not move V2 into V3 or compile the legacy archive.
