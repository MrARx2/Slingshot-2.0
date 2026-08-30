#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TrackGeneration.Core;
using TrackGeneration.Design;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Editor
{
    /// <summary>
    /// Runs a small, deterministic, planning-only seed audit. It deliberately does not
    /// build meshes or mutate the scene, and it stops scheduling new seeds when its
    /// wall-clock budget has been reached.
    /// </summary>
    public sealed class V2StabilitySweepWindow : EditorWindow
    {
        private const int DefaultSeedCount = 6;
        private const float DefaultBudgetSeconds = 120f;

        [SerializeField] private TrackGenerator generator;
        [SerializeField] private int startingSeed = 20260824;
        [SerializeField] private int seedCount = DefaultSeedCount;
        [SerializeField] private float totalBudgetSeconds = DefaultBudgetSeconds;
        [SerializeField] private bool stopOnFirstFailure;
        [SerializeField] private bool featureAdmissionSweep;
        [SerializeField] private TrackPatternType featureUnderTest = TrackPatternType.Camelback;
        [SerializeField] private string latestReportPath = "";

        private string resultsText = "No audit has been run yet.";
        private Vector2 resultsScroll;

        [MenuItem("Track/V2/Bounded Stability Sweep")]
        public static void Open()
        {
            var window = GetWindow<V2StabilitySweepWindow>("V2 Stability");
            window.minSize = new Vector2(520f, 440f);
            window.TryAdoptSelectedGenerator();
            window.Show();
        }

        public static void OpenFor(TrackGenerator source)
        {
            Open();
            var window = GetWindow<V2StabilitySweepWindow>();
            window.generator = source;
            window.Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(520f, 440f);
            if (generator == null) TryAdoptSelectedGenerator();
        }

        private void TryAdoptSelectedGenerator()
        {
            if (Selection.activeGameObject != null)
                generator = Selection.activeGameObject.GetComponentInParent<TrackGenerator>();
            if (generator == null)
                generator = UnityEngine.Object.FindAnyObjectByType<TrackGenerator>(FindObjectsInactive.Include);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            DrawHeader();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                generator = (TrackGenerator)EditorGUILayout.ObjectField(
                    new GUIContent("Track Generator", "Settings source used for every planning run."),
                    generator, typeof(TrackGenerator), true);

                EditorGUILayout.Space(3f);
                startingSeed = EditorGUILayout.IntField(
                    new GUIContent("Starting Seed", "The first seed. Later seeds follow a fixed deterministic sequence."),
                    startingSeed);
                seedCount = EditorGUILayout.IntSlider(
                    new GUIContent("Seeds", "Maximum number of seeds to inspect."),
                    seedCount, 1, 20);
                totalBudgetSeconds = EditorGUILayout.Slider(
                    new GUIContent("Total Time Limit", "No new seed starts after this many seconds. A seed already running is allowed to finish."),
                    totalBudgetSeconds, 15f, 300f);
                stopOnFirstFailure = EditorGUILayout.Toggle(
                    new GUIContent("Stop On First Failure", "Useful when you only need the first reproducible failing seed."),
                    stopOnFirstFailure);

                featureAdmissionSweep = EditorGUILayout.Toggle(
                    new GUIContent("Require One Feature",
                        "Adds one explicit Required Pattern to a cloned settings profile for every audited seed."),
                    featureAdmissionSweep);
                if (featureAdmissionSweep)
                    featureUnderTest = (TrackPatternType)EditorGUILayout.EnumPopup(
                        new GUIContent("Feature Under Test",
                            "The definition/pattern that every successful audit layout must contain."),
                        featureUnderTest);

                EditorGUILayout.Space(5f);
                EditorGUILayout.HelpBox(
                    "Planning only: this checks strict V2 generation yield without creating meshes, replacing the current track, or changing the scene. The time limit is checked between seeds; one active seed may finish slightly beyond it.",
                    MessageType.Info);

                EditorGUI.BeginDisabledGroup(generator == null || generator.Config == null || generator.Designer == null);
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.18f, 0.60f, 0.64f);
                if (TrackUiActionFeedback.Button(this, "stability.run", "RUN BOUNDED AUDIT",
                        "Run the configured bounded TrackGenerator stability audit.", "AUDITING…",
                        GUILayout.Height(34f)))
                    RunSweep();
                GUI.backgroundColor = oldColor;
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(7f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(latestReportPath) || !File.Exists(latestReportPath));
                if (TrackUiActionFeedback.Button(this, "stability.show-report", "Show Latest Report",
                        "Reveal the latest stability report.", "OPENING…", GUILayout.Height(22f)))
                    EditorUtility.RevealInFinder(latestReportPath);
                EditorGUI.EndDisabledGroup();

                if (TrackUiActionFeedback.Button(this, "stability.copy", "Copy Results",
                        "Copy the stability results to the clipboard.", "COPYING…", GUILayout.Height(22f)))
                    EditorGUIUtility.systemCopyBuffer = resultsText ?? "";
            }

            EditorGUILayout.Space(4f);
            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll);
            EditorGUILayout.TextArea(resultsText ?? "", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            Color old = GUI.color;
            GUI.color = new Color(0.62f, 0.95f, 0.98f);
            EditorGUILayout.LabelField("TRACK GENERATOR V2 · BOUNDED STABILITY AUDIT", EditorStyles.boldLabel);
            GUI.color = old;
            EditorGUILayout.LabelField(
                "Measure strict planning reliability across reproducible seeds—without another runaway test.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(5f);
        }

        private void RunSweep()
        {
            if (generator == null || generator.Config == null || generator.Designer == null)
            {
                EditorUtility.DisplayDialog("Track Generator Required",
                    "Assign a TrackGenerator with both a rulebook and design settings.", "OK");
                return;
            }

            TrackDesignerSettings auditSettings = generator.Designer.Clone();
            if (featureAdmissionSweep)
            {
                auditSettings.Features.RequiredPatterns.RemoveAll(
                    entry => entry != null && entry.Pattern == featureUnderTest);
                auditSettings.Features.RequiredPatterns.Add(new RequiredPatternEntry
                {
                    Pattern = featureUnderTest,
                    Count = 1
                });
            }
            var resolved = ResolvedTrackGenerationConfig.Resolve(generator.Config, auditSettings);
            if (resolved.HasHardErrors)
            {
                var invalid = new StringBuilder("The current setup has hard configuration errors:\n");
                foreach (var issue in resolved.Issues)
                    if (issue.Severity == ResolvedIssueSeverity.Error)
                        invalid.AppendLine($"• {issue.Field}: {issue.Message}");
                resultsText = invalid.ToString();
                return;
            }

            int requested = Mathf.Clamp(seedCount, 1, 20);
            double budget = Mathf.Clamp(totalBudgetSeconds, 15f, 300f);
            int seed = startingSeed;
            int completed = 0;
            int passed = 0;
            int errors = 0;
            int strictPasses = 0;
            int nonStrictPasses = 0;
            bool cancelled = false;
            bool budgetReached = false;
            double measuredSeedSeconds = 0d;
            double slowestSeedSeconds = 0d;
            int slowestSeed = 0;
            var totalTimer = Stopwatch.StartNew();
            var rows = new StringBuilder(2048);
            var aggregateFailures = new Dictionary<GenerationFailureReason, int>();
            var failingSeeds = new List<int>();

            rows.AppendLine("TRACK GENERATOR V2 · BOUNDED STABILITY AUDIT");
            rows.AppendLine($"Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            rows.AppendLine($"Requested {requested} seed(s), {budget:F0}s scheduling budget, strict planning pass only");
            if (featureAdmissionSweep)
                rows.AppendLine($"Feature admission: every accepted layout must contain {featureUnderTest}");
            rows.AppendLine();
            rows.AppendLine("#   Seed          Result   Time      Pass      Attempts  Candidates  Primary reason");
            rows.AppendLine("──  ────────────  ───────  ────────  ────────  ────────  ──────────  ──────────────");

            try
            {
                for (int i = 0; i < requested; i++)
                {
                    if (i > 0 && totalTimer.Elapsed.TotalSeconds >= budget)
                    {
                        budgetReached = true;
                        break;
                    }

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Track Generator V2 Stability Audit",
                            $"Planning seed {seed} ({i + 1}/{requested})",
                            i / (float)requested))
                    {
                        cancelled = true;
                        break;
                    }

                    TrackGenerationResult result;
                    var seedTimer = Stopwatch.StartNew();
                    try
                    {
                        result = new TrackGenerationPipeline().Run(resolved, TrackSeed.CreateNew(seed));
                        seedTimer.Stop();
                    }
                    catch (Exception exception)
                    {
                        seedTimer.Stop();
                        errors++;
                        double errorSeconds = seedTimer.Elapsed.TotalSeconds;
                        measuredSeedSeconds += errorSeconds;
                        if (errorSeconds > slowestSeedSeconds)
                        {
                            slowestSeedSeconds = errorSeconds;
                            slowestSeed = seed;
                        }
                        failingSeeds.Add(seed);
                        rows.AppendLine($"{i + 1,-3} {seed,-13} ERROR    {errorSeconds,7:F2}s  —         —         —           {exception.GetType().Name}: {exception.Message}");
                        completed++;
                        if (stopOnFirstFailure) break;
                        seed = NextSeed(seed);
                        continue;
                    }

                    completed++;
                    // Use the audit's own wall clock for decisions. The report duration is
                    // diagnostic data owned by the pipeline and may be absent in older recipes.
                    double resultSeconds = seedTimer.Elapsed.TotalSeconds;
                    measuredSeedSeconds += resultSeconds;
                    if (resultSeconds > slowestSeedSeconds)
                    {
                        slowestSeedSeconds = resultSeconds;
                        slowestSeed = seed;
                    }

                    bool admissionSatisfied = !featureAdmissionSweep ||
                        (result.Layout?.Sections != null && result.Layout.Sections.Exists(section =>
                            section?.Definition != null &&
                            section.Definition.SemanticElement == FeaturePlanning.ElementOf(featureUnderTest)));
                    bool auditPassed = result.Success && admissionSatisfied;
                    if (auditPassed)
                    {
                        passed++;
                        if (string.Equals(result.Report.AcceptedPass, "Strict", StringComparison.OrdinalIgnoreCase))
                            strictPasses++;
                        else
                            nonStrictPasses++;
                    }
                    else
                    {
                        failingSeeds.Add(seed);
                    }

                    string primaryReason = "—";
                    var reasons = result.Report.FailureCountsByReason();
                    if (reasons.Count > 0)
                    {
                        primaryReason = reasons[0].reason.ToString();
                        foreach (var reason in reasons)
                        {
                            aggregateFailures.TryGetValue(reason.reason, out int count);
                            aggregateFailures[reason.reason] = count + reason.count;
                        }
                    }
                    if (result.Success && !admissionSatisfied)
                    {
                        primaryReason = $"AdmissionMissing:{featureUnderTest}";
                        aggregateFailures.TryGetValue(
                            GenerationFailureReason.RequiredFeatureMissing, out int missingCount);
                        aggregateFailures[GenerationFailureReason.RequiredFeatureMissing] = missingCount + 1;
                    }

                    rows.AppendLine(
                        $"{i + 1,-3} {seed,-13} {(auditPassed ? "PASS" : "FAIL"),-7} " +
                        $"{resultSeconds,7:F2}s  {ShortPass(result.Report.AcceptedPass),-8}  {result.Report.AttemptsEvaluated,8}  " +
                        $"{result.Report.ValidCandidateCount,10}  {primaryReason}");

                    if (!auditPassed && stopOnFirstFailure) break;
                    seed = NextSeed(seed);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                totalTimer.Stop();
            }

            rows.AppendLine();
            rows.AppendLine("SUMMARY");
            rows.AppendLine($"Completed: {completed}/{requested}");
            rows.AppendLine($"Passed: {passed}/{completed} ({(completed > 0 ? 100f * passed / completed : 0f):F1}%)");
            rows.AppendLine($"Failed: {Mathf.Max(0, completed - passed - errors)} | Errors: {errors}");
            rows.AppendLine($"Accepted passes: strict {strictPasses} | non-strict {nonStrictPasses}");
            rows.AppendLine($"Average seed time: {(completed > 0 ? measuredSeedSeconds / completed : 0d):F2}s");
            if (completed > 0)
                rows.AppendLine($"Slowest seed: {slowestSeed} ({slowestSeedSeconds:F2}s)");
            rows.AppendLine($"Elapsed: {totalTimer.Elapsed.TotalSeconds:F2}s");
            if (cancelled) rows.AppendLine("Stopped: cancelled by user between seeds.");
            else if (budgetReached) rows.AppendLine("Stopped: total scheduling budget reached.");
            else if (stopOnFirstFailure && passed < completed) rows.AppendLine("Stopped: first failure captured.");
            else rows.AppendLine("Stopped: requested seed count completed.");

            if (aggregateFailures.Count > 0)
            {
                var sorted = new List<KeyValuePair<GenerationFailureReason, int>>(aggregateFailures);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                rows.AppendLine();
                rows.AppendLine("FAILURE DISTRIBUTION");
                foreach (var entry in sorted)
                    rows.AppendLine($"{entry.Value,7}  {entry.Key}");
            }

            if (failingSeeds.Count > 0)
            {
                rows.AppendLine();
                rows.AppendLine("REPRODUCIBLE FAILING SEEDS");
                rows.AppendLine(string.Join(", ", failingSeeds));
            }

            resultsText = rows.ToString();
            latestReportPath = SaveReport(resultsText);
            Repaint();
        }

        private static int NextSeed(int current)
        {
            unchecked
            {
                return current * 1664525 + 1013904223;
            }
        }

        private static string ShortPass(string acceptedPass)
        {
            if (string.IsNullOrWhiteSpace(acceptedPass)) return "—";
            if (acceptedPass.Length <= 8) return acceptedPass;
            return acceptedPass.Substring(0, 8);
        }

        private static string SaveReport(string text)
        {
            string directory = Path.Combine(Application.dataPath, "..", "Library", "SlingshotDiagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"V2StabilitySweep_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, text ?? "");
            return Path.GetFullPath(path);
        }
    }
}
#endif
