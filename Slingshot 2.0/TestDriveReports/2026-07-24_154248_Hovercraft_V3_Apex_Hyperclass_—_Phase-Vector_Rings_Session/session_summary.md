# Hovercraft Test Drive Report

## 1. Session Identity
- Session: 82a17ffbeb72 — 
- Start: 2026-07-24 12:42:48 UTC, duration 101.2 s, mode StandardTestDrive
- Schema 1.4, Unity 6000.5.0f1, fixed dt 0.01 s
- Recorder overhead: 0.019 ms per sampled step; dropped samples: 0

## 2. Craft Configuration
- Hovercraft V3 Apex Hyperclass — Phase-Vector Rings (ApexHyperclass), mass 11000 kg, config hash `496ecee193f16660`
- Downward assist 0 m/s², air density 1.225
- Full config: `active_craft_config.json`

## 3. Track and Test Conditions
- Scene: SampleScene; track context: section index / arc length / centerline offset recorded
- Tester notes: 

## 4. Executive Summary
- 101.2 s, 26.57 km, max 1476 km/h (avg 946), grounded 59.9 s / airborne 41.3 s / inverted 7.6 s
- Bottoming: 10 confirmed (+16 near); hard landings 2, spins 0, respawns 2
- Impacts: 10 distinct (6 chassis / 2 wall / 2 high-energy); contact intervals 198 (17 scrapes)
- Steering: 1 numb events (0.38 s) + 0 contact-constrained events (chassis was scraping — not free-running steering weakness)
- Clearance: min free 0.004 m, max penetration -0.365 m, near-bottoming 0.52 s, confirmed-bottoming 20.74 s
- Legacy hover-command metrics are not applicable to this Phase-Vector craft; authoritative coupling/thruster totals are reported in the Phase-Vector section.
- Legacy overdrive metrics (Rev4 semantics — any command above 1; NOT valid for Apex band interpretation, see the operating-band section): continuous-saturated 0.00 s, emergency-saturated 0.00 s, overdrive 0.00 s
- Power: reactor overload 0.00 s; hover power-limited 0.00 s; vectoring power-limited 0.00 s (overload ≠ hover starvation)
- Acceleration by class (Rev1.1 — classified before aggregation, nothing deleted): valid free-driving 39.9 g | grounded non-contact 40.0 g | chassis scrape 377.4 g | collision 211.9 g | landing 115.5 g | respawn/discontinuity 1533.8 g | unknown 109.6 g
- Legacy acceleration metric (pre-classification, contact-contaminated — NOT valid continuous driving): 115.5 g

## 5. Driver Notes and Manual Markers
- none

## 6. Key Problems Detected / 19. Suspected Tuning Constraints
1. Bottoming/near-bottoming occurred 26x; emergency hover capacity was reached in 0% of them, power starvation in 0%, excessive downforce flagged in 96%, roof-force opposition in 0%.
2. Numb steering occurred 1x (0.4s total); tilt safety flagged in 0%, thruster saturation in 0%, energy limiting in 0%, yaw damping in 100%.
3. Hover power limitation was not observed.

## 7-8. Hover Performance & Bottoming (by speed band)

| Speed Band | Samples | Avg Clearance | Min Clearance | Avg Cmd | Saturated s | Bottoming | Avg Downforce N |
|---|---:|---:|---:|---:|---:|---:|---:|
| 0-200 | 287 | 2.40 | 2.10 | 0.00 | 0.00 | 0 | 1122 |
| 200-400 | 137 | 2.37 | 2.34 | 0.00 | 0.00 | 0 | 17724 |
| 400-600 | 251 | 2.38 | 2.30 | 0.00 | 0.00 | 1 | 28316 |
| 600-800 | 457 | 2.06 | -0.36 | 0.00 | 0.00 | 0 | 52539 |
| 800-1000 | 1019 | 1.58 | -0.36 | 0.00 | 0.00 | 2 | 76756 |
| 1000-1200 | 2128 | 0.41 | -0.36 | 0.00 | 0.00 | 4 | 154150 |
| 1200+ | 783 | 1.43 | -0.36 | 0.00 | 0.00 | 3 | 86326 |

## 9-10. Steering Performance (by speed band)

| Speed Band | Samples | Avg Input | Yaw Accel per Input rad/s² | Avg Slip ° | Numb s | Collisions |
|---|---:|---:|---:|---:|---:|---:|
| 0-200 | 287 | 0.34 | 9.961 | 0.8 | 0.00 | 0 |
| 200-400 | 137 | 0.24 | 12.034 | 0.7 | 0.00 | 0 |
| 400-600 | 251 | 0.21 | 2.599 | 7.3 | 0.00 | 1 |
| 600-800 | 457 | 0.32 | 11.262 | 13.5 | 0.00 | 1 |
| 800-1000 | 1019 | 0.50 | 9.685 | 12.6 | 0.00 | 2 |
| 1000-1200 | 2128 | 0.54 | 11.317 | 11.5 | 0.38 | 2 |
| 1200+ | 783 | 0.52 | 11.893 | 18.2 | 0.00 | 4 |

