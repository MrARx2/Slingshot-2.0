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
#if UNITY_EDITOR
        public static event System.Action<TrackGenerator> EditorPreviewBuilt;
        public static event System.Action<TrackGenerator> EditorPreviewCleared;
#endif

        [Header("Track Design (the actual request)")]
        [Tooltip("Every value generation uses. Style presets INITIALIZE these fields; nothing switches behavior on a preset name at runtime.")]
        public TrackDesignerSettings Designer = TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);

        [Header("Rulebook (hard technical limits)")]
        [Tooltip("Absolute legal limits, safety constraints, mesh budgets and validation tolerances. Never the personality of a specific track.")]
        public TrackConfig Config;

        [Header("Materials")]
        public Material MainRoadMaterial;
        public Material WallMaterial;
        [UnityEngine.Serialization.FormerlySerializedAs("GuideLineMaterial")]
        [Tooltip("Persistent, editable material for road center lines and wall marker bands. Using an asset prevents cached editor previews from falling back to magenta.")]
        public Material RoadLineMaterial;
        [Tooltip("Persistent checker/emissive material used by the start/finish beam and floor stripe.")]
        public Material StartFinishMaterial;
        [Tooltip("Persistent structural material used by the start/finish pillars.")]
        public Material StartGatePillarMaterial;
        [Tooltip("Persistent emissive material shared by checkpoint arches and number labels.")]
        public Material CheckpointMaterial;

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
        [Tooltip("Standalone/player builds may generate when no track exists. In the Unity Editor, Play Mode never silently replaces a missing preview — use Generate New Track explicitly.")]
        [SerializeField] private bool generateOnStart = true;
        [Tooltip("Adopt the complete editor-generated preview when entering Play Mode instead of rebuilding it.")]
        [SerializeField] private bool keepEditorTrackOnPlay = true;
        [SerializeField] private bool placeHovercraftOnStart = true;
        [SerializeField, Min(0f)] private float startLineForwardOffset = 0f;
        [SerializeField] private bool resetHovercraftWithBackspace = true;

        [Header("Seed Streams (independent subsystem randomness)")]
        [Tooltip("Per-subsystem seed streams. Partial regeneration commands re-randomize only some of them — 'Same Layout, New Content' keeps Layout+Quarter and rolls Feature/Elevation/Surface/Visual.")]
        [SerializeField] private TrackSeedStreams seedStreams = new TrackSeedStreams();

        [Header("Settings Locks (preserved across presets and Random)")]
        [SerializeField] private SettingsLockState settingsLocks = new SettingsLockState();

        [Tooltip("Layout lock: Skeleton preserves the corner plan/branch topology streams across 'Randomize All Unlocked'; Geometry preserves every generation stream (only surface/visual treatment may change).")]
        [SerializeField] private LayoutLockMode layoutLockMode = LayoutLockMode.Unlocked;

        // Read-only generation output. HIDDEN from the inspector: the custom editor
        // shows a cached summary instead — the default property drawer would traverse
        // every failure/warning string of the report on every repaint.
        [SerializeField, HideInInspector] private TrackGenerationReport lastReport = new TrackGenerationReport();
        [SerializeField, HideInInspector] private TrackGenerationMetrics lastMetrics = new TrackGenerationMetrics();

        [Tooltip("Bumped whenever a generation result is stored — editor caches key off it.")]
        [SerializeField, HideInInspector] private int generationRevision;

        [SerializeField, HideInInspector] private int generatedMeshCount;
        [SerializeField, HideInInspector] private int generatedVertexCount;
        [SerializeField, HideInInspector] private int generatedTriangleCount;

        // Generator-owned backup of the start frame. The generated hierarchy normally
        // carries a TrackStartAnchor, but these values survive editor preview recovery
        // even when optional runtime component references do not.
        [SerializeField, HideInInspector] private bool cachedStartFrameValid;
        [SerializeField, HideInInspector] private Vector3 cachedStartLocalPosition;
        [SerializeField, HideInInspector] private Vector3 cachedStartLocalForward = Vector3.forward;
        [SerializeField, HideInInspector] private Vector3 cachedStartLocalUp = Vector3.up;

        /// <summary>Flattened generated section list (branch routes appear as consecutive pairs).</summary>
        public List<GeneratedTrackSection> CurrentMacroSections { get; private set; }

        /// <summary>The authoritative layout of the current track (null after scene reload — sections persist via the visualizer).</summary>
        public GeneratedTrackLayout CurrentLayout { get; private set; }

        /// <summary>Root transform the generated track is local to.</summary>
        public Transform TrackRoot => trackRoot;

        public TrackGenerationReport LastReport => lastReport;
        public TrackGenerationMetrics LastMetrics => lastMetrics;

        /// <summary>Independent per-subsystem seed streams (serialized — partial regeneration state).</summary>
        public TrackSeedStreams SeedStreams => seedStreams;

        /// <summary>Settings-group locks (editor state; never part of runtime resolution).</summary>
        public SettingsLockState SettingsLocks => settingsLocks;

        /// <summary>Layout lock mode consumed by 'Randomize All Unlocked'.</summary>
        public LayoutLockMode LayoutLockMode
        {
            get => layoutLockMode;
            set => layoutLockMode = value;
        }

        public int GeneratedMeshCount => generatedMeshCount;
        public int GeneratedVertexCount => generatedVertexCount;
        public int GeneratedTriangleCount => generatedTriangleCount;

        /// <summary>Monotonic counter of stored generation results — inspector caches rebuild only when this changes.</summary>
        public int GenerationRevision => generationRevision;

        private TrackSeedManager _seedManager;
        private ITrackRaceCraft _craft;
        private bool _isGenerating;
        private bool _pendingStartPlacement;
        private float _startPlacementRetryDeadline;
        private bool _startPlacementFailureLogged;

        private const string GeneratedTrackRootPrefix = "GeneratedTrack_";
        private const string PendingTrackSuffix = "_pending";

        private void Awake()
        {
            _seedManager = GetComponent<TrackSeedManager>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Procedural track meshes can contain several gigabytes of vertex/index data.
            // They are reproducible from the saved seed and settings, so keep them in the
            // editor scene for preview and play, but never embed them in the .unity file.
            if (!Application.isPlaying && trackRoot != null)
            {
                DisableDebugVisualization(trackRoot.gameObject);
                RepairGeneratedTrackState(trackRoot.gameObject);
                MarkGeneratedHierarchyTransient(trackRoot.gameObject);
            }
        }

        /// <summary>Editor save guard for tracks created before transient serialization was introduced.</summary>
        public void PrepareGeneratedTrackForEditorSave()
        {
            if (Application.isPlaying) return;

            // Domain reloads and interrupted editor sessions can lose the serialized
            // reference while the transient preview is still alive. Guard every
            // generated root so a scene save can never embed its procedural meshes.
            List<GameObject> roots = FindGeneratedTrackRoots();
            foreach (GameObject root in roots)
            {
                DisableDebugVisualization(root);
                RepairGeneratedTrackState(root);
                MarkGeneratedHierarchyTransient(root);
            }

            if (trackRoot == null)
            {
                GameObject recovered = SelectNewestCompleteTrack(roots);
                if (recovered != null)
                    trackRoot = recovered.transform;
            }
        }
