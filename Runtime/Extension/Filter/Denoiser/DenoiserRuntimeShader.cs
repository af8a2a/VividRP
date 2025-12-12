using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class DenoiserRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        #region Spatial

        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/SpatialDenoiser/Shader/SpatialDenoiser.compute")]
        private ComputeShader m_SpatialDenoiserCS;

        public ComputeShader SpatialDenoiserCS
        {
            get => m_SpatialDenoiserCS;
            set => this.SetValueAndNotify(ref m_SpatialDenoiserCS, value);
        }

        #endregion


        #region Temporal

        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/TemporalFilter/Shader/TemporalFilter.compute")]
        private ComputeShader m_TemporalFilterCS;

        public ComputeShader temporalFilterCS
        {
            get => m_TemporalFilterCS;
            set => this.SetValueAndNotify(ref m_TemporalFilterCS, value);
        }

        #endregion


        #region SIGMA

        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_ClassifyTiles.compute")]
        private ComputeShader m_ShadowClassifyTiles;

        public ComputeShader shadowClassifyTiles
        {
            get => m_ShadowClassifyTiles;
            set => this.SetValueAndNotify(ref m_ShadowClassifyTiles, value);
        }


        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_SmoothTiles.compute")]
        private ComputeShader m_ShadowSmoothTiles;

        public ComputeShader shadowSmoothTiles
        {
            get => m_ShadowSmoothTiles;
            set => this.SetValueAndNotify(ref m_ShadowSmoothTiles, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Copy.compute")]
        private ComputeShader m_ShadowCopy;

        public ComputeShader shadowCopy
        {
            get => m_ShadowCopy;
            set => this.SetValueAndNotify(ref m_ShadowCopy, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_Blur.compute")]
        private ComputeShader m_ShadowBlur;

        public ComputeShader shadowBlur
        {
            get => m_ShadowBlur;
            set => this.SetValueAndNotify(ref m_ShadowBlur, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_PostBlur.compute")]
        private ComputeShader m_ShadowPostBlur;

        public ComputeShader shadowPostBlur
        {
            get => m_ShadowPostBlur;
            set => this.SetValueAndNotify(ref m_ShadowPostBlur, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_TemporalStabilization.compute")]
        private ComputeShader m_ShadowTemporalStabilization;

        public ComputeShader shadowTemporalStabilization
        {
            get => m_ShadowTemporalStabilization;
            set => this.SetValueAndNotify(ref m_ShadowTemporalStabilization, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/SIGMA/SIGMA_Shadow_SplitScreen.compute")]
        private ComputeShader m_ShadowSplitScreen;

        public ComputeShader shadowSplitScreen
        {
            get => m_ShadowSplitScreen;
            set => this.SetValueAndNotify(ref m_ShadowSplitScreen, value);
        }

        #endregion


        #region REBLUR

        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_Blur.compute")]
        private ComputeShader m_REBLUR_Blur;

        public ComputeShader REBLURBlur
        {
            get => m_REBLUR_Blur;
            set => this.SetValueAndNotify(ref m_REBLUR_Blur, value);
        }

        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_PostBlur.compute")]
        private ComputeShader m_REBLUR_PostBlur;

        public ComputeShader REBLURPostBlur
        {
            get => m_REBLUR_PostBlur;
            set => this.SetValueAndNotify(ref m_REBLUR_PostBlur, value);
        }

        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_ClassifyTiles.compute")]
        private ComputeShader m_REBLUR_ClassifyTiles;

        public ComputeShader REBLURClassifyTiles
        {
            get => m_REBLUR_ClassifyTiles;
            set => this.SetValueAndNotify(ref m_REBLUR_ClassifyTiles, value);
        }
        
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_HistoryFix.compute")]
        private ComputeShader m_REBLUR_HistoryFix;

        public ComputeShader REBLURHistoryFix
        {
            get => m_REBLUR_HistoryFix;
            set => this.SetValueAndNotify(ref m_REBLUR_HistoryFix, value);
        }


        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_HitDistReconstruction.compute")]
        private ComputeShader m_REBLUR_HitDistReconstruction;

        public ComputeShader REBLURHitDistReconstruction
        {
            get => m_REBLUR_HitDistReconstruction;
            set => this.SetValueAndNotify(ref m_REBLUR_HitDistReconstruction, value);
        }

        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_PrePass.compute")]
        private ComputeShader m_REBLUR_PrePass;

        public ComputeShader REBLURPrePass
        {
            get => m_REBLUR_PrePass;
            set => this.SetValueAndNotify(ref m_REBLUR_PrePass, value);
        }

        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_SplitScreen.compute")]
        private ComputeShader m_REBLUR_SplitScreen;

        public ComputeShader REBLURSplitScreen
        {
            get => m_REBLUR_SplitScreen;
            set => this.SetValueAndNotify(ref m_REBLUR_SplitScreen, value);
        }

        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_TemporalAccumulation.compute")]
        private ComputeShader m_REBLUR_TemporalAccumulation;

        public ComputeShader REBLURTemporalAccumulation
        {
            get => m_REBLUR_TemporalAccumulation;
            set => this.SetValueAndNotify(ref m_REBLUR_TemporalAccumulation, value);
        }

        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_TemporalStabilization.compute")]
        private ComputeShader m_REBLUR_TemporalStabilization;

        public ComputeShader REBLURTemporalStabilization
        {
            get => m_REBLUR_TemporalStabilization;
            set => this.SetValueAndNotify(ref m_REBLUR_TemporalStabilization, value);
        }

        
        [SerializeField] [ResourcePath("Runtime/Extension/Filter/Denoiser/NRD/REBLUR/REBLUR_Validation.compute")]
        private ComputeShader m_REBLUR_Validation;

        public ComputeShader REBLURValidation
        {
            get => m_REBLUR_Validation;
            set => this.SetValueAndNotify(ref m_REBLUR_Validation, value);
        }

        #endregion
    }
}