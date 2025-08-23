using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]

    public class TonemappingRuntimeShader: IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/ToneMapping/Shader/Tonemapping.shader")]
        private Shader m_Tonemapping;

        public Shader tonemapping
        {
            get => m_Tonemapping;
            set => this.SetValueAndNotify(ref m_Tonemapping, value);
        }

        

    }
}