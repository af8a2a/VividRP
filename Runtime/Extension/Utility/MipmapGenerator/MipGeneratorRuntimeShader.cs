using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class MipGeneratorRuntimeShader : IRenderPipelineResources

    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Utility/MipmapGenerator/Shader/CopyColor.compute")]
        private ComputeShader m_GPUCopyColor;

        public ComputeShader GPUCopyColor
        {
            get => m_GPUCopyColor;
            set => this.SetValueAndNotify(ref m_GPUCopyColor, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Utility/MipmapGenerator/Shader/ColorPyramid.compute")]
        private ComputeShader m_ColorPyramid;

        public ComputeShader colorPyramid
        {
            get => m_ColorPyramid;
            set => this.SetValueAndNotify(ref m_ColorPyramid, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/FidelityFX/SinglePassDownsample/Shader/SPDCompatible.compute")]
        private ComputeShader m_SinglePassDownsampleCompatible;

        public ComputeShader spdCompatible
        {
            get => m_SinglePassDownsampleCompatible;
            set => this.SetValueAndNotify(ref m_SinglePassDownsampleCompatible, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Extension/FidelityFX/SinglePassDownsample/Shader/SPDIntegration.compute")]
        private ComputeShader m_SinglePassDownsampleIntegration;

        public ComputeShader spdIntegration
        {
            get => m_SinglePassDownsampleIntegration;
            set => this.SetValueAndNotify(ref m_SinglePassDownsampleIntegration, value);
        }
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Utility/MipmapGenerator/Shader/DepthPyramid.compute")]
        private ComputeShader m_DepthPyramidCS;

        public ComputeShader depthPyramidCS
        {
            get => m_DepthPyramidCS;
            set => this.SetValueAndNotify(ref m_DepthPyramidCS, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/Utility/MipmapGenerator/Shader/GPUCopy.compute")]
        private ComputeShader m_CopyChannelCS;

        public ComputeShader copyChannelCS
        {
            get => m_CopyChannelCS;
            set => this.SetValueAndNotify(ref m_CopyChannelCS, value);
        }


    }
}