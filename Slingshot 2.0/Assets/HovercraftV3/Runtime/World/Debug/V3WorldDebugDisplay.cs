using UnityEngine;
using UnityEngine.InputSystem;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3WorldDebugDisplay : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private bool drawSceneVectors = true;
        private V3WorldEnvironmentProvider environment;
        private V3CraftAerodynamicsRuntime aerodynamics;
        private V3CraftMainframe mainframe;
        private GUIStyle titleStyle;
        private GUIStyle valueStyle;
        private Texture2D panelTexture;

        public bool Visible => visible;

        public void Initialize(V3CraftRuntime runtime)
        {
            environment = runtime != null
                ? runtime.GetComponent<V3WorldEnvironmentProvider>()
                : null;
            aerodynamics = runtime != null
                ? runtime.GetComponent<V3CraftAerodynamicsRuntime>()
                : null;
            mainframe = runtime != null
                ? runtime.GetComponent<V3CraftMainframe>()
                : null;
        }

        public void Toggle()
        {
            visible = !visible;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
            {
                Toggle();
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }
            ResolveBindings();
            EnsureStyles();
            V3WorldEnvironmentSample world = environment != null
                ? environment.CurrentSample
                : default;
            V3CraftAerodynamicState aero = aerodynamics != null
                ? aerodynamics.State
                : default;
            Rect safe = Screen.safeArea;
            Rect panel = new Rect(safe.x + 16f, safe.y + 16f, 410f, 370f);
            GUI.DrawTexture(panel, panelTexture);
            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 10f, 382f, 24f),
                "WORLD TRUTH  //  F5 CLOSE",
                titleStyle);
            string zones = world.ActiveZoneCount > 0
                ? world.DominantZoneId +
                  $" ({world.DominantZoneWeight:0.00})"
                : "NONE";
            string text = world.IsValid
                ? $"PROFILE       {world.WorldProfileName} [{world.WorldProfileId}]\n" +
                  $"WORLD TICK    {world.PhysicsTickId}\n" +
                  $"CRAFT TICK    {(mainframe != null ? mainframe.CraftPhysicsTickId : -1)}\n" +
                  $"WORLD TIME    {world.SimulationTime:0.000} s\n" +
                  $"ALTITUDE      {world.AltitudeMeters:0.0} m\n" +
                  $"TEMPERATURE   {world.AmbientTemperatureC:0.0} C\n" +
                  $"PRESSURE      {world.AtmosphericPressurePa / 1000f:0.00} kPa\n" +
                  $"DENSITY       {world.AirDensityKgPerCubicMeter:0.000} kg/m3\n" +
                  $"SOUND SPEED   {world.SpeedOfSoundMetersPerSecond:0.0} m/s\n" +
                  $"AIR VELOCITY  {Format(world.LocalAirVelocity)} m/s\n" +
                  $"GUST          {Format(world.GustVelocity)} m/s\n" +
                  $"TURBULENCE    {Format(world.TurbulenceVelocity)} m/s\n" +
                  $"ZONE          {zones}\n" +
                  $"AIRSPEED      {aero.AirspeedMetersPerSecond:0.0} m/s\n" +
                  $"MACH          {aero.MachNumber:0.000}\n" +
                  $"DYN PRESSURE  {aero.DynamicPressurePa / 1000f:0.00} kPa\n" +
                  $"AERO FORCE    {Format(aero.TotalAerodynamicForce)} N\n" +
                  $"SENSORS       {(mainframe != null ? mainframe.Sensors.Count : 0)}"
                : $"INVALID WORLD SAMPLE\nFLAGS  {world.Flags}\n" +
                  "World-dependent aerodynamics and sensor truth are unavailable.";
            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 40f, 382f, 320f),
                text,
                valueStyle);
        }

        private void OnDrawGizmos()
        {
            if (!visible || !drawSceneVectors)
            {
                return;
            }
            ResolveBindings();
            if (environment == null || !environment.CurrentSample.IsValid)
            {
                return;
            }
            Vector3 origin = transform.position;
            V3WorldEnvironmentSample world = environment.CurrentSample;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + world.GravityVector);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + world.LocalAirVelocity);
            if (aerodynamics != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(
                    origin,
                    origin - aerodynamics.State.RelativeAirVelocity * 0.1f);
            }
        }

        private void ResolveBindings()
        {
            if (environment == null)
            {
                environment = GetComponent<V3WorldEnvironmentProvider>();
            }
            if (aerodynamics == null)
            {
                aerodynamics = GetComponent<V3CraftAerodynamicsRuntime>();
            }
            if (mainframe == null)
            {
                mainframe = GetComponent<V3CraftMainframe>();
            }
        }

        private void EnsureStyles()
        {
            if (panelTexture == null)
            {
                panelTexture = new Texture2D(1, 1);
                panelTexture.SetPixel(0, 0, new Color(0.02f, 0.05f, 0.07f, 0.92f));
                panelTexture.Apply();
            }
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.9f, 1f) }
            };
            valueStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
        }

        private static string Format(Vector3 value)
        {
            return $"({value.x:0.0}, {value.y:0.0}, {value.z:0.0})";
        }
    }
}
