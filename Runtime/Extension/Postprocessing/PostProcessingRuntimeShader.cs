using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class PostProcessingRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        [SerializeField, ResourcePath("Shaders/PostProcessing/TemporalAA.shader")]
        private Shader m_TemporalAAShader;

        public Shader temporalAAShader
        {
            get => m_TemporalAAShader;
            set => this.SetValueAndNotify(ref m_TemporalAAShader, value);
        }

    }
}