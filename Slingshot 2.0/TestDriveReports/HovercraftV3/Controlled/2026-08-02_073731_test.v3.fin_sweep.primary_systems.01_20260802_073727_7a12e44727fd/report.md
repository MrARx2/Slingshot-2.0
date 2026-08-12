# Hovercraft V3 Diagnostic Run Report

## Executive summary

Captured 327 synchronized physics samples over 3.27 seconds. Data quality: Nominal. Evidence includes 0 unexpected-airborne samples, 0 world/belief disagreement samples, and 0 power-limited samples. These counts identify review windows, not causes.

No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.

## Session identity

Session: `7a12e44727fd`  
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
|0|327|3.26|0|101.304|104.091|0|5|

## Lap summary

| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |
|---:|---:|---:|---:|---:|---:|
|0|326|3.25|101.308|104.091|5|

## Section summary

| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |
|---|---:|---:|---:|---:|---:|
|unmapped|326|101.308|0|0|0|

## Event timeline

| Event type | Count | Max severity |
|---|---:|---:|
|ManualMarker|2|0|
|SampleRateDrop|1|1|
|TaskUnderMinimum|1|2|
|PowerStarvation|1|2|

## World truth versus craft belief

0 samples disagreed on grounded/contact classification.

## Sensor accuracy

| Sensor | Samples | Hits | Comparable | Mean abs distance error (m) | Mean normal error (deg) | Misses |
|---|---:|---:|---:|---:|---:|---:|
|sensor.chassis.front|326|0|0|0|0|0|
|sensor.chassis.rear|326|0|0|0|0|0|
|sensor.chassis.left|326|0|0|0|0|0|
|sensor.chassis.right|326|0|0|0|0|0|
|sensor.chassis.top|326|0|0|0|0|0|
|sensor.chassis.bottom|326|0|0|0|0|0|
|sensor_6|1|0|1|0|0|0|
|sensor_7|1|0|1|0|0|0|
|sensor_8|1|0|1|0|0|0|
|sensor_9|1|0|1|0|0|0|
|Hover.Front.Left.Bottom|325|0|325|0|0|0|
|Hover.Front.Right.Bottom|325|0|325|0|0|0|
|Hover.Rear.Left.Bottom|325|0|325|0|0|0|
|Hover.Rear.Right.Bottom|325|0|325|0|0|0|

## System execution

| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |
|---|---|---:|---:|---:|---:|---:|---:|
|software.pilotinterface.stock|PilotInterface|120|120|83.053|0|100|0|
|software.hover.stock|Hover|120|120|83.053|0|100|0|
|software.stabilizer.stock|Stabilizer|120|120|83.053|0|100|0|
|software.drive.stock|Drive|60|60|41.49|0|100|0|
|software.traction.stock|Traction|120|120|83.053|0|100|0|
|software.aerodynamics.stock|Aerodynamics|60|60|41.49|0|100|0|
|software.telemetry.stock|Telemetry|30|30|20.592|0|100|0|

## Router and actuator behavior

| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Propulsion.Rear.Center:connector.propulsion.fixed.baseline.01|Part|0|0|1|0|19.995|0|0|
|Propulsion.Rear.Center:thruster.main.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Braking.Front.Center:thruster.brake.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Hover.Front.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Front.Left.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Hover.Front.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Front.Right.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Hover.Rear.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Rear.Left.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Hover.Rear.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Rear.Right.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Control.Front.Left.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Control.Front.Right.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Control.Rear.Left.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Control.Rear.Right.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Strafe.Left.Front:thruster.strafe.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Strafe.Right.Front:thruster.strafe.balanced.01|Thruster|0|0|1|4.09|19.995|0|0|
|Strafe.Left.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|4.401|19.995|0|0|
|Strafe.Right.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|0|19.995|0|0|
|Core.Main:energycore.main.balanced.01|Part|0.032|0.032|1|0|19.995|0|0|
|Cockpit.Main:cockpit.main.balanced.01|Part|0|0|1|0|19.995|0|0|
|System.PilotInterface:computer.pilotinterface.balanced.01|Part|120|120|1|0|0|0|0|
|System.Drive:computer.drive.balanced.01|Part|60|60|1|0|0|0|0|
|System.Hover:computer.hover.balanced.01|Part|120|120|1|0|0|0|0|
|System.Stabilizer:computer.stabilizer.balanced.01|Part|120|120|1|0|0|0|0|
|System.Traction:computer.traction.balanced.01|Part|120|120|1|0|0|0|0|
|System.Aerodynamics:computer.aerodynamics.balanced.01|Part|60|60|1|0|0|0|0|
|System.Telemetry:computer.telemetry.balanced.01|Part|30|30|1|0|0|0|0|
|Aero.Front.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.403|1|1|0|0|0|0|
|Aero.Front.Left:fin.aero.balanced.01|AerodynamicFin|101.064|3834.11|1|4473.15|0|0|0|
|Aero.Front.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.403|1|1|0|0|0|0|
|Aero.Front.Right:fin.aero.balanced.01|AerodynamicFin|101.066|3834.352|1|4473.12|0|0|0|
|Aero.Rear.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.403|1|1|0|0|0|0|
|Aero.Rear.Left:fin.aero.balanced.01|AerodynamicFin|101.352|3856.723|1|4480.437|0|0|0|
|Aero.Rear.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.403|1|1|0|0|0|0|
|Aero.Rear.Right:fin.aero.balanced.01|AerodynamicFin|101.355|3856.966|1|4480.622|0|0|0|
|Cooling.Main:cooling.active.balanced.01|CoolingModule|0|0|1|0|0|0|0|

## Force and torque analysis

| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |
|---|---:|---:|---:|
|gravity|113948.1|113948.4|-113948.1|
|propulsion|0|0|0|
|braking|0|0|0|
|hover|0|0|0|
|roof|0|0|0|
|lateral|0.126|0.311|0|
|aerodynamic|15866.21|18338.35|-13302.29|
|contact|0|0|0|
|expected_net|127548.6|131019.1|-127250.7|
|observed_net|127548.7|131018.9|-127250.6|
|residual|2.395|4.48|0.039|

Maximum residual force: 4.48 N; maximum residual torque: 49.398 Nm.
Contact-free residual (valid dynamics only): mean 2.395 N; p95 4.224 N; max 4.48 N across 326 samples.

## Power and thermal analysis

0 samples were power-limited; 0 per-device samples were thermally derated.

## Atmosphere and aerodynamics

Invalid world samples: 0; samples inside zones: 0. Density range: 1.190 to 1.197 kg/m3. Temperature range: 13.1 to 13.4 C. Maximum Mach: 0.306; maximum dynamic pressure: 6453 Pa.

## Airborne/contact analysis

0 samples were classified unexpected-airborne.

## Repeated issue locations

| Section | Distance window (m) | Events | Max severity | Types |
|---|---:|---:|---:|---|
|unmapped|0–10|5|2|ManualMarker, PowerStarvation, SampleRateDrop, TaskUnderMinimum|

## Evidence tables

The tables below are derived from synchronized samples; raw records remain authoritative.

## Potential investigation areas

- Review high force-residual windows for unmodeled contacts or incomplete force attribution.

## Raw-data files

`channel_schema.json`, `events.json`, `event_context.ndjson`, `track_report.json`, `samples_compact.bin`, `samples_compact.bin.crc32`, `samples_core.csv`, `device_samples.csv`, `attempt_summary.csv`.

