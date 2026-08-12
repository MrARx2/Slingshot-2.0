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
            var assembler = (V3CraftAssembler)target;
            V3CraftTestSpawner controllingSpawner =
                V3EditorPreviewAssembler.ResolveControllingSpawner(
                    assembler);
            DrawAssemblyConfiguration(
                assembler,
                controllingSpawner);

            CraftBuildDefinition build =
                V3EditorPreviewAssembler.ResolveBuild(assembler);
            EditorGUILayout.Space(8f);
            if (build == null)
            {
                EditorGUILayout.HelpBox(
                    "Choose a Craft Build to see its vehicle card.",
                    MessageType.Info);
                return;
            }

            EnsureStyles();
            if (controllingSpawner != null)
            {
                EditorGUILayout.HelpBox(
                    "Scene build authority: " +
                    controllingSpawner.name +
                    ". This one selection drives the vehicle card, " +
                    "Edit Mode preview, and Play Mode assembly.",
                    MessageType.Info);
            }

            DrawVehicleCard(build, assembler);
            DrawEditorPreview(assembler);
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawAssemblyConfiguration(
            V3CraftAssembler assembler,
            V3CraftTestSpawner controllingSpawner)
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Script",
                    MonoScript.FromMonoBehaviour(assembler),
                    typeof(MonoScript),
                    false);
            }

            CraftBuildDefinition currentBuild =
                controllingSpawner != null
                    ? controllingSpawner.SelectedBuild
                    : assembler.Build;
            EditorGUI.BeginChangeCheck();
            CraftBuildDefinition selectedBuild =
                (CraftBuildDefinition)EditorGUILayout.ObjectField(
                    controllingSpawner != null
                        ? "Craft Build (Scene Authority)"
                        : "Craft Build",
                    currentBuild,
                    typeof(CraftBuildDefinition),
                    false);
            if (EditorGUI.EndChangeCheck())
            {
                bool hadPreview =
                    assembler.transform.Find(
                        V3EditorPreviewAssembler.PreviewRootName) != null;
                if (controllingSpawner != null)
                {
                    Undo.RecordObjects(
                        new UnityEngine.Object[]
                        {
                            controllingSpawner,
                            assembler
                        },
                        "Select Hovercraft V3 Build");
                    V3EditorPreviewAssembler.SetAuthoritativeBuild(
                        assembler,
                        selectedBuild);
                    EditorUtility.SetDirty(controllingSpawner);
                    EditorUtility.SetDirty(assembler);
                }
                else
                {
                    SerializedProperty buildProperty =
                        serializedObject.FindProperty("build");
                    buildProperty.objectReferenceValue = selectedBuild;
                    serializedObject.ApplyModifiedProperties();
                }

                if (hadPreview)
                {
                    if (selectedBuild != null)
                    {
                        TryBuildPreview(assembler);
                    }
                    else
                    {
                        V3EditorPreviewAssembler.ClearPreview(assembler);
                    }
                }

                serializedObject.Update();
            }

            SerializedProperty assembleOnStart =
                serializedObject.FindProperty("assembleOnStart");
            using (new EditorGUI.DisabledScope(
                       controllingSpawner != null))
            {
                EditorGUILayout.PropertyField(assembleOnStart);
            }

            if (controllingSpawner != null)
            {
                EditorGUILayout.HelpBox(
                    "The scene spawner controls automatic assembly on Play. " +
                    "Use its Assemble On Start setting when needed.",
                    MessageType.None);
            }

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "installReferenceControllers"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("runtimeParent"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(
                    "previewBuildInEditor"));
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawEditorPreview(
            V3CraftAssembler assembler)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "EDIT MODE ASSEMBLY PREVIEW",
                EditorStyles.boldLabel);
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Preview controls are available outside Play Mode.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build Preview"))
            {
                TryBuildPreview(assembler);
            }

            if (GUILayout.Button("Refresh Preview"))
            {
                TryBuildPreview(assembler);
            }

            if (GUILayout.Button("Clear Preview"))
            {
                V3EditorPreviewAssembler.ClearPreview(assembler);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Validate Preview"))
            {
                BuildValidationReport report =
                    V3EditorPreviewAssembler.Validate(assembler);
                EditorUtility.DisplayDialog(
                    "Hovercraft V3 Preview Validation",
                    report != null && report.IsValid
                        ? "Build definition is valid."
                        : "Build definition contains validation errors. " +
                          "See the Console for details.",
                    "OK");
            }

            Transform preview = assembler.transform.Find(
                V3EditorPreviewAssembler.PreviewRootName);
            if (preview != null)
            {
                V3EditorPreviewRoot data =
                    preview.GetComponent<V3EditorPreviewRoot>();
                EditorGUILayout.HelpBox(
                    data != null
                        ? $"Preview active | {data.TotalMassKg:0.0} kg | " +
                          $"COM {data.CenterOfMass:F3}\n" +
                          $"Sensors {data.SensorDeviceCount} | " +
                          $"Computers {data.ComputerRuntimeCount} | " +
                          $"Systems {data.SystemRuntimeCount}\n" +
                          data.ValidationSummary
                        : "Preview root is active.",
                    data != null && data.IsValid
                        ? MessageType.Info
                        : MessageType.Warning);
            }
        }

        private static void TryBuildPreview(
            V3CraftAssembler assembler)
        {
            try
            {
                V3EditorPreviewAssembler.BuildPreview(assembler);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, assembler);
                EditorUtility.DisplayDialog(
                    "Hovercraft V3 Preview",
                    exception.Message,
                    "OK");
            }
        }

        private void OnSceneGUI()
        {
            var assembler = (V3CraftAssembler)target;
            Transform preview = assembler.transform.Find(
                V3EditorPreviewAssembler.PreviewRootName);
            if (preview == null)
            {
                return;
            }

            V3EditorPreviewRoot data =
                preview.GetComponent<V3EditorPreviewRoot>();
            if (data != null)
            {
                Handles.color = Color.yellow;
                Vector3 worldCom =
                    preview.TransformPoint(data.CenterOfMass);
                Handles.SphereHandleCap(
                    0,
                    worldCom,
                    Quaternion.identity,
                    0.35f,
                    EventType.Repaint);
                Handles.Label(
                    worldCom + Vector3.up * 0.4f,
                    $"COM  {data.TotalMassKg:0} kg");
            }

            RuntimeThrusterInstance[] thrusters =
                preview.GetComponentsInChildren<
                    RuntimeThrusterInstance>(true);
            Handles.color = new Color(0.1f, 0.85f, 1f);
            for (int i = 0; i < thrusters.Length; i++)
            {
                ThrusterDefinition definition =
                    thrusters[i].ThrusterDefinition;
                if (definition == null)
                {
                    continue;
                }

                Vector3 origin = thrusters[i].transform.TransformPoint(
                    definition.LocalForceOrigin);
                Vector3 direction =
                    thrusters[i].transform.TransformDirection(
                        definition.LocalThrustDirection).normalized;
                Handles.ArrowHandleCap(
                    0,
                    origin,
                    Quaternion.LookRotation(direction),
                    1.25f,
                    EventType.Repaint);
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
