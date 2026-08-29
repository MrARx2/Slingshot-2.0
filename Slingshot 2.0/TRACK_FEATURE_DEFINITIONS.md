# Track Feature Definitions

Track Generator V2 treats each named feature as an individual, versioned content asset. Runtime
assets live in `Assets/Resources/TrackFeatureDefinitions`; geometry code provides reusable solvers,
while each definition owns the feature's identity, permitted ranges, contracts and primitive recipe.

## Current coverage

- 28/28 serialized designer pattern types have one definition asset.
- Ordinary Curve and Directional Corkscrew have additional selectable semantic assets.
- Total current catalog: 30 individual definitions.

The first additive V2 catalog wave is deliberately opt-in while it earns its stability gate:

- **Camelback** — a tall C² crest with an authored 1 km hard cap. The compiler expands within
  that cap and then reduces height until pitch, curvature-induced load and curvature rate all pass.
- **Cutback** — a definition-native 135° corner available to explicit required patterns and Track
  Editor replacement, with canonical-token closure support and the shared 1 km ordinary-road cap.
- **Heartline Roll** — a compact heading-neutral transported roll with four roll units and a shallow
  centerline orbit, sized from 0.9–1.8 km.
- **Zero-G Roll** — a heading-neutral transported roll with a controlled rise/fall profile and four
  roll units, sized from 1.1–2.2 km.
- **Dive Loop** — a 180° moderate inversion that rolls into a descending vertical phase and owns its
  recovery, sized from 1.5–2.8 km.
- **Sidewinder** — a 90° moderate inversion that combines a vertical arc with a transported roll and
  owns its recovery, sized from 1.45–2.7 km.

Each feature now has an independent ON/MIN/MAX row in the normal Generator controls. These rows
default to off, so established presets and seeds keep their current pacing; enabling a row admits
that feature to weighted procedural placement, while explicit Required Pattern recipes remain valid.

The Track Generator Inspector shows this coverage. Open **Track > V2 > Feature Definitions** to edit
the assets and use **Validate All** after any addition or tuning pass.

## Definition rules

1. Stable IDs are permanent and globally unique.
2. Increment `DefinitionVersion` when intentionally changing an accepted feature contract or shape.
3. Every pattern asset must map to exactly one `TrackPatternType` and the matching
   `SemanticElementId`.
4. Entry, core and recovery lengths must be positive and explicit. Compact fitting may scale the
   entry/recovery budgets, but the authored source contract remains unchanged.
5. Primitive IDs are stable, unique inside the feature and total exactly 1.0 core-length fraction.
6. Internal primitive boundaries never receive generic connectors. The feature is solved and
   validated atomically.
7. Definitions describe creative intent; the rulebook retains all hard clearance, curvature,
   transition-rate, closure and driveability ceilings.
8. A new feature is not enabled procedurally until its definition, builder, count enforcement,
   deterministic recipe behavior, focused tests and one visual driveability check all pass.

## Safe addition sequence

1. Add the semantic and append the serialized pattern enum without reordering existing values.
2. Add one disabled-by-default definition asset and a reusable solver/primitive path.
3. Add Track Editor preview/replacement support and exact boundary tests.
4. Add designer Min/Max controls, resolved configuration, allocation and final count validation.
5. Add deterministic recipe round-trip and canonical-layout tests.
6. Run the focused Unity gate and a bounded seed sweep.
7. Visually inspect entry, core, recovery, clearance and driving flow before enabling it in presets.

This order deliberately separates content introduction from procedural rollout: a feature can be
authored and inspected without changing existing seed generation, then enabled only after evidence
shows it is stable.

The rotational additions share the reusable `RotationalSequenceV1` compiler. Their individual assets
own phase order, roll/vertical units, radii, handedness, orbit, pitch and yaw shaping, length envelope,
entry/exit contract, intentional core roll speed and recovery policy; the compiler owns only the
deterministic construction and hard safety checks. Intentional inversion roll speed is distinct from
the ordinary connector roll-rate envelope: connector boundaries still return to the shared legal
rate, while the authored core is sized in degrees per second so compact inversions scale correctly
with the selected design speed.

The bounded sweep has a **Require One Feature** mode. It clones the current designer settings,
adds one explicit required pattern for the selected feature, and verifies that every reported pass
actually contains that semantic element. Use this admission mode before moving an opt-in feature
into normal random selection.
