using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// <b>V2 Craft Feedback System.</b>
/// <para>
/// Manages the visual and feedback layer of the hovercraft, including:
/// <list type="bullet">
///   <item>Grip Breaker indicator cube emission (blue normal, red active).</item>
///   <item>Thruster node trails and glow indicator spheres.</item>
///   <item>Optional engine/thruster glows and hover pulses.</item>
/// </list>
/// </para>
/// <para>
/// This component is purely visual and executes in <c>Update()</c>. It contains
/// no physics logic and does not modify craft movement.
/// </para>
/// </summary>
public class CraftFeedbackSystem : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  THRUSTER VISUAL BINDING STRUCT
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Binds a physical <see cref="ThrusterNode"/> to its visual representations.
    /// </summary>
    [System.Serializable]
    public class ThrusterVisualBinding
    {
        [Tooltip("Reference to the physical ThrusterNode component.")]
        public ThrusterNode node;

        [Tooltip("Optional label matching the ThrusterNode label if node is null.")]
        public string label;

        [Tooltip("Optional TrailRenderer attached to this thruster.")]
        public TrailRenderer trailRenderer;

        [Tooltip("Optional sphere GameObject that lights up when the thruster is firing.")]
        public GameObject indicatorSphere;

        [HideInInspector] public Renderer indicatorRenderer;
        [HideInInspector]
        [System.NonSerialized]
        public MaterialPropertyBlock propBlock;
        [HideInInspector] public float originalTrailWidth;
        [HideInInspector] public ThrusterNode resolvedNode;
    }

    // Retained for authored TrailRenderer bindings; the hero wake itself is particle-based.
    private sealed class WakeLayer
    {
        public Transform anchor;
        public TrailRenderer trail;
        public LineRenderer plume;
        public Material material;
        public float lateral;
        public float vertical;
        public float widthScale;
        public float timeScale;
        public float phase;
        public bool overchargeOnly;
        public float smoothedEnergy;
        public float lastColorBlend = -1f;
    }

    // ══════════════════════════════════════════════════════════════
    //  CONFIGURATION — GRIP BREAKER INDICATOR
    // ══════════════════════════════════════════════════════════════

    [Header("Grip Breaker Visuals")]
    [Tooltip("Renderer used as the Grip Breaker visual indicator cube.")]
    public Renderer gripBreakerIndicatorRenderer;

    [Tooltip("Emission color when Grip Breaker is inactive (normal mode).")]
    [ColorUsage(true, true)]
    public Color gripBreakerNormalEmission = Color.blue;

    [Tooltip("Emission color when Grip Breaker is active (drift mode).")]
    [ColorUsage(true, true)]
    public Color gripBreakerActiveEmission = Color.red;

    [Tooltip("Intensity multiplier for the emissive color (for bloom effects).")]
    public float gripBreakerEmissionIntensity = 2f;

    [Tooltip("If true, visual color transitions smoothly based on gripBreakerAmount. If false, it snaps instantly.")]
    public bool gripBreakerUseAnalogBlend = true;

    // ══════════════════════════════════════════════════════════════
    //  CONFIGURATION — THRUSTER VISUALS
    // ══════════════════════════════════════════════════════════════

    [Header("Thruster Visual Bindings")]
    [Tooltip("List of bindings connecting thruster nodes to their trail renderers and indicators.")]
    public List<ThrusterVisualBinding> thrusterVisuals = new List<ThrusterVisualBinding>();

    [Header("Boost Visual Response")]
    [Tooltip("Main-engine trail width multiplier at the crest of a boost.")]
    [Range(1f, 5f)] public float boostTrailWidthMultiplier = 3.4f;

    [Tooltip("Additional main-engine emission multiplier at the crest of a boost.")]
    [Range(1f, 8f)] public float boostEmissionMultiplier = 4.8f;

    [Header("Cinematic Propulsion Wake")]
    [Tooltip("Automatically creates a layered rear-engine wake when no authored main-engine trail is assigned.")]
    public bool autoBuildCinematicWake = true;
    [Tooltip("Disables the two original Trail prefab renderers so only the cinematic propulsion system is visible.")]
    public bool disableLegacyTrailRenderers = true;
    [Tooltip("Soft additive plasma sprite used by the nozzle, ion, spark, shock, and ghost-wake layers.")]
    public Texture2D plasmaWakeTexture;
    [Min(0.005f)] public float idleWakeWidth = 0.055f;
    [Min(0.05f)] public float cruiseWakeWidth = 0.34f;
    [Min(0.1f)] public float overchargeWakeWidth = 1.25f;
    [Range(0.05f, 1f)] public float cruiseWakeTime = 0.26f;
    [Range(0.1f, 2f)] public float overchargeWakeTime = 0.72f;
    [Min(100f)] public float cinematicSpeedKmh = 1800f;
    [Tooltip("Optional placement prefab root. When assigned (or found below this craft), its two named anchors override the fallback offsets.")]
    public Transform propulsionWakePlacementRoot;
    [Tooltip("Optional separate prefab for plasma motes and sparks. Its sockets do not move the clean core trails.")]
    public Transform plasmaEffectsPlacementRoot;
    [Tooltip("Fallback only: left exhaust outlet relative to Main_Rear when no placement rig is present.")]
    public Vector3 leftNozzleOffset = new Vector3(-0.52f, 0.04f, -0.08f);
    [Tooltip("Fallback only: right exhaust outlet relative to Main_Rear when no placement rig is present.")]
    public Vector3 rightNozzleOffset = new Vector3(0.52f, 0.04f, -0.08f);
    [ColorUsage(true, true)] public Color idleWakeColor = new Color(0.28f, 0.72f, 0.66f, 1f);
    [ColorUsage(true, true)] public Color cruiseWakeColor = new Color(0.40f, 1.15f, 1.45f, 1f);
    [ColorUsage(true, true)] public Color overchargeCoreColor = new Color(2.2f, 2.0f, 1.65f, 1f);
    [ColorUsage(true, true)] public Color overchargeTailColor = new Color(0.82f, 0.34f, 1.55f, 1f);
    [ColorUsage(true, true)] public Color overchargeEdgeColor = new Color(1.35f, 0.16f, 0.55f, 1f);

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ══════════════════════════════════════════════════════════════

    private Material _gripBreakerIndicatorMaterial;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private ThrusterBus _thrusterBus;
    private OverchargeCore _boostCore;
    private ThrusterNode _wakeEngine;
    private Material _wakeMaterial;
    private Material _coreWakeMaterial;
    private Material _filamentWakeMaterial;
    private Material _memoryWakeMaterial;
    private Material _sparkWakeMaterial;
    private Texture2D _fallbackWakeTexture;
    private readonly List<WakeLayer> _wakeLayers = new List<WakeLayer>();
    private Transform _wakeRoot;
    private Transform _leftWakeProxy;
    private Transform _rightWakeProxy;
    private Transform _leftPlasmaProxy;
    private Transform _rightPlasmaProxy;
    private Transform _leftAuthoredNozzle;
    private Transform _rightAuthoredNozzle;
    private Transform _leftAuthoredPlasma;
    private Transform _rightAuthoredPlasma;
    private PropulsionWakeTuning _wakeTuning;
    private TrailRenderer _ghostLeft;
    private TrailRenderer _ghostRight;
    private ParticleSystem _coreLeft;
    private ParticleSystem _coreRight;
    private ParticleSystem _nozzleLeft;
    private ParticleSystem _nozzleRight;
    private ParticleSystem _ionLeft;
    private ParticleSystem _ionRight;
    private ParticleSystem _motesLeft;
    private ParticleSystem _motesRight;
    private ParticleSystem _chamberLeft;
    private ParticleSystem _chamberRight;
    private ParticleSystem _sparksLeft;
    private ParticleSystem _sparksRight;
    private ParticleSystem _shockBurst;
    private Light _wakeLight;
    private int _lastWakeSequence = int.MinValue;
    private float _ignitionFlash;
    private float _nextWakeAnchorSearchTime;
    private bool _usingAuthoredWakePlacement;
    private bool _wakeMotionInitialized;
    private Vector3 _previousWakePosition;
    private Vector3 _previousWakeForward;

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _thrusterBus = GetComponentInParent<ThrusterBus>();
        _boostCore = GetComponentInParent<OverchargeCore>();
    }

    private void Start()
    {
        // ── Initialize Grip Breaker Material ───────────────────────
        if (gripBreakerIndicatorRenderer != null)
        {
            // Instantiate material so we don't modify the shared asset
            _gripBreakerIndicatorMaterial = gripBreakerIndicatorRenderer.material;
            _gripBreakerIndicatorMaterial.EnableKeyword("_EMISSION");
        }

        // ── Initialize Thruster Visuals ────────────────────────────
        foreach (var visual in thrusterVisuals)
        {
            if (visual == null) continue;

            // Resolve node reference if not explicitly set
            if (visual.node != null)
            {
                visual.resolvedNode = visual.node;
            }
            else if (!string.IsNullOrEmpty(visual.label) && _thrusterBus != null)
            {
                visual.resolvedNode = _thrusterBus.FindByLabel(visual.label)?.node;
            }

            // Set up TrailRenderer
            if (visual.trailRenderer != null)
            {
                visual.originalTrailWidth = visual.trailRenderer.startWidth;
                visual.trailRenderer.emitting = false;
            }

            // Set up Indicator Sphere
            if (visual.indicatorSphere != null)
            {
                visual.indicatorRenderer = visual.indicatorSphere.GetComponent<Renderer>();
                if (visual.indicatorRenderer != null)
                {
                    visual.propBlock = new MaterialPropertyBlock();
                }
            }
        }

        BuildCinematicWakeIfNeeded();
    }

    private void OnDestroy()
    {
        // Clean up instantiated material
        if (_gripBreakerIndicatorMaterial != null)
        {
            Destroy(_gripBreakerIndicatorMaterial);
        }

        if (_wakeMaterial != null)
            Destroy(_wakeMaterial);
        if (_coreWakeMaterial != null)
            Destroy(_coreWakeMaterial);
        if (_filamentWakeMaterial != null)
            Destroy(_filamentWakeMaterial);
        if (_memoryWakeMaterial != null)
            Destroy(_memoryWakeMaterial);
        if (_sparkWakeMaterial != null)
            Destroy(_sparkWakeMaterial);
        if (_fallbackWakeTexture != null)
            Destroy(_fallbackWakeTexture);
    }

    private void OnDrawGizmos()
    {
        Transform placementRoot = propulsionWakePlacementRoot != null
            ? propulsionWakePlacementRoot
            : FindDescendant(transform, "Propulsion Wake Placement Rig");
        DrawWakeAnchorGizmo(FindDescendant(placementRoot,
            "LEFT EXHAUST - PLACE ON NOZZLE"), new Color(0.15f, 0.95f, 1f, 1f),
            Vector3.zero);
        DrawWakeAnchorGizmo(FindDescendant(placementRoot,
            "RIGHT EXHAUST - PLACE ON NOZZLE"), new Color(0.78f, 0.35f, 1f, 1f),
            Vector3.zero);

        Transform plasmaRoot = plasmaEffectsPlacementRoot != null
            ? plasmaEffectsPlacementRoot
            : FindDescendantStartingWith(transform, "PlasmaFX_Rig");
        PropulsionWakeTuning gizmoTuning = placementRoot != null
            ? placementRoot.GetComponent<PropulsionWakeTuning>()
            : null;
        DrawWakeAnchorGizmo(FindDescendant(plasmaRoot,
            "LEFT PLASMA FX - PLACE ON NOZZLE"), new Color(1f, 0.32f, 0.58f, 1f),
            gizmoTuning != null ? gizmoTuning.leftPlasmaOffset : Vector3.zero);
        DrawWakeAnchorGizmo(FindDescendant(plasmaRoot,
            "RIGHT PLASMA FX - PLACE ON NOZZLE"), new Color(1f, 0.62f, 0.18f, 1f),
            gizmoTuning != null ? gizmoTuning.rightPlasmaOffset : Vector3.zero);
    }

    private static void DrawWakeAnchorGizmo(Transform anchor, Color color,
        Vector3 localOffset)
    {
        if (anchor == null)
            return;

        const float length = 0.34f;
        Vector3 origin = anchor.position + anchor.TransformVector(localOffset);
        Vector3 tip = origin + anchor.forward * length;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(origin, 0.065f);
        Gizmos.DrawLine(origin, tip);
        Gizmos.DrawLine(tip, tip - anchor.forward * 0.09f + anchor.right * 0.05f);
        Gizmos.DrawLine(tip, tip - anchor.forward * 0.09f - anchor.right * 0.05f);
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates all visual indicators and thruster effects. Called by CraftCore in Update().
    /// </summary>
    public void UpdateVisuals(PilotCommand command, CraftIntent intent, CraftTelemetry telemetry, TractionState traction)
    {
        // ── 1. Update Grip Breaker Indicator ────────────────────────
        UpdateGripBreakerVisuals(command, intent, telemetry, traction);

        // ── 2. Update Thruster Node Visuals ─────────────────────────
        UpdateThrusterVisuals();
        // Keep the layered propulsion wake synchronized with live craft telemetry.
        UpdateParticleWake(telemetry);
    }

    // ══════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════

    private void UpdateGripBreakerVisuals(PilotCommand command, CraftIntent intent, CraftTelemetry telemetry, TractionState traction)
    {
        if (_gripBreakerIndicatorMaterial == null) return;

        // Analog mode follows the actual TractionCore blend, including recovery fade.
        // Snap mode still follows the held input for instant visual feedback.
        float blendFactor = gripBreakerUseAnalogBlend
            ? Mathf.Clamp01(traction.gripBreakerAmount)
            : (command.gripBreakerHeld ? 1f : 0f);

        Color baseEmission = Color.Lerp(
            gripBreakerNormalEmission,
            gripBreakerActiveEmission,
            blendFactor
        );

        Color emissionColor = baseEmission * gripBreakerEmissionIntensity;

        _gripBreakerIndicatorMaterial.SetColor(EmissionColorID, emissionColor);

        // Update main albedo color if present
        if (_gripBreakerIndicatorMaterial.HasProperty("_BaseColor"))
        {
            _gripBreakerIndicatorMaterial.SetColor("_BaseColor", baseEmission);
        }
        else if (_gripBreakerIndicatorMaterial.HasProperty("_Color"))
        {
            _gripBreakerIndicatorMaterial.SetColor("_Color", baseEmission);
        }
    }

    private void UpdateThrusterVisuals()
    {
        foreach (var visual in thrusterVisuals)
        {
            if (visual == null || visual.resolvedNode == null) continue;

            float throttle = visual.resolvedNode.Throttle;
            float boost = visual.resolvedNode.role == ThrusterNode.ThrusterRole.Main && _boostCore != null
                ? _boostCore.CameraBoost01
                : 0f;

            // ── Update Trail Renderer ──────────────────────────────
            if (visual.trailRenderer != null)
            {
                float activeThreshold = 0.05f;
                if (throttle > activeThreshold)
                {
                    float cappedThrottle = Mathf.Clamp01(throttle);
                    visual.trailRenderer.emitting = true;
                    float boostWidth = Mathf.Lerp(1f, boostTrailWidthMultiplier, boost);
                    visual.trailRenderer.startWidth = visual.originalTrailWidth * cappedThrottle * boostWidth;
                }
                else
                {
                    visual.trailRenderer.emitting = false;
                }
            }

            // ── Update Indicator Sphere ─────────────────────────────
            if (visual.indicatorRenderer != null && visual.propBlock != null)
            {
                GetRoleColors(visual.resolvedNode.role, out Color idleColor, out Color activeColor);

                float t = Mathf.Clamp01(throttle / 0.8f); // fully glowing at 80% throttle
                Color baseColor = Color.Lerp(idleColor, activeColor, t);

                // Emission scales up with throttle
                float boostEmission = Mathf.Lerp(1f, boostEmissionMultiplier, boost);
                Color emissiveColor = throttle > 0.05f
                    ? activeColor * (t * 2.5f * boostEmission) // HDR boost
                    : Color.black;

                visual.indicatorRenderer.GetPropertyBlock(visual.propBlock);
                visual.propBlock.SetColor("_BaseColor", baseColor);
                visual.propBlock.SetColor("_EmissionColor", emissiveColor);
                visual.indicatorRenderer.SetPropertyBlock(visual.propBlock);
            }
        }
    }

    private void BuildCinematicWakeIfNeeded()
    {
        if (!autoBuildCinematicWake || _wakeRoot != null)
            return;

        DisableLegacyWakeTrails();

        // Survive disabled-domain-reload Play Mode and accidental duplicate initialization.
        // A craft may own exactly one generated propulsion hierarchy.
        List<GameObject> staleWakeObjects = new List<GameObject>();
        for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
        {
            Transform child = transform.GetChild(childIndex);
            if (child.name.StartsWith("Cinematic Propulsion Wake") ||
                child.name.StartsWith("CinematicWake_"))
                staleWakeObjects.Add(child.gameObject);
        }
        foreach (GameObject staleWakeObject in staleWakeObjects)
        {
            if (Application.isPlaying)
                Destroy(staleWakeObject);
            else
                DestroyImmediate(staleWakeObject);
        }

        foreach (ThrusterVisualBinding visual in thrusterVisuals)
        {
            if (visual == null || visual.resolvedNode == null ||
                visual.resolvedNode.role != ThrusterNode.ThrusterRole.Main)
                continue;

            _wakeEngine = visual.resolvedNode;
            break;
        }

        if (_wakeEngine == null && _thrusterBus != null)
        {
            ThrusterBus.ThrusterNodeEntry entry = _thrusterBus.FindByLabel("Main_Rear");
            _wakeEngine = entry != null ? entry.node : null;
        }

        if (_wakeEngine == null)
            return;

        _lastWakeSequence = _boostCore != null ? _boostCore.BoostSequenceId : 0;
        // Build once after the main rear engine has been resolved.
        BuildParticleWake();
    }

    private void DisableLegacyWakeTrails()
    {
        if (!disableLegacyTrailRenderers)
            return;

        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);
        foreach (TrailRenderer trail in trails)
        {
            if (trail == null)
                continue;

            string trailName = trail.gameObject.name;
            bool isLegacyTrail = trailName == "Trail" ||
                trailName.StartsWith("Trail (") ||
                trailName.StartsWith("Legacy Trail");
            if (!isLegacyTrail)
                continue;

            trail.Clear();
            trail.emitting = false;
            trail.enabled = false;
        }
    }

    private void BuildParticleWake()
    {
        ResolveWakeAnchors();
        _usingAuthoredWakePlacement = HasAuthoredWakePlacement();
        GameObject wakeRootObject = new GameObject(_usingAuthoredWakePlacement
            ? "Cinematic Propulsion Wake [AUTHORED ANCHORS]"
            : "Cinematic Propulsion Wake [FALLBACK OFFSETS]");
        _wakeRoot = wakeRootObject.transform;
        _wakeRoot.SetParent(transform, false);

        _wakeMaterial = CreateWakeMaterial(
            "Plasma Body", cruiseWakeColor, overchargeTailColor,
            1.55f, 7.5f, 1.65f, 2.4f, 0.045f, 0.72f);
        _coreWakeMaterial = CreateWakeMaterial(
            "White-Hot Core", overchargeCoreColor, cruiseWakeColor,
            3.4f, 18f, 2.8f, 5.2f, 0.018f, 0.38f);
        _filamentWakeMaterial = CreateWakeMaterial(
            "Ion Filaments", cruiseWakeColor, overchargeEdgeColor,
            2.25f, 11f, 2.1f, 7.5f, 0.065f, 0.88f);
        _memoryWakeMaterial = CreateWakeMaterial(
            "Exhaust Memory", cruiseWakeColor, overchargeTailColor,
            0.48f, 5.5f, 1.25f, 4.6f, 0.11f, 0.92f);
        _sparkWakeMaterial = CreateWakeMaterial(
            "Plasma Sparks", overchargeCoreColor, overchargeEdgeColor,
            4.1f, 22f, 3.4f, 10.5f, 0.16f, 0.64f);

        _leftWakeProxy = CreateWakeProxy("LEFT EXHAUST OUTPUT");
        _rightWakeProxy = CreateWakeProxy("RIGHT EXHAUST OUTPUT");
        _leftPlasmaProxy = CreateWakeProxy("LEFT PLASMA EFFECTS OUTPUT");
        _rightPlasmaProxy = CreateWakeProxy("RIGHT PLASMA EFFECTS OUTPUT");
        SyncWakeProxies();
        ApplyWakeLayerPlacement();
        ApplyMemoryRibbonSafety();

        _ghostLeft = CreateGhostTrail("Memory_L", _leftWakeProxy);
        _ghostRight = CreateGhostTrail("Memory_R", _rightWakeProxy);

        _coreLeft = CreateParticleLayer("Core_L", _leftWakeProxy, false,
            _coreWakeMaterial);
        _coreRight = CreateParticleLayer("Core_R", _rightWakeProxy, false,
            _coreWakeMaterial);
        ConfigureEngineCore(_coreLeft);
        ConfigureEngineCore(_coreRight);

        _nozzleLeft = CreateParticleLayer("Envelope_L", _leftWakeProxy, false,
            _wakeMaterial);
        _nozzleRight = CreateParticleLayer("Envelope_R", _rightWakeProxy, false,
            _wakeMaterial);
        ConfigureNozzle(_nozzleLeft);
        ConfigureNozzle(_nozzleRight);

        _ionLeft = CreateParticleLayer("Filaments_L", _leftWakeProxy, true,
            _filamentWakeMaterial);
        _ionRight = CreateParticleLayer("Filaments_R", _rightWakeProxy, true,
            _filamentWakeMaterial);
        ConfigureIonStreaks(_ionLeft);
        ConfigureIonStreaks(_ionRight);

        _motesLeft = CreateParticleLayer("Motes_L", _leftPlasmaProxy, true,
            _wakeMaterial);
        _motesRight = CreateParticleLayer("Motes_R", _rightPlasmaProxy, true,
            _wakeMaterial);
        ConfigurePlasmaMotes(_motesLeft);
        ConfigurePlasmaMotes(_motesRight);

        _chamberLeft = CreateParticleLayer("PlasmaChamber_L", _leftPlasmaProxy, false,
            _sparkWakeMaterial);
        _chamberRight = CreateParticleLayer("PlasmaChamber_R", _rightPlasmaProxy, false,
            _sparkWakeMaterial);
        ConfigurePlasmaChamber(_chamberLeft);
        ConfigurePlasmaChamber(_chamberRight);

        _sparksLeft = CreateParticleLayer("PlasmaSparks_L", _leftPlasmaProxy, true,
            _sparkWakeMaterial);
        _sparksRight = CreateParticleLayer("PlasmaSparks_R", _rightPlasmaProxy, true,
            _sparkWakeMaterial);
        ConfigurePlasmaSparks(_sparksLeft);
        ConfigurePlasmaSparks(_sparksRight);

        _shockBurst = CreateParticleLayer("IgnitionBurst", _wakeRoot, true,
            _filamentWakeMaterial);
        ConfigureShockBurst(_shockBurst);

        GameObject lightObject = CreateWakeObject("Light", _wakeRoot);
        _wakeLight = lightObject.AddComponent<Light>();
        _wakeLight.type = LightType.Point;
        _wakeLight.shadows = LightShadows.None;
        _wakeLight.range = 3.2f;
        _wakeLight.intensity = 0f;
        _wakeLight.color = idleWakeColor;
    }

    private void ResolveWakeAnchors()
    {
        if (propulsionWakePlacementRoot == null ||
            !propulsionWakePlacementRoot.name.StartsWith("Propulsion Wake Placement Rig"))
            propulsionWakePlacementRoot = FindWakePlacementRoot();

        _leftAuthoredNozzle = FindDescendant(propulsionWakePlacementRoot,
            "LEFT EXHAUST - PLACE ON NOZZLE");
        _rightAuthoredNozzle = FindDescendant(propulsionWakePlacementRoot,
            "RIGHT EXHAUST - PLACE ON NOZZLE");
        if (plasmaEffectsPlacementRoot == null ||
            !plasmaEffectsPlacementRoot.name.StartsWith("PlasmaFX_Rig"))
            plasmaEffectsPlacementRoot = FindPlacementRoot("PlasmaFX_Rig");
        _leftAuthoredPlasma = FindDescendant(plasmaEffectsPlacementRoot,
            "LEFT PLASMA FX - PLACE ON NOZZLE");
        _rightAuthoredPlasma = FindDescendant(plasmaEffectsPlacementRoot,
            "RIGHT PLASMA FX - PLACE ON NOZZLE");
        _wakeTuning = propulsionWakePlacementRoot != null
            ? propulsionWakePlacementRoot.GetComponent<PropulsionWakeTuning>()
            : null;
    }

    private Transform FindWakePlacementRoot()
    {
        return FindPlacementRoot("Propulsion Wake Placement Rig");
    }

    private Transform FindPlacementRoot(string namePrefix)
    {
        Transform nested = FindDescendantStartingWith(transform, namePrefix);
        if (nested != null)
            return nested;

        // Also support placing the rig beside the craft in the scene while aligning it.
        Transform nearest = null;
        float nearestDistance = float.PositiveInfinity;
        Transform[] sceneTransforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include);
        foreach (Transform candidate in sceneTransforms)
        {
            if (!candidate.name.StartsWith(namePrefix))
                continue;

            float distance = (candidate.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private bool HasAuthoredWakePlacement()
    {
        return _leftAuthoredNozzle != null && _rightAuthoredNozzle != null;
    }

    private static Transform FindDescendant(Transform searchRoot, string exactName)
    {
        if (searchRoot == null)
            return null;

        Transform[] descendants = searchRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant.name == exactName)
                return descendant;
        }
        return null;
    }

    private static Transform FindDescendantStartingWith(Transform searchRoot, string namePrefix)
    {
        if (searchRoot == null)
            return null;

        Transform[] descendants = searchRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant.name.StartsWith(namePrefix))
                return descendant;
        }
        return null;
    }

    private Transform CreateWakeProxy(string proxyName)
    {
        GameObject proxyObject = new GameObject(proxyName);
        proxyObject.transform.SetParent(_wakeRoot, false);
        return proxyObject.transform;
    }

    private void SyncWakeProxies()
    {
        if (_wakeEngine == null)
            return;

        SyncWakeProxy(_leftWakeProxy, _leftAuthoredNozzle, leftNozzleOffset);
        SyncWakeProxy(_rightWakeProxy, _rightAuthoredNozzle, rightNozzleOffset);
        SyncPlasmaProxy(_leftPlasmaProxy, _leftAuthoredPlasma, _leftWakeProxy,
            _wakeTuning != null ? _wakeTuning.leftPlasmaOffset : Vector3.zero);
        SyncPlasmaProxy(_rightPlasmaProxy, _rightAuthoredPlasma, _rightWakeProxy,
            _wakeTuning != null ? _wakeTuning.rightPlasmaOffset : Vector3.zero);
    }

    private static void SyncPlasmaProxy(Transform proxy, Transform authoredAnchor,
        Transform fallbackProxy, Vector3 localOffset)
    {
        if (proxy == null)
            return;

        if (authoredAnchor != null)
        {
            proxy.SetPositionAndRotation(authoredAnchor.position, authoredAnchor.rotation);
            proxy.position += authoredAnchor.TransformVector(localOffset);
            return;
        }

        if (fallbackProxy != null)
        {
            proxy.SetPositionAndRotation(fallbackProxy.position, fallbackProxy.rotation);
            proxy.position += fallbackProxy.TransformVector(localOffset);
        }
    }

    private void SyncWakeProxy(Transform proxy, Transform authoredAnchor,
        Vector3 fallbackOffset)
    {
        if (proxy == null)
            return;

        if (authoredAnchor != null)
        {
            proxy.SetPositionAndRotation(authoredAnchor.position, authoredAnchor.rotation);
            return;
        }

        Vector3 fallbackPosition = _wakeEngine.transform.position
            + transform.TransformVector(fallbackOffset);
        Quaternion fallbackRotation = Quaternion.LookRotation(-transform.forward, transform.up);
        proxy.SetPositionAndRotation(fallbackPosition, fallbackRotation);
    }

    private TrailRenderer CreateGhostTrail(string layerName, Transform nozzleProxy)
    {
        GameObject ghostObject = CreateWakeObject(layerName, nozzleProxy);
        TrailRenderer ghost = ghostObject.AddComponent<TrailRenderer>();
        ghost.material = _memoryWakeMaterial;
        ghost.emitting = false;
        ghost.time = 0.025f;
        ghost.startWidth = 0.025f;
        ghost.endWidth = 0f;
        ghost.minVertexDistance = 0.035f;
        ghost.numCornerVertices = 10;
        ghost.numCapVertices = 8;
        ghost.alignment = LineAlignment.View;
        ghost.textureMode = LineTextureMode.Stretch;
        ghost.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ghost.receiveShadows = false;
        ghost.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.18f),
            new Keyframe(0.06f, 0.72f),
            new Keyframe(0.32f, 0.30f),
            new Keyframe(0.70f, 0.06f),
            new Keyframe(1f, 0f));
        return ghost;
    }

    private GameObject CreateWakeObject(string layerName, Transform parent)
    {
        GameObject layerObject = new GameObject("CinematicWake_" + layerName);
        layerObject.transform.SetParent(parent != null ? parent : transform, false);
        layerObject.transform.localPosition = Vector3.zero;
        layerObject.transform.localRotation = Quaternion.identity;
        return layerObject;
    }

    private ParticleSystem CreateParticleLayer(string layerName, Transform nozzleProxy,
        bool worldSpace, Material material)
    {
        GameObject particleObject = CreateWakeObject(layerName, nozzleProxy);
        // A placement anchor's blue (+Z) axis is the exhaust direction.
        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();

        // AddComponent starts a ParticleSystem immediately on an active GameObject.
        // Stop it before changing immutable-while-playing settings such as duration.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = worldSpace
            ? ParticleSystemSimulationSpace.World
            : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = worldSpace ? 1400 : 420;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.material = material;
        renderer.trailMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        // Stretched billboards elongate along their billboard-local Y axis.
        // Shifting Y anchors the long visual at the nozzle instead of centering it there.
        renderer.pivot = new Vector3(0f, WakeParticlePivot, 0f);
        return particles;
    }

    private void ConfigureEngineCore(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.055f, 0.095f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7.5f, 12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.075f, 0.13f);
        main.startColor = overchargeCoreColor;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 80f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 0.65f;
        shape.radius = 0.012f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            overchargeCoreColor, cruiseWakeColor, cruiseWakeColor,
            1f, 0.72f, 0f);

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.32f),
            new Keyframe(0.08f, 1f),
            new Keyframe(0.72f, 0.62f),
            new Keyframe(1f, 0f)));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.12f;
        renderer.lengthScale = 5.6f;
        particles.Play();
    }

    private void ConfigureNozzle(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.11f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 5.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.40f);
        main.startColor = idleWakeColor;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 24f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 3.2f;
        shape.radius = 0.032f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            overchargeCoreColor, cruiseWakeColor, idleWakeColor,
            0.92f, 0.68f, 0f);

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.22f),
            new Keyframe(0.18f, 1f),
            new Keyframe(0.68f, 0.56f),
            new Keyframe(1f, 0f)));

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = 0.055f;
        noise.frequency = 4.2f;
        noise.scrollSpeed = 0.7f;
        noise.octaveCount = 2;
        noise.damping = true;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.09f;
        renderer.lengthScale = 2.8f;
        particles.Play();
    }

    private void ConfigureIonStreaks(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.055f, 0.16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(26f, 68f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.052f);
        main.startColor = cruiseWakeColor;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 1.15f;
        shape.radius = 0.026f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            overchargeCoreColor, cruiseWakeColor, overchargeTailColor,
            0.92f, 0.56f, 0f);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = 0.10f;
        noise.frequency = 4.2f;
        noise.scrollSpeed = 2.2f;
        noise.octaveCount = 3;
        noise.damping = true;

        ParticleSystem.TrailModule trails = particles.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 0.86f;
        trails.lifetime = 0.055f;
        trails.minVertexDistance = 0.018f;
        trails.dieWithParticles = false;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.65f), new Keyframe(1f, 0f)));
        trails.colorOverLifetime = MakeGradient(
            cruiseWakeColor, overchargeTailColor, overchargeEdgeColor,
            0.74f, 0.34f, 0f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.018f;
        renderer.lengthScale = 2.2f;
        particles.Play();
    }

    private void ConfigurePlasmaMotes(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.58f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.10f);
        main.startColor = overchargeTailColor;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 7f;
        shape.radius = 0.07f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            cruiseWakeColor, overchargeTailColor, overchargeEdgeColor,
            0.62f, 0.38f, 0f);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = 0.72f;
        noise.frequency = 1.15f;
        noise.scrollSpeed = 0.92f;
        noise.octaveCount = 3;
        noise.damping = true;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.02f;
        renderer.lengthScale = 1.8f;
        particles.Play();
    }

    private void ConfigurePlasmaSparks(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.075f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(18f, 46f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.075f);
        main.startColor = overchargeCoreColor;
        main.maxParticles = 800;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 11f;
        shape.radius = 0.055f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            overchargeCoreColor, overchargeEdgeColor, overchargeTailColor,
            1f, 0.72f, 0f);

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.08f, 1f),
            new Keyframe(0.55f, 0.64f),
            new Keyframe(1f, 0f)));

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = 0.34f;
        noise.frequency = 5.5f;
        noise.scrollSpeed = 2.7f;
        noise.octaveCount = 3;
        noise.damping = true;

        ParticleSystem.TrailModule trails = particles.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 0.44f;
        trails.lifetime = 0.075f;
        trails.minVertexDistance = 0.012f;
        trails.dieWithParticles = false;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.72f),
            new Keyframe(0.22f, 0.42f),
            new Keyframe(1f, 0f)));
        trails.colorOverLifetime = MakeGradient(
            overchargeCoreColor, overchargeEdgeColor, overchargeTailColor,
            0.88f, 0.48f, 0f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.025f;
        renderer.lengthScale = 2.8f;
        particles.Play();
    }

    private void ConfigurePlasmaChamber(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startColor = overchargeCoreColor;
        main.maxParticles = 160;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 80f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.045f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            overchargeCoreColor, cruiseWakeColor, overchargeTailColor,
            1f, 0.82f, 0f);

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.48f),
            new Keyframe(0.18f, 1f),
            new Keyframe(0.72f, 0.78f),
            new Keyframe(1f, 0f)));

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = 0.08f;
        noise.frequency = 8f;
        noise.scrollSpeed = 2.2f;
        noise.octaveCount = 2;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.pivot = Vector3.zero;
        particles.Play();
    }

    private void ConfigureShockBurst(ParticleSystem particles)
    {
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.30f);
        main.startColor = overchargeCoreColor;
        main.maxParticles = 320;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = MakeGradient(
            overchargeCoreColor, overchargeTailColor, overchargeEdgeColor,
            1f, 0.55f, 0f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.cameraVelocityScale = 0f;
        renderer.velocityScale = 0.065f;
        renderer.lengthScale = 4.5f;
    }

    private Material CreateWakeMaterial(string layerName, Color tint, Color edgeColor,
        float intensity, float coreSharpness, float haloSharpness,
        float flowSpeed, float turbulence, float textureInfluence)
    {
        Shader shader = Shader.Find("Slingshot/VFX/Propulsion Plasma");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material material = new Material(shader);
        material.name = "Cinematic " + layerName + " (Runtime)";

        Texture2D texture = plasmaWakeTexture != null
            ? plasmaWakeTexture
            : CreateFallbackWakeTexture();
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
        if (material.HasProperty("_EdgeColor")) material.SetColor("_EdgeColor", edgeColor);
        if (material.HasProperty("_Intensity")) material.SetFloat("_Intensity", intensity);
        if (material.HasProperty("_CoreSharpness")) material.SetFloat("_CoreSharpness", coreSharpness);
        if (material.HasProperty("_HaloSharpness")) material.SetFloat("_HaloSharpness", haloSharpness);
        if (material.HasProperty("_FlowSpeed")) material.SetFloat("_FlowSpeed", flowSpeed);
        if (material.HasProperty("_Turbulence")) material.SetFloat("_Turbulence", turbulence);
        if (material.HasProperty("_TextureInfluence")) material.SetFloat("_TextureInfluence", textureInfluence);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SoftParticlesEnabled"))
            material.SetFloat("_SoftParticlesEnabled", 1f);
        if (material.HasProperty("_SoftParticlesNearFadeDistance"))
            material.SetFloat("_SoftParticlesNearFadeDistance", 0.05f);
        if (material.HasProperty("_SoftParticlesFarFadeDistance"))
            material.SetFloat("_SoftParticlesFarFadeDistance", 1.8f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_SOFTPARTICLES_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        return material;
    }

    private Texture2D CreateFallbackWakeTexture()
    {
        const int width = 128;
        const int height = 32;
        _fallbackWakeTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Fallback Plasma Filament",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = Mathf.Abs((y + 0.5f) / height * 2f - 1f);
            for (int x = 0; x < width; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                float radial = Mathf.Exp(-v * v * 12f);
                float taper = Mathf.SmoothStep(1f, 0f, u);
                float filament = radial * taper;
                pixels[y * width + x] = new Color(
                    filament * 0.42f,
                    filament * 0.92f,
                    filament,
                    filament);
            }
        }
        _fallbackWakeTexture.SetPixels(pixels);
        _fallbackWakeTexture.Apply(false, true);
        return _fallbackWakeTexture;
    }

    private static ParticleSystem.MinMaxGradient MakeGradient(
        Color head, Color middle, Color tail,
        float headAlpha, float middleAlpha, float tailAlpha)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(head, 0f),
                new GradientColorKey(middle, 0.38f),
                new GradientColorKey(tail, 1f)
            },
            new[]
            {
                new GradientAlphaKey(headAlpha, 0f),
                new GradientAlphaKey(middleAlpha, 0.48f),
                new GradientAlphaKey(tailAlpha, 1f)
            });
        return new ParticleSystem.MinMaxGradient(gradient);
    }

    private void UpdateParticleWake(CraftTelemetry telemetry)
    {
        if (_wakeEngine == null || _ghostLeft == null || _ghostRight == null)
            return;

        if (!_usingAuthoredWakePlacement && Time.unscaledTime >= _nextWakeAnchorSearchTime)
        {
            _nextWakeAnchorSearchTime = Time.unscaledTime + 0.5f;
            ResolveWakeAnchors();
            if (HasAuthoredWakePlacement())
            {
                _usingAuthoredWakePlacement = true;
                if (_wakeRoot != null)
                    _wakeRoot.name = "Cinematic Propulsion Wake [AUTHORED ANCHORS]";
                _ghostLeft.Clear();
                _ghostRight.Clear();
            }
        }

        SyncWakeProxies();
        ApplyWakeLayerPlacement();
        ApplyMemoryRibbonSafety();

        float speedKmh = Mathf.Max(0f, telemetry.speed) * 3.6f;
        float speed01 = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(speedKmh / Mathf.Max(100f, cinematicSpeedKmh)));
        float throttle01 = Mathf.Clamp01(_wakeEngine.Throttle);
        float boost01 = _boostCore != null ? _boostCore.CameraBoost01 : 0f;
        float ignition01 = _boostCore != null ? _boostCore.Ignition01 : 0f;
        float driveEnergy = Mathf.Clamp01(Mathf.Max(throttle01, speed01 * 0.58f));
        float cinematic = Mathf.Clamp01(boost01 * 0.90f + ignition01 * 0.48f);
        float flicker = 0.94f + Mathf.PerlinNoise(Time.time * 18f, 0.37f) * 0.12f;
        float densityScale = WakeDensityScale;
        UpdateParticlePivots();
        UpdateParticleStretch();

        if (_boostCore != null && _boostCore.BoostSequenceId != _lastWakeSequence)
        {
            _lastWakeSequence = _boostCore.BoostSequenceId;
            _ignitionFlash = 1f;
            _ghostLeft.Clear();
            _ghostRight.Clear();
            EmitOverchargeBurst();
        }
        _ignitionFlash = Mathf.MoveTowards(_ignitionFlash, 0f, Time.deltaTime * 4.6f);

        UpdateNozzle(_nozzleLeft, driveEnergy, cinematic, flicker);
        UpdateNozzle(_nozzleRight, driveEnergy, cinematic, flicker);
        UpdateEngineCore(_coreLeft, driveEnergy, cinematic, flicker);
        UpdateEngineCore(_coreRight, driveEnergy, cinematic, flicker);
        float filamentRate = (Mathf.Lerp(4f, 38f, speed01)
            + cinematic * 72f + _ignitionFlash * 28f) * densityScale;
        if (_wakeTuning != null && !_wakeTuning.showIonFilaments)
            filamentRate = 0f;
        float moteRate = (Mathf.Lerp(1f, 9f, speed01)
            + cinematic * 28f + _ignitionFlash * 12f) * densityScale;
        if (_wakeTuning != null)
            moteRate *= _wakeTuning.showPlasmaMotes
                ? Mathf.Clamp(_wakeTuning.moteAmount, 0f, 3f)
                : 0f;
        SetParticleRate(_ionLeft, filamentRate);
        SetParticleRate(_ionRight, filamentRate);
        SetParticleRate(_motesLeft, moteRate);
        SetParticleRate(_motesRight, moteRate);
        UpdateFilaments(_ionLeft, speed01, cinematic,
            WakeWidthScale * WakeFilamentSize);
        UpdateFilaments(_ionRight, speed01, cinematic,
            WakeWidthScale * WakeFilamentSize);
        UpdatePlasmaMotes(_motesLeft, driveEnergy, cinematic);
        UpdatePlasmaMotes(_motesRight, driveEnergy, cinematic);
        UpdatePlasmaChamber(_chamberLeft, driveEnergy, cinematic, flicker);
        UpdatePlasmaChamber(_chamberRight, driveEnergy, cinematic, flicker);
        UpdatePlasmaSparks(_sparksLeft, driveEnergy, cinematic, flicker);
        UpdatePlasmaSparks(_sparksRight, driveEnergy, cinematic, flicker);

        UpdateGhostTrail(_ghostLeft, speedKmh, speed01, cinematic);
        UpdateGhostTrail(_ghostRight, speedKmh, speed01, cinematic);

        if (_wakeLight != null)
        {
            _wakeLight.transform.position = GetWakeMidpoint();
            _wakeLight.color = Color.Lerp(idleWakeColor, overchargeTailColor, cinematic);
            _wakeLight.intensity = (0.32f + driveEnergy * 2.2f + cinematic * 6.5f
                + _ignitionFlash * 9f) * flicker;
            _wakeLight.range = Mathf.Lerp(2.8f, 9.5f,
                Mathf.Clamp01(cinematic + _ignitionFlash * 0.55f));
        }
    }

    private void UpdateFilaments(ParticleSystem particles, float speed01, float cinematic,
        float widthScale)
    {
        if (particles == null) return;
        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            Mathf.Lerp(0.055f, 0.11f, speed01) * WakeFilamentLength,
            (Mathf.Lerp(0.10f, 0.21f, speed01) + cinematic * 0.055f) * WakeFilamentLength);
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            Mathf.Lerp(20f, 42f, speed01) + cinematic * 20f,
            Mathf.Lerp(38f, 82f, speed01) + cinematic * 34f);
        main.startSize = new ParticleSystem.MinMaxCurve(
            (0.018f + cinematic * 0.008f) * widthScale,
            (0.050f + cinematic * 0.030f) * widthScale);
        main.startColor = Color.Lerp(cruiseWakeColor, overchargeCoreColor, cinematic);
    }

    private void ApplyWakeLayerPlacement()
    {
        if (_wakeTuning == null)
            return;

        SetLayerLocalTransform(_coreLeft, _wakeTuning.leftCoreOffset,
            _wakeTuning.leftCoreRotation);
        SetLayerLocalTransform(_coreRight, _wakeTuning.rightCoreOffset,
            _wakeTuning.rightCoreRotation);
        SetLayerLocalTransform(_nozzleLeft, _wakeTuning.leftPlumeOffset,
            _wakeTuning.leftPlumeRotation);
        SetLayerLocalTransform(_nozzleRight, _wakeTuning.rightPlumeOffset,
            _wakeTuning.rightPlumeRotation);
        SetLayerLocalTransform(_ionLeft, _wakeTuning.leftFilamentOffset,
            _wakeTuning.leftFilamentRotation);
        SetLayerLocalTransform(_ionRight, _wakeTuning.rightFilamentOffset,
            _wakeTuning.rightFilamentRotation);
        SetLayerLocalTransform(_ghostLeft, _wakeTuning.leftRibbonOffset,
            _wakeTuning.leftRibbonRotation);
        SetLayerLocalTransform(_ghostRight, _wakeTuning.rightRibbonOffset,
            _wakeTuning.rightRibbonRotation);

        SetLayerLocalTransform(_chamberLeft, _wakeTuning.leftChamberOffset,
            Vector3.zero);
        SetLayerLocalTransform(_chamberRight, _wakeTuning.rightChamberOffset,
            Vector3.zero);
        SetLayerLocalTransform(_motesLeft, _wakeTuning.leftMoteOffset,
            _wakeTuning.leftMoteRotation);
        SetLayerLocalTransform(_motesRight, _wakeTuning.rightMoteOffset,
            _wakeTuning.rightMoteRotation);
        SetLayerLocalTransform(_sparksLeft, _wakeTuning.leftSparkOffset,
            _wakeTuning.leftSparkRotation);
        SetLayerLocalTransform(_sparksRight, _wakeTuning.rightSparkOffset,
            _wakeTuning.rightSparkRotation);
    }

    private static void SetLayerLocalTransform(Component layer, Vector3 position,
        Vector3 eulerRotation)
    {
        if (layer == null)
            return;
        layer.transform.localPosition = position;
        layer.transform.localRotation = Quaternion.Euler(eulerRotation);
    }

    private void UpdateGhostTrail(TrailRenderer ghost,
        float speedKmh, float speed01, float cinematic)
    {
        if (ghost == null) return;
        bool showMemory = _wakeTuning == null || _wakeTuning.showMemoryRibbons;
        ghost.emitting = showMemory && (speedKmh > 120f || cinematic > 0.18f);
        if (!showMemory)
        {
            if (ghost.positionCount > 0)
                ghost.Clear();
            return;
        }

        ghost.startWidth = Mathf.Lerp(0.006f, 0.021f, speed01)
            + cinematic * 0.042f + _ignitionFlash * 0.018f;
        ghost.startWidth *= WakeWidthScale * WakeRibbonSize;
        float requestedTime = (Mathf.Lerp(0.008f, 0.030f, speed01) + cinematic * 0.038f)
            * WakeMemoryLength;
        float speedMetersPerSecond = Mathf.Max(0.1f, speedKmh / 3.6f);
        float maximumLength = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.maximumMemoryLengthMeters, 0.5f, 60f)
            : 12f;
        ghost.time = Mathf.Min(requestedTime, maximumLength / speedMetersPerSecond);
        float smoothness = _wakeTuning != null
            ? Mathf.Clamp01(_wakeTuning.memorySmoothness)
            : 0.72f;
        ghost.minVertexDistance = Mathf.Lerp(0.24f, 0.018f, smoothness);
        Color memoryColor = Color.Lerp(idleWakeColor, overchargeTailColor, cinematic);
        memoryColor.a = Mathf.Lerp(0.18f, 0.42f, cinematic);
        ghost.startColor = memoryColor;
        ghost.endColor = new Color(
            overchargeTailColor.r, overchargeTailColor.g, overchargeTailColor.b, 0f);
    }

    private void UpdatePlasmaMotes(ParticleSystem particles, float driveEnergy,
        float cinematic)
    {
        if (particles == null) return;
        ParticleSystem.MainModule main = particles.main;
        float moteSize = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.moteSize, 0.2f, 3f)
            : 1f;
        float moteLifetime = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.moteLifetime, 0.2f, 3f)
            : 1f;
        float moteSpeed = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.moteSpeed, 0.2f, 3f)
            : 1f;
        float sizeScale = WakeWidthScale * WakePlumeSize * moteSize;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            (0.16f + driveEnergy * 0.05f) * WakeBodyLength * moteLifetime,
            (0.34f + cinematic * 0.18f) * WakeBodyLength * moteLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            (4f + driveEnergy * 4f) * moteSpeed,
            (12f + cinematic * 18f) * moteSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(
            (0.018f + cinematic * 0.012f) * sizeScale,
            (0.075f + cinematic * 0.055f) * sizeScale);
        ParticleSystem.ShapeModule shape = particles.shape;
        float moteSpread = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.moteSpread, 0.2f, 3f)
            : 1f;
        shape.angle = Mathf.Clamp((5f + cinematic * 8f) * moteSpread, 0.1f, 60f);
        shape.radius = 0.055f * WakeWidthScale * moteSpread;
        ParticleSystem.NoiseModule noise = particles.noise;
        float moteTurbulence = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.moteTurbulence, 0.2f, 3f)
            : 1f;
        noise.strength = (0.48f + cinematic * 0.40f) * moteTurbulence;
    }

    private void UpdatePlasmaSparks(ParticleSystem particles, float driveEnergy,
        float cinematic, float flicker)
    {
        if (particles == null) return;

        bool showSparks = _wakeTuning == null || _wakeTuning.showPlasmaSparks;
        float sparkAmount = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkAmount, 0f, 3f)
            : 1f;
        float overchargeBoost = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.overchargeSparkBoost, 0f, 3f)
            : 1f;
        float idlePresence = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.idleSparkPresence, 0f, 3f)
            : 1f;
        float throttleResponse = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.throttleSparkResponse, 0f, 3f)
            : 1f;
        float ignitionBurst = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.ignitionSparkBurst, 0f, 3f)
            : 1f;
        float flickerSpeed = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkFlickerSpeed, 0.1f, 4f)
            : 1f;
        float sparkFlicker = 0.82f + Mathf.PerlinNoise(
            Time.time * 21f * flickerSpeed,
            particles == _sparksLeft ? 0.173f : 0.719f) * 0.36f;
        float rate = showSparks
            ? (10f * idlePresence + driveEnergy * 28f * throttleResponse
                + cinematic * 78f * overchargeBoost
                + _ignitionFlash * 88f * ignitionBurst)
                * sparkAmount * WakeDensityScale * flicker * sparkFlicker
            : 0f;
        SetParticleRate(particles, rate);

        ParticleSystem.MainModule main = particles.main;
        float lifetimeScale = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkLifetime, 0.2f, 3f)
            : 1f;
        float speedScale = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkSpeed, 0.2f, 3f)
            : 1f;
        float speedVariation = _wakeTuning != null
            ? Mathf.Clamp01(_wakeTuning.sparkSpeedVariation)
            : 0.65f;
        float sizeScale = WakeWidthScale * WakeSparkSize;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            (0.055f + cinematic * 0.025f) * lifetimeScale,
            (0.17f + cinematic * 0.15f + _ignitionFlash * 0.08f) * lifetimeScale);
        float centerSpeed = (32f + driveEnergy * 20f + cinematic * 38f) * speedScale;
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            centerSpeed * Mathf.Lerp(0.92f, 0.32f, speedVariation),
            centerSpeed * Mathf.Lerp(1.08f, 1.72f, speedVariation));
        main.gravityModifier = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkGravity, -2f, 2f)
            : 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(
            (0.018f + cinematic * 0.008f) * sizeScale,
            (0.065f + cinematic * 0.040f + _ignitionFlash * 0.018f) * sizeScale);
        main.startColor = Color.Lerp(overchargeCoreColor, overchargeEdgeColor,
            Mathf.Clamp01(cinematic * 0.8f));
        if (_wakeTuning != null)
            main.startColor = Color.Lerp(_wakeTuning.plasmaSparkColor,
                overchargeCoreColor, Mathf.Clamp01(cinematic * 0.65f));

        ParticleSystem.ShapeModule shape = particles.shape;
        float spreadScale = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkSpread, 0.2f, 3f)
            : 1f;
        shape.angle = Mathf.Clamp((5f + driveEnergy * 6f + cinematic * 8f) * spreadScale,
            0.1f, 55f);
        float spawnRadius = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkSpawnRadius, 0.05f, 3f)
            : 1f;
        shape.radius = (0.035f + cinematic * 0.025f)
            * WakeWidthScale * spawnRadius;

        ParticleSystem.TrailModule trails = particles.trails;
        float trailLifetime = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkTrailLifetime, 0.1f, 3f)
            : 1f;
        float trailWidth = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkTrailWidth, 0.1f, 3f)
            : 1f;
        trails.lifetime = (0.045f + cinematic * 0.065f)
            * lifetimeScale * trailLifetime;
        float trailAmount = _wakeTuning != null
            ? Mathf.Clamp01(_wakeTuning.sparkTrailAmount)
            : 0.55f;
        trails.ratio = trailAmount * Mathf.Lerp(0.52f, 1f, cinematic);
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(trailWidth,
            new AnimationCurve(
                new Keyframe(0f, 0.72f),
                new Keyframe(0.22f, 0.42f),
                new Keyframe(1f, 0f)));

        ParticleSystem.NoiseModule noise = particles.noise;
        float turbulence = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkTurbulence, 0.2f, 3f)
            : 1f;
        noise.strength = (0.20f + cinematic * 0.26f) * turbulence;
        float turbulenceFrequency = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkTurbulenceFrequency, 0.2f, 3f)
            : 1f;
        noise.frequency = 5.5f * turbulenceFrequency;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity =
            particles.limitVelocityOverLifetime;
        float drag = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkDrag, 0f, 3f)
            : 0.25f;
        limitVelocity.enabled = drag > 0.001f;
        limitVelocity.limit = 250f;
        limitVelocity.dampen = Mathf.Clamp01(drag / 3f) * 0.72f;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        float streak = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.sparkStreakLength, 0.2f, 3f)
            : 1f;
        renderer.lengthScale = 2.8f * streak;
    }

    private void UpdatePlasmaChamber(ParticleSystem particles, float driveEnergy,
        float cinematic, float flicker)
    {
        if (particles == null) return;
        bool visible = _wakeTuning == null || _wakeTuning.showPlasmaChamber;
        ParticleSystem.EmissionModule emission = particles.emission;
        float chamberIntensity = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.chamberIntensity, 0f, 3f)
            : 1f;
        emission.rateOverTime = visible
            ? (52f + driveEnergy * 70f + cinematic * 115f)
                * WakeDensityScale * chamberIntensity
            : 0f;

        ParticleSystem.MainModule main = particles.main;
        float chamberScale = _wakeTuning != null
            ? Mathf.Clamp(_wakeTuning.chamberSize, 0.2f, 3f)
            : 1f;
        float sizeScale = WakeWidthScale * chamberScale;
        main.startSize = new ParticleSystem.MinMaxCurve(
            (0.10f + cinematic * 0.06f) * sizeScale * flicker,
            (0.21f + driveEnergy * 0.07f + cinematic * 0.17f) * sizeScale * flicker);
        Color chamberColor = _wakeTuning != null
            ? _wakeTuning.plasmaChamberColor
            : cruiseWakeColor;
        main.startColor = Color.Lerp(chamberColor, overchargeCoreColor,
            Mathf.Clamp01(cinematic + _ignitionFlash * 0.55f));

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.radius = (0.035f + cinematic * 0.045f) * sizeScale;
    }

    private void ApplyMemoryRibbonSafety()
    {
        Vector3 currentPosition = GetWakeMidpoint();
        Vector3 currentForward = transform.forward;
        if (!_wakeMotionInitialized)
        {
            _wakeMotionInitialized = true;
            _previousWakePosition = currentPosition;
            _previousWakeForward = currentForward;
            return;
        }

        bool clear = false;
        if (_wakeTuning != null)
        {
            if (_wakeTuning.clearOnSharpDirectionChange &&
                Vector3.Angle(_previousWakeForward, currentForward) >=
                Mathf.Clamp(_wakeTuning.directionChangeClearAngle, 10f, 120f))
                clear = true;

            if (Vector3.Distance(_previousWakePosition, currentPosition) >=
                Mathf.Clamp(_wakeTuning.teleportClearDistance, 1f, 100f))
                clear = true;

            if (_wakeTuning.preventForwardWrap &&
                (TrailWrapsForward(_ghostLeft, _wakeTuning.forwardWrapAllowance) ||
                 TrailWrapsForward(_ghostRight, _wakeTuning.forwardWrapAllowance)))
                clear = true;
        }

        if (clear)
        {
            _ghostLeft?.Clear();
            _ghostRight?.Clear();
        }

        _previousWakePosition = currentPosition;
        _previousWakeForward = currentForward;
    }

    private bool TrailWrapsForward(TrailRenderer trail, float allowance)
    {
        if (trail == null || trail.positionCount < 2)
            return false;

        float allowedDistance = Mathf.Max(0f, allowance);
        for (int i = 0; i < trail.positionCount; i++)
        {
            Vector3 relative = trail.GetPosition(i) - transform.position;
            if (Vector3.Dot(relative, transform.forward) > allowedDistance)
                return true;
        }
        return false;
    }

    private void UpdateNozzle(ParticleSystem particles, float driveEnergy,
        float cinematic, float flicker)
    {
        if (particles == null) return;
        ParticleSystem.EmissionModule emission = particles.emission;
        bool visible = _wakeTuning == null || _wakeTuning.showPlasmaPlume;
        emission.rateOverTime = visible
            ? (12f + driveEnergy * 34f + cinematic * 54f)
                * flicker * WakeDensityScale
            : 0f;
        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            (0.10f + cinematic * 0.035f) * WakeBodyLength,
            (0.19f + driveEnergy * 0.07f + cinematic * 0.10f) * WakeBodyLength);
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            2.2f + driveEnergy * 2.8f + cinematic * 4.5f,
            4.6f + driveEnergy * 5.2f + cinematic * 8.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(
            (0.12f + cinematic * 0.05f) * WakeWidthScale * WakePlumeSize,
            (0.25f + driveEnergy * 0.10f + cinematic * 0.18f)
                * WakeWidthScale * WakePlumeSize);
        main.startColor = Color.Lerp(idleWakeColor, overchargeCoreColor, cinematic);
    }

    private void UpdateEngineCore(ParticleSystem particles, float driveEnergy,
        float cinematic, float flicker)
    {
        if (particles == null) return;
        ParticleSystem.EmissionModule emission = particles.emission;
        bool visible = _wakeTuning == null || _wakeTuning.showHotCore;
        emission.rateOverTime = visible
            ? (38f + driveEnergy * 82f + cinematic * 126f)
                * flicker * WakeDensityScale
            : 0f;
        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            (0.045f + cinematic * 0.012f) * WakeBodyLength,
            (0.082f + driveEnergy * 0.022f + cinematic * 0.035f) * WakeBodyLength);
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            6.5f + driveEnergy * 6f + cinematic * 8f,
            11f + driveEnergy * 11f + cinematic * 17f);
        main.startSize = new ParticleSystem.MinMaxCurve(
            (0.055f + cinematic * 0.018f) * WakeWidthScale * WakeCoreSize,
            (0.105f + driveEnergy * 0.025f + cinematic * 0.045f)
                * WakeWidthScale * WakeCoreSize);
        main.startColor = Color.Lerp(cruiseWakeColor, overchargeCoreColor, cinematic);
    }

    private static void SetParticleRate(ParticleSystem particles, float rate)
    {
        if (particles == null) return;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = Mathf.Max(0f, rate);
    }

    private float WakeOverallLength => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.overallLength, 0.1f, 4f)
        : 1f;

    private float WakeBodyLength => WakeOverallLength * (_wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.exhaustBodyLength, 0.1f, 3f)
        : 1f);

    private float WakeFilamentLength => WakeOverallLength * (_wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.filamentLength, 0.1f, 3f)
        : 1f);

    private float WakeMemoryLength => WakeOverallLength * (_wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.memoryLength, 0.1f, 3f)
        : 1f);

    private float WakeWidthScale => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.width, 0.25f, 2.5f)
        : 1f;

    private float WakeCoreSize => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.coreSize, 0.2f, 3f)
        : 1f;

    private float WakePlumeSize => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.plumeSize, 0.2f, 3f)
        : 1f;

    private float WakeFilamentSize => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.filamentSize, 0.2f, 3f)
        : 1f;

    private float WakeRibbonSize => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.ribbonSize, 0.2f, 3f)
        : 1f;

    private float WakeSparkSize => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.sparkSize, 0.2f, 3f)
        : 1f;

    private float WakeParticleStretch => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.particleStretch, 0.2f, 3f)
        : 1f;

    private float WakeDensityScale => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.particleDensity, 0.2f, 2f)
        : 1f;

    private float WakeParticlePivot => _wakeTuning != null
        ? Mathf.Clamp(_wakeTuning.stretchedParticlePivot, -1f, 1f)
        : 0.5f;

    private void UpdateParticlePivots()
    {
        Vector3 pivot = new Vector3(0f, WakeParticlePivot, 0f);
        SetParticlePivot(_coreLeft, pivot);
        SetParticlePivot(_coreRight, pivot);
        SetParticlePivot(_nozzleLeft, pivot);
        SetParticlePivot(_nozzleRight, pivot);
        SetParticlePivot(_ionLeft, pivot);
        SetParticlePivot(_ionRight, pivot);
        SetParticlePivot(_motesLeft, pivot);
        SetParticlePivot(_motesRight, pivot);
        SetParticlePivot(_sparksLeft, pivot);
        SetParticlePivot(_sparksRight, pivot);
    }

    private void UpdateParticleStretch()
    {
        float stretch = WakeParticleStretch;
        SetParticleStretch(_coreLeft, 5.6f * stretch);
        SetParticleStretch(_coreRight, 5.6f * stretch);
        SetParticleStretch(_nozzleLeft, 2.8f * stretch);
        SetParticleStretch(_nozzleRight, 2.8f * stretch);
        SetParticleStretch(_ionLeft, 2.2f * stretch);
        SetParticleStretch(_ionRight, 2.2f * stretch);
        SetParticleStretch(_motesLeft, 1.8f * stretch);
        SetParticleStretch(_motesRight, 1.8f * stretch);
        SetParticleStretch(_sparksLeft, 2.8f * stretch);
        SetParticleStretch(_sparksRight, 2.8f * stretch);
    }

    private static void SetParticlePivot(ParticleSystem particles, Vector3 pivot)
    {
        if (particles == null)
            return;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
            renderer.pivot = pivot;
    }

    private static void SetParticleStretch(ParticleSystem particles, float stretch)
    {
        if (particles == null)
            return;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
            renderer.lengthScale = Mathf.Max(0.05f, stretch);
    }

    private void EmitOverchargeBurst()
    {
        if (_shockBurst == null || _wakeEngine == null)
            return;

        _shockBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _shockBurst.Play(true);

        int sequence = _boostCore != null ? _boostCore.BoostSequenceId : 0;
        const int particlesPerNozzle = 34;
        for (int nozzleIndex = 0; nozzleIndex < 2; nozzleIndex++)
        {
            Transform nozzle = nozzleIndex == 0 ? _leftWakeProxy : _rightWakeProxy;
            if (nozzle == null)
                continue;

            Vector3 origin = nozzle.position;
            for (int i = 0; i < particlesPerNozzle; i++)
            {
                float angle = i / (float)particlesPerNozzle * Mathf.PI * 2f;
                float jitter = Hash01(i + nozzleIndex * 131 + sequence * 97);
                Vector3 radial = nozzle.right * Mathf.Cos(angle)
                    + nozzle.up * Mathf.Sin(angle);
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
                {
                    position = origin + radial * Mathf.Lerp(0.015f, 0.11f, jitter),
                    velocity = nozzle.forward * Mathf.Lerp(34f, 82f, jitter)
                        + radial * Mathf.Lerp(2.5f, 7.5f, 1f - jitter),
                    startLifetime = Mathf.Lerp(0.12f, 0.27f, jitter),
                    startSize = Mathf.Lerp(0.055f, 0.16f, 1f - jitter),
                    startColor = Color.Lerp(overchargeCoreColor, overchargeEdgeColor, jitter)
                };
                _shockBurst.Emit(emit, 1);
            }
        }
    }

    private Vector3 GetWakeMidpoint()
    {
        if (_leftWakeProxy != null && _rightWakeProxy != null)
            return Vector3.Lerp(_leftWakeProxy.position, _rightWakeProxy.position, 0.5f);
        if (_leftWakeProxy != null)
            return _leftWakeProxy.position;
        if (_rightWakeProxy != null)
            return _rightWakeProxy.position;
        return _wakeEngine != null ? _wakeEngine.transform.position : transform.position;
    }

    private static float Hash01(int value)
    {
        float s = Mathf.Sin(value * 12.9898f + 78.233f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    private void AddWakeLayer(string layerName, float lateral, float vertical,
        float widthScale, float timeScale, float phase, bool overchargeOnly)
    {
        GameObject layerObject = new GameObject("CinematicWake_" + layerName);
        layerObject.transform.SetParent(transform, false);
        layerObject.transform.position = _wakeEngine.transform.position
            + transform.right * lateral + transform.up * vertical;

        TrailRenderer trail = layerObject.AddComponent<TrailRenderer>();
        trail.emitting = false;
        trail.time = 0.1f;
        trail.startWidth = idleWakeWidth * widthScale;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.42f;
        trail.numCornerVertices = 3;
        trail.numCapVertices = 2;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.16f, 0.88f),
            new Keyframe(0.70f, 0.26f),
            new Keyframe(1f, 0f));

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material material = shader != null ? new Material(shader) : null;
        if (material != null)
        {
            material.name = "Cinematic Wake " + layerName + " (Runtime)";
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            trail.material = material;
        }

        LineRenderer plume = layerObject.AddComponent<LineRenderer>();
        plume.useWorldSpace = false;
        plume.positionCount = 2;
        plume.SetPosition(0, Vector3.zero);
        plume.SetPosition(1, Vector3.back * 0.4f);
        plume.alignment = LineAlignment.View;
        plume.textureMode = LineTextureMode.Stretch;
        plume.numCornerVertices = 3;
        plume.numCapVertices = 2;
        plume.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        plume.receiveShadows = false;
        plume.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.62f),
            new Keyframe(0.24f, 1f),
            new Keyframe(1f, 0f));
        if (material != null) plume.material = material;

        WakeLayer layer = new WakeLayer
        {
            anchor = layerObject.transform,
            trail = trail,
            plume = plume,
            material = material,
            lateral = lateral,
            vertical = vertical,
            widthScale = widthScale,
            timeScale = timeScale,
            phase = phase,
            overchargeOnly = overchargeOnly
        };
        ApplyWakeGradient(layer, 0f);
        _wakeLayers.Add(layer);
    }

    private void UpdateCinematicWake(CraftTelemetry telemetry)
    {
        if (_wakeEngine == null || _wakeLayers.Count == 0)
            return;

        float speedKmh = Mathf.Max(0f, telemetry.speed) * 3.6f;
        float speed01 = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(speedKmh / Mathf.Max(100f, cinematicSpeedKmh)));
        float throttle01 = Mathf.Clamp01(_wakeEngine.Throttle);
        float boost01 = _boostCore != null ? _boostCore.CameraBoost01 : 0f;
        float ignition01 = _boostCore != null ? _boostCore.Ignition01 : 0f;

        if (_boostCore != null && _boostCore.BoostSequenceId != _lastWakeSequence)
        {
            _lastWakeSequence = _boostCore.BoostSequenceId;
            _ignitionFlash = 1f;
            foreach (WakeLayer layer in _wakeLayers)
                layer.trail.Clear();
        }
        _ignitionFlash = Mathf.MoveTowards(_ignitionFlash, 0f, Time.deltaTime * 3.8f);

        float driveEnergy = Mathf.Clamp01(Mathf.Max(throttle01, speed01 * 0.62f));
        float cinematic = Mathf.Clamp01(boost01 * 0.88f + ignition01 * 0.5f);
        float flicker = 0.94f + Mathf.PerlinNoise(Time.time * 15f, 0.37f) * 0.12f;

        foreach (WakeLayer layer in _wakeLayers)
        {
            float target = layer.overchargeOnly
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.72f, cinematic))
                : Mathf.Clamp01(0.10f + driveEnergy * 0.90f);
            layer.smoothedEnergy = Mathf.Lerp(layer.smoothedEnergy, target,
                1f - Mathf.Exp(-Time.deltaTime * (target > layer.smoothedEnergy ? 18f : 5f)));

            float braid = layer.overchargeOnly ? cinematic : cinematic * 0.34f;
            float wave = Mathf.Sin(Time.time * (11f + cinematic * 9f) + layer.phase);
            float lift = Mathf.Cos(Time.time * (9f + cinematic * 7f) + layer.phase);
            layer.anchor.position = _wakeEngine.transform.position
                + transform.right * (layer.lateral + wave * 0.075f * braid)
                + transform.up * (layer.vertical + lift * 0.055f * braid);

            bool movingEnough = speedKmh > 4f || throttle01 > 0.04f || cinematic > 0.02f;
            layer.trail.emitting = movingEnough && layer.smoothedEnergy > 0.025f;

            float baseWidth = Mathf.Lerp(idleWakeWidth, cruiseWakeWidth, driveEnergy);
            float width = Mathf.Lerp(baseWidth, overchargeWakeWidth, cinematic);
            float ignitionExpansion = 1f + _ignitionFlash * (layer.overchargeOnly ? 1.6f : 0.75f);
            layer.trail.startWidth = width * layer.widthScale * ignitionExpansion * flicker;
            layer.trail.time = Mathf.Lerp(0.08f, cruiseWakeTime, driveEnergy) * layer.timeScale
                + overchargeWakeTime * cinematic * (0.72f + 0.28f * layer.timeScale);
            layer.trail.minVertexDistance = Mathf.Lerp(0.20f, 0.85f, speed01);

            if (layer.plume != null)
            {
                float plumeEnergy = layer.overchargeOnly ? layer.smoothedEnergy :
                    Mathf.Max(0.16f, layer.smoothedEnergy);
                layer.plume.enabled = plumeEnergy > 0.025f;
                float plumeLength = Mathf.Lerp(0.42f, 3.4f, driveEnergy)
                    + cinematic * 8.5f + _ignitionFlash * 3.2f;
                layer.plume.SetPosition(0, Vector3.zero);
                layer.plume.SetPosition(1, Vector3.back * plumeLength * layer.timeScale);
                layer.plume.widthMultiplier = width * layer.widthScale
                    * Mathf.Lerp(0.72f, 1.12f, cinematic) * flicker;
            }

            float colorBlend = Mathf.Clamp01(cinematic * 0.82f + _ignitionFlash * 0.3f);
            if (Mathf.Abs(colorBlend - layer.lastColorBlend) > 0.025f)
                ApplyWakeGradient(layer, colorBlend);
        }

        if (_wakeLight != null)
        {
            _wakeLight.transform.position = _wakeEngine.transform.position - transform.forward * 0.18f;
            _wakeLight.color = Color.Lerp(idleWakeColor, overchargeTailColor, cinematic);
            _wakeLight.intensity = (0.45f + driveEnergy * 3.8f + cinematic * 11f
                + _ignitionFlash * 13f) * flicker;
            _wakeLight.range = Mathf.Lerp(3.2f, 13f, Mathf.Clamp01(cinematic + _ignitionFlash * 0.65f));
        }
    }

    private void ApplyWakeGradient(WakeLayer layer, float overchargeBlend)
    {
        if (layer == null || layer.trail == null)
            return;

        Color head = Color.Lerp(cruiseWakeColor, overchargeCoreColor, overchargeBlend);
        Color middle = layer.overchargeOnly
            ? Color.Lerp(cruiseWakeColor, overchargeEdgeColor, overchargeBlend)
            : Color.Lerp(idleWakeColor, overchargeTailColor, overchargeBlend);
        Color tail = Color.Lerp(idleWakeColor, overchargeEdgeColor, overchargeBlend);
        float alpha = layer.overchargeOnly ? Mathf.Lerp(0.45f, 1f, overchargeBlend) : 1f;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(head, 0f),
                new GradientColorKey(middle, 0.28f),
                new GradientColorKey(tail, 0.78f),
                new GradientColorKey(tail * 0.32f, 1f)
            },
            new[]
            {
                new GradientAlphaKey(alpha, 0f),
                new GradientAlphaKey(alpha * 0.86f, 0.35f),
                new GradientAlphaKey(alpha * 0.32f, 0.82f),
                new GradientAlphaKey(0f, 1f)
            });
        layer.trail.colorGradient = gradient;
        if (layer.plume != null)
            layer.plume.colorGradient = gradient;
        layer.lastColorBlend = overchargeBlend;
    }

    private void GetRoleColors(ThrusterNode.ThrusterRole role, out Color idleColor, out Color activeColor)
    {
        switch (role)
        {
            case ThrusterNode.ThrusterRole.Hover:
                idleColor = new Color(0.08f, 0.08f, 0.15f);
                activeColor = new Color(0.3f, 0.7f, 1.0f); // cool blue
                break;
            case ThrusterNode.ThrusterRole.Main:
                idleColor = new Color(0.15f, 0.08f, 0.02f);
                activeColor = new Color(1.0f, 0.5f, 0.1f); // warm orange
                break;
            case ThrusterNode.ThrusterRole.Brake:
                idleColor = new Color(0.12f, 0.02f, 0.02f);
                activeColor = new Color(1.0f, 0.15f, 0.1f); // red
                break;
            case ThrusterNode.ThrusterRole.Strafe:
                idleColor = new Color(0.05f, 0.12f, 0.05f);
                activeColor = new Color(0.2f, 1.0f, 0.3f); // green
                break;
            default:
                idleColor = new Color(0.1f, 0.1f, 0.1f);
                activeColor = Color.white;
                break;
        }
    }
}
