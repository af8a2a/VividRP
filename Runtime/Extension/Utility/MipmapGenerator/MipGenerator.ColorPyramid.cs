using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class MipGenerator
    {
        class ColorPyramidPassData
        {
            public ComputeShader ColorPyramidShader;
            public int ColorPyramidKernelID;
            public int ColorGaussianKernelID;

            public ComputeShader GPUCopyShader;
            public int GPUCopyKernelID;


            public TextureHandle colorTexture;
            public TextureHandle colorPyramidTexture;
            public TextureHandle tempTexture;

            public int width;
            public int height;
            public int sourceSlice, destSlice;
        }

        static int _SourceSlice = Shader.PropertyToID("_SourceSlice");
        static int _DestSlice = Shader.PropertyToID("_DestSlice");


        //assume src format equal to dst
        public void CopyColor(RenderGraph renderGraph,  TextureHandle src, TextureHandle dst)
        {
            using (var builder = renderGraph.AddComputePass<ColorPyramidPassData>("GPU Color Copy", out var passData))
            {
                passData.GPUCopyShader = GPUCopyColor;
                passData.GPUCopyKernelID = GPUCopyColorKernelID;
                var desc = renderGraph.GetTextureDesc(src);

                passData.width = desc.width;
                passData.height = desc.height;


                passData.colorTexture = src;
                passData.tempTexture = dst;

                builder.UseTexture(passData.colorTexture);
                builder.UseTexture(passData.tempTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ColorPyramidPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;


                    var threadX = RenderingUtilsExt.DivRoundUp(data.width, 8);
                    var threadY = RenderingUtilsExt.DivRoundUp(data.height, 8);

                    cmd.SetComputeTextureParam(data.GPUCopyShader, data.GPUCopyKernelID, "_Input", data.colorTexture);
                    cmd.SetComputeTextureParam(data.GPUCopyShader, data.GPUCopyKernelID, "_Output", data.tempTexture);
                    cmd.DispatchCompute(data.GPUCopyShader, data.GPUCopyKernelID, threadX, threadY, 1);
                });
            }
        }
        
        
        //assume src format equal to dst
        public void CopyColorArray(RenderGraph renderGraph,  TextureHandle src, TextureHandle dst, int sourceSlice = 0, int destSlice = 0)
        {
            using (var builder = renderGraph.AddComputePass<ColorPyramidPassData>("GPU Color Copy", out var passData))
            {
                passData.GPUCopyShader = GPUCopyColor;
                passData.GPUCopyKernelID = 1;
                var desc = renderGraph.GetTextureDesc(src);

                passData.width = desc.width;
                passData.height = desc.height;
                passData.sourceSlice = sourceSlice;
                passData.destSlice = destSlice;

                passData.colorTexture = src;
                passData.tempTexture = dst;

                builder.UseTexture(passData.colorTexture);
                builder.UseTexture(passData.tempTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ColorPyramidPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;


                    var threadX = RenderingUtilsExt.DivRoundUp(data.width, 8);
                    var threadY = RenderingUtilsExt.DivRoundUp(data.height, 8);

                    cmd.SetComputeIntParam(data.GPUCopyShader,_SourceSlice, data.sourceSlice);
                    cmd.SetComputeIntParam(data.GPUCopyShader,_DestSlice, data.destSlice);


                    cmd.SetComputeTextureParam(data.GPUCopyShader, data.GPUCopyKernelID, "_Input", data.colorTexture);
                    cmd.SetComputeTextureParam(data.GPUCopyShader, data.GPUCopyKernelID, "_Output", data.tempTexture);
                    cmd.DispatchCompute(data.GPUCopyShader, data.GPUCopyKernelID, threadX, threadY, 1);
                });
            }
        }



        public TextureHandle RenderColorPyramid(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<ColorPyramidPassData>("Color Pyramid Generate", out var passData))
            {
                passData.ColorPyramidShader = m_ColorPyramidCS;
                passData.ColorPyramidKernelID = m_ColorDownsampleKernel;
                passData.ColorGaussianKernelID = m_ColorGaussianKernel;
                passData.GPUCopyShader = GPUCopyColor;
                passData.GPUCopyKernelID = GPUCopyColorKernelID;
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                passData.width = cameraData.pixelWidth;
                passData.height = cameraData.pixelHeight;

                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);

                passData.colorPyramidTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                {
                    enableRandomWrite = true,
                    name = "ColorPyramidTexture",
                    format = desc.format,
                    useMipMap = true,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Bilinear
                });

                passData.tempTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth / 2, cameraData.pixelHeight / 2)
                {
                    enableRandomWrite = true,
                    name = "tempTexture",
                    format = desc.format,
                    useMipMap = true,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Bilinear
                });


                passData.colorTexture = resourceData.activeColorTexture;

                builder.UseTexture(passData.colorTexture);
                builder.UseTexture(passData.colorPyramidTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.tempTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ColorPyramidPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    int srcMipWidth = data.width;
                    int srcMipHeight = data.height;
                    int mipLevel = 0;


                    {
                        CoreUtils.SetKeyword(cmd, "COPY_MIP_0", true);
                        cmd.SetComputeVectorParam(data.ColorPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Source", data.colorTexture);
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Mip0", data.colorPyramidTexture, 0);
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Destination", data.tempTexture, 0);
                        cmd.DispatchCompute(data.ColorPyramidShader, data.ColorPyramidKernelID, RenderingUtilsExt.DivRoundUp(srcMipWidth / 2, 8),
                            RenderingUtilsExt.DivRoundUp(srcMipHeight / 2, 8), 1);

                        CoreUtils.SetKeyword(cmd, "COPY_MIP_0", false);
                    }


                    while (srcMipWidth >= 8 || srcMipHeight >= 8)
                    {
                        int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                        int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);


                        var threadX = RenderingUtilsExt.DivRoundUp(dstMipWidth, 8);
                        var threadY = RenderingUtilsExt.DivRoundUp(dstMipHeight, 8);

                        if (mipLevel != 0)
                        {
                            cmd.SetComputeVectorParam(data.ColorPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                            cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Source", data.colorPyramidTexture, mipLevel);
                            cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Destination", data.tempTexture, mipLevel);
                            cmd.DispatchCompute(data.ColorPyramidShader, data.ColorPyramidKernelID, threadX, threadY, 1);
                        }

                        cmd.SetComputeVectorParam(data.ColorPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorGaussianKernelID, "_Source", data.tempTexture, mipLevel);
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorGaussianKernelID, "_Destination", data.colorPyramidTexture, mipLevel + 1);
                        cmd.DispatchCompute(data.ColorPyramidShader, data.ColorGaussianKernelID, threadX, threadY, 1);


                        mipLevel += 1;
                        srcMipWidth >>= 1;
                        srcMipHeight >>= 1;
                    }
                });
                return passData.colorPyramidTexture;
            }
        }

        public TextureHandle RenderColorPyramid(RenderGraph renderGraph, ContextContainer frameData, TextureHandle inputTexture)
        {
            using (var builder = renderGraph.AddComputePass<ColorPyramidPassData>("Color Pyramid Generate", out var passData))
            {
                passData.ColorPyramidShader = m_ColorPyramidCS;
                passData.ColorPyramidKernelID = m_ColorDownsampleKernel;
                passData.ColorGaussianKernelID = m_ColorGaussianKernel;
                passData.GPUCopyShader = GPUCopyColor;
                passData.GPUCopyKernelID = GPUCopyColorKernelID;
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                passData.width = cameraData.pixelWidth;
                passData.height = cameraData.pixelHeight;

                var desc = renderGraph.GetTextureDesc(inputTexture);

                passData.colorPyramidTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                {
                    enableRandomWrite = true,
                    name = "ColorPyramidTexture",
                    format = desc.format,
                    useMipMap = true,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Bilinear
                });

                passData.tempTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth / 2, cameraData.pixelHeight / 2)
                {
                    enableRandomWrite = true,
                    name = "tempTexture",
                    format = desc.format,
                    useMipMap = true,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Bilinear
                });


                passData.colorTexture = inputTexture;

                builder.UseTexture(passData.colorTexture);
                builder.UseTexture(passData.colorPyramidTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.tempTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((ColorPyramidPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    int srcMipWidth = data.width;
                    int srcMipHeight = data.height;
                    int mipLevel = 0;


                    {
                        CoreUtils.SetKeyword(cmd, "COPY_MIP_0", true);
                        cmd.SetComputeVectorParam(data.ColorPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Source", data.colorTexture);
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Mip0", data.colorPyramidTexture, 0);
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Destination", data.tempTexture, 0);
                        cmd.DispatchCompute(data.ColorPyramidShader, data.ColorPyramidKernelID, RenderingUtilsExt.DivRoundUp(srcMipWidth / 2, 8),
                            RenderingUtilsExt.DivRoundUp(srcMipHeight / 2, 8), 1);

                        CoreUtils.SetKeyword(cmd, "COPY_MIP_0", false);
                    }


                    while (srcMipWidth >= 8 || srcMipHeight >= 8)
                    {
                        int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                        int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);


                        var threadX = RenderingUtilsExt.DivRoundUp(dstMipWidth, 8);
                        var threadY = RenderingUtilsExt.DivRoundUp(dstMipHeight, 8);

                        if (mipLevel != 0)
                        {
                            cmd.SetComputeVectorParam(data.ColorPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                            cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Source", data.colorPyramidTexture, mipLevel);
                            cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorPyramidKernelID, "_Destination", data.tempTexture, mipLevel);
                            cmd.DispatchCompute(data.ColorPyramidShader, data.ColorPyramidKernelID, threadX, threadY, 1);
                        }

                        cmd.SetComputeVectorParam(data.ColorPyramidShader, "_Size", new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorGaussianKernelID, "_Source", data.tempTexture, mipLevel);
                        cmd.SetComputeTextureParam(data.ColorPyramidShader, data.ColorGaussianKernelID, "_Destination", data.colorPyramidTexture, mipLevel + 1);
                        cmd.DispatchCompute(data.ColorPyramidShader, data.ColorGaussianKernelID, threadX, threadY, 1);


                        mipLevel += 1;
                        srcMipWidth >>= 1;
                        srcMipHeight >>= 1;
                    }
                });
                return passData.colorPyramidTexture;
            }
        }
    }
}