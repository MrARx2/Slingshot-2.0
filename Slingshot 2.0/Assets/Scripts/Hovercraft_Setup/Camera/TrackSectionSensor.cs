using System.Collections.Generic;
using UnityEngine;
using TrackGeneration;
using TrackGeneration.Macro;

/// <summary>
/// Tracks which generated macro track section the craft is currently on, by
/// continuously following its arc-length position along the driving line.
///
/// This gives consumers (the camera, later maybe audio/VFX) AUTHORITATIVE section
/// knowledge — "the craft is 12 m into a Loop" — instead of inferring track shape
/// from realtime physics signals. Because sections are arc-length-stamped, it can
/// also anticipate: report an upcoming loop before the craft reaches it.
///
/// Position tracking uses a windowed nearest-frame search around the last known
/// arc position (a global search only on acquire/loss). The window is what makes
/// loops safe: the craft at the top of a loop is spatially near the road below,
/// but only frames near its own arc position are considered.
/// </summary>
public class TrackSectionSensor : MonoBehaviour
{
    [Tooltip("Craft transform to locate on the track. Auto-assigned by HovercraftCamera.")]
    public Transform target;

    [Tooltip("Max distance from the driving line before tracking is considered lost and re-acquired globally (meters). Must exceed road half-width + jump heights.")]
    public float reacquireDistance = 60f;

    [Tooltip("Arc-length window searched around the last known position each frame (meters). Must exceed per-frame travel at top speed (~420 m/s ≈ 7 m/frame) with a wide margin.")]
    public float searchWindow = 120f;

    /// <summary>True when a generated macro track has been found.</summary>
    public bool HasTrack { get; private set; }

    /// <summary>True when the craft's position on the track is currently locked in.</summary>
    public bool IsTracking { get; private set; }

    /// <summary>The section the craft is currently on (null while not tracking).</summary>
    public GeneratedTrackSection CurrentSection { get; private set; }

    /// <summary>Craft's arc-length position along the lap (meters).</summary>
    public float CurrentArcLength { get; private set; }

    /// <summary>Craft's distance from the driving line (meters).</summary>
    public float DistanceFromLine { get; private set; }

    private IReadOnlyList<GeneratedTrackSection> _sections;
    private Transform _trackRoot;
    private float _totalLength;
    private float _nextAcquireAttempt;

