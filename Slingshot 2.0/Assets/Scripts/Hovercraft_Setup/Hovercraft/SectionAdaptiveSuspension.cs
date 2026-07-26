using System;
using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

/// <summary>
/// Adapts the hover suspension to the track section the craft is on, using the
/// same authoritative arc-length tracking the camera uses (<see cref="TrackSectionSensor"/>).
/// <para>
/// Instead of inferring track shape purely from physics signals, this reads
/// "the craft is on a Loop" / "a Corkscrew starts in 40 m" directly from the
/// generated section list and feeds tuning multipliers into
/// <see cref="HoverStabilizerArray"/>'s Context* properties: stiffness, damping,
/// alignment authority, and extra nose-up attitude per section type.
/// </para>
/// <para>
/// Loop/corkscrew tuning is ANTICIPATED via arc distance: it blends in before the
/// section starts (the suspension is already braced when the curvature hits) and
/// releases after it ends. All transitions are exponentially smoothed — the
/// suspension never snaps between profiles.
/// </para>
/// </summary>
[RequireComponent(typeof(HoverStabilizerArray))]
public class SectionAdaptiveSuspension : MonoBehaviour
{
    /// <summary>Tuning overrides for one macro section type. Values are multipliers (1 = no change).</summary>
    [Serializable]
    public class SectionTuning
    {
        [Tooltip("The macro section type this tuning applies to.")]
        public TrackMacroSectionType sectionType;

        [Tooltip("Suspension stiffness multiplier (springs, damping ceiling, alignment baseline).")]
        public float stiffnessMultiplier = 1f;

        [Tooltip("Hover damping multiplier on top of stiffness.")]
        public float dampingMultiplier = 1f;

        [Tooltip("Surface-alignment authority multiplier (spring + torque cap).")]
        public float alignmentMultiplier = 1f;

        [Tooltip("Extra nose-up attitude in degrees while on this section.")]
        public float extraNoseUpDegrees = 0f;
    }

    [Tooltip("Per-section-type suspension tuning. Section types not listed run at 1× (no change).")]
    public List<SectionTuning> sectionTunings = new List<SectionTuning>
    {
        // Loop/corkscrew damping stays at 1×: corner damping is what responds to the
        // facet noise of the ring mesh — raising it there amplifies the jiggle.
        // Attitude authority (alignment) and stiffness carry the load instead.
        new SectionTuning { sectionType = TrackMacroSectionType.Loop,         stiffnessMultiplier = 1.3f,  dampingMultiplier = 1f,    alignmentMultiplier = 1.5f,  extraNoseUpDegrees = 1.5f },
        new SectionTuning { sectionType = TrackMacroSectionType.Corkscrew,    stiffnessMultiplier = 1.2f,  dampingMultiplier = 1f,    alignmentMultiplier = 1.6f,  extraNoseUpDegrees = 1f },
        new SectionTuning { sectionType = TrackMacroSectionType.LandingRamp,  stiffnessMultiplier = 1.2f,  dampingMultiplier = 1.4f,  alignmentMultiplier = 1.2f },
        new SectionTuning { sectionType = TrackMacroSectionType.BankedHairpin, stiffnessMultiplier = 1.15f, dampingMultiplier = 1.1f,  alignmentMultiplier = 1.3f },
        new SectionTuning { sectionType = TrackMacroSectionType.HalfLoopTwist, stiffnessMultiplier = 1.3f,  dampingMultiplier = 1f,    alignmentMultiplier = 1.6f,  extraNoseUpDegrees = 1.5f },
        new SectionTuning { sectionType = TrackMacroSectionType.Spiral,        stiffnessMultiplier = 1.15f, dampingMultiplier = 1f,    alignmentMultiplier = 1.3f },
        // RotationalEvent is how the CURRENT generator emits all loops, corkscrews
        // and half-loops — without this entry, generated tracks get no tuning at all.
        new SectionTuning { sectionType = TrackMacroSectionType.RotationalEvent, stiffnessMultiplier = 1.3f, dampingMultiplier = 1f,   alignmentMultiplier = 1.55f, extraNoseUpDegrees = 1.5f },
    };

    [Header("Blending")]
    [Tooltip("Meters BEFORE a loop/corkscrew where its tuning starts blending in — the suspension is braced before the curvature arrives. Scale with your speeds: 420 m/s covers 100 m in 0.24 s.")]
    public float orientationBlendInDistance = 100f;

    [Tooltip("Meters AFTER a loop/corkscrew where its tuning blends back out.")]
    public float orientationBlendOutDistance = 50f;

    [Tooltip("Exponential response of the tuning blend (1/s). Higher = follows section changes faster.")]
    public float blendSpeed = 6f;

    /// <summary>The sensor used to locate the craft on the track (auto-added).</summary>
    public TrackSectionSensor Sensor { get; private set; }

