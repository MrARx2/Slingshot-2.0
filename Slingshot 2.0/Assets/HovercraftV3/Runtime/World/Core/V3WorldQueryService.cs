using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lunarlight.Hovercraft.V3
{
    public enum V3WorldQueryStatus
    {
        Valid,
        MissingWorld,
        DuplicateWorld,
        MissingProfile
    }

    public static class V3WorldQueryService
    {
        private static readonly List<V3WorldSimulationRoot> Roots =
            new List<V3WorldSimulationRoot>(4);

        public static void Register(V3WorldSimulationRoot root)
        {
            if (root != null && !Roots.Contains(root))
            {
                Roots.Add(root);
            }
        }

        public static void Unregister(V3WorldSimulationRoot root)
        {
            Roots.Remove(root);
        }

        public static void NotifyZoneConfigurationChanged(Scene scene)
        {
            for (int i = Roots.Count - 1; i >= 0; i--)
            {
                V3WorldSimulationRoot root = Roots[i];
                if (root == null)
                {
                    Roots.RemoveAt(i);
                }
                else if (root.gameObject.scene == scene)
                {
                    root.ZoneRegistry?.MarkDirty();
                }
            }
        }

        public static V3WorldQueryStatus Resolve(
            Scene scene,
            out V3WorldSimulationRoot activeRoot)
        {
            activeRoot = null;
            int count = 0;
            for (int i = Roots.Count - 1; i >= 0; i--)
            {
                V3WorldSimulationRoot root = Roots[i];
                if (root == null)
                {
                    Roots.RemoveAt(i);
                    continue;
                }
                if (!root.enabled ||
                    !root.gameObject.activeInHierarchy ||
                    root.gameObject.scene.handle != scene.handle)
                {
                    continue;
                }
                count++;
                activeRoot = root;
            }

            if (count == 0)
            {
                activeRoot = null;
                return V3WorldQueryStatus.MissingWorld;
            }
            if (count > 1)
            {
                activeRoot = null;
                return V3WorldQueryStatus.DuplicateWorld;
            }
            return activeRoot.Profile != null
                ? V3WorldQueryStatus.Valid
                : V3WorldQueryStatus.MissingProfile;
        }

        public static bool TrySample(
            Scene scene,
            Vector3 worldPosition,
            out V3WorldEnvironmentSample sample)
        {
            V3WorldQueryStatus status = Resolve(scene, out V3WorldSimulationRoot root);
            if (status != V3WorldQueryStatus.Valid)
            {
                V3WorldSampleFlags flags = status switch
                {
                    V3WorldQueryStatus.DuplicateWorld =>
                        V3WorldSampleFlags.DuplicateWorld,
                    V3WorldQueryStatus.MissingProfile =>
                        V3WorldSampleFlags.MissingProfile,
                    _ => V3WorldSampleFlags.MissingWorld
                };
                sample = V3WorldEnvironmentSample.Invalid(
                    worldPosition,
                    flags,
                    root != null ? root.CurrentClock : default);
                return false;
            }

            return root.TrySample(
                worldPosition,
                out sample);
        }
    }
}
