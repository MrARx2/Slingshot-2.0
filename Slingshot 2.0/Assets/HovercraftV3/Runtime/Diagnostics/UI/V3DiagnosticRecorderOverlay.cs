using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [DisallowMultipleComponent]
    public sealed class V3DiagnosticRecorderOverlay : MonoBehaviour
    {
        [SerializeField] private V3DiagnosticRecorder recorder;
        [SerializeField] private Rect panel = new Rect(12f, 12f, 520f, 128f);
        [SerializeField, Min(0f)] private float topMargin = 82f;
        [SerializeField] private bool visible = true;
        private GUIStyle boxStyle;
        private GUIStyle titleStyle;

        public void Bind(V3DiagnosticRecorder value)
        {
            recorder = value;
        }

        private void Awake()
        {
            if (recorder == null)
                recorder = GetComponent<V3DiagnosticRecorder>();
        }

        private void OnGUI()
        {
            if (!visible || recorder == null ||
                (!recorder.IsRecording && !recorder.IsExporting))
            {
                return;
            }
            EnsureStyles();
            Rect safe = Screen.safeArea;
            float width = Mathf.Min(panel.width, safe.width);
            float height = Mathf.Min(panel.height,
                Mathf.Max(0f, safe.height - topMargin));
            Rect anchoredPanel = new Rect(
                safe.center.x - width * 0.5f,
                safe.y + topMargin,
                width,
                height);
            GUILayout.BeginArea(anchoredPanel, boxStyle);
            GUILayout.Label(
                recorder.IsRecording
                    ? "REC  HOVERCRAFT V3 SESSION"
                    : "EXPORTING HOVERCRAFT V3 SESSION",
                titleStyle);
            V3DiagnosticSession session = recorder.CurrentSession;
            if (recorder.IsExporting)
            {
                GUILayout.Label(recorder.ExportStage + "  " +
                    (recorder.ExportProgress * 100f).ToString("0") + "%");
            }
            if (session != null)
            {
                V3DiagnosticSample latest = session.SampleCount > 0
                    ? session.Samples[session.SampleCount - 1] : default;
                GUILayout.Label(
                    "TIME " + recorder.Clock.Current.sessionElapsedSeconds.ToString("0.00") +
                    " s    SAMPLES " + session.SampleCount +
                    "    EVENTS " + session.EventCount +
                    "    DROPPED " + session.Manifest.droppedSampleCount);
                GUILayout.Label(
                    "TRACK " + latest.track.distanceAlongTrack.ToString("0.0") +
                    " m    " + (latest.track.sectionId ?? "UNMAPPED") +
                    "    WORLD " + latest.worldContactState +
                    "    BELIEF " +
                    (latest.belief.hoverGrounded ? "GROUNDED" : "NOT GROUNDED"));
            }
            GUILayout.Label("F7  STOP    F8  MARK    F9  EXPORT");
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (boxStyle != null) return;
            boxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(12, 12, 8, 8),
                normal =
                {
                    background = Texture2D.blackTexture,
                    textColor = Color.white
                }
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.35f, 0.95f, 1f) }
            };
        }
    }
}
