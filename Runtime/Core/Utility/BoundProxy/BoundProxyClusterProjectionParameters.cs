using UnityEngine;

namespace VividRP.Runtime
{
    public readonly struct BoundProxyClusterProjectionParameters
    {
        public readonly Matrix4x4 worldToViewMatrix;
        public readonly int screenWidth;
        public readonly int screenHeight;
        public readonly int tileSize;
        public readonly int tileCountX;
        public readonly int tileCountY;
        public readonly int bigTileSize;
        public readonly int bigTileCountX;
        public readonly int bigTileCountY;
        public readonly int sliceCount;
        public readonly float nearClip;
        public readonly float farClip;
        public readonly float logDepthScale;
        public readonly float linearDepthScale;
        public readonly float tanHalfFovX;
        public readonly float tanHalfFovY;
        public readonly float orthoHalfWidth;
        public readonly float orthoHalfHeight;
        public readonly int isOrthographic;

        public BoundProxyClusterProjectionParameters(
            Matrix4x4 worldToViewMatrix,
            int screenWidth,
            int screenHeight,
            int tileSize,
            int tileCountX,
            int tileCountY,
            int bigTileSize,
            int bigTileCountX,
            int bigTileCountY,
            int sliceCount,
            float nearClip,
            float farClip,
            float logDepthScale,
            float linearDepthScale,
            float tanHalfFovX,
            float tanHalfFovY,
            float orthoHalfWidth,
            float orthoHalfHeight,
            int isOrthographic)
        {
            this.worldToViewMatrix = worldToViewMatrix;
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.tileSize = tileSize;
            this.tileCountX = tileCountX;
            this.tileCountY = tileCountY;
            this.bigTileSize = bigTileSize;
            this.bigTileCountX = bigTileCountX;
            this.bigTileCountY = bigTileCountY;
            this.sliceCount = sliceCount;
            this.nearClip = nearClip;
            this.farClip = farClip;
            this.logDepthScale = logDepthScale;
            this.linearDepthScale = linearDepthScale;
            this.tanHalfFovX = tanHalfFovX;
            this.tanHalfFovY = tanHalfFovY;
            this.orthoHalfWidth = orthoHalfWidth;
            this.orthoHalfHeight = orthoHalfHeight;
            this.isOrthographic = isOrthographic;
        }
    }
}
