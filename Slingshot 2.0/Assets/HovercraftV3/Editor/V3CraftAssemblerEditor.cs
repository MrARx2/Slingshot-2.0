using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    [CustomEditor(typeof(V3CraftAssembler))]
    public sealed class V3CraftAssemblerEditor : UnityEditor.Editor
    {
        private sealed class BuildStats
        {
            public float TotalMassKg;
            public Vector3 Dimensions;
            public Vector3 ApproximateCenterOfMass;
            public float FrontBalancePercent;
            public float LeftBalancePercent;
            public float ContinuousPower;
            public float PropulsionPowerCeiling;
            public float NormalDriveForceN;
            public float OverloadDriveForceN;
            public float BrakeForceN;
            public int ThrusterCount;
            public int HoverThrusterCount;
            public int GimbalCount;
            public int SpringMountCount;
            public float GimbalPitchDegrees;
            public float GimbalYawDegrees;
            public float SpringTravelMetres;
        }

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle sectionStyle;
        private bool showPowerModes = true;
        private bool showInstalledHardware;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var assembler = (V3CraftAssembler)target;
            CraftBuildDefinition build = assembler.Build;
            EditorGUILayout.Space(8f);
            if (build == null)
            {
                EditorGUILayout.HelpBox(
                    "Choose a Craft Build to see its vehicle card.",
                    MessageType.Info);
                return;
            }

            EnsureStyles();
            DrawVehicleCard(build, assembler);
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawVehicleCard(
            CraftBuildDefinition build,
            V3CraftAssembler assembler)
        {
            BuildStats stats = CalculateStats(build);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                $"{build.ManufacturerName.ToUpperInvariant()}  //  " +
                build.DisplayName,
                titleStyle);
            EditorGUILayout.LabelField(
                $"{build.VehicleClass}  |  {build.LayoutName}",
                subtitleStyle);

            if (!string.IsNullOrWhiteSpace(build.Summary))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(build.Summary, MessageType.None);
            }

            DrawSection("VEHICLE SPECIFICATION");
            DrawStat(
                "Mass",
                $"{stats.TotalMassKg:N0} kg  " +
                $"({stats.TotalMassKg / 1000f:0.00} t)");
            DrawStat(
                "Body size",
                stats.Dimensions.sqrMagnitude > 0.001f
                    ? $"{stats.Dimensions.z:0.0} m L  x  " +
                      $"{stats.Dimensions.x:0.0} m W  x  " +
                      $"{stats.Dimensions.y:0.0} m H"
                    : "Not available");
            DrawStat(
                "Power core",
                stats.ContinuousPower > 0f
                    ? $"{stats.ContinuousPower:N0} PU continuous  |  " +
                      $"{stats.PropulsionPowerCeiling:N0} PU propulsion"
                    : "No energy core");
            DrawStat(
                "Rear drive",
                stats.NormalDriveForceN > 0f
                    ? $"{FormatForce(stats.NormalDriveForceN)} normal  |  " +
                      $"{FormatForce(stats.OverloadDriveForceN)} overload"
                    : "No drive thruster");
            DrawStat(
                "Braking force",
                stats.BrakeForceN > 0f
                    ? FormatForce(stats.BrakeForceN)
                    : "No dedicated brake");
            DrawStat(
                "Thrust / mass",
                stats.TotalMassKg > 0f
                    ? $"{stats.NormalDriveForceN / stats.TotalMassKg:0.0} N/kg"
                    : "Not available");
            DrawStat(
                "Balance F/R",
                $"{stats.FrontBalancePercent:0.0}% / " +
                $"{100f - stats.FrontBalancePercent:0.0}%");
            DrawStat(
                "Balance L/R",
                $"{stats.LeftBalancePercent:0.0}% / " +
                $"{100f - stats.LeftBalancePercent:0.0}%");
            DrawStat(
                "Approx. center of mass",
                $"X {stats.ApproximateCenterOfMass.x:+0.00;-0.00;0.00}  " +
                $"Y {stats.ApproximateCenterOfMass.y:+0.00;-0.00;0.00}  " +
                $"Z {stats.ApproximateCenterOfMass.z:+0.00;-0.00;0.00} m");
            DrawStat(
                "Thruster layout",
                $"{stats.ThrusterCount} total  |  " +
                $"{stats.HoverThrusterCount} primary hover");

            DrawSection("HANDLING PROFILE  //  DESIGN INTENT");
            bool hasGimbal = stats.GimbalCount > 0;
            bool hasSpring = stats.SpringMountCount > 0;
            DrawRating("Straight-line pace", hasGimbal ? 0.78f : 0.8f);
            DrawRating("Vectoring agility", hasGimbal ? 0.92f : 0.64f);
            DrawRating("Surface compliance", hasSpring ? 0.9f : 0.42f);
            DrawRating(
                "Predictability",
                hasGimbal ? 0.68f : hasSpring ? 0.8f : 0.94f);

            if (!string.IsNullOrWhiteSpace(build.DrivingNotes))
            {
                EditorGUILayout.Space(5f);
                EditorGUILayout.HelpBox(
                    "WHAT TO EXPECT\n" + build.DrivingNotes,
                    MessageType.Info);
            }

            showInstalledHardware = EditorGUILayout.Foldout(
                showInstalledHardware,
                "Layout hardware",
                true);
            if (showInstalledHardware)
            {
                EditorGUI.indentLevel++;
                DrawStat(
                    "Main connector",
                    stats.GimbalCount > 0
                        ? $"{stats.GimbalCount} powered gimbal  " +
                          $"({stats.GimbalPitchDegrees:0} deg pitch / " +
                          $"{stats.GimbalYawDegrees:0} deg yaw)"
                        : "Direct-mounted");
                DrawStat(
                    "Hover compliance",
                    stats.SpringMountCount > 0
                        ? $"{stats.SpringMountCount} spring mount  |  " +
                          $"{stats.SpringTravelMetres:0.00} m travel"
                        : "Direct-mounted");
                EditorGUI.indentLevel--;
            }

            showPowerModes = EditorGUILayout.Foldout(
                showPowerModes,
                "Power allocation modes (Tab)",
                true);
            if (showPowerModes)
            {
                EditorGUI.indentLevel++;
                DrawPowerMode(V3PowerAllocationMode.Balanced);
                DrawPowerMode(V3PowerAllocationMode.Propulsion);
                DrawPowerMode(V3PowerAllocationMode.Stability);
                DrawPowerMode(V3PowerAllocationMode.Recovery);
                EditorGUILayout.HelpBox(
                    "These modes change priority only when demand exceeds " +
                    "available power. With normal power headroom, they are " +
                    "expected to drive identically.",
                    MessageType.Warning);
                EditorGUI.indentLevel--;
            }

            if (Application.isPlaying &&
                assembler.AssembledRoot != null)
            {
                V3CraftTelemetryHub live =
                    assembler.AssembledRoot.GetComponent<V3CraftTelemetryHub>();
                if (live != null)
                {
                    DrawSection("LIVE");
                    DrawStat("Speed", $"{live.SpeedKmh:0} km/h");
                    DrawStat(
                        "Power mode",
                        live.PowerAllocationMode.ToString());
                    DrawStat(
                        "Power state",
                        V3PowerModeInfo.GetLimitationNote(
                            live.IsPropulsionPowerLimited));
                    DrawStat(
                        "Maximum temperature",
                        $"{live.MaximumTemperatureC:0.0} C");
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPowerMode(V3PowerAllocationMode mode)
        {
            EditorGUILayout.LabelField(
                mode.ToString().ToUpperInvariant(),
                V3PowerModeInfo.GetShortDescription(mode));
            EditorGUILayout.LabelField(
                string.Empty,
                V3PowerModeInfo.GetPriorityOrder(mode),
                EditorStyles.miniLabel);
        }

        private void DrawSection(string label)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(label, sectionStyle);
        }

        private static void DrawStat(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                label,
                GUILayout.Width(
                    Mathf.Max(
                        110f,
                        EditorGUIUtility.labelWidth - 4f)));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawRating(string label, float value)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            if (rect.width < 210f)
            {
                EditorGUI.LabelField(
                    rect,
                    $"{label}: {RatingLabel(value)}");
                return;
            }

            float labelWidth = Mathf.Clamp(
                EditorGUIUtility.labelWidth - 4f,
                100f,
                rect.width - 90f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                labelWidth,
                rect.height);
            Rect barRect = new Rect(
                labelRect.xMax + 4f,
                rect.y + 1f,
                rect.width - labelRect.width - 4f,
                rect.height - 2f);
            EditorGUI.LabelField(labelRect, label);
            EditorGUI.ProgressBar(
                barRect,
                Mathf.Clamp01(value),
                RatingLabel(value));
        }

        private static string RatingLabel(float value)
        {
            int rating = Mathf.Clamp(Mathf.RoundToInt(value * 5f), 1, 5);
            return new string('●', rating) +
                   new string('○', 5 - rating);
        }

        private static BuildStats CalculateStats(CraftBuildDefinition build)
        {
            var stats = new BuildStats();
            ChassisDefinition chassis = build.Chassis;
            if (chassis == null)
            {
                return stats;
            }

            stats.TotalMassKg = chassis.BaseMassKg;
            stats.ApproximateCenterOfMass =
                chassis.BaseCenterOfMass * chassis.BaseMassKg;
            stats.Dimensions = GetDimensions(chassis.Prefab);
            Dictionary<string, Vector3> socketPositions =
                GetSocketPositions(chassis.Prefab);

            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation == null || installation.IsEmpty)
                {
                    continue;
                }

                Vector3 position =
                    socketPositions.TryGetValue(
                        installation.SocketId,
                        out Vector3 socketPosition)
                        ? socketPosition
                        : Vector3.zero;
                AddMass(
                    installation.Connector,
                    position,
                    stats);
                AddMass(
                    installation.Endpoint,
                    position,
                    stats);

                if (installation.Connector is GimbalDefinition gimbal)
                {
                    stats.GimbalCount++;
                    stats.GimbalPitchDegrees = Mathf.Max(
                        stats.GimbalPitchDegrees,
                        gimbal.MaximumPitchDegrees);
                    stats.GimbalYawDegrees = Mathf.Max(
                        stats.GimbalYawDegrees,
                        gimbal.MaximumYawDegrees);
                }
                else if (installation.Connector is SpringMountDefinition spring)
                {
                    stats.SpringMountCount++;
                    stats.SpringTravelMetres += spring.TravelDistanceM;
                }

                if (installation.Endpoint is EnergyCoreDefinition core)
                {
                    stats.ContinuousPower += core.ContinuousOutput;
                    stats.PropulsionPowerCeiling +=
                        core.PropulsionChannelCeiling;
                }

                if (installation.Endpoint is ThrusterDefinition thruster)
                {
                    stats.ThrusterCount++;
                    float normalForce =
                        thruster.MaximumForwardForceN *
                        thruster.NormalOutputMultiplier;
                    float overloadForce =
                        thruster.MaximumForwardForceN *
                        thruster.OverloadOutputMultiplier;
                    switch (thruster.Role)
                    {
                        case PartRole.Propulsion:
                            stats.NormalDriveForceN += normalForce;
                            stats.OverloadDriveForceN += overloadForce;
                            break;
                        case PartRole.Braking:
                            stats.BrakeForceN += normalForce;
                            break;
                        case PartRole.Hover:
                            stats.HoverThrusterCount++;
                            break;
                    }
                }
            }

            if (stats.TotalMassKg > 0f)
            {
                stats.ApproximateCenterOfMass =
                    stats.ApproximateCenterOfMass /
                    stats.TotalMassKg +
                    chassis.ParityCenterOfMassCalibration;
            }

            float length = Mathf.Max(0.01f, stats.Dimensions.z);
            float width = Mathf.Max(0.01f, stats.Dimensions.x);
            stats.FrontBalancePercent = Mathf.Clamp(
                50f +
                stats.ApproximateCenterOfMass.z / length * 100f,
                0f,
                100f);
            stats.LeftBalancePercent = Mathf.Clamp(
                50f -
                stats.ApproximateCenterOfMass.x / width * 100f,
                0f,
                100f);
            return stats;
        }

        private static void AddMass(
            PartDefinition part,
            Vector3 position,
            BuildStats stats)
        {
            if (part == null)
            {
                return;
            }

            float mass = Mathf.Max(0f, part.Physical.massKg);
            stats.TotalMassKg += mass;
            stats.ApproximateCenterOfMass +=
                (position + part.Physical.localCenterOfMass) * mass;
        }

        private static Vector3 GetDimensions(GameObject chassisPrefab)
        {
            if (chassisPrefab == null)
            {
                return Vector3.zero;
            }

            BoxCollider box = chassisPrefab.GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 scale = chassisPrefab.transform.lossyScale;
                return new Vector3(
                    Mathf.Abs(box.size.x * scale.x),
                    Mathf.Abs(box.size.y * scale.y),
                    Mathf.Abs(box.size.z * scale.z));
            }

            Renderer[] renderers =
                chassisPrefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.zero;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.size;
        }

        private static Dictionary<string, Vector3> GetSocketPositions(
            GameObject chassisPrefab)
        {
            var positions =
                new Dictionary<string, Vector3>(StringComparer.Ordinal);
            if (chassisPrefab == null)
            {
                return positions;
            }

            Transform root = chassisPrefab.transform;
            V3Socket[] sockets =
                chassisPrefab.GetComponentsInChildren<V3Socket>(true);
            for (int i = 0; i < sockets.Length; i++)
            {
                V3Socket socket = sockets[i];
                positions[socket.SocketId] =
                    root.InverseTransformPoint(
                        socket.MountTransform.position);
            }

            return positions;
        }

        private static string FormatForce(float forceN)
        {
            if (forceN >= 1000000f)
            {
                return $"{forceN / 1000000f:0.00} MN";
            }

            return $"{forceN / 1000f:0} kN";
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.2f, 0.72f, 0.95f) }
            };
            subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.35f, 0.8f, 1f) }
            };
        }
    }
}
