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

    // FindAnyObjectByType scans the whole scene — retry at most once per second
    // instead of every frame while no craft exists.
    private float _nextAutoAssignTime;

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

        if (craftCore == null && Time.unscaledTime >= _nextAutoAssignTime)
        {
            _nextAutoAssignTime = Time.unscaledTime + 1f;
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
        DrawBoost(ref y, x);
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

    private void DrawBoost(ref float y, float x)
    {
        OverchargeCore boost = craftCore.overcharge;
        if (boost == null)
        {
            DrawLabel(ref y, x, "Boost", "missing", dangerColor);
            return;
        }

        string status;
        Color c;
        if (boost.IsBoosting)
        {
            status = $"BOOSTING  env {(boost.BoostEnvelope01 * 100f):0}%  grant {(boost.GrantedBoostStrength):0.0}";
            c = normalColor;
        }
        else if (boost.CanBoost)
        {
            status = "READY";
            c = goodColor;
        }
        else
        {
            status = $"COOLDOWN {boost.CooldownRemaining:0.0}s";
            c = warningColor;
        }

        DrawLabel(ref y, x, "Boost", status, c);
        DrawMiniBar(ref y, x, "Readiness", boost.BoostReadiness01, boost.CanBoost ? goodColor : warningColor);
        DrawMiniBar(ref y, x, "Envelope", boost.BoostEnvelope01, normalColor);
        DrawLabel(ref y, x, "Seq", boost.BoostSequenceId.ToString(), mutedColor);
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
        float protectedRequest = (e.baseHoverProtected ? e.baseHoverRequest : 0f)
                               + (e.stabilizerProtected ? e.stabilizerRequest : 0f);
        float protectedGranted = (e.baseHoverProtected ? e.baseHoverGranted : 0f)
                               + (e.stabilizerProtected ? e.stabilizerGranted : 0f);
        float sharedRequested = Mathf.Max(0f, e.totalRequested - protectedRequest);
        float sharedGranted = Mathf.Max(0f, e.totalGranted - protectedGranted);
        string header = $"{sharedGranted:0}/{e.totalBudget:0} shared";
        if (sharedRequested > e.totalBudget)
        {
            header += $"  OVERLOAD {(e.overload01 * 100f):0}%";
        }

        DrawLabel(ref y, x, "Reactor BUS", header, e.overload01 > 0f ? warningColor : goodColor);
        // The four control slots (Steer > Boost > Drive > Vertical), then the free
        // auto systems. Each bar's right column shows its THROTTLE % when over budget.
        DrawEnergyBar(ref y, x, "Steer", e.vectoringRequest, e.vectoringGranted, e.totalBudget, e.vectoringPower01);
        DrawEnergyBar(ref y, x, "Boost", e.overchargeRequest, e.overchargeGranted, e.totalBudget, e.overchargePower01);
        DrawEnergyBar(ref y, x, "Drive", e.driveRequest, e.driveGranted, e.totalBudget, e.drivePower01);
        DrawEnergyBar(ref y, x, "Vertical Q/E",
            Mathf.Max(e.roofRequest, e.bottomRequest), Mathf.Max(e.roofGranted, e.bottomGranted),
            e.totalBudget, Mathf.Min(e.roofPower01, e.bottomPower01));
        DrawEnergyBar(ref y, x, "Hover [free]", e.baseHoverRequest, e.baseHoverGranted, Mathf.Max(e.totalBudget, e.totalGranted), e.baseHoverPower01);
        DrawEnergyBar(ref y, x, e.stabilizerProtected ? "Stabilizer [free]" : "Stabilizer", e.stabilizerRequest, e.stabilizerGranted, Mathf.Max(e.totalBudget, e.stabilizerGranted), e.stabilizerPower01);
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

        // Right column: when the channel is being throttled by the BUS, call it out as
        // "THR xx%"; otherwise just show the granted power.
        bool throttled = request > 0.001f && power01 < 0.985f;
        string right = throttled ? $"THR {(power01 * 100f):0}%" : $"{granted:0}";
        GUI.color = throttled ? warningColor : textColor;
        GUI.Label(new Rect(x + 95f + barWidth, y, 92f, rowHeight), right, _smallStyle);
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
