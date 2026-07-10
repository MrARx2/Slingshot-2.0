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

    // ══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ══════════════════════════════════════════════════════════════

    private Material _gripBreakerIndicatorMaterial;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private ThrusterBus _thrusterBus;

    // ══════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════

    private void Awake()
    {
        _thrusterBus = GetComponentInParent<ThrusterBus>();
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
    }

    private void OnDestroy()
    {
        // Clean up instantiated material
        if (_gripBreakerIndicatorMaterial != null)
        {
            Destroy(_gripBreakerIndicatorMaterial);
        }
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

            // ── Update Trail Renderer ──────────────────────────────
            if (visual.trailRenderer != null)
            {
                float activeThreshold = 0.05f;
                if (throttle > activeThreshold)
                {
                    float cappedThrottle = Mathf.Clamp01(throttle);
                    visual.trailRenderer.emitting = true;
                    visual.trailRenderer.startWidth = visual.originalTrailWidth * cappedThrottle;
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
                Color emissiveColor = throttle > 0.05f
                    ? activeColor * (t * 2.5f) // HDR boost
                    : Color.black;

                visual.indicatorRenderer.GetPropertyBlock(visual.propBlock);
                visual.propBlock.SetColor("_BaseColor", baseColor);
                visual.propBlock.SetColor("_EmissionColor", emissiveColor);
                visual.indicatorRenderer.SetPropertyBlock(visual.propBlock);
            }
        }
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
