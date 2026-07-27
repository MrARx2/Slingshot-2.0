using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Tests
{
    public sealed class CraftBuildValidatorTests
    {
        private GameObject socketObject;
        private V3Socket socket;
        private ThrusterDefinition thruster;
        private FixedAdapterDefinition connector;

        [SetUp]
        public void SetUp()
        {
            socketObject = new GameObject("Test Socket");
            socket = socketObject.AddComponent<V3Socket>();
            SetEnum(socket, "family", SocketFamily.HeavyPropulsion);
            SetEnum(socket, "size", PartSize.Large);
            SetFloat(socket, "maximumSupportedMassKg", 500f);
            SetFloat(socket, "maximumSupportedForceN", 500000f);
            SetFloat(socket, "powerAvailability", 1000f);
            SetBool(socket, "hasPower", true);
            SetBool(socket, "hasData", true);
            SetEnumArray(socket, "allowedDirectEndpoints", (int)EndpointCategory.Thruster);
            SetEnumArray(socket, "allowedConnectors", (int)ConnectorKind.FixedAdapter);

            thruster = ScriptableObject.CreateInstance<ThrusterDefinition>();
            SetString(thruster, "stableId", "Thruster.Test");
            SetEnum(thruster, "size", PartSize.Medium);
            SetEnumArray(thruster, "compatibleParentFamilies", (int)SocketFamily.HeavyPropulsion);
            SetPhysical(thruster, 100f, 400000f);
            SetFloat(thruster, "maximumForwardForceN", 400000f);

            connector = ScriptableObject.CreateInstance<FixedAdapterDefinition>();
            SetString(connector, "stableId", "Connector.Test");
            SetEnum(connector, "size", PartSize.Medium);
            SetEnumArray(connector, "compatibleParentFamilies", (int)SocketFamily.HeavyPropulsion);
            SetPhysical(connector, 20f, 500000f);
            SetEnum(connector, "kind", ConnectorKind.FixedAdapter);
            SetEnum(connector, "childSocketFamily", SocketFamily.HeavyPropulsion);
            SetEnum(connector, "childSize", PartSize.Medium);
            SetEnumArray(connector, "allowedEndpointCategories", (int)EndpointCategory.Thruster);
            SetFloat(connector, "supportedEndpointMassKg", 200f);
            SetFloat(connector, "supportedEndpointForceN", 500000f);
            SetBool(connector, "childHasPower", true);
            SetBool(connector, "childHasData", true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(socketObject);
            Object.DestroyImmediate(thruster);
            Object.DestroyImmediate(connector);
        }

        [Test]
        public void DirectEndpoint_WithMatchingLimits_IsCompatible()
        {
            bool valid = CraftBuildValidator.TryValidateEndpoint(
                socket,
                null,
                thruster,
                out string error);

            Assert.That(valid, Is.True, error);
        }

        [Test]
        public void DirectEndpoint_OverForceLimit_IsRejected()
        {
            SetFloat(thruster, "maximumForwardForceN", 600000f);

            bool valid = CraftBuildValidator.TryValidateEndpoint(
                socket,
                null,
                thruster,
                out string error);

            Assert.That(valid, Is.False);
            StringAssert.Contains("exceeds", error);
        }

        [Test]
        public void ConnectorChain_WithCompatibleChild_IsCompatible()
        {
            bool valid = CraftBuildValidator.TryValidateChain(
                socket,
                connector,
                thruster,
                out string error);

            Assert.That(valid, Is.True, error);
        }

        [Test]
        public void ConnectorChain_OverChildMassLimit_IsRejected()
        {
            SetPhysical(thruster, 250f, 400000f);

            bool valid = CraftBuildValidator.TryValidateEndpoint(
                socket,
                connector,
                thruster,
                out string error);

            Assert.That(valid, Is.False);
            StringAssert.Contains("mass", error);
        }

        [Test]
        public void ConnectorChain_CombinedMassOverParentLimit_IsRejected()
        {
            SetPhysical(connector, 150f, 500000f);
            SetFloat(connector, "supportedEndpointMassKg", 450f);
            SetPhysical(thruster, 400f, 400000f);

            bool valid = CraftBuildValidator.TryValidateChain(
                socket,
                connector,
                thruster,
                out string error);

            Assert.That(valid, Is.False);
            StringAssert.Contains("chain mass", error);
        }

        private static void SetPhysical(Object target, float massKg, float maximumForceN)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty physical = serialized.FindProperty("physical");
            physical.FindPropertyRelative("massKg").floatValue = massKg;
            physical.FindPropertyRelative("maximumSupportedForceN").floatValue = maximumForceN;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string property, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string property, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum<T>(Object target, string property, T value) where T : System.Enum
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).enumValueIndex = System.Convert.ToInt32(value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnumArray(Object target, string property, params int[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(property);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).enumValueIndex = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
