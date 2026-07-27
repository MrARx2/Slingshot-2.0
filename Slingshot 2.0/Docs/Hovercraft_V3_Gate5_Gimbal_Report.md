# Hovercraft V3 Gate 5 Gimbal Variation Report

## Status

**Gate 5: passed on 2026-07-27 with physical-only actuator revalidation.**

The same endpoint-through-connector requirement remains satisfied, and the
direct-versus-gimbal comparison now runs with the final physical actuator
allocator. The test simulates both assembled crafts and verifies that the
connector-directed rear-main force adds measurable natural yaw authority
beyond the unchanged lateral-thruster layout.

The existing `ApexV3_MainGimbalDemo` build now changes the physical relationship
between the rear chassis socket and the V2-reference main thruster. The endpoint
is the same `ThrusterDefinition` used by the direct build; its new steering
behavior comes entirely from the installed connector.

V2 source, prefabs, and scenes remain untouched.

## Direct and connector builds

```text
ApexV3_V2Reference
Rear Socket
└── V2 Reference Main Thruster
```

```text
ApexV3_MainGimbalDemo
Rear Socket
└── V2 Reference Main Gimbal
    └── V2 Reference Main Thruster
```

Both chains reference the same main-thruster asset. No duplicate gimballed
thruster product was created.

## Runtime implementation

### Runtime gimbal instance

`RuntimeGimbalInstance`:

- owns installed pitch/yaw angle and angular velocity state;
- rotates only the connector child mount;
- respects authored pitch/yaw support and ±30° limits;
- respects the authored 180°/s speed and 720°/s² acceleration;
- exposes current actuator torque against the authored 25,000 Nm rating;
- requests systems power from idle to maximum according to actuator speed;
- reports output through the shared runtime-part and thermal telemetry;
- returns to its neutral mount orientation on dynamic reset.

The child main thruster still applies force through
`RuntimeThrusterInstance`. Because its transform is below the moving connector
mount, the existing force origin and direction naturally follow gimbal motion.
There is no second force path.

### Systems power

`V3PowerDistributor` now accounts for live systems demand as well as static idle
demand. Gimbal motion receives a real power grant before the connector moves,
and that grant reduces the power remaining to propulsion if the EnergyCore
budget becomes constrained.

## Direct-versus-gimbal comparison

| Property | Direct build | Gimbal variation |
| --- | ---: | ---: |
| Main-thruster definition | V2 reference main | Same asset |
| Total mass | 11,000 kg | 11,120 kg |
| Added connector mass | 0 kg | 120 kg |
| COM Z | approximately 0 m | approximately -0.0246 m |
| Systems demand before motion | 45 | 55 |
| Systems demand, first full-yaw step | 45 | 58.2 |
| Main thrust lateral component | 0 | Non-zero, command-directed |
| Natural main-thrust yaw moment | 0 | Positive for positive yaw command |
| Gimbal yaw target | None | -30° mount deflection |
| Stabilizer implementation | V3 reference | Same V3 reference controller |

On the first 0.02-second full-yaw step, the authored acceleration limit produces
14.4°/s requested gimbal velocity, 0.288° of mount movement, and 2,000 Nm of
reported actuator torque. Repeated identical ticks reach but do not exceed the
30° authored limit.

The gimbal variation retains the same hover, traction, vectoring, and
stabilization controllers. Their correction now responds to the connector's
real force direction rather than a connector-specific controller rewrite.

## Validation

- Final suite:
  `Temp/CodexV3UnityProject/TestResults-final-audit.xml`
  (`76` passed, `1` opt-in capture skipped, `0` failed)
- Physical comparison:
  `GimbalVariation_PhysicalSimulationAddsNaturalYawAuthority`
- Final direct-build parity:
  `Temp/CodexV3UnityProject/TestResults-physical-parity-capture-r7.xml`
  (`1/1` passed)
- Parity run ID: `20260727_150026`

The physical comparison also verifies that the gimbal remains powered, obeys
its speed, acceleration, and ±30-degree limits, reports actuator torque and
thermal load, and returns to neutral on reset. A direct build has no gimbal
runtime instance and receives no connector-specific behavior.

## Gate decision

Gate 5 is accepted. The same endpoint gains directional thrust, natural yaw
torque, actuator response, mass/COM changes, systems-power demand, and thermal
load solely because it is installed through a gimbal connector.
