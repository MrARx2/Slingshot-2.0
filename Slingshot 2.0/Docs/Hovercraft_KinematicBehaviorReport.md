# Hovercraft V2 — Kinematic Behavior Report

Every direct position/rotation/velocity manipulation found in the craft-related codebase, with a classification per the audit rules.

## Verdict

**The craft runtime contains zero kinematic writes.** Every subsystem drives the rigidbody exclusively through `AddForce` / `AddForceAtPosition` / `AddTorque`. No velocity projection, normalization, or clamping ever writes back to the rigidbody (TractionCore and CraftAerodynamics clamp their own *force vectors* before applying them, which is legitimate).

## Inventory

| Location | What it does | Classification |
|---|---|---|
| `TrackGenerator.cs:612-616` — `rb.position/rotation = …; linearVelocity = 0; angularVelocity = 0; WakeUp()` | Spawn/respawn teleport to track start (Start, Backspace, post-generate) | **Required by design** — this is the correct way to teleport a rigidbody; keep in V3 |
| `TrackGenerator.cs:620` — `SetPositionAndRotation` fallback | Only used if the craft had no Rigidbody | Required temporary V2 (dead path in practice) |
| `CraftCore.OnPlacedAtTrackStart()` | State reset on teleport (overcharge, smoothed telemetry, camera snap) — no kinematics itself | Required by design |
| `RaceCourse.cs:133` — `TryGetRespawnPose` | Gate-anchored respawn pose computation — **no callers** | Unknown / planned feature (dead code) |
| `SceneBootstrapper.cs` transform writes | Editor-only (`#if UNITY_EDITOR`) one-time scene construction | Debug-only |
| `BoostPadActor` / `GravityZone` transform writes | Position their own actor objects at track build; craft is affected via AddForce only | Not craft manipulation |
| `HovercraftCamera` transform writes | Camera moves itself; zero writes through `targetRigidbody` | Out of scope (read-only w.r.t. craft) |

## Frame-rate discipline

No `Time.deltaTime` appears in any FixedUpdate physics path; all smoothing uses `Time.fixedDeltaTime` with `MoveTowards(rate·dt)` or exponential `1−exp(−k·dt)` forms. The hover damper is explicitly discrete-time-stabilized. Notes:

- `HoverStabilizerArray` emergency-catch blend uses a constant-t Lerp per tick — stable at fixed dt, but its response implicitly depends on the physics rate (0.01 s). Worth a comment if the timestep ever changes.
- `SectionAdaptiveSuspension` smooths its multipliers in `Update()` (dt-scaled correctly) while physics consumes them in FixedUpdate — no bug; moving to FixedUpdate would be marginally cleaner.
- Mouse input is accumulated per render frame and consumed exactly once per physics tick — deliberately frame-rate independent.
