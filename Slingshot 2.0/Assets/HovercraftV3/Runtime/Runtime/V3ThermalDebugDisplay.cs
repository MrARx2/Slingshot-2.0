using System.Text;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3ThermalDebugDisplay : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private Vector2 screenPosition = new Vector2(12f, 12f);
        [SerializeField, Min(240f)] private float width = 420f;

        private readonly StringBuilder builder = new StringBuilder(1024);
        private V3ThermalController thermal;

        public bool ShowOverlay
        {
            get => showOverlay;
            set => showOverlay = value;
        }

        public void Initialize(V3ThermalController thermalController)
        {
            thermal = thermalController;
        }

        public string BuildDebugText()
        {
            builder.Clear();
            builder.Append("V3 THERMAL  max ");
            builder.Append(thermal != null
                ? thermal.MaximumTemperatureC.ToString("0.0")
                : "--");
            builder.AppendLine(" C");
            if (thermal == null)
            {
                builder.Append("No thermal controller");
                return builder.ToString();
            }

            for (int i = 0; i < thermal.Telemetry.Count; i++)
            {
                V3PartThermalTelemetry part = thermal.Telemetry[i];
                builder.Append(part.socketId);
                builder.Append("  ");
                builder.Append(part.currentTemperatureC.ToString("0.0"));
                builder.Append(" C  +");
                builder.Append(part.generatedHeatPerSecond.ToString("0.0"));
                builder.Append(" / -");
                builder.Append(part.passiveCoolingPerSecond.ToString("0.0"));
                if (part.isThermallyLockedOut)
                {
                    builder.Append("  ");
                    builder.Append(
                        part.protectionState ==
                        V3ThermalProtectionState.Overheated
                            ? "OVERHEATED"
                            : "COOLING LOCKOUT");
                }
                else if (part.protectionState ==
                    V3ThermalProtectionState.Warning)
                {
                    builder.Append("  WARNING ");
                    builder.Append((part.outputLimit * 100f).ToString("0"));
                    builder.Append("%");
                }
                else if (part.protectionState ==
                    V3ThermalProtectionState.Recovered)
                {
                    builder.Append("  RECOVERED");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private void OnGUI()
        {
            if (!showOverlay || thermal == null)
            {
                return;
            }

            int lines = Mathf.Max(2, thermal.Telemetry.Count + 1);
            GUI.Box(
                new Rect(
                    screenPosition.x,
                    screenPosition.y,
                    width,
                    10f + lines * 19f),
                BuildDebugText());
        }
    }
}
