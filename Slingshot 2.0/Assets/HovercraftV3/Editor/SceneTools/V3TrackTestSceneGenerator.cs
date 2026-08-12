using System;
using System.Collections.Generic;
using System.IO;
using Lunarlight.Hovercraft.V3.TrackIntegration;
using TrackGeneration;
using TrackGeneration.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Lunarlight.Hovercraft.V3.Editor
{
    /// <summary>
    /// Builds a dedicated procedural-track driving scene. CraftLab remains the
    /// focused craft/systems laboratory; this scene owns full-track generation,
    /// track-aligned spawn/recovery, world simulation, and forensic recording.
    /// </summary>
    public static class V3TrackTestSceneGenerator
    {
        public const string ScenePath =
            "Assets/HovercraftV3/Scenes/Development/V3_TrackTest.unity";

        private const string TrackGeneratorPrefabPath =
            "Assets/Prefabs/TrackGenerator.prefab";
        private const string AssemblerPrefabPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/V2Parity/" +
            "Prefabs/ApexV3_V2Reference_Assembler.prefab";
        private const string BaselineBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/Hovercraft_Baseline.asset";
        private const string PrimarySystemsBuildPath =
            "Assets/HovercraftV3/Generated/ReferenceCrafts/" +
            "SystemsIntegration/Builds/ApexV3_SystemsIntegration.asset";
        private const string ThemePath =
            "Assets/HovercraftV3/Content/UIThemes/" +
            "GenericSystemsTheme.asset";

        [MenuItem("Tools/Hovercraft V3/Generate V3 Track Test Scene")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "Exit Play Mode before generating the V3 Track Test scene.");
                return;
            }

            Scene loaded = SceneManager.GetSceneByPath(ScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                UpgradeLoadedSceneToCachedTrack(loaded);
                return;
            }

            GameObject trackPrefab = RequireAsset<GameObject>(
                TrackGeneratorPrefabPath);
            GameObject assemblerPrefab = RequireAsset<GameObject>(
                AssemblerPrefabPath);
            GameObject cachedTrackPrefab =
                V3TrackTestCacheBuilder.RequireCachedTrackPrefab();
            CraftBuildDefinition build =
                RequireAsset<CraftBuildDefinition>(PrimarySystemsBuildPath);

            Scene previousActive = SceneManager.GetActiveScene();
            bool additive = previousActive.IsValid() &&
                            previousActive.isLoaded &&
                            !string.IsNullOrEmpty(previousActive.path);
            if (!additive &&
                previousActive.IsValid() &&
                previousActive.isDirty &&
                !Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                additive ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            try
            {
                ConfigureEnvironment();
                CreateLighting();

                GameObject trackObject =
                    (GameObject)PrefabUtility.InstantiatePrefab(
                        trackPrefab,
                        scene);
                trackObject.name = "V3 Track Generator";
                trackObject.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                TrackGenerator generator =
                    trackObject.GetComponent<TrackGenerator>();
                if (generator == null)
                    throw new InvalidOperationException(
                        "TrackGenerator prefab has no TrackGenerator component.");

                GameObject cachedTrack =
                    (GameObject)PrefabUtility.InstantiatePrefab(
                        cachedTrackPrefab,
                        scene);
                cachedTrack.name = "V3 Track Test Cached Track";
                cachedTrack.transform.SetPositionAndRotation(
                    generator.transform.position,
                    generator.transform.rotation);
                AssignObject(generator, "trackRoot", cachedTrack.transform);
                AssignBool(generator, "generateOnStart", false);
                AssignBool(generator, "keepEditorTrackOnPlay", true);
                AssignBool(generator, "placeHovercraftOnStart", true);
                AssignBool(generator, "resetHovercraftWithBackspace", false);
                TrackGeneration.Core.TrackSeedManager seedManager =
                    trackObject.GetComponent<
                        TrackGeneration.Core.TrackSeedManager>();
                if (seedManager != null)
                {
                    // The prefab stores a known successful full-track seed.
                    // Scene generation must be repeatable and must not fail
                    // merely because a random candidate set was unlucky.
                    seedManager.UseRandomSeed = false;
                }
                float rideHeight =
                    build.HoverConfiguration.TargetHoverHeight;
                generator.SetTrackStartSpawnRideHeight(rideHeight);
                Transform spawn =
                    generator.GetOrCreateTrackStartSpawnPoint();
                if (!AdoptCachedTrackInEditor(generator))
                    throw new InvalidOperationException(
                        "Cached Track Test prefab contains no adoptable " +
                        "track-section data.");
                spawn.SetPositionAndRotation(
                    generator.TrackStartSpawnPoint.position,
                    generator.TrackStartSpawnPoint.rotation);
                GameObject assemblerObject =
                    (GameObject)PrefabUtility.InstantiatePrefab(
                        assemblerPrefab,
                        scene);
                assemblerObject.name = "Hovercraft Primary Systems Track Test Assembler";
                assemblerObject.transform.SetPositionAndRotation(
                    spawn.position,
                    spawn.rotation);
                V3CraftAssembler assembler =
                    assemblerObject.GetComponent<V3CraftAssembler>();
                assembler.SetBuild(build);
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    assembler);
                EditorUtility.SetDirty(assembler);
                AssignBool(assembler, "assembleOnStart", false);

                var sessionObject = new GameObject("V3 Track Test Session");
                V3FreeDriveSession session =
                    sessionObject.AddComponent<V3FreeDriveSession>();
                var inputObject = new GameObject("Local Pilot Input Source");
                inputObject.transform.SetParent(sessionObject.transform, false);
                V3PilotInputAdapter input =
                    inputObject.AddComponent<V3PilotInputAdapter>();

                AssignObject(session, "assembler", assembler);
                AssignObject(session, "spawnPoint", spawn);
                AssignObject(session, "localPilotInput", input);
                AssignInt(session, "trackSurfaceMask", 1 << 8);

                V3FreeDriveHud hud =
                    sessionObject.AddComponent<V3FreeDriveHud>();
                AssignObject(hud, "session", session);
                V3SystemsConsoleController console =
                    sessionObject.AddComponent<V3SystemsConsoleController>();
                AssignObject(console, "inputSource", input);
                V3SystemUIThemeDefinition theme =
                    AssetDatabase.LoadAssetAtPath<
                        V3SystemUIThemeDefinition>(ThemePath);
                if (theme != null)
                    AssignObject(console, "theme", theme);

                V3FreeDriveCamera cameraRig = CreateCamera(session, spawn);
                V3CraftTestSpawner spawner =
                    sessionObject.AddComponent<V3CraftTestSpawner>();
                spawner.SelectedBuild = build;
                EditorUtility.SetDirty(spawner);
                AssignObject(spawner, "assembler", assembler);
                AssignObject(spawner, "inputSource", input);
                AssignObject(spawner, "spawnPoint", spawn);
                AssignObject(spawner, "session", session);
                AssignObject(spawner, "cameraRig", cameraRig);
                AssignObject(spawner, "hud", hud);
                AssignObject(spawner, "systemsConsole", console);

                V3TrackRaceCraftBridge bridge =
                    sessionObject.AddComponent<V3TrackRaceCraftBridge>();
                bridge.Configure(spawner, session);

                V3DiagnosticSceneInstaller.EnsureRecorderInScene();
                V3WorldSimulationInstaller.InstallIntoScene(scene);

                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuildSettings();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (additive &&
                    previousActive.IsValid() &&
                    previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                if (additive && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.Refresh();
            Debug.Log($"Hovercraft V3 Track Test scene generated: {ScenePath}");
        }

        private static void UpgradeLoadedSceneToCachedTrack(Scene scene)
        {
            TrackGenerator generator = FindInScene<TrackGenerator>(scene);
            if (generator == null)
                throw new InvalidOperationException(
                    "The loaded V3 Track Test scene has no TrackGenerator.");

            GameObject cachedPrefab =
                V3TrackTestCacheBuilder.RequireCachedTrackPrefab();
            Transform existing = generator.TrackRoot;
            TrackRuntimeCacheIdentity existingIdentity = existing != null
                ? existing.GetComponent<TrackRuntimeCacheIdentity>()
                : null;
            if (existingIdentity == null)
            {
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);
                GameObject cached = (GameObject)PrefabUtility.InstantiatePrefab(
                    cachedPrefab,
                    scene);
                cached.name = "V3 Track Test Cached Track";
                cached.transform.SetPositionAndRotation(
                    generator.transform.position,
                    generator.transform.rotation);
                AssignObject(generator, "trackRoot", cached.transform);
            }

            AssignBool(generator, "generateOnStart", false);
            AssignBool(generator, "keepEditorTrackOnPlay", true);
            AssignBool(generator, "placeHovercraftOnStart", true);
            AssignBool(generator, "resetHovercraftWithBackspace", false);

            TrackSeedManager seedManager =
                generator.GetComponent<TrackSeedManager>();
            if (seedManager != null)
                seedManager.UseRandomSeed = false;

            V3CraftTestSpawner spawner =
                FindInScene<V3CraftTestSpawner>(scene);
            CraftBuildDefinition baseline =
                RequireAsset<CraftBuildDefinition>(PrimarySystemsBuildPath);
            if (spawner != null)
            {
                spawner.SelectedBuild = baseline;
                EditorUtility.SetDirty(spawner);
                V3CraftAssembler assembler = spawner.Assembler;
                if (assembler != null)
                {
                    assembler.SetBuild(baseline);
                    EditorUtility.SetDirty(assembler);
                }

                generator.SetTrackStartSpawnRideHeight(
                    baseline.HoverConfiguration
                        .TargetHoverHeight);
            }

            if (!AdoptCachedTrackInEditor(generator))
                throw new InvalidOperationException(
                    "Cached Track Test prefab contains no adoptable " +
                    "track-section data.");

            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Loaded Hovercraft V3 Track Test scene upgraded to the " +
                "persistent cached track.");
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
        }

        public static void RebuildCacheAndSceneFromCommandLine()
        {
            V3TrackTestCacheBuilder.RebuildCache();
            Generate();
        }

        [MenuItem("Tools/Hovercraft V3/Track Test Build/TrackTest_Baseline")]
        public static void SelectBaselinePreset()
        {
            ApplyBuildPreset(RequireAsset<CraftBuildDefinition>(BaselineBuildPath));
        }

        [MenuItem("Tools/Hovercraft V3/Track Test Build/TrackTest_PrimarySystems")]
        public static void SelectPrimarySystemsPreset()
        {
            ApplyBuildPreset(RequireAsset<CraftBuildDefinition>(PrimarySystemsBuildPath));
        }

        [MenuItem("Tools/Hovercraft V3/Track Test Build/TrackTest_SelectedAuthoredBuild")]
        public static void SelectAuthoredBuildPreset()
        {
            CraftBuildDefinition selected = Selection.activeObject as CraftBuildDefinition;
            if (selected == null ||
                selected.Ownership != V3BuildOwnership.AuthoredVariant)
            {
                throw new InvalidOperationException(
                    "Select an authored CraftBuildDefinition before applying the authored Track Test preset.");
            }
            ApplyBuildPreset(selected);
        }

        private static void ApplyBuildPreset(CraftBuildDefinition build)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }
            V3CraftTestSpawner spawner = FindInScene<V3CraftTestSpawner>(scene);
            if (spawner == null)
                throw new InvalidOperationException("Track Test scene has no authoritative craft spawner.");
            spawner.SelectedBuild = build;
            EditorUtility.SetDirty(spawner);
            if (spawner.Assembler != null)
            {
                spawner.Assembler.SetBuild(build);
                EditorUtility.SetDirty(spawner.Assembler);
            }
            TrackGenerator generator = FindInScene<TrackGenerator>(scene);
            if (generator != null)
            {
                generator.SetTrackStartSpawnRideHeight(
                    build.HoverConfiguration.TargetHoverHeight);
                EditorUtility.SetDirty(generator);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log("V3 Track Test build preset selected: " + build.StableId);
        }

        private static bool AdoptCachedTrackInEditor(
            TrackGenerator generator)
        {
            if (generator == null || generator.TrackRoot == null)
                return false;
            TrackGeneration.Macro.MacroTrackDebugVisualizer visualizer =
                generator.TrackRoot.GetComponent<
                    TrackGeneration.Macro.MacroTrackDebugVisualizer>();
            if (visualizer == null || visualizer.Sections == null ||
                visualizer.Sections.Count == 0)
                return false;

            System.Reflection.PropertyInfo property =
                typeof(TrackGenerator).GetProperty(
                    nameof(TrackGenerator.CurrentMacroSections));
            property?.SetValue(generator, visualizer.Sections);
            return generator.RefreshTrackStartSpawnPoint();
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Missing asset: {path}");
            return asset;
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.34f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.16f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.045f, 0.055f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.08f, 0.12f, 0.15f);
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 2400f;
        }

        private static void CreateLighting()
        {
            var sunObject = new GameObject("Track Test Sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(0.92f, 0.96f, 1f);
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var fillObject = new GameObject("Track Test Fill Light");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.32f;
            fill.color = new Color(0.25f, 0.55f, 1f);
            fill.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(32f, 145f, 0f);
        }

        private static V3FreeDriveCamera CreateCamera(
            V3FreeDriveSession session,
            Transform spawn)
        {
            var cameraObject = new GameObject("V3 Track Chase Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                spawn.position - spawn.forward * 14f + spawn.up * 6f,
                spawn.rotation);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 5000f;
            camera.fieldOfView = 68f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
            V3FreeDriveCamera rig =
                cameraObject.AddComponent<V3FreeDriveCamera>();
            AssignObject(rig, "session", session);
            return rig;
        }

        private static void AddSceneToBuildSettings()
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(
                        existing[i].path,
                        ScenePath,
                        StringComparison.Ordinal))
                    return;
            }

            var updated = new List<EditorBuildSettingsScene>(existing)
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            EditorBuildSettings.scenes = updated.ToArray();
        }

        private static SerializedProperty Property(
            UnityEngine.Object target,
            string name,
            out SerializedObject serialized)
        {
            serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException(
                    $"{target.GetType().Name} is missing {name}.");
            return property;
        }

        private static void AssignObject(
            UnityEngine.Object target,
            string name,
            UnityEngine.Object value)
        {
            SerializedProperty property = Property(target, name, out var serialized);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignBool(
            UnityEngine.Object target,
            string name,
            bool value)
        {
            SerializedProperty property = Property(target, name, out var serialized);
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignInt(
            UnityEngine.Object target,
            string name,
            int value)
        {
            SerializedProperty property = Property(target, name, out var serialized);
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T FindInScene<T>(Scene scene)
            where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            return null;
        }
    }

    /// <summary>
    /// Performs expensive deterministic planning/mesh generation once in the
    /// Editor and stores persistent mesh assets. The Track Test scene then
    /// adopts a nested prefab instead of rebuilding 48 km of colliders on Play.
    /// </summary>
    public static class V3TrackTestCacheBuilder
    {
        public const string CacheRoot =
            "Assets/HovercraftV3/Generated/TrackTestCache";
        public const string CachePrefabPath =
            CacheRoot + "/V3_TrackTest_CachedTrack.prefab";
        private const string MeshFolder = CacheRoot + "/Meshes";
        private const string TrackGeneratorPrefabPath =
            "Assets/Prefabs/TrackGenerator.prefab";
        private const int CachedProfileResolution = 32;

        [MenuItem("Tools/Hovercraft V3/Track Test/Rebuild Cached Track")]
        public static void RebuildCache()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Exit Play Mode before rebuilding the Track Test cache.");

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                TrackGeneratorPrefabPath);
            if (sourcePrefab == null)
                throw new InvalidOperationException(
                    "Missing TrackGenerator prefab: " +
                    TrackGeneratorPrefabPath);

            Scene previous = SceneManager.GetActiveScene();
            Scene temporary = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            GameObject generatorObject = null;
            try
            {
                generatorObject = (GameObject)PrefabUtility.InstantiatePrefab(
                    sourcePrefab,
                    temporary);
                TrackGenerator generator =
                    generatorObject.GetComponent<TrackGenerator>();
                TrackSeedManager seedManager =
                    generatorObject.GetComponent<TrackSeedManager>();
                if (generator == null || seedManager == null)
                    throw new InvalidOperationException(
                        "TrackGenerator cache source is incomplete.");

                seedManager.UseRandomSeed = false;
                generator.Designer = generator.Designer.Clone();
                generator.Designer.Road.ProfileResolution =
                    CachedProfileResolution;
                generator.Designer.Sanitize();

                EditorUtility.DisplayProgressBar(
                    "Hovercraft V3 Track Cache",
                    "Generating deterministic track layout and geometry...",
                    0.05f);
                generator.GenerateTrack();
                if (generator.TrackRoot == null ||
                    generator.CurrentMacroSections == null ||
                    generator.CurrentMacroSections.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Deterministic Track Test generation failed. " +
                        "Inspect the TrackGenerator report before retrying.");
                }

                RecreateCacheFolders();
                PersistMeshes(generator.TrackRoot.gameObject);

                TrackRuntimeCacheIdentity identity =
                    generator.TrackRoot.GetComponent<
                        TrackRuntimeCacheIdentity>();
                if (identity == null)
                    identity = generator.TrackRoot.gameObject.AddComponent<
                        TrackRuntimeCacheIdentity>();
                identity.Configure(
                    seedManager.CurrentSeedInput,
                    DateTime.UtcNow.ToString("O"),
                    CachedProfileResolution,
                    generator.GeneratedMeshCount,
                    generator.GeneratedVertexCount,
                    generator.GeneratedTriangleCount);

                EditorUtility.DisplayProgressBar(
                    "Hovercraft V3 Track Cache",
                    "Saving reusable cached-track prefab...",
                    0.95f);
                PrefabUtility.SaveAsPrefabAsset(
                    generator.TrackRoot.gameObject,
                    CachePrefabPath,
                    out bool success);
                if (!success)
                    throw new InvalidOperationException(
                        "Unity could not save the cached track prefab.");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    "Hovercraft V3 Track Test cache rebuilt at " +
                    CachePrefabPath + ".");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (generatorObject != null)
                    Object.DestroyImmediate(generatorObject);
                if (temporary.IsValid() && temporary.isLoaded)
                    EditorSceneManager.CloseScene(temporary, true);
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
        }

        public static GameObject RequireCachedTrackPrefab()
        {
            GameObject cached = AssetDatabase.LoadAssetAtPath<GameObject>(
                CachePrefabPath);
            if (cached == null)
            {
                throw new InvalidOperationException(
                    "The V3 Track Test cache is missing. Run Tools > " +
                    "Hovercraft V3 > Track Test > Rebuild Cached Track once, " +
                    "then regenerate the Track Test scene.");
            }
            return cached;
        }

        private static void RecreateCacheFolders()
        {
            if (AssetDatabase.IsValidFolder(CacheRoot))
                AssetDatabase.DeleteAsset(CacheRoot);

            EnsureFolder("Assets/HovercraftV3/Generated");
            EnsureFolder(CacheRoot);
            EnsureFolder(MeshFolder);
        }

        private static void PersistMeshes(GameObject trackRoot)
        {
            MeshFilter[] filters =
                trackRoot.GetComponentsInChildren<MeshFilter>(true);
            MeshCollider[] colliders =
                trackRoot.GetComponentsInChildren<MeshCollider>(true);
            var persistent = new Dictionary<int, Mesh>();
            int total = filters.Length + colliders.Length;
            int progress = 0;

            for (int i = 0; i < filters.Length; i++)
            {
                filters[i].sharedMesh = PersistMesh(
                    filters[i].sharedMesh,
                    persistent,
                    ref progress,
                    total);
            }
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].sharedMesh = PersistMesh(
                    colliders[i].sharedMesh,
                    persistent,
                    ref progress,
                    total);
            }
        }

        private static Mesh PersistMesh(
            Mesh source,
            Dictionary<int, Mesh> persistent,
            ref int progress,
            int total)
        {
            progress++;
            if (source == null)
                return null;
            int key = source.GetEntityId().GetHashCode();
            if (persistent.TryGetValue(key, out Mesh existing))
                return existing;

            EditorUtility.DisplayProgressBar(
                "Hovercraft V3 Track Cache",
                "Persisting mesh " + (persistent.Count + 1) + "...",
                Mathf.Lerp(0.65f, 0.93f,
                    progress / (float)Mathf.Max(1, total)));
            Mesh copy = Object.Instantiate(source);
            copy.name = source.name;
            string filename = persistent.Count.ToString("D4") + "_" +
                Sanitize(source.name) + ".asset";
            AssetDatabase.CreateAsset(copy, MeshFolder + "/" + filename);
            persistent.Add(key, copy);
            return copy;
        }

        private static string Sanitize(string value)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "TrackMesh"
                : value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return result;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException(
                    "Invalid cache folder path: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    /// <summary>
    /// Lets an already-open Unity Editor consume a deliberately-created,
    /// one-shot cache build request after script compilation. Nothing happens
    /// during normal Editor startup unless the request file exists.
    /// </summary>
    [InitializeOnLoad]
    internal static class V3TrackTestCacheBuildRequest
    {
        private const string RequestPath =
            "Temp/V3TrackTestCacheBuild.request";
        private const string SuccessPath =
            "Temp/V3TrackTestCacheBuild.succeeded";
        private const string FailurePath =
            "Temp/V3TrackTestCacheBuild.failed";
        private static bool processing;

        static V3TrackTestCacheBuildRequest()
        {
            EditorApplication.delayCall += TryProcess;
        }

        private static void TryProcess()
        {
            string request = Path.GetFullPath(RequestPath);
            if (processing || !File.Exists(request))
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryProcess;
                return;
            }

            processing = true;
            string success = Path.GetFullPath(SuccessPath);
            string failure = Path.GetFullPath(FailurePath);
            try
            {
                if (File.Exists(success)) File.Delete(success);
                if (File.Exists(failure)) File.Delete(failure);
                bool forceRebuild = File.ReadAllText(request)
                    .IndexOf("rebuild", StringComparison.OrdinalIgnoreCase) >= 0;
                if (forceRebuild || AssetDatabase.LoadAssetAtPath<GameObject>(
                        V3TrackTestCacheBuilder.CachePrefabPath) == null)
                {
                    V3TrackTestCacheBuilder.RebuildCache();
                }
                V3TrackTestSceneGenerator.Generate();
                File.WriteAllText(
                    success,
                    DateTime.UtcNow.ToString("O"));
            }
            catch (Exception exception)
            {
                File.WriteAllText(failure, exception.ToString());
                Debug.LogException(exception);
            }
            finally
            {
                if (File.Exists(request)) File.Delete(request);
                processing = false;
            }
        }
    }
}
