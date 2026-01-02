using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Interface for querying lights at arbitrary world positions.
    /// Used by path tracing and multi-bounce global illumination systems.
    /// </summary>
    public interface IWorldLightQuery
    {
        /// <summary>
        /// Queries lights that may affect the given world position.
        /// </summary>
        /// <param name="positionWS">World-space position to query.</param>
        /// <param name="maxDistance">Maximum distance to search for lights.</param>
        /// <param name="result">Output list of light indices.</param>
        /// <returns>Number of lights found.</returns>
        int QueryLights(float3 positionWS, float maxDistance, List<int> result);

        /// <summary>
        /// Queries lights that may affect the given world position (NativeList version).
        /// </summary>
        /// <param name="positionWS">World-space position to query.</param>
        /// <param name="maxDistance">Maximum distance to search for lights.</param>
        /// <param name="result">Output NativeList of light indices.</param>
        /// <returns>Number of lights found.</returns>
        int QueryLights(float3 positionWS, float maxDistance, NativeList<int> result);

        /// <summary>
        /// Gets light data by index.
        /// </summary>
        /// <param name="lightIndex">Index of the light.</param>
        /// <returns>Light data, or null if index is invalid.</returns>
        WorldLightData? GetLightData(int lightIndex);

        /// <summary>
        /// Gets the GPU buffer containing all light data for shader access.
        /// </summary>
        /// <returns>GraphicsBuffer containing WorldLightDataGPU array, or null if not available.</returns>
        GraphicsBuffer GetGPULightDataBuffer();
    }
}

