using UnityEngine;

namespace VividRP.Runtime
{
    public struct ClusteredProxyScreenBounds
    {
        public Vector3 viewSpaceAabbMin;
        public Vector3 viewSpaceAabbMax;
        public Vector2 clipSpaceAabbMin;
        public Vector2 clipSpaceAabbMax;
        public int sliceMin;
        public int sliceMax;
        public int tileMinX;
        public int tileMaxX;
        public int tileMinY;
        public int tileMaxY;
        public int bigTileMinX;
        public int bigTileMaxX;
        public int bigTileMinY;
        public int bigTileMaxY;
        public uint isValid;

        public readonly bool IsValid => isValid != 0u;
    }
}
