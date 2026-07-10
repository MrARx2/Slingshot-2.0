using System;
using UnityEngine;
using Unity.Mathematics;

namespace TrackGeneration.Mesh
{
    /// <summary>
    /// Defines the road cross-section profile used to build the track mesh.
    /// This profile is stamped at each spline sample to create the track surface and walls.
    /// </summary>
    [Serializable]
    public class TrackCrossSection
    {
        [Tooltip("Total width of the road surface (meters).")]
        public float RoadWidth = 8f;

        [Tooltip("Height of the side walls from the road surface (meters).")]
        [Range(0.5f, 5f)]
        public float WallHeight = 1.5f;

        [Tooltip("Thickness of the side walls (meters).")]
        [Range(0.1f, 1f)]
        public float WallThickness = 0.3f;

        [Tooltip("Slight curvature for the road crown (degrees). Positive is higher in the center.")]
        [Range(-5f, 5f)]
        public float RoadCamber = 0f;

        [Tooltip("Number of vertices across the profile.")]
        [Range(4, 16)]
        public int ProfileVertexCount = 8;

        [Tooltip("Adds a center lane line vertex for 2-lane roads.")]
        public bool HasLaneDivider = true;

        /// <summary>
        /// Returns an array of local-space cross-section vertices (in the cross-track plane, Y=up, X=right).
        /// Applies the specified banking angle (in degrees).
        /// </summary>
        public Vector3[] GetProfile(float bankAngle, float leftScale = 1f, float rightScale = 1f, float leftWallScale = 1f, float rightWallScale = 1f)
        {
            int vertCount = GetVertexCount();
            Vector3[] profile = new Vector3[vertCount];

            float leftExt = -RoadWidth * 0.5f * leftScale;
            float rightExt = RoadWidth * 0.5f * rightScale;
            
            float currentLeftWallHeight = WallHeight * leftWallScale;
            float currentRightWallHeight = WallHeight * rightWallScale;
            int vIndex = 0;

            // 1. Left wall top outer
            profile[vIndex++] = new Vector3(leftExt - WallThickness, currentLeftWallHeight, 0f);
            
            // 2. Left wall top inner
            profile[vIndex++] = new Vector3(leftExt, currentLeftWallHeight, 0f);
            
            // 3. Left wall bottom inner / road left edge
            profile[vIndex++] = new Vector3(leftExt, 0f, 0f);

            // Optional: Lane divider (slight bump for center line)
            if (HasLaneDivider)
            {
                // Keeping max camber constant based on full width so it doesn't bounce during scaling
                float camberHeight = (RoadWidth * 0.5f) * math.tan(math.radians(RoadCamber));
                float centerPos = (leftExt + rightExt) * 0.5f;
                // If it's fully collapsed, also collapse the lane divider height
                float dividerHeight = (math.abs(leftScale) < 0.01f && math.abs(rightScale) < 0.01f) ? 0f : 0.02f;
                profile[vIndex++] = new Vector3(centerPos, camberHeight + dividerHeight, 0f);
            }

            // Next: Right wall bottom inner / road right edge
            profile[vIndex++] = new Vector3(rightExt, 0f, 0f);
            
            // Next: Right wall top inner
            profile[vIndex++] = new Vector3(rightExt, currentRightWallHeight, 0f);
            
            // Last: Right wall top outer
            profile[vIndex++] = new Vector3(rightExt + WallThickness, currentRightWallHeight, 0f);

            // Apply road camber to edge points if no lane divider, otherwise handle camber
            if (RoadCamber != 0f && !HasLaneDivider)
            {
                // Simple parabolic/linear camber approximation if needed, 
                // but since we only have edges, we just keep edges at 0.
            }

            // Apply banking rotation around the local Z-axis (forward)
            if (bankAngle != 0f)
            {
                Quaternion bankRotation = Quaternion.Euler(0f, 0f, bankAngle);
                for (int i = 0; i < profile.Length; i++)
                {
                    profile[i] = bankRotation * profile[i];
                }
            }

            return profile;
        }

        /// <summary>
        /// Returns the number of vertices in the profile.
        /// </summary>
        public int GetVertexCount()
        {
            return HasLaneDivider ? 7 : 6;
        }

        /// <summary>
        /// Returns triangle indices to connect two adjacent cross-section rings into quads.
        /// </summary>
        public int[] GetTriangleStripIndices(int currentRing, int nextRing)
        {
            int numVerts = GetVertexCount();
            // Each adjacent pair of vertices forms a quad (2 triangles, 6 indices)
            int numQuads = numVerts - 1;
            int[] indices = new int[numQuads * 6];

            int idx = 0;
            for (int i = 0; i < numQuads; i++)
            {
                int currentLeft = currentRing * numVerts + i;
                int currentRight = currentRing * numVerts + i + 1;
                int nextLeft = nextRing * numVerts + i;
                int nextRight = nextRing * numVerts + i + 1;

                // Triangle 1
                indices[idx++] = currentLeft;
                indices[idx++] = nextLeft;
                indices[idx++] = nextRight;

                // Triangle 2
                indices[idx++] = currentLeft;
                indices[idx++] = nextRight;
                indices[idx++] = currentRight;
            }

            return indices;
        }
    }
}
