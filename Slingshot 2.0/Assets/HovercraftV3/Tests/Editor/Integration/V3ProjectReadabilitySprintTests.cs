using System.Collections.Generic;
using System.IO;
using Lunarlight.Hovercraft.V3.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools.Utils;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3ProjectReadabilitySprintTests
    {
        private const string V2BuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/" +
            "Builds/ApexV3_V2Reference.asset";
        private const string SystemsBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/" +
            "ApexV3_SystemsIntegration.asset";

        [Test]
        public void LocalInput_UsesExplicitAuthorityStatesAndPageActions()
        {
            var inputHost = new GameObject("Local Pilot Input Source");
            var craft = new GameObject("Craft");
            try
            {
                V3PilotInputAdapter input =
                    inputHost.AddComponent<V3PilotInputAdapter>();
                V3ControllerPipeline pipeline =
                    craft.AddComponent<V3ControllerPipeline>();
                V3CraftMainframe mainframe =
                    craft.AddComponent<V3CraftMainframe>();

                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.AwaitingCraft));
                input.BindCraft(pipeline, mainframe);
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.Active));
                Assert.That(pipeline.InputSource, Is.SameAs(input));

                input.SetCursorCaptured(false);
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(
                        V3InputAuthorityState.SuspendedByCursor));
                Assert.That(input.enabled, Is.True);

                input.SetUiSuspended(true);
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.SuspendedByUI));
                input.SetUiSuspended(false);
                input.SetCursorCaptured(true);
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.Active));

                AssertBinding(
                    input.FindAction("Previous System Page"),
                    "<Keyboard>/o");
                AssertBinding(
                    input.FindAction("Next System Page"),
                    "<Keyboard>/p");

                input.UnbindCraft();
                Assert.That(
                    input.AuthorityState,
                    Is.EqualTo(V3InputAuthorityState.AwaitingCraft));
                Assert.That(pipeline.InputSource, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(inputHost);
                Object.DestroyImmediate(craft);
            }
        }

        [Test]
        public void CompleteCraft_HasReadableHierarchyAndNoLocalInput()
        {
            GameObject host =
                Assemble(SystemsBuildPath, out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                Assert.That(root.transform.Find("Chassis"), Is.Not.Null);
                Assert.That(root.transform.Find("Electronics"), Is.Not.Null);
                Assert.That(root.transform.Find("Cockpit"), Is.Not.Null);
                Assert.That(root.transform.Find("Hardware"), Is.Not.Null);
                Assert.That(root.transform.Find("Presentation"), Is.Not.Null);
                Assert.That(
                    root.transform.Find("Electronics/Mainframe"),
                    Is.Not.Null);

                Transform sensors =
                    root.transform.Find(
                        "Electronics/Chassis Sensors");
                Transform computers =
                    root.transform.Find(
                        "Cockpit/System Computers");
                Assert.That(sensors, Is.Not.Null);
                Assert.That(computers, Is.Not.Null);
                Assert.That(
                    root.GetComponentsInChildren<
                        V3DirectionalSensorDeviceRuntime>(true),
                    Has.Length.EqualTo(6));
                Assert.That(computers.childCount, Is.EqualTo(7));
                Assert.That(
                    root.GetComponent<V3PilotInputAdapter>(),
                    Is.Null);
                Assert.That(
                    root.GetComponentsInChildren<Rigidbody>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    root.GetComponentsInChildren<Collider>(true).Length,
                    Is.EqualTo(1));

                AssertPhysicalSocketPositionsPreserved(
                    assembler.Build.Chassis.Prefab,
                    root);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Spawner_AssemblesRebuildsReconnectsAndClears()
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    V2BuildPath);
            var host = new GameObject("Spawner Host");
            var assemblerHost = new GameObject("Assembler");
            assemblerHost.transform.SetParent(host.transform, false);
            try
            {
                V3CraftAssembler assembler =
                    assemblerHost.AddComponent<V3CraftAssembler>();
                SetObject(assembler, "build", build);
                var inputHost =
                    new GameObject("Local Pilot Input Source");
                inputHost.transform.SetParent(host.transform, false);
                V3PilotInputAdapter input =
                    inputHost.AddComponent<V3PilotInputAdapter>();
                V3CraftTestSpawner spawner =
                    host.AddComponent<V3CraftTestSpawner>();
                SetObject(spawner, "assembler", assembler);
                SetObject(spawner, "inputSource", input);
                SetObject(spawner, "selectedBuild", build);

                Assert.That(spawner.Assemble(), Is.True);
                GameObject first = spawner.CurrentCraft;
                Assert.That(first, Is.Not.Null);
                Assert.That(
                    first.GetComponent<V3PilotInputAdapter>(),
                    Is.Null);
                Assert.That(input.TargetCraft, Is.SameAs(first));

                Assert.That(spawner.Rebuild(), Is.True);
                Assert.That(spawner.CurrentCraft, Is.Not.SameAs(first));
                Assert.That(input.TargetCraft, Is.SameAs(
                    spawner.CurrentCraft));

                spawner.Clear();
                Assert.That(spawner.CurrentCraft, Is.Null);
                Assert.That(input.TargetCraft, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void EditorPreview_IsCompleteNonSimulatingAndCleanable()
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SystemsBuildPath);
            var host = new GameObject("Preview Assembler");
            try
            {
                V3CraftAssembler assembler =
                    host.AddComponent<V3CraftAssembler>();
                SetObject(assembler, "build", build);
                V3EditorPreviewRoot preview =
                    V3EditorPreviewAssembler.BuildPreview(assembler);

                Assert.That(preview, Is.Not.Null);
                Assert.That(preview.IsValid, Is.True);
                Assert.That(preview.TotalMassKg, Is.GreaterThan(0f));
                Assert.That(preview.SensorDeviceCount, Is.EqualTo(6));
                Assert.That(preview.ComputerRuntimeCount, Is.EqualTo(7));
                Assert.That(preview.SystemRuntimeCount, Is.EqualTo(7));
                Assert.That(
                    preview.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    preview.GetComponentsInChildren<
                        V3PilotInputAdapter>(true),
                    Is.Empty);
                Collider[] colliders =
                    preview.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Assert.That(colliders[i].enabled, Is.False);
                }

                V3EditorPreviewAssembler.ClearPreview(assembler);
                Assert.That(
                    assembler.transform.Find(
                        V3EditorPreviewAssembler.PreviewRootName),
                    Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AuthoritativeBuildSelection_DrivesPreviewAndPlayAssembly()
        {
            CraftBuildDefinition fallbackBuild =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    V2BuildPath);
            CraftBuildDefinition requestedBuild =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SystemsBuildPath);
            var host = new GameObject("Authoritative Build Host");
            var assemblerHost = new GameObject("Assembler");
            assemblerHost.transform.SetParent(host.transform, false);
            try
            {
                V3CraftAssembler assembler =
                    assemblerHost.AddComponent<V3CraftAssembler>();
                assembler.SetBuild(fallbackBuild);
                V3CraftTestSpawner spawner =
                    host.AddComponent<V3CraftTestSpawner>();
                SetObject(spawner, "assembler", assembler);
                spawner.SelectedBuild = fallbackBuild;

                V3EditorPreviewAssembler.SetAuthoritativeBuild(
                    assembler,
                    requestedBuild);

                Assert.That(
                    spawner.SelectedBuild,
                    Is.SameAs(requestedBuild));
                Assert.That(
                    assembler.Build,
                    Is.SameAs(requestedBuild));
                Assert.That(
                    V3EditorPreviewAssembler.ResolveBuild(assembler),
                    Is.SameAs(requestedBuild));

                V3EditorPreviewRoot preview =
                    V3EditorPreviewAssembler.BuildPreview(assembler);
                Assert.That(preview.Build, Is.SameAs(requestedBuild));
                V3EditorPreviewAssembler.ClearPreview(assembler);

                Assert.That(spawner.Assemble(), Is.True);
                Assert.That(
                    spawner.CurrentCraft
                        .GetComponent<V3CraftRuntime>()
                        .Build,
                    Is.SameAs(requestedBuild));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SystemsConsole_DiscoversTenPagesAndWraps()
        {
            GameObject host =
                Assemble(SystemsBuildPath, out V3CraftAssembler assembler);
            var consoleHost = new GameObject("Systems Console");
            var inputHost = new GameObject("Local Pilot Input Source");
            try
            {
                V3PilotInputAdapter input =
                    inputHost.AddComponent<V3PilotInputAdapter>();
                V3SystemsConsoleController console =
                    consoleHost.AddComponent<
                        V3SystemsConsoleController>();
                console.BindCraft(assembler.AssembledRoot, input);

                Assert.That(console.PageCount, Is.EqualTo(10));
                Assert.That(
                    console.Registry.Providers[0].Id,
                    Is.EqualTo(V3SystemsPageId.Mainframe));
                Assert.That(
                    console.Registry.Providers[9].Id,
                    Is.EqualTo(V3SystemsPageId.Sensors));
                console.PreviousPage();
                Assert.That(console.CurrentPageIndex, Is.EqualTo(9));
                console.NextPage();
                Assert.That(console.CurrentPageIndex, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(consoleHost);
                Object.DestroyImmediate(inputHost);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PresentationPanels_CanBeCollapsedAndRestored()
        {
            var host = new GameObject("Presentation Panels");
            try
            {
                V3SystemsConsoleController console =
                    host.AddComponent<V3SystemsConsoleController>();
                V3FreeDriveHud hud = host.AddComponent<V3FreeDriveHud>();

                Assert.That(console.IsMinimized, Is.False);
                console.ToggleMinimized();
                Assert.That(console.IsMinimized, Is.True);
                console.ToggleMinimized();
                Assert.That(console.IsMinimized, Is.False);

                Assert.That(console.ShowConsole, Is.False);
                Assert.That(hud.ShowControls, Is.False);
                hud.ToggleControls();
                Assert.That(hud.ShowControls, Is.True);
                hud.ToggleControls();
                Assert.That(hud.ShowControls, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MigratedPathsAndForceAuthority_AreCanonical()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    V2BuildPath),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    SystemsBuildPath),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    "Assets/HovercraftV3/Scenes/Development/" +
                    "V3_CraftLab.unity"),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    "Assets/HovercraftV3/Scenes/Development/" +
                    "V3_HandlingTrack.unity"),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.IsValidFolder(
                    "Assets/HovercraftV3/Runtime/Runtime"),
                Is.False);
            Assert.That(
                AssetDatabase.IsValidFolder(
                    "Assets/HovercraftV3/Runtime/Data"),
                Is.False);

            string[] files = Directory.GetFiles(
                "Assets/HovercraftV3/Runtime",
                "*.cs",
                SearchOption.AllDirectories);
            var forceFiles = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                string source = File.ReadAllText(files[i]);
                if (source.Contains(".AddForce(") ||
                    source.Contains(".AddTorque(") ||
                    source.Contains(".AddForceAtPosition("))
                {
                    forceFiles.Add(Path.GetFileName(files[i]));
                }
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "RuntimeThrusterInstance.cs",
                    "RuntimeAerodynamicFinInstance.cs",
                    "V3CraftAerodynamicsRuntime.cs",
                    "V3WorldEnvironmentProvider.cs"
                },
                forceFiles);
        }

        private static GameObject Assemble(
            string buildPath,
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    buildPath);
            Assert.That(build, Is.Not.Null, buildPath);
            var host = new GameObject("V3 Test Assembler");
            assembler = host.AddComponent<V3CraftAssembler>();
            SetObject(assembler, "build", build);
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }

        private static void AssertBinding(
            InputAction action,
            string path)
        {
            Assert.That(action, Is.Not.Null);
            bool found = false;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].path == path)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, path);
        }

        private static void AssertPhysicalSocketPositionsPreserved(
            GameObject prefab,
            GameObject runtime)
        {
            V3Socket[] authored =
                prefab.GetComponentsInChildren<V3Socket>(true);
            V3Socket[] assembled =
                runtime.GetComponentsInChildren<V3Socket>(true);
            var expected =
                new Dictionary<string, Vector3>(
                    System.StringComparer.Ordinal);
            for (int i = 0; i < authored.Length; i++)
            {
                expected[authored[i].SocketId] =
                    prefab.transform.InverseTransformPoint(
                        authored[i].MountTransform.position);
            }

            Assert.That(assembled.Length, Is.EqualTo(authored.Length));
            for (int i = 0; i < assembled.Length; i++)
            {
                Vector3 actual =
                    runtime.transform.InverseTransformPoint(
                        assembled[i].MountTransform.position);
                Assert.That(
                    actual,
                    Is.EqualTo(expected[assembled[i].SocketId])
                        .Using(Vector3ComparerWithEqualsOperator.Instance),
                    assembled[i].SocketId);
            }
        }

        [Test]
        public void StableIdAudit_FindsNoDuplicateAssetsOrInvalidWorldRoots()
        {
            Lunarlight.Hovercraft.V3.Editor.V3StableIdAuditResult result =
                Lunarlight.Hovercraft.V3.Editor.V3StableIdAudit.Scan();
            Assert.That(result.IsValid, Is.True,
                Lunarlight.Hovercraft.V3.Editor.V3StableIdAudit.Format(result));
        }

        private static void SetObject(
            Object target,
            string propertyName,
            Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
