using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class GPUDrivenRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int m_Version = 1;
        public int version => m_Version;
        
        [SerializeField] [ResourcePath("Runtime/Extension/GPUDriven/Shader/VisibilityBufferRT.compute")]
        private ComputeShader m_VisibilityBufferCS;

        public ComputeShader visibilityBufferCS
        {
            get => m_VisibilityBufferCS;
            set => this.SetValueAndNotify(ref m_VisibilityBufferCS, value);
        }

        
        
    }
}