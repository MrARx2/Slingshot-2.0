using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// Placement-ready HDRI environment controller for URP.
/// Rotate the GameObject or use pitch/yaw/roll offsets to art-direct the panorama.
/// Position and scale intentionally have no effect because a sky environment is infinite.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class HDRIEnvironmentController : MonoBehaviour
{
    [Header("Environment Source")]
    [Tooltip("Latitude-longitude HDR panorama used for the sky and environment reflections.")]
    public Texture hdriTexture;

    [Header("Art Direction")]
    [Min(0f)] public float exposure = 1f;
    [Tooltip("Controls the apparent angular size of the panorama. 1 is the source HDRI. Higher values make stars and nebulae smaller and feel farther away; lower values make them larger and closer. This is an artistic scale control because a skybox is physically infinite.")]
    [Range(0.5f, 6f)] public float apparentDistance = 1f;
    [Tooltip("Moves the HDRI view up/down around the local X axis.")]
    [Range(-180f, 180f)] public float pitchOffset = 0f;
    [Tooltip("Turns the HDRI left/right around the local Y axis.")]
    [FormerlySerializedAs("rotationOffset")]
    [Range(-180f, 180f)] public float yawOffset = 0f;
    [Tooltip("Rolls the HDRI horizon around the local Z axis.")]
    [Range(-180f, 180f)] public float rollOffset = 0f;
    [ColorUsage(false, true)] public Color tint = Color.white;

    [Header("Scene Lighting")]
    public bool driveAmbientLighting = true;
    [Min(0f)] public float ambientIntensity = 1f;
    [Min(0f)] public float reflectionIntensity = 1f;
    [Tooltip("Refresh Unity's ambient probe after the environment changes. Disable only if another system owns GI updates.")]
    public bool refreshEnvironmentLighting = true;

    private Material _runtimeSkybox;
    private Material _previousSkybox;
    private AmbientMode _previousAmbientMode;
    private DefaultReflectionMode _previousReflectionMode;
    private float _previousAmbientIntensity;
    private float _previousReflectionIntensity;
    private bool _capturedPreviousSettings;

    private Texture _appliedTexture;
    private Color _appliedTint;
    private float _appliedExposure = float.NaN;
    private float _appliedApparentDistance = float.NaN;
    private Vector3 _appliedRotation = new Vector3(float.NaN, float.NaN, float.NaN);
    private float _appliedAmbientIntensity = float.NaN;
    private float _appliedReflectionIntensity = float.NaN;
    private bool _appliedDriveAmbient;

    private void OnEnable()
    {
        CapturePreviousSettings();
        ApplyEnvironment(true);
    }

    private void Update()
    {
        ApplyEnvironment(false);
    }

    private void OnValidate()
    {
        exposure = Mathf.Max(0f, exposure);
        apparentDistance = Mathf.Clamp(apparentDistance, 0.5f, 6f);
        ambientIntensity = Mathf.Max(0f, ambientIntensity);
        reflectionIntensity = Mathf.Max(0f, reflectionIntensity);
        ApplyEnvironment(true);
    }

    private void OnDisable()
    {
        if (_runtimeSkybox != null && RenderSettings.skybox == _runtimeSkybox && _capturedPreviousSettings)
        {
            RenderSettings.skybox = _previousSkybox;
            RenderSettings.ambientMode = _previousAmbientMode;
            RenderSettings.defaultReflectionMode = _previousReflectionMode;
            RenderSettings.ambientIntensity = _previousAmbientIntensity;
            RenderSettings.reflectionIntensity = _previousReflectionIntensity;

            if (refreshEnvironmentLighting && Application.isPlaying)
                DynamicGI.UpdateEnvironment();
        }

        DestroyRuntimeMaterial();
    }

    private void CapturePreviousSettings()
    {
        if (_capturedPreviousSettings)
            return;

        _previousSkybox = RenderSettings.skybox;
        _previousAmbientMode = RenderSettings.ambientMode;
        _previousReflectionMode = RenderSettings.defaultReflectionMode;
        _previousAmbientIntensity = RenderSettings.ambientIntensity;
        _previousReflectionIntensity = RenderSettings.reflectionIntensity;
        _capturedPreviousSettings = true;
    }

    private void ApplyEnvironment(bool force)
    {
        // Do not let an imported/selected prefab asset change the Editor's active scene sky.
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded ||
            !isActiveAndEnabled || hdriTexture == null)
            return;

        if (_runtimeSkybox == null)
        {
            Shader panoramicShader = Shader.Find("Slingshot/Skybox HDRI 3D Rotation");
            if (panoramicShader == null)
            {
                Debug.LogError("HDRI Environment requires the Slingshot 3D-rotation skybox shader.", this);
                return;
            }

            _runtimeSkybox = new Material(panoramicShader)
            {
                name = name + " HDRI Skybox (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            force = true;
        }

        Vector3 rotation = transform.eulerAngles + new Vector3(
            pitchOffset,
            yawOffset,
            rollOffset);
        bool changed = force ||
            _appliedTexture != hdriTexture ||
            _appliedTint != tint ||
            !Mathf.Approximately(_appliedExposure, exposure) ||
            !Mathf.Approximately(_appliedApparentDistance, apparentDistance) ||
            _appliedRotation != rotation ||
            !Mathf.Approximately(_appliedAmbientIntensity, ambientIntensity) ||
            !Mathf.Approximately(_appliedReflectionIntensity, reflectionIntensity) ||
            _appliedDriveAmbient != driveAmbientLighting;

        if (!changed)
            return;

        _runtimeSkybox.SetTexture("_MainTex", hdriTexture);
        _runtimeSkybox.SetColor("_Tint", tint);
        _runtimeSkybox.SetFloat("_Exposure", exposure);
        _runtimeSkybox.SetFloat("_ApparentDistance", apparentDistance);
        _runtimeSkybox.SetVector("_RotationXYZ", rotation);

        RenderSettings.skybox = _runtimeSkybox;
        RenderSettings.reflectionIntensity = reflectionIntensity;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

        if (driveAmbientLighting)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = ambientIntensity;
        }

        _appliedTexture = hdriTexture;
        _appliedTint = tint;
        _appliedExposure = exposure;
        _appliedApparentDistance = apparentDistance;
        _appliedRotation = rotation;
        _appliedAmbientIntensity = ambientIntensity;
        _appliedReflectionIntensity = reflectionIntensity;
        _appliedDriveAmbient = driveAmbientLighting;

        // Runtime refresh is immediate. In Edit Mode Unity refreshes the sky through its normal repaint cycle.
        if (refreshEnvironmentLighting && Application.isPlaying)
            DynamicGI.UpdateEnvironment();
    }

    private void DestroyRuntimeMaterial()
    {
        if (_runtimeSkybox == null)
            return;

        if (Application.isPlaying)
            Destroy(_runtimeSkybox);
        else
            DestroyImmediate(_runtimeSkybox);

        _runtimeSkybox = null;
    }
}
