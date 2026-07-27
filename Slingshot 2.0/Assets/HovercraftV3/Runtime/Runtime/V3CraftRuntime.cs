using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3CraftRuntime : MonoBehaviour
    {
        private readonly List<RuntimePartInstance> installedParts = new List<RuntimePartInstance>();

        public CraftBuildDefinition Build { get; private set; }
        public Rigidbody RootRigidbody { get; private set; }
        public IReadOnlyList<RuntimePartInstance> InstalledParts => installedParts;
        public float CalculatedMassKg { get; private set; }
        public Vector3 CalculatedCenterOfMass { get; private set; }

        internal void Initialize(
            CraftBuildDefinition build,
            Rigidbody rootRigidbody,
            List<RuntimePartInstance> parts,
            float calculatedMassKg,
            Vector3 calculatedCenterOfMass)
        {
            Build = build;
            RootRigidbody = rootRigidbody;
            installedParts.Clear();
            installedParts.AddRange(parts);
            CalculatedMassKg = calculatedMassKg;
            CalculatedCenterOfMass = calculatedCenterOfMass;
        }
    }
}
