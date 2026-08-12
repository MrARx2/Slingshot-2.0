using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    /// <summary>
    /// Creates two persistent authored variants, regenerates the canonical
    /// reference craft, reloads both variants, and proves that their local
    /// tuning survives while catalog products remain shared.
    /// </summary>
    public static class V3ControlledAuthoredVariantEvidenceWriter
    {
        private const string Root =
            "Assets/HovercraftV3/Content/ControlledTests/Variants";
        private const string VariantAPath = Root + "/ScenarioG_VariantA.asset";
        private const string VariantBPath = Root + "/ScenarioG_VariantB.asset";

        private sealed class Snapshot
        {
            public string stableId;
            public string guid;
            public float targetHeight;
            public float captureRange;
            public float automaticCap;
            public float manualCap;
            public bool emergencyAllowed;
            public Vector3 calibration;
            public string chassisGuid;
            public string catalogGuid;
            public string endpointGuid;
            public string connectorGuid;
        }

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Controlled Tests/Write Authored Variant Evidence")]
        public static void Write()
        {
            EnsureFolder(Root);
            CraftBuildDefinition source = AssetDatabase.LoadAssetAtPath<
                CraftBuildDefinition>(V3SystemsIntegrationPrototypeGenerator.BuildPath);
            if (source == null)
                throw new FileNotFoundException("Missing primary systems build.",
                    V3SystemsIntegrationPrototypeGenerator.BuildPath);

            CraftBuildDefinition variantA = CreateOrLoad(
                VariantAPath, source, "build.authored.controlled.variant_a.01");
            CraftBuildDefinition variantB = CreateOrLoad(
                VariantBPath, source, "build.authored.controlled.variant_b.01");
            ConfigureLocalTuning(variantA, 3.75f, 24f,
                0.8f, 1f, true, new Vector3(0.1f, 0f, 0f));
            ConfigureLocalTuning(variantB, 5.25f, 30f,
                0.55f, 0.7f, false, new Vector3(-0.1f, 0.05f, 0f));
            EditorUtility.SetDirty(variantA);
            EditorUtility.SetDirty(variantB);
            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(
                new[] { VariantAPath, VariantBPath });
            AssetDatabase.SaveAssets();

            Snapshot beforeA = Capture(variantA, VariantAPath);
            Snapshot beforeB = Capture(variantB, VariantBPath);
            V3SystemsIntegrationPrototypeGenerator.Generate();
            AssetDatabase.ImportAsset(VariantAPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(VariantBPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            variantA = AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(VariantAPath);
            variantB = AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(VariantBPath);
            Snapshot afterA = Capture(variantA, VariantAPath);
            Snapshot afterB = Capture(variantB, VariantBPath);

            bool aPersisted = Equal(beforeA, afterA);
            bool bPersisted = Equal(beforeB, afterB);
            bool independent = !Mathf.Approximately(
                afterA.targetHeight, afterB.targetHeight) &&
                !Mathf.Approximately(afterA.captureRange, afterB.captureRange) &&
                !Mathf.Approximately(afterA.automaticCap, afterB.automaticCap) &&
                afterA.calibration != afterB.calibration;
            bool sharedProducts = afterA.chassisGuid == afterB.chassisGuid &&
                afterA.catalogGuid == afterB.catalogGuid &&
                afterA.endpointGuid == afterB.endpointGuid &&
                afterA.connectorGuid == afterB.connectorGuid;
            bool identity = afterA.guid != afterB.guid &&
                afterA.stableId != afterB.stableId &&
                variantA.Ownership == V3BuildOwnership.AuthoredVariant &&
                variantB.Ownership == V3BuildOwnership.AuthoredVariant &&
                !variantA.AllowLegacyPipelineFallback &&
                !variantB.AllowLegacyPipelineFallback;
            bool passed = aPersisted && bPersisted && independent &&
                sharedProducts && identity;

            string package = Path.GetFullPath(Path.Combine(
                "TestDriveReports", "HovercraftV3", "Controlled", "Authoring",
                DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) +
                "_scenario_g_variant_regeneration"));
            Directory.CreateDirectory(package);
            var text = new StringBuilder(4096);
            text.AppendLine("# Scenario G — Authored Variant Persistence");
            text.AppendLine();
            text.AppendLine("- Result: **" + (passed ? "PASS" : "FAIL") + "**");
            text.AppendLine("- Regenerator: `V3SystemsIntegrationPrototypeGenerator.Generate`");
            text.AppendLine("- Variant A: `" + VariantAPath + "` / `" + afterA.guid + "`");
            text.AppendLine("- Variant B: `" + VariantBPath + "` / `" + afterB.guid + "`");
            text.AppendLine();
            text.AppendLine("## Assertions");
            text.AppendLine();
            Append(text, "Variant A local values persisted after regeneration", aPersisted);
            Append(text, "Variant B local values persisted after regeneration", bPersisted);
            Append(text, "Variant local tuning remains independent", independent);
            Append(text, "Product/chassis/catalog references remain shared", sharedProducts);
            Append(text, "Stable IDs, GUIDs, ownership, and fallback policy are valid", identity);
            text.AppendLine();
            text.AppendLine("## Persisted values");
            text.AppendLine();
            text.AppendLine("| Variant | Target height | Capture range | Auto cap | Manual cap | Emergency | Calibration |");
            text.AppendLine("|---|---:|---:|---:|---:|---|---|");
            AppendValues(text, "A", afterA);
            AppendValues(text, "B", afterB);
            File.WriteAllText(Path.Combine(package, "asset_persistence_report.md"),
                text.ToString());
            File.WriteAllText(Path.Combine(package, "asset_persistence_result.json"),
                "{\n" +
                "  \"schemaVersion\": 6,\n" +
                "  \"testId\": \"test.v3.authored_variant.persistence.01\",\n" +
                "  \"passed\": " + passed.ToString().ToLowerInvariant() + ",\n" +
                "  \"variantAPath\": \"" + VariantAPath + "\",\n" +
                "  \"variantAGuid\": \"" + afterA.guid + "\",\n" +
                "  \"variantBPath\": \"" + VariantBPath + "\",\n" +
                "  \"variantBGuid\": \"" + afterB.guid + "\"\n" +
                "}\n");
            Debug.Log("Controlled authored-variant evidence: " + package);
            if (!passed)
                throw new InvalidOperationException(
                    "Authored variant persistence assertions failed. See " + package);
        }

        public static void WriteFromCommandLine() => Write();

        private static CraftBuildDefinition CreateOrLoad(
            string path, CraftBuildDefinition source, string stableId)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(path);
            if (build == null)
            {
                build = ScriptableObject.CreateInstance<CraftBuildDefinition>();
                build.CopySelectionsFrom(source);
                build.CopyPresentationFrom(source);
                AssetDatabase.CreateAsset(build, path);
                AssetDatabase.SaveAssets();
            }
            build.ConfigureAuthoringIdentity(
                stableId,
                AssetDatabase.AssetPathToGUID(path),
                V3BuildOwnership.AuthoredVariant,
                false);
            return build;
        }

        private static void ConfigureLocalTuning(
            CraftBuildDefinition build,
            float targetHeight,
            float captureRange,
            float automaticCap,
            float manualCap,
            bool emergency,
            Vector3 calibration)
        {
            var serialized = new SerializedObject(build);
            SerializedProperty hover = serialized.FindProperty("hoverConfiguration");
            hover.FindPropertyRelative("targetHoverHeight").floatValue = targetHeight;
            hover.FindPropertyRelative("captureRange").floatValue = captureRange;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (build.Installations.Count == 0)
                throw new InvalidOperationException("Variant has no installations.");
            build.Installations[0].ConfigureAuthority(
                automaticCap, manualCap, emergency, calibration);
        }

        private static Snapshot Capture(CraftBuildDefinition build, string path)
        {
            if (build == null)
                throw new InvalidDataException("Could not reload " + path);
            SocketInstallation first = build.Installations.Count > 0
                ? build.Installations[0] : null;
            return new Snapshot
            {
                stableId = build.StableId,
                guid = AssetDatabase.AssetPathToGUID(path),
                targetHeight = build.HoverConfiguration.TargetHoverHeight,
                captureRange = build.HoverConfiguration.CaptureRange,
                automaticCap = first != null ? first.AutomaticAuthorityCap : -1f,
                manualCap = first != null ? first.ManualAuthorityCap : -1f,
                emergencyAllowed = first != null && first.EmergencyAllowed,
                calibration = first != null ? first.LocalCalibration : Vector3.zero,
                chassisGuid = GuidOf(build.Chassis),
                catalogGuid = GuidOf(build.Catalog),
                endpointGuid = GuidOf(first != null ? first.Endpoint : null),
                connectorGuid = GuidOf(first != null ? first.Connector : null)
            };
        }

        private static bool Equal(Snapshot a, Snapshot b)
        {
            return a.stableId == b.stableId && a.guid == b.guid &&
                Mathf.Approximately(a.targetHeight, b.targetHeight) &&
                Mathf.Approximately(a.captureRange, b.captureRange) &&
                Mathf.Approximately(a.automaticCap, b.automaticCap) &&
                Mathf.Approximately(a.manualCap, b.manualCap) &&
                a.emergencyAllowed == b.emergencyAllowed &&
                a.calibration == b.calibration &&
                a.chassisGuid == b.chassisGuid && a.catalogGuid == b.catalogGuid &&
                a.endpointGuid == b.endpointGuid &&
                a.connectorGuid == b.connectorGuid;
        }

        private static string GuidOf(UnityEngine.Object value)
        {
            return value != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value))
                : string.Empty;
        }

        private static void Append(StringBuilder text, string label, bool passed)
        {
            text.AppendLine("- " + (passed ? "PASS" : "FAIL") + ": " + label);
        }

        private static void AppendValues(StringBuilder text, string label,
            Snapshot value)
        {
            text.Append("| ").Append(label).Append(" | ")
                .Append(value.targetHeight.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(value.captureRange.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(value.automaticCap.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(value.manualCap.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ").Append(value.emergencyAllowed)
                .Append(" | ").Append(value.calibration).AppendLine(" |");
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
