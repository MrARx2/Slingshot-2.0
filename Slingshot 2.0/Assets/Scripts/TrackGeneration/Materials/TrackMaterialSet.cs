using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration
{
    /// <summary>
    /// Persistent visual materials used by every generated-track subsystem.
    /// Keeping these references in a project asset makes editor previews, cached
    /// layouts and exact recipe replays visually deterministic.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackMaterialSet", menuName = "Track Generation/Material Set")]
    public sealed class TrackMaterialSet : ScriptableObject
    {
        [Header("Road")]
        [Tooltip("Driving floor surface of the half-pipe road.")]
        public Material RoadSurface;
        [Tooltip("Rideable inner walls of the half-pipe. Existing material sets fall back to Road Surface until this is assigned.")]
        public Material InnerWallSurface;
        [Tooltip("Non-drivable outer shell, exposed tips, and end caps.")]
        public Material WallSide;

        [Header("Guidance")]
        public Material GuideMarking;
        public Material WallMarker;

        [Header("Gameplay")]
        public Material BoostSurface;
        [Tooltip("Generic gate fallback retained for older material sets.")]
        public Material RaceGate;
        public Material StartFinish;
        public Material StartGatePillar;
        public Material Checkpoint;

        public bool IsComplete => GetMissingRoles().Count == 0;

        public List<string> GetMissingRoles()
        {
            var missing = new List<string>();
            if (RoadSurface == null) missing.Add(nameof(RoadSurface));
            if (InnerWallSurface == null) missing.Add(nameof(InnerWallSurface));
            if (WallSide == null) missing.Add(nameof(WallSide));
            if (GuideMarking == null) missing.Add(nameof(GuideMarking));
            if (WallMarker == null) missing.Add(nameof(WallMarker));
            if (BoostSurface == null) missing.Add(nameof(BoostSurface));
            if (RaceGate == null) missing.Add(nameof(RaceGate));
            if (StartFinish == null) missing.Add(nameof(StartFinish));
            if (StartGatePillar == null) missing.Add(nameof(StartGatePillar));
            if (Checkpoint == null) missing.Add(nameof(Checkpoint));
            return missing;
        }
    }

    /// <summary>
    /// Resolved material contract. Legacy fields are accepted during migration, but
    /// builders consume this single immutable result rather than inventing materials.
    /// </summary>
    public readonly struct ResolvedTrackMaterials
    {
        public readonly Material RoadSurface;
        public readonly Material InnerWallSurface;
        public readonly Material WallSide;
        public readonly Material GuideMarking;
        public readonly Material WallMarker;
        public readonly Material BoostSurface;
        public readonly Material RaceGate;
        public readonly Material StartFinish;
        public readonly Material StartGatePillar;
        public readonly Material Checkpoint;

        private ResolvedTrackMaterials(Material roadSurface, Material innerWallSurface, Material wallSide,
            Material guideMarking, Material wallMarker, Material boostSurface,
            Material raceGate, Material startFinish, Material startGatePillar,
            Material checkpoint)
        {
            RoadSurface = roadSurface;
            InnerWallSurface = innerWallSurface;
            WallSide = wallSide;
            GuideMarking = guideMarking;
            WallMarker = wallMarker;
            BoostSurface = boostSurface;
            RaceGate = raceGate;
            StartFinish = startFinish;
            StartGatePillar = startGatePillar;
            Checkpoint = checkpoint;
        }

        public static ResolvedTrackMaterials Resolve(TrackMaterialSet set,
            Material legacyRoad, Material legacyWall, Material legacyGuide,
            Material legacyStartFinish, Material legacyStartPillar,
            Material legacyCheckpoint)
        {
            Material raceGate = set != null && set.RaceGate != null
                ? set.RaceGate
                : legacyCheckpoint != null ? legacyCheckpoint
                : legacyStartFinish != null ? legacyStartFinish
                : legacyStartPillar;

            Material guide = set != null && set.GuideMarking != null
                ? set.GuideMarking : legacyGuide;

            Material roadSurface = set != null && set.RoadSurface != null ? set.RoadSurface : legacyRoad;
            Material innerWallSurface = set != null && set.InnerWallSurface != null
                ? set.InnerWallSurface
                : roadSurface;

            return new ResolvedTrackMaterials(
                roadSurface,
                innerWallSurface,
                set != null && set.WallSide != null ? set.WallSide : legacyWall,
                guide,
                set != null && set.WallMarker != null ? set.WallMarker : guide,
                set != null ? set.BoostSurface : null,
                raceGate,
                set != null && set.StartFinish != null
                    ? set.StartFinish : legacyStartFinish != null ? legacyStartFinish : raceGate,
                set != null && set.StartGatePillar != null
                    ? set.StartGatePillar : legacyStartPillar != null ? legacyStartPillar : raceGate,
                set != null && set.Checkpoint != null
                    ? set.Checkpoint : legacyCheckpoint != null ? legacyCheckpoint : raceGate);
        }

        public List<string> GetMissingRequiredRoles(bool includeRaceCourse)
        {
            var missing = new List<string>();
            if (RoadSurface == null) missing.Add(nameof(RoadSurface));
            if (InnerWallSurface == null) missing.Add(nameof(InnerWallSurface));
            if (WallSide == null) missing.Add(nameof(WallSide));
            if (GuideMarking == null) missing.Add(nameof(GuideMarking));
            if (WallMarker == null) missing.Add(nameof(WallMarker));
            if (includeRaceCourse)
            {
                if (StartFinish == null) missing.Add(nameof(StartFinish));
                if (StartGatePillar == null) missing.Add(nameof(StartGatePillar));
                if (Checkpoint == null) missing.Add(nameof(Checkpoint));
            }
            return missing;
        }
    }
}
