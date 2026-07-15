#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Editor
{
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
        private bool _showLocks = true;
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
        private static readonly string[] SettingsFieldNames =
        {
            "Designer", "Config", "MainRoadMaterial", "WallMaterial", "trackRoot",
            "buildRaceCourse", "checkpointCount", "startLineArcOffset",
            "generateOnStart", "keepEditorTrackOnPlay", "placeHovercraftOnStart",
            "startLineForwardOffset", "resetHovercraftWithBackspace", "seedStreams"
        };

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
                _headerWatch.Restart();
                DrawTopToolbar(generator);
                DrawReportCard(generator);
                DrawLockPanel(generator);
                DrawInspectorDiagnostics(generator);
                _headerWatch.Stop();
                _headerTiming.Record(_headerWatch.Elapsed.TotalMilliseconds, Event.current.type);
                WarnIfSlow("header", _headerWatch.Elapsed.TotalMilliseconds);
            }));

            // ── Settings: retained-mode property fields ──
            var settingsBlock = new VisualElement { style = { marginTop = 6 } };
            foreach (var name in SettingsFieldNames)
            {
                var prop = serializedObject.FindProperty(name);
                if (prop != null)
                {
                    settingsBlock.Add(new PropertyField(prop));
                    _settingsPropertyFieldCount++;
                }
            }
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

        private static void SectionHeader(string title)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        }

        private static bool Btn(string label, string tooltip, float height = 22f)
            => GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Height(height));

        /// <summary>Any command that regenerates or mutates settings invalidates the caches.</summary>
        private void AfterGeneratorCommand(TrackGenerator generator)
        {
            _cache.MarkSettingsDirty(); // relaxation policies may have adjusted settings
            EditorUtility.SetDirty(generator);
        }

        private void DrawTopToolbar(TrackGenerator generator)
        {
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                // Provenance line.
                string provenance = string.IsNullOrEmpty(generator.Designer.AppliedPresetName)
                    ? "Custom (no preset applied)"
                    : generator.Designer.ModifiedSincePreset
                        ? $"Custom — based on {generator.Designer.AppliedPresetName}"
                        : generator.Designer.AppliedPresetName;
                EditorGUILayout.LabelField($"Settings: {provenance}", EditorStyles.miniLabel);

                // ── PRESETS ──
                SectionHeader("PRESETS");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Style", GUILayout.Width(40));
                    _styleIndex = EditorGUILayout.Popup(_styleIndex, StyleOptions());
                    EditorGUILayout.LabelField("Difficulty", GUILayout.Width(58));
                    _difficultyIndex = EditorGUILayout.Popup(_difficultyIndex, DifficultyOptions);
                    EditorGUILayout.LabelField("Size", GUILayout.Width(32));
                    _sizeIndex = EditorGUILayout.Popup(_sizeIndex, SizeOptions);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    _randomMode = (PresetRandomizationMode)EditorGUILayout.EnumPopup("Random Mode", _randomMode);
                    var seedManager = generator.GetComponent<TrackSeedManager>();
                    if (seedManager != null)
                    {
                        seedManager.UseRandomSeed = EditorGUILayout.ToggleLeft(
                            new GUIContent("Random Seed", "On: every Generate New Track draws a fresh master seed. Off: the seed field is used verbatim."),
                            seedManager.UseRandomSeed, GUILayout.Width(100));
                        using (new EditorGUI.DisabledScope(seedManager.UseRandomSeed))
                            seedManager.CurrentSeedInput = EditorGUILayout.IntField(seedManager.CurrentSeedInput);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Apply Presets",
                        "Applies the selected Style/Difficulty/Size to the designer settings. Locked settings groups are skipped; Random selectors resolve deterministically from the PresetResolution seed. Does NOT generate a track."))
                        ApplyPresets(generator);
                    if (Btn("Reset Unlocked",
                        "Restores every UNLOCKED settings group to the last applied preset snapshot. Locked groups keep their manual edits. Does NOT generate a track."))
                    {
                        Undo.RecordObject(generator, "Reset Unlocked Groups");
                        PresetApplicator.ResetUnlockedGroups(generator.Designer, generator.SettingsLocks);
                        EditorUtility.SetDirty(generator);
                    }
                }

                // ── RANDOMIZATION ──
                SectionHeader("RANDOMIZATION");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Randomize All Unlocked",
                        "Re-rolls every seed stream the layout lock allows (Unlocked: everything; Skeleton: keeps Layout+Quarter; Geometry: only Surface+Visual) and regenerates. Locked settings groups are never modified."))
                    {
                        Undo.RecordObject(generator, "Randomize All Unlocked");
                        generator.RandomizeAllUnlocked();
                        AfterGeneratorCommand(generator);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Same Layout, New Content",
                        "Keeps the corner skeleton and quarter topology (Layout + Quarter seeds); re-rolls features, elevation, surface and visuals. On failure the previous track is preserved."))
                    {
                        generator.SameLayoutNewContent();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("Same Geometry, New Surface",
                        "Keeps ALL generation geometry; re-rolls only surface treatment and visuals (Surface + Visual seeds)."))
                    {
                        generator.SameGeometryNewSurface();
                        AfterGeneratorCommand(generator);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("New Features Only",
                        "Re-rolls only the Feature seed: which features fill the gaps and their parameters. Corner skeleton, quarters, elevation, surface and visuals are preserved.", 20f))
                    {
                        generator.NewFeaturesOnly();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("New Quarter Content Only",
                        "Re-rolls only the Quarter seed: which quarters are dual and their alternate road content. The corner skeleton and feature placement are preserved; existing features may become part of a dual quarter.", 20f))
                    {
                        generator.NewQuarterContentOnly();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("New Visuals Only",
                        "Re-rolls only the Visual seed: marker phasing and decoration. Geometry is untouched.", 20f))
                    {
                        generator.NewVisualsOnly();
                        AfterGeneratorCommand(generator);
                    }
                }

                // ── GENERATION ──
                SectionHeader("GENERATION");
                using (new EditorGUILayout.HorizontalScope())
                {
                    var big = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                    if (GUILayout.Button(new GUIContent("Generate New Track",
                            "Draws a fresh master seed (all streams re-derive) and generates a completely new track. On failure the previous valid track is preserved and the report card explains why."),
                            big, GUILayout.Height(34)))
                    {
                        generator.GenerateNewEverything();
                        AfterGeneratorCommand(generator);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Regenerate Same Seed",
                        "Deterministic re-run of the current master seed with the current settings — the same track unless settings changed."))
                    {
                        generator.RegenerateSameSettings();
                        AfterGeneratorCommand(generator);
                    }
                    if (Btn("Validate Current Track",
                        "Resolves the current settings against the rulebook and shows the derived values and any clamp warnings below. Does not modify the track."))
                        _showResolvedPreview = true;
                }

                // ── LOCKS ──
                SectionHeader("LOCKS");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var mode = (LayoutLockMode)EditorGUILayout.EnumPopup(
                        new GUIContent("Layout Lock", "Unlocked: everything may regenerate. Skeleton: corner plan + quarter topology preserved. Geometry: exact geometry preserved, only surface/visuals change."),
                        generator.LayoutLockMode);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(generator, "Layout Lock Mode");
                        generator.LayoutLockMode = mode;
                        EditorUtility.SetDirty(generator);
                    }
                    if (Btn("Lock Current Layout",
                        "Sets the layout lock to Skeleton: the current corner plan and quarter topology survive every regeneration command."))
                    {
                        Undo.RecordObject(generator, "Lock Current Layout");
                        generator.LayoutLockMode = LayoutLockMode.Skeleton;
                        EditorUtility.SetDirty(generator);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Lock Modified Groups",
                        "Locks every settings group whose values differ from the last applied preset — your manual edits survive future presets and randomization.", 20f))
                    {
                        Undo.RecordObject(generator, "Lock Modified Groups");
                        PresetApplicator.LockModifiedGroups(generator.Designer, generator.SettingsLocks);
                        EditorUtility.SetDirty(generator);
                    }
                    if (Btn("Lock All", "Locks every settings group.", 20f))
                    {
                        Undo.RecordObject(generator, "Lock All Groups");
                        generator.SettingsLocks.SetAll(true);
                        EditorUtility.SetDirty(generator);
                    }
                    if (Btn("Unlock All", "Unlocks every settings group.", 20f))
                    {
                        Undo.RecordObject(generator, "Unlock All Groups");
                        generator.SettingsLocks.SetAll(false);
                        EditorUtility.SetDirty(generator);
                    }
                }

                // ── DIAGNOSTICS ──
                SectionHeader("DIAGNOSTICS");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Export Debug Report",
                        "Writes the full text report (settings, resolved values, section+ring dump, connector audit, hotspots, quarter dissection) to a file of your choice. Never regenerates or mutates the track."))
                    {
                        string path = EditorUtility.SaveFilePanel("Export Track Debug Report",
                            System.IO.Path.GetDirectoryName(Application.dataPath),
                            $"TrackReport_{generator.LastReport?.Seed ?? 0}", "txt");
                        if (!string.IsNullOrEmpty(path))
                        {
                            TrackDebugReportExporter.Export(generator, path);
                            _lastExportPath = path;
                            EditorUtility.RevealInFinder(path);
                        }
                    }
                    if (Btn("Open Export Folder",
                        "Reveals the most recent debug export in the file browser (or the project folder when nothing was exported yet)."))
                    {
                        EditorUtility.RevealInFinder(string.IsNullOrEmpty(_lastExportPath)
                            ? System.IO.Path.GetDirectoryName(Application.dataPath)
                            : _lastExportPath);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Copy Debug Summary",
                        "Copies the last generation report (metrics, streams, warnings, failures) to the clipboard, paste-friendly.", 20f))
                        EditorGUIUtility.systemCopyBuffer = FullReportText(generator.LastReport, generator.LastMetrics);
                    if (Btn("Focus Primary Failure",
                        "Frames the scene view on the position of the last recorded failure (when the failure was positional).", 20f))
                        FocusPrimaryFailure(generator);
                    if (Btn("Toggle Debug View",
                        "Enables/disables the track debug visualizer component (quarter boundaries, road colors, wall masks, open edges).", 20f))
                        ToggleDebugView(generator);
                }

                // ── TRACK MANAGEMENT (destructive — kept visually separate) ──
                SectionHeader("TRACK MANAGEMENT");
                using (new EditorGUILayout.HorizontalScope())
                {
                    var warnStyle = new GUIStyle(GUI.skin.button);
                    warnStyle.normal.textColor = new Color(1f, 0.55f, 0.4f);
                    if (GUILayout.Button(new GUIContent("Clear Generated Track",
                            "Destroys the current generated track GameObjects after confirmation. Settings, seeds and locks are kept — Regenerate Same Seed rebuilds the identical track."),
                            warnStyle, GUILayout.Height(20)))
                    {
                        if (EditorUtility.DisplayDialog("Clear Track",
                                "Destroy the current generated track?", "Clear", "Cancel"))
                        {
                            Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, "Clear Track");
                            if (generator.TrackRoot != null)
                                Undo.RegisterFullObjectHierarchyUndo(generator.TrackRoot.gameObject, "Clear Track");
                            generator.ClearTrack();
                            EditorUtility.SetDirty(generator);
                        }
                    }
                }

                // ── PRESET ASSETS ──
                SectionHeader("PRESET ASSETS");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Btn("Save Current as Preset Asset",
                        "Saves the current designer settings as a reusable TrackStylePreset asset in the project.", 20f))
                        SaveAsPresetAsset(generator.Designer);
                }

                if (!string.IsNullOrEmpty(_lastPresetApplication))
                    EditorGUILayout.HelpBox(_lastPresetApplication, MessageType.None);
            }
        }

        /// <summary>Resolves Random selectors (curated), applies style+difficulty+size lock-aware.</summary>
        private void ApplyPresets(TrackGenerator generator)
        {
            Undo.RecordObject(generator, "Apply Presets");

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
            vis.enabled = !vis.enabled;
            SceneView.RepaintAll();
        }

        // ═══════════════════════════ Report card ═══════════════════════════

        private static GUIStyle _reportHeaderStyle;
        private static GUIStyle _reportBodyStyle;
        private static GUIStyle _miniWrapStyle;

        private static void EnsureReportStyles()
        {
            _reportHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            _reportBodyStyle ??= new GUIStyle(EditorStyles.label) { fontSize = 12, richText = true, wordWrap = true };
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

            Color bg = _cache.ReportSuccess
                ? (report.Warnings.Count > 0 ? new Color(0.85f, 0.75f, 0.2f, 0.25f) : new Color(0.2f, 0.8f, 0.3f, 0.22f))
                : new Color(0.9f, 0.25f, 0.2f, 0.28f);

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = bg * 4f;
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                GUI.backgroundColor = prev;

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
                    if (GUILayout.Button("Copy Report", GUILayout.Width(100)))
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
            sb.AppendLine($"Attempts: {report.AttemptsEvaluated}   Valid candidates: {report.ValidCandidateCount}   Score: {report.SelectedCandidateScore:F1}");
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
                $"Settings Locks ({generator.SettingsLocks.LockedCount}/{SettingsLockState.GroupNames.Length} locked)", true);
            if (!_showLocks) return;

            var locks = generator.SettingsLocks;
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
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
                    if (GUILayout.Button("Lock All")) { Undo.RecordObject(generator, "Lock All"); locks.SetAll(true); EditorUtility.SetDirty(generator); }
                    if (GUILayout.Button("Unlock All")) { Undo.RecordObject(generator, "Unlock All"); locks.SetAll(false); EditorUtility.SetDirty(generator); }
                    if (GUILayout.Button("Invert")) { Undo.RecordObject(generator, "Invert Locks"); locks.Invert(); EditorUtility.SetDirty(generator); }
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
}
#endif
