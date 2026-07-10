using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using TrackGeneration.Core;
using TrackGeneration.Splines;
using Unity.Mathematics;

namespace TrackGeneration.Stunts
{
    /// <summary>
    /// Determines the placement of stunts along the track splines.
    /// Does not instantiate game objects; only produces placement data.
    ///
    /// Placement philosophy (per the Track Tuning Context):
    /// <list type="bullet">
    /// <item>The road is first classified into typed sections (<see cref="TrackSectionAnalyzer"/>).
    /// Every stunt has a rule for WHERE it belongs — no dice-rolls on arbitrary distance buckets.</item>
    /// <item><b>Ramps</b> live ONLY on straights, and only if the FULL chain fits with room to spare:
    /// Jump Ramp → Air Gap → Landing Ramp → Recovery Straight. Heading and slope over the whole
    /// footprint must be near-zero, so the jump follows the flow of the road.</item>
    /// <item><b>Wall rides</b> live ONLY on medium/sharp curves with a consistent turn direction.</item>
    /// <item>A global budget of MinStunts..MaxStunts (ramps + wall rides) is selected from all
    /// valid candidates with minimum spacing and type variety. Shortcut candidates are favored
    /// and skew harder — risk is the price of the alternative route.</item>
    /// <item><b>Boost pads</b> (not budgeted) go on straights leading into corners or jumps —
    /// the tuning file's "Boost Straight" placed before big banked turns and ramps.</item>
    /// </list>
    /// </summary>
    public class StuntPlacer
    {
        // ── Ramp chain dimensions per difficulty (launch, height, air gap, landing) ──
        // Sized for a 3.5 m hovercraft: a lip a few meters high at racing speed already
        // produces serious air. Anything bigger reads as scenery, not a stunt.
        private static readonly float[] LaunchLength = { 10f, 12f, 14f };   // Easy, Medium, Hard
        private static readonly float[] LaunchHeight = { 2.2f, 3.2f, 4.5f };
        private static readonly float[] AirGap = { 12f, 18f, 26f };
        private static readonly float[] LandingLength = { 12f, 15f, 18f };

        // Recovery straight required AFTER the landing ramp (tuning file rule).
        private const float RecoveryLength = 30f;

        // Max total heading change across the ramp footprint. Beyond this the jump
        // fires the craft toward the wall / off the map instead of down the road.
        private const float MaxRampHeadingDeg = 6f;

        // Max heading change across footprint + recovery (landing may ease into a gentle curve).
        private const float MaxRecoveryHeadingDeg = 15f;

        // Max average slope across the ramp footprint (rise/run).
        private const float MaxRampGradient = 0.12f;

        // Wall ride sizing.
        private const float MinWallRideSection = 50f;
        private const float MinWallRideLength = 40f;
        private const float MaxWallRideLength = 80f;

        // Boost pad geometry / placement.
        private const float BoostPadLength = 6f;
        private const float PadLeadBeforeCorner = 35f;  // pad ends this far before the corner entry
        private const float PadLeadBeforeRamp = 25f;    // pad ends this far before a launch ramp
        private const float MinPadSpacing = 40f;

        /// <summary>One potential stunt site, produced by the collection pass.</summary>
        private struct Candidate
        {
            public StuntType Type;
            public int SplineIndex;
            public float StartArc;      // arc where the actor footprint begins
            public float EndArc;        // arc where the actor footprint ends (incl. landing for ramps)
            public float T;             // normalized t at StartArc
            public float Length;        // launch length (ramp) / section length (wall ride)
            public float Height;        // ramp lip height
            public float Gap;           // ramp air gap
            public float Landing;       // ramp landing length
            public int Lane;
            public StuntDifficulty Difficulty;
            public float Score;
        }

        private readonly Dictionary<int, TrackSectionAnalyzer> _analyzers = new Dictionary<int, TrackSectionAnalyzer>();
        private readonly Dictionary<int, List<(float start, float end)>> _occupied = new Dictionary<int, List<(float, float)>>();

