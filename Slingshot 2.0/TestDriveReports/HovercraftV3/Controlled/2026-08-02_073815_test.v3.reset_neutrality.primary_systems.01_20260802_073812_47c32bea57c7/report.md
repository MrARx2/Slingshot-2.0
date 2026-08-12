# Hovercraft V3 Diagnostic Run Report

## Executive summary

Captured 327 synchronized physics samples over 3.27 seconds. Data quality: Nominal. Evidence includes 0 unexpected-airborne samples, 321 world/belief disagreement samples, and 0 power-limited samples. These counts identify review windows, not causes.

No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.

## Session identity

Session: `47c32bea57c7`  
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
|0|101|1|0|14.663|23.269|0|11|
|1|226|2.25|0|14.312|22.406|0|16|

## Lap summary

| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |
|---:|---:|---:|---:|---:|---:|
|0|325|3.25|14.465|23.269|27|

## Section summary

| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |
|---|---:|---:|---:|---:|---:|
|unmapped|325|14.465|0|0|0|

## Event timeline

| Event type | Count | Max severity |
|---|---:|---:|
|ManualMarker|3|0|
|SampleRateDrop|1|1|
|TaskUnderMinimum|2|2|
|PowerStarvation|1|2|
|SurfaceReacquired|2|0|
|SpringTravelLimit|6|1|
|Spin|3|2|
|SensorMiss|3|1|
|SensorWorldDisagreement|3|1|
|Reset|1|0|
|MainframeDegraded|1|2|
|ContactLoss|1|0|

## World truth versus craft belief

321 samples disagreed on grounded/contact classification.

## Sensor accuracy

| Sensor | Samples | Hits | Comparable | Mean abs distance error (m) | Mean normal error (deg) | Misses |
|---|---:|---:|---:|---:|---:|---:|
|sensor.chassis.front|325|187|184|0.236|0|4|
|sensor.chassis.rear|325|0|0|0|0|0|
|sensor.chassis.left|325|0|0|0|0|0|
|sensor.chassis.right|325|0|0|0|0|0|
|sensor.chassis.top|325|0|0|0|0|0|
|sensor.chassis.bottom|325|319|319|0.024|0|1|
|sensor_6|2|0|2|0|0|0|
|sensor_7|2|0|2|0|0|0|
|sensor_8|2|0|2|0|0|0|
|sensor_9|2|0|2|0|0|0|
|Hover.Front.Left.Bottom|323|320|323|0|0|0|
|Hover.Front.Right.Bottom|323|320|323|0|0|0|
|Hover.Rear.Left.Bottom|323|320|323|0|0|0|
|Hover.Rear.Right.Bottom|323|320|323|0|0|0|

## System execution

| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |
|---|---|---:|---:|---:|---:|---:|---:|
|software.pilotinterface.stock|PilotInterface|120|120|46.078|0|200|0|
|software.hover.stock|Hover|120|120|46.078|0|200|0|
|software.stabilizer.stock|Stabilizer|120|120|46.078|0|200|0|
|software.drive.stock|Drive|60|60|22.922|0|200|0|
|software.traction.stock|Traction|120|120|46.078|0|200|0|
|software.aerodynamics.stock|Aerodynamics|60|60|22.922|0|200|0|
|software.telemetry.stock|Telemetry|30|30|11.424|0|200|0|

## Router and actuator behavior

| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Propulsion.Rear.Center:connector.propulsion.fixed.baseline.01|Part|0|0|1|0|19.998|0|0|
|Propulsion.Rear.Center:thruster.main.balanced.01|Thruster|1.055|1.055|1|599248.3|21.717|0|0|
|Braking.Front.Center:thruster.brake.balanced.01|Thruster|0.003|0.003|1|137751.2|19.998|0|0|
|Hover.Front.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.2|0.948|1|0|0|286|0|
|Hover.Front.Left.Bottom:thruster.hover.balanced.01|Thruster|1.5|1.5|1|638590.3|19.998|0|0|
|Hover.Front.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.185|0.961|1|0|0|283|0|
|Hover.Front.Right.Bottom:thruster.hover.balanced.01|Thruster|1.388|1.388|1|471066.1|19.998|0|0|
|Hover.Rear.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.134|0.823|1|0|0|33|0|
|Hover.Rear.Left.Bottom:thruster.hover.balanced.01|Thruster|1.007|1.007|1|634030.4|19.998|0|0|
|Hover.Rear.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.149|0.92|1|0|0|62|0|
|Hover.Rear.Right.Bottom:thruster.hover.balanced.01|Thruster|1.116|1.116|1|476484.7|19.998|0|0|
|Control.Front.Left.Top:thruster.roof.balanced.01|Thruster|1.059|1.058|1|288541.4|19.998|0|0|
|Control.Front.Right.Top:thruster.roof.balanced.01|Thruster|1.013|1.012|1|283706.5|19.998|0|0|
|Control.Rear.Left.Top:thruster.roof.balanced.01|Thruster|0.989|0.989|1|257866.4|19.998|0|0|
|Control.Rear.Right.Top:thruster.roof.balanced.01|Thruster|0.802|0.802|1|240907.3|19.998|0|0|
|Strafe.Left.Front:thruster.strafe.balanced.01|Thruster|0.525|0.525|1|502235.7|20.042|0|0|
|Strafe.Right.Front:thruster.strafe.balanced.01|Thruster|0.368|0.368|1|349015.9|19.998|0|0|
|Strafe.Left.Rear:thruster.strafe.balanced.01|Thruster|0.394|0.394|1|480876.7|19.998|0|0|
|Strafe.Right.Rear:thruster.strafe.balanced.01|Thruster|0.295|0.295|1|286585.4|19.998|0|0|
|Core.Main:energycore.main.balanced.01|Part|0.033|0.033|1|0|19.998|0|0|
|Cockpit.Main:cockpit.main.balanced.01|Part|0|0|1|0|19.998|0|0|
|System.PilotInterface:computer.pilotinterface.balanced.01|Part|120|120|1|0|0|0|0|
|System.Drive:computer.drive.balanced.01|Part|60|60|1|0|0|0|0|
|System.Hover:computer.hover.balanced.01|Part|120|120|1|0|0|0|0|
|System.Stabilizer:computer.stabilizer.balanced.01|Part|120|120|1|0|0|0|0|
|System.Traction:computer.traction.balanced.01|Part|120|120|1|0|0|0|0|
|System.Aerodynamics:computer.aerodynamics.balanced.01|Part|60|60|1|0|0|0|0|
|System.Telemetry:computer.telemetry.balanced.01|Part|30|30|1|0|0|0|0|
|Aero.Front.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.408|1|1|0|0|0|0|
|Aero.Front.Left:fin.aero.balanced.01|AerodynamicFin|16.155|93.054|1|288.616|0|0|0|
|Aero.Front.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.591|1|1|0|0|0|0|
|Aero.Front.Right:fin.aero.balanced.01|AerodynamicFin|10.571|52.694|1|149.497|0|0|0|
|Aero.Rear.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.408|1|1|0|0|0|0|
|Aero.Rear.Left:fin.aero.balanced.01|AerodynamicFin|27.08|363.292|1|724.022|0|0|0|
|Aero.Rear.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.591|1|1|0|0|0|0|
|Aero.Rear.Right:fin.aero.balanced.01|AerodynamicFin|23.644|258.08|1|539.509|0|0|0|
|Cooling.Main:cooling.active.balanced.01|CoolingModule|0|0|1|0|0|0|0|

## Force and torque analysis

| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |
|---|---:|---:|---:|
|gravity|113948.1|113948.4|-113948.1|
|propulsion|506410.9|599248.3|-77107.95|
|braking|5913.041|137751.2|717.977|
|hover|801712.1|2220172|791341.6|
|roof|617634.5|1069841|-609800.2|
|lateral|204316.8|746203.7|8385.706|
|aerodynamic|592.642|1204.386|397.83|
|contact|699.571|114949.1|699.571|
|expected_net|608649.5|1527499|686.514|
|observed_net|609316|1527499|686.52|
|residual|674.693|140411.2|0.007|

Maximum residual force: 140411.2 N; maximum residual torque: 467359.6 Nm.
Contact-free residual (valid dynamics only): mean 0.426 N; p95 1.027 N; max 1.364 N across 318 samples.

## Power and thermal analysis

0 samples were power-limited; 0 per-device samples were thermally derated.

## Atmosphere and aerodynamics

Invalid world samples: 0; samples inside zones: 0. Density range: 1.224 to 1.225 kg/m3. Temperature range: 14.9 to 15.0 C. Maximum Mach: 0.063; maximum dynamic pressure: 280 Pa.

## Airborne/contact analysis

0 samples were classified unexpected-airborne.

## Repeated issue locations

| Section | Distance window (m) | Events | Max severity | Types |
|---|---:|---:|---:|---|
|unmapped|0–10|27|2|ContactLoss, MainframeDegraded, ManualMarker, PowerStarvation, Reset, SampleRateDrop, SensorMiss, SensorWorldDisagreement, Spin, SpringTravelLimit, SurfaceReacquired, TaskUnderMinimum|

## Evidence tables

The tables below are derived from synchronized samples; raw records remain authoritative.

## Potential investigation areas

- Review world-contact versus hover-belief disagreement samples and their sensor ages.
- Review high force-residual windows for unmodeled contacts or incomplete force attribution.

## Raw-data files

`channel_schema.json`, `events.json`, `event_context.ndjson`, `track_report.json`, `samples_compact.bin`, `samples_compact.bin.crc32`, `samples_core.csv`, `device_samples.csv`, `attempt_summary.csv`.

