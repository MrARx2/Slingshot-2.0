using UnityEngine;

/// <summary>
/// Artist-facing controls stored directly on the propulsion placement rig.
/// Values are read continuously, including while Play Mode is running.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PropulsionWakeTuning : MonoBehaviour
{
    [Header("Trail Length")]
    [Tooltip("Master multiplier applied to every propulsion trail layer.")]
    [Range(0.1f, 4f)] public float overallLength = 1f;

    [Tooltip("Length of the dense white-hot core and colored exhaust body.")]
    [Range(0.1f, 3f)] public float exhaustBodyLength = 1f;

    [Tooltip("Length of the fast, fine ion filaments around the main exhaust.")]
    [Range(0.1f, 3f)] public float filamentLength = 1f;

    [Tooltip("How long the thin movement-history trails linger behind the craft.")]
    [Range(0.1f, 3f)] public float memoryLength = 1f;

    [Header("Long Trail Control")]
    [Tooltip("Turns off only the long movement-history ribbons. The detailed nozzle particles remain visible.")]
    public bool showMemoryRibbons = true;

    [Tooltip("Hard world-space length cap for each memory ribbon, independent of vehicle speed.")]
    [Range(0.5f, 60f)] public float maximumMemoryLengthMeters = 12f;

    [Tooltip("Higher values create a smoother ribbon. Lower values use fewer trail vertices.")]
    [Range(0f, 1f)] public float memorySmoothness = 0.72f;

    [Tooltip("Clears accumulated ribbon history if any part wraps around in front of the craft.")]
    public bool preventForwardWrap = true;

    [Tooltip("Small forward allowance before wrapped history is cleared.")]
    [Range(0f, 4f)] public float forwardWrapAllowance = 0.35f;

    [Tooltip("Clears the history during a very sharp one-frame direction change, preventing chords across loops and crashes.")]
    public bool clearOnSharpDirectionChange = true;

    [Tooltip("One-frame craft rotation that clears the memory ribbon.")]
    [Range(10f, 120f)] public float directionChangeClearAngle = 52f;

    [Tooltip("One-frame movement treated as a teleport and cleared instead of drawing a line through the level.")]
    [Range(1f, 100f)] public float teleportClearDistance = 18f;

    [Header("Trail Shape")]
    [HideInInspector]
    public Vector3 leftEffectOffset = Vector3.zero;

    [HideInInspector]
    public Vector3 rightEffectOffset = Vector3.zero;

    [Tooltip("Moves stretched particles toward one end of their billboard so the bright streak begins at the nozzle instead of straddling it. Start at 0.5.")]
    [Range(-1f, 1f)] public float stretchedParticlePivot = 0.5f;

    [Tooltip("Master thickness for the complete effect without changing anchor spacing.")]
    [Range(0.25f, 2.5f)] public float width = 1f;

    [Header("Layer Size")] 
    [Tooltip("Thickness of the tight white-hot exhaust center.")]
    [Range(0.2f, 3f)] public float coreSize = 1f;

    [Tooltip("Thickness of the colored plasma body around the core.")]
    [Range(0.2f, 3f)] public float plumeSize = 1f;

    [Tooltip("Thickness of the fast fine streaks surrounding the exhaust.")]
    [Range(0.2f, 3f)] public float filamentSize = 1f;

    [Tooltip("Width of the long movement-history ribbons only.")]
    [Range(0.2f, 3f)] public float ribbonSize = 1f;

    [Tooltip("Visual elongation of stretched particles. This changes streak length without changing particle lifetime or trail memory.")]
    [Range(0.2f, 3f)] public float particleStretch = 1f;

    [Header("Independent Exhaust Layer Placement")]
    [Tooltip("Shows the tight white-hot center emitted by both nozzles.")]
    public bool showHotCore = true;

    [Tooltip("Shows the colored plasma body emitted by both nozzles.")]
    public bool showPlasmaPlume = true;

    [Tooltip("Shows the fine high-speed ion streaks emitted by both nozzles.")]
    public bool showIonFilaments = true;

    [Tooltip("Local placement correction for the left white-hot core only.")]
    public Vector3 leftCoreOffset = Vector3.zero;
    public Vector3 rightCoreOffset = Vector3.zero;
    public Vector3 leftCoreRotation = Vector3.zero;
    public Vector3 rightCoreRotation = Vector3.zero;

    [Tooltip("Local placement correction for the left colored plume only.")]
    public Vector3 leftPlumeOffset = Vector3.zero;
    public Vector3 rightPlumeOffset = Vector3.zero;
    public Vector3 leftPlumeRotation = Vector3.zero;
    public Vector3 rightPlumeRotation = Vector3.zero;

    [Tooltip("Local placement correction for the fine ion filaments only.")]
    public Vector3 leftFilamentOffset = Vector3.zero;
    public Vector3 rightFilamentOffset = Vector3.zero;
    public Vector3 leftFilamentRotation = Vector3.zero;
    public Vector3 rightFilamentRotation = Vector3.zero;

    [Tooltip("Local placement correction for the long memory ribbon emitter only.")]
    public Vector3 leftRibbonOffset = Vector3.zero;
    public Vector3 rightRibbonOffset = Vector3.zero;
    public Vector3 leftRibbonRotation = Vector3.zero;
    public Vector3 rightRibbonRotation = Vector3.zero;

    [Header("Plasma Sparks")]
    [Tooltip("Adds hot plasma fragments that peel away from each exhaust under load.")]
    public bool showPlasmaSparks = true;

    [Tooltip("Shows the compact plasma glow sitting inside each exhaust pipe.")]
    public bool showPlasmaChamber = true;

    [Tooltip("Fine local correction for the left plasma chamber and sparks only. XYZ follows the left plasma socket axes.")]
    public Vector3 leftPlasmaOffset = Vector3.zero;

    [Tooltip("Fine local correction for the right plasma chamber and sparks only. XYZ follows the right plasma socket axes.")]
    public Vector3 rightPlasmaOffset = Vector3.zero;

    [Tooltip("Additional placement for the compact pipe chamber only, after the master plasma offset.")]
    public Vector3 leftChamberOffset = Vector3.zero;
    public Vector3 rightChamberOffset = Vector3.zero;

    [Tooltip("Additional placement for the loose plasma motes only.")]
    public Vector3 leftMoteOffset = Vector3.zero;
    public Vector3 rightMoteOffset = Vector3.zero;
    public Vector3 leftMoteRotation = Vector3.zero;
    public Vector3 rightMoteRotation = Vector3.zero;

    [Tooltip("Shows the loose plasma fragments around the main sparks.")]
    public bool showPlasmaMotes = true;
    [Range(0f, 3f)] public float moteAmount = 1f;
    [Range(0.2f, 3f)] public float moteSize = 1f;
    [Range(0.2f, 3f)] public float moteSpeed = 1f;
    [Range(0.2f, 3f)] public float moteSpread = 1f;
    [Range(0.2f, 3f)] public float moteLifetime = 1f;
    [Range(0.2f, 3f)] public float moteTurbulence = 1f;

    [Tooltip("Additional placement for expelled sparks only.")]
    public Vector3 leftSparkOffset = Vector3.zero;
    public Vector3 rightSparkOffset = Vector3.zero;

    [Tooltip("Independent spark ejection direction in local Euler angles.")]
    public Vector3 leftSparkRotation = Vector3.zero;
    public Vector3 rightSparkRotation = Vector3.zero;

    [Tooltip("HDR color of the compact energy visible inside the exhaust pipe.")]
    [ColorUsage(true, true)] public Color plasmaChamberColor =
        new Color(0.42f, 1.35f, 1.8f, 1f);

    [Tooltip("Physical size of the compact plasma chamber inside the pipe.")]
    [Range(0.2f, 3f)] public float chamberSize = 1f;

    [Tooltip("Brightness and particle density of the plasma chamber.")]
    [Range(0f, 3f)] public float chamberIntensity = 1f;

    [Tooltip("HDR head color of expelled plasma sparks.")]
    [ColorUsage(true, true)] public Color plasmaSparkColor =
        new Color(2.2f, 1.25f, 0.62f, 1f);

    [Tooltip("Number of plasma sparks. This is separate from the general detail density.")]
    [Range(0f, 3f)] public float sparkAmount = 1f;

    [Tooltip("Physical size of individual plasma sparks.")]
    [Range(0.2f, 3f)] public float sparkSize = 1f;

    [Tooltip("How quickly sparks are expelled behind the craft.")]
    [Range(0.2f, 3f)] public float sparkSpeed = 1f;

    [Tooltip("How widely sparks scatter around the main exhaust beam.")]
    [Range(0.2f, 3f)] public float sparkSpread = 1f;

    [Tooltip("How long plasma sparks remain visible.")]
    [Range(0.2f, 3f)] public float sparkLifetime = 1f;

    [Tooltip("Visual length of individual spark streaks, independent from the main exhaust streaks.")]
    [Range(0.2f, 3f)] public float sparkStreakLength = 1f;

    [Tooltip("Amount of short after-trail attached to sparks. Zero produces clean individual fragments.")]
    [Range(0f, 1f)] public float sparkTrailAmount = 0.55f;

    [Tooltip("Irregular sideways motion applied after sparks leave the exhaust.")]
    [Range(0.2f, 3f)] public float sparkTurbulence = 1f;

    [Tooltip("Extra spark intensity while overcharge is being discharged.")]
    [Range(0f, 3f)] public float overchargeSparkBoost = 1f;

    [Tooltip("Radius of the area sparks are born across at the pipe opening.")]
    [Range(0.05f, 3f)] public float sparkSpawnRadius = 1f;

    [Tooltip("Variation between the slowest and fastest expelled sparks.")]
    [Range(0f, 1f)] public float sparkSpeedVariation = 0.65f;

    [Tooltip("Drag applied after emission. Higher values keep sparks near the exhaust.")]
    [Range(0f, 3f)] public float sparkDrag = 0.25f;

    [Tooltip("Local gravity applied to sparks. Negative values make them rise.")]
    [Range(-2f, 2f)] public float sparkGravity = 0f;

    [Tooltip("Lifetime multiplier for the tiny after-trails behind spark particles.")]
    [Range(0.1f, 3f)] public float sparkTrailLifetime = 1f;

    [Tooltip("Width multiplier for the tiny after-trails behind spark particles.")]
    [Range(0.1f, 3f)] public float sparkTrailWidth = 1f;

    [Tooltip("How many sparks remain visible at idle before throttle is applied.")]
    [Range(0f, 3f)] public float idleSparkPresence = 1f;

    [Tooltip("How strongly ordinary engine load increases spark output.")]
    [Range(0f, 3f)] public float throttleSparkResponse = 1f;

    [Tooltip("Strength of the one-shot spark spray when overcharge ignites.")]
    [Range(0f, 3f)] public float ignitionSparkBurst = 1f;

    [Tooltip("Speed of irregular emission flicker.")]
    [Range(0.1f, 4f)] public float sparkFlickerSpeed = 1f;

    [Tooltip("Frequency/detail of sideways turbulent motion.")]
    [Range(0.2f, 3f)] public float sparkTurbulenceFrequency = 1f;

    [Tooltip("Number of exhaust particles. Lower this before reducing quality elsewhere.")]
    [Range(0.2f, 2f)] public float particleDensity = 1f;

    private void OnValidate()
    {
        // Authored nozzle transforms are now the only source of placement truth.
        // Clear values serialized by the retired post-anchor offset workflow.
        leftEffectOffset = Vector3.zero;
        rightEffectOffset = Vector3.zero;
        overallLength = Mathf.Clamp(overallLength, 0.1f, 4f);
        exhaustBodyLength = Mathf.Clamp(exhaustBodyLength, 0.1f, 3f);
        filamentLength = Mathf.Clamp(filamentLength, 0.1f, 3f);
        memoryLength = Mathf.Clamp(memoryLength, 0.1f, 3f);
        maximumMemoryLengthMeters = Mathf.Clamp(maximumMemoryLengthMeters, 0.5f, 60f);
        memorySmoothness = Mathf.Clamp01(memorySmoothness);
        forwardWrapAllowance = Mathf.Clamp(forwardWrapAllowance, 0f, 4f);
        directionChangeClearAngle = Mathf.Clamp(directionChangeClearAngle, 10f, 120f);
        teleportClearDistance = Mathf.Clamp(teleportClearDistance, 1f, 100f);
        width = Mathf.Clamp(width, 0.25f, 2.5f);
        coreSize = Mathf.Clamp(coreSize, 0.2f, 3f);
        plumeSize = Mathf.Clamp(plumeSize, 0.2f, 3f);
        filamentSize = Mathf.Clamp(filamentSize, 0.2f, 3f);
        ribbonSize = Mathf.Clamp(ribbonSize, 0.2f, 3f);
        particleStretch = Mathf.Clamp(particleStretch, 0.2f, 3f);
        chamberSize = Mathf.Clamp(chamberSize, 0.2f, 3f);
        chamberIntensity = Mathf.Clamp(chamberIntensity, 0f, 3f);
        moteAmount = Mathf.Clamp(moteAmount, 0f, 3f);
        moteSize = Mathf.Clamp(moteSize, 0.2f, 3f);
        moteSpeed = Mathf.Clamp(moteSpeed, 0.2f, 3f);
        moteSpread = Mathf.Clamp(moteSpread, 0.2f, 3f);
        moteLifetime = Mathf.Clamp(moteLifetime, 0.2f, 3f);
        moteTurbulence = Mathf.Clamp(moteTurbulence, 0.2f, 3f);
        sparkAmount = Mathf.Clamp(sparkAmount, 0f, 3f);
        sparkSize = Mathf.Clamp(sparkSize, 0.2f, 3f);
        sparkSpeed = Mathf.Clamp(sparkSpeed, 0.2f, 3f);
        sparkSpread = Mathf.Clamp(sparkSpread, 0.2f, 3f);
        sparkLifetime = Mathf.Clamp(sparkLifetime, 0.2f, 3f);
        sparkStreakLength = Mathf.Clamp(sparkStreakLength, 0.2f, 3f);
        sparkTrailAmount = Mathf.Clamp01(sparkTrailAmount);
        sparkTurbulence = Mathf.Clamp(sparkTurbulence, 0.2f, 3f);
        overchargeSparkBoost = Mathf.Clamp(overchargeSparkBoost, 0f, 3f);
        sparkSpawnRadius = Mathf.Clamp(sparkSpawnRadius, 0.05f, 3f);
        sparkSpeedVariation = Mathf.Clamp01(sparkSpeedVariation);
        sparkDrag = Mathf.Clamp(sparkDrag, 0f, 3f);
        sparkGravity = Mathf.Clamp(sparkGravity, -2f, 2f);
        sparkTrailLifetime = Mathf.Clamp(sparkTrailLifetime, 0.1f, 3f);
        sparkTrailWidth = Mathf.Clamp(sparkTrailWidth, 0.1f, 3f);
        idleSparkPresence = Mathf.Clamp(idleSparkPresence, 0f, 3f);
        throttleSparkResponse = Mathf.Clamp(throttleSparkResponse, 0f, 3f);
        ignitionSparkBurst = Mathf.Clamp(ignitionSparkBurst, 0f, 3f);
        sparkFlickerSpeed = Mathf.Clamp(sparkFlickerSpeed, 0.1f, 4f);
        sparkTurbulenceFrequency = Mathf.Clamp(sparkTurbulenceFrequency, 0.2f, 3f);
        particleDensity = Mathf.Clamp(particleDensity, 0.2f, 2f);
    }
}
