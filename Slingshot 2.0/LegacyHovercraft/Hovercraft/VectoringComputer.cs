using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// V2 Vectoring Computer — Flight Computer / Intent Interpreter.
/// Converts raw pilot input into clean, normalized craft intent.
///
/// Important: this script decides what the pilot wants. It does not apply
/// forces, does not set thrusters, and does not tune suspension physics.
/// </summary>
public class VectoringComputer : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  PILOT INTENT RESPONSE
    // ══════════════════════════════════════════════════════════════

    [Header("Pilot Intent Response")]
    [Tooltip("Main mouse steering gain before yaw is sent to VectorThrusterArray. Use this as the first steering sensitivity knob after mouse sensitivity.")]
    public float mouseYawIntentGain = 1.0f;

    [Tooltip("Main mouse pitch gain before pitch is sent to HoverStabilizerArray. Use this as the first pitch sensitivity knob after mouse sensitivity.")]
    public float mousePitchIntentGain = 1.0f;

    [Tooltip("How much A/D edge shift contributes to inside-edge banking/carve commitment.")]
    [FormerlySerializedAs("edgeCarveLeanWeight")]
    [Range(0f, 2f)] public float edgeShiftLeanGain = 0.85f;

    [Tooltip("How much mouse yaw contributes to automatic inside-edge banking.")]
    [FormerlySerializedAs("yawCarveLeanWeight")]
    [Range(0f, 2f)] public float yawToLeanGain = 0.45f;

    [Tooltip("Flip this if the craft leans away from the turn instead of into it.")]
    public bool invertCarveLeanDirection = false;

    // ══════════════════════════════════════════════════════════════
    //  STABILIZER TOGGLE
    // ══════════════════════════════════════════════════════════════

    [Header("Stabilizer Toggle")]
    public bool stabilizerStartsArmed = true;

    [Tooltip("When stabilizer is armed, Q/E manual roof/bottom thrusters remain usable until groundedFactor reaches this value. Then stabilizer owns vertical thrusters.")]
    [Range(0f, 1f)] public float stabilizerVerticalLockGroundedFactor = 0.70f;

    // ══════════════════════════════════════════════════════════════
    //  OVERCHARGE TARGETING
    // ══════════════════════════════════════════════════════════════

    [Header("Overcharge Targeting")]
    [Range(0f, 1f)]
    public float overchargeThrottleSelectionDeadZone = 0.2f;

    public CraftIntent CurrentIntent { get; private set; }
    public bool StabilizerArmed => _stabilizerArmed;

    private bool _stabilizerArmed;
    private int _lastStabilizerToggleFrame = -1;

    private void Awake()
    {
        _stabilizerArmed = stabilizerStartsArmed;
    }

    public void EvaluateIntent(PilotCommand command, CraftTelemetry telemetry)
    {
        EvaluateIntent(command, telemetry, OverchargeTarget.None);
    }

    /// <param name="lockedOverchargeTarget">
    /// The target OverchargeCore locked when charging began (None when not charging).
    /// While a charge is in progress the capture logic MUST follow the locked target,
    /// not a fresh key-based selection — otherwise changing held keys mid-charge
    /// un-captures the charging thruster group and it direct-fires while charging.
    /// </param>
    public void EvaluateIntent(PilotCommand command, CraftTelemetry telemetry, OverchargeTarget lockedOverchargeTarget)
    {
        if (command.stabilizerTogglePressed && command.sampleFrame != _lastStabilizerToggleFrame)
        {
            _stabilizerArmed = !_stabilizerArmed;
            _lastStabilizerToggleFrame = command.sampleFrame;
        }

        float yawRequest = Mathf.Clamp(command.steer * mouseYawIntentGain, -1f, 1f);
        float pitchLeanRequest = Mathf.Clamp(command.pitch * mousePitchIntentGain, -1f, 1f);
        float edgeShiftRequest = command.edgeShift;

        float carveLeanRequest =
            edgeShiftRequest * edgeShiftLeanGain
            + yawRequest * yawToLeanGain;

        carveLeanRequest = Mathf.Clamp(carveLeanRequest, -1f, 1f);

        if (invertCarveLeanDirection)
        {
            carveLeanRequest = -carveLeanRequest;
        }

        float throttle = Mathf.Max(0f, command.throttle);
        float brake = Mathf.Max(0f, -command.throttle);

        OverchargeTarget overchargeTarget = lockedOverchargeTarget != OverchargeTarget.None
            ? lockedOverchargeTarget
            : SelectOverchargeTarget(command);
        bool overcharging = command.overchargeHeld;

        // When Space is held, the selected thruster group is captured by
        // Overcharge and does NOT direct-fire until Space is released.
        if (overcharging && overchargeTarget == OverchargeTarget.MainThruster)
        {
            throttle = 0f;
        }

        if (overcharging && overchargeTarget == OverchargeTarget.BrakeThruster)
        {
            brake = 0f;
        }

        // Stabilizer has two states:
        // Armed  = allowed to catch/stabilize when close enough.
        // Active = close enough to a surface to actually own vertical thrusters.
        //
        // This lets the player press R before landing and still use Q/E to manually
        // soften the approach. Once the craft reaches the hover capture zone, the
        // stabilizer owns roof/bottom thrusters until disengaged again. Exception:
        // if the craft is inverted/on its back, Q is allowed as a recovery override.
        bool recoveryOverride = telemetry.isInverted && command.roofThrustersHeld;
        bool stabilizerActiveEnoughToLockVertical =
            _stabilizerArmed && telemetry.groundedFactor >= stabilizerVerticalLockGroundedFactor;
        bool verticalLockedByStabilizer = stabilizerActiveEnoughToLockVertical && !recoveryOverride;

        float manualRoofRequest =
            command.roofThrustersHeld
            && !verticalLockedByStabilizer
            && !(overcharging && overchargeTarget == OverchargeTarget.RoofThrusters)
                ? 1f
                : 0f;

        float manualBottomRequest =
            command.bottomThrustersHeld
            && !verticalLockedByStabilizer
            && !(overcharging && overchargeTarget == OverchargeTarget.BottomThrusters)
                ? 1f
                : 0f;

        CurrentIntent = new CraftIntent
        {
            commandFrame = command.sampleFrame,

            yawRequest = yawRequest,
            edgeShiftRequest = edgeShiftRequest,
            carveLeanRequest = carveLeanRequest,
            pitchLeanRequest = pitchLeanRequest,

            throttle = throttle,
            brake = brake,

            manualRoofThrusterRequest = manualRoofRequest,
            manualBottomThrusterRequest = manualBottomRequest,

            wantsGripBreaker = command.gripBreakerHeld,

            wantsOvercharge = command.overchargeHeld,
            overchargeReleased = command.overchargeReleased,
            overchargeTarget = overchargeTarget,

            stabilizerArmed = _stabilizerArmed,
            verticalThrustersLockedByStabilizer = verticalLockedByStabilizer,
            recoveryOverrideActive = recoveryOverride
        };
    }

    private OverchargeTarget SelectOverchargeTarget(PilotCommand command)
    {
        if (command.roofThrustersHeld)
        {
            return OverchargeTarget.RoofThrusters;
        }

        if (command.bottomThrustersHeld)
        {
            return OverchargeTarget.BottomThrusters;
        }

        if (command.throttle > overchargeThrottleSelectionDeadZone)
        {
            return OverchargeTarget.MainThruster;
        }

        if (command.throttle < -overchargeThrottleSelectionDeadZone)
        {
            return OverchargeTarget.BrakeThruster;
        }

        // Space alone = bottom thruster charge/jump.
        return OverchargeTarget.BottomThrusters;
    }
}
