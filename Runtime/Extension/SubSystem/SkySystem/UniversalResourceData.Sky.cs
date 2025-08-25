using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class UniversalResourceData
    {
        internal BufferHandle skyAmbientProbe
        {
            get => CheckAndGetBufferHandle(ref _skyAmbientProbe);
            set => CheckAndSetBufferHandle(ref _skyAmbientProbe, value);
        }
        private BufferHandle _skyAmbientProbe;

        internal TextureHandle skyReflectionProbe
        {
            get => CheckAndGetTextureHandle(ref _skyReflectionProbe);
            set => CheckAndSetTextureHandle(ref _skyReflectionProbe, value);
        }
        private TextureHandle _skyReflectionProbe;

    }
}