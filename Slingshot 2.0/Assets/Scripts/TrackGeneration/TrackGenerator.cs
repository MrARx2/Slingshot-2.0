using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;
using TrackGeneration.Race;

namespace TrackGeneration
{
    /// <summary>
    /// Orchestrator for the macro track-generation pipeline. Contains NO generation
    /// grammar — it wires:
    ///
    ///   Seed → designer settings → rulebook validation → resolved request →
    ///   candidate pipeline (plan / build / validate / score) → temporary mesh build →
    ///   race course → transactional swap → report.
    ///
    /// A failed generation NEVER destroys the previous valid track, and no fallback is
    /// ever presented as a success — the report says exactly what happened.
    /// </summary>
    [AddComponentMenu("Track Generation/Track Generator")]
    [RequireComponent(typeof(TrackSeedManager))]
    public class TrackGenerator : MonoBehaviour
    {
        [Header("Track Design (the actual request)")]
        [Tooltip("Every value generation uses. Style presets INITIALIZE these fields; nothing switches behavior on a preset name at runtime.")]
        public TrackDesignerSettings Designer = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);

        [Header("Rulebook (hard technical limits)")]
        [Tooltip("Absolute legal limits, safety constraints, mesh budgets and validation tolerances. Never the personality of a specific track.")]
        public TrackConfig Config;

        [Header("Materials")]
        public Material MainRoadMaterial;
        public Material WallMaterial;

        [Header("Generation Output")]
        [SerializeField] private Transform trackRoot;

        [Header("Race Course")]
        [Tooltip("Build a start/finish line and checkpoint groups (branch-aware) for time attack.")]
        [SerializeField] private bool buildRaceCourse = true;
        [Tooltip("Logical checkpoints per lap. Checkpoints inside a branch get one gate per route — crossing either advances the lap.")]
        [SerializeField, Range(1, 16)] private int checkpointCount = 8;
        [Tooltip("Arc distance from the spawn frame to the start/finish line.")]
        [SerializeField, Min(5f)] private float startLineArcOffset = 20f;

        [Header("Runtime Start")]
        [SerializeField] private bool generateOnStart = true;
        [Tooltip("Keep an editor-generated track when entering Play Mode instead of regenerating.")]
        [SerializeField] private bool keepEditorTrackOnPlay = true;
        [SerializeField] private bool placeHovercraftOnStart = true;
        [SerializeField, Min(0f)] private float startLineForwardOffset = 0f;
        [SerializeField] private bool resetHovercraftWithBackspace = true;

        [Header("Last Generation Report (read-only)")]
        [SerializeField] private TrackGenerationReport lastReport = new TrackGenerationReport();
        [SerializeField] private TrackGenerationMetrics lastMetrics = new TrackGenerationMetrics();

        [SerializeField, HideInInspector] private int generatedMeshCount;
        [SerializeField, HideInInspector] private int generatedVertexCount;
        [SerializeField, HideInInspector] private int generatedTriangleCount;

        /// <summary>Flattened generated section list (branch routes appear as consecutive pairs).</summary>
        public List<GeneratedTrackSection> CurrentMacroSections { get; private set; }

        /// <summary>The authoritative layout of the current track (null after scene reload — sections persist via the visualizer).</summary>
        public GeneratedTrackLayout CurrentLayout { get; private set; }

        /// <summary>Root transform the generated track is local to.</summary>
        public Transform TrackRoot => trackRoot;

        public TrackGenerationReport LastReport => lastReport;
        public TrackGenerationMetrics LastMetrics => lastMetrics;

        public int GeneratedMeshCount => generatedMeshCount;
        public int GeneratedVertexCount => generatedVertexCount;
        public int GeneratedTriangleCount => generatedTriangleCount;

        private TrackSeedManager _seedManager;
        private ITrackRaceCraft _craft;

        private void Awake()
        {
            _seedManager = GetComponent<TrackSeedManager>();
        }

