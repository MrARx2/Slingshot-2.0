# Hovercraft V3 Diagnostic Run Report

## Executive summary

Captured 277 synchronized physics samples over 2.77 seconds. Data quality: Nominal. Evidence includes 0 unexpected-airborne samples, 133 world/belief disagreement samples, and 0 power-limited samples. These counts identify review windows, not causes.

No gameplay authority was assigned to the recorder. Automated findings identify evidence windows and do not assert causation.

## Session identity

Session: `1297e655a4c5`  
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
|0|277|2.76|0|23.592|30|0|11|

## Lap summary

| Lap | Samples | Duration (s) | Mean speed (m/s) | Max speed (m/s) | Events |
|---:|---:|---:|---:|---:|---:|
|0|276|2.75|23.569|30|11|

## Section summary

| Section | Samples | Mean speed | Max lateral offset | Min ride height | Events |
|---|---:|---:|---:|---:|---:|
|unmapped|276|23.569|0|0|0|

## Event timeline

| Event type | Count | Max severity |
|---|---:|---:|
|ManualMarker|2|0|
|SampleRateDrop|1|1|
|TaskUnderMinimum|1|2|
|PowerStarvation|1|2|
|SurfaceReacquired|1|0|
|ThrusterSaturation|1|1|
|NearSurfaceExit|2|0|
|SurfaceEnvelopeDeparture|1|0|
|ProbeLoss|1|1|

## World truth versus craft belief

133 samples disagreed on grounded/contact classification.

## Sensor accuracy

| Sensor | Samples | Hits | Comparable | Mean abs distance error (m) | Mean normal error (deg) | Misses |
|---|---:|---:|---:|---:|---:|---:|
|sensor.chassis.front|276|0|0|0|0|0|
|sensor.chassis.rear|276|0|0|0|0|0|
|sensor.chassis.left|276|0|0|0|0|0|
|sensor.chassis.right|276|0|0|0|0|0|
|sensor.chassis.top|276|0|0|0|0|0|
|sensor.chassis.bottom|276|118|117|0.005|0|0|
|sensor_6|1|0|1|0|0|0|
|sensor_7|1|0|1|0|0|0|
|sensor_8|1|0|1|0|0|0|
|sensor_9|1|0|1|0|0|0|
|Hover.Front.Left.Bottom|275|121|257|0|0|0|
|Hover.Front.Right.Bottom|275|121|257|0|0|0|
|Hover.Rear.Left.Bottom|275|134|274|0|0|0|
|Hover.Rear.Right.Bottom|275|134|274|0|0|0|

## System execution

| Task | Role | Requested Hz | Granted Hz | Measured Hz | Skipped | Below minimum samples | Fault samples |
|---|---|---:|---:|---:|---:|---:|---:|
|software.pilotinterface.stock|PilotInterface|120|120|76.396|0|100|0|
|software.hover.stock|Hover|120|120|76.396|0|100|0|
|software.stabilizer.stock|Stabilizer|120|120|76.396|0|100|0|
|software.drive.stock|Drive|60|60|38.151|0|100|0|
|software.traction.stock|Traction|120|120|76.396|0|100|0|
|software.aerodynamics.stock|Aerodynamics|60|60|38.151|0|100|0|
|software.telemetry.stock|Telemetry|30|30|18.941|0|100|0|

## Router and actuator behavior

