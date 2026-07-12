using UnityEngine;

/// <summary>
/// Shared data contracts for the V2 hovercraft architecture.
/// </summary>

public enum OverchargeTarget
{
    None,
    BottomThrusters,
    RoofThrusters,
    MainThruster,
    BrakeThruster
}

public struct PilotCommand
{
    public int sampleFrame;

    public float throttle;
    public float steer;
    public float pitch;
    public float edgeShift;

    public bool roofThrustersHeld;
    public bool bottomThrustersHeld;

    public bool overchargeHeld;
    public bool overchargePressed;
    public bool overchargeReleased;

    public bool stabilizerTogglePressed;
    public bool gripBreakerHeld;
}

public struct CraftTelemetry
{
    public Vector3 worldVelocity;
    public Vector3 localVelocity;

    public float speed;
    public float forwardSpeed;
    public float sideSpeed;

    public float rollAngle;
    public float pitchAngle;
    public float yawRate;

    public bool isGrounded;
    public float groundedFactor;
    public int groundedCount;

    public Vector3 groundNormal;

    // True surface contact uses both bottom hover probes and roof probes.
    // This lets the craft know it is touching a surface even when upside down.
    public bool hasSurfaceContact;
    public int surfaceContactCount;
    public Vector3 surfaceNormal;

    // World-up orientation helper. 1 = upright, 0 = sideways, -1 = fully inverted.
    public float uprightDot;
    public bool isInverted;
}

public struct CraftIntent
{
    public int commandFrame;

    public float throttle;
    public float brake;

    public float yawRequest;
    public float edgeShiftRequest;
    public float carveLeanRequest;
    public float pitchLeanRequest;

    public float manualRoofThrusterRequest;
    public float manualBottomThrusterRequest;

    public bool wantsGripBreaker;

    public bool wantsOvercharge;
    public bool overchargeReleased;
    public OverchargeTarget overchargeTarget;

    public bool stabilizerArmed;

    // True when the stabilizer owns Q/E vertical thruster control.
    // Manual roof/bottom requests should be blocked unless recoveryOverrideActive is true.
    public bool verticalThrustersLockedByStabilizer;

    // True when the craft is inverted enough that manual roof thrusters are allowed
    // to help lift/recover even if the stabilizer is armed.
    public bool recoveryOverrideActive;
}

public struct TractionState
{
    public static TractionState Normal => new TractionState
    {
        gripBreakerAmount = 0f,
        lateralGrip = 7f,
        longitudinalGrip = 6f,
        coastingGrip = 0.25f,
        carveBite = 1f,
        yawFreedom = 0f,
        steeringMultiplier = 1f,
        carveLeanMultiplier = 1f
    };

    public float gripBreakerAmount;
    public float lateralGrip;
    public float longitudinalGrip;
    public float coastingGrip;
    public float carveBite;
    public float yawFreedom;
    public float steeringMultiplier;
    public float carveLeanMultiplier;
}


public enum ThrusterPowerChannel
{
    Auto,
    BaseHover,
    Drive,
    Vectoring,
    Roof,
    Bottom,
    Overcharge,
    Stabilizer,
    Other
}

[System.Serializable]
public struct EnergyState
{
    public static EnergyState Full => new EnergyState
    {
        totalBudget = 100f,
        totalRequested = 0f,
        totalGranted = 0f,
        overload01 = 0f,

        baseHoverRequest = 0f,
        baseHoverGranted = 0f,
        driveRequest = 0f,
        vectoringRequest = 0f,
        roofRequest = 0f,
        bottomRequest = 0f,
        overchargeRequest = 0f,
        stabilizerRequest = 0f,
        otherRequest = 0f,

        driveGranted = 0f,
        vectoringGranted = 0f,
        roofGranted = 0f,
        bottomGranted = 0f,
        overchargeGranted = 0f,
        stabilizerGranted = 0f,
        otherGranted = 0f,

        baseHoverPower01 = 1f,
        drivePower01 = 1f,
        vectoringPower01 = 1f,
        roofPower01 = 1f,
        bottomPower01 = 1f,
        overchargePower01 = 1f,
        overchargeChargePower01 = 1f,
        overchargeBurstPower01 = 1f,
        stabilizerPower01 = 1f,
        otherPower01 = 1f,
        performancePowerScale01 = 1f,
        baseHoverProtected = true
    };

    public float totalBudget;
    public float totalRequested;
    public float totalGranted;
    public float overload01;

    public float baseHoverRequest;
    public float baseHoverGranted;
    public float driveRequest;
    public float vectoringRequest;
    public float roofRequest;
    public float bottomRequest;
    public float overchargeRequest;
    public float stabilizerRequest;
    public float otherRequest;

    public float driveGranted;
    public float vectoringGranted;
    public float roofGranted;
    public float bottomGranted;
    public float overchargeGranted;
    public float stabilizerGranted;
    public float otherGranted;

    public float baseHoverPower01;
    public float drivePower01;
    public float vectoringPower01;
    public float roofPower01;
    public float bottomPower01;
    public float overchargePower01;
    public float overchargeChargePower01;
    public float overchargeBurstPower01;
    public float stabilizerPower01;
    public float otherPower01;
    public float performancePowerScale01;

    // Base automatic hover can be protected outside the performance budget.
    // This keeps the craft from collapsing when the player steers/charges/boosts.
    public bool baseHoverProtected;
}


public struct HoverContact
{
    public bool isGrounded;
    public float distance;
    public Vector3 normal;
    public Vector3 point;
}
