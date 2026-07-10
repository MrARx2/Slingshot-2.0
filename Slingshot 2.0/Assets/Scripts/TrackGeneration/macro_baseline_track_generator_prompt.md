# Hovercraft Track Generator V1 — Macro Sections + Box/Prism Geometry

## Purpose

This document defines the direction for the V1 procedural track generator for the fast hovercraft racing game.

The current generator should evolve from a mostly spline-driven system into a clearer **macro-section track generator**.

The track should feel like a real readable race track:

- Straight means straight.
- Turn means turn.
- Jump means jump.
- Hairpin means hairpin.
- Chicane means chicane.
- S-curve means S-curve.

Do **not** build the gameplay layout from many tiny random angle changes.

Bad example:

```text
Straight → 6 degree turn → 4 degree turn → 8 degree turn → 5 degree turn → 7 degree turn
```

Good example:

```text
Straight → 90 degree banked curve → long straight → jump section → recovery straight
```

The generator should decide the **macro gameplay section first**, then build the geometry for that section.

Correct order:

```text
1. Choose macro section:
   "90 degree high-speed banked curve"

2. Decide parameters:
   angle, radius, width, banking, length, subdivision count

3. Build the section from clean cube/prism subdivisions
```

Wrong order:

```text
1. Randomly rotate tiny pieces
2. Hope the result becomes a fun track
```

---

# Current Architecture

The current uploaded scripts already have a usable generation pipeline.

Current flow:

```text
TrackGenerator
→ Seed setup
→ Spline generation
→ Branches / shortcuts
→ Stunt placement
→ Gravity zone placement
→ Mesh generation
→ Stunt actors
→ Gravity actors
```

Important existing systems to preserve where useful:

- `TrackGenerator`
- `TrackConfig`
- `TrackSeed`
- `TrackSeedManager`
- `TrackData`
- `TrackMeshBuilder`
- `TrackSectionAnalyzer`
- `StuntPlacer`
- `RampActor`
- `BoostPadActor`
- `WallRideActor`
- `GravityZone`
- `GravityField`
- Editor generate button/debug workflow

Do not blindly delete the current system.

The safest transition is:

```text
Current spline-first generator
→ Add macro section generator
→ Build section frames
→ Feed those frames into a new prism/box mesh builder
→ Reconnect existing stunts/gravity/branches later
```

The big architectural change:

```text
Old brain: spline shape decides the race track
New brain: macro race sections decide the race track
```

Splines may still be used as helper data or adapters, but they should not be the main designer brain of the track.

---

# V1 Macro Piece List

## 1. Straight

Basic forward section.

Used for:

- Connection
- Speed build-up
- Pacing
- Breathing room

---

## 2. Wide Straight

A wider straight section.

Used for:

- Recovery
- Overtaking
- Stabilizing after difficult sections
- Giving the player more room

---

## 3. Boost Straight

A straight section intended for speed build-up.

For V1, this can simply be tagged as boost-capable.

Later it can contain:

- Boost pads
- Boost lanes
- Visual speed gates

---

## 4. Banked Curve

The main corner type.

The hovercraft is very fast, so most corners should be banked by default.

Supports:

- Left / right direction
- Gentle / medium / sharp angle
- Radius
- Banking strength
- Subdivision count

Flat sharp turns should be avoided in V1 unless intentionally marked as slow technical challenges.

---

## 5. Banked Hairpin

A strong 120–180 degree banked turn.

Used as:

- Major direction change
- Major skill check
- Rare dramatic section

Use sparingly.

---

## 6. S-Curve

A flowing left-right or right-left sequence.

Example:

```text
Left 45 degree banked curve → Right 45 degree banked curve
```

It should feel rhythmic and readable.

It should **not** feel like random wiggle noise.

---

## 7. Chicane

A sharper left-right-left or right-left-right section.

Used to:

- Break up long high-speed sections
- Test reaction/control
- Force quick but readable direction changes

It should be deliberate and obvious.

---

## 8. Jump Ramp

A launch ramp that sends the hovercraft airborne.

Used for:

- Air-control gameplay
- Spectacle
- Speed expression

---

## 9. Air Gap

The empty space between jump ramp and landing ramp.

Can vary by:

- Distance
- Height
- Risk
- Landing difficulty

---

## 10. Landing Ramp / Recovery Platform

A reversed ramp that catches the hovercraft after a jump.

This should behave like a motocross landing ramp.

The point is to avoid forcing the hovercraft to crash onto a flat platform after a high-speed jump.

The landing ramp should:

- Slope in the expected landing direction
- Let the player match pitch to the landing
- Smoothly return the craft to the track

---

## 11. Recovery Straight

A safe straight section after a difficult section.

Used after:

- Jumps
- Hairpins
- Chicanes
- Chaotic sections
- High-bank turns

---

## 12. Tunnel / Bridge Variant

A visual/environmental variant of another piece.

Not a core gameplay type for V1.

Can be applied to:

- Straight
- Wide Straight
- Banked Curve

