using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug HUD overlay using OnGUI for zero-overhead telemetry during prototyping.
/// Shows real-time state from CraftCore, TelemetryMainframe, and ThrusterBus.
/// Includes a G-force meter with visual lean direction indicator and a full
/// thruster monitor panel showing per-thruster throttle and applied force.
/// </summary>
public class HovercraftDebugHUD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found in the scene if not assigned.")]
    public CraftCore craftCore;

    private ThrusterBus _thrusterBus;

    [Header("Display")]
    [Tooltip("Toggle HUD visibility at runtime.")]
    public bool showHUD = true;

    [Header("G-Force Meter")]
    [Tooltip("Size of the G-force gauge in pixels.")]
    public float gForceGaugeSize = 160f;

    [Tooltip("Maximum G displayed at the edge of the gauge.")]
    public float gForceMaxDisplay = 3f;

    [Tooltip("Smoothing for G-force reading (lower = smoother).")]
    [Range(0.01f, 1f)]
    public float gForceSmoothing = 0.15f;

    // ── Styles ────────────────────────────────────────────────────
    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _valueStyle;
    private Texture2D _bgTexture;
    private bool _stylesInitialized;

    // ── G-Force Meter Textures ────────────────────────────────────
    private Texture2D _gDotTexture;
    private Texture2D _gRingPixel;
    private Texture2D _gCrosshairPixel;
    private Texture2D _gTrailTexture;

    // ── G-Force State ─────────────────────────────────────────────
    private Vector3 _prevVelocity;
    private Vector2 _smoothedGForce;
    private Vector2[] _gForceTrail;
    private int _trailIndex;
    private const int TRAIL_LENGTH = 20;

    // ───────────────────────────────────────────────────────────────
    void Start()
    {
        if (craftCore == null)
            craftCore = FindAnyObjectByType<CraftCore>();

        if (craftCore != null)
            _thrusterBus = craftCore.GetComponent<ThrusterBus>();

        _gForceTrail = new Vector2[TRAIL_LENGTH];
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            showHUD = !showHUD;
    }

    void FixedUpdate()
    {
        if (craftCore == null) return;

        Rigidbody rb = craftCore.GetComponent<Rigidbody>();
        Transform t  = craftCore.transform;
        if (rb == null) return;

        // Compute acceleration in world space
        Vector3 currentVel = rb.linearVelocity;
        Vector3 accel = (currentVel - _prevVelocity) / Time.fixedDeltaTime;
        _prevVelocity = currentVel;

        // Project into craft's local frame
        float lateralG     = Vector3.Dot(accel, t.right)   / 9.81f;
        float longitudinalG = Vector3.Dot(accel, t.forward) / 9.81f;

        // Smooth
        Vector2 rawG = new Vector2(lateralG, longitudinalG);
        _smoothedGForce = Vector2.Lerp(_smoothedGForce, rawG, gForceSmoothing);

        // Trail history
        _gForceTrail[_trailIndex] = _smoothedGForce;
        _trailIndex = (_trailIndex + 1) % TRAIL_LENGTH;
    }

    // ───────────────────────────────────────────────────────────────
    void InitStyles()
    {
        if (_stylesInitialized) return;

        _bgTexture = new Texture2D(1, 1);
        _bgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
        _bgTexture.Apply();

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.normal.background = _bgTexture;

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize = 13;
        _labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        _headerStyle = new GUIStyle(GUI.skin.label);
        _headerStyle.fontSize = 14;
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.normal.textColor = new Color(0.4f, 0.9f, 1f);

        _valueStyle = new GUIStyle(GUI.skin.label);
        _valueStyle.fontSize = 13;
        _valueStyle.alignment = TextAnchor.MiddleRight;
        _valueStyle.normal.textColor = Color.white;

        // G-force dot (filled circle)
        _gDotTexture = CreateCircleTexture(16, Color.white);

        // Trail dot (smaller)
        _gTrailTexture = CreateCircleTexture(8, new Color(1f, 1f, 1f, 0.5f));

        // Single-pixel textures for drawing lines and rings
        _gRingPixel = new Texture2D(1, 1);
        _gRingPixel.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.2f));
        _gRingPixel.Apply();

        _gCrosshairPixel = new Texture2D(1, 1);
        _gCrosshairPixel.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.15f));
        _gCrosshairPixel.Apply();

        _stylesInitialized = true;
    }

    // ───────────────────────────────────────────────────────────────
    Texture2D CreateCircleTexture(int size, Color color)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float rad = size * 0.5f;
        Color clear = new Color(0, 0, 0, 0);

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float dist = Vector2.Distance(new Vector2(px, py), new Vector2(rad, rad));
                if (dist < rad - 1f)
                    tex.SetPixel(px, py, color);
                else if (dist < rad)
                    tex.SetPixel(px, py, new Color(color.r, color.g, color.b, color.a * (rad - dist)));
                else
                    tex.SetPixel(px, py, clear);
            }
        }
        tex.Apply();
        return tex;
    }

    // ───────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!showHUD || craftCore == null) return;

        InitStyles();

        float panelWidth  = 280f;
        float panelX      = 10f;
        float panelY      = 10f;
        float lineHeight  = 20f;
        float padding     = 8f;
        float y           = panelY + padding;
        float panelHeight = 380f;

        // Background
        GUI.Box(new Rect(panelX, panelY, panelWidth, panelHeight), "", _boxStyle);

        float labelX = panelX + padding;
        float valueX = panelX + panelWidth * 0.55f;
        float valueW = panelWidth * 0.4f;

        // ── Title ─────────────────────────────────────────────────
        GUI.Label(new Rect(labelX, y, panelWidth - padding * 2, lineHeight),
            "HOVERCRAFT TELEMETRY (V2)", _headerStyle);
        y += lineHeight + 4f;

        // ── Read state from TelemetryMainframe ─────────────────────
        CraftTelemetry telem = craftCore.telemetry.CurrentTelemetry;
        float groundedFactor = telem.groundedFactor;
        float speed = telem.speed;
        float targetPitch = craftCore.hoverArray != null ? craftCore.hoverArray.LeanTargetPitch : 0f;
        float targetRoll = craftCore.hoverArray != null ? craftCore.hoverArray.LeanTargetRoll : 0f;
        float jumpCooldownTimer = craftCore.hoverArray != null ? craftCore.hoverArray.JumpCooldownTimer : 0f;
        Rigidbody rb = craftCore.GetComponent<Rigidbody>();
        float pitch = telem.pitchAngle;
        float roll = telem.rollAngle;
        float angularVel = rb != null ? rb.angularVelocity.magnitude : 0f;

        bool isDrifting = false;
        float activeLatGrip = 0f;
        float activeLongGrip = 0f;
        if (craftCore.traction != null)
        {
            isDrifting = craftCore.traction.IsGripBroken || craftCore.traction.CurrentTraction.lateralGrip < (craftCore.traction.lateralGrip - 0.05f);
            activeLatGrip = craftCore.traction.CurrentTraction.lateralGrip;
            activeLongGrip = craftCore.traction.CurrentTraction.longitudinalGrip;
        }

        // ── Mode ──────────────────────────────────────────────────
        string mode;
        Color modeColor;
        if (groundedFactor > 0.85f)
        {
            if (isDrifting)
            {
                mode = "DRIFTING";
                modeColor = new Color(1f, 0.7f, 0f);
            }
            else
            {
                mode = "GROUNDED";
                modeColor = new Color(0.3f, 1f, 0.3f);
            }
        }
        else if (groundedFactor < 0.15f)
        {
            mode = "AIRBORNE";
            modeColor = new Color(1f, 0.5f, 0.2f);
        }
        else
        {
            mode = "TRANSITION";
            modeColor = new Color(1f, 1f, 0.3f);
        }

        GUIStyle modeStyle = new GUIStyle(_valueStyle);
        modeStyle.normal.textColor = modeColor;
        modeStyle.fontStyle = FontStyle.Bold;

        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "State", mode, _labelStyle, modeStyle);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Grounded Factor",
            groundedFactor.ToString("F2"), _labelStyle, _valueStyle);

        // Live grip telemetry
        GUIStyle gripStyle = new GUIStyle(_valueStyle);
        gripStyle.normal.textColor = isDrifting ? new Color(1f, 0.7f, 0f) : new Color(0.85f, 0.85f, 0.85f);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Lat Grip",
            activeLatGrip.ToString("F2"), _labelStyle, gripStyle);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Long Grip",
            activeLongGrip.ToString("F2"), _labelStyle, gripStyle);

        y += 6f;

        // ── Speed ─────────────────────────────────────────────────
        GUI.Label(new Rect(labelX, y, panelWidth - padding * 2, lineHeight),
            "SPEED", _headerStyle);
        y += lineHeight;

        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "m/s",
            speed.ToString("F1"), _labelStyle, _valueStyle);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "km/h",
            (speed * 3.6f).ToString("F0"), _labelStyle, _valueStyle);

        y += 6f;

        // ── Attitude ──────────────────────────────────────────────
        GUI.Label(new Rect(labelX, y, panelWidth - padding * 2, lineHeight),
            "ATTITUDE", _headerStyle);
        y += lineHeight;

        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Pitch (current)",
            pitch.ToString("F1") + "\u00B0", _labelStyle, _valueStyle);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Roll (current)",
            roll.ToString("F1") + "\u00B0", _labelStyle, _valueStyle);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Pitch (target)",
            targetPitch.ToString("F1") + "\u00B0", _labelStyle, _valueStyle);
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Roll (target)",
            targetRoll.ToString("F1") + "\u00B0", _labelStyle, _valueStyle);

        y += 6f;

        // ── Hover Thrusters ───────────────────────────────────────
        if (_thrusterBus != null)
        {
            GUI.Label(new Rect(labelX, y, panelWidth - padding * 2, lineHeight),
                "HOVER THRUSTERS", _headerStyle);
            y += lineHeight;

            var hoverList = _thrusterBus.HoverNodes;
            for (int i = 0; i < hoverList.Count; i++)
            {
                var entry = hoverList[i];
                if (entry.node == null) continue;
                float dist = entry.node.GroundDistance;
                string val = entry.node.IsGrounded ? dist.ToString("F2") + "m" : "---";
                GUIStyle vs = new GUIStyle(_valueStyle);
                vs.normal.textColor = entry.node.IsGrounded ? Color.green : Color.red;
                DrawRow(ref y, labelX, valueX, valueW, lineHeight, entry.label, val, _labelStyle, vs);
            }
        }

        y += 6f;

        // ── Jump ──────────────────────────────────────────────────
        string jumpStr = jumpCooldownTimer > 0 ? jumpCooldownTimer.ToString("F1") + "s" : "READY";
        GUIStyle jumpStyle = new GUIStyle(_valueStyle);
        jumpStyle.normal.textColor = jumpCooldownTimer > 0 ? new Color(1f, 0.5f, 0.5f) : Color.green;
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Jump", jumpStr, _labelStyle, jumpStyle);

        // ── Angular Velocity ──────────────────────────────────────
        DrawRow(ref y, labelX, valueX, valueW, lineHeight, "Ang. Vel",
            angularVel.ToString("F2") + " rad/s", _labelStyle, _valueStyle);

        // ── Thruster Monitor Panel ────────────────────────────────
        if (_thrusterBus != null)
        {
            float thrusterPanelX     = panelX + panelWidth + 10f;
            float thrusterPanelWidth  = 320f;
            float thrusterPanelHeight = panelHeight;

            GUI.Box(new Rect(thrusterPanelX, panelY, thrusterPanelWidth, thrusterPanelHeight), "", _boxStyle);

            float tx = thrusterPanelX + padding;
            float ty = panelY + padding;

            GUI.Label(new Rect(tx, ty, thrusterPanelWidth - padding * 2, lineHeight),
                "THRUSTER MONITOR", _headerStyle);
            ty += lineHeight + 4f;

            DrawRow(ref ty, tx, tx + thrusterPanelWidth * 0.6f, thrusterPanelWidth * 0.35f, lineHeight,
                "Master Throttle Mult", _thrusterBus.masterThrottleMultiplier.ToString("F2"), _labelStyle, _valueStyle);
            ty += 6f;

            foreach (var entry in _thrusterBus.thrusters)
            {
                if (entry.node == null) continue;

                string statusStr = entry.enabled ? "ON" : "OFF";
                Color statusColor = entry.enabled ? Color.green : Color.red;

                // Thruster label
                GUI.Label(new Rect(tx, ty, 110f, lineHeight), entry.label, _labelStyle);

                // Throttle and force reading
                string telemetryStr = string.Format("T: {0:F1} | F: {1:F1}", entry.lastThrottle, entry.lastAppliedForce);
                GUIStyle telemetryStyle = new GUIStyle(_valueStyle);
                telemetryStyle.normal.textColor = Color.cyan;
                GUI.Label(new Rect(tx + 115f, ty, 130f, lineHeight), telemetryStr, telemetryStyle);

                // Status
                GUIStyle statusStyle = new GUIStyle(_valueStyle);
                statusStyle.normal.textColor = statusColor;
                statusStyle.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(tx + 250f, ty, 50f, lineHeight), statusStr, statusStyle);

                ty += lineHeight;
            }
        }

        // ── G-Force Meter ─────────────────────────────────────────
        DrawGForceMeter(panelX, panelY + panelHeight + 12f);
    }

    // ═══════════════════════════════════════════════════════════════
    //  G-FORCE METER
    // ═══════════════════════════════════════════════════════════════

    void DrawGForceMeter(float originX, float originY)
    {
        float size        = gForceGaugeSize;
        float totalHeight = size + 70f;
        float totalWidth  = size + 16f;
        float padding     = 8f;

        // Background panel
        GUI.Box(new Rect(originX, originY, totalWidth, totalHeight), "", _boxStyle);

        // Header
        GUI.Label(new Rect(originX + padding, originY + 4f, totalWidth - padding * 2, 20f),
            "G-FORCE", _headerStyle);

        float gaugeX  = originX + (totalWidth - size) * 0.5f;
        float gaugeY  = originY + 24f;
        float centerX = gaugeX + size * 0.5f;
        float centerY = gaugeY + size * 0.5f;
        float radius  = size * 0.5f - 4f;

        // ── Crosshairs ────────────────────────────────────────────
        GUI.DrawTexture(new Rect(gaugeX + 4f, centerY - 0.5f, size - 8f, 1f), _gCrosshairPixel);
        GUI.DrawTexture(new Rect(centerX - 0.5f, gaugeY + 4f, 1f, size - 8f), _gCrosshairPixel);

        // ── G rings ───────────────────────────────────────────────
        DrawCircleOutline(centerX, centerY, radius * (1f / gForceMaxDisplay), 48,
            new Color(0.3f, 0.8f, 0.3f, 0.3f));
        DrawCircleOutline(centerX, centerY, radius * (2f / gForceMaxDisplay), 48,
            new Color(1f, 0.8f, 0.2f, 0.25f));
        DrawCircleOutline(centerX, centerY, radius, 64,
            new Color(1f, 0.3f, 0.3f, 0.25f));

        // ── Ring labels ───────────────────────────────────────────
        GUIStyle ringLabel = new GUIStyle(_labelStyle);
        ringLabel.fontSize = 9;
        ringLabel.normal.textColor = new Color(1f, 1f, 1f, 0.4f);
        ringLabel.alignment = TextAnchor.MiddleLeft;

        float r1 = radius * (1f / gForceMaxDisplay);
        GUI.Label(new Rect(centerX + 2f, centerY - r1 - 7f, 30f, 14f), "1G", ringLabel);
        float r2 = radius * (2f / gForceMaxDisplay);
        GUI.Label(new Rect(centerX + 2f, centerY - r2 - 7f, 30f, 14f), "2G", ringLabel);

        // ── Axis labels ───────────────────────────────────────────
        GUIStyle axisLabel = new GUIStyle(_labelStyle);
        axisLabel.fontSize = 10;
        axisLabel.normal.textColor = new Color(1f, 1f, 1f, 0.35f);
        axisLabel.alignment = TextAnchor.MiddleCenter;

        GUI.Label(new Rect(centerX - 15f, gaugeY - 1f, 30f, 14f), "FWD", axisLabel);
        GUI.Label(new Rect(centerX - 15f, gaugeY + size - 14f, 30f, 14f), "BRK", axisLabel);
        axisLabel.alignment = TextAnchor.MiddleLeft;
        GUI.Label(new Rect(gaugeX + size - 4f, centerY - 7f, 25f, 14f), "R", axisLabel);
        axisLabel.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(gaugeX - 8f, centerY - 7f, 16f, 14f), "L", axisLabel);

        // ── Trail ─────────────────────────────────────────────────
        if (_gForceTrail != null)
        {
            for (int i = 0; i < TRAIL_LENGTH; i++)
            {
                int idx = (_trailIndex + i) % TRAIL_LENGTH;
                Vector2 trailG = _gForceTrail[idx];
                float trailPx = centerX + (trailG.x / gForceMaxDisplay) * radius;
                float trailPy = centerY - (trailG.y / gForceMaxDisplay) * radius;

                trailPx = Mathf.Clamp(trailPx, gaugeX + 4f, gaugeX + size - 4f);
                trailPy = Mathf.Clamp(trailPy, gaugeY + 4f, gaugeY + size - 4f);

                float alpha = (float)i / TRAIL_LENGTH * 0.4f;
                GUI.color = new Color(0.4f, 0.8f, 1f, alpha);
                float trailDotSize = 4f + (float)i / TRAIL_LENGTH * 3f;
                GUI.DrawTexture(new Rect(trailPx - trailDotSize * 0.5f, trailPy - trailDotSize * 0.5f,
                    trailDotSize, trailDotSize), _gTrailTexture);
            }
            GUI.color = Color.white;
        }

        // ── Current G dot ─────────────────────────────────────────
        float dotPx = centerX + (_smoothedGForce.x / gForceMaxDisplay) * radius;
        float dotPy = centerY - (_smoothedGForce.y / gForceMaxDisplay) * radius;

        dotPx = Mathf.Clamp(dotPx, gaugeX + 4f, gaugeX + size - 4f);
        dotPy = Mathf.Clamp(dotPy, gaugeY + 4f, gaugeY + size - 4f);

        // Color by intensity: green → yellow → orange → red
        float gMag = _smoothedGForce.magnitude;
        Color dotColor;
        if (gMag < 1f)
            dotColor = Color.Lerp(new Color(0.3f, 1f, 0.4f), new Color(1f, 1f, 0.3f), gMag);
        else if (gMag < 2f)
            dotColor = Color.Lerp(new Color(1f, 1f, 0.3f), new Color(1f, 0.4f, 0.1f), gMag - 1f);
        else
            dotColor = Color.Lerp(new Color(1f, 0.4f, 0.1f), new Color(1f, 0.1f, 0.1f),
                Mathf.Clamp01(gMag - 2f));

        // Line from center to dot
        DrawLine(centerX, centerY, dotPx, dotPy,
            new Color(dotColor.r, dotColor.g, dotColor.b, 0.4f));

        // Dot
        float dotSize = 14f;
        GUI.color = dotColor;
        GUI.DrawTexture(new Rect(dotPx - dotSize * 0.5f, dotPy - dotSize * 0.5f, dotSize, dotSize),
            _gDotTexture);
        GUI.color = Color.white;

        // ── Numeric readout ───────────────────────────────────────
        float textY = gaugeY + size + 4f;
        float textX = originX + padding;
        float textW = totalWidth - padding * 2f;

        GUIStyle gValStyle = new GUIStyle(_labelStyle);
        gValStyle.fontSize = 12;
        gValStyle.alignment = TextAnchor.MiddleCenter;
        gValStyle.normal.textColor = dotColor;

        GUI.Label(new Rect(textX, textY, textW, 18f),
            string.Format("Total: {0:F2}G", gMag), gValStyle);
        textY += 16f;

        gValStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        gValStyle.fontSize = 11;
        GUI.Label(new Rect(textX, textY, textW, 16f),
            string.Format("Lat: {0:F2}G  |  Lon: {1:F2}G", _smoothedGForce.x, _smoothedGForce.y),
            gValStyle);
    }

    // ═══════════════════════════════════════════════════════════════
    //  DRAWING HELPERS
    // ═══════════════════════════════════════════════════════════════

    void DrawCircleOutline(float cx, float cy, float r, int segments, Color color)
    {
        if (r < 1f) return;
        GUI.color = color;
        float angleStep = 360f / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float px = cx + Mathf.Cos(angle) * r;
            float py = cy - Mathf.Sin(angle) * r;
            GUI.DrawTexture(new Rect(px - 0.5f, py - 0.5f, 2f, 2f), _gRingPixel);
        }
        GUI.color = Color.white;
    }

    void DrawLine(float x1, float y1, float x2, float y2, Color color)
    {
        float dist = Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2));
        if (dist < 1f) return;
        GUI.color = color;
        int steps = Mathf.CeilToInt(dist / 2f);
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float px = Mathf.Lerp(x1, x2, t);
            float py = Mathf.Lerp(y1, y2, t);
            GUI.DrawTexture(new Rect(px - 0.5f, py - 0.5f, 2f, 2f), _gRingPixel);
        }
        GUI.color = Color.white;
    }

    // ───────────────────────────────────────────────────────────────
    void DrawRow(ref float y, float labelX, float valueX, float valueW,
        float lineHeight, string label, string value, GUIStyle ls, GUIStyle vs)
    {
        GUI.Label(new Rect(labelX, y, valueX - labelX, lineHeight), label, ls);
        GUI.Label(new Rect(valueX, y, valueW, lineHeight), value, vs);
        y += lineHeight;
    }
}
