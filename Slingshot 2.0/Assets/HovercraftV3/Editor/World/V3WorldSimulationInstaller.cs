using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V3WorldSimulationInstaller
    {
        public const string ProfilePath =
            "Assets/HovercraftV3/Content/World/EarthStandard.asset";
        public const string PrefabPath =
            "Assets/HovercraftV3/Generated/World/" +
            "V3WorldSimulationRoot.prefab";
        private static readonly string[] ScenePaths =
        {
            "Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity",
            "Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity"
        };

        [MenuItem("Tools/Hovercraft V3/World/Install World Simulation")]
        public static void InstallAll()
        {
            EnsureFolders();
            V3WorldProfile profile = EnsureEarthProfile();
            PersistChassisAerodynamicDefaults();
            EnsureRootPrefab(profile);
            for (int i = 0; i < ScenePaths.Length; i++)
            {
                if (!System.IO.File.Exists(ScenePaths[i]))
                {
                    continue;
                }
                Scene scene = EditorSceneManager.OpenScene(
                    ScenePaths[i],
                    OpenSceneMode.Single);
                InstallIntoScene(scene, profile);
                EditorSceneManager.SaveScene(scene, ScenePaths[i]);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static V3WorldProfile EnsureEarthProfile()
        {
            EnsureFolders();
            V3WorldProfile profile =
                AssetDatabase.LoadAssetAtPath<V3WorldProfile>(ProfilePath);
            if (profile != null)
            {
                return profile;
            }
            profile = ScriptableObject.CreateInstance<V3WorldProfile>();
            profile.name = "Earth Standard";
            AssetDatabase.CreateAsset(profile, ProfilePath);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        public static void InstallIntoScene(
            Scene scene,
            V3WorldProfile profile = null,
            string stableRootId = null)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("A valid scene is required.", nameof(scene));
            }
            profile ??= EnsureEarthProfile();
            GameObject rootObject = FindRoot(scene, "WorldSimulationRoot");
            if (rootObject == null)
            {
                rootObject = new GameObject("WorldSimulationRoot");
                SceneManager.MoveGameObjectToScene(rootObject, scene);
            }
            rootObject.SetActive(false);
            V3WorldSimulationRoot root =
                rootObject.GetComponent<V3WorldSimulationRoot>();
            if (root == null)
            {
                root = rootObject.AddComponent<V3WorldSimulationRoot>();
            }
            root.Configure(
                profile,
                string.IsNullOrWhiteSpace(stableRootId)
                    ? "world.root." + scene.name.ToLowerInvariant()
                    : stableRootId);
            EnsureTestZones(rootObject.transform);
            rootObject.SetActive(true);
            root.ZoneRegistry.Refresh();
            EditorUtility.SetDirty(rootObject);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void EnsureRootPrefab(V3WorldProfile profile)
        {
            var host = new GameObject("WorldSimulationRoot");
            host.SetActive(false);
            try
            {
                V3WorldSimulationRoot root =
                    host.AddComponent<V3WorldSimulationRoot>();
                root.Configure(profile, "world.root.default");
                host.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(host, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void EnsureTestZones(Transform root)
        {
            Transform container = root.Find("EnvironmentZones");
            if (container == null)
            {
                var go = new GameObject("EnvironmentZones");
                go.transform.SetParent(root, false);
                container = go.transform;
            }

            ConfigureZone(
                EnsureZone(container, "Tunnel Wind Shelter"),
                "zone.tunnel_wind_shelter",
                new Vector3(0f, 12f, 220f),
                new Vector3(90f, 30f, 100f),
                10,
                V3EnvironmentZoneOperation.Multiplier,
                Vector3.zero,
                0.2f,
                V3EnvironmentZoneOperation.None,
                0f,
                V3EnvironmentZoneOperation.Multiplier,
                0.25f);
            ConfigureZone(
                EnsureZone(container, "Crosswind Test Zone"),
                "zone.crosswind_test",
                new Vector3(0f, 15f, 430f),
                new Vector3(120f, 40f, 120f),
                20,
                V3EnvironmentZoneOperation.Additive,
                new Vector3(22f, 0f, 0f),
                1f,
                V3EnvironmentZoneOperation.None,
                0f,
                V3EnvironmentZoneOperation.Additive,
                0.35f);
            ConfigureZone(
                EnsureZone(container, "Hot Thermal Test Zone"),
                "zone.hot_thermal_test",
                new Vector3(0f, 15f, 650f),
                new Vector3(120f, 40f, 120f),
                30,
                V3EnvironmentZoneOperation.None,
                Vector3.zero,
                1f,
                V3EnvironmentZoneOperation.Additive,
                35f,
                V3EnvironmentZoneOperation.None,
                0f);
        }

        private static V3WorldEnvironmentZone EnsureZone(
            Transform parent,
            string name)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject(name);
            go.transform.SetParent(parent, false);
            V3WorldEnvironmentZone zone =
                go.GetComponent<V3WorldEnvironmentZone>();
            return zone != null
                ? zone
                : go.AddComponent<V3WorldEnvironmentZone>();
        }

        private static void ConfigureZone(
            V3WorldEnvironmentZone zone,
            string id,
            Vector3 position,
            Vector3 size,
            int priority,
            V3EnvironmentZoneOperation windOperation,
            Vector3 windVelocity,
            float windMultiplier,
            V3EnvironmentZoneOperation temperatureOperation,
            float temperatureValue,
            V3EnvironmentZoneOperation turbulenceOperation,
            float turbulenceValue)
        {
            zone.transform.localPosition = position;
            zone.transform.localRotation = Quaternion.identity;
            zone.transform.localScale = Vector3.one;
            var serialized = new SerializedObject(zone);
            Set(serialized, "stableId", id);
            Set(serialized, "priority", priority);
            Set(serialized, "boxSize", size);
            Set(serialized, "blendDistance", 20f);
            Set(serialized, "windOperation", (int)windOperation);
            Set(serialized, "windVelocity", windVelocity);
            Set(serialized, "windMultiplier", windMultiplier);
            Set(serialized, "temperatureOperation", (int)temperatureOperation);
            Set(serialized, "temperatureValue", temperatureValue);
            Set(serialized, "turbulenceOperation", (int)turbulenceOperation);
            Set(serialized, "turbulenceValue", turbulenceValue);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(zone);
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }
            return null;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/HovercraftV3/Content", "World");
            EnsureFolder("Assets/HovercraftV3/Generated", "World");
        }

        private static void PersistChassisAerodynamicDefaults()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:ChassisDefinition",
                new[] { "Assets/HovercraftV3" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                ChassisDefinition chassis =
                    AssetDatabase.LoadAssetAtPath<ChassisDefinition>(path);
                if (chassis != null)
                {
                    EditorUtility.SetDirty(chassis);
                }
            }
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static void Set(SerializedObject target, string name, string value)
        {
            target.FindProperty(name).stringValue = value;
        }

        private static void Set(SerializedObject target, string name, int value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property.propertyType == SerializedPropertyType.Enum)
            {
                property.enumValueIndex = value;
            }
            else
            {
                property.intValue = value;
            }
        }

        private static void Set(SerializedObject target, string name, float value)
        {
            target.FindProperty(name).floatValue = value;
        }

        private static void Set(
            SerializedObject target,
            string name,
            Vector3 value)
        {
            target.FindProperty(name).vector3Value = value;
        }
    }
}
