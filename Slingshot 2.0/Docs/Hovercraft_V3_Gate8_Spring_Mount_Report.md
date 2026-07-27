# Hovercraft V3 Gate 8 Spring Mount Experiment Report

## Status

**Gate 8: passed on 2026-07-27 with physical-only coupled-craft
revalidation.**

The spring integrator, limits, mass/COM contribution, and moving physical force
origin remain valid. The coupled five-second flat-ground simulation now runs
with the final physical-only stabilization allocator and remains bounded.

Gate 8 resolves the GDD's first spring-simulation decision and adds a saved
craft variation with one V2-reference hover thruster installed through a
passive spring connector.

V2 source, prefabs, scenes, and behavior remain untouched.

## Technical decision

The first spring mount uses controlled one-axis local motion with the existing
single root Rigidbody. It does not add a child Rigidbody or
`ConfigurableJoint`.

This is the appropriate model for the current architecture because all
installed-part mass already belongs to the root rigid body. The endpoint's
external thrust must therefore remain fully applied to the craft; filtering
that force through a simulated child mass would incorrectly change
center-of-mass acceleration without representing the equal and opposite
internal reaction.

Instead:

1. The endpoint calculates and applies force through the existing
   `RuntimeThrusterInstance` path.
2. The attached `RuntimeSpringMountInstance` projects that endpoint load onto
   its single authored movement axis.
3. Endpoint mass, stiffness, damping, and the projected load drive a substepped
   spring-damper simulation.
4. The connector child mount moves within hard compression and extension
   limits.
5. The endpoint's real force origin follows that moving mount.

The spring never moves the root transform directly and never creates a second
force path.

## Authored reference connector

| Property | Value |
| --- | ---: |
| Connector | V2 Reference Hover Spring Mount |
| Parent/child family | Hover Vertical Control |
| Movement axis | Connector-local forward |
| Compression limit | 0.30 m |
| Extension limit | 0.12 m |
| Stiffness | 600,000 N/m |
| Damping | 18,000 N·s/m |
| Supported endpoint mass | 200 kg |
| Supported endpoint force | 1,200,000 N |
| Connector mass | 35 kg |
| Power demand | 0 |
| Thermal simulation | Disabled |

The integrator uses semi-implicit Euler steps no larger than 1/240 second,
independent of the caller's fixed timestep. Full manual lift from the installed
160,000 N hover thruster settles at approximately 0.267 m compression, below
the 0.30 m hard stop.

## Saved build variation

```text
ApexV3_HoverSpringDemo
Hover.Front.Left.Bottom
└── V2 Reference Hover Spring Mount
    └── V2 Reference Hover Thruster
```

The other fifteen installations are unchanged from `ApexV3_V2Reference`. The
variation:

- uses the exact same hover-thruster definition as the direct build;
- has 17 installed runtime parts instead of 16;
- has one root Rigidbody;
- increases total mass from 11,000 kg to 11,035 kg;
- shifts center of mass toward the front-left connector;
- retains the 45-unit systems idle demand because the spring is passive;
- retains 16 thermally tracked parts because the passive spring has no thermal
  profile.

All four bottom hover sockets now allow a compatible spring connector in the
editor builder. The saved demonstration changes only the front-left chain so
asymmetric mass and compliance are observable.

## Validation

- Generated reference, gimbal, and spring builds all pass authoring and runtime
  validation.
- Final suite:
  `Temp/CodexV3UnityProject/TestResults-final-audit.xml`
  (`76` passed, `1` opt-in capture skipped, `0` failed)
- Coupled physical simulation:
  `SpringVariation_RemainsBoundedDuringCoupledFlatGroundHover`
- Final direct-build parity:
  `Temp/CodexV3UnityProject/TestResults-physical-parity-capture-r7.xml`
  (`1/1` passed)
- Parity run ID: `20260727_150026`

The automated spring tests cover:

- static deflection under a known endpoint load;
- damped return to neutral after unloading;
- compression and extension hard stops;
- same-endpoint identity through the connector;
- added mass and center-of-mass change;
- one-Rigidbody enforcement;
- unchanged endpoint force transfer;
- unchanged systems demand and thermal-part count;
- dynamic reset;
- five seconds of coupled flat-ground hover with bounded chassis and spring
  motion.

## Gate decision

Gate 8 is accepted. Controlled local mount simulation is stable and physically
consistent with the one-root-Rigidbody prototype. A jointed child-body model is
not justified at this stage; it should be reconsidered only if future damage,
detachment, wheel-like contact, or independently colliding endpoints require
real child-body inertia.
