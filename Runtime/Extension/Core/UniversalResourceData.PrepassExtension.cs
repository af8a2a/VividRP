using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class UniversalResourceData
    {
        
        /// <summary>
        /// Camera depth pyramid texture. Contains the scene min depth mips.
        /// </summary>
        public TextureHandle cameraDepthPyramidTexture
        {
            get => CheckAndGetTextureHandle(ref _cameraDepthPyramidTexture);
            set => CheckAndSetTextureHandle(ref _cameraDepthPyramidTexture, value);
        }
        private TextureHandle _cameraDepthPyramidTexture;

        
        /// <summary>
        /// Camera depth color texture. Contains the scene gaussian color mips.
        /// </summary>
        public TextureHandle cameraColorPyramidTexture
        {
            get => CheckAndGetTextureHandle(ref _cameraColorPyramidTexture);
            set => CheckAndSetTextureHandle(ref _cameraColorPyramidTexture, value);
        }
        private TextureHandle _cameraColorPyramidTexture;

    }
}