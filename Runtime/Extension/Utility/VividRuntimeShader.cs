using System;

namespace UnityEngine.Rendering.Universal
{
    
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public partial class VividRuntimeShader: IRenderPipelineResources
    {
        public int version { get; }
        
        
        
        [SerializeField] [ResourcePath("ShaderLibrary/Extension/GenerateViewZ.compute")]
        private ComputeShader m_GenerateViewZ;

        public ComputeShader generateViewZ
        {
            get => m_GenerateViewZ;
            set => this.SetValueAndNotify(ref m_GenerateViewZ, value);
        }
        
        
    }
}