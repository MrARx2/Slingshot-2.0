using System;
using UnityEngine;

namespace TrackGeneration.Macro
{
    /// <summary>
    /// One actual chosen section in the generated track: its definition, its resolved
    /// entry/exit frames, the subdivision ring frames the mesh is built from, bounds,
    /// and debug data. Serializable so the generated layout is inspectable in the editor.
    /// </summary>
    [Serializable]
    public class GeneratedTrackSection
    {
        [Tooltip("The macro definition this section was built from.")]
        public TrackMacroSectionDefinition Definition;

        [Tooltip("Index of this section in the track sequence.")]
        public int SectionIndex;

        [Tooltip("Entry frame — identical to the previous section's exit frame.")]
        public TrackConnectionFrame StartFrame;

        [Tooltip("Exit frame — identical to the next section's entry frame.")]
        public TrackConnectionFrame EndFrame;

        [Tooltip("Subdivision ring frames the prism mesh is built from. Empty for AirGap.")]
        public TrackConnectionFrame[] SubdivisionFrames;

        [Tooltip("Root-local bounds of this section's geometry.")]
        public Bounds SectionBounds;

        /// <summary>The GameObject built for this section (set by the mesh builder).</summary>
        [NonSerialized]
        public GameObject SectionObject;

        /// <summary>True if this section emits no geometry (the air gap).</summary>
        public bool IsEmptySpace => Definition != null && Definition.SectionType == TrackMacroSectionType.AirGap;

        /// <summary>Recomputes bounds from the subdivision frames and road width.</summary>
        public void RecalculateBounds()
        {
            if (SubdivisionFrames == null || SubdivisionFrames.Length == 0)
            {
                // Air gap: span the empty space between entry and exit for debug drawing.
                Bounds b = new Bounds(StartFrame.Position, Vector3.zero);
                b.Encapsulate(EndFrame.Position);
                b.Expand(StartFrame.Width);
                SectionBounds = b;
                return;
            }

            Bounds bounds = new Bounds(SubdivisionFrames[0].Position, Vector3.zero);
            foreach (var f in SubdivisionFrames)
            {
                float halfW = f.Width * 0.5f;
                bounds.Encapsulate(f.Position + f.Right * halfW);
                bounds.Encapsulate(f.Position - f.Right * halfW);
                bounds.Encapsulate(f.Position + f.Up * 0.5f);
                bounds.Encapsulate(f.Position - f.Up * 1.5f);
            }
            SectionBounds = bounds;
        }

        public override string ToString()
            => $"[{SectionIndex:D2}] {Definition?.DebugName} @ {StartFrame.ArcLength:F0}m";
    }
}