        private void Start()
        {
            bool adoptedExisting = keepEditorTrackOnPlay && TryAdoptExistingTrack();

            if (generateOnStart && !adoptedExisting)
            {
                GenerateTrack();
            }

            if (placeHovercraftOnStart)
            {
                PlaceHovercraftAtTrackStart();
            }
        }

        private void Update()
        {
            if (!resetHovercraftWithBackspace || Keyboard.current == null)
                return;

            if (Keyboard.current.backspaceKey.wasPressedThisFrame)
            {
                PlaceHovercraftAtTrackStart();
            }
        }

        // ─────────────────────────── Generation ───────────────────────────

        [ContextMenu("Generate Track")]
        public void GenerateTrack()
        {
            if (Config == null)
            {
                Debug.LogError("[TrackGenerator] TrackConfig rulebook is missing.");
                return;
            }

            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            TrackSeed seed = _seedManager.InitializeSeed();

            Designer ??= TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
            Designer.Sanitize();

            TrackGenerationResult result = RunPipelineWithPolicy(seed);

            lastReport = result.Report;
            lastMetrics = result.Layout?.Metrics ?? new TrackGenerationMetrics();

            if (!result.Success)
            {
                LogFailure(result);
                return; // previous valid track stays untouched
            }

            if (!TryBuildTransactional(seed, result))
            {
                LogFailure(result);
                return;
            }

            LogSuccess(result);
        }

        /// <summary>Runs the pipeline, applying the configured failure policy when no valid candidate exists.</summary>
        private TrackGenerationResult RunPipelineWithPolicy(TrackSeed seed)
        {
            var pipeline = new TrackGenerationPipeline();

            ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(Config, Designer);
            TrackGenerationResult result = pipeline.Run(resolved, seed);
            if (result.Success) return result;

            switch (Designer.Generation.FailurePolicy)
            {
                case GenerationFailurePolicy.RelaxOptionalSettings:
                {
                    TrackDesignerSettings relaxed = Designer.Clone();
                    var records = RelaxOptionalSettings(relaxed);
                    var relaxedResolved = ResolvedTrackGenerationConfig.Resolve(Config, relaxed);
                    TrackGenerationResult retry = pipeline.Run(relaxedResolved, seed);

                    // Merge failure history so the report shows the whole story.
                    retry.Report.Failures.InsertRange(0, result.Report.Failures);
                    retry.Report.RelaxedSettings.AddRange(records);
                    retry.Report.AttemptsEvaluated += result.Report.AttemptsEvaluated;
                    return retry;
                }

                case GenerationFailurePolicy.UseSimpleTemplate:
                {
                    TrackDesignerSettings template = BuildTemplateSettings();
                    var templateResolved = ResolvedTrackGenerationConfig.Resolve(Config, template);
                    TrackGenerationResult retry = pipeline.Run(templateResolved, seed);

                    retry.Report.Failures.InsertRange(0, result.Report.Failures);
                    retry.Report.AttemptsEvaluated += result.Report.AttemptsEvaluated;
                    if (retry.Success)
                    {
                        retry.UsedFallback = true;
                        retry.Report.UsedFallback = true;
                        retry.Report.FallbackDescription =
                            "Simple validated template — the requested settings could NOT be satisfied. This track does not match the request.";
                    }
                    return retry;
                }

                default:
                    // KeepPreviousValidTrack / FailAndReport: nothing is built either way;
                    // the previous track is never destroyed before a successful swap.
                    return result;
            }
        }