---

# Geometry Philosophy

Everything should be built from **cube-like / box-like / prism-like sections** with clean topology.

This does **not** mean low-resolution.

Allowed:

- High subdivision counts
- Many pieces
- Smooth curves
- Smooth banking transitions
- Smooth ramps
- Future loops
- Future corkscrews

Not allowed:

- N-gons
- Messy freeform topology
- Strange non-manifold shapes
- Procedural spaghetti
- Tiny random gameplay steering changes
- Complex unpredictable collision surfaces

The track can be geometrically smooth, but structurally it must be made from clear macro race sections.

Core rule:

```text
Macro section = readable gameplay unit
Subdivision = internal geometry detail
```

Example:

```text
Banked Curve = one macro track section
Internally built from 24 clean prism subdivisions
```

---

# Cube / Prism Vertex Manipulation

A road segment starts conceptually as a rectangular prism.

Basic prism structure:

- 8 core corner vertices
- Top face is the driving surface
- Bottom and side faces give thickness/collision
- Clean quad-like topology where possible
- Unity triangles are fine for rendering
- Logical construction should remain rectangular/quad-based
- No n-gons
- No messy custom freeform shapes

---

## Straight Section Geometry

A straight is an elongated rectangular prism.

The front ring and back ring have:

- Same width
- Same height
- Same orientation
- Same bank angle

Conceptually:

```text
Back ring  →  Front ring
same width    same width
same bank     same bank
```

---

## Turn / Curve Geometry

A turn is built by connecting many wedge-like prism subdivisions.

For a left turn:

- The left/inside side travels a shorter distance.
- The right/outside side travels a longer distance.
- Each subdivision remains a clean prism.
- Connected together, the subdivisions form a smooth curve.

Important:

```text
A 90 degree curve may use 16, 24, 32, or more prism subdivisions.
But it is still ONE macro gameplay section.
```

Do not treat each small subdivision as a separate gameplay turn.

---

## Banked Curve Geometry

A banked curve is created by raising/lowering side vertices.

For a left banked curve:

- The outside/right edge is raised.
- The inside/left edge stays lower.
- The driving surface tilts inward toward the turn.

This creates a NASCAR-style / futuristic high-speed banked corner.

Banking should be gradual:

```text
Bank-in transition
→ Full bank hold
→ Bank-out transition
```

Banking should visually communicate:

- This is a high-speed turn.
- The player can hold speed.
- The track is helping the hovercraft turn.

---

## Ramp / Jump Geometry

A jump ramp is made by raising the front/top vertices relative to the back/top vertices.

The ramp should:

- Be prism-based
- Have clean topology
- Be subdivided along length if needed
- Pitch upward smoothly

Normal jump structure:

```text
Approach or Boost Straight
→ Jump Ramp
→ Air Gap
→ Landing Ramp / Recovery Platform
→ Recovery Straight
```

---

## Landing Ramp / Recovery Platform Geometry

The landing ramp is the reverse of the jump ramp.

It should catch the hovercraft smoothly, like a motocross landing ramp.

The landing ramp should:

- Slope downward in the expected landing direction
- Allow the player to match pitch
- Reduce physics jank
- Avoid sudden flat impacts
- Transition back into normal road flow

Avoid flat landings after high-speed jumps unless intentionally creating a dangerous section.

---

## Loop Geometry

Loops are not required for the first pass, but the architecture should support them later.

A loop can be created from many prism subdivisions where each subdivision gradually rotates in pitch.

Each segment remains a clean prism.

The full loop is one macro gameplay section.

---

## Corkscrew Geometry

Corkscrews are not required for the first pass, but the architecture should support them later.

A corkscrew can be created from many prism subdivisions where each subdivision gradually changes:

- Forward direction
- Pitch
- Roll / bank angle

Each subdivision remains cube/prism-based.

The full corkscrew is one macro gameplay section.

---

# Track Readability Rules

The generator should create sections like a real race track.

Good:

```text
Straight
→ 90 Degree Banked Curve
→ Long Straight
→ Jump Section
→ Recovery Straight
```

Bad:

```text
Straight
→ 7 Degree Turn
→ 5 Degree Turn
→ 9 Degree Turn
→ 4 Degree Turn
→ 6 Degree Turn
```

The mesh builder can subdivide one macro turn into many small prism chunks.

But the gameplay generator must still treat the whole turn as one intentional section.

The generator should never rely on random tiny rotations to create the track.

---

# Generator Rules

The macro layout generator should enforce:

- Do not place a jump directly into a sharp turn.
- Do not place a hairpin immediately after a jump.
- Do not place hairpins too frequently.
- Do not chain too many chicanes.
- Do not chain several small turns and call it a corner.
- Add recovery straight after jumps.
- Add recovery straight after hairpins.
- Add recovery straight after chicanes.
- Banked turns are the default.
- Use long enough straights for hovercraft speed/readability.
- Prefer clear racing language over procedural randomness.
- Preserve deterministic seed behavior.

---

