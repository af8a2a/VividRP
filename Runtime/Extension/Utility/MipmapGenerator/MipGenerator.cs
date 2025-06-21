using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    //copy and modifed from HDRP
    public partial class MipGenerator
    {
        MaterialPropertyBlock m_PropertyBlock;

        ComputeShader m_ColorPyramidCS;

        int m_ColorDownsampleKernel;
        int m_ColorGaussianKernel;
        int m_HizDownsampleKernel;
        int m_PassThroughtKernel;

        RenderTextureDescriptor m_ColorPyramidDescriptor;
        RenderTextureDescriptor m_DepthPyramidDescriptor;

        public MipGenerator()
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<MipGeneratorRuntimeShader>();

            m_ColorPyramidCS = runtimeShaders.colorPyramid;
            m_ColorDownsampleKernel = m_ColorPyramidCS.FindKernel("KColorDownsample");
            m_ColorGaussianKernel = m_ColorPyramidCS.FindKernel("KColorGaussian");
            m_HizDownsampleKernel = m_ColorPyramidCS.FindKernel("KHizDownsample");
            m_PassThroughtKernel = m_ColorPyramidCS.FindKernel("KPassthrought");
            m_PropertyBlock = new MaterialPropertyBlock();
            m_ColorPyramidDescriptor = new RenderTextureDescriptor();
            m_DepthPyramidDescriptor = new RenderTextureDescriptor();

            #region GPUCopy

            GPUCopyColor =runtimeShaders.GPUCopyColor;
            GPUCopyColorKernelID = GPUCopyColor.FindKernel("KMain");

            #endregion


            #region SPD

            spdCompatibleCS = runtimeShaders.spdCompatible;
            spdCS = runtimeShaders.spdIntegration;
            spdKernelID = spdCompatibleCS.FindKernel("KMain"); //default 0

            #endregion
        }

        private static Lazy<MipGenerator> s_Instance = new Lazy<MipGenerator>(() => new MipGenerator());

        public static MipGenerator Instance => s_Instance.Value;

    }
}