using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Runtime Game-view instrumentation for inspecting the actual direction and
    /// output of every assembled thruster, including connector-driven motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3ThrusterDebugView : MonoBehaviour
    {
        private sealed class ThrusterVisual
        {
            public RuntimeThrusterInstance Thruster;
            public RuntimeGimbalInstance Gimbal;
            public GameObject Root;
            public LineRenderer FacingArrow;
            public LineRenderer ForceArrow;
        }

        private static readonly Color CoolColor =
            new Color(0.12f, 0.65f, 1f, 1f);
        private static readonly Color NormalColor =
            new Color(0.18f, 1f, 0.52f, 1f);
        private static readonly Color SafeLimitColor =
            new Color(1f, 0.82f, 0.08f, 1f);
        private static readonly Color LockedOutColor =
            new Color(1f, 0.12f, 0.08f, 1f);
        private static readonly Color SurfaceNormalColor =
            new Color(0.78f, 0.32f, 1f, 1f);
        private static readonly Color CraftUpColor =
            new Color(0.95f, 0.98f, 1f, 0.9f);

        [Header("Visibility")]
        [SerializeField] private bool showVisualization;
        [SerializeField] private bool showWorldLabels = true;

        [Header("Scale")]
        [Min(0.1f), SerializeField] private float facingArrowLength = 1.35f;
        [Min(0.1f), SerializeField] private float maximumForceArrowLength = 3.5f;
        [Min(0.001f), SerializeField] private float facingLineWidth = 0.035f;
        [Min(0.001f), SerializeField] private float forceLineWidth = 0.11f;
        [Min(0f), SerializeField] private float firingThreshold = 0.005f;

        private readonly List<ThrusterVisual> visuals =
            new List<ThrusterVisual>(16);
        private V3CraftRuntime runtime;
        private V3HoverController hover;
        private GameObject surfaceVisualRoot;
        private LineRenderer surfaceNormalArrow;
        private LineRenderer craftUpArrow;
        private Material lineMaterial;
        private GUIStyle titleStyle;
        private GUIStyle legendStyle;
        private GUIStyle nameStyle;
        private GUIStyle valueStyle;
        private GUIStyle worldLabelStyle;

        public bool ShowVisualization
        {
            get => showVisualization;
            set
            {
                showVisualization = value;
                ApplyVisibility();
            }
        }

        public bool ShowWorldLabels
        {
            get => showWorldLabels;
            set => showWorldLabels = value;
        }

        public int ThrusterCount => visuals.Count;

        public int ActiveThrusterCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < visuals.Count; i++)
                {
                    if (IsFiring(visuals[i].Thruster))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Initialize(V3CraftRuntime value)
        {
            ClearVisuals();
            runtime = value;
            if (runtime == null)
            {
                return;
            }

            EnsureLineMaterial();
            hover = runtime.GetComponent<V3HoverController>();
            for (int i = 0; i < runtime.InstalledParts.Count; i++)
            {
                if (!(runtime.InstalledParts[i] is RuntimeThrusterInstance thruster))
                {
                    continue;
                }

                var visualRoot = new GameObject(
                    $"Debug Vector - {thruster.ParentSocketId}");
                visualRoot.transform.SetParent(transform, false);
                visualRoot.hideFlags = HideFlags.DontSave;

                var visual = new ThrusterVisual
                {
                    Thruster = thruster,
                    Gimbal = thruster.GetComponentInParent<RuntimeGimbalInstance>(),
                    Root = visualRoot,
                    FacingArrow = CreateLine(
                        visualRoot.transform,
                        "Facing",
                        CoolColor,
                        facingLineWidth),
                    ForceArrow = CreateLine(
                        visualRoot.transform,
                        "Applied Force",
                        CoolColor,
                        forceLineWidth)
                };
                visual.ForceArrow.sortingOrder = 501;
                visuals.Add(visual);
            }

            CreateSurfaceVisuals();
            ApplyVisibility();
            RefreshVisualization();
        }

        public void ToggleVisualization()
        {
            ShowVisualization = !ShowVisualization;
        }

        public void RefreshVisualization()
        {
            if (!showVisualization)
            {
                return;
            }

            Camera viewCamera = Camera.main;
            for (int i = 0; i < visuals.Count; i++)
            {
                UpdateVisual(visuals[i], viewCamera);
            }

            UpdateSurfaceVisual(viewCamera);
        }

        public static Vector3 GetWorldFacingDirection(
            RuntimeThrusterInstance thruster)
        {
            if (thruster == null || thruster.ThrusterDefinition == null)
            {
                return Vector3.zero;
            }

            return thruster.transform.TransformDirection(
                thruster.ThrusterDefinition.LocalThrustDirection).normalized;
        }

        private void LateUpdate()
        {
            RefreshVisualization();
        }

        private void OnGUI()
        {
            if (!showVisualization || runtime == null)
            {
                return;
            }

            EnsureGuiStyles();
            DrawInspectorPanel();
            if (showWorldLabels)
            {
                DrawActiveWorldLabels();
            }
        }

        private void OnDestroy()
        {
            ClearVisuals();
            if (lineMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(lineMaterial);
            }
            else
            {
                DestroyImmediate(lineMaterial);
            }

            lineMaterial = null;
        }

        private void UpdateVisual(ThrusterVisual visual, Camera viewCamera)
        {
            RuntimeThrusterInstance thruster = visual.Thruster;
            ThrusterDefinition definition =
                thruster != null ? thruster.ThrusterDefinition : null;
            if (thruster == null || definition == null)
            {
                visual.Root.SetActive(false);
                return;
            }

            Vector3 origin =
                thruster.transform.TransformPoint(definition.LocalForceOrigin);
            Vector3 facing = GetWorldFacingDirection(thruster);
            Color temperatureColor = GetTemperatureColor(thruster);
            DrawArrow(
                visual.FacingArrow,
                origin - facing * facingArrowLength,
                facing,
                facingArrowLength,
                WithAlpha(temperatureColor, 0.72f),
                facingLineWidth,
                viewCamera);

            bool firing = IsFiring(thruster);
            visual.ForceArrow.enabled = firing;
            if (!firing)
            {
                return;
            }

            Vector3 forceDirection =
                thruster.CurrentWorldForce.sqrMagnitude > 0.0001f
                    ? thruster.CurrentWorldForce.normalized
                    : facing * Mathf.Sign(thruster.CurrentOutput);
            float maximumForce =
                thruster.CurrentOutput >= 0f
                    ? definition.MaximumForwardForceN *
                      definition.OverloadOutputMultiplier
                    : definition.MaximumReverseForceN;
            float force01 =
                maximumForce <= 0f
                    ? 0f
                    : Mathf.Clamp01(
                        thruster.CurrentAppliedForceN / maximumForce);
            float length = Mathf.Lerp(
                facingArrowLength * 0.45f,
                maximumForceArrowLength,
                Mathf.Sqrt(force01));
            float width = forceLineWidth *
                          Mathf.Lerp(0.65f, 1.5f, force01);
            DrawArrow(
                visual.ForceArrow,
                origin - forceDirection * length,
                forceDirection,
                length,
                temperatureColor,
                width,
                viewCamera);
        }

        private void UpdateSurfaceVisual(Camera viewCamera)
        {
            if (surfaceVisualRoot == null)
            {
                return;
            }

            bool available =
                showVisualization &&
                hover != null &&
                hover.IsGrounded &&
                runtime != null &&
                runtime.RootRigidbody != null;
            surfaceVisualRoot.SetActive(available);
            if (!available)
            {
                return;
            }

            Vector3 origin =
                runtime.RootRigidbody.worldCenterOfMass +
                runtime.transform.up * 1.2f;
            DrawArrow(
                craftUpArrow,
                origin,
                runtime.transform.up,
                2.15f,
                CraftUpColor,
                0.045f,
                viewCamera);
            DrawArrow(
                surfaceNormalArrow,
                origin,
                hover.GroundNormal,
                2.65f,
                SurfaceNormalColor,
                0.07f,
                viewCamera);
        }

        private void DrawInspectorPanel()
        {
            Rect safe = Screen.safeArea;
            const float width = 470f;
            float height = 143f + visuals.Count * 23f;
            Rect panel = new Rect(
                Mathf.Max(safe.x + 16f, safe.xMax - width - 16f),
                safe.y + 16f,
                Mathf.Min(width, safe.width - 32f),
                Mathf.Min(height, safe.height - 32f));

            Color previous = GUI.color;
            GUI.color = new Color(0.018f, 0.035f, 0.055f, 0.92f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = CoolColor;
            GUI.DrawTexture(
                new Rect(panel.x, panel.y, 3f, panel.height),
                Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 8f, panel.width - 28f, 24f),
                $"THRUSTER DEBUG  //  {ActiveThrusterCount}/{ThrusterCount} FIRING",
                titleStyle);
            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 31f, panel.width - 28f, 57f),
                "THIN = facing direction   THICK/LONG = actual force\n" +
                "TEMP: BLUE (cool) > GREEN > YELLOW (safe limit) > RED\n" +
                "PURPLE = averaged surface normal   WHITE = craft up\n" +
                "G = live gimbal pitch/yaw in degrees",
                legendStyle);

            string surfaceStatus = "SURFACE: AIRBORNE / NO PROBES";
            if (hover != null && hover.IsGrounded)
            {
                float alignmentAngle = Vector3.Angle(
                    runtime.transform.up,
                    hover.GroundNormal);
                V3ActuatorAllocationResult allocation =
                    hover.SurfaceAlignmentAllocation;
                surfaceStatus =
                    $"SURFACE: {hover.GroundedProbeCount}/4 PROBES  " +
                    $"UP/NORMAL {alignmentAngle:0.0} deg  " +
                    $"SOLVED/REQUEST " +
                    $"{allocation.AllocatedTorqueNm.magnitude / 1000f:0}/" +
                    $"{allocation.RequestedTorqueNm.magnitude / 1000f:0} kNm";
            }

            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 96f, panel.width - 28f, 22f),
                surfaceStatus,
                legendStyle);

            float y = panel.y + 124f;
            for (int i = 0; i < visuals.Count; i++)
            {
                if (y + 22f > panel.yMax)
                {
                    break;
                }

                ThrusterVisual visual = visuals[i];
                RuntimeThrusterInstance thruster = visual.Thruster;
                Color stateColor = GetTemperatureColor(thruster);
                GUI.color = stateColor;
                GUI.DrawTexture(
                    new Rect(panel.x + 14f, y + 6f, 8f, 8f),
                    Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(
                    new Rect(panel.x + 28f, y, 215f, 22f),
                    thruster != null ? thruster.ParentSocketId : "MISSING",
                    nameStyle);
                GUI.Label(
                    new Rect(panel.x + 235f, y, panel.width - 249f, 22f),
                    BuildStateText(visual),
                    valueStyle);
                y += 23f;
            }
        }

        private void DrawActiveWorldLabels()
        {
            Camera viewCamera = Camera.main;
            if (viewCamera == null)
            {
                return;
            }

            for (int i = 0; i < visuals.Count; i++)
            {
                ThrusterVisual visual = visuals[i];
                RuntimeThrusterInstance thruster = visual.Thruster;
                if (!IsFiring(thruster))
                {
                    continue;
                }

                Vector3 origin = thruster.transform.TransformPoint(
                    thruster.ThrusterDefinition.LocalForceOrigin);
                Vector3 screen = viewCamera.WorldToScreenPoint(origin);
                if (screen.z <= 0f)
                {
                    continue;
                }

                Color previous = GUI.color;
                GUI.color = GetTemperatureColor(thruster);
                GUI.Label(
                    new Rect(
                        screen.x + 8f,
                        Screen.height - screen.y - 16f,
                        210f,
                        34f),
                    $"{thruster.ParentSocketId}\n" +
                    $"{Mathf.Abs(thruster.CurrentOutput) * 100f:0}%  " +
                    $"{thruster.CurrentAppliedForceN / 1000f:0.0} kN  " +
                    $"{thruster.CurrentTemperatureC:0} C",
                    worldLabelStyle);
                GUI.color = previous;
            }
        }

        private string BuildStateText(ThrusterVisual visual)
        {
            RuntimeThrusterInstance thruster = visual.Thruster;
            if (thruster == null || !thruster.IsEnabled)
            {
                return "OFFLINE";
            }

            if (thruster.IsThermallyLockedOut)
            {
                return $"LOCKOUT  {thruster.CurrentTemperatureC:0} C";
            }

            string temperature = $"{thruster.CurrentTemperatureC:0} C";
            string gimbal = visual.Gimbal != null
                ? $"  G {visual.Gimbal.CurrentPitchDegrees:+0;-0;0}/" +
                  $"{visual.Gimbal.CurrentYawDegrees:+0;-0;0}"
                : string.Empty;
            if (!IsFiring(thruster))
            {
                return $"IDLE  {temperature}{gimbal}";
            }

            string state = thruster.CurrentOutput < 0f
                ? "REV"
                : thruster.ThrusterDefinition != null &&
                  thruster.CurrentOutput >
                  thruster.ThrusterDefinition.NormalOutputMultiplier + 0.0001f
                    ? "OVR"
                    : "FIRE";
            return
                $"{state} {Mathf.Abs(thruster.CurrentOutput) * 100f:0}%  " +
                $"{thruster.CurrentAppliedForceN / 1000f:0} kN  " +
                $"{temperature}{gimbal}";
        }

        private bool IsFiring(RuntimeThrusterInstance thruster)
        {
            return thruster != null &&
                   thruster.IsEnabled &&
                   !thruster.IsThermallyLockedOut &&
                   Mathf.Abs(thruster.CurrentOutput) >= firingThreshold &&
                   thruster.CurrentAppliedForceN > 0f;
        }

        public static Color GetTemperatureColor(
            RuntimeThrusterInstance thruster)
        {
            if (thruster == null ||
                !thruster.IsEnabled ||
                thruster.IsThermallyLockedOut)
            {
                return LockedOutColor;
            }

            if (thruster.Definition == null)
            {
                return CoolColor;
            }

            return EvaluateTemperatureColor(
                thruster.CurrentTemperatureC,
                thruster.Definition.Thermal,
                thruster.IsThermallyLockedOut);
        }

        public static Color EvaluateTemperatureColor(
            float temperatureC,
            ThermalProfile thermal,
            bool lockedOut = false)
        {
            if (lockedOut)
            {
                return LockedOutColor;
            }

            if (!thermal.enabled)
            {
                return new Color(0.62f, 0.68f, 0.72f, 1f);
            }

            float ambient = thermal.ambientTemperatureC;
            float safe = Mathf.Max(
                ambient + 0.001f,
                thermal.maximumSafeTemperatureC);
            float overheat = Mathf.Max(
                safe + 0.001f,
                thermal.overheatTemperatureC);
            if (temperatureC <= safe)
            {
                float safe01 = Mathf.InverseLerp(
                    ambient,
                    safe,
                    temperatureC);
                return safe01 <= 0.65f
                    ? Color.Lerp(
                        CoolColor,
                        NormalColor,
                        safe01 / 0.65f)
                    : Color.Lerp(
                        NormalColor,
                        SafeLimitColor,
                        (safe01 - 0.65f) / 0.35f);
            }

            return Color.Lerp(
                SafeLimitColor,
                LockedOutColor,
                Mathf.InverseLerp(safe, overheat, temperatureC));
        }

        private LineRenderer CreateLine(
            Transform parent,
            string lineName,
            Color color,
            float width)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(parent, false);
            lineObject.hideFlags = HideFlags.DontSave;
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 5;
            line.sharedMaterial = lineMaterial;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 500;
            return line;
        }

        private static void DrawArrow(
            LineRenderer line,
            Vector3 origin,
            Vector3 direction,
            float length,
            Color color,
            float width,
            Camera viewCamera)
        {
            if (line == null || direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();
            Vector3 tip = origin + direction * Mathf.Max(0.01f, length);
            float headLength = Mathf.Min(length * 0.3f, 0.38f);
            float headWidth = headLength * 0.55f;
            Vector3 viewDirection =
                viewCamera != null
                    ? (viewCamera.transform.position - tip).normalized
                    : Vector3.up;
            Vector3 side = Vector3.Cross(direction, viewDirection);
            if (side.sqrMagnitude < 0.001f)
            {
                side = Vector3.Cross(direction, Vector3.up);
            }

            if (side.sqrMagnitude < 0.001f)
            {
                side = Vector3.Cross(direction, Vector3.right);
            }

            side.Normalize();
            Vector3 headBase = tip - direction * headLength;
            line.enabled = true;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.SetPosition(0, origin);
            line.SetPosition(1, tip);
            line.SetPosition(2, headBase + side * headWidth);
            line.SetPosition(3, tip);
            line.SetPosition(4, headBase - side * headWidth);
        }

        private void CreateSurfaceVisuals()
        {
            surfaceVisualRoot =
                new GameObject("Debug Surface Orientation");
            surfaceVisualRoot.transform.SetParent(transform, false);
            surfaceVisualRoot.hideFlags = HideFlags.DontSave;
            craftUpArrow = CreateLine(
                surfaceVisualRoot.transform,
                "Craft Up",
                CraftUpColor,
                0.045f);
            surfaceNormalArrow = CreateLine(
                surfaceVisualRoot.transform,
                "Averaged Surface Normal",
                SurfaceNormalColor,
                0.07f);
            craftUpArrow.sortingOrder = 502;
            surfaceNormalArrow.sortingOrder = 503;
        }

        private void ApplyVisibility()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i].Root != null)
                {
                    visuals[i].Root.SetActive(showVisualization);
                }
            }

            if (surfaceVisualRoot != null)
            {
                surfaceVisualRoot.SetActive(
                    showVisualization &&
                    hover != null &&
                    hover.IsGrounded);
            }
        }

        private void ClearVisuals()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                GameObject root = visuals[i].Root;
                if (root == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(root);
                }
                else
                {
                    DestroyImmediate(root);
                }
            }

            visuals.Clear();
            if (surfaceVisualRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(surfaceVisualRoot);
                }
                else
                {
                    DestroyImmediate(surfaceVisualRoot);
                }
            }

            surfaceVisualRoot = null;
            surfaceNormalArrow = null;
            craftUpArrow = null;
            hover = null;
            runtime = null;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private void EnsureLineMaterial()
        {
            if (lineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader != null)
            {
                lineMaterial = new Material(shader)
                {
                    name = "V3 Thruster Debug Lines",
                    hideFlags = HideFlags.DontSave
                };
            }
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = CoolColor }
            };
            legendStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.72f, 0.82f, 0.86f) }
            };
            nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.83f, 0.9f, 0.93f) }
            };
            valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };
            worldLabelStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }
    }
}
