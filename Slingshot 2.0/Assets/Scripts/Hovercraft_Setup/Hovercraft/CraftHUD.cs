using UnityEngine;

/// <summary>
/// Player-facing V2 hovercraft HUD.
/// <para>
/// This is the always-on driving HUD, not a debug overlay. It shows only the
/// information the player needs while piloting: speed, craft state, stabilizer,
/// Grip Breaker, Overcharge, Q/E vertical thrusters, and compact energy usage.
/// </para>
/// <para>
/// Keep this separate from <see cref="HovercraftCamera"/>. The camera owns view
/// behavior; this script owns screen-space driving information.
/// </para>
/// </summary>
public class CraftHUD : MonoBehaviour
{
    [Header("References")]
    public CraftCore craftCore;

    [Header("Visibility")]
    [Tooltip("Player HUD visibility. This is intended to stay enabled during normal play.")]
    public bool showHUD = true;

    [Tooltip("If true, missing CraftCore is auto-found at runtime.")]
    public bool autoFindCraftCore = true;

    [Header("Layout")]
    [Tooltip("Distance from screen edges for HUD groups.")]
    public Vector2 margin = new Vector2(24f, 20f);

    public float barWidth = 190f;
    public float barHeight = 12f;
    public float rowHeight = 19f;

    [Tooltip("Scale applied to all HUD elements.")]
    [Range(0.6f, 1.6f)] public float uiScale = 1.0f;

    [Header("Panels")]
    public bool showSpeedPanel = true;
    public bool showSystemsPanel = true;
    public bool showEnergyPanel = true;

    [Header("Speed Display (bottom center)")]
    [Tooltip("Distance from the bottom safe-area edge to the speed readout (pixels at 1080p, scales with resolution).")]
    public float bottomOffset = 42f;

    [Tooltip("Font size of the big speed number (at 1080p).")]
    public int speedFontSize = 64;

    [Tooltip("Font size of the KM/H unit label (at 1080p).")]
    public int unitFontSize = 20;

    [Tooltip("Show the KM/H unit label under the number.")]
    public bool showUnit = true;

    [Tooltip("Display smoothing time constant in seconds. Small values only — the readout must never noticeably lag the craft. 0 = raw.")]
    [Range(0f, 0.3f)] public float displaySmoothing = 0.08f;

    private float _smoothedSpeedKmh;
    private GUIStyle _speedNumberStyle;
    private GUIStyle _speedUnitStyle;
    private int _speedStyleSizeCache = -1;

    [Header("Colors")]
    public Color panelColor = new Color(0f, 0f, 0f, 0.42f);
    public Color textColor = new Color(0.92f, 0.97f, 1f, 1f);
    public Color mutedColor = new Color(0.45f, 0.52f, 0.58f, 1f);
    public Color cyanColor = new Color(0.25f, 0.82f, 1f, 1f);
    public Color greenColor = new Color(0.35f, 1f, 0.45f, 1f);
    public Color yellowColor = new Color(1f, 0.75f, 0.25f, 1f);
    public Color redColor = new Color(1f, 0.25f, 0.2f, 1f);
    public Color purpleColor = new Color(0.85f, 0.45f, 1f, 1f);

    private GUIStyle _headerStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _smallStyle;
    private GUIStyle _largeStyle;

    private void Awake()
    {
        TryAutoAssign();
    }

    // FindAnyObjectByType scans the whole scene — retry at most once per second
    // instead of every frame while no craft exists.
    private float _nextAutoAssignTime;

    private void Update()
    {
        if (craftCore == null && autoFindCraftCore && Time.unscaledTime >= _nextAutoAssignTime)
        {
            _nextAutoAssignTime = Time.unscaledTime + 1f;
            TryAutoAssign();
        }

        // Exponential display smoothing: converges within ~3τ (≤ a quarter second at
        // the default), so the readout stays calm without noticeable lag at 1300 km/h.
        if (craftCore != null)
        {
            float speedKmh = GetTelemetry().speed * 3.6f;
            if (displaySmoothing <= 0.001f)
                _smoothedSpeedKmh = speedKmh;
            else
                _smoothedSpeedKmh = Mathf.Lerp(_smoothedSpeedKmh, speedKmh,
                    1f - Mathf.Exp(-Time.deltaTime / displaySmoothing));
        }
    }

    private void TryAutoAssign()
    {
        if (!autoFindCraftCore || craftCore != null) return;

#if UNITY_2023_1_OR_NEWER
        craftCore = FindAnyObjectByType<CraftCore>();
#else
        craftCore = FindObjectOfType<CraftCore>();
#endif
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null) return;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(14f * uiScale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = textColor }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(13f * uiScale),
            normal = { textColor = textColor }
        };

        _smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(11f * uiScale),
            normal = { textColor = textColor }
        };

        _largeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(28f * uiScale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight,
            normal = { textColor = textColor }
        };
    }

    private void OnGUI()
    {
        if (!showHUD || craftCore == null) return;

        EnsureStyles();

        if (showSpeedPanel)
        {
            DrawSpeedPanel();
        }

        if (showSystemsPanel)
        {
            DrawSystemsPanel();
        }

        if (showEnergyPanel)
        {
            DrawEnergyPanel();
        }
    }

    private void DrawSpeedPanel()
    {
        CraftTelemetry telemetry = GetTelemetry();

        // ── Bottom-center speed readout ──
        // Resolution scaling keys off HEIGHT so ultrawide monitors keep the same
        // physical size; the safe area keeps it clear of notches/rounded corners.
        float resScale = Screen.height / 1080f * uiScale;
        Rect safe = Screen.safeArea;

        int numberSize = Mathf.RoundToInt(speedFontSize * resScale);
        int unitSize = Mathf.RoundToInt(unitFontSize * resScale);
        if (_speedNumberStyle == null || _speedStyleSizeCache != numberSize)
        {
            _speedStyleSizeCache = numberSize;
            _speedNumberStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = numberSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerCenter,
                normal = { textColor = textColor }
            };
            _speedUnitStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = unitSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = mutedColor }
            };
        }

        // Screen.safeArea has a bottom-left origin; OnGUI a top-left one — the safe
        // area's bottom edge in GUI space is Screen.height − safe.y.
        float safeBottomY = Screen.height - safe.y;
        float unitHeight = showUnit ? unitSize * 1.35f : 0f;
        float numberHeight = numberSize * 1.25f;
        float blockWidth = numberSize * 5f;
        float centerX = safe.x + safe.width * 0.5f;
        float blockBottom = safeBottomY - bottomOffset * resScale;

        string speed = Mathf.RoundToInt(Mathf.Max(0f, _smoothedSpeedKmh)).ToString();
        GUI.color = textColor;
        GUI.Label(new Rect(centerX - blockWidth * 0.5f, blockBottom - unitHeight - numberHeight, blockWidth, numberHeight),
            speed, _speedNumberStyle);
        if (showUnit)
        {
            GUI.color = mutedColor;
            GUI.Label(new Rect(centerX - blockWidth * 0.5f, blockBottom - unitHeight, blockWidth, unitHeight),
                "KM/H", _speedUnitStyle);
        }

        // ── Craft state panel stays top-right (unchanged information) ──
        float width = 270f * uiScale;
        float height = 62f * uiScale;
        float x = Screen.width - margin.x - width;
        float y = margin.y;

        DrawPanel(new Rect(x, y, width, height));
        string state = GetStateLabel(telemetry, out Color stateColor);
        DrawRightLabel(x + 12f * uiScale, y + 10f * uiScale, width - 24f * uiScale, "STATE", state, stateColor);
        DrawRightLabel(x + 12f * uiScale, y + 32f * uiScale, width - 24f * uiScale, "GRIP", GetGripLabel(), GetGripColor());

        GUI.color = Color.white;
    }

    private void DrawSystemsPanel()
    {
        float width = 330f * uiScale;
        float height = 174f * uiScale;
        float x = margin.x;
        float y = Screen.height - margin.y - height;

        DrawPanel(new Rect(x, y, width, height));

        float rowY = y + 10f * uiScale;
        DrawHeader(ref rowY, x + 12f * uiScale, "CRAFT SYSTEMS");

        DrawStabilizerRow(ref rowY, x + 12f * uiScale);
        DrawOverchargeRow(ref rowY, x + 12f * uiScale);
        DrawVerticalThrusterRows(ref rowY, x + 12f * uiScale);
        DrawGripBreakerRow(ref rowY, x + 12f * uiScale);
    }

    private void DrawEnergyPanel()
    {
        EnergyCore energyCore = craftCore.energy;
        if (energyCore == null) return;

        EnergyState e = energyCore.CurrentEnergy;

        float width = 350f * uiScale;
        float height = 190f * uiScale;
        float x = Screen.width - margin.x - width;
        float y = Screen.height - margin.y - height;

        DrawPanel(new Rect(x, y, width, height));

        float rowY = y + 10f * uiScale;
        DrawHeader(ref rowY, x + 12f * uiScale, "ENERGY DISTRIBUTION");

        string bus = $"{e.totalGranted:0}/{e.totalBudget:0}";
        Color busColor = e.overload01 > 0.01f ? yellowColor : greenColor;
        DrawLabel(ref rowY, x + 12f * uiScale, "BUS", bus, busColor);

        DrawEnergyBar(ref rowY, x + 12f * uiScale, "BASE", e.baseHoverRequest, e.baseHoverGranted, Mathf.Max(e.totalBudget, e.totalGranted), e.baseHoverPower01, greenColor);
        DrawEnergyBar(ref rowY, x + 12f * uiScale, "DRIVE", e.driveRequest, e.driveGranted, e.totalBudget, e.drivePower01, cyanColor);
        DrawEnergyBar(ref rowY, x + 12f * uiScale, "VECTOR", e.vectoringRequest, e.vectoringGranted, e.totalBudget, e.vectoringPower01, cyanColor);
        DrawEnergyBar(ref rowY, x + 12f * uiScale, "ROOF", e.roofRequest, e.roofGranted, e.totalBudget, e.roofPower01, purpleColor);
        DrawEnergyBar(ref rowY, x + 12f * uiScale, "BOTTOM", e.bottomRequest, e.bottomGranted, e.totalBudget, e.bottomPower01, greenColor);
        DrawEnergyBar(ref rowY, x + 12f * uiScale, "CHARGE", e.overchargeRequest, e.overchargeGranted, e.totalBudget, e.overchargePower01, yellowColor);
        DrawEnergyBar(ref rowY, x + 12f * uiScale, "STABIL", e.stabilizerRequest, e.stabilizerGranted, e.totalBudget, e.stabilizerPower01, cyanColor);
    }

    private void DrawStabilizerRow(ref float y, float x)
    {
        HoverStabilizerArray hover = craftCore.hoverArray;
        if (hover == null)
        {
            DrawLabel(ref y, x, "STABILIZER", "MISSING", redColor);
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
            color = greenColor;
        }
        else
        {
            status = "ARMED";
            color = cyanColor;
        }

        DrawLabel(ref y, x, "STABILIZER", status, color);
    }

    private void DrawOverchargeRow(ref float y, float x)
    {
        OverchargeCore overcharge = craftCore.overcharge;
        if (overcharge == null)
        {
            DrawLabel(ref y, x, "OVERCHARGE", "MISSING", redColor);
            return;
        }

        string label;
        Color color;

        if (overcharge.IsCharging)
        {
            label = overcharge.ChargingTarget.ToString().Replace("Thrusters", "").ToUpperInvariant();
            color = yellowColor;
            DrawLabel(ref y, x, "OVERCHARGE", label + " " + (overcharge.Charge01 * 100f).ToString("0") + "%", color);
            DrawBarRow(ref y, x, "CHARGE", overcharge.Charge01, yellowColor);
        }
        else if (overcharge.IsBursting)
        {
            label = overcharge.ActiveBurstTarget.ToString().Replace("Thrusters", "").ToUpperInvariant();
            color = redColor;
            DrawLabel(ref y, x, "OVERCHARGE", "BURST " + label, color);
            DrawBarRow(ref y, x, "CHARGE", 1f, redColor);
        }
        else
        {
            DrawLabel(ref y, x, "OVERCHARGE", "READY", mutedColor);
            DrawBarRow(ref y, x, "CHARGE", 0f, mutedColor);
        }
    }

    private void DrawVerticalThrusterRows(ref float y, float x)
    {
        PilotCommand command = craftCore.pilotInput != null ? craftCore.pilotInput.CurrentCommand : default;
        CraftIntent intent = craftCore.vectoring != null ? craftCore.vectoring.CurrentIntent : default;
        EnergyState energy = craftCore.energy != null ? craftCore.energy.CurrentEnergy : EnergyState.Full;

        bool locked = intent.verticalThrustersLockedByStabilizer && !intent.recoveryOverrideActive;

        float roofValue = locked ? 0f : Mathf.Clamp01(intent.manualRoofThrusterRequest);
        float bottomValue = locked ? 0f : Mathf.Clamp01(intent.manualBottomThrusterRequest);

        Color roofColor = command.roofThrustersHeld ? purpleColor : mutedColor;
        Color bottomColor = command.bottomThrustersHeld ? greenColor : mutedColor;

        if (locked)
        {
            DrawLabel(ref y, x, "Q ROOF", "LOCKED BY STABILIZER", mutedColor);
        }
        else
        {
            DrawThrusterRow(ref y, x, "Q ROOF", roofValue, energy.roofPower01, roofColor);
        }

        DrawThrusterRow(ref y, x, "E BOTTOM", bottomValue, energy.bottomPower01, bottomColor);
    }

    private void DrawGripBreakerRow(ref float y, float x)
    {
        TractionState traction = craftCore.traction != null ? craftCore.traction.CurrentTraction : TractionState.Normal;
        DrawBarRow(ref y, x, "GRIP BREAK", traction.gripBreakerAmount, redColor);
    }

    private void DrawThrusterRow(ref float y, float x, string label, float request01, float power01, Color color)
    {
        float value = Mathf.Clamp01(request01 * power01);
        DrawBarRow(ref y, x, label, value, color);
    }

    private CraftTelemetry GetTelemetry()
    {
        return craftCore.telemetry != null ? craftCore.telemetry.CurrentTelemetry : default;
    }

    private string GetGripLabel()
    {
        if (craftCore.traction == null) return "N/A";
        float g = craftCore.traction.CurrentTraction.gripBreakerAmount;
        if (g > 0.75f) return "BROKEN";
        if (g > 0.15f) return "SLIDING";
        return "LOCKED";
    }

    private Color GetGripColor()
    {
        if (craftCore.traction == null) return mutedColor;
        float g = craftCore.traction.CurrentTraction.gripBreakerAmount;
        return g > 0.75f ? redColor : g > 0.15f ? yellowColor : greenColor;
    }

    private string GetStateLabel(CraftTelemetry telemetry, out Color color)
    {
        if (telemetry.isInverted)
        {
            color = yellowColor;
            return "INVERTED";
        }

        if (telemetry.isGrounded)
        {
            color = greenColor;
            return "GROUNDED";
        }

        if (telemetry.hasSurfaceContact)
        {
            color = cyanColor;
            return "SURFACE";
        }

        color = cyanColor;
        return "AIRBORNE";
    }

    private void DrawPanel(Rect rect)
    {
        GUI.color = panelColor;
        GUI.Box(rect, GUIContent.none);
        GUI.color = Color.white;
    }

    private void DrawHeader(ref float y, float x, string text)
    {
        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 320f * uiScale, rowHeight * uiScale), text, _headerStyle);
        GUI.color = Color.white;
        y += (rowHeight + 3f) * uiScale;
    }

    private void DrawLabel(ref float y, float x, string key, string value, Color valueColor)
    {
        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 112f * uiScale, rowHeight * uiScale), key, _labelStyle);
        GUI.color = valueColor;
        GUI.Label(new Rect(x + 118f * uiScale, y, 210f * uiScale, rowHeight * uiScale), value, _labelStyle);
        GUI.color = Color.white;
        y += rowHeight * uiScale;
    }

    private void DrawRightLabel(float x, float y, float width, string key, string value, Color valueColor)
    {
        GUI.color = mutedColor;
        GUI.Label(new Rect(x, y, width * 0.4f, rowHeight * uiScale), key, _smallStyle);
        GUI.color = valueColor;
        GUIStyle rightStyle = new GUIStyle(_smallStyle) { alignment = TextAnchor.UpperRight };
        GUI.Label(new Rect(x, y, width, rowHeight * uiScale), value, rightStyle);
        GUI.color = Color.white;
    }

    private void DrawBarRow(ref float y, float x, string label, float value01, Color fill)
    {
        value01 = Mathf.Clamp01(value01);
        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 90f * uiScale, rowHeight * uiScale), label, _smallStyle);
        DrawBar(new Rect(x + 96f * uiScale, y + 4f * uiScale, barWidth * uiScale, barHeight * uiScale), value01, fill, new Color(0.12f, 0.16f, 0.19f, 0.9f));
        GUI.color = textColor;
        GUI.Label(new Rect(x + (102f + barWidth) * uiScale, y, 48f * uiScale, rowHeight * uiScale), (value01 * 100f).ToString("0") + "%", _smallStyle);
        GUI.color = Color.white;
        y += rowHeight * uiScale;
    }

    private void DrawEnergyBar(ref float y, float x, string label, float request, float granted, float budget, float power01, Color fill)
    {
        float request01 = budget > 0.001f ? Mathf.Clamp01(request / budget) : 0f;
        float granted01 = budget > 0.001f ? Mathf.Clamp01(granted / budget) : 0f;

        GUI.color = textColor;
        GUI.Label(new Rect(x, y, 76f * uiScale, rowHeight * uiScale), label, _smallStyle);

        Rect rect = new Rect(x + 82f * uiScale, y + 4f * uiScale, barWidth * uiScale, barHeight * uiScale);
        DrawBar(rect, request01, new Color(0.12f, 0.16f, 0.19f, 0.9f), mutedColor);
        DrawBar(rect, granted01, power01 < 0.75f ? yellowColor : fill, Color.clear);

        GUI.color = textColor;
        GUI.Label(new Rect(x + (88f + barWidth) * uiScale, y, 72f * uiScale, rowHeight * uiScale), $"{granted:0}/{request:0}", _smallStyle);
        GUI.color = Color.white;

        y += rowHeight * uiScale;
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

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = Color.white;
    }
}
