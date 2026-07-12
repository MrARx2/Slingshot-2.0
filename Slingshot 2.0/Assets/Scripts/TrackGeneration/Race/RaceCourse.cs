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
        [Tooltip("True only when every checkpoint was crossed in order before the finish line.")]
        public bool Valid;
        [Tooltip("Average speed over the lap in km/h (lap length / lap time).")]
        public float AverageSpeedKmh;
    }

    /// <summary>
    /// The race course built onto a generated track: the start/finish gate, the ordered
    /// checkpoint gates, and the time-attack session state (current lap, lap history,
    /// session best).
    /// <para>
    /// Crossing detection runs in FixedUpdate as a plane-crossing test per gate: the craft's
    /// gate-local Z flips from negative to positive inside the gate's detection window.
    /// This is tunnel-proof at any speed (the craft moves ~2.8 m per tick at 1000 km/h) and
    /// the crossing instant is interpolated between ticks for sub-tick lap timing.
    /// </para>
    /// <para>
    /// A lap is VALID only when all checkpoints were crossed in order (1..N) before the
    /// finish line. Out-of-order or repeated checkpoint crossings are ignored.
    /// The session (history + best lap) lives with the course, so regenerating the track
    /// starts a fresh session — times from different layouts never mix.
    /// </para>
    /// </summary>
    public class RaceCourse : MonoBehaviour
    {
        [Header("Course (built by the track generator)")]
        public RaceGate StartFinishGate;
        public List<RaceGate> Checkpoints = new List<RaceGate>();

        [Tooltip("Lap length in meters — used for average speed.")]
        public float TrackLength;

        [Header("Detection")]
        [Tooltip("A per-tick world-space jump larger than this is a teleport/respawn, never a legitimate crossing.")]
        public float TeleportDistance = 50f;

        /// <summary>Fired when a lap completes (valid or not).</summary>
        public event Action<LapRecord> LapCompleted;

        /// <summary>Fired when the next in-order checkpoint is crossed (0-based index).</summary>
        public event Action<int> CheckpointPassed;

        /// <summary>True once the craft has crossed the start line and the clock is running.</summary>
        public bool LapInProgress { get; private set; }

        /// <summary>1-based number of the lap currently being driven.</summary>
        public int CurrentLapNumber { get; private set; }

        /// <summary>Index of the next checkpoint that must be crossed (== Checkpoints.Count when all are collected).</summary>
        public int NextCheckpointIndex { get; private set; }

        /// <summary>All completed laps this session, oldest first.</summary>
        public IReadOnlyList<LapRecord> Laps => _laps;

        /// <summary>Fastest VALID lap of the session, or null.</summary>
        public LapRecord BestLap { get; private set; }

        /// <summary>Elapsed time of the lap in progress, in seconds.</summary>
        public float CurrentLapTime => LapInProgress ? (float)(Time.timeAsDouble - _lapStartTime) : 0f;

        private readonly List<LapRecord> _laps = new List<LapRecord>();
        private double _lapStartTime;

        private global::CraftCore _craft;
        private Rigidbody _craftBody;
        private float _nextCraftSearchTime;

        // Per-gate craft position in gate-local space from the previous tick.
        // Index 0 = start/finish, 1..N = checkpoints. Invalid until _tracking is set.
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

        /// <summary>
        /// The HUD survives track regeneration (it lives on the camera), so only the first
        /// generated course actually creates it.
        /// </summary>
        private void EnsureHud()
        {
            if (FindAnyObjectByType<RaceHUD>() != null) return;

            Camera cam = Camera.main;
            GameObject host = cam != null ? cam.gameObject : new GameObject("RaceHUD");
            host.AddComponent<RaceHUD>();
        }

        /// <summary>
        /// Call after the craft is teleported (spawn, Backspace reset): abandons the lap in
        /// progress and re-arms detection. Session history and best lap are kept.
        /// </summary>
        public void NotifyRespawn()
        {
            LapInProgress = false;
            NextCheckpointIndex = 0;
            _tracking = false;
        }

        private void FixedUpdate()
        {
            if (StartFinishGate == null || !AcquireCraft()) return;

            int gateCount = 1 + Checkpoints.Count;
            if (_prevLocal == null || _prevLocal.Length != gateCount)
            {
                _prevLocal = new Vector3[gateCount];
                _tracking = false;
            }

            Vector3 worldPos = _craftBody != null ? _craftBody.position : _craft.transform.position;

            // Teleport guard (backup for resets that don't call NotifyRespawn).
            if (_tracking && (worldPos - _prevWorld).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                NotifyRespawn();
            }

            for (int i = 0; i < gateCount; i++)
            {
                RaceGate gate = i == 0 ? StartFinishGate : Checkpoints[i - 1];
                if (gate == null) continue;

                Vector3 local = gate.transform.InverseTransformPoint(worldPos);

                if (_tracking)
                {
                    Vector3 prev = _prevLocal[i];
                    if (prev.z < 0f && local.z >= 0f)
                    {
                        // Interpolate the exact crossing point and instant within the tick.
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

        private void HandleCrossing(RaceGate gate, double crossTime)
        {
            if (gate.IsStartFinish)
            {
                if (LapInProgress)
                {
                    CompleteLap(crossTime);
                }

                // First crossing arms the clock; every later crossing rolls straight into the next lap.
                LapInProgress = true;
                CurrentLapNumber = _laps.Count + 1;
                NextCheckpointIndex = 0;
                _lapStartTime = crossTime;
                return;
            }

            if (!LapInProgress) return;

            if (gate.CheckpointIndex == NextCheckpointIndex)
            {
                NextCheckpointIndex++;
                CheckpointPassed?.Invoke(gate.CheckpointIndex);
            }
            // Out-of-order or repeat crossings are ignored — the lap simply
            // finishes invalid if any checkpoint was skipped.
        }

        private void CompleteLap(double crossTime)
        {
            float lapTime = Mathf.Max(0.001f, (float)(crossTime - _lapStartTime));
            bool valid = NextCheckpointIndex >= Checkpoints.Count;

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
            _craft = FindAnyObjectByType<global::CraftCore>();
            if (_craft == null) return false;

            _craftBody = _craft.GetComponent<Rigidbody>();
            _tracking = false;
            return true;
        }
    }
}
