using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using TrackGeneration.Design;
using TrackGeneration.Definitions;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Human-facing quality assessment stored with an accepted exact recipe. This is
    /// output metadata, not a deterministic generation input, so it is deliberately
    /// excluded from recipe identity hashes.
    /// </summary>
    [Serializable]
    public sealed class TrackRecipeRating
    {
        public const int CurrentVersion = 1;

        public int Version;
        [Range(0, 100)] public int Overall;
        [Range(0, 100)] public int Flow;
        [Range(0, 100)] public int Variety;
        [Range(0, 100)] public int RequestFit;
        [Range(0, 100)] public int TechnicalQuality;
        public string RatedLayoutHash = "";
        public string Source = "";

        public bool IsRated => Version == CurrentVersion &&
                               Overall >= 0 && Overall <= 100;

        public void Normalize()
        {
            RatedLayoutHash ??= "";
            Source ??= "";
            if (Version <= 0)
            {
                Version = 0;
                Overall = Flow = Variety = RequestFit = TechnicalQuality = 0;
                return;
            }

            Overall = Mathf.Clamp(Overall, 0, 100);
            Flow = Mathf.Clamp(Flow, 0, 100);
            Variety = Mathf.Clamp(Variety, 0, 100);
            RequestFit = Mathf.Clamp(RequestFit, 0, 100);
            TechnicalQuality = Mathf.Clamp(TechnicalQuality, 0, 100);
        }
    }

    /// <summary>
    /// Converts accepted output measurements into a stable, readable 0–100 rating.
    /// The candidate-search score remains untouched: it is intentionally harsher and
    /// may be negative because it exists to rank candidates, not judge a finished lap.
    /// </summary>
    public static class TrackRecipeRatingCalculator
    {
        public static TrackRecipeRating Calculate(
            TrackGenerationMetrics metrics,
            TrackGenerationReport report,
            TrackDesignerSettings settings,
            string layoutHash)
        {
            if (metrics == null || report == null || settings == null || !report.Success)
                return new TrackRecipeRating();

            settings.Sanitize();

            float flow = metrics.RhythmScore > 0.01f ? metrics.RhythmScore : 82f;
            flow -= Mathf.Min(25f, metrics.RhythmViolationCount * 5f);
            flow -= Mathf.Min(18f,
                Mathf.Max(0, metrics.LongestEncounterFamilyStreak - 2) * 6f);
            flow = Mathf.Clamp(flow, 0f, 100f);

            int fallbackEncounterCount = metrics.LoopCount + metrics.CorkscrewCount +
                                         metrics.SpiralCount + metrics.HalfLoopCount +
                                         metrics.JumpCount + metrics.HairpinCount +
                                         metrics.ChicaneCount + metrics.SCurveCount +
                                         metrics.FullPipeCount + metrics.WallrideCount;
            int encounterCount = Mathf.Max(metrics.EncounterCount, fallbackEncounterCount);
            int distinctFamilies = metrics.DistinctEncounterFamilies;
            if (distinctFamilies <= 0)
            {
                distinctFamilies += metrics.LoopCount > 0 ? 1 : 0;
                distinctFamilies += metrics.CorkscrewCount > 0 ? 1 : 0;
                distinctFamilies += metrics.SpiralCount > 0 ? 1 : 0;
                distinctFamilies += metrics.HalfLoopCount > 0 ? 1 : 0;
                distinctFamilies += metrics.JumpCount > 0 ? 1 : 0;
                distinctFamilies += metrics.HairpinCount > 0 ? 1 : 0;
                distinctFamilies += metrics.ChicaneCount > 0 || metrics.SCurveCount > 0 ? 1 : 0;
                distinctFamilies += metrics.FullPipeCount > 0 ? 1 : 0;
                distinctFamilies += metrics.WallrideCount > 0 ? 1 : 0;
            }
            float variety = 50f;
            if (encounterCount > 0)
            {
                float coverage = Mathf.Clamp01(distinctFamilies /
                    (float)Mathf.Max(1, Mathf.Min(8, encounterCount)));
                float richness = Mathf.Clamp01(encounterCount / 8f);
                variety = coverage * 70f + richness * 30f;
            }

            float targetLap = Mathf.Max(1f, settings.Scale.TargetLapTimeSeconds);
            float lapFit = 100f * (1f - Mathf.Clamp01(
                Mathf.Abs(metrics.EstimatedNeutralLapTimeSeconds - targetLap) / targetLap));
            float requestedTurnCenter = (settings.Layout.MinTurnCount +
                                         settings.Layout.MaxTurnCount) * 0.5f;
            float turnFit = Mathf.Clamp(100f -
                Mathf.Abs(metrics.TurnCount - requestedTurnCenter) * 12f, 0f, 100f);
            float targetElevation = settings.Elevation.TargetElevationAmplitude;
            float elevationFit = targetElevation <= 5f
                ? 100f
                : 100f * (1f - Mathf.Clamp01(
                    Mathf.Abs(metrics.ElevationRange - targetElevation) / targetElevation));
            float requestFit = lapFit * 0.50f + turnFit * 0.20f + elevationFit * 0.30f;

            float technical = 100f;
            if (report.UsedFallback) technical -= 25f;
            technical -= Mathf.Min(20f, report.RelaxedSettings.Count * 5f);
            technical -= Mathf.Min(20f, report.Warnings.Count * 4f);

            float ringBudget = Mathf.Max(1f, settings.Generation.TargetTotalRings);
            float ringUse = metrics.TotalRings / ringBudget;
            if (ringUse > 0.85f)
                technical -= Mathf.Clamp01((ringUse - 0.85f) / 0.75f) * 20f;

            float targetFacet = Mathf.Max(0.01f,
                settings.Generation.MaxFacetAngleDegrees);
            if (metrics.MaxFacetAngleObserved > targetFacet)
            {
                technical -= Mathf.Clamp01((metrics.MaxFacetAngleObserved - targetFacet) /
                                             (targetFacet * 3f)) * 20f;
            }
            technical = Mathf.Clamp(technical, 0f, 100f);

            int flowInt = Mathf.RoundToInt(flow);
            int varietyInt = Mathf.RoundToInt(Mathf.Clamp(variety, 0f, 100f));
            int requestInt = Mathf.RoundToInt(Mathf.Clamp(requestFit, 0f, 100f));
            int technicalInt = Mathf.RoundToInt(technical);
            int overall = Mathf.RoundToInt(flowInt * 0.35f + varietyInt * 0.25f +
                                           requestInt * 0.20f + technicalInt * 0.20f);

            return new TrackRecipeRating
            {
                Version = TrackRecipeRating.CurrentVersion,
                Overall = Mathf.Clamp(overall, 0, 100),
                Flow = flowInt,
                Variety = varietyInt,
                RequestFit = requestInt,
                TechnicalQuality = technicalInt,
                RatedLayoutHash = layoutHash ?? "",
                Source = "Accepted generation metrics"
            };
        }
    }

    /// <summary>How strictly an imported recipe must match this generator build.</summary>
    public enum RecipeReplayMode
    {
        Strict,
        Compatible,
        BestEffort
    }

    /// <summary>The largest area an interactive feature replacement may replan.</summary>
    public enum LocalReplanScope
    {
        FeatureOnly,
        OwnedConnectors,
        TurnComplex,
        Quarter,
        WholeLayout
    }

    /// <summary>One named, deterministic parameter override for a feature realization.</summary>
    [Serializable]
    public sealed class FeatureParameterOverride
    {
        public string Name = "";
        public float Value;
    }

    /// <summary>
    /// One neighboring authored feature that a Track Editor rebuild is allowed to
    /// consume. Stable identity is stored alongside the friendly relative offset so
    /// Exact Replay never relies on a mutable definition index.
    /// </summary>
    [Serializable]
    public sealed class TopologyImpactMember
    {
        public string TopologySlotId = "";
        public string StructuralAnchor = "";
        public int RelativeFeatureOffset;
        public SemanticElementId OriginalRealization = SemanticElementId.None;

        internal void Normalize()
        {
            TopologySlotId = (TopologySlotId ?? "").Trim();
            StructuralAnchor = (StructuralAnchor ?? "").Trim();
        }
    }

    /// <summary>
    /// A handpicked realization placed on top of a generated topology demand. V2 Stage 4
    /// will consume these records; the recipe schema owns them now so edited variants do
    /// not require a breaking serialization change later.
    /// </summary>
    [Serializable]
    public sealed class TopologySlotOverride
    {
        public string TopologySlotId = "";
        public int CanonicalOrder = -1;
        // Compact, version-tolerant demand identity used when connector/closure passes
        // change the route-order token inside TopologySlotId. Empty on older recipes.
        public string StructuralAnchor = "";
        public SemanticElementId OriginalRealization = SemanticElementId.None;
        public SemanticElementId RequestedRealization = SemanticElementId.None;
        // Explicitly distinguishes an intentional rebuild of the design already in use
        // from a replacement request that accidentally points back to the current design.
        public bool RebuildCurrentRealization;
        public bool LockRealization = true;
        public bool LockParameters;
        public LocalReplanScope MaximumReplanScope = LocalReplanScope.OwnedConnectors;
        public List<FeatureParameterOverride> Parameters = new List<FeatureParameterOverride>();
        // Zero keeps neighboring features protected. Non-zero values are designer
        // authority, not random-generation tolerances: the exact affected identities
        // below must also be present and confirmed by the Track Editor.
        public int ImpactBackwardFeatures;
        public int ImpactForwardFeatures;
        public bool AllowFeatureOverrides;
        public List<TopologyImpactMember> ImpactMembers = new List<TopologyImpactMember>();

        internal void Normalize()
        {
            TopologySlotId = (TopologySlotId ?? "").Trim();
            StructuralAnchor = (StructuralAnchor ?? "").Trim();
            Parameters ??= new List<FeatureParameterOverride>();
            Parameters.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            foreach (FeatureParameterOverride parameter in Parameters)
                parameter.Name = parameter.Name.Trim();
            Parameters.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            ImpactBackwardFeatures = Mathf.Clamp(ImpactBackwardFeatures, 0, 8);
            ImpactForwardFeatures = Mathf.Clamp(ImpactForwardFeatures, 0, 8);
            ImpactMembers ??= new List<TopologyImpactMember>();
            ImpactMembers.RemoveAll(member => member == null ||
                                                string.IsNullOrWhiteSpace(member.TopologySlotId));
            foreach (TopologyImpactMember member in ImpactMembers) member.Normalize();
            ImpactMembers.Sort((a, b) =>
            {
                int offset = a.RelativeFeatureOffset.CompareTo(b.RelativeFeatureOffset);
                return offset != 0
                    ? offset
                    : string.CompareOrdinal(a.TopologySlotId, b.TopologySlotId);
            });
        }
    }

    /// <summary>Track-root transform captured as deterministic recipe data.</summary>
    [Serializable]
    public sealed class TrackOriginSnapshot
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Scale = Vector3.one;
    }

    /// <summary>
    /// Complete, versioned input identity for one generated track. The friendly base
    /// seed is only one field: exact replay also needs streams, settings, rulebook,
    /// transform, selection policy, locks, versions, and handpicked slot overrides.
    /// </summary>
    [Serializable]
    public sealed class GenerationRecipeV1
    {
        public const int CurrentSchemaVersion = 1;
        public const string CurrentAlgorithmVersion = "slingshot-trackgen-v2-dev1";
        public const string CurrentFeatureCatalogVersion = "semantic-catalog-v1";

        public int SchemaVersion = CurrentSchemaVersion;
        public string AlgorithmVersion = CurrentAlgorithmVersion;
        public string FeatureCatalogVersion = CurrentFeatureCatalogVersion;
        public List<TrackFeatureDefinitionIdentity> FeatureDefinitions =
            new List<TrackFeatureDefinitionIdentity>();

        public int BaseSeed;
        public string SeedDisplayName = "";
        public TrackSeedStreams SeedStreams = new TrackSeedStreams();

        [Tooltip("Canonical TrackDesignerSettings JSON. Stored as text so schema/version checks happen before it is applied.")]
        public string DesignerSettingsJson = "";

        public int RulebookSchemaVersion;
        public string RulebookHash = "";
        public string AppliedPresetName = "";

        public SettingsLockState SettingsLocks = new SettingsLockState();
        public LayoutLockMode LayoutLockMode;
        public TrackOriginSnapshot Origin = new TrackOriginSnapshot();

        public CandidateSelectionMode SelectionMode;
        public int MaximumAttempts;
        public int CandidatesToScore;

        public string BaseRecipeHash = "";
        public int RecipeRevision;
        public List<TopologySlotOverride> TopologySlotOverrides = new List<TopologySlotOverride>();

        [Tooltip("True when this recipe is an accepted designer-authored Track Editor variant rather than a fresh procedural search.")]
        public bool DesignerAuthoredVariant;
        [Tooltip("Absolute deterministic attempt index of the accepted route that the edit rebuilds.")]
        public int AuthoringBaselineAttemptIndex = -1;
        [Tooltip("Accepted lap length before the edit. Used as an authoring baseline, never as a hard cap.")]
        public float AuthoringBaselineLapLengthMeters;
        [Tooltip("Compact pre-edit elevation state consumed as a deterministic input when rebuilding this authored variant across Undo, reload and Exact Replay.")]
        public List<AuthoringElevationBaselineEntry> AuthoringElevationBaseline =
            new List<AuthoringElevationBaselineEntry>();

        [Tooltip("Expected mesh-independent layout hash. Empty for a request that has not generated successfully yet.")]
        public string ExpectedLayoutHash = "";

        [Tooltip("Human-facing 0-100 assessment of the accepted output. Output metadata only; excluded from exact recipe identity.")]
        public TrackRecipeRating Rating = new TrackRecipeRating();

        [Tooltip("Hash of every recipe input, including overrides. Derived; never used as an input to itself.")]
        public string RecipeHash = "";

        public static GenerationRecipeV1 Capture(
            TrackSeed seed,
            TrackSeedStreams streams,
            TrackDesignerSettings settings,
            TrackConfig rulebook,
            SettingsLockState locks,
            LayoutLockMode layoutLock,
            Transform origin)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.Sanitize();
            var recipe = new GenerationRecipeV1
            {
                BaseSeed = seed.BaseSeed,
                SeedDisplayName = seed.DisplayName ?? "",
                SeedStreams = streams?.Clone() ?? new TrackSeedStreams(),
                FeatureDefinitions = TrackFeatureDefinitionCatalog.CaptureIdentities(),
                DesignerSettingsJson = settings.ToJson(),
                RulebookSchemaVersion = rulebook != null ? rulebook.SchemaVersion : 0,
                RulebookHash = rulebook != null ? GenerationHashUtility.Sha256Hex(JsonUtility.ToJson(rulebook)) : "",
                AppliedPresetName = settings.AppliedPresetName ?? "",
                SettingsLocks = CloneLocks(locks),
                LayoutLockMode = layoutLock,
                Origin = new TrackOriginSnapshot
                {
                    Position = origin != null ? origin.position : Vector3.zero,
                    Rotation = origin != null ? origin.rotation : Quaternion.identity,
                    Scale = origin != null ? origin.localScale : Vector3.one
                },
                SelectionMode = settings.Generation.SelectionMode,
                MaximumAttempts = settings.Generation.MaxAttempts,
                CandidatesToScore = settings.Generation.CandidatesToScore
            };
            recipe.RefreshHashes();
            return recipe;
        }

        public TrackDesignerSettings DeserializeDesignerSettings()
        {
            if (string.IsNullOrWhiteSpace(DesignerSettingsJson)) return null;
            TrackDesignerSettings settings = JsonUtility.FromJson<TrackDesignerSettings>(DesignerSettingsJson);
            settings?.Sanitize();
            return settings;
        }

        public GenerationRecipeV1 Clone()
        {
            return Deserialize(Serialize(this, false), out _);
        }

        /// <summary>Normalizes list order and refreshes both base and exact variant hashes.</summary>
        public void RefreshHashes()
        {
            Normalize();
            BaseRecipeHash = ComputeBaseRecipeHash();
            RecipeHash = ComputeRecipeHash();
        }

        public string ComputeBaseRecipeHash()
        {
            GenerationRecipeV1 copy = CloneForHash();
            copy.TopologySlotOverrides.Clear();
            copy.RecipeRevision = 0;
            copy.DesignerAuthoredVariant = false;
            copy.AuthoringBaselineAttemptIndex = -1;
            copy.AuthoringBaselineLapLengthMeters = 0f;
            copy.AuthoringElevationBaseline.Clear();
            return GenerationHashUtility.Sha256Hex(SerializeHashIdentity(copy));
        }

        public string ComputeRecipeHash()
        {
            GenerationRecipeV1 copy = CloneForHash();
            return GenerationHashUtility.Sha256Hex(SerializeHashIdentity(copy));
        }

        public bool ValidateFor(TrackConfig localRulebook, RecipeReplayMode mode, out string error)
        {
            Normalize();
            if (SchemaVersion != CurrentSchemaVersion)
            {
                if (mode != RecipeReplayMode.BestEffort)
                {
                    error = $"Recipe schema {SchemaVersion} is unsupported; this build expects {CurrentSchemaVersion}.";
                    return false;
                }
            }

            if (mode != RecipeReplayMode.BestEffort &&
                !string.Equals(AlgorithmVersion, CurrentAlgorithmVersion, StringComparison.Ordinal))
            {
                error = $"Generator algorithm '{AlgorithmVersion}' does not match '{CurrentAlgorithmVersion}'.";
                return false;
            }

            if (mode != RecipeReplayMode.BestEffort &&
                !string.Equals(FeatureCatalogVersion, CurrentFeatureCatalogVersion, StringComparison.Ordinal))
            {
                error = $"Feature catalog '{FeatureCatalogVersion}' does not match '{CurrentFeatureCatalogVersion}'.";
                return false;
            }

            // Recipes created before definition identities were introduced keep an
            // empty list and continue through the catalog-version compatibility path.
            // New strict recipes bind to exact content; Compatible mode permits a
            // content-only change only when the stable ID and explicit version agree.
            if (mode != RecipeReplayMode.BestEffort && FeatureDefinitions.Count > 0)
            {
                List<TrackFeatureDefinitionIdentity> localDefinitions =
                    TrackFeatureDefinitionCatalog.CaptureIdentities();
                for (int i = 0; i < FeatureDefinitions.Count; i++)
                {
                    TrackFeatureDefinitionIdentity expected = FeatureDefinitions[i];
                    TrackFeatureDefinitionIdentity local = localDefinitions.Find(candidate =>
                        string.Equals(candidate.StableId, expected.StableId, StringComparison.Ordinal));
                    bool sameIdentity = local != null &&
                                        expected.DefinitionVersion == local.DefinitionVersion;
                    bool sameContent = local != null &&
                                       string.Equals(expected.ContentHash, local.ContentHash,
                                           StringComparison.OrdinalIgnoreCase);
                    if (!sameIdentity || (mode == RecipeReplayMode.Strict && !sameContent))
                    {
                        error = $"Feature Definition '{expected.StableId}' does not match the local catalog.";
                        return false;
                    }
                }
            }

            if (DeserializeDesignerSettings() == null)
            {
                error = "Recipe contains no readable designer settings.";
                return false;
            }

            if (localRulebook == null)
            {
                error = "No local TrackConfig rulebook is assigned.";
                return false;
            }

            string localRulebookHash = GenerationHashUtility.Sha256Hex(JsonUtility.ToJson(localRulebook));
            if (mode == RecipeReplayMode.Strict &&
                !string.Equals(RulebookHash, localRulebookHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "The imported recipe was created with a different TrackConfig rulebook.";
                return false;
            }

            string suppliedHash = RecipeHash;
            string computedHash = ComputeRecipeHash();
            if (!string.IsNullOrEmpty(suppliedHash) &&
                !string.Equals(suppliedHash, computedHash, StringComparison.OrdinalIgnoreCase))
            {
                // Definition identities and topology structural anchors were additive
                // V1 fields. Accept only the exact older hash shapes when the imported
                // content genuinely has no value for the corresponding new field.
                bool hasNoStructuralAnchors = TopologySlotOverrides.TrueForAll(item =>
                    item == null || string.IsNullOrEmpty(item.StructuralAnchor));
                bool hasDefaultAuthoringFields = !DesignerAuthoredVariant &&
                                                 AuthoringBaselineAttemptIndex < 0 &&
                                                 AuthoringBaselineLapLengthMeters <= 0f;
                bool hasNoAuthoringElevationBaseline =
                    AuthoringElevationBaseline == null ||
                    AuthoringElevationBaseline.Count == 0;
                bool hasDefaultImpactFields = TopologySlotOverrides.TrueForAll(item =>
                    item == null ||
                    (item.ImpactBackwardFeatures == 0 && item.ImpactForwardFeatures == 0 &&
                     !item.AllowFeatureOverrides &&
                     (item.ImpactMembers == null || item.ImpactMembers.Count == 0)));
                bool matchedLegacyShape = false;
                for (int definitions = 0; definitions <= (FeatureDefinitions.Count == 0 ? 1 : 0) &&
                                          !matchedLegacyShape; definitions++)
                for (int anchors = 0; anchors <= (hasNoStructuralAnchors ? 1 : 0) &&
                                      !matchedLegacyShape; anchors++)
                for (int authoring = 0; authoring <= (hasDefaultAuthoringFields ? 1 : 0) &&
                                        !matchedLegacyShape; authoring++)
                for (int impact = 0; impact <= (hasDefaultImpactFields ? 1 : 0); impact++)
                for (int elevationBaseline = 0;
                     elevationBaseline <= (hasNoAuthoringElevationBaseline ? 1 : 0);
                     elevationBaseline++)
                {
                    if (definitions == 0 && anchors == 0 && authoring == 0 && impact == 0 &&
                        elevationBaseline == 0)
                        continue;
                    string legacyHash = ComputeLegacyRecipeHash(
                        definitions == 1, anchors == 1, authoring == 1, impact == 1,
                        elevationBaseline == 1);
                    if (string.Equals(suppliedHash, legacyHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        matchedLegacyShape = true;
                        break;
                    }
                }
                if (!matchedLegacyShape)
                {
                    error = "Recipe content hash is invalid or the recipe was modified outside the supported editor.";
                    return false;
                }
            }

            error = "";
            return true;
        }

        public static string Serialize(GenerationRecipeV1 recipe, bool prettyPrint = true)
        {
            if (recipe == null) return "";
            recipe.RefreshHashes();
            return JsonUtility.ToJson(recipe, prettyPrint);
        }

        public static GenerationRecipeV1 Deserialize(string json, out string error)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Recipe JSON is empty.";
                return null;
            }

            try
            {
                GenerationRecipeV1 recipe = JsonUtility.FromJson<GenerationRecipeV1>(json);
                if (recipe == null)
                {
                    error = "Recipe JSON did not contain a GenerationRecipeV1 object.";
                    return null;
                }
                recipe.Normalize();
                error = "";
                return recipe;
            }
            catch (Exception exception)
            {
                error = $"Recipe JSON could not be read: {exception.Message}";
                return null;
            }
        }

        private GenerationRecipeV1 CloneForHash()
        {
            Normalize();
            string recipeHash = RecipeHash;
            string baseHash = BaseRecipeHash;
            string expectedHash = ExpectedLayoutHash;
            RecipeHash = "";
            BaseRecipeHash = "";
            ExpectedLayoutHash = "";
            try
            {
                GenerationRecipeV1 copy = JsonUtility.FromJson<GenerationRecipeV1>(JsonUtility.ToJson(this, false));
                copy.Normalize();
                return copy;
            }
            finally
            {
                RecipeHash = recipeHash;
                BaseRecipeHash = baseHash;
                ExpectedLayoutHash = expectedHash;
            }
        }

        private string ComputeLegacyRecipeHash(bool removeDefinitions,
            bool removeStructuralAnchors, bool removeAuthoringFields,
            bool removeImpactFields, bool removeAuthoringElevationBaseline)
        {
            GenerationRecipeV1 copy = CloneForHash();
            string json = SerializeHashIdentity(copy);
            if (removeDefinitions)
                json = json.Replace(",\"FeatureDefinitions\":[]", "");
            if (removeStructuralAnchors)
                json = json.Replace(",\"StructuralAnchor\":\"\"", "");
            if (removeAuthoringFields)
            {
                json = RemovePrimitiveJsonField(json, nameof(DesignerAuthoredVariant));
                json = RemovePrimitiveJsonField(json, nameof(AuthoringBaselineAttemptIndex));
                json = RemovePrimitiveJsonField(json, nameof(AuthoringBaselineLapLengthMeters));
            }
            if (removeImpactFields)
            {
                json = RemoveAllPrimitiveJsonFields(json,
                    nameof(TopologySlotOverride.ImpactBackwardFeatures));
                json = RemoveAllPrimitiveJsonFields(json,
                    nameof(TopologySlotOverride.ImpactForwardFeatures));
                json = RemoveAllPrimitiveJsonFields(json,
                    nameof(TopologySlotOverride.AllowFeatureOverrides));
                json = json.Replace(",\"ImpactMembers\":[]", "");
            }
            if (removeAuthoringElevationBaseline)
                json = RemoveJsonArrayField(json,
                    nameof(AuthoringElevationBaseline));
            return GenerationHashUtility.Sha256Hex(json);
        }

        private static string SerializeHashIdentity(GenerationRecipeV1 recipe)
        {
            string json = JsonUtility.ToJson(recipe, false);
            return RemoveJsonObjectField(json, nameof(Rating));
        }

        private static string RemoveJsonObjectField(string json, string field)
        {
            string token = $",\"{field}\":";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) return json;
            int objectStart = start + token.Length;
            if (objectStart >= json.Length || json[objectStart] != '{') return json;

            int depth = 0;
            bool quoted = false;
            bool escaped = false;
            for (int i = objectStart; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                    return json.Remove(start, i - start + 1);
            }
            return json;
        }

        private static string RemoveJsonArrayField(string json, string field)
        {
            string token = $",\"{field}\":";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) return json;
            int arrayStart = start + token.Length;
            if (arrayStart >= json.Length || json[arrayStart] != '[') return json;

            int depth = 0;
            bool quoted = false;
            bool escaped = false;
            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == '[') depth++;
                else if (c == ']' && --depth == 0)
                    return json.Remove(start, i - start + 1);
            }
            return json;
        }

        private static string RemoveAllPrimitiveJsonFields(string json, string field)
        {
            string previous;
            do
            {
                previous = json;
                json = RemovePrimitiveJsonField(json, field);
            } while (!string.Equals(previous, json, StringComparison.Ordinal));
            return json;
        }

        private static string RemovePrimitiveJsonField(string json, string field)
        {
            string token = $",\"{field}\":";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) return json;
            int end = start + token.Length;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Remove(start, end - start);
        }

        private void Normalize()
        {
            AlgorithmVersion ??= "";
            FeatureCatalogVersion ??= "";
            SeedDisplayName ??= "";
            DesignerSettingsJson ??= "";
            RulebookHash ??= "";
            AppliedPresetName ??= "";
            BaseRecipeHash ??= "";
            ExpectedLayoutHash ??= "";
            RecipeHash ??= "";
            Rating ??= new TrackRecipeRating();
            Rating.Normalize();
            SeedStreams ??= new TrackSeedStreams();
            SettingsLocks ??= new SettingsLockState();
            Origin ??= new TrackOriginSnapshot();
            FeatureDefinitions ??= new List<TrackFeatureDefinitionIdentity>();
            FeatureDefinitions.RemoveAll(definition =>
                definition == null || string.IsNullOrWhiteSpace(definition.StableId));
            foreach (TrackFeatureDefinitionIdentity definition in FeatureDefinitions)
                definition.Normalize();
            FeatureDefinitions.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
            TopologySlotOverrides ??= new List<TopologySlotOverride>();
            TopologySlotOverrides.RemoveAll(o => o == null || string.IsNullOrWhiteSpace(o.TopologySlotId));
            foreach (TopologySlotOverride slotOverride in TopologySlotOverrides) slotOverride.Normalize();
            TopologySlotOverrides.Sort((a, b) =>
            {
                int byId = string.CompareOrdinal(a.TopologySlotId, b.TopologySlotId);
                return byId != 0 ? byId : a.CanonicalOrder.CompareTo(b.CanonicalOrder);
            });
            RecipeRevision = Mathf.Max(0, RecipeRevision);
            AuthoringBaselineAttemptIndex = Mathf.Max(-1, AuthoringBaselineAttemptIndex);
            AuthoringBaselineLapLengthMeters = Mathf.Max(0f, AuthoringBaselineLapLengthMeters);
            AuthoringElevationBaseline ??= new List<AuthoringElevationBaselineEntry>();
            AuthoringElevationBaseline.RemoveAll(entry => entry == null ||
                string.IsNullOrWhiteSpace(entry.MatchKey));
            AuthoringElevationBaseline.Sort((a, b) =>
                string.CompareOrdinal(a.MatchKey, b.MatchKey));
        }

        private static SettingsLockState CloneLocks(SettingsLockState source)
        {
            if (source == null) return new SettingsLockState();
            return JsonUtility.FromJson<SettingsLockState>(JsonUtility.ToJson(source)) ?? new SettingsLockState();
        }
    }

    /// <summary>Verified output identity produced from an accepted recipe and layout.</summary>
    [Serializable]
    public sealed class TrackResultManifest
    {
        public int ManifestSchemaVersion = 1;
        public string RecipeHash = "";
        public int SelectedAttemptIndex = -1;
        public int SelectedCandidateIndex = -1;
        public string CanonicalLayoutHash = "";
        public string QuantizedConnectionFrameChecksum = "";
        public string AppliedOverrideChecksum = "";
        public List<string> OrderedSemanticElements = new List<string>();
        public TrackGenerationMetrics Metrics = new TrackGenerationMetrics();

        public static TrackResultManifest Capture(GenerationRecipeV1 recipe, TrackGenerationResult result)
        {
            if (recipe == null || result?.Layout == null) return null;
            var manifest = new TrackResultManifest
            {
                RecipeHash = recipe.RecipeHash,
                SelectedAttemptIndex = result.SelectedAttemptIndex,
                SelectedCandidateIndex = result.SelectedCandidateIndex,
                CanonicalLayoutHash = TrackCanonicalHasher.ComputeLayoutHash(result.Layout),
                QuantizedConnectionFrameChecksum = TrackCanonicalHasher.ComputeConnectionFrameChecksum(result.Layout),
                AppliedOverrideChecksum = GenerationHashUtility.Sha256Hex(
                    JsonUtility.ToJson(new TopologyOverrideList { Items = recipe.TopologySlotOverrides }, false)),
                Metrics = result.Layout.Metrics
            };

            foreach (GeneratedTrackSection section in result.Layout.Sections)
            {
                if (section?.Definition == null) continue;
                manifest.OrderedSemanticElements.Add(section.Definition.SemanticElement.ToString());
            }
            return manifest;
        }

        [Serializable]
        private sealed class TopologyOverrideList
        {
            public List<TopologySlotOverride> Items = new List<TopologySlotOverride>();
        }
    }

    /// <summary>Mesh-independent, quantized identity of the accepted macro layout.</summary>
    public static class TrackCanonicalHasher
    {
        public static string ComputeLayoutHash(GeneratedTrackLayout layout)
        {
            var text = new StringBuilder(4096);
            if (layout == null)
            {
                text.Append("null-layout");
                return GenerationHashUtility.Sha256Hex(text.ToString());
            }

            text.Append("layout-v1|");
            Add(text, layout.Sections?.Count ?? 0);
            Add(text, Quantize(layout.LapLength, 1000f));
            if (layout.Sections != null)
            {
                foreach (GeneratedTrackSection section in layout.Sections)
                    AppendSection(text, section);
            }
            AppendQuarters(text, layout.Quarters);
            return GenerationHashUtility.Sha256Hex(text.ToString());
        }

        public static string ComputeConnectionFrameChecksum(GeneratedTrackLayout layout)
        {
            var text = new StringBuilder(4096);
            text.Append("frames-v1|");
            if (layout?.Sections != null)
            {
                Add(text, layout.Sections.Count);
                foreach (GeneratedTrackSection section in layout.Sections)
                {
                    if (section == null) { text.Append("null|"); continue; }
                    Add(text, section.SectionIndex);
                    AppendFrame(text, section.StartFrame);
                    AppendFrame(text, section.EndFrame);
                }
            }
            return GenerationHashUtility.Sha256Hex(text.ToString());
        }

        private static void AppendSection(StringBuilder text, GeneratedTrackSection section)
        {
            if (section == null)
            {
                text.Append("null-section|");
                return;
            }

            TrackMacroSectionDefinition definition = section.Definition;
            Add(text, section.SectionIndex);
            Add(text, section.QuarterIndex);
            Add(text, section.RoadId);
            Add(text, section.CapStart ? 1 : 0);
            Add(text, section.CapEnd ? 1 : 0);
            Add(text, section.OpenStart ? 1 : 0);
            Add(text, section.OpenEnd ? 1 : 0);
            Add(text, section.ConnectsToAirGap ? 1 : 0);
            Add(text, section.PatternId ?? "");
            if (definition == null)
            {
                text.Append("null-definition|");
            }
            else
            {
                Add(text, (int)definition.SectionType);
                Add(text, (int)definition.SemanticElement);
                Add(text, (int)definition.Direction);
                Add(text, Quantize(definition.Length, 1000f));
                Add(text, Quantize(definition.Width, 1000f));
                Add(text, Quantize(definition.TurnAngle, 1000f));
                Add(text, Quantize(definition.Radius, 1000f));
                Add(text, Quantize(definition.SecondaryRadius, 1000f));
                Add(text, Quantize(definition.BankingAngle, 1000f));
                Add(text, Quantize(definition.ElevationChange, 1000f));
                Add(text, Quantize(definition.PitchChange, 1000f));
                Add(text, Quantize(definition.RollChange, 1000f));
                Add(text, Quantize(definition.HillHeight, 1000f));
                Add(text, Quantize(definition.PlanLateralOffset, 1000f));
                Add(text, Quantize(definition.PlanHorizontalLength, 1000f));
                Add(text, (int)definition.SpeedIntent);
                Add(text, (int)definition.RiskLevel);
                Add(text, definition.RequiresRecoveryAfter ? 1 : 0);
                Add(text, definition.AllowsBoost ? 1 : 0);
                Add(text, definition.AllowsJump ? 1 : 0);
                Add(text, definition.LockLength ? 1 : 0);
                Add(text, definition.QuarterIndex);
                Add(text, definition.RoadId);
                Add(text, Quantize(definition.AirtimeSeconds, 100000f));
                Add(text, Quantize(definition.JumpApexHeight, 1000f));
                Add(text, Quantize(definition.JumpTimeToApex, 100000f));
                Add(text, definition.JumpCapturesMinimumSpeed ? 1 : 0);
                Add(text, definition.JumpCapturesNominalSpeed ? 1 : 0);
                Add(text, definition.JumpCapturesMaximumSpeed ? 1 : 0);
                Add(text, Quantize(definition.SecondaryPitchDeg, 1000f));
                Add(text, Quantize(definition.FeatureEntryStraightFraction, 100000f));
                Add(text, Quantize(definition.FeatureExitStraightFraction, 100000f));
                Add(text, Quantize(definition.PipeCloseFraction, 100000f));
                Add(text, Quantize(definition.PipeOpenFraction, 100000f));
                Add(text, (int)definition.ConnectorBehavior);
                Add(text, Quantize(definition.BridgeCarry, 100000f));
                Add(text, Quantize(definition.MinimumLength, 1000f));
                Add(text, definition.TurnComplexId ?? "");
                AppendContract(text, definition.Contract);
                Add(text, definition.IsClosure ? 1 : 0);
                Add(text, definition.PatternId ?? "");
                Add(text, definition.RotationalPhases?.Count ?? 0);
                if (definition.RotationalPhases != null)
                {
                    foreach (RotationalPhaseDefinition phase in definition.RotationalPhases)
                    {
                        if (phase == null) { text.Append("null-phase|"); continue; }
                        Add(text, (int)phase.Axis);
                        Add(text, (int)phase.Direction);
                        Add(text, phase.RotationUnits);
                        Add(text, Quantize(phase.FirstHalfLength, 1000f));
                        Add(text, Quantize(phase.SecondHalfLength, 1000f));
                        Add(text, Quantize(phase.FirstHalfRadius, 1000f));
                        Add(text, Quantize(phase.SecondHalfRadius, 1000f));
                        Add(text, Quantize(phase.HorizontalTurnDegrees, 1000f));
                        Add(text, Quantize(phase.FirstHalfYawBiasDegrees, 1000f));
                        Add(text, Quantize(phase.SecondHalfYawBiasDegrees, 1000f));
                        Add(text, Quantize(phase.VerticalDriftDegrees, 1000f));
                        Add(text, Quantize(phase.FirstHalfPitchBiasDegrees, 1000f));
                        Add(text, Quantize(phase.SecondHalfPitchBiasDegrees, 1000f));
                        Add(text, Quantize(phase.CenterlineOrbitDegrees, 1000f));
                        Add(text, Quantize(phase.ExitHorizontalCurvature, 100000000f));
                        Add(text, Quantize(phase.ExitVerticalCurvature, 100000000f));
                        Add(text, Quantize(phase.ExitRoadRollRate, 100000f));
                        Add(text, Quantize(phase.ExitWidth, 1000f));
                        Add(text, (int)phase.BlendToNext);
                    }
                }
            }
            AppendFrame(text, section.StartFrame);
            AppendFrame(text, section.EndFrame);
        }

        private static void AppendContract(StringBuilder text, SectionConnectionContract contract)
        {
            Add(text, (int)contract.RequiredEntryOrientation);
            Add(text, (int)contract.ExitOrientation);
            Add(text, Quantize(contract.HeadingDeltaDegrees, 1000f));
            Add(text, Quantize(contract.PitchDeltaDegrees, 1000f));
            Add(text, Quantize(contract.RollDeltaDegrees, 1000f));
            Add(text, Quantize(contract.ElevationDelta, 1000f));
            Add(text, contract.ClosureCompatible ? 1 : 0);
        }

        private static void AppendQuarters(StringBuilder text, List<GeneratedTrackQuarter> quarters)
        {
            text.Append("quarters|");
            Add(text, quarters?.Count ?? 0);
            if (quarters == null) return;

            foreach (GeneratedTrackQuarter quarter in quarters)
            {
                if (quarter == null) { text.Append("null-quarter|"); continue; }
                Add(text, quarter.QuarterIndex);
                Add(text, (int)quarter.QuarterType);
                Add(text, Quantize(quarter.LogicalProgressStart, 1000000f));
                Add(text, Quantize(quarter.LogicalProgressEnd, 1000000f));
                Add(text, (int)quarter.ChoiceType);
                Add(text, Quantize(quarter.LaneSeparationMeters, 1000f));
                AppendFrame(text, quarter.EntryFrame);
                AppendFrame(text, quarter.ExitFrame);
                AppendQuarterRoute(text, quarter.RouteA);
                AppendQuarterRoute(text, quarter.RouteB);
            }
        }

        private static void AppendQuarterRoute(StringBuilder text, GeneratedQuarterRoute route)
        {
            if (route == null) { text.Append("null-route|"); return; }
            Add(text, route.RoadId);
            Add(text, route.FirstSectionIndex);
            Add(text, route.LastSectionIndex);
            Add(text, Quantize(route.PhysicalLengthMeters, 1000f));
            Add(text, route.FeaturePatternIds?.Count ?? 0);
            if (route.FeaturePatternIds != null)
                foreach (string patternId in route.FeaturePatternIds) Add(text, patternId ?? "");
            AppendFrame(text, route.EntryFrame);
            AppendFrame(text, route.ExitFrame);
        }

        private static void AppendFrame(StringBuilder text, TrackConnectionFrame frame)
        {
            AppendVector(text, frame.Position, 1000f);
            AppendVector(text, frame.Forward, 100000f);
            AppendVector(text, frame.Right, 100000f);
            AppendVector(text, frame.Up, 100000f);
            Add(text, Quantize(frame.Width, 1000f));
            Add(text, Quantize(frame.BankAngle, 1000f));
            Add(text, Quantize(frame.PitchAngle, 1000f));
            Add(text, Quantize(frame.AccumulatedRoadRoll, 1000f));
            Add(text, Quantize(frame.AccumulatedVerticalRotation, 1000f));
            Add(text, Quantize(frame.HorizontalCurvature, 100000000f));
            Add(text, Quantize(frame.HorizontalCurvatureRate, 100000000f));
            Add(text, Quantize(frame.VerticalCurvature, 100000000f));
            Add(text, Quantize(frame.VerticalCurvatureRate, 100000000f));
            Add(text, Quantize(frame.RoadRollRate, 100000f));
            Add(text, Quantize(frame.ArcLength, 1000f));
            Add(text, Quantize(frame.LapProgress, 1000000f));
        }

        private static void AppendVector(StringBuilder text, Vector3 value, float scale)
        {
            Add(text, Quantize(value.x, scale));
            Add(text, Quantize(value.y, scale));
            Add(text, Quantize(value.z, scale));
        }

        private static long Quantize(float value, float scale)
        {
            if (float.IsNaN(value)) return long.MinValue;
            if (float.IsPositiveInfinity(value)) return long.MaxValue;
            if (float.IsNegativeInfinity(value)) return long.MinValue + 1;
            return (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }

        private static void Add(StringBuilder text, int value) => text.Append(value).Append('|');
        private static void Add(StringBuilder text, long value) => text.Append(value).Append('|');
        private static void Add(StringBuilder text, string value)
            => text.Append(value?.Length ?? 0).Append(':').Append(value ?? "").Append('|');
    }

    /// <summary>Shared stable SHA-256 utility for recipe and manifest identities.</summary>
    public static class GenerationHashUtility
    {
        public static string Sha256Hex(string value)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }
}