    void Update()
    {
        if (target == null)
            return;

        if (!EnsureTrack())
        {
            HasTrack = false;
            IsTracking = false;
            CurrentSection = null;
            return;
        }

        UpdatePosition();
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC QUERIES
    // ══════════════════════════════════════════════════════════════

    /// <summary>True when the current section is a Loop or Corkscrew.</summary>
    public bool IsOnOrientationSection
    {
        get
        {
            if (!IsTracking || CurrentSection?.Definition == null)
                return false;

            return IsOrientationType(CurrentSection.Definition.SectionType);
        }
    }

    /// <summary>
    /// 0..1 weight for "the craft is in loop/corkscrew territory", with arc-based
    /// anticipation: ramps 0→1 across <paramref name="blendInDistance"/> BEFORE an
    /// orientation section starts, holds 1 inside it, and ramps back to 0 across
    /// <paramref name="blendOutDistance"/> after it ends. Contiguous orientation
    /// sections merge naturally (the max of their spans).
    /// </summary>
    public float GetOrientationWeight(float blendInDistance, float blendOutDistance)
    {
        if (!IsTracking || _sections == null || _totalLength <= 0.01f)
            return 0f;

        float weight = 0f;

        for (int i = 0; i < _sections.Count && weight < 1f; i++)
        {
            GeneratedTrackSection section = _sections[i];

            if (section?.Definition == null || !IsOrientationType(section.Definition.SectionType))
                continue;

            weight = Mathf.Max(weight, SpanWeight(
                section.StartFrame.ArcLength,
                section.EndFrame.ArcLength,
                blendInDistance,
                blendOutDistance
            ));
        }

        return weight;
    }

    // ══════════════════════════════════════════════════════════════
    //  TRACK ACQUISITION
    // ══════════════════════════════════════════════════════════════

    private bool EnsureTrack()
    {
        if (_sections != null && _sections.Count > 0 && _trackRoot != null)
            return true;

        if (Time.unscaledTime < _nextAcquireAttempt)
            return false;

        _nextAcquireAttempt = Time.unscaledTime + 1f;

        // Primary: the runtime generator.
        TrackGenerator generator = FindAnyObjectByType<TrackGenerator>();
        if (generator != null && generator.CurrentMacroSections != null
            && generator.CurrentMacroSections.Count > 0 && generator.TrackRoot != null)
        {
            AdoptTrack(generator.CurrentMacroSections, generator.TrackRoot);
            return true;
        }

        // Fallback: an editor-generated track kept alive via its debug visualizer.
        MacroTrackDebugVisualizer visualizer = FindAnyObjectByType<MacroTrackDebugVisualizer>();
        if (visualizer != null && visualizer.Sections != null && visualizer.Sections.Count > 0)
        {
            AdoptTrack(visualizer.Sections, visualizer.transform);
            return true;
        }

        return false;
    }

    private void AdoptTrack(IReadOnlyList<GeneratedTrackSection> sections, Transform root)
    {
        _sections = sections;
        _trackRoot = root;
        _totalLength = sections[sections.Count - 1].EndFrame.ArcLength;
        HasTrack = true;
        IsTracking = false;
    }

    /// <summary>Drops the current track so it is re-acquired (call after regenerating).</summary>
    public void InvalidateTrack()
    {
        _sections = null;
        _trackRoot = null;
        HasTrack = false;
        IsTracking = false;
        CurrentSection = null;
        _nextAcquireAttempt = 0f;
    }

    // ══════════════════════════════════════════════════════════════
    //  POSITION TRACKING
    // ══════════════════════════════════════════════════════════════

    private void UpdatePosition()
    {
        Vector3 localPosition = _trackRoot.InverseTransformPoint(target.position);

        float bestDistanceSqr;
        float bestArc;

        if (IsTracking)
        {
            SearchNearestFrame(localPosition, CurrentArcLength, searchWindow, out bestArc, out bestDistanceSqr);

            // Lost (teleport/respawn/long jump off-line): fall back to global search.
            if (bestDistanceSqr > reacquireDistance * reacquireDistance)
                SearchNearestFrame(localPosition, 0f, float.PositiveInfinity, out bestArc, out bestDistanceSqr);
        }
        else
        {
            SearchNearestFrame(localPosition, 0f, float.PositiveInfinity, out bestArc, out bestDistanceSqr);
        }

        DistanceFromLine = Mathf.Sqrt(bestDistanceSqr);
        IsTracking = DistanceFromLine <= reacquireDistance;

        if (!IsTracking)
        {
            CurrentSection = null;
            return;
        }

        CurrentArcLength = bestArc;
        CurrentSection = FindSectionAt(bestArc);
    }

    /// <summary>
    /// Nearest driving-line point to <paramref name="localPosition"/>, considering
    /// only frames within <paramref name="window"/> meters of arc position
    /// <paramref name="aroundArc"/> (wrapping). Projects onto the segments between
    /// subdivision frames so the returned arc is continuous, not frame-quantized.
    /// </summary>
    private void SearchNearestFrame(Vector3 localPosition, float aroundArc, float window, out float bestArc, out float bestDistanceSqr)
    {
        bestArc = aroundArc;
        bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < _sections.Count; i++)
        {
            GeneratedTrackSection section = _sections[i];
            TrackConnectionFrame[] frames = section?.SubdivisionFrames;

            if (frames == null || frames.Length < 2)
                continue; // air gap

            if (!float.IsPositiveInfinity(window))
            {
                // Skip sections entirely outside the arc window (wrapped).
                float sectionStart = section.StartFrame.ArcLength;
                float sectionEnd = section.EndFrame.ArcLength;

                if (WrappedArcDistance(aroundArc, sectionStart) > window
                    && WrappedArcDistance(aroundArc, sectionEnd) > window
                    && !(WrappedForward(sectionStart, aroundArc) <= WrappedForward(sectionStart, sectionEnd)))
                {
                    continue;
                }
            }

            for (int f = 0; f < frames.Length - 1; f++)
            {
                Vector3 a = frames[f].Position;
                Vector3 b = frames[f + 1].Position;
                Vector3 ab = b - a;

                float abLengthSqr = ab.sqrMagnitude;
                float t = abLengthSqr > 0.0001f
                    ? Mathf.Clamp01(Vector3.Dot(localPosition - a, ab) / abLengthSqr)
                    : 0f;

                Vector3 closest = a + ab * t;
                float distanceSqr = (localPosition - closest).sqrMagnitude;

                if (distanceSqr >= bestDistanceSqr)
                    continue;

                float arc = Mathf.Lerp(frames[f].ArcLength, frames[f + 1].ArcLength, t);

                if (!float.IsPositiveInfinity(window) && WrappedArcDistance(aroundArc, arc) > window)
                    continue;

                bestDistanceSqr = distanceSqr;
                bestArc = arc;
            }
        }
    }

