using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class UniversalResourceData
    {
        private TextureHandle _ssrLightingTexture;

        public TextureHandle ssrLightingTexture
        {
            get => CheckAndGetTextureHandle(ref _ssrLightingTexture);
            set => CheckAndSetTextureHandle(ref _ssrLightingTexture, value);
        }


        private TextureHandle _indirectDiffuseTexture;

        public TextureHandle indirectDiffuseTexture
        {
            get => CheckAndGetTextureHandle(ref _indirectDiffuseTexture);
            set => CheckAndSetTextureHandle(ref _indirectDiffuseTexture, value);
        }
    }
}