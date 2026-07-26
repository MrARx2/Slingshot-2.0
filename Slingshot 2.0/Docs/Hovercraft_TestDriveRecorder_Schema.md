# Hovercraft Test Drive Recorder — Schema (v1.4)

`recorderSchemaVersion` appears in every metadata block. Major version changes are breaking; minor differences warn.

## Bundle layout

```
TestDriveReports/YYYY-MM-DD_HHMMSS_Craft_Session/
  session_summary.md        human report (21-section structure)
  session_metadata.json     identity, environment, craft, track, input, frequencies, thruster table
  core_frames.csv           Stream A
  hover_nodes.csv           Stream B (row per hover node per sample)
  thrusters.csv             Stream C (row per thruster per sample; header maps indices to labels)
  extended_systems.csv      Stream D
  events.csv                Stream E
  collisions.csv            Stream F
  track_sections.csv        Stream G aggregates
  driver_markers.csv        Stream H
  aero_frames.csv           Stream I (aggregate aerodynamics + atmosphere)
  aero_surfaces.csv         Stream J (row per aerodynamic surface)
  phase_vector_global.csv   Stream K (coupling, reserve and real-thruster totals)
  phase_vector_rings.csv    Stream L (per-ring coupling and complete gimbal response)
  phase_vector_events.csv   Phase coupling / reserve transition events
  session_compact.json      same content as the .hoverdrive.json
  <name>.hoverdrive.json    single-file upload format
  active_craft_config.json  full craft config at record time (hash in metadata)
  recorder_log.txt          warnings, missing signals, marker log, overhead
```

All CSVs: invariant culture, `.` decimal separator, 1/0 booleans, empty cell = NaN/unavailable. Units: SI (m, m/s, m/s², N, s); angles deg in files; `sectionIndex -1` and negative distances = Unknown.

## Stream A — core_frames.csv (key columns)

`sample,time_s` — sample index and time since recording start. `speed_ms/speed_kmh`, local velocity split `velFwd/velLat/velVert`, derived `accel*/accelG/jerk` (labeled derived: finite differences at the sample rate), `yawRate_degs`, derived `yawAccel_rads2`, attitude `roll/pitch/slip/upVsUp`. Ground: `grounded, groundedFactor, groundedNodes, minNodeDist_m, inverted`. Chassis: `clearance_m` (lowest-collider-point estimate = min node ground distance − node-to-collider-bottom offset, logged at start), `clearanceErr_m`, `nearBottom`, `chassisContact` (real collision on bottom region). Inputs raw + intent. Steering safety: `steerStability, steerTilt01, steerRollRate01`. Hover aggregate: `hoverReqCmd/hoverFinalCmd` (sum of 4 corner commands, continuous-throttle units where 1 = continuous Max Force), `hoverForceN, hoverSatNodes, hoverOverdrive, hoverPower01`. Aero: `dragN, wingN, groundEffectN, downforceG`. Power: `powerReq/powerGranted/powerBudget/overload01`. Track: `section, arc_m, centerDist_m`.

## Stream B — hover_nodes.csv

`node`: 0=FL 1=FR 2=RL 3=RR. Contribution columns (`gravity, curvature, spring, damping, oscillation, attitude`) are the owning system's own pre-clamp breakdown (HoverStabilizerArray.CornerDebug — not recomputed). `preClamp → finalCmd` shows the clamp; `ceiling` is the tick's hardware ceiling (1 = continuous, >1 = overdrive unlocked); `appliedThrottle/forceN` the hardware result; `reqPower/grantedPower/power01` the energy result. `saturated` = applied ≥ 98% of min(ceiling, 1).

## Stream C — thrusters.csv

First line is a comment mapping `thruster` index → label (full identity incl. continuous/overdrive/gains lives in session_metadata.thrusters). Columns show request → bus-final → hardware-applied throttle, applied force, power request/grant, ceiling, saturation.

## Streams K-L — Phase-Vector Rings

`phase_vector_global.csv` records valid coupled nodes, average surface quality, Flight Reserve level/recharge/drain, phase-coupling support, real-thruster support, capture state, assist state and surface class.

`phase_vector_rings.csv` records each ring's surface hit and coupling-force breakdown plus inner-thruster assist/manual/automatic output. Schema 1.4 adds requested and applied force vectors, target and actual gimbal directions in both world and mount-local coordinates, gimbal deflection, target slew error, directional agreement, reserve output scale, hemisphere-limit state and force-limit state. `session_metadata.phaseVector` contains the controller and per-ring tuning used by the run.

## Stream E — events.csv

`type`: NearBottoming, ConfirmedBottoming, ProbeLostSurface, ContinuousSaturation, EmergencyOverdrive, HoverPowerStarvation, HardLanding, UnexpectedLaunch, NumbSteering, SteeringSaturation, HighSlipAngle, Spin, GripBreakerActivated, StabilizerToggled, MainSaturation, OverchargeBurst, TotalPowerSaturation, Takeoff, Landing, InvertedAirborne, Scrape, WallImpact, ChassisImpact, HighEnergyCollision, Respawn, DroppedSamples, InvalidTelemetry.
`evidence` is a FLAGS set (comma-joined): ContinuousForceReached, EmergencyForceReached, HoverMaxCommandReached, PowerStarvation, ProbeLostSurface, ExcessiveDownforce, RoofForceOpposing, CurvatureDemand, LandingSpeedExceededCapacity, TiltSafety, RollRateSafety, YawDamping, ThrusterSaturation, EnergyLimiting, TractionOpposition, LostSurfaceContact, NodeAsymmetry, Unknown. Multiple flags may be true; no single root cause is forced. `startSample/endSample` link into Stream A.

## .hoverdrive.json (single-file upload)

```
format: "hoverdrive"
recorderSchemaVersion
metadata { session, environment, craft (incl. configHash), track, input, thrusters[] }
summary { duration, distance, speeds, grounded/airborne/inverted time, clearance stats,
          bottoming/numb/landing/spin/collision counts, overdrive/saturation/starvation time,
          power use, droppedSamples, recorderOverheadMsPerSample }
constraintRanking [ evidence statements, ranked ]
speedBands [ per 200 km/h band: steering input/response, slip, hover cmd, clearance,
             downforce, power, saturation/numb time, bottoming, collisions ]
events [ compact event list with evidence ]
markers [ driver markers ]
sections [ per-section aggregates ]
decimatedFrames [ ~5 Hz: t, kmh, clr, hovCmd, adhMs2, adhLimited, airStab,
                  dfN, steer, yawDs, pw, gnd, sec ]
configHash
```

## Event thresholds

Defaults per spec (warning clearance 0.20 m, critical 0.05 m, continuous-saturation 90%, emergency 95% + 0.10 s, numb steering input > 0.60 / speed > 250 km/h / 0.30 s / yaw-accel-per-input < 0.35 rad/s², slip 25/45°, hard landing 6/12 m/s, collision 5/15 m/s) — all editable on the recorder's Thresholds block and recorded per session.

## Missing-signal policy

Signals that don't exist are recorded as Unknown (−1 / empty) and listed in section 20 of the report + recorder_log.txt. The recorder never re-derives system formulas; every force/command breakdown comes from read-only snapshots owned by the source system (HoverStabilizerArray.CornerDebug, VectorThrusterArray.LastSteering, TractionCore.LastGrip, EnergyCore.CurrentEnergy, CraftAerodynamics.Last*, ThrusterNode.AppliedThrottle/CurrentThrottleCeiling).
