using System.Linq;
using Lunarlight.Hovercraft.V3.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3WorldClockAndHoverConfigurationTests
    {
        private const string SystemsBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset";
        private const string ReferenceBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "V2Parity/Builds/ApexV3_V2Reference.asset";

        [Test]
        public void SerializedHoverConfiguration_PersistsAndReassemblesExactly()
        {
            AssetDatabase.ImportAsset(SystemsBuildPath,
                ImportAssetOptions.ForceUpdate);
            CraftBuildDefinition systems =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SystemsBuildPath);
            CraftBuildDefinition reference =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    ReferenceBuildPath);
            Assert.That(systems, Is.Not.Null);
            Assert.That(reference, Is.Not.Null);
            Assert.That(systems.HoverConfiguration.StableId,
                Is.EqualTo("hover.apex.systems.01"));
            Assert.That(systems.HoverConfiguration.TargetHoverHeight,
                Is.EqualTo(8f).Within(0.0001f));
            Assert.That(reference.HoverConfiguration.TargetHoverHeight,
                Is.EqualTo(3.075f).Within(0.0001f));
            Assert.That(systems.HoverConfiguration.TargetHoverHeight,
                Is.Not.EqualTo(reference.HoverConfiguration.TargetHoverHeight));

            var host = new GameObject("Serialized hover assembly test");
            try
            {
                V3CraftAssembler assembler = host.AddComponent<V3CraftAssembler>();
                var serialized = new SerializedObject(assembler);
                serialized.FindProperty("build").objectReferenceValue = systems;
                serialized.FindProperty("assembleOnStart").boolValue = false;
                serialized.FindProperty("installReferenceControllers").boolValue =
                    true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(assembler.Rebuild(), Is.True);
                AssertAssembly(assembler, systems);
                GameObject firstRoot = assembler.AssembledRoot;

                Assert.That(assembler.Rebuild(), Is.True);
                Assert.That(assembler.AssembledRoot, Is.Not.SameAs(firstRoot));
                AssertAssembly(assembler, systems);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CaptureEnvelope_DecaysAndFallbackCannotReachFullAuthority()
        {
            var configuration = new V3HoverConfiguration();
            Assert.That(configuration.EvaluateCaptureAuthority(0f),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(configuration.EvaluateCaptureAuthority(20f),
                Is.LessThan(1f));
            Assert.That(configuration.EvaluateCaptureAuthority(28.01f),
                Is.Zero.Within(0.0001f));
            Assert.That(
                configuration.EvaluateProbeConsensusAuthority(1, 0),
                Is.EqualTo(configuration.FallbackOnlyAuthority)
                    .Within(0.0001f));
            Assert.That(configuration.FallbackOnlyAuthority,
                Is.LessThan(1f));
        }

        private static void AssertAssembly(
            V3CraftAssembler assembler,
            CraftBuildDefinition build)
        {
            GameObject root = assembler.AssembledRoot;
            V3HoverController controller = root.GetComponent<V3HoverController>();
            V3CraftTelemetryHub telemetry =
                root.GetComponent<V3CraftTelemetryHub>();
            V3CraftRuntime runtime = root.GetComponent<V3CraftRuntime>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Configuration,
                Is.SameAs(build.HoverConfiguration));
            Assert.That(controller.TargetHeight,
                Is.EqualTo(build.HoverConfiguration.TargetHoverHeight));
            Assert.That(controller.MinimumClearance,
                Is.EqualTo(build.HoverConfiguration.MinimumClearance));
            Assert.That(controller.ProbeRange,
                Is.EqualTo(build.HoverConfiguration.MaximumOperationalRange));
            Assert.That(controller.GravityCompensationEnabled,
                Is.EqualTo(build.HoverConfiguration.GravityCompensationEnabled));

            RuntimeSpringMountInstance[] springs = runtime.InstalledParts
                .OfType<RuntimeSpringMountInstance>().ToArray();
            Assert.That(springs, Is.Not.Empty);
            Assert.That(springs.All(s => s.EffectiveCompressionLimitM <=
                build.HoverConfiguration.CompressionLimit + 0.0001f), Is.True);
            Assert.That(springs.All(s => s.EffectiveExtensionLimitM <=
                build.HoverConfiguration.ExtensionLimit + 0.0001f), Is.True);

            V3CraftTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();
            Assert.That(snapshot.HoverConfigurationId,
                Is.EqualTo(build.HoverConfiguration.StableId));
            Assert.That(snapshot.TargetHoverHeight,
                Is.EqualTo(build.HoverConfiguration.TargetHoverHeight));
            Assert.That(snapshot.MinimumHoverClearance,
                Is.EqualTo(build.HoverConfiguration.MinimumClearance));
            Assert.That(snapshot.MaximumHoverRange,
                Is.EqualTo(build.HoverConfiguration.MaximumOperationalRange));

            var context = new V3DiagnosticContext();
            Assert.That(context.Bind(runtime, null, null), Is.True);
            V3DiagnosticCraftSnapshot diagnostic =
                V3CraftDiagnosticSnapshotExporter.Capture(context);
            Assert.That(diagnostic.hoverConfigurationId,
                Is.EqualTo(build.HoverConfiguration.StableId));
            Assert.That(diagnostic.targetHoverHeight,
                Is.EqualTo(build.HoverConfiguration.TargetHoverHeight));
        }
    }
}
