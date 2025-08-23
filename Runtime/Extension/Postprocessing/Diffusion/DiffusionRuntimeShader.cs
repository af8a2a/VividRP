using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class DiffusionRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/Diffusion/Shader/Diffusion.shader")]
        private Shader m_DiffusionShader;

        public Shader diffusionShader
        {
            get => m_DiffusionShader;
            set => this.SetValueAndNotify(ref m_DiffusionShader, value);
        }


    }
}