using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TrackGeneration.Core
{
    /// <summary>
    /// A base seed value plus deterministic subsystem RNG derivation. Two
    /// <see cref="TrackSeed"/> instances with the same base seed always produce the
    /// same subsystem random streams — and therefore the same track for the same
    /// designer settings and rulebook.
    /// </summary>
    [Serializable]
    public class TrackSeed
    {
        /// <summary>Base integer seed used by every subsystem RNG.</summary>
        [Tooltip("Base integer seed for deterministic generation.")]
        public int BaseSeed;

        /// <summary>Human-readable name shown in UI (auto-generated or user-set).</summary>
        [Tooltip("Display name for the UI.")]
        public string DisplayName;

        /// <summary>Timestamp when this seed was first created (serialized as ticks string).</summary>
        [Tooltip("Creation timestamp.")]
        [SerializeField] private string createdAtTicks;

        /// <summary>Creation date/time of this seed.</summary>
        public DateTime CreatedAt
        {
            get => long.TryParse(createdAtTicks, out long ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            set => createdAtTicks = value.Ticks.ToString();
        }

        /// <summary>Deterministic SHA-256 hash of the seed (for change detection / sharing).</summary>
        public string ComputeHash()
        {
            using SHA256 sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(BaseSeed.ToString());
            byte[] hash = sha.ComputeHash(bytes);

            StringBuilder hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        /// <summary>
        /// Creates a deterministic RNG whose seed derives from <see cref="BaseSeed"/>
        /// combined with a subsystem key. Distinct keys give independent streams —
        /// changing one subsystem's draw count never perturbs another subsystem.
        /// </summary>
        public Unity.Mathematics.Random CreateSubsystemRandom(string subsystemKey)
        {
            int combined = CombineHash(BaseSeed, StableStringHash(subsystemKey));
            uint derived = (uint)combined;
            if (derived == 0u) derived = 1u;
            return new Unity.Mathematics.Random(derived);
        }

        /// <summary>Returns a copy of this seed.</summary>
        public TrackSeed Clone()
        {
            return new TrackSeed
            {
                BaseSeed = BaseSeed,
                DisplayName = DisplayName,
                createdAtTicks = createdAtTicks
            };
        }

        /// <summary>Creates a new seed with the current UTC time.</summary>
        public static TrackSeed CreateNew(int seed)
        {
            var ts = new TrackSeed
            {
                BaseSeed = seed,
                DisplayName = "Seed_" + seed
            };
            ts.CreatedAt = DateTime.UtcNow;
            return ts;
        }

        /// <summary>Serializes this seed to JSON.</summary>
        public string Serialize() => JsonUtility.ToJson(this, prettyPrint: true);

        /// <summary>Deserializes a seed produced by <see cref="Serialize"/>.</summary>
        public static TrackSeed Deserialize(string json) => JsonUtility.FromJson<TrackSeed>(json);

        /// <summary>
        /// Platform-stable string hash (string.GetHashCode is randomized per process on
        /// some runtimes, which would silently break cross-session determinism).
        /// </summary>
        private static int StableStringHash(string s)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }

        private static int CombineHash(int h1, int h2)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + h1;
                hash = hash * 31 + h2;
                return hash;
            }
        }
    }
}
