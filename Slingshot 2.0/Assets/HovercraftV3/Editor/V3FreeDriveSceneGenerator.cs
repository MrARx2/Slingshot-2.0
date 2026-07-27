using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V3FreeDriveSceneGenerator
    {
        public const string FreeDriveRoot =
            "Assets/HovercraftV3/FreeDrive";
        public const string ScenePath =
            FreeDriveRoot + "/Scenes/V3_FreeDrive_Test.unity";

        private const string AssemblerPrefabPath =
            "Assets/HovercraftV3/Prototype/Prefabs/" +
            "ApexV3_V2Reference_Assembler.prefab";
        private const string MaterialsPath =
            FreeDriveRoot + "/Materials";
        private static readonly Vector3 SpawnPosition =
            new Vector3(0f, 3.4f, -360f);

        [MenuItem("Tools/Hovercraft V3/Generate Free Drive Test Scene")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "Exit Play Mode before generating the V3 free-drive scene.");
                return;
            }

            Scene loadedScene = SceneManager.GetSceneByPath(ScenePath);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                Debug.LogWarning(
                    "Close the existing V3 free-drive scene before regenerating it.");
                return;
            }

            GameObject assemblerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssemblerPrefabPath);
            if (assemblerPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing assembler prefab: {AssemblerPrefabPath}. " +
                    "Generate the V2 reference prototype first.");
            }

            EnsureFolders();
            Material groundMaterial = EnsureMaterial(
                "FreeDrive_Ground",
                new Color(0.055f, 0.075f, 0.085f),
                0.15f,
                0.7f);
            Material laneMaterial = EnsureMaterial(
                "FreeDrive_Lane",
                new Color(0.12f, 0.55f, 0.72f),
                0.25f,
                0.55f,
                new Color(0.08f, 0.65f, 1.1f));
            Material obstacleMaterial = EnsureMaterial(
                "FreeDrive_Obstacle",
                new Color(0.95f, 0.28f, 0.08f),
                0.05f,
                0.45f,
                new Color(0.5f, 0.05f, 0.01f));
            Material structureMaterial = EnsureMaterial(
                "FreeDrive_Structure",
                new Color(0.12f, 0.16f, 0.19f),
                0.65f,
                0.5f);
            AssetDatabase.SaveAssets();

            Scene previousActive = SceneManager.GetActiveScene();
            bool canGenerateAdditively =
                previousActive.IsValid() &&
                previousActive.isLoaded &&
                !string.IsNullOrEmpty(previousActive.path);
            if (!canGenerateAdditively &&
                !Application.isBatchMode &&
                previousActive.IsValid() &&
                previousActive.isDirty &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            canGenerateAdditively =
                previousActive.IsValid() &&
                previousActive.isLoaded &&
                !string.IsNullOrEmpty(previousActive.path);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                canGenerateAdditively
                    ? NewSceneMode.Additive
                    : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            try
            {
                ConfigureEnvironment();
                CreateLighting();
                CreateCourse(
                    groundMaterial,
                    laneMaterial,
                    obstacleMaterial,
                    structureMaterial);

                var spawnObject = new GameObject("Free Drive Spawn Point");
                spawnObject.transform.SetPositionAndRotation(
                    SpawnPosition,
                    Quaternion.identity);

                var assemblerInstance =
                    (GameObject)PrefabUtility.InstantiatePrefab(
                        assemblerPrefab,
                        scene);
                assemblerInstance.name = "Apex V3 Free Drive Assembler";
                assemblerInstance.transform.SetPositionAndRotation(
                    SpawnPosition,
                    Quaternion.identity);
                V3CraftAssembler assembler =
                    assemblerInstance.GetComponent<V3CraftAssembler>();

                var sessionObject = new GameObject("V3 Free Drive Session");
                V3FreeDriveSession session =
                    sessionObject.AddComponent<V3FreeDriveSession>();
                AssignObject(session, "assembler", assembler);
                AssignObject(session, "spawnPoint", spawnObject.transform);
                AssignInt(session, "trackSurfaceMask", 1 << 8);
                V3FreeDriveHud hud =
                    sessionObject.AddComponent<V3FreeDriveHud>();
                AssignObject(hud, "session", session);

                CreateCamera(session);

                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuildSettings();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (canGenerateAdditively &&
                    previousActive.IsValid() &&
                    previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"Hovercraft V3 free-drive scene generated: {ScenePath}");
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor =
                new Color(0.28f, 0.34f, 0.4f);
            RenderSettings.ambientEquatorColor =
                new Color(0.12f, 0.16f, 0.2f);
            RenderSettings.ambientGroundColor =
                new Color(0.035f, 0.045f, 0.055f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor =
                new Color(0.08f, 0.12f, 0.15f);
            RenderSettings.fogStartDistance = 350f;
            RenderSettings.fogEndDistance = 1100f;
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Free Drive Sun");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(0.92f, 0.96f, 1f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation =
                Quaternion.Euler(48f, -32f, 0f);

            var fillObject = new GameObject("Free Drive Fill Light");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.32f;
            fill.color = new Color(0.25f, 0.55f, 1f);
            fill.shadows = LightShadows.None;
            fillObject.transform.rotation =
                Quaternion.Euler(32f, 145f, 0f);
        }

        private static void CreateCamera(V3FreeDriveSession session)
        {
            var cameraObject = new GameObject("V3 Chase Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position =
                SpawnPosition + new Vector3(0f, 6f, -14f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 1800f;
            camera.fieldOfView = 68f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
            V3FreeDriveCamera chase =
                cameraObject.AddComponent<V3FreeDriveCamera>();
            AssignObject(chase, "session", session);
        }

        private static void CreateCourse(
            Material groundMaterial,
            Material laneMaterial,
            Material obstacleMaterial,
            Material structureMaterial)
        {
            var course = new GameObject("Free Drive Handling Course");

            CreateBlock(
                "TrackSurface Test Field",
                course.transform,
                new Vector3(0f, -0.5f, 100f),
                new Vector3(170f, 1f, 1040f),
                Quaternion.identity,
                8,
                groundMaterial,
                true);

            CreateBlock(
                "Left Boundary Rail",
                course.transform,
                new Vector3(-84f, 1.25f, 100f),
                new Vector3(2f, 2.5f, 1040f),
                Quaternion.identity,
                0,
                structureMaterial,
                true);
            CreateBlock(
                "Right Boundary Rail",
                course.transform,
                new Vector3(84f, 1.25f, 100f),
                new Vector3(2f, 2.5f, 1040f),
                Quaternion.identity,
                0,
                structureMaterial,
                true);

            for (int z = -330; z <= 500; z += 35)
            {
                CreateMarking(
                    $"Center Marking {z}",
                    course.transform,
                    new Vector3(0f, 0.018f, z),
                    new Vector3(0.35f, 0.035f, 12f),
                    laneMaterial);
            }

            CreateGate(
                "Start Gate",
                course.transform,
                new Vector3(0f, 0f, -325f),
                laneMaterial,
                structureMaterial);

            for (int i = 0; i < 6; i++)
            {
                float x = i % 2 == 0 ? -9f : 9f;
                CreatePylon(
                    $"Slalom Pylon {i + 1}",
                    course.transform,
                    new Vector3(x, 3f, -255f + i * 42f),
                    obstacleMaterial);
            }

            CreateGate(
                "Acceleration Gate",
                course.transform,
                new Vector3(0f, 0f, 20f),
                obstacleMaterial,
                structureMaterial);

            CreateBlock(
                "Left Chicane Wall",
                course.transform,
                new Vector3(-18f, 2f, 72f),
                new Vector3(34f, 4f, 3f),
                Quaternion.Euler(0f, 12f, 0f),
                0,
                obstacleMaterial,
                true);
            CreateBlock(
                "Right Chicane Wall",
                course.transform,
                new Vector3(18f, 2f, 126f),
                new Vector3(34f, 4f, 3f),
                Quaternion.Euler(0f, -12f, 0f),
                0,
                obstacleMaterial,
                true);

            CreateBlock(
                "TrackSurface Jump Ramp",
                course.transform,
                new Vector3(0f, 2.7f, 188f),
                new Vector3(18f, 1f, 30f),
                Quaternion.Euler(-11f, 0f, 0f),
                8,
                structureMaterial,
                true);

            CreateMarking(
                "Landing Zone",
                course.transform,
                new Vector3(0f, 0.02f, 250f),
                new Vector3(30f, 0.04f, 42f),
                laneMaterial);

            CreateGate(
                "Recovery Gate",
                course.transform,
                new Vector3(0f, 0f, 305f),
                laneMaterial,
                structureMaterial);

            CreateTerrainLaboratory(
                course.transform,
                groundMaterial,
                obstacleMaterial,
                structureMaterial);

            CreatePylon(
                "Precision Pylon Left",
                course.transform,
                new Vector3(-6f, 4f, 370f),
                obstacleMaterial,
                new Vector3(1.8f, 4f, 1.8f));
            CreatePylon(
                "Precision Pylon Right",
                course.transform,
                new Vector3(6f, 4f, 370f),
                obstacleMaterial,
                new Vector3(1.8f, 4f, 1.8f));

            CreateGate(
                "Finish Gate",
                course.transform,
                new Vector3(0f, 0f, 450f),
                obstacleMaterial,
                structureMaterial);
        }

        private static void CreateTerrainLaboratory(
            Transform parent,
            Material groundMaterial,
            Material obstacleMaterial,
            Material structureMaterial)
        {
            var laboratory = new GameObject("Terrain Stability Laboratory");
            laboratory.transform.SetParent(parent, false);

            const float bankLaneX = -48f;
            CreateBlock(
                "TrackSurface Banked Approach",
                laboratory.transform,
                new Vector3(bankLaneX, 1.45f, 280f),
                new Vector3(18f, 0.8f, 28f),
                Quaternion.Euler(-6.5f, 0f, 0f),
                8,
                structureMaterial,
                true);
            CreateBlock(
                "TrackSurface Banked Stability Deck",
                laboratory.transform,
                new Vector3(bankLaneX, 3f, 315f),
                new Vector3(18f, 0.8f, 42f),
                Quaternion.Euler(0f, 0f, -10f),
                8,
                groundMaterial,
                true);
            CreateBlock(
                "TrackSurface Banked Exit",
                laboratory.transform,
                new Vector3(bankLaneX, 1.45f, 350f),
                new Vector3(18f, 0.8f, 28f),
                Quaternion.Euler(6.5f, 0f, 0f),
                8,
                structureMaterial,
                true);

            const float washboardLaneX = 43f;
            for (int i = 0; i < 18; i++)
            {
                float height = i % 3 == 0
                    ? 0.9f
                    : i % 2 == 0
                        ? 0.55f
                        : 0.32f;
                CreateBlock(
                    $"TrackSurface Washboard Ridge {i + 1:00}",
                    laboratory.transform,
                    new Vector3(
                        washboardLaneX,
                        height * 0.5f,
                        258f + i * 4.2f),
                    new Vector3(18f, height, 1.25f),
                    Quaternion.identity,
                    8,
                    obstacleMaterial,
                    true);
            }

            for (int i = 0; i < 10; i++)
            {
                bool leftHigh = i % 2 == 0;
                float leftHeight = leftHigh ? 1.35f : 0.28f;
                float rightHeight = leftHigh ? 0.28f : 1.35f;
                float z = 362f + i * 7f;
                CreateBlock(
                    $"TrackSurface Articulation Left {i + 1:00}",
                    laboratory.transform,
                    new Vector3(
                        washboardLaneX - 3.6f,
                        leftHeight * 0.5f,
                        z),
                    new Vector3(5.2f, leftHeight, 5.4f),
                    Quaternion.Euler(0f, 0f, leftHigh ? -4f : 2f),
                    8,
                    structureMaterial,
                    true);
                CreateBlock(
                    $"TrackSurface Articulation Right {i + 1:00}",
                    laboratory.transform,
                    new Vector3(
                        washboardLaneX + 3.6f,
                        rightHeight * 0.5f,
                        z),
                    new Vector3(5.2f, rightHeight, 5.4f),
                    Quaternion.Euler(0f, 0f, leftHigh ? -2f : 4f),
                    8,
                    structureMaterial,
                    true);
            }

            CreateGate(
                "Terrain Laboratory Gate",
                laboratory.transform,
                new Vector3(0f, 0f, 455f),
                obstacleMaterial,
                structureMaterial);
        }

        private static void CreateGate(
            string name,
            Transform parent,
            Vector3 position,
            Material accent,
            Material structure)
        {
            var gate = new GameObject(name);
            gate.transform.SetParent(parent, false);
            gate.transform.position = position;
            CreateBlock(
                "Left Post",
                gate.transform,
                new Vector3(-17f, 5f, 0f),
                new Vector3(2f, 10f, 2f),
                Quaternion.identity,
                0,
                structure,
                true,
                true);
            CreateBlock(
                "Right Post",
                gate.transform,
                new Vector3(17f, 5f, 0f),
                new Vector3(2f, 10f, 2f),
                Quaternion.identity,
                0,
                structure,
                true,
                true);
            CreateBlock(
                "Top Beam",
                gate.transform,
                new Vector3(0f, 10f, 0f),
                new Vector3(36f, 1.2f, 2f),
                Quaternion.identity,
                0,
                accent,
                true,
                true);
        }

        private static void CreatePylon(
            string name,
            Transform parent,
            Vector3 position,
            Material material,
            Vector3? scale = null)
        {
            GameObject pylon =
                GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pylon.name = name;
            pylon.transform.SetParent(parent, false);
            pylon.transform.position = position;
            pylon.transform.localScale =
                scale ?? new Vector3(1.25f, 3f, 1.25f);
            pylon.GetComponent<Renderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(
                pylon,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic);
        }

        private static GameObject CreateBlock(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            int layer,
            Material material,
            bool isStatic,
            bool localPosition = false)
        {
            GameObject block =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            if (localPosition)
            {
                block.transform.localPosition = position;
                block.transform.localRotation = rotation;
            }
            else
            {
                block.transform.SetPositionAndRotation(position, rotation);
            }

            block.transform.localScale = scale;
            block.layer = layer;
            block.GetComponent<Renderer>().sharedMaterial = material;
            if (isStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    block,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic);
            }

            return block;
        }

        private static void CreateMarking(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject marking = CreateBlock(
                name,
                parent,
                position,
                scale,
                Quaternion.identity,
                0,
                material,
                true);
            Collider collider = marking.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            Renderer renderer = marking.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material EnsureMaterial(
            string name,
            Color baseColor,
            float metallic,
            float smoothness,
            Color? emission = null)
        {
            string path = $"{MaterialsPath}/{name}.mat";
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "No supported lit shader is available.");
                }

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", baseColor);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (emission.HasValue &&
                material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/HovercraftV3", "FreeDrive");
            EnsureFolder(FreeDriveRoot, "Scenes");
            EnsureFolder(FreeDriveRoot, "Materials");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void AddSceneToBuildSettings()
        {
            EditorBuildSettingsScene[] existing =
                EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(
                        existing[i].path,
                        ScenePath,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            var updated =
                new List<EditorBuildSettingsScene>(existing)
                {
                    new EditorBuildSettingsScene(ScenePath, true)
                };
            EditorBuildSettings.scenes = updated.ToArray();
        }

        private static void AssignObject(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name} is missing {propertyName}.");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignInt(
            UnityEngine.Object target,
            string propertyName,
            int value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name} is missing {propertyName}.");
            }

            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