        /// <summary>Relaxes optional weights/preferences ONLY — required counts and patterns are untouched.</summary>
        private static List<RelaxedSettingRecord> RelaxOptionalSettings(TrackDesignerSettings s)
        {
            var records = new List<RelaxedSettingRecord>();

            void Relax(string name, ref float value, float relaxedValue)
            {
                if (Mathf.Approximately(value, relaxedValue)) return;
                records.Add(new RelaxedSettingRecord { SettingName = name, OriginalValue = value, RelaxedValue = relaxedValue });
                value = relaxedValue;
            }

            Relax("Features.CompoundFeatureChance", ref s.Features.CompoundFeatureChance, 0f);
            Relax("Layout.CornerSequenceChance", ref s.Layout.CornerSequenceChance, s.Layout.CornerSequenceChance * 0.5f);
            Relax("Scale.PacingVariation", ref s.Scale.PacingVariation, Mathf.Min(s.Scale.PacingVariation, 0.4f));

            void RelaxRule(string name, TrackFeatureRule rule)
            {
                if (rule == null || !rule.Enabled) return;
                if (rule.MaximumCount > rule.MinimumCount)
                {
                    records.Add(new RelaxedSettingRecord
                    {
                        SettingName = $"{name}.MaximumCount",
                        OriginalValue = rule.MaximumCount,
                        RelaxedValue = Mathf.Max(rule.MinimumCount, rule.MinimumCount)
                    });
                    rule.MaximumCount = Mathf.Max(rule.MinimumCount, rule.MinimumCount);
                }
                if (rule.MinimumCount == 0 && rule.OptionalWeight > 0f)
                {
                    records.Add(new RelaxedSettingRecord
                    {
                        SettingName = $"{name}.OptionalWeight",
                        OriginalValue = rule.OptionalWeight,
                        RelaxedValue = 0f
                    });
                    rule.OptionalWeight = 0f;
                }
            }

            RelaxRule("Features.Loops", s.Features.Loops);
            RelaxRule("Features.Corkscrews", s.Features.Corkscrews);
            RelaxRule("Features.Spirals", s.Features.Spirals);
            RelaxRule("Features.Jumps", s.Features.Jumps);
            RelaxRule("Features.HalfLoops", s.Features.HalfLoops);
            RelaxRule("Features.Chicanes", s.Features.Chicanes);
            RelaxRule("Features.SCurves", s.Features.SCurves);

            if (s.Branches.MaxBranchGroups > s.Branches.MinBranchGroups)
            {
                records.Add(new RelaxedSettingRecord
                {
                    SettingName = "Branches.MaxBranchGroups",
                    OriginalValue = s.Branches.MaxBranchGroups,
                    RelaxedValue = s.Branches.MinBranchGroups
                });
                s.Branches.MaxBranchGroups = s.Branches.MinBranchGroups;
            }

            s.Sanitize();
            return records;
        }

        /// <summary>A minimal circuit request that still respects the rulebook's allowed features (no features, no branches).</summary>
        private TrackDesignerSettings BuildTemplateSettings()
        {
            TrackDesignerSettings t = Designer.Clone();
            t.Layout.MinTurnCount = 6;
            t.Layout.MaxTurnCount = 8;
            t.Layout.DirectionPattern = TurnDirectionPattern.Mixed;
            t.Layout.CornerSequenceChance = 0f;
            t.Features.Loops = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Corkscrews = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Spirals = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Jumps = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.HalfLoops = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Chicanes = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.SCurves = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Hairpins = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.MinFeatureGroups = 0;
            t.Features.MaxFeatureGroups = 0;
            t.Features.RequiredPatterns.Clear();
            t.Branches.MinBranchGroups = 0;
            t.Branches.MaxBranchGroups = 0;
            t.Elevation.TargetElevationAmplitude = Mathf.Min(t.Elevation.TargetElevationAmplitude, 80f);
            t.Elevation.MinMajorElevationSections = 0;
            t.Elevation.MaxMajorElevationSections = 3;
            t.Sanitize();
            return t;
        }

        // ─────────────────────────── Transactional build ───────────────────────────

