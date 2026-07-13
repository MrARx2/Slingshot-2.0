using UnityEngine;

namespace TrackGeneration.Race
{
    /// <summary>
    /// Time-attack HUD: current lap timer, checkpoint progress, the last 5 laps, and the
    /// session-best lap with its average speed. Draws in the top-left corner — CraftHUD
    /// owns the other three corners.
    /// <para>
    /// Auto-finds the active <see cref="RaceCourse"/> and re-finds it after the track is
    /// regenerated (the course is destroyed and rebuilt with the track root).
    /// </para>
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [Header("References")]
        public RaceCourse course;

        [Header("Visibility")]
        public bool showHUD = true;

        [Header("Layout")]
        public Vector2 margin = new Vector2(24f, 20f);
        [Range(0.6f, 1.6f)] public float uiScale = 1.0f;
        public float rowHeight = 19f;

        [Header("Colors")]
        public Color panelColor = new Color(0f, 0f, 0f, 0.42f);
        public Color textColor = new Color(0.92f, 0.97f, 1f, 1f);
        public Color mutedColor = new Color(0.45f, 0.52f, 0.58f, 1f);
        public Color cyanColor = new Color(0.25f, 0.82f, 1f, 1f);
        public Color greenColor = new Color(0.35f, 1f, 0.45f, 1f);
        public Color yellowColor = new Color(1f, 0.75f, 0.25f, 1f);
        public Color redColor = new Color(1f, 0.25f, 0.2f, 1f);

        private const int LapHistoryRows = 5;

        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _largeStyle;

        // Course lookup scans the scene — retry at most once per second while none exists.
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
                fontSize = Mathf.RoundToInt(26f * uiScale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = textColor }
            };
        }

        private void OnGUI()
        {
            if (!showHUD || course == null) return;

            EnsureStyles();

            float width = 300f * uiScale;
            float x = margin.x;
            float y = margin.y;
            float pad = 12f * uiScale;

            float height = (10f + 22f + 34f + 20f + 6f + 18f + LapHistoryRows * rowHeight + 6f + rowHeight + 10f) * uiScale;
            DrawPanel(new Rect(x, y, width, height));

            float rowY = y + 10f * uiScale;
            float innerWidth = width - pad * 2f;

            // Header
            GUI.color = textColor;
            GUI.Label(new Rect(x + pad, rowY, innerWidth, 22f * uiScale), "TIME ATTACK", _headerStyle);
            rowY += 22f * uiScale;

            // Current lap time + lap number
            if (course.LapInProgress)
            {
                GUI.color = cyanColor;
                GUI.Label(new Rect(x + pad, rowY, innerWidth, 34f * uiScale), FormatTime(course.CurrentLapTime), _largeStyle);
                DrawRightAligned(new Rect(x + pad, rowY + 12f * uiScale, innerWidth, rowHeight * uiScale), $"LAP {course.CurrentLapNumber}", mutedColor);
            }
            else
            {
                GUI.color = mutedColor;
                GUI.Label(new Rect(x + pad, rowY, innerWidth, 34f * uiScale), "--:--.---", _largeStyle);
                DrawRightAligned(new Rect(x + pad, rowY + 12f * uiScale, innerWidth, rowHeight * uiScale), "CROSS THE LINE", mutedColor);
            }
            rowY += 34f * uiScale;

            // Checkpoint progress
            int cpTotal = course.CheckpointCount;
            int cpDone = Mathf.Min(course.NextCheckpointIndex, cpTotal);
            Color cpColor = !course.LapInProgress ? mutedColor : cpDone >= cpTotal ? greenColor : yellowColor;
            DrawKeyValue(new Rect(x + pad, rowY, innerWidth, 20f * uiScale), "CHECKPOINTS", $"{cpDone}/{cpTotal}", cpColor);
            rowY += (20f + 6f) * uiScale;

            // Lap history header
            GUI.color = mutedColor;
            GUI.Label(new Rect(x + pad, rowY, innerWidth, 18f * uiScale), "LAST LAPS", _smallStyle);
            rowY += 18f * uiScale;

            // Last 5 laps, newest first
            var laps = course.Laps;
            for (int row = 0; row < LapHistoryRows; row++)
            {
                Rect rect = new Rect(x + pad, rowY, innerWidth, rowHeight * uiScale);
                int lapIdx = laps.Count - 1 - row;
                if (lapIdx >= 0)
                {
                    LapRecord lap = laps[lapIdx];
                    bool isBest = course.BestLap != null && lap == course.BestLap;
                    Color timeColor = !lap.Valid ? mutedColor : isBest ? greenColor : textColor;

                    GUI.color = mutedColor;
                    GUI.Label(rect, $"L{lap.LapNumber}", _smallStyle);
                    GUI.color = timeColor;
                    GUI.Label(new Rect(rect.x + 42f * uiScale, rect.y, rect.width, rect.height), FormatTime(lap.TimeSeconds), _labelStyle);
                    DrawRightAligned(rect, lap.Valid ? $"{lap.AverageSpeedKmh:0} KM/H" : "INVALID", lap.Valid ? mutedColor : redColor);
                }
                else
                {
                    GUI.color = mutedColor;
                    GUI.Label(rect, "—", _smallStyle);
                }
                rowY += rowHeight * uiScale;
            }
            rowY += 6f * uiScale;

            // Session best
            Rect bestRect = new Rect(x + pad, rowY, innerWidth, rowHeight * uiScale);
            if (course.BestLap != null)
            {
                GUI.color = mutedColor;
                GUI.Label(bestRect, "BEST", _smallStyle);
                GUI.color = greenColor;
                GUI.Label(new Rect(bestRect.x + 42f * uiScale, bestRect.y, bestRect.width, bestRect.height), FormatTime(course.BestLap.TimeSeconds), _labelStyle);
                DrawRightAligned(bestRect, $"AVG {course.BestLap.AverageSpeedKmh:0} KM/H", greenColor);
            }
            else
            {
                DrawKeyValue(bestRect, "BEST", "NO VALID LAP", mutedColor);
            }

            GUI.color = Color.white;
        }

        private static string FormatTime(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            float rem = seconds - minutes * 60f;
            return $"{minutes}:{rem:00.000}";
        }

        private void DrawKeyValue(Rect rect, string key, string value, Color valueColor)
        {
            GUI.color = mutedColor;
            GUI.Label(rect, key, _smallStyle);
            DrawRightAligned(rect, value, valueColor);
        }

        private void DrawRightAligned(Rect rect, string text, Color color)
        {
            GUI.color = color;
            GUIStyle style = new GUIStyle(_smallStyle) { alignment = TextAnchor.UpperRight };
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        private void DrawPanel(Rect rect)
        {
            GUI.color = panelColor;
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
        }
    }
}
