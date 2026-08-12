using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    public interface IV3RuntimeDevice
    {
        string RuntimeDeviceId { get; }
        string DeviceDisplayName { get; }
        bool IsDeviceOnline { get; }
        Component DeviceComponent { get; }
    }

    [DisallowMultipleComponent]
    public sealed class V3RuntimeDeviceRegistry : MonoBehaviour
    {
        private readonly List<IV3RuntimeDevice> devices =
            new List<IV3RuntimeDevice>(24);
        private readonly Dictionary<string, IV3RuntimeDevice> byId =
            new Dictionary<string, IV3RuntimeDevice>(
                StringComparer.Ordinal);

        public IReadOnlyList<IV3RuntimeDevice> Devices => devices;
        public int Count => devices.Count;
        public string ValidationError { get; private set; } =
            string.Empty;

        public bool Initialize(GameObject craftRoot)
        {
            devices.Clear();
            byId.Clear();
            ValidationError = string.Empty;
            if (craftRoot == null)
            {
                ValidationError = "Device registry requires a craft root.";
                return false;
            }

            MonoBehaviour[] behaviours =
                craftRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IV3RuntimeDevice device))
                {
                    continue;
                }

                string id = device.RuntimeDeviceId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    ValidationError =
                        $"Runtime device '{behaviours[i].name}' has no ID.";
                    return false;
                }

                if (byId.ContainsKey(id))
                {
                    ValidationError =
                        $"Duplicate runtime device ID '{id}'.";
                    return false;
                }

                devices.Add(device);
                byId.Add(id, device);
            }

            return true;
        }

        public bool TryGet(
            string runtimeDeviceId,
            out IV3RuntimeDevice device)
        {
            return byId.TryGetValue(
                runtimeDeviceId ?? string.Empty,
                out device);
        }

        public int CountDevices<T>()
        {
            int count = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] is T)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
