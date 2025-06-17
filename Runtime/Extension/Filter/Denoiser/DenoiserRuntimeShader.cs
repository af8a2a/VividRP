using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public partial class DenoiserRuntimeShader : IRenderPipelineResources
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
        

        
        
        // [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/TemporalFilter/Shaders/TemporalFilter.compute")]
        // private ComputeShader m_TemporalFilterCS;
        // public ComputeShader temporalFilterCS
        // {
        //     get => m_TemporalFilterCS;
        //     set => this.SetValueAndNotify(ref m_TemporalFilterCS, value);
        // }
        //
    }
}