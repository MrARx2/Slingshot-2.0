using UnityEngine;

/// <summary>
/// Manual vertical thruster control layer.
/// <para>
/// Q fires roof thrusters for downforce. E adds lift through the bottom
/// hover thrusters. This system is manual player control, not automatic
/// hover stabilization.
/// </para>
/// </summary>
public class AttitudeControlArray : MonoBehaviour
{
    [HideInInspector] public Rigidbody rb;
    [HideInInspector] public ThrusterBus thrusterBus;

    [Header("Manual Q/E Thruster Power")]
    [Tooltip("Raw throttle added to all roof thrusters while Q is held.")]
    public float manualRoofThrottle = 1.0f;

    [Tooltip("Raw throttle added to all bottom hover thrusters while E is held.")]
    public float manualBottomThrottle = 0.85f;

    [Header("Near Ground Safety")]
    [Tooltip("Roof/downforce multiplier when telemetry says the craft is grounded. Prevents Q from smashing the craft into the floor.")]
    [Range(0f, 1f)]
    public float roofThrottleMultiplierWhenGrounded = 0.35f;

    [Tooltip("Bottom/lift multiplier when grounded. Keep below 1 if E should not act like a full jump while sitting on the ground.")]
    [Range(0f, 1f)]
    public float bottomThrottleMultiplierWhenGrounded = 0.75f;

    public float CurrentRoofThrottle => _currentRoofThrottle;
    public float CurrentBottomThrottle => _currentBottomThrottle;

    private float _currentRoofThrottle;
    private float _currentBottomThrottle;

    public void ApplyAttitudeControl(CraftIntent intent, CraftTelemetry telemetry, TractionState traction)
    {
        if (thrusterBus == null)
        {
            _currentRoofThrottle = 0f;
            _currentBottomThrottle = 0f;
            return;
        }

        // If the stabilizer is armed, it owns vertical thrusters. Manual Q/E
        // should not fight it. Exception: recovery override lets Q use roof
        // thrusters when the craft is inverted/on its back.
        if (intent.verticalThrustersLockedByStabilizer && !intent.recoveryOverrideActive)
        {
            _currentRoofThrottle = 0f;
            _currentBottomThrottle = 0f;
            return;
        }

        float grounded01 = Mathf.Clamp01(telemetry.groundedFactor);

        float roofMultiplier = intent.recoveryOverrideActive
            ? 1f
            : Mathf.Lerp(1f, roofThrottleMultiplierWhenGrounded, grounded01);

        float bottomMultiplier = Mathf.Lerp(1f, bottomThrottleMultiplierWhenGrounded, grounded01);

        // Submit raw desired throttle. EnergyCore will grant/scaleback the final
        // amount in ThrusterBus based on the actual roof/hover thruster hardware.
        _currentRoofThrottle = Mathf.Clamp01(intent.manualRoofThrusterRequest) * manualRoofThrottle * roofMultiplier;
        _currentBottomThrottle = Mathf.Clamp01(intent.manualBottomThrusterRequest) * manualBottomThrottle * bottomMultiplier;

        if (_currentRoofThrottle > 0f)
        {
            thrusterBus.AddThrottle(ThrusterNode.ThrusterRole.Roof, null, _currentRoofThrottle, ThrusterPowerChannel.Roof);
        }

        if (_currentBottomThrottle > 0f)
        {
            // Add to hover nodes instead of overwriting HoverStabilizerArray output.
            thrusterBus.AddThrottle(ThrusterNode.ThrusterRole.Hover, null, _currentBottomThrottle, ThrusterPowerChannel.Bottom);
        }
    }
}
