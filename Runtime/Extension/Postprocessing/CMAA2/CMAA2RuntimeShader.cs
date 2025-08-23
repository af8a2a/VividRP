using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class CMAA2RuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Postprocessing/CMAA2/Shader/CMAA.compute")]
        private ComputeShader m_CMAA2Shader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public ComputeShader cmaa2Shader
        {
            get => m_CMAA2Shader;
            set => this.SetValueAndNotify(ref m_CMAA2Shader, value, nameof(m_CMAA2Shader));
        }

    }
}