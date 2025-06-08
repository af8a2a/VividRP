using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    class ShadowResourceData : ContextItem
    {
        public TextureHandle directionalShadowsTexture;


        public override void Reset()
        {
            directionalShadowsTexture = TextureHandle.nullHandle;
        }
    }
}