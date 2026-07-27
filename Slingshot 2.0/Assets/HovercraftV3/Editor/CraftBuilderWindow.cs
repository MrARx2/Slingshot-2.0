using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public sealed class CraftBuilderWindow : EditorWindow
    {
        private CraftBuildDefinition build;
        private CraftBuildDefinition referenceBuild;
        private Vector2 scrollPosition;
        private readonly Dictionary<string, bool> compatibilityFoldouts =
            new Dictionary<string, bool>();

        [MenuItem("Tools/Hovercraft V3/Craft Builder")]
        private static void Open()
        {
            GetWindow<CraftBuilderWindow>("V3 Craft Builder");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Hovercraft V3 Craft Builder", EditorStyles.boldLabel);
            build = (CraftBuildDefinition)EditorGUILayout.ObjectField(
                "Build",
                build,
                typeof(CraftBuildDefinition),
                false);
            referenceBuild = (CraftBuildDefinition)EditorGUILayout.ObjectField(
                "V2 Reference Build",
                referenceBuild,
                typeof(CraftBuildDefinition),
                false);

            if (build == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign or create a Craft Build Definition to edit authored socket chains.",
                    MessageType.Info);
                if (GUILayout.Button("Create Craft Build Definition..."))
                {
                    CreateBuildAsset();
                }

                return;
            }

            DrawBuildHeader();

            if (build.Chassis == null || build.Chassis.Prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a chassis definition with a prefab before selecting parts.",
                    MessageType.Warning);
                DrawValidation();
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            V3Socket[] sockets = build.Chassis.Prefab
                .GetComponentsInChildren<V3Socket>(true)
                .OrderBy(socket => socket.SocketId)
                .ToArray();

            for (int i = 0; i < sockets.Length; i++)
            {
                DrawSocket(sockets[i]);
            }

            EditorGUILayout.EndScrollView();
            DrawSummary();
            DrawValidation();
            DrawActions();
        }

        private void DrawBuildHeader()
        {
            var serializedBuild = new SerializedObject(build);
            serializedBuild.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField(
                "Vehicle Identity",
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedBuild.FindProperty("displayName"));
            EditorGUILayout.PropertyField(
                serializedBuild.FindProperty("manufacturerName"));
            EditorGUILayout.PropertyField(
                serializedBuild.FindProperty("vehicleClass"));
            EditorGUILayout.PropertyField(
                serializedBuild.FindProperty("layoutName"));
            EditorGUILayout.PropertyField(
                serializedBuild.FindProperty("summary"));
            EditorGUILayout.PropertyField(
                serializedBuild.FindProperty("drivingNotes"));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Assembly", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedBuild.FindProperty("chassis"));
            EditorGUILayout.PropertyField(serializedBuild.FindProperty("catalog"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedBuild.ApplyModifiedProperties();
            }
        }

        private void DrawSocket(V3Socket socket)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(socket.DebugName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                socket.SocketId,
                $"{socket.Family} / {socket.Size}",
                EditorStyles.miniLabel);

            SocketInstallation installation = build.FindInstallation(socket.SocketId);
            List<EndpointDefinition> directEndpoints = GetCompatibleDirectEndpoints(socket);
            List<ConnectorDefinition> connectors = GetCompatibleConnectors(socket);

            var labels = new List<string> { "Empty" };
            var kinds = new List<int> { 0 };
            var definitions = new List<PartDefinition> { null };

            for (int i = 0; i < directEndpoints.Count; i++)
            {
                labels.Add($"Endpoint / {directEndpoints[i].DisplayName}");
                kinds.Add(1);
                definitions.Add(directEndpoints[i]);
            }

            for (int i = 0; i < connectors.Count; i++)
            {
                labels.Add($"Connector / {connectors[i].DisplayName}");
                kinds.Add(2);
                definitions.Add(connectors[i]);
            }

            AddCurrentSelectionIfFiltered(
                installation,
                labels,
                kinds,
                definitions);
            int selectedIndex = FindSelectedIndex(installation, kinds, definitions);
            int nextIndex = EditorGUILayout.Popup("Installed", selectedIndex, labels.ToArray());
            if (nextIndex != selectedIndex)
            {
                Undo.RecordObject(build, "Change V3 socket installation");
                installation = build.GetOrCreateInstallation(socket.SocketId);
                if (kinds[nextIndex] == 0)
                {
                    installation.SetEmpty();
                }
                else if (kinds[nextIndex] == 1)
                {
                    installation.SetDirect((EndpointDefinition)definitions[nextIndex]);
                }
                else
                {
                    installation.SetConnector((ConnectorDefinition)definitions[nextIndex], null);
                }

                EditorUtility.SetDirty(build);
            }

            if (installation != null && installation.Connector != null)
            {
                DrawConnectorEndpoint(socket, installation);
            }

            DrawCompatibilityNotes(socket, installation?.Connector);
            EditorGUILayout.EndVertical();
        }

        private void DrawConnectorEndpoint(V3Socket socket, SocketInstallation installation)
        {
            List<EndpointDefinition> endpoints = GetCompatibleChildEndpoints(
                socket,
                installation.Connector);
            var labels = new List<string> { "Select compatible endpoint..." };
            labels.AddRange(endpoints.Select(endpoint => endpoint.DisplayName));
            if (installation.Endpoint != null &&
                !endpoints.Contains(installation.Endpoint))
            {
                endpoints.Add(installation.Endpoint);
                labels.Add(
                    $"Incompatible / {installation.Endpoint.DisplayName}");
            }

            int selected = installation.Endpoint == null
                ? 0
                : endpoints.IndexOf(installation.Endpoint) + 1;
            if (selected < 0)
            {
                selected = 0;
            }

            int next = EditorGUILayout.Popup("Child Endpoint", selected, labels.ToArray());
            if (next == selected)
            {
                return;
            }

            Undo.RecordObject(build, "Change V3 connector endpoint");
            installation.SetChildEndpoint(next == 0 ? null : endpoints[next - 1]);
            EditorUtility.SetDirty(build);
        }

        private void DrawCompatibilityNotes(
            V3Socket socket,
            ConnectorDefinition selectedConnector)
        {
            if (build.Catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Part Catalog to browse compatible products.",
                    MessageType.Warning);
                return;
            }

            bool expanded = compatibilityFoldouts.TryGetValue(
                socket.SocketId,
                out bool current) && current;
            expanded = EditorGUILayout.Foldout(
                expanded,
                "Compatibility details",
                true);
            compatibilityFoldouts[socket.SocketId] = expanded;
            if (!expanded)
            {
                return;
            }

            bool foundRejected = false;
            foreach (ConnectorDefinition connector
                in EnumerateCatalog<ConnectorDefinition>()
                    .OrderBy(part => part.DisplayName))
            {
                if (CraftBuildValidator.TryValidateConnector(
                    socket,
                    connector,
                    out string error))
                {
                    continue;
                }

                foundRejected = true;
                EditorGUILayout.HelpBox(
                    $"{connector.DisplayName}: {error}",
                    MessageType.None);
            }

            foreach (EndpointDefinition endpoint
                in EnumerateCatalog<EndpointDefinition>()
                    .OrderBy(part => part.DisplayName))
            {
                bool valid = selectedConnector != null
                    ? CraftBuildValidator.TryValidateChain(
                        socket,
                        selectedConnector,
                        endpoint,
                        out string error)
                    : CraftBuildValidator.TryValidateEndpoint(
                        socket,
                        null,
                        endpoint,
                        out error);
                if (valid)
                {
                    continue;
                }

                foundRejected = true;
                string context = selectedConnector != null
                    ? $"after {selectedConnector.DisplayName}"
                    : "direct";
                EditorGUILayout.HelpBox(
                    $"{endpoint.DisplayName} ({context}): {error}",
                    MessageType.None);
            }

            if (!foundRejected)
            {
                EditorGUILayout.LabelField(
                    "Every catalog option in this context is compatible.",
                    EditorStyles.miniLabel);
            }
        }

        private static void AddCurrentSelectionIfFiltered(
            SocketInstallation installation,
            List<string> labels,
            List<int> kinds,
            List<PartDefinition> definitions)
        {
            if (installation == null || installation.IsEmpty)
            {
                return;
            }

            PartDefinition selected = installation.Connector != null
                ? installation.Connector
                : installation.Endpoint;
            if (selected == null || definitions.Contains(selected))
            {
                return;
            }

            labels.Add($"Incompatible / {selected.DisplayName}");
            kinds.Add(installation.Connector != null ? 2 : 1);
            definitions.Add(selected);
        }

        private List<EndpointDefinition> GetCompatibleDirectEndpoints(V3Socket socket)
        {
            return EnumerateCatalog<EndpointDefinition>()
                .Where(endpoint =>
                    CraftBuildValidator.TryValidateEndpoint(socket, null, endpoint, out _))
                .OrderBy(endpoint => endpoint.DisplayName)
                .ToList();
        }

        private List<ConnectorDefinition> GetCompatibleConnectors(V3Socket socket)
        {
            return EnumerateCatalog<ConnectorDefinition>()
                .Where(connector =>
                    CraftBuildValidator.TryValidateConnector(socket, connector, out _))
                .OrderBy(connector => connector.DisplayName)
                .ToList();
        }

        private List<EndpointDefinition> GetCompatibleChildEndpoints(
            V3Socket socket,
            ConnectorDefinition connector)
        {
            return EnumerateCatalog<EndpointDefinition>()
                .Where(endpoint =>
                    CraftBuildValidator.TryValidateChain(socket, connector, endpoint, out _))
                .OrderBy(endpoint => endpoint.DisplayName)
                .ToList();
        }

        private IEnumerable<T> EnumerateCatalog<T>() where T : PartDefinition
        {
            if (build.Catalog == null)
            {
                return Enumerable.Empty<T>();
            }

            return build.Catalog.Parts.OfType<T>().Where(part => part != null);
        }

        private static int FindSelectedIndex(
            SocketInstallation installation,
            IReadOnlyList<int> kinds,
            IReadOnlyList<PartDefinition> definitions)
        {
            if (installation == null || installation.IsEmpty)
            {
                return 0;
            }

            PartDefinition selected = installation.Connector != null
                ? installation.Connector
                : installation.Endpoint;
            int selectedKind = installation.Connector != null ? 2 : 1;

            for (int i = 1; i < definitions.Count; i++)
            {
                if (kinds[i] == selectedKind && definitions[i] == selected)
                {
                    return i;
                }
            }

            return 0;
        }

        private void DrawSummary()
        {
            float partsMass = 0f;
            float idlePower = 0f;
            float maximumPower = 0f;

            for (int i = 0; i < build.Installations.Count; i++)
            {
                SocketInstallation installation = build.Installations[i];
                if (installation == null)
                {
                    continue;
                }

                AddPart(installation.Connector);
                AddPart(installation.Endpoint);
            }

            void AddPart(PartDefinition part)
            {
                if (part == null)
                {
                    return;
                }

                partsMass += part.Physical.massKg;
                idlePower += part.Power.idleDemand;
                maximumPower += part.Power.maximumDemand;
            }

            float chassisMass = build.Chassis != null ? build.Chassis.BaseMassKg : 0f;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build Effects", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Calculated Mass", $"{chassisMass + partsMass:0.##} kg");
            EditorGUILayout.LabelField("Part Mass", $"{partsMass:0.##} kg");
            EditorGUILayout.LabelField("Idle Demand", $"{idlePower:0.##}");
            EditorGUILayout.LabelField("Maximum Demand", $"{maximumPower:0.##}");
        }

        private void DrawValidation()
        {
            BuildValidationReport report = CraftBuildValidator.Validate(build);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                report.IsValid ? "Build is valid" : "Build has errors",
                EditorStyles.boldLabel);

            for (int i = 0; i < report.Issues.Count; i++)
            {
                BuildIssue issue = report.Issues[i];
                string prefix = string.IsNullOrWhiteSpace(issue.SocketId)
                    ? issue.Code
                    : $"{issue.SocketId} / {issue.Code}";
                EditorGUILayout.HelpBox(
                    $"{prefix}: {issue.Message}",
                    issue.Severity == BuildIssueSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning);
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = referenceBuild != null;
            if (GUILayout.Button("Reset to V2 Reference"))
            {
                Undo.RecordObject(build, "Reset V3 build to V2 reference");
                build.CopySelectionsFrom(referenceBuild);
                EditorUtility.SetDirty(build);
            }

            GUI.enabled = true;
            if (GUILayout.Button("Save Build Asset"))
            {
                EditorUtility.SetDirty(build);
                AssetDatabase.SaveAssets();
                ShowNotification(new GUIContent("Craft build saved."));
            }

            if (GUILayout.Button("Rebuild Selected Assembler"))
            {
                V3CraftAssembler assembler = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponent<V3CraftAssembler>()
                    : null;
                if (assembler == null)
                {
                    ShowNotification(new GUIContent("Select a GameObject with V3CraftAssembler."));
                }
                else
                {
                    assembler.Rebuild();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void CreateBuildAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Hovercraft V3 Build",
                "NewHovercraftV3Build",
                "asset",
                "Choose where to save the Craft Build Definition.");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            CraftBuildDefinition created =
                CreateInstance<CraftBuildDefinition>();
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            build = created;
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
        }
    }
}
