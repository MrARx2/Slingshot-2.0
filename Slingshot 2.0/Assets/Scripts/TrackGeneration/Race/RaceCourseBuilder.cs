using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;
using TrackGeneration.Planning;

namespace TrackGeneration.Race
{
    /// <summary>
    /// Builds the race course onto a generated track: a start/finish arch at a small arc
    /// offset past the spawn point, and N LOGICAL checkpoint groups evenly spaced around
    /// the lap. Checkpoints falling inside a Dual Road Quarter get one gate PER ROAD
    /// sharing the same logical index — crossing either advances the lap, so a player is
    /// never required to drive both roads. Gate visuals are decoration (no colliders);
    /// detection is handled by <see cref="RaceCourse"/>.
    /// </summary>
    public static class RaceCourseBuilder
    {
        private const float MinGateSeparation = 60f;

        /// <summary>Builds the quarter-aware course under the track root. Returns null on failure.</summary>
        public static RaceCourse Build(
            Transform trackRoot,
            GeneratedTrackLayout layout,
            TrackRoadProfileSettings roadProfile,
            int checkpointCount,
            float startLineArcOffset,
            Material startFinishMaterial = null,
            Material startPillarMaterial = null,
            Material checkpointMaterial = null)
        {
            var sections = layout.Sections;
            float total = MacroTrackSampler.GetTotalLength(sections);
            if (trackRoot == null || total <= MinGateSeparation * (checkpointCount + 1))
            {
                Debug.LogWarning("[RaceCourseBuilder] Track too short (or missing) for a race course — skipped.");
                return null;
            }

            GameObject courseObj = new GameObject("RaceCourse");
            courseObj.transform.SetParent(trackRoot, false);

            RaceCourse course = courseObj.AddComponent<RaceCourse>();
            course.TrackLength = total;

            Materials mats = ResolveMaterials(startFinishMaterial, startPillarMaterial, checkpointMaterial);

            float startArc = Mathf.Min(startLineArcOffset, total * 0.25f);
            if (!MacroTrackSampler.TrySampleFrame(sections, startArc, out TrackConnectionFrame startFrame))
            {
                Debug.LogWarning("[RaceCourseBuilder] Could not sample the start line frame — skipped.");
                DestroyObject(courseObj);
                return null;
            }
            course.StartFinishGate = BuildGate(courseObj.transform, startFrame, roadProfile, isStartFinish: true,
                logicalIndex: -1, quarterIndex: -1, roadId: -1, mats);

            // Logical checkpoints: evenly spaced arcs; dual-quarter spans emit one gate per road.
            float spacing = total / (checkpointCount + 1);
            float lastArc = 0f;
            int logicalIndex = 0;

            for (int i = 0; i < checkpointCount; i++)
            {
                float arc = Mathf.Repeat(startArc + spacing * (i + 1), total);

                float rel = Mathf.Repeat(arc - startArc, total);
                if (rel < lastArc + MinGateSeparation || rel > total - MinGateSeparation) continue;
                lastArc = rel;

                var group = new CheckpointGroup();

                var dual = FindDualWindowAt(layout, arc);
                if (dual != null)
                {
                    // Road A gate at the arc, road B gate at the matching normalized
                    // position along its own chain (both roads agree on gate progress).
                    if (!MacroTrackSampler.TrySampleFrame(sections, arc, out TrackConnectionFrame frameA)) continue;
                    var gateA = BuildGate(courseObj.transform, frameA, roadProfile, false, logicalIndex,
                        dual.QuarterIndex, 0, mats);
                    group.Gates.Add(gateA);

                    float t = Mathf.InverseLerp(dual.WindowStartArc, dual.WindowEndArc, arc);
                    var frameB = SampleChainFrameNormalized(sections,
                        dual.Quarter.RouteB.FirstSectionIndex, dual.Quarter.RouteB.LastSectionIndex, t);
                    var gateB = BuildGate(courseObj.transform, frameB, roadProfile, false, logicalIndex,
                        dual.QuarterIndex, 1, mats);
                    group.Gates.Add(gateB);
                }
                else
                {
                    if (!MacroTrackSampler.TrySampleFrame(sections, arc, out TrackConnectionFrame f)) continue;
                    group.Gates.Add(BuildGate(courseObj.transform, f, roadProfile, false, logicalIndex, -1, -1, mats));
                }

                if (group.Gates.Count > 0)
                {
                    course.CheckpointGroups.Add(group);
                    logicalIndex++;
                }
            }

            if (course.CheckpointGroups.Count == 0)
            {
                Debug.LogWarning("[RaceCourseBuilder] No checkpoints could be placed — course rejected.");
                DestroyObject(courseObj);
                return null;
            }

            if (course.CheckpointGroups.Count < checkpointCount)
                Debug.LogWarning($"[RaceCourseBuilder] Placed {course.CheckpointGroups.Count}/{checkpointCount} logical checkpoints — some sample points fell too close to a neighbour.");

            return course;
        }