    /// <summary>The blended multipliers currently applied (read-only, for HUD/debug).</summary>
    public float CurrentStiffness { get; private set; } = 1f;
    public float CurrentDamping { get; private set; } = 1f;
    public float CurrentAlignment { get; private set; } = 1f;
    public float CurrentNoseUp { get; private set; }

    private HoverStabilizerArray _hoverArray;
    private readonly Dictionary<TrackMacroSectionType, SectionTuning> _tuningByType =
        new Dictionary<TrackMacroSectionType, SectionTuning>();

    private void Awake()
    {
        _hoverArray = GetComponent<HoverStabilizerArray>();

        // The craft gets its own sensor instance (the camera has its own): same
        // logic, different target.
        Sensor = GetComponent<TrackSectionSensor>();
        if (Sensor == null) Sensor = gameObject.AddComponent<TrackSectionSensor>();
        Sensor.target = transform;

        RebuildLookup();
    }

    private void OnValidate()
    {
        RebuildLookup();
    }

    private void RebuildLookup()
    {
        _tuningByType.Clear();
        if (sectionTunings == null) return;

        foreach (SectionTuning tuning in sectionTunings)
        {
            if (tuning != null) _tuningByType[tuning.sectionType] = tuning;
        }

        // Scenes serialized before the generator switched to RotationalEvent carry
        // only the legacy Loop entry — alias it so generated tracks still tune.
        if (!_tuningByType.ContainsKey(TrackMacroSectionType.RotationalEvent) &&
            _tuningByType.TryGetValue(TrackMacroSectionType.Loop, out SectionTuning legacyLoop))
        {
            _tuningByType[TrackMacroSectionType.RotationalEvent] = legacyLoop;
        }
    }

    private void Update()
    {
        if (_hoverArray == null) return;

        // ── Target multipliers from the current section ──
        float stiffness = 1f, damping = 1f, alignment = 1f, noseUp = 0f;

        if (Sensor != null && Sensor.IsTracking)
        {
            // Base: the section the craft is on right now.
            TrackMacroSectionType? type = Sensor.CurrentSection?.Definition?.SectionType;
            if (type.HasValue && _tuningByType.TryGetValue(type.Value, out SectionTuning current))
            {
                stiffness = current.stiffnessMultiplier;
                damping = current.dampingMultiplier;
                alignment = current.alignmentMultiplier;
                noseUp = current.extraNoseUpDegrees;
            }

            // Anticipation: loop/corkscrew tuning ramps in BEFORE the section starts
            // and out after it ends, using authoritative arc distance. Take the max
            // so being between two orientation sections keeps the tuning up.
            float orientation01 = Sensor.GetOrientationWeight(orientationBlendInDistance, orientationBlendOutDistance);
            if (orientation01 > 0.001f)
            {
                // Generated tracks emit orientation sections as RotationalEvent;
                // fall back to the legacy Loop entry for hand-built content.
                SectionTuning loop =
                    _tuningByType.TryGetValue(TrackMacroSectionType.RotationalEvent, out SectionTuning rotational) ? rotational
                    : _tuningByType.TryGetValue(TrackMacroSectionType.Loop, out SectionTuning l) ? l : null;
                if (loop != null)
                {
                    stiffness = Mathf.Max(stiffness, Mathf.Lerp(1f, loop.stiffnessMultiplier, orientation01));
                    damping = Mathf.Max(damping, Mathf.Lerp(1f, loop.dampingMultiplier, orientation01));
                    alignment = Mathf.Max(alignment, Mathf.Lerp(1f, loop.alignmentMultiplier, orientation01));
                    noseUp = Mathf.Max(noseUp, Mathf.Lerp(0f, loop.extraNoseUpDegrees, orientation01));
                }
            }
        }

        // ── Smooth toward the targets and publish ──
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, blendSpeed) * Time.deltaTime);

        CurrentStiffness = Mathf.Lerp(CurrentStiffness, stiffness, t);
        CurrentDamping = Mathf.Lerp(CurrentDamping, damping, t);
        CurrentAlignment = Mathf.Lerp(CurrentAlignment, alignment, t);
        CurrentNoseUp = Mathf.Lerp(CurrentNoseUp, noseUp, t);

        _hoverArray.ContextStiffnessMultiplier = CurrentStiffness;
        _hoverArray.ContextDampingMultiplier = CurrentDamping;
        _hoverArray.ContextAlignmentMultiplier = CurrentAlignment;
        _hoverArray.ContextNoseUpDegrees = CurrentNoseUp;
    }

    private void OnDisable()
    {
        // Never leave stale context on the stabilizer.
        if (_hoverArray != null)
        {
            _hoverArray.ContextStiffnessMultiplier = 1f;
            _hoverArray.ContextDampingMultiplier = 1f;
            _hoverArray.ContextAlignmentMultiplier = 1f;
            _hoverArray.ContextNoseUpDegrees = 0f;
        }
    }
}
