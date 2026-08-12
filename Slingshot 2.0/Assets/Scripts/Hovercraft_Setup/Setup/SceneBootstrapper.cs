using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor menu that programmatically creates a V2 modular hovercraft test scene.
/// Use: Tools > Hovercraft > Setup V2 Hovercraft Test Scene
/// </summary>
public class SceneBootstrapper : MonoBehaviour
{
#if UNITY_EDITOR

    // ══════════════════════════════════════════════════════════════
    //  V2 MODULAR SCENE SETUP
    // ══════════════════════════════════════════════════════════════

    [MenuItem("Tools/Hovercraft/Setup V2 Hovercraft Test Scene")]
    public static void SetupV2TestScene()
    {
        var mats = CreatePhysicsMaterials();
        CreateEnvironment(mats.ground);

        // ── Hovercraft Root ────────────────────────────────────────
        GameObject craft = CreateCraftVisuals(mats.craft);

        // ── Create Thruster Nodes ──────────────────────────────────
        GameObject thrusterParent = new GameObject("Thrusters");
        thrusterParent.transform.parent = craft.transform;
        thrusterParent.transform.localPosition = Vector3.zero;
        thrusterParent.transform.localRotation = Quaternion.identity;

        float tX = 0.8f, tZ = 1.1f, tY = -0.2f;

        // 4x Hover thrusters (bottom corners, pointing down → local Y points UP)
        ThrusterNode hoverFL = CreateThrusterNode(thrusterParent.transform, "Hover_FL",
            new Vector3(-tX, tY, tZ), Quaternion.identity,
            ThrusterNode.ThrusterRole.Hover, 100f);
        ThrusterNode hoverFR = CreateThrusterNode(thrusterParent.transform, "Hover_FR",
            new Vector3(tX, tY, tZ), Quaternion.identity,
            ThrusterNode.ThrusterRole.Hover, 100f);
        ThrusterNode hoverRL = CreateThrusterNode(thrusterParent.transform, "Hover_RL",
            new Vector3(-tX, tY, -tZ), Quaternion.identity,
            ThrusterNode.ThrusterRole.Hover, 100f);
        ThrusterNode hoverRR = CreateThrusterNode(thrusterParent.transform, "Hover_RR",
            new Vector3(tX, tY, -tZ), Quaternion.identity,
            ThrusterNode.ThrusterRole.Hover, 100f);

        // Main thruster (rear, pointing backward → local Y points FORWARD)
        ThrusterNode mainThruster = CreateThrusterNode(thrusterParent.transform, "Main_Rear",
            new Vector3(0f, 0f, -1.5f), Quaternion.Euler(-90f, 0f, 0f),
            ThrusterNode.ThrusterRole.Main, 80f);

        // Brake thruster (front, pointing forward → local Y points BACKWARD)
        ThrusterNode brakeThruster = CreateThrusterNode(thrusterParent.transform, "Brake_Front",
            new Vector3(0f, 0f, 1.5f), Quaternion.Euler(90f, 0f, 0f),
            ThrusterNode.ThrusterRole.Brake, 40f);

        // Corner strafe thrusters
        ThrusterNode strafeFL = CreateThrusterNode(thrusterParent.transform, "Strafe_Front_Left",
            new Vector3(-1.0f, 0f, tZ), Quaternion.Euler(0f, 0f, -90f),
            ThrusterNode.ThrusterRole.Strafe, 30f);
        ThrusterNode strafeBL = CreateThrusterNode(thrusterParent.transform, "Strafe_Back_Left",
            new Vector3(-1.0f, 0f, -tZ), Quaternion.Euler(0f, 0f, -90f),
            ThrusterNode.ThrusterRole.Strafe, 30f);
        ThrusterNode strafeFR = CreateThrusterNode(thrusterParent.transform, "Strafe_Front_Right",
            new Vector3(1.0f, 0f, tZ), Quaternion.Euler(0f, 0f, 90f),
            ThrusterNode.ThrusterRole.Strafe, 30f);
        ThrusterNode strafeBR = CreateThrusterNode(thrusterParent.transform, "Strafe_Back_Right",
            new Vector3(1.0f, 0f, -tZ), Quaternion.Euler(0f, 0f, 90f),
            ThrusterNode.ThrusterRole.Strafe, 30f);

        // ── Wire up the V2 modular stack ───────────────────────────
        CraftCore core = craft.AddComponent<CraftCore>();
        core.pilotInput = craft.AddComponent<PilotCommandInterface>();
        core.telemetry = craft.AddComponent<TelemetryMainframe>();
        core.vectoring = craft.AddComponent<VectoringComputer>();
        core.traction = craft.AddComponent<TractionCore>();
        core.hoverArray = craft.AddComponent<HoverStabilizerArray>();
        core.drive = craft.AddComponent<DriveCore>();
        core.vectorThrusters = craft.AddComponent<VectorThrusterArray>();
        core.attitude = craft.AddComponent<AttitudeControlArray>();
        core.overcharge = craft.AddComponent<OverchargeCore>();
        core.energy = craft.AddComponent<EnergyCore>();
        core.feedback = craft.AddComponent<CraftFeedbackSystem>();
        craft.AddComponent<SectionAdaptiveSuspension>(); // per-track-section suspension tuning

        // Wire visual bindings to the feedback system
        core.feedback.thrusterVisuals = new List<CraftFeedbackSystem.ThrusterVisualBinding>();
        ThrusterNode[] allNodes = { hoverFL, hoverFR, hoverRL, hoverRR, mainThruster, brakeThruster, strafeFL, strafeBL, strafeFR, strafeBR };
        foreach (var node in allNodes)
        {
            core.feedback.thrusterVisuals.Add(new CraftFeedbackSystem.ThrusterVisualBinding
            {
                node = node,
                label = node.label
            });
        }

        // Initialize the thruster bus (auto-added by CraftCore's RequireComponent,
        // but do not rely on that side effect).
        ThrusterBus bus = craft.GetComponent<ThrusterBus>();
        if (bus == null) bus = craft.AddComponent<ThrusterBus>();
        bus.DiscoverThrusters();

        // ── Camera and HUD ─────────────────────────────────────────
        Rigidbody rb = craft.GetComponent<Rigidbody>();
        GameObject camObj = SetupCamera(craft.transform, rb);

        // Player HUD (always on) + debug HUD (F2)
        CraftHUD existingHud = camObj.GetComponent<CraftHUD>();
        if (existingHud != null) Object.DestroyImmediate(existingHud);
        CraftHUD hud = camObj.AddComponent<CraftHUD>();
        hud.craftCore = core;

        CraftDebugHUD existingDebug = camObj.GetComponent<CraftDebugHUD>();
        if (existingDebug != null) Object.DestroyImmediate(existingDebug);
        CraftDebugHUD debugHud = camObj.AddComponent<CraftDebugHUD>();
        debugHud.craftCore = core;

        SetupLighting();

        Debug.Log("<b>[Hovercraft V2]</b> Setup complete! Press Play to test the modular craft.");
        Debug.Log("<b>[Hovercraft V2]</b> Controls: W/S = Throttle/Brake, Mouse X = Steering, Mouse Y = Pitch shift, A/D = Edge shift, Shift = Grip breaker, Space = BOOST (instant, 10s cooldown), Q/E = Roof/Bottom thrusters, R = Stabilizer toggle, P = Cycle camera view, O = Camera ground orientation, F2 = Debug HUD.");

        Selection.activeGameObject = craft;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    // ══════════════════════════════════════════════════════════════
    //  SHARED HELPERS
    // ══════════════════════════════════════════════════════════════

    struct PhysMats { public PhysicsMaterial ground, craft; }

    static PhysMats CreatePhysicsMaterials()
    {
        PhysicsMaterial groundMat = new PhysicsMaterial("GroundPhysics");
        groundMat.dynamicFriction = 0.3f;
        groundMat.staticFriction = 0.3f;
        groundMat.bounciness = 0.1f;
        groundMat.frictionCombine = PhysicsMaterialCombine.Average;
        groundMat.bounceCombine = PhysicsMaterialCombine.Average;

        PhysicsMaterial craftMat = new PhysicsMaterial("CraftPhysics");
        craftMat.dynamicFriction = 0.15f;
        craftMat.staticFriction = 0.15f;
        craftMat.bounciness = 0.2f;
        craftMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        craftMat.bounceCombine = PhysicsMaterialCombine.Average;

        return new PhysMats { ground = groundMat, craft = craftMat };
    }

    static void CreateEnvironment(PhysicsMaterial groundMat)
    {
        // Ground
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(200f, 1f, 200f);
        ground.GetComponent<Collider>().material = groundMat;
        ground.isStatic = true;

        Renderer groundRend = ground.GetComponent<Renderer>();
        Material groundVisualMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundVisualMat.color = new Color(0.2f, 0.22f, 0.25f);
        groundRend.material = groundVisualMat;

        // Ramp
        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Ramp";
        ramp.transform.position = new Vector3(0f, 1f, 30f);
        ramp.transform.localScale = new Vector3(8f, 1f, 12f);
        ramp.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);
        ramp.GetComponent<Collider>().material = groundMat;
        ramp.isStatic = true;

        Material rampVisualMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        rampVisualMat.color = new Color(0.6f, 0.35f, 0.1f);
        ramp.GetComponent<Renderer>().material = rampVisualMat;

        // Steep ramp
        GameObject ramp2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp2.name = "Ramp_Steep";
        ramp2.transform.position = new Vector3(20f, 1.5f, 25f);
        ramp2.transform.localScale = new Vector3(6f, 1f, 10f);
        ramp2.transform.rotation = Quaternion.Euler(-30f, 15f, 0f);
        ramp2.GetComponent<Collider>().material = groundMat;
        ramp2.isStatic = true;
        ramp2.GetComponent<Renderer>().material = rampVisualMat;

        // Half-pipe
        GameObject halfPipeParent = new GameObject("HalfPipe");
        halfPipeParent.transform.position = new Vector3(-25f, 0f, 15f);
        halfPipeParent.isStatic = true;

        Material halfPipeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        halfPipeMat.color = new Color(0.15f, 0.4f, 0.5f);

        GameObject wallL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallL.name = "Wall_Left";
        wallL.transform.parent = halfPipeParent.transform;
        wallL.transform.localPosition = new Vector3(-6f, 2f, 0f);
        wallL.transform.localScale = new Vector3(1f, 6f, 20f);
        wallL.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
        wallL.GetComponent<Collider>().material = groundMat;
        wallL.GetComponent<Renderer>().material = halfPipeMat;
        wallL.isStatic = true;

        GameObject wallR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallR.name = "Wall_Right";
        wallR.transform.parent = halfPipeParent.transform;
        wallR.transform.localPosition = new Vector3(6f, 2f, 0f);
        wallR.transform.localScale = new Vector3(1f, 6f, 20f);
        wallR.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);
        wallR.GetComponent<Collider>().material = groundMat;
        wallR.GetComponent<Renderer>().material = halfPipeMat;
        wallR.isStatic = true;

