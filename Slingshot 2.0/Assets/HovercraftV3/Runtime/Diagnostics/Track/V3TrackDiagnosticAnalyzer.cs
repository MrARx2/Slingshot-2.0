using System;
using System.Collections.Generic;
using TrackGeneration.Macro;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [Serializable]
    public sealed class V3TrackSectionDiagnosticSnapshot
    {
        public string sectionId;
        public int sectionIndex;
        public string sectionType;
        public string debugName;
        public string patternId;
        public int quarterIndex;
        public int roadId;
        public float startDistance;
        public float endDistance;
        public float length;
        public float startWidth;
        public float endWidth;
        public bool openStart;
        public bool openEnd;
        public bool airGap;
        public bool allowsJump;
        public int centerlineSampleCount;
        public Bounds worldBounds;
    }

    [Serializable]
    public sealed class V3TrackCenterlineDiagnosticPoint
    {
        public string sectionId;
        public float distance;
        public float lapProgress;
        public Vector3 position;
        public Vector3 tangent;
        public Vector3 right;
        public Vector3 normal;
        public float width;
        public float bankDegrees;
        public float slopeDegrees;
        public float horizontalCurvature;
        public float verticalCurvature;
        public float torsionDegreesPerMeter;
    }

    [Serializable]
    public sealed class V3TrackColliderAuditSnapshot
    {
        public int colliderCount;
        public int enabledColliderCount;
        public int triggerCount;
        public int distinctLayerCount;
        public string[] layers;
        public Bounds combinedWorldBounds;
    }

    [Serializable]
    public sealed class V3TrackDiagnosticSnapshot
    {
        public int schemaVersion;
        public int trackEntityId;
        public int generationRevision;
        public string trackRootName;
        public float totalLengthMeters;
        public float centerlineSampleIntervalMeters;
        public int sectionCount;
        public int centerlineSampleCount;
        public V3TrackSectionDiagnosticSnapshot[] sections;
        public V3TrackCenterlineDiagnosticPoint[] centerline;
        public V3TrackColliderAuditSnapshot colliderAudit;
        public Vector3 gravity;
        public string dataQuality;
        public string notes;
    }

    internal struct V3TrackDiagnosticSegment
    {
        public TrackConnectionFrame a;
        public TrackConnectionFrame b;
        public GeneratedTrackSection section;
        public string sectionId;
        public string sectionType;
    }

    public sealed class V3TrackDiagnosticAnalyzer : IV3DiagnosticSource
    {
        private const int LocalSearchRadius = 96;
        private const int CoarseStride = 32;
        private readonly RaycastHit[] raycastHits = new RaycastHit[32];
        private V3DiagnosticContext context;
        private V3TrackDiagnosticSegment[] segments =
            Array.Empty<V3TrackDiagnosticSegment>();
        private Transform trackRoot;
        private int lastSegmentIndex = -1;
        private float previousLapProgress;
        private int lap;
        private readonly float centerlineSampleIntervalMeters;

        public string SourceId => "track";
        public int SchemaVersion => V3DiagnosticSchema.Version;
        public V3TrackDiagnosticSnapshot Snapshot { get; private set; }

        public V3TrackDiagnosticAnalyzer(
            float centerlineSampleIntervalMeters = 1f)
        {
            this.centerlineSampleIntervalMeters = Mathf.Max(
                0.1f, centerlineSampleIntervalMeters);
        }

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
        }

        public void OnSessionStarted(V3DiagnosticSession session)
        {
            BuildStaticSnapshot();
            session.SetTrackSnapshot(Snapshot);
            if (segments.Length == 0)
            {
                session.AddDataWarning(
                    "Track analyzer has no generated centerline segments; track-relative channels are unavailable.");
            }
            lastSegmentIndex = -1;
            previousLapProgress = 0f;
            lap = 0;
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            if (segments.Length == 0 || context == null || context.Body == null)
            {
                sample.track.mappingConfidence = 0f;
                return;
            }
            Vector3 position = context.Body.worldCenterOfMass;
            int segmentIndex = FindNearestSegment(position, out float interpolation,
                out Vector3 nearest, out float squaredDistance);
            if (segmentIndex < 0)
            {
                sample.track.mappingConfidence = 0f;
                return;
            }

            lastSegmentIndex = segmentIndex;
            V3TrackDiagnosticSegment segment = segments[segmentIndex];
            TrackConnectionFrame frame = Interpolate(segment.a, segment.b, interpolation);
            Vector3 tangent = DirectionToWorld(frame.Forward, Vector3.forward);
            Vector3 normal = DirectionToWorld(frame.Up, Vector3.up);
            Vector3 right = DirectionToWorld(frame.Right, Vector3.right);
            Vector3 offset = position - nearest;
            float lateral = Vector3.Dot(offset, right);
            float vertical = Vector3.Dot(offset, normal);
            float halfWidth = Mathf.Max(0.01f, frame.Width * 0.5f);
            float distanceFromCenter = Mathf.Sqrt(Mathf.Max(0f, squaredDistance));
            float confidence = Mathf.Clamp01(1f -
                Mathf.Max(0f, distanceFromCenter - halfWidth) /
                Mathf.Max(halfWidth * 3f, 1f));
            bool reverse = Vector3.Dot(context.Body.linearVelocity, tangent) < -0.1f;
            if (frame.LapProgress + 0.5f < previousLapProgress && !reverse)
            {
                lap++;
            }
            previousLapProgress = frame.LapProgress;
            float trueDistance = Mathf.Abs(vertical);
            Vector3 truePoint = nearest + right * lateral;
            Vector3 trueNormal = normal;
            int trueColliderId = 0;
            if (TryProbeTrackSurface(position, -normal, out RaycastHit hit))
            {
                trueDistance = hit.distance;
                truePoint = hit.point;
                trueNormal = hit.normal;
                trueColliderId = hit.collider != null
                    ? hit.collider.GetEntityId().GetHashCode() : 0;
            }

            sample.track.mapped = true;
            sample.track.mappingConfidence = confidence;
            sample.track.distanceAlongTrack = frame.ArcLength;
            sample.track.sectionProgress = Mathf.InverseLerp(
                segment.section.StartFrame.ArcLength,
                segment.section.EndFrame.ArcLength,
                frame.ArcLength);
            sample.track.lap = lap;
            sample.track.reverseTravel = reverse;
            sample.track.sectionId = segment.sectionId;
            sample.track.sectionType = segment.sectionType;
            sample.track.nearestCenterlinePoint = nearest;
            sample.track.tangent = tangent;
            sample.track.normal = normal;
            sample.track.right = right;
            sample.track.signedLateralOffset = lateral;
            sample.track.signedVerticalOffset = vertical;
            sample.track.trueSurfaceDistance = trueDistance;
            sample.track.trueSurfacePoint = truePoint;
            sample.track.trueSurfaceNormal = trueNormal;
            sample.track.trueSurfaceColliderId = trueColliderId;
            sample.track.bankDegrees = frame.BankAngle;
            sample.track.slopeDegrees = frame.PitchAngle;
            sample.track.horizontalCurvature = frame.HorizontalCurvature;
            sample.track.verticalCurvature = frame.VerticalCurvature;
            sample.track.torsionDegreesPerMeter = frame.RoadRollRate;
            sample.track.insideTrackBounds = Mathf.Abs(lateral) <= halfWidth;
            sample.track.expectedAirborne = segment.section.IsEmptySpace ||
                segment.section.OpenStart || segment.section.OpenEnd ||
                (segment.section.Definition != null &&
                 segment.section.Definition.AllowsJump);
            sample.identity.trackDistance = frame.ArcLength;
            sample.identity.sectionId = segment.sectionId;
        }

        public void OnSessionEnded(V3DiagnosticSession session)
        {
        }

        private void BuildStaticSnapshot()
        {
            TrackGeneration.TrackGenerator generator =
                context != null ? context.TrackGenerator : null;
            trackRoot = generator != null && generator.TrackRoot != null
                ? generator.TrackRoot
                : generator != null ? generator.transform : null;
            IReadOnlyList<GeneratedTrackSection> sections =
                generator != null ? generator.CurrentMacroSections : null;
            if ((sections == null || sections.Count == 0) && trackRoot != null)
            {
                MacroTrackDebugVisualizer visualizer =
                    trackRoot.GetComponent<MacroTrackDebugVisualizer>();
                if (visualizer != null)
                {
                    sections = visualizer.Sections;
                }
            }
            if (sections == null)
            {
                sections = Array.Empty<GeneratedTrackSection>();
            }

            var segmentList = new List<V3TrackDiagnosticSegment>(4096);
            var pointList = new List<V3TrackCenterlineDiagnosticPoint>(4096);
            var sectionSnapshots =
                new V3TrackSectionDiagnosticSnapshot[sections.Count];
            for (int i = 0; i < sections.Count; i++)
            {
                GeneratedTrackSection section = sections[i];
                if (section == null)
                {
                    continue;
                }
                TrackConnectionFrame[] frames = section.SubdivisionFrames;
                if (frames == null || frames.Length < 2)
                {
                    frames = new[] { section.StartFrame, section.EndFrame };
                }
                string sectionId = SectionId(section);
                string sectionType = section.Definition != null
                    ? section.Definition.SectionType.ToString() : "Unknown";
                float lastSampleArc = float.NegativeInfinity;
                for (int f = 0; f < frames.Length; f++)
                {
                    TrackConnectionFrame frame = frames[f];
                    bool endpoint = f == 0 || f == frames.Length - 1;
                    if (endpoint || frame.ArcLength - lastSampleArc >=
                        centerlineSampleIntervalMeters)
                    {
                        pointList.Add(ToPoint(sectionId, frame));
                        lastSampleArc = frame.ArcLength;
                    }
                    if (f > 0)
                    {
                        segmentList.Add(new V3TrackDiagnosticSegment
                        {
                            a = frames[f - 1],
                            b = frame,
                            section = section,
                            sectionId = sectionId,
                            sectionType = sectionType
                        });
                    }
                }
                Bounds worldBounds = ToWorldBounds(section.SectionBounds);
                sectionSnapshots[i] = new V3TrackSectionDiagnosticSnapshot
                {
                    sectionId = sectionId,
                    sectionIndex = section.SectionIndex,
                    sectionType = sectionType,
                    debugName = section.Definition != null
                        ? section.Definition.DebugName : string.Empty,
                    patternId = section.PatternId ?? string.Empty,
                    quarterIndex = section.QuarterIndex,
                    roadId = section.RoadId,
                    startDistance = section.StartFrame.ArcLength,
                    endDistance = section.EndFrame.ArcLength,
                    length = Mathf.Max(0f,
                        section.EndFrame.ArcLength - section.StartFrame.ArcLength),
                    startWidth = section.StartFrame.Width,
                    endWidth = section.EndFrame.Width,
                    openStart = section.OpenStart,
                    openEnd = section.OpenEnd,
                    airGap = section.IsEmptySpace,
                    allowsJump = section.Definition != null &&
                        section.Definition.AllowsJump,
                    centerlineSampleCount = CountSectionPoints(
                        pointList, sectionId),
                    worldBounds = worldBounds
                };
            }
            segments = segmentList.ToArray();
            V3TrackCenterlineDiagnosticPoint[] points = pointList.ToArray();
            Vector3 diagnosticGravity = Physics.gravity;
            if (context != null && context.Craft != null &&
                V3WorldQueryService.Resolve(
                    context.Craft.gameObject.scene,
                    out V3WorldSimulationRoot worldRoot) ==
                V3WorldQueryStatus.Valid)
            {
                diagnosticGravity = worldRoot.Profile.GravityVector;
            }
            Snapshot = new V3TrackDiagnosticSnapshot
            {
                schemaVersion = V3DiagnosticSchema.Version,
                trackEntityId = generator != null
                    ? generator.GetEntityId().GetHashCode() : 0,
                generationRevision = generator != null
                    ? generator.GenerationRevision : 0,
                trackRootName = trackRoot != null ? trackRoot.name : string.Empty,
                totalLengthMeters = sections.Count > 0 && sections[sections.Count - 1] != null
                    ? sections[sections.Count - 1].EndFrame.ArcLength : 0f,
                centerlineSampleIntervalMeters =
                    centerlineSampleIntervalMeters,
                sectionCount = sections.Count,
                centerlineSampleCount = points.Length,
                sections = sectionSnapshots,
                centerline = points,
                colliderAudit = CaptureColliderAudit(),
                gravity = diagnosticGravity,
                dataQuality = segments.Length > 0 ? "High" : "Unavailable",
                notes = "Centerline and section data are read from the generated track; surface distance is independently probed against track colliders."
            };
        }

        private int FindNearestSegment(
            Vector3 worldPosition,
            out float nearestT,
            out Vector3 nearestPoint,
            out float nearestSquaredDistance)
        {
            int best = -1;
            nearestT = 0f;
            nearestPoint = Vector3.zero;
            nearestSquaredDistance = float.PositiveInfinity;
            if (lastSegmentIndex >= 0)
            {
                SearchRange(worldPosition,
                    Mathf.Max(0, lastSegmentIndex - LocalSearchRadius),
                    Mathf.Min(segments.Length, lastSegmentIndex + LocalSearchRadius + 1),
                    1, ref best, ref nearestT, ref nearestPoint,
                    ref nearestSquaredDistance);
                if (nearestSquaredDistance < 400f)
                {
                    return best;
                }
            }
            int coarseBest = -1;
            float ignoredT = 0f;
            Vector3 ignoredPoint = Vector3.zero;
            float coarseDistance = float.PositiveInfinity;
            SearchRange(worldPosition, 0, segments.Length, CoarseStride,
                ref coarseBest, ref ignoredT, ref ignoredPoint, ref coarseDistance);
            if (coarseBest >= 0)
            {
                SearchRange(worldPosition,
                    Mathf.Max(0, coarseBest - CoarseStride * 2),
                    Mathf.Min(segments.Length, coarseBest + CoarseStride * 2 + 1),
                    1, ref best, ref nearestT, ref nearestPoint,
                    ref nearestSquaredDistance);
            }
            return best;
        }

        private void SearchRange(
            Vector3 worldPosition, int start, int end, int stride,
            ref int best, ref float bestT, ref Vector3 bestPoint,
            ref float bestSquaredDistance)
        {
            for (int i = start; i < end; i += stride)
            {
                Vector3 a = PointToWorld(segments[i].a.Position);
                Vector3 b = PointToWorld(segments[i].b.Position);
                Vector3 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator > 0.000001f
                    ? Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / denominator)
                    : 0f;
                Vector3 point = a + ab * t;
                float squared = (worldPosition - point).sqrMagnitude;
                if (squared < bestSquaredDistance)
                {
                    bestSquaredDistance = squared;
                    best = i;
                    bestT = t;
                    bestPoint = point;
                }
            }
        }

        private bool TryProbeTrackSurface(
            Vector3 origin, Vector3 direction, out RaycastHit closest)
        {
            closest = default;
            if (trackRoot == null)
            {
                return false;
            }
            int count = Physics.RaycastNonAlloc(origin, direction, raycastHits,
                250f, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = raycastHits[i].collider;
                if (collider != null && collider.transform.IsChildOf(trackRoot) &&
                    raycastHits[i].distance < bestDistance)
                {
                    closest = raycastHits[i];
                    bestDistance = closest.distance;
                    found = true;
                }
            }
            return found;
        }

        private V3TrackCenterlineDiagnosticPoint ToPoint(
            string sectionId, TrackConnectionFrame frame)
        {
            return new V3TrackCenterlineDiagnosticPoint
            {
                sectionId = sectionId,
                distance = frame.ArcLength,
                lapProgress = frame.LapProgress,
                position = PointToWorld(frame.Position),
                tangent = DirectionToWorld(frame.Forward, Vector3.forward),
                right = DirectionToWorld(frame.Right, Vector3.right),
                normal = DirectionToWorld(frame.Up, Vector3.up),
                width = frame.Width,
                bankDegrees = frame.BankAngle,
                slopeDegrees = frame.PitchAngle,
                horizontalCurvature = frame.HorizontalCurvature,
                verticalCurvature = frame.VerticalCurvature,
                torsionDegreesPerMeter = frame.RoadRollRate
            };
        }

        private V3TrackColliderAuditSnapshot CaptureColliderAudit()
        {
            Collider[] colliders = trackRoot != null
                ? trackRoot.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
            var layerNames = new List<string>(8);
            int enabled = 0;
            int triggers = 0;
            Bounds combined = default;
            bool hasBounds = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                if (collider.enabled) enabled++;
                if (collider.isTrigger) triggers++;
                string layer = LayerMask.LayerToName(collider.gameObject.layer);
                if (string.IsNullOrEmpty(layer))
                {
                    layer = collider.gameObject.layer.ToString();
                }
                if (!layerNames.Contains(layer)) layerNames.Add(layer);
                if (!hasBounds) { combined = collider.bounds; hasBounds = true; }
                else combined.Encapsulate(collider.bounds);
            }
            return new V3TrackColliderAuditSnapshot
            {
                colliderCount = colliders.Length,
                enabledColliderCount = enabled,
                triggerCount = triggers,
                distinctLayerCount = layerNames.Count,
                layers = layerNames.ToArray(),
                combinedWorldBounds = combined
            };
        }

        private Bounds ToWorldBounds(Bounds local)
        {
            if (trackRoot == null) return local;
            Vector3 center = trackRoot.TransformPoint(local.center);
            Vector3 extents = local.extents;
            Vector3 axisX = trackRoot.TransformVector(extents.x, 0f, 0f);
            Vector3 axisY = trackRoot.TransformVector(0f, extents.y, 0f);
            Vector3 axisZ = trackRoot.TransformVector(0f, 0f, extents.z);
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        private Vector3 PointToWorld(Vector3 point)
        {
            return trackRoot != null ? trackRoot.TransformPoint(point) : point;
        }

        private Vector3 DirectionToWorld(Vector3 direction, Vector3 fallback)
        {
            Vector3 value = trackRoot != null
                ? trackRoot.TransformDirection(direction) : direction;
            return value.sqrMagnitude > 0.000001f ? value.normalized : fallback;
        }

        private static string SectionId(GeneratedTrackSection section)
        {
            return section == null
                ? string.Empty
                : "section_" + section.SectionIndex.ToString("000") +
                  "_road_" + section.RoadId;
        }

        private static int CountSectionPoints(
            List<V3TrackCenterlineDiagnosticPoint> points,
            string sectionId)
        {
            int count = 0;
            for (int i = points.Count - 1; i >= 0; i--)
            {
                if (points[i].sectionId != sectionId) break;
                count++;
            }
            return count;
        }

        private static TrackConnectionFrame Interpolate(
            TrackConnectionFrame a, TrackConnectionFrame b, float t)
        {
            return new TrackConnectionFrame
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Forward = Vector3.Slerp(a.Forward, b.Forward, t).normalized,
                Right = Vector3.Slerp(a.Right, b.Right, t).normalized,
                Up = Vector3.Slerp(a.Up, b.Up, t).normalized,
                Width = Mathf.Lerp(a.Width, b.Width, t),
                BankAngle = Mathf.LerpAngle(a.BankAngle, b.BankAngle, t),
                PitchAngle = Mathf.LerpAngle(a.PitchAngle, b.PitchAngle, t),
                HorizontalCurvature = Mathf.Lerp(a.HorizontalCurvature, b.HorizontalCurvature, t),
                HorizontalCurvatureRate = Mathf.Lerp(a.HorizontalCurvatureRate, b.HorizontalCurvatureRate, t),
                VerticalCurvature = Mathf.Lerp(a.VerticalCurvature, b.VerticalCurvature, t),
                VerticalCurvatureRate = Mathf.Lerp(a.VerticalCurvatureRate, b.VerticalCurvatureRate, t),
                RoadRollRate = Mathf.Lerp(a.RoadRollRate, b.RoadRollRate, t),
                RoadRollAcceleration = Mathf.Lerp(a.RoadRollAcceleration, b.RoadRollAcceleration, t),
                ArcLength = Mathf.Lerp(a.ArcLength, b.ArcLength, t),
                LapProgress = Mathf.Lerp(a.LapProgress, b.LapProgress, t)
            };
        }
    }
}
