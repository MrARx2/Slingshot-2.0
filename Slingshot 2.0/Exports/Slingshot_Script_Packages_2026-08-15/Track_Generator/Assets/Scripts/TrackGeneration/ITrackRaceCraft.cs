using UnityEngine;

namespace TrackGeneration
{
    /// <summary>
    /// Contract between the track-generation assembly and the player craft. The track
    /// side never references concrete craft/camera types — the craft implements this
    /// (see CraftCore) and the track discovers it at runtime.
    /// </summary>
    public interface ITrackRaceCraft
    {
        /// <summary>Root transform to place at the track start.</summary>
        Transform CraftTransform { get; }

        /// <summary>Rigidbody used for teleporting and race-gate crossing detection (may be null).</summary>
        Rigidbody CraftRigidbody { get; }

        /// <summary>Hover ride height so spawns sit on the surface instead of inside it.</summary>
        float SpawnRideHeight { get; }

        /// <summary>Called after the craft has been teleported (spawn, reset) — reset cameras/effects here.</summary>
        void OnPlacedAtTrackStart();
    }

    /// <summary>Runtime discovery for the scene's <see cref="ITrackRaceCraft"/>.</summary>
    public static class TrackCraftLocator
    {
        /// <summary>Finds the first active craft in the scene, or null. Callers should cache and re-poll slowly.</summary>
        public static ITrackRaceCraft FindCraft()
        {
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
            foreach (var b in behaviours)
            {
                if (b is ITrackRaceCraft craft) return craft;
            }
            return null;
        }
    }
}