#endif

        private void Start()
        {
            bool adoptedExisting = keepEditorTrackOnPlay && TryAdoptExistingTrack();

            if (generateOnStart && !adoptedExisting)
            {
#if UNITY_EDITOR
                // Editor track creation is an explicit designer action. A missing or
                // recovery-stripped preview must never turn Play into an expensive new
                // random generation — especially because that also changes the seed the
                // designer believed they were testing. Player builds retain the normal
                // generate-on-start fallback below.
                Debug.LogWarning("[TrackGenerator] Play Mode found no complete cached track preview, so automatic editor regeneration was skipped. Exit Play Mode and use 'Generate New Track'.");
#else
                GenerateTrack();
#endif
            }

            if (placeHovercraftOnStart)
            {
                PlaceHovercraftAtTrackStart();
            }
        }

        private void Update()
        {
            if (_pendingStartPlacement)
            {
                if (TryPlaceHovercraftAtTrackStart())
                {
                    _pendingStartPlacement = false;
                    _startPlacementFailureLogged = false;
                }
                else if (Time.unscaledTime >= _startPlacementRetryDeadline)
                {
                    _pendingStartPlacement = false;
                    if (!_startPlacementFailureLogged)
                    {
                        Debug.LogError("[TrackGenerator] Hovercraft placement could not resolve a valid cached track start after retrying. Regenerate or restore the track preview before driving.");
                        _startPlacementFailureLogged = true;
                    }
                }
            }

            if (resetHovercraftWithBackspace && Keyboard.current != null &&
                Keyboard.current.backspaceKey.wasPressedThisFrame)
            {
                PlaceHovercraftAtTrackStart();
            }
        }

        // ─────────────────────────── Generation ───────────────────────────

        /// <summary>Full generation: every seed stream re-derives from the (new or fixed) master seed.</summary>
        [ContextMenu("Generate Track")]
        public void GenerateTrack()
        {
            GenerateInternal("Generate", deriveStreamsFromMaster: true, changedStreams: null);
        }

        /// <summary>New master seed + all streams: a completely fresh track.</summary>
        [ContextMenu("Generate New Everything")]
        public void GenerateNewEverything()
        {
            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            _seedManager.UseRandomSeed = true;
            GenerateInternal("Generate New Everything", deriveStreamsFromMaster: true, changedStreams: null);
        }

        /// <summary>Deterministic re-run of the current master seed and settings.</summary>
        [ContextMenu("Regenerate Same Settings")]
        public void RegenerateSameSettings()
        {
            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            _seedManager.UseRandomSeed = false;
            GenerateInternal("Regenerate Same Settings", deriveStreamsFromMaster: true, changedStreams: null);
        }

        /// <summary>Keeps the layout skeleton (Layout + Quarter streams); rolls features, elevation, surface, visuals.</summary>
        [ContextMenu("Same Layout, New Content")]
        public void SameLayoutNewContent()
            => PartialRegenerate("Same Layout, New Content",
                SeedStream.Feature, SeedStream.Elevation, SeedStream.Surface, SeedStream.Visual);

        /// <summary>Keeps every generation stream; rolls only surface and visual treatment.</summary>
        [ContextMenu("Same Geometry, New Surface")]
        public void SameGeometryNewSurface()
            => PartialRegenerate("Same Geometry, New Surface", SeedStream.Surface, SeedStream.Visual);

        /// <summary>Rolls only the feature stream (which features fill the gaps and their parameters).</summary>
        [ContextMenu("New Features Only")]
        public void NewFeaturesOnly()
            => PartialRegenerate("New Features Only", SeedStream.Feature);

        /// <summary>Rolls only the quarter stream (dual-quarter selection and alternate road content).</summary>
        [ContextMenu("New Quarter Content Only")]
        public void NewQuarterContentOnly()
            => PartialRegenerate("New Quarter Content Only", SeedStream.Quarter);

        /// <summary>Rolls only the visual stream (marker phasing/decoration — geometry untouched).</summary>
        [ContextMenu("New Visuals Only")]
        public void NewVisualsOnly()
            => PartialRegenerate("New Visuals Only", SeedStream.Visual);

        /// <summary>
        /// Randomizes every stream the LAYOUT LOCK allows: Unlocked = everything,
        /// Skeleton = keep Layout+Quarter, Geometry = only Surface+Visual.
        /// </summary>
        [ContextMenu("Randomize All Unlocked")]
        public void RandomizeAllUnlocked()
        {
            switch (layoutLockMode)
            {
                case LayoutLockMode.Skeleton:
                    PartialRegenerate("Randomize All Unlocked (Skeleton lock)",
                        SeedStream.Feature, SeedStream.Elevation, SeedStream.Surface, SeedStream.Visual);
                    break;
                case LayoutLockMode.Geometry:
                    PartialRegenerate("Randomize All Unlocked (Geometry lock)",
                        SeedStream.Surface, SeedStream.Visual);
                    break;
                default:
                    GenerateNewEverything();
                    break;
            }
        }

        private void PartialRegenerate(string command, params SeedStream[] randomize)
        {
            EnsureStreamsInitialized();
            seedStreams.Randomize(randomize);
            GenerateInternal(command, deriveStreamsFromMaster: false, changedStreams: randomize);
        }

        /// <summary>Streams of a freshly added component derive from the active/master seed once.</summary>
        private void EnsureStreamsInitialized()
        {
            seedStreams ??= new TrackSeedStreams();
            bool uninitialized = seedStreams.LayoutSeed == 0 && seedStreams.FeatureSeed == 0 &&
                                 seedStreams.ElevationSeed == 0 && seedStreams.QuarterSeed == 0;
            if (!uninitialized) return;

            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            int master = _seedManager.GetActiveSeed()?.BaseSeed ?? _seedManager.CurrentSeedInput;
            seedStreams.DeriveAllFrom(master);
        }

        private void GenerateInternal(string command, bool deriveStreamsFromMaster, SeedStream[] changedStreams)
        {
            if (_isGenerating)
            {
                Debug.LogWarning("[TrackGenerator] Ignored a second generation request while a track is already being built.");
                return;
            }

            _isGenerating = true;
            try
            {
                // Repair state left by a domain reload, interrupted generation, or an
                // older generator version before allocating another full track.
                ReconcileGeneratedTrackRoots(removeDuplicates: true);
                GenerateInternalCore(command, deriveStreamsFromMaster, changedStreams);
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private void GenerateInternalCore(string command, bool deriveStreamsFromMaster, SeedStream[] changedStreams)
        {
            if (Config == null)
            {
                Debug.LogError("[TrackGenerator] TrackConfig rulebook is missing.");
                return;
            }

            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();

            TrackSeed seed;
            if (deriveStreamsFromMaster)
            {
                seed = _seedManager.InitializeSeed();
                (seedStreams ??= new TrackSeedStreams()).DeriveAllFrom(seed.BaseSeed);
            }
            else
            {
                seed = _seedManager.GetActiveSeed() ?? _seedManager.InitializeSeed();
            }

            Designer ??= TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
            Designer.Sanitize();

            TrackGenerationResult result = RunPipelineWithPolicy(seed);

            // Provenance: what this run preserved/changed and under which locks.
            result.Report.RegenerationCommand = command;
            result.Report.LayoutLockMode = layoutLockMode.ToString();
            result.Report.LockedSettingsGroups = settingsLocks?.LockedGroupNames() ?? new List<string>();
            var allStreams = new[] { SeedStream.Layout, SeedStream.Feature, SeedStream.Elevation,
                SeedStream.Quarter, SeedStream.Surface, SeedStream.Visual };
            foreach (var s in allStreams)
            {
                bool changed = deriveStreamsFromMaster ||
                    (changedStreams != null && System.Array.IndexOf(changedStreams, s) >= 0);
                (changed ? result.Report.ChangedStreams : result.Report.PreservedStreams).Add(s.ToString());
            }

            lastReport = result.Report;
            lastMetrics = result.Layout?.Metrics ?? new TrackGenerationMetrics();
            unchecked { generationRevision++; }

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
            TrackGenerationResult result = pipeline.Run(resolved, seed, seedStreams);
            if (result.Success) return result;

            switch (Designer.Generation.FailurePolicy)
            {
                case GenerationFailurePolicy.RelaxOptionalSettings:
                {
                    TrackDesignerSettings relaxed = Designer.Clone();
                    var records = RelaxOptionalSettings(relaxed);
                    var relaxedResolved = ResolvedTrackGenerationConfig.Resolve(Config, relaxed);
                    TrackGenerationResult retry = pipeline.Run(relaxedResolved, seed, seedStreams);

                    // Merge failure history so the report shows the whole story.
                    retry.Report.PrependFailuresFrom(result.Report);
                    retry.Report.RelaxedSettings.AddRange(records);
                    retry.Report.AttemptsEvaluated += result.Report.AttemptsEvaluated;
                    return retry;
                }

                case GenerationFailurePolicy.UseSimpleTemplate:
                {
                    TrackDesignerSettings template = BuildTemplateSettings();
                    var templateResolved = ResolvedTrackGenerationConfig.Resolve(Config, template);
                    TrackGenerationResult retry = pipeline.Run(templateResolved, seed, seedStreams);

                    retry.Report.PrependFailuresFrom(result.Report);
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

            if (s.Quarters.MaximumDualQuarterCount > s.Quarters.MinimumDualQuarterCount)
            {
                records.Add(new RelaxedSettingRecord
                {
                    SettingName = "Quarters.MaximumDualQuarterCount",
                    OriginalValue = s.Quarters.MaximumDualQuarterCount,
                    RelaxedValue = s.Quarters.MinimumDualQuarterCount
                });
                s.Quarters.MaximumDualQuarterCount = s.Quarters.MinimumDualQuarterCount;
            }

            s.Sanitize();
            return records;
        }

        /// <summary>A minimal circuit request that still respects the rulebook's allowed features (no features, no dual quarters).</summary>
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
            t.Quarters.MinimumDualQuarterCount = 0;
            t.Quarters.MaximumDualQuarterCount = 0;
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

            // The generated track is a SCENE-ROOT SIBLING linked by the trackRoot
            // reference — NEVER a child of the generator. Selecting a GameObject makes
            // the editor's inspector machinery (preview footer, header aggregation)
            // work over its child hierarchy every repaint; hanging a multi-million-
            // vertex track under the generator made selecting it freeze the editor.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(tempRootObj, gameObject.scene);
            tempRootObj.transform.SetPositionAndRotation(transform.position, transform.rotation);
            tempRootObj.transform.localScale = transform.lossyScale;

            try
            {
                ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(Config, Designer);

                var prismBuilder = new BoxPrismTrackMeshBuilder(resolved.RoadProfile);
                prismBuilder.Build(layout.Sections, MainRoadMaterial, WallMaterial, tempRootObj.transform);

                // Guidance visuals: center-flat guide lines + wall marker bands
                // (non-colliding overlay meshes; break only at air gaps/open edges).
                TrackGuideMarkingBuilder.Build(layout.Sections, resolved.RoadProfile, Designer.Visual,
                    tempRootObj.transform, RoadLineMaterial);

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
                visualizer.Level = TrackDebugVisualizationLevel.Off;

                CreateStartAnchor(tempRootObj.transform, layout.Sections[0].StartFrame);

                if (buildRaceCourse)
                {
                    RaceCourse course = RaceCourseBuilder.Build(tempRootObj.transform, layout, resolved.RoadProfile,
                        checkpointCount, startLineArcOffset, StartFinishMaterial,
                        StartGatePillarMaterial, CheckpointMaterial);
                    if (course == null)
                    {
                        result.Report.AddFailure(-1, GenerationFailureReason.RaceCourseBuildFailure, "RaceCourse",
                            "Race course build failed — track rejected (transactional).");
                        result.Success = false;
                        DestroyObject(tempRootObj);
                        return false;
                    }
                }

                // Commit: release every old/orphaned preview, not only the serialized
                // reference. Editor recovery can lose that reference while the scene
                // object remains alive.
                DestroyGeneratedTrackRootsExcept(tempRootObj);

                tempRootObj.name = $"GeneratedTrack_{seed.BaseSeed}";
                trackRoot = tempRootObj.transform;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    MarkGeneratedHierarchyTransient(tempRootObj);
#endif

                CurrentMacroSections = layout.Sections;
                CurrentLayout = layout;
                CacheStartFrame(tempRootObj.transform, layout.Sections[0].StartFrame);
                RepairGeneratedTrackState(tempRootObj);
                UpdateGeneratedMeshStats();
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    EditorPreviewBuilt?.Invoke(this);
#endif
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

        /// <summary>Writes the full text diagnostic report next to the project (see <see cref="TrackDebugReportExporter"/>).</summary>
        [ContextMenu("Export Debug Report")]
        public void ExportDebugReport()
        {
            TrackDebugReportExporter.Export(this);
        }

        /// <summary>Destroys all generated track previews while preserving settings and seeds.</summary>
        [ContextMenu("Clear Track")]
        public void ClearTrack()
        {
            DestroyGeneratedTrackRootsExcept(null);
            trackRoot = null;
            CurrentMacroSections = null;
            CurrentLayout = null;
            UpdateGeneratedMeshStats();
#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorPreviewCleared?.Invoke(this);
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Produces the exact lightweight layout needed to rebuild the editor preview.
        /// CurrentLayout is runtime-only and can be cleared by a script/domain reload
        /// while the visible DontSaveInEditor hierarchy remains alive. In that case the
        /// serialized debug visualizer is the authoritative recovery source.
        /// </summary>
        public bool TryGetEditorPreviewCacheSnapshot(out int seedValue, out GeneratedTrackLayout layout)
        {
            seedValue = 0;
            layout = null;

            ReconcileGeneratedTrackRoots(removeDuplicates: true);

            if (CurrentLayout?.Sections != null && CurrentLayout.Sections.Count > 0)
            {
                layout = CurrentLayout;
                MacroTrackDebugVisualizer currentVisualizer = trackRoot != null
                    ? trackRoot.GetComponent<MacroTrackDebugVisualizer>()
                    : null;
                seedValue = currentVisualizer != null
                    ? currentVisualizer.Seed
                    : (_seedManager ??= GetComponent<TrackSeedManager>()).CurrentSeedInput;
                return true;
            }

            if (trackRoot == null)
                return false;

            MacroTrackDebugVisualizer visualizer = trackRoot.GetComponent<MacroTrackDebugVisualizer>();
            if (visualizer?.Sections == null || visualizer.Sections.Count == 0)
                return false;

            layout = new GeneratedTrackLayout
            {
                Sections = visualizer.Sections,
                Quarters = visualizer.Quarters ?? new List<GeneratedTrackQuarter>(),
                LapLength = MacroTrackSampler.GetTotalLength(visualizer.Sections),
                EstimatedNeutralLapTime = lastMetrics != null
                    ? lastMetrics.EstimatedNeutralLapTimeSeconds
                    : 0f,
                Metrics = lastMetrics ?? new TrackGenerationMetrics()
            };
            seedValue = visualizer.Seed;
            return true;
        }

        /// <summary>
        /// Rebuilds only the render/collider preview from an already-generated layout.
        /// This is used after Unity restores its Play Mode scene backup: the transient
        /// mesh hierarchy is intentionally absent from that backup, but the layout cache
        /// remains alive while Domain Reload is disabled. No procedural planning, seed
        /// randomization, candidate search, or report replacement occurs here.
        /// </summary>
        public bool RestoreEditorPreviewFromCache(int seedValue, GeneratedTrackLayout cachedLayout,
            bool forceRebuild = false)
        {
            if (cachedLayout?.Sections == null || cachedLayout.Sections.Count == 0)
                return false;

            ReconcileGeneratedTrackRoots(removeDuplicates: true);
            if (!forceRebuild && TryAdoptExistingTrack())
                return true;

            var cachedResult = new TrackGenerationResult
            {
                Success = true,
                RequestedSeed = seedValue,
                Layout = cachedLayout,
                Report = new TrackGenerationReport()
            };

            bool restored = TryBuildTransactional(TrackSeed.CreateNew(seedValue), cachedResult);
            if (!restored)
            {
                Debug.LogWarning("[TrackGenerator] The cached track layout could not be restored. Use 'Regenerate Same Seed' to rebuild it.");
                return false;
            }

            // Preserve the original generation report and revision. This operation only
            // restores transient scene objects; it is not a new generation result.
            Debug.Log($"[TrackGenerator] Restored cached track preview (seed {seedValue}, {cachedLayout.Sections.Count} sections) without rerunning generation.");
            if (Application.isPlaying && placeHovercraftOnStart)
                PlaceHovercraftAtTrackStart();
            return true;
        }
#endif

        /// <summary>
        /// Removes abandoned pending builds and duplicate preview roots. If the serialized
        /// reference was lost, the newest complete preview is adopted before older copies
        /// are released. Safe to call repeatedly.
        /// </summary>
        [ContextMenu("Clean Stale Generated Tracks")]
        public void CleanStaleGeneratedTracks()
        {
            ReconcileGeneratedTrackRoots(removeDuplicates: true);
            UpdateGeneratedMeshStats();
        }

        private static void DestroyObject(GameObject obj)
        {
            if (obj == null) return;
            ReleaseGeneratedMeshes(obj);
            if (Application.isPlaying)
            {
                obj.SetActive(false);
                Destroy(obj);
            }
            else DestroyImmediate(obj);
        }

        /// <summary>
        /// Frees the procedurally built meshes under <paramref name="root"/> before the
        /// hierarchy goes away.
        ///
        /// Destroying a GameObject does NOT destroy the Mesh its MeshFilter/MeshCollider
        /// points at: a mesh built with `new Mesh()` is a UnityEngine.Object that lives
        /// until something destroys it or the scene reloads. A generate → clear → generate
        /// loop would therefore strand a full track's worth of geometry (hundreds of MB)
        /// every cycle — and now that Enter Play Mode keeps the domain and scene alive,
        /// nothing sweeps those orphans up between sessions any more.
        ///
        /// Meshes saved as project assets are SKIPPED. They are shared, they belong to
        /// the AssetDatabase, and destroying one would delete it from disk.
        /// </summary>
        private static void ReleaseGeneratedMeshes(GameObject root)
        {
            if (root == null) return;

            // MeshFilter and MeshCollider commonly point to the same runtime Mesh.
            // Gather first so a shared mesh is destroyed exactly once.
            var meshes = new HashSet<UnityEngine.Mesh>();

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                    meshes.Add(filter.sharedMesh);
                filter.sharedMesh = null;
            }

            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh != null)
                    meshes.Add(collider.sharedMesh);
                collider.sharedMesh = null;
            }

            foreach (UnityEngine.Mesh mesh in meshes)
                ReleaseMesh(mesh);
        }

        private List<GameObject> FindGeneratedTrackRoots()
        {
            var roots = new List<GameObject>();
            var seen = new HashSet<GameObject>();

            void AddIfGeneratedRoot(GameObject candidate)
            {
                if (candidate == null || candidate == gameObject) return;
                if (!candidate.name.StartsWith(GeneratedTrackRootPrefix, System.StringComparison.Ordinal)) return;

#if UNITY_EDITOR
                // Resources.FindObjectsOfTypeAll also returns prefab/assets from the
                // AssetDatabase. Those are not live previews and must never be destroyed.
                if (UnityEditor.EditorUtility.IsPersistent(candidate)) return;
#endif

                // Do not clean previews owned by a TrackGenerator in another additive
                // scene. A DontSaveInEditor preview may temporarily have an invalid scene,
                // so those candidates still belong in this recovery scan.
                if (candidate.scene.IsValid() && gameObject.scene.IsValid() &&
                    candidate.scene != gameObject.scene)
                    return;

                if (seen.Add(candidate))
                    roots.Add(candidate);
            }

            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    AddIfGeneratedRoot(root);
            }

            // DontSaveInEditor objects can remain alive, rendered and collidable while
            // Unity omits them from Scene.GetRootGameObjects(). This was the source of
            // stacked "ghost" tracks after Generate New Track. Scan all live objects so
            // transactional replacement can release those hidden cached previews too.
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                    AddIfGeneratedRoot(candidate);
            }
            else
#endif
            {
                foreach (GameObject candidate in
                         UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                    AddIfGeneratedRoot(candidate);
            }

            // Preserve awareness of the explicitly referenced root even if Unity is in
            // the middle of changing its HideFlags/scene registration.
            if (trackRoot != null)
                AddIfGeneratedRoot(trackRoot.gameObject);

            return roots;
        }

        private static bool IsPendingTrack(GameObject root)
            => root != null && root.name.EndsWith(PendingTrackSuffix, System.StringComparison.Ordinal);

        private static bool HasGeneratedMesh(GameObject root)
        {
            if (root == null) return false;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0)
                    return true;
            return false;
        }

        private static GameObject SelectNewestCompleteTrack(List<GameObject> roots)
        {
            GameObject selected = null;
            int highestSibling = int.MinValue;
            foreach (GameObject root in roots)
            {
                if (root == null || IsPendingTrack(root) || !HasGeneratedMesh(root)) continue;
                int sibling = root.transform.GetSiblingIndex();
                if (selected == null || sibling >= highestSibling)
                {
                    selected = root;
                    highestSibling = sibling;
                }
            }
            return selected;
        }

        private void ReconcileGeneratedTrackRoots(bool removeDuplicates)
        {
            List<GameObject> roots = FindGeneratedTrackRoots();
            GameObject selected = trackRoot != null && roots.Contains(trackRoot.gameObject) &&
                                  !IsPendingTrack(trackRoot.gameObject) && HasGeneratedMesh(trackRoot.gameObject)
                ? trackRoot.gameObject
                : SelectNewestCompleteTrack(roots);

            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                DisableDebugVisualization(root);

                if (IsPendingTrack(root) || (removeDuplicates && root != selected))
                    DestroyObject(root);
            }

            trackRoot = selected != null ? selected.transform : null;

#if UNITY_EDITOR
            if (!Application.isPlaying && selected != null)
            {
                RepairGeneratedTrackState(selected);
                MarkGeneratedHierarchyTransient(selected);
            }
            else if (selected != null)
                RepairGeneratedTrackState(selected);
#else
            if (selected != null)
                RepairGeneratedTrackState(selected);
#endif
        }

        private void RepairGeneratedTrackState(GameObject root)
        {
            if (root == null) return;
            RepairRoadMaterials(root);
            RepairGuideLineMaterial(root);
            RepairRaceCourse(root);
            EnsureStartAnchor(root.transform);
        }

        private void RepairRoadMaterials(GameObject root)
        {
            if (root == null || (MainRoadMaterial == null && WallMaterial == null)) return;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform parent = renderer.transform.parent;
                if (parent == null || !parent.name.StartsWith("Track_", System.StringComparison.Ordinal) ||
                    !renderer.name.StartsWith("LOD", System.StringComparison.Ordinal)) continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length < 2) materials = new Material[2];
                if (MainRoadMaterial != null) materials[0] = MainRoadMaterial;
                if (WallMaterial != null) materials[1] = WallMaterial;
                renderer.sharedMaterials = materials;
            }
        }

        private void RepairGuideLineMaterial(GameObject root)
        {
            if (root == null || RoadLineMaterial == null) return;
            Transform markingRoot = root.transform.Find("TrackGuideMarkings");
            if (markingRoot == null) return;

            foreach (MeshRenderer renderer in markingRoot.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterial = RoadLineMaterial;
        }

        private void RepairRaceCourse(GameObject root)
        {
            if (root == null) return;
            RaceCourse course = root.GetComponentInChildren<RaceCourse>(true);
            if (course != null)
                course.RepairGeneratedReferences();

            foreach (RaceGate gate in root.GetComponentsInChildren<RaceGate>(true))
            {
                if (gate == null) continue;
                foreach (MeshRenderer renderer in gate.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material material;
                    if (!gate.IsStartFinish)
                        material = CheckpointMaterial;
                    else if (renderer.name.StartsWith("Pillar_", System.StringComparison.Ordinal))
                        material = StartGatePillarMaterial;
                    else
                        material = StartFinishMaterial;

                    if (material != null)
                        renderer.sharedMaterial = material;
                }
            }
        }

        private void DestroyGeneratedTrackRootsExcept(GameObject keep)
        {
            List<GameObject> roots = FindGeneratedTrackRoots();
            foreach (GameObject root in roots)
            {
                if (root == null || root == keep) continue;
                DestroyObject(root);
            }
        }

        private static void DisableDebugVisualization(GameObject root)
        {
            if (root == null) return;
            MacroTrackDebugVisualizer visualizer = root.GetComponent<MacroTrackDebugVisualizer>();
            if (visualizer != null)
                visualizer.Level = TrackDebugVisualizationLevel.Off;
        }

#if UNITY_EDITOR
        private static void MarkGeneratedHierarchyTransient(GameObject root)
        {
            if (root == null) return;

            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                item.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
                foreach (Component component in item.GetComponents<Component>())
                {
                    if (component != null)
                        component.hideFlags |= HideFlags.DontSaveInEditor;
                }
            }

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null &&
                    !UnityEditor.EditorUtility.IsPersistent(filter.sharedMesh))
                    filter.sharedMesh.hideFlags |= HideFlags.DontSaveInEditor;
            }

            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh != null &&
                    !UnityEditor.EditorUtility.IsPersistent(collider.sharedMesh))
                    collider.sharedMesh.hideFlags |= HideFlags.DontSaveInEditor;
            }
        }
