using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3.Editor
{
    public static class V3ParityHarnessGenerator
    {
        public const string ParityRoot = "Assets/HovercraftV3/Parity";
        public const string ProfilePath =
            ParityRoot + "/Profiles/V2_V3_Parity_Command_Profile.asset";
        public const string ScenePath =
            ParityRoot + "/Scenes/V2_V3_FlatGround_Parity.unity";

        private const string V2PrefabPath = "Assets/Prefabs/HovercraftRootV2.prefab";
        private const string V3AssemblerPrefabPath =
            V2ReferencePrototypeGenerator.PrototypeRoot +
            "/Prefabs/ApexV3_V2Reference_Assembler.prefab";
        private const float V2ReferenceMassKg = 11000f;

        [MenuItem("Tools/Hovercraft V3/Generate Gate 3 Parity Harness")]
        public static void Generate()
        {
            EnsureFolder("Assets/HovercraftV3", "Parity");
            EnsureFolder(ParityRoot, "Profiles");
            EnsureFolder(ParityRoot, "Scenes");

            V3ParityCommandProfile profile =
                AssetDatabase.LoadAssetAtPath<V3ParityCommandProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<V3ParityCommandProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.SetSegments(CreateReferenceSegments());
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            profile =
                AssetDatabase.LoadAssetAtPath<V3ParityCommandProfile>(ProfilePath);
            if (profile == null)
            {
                throw new InvalidOperationException(
                    $"Parity command profile failed to reload: {ProfilePath}");
            }

            CreateGround();
            CreateLightingAndCamera();

            GameObject v2Root = CreateV2Reference(scene);
            V3CraftAssembler v3Assembler = CreateV3Reference(scene);

            var managerObject = new GameObject("Gate 3 Parity Run Controller");
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            V3ParityRunController controller =
                managerObject.AddComponent<V3ParityRunController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("profile").objectReferenceValue = profile;
            serialized.FindProperty("v2CraftRoot").objectReferenceValue = v2Root;
            serialized.FindProperty("v3Assembler").objectReferenceValue = v3Assembler;
            serialized.FindProperty("autoRun").boolValue = true;
            serialized.FindProperty("requireV2ReferenceFingerprint").boolValue =
                v2Root != null;
            serialized.FindProperty("exportAutomatically").boolValue = true;
            serialized.FindProperty("trackSurfaceMask").intValue = 1 << 8;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"Hovercraft V3 Gate 3 parity harness generated: {ScenePath}. " +
                (v2Root != null
                    ? "V2 reference clone was normalized from the Rev1.1 physical baseline."
                    : "V2 prefab was unavailable, so this scene currently records V3 only."));
        }

        public static void GenerateFromCommandLine()
        {
            Generate();
        }

        private static List<V3ParityCommandSegment> CreateReferenceSegments()
        {
            V3PilotCommand neutral = Stabilized();
            return new List<V3ParityCommandSegment>
            {
                new V3ParityCommandSegment(
                    "Settle",
                    5f,
                    neutral,
                    true),
                new V3ParityCommandSegment(
                    "Idle Hover",
                    20f,
                    neutral),
                new V3ParityCommandSegment(
                    "Acceleration",
                    30f,
                    Command(throttle: 1f),
                    true),
                new V3ParityCommandSegment(
                    "Brake From 500",
                    8f,
                    Command(throttle: -1f),
                    true,
                    500f),
                new V3ParityCommandSegment(
                    "Yaw Pulse 500",
                    4f,
                    Command(yaw: 0.65f),
                    true,
                    500f),
                new V3ParityCommandSegment(
                    "Yaw Pulse 1000",
                    4f,
                    Command(yaw: 0.65f),
                    true,
                    1000f),
                new V3ParityCommandSegment(
                    "Strafe Pulse 500",
                    4f,
                    Command(strafe: 1f),
                    true,
                    500f),
                new V3ParityCommandSegment(
                    "Pitch Pulse",
                    3f,
                    Command(pitch: 0.65f),
                    true)
            };
        }

        private static V3PilotCommand Stabilized()
        {
            return new V3PilotCommand { StabilizationEnabled = true };
        }

        private static V3PilotCommand Command(
            float throttle = 0f,
            float strafe = 0f,
            float yaw = 0f,
            float pitch = 0f)
        {
            return new V3PilotCommand
            {
                Throttle = throttle,
                Strafe = strafe,
                Yaw = yaw,
                Pitch = pitch,
                StabilizationEnabled = true
            };
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TrackSurface Flat Ground";
            ground.layer = 8;
            // The preset-speed maneuver segments can cover hundreds of metres
            // sideways, while the 30-second acceleration baseline covers many
            // kilometres. Keep every sample on the same surface so leaving the
            // collider cannot masquerade as a handling difference.
            ground.transform.position = new Vector3(0f, -0.5f, 24000f);
            ground.transform.localScale = new Vector3(10000f, 1f, 50000f);
        }

        private static void CreateLightingAndCamera()
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            var cameraObject = new GameObject("Parity Overview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 18f, -28f);
            cameraObject.transform.rotation = Quaternion.Euler(22f, 0f, 0f);
            camera.farClipPlane = 6000f;
        }

        private static GameObject CreateV2Reference(Scene scene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(V2PrefabPath);
            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "V2 Reference (Runtime Clone)";
            instance.transform.SetPositionAndRotation(
                new Vector3(-8f, 2.8f, 0f),
                Quaternion.identity);
            ConfigureV2ReferenceClone(instance);
            return instance;
        }

        private static V3CraftAssembler CreateV3Reference(Scene scene)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(V3AssemblerPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing V3 assembler prefab: {V3AssemblerPrefabPath}. " +
                    "Generate the V2 reference prototype first.");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "V3 Reference Assembler";
            instance.transform.SetPositionAndRotation(
                new Vector3(8f, 2.8f, 0f),
                Quaternion.identity);
            return instance.GetComponent<V3CraftAssembler>();
        }

        private static void ConfigureV2ReferenceClone(GameObject root)
        {
            Rigidbody body = root.GetComponent<Rigidbody>();
            body.mass = V2ReferenceMassKg;
            body.linearDamping = 0f;
            body.angularDamping = 2f;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 50f;
            body.centerOfMass = new Vector3(0f, -0.5f, 0f);

            BoxCollider collider = root.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.size = new Vector3(3.6f, 1.55f, 7.8f);
            }

            ConfigureThruster(root, "Hover_FL", new Vector3(-1.5f, -0.725f, 3f), 160000f, true);
            ConfigureThruster(root, "Hover_FR", new Vector3(1.5f, -0.725f, 3f), 160000f, true);
            ConfigureThruster(root, "Hover_RL", new Vector3(-1.5f, -0.725f, -3f), 160000f, true);
            ConfigureThruster(root, "Hover_RR", new Vector3(1.5f, -0.725f, -3f), 160000f, true);
            ConfigureThruster(root, "Roof_FL", new Vector3(-1.5f, -0.665f, 3f), 160000f, true);
            ConfigureThruster(root, "Roof_FR", new Vector3(1.5f, -0.665f, 3f), 160000f, true);
            ConfigureThruster(root, "Roof_RL", new Vector3(-1.5f, -0.665f, -3f), 160000f, true);
            ConfigureThruster(root, "Roof_RR", new Vector3(1.5f, -0.665f, -3f), 160000f, true);
            ConfigureThruster(root, "Main_Rear", new Vector3(0f, 0f, -3.9f), 480000f, false);
            ConfigureThruster(root, "Brake_Front", new Vector3(0f, 0f, 3.9f), 380000f, false);
            ConfigureThruster(root, "Strafe_Front_Left", new Vector3(-1.8f, 0f, 3f), 160000f, false);
            ConfigureThruster(root, "Strafe_Front_Right", new Vector3(1.8f, 0f, 3f), 160000f, false);
            ConfigureThruster(root, "Strafe_Back_Left", new Vector3(-1.8f, 0f, -3f), 160000f, false);
            ConfigureThruster(root, "Strafe_Back_Right", new Vector3(1.8f, 0f, -3f), 160000f, false);

            SetFloat(root, "CraftCore", "extraGravity", 0f);
            SetFloat(root, "EnergyCore", "totalPowerOutput", 4500f);
            // V2 stores force as acceleration. Multiplying the JSON N-cost by
            // reference mass preserves the configured power draw after N/kg conversion.
            SetFloat(root, "EnergyCore", "powerCostPerForce", 11f);
            SetBool(root, "EnergyCore", "protectBaseHover", true);
            SetFloat(root, "EnergyCore", "vectoringPriorityBias", 1.5f);

            SetFloat(root, "HoverStabilizerArray", "hoverHeight", 2.8f);
            SetFloat(root, "HoverStabilizerArray", "hoverKp", 2.5f);
            SetFloat(root, "HoverStabilizerArray", "hoverKd", 0.3f);
            SetFloat(root, "HoverStabilizerArray", "hoverMaxThrottle", 5f);
            SetFloat(root, "HoverStabilizerArray", "hoverThrusterResponseSpeed", 24f);
            SetFloat(root, "HoverStabilizerArray", "hoverThrottleFallResponseSpeed", 30f);
            SetFloat(root, "HoverStabilizerArray", "airborneHoverThrottleDecaySpeed", 40f);
            SetFloat(root, "HoverStabilizerArray", "landingDampingMultiplier", 3.5f);
            SetFloat(root, "HoverStabilizerArray", "reboundDampingMultiplier", 5f);
            SetFloat(root, "HoverStabilizerArray", "hardLandingSpeedForMaxDamping", 18f);
            SetFloat(root, "HoverStabilizerArray", "hardLandingExtraDamping", 2.5f);
            SetFloat(root, "HoverStabilizerArray", "maxCushionRecoverySpeed", 3.5f);
            SetFloat(root, "HoverStabilizerArray", "emergencyCatchStartSpeed", 8f);
            SetFloat(root, "HoverStabilizerArray", "emergencyCatchFullSpeed", 22f);
            SetFloat(root, "HoverStabilizerArray", "stiffenStartSpeed", 80f);
            SetFloat(root, "HoverStabilizerArray", "stiffenFullSpeed", 600f);
            SetFloat(root, "HoverStabilizerArray", "maxSpeedStiffness", 3.5f);
            SetBool(root, "HoverStabilizerArray", "alignToSurface", true);
            SetFloat(root, "HoverStabilizerArray", "surfaceAlignStrength", 4f);
            SetFloat(root, "HoverStabilizerArray", "surfaceAlignDamping", 0.8f);
            SetFloat(root, "HoverStabilizerArray", "maxSurfaceAlignTorque", 12f);
            SetFloat(root, "HoverStabilizerArray", "surfaceLookAheadTime", 0.25f);
            SetFloat(root, "HoverStabilizerArray", "maxSurfaceLookAheadDistance", 80f);
            SetLayerMask(root, "HoverStabilizerArray", "surfaceProbeLayers", 1 << 8);

            // Current V2 exposes a single rise/fall ramp. Use the authoritative
            // rise value and report this limitation in the parity output.
            SetFloat(root, "DriveCore", "throttleRampSpeed", 4.5f);
            SetFloat(root, "VectorThrusterArray", "steerSensitivity", 0.24f);
            SetFloat(root, "VectorThrusterArray", "steeringYawAuthority", 2.8f);
            SetBool(root, "VectorThrusterArray", "edgeShiftCanAssistYaw", false);
            SetBool(root, "VectorThrusterArray", "edgeShiftCanFireStrafeThrusters", true);
            SetFloat(root, "VectorThrusterArray", "strafeSensitivity", 0.9f);
            SetFloat(root, "VectorThrusterArray", "lateralAssistAuthority", 1f);
            SetFloat(root, "VectorThrusterArray", "yawDamping", 3.2f);
            SetFloat(root, "VectorThrusterArray", "gripBreakerYawDampingMultiplier", 0.25f);

            SetFloat(root, "TractionCore", "lateralGrip", 8f);
            SetFloat(root, "TractionCore", "longitudinalGrip", 7f);
            SetFloat(root, "TractionCore", "coastingGrip", 0.3f);
            SetFloat(root, "TractionCore", "maxGripAcceleration", 120f);
            SetFloat(root, "TractionCore", "driftSpeedConservation", 0.2f);
        }

        private static void ConfigureThruster(
            GameObject root,
            string name,
            Vector3 localPosition,
            float forceN,
            bool probesGround)
        {
            Transform child = FindChild(root.transform, name);
            if (child == null)
            {
                return;
            }

            child.localPosition = localPosition;
            MonoBehaviour node = FindComponentByTypeName(child.gameObject, "ThrusterNode");
            if (node == null)
            {
                return;
            }

            var serialized = new SerializedObject(node);
            SetFloat(serialized, "maxForce", forceN / V2ReferenceMassKg);
            SetFloat(serialized, "efficiency", 1f);
            SetFloat(serialized, "powerDrawMultiplier", 1f);
            if (probesGround)
            {
                SetFloat(serialized, "groundDetectionRange", 7f);
                SerializedProperty layers = serialized.FindProperty("groundLayers");
                if (layers != null)
                {
                    layers.intValue = 1 << 8;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(
            GameObject root,
            string typeName,
            string propertyName,
            float value)
        {
            MonoBehaviour component = FindComponentByTypeName(root, typeName);
            if (component == null)
            {
                return;
            }

            var serialized = new SerializedObject(component);
            SetFloat(serialized, propertyName, value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(
            SerializedObject serialized,
            string propertyName,
            float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetBool(
            GameObject root,
            string typeName,
            string propertyName,
            bool value)
        {
            MonoBehaviour component = FindComponentByTypeName(root, typeName);
            if (component == null)
            {
                return;
            }

            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetLayerMask(
            GameObject root,
            string typeName,
            string propertyName,
            int value)
        {
            MonoBehaviour component = FindComponentByTypeName(root, typeName);
            if (component == null)
            {
                return;
            }

            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static MonoBehaviour FindComponentByTypeName(
            GameObject root,
            string typeName)
        {
            MonoBehaviour[] components =
                root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    string.Equals(
                        components[i].GetType().Name,
                        typeName,
                        StringComparison.Ordinal))
                {
                    return components[i];
                }
            }

            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, name, StringComparison.Ordinal))
                {
                    return transforms[i];
                }
            }

            return null;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