        private static void DestroyObject(GameObject obj)
        {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        // ─────────────────────────── Quarter-aware sampling ───────────────────────────

        private class DualWindow
        {
            public GeneratedTrackQuarter Quarter;
            public int QuarterIndex;
            public float WindowStartArc; // road A's landing mouth
            public float WindowEndArc;   // road A's launch lip end
        }

        /// <summary>
        /// The dual-road window an arc falls inside, if any: from the quarter's landing
        /// mouths to its launch lips (the span where the player is committed to a road).
        /// </summary>
        private static DualWindow FindDualWindowAt(GeneratedTrackLayout layout, float arc)
        {
            var sections = layout.Sections;
            foreach (var q in layout.Quarters)
            {
                if (!q.IsDual || q.RouteB == null || q.RouteB.FirstSectionIndex < 1) continue;

                // Road B's chain starts at the shared mouth arc; road A's lip is the
                // canonical section right before the chain in the list.
                float windowStart = sections[q.RouteB.FirstSectionIndex].StartFrame.ArcLength;
                float windowEnd = sections[q.RouteB.FirstSectionIndex - 1].EndFrame.ArcLength;
                if (arc >= windowStart + 1f && arc <= windowEnd - 1f)
                    return new DualWindow
                    {
                        Quarter = q,
                        QuarterIndex = q.QuarterIndex,
                        WindowStartArc = windowStart,
                        WindowEndArc = windowEnd
                    };
            }
            return null;
        }

        /// <summary>Samples a frame at normalized t along a contiguous run of sections (an alternate road chain).</summary>
        private static TrackConnectionFrame SampleChainFrameNormalized(List<GeneratedTrackSection> sections,
            int firstSection, int lastSection, float t)
        {
            float chainStart = sections[firstSection].StartFrame.ArcLength;
            float chainEnd = sections[lastSection].EndFrame.ArcLength;
            float targetArc = Mathf.Lerp(chainStart, chainEnd, Mathf.Clamp01(t));

            for (int i = firstSection; i <= lastSection; i++)
            {
                var frames = sections[i].SubdivisionFrames;
                if (frames == null || frames.Length == 0) continue;
                if (targetArc > sections[i].EndFrame.ArcLength && i < lastSection) continue;

                float segT = Mathf.InverseLerp(sections[i].StartFrame.ArcLength, sections[i].EndFrame.ArcLength, targetArc);
                int idx = Mathf.Clamp(Mathf.RoundToInt(segT * (frames.Length - 1)), 0, frames.Length - 1);
                return frames[idx];
            }
            return sections[firstSection].StartFrame;
        }

        // ─────────────────────────────── Gates ───────────────────────────────

        private static RaceGate BuildGate(
            Transform parent,
            TrackConnectionFrame frame,
            TrackRoadProfileSettings roadProfile,
            bool isStartFinish,
            int logicalIndex,
            int quarterIndex,
            int roadId,
            Materials mats)
        {
            string roadSuffix = roadId == 0 ? "A" : roadId == 1 ? "B" : "";
            GameObject go = new GameObject(isStartFinish ? "StartFinishLine" : $"Checkpoint_{logicalIndex + 1}{roadSuffix}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = frame.Position;
            go.transform.localRotation = Quaternion.LookRotation(frame.Forward, frame.Up);

            RaceGate gate = go.AddComponent<RaceGate>();
            gate.IsStartFinish = isStartFinish;
            gate.CheckpointIndex = logicalIndex;
            gate.ArcLength = frame.ArcLength;
            gate.QuarterIndex = quarterIndex;
            gate.RoadId = roadId;
            gate.DetectionHalfWidth = frame.Width * 0.75f;
            gate.DetectionBottom = -6f;
            gate.DetectionTop = 60f;

            float sideHeight = frame.SideHeight > 0.01f
                ? frame.SideHeight
                : (roadProfile != null ? roadProfile.SideHeight : 3.5f);

            BuildGateVisuals(go.transform, frame.Width, sideHeight, isStartFinish, logicalIndex, mats);
            return gate;
        }

        private static void BuildGateVisuals(Transform gate, float roadWidth, float sideHeight, bool isStartFinish, int index, Materials mats)
        {
            float halfWidth = roadWidth * 0.5f;
            float pillarHeight = sideHeight + 8f;
            float pillarThickness = isStartFinish ? 1.2f : 0.7f;
            Material pillarMat = isStartFinish ? mats.Pillar : mats.Checkpoint;
            Material beamMat = isStartFinish ? mats.Checker : mats.Checkpoint;

            float pillarX = halfWidth + pillarThickness;
            CreatePart(gate, "Pillar_L", new Vector3(-pillarX, pillarHeight * 0.5f, 0f),
                new Vector3(pillarThickness, pillarHeight, pillarThickness), pillarMat);
            CreatePart(gate, "Pillar_R", new Vector3(pillarX, pillarHeight * 0.5f, 0f),
                new Vector3(pillarThickness, pillarHeight, pillarThickness), pillarMat);

            float beamHeight = isStartFinish ? 1.6f : 0.8f;
            CreatePart(gate, "Beam", new Vector3(0f, pillarHeight - beamHeight * 0.5f, 0f),
                new Vector3(pillarX * 2f + pillarThickness, beamHeight, pillarThickness * 0.6f), beamMat);

            if (isStartFinish)
            {
                CreatePart(gate, "FloorStripe", new Vector3(0f, 0.04f, 0f),
                    new Vector3(roadWidth * 0.95f, 0.06f, 3f), mats.Checker);
            }
            else
            {
                CreateNumberLabel(gate, index + 1, pillarHeight + 2.6f, mats.Checkpoint);
            }
        }

        private static void CreatePart(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;

            // Decoration only — the craft must never collide with a gate.
            Collider col = part.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }

            part.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ─────────────────────────── Number labels ───────────────────────────
        // Seven-segment digits built from cubes: real depth-tested geometry.

        private const float DigitHeight = 3.2f;
        private const float DigitWidth = 1.8f;
        private const float DigitStroke = 0.4f;
        private const float DigitDepth = 0.25f;

        private static readonly byte[] DigitSegments =
        {
            0b0111111, // 0
            0b0000110, // 1
            0b1011011, // 2
            0b1001111, // 3
            0b1100110, // 4
            0b1101101, // 5
            0b1111101, // 6
            0b0000111, // 7
            0b1111111, // 8
            0b1101111  // 9
        };

        private static void CreateNumberLabel(Transform gate, int number, float height, Material mat)
        {
            GameObject labelObj = new GameObject("NumberLabel");
            labelObj.transform.SetParent(gate, false);
            labelObj.transform.localPosition = new Vector3(0f, height, 0f);

            string text = number.ToString();
            float step = DigitWidth + 1.0f;
            float xStart = -(text.Length - 1) * step * 0.5f;

            for (int i = 0; i < text.Length; i++)
            {
                CreateDigit(labelObj.transform, text[i] - '0', new Vector3(xStart + i * step, 0f, 0f), mat);
            }
        }

        private static void CreateDigit(Transform parent, int digit, Vector3 localPos, Material mat)
        {
            if (digit < 0 || digit > 9) return;
            byte segs = DigitSegments[digit];

            float hw = DigitWidth * 0.5f;
            float hh = DigitHeight * 0.5f;
            float qh = DigitHeight * 0.25f;
            Vector3 horizontal = new Vector3(DigitWidth, DigitStroke, DigitDepth);
            Vector3 vertical = new Vector3(DigitStroke, DigitHeight * 0.5f + DigitStroke * 0.5f, DigitDepth);

            void Segment(int bit, string name, Vector3 pos, Vector3 scale)
            {
                if ((segs & (1 << bit)) != 0)
                    CreatePart(parent, name, localPos + pos, scale, mat);
            }

            Segment(0, "SegA", new Vector3(0f, hh, 0f), horizontal);
            Segment(1, "SegB", new Vector3(hw, qh, 0f), vertical);
            Segment(2, "SegC", new Vector3(hw, -qh, 0f), vertical);
            Segment(3, "SegD", new Vector3(0f, -hh, 0f), horizontal);
            Segment(4, "SegE", new Vector3(-hw, -qh, 0f), vertical);
            Segment(5, "SegF", new Vector3(-hw, qh, 0f), vertical);
            Segment(6, "SegG", Vector3.zero, horizontal);
        }

        // ───────────────────────────── Materials ─────────────────────────────

        private struct Materials
        {
            public Material Checker;
            public Material Pillar;
            public Material Checkpoint;
        }

        private static Materials ResolveMaterials(Material checker, Material pillar, Material checkpoint)
        {
            if (checker != null && pillar != null && checkpoint != null)
                return new Materials { Checker = checker, Pillar = pillar, Checkpoint = checkpoint };

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
                lit = Shader.Find("Standard");

            if (checker == null)
            {
                checker = new Material(lit) { name = "RaceGate_Checker_Fallback" };
                checker.mainTexture = CreateCheckerTexture();
            }

            if (pillar == null)
            {
                pillar = new Material(lit) { name = "RaceGate_Pillar_Fallback" };
                pillar.color = new Color(0.12f, 0.13f, 0.16f);
            }

            if (checkpoint == null)
            {
                checkpoint = new Material(lit) { name = "RaceGate_Checkpoint_Fallback" };
                Color cyan = new Color(0.15f, 0.75f, 1f);
                checkpoint.color = cyan;
                checkpoint.EnableKeyword("_EMISSION");
                checkpoint.SetColor("_EmissionColor", cyan * 1.6f);
            }

            return new Materials { Checker = checker, Pillar = pillar, Checkpoint = checkpoint };
        }

        private static Texture2D CreateCheckerTexture()
        {
            const int w = 16, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "RaceGate_CheckerTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool white = ((x + y) & 1) == 0;
                    tex.SetPixel(x, y, white ? Color.white : new Color(0.05f, 0.05f, 0.05f));
                }
            }

            tex.Apply(false, true);
            return tex;
        }
    }
}
