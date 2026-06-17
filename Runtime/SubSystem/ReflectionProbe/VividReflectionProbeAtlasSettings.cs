using UnityEngine;

namespace VividRP.Runtime
{
    internal static class VividReflectionProbeAtlasSettings
    {
        internal static Vector2Int ResolveDimensions(VividReflectionProbeAtlasResolution resolution)
        {
            var packedResolution = (int)resolution;
            if (packedResolution <= (int)VividReflectionProbeAtlasResolution.Resolution16384x16384)
                return new Vector2Int(packedResolution, packedResolution);

            return new Vector2Int(packedResolution >> 16, packedResolution & 0xFFFF);
        }
    }
}
