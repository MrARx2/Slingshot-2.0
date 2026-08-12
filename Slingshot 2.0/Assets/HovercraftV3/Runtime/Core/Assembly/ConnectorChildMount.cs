using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class ConnectorChildMount : MonoBehaviour
    {
        [SerializeField] private Transform mountTransform;

        public Transform MountTransform => mountTransform != null ? mountTransform : transform;
    }
}
