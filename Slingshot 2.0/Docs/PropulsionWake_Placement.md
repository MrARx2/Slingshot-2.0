# Propulsion Wake Placement

Use `Assets/Prefabs/Hovercraft_PropulsionWakePlacement.prefab` to align the cinematic wake with the visible hovercraft exhausts.

1. Drag the prefab below `HovercraftRootV2` in the Hierarchy. Keep the prefab root at local position `(0, 0, 0)`, local rotation `(0, 0, 0)`, and scale `(1, 1, 1)`.
2. Move `LEFT EXHAUST - PLACE ON NOZZLE` to the exact center of the visible left outlet.
3. Move `RIGHT EXHAUST - PLACE ON NOZZLE` to the exact center of the visible right outlet.
4. Rotate each anchor so its blue **Z** axis points outward from the nozzle, in the direction the exhaust should travel.
5. Exit and re-enter Play Mode after adding the prefab. Position and rotation adjustments then drive every wake layer automatically.

The cyan and violet Scene-view arrows are editor-only placement guides. They are not rendered in the game.

Do not move the generated `Cinematic Propulsion Wake` children. They are rebuilt at runtime and follow the two authored anchors.
