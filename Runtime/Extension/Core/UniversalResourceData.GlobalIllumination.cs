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


        #region ReferencedPathTracing

        // Path Tracing raw material data (from DXR GBuffer)
        private TextureHandle _materialData;

        public TextureHandle materialData
        {
            get => CheckAndGetTextureHandle(ref _materialData);
            set => CheckAndSetTextureHandle(ref _materialData, value);
        }

        // DLSS-RR GBuffer outputs
        private TextureHandle _diffuseAlbedo;

        public TextureHandle diffuseAlbedo
        {
            get => CheckAndGetTextureHandle(ref _diffuseAlbedo);
            set => CheckAndSetTextureHandle(ref _diffuseAlbedo, value);
        }

        private TextureHandle _specularAlbedo;

        public TextureHandle specularAlbedo
        {
            get => CheckAndGetTextureHandle(ref _specularAlbedo);
            set => CheckAndSetTextureHandle(ref _specularAlbedo, value);
        }

        private TextureHandle _normalRoughness;

        public TextureHandle normalRoughness
        {
            get => CheckAndGetTextureHandle(ref _normalRoughness);
            set => CheckAndSetTextureHandle(ref _normalRoughness, value);
        }

        private TextureHandle _hitDistance;

        public TextureHandle hitDistance
        {
            get => CheckAndGetTextureHandle(ref _hitDistance);
            set => CheckAndSetTextureHandle(ref _hitDistance, value);
        }

        #endregion
    }
}