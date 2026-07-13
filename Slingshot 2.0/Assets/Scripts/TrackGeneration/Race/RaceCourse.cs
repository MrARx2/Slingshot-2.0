using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrackGeneration.Race
{
    /// <summary>One completed lap in the session.</summary>
    [Serializable]
    public class LapRecord
    {
        public int LapNumber;
        public float TimeSeconds;
        [Tooltip("True only when every logical checkpoint was crossed in order before the finish line.")]
        public bool Valid;
        [Tooltip("Average speed over the lap in km/h (lap length / lap time).")]
        public float AverageSpeedKmh;
    }

    /// <summary>
    /// One LOGICAL checkpoint: one or more alternative gates (one per branch route).
    /// Crossing ANY gate of the group advances the lap — a player is never required
    /// to drive both branch routes.
    /// </summary>
    [Serializable]
    public class CheckpointGroup
    {
        public List<RaceGate> Gates = new List<RaceGate>();
    }

    /// <summary>
    /// The race course built onto a generated track: the start/finish gate, ordered
    /// LOGICAL checkpoint groups (branch-aware), and the time-attack session state.
    /// <para>
    /// Crossing detection runs in FixedUpdate as a plane-crossing test per gate —
    /// tunnel-proof at any speed (the craft covers ~2.8 m per tick at 1000 km/h), with
    /// the crossing instant interpolated for sub-tick lap timing.
    /// </para>
    /// <para>
    /// The craft is discovered through <see cref="TrackGeneration.ITrackRaceCraft"/> —
    /// this assembly never references concrete craft types.
    /// </para>
    /// </summary>
    public class RaceCourse : MonoBehaviour
    {
        [Header("Course (built by the track generator)")]
        public RaceGate StartFinishGate;

        [Tooltip("Ordered logical checkpoint groups. Each group holds one gate per available route.")]
        public List<CheckpointGroup> CheckpointGroups = new List<CheckpointGroup>();

        [Tooltip("Lap length in meters — used for average speed.")]
        public float TrackLength;

        [Header("Detection")]
        [Tooltip("A per-tick world-space jump larger than this is a teleport/respawn, never a legitimate crossing.")]
        public float TeleportDistance = 150f;

        /// <summary>Fired when a lap completes (valid or not).</summary>
        public event Action<LapRecord> LapCompleted;

        /// <summary>Fired when the next in-order logical checkpoint is crossed (0-based index).</summary>
        public event Action<int> CheckpointPassed;

        public bool LapInProgress { get; private set; }
        public int CurrentLapNumber { get; private set; }

        /// <summary>Index of the next LOGICAL checkpoint that must be crossed.</summary>
        public int NextCheckpointIndex { get; private set; }

        /// <summary>Branch group the player most recently entered via a route gate (-1 = main line).</summary>
        public int CurrentBranchGroupId { get; private set; } = -1;

        /// <summary>Route the player chose in the current branch group (0 = A, 1 = B, -1 = unknown).</summary>
        public int CurrentRouteId { get; private set; } = -1;

        /// <summary>The last gate crossed in order — the respawn anchor (null before the first crossing).</summary>
        public RaceGate LastValidGate { get; private set; }

        public IReadOnlyList<LapRecord> Laps => _laps;
        public LapRecord BestLap { get; private set; }
        public float CurrentLapTime => LapInProgress ? (float)(Time.timeAsDouble - _lapStartTime) : 0f;

        /// <summary>Total logical checkpoints per lap.</summary>
        public int CheckpointCount => CheckpointGroups.Count;

        private readonly List<LapRecord> _laps = new List<LapRecord>();
        private double _lapStartTime;

        private TrackGeneration.ITrackRaceCraft _craft;
        private Rigidbody _craftBody;
        private float _nextCraftSearchTime;

        private readonly List<RaceGate> _allGates = new List<RaceGate>();
        private Vector3[] _prevLocal;
        private Vector3 _prevWorld;
        private bool _tracking;

        private void Start()
        {
            if (Application.isPlaying)
            {
                EnsureHud();
            }
        }

        private void EnsureHud()
        {
            if (FindAnyObjectByType<RaceHUD>() != null) return;

            Camera cam = Camera.main;
            GameObject host = cam != null ? cam.gameObject : new GameObject("RaceHUD");
            host.AddComponent<RaceHUD>();
        }

        /// <summary>
        /// Call after the craft is teleported: abandons the lap in progress and re-arms
        /// detection. Session history and best lap are kept.
        /// </summary>
        public void NotifyRespawn()
        {
            LapInProgress = false;
            NextCheckpointIndex = 0;
            CurrentBranchGroupId = -1;
            CurrentRouteId = -1;
            LastValidGate = null;
            _tracking = false;
        }

        /// <summary>
        /// Respawn pose on the player's chosen route: the last valid gate's frame (or
        /// the start/finish line before any crossing).
        /// </summary>
        public bool TryGetRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            RaceGate anchor = LastValidGate != null ? LastValidGate : StartFinishGate;
            if (anchor == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            position = anchor.transform.position;
            rotation = anchor.transform.rotation;
            return true;
        }

        private void FixedUpdate()
        {
            if (StartFinishGate == null || !AcquireCraft()) return;

            RebuildGateCache();

            int gateCount = _allGates.Count;
            if (_prevLocal == null || _prevLocal.Length != gateCount)
            {
                _prevLocal = new Vector3[gateCount];
                _tracking = false;
            }

            Vector3 worldPos = _craftBody != null ? _craftBody.position : _craft.CraftTransform.position;

            if (_tracking && (worldPos - _prevWorld).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                NotifyRespawn();
            }

            for (int i = 0; i < gateCount; i++)
            {
                RaceGate gate = _allGates[i];
                if (gate == null) continue;

                Vector3 local = gate.transform.InverseTransformPoint(worldPos);

                if (_tracking)
                {
                    Vector3 prev = _prevLocal[i];
                    if (prev.z < 0f && local.z >= 0f)
                    {
                        float f = prev.z / (prev.z - local.z);
                        Vector3 at = Vector3.Lerp(prev, local, f);
                        if (gate.IsInsideWindow(at))
                        {
                            double crossTime = Time.fixedTimeAsDouble - Time.fixedDeltaTime * (1f - f);
                            HandleCrossing(gate, crossTime);
                        }
                    }
                }

                _prevLocal[i] = local;
            }

            _prevWorld = worldPos;
            _tracking = true;
        }

        private void RebuildGateCache()
        {
            int expected = 1;
            foreach (var g in CheckpointGroups) expected += g?.Gates?.Count ?? 0;
            if (_allGates.Count == expected) return;

            _allGates.Clear();
            _allGates.Add(StartFinishGate);
            foreach (var g in CheckpointGroups)
            {
                if (g?.Gates == null) continue;
                foreach (var gate in g.Gates) _allGates.Add(gate);
            }
        }

        private void HandleCrossing(RaceGate gate, double crossTime)
        {
            if (gate.IsStartFinish)
            {
                if (LapInProgress)
                {
                    CompleteLap(crossTime);
                }

                LapInProgress = true;
                CurrentLapNumber = _laps.Count + 1;
                NextCheckpointIndex = 0;
                CurrentBranchGroupId = -1;
                CurrentRouteId = -1;
                LastValidGate = gate;
                _lapStartTime = crossTime;
                return;
            }

            if (!LapInProgress) return;

            // Any gate of the NEXT logical group advances the lap — either branch route counts.
            if (gate.CheckpointIndex == NextCheckpointIndex)
            {
                NextCheckpointIndex++;
                LastValidGate = gate;
                CurrentBranchGroupId = gate.BranchGroupId;
                CurrentRouteId = gate.RouteId;
                CheckpointPassed?.Invoke(gate.CheckpointIndex);
            }
            // Out-of-order or repeat crossings are ignored — the lap simply
            // finishes invalid if any logical checkpoint was skipped.
        }

        private void CompleteLap(double crossTime)
        {
            float lapTime = Mathf.Max(0.001f, (float)(crossTime - _lapStartTime));
            bool valid = NextCheckpointIndex >= CheckpointGroups.Count;

            var record = new LapRecord
            {
                LapNumber = CurrentLapNumber,
                TimeSeconds = lapTime,
                Valid = valid,
                AverageSpeedKmh = TrackLength / lapTime * 3.6f
            };
            _laps.Add(record);

            if (valid && (BestLap == null || lapTime < BestLap.TimeSeconds))
            {
                BestLap = record;
            }

            LapCompleted?.Invoke(record);
        }

        private bool AcquireCraft()
        {
            if (_craft != null) return true;
            if (Time.unscaledTime < _nextCraftSearchTime) return false;

            _nextCraftSearchTime = Time.unscaledTime + 1f;
            _craft = TrackGeneration.TrackCraftLocator.FindCraft();
            if (_craft == null) return false;

            _craftBody = _craft.CraftRigidbody;
            _tracking = false;
            return true;
        }
    }
}
