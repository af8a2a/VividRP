using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class AmbientOcclusionRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;


        #region XeGTAO

        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/XeGTAO/Shader/XeGTAO_PrefilterDepths16x16.compute")]
        private ComputeShader m_XeGTAOPrefilter;

        public ComputeShader XeGTAOPrefilter
        {
            get => m_XeGTAOPrefilter;
            set => this.SetValueAndNotify(ref m_XeGTAOPrefilter, value);
        }


        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/XeGTAO/Shader/XeGTAO_MainPass.compute")]
        private ComputeShader m_XeGTAOMainPass;

        public ComputeShader XeGTAOMainPass
        {
            get => m_XeGTAOMainPass;
            set => this.SetValueAndNotify(ref m_XeGTAOMainPass, value);
        }

        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/XeGTAO/Shader/XeGTAO_Denoise.compute")]
        private ComputeShader m_XeGTAODenoise;

        public ComputeShader XeGTAODenoise
        {
            get => m_XeGTAODenoise;
            set => this.SetValueAndNotify(ref m_XeGTAODenoise, value);
        }

        #endregion


        #region HBAO

        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/HBAO/Shader/HBAOBlur.compute")]
        private ComputeShader m_HBAOBlur;

        public ComputeShader HBAOBlur
        {
            get => m_HBAOBlur;
            set => this.SetValueAndNotify(ref m_HBAOBlur, value);
        }


        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/HBAO/Shader/HBAOCalc.compute")]
        private ComputeShader m_HBAOCalc;

        public ComputeShader HBAOCalc
        {
            get => m_HBAOCalc;
            set => this.SetValueAndNotify(ref m_HBAOCalc, value);
        }

        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/HBAO/Shader/HBAODeinterleaveDepth.compute")]
        private ComputeShader m_HBAODeinterleaveDepth;

        public ComputeShader HBAODeinterleaveDepth
        {
            get => m_HBAODeinterleaveDepth;
            set => this.SetValueAndNotify(ref m_HBAODeinterleaveDepth, value);
        }


        
        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/HBAO/Shader/HBAODeinterleaveNormal.compute")]
        private ComputeShader m_HBAODeinterleaveNormal;

        public ComputeShader HBAODeinterleaveNormal
        {
            get => m_HBAODeinterleaveNormal;
            set => this.SetValueAndNotify(ref m_HBAODeinterleaveNormal, value);
        }

        
        [SerializeField] [ResourcePath("Runtime/Extension/AmbientOcclusion/HBAO/Shader/HBAOReinterleave.compute")]
        private ComputeShader m_HBAOReinterleave;

        public ComputeShader HBAOReinterleave
        {
            get => m_HBAOReinterleave;
            set => this.SetValueAndNotify(ref m_HBAOReinterleave, value);
        }

        
        #endregion
    }
}