## Steering Limiter Breakdown

| Limiter | Time Active s |
|---|---:|
| Tilt safety | 0.32 |
| Roll-rate safety | 0.18 |
| Energy scaling | 0.00 |
| Thruster saturation | 0.00 |
| Traction clamp | 0.00 |

## Phase-Vector Ring Performance
- Coupled 62.80 s; airborne inner-thruster output 32.22 s.
- Flight Reserve minimum 0.0%, ending 55.8%, depleted 6.18 s.
- Average phase force 509,715 N; average real-thruster force 328,465 N.
- Per-ring limit samples: gimbal hemisphere 1890, force/slew/reserve limited 8994. See `phase_vector_rings.csv` for request, target, actual and applied vectors in world and mount-local coordinates.

## Load Budget & Operating Bands (Continuous/Performance/Extreme/Emergency/Saturated scheme)
- Capacities: continuous 640,000 N / high-load 1,280,000 N / emergency 4,480,000 N
- Band time: Continuous 101.2 s | Performance 0.0 s | Extreme 0.0 s | Emergency 0.0 s | Saturated 0.0 s
- Longest single interval: Continuous 101.23 s | Performance 0.00 s | Extreme 0.00 s | Emergency 0.00 s | Saturated 0.00 s
- Avg / max speed by band: Continuous 946/1476 km/h | Performance 0/0 km/h | Extreme 0/0 km/h | Emergency 0/0 km/h | Saturated 0/0 km/h
- Avg clearance by band: Continuous 1.22 m | Performance n/a | Extreme n/a | Emergency n/a | Saturated n/a
- Emergency share of grounded time: 0% (target < 35%); saturated share 0% (target < 15%)
- Per-section budget: section_load_budget.csv; compatibility classes + recommended speeds: section_compatibility.csv (diagnostics, not caps)

## 13-14. Aerodynamic Loads & Power
- Peak downforce 330553 N; avg power 520 / peak 1159; reactor overloaded 0.00 s (hover-limited 0.00 s, vectoring-limited 0.00 s)

## 15. Collision and Scrape Summary
- Contact intervals 198 (17 scrapes); distinct impact events 10. See collisions.csv.

## 17. Performance by Track Section
- See track_sections.csv (12 sections visited)

