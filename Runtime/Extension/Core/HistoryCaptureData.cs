using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Features.Core
{
    public class HistoryCaptureData : ContextItem
    {
        public TextureHandle PrevColorTexture = TextureHandle.nullHandle;
        public TextureHandle CurrColorTexture = TextureHandle.nullHandle;
        public TextureHandle PrevDepthTexture = TextureHandle.nullHandle;
        public TextureHandle CurrDepthTexture = TextureHandle.nullHandle;
        
        public override void Reset()
        {
            PrevColorTexture = TextureHandle.nullHandle;
            CurrColorTexture = TextureHandle.nullHandle;
            PrevDepthTexture = TextureHandle.nullHandle;
            CurrDepthTexture = TextureHandle.nullHandle;
        }
    }
}