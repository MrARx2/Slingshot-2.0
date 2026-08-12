using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [Serializable]
    public sealed class V3ParityCommandSegment
    {
        [SerializeField] private string label;
        [Min(0.02f), SerializeField] private float durationSeconds = 1f;
        [SerializeField] private bool resetCraftsBeforeSegment;
        [Min(0f), SerializeField] private float initialForwardSpeedKmh;
        [SerializeField] private V3PilotCommand command;

        public string Label => string.IsNullOrWhiteSpace(label) ? "Unnamed" : label;
        public float DurationSeconds => Mathf.Max(0.02f, durationSeconds);
        public bool ResetCraftsBeforeSegment => resetCraftsBeforeSegment;
        public float InitialForwardSpeedKmh => Mathf.Max(0f, initialForwardSpeedKmh);
        public V3PilotCommand Command => command;

        public V3ParityCommandSegment(
            string label,
            float durationSeconds,
            V3PilotCommand command,
            bool resetCraftsBeforeSegment = false,
            float initialForwardSpeedKmh = 0f)
        {
            this.label = label;
            this.durationSeconds = Mathf.Max(0.02f, durationSeconds);
            this.command = command;
            this.resetCraftsBeforeSegment = resetCraftsBeforeSegment;
            this.initialForwardSpeedKmh = Mathf.Max(0f, initialForwardSpeedKmh);
        }
    }

    [CreateAssetMenu(
        menuName = "Hovercraft V3/Parity/Command Profile",
        fileName = "V2_V3_Parity_Command_Profile")]
    public sealed class V3ParityCommandProfile : ScriptableObject
    {
        [SerializeField] private List<V3ParityCommandSegment> segments =
            new List<V3ParityCommandSegment>();

        public IReadOnlyList<V3ParityCommandSegment> Segments => segments;

        public float TotalDurationSeconds
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i] != null)
                    {
                        total += segments[i].DurationSeconds;
                    }
                }

                return total;
            }
        }

        public bool TryEvaluate(
            float elapsedSeconds,
            out V3PilotCommand command,
            out int segmentIndex,
            out float segmentElapsedSeconds)
        {
            float cursor = 0f;
            float time = Mathf.Max(0f, elapsedSeconds);
            for (int i = 0; i < segments.Count; i++)
            {
                V3ParityCommandSegment segment = segments[i];
                if (segment == null)
                {
                    continue;
                }

                float end = cursor + segment.DurationSeconds;
                if (time < end)
                {
                    command = segment.Command;
                    segmentIndex = i;
                    segmentElapsedSeconds = time - cursor;
                    return true;
                }

                cursor = end;
            }

            command = default;
            segmentIndex = -1;
            segmentElapsedSeconds = 0f;
            return false;
        }

        public void SetSegments(IEnumerable<V3ParityCommandSegment> values)
        {
            segments.Clear();
            if (values != null)
            {
                segments.AddRange(values);
            }
        }
    }
}
