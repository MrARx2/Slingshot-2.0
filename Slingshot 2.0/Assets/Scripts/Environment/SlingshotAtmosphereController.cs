using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Lightweight, scene-wide atmosphere for Slingshot. It drives Unity's native fog
/// uniforms, so every compatible URP material receives distance depth without a
/// fullscreen raymarch, fog particles, cameras, or render textures.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SlingshotAtmosphereController : MonoBehaviour
{
    public enum AtmospherePreset
    {
        CleanRace,
        DeepSpace,
        CinematicNebula,
        Custom
    }

    [Header("Atmosphere Look")]
    [Tooltip("Choose a starting look. Change the controls below afterwards to art-direct it.")]
    public AtmospherePreset preset = AtmospherePreset.DeepSpace;

    [Tooltip("The color distant track geometry dissolves into. A dark blue-green keeps cyan guides readable.")]
    [ColorUsage(false, false)]
    public Color atmosphereColor = new Color(0.018f, 0.052f, 0.068f, 1f);

    [Tooltip("Approximate distance where the atmosphere becomes visually dominant. Larger values keep more of the generated track visible.")]
    [Min(100f)]
    public float visibilityDistance = 1500f;

    [Tooltip("Overall depth strength without changing the chosen visibility distance.")]
    [Range(0f, 2f)]
    public float depthStrength = 1f;

    [Header("Sense Of Speed")]
    [Tooltip("At high speed, gently opens visibility so the player can read the road ahead. This never removes the atmosphere entirely.")]
    public bool speedResponsive = true;

    [Tooltip("Optional craft body. When empty, the system finds the active CraftCore automatically in Play Mode.")]
    public Rigidbody speedSource;

    [Tooltip("Speed at which the atmosphere begins opening up.")]
    [Min(0f)]
    public float responseStartKmh = 650f;

    [Tooltip("Speed at which the visibility boost reaches its maximum.")]
    [Min(1f)]
    public float responseFullKmh = 1800f;

    [Tooltip("Maximum visibility-distance multiplier at high speed.")]
    [Range(1f, 1.75f)]
    public float highSpeedVisibility = 1.3f;

    [Tooltip("How quickly atmosphere responds without visibly pumping during acceleration.")]
    [Range(0.5f, 12f)]
    public float responseSmoothing = 4f;

    [Header("System")]
    [Tooltip("Master switch. Disabling the component also restores the scene's previous fog settings.")]
    public bool atmosphereEnabled = true;

    [SerializeField, HideInInspector]
    private AtmospherePreset _lastAppliedPreset = (AtmospherePreset)(-1);

    private bool _capturedSettings;
    private bool _previousFog;
    private FogMode _previousMode;
    private Color _previousColor;
    private float _previousDensity;
    private float _previousStart;
    private float _previousEnd;
    private float _smoothedSpeed01;
    private float _nextSourceSearchTime;

    private const float DominantFogOpticalDepth = 1.4f;

    private void OnEnable()
    {
        CaptureSettings();
        ApplyAtmosphere(true);
    }

    private void Update()
    {
        ApplyAtmosphere(false);
    }

    private void OnValidate()
    {
        visibilityDistance = Mathf.Max(100f, visibilityDistance);
        depthStrength = Mathf.Clamp(depthStrength, 0f, 2f);
        responseStartKmh = Mathf.Max(0f, responseStartKmh);
        responseFullKmh = Mathf.Max(responseStartKmh + 1f, responseFullKmh);
        highSpeedVisibility = Mathf.Clamp(highSpeedVisibility, 1f, 1.75f);
        responseSmoothing = Mathf.Clamp(responseSmoothing, 0.5f, 12f);

        if (preset != AtmospherePreset.Custom && preset != _lastAppliedPreset)
            ApplyPresetValues(preset);

        ApplyAtmosphere(true);
    }

    private void OnDisable()
    {
        RestoreSettings();
    }

    [ContextMenu("Apply Selected Atmosphere Preset")]
    private void ApplySelectedPreset()
    {
        if (preset != AtmospherePreset.Custom)
            ApplyPresetValues(preset);

        ApplyAtmosphere(true);
    }

    private void ApplyPresetValues(AtmospherePreset selected)
    {
        switch (selected)
        {
            case AtmospherePreset.CleanRace:
                atmosphereColor = new Color(0.012f, 0.037f, 0.048f, 1f);
                visibilityDistance = 2200f;
                depthStrength = 0.78f;
                highSpeedVisibility = 1.22f;
                break;

            case AtmospherePreset.DeepSpace:
                atmosphereColor = new Color(0.018f, 0.052f, 0.068f, 1f);
                visibilityDistance = 1500f;
                depthStrength = 1f;
                highSpeedVisibility = 1.3f;
                break;

            case AtmospherePreset.CinematicNebula:
                atmosphereColor = new Color(0.048f, 0.028f, 0.075f, 1f);
                visibilityDistance = 1100f;
                depthStrength = 1.12f;
                highSpeedVisibility = 1.38f;
                break;
        }

        _lastAppliedPreset = selected;
    }

    private void ApplyAtmosphere(bool immediate)
    {
        // A prefab asset selected in the Project window must never take ownership of
        // the active scene's global RenderSettings.
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded || !isActiveAndEnabled)
            return;

        CaptureSettings();

        if (!atmosphereEnabled || depthStrength <= 0.0001f)
        {
            RenderSettings.fog = false;
            return;
        }

        float targetSpeed01 = ResolveSpeed01();
        if (!Application.isPlaying || immediate)
        {
            _smoothedSpeed01 = targetSpeed01;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-responseSmoothing * Time.unscaledDeltaTime);
            _smoothedSpeed01 = Mathf.Lerp(_smoothedSpeed01, targetSpeed01, blend);
        }

        float visibilityMultiplier = Mathf.Lerp(1f, highSpeedVisibility,
            Mathf.SmoothStep(0f, 1f, _smoothedSpeed01));
        float effectiveVisibility = visibilityDistance * visibilityMultiplier;
        float density = DominantFogOpticalDepth / Mathf.Max(100f, effectiveVisibility);
        density *= depthStrength;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = atmosphereColor;
        RenderSettings.fogDensity = Mathf.Clamp(density, 0.00001f, 0.02f);
    }

    private float ResolveSpeed01()
    {
        if (!Application.isPlaying || !speedResponsive)
            return 0f;

        if (speedSource == null && Time.unscaledTime >= _nextSourceSearchTime)
        {
            CraftCore craft = FindAnyObjectByType<CraftCore>();
            if (craft != null)
                speedSource = craft.GetComponent<Rigidbody>();

            _nextSourceSearchTime = Time.unscaledTime + 1f;
        }

        if (speedSource == null)
            return 0f;

        float speedKmh = speedSource.linearVelocity.magnitude * 3.6f;
        return Mathf.InverseLerp(responseStartKmh, responseFullKmh, speedKmh);
    }

    private void CaptureSettings()
    {
        if (_capturedSettings)
            return;

        _previousFog = RenderSettings.fog;
        _previousMode = RenderSettings.fogMode;
        _previousColor = RenderSettings.fogColor;
        _previousDensity = RenderSettings.fogDensity;
        _previousStart = RenderSettings.fogStartDistance;
        _previousEnd = RenderSettings.fogEndDistance;
        _capturedSettings = true;
    }

    private void RestoreSettings()
    {
        if (!_capturedSettings)
            return;

        RenderSettings.fog = _previousFog;
        RenderSettings.fogMode = _previousMode;
        RenderSettings.fogColor = _previousColor;
        RenderSettings.fogDensity = _previousDensity;
        RenderSettings.fogStartDistance = _previousStart;
        RenderSettings.fogEndDistance = _previousEnd;
        _capturedSettings = false;
    }
}
