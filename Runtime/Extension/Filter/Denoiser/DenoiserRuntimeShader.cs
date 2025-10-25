using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class DenoiserRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/SpatialDenoiser/Shader/SpatialDenoiser.compute")]
        private ComputeShader m_SpatialDenoiserCS;

        public ComputeShader SpatialDenoiserCS
        {
            get => m_SpatialDenoiserCS;
            set => this.SetValueAndNotify(ref m_SpatialDenoiserCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/TemporalDenoiser/Shader/TemporalFilter.compute")]
        private ComputeShader m_TemporalFilterCS;

        public ComputeShader temporalFilterCS
        {
            get => m_TemporalFilterCS;
            set => this.SetValueAndNotify(ref m_TemporalFilterCS, value);
        }



        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_ClassifyTiles.compute")]
        private ComputeShader m_ShadowClassifyTiles;

        public ComputeShader shadowClassifyTiles
        {
            get => m_ShadowClassifyTiles;
            set => this.SetValueAndNotify(ref m_ShadowClassifyTiles, value);
        }

        
        
                
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_SmoothTiles.compute")]
        private ComputeShader m_ShadowSmoothTiles;

        public ComputeShader shadowSmoothTiles
        {
            get => m_ShadowSmoothTiles;
            set => this.SetValueAndNotify(ref m_ShadowSmoothTiles, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Copy.compute")]
        private ComputeShader m_ShadowCopy;
        public ComputeShader shadowCopy
        {
            get => m_ShadowCopy;
            set => this.SetValueAndNotify(ref m_ShadowCopy, value);
        }

        
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_Blur.compute")]
        private ComputeShader m_ShadowBlur;

        public ComputeShader shadowBlur
        {
            get => m_ShadowBlur;
            set => this.SetValueAndNotify(ref m_ShadowBlur, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_PostBlur.compute")]
        private ComputeShader m_ShadowPostBlur;
        public ComputeShader shadowPostBlur
        {
            get => m_ShadowPostBlur;
            set => this.SetValueAndNotify(ref m_ShadowPostBlur, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_TemporalStabilization.compute")]
        private ComputeShader m_ShadowTemporalStabilization;
        public ComputeShader shadowTemporalStabilization
        {
            get => m_ShadowTemporalStabilization;
            set => this.SetValueAndNotify(ref m_ShadowTemporalStabilization, value);
        }

    }
}