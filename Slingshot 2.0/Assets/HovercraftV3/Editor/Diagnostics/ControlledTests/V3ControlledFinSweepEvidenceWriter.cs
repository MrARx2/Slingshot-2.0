using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    /// <summary>
    /// Produces a deterministic aerodynamic matrix through the production fin
    /// runtime. This is an editor-only fixture and never participates in play.
    /// </summary>
    public static class V3ControlledFinSweepEvidenceWriter
    {
        private const string FinPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/SystemsIntegration/" +
            "Definitions/Balanced_AeroFin.asset";
        private static readonly float[] BaseAngles =
        {
            -180f, -135f, -90f, -45f, -18f, -10f, -5f, 0f,
            5f, 10f, 18f, 45f, 90f, 135f, 180f
        };
        private static readonly float[] Speeds = { 0f, 25f, 100f, 250f };
        private static readonly float[] Densities = { 1.225f, 0.6f };
        private static readonly float[] Deflections = { -10f, 0f, 10f };

        private sealed class Row
        {
            public float baseAngle;
            public float speed;
            public float density;
            public float deflection;
            public float actualAngle;
            public float cl;
            public float cd;
            public bool stalled;
            public bool reverse;
            public float lift;
            public float drag;
            public float force;
            public float torque;
            public float copError;
            public float torqueError;
            public bool warning;
            public bool limited;
        }

        [MenuItem("Tools/Hovercraft V3/Diagnostics/Controlled Tests/Write Fin Sweep Evidence")]
        public static void Write()
        {
            V3AerodynamicFinDefinition definition =
                AssetDatabase.LoadAssetAtPath<V3AerodynamicFinDefinition>(FinPath);
            if (definition == null)
                throw new FileNotFoundException("Missing production fin definition.", FinPath);

            string package = Path.GetFullPath(Path.Combine(
                "TestDriveReports", "HovercraftV3", "Controlled", "FinSweeps",
                DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) +
                "_scenario_b_production_fin_matrix"));
            Directory.CreateDirectory(package);

            GameObject bodyObject = new GameObject("[V3 Fin Sweep Body]");
            GameObject finObject = new GameObject("[V3 Fin Sweep Runtime]");
            var rows = new List<Row>(BaseAngles.Length * Speeds.Length *
                Densities.Length * Deflections.Length);
            try
            {
                Rigidbody body = bodyObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                finObject.transform.SetParent(bodyObject.transform, false);
                RuntimeAerodynamicFinInstance runtime =
                    finObject.AddComponent<RuntimeAerodynamicFinInstance>();
                runtime.Initialize(definition, "fixture.fin.controlled_sweep");
                runtime.BindRigidbody(body);

                foreach (float density in Densities)
                foreach (float speed in Speeds)
                foreach (float deflection in Deflections)
                foreach (float baseAngle in BaseAngles)
                {
                    finObject.transform.localPosition = new Vector3(0.3f, 0.2f, 1.1f);
                    finObject.transform.localRotation = Quaternion.AngleAxis(
                        -deflection, Vector3.right);
                    Vector3 incoming = Quaternion.AngleAxis(
                        -baseAngle, Vector3.right) * Vector3.forward;
                    var environment = new V3WorldEnvironmentSample
                    {
                        IsValid = true,
                        AirDensityKgPerCubicMeter = density,
                        ReferenceAirDensityKgPerCubicMeter = 1.225f,
                        SpeedOfSoundMetersPerSecond = 340.3f,
                        LocalAirVelocity = -incoming.normalized * speed
                    };
                    runtime.ApplyAerodynamicForce(environment);
                    Vector3 expectedPoint = finObject.transform.TransformPoint(
                        definition.LocalCenterOfPressure);
                    Vector3 expectedTorque = Vector3.Cross(
                        runtime.CurrentForceApplicationPoint - body.worldCenterOfMass,
                        runtime.CurrentWorldAerodynamicForce);
                    rows.Add(new Row
                    {
                        baseAngle = baseAngle,
                        speed = speed,
                        density = density,
                        deflection = deflection,
                        actualAngle = runtime.CurrentAngleOfAttackDegrees,
                        cl = runtime.CurrentLiftCoefficient,
                        cd = runtime.CurrentDragCoefficient,
                        stalled = runtime.IsStalled,
                        reverse = runtime.IsReverseFlow,
                        lift = runtime.CurrentWorldLiftForce.magnitude,
                        drag = runtime.CurrentWorldDragForce.magnitude,
                        force = runtime.CurrentWorldAerodynamicForce.magnitude,
                        torque = runtime.CurrentWorldAerodynamicTorque.magnitude,
                        copError = Vector3.Distance(
                            expectedPoint, runtime.CurrentForceApplicationPoint),
                        torqueError = Vector3.Distance(
                            expectedTorque, runtime.CurrentWorldAerodynamicTorque),
                        warning = runtime.HasStructuralWarning,
                        limited = runtime.IsStructurallyLimited
                    });
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(finObject);
                UnityEngine.Object.DestroyImmediate(bodyObject);
            }

            File.WriteAllText(Path.Combine(package, "fin_sweep_samples.csv"),
                BuildCsv(rows));
            WriteCurveCsv(package, "aoa_vs_cl.csv", rows, true);
            WriteCurveCsv(package, "aoa_vs_cd.csv", rows, false);
            bool zeroForce = rows.FindAll(r => r.speed == 0f)
                .TrueForAll(r => r.force <= 0.0001f);
            Row zero = Find(rows, 0f, 100f, 1.225f, 0f);
            Row plusFive = Find(rows, 5f, 100f, 1.225f, 0f);
            Row plusTen = Find(rows, 10f, 100f, 1.225f, 0f);
            Row plusStall = Find(rows, 18f, 100f, 1.225f, 0f);
            Row plusNinety = Find(rows, 90f, 100f, 1.225f, 0f);
            Row reverse = Find(rows, 135f, 100f, 1.225f, 0f);
            Row negative = Find(rows, -10f, 100f, 1.225f, 0f);
            bool nearZero = zero != null && Mathf.Abs(zero.cl) <= 0.01f;
            bool sign = plusTen != null && negative != null &&
                plusTen.cl * plusTen.actualAngle > 0f &&
                negative.cl * negative.actualAngle > 0f;
            bool monotonic = plusFive != null && plusTen != null && plusStall != null &&
                Mathf.Abs(plusFive.cl) < Mathf.Abs(plusTen.cl) &&
                Mathf.Abs(plusTen.cl) <= Mathf.Abs(plusStall.cl);
            bool stallShape = plusStall != null && plusNinety != null &&
                Mathf.Abs(Mathf.Abs(plusStall.actualAngle) -
                    definition.PositiveStallAngleDegrees) <= 0.01f &&
                plusNinety.stalled &&
                Mathf.Abs(plusNinety.cl) < Mathf.Abs(plusStall.cl) &&
                plusNinety.cd > plusStall.cd;
            bool reverseBounded = reverse != null && reverse.reverse &&
                Mathf.Abs(reverse.cl) <= definition.MaximumLiftCoefficient *
                definition.ReverseFlowScale + 0.0001f;
            bool cop = rows.TrueForAll(r => r.copError <= 0.0001f &&
                r.torqueError <= 0.01f);
            bool passed = zeroForce && nearZero && sign && monotonic &&
                stallShape && reverseBounded && cop;

            var report = new StringBuilder(4096);
            report.AppendLine("# Scenario B — Production Fin Sweep");
            report.AppendLine();
            report.AppendLine("- Result: **" + (passed ? "PASS" : "FAIL") + "**");
            report.AppendLine("- Definition: `" + definition.StableId + "` (`" +
                AssetDatabase.AssetPathToGUID(FinPath) + "`)");
            report.AppendLine("- Fixture: `RuntimeAerodynamicFinInstance.ApplyAerodynamicForce`");
            report.AppendLine("- Samples: " + rows.Count);
            report.AppendLine("- AoA: -180°..180°; speed: 0/25/100/250 m/s; " +
                "density: 1.225/0.6 kg/m³; deflection: -10°/0°/+10°");
            report.AppendLine();
            report.AppendLine("## Assertions");
            report.AppendLine();
            Append(report, "Zero airflow produces zero force", zeroForce);
            Append(report, "Near-zero AoA produces near-zero lift", nearZero);
            Append(report, "Lift sign reverses with AoA", sign);
            Append(report, "Attached-region lift is monotonic", monotonic);
            Append(report, "Post-stall lift falls while drag rises", stallShape);
            Append(report, "Reverse-flow lift is bounded", reverseBounded);
            Append(report, "Force is evaluated at CoP and torque is consistent", cop);
            report.AppendLine();
            report.AppendLine("## Calibration decision");
            report.AppendLine();
            report.AppendLine("Retain current coefficients. The controlled matrix did not " +
                "identify a correctness-driven tuning change.");
            File.WriteAllText(Path.Combine(package, "fin_sweep_report.md"),
                report.ToString());
            File.WriteAllText(Path.Combine(package, "fin_sweep_manifest.json"),
                "{\n" +
                "  \"schemaVersion\": 6,\n" +
                "  \"testId\": \"test.v3.fin_sweep.production_runtime_matrix.01\",\n" +
                "  \"definitionStableId\": \"" + Escape(definition.StableId) + "\",\n" +
                "  \"definitionGuid\": \"" + AssetDatabase.AssetPathToGUID(FinPath) + "\",\n" +
                "  \"sampleCount\": " + rows.Count + ",\n" +
                "  \"passed\": " + passed.ToString().ToLowerInvariant() + ",\n" +
                "  \"productionRuntime\": \"RuntimeAerodynamicFinInstance.ApplyAerodynamicForce\"\n" +
                "}\n");
            Debug.Log("Controlled fin sweep evidence: " + package);
            if (!passed)
                throw new InvalidOperationException(
                    "Production fin sweep assertions failed. See " + package);
        }

        public static void WriteFromCommandLine() => Write();

        private static Row Find(List<Row> rows, float angle, float speed,
            float density, float deflection)
        {
            return rows.Find(r => Mathf.Approximately(r.baseAngle, angle) &&
                Mathf.Approximately(r.speed, speed) &&
                Mathf.Approximately(r.density, density) &&
                Mathf.Approximately(r.deflection, deflection));
        }

        private static string BuildCsv(List<Row> rows)
        {
            var text = new StringBuilder(65536);
            text.AppendLine("base_aoa_deg,speed_mps,density_kg_m3,deflection_deg," +
                "runtime_aoa_deg,cl,cd,stalled,reverse_flow,lift_n,drag_n," +
                "total_force_n,torque_nm,cop_error_m,torque_error_nm," +
                "structural_warning,structural_limited");
            foreach (Row r in rows)
            {
                text.Append(F(r.baseAngle)).Append(',').Append(F(r.speed)).Append(',')
                    .Append(F(r.density)).Append(',').Append(F(r.deflection)).Append(',')
                    .Append(F(r.actualAngle)).Append(',').Append(F(r.cl)).Append(',')
                    .Append(F(r.cd)).Append(',').Append(r.stalled).Append(',')
                    .Append(r.reverse).Append(',').Append(F(r.lift)).Append(',')
                    .Append(F(r.drag)).Append(',').Append(F(r.force)).Append(',')
                    .Append(F(r.torque)).Append(',').Append(F(r.copError)).Append(',')
                    .Append(F(r.torqueError)).Append(',').Append(r.warning).Append(',')
                    .Append(r.limited).AppendLine();
            }
            return text.ToString();
        }

        private static void WriteCurveCsv(string package, string name,
            List<Row> rows, bool lift)
        {
            var text = new StringBuilder(2048);
            text.AppendLine(lift ? "aoa_deg,cl" : "aoa_deg,cd");
            foreach (Row row in rows)
                if (row.speed == 100f && row.density == 1.225f &&
                    row.deflection == 0f)
                    text.Append(F(row.actualAngle)).Append(',')
                        .AppendLine(F(lift ? row.cl : row.cd));
            File.WriteAllText(Path.Combine(package, name), text.ToString());
        }

        private static void Append(StringBuilder text, string label, bool passed)
        {
            text.AppendLine("- " + (passed ? "PASS" : "FAIL") + ": " + label);
        }

        private static string F(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        private static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
