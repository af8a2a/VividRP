using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    class ShadowResourceData : ContextItem
    {
        public TextureHandle directionalShadowsTexture;
        public TextureHandle perObjectShadowTexture;
        public TextureHandle screenSpaceShadowmapTex;


        public override void Reset()
        {
            directionalShadowsTexture = TextureHandle.nullHandle;
            perObjectShadowTexture = TextureHandle.nullHandle;
            screenSpaceShadowmapTex = TextureHandle.nullHandle;
        }
    }
}