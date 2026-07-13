using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Tests
{
    /// <summary>Shared helpers for the generation test suites (pure logic — no scenes, no meshes).</summary>
    public static class TrackGenerationTestUtil
    {
        /// <summary>A default rulebook instance (code defaults = the §17 high-speed values).</summary>
        public static TrackConfig CreateConfig()
        {
            var config = ScriptableObject.CreateInstance<TrackConfig>();
            config.Validate();
            return config;
        }

        /// <summary>Runs the full pipeline for one seed and settings.</summary>
        public static TrackGenerationResult Generate(TrackDesignerSettings settings, int seed, TrackConfig config = null)
        {
            bool ownsConfig = config == null;
            if (ownsConfig) config = CreateConfig();
            try
            {
                settings.Sanitize();
                var resolved = ResolvedTrackGenerationConfig.Resolve(config, settings);
                var pipeline = new TrackGenerationPipeline();
                return pipeline.Run(resolved, TrackSeed.CreateNew(seed));
            }
            finally
            {
                if (ownsConfig) Object.DestroyImmediate(config);
            }
        }

        /// <summary>Fast settings for bulk tests: first valid candidate, preset attempt budget, coarse rings (tests exercise logic, not mesh density).</summary>
        public static TrackDesignerSettings FastSettings(string presetName)
        {
            var s = TrackStylePresetLibrary.Create(presetName);
            s.Generation.SelectionMode = CandidateSelectionMode.FirstValid;
            s.Generation.MetersPerRing = 2.5f;
            return s;
        }

        /// <summary>Counts sections of a type in a layout.</summary>
        public static int Count(GeneratedTrackLayout layout, TrackMacroSectionType type)
        {
            int n = 0;
            foreach (var s in layout.Sections)
                if (s.Definition.SectionType == type) n++;
            return n;
        }

        /// <summary>A compact fingerprint of a layout for determinism comparison.</summary>
        public static string Fingerprint(GeneratedTrackLayout layout)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(layout.Sections.Count).Append('|');
            sb.Append(layout.LapLength.ToString("F3")).Append('|');
            foreach (var s in layout.Sections)
            {
                sb.Append(s.Definition.SectionType).Append(',');
                sb.Append(s.Definition.Length.ToString("F3")).Append(',');
                sb.Append(s.Definition.TurnAngle.ToString("F1")).Append(',');
                sb.Append(s.StartFrame.Position.x.ToString("F3")).Append(',');
                sb.Append(s.StartFrame.Position.y.ToString("F3")).Append(',');
                sb.Append(s.StartFrame.Position.z.ToString("F3")).Append(';');
            }
            foreach (var g in layout.BranchGroups)
            {
                sb.Append('B').Append(g.BranchGroupId).Append(g.PairingMode).Append(g.InteractionPattern).Append(';');
            }
            return sb.ToString();
        }
    }
}
