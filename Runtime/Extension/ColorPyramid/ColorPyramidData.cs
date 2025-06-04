using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class ColorPyramidData : ContextItem
    {
        public TextureHandle ColorTexture = TextureHandle.nullHandle;
        public int MipCount = 0;

        public override void Reset()
        {
            ColorTexture = TextureHandle.nullHandle;
        }
    }
}