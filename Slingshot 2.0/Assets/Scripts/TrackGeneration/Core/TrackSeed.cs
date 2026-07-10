using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TrackGeneration.Core
{
    /// <summary>
    /// Encapsulates a base seed value together with an ordered list of
    /// <see cref="TrackModification"/> records.  Two <see cref="TrackSeed"/>
    /// instances with identical data will always produce the same track.
    /// </summary>
    [Serializable]
    public class TrackSeed
    {
        // ──────────────────────────────────────────────
        //  Serialized Fields
        // ──────────────────────────────────────────────

        /// <summary>Base integer seed used by every subsystem RNG.</summary>
        [Tooltip("Base integer seed for deterministic generation.")]
        public int BaseSeed;

        /// <summary>Ordered list of user or procedural modifications applied on top of the base seed.</summary>
        [Tooltip("Ordered list of modifications applied after initial generation.")]
        public List<TrackModification> Modifications = new List<TrackModification>();

        /// <summary>Human-readable name shown in UI (auto-generated or user-set).</summary>
        [Tooltip("Display name for the UI.")]
        public string DisplayName;

        /// <summary>Timestamp when this seed was first created (serialized as ticks string).</summary>
        [Tooltip("Creation timestamp.")]
        [SerializeField] private string createdAtTicks;

        // ──────────────────────────────────────────────
        //  Properties
        // ──────────────────────────────────────────────

        /// <summary>Whether any modifications have been applied to this seed.</summary>
        public bool HasModifications => Modifications != null && Modifications.Count > 0;

        /// <summary>Creation date/time of this seed.</summary>
        public DateTime CreatedAt
        {
            get => long.TryParse(createdAtTicks, out long ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            set => createdAtTicks = value.Ticks.ToString();
        }

        // ──────────────────────────────────────────────
        //  Deterministic Hashing
        // ──────────────────────────────────────────────

        /// <summary>
        /// Computes a deterministic SHA-256 hash string from <see cref="BaseSeed"/>
        /// and every <see cref="TrackModification"/> in order.
        /// </summary>
        /// <returns>Lowercase hexadecimal hash string.</returns>
        public string ComputeHash()
        {
            using SHA256 sha = SHA256.Create();

            StringBuilder sb = new StringBuilder();
            sb.Append(BaseSeed);

            if (Modifications != null)
            {
                foreach (TrackModification mod in Modifications)
                {
                    sb.Append('|');
                    sb.Append((int)mod.Type);
                    sb.Append(',');
                    sb.Append(mod.TargetIndex);
                    sb.Append(',');
                    sb.Append(mod.Delta.x.ToString("R"));
                    sb.Append(',');
                    sb.Append(mod.Delta.y.ToString("R"));
                    sb.Append(',');
                    sb.Append(mod.Delta.z.ToString("R"));
                    sb.Append(',');
                    sb.Append(mod.SerializedData ?? string.Empty);
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] hash = sha.ComputeHash(bytes);

            StringBuilder hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                hex.Append(b.ToString("x2"));
            }

            return hex.ToString();
        }

        // ──────────────────────────────────────────────
        //  Subsystem RNG
        // ──────────────────────────────────────────────

        /// <summary>
        /// Creates a deterministic <see cref="Unity.Mathematics.Random"/> instance
        /// whose seed is derived from <see cref="BaseSeed"/> combined with a
        /// subsystem-specific key.  The returned RNG is guaranteed to have a
        /// non-zero seed.
        /// </summary>
        /// <param name="subsystemKey">
        /// Unique string identifier for the calling subsystem
        /// (e.g. "Elevation", "Branching", "Stunts").
        /// </param>
        /// <returns>A ready-to-use <see cref="Unity.Mathematics.Random"/>.</returns>
        public Unity.Mathematics.Random CreateSubsystemRandom(string subsystemKey)
        {
            int combined = CombineHash(BaseSeed, subsystemKey.GetHashCode());
            uint derived = (uint)combined;

            // Unity.Mathematics.Random requires a non-zero seed.
            if (derived == 0u)
            {
                derived = 1u;
            }

            return new Unity.Mathematics.Random(derived);
        }

        // ──────────────────────────────────────────────
        //  Cloning
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns a deep copy of this <see cref="TrackSeed"/>,
        /// duplicating the modifications list.
        /// </summary>
        public TrackSeed Clone()
        {
            TrackSeed clone = new TrackSeed
            {
                BaseSeed = BaseSeed,
                DisplayName = DisplayName,
                createdAtTicks = createdAtTicks,
                Modifications = Modifications != null
                    ? new List<TrackModification>(Modifications)
                    : new List<TrackModification>()
            };

            return clone;
        }

        // ──────────────────────────────────────────────
        //  Factory
        // ──────────────────────────────────────────────

        /// <summary>
        /// Creates a brand-new <see cref="TrackSeed"/> with the given base seed,
        /// an empty modification list, and the current UTC time.
        /// </summary>
        /// <param name="seed">The base seed value.</param>
        /// <returns>A freshly initialised <see cref="TrackSeed"/>.</returns>
        public static TrackSeed CreateNew(int seed)
        {
            TrackSeed ts = new TrackSeed
            {
                BaseSeed = seed,
                Modifications = new List<TrackModification>(),
                DisplayName = "Seed_" + seed
            };

            ts.CreatedAt = DateTime.UtcNow;
            return ts;
        }

        // ──────────────────────────────────────────────
        //  Serialization
        // ──────────────────────────────────────────────

        /// <summary>Serializes this seed to a JSON string via <see cref="JsonUtility"/>.</summary>
        public string Serialize()
        {
            return JsonUtility.ToJson(this, prettyPrint: true);
        }

        /// <summary>Deserializes a JSON string produced by <see cref="Serialize"/> back into a <see cref="TrackSeed"/>.</summary>
        /// <param name="json">JSON string.</param>
        /// <returns>The deserialized <see cref="TrackSeed"/>.</returns>
        public static TrackSeed Deserialize(string json)
        {
            return JsonUtility.FromJson<TrackSeed>(json);
        }

        // ──────────────────────────────────────────────
        //  Private Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Simple hash-combining helper (similar to System.HashCode.Combine
        /// but available in all runtimes).
        /// </summary>
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
