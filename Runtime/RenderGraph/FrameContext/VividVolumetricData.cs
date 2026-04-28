using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividVolumetricData : ContextItem
    {
        public VividVolumetricFogSettings settings;
        public ShaderVariablesVolumetric shaderVariables;
        public RenderGraphTexture VBufferMaxZ;
        public RenderGraphTexture VBufferDensity;
        public RenderGraphTexture VBufferLighting;
        public RenderGraphBuffer localVolumetricFogBuffer;
        public int localVolumetricFogCount;
        public bool enabled;
        public bool gaussianFilteringEnabled;

        public override void Reset()
        {
            settings = default;
            shaderVariables = default;
            VBufferMaxZ = null;
            VBufferDensity = null;
            VBufferLighting = null;
            localVolumetricFogBuffer = null;
            localVolumetricFogCount = 0;
            enabled = false;
            gaussianFilteringEnabled = false;
        }
    }
}
