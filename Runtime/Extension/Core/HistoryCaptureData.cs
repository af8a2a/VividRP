using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class HistoryCaptureData : ContextItem
    {
        public TextureHandle HistoryColorTexture = TextureHandle.nullHandle;
        public TextureHandle HistoryDepthTexture = TextureHandle.nullHandle;
        public TextureHandle HisotryNormalTexture = TextureHandle.nullHandle;

        public override void Reset()
        {
            HistoryColorTexture = TextureHandle.nullHandle;
            HistoryDepthTexture = TextureHandle.nullHandle;
        }
    }
}