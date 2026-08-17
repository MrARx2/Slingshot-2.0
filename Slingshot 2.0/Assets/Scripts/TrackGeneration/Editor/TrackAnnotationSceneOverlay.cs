// Intentionally empty.
//
// The always-on SceneView.duringSceneGui overlay that used to live here was a
// workaround for the track annotations not appearing. The real cause was the debug
// visualizer's Level being forced back to Off on every OnValidate/reload (see
// TrackGenerator.RestoreDebugAnnotations). With that fixed, the visualizer's own
// OnDrawGizmos draws the labels — the overlay only produced duplicates, so it was
// removed. Safe to delete this file and its .meta.
