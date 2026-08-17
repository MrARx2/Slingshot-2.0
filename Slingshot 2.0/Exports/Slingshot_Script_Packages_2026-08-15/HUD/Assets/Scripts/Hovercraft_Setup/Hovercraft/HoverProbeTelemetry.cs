using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// READ-ONLY diagnostic telemetry for the floor-to-wall / corkscrew launch investigation.
/// Add it to the craft root (the object with the Rigidbody + child <see cref="ThrusterNode"/>s).
///
/// It auto-starts recording on play and draws a small on-screen HUD so you can SEE it working.
/// Each FixedUpdate it READS the hover nodes' already-computed contact state
/// (<see cref="ThrusterNode.GroundNormal"/>, <see cref="ThrusterNode.IsGrounded"/>) and the craft
/// Rigidbody's velocities. It NEVER writes throttle, applies force, calls CastGroundRay, or
/// otherwise touches physics — it only observes. Press the key to dump a Console summary + reset.
///
/// The point: prove (or disprove) that a hover node's contact NORMAL snaps abruptly as it crosses
/// the coarse collider's floor→wall shoulder or rides a corkscrew wall — the felt wobble/launch.
/// </summary>
[DisallowMultipleComponent]
public class HoverProbeTelemetry : MonoBehaviour
{
    [Tooltip("Key to dump a Console summary and reset the window (new Input System).")]
    public Key dumpKey = Key.T;

    [Tooltip("Draw the full live diagnostic HUD. Off by default so instrumentation never pollutes the player HUD.")]
    public bool showHud = false;

    [Tooltip("Toggle the full diagnostic HUD without stopping capture.")]
    public Key toggleHudKey = Key.F3;

    [Tooltip("Per-tick node normal change (deg) above this counts as a snap event.")]
    public float normalSnapDeg = 10f;

    private Rigidbody _rb;
    private ThrusterNode[] _hoverNodes;
    private Vector3[] _lastNormal;
    private bool[] _hadContact;
    private float _lastUpSpeed;
    private float _startTime;

    private int _contactLossEvents, _snapEvents;
    private Vector3 _worstSnapPos; private float _worstSnapDeg; private string _worstSnapNode = "-";
    private float _worstLaunchWithSnapDeg, _worstLaunchMps;
    private float _liveNormalDelta, _liveDisagree, _liveRollRate; // last-tick values for the HUD
    private float _toastUntil;

    private readonly List<float> _maxNodeNormalDelta = new List<float>(1 << 15);
    private readonly List<float> _nodeDisagreement = new List<float>(1 << 15);
    private readonly List<float> _rollRateDeg = new List<float>(1 << 15);
    private readonly List<float> _upSpeedDelta = new List<float>(1 << 15);

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>();
        var all = GetComponentsInChildren<ThrusterNode>(includeInactive: true);
        var hover = new List<ThrusterNode>();
        foreach (var t in all) if (t.role == ThrusterNode.ThrusterRole.Hover) hover.Add(t);
        _hoverNodes = hover.ToArray();
        _lastNormal = new Vector3[_hoverNodes.Length];
        _hadContact = new bool[_hoverNodes.Length];
        for (int i = 0; i < _hoverNodes.Length; i++) _lastNormal[i] = Vector3.up;

