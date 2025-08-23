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


        [SerializeField, ResourcePath("Shaders/PostProcessing/StopNaN.shader")]
        private Shader m_StopNaNShader;

        public Shader stopNaNShader
        {
            get => m_TemporalAAShader;
            set => this.SetValueAndNotify(ref m_TemporalAAShader, value);
        }
        
        
        [SerializeField, ResourcePath("Shaders/PostProcessing/SubpixelMorphologicalAntialiasing.shader")]
        private Shader m_SMAAShader;

        public Shader smaaShader
        {
            get => m_SMAAShader;
            set => this.SetValueAndNotify(ref m_SMAAShader, value);
        }

        
        /// <summary>
        /// <c>SubpixelMorphologicalAntiAliasing</c> SMAA area texture.
        /// </summary>
        [ResourcePath("Textures/SMAA/AreaTex.tga")] 
        public Texture2D smaaAreaTex;

        /// <summary>
        /// <c>SubpixelMorphologicalAntiAliasing</c> SMAA search texture.
        /// </summary>
        [ResourcePath("Textures/SMAA/SearchTex.tga")]
        public Texture2D smaaSearchTex;

    }
}