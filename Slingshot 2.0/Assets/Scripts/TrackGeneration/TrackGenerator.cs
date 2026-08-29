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
        [Tooltip("Authoritative persistent materials for generated roads, markings and gameplay surfaces. Legacy fields below remain as migration fallbacks.")]
        public TrackMaterialSet MaterialSet;
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

        [SerializeField, HideInInspector] private GenerationRecipeV1 lastAcceptedRecipe;
        [SerializeField, HideInInspector] private TrackResultManifest lastResultManifest;
        [SerializeField, HideInInspector] private GenerationRecipeV1 pendingImportedRecipe;
        [SerializeField, HideInInspector] private bool pendingRecipeAwaitingReplay;
        [System.NonSerialized] private TrackRecipeRating inspectorRatingFallback;

        // The latest report may describe a rejected generation attempt. Keep the
        // editor's topology index tied to the last transactionally accepted track so
        // a failed replacement cannot make the still-visible track uneditable.
        [SerializeField, HideInInspector]
        private List<TopologySlotRecord> lastAcceptedTopologySlots = new List<TopologySlotRecord>();

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

        /// <summary>Whether accepted tracks include start/finish and checkpoint geometry.</summary>
        public bool BuildsRaceCourse => buildRaceCourse;

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

        /// <summary>Complete recipe that produced the last transactionally accepted track.</summary>
        public GenerationRecipeV1 LastAcceptedRecipe => lastAcceptedRecipe;

        /// <summary>The imported recipe awaiting replay, or the accepted recipe currently on screen.</summary>
        public GenerationRecipeV1 DisplayedRecipe =>
            pendingRecipeAwaitingReplay && pendingImportedRecipe != null
                ? pendingImportedRecipe
                : lastAcceptedRecipe;

        /// <summary>
        /// Rating associated with the recipe shown by the Inspector. This getter is kept
        /// repaint-cheap: the accepted recipe normally owns the complete rating, while a
        /// one-time report fallback covers an older serialized snapshot without hashing
        /// or mutating recipe data during GUI repaint.
        /// </summary>
        public TrackRecipeRating DisplayedRecipeRating
        {
            get
            {
                // A deliberately imported recipe owns the card until it is replayed,
                // including the honest "Unrated" state of an older recipe. A stale
                // serialized pending object is ignored unless the explicit replay flag
                // says the user actually loaded it.
                if (pendingRecipeAwaitingReplay && pendingImportedRecipe != null)
                    return pendingImportedRecipe.Rating;

                TrackRecipeRating accepted = lastAcceptedRecipe?.Rating;
                if (accepted != null && accepted.IsRated)
                    return accepted;

                if (lastReport != null && lastReport.Success && lastMetrics != null)
                {
                    string layoutHash = lastResultManifest?.CanonicalLayoutHash ?? "";
                    if (inspectorRatingFallback == null ||
                        inspectorRatingFallback.Overall != lastReport.TrackRating ||
                        !string.Equals(inspectorRatingFallback.RatedLayoutHash, layoutHash,
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        TrackDesignerSettings settings =
                            lastAcceptedRecipe?.DeserializeDesignerSettings() ?? Designer;
                        inspectorRatingFallback = TrackRecipeRatingCalculator.Calculate(
                            lastMetrics, lastReport, settings, layoutHash);
                        if (lastReport.TrackRating > 0)
                            inspectorRatingFallback.Overall = lastReport.TrackRating;
                    }
                    if (inspectorRatingFallback != null && inspectorRatingFallback.IsRated)
                        return inspectorRatingFallback;
                }

                return accepted;
            }
        }

        /// <summary>
        /// Stable topology index for the scene track that is actually accepted and
        /// visible. Unlike <see cref="LastReport"/>, this does not switch to a failed
        /// attempt merely because that attempt is useful for diagnostics.
        /// </summary>
        public List<TopologySlotRecord> EditableTopologySlots
        {
            get
            {
                if (lastAcceptedTopologySlots != null && lastAcceptedTopologySlots.Count > 0)
                    return lastAcceptedTopologySlots;

                // Migration path for scenes saved before the accepted index had its
                // own serialized field.
                if (lastReport != null && lastReport.Success &&
                    lastReport.TopologySlots != null && lastReport.TopologySlots.Count > 0)
                    return lastReport.TopologySlots;

                return null;
            }
        }

        /// <summary>Verified identity and canonical hashes of the last accepted track.</summary>
        public TrackResultManifest LastResultManifest => lastResultManifest;

        private TrackSeedManager _seedManager;
        private ITrackRaceCraft _craft;
        private bool _isGenerating;
        private bool _pendingStartPlacement;
        private float _startPlacementRetryDeadline;
        private bool _startPlacementFailureLogged;

        private sealed class AuthoringRunContext
        {
            public int BaselineAttemptIndex = -1;
            public float BaselineLapLength;
            public IReadOnlyList<AuthoringElevationBaselineEntry> ElevationBaseline;
        }

        private const string GeneratedTrackRootPrefix = "GeneratedTrack_";
        private const string PendingTrackSuffix = "_pending";

        private void Awake()
        {
            _seedManager = GetComponent<TrackSeedManager>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureAcceptedRecipeRating();

            // Procedural track meshes can contain several gigabytes of vertex/index data.
            // They are reproducible from the saved seed and settings, so keep them in the
            // editor scene for preview and play, but never embed them in the .unity file.
            if (!Application.isPlaying && trackRoot != null)
            {
                RestoreDebugAnnotations(trackRoot.gameObject);
                RestoreGeneratedTrackState(trackRoot.gameObject);
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
                RestoreDebugAnnotations(root);
                RestoreGeneratedTrackState(root);
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
            pendingImportedRecipe = null;
            pendingRecipeAwaitingReplay = false;
            GenerateInternal("Generate", deriveStreamsFromMaster: true, changedStreams: null);
        }

        /// <summary>New master seed + all streams: a completely fresh track.</summary>
        [ContextMenu("Generate New Everything")]
        public void GenerateNewEverything()
        {
            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            pendingImportedRecipe = null;
            pendingRecipeAwaitingReplay = false;
            _seedManager.UseRandomSeed = true;
            GenerateInternal("Generate New Everything", deriveStreamsFromMaster: true, changedStreams: null);
        }

        /// <summary>Deterministic re-run of the current master seed and settings.</summary>
        [ContextMenu("Regenerate Same Settings")]
        public void RegenerateSameSettings()
        {
            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            pendingImportedRecipe = null;
            pendingRecipeAwaitingReplay = false;
            _seedManager.UseRandomSeed = false;
            GenerateInternal("Regenerate Same Settings", deriveStreamsFromMaster: true, changedStreams: null);
        }

        /// <summary>
        /// Captures every current generation input as a versioned recipe. This is safe
        /// before generation; ExpectedLayoutHash remains empty until a track is accepted.
        /// </summary>
        public GenerationRecipeV1 CaptureGenerationRecipe()
        {
            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            EnsureStreamsInitialized();
            TrackSeed seed = _seedManager.GetActiveSeed() ??
                             _seedManager.ActivateSeed(_seedManager.CurrentSeedInput);
            Designer ??= TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);
            return GenerationRecipeV1.Capture(seed, seedStreams, Designer, Config,
                settingsLocks, layoutLockMode, transform);
        }

        /// <summary>Exports the last accepted exact recipe, or the current request when no accepted recipe exists.</summary>
        public string ExportGenerationRecipe(bool preferLastAccepted = true)
        {
            GenerationRecipeV1 recipe = preferLastAccepted && lastAcceptedRecipe != null
                ? lastAcceptedRecipe.Clone()
                : CaptureGenerationRecipe();
            EnsureRecipeRating(recipe, lastReport, lastMetrics,
                recipe?.ExpectedLayoutHash);

            // Upgrade an accepted authored recipe created by an older editor build at
            // the moment it becomes an Undo snapshot. The visible accepted layout is
            // authoritative and supplies the compact state missing from that recipe.
            // This makes the next successfully applied edit undoable even when the
            // starting track itself predates complete history serialization.
            if (preferLastAccepted && recipe != null && recipe.DesignerAuthoredVariant &&
                (recipe.AuthoringElevationBaseline == null ||
                 recipe.AuthoringElevationBaseline.Count == 0))
            {
                GeneratedTrackLayout acceptedLayout = CurrentLayout;
#if UNITY_EDITOR
                if (acceptedLayout?.Sections == null || acceptedLayout.Sections.Count == 0)
                    TryGetEditorPreviewCacheSnapshot(out _, out acceptedLayout);
#endif
                IReadOnlyList<AuthoringElevationBaselineEntry> baseline =
                    TrackTopologyPlanner.CaptureAuthoringElevationBaseline(
                        acceptedLayout?.Sections);
                if (baseline.Count > 0)
                {
                    recipe.AuthoringElevationBaseline =
                        CloneAuthoringElevationBaseline(baseline);
                    recipe.RefreshHashes();
                }
            }
            return GenerationRecipeV1.Serialize(recipe, true);
        }

        /// <summary>
        /// Validates and applies a recipe's deterministic inputs without generating.
        /// Call <see cref="RegenerateExactRecipe"/> after a successful import.
        /// </summary>
        public bool TryImportGenerationRecipe(string json, RecipeReplayMode replayMode, out string error)
        {
            GenerationRecipeV1 recipe = GenerationRecipeV1.Deserialize(json, out error);
            if (recipe == null) return false;
            if (!recipe.ValidateFor(Config, replayMode, out error)) return false;

            TrackDesignerSettings importedSettings = recipe.DeserializeDesignerSettings();
            if (importedSettings == null)
            {
                error = "The recipe's designer settings could not be restored.";
                return false;
            }

            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            _seedManager.ActivateSeed(recipe.BaseSeed, recipe.SeedDisplayName);
            Designer = importedSettings;
            seedStreams = recipe.SeedStreams?.Clone() ?? new TrackSeedStreams();
            settingsLocks = CloneSettingsLocks(recipe.SettingsLocks);
            layoutLockMode = recipe.LayoutLockMode;
            transform.SetPositionAndRotation(recipe.Origin?.Position ?? transform.position,
                recipe.Origin?.Rotation ?? transform.rotation);
            if (recipe.Origin != null) transform.localScale = recipe.Origin.Scale;
            pendingImportedRecipe = recipe.Clone();
            pendingRecipeAwaitingReplay = true;
            error = "";
            return true;
        }

        /// <summary>Generates from the exact imported recipe, or replays the last accepted recipe.</summary>
        public bool RegenerateExactRecipe(out string error)
        {
            GenerationRecipeV1 recipe = (pendingImportedRecipe ?? lastAcceptedRecipe)?.Clone();
            if (recipe == null)
            {
                error = "No imported or previously accepted Generation Recipe is available.";
                return false;
            }

            GenerationRecipeV1 currentAccepted = lastAcceptedRecipe?.Clone();
            List<TopologySlotRecord> currentSlots = CloneTopologySlots(EditableTopologySlots);
            string json = GenerationRecipeV1.Serialize(recipe, false);
            if (!TryImportGenerationRecipe(json, RecipeReplayMode.Strict, out error)) return false;

            // Loading the recipe that already owns the visible accepted layout is a
            // successful exact restore, not a reason to throw that layout back through
            // the authoring solver. This also upgrades older edited recipes whose saved
            // elevation snapshot described the accepted output rather than the original
            // authoring input: the canonical hash proves that the requested exact track
            // is already present and no geometry needs to be guessed or rebuilt.
            string acceptedLayoutHash = CurrentLayout?.Sections != null &&
                                        CurrentLayout.Sections.Count > 0
                ? TrackCanonicalHasher.ComputeLayoutHash(CurrentLayout)
                : lastResultManifest?.CanonicalLayoutHash;
            if (!string.IsNullOrWhiteSpace(recipe.ExpectedLayoutHash) &&
                string.Equals(recipe.ExpectedLayoutHash, acceptedLayoutHash,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                EnsureRecipeRating(recipe, lastReport, lastMetrics, acceptedLayoutHash);
                lastAcceptedRecipe = recipe;
                pendingImportedRecipe = null;
                pendingRecipeAwaitingReplay = false;
                if (lastResultManifest != null)
                    lastResultManifest.RecipeHash = recipe.RecipeHash;
                lastReport ??= new TrackGenerationReport();
                lastReport.Success = true;
                lastReport.TrackRating = recipe.Rating?.Overall ?? 0;
                lastReport.RegenerationCommand = "Regenerate Exact Recipe";
                lastReport.AcceptedPass = "Exact layout already accepted";
                lastReport.Failures?.Clear();
                unchecked { generationRevision++; }
                Debug.Log($"[TrackGenerator] Exact recipe already matches the accepted track " +
                          $"({acceptedLayoutHash}). No rebuild was necessary.");
                error = "";
                return true;
            }

            GenerateInternal("Regenerate Exact Recipe", deriveStreamsFromMaster: false, changedStreams: null);
            if (lastReport != null && lastReport.Success) return true;

            if (lastReport?.Failures != null && lastReport.Failures.Count > 0)
                error = lastReport.Failures[lastReport.Failures.Count - 1].Message;
            else
                error = "Exact recipe generation failed before an accepted track could be built.";

            // Exact replay is transactional just like Track Editor authoring. A failed
            // imported recipe must not leave its seed, settings, locks or pending state
            // attached to the still-visible previous track. Keep a detached copy of the
            // requested recipe available for inspection/retry; the next replay reapplies
            // its inputs transactionally, while the live controls continue to describe
            // the accepted visible track.
            RestoreAcceptedRecipeInputs(currentAccepted);
            lastAcceptedTopologySlots = currentSlots;
            pendingImportedRecipe = recipe;
            pendingRecipeAwaitingReplay = true;
            return false;
        }

        /// <summary>
        /// Rebuilds an exact previously accepted recipe for the Track Editor's explicit
        /// Undo action. This is transactional just like an authored replacement: if the
        /// historical recipe no longer validates, the current accepted track and its
        /// generation inputs remain active.
        /// </summary>
        public bool RestoreAcceptedRecipeSnapshot(string recipeJson, out string error)
        {
            if (string.IsNullOrWhiteSpace(recipeJson))
            {
                error = "No previous accepted Track Editor design is available to undo.";
                return false;
            }

            // Recipes accepted before authored elevation snapshots were introduced
            // do not contain enough information to reproduce their exact geometry.
            // Keep the currently accepted track instead of attempting a misleading
            // best-effort undo that will inevitably fail the canonical layout hash.
            GenerationRecipeV1 historicalRecipe = GenerationRecipeV1.Deserialize(
                recipeJson, out string historyReadError);
            if (historicalRecipe == null)
            {
                error = historyReadError;
                return false;
            }
            if (historicalRecipe.DesignerAuthoredVariant &&
                (historicalRecipe.AuthoringElevationBaseline == null ||
                 historicalRecipe.AuthoringElevationBaseline.Count == 0))
            {
                error = "This Undo entry was created before complete Track Editor history was available, " +
                        "so its exact prior geometry cannot be reconstructed. Your current track was kept. " +
                        "Apply another edit; new Undo entries include the complete accepted design.";
                return false;
            }

            GenerationRecipeV1 currentAccepted = lastAcceptedRecipe?.Clone();
            List<TopologySlotRecord> currentSlots = CloneTopologySlots(EditableTopologySlots);
            if (!TryImportGenerationRecipe(recipeJson, RecipeReplayMode.Strict, out error))
                return false;

            GenerateInternal("Undo Last Track Editor Change",
                deriveStreamsFromMaster: false, changedStreams: null);
            if (lastReport != null && lastReport.Success) return true;

            if (lastReport?.Failures != null && lastReport.Failures.Count > 0)
                error = DescribeTopologyEditFailure(lastReport);
            else
                error = "The previous accepted design could not be rebuilt. The current track was kept.";

            RestoreAcceptedRecipeInputs(currentAccepted);
            lastAcceptedTopologySlots = currentSlots;
            return false;
        }

        /// <summary>
        /// Rebuilds an accepted route with hand-authored topology choices. The normal
        /// whole-track planner owns connectors, closure, clearance and validation; the
        /// currently accepted scene track is swapped only after the edited route passes.
        /// </summary>
        public bool ApplyTopologyOverrides(
            IReadOnlyList<TopologySlotOverride> requestedOverrides,
            out string error)
        {
            if (requestedOverrides == null || requestedOverrides.Count == 0)
            {
                error = "Choose at least one replacement before applying changes.";
                return false;
            }

            GenerationRecipeV1 previousAcceptedRecipe = lastAcceptedRecipe?.Clone();
            List<TopologySlotRecord> previousAcceptedTopologySlots =
                CloneTopologySlots(EditableTopologySlots);
            GenerationRecipeV1 recipe = previousAcceptedRecipe?.Clone();
            if (recipe == null)
            {
                error = "Generate a valid track before editing its sections.";
                return false;
            }

            GeneratedTrackLayout acceptedBaselineLayout = CurrentLayout;
#if UNITY_EDITOR
            // A domain/script reload clears the runtime property, while the accepted
            // DontSaveInEditor hierarchy and its lightweight visualizer survive. Use
            // that authoritative cache so the first edit after recompilation still
            // preserves the visible track's elevation design.
            if (acceptedBaselineLayout?.Sections == null ||
                acceptedBaselineLayout.Sections.Count == 0)
                TryGetEditorPreviewCacheSnapshot(out _, out acceptedBaselineLayout);
#endif

            recipe.TopologySlotOverrides ??= new List<TopologySlotOverride>();
            bool changedRecipe = false;
            bool restoredOriginalChoice = false;
            for (int i = 0; i < requestedOverrides.Count; i++)
            {
                TopologySlotOverride requested = CloneTopologyOverride(requestedOverrides[i]);
                if (!MergeTopologyOverrideForAuthoring(recipe.TopologySlotOverrides, requested,
                        out bool restoredOriginal))
                    continue;
                changedRecipe = true;
                restoredOriginalChoice |= restoredOriginal;
            }

            if (!changedRecipe)
            {
                error = "The selected replacements did not contain a valid editable section.";
                return false;
            }

            // This is a new authored variant. Its accepted layout hash is established
            // only after the rebuilt route passes and is transactionally swapped in.
            recipe.ExpectedLayoutHash = "";
            bool restoredBaseRecipe = restoredOriginalChoice &&
                                      recipe.TopologySlotOverrides.Count == 0;
            if (restoredBaseRecipe)
            {
                // Returning the last authored choice to its procedural original is a
                // true revert, not another authored realization. Run the original
                // deterministic recipe so its sampled feature parameters, recovery,
                // closure and accepted candidate all return together.
                recipe.RecipeRevision = 0;
                recipe.DesignerAuthoredVariant = false;
                recipe.AuthoringBaselineAttemptIndex = -1;
                recipe.AuthoringBaselineLapLengthMeters = 0f;
            }
            else
            {
                recipe.RecipeRevision = Mathf.Max(0, recipe.RecipeRevision) + 1;
                recipe.DesignerAuthoredVariant = true;
                recipe.AuthoringBaselineAttemptIndex = lastResultManifest?.SelectedAttemptIndex ?? -1;
                recipe.AuthoringBaselineLapLengthMeters = acceptedBaselineLayout?.LapLength ??
                    lastResultManifest?.Metrics?.LapLengthMeters ??
                    lastMetrics?.LapLengthMeters ?? 0f;
            }

            IReadOnlyList<AuthoringElevationBaselineEntry> elevationBaseline =
                restoredBaseRecipe
                    ? null
                    : TrackTopologyPlanner.CaptureAuthoringElevationBaseline(
                        acceptedBaselineLayout?.Sections);
            recipe.AuthoringElevationBaseline = restoredBaseRecipe
                ? new List<AuthoringElevationBaselineEntry>()
                : CloneAuthoringElevationBaseline(elevationBaseline);
            recipe.RefreshHashes();

            string json = GenerationRecipeV1.Serialize(recipe, false);
            if (!TryImportGenerationRecipe(json, RecipeReplayMode.Strict, out error)) return false;

            AuthoringRunContext authoring = restoredBaseRecipe
                ? null
                : new AuthoringRunContext
                  {
                      BaselineAttemptIndex = recipe.AuthoringBaselineAttemptIndex,
                      BaselineLapLength = recipe.AuthoringBaselineLapLengthMeters,
                      ElevationBaseline = elevationBaseline
                  };
            GenerateInternal(restoredBaseRecipe
                    ? "Restore Original Track Editor Feature"
                    : "Apply Track Editor Changes",
                deriveStreamsFromMaster: false, changedStreams: null,
                authoring: authoring);
            if (lastReport != null && lastReport.Success) return true;

            if (lastReport?.Failures != null && lastReport.Failures.Count > 0)
                error = DescribeTopologyEditFailure(lastReport);
            else
                error = "The replacement could not produce a valid complete route. The previous track was kept.";

            // TryImportGenerationRecipe intentionally loads all deterministic inputs
            // before generation. If the edited route is rejected, put the Inspector,
            // active seed, origin, locks and pending-recipe state back on the last
            // accepted route as well. The scene geometry was already preserved by the
            // transactional generator; this completes the rollback of authoring state.
            RestoreAcceptedRecipeInputs(previousAcceptedRecipe);
            lastAcceptedTopologySlots = previousAcceptedTopologySlots;
            return false;
        }

        /// <summary>
        /// Applies one inspector request to recipe data. Choosing the procedural
        /// original after an ordinary replacement removes the override completely;
        /// this restores the sampled original geometry instead of compiling a new
        /// generic realization with the same feature name.
        /// </summary>
        public static bool MergeTopologyOverrideForAuthoring(
            List<TopologySlotOverride> recipeOverrides,
            TopologySlotOverride requested,
            out bool restoredOriginal)
        {
            restoredOriginal = false;
            if (recipeOverrides == null || requested == null ||
                string.IsNullOrWhiteSpace(requested.TopologySlotId))
                return false;

            // Confirming an Area of Impact explicitly supersedes any earlier authored
            // choice inside that window. Leaving both in the recipe would create
            // overlapping ownership and an ambiguous replay.
            if (requested.AllowFeatureOverrides && requested.ImpactMembers != null)
            {
                for (int memberIndex = 0; memberIndex < requested.ImpactMembers.Count;
                     memberIndex++)
                {
                    TopologyImpactMember member = requested.ImpactMembers[memberIndex];
                    if (member == null) continue;
                    recipeOverrides.RemoveAll(existingOverride =>
                        existingOverride != null && ImpactMemberMatchesOverride(
                            member, existingOverride));
                }
            }

            int existingIndex = recipeOverrides.FindIndex(existing => existing != null &&
                string.Equals(existing.TopologySlotId, requested.TopologySlotId,
                    System.StringComparison.Ordinal));
            if (existingIndex < 0)
            {
                recipeOverrides.Add(requested);
                return true;
            }

            TopologySlotOverride accepted = recipeOverrides[existingIndex];
            bool acceptedHasImpact = accepted.ImpactMembers != null &&
                                     accepted.ImpactMembers.Count > 0;
            bool requestedHasImpact = requested.ImpactMembers != null &&
                                      requested.ImpactMembers.Count > 0;
            if (!acceptedHasImpact && !requestedHasImpact &&
                requested.RequestedRealization == accepted.OriginalRealization)
            {
                recipeOverrides.RemoveAt(existingIndex);
                restoredOriginal = true;
                return true;
            }

            // A later edit of the surviving feature must not silently restore
            // neighbors that an earlier accepted Area of Impact removed.
            MergeAcceptedImpact(accepted, requested);
            recipeOverrides[existingIndex] = requested;
            return true;
        }

        private static bool ImpactMemberMatchesOverride(
            TopologyImpactMember member, TopologySlotOverride existing)
        {
            if (member == null || existing == null) return false;
            if (!string.IsNullOrWhiteSpace(member.TopologySlotId) && string.Equals(
                    member.TopologySlotId, existing.TopologySlotId,
                    System.StringComparison.Ordinal))
                return true;
            return !string.IsNullOrWhiteSpace(member.StructuralAnchor) &&
                   string.Equals(member.StructuralAnchor, existing.StructuralAnchor,
                       System.StringComparison.Ordinal);
        }

        private static void MergeAcceptedImpact(
            TopologySlotOverride accepted, TopologySlotOverride requested)
        {
            if (accepted?.ImpactMembers == null || accepted.ImpactMembers.Count == 0 ||
                requested == null)
                return;
            // The structural anchor identifies the original procedural demand. The
            // accepted visible slot may shift when a backward feature is consumed;
            // retaining the source anchor keeps Exact Replay tied to the same demand.
            requested.StructuralAnchor = accepted.StructuralAnchor;
            requested.OriginalRealization = accepted.OriginalRealization;
            requested.ImpactMembers ??= new List<TopologyImpactMember>();
            for (int i = 0; i < accepted.ImpactMembers.Count; i++)
            {
                TopologyImpactMember prior = accepted.ImpactMembers[i];
                if (prior == null) continue;
                bool alreadyPresent = false;
                for (int j = 0; j < requested.ImpactMembers.Count; j++)
                {
                    TopologyImpactMember current = requested.ImpactMembers[j];
                    if (current != null &&
                        ((!string.IsNullOrWhiteSpace(prior.StructuralAnchor) &&
                          string.Equals(prior.StructuralAnchor, current.StructuralAnchor,
                              System.StringComparison.Ordinal)) ||
                         string.Equals(prior.TopologySlotId, current.TopologySlotId,
                             System.StringComparison.Ordinal)))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }
                if (!alreadyPresent)
                    requested.ImpactMembers.Add(JsonUtility.FromJson<TopologyImpactMember>(
                        JsonUtility.ToJson(prior)));
            }
            requested.AllowFeatureOverrides = requested.ImpactMembers.Count > 0;
            requested.ImpactBackwardFeatures = Mathf.Max(requested.ImpactBackwardFeatures,
                accepted.ImpactBackwardFeatures);
            requested.ImpactForwardFeatures = Mathf.Max(requested.ImpactForwardFeatures,
                accepted.ImpactForwardFeatures);
            requested.MaximumReplanScope = (LocalReplanScope)Mathf.Max(
                (int)requested.MaximumReplanScope, (int)accepted.MaximumReplanScope);
        }

        private static string DescribeTopologyEditFailure(TrackGenerationReport report)
        {
            if (report?.Failures == null || report.Failures.Count == 0)
                return "The replacement failed without a detailed generation reason.";

            // A candidate that reached closure/validation contains more useful fitting
            // evidence than unrelated random attempts that lacked the authored demand.
            List<(GenerationFailureReason reason, int count)> counts =
                report.FailureCountsByReason();
            GenerationAttemptFailure selected = null;
            for (int i = 0; i < counts.Count; i++)
            {
                if (counts[i].reason == GenerationFailureReason.RecipeCompatibilityFailure)
                    continue;
                selected = report.RepresentativeFailure(counts[i].reason);
                if (selected == null)
                {
                    selected = new GenerationAttemptFailure
                    {
                        Reason = counts[i].reason,
                        Message = $"A compatible edited candidate reached {counts[i].reason}."
                    };
                }
                break;
            }
            selected ??= report.Failures[report.Failures.Count - 1];

            var summary = new System.Text.StringBuilder();
            int shown = Mathf.Min(3, counts.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) summary.Append(", ");
                summary.Append(counts[i].reason).Append(" x").Append(counts[i].count);
            }

            return summary.Length == 0
                ? selected.Message
                : $"{selected.Message} Failure breakdown: {summary}.";
        }

        private void RestoreAcceptedRecipeInputs(GenerationRecipeV1 acceptedRecipe)
        {
            if (acceptedRecipe == null)
            {
                pendingImportedRecipe = null;
                pendingRecipeAwaitingReplay = false;
                return;
            }

            string acceptedJson = GenerationRecipeV1.Serialize(acceptedRecipe, false);
            if (!TryImportGenerationRecipe(acceptedJson, RecipeReplayMode.Strict,
                    out string restoreError))
            {
                pendingImportedRecipe = null;
                pendingRecipeAwaitingReplay = false;
                Debug.LogError(
                    $"[TrackGenerator] The edited route was rejected, but its previous authoring inputs could not be restored: {restoreError}");
                return;
            }

            // The accepted recipe is the live state, not a newly imported recipe waiting
            // for replay. Clearing this prevents Exact Replay from accidentally targeting
            // a failed edit on the next click.
            pendingImportedRecipe = null;
            pendingRecipeAwaitingReplay = false;
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
            pendingImportedRecipe = null;
            pendingRecipeAwaitingReplay = false;
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

        private void GenerateInternal(string command, bool deriveStreamsFromMaster,
            SeedStream[] changedStreams, AuthoringRunContext authoring = null)
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
                GenerateInternalCore(command, deriveStreamsFromMaster, changedStreams, authoring);
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private void GenerateInternalCore(string command, bool deriveStreamsFromMaster,
            SeedStream[] changedStreams, AuthoringRunContext authoring)
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

            GenerationRecipeV1 requestedRecipe = pendingImportedRecipe?.Clone() ?? CaptureGenerationRecipe();

            // Exact replay of an accepted edited variant restores the same focused
            // authoring policy even after scene reload or recipe import.
            if (authoring == null && requestedRecipe.DesignerAuthoredVariant)
            {
                authoring = new AuthoringRunContext
                {
                    BaselineAttemptIndex = requestedRecipe.AuthoringBaselineAttemptIndex,
                    BaselineLapLength = requestedRecipe.AuthoringBaselineLapLengthMeters,
                    ElevationBaseline = requestedRecipe.AuthoringElevationBaseline
                };
            }

            TrackGenerationResult result = RunPipelineWithPolicy(seed,
                requestedRecipe.TopologySlotOverrides, authoring);

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

            // A recovery pass may have accepted a relaxed/template configuration. The
            // exact recipe must describe the settings that built the accepted layout,
            // not the failed settings that merely initiated the request.
            if (result.EffectiveDesignerSettings != null)
            {
                string expectedLayoutHash = requestedRecipe.ExpectedLayoutHash;
                int recipeRevision = requestedRecipe.RecipeRevision;
                bool designerAuthoredVariant = requestedRecipe.DesignerAuthoredVariant;
                int authoringBaselineAttemptIndex = requestedRecipe.AuthoringBaselineAttemptIndex;
                float authoringBaselineLapLength = requestedRecipe.AuthoringBaselineLapLengthMeters;
                List<AuthoringElevationBaselineEntry> authoringElevationBaseline =
                    CloneAuthoringElevationBaseline(
                        requestedRecipe.AuthoringElevationBaseline);
                List<TopologySlotOverride> topologyOverrides =
                    CloneTopologyOverrides(requestedRecipe.TopologySlotOverrides);
                requestedRecipe = GenerationRecipeV1.Capture(seed, seedStreams,
                    result.EffectiveDesignerSettings, Config, settingsLocks,
                    layoutLockMode, transform);
                requestedRecipe.ExpectedLayoutHash = expectedLayoutHash;
                requestedRecipe.RecipeRevision = recipeRevision;
                requestedRecipe.DesignerAuthoredVariant = designerAuthoredVariant;
                requestedRecipe.AuthoringBaselineAttemptIndex = authoringBaselineAttemptIndex;
                requestedRecipe.AuthoringBaselineLapLengthMeters = authoringBaselineLapLength;
                requestedRecipe.AuthoringElevationBaseline = authoringElevationBaseline;
                requestedRecipe.TopologySlotOverrides = topologyOverrides;
                requestedRecipe.RefreshHashes();
            }

            // AuthoringElevationBaseline is a deterministic INPUT: it is the accepted
            // route immediately before this edit. Do not replace it with the edited
            // output here. Replaying against that output snapshot changes which
            // sections the authoring solver treats as protected and can produce a
            // different canonical layout from the same saved recipe.
            requestedRecipe.AuthoringElevationBaseline ??=
                new List<AuthoringElevationBaselineEntry>();

            string generatedLayoutHash = TrackCanonicalHasher.ComputeLayoutHash(result.Layout);
            if (!string.IsNullOrEmpty(requestedRecipe.ExpectedLayoutHash) &&
                !string.Equals(requestedRecipe.ExpectedLayoutHash, generatedLayoutHash,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Report.Success = false;
                result.Report.AddFailure(result.SelectedAttemptIndex,
                    GenerationFailureReason.RecipeCompatibilityFailure,
                    "GenerationRecipe",
                    $"Exact recipe replay produced layout hash {generatedLayoutHash}, expected {requestedRecipe.ExpectedLayoutHash}. Previous valid track kept.");
                lastReport = result.Report;
                LogFailure(result);
                return;
            }

            if (!NormalizeAcceptedTopologyOverrides(requestedRecipe.TopologySlotOverrides,
                    result.Report?.TopologySlots, out string normalizationError))
            {
                result.Success = false;
                result.Report.Success = false;
                result.Report.AddFailure(result.SelectedAttemptIndex,
                    GenerationFailureReason.RecipeCompatibilityFailure,
                    "TrackEditor",
                    normalizationError + " Previous valid track kept.");
                lastReport = result.Report;
                LogFailure(result);
                return;
            }

            if (!TryBuildTransactional(seed, result))
            {
                LogFailure(result);
                return;
            }

            requestedRecipe.ExpectedLayoutHash = generatedLayoutHash;
            requestedRecipe.Rating = TrackRecipeRatingCalculator.Calculate(
                result.Layout.Metrics, result.Report,
                result.EffectiveDesignerSettings ?? Designer,
                generatedLayoutHash);
            result.Report.TrackRating = requestedRecipe.Rating.Overall;
            requestedRecipe.RefreshHashes();
            lastAcceptedRecipe = requestedRecipe;
            lastResultManifest = TrackResultManifest.Capture(lastAcceptedRecipe, result);
            lastAcceptedTopologySlots = CloneTopologySlots(result.Report?.TopologySlots);
            pendingImportedRecipe = null;
            pendingRecipeAwaitingReplay = false;
            inspectorRatingFallback = requestedRecipe.Rating;

            LogSuccess(result);
        }

        private void EnsureAcceptedRecipeRating()
        {
            if (lastAcceptedRecipe == null || lastReport == null || !lastReport.Success)
                return;
            EnsureRecipeRating(lastAcceptedRecipe, lastReport, lastMetrics,
                lastAcceptedRecipe.ExpectedLayoutHash);
            lastReport.TrackRating = lastAcceptedRecipe.Rating?.Overall ?? 0;
        }

        private static void EnsureRecipeRating(GenerationRecipeV1 recipe,
            TrackGenerationReport report, TrackGenerationMetrics metrics,
            string layoutHash)
        {
            if (recipe == null || report == null || metrics == null || !report.Success)
                return;
            if (recipe.Rating != null && recipe.Rating.Version == TrackRecipeRating.CurrentVersion &&
                string.Equals(recipe.Rating.RatedLayoutHash, layoutHash ?? "",
                    System.StringComparison.OrdinalIgnoreCase))
                return;

            TrackDesignerSettings settings = recipe.DeserializeDesignerSettings();
            if (settings == null) return;
            recipe.Rating = TrackRecipeRatingCalculator.Calculate(
                metrics, report, settings, layoutHash);
        }

        private static SettingsLockState CloneSettingsLocks(SettingsLockState source)
        {
            if (source == null) return new SettingsLockState();
            return JsonUtility.FromJson<SettingsLockState>(JsonUtility.ToJson(source)) ?? new SettingsLockState();
        }

        private static TopologySlotOverride CloneTopologyOverride(TopologySlotOverride source)
        {
            if (source == null) return null;
            return JsonUtility.FromJson<TopologySlotOverride>(JsonUtility.ToJson(source));
        }

        private static List<TopologySlotOverride> CloneTopologyOverrides(
            IReadOnlyList<TopologySlotOverride> source)
        {
            var result = new List<TopologySlotOverride>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                TopologySlotOverride clone = CloneTopologyOverride(source[i]);
                if (clone != null) result.Add(clone);
            }
            return result;
        }

        private static List<AuthoringElevationBaselineEntry> CloneAuthoringElevationBaseline(
            IReadOnlyList<AuthoringElevationBaselineEntry> source)
        {
            var result = new List<AuthoringElevationBaselineEntry>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                AuthoringElevationBaselineEntry entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.MatchKey)) continue;
                result.Add(new AuthoringElevationBaselineEntry
                {
                    MatchKey = entry.MatchKey,
                    ElevationChange = entry.ElevationChange,
                    HillHeight = entry.HillHeight,
                    StartElevation = entry.StartElevation,
                    EndElevation = entry.EndElevation,
                    RoadId = entry.RoadId,
                    CanonicalLapEnd = entry.CanonicalLapEnd
                });
            }
            return result;
        }

        private static List<TopologySlotRecord> CloneTopologySlots(
            IReadOnlyList<TopologySlotRecord> source)
        {
            var result = new List<TopologySlotRecord>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                TopologySlotRecord item = source[i];
                if (item == null) continue;
                TopologySlotRecord clone = JsonUtility.FromJson<TopologySlotRecord>(
                    JsonUtility.ToJson(item));
                if (clone != null) result.Add(clone);
            }
            return result;
        }

        /// <summary>
        /// Rebinds an accepted Track Editor request to the finished route identity.
        /// Area of Impact may legitimately remove a later demand and renumber or
        /// re-quarter the replacement, so the exact marker stamped on the accepted
        /// geometry is authoritative here just as it is inside the planner.
        /// </summary>
        public static bool NormalizeAcceptedTopologyOverrides(
            IReadOnlyList<TopologySlotOverride> overrides,
            IReadOnlyList<TopologySlotRecord> acceptedSlots,
            out string error)
        {
            error = "";
            if (overrides == null || overrides.Count == 0) return true;
            for (int i = 0; i < overrides.Count; i++)
            {
                TopologySlotOverride request = overrides[i];
                if (request == null) continue;
                if (!TrackTopologyPlanner.TryResolveAppliedReplacementSlot(
                        acceptedSlots, request, out TopologySlotRecord slot))
                {
                    TopologySlotCatalog.TryResolveOverride(acceptedSlots, request,
                        out _, out string reason, appliedRealization: true);
                    error = reason;
                    return false;
                }

                request.TopologySlotId = slot.TopologySlotId;
                request.CanonicalOrder = slot.CanonicalOrder;
                if (request.ImpactMembers == null || request.ImpactMembers.Count == 0)
                    request.StructuralAnchor = TopologySlotCatalog.BuildStructuralAnchor(slot);
            }
            return true;
        }

        /// <summary>Runs the pipeline, applying the configured failure policy when no valid candidate exists.</summary>
        private TrackGenerationResult RunPipelineWithPolicy(
            TrackSeed seed,
            IReadOnlyList<TopologySlotOverride> topologyOverrides,
            AuthoringRunContext authoring = null)
        {
            var pipeline = new TrackGenerationPipeline();

            ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(Config, Designer);
            if (authoring != null)
                TrackEditorAuthoringPolicy.Apply(resolved, authoring.BaselineLapLength);
            var pipelineOptions = authoring != null && authoring.BaselineAttemptIndex >= 0
                ? new TrackGenerationPipelineOptions
                {
                    OnlyAttemptIndex = authoring.BaselineAttemptIndex,
                    AuthoringElevationBaseline = authoring.ElevationBaseline
                }
                : authoring?.ElevationBaseline != null
                    ? new TrackGenerationPipelineOptions
                    {
                        AuthoringElevationBaseline = authoring.ElevationBaseline
                    }
                    : null;
            TrackGenerationResult result = pipeline.Run(resolved, seed, seedStreams,
                topologyOverrides, pipelineOptions);
            if (result.Success)
            {
                result.Report.AcceptedPass = "Strict";
                result.EffectiveDesignerSettings = Designer.Clone();
                return result;
            }

            // A Track Editor change is a focused rebuild of the accepted design, not a
            // request to simplify/reroll it. Never enter the random-generation fallback
            // pass, which can remove the authored structural demand entirely.
            if (authoring != null) return result;

            switch (Designer.Generation.FailurePolicy)
            {
                case GenerationFailurePolicy.RelaxOptionalSettings:
                {
                    TrackDesignerSettings relaxed = Designer.Clone();
                    var records = RelaxOptionalSettings(relaxed);
                    // The relaxed pass exists to recover one playable result quickly,
                    // not to score four more candidates after the full pass failed.
                    relaxed.Generation.SelectionMode = CandidateSelectionMode.FirstValid;
                    var relaxedResolved = ResolvedTrackGenerationConfig.Resolve(Config, relaxed);
                    TrackGenerationResult retry = pipeline.Run(relaxedResolved, seed, seedStreams,
                        topologyOverrides);

                    // Merge failure history so the report shows the whole story.
                    retry.Report.PrependFailuresFrom(result.Report);
                    retry.Report.RelaxedSettings.AddRange(records);
                    retry.Report.AttemptsEvaluated += result.Report.AttemptsEvaluated;
                    MergePipelinePassDiagnostics(result.Report, retry.Report,
                        retry.Success ? "Relaxed optional settings" : "No accepted pass");
                    if (retry.Success) retry.EffectiveDesignerSettings = relaxed.Clone();
                    return retry;
                }

                case GenerationFailurePolicy.UseSimpleTemplate:
                {
                    TrackDesignerSettings template = BuildTemplateSettings();
                    var templateResolved = ResolvedTrackGenerationConfig.Resolve(Config, template);
                    TrackGenerationResult retry = pipeline.Run(templateResolved, seed, seedStreams,
                        topologyOverrides);

                    retry.Report.PrependFailuresFrom(result.Report);
                    retry.Report.AttemptsEvaluated += result.Report.AttemptsEvaluated;
                    MergePipelinePassDiagnostics(result.Report, retry.Report,
                        retry.Success ? "Simple template fallback" : "No accepted pass");
                    if (retry.Success)
                    {
                        retry.EffectiveDesignerSettings = template.Clone();
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
            Relax("Layout.CornerSequenceChance", ref s.Layout.CornerSequenceChance, 0f);
            Relax("Scale.PacingVariation", ref s.Scale.PacingVariation, Mathf.Min(s.Scale.PacingVariation, 0.2f));

            void RelaxInt(string name, ref int value, int relaxedValue)
            {
                if (value == relaxedValue) return;
                records.Add(new RelaxedSettingRecord
                {
                    SettingName = name,
                    OriginalValue = value,
                    RelaxedValue = relaxedValue
                });
                value = relaxedValue;
            }

            // Required minima remain intact. The fallback removes only optional
            // geometry so it does not repeat the same oversized search a second time.
            RelaxInt("Features.MaxFeatureGroups", ref s.Features.MaxFeatureGroups,
                s.Features.MinFeatureGroups);
            RelaxInt("Layout.MaxTurnCount", ref s.Layout.MaxTurnCount,
                Mathf.Min(s.Layout.MaxTurnCount, s.Layout.MinTurnCount + 1));
            if (s.Layout.DirectionPattern != TurnDirectionPattern.Circuit)
            {
                records.Add(new RelaxedSettingRecord
                {
                    SettingName = "Layout.DirectionPattern",
                    OriginalValue = (float)s.Layout.DirectionPattern,
                    RelaxedValue = (float)TurnDirectionPattern.Circuit
                });
                // A mostly one-direction circuit has a positive-length closure
                // solution far more often than a deeply folded mixed-sign walk. This
                // applies only after the authored first pass has already failed.
                s.Layout.DirectionPattern = TurnDirectionPattern.Circuit;
            }
            RelaxInt("Elevation.MaxMajorElevationSections", ref s.Elevation.MaxMajorElevationSections,
                s.Elevation.MinMajorElevationSections);
            Relax("Elevation.TargetElevationAmplitude", ref s.Elevation.TargetElevationAmplitude,
                Mathf.Min(s.Elevation.TargetElevationAmplitude, 300f));

            void RelaxRule(string name, TrackFeatureRule rule)
            {
                if (rule == null || !rule.Enabled) return;
                if (rule.MaximumCount > rule.MinimumCount)
                {
                    records.Add(new RelaxedSettingRecord
                    {
                        SettingName = $"{name}.MaximumCount",
                        OriginalValue = rule.MaximumCount,
                        RelaxedValue = rule.MinimumCount
                    });
                    rule.MaximumCount = rule.MinimumCount;
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
            RelaxRule("Features.FullPipes", s.Features.FullPipes);
            RelaxRule("Features.Camelbacks", s.Features.Camelbacks);
            RelaxRule("Features.HeartlineRolls", s.Features.HeartlineRolls);
            RelaxRule("Features.ZeroGRolls", s.Features.ZeroGRolls);
            RelaxRule("Features.DiveLoops", s.Features.DiveLoops);
            RelaxRule("Features.Sidewinders", s.Features.Sidewinders);
            RelaxRule("Features.Wallrides", s.Features.Wallrides);
            RelaxRule("Features.Chicanes", s.Features.Chicanes);
            RelaxRule("Features.SCurves", s.Features.SCurves);
            RelaxRule("Features.Hairpins", s.Features.Hairpins);
            RelaxRule("Features.WideTurnarounds", s.Features.WideTurnarounds);
            RelaxRule("Features.Horseshoes", s.Features.Horseshoes);
            RelaxRule("Features.Cutbacks", s.Features.Cutbacks);
            RelaxRule("Elevation.Crests", s.Elevation.Crests);
            RelaxRule("Elevation.Bridges", s.Elevation.Bridges);
            RelaxRule("Elevation.Underpasses", s.Elevation.Underpasses);

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
            t.Features.FullPipes = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Wallrides = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Camelbacks = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.HeartlineRolls = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.ZeroGRolls = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.DiveLoops = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Sidewinders = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Chicanes = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.SCurves = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Hairpins = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.WideTurnarounds = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Horseshoes = new TrackFeatureRule(false, 0, 0, 0f);
            t.Features.Cutbacks = new TrackFeatureRule(false, 0, 0, 0f);
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
                ResolvedTrackMaterials materials = ResolveMaterials();
                List<string> missingMaterials = materials.GetMissingRequiredRoles(buildRaceCourse);
                if (missingMaterials.Count > 0)
                {
                    result.Report.AddFailure(-1, GenerationFailureReason.MeshBuildFailure, "Materials",
                        $"Persistent material assignment is incomplete: {string.Join(", ", missingMaterials)}.");
                    result.Success = false;
                    DestroyObject(tempRootObj);
                    return false;
                }

                var prismBuilder = new BoxPrismTrackMeshBuilder(resolved.RoadProfile);
                prismBuilder.Build(layout.Sections, materials.RoadSurface, materials.InnerWallSurface,
                    materials.WallSide, tempRootObj.transform);

                // Guidance visuals: center-flat guide lines + wall marker bands
                // (non-colliding overlay meshes; break only at air gaps/open edges).
                TrackGuideMarkingBuilder.Build(layout.Sections, resolved.RoadProfile, Designer.Visual,
                    tempRootObj.transform, materials.GuideMarking, resolved.RoadWidth, materials.WallMarker);

                // ── KeepAboveStart ground guarantee ──────────────────────────────
                // The elevation solver only BIASES toward staying above the start; its
                // closure correction can still drive the realised profile below y=0.
                // Because a closed lap's elevation is defined only up to a constant
                // vertical offset, lift the whole track (root transform) so its lowest
                // rideable point sits at/above the ground plane. Everything — mesh,
                // collider, markings and craft spawn — world-transforms through this
                // root, so the lift is uniform and cannot reopen the loop.
                if (resolved.GroundLevelPolicy == TrackGeneration.Design.TrackGroundLevelPolicy.KeepAboveStart)
                {
                    // Measure the completed road mesh, including its wall profile. The
                    // old centerline-only check could pass while a deep outer wall still
                    // extended below the requested ground plane.
                    float worldMinY;
                    if (!TryGetGeneratedRoadWorldMinY(tempRootObj, out worldMinY))
                        worldMinY = GetSubdivisionWorldMinY(layout, tempRootObj.transform);

                    const float groundClearance = 0.05f; // sit a hair above y=0
                    float lift = CalculateGroundLift(worldMinY, groundClearance);
                    if (lift > 0f)
                    {
                        tempRootObj.transform.position += Vector3.up * lift;
                        Debug.Log($"[TrackGenerator] KeepAboveStart: lifted track {lift:F1}m so the completed road and walls clear the ground plane.");
                    }
                }

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
                // Early-development default: annotate every turn/feature in the Scene view
                // ([NN] DebugName per section + ◆ pattern-group labels). Camera-distance
                // gated (LabelDrawDistance) so it never floods the editor. Turn it Off, or
                // lower it, on the MacroTrackDebugVisualizer on the track root (or via the
                // Toggle Debug View button) when the labels get in the way.
                visualizer.Level = TrackDebugVisualizationLevel.Normal;

                CreateStartAnchor(tempRootObj.transform, layout.Sections[0].StartFrame);

                if (buildRaceCourse)
                {
                    RaceCourse course = RaceCourseBuilder.Build(tempRootObj.transform, layout, resolved.RoadProfile,
                        checkpointCount, startLineArcOffset, materials.StartFinish,
                        materials.StartGatePillar, materials.Checkpoint);
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
                RestoreGeneratedTrackState(tempRootObj);
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
                RestoreDebugAnnotations(root);

                if (IsPendingTrack(root) || (removeDuplicates && root != selected))
                    DestroyObject(root);
            }

            trackRoot = selected != null ? selected.transform : null;

#if UNITY_EDITOR
            if (!Application.isPlaying && selected != null)
            {
                RestoreGeneratedTrackState(selected);
                MarkGeneratedHierarchyTransient(selected);
            }
            else if (selected != null)
                RestoreGeneratedTrackState(selected);
#else
            if (selected != null)
                RestoreGeneratedTrackState(selected);
#endif
        }

        /// <summary>
        /// Restores persistent asset references and runtime components after an editor
        /// cache/domain reload. This never synthesizes fallback materials.
        /// </summary>
        private void RestoreGeneratedTrackState(GameObject root)
        {
            if (root == null) return;
            ReapplyPersistentRoadMaterials(root);
            ReapplyPersistentGuideMaterials(root);
            RestoreRaceCourseStateAndMaterials(root);
            EnsureStartAnchor(root.transform);
        }

        private void ReapplyPersistentRoadMaterials(GameObject root)
        {
            ResolvedTrackMaterials resolved = ResolveMaterials();
            if (root == null || (resolved.RoadSurface == null && resolved.InnerWallSurface == null &&
                                 resolved.WallSide == null)) return;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform parent = renderer.transform.parent;
                if (parent == null || !parent.name.StartsWith("Track_", System.StringComparison.Ordinal) ||
                    !renderer.name.StartsWith("LOD", System.StringComparison.Ordinal)) continue;

                Material[] materials = renderer.sharedMaterials;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                int expectedSlots = filter != null && filter.sharedMesh != null
                    ? Mathf.Max(2, filter.sharedMesh.subMeshCount)
                    : Mathf.Max(2, materials?.Length ?? 0);
                if (materials == null || materials.Length < expectedSlots)
                    System.Array.Resize(ref materials, expectedSlots);
                if (resolved.RoadSurface != null) materials[0] = resolved.RoadSurface;
                if (materials.Length >= 3)
                {
                    if (resolved.InnerWallSurface != null) materials[1] = resolved.InnerWallSurface;
                    if (resolved.WallSide != null) materials[2] = resolved.WallSide;
                }
                else if (resolved.WallSide != null)
                {
                    // Cached meshes made before the three-surface contract used slot 1
                    // for the outer shell. Preserve that interpretation during migration.
                    materials[1] = resolved.WallSide;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private void ReapplyPersistentGuideMaterials(GameObject root)
        {
            ResolvedTrackMaterials resolved = ResolveMaterials();
            if (root == null || (resolved.GuideMarking == null && resolved.WallMarker == null)) return;
            Transform markingRoot = root.transform.Find("TrackGuideMarkings");
            if (markingRoot == null) return;

            foreach (MeshRenderer renderer in markingRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isWallMarker = renderer.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0;
                renderer.sharedMaterial = isWallMarker ? resolved.WallMarker : resolved.GuideMarking;
            }
        }

        private void RestoreRaceCourseStateAndMaterials(GameObject root)
        {
            if (root == null) return;
            ResolvedTrackMaterials resolved = ResolveMaterials();
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
                        material = resolved.Checkpoint;
                    else if (renderer.name.StartsWith("Pillar_", System.StringComparison.Ordinal))
                        material = resolved.StartGatePillar;
                    else
                        material = resolved.StartFinish;

                    if (material != null)
                        renderer.sharedMaterial = material;
                }
            }
        }

        private static void MergePipelinePassDiagnostics(
            TrackGenerationReport earlier,
            TrackGenerationReport latest,
            string acceptedPass)
        {
            if (latest == null) return;
            latest.GenerationDurationSeconds += earlier?.GenerationDurationSeconds ?? 0f;
            latest.PipelinePassCount += earlier?.PipelinePassCount ?? 0;
            latest.AcceptedPass = acceptedPass;
        }

        public ResolvedTrackMaterials ResolveMaterials()
        {
            return ResolvedTrackMaterials.Resolve(MaterialSet, MainRoadMaterial, WallMaterial,
                RoadLineMaterial, StartFinishMaterial, StartGatePillarMaterial, CheckpointMaterial);
        }

        /// <summary>Returns the upward translation needed to satisfy a ground plane.</summary>
        public static float CalculateGroundLift(float worldMinY, float groundClearance = 0.05f)
        {
            if (float.IsNaN(worldMinY) || float.IsInfinity(worldMinY)) return 0f;
            return Mathf.Max(0f, groundClearance - worldMinY);
        }

        private static bool TryGetGeneratedRoadWorldMinY(GameObject root, out float worldMinY)
        {
            worldMinY = float.MaxValue;
            if (root == null) return false;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter) ||
                    filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                    continue;

                Transform parent = renderer.transform.parent;
                if (parent == null ||
                    !parent.name.StartsWith("Track_", System.StringComparison.Ordinal) ||
                    !renderer.name.StartsWith("LOD", System.StringComparison.Ordinal))
                    continue;

                float minY = renderer.bounds.min.y;
                if (float.IsNaN(minY) || float.IsInfinity(minY)) continue;
                worldMinY = Mathf.Min(worldMinY, minY);
            }

            return worldMinY != float.MaxValue;
        }

        private static float GetSubdivisionWorldMinY(GeneratedTrackLayout layout, Transform root)
        {
            float worldMinY = float.MaxValue;
            if (layout?.Sections == null || root == null) return worldMinY;

            foreach (GeneratedTrackSection section in layout.Sections)
            {
                if (section?.SubdivisionFrames == null) continue;
                foreach (TrackConnectionFrame frame in section.SubdivisionFrames)
                    worldMinY = Mathf.Min(worldMinY, root.TransformPoint(frame.Position).y);
            }

            return worldMinY;
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

        // Early-development default: keep the track's turn/feature annotations ON.
        // OnValidate and the editor-save guard run constantly (any inspector tweak, every
        // domain reload), so forcing the level Off here was silently killing the labels a
        // moment after generation set them to Normal. Restore the Normal annotation tier
        // instead. Only lifts from Off, so a manually raised Detailed/Full tier is kept,
        // and disabling the MacroTrackDebugVisualizer component still hides everything.
        private static void RestoreDebugAnnotations(GameObject root)
        {
            if (root == null) return;
            MacroTrackDebugVisualizer visualizer = root.GetComponent<MacroTrackDebugVisualizer>();
            if (visualizer != null && visualizer.Level == TrackDebugVisualizationLevel.Off)
                visualizer.Level = TrackDebugVisualizationLevel.Normal;
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
                RestoreGeneratedTrackState(trackRoot.gameObject);
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
            RestoreGeneratedTrackState(trackRoot.gameObject);
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
                $"attempts {result.AttemptsEvaluated}, candidates {result.ValidCandidateCount}, score {result.Report.SelectedCandidateScore:F1}, rating {result.Report.TrackRating}/100.\n" +
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
