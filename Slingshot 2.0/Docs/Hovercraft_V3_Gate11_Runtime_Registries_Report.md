# Hovercraft V3 Gate 11 Runtime Registries Report

## Status

**Gate 11: passed.**

Gate 11 implements the Socket Registry, Part Registry, and Capability Registry
named by the GDD runtime-kernel architecture. Runtime systems can now resolve
the assembled craft through stable identities and indexed relationships rather
than rescanning transforms or assuming one part per socket.

V2 source, prefabs, scenes, and behavior remain untouched.

## Socket Registry

`V3SocketRegistry` is initialized from the assembled chassis sockets and owns:

- the ordered runtime socket list;
- stable-ID lookup;
- paired-socket lookup;
- configuration-group lookup;
- runtime duplicate/empty-ID validation.

The reference chassis registers 16 sockets. Paired relationships such as
`Hover.Front.Left.Bottom` to `Hover.Front.Right.Bottom` resolve through the
registry without searching the hierarchy.

The build validator remains the first validation boundary. Registry validation
is repeated after instantiation so a malformed runtime prefab cannot silently
produce an ambiguous lookup table.

## Part Registry

`V3PartRegistry` indexes runtime instances by:

- ordered installed-part identity;
- parent socket ID;
- stable part-definition ID.

Repeated products remain repeated instances. The four reference hover
thrusters therefore produce four entries under
`Thruster.Hover.V2Reference`, and the four lateral thrusters produce four
entries under `Thruster.Strafe.V2Reference`.

Connector chains intentionally permit multiple parts at one parent socket.
The gimbal build returns both:

```text
Propulsion.Rear.Center
|- Connector.Gimbal.Main.V2Reference
`- Thruster.Main.V2Reference
```

Callers can query all parts at a socket or resolve its connector and endpoint
separately. Typed endpoint lookup supports actuator consumers without relying
on installation order.

## Capability Registry

`V3CapabilityRegistry` activates the existing
`ProvidedCapabilities` / `RequiredCapabilities` data contract.

It reports:

- the union of installed provided capabilities;
- the union of installed required capabilities;
- missing capabilities;
- overall requirement satisfaction;
- providers for each individual capability flag;
- `HasAll` and `HasAny` queries.

Capabilities are craft-global dependencies. A requirement may therefore be
satisfied by another installed part. In the reference build, the cockpit
provides drive, hover, stabilization, and vectoring control; the EnergyCore
provides power distribution; and relevant powered parts provide thermal
telemetry.

## Build validation

`CraftBuildValidator` now performs a global capability pass after validating
all individual socket installations.

```text
Missing = Required capabilities AND NOT Provided capabilities
```

Missing requirements produce a `CAPABILITY_MISSING` build error before runtime
assembly. Runtime assembly repeats the registry check and rejects the
instantiated craft if its resolved providers do not satisfy its requirements.

The automated tests prove both sides:

- a main thruster requiring `DriveControl` is valid when the installed cockpit
  provides it;
- a cockpit requiring `DriveControl` is rejected after its only provider is
  removed.

## Telemetry integration

Gate 10 snapshots now include:

- registered socket count;
- provided capabilities;
- required capabilities;
- missing capabilities;
- an aggregate requirements-satisfied flag.

This allows cockpit, diagnostics, garage, and recording consumers to explain a
craft's functional composition without directly accessing the mutable
registries.

## Validation

- Gate 11 fast suite:
  `Temp/CodexV3UnityProject/TestResults-gate11-fast-r2.xml`
  (`55` passed, `1` opt-in capture skipped, `0` failed)
- Full direct-build parity regression:
  `Temp/CodexV3UnityProject/TestResults-gate11-parity-regression.xml`
  (`1/1` passed)
- Regression run ID: `20260727_095454`
- Archived regression telemetry:
  `Temp/Gate3ParityCapture/20260727_095454`

All compared V3 scalar metrics are exactly unchanged from Gate 10:

- 3.4050248 m idle clearance;
- 7,075.7573 km/h maximum speed;
- 2.2000504 s to 500 km/h;
- 4.3000984 s to 1,000 km/h;
- 792.3162 m braking distance;
- 413.4055 degrees/s peak yaw rate;
- 13.077149 km/h peak side speed;
- 919.6492 requested and 919.6490 granted peak power.

## Gate decision

Gate 11 is accepted. Socket topology, runtime products, connector chains, and
functional capabilities now have authoritative indexed owners. Future runtime
garage and equippable-system work can build on these registries without
coupling UI or controllers to scene hierarchy details.
