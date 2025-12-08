using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class AmbientOcclusionRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;


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
    }
}