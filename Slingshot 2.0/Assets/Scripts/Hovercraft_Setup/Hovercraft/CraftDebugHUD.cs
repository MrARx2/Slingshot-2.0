using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lightweight V2 debug HUD for the hovercraft.
/// <para>
/// This is intentionally separate from HovercraftCamera. The camera controls
/// view behavior only; this HUD reads V2 craft systems and displays stabilizer,
/// overcharge, energy distribution, grip, and basic telemetry.
/// </para>
/// <para>
/// Uses OnGUI for fast iteration. Replace with a proper Canvas later once the
/// mechanics are locked.
/// </para>
/// </summary>
public class CraftDebugHUD : MonoBehaviour
{
    [Header("References")]
    public CraftCore craftCore;

    [Header("Visibility")]
    public bool showHUD = false;

    [Tooltip("Input System binding path used to toggle the HUD. Default is F2.")]
    public string toggleBinding = "<Keyboard>/f2";

    [Header("Layout")]
    public Vector2 screenOffset = new Vector2(20f, 20f);
    public float panelWidth = 360f;
    public float barWidth = 220f;
    public float barHeight = 12f;
    public float rowHeight = 20f;

    [Header("Colors")]
    public Color panelColor = new Color(0f, 0f, 0f, 0.55f);
    public Color textColor = Color.white;
    public Color normalColor = new Color(0.25f, 0.75f, 1f, 1f);
    public Color warningColor = new Color(1f, 0.7f, 0.2f, 1f);
    public Color dangerColor = new Color(1f, 0.25f, 0.2f, 1f);
    public Color goodColor = new Color(0.35f, 1f, 0.45f, 1f);
    public Color mutedColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private GUIStyle _labelStyle;
    private GUIStyle _smallStyle;
    private GUIStyle _headerStyle;

    private InputAction _toggleHudAction;

    private void Awake()
    {
        TryAutoAssign();
        SetupToggleAction();
    }

    private void OnEnable()
    {
        if (_toggleHudAction == null)
        {
            SetupToggleAction();
        }

        _toggleHudAction?.Enable();
    }

    private void OnDisable()
    {
        _toggleHudAction?.Disable();
    }

    private void OnDestroy()
    {
        _toggleHudAction?.Dispose();
        _toggleHudAction = null;
    }

    private void Update()
    {
        // New Input System only. This avoids UnityEngine.Input and also avoids
        // Keyboard.current[Key] indexing, which can throw on some Input System versions.
        if (_toggleHudAction != null && _toggleHudAction.WasPressedThisFrame())
        {
            showHUD = !showHUD;
        }

        if (craftCore == null)
        {
            TryAutoAssign();
        }
    }

    private void SetupToggleAction()
    {
        _toggleHudAction?.Dispose();

        string binding = string.IsNullOrWhiteSpace(toggleBinding)
            ? "<Keyboard>/f2"
            : toggleBinding;

        _toggleHudAction = new InputAction(
            "Toggle Craft Debug HUD",
            InputActionType.Button,
            binding
        );
    }

