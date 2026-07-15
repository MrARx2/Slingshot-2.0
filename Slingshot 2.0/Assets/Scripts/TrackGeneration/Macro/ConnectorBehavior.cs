namespace TrackGeneration.Macro
{
    /// <summary>
    /// How a connector (a straight between two content sections) resolves the
    /// intentions of its neighbours. A connector is NOT an independent road: its
    /// purpose is to blend the surface properties of the sections around it, and the
    /// global surface-resolution passes read this classification to decide what to
    /// carry through it.
    /// </summary>
    public enum ConnectorBehavior
    {
        /// <summary>Plain transition with no special inheritance (long open straights).</summary>
        NeutralTransition,

        /// <summary>Carry the previous section's surface intent through the connector.</summary>
        ContinuePrevious,

        /// <summary>Pre-shape the road for the next section (feature approaches).</summary>
        PrepareNext,

        /// <summary>Blend both neighbours' intents across the connector (default).</summary>
        BlendNeighbours,

        /// <summary>
        /// Short link between two same-direction turns: bank, outside-wall support and
        /// turn rounding are PRESERVED through it — the three elements read as one
        /// turn complex, never as turn / flat reset / turn.
        /// </summary>
        SameDirectionTurnBridge,

        /// <summary>
        /// Link between opposed turns: bank crosses zero smoothly while overall wall
        /// support stays elevated and the outside-wall emphasis hands over sides —
        /// never both walls collapsing before the new side rises.
        /// </summary>
        OppositeDirectionTransfer,

        /// <summary>
        /// After orientation features (loops, corkscrews, half-loops, landings):
        /// stable upright recovery has priority over inheriting feature values.
        /// </summary>
        OrientationRecovery
    }
}