        Debug.Log($"[HoverProbeTelemetry] ACTIVE on '{name}': hover nodes {_hoverNodes.Length}, " +
                  $"rigidbody {(_rb != null ? "found" : "MISSING")}. Recording now — press {dumpKey} to dump a summary.");
        if (_rb == null || _hoverNodes.Length == 0)
            Debug.LogWarning("[HoverProbeTelemetry] No Rigidbody or no HOVER-role ThrusterNodes found under this object — " +
                             "attach me to the craft ROOT (the Rigidbody owner).", this);
        ResetWindow();
    }

    private void ResetWindow()
    {
        _startTime = Time.time;
        _contactLossEvents = _snapEvents = 0;
        _worstSnapDeg = _worstLaunchMps = _worstLaunchWithSnapDeg = 0f; _worstSnapNode = "-";
        _maxNodeNormalDelta.Clear(); _nodeDisagreement.Clear(); _rollRateDeg.Clear(); _upSpeedDelta.Clear();
        _lastUpSpeed = _rb != null ? Vector3.Dot(_rb.linearVelocity, transform.up) : 0f;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[dumpKey].wasPressedThisFrame)
        {
            Report("KEY DUMP");
            ResetWindow();
            _toastUntil = Time.unscaledTime + 1.4f;
        }

        if (kb[toggleHudKey].wasPressedThisFrame)
            showHud = !showHud;
    }

    void FixedUpdate()
    {
        if (_rb == null || _hoverNodes.Length == 0) return;

        _liveRollRate = Mathf.Abs(Vector3.Dot(_rb.angularVelocity, transform.forward)) * Mathf.Rad2Deg;
        _rollRateDeg.Add(_liveRollRate);

        float upSpeed = Vector3.Dot(_rb.linearVelocity, transform.up);
        float upDelta = upSpeed - _lastUpSpeed;
        _lastUpSpeed = upSpeed;
        _upSpeedDelta.Add(upDelta);

        float maxDelta = 0f; string maxNode = "-";
        var grounded = new List<Vector3>(_hoverNodes.Length);
        for (int i = 0; i < _hoverNodes.Length; i++)
        {
            var n = _hoverNodes[i];
            bool g = n.IsGrounded;
            if (_hadContact[i] && !g) _contactLossEvents++;
            _hadContact[i] = g;
            if (!g) continue;

            Vector3 nrm = n.GroundNormal;
            grounded.Add(nrm);
            float d = Vector3.Angle(_lastNormal[i], nrm);
            _lastNormal[i] = nrm;
            if (d > maxDelta) { maxDelta = d; maxNode = string.IsNullOrEmpty(n.label) ? n.name : n.label; }
        }
        _maxNodeNormalDelta.Add(maxDelta);
        _liveNormalDelta = maxDelta;

        float disagree = 0f;
        for (int a = 0; a < grounded.Count; a++)
            for (int b = a + 1; b < grounded.Count; b++)
                disagree = Mathf.Max(disagree, Vector3.Angle(grounded[a], grounded[b]));
        _nodeDisagreement.Add(disagree);
        _liveDisagree = disagree;

        if (maxDelta > normalSnapDeg)
        {
            _snapEvents++;
            if (maxDelta > _worstSnapDeg) { _worstSnapDeg = maxDelta; _worstSnapPos = transform.position; _worstSnapNode = maxNode; }
            if (upDelta > _worstLaunchWithSnapDeg) _worstLaunchWithSnapDeg = upDelta;
        }
        if (upDelta > _worstLaunchMps) _worstLaunchMps = upDelta;
    }

    void OnGUI()
    {
        if (!showHud)
        {
            if (Time.unscaledTime < _toastUntil)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.Box(new Rect(18f, 18f, 220f, 34f), GUIContent.none);
                GUI.color = new Color(0.25f, 0.9f, 1f, 1f);
                GUI.Label(new Rect(30f, 25f, 200f, 22f), "TELEMETRY SAVED TO CONSOLE");
                GUI.color = Color.white;
            }
            return;
        }
        string s = _hoverNodes != null && _hoverNodes.Length > 0
            ? $"HoverProbe  nodes {_hoverNodes.Length}  ticks {_maxNodeNormalDelta.Count}\n" +
              $"normalΔ now {_liveNormalDelta:F1}°  worst {Max(_maxNodeNormalDelta):F1}°\n" +
              $"disagree now {_liveDisagree:F1}°  worst {Max(_nodeDisagreement):F1}°\n" +
              $"roll {_liveRollRate:F0}°/s  snaps {_snapEvents}  contactLoss {_contactLossEvents}\n" +
              $"[{dumpKey}] dump+reset  [{toggleHudKey}] hide"
            : "HoverProbe: NO hover nodes / no Rigidbody — attach to craft root";
        GUI.color = Color.black; GUI.Label(new Rect(11, 11, 520, 110), s);
        GUI.color = Color.green; GUI.Label(new Rect(10, 10, 520, 110), s);
    }

    void OnDisable() => Report("STOP");

    private void Report(string tag)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"──────── HoverProbe Telemetry [{tag}] (read-only) ────────");
        sb.AppendLine($"  window {Mathf.Max(0.01f, Time.time - _startTime):F1}s | hover nodes {_hoverNodes.Length} | ticks {_maxNodeNormalDelta.Count}");
        sb.AppendLine($"  node contact-normal change/tick:  max {Max(_maxNodeNormalDelta):F1}°  p95 {P95(_maxNodeNormalDelta):F1}°");
        sb.AppendLine($"  cross-node normal disagreement:   max {Max(_nodeDisagreement):F1}°  p95 {P95(_nodeDisagreement):F1}°");
        sb.AppendLine($"  craft roll rate:                  max {Max(_rollRateDeg):F1}°/s  p95 {P95(_rollRateDeg):F1}°/s");
        sb.AppendLine($"  upward speed change/tick:         max {Max(_upSpeedDelta):F2}m/s  p95 {P95(_upSpeedDelta):F2}m/s");
        sb.AppendLine($"  events: normal snaps > {normalSnapDeg:F0}°: {_snapEvents} | contact losses: {_contactLossEvents}");
        sb.AppendLine($"  worst snap: {_worstSnapDeg:F1}° on node '{_worstSnapNode}' at world {_worstSnapPos}");
        sb.AppendLine($"  worst launch impulse: {_worstLaunchMps:F2}m/s (coincident-with-snap: {_worstLaunchWithSnapDeg:F2}m/s)");
        sb.AppendLine("  READ-ONLY: no physics were influenced by this capture.");
        Debug.Log(sb.ToString());
    }

    private static float Max(List<float> v) { float m = 0f; if (v != null) foreach (var x in v) if (x > m) m = x; return m; }

    private static float P95(List<float> v)
    {
        if (v == null || v.Count == 0) return 0f;
        var s = new List<float>(v); s.Sort();
        return s[Mathf.Clamp(Mathf.CeilToInt(0.95f * s.Count) - 1, 0, s.Count - 1)];
    }
}