        /// <summary>
        /// Builds meshes and the race course under a TEMPORARY root; only when everything
        /// succeeds is the previous track destroyed and the new root swapped in.
        /// </summary>
        private bool TryBuildTransactional(TrackSeed seed, TrackGenerationResult result)
        {
            GeneratedTrackLayout layout = result.Layout;
            var tempRootObj = new GameObject($"GeneratedTrack_{seed.BaseSeed}_pending");
            tempRootObj.transform.SetParent(transform, false);

            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(Config, Designer);

                var prismBuilder = new BoxPrismTrackMeshBuilder(resolved.RoadProfile);
                prismBuilder.Build(layout.Sections, MainRoadMaterial, WallMaterial, tempRootObj.transform);

                // Validate the built objects before committing.
                int meshCount = 0;
                foreach (MeshFilter f in tempRootObj.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && f.sharedMesh.vertexCount > 0) meshCount++;

                if (meshCount == 0)
                {
                    result.Report.AddFailure(-1, GenerationFailureReason.MeshBuildFailure, "MeshBuild",
                        "Mesh build produced no valid meshes.");
                    result.Success = false;
                    DestroyObject(tempRootObj);
                    return false;
                }

                var visualizer = tempRootObj.AddComponent<MacroTrackDebugVisualizer>();
                visualizer.Initialize(seed.BaseSeed, layout.Sections, resolved.RoadProfile);
                visualizer.SetLayout(layout);

                if (buildRaceCourse)
                {
                    RaceCourse course = RaceCourseBuilder.Build(tempRootObj.transform, layout, resolved.RoadProfile,
                        checkpointCount, startLineArcOffset);
                    if (course == null)
                    {
                        result.Report.AddFailure(-1, GenerationFailureReason.RaceCourseBuildFailure, "RaceCourse",
                            "Race course build failed — track rejected (transactional).");
                        result.Success = false;
                        DestroyObject(tempRootObj);
                        return false;
                    }
                }

                // Commit: destroy the previous track and promote the temporary root.
                if (trackRoot != null) DestroyObject(trackRoot.gameObject);

                tempRootObj.name = $"GeneratedTrack_{seed.BaseSeed}";
                trackRoot = tempRootObj.transform;

                CurrentMacroSections = layout.Sections;
                CurrentLayout = layout;
                UpdateGeneratedMeshStats();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                result.Report.AddFailure(-1, GenerationFailureReason.MeshBuildFailure, "MeshBuild",
                    $"Exception during build: {e.Message}");
                result.Success = false;
                DestroyObject(tempRootObj);
                return false;
            }
        }

        private static void DestroyObject(GameObject obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
            {
                obj.SetActive(false);
                Destroy(obj);
            }
            else DestroyImmediate(obj);
        }

        // ─────────────────────────── Adoption / spawn ───────────────────────────

        /// <summary>
        /// Adopts a track generated in the editor before play mode (the section list is
        /// serialized on the debug visualizer, so runtime references restore losslessly).
        /// </summary>
        private bool TryAdoptExistingTrack()
        {
            if (trackRoot == null) return false;

            bool hasMesh = false;
            foreach (MeshFilter filter in trackRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) { hasMesh = true; break; }
            }
            if (!hasMesh) return false;

            var visualizer = trackRoot.GetComponent<MacroTrackDebugVisualizer>();
            if (visualizer != null && visualizer.Sections != null && visualizer.Sections.Count > 0)
            {
                CurrentMacroSections = visualizer.Sections;
                UpdateGeneratedMeshStats();
                Debug.Log($"[TrackGenerator] Keeping editor-generated track (seed {visualizer.Seed}, {visualizer.Sections.Count} sections).");
                return true;
            }

            return false;
        }

        [ContextMenu("Place Hovercraft At Track Start")]
        public void PlaceHovercraftAtTrackStart()
        {
            _craft ??= TrackCraftLocator.FindCraft();
            if (_craft == null || _craft.CraftTransform == null)
            {
                Debug.LogWarning("[TrackGenerator] Could not place hovercraft: no ITrackRaceCraft found in the scene.");
                return;
            }

            if (!TryGetTrackStartFrame(out Vector3 position, out Vector3 forward, out Vector3 up))
            {
                Debug.LogWarning("[TrackGenerator] Could not place hovercraft: generate a track first.");
                return;
            }

            up = up.sqrMagnitude > 0.001f ? up.normalized : Vector3.up;
            forward = Vector3.ProjectOnPlane(forward, up);
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            Vector3 spawnPosition = position + forward * startLineForwardOffset + up * _craft.SpawnRideHeight;
            Quaternion spawnRotation = Quaternion.LookRotation(forward, up);

            Rigidbody rb = _craft.CraftRigidbody;
            if (rb != null)
            {
                rb.position = spawnPosition;
                rb.rotation = spawnRotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
            else
            {
                _craft.CraftTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
            }

            _craft.OnPlacedAtTrackStart();

            // A teleport is never a lap: abandon the lap in progress and re-arm the gates.
            if (trackRoot != null)
            {
                RaceCourse course = trackRoot.GetComponentInChildren<RaceCourse>(true);
                if (course != null) course.NotifyRespawn();
            }
        }

        private bool TryGetTrackStartFrame(out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            Transform root = trackRoot != null ? trackRoot : transform;

            if (CurrentMacroSections != null && CurrentMacroSections.Count > 0)
            {
                TrackConnectionFrame start = CurrentMacroSections[0].StartFrame;
                position = root.TransformPoint(start.Position);
                forward = root.TransformDirection(start.Forward);
                up = root.TransformDirection(start.Up);
                return true;
            }

            position = Vector3.zero;
            forward = Vector3.forward;
            up = Vector3.up;
            return false;
        }

        // ─────────────────────────── Stats / logging ───────────────────────────

        private void UpdateGeneratedMeshStats()
        {
            generatedMeshCount = 0;
            generatedVertexCount = 0;
            generatedTriangleCount = 0;

            if (trackRoot == null) return;

            foreach (MeshFilter filter in trackRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                UnityEngine.Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                generatedMeshCount++;
                generatedVertexCount += mesh.vertexCount;

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (mesh.GetTopology(i) == MeshTopology.Triangles)
                        generatedTriangleCount += (int)(mesh.GetIndexCount(i) / 3);
                }
            }
        }

        private void LogSuccess(TrackGenerationResult result)
        {
            var m = result.Layout.Metrics;
            string fallback = result.UsedFallback ? " [TEMPLATE FALLBACK — request NOT satisfied]" : "";
            Debug.Log(
                $"[TrackGenerator] Generated track{fallback}. Seed {result.RequestedSeed}, " +
                $"attempts {result.AttemptsEvaluated}, candidates {result.ValidCandidateCount}, score {result.Report.SelectedCandidateScore:F1}.\n" +
                $"  Lap {m.LapLengthMeters / 1000f:F2}km, est. {m.EstimatedNeutralLapTimeSeconds:F1}s | turns {m.TurnCount} | " +
                $"loops {m.LoopCount}, corkscrews {m.CorkscrewCount}, spirals {m.SpiralCount}, half-loops {m.HalfLoopCount}, jumps {m.JumpCount} | " +
                $"branches {m.BranchGroupCount} | elevation {m.MinElevation:F0}..{m.MaxElevation:F0}m | rings {m.TotalRings}.");

            foreach (var b in result.Report.BranchBalance)
                Debug.Log($"[TrackGenerator] {b}");

            foreach (var w in result.Report.Warnings)
                Debug.LogWarning($"[TrackGenerator] {w}");
        }

        private void LogFailure(TrackGenerationResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[TrackGenerator] Generation FAILED (seed {result.RequestedSeed}, {result.AttemptsEvaluated} attempts, {result.ValidCandidateCount} valid candidates). Previous track kept.");
            foreach (var (reason, count) in result.Report.FailureCountsByReason())
                sb.AppendLine($"  {reason}: ×{count}");

            int shown = 0;
            foreach (var f in result.Report.Failures)
            {
                if (shown++ >= 6) { sb.AppendLine("  … (full list in the Last Generation Report)"); break; }
                sb.AppendLine($"  {f}");
            }

            Debug.LogError(sb.ToString().TrimEnd());
        }
    }
}
