using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Lightweight IMGUI instrumentation for the standalone free-drive scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3FreeDriveHud : MonoBehaviour
    {
        [SerializeField] private V3FreeDriveSession session;

        private GUIStyle titleStyle;
        private GUIStyle valueStyle;
        private GUIStyle labelStyle;
        private GUIStyle controlsStyle;
        private GUIStyle statusStyle;
        private bool hasObservedPowerMode;
        private V3PowerAllocationMode observedPowerMode;
        private float powerModeMessageUntil;

        private void OnGUI()
        {
            if (session == null || !session.ShowHud)
            {
                return;
            }

            EnsureStyles();
            ObservePowerMode();
            DrawTelemetryPanel();
            DrawControlsPanel();
            DrawStatus();
            DrawReticle();
        }

        private void DrawTelemetryPanel()
        {
            Rect safe = Screen.safeArea;
            Rect panel = new Rect(
                safe.x + 16f,
                safe.y + 16f,
                310f,
                215f);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.075f, 0.88f));

            CraftBuildDefinition build =
                session.CraftRuntime != null
                    ? session.CraftRuntime.Build
                    : null;
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 10f, 278f, 28f),
                build != null
                    ? build.DisplayName.ToUpperInvariant()
                    : "APEX V3  //  FREE DRIVE",
                titleStyle);
            if (build != null)
            {
                GUI.Label(
                    new Rect(panel.x + 16f, panel.y + 34f, 278f, 20f),
                    build.LayoutName.ToUpperInvariant(),
                    labelStyle);
            }

            V3CraftTelemetryHub data = session.Telemetry;
            if (data == null)
            {
                GUI.Label(
                    new Rect(panel.x + 16f, panel.y + 62f, 278f, 26f),
                    "ASSEMBLING CRAFT...",
                    valueStyle);
                return;
            }

            float altitude = 0f;
            bool hasAltitude = session.TryGetAltitude(out altitude);
            DrawMetric(panel, 62f, "SPEED", $"{data.SpeedKmh:0} km/h");
            DrawMetric(
                panel,
                90f,
                "ALTITUDE",
                hasAltitude ? $"{altitude:0.0} m" : "---");
            DrawMetric(
                panel,
                118f,
                "POWER MODE",
                data.PowerAllocationMode.ToString().ToUpperInvariant());
            DrawMetric(
                panel,
                146f,
                "MAX TEMP",
                $"{data.MaximumTemperatureC:0.0} C");

            string stability =
                session.PilotInput != null &&
                session.PilotInput.StabilizationEnabled
                    ? "ON"
                    : "OFF";
            DrawMetric(panel, 174f, "STABILIZATION", stability);
        }

        private void DrawControlsPanel()
        {
            Rect safe = Screen.safeArea;
            const float width = 700f;
            const float height = 126f;
            Rect panel = new Rect(
                safe.x + 16f,
                safe.yMax - height - 16f,
                Mathf.Min(width, safe.width - 32f),
                height);
            DrawPanel(panel, new Color(0.025f, 0.055f, 0.075f, 0.82f));
            GUI.Label(
                new Rect(
                    panel.x + 14f,
                panel.y + 10f,
                    panel.width - 28f,
                    panel.height - 20f),
                "W/S  THROTTLE & BRAKE     A/D  STRAFE     MOUSE  STEER\n" +
                "E/Q  LIFT & DOWNFORCE     SHIFT  DRIFT     CTRL  OVERLOAD\n" +
                "R  STABILIZATION     TAB  BAL > PROP > STAB > REC     BACKSPACE  RESET\n" +
                "F1  HUD     F2  THRUSTER VECTORS     ARROWS  HELD GIMBAL/YAW TEST",
                controlsStyle);
        }

        private void DrawStatus()
        {
            V3CockpitWarningController warning = session.Warnings;
            string text =
                warning != null &&
                warning.AlertLevel != V3CockpitAlertLevel.Clear
                    ? warning.AlertMessage
                    : Time.unscaledTime <= powerModeMessageUntil &&
                      session.Telemetry != null
                        ? BuildPowerModeMessage(session.Telemetry)
                    : session.StatusMessage;
            Color color =
                warning != null
                    ? AlertColor(warning.AlertLevel)
                    : Color.white;

            statusStyle.normal.textColor = color;
            Rect safe = Screen.safeArea;
            GUI.Label(
                new Rect(
                    safe.center.x - 240f,
                    safe.y + 20f,
                    480f,
                    58f),
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

        private static string BuildPowerModeMessage(
            V3CraftTelemetryHub data)
        {
            return
                $"{data.PowerAllocationMode.ToString().ToUpperInvariant()}: " +
                V3PowerModeInfo.GetShortDescription(
                    data.PowerAllocationMode) +
                "\n" +
                V3PowerModeInfo.GetLimitationNote(
                    data.IsPropulsionPowerLimited);
        }

        private static void DrawReticle()
        {
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            Color previous = GUI.color;
            GUI.color = new Color(0.35f, 0.9f, 1f, 0.8f);
            GUI.DrawTexture(
                new Rect(x - 9f, y - 1f, 18f, 2f),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(x - 1f, y - 9f, 2f, 18f),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawMetric(
            Rect panel,
            float yOffset,
            string label,
            string value)
        {
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + yOffset, 122f, 24f),
                label,
                labelStyle);
            GUI.Label(
                new Rect(panel.x + 138f, panel.y + yOffset, 156f, 24f),
                value,
                valueStyle);
        }

        private static void DrawPanel(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.25f, 0.85f, 1f, 0.65f);
            GUI.DrawTexture(
                new Rect(rect.x, rect.y, 3f, rect.height),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.35f, 0.9f, 1f) }
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.55f, 0.7f, 0.76f) }
            };
            valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };
            controlsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.92f, 0.95f) }
            };
            statusStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
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
