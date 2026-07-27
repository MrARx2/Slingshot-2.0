using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3CraftKernelServicesTests
    {
        private const string ReferenceBuildPath =
            "Assets/HovercraftV3/Prototype/Builds/" +
            "ApexV3_V2Reference.asset";
        private const string GimbalBuildPath =
            "Assets/HovercraftV3/Prototype/Builds/" +
            "ApexV3_MainGimbalDemo.asset";
        private const string SpringBuildPath =
            "Assets/HovercraftV3/Prototype/Builds/" +
            "ApexV3_HoverSpringDemo.asset";

        [Test]
        public void ReferenceCraft_UsesAuditableMassAndValidationServices()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                true,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3CraftMassCalculator mass =
                    root.GetComponent<V3CraftMassCalculator>();
                V3RuntimeBuildValidator validator =
                    root.GetComponent<V3RuntimeBuildValidator>();
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                Assert.That(mass, Is.Not.Null);
                Assert.That(mass.IsInitialized, Is.True);
                Assert.That(mass.LastError, Is.Empty);
                Assert.That(mass.Result, Is.Not.Null);
                Assert.That(
                    mass.Result.ChassisBaseMassKg,
                    Is.EqualTo(8430f).Within(0.001f));
                Assert.That(
                    mass.Result.InstalledPartsMassKg,
                    Is.EqualTo(2570f).Within(0.001f));
                Assert.That(
                    mass.Result.TotalMassKg,
                    Is.EqualTo(11000f).Within(0.001f));
                Assert.That(
                    mass.Result.Contributions,
                    Has.Count.EqualTo(16));
                Assert.That(
                    mass.Result.FinalCenterOfMass.y,
                    Is.EqualTo(-0.5f).Within(0.001f));
                Assert.That(
                    body.mass,
                    Is.EqualTo(mass.Result.TotalMassKg).Within(0.001f));
                Assert.That(
                    body.centerOfMass,
                    Is.EqualTo(mass.Result.FinalCenterOfMass));

                V3MassContribution rearMain =
                    mass.Result.Contributions.Single(
                        contribution =>
                            contribution.SocketId ==
                            "Propulsion.Rear.Center");
                Assert.That(
                    rearMain.StablePartId,
                    Is.EqualTo("Thruster.Main.V2Reference"));
                Assert.That(rearMain.MassKg, Is.EqualTo(300f).Within(0.001f));

                Assert.That(validator, Is.Not.Null);
                Assert.That(validator.IsValid, Is.True);
                Assert.That(validator.AuthoringReport.IsValid, Is.True);
                Assert.That(validator.RuntimeReport.IsValid, Is.True);
                Assert.That(validator.RuntimeReport.Issues, Is.Empty);

                V3CraftTelemetrySnapshot snapshot =
                    telemetry.CaptureSnapshot();
                Assert.That(snapshot.IsRuntimeBuildValid, Is.True);
                Assert.That(snapshot.RuntimeBuildIssueCount, Is.Zero);
                Assert.That(
                    snapshot.ChassisBaseMassKg,
                    Is.EqualTo(8430f).Within(0.001f));
                Assert.That(
                    snapshot.InstalledPartsMassKg,
                    Is.EqualTo(2570f).Within(0.001f));
                Assert.That(
                    snapshot.ParityCenterOfMassCalibration,
                    Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(GimbalBuildPath, 11120f, 2690f, 120f,
            "Connector.Gimbal.Main.V2Reference")]
        [TestCase(SpringBuildPath, 11035f, 2605f, 35f,
            "Connector.Spring.Hover.V2Reference")]
        public void ConnectorVariations_IncludeConnectorMassExactlyOnce(
            string buildPath,
            float expectedTotalMass,
            float expectedInstalledMass,
            float connectorMass,
            string connectorStableId)
        {
            GameObject host = BuildCraft(
                buildPath,
                true,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                V3CraftMassCalculator mass =
                    root.GetComponent<V3CraftMassCalculator>();
                V3RuntimeBuildValidator validator =
                    root.GetComponent<V3RuntimeBuildValidator>();

                Assert.That(
                    mass.Result.TotalMassKg,
                    Is.EqualTo(expectedTotalMass).Within(0.001f));
                Assert.That(
                    mass.Result.InstalledPartsMassKg,
                    Is.EqualTo(expectedInstalledMass).Within(0.001f));
                Assert.That(
                    mass.Result.Contributions,
                    Has.Count.EqualTo(17));
                Assert.That(
                    mass.Result.Contributions.Count(
                        contribution =>
                            contribution.StablePartId ==
                            connectorStableId),
                    Is.EqualTo(1));
                Assert.That(
                    mass.Result.Contributions.Single(
                            contribution =>
                                contribution.StablePartId ==
                                connectorStableId)
                        .MassKg,
                    Is.EqualTo(connectorMass).Within(0.001f));
                Assert.That(validator.IsValid, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RuntimeValidator_DetectsAndRecoversFromMassCorruption()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                true,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                Rigidbody body = root.GetComponent<Rigidbody>();
                V3CraftMassCalculator mass =
                    root.GetComponent<V3CraftMassCalculator>();
                V3RuntimeBuildValidator validator =
                    root.GetComponent<V3RuntimeBuildValidator>();
                V3CraftTelemetryHub telemetry =
                    root.GetComponent<V3CraftTelemetryHub>();

                body.mass += 10f;
                Assert.That(validator.RefreshValidation(), Is.False);
                Assert.That(
                    validator.RuntimeReport.Issues.Any(
                        issue =>
                            issue.Code == "RUNTIME_MASS_MISMATCH"),
                    Is.True,
                    JoinIssues(validator.RuntimeReport));

                V3CraftTelemetrySnapshot invalid =
                    telemetry.CaptureSnapshot();
                Assert.That(invalid.IsRuntimeBuildValid, Is.False);
                Assert.That(invalid.RuntimeBuildIssueCount, Is.EqualTo(1));

                Assert.That(mass.RecalculateAndApply(), Is.True);
                Assert.That(validator.RefreshValidation(), Is.True);
                V3CraftTelemetrySnapshot recovered =
                    telemetry.CaptureSnapshot();
                Assert.That(recovered.IsRuntimeBuildValid, Is.True);
                Assert.That(recovered.RuntimeBuildIssueCount, Is.Zero);
                Assert.That(body.mass, Is.EqualTo(11000f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ControllerFreeCraft_StillOwnsMassAndValidationKernel()
        {
            GameObject host = BuildCraft(
                ReferenceBuildPath,
                false,
                out V3CraftAssembler assembler);
            try
            {
                GameObject root = assembler.AssembledRoot;
                Assert.That(
                    root.GetComponent<V3ControllerPipeline>(),
                    Is.Null);
                Assert.That(
                    root.GetComponent<V3CraftMassCalculator>(),
                    Is.Not.Null);
                Assert.That(
                    root.GetComponent<V3RuntimeBuildValidator>(),
                    Is.Not.Null);
                Assert.That(
                    root.GetComponent<V3RuntimeBuildValidator>().IsValid,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static GameObject BuildCraft(
            string buildPath,
            bool installReferenceControllers,
            out V3CraftAssembler assembler)
        {
            CraftBuildDefinition build =
                AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(
                    buildPath);
            Assert.That(build, Is.Not.Null, buildPath);
            var host = new GameObject(
                "V3 Gate 12 Kernel Services Test Host");
            assembler = host.AddComponent<V3CraftAssembler>();
            var serialized = new SerializedObject(assembler);
            serialized.FindProperty("build").objectReferenceValue = build;
            serialized.FindProperty("installReferenceControllers")
                .boolValue = installReferenceControllers;
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
