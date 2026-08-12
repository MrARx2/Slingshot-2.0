# Hovercraft V3 Diagnostic Run Report

## Executive summary

Captured 427 synchronized physics samples over 4.27 seconds. Data quality: Nominal. Evidence includes 0 unexpected-airborne samples, 425 world/belief disagreement samples, and 0 power-limited samples. These counts identify review windows, not causes.

No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.

## Session identity

Session: `1963a5dcaaa6`  
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

Scene: `V3_ControlledDynamics`  
World root: `world.root.v3_controlleddynamics`  
Profile: `Earth Standard` (`world.earth_standard`)  
Gravity: (0.00, -9.81, 0.00) m/s2  
Sea-level atmosphere: 15.0 C, 101325 Pa, 1.225 kg/m3  
Fixed timestep: 0.01 s; environment zones: 0.

## Track overview

See `track_report.json` and `track_sections.csv`.

## Data quality

Nominal. Dropped samples: 0; dropped events: 0.

## Driving attempt summary

Resets create a new attempt. Teleport discontinuity samples are retained in raw data but excluded from dynamics aggregates.

| Attempt | Samples | Duration (s) | Max distance (m) | Mean speed (m/s) | Max speed (m/s) | Min ride height (m) | Events |
|---:|---:|---:|---:|---:|---:|---:|---:|
|0|427|4.26|0|21.759|35|0|9|

## Lap summary

| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |
|---:|---:|---:|---:|---:|---:|
|0|426|4.25|21.727|35|9|

## Section summary

| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |
|---|---:|---:|---:|---:|---:|
|unmapped|426|21.727|0|0|0|

## Event timeline

| Event type | Count | Max severity |
|---|---:|---:|
|ManualMarker|2|0|
|SampleRateDrop|1|1|
|SensorWorldDisagreement|1|1|
|TaskUnderMinimum|1|2|
|PowerStarvation|1|2|
|SurfaceReacquired|1|0|
|ThrusterSaturation|1|1|
|SpringTravelLimit|1|1|

## World truth versus craft belief

425 samples disagreed on grounded/contact classification.

## Sensor accuracy

| Sensor | Samples | Hits | Comparable | Mean abs distance error (m) | Mean normal error (deg) | Misses |
|---|---:|---:|---:|---:|---:|---:|
|sensor.chassis.front|426|0|0|0|0|0|
|sensor.chassis.rear|426|0|0|0|0|0|
|sensor.chassis.left|426|0|0|0|0|0|
|sensor.chassis.right|426|0|0|0|0|0|
|sensor.chassis.top|426|0|0|0|0|0|
|sensor.chassis.bottom|426|426|426|0.011|0|0|
|sensor_6|1|0|1|0|0|0|
|sensor_7|1|0|1|0|0|0|
|sensor_8|1|0|1|0|0|0|
|sensor_9|1|0|1|0|0|0|
|Hover.Front.Left.Bottom|425|425|425|0|0|0|
|Hover.Front.Right.Bottom|425|425|425|0|0|0|
|Hover.Rear.Left.Bottom|425|425|425|0|0|0|
|Hover.Rear.Right.Bottom|425|425|425|0|0|0|

## System execution

| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |
|---|---|---:|---:|---:|---:|---:|---:|
|software.pilotinterface.stock|PilotInterface|120|120|91.68|0|100|0|
|software.hover.stock|Hover|120|120|91.68|0|100|0|
|software.stabilizer.stock|Stabilizer|120|120|91.68|0|100|0|
|software.drive.stock|Drive|60|60|45.749|0|100|0|
|software.traction.stock|Traction|120|120|91.68|0|100|0|
|software.aerodynamics.stock|Aerodynamics|60|60|45.749|0|100|0|
|software.telemetry.stock|Telemetry|30|30|22.784|0|100|0|

## Router and actuator behavior

| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Propulsion.Rear.Center:connector.propulsion.fixed.baseline.01|Part|0|0|1|0|19.997|0|0|
|Propulsion.Rear.Center:thruster.main.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Braking.Front.Center:thruster.brake.balanced.01|Thruster|0.044|0.044|1|120555.9|19.997|0|0|
|Hover.Front.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.034|0.224|1|0|0|0|0|
|Hover.Front.Left.Bottom:thruster.hover.balanced.01|Thruster|0.253|0.253|1|226717.1|19.997|0|0|
|Hover.Front.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.034|0.223|1|0|0|0|0|
|Hover.Front.Right.Bottom:thruster.hover.balanced.01|Thruster|0.252|0.252|1|226717.1|19.997|0|0|
|Hover.Rear.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.039|0.25|1|0|0|9|0|
|Hover.Rear.Left.Bottom:thruster.hover.balanced.01|Thruster|0.292|0.292|1|374425.4|19.997|0|0|
|Hover.Rear.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.037|0.239|1|0|0|7|0|
|Hover.Rear.Right.Bottom:thruster.hover.balanced.01|Thruster|0.275|0.275|1|320594.5|19.997|0|0|
|Control.Front.Left.Top:thruster.roof.balanced.01|Thruster|0.133|0.131|1|184048.5|19.997|0|0|
|Control.Front.Right.Top:thruster.roof.balanced.01|Thruster|0.113|0.112|1|132930.2|19.997|0|0|
|Control.Rear.Left.Top:thruster.roof.balanced.01|Thruster|0.112|0.111|1|129351.3|19.997|0|0|
|Control.Rear.Right.Top:thruster.roof.balanced.01|Thruster|0.113|0.112|1|132983|19.997|0|0|
|Strafe.Left.Front:thruster.strafe.balanced.01|Thruster|0|0|1|169.078|19.997|0|0|
|Strafe.Right.Front:thruster.strafe.balanced.01|Thruster|0|0|1|313.315|19.997|0|0|
|Strafe.Left.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|289.485|19.997|0|0|
|Strafe.Right.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|273.099|19.997|0|0|
|Core.Main:energycore.main.balanced.01|Part|0.031|0.031|1|0|19.997|0|0|
|Cockpit.Main:cockpit.main.balanced.01|Part|0|0|1|0|19.997|0|0|
|System.PilotInterface:computer.pilotinterface.balanced.01|Part|120|120|1|0|0|0|0|
|System.Drive:computer.drive.balanced.01|Part|60|60|1|0|0|0|0|
|System.Hover:computer.hover.balanced.01|Part|120|120|1|0|0|0|0|
|System.Stabilizer:computer.stabilizer.balanced.01|Part|120|120|1|0|0|0|0|
|System.Traction:computer.traction.balanced.01|Part|120|120|1|0|0|0|0|
|System.Aerodynamics:computer.aerodynamics.balanced.01|Part|60|60|1|0|0|0|0|
|System.Telemetry:computer.telemetry.balanced.01|Part|30|30|1|0|0|0|0|
|Aero.Front.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Front.Left:fin.aero.balanced.01|AerodynamicFin|21.797|51.546|1|371.166|0|0|0|
|Aero.Front.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Front.Right:fin.aero.balanced.01|AerodynamicFin|21.797|51.574|1|372.374|0|0|0|
|Aero.Rear.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Rear.Left:fin.aero.balanced.01|AerodynamicFin|21.83|68.081|1|538.244|0|0|0|
|Aero.Rear.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Rear.Right:fin.aero.balanced.01|AerodynamicFin|21.83|67.999|1|538.615|0|0|0|
|Cooling.Main:cooling.active.balanced.01|CoolingModule|0|0|1|0|0|0|0|

## Force and torque analysis

| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |
|---|---:|---:|---:|
|gravity|113948.1|113948.4|-113948.1|
|propulsion|0|0|0|
|braking|74524.13|120555.9|11821.55|
|hover|171469.2|1100319|169474.4|
|roof|74741.41|574187.9|-73967.71|
|lateral|67.605|567.816|0.023|
|aerodynamic|264.435|1676.7|201.771|
|contact|0|0|0|
|expected_net|70421.33|952900.9|-6418.564|
|observed_net|70421.32|952900.7|-6418.566|
|residual|0.53|2.172|0.003|

Maximum residual force: 2.172 N; maximum residual torque: 6311.595 Nm.
Contact-free residual (valid dynamics only): mean 0.53 N; p95 1.074 N; max 2.172 N across 426 samples.

## Power and thermal analysis

0 samples were power-limited; 0 per-device samples were thermally derated.

## Atmosphere and aerodynamics

Invalid world samples: 0; samples inside zones: 0. Density range: 1.224 to 1.225 kg/m3. Temperature range: 14.9 to 15.0 C. Maximum Mach: 0.103; maximum dynamic pressure: 750 Pa.

## Airborne/contact analysis

0 samples were classified unexpected-airborne.

## Repeated issue locations

| Section | Distance window (m) | Events | Max severity | Types |
|---|---:|---:|---:|---|
|unmapped|0–10|9|2|ManualMarker, PowerStarvation, SampleRateDrop, SensorWorldDisagreement, SpringTravelLimit, SurfaceReacquired, TaskUnderMinimum, ThrusterSaturation|

## Evidence tables

The tables below are derived from synchronized samples; raw records remain authoritative.

## Potential investigation areas

- Review world-contact versus hover-belief disagreement samples and their sensor ages.
- Review high force-residual windows for unmodeled contacts or incomplete force attribution.

## Raw-data files

`channel_schema.json`, `events.json`, `event_context.ndjson`, `track_report.json`, `samples_compact.bin`, `samples_compact.bin.crc32`, `samples_core.csv`, `device_samples.csv`, `attempt_summary.csv`.

