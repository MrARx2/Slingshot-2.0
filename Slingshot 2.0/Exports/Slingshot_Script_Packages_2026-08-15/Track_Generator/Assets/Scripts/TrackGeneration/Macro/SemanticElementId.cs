namespace TrackGeneration.Macro
{
    /// <summary>
    /// Authoritative semantic identity of a generated element (Stage A of the dynamic
    /// topology plan). Replaces string inference from <c>PatternId</c> prefixes
    /// (metrics, camera, quarter fitting) with a serialized value stamped at emission.
    ///
    /// EXPLICIT VALUES, APPEND-ONLY: <see cref="GeneratedTrackSection"/> lists are
    /// scene-serialized, so members must never be renumbered or reordered — removed
    /// members leave holes (same rule as <see cref="TrackMacroSectionType"/>).
    /// Default 0 = legacy/no identity: every section generated before this field
    /// existed deserializes as <see cref="None"/> and all consumers must treat that
    /// as "fall back to the old inference".
    /// </summary>
    public enum SemanticElementId
    {
        None = 0,

        // ── Corner realizations (EmitCorner) ──
        OrdinaryCurve = 1,
        Hairpin = 2,
        DoubleApex = 3,
        TighteningCorner = 4,
        OpeningCorner = 5,
        SweeperIntoHairpin = 6,
        WallrideTurn = 7,

        // ── Transfers ──
        SCurve = 10,
        Chicane = 11,
        ClosureTransfer = 12,
        QuarterTransfer = 13,

        // ── Registered feature patterns ──
        VerticalLoop = 20,
        InlineCorkscrew = 21,
        DoubleCorkscrew = 22,
        Spiral = 23,
        Immelmann = 24,              // HalfLoopRollout
        HalfLoopToCorkscrew = 25,
        LoopToCorkscrew = 26,
        SpiralToCorkscrew = 27,
        JumpGap = 28,
        JumpToBankedLanding = 29,
        FullPipe = 30,
        AlternatingRadiusSequence = 31,

        // ── Structural ──
        RecoverySection = 40,

        // ── Stage F: transported-roll family ──
        DirectionalCorkscrew = 50,   // barrel roll whose axis also turns ±45/±90 (consumes a turn token)

        // ── Reserved for future catalog waves (F–J); values assigned when implemented ──
        // Cutback = 51, InlineTwist = 52, HeartlineRoll = 53,
        // Camelback = 60, SpeedHill = 61, DoubleDip = 62, TopHat = 63, DiveDrop = 64,
        // DiveLoop = 70, Sidewinder = 71, JuniorImmelmann = 72, …
    }
}
