using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class SkyResourceData : ContextItem
    {
        public BufferHandle skyAmbientProbe;
        public TextureHandle skyReflectionProbe;

        public override void Reset()
        {
            skyAmbientProbe = BufferHandle.nullHandle;
            skyReflectionProbe = TextureHandle.nullHandle;
        }
    }
}