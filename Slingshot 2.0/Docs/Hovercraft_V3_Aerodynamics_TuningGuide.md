# Hovercraft V3 — Aerodynamics Tuning Guide

How to set up and tune the V3 aerodynamic model. Architecture reference:
`Hovercraft_V3_Aerodynamics_Architecture.md`.

## 1. Golden rules

1. **The world defines the air, the craft defines the reaction.** Density,
   wind, turbulence and global multipliers live on `WorldAtmosphere`; CdA,
   center of pressure and surfaces live on the craft. Never tune a track
   problem with craft values or vice versa.
2. **Tune one migration step at a time.** Legacy → body-only → rear
   stabilizers → wing/GE surfaces → active aero. Do not jump straight to a
   full surface build.
3. **Watch the recorder, not your gut.** `aero_frames.csv` and
   `aero_surfaces.csv` show exactly which term produced which force.
4. Keep the Medium Rev4 craft in **LegacyV2** until V3 passes the validation
   tests.

## 2. Migration walkthrough

### Step 1 — LegacyV2 (no action)
Default mode. Behavior identical to the shipped craft.

### Step 2 — DirectionalBodyV3
1. Switch `mode` to `DirectionalBodyV3`. Leave both
   `useLegacyWingDownforceInBodyMode` and `useLegacyGroundEffectInBodyMode` ON
   — downforce stays as before while you tune drag.
2. Starting body values (Medium class):

   | Field | Value |
   |---|---|
   | Forward CdA | 2.5 |
   | Reverse CdA | 3.2 |
   | Side CdA | 6.0 |
   | Top CdA | 4.0 |
   | Bottom CdA | 4.5 |
   | Center of pressure | (0, 0.1, −0.8) — 0.5–1.0 m behind COM |

3. Verify: nose-first top speed close to the legacy value (inspector shows the
   forward-CdA estimate); sliding broadside scrubs speed hard; the craft
   passively straightens nose-into-airflow when airborne.

### Step 3 — Rear stabilizers (SurfaceBasedV3)
1. Create two children at the tail (~z = −1.8, y = +0.4):
   - **RearVerticalFin** — rotate 90° roll so +Y (lift axis) points sideways;
     type `VerticalFin`, area 0.3–0.5 m².
   - **RearHorizontalStabilizer** — level; type `Stabilizer`, area 0.4–0.6 m².
2. Switch `mode` to `SurfaceBasedV3` (the legacy bridge turns off — see step 4
   for replacing wing/GE downforce; for a stabilizer-only test you can stay in
   DirectionalBodyV3 and skip ahead).
3. Verify: side-slip creates a yaw moment toward airflow alignment; airborne
   pitch oscillation damps out instead of tumbling. If the tail wags, reduce
   fin area before touching `yawStability` damping.

### Step 4 — Wing and ground-effect surfaces
Replace the aggregate legacy values with:

| Surface | Type | Position | Setup |
|---|---|---|---|
| FrontDownforceSurface | Wing | nose, low | `baseLiftCoefficient` ≈ −0.6, area ≈ 0.8 m² |
| RearDownforceSurface | Wing | tail, high | `baseLiftCoefficient` ≈ −0.9, area ≈ 1.0 m² |
| UndersideGroundEffectSurface | GroundEffect | belly center | `baseLiftCoefficient` ≈ 1.0 (suction), area ≈ 2.0 m², range 3–4 m, layers = track only |

Match the old grip level: legacy downforce was
`½ρv²·(wingClA)` with wingClA = 2 m². Surface downforce is
`½ρv²·Σ(|Cl|·area)` — so make `Σ(|Cl|·area)` ≈ 2 to start (0.6·0.8 + 0.9·1.0 ≈
1.4 plus body lift/GE covers the rest; tune to taste). Front/rear split sets
the pitch balance under braking and over jumps.

### Step 5 — Active aero (later)
`AeroSurface.SetControlDeflection(deg)` is the entry point (rate-limited,
authority-scaled). Nothing drives it yet by design.

## 3. What each knob does

### Body (AeroBodyDefinition)
- **forwardCdA** — top speed. Lower = faster. The inspector's estimated top
  speed updates live.
- **reverseCdA** — how violently the craft slows when sliding backwards.
- **sideCdA** — cost of broadside flight; the main "stop feeling like a plank"
  value together with CoP. Raise until sideways jumps feel draggy.