    private void TryAutoAssign()
    {
        if (craftCore == null)
        {
            #if UNITY_2023_1_OR_NEWER
            craftCore = FindAnyObjectByType<CraftCore>();
#else
            craftCore = FindObjectOfType<CraftCore>();
#endif
        }
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null) return;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = textColor }
        };

        _smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = textColor }
        };

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = textColor }
        };
    }

    private void OnGUI()
    {
        if (!showHUD || craftCore == null) return;

        EnsureStyles();

        float x = screenOffset.x;
        float y = screenOffset.y;
        float h = 300f;

        GUI.color = panelColor;
        GUI.Box(new Rect(x - 10f, y - 10f, panelWidth, h), GUIContent.none);
        GUI.color = Color.white;

        DrawHeader(ref y, x, "CRAFT DEBUG");
        DrawTelemetry(ref y, x);
        y += 6f;
        DrawStabilizer(ref y, x);
        y += 6f;
        DrawOvercharge(ref y, x);
        y += 6f;
        DrawEnergy(ref y, x);
        y += 6f;
        DrawGrip(ref y, x);
    }

    private void DrawHeader(ref float y, float x, string text)
    {
        GUI.Label(new Rect(x, y, panelWidth - 20f, rowHeight), text, _headerStyle);
        y += rowHeight + 2f;
    }

    private void DrawTelemetry(ref float y, float x)
    {
        CraftTelemetry t = craftCore.telemetry != null
            ? craftCore.telemetry.CurrentTelemetry
            : default;

        string state = t.isInverted ? "INVERTED" : t.isGrounded ? "GROUNDED" : t.hasSurfaceContact ? "SURFACE" : "AIRBORNE";
        Color stateColor = t.isInverted ? warningColor : t.isGrounded ? goodColor : normalColor;

        DrawLabel(ref y, x, "State", state, stateColor);
        DrawLabel(ref y, x, "Speed", (t.speed * 3.6f).ToString("0") + " km/h", textColor);
    }

    private void DrawStabilizer(ref float y, float x)
    {
        HoverStabilizerArray hover = craftCore.hoverArray;
        if (hover == null)
        {
            DrawLabel(ref y, x, "Stabilizer", "missing", dangerColor);
            return;
        }

        string status;
        Color color;

        if (!hover.IsStabilizerArmed)
        {
            status = "DISENGAGED";
            color = mutedColor;
        }
        else if (hover.IsStabilizerActive)
        {
            status = "ACTIVE";
            color = goodColor;
        }
        else
        {
            status = "ARMED";
            color = normalColor;
        }

        DrawLabel(ref y, x, "Stabilizer", status, color);
        DrawMiniBar(ref y, x, "Hover", hover.AverageAutoHoverThrottle / Mathf.Max(0.001f, hover.hoverMaxThrottle), normalColor);
        DrawMiniBar(ref y, x, "Roof", hover.AverageAutoRoofThrottle / Mathf.Max(0.001f, hover.roofStabilizerMaxThrottle), warningColor);
    }

    private void DrawOvercharge(ref float y, float x)
    {
        OverchargeCore over = craftCore.overcharge;
        if (over == null)
        {
            DrawLabel(ref y, x, "Overcharge", "missing", dangerColor);
            return;
        }

        string target = over.IsCharging
            ? over.ChargingTarget.ToString()
            : over.IsBursting
                ? over.ActiveBurstTarget.ToString()
                : "Idle";

        string status = over.IsCharging
            ? $"CHARGING {target} {(over.Charge01 * 100f):0}%"
            : over.IsBursting
                ? $"BURST {target}"
                : "IDLE";

        Color c = over.IsCharging ? warningColor : over.IsBursting ? dangerColor : mutedColor;
        DrawLabel(ref y, x, "Overcharge", status, c);
        DrawMiniBar(ref y, x, "Charge", over.Charge01, warningColor);
    }

    private void DrawEnergy(ref float y, float x)
    {
        EnergyCore energy = craftCore.energy;
        if (energy == null)
        {
            DrawLabel(ref y, x, "Energy Bus", "missing", dangerColor);
            return;
        }

        EnergyState e = energy.CurrentEnergy;
        string header = $"{e.totalGranted:0}/{e.totalBudget:0} used";
        if (e.totalRequested > e.totalBudget)
        {
            header += $"  OVERLOAD {(e.overload01 * 100f):0}%";
        }

        DrawLabel(ref y, x, "Energy Bus", header, e.overload01 > 0f ? warningColor : goodColor);
        DrawEnergyBar(ref y, x, "Base Hover", e.baseHoverRequest, e.baseHoverGranted, Mathf.Max(e.totalBudget, e.totalGranted), e.baseHoverPower01);
        DrawEnergyBar(ref y, x, "Drive", e.driveRequest, e.driveGranted, e.totalBudget, e.drivePower01);
        DrawEnergyBar(ref y, x, "Vector", e.vectoringRequest, e.vectoringGranted, e.totalBudget, e.vectoringPower01);
        DrawEnergyBar(ref y, x, "Roof Q", e.roofRequest, e.roofGranted, e.totalBudget, e.roofPower01);
        DrawEnergyBar(ref y, x, "Bottom E", e.bottomRequest, e.bottomGranted, e.totalBudget, e.bottomPower01);
        DrawEnergyBar(ref y, x, "Overcharge", e.overchargeRequest, e.overchargeGranted, e.totalBudget, e.overchargePower01);
        DrawEnergyBar(ref y, x, "Stabilizer", e.stabilizerRequest, e.stabilizerGranted, e.totalBudget, e.stabilizerPower01);
    }

    private void DrawGrip(ref float y, float x)
    {
        TractionCore traction = craftCore.traction;
        if (traction == null)
        {
            DrawLabel(ref y, x, "Grip", "missing", dangerColor);
            return;
        }

        TractionState t = traction.CurrentTraction;
        DrawMiniBar(ref y, x, "Grip Break", t.gripBreakerAmount, dangerColor);
    }

    private void DrawLabel(ref float y, float x, string key, string value, Color valueColor)
    {
        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 110f, rowHeight), key, _labelStyle);
        GUI.color = valueColor;
        GUI.Label(new Rect(x + 112f, y, panelWidth - 130f, rowHeight), value, _labelStyle);
        GUI.color = Color.white;
        y += rowHeight;
    }

    private void DrawMiniBar(ref float y, float x, string label, float value01, Color fill)
    {
        value01 = Mathf.Clamp01(value01);
        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 85f, rowHeight), label, _smallStyle);
        DrawBar(new Rect(x + 90f, y + 4f, barWidth, barHeight), value01, fill, mutedColor);
        GUI.color = textColor;
        GUI.Label(new Rect(x + 95f + barWidth, y, 45f, rowHeight), (value01 * 100f).ToString("0") + "%", _smallStyle);
        GUI.color = Color.white;
        y += rowHeight;
    }

    private void DrawEnergyBar(ref float y, float x, string label, float request, float granted, float budget, float power01)
    {
        float request01 = budget > 0.001f ? Mathf.Clamp01(request / budget) : 0f;
        float grant01 = budget > 0.001f ? Mathf.Clamp01(granted / budget) : 0f;
        Color fill = power01 < 0.65f ? warningColor : normalColor;

        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 85f, rowHeight), label, _smallStyle);

        Rect r = new Rect(x + 90f, y + 4f, barWidth, barHeight);
        DrawBar(r, request01, new Color(0.18f, 0.18f, 0.18f, 1f), mutedColor);
        DrawBar(r, grant01, fill, Color.clear);

        GUI.color = textColor;
        GUI.Label(new Rect(x + 95f + barWidth, y, 70f, rowHeight), $"{granted:0}/{request:0}", _smallStyle);
        GUI.color = Color.white;
        y += rowHeight;
    }

    private void DrawBar(Rect rect, float fill01, Color fill, Color background)
    {
        if (background.a > 0f)
        {
            GUI.color = background;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        GUI.color = fill;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill01), rect.height), Texture2D.whiteTexture);

        GUI.color = Color.black;
        GUI.Box(rect, GUIContent.none);
        GUI.color = Color.white;
    }
}
