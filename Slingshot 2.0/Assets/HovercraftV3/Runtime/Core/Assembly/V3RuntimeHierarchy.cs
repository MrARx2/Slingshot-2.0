using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3CraftIdentity : MonoBehaviour
    {
        public string StableBuildId { get; private set; } = string.Empty;
        public string DisplayName { get; private set; } = string.Empty;
        public string Manufacturer { get; private set; } = string.Empty;

        public void Initialize(CraftBuildDefinition build)
        {
            StableBuildId = build != null ? build.StableId : string.Empty;
            DisplayName = build != null ? build.DisplayName : string.Empty;
            Manufacturer =
                build != null ? build.ManufacturerName : string.Empty;
        }
    }

    [DisallowMultipleComponent]
    public sealed class V3RuntimeOwnershipMarker : MonoBehaviour
    {
        public string Responsibility { get; private set; } = string.Empty;
        public Component RuntimeOwner { get; private set; }

        public void Initialize(
            string responsibility,
            Component runtimeOwner)
        {
            Responsibility = responsibility ?? string.Empty;
            RuntimeOwner = runtimeOwner;
        }
    }

    [DisallowMultipleComponent]
    public sealed class V3RuntimeHierarchy : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> groups =
            new Dictionary<string, Transform>(
                System.StringComparer.Ordinal);

        public Transform Chassis { get; private set; }
        public Transform Electronics { get; private set; }
        public Transform Cockpit { get; private set; }
        public Transform Hardware { get; private set; }
        public Transform Presentation { get; private set; }

        public static V3RuntimeHierarchy Create(GameObject craftRoot)
        {
            V3RuntimeHierarchy hierarchy =
                craftRoot.GetComponent<V3RuntimeHierarchy>();
            if (hierarchy == null)
            {
                hierarchy =
                    craftRoot.AddComponent<V3RuntimeHierarchy>();
            }

            hierarchy.BuildReadableHierarchy();
            return hierarchy;
        }

        public void BuildReadableHierarchy()
        {
            groups.Clear();
            Chassis = GetOrCreate("Chassis");
            Electronics = GetOrCreate("Electronics");
            Cockpit = GetOrCreate("Cockpit");
            Hardware = GetOrCreate("Hardware");
            Presentation = GetOrCreate("Presentation");

            GetOrCreate("Chassis/Structural Visuals");
            GetOrCreate("Electronics/Mainframe");
            GetOrCreate("Electronics/Chassis Sensors");
            GetOrCreate("Cockpit/Runtime Cockpit");
            GetOrCreate("Cockpit/System Computers");
            GetOrCreate("Hardware/Energy Core");
            GetOrCreate("Hardware/Cooling");
            GetOrCreate("Hardware/Rear Propulsion");
            GetOrCreate("Hardware/Front Brake Reverse");
            GetOrCreate("Hardware/Hover Assemblies");
            GetOrCreate("Hardware/Roof Thrusters");
            GetOrCreate("Hardware/Lateral Thrusters");
            GetOrCreate("Hardware/Active Aero");
            GetOrCreate("Presentation/Systems Console");
            GetOrCreate("Presentation/Warning Display");
            GetOrCreate("Presentation/Debug Visualization");

            MovePhysicalAndAuthoredChildren();
        }

        public void BindRuntimeServices()
        {
            BindMarker(
                "Electronics/Mainframe/V3 Craft Mainframe",
                "Centralized observation, intent, and scheduling",
                GetComponent<V3CraftMainframe>());
            BindMarker(
                "Electronics/Mainframe/Observation Bus",
                "Read-only observation exchange owned by Mainframe",
                GetComponent<V3CraftMainframe>());
            BindMarker(
                "Electronics/Mainframe/Intent Bus",
                "Pilot intent exchange owned by Mainframe",
                GetComponent<V3CraftMainframe>());
            BindMarker(
                "Electronics/Mainframe/Software Scheduler",
                "Central scheduled software execution",
                GetComponent<V3CraftMainframe>());
            BindMarker(
                "Electronics/Mainframe/Control Router",
                "Intent-to-device request routing",
                GetComponent<V3ActuatorCommandRouter>());
            BindMarker(
                "Electronics/Mainframe/Power Distributor",
                "Propulsion and systems power grants",
                GetComponent<V3PowerDistributor>());
            BindMarker(
                "Electronics/Mainframe/Health Service",
                "Build and runtime health validation",
                GetComponent<V3RuntimeBuildValidator>());
            BindMarker(
                "Electronics/Mainframe/Telemetry Hub",
                "Authoritative craft snapshots",
                GetComponent<V3CraftTelemetryHub>());
            BindMarker(
                "Presentation/Systems Console/Telemetry Source",
                "Authoritative data source for the scene-owned systems console",
                GetComponent<V3CraftTelemetryHub>());
            BindMarker(
                "Presentation/Warning Display/Runtime Source",
                "Cockpit warning presentation",
                GetComponent<V3CockpitWarningDisplay>());
            BindMarker(
                "Presentation/Debug Visualization/Thrusters",
                "Thruster direction and thermal debug",
                GetComponent<V3ThrusterDebugView>());
        }

        private void MovePhysicalAndAuthoredChildren()
        {
            V3Socket[] sockets = GetComponentsInChildren<V3Socket>(true);
            var movedAncestors = new HashSet<Transform>();
            for (int i = 0; i < sockets.Length; i++)
            {
                V3Socket socket = sockets[i];
                if (socket == null)
                {
                    continue;
                }

                Transform movable = FindMovableAncestor(socket.transform);
                if (movable == null || !movedAncestors.Add(movable))
                {
                    continue;
                }

                Transform target = GetSocketGroup(socket.SocketId);
                movable.SetParent(target, true);
            }

            Transform[] descendants =
                GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                Transform child = descendants[i];
                if (child == transform ||
                    child.parent == null ||
                    IsOrganizationGroup(child) ||
                    child.GetComponentInParent<V3Socket>() != null)
                {
                    continue;
                }

                if (child.name.StartsWith(
                        "Sensor.",
                        System.StringComparison.Ordinal) &&
                    child.GetComponent<V3Socket>() == null)
                {
                    child.SetParent(
                        GetOrCreate("Electronics/Chassis Sensors"),
                        true);
                }
            }

            var rootChildren = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
            {
                rootChildren.Add(transform.GetChild(i));
            }

            for (int i = 0; i < rootChildren.Count; i++)
            {
                Transform child = rootChildren[i];
                if (IsOrganizationGroup(child))
                {
                    continue;
                }

                if (child.name == "SystemsIntegrationHardpoints" &&
                    child.childCount == 0)
                {
                    DestroyHierarchyObject(child.gameObject);
                }
                else if (child.GetComponentInChildren<Collider>(true) != null ||
                         child.GetComponentInChildren<Renderer>(true) != null)
                {
                    child.SetParent(
                        GetOrCreate("Chassis/Structural Visuals"),
                        true);
                }
            }
        }

        private Transform FindMovableAncestor(Transform socket)
        {
            Transform candidate = socket;
            while (candidate.parent != null &&
                   candidate.parent != transform &&
                   !IsOrganizationGroup(candidate.parent))
            {
                if (candidate.parent.name ==
                    "SystemsIntegrationHardpoints")
                {
                    break;
                }

                candidate = candidate.parent;
            }

            return candidate;
        }

        private Transform GetSocketGroup(string socketId)
        {
            string id = socketId ?? string.Empty;
            if (id.StartsWith("System."))
            {
                return GetOrCreate("Cockpit/System Computers");
            }

            if (id.StartsWith("Cockpit."))
            {
                return GetOrCreate("Cockpit/Runtime Cockpit");
            }

            if (id.StartsWith("Core."))
            {
                return GetOrCreate("Hardware/Energy Core");
            }

            if (id.StartsWith("Cooling."))
            {
                return GetOrCreate("Hardware/Cooling");
            }

            if (id.StartsWith("Propulsion."))
            {
                return GetOrCreate("Hardware/Rear Propulsion");
            }

            if (id.StartsWith("Braking."))
            {
                return GetOrCreate("Hardware/Front Brake Reverse");
            }

            if (id.StartsWith("Aero."))
            {
                return GetOrCreate("Hardware/Active Aero");
            }

            if (id.StartsWith("Strafe."))
            {
                return GetOrCreate("Hardware/Lateral Thrusters");
            }

            if (id.StartsWith("Control."))
            {
                return GetOrCreate("Hardware/Roof Thrusters");
            }

            return GetOrCreate("Hardware/Hover Assemblies");
        }

        private void BindMarker(
            string path,
            string responsibility,
            Component owner)
        {
            Transform host = GetOrCreate(path);
            V3RuntimeOwnershipMarker marker =
                host.GetComponent<V3RuntimeOwnershipMarker>();
            if (marker == null)
            {
                marker =
                    host.gameObject.AddComponent<
                        V3RuntimeOwnershipMarker>();
            }

            marker.Initialize(responsibility, owner);
        }

        private Transform GetOrCreate(string path)
        {
            if (groups.TryGetValue(path, out Transform cached) &&
                cached != null)
            {
                return cached;
            }

            string[] segments = path.Split('/');
            Transform parent = transform;
            string key = string.Empty;
            for (int i = 0; i < segments.Length; i++)
            {
                key = key.Length == 0
                    ? segments[i]
                    : key + "/" + segments[i];
                if (groups.TryGetValue(key, out Transform existing) &&
                    existing != null)
                {
                    parent = existing;
                    continue;
                }

                Transform child = parent.Find(segments[i]);
                if (child == null)
                {
                    var host = new GameObject(segments[i]);
                    child = host.transform;
                    child.SetParent(parent, false);
                }

                groups[key] = child;
                parent = child;
            }

            return parent;
        }

        private bool IsOrganizationGroup(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate == Chassis ||
                   candidate == Electronics ||
                   candidate == Cockpit ||
                   candidate == Hardware ||
                   candidate == Presentation ||
                   candidate.IsChildOf(Chassis) ||
                   candidate.IsChildOf(Electronics) ||
                   candidate.IsChildOf(Cockpit) ||
                   candidate.IsChildOf(Hardware) ||
                   candidate.IsChildOf(Presentation);
        }

        private static void DestroyHierarchyObject(GameObject target)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
