using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    
    public partial class MipGenerator
    {
        class DepthPyramidPassData
        {
            public ComputeShader DepthPyramidShader;
            public int DepthPyramidKernelID;
            public ComputeShader GPUCopyShader;
            public int GPUCopyKernelID;


            public TextureHandle depthTexture;
            public TextureHandle depthPyramidTexture;

            public PackedMipChainInfo depthBufferMipChainInfo;

            public int width;
            public int height;
        }


        public TextureHandle RenderDepthPyramid(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<DepthPyramidPassData>("DepthPyramid Generate", out var passData))
            {
                passData.DepthPyramidShader = m_ColorPyramidCS;
                passData.DepthPyramidKernelID = m_HizDownsampleKernel;
                passData.GPUCopyShader = GPUCopyColor;
                passData.GPUCopyKernelID = GPUCopyColorKernelID;
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                passData.width = cameraData.pixelWidth;
                passData.height = cameraData.pixelHeight;

                passData.depthPyramidTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                {
                    enableRandomWrite = true,
                    name = "HizTexture",
                    format = GraphicsFormat.R32_SFloat,
                    useMipMap = true,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None
                });
                passData.depthTexture = resourceData.cameraDepthTexture;

                builder.UseTexture(passData.depthTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.depthPyramidTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DepthPyramidPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    {
                        var threadX = RenderingUtilsExt.DivRoundUp(data.width, 8);
                        var threadY = RenderingUtilsExt.DivRoundUp(data.height, 8);
                        cmd.SetComputeTextureParam(data.GPUCopyShader, data.GPUCopyKernelID, "_Input", data.depthTexture);
                        cmd.SetComputeTextureParam(data.GPUCopyShader, data.GPUCopyKernelID, "_Output", data.depthPyramidTexture);

                        cmd.DispatchCompute(data.GPUCopyShader, data.GPUCopyKernelID, threadX, threadY, 1);
                    }

                    int srcMipWidth = data.width;
                    int srcMipHeight = data.height;
                    int mipLevel = 0;
                    while (srcMipWidth >= 8 || srcMipHeight >= 8)
                    {
                        var threadX = RenderingUtilsExt.DivRoundUp(srcMipWidth, 8);
                        var threadY = RenderingUtilsExt.DivRoundUp(srcMipHeight, 8);
                        cmd.SetComputeVectorParam(data.DepthPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));

                        cmd.SetComputeTextureParam(data.DepthPyramidShader, data.DepthPyramidKernelID, "_Source", data.depthPyramidTexture, mipLevel);
                        cmd.SetComputeTextureParam(data.DepthPyramidShader, data.DepthPyramidKernelID, "_Destination", data.depthPyramidTexture, mipLevel + 1);

                        cmd.DispatchCompute(data.DepthPyramidShader, data.DepthPyramidKernelID, threadX, threadY, 1);

                        mipLevel += 1;
                        srcMipWidth >>= 1;
                        srcMipHeight >>= 1;

                    }
                });
                return passData.depthPyramidTexture;
            }
        }
    }
}