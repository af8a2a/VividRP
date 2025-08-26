namespace UnityEngine.Rendering.Universal
{
    partial class UniversalAdditionalCameraData
    {
        [SerializeField] float m_RenderScale = 1.0f;


        [SerializeField] UpscalingTechnique m_UpscalerTechnique = UpscalingTechnique.Linear;
        
        /// <summary>
        /// Controls if this camera should render shadows.
        /// </summary>
        public float renderScale
        {
            get => m_RenderScale;
            set => m_RenderScale = value;
        }



        public UpscalingTechnique upscalerTechnique
        {
            get => m_UpscalerTechnique;
            set => m_UpscalerTechnique = value;
        }
    }
}