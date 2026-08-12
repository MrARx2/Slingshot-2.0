# Hovercraft V3 Diagnostic Run Report

## Executive summary

Captured 127 synchronized physics samples over 1.27 seconds. Data quality: Nominal. Evidence includes 0 unexpected-airborne samples, 0 world/belief disagreement samples, and 0 power-limited samples. These counts identify review windows, not causes.

No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.

## Session identity

Session: `d27c33d91119`  
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
|0|127|1.26|0|6.176|12.347|0|5|

## Lap summary

| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |
|---:|---:|---:|---:|---:|---:|
|0|126|1.25|6.225|12.347|5|

## Section summary

| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |
|---|---:|---:|---:|---:|---:|
|unmapped|126|6.225|0|0|0|

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
|sensor.chassis.front|126|0|0|0|0|0|
|sensor.chassis.rear|126|0|0|0|0|0|
|sensor.chassis.left|126|0|0|0|0|0|
|sensor.chassis.right|126|0|0|0|0|0|
|sensor.chassis.top|126|0|0|0|0|0|
|sensor.chassis.bottom|126|0|0|0|0|0|
|sensor_6|1|0|1|0|0|0|
|sensor_7|1|0|1|0|0|0|
|sensor_8|1|0|1|0|0|0|
|sensor_9|1|0|1|0|0|0|
|Hover.Front.Left.Bottom|125|0|125|0|0|0|
|Hover.Front.Right.Bottom|125|0|125|0|0|0|
|Hover.Rear.Left.Bottom|125|0|125|0|0|0|
|Hover.Rear.Right.Bottom|125|0|125|0|0|0|

## System execution

| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |
|---|---|---:|---:|---:|---:|---:|---:|
|software.pilotinterface.stock|PilotInterface|120|120|24.721|0|100|0|
|software.hover.stock|Hover|120|120|24.721|0|100|0|
|software.stabilizer.stock|Stabilizer|120|120|24.721|0|100|0|
|software.drive.stock|Drive|60|60|12.258|0|100|0|
|software.traction.stock|Traction|120|120|24.721|0|100|0|
|software.aerodynamics.stock|Aerodynamics|60|60|12.258|0|100|0|
|software.telemetry.stock|Telemetry|30|30|6.129|0|100|0|

## Router and actuator behavior

| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Propulsion.Rear.Center:connector.propulsion.fixed.baseline.01|Part|0|0|1|0|19.998|0|0|
|Propulsion.Rear.Center:thruster.main.balanced.01|Thruster|0|0|1|0|19.999|0|0|
|Braking.Front.Center:thruster.brake.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Hover.Front.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Front.Left.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Hover.Front.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Front.Right.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Hover.Rear.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Rear.Left.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Hover.Rear.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0|0|1|0|0|0|0|
|Hover.Rear.Right.Bottom:thruster.hover.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Control.Front.Left.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Control.Front.Right.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Control.Rear.Left.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Control.Rear.Right.Top:thruster.roof.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Strafe.Left.Front:thruster.strafe.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Strafe.Right.Front:thruster.strafe.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Strafe.Left.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Strafe.Right.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Core.Main:energycore.main.balanced.01|Part|0.031|0.031|1|0|19.998|0|0|
|Cockpit.Main:cockpit.main.balanced.01|Part|0|0|1|0|19.998|0|0|
|System.PilotInterface:computer.pilotinterface.balanced.01|Part|120|120|1|0|0|0|0|
|System.Drive:computer.drive.balanced.01|Part|60|60|1|0|0|0|0|
|System.Hover:computer.hover.balanced.01|Part|120|120|1|0|0|0|0|
|System.Stabilizer:computer.stabilizer.balanced.01|Part|120|120|1|0|0|0|0|
|System.Traction:computer.traction.balanced.01|Part|120|120|1|0|0|0|0|
|System.Aerodynamics:computer.aerodynamics.balanced.01|Part|60|60|1|0|0|0|0|
|System.Telemetry:computer.telemetry.balanced.01|Part|30|30|1|0|0|0|0|
|Aero.Front.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.5|1|1|0|0|0|0|
|Aero.Front.Left:fin.aero.balanced.01|AerodynamicFin|6.146|19.676|1|58.721|0|0|0|
|Aero.Front.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.5|1|1|0|0|0|0|
|Aero.Front.Right:fin.aero.balanced.01|AerodynamicFin|6.146|19.676|1|58.721|0|0|0|
|Aero.Rear.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.5|1|1|0|0|0|0|
|Aero.Rear.Left:fin.aero.balanced.01|AerodynamicFin|6.147|19.678|1|58.729|0|0|0|
|Aero.Rear.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.5|1|1|0|0|0|0|
|Aero.Rear.Right:fin.aero.balanced.01|AerodynamicFin|6.147|19.678|1|58.729|0|0|0|
|Cooling.Main:cooling.active.balanced.01|CoolingModule|0|0|1|0|0|0|0|

## Force and torque analysis

| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |
|---|---:|---:|---:|
|gravity|113948.3|113948.4|-113948.3|
|propulsion|0|0|0|
|braking|0|0|0|
|hover|0|0|0|
|roof|0|0|0|
|lateral|0|0|0|
|aerodynamic|82.004|244.966|81.91|
|contact|0|0|0|
|expected_net|113866.5|113948.4|-113866.5|
|observed_net|113866.5|113948.4|-113866.5|
|residual|0.158|0.516|-0.004|

Maximum residual force: 0.516 N; maximum residual torque: 0 Nm.
Contact-free residual (valid dynamics only): mean 0.158 N; p95 0.438 N; max 0.516 N across 126 samples.

## Power and thermal analysis

0 samples were power-limited; 0 per-device samples were thermally derated.

## Atmosphere and aerodynamics

Invalid world samples: 0; samples inside zones: 0. Density range: 1.220 to 1.221 kg/m3. Temperature range: 14.7 to 14.8 C. Maximum Mach: 0.036; maximum dynamic pressure: 92 Pa.

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