# Suggested V1 Piece Frequency

Common:

- Straight
- Wide Straight
- Banked Curve
- Recovery Straight

Occasional:

- Boost Straight
- S-Curve
- Chicane
- Jump Section

Rare:

- Banked Hairpin

Later:

- Tunnel
- Bridge
- Wall ride
- Loop
- Corkscrew
- Gravity sections
- Shortcuts

---

# Proposed New Systems

## 1. TrackMacroSectionDefinition

A data definition for a readable gameplay section.

Suggested fields:

```csharp
public enum TrackMacroSectionType
{
    Straight,
    WideStraight,
    BoostStraight,
    BankedCurve,
    BankedHairpin,
    SCurve,
    Chicane,
    JumpRamp,
    AirGap,
    LandingRamp,
    RecoveryStraight,
    TunnelVariant,
    BridgeVariant
}
```

Suggested data:

```text
SectionType
Length
Width
Direction
TurnAngle
Radius
BankingAngle
ElevationChange
PitchChange
RollChange
SubdivisionCount
SpeedIntent
RiskLevel
RequiresRecoveryAfter
AllowsBoost
AllowsJump
DebugName
```

---

## 2. GeneratedTrackSection

Represents one actual chosen section in the generated track.

Should store:

```text
Definition / parameters
Start frame
End frame
Generated subdivision frames
Section bounds
Debug data
```

---

## 3. TrackConnectionFrame

Each section needs a clean entry and exit frame.

Suggested fields:

```text
Position
Forward
Right
Up
Width
BankAngle
PitchAngle
ArcLength
```

This allows pieces to connect cleanly without gaps.

---

## 4. MacroTrackLayoutGenerator

Chooses the sequence of real track sections.

It should not generate tiny random rotations.

Example output:

```text
Straight
→ Banked Curve
→ Straight
→ S-Curve
→ Boost Straight
→ Jump Section
→ Recovery Straight
→ Banked Hairpin
→ Wide Straight
```

This class is the new “designer brain” of the track.

---

## 5. BoxPrismTrackMeshBuilder

Builds the actual geometry from the macro sections.

Responsibilities:

- Build vertices from prism rings
- Build road surface
- Build sides
- Build bottom/thickness
- Build simple collision
- Avoid n-gons
- Avoid messy topology
- Support high subdivision counts
- Support future pitch/roll/yaw transitions
- Support loops and corkscrews later

---

# Implementation Direction

Do not try to solve everything at once.

First inspect the current scripts and propose the safest file-by-file implementation plan.

Preferred transition path:

```text
1. Keep TrackGenerator orchestration.
2. Keep TrackSeed / TrackSeedManager.
3. Add macro section data structures.
4. Add MacroTrackLayoutGenerator.
5. Add BoxPrismTrackMeshBuilder.
6. Generate a closed loop from macro sections.
7. Keep old spline generator available until the new system is stable.
8. Reconnect stunts/gravity/branches later.
```

---

# First Milestone

A complete drivable closed loop using:

- Straight
- Wide Straight
- Boost Straight
- Banked Curve
- Jump Ramp
- Air Gap
- Landing Ramp / Recovery Platform
- Recovery Straight

The track should be:

- Fast
- Readable
- Banked by default on turns
- Built from clean cube/prism subdivisions
- Deterministic by seed
- Debuggable in the editor
- Collision-stable

---

# Suggested First Test Layout

```text
Straight
→ Banked Curve
→ Long Straight
→ Boost Straight
→ Jump Ramp
→ Air Gap
→ Landing Ramp
→ Recovery Straight
→ Banked Curve
→ Wide Straight
→ Banked Curve
→ Straight
```

This is enough to test:

- Straight generation
- Banked turn generation
- Smooth prism subdivision
- Jump/landing sequence
- Recovery logic
- Closed-loop connection

---

# Debug Requirements

Add debug visualization for:

- Macro section labels
- Section start frame
- Section end frame
- Banking direction
- Turn direction
- Jump landing target
- Section bounds
- Seed value
- Generated section list

Expose inspector parameters:

```text
Seed
Use Random Seed
Segment count
Track width
Min straight length
Max straight length
Curve angle options
Curve radius options
Banking strength
Subdivision density
Jump frequency
Hairpin frequency
Chicane frequency
S-curve frequency
Recovery length
```

---

# Claude Task

Please start by reviewing the current uploaded scripts.

Then:

1. Summarize the current generator architecture.
2. Identify which files should be preserved, modified, or replaced.
3. Propose the safest implementation plan.
4. Implement the first milestone:
   - Macro section generation
   - Box/prism mesh generation
   - A complete drivable closed loop
   - Basic debug visualization
5. Do not remove useful existing systems unless the replacement is working.
6. Keep the seed system deterministic.
7. Keep the editor workflow usable.
8. Do not build spline spaghetti.
9. Build readable macro race sections first, then geometry.

Main rule:

```text
Macro race design first.
Cube/prism geometry second.
Readable hovercraft racing always.
```
