using UnityEngine;

/// <summary>
/// Artist-facing controls stored directly on the propulsion placement rig.
/// Values are read continuously, including while Play Mode is running.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PropulsionWakeTuning : MonoBehaviour
{
    [Header("Thrusters Plasma Effect")]
    [Tooltip("Optional soft plasma texture used by every authored propulsion layer.")]
    public Texture2D plasmaTexture;

    [Tooltip("Main cyan plasma color from idle through normal thrust.")]
    [ColorUsage(true, true)] public Color plasmaColor =
        new Color(0.22f, 1.35f, 1.9f, 1f);

    [Tooltip("Plasma color reached while overcharge is being discharged. This is intentionally independent from the movement-memory color.")]
    [ColorUsage(true, true)] public Color overchargePlasmaColor =
        new Color(0.40f, 1.8f, 2.5f, 1f);

    [Tooltip("Brightness of the tight plasma body and nozzle source.")]
    [Range(0.1f, 3f)] public float plasmaBrightness = 1f;

    [Tooltip("How much longer the live plasma becomes at full overcharge.")]
    [Range(1f, 3f)] public float overchargeLengthMultiplier = 1.55f;

    [Tooltip("How much brighter and denser the live plasma becomes at full overcharge.")]
    [Range(1f, 3f)] public float overchargeIntensityMultiplier = 1.65f;

    [Tooltip("How quickly the propulsion visuals attack and release when throttle or overcharge changes.")]
    [Range(1f, 30f)] public float visualResponse = 12f;

    [Tooltip("Speed at which the speed-driven portion of the propulsion effect reaches its full cinematic response.")]
    [Min(100f)] public float fullEffectSpeedKmh = 1800f;

    [Tooltip("Width of the authored wake at idle.")]
    [Min(0.005f)] public float idleWakeWidth = 0.055f;
    [Tooltip("Width of the authored wake at ordinary cruise.")]
    [Min(0.05f)] public float cruiseWakeWidth = 0.34f;
    [Tooltip("Width reached by the authored wake during full overcharge.")]
    [Min(0.1f)] public float overchargeWakeWidth = 1.25f;
    [Range(0.05f, 1f)] public float cruiseWakeTime = 0.26f;
    [Range(0.1f, 2f)] public float overchargeWakeTime = 0.72f;

    [Tooltip("Width response used by any authored legacy thruster trails bound to the craft.")]
    [Range(1f, 5f)] public float authoredTrailOverchargeWidth = 3.4f;
    [Tooltip("Emission response used by authored thruster materials bound to the craft.")]
    [Range(1f, 8f)] public float authoredTrailOverchargeEmission = 4.8f;

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

    [Tooltip("Independent color for movement history. It is not recolored by overcharge.")]
    [ColorUsage(true, true)] public Color memoryColor =
        new Color(0.34f, 0.82f, 1.35f, 1f);

    [Tooltip("Opacity of the movement-memory ribbons without changing the live plasma.")]
    [Range(0f, 1f)] public float memoryOpacity = 0.62f;

    [Tooltip("How strongly overcharge widens the memory ribbon. Set to zero for completely independent memory.")]
    [Range(0f, 2f)] public float memoryOverchargeResponse = 0.25f;

    [Header("Plasma Path Echo")]
    [Tooltip("Adds a compact vector-plasma signature along the same movement path as the longer propulsion-memory ribbon.")]
    public bool followMemoryPath = true;

    [Tooltip("Master length of the vector-plasma trail as a fraction of the movement-memory path. The three response controls below scale this live value.")]
    [Range(0.05f, 1f)] public float plasmaEchoLength = 0.16f;

    [Tooltip("How much throttle extends the vector-plasma trail. Zero removes throttle from the length calculation.")]
    [Range(0f, 2f)] public float throttleTrailLengthResponse = 0.72f;

    [Tooltip("How much active Overcharge extends the vector-plasma trail. Zero removes boost from the length calculation.")]
    [Range(0f, 2f)] public float boostTrailLengthResponse = 1.25f;

    [Tooltip("How much Grip Break extends the vector-plasma trail while sliding. Zero removes Grip Break from the length calculation.")]
    [Range(0f, 2f)] public float gripBreakTrailLengthResponse = 0.82f;

    [Tooltip("Width of the cyan plasma echo. This does not change the longer memory ribbon.")]
    [Range(0.1f, 2f)] public float plasmaEchoWidth = 0.72f;

    [Tooltip("Visibility of the cyan plasma echo before overcharge response is added.")]
    [Range(0f, 1f)] public float plasmaEchoOpacity = 0.34f;

    [Tooltip("Extra brightness and width given to the plasma echo during overcharge.")]
    [Range(0f, 2.5f)] public float overchargeEchoIntensity = 1.55f;

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

    [Header("Exhaust Effect")]
    [Tooltip("Adds hot plasma fragments that peel away from each exhaust under load.")]
    public bool showPlasmaSparks = true;

    [Tooltip("Shows the compact cyan plasma source at each exhaust nozzle.")]
    [UnityEngine.Serialization.FormerlySerializedAs("showPlasmaChamber")]
    public bool showThrustersPlasmaEffect = true;

    [Tooltip("Moves the complete left plasma source, motes, and sparks assembly relative to the left exhaust pipe.")]
    [UnityEngine.Serialization.FormerlySerializedAs("leftPlasmaOffset")]
    public Vector3 leftExhaustEffectOffset = Vector3.zero;

    [Tooltip("Moves the complete right plasma source, motes, and sparks assembly relative to the right exhaust pipe.")]
    [UnityEngine.Serialization.FormerlySerializedAs("rightPlasmaOffset")]
    public Vector3 rightExhaustEffectOffset = Vector3.zero;

    [Tooltip("Aims the complete left plasma source, motes, and sparks assembly relative to the exhaust trail.")]
    public Vector3 leftExhaustEffectRotation = Vector3.zero;

    [Tooltip("Aims the complete right plasma source, motes, and sparks assembly relative to the exhaust trail.")]
    public Vector3 rightExhaustEffectRotation = Vector3.zero;

    [Tooltip("Additional placement for the compact thrusters plasma source only, after the master exhaust-effect offset.")]
    [UnityEngine.Serialization.FormerlySerializedAs("leftChamberOffset")]
    public Vector3 leftThrustersPlasmaOffset = Vector3.zero;
    [UnityEngine.Serialization.FormerlySerializedAs("rightChamberOffset")]
    public Vector3 rightThrustersPlasmaOffset = Vector3.zero;

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

    [Tooltip("HDR color of the compact energy visible at the exhaust source.")]
    [UnityEngine.Serialization.FormerlySerializedAs("plasmaChamberColor")]
    [ColorUsage(true, true)] public Color thrustersPlasmaColor =
        new Color(0.42f, 1.35f, 1.8f, 1f);

    [Tooltip("Physical size of the compact plasma source at the nozzle.")]
    [UnityEngine.Serialization.FormerlySerializedAs("chamberSize")]
    [Range(0.2f, 3f)] public float thrustersPlasmaSize = 1f;

    [Tooltip("Brightness and particle density of the compact plasma source.")]
    [UnityEngine.Serialization.FormerlySerializedAs("chamberIntensity")]
    [Range(0f, 3f)] public float thrustersPlasmaIntensity = 1f;

    [Tooltip("HDR head color of the expelled exhaust sparks.")]
    [ColorUsage(true, true)] public Color plasmaSparkColor =
        new Color(2.2f, 1.25f, 0.62f, 1f);

    [Tooltip("Number of expelled exhaust sparks. This is separate from the general detail density.")]
    [Range(0f, 3f)] public float sparkAmount = 1f;

    [Tooltip("Physical size of individual exhaust sparks.")]
    [Range(0.2f, 3f)] public float sparkSize = 1f;

    [Tooltip("How quickly sparks are expelled behind the craft.")]
    [Range(0.2f, 3f)] public float sparkSpeed = 1f;

    [Tooltip("How widely sparks scatter around the main exhaust beam.")]
    [Range(0.2f, 3f)] public float sparkSpread = 1f;

    [Tooltip("How long exhaust sparks remain visible.")]
    [Range(0.2f, 3f)] public float sparkLifetime = 1f;

    [Tooltip("Visual length of individual spark streaks, independent from the main exhaust streaks.")]
    [Range(0.2f, 3f)] public float sparkStreakLength = 1f;

    [Tooltip("Amount of short after-trail attached to sparks. Zero produces clean individual fragments.")]
    [Range(0f, 1f)] public float sparkTrailAmount = 0.55f;

    [Tooltip("Irregular sideways motion applied after sparks leave the exhaust.")]
    [Range(0.2f, 3f)] public float sparkTurbulence = 1f;

    [Tooltip("Extra spark intensity while overcharge is being discharged.")]
    [Range(0f, 3f)] public float overchargeSparkBoost = 1f;

    [Header("Spark Surface Contact")]
    [Tooltip("Lets expelled sparks collide with static roads and walls. Dynamic craft colliders are ignored to prevent self-collision.")]
    public bool sparkSurfaceCollision = true;

    [Tooltip("Layers containing road and wall colliders. Keep the hovercraft on a different/dynamic collider layer.")]
    public LayerMask sparkCollisionMask = ~0;

    [Range(0f, 1f)] public float sparkCollisionBounce = 0.12f;
    [Range(0f, 1f)] public float sparkCollisionDamping = 0.42f;
    [Range(0f, 1f)] public float sparkCollisionLifetimeLoss = 0.22f;
    public bool highQualitySparkCollision = true;

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
        plasmaBrightness = Mathf.Clamp(plasmaBrightness, 0.1f, 3f);
        overchargeLengthMultiplier = Mathf.Clamp(overchargeLengthMultiplier, 1f, 3f);
        overchargeIntensityMultiplier = Mathf.Clamp(overchargeIntensityMultiplier, 1f, 3f);
        visualResponse = Mathf.Clamp(visualResponse, 1f, 30f);
        fullEffectSpeedKmh = Mathf.Max(100f, fullEffectSpeedKmh);
        idleWakeWidth = Mathf.Max(0.005f, idleWakeWidth);
        cruiseWakeWidth = Mathf.Max(0.05f, cruiseWakeWidth);
        overchargeWakeWidth = Mathf.Max(0.1f, overchargeWakeWidth);
        cruiseWakeTime = Mathf.Clamp(cruiseWakeTime, 0.05f, 1f);
        overchargeWakeTime = Mathf.Clamp(overchargeWakeTime, 0.1f, 2f);
        authoredTrailOverchargeWidth = Mathf.Clamp(authoredTrailOverchargeWidth, 1f, 5f);
        authoredTrailOverchargeEmission = Mathf.Clamp(authoredTrailOverchargeEmission, 1f, 8f);
        memoryOpacity = Mathf.Clamp01(memoryOpacity);
        memoryOverchargeResponse = Mathf.Clamp(memoryOverchargeResponse, 0f, 2f);
        plasmaEchoLength = Mathf.Clamp(plasmaEchoLength, 0.05f, 1f);
        plasmaEchoWidth = Mathf.Clamp(plasmaEchoWidth, 0.1f, 2f);
        plasmaEchoOpacity = Mathf.Clamp01(plasmaEchoOpacity);
        overchargeEchoIntensity = Mathf.Clamp(overchargeEchoIntensity, 0f, 2.5f);
        thrustersPlasmaSize = Mathf.Clamp(thrustersPlasmaSize, 0.2f, 3f);
        thrustersPlasmaIntensity = Mathf.Clamp(thrustersPlasmaIntensity, 0f, 3f);
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
        sparkCollisionBounce = Mathf.Clamp01(sparkCollisionBounce);
        sparkCollisionDamping = Mathf.Clamp01(sparkCollisionDamping);
        sparkCollisionLifetimeLoss = Mathf.Clamp01(sparkCollisionLifetimeLoss);
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