- **top/bottomCdA** — vertical damping in air: bottomCdA softens landings
  (falling = air resisting on the belly), topCdA resists being launched up.
- **centerOfPressureLocalM.z** — stability. More negative (further behind COM)
  = stronger nose-into-airflow weathervane. Positive = unstable/agile (expert
  craft only).
- **centerOfPressureLocalM.y** — above COM: drag pitches the nose down under
  braking airflow; below: nose up.
- **pitch/yaw/rollStability** — LAST RESORT damping. Try CoP offset and fin
  area first; damping on top of both double-counts stability and makes the
  craft feel dead.

### Surfaces
- **areaM2** — linear force scale.
- **baseLiftCoefficient** — camber: constant lift at zero AoA. Negative =
  downforce wing. For GroundEffect surfaces it's the suction strength
  (magnitude used).
- **stallAngleDeg / postStallLiftMultiplier** — where lift gives up. Lower
  stall angle = craft punishes sloppy airborne pitch earlier.
- **Cl/Cd curves** — full custom override (absolute coefficient vs signed AoA
  in degrees, 2+ keys to activate). Use for asymmetric wings.
- **groundEffectRangeM + curve** — how high the venturi floor works and its
  falloff shape (default (1−h)²). Force is exactly zero with no probe hit —
  leaving a track edge kills GE immediately, by design.

### World (per track/environment)
- **airDensityKgM3** — global speed/grip trade: thin air = faster and slippier
  everywhere.
- **windVelocityWorldMs** — constant push; affects every craft through
  relative airflow.
- **turbulence** — off by default; Storm preset is a reasonable starting point
  (8 m/s, 30 m cells). Same seed = same gusts (replays stay deterministic).
- **global multipliers** — blunt per-track balancing tools; keep 1 unless the
  track concept demands otherwise.

## 4. Class differentiation recipes

| | Agile | Medium | Heavy |
|---|---|---|---|
| forwardCdA | 1.8 | 2.5 | 3.5 |
| sideCdA | 4.5 | 6.0 | 8.5 |
| CoP z-offset | −0.4 (lively) | −0.8 | −1.2 (very stable) |
| Fin/stabilizer area | small (0.25 m²) | medium (0.4 m²) | large (0.7 m²) |
| controlResponseSpeedDegPerSec | 180 | 90 | 45 |
| controlAuthorityMultiplier | 1.3 | 1.0 | 0.8 |
| maximumAeroAccelerationG | 10 | 12 | 14 |

## 5. Debugging workflow

1. **Inspector Debug foldouts** (play mode): craft-level AoA/side-slip, per
   term forces, clamp warnings; per-surface AoA/Cl/Cd/stall/GE probe.
2. **Scene gizmos**: yellow COM, orange CoP (+ their offset line), white
   airflow, magenta forces, red torque; surfaces draw their area quad, chord
   (cyan), lift axis (yellow) and GE probe (green).
3. **Recorder**: run a test drive; open `aero_frames.csv` (aggregate) and
   `aero_surfaces.csv` (per surface). Airborne events in `events.csv` carry the
   full force balance at takeoff/landing/launch.
4. **Clamp flags**: `forceClamped`/`torqueClamped` firing constantly means the
   coefficients are unphysical — fix the numbers, don't raise the clamp.

## 6. Common symptoms

| Symptom | Likely cause | Fix |
|---|---|---|
| Craft still feels like a floating plank | Side/top CdA too low, CoP at COM | Raise sideCdA, move CoP back |
| Nose hunts left-right at speed | CoP too far back + big fin | Reduce fin area or CoP offset |
| Backflips off every jump | Rear downforce ≫ front, or CoP below COM | Rebalance front/rear wing, raise CoP y |
| Sticks to walls after leaving track | (Should be impossible in V3) GE layers include non-track geometry | Restrict `groundEffectLayers` |
| Jitter at standstill in wind | `minimumAirflowSpeedMs` too low | Keep ≥ 2 m/s |
| Sudden force spikes in turbulence | Turbulence strength ≫ scale | Larger `turbulenceScaleM`, lower strength |
| Top speed dropped after V3 switch | forwardCdA > legacy CdA | Match forwardCdA to old `dragCoefficientTimesArea` |