## 18. Event Timeline
-    4.48s  UnexpectedLaunch (sev 2, 465 km/h, 0.52s) — aero: v=129.1m/s vVert=5.1m/s gravity=107910N hover=0N GE=20416N body=22423N surfaces=0N torque=14660Nm AoA=-2.3deg slip=0.3deg probes=4/4 upVsNormal=2deg fwdVsAirflow=2deg [Unknown]
-    5.34s  UnexpectedLaunch (sev 2, 580 km/h, 1.86s) — aero: v=161.1m/s vVert=5.5m/s gravity=107910N hover=0N GE=31782N body=34919N surfaces=0N torque=22793Nm AoA=-2.0deg slip=0.3deg probes=4/4 upVsNormal=2deg fwdVsAirflow=2deg [Unknown]
-    7.20s  Takeoff (sev 1, 809 km/h) — at 809 km/h | aero: v=224.8m/s vVert=10.0m/s gravity=107910N hover=0N GE=0N body=67899N surfaces=0N torque=44469Nm AoA=-2.5deg slip=-1.8deg probes=4/4 upVsNormal=-1deg fwdVsAirflow=3deg
-    8.20s  Landing (sev 1, 906 km/h) — impact -11.7 m/s | aero: v=251.7m/s vVert=12.0m/s gravity=107910N hover=0N GE=46575N body=84954N surfaces=0N torque=55712Nm AoA=-2.7deg slip=3.1deg probes=4/4 upVsNormal=4deg fwdVsAirflow=4deg
-    8.20s  UnexpectedLaunch (sev 2, 913 km/h, 3.18s) — aero: v=253.7m/s vVert=12.7m/s gravity=107910N hover=0N GE=78815N body=86301N surfaces=0N torque=56641Nm AoA=-2.9deg slip=2.7deg probes=4/4 upVsNormal=4deg fwdVsAirflow=4deg [CollisionRebound, Unknown]
-   10.98s  NearBottoming (sev 2, 1202 km/h, 0.02s) [ExcessiveDownforce]
-   11.00s  ConfirmedBottoming (sev 3, 1204 km/h, 8.02s) [ExcessiveDownforce]
-   11.04s  ChassisImpact (sev 2, 1207 km/h) — TrackCollider_03_02, normal 8.1 m/s, region bottom
-   11.36s  Scrape (sev 1, 1157 km/h) — TrackCollider_03_02, 0.32s, region bottom
-   11.70s  Scrape (sev 1, 1156 km/h) — TrackCollider_03_02, 0.34s, region bottom
-   16.36s  Scrape (sev 1, 1087 km/h) — TrackCollider_03_02, 0.70s, region bottom
-   16.66s  Scrape (sev 1, 1104 km/h) — TrackCollider_03_02, 0.30s, region bottom
-   17.90s  UnexpectedLaunch (sev 2, 1209 km/h, 2.64s) — aero: v=335.9m/s vVert=5.6m/s gravity=107910N hover=0N GE=138218N body=151997N surfaces=0N torque=98896Nm AoA=-1.0deg slip=-0.5deg probes=4/4 upVsNormal=1deg fwdVsAirflow=1deg [CollisionRebound, Unknown]
-   18.40s  Scrape (sev 1, 1172 km/h) — TrackCollider_03_02, 0.41s, region bottom
-   18.76s  Scrape (sev 1, 1116 km/h) — TrackCollider_03_02, 0.36s, region bottom
-   18.78s  ProbeSurfaceChanged (sev 1, 1112 km/h, 0.02s)
-   19.02s  NearBottoming (sev 2, 1114 km/h, 0.04s) [ExcessiveDownforce]
-   20.88s  UnexpectedLaunch (sev 2, 1233 km/h, 0.68s) — aero: v=342.4m/s vVert=7.6m/s gravity=107910N hover=0N GE=143581N body=152321N surfaces=0N torque=109174Nm AoA=-1.3deg slip=-16.4deg probes=4/4 upVsNormal=4deg fwdVsAirflow=16deg [Unknown]
-   22.06s  UnexpectedLaunch (sev 2, 1316 km/h, 0.36s) — aero: v=365.7m/s vVert=9.2m/s gravity=107910N hover=0N GE=161944N body=173619N surfaces=0N torque=122564Nm AoA=-1.5deg slip=-15.6deg probes=4/4 upVsNormal=2deg fwdVsAirflow=16deg [Unknown]
-   22.22s  HighSlipAngle (sev 3, 1278 km/h, 0.84s) [TractionOpposition]
-   22.18s  AeroBroadsidePenalty (sev 2, 1236 km/h, 1.46s) [TractionOpposition]
-   22.30s  ExcessiveSideSlip (sev 3, 1236 km/h, 0.60s) [TractionOpposition]
-   23.80s  NearBottoming (sev 2, 1055 km/h, 0.06s) [ExcessiveDownforce]
-   23.86s  ConfirmedBottoming (sev 3, 1054 km/h, 2.80s) [ExcessiveDownforce]
-   23.88s  UnexpectedLaunch (sev 2, 1052 km/h, 3.02s) — aero: v=292.3m/s vVert=16.6m/s gravity=107910N hover=0N GE=104655N body=111016N surfaces=0N torque=82317Nm AoA=-3.4deg slip=-17.6deg probes=4/4 upVsNormal=19deg fwdVsAirflow=18deg [CollisionRebound, Unknown]
-   25.10s  Scrape (sev 1, 939 km/h) — TrackCollider_03_03, 1.12s, region bottom
-   25.48s  Scrape (sev 1, 918 km/h) — TrackCollider_03_03, 0.25s, region bottom
-   26.38s  Scrape (sev 1, 633 km/h) — TrackCollider_03_03, 0.90s, region bottom
-   26.66s  NearBottoming (sev 2, 652 km/h, 0.02s) [ExcessiveDownforce]
-   26.90s  Takeoff (sev 1, 662 km/h) — at 662 km/h | aero: v=184.0m/s vVert=68.6m/s gravity=107910N hover=0N GE=0N body=41101N surfaces=0N torque=41074Nm AoA=-22.5deg slip=13.8deg probes=4/4 upVsNormal=-1deg fwdVsAirflow=26deg
-   26.96s  ProbeGraceEntered (sev 1, 667 km/h) — Hover_RL: probe missed, holding stale surface (dist 7.00 m)
-   26.96s  ProbeGraceEntered (sev 1, 667 km/h) — Hover_RR: probe missed, holding stale surface (dist 7.00 m)
-   27.00s  ProbeGraceExpired (sev 2, 669 km/h) — Hover_RL: grace window expired — surface lost for real
-   27.00s  ProbeGraceExpired (sev 2, 669 km/h) — Hover_RR: grace window expired — surface lost for real
-   27.06s  ProbeGraceEntered (sev 1, 671 km/h) — Hover_FR: probe missed, holding stale surface (dist 6.96 m)
-   27.10s  ProbeGraceExpired (sev 2, 671 km/h) — Hover_FR: grace window expired — surface lost for real
-   27.14s  ProbeGraceEntered (sev 1, 671 km/h) — Hover_FL: probe missed, holding stale surface (dist 7.03 m)
-   27.18s  ProbeGraceExpired (sev 2, 671 km/h) — Hover_FL: grace window expired — surface lost for real
-   27.04s  AeroBroadsidePenalty (sev 2, 670 km/h, 1.18s) [TractionOpposition]
-   27.46s  HighEnergyCollision (sev 3, 669 km/h) — TrackCollider_03_03, normal 31.6 m/s, region front
-   27.58s  WallImpact (sev 2, 571 km/h) — TrackCollider_03_03, normal 8.3 m/s, region front
-   27.60s  ConfirmedBottoming (sev 3, 555 km/h, 0.02s) [ProbeLostSurface]
-   27.48s  ExcessiveSideSlip (sev 3, 556 km/h, 0.32s) [TractionOpposition]
-   30.14s  AeroBroadsidePenalty (sev 2, 692 km/h, 0.64s) [TractionOpposition]
-   30.34s  ExcessiveSideSlip (sev 3, 697 km/h, 0.38s) [TractionOpposition]
-   31.24s  AeroBroadsidePenalty (sev 2, 739 km/h, 1.74s) [TractionOpposition]
-   35.74s  AeroBroadsidePenalty (sev 2, 947 km/h, 2.02s) [TractionOpposition]
-   39.08s  AeroBroadsidePenalty (sev 2, 980 km/h, 0.76s) [TractionOpposition]
-   39.32s  ExcessiveSideSlip (sev 3, 970 km/h, 0.34s) [TractionOpposition]
-   40.52s  Landing (sev 1, 929 km/h) — impact 138.3 m/s | aero: v=258.1m/s vVert=-127.3m/s gravity=107910N hover=0N GE=48546N body=89534N surfaces=0N torque=17123Nm AoA=29.8deg slip=-7.8deg probes=4/4 upVsNormal=46deg fwdVsAirflow=30deg
-   40.52s  HardLanding (sev 3, 929 km/h) — impact 138.3 m/s [LandingSpeedExceededCapacity]
-   40.56s  NearBottoming (sev 2, 916 km/h, 0.02s) [ExcessiveDownforce]
-   40.58s  ConfirmedBottoming (sev 3, 910 km/h, 0.12s) [ExcessiveDownforce]
-   40.58s  HighEnergyCollision (sev 3, 910 km/h) — TrackCollider_00_00, normal 18.9 m/s, region rear
-   40.60s  ProbeNormalDiscontinuity (sev 2, 889 km/h, 0.02s) [TrackNormalDiscontinuity]
-   40.70s  NearBottoming (sev 2, 845 km/h, 0.04s) [ExcessiveDownforce]
-   40.72s  UnexpectedLaunch (sev 2, 831 km/h, 0.24s) — aero: v=230.9m/s vVert=13.8m/s gravity=107910N hover=0N GE=65289N body=91869N surfaces=0N torque=97462Nm AoA=-4.1deg slip=-32.9deg probes=4/4 upVsNormal=2deg fwdVsAirflow=33deg [CollisionRebound, Unknown]
-   40.64s  HighSlipAngle (sev 2, 828 km/h, 0.54s) [TractionOpposition]
-   40.62s  AeroBroadsidePenalty (sev 2, 807 km/h, 0.72s) [TractionOpposition]
-   41.46s  UnexpectedLaunch (sev 2, 759 km/h, 1.08s) — aero: v=211.0m/s vVert=5.7m/s gravity=107910N hover=0N GE=54512N body=58417N surfaces=0N torque=38721Nm AoA=-1.6deg slip=-10.3deg probes=4/4 upVsNormal=5deg fwdVsAirflow=10deg [Unknown]
- … 137 more, see events.csv

## 20. Data Quality and Missing Signals
- Dropped samples: 0; recorder overhead 0.019 ms/sampled step.

## 21. Exported Files
session_metadata.json, core_frames.csv, hover_nodes.csv, thrusters.csv, extended_systems.csv, aero_frames.csv, aero_surfaces.csv, phase_vector_global.csv, phase_vector_rings.csv, phase_vector_events.csv, events.csv, collisions.csv, track_sections.csv, driver_markers.csv, session_compact.json, *.hoverdrive.json, active_craft_config.json, recorder_log.txt.
- A ZIP bundle is also attempted next to the session folder; ZIP failures are reported in the Unity Console.
