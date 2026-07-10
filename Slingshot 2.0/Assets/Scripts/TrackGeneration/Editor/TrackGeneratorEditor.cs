#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TrackGeneration.Core;

namespace TrackGeneration.Editor
{
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            TrackGenerator generator = (TrackGenerator)target;

            GUILayout.Space(20);
            if (GUILayout.Button("Generate Track", GUILayout.Height(40)))
            {
                generator.GenerateTrack();
                EditorUtility.SetDirty(generator);
            }

            var seedManager = generator.GetComponent<TrackSeedManager>();
            if (seedManager != null)
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Regenerate Same Seed", GUILayout.Height(28)))
                {
                    bool previous = seedManager.UseRandomSeed;
                    seedManager.UseRandomSeed = false;
                    generator.GenerateTrack();
                    seedManager.UseRandomSeed = previous;
                    EditorUtility.SetDirty(generator);
                }

                if (GUILayout.Button("Randomize Seed", GUILayout.Height(28)))
                {
                    seedManager.UseRandomSeed = true;
                    generator.GenerateTrack();
                    EditorUtility.SetDirty(generator);
                }

                EditorGUILayout.EndHorizontal();
            }

            DrawPolyCount(generator);

            if (generator.CurrentMacroSections != null && generator.CurrentMacroSections.Count > 0)
            {
                var sections = generator.CurrentMacroSections;
                float length = sections[sections.Count - 1].EndFrame.ArcLength;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Generated Macro Track: {sections.Count} sections, {length:F0}m");
                for (int i = 0; i < sections.Count; i++)
                {
                    sb.AppendLine($"  [{i:D2}] {sections[i].Definition.DebugName}");
                }

                GUILayout.Space(10);
                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Info);
            }

            if (generator.CurrentTrackData != null)
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox($"Generated Track:\n- Length: {generator.CurrentTrackData.MainCircuitLength:F1}m\n- Shortcuts: {generator.CurrentTrackData.Shortcuts.Count}\n- Stunts: {generator.CurrentTrackData.StuntPlacements.Count}\n- Gravity Zones: {generator.CurrentTrackData.GravityZonePlacements.Count}", MessageType.Info);
            }
        }

        private static void DrawPolyCount(TrackGenerator generator)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Generated Track Poly Count", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Meshes", generator.GeneratedMeshCount);
                EditorGUILayout.TextField("Vertices", generator.GeneratedVertexCount.ToString("N0"));
                EditorGUILayout.TextField("Triangles / Polys", generator.GeneratedTriangleCount.ToString("N0"));
            }
        }
    }
}
#endif
