# Hovercraft Codebase Ownership

The project contains three distinct hovercraft code areas.

## Active Hovercraft V2

`Assets/Scripts/Hovercraft_Setup/`

This is the active V2 source and editor tooling. V3 parity uses it as the
behavioral reference. Do not reorganize it as part of V3 work.

## Legacy archive

`LegacyHovercraft/`

This is an archive only. Do not add it to Unity compilation, move it into
`Assets/`, or treat it as the active V2 implementation.

## Active Hovercraft V3

`Assets/HovercraftV3/`

This is the active modular V3 architecture. Its canonical overview is
`Docs/Hovercraft_V3_AI_Agent_Handoff.md`.
