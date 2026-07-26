using UnityEngine;

namespace TrackGeneration
{
    /// <summary>
    /// Physics material for all generated track surfaces (road, walls, loops,
    /// ramps, caps — the whole collision shell built by
    /// BoxPrismTrackMeshBuilder).
    /// <para>
    /// Moderate friction with Minimum combine: paired with the craft hull's
    /// low-friction Minimum material, contacts resolve to the craft's value,
    /// so wall scrapes slide instead of grabbing (Unity's default would be
    /// 0.6/0.6 Average). Created once at runtime — no asset dependency.
    /// </para>
    /// </summary>
    public static class TrackSurfacePhysics
    {
        public const float SurfaceDynamicFriction = 0.35f;
        public const float SurfaceStaticFriction = 0.35f;
        public const float SurfaceBounciness = 0f;

        /// <summary>
        /// Dedicated physics layer for drivable track collision shells, so hover
        /// and surface probes can filter to real track only (never craft hulls,
        /// props, decoration or helper geometry). Defined in TagManager (layer 8).
        /// </summary>
        public const string SurfaceLayerName = "TrackSurface";

        private static int _surfaceLayer = -2; // -2 = not resolved yet

        /// <summary>
        /// Layer index of <see cref="SurfaceLayerName"/>, or 0 (Default) with a
        /// one-time warning when the project is missing the layer definition.
        /// </summary>
        public static int SurfaceLayer
        {
            get
            {
                if (_surfaceLayer == -2)
                {
                    _surfaceLayer = LayerMask.NameToLayer(SurfaceLayerName);
                    if (_surfaceLayer < 0)
                    {
                        Debug.LogWarning($"[TrackSurfacePhysics] Layer '{SurfaceLayerName}' is not defined in TagManager — generated track colliders fall back to Default. Hover-probe layer filtering will not work.");
                        _surfaceLayer = 0;
                    }
                }
                return _surfaceLayer;
            }
        }

        /// <summary>Layer mask containing only the track surface layer.</summary>
        public static int SurfaceMask => 1 << SurfaceLayer;

        private static PhysicsMaterial _surface;

        /// <summary>Shared material for every generated track collider.</summary>
        public static PhysicsMaterial Surface
        {
            get
            {
                if (_surface == null)
                {
                    _surface = new PhysicsMaterial("HoverTrackSurface")
                    {
                        dynamicFriction = SurfaceDynamicFriction,
                        staticFriction = SurfaceStaticFriction,
                        bounciness = SurfaceBounciness,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounceCombine = PhysicsMaterialCombine.Minimum
                    };
                }
                return _surface;
            }
        }
    }
}
