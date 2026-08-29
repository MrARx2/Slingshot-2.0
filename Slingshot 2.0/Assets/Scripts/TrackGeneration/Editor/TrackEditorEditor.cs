using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Editor
{
    /// <summary>
    /// Dedicated post-generation authoring inspector. The generator inspector stays
    /// focused on generation; topology selection and future replacement work lives here.
    /// </summary>
    [CustomEditor(typeof(TrackEditor))]
    public sealed class TrackEditorEditor : UnityEditor.Editor
    {
        private string _filter = "";
        private bool _showBlocked;
        private bool _showRequests;
        private bool _showTechnicalResult;
        private GUIStyle _wrapStyle;
        private static GUIStyle _cardTitleStyle;
        private static GUIStyle _originalChoiceIconStyle;
        private string _replacementCheckKey = "";
        private FeaturePlanResult _replacementCheckCandidate;
        private TopologyReplacementGeometryResult _replacementCheckGeometry;
        private InspectionPreview _inspectionPreview;
        private string _applyMessage = "";
        private bool _applySucceeded;
        private string _applyFailureClipboardReport = "";
        private string _regenerationMessage = "";
        private bool _regenerationSucceeded;
        private string _regenerationFailureClipboardReport = "";
        private string _clipboardStatus = "";
        private string _recoveryMessage = "";
        private string _undoMessage = "";
        private bool _undoSucceeded;
        private const string SectionSearchControlName = "TrackEditor.SectionSearch";

        // Match the Track Generator's restrained language: cyan for navigation,
        // violet for authoring, and semantic colors only for actual result states.
        private static readonly Color NavigationAccent = new Color(0.27f, 0.66f, 0.70f, 1f);
        private static readonly Color AuthoringAccent = new Color(0.55f, 0.41f, 0.76f, 1f);
        private static readonly Color AuthoringActionTint = new Color(0.40f, 0.30f, 0.58f, 1f);
        private static readonly Color CheckActionTint = new Color(0.22f, 0.46f, 0.52f, 1f);
        private static readonly Color SuccessTint = new Color(0.29f, 0.60f, 0.47f, 1f);
        private static readonly Color WarningTint = new Color(0.72f, 0.52f, 0.22f, 1f);
        private static readonly Color CurrentTint = new Color(0.24f, 0.48f, 0.50f, 1f);
        private static readonly Color SelectedTint = new Color(0.46f, 0.36f, 0.62f, 1f);
        private static readonly Color RequestedTint = new Color(0.54f, 0.43f, 0.70f, 1f);
        private static readonly Color MutedTint = new Color(0.60f, 0.60f, 0.62f, 1f);

        private sealed class InspectionPreview
        {
            public string Key;
            public SemanticElementId Realization;
            public string FailureReason = "";
            public readonly List<GeneratedTrackSection> Sections = new List<GeneratedTrackSection>();
            public readonly List<GeneratedTrackSection> OwnedSections = new List<GeneratedTrackSection>();
            public TrackConnectionFrame EntryFrame;
            public TrackConnectionFrame CandidateExitFrame;
            public TrackConnectionFrame OwnedExitFrame;
            public Bounds LocalBounds;
            public bool HasBounds;

            public bool Valid => string.IsNullOrEmpty(FailureReason) && Sections.Count > 0;
            public float BoundaryDistance => Vector3.Distance(
                CandidateExitFrame.Position, OwnedExitFrame.Position);
        }

        public override void OnInspectorGUI()
        {
            var trackEditor = (TrackEditor)target;
            serializedObject.Update();

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("TRACK EDITOR",
                    "Choose a section, preview another design, then rebuild the route safely.",
                    AuthoringAccent);

                EditorGUI.BeginChangeCheck();
                TrackGenerator boundGenerator = (TrackGenerator)EditorGUILayout.ObjectField(
                    new GUIContent("Source Track", "The generated track being edited."),
                    trackEditor.Generator, typeof(TrackGenerator), true);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(trackEditor, "Bind Track Editor");
                    trackEditor.Bind(boundGenerator);
                    EditorUtility.SetDirty(trackEditor);
                }
            }

            TrackGenerator generator = trackEditor.Generator;

            if (generator == null)
            {
                EditorGUILayout.HelpBox("Assign a TrackGenerator, or use Find Track Generator.", MessageType.Warning);
                if (AccentButton("FIND TRACK GENERATOR",
                        "Find and bind the TrackGenerator in the current scene.", NavigationAccent, 26f))
                {
                    TrackGenerator found = UnityEngine.Object.FindAnyObjectByType<TrackGenerator>(
                        FindObjectsInactive.Include);
                    if (found != null)
                    {
                        Undo.RecordObject(trackEditor, "Bind Track Editor");
                        trackEditor.Bind(found);
                        EditorUtility.SetDirty(trackEditor);
                    }
                }
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawAcceptedEditUndo(trackEditor, generator);
            DrawBrowser(trackEditor, generator);
            DrawRequestedChanges(trackEditor, generator.EditableTopologySlots);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAcceptedEditUndo(TrackEditor trackEditor, TrackGenerator generator)
        {
            if (!trackEditor.CanUndoLastAppliedEdit && string.IsNullOrWhiteSpace(_undoMessage))
                return;

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("DESIGN HISTORY",
                    trackEditor.CanUndoLastAppliedEdit
                        ? "Return to the complete accepted track from immediately before your latest edit."
                        : "The latest Track Editor history action.",
                    NavigationAccent);

                if (trackEditor.CanUndoLastAppliedEdit)
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(trackEditor.LastAppliedEditUndoLabel)
                            ? "Last accepted Track Editor change"
                            : trackEditor.LastAppliedEditUndoLabel,
                        EditorStyles.wordWrappedMiniLabel);
                    if (AccentButton("UNDO LAST APPLIED CHANGE",
                            "Rebuild the exact accepted recipe from before the latest successful Track Editor change.",
                            NavigationAccent, 30f))
                    {
                        string recipe = trackEditor.LastAppliedEditUndoRecipe;
                        if (generator.RestoreAcceptedRecipeSnapshot(recipe, out string error))
                        {
                            Undo.RecordObject(trackEditor, "Undo Last Applied Track Change");
                            trackEditor.ClearLastAppliedEditUndo();
                            trackEditor.ClearRequestedOverrides();
                            trackEditor.SelectAlternative(SemanticElementId.None);
                            EditorUtility.SetDirty(trackEditor);
                            _undoMessage = "Previous accepted track restored.";
                            _undoSucceeded = true;
                            ClearInspectionState();
                            SceneView.RepaintAll();
                            Repaint();
                            GUIUtility.ExitGUI();
                        }

                        _undoMessage = string.IsNullOrWhiteSpace(error)
                            ? "The previous accepted track could not be restored. Your current track was kept."
                            : error;
                        _undoSucceeded = false;
                    }
                }

                if (!string.IsNullOrWhiteSpace(_undoMessage))
                    EditorGUILayout.HelpBox(_undoMessage,
                        _undoSucceeded ? MessageType.Info : MessageType.Error);
            }
        }

        private void DrawBrowser(TrackEditor trackEditor, TrackGenerator generator)
        {
            List<TopologySlotRecord> allSlots = generator.EditableTopologySlots;
            if (allSlots == null || allSlots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    generator.LastAcceptedRecipe != null
                        ? "The last edit was rejected, but your previous track is still safe. Restore its editor index to continue editing."
                        : "No editable track is available. Generate a valid track, then return here.",
                    generator.LastAcceptedRecipe != null ? MessageType.Warning : MessageType.None);

                if (generator.LastAcceptedRecipe != null &&
                    AccentButton("RESTORE TRACK EDITOR",
                        "Replay the last accepted recipe and rebuild this editor's section index.",
                        NavigationAccent, 28f))
                {
                    if (generator.RegenerateExactRecipe(out string error))
                    {
                        _recoveryMessage = "Track Editor restored from the last accepted recipe.";
                        ClearInspectionState();
                        EditorUtility.SetDirty(generator);
                        SceneView.RepaintAll();
                        Repaint();
                        GUIUtility.ExitGUI();
                    }

                    _recoveryMessage = string.IsNullOrWhiteSpace(error)
                        ? "The accepted track could not be restored."
                        : error;
                }

                if (!string.IsNullOrWhiteSpace(_recoveryMessage))
                    EditorGUILayout.HelpBox(_recoveryMessage, MessageType.Error);
                return;
            }

            _recoveryMessage = "";

            TopologySlotRecord selected;
            List<GeneratedTrackSection> owned;
            TrackConnectionFrame entry;
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("1 · CHOOSE A SECTION",
                    $"{allSlots.Count} editable sections. Choosing one automatically frames it in the Scene view.",
                    NavigationAccent);

                // Treat search and route navigation as one composition. A regular
                // labelled property field uses Unity's wide inspector label gutter,
                // which leaves the input visually detached from the controls below.
                EditorGUILayout.LabelField("SEARCH SECTIONS", EditorStyles.miniBoldLabel);
                Rect searchRect = EditorGUILayout.GetControlRect(false, 22f);
                GUI.SetNextControlName(SectionSearchControlName);
                _filter = GUI.TextField(searchRect, _filter ?? "", EditorStyles.toolbarSearchField);
                GUILayout.Space(2f);

                var visible = new List<TopologySlotRecord>();
                for (int i = 0; i < allSlots.Count; i++)
                {
                    TopologySlotRecord slot = allSlots[i];
                    if (slot != null && SlotMatchesFilter(slot, _filter)) visible.Add(slot);
                }
                if (visible.Count == 0)
                {
                    EditorGUILayout.HelpBox("No topology slots match this filter.", MessageType.None);
                    return;
                }

                int selectedIndex = visible.FindIndex(s => string.Equals(
                    s.TopologySlotId, trackEditor.SelectedTopologySlotId, StringComparison.Ordinal));
                bool selectionMissing = selectedIndex < 0;
                if (selectionMissing) selectedIndex = 0;

                bool userChangedSelection = false;
                Event currentEvent = Event.current;
                bool searchHasFocus = string.Equals(GUI.GetNameOfFocusedControl(),
                    SectionSearchControlName, StringComparison.Ordinal);
                if (!searchHasFocus && currentEvent.type == EventType.KeyDown)
                {
                    bool previousKey = currentEvent.keyCode == KeyCode.LeftArrow ||
                                       currentEvent.keyCode == KeyCode.Comma;
                    bool nextKey = currentEvent.keyCode == KeyCode.RightArrow ||
                                   currentEvent.keyCode == KeyCode.Period;
                    if (previousKey)
                    {
                        selectedIndex = WrapSectionIndex(selectedIndex - 1, visible.Count);
                        userChangedSelection = true;
                        currentEvent.Use();
                    }
                    else if (nextKey)
                    {
                        selectedIndex = WrapSectionIndex(selectedIndex + 1, visible.Count);
                        userChangedSelection = true;
                        currentEvent.Use();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(visible.Count <= 1))
                    {
                        if (GUILayout.Button(new GUIContent("◀",
                                "Previous section. Wraps from the first section to the last."),
                                EditorStyles.miniButtonLeft, GUILayout.Width(30f), GUILayout.Height(24f)))
                        {
                            selectedIndex = WrapSectionIndex(selectedIndex - 1, visible.Count);
                            userChangedSelection = true;
                        }
                    }

                    TopologySlotRecord navigatorSlot = visible[Mathf.Clamp(
                        selectedIndex, 0, visible.Count - 1)];
                    if (GUILayout.Button(
                            new GUIContent(SlotNavigatorLabel(navigatorSlot),
                                "Open the route section picker."),
                            EditorStyles.miniButtonMid, GUILayout.Height(24f)))
                    {
                        ShowSlotPicker(trackEditor, generator, visible);
                    }

                    using (new EditorGUI.DisabledScope(visible.Count <= 1))
                    {
                        if (GUILayout.Button(new GUIContent("▶",
                                "Next section. Wraps from the last section to the first."),
                                EditorStyles.miniButtonRight, GUILayout.Width(30f), GUILayout.Height(24f)))
                        {
                            selectedIndex = WrapSectionIndex(selectedIndex + 1, visible.Count);
                            userChangedSelection = true;
                        }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        new GUIContent($"{selectedIndex + 1} / {visible.Count}",
                            "Keyboard: use Left/Right Arrow or </> to cycle continuously between sections."),
                        EditorStyles.centeredGreyMiniLabel, GUILayout.Width(54f), GUILayout.Height(18f));
                    GUILayout.FlexibleSpace();
                }
                if (userChangedSelection || selectionMissing ||
                    string.IsNullOrEmpty(trackEditor.SelectedTopologySlotId))
                {
                    Undo.RecordObject(trackEditor, "Select Topology Segment");
                    trackEditor.SelectSlot(visible[Mathf.Clamp(selectedIndex, 0, visible.Count - 1)].TopologySlotId);
                    EditorUtility.SetDirty(trackEditor);
                    _inspectionPreview = null;
                    _replacementCheckKey = "";
                    _replacementCheckCandidate = null;
                    _replacementCheckGeometry = null;
                    _applyMessage = "";
                    _applySucceeded = false;
                }

                selected = visible[Mathf.Clamp(selectedIndex, 0, visible.Count - 1)];
                List<GeneratedTrackSection> sections = CurrentSections(generator);
                owned = FindSlotSections(sections, selected.TopologySlotId);
                entry = owned.Count > 0 ? owned[0].StartFrame : default;
                float length = 0f;
                for (int i = 0; i < owned.Count; i++)
                    length += Mathf.Max(0f, owned[i].Definition?.Length ?? 0f);

                TopologySlotCatalog.TryGetNeighbours(allSlots, selected.TopologySlotId,
                    out TopologySlotRecord previous, out TopologySlotRecord next);
                string neighbours = $"{(previous != null ? previous.CurrentRealization.ToString() : "route start")}  →  " +
                                    $"{selected.CurrentRealization}  →  " +
                                    $"{(next != null ? next.CurrentRealization.ToString() : "route end")}";

                using (new EditorGUILayout.VerticalScope("HelpBox"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(selected.CurrentRealization.ToString(), CardTitleStyle());
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(
                            $"{selected.SignedHeadingDelta:+0.#;-0.#;0}°",
                            EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                    }
                    EditorGUILayout.LabelField(selected.TopologySlotId, EditorStyles.miniLabel);
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("Demand", selected.DemandType.ToString());
                    EditorGUILayout.LabelField("Route",
                        $"Quarter {selected.QuarterIndex + 1} · Road {(selected.RoadId == 0 ? "A" : "B")} · slot {selected.RouteOrder}");
                    EditorGUILayout.LabelField("Owned geometry",
                        owned.Count > 0 ? $"{owned.Count} section(s) · {length:0.#} m" : "not loaded");
                    EditorGUILayout.LabelField(new GUIContent("Neighbors", neighbours),
                        new GUIContent(neighbours));

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent("Recenter",
                                "Frame this section again if you moved the Scene camera."),
                                EditorStyles.miniButton, GUILayout.Width(72f), GUILayout.Height(21f)))
                            FocusSlot(generator, selected.TopologySlotId, owned);
                        GUIContent clipboard = EditorGUIUtility.IconContent("Clipboard");
                        clipboard.tooltip = "Copy the stable topology-slot ID.";
                        if (GUILayout.Button(clipboard, GUILayout.Width(28f), GUILayout.Height(21f)))
                            GUIUtility.systemCopyBuffer = selected.TopologySlotId;
                    }
                }

                if (userChangedSelection)
                    FocusSlot(generator, selected.TopologySlotId, owned);
            }

            if (owned.Count == 0)
                EditorGUILayout.HelpBox(
                    "This saved slot has no live section frames. Repair or regenerate the preview before assessing alternatives.",
                    MessageType.Warning);

            List<TopologySlotCompatibilityResult> compatibility =
                TopologySlotCompatibility.Evaluate(selected, entry);
            var alternatives = new List<TopologySlotCompatibilityResult>();
            var blocked = new List<TopologySlotCompatibilityResult>();
            for (int i = 0; i < compatibility.Count; i++)
            {
                TopologySlotCompatibilityResult result = compatibility[i];
                if (result.Status == TopologySlotCompatibilityStatus.Blocked) blocked.Add(result);
                else alternatives.Add(result);
            }

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("2 · PREVIEW A DESIGN",
                    $"{alternatives.Count} designs match this section. Click Preview again to hide the ghost. " +
                    "Your live track is never changed here.",
                    AuthoringAccent);
                if (selected.OverrideState == TopologySlotOverrideState.Applied &&
                    selected.OriginalRealization != SemanticElementId.None &&
                    selected.CurrentRealization != selected.OriginalRealization)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawOriginalChoiceIcon(selected.OriginalRealization, true);
                        EditorGUILayout.LabelField(
                            "Original generated choice — selecting it restores the exact original recipe.",
                            EditorStyles.miniLabel);
                    }
                    EditorGUILayout.Space(2f);
                }
                for (int i = 0; i < alternatives.Count; i++)
                    DrawCandidate(trackEditor, generator, selected, entry, owned, alternatives[i]);
            }

            TrackEditorImpactPlan displayedImpact = DrawAreaOfImpact(
                trackEditor, generator, selected, allSlots);

            SemanticElementId selectedAlternative = trackEditor.SelectedAlternative;
            bool hasSelectedReplacement = selectedAlternative != SemanticElementId.None &&
                                          selectedAlternative != selected.CurrentRealization;
            bool restoringOriginal = IsOriginalRecipeRestore(
                selected, selectedAlternative, displayedImpact);
            bool selectedPreviewVisible = hasSelectedReplacement &&
                                          _inspectionPreview != null &&
                                          string.Equals(
                                              _inspectionPreview.Key,
                                              InspectionPreviewKey(generator, selected, selectedAlternative),
                                              StringComparison.Ordinal);
            if (hasSelectedReplacement)
            {
                TopologySlotOverride requested = trackEditor.FindRequestedOverride(selected.TopologySlotId);
                bool alreadyRequested = requested != null &&
                                        requested.RequestedRealization == trackEditor.SelectedAlternative;
                string resultKey = ReplacementCheckKey(
                    generator, selected, trackEditor.SelectedAlternative);
                bool hasCurrentResult = string.Equals(
                                            _replacementCheckKey, resultKey, StringComparison.Ordinal) &&
                                        _replacementCheckGeometry != null;
                using (new EditorGUILayout.VerticalScope("HelpBox"))
                {
                    CardHeader(restoringOriginal
                            ? "4 · RESTORE ORIGINAL"
                            : "4 · BUILD & APPLY",
                        $"{selected.CurrentRealization}  →  {trackEditor.SelectedAlternative}",
                        AuthoringAccent);

                    EditorGUILayout.LabelField(
                        restoringOriginal
                            ? "This removes the authored replacement and regenerates the original accepted recipe. " +
                              "Its sampled feature parameters, recovery, closure and surrounding route return together."
                            : "The generator will build this design, reconnect the surrounding route, and validate the complete track. " +
                              "Only this change will be attempted. Your current track is kept unless the rebuilt version passes every check.",
                        EditorStyles.wordWrappedMiniLabel);

                    if (restoringOriginal)
                        EditorGUILayout.HelpBox(
                            "The Scene ghost shows the Hairpin family. Restore uses the exact original generated Hairpin saved by the recipe, not a newly compiled generic Hairpin.",
                            MessageType.Info);

                    if (!selectedPreviewVisible)
                        EditorGUILayout.LabelField(
                            "Preview hidden · the selected design is still ready to build.",
                            EditorStyles.miniLabel);

                    TopologyReplacementPreflightResult preflight = alreadyRequested
                        ? TopologyReplacementPreflight.Evaluate(selected, requested, owned)
                        : null;
                    if (preflight != null && !preflight.Passed)
                        EditorGUILayout.HelpBox(FriendlyPreflightFailure(preflight.Reason), MessageType.Error);

                    if (hasCurrentResult)
                        DrawReplacementCheckResult(_replacementCheckCandidate, _replacementCheckGeometry);

                    using (new EditorGUI.DisabledScope(owned.Count == 0 ||
                                                       (preflight != null && !preflight.Passed)))
                    {
                        if (AccentButton(restoringOriginal
                                    ? $"RESTORE ORIGINAL {selected.OriginalRealization.ToString().ToUpperInvariant()}"
                                    : "BUILD & APPLY",
                                restoringOriginal
                                    ? "Remove the authored override and regenerate the original deterministic track recipe."
                                    : "Build this design and its connectors, validate the complete route, then replace the live track only if it succeeds.",
                                AuthoringActionTint, 34f))
                        {
                            BuildAndApplySelected(trackEditor, generator, selected, owned);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(_applyMessage))
                    {
                        EditorGUILayout.HelpBox(_applyMessage,
                            _applySucceeded ? MessageType.Info : MessageType.Error);
                        if (!_applySucceeded)
                            DrawCopyFailureReportButton(_applyFailureClipboardReport, _applyMessage);
                    }
                }
            }
            else
            {
                using (new EditorGUILayout.VerticalScope("HelpBox"))
                {
                    CardHeader("4 · REGENERATE & APPLY",
                        $"Rebuild {selected.CurrentRealization} with the selected Area of Impact.",
                        AuthoringAccent);
                    EditorGUILayout.LabelField(
                        displayedImpact.FeatureOverrideCount == 0
                            ? "The current feature and its connectors will be rebuilt. Nearby features remain protected."
                            : $"The current feature will be rebuilt and {displayedImpact.FeatureOverrideCount} nearby feature" +
                              $"{(displayedImpact.FeatureOverrideCount == 1 ? "" : "s")} may be removed after confirmation.",
                        EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUI.DisabledScope(owned.Count == 0))
                    {
                        if (AccentButton("REGENERATE CURRENT AREA",
                                "Rebuild the current feature and authorized area, validate the complete route, then replace the live track only if it succeeds.",
                                CheckActionTint, 30f))
                            BuildAndApplyCurrent(trackEditor, generator, selected, owned);
                    }
                    if (!string.IsNullOrWhiteSpace(_regenerationMessage))
                    {
                        EditorGUILayout.HelpBox(_regenerationMessage,
                            _regenerationSucceeded ? MessageType.Info : MessageType.Error);
                        if (!_regenerationSucceeded)
                            DrawCopyFailureReportButton(_regenerationFailureClipboardReport,
                                _regenerationMessage);
                    }
                }
            }

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                _showBlocked = EditorGUILayout.Foldout(_showBlocked,
                    $"Unavailable choices ({blocked.Count})", true);
                if (_showBlocked)
                {
                    EditorGUILayout.LabelField(
                        "These choices do not match the current semantic entry/exit contract.",
                        EditorStyles.wordWrappedMiniLabel);
                    for (int i = 0; i < blocked.Count; i++) DrawBlocked(blocked[i]);
                }
            }
        }

        private TrackEditorImpactPlan DrawAreaOfImpact(
            TrackEditor trackEditor,
            TrackGenerator generator,
            TopologySlotRecord selected,
            IReadOnlyList<TopologySlotRecord> allSlots)
        {
            TrackEditorImpactPlan current = TrackEditorImpact.Analyze(allSlots,
                selected.TopologySlotId, trackEditor.ImpactBackwardFeatures,
                trackEditor.ImpactForwardFeatures);
            TrackEditorImpactPlan available = TrackEditorImpact.Analyze(allSlots,
                selected.TopologySlotId, TrackEditorImpact.MaximumFeatureReach,
                TrackEditorImpact.MaximumFeatureReach);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("3 · AREA OF IMPACT",
                    "Choose how far this edit may reach in the track's travel direction. " +
                    "Zero protects nearby features; connectors and recovery road can still be rebuilt.",
                    AuthoringAccent);

                int backward = DrawImpactReachControl(
                    "BACKWARD · BEFORE", trackEditor.ImpactBackwardFeatures,
                    available.Backward.Count,
                    "Features before the selected section that this edit may consume.");
                int forward = DrawImpactReachControl(
                    "FORWARD · AFTER", trackEditor.ImpactForwardFeatures,
                    available.Forward.Count,
                    "Features after the selected section that this edit may consume.");
                if (backward != trackEditor.ImpactBackwardFeatures ||
                    forward != trackEditor.ImpactForwardFeatures)
                {
                    Undo.RecordObject(trackEditor, "Change Track Editor Area of Impact");
                    trackEditor.SetImpactReach(backward, forward);
                    EditorUtility.SetDirty(trackEditor);
                    _replacementCheckKey = "";
                    _replacementCheckCandidate = null;
                    _replacementCheckGeometry = null;
                    _applyMessage = "";
                    _regenerationMessage = "";
                    current = TrackEditorImpact.Analyze(allSlots, selected.TopologySlotId,
                        backward, forward);
                    SceneView.RepaintAll();
                }

                if (current.FeatureOverrideCount == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Focused edit: every neighboring feature stays protected.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"{current.FeatureOverrideCount} nearby feature" +
                        $"{(current.FeatureOverrideCount == 1 ? "" : "s")} may be removed so the new design can fit cleanly. " +
                        "Nothing is overridden without a final confirmation.",
                        MessageType.Warning);
                    DrawImpactMembers(trackEditor, generator, current.Backward,
                        "BEFORE");
                    DrawImpactMembers(trackEditor, generator, current.Forward,
                        "AFTER");
                }
            }
            return current;
        }

        private static int DrawImpactReachControl(
            string label, int value, int available, string tooltip)
        {
            int maximum = Mathf.Clamp(available, 0, TrackEditorImpact.MaximumFeatureReach);
            value = Mathf.Clamp(value, 0, maximum);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(label, tooltip),
                    EditorStyles.miniBoldLabel, GUILayout.Width(132f));
                using (new EditorGUI.DisabledScope(value <= 0))
                {
                    if (GUILayout.Button(new GUIContent("−", "Protect one more nearby feature."),
                            EditorStyles.miniButtonLeft, GUILayout.Width(28f), GUILayout.Height(21f)))
                        value--;
                }
                EditorGUILayout.LabelField(value == 0 ? "NONE" : value.ToString(),
                    EditorStyles.centeredGreyMiniLabel, GUILayout.Width(42f), GUILayout.Height(21f));
                using (new EditorGUI.DisabledScope(value >= maximum))
                {
                    if (GUILayout.Button(new GUIContent("+", "Allow this edit to reach one more feature."),
                            EditorStyles.miniButtonRight, GUILayout.Width(28f), GUILayout.Height(21f)))
                        value++;
                }
                GUILayout.FlexibleSpace();
            }
            return Mathf.Clamp(value, 0, maximum);
        }

        private void DrawImpactMembers(
            TrackEditor trackEditor,
            TrackGenerator generator,
            IReadOnlyList<TopologySlotRecord> members,
            string direction)
        {
            if (members == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                TopologySlotRecord member = members[i];
                if (member == null) continue;
                string suggestions = SuggestedAlternatives(generator, member);
                using (new EditorGUILayout.HorizontalScope("HelpBox"))
                {
                    EditorGUILayout.LabelField(
                        new GUIContent($"{direction} · {member.CurrentRealization}",
                            string.IsNullOrEmpty(suggestions)
                                ? "This feature may be removed by the rebuild."
                                : $"Suggested replacements: {suggestions}"),
                        WrapStyle());
                    if (GUILayout.Button(new GUIContent("EDIT",
                            string.IsNullOrEmpty(suggestions)
                                ? "Select this feature before applying the current change."
                                : $"Select this feature. Suggested alternatives: {suggestions}"),
                            EditorStyles.miniButton, GUILayout.Width(44f), GUILayout.Height(20f)))
                    {
                        Undo.RecordObject(trackEditor, "Edit Affected Track Feature");
                        trackEditor.SetImpactReach(0, 0);
                        EditorUtility.SetDirty(trackEditor);
                        SelectSlotFromPicker(trackEditor, generator, member);
                        GUIUtility.ExitGUI();
                    }
                }
                if (!string.IsNullOrEmpty(suggestions))
                    EditorGUILayout.LabelField($"Suggested instead: {suggestions}",
                        EditorStyles.centeredGreyMiniLabel);
            }
        }

        private static string SuggestedAlternatives(
            TrackGenerator generator, TopologySlotRecord slot)
        {
            List<GeneratedTrackSection> sections = FindSlotSections(CurrentSections(generator),
                slot.TopologySlotId);
            TrackConnectionFrame entry = sections.Count > 0 ? sections[0].StartFrame : default;
            List<TopologySlotCompatibilityResult> choices =
                TopologySlotCompatibility.Evaluate(slot, entry);
            var names = new List<string>();
            for (int i = 0; i < choices.Count && names.Count < 3; i++)
            {
                TopologySlotCompatibilityResult choice = choices[i];
                if (choice.Status == TopologySlotCompatibilityStatus.Blocked ||
                    choice.Status == TopologySlotCompatibilityStatus.Current)
                    continue;
                names.Add(choice.Candidate.ToString());
            }
            return string.Join(", ", names);
        }

        private void ShowSlotPicker(TrackEditor trackEditor, TrackGenerator generator,
            List<TopologySlotRecord> visible)
        {
            var menu = new GenericMenu();
            for (int i = 0; i < visible.Count; i++)
            {
                TopologySlotRecord slot = visible[i];
                if (slot == null) continue;

                string road = slot.RoadId == 0 ? "Road A" : "Road B";
                string heading = $"{slot.SignedHeadingDelta:+0.#;-0.#;0}°";
                string path = $"Quarter {slot.QuarterIndex + 1}/{road}/" +
                              $"{slot.RouteOrder + 1:00}  {slot.CurrentRealization}  {heading}";
                bool active = string.Equals(slot.TopologySlotId,
                    trackEditor.SelectedTopologySlotId, StringComparison.Ordinal);
                TopologySlotRecord captured = slot;
                menu.AddItem(new GUIContent(path), active,
                    () => SelectSlotFromPicker(trackEditor, generator, captured));
            }

            menu.ShowAsContext();
        }

        private void SelectSlotFromPicker(TrackEditor trackEditor, TrackGenerator generator,
            TopologySlotRecord slot)
        {
            if (trackEditor == null || generator == null || slot == null) return;

            Undo.RecordObject(trackEditor, "Select Topology Segment");
            trackEditor.SelectSlot(slot.TopologySlotId);
            EditorUtility.SetDirty(trackEditor);
            _inspectionPreview = null;
            _replacementCheckKey = "";
            _replacementCheckCandidate = null;
            _replacementCheckGeometry = null;
            _applyMessage = "";
            _applySucceeded = false;

            List<GeneratedTrackSection> owned = FindSlotSections(
                CurrentSections(generator), slot.TopologySlotId);
            FocusSlot(generator, slot.TopologySlotId, owned);
            Repaint();
        }

        private static string SlotNavigatorLabel(TopologySlotRecord slot)
        {
            if (slot == null) return "Choose a section";
            string road = slot.RoadId == 0 ? "A" : "B";
            string heading = $"{slot.SignedHeadingDelta:+0.#;-0.#;0}°";
            return $"Q{slot.QuarterIndex + 1} · {road} · #{slot.RouteOrder + 1}   " +
                   $"{slot.CurrentRealization}   {heading}";
        }

        /// <summary>
        /// Circular section navigation used by both the Inspector buttons and the
        /// keyboard shortcuts. The filtered list is the active navigation set.
        /// </summary>
        private static int WrapSectionIndex(int index, int count)
        {
            if (count <= 1) return 0;
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        private void DrawCandidate(TrackEditor trackEditor, TrackGenerator generator,
            TopologySlotRecord slot, in TrackConnectionFrame entry,
            List<GeneratedTrackSection> owned, TopologySlotCompatibilityResult result)
        {
            bool current = result.Status == TopologySlotCompatibilityStatus.Current;
            TopologySlotOverride requested = trackEditor.FindRequestedOverride(slot.TopologySlotId);
            bool isRequested = requested != null && requested.RequestedRealization == result.Candidate;
            bool selected = trackEditor.SelectedAlternative == result.Candidate || isRequested;
            string previewKey = InspectionPreviewKey(generator, slot, result.Candidate);
            bool previewActive = !current && _inspectionPreview != null &&
                                 string.Equals(_inspectionPreview.Key, previewKey, StringComparison.Ordinal);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = current
                ? CurrentTint
                : isRequested
                    ? RequestedTint
                    : selected
                        ? SelectedTint
                        : MutedTint;
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                GUI.backgroundColor = previousBackground;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(result.Candidate.ToString(), EditorStyles.boldLabel);
                    if (result.Candidate == slot.OriginalRealization)
                        DrawOriginalChoiceIcon(result.Candidate,
                            slot.OverrideState == TopologySlotOverrideState.Applied &&
                            slot.CurrentRealization != slot.OriginalRealization);
                    GUILayout.FlexibleSpace();
                    if (current)
                    {
                        EditorGUILayout.LabelField("IN USE", EditorStyles.miniBoldLabel, GUILayout.Width(68f));
                    }
                    else
                    {
                        if (selected)
                        {
                            Color previousContent = GUI.contentColor;
                            GUI.contentColor = AuthoringAccent;
                            GUILayout.Label(
                                new GUIContent("◆",
                                    "This is the design currently selected for this section."),
                                EditorStyles.boldLabel,
                                GUILayout.Width(16f), GUILayout.Height(18f));
                            GUI.contentColor = previousContent;
                        }
                        string button = previewActive ? "HIDE PREVIEW" : "PREVIEW";
                        Color actionTint = previewActive
                            ? SelectedTint
                            : isRequested
                                ? RequestedTint
                                : MutedTint;
                        Color buttonBackground = GUI.backgroundColor;
                        GUI.backgroundColor = actionTint;
                        if (GUILayout.Button(button, EditorStyles.miniButton, GUILayout.Width(96f), GUILayout.Height(19f)))
                        {
                            if (previewActive)
                            {
                                _inspectionPreview = null;
                                previewActive = false;
                                SceneView.RepaintAll();
                                Repaint();
                            }
                            else
                            {
                                Undo.RecordObject(trackEditor, "Select Topology Alternative");
                                trackEditor.SelectAlternative(result.Candidate);
                                EditorUtility.SetDirty(trackEditor);
                                BuildInspectionPreview(generator, slot, result.Candidate, entry, owned, true);
                            }
                        }
                        GUI.backgroundColor = buttonBackground;
                    }
                }
                if (current && slot.CurrentRealization == result.Candidate)
                {
                    EditorGUILayout.LabelField("The generated track currently uses this design.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField(
                        "Use Area of Impact below, then choose Regenerate Current Area.",
                        EditorStyles.miniLabel);
                }
                else if (isRequested)
                    EditorGUILayout.LabelField("PLANNED CHANGE · LIVE TRACK UNCHANGED",
                        EditorStyles.miniBoldLabel);
                else if (!selected)
                    EditorGUILayout.LabelField("Compatible with this section type.",
                        EditorStyles.miniLabel);

                if (previewActive && _inspectionPreview != null)
                {
                    if (_inspectionPreview.Valid)
                    {
                        EditorGUILayout.LabelField(
                            $"SCENE PREVIEW ON · {_inspectionPreview.Sections.Count} section(s)",
                            EditorStyles.miniBoldLabel);
                        EditorGUILayout.LabelField(
                            "Violet is the proposed road. Amber is the section it would replace.",
                            EditorStyles.wordWrappedMiniLabel);
                    }
                    else if (!string.IsNullOrWhiteSpace(_inspectionPreview.FailureReason))
                    {
                        EditorGUILayout.HelpBox(_inspectionPreview.FailureReason, MessageType.Warning);
                    }
                }
            }
            GUI.backgroundColor = previousBackground;
        }

        private void BuildInspectionPreview(
            TrackGenerator generator,
            TopologySlotRecord slot,
            SemanticElementId realization,
            in TrackConnectionFrame entry,
            List<GeneratedTrackSection> owned,
            bool focus)
        {
            string key = InspectionPreviewKey(generator, slot, realization);
            if (_inspectionPreview != null && string.Equals(_inspectionPreview.Key, key,
                    StringComparison.Ordinal))
            {
                if (focus) FocusInspectionPreview(generator, _inspectionPreview);
                SceneView.RepaintAll();
                return;
            }

            var preview = new InspectionPreview
            {
                Key = key,
                Realization = realization,
                EntryFrame = entry,
                OwnedExitFrame = owned != null && owned.Count > 0
                    ? owned[owned.Count - 1].EndFrame
                    : entry
            };
            if (owned != null) preview.OwnedSections.AddRange(owned);

            ResolvedTrackGenerationConfig resolved = generator != null && generator.Config != null &&
                                                       generator.Designer != null
                ? ResolvedTrackGenerationConfig.Resolve(generator.Config, generator.Designer.Clone())
                : null;
            FeaturePlanResult candidate = TrackTopologyPlanner.BuildReplacementCandidate(
                resolved, slot, realization, entry);
            if (candidate == null || candidate.Failed || candidate.Definitions == null ||
                candidate.Definitions.Count == 0)
            {
                preview.FailureReason = candidate?.FailureReason ??
                                        "This alternative did not produce preview geometry.";
                _inspectionPreview = preview;
                Repaint();
                SceneView.RepaintAll();
                return;
            }

            FrameBuildContext context = FrameBuildContext.From(resolved);
            TrackConnectionFrame frame = entry;
            for (int i = 0; i < candidate.Definitions.Count; i++)
            {
                TrackMacroSectionDefinition definition = candidate.Definitions[i];
                if (definition == null) continue;

                TrackConnectionFrame start = frame;
                TrackConnectionFrame[] frames;
                if (definition.SectionType == TrackMacroSectionType.AirGap)
                {
                    frames = Array.Empty<TrackConnectionFrame>();
                    frame = SectionFrameBuilders.AirGapLanding(frame, definition);
                }
                else
                {
                    frames = SectionFrameBuilders.BuildSectionFrames(frame, definition, context);
                    if (frames == null || frames.Length == 0)
                    {
                        preview.FailureReason =
                            $"The authoritative builder produced no frames for {definition.DebugName}.";
                        preview.Sections.Clear();
                        _inspectionPreview = preview;
                        Repaint();
                        SceneView.RepaintAll();
                        return;
                    }
                    frame = frames[frames.Length - 1];
                }

                var section = new GeneratedTrackSection
                {
                    Definition = definition,
                    SectionIndex = i,
                    StartFrame = start,
                    EndFrame = frame,
                    SubdivisionFrames = frames
                };
                section.RecalculateBounds();
                preview.Sections.Add(section);
                EncapsulatePreviewBounds(preview, section.SectionBounds);
            }

            preview.CandidateExitFrame = frame;
            EncapsulatePreviewPoint(preview, preview.OwnedExitFrame.Position,
                Mathf.Max(2f, preview.OwnedExitFrame.Width * 0.1f));
            _inspectionPreview = preview;
            if (focus) FocusInspectionPreview(generator, preview);
            Repaint();
            SceneView.RepaintAll();
        }

        private static string InspectionPreviewKey(
            TrackGenerator generator, TopologySlotRecord slot, SemanticElementId realization) =>
            $"{generator?.gameObject.scene.path}|{generator?.name}|{generator?.GenerationRevision ?? -1}|" +
            $"{slot?.TopologySlotId}|{realization}";

        private static void EncapsulatePreviewBounds(InspectionPreview preview, Bounds bounds)
        {
            if (!preview.HasBounds)
            {
                preview.LocalBounds = bounds;
                preview.HasBounds = true;
            }
            else
            {
                preview.LocalBounds.Encapsulate(bounds);
            }
        }

        private static void EncapsulatePreviewPoint(InspectionPreview preview, Vector3 point, float radius)
        {
            Bounds pointBounds = new Bounds(point, Vector3.one * Mathf.Max(0.1f, radius * 2f));
            EncapsulatePreviewBounds(preview, pointBounds);
        }

        private static void FocusInspectionPreview(TrackGenerator generator, InspectionPreview preview)
        {
            if (generator == null || preview == null || !preview.HasBounds) return;
            Transform root = generator.TrackRoot != null ? generator.TrackRoot : generator.transform;
            Vector3 center = root.TransformPoint(preview.LocalBounds.center);
            Vector3 worldExtents = root.TransformVector(preview.LocalBounds.extents);
            float size = Mathf.Max(35f, worldExtents.magnitude * 2.25f);
            SceneView.lastActiveSceneView?.LookAt(center,
                SceneView.lastActiveSceneView.rotation, size);
        }

        private void OnSceneGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            var trackEditor = (TrackEditor)target;
            TrackGenerator generator = trackEditor != null ? trackEditor.Generator : null;
            if (generator == null) return;

            Transform root = generator.TrackRoot != null ? generator.TrackRoot : generator.transform;
            Matrix4x4 toWorld = root.localToWorldMatrix;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            DrawImpactAreaOverlay(trackEditor, generator, toWorld);
            if (_inspectionPreview != null && _inspectionPreview.Valid &&
                trackEditor.SelectedAlternative == _inspectionPreview.Realization)
            {
                DrawOwnedGeometry(_inspectionPreview.OwnedSections, toWorld);
                DrawCandidateGeometry(_inspectionPreview.Sections, toWorld);
                DrawPreviewBoundary(_inspectionPreview, toWorld);
            }

            Handles.zTest = previousZTest;
        }

        private static void DrawImpactAreaOverlay(
            TrackEditor trackEditor, TrackGenerator generator, Matrix4x4 toWorld)
        {
            if (trackEditor == null || generator == null ||
                (trackEditor.ImpactBackwardFeatures <= 0 &&
                 trackEditor.ImpactForwardFeatures <= 0))
                return;
            TrackEditorImpactPlan plan = TrackEditorImpact.Analyze(
                generator.EditableTopologySlots, trackEditor.SelectedTopologySlotId,
                trackEditor.ImpactBackwardFeatures, trackEditor.ImpactForwardFeatures);
            List<GeneratedTrackSection> sections = CurrentSections(generator);
            DrawImpactDirection(plan.Backward, sections, toWorld, "AREA · BEFORE",
                new Color(1f, 0.52f, 0.18f, 0.92f));
            DrawImpactDirection(plan.Forward, sections, toWorld, "AREA · AFTER",
                new Color(1f, 0.30f, 0.34f, 0.92f));
        }

        private static void DrawImpactDirection(
            IReadOnlyList<TopologySlotRecord> slots,
            List<GeneratedTrackSection> allSections,
            Matrix4x4 toWorld,
            string label,
            Color color)
        {
            if (slots == null || slots.Count == 0) return;
            bool labelled = false;
            Color edge = new Color(color.r, color.g, color.b, 0.42f);
            for (int i = 0; i < slots.Count; i++)
            {
                List<GeneratedTrackSection> owned = FindSlotSections(allSections,
                    slots[i].TopologySlotId);
                for (int sectionIndex = 0; sectionIndex < owned.Count; sectionIndex++)
                    DrawPreviewSection(owned[sectionIndex], toWorld, color, edge, true);
                if (!labelled && owned.Count > 0)
                {
                    Vector3 point = toWorld.MultiplyPoint3x4(owned[0].StartFrame.Position);
                    Handles.color = color;
                    Handles.Label(point, $"{label} · {slots.Count} FEATURE" +
                                         (slots.Count == 1 ? "" : "S"),
                        EditorStyles.whiteBoldLabel);
                    labelled = true;
                }
            }
        }

        private static void DrawOwnedGeometry(
            List<GeneratedTrackSection> sections, Matrix4x4 toWorld)
        {
            if (sections == null) return;
            Color center = new Color(1f, 0.66f, 0.18f, 0.82f);
            Color edge = new Color(1f, 0.66f, 0.18f, 0.36f);
            for (int i = 0; i < sections.Count; i++)
                DrawPreviewSection(sections[i], toWorld, center, edge, true);
        }

        private static void DrawCandidateGeometry(
            List<GeneratedTrackSection> sections, Matrix4x4 toWorld)
        {
            Color center = new Color(0.35f, 0.96f, 1f, 1f);
            Color edge = new Color(0.78f, 0.48f, 1f, 0.95f);
            for (int i = 0; i < sections.Count; i++)
                DrawPreviewSection(sections[i], toWorld, center, edge, false);
        }

        private static void DrawPreviewSection(
            GeneratedTrackSection section,
            Matrix4x4 toWorld,
            Color centerColor,
            Color edgeColor,
            bool dotted)
        {
            if (section == null) return;
            TrackConnectionFrame[] frames = section.SubdivisionFrames;
            if (frames == null || frames.Length == 0)
            {
                Vector3 start = toWorld.MultiplyPoint3x4(section.StartFrame.Position);
                Vector3 end = toWorld.MultiplyPoint3x4(section.EndFrame.Position);
                Handles.color = edgeColor;
                Handles.DrawDottedLine(start, end, 5f);
                Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);
                Handles.Label(midpoint, "AIR GAP · no road mesh",
                    EditorStyles.whiteMiniLabel);
                return;
            }

            int stride = Mathf.Max(1, Mathf.CeilToInt(frames.Length / 260f));
            var center = new List<Vector3>();
            var left = new List<Vector3>();
            var right = new List<Vector3>();
            for (int i = 0; i < frames.Length; i += stride)
                AddPreviewFrame(frames[i], toWorld, center, left, right);
            if ((frames.Length - 1) % stride != 0)
                AddPreviewFrame(frames[frames.Length - 1], toWorld, center, left, right);

            Handles.color = centerColor;
            if (dotted)
            {
                for (int i = 1; i < center.Count; i++)
                    Handles.DrawDottedLine(center[i - 1], center[i], 4f);
            }
            else
            {
                Handles.DrawAAPolyLine(4f, center.ToArray());
            }

            Handles.color = edgeColor;
            if (dotted)
            {
                for (int i = 1; i < left.Count; i++)
                {
                    Handles.DrawDottedLine(left[i - 1], left[i], 5f);
                    Handles.DrawDottedLine(right[i - 1], right[i], 5f);
                }
            }
            else
            {
                Handles.DrawAAPolyLine(2.5f, left.ToArray());
                Handles.DrawAAPolyLine(2.5f, right.ToArray());
                int crossbarStep = Mathf.Max(1, center.Count / 26);
                for (int i = 0; i < center.Count; i += crossbarStep)
                    Handles.DrawAAPolyLine(1.2f, left[i], right[i]);
            }
        }

        private static void AddPreviewFrame(
            in TrackConnectionFrame frame,
            Matrix4x4 toWorld,
            List<Vector3> center,
            List<Vector3> left,
            List<Vector3> right)
        {
            float halfWidth = frame.Width * 0.5f;
            center.Add(toWorld.MultiplyPoint3x4(frame.Position));
            left.Add(toWorld.MultiplyPoint3x4(frame.Position - frame.Right * halfWidth));
            right.Add(toWorld.MultiplyPoint3x4(frame.Position + frame.Right * halfWidth));
        }

        private static void DrawPreviewBoundary(InspectionPreview preview, Matrix4x4 toWorld)
        {
            Vector3 entry = toWorld.MultiplyPoint3x4(preview.EntryFrame.Position);
            Vector3 candidateExit = toWorld.MultiplyPoint3x4(preview.CandidateExitFrame.Position);
            Vector3 ownedExit = toWorld.MultiplyPoint3x4(preview.OwnedExitFrame.Position);
            float marker = Mathf.Max(2f, preview.EntryFrame.Width * 0.08f);

            Handles.color = new Color(0.25f, 1f, 0.72f, 1f);
            Handles.DrawWireDisc(entry, toWorld.MultiplyVector(preview.EntryFrame.Forward).normalized, marker);
            Handles.Label(entry + Vector3.up * marker, $"PREVIEW · {preview.Realization}",
                EditorStyles.whiteBoldLabel);

            Handles.color = new Color(0.92f, 0.48f, 1f, 1f);
            Handles.DrawWireDisc(candidateExit,
                toWorld.MultiplyVector(preview.CandidateExitFrame.Forward).normalized, marker);
            Handles.Label(candidateExit + Vector3.up * marker, "CANDIDATE EXIT",
                EditorStyles.whiteMiniLabel);

            Handles.color = new Color(1f, 0.68f, 0.18f, 1f);
            Handles.DrawWireDisc(ownedExit,
                toWorld.MultiplyVector(preview.OwnedExitFrame.Forward).normalized, marker);
            Handles.Label(ownedExit + Vector3.up * marker, "REQUIRED EXIT",
                EditorStyles.whiteMiniLabel);

            if (Vector3.Distance(candidateExit, ownedExit) > 0.05f)
            {
                Handles.DrawDottedLine(candidateExit, ownedExit, 7f);
                Vector3 midpoint = Vector3.Lerp(candidateExit, ownedExit, 0.5f);
                Handles.Label(midpoint,
                    $"CONNECTOR SOLVE · {preview.BoundaryDistance:0.#} m",
                    EditorStyles.whiteMiniLabel);
            }
        }

        private void DrawRequestedChanges(TrackEditor trackEditor, IReadOnlyList<TopologySlotRecord> slots)
        {
            IReadOnlyList<TopologySlotOverride> requests = trackEditor.RequestedOverrides;
            if (requests == null || requests.Count == 0) return;

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                CardHeader("PLANNED CHANGES",
                    $"{requests.Count} design change{(requests.Count == 1 ? "" : "s")} ready to build. The live track is still unchanged.",
                    AuthoringAccent);
                _showRequests = EditorGUILayout.Foldout(_showRequests,
                    $"Show planned changes ({requests.Count})", true);
                if (!_showRequests) return;

                EditorGUILayout.LabelField(
                    "These are saved design choices. They are not written into the live track or Exact Recipe yet.",
                    EditorStyles.wordWrappedMiniLabel);

                for (int i = 0; i < requests.Count; i++)
                {
                    TopologySlotOverride request = requests[i];
                    if (request == null) continue;
                    bool exists = FindSlot(slots, request.TopologySlotId) != null;
                    using (new EditorGUILayout.HorizontalScope("HelpBox"))
                    {
                        string label = exists
                            ? $"{request.TopologySlotId}  →  {request.RequestedRealization}"
                            : $"⚠ {request.TopologySlotId}  →  {request.RequestedRealization} (stale)";
                        EditorGUILayout.LabelField(label, WrapStyle());
                        if (exists && GUILayout.Button("SELECT", EditorStyles.miniButton, GUILayout.Width(54f)))
                        {
                            Undo.RecordObject(trackEditor, "Select Requested Topology Segment");
                            trackEditor.SelectSlot(request.TopologySlotId);
                            trackEditor.SelectAlternative(request.RequestedRealization);
                            EditorUtility.SetDirty(trackEditor);
                            TopologySlotRecord slot = FindSlot(slots, request.TopologySlotId);
                            List<GeneratedTrackSection> owned = FindSlotSections(
                                CurrentSections(trackEditor.Generator), request.TopologySlotId);
                            TrackConnectionFrame entry = owned.Count > 0 ? owned[0].StartFrame : default;
                            FocusSlot(trackEditor.Generator, request.TopologySlotId, owned);
                            BuildInspectionPreview(trackEditor.Generator, slot,
                                request.RequestedRealization, entry, owned, false);
                        }
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f)))
                        {
                            Undo.RecordObject(trackEditor, "Remove Topology Replacement Request");
                            trackEditor.RemoveRequestedOverride(request.TopologySlotId);
                            EditorUtility.SetDirty(trackEditor);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (GUILayout.Button("Clear Planned Changes", GUILayout.Height(22f)))
                {
                    Undo.RecordObject(trackEditor, "Clear Topology Replacement Requests");
                    trackEditor.ClearRequestedOverrides();
                    EditorUtility.SetDirty(trackEditor);
                }
            }
        }

        private static TopologySlotRecord FindSlot(IReadOnlyList<TopologySlotRecord> slots, string slotId)
        {
            if (slots == null || string.IsNullOrWhiteSpace(slotId)) return null;
            for (int i = 0; i < slots.Count; i++)
            {
                TopologySlotRecord slot = slots[i];
                if (slot != null && string.Equals(slot.TopologySlotId, slotId, StringComparison.Ordinal))
                    return slot;
            }
            return null;
        }

        private void CheckReplacement(
            TrackEditor trackEditor,
            TrackGenerator generator,
            TopologySlotRecord slot,
            List<GeneratedTrackSection> owned,
            SemanticElementId realization,
            bool regenerateCurrent,
            TrackEditorImpactPlan impactPlan,
            bool allowFeatureOverrides)
        {
            _replacementCheckKey = ReplacementCheckKey(
                generator, slot, realization);
            Undo.RecordObject(trackEditor, "Check Topology Replacement");
            string requestReason;
            bool requestAccepted;
            if (regenerateCurrent)
                requestAccepted = trackEditor.TryRequestRegeneration(slot, impactPlan,
                    allowFeatureOverrides, out requestReason);
            else
                requestAccepted = trackEditor.TryRequestReplacement(slot, realization, impactPlan,
                    allowFeatureOverrides, out requestReason);
            if (!requestAccepted)
            {
                _replacementCheckCandidate = null;
                _replacementCheckGeometry = new TopologyReplacementGeometryResult
                {
                    Status = TopologyReplacementGeometryStatus.BlockedPreflight,
                    Reason = requestReason
                };
                Repaint();
                return;
            }

            TopologySlotOverride request = trackEditor.FindRequestedOverride(slot.TopologySlotId);
            TopologyReplacementPreflightResult preflight =
                TopologyReplacementPreflight.Evaluate(slot, request, owned);
            if (!preflight.Passed)
            {
                trackEditor.RemoveRequestedOverride(slot.TopologySlotId);
                _replacementCheckCandidate = null;
                _replacementCheckGeometry = new TopologyReplacementGeometryResult
                {
                    Status = TopologyReplacementGeometryStatus.BlockedPreflight,
                    Reason = FriendlyPreflightFailure(preflight.Reason)
                };
                EditorUtility.SetDirty(trackEditor);
                Repaint();
                return;
            }

            RunReplacementCheck(generator, slot, request, preflight);
            if (_replacementCheckGeometry == null || !_replacementCheckGeometry.GeometryBuilt)
                trackEditor.RemoveRequestedOverride(slot.TopologySlotId);
            EditorUtility.SetDirty(trackEditor);
        }

        private void BuildAndApplySelected(
            TrackEditor trackEditor,
            TrackGenerator generator,
            TopologySlotRecord slot,
            List<GeneratedTrackSection> owned)
        {
            BuildAndApplyRealization(trackEditor, generator, slot, owned,
                trackEditor.SelectedAlternative, false);
        }

        private void BuildAndApplyCurrent(
            TrackEditor trackEditor,
            TrackGenerator generator,
            TopologySlotRecord slot,
            List<GeneratedTrackSection> owned)
        {
            BuildAndApplyRealization(trackEditor, generator, slot, owned,
                slot.CurrentRealization, true);
        }

        private void BuildAndApplyRealization(
            TrackEditor trackEditor,
            TrackGenerator generator,
            TopologySlotRecord slot,
            List<GeneratedTrackSection> owned,
            SemanticElementId realization,
            bool regenerateCurrent)
        {
            SetApplyOutcome(regenerateCurrent, "", false);
            _clipboardStatus = "";
            if (regenerateCurrent) _regenerationFailureClipboardReport = "";
            else _applyFailureClipboardReport = "";

            // The Track Editor intentionally applies one authored change at a time.
            // A previous failed or abandoned request must never hitch a ride on the
            // next replacement the designer chooses to build.
            if (trackEditor.RequestedOverrides != null && trackEditor.RequestedOverrides.Count > 0)
            {
                Undo.RecordObject(trackEditor, "Replace Planned Track Change");
                trackEditor.ClearRequestedOverrides();
            }

            TrackEditorImpactPlan impactPlan = TrackEditorImpact.Analyze(
                generator.EditableTopologySlots, slot.TopologySlotId,
                trackEditor.ImpactBackwardFeatures, trackEditor.ImpactForwardFeatures);
            bool allowFeatureOverrides = impactPlan.FeatureOverrideCount == 0;
            if (impactPlan.FeatureOverrideCount > 0)
            {
                int decision = EditorUtility.DisplayDialogComplex(
                    "Override nearby features?",
                    BuildImpactConfirmationMessage(slot, realization, impactPlan),
                    "OVERRIDE FEATURES ANYWAY",
                    "CANCEL",
                    "EDIT FIRST AFFECTED");
                if (decision != 0)
                {
                    if (decision == 2)
                    {
                        TopologySlotRecord firstAffected = impactPlan.Backward.Count > 0
                            ? impactPlan.Backward[0]
                            : impactPlan.Forward.Count > 0
                                ? impactPlan.Forward[0]
                                : null;
                        if (firstAffected != null)
                        {
                            Undo.RecordObject(trackEditor, "Edit Affected Track Feature");
                            trackEditor.SetImpactReach(0, 0);
                            EditorUtility.SetDirty(trackEditor);
                            SelectSlotFromPicker(trackEditor, generator, firstAffected);
                        }
                    }
                    SetApplyOutcome(regenerateCurrent,
                        "Build cancelled. Your live track was not changed.", false);
                    Repaint();
                    return;
                }
                allowFeatureOverrides = true;
            }

            bool restoringOriginal = !regenerateCurrent && IsOriginalRecipeRestore(
                slot, realization, impactPlan);
            if (restoringOriginal)
            {
                Undo.RecordObject(trackEditor, "Restore Original Track Feature");
                if (!trackEditor.TryRequestReplacement(slot, realization, impactPlan,
                        allowFeatureOverrides, out string requestFailure))
                {
                    SetApplyOutcome(false, requestFailure, false);
                    SetFailureClipboardReport(false,
                        BuildFailureClipboardReport(generator, slot, realization,
                            null, requestFailure, impactPlan));
                    Repaint();
                    return;
                }
                EditorUtility.SetDirty(trackEditor);
            }
            else
            {
                CheckReplacement(trackEditor, generator, slot, owned, realization,
                    regenerateCurrent, impactPlan, allowFeatureOverrides);
            }
            if (!restoringOriginal &&
                (_replacementCheckGeometry == null || !_replacementCheckGeometry.GeometryBuilt))
            {
                string message = FriendlyGeometryFailure(_replacementCheckGeometry?.Reason);
                SetApplyOutcome(regenerateCurrent, message, false);
                SetFailureClipboardReport(regenerateCurrent,
                    BuildFailureClipboardReport(generator, slot, realization,
                        _replacementCheckGeometry, message, impactPlan));
                Repaint();
                return;
            }

            // Save the exact accepted design only after every non-mutating gate has
            // passed. It becomes a one-step Track Editor history entry if the edited
            // route is accepted; a rejected attempt never replaces existing history.
            string undoRecipe = generator.ExportGenerationRecipe(preferLastAccepted: true);
            string undoLabel = regenerateCurrent
                ? $"Regenerated {slot.CurrentRealization}"
                : $"{slot.CurrentRealization} → {realization}";

            // ReadyForConnectorSolve is intentionally allowed here. The designer-first
            // rebuild may adapt legal ordinary/recovery road beyond the exact old altitude
            // layers, while the final whole-track safety validators remain authoritative.
            if (!generator.ApplyTopologyOverrides(trackEditor.RequestedOverrides, out string error))
            {
                string message = string.IsNullOrWhiteSpace(error)
                    ? "The rebuilt route did not pass validation. Your previous track was kept."
                    : $"The rebuilt route was not accepted: {error} Your previous track was kept.";

                // ApplyTopologyOverrides is transactional, so the live route is already
                // safe here. Drop the rejected request as well: otherwise the Track Editor
                // can reopen in a stale, unavailable state after a failed regeneration.
                Undo.RecordObject(trackEditor, "Discard Rejected Track Change");
                trackEditor.ClearRequestedOverrides();
                EditorUtility.SetDirty(trackEditor);

                SetApplyOutcome(regenerateCurrent, message, false);
                SetFailureClipboardReport(regenerateCurrent,
                    BuildFailureClipboardReport(generator, slot, realization,
                        _replacementCheckGeometry, message, impactPlan));
                Repaint();
                SceneView.RepaintAll();
                return;
            }

            Undo.RecordObject(trackEditor, "Apply Track Design Changes");
            trackEditor.RememberLastAppliedEdit(undoRecipe, undoLabel);
            _undoMessage = "";
            _undoSucceeded = false;
            trackEditor.ClearRequestedOverrides();
            trackEditor.SelectAlternative(SemanticElementId.None);
            EditorUtility.SetDirty(trackEditor);

            _inspectionPreview = null;
            _replacementCheckKey = "";
            _replacementCheckCandidate = null;
            _replacementCheckGeometry = null;
            string successMessage = regenerateCurrent
                ? "Current segment regenerated. The feature, connectors, and surrounding route were rebuilt and validated."
                : restoringOriginal
                    ? "Original generated feature restored. Its original parameters, recovery, closure, route and mesh were rebuilt from the deterministic recipe."
                    : "Replacement applied. The feature, connectors, and surrounding route were rebuilt and validated.";
            SetApplyOutcome(regenerateCurrent, successMessage, true);
            SceneView.RepaintAll();
            Repaint();
            GUIUtility.ExitGUI();
        }

        private static string BuildImpactConfirmationMessage(
            TopologySlotRecord selected,
            SemanticElementId realization,
            TrackEditorImpactPlan impactPlan)
        {
            var text = new StringBuilder(512);
            text.Append(selected.CurrentRealization).Append(" → ").Append(realization)
                .AppendLine().AppendLine();
            text.AppendLine("The rebuild may remove these nearby features:");
            for (int i = 0; i < impactPlan.Backward.Count; i++)
                text.Append("• BEFORE: ").AppendLine(
                    impactPlan.Backward[i].CurrentRealization.ToString());
            for (int i = 0; i < impactPlan.Forward.Count; i++)
                text.Append("• AFTER: ").AppendLine(
                    impactPlan.Forward[i].CurrentRealization.ToString());
            text.AppendLine();
            text.AppendLine("The complete route and mesh will be rebuilt and validated. " +
                            "If it fails, your current track stays untouched.");
            return text.ToString();
        }

        private static bool IsOriginalRecipeRestore(
            TopologySlotRecord slot,
            SemanticElementId realization,
            TrackEditorImpactPlan impactPlan)
        {
            return slot != null &&
                   slot.OverrideState == TopologySlotOverrideState.Applied &&
                   slot.OriginalRealization != SemanticElementId.None &&
                   slot.CurrentRealization != slot.OriginalRealization &&
                   realization == slot.OriginalRealization &&
                   (impactPlan == null || impactPlan.FeatureOverrideCount == 0);
        }

        private void SetApplyOutcome(bool regeneration, string message, bool succeeded)
        {
            if (regeneration)
            {
                _regenerationMessage = message ?? "";
                _regenerationSucceeded = succeeded;
            }
            else
            {
                _applyMessage = message ?? "";
                _applySucceeded = succeeded;
            }
        }

        private void ClearInspectionState()
        {
            _inspectionPreview = null;
            _replacementCheckKey = "";
            _replacementCheckCandidate = null;
            _replacementCheckGeometry = null;
            _applyMessage = "";
            _applySucceeded = false;
            _applyFailureClipboardReport = "";
            _regenerationMessage = "";
            _regenerationSucceeded = false;
            _regenerationFailureClipboardReport = "";
            _clipboardStatus = "";
        }

        private void SetFailureClipboardReport(bool regeneration, string report)
        {
            if (regeneration) _regenerationFailureClipboardReport = report ?? "";
            else _applyFailureClipboardReport = report ?? "";
        }

        private void DrawCopyFailureReportButton(string report, string fallbackMessage)
        {
            string text = string.IsNullOrWhiteSpace(report) ? fallbackMessage : report;
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(text)))
            {
                if (AccentButton("COPY ERROR REPORT",
                        "Copy a compact Track Editor report that can be pasted directly into a bug report or Codex chat.",
                        CheckActionTint, 23f, false))
                {
                    EditorGUIUtility.systemCopyBuffer = text;
                    _clipboardStatus = "Copied compact error report to the clipboard.";
                    GUI.FocusControl(null);
                }
            }

            if (!string.IsNullOrWhiteSpace(_clipboardStatus))
                EditorGUILayout.LabelField(_clipboardStatus, EditorStyles.miniLabel);
        }

        private static string BuildFailureClipboardReport(
            TrackGenerator generator,
            TopologySlotRecord slot,
            SemanticElementId requestedRealization,
            TopologyReplacementGeometryResult geometry,
            string message,
            TrackEditorImpactPlan impactPlan = null)
        {
            var sb = new StringBuilder(1536);
            sb.AppendLine("TRACK EDITOR BUILD & APPLY ERROR");
            if (slot != null)
            {
                sb.AppendLine($"Requested edit: {slot.CurrentRealization} -> {requestedRealization}");
                sb.AppendLine($"Slot: {slot.TopologySlotId}");
                sb.AppendLine($"Route: Q{slot.QuarterIndex + 1} / Road {(slot.RoadId == 0 ? "A" : "B")} / route slot {slot.RouteOrder}");
                sb.AppendLine($"Demand: {slot.DemandType}, heading {slot.SignedHeadingDelta:+0.#;-0.#;0} deg");
                sb.AppendLine($"Allowed rebuild scope: {slot.LocalReplanPolicy}");
            }
            else
            {
                sb.AppendLine($"Requested realization: {requestedRealization}");
            }

            if (impactPlan != null)
            {
                sb.AppendLine($"Area of Impact: {impactPlan.Backward.Count} before, " +
                              $"{impactPlan.Forward.Count} after");
                foreach (TopologySlotRecord affected in impactPlan.EnumerateAffected())
                    sb.AppendLine($"- May override: {affected.CurrentRealization} ({affected.TopologySlotId})");
            }

            if (geometry != null)
            {
                sb.AppendLine($"Preview: {geometry.Status}, candidate length {geometry.CandidateLength:0.##} m");
                sb.AppendLine($"Preview boundary error: position {geometry.PositionError:0.##} m, " +
                              $"forward {geometry.ForwardAngleError:0.##} deg, up {geometry.UpAngleError:0.##} deg, " +
                              $"width {geometry.WidthError:0.##} m");
            }

            TrackGenerationReport report = generator?.LastReport;
            if (report != null)
            {
                sb.AppendLine($"Generation: seed {report.Seed}, command {report.RegenerationCommand}");
                sb.AppendLine($"Attempts: {report.AttemptsEvaluated}, valid candidates: {report.ValidCandidateCount}, " +
                              $"time: {report.GenerationDurationSeconds:0.00}s, passes: {Mathf.Max(1, report.PipelinePassCount)}");

                List<(GenerationFailureReason reason, int count)> counts = report.FailureCountsByReason();
                if (counts.Count > 0)
                {
                    sb.AppendLine("Exact failure counts:");
                    for (int i = 0; i < counts.Count; i++)
                        sb.AppendLine($"- {counts[i].reason}: {counts[i].count}");

                    sb.AppendLine("Representative failure details:");
                    for (int i = 0; i < counts.Count; i++)
                    {
                        GenerationAttemptFailure failure = report.RepresentativeFailure(counts[i].reason);
                        if (failure == null || string.IsNullOrWhiteSpace(failure.Message)) continue;
                        sb.AppendLine($"- {counts[i].reason}: {failure.Message}");
                    }
                }
            }

            sb.AppendLine("Editor message:");
            sb.AppendLine(string.IsNullOrWhiteSpace(message) ? "No additional message was recorded." : message.Trim());
            return sb.ToString().TrimEnd();
        }

        private static string FriendlyPreflightFailure(string reason) =>
            string.IsNullOrWhiteSpace(reason)
                ? "This section no longer has enough reliable boundary data to check a replacement. Regenerate or repair the track preview first."
                : reason.Replace("preflight", "initial fit check", StringComparison.OrdinalIgnoreCase)
                    .Replace("dry-run", "route build", StringComparison.OrdinalIgnoreCase);

        private static string FriendlyGeometryFailure(string reason) =>
            string.IsNullOrWhiteSpace(reason)
                ? "The temporary replacement could not be built safely in this section."
                : reason.Replace("candidate", "replacement", StringComparison.OrdinalIgnoreCase)
                    .Replace("dry-run", "route build", StringComparison.OrdinalIgnoreCase);

        private void RunReplacementCheck(
            TrackGenerator generator,
            TopologySlotRecord slot,
            TopologySlotOverride request,
            TopologyReplacementPreflightResult preflight)
        {
            _replacementCheckKey = ReplacementCheckKey(
                generator, slot, request.RequestedRealization);
            ResolvedTrackGenerationConfig resolved = generator.Config != null && generator.Designer != null
                ? ResolvedTrackGenerationConfig.Resolve(generator.Config, generator.Designer.Clone())
                : null;
            _replacementCheckCandidate = TrackTopologyPlanner.BuildReplacementCandidate(
                resolved, slot, request.RequestedRealization, preflight.EntryFrame);
            _replacementCheckGeometry = TopologyReplacementGeometryProbe.Evaluate(
                preflight, request, _replacementCheckCandidate);
            Repaint();
        }

        private static string ReplacementCheckKey(
            TrackGenerator generator,
            TopologySlotRecord slot,
            SemanticElementId realization) =>
            $"{generator?.gameObject.scene.path}|{generator?.name}|{generator?.GenerationRevision ?? -1}|" +
            $"{slot?.TopologySlotId}|{realization}|{slot?.LocalReplanPolicy}";

        private void DrawReplacementCheckResult(
            FeaturePlanResult candidate,
            TopologyReplacementGeometryResult geometry)
        {
            if (geometry == null) return;

            string title = geometry.Status switch
            {
                TopologyReplacementGeometryStatus.ReadyForClearanceValidation => "READY TO BUILD",
                TopologyReplacementGeometryStatus.ReadyForConnectorSolve => "ROUTE ADAPTATION AVAILABLE",
                _ => "THIS DESIGN CANNOT FIT HERE"
            };
            string explanation = geometry.Status switch
            {
                TopologyReplacementGeometryStatus.ReadyForClearanceValidation =>
                    "The replacement reaches the surrounding road correctly. Build & Apply will run the complete clearance and driving checks.",
                TopologyReplacementGeometryStatus.ReadyForConnectorSolve =>
                    $"The feature changes the old exit by {geometry.PositionError:0.#} m. " +
                    "Build & Apply may reshape legal connector, recovery, and ordinary road to keep the design, then validates the complete track.",
                _ => FriendlyGeometryFailure(geometry.Reason)
            };
            Color statusTint = geometry.Status switch
            {
                TopologyReplacementGeometryStatus.ReadyForClearanceValidation => SuccessTint,
                TopologyReplacementGeometryStatus.ReadyForConnectorSolve => CheckActionTint,
                _ => new Color(0.72f, 0.30f, 0.30f, 1f)
            };

            Rect statusLine = EditorGUILayout.GetControlRect(false, 3f);
            EditorGUI.DrawRect(statusLine, statusTint);
            EditorGUILayout.LabelField(title, CardTitleStyle());
            MessageType messageType = geometry.Status switch
            {
                TopologyReplacementGeometryStatus.ReadyForClearanceValidation => MessageType.Info,
                TopologyReplacementGeometryStatus.ReadyForConnectorSolve => MessageType.Info,
                _ => MessageType.Error
            };
            EditorGUILayout.HelpBox(explanation, messageType);

            _showTechnicalResult = EditorGUILayout.Foldout(_showTechnicalResult,
                "Technical measurements", true);
            if (_showTechnicalResult && candidate != null && !candidate.Failed)
            {
                EditorGUILayout.LabelField(
                    $"Built {candidate.Definitions?.Count ?? 0} section(s) · {candidate.ArcLength:0.#} m · " +
                    $"heading {candidate.HeadingContributionDeg:+0.#;-0.#;0}°",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"Boundary error: {geometry.PositionError:0.##} m · forward {geometry.ForwardAngleError:0.##}° · " +
                    $"up {geometry.UpAngleError:0.##}°",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(geometry.Reason, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.LabelField(
                "PREVIEW ONLY · LIVE TRACK UNCHANGED UNTIL BUILD & APPLY",
                EditorStyles.miniBoldLabel);
        }

        private void DrawBlocked(TopologySlotCompatibilityResult result)
        {
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                EditorGUILayout.LabelField($"× {result.Candidate}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(result.Reason, WrapStyle());
            }
        }

        public static void SelectOrCreateFor(TrackGenerator generator)
        {
            TrackEditor[] editors = UnityEngine.Object.FindObjectsByType<TrackEditor>(FindObjectsInactive.Include);
            for (int i = 0; i < editors.Length; i++)
            {
                if (editors[i] != null && editors[i].Generator == generator)
                {
                    ParentUnderGenerator(editors[i].transform, generator);
                    Selection.activeGameObject = editors[i].gameObject;
                    EditorGUIUtility.PingObject(editors[i].gameObject);
                    return;
                }
            }

            var go = new GameObject("TrackEditor");
            Undo.RegisterCreatedObjectUndo(go, "Create Track Editor");
            ParentUnderGenerator(go.transform, generator);
            var editor = go.AddComponent<TrackEditor>();
            editor.Bind(generator);
            EditorUtility.SetDirty(editor);
            Selection.activeGameObject = go;
        }

        private static void ParentUnderGenerator(Transform editorTransform, TrackGenerator generator)
        {
            if (editorTransform == null || generator == null ||
                editorTransform.parent == generator.transform) return;

            Undo.SetTransformParent(editorTransform, generator.transform, "Parent Track Editor");
            editorTransform.localPosition = Vector3.zero;
            editorTransform.localRotation = Quaternion.identity;
            editorTransform.localScale = Vector3.one;
            EditorUtility.SetDirty(editorTransform);
        }

        [MenuItem("GameObject/Track Generation/Track Editor", false, 11)]
        private static void CreateFromMenu(MenuCommand command)
        {
            TrackGenerator generator = UnityEngine.Object.FindAnyObjectByType<TrackGenerator>(
                FindObjectsInactive.Include);
            SelectOrCreateFor(generator);
        }

        private static List<GeneratedTrackSection> CurrentSections(TrackGenerator generator)
        {
            if (generator.CurrentMacroSections != null && generator.CurrentMacroSections.Count > 0)
                return generator.CurrentMacroSections;
            MacroTrackDebugVisualizer visualizer = generator.TrackRoot != null
                ? generator.TrackRoot.GetComponent<MacroTrackDebugVisualizer>()
                : null;
            return visualizer?.Sections ?? new List<GeneratedTrackSection>();
        }

        private static List<GeneratedTrackSection> FindSlotSections(
            List<GeneratedTrackSection> sections, string topologySlotId)
        {
            var matches = new List<GeneratedTrackSection>();
            if (sections == null) return matches;
            for (int i = 0; i < sections.Count; i++)
            {
                GeneratedTrackSection section = sections[i];
                if (section != null && string.Equals(section.TopologySlotId, topologySlotId,
                        StringComparison.Ordinal))
                    matches.Add(section);
            }
            matches.Sort((a, b) => a.SectionIndex.CompareTo(b.SectionIndex));
            return matches;
        }

        private static void FocusSlot(TrackGenerator generator, string topologySlotId,
            List<GeneratedTrackSection> owned)
        {
            MacroTrackDebugVisualizer visualizer = generator.TrackRoot != null
                ? generator.TrackRoot.GetComponent<MacroTrackDebugVisualizer>()
                : null;
            if (visualizer == null || owned == null || owned.Count == 0)
            {
                Debug.Log("[TrackEditor] This topology segment has no live preview geometry to focus.");
                return;
            }

            visualizer.HighlightedTopologySlotId = topologySlotId;
            Bounds local = owned[0].SectionBounds;
            for (int i = 1; i < owned.Count; i++) local.Encapsulate(owned[i].SectionBounds);
            Vector3 worldCenter = visualizer.transform.TransformPoint(local.center);
            float viewSize = Mathf.Max(30f, local.extents.magnitude * 2.2f);
            SceneView.lastActiveSceneView?.LookAt(worldCenter,
                SceneView.lastActiveSceneView.rotation, viewSize);
            SceneView.RepaintAll();
        }

        private static bool SlotMatchesFilter(TopologySlotRecord slot, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            string needle = filter.Trim();
            return slot.TopologySlotId.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   slot.CurrentRealization.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   slot.DemandType.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   $"Q{slot.QuarterIndex + 1}".IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   $"Road {(slot.RoadId == 0 ? "A" : "B")}".IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   slot.SignedHeadingDelta.ToString("+0.#;-0.#;0")
                       .IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SlotLabel(TopologySlotRecord slot) =>
            $"Q{slot.QuarterIndex + 1} · {(slot.RoadId == 0 ? "A" : "B")} · #{slot.RouteOrder} · " +
            $"{slot.CurrentRealization} · {slot.SignedHeadingDelta:+0.#;-0.#;0}°";

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

        private static void DrawOriginalChoiceIcon(
            SemanticElementId realization, bool restoresRecipe)
        {
            GUIStyle style = _originalChoiceIconStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fixedWidth = 22f,
                fixedHeight = 19f,
                margin = new RectOffset(1, 2, 0, 0)
            };
            Color previous = GUI.contentColor;
            GUI.contentColor = AuthoringAccent;
            GUILayout.Label(new GUIContent("↶", restoresRecipe
                    ? $"{realization} is the original generated choice. Selecting it restores the exact original recipe."
                    : $"{realization} is the original generated choice for this section."),
                style, GUILayout.Width(22f), GUILayout.Height(19f));
            GUI.contentColor = previous;
        }

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

        private GUIStyle WrapStyle() => _wrapStyle ??= new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            fontSize = 10
        };
    }
}