        public List<StuntPlacement> PlaceStunts(TrackConfig config, ref Unity.Mathematics.Random rng, SplineContainer mainCircuit, List<TrackBranch> shortcuts)
        {
            var placements = new List<StuntPlacement>();
            _analyzers.Clear();
            _occupied.Clear();

            if (mainCircuit == null) return placements;
            float mainLength = SplineUtilities.GetSplineLength(mainCircuit);

            // ── 1. Analyze road structure (main + shortcuts) ──
            var mainAnalyzer = new TrackSectionAnalyzer();
            mainAnalyzer.Analyze(mainCircuit);
            _analyzers[0] = mainAnalyzer;

            if (shortcuts != null)
            {
                for (int i = 0; i < shortcuts.Count; i++)
                {
                    var a = new TrackSectionAnalyzer();
                    a.Analyze(shortcuts[i].Spline);
                    _analyzers[i + 1] = a;
                }
            }

            // ── 2. Collect all valid stunt candidates ──
            var candidates = new List<Candidate>();
            foreach (var kvp in _analyzers)
            {
                int splineIndex = kvp.Key;
                bool isShortcut = splineIndex > 0;
                int lanes = isShortcut ? config.ShortcutRoadLanes : config.MainRoadLanes;

                CollectRampCandidates(splineIndex, kvp.Value, lanes, config, shortcuts, mainLength, ref rng, candidates);
                CollectWallRideCandidates(splineIndex, kvp.Value, config, shortcuts, mainLength, ref rng, candidates);
            }

            // ── 3. Select up to budget with spacing + variety ──
            int budget = rng.NextInt(config.MinStunts, config.MaxStunts + 1);
            SelectCandidates(candidates, budget, config, ref rng, placements);

            if (placements.Count == 0 && budget > 0)
            {
                Debug.LogWarning("[StuntPlacer] No valid stunt sites found on this track (all straights too short / curves too tight near junctions). Consider more straight sections in TrackConfig.");
            }

            // ── 4. Boost pads (separate budget, placed relative to corners and chosen ramps) ──
            PlaceBoostPads(config, ref rng, shortcuts, mainLength, placements);

            return placements;
        }

        // ──────────────────────────── Ramp candidates ────────────────────────────

        private void CollectRampCandidates(int splineIndex, TrackSectionAnalyzer analyzer, int lanes, TrackConfig config,
            List<TrackBranch> shortcuts, float mainLength, ref Unity.Mathematics.Random rng, List<Candidate> candidates)
        {
            bool isShortcut = splineIndex > 0;
            float splineLength = analyzer.TotalLength;

            foreach (var section in analyzer.Sections)
            {
                if (section.Type != SectionType.Straight) continue;

                // Roll difficulty first — it defines the footprint we must fit.
                StuntDifficulty diff = DetermineDifficulty(ref rng, isShortcut);
                int d = (int)diff;

                float height = math.min(LaunchHeight[d], config.RampMaxHeight);
                float footprint = LaunchLength[d] + AirGap[d] + LandingLength[d];

                // Keep a safety margin inside the section on both sides: the section
                // boundary is where curvature starts building again.
                const float edgeMargin = 10f;
                float usable = section.Length - footprint - RecoveryLength - edgeMargin * 2f;
                if (usable < 0f) continue;

                // Randomize the start within the available slack (deterministic via seed).
                float startArc = section.StartArc + edgeMargin + rng.NextFloat() * usable;
                float endArc = startArc + footprint;
                float recoveryEnd = endArc + RecoveryLength;

                // Flow check: the craft is committed while airborne — the road must not
                // turn away underneath it, and must stay flat enough to land clean.
                if (analyzer.HeadingChangeDegrees(startArc, endArc) > MaxRampHeadingDeg) continue;
                if (analyzer.HeadingChangeDegrees(startArc, recoveryEnd) > MaxRecoveryHeadingDeg) continue;
                if (math.abs(analyzer.ElevationChange(startArc, endArc)) / footprint > MaxRampGradient) continue;

                // Junction check: never block a shortcut entrance/exit or main-road junction —
                // launch, landing, AND recovery must all stay clear.
                if (TouchesMergeZone(splineIndex, analyzer, startArc, recoveryEnd, splineLength, shortcuts, mainLength, config)) continue;

                // Longer straights make better jump sites (more reaction room); shortcut
                // stunts are the risk/reward hook of the alternative route.
                float score = (config.RampProbabilityPerSegment + 0.05f)
                            * math.saturate(section.Length / 200f)
                            * (isShortcut ? 1.5f : 1f);

                candidates.Add(new Candidate
                {
                    Type = StuntType.Ramp,
                    SplineIndex = splineIndex,
                    StartArc = startArc,
                    EndArc = endArc,
                    T = analyzer.TAtArc(startArc),
                    Length = LaunchLength[d],
                    Height = height,
                    Gap = AirGap[d],
                    Landing = LandingLength[d],
                    // Shortcuts are narrow: always full width. Wide main road: single-lane
                    // ramps leave a bypass lane, so jumping stays a choice, not a wall.
                    Lane = (!isShortcut && lanes > 1) ? rng.NextInt(1, lanes + 1) : 0,
                    Difficulty = diff,
                    Score = score
                });
            }
        }