#endif

        private static void ReleaseMesh(UnityEngine.Mesh mesh)
        {
            if (mesh == null) return;
#if UNITY_EDITOR
            // An asset-backed mesh is owned by the project, not by this track.
            if (UnityEditor.EditorUtility.IsPersistent(mesh)) return;
#endif
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        // ─────────────────────────── Adoption / spawn ───────────────────────────

        /// <summary>
        /// Adopts a track generated in the editor before play mode (the section list is
        /// serialized on the debug visualizer, so runtime references restore losslessly).
        /// </summary>
        private bool TryAdoptExistingTrack()
        {
            ReconcileGeneratedTrackRoots(removeDuplicates: true);
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
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    MarkGeneratedHierarchyTransient(trackRoot.gameObject);
#endif
                CurrentMacroSections = visualizer.Sections;
                RepairGeneratedTrackState(trackRoot.gameObject);
                UpdateGeneratedMeshStats();
                Debug.Log($"[TrackGenerator] Keeping editor-generated track (seed {visualizer.Seed}, {visualizer.Sections.Count} sections).");
                return true;
            }

            // Section records power diagnostics, but they are not required to render,
            // collide with, or race on an already-built preview. Recovery and hot reload
            // can strip that optional list while leaving every mesh and RaceCourse object
            // intact. Treat the complete mesh hierarchy as the authoritative cache rather
            // than throwing it away and generating an unrelated road.
            CurrentMacroSections = visualizer != null ? visualizer.Sections : null;
            CurrentLayout = null;
            RepairGeneratedTrackState(trackRoot.gameObject);
            UpdateGeneratedMeshStats();
            Debug.LogWarning("[TrackGenerator] Keeping the existing editor-generated track. Its optional section diagnostics were unavailable, but the playable mesh cache is complete.");
            return true;
        }

        [ContextMenu("Place Hovercraft At Track Start")]
        public void PlaceHovercraftAtTrackStart()
        {
            if (TryPlaceHovercraftAtTrackStart())
            {
                _pendingStartPlacement = false;
                _startPlacementFailureLogged = false;
                return;
            }

            // Cache restoration and scene activation can finish after Start on the
            // first frame. Keep the request alive briefly rather than leaving the
            // craft at its unrelated scene-authored position in empty space.
            _pendingStartPlacement = true;
            _startPlacementRetryDeadline = Time.unscaledTime + 2f;
        }

        private bool TryPlaceHovercraftAtTrackStart()
        {
            // `??=` is a plain C# null check, so it cannot see a craft whose
            // GameObject has been destroyed — the interface reference stays
            // non-null and the cache goes stale. That matters now that Enter Play
            // Mode keeps the domain alive: this field outlives a single session,
            // so re-find whenever the cached craft has lost its transform.
            if (_craft == null || _craft.CraftTransform == null)
                _craft = TrackCraftLocator.FindCraft();

            if (_craft == null || _craft.CraftTransform == null)
            {
                return false;
            }

            if (!TryGetTrackStartFrame(out Vector3 position, out Vector3 forward, out Vector3 up))
            {
                return false;
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

            return true;
        }

        private bool TryGetTrackStartFrame(out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            Transform root = trackRoot != null ? trackRoot : transform;

            if (trackRoot != null)
            {
                Transform anchor = trackRoot.Find("TrackStartAnchor");
                if (anchor != null)
                {
                    position = anchor.position;
                    forward = anchor.forward;
                    up = anchor.up;
                    return true;
                }
            }

            MacroTrackDebugVisualizer visualizer = trackRoot != null
                ? trackRoot.GetComponent<MacroTrackDebugVisualizer>()
                : null;
            List<GeneratedTrackSection> ownedSections = visualizer != null ? visualizer.Sections : null;
            if (ownedSections != null && ownedSections.Count > 0)
            {
                TrackConnectionFrame start = ownedSections[0].StartFrame;
                position = root.TransformPoint(start.Position);
                forward = root.TransformDirection(start.Forward);
                up = root.TransformDirection(start.Up);
                return true;
            }

            // A recovered mesh-only preview can lose its optional section diagnostics.
            // The race gate still provides a stable, driveable frame instead of leaving
            // the craft at an unrelated scene position.
            if (trackRoot != null)
            {
                RaceCourse course = trackRoot.GetComponentInChildren<RaceCourse>(true);
                if (course != null)
                    course.RepairGeneratedReferences();

                RaceGate startGate = course != null ? course.StartFinishGate : null;
                if (startGate == null)
                {
                    foreach (RaceGate candidate in trackRoot.GetComponentsInChildren<RaceGate>(true))
                    {
                        if (candidate != null && candidate.IsStartFinish)
                        {
                            startGate = candidate;
                            break;
                        }
                    }
                }

                if (startGate != null)
                {
                    Transform gate = startGate.transform;
                    position = gate.position;
                    forward = gate.forward;
                    up = gate.up;
                    return true;
                }

                // The serialized generator-owned frame is deliberately last: only use
                // it while a complete cached mesh is present, never as permission to
                // spawn into empty space when the preview itself is missing.
                if (cachedStartFrameValid && HasGeneratedMesh(trackRoot.gameObject))
                {
                    position = transform.TransformPoint(cachedStartLocalPosition);
                    forward = transform.TransformDirection(cachedStartLocalForward);
                    up = transform.TransformDirection(cachedStartLocalUp);
                    return true;
                }
            }

            position = Vector3.zero;
            forward = Vector3.forward;
            up = Vector3.up;
            return false;
        }

        private void CreateStartAnchor(Transform root, TrackConnectionFrame frame)
        {
            if (root == null) return;
            Transform existing = root.Find("TrackStartAnchor");
            Transform anchor;
            if (existing != null)
            {
                anchor = existing;
            }
            else
            {
                GameObject anchorObject = new GameObject("TrackStartAnchor");
                anchorObject.transform.SetParent(root, false);
                anchor = anchorObject.transform;
            }

            anchor.localPosition = frame.Position;
            Vector3 frameUp = frame.Up.sqrMagnitude > 0.001f ? frame.Up.normalized : Vector3.up;
            Vector3 frameForward = Vector3.ProjectOnPlane(frame.Forward, frameUp);
            frameForward = frameForward.sqrMagnitude > 0.001f ? frameForward.normalized : Vector3.forward;
            anchor.localRotation = Quaternion.LookRotation(frameForward, frameUp);
        }

        private void CacheStartFrame(Transform root, TrackConnectionFrame frame)
        {
            if (root == null) return;
            cachedStartLocalPosition = transform.InverseTransformPoint(root.TransformPoint(frame.Position));
            cachedStartLocalForward = transform.InverseTransformDirection(root.TransformDirection(frame.Forward)).normalized;
            cachedStartLocalUp = transform.InverseTransformDirection(root.TransformDirection(frame.Up)).normalized;
            cachedStartFrameValid = cachedStartLocalForward.sqrMagnitude > 0.001f &&
                                    cachedStartLocalUp.sqrMagnitude > 0.001f;
        }

        private void EnsureStartAnchor(Transform root)
        {
            if (root == null || root.Find("TrackStartAnchor") != null) return;

            // Always prefer metadata belonging to THIS hierarchy. With domain reload
            // disabled, CurrentMacroSections can still point at the previous track
            // during the first adoption pass.
            MacroTrackDebugVisualizer visualizer = root.GetComponent<MacroTrackDebugVisualizer>();
            List<GeneratedTrackSection> sections = visualizer != null ? visualizer.Sections : null;

            if (sections != null && sections.Count > 0)
            {
                CreateStartAnchor(root, sections[0].StartFrame);
                CacheStartFrame(root, sections[0].StartFrame);
                return;
            }

            RaceGate startGate = null;
            foreach (RaceGate gate in root.GetComponentsInChildren<RaceGate>(true))
            {
                if (gate != null && gate.IsStartFinish)
                {
                    startGate = gate;
                    break;
                }
            }

            GameObject anchorObject = new GameObject("TrackStartAnchor");
            anchorObject.transform.SetParent(root, false);

            if (startGate != null)
            {
                anchorObject.transform.SetPositionAndRotation(startGate.transform.position, startGate.transform.rotation);
            }
            else if (cachedStartFrameValid && HasGeneratedMesh(root.gameObject))
            {
                anchorObject.transform.SetPositionAndRotation(
                    transform.TransformPoint(cachedStartLocalPosition),
                    Quaternion.LookRotation(transform.TransformDirection(cachedStartLocalForward),
                        transform.TransformDirection(cachedStartLocalUp)));
            }
            else
            {
                DestroyObject(anchorObject);
            }
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
                $"dual quarters {m.DualRoadQuarterCount} | elevation {m.MinElevation:F0}..{m.MaxElevation:F0}m | rings {m.TotalRings}.");

            foreach (var b in result.Report.QuarterBalance)
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
