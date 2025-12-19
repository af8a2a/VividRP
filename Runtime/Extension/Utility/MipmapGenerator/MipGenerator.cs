using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    //copy and modifed from HDRP
    public partial class MipGenerator : IDisposable
    {
        MaterialPropertyBlock m_PropertyBlock;

        ComputeShader m_ColorPyramidCS;

        int m_ColorDownsampleKernel;
        int m_ColorGaussianKernel;
        int m_HizDownsampleKernel;
        int m_PassThroughtKernel;

        RenderTextureDescriptor m_ColorPyramidDescriptor;
        RenderTextureDescriptor m_DepthPyramidDescriptor;


        #region HDRP

        ComputeShader m_DepthPyramidCS;
        int m_DepthDownsampleKernel;
        internal PackedMipChainInfo m_DepthBufferMipChainInfo = new PackedMipChainInfo();
        ComputeBuffer m_DepthPyramidMipLevelOffsetsBuffer = null;

        #endregion

        #region GPUCopy

        ComputeShader m_HDRPGPUCopyShader;
        int k_SampleKernel_xyzw2x_8;
        int k_SampleKernel_xyzw2x_1;

        #endregion


        #region ColorClear

        ComputeShader m_ColorClearShader;

        #endregion

        public void Init()
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

            GPUCopyColor = runtimeShaders.GPUCopyColor;
            GPUCopyColorKernelID = GPUCopyColor.FindKernel("KMain");

            #endregion


            #region SPD

            spdCompatibleCS = runtimeShaders.spdCompatible;
            spdCS = runtimeShaders.spdIntegration;
            spdKernelID = spdCompatibleCS.FindKernel("KMain"); //default 0

            #endregion


            #region HDRP

            m_DepthPyramidCS = runtimeShaders.depthPyramidCS;
            m_DepthDownsampleKernel = m_DepthPyramidCS.FindKernel("KDepthDownsample8DualUav");
            m_DepthBufferMipChainInfo.Allocate();
            m_DepthPyramidMipLevelOffsetsBuffer = new ComputeBuffer(15, sizeof(int) * 2, ComputeBufferType.Structured);

            #endregion


            #region GPUCopy

            m_HDRPGPUCopyShader = runtimeShaders.copyChannelCS;
            k_SampleKernel_xyzw2x_8 = m_HDRPGPUCopyShader.FindKernel("KSampleCopy4_1_x_8");
            k_SampleKernel_xyzw2x_1 = m_HDRPGPUCopyShader.FindKernel("KSampleCopy4_1_x_1");

            #endregion

            #region ColorClear

            m_ColorClearShader = runtimeShaders.colorClearCS;

            #endregion
        }


        private static MipGenerator s_Instance = new MipGenerator();

        
        public static MipGenerator instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new MipGenerator();

                return s_Instance;
            }
        }




        public static void ClearAll()
        {
            if (s_Instance != null)
                s_Instance.Dispose();

            s_Instance = null;
        }


        public void Dispose()
        {
            CoreUtils.SafeRelease(m_DepthPyramidMipLevelOffsetsBuffer);
        }
    }
}