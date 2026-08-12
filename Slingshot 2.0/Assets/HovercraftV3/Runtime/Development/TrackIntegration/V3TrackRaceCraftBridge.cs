using TrackGeneration;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.TrackIntegration
{
    /// <summary>
    /// Keeps the procedural track assembly independent of concrete V3 craft
    /// types while allowing its existing race-craft contract to place the
    /// currently assembled V3 craft at the authored start frame.
    /// </summary>
    [DefaultExecutionOrder(-1050)]
    [DisallowMultipleComponent]
    public sealed class V3TrackRaceCraftBridge : MonoBehaviour,
        ITrackRaceCraft,
        ITrackBoundsConsumer
    {
        [SerializeField] private V3CraftTestSpawner spawner;
        [SerializeField] private V3FreeDriveSession session;

        public Transform CraftTransform
        {
            get
            {
                GameObject craft = spawner != null
                    ? spawner.CurrentCraft
                    : null;
                return craft != null ? craft.transform : null;
            }
        }

        public Rigidbody CraftRigidbody
        {
            get
            {
                if (session != null && session.CraftBody != null)
                    return session.CraftBody;

                GameObject craft = spawner != null
                    ? spawner.CurrentCraft
                    : null;
                V3CraftRuntime runtime = craft != null
                    ? craft.GetComponent<V3CraftRuntime>()
                    : null;
                return runtime != null ? runtime.RootRigidbody : null;
            }
        }

        public float SpawnRideHeight
        {
            get
            {
                CraftBuildDefinition build = spawner != null
                    ? spawner.SelectedBuild
                    : null;
                return build != null
                    ? build.HoverConfiguration.TargetHoverHeight
                    : 3.075f;
            }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        public void Configure(
            V3CraftTestSpawner craftSpawner,
            V3FreeDriveSession freeDriveSession)
        {
            spawner = craftSpawner;
            session = freeDriveSession;
        }

        public void OnPlacedAtTrackStart()
        {
            session?.RefreshCraftBinding();
            session?.Telemetry?.RefreshLiveTelemetry();
            Physics.SyncTransforms();
        }

        public void OnTrackBoundsChanged(Bounds worldBounds)
        {
            session?.ConfigureKillPlaneFromTrackBounds(worldBounds);
        }

        private void ResolveReferences()
        {
            if (spawner == null)
                spawner = GetComponent<V3CraftTestSpawner>();

            if (session == null)
                session = GetComponent<V3FreeDriveSession>();
        }
    }
}
