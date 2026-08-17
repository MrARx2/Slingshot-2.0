namespace TrackGeneration.Planning
{
    /// <summary>
    /// The independent per-attempt random streams the topology planner consumes.
    /// Keeping the streams separate is what makes partial regeneration possible:
    /// re-randomizing the Feature stream cannot change the corner plan, because the
    /// corner plan only ever draws from the Layout stream (and so on).
    /// </summary>
    public class PlanRandomStreams
    {
        public Unity.Mathematics.Random Layout;
        public Unity.Mathematics.Random Feature;
        public Unity.Mathematics.Random Elevation;
        public Unity.Mathematics.Random Quarter;

        /// <summary>Forks four independent streams from one attempt RNG (legacy single-seed path).</summary>
        public static PlanRandomStreams FromSingle(ref Unity.Mathematics.Random rng) => new PlanRandomStreams
        {
            Layout = new Unity.Mathematics.Random(rng.NextUInt() | 1u),
            Feature = new Unity.Mathematics.Random(rng.NextUInt() | 1u),
            Elevation = new Unity.Mathematics.Random(rng.NextUInt() | 1u),
            Quarter = new Unity.Mathematics.Random(rng.NextUInt() | 1u)
        };

        /// <summary>
        /// Value copy of every stream. Unity.Mathematics.Random is a struct, so the copy
        /// lets a caller re-run a deterministic planning core from the same RNG state
        /// (used by the dual-quarter demotion retry loop).
        /// </summary>
        public PlanRandomStreams Copy() => new PlanRandomStreams
        {
            Layout = Layout,
            Feature = Feature,
            Elevation = Elevation,
            Quarter = Quarter
        };
    }
}
