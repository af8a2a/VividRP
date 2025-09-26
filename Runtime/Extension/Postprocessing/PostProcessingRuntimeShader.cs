using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class PostProcessingRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        #region Upscaler

        
        [SerializeField, ResourcePath("Shaders/PostProcessing/TemporalAA.shader")]
        private Shader m_TemporalAAShader;

        public Shader temporalAAShader
        {
            get => m_TemporalAAShader;
            set => this.SetValueAndNotify(ref m_TemporalAAShader, value);
        }
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/SuperResolution/TAAU/Shader/TemporalAntiAliasing.shader")]
        private Shader m_TAAUShader;

        public Shader taauShader
        {
            get => m_TAAUShader;
            set => this.SetValueAndNotify(ref m_TAAUShader, value);
        }


        #endregion



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


        [SerializeField, ResourcePath("Shaders/PostProcessing/CameraMotionBlur.shader")]
        private Shader m_CameraMotionBlurShader;

        public Shader cameraMotionBlur
        {
            get => m_CameraMotionBlurShader;
            set => this.SetValueAndNotify(ref m_CameraMotionBlurShader, value);
        }
        
        
        
        [SerializeField, ResourcePath("Shaders/PostProcessing/PaniniProjection.shader")]
        private Shader m_paniniProjectionShader;

        public Shader paniniProjection
        {
            get => m_paniniProjectionShader;
            set => this.SetValueAndNotify(ref m_paniniProjectionShader, value);
        }

        
        [SerializeField, ResourcePath("Shaders/PostProcessing/LensFlareDataDriven.shader")]
        private Shader m_LensFlareDataDrivenShader;

        public Shader lensFlareDataDriven
        {
            get => m_LensFlareDataDrivenShader;
            set => this.SetValueAndNotify(ref m_LensFlareDataDrivenShader, value);
        }

        [SerializeField, ResourcePath("Shaders/PostProcessing/LensFlareScreenSpace.shader")]
        private Shader m_LensFlareScreenSpaceShader;

        public Shader lensFlareScreenSpace
        {
            get => m_LensFlareScreenSpaceShader;
            set => this.SetValueAndNotify(ref m_LensFlareScreenSpaceShader, value);
        }
        
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/UberPass/Shader/UberPost.shader")]
        private Shader m_UberPostShader;

        public Shader uberPost
        {
            get => m_UberPostShader;
            set => this.SetValueAndNotify(ref m_UberPostShader, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/UberPass/Shader/FinalPost.shader")]
        private Shader m_FinalPostShader;

        public Shader finalPost
        {
            get => m_FinalPostShader;
            set => this.SetValueAndNotify(ref m_FinalPostShader, value);
        }
        
        
        
        /// <summary>
        /// The LUT Builder LDR Post Processing shader.
        /// </summary>
        [ResourcePath("Runtime/Extension/Postprocessing/ColorGrading/Shader/LutBuilderLdr.shader")]
        private Shader m_LUTBuilderLdrPS;

        public Shader lutBuilderLdrPS
        {
            get => m_LUTBuilderLdrPS;
            set => this.SetValueAndNotify(ref m_LUTBuilderLdrPS, value);
        }

        
        
        /// <summary>
        /// The LUT Builder HDR Post Processing shader.
        /// </summary>
        [ResourcePath("Runtime/Extension/Postprocessing/ColorGrading/Shader/LutBuilderHdr.shader")]
        private Shader m_LUTBuilderHdrPS;

        public Shader lutBuilderHdrPS
        {
            get => m_LUTBuilderHdrPS;
            set => this.SetValueAndNotify(ref m_LUTBuilderHdrPS, value);
        }
        
        
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/SuperResolution/DLSS/Shader/DLSSBiasColorMask.shader")]
        private Shader m_DLSSBiasColorMaskPS;
        public Shader DLSSBiasColorMaskPS
        {
            get => m_DLSSBiasColorMaskPS;
            set => this.SetValueAndNotify(ref m_DLSSBiasColorMaskPS, value);
        }

    }
}