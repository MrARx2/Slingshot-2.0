using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    /// <summary>
    /// Keeps generated build assets and future regeneration on the same
    /// serialized hover-configuration contract.
    /// </summary>
    public static class V3HoverConfigurationAssetUtility
    {
        [MenuItem("Tools/Hovercraft V3/Install Serialized Hover Configurations")]
        public static void InstallGeneratedConfigurations()
        {
            ConfigureKnownBuild(
                V2ReferencePrototypeGenerator.ReferenceBuildPath,
                "hover.apex.reference.01", 3.075f, 0.5f, 7f);
            ConfigureKnownBuild(
                V2ReferencePrototypeGenerator.GimbalBuildPath,
                "hover.apex.vector.01", 3.075f, 0.5f, 7f);
            ConfigureKnownBuild(
                V2ReferencePrototypeGenerator.SpringBuildPath,
                "hover.apex.terrain.01", 3.075f, 0.5f, 7f);
            ConfigureKnownBuild(
                V3SystemsIntegrationPrototypeGenerator.BuildPath,
                "hover.apex.systems.01", 8f, 1f, 80f);
            ConfigureKnownBuild(
                V3SystemsIntegrationPrototypeGenerator.BaselineBuildPath,
                "hover.hovercraft.baseline.01", 8f, 1f, 80f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Installed serialized Hovercraft V3 hover configurations.");
        }

        public static void Configure(
            CraftBuildDefinition build,
            string stableId,
            float targetHeight,
            float minimumClearance,
            float maximumOperationalRange)
        {
            if (build == null)
            {
                return;
            }

            var serialized = new SerializedObject(build);
            SerializedProperty hover = serialized.FindProperty("hoverConfiguration");
            float nearRange = Mathf.Min(
                maximumOperationalRange,
                Mathf.Max(targetHeight + 1f, targetHeight * 1.5f));
            float captureRange = Mathf.Min(
                maximumOperationalRange,
                Mathf.Max(nearRange, 28f));
            float fallbackRange = Mathf.Min(
                maximumOperationalRange,
                Mathf.Max(captureRange, 32f));
            float falloffMidpoint = Mathf.Lerp(
                nearRange,
                captureRange,
                0.5f);
            Set(hover, "stableId", stableId);
            Set(hover, "configurationVersion", 3);
            Set(hover, "targetHoverHeight", targetHeight);
            Set(hover, "minimumClearance", minimumClearance);
            Set(hover, "maximumOperationalRange", maximumOperationalRange);
            Set(hover, "probeRadius", 0.1f);
            Set(hover, "fallbackProbeRadius", 0.35f);
            Set(hover, "nearHoverRange", nearRange);
            Set(hover, "captureRange", captureRange);
            Set(hover, "fallbackRange", fallbackRange);
            Set(hover, "captureAuthorityByDistance", new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(nearRange, 1f),
                new Keyframe(falloffMidpoint, 0.35f),
                new Keyframe(captureRange, 0f)));
            Set(hover, "oneProbeAuthority", 0.2f);
            Set(hover, "twoProbeAuthority", 0.55f);
            Set(hover, "threeProbeAuthority", 0.8f);
            Set(hover, "fallbackOnlyAuthority", 0.15f);
            Set(hover, "normalConsensusAngleDegrees", 40f);
            Set(hover, "staleSurfaceTimeoutSeconds", 0.15f);
            Set(hover, "automaticHoverAuthorityLimit", 7f);
            Set(hover, "automaticRoofAuthorityLimit", 7f);
            Set(hover, "manualAuthorityLimit", 1f);
            Set(hover, "hoverStrength", 0.45f);
            Set(hover, "hoverDamping", 0.12f);
            Set(hover, "gravityCompensationEnabled", true);
            Set(hover, "gravityCompensationMultiplier", 1f);
            Set(hover, "compressionLimit", 0.3f);
            Set(hover, "extensionLimit", 0.12f);
            Set(hover, "angularDamping", 0.08f);
            Set(hover, "pitchSensitivity", 0.589f);
            Set(hover, "surfaceAlignStrength", 60f);
            Set(hover, "surfaceAlignDamping", 4f);
            Set(hover, "maximumSurfaceAlignAcceleration", 60f);
            Set(hover, "stiffenStartSpeed", 80f);
            Set(hover, "stiffenFullSpeed", 600f);
            Set(hover, "maximumSpeedStiffness", 3.5f);
            Set(hover, "surfaceCurvatureFeedForward", 1.1f);
            Set(hover, "surfaceTrackingResponse", 12f);
            Set(hover, "maximumSurfaceTrackingAcceleration", 340f);
            Set(hover, "roofCaptureStrength", 7f);
            Set(hover, "roofCaptureDamping", 5f);
            Set(hover, "maximumRoofCaptureAcceleration", 180f);
            Set(hover, "roofCaptureDeadband", 0.35f);
            Set(hover, "trackSurfaceMask", 1 << 8);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(build);
        }

        public static void InstallGeneratedConfigurationsFromCommandLine()
        {
            InstallGeneratedConfigurations();
        }

        private static void ConfigureKnownBuild(
            string path,
            string stableId,
            float targetHeight,
            float minimumClearance,
            float maximumOperationalRange)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(path);
            if (build == null)
            {
                throw new System.InvalidOperationException(
                    "Missing generated craft build: " + path);
            }
            Configure(build, stableId, targetHeight, minimumClearance,
                maximumOperationalRange);
        }

        private static void Set(SerializedProperty parent, string name, string value)
        {
            parent.FindPropertyRelative(name).stringValue = value;
        }

        private static void Set(SerializedProperty parent, string name, int value)
        {
            parent.FindPropertyRelative(name).intValue = value;
        }

        private static void Set(SerializedProperty parent, string name, float value)
        {
            parent.FindPropertyRelative(name).floatValue = value;
        }

        private static void Set(SerializedProperty parent, string name, bool value)
        {
            parent.FindPropertyRelative(name).boolValue = value;
        }

        private static void Set(
            SerializedProperty parent,
            string name,
            AnimationCurve value)
        {
            parent.FindPropertyRelative(name).animationCurveValue = value;
        }
    }
}
