using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Player-facing V2 hovercraft HUD — an animated, resolution-aware sci-fi interface.
/// <para>
/// A restrained racing interface: one unified top-center command cluster combining
/// velocity, OVERCHARGE, POWER BUS, stabilizer, and contextual warning chips. Deep telemetry
/// remains on the F2 HUD and never competes with the driving view.
/// </para>
/// <para>
/// Motion (integrated once per frame in <see cref="Update"/>): intro rise/fade, an
/// odometer speed sweep, a boost "punch" (number pop + burst halo) fired off the boost
/// sequence id, a READY glow pulse, and a charging shimmer. Energy/deep telemetry stay
/// on the F2 HUD.
/// </para>
/// </summary>
public class CraftHUD : MonoBehaviour
{
    [Header("References")]
    public CraftCore craftCore;

    [Header("Visibility")]
    public bool showHUD = true;
    [Tooltip("If true, missing CraftCore is auto-found at runtime.")]
    public bool autoFindCraftCore = true;

    [Header("Layout")]
    [Tooltip("Distance from the screen edges.")]
    public Vector2 margin = new Vector2(28f, 24f);
    [Tooltip("Extra size multiplier on top of automatic resolution scaling.")]
    [Range(0.6f, 1.8f)] public float uiScale = 0.9f;

    [Header("Motion")]
    public bool animate = true;

    [Header("Sense Of Speed")]
    [Tooltip("Draw restrained peripheral streaks at racing speed. They stay outside the central driving view.")]
    public bool showPeripheralSpeedStreaks = true;
    [Tooltip("Speed where peripheral motion begins to appear.")]
    [Min(0f)] public float speedStreaksStartMps = 85f;
    [Tooltip("Speed where the normal (non-boost) streak effect is at full strength.")]
    [Min(1f)] public float speedStreaksFullMps = 340f;
    [Range(0f, 1.5f)] public float speedStreakOpacity = 0.55f;

    [Tooltip("Speed represented by a full velocity ladder.")]
    [Min(100f)] public float speedGaugeMaxKmh = 2000f;

    [Tooltip("At and above this speed the complete velocity instrument enters its red high-speed state.")]
    [Min(100f)] public float dangerSpeedKmh = 1800f;

    [Header("Velocity Language")]
    [Tooltip("Beginning of the electric-blue high-speed band.")]
    [Min(0f)] public float highSpeedKmh = 1000f;
    [Tooltip("Beginning of the magenta extreme-speed band.")]
    [Min(0f)] public float extremeSpeedKmh = 1500f;
    public Color speedCruiseGreen = new Color(0.49f, 0.78f, 0.56f, 1f);
    public Color speedElectricBlue = new Color(0.36f, 0.59f, 0.82f, 1f);
    public Color speedExtremeMagenta = new Color(0.76f, 0.42f, 0.64f, 1f);
    public Color speedCandyRed = new Color(1f, 0.10f, 0.26f, 1f);

    [Header("Drift Angle Indicator")]
    [Tooltip("Show the compact three-node slip-angle instrument directly below velocity.")]
    public bool showDriftAngleIndicator = true;
    [Tooltip("Side-slip angle represented by the full left/right travel of the live node.")]
    [Range(10f, 60f)] public float driftFullScaleDegrees = 32f;
    [Tooltip("Angles inside this range settle at center so ordinary hover noise does not animate the HUD.")]
    [Range(0f, 8f)] public float driftDeadZoneDegrees = 1.5f;
    [Tooltip("Minimum planar speed before slip angle becomes meaningful.")]
    [Min(0f)] public float driftMinimumSpeedKmh = 30f;
    [Tooltip("How quickly the live drift node follows the measured angle.")]
    [Range(1f, 30f)] public float driftIndicatorResponse = 12f;

    [Header("Theme")]
    public Color accent = new Color(0.32f, 0.68f, 0.66f, 1f);
    public Color accentHot = new Color(0.82f, 0.43f, 0.58f, 1f);
    public Color panelColor = new Color(0.035f, 0.05f, 0.055f, 0.90f);
    public Color textColor = new Color(0.93f, 0.91f, 0.86f, 1f);
    public Color mutedColor = new Color(0.50f, 0.57f, 0.56f, 1f);
    public Color goodColor = new Color(0.49f, 0.78f, 0.56f, 1f);
    public Color warnColor = new Color(0.88f, 0.65f, 0.35f, 1f);
    public Color dangerColor = new Color(0.92f, 0.36f, 0.34f, 1f);

    // ── Styles ──────────────────────────────────────────────────────
    private GUIStyle _speed, _unit, _title, _titleRight, _status, _statusRight, _key, _val, _chip;
    private float _styleScale = -1f;

    // ── Animation state ─────────────────────────────────────────────
    private float _intro, _displaySpeed, _speedVel, _displayDriftAngle;
    private int _lastBoostSeq = int.MinValue;
    private float _boostPunch, _uiAlpha = 1f;

    // ── GL ──────────────────────────────────────────────────────────
    private static Material _glMat;

    // ── Layout snapshot (computed once per OnGUI, shared by shape + text passes) ──
    private Rect _speedR, _systemsR, _boostR, _statusR, _stateChipR, _busR;
    private string _kmh, _speedMode, _boostStatus, _overchargeState, _grip, _stab, _state, _busStatus, _busMode;
    private Color _speedColor, _boostColor, _gripColor, _stabColor, _stateColor, _busColor;
    private float _boostReady, _speedT, _speedFx, _boostFx, _busReserve, _busLimit;
    private int _speedTier;
    private bool _speedDanger, _boosting, _coolingDown, _hasBoost, _showStateChip, _hasBus, _busLimited;

    private float _nextAutoAssign;

    private void Awake() => TryAutoAssign();

    private void Update()
    {
        if (craftCore == null && autoFindCraftCore && Time.unscaledTime >= _nextAutoAssign)
        {
            _nextAutoAssign = Time.unscaledTime + 1f;
            TryAutoAssign();
        }

        float dt = Time.unscaledDeltaTime;
        float targetKmh = craftCore != null && craftCore.telemetry != null
            ? Mathf.Abs(craftCore.telemetry.CurrentTelemetry.speed) * 3.6f : 0f;

        float targetSpeedFx = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            speedStreaksStartMps,
            Mathf.Max(speedStreaksStartMps + 1f, speedStreaksFullMps),
            targetKmh / 3.6f));
        OverchargeCore boost = craftCore != null ? craftCore.overcharge : null;
        float targetBoostFx = boost != null ? boost.CameraBoost01 : 0f;

        float targetDriftAngle = 0f;
        if (craftCore != null && craftCore.telemetry != null)
        {
            CraftTelemetry telemetry = craftCore.telemetry.CurrentTelemetry;
            float planarSpeed = new Vector2(telemetry.forwardSpeed, telemetry.sideSpeed).magnitude;
            if (planarSpeed * 3.6f >= driftMinimumSpeedKmh)
            {
                // Signed angle between the craft nose and its actual direction of travel.
                // Abs(forward) keeps reversing from reading as an artificial 180-degree drift.
                targetDriftAngle = Mathf.Atan2(
                    telemetry.sideSpeed,
                    Mathf.Max(0.01f, Mathf.Abs(telemetry.forwardSpeed))) * Mathf.Rad2Deg;
                if (Mathf.Abs(targetDriftAngle) <= driftDeadZoneDegrees)
                    targetDriftAngle = 0f;
            }
        }

        float targetBusReserve = 0f;
        float targetBusLimit = 0f;
        if (craftCore != null && craftCore.energy != null)
        {
            EnergyState e = craftCore.energy.CurrentEnergy;
            float budget = Mathf.Max(0.001f, e.totalBudget);
            float protectedGranted = (e.baseHoverProtected ? e.baseHoverGranted : 0f)
                                   + (e.stabilizerProtected ? e.stabilizerGranted : 0f);
            float grantedLoad = Mathf.Clamp01((e.totalGranted - protectedGranted) / budget);
            targetBusReserve = 1f - grantedLoad;
            targetBusLimit = Mathf.Clamp01(1f - e.performancePowerScale01);
        }

        if (animate)
        {
            _intro = Mathf.MoveTowards(_intro, 1f, dt / 0.45f);
            _displaySpeed = Mathf.SmoothDamp(_displaySpeed, targetKmh, ref _speedVel, 0.10f, Mathf.Infinity, dt);

            OverchargeCore b = boost;
            if (b != null)
            {
                if (_lastBoostSeq == int.MinValue) _lastBoostSeq = b.BoostSequenceId;
                else if (b.BoostSequenceId != _lastBoostSeq) { _lastBoostSeq = b.BoostSequenceId; _boostPunch = 1f; }
            }
            _boostPunch = Mathf.MoveTowards(_boostPunch, 0f, dt / 0.55f);
            _speedFx = Mathf.Lerp(_speedFx, targetSpeedFx, 1f - Mathf.Exp(-4f * dt));
            float boostResponse = targetBoostFx > _boostFx ? 24f : 7f;
            _boostFx = Mathf.Lerp(_boostFx, targetBoostFx, 1f - Mathf.Exp(-boostResponse * dt));
            float busResponse = targetBusReserve < _busReserve ? 14f : 5f;
            _busReserve = Mathf.Lerp(_busReserve, targetBusReserve, 1f - Mathf.Exp(-busResponse * dt));
            float limitResponse = targetBusLimit > _busLimit ? 22f : 4f;
            _busLimit = Mathf.Lerp(_busLimit, targetBusLimit, 1f - Mathf.Exp(-limitResponse * dt));
            _displayDriftAngle = Mathf.Lerp(
                _displayDriftAngle,
                targetDriftAngle,
                1f - Mathf.Exp(-Mathf.Max(1f, driftIndicatorResponse) * dt));
        }
        else
        {
            _intro = 1f;
            _displaySpeed = targetKmh;
            _boostPunch = 0f;
            _speedFx = targetSpeedFx;
            _boostFx = targetBoostFx;
            _busReserve = targetBusReserve;
            _busLimit = targetBusLimit;
            _displayDriftAngle = targetDriftAngle;
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

    private float S => uiScale * Mathf.Clamp(Screen.height / 1080f, 0.8f, 2.4f);

    private void EnsureStyles()
    {
        float s = S;
        if (_speed != null && Mathf.Approximately(_styleScale, s)) return;
        _styleScale = s;
        _speed  = Style(40f, FontStyle.Bold, TextAnchor.MiddleCenter, textColor);
        _unit   = Style(13f, FontStyle.Bold, TextAnchor.UpperCenter,  mutedColor);
        _title  = Style(11f, FontStyle.Bold, TextAnchor.UpperLeft,    mutedColor);
        _titleRight = Style(11f, FontStyle.Bold, TextAnchor.UpperRight, mutedColor);
        _status = Style(22f, FontStyle.Bold, TextAnchor.MiddleLeft,   textColor);
        _statusRight = Style(22f, FontStyle.Bold, TextAnchor.MiddleRight, textColor);
        _key    = Style(13f, FontStyle.Bold, TextAnchor.MiddleLeft,   mutedColor);
        _val    = Style(15f, FontStyle.Bold, TextAnchor.MiddleRight,  textColor);
        _chip   = Style(13f, FontStyle.Bold, TextAnchor.MiddleCenter, textColor);
    }

    private GUIStyle Style(float size, FontStyle fs, TextAnchor a, Color c) => new GUIStyle(GUI.skin.label)
    {
        fontSize = Mathf.RoundToInt(size * S),
        fontStyle = fs,
        alignment = a,
        wordWrap = false,
        clipping = TextClipping.Overflow,
        padding = new RectOffset(0, 0, 0, 0),
        normal = { textColor = c }
    };

    private static void EnsureMat()
    {
        if (_glMat != null) return;
        Shader sh = Shader.Find("Hidden/Internal-Colored");
        if (sh == null) return;
        _glMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _glMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _glMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _glMat.SetInt("_Cull", (int)CullMode.Off);
        _glMat.SetInt("_ZWrite", 0);
    }

    private void OnGUI()
    {
        if (!showHUD || craftCore == null) return;
        EnsureStyles();
        ComputeLayout();

        _uiAlpha = animate ? Ease(_intro) : 1f;

        // 1) Shape pass (GL, repaint only).
        if (Event.current.type == EventType.Repaint)
        {
            EnsureMat();
            if (_glMat != null)
            {
                GL.PushMatrix();
                _glMat.SetPass(0);
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                ShapePass();
                GL.PopMatrix();
            }
        }

        // 2) Text pass (IMGUI, on top).
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, _uiAlpha);
        TextPass();
        GUI.color = prev;
    }

    // ══════════════════════════════════════════════════════════════
    //  LAYOUT
    // ══════════════════════════════════════════════════════════════
    private void ComputeLayout()
    {
        float s = S;
        CraftTelemetry t = GetTelemetry();
        OverchargeCore boost = craftCore.overcharge;

        // One top-center command cluster: equal power wings orbit a raised velocity
        // module. This keeps the craft silhouette and exhaust completely clear.
        float rise = (1f - Ease(_intro)) * 26f * s;
        float systemsW = Mathf.Min(920f * s, Screen.width - margin.x * 2f);
        float systemsH = 96f * s;
        _systemsR = new Rect(
            (Screen.width - systemsW) * 0.5f,
            margin.y + 10f * s - rise,
            systemsW,
            systemsH);

        float speedW = 244f * s;
        float speedH = 82f * s;
        _speedR = new Rect(
            (Screen.width - speedW) * 0.5f,
            margin.y - rise,
            speedW,
            speedH);

        float innerPad = 20f * s;
        float wingGap = 18f * s;
        float wingW = (_systemsR.width - innerPad * 2f - speedW - wingGap * 2f) * 0.5f;
        _boostR = new Rect(
            _systemsR.x + innerPad,
            _systemsR.y,
            wingW,
            _systemsR.height);
        _busR = new Rect(
            _systemsR.xMax - innerPad - wingW,
            _systemsR.y,
            wingW,
            _systemsR.height);

        _kmh = Mathf.RoundToInt(_displaySpeed).ToString();
        _speedT = Mathf.Clamp01(_displaySpeed / Mathf.Max(100f, speedGaugeMaxKmh));
        ResolveSpeedLanguage(_displaySpeed);
        _state = GetStateLabel(t, out _stateColor);
        _showStateChip = _state != "GROUNDED";
        _hasBoost = boost != null;
        _boosting = boost != null && boost.IsBoosting;
        _coolingDown = boost != null && boost.IsRecharging;
        if (boost == null)
        {
            _boostStatus = "--";
            _overchargeState = "OFFLINE";
            _boostColor = dangerColor;
            _boostReady = 0f;
        }
        else
        {
            _boostReady = boost.OverchargeReserve01;
            _boostStatus = $"{Mathf.RoundToInt(_boostReady * 100f)}%";
            _boostColor = _boostReady < 0.18f ? warnColor : goodColor;
            if (_boosting) _overchargeState = "DISCHARGING";
            else if (boost.BoostDeniedRecently || _boostReady <= 0.001f) _overchargeState = "EMPTY";
            else if (_coolingDown) _overchargeState = "RECHARGING";
            else if (_boostReady >= 0.999f) _overchargeState = "READY";
            else _overchargeState = "AVAILABLE";
        }

        // Context alerts dock below the cluster. When both are active they occupy
        // balanced slots instead of stacking or colliding with the velocity module.
        _grip = GetGripLabel(out _gripColor);
        _stab = GetStabilizerLabel(out _stabColor);
        bool showGripAlert = _grip != "LOCKED";
        float alertW = 170f * s;
        float alertH = 22f * s;
        float alertGap = 8f * s;
        float alertY = _systemsR.yMax + 8f * s;
        if (_showStateChip && showGripAlert)
        {
            _stateChipR = new Rect(Screen.width * 0.5f - alertGap * 0.5f - alertW, alertY, alertW, alertH);
            _statusR = new Rect(Screen.width * 0.5f + alertGap * 0.5f, alertY, alertW, alertH);
        }
        else
        {
            Rect centeredAlert = new Rect((Screen.width - alertW) * 0.5f, alertY, alertW, alertH);
            _stateChipR = centeredAlert;
            _statusR = centeredAlert;
        }

        // The BUS is a player-facing resource: it shows the shared performance-power
        // cap after protected base hover has been removed from the calculation.
        _hasBus = craftCore.energy != null;
        _busLimited = _hasBus && _busLimit > 0.01f;
        if (!_hasBus)
        {
            _busStatus = "--";
            _busMode = "OFFLINE";
            _busColor = dangerColor;
        }
        else if (_busLimited)
        {
            _busStatus = $"{Mathf.RoundToInt(_busReserve * 100f)}%";
            _busMode = $"THROTTLED {Mathf.RoundToInt((1f - _busLimit) * 100f)}%";
            _busColor = warnColor;
        }
        else if (_busReserve < 0.12f)
        {
            _busStatus = $"{Mathf.RoundToInt(_busReserve * 100f)}%";
            _busMode = "LOW RESERVE";
            _busColor = warnColor;
        }
        else
        {
            _busStatus = $"{Mathf.RoundToInt(_busReserve * 100f)}%";
            _busMode = "NOMINAL";
            _busColor = goodColor;
        }
    }

    /// <summary>
    /// Discrete racing-speed states let the player identify the current performance
    /// band peripherally without having to read the number.
    /// </summary>
    private void ResolveSpeedLanguage(float speedKmh)
    {
        float high = Mathf.Max(0f, highSpeedKmh);
        float extreme = Mathf.Max(high + 1f, extremeSpeedKmh);
        float redline = Mathf.Max(extreme + 1f, dangerSpeedKmh);

        if (speedKmh < high)
        {
            _speedTier = 0;
            _speedMode = "CRUISE";
            _speedColor = speedCruiseGreen;
        }
        else if (speedKmh < extreme)
        {
            _speedTier = 1;
            _speedMode = "HIGH VELOCITY";
            _speedColor = speedElectricBlue;
        }
        else if (speedKmh < redline)
        {
            _speedTier = 2;
            _speedMode = "EXTREME";
            _speedColor = speedExtremeMagenta;
        }
        else
        {
            _speedTier = 3;
            _speedMode = "REDLINE";
            float pulse = 0.08f + 0.14f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
            _speedColor = Color.Lerp(speedCandyRed, Color.white, pulse);
        }

        _speedDanger = _speedTier >= 3;
    }

    // ══════════════════════════════════════════════════════════════
    //  SHAPE PASS (GL)
    // ══════════════════════════════════════════════════════════════
    private void ShapePass()
    {
        float s = S;
        float cham = 16f * s;

        if (showPeripheralSpeedStreaks)
            SpeedStreakPass();
        if (_boostPunch > 0.001f)
            BoostScreenPulse();

        // ---- UNIFIED TOP COMMAND CLUSTER ----
        Color ribbonRail = _busLimited ? warnColor : accent;
        if (_boostPunch > 0.001f) BurstHalo(_systemsR, cham, goodColor, _boostPunch * 0.65f);
        Panel(_systemsR, cham, ribbonRail);
        Scanlines(_systemsR, cham);

        // Equal mirrored wings terminate cleanly at the raised velocity bay.
        float centerX = _systemsR.center.x;
        FillQuad(new Rect(_boostR.x, _systemsR.y, _boostR.width, 2f * s), _boostColor, 0.92f);
        FillQuad(new Rect(_busR.x, _systemsR.y, _busR.width, 2f * s), _busColor, 0.92f);
        FillQuad(new Rect(_speedR.x - 9f * s, _systemsR.y + 14f * s, 1f * s, _systemsR.height - 28f * s), accent, 0.13f);
        FillQuad(new Rect(_speedR.xMax + 8f * s, _systemsR.y + 14f * s, 1f * s, _systemsR.height - 28f * s), accent, 0.13f);
        DrawCenterTelemetrySpine(centerX, _systemsR.y + 82f * s);

        if (_coolingDown) Sweep(_systemsR, cham, 1.25f, true);
        else if (_boosting) Sweep(_systemsR, cham, 1.65f, false);

        float px = _boostR.x, pw = _boostR.width;
        Color bfill = _boosting ? Color.Lerp(goodColor, Color.white, 0.25f) : goodColor;
        SegmentLadder(new Rect(px, _systemsR.y + 54f * s, pw, 8f * s), Mathf.Clamp01(_boostReady), bfill, _coolingDown);

        float busX = _busR.x, busW = _busR.width;
        SegmentLadder(new Rect(busX, _systemsR.y + 54f * s, busW, 8f * s), _busReserve, _busColor, _busLimited);

        // Velocity is the raised focal module and is rendered last so the shared
        // ribbon reads as one fitted assembly rather than three floating boxes.
        if (_speedDanger)
        {
            float redPulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
            BurstHalo(_speedR, 12f * s, speedCandyRed, 0.56f + 0.22f * redPulse);
        }
        Panel(_speedR, 12f * s, _speedColor);
        SideTicks(_speedR, 12f * s, _speedColor);
        if (_speedTier >= 1)
            Sweep(_speedR, 12f * s, 0.85f + _speedTier * 0.38f);
        SegmentLadder(new Rect(_speedR.x + 17f * s, _speedR.yMax - 10f * s, _speedR.width - 34f * s, 4f * s),
                      _speedT, _speedColor, _speedTier >= 2);

        if (_showStateChip) Chip(_stateChipR, _stateColor);
        if (_grip != "LOCKED")
        {
            if (_showStateChip) Chip(_statusR, _gripColor);
            else AlertChip(_statusR, _gripColor);
        }
    }

    /// <summary>
    /// Peripheral-only motion streaks. Their vanishing point follows the screen center,
    /// but the inner third is kept clear so they add speed without obscuring the road.
    /// Boost increases brightness and length immediately, independently of speed lag.
    /// </summary>
    private void SpeedStreakPass()
    {
        float strength = Mathf.Clamp01(_speedFx * speedStreakOpacity + _boostFx * 0.75f);
        if (strength <= 0.002f) return;

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.52f);
        float radius = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height) * 0.5f;
        int count = 26 + Mathf.RoundToInt(18f * strength);
        float travel = Time.unscaledTime * Mathf.Lerp(0.75f, 2.3f, strength);

        GL.Begin(GL.QUADS);
        for (int i = 0; i < count; i++)
        {
            float seed = Mathf.Repeat(i * 0.6180339f, 1f);
            float angle = seed * Mathf.PI * 2f + Mathf.Sin(i * 2.17f) * 0.12f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float phase = Mathf.Repeat(travel + i * 0.137f, 1f);
            float inner = Mathf.Lerp(radius * 0.42f, radius * 0.90f, phase);
            float length = Mathf.Lerp(44f, 210f, strength) * Mathf.Lerp(0.7f, 1.3f, seed);
            float width = Mathf.Lerp(0.45f, 1.35f, strength);
            Vector2 a = center + dir * inner;
            Vector2 b = center + dir * Mathf.Min(radius, inner + length);

            GLc(Color.Lerp(accent, Color.white, 0.38f), strength * Mathf.Lerp(0.05f, 0.23f, phase));
            GL.Vertex3(a.x - perp.x * width, a.y - perp.y * width, 0f);
            GL.Vertex3(a.x + perp.x * width, a.y + perp.y * width, 0f);
            GL.Vertex3(b.x + perp.x * width * 0.25f, b.y + perp.y * width * 0.25f, 0f);
            GL.Vertex3(b.x - perp.x * width * 0.25f, b.y - perp.y * width * 0.25f, 0f);
        }
        GL.End();
    }

    /// <summary>Short edge pulse at boost onset; never fills or flashes the center.</summary>
    private void BoostScreenPulse()
    {
        float p = Mathf.Clamp01(_boostPunch);
        float inset = (1f - p) * 34f * S;
        float thickness = Mathf.Lerp(1f, 5f, p) * S;
        float alpha = p * 0.34f;
        Rect r = new Rect(inset, inset, Screen.width - inset * 2f, Screen.height - inset * 2f);
        FillQuad(new Rect(r.x, r.y, r.width, thickness), goodColor, alpha);
        FillQuad(new Rect(r.x, r.yMax - thickness, r.width, thickness), goodColor, alpha);
        FillQuad(new Rect(r.x, r.y, thickness, r.height), goodColor, alpha * 0.65f);
        FillQuad(new Rect(r.xMax - thickness, r.y, thickness, r.height), goodColor, alpha * 0.65f);
    }

    /// <summary>Chamfered glass panel: accent frame, dark body, bright top rail + glow.</summary>
    private void Panel(Rect r, float c, Color rail)
    {
        float s = S;
        // Drop shadow.
        FillPoly(Octa(new Rect(r.x + 5f * s, r.y + 7f * s, r.width, r.height), c), Color.black, 0.34f);
        // Restrained hairline silhouette. Color communicates state; it is not a neon box.
        FillPoly(Octa(r, c), rail, 0.26f);
        // Body inset over the frame → crisp uniform border.
        float t = 1.1f * s;
        FillPoly(Octa(new Rect(r.x + t, r.y + t, r.width - 2f * t, r.height - 2f * t), c), panelColor, panelColor.a);
        // Top rail (between chamfers) + glow bleed.
        FillQuad(new Rect(r.x + c, r.y, r.width - 2f * c, 2f * s), rail, 0.88f);
        FillQuad(new Rect(r.x + c, r.y + 2f * s, r.width - 2f * c, 5f * s), rail, 0.055f);
    }

    /// <summary>Faint horizontal scanlines inside a panel.</summary>
    private void Scanlines(Rect r, float c)
    {
        float s = S;
        for (float y = r.y + 14f * s; y < r.yMax - 6f * s; y += 6f * s)
            FillQuad(new Rect(r.x + c, y, r.width - 2f * c, 1f), accent, 0.035f);
    }

    /// <summary>A short highlight travelling along the top rail.</summary>
    private void Sweep(Rect r, float c, float speed, bool reverse = false)
    {
        float s = S;
        float railL = r.x + c, railW = r.width - 2f * c;
        float tt = Mathf.Repeat(Time.unscaledTime * speed, 1f);
        if (reverse) tt = 1f - tt;
        float sx = railL + tt * railW;
        float w = 46f * s;
        float x = Mathf.Clamp(sx - w * 0.5f, railL, railL + railW - w);
        FillQuad(new Rect(x, r.y, w, 3f * s), Color.white, 0.45f);
    }

    /// <summary>
    /// Compact signed slip-angle instrument beneath velocity. The two quiet outer
    /// diamonds establish the readable range; the live center diamond travels toward
    /// the craft's actual slide direction. The vertical stem preserves the visual
    /// relationship between velocity and the shared power ribbon.
    /// </summary>
    private void DrawCenterTelemetrySpine(float centerX, float centerY)
    {
        float s = S;
        float angle01 = Mathf.Clamp(
            _displayDriftAngle / Mathf.Max(1f, driftFullScaleDegrees), -1f, 1f);
        float severity = Mathf.Abs(angle01);
        float eased = Mathf.Sign(angle01) * Mathf.SmoothStep(0f, 1f, severity);
        float gripBreak = craftCore != null && craftCore.traction != null
            ? craftCore.traction.CurrentTraction.gripBreakerAmount
            : 0f;

        Color driftColor = Color.Lerp(goodColor, accent, Mathf.Clamp01(severity * 1.8f));
        if (severity > 0.62f)
            driftColor = Color.Lerp(driftColor, warnColor, Mathf.InverseLerp(0.62f, 1f, severity));
        driftColor = Color.Lerp(driftColor, warnColor, gripBreak * severity * 0.28f);

        float halfRange = 18f * s;
        float liveX = centerX + eased * halfRange;
        float spineTop = _speedR.yMax + 2f * s;
        float spineHeight = Mathf.Max(0f, centerY - 4f * s - spineTop);
        if (spineHeight > 0f)
            FillQuad(new Rect(centerX - 0.5f * s, spineTop, 1f * s, spineHeight), accent, 0.20f);

        if (!showDriftAngleIndicator) return;

        // Hairline range rail and a zero notch keep the control legible without text.
        FillQuad(new Rect(centerX - halfRange, centerY - 0.5f * s, halfRange * 2f, 1f * s), accent, 0.13f);
        FillQuad(new Rect(centerX - 0.5f * s, centerY - 3.5f * s, 1f * s, 7f * s), accent, 0.24f);

        // Quiet boundary nodes: these stay fixed, while the third node reports slip.
        DriftDiamond(centerX - halfRange, centerY, 2.2f * s, accent, 0.30f + (angle01 < 0f ? severity * 0.34f : 0f));
        DriftDiamond(centerX + halfRange, centerY, 2.2f * s, accent, 0.30f + (angle01 > 0f ? severity * 0.34f : 0f));

        float pulse = severity > 0.68f
            ? 0.82f + 0.18f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f))
            : 1f;
        float liveSize = Mathf.Lerp(3.1f, 4.2f, severity) * s;
        DriftDiamond(liveX, centerY, liveSize, driftColor, pulse);
    }

    private void DriftDiamond(float x, float y, float radius, Color color, float alpha)
    {
        FillPoly(new[]
        {
            new Vector2(x, y - radius),
            new Vector2(x + radius, y),
            new Vector2(x, y + radius),
            new Vector2(x - radius, y)
        }, color, alpha);
    }

    /// <summary>Small graduation ticks down the inner sides.</summary>
    private void SideTicks(Rect r, float c, Color col)
    {
        float s = S;
        int n = 6;
        for (int i = 0; i < n; i++)
        {
            float y = Mathf.Lerp(r.y + c + 6f * s, r.yMax - c - 6f * s, i / (float)(n - 1));
            float len = (i == 0 || i == n - 1) ? 12f * s : 6f * s;
            FillQuad(new Rect(r.x + 3f * s, y, len, 2f * s), col, 0.5f);
            FillQuad(new Rect(r.xMax - 3f * s - len, y, len, 2f * s), col, 0.5f);
        }
    }

    /// <summary>Skewed-segment ladder that fills to value01 (with an optional shimmering lead).</summary>
    private void SegmentLadder(Rect r, float value01, Color fill, bool shimmer)
    {
        float s = S;
        const int segs = 22;
        float gap = 2f * s;
        float skew = r.height * 0.55f;
        float segW = (r.width - gap * (segs - 1)) / segs;
        int lit = Mathf.RoundToInt(value01 * segs);
        for (int i = 0; i < segs; i++)
        {
            float x = r.x + i * (segW + gap);
            float a = i < lit ? 0.95f : 0.10f;
            if (shimmer && i == lit - 1) a = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
            FillSkew(x, r.y, segW, r.height, skew, fill, a);
        }
    }

    /// <summary>Thin requested-power marker above the granted BUS load.</summary>
    private void DemandMarker(Rect r, float demand01, Color color)
    {
        float x = Mathf.Lerp(r.x, r.xMax, Mathf.Clamp01(demand01));
        float w = 2f * S;
        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
        FillQuad(new Rect(x - w * 0.5f, r.y, w, r.height), color, pulse);
    }

    /// <summary>Small chamfered chip background + hairline frame.</summary>
    private void Chip(Rect r, Color color)
    {
        float c = 8f * S;
        FillPoly(Octa(r, c), color, 0.16f);
        float t = 1.4f * S;
        FillPoly(Octa(new Rect(r.x + t, r.y + t, r.width - 2f * t, r.height - 2f * t), c), panelColor, 0.55f);
    }

    /// <summary>A detached center alert with quiet bracket lines. It remains visually
    /// associated with the power ribbon without colliding with its top rail.</summary>
    private void AlertChip(Rect r, Color color)
    {
        float s = S;
        float lineY = r.center.y - 0.5f * s;
        float gap = 7f * s;
        float line = 38f * s;
        FillQuad(new Rect(r.x - gap - line, lineY, line, 1f * s), color, 0.34f);
        FillQuad(new Rect(r.xMax + gap, lineY, line, 1f * s), color, 0.34f);
        FillQuad(new Rect(r.x - gap - line, lineY, 1f * s, 5f * s), color, 0.45f);
        FillQuad(new Rect(r.xMax + gap + line - 1f * s, lineY, 1f * s, 5f * s), color, 0.45f);
        Chip(r, color);
    }

    /// <summary>Expanding translucent halo behind the boost card when it fires.</summary>
    private void BurstHalo(Rect r, float c, Color color, float punch)
    {
        float grow = (1f - punch) * 30f * S;
        Rect big = new Rect(r.x - grow, r.y - grow, r.width + grow * 2f, r.height + grow * 2f);
        FillPoly(Octa(big, c + 6f * S), color, 0.28f * punch);
    }

    // ── GL primitives ───────────────────────────────────────────────
    private static Vector2[] Octa(Rect r, float c)
    {
        c = Mathf.Min(c, Mathf.Min(r.width, r.height) * 0.5f);
        return new[]
        {
            new Vector2(r.x + c, r.y),
            new Vector2(r.xMax - c, r.y),
            new Vector2(r.xMax, r.y + c),
            new Vector2(r.xMax, r.yMax - c),
            new Vector2(r.xMax - c, r.yMax),
            new Vector2(r.x + c, r.yMax),
            new Vector2(r.x, r.yMax - c),
            new Vector2(r.x, r.y + c),
        };
    }

    private void GLc(Color c, float a)
        => GL.Color(new Color(c.r, c.g, c.b, Mathf.Clamp01(a) * _uiAlpha));

    private void FillPoly(Vector2[] p, Color c, float a)
    {
        GL.Begin(GL.TRIANGLES);
        GLc(c, a);
        for (int i = 1; i < p.Length - 1; i++)
        {
            GL.Vertex3(p[0].x, p[0].y, 0f);
            GL.Vertex3(p[i].x, p[i].y, 0f);
            GL.Vertex3(p[i + 1].x, p[i + 1].y, 0f);
        }
        GL.End();
    }

    private void FillQuad(Rect r, Color c, float a)
    {
        GL.Begin(GL.QUADS);
        GLc(c, a);
        GL.Vertex3(r.x, r.y, 0f);
        GL.Vertex3(r.xMax, r.y, 0f);
        GL.Vertex3(r.xMax, r.yMax, 0f);
        GL.Vertex3(r.x, r.yMax, 0f);
        GL.End();
    }

    private void FillSkew(float x, float y, float w, float h, float skew, Color c, float a)
    {
        GL.Begin(GL.QUADS);
        GLc(c, a);
        GL.Vertex3(x + skew, y, 0f);
        GL.Vertex3(x + skew + w, y, 0f);
        GL.Vertex3(x + w, y + h, 0f);
        GL.Vertex3(x, y + h, 0f);
        GL.End();
    }

    // ══════════════════════════════════════════════════════════════
    //  TEXT PASS (IMGUI)
    // ══════════════════════════════════════════════════════════════
    private void TextPass()
    {
        float s = S;

        // SPEED
        float speedPad = 17f * s;
        Color speedTitlePrev = _title.normal.textColor;
        _title.normal.textColor = Color.Lerp(mutedColor, _speedColor, 0.62f);
        GUI.Label(new Rect(_speedR.x + speedPad, _speedR.y + 8f * s, _speedR.width * 0.62f, 13f * s), _speedMode, _title);
        _title.normal.textColor = speedTitlePrev;
        GUI.Label(new Rect(_speedR.x + _speedR.width * 0.70f, _speedR.y + 8f * s, _speedR.width * 0.30f - speedPad, 13f * s), "KM/H", _titleRight);
        Rect numRect = new Rect(_speedR.x + speedPad, _speedR.y + 24f * s, _speedR.width - speedPad * 2f, 36f * s);
        Matrix4x4 m = GUI.matrix;
        if (_boostPunch > 0.001f)
            GUIUtility.ScaleAroundPivot(new Vector2(1f + 0.16f * _boostPunch, 1f + 0.16f * _boostPunch), numRect.center);
        ShadowLabel(numRect, _kmh, _speed,
            Color.Lerp(_speedColor, Color.white, _boostPunch * 0.22f));
        GUI.matrix = m;
        if (_showStateChip) ChipText(_stateChipR, _state, _stateColor);

        // OVERCHARGE
        float bx = _boostR.x, bw = _boostR.width;
        GUI.Label(new Rect(bx, _systemsR.y + 12f * s, bw * 0.52f, 15f * s), "OVERCHARGE", _title);
        Color prev = _titleRight.normal.textColor;
        _titleRight.normal.textColor = _boostColor;
        GUI.Label(new Rect(bx + bw * 0.52f, _systemsR.y + 12f * s, bw * 0.48f, 15f * s), _overchargeState, _titleRight);
        _titleRight.normal.textColor = prev;
        ShadowLabel(new Rect(bx, _systemsR.y + 27f * s, bw, 27f * s), _boostStatus, _status, _boostColor);
        GUI.Label(new Rect(bx, _systemsR.y + 72f * s, bw * 0.58f, 14f * s), _boosting ? "POWER DISCHARGE" : "STORED RESERVE", _title);
        GUI.Label(new Rect(bx + bw * 0.60f, _systemsR.y + 72f * s, bw * 0.40f, 14f * s), "[HOLD SPACE]", _titleRight);

        // POWER RESERVE is the remaining headroom inside the shared engine ceiling.
        // THROTTLED reports surviving performance authority under overload.
        float px = _busR.x, pw = _busR.width;
        prev = _title.normal.textColor;
        _title.normal.textColor = _busColor;
        GUI.Label(new Rect(px, _systemsR.y + 12f * s, pw * 0.48f, 15f * s), _busMode, _title);
        _title.normal.textColor = prev;
        GUI.Label(new Rect(px + pw * 0.48f, _systemsR.y + 12f * s, pw * 0.52f, 15f * s), "POWER BUS", _titleRight);
        ShadowLabel(new Rect(px, _systemsR.y + 27f * s, pw, 27f * s), _busStatus, _statusRight, _busColor);

        prev = _title.normal.textColor;
        _title.normal.textColor = _stabColor;
        GUI.Label(new Rect(px, _systemsR.y + 72f * s, pw * 0.58f, 14f * s), $"STABILIZER  {_stab}", _title);
        _title.normal.textColor = prev;
        GUI.Label(new Rect(px + pw * 0.60f, _systemsR.y + 72f * s, pw * 0.40f, 14f * s), "AVAILABLE POWER", _titleRight);

        if (_grip != "LOCKED") ChipText(_statusR, _grip == "BROKEN" ? "GRIP BREAK" : "GRIP SLIDE", _gripColor);
    }

    private void ChipText(Rect r, string label, Color color)
    {
        Color prev = _chip.normal.textColor;
        _chip.normal.textColor = color;
        GUI.Label(r, label, _chip);
        _chip.normal.textColor = prev;
    }

    private void KeyValue(Rect r, string key, string value, Color valueColor)
    {
        GUI.Label(new Rect(r.x, r.y, r.width * 0.45f, r.height), key, _key);
        Color prev = _val.normal.textColor;
        _val.normal.textColor = valueColor;
        GUI.Label(new Rect(r.x + r.width * 0.45f, r.y, r.width * 0.55f, r.height), value, _val);
        _val.normal.textColor = prev;
    }

    private void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
    {
        float s = S;
        Color prev = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
        GUI.Label(new Rect(r.x + 2f * s, r.y + 2f * s, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.normal.textColor = prev;
    }

    private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    // ══════════════════════════════════════════════════════════════
    //  DATA HELPERS
    // ══════════════════════════════════════════════════════════════
    private CraftTelemetry GetTelemetry()
        => craftCore.telemetry != null ? craftCore.telemetry.CurrentTelemetry : default;

    private string GetStateLabel(CraftTelemetry t, out Color color)
    {
        if (t.isInverted) { color = warnColor; return "INVERTED"; }
        if (t.isGrounded) { color = goodColor; return "GROUNDED"; }
        if (t.hasSurfaceContact) { color = accent; return "SURFACE"; }
        color = accent; return "AIRBORNE";
    }

    private string GetGripLabel(out Color color)
    {
        if (craftCore.traction == null) { color = mutedColor; return "N/A"; }
        float g = craftCore.traction.CurrentTraction.gripBreakerAmount;
        if (g > 0.75f) { color = warnColor; return "BROKEN"; }
        if (g > 0.15f) { color = warnColor; return "SLIDING"; }
        color = goodColor; return "LOCKED";
    }

    private string GetStabilizerLabel(out Color color)
    {
        HoverStabilizerArray h = craftCore.hoverArray;
        if (h == null || !h.IsStabilizerArmed) { color = mutedColor; return "OFF"; }
        if (h.IsStabilizerActive) { color = goodColor; return "ACTIVE"; }
        color = accent; return "ARMED";
    }

}