| Device | Type | Mean requested | Mean actual | Mean grant | Peak force (N) | Peak temp (C) | Limits | Faults |
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Propulsion.Rear.Center:connector.propulsion.fixed.baseline.01|Part|0|0|1|0|19.997|0|0|
|Propulsion.Rear.Center:thruster.main.balanced.01|Thruster|0|0|1|0|19.998|0|0|
|Braking.Front.Center:thruster.brake.balanced.01|Thruster|0.024|0.024|1|103333.8|19.997|0|0|
|Hover.Front.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.013|0.084|1|0|0|0|0|
|Hover.Front.Left.Bottom:thruster.hover.balanced.01|Thruster|0.095|0.095|1|79399.28|19.997|0|0|
|Hover.Front.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.013|0.084|1|0|0|0|0|
|Hover.Front.Right.Bottom:thruster.hover.balanced.01|Thruster|0.094|0.094|1|79399.29|19.997|0|0|
|Hover.Rear.Left.Bottom:connector.spring.hover.balanced.01|SpringMount|0.017|0.113|1|0|0|0|0|
|Hover.Rear.Left.Bottom:thruster.hover.balanced.01|Thruster|0.127|0.127|1|151760.8|19.997|0|0|
|Hover.Rear.Right.Bottom:connector.spring.hover.balanced.01|SpringMount|0.016|0.105|1|0|0|0|0|
|Hover.Rear.Right.Bottom:thruster.hover.balanced.01|Thruster|0.119|0.119|1|88223.3|19.997|0|0|
|Control.Front.Left.Top:thruster.roof.balanced.01|Thruster|0.049|0.041|1|93973.02|19.997|0|0|
|Control.Front.Right.Top:thruster.roof.balanced.01|Thruster|0.039|0.034|1|92525.72|19.997|0|0|
|Control.Rear.Left.Top:thruster.roof.balanced.01|Thruster|0.039|0.035|1|100420.3|19.997|0|0|
|Control.Rear.Right.Top:thruster.roof.balanced.01|Thruster|0.039|0.035|1|94367.47|19.997|0|0|
|Strafe.Left.Front:thruster.strafe.balanced.01|Thruster|0|0|1|28.3|19.997|0|0|
|Strafe.Right.Front:thruster.strafe.balanced.01|Thruster|0|0|1|527.136|19.997|0|0|
|Strafe.Left.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|226.319|19.997|0|0|
|Strafe.Right.Rear:thruster.strafe.balanced.01|Thruster|0|0|1|264.975|19.997|0|0|
|Core.Main:energycore.main.balanced.01|Part|0.031|0.031|1|0|19.998|0|0|
|Cockpit.Main:cockpit.main.balanced.01|Part|0|0|1|0|19.997|0|0|
|System.PilotInterface:computer.pilotinterface.balanced.01|Part|120|120|1|0|0|0|0|
|System.Drive:computer.drive.balanced.01|Part|60|60|1|0|0|0|0|
|System.Hover:computer.hover.balanced.01|Part|120|120|1|0|0|0|0|
|System.Stabilizer:computer.stabilizer.balanced.01|Part|120|120|1|0|0|0|0|
|System.Traction:computer.traction.balanced.01|Part|120|120|1|0|0|0|0|
|System.Aerodynamics:computer.aerodynamics.balanced.01|Part|60|60|1|0|0|0|0|
|System.Telemetry:computer.telemetry.balanced.01|Part|30|30|1|0|0|0|0|
|Aero.Front.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Front.Left:fin.aero.balanced.01|AerodynamicFin|23.819|111.539|1|238.85|0|0|0|
|Aero.Front.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Front.Right:fin.aero.balanced.01|AerodynamicFin|23.848|119.478|1|255.964|0|0|0|
|Aero.Rear.Left:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Rear.Left:fin.aero.balanced.01|AerodynamicFin|23.69|49.984|1|112.012|0|0|0|
|Aero.Rear.Right:connector.aero.rotary.balanced.01|RotaryActuator|0.496|1|1|0|0|0|0|
|Aero.Rear.Right:fin.aero.balanced.01|AerodynamicFin|23.705|57.207|1|127.648|0|0|0|
|Cooling.Main:cooling.active.balanced.01|CoolingModule|0|0|1|0|0|0|0|

## Force and torque analysis

| Category | Mean magnitude (N) | Peak magnitude (N) | Mean gravity-vertical (N) |
|---|---:|---:|---:|
|gravity|113948.1|113948.4|-113948.1|
|propulsion|0|0|0|
|braking|41138.07|103333.8|-208.508|
|hover|69568.23|317605|69565.85|
|roof|23296.39|381286.5|-23294.99|
|lateral|27.578|565.793|0.043|
|aerodynamic|370.3|759.57|-228.373|
|contact|0|0|0|
|expected_net|109254.4|286635.1|-68114.37|
|observed_net|109254.5|286635|-68114.34|
|residual|0.595|1.143|-0.005|

Maximum residual force: 1.143 N; maximum residual torque: 3990.044 Nm.
Contact-free residual (valid dynamics only): mean 0.595 N; p95 1.033 N; max 1.143 N across 276 samples.

## Power and thermal analysis

0 samples were power-limited; 0 per-device samples were thermally derated.

## Atmosphere and aerodynamics

Invalid world samples: 0; samples inside zones: 0. Density range: 1.224 to 1.225 kg/m3. Temperature range: 14.9 to 15.0 C. Maximum Mach: 0.088; maximum dynamic pressure: 551 Pa.

## Airborne/contact analysis

0 samples were classified unexpected-airborne.

## Repeated issue locations

| Section | Distance window (m) | Events | Max severity | Types |
|---|---:|---:|---:|---|
|unmapped|0–10|11|2|ManualMarker, NearSurfaceExit, PowerStarvation, ProbeLoss, SampleRateDrop, SurfaceEnvelopeDeparture, SurfaceReacquired, TaskUnderMinimum, ThrusterSaturation|

## Evidence tables

The tables below are derived from synchronized samples; raw records remain authoritative.

## Potential investigation areas

- Review world-contact versus hover-belief disagreement samples and their sensor ages.
- Review high force-residual windows for unmodeled contacts or incomplete force attribution.

## Raw-data files

`channel_schema.json`, `events.json`, `event_context.ndjson`, `track_report.json`, `samples_compact.bin`, `samples_compact.bin.crc32`, `samples_core.csv`, `device_samples.csv`, `attempt_summary.csv`.

