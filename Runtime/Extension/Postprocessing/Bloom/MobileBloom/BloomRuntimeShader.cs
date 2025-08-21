using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class BloomRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Postprocessing/Bloom/MobileBloom/Shader/Bloom.shader")]
        private Shader m_BloomShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public Shader bloomShader
        {
            get => m_BloomShader;
            set => this.SetValueAndNotify(ref m_BloomShader, value, nameof(m_BloomShader));
        }
        
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Postprocessing/Bloom/MobileBloom/Shader/URPBloom.shader")]
        private Shader m_URPBloomShader;

        /// <summary>
        /// Default directional shadowramp texture.
        /// </summary>
        public Shader URPBloomShader
        {
            get => m_URPBloomShader;
            set => this.SetValueAndNotify(ref m_URPBloomShader, value, nameof(m_URPBloomShader));
        }

    }
}