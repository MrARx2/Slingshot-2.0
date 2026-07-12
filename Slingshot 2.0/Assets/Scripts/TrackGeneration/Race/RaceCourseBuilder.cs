using System.Collections.Generic;
using UnityEngine;
using TrackGeneration.Macro;

namespace TrackGeneration.Race
{
    /// <summary>
    /// Builds the race course onto a generated macro track: a start/finish arch (checkered)
    /// at a small arc offset past the spawn point, and N numbered checkpoint gates evenly
    /// spaced around the lap. Gate visuals are pure decoration — no colliders, so a wide or
    /// airborne line never clips a pillar. Detection is handled by <see cref="RaceCourse"/>.
    /// </summary>
    public static class RaceCourseBuilder
    {
        private const float MinGateSeparation = 10f;

        /// <summary>
        /// Builds the course under the track root. <paramref name="startLineArcOffset"/> pushes
        /// the start/finish line ahead of the spawn frame so the first crossing (not the spawn
        /// itself) starts the clock. Returns null when the section list can't host a course.
        /// </summary>
        public static RaceCourse Build(
            Transform trackRoot,
            List<GeneratedTrackSection> sections,
            TrackRoadProfileSettings roadProfile,
            int checkpointCount,
            float startLineArcOffset)
        {
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

            Materials mats = CreateMaterials();

            // Start/finish sits a little way down the track from the spawn frame.
            float startArc = Mathf.Min(startLineArcOffset, total * 0.25f);
            if (!MacroTrackSampler.TrySampleFrame(sections, startArc, out TrackConnectionFrame startFrame))
            {
                Debug.LogWarning("[RaceCourseBuilder] Could not sample the start line frame — skipped.");
                if (Application.isPlaying) Object.Destroy(courseObj);
                else Object.DestroyImmediate(courseObj);
                return null;
            }
            course.StartFinishGate = BuildGate(courseObj.transform, startFrame, roadProfile, isStartFinish: true, index: -1, mats);

            // Checkpoints: evenly spaced boundaries between start line and finish line.
            // Frames landing in an air gap get nudged forward by the sampler, so re-sort by
            // distance from the start line and drop any that collapsed onto a neighbour.
            float spacing = total / (checkpointCount + 1);
            var frames = new List<TrackConnectionFrame>();
            for (int i = 0; i < checkpointCount; i++)
            {
                float arc = Mathf.Repeat(startArc + spacing * (i + 1), total);
                if (MacroTrackSampler.TrySampleFrame(sections, arc, out TrackConnectionFrame f))
                {
                    frames.Add(f);
                }
            }
            frames.Sort((a, b) =>
                Mathf.Repeat(a.ArcLength - startArc, total).CompareTo(Mathf.Repeat(b.ArcLength - startArc, total)));

            float lastRel = 0f;
            foreach (TrackConnectionFrame f in frames)
            {
                float rel = Mathf.Repeat(f.ArcLength - startArc, total);
                if (rel < lastRel + MinGateSeparation || rel > total - MinGateSeparation) continue;
                lastRel = rel;

                RaceGate gate = BuildGate(courseObj.transform, f, roadProfile, isStartFinish: false, index: course.Checkpoints.Count, mats);
                course.Checkpoints.Add(gate);
            }

            if (course.Checkpoints.Count < checkpointCount)
            {
                Debug.LogWarning($"[RaceCourseBuilder] Placed {course.Checkpoints.Count}/{checkpointCount} checkpoints — some sample points fell in air gaps too close to a neighbour.");
            }

            return course;
        }

        // ─────────────────────────────── Gates ───────────────────────────────

        private static RaceGate BuildGate(
            Transform parent,
            TrackConnectionFrame frame,
            TrackRoadProfileSettings roadProfile,
            bool isStartFinish,
            int index,
            Materials mats)
        {
            GameObject go = new GameObject(isStartFinish ? "StartFinishLine" : $"Checkpoint_{index + 1}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = frame.Position;
            go.transform.localRotation = Quaternion.LookRotation(frame.Forward, frame.Up);

            RaceGate gate = go.AddComponent<RaceGate>();
            gate.IsStartFinish = isStartFinish;
            gate.CheckpointIndex = index;
            gate.ArcLength = frame.ArcLength;
            gate.DetectionHalfWidth = frame.Width * 0.75f;
            gate.DetectionBottom = -6f;
            gate.DetectionTop = 45f;

            float sideHeight = frame.SideHeight > 0.01f
                ? frame.SideHeight
                : (roadProfile != null ? roadProfile.SideHeight : 3.5f);

            BuildGateVisuals(go.transform, frame.Width, sideHeight, isStartFinish, index, mats);
            return gate;
        }

        private static void BuildGateVisuals(Transform gate, float roadWidth, float sideHeight, bool isStartFinish, int index, Materials mats)
        {
            float halfWidth = roadWidth * 0.5f;
            float pillarHeight = sideHeight + 8f;
            float pillarThickness = isStartFinish ? 1.2f : 0.7f;
            Material pillarMat = isStartFinish ? mats.Pillar : mats.Checkpoint;
            Material beamMat = isStartFinish ? mats.Checker : mats.Checkpoint;

            // Pillars just outside the half-pipe edges.
            float pillarX = halfWidth + pillarThickness;
            CreatePart(gate, "Pillar_L", new Vector3(-pillarX, pillarHeight * 0.5f, 0f),
                new Vector3(pillarThickness, pillarHeight, pillarThickness), pillarMat);
            CreatePart(gate, "Pillar_R", new Vector3(pillarX, pillarHeight * 0.5f, 0f),
                new Vector3(pillarThickness, pillarHeight, pillarThickness), pillarMat);

            // Overhead beam spanning the road.
            float beamHeight = isStartFinish ? 1.6f : 0.8f;
            CreatePart(gate, "Beam", new Vector3(0f, pillarHeight - beamHeight * 0.5f, 0f),
                new Vector3(pillarX * 2f + pillarThickness, beamHeight, pillarThickness * 0.6f), beamMat);

            if (isStartFinish)
            {
                // Checkered stripe across the road surface (sinks into the rising half-pipe sides).
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
        // Seven-segment digits built from cubes: real depth-tested geometry, unlike
        // TextMesh whose GUI font shader draws through everything (ZTest Always).

        private const float DigitHeight = 3.2f;
        private const float DigitWidth = 1.8f;
        private const float DigitStroke = 0.4f;
        private const float DigitDepth = 0.25f;

        // Segment bit order: A(top) B(top-right) C(bottom-right) D(bottom) E(bottom-left) F(top-left) G(middle)
        private static readonly byte[] DigitSegments =
        {
            0b0111111, // 0: ABCDEF
            0b0000110, // 1: BC
            0b1011011, // 2: ABDEG
            0b1001111, // 3: ABCDG
            0b1100110, // 4: BCFG
            0b1101101, // 5: ACDFG
            0b1111101, // 6: ACDEFG
            0b0000111, // 7: ABC
            0b1111111, // 8: all
            0b1101111  // 9: ABCDFG
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

        private static Materials CreateMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");

            var checker = new Material(lit) { name = "RaceGate_Checker" };
            checker.mainTexture = CreateCheckerTexture();

            var pillar = new Material(lit) { name = "RaceGate_Pillar" };
            pillar.color = new Color(0.12f, 0.13f, 0.16f);

            var checkpoint = new Material(lit) { name = "RaceGate_Checkpoint" };
            Color cyan = new Color(0.15f, 0.75f, 1f);
            checkpoint.color = cyan;
            checkpoint.EnableKeyword("_EMISSION");
            checkpoint.SetColor("_EmissionColor", cyan * 1.6f);

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
