using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
using TrackGeneration.Core;
using TrackGeneration.Splines;
using TrackGeneration.Mesh;
using TrackGeneration.Stunts;
using TrackGeneration.Gravity;
using TrackGeneration.Macro;

namespace TrackGeneration
{
    /// <summary>Which generation brain builds the track.</summary>
    public enum GenerationMode
    {
        /// <summary>New V1 brain: readable macro race sections first, box/prism geometry second.</summary>
        MacroSections,

        /// <summary>Old brain: spline shape decides the track. Kept until the macro system is stable.</summary>
        SplineLegacy
    }

    /// <summary>
    /// Master orchestrator for generating the entire track.
    /// Macro mode: Seed -> Macro layout -> Section frames -> Prism meshes -> Debug view.
    /// Legacy mode: Seed -> Splines -> Mesh -> Stunts -> Gravity.
    /// </summary>
    [AddComponentMenu("Track Generation/Track Generator")]
    [RequireComponent(typeof(TrackSeedManager))]
    public class TrackGenerator : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("MacroSections = new macro race-section generator. SplineLegacy = previous spline-first generator.")]
        public GenerationMode Mode = GenerationMode.MacroSections;

        [Header("Track Design — the designer's 10 controls")]
        [Tooltip("The personality of THIS track. Seed (control #1) lives on the Track Seed Manager.")]
        public TrackDesignerProfile Designer = new TrackDesignerProfile();

        [Header("Rulebook (advanced technical asset)")]
        [Tooltip("Defines what is POSSIBLE (limits, allowed features, safety). Not a designer control panel.")]
        public TrackConfig Config;

        [Header("Materials")]
        public Material MainRoadMaterial;
        public Material ShortcutRoadMaterial;
        public Material WallMaterial;

        [Header("Generation Output")]
        [SerializeField] private Transform trackRoot;

        [Header("Runtime Start")]
        [SerializeField] private bool generateOnStart = true;
        [SerializeField] private bool placeHovercraftOnStart = true;
        [SerializeField] private Transform hovercraft;
        [SerializeField, Min(0f)] private float startLineForwardOffset = 0f;
        [SerializeField, Min(0f)] private float fallbackRideHeight = 2.05f;
        [SerializeField] private bool resetHovercraftWithBackspace = true;

        [SerializeField, HideInInspector] private int generatedMeshCount;
        [SerializeField, HideInInspector] private int generatedVertexCount;
        [SerializeField, HideInInspector] private int generatedTriangleCount;

        // Current generated data
        public TrackData CurrentTrackData { get; private set; }

        /// <summary>Macro mode output: the generated section list (null in legacy mode).</summary>
        public List<GeneratedTrackSection> CurrentMacroSections { get; private set; }

        public int GeneratedMeshCount => generatedMeshCount;
        public int GeneratedVertexCount => generatedVertexCount;
        public int GeneratedTriangleCount => generatedTriangleCount;

        private TrackSeedManager _seedManager;

        private void Awake()
        {
            _seedManager = GetComponent<TrackSeedManager>();
        }

        private void Start()
        {
            if (generateOnStart)
            {
                GenerateTrack();
            }

            if (placeHovercraftOnStart)
            {
                PlaceHovercraftAtTrackStart();
            }
        }

        private void Update()
        {
            if (!resetHovercraftWithBackspace || Keyboard.current == null)
                return;

            if (Keyboard.current.backspaceKey.wasPressedThisFrame)
            {
                PlaceHovercraftAtTrackStart();
            }
        }

        [ContextMenu("Generate Track")]
        public void GenerateTrack()
        {
            if (Config == null)
            {
                Debug.LogError("[TrackGenerator] TrackConfig is missing!");
                return;
            }

            // 1. Setup Seed
            if (_seedManager == null) _seedManager = GetComponent<TrackSeedManager>();
            TrackSeed seed = _seedManager.InitializeSeed();
            
            // Subsystem RNGs
            Unity.Mathematics.Random splineRng = seed.CreateSubsystemRandom("Splines");
            Unity.Mathematics.Random branchRng = seed.CreateSubsystemRandom("Branches");
            Unity.Mathematics.Random stuntRng = seed.CreateSubsystemRandom("Stunts");
            Unity.Mathematics.Random gravityRng = seed.CreateSubsystemRandom("Gravity");

            // 2. Clear previous track
            if (trackRoot != null)
            {
                if (Application.isPlaying)
                {
                    trackRoot.gameObject.SetActive(false);
                    Destroy(trackRoot.gameObject);
                }
                else DestroyImmediate(trackRoot.gameObject);
            }

            CurrentTrackData = null;
            CurrentMacroSections = null;
            ClearGeneratedMeshStats();

            GameObject rootObj = new GameObject("GeneratedTrack_" + seed.BaseSeed);
            rootObj.transform.SetParent(this.transform, false);
            trackRoot = rootObj.transform;

            // ── Macro mode: readable race sections first, prism geometry second ──
            if (Mode == GenerationMode.MacroSections)
            {
                GenerateMacroTrack(rootObj, seed);
                return;
            }

            // ── Legacy spline mode below (kept until the macro system is stable) ──

            // 3. Generate Splines
            SplinePathGenerator splineGen = new SplinePathGenerator();
            SplineContainer mainCircuit = splineGen.GenerateMainCircuit(rootObj, Config, splineRng);
            float mainLength = SplineUtilities.GetSplineLength(mainCircuit);

            BranchPathGenerator branchGen = new BranchPathGenerator();
            List<TrackBranch> shortcuts = branchGen.GenerateBranches(rootObj, mainCircuit, Config, ref branchRng);

            List<float> shortcutLengths = new List<float>();
            foreach (var sc in shortcuts) shortcutLengths.Add(sc.Length);

            // 4. Determine Placements
            StuntPlacer stuntPlacer = new StuntPlacer();
            List<StuntPlacement> stuntPlacements = stuntPlacer.PlaceStunts(Config, ref stuntRng, mainCircuit, shortcuts);

            GravityZonePlacer gravityPlacer = new GravityZonePlacer();
            List<GravityZonePlacement> gravityPlacements = gravityPlacer.PlaceGravityZones(Config, ref gravityRng, mainLength, shortcutLengths, shortcuts);

            // 5. Generate Meshes
            TrackMeshBuilder meshBuilder = new TrackMeshBuilder();
            meshBuilder.BuildTrackMesh(mainCircuit, Config.MainRoadWidth, Config, MainRoadMaterial, WallMaterial, trackRoot, "MainCircuitMesh", shortcuts);

            for (int i = 0; i < shortcuts.Count; i++)
            {
                meshBuilder.BuildTrackMesh(shortcuts[i].Spline, Config.ShortcutRoadWidth, Config, ShortcutRoadMaterial, WallMaterial, shortcuts[i].Spline.transform, $"ShortcutMesh_{i+1}", null, shortcuts[i], mainCircuit);
            }

            // 6. Build Stunt Actors
            BuildStunts(stuntPlacements, mainCircuit, shortcuts);

            // 7. Build Gravity Zones
            BuildGravityZones(gravityPlacements, mainCircuit, shortcuts);

            // Store Data
            CurrentTrackData = new TrackData(seed, mainCircuit, mainLength, shortcuts, stuntPlacements, gravityPlacements);
            UpdateGeneratedMeshStats();

            int stuntCount = 0, padCount = 0;
            foreach (var p in stuntPlacements)
            {
                if (p.Type == StuntType.BoostPad) padCount++;
                else stuntCount++;
            }
            Debug.Log($"[TrackGenerator] Generated Track! Seed: {seed.BaseSeed}, Main Length: {mainLength:F1}m, Shortcuts: {shortcuts.Count}, Stunts: {stuntCount}, Boost Pads: {padCount}, Gravity Zones: {gravityPlacements.Count}");
        }

        [ContextMenu("Place Hovercraft At Track Start")]
        public void PlaceHovercraftAtTrackStart()
        {
            Transform craft = ResolveHovercraft();
            if (craft == null)
            {
                Debug.LogWarning("[TrackGenerator] Could not place hovercraft: no CraftCore or HovercraftRoot object found.");
                return;
            }

            if (!TryGetTrackStartFrame(out Vector3 position, out Vector3 forward, out Vector3 up))
            {
                Debug.LogWarning("[TrackGenerator] Could not place hovercraft: generate a track first.");
                return;
            }

            up = up.sqrMagnitude > 0.001f ? up.normalized : Vector3.up;
            forward = Vector3.ProjectOnPlane(forward, up);
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            float rideHeight = GetRideHeight(craft);
            Vector3 spawnPosition = position + forward * startLineForwardOffset + up * rideHeight;
            Quaternion spawnRotation = Quaternion.LookRotation(forward, up);

            Rigidbody rb = craft.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = spawnPosition;
                rb.rotation = spawnRotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
            else
            {
                craft.SetPositionAndRotation(spawnPosition, spawnRotation);
            }

            ResetHovercraftCamera(craft, rb);
        }

        /// <summary>
        /// Macro-section pipeline: layout brain chooses readable race sections and lays out
        /// connection frames; the prism builder turns each section into its own clean mesh.
        /// Stunt actors / gravity / shortcuts reconnect to this pipeline in a later pass.
        /// </summary>
        private void GenerateMacroTrack(GameObject rootObj, TrackSeed seed)
        {
            Unity.Mathematics.Random layoutRng = seed.CreateSubsystemRandom("MacroLayout");

            // Rulebook limits + designer intent → the internal values the generator consumes.
            ResolvedTrackGenerationConfig resolved = ResolvedTrackGenerationConfig.Resolve(Config, Designer);

            var layoutGenerator = new MacroTrackLayoutGenerator();
            List<GeneratedTrackSection> sections = layoutGenerator.Generate(resolved, ref layoutRng);

            // Global half-pipe rule: the resolved road profile is handed to the mesh
            // builder so EVERY section gets the rideable water-slide cross-section.
            var prismBuilder = new BoxPrismTrackMeshBuilder(resolved.RoadProfile);
            prismBuilder.Build(sections, MainRoadMaterial, WallMaterial, trackRoot);

            var visualizer = rootObj.AddComponent<MacroTrackDebugVisualizer>();
            visualizer.Initialize(seed.BaseSeed, sections, resolved.RoadProfile);

            CurrentMacroSections = sections;
            CurrentTrackData = null; // legacy data does not apply in macro mode
            UpdateGeneratedMeshStats();

            float length = sections.Count > 0 ? sections[sections.Count - 1].EndFrame.ArcLength : 0f;
            Debug.Log($"[TrackGenerator] Generated MACRO track! Seed: {seed.BaseSeed}, Sections: {sections.Count}, Length: {length:F1}m.");
        }

        private void ClearGeneratedMeshStats()
        {
            generatedMeshCount = 0;
            generatedVertexCount = 0;
            generatedTriangleCount = 0;
        }

        private void UpdateGeneratedMeshStats()
        {
            ClearGeneratedMeshStats();

            if (trackRoot == null)
                return;

            MeshFilter[] filters = trackRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                UnityEngine.Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                generatedMeshCount++;
                generatedVertexCount += mesh.vertexCount;

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (mesh.GetTopology(i) == MeshTopology.Triangles)
                    {
                        generatedTriangleCount += (int)(mesh.GetIndexCount(i) / 3);
                    }
                }
            }
        }

        private bool TryGetTrackStartFrame(out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            Transform root = trackRoot != null ? trackRoot : transform;

            if (Mode == GenerationMode.MacroSections && CurrentMacroSections != null && CurrentMacroSections.Count > 0)
            {
                TrackConnectionFrame start = CurrentMacroSections[0].StartFrame;
                position = root.TransformPoint(start.Position);
                forward = root.TransformDirection(start.Forward);
                up = root.TransformDirection(start.Up);
                return true;
            }

            if (CurrentTrackData != null && CurrentTrackData.MainCircuit != null)
            {
                SplineUtilities.EvaluateSplineFrameBanked(
                    CurrentTrackData.MainCircuit,
                    0f,
                    Config != null ? Config.BankingMultiplier : 0f,
                    Config != null ? Config.MaxBankAngle : 0f,
                    Config != null ? Config.MeshSegmentLength : 5f,
                    out Unity.Mathematics.float3 splinePosition,
                    out Unity.Mathematics.float3 splineForward,
                    out Unity.Mathematics.float3 splineUp,
                    out _);

                position = new Vector3(splinePosition.x, splinePosition.y, splinePosition.z);
                forward = new Vector3(splineForward.x, splineForward.y, splineForward.z);
                up = new Vector3(splineUp.x, splineUp.y, splineUp.z);
                return true;
            }

            position = Vector3.zero;
            forward = Vector3.forward;
            up = Vector3.up;
            return false;
        }

        private Transform ResolveHovercraft()
        {
            if (hovercraft != null) return hovercraft;

            var core = UnityEngine.Object.FindAnyObjectByType<global::CraftCore>();
            if (core != null)
            {
                hovercraft = core.transform;
                return hovercraft;
            }

            GameObject namedCraft = GameObject.Find("HovercraftRootV2") ?? GameObject.Find("HovercraftRoot");
            if (namedCraft != null)
            {
                hovercraft = namedCraft.transform;
            }

            return hovercraft;
        }

        private float GetRideHeight(Transform craft)
        {
            var hoverArray = craft.GetComponent<global::HoverStabilizerArray>();
            if (hoverArray != null)
            {
                return Mathf.Max(0f, hoverArray.hoverHeight + 0.05f);
            }

            return fallbackRideHeight;
        }

        private static void ResetHovercraftCamera(Transform craft, Rigidbody rb)
        {
            var hovercraftCamera = UnityEngine.Object.FindAnyObjectByType<global::HovercraftCamera>();
            if (hovercraftCamera == null) return;

            hovercraftCamera.target = craft;
            hovercraftCamera.targetRigidbody = rb != null ? rb : craft.GetComponent<Rigidbody>();
            hovercraftCamera.craftCore = craft.GetComponent<global::CraftCore>();
            hovercraftCamera.ResetCameraImmediate();
        }

        private void BuildStunts(List<StuntPlacement> placements, SplineContainer mainCircuit, List<TrackBranch> shortcuts)
        {
            GameObject stuntsRoot = new GameObject("Stunts");
            stuntsRoot.transform.SetParent(trackRoot, false);

            foreach (var placement in placements)
            {
                SplineContainer targetSpline = placement.SplineIndex == 0 ? mainCircuit : shortcuts[placement.SplineIndex - 1].Spline;
                float roadWidth = placement.SplineIndex == 0 ? Config.MainRoadWidth : Config.ShortcutRoadWidth;

                // Banked frame: ramps and pads must sit flush on the banked road surface.
                SplineUtilities.EvaluateSplineFrameBanked(targetSpline, placement.T, Config.BankingMultiplier, Config.MaxBankAngle, Config.MeshSegmentLength,
                    out Unity.Mathematics.float3 pos, out Unity.Mathematics.float3 tan, out Unity.Mathematics.float3 up, out Unity.Mathematics.float3 right);

                SplineFrame frame = new SplineFrame {
                    T = placement.T,
                    Position = pos,
                    Tangent = tan,
                    Up = up,
                    Right = right
                };

                if (placement.Type == StuntType.Ramp)
                {
                    // Full chain from the tuning file: Jump Ramp → Air Gap → Landing Ramp.
                    // (The recovery straight is guaranteed by the placer's site validation.)
                    GameObject chainObj = new GameObject($"JumpGap_{placement.SplineIndex}_{placement.T:F2}");
                    chainObj.transform.SetParent(stuntsRoot.transform, true);

                    // Launch half.
                    GameObject launchObj = new GameObject("LaunchRamp");
                    launchObj.transform.SetParent(chainObj.transform, true);
                    RampActor launch = launchObj.AddComponent<RampActor>();
                    launch.Mode = RampMode.Launch;
                    launch.RampLength = placement.Length;
                    launch.RampHeight = placement.Height;
                    launch.Generate(frame, roadWidth, placement.Lane);

                    // Landing half — placed arc-accurately down the road (t is NOT arc-uniform).
                    float landingT = SplineUtilities.GetTAtDistance(targetSpline, placement.T, placement.Length + placement.AirGapLength);
                    SplineUtilities.EvaluateSplineFrameBanked(targetSpline, landingT, Config.BankingMultiplier, Config.MaxBankAngle, Config.MeshSegmentLength,
                        out pos, out tan, out up, out right);
                    SplineFrame landingFrame = new SplineFrame {
                        T = landingT,
                        Position = pos,
                        Tangent = tan,
                        Up = up,
                        Right = right
                    };

                    GameObject landingObj = new GameObject("LandingRamp");
                    landingObj.transform.SetParent(chainObj.transform, true);
                    RampActor landing = landingObj.AddComponent<RampActor>();
                    landing.Mode = RampMode.Landing;
                    landing.RampLength = placement.LandingLength;
                    // Slightly lower than the launch lip: the craft arrives descending and the
                    // catch surface eases it down instead of slamming the nose.
                    landing.RampHeight = placement.Height * 0.85f;
                    landing.Generate(landingFrame, roadWidth, placement.Lane);
                }
                else if (placement.Type == StuntType.BoostPad)
                {
                    GameObject padObj = new GameObject($"BoostPad_{placement.SplineIndex}_{placement.T:F2}");
                    padObj.transform.SetParent(stuntsRoot.transform, true);
                    BoostPadActor pad = padObj.AddComponent<BoostPadActor>();
                    pad.PadLength = placement.Length;
                    pad.Generate(frame, roadWidth, placement.Lane, Config.BoostPadStrength);
                }
                else if (placement.Type == StuntType.WallRide)
                {
                    GameObject wallRideObj = new GameObject($"WallRide_{placement.SplineIndex}_{placement.T:F2}");
                    wallRideObj.transform.SetParent(stuntsRoot.transform, true);
                    WallRideActor wallRide = wallRideObj.AddComponent<WallRideActor>();
                    wallRide.SectionLength = placement.Length;
                    
                    // Sample frames along the wall ride length
                    int numFrames = 20;
                    SplineFrame[] frames = new SplineFrame[numFrames];
                    float tStep = (placement.Length / SplineUtilities.GetSplineLength(targetSpline)) / numFrames;
                    for(int i = 0; i < numFrames; i++)
                    {
                        float currentT = placement.T + i * tStep;
                        SplineUtilities.EvaluateSplineFrame(targetSpline, currentT, out pos, out tan, out up, out right);
                        frames[i] = new SplineFrame { T = currentT, Position = pos, Tangent = tan, Up = up, Right = right };
                    }

                    wallRide.Generate(frames, roadWidth);
                }
            }
        }

        private void BuildGravityZones(List<GravityZonePlacement> placements, SplineContainer mainCircuit, List<TrackBranch> shortcuts)
        {
            GameObject gravityRoot = new GameObject("GravityZones");
            gravityRoot.transform.SetParent(trackRoot, false);

            foreach (var placement in placements)
            {
                SplineContainer targetSpline = placement.SplineIndex == 0 ? mainCircuit : shortcuts[placement.SplineIndex - 1].Spline;
                float roadWidth = placement.SplineIndex == 0 ? Config.MainRoadWidth : Config.ShortcutRoadWidth;

                GameObject zoneObj = new GameObject($"GravityZone_{placement.SplineIndex}_{placement.T:F2}");
                zoneObj.transform.SetParent(gravityRoot.transform, false);
                GravityZone zone = zoneObj.AddComponent<GravityZone>();

                // Sample frames
                int numFrames = 10;
                SplineFrame[] frames = new SplineFrame[numFrames];
                float tStep = (placement.Length / SplineUtilities.GetSplineLength(targetSpline)) / numFrames;
                for(int i = 0; i < numFrames; i++)
                {
                    float currentT = placement.T + i * tStep;
                    SplineUtilities.EvaluateSplineFrame(targetSpline, currentT, out Unity.Mathematics.float3 pos, out Unity.Mathematics.float3 tan, out Unity.Mathematics.float3 up, out Unity.Mathematics.float3 right);
                    frames[i] = new SplineFrame { T = currentT, Position = pos, Tangent = tan, Up = up, Right = right };
                }

                zoneObj.transform.position = frames[numFrames / 2].Position;
                zone.Generate(frames, roadWidth, placement.Strength, placement.Radius);
            }
        }
    }
}
