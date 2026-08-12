using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    public sealed class V3CraftTestSpawner : MonoBehaviour
    {
        [SerializeField] private CraftBuildDefinition selectedBuild;
        [SerializeField] private bool assembleOnStart = true;
        [SerializeField] private V3CraftAssembler assembler;
        [SerializeField] private V3PilotInputAdapter inputSource;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private V3FreeDriveSession session;
        [SerializeField] private V3FreeDriveCamera cameraRig;
        [SerializeField] private V3FreeDriveHud hud;
        [SerializeField] private V3SystemsConsoleController systemsConsole;
        [SerializeField] private bool showThrusterDebug;

        public CraftBuildDefinition SelectedBuild
        {
            get => selectedBuild;
            set
            {
                selectedBuild = value;
                assembler?.SetBuild(value);
            }
        }

        public V3CraftAssembler Assembler => assembler;
        public V3PilotInputAdapter InputSource => inputSource;
        public GameObject CurrentCraft =>
            assembler != null ? assembler.AssembledRoot : null;
        public string LastError { get; private set; } = string.Empty;
        public string ConfigurationWarning { get; private set; } =
            string.Empty;
        public string SelectedBuildId =>
            selectedBuild != null
                ? selectedBuild.StableId
                : string.Empty;

        private void Awake()
        {
            ResolveReferences();
            if (assembler != null)
            {
                assembler.SetBuild(selectedBuild);
                assembler.SetSpawnerControlled(true);
            }

            ValidateSceneConfiguration();
            Subscribe();
        }

        private void OnValidate()
        {
            // Keep the assembler's standalone fallback aligned with the
            // development-scene authority. This also makes scene reloads and
            // edit-mode preview show the same selection as Play Mode.
            assembler?.SetBuild(selectedBuild);
        }

        private void Start()
        {
            if (assembleOnStart && CurrentCraft == null)
            {
                Assemble();
            }
            else
            {
                Reconnect(CurrentCraft);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        [ContextMenu("Assemble Selected V3 Craft")]
        public bool Assemble()
        {
            ResolveReferences();
            LastError = string.Empty;
            if (assembler == null)
            {
                LastError = "No V3CraftAssembler is assigned.";
                Debug.LogError(LastError, this);
                return false;
            }

            CraftBuildDefinition build = selectedBuild;
            if (build == null)
            {
                LastError =
                    "No V3CraftTestSpawner.SelectedBuild is assigned. " +
                    "The spawner is the authoritative development-scene " +
                    "build selection.";
                Debug.LogError(LastError, this);
                return false;
            }

            BuildValidationReport report =
                CraftBuildValidator.Validate(build);
            if (!report.IsValid)
            {
                LastError = BuildValidationMessage(report);
                Debug.LogError(LastError, this);
                return false;
            }

            SelectedBuild = build;
            bool built = assembler.Rebuild(build);
            if (!built)
            {
                LastError =
                    "The selected build failed runtime assembly validation.";
                return false;
            }

            Reconnect(assembler.AssembledRoot);
            return true;
        }

        [ContextMenu("Rebuild Selected V3 Craft")]
        public bool Rebuild()
        {
            return Assemble();
        }

        [ContextMenu("Clear Spawned V3 Craft")]
        public void Clear()
        {
            ResolveReferences();
            assembler?.ClearCraft();
            Reconnect(null);
        }

        [ContextMenu("Reset Spawned V3 Craft")]
        public bool ResetCraft()
        {
            ResolveReferences();
            return session != null && session.ResetCraft();
        }

        public bool SelectAndAssemble(CraftBuildDefinition build)
        {
            SelectedBuild = build;
            return Assemble();
        }

        private void ResolveReferences()
        {
            if (assembler == null)
            {
                assembler = GetComponentInChildren<V3CraftAssembler>(true);
                if (assembler == null)
                {
                    assembler =
                        FindAnyObjectByType<V3CraftAssembler>(
                            FindObjectsInactive.Include);
                }
            }

            if (inputSource == null)
            {
                inputSource =
                    GetComponentInChildren<V3PilotInputAdapter>(true);
            }

            if (inputSource == null)
            {
                var inputObject =
                    new GameObject("Local Pilot Input Source");
                inputObject.transform.SetParent(transform, false);
                inputSource =
                    inputObject.AddComponent<V3PilotInputAdapter>();
            }

            if (session == null)
            {
                session =
                    GetComponentInChildren<V3FreeDriveSession>(true);
                if (session == null)
                {
                    session =
                        FindAnyObjectByType<V3FreeDriveSession>(
                            FindObjectsInactive.Include);
                }
            }

            if (systemsConsole == null)
            {
                systemsConsole =
                    GetComponentInChildren<
                        V3SystemsConsoleController>(true);
            }

            session?.Configure(assembler, spawnPoint, inputSource);
        }

        private void ValidateSceneConfiguration()
        {
            ConfigurationWarning = string.Empty;
            if (assembler == null)
            {
                return;
            }

            if (selectedBuild == null)
            {
                ConfigurationWarning =
                    "Spawner Selected Build is empty.";
            }
            else if (assembler.Build != null &&
                     assembler.Build != selectedBuild)
            {
                ConfigurationWarning =
                    "Spawner Selected Build differs from the assembler " +
                    "fallback. The spawner selection is authoritative.";
            }

            V3CraftTestSpawner[] spawners =
                FindObjectsByType<V3CraftTestSpawner>(
                    FindObjectsInactive.Include);
            int matchingSpawners = 0;
            for (int i = 0; i < spawners.Length; i++)
            {
                if (spawners[i] != null &&
                    spawners[i].assembler == assembler)
                {
                    matchingSpawners++;
                }
            }

            if (matchingSpawners > 1)
            {
                ConfigurationWarning =
                    "Multiple V3CraftTestSpawner components target the " +
                    "same assembler.";
            }

            V3PilotInputAdapter[] inputs =
                FindObjectsByType<V3PilotInputAdapter>(
                    FindObjectsInactive.Exclude);
            if (inputs.Length > 1)
            {
                ConfigurationWarning =
                    "Multiple active V3PilotInputAdapter sources exist " +
                    "in this development scene.";
            }

            if (!string.IsNullOrEmpty(ConfigurationWarning))
            {
                Debug.LogWarning(
                    "Hovercraft V3 scene configuration: " +
                    ConfigurationWarning,
                    this);
            }
        }

        private void Subscribe()
        {
            if (assembler != null)
            {
                assembler.AssembledCraftChanged -= Reconnect;
                assembler.AssembledCraftChanged += Reconnect;
            }
        }

        private void Unsubscribe()
        {
            if (assembler != null)
            {
                assembler.AssembledCraftChanged -= Reconnect;
            }
        }

        private void Reconnect(GameObject craft)
        {
            session?.Configure(assembler, spawnPoint, inputSource);
            session?.RefreshCraftBinding();
            systemsConsole?.BindCraft(craft, inputSource);

            if (craft == null)
            {
                inputSource?.UnbindCraft();
                return;
            }

            inputSource?.BindCraft(
                craft.GetComponent<V3ControllerPipeline>(),
                craft.GetComponent<V3CraftMainframe>());

            V3ThrusterDebugView debug =
                craft.GetComponent<V3ThrusterDebugView>();
            if (debug != null)
            {
                debug.ShowVisualization = showThrusterDebug;
            }
        }

        private static string BuildValidationMessage(
            BuildValidationReport report)
        {
            if (report == null)
            {
                return "Build validation returned no report.";
            }

            for (int i = 0; i < report.Issues.Count; i++)
            {
                BuildIssue issue = report.Issues[i];
                if (issue.Severity == BuildIssueSeverity.Error)
                {
                    return $"[{issue.Code}] {issue.Message}";
                }
            }

            return "The selected craft build is invalid.";
        }
    }
}
