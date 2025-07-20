using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    partial class UniversalResourceData
    {
        public TextureHandle currentExposure
        {
            get => CheckAndGetTextureHandle(ref _currentExposure);
            internal set => CheckAndSetTextureHandle(ref _currentExposure, value);
        }

        private TextureHandle _currentExposure;

        /// <summary>
        /// Rendering Layers Texture. Can be written to by the DrawOpaques pass or DepthNormals prepass based on settings.
        /// </summary>
        public TextureHandle previousExposure
        {
            get => CheckAndGetTextureHandle(ref _previousExposure);
            internal set => CheckAndSetTextureHandle(ref _previousExposure, value);
        }

        private TextureHandle _previousExposure;
        

        public bool useFetchedExposure;
        public float fetchedGpuExposure;
    }
}