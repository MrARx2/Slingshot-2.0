#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Editor
{
    /// <summary>
    /// Designer-facing inspector for the track generator: preset tools (initializers,
    /// never runtime modes), the full grouped settings, a resolved preview of the derived
    /// values BEFORE generation, and the last generation report.
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : UnityEditor.Editor
    {
        private int _presetIndex;
        private TrackDifficultyLevel _difficulty = TrackDifficultyLevel.Normal;
        private TrackSizeLevel _size = TrackSizeLevel.Medium;
        private bool _showResolvedPreview = true;
        private bool _showReport = true;

        public override void OnInspectorGUI()
        {
            var generator = (TrackGenerator)target;
            generator.Designer ??= TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);

            DrawPresetTools(generator);

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(generator.Designer.AppliedPresetName))
            {
                generator.Designer.ModifiedSincePreset = true;
            }

            DrawResolvedPreview(generator);
            DrawGenerationButtons(generator);
            DrawPolyCount(generator);
            DrawLastReport(generator);
        }

        // ─────────────────────────── Preset tools ───────────────────────────

        private void DrawPresetTools(TrackGenerator generator)
        {
            EditorGUILayout.LabelField("Preset Tools", EditorStyles.boldLabel);

            // Provenance: presets are initializers — show what these settings came from.
            string provenance = string.IsNullOrEmpty(generator.Designer.AppliedPresetName)
                ? "Custom (no preset applied)"
                : generator.Designer.ModifiedSincePreset
                    ? $"Custom — based on {generator.Designer.AppliedPresetName}"
                    : generator.Designer.AppliedPresetName;
            EditorGUILayout.HelpBox($"Settings: {provenance}", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                _presetIndex = EditorGUILayout.Popup(_presetIndex, TrackStylePresetLibrary.BuiltInNames);
                if (GUILayout.Button("Apply Style Preset", GUILayout.Width(150)))
                {
                    Undo.RecordObject(generator, "Apply Style Preset");
                    generator.Designer = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.BuiltInNames[_presetIndex]);
                    EditorUtility.SetDirty(generator);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _difficulty = (TrackDifficultyLevel)EditorGUILayout.EnumPopup(_difficulty);
                if (GUILayout.Button("Apply Difficulty Modifier", GUILayout.Width(150)))
                {
                    Undo.RecordObject(generator, "Apply Difficulty Modifier");
                    TrackDifficultyModifier.Apply(generator.Designer, _difficulty);
                    EditorUtility.SetDirty(generator);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _size = (TrackSizeLevel)EditorGUILayout.EnumPopup(_size);
                if (GUILayout.Button("Apply Size Modifier", GUILayout.Width(150)))
                {
                    Undo.RecordObject(generator, "Apply Size Modifier");
                    TrackSizeModifier.Apply(generator.Designer, _size);
                    EditorUtility.SetDirty(generator);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(generator.Designer.AppliedPresetSnapshotJson)))
                {
                    if (GUILayout.Button("Reset to Applied Preset"))
                    {
                        Undo.RecordObject(generator, "Reset to Applied Preset");
                        var restored = JsonUtility.FromJson<TrackDesignerSettings>(generator.Designer.AppliedPresetSnapshotJson);
                        if (restored != null)
                        {
                            string snapshot = generator.Designer.AppliedPresetSnapshotJson;
                            generator.Designer = restored;
                            generator.Designer.AppliedPresetSnapshotJson = snapshot;
                            generator.Designer.ModifiedSincePreset = false;
                        }
                        EditorUtility.SetDirty(generator);
                    }

                    if (GUILayout.Button("Compare Current to Preset"))
                    {
                        CompareToPreset(generator.Designer);
                    }
                }

                if (GUILayout.Button("Save Current as Preset Asset"))
                {
                    SaveAsPresetAsset(generator.Designer);
                }
            }
        }

        private static void CompareToPreset(TrackDesignerSettings current)
        {
            if (string.IsNullOrEmpty(current.AppliedPresetSnapshotJson))
            {
                EditorUtility.DisplayDialog("Compare", "No preset snapshot to compare against.", "OK");
                return;
            }

            var baseline = JsonUtility.FromJson<TrackDesignerSettings>(current.AppliedPresetSnapshotJson);
            var diffs = new List<string>();
            DiffObjects("", baseline, current, diffs);

            string message = diffs.Count == 0
                ? $"No differences from '{current.AppliedPresetName}'."
                : $"{diffs.Count} field(s) differ from '{current.AppliedPresetName}':\n\n" + string.Join("\n", diffs.GetRange(0, Mathf.Min(30, diffs.Count)));
            if (diffs.Count > 30) message += $"\n… and {diffs.Count - 30} more (see console).";

            Debug.Log($"[TrackGeneratorEditor] Preset comparison ({diffs.Count} differences):\n" + string.Join("\n", diffs));
            EditorUtility.DisplayDialog("Compare Current Settings to Preset", message, "OK");
        }

        /// <summary>Recursive field-level diff over the serializable settings graph.</summary>
        private static void DiffObjects(string path, object a, object b, List<string> diffs)
        {
            if (a == null || b == null)
            {
                if (a != b) diffs.Add($"{path}: {(a == null ? "null" : a.ToString())} → {(b == null ? "null" : b.ToString())}");
                return;
            }

            System.Type type = a.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == "AppliedPresetName" || field.Name == "ModifiedSincePreset") continue;

                object va = field.GetValue(a);
                object vb = field.GetValue(b);
                string fieldPath = string.IsNullOrEmpty(path) ? field.Name : $"{path}.{field.Name}";

                if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string))
                {
                    if (!Equals(va, vb)) diffs.Add($"{fieldPath}: {va} → {vb}");
                }
                else if (field.FieldType == typeof(Vector2))
                {
                    if ((Vector2)va != (Vector2)vb) diffs.Add($"{fieldPath}: {va} → {vb}");
                }
                else if (typeof(System.Collections.IList).IsAssignableFrom(field.FieldType))
                {
                    var la = (System.Collections.IList)va;
                    var lb = (System.Collections.IList)vb;
                    if ((la?.Count ?? 0) != (lb?.Count ?? 0))
                        diffs.Add($"{fieldPath}: {la?.Count ?? 0} entries → {lb?.Count ?? 0} entries");
                }
                else if (field.FieldType.IsClass)
                {
                    DiffObjects(fieldPath, va, vb, diffs);
                }
            }
        }

        private static void SaveAsPresetAsset(TrackDesignerSettings settings)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Track Style Preset",
                "TrackStylePreset_Custom", "asset", "Save the current designer settings as an editable preset asset.");
            if (string.IsNullOrEmpty(path)) return;

            var preset = ScriptableObject.CreateInstance<TrackStylePreset>();
            preset.DisplayName = System.IO.Path.GetFileNameWithoutExtension(path).Replace("TrackStylePreset_", "");
            preset.Settings = settings.Clone();
            preset.Settings.AppliedPresetName = "";
            preset.Settings.ModifiedSincePreset = false;

            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(preset);
        }

        // ─────────────────────────── Resolved preview ───────────────────────────

        private void DrawResolvedPreview(TrackGenerator generator)
        {
            EditorGUILayout.Space(6);
            _showResolvedPreview = EditorGUILayout.Foldout(_showResolvedPreview, "Resolved Preview (derived values before generation)", true);
            if (!_showResolvedPreview) return;

            if (generator.Config == null)
            {
                EditorGUILayout.HelpBox("Assign a TrackConfig rulebook to see the resolved preview.", MessageType.Warning);
                return;
            }

            ResolvedTrackGenerationConfig r = ResolvedTrackGenerationConfig.Resolve(generator.Config, generator.Designer.Clone());

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Design speed: {r.DesignSpeedKph:F0} km/h  ({r.DesignSpeedMps:F0} m/s)");
            sb.AppendLine($"Requested lap: {r.TargetLapTimeSeconds:F0} s  → target length ≈ {r.TargetTrackLength / 1000f:F1} km (cap {r.MaxTrackLength / 1000f:F1} km)");
            sb.AppendLine($"Straights: {r.MinStraightLength:F0}–{r.MaxStraightLength:F0} m | curve radius {r.MinCurveRadius:F0}–{r.MaxCurveRadius:F0} m | turns {r.MinTurnCount}–{r.MaxTurnCount} ({r.DirectionPattern})");
            sb.AppendLine($"Approach {r.DefaultApproachLength:F0} m | recovery {r.DefaultRecoveryLength:F0} m | dangerous spacing {r.DangerousSpacingLength:F0} m");
            sb.AppendLine($"Bank blend {r.BankTransitionLength:F0} m | pitch {r.PitchTransitionLength:F0} m | roll {r.RollTransitionLength:F0} m | max bank {r.MaxBankAngle:F0}°");
            sb.AppendLine($"Feature groups: {r.MinFeatureGroups}–{r.MaxFeatureGroups} | compound chance {r.CompoundFeatureChance:P0}");
            if (r.RequiredPatterns.Count > 0)
            {
                sb.Append("Required patterns: ");
                foreach (var p in r.RequiredPatterns) sb.Append($"{p.Count}× {p.Pattern}  ");
                sb.AppendLine();
            }
            sb.AppendLine($"Branch groups: {r.MinBranchGroups}–{r.MaxBranchGroups} | route {r.MinRouteLength:F0}–{r.MaxRouteLength:F0} m | lateral sep ≥ {r.MinLateralSeparation:F0} m (wall-aware)");
            sb.AppendLine($"Elevation: amplitude {r.TargetElevationAmplitude:F0} m | majors {r.MinMajorElevationSections}–{r.MaxMajorElevationSections} | climb ≤ {r.MaxClimbAngle:F0}° | clearance {r.VerticalClearance:F0} m (walls included)");
            sb.AppendLine($"Expected rings ≈ {r.TargetTrackLength / Mathf.Max(0.5f, r.MeshMetersPerRing) * 1.4f:F0} (budget {r.MaxTotalRings}) | facet target {r.MaxRingFacetAngle:F2}°");
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Info);

            foreach (var issue in r.Issues)
            {
                MessageType t = issue.Severity switch
                {
                    ResolvedIssueSeverity.Error => MessageType.Error,
                    ResolvedIssueSeverity.Warning => MessageType.Warning,
                    _ => MessageType.None
                };
                EditorGUILayout.HelpBox($"{issue.Field}: {issue.Message}", t);
            }
        }

        // ─────────────────────────── Generation & report ───────────────────────────

        private void DrawGenerationButtons(TrackGenerator generator)
        {
            GUILayout.Space(12);
            if (GUILayout.Button("Generate Track", GUILayout.Height(40)))
            {
                generator.GenerateTrack();
                EditorUtility.SetDirty(generator);
            }

            var seedManager = generator.GetComponent<TrackSeedManager>();
            if (seedManager != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
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
                }
            }
        }

        private static void DrawPolyCount(TrackGenerator generator)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Generated Track Mesh Statistics", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Meshes", generator.GeneratedMeshCount);
                EditorGUILayout.TextField("Vertices", generator.GeneratedVertexCount.ToString("N0"));
                EditorGUILayout.TextField("Triangles", generator.GeneratedTriangleCount.ToString("N0"));
            }
        }

        private void DrawLastReport(TrackGenerator generator)
        {
            GUILayout.Space(8);
            _showReport = EditorGUILayout.Foldout(_showReport, "Last Generation Report", true);
            if (!_showReport) return;

            TrackGenerationReport report = generator.LastReport;
            TrackGenerationMetrics metrics = generator.LastMetrics;
            if (report == null || (report.AttemptsEvaluated == 0 && report.Failures.Count == 0))
            {
                EditorGUILayout.HelpBox("No generation has run yet.", MessageType.None);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Seed {report.Seed} — {(report.Success ? "SUCCESS" : "FAILED")}{(report.UsedFallback ? "  [TEMPLATE FALLBACK]" : "")}");
            sb.AppendLine($"Attempts {report.AttemptsEvaluated} | valid candidates {report.ValidCandidateCount} | selected score {report.SelectedCandidateScore:F1}");
            if (report.Success && metrics != null)
            {
                sb.AppendLine($"Lap {metrics.LapLengthMeters / 1000f:F2} km | est. neutral lap {metrics.EstimatedNeutralLapTimeSeconds:F1} s | turns {metrics.TurnCount}");
                sb.AppendLine($"Loops {metrics.LoopCount} | corkscrews {metrics.CorkscrewCount} | spirals {metrics.SpiralCount} | half-loops {metrics.HalfLoopCount} | jumps {metrics.JumpCount} | compounds {metrics.CompoundPatternCount}");
                sb.AppendLine($"Hairpins {metrics.HairpinCount} | chicanes {metrics.ChicaneCount} | S-curves {metrics.SCurveCount} | branch groups {metrics.BranchGroupCount}");
                sb.AppendLine($"Elevation {metrics.MinElevation:F0}..{metrics.MaxElevation:F0} m | min clearance {metrics.MinObservedClearance:F0} m | max facet {metrics.MaxFacetAngleObserved:F2}° | rings {metrics.TotalRings}");
            }
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), report.Success ? MessageType.Info : MessageType.Error);

            if (!string.IsNullOrEmpty(report.FallbackDescription))
                EditorGUILayout.HelpBox(report.FallbackDescription, MessageType.Warning);

            if (report.RelaxedSettings.Count > 0)
            {
                var relaxSb = new System.Text.StringBuilder("Relaxed settings:\n");
                foreach (var rec in report.RelaxedSettings) relaxSb.AppendLine($"  {rec}");
                EditorGUILayout.HelpBox(relaxSb.ToString().TrimEnd(), MessageType.Warning);
            }

            foreach (var balance in report.BranchBalance)
                EditorGUILayout.HelpBox(balance.ToString(), MessageType.None);

            if (report.Failures.Count > 0)
            {
                var failSb = new System.Text.StringBuilder($"Failures by reason ({report.Failures.Count} total):\n");
                foreach (var (reason, count) in report.FailureCountsByReason())
                    failSb.AppendLine($"  {reason}: ×{count}");
                failSb.AppendLine("\nMost recent:");
                int shown = 0;
                for (int i = report.Failures.Count - 1; i >= 0 && shown < 5; i--, shown++)
                    failSb.AppendLine($"  {report.Failures[i]}");
                EditorGUILayout.HelpBox(failSb.ToString().TrimEnd(), report.Success ? MessageType.None : MessageType.Warning);
            }
        }
    }
}
#endif
