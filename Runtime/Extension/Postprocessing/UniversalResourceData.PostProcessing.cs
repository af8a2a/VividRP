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

        
        
        TextureHandle[] _BloomMipUp=new TextureHandle[1];
        TextureHandle[] _BloomMipDown=new TextureHandle[1];

        
        public TextureHandle[] bloomMipUpTexture
        {
            get => CheckAndGetTextureHandle(ref _BloomMipUp);
            internal set => CheckAndSetTextureHandle(ref _BloomMipUp, value);
        }

        public TextureHandle[] bloomMipDownTexture
        {
            get => CheckAndGetTextureHandle(ref _BloomMipDown);
            internal set => CheckAndSetTextureHandle(ref _BloomMipDown, value);
        }

    }
}