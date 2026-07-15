using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace TrackGeneration.Design
{
    /// <summary>How much of the generated layout is preserved across regeneration.</summary>
    public enum LayoutLockMode
    {
        [Tooltip("Everything may regenerate: footprint, topology, features, elevation, quarters.")]
        Unlocked,

        [Tooltip("Preserve the layout skeleton (corner plan, gap order, pacing, quarter topology — the Layout and Quarter seed streams) while content regenerates inside it: feature placement, corner realizations, elevation detail, surfaces, visuals.")]
        Skeleton,

        [Tooltip("Preserve the exact generated geometry (all generation streams). Only surface and visual treatment may change.")]
        Geometry
    }

    /// <summary>
    /// SETTINGS-group locks: preserve manually edited parameter groups when applying
    /// presets, difficulty/size modifiers, Random resolution or resets. These are
    /// settings locks, not generated-geometry locks (see <see cref="LayoutLockMode"/>).
    /// Serialized on the generator, so lock state survives scene reload, editor
    /// restart, play mode and preset changes. Runtime resolution never reads them.
    /// </summary>
    [Serializable]
    public class SettingsLockState
    {
        public bool ScaleAndSpeed;
        public bool Layout;
        public bool Corners;
        public bool RoadShape;
        public bool Transitions;
        public bool Elevation;
        public bool Features;
        [FormerlySerializedAs("Branches")]
        public bool Quarters;
        public bool GenerationBehavior;
        public bool Visual;

        /// <summary>All group names in display order.</summary>
        public static readonly string[] GroupNames =
        {
            "Scale and Speed", "Layout", "Corners", "Road Shape", "Transitions",
            "Elevation", "Features", "Quarters", "Generation Behavior", "Visual Settings"
        };

        public bool Get(int groupIndex) => groupIndex switch
        {
            0 => ScaleAndSpeed, 1 => Layout, 2 => Corners, 3 => RoadShape, 4 => Transitions,
            5 => Elevation, 6 => Features, 7 => Quarters, 8 => GenerationBehavior, _ => Visual
        };

        public void Set(int groupIndex, bool locked)
        {
            switch (groupIndex)
            {
                case 0: ScaleAndSpeed = locked; break;
                case 1: Layout = locked; break;
                case 2: Corners = locked; break;
                case 3: RoadShape = locked; break;
                case 4: Transitions = locked; break;
                case 5: Elevation = locked; break;
                case 6: Features = locked; break;
                case 7: Quarters = locked; break;
                case 8: GenerationBehavior = locked; break;
                default: Visual = locked; break;
            }
        }

        public void SetAll(bool locked)
        {
            for (int i = 0; i < GroupNames.Length; i++) Set(i, locked);
        }

        public void Invert()
        {
            for (int i = 0; i < GroupNames.Length; i++) Set(i, !Get(i));
        }

        public int LockedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < GroupNames.Length; i++) if (Get(i)) n++;
                return n;
            }
        }

        public List<string> LockedGroupNames()
        {
            var list = new List<string>();
            for (int i = 0; i < GroupNames.Length; i++) if (Get(i)) list.Add(GroupNames[i]);
            return list;
        }
    }

    /// <summary>
    /// Applies presets/modifiers GROUP-WISE so locked groups keep their manually
    /// edited values. All preset application paths (style, difficulty, size, Random)
    /// go through here.
    /// </summary>
    public static class PresetApplicator
    {
        /// <summary>Result of one lock-aware application: which groups changed, which were skipped.</summary>
        public class ApplyResult
        {
            public List<string> ChangedGroups = new List<string>();
            public List<string> SkippedGroups = new List<string>();
        }

        /// <summary>
        /// Copies <paramref name="incoming"/> into <paramref name="target"/> group by
        /// group, skipping locked groups. Groups that are byte-identical are not
        /// reported as changed.
        /// </summary>
        public static ApplyResult Apply(TrackDesignerSettings target, TrackDesignerSettings incoming, SettingsLockState locks)
        {
            var result = new ApplyResult();
            locks ??= new SettingsLockState();

            void Group<T>(int idx, Func<TrackDesignerSettings, T> get, Action<TrackDesignerSettings, T> set) where T : class
            {
                string name = SettingsLockState.GroupNames[idx];
                if (locks.Get(idx))
                {
                    result.SkippedGroups.Add(name);
                    return;
                }

                T incomingGroup = get(incoming);
                if (JsonUtility.ToJson(get(target)) != JsonUtility.ToJson(incomingGroup))
                    result.ChangedGroups.Add(name);
                set(target, incomingGroup);
            }

            Group(0, s => s.Scale, (s, v) => s.Scale = v);
            Group(1, s => s.Layout, (s, v) => s.Layout = v);
            Group(2, s => s.Corners, (s, v) => s.Corners = v);
            Group(3, s => s.Road, (s, v) => s.Road = v);
            Group(4, s => s.Transitions, (s, v) => s.Transitions = v);
            Group(5, s => s.Elevation, (s, v) => s.Elevation = v);
            Group(6, s => s.Features, (s, v) => s.Features = v);
            Group(7, s => s.Quarters, (s, v) => s.Quarters = v);
            Group(8, s => s.Generation, (s, v) => s.Generation = v);
            Group(9, s => s.Visual, (s, v) => s.Visual = v);

            target.AppliedPresetName = incoming.AppliedPresetName;
            target.ModifiedSincePreset = locks.LockedCount > 0; // locked manual edits survive
            target.Sanitize();
            target.AppliedPresetSnapshotJson = target.ToJson();
            return result;
        }

        /// <summary>
        /// Locks every group whose current values differ from the last applied preset
        /// snapshot (i.e. groups containing manual changes).
        /// </summary>
        public static List<string> LockModifiedGroups(TrackDesignerSettings current, SettingsLockState locks)
        {
            var locked = new List<string>();
            if (string.IsNullOrEmpty(current.AppliedPresetSnapshotJson)) return locked;

            var baseline = JsonUtility.FromJson<TrackDesignerSettings>(current.AppliedPresetSnapshotJson);
            if (baseline == null) return locked;

            void Check<T>(int idx, Func<TrackDesignerSettings, T> get) where T : class
            {
                if (JsonUtility.ToJson(get(current)) != JsonUtility.ToJson(get(baseline)))
                {
                    locks.Set(idx, true);
                    locked.Add(SettingsLockState.GroupNames[idx]);
                }
            }

            Check(0, s => s.Scale);
            Check(1, s => s.Layout);
            Check(2, s => s.Corners);
            Check(3, s => s.Road);
            Check(4, s => s.Transitions);
            Check(5, s => s.Elevation);
            Check(6, s => s.Features);
            Check(7, s => s.Quarters);
            Check(8, s => s.Generation);
            Check(9, s => s.Visual);
            return locked;
        }

        /// <summary>Restores the last applied preset snapshot for UNLOCKED groups only.</summary>
        public static ApplyResult ResetUnlockedGroups(TrackDesignerSettings current, SettingsLockState locks)
        {
            if (string.IsNullOrEmpty(current.AppliedPresetSnapshotJson)) return new ApplyResult();
            var baseline = JsonUtility.FromJson<TrackDesignerSettings>(current.AppliedPresetSnapshotJson);
            if (baseline == null) return new ApplyResult();

            string snapshot = current.AppliedPresetSnapshotJson;
            baseline.AppliedPresetName = current.AppliedPresetName;
            var result = Apply(current, baseline, locks);
            current.AppliedPresetSnapshotJson = snapshot;
            return result;
        }
    }
}
