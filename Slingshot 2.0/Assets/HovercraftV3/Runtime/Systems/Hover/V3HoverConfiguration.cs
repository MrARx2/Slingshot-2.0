using System;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Serialized per-build hover and surface-following configuration. Spring
    /// travel values are operational limits and are clamped to the installed
    /// connector hardware limits at assembly.
    /// </summary>
    [Serializable]
    public sealed class V3HoverConfiguration
    {
        [SerializeField] private string stableId = "hover.standard.v1";
        [Min(1), SerializeField] private int configurationVersion = 1;

        [Header("Ride Height and Sensing (m)")]
        [Min(0f), SerializeField] private float targetHoverHeight = 3.075f;
        [Min(0f), SerializeField] private float minimumClearance = 0.5f;
        [Min(0f), SerializeField] private float maximumOperationalRange = 7f;
        [Min(0f), SerializeField] private float probeRadius = 0.1f;
        [Min(0f), SerializeField] private float fallbackProbeRadius = 0.35f;

        [Header("Surface State and Capture Envelope (m)")]
        [Tooltip("Distance at which the craft is considered near the sensed surface. Sensing may continue farther away.")]
        [Min(0f), SerializeField] private float nearHoverRange = 12f;
        [Tooltip("Maximum distance at which automatic bottom/roof capture may retain authority.")]
        [Min(0f), SerializeField] private float captureRange = 28f;
        [Tooltip("Maximum range of the conservative chassis fallback cast.")]
        [Min(0f), SerializeField] private float fallbackRange = 32f;
        [Tooltip("Absolute-distance curve. Authority must reach zero at or before Capture Range.")]
        [SerializeField] private AnimationCurve captureAuthorityByDistance =
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(12f, 1f),
                new Keyframe(20f, 0.35f),
                new Keyframe(28f, 0f));
        [Range(0f, 1f), SerializeField] private float oneProbeAuthority = 0.2f;
        [Range(0f, 1f), SerializeField] private float twoProbeAuthority = 0.55f;
        [Range(0f, 1f), SerializeField] private float threeProbeAuthority = 0.8f;
        [Range(0f, 1f), SerializeField] private float fallbackOnlyAuthority = 0.15f;
        [Range(1f, 90f), SerializeField] private float normalConsensusAngleDegrees = 40f;
        [Min(0f), SerializeField] private float staleSurfaceTimeoutSeconds = 0.15f;

        [Header("Craft Automatic Authority (output multipliers)")]
        [Min(0f), SerializeField] private float automaticHoverAuthorityLimit = 7f;
        [Min(0f), SerializeField] private float automaticRoofAuthorityLimit = 7f;
        [Min(0f), SerializeField] private float manualAuthorityLimit = 1f;

        [Header("Hover Response")]
        [Min(0f), SerializeField] private float hoverStrength = 0.45f;
        [Min(0f), SerializeField] private float hoverDamping = 0.12f;
        [SerializeField] private bool gravityCompensationEnabled = true;
        [Min(0f), SerializeField] private float gravityCompensationMultiplier = 1f;

        [Header("Suspension Operational Limits (m)")]
        [Min(0f), SerializeField] private float compressionLimit = 0.3f;
        [Min(0f), SerializeField] private float extensionLimit = 0.12f;

        [Header("Physical Stabilizer Requests")]
        [Min(0f), SerializeField] private float angularDamping = 0.08f;
        [Min(0f), SerializeField] private float pitchSensitivity = 0.589f;
        [Min(0f), SerializeField] private float surfaceAlignStrength = 60f;
        [Min(0f), SerializeField] private float surfaceAlignDamping = 4f;
        [Min(0f), SerializeField] private float maximumSurfaceAlignAcceleration = 60f;
        [Min(0f), SerializeField] private float stiffenStartSpeed = 80f;
        [Min(0f), SerializeField] private float stiffenFullSpeed = 600f;
        [Min(1f), SerializeField] private float maximumSpeedStiffness = 3.5f;

        [Header("Physical Surface Following")]
        [Tooltip("Feed-forward gain applied to curvature inferred from consecutive physical surface-normal samples.")]
        [Min(0f), SerializeField] private float surfaceCurvatureFeedForward = 1.1f;
        [Tooltip("Response rate of the filtered curvature acceleration request.")]
        [Min(0f), SerializeField] private float surfaceTrackingResponse = 12f;
        [Tooltip("Maximum signed surface-curvature acceleration magnitude requested from physical bottom or roof thrusters.")]
        [Min(0f), SerializeField] private float maximumSurfaceTrackingAcceleration = 340f;
        [Tooltip("Distance error gain used to request physical roof-thruster force toward the sensed surface.")]
        [Min(0f), SerializeField] private float roofCaptureStrength = 7f;
        [Tooltip("Surface-normal separation-velocity damping requested from the physical roof thrusters.")]
        [Min(0f), SerializeField] private float roofCaptureDamping = 5f;
        [Tooltip("Maximum acceleration toward the sensed surface requested from the roof thrusters.")]
        [Min(0f), SerializeField] private float maximumRoofCaptureAcceleration = 180f;
        [Tooltip("Height error around the configured target in which automatic roof capture remains idle.")]
        [Min(0f), SerializeField] private float roofCaptureDeadband = 0.35f;
        [SerializeField] private LayerMask trackSurfaceMask = 1 << 8;

        public string StableId => string.IsNullOrWhiteSpace(stableId)
            ? "hover.unnamed" : stableId;
        public int ConfigurationVersion => Mathf.Max(1, configurationVersion);
        public float MinimumClearance => Mathf.Max(0f, minimumClearance);
        public float TargetHoverHeight => Mathf.Max(
            MinimumClearance,
            targetHoverHeight);
        public float MaximumOperationalRange => Mathf.Max(
            TargetHoverHeight,
            maximumOperationalRange);
        public float ProbeRadius => Mathf.Max(0f, probeRadius);
        public float FallbackProbeRadius => Mathf.Max(0f, fallbackProbeRadius);
        public float NearHoverRange => Mathf.Clamp(
            nearHoverRange,
            TargetHoverHeight,
            MaximumOperationalRange);
        public float CaptureRange => Mathf.Clamp(
            captureRange,
            NearHoverRange,
            MaximumOperationalRange);
        public float FallbackRange => Mathf.Clamp(
            fallbackRange,
            TargetHoverHeight,
            MaximumOperationalRange);
        public float OneProbeAuthority => Mathf.Clamp01(oneProbeAuthority);
        public float TwoProbeAuthority => Mathf.Clamp01(twoProbeAuthority);
        public float ThreeProbeAuthority => Mathf.Clamp01(threeProbeAuthority);
        public float FallbackOnlyAuthority => Mathf.Clamp01(fallbackOnlyAuthority);
        public float NormalConsensusAngleDegrees => Mathf.Clamp(
            normalConsensusAngleDegrees, 1f, 90f);
        public float StaleSurfaceTimeoutSeconds => Mathf.Max(
            0f, staleSurfaceTimeoutSeconds);
        public float AutomaticHoverAuthorityLimit => Mathf.Max(
            0f, automaticHoverAuthorityLimit);
        public float AutomaticRoofAuthorityLimit => Mathf.Max(
            0f, automaticRoofAuthorityLimit);
        public float ManualAuthorityLimit => Mathf.Max(0f, manualAuthorityLimit);
        public float HoverStrength => Mathf.Max(0f, hoverStrength);
        public float HoverDamping => Mathf.Max(0f, hoverDamping);
        public bool GravityCompensationEnabled => gravityCompensationEnabled;
        public float GravityCompensationMultiplier => Mathf.Max(
            0f,
            gravityCompensationMultiplier);
        public float CompressionLimit => Mathf.Max(0f, compressionLimit);
        public float ExtensionLimit => Mathf.Max(0f, extensionLimit);
        public float AngularDamping => Mathf.Max(0f, angularDamping);
        public float PitchSensitivity => Mathf.Max(0f, pitchSensitivity);
        public float SurfaceAlignStrength => Mathf.Max(0f, surfaceAlignStrength);
        public float SurfaceAlignDamping => Mathf.Max(0f, surfaceAlignDamping);
        public float MaximumSurfaceAlignAcceleration => Mathf.Max(
            0f,
            maximumSurfaceAlignAcceleration);
        public float StiffenStartSpeed => Mathf.Max(0f, stiffenStartSpeed);
        public float StiffenFullSpeed => Mathf.Max(
            StiffenStartSpeed + 1f,
            stiffenFullSpeed);
        public float MaximumSpeedStiffness => Mathf.Max(
            1f,
            maximumSpeedStiffness);
        public float SurfaceCurvatureFeedForward => Mathf.Max(
            0f,
            surfaceCurvatureFeedForward);
        public float SurfaceTrackingResponse => Mathf.Max(
            0f,
            surfaceTrackingResponse);
        public float MaximumSurfaceTrackingAcceleration => Mathf.Max(
            0f,
            maximumSurfaceTrackingAcceleration);
        public float RoofCaptureStrength => Mathf.Max(0f, roofCaptureStrength);
        public float RoofCaptureDamping => Mathf.Max(0f, roofCaptureDamping);
        public float MaximumRoofCaptureAcceleration => Mathf.Max(
            0f,
            maximumRoofCaptureAcceleration);
        public float RoofCaptureDeadband => Mathf.Max(0f, roofCaptureDeadband);
        public LayerMask TrackSurfaceMask => trackSurfaceMask;

        public float EvaluateCaptureAuthority(float distanceMeters)
        {
            if (distanceMeters < 0f || distanceMeters > CaptureRange)
            {
                return 0f;
            }
            if (captureAuthorityByDistance == null ||
                captureAuthorityByDistance.length == 0)
            {
                return Mathf.InverseLerp(CaptureRange, NearHoverRange, distanceMeters);
            }
            return Mathf.Clamp01(captureAuthorityByDistance.Evaluate(distanceMeters));
        }

        public float EvaluateProbeConsensusAuthority(
            int consistentProbeCount,
            int primaryProbeCount)
        {
            if (consistentProbeCount <= 0) return 0f;
            if (primaryProbeCount <= 0) return FallbackOnlyAuthority;
            return consistentProbeCount switch
            {
                1 => OneProbeAuthority,
                2 => TwoProbeAuthority,
                3 => ThreeProbeAuthority,
                _ => 1f
            };
        }

        public V3HoverConfiguration Clone()
        {
            var copy = new V3HoverConfiguration();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(V3HoverConfiguration source)
        {
            if (source == null)
            {
                return;
            }

            stableId = source.stableId;
            configurationVersion = source.configurationVersion;
            targetHoverHeight = source.targetHoverHeight;
            minimumClearance = source.minimumClearance;
            maximumOperationalRange = source.maximumOperationalRange;
            probeRadius = source.probeRadius;
            fallbackProbeRadius = source.fallbackProbeRadius;
            nearHoverRange = source.nearHoverRange;
            captureRange = source.captureRange;
            fallbackRange = source.fallbackRange;
            captureAuthorityByDistance = source.captureAuthorityByDistance != null
                ? new AnimationCurve(source.captureAuthorityByDistance.keys)
                : new AnimationCurve();
            oneProbeAuthority = source.oneProbeAuthority;
            twoProbeAuthority = source.twoProbeAuthority;
            threeProbeAuthority = source.threeProbeAuthority;
            fallbackOnlyAuthority = source.fallbackOnlyAuthority;
            normalConsensusAngleDegrees = source.normalConsensusAngleDegrees;
            staleSurfaceTimeoutSeconds = source.staleSurfaceTimeoutSeconds;
            automaticHoverAuthorityLimit = source.automaticHoverAuthorityLimit;
            automaticRoofAuthorityLimit = source.automaticRoofAuthorityLimit;
            manualAuthorityLimit = source.manualAuthorityLimit;
            hoverStrength = source.hoverStrength;
            hoverDamping = source.hoverDamping;
            gravityCompensationEnabled = source.gravityCompensationEnabled;
            gravityCompensationMultiplier =
                source.gravityCompensationMultiplier;
            compressionLimit = source.compressionLimit;
            extensionLimit = source.extensionLimit;
            angularDamping = source.angularDamping;
            pitchSensitivity = source.pitchSensitivity;
            surfaceAlignStrength = source.surfaceAlignStrength;
            surfaceAlignDamping = source.surfaceAlignDamping;
            maximumSurfaceAlignAcceleration =
                source.maximumSurfaceAlignAcceleration;
            stiffenStartSpeed = source.stiffenStartSpeed;
            stiffenFullSpeed = source.stiffenFullSpeed;
            maximumSpeedStiffness = source.maximumSpeedStiffness;
            surfaceCurvatureFeedForward =
                source.surfaceCurvatureFeedForward;
            surfaceTrackingResponse = source.surfaceTrackingResponse;
            maximumSurfaceTrackingAcceleration =
                source.maximumSurfaceTrackingAcceleration;
            roofCaptureStrength = source.roofCaptureStrength;
            roofCaptureDamping = source.roofCaptureDamping;
            maximumRoofCaptureAcceleration =
                source.maximumRoofCaptureAcceleration;
            roofCaptureDeadband = source.roofCaptureDeadband;
            trackSurfaceMask = source.trackSurfaceMask;
        }

        public void Validate()
        {
            configurationVersion = Mathf.Max(1, configurationVersion);
            minimumClearance = Mathf.Max(0f, minimumClearance);
            targetHoverHeight = Mathf.Max(minimumClearance, targetHoverHeight);
            maximumOperationalRange = Mathf.Max(
                targetHoverHeight,
                maximumOperationalRange);
            probeRadius = Mathf.Max(0f, probeRadius);
            fallbackProbeRadius = Mathf.Max(0f, fallbackProbeRadius);
            nearHoverRange = Mathf.Clamp(
                nearHoverRange, targetHoverHeight, maximumOperationalRange);
            captureRange = Mathf.Clamp(
                captureRange, nearHoverRange, maximumOperationalRange);
            fallbackRange = Mathf.Clamp(
                fallbackRange, targetHoverHeight, maximumOperationalRange);
            oneProbeAuthority = Mathf.Clamp01(oneProbeAuthority);
            twoProbeAuthority = Mathf.Clamp01(twoProbeAuthority);
            threeProbeAuthority = Mathf.Clamp01(threeProbeAuthority);
            fallbackOnlyAuthority = Mathf.Clamp01(fallbackOnlyAuthority);
            normalConsensusAngleDegrees = Mathf.Clamp(
                normalConsensusAngleDegrees, 1f, 90f);
            staleSurfaceTimeoutSeconds = Mathf.Max(
                0f, staleSurfaceTimeoutSeconds);
            automaticHoverAuthorityLimit = Mathf.Max(
                0f, automaticHoverAuthorityLimit);
            automaticRoofAuthorityLimit = Mathf.Max(
                0f, automaticRoofAuthorityLimit);
            manualAuthorityLimit = Mathf.Max(0f, manualAuthorityLimit);
            hoverStrength = Mathf.Max(0f, hoverStrength);
            hoverDamping = Mathf.Max(0f, hoverDamping);
            gravityCompensationMultiplier = Mathf.Max(
                0f,
                gravityCompensationMultiplier);
            compressionLimit = Mathf.Max(0f, compressionLimit);
            extensionLimit = Mathf.Max(0f, extensionLimit);
            angularDamping = Mathf.Max(0f, angularDamping);
            pitchSensitivity = Mathf.Max(0f, pitchSensitivity);
            surfaceAlignStrength = Mathf.Max(0f, surfaceAlignStrength);
            surfaceAlignDamping = Mathf.Max(0f, surfaceAlignDamping);
            maximumSurfaceAlignAcceleration = Mathf.Max(
                0f,
                maximumSurfaceAlignAcceleration);
            stiffenStartSpeed = Mathf.Max(0f, stiffenStartSpeed);
            stiffenFullSpeed = Mathf.Max(
                stiffenStartSpeed + 1f,
                stiffenFullSpeed);
            maximumSpeedStiffness = Mathf.Max(1f, maximumSpeedStiffness);
            surfaceCurvatureFeedForward = Mathf.Max(
                0f,
                surfaceCurvatureFeedForward);
            surfaceTrackingResponse = Mathf.Max(0f, surfaceTrackingResponse);
            maximumSurfaceTrackingAcceleration = Mathf.Max(
                0f,
                maximumSurfaceTrackingAcceleration);
            roofCaptureStrength = Mathf.Max(0f, roofCaptureStrength);
            roofCaptureDamping = Mathf.Max(0f, roofCaptureDamping);
            maximumRoofCaptureAcceleration = Mathf.Max(
                0f,
                maximumRoofCaptureAcceleration);
            roofCaptureDeadband = Mathf.Max(0f, roofCaptureDeadband);
        }
    }
}