        // ──────────────────────────── Wall ride candidates ────────────────────────────

        private void CollectWallRideCandidates(int splineIndex, TrackSectionAnalyzer analyzer, TrackConfig config,
            List<TrackBranch> shortcuts, float mainLength, ref Unity.Mathematics.Random rng, List<Candidate> candidates)
        {
            bool isShortcut = splineIndex > 0;
            float splineLength = analyzer.TotalLength;

            foreach (var section in analyzer.Sections)
            {
                // Wall rides belong on committed corners: the wall IS the racing line there.
                // Straights and gentle curves would make a wall ride pointless (nothing to
                // carry through) — exactly the "no logic" placements being removed.
                if (section.Type != SectionType.MediumCurve && section.Type != SectionType.SharpCurve) continue;
                if (section.Length < MinWallRideSection) continue;
                if (section.TurnSign == 0) continue; // mixed S-curve: no single wall to ride

                float length = math.clamp(section.Length * 0.6f, MinWallRideLength, MaxWallRideLength);

                // Center the ride in the section so entry/exit transitions happen while
                // curvature is still building/releasing.
                float startArc = section.StartArc + (section.Length - length) * 0.5f;
                float endArc = startArc + length;

                if (TouchesMergeZone(splineIndex, analyzer, startArc, endArc, splineLength, shortcuts, mainLength, config)) continue;

                // Sharper corners make better wall rides.
                float sharpness = math.saturate(section.MaxCurvature * 120f); // 1.0 at R <= 120 m
                float score = (config.WallRideProbability + 0.05f)
                            * (0.5f + 0.5f * sharpness)
                            * (isShortcut ? 1.5f : 1f);

                candidates.Add(new Candidate
                {
                    Type = StuntType.WallRide,
                    SplineIndex = splineIndex,
                    StartArc = startArc,
                    EndArc = endArc,
                    T = analyzer.TAtArc(startArc),
                    Length = length,
                    Lane = 0, // wall rides are always full width
                    Difficulty = DetermineDifficulty(ref rng, isShortcut),
                    Score = score
                });
            }
        }

        // ──────────────────────────── Selection ────────────────────────────

        private void SelectCandidates(List<Candidate> candidates, int budget, TrackConfig config,
            ref Unity.Mathematics.Random rng, List<StuntPlacement> placements)
        {
            // Jitter scores so the same config still produces varied (but seed-deterministic) picks.
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                c.Score *= rng.NextFloat(0.75f, 1.25f);
                candidates[i] = c;
            }

            var chosen = new List<Candidate>();

            while (chosen.Count < budget && candidates.Count > 0)
            {
                // Pick the best remaining valid candidate.
                int bestIdx = -1;
                float bestScore = float.MinValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Score > bestScore && IsCompatible(candidates[i], chosen, config))
                    {
                        bestScore = candidates[i].Score;
                        bestIdx = i;
                    }
                }
                if (bestIdx < 0) break;

                Candidate pick = candidates[bestIdx];
                chosen.Add(pick);
                candidates.RemoveAt(bestIdx);