    private GeneratedTrackSection FindSectionAt(float arc)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            GeneratedTrackSection s = _sections[i];
            if (s == null) continue;

            if (arc >= s.StartFrame.ArcLength - 0.001f && arc <= s.EndFrame.ArcLength + 0.001f)
                return s;
        }

        return _sections[_sections.Count - 1];
    }

    // ══════════════════════════════════════════════════════════════
    //  ARC MATH
    // ══════════════════════════════════════════════════════════════

    /// <summary>Shortest wrapped distance between two arc positions.</summary>
    private float WrappedArcDistance(float a, float b)
    {
        float d = Mathf.Abs(Mathf.Repeat(b - a, _totalLength));
        return Mathf.Min(d, _totalLength - d);
    }

    /// <summary>Forward (driving-direction) wrapped distance from a to b.</summary>
    private float WrappedForward(float a, float b)
    {
        return Mathf.Repeat(b - a, _totalLength);
    }

    private float SpanWeight(float spanStart, float spanEnd, float blendIn, float blendOut)
    {
        float arc = CurrentArcLength;
        float spanLength = WrappedForward(spanStart, spanEnd);

        // Inside the span.
        if (WrappedForward(spanStart, arc) <= spanLength)
            return 1f;

        // Approaching: ramp up across blendIn before the span starts.
        float toStart = WrappedForward(arc, spanStart);
        if (blendIn > 0.01f && toStart <= blendIn)
            return 1f - toStart / blendIn;

        // Leaving: ramp down across blendOut after the span ends.
        float pastEnd = WrappedForward(spanEnd, arc);
        if (blendOut > 0.01f && pastEnd <= blendOut)
            return 1f - pastEnd / blendOut;

        return 0f;
    }

    private static bool IsOrientationType(TrackMacroSectionType type)
    {
        // RotationalEvent is how the current generator emits ALL loops, corkscrews
        // and half-loops (the legacy Loop/Corkscrew/HalfLoopTwist types survive only
        // for hand-built content and tests).
        return type == TrackMacroSectionType.RotationalEvent
            || type == TrackMacroSectionType.Loop
            || type == TrackMacroSectionType.Corkscrew
            || type == TrackMacroSectionType.HalfLoopTwist;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!IsTracking || _trackRoot == null || _sections == null || target == null)
            return;

        if (TrackGeneration.Race.MacroTrackSampler.TrySampleFrame(_sections, CurrentArcLength, out TrackConnectionFrame frame))
        {
            Vector3 world = _trackRoot.TransformPoint(frame.Position);

            Gizmos.color = IsOnOrientationSection ? Color.magenta : Color.cyan;
            Gizmos.DrawWireSphere(world, 1f);
            Gizmos.DrawLine(world, target.position);
            Gizmos.DrawRay(world, _trackRoot.TransformDirection(frame.Up) * 4f);
        }
    }
#endif
}
