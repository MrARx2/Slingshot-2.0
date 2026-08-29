using System;
using System.Collections.Generic;
using System.Diagnostics;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Core
{
    [Serializable]
    public sealed class ReplayVerificationRun
    {
        public int RunIndex;
        public bool Generated;
        public int SelectedAttemptIndex = -1;
        public int SelectedCandidateIndex = -1;
        public string LayoutHash = "";
        public string ConnectionFrameChecksum = "";
        public double ElapsedSeconds;
        public string Failure = "";
    }

    /// <summary>Bounded, scene-free exact-replay verification result.</summary>
    [Serializable]
    public sealed class ReplayVerificationResult
    {
        public bool Success;
        public int RequestedRuns;
        public int CompletedRuns;
        public string ExpectedLayoutHash = "";
        public string VerifiedLayoutHash = "";
        public string Failure = "";
        public List<ReplayVerificationRun> Runs = new List<ReplayVerificationRun>();

        public override string ToString()
            => Success
                ? $"PASS — {CompletedRuns}/{RequestedRuns} exact runs matched {VerifiedLayoutHash}"
                : $"FAIL — {CompletedRuns}/{RequestedRuns} runs completed: {Failure}";
    }

    /// <summary>
    /// Runs an exact Generation Recipe repeatedly through the data pipeline without
    /// touching scene objects or meshes. This is the small V2 replay gate; it is not a
    /// corpus scanner and never searches for substitute seeds.
    /// </summary>
    public static class GenerationReplayVerifier
    {
        public static ReplayVerificationResult Verify(GenerationRecipeV1 recipe, TrackConfig rulebook,
            int repetitions = 2)
        {
            var audit = new ReplayVerificationResult
            {
                RequestedRuns = Math.Max(1, repetitions),
                ExpectedLayoutHash = recipe?.ExpectedLayoutHash ?? ""
            };

            if (recipe == null)
            {
                audit.Failure = "No Generation Recipe was supplied.";
                return audit;
            }
            if (!recipe.ValidateFor(rulebook, RecipeReplayMode.Strict, out string validationError))
            {
                audit.Failure = validationError;
                return audit;
            }
            var settings = recipe.DeserializeDesignerSettings();
            if (settings == null)
            {
                audit.Failure = "The recipe's designer settings could not be restored.";
                return audit;
            }

            ResolvedTrackGenerationConfig cfg = ResolvedTrackGenerationConfig.Resolve(rulebook, settings);
            var seed = TrackSeed.CreateNew(recipe.BaseSeed);
            seed.DisplayName = recipe.SeedDisplayName;
            string baselineHash = string.IsNullOrEmpty(recipe.ExpectedLayoutHash)
                ? ""
                : recipe.ExpectedLayoutHash;
            string baselineFrames = "";

            for (int runIndex = 0; runIndex < audit.RequestedRuns; runIndex++)
            {
                var run = new ReplayVerificationRun { RunIndex = runIndex };
                var timer = Stopwatch.StartNew();
                TrackGenerationResult generated = new TrackGenerationPipeline().Run(cfg, seed,
                    recipe.SeedStreams?.Clone(), recipe.TopologySlotOverrides);
                timer.Stop();
                run.ElapsedSeconds = timer.Elapsed.TotalSeconds;
                run.Generated = generated is { Success: true, Layout: not null };
                run.SelectedAttemptIndex = generated?.SelectedAttemptIndex ?? -1;
                run.SelectedCandidateIndex = generated?.SelectedCandidateIndex ?? -1;

                if (!run.Generated)
                {
                    run.Failure = LastFailure(generated);
                    audit.Runs.Add(run);
                    audit.Failure = $"Run {runIndex + 1} did not generate: {run.Failure}";
                    return audit;
                }

                run.LayoutHash = TrackCanonicalHasher.ComputeLayoutHash(generated.Layout);
                run.ConnectionFrameChecksum = TrackCanonicalHasher.ComputeConnectionFrameChecksum(generated.Layout);
                audit.Runs.Add(run);
                audit.CompletedRuns++;

                if (string.IsNullOrEmpty(baselineHash)) baselineHash = run.LayoutHash;
                if (string.IsNullOrEmpty(baselineFrames)) baselineFrames = run.ConnectionFrameChecksum;

                if (!string.Equals(baselineHash, run.LayoutHash, StringComparison.OrdinalIgnoreCase))
                {
                    audit.Failure = $"Run {runIndex + 1} layout hash {run.LayoutHash} did not match {baselineHash}.";
                    return audit;
                }
                if (!string.Equals(baselineFrames, run.ConnectionFrameChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    audit.Failure = $"Run {runIndex + 1} connection-frame checksum changed.";
                    return audit;
                }
            }

            audit.Success = true;
            audit.VerifiedLayoutHash = baselineHash;
            return audit;
        }

        private static string LastFailure(TrackGenerationResult result)
        {
            if (result?.Report?.Failures == null || result.Report.Failures.Count == 0)
                return "generation failed without a detailed failure record";
            return result.Report.Failures[result.Report.Failures.Count - 1].ToString();
        }
    }
}
