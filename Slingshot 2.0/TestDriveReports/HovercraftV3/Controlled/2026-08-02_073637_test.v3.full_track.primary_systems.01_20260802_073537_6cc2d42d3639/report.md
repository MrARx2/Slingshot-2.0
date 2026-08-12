# Hovercraft V3 Diagnostic Run Report

## Executive summary

Captured 6027 synchronized physics samples over 60.27 seconds. Data quality: Nominal. Evidence includes 274 unexpected-airborne samples, 197 world/belief disagreement samples, and 0 power-limited samples. These counts identify review windows, not causes.

No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.

## Session identity

Session: `6cc2d42d3639`  
Profile: `ForensicCompact`

## Craft configuration

Build: `build.hovercraft.baseline.01`  
Mass: 11619.5 kg  
Hover configuration: `hover.hovercraft.baseline.01` v3  
Target / minimum / maximum range: 8 / 1 / 80 m  
Hover strength / damping: 0.45 / 0.12; gravity compensation: enabled x1  
Compression / extension limits: 0.3 / 0.12 m.  
Surface curvature feed-forward / response / cap: 1.1 / 12 / 340 m/s2.  
Roof capture strength / damping / cap / deadband: 7 / 5 / 180 m/s2 / 0.35 m.

## World configuration

Scene: `V3_TrackTest`  
World root: `world.root.v3_tracktest`  
Profile: `Earth Standard` (`world.earth_standard`)  
Gravity: (0.00, -9.81, 0.00) m/s2  
Sea-level atmosphere: 15.0 C, 101325 Pa, 1.225 kg/m3  
Fixed timestep: 0.01 s; environment zones: 3.

## Track overview

See `track_report.json` and `track_sections.csv`.

## Data quality

Nominal. Dropped samples: 0; dropped events: 0.

## Driving attempt summary

Resets create a new attempt. Teleport discontinuity samples are retained in raw data but excluded from dynamics aggregates.

| Attempt | Samples | Duration (s) | Max distance (m) | Mean speed (m/s) | Max speed (m/s) | Min ride height (m) | Events |
|---:|---:|---:|---:|---:|---:|---:|---:|
|0|5851|58.5|9326.842|161.198|193.679|0.024|181|
|1|176|1.75|61.776|34.872|65.515|7.673|6|

## Lap summary

| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |
|---:|---:|---:|---:|---:|---:|
|0|6025|60.25|157.556|193.679|187|

## Section summary

| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |
|---|---:|---:|---:|---:|---:|
|section_000_road_0|834|97.582|0.156|7.523|16|
|section_001_road_0|2611|160.209|2.674|7.238|85|
|section_002_road_0|843|170.416|10.148|7.502|34|
|section_003_road_0|333|170.392|6.951|7|17|
|section_004_road_0|423|170.412|0.14|7.507|8|
|section_005_road_0|149|170.411|0.006|8.015|3|
|section_006_road_0|241|172.869|1.115|0.024|7|
|section_007_road_0|317|181.323|16.42|24.503|6|
|section_008_road_0|274|191.847|38.364|189.532|10|

## Event timeline

| Event type | Count | Max severity |
|---|---:|---:|
|ManualMarker|2|0|
|SampleRateDrop|1|1|
|TaskUnderMinimum|2|2|
|PowerStarvation|1|2|
|SurfaceReacquired|2|0|
|ThrusterSaturation|45|1|
|SpringTravelLimit|62|1|
|SectionExit|8|0|
|SectionEntry|8|0|
|SensorWorldDisagreement|37|1|
|SensorMiss|4|1|
|NearSurfaceExit|1|0|
|SurfaceEnvelopeDeparture|1|0|
|ProbeLoss|1|1|
|BottomingOut|1|2|
|FreeFlightEntered|1|0|
|Rollover|3|3|
|UnexpectedAirborne|3|2|
|Spin|2|2|
|Reset|1|0|
|MainframeDegraded|1|2|

## World truth versus craft belief

197 samples disagreed on grounded/contact classification.

## Sensor accuracy

| Sensor | Samples | Hits | Comparable | Mean abs distance error (m) | Mean normal error (deg) | Misses |
|---|---:|---:|---:|---:|---:|---:|
|sensor.chassis.front|6025|0|0|0|0|0|
|sensor.chassis.rear|6025|0|0|0|0|0|
|sensor.chassis.left|6025|5103|5094|0.068|0.251|5|
|sensor.chassis.right|6025|5195|5193|0.068|0.218|0|
|sensor.chassis.top|6025|44|43|0.639|0.131|1|
|sensor.chassis.bottom|6025|5194|5193|0.005|0.115|0|
|sensor_6|2|0|2|0|0|0|
|sensor_7|2|0|2|0|0|0|
|sensor_8|2|0|2|0|0|0|
|sensor_9|2|0|2|0|0|0|
|Hover.Front.Left.Bottom|6023|5193|6021|0|0|0|
|Hover.Front.Right.Bottom|6023|5193|6021|0|0|0|
|Hover.Rear.Left.Bottom|6023|5194|6023|0|0|0|
|Hover.Rear.Right.Bottom|6023|5194|6023|0|0|0|

