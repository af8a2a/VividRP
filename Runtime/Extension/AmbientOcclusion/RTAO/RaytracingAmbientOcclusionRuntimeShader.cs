using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class RaytracingAmbientOcclusionRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/RTAO/Shader/InlineRT/RTAO.compute")]
        private ComputeShader m_RaytracingAmbientOcclusionCS;
        
        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader inlineRaytracingAmbientOcclusionShader
        {
            get => m_RaytracingAmbientOcclusionCS;
            set => this.SetValueAndNotify(ref m_RaytracingAmbientOcclusionCS, value, nameof(m_RaytracingAmbientOcclusionCS));
        }

        
        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/RTAO/Shader/RaytracingAmbientOcclusion.raytrace")]
        private RayTracingShader m_RaytracingAmbientOcclusionRTShader;
        
        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public RayTracingShader raytracingAmbientOcclusionRTShader
        {
            get => m_RaytracingAmbientOcclusionRTShader;
            set => this.SetValueAndNotify(ref m_RaytracingAmbientOcclusionRTShader, value, nameof(m_RaytracingAmbientOcclusionRTShader));
        }

    }
}