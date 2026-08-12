using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Conservative racing HUD for development driving. The pilot strip is the
    /// only panel shown on startup; detailed controls and systems are opt-in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3FreeDriveHud : MonoBehaviour
    {
        [SerializeField] private V3FreeDriveSession session;
        [SerializeField] private bool showControls;

        private GUIStyle speedStyle;
        private GUIStyle speedUnitStyle;
        private GUIStyle metricLabelStyle;
        private GUIStyle metricValueStyle;
        private GUIStyle craftStyle;
        private GUIStyle controlsStyle;
        private GUIStyle statusStyle;
        private GUIStyle keyHintStyle;
        private GUIStyle keyHintLeftStyle;
        private GUIStyle keyHintRightStyle;
        private bool hasObservedPowerMode;
        private V3PowerAllocationMode observedPowerMode;
        private float powerModeMessageUntil;

        public bool ShowControls => showControls;

        public void ToggleControls()
        {
            showControls = !showControls;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                ToggleControls();
            }
        }

        private void OnGUI()
        {
            if (session == null || !session.ShowHud)
            {
                return;
            }

            EnsureStyles();
            ObservePowerMode();
            DrawTopHints();
            DrawPilotHud();
            if (showControls)
            {
                DrawControlsPanel();
            }
            DrawStatus();
            DrawReticle();
        }

        private void DrawTopHints()
        {
            Rect safe = Screen.safeArea;
            GUI.Label(
                new Rect(safe.center.x - 150f, safe.y + 10f, 300f, 22f),
                "F7  SESSION RECORDER",
                keyHintStyle);

            GUI.Label(
                new Rect(safe.xMax - 250f, safe.y + 10f, 236f, 22f),
                "F4  CRAFT SYSTEMS",
                keyHintRightStyle);
            GUI.Label(
                new Rect(safe.xMax - 250f, safe.y + 32f, 236f, 22f),
                "F2  DEBUG",
                keyHintRightStyle);
            GUI.Label(
                new Rect(safe.x + 14f, safe.y + 10f, 220f, 22f),
                "F3  CONTROLS    F1  HUD",
                keyHintLeftStyle);
        }

        private void DrawPilotHud()
        {
            Rect safe = Screen.safeArea;
            float width = Mathf.Min(1040f, safe.width - 24f);
            float left = safe.center.x - width * 0.5f;
            float baseline = safe.yMax - 82f;
            V3CraftTelemetryHub data = session.Telemetry;

            CraftBuildDefinition build = session.CraftRuntime != null
                ? session.CraftRuntime.Build
                : null;
            GUI.Label(
                new Rect(safe.center.x - 260f, baseline - 21f, 520f, 20f),
                build != null
                    ? build.DisplayName.ToUpperInvariant()
                    : "ASSEMBLING CRAFT",
                craftStyle);

            float speedKmh = data != null ? data.SpeedKmh : 0f;
            GUI.Label(
                new Rect(safe.center.x - 150f, baseline, 250f, 58f),
                speedKmh.ToString("0"),
                speedStyle);
            GUI.Label(
                new Rect(safe.center.x + 94f, baseline + 28f, 70f, 25f),
                "km/h",
                speedUnitStyle);

            float altitude = 0f;
            bool hasAltitude = session.TryGetAltitude(out altitude);
            DrawMetric(
                left + width * 0.08f,
                baseline + 4f,
                "ALTITUDE",
                hasAltitude ? altitude.ToString("0.0") + " m" : "---");
            DrawMetric(
                left + width * 0.27f,
                baseline + 4f,
                "POWER",
                data != null
                    ? data.PowerAllocationMode.ToString().ToUpperInvariant()
                    : "---");
            DrawMetric(
                left + width * 0.73f,
                baseline + 4f,
                "STABILITY",
                session.PilotInput != null &&
                session.PilotInput.StabilizationEnabled ? "ON" : "OFF");
            DrawMetric(
                left + width * 0.92f,
                baseline + 4f,
                "MAX TEMP",
                data != null
                    ? data.MaximumTemperatureC.ToString("0") + " C"
                    : "---");
        }

        private void DrawMetric(float centerX, float y, string label, string value)
        {
            GUI.Label(
                new Rect(centerX - 75f, y, 150f, 20f),
                label,
                metricLabelStyle);
            GUI.Label(
                new Rect(centerX - 75f, y + 20f, 150f, 28f),
                value,
                metricValueStyle);
        }

        private void DrawControlsPanel()
        {
            Rect safe = Screen.safeArea;
            const float width = 760f;
            const float height = 108f;
            Rect panel = new Rect(
                safe.center.x - Mathf.Min(width, safe.width - 24f) * 0.5f,
                safe.yMax - 205f,
                Mathf.Min(width, safe.width - 24f),
                height);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.52f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 10f,
                    panel.width - 32f, panel.height - 20f),
                "W/S  THROTTLE & BRAKE     A/D  STRAFE     MOUSE  STEER     E/Q  LIFT & DOWNFORCE\n" +
                "SHIFT  DRIFT     CTRL  OVERLOAD     R  STABILIZATION     TAB  POWER MODE\n" +
                "BACKSPACE  RESET     F2  DEBUG     F4  SYSTEMS     F7  RECORD     F8  MARK     F9  EXPORT\n" +
                "O/P  SYSTEM PAGES     I  MINIMIZE SYSTEMS     ARROWS  GIMBAL/YAW TEST",
                controlsStyle);
        }

        private void DrawStatus()
        {
            V3CockpitWarningController warning = session.Warnings;
            string text = warning != null &&
                warning.AlertLevel != V3CockpitAlertLevel.Clear
                    ? warning.AlertMessage
                    : Time.unscaledTime <= powerModeMessageUntil &&
                      session.Telemetry != null
                        ? BuildPowerModeMessage(session.Telemetry)
                        : session.StatusMessage;
            statusStyle.normal.textColor = warning != null
                ? AlertColor(warning.AlertLevel)
                : Color.white;
            Rect safe = Screen.safeArea;
            GUI.Label(
                new Rect(safe.center.x - 260f, safe.y + 38f, 520f, 52f),
                text,
                statusStyle);
        }

        private void ObservePowerMode()
        {
            V3CraftTelemetryHub data = session.Telemetry;
            if (data == null)
            {
                return;
            }

            if (!hasObservedPowerMode ||
                observedPowerMode != data.PowerAllocationMode)
            {
                hasObservedPowerMode = true;
                observedPowerMode = data.PowerAllocationMode;
                powerModeMessageUntil = Time.unscaledTime + 4f;
            }
        }

        private static string BuildPowerModeMessage(V3CraftTelemetryHub data)
        {
            return data.PowerAllocationMode.ToString().ToUpperInvariant() +
                ": " + V3PowerModeInfo.GetShortDescription(data.PowerAllocationMode) +
                "\n" + V3PowerModeInfo.GetLimitationNote(
                    data.IsPropulsionPowerLimited);
        }

        private static void DrawReticle()
        {
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            Color previous = GUI.color;
            GUI.color = new Color(0.35f, 0.9f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(x - 8f, y - 1f, 16f, 2f),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1f, y - 8f, 2f, 16f),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (speedStyle != null)
            {
                return;
            }

            Color cyan = new Color(0.35f, 0.9f, 1f);
            speedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };
            speedUnitStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = cyan }
            };
            metricLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.55f, 0.7f, 0.76f) }
            };
            metricValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            craftStyle = new GUIStyle(metricLabelStyle)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = cyan }
            };
            controlsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.88f, 0.94f, 0.96f) }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            keyHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.72f, 0.82f, 0.86f) }
            };
            keyHintLeftStyle = new GUIStyle(keyHintStyle)
            {
                alignment = TextAnchor.UpperLeft
            };
            keyHintRightStyle = new GUIStyle(keyHintStyle)
            {
                alignment = TextAnchor.UpperRight
            };
        }

        private static Color AlertColor(V3CockpitAlertLevel level)
        {
            switch (level)
            {
                case V3CockpitAlertLevel.Critical:
                    return new Color(1f, 0.2f, 0.15f);
                case V3CockpitAlertLevel.Warning:
                    return new Color(1f, 0.65f, 0.1f);
                case V3CockpitAlertLevel.Advisory:
                    return new Color(0.35f, 0.8f, 1f);
                default:
                    return Color.white;
            }
        }
    }
}