                // Variety pressure: decay remaining same-type scores so a 4-stunt track
                // tends toward a mix of ramps and wall rides rather than four of a kind.
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Type == pick.Type)
                    {
                        var c = candidates[i];
                        c.Score *= 0.7f;
                        candidates[i] = c;
                    }
                }
            }

            foreach (var c in chosen)
            {
                MarkOccupied(c.SplineIndex, c.StartArc, c.EndArc + (c.Type == StuntType.Ramp ? RecoveryLength : 0f));

                placements.Add(new StuntPlacement
                {
                    Type = c.Type,
                    SplineIndex = c.SplineIndex,
                    T = c.T,
                    Length = c.Length,
                    Lane = c.Lane,
                    Difficulty = c.Difficulty,
                    Height = c.Height,
                    AirGapLength = c.Gap,
                    LandingLength = c.Landing
                });
            }
        }

        private bool IsCompatible(Candidate c, List<Candidate> chosen, TrackConfig config)
        {
            foreach (var other in chosen)
            {
                if (other.SplineIndex != c.SplineIndex) continue;

                float len = _analyzers[c.SplineIndex].TotalLength;
                float d = ArcDistance(
                    (c.StartArc + c.EndArc) * 0.5f,
                    (other.StartArc + other.EndArc) * 0.5f,
                    len, c.SplineIndex == 0);

                if (d < config.MinStuntSpacing) return false;
            }
            return true;
        }

        // ──────────────────────────── Boost pads ────────────────────────────

        private void PlaceBoostPads(TrackConfig config, ref Unity.Mathematics.Random rng,
            List<TrackBranch> shortcuts, float mainLength, List<StuntPlacement> placements)
        {
            int padBudget = rng.NextInt(config.MinBoostPads, config.MaxBoostPads + 1);
            if (padBudget <= 0) return;

            // Sites in priority order: (a) before every chosen launch ramp — a boost
            // straight feeding a jump is the tuning file's signature combo — then
            // (b) on straights feeding medium/sharp corners, sharpest first.
            var sites = new List<(int splineIndex, float arc, float priority)>();

            foreach (var p in placements)
            {
                if (p.Type != StuntType.Ramp) continue;
                var analyzer = _analyzers[p.SplineIndex];
                float rampStartArc = ArcFromT(analyzer, p.T);
                float arc = rampStartArc - PadLeadBeforeRamp - BoostPadLength;
                sites.Add((p.SplineIndex, arc, 2f));
            }

            foreach (var kvp in _analyzers)
            {
                var analyzer = kvp.Value;
                var sections = analyzer.Sections;
                for (int i = 0; i < sections.Count; i++)
                {
                    var s = sections[i];
                    if (s.Type != SectionType.Straight || s.Length < 70f) continue;

                    // What does this straight feed into? (wrap to first section on the closed
                    // main loop; on an open shortcut the last section feeds into nothing)
                    if (i + 1 >= sections.Count && kvp.Key != 0) continue;
                    var next = sections[(i + 1) % sections.Count];
                    if (next.Type != SectionType.MediumCurve && next.Type != SectionType.SharpCurve) continue;

                    float arc = s.EndArc - PadLeadBeforeCorner - BoostPadLength;
                    float sharpness = math.saturate(next.MaxCurvature * 120f);
                    sites.Add((kvp.Key, arc, 1f + sharpness * 0.5f));
                }
            }

            sites.Sort((a, b) => b.priority.CompareTo(a.priority));

            int placedPads = 0;
            var padArcs = new Dictionary<int, List<float>>();

            foreach (var site in sites)
            {
                if (placedPads >= padBudget) break;

                var analyzer = _analyzers[site.splineIndex];
                float splineLength = analyzer.TotalLength;
                bool closed = site.splineIndex == 0;

                float arc = site.arc;
                if (closed) { arc %= splineLength; if (arc < 0f) arc += splineLength; }
                else if (arc < 0f || arc + BoostPadLength > splineLength) continue;

                float endArc = arc + BoostPadLength;

                // A pad must sit on clean, straight road: outside merge zones, clear of
                // stunt footprints, and away from other pads.
                if (TouchesMergeZone(site.splineIndex, analyzer, arc, endArc, splineLength, shortcuts, mainLength, config)) continue;
                if (OverlapsOccupied(site.splineIndex, arc, endArc)) continue;

                bool tooClose = false;
                if (padArcs.TryGetValue(site.splineIndex, out var existing))
                {
                    foreach (float other in existing)
                    {
                        if (ArcDistance(arc, other, splineLength, closed) < MinPadSpacing) { tooClose = true; break; }
                    }
                }
                if (tooClose) continue;

                bool isShortcut = site.splineIndex > 0;
                int lanes = isShortcut ? config.ShortcutRoadLanes : config.MainRoadLanes;

                placements.Add(new StuntPlacement
                {
                    Type = StuntType.BoostPad,
                    SplineIndex = site.splineIndex,
                    T = analyzer.TAtArc(arc),
                    Length = BoostPadLength,
                    // On multi-lane roads pads occupy one lane — grabbing the boost is a
                    // line choice. On narrow shortcuts they span the full width.
                    Lane = (!isShortcut && lanes > 1) ? rng.NextInt(1, lanes + 1) : 0,
                    Difficulty = StuntDifficulty.Easy
                });

                if (!padArcs.ContainsKey(site.splineIndex)) padArcs[site.splineIndex] = new List<float>();
                padArcs[site.splineIndex].Add(arc);
                placedPads++;
            }
        }

        // ──────────────────────────── Shared helpers ────────────────────────────

        private bool TouchesMergeZone(int splineIndex, TrackSectionAnalyzer analyzer, float startArc, float endArc,
            float splineLength, List<TrackBranch> shortcuts, float mainLength, TrackConfig config)
        {
            // Check start, middle, and end of the span — a long footprint could straddle
            // a junction even when both endpoints are clear.
            for (int i = 0; i <= 2; i++)
            {
                float arc = math.lerp(startArc, endArc, i * 0.5f);
                float t = analyzer.TAtArc(arc);
                if (MergeZoneUtility.IsInMergeZone(splineIndex, t, splineLength, shortcuts, mainLength, config.ShortcutMergeDistance))
                    return true;
            }
            return false;
        }

        private void MarkOccupied(int splineIndex, float startArc, float endArc)
        {
            if (!_occupied.ContainsKey(splineIndex)) _occupied[splineIndex] = new List<(float, float)>();
            _occupied[splineIndex].Add((startArc, endArc));
        }

        private bool OverlapsOccupied(int splineIndex, float startArc, float endArc)
        {
            if (!_occupied.TryGetValue(splineIndex, out var spans)) return false;
            foreach (var (s, e) in spans)
            {
                if (startArc < e && endArc > s) return true;
            }
            return false;
        }

        private float ArcFromT(TrackSectionAnalyzer analyzer, float t)
        {
            // Inverse of TAtArc via coarse scan (sections are few, precision of ±1 m is fine here).
            float len = analyzer.TotalLength;
            float best = 0f, bestDiff = float.MaxValue;
            const int steps = 512;
            for (int i = 0; i <= steps; i++)
            {
                float arc = len * i / steps;
                float diff = math.abs(analyzer.TAtArc(arc) - t);
                if (diff < bestDiff) { bestDiff = diff; best = arc; }
            }
            return best;
        }

        private static float ArcDistance(float a, float b, float totalLength, bool closed)
        {
            float d = math.abs(a - b);
            if (closed) d = math.min(d, totalLength - d);
            return d;
        }

        private StuntDifficulty DetermineDifficulty(ref Unity.Mathematics.Random rng, bool hardBias = false)
        {
            float roll = rng.NextFloat();
            if (hardBias)
            {
                // Shortcut distribution: risk is the price of the alternative route.
                if (roll < 0.45f) return StuntDifficulty.Hard;
                if (roll < 0.85f) return StuntDifficulty.Medium;
                return StuntDifficulty.Easy;
            }
            if (roll < 0.2f) return StuntDifficulty.Hard;
            if (roll < 0.6f) return StuntDifficulty.Medium;
            return StuntDifficulty.Easy;
        }
    }
}
