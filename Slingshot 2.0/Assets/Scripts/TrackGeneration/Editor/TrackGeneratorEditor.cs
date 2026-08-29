#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TrackGeneration.Core;
using TrackGeneration.Definitions;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Editor
{
    /// <summary>
    /// Keeps the generated layout (not its heavy procedural meshes) across Unity's
    /// Play Mode backup restore. The backup correctly excludes DontSaveInEditor track
    /// objects; after Play, this recreates only that exact preview from the cached layout.
    /// </summary>
    [InitializeOnLoad]
    internal static class TrackGeneratorPlayPreviewCache
    {
        [System.Serializable]
        private sealed class CachedPreview
        {
            public string ScenePath;
            public string GeneratorPath;
            public int Seed;
            public string Fingerprint;
            public GeneratedTrackLayout Layout;
        }

        [System.Serializable]
        private sealed class CachedPreviewFile
        {
            public List<CachedPreview> Entries = new List<CachedPreview>();
        }

        private static readonly Dictionary<string, CachedPreview> Previews =
            new Dictionary<string, CachedPreview>();
        private static readonly string CacheFilePath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Library", "SlingshotTrackPreviewCache.json");
        private static bool cacheFileKnownValid;
        private static bool cancelledPlayTransitionNeedsRepair;

        static TrackGeneratorPlayPreviewCache()
        {
            LoadFromDisk();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            TrackGenerator.EditorPreviewBuilt += OnEditorPreviewBuilt;
            TrackGenerator.EditorPreviewCleared += Forget;
        }

        private static void OnEditorPreviewBuilt(TrackGenerator generator)
        {
            // A deliberate generator build may change internal samples while retaining
            // the same seed and section endpoints. Always persist that new source data.
            Capture(generator, forceWrite: true);
        }

        internal static bool Capture(TrackGenerator generator)
            => Capture(generator, forceWrite: false);

        private static bool Capture(TrackGenerator generator, bool forceWrite)
        {
            if (generator == null ||
                !generator.TryGetEditorPreviewCacheSnapshot(out int seed, out GeneratedTrackLayout layout))
                return false;

            string scenePath = generator.gameObject.scene.path;
            string generatorPath = GetHierarchyPath(generator.transform);
            string key = CacheKey(scenePath, generatorPath);
            string fingerprint = ComputeLayoutFingerprint(seed, layout);

            // The layout cache is large. Rewriting the identical payload at every
            // Play click both stalls the editor and gives virus scanners/indexers a
            // needless opportunity to hold the destination file open.
            if (!forceWrite && cacheFileKnownValid &&
                Previews.TryGetValue(key, out CachedPreview existing) &&
                existing.Seed == seed && existing.Fingerprint == fingerprint)
            {
                existing.Layout = layout;
                return true;
            }

            Previews[key] = new CachedPreview
            {
                ScenePath = scenePath,
                GeneratorPath = generatorPath,
                Seed = seed,
                Fingerprint = fingerprint,
                Layout = layout
            };
            return SaveToDisk();
        }

        /// <summary>
        /// True when a complete track layout is already held in memory for this
        /// generator (regardless of whether the disk cache write succeeded). Enough to
        /// rebuild the preview for the current Play session.
        /// </summary>
        private static bool HasUsableInMemoryPreview(TrackGenerator generator)
        {
            if (generator == null) return false;
            string key = CacheKey(generator.gameObject.scene.path, GetHierarchyPath(generator.transform));
            return Previews.TryGetValue(key, out CachedPreview cached)
                && cached?.Layout?.Sections != null
                && cached.Layout.Sections.Count > 0;
        }

        internal static void Forget(TrackGenerator generator)
        {
            if (generator == null) return;
            Previews.Remove(CacheKey(generator.gameObject.scene.path, GetHierarchyPath(generator.transform)));
            SaveToDisk();
        }

        internal static bool Repair(TrackGenerator generator)
        {
            if (generator == null)
                return false;

            string key = CacheKey(generator.gameObject.scene.path, GetHierarchyPath(generator.transform));
            if (!Previews.TryGetValue(key, out CachedPreview cached) ||
                cached.Layout?.Sections == null || cached.Layout.Sections.Count == 0)
            {
                LoadFromDisk();
                Previews.TryGetValue(key, out cached);
            }

            if (cached?.Layout?.Sections == null || cached.Layout.Sections.Count == 0)
                return false;

            bool repaired = generator.RestoreEditorPreviewFromCache(
                cached.Seed, cached.Layout, forceRebuild: true);
            if (repaired)
                SceneView.RepaintAll();
            return repaired;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                bool allPreviewsReady = true;
                foreach (TrackGenerator generator in
                    Object.FindObjectsByType<TrackGenerator>(FindObjectsInactive.Include))
                {
                    if (Capture(generator))
                        continue;

                    // Capture returned false. Only a genuinely MISSING layout can
                    // strand the craft in empty space and justify cancelling Play.
                    // A mere disk-write hiccup (the Library cache file momentarily
                    // locked by an indexer / AV, etc.) leaves the in-memory layout
                    // intact and fully usable for this session — proceed rather than
                    // forcing the user to click Play a second time.
                    if (HasUsableInMemoryPreview(generator))
                    {
                        Debug.LogWarning(
                            $"[TrackGenerator] Preview cache for '{generator.name}' could not be written " +
                            "to disk, but the in-memory layout is valid — continuing into Play Mode.");
                        continue;
                    }

                    allPreviewsReady = false;
                    Debug.LogError(
                        $"[TrackGenerator] Play Mode was stopped because no preview layout is cached for " +
                        $"'{generator.name}'. Generate the track once, then press Play again.");
                }

                // A track made from DontSaveInEditor meshes is deliberately omitted
                // from Unity's Play Mode scene copy. Entering Play without ANY cached
                // layout can only strand the craft in empty space, so fail safely.
                if (!allPreviewsReady)
                {
                    cancelledPlayTransitionNeedsRepair = true;
                    EditorApplication.isPlaying = false;
                }
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Generated previews carry DontSaveInEditor and are intentionally absent
                // from Unity's Play Mode scene backup. Rebuild the exact cached layout
                // immediately after that backup has been applied.
                EditorApplication.delayCall += RestoreMissingPreviewsForPlayMode;
            }
            else if (state == PlayModeStateChange.EnteredEditMode && Previews.Count > 0)
            {
                // Unity finishes applying the backup at the end of this state change.
                EditorApplication.delayCall += RestoreMissingPreviews;
            }
        }

        private static void RestoreMissingPreviews()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            foreach (TrackGenerator generator in
                Object.FindObjectsByType<TrackGenerator>(FindObjectsInactive.Include))
            {
                string scenePath = generator.gameObject.scene.path;
                string generatorPath = GetHierarchyPath(generator.transform);
                if (!Previews.TryGetValue(CacheKey(scenePath, generatorPath), out CachedPreview cached) ||
                    cached.Layout == null)
                    continue;

                // Unity may have started stripping DontSaveInEditor objects before a
                // failed Play transition was cancelled. Never adopt that possibly
                // partial hierarchy; rebuild one complete preview transactionally.
                generator.RestoreEditorPreviewFromCache(cached.Seed, cached.Layout,
                    forceRebuild: cancelledPlayTransitionNeedsRepair);
            }

            cancelledPlayTransitionNeedsRepair = false;
            SceneView.RepaintAll();
        }

        private static void RestoreMissingPreviewsForPlayMode()
        {
            if (!EditorApplication.isPlaying)
                return;

            // Rehydrate plain serializable data rather than retaining references owned
            // by the Edit Mode hierarchy Unity just removed from its scene backup.
            LoadFromDisk();
            RestoreForCurrentGenerators();
        }

        private static void RestoreForCurrentGenerators()
        {
            if (Previews.Count == 0)
                LoadFromDisk();

            foreach (TrackGenerator generator in
                Object.FindObjectsByType<TrackGenerator>(FindObjectsInactive.Include))
            {
                string scenePath = generator.gameObject.scene.path;
                string generatorPath = GetHierarchyPath(generator.transform);
                if (!Previews.TryGetValue(CacheKey(scenePath, generatorPath), out CachedPreview cached) ||
                    cached.Layout?.Sections == null || cached.Layout.Sections.Count == 0)
                    continue;

                if (generator.TrackRoot == null)
                    generator.RestoreEditorPreviewFromCache(cached.Seed, cached.Layout);
            }
        }

        private static bool SaveToDisk()
        {
            string temporaryPath = null;
            try
            {
                var file = new CachedPreviewFile();
                file.Entries.AddRange(Previews.Values);
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath));
                string json = JsonUtility.ToJson(file);
                if (string.IsNullOrWhiteSpace(json) || json == "{}")
                    throw new System.InvalidOperationException("Unity produced an empty preview-cache payload.");

                // Never write into the live cache. Complete a unique sibling file,
                // then atomically replace the destination. A brief Windows sharing
                // lock is retried instead of being treated as corrupted track data.
                temporaryPath = CacheFilePath + "." + System.Guid.NewGuid().ToString("N") + ".writing";
                File.WriteAllText(temporaryPath, json);

                System.Exception lastException = null;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        if (File.Exists(CacheFilePath))
                            File.Replace(temporaryPath, CacheFilePath, null);
                        else
                            File.Move(temporaryPath, CacheFilePath);

                        cacheFileKnownValid = File.Exists(CacheFilePath) &&
                            new FileInfo(CacheFilePath).Length > 0;
                        return cacheFileKnownValid;
                    }
                    catch (System.IO.IOException exception)
                    {
                        lastException = exception;
                    }
                    catch (System.UnauthorizedAccessException exception)
                    {
                        lastException = exception;
                    }

                    System.Threading.Thread.Sleep(35 * (attempt + 1));
                }

                throw new System.IO.IOException(
                    "The cache remained locked after eight atomic replacement attempts.", lastException);
            }
            catch (System.Exception exception)
            {
                cacheFileKnownValid = false;
                Debug.LogError($"[TrackGenerator] Could not persist the editor track preview cache: {exception.Message}");
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath) && File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch { /* A stale temp is harmless and never read as a cache. */ }
                }
            }
        }

        private static void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(CacheFilePath)) return;
                string json = ReadCacheTextWithRetry();
                CachedPreviewFile file = JsonUtility.FromJson<CachedPreviewFile>(json);
                if (file?.Entries == null) return;

                Previews.Clear();
                foreach (CachedPreview preview in file.Entries)
                {
                    if (preview?.Layout?.Sections == null || preview.Layout.Sections.Count == 0) continue;
                    if (string.IsNullOrEmpty(preview.Fingerprint))
                        preview.Fingerprint = ComputeLayoutFingerprint(preview.Seed, preview.Layout);
                    Previews[CacheKey(preview.ScenePath, preview.GeneratorPath)] = preview;
                }
                cacheFileKnownValid = Previews.Count > 0;
            }
            catch (System.Exception exception)
            {
                cacheFileKnownValid = false;
                Debug.LogWarning($"[TrackGenerator] Could not read the editor track preview cache: {exception.Message}");
            }
        }

        private static string ReadCacheTextWithRetry()
        {
            System.Exception lastException = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try { return File.ReadAllText(CacheFilePath); }
                catch (System.IO.IOException exception) { lastException = exception; }
                catch (System.UnauthorizedAccessException exception) { lastException = exception; }
                System.Threading.Thread.Sleep(25 * (attempt + 1));
            }

            throw new System.IO.IOException("The preview cache remained locked while reading.", lastException);
        }

        private static string ComputeLayoutFingerprint(int seed, GeneratedTrackLayout layout)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                void AddInt(int value) { hash ^= (uint)value; hash *= 1099511628211UL; }
                void AddFloat(float value) => AddInt(value.GetHashCode());
                void AddVector(Vector3 value)
                {
                    AddFloat(value.x);
                    AddFloat(value.y);
                    AddFloat(value.z);
                }

                AddInt(seed);
                List<GeneratedTrackSection> sections = layout?.Sections;
                AddInt(sections?.Count ?? 0);
                if (sections != null)
                {
                    foreach (GeneratedTrackSection section in sections)
                    {
                        if (section == null) { AddInt(-1); continue; }
                        AddInt(section.SectionIndex);
                        AddInt((int)(section.Definition?.SectionType ?? default));
                        AddInt(section.Definition?.SubdivisionCount ?? 0);
                        AddFloat(section.Definition?.Length ?? 0f);
                        AddVector(section.StartFrame.Position);
                        AddVector(section.StartFrame.Forward);
                        AddVector(section.StartFrame.Up);
                        AddVector(section.EndFrame.Position);
                        AddVector(section.EndFrame.Forward);
                        AddVector(section.EndFrame.Up);
                    }
                }

                return hash.ToString("X16");
            }
        }

        private static string CacheKey(string scenePath, string generatorPath)
            => $"{scenePath}|{generatorPath}";

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }
            return path;
        }
    }

    internal sealed class TrackGeneratorSceneSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            bool savingScene = false;
            foreach (string path in paths)
            {
                if (path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
                {
                    savingScene = true;
                    break;
                }
            }

            if (!savingScene)
                return paths;

            foreach (TrackGenerator generator in
                Object.FindObjectsByType<TrackGenerator>(FindObjectsInactive.Include))
                generator.PrepareGeneratedTrackForEditorSave();

            return paths;
        }
    }

    /// <summary>
    /// Designer-facing inspector for the track generator, organized for the intended
    /// workflow: generate → keep what looks good → lock settings/layout → randomize
    /// the rest. The primary controls live in a TOP TOOLBAR (no scrolling to
    /// generate), followed by a LARGE persistent report card, the lock summary and
    /// only then the full grouped settings.
    /// </summary>
    [CustomEditor(typeof(TrackGenerator))]
    public class TrackGeneratorEditor : UnityEditor.Editor
    {
        // Selector state: index 0 = Random on every selector.
        private int _styleIndex = 2;      // Balanced by default (index 0 is Random)
        private int _difficultyIndex = 2; // Normal
        private int _sizeIndex = 3;       // Medium
        private PresetRandomizationMode _randomMode = PresetRandomizationMode.Curated;

        private bool _showTechnicalReport;
        private bool _showLocks;
        private bool _showFeatureAmounts = true;
        private bool _showAdvancedGeneration;
        private bool _showDiagnosticsAndMaintenance;
        private bool _showInspectorPerformance;
        private bool _showResolvedPreview;
        private static string _lastPresetApplication = "";

        // The inspector's ordinary job is to DRAW this cache. Settings resolution and
        // report summarization run only when something actually changed — never per
        // repaint (see TrackInspectorPreviewCache).
        private readonly TrackInspectorPreviewCache _cache = new TrackInspectorPreviewCache();

        // Log a warning when a single inspector pass turns expensive (kept permanently:
        // one comparison per pass; fires only on real regressions).
        private const double SlowInspectorPassMs = 32.0;
        private readonly System.Diagnostics.Stopwatch _headerWatch = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch _footerWatch = new System.Diagnostics.Stopwatch();
        private readonly PassTiming _headerTiming = new PassTiming();
        private readonly PassTiming _footerTiming = new PassTiming();

        // Bumped whenever a toolbar button runs a generator command (generate, randomize,
        // regenerate, …). A generate runs 300 attempts synchronously inside the toolbar's
        // own IMGUI callback, so its seconds land inside the header stopwatch — that is a
        // user command executing, NOT expensive work leaking into idle repaints. Passes in
        // which the epoch advanced are excluded from the repaint-timing stats and warning.
        private static long _commandEpoch;

        // Temporary proof counters. None of these schedule updates or request repaints.
        private static int _activeEditorCount;
        private bool _undoSubscribed;
        private int _inspectorBuildCount;
        private int _serializedTrackerRegistrationCount;
        private int _settingsPropertyFieldCount;
        private int _lastDetachedLegacyDescendantCount;
        private int _diagnosedGenerationRevision = int.MinValue;
        private Transform _diagnosedTrackRoot;
        private int _diagnosedTrackDescendantCount;

        private sealed class PassTiming
        {
            public int Count { get; private set; }
            public double LastMs { get; private set; }
            public double MaxMs { get; private set; }
            public double AverageMs => Count > 0 ? _totalMs / Count : 0.0;
            public string LastEventType { get; private set; } = "none";
            private double _totalMs;

            public void Record(double milliseconds, EventType eventType)
            {
                Count++;
                LastMs = milliseconds;
                MaxMs = System.Math.Max(MaxMs, milliseconds);
                _totalMs += milliseconds;
                LastEventType = eventType.ToString();
            }
        }

        private static readonly string[] DifficultyOptions =
            { PresetRandomizer.RandomOption, "Easy", "Normal", "Hard", "Extreme" };
        private static readonly string[] SizeOptions =
            { PresetRandomizer.RandomOption, "Very Small", "Small", "Medium", "Large", "Huge" };
        private static readonly TrackSizeLevel[] SizeValues =
            { TrackSizeLevel.Medium /*placeholder for Random*/, TrackSizeLevel.VerySmall, TrackSizeLevel.Small,
              TrackSizeLevel.Medium, TrackSizeLevel.Large, TrackSizeLevel.Huge };

        private static string[] _styleOptions;

        private string[] StyleOptions()
        {
            if (_styleOptions == null)
            {
                var names = new List<string> { PresetRandomizer.RandomOption };
                names.AddRange(TrackStylePresetLibrary.BuiltInNames);
                _styleOptions = names.ToArray();
            }
            return _styleOptions;
        }

        // Designer-facing serialized fields drawn as RETAINED-MODE UIElements property
        // fields, in order. The hidden generation output (lastReport/lastMetrics) is
        // deliberately absent — the report card shows its cached summary instead.
        // settingsLocks and layoutLockMode are owned by the lock panel / toolbar.
        private void OnEnable()
        {
            // Idempotent subscription makes duplicate OnEnable calls observable and safe.
            if (!_undoSubscribed)
            {
                Undo.undoRedoPerformed += OnUndoRedo;
                _undoSubscribed = true;
                _activeEditorCount++;
            }
            _cache.MarkSettingsDirty();

            var generator = target as TrackGenerator;
            if (generator?.Designer?.NormalizeAppliedPresetSnapshot() == true)
            {
                EditorUtility.SetDirty(generator);
                Debug.LogWarning("[TrackGeneratorEditor] Removed recursively embedded preset-snapshot JSON from the generator. " +
                                 "The Inspector payload is bounded again; save the scene to persist the cleanup.");
            }
            int trimmed = generator?.LastReport?.TrimStoredFailuresToLimit() ?? 0;
            if (trimmed > 0)
            {
                EditorUtility.SetDirty(generator);
                Debug.LogWarning($"[TrackGeneratorEditor] Trimmed {trimmed} legacy serialized failure rows; " +
                                 $"the newest {TrackGenerationReport.MaxStoredFailures} remain and aggregate counts are preserved.");
            }

            DetachLegacyChildTrack();

            // Selecting the generator is also a safe recovery point after an editor or
            // domain-reload interruption. Keep one complete preview, release abandoned
            // pending/duplicate roots, and leave diagnostic gizmos off.
            generator?.CleanStaleGeneratedTracks();
        }

        private void OnDisable()
        {
            if (!_undoSubscribed) return;
            Undo.undoRedoPerformed -= OnUndoRedo;
            _undoSubscribed = false;
            _activeEditorCount = Mathf.Max(0, _activeEditorCount - 1);
        }

        private void OnUndoRedo() => _cache.MarkSettingsDirty();

        /// <summary>
        /// One-time migration: tracks generated before the parenting fix live UNDER the
        /// generator GameObject, which makes selecting the generator traverse the whole
        /// multi-million-vertex hierarchy in the inspector. Detach them (world pose
        /// preserved) the moment such a generator is inspected.
        /// </summary>
        private void DetachLegacyChildTrack()
        {
            var generator = target as TrackGenerator;
            if (generator == null || generator.TrackRoot == null) return;
            if (generator.TrackRoot.parent != generator.transform) return;

            _lastDetachedLegacyDescendantCount = CountDescendants(generator.TrackRoot);
            Undo.SetTransformParent(generator.TrackRoot, null, "Detach Generated Track");
            Debug.Log("[TrackGeneratorEditor] Moved the generated track to the scene root — a child track makes " +
                      "the inspector traverse its whole mesh hierarchy whenever the generator is selected.");
        }

        /// <summary>
        /// UIElements hybrid inspector. The settings tree is RETAINED mode: the property
        /// fields are built ONCE and repaint only when their values change — an IMGUI
        /// OnInspectorGUI re-layouts every visible nested control of the designer
        /// settings on every repaint event, which is exactly the "UI repaint" cost that
        /// makes the editor crawl once a few groups are expanded. The toolbar, report
        /// card and lock panel remain small bounded IMGUI islands.
        /// </summary>
        public override VisualElement CreateInspectorGUI()
        {
            _inspectorBuildCount++;
            _settingsPropertyFieldCount = 0;
            var generator = (TrackGenerator)target;
            generator.Designer ??= TrackStylePresetLibrary.Create(TrackStylePresetLibrary.Balanced);

            var root = new VisualElement();

            // ── Header: toolbar + report card + locks (bounded IMGUI) ──
            root.Add(new IMGUIContainer(() =>
            {
                long epochBefore = _commandEpoch;
                _headerWatch.Restart();
                DrawTopToolbar(generator);
                DrawReportCard(generator);
                DrawLockPanel(generator);
                _headerWatch.Stop();
                // A toolbar button may have run a synchronous generator command this pass;
                // its seconds are the command, not repaint leakage — don't record or warn.
                if (_commandEpoch == epochBefore)
                {
                    _headerTiming.Record(_headerWatch.Elapsed.TotalMilliseconds, Event.current.type);
                    WarnIfSlow("header", _headerWatch.Elapsed.TotalMilliseconds);
                }
            }));

            // ── Settings: retained-mode property fields ──
            var settingsBlock = new VisualElement { style = { marginTop = 6 } };
            AddPropertyGroup(settingsBlock, "TRACK DESIGN SETTINGS", false,
                "Designer", "Config");
            AddMaterialGroup(settingsBlock, generator);
            AddPropertyGroup(settingsBlock, "RACE COURSE", false,
                "buildRaceCourse", "checkpointCount", "startLineArcOffset");
            AddPropertyGroup(settingsBlock, "PLAY MODE", false,
                "generateOnStart", "keepEditorTrackOnPlay", "placeHovercraftOnStart",
                "startLineForwardOffset", "resetHovercraftWithBackspace");
            AddPropertyGroup(settingsBlock, "ADVANCED OUTPUT & SEEDS", false,
                "trackRoot", "seedStreams");
            root.Add(settingsBlock);

            // ── Footer: cached resolved preview + mesh stats (bounded IMGUI) ──
            root.Add(new IMGUIContainer(() =>
            {
                _footerWatch.Restart();
                DrawResolvedPreview(generator);
                DrawPolyCount(generator);
                _footerWatch.Stop();
                _footerTiming.Record(_footerWatch.Elapsed.TotalMilliseconds, Event.current.type);
                WarnIfSlow("footer", _footerWatch.Elapsed.TotalMilliseconds);
            }));

            root.Bind(serializedObject);

            // Real change detection (fires on actual serialized changes, never on
            // repaint): refresh the cached previews and maintain preset provenance.
            _serializedTrackerRegistrationCount++;
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                _cache.MarkSettingsDirty();

                var d = generator.Designer;
                if (d != null && !string.IsNullOrEmpty(d.AppliedPresetName) && !d.ModifiedSincePreset &&
                    !string.IsNullOrEmpty(d.AppliedPresetSnapshotJson) && d.AppliedPresetSnapshotJson != d.ToJson())
                {
                    d.ModifiedSincePreset = true;
                }
            });

            return root;
        }

        private void AddPropertyGroup(VisualElement parent, string title, bool expanded,
            params string[] propertyNames)
        {
            var foldout = new Foldout { text = title, value = expanded };
            foldout.style.marginLeft = 3;
            foldout.style.marginRight = 3;
            foldout.style.marginTop = 2;
            foldout.style.marginBottom = 2;

            for (int i = 0; i < propertyNames.Length; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(propertyNames[i]);
                if (property == null) continue;
                foldout.Add(new PropertyField(property));
                _settingsPropertyFieldCount++;
            }

            parent.Add(foldout);
        }

        private void AddMaterialGroup(VisualElement parent, TrackGenerator generator)
        {
            var foldout = new Foldout { text = "MATERIALS", value = false };
            foldout.style.marginLeft = 3;
            foldout.style.marginRight = 3;
            foldout.style.marginTop = 2;
            foldout.style.marginBottom = 2;

            string[] propertyNames =
            {
                "MaterialSet", "MainRoadMaterial", "WallMaterial", "RoadLineMaterial",
                "StartFinishMaterial", "StartGatePillarMaterial", "CheckpointMaterial"
            };
            for (int i = 0; i < propertyNames.Length; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(propertyNames[i]);
                if (property == null) continue;
                foldout.Add(new PropertyField(property));
                _settingsPropertyFieldCount++;
            }

            foldout.Add(new IMGUIContainer(() =>
            {
                EditorGUILayout.Space(3f);
                List<string> missing = generator.ResolveMaterials().GetMissingRequiredRoles(generator.BuildsRaceCourse);
                if (missing.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        generator.MaterialSet != null
                            ? "Persistent material set ready. Road, markings and race-course visuals will survive cache reloads and Exact Replay."
                            : "Material roles resolve through legacy references. Assign a Material Set to make the visual contract portable and explicit.",
                        generator.MaterialSet != null ? MessageType.Info : MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Generation is blocked until these persistent roles are assigned: " + string.Join(", ", missing),
                        MessageType.Error);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var repairContent = new GUIContent(
                        "Create / Repair Default Set",
                        "Creates any missing persistent material assets and reconnects the role set. Existing material colors, emission, textures, and other authored tuning are preserved.");
                    if (GUILayout.Button(repairContent, GUILayout.Height(22f)))
                    {
                        Undo.RecordObject(generator, "Assign Track Material Set");
                        generator.MaterialSet = TrackMaterialGenerator.GenerateOrLoadMaterialSet();
                        EditorUtility.SetDirty(generator);
                        serializedObject.Update();
                    }

                    EditorGUI.BeginDisabledGroup(generator.MaterialSet == null);
                    if (GUILayout.Button("Select Set", GUILayout.Height(22f)))
                    {
                        Selection.activeObject = generator.MaterialSet;
                        EditorGUIUtility.PingObject(generator.MaterialSet);
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }));

            parent.Add(foldout);
        }

        private static double _lastSlowLogTime;

        private static void WarnIfSlow(string sectionName, double elapsedMs)
        {
            if (elapsedMs <= SlowInspectorPassMs) return;
            if (EditorApplication.timeSinceStartup - _lastSlowLogTime < 5.0) return; // never spam the console into its own slowdown
            _lastSlowLogTime = EditorApplication.timeSinceStartup;
            Debug.LogWarning($"[TrackGeneratorEditor] Inspector {sectionName} pass took {elapsedMs:F1} ms — expensive work leaked into a repaint path.");
        }

        private void DrawInspectorDiagnostics(TrackGenerator generator)
        {
            RefreshHierarchyDiagnosticsIfNeeded(generator);

            TrackGenerationReport report = generator.LastReport;
            TrackSeedManager seedManager = generator.GetComponent<TrackSeedManager>();
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                EditorGUILayout.LabelField("Temporary Inspector Diagnostics", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"UI roots built: {_inspectorBuildCount} | serialized trackers: {_serializedTrackerRegistrationCount} | " +
                    $"property fields/root: {_settingsPropertyFieldCount} | active editors: {_activeEditorCount} | " +
                    $"Undo callback: {(_undoSubscribed ? 1 : 0)}",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    $"Header ({_headerTiming.LastEventType}): last {_headerTiming.LastMs:F2} ms, avg {_headerTiming.AverageMs:F2}, " +
                    $"max {_headerTiming.MaxMs:F2}, passes {_headerTiming.Count} | Footer ({_footerTiming.LastEventType}): " +
                    $"last {_footerTiming.LastMs:F2} ms, avg {_footerTiming.AverageMs:F2}, max {_footerTiming.MaxMs:F2}, " +
                    $"passes {_footerTiming.Count}",
                    EditorStyles.wordWrappedMiniLabel);

                if (report != null)
                {
                    EditorGUILayout.LabelField(
                        $"Serialized report: failures {report.Failures.Count}/{report.TotalFailureCount} stored/total " +
                        $"(cap {TrackGenerationReport.MaxStoredFailures}), warnings {report.Warnings.Count}, " +
                        $"connectors {report.ConnectorDecisions.Count}, regions {report.SubdivisionRegions.Count}, " +
                        $"quarter rows {report.QuarterBalance.Count}",
                        EditorStyles.wordWrappedMiniLabel);
                }

                bool generatorOwnsTrackHierarchy = generator.TrackRoot != null &&
                                                   generator.TrackRoot.parent == generator.transform;
                EditorGUILayout.LabelField(
                    $"Hierarchy: generator direct children {generator.transform.childCount}, track descendants " +
                    $"{_diagnosedTrackDescendantCount}, track parented under generator: {generatorOwnsTrackHierarchy} | " +
                    $"seed bookmarks {seedManager?.SavedSeedCount ?? 0}" +
                    (_lastDetachedLegacyDescendantCount > 0
                        ? $" | detached legacy descendants {_lastDetachedLegacyDescendantCount}"
                        : ""),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    $"Cache work: settings resolves {_cache.ResolveCount}, report summaries {_cache.SummaryRebuildCount}. " +
                    "These counts and callback/UI-root counts must remain stable while the Inspector idles.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void RefreshHierarchyDiagnosticsIfNeeded(TrackGenerator generator)
        {
            if (_diagnosedGenerationRevision == generator.GenerationRevision &&
                _diagnosedTrackRoot == generator.TrackRoot) return;

            _diagnosedGenerationRevision = generator.GenerationRevision;
            _diagnosedTrackRoot = generator.TrackRoot;
            _diagnosedTrackDescendantCount = CountDescendants(generator.TrackRoot);
        }

        private static int CountDescendants(Transform root)
        {
            if (root == null) return 0;
            int count = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                count++;
                count += CountDescendants(root.GetChild(i));
            }
            return count;
        }

        // ═══════════════════════════ Top toolbar ═══════════════════════════

        private static string _lastExportPath = "";
        private static GUIStyle _bigButtonStyle;
        private static GUIStyle _warnButtonStyle;
        private static GUIStyle _cardTitleStyle;
        private bool _exactReplayQueued;
        private bool _recipeIoQueued;
        private bool _reportExportQueued;

        // One restrained visual language for the whole tool. Structural UI uses
        // cyan-teal; green, amber, and red are reserved for actual result states.
        private static readonly Color ProfileAccent = new Color(0.48f, 0.51f, 0.53f, 1f);
        private static readonly Color FeatureAccent = new Color(0.34f, 0.58f, 0.62f, 1f);
        private static readonly Color GenerationAccent = new Color(0.27f, 0.66f, 0.70f, 1f);
        private static readonly Color GenerationActionTint = new Color(0.22f, 0.50f, 0.57f, 1f);
        private static readonly Color RecipeAccent = new Color(0.66f, 0.55f, 0.79f, 1f);
        private static readonly Color ReplayActionTint = new Color(0.54f, 0.45f, 0.68f, 1f);
        private static readonly Color EditorAccent = new Color(0.55f, 0.41f, 0.76f, 1f);
        private static readonly Color EditorActionTint = new Color(0.40f, 0.30f, 0.58f, 1f);

        private static string GetDefaultRecipeDirectory()
        {
            string directory = Path.Combine(Application.dataPath, "Scripts", "TrackGeneration", "GenerationTrackRecipes");
            try
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[TrackGenerator] Could not create the default recipe folder '{directory}': {exception.Message}");
                return Application.dataPath;
            }
        }

        private static GUIContent CopyRecipeIcon()
        {
            const string tooltip = "Copy the complete exact recipe JSON for the last accepted track to the clipboard.";
            GUIContent builtIn = EditorGUIUtility.IconContent("Clipboard");
            return builtIn != null && builtIn.image != null
                ? new GUIContent(builtIn.image, tooltip)
                : new GUIContent("⧉", tooltip);
        }

        private static void SectionHeader(string title)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        }

        private static void CardHeader(string title, string subtitle, Color accent)
        {
            Rect accentLine = EditorGUILayout.GetControlRect(false, 3f);
            accentLine.xMin += 1f;
            accentLine.xMax -= 1f;
            EditorGUI.DrawRect(accentLine, accent);
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(title, CardTitleStyle());
            if (!string.IsNullOrEmpty(subtitle))
                EditorGUILayout.LabelField(subtitle, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2f);
        }

        private static GUIStyle CardTitleStyle() => _cardTitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12
        };

        private static bool AccentButton(string label, string tooltip, Color tint,
            float height = 28f, bool bold = true)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            var style = new GUIStyle(GUI.skin.button)
            {
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            bool pressed = GUILayout.Button(new GUIContent(label, tooltip), style, GUILayout.Height(height));
            GUI.backgroundColor = previous;
            return pressed;
        }

        private static bool Btn(string label, string tooltip, float height = 22f)
            => GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Height(height));

        /// <summary>Any command that regenerates or mutates settings invalidates the caches.</summary>
        private void AfterGeneratorCommand(TrackGenerator generator)
        {
            _commandEpoch++; // mark this IMGUI pass as command-driven, not an idle repaint
            // Generator commands mutate hidden serialized recipe/report fields while the
            // retained UI still owns its pre-click SerializedObject snapshot. Refresh it
            // immediately so binding cannot write that stale snapshot back over the
            // newly accepted recipe (which previously erased the visible rating).
            serializedObject.Update();
            _cache.MarkSettingsDirty(); // relaxation policies may have adjusted settings
            EditorUtility.SetDirty(generator);
            TrackGeneratorPlayPreviewCache.Capture(generator);
        }

        private bool ImportRecipeJson(TrackGenerator generator, string json)
        {
            Undo.RecordObject(generator, "Import Generation Recipe");
            TrackSeedManager seedManager = generator.GetComponent<TrackSeedManager>();
            if (seedManager != null) Undo.RecordObject(seedManager, "Import Generation Recipe Seed");
            if (!generator.TryImportGenerationRecipe(json, RecipeReplayMode.Strict, out string importError))
            {
                EditorUtility.DisplayDialog("Recipe Import Failed", importError, "OK");
                return false;
            }

            EditorUtility.SetDirty(generator);
            if (seedManager != null) EditorUtility.SetDirty(seedManager);
            serializedObject.Update();
            Debug.Log("[TrackGenerator] Imported exact Generation Recipe inputs. Press Exact Replay to verify and build it.");
            return true;
        }

        private void DrawTopToolbar(TrackGenerator generator)
        {
            EditorGUILayout.Space(2);
            DrawProfileCard(generator);
            DrawFeatureAmountsCard(generator);
            DrawFeatureDefinitionsCard();
            DrawGenerationCard(generator);
            DrawExactRecipeCard(generator);
            DrawTrackEditorCard(generator);
            DrawAdvancedGeneration(generator);
            DrawDiagnosticsAndMaintenance(generator);
        }

        private static void DrawFeatureDefinitionsCard()
        {
            EditorGUILayout.Space(3f);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                bool complete = TrackFeatureDefinitionCatalog.TryValidateDesignerCoverage(
                    out string summary, out List<TrackPatternType> missing);
                CardHeader("FEATURE DEFINITIONS", summary, FeatureAccent);

                if (!complete)
                {
                    string details = TrackFeatureDefinitionCatalog.LoadErrors.Count > 0
                        ? string.Join("\n", TrackFeatureDefinitionCatalog.LoadErrors)
                        : "Missing individual assets: " + string.Join(", ", missing);
                    EditorGUILayout.HelpBox(details, MessageType.Error);
                }

                if (Btn("Open Feature Definitions",
                        "Edit the versioned parameters and contracts for each individual feature asset.", 23f))
                    EditorApplication.ExecuteMenuItem("Track/V2/Feature Definitions");
            }
        }

        private void DrawProfileCard(TrackGenerator generator)
        {
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                string provenance = string.IsNullOrEmpty(generator.Designer.AppliedPresetName)
                    ? "Custom setup"
                    : generator.Designer.ModifiedSincePreset
                        ? $"Custom · based on {generator.Designer.AppliedPresetName}"
                        : generator.Designer.AppliedPresetName;
                CardHeader("TRACK PROFILE", provenance,
                    ProfileAccent);

                DrawProfilePopup("STYLE", ref _styleIndex, StyleOptions());
                DrawVerticalProfilePopup(generator);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawProfilePopup("DIFFICULTY", ref _difficultyIndex, DifficultyOptions);
                    GUILayout.Space(4f);
                    DrawProfilePopup("SIZE", ref _sizeIndex, SizeOptions);
                }

                EditorGUILayout.Space(2f);
                if (AccentButton("APPLY PROFILE",
                    "Applies Style, Difficulty and Size without generating. Locked settings stay untouched.",
                    Color.white, 24f))
                    ApplyPresets(generator);

                if (!string.IsNullOrEmpty(_lastPresetApplication))
                    EditorGUILayout.LabelField(_lastPresetApplication, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawVerticalProfilePopup(TrackGenerator generator)
        {
            if (generator?.Designer?.Elevation == null) return;

            EditorGUILayout.LabelField("ELEVATION", EditorStyles.miniBoldLabel);
            VerticalGenerationProfile current = generator.Designer.Elevation.Profile;
            EditorGUI.BeginChangeCheck();
            VerticalGenerationProfile selected = (VerticalGenerationProfile)EditorGUILayout.EnumPopup(current);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(generator, "Change Vertical Profile");
            generator.Designer.Elevation.Profile = selected;
            generator.Designer.ModifiedSincePreset = true;
            _cache.MarkSettingsDirty();
            EditorUtility.SetDirty(generator);
        }

        private static void DrawProfilePopup(string label, ref int index, string[] options)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
                index = EditorGUILayout.Popup(index, options, GUILayout.MinWidth(80f), GUILayout.ExpandWidth(true));
            }
        }

        private void DrawFeatureAmountsCard(TrackGenerator generator)
        {
            EditorGUILayout.Space(3);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                TrackFeatureSettings features = generator.Designer?.Features;
                int required = 0;
                int maximum = 0;
                if (features != null)
                {
                    AccumulateFeatureCounts(features.Jumps, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Loops, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Corkscrews, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Spirals, ref required, ref maximum);
                    AccumulateFeatureCounts(features.HalfLoops, ref required, ref maximum);
                    AccumulateFeatureCounts(features.FullPipes, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Camelbacks, ref required, ref maximum);
                    AccumulateFeatureCounts(features.HeartlineRolls, ref required, ref maximum);
                    AccumulateFeatureCounts(features.ZeroGRolls, ref required, ref maximum);
                    AccumulateFeatureCounts(features.DiveLoops, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Sidewinders, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Wallrides, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Chicanes, ref required, ref maximum);
                    AccumulateFeatureCounts(features.SCurves, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Hairpins, ref required, ref maximum);
                    AccumulateFeatureCounts(features.WideTurnarounds, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Horseshoes, ref required, ref maximum);
                    AccumulateFeatureCounts(features.Cutbacks, ref required, ref maximum);
                }

                CardHeader("FEATURE AMOUNTS",
                    $"Guaranteed minimum {required} · combined cap {maximum}", FeatureAccent);
                _showFeatureAmounts = EditorGUILayout.Foldout(_showFeatureAmounts,
                    _showFeatureAmounts ? "Hide feature limits" : "Edit feature limits", true);
                if (!_showFeatureAmounts)
                    return;

                SerializedProperty designerProperty = serializedObject.FindProperty("Designer");
                SerializedProperty featureProperty = designerProperty?.FindPropertyRelative("Features");
                if (featureProperty == null)
                {
                    EditorGUILayout.HelpBox("Feature rules are unavailable until the designer settings are initialized.",
                        MessageType.Info);
                    return;
                }

                EditorGUILayout.Space(2f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("FEATURE", EditorStyles.miniBoldLabel, GUILayout.MinWidth(104f));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("ON", EditorStyles.miniBoldLabel, GUILayout.Width(24f));
                    GUILayout.Label("MIN", EditorStyles.miniBoldLabel, GUILayout.Width(42f));
                    GUILayout.Label("MAX", EditorStyles.miniBoldLabel, GUILayout.Width(42f));
                }

                EditorGUI.BeginChangeCheck();
                DrawFeatureAmountRow(featureProperty, "Jumps", "Air Gaps");
                DrawFeatureAmountRow(featureProperty, "Loops", "Loops");
                DrawFeatureAmountRow(featureProperty, "Corkscrews", "Corkscrews");
                DrawFeatureAmountRow(featureProperty, "Spirals", "Spirals");
                DrawFeatureAmountRow(featureProperty, "HalfLoops", "Half-Loops");
                DrawFeatureAmountRow(featureProperty, "FullPipes", "Full Pipes");
                DrawFeatureAmountRow(featureProperty, "Camelbacks", "Camelbacks");
                DrawFeatureAmountRow(featureProperty, "HeartlineRolls", "Heartline Rolls");
                DrawFeatureAmountRow(featureProperty, "ZeroGRolls", "Zero-G Rolls");
                DrawFeatureAmountRow(featureProperty, "DiveLoops", "Dive Loops");
                DrawFeatureAmountRow(featureProperty, "Sidewinders", "Sidewinders");
                DrawFeatureAmountRow(featureProperty, "Wallrides", "Wallrides");
                DrawFeatureAmountRow(featureProperty, "Chicanes", "Chicanes");
                DrawFeatureAmountRow(featureProperty, "SCurves", "S-Curves");
                DrawFeatureAmountRow(featureProperty, "Hairpins", "Hairpins");
                DrawFeatureAmountRow(featureProperty, "WideTurnarounds", "Wide Turnarounds");
                DrawFeatureAmountRow(featureProperty, "Horseshoes", "Horseshoes");
                DrawFeatureAmountRow(featureProperty, "Cutbacks", "Cutbacks");

                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField("PROCEDURAL RHYTHM", EditorStyles.miniBoldLabel);
                SerializedProperty rhythmEnabled = featureProperty.FindPropertyRelative("EnforceProceduralRhythm");
                EditorGUILayout.PropertyField(rhythmEnabled, new GUIContent("Balanced encounter spacing"));
                if (rhythmEnabled != null && rhythmEnabled.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(
                        featureProperty.FindPropertyRelative("MaxConsecutiveSCurveEncounters"),
                        new GUIContent("Consecutive S-flow maximum"));
                    EditorGUILayout.PropertyField(
                        featureProperty.FindPropertyRelative("SCurveDiversityWindow"),
                        new GUIContent("S-flow window"));
                    EditorGUILayout.PropertyField(
                        featureProperty.FindPropertyRelative("MaxSCurveEncountersPerWindow"),
                        new GUIContent("S-flow maximum in window"));
                    EditorGUILayout.PropertyField(
                        featureProperty.FindPropertyRelative("SameFamilyCooldownEncounters"),
                        new GUIContent("Other-family cooldown"));
                    EditorGUI.indentLevel--;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    generator.Designer.Features.Sanitize();
                    generator.Designer.ModifiedSincePreset = true;
                    EditorUtility.SetDirty(generator);
                    _cache.MarkSettingsDirty();
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(
                    "Minimum is guaranteed. If it cannot fit safely, generation fails clearly instead of silently dropping it. Maximum is a hard cap; 0 disables placement.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void AccumulateFeatureCounts(TrackFeatureRule rule, ref int minimum, ref int maximum)
        {
            if (rule == null) return;
            minimum += rule.EffectiveMinimum;
            maximum += rule.EffectiveMaximum;
        }

        private static void DrawFeatureAmountRow(SerializedProperty featureProperty,
            string propertyName, string label)
        {
            SerializedProperty rule = featureProperty.FindPropertyRelative(propertyName);
            if (rule == null) return;

            SerializedProperty enabledProperty = rule.FindPropertyRelative("Enabled");
            SerializedProperty minimumProperty = rule.FindPropertyRelative("MinimumCount");
            SerializedProperty maximumProperty = rule.FindPropertyRelative("MaximumCount");
            if (enabledProperty == null || minimumProperty == null || maximumProperty == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.MinWidth(104f));
                GUILayout.FlexibleSpace();
                enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(24f));

                int minimum = Mathf.Max(0,
                    EditorGUILayout.IntField(minimumProperty.intValue, GUILayout.Width(42f)));
                int maximum = Mathf.Max(minimum,
                    EditorGUILayout.IntField(maximumProperty.intValue, GUILayout.Width(42f)));
                minimumProperty.intValue = minimum;
                maximumProperty.intValue = maximum;
            }
        }

        private void DrawGenerationCard(TrackGenerator generator)
        {
            EditorGUILayout.Space(3);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("GENERATION", "Build a fresh route, replay this seed, or export its report.",
                    GenerationAccent);
                if (AccentButton("GENERATE NEW TRACK",
                    "Creates a completely new track from a fresh master seed. The previous valid track is preserved if generation fails.",
                    GenerationActionTint, 40f))
                {
                    generator.GenerateNewEverything();
                    AfterGeneratorCommand(generator);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Same Seed",
                        "Rebuilds the current seed with the current settings."))
                    {
                        generator.RegenerateSameSettings();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("Validate",
                        "Shows resolved rules and warnings without changing the track."))
                        _showResolvedPreview = true;
                }

                EditorGUILayout.Space(2f);
                using (new EditorGUI.DisabledScope(_reportExportQueued))
                {
                    if (Btn(_reportExportQueued ? "Opening…" : "Export Debug Report",
                        "Saves the complete diagnostic report for the current generated track.", 20f))
                        QueueExportDebugReport(generator);
                }
            }
        }

        private void QueueExportDebugReport(TrackGenerator generator)
        {
            if (_reportExportQueued || generator == null) return;
            _reportExportQueued = true;
            EditorApplication.delayCall += () =>
            {
                _reportExportQueued = false;
                if (this == null || generator == null) return;
                ExportDebugReport(generator);
                Repaint();
            };
        }

        private static void ExportDebugReport(TrackGenerator generator)
        {
            string path = EditorUtility.SaveFilePanel("Export Track Debug Report",
                Path.GetDirectoryName(Application.dataPath),
                $"TrackReport_{generator.LastReport?.Seed ?? 0}", "txt");
            if (string.IsNullOrEmpty(path)) return;

            TrackDebugReportExporter.Export(generator, path);
            _lastExportPath = path;
            EditorUtility.RevealInFinder(path);
        }

        private void DrawExactRecipeCard(TrackGenerator generator)
        {
            EditorGUILayout.Space(3);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                string acceptedHash = generator.LastResultManifest?.CanonicalLayoutHash;
                string identity = string.IsNullOrEmpty(acceptedHash)
                        ? "No accepted recipe yet"
                        : $"Layout ID · {acceptedHash.Substring(0, Mathf.Min(12, acceptedHash.Length))}…";
                CardHeader("EXACT RECIPE", identity,
                    RecipeAccent);

                TrackRecipeRating rating = generator.DisplayedRecipeRating;
                if (rating != null && rating.IsRated)
                {
                    EditorGUILayout.LabelField($"TRACK RATING   {rating.Overall} / 100",
                        EditorStyles.boldLabel);
                    Rect barRect = GUILayoutUtility.GetRect(1f, 7f, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(barRect, new Color(0.12f, 0.12f, 0.14f, 1f));
                    Rect fillRect = barRect;
                    fillRect.width *= rating.Overall / 100f;
                    Color ratingColor = Color.Lerp(new Color(0.85f, 0.30f, 0.25f),
                        new Color(0.20f, 0.78f, 0.62f), rating.Overall / 100f);
                    EditorGUI.DrawRect(fillRect, ratingColor);
                    EditorGUILayout.LabelField(
                        $"Flow {rating.Flow}   Variety {rating.Variety}   Request fit {rating.RequestFit}   Technical {rating.TechnicalQuality}",
                        EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("TRACK RATING   Unrated — replay or generate this recipe",
                        EditorStyles.centeredGreyMiniLabel);
                }

                EditorGUILayout.Space(3f);

                using (new EditorGUI.DisabledScope(_recipeIoQueued))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(CopyRecipeIcon(), GUILayout.Width(32f), GUILayout.Height(23f)))
                    {
                        GUIUtility.systemCopyBuffer = generator.ExportGenerationRecipe(preferLastAccepted: true);
                        Debug.Log("[TrackGenerator] Copied exact Generation Recipe JSON to the clipboard.");
                    }
                    if (Btn(_recipeIoQueued ? "Opening…" : "Save",
                            "Save the accepted recipe as a portable JSON file.", 23f))
                        QueueRecipeIo(generator, save: true);
                    if (Btn(_recipeIoQueued ? "Opening…" : "Load",
                            "Load and validate a recipe JSON. Exact Replay remains a separate action.", 23f))
                        QueueRecipeIo(generator, save: false);
                }

                EditorGUILayout.Space(3f);
                using (new EditorGUI.DisabledScope(_exactReplayQueued))
                {
                    if (AccentButton(_exactReplayQueued ? "REPLAYING…" : "EXACT REPLAY",
                            "Rebuilds the loaded or last accepted recipe and verifies its exact layout identity.",
                            ReplayActionTint, 30f))
                        QueueExactReplay(generator);
                }
            }
        }

        private void QueueExactReplay(TrackGenerator generator)
        {
            if (_exactReplayQueued || generator == null) return;
            _exactReplayQueued = true;

            // Generation and failure dialogs mutate Selection/Inspector state. Running
            // either while IMGUI still owns VerticalScope/HorizontalScope groups causes
            // Unity's "EndLayoutGroup: BeginLayoutGroup must be called first" error.
            // Defer the complete command until this Inspector pass has closed cleanly.
            EditorApplication.delayCall += () =>
            {
                _exactReplayQueued = false;
                if (this == null || generator == null) return;

                if (!generator.RegenerateExactRecipe(out string replayError))
                    EditorUtility.DisplayDialog("Exact Recipe Replay Failed", replayError, "OK");
                AfterGeneratorCommand(generator);
                Repaint();
            };
        }

        private void SaveRecipe(TrackGenerator generator)
        {
            string path = EditorUtility.SaveFilePanel("Save Generation Recipe",
                GetDefaultRecipeDirectory(), "SlingshotTrackRecipe", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, generator.ExportGenerationRecipe(preferLastAccepted: true));
                if (path.StartsWith(Application.dataPath, System.StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.Refresh();
                Debug.Log($"[TrackGenerator] Saved Generation Recipe: {path}");
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Recipe Save Failed", exception.Message, "OK");
            }
        }

        private void QueueRecipeIo(TrackGenerator generator, bool save)
        {
            if (_recipeIoQueued || generator == null) return;
            _recipeIoQueued = true;

            // Native file panels and their validation dialogs can invalidate the UIElements-backed
            // Inspector while its IMGUI scopes are still open. Run the complete interaction after
            // this draw pass, just like Exact Replay, so every layout group closes first.
            EditorApplication.delayCall += () =>
            {
                _recipeIoQueued = false;
                if (this == null || generator == null) return;
                if (save) SaveRecipe(generator);
                else LoadRecipe(generator);
                Repaint();
            };
        }

        private void LoadRecipe(TrackGenerator generator)
        {
            string path = EditorUtility.OpenFilePanel("Load Generation Recipe",
                GetDefaultRecipeDirectory(), "json");
            if (string.IsNullOrEmpty(path)) return;
            try { ImportRecipeJson(generator, File.ReadAllText(path)); }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Recipe Load Failed", exception.Message, "OK");
            }
        }

        private static void DrawTrackEditorCard(TrackGenerator generator)
        {
            EditorGUILayout.Space(3);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                TrackEditor child = generator.GetComponentInChildren<TrackEditor>(true);
                CardHeader("TRACK EDITOR", child != null
                        ? "Ready · edit generated segments and topology choices"
                        : "Creates a lightweight editing tool under this TrackGenerator",
                    EditorAccent);
                if (AccentButton(child != null ? "OPEN TRACK EDITOR" : "CREATE TRACK EDITOR",
                        "Selects the TrackEditor for this generator, creating and parenting it when needed.",
                        EditorActionTint, 25f))
                    TrackEditorEditor.SelectOrCreateFor(generator);
            }
        }

        private void DrawAdvancedGeneration(TrackGenerator generator)
        {
            EditorGUILayout.Space(3);
            _showAdvancedGeneration = EditorGUILayout.Foldout(_showAdvancedGeneration,
                "Advanced Regeneration", true);
            if (!_showAdvancedGeneration) return;

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                _randomMode = (PresetRandomizationMode)EditorGUILayout.EnumPopup("Random Style", _randomMode);
                TrackSeedManager seedManager = generator.GetComponent<TrackSeedManager>();
                if (seedManager != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool useRandom = EditorGUILayout.ToggleLeft(
                        new GUIContent("Use a fresh seed for new tracks", "Turn off to generate from the entered master seed."),
                        seedManager.UseRandomSeed);
                    using (new EditorGUI.DisabledScope(useRandom))
                        seedManager.CurrentSeedInput = EditorGUILayout.IntField("Master Seed", seedManager.CurrentSeedInput);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(seedManager, "Change Track Seed Mode");
                        seedManager.UseRandomSeed = useRandom;
                        EditorUtility.SetDirty(seedManager);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Same Layout · New Content",
                        "Keeps the skeleton and quarter topology while re-rolling content."))
                    {
                        generator.SameLayoutNewContent();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("Same Geometry · New Surface",
                        "Keeps all geometry and changes only surface treatment and visuals."))
                    {
                        generator.SameGeometryNewSurface();
                        AfterGeneratorCommand(generator);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Features Only", "Re-rolls only feature choices.", 20f))
                    {
                        generator.NewFeaturesOnly();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("Quarter Content", "Re-rolls only quarter content.", 20f))
                    {
                        generator.NewQuarterContentOnly();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("Visuals Only", "Re-rolls visuals without changing geometry.", 20f))
                    {
                        generator.NewVisualsOnly();
                        AfterGeneratorCommand(generator);
                    }
                }
                if (Btn("Randomize Everything Unlocked",
                    "Re-rolls every stream allowed by the current layout and settings locks."))
                {
                    Undo.RecordObject(generator, "Randomize All Unlocked");
                    generator.RandomizeAllUnlocked();
                    AfterGeneratorCommand(generator);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Reset Unlocked Settings",
                        "Restores unlocked groups to the applied profile.", 20f))
                    {
                        Undo.RecordObject(generator, "Reset Unlocked Groups");
                        PresetApplicator.ResetUnlockedGroups(generator.Designer, generator.SettingsLocks);
                        EditorUtility.SetDirty(generator);
                    }
                    if (Btn("Save Profile Asset",
                        "Saves the current design settings as a reusable project asset.", 20f))
                        SaveAsPresetAsset(generator.Designer);
                }
            }
        }

        private void DrawDiagnosticsAndMaintenance(TrackGenerator generator)
        {
            EditorGUILayout.Space(3);
            _showDiagnosticsAndMaintenance = EditorGUILayout.Foldout(_showDiagnosticsAndMaintenance,
                "Diagnostics & Maintenance", true);
            if (!_showDiagnosticsAndMaintenance) return;

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Export Report", "Writes the complete track diagnostic report to a text file."))
                        ExportDebugReport(generator);
                    if (Btn("Open Reports", "Opens the most recent report location."))
                        EditorUtility.RevealInFinder(string.IsNullOrEmpty(_lastExportPath)
                            ? Path.GetDirectoryName(Application.dataPath)
                            : _lastExportPath);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Copy Summary", "Copies the last compact generation report.", 20f))
                        EditorGUIUtility.systemCopyBuffer = FullReportText(generator.LastReport, generator.LastMetrics);
                    if (Btn("Focus Failure", "Frames the last positional generation failure.", 20f))
                        FocusPrimaryFailure(generator);
                    if (Btn("Debug View", "Toggles track diagnostics in the Scene view.", 20f))
                        ToggleDebugView(generator);
                }

                SectionHeader("V2 STABILITY");
                EditorGUILayout.LabelField(
                    "Run a small, time-bounded seed audit without building meshes or changing the scene.",
                    EditorStyles.wordWrappedMiniLabel);
                if (Btn("Open Bounded Stability Audit",
                    "Measures strict planning reliability across deterministic seeds and writes a compact report.", 24f))
                    V2StabilitySweepWindow.OpenFor(generator);

                SectionHeader("PREVIEW CARE");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Repair Cached Preview", "Rebuilds the exact cached preview without a new seed.", 20f) &&
                        !TrackGeneratorPlayPreviewCache.Repair(generator))
                        EditorUtility.DisplayDialog("Cached Preview Unavailable",
                            "No valid cached layout exists. Generate a track once to create it.", "OK");
                    if (Btn("Clean Stale Previews", "Removes duplicate and abandoned generated roots.", 20f))
                    {
                        Undo.RecordObject(generator, "Clean Stale Track Previews");
                        generator.CleanStaleGeneratedTracks();
                        EditorUtility.SetDirty(generator);
                    }
                }

                Color previous = GUI.color;
                GUI.color = new Color(1f, 0.68f, 0.58f);
                if (Btn("Clear Generated Track…",
                    "Destroys generated track objects after confirmation; settings and recipes remain.", 22f) &&
                    EditorUtility.DisplayDialog("Clear Track", "Destroy the current generated track?", "Clear", "Cancel"))
                {
                    Undo.RecordObject(generator, "Clear Track");
                    generator.ClearTrack();
                    TrackGeneratorPlayPreviewCache.Forget(generator);
                    EditorUtility.SetDirty(generator);
                }
                GUI.color = previous;

                _showInspectorPerformance = EditorGUILayout.Foldout(_showInspectorPerformance,
                    "Inspector performance details", true);
                if (_showInspectorPerformance)
                    DrawInspectorDiagnostics(generator);
            }
        }

        /// <summary>Resolves Random selectors (curated), applies style+difficulty+size lock-aware.</summary>
        private void ApplyPresets(TrackGenerator generator)
        {
            Undo.RecordObject(generator, "Apply Presets");
            VerticalGenerationProfile verticalProfile = generator.Designer?.Elevation?.Profile
                ?? VerticalGenerationProfile.Balanced;

            var req = new PresetRandomizer.Request
            {
                StyleIsRandom = _styleIndex == 0,
                Style = _styleIndex == 0 ? TrackStylePresetLibrary.Balanced : StyleOptions()[_styleIndex],
                DifficultyIsRandom = _difficultyIndex == 0,
                Difficulty = _difficultyIndex == 0 ? TrackDifficultyLevel.Normal : (TrackDifficultyLevel)(_difficultyIndex - 1),
                SizeIsRandom = _sizeIndex == 0,
                Size = SizeValues[_sizeIndex],
                Mode = _randomMode
            };

            // Deterministic curated pick from the PresetResolution stream, advanced each use.
            var streams = generator.SeedStreams;
            var rng = new Unity.Mathematics.Random((uint)Mathf.Max(1, streams.PresetResolutionSeed == 0 ? 1 : streams.PresetResolutionSeed));
            streams.PresetResolutionSeed = unchecked(streams.PresetResolutionSeed * 486187739 + 1);

            var selection = PresetRandomizer.Resolve(req, generator.Config, generator.Designer, generator.SettingsLocks, ref rng);
            var incoming = PresetRandomizer.BuildCandidate(selection.Style, selection.Difficulty, selection.Size,
                generator.Designer, new SettingsLockState() /* locks applied below */);
            incoming.AppliedPresetName = selection.Style;

            var applied = PresetApplicator.Apply(generator.Designer, incoming, generator.SettingsLocks);
            // Elevation character is an independent V2 authoring choice, not part of
            // Style/Difficulty/Size. Applying another profile must not silently reset it.
            if (generator.Designer?.Elevation != null)
                generator.Designer.Elevation.Profile = verticalProfile;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Applied: {selection.Style} / {selection.Difficulty} / {selection.Size}");
            if (req.StyleIsRandom || req.DifficultyIsRandom || req.SizeIsRandom)
            {
                sb.AppendLine($"Requested: {selection.RequestedSummary}");
                foreach (var reason in selection.Reasons) sb.AppendLine($"  • {reason}");
            }
            sb.AppendLine($"Changed: {(applied.ChangedGroups.Count > 0 ? string.Join(", ", applied.ChangedGroups) : "nothing")}");
            if (applied.SkippedGroups.Count > 0)
                sb.AppendLine($"Skipped because locked: {string.Join(", ", applied.SkippedGroups)}");
            _lastPresetApplication = sb.ToString().TrimEnd();

            _cache.MarkSettingsDirty();
            EditorUtility.SetDirty(generator);
        }

        private static void FocusPrimaryFailure(TrackGenerator generator)
        {
            var report = generator.LastReport;
            if (report == null || report.Failures.Count == 0)
            {
                Debug.Log("[TrackGeneratorEditor] No recorded failures to focus.");
                return;
            }

            for (int i = report.Failures.Count - 1; i >= 0; i--)
            {
                var f = report.Failures[i];
                if (f.Position == Vector3.zero) continue;
                var world = generator.transform.TransformPoint(f.Position);
                SceneView.lastActiveSceneView?.LookAt(world, SceneView.lastActiveSceneView.rotation, 120f);
                Debug.Log($"[TrackGeneratorEditor] Focused failure: {f.Message}");
                return;
            }
            Debug.Log("[TrackGeneratorEditor] The recorded failures carry no positions.");
        }

        private static void ToggleDebugView(TrackGenerator generator)
        {
            // The generated track is a scene-root sibling — reach it via the reference.
            var vis = generator.TrackRoot != null
                ? generator.TrackRoot.GetComponent<MacroTrackDebugVisualizer>()
                : null;
            if (vis == null)
            {
                Debug.Log("[TrackGeneratorEditor] No debug visualizer on the current track (generate first).");
                return;
            }
            Undo.RecordObject(vis, "Toggle Debug View");
            bool turnOn = !vis.enabled || vis.Level == TrackDebugVisualizationLevel.Off;
            vis.enabled = true;
            vis.Level = turnOn ? TrackDebugVisualizationLevel.Normal : TrackDebugVisualizationLevel.Off;
            SceneView.RepaintAll();
        }

        // ═══════════════════════════ Report card ═══════════════════════════

        private static GUIStyle _reportHeaderStyle;
        private static GUIStyle _reportBodyStyle;
        private static GUIStyle _miniWrapStyle;

        private static void EnsureReportStyles()
        {
            _reportHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _reportBodyStyle ??= new GUIStyle(EditorStyles.label) { fontSize = 11, richText = true, wordWrap = true };
            _miniWrapStyle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        }

        private void DrawReportCard(TrackGenerator generator)
        {
            // Draw the CACHED summary — rebuilt only when a new result is stored.
            _cache.RefreshReportIfNeeded(generator.LastReport, generator.LastMetrics, generator.GenerationRevision);

            EditorGUILayout.Space(4);
            if (!_cache.HasReport)
            {
                EditorGUILayout.HelpBox("No generation has run yet. Use the toolbar above.", MessageType.None);
                return;
            }

            EnsureReportStyles();
            var report = generator.LastReport;

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                Color accent = _cache.ReportSuccess
                    ? (report.Warnings.Count > 0
                        ? new Color(0.86f, 0.64f, 0.28f, 1f)
                        : new Color(0.38f, 0.74f, 0.52f, 1f))
                    : new Color(0.86f, 0.34f, 0.32f, 1f);
                Rect accentLine = EditorGUILayout.GetControlRect(false, 4f);
                EditorGUI.DrawRect(accentLine, accent);
                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(_cache.ReportHeadline, _reportHeaderStyle);
                EditorGUILayout.LabelField(_cache.ReportBody, _reportBodyStyle);

                if (_cache.ReportStreamsLine.Length > 0)
                    EditorGUILayout.LabelField(_cache.ReportStreamsLine, EditorStyles.miniLabel);
                if (_cache.ReportLocksLine.Length > 0)
                    EditorGUILayout.LabelField(_cache.ReportLocksLine, EditorStyles.miniLabel);
                if (_cache.ReportLayoutLockLine.Length > 0)
                    EditorGUILayout.LabelField(_cache.ReportLayoutLockLine, EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy Summary", GUILayout.Width(105)))
                        EditorGUIUtility.systemCopyBuffer = FullReportText(report, generator.LastMetrics);
                    _showTechnicalReport = EditorGUILayout.Foldout(_showTechnicalReport, "Technical details", true);
                }

                if (_showTechnicalReport)
                    DrawTechnicalDetails(report);
            }
        }

        /// <summary>
        /// Bounded technical foldout: shows counts plus the top entries of each list —
        /// the full detail lives in the exported debug report, never inline in IMGUI.
        /// </summary>
        private void DrawTechnicalDetails(TrackGenerationReport report)
        {
            EnsureReportStyles();
            var mini = _miniWrapStyle;
            const int maxEntries = 15;

            void DrawCapped<T>(string title, IReadOnlyList<T> list)
            {
                if (list.Count == 0) return;
                EditorGUILayout.LabelField($"{title} ({list.Count})", EditorStyles.boldLabel);
                int shown = Mathf.Min(list.Count, maxEntries);
                for (int i = 0; i < shown; i++)
                    EditorGUILayout.LabelField("• " + list[i], mini);
                if (list.Count > shown)
                    EditorGUILayout.LabelField($"…and {list.Count - shown} more (Export Debug Report for the full list).", mini);
            }

            DrawCapped("Connector decisions", report.ConnectorDecisions);
            if (report.ConnectorShadowSummary != null && report.ConnectorShadowSummary.BoundaryCount > 0)
            {
                MessageType type = report.ConnectorShadowSummary.HasBlockingMismatch
                    ? MessageType.Warning
                    : MessageType.Info;
                EditorGUILayout.HelpBox("V2 connector shadow audit: " + report.ConnectorShadowSummary, type);
            }
            DrawCapped("Connector shadow audit", report.ConnectorShadowRecords);
            DrawCapped("V2 topology slots", report.TopologySlots);
            DrawCapped("Subdivision regions", report.SubdivisionRegions);
            DrawCapped("Dual-quarter balance", report.QuarterBalance);
            DrawCapped("Relaxed settings", report.RelaxedSettings);
            DrawCapped("Warnings", report.Warnings);

            if (report.TotalFailureCount > 0)
            {
                EditorGUILayout.LabelField($"Failures by reason ({report.TotalFailureCount} total)", EditorStyles.boldLabel);
                foreach (var (reason, count) in report.FailureCountsByReason())
                    EditorGUILayout.LabelField($"• {reason}: ×{count}", mini);
                int shown = 0;
                for (int i = report.Failures.Count - 1; i >= 0 && shown < 5; i--, shown++)
                    EditorGUILayout.LabelField("  " + report.Failures[i], mini);
            }

            EditorGUILayout.LabelField(
                $"Inspector diagnostics: settings resolves {_cache.ResolveCount}, summary rebuilds {_cache.SummaryRebuildCount} (these must NOT grow while idling).",
                EditorStyles.centeredGreyMiniLabel);
        }

        private static string FullReportText(TrackGenerationReport report, TrackGenerationMetrics metrics)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(report.Success ? "GENERATED SUCCESSFULLY" : "GENERATION FAILED");
            sb.AppendLine($"Seed: {report.Seed}   Command: {report.RegenerationCommand}");
            sb.AppendLine($"Generation: {report.GenerationDurationSeconds:F2}s across {Mathf.Max(1, report.PipelinePassCount)} pipeline pass(es)   Accepted: {report.AcceptedPass}");
            sb.AppendLine($"Rating: {report.TrackRating}/100   Attempts: {report.AttemptsEvaluated}   Valid candidates: {report.ValidCandidateCount}   Internal score: {report.SelectedCandidateScore:F1}");
            sb.AppendLine(report.AngleReliefEnabled
                ? $"Closure relief: ON — invoked on {report.AngleReliefAttempts} solves, closed {report.AngleReliefClosures}"
                : "Closure relief: OFF (flag disabled this run)");
            var reasons = report.FailureCountsByReason();
            if (reasons.Count > 0)
            {
                int totF = report.TotalFailureCount;
                sb.Append("Failure modes:");
                foreach (var (reason, count) in reasons)
                    sb.Append($" {reason} {count} ({(totF > 0 ? 100f * count / totF : 0f):F0}%);");
                sb.AppendLine();
            }
            if (metrics != null && report.Success)
            {
                sb.AppendLine($"Estimated lap: {metrics.EstimatedNeutralLapTimeSeconds:F1}s   Length: {metrics.LapLengthMeters / 1000f:F2}km   Turns: {metrics.TurnCount}");
                sb.AppendLine($"Loops {metrics.LoopCount} | Corkscrews {metrics.CorkscrewCount} | Spirals {metrics.SpiralCount} | Half-loops {metrics.HalfLoopCount} | Jumps {metrics.JumpCount}");
                sb.AppendLine($"Full pipes {metrics.FullPipeCount} | Wallrides {metrics.WallrideCount} | Dual quarters {metrics.DualRoadQuarterCount}");
                sb.AppendLine($"Rings {metrics.TotalRings} | Max facet {metrics.MaxFacetAngleObserved:F2}°");
            }
            sb.AppendLine($"Layout lock: {report.LayoutLockMode}");
            if (report.LockedSettingsGroups.Count > 0) sb.AppendLine($"Locked groups: {string.Join(", ", report.LockedSettingsGroups)}");
            if (report.PreservedStreams.Count > 0) sb.AppendLine($"Preserved streams: {string.Join(", ", report.PreservedStreams)}");
            if (report.ChangedStreams.Count > 0) sb.AppendLine($"Changed streams: {string.Join(", ", report.ChangedStreams)}");
            foreach (var d in report.ConnectorDecisions) sb.AppendLine("Connector: " + d);
            if (report.ConnectorShadowSummary != null && report.ConnectorShadowSummary.BoundaryCount > 0)
                sb.AppendLine("Connector shadow: " + report.ConnectorShadowSummary);
            foreach (var d in report.ConnectorShadowRecords) sb.AppendLine("Connector shadow detail: " + d);
            foreach (var slot in report.TopologySlots) sb.AppendLine("Topology slot: " + slot);
            foreach (var r in report.SubdivisionRegions) sb.AppendLine("Region: " + r);
            foreach (var b in report.QuarterBalance) sb.AppendLine(b.ToString());
            foreach (var w in report.Warnings) sb.AppendLine("Warning: " + w);
            foreach (var f in report.Failures) sb.AppendLine("Failure: " + f);
            return sb.ToString();
        }

        // ═══════════════════════════ Lock panel ═══════════════════════════

        private void DrawLockPanel(TrackGenerator generator)
        {
            EditorGUILayout.Space(4);
            _showLocks = EditorGUILayout.Foldout(_showLocks,
                $"Locks · {generator.LayoutLockMode} layout · " +
                $"{generator.SettingsLocks.LockedCount}/{SettingsLockState.GroupNames.Length} settings groups", true);
            if (!_showLocks) return;

            var locks = generator.SettingsLocks;
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                SectionHeader("LAYOUT PRESERVATION");
                EditorGUILayout.LabelField(
                    "Choose how much of the generated route must stay unchanged during regeneration.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    LayoutLockMode mode = (LayoutLockMode)EditorGUILayout.EnumPopup(
                        new GUIContent("Mode", "Unlocked changes everything. Skeleton preserves route topology. Geometry preserves the exact shape."),
                        generator.LayoutLockMode);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(generator, "Layout Lock Mode");
                        generator.LayoutLockMode = mode;
                        EditorUtility.SetDirty(generator);
                    }
                    if (Btn("Keep Current Layout",
                        "Preserves the current corner plan and quarter topology on future regeneration commands.", 20f))
                    {
                        Undo.RecordObject(generator, "Lock Current Layout");
                        generator.LayoutLockMode = LayoutLockMode.Skeleton;
                        EditorUtility.SetDirty(generator);
                    }
                }

                SectionHeader("DESIGN SETTING LOCKS");
                EditorGUILayout.LabelField(
                    "Checked groups keep your hand-tuned values when profiles or randomization are applied.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                for (int i = 0; i < SettingsLockState.GroupNames.Length; i += 2)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool a = EditorGUILayout.ToggleLeft(SettingsLockState.GroupNames[i], locks.Get(i));
                        if (a != locks.Get(i)) { Undo.RecordObject(generator, "Toggle Lock"); locks.Set(i, a); }
                        if (i + 1 < SettingsLockState.GroupNames.Length)
                        {
                            bool b = EditorGUILayout.ToggleLeft(SettingsLockState.GroupNames[i + 1], locks.Get(i + 1));
                            if (b != locks.Get(i + 1)) { Undo.RecordObject(generator, "Toggle Lock"); locks.Set(i + 1, b); }
                        }
                    }
                }
                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(generator);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Lock All"))
                    {
                        Undo.RecordObject(generator, "Lock All");
                        locks.SetAll(true);
                        EditorUtility.SetDirty(generator);
                    }
                    if (GUILayout.Button("Unlock All"))
                    {
                        Undo.RecordObject(generator, "Unlock All");
                        locks.SetAll(false);
                        EditorUtility.SetDirty(generator);
                    }
                    if (GUILayout.Button("Invert"))
                    {
                        Undo.RecordObject(generator, "Invert Locks");
                        locks.Invert();
                        EditorUtility.SetDirty(generator);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Lock Modified Groups"))
                    {
                        Undo.RecordObject(generator, "Lock Modified Groups");
                        var locked = PresetApplicator.LockModifiedGroups(generator.Designer, locks);
                        _lastPresetApplication = locked.Count > 0
                            ? $"Locked modified groups: {string.Join(", ", locked)}"
                            : "No groups differ from the applied preset.";
                        EditorUtility.SetDirty(generator);
                    }
                    if (GUILayout.Button("Reset Unlocked Groups"))
                    {
                        Undo.RecordObject(generator, "Reset Unlocked Groups");
                        var result = PresetApplicator.ResetUnlockedGroups(generator.Designer, locks);
                        _lastPresetApplication = $"Reset: {string.Join(", ", result.ChangedGroups)}" +
                            (result.SkippedGroups.Count > 0 ? $" | kept (locked): {string.Join(", ", result.SkippedGroups)}" : "");
                        EditorUtility.SetDirty(generator);
                    }
                }
            }
        }

        // ═══════════════════════════ Resolved preview / stats ═══════════════════════════

        private void DrawResolvedPreview(TrackGenerator generator)
        {
            EditorGUILayout.Space(6);
            _showResolvedPreview = EditorGUILayout.Foldout(_showResolvedPreview, "Validate: Resolved Preview (derived values before generation)", true);
            if (!_showResolvedPreview) return;

            if (generator.Config == null)
            {
                EditorGUILayout.HelpBox("Assign a TrackConfig rulebook to see the resolved preview.", MessageType.Warning);
                return;
            }

            // CACHED: settings resolve once per real change (edit, undo, preset apply,
            // Validate click) — never per repaint. The old per-repaint Clone()+Resolve()
            // was the inspector's main performance killer.
            _cache.RefreshResolvedIfNeeded(generator.Config, generator.Designer);
            if (!_cache.HasResolved)
            {
                EditorGUILayout.HelpBox("Resolved preview unavailable.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(_cache.ResolvedSummary, MessageType.Info);

            foreach (var issue in _cache.ResolvedIssues)
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
    }

    /// <summary>
    /// The seed component is infrastructure, so its default serialized Inspector is far
    /// more technical than the designer needs. Keep the everyday seed choice compact and
    /// move bookmarks and hashes behind one explicit diagnostics foldout.
    /// </summary>
    [CustomEditor(typeof(TrackSeedManager))]
    public sealed class TrackSeedManagerEditor : UnityEditor.Editor
    {
        private bool _showSeedDetails;
        private GUIStyle _seedTitleStyle;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty config = serializedObject.FindProperty("config");
            SerializedProperty seedInput = serializedObject.FindProperty("currentSeedInput");
            SerializedProperty useRandom = serializedObject.FindProperty("useRandomSeed");
            SerializedProperty activeSeed = serializedObject.FindProperty("activeSeed");
            SerializedProperty savedSeeds = serializedObject.FindProperty("savedSeeds");
            SerializedProperty lastHash = serializedObject.FindProperty("lastGeneratedHash");

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                Rect accent = EditorGUILayout.GetControlRect(false, 3f);
                EditorGUI.DrawRect(accent, new Color(0.27f, 0.66f, 0.70f, 1f));
                EditorGUILayout.Space(3f);
                _seedTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                EditorGUILayout.LabelField("SEED SOURCE", _seedTitleStyle);
                EditorGUILayout.LabelField(
                    "Controls whether new tracks use a fresh seed or a repeatable number.",
                    EditorStyles.wordWrappedMiniLabel);

                if (config != null)
                    EditorGUILayout.PropertyField(config, new GUIContent("Rulebook"));
                if (useRandom != null)
                    EditorGUILayout.PropertyField(useRandom, new GUIContent("Fresh Seed Each Time"));
                if (seedInput != null)
                {
                    using (new EditorGUI.DisabledScope(useRandom != null && useRandom.boolValue))
                        EditorGUILayout.PropertyField(seedInput, new GUIContent("Master Seed"));
                }
            }

            _showSeedDetails = EditorGUILayout.Foldout(_showSeedDetails,
                $"Bookmarks & diagnostics ({savedSeeds?.arraySize ?? 0})", true);
            if (_showSeedDetails)
            {
                using (new EditorGUILayout.VerticalScope("HelpBox"))
                using (new EditorGUI.DisabledScope(true))
                {
                    if (activeSeed != null) EditorGUILayout.PropertyField(activeSeed);
                    if (savedSeeds != null) EditorGUILayout.PropertyField(savedSeeds, true);
                    if (lastHash != null) EditorGUILayout.PropertyField(lastHash, new GUIContent("Last Layout Hash"));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