        GameObject halfPipeFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        halfPipeFloor.name = "Floor";
        halfPipeFloor.transform.parent = halfPipeParent.transform;
        halfPipeFloor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        halfPipeFloor.transform.localScale = new Vector3(8f, 0.5f, 20f);
        halfPipeFloor.GetComponent<Collider>().material = groundMat;
        halfPipeFloor.GetComponent<Renderer>().material = halfPipeMat;
        halfPipeFloor.isStatic = true;

        // Markers
        Material markerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        markerMat.color = new Color(0.9f, 0.2f, 0.3f);

        for (int i = 0; i < 5; i++)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"Marker_{i}";
            float angle = i * 72f * Mathf.Deg2Rad;
            marker.transform.position = new Vector3(
                Mathf.Cos(angle) * 40f, 1.5f, Mathf.Sin(angle) * 40f);
            marker.transform.localScale = new Vector3(1f, 3f, 1f);
            marker.GetComponent<Collider>().material = groundMat;
            marker.GetComponent<Renderer>().material = markerMat;
            marker.isStatic = true;
        }
    }

    static GameObject CreateCraftVisuals(PhysicsMaterial craftMat)
    {
        GameObject craft = new GameObject("HovercraftRoot");
        craft.transform.position = new Vector3(0f, 3f, 0f);

        Rigidbody rb = craft.AddComponent<Rigidbody>();
        rb.mass = 5f;
        rb.angularDamping = 2f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        BoxCollider col = craft.AddComponent<BoxCollider>();
        col.size = new Vector3(2f, 0.5f, 3f);
        col.center = Vector3.zero;
        col.material = craftMat;

        GameObject visual = new GameObject("VisualModel");
        visual.transform.parent = craft.transform;
        visual.transform.localPosition = Vector3.zero;

        Material craftBodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        craftBodyMat.color = new Color(0.1f, 0.6f, 0.9f);
        Material craftAccentMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        craftAccentMat.color = new Color(0.9f, 0.4f, 0.1f);
        Material cockpitMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        cockpitMat.color = new Color(0.15f, 0.15f, 0.2f);
        Material engineMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        engineMat.color = new Color(0.3f, 0.3f, 0.35f);

        CreateVisualPart(visual.transform, "Body", Vector3.zero, new Vector3(1.8f, 0.35f, 2.8f), Quaternion.identity, craftBodyMat);
        CreateVisualPart(visual.transform, "Nose", new Vector3(0f, 0.05f, 1.6f), new Vector3(1.2f, 0.25f, 0.8f), Quaternion.identity, craftAccentMat);
        CreateVisualPart(visual.transform, "Cockpit", new Vector3(0f, 0.3f, 0.3f), new Vector3(0.8f, 0.3f, 0.8f), Quaternion.identity, cockpitMat);
        CreateVisualPart(visual.transform, "Fin_Left", new Vector3(-1.1f, 0.1f, -0.8f), new Vector3(0.3f, 0.5f, 1.2f), Quaternion.Euler(0f, 0f, -15f), craftAccentMat);
        CreateVisualPart(visual.transform, "Fin_Right", new Vector3(1.1f, 0.1f, -0.8f), new Vector3(0.3f, 0.5f, 1.2f), Quaternion.Euler(0f, 0f, 15f), craftAccentMat);
        CreateVisualPart(visual.transform, "Engine", new Vector3(0f, 0.15f, -1.3f), new Vector3(1.4f, 0.4f, 0.5f), Quaternion.identity, engineMat);

        return craft;
    }

    static void CreateVisualPart(Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.parent = parent;
        part.transform.localPosition = pos;
        part.transform.localScale = scale;
        part.transform.localRotation = rot;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().material = mat;
    }

    static ThrusterNode CreateThrusterNode(Transform parent, string name, Vector3 localPos, Quaternion localRot,
                                            ThrusterNode.ThrusterRole role, float maxForce)
    {
        GameObject go = new GameObject(name);
        go.transform.parent = parent;
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        ThrusterNode t = go.AddComponent<ThrusterNode>();
        t.role = role;
        t.maxForce = maxForce;
        t.label = name;
        return t;
    }

    static GameObject SetupCamera(Transform target, Rigidbody rb)
    {
        Camera mainCam = Camera.main;
        GameObject camObj;
        if (mainCam != null)
        {
            camObj = mainCam.gameObject;
        }
        else
        {
            camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            camObj.AddComponent<Camera>();
        }

        camObj.transform.position = new Vector3(0f, 5f, -10f);

        // Destroy Cinemachine to prevent fight
        Component[] comps = camObj.GetComponents<Component>();
        foreach (var c in comps)
        {
            if (c != null && c.GetType().Name.Contains("CinemachineBrain"))
            {
                Object.DestroyImmediate(c);
            }
        }

        HovercraftCamera existingCam = camObj.GetComponent<HovercraftCamera>();
        if (existingCam != null) Object.DestroyImmediate(existingCam);

        HovercraftCamera hovCam = camObj.AddComponent<HovercraftCamera>();
        hovCam.target = target;
        hovCam.targetRigidbody = rb;

        return camObj;
    }

    static void SetupLighting()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        bool hasDirectional = false;
        foreach (var light in lights)
        {
            if (light.type == LightType.Directional)
            {
                hasDirectional = true;
                break;
            }
        }

        if (!hasDirectional)
        {
            GameObject lightObj = new GameObject("Directional Light");
            Light dirLight = lightObj.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.intensity = 1.2f;
            dirLight.color = new Color(1f, 0.95f, 0.85f);
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

#endif
}
