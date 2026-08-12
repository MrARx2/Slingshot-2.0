using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [CreateAssetMenu(
        menuName = "Hovercraft V3/UI/Systems Console Theme",
        fileName = "GenericSystemsTheme")]
    public sealed class V3SystemUIThemeDefinition : ScriptableObject
    {
        [SerializeField] private Color panelColor =
            new Color(0.025f, 0.045f, 0.06f, 0.94f);
        [SerializeField] private Color headerColor =
            new Color(0.08f, 0.75f, 0.95f, 1f);
        [SerializeField] private Color textColor =
            new Color(0.82f, 0.94f, 0.98f, 1f);
        [SerializeField] private Color healthyColor =
            new Color(0.25f, 1f, 0.55f, 1f);
        [SerializeField] private Color warningColor =
            new Color(1f, 0.72f, 0.15f, 1f);

        public Color PanelColor => panelColor;
        public Color HeaderColor => headerColor;
        public Color TextColor => textColor;
        public Color HealthyColor => healthyColor;
        public Color WarningColor => warningColor;
    }
}
