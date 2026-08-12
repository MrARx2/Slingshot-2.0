using System.Text.RegularExpressions;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class V3WorldSimulationTests
    {
        private static readonly string[] InstalledScenePaths =
        {
            "Assets/HovercraftV3/Scenes/Development/V3_CraftLab.unity",
            "Assets/HovercraftV3/Scenes/Development/V3_HandlingTrack.unity"
        };
        private GameObject rootObject;
        private V3WorldProfile profile;
        private Scene testScene;

        [SetUp]
        public void SetUp()
        {
            testScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            profile = ScriptableObject.CreateInstance<V3WorldProfile>();
            rootObject = new GameObject("World Simulation Root");
            SceneManager.MoveGameObjectToScene(rootObject, testScene);
            rootObject.SetActive(false);
            V3WorldSimulationRoot root =
                rootObject.AddComponent<V3WorldSimulationRoot>();
            root.Configure(profile, "world.root.tests");
            rootObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (rootObject != null)
            {
                Object.DestroyImmediate(rootObject);
            }
            if (profile != null)
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void EarthProfile_ProducesCoherentSeaLevelAndAltitudeSamples()
        {
            V3WorldSimulationRoot root =
                rootObject.GetComponent<V3WorldSimulationRoot>();
            root.SetClockState(1d, 10);
            Assert.That(root.TrySample(
                Vector3.zero,
                out V3WorldEnvironmentSample seaLevel), Is.True);
            Assert.That(seaLevel.IsValid, Is.True);
            Assert.That(seaLevel.GravityVector.y,
                Is.EqualTo(-9.80665f).Within(0.0001f));
            Assert.That(seaLevel.AmbientTemperatureC,
                Is.EqualTo(15f).Within(0.01f));
            Assert.That(seaLevel.AtmosphericPressurePa,
                Is.EqualTo(101325f).Within(1f));
            Assert.That(seaLevel.AirDensityKgPerCubicMeter,
                Is.EqualTo(1.225f).Within(0.002f));
            Assert.That(seaLevel.SpeedOfSoundMetersPerSecond,
                Is.EqualTo(340.3f).Within(0.5f));

            Assert.That(root.TrySample(
                new Vector3(0f, 1000f, 0f),
                out V3WorldEnvironmentSample altitude), Is.True);
            Assert.That(altitude.AmbientTemperatureC,
                Is.LessThan(seaLevel.AmbientTemperatureC));
            Assert.That(altitude.AtmosphericPressurePa,
                Is.LessThan(seaLevel.AtmosphericPressurePa));
            Assert.That(altitude.AirDensityKgPerCubicMeter,
                Is.LessThan(seaLevel.AirDensityKgPerCubicMeter));
            Assert.That(altitude.SpeedOfSoundMetersPerSecond,
                Is.LessThan(seaLevel.SpeedOfSoundMetersPerSecond));
        }

        [Test]
        public void WindGustAndTurbulence_AreDeterministicAndSmooth()
        {
            SetProfile("globalWindDirection", new Vector3(1f, 0f, 0f));
            SetProfile("globalWindSpeed", 20f);
            SetProfile("gustStrength", 6f);
            SetProfile("turbulenceStrength", 4f);
            SetProfile("deterministicWindSeed", 7123);
            V3WorldSimulationRoot root =
                rootObject.GetComponent<V3WorldSimulationRoot>();

            root.SetClockState(5d, 20);
            root.TrySample(new Vector3(12f, 3f, 40f), out var first);
            root.TrySample(new Vector3(12f, 3f, 40f), out var repeated);
            root.SetClockState(5.01d, 21);
            root.TrySample(new Vector3(12.1f, 3f, 40f), out var nearby);

            Assert.That(repeated.LocalAirVelocity, Is.EqualTo(first.LocalAirVelocity));
            Assert.That(first.BaseWindVelocity.x, Is.EqualTo(20f).Within(0.001f));
            Assert.That((nearby.LocalAirVelocity - first.LocalAirVelocity).magnitude,
                Is.LessThan(1f));
        }

        [Test]
        public void Zones_BlendAndHigherPriorityOverrideWins()
        {
            V3WorldEnvironmentZone additive = CreateZone(
                "zone.hot_blend",
                0,
                V3EnvironmentZoneOperation.Additive,
                30f);
            V3WorldSimulationRoot root =
                rootObject.GetComponent<V3WorldSimulationRoot>();
            root.ZoneRegistry.MarkDirty();
            root.SetClockState(0d, 1);

            root.TrySample(Vector3.zero, out var inside);
            root.TrySample(new Vector3(10f, 0f, 0f), out var blend);
            root.TrySample(new Vector3(16f, 0f, 0f), out var outside);
            Assert.That(inside.AmbientTemperatureC, Is.EqualTo(45f).Within(0.01f));
            Assert.That(blend.AmbientTemperatureC,
                Is.GreaterThan(outside.AmbientTemperatureC));
            Assert.That(blend.AmbientTemperatureC,
                Is.LessThan(inside.AmbientTemperatureC));
            Assert.That(outside.ActiveZoneCount, Is.Zero);

            V3WorldEnvironmentZone high = CreateZone(
                "zone.priority_override",
                100,
                V3EnvironmentZoneOperation.Override,
                5f);
            root.ZoneRegistry.MarkDirty();
            root.SetClockState(0d, 2);
            root.TrySample(Vector3.zero, out var priority);
            Assert.That(priority.AmbientTemperatureC,
                Is.EqualTo(5f).Within(0.01f));
            Assert.That(priority.DominantZoneId,
                Is.EqualTo("zone.priority_override"));
            Assert.That(priority.ActiveZoneCount, Is.EqualTo(2));

            Object.DestroyImmediate(additive.gameObject);
            Object.DestroyImmediate(high.gameObject);
        }

        [Test]
        public void QueryService_ReportsMissingAndDuplicateWorlds()
        {
            Object.DestroyImmediate(rootObject);
            rootObject = null;
            Assert.That(V3WorldQueryService.TrySample(
                testScene,
                Vector3.zero,
                out V3WorldEnvironmentSample missing), Is.False);
            Assert.That(
                (missing.Flags & V3WorldSampleFlags.MissingWorld) != 0,
                Is.True);

            rootObject = CreateConfiguredRoot("World A", "world.a");
            GameObject duplicate = new GameObject("World B");
            SceneManager.MoveGameObjectToScene(duplicate, testScene);
            duplicate.SetActive(false);
            V3WorldSimulationRoot second =
                duplicate.AddComponent<V3WorldSimulationRoot>();
            second.Configure(profile, "world.b");
            LogAssert.Expect(
                LogType.Error,
                new Regex("requires exactly one active World Simulation"));
            duplicate.SetActive(true);
            Assert.That(V3WorldQueryService.Resolve(
                testScene,
                out _),
                Is.EqualTo(V3WorldQueryStatus.DuplicateWorld));
            Object.DestroyImmediate(duplicate);
        }

        [Test]
        public void ThermalInput_UsesDensityAirspeedAndWorldMultiplier()
        {
            var low = new V3ThermalEnvironmentInput(
                15f, 0.5f, 0f, 1f, 100f, 4f);
            var fast = new V3ThermalEnvironmentInput(
                15f, 1f, 200f, 1.5f, 100f, 4f);
            Assert.That(fast.EffectiveCoolingMultiplier,
                Is.GreaterThan(low.EffectiveCoolingMultiplier));
            Assert.That(fast.AirflowMultiplier, Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void RelativeAirflow_HeadwindAndTailwindChangeDynamicPressure()
        {
            Vector3 groundVelocity = Vector3.forward * 100f;
            Vector3 headwind = Vector3.back * 20f;
            Vector3 tailwind = Vector3.forward * 20f;
            Vector3 headwindRelative =
                V3CraftAerodynamicsRuntime.CalculateRelativeAirVelocity(
                    groundVelocity,
                    headwind);
            Vector3 tailwindRelative =
                V3CraftAerodynamicsRuntime.CalculateRelativeAirVelocity(
                    groundVelocity,
                    tailwind);
            float headwindPressure =
                V3CraftAerodynamicsRuntime.CalculateDynamicPressure(
                    1.225f,
                    headwindRelative);
            float tailwindPressure =
                V3CraftAerodynamicsRuntime.CalculateDynamicPressure(
                    1.225f,
                    tailwindRelative);

            Assert.That(headwindRelative.z, Is.EqualTo(120f).Within(0.001f));
            Assert.That(tailwindRelative.z, Is.EqualTo(80f).Within(0.001f));
            Assert.That(headwindPressure, Is.GreaterThan(tailwindPressure));
        }

        [Test]
        public void CrosswindAndHotZones_BlendAirflowDensityAndCooling()
        {
            V3WorldEnvironmentZone crosswind = CreateZone(
                "zone.crosswind.acceptance",
                10,
                V3EnvironmentZoneOperation.None,
                0f);
            var windSettings = new SerializedObject(crosswind);
            windSettings.FindProperty("windOperation").enumValueIndex =
                (int)V3EnvironmentZoneOperation.Additive;
            windSettings.FindProperty("windVelocity").vector3Value =
                Vector3.right * 30f;
            windSettings.ApplyModifiedPropertiesWithoutUndo();

            V3WorldEnvironmentZone hot = CreateZone(
                "zone.hot.acceptance",
                20,
                V3EnvironmentZoneOperation.Additive,
                40f);
            hot.transform.position = Vector3.forward * 100f;
            V3WorldSimulationRoot root =
                rootObject.GetComponent<V3WorldSimulationRoot>();
            root.ZoneRegistry.MarkDirty();
            root.SetClockState(3d, 31);

            root.TrySample(Vector3.zero, out var crosswindInside);
            root.TrySample(new Vector3(10f, 0f, 0f),
                out var crosswindBlend);
            root.TrySample(new Vector3(14.9f, 0f, 0f),
                out var crosswindEdge);
            root.TrySample(new Vector3(15f, 0f, 0f),
                out var crosswindOutside);
            Assert.That(crosswindInside.LocalAirVelocity.x,
                Is.EqualTo(30f).Within(0.001f));
            Assert.That(crosswindBlend.LocalAirVelocity.x,
                Is.InRange(0.01f, 29.99f));
            Assert.That((crosswindEdge.LocalAirVelocity -
                         crosswindOutside.LocalAirVelocity).magnitude,
                Is.LessThan(0.1f));
            Assert.That(crosswindInside.DominantZoneId,
                Is.EqualTo("zone.crosswind.acceptance"));

            root.TrySample(Vector3.forward * 100f, out var hotInside);
            root.TrySample(Vector3.forward * 116f, out var hotOutside);
            Assert.That(hotInside.AmbientTemperatureC,
                Is.EqualTo(55f).Within(0.01f));
            Assert.That(hotInside.AirDensityKgPerCubicMeter,
                Is.LessThan(hotOutside.AirDensityKgPerCubicMeter));
            var insideCooling = new V3ThermalEnvironmentInput(
                hotInside.AmbientTemperatureC,
                hotInside.DensityRatio,
                100f,
                hotInside.EnvironmentalCoolingMultiplier,
                hotInside.CoolingReferenceAirspeed,
                hotInside.MaximumAirflowCoolingMultiplier);
            var outsideCooling = new V3ThermalEnvironmentInput(
                hotOutside.AmbientTemperatureC,
                hotOutside.DensityRatio,
                100f,
                hotOutside.EnvironmentalCoolingMultiplier,
                hotOutside.CoolingReferenceAirspeed,
                hotOutside.MaximumAirflowCoolingMultiplier);
            Assert.That(insideCooling.EffectiveCoolingMultiplier,
                Is.LessThan(outsideCooling.EffectiveCoolingMultiplier));

            Object.DestroyImmediate(crosswind.gameObject);
            Object.DestroyImmediate(hot.gameObject);
        }

        [Test]
        public void DifferentCraftInitializationTimes_ShareOneWorldClockAndFields()
        {
            SetProfile("globalWindDirection", new Vector3(0.4f, 0.1f, 1f));
            SetProfile("globalWindSpeed", 23f);
            SetProfile("gustStrength", 7f);
            SetProfile("turbulenceStrength", 5f);
            SetProfile("deterministicWindSeed", 9841);
            V3WorldEnvironmentZone sharedZone = CreateZone(
                "zone.shared.clock.proof",
                25,
                V3EnvironmentZoneOperation.Additive,
                12f);

            V3WorldSimulationRoot root =
                rootObject.GetComponent<V3WorldSimulationRoot>();
            root.ZoneRegistry.MarkDirty();
            var firstCraft = new GameObject("Craft initialized first");
            SceneManager.MoveGameObjectToScene(firstCraft, testScene);
            V3WorldEnvironmentProvider firstProvider =
                firstCraft.AddComponent<V3WorldEnvironmentProvider>();
            firstProvider.Initialize(null);

            root.SetClockState(19.25d, 200);
            var secondCraft = new GameObject("Craft initialized later");
            SceneManager.MoveGameObjectToScene(secondCraft, testScene);
            V3WorldEnvironmentProvider secondProvider =
                secondCraft.AddComponent<V3WorldEnvironmentProvider>();
            secondProvider.Initialize(null);

            root.SetClockState(87.5d, 901);
            Vector3 sharedPosition = Vector3.zero;
            Assert.That(firstProvider.BeginPhysicsTick(sharedPosition, 1504),
                Is.True);
            Assert.That(secondProvider.BeginPhysicsTick(sharedPosition, 3),
                Is.True);

            V3WorldEnvironmentSample first = firstProvider.CurrentSample;
            V3WorldEnvironmentSample second = secondProvider.CurrentSample;
            Assert.That(firstProvider.CurrentCraftPhysicsTickId,
                Is.EqualTo(1504));
            Assert.That(secondProvider.CurrentCraftPhysicsTickId,
                Is.EqualTo(3));
            Assert.That(first.PhysicsTickId, Is.EqualTo(901));
            Assert.That(second.PhysicsTickId, Is.EqualTo(901));
            Assert.That(first.SimulationTime, Is.EqualTo(87.5d));
            Assert.That(second.SimulationTime, Is.EqualTo(87.5d));
            Assert.That(second.BaseWindVelocity, Is.EqualTo(first.BaseWindVelocity));
            Assert.That(second.GustVelocity, Is.EqualTo(first.GustVelocity));
            Assert.That(second.TurbulenceVelocity,
                Is.EqualTo(first.TurbulenceVelocity));
            Assert.That(second.LocalAirVelocity, Is.EqualTo(first.LocalAirVelocity));
            Assert.That(second.AmbientTemperatureC,
                Is.EqualTo(first.AmbientTemperatureC));
            Assert.That(second.AtmosphericPressurePa,
                Is.EqualTo(first.AtmosphericPressurePa));
            Assert.That(second.AirDensityKgPerCubicMeter,
                Is.EqualTo(first.AirDensityKgPerCubicMeter));
            Assert.That(second.ActiveZoneCount, Is.EqualTo(first.ActiveZoneCount));
            Assert.That(second.DominantZoneId, Is.EqualTo(first.DominantZoneId));
            Assert.That(second.DominantZoneWeight,
                Is.EqualTo(first.DominantZoneWeight));
            Assert.That(second.Zone0Id, Is.EqualTo(first.Zone0Id));
            Assert.That(second.Zone0Weight, Is.EqualTo(first.Zone0Weight));
            Assert.That(second.AppliedZoneOperations,
                Is.EqualTo(first.AppliedZoneOperations));
            Assert.That(first.DominantZoneId,
                Is.EqualTo("zone.shared.clock.proof"));

            Object.DestroyImmediate(firstCraft);
            Object.DestroyImmediate(secondCraft);
            Object.DestroyImmediate(sharedZone.gameObject);
        }

        [Test]
        public void DevelopmentScenes_HaveExactlyOneConfiguredWorldRoot()
        {
            for (int i = 0; i < InstalledScenePaths.Length; i++)
            {
                Scene existing = SceneManager.GetSceneByPath(
                    InstalledScenePaths[i]);
                bool openedByTest = !existing.isLoaded;
                Scene scene = openedByTest
                    ? EditorSceneManager.OpenScene(
                        InstalledScenePaths[i],
                        OpenSceneMode.Additive)
                    : existing;
                try
                {
                    V3WorldSimulationRoot[] roots =
                        ComponentsInScene<V3WorldSimulationRoot>(scene);
                    V3WorldEnvironmentZone[] zones =
                        ComponentsInScene<V3WorldEnvironmentZone>(scene);
                    Assert.That(roots, Has.Length.EqualTo(1),
                        InstalledScenePaths[i]);
                    Assert.That(roots[0].Profile, Is.Not.Null,
                        InstalledScenePaths[i]);
                    Assert.That(zones, Has.Length.EqualTo(3),
                        InstalledScenePaths[i]);
                }
                finally
                {
                    if (openedByTest)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }

        private V3WorldEnvironmentZone CreateZone(
            string id,
            int priority,
            V3EnvironmentZoneOperation temperatureOperation,
            float temperatureValue)
        {
            var go = new GameObject(id);
            SceneManager.MoveGameObjectToScene(go, testScene);
            go.SetActive(false);
            V3WorldEnvironmentZone zone =
                go.AddComponent<V3WorldEnvironmentZone>();
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("stableId").stringValue = id;
            serialized.FindProperty("priority").intValue = priority;
            serialized.FindProperty("boxSize").vector3Value =
                new Vector3(10f, 10f, 10f);
            serialized.FindProperty("blendDistance").floatValue = 10f;
            serialized.FindProperty("temperatureOperation").enumValueIndex =
                (int)temperatureOperation;
            serialized.FindProperty("temperatureValue").floatValue =
                temperatureValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);
            return zone;
        }

        private GameObject CreateConfiguredRoot(string name, string id)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, testScene);
            go.SetActive(false);
            V3WorldSimulationRoot root =
                go.AddComponent<V3WorldSimulationRoot>();
            root.Configure(profile, id);
            go.SetActive(true);
            return go;
        }

        private void SetProfile(string propertyName, Vector3 value)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty(propertyName).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetProfile(string propertyName, float value)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetProfile(string propertyName, int value)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty(propertyName).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T[] ComponentsInScene<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
