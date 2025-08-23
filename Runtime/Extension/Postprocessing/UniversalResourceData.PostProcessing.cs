using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    partial class UniversalResourceData
    {

        public TextureHandle bloomTexture
        {
            get => CheckAndGetTextureHandle(ref _bloomTexture);
            internal set => CheckAndSetTextureHandle(ref _bloomTexture, value);
        }
        private TextureHandle _bloomTexture;

    }
}