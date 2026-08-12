using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

            bool generated = build.IsGenerated;
            if (generated)
            {
                EditorGUILayout.HelpBox(
                    "This is a generator-owned reference build. It is read-only here because canonical regeneration may replace it. Create an authored variant before changing tuning or installations.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(generated))
            {
                DrawBuildHeader();

                if (build.Chassis == null || build.Chassis.Prefab == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a chassis definition with a prefab before selecting parts.",
                        MessageType.Warning);
                }
                else
                {
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
                }
            }
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
            if (GUILayout.Button("Create Authored Variant..."))
            {
                CreateAuthoredVariant();
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = referenceBuild != null;
            if (GUILayout.Button("Reset to V2 Reference"))
            {
                Undo.RecordObject(build, "Reset V3 build to V2 reference");
                build.CopySelectionsFrom(referenceBuild);
                EditorUtility.SetDirty(build);
            }

            GUI.enabled = build.Ownership == V3BuildOwnership.AuthoredVariant ||
                build.Ownership == V3BuildOwnership.TestOnly;
            if (GUILayout.Button("Save Build Asset"))
            {
                EditorUtility.SetDirty(build);
                AssetDatabase.SaveAssets();
                ShowNotification(new GUIContent("Craft build saved."));
            }

            if (GUILayout.Button("Revert Build Asset"))
            {
                RevertBuildAsset();
            }

            if (GUILayout.Button("Compare Runtime to Source"))
            {
                CompareRuntimeToSource();
            }

            if (GUILayout.Button("Apply Runtime Calibration"))
            {
                ApplyRuntimeCalibration();
            }

            GUI.enabled = true;
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

            if (build.IsGenerated && GUILayout.Button("Edit Generator Source"))
            {
                MonoScript generator = AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Assets/HovercraftV3/Editor/Generation/V3SystemsIntegrationPrototypeGenerator.cs");
                Selection.activeObject = generator;
                EditorGUIUtility.PingObject(generator);
            }
        }

        private void CreateAuthoredVariant()
        {
            string defaultName = build.name + "_Variant";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Authored Hovercraft V3 Variant",
                defaultName,
                "asset",
                "Authored variants are stored outside Generated and survive canonical regeneration.",
                "Assets/HovercraftV3/Content/Builds");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (path.StartsWith("Assets/HovercraftV3/Generated/",
                StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Invalid authored variant path",
                    "Authored variants cannot be stored under Generated.",
                    "OK");
                return;
            }

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            CraftBuildDefinition created =
                CreateInstance<CraftBuildDefinition>();
            created.CopyPresentationFrom(build);
            created.CopySelectionsFrom(build);
            string slug = Slug(defaultName);
            string stableId = "build.authored." + slug + "." +
                Guid.NewGuid().ToString("N").Substring(0, 8);
            created.ConfigureAuthoringIdentity(
                stableId,
                string.Empty,
                V3BuildOwnership.AuthoredVariant,
                false);
            AssetDatabase.CreateAsset(created, path);
            created.ConfigureAuthoringIdentity(
                stableId,
                AssetDatabase.AssetPathToGUID(path),
                V3BuildOwnership.AuthoredVariant,
                false);
            Undo.RegisterCreatedObjectUndo(created, "Create authored V3 craft variant");
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();
            build = created;
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
            Repaint();
        }

        private void RevertBuildAsset()
        {
            string path = AssetDatabase.GetAssetPath(build);
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            build = AssetDatabase.LoadAssetAtPath<CraftBuildDefinition>(path);
            ShowNotification(new GUIContent("Craft build reverted from disk."));
        }

        private void CompareRuntimeToSource()
        {
            V3CraftRuntime runtime = FindAnyObjectByType<V3CraftRuntime>();
            if (runtime == null || runtime.Build == null)
            {
                ShowNotification(new GUIContent("No assembled runtime craft is available."));
                return;
            }
            bool same = JsonUtility.ToJson(runtime.Build.HoverConfiguration) ==
                JsonUtility.ToJson(build.HoverConfiguration);
            EditorUtility.DisplayDialog(
                "Runtime calibration comparison",
                same
                    ? "Runtime and source hover/surface-capture calibration match."
                    : "Runtime and source hover/surface-capture calibration differ. Apply only if the runtime values are deliberate.",
                "OK");
        }

        private void ApplyRuntimeCalibration()
        {
            V3CraftRuntime runtime = FindAnyObjectByType<V3CraftRuntime>();
            if (runtime == null || runtime.Build == null) return;
            Undo.RecordObject(build, "Apply runtime V3 calibration");
            build.HoverConfiguration.CopyFrom(
                runtime.Build.HoverConfiguration);
            EditorUtility.SetDirty(build);
            ShowNotification(new GUIContent(
                "Runtime hover/surface calibration applied. Use Save to persist."));
        }

        private static string Slug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "variant";
            char[] buffer = value.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (!char.IsLetterOrDigit(buffer[i])) buffer[i] = '_';
            }
            return new string(buffer).Trim('_');
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
            created.ConfigureAuthoringIdentity(
                "build.authored.new." + Guid.NewGuid().ToString("N").Substring(0, 8),
                string.Empty,
                V3BuildOwnership.AuthoredVariant,
                false);
            AssetDatabase.CreateAsset(created, path);
            created.ConfigureAuthoringIdentity(
                created.StableId,
                AssetDatabase.AssetPathToGUID(path),
                V3BuildOwnership.AuthoredVariant,
                false);
            AssetDatabase.SaveAssets();
            build = created;
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
        }
    }

    public sealed class V3StableIdAuditResult
    {
        public readonly List<string> DuplicateIds = new List<string>();
        public readonly List<string> MissingIds = new List<string>();
        public readonly List<string> InvalidWorldRootIds = new List<string>();
        public bool IsValid => DuplicateIds.Count == 0 &&
            MissingIds.Count == 0 && InvalidWorldRootIds.Count == 0;
    }

    public static class V3StableIdAudit
    {
        private const string Root = "Assets/HovercraftV3";
        private static readonly Regex SceneWorldId = new Regex(
            @"^\s*stableRootId:\s*(?<id>.*)\s*$",
            RegexOptions.CultureInvariant);

        [MenuItem("Tools/Hovercraft V3/Validation/Audit Stable IDs")]
        public static void AuditMenu()
        {
            V3StableIdAuditResult result = Scan();
            if (!result.IsValid)
            {
                throw new InvalidOperationException(Format(result));
            }
            Debug.Log("Hovercraft V3 stable-ID audit passed.");
        }

        public static V3StableIdAuditResult Scan()
        {
            var result = new V3StableIdAuditResult();
            var owners = new Dictionary<string, string>(
                StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets(
                "t:ScriptableObject",
                new[] { Root });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                UnityEngine.Object asset =
                    AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) continue;
                var serialized = new SerializedObject(asset);
                SerializedProperty stableId =
                    serialized.FindProperty("stableId");
                if (stableId == null) continue;
                Register(stableId.stringValue, path, owners, result);
            }

            string[] sceneGuids = AssetDatabase.FindAssets(
                "t:Scene",
                new[] { Root });
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) continue;
                // Development scenes can contain hundreds of megabytes of
                // cached track YAML. Stream the one identity field instead of
                // retaining the entire scene plus a regex copy in memory.
                foreach (string line in File.ReadLines(fullPath))
                {
                    Match match = SceneWorldId.Match(line);
                    if (!match.Success) continue;
                    string id = match.Groups["id"].Value.Trim();
                    if (!IsValidStableId(id))
                    {
                        result.InvalidWorldRootIds.Add(path + ": " + id);
                    }
                    Register(id, path + "#world-root", owners, result);
                }
            }
            return result;
        }

        public static bool IsValidStableId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.EndsWith(".", StringComparison.Ordinal))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    return false;
            }
            return true;
        }

        public static string Format(V3StableIdAuditResult result)
        {
            return "Duplicate IDs: " + string.Join("; ", result.DuplicateIds) +
                " | Missing IDs: " + string.Join("; ", result.MissingIds) +
                " | Invalid world IDs: " +
                string.Join("; ", result.InvalidWorldRootIds);
        }

        private static void Register(
            string id,
            string owner,
            Dictionary<string, string> owners,
            V3StableIdAuditResult result)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                result.MissingIds.Add(owner);
                return;
            }
            if (owners.TryGetValue(id, out string existing))
            {
                result.DuplicateIds.Add(
                    id + " => " + existing + " | " + owner);
            }
            else
            {
                owners.Add(id, owner);
            }
        }
    }
}