## System execution

| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |
|---|---|---:|---:|---:|---:|---:|---:|
|software.pilotinterface.stock|PilotInterface|120|120|116.013|0|200|0|
|software.hover.stock|Hover|120|120|116.013|0|200|0|
|software.stabilizer.stock|Stabilizer|120|120|116.013|0|200|0|
|software.drive.stock|Drive|60|60|57.998|0|200|0|
|software.traction.stock|Traction|120|120|116.013|0|200|0|
|software.aerodynamics.stock|Aerodynamics|60|60|57.998|0|200|0|
|software.telemetry.stock|Telemetry|30|30|28.998|0|200|0|

## Router and actuator behavior

| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Propulsion.Rear.Center:connector.propulsion.fixed.baseline.01|Part|0|0|1|0|19.998|0|0|
|Propulsion.Rear.Center:thruster.main.balanced.01|Thruster|0.184|0.184|1|540000.1|22.256|0|0|
|Braking.Front.Center:thruster.brake.balanced.01|Thruster|0.015|0.015|1|589342.5|19.998|0|0|
|Hover.Front.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.08|0.478|1|0|0|636|0|
|Hover.Front.Left.Bottom:thruster.hover.balanced.01|Thruster|0.6|0.6|1|1120000|19.998|0|0|
|Hover.Front.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.08|0.488|1|0|0|635|0|
|Hover.Front.Right.Bottom:thruster.hover.balanced.01|Thruster|0.604|0.604|1|1120000|19.998|0|0|
|Hover.Rear.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.084|0.479|1|0|0|1125|0|
|Hover.Rear.Left.Bottom:thruster.hover.balanced.01|Thruster|0.633|0.633|1|1119692|19.998|0|0|
|Hover.Rear.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.085|0.498|1|0|0|942|0|
|Hover.Rear.Right.Bottom:thruster.hover.balanced.01|Thruster|0.635|0.635|1|1120000|19.998|0|0|
|Control.Front.Left.Top:thruster.roof.balanced.01|Thruster|0.182|0.17|1|530425.4|19.998|0|0|
|Control.Front.Right.Top:thruster.roof.balanced.01|Thruster|0.127|0.119|1|515872.4|19.998|0|0|
|Control.Rear.Left.Top:thruster.roof.balanced.01|Thruster|0.204|0.195|1|486893.8|19.998|0|0|
|Control.Rear.Right.Top:thruster.roof.balanced.01|Thruster|0.185|0.177|1|465498.2|19.998|0|0|
|Strafe.Left.Front:thruster.strafe.balanced.01|Thruster|0.071|0.071|1|375826.4|19.998|0|0|
|Strafe.Right.Front:thruster.strafe.balanced.01|Thruster|0.034|0.034|1|138607|19.998|0|0|
|Strafe.Left.Rear:thruster.strafe.balanced.01|Thruster|0.058|0.058|1|300549.7|19.998|0|0|
|Strafe.Right.Rear:thruster.strafe.balanced.01|Thruster|0.02|0.02|1|105821.2|19.998|0|0|
|Core.Main:energycore.main.balanced.01|Part|0.031|0.031|1|0|19.998|0|0|
|Cockpit.Main:cockpit.main.balanced.01|Part|0|0|1|0|19.998|0|0|
|System.PilotInterface:computer.pilotinterface.balanced.01|Part|120|120|1|0|0|0|0|
|System.Drive:computer.drive.balanced.01|Part|60|60|1|0|0|0|0|
|System.Hover:computer.hover.balanced.01|Part|120|120|1|0|0|0|0|
|System.Stabilizer:computer.stabilizer.balanced.01|Part|120|120|1|0|0|0|0|
|System.Traction:computer.traction.balanced.01|Part|120|120|1|0|0|0|0|
|System.Aerodynamics:computer.aerodynamics.balanced.01|Part|60|60|1|0|0|0|0|
|System.Telemetry:computer.telemetry.balanced.01|Part|30|30|1|0|0|0|0|
|Aero.Front.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.386|1|1|0|0|0|0|
|Aero.Front.Left:fin.aero.balanced.01|AerodynamicFin|157.475|6848.316|1|17166.11|0|0|0|
|Aero.Front.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.387|1|1|0|0|0|0|
|Aero.Front.Right:fin.aero.balanced.01|AerodynamicFin|157.358|6767.913|1|17510.08|0|0|0|
|Aero.Rear.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.386|1|1|0|0|0|0|
|Aero.Rear.Left:fin.aero.balanced.01|AerodynamicFin|157.54|7075.855|1|17687.33|0|0|0|
|Aero.Rear.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.387|1|1|0|0|0|0|
|Aero.Rear.Right:fin.aero.balanced.01|AerodynamicFin|157.416|6994.045|1|17590.25|0|0|0|
|Cooling.Main:cooling.active.balanced.01|CoolingModule|0|0|1|0|0|0|0|

## Force and torque analysis

| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |
|---|---:|---:|---:|
|gravity|113944.6|113948.4|-113944.6|
|propulsion|88336.61|540000.1|11891.38|
|braking|24670.4|589342.5|6204.563|
|hover|395533|2931392|183446.8|
|roof|105741.2|1997443|-66065.23|
|lateral|84353.97|489941.4|-19354.94|
|aerodynamic|28823.87|69062.4|-16769.98|
|contact|0|0|0|
|expected_net|416583.7|2748147|-14595.41|
|observed_net|416583.8|2748148|-14595.38|
|residual|4.409|9.778|0.005|

Maximum residual force: 9.778 N; maximum residual torque: 435054 Nm.
Contact-free residual (valid dynamics only): mean 4.409 N; p95 8.471 N; max 9.778 N across 6025 samples.

## Power and thermal analysis

0 samples were power-limited; 0 per-device samples were thermally derated.

## Atmosphere and aerodynamics

Invalid world samples: 0; samples inside zones: 49. Density range: 1.102 to 1.225 kg/m3. Temperature range: 7.9 to 15.0 C. Maximum Mach: 0.574; maximum dynamic pressure: 23333 Pa.

## Airborne/contact analysis

274 samples were classified unexpected-airborne.

## Repeated issue locations

| Section | Distance window (m) | Events | Max severity | Types |
|---|---:|---:|---:|---|
|section_000_road_0|0–10|7|2|MainframeDegraded, PowerStarvation, SampleRateDrop, SurfaceReacquired, TaskUnderMinimum|
|section_000_road_0|30–40|2|1|ThrusterSaturation|
|section_000_road_0|670–680|2|1|SpringTravelLimit, ThrusterSaturation|
|section_001_road_0|720–730|2|0|SectionEntry, SectionExit|
|section_001_road_0|800–810|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_001_road_0|890–900|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_001_road_0|1090–1100|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_001_road_0|1430–1440|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_001_road_0|1720–1730|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_001_road_0|1800–1810|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_001_road_0|1920–1930|2|1|SensorWorldDisagreement, ThrusterSaturation|
|section_001_road_0|2260–2270|2|1|SpringTravelLimit, ThrusterSaturation|
|section_001_road_0|4100–4110|3|1|SensorWorldDisagreement, SpringTravelLimit, ThrusterSaturation|
|section_001_road_0|4780–4790|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_002_road_0|4950–4960|2|0|SectionEntry, SectionExit|
|section_002_road_0|4960–4970|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_002_road_0|5180–5190|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_002_road_0|5310–5320|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_002_road_0|5410–5420|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_002_road_0|5640–5650|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_002_road_0|5740–5750|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_003_road_0|6410–6420|2|0|SectionEntry, SectionExit|
|section_003_road_0|6460–6470|3|1|SensorWorldDisagreement, SpringTravelLimit, ThrusterSaturation|
|section_003_road_0|6560–6570|3|1|SensorWorldDisagreement, SpringTravelLimit, ThrusterSaturation|
|section_003_road_0|6740–6750|2|1|SensorWorldDisagreement, SpringTravelLimit|
|section_004_road_0|6910–6920|2|0|SectionEntry, SectionExit|
|section_005_road_0|7630–7640|2|0|SectionEntry, SectionExit|
|section_006_road_0|7890–7900|6|1|NearSurfaceExit, ProbeLoss, SectionEntry, SectionExit, SurfaceEnvelopeDeparture, ThrusterSaturation|
|section_007_road_0|8300–8310|2|0|SectionEntry, SectionExit|
|section_007_road_0|8310–8320|2|1|SensorMiss, SensorWorldDisagreement|
|section_008_road_0|8850–8860|3|2|SectionEntry, SectionExit, UnexpectedAirborne|

## Evidence tables

The tables below are derived from synchronized samples; raw records remain authoritative.

## Potential investigation areas

- Review world-contact versus hover-belief disagreement samples and their sensor ages.
- Review high force-residual windows for unmodeled contacts or incomplete force attribution.
- Review unexpected-airborne locations against track surface truth and hover probes.

## Raw-data files

`channel_schema.json`, `events.json`, `event_context.ndjson`, `track_report.json`, `samples_compact.bin`, `samples_compact.bin.crc32`, `samples_core.csv`, `device_samples.csv`, `attempt_summary.csv`.

