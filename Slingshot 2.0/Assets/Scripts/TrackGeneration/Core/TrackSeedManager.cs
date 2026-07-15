using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Scene-level manager responsible for creating, storing, and managing
    /// <see cref="TrackSeed"/> instances.  Provides Inspector controls for
    /// seed input, bookmarking, and JSON import/export.
    /// </summary>
    [AddComponentMenu("Track Generation/Track Seed Manager")]
    public class TrackSeedManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector — Configuration
        // ──────────────────────────────────────────────

        [Header("Configuration")]

        [Tooltip("Reference to the shared TrackConfig ScriptableObject that holds all generation parameters.")]
        [SerializeField] private TrackConfig config;

        [Space]

        [Header("Seed Input")]

        [Tooltip("User-facing seed value. Ignored when UseRandomSeed is true.")]
        [SerializeField] private int currentSeedInput;

        [Tooltip("When true, a random seed is generated instead of using CurrentSeedInput.")]
        [SerializeField] private bool useRandomSeed = true;

        // ──────────────────────────────────────────────
        //  Inspector — Debug (Read Only)
        // ──────────────────────────────────────────────

        [Header("Debug — Read Only")]

        [Tooltip("The currently active TrackSeed.")]
        [SerializeField] private TrackSeed activeSeed;

        [Tooltip("Bookmarked seeds for later recall.")]
        [SerializeField] private List<TrackSeed> savedSeeds = new List<TrackSeed>();

        [Tooltip("SHA-256 hash of the last generated track (for change detection).")]
        [SerializeField] private string lastGeneratedHash;

        // ──────────────────────────────────────────────
        //  Public Properties
        // ──────────────────────────────────────────────

        /// <summary>Reference to the shared <see cref="TrackConfig"/>.</summary>
        public TrackConfig Config => config;

        /// <summary>User-facing seed value.</summary>
        public int CurrentSeedInput
        {
            get => currentSeedInput;
            set => currentSeedInput = value;
        }

        /// <summary>Whether a random seed should be generated.</summary>
        public bool UseRandomSeed
        {
            get => useRandomSeed;
            set => useRandomSeed = value;
        }

        /// <summary>Diagnostic count for the generator inspector; bookmarks grow only through SaveCurrentSeed.</summary>
        public int SavedSeedCount => savedSeeds?.Count ?? 0;

        // ──────────────────────────────────────────────
        //  Public Methods
        // ──────────────────────────────────────────────

        /// <summary>
        /// Creates and activates a new <see cref="TrackSeed"/>.
        /// Uses <see cref="CurrentSeedInput"/> when <see cref="UseRandomSeed"/>
        /// is <c>false</c>, otherwise generates a random seed value.
        /// </summary>
        /// <returns>The newly created and activated <see cref="TrackSeed"/>.</returns>
        public TrackSeed InitializeSeed()
        {
            int seed = useRandomSeed
                ? GenerateRandomSeedValue()
                : currentSeedInput;

            activeSeed = TrackSeed.CreateNew(seed);
            currentSeedInput = seed;
            lastGeneratedHash = activeSeed.ComputeHash();

            Debug.Log($"[TrackSeedManager] Initialized seed: {activeSeed.DisplayName} (hash: {lastGeneratedHash})");
            return activeSeed;
        }

        /// <summary>
        /// Clones the current <see cref="activeSeed"/> and appends it to
        /// the <see cref="savedSeeds"/> bookmark list.
        /// </summary>
        public void SaveCurrentSeed()
        {
            if (activeSeed == null)
            {
                Debug.LogWarning("[TrackSeedManager] No active seed to save.");
                return;
            }

            savedSeeds.Add(activeSeed.Clone());
            Debug.Log($"[TrackSeedManager] Saved seed '{activeSeed.DisplayName}' (total bookmarks: {savedSeeds.Count}).");
        }

        /// <summary>
        /// Loads a previously saved seed from the bookmark list by index
        /// and sets it as the active seed (cloned to prevent aliasing).
        /// </summary>
        /// <param name="index">Zero-based index into <see cref="savedSeeds"/>.</param>
        public void LoadSeed(int index)
        {
            if (savedSeeds == null || index < 0 || index >= savedSeeds.Count)
            {
                Debug.LogWarning($"[TrackSeedManager] Invalid saved-seed index: {index}.");
                return;
            }

            activeSeed = savedSeeds[index].Clone();
            currentSeedInput = activeSeed.BaseSeed;
            lastGeneratedHash = activeSeed.ComputeHash();

            Debug.Log($"[TrackSeedManager] Loaded seed '{activeSeed.DisplayName}' from bookmark slot {index}.");
        }

        /// <summary>
        /// Returns the active seed serialized as a JSON string,
        /// suitable for clipboard or file export.
        /// </summary>
        /// <returns>JSON representation of <see cref="activeSeed"/>, or an empty string if no seed is active.</returns>
        public string ExportSeedString()
        {
            if (activeSeed == null)
            {
                Debug.LogWarning("[TrackSeedManager] No active seed to export.");
                return string.Empty;
            }

            string json = activeSeed.Serialize();
            Debug.Log($"[TrackSeedManager] Exported seed JSON ({json.Length} chars).");
            return json;
        }

        /// <summary>
        /// Deserializes the provided JSON string and sets the result as the
        /// active seed.
        /// </summary>
        /// <param name="json">JSON string previously produced by <see cref="ExportSeedString"/>.</param>
        public void ImportSeedString(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[TrackSeedManager] Import string is null or empty.");
                return;
            }

            activeSeed = TrackSeed.Deserialize(json);
            currentSeedInput = activeSeed.BaseSeed;
            lastGeneratedHash = activeSeed.ComputeHash();

            Debug.Log($"[TrackSeedManager] Imported seed '{activeSeed.DisplayName}'.");
        }

        /// <summary>Returns the currently active <see cref="TrackSeed"/>.</summary>
        /// <returns>The active seed, or <c>null</c> if none has been initialized.</returns>
        public TrackSeed GetActiveSeed()
        {
            return activeSeed;
        }

        // ──────────────────────────────────────────────
        //  Private Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Generates a random integer seed value using
        /// <see cref="System.Environment.TickCount"/> so we never touch
        /// <c>UnityEngine.Random</c>.
        /// </summary>
        private static int GenerateRandomSeedValue()
        {
            // Use system tick count combined with hash code of a new GUID
            // for additional entropy without relying on UnityEngine.Random.
            return System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode();
        }
    }
}
