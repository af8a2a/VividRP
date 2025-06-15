using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class RaytracingAmbientOcclusionRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/RTAO/Shader/RTAO.compute")]
        private ComputeShader m_RaytracingAmbientOcclusionShader;
        
        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader raytracingAmbientOcclusionShader
        {
            get => m_RaytracingAmbientOcclusionShader;
            set => this.SetValueAndNotify(ref m_RaytracingAmbientOcclusionShader, value, nameof(m_RaytracingAmbientOcclusionShader));
        }

    }
}