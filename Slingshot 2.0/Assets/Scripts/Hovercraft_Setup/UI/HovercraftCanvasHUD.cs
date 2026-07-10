using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// A modern screen-space Canvas-based HUD for the V2 hovercraft.
/// Programmatically generates a clean, premium cockpit interface showing speed, flight state, and a live thruster schematic.
/// </summary>
public class HovercraftCanvasHUD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Target hovercraft core. Auto-detected if null.")]
    public CraftCore craftCore;

    [Tooltip("Toggle HUD visibility.")]
    public bool showHUD = true;

    // UI Object references
    private GameObject _canvasRoot;
    private Text _speedText;
    private Image _speedBarFill;
    private Text _stateText;
    private Text _groundedFactorText;
    private Image _groundedBarFill;
    private Text _gripLateralText;
    private Text _gripLongitudinalText;
    private bool _canvasInitialized = false;

    // Thruster visual mapping
    private struct ThrusterIndicator
    {
        public ThrusterNode thruster;
        public Image glowImage;
        public RectTransform rectTransform;
        public Color activeColor;
        public string label;
        public Text statusText;
        public Image rowDotImage;
    }
    private List<ThrusterIndicator> _indicators = new List<ThrusterIndicator>();

    // Color Palette
    private readonly Color _bgColor = new Color(0.04f, 0.06f, 0.1f, 0.8f);      // Deep space blue glass
    private readonly Color _borderColor = new Color(0.15f, 0.25f, 0.4f, 0.6f);  // Tech blue border
    private readonly Color _textPrimary = new Color(0.9f, 0.95f, 1.0f, 1.0f);   // White-blue
    private readonly Color _textSecondary = new Color(0.5f, 0.65f, 0.8f, 1.0f); // Muted blue
    private readonly Color _accentCyan = new Color(0f, 0.8f, 1.0f, 1.0f);       // Cyan glow
    
    // Thruster Colors
    private readonly Color _colorHover = new Color(0.1f, 0.6f, 1.0f, 1.0f);     // Blue
    private readonly Color _colorMain = new Color(1.0f, 0.5f, 0.1f, 1.0f);      // Orange
    private readonly Color _colorBrake = new Color(1.0f, 0.2f, 0.2f, 1.0f);     // Red
    private readonly Color _colorStrafe = new Color(1.0f, 0.8f, 0.0f, 1.0f);    // Yellow
    private readonly Color _colorIdle = new Color(0.15f, 0.2f, 0.25f, 0.4f);

    void Start()
    {
        TryFindController();
    }

    void Update()
    {
        // Toggle visibility
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f2Key.wasPressedThisFrame)
        {
            showHUD = !showHUD;
        }

        if (craftCore == null)
        {
            TryFindController();
        }

        if (_canvasRoot != null)
        {
            _canvasRoot.SetActive(showHUD && craftCore != null);
        }

        if (showHUD && craftCore != null)
        {
            UpdateTelemetry();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  UI GENERATION
    // ══════════════════════════════════════════════════════════════

    private void CreateHUDCanvas()
    {
        // 1. Create Canvas Root
        _canvasRoot = new GameObject("HovercraftCanvasHUD");
        Canvas canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // 2. Main HUD Container (anchored to fill canvas)
        GameObject container = new GameObject("Container", typeof(RectTransform));
        container.transform.SetParent(_canvasRoot.transform, false);
        RectTransform containerRect = container.GetComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.sizeDelta = Vector2.zero;

        // 3. Create Panels
        CreateVelocityPanel(container.transform);
        CreateTelemetryPanel(container.transform);
        CreateSchematicPanel(container.transform);
        CreateHelpPanel(container.transform);
    }

    private void CreateVelocityPanel(Transform parent)
    {
        // Bottom-Center speedometer
        RectTransform panel = CreatePanel("VelocityPanel", parent, _bgColor, 
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), 
            new Vector2(320, 130), new Vector2(0, 30));

        // Outline border
        CreateOutline(panel.gameObject, _borderColor);

        // Speed header
        CreateText("Header", panel, "VELOCITY", 11, _textSecondary, TextAnchor.MiddleCenter,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, 20), new Vector2(0, -12));

        // Speed Value
        _speedText = CreateText("SpeedValue", panel, "000", 46, _textPrimary, TextAnchor.MiddleRight,
            new Vector2(0f, 0.5f), new Vector2(0.6f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(120, 60), new Vector2(-10, 0));
        
        // Speed Unit
        CreateText("SpeedUnit", panel, "KM/H", 16, _accentCyan, TextAnchor.MiddleLeft,
            new Vector2(0.6f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(80, 30), new Vector2(15, -6));

        // Speed bar background
        Image barBg = CreateImage("SpeedBarBg", panel, new Color(0.1f, 0.15f, 0.2f, 0.6f),
            new Vector2(0.05f, 0f), new Vector2(0.95f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 8), new Vector2(0, 15));
        
        // Speed bar fill
        _speedBarFill = CreateImage("SpeedBarFill", barBg.transform, _accentCyan,
            Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
            Vector2.zero, Vector2.zero);
    }

    private void CreateTelemetryPanel(Transform parent)
    {
        // Bottom-Left flight status
        RectTransform panel = CreatePanel("TelemetryPanel", parent, _bgColor,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(300, 240), new Vector2(30, 30));

        CreateOutline(panel.gameObject, _borderColor);

        // Title
        CreateText("Title", panel, "FLIGHT SYSTEM DATA", 12, _textSecondary, TextAnchor.MiddleLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-20, 25), new Vector2(10, -10));

        float startY = -45f;
        float rowHeight = 30f;

        // Rows
        _stateText = CreateRow(panel, "FLIGHT STATE", "Grounded", ref startY, rowHeight);
        
        // Grounded factor row with custom bar
        GameObject factorRow = new GameObject("GroundedFactorRow", typeof(RectTransform));
        factorRow.transform.SetParent(panel, false);
        RectTransform rowRect = factorRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.sizeDelta = new Vector2(-20, 26);
        rowRect.anchoredPosition = new Vector2(0, startY);
        startY -= rowHeight;

        CreateText("Label", factorRow.transform, "GROUND INFL.", 12, _textSecondary, TextAnchor.MiddleLeft,
            new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(120, 26), Vector2.zero);

        Image barBg = CreateImage("BarBg", factorRow.transform, new Color(0.1f, 0.15f, 0.2f, 0.6f),
            new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 10), new Vector2(0, 0));
        
        _groundedBarFill = CreateImage("BarFill", barBg.transform, _colorHover,
            Vector2.zero, new Vector2(0f, 1f), Vector2.zero,
            Vector2.zero, Vector2.zero);

        _gripLateralText = CreateRow(panel, "LATERAL GRIP", "5.00", ref startY, rowHeight);
        _gripLongitudinalText = CreateRow(panel, "LONG. GRIP", "2.00", ref startY, rowHeight);
        
        // Add current lean indicators
        _groundedFactorText = CreateRow(panel, "PITCH / ROLL", "0.0° / 0.0°", ref startY, rowHeight);
    }

    private void CreateSchematicPanel(Transform parent)
    {
        // Bottom-Right thruster schematic — now wider (650px) to show graphical + text status side-by-side
        RectTransform panel = CreatePanel("SchematicPanel", parent, _bgColor,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(650, 280), new Vector2(-30, 30));

        CreateOutline(panel.gameObject, _borderColor);

        // Title
        CreateText("Title", panel, "THRUSTER SCHEMATIC & TELEMETRY", 12, _textSecondary, TextAnchor.MiddleLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-20, 25), new Vector2(10, -10));

        // 1. Draw central ship silhouette wireframe (shifted left by -150px)
        RectTransform hull = CreatePanel("Hull", panel, new Color(0.12f, 0.18f, 0.25f, 0.3f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(80, 120), new Vector2(-150, -10));
        CreateOutline(hull.gameObject, new Color(0.25f, 0.35f, 0.5f, 0.5f));

        RectTransform nose = CreatePanel("Nose", panel, new Color(0.12f, 0.18f, 0.25f, 0.3f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(50, 40), new Vector2(-150, 70));
        CreateOutline(nose.gameObject, new Color(0.25f, 0.35f, 0.5f, 0.5f));

        // 2. Position the thruster indicator circles (shifted left by -150px)
        // Hover thrusters (circles on the corners of the hull)
        AddIndicator(GetThrusterReference("Hover_FL"), "H_FL", "Hover FL", panel, new Vector2(-185, 45), _colorHover);
        AddIndicator(GetThrusterReference("Hover_FR"), "H_FR", "Hover FR", panel, new Vector2(-115, 45), _colorHover);
        AddIndicator(GetThrusterReference("Hover_RL"), "H_RL", "Hover RL", panel, new Vector2(-185, -65), _colorHover);
        AddIndicator(GetThrusterReference("Hover_RR"), "H_RR", "Hover RR", panel, new Vector2(-115, -65), _colorHover);

        // Main rear thruster
        AddIndicator(GetThrusterReference("Main_Rear"), "MAIN", "Main Engine", panel, new Vector2(-150, -90), _colorMain);

        // Front brake thruster
        AddIndicator(GetThrusterReference("Brake_Front"), "BRK", "Brake Engine", panel, new Vector2(-150, 95), _colorBrake);

        // Corner strafe thrusters
        AddIndicator(GetThrusterReference("Strafe_Front_Left"), "S_FL", "Strafe FL", panel, new Vector2(-215, 50), _colorStrafe);
        AddIndicator(GetThrusterReference("Strafe_Front_Right"), "S_FR", "Strafe FR", panel, new Vector2(-85, 50), _colorStrafe);
        AddIndicator(GetThrusterReference("Strafe_Back_Left"), "S_BL", "Strafe RL", panel, new Vector2(-215, -60), _colorStrafe);
        AddIndicator(GetThrusterReference("Strafe_Back_Right"), "S_BR", "Strafe RR", panel, new Vector2(-85, -60), _colorStrafe);
    }

    private void CreateHelpPanel(Transform parent)
    {
        // Simple overlay instructions at Top-Left
        RectTransform panel = CreatePanel("HelpPanel", parent, new Color(0.04f, 0.06f, 0.1f, 0.6f),
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(360, 65), new Vector2(30, -30));
        
        CreateOutline(panel.gameObject, new Color(0.15f, 0.25f, 0.4f, 0.4f));

        CreateText("HelpText", panel, 
            "Controls: W/S = Forward/Back, A/D = Edge Shift, Mouse = Steering/Pitch\n" +
            "Space = Jump | F1 = Toggle Legacy HUD | F2 = Toggle Canvas HUD", 
            11, _textSecondary, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(-20, -10), new Vector2(10, 0));
    }

    private void AddIndicator(ThrusterNode thruster, string label, string displayName, Transform parent, Vector2 pos, Color activeColor)
    {
        if (thruster == null) return;

        // Background circle
        GameObject indicator = new GameObject("Ind_" + label, typeof(RectTransform));
        indicator.transform.SetParent(parent, false);
        RectTransform rect = indicator.GetComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(18, 18);

        Image bgImg = indicator.AddComponent<Image>();
        bgImg.color = _colorIdle;

        // Add a clean outline border to the indicator circle
        CreateOutline(indicator, new Color(0.4f, 0.5f, 0.6f, 0.4f));

        // Glow circle (inside)
        GameObject glow = new GameObject("Glow", typeof(RectTransform));
        glow.transform.SetParent(indicator.transform, false);
        RectTransform glowRect = glow.GetComponent<RectTransform>();
        glowRect.anchorMin = Vector2.zero;
        glowRect.anchorMax = Vector2.one;
        glowRect.sizeDelta = Vector2.zero;

        Image glowImg = glow.AddComponent<Image>();
        glowImg.color = activeColor;
        glowRect.localScale = Vector3.one * 0.1f; // start tiny

        // Create the text row on the right half of the panel
        float rowY = -42f - (_indicators.Count * 21f); // space rows by 21px
        
        GameObject row = new GameObject("Row_" + label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.sizeDelta = new Vector2(-20, 20);
        rowRect.anchoredPosition = new Vector2(20, rowY);

        // Indicator dot for row
        GameObject rowDot = new GameObject("Dot", typeof(RectTransform));
        rowDot.transform.SetParent(row.transform, false);
        RectTransform dotRect = rowDot.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0f, 0.5f);
        dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0f, 0.5f);
        dotRect.sizeDelta = new Vector2(8, 8);
        dotRect.anchoredPosition = new Vector2(0, 0);
        Image dotImg = rowDot.AddComponent<Image>();
        dotImg.color = _colorIdle;

        // Add outline to dot
        CreateOutline(rowDot, new Color(0.4f, 0.5f, 0.6f, 0.4f));

        // Label on left of row
        CreateText("Label", row.transform, displayName, 11, _textSecondary, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(0.45f, 1f), new Vector2(0f, 0.5f),
            new Vector2(0, 0), new Vector2(15, 0));

        // Value text on right of row
        Text statusTxt = CreateText("Value", row.transform, "T: 0.00 | F: 0 N", 11, _textSecondary, TextAnchor.MiddleRight,
            new Vector2(0.45f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            new Vector2(0, 0), Vector2.zero);

        // Add to update list
        _indicators.Add(new ThrusterIndicator
        {
            thruster = thruster,
            glowImage = glowImg,
            rectTransform = glowRect,
            activeColor = activeColor,
            label = label,
            statusText = statusTxt,
            rowDotImage = dotImg
        });
    }

    private Text CreateRow(Transform parent, string labelText, string valueText, ref float yPos, float height)
    {
        GameObject row = new GameObject(labelText + "_Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.sizeDelta = new Vector2(-20, height - 4);
        rowRect.anchoredPosition = new Vector2(0, yPos);
        yPos -= height;

        CreateText("Label", row.transform, labelText, 12, _textSecondary, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
            new Vector2(120, 0), Vector2.zero);

        return CreateText("Value", row.transform, valueText, 12, _textPrimary, TextAnchor.MiddleRight,
            new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            new Vector2(100, 0), Vector2.zero);
    }

    // ══════════════════════════════════════════════════════════════
    //  UI UPDATE
    // ══════════════════════════════════════════════════════════════

    private void UpdateTelemetry()
    {
        // Get flight telemetries
        CraftTelemetry telem = craftCore.telemetry.CurrentTelemetry;
        
        // 1. Update Speed
        float speedKmh = telem.speed * 3.6f;
        _speedText.text = speedKmh.ToString("F0");
        
        // Speed bar fill (0 to 120 km/h is normal range)
        float speedRatio = Mathf.Clamp01(speedKmh / 120f);
        _speedBarFill.rectTransform.anchorMax = new Vector2(speedRatio, 1f);

        // 2. Update Flight State
        float groundedFactor = telem.groundedFactor;
        _groundedBarFill.rectTransform.anchorMax = new Vector2(groundedFactor, 1f);

        bool isDrifting = false;
        float activeLat = 0f;
        float activeLong = 0f;

        if (craftCore.traction != null)
        {
            isDrifting = craftCore.traction.IsGripBroken || craftCore.traction.CurrentTraction.lateralGrip < (craftCore.traction.lateralGrip - 0.05f);
            activeLat = craftCore.traction.CurrentTraction.lateralGrip;
            activeLong = craftCore.traction.CurrentTraction.longitudinalGrip;
        }

        if (groundedFactor > 0.85f)
        {
            if (isDrifting)
            {
                _stateText.text = "DRIFTING";
                _stateText.color = new Color(1f, 0.7f, 0f); // orange-yellow
            }
            else
            {
                _stateText.text = "GROUNDED";
                _stateText.color = Color.green;
            }
        }
        else if (groundedFactor < 0.15f)
        {
            _stateText.text = "AIRBORNE";
            _stateText.color = new Color(1f, 0.5f, 0.1f);
        }
        else
        {
            _stateText.text = "TRANSITION";
            _stateText.color = new Color(1f, 0.8f, 0.2f);
        }

        // 3. Update Grip Parameter displays
        _gripLateralText.text = activeLat.ToString("F2");
        _gripLongitudinalText.text = activeLong.ToString("F2");

        Color gripColor = isDrifting ? new Color(1f, 0.7f, 0f) : _textPrimary;
        _gripLateralText.color = gripColor;
        _gripLongitudinalText.color = gripColor;

        // 4. Pitch / Roll attitude readings
        _groundedFactorText.text = string.Format("{0:F1}° / {1:F1}°", telem.pitchAngle, telem.rollAngle);

        // 5. Update live thruster schematic indicators & text readings
        for (int i = 0; i < _indicators.Count; i++)
        {
            var ind = _indicators[i];
            if (ind.thruster == null) continue;

            float throttle = ind.thruster.Throttle; // 0 to 1+
            float activeFactor = Mathf.Clamp01(throttle);
            
            if (throttle > 0.01f)
            {
                // Active: pop scale (0.7 to 1.4) and glow activeColor
                ind.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.4f, activeFactor);
                ind.glowImage.color = ind.activeColor;

                // Colorize the row dot and status text
                if (ind.rowDotImage != null)
                {
                    ind.rowDotImage.color = ind.activeColor;
                }
                
                ind.statusText.color = Color.Lerp(_textPrimary, ind.activeColor, 0.4f);
                ind.statusText.text = string.Format("T: {0:F2} | F: {1:F0} N", throttle, ind.thruster.LastAppliedForce.magnitude);
            }
            else
            {
                // Idle: tiny, dark idle color
                ind.rectTransform.localScale = Vector3.one * 0.2f;
                ind.glowImage.color = _colorIdle;

                // Mute the row dot and status text
                if (ind.rowDotImage != null)
                {
                    ind.rowDotImage.color = _colorIdle;
                }
                
                ind.statusText.color = _textSecondary;
                ind.statusText.text = "T: 0.00 | F: 0 N";
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  UI COMPONENT HELPERS
    // ══════════════════════════════════════════════════════════════

    private RectTransform CreatePanel(string name, Transform parent, Color bgColor,
                                      Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                      Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        Image img = go.AddComponent<Image>();
        img.color = bgColor;

        return rect;
    }

    private Image CreateImage(string name, Transform parent, Color color,
                             Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                             Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        Image img = go.AddComponent<Image>();
        img.color = color;

        return img;
    }

    private Text CreateText(string name, Transform parent, string text, int fontSize, Color color,
                            TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                            Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        Text txt = go.AddComponent<Text>();
        txt.text = text;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.color = color;
        txt.alignment = alignment;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        return txt;
    }

    private void CreateOutline(GameObject target, Color color)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1, 1);
    }

    private void TryFindController()
    {
        // Search for HovercraftRoot if we don't have a craftCore reference yet
        if (craftCore == null)
        {
            GameObject playerObj = GameObject.Find("HovercraftRoot");
            if (playerObj != null)
            {
                craftCore = playerObj.GetComponent<CraftCore>();
            }
        }

        // Fallback: search by type
        if (craftCore == null)
        {
            craftCore = Object.FindAnyObjectByType<CraftCore>();
        }

        // Initialize Canvas HUD once target core is resolved
        if (craftCore != null && !_canvasInitialized)
        {
            CreateHUDCanvas();
            _canvasInitialized = true;
        }
    }

    private ThrusterNode GetThrusterReference(string label)
    {
        if (craftCore == null) return null;
        var bus = craftCore.GetComponent<ThrusterBus>();
        if (bus == null) return null;
        return bus.FindByLabel(label)?.node;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnSceneLoaded()
    {
        // Auto-instantiate the HUD if missing in the active scene
        if (Object.FindAnyObjectByType<HovercraftCanvasHUD>() != null) return;

        CraftCore core = null;
        GameObject playerObj = GameObject.Find("HovercraftRoot");
        if (playerObj != null)
        {
            core = playerObj.GetComponent<CraftCore>();
        }

        if (core == null)
        {
            core = Object.FindAnyObjectByType<CraftCore>();
        }

        if (core != null)
        {
            GameObject hudManager = new GameObject("HovercraftHUDManager_Auto");
            var canvasHud = hudManager.AddComponent<HovercraftCanvasHUD>();
            canvasHud.craftCore = core;
            canvasHud.showHUD = true;
            Debug.Log("<b>[Hovercraft HUD]</b> Automatically spawned HUD Canvas for " + core.gameObject.name);
        }
    }
}
