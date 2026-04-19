using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividClusteredLightingData : ContextItem
    {
        public RenderGraphBuffer directionalLights;
        public RenderGraphBuffer punctualLights;
        public RenderGraphBuffer areaLights;
        public RenderGraphBuffer layeredOffset;
        public RenderGraphBuffer layeredLightList;
        public RenderGraphBuffer logBaseBuffer;
        public int directionalLightCount;
        public int punctualLightCount;
        public int areaLightCount;
        public int mainDirectionalLightIndex;
        public int clusterTileSize;
        public int clusterSliceCount;
        public int clusterTileCountX;
        public int clusterTileCountY;
        public float clusterNearClip;
        public float clusterFarClip;
        public int clusterIsOrthographic;
        public float clusterScale;
        public float clusterBase;
        public int clusterLog2SliceCount;
        public bool supportsClusteredPunctualLights;
        public bool isLogBaseBufferEnabled;

        public override void Reset()
        {
            directionalLights = null;
            punctualLights = null;
            areaLights = null;
            layeredOffset = null;
            layeredLightList = null;
            logBaseBuffer = null;
            directionalLightCount = 0;
            punctualLightCount = 0;
            areaLightCount = 0;
            mainDirectionalLightIndex = -1;
            clusterTileSize = 0;
            clusterSliceCount = 0;
            clusterTileCountX = 0;
            clusterTileCountY = 0;
            clusterNearClip = 0.0f;
            clusterFarClip = 0.0f;
            clusterIsOrthographic = 0;
            clusterScale = 0.0f;
            clusterBase = 0.0f;
            clusterLog2SliceCount = 0;
            supportsClusteredPunctualLights = false;
            isLogBaseBufferEnabled = false;
        }
    }
}
