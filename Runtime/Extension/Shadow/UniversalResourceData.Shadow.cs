using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class UniversalResourceData
    {
        /// <summary>
        /// Main shadow map.
        /// </summary>
        public TextureHandle directionalShadowsTexture
        {
            get => CheckAndGetTextureHandle(ref _directionalShadowsTexture);
            set => CheckAndSetTextureHandle(ref _directionalShadowsTexture, value);
        }
        private TextureHandle _directionalShadowsTexture;
        
        /// <summary>
        /// ScreenSpace shadow map.
        /// </summary>
        public TextureHandle screenSpaceShadowsTexture
        {
            get => CheckAndGetTextureHandle(ref _screenSpaceShadowsTexture);
            set => CheckAndSetTextureHandle(ref _screenSpaceShadowsTexture, value);
        }
        private TextureHandle _screenSpaceShadowsTexture;

        
        
        public TextureHandle perObjectShadowTexture
        {
            get => CheckAndGetTextureHandle(ref _perObjectShadowTexture);
            set => CheckAndSetTextureHandle(ref _perObjectShadowTexture, value);
        }
        private TextureHandle _perObjectShadowTexture;

        
    }
}