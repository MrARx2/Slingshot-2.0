using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3CockpitWarningDisplay : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(220f)] private float width = 360f;
        [SerializeField] private float topMargin = 18f;

        private V3CockpitWarningController warnings;
        private GUIStyle style;

        public bool ShowOverlay
        {
            get => showOverlay;
            set => showOverlay = value;
        }

        public void Initialize(V3CockpitWarningController warningController)
        {
            warnings = warningController;
        }

        public string BuildDisplayText()
        {
            if (warnings == null)
            {
                return "COCKPIT WARNING DATA OFFLINE";
            }

            string telemetry =
                warnings.IsPropulsionPowerLimited ||
                warnings.AlertLevel == V3CockpitAlertLevel.Clear ||
                warnings.AlertLevel == V3CockpitAlertLevel.Advisory
                    ? $"POWER MODE  {warnings.PowerAllocationMode.ToString().ToUpperInvariant()}"
                    : $"MAX TEMP  {warnings.MaximumTemperatureC:0.0} C";
            return $"{warnings.AlertMessage}\n{telemetry}";
        }

        private void OnGUI()
        {
            if (!showOverlay || warnings == null)
            {
                return;
            }

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 16
                };
            }

            style.normal.textColor = AlertColor(warnings.AlertLevel);
            GUI.Box(
                new Rect(
                    (Screen.width - width) * 0.5f,
                    topMargin,
                    width,
                    54f),
                BuildDisplayText(),
                style);
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
