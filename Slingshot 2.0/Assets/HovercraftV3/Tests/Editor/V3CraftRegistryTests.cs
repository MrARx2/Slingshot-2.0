using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3CraftRegistryTests
    {
        private const string ReferenceBuildPath =
            "Assets/HovercraftV3/Prototype/Builds/" +
            "ApexV3_V2Reference.asset";
        private const string GimbalBuildPath =
            "Assets/HovercraftV3/Prototype/Builds/" +
            "ApexV3_MainGimbalDemo.asset";

        [Test]
        public void ReferenceCraft_IndexesSocketsPartsAndCapabilities()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3SocketRegistry sockets =
                    root.GetComponent<V3SocketRegistry>();
                V3PartRegistry parts =
                    root.GetComponent<V3PartRegistry>();
                V3CapabilityRegistry capabilities =
                    root.GetComponent<V3CapabilityRegistry>();
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                Assert.That(sockets, Is.Not.Null);
                Assert.That(sockets.IsValid, Is.True);
                Assert.That(sockets.Count, Is.EqualTo(16));
                Assert.That(
                    sockets.TryGetSocket(
                        "Propulsion.Rear.Center",
                        out V3Socket rear),
                    Is.True);
                Assert.That(
                    rear.Family,
                    Is.EqualTo(SocketFamily.HeavyPropulsion));
                Assert.That(
                    sockets.TryGetPairedSocket(
                        "Hover.Front.Left.Bottom",
                        out V3Socket pairedHover),
                    Is.True);
                Assert.That(
                    pairedHover.SocketId,
                    Is.EqualTo("Hover.Front.Right.Bottom"));
                Assert.That(
                    sockets.GetConfigurationGroup("Unused"),
                    Is.Empty);

                Assert.That(parts, Is.Not.Null);
                Assert.That(parts.Count, Is.EqualTo(16));
                Assert.That(
                    parts.GetPartsAtSocket("Propulsion.Rear.Center"),
                    Has.Count.EqualTo(1));
                Assert.That(
                    parts.TryGetConnector(
                        "Propulsion.Rear.Center",
                        out _),
                    Is.False);
                Assert.That(
                    parts.TryGetEndpoint(
                        "Propulsion.Rear.Center",
                        out RuntimePartInstance main),
                    Is.True);
                Assert.That(
                    main.Definition.StableId,
                    Is.EqualTo("Thruster.Main.V2Reference"));
                Assert.That(
                    parts.GetPartsByStableId(
                        "Thruster.Hover.V2Reference"),
                    Has.Count.EqualTo(4));
                Assert.That(
                    parts.GetPartsByStableId(
                        "Thruster.Strafe.V2Reference"),
                    Has.Count.EqualTo(4));

                PartCapability allReferenceCapabilities =
                    PartCapability.DriveControl |
                    PartCapability.HoverControl |
                    PartCapability.Stabilization |
                    PartCapability.VectoringControl |
                    PartCapability.ThermalTelemetry |
                    PartCapability.PowerDistribution;
                Assert.That(capabilities, Is.Not.Null);
                Assert.That(
                    capabilities.ProvidedCapabilities,
                    Is.EqualTo(allReferenceCapabilities));
                Assert.That(
                    capabilities.RequiredCapabilities,
                    Is.EqualTo(PartCapability.None));
                Assert.That(
                    capabilities.AreRequirementsSatisfied,
                    Is.True);
                Assert.That(
                    capabilities.HasAll(
                        PartCapability.DriveControl |
                        PartCapability.PowerDistribution),
                    Is.True);
                Assert.That(
                    capabilities.GetProviders(
                        PartCapability.DriveControl),
                    Has.Count.EqualTo(1));
                Assert.That(
                    capabilities.GetProviders(
                        PartCapability.PowerDistribution),
                    Has.Count.EqualTo(1));
                Assert.That(
                    capabilities.GetProviders(
                        PartCapability.ThermalTelemetry),
                    Has.Count.EqualTo(15));

                V3CraftTelemetrySnapshot snapshot =
                    telemetry.CaptureSnapshot();
                Assert.That(snapshot.SocketCount, Is.EqualTo(16));
                Assert.That(
                    snapshot.ProvidedCapabilities,
                    Is.EqualTo(allReferenceCapabilities));
                Assert.That(
                    snapshot.AreCapabilityRequirementsSatisfied,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GimbalCraft_IndexesConnectorAndEndpointAtSameSocket()
        {
            GameObject host = BuildCraft(
                GimbalBuildPath,
                out V3CraftAssembler assembler);
            try
            {
                V3PartRegistry parts =
                    assembler.AssembledRoot.GetComponent<V3PartRegistry>();

                Assert.That(parts.Count, Is.EqualTo(17));
                Assert.That(
                    parts.GetPartsAtSocket("Propulsion.Rear.Center"),
                    Has.Count.EqualTo(2));
                Assert.That(
                    parts.TryGetConnector(
                        "Propulsion.Rear.Center",
                        out RuntimePartInstance connector),
                    Is.True);
                Assert.That(
                    connector.Definition.StableId,
                    Is.EqualTo("Connector.Gimbal.Main.V2Reference"));
                Assert.That(
                    parts.TryGetEndpoint<RuntimeThrusterInstance>(
                        "Propulsion.Rear.Center",
                        out RuntimeThrusterInstance endpoint),
                    Is.True);
                Assert.That(
                    endpoint.Definition.StableId,
                    Is.EqualTo("Thruster.Main.V2Reference"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BuildValidation_AllowsRequirementProvidedByAnotherPart()
        {
            CraftBuildDefinition build = null;
            ThrusterDefinition main = null;
            try
            {
                CraftBuildDefinition source =
                    AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                        ReferenceBuildPath);
                build = CreateBuildClone(source);
                main = Object.Instantiate(
                    source.FindInstallation("Propulsion.Rear.Center")
                        .Endpoint as ThrusterDefinition);
                SetCapability(
                    main,
                    "requiredCapabilities",
                    PartCapability.DriveControl);
                build.FindInstallation("Propulsion.Rear.Center")
                    .SetDirect(main);

                BuildValidationReport report =
                    CraftBuildValidator.Validate(build);

                Assert.That(
                    report.IsValid,
                    Is.True,
                    JoinIssues(report));
            }
            finally
            {
                if (build != null)
                {
                    Object.DestroyImmediate(build);
                }

                if (main != null)
                {
                    Object.DestroyImmediate(main);
                }
            }
        }

        [Test]
        public void BuildValidation_RejectsMissingGlobalCapability()
        {
            CraftBuildDefinition build = null;
            CockpitDefinition cockpit = null;
            try
            {
                CraftBuildDefinition source =
                    AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                        ReferenceBuildPath);
                build = CreateBuildClone(source);
                cockpit = Object.Instantiate(
                    source.FindInstallation("Cockpit.Main")
                        .Endpoint as CockpitDefinition);
                SetCapability(
                    cockpit,
                    "providedCapabilities",
                    PartCapability.None);
                SetCapability(
                    cockpit,
                    "requiredCapabilities",
                    PartCapability.DriveControl);
                build.FindInstallation("Cockpit.Main").SetDirect(cockpit);

                BuildValidationReport report =
                    CraftBuildValidator.Validate(build);

                Assert.That(report.IsValid, Is.False);
                Assert.That(
                    report.Issues.Any(
                        issue =>
                            issue.Code == "CAPABILITY_MISSING" &&
                            issue.Message.Contains("DriveControl")),
                    Is.True,
                    JoinIssues(report));
            }
            finally
            {
                if (build != null)
                {
                    Object.DestroyImmediate(build);
                }

                if (cockpit != null)
                {
                    Object.DestroyImmediate(cockpit);
                }
            }
        }

        private static CraftBuildDefinition CreateBuildClone(
            CraftBuildDefinition source)
        {
            Assert.That(source, Is.Not.Null);
            CraftBuildDefinition build =
                ScriptableObject.CreateInstance<CraftBuildDefinition>();
            build.CopySelectionsFrom(source);
            var serialized = new SerializedObject(build);
            serialized.FindProperty("catalog").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return build;
        }

        private static void SetCapability(
            PartDefinition definition,
            string propertyName,
            PartCapability capability)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty(propertyName).intValue =
                (int)capability;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject BuildCraft(
            string buildPath,
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    buildPath);
            Assert.That(build, Is.Not.Null, buildPath);
            var host = new GameObject("V3 Gate 11 Registry Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(assembler.Rebuild(), Is.True);
            return host;
        }

        private static string JoinIssues(BuildValidationReport report)
        {
            return string.Join(
                "\n",
                report.Issues.Select(
                    issue => $"{issue.Code}: {issue.Message}"));
        }
    }
}
