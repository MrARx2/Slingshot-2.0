using UnityEngine;

namespace TrackGeneration.Race
{
    /// <summary>
    /// Compact player-facing time-attack HUD. Empty lap-history rows are never shown;
    /// the panel grows only when the player has useful results to read.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [Header("References")]
        public RaceCourse course;

        [Header("Visibility")]
        public bool showHUD = true;

        [Header("Layout")]
        public Vector2 margin = new Vector2(28f, 24f);
        [Range(0.6f, 1.6f)] public float uiScale = 0.9f;
        public float rowHeight = 19f;

        [Header("Colors")]
        public Color panelColor = new Color(0.035f, 0.05f, 0.055f, 0.90f);
        public Color textColor = new Color(0.93f, 0.91f, 0.86f, 1f);
        public Color mutedColor = new Color(0.50f, 0.57f, 0.56f, 1f);
        public Color cyanColor = new Color(0.32f, 0.68f, 0.66f, 1f);
        public Color greenColor = new Color(0.49f, 0.78f, 0.56f, 1f);
        public Color yellowColor = new Color(0.88f, 0.65f, 0.35f, 1f);
        public Color redColor = new Color(0.92f, 0.36f, 0.34f, 1f);

        private const int LapHistoryRows = 2;

        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _largeStyle;
        private GUIStyle _rightSmallStyle;
        private float _styleScale = -1f;
        private float _nextCourseSearchTime;

        private void Update()
        {
            if (course == null && Time.unscaledTime >= _nextCourseSearchTime)
            {
                _nextCourseSearchTime = Time.unscaledTime + 1f;
                course = FindAnyObjectByType<RaceCourse>();
            }
        }

        private void EnsureStyles()
        {
            float s = S;
            if (_labelStyle != null && Mathf.Approximately(_styleScale, s)) return;
            _styleScale = s;

            _headerStyle = NewStyle(15f, FontStyle.Bold, TextAnchor.UpperLeft, textColor);
            _labelStyle = NewStyle(13f, FontStyle.Bold, TextAnchor.UpperLeft, textColor);
            _smallStyle = NewStyle(11f, FontStyle.Bold, TextAnchor.UpperLeft, mutedColor);
            _largeStyle = NewStyle(27f, FontStyle.Bold, TextAnchor.UpperLeft, cyanColor);
            _rightSmallStyle = NewStyle(11f, FontStyle.Bold, TextAnchor.UpperRight, mutedColor);
        }

        private GUIStyle NewStyle(float size, FontStyle fontStyle, TextAnchor anchor, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * S),
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = color }
            };
        }

        private void OnGUI()
        {
            if (!showHUD || course == null) return;
            EnsureStyles();

            var laps = course.Laps;
            int historyRows = Mathf.Min(LapHistoryRows, laps.Count);
            bool showHistory = historyRows > 0;

            float s = S;
            float width = 238f * s;
            float pad = 16f * s;
            float height = (course.LapInProgress ? 88f : 64f) * s;
            if (showHistory) height += (24f + historyRows * rowHeight + 23f) * s;

            Rect panel = new Rect(margin.x, margin.y, width, height);
            DrawPanel(panel);

            float x = panel.x + pad;
            float y = panel.y + 11f * s;
            float innerWidth = panel.width - pad * 2f;
            int cpTotal = course.CheckpointCount;
            int cpDone = Mathf.Min(course.NextCheckpointIndex, cpTotal);
            Color cpColor = !course.LapInProgress ? mutedColor : cpDone >= cpTotal ? greenColor : yellowColor;

            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, innerWidth, 18f * s), "TIME ATTACK", _smallStyle);
            DrawRightAligned(new Rect(x, y, innerWidth, 18f * s), $"CP {cpDone}/{cpTotal}", cpColor);
            y += 17f * s;

            if (course.LapInProgress)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(x, y, innerWidth, 31f * s), FormatTime(course.CurrentLapTime), _largeStyle);
                y += 30f * s;
                DrawKeyValue(new Rect(x, y, innerWidth, 18f * s), "CURRENT", $"LAP {course.CurrentLapNumber}", mutedColor);
                y += 20f * s;
            }
            else
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(x, y + 4f * s, innerWidth, 25f * s), "CROSS START LINE", _headerStyle);
                y += 31f * s;
            }

            if (showHistory)
            {
                GUI.color = new Color(cyanColor.r, cyanColor.g, cyanColor.b, 0.30f);
                GUI.DrawTexture(new Rect(x, y + 2f * s, innerWidth, 1f * s), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(x, y + 7f * s, innerWidth, 16f * s), "RECENT", _smallStyle);
                y += 23f * s;

                for (int row = 0; row < historyRows; row++)
                {
                    LapRecord lap = laps[laps.Count - 1 - row];
                    bool isBest = course.BestLap != null && lap == course.BestLap;
                    Color timeColor = !lap.Valid ? mutedColor : isBest ? greenColor : textColor;
                    Rect r = new Rect(x, y, innerWidth, rowHeight * s);

                    GUI.color = Color.white;
                    GUI.Label(r, $"L{lap.LapNumber}", _smallStyle);
                    Color previous = _labelStyle.normal.textColor;
                    _labelStyle.normal.textColor = timeColor;
                    GUI.Label(new Rect(r.x + 31f * s, r.y, r.width, r.height), FormatTime(lap.TimeSeconds), _labelStyle);
                    _labelStyle.normal.textColor = previous;
                    DrawRightAligned(r, isBest ? "BEST" : lap.Valid ? $"{lap.AverageSpeedKmh:0}" : "INVALID",
                        isBest ? greenColor : lap.Valid ? mutedColor : redColor);
                    y += rowHeight * s;
                }

                if (course.BestLap != null)
                {
                    Rect best = new Rect(x, y + 2f * s, innerWidth, rowHeight * s);
                    DrawKeyValue(best, "SESSION BEST", FormatTime(course.BestLap.TimeSeconds), greenColor);
                }
            }

            GUI.color = Color.white;
        }

        private static string FormatTime(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return $"{minutes}:{remainder:00.000}";
        }

        private void DrawKeyValue(Rect rect, string key, string value, Color valueColor)
        {
            GUI.color = Color.white;
            GUI.Label(rect, key, _smallStyle);
            DrawRightAligned(rect, value, valueColor);
        }

        private void DrawRightAligned(Rect rect, string text, Color color)
        {
            Color guiColor = GUI.color;
            Color previous = _rightSmallStyle.normal.textColor;
            GUI.color = Color.white;
            _rightSmallStyle.normal.textColor = color;
            GUI.Label(rect, text, _rightSmallStyle);
            _rightSmallStyle.normal.textColor = previous;
            GUI.color = guiColor;
        }

        private void DrawPanel(Rect rect)
        {
            float s = S;
            GUI.color = new Color(0f, 0f, 0f, 0.30f);
            GUI.DrawTexture(new Rect(rect.x + 5f * s, rect.y + 7f * s, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = panelColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(cyanColor.r, cyanColor.g, cyanColor.b, 0.48f);
            GUI.DrawTexture(new Rect(rect.x + 14f * s, rect.y, rect.width - 28f * s, 2f * s), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y + 14f * s, 1f * s, rect.height - 28f * s), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private float S => uiScale * Mathf.Clamp(Screen.height / 1080f, 0.8f, 2.4f);
    }
}
