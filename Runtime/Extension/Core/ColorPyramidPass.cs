using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;


namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Generates the gaussian pyramid of source into destination.
    /// We can't do it in place as the color pyramid has to be read while writing to the color buffer in some cases (e.g. refraction, distortion)
    /// </summary>
    public class ColorPyramidPass : ScriptableRenderPass
    {
        private ComputeShader m_Shader;
        private int m_GaussianKernel;
        private int m_DownsampleKernel;

        /// <summary>
        /// Generate color pyramid with compute shader
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="computeShader"></param>
        public ColorPyramidPass(RenderPassEvent evt)
        {
            base.profilingSampler = new ProfilingSampler("ColorPyramid");
            renderPassEvent = evt;

        }

        public void Setup()
        {
            var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<MipGeneratorRuntimeShader>();
            
            m_Shader = runtimeShader.colorPyramid;
            if (m_Shader)
            {
                m_GaussianKernel = m_Shader.FindKernel("KColorGaussian");
                m_DownsampleKernel = m_Shader.FindKernel("KColorDownsample");
            }

        }

        static int GetMipCount(Vector2Int pyramidSize)
        {
            int srcMipLevel = 0;
            int srcMipWidth = pyramidSize.x;
            int srcMipHeight = pyramidSize.y;

            while (srcMipWidth >= 8 || srcMipHeight >= 8)
            {
                int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);

                srcMipLevel++;
                srcMipWidth >>= 1;
                srcMipHeight >>= 1;
            }
            return srcMipLevel + 1;
        }

        static int ComputeColorGaussianPyramid(ComputeCommandBuffer cmd, PassData data)
        {
            var cs = data.cs;

            int srcMipLevel = 0;
            int srcMipWidth = data.pyramidSize.x;
            int srcMipHeight = data.pyramidSize.y;

            Vector2Int targetSize = data.tempTargetSize;

            cmd.EnableShaderKeyword("COPY_MIP_0");
            cmd.SetComputeVectorParam(cs, ShaderConstants._Size, new Vector4(srcMipWidth, srcMipHeight, 0, 0));
            cmd.SetComputeTextureParam(cs, data.downsampleKernel, ShaderConstants._Source, data.source);
            cmd.SetComputeTextureParam(cs, data.downsampleKernel, ShaderConstants._Mip0, data.destination, 0);
            cmd.SetComputeTextureParam(cs, data.downsampleKernel, ShaderConstants._Destination, data.tempDownsamplePyramid);
            cmd.DispatchCompute(cs, data.downsampleKernel, RenderingUtilsExt.DivRoundUp(targetSize.x, 8), RenderingUtilsExt.DivRoundUp(targetSize.y, 8), 1);
            cmd.DisableShaderKeyword("COPY_MIP_0");

            // Note: smaller mips are excluded as we don't need them and the gaussian compute works
            // on 8x8 blocks
            while (srcMipWidth >= 8 || srcMipHeight >= 8)
            {
                int dstMipWidth = Mathf.Max(1, srcMipWidth >> 1);
                int dstMipHeight = Mathf.Max(1, srcMipHeight >> 1);

                if (srcMipLevel != 0)
                {
                    cmd.SetComputeVectorParam(cs, ShaderConstants._Size, new Vector4(srcMipWidth, srcMipHeight, 0, 0));
                    cmd.SetComputeTextureParam(cs, data.downsampleKernel, ShaderConstants._Source, data.destination, srcMipLevel);
                    cmd.SetComputeTextureParam(cs, data.downsampleKernel, ShaderConstants._Destination, data.tempDownsamplePyramid);
                    cmd.DispatchCompute(cs, data.downsampleKernel, RenderingUtilsExt.DivRoundUp(dstMipWidth, 8), RenderingUtilsExt.DivRoundUp(dstMipHeight, 8), 1);
                }

                cmd.SetComputeVectorParam(cs, ShaderConstants._Size, new Vector4(dstMipWidth, dstMipHeight, 0, 0));
                cmd.SetComputeTextureParam(cs, data.gaussianKernel, ShaderConstants._Source, data.tempDownsamplePyramid);
                cmd.SetComputeTextureParam(cs, data.gaussianKernel, ShaderConstants._Destination, data.destination, srcMipLevel + 1);
                cmd.DispatchCompute(cs, data.gaussianKernel, RenderingUtilsExt.DivRoundUp(dstMipWidth, 8), RenderingUtilsExt.DivRoundUp(dstMipHeight, 8), 1);

                srcMipLevel++;
                srcMipWidth >>= 1;
                srcMipHeight >>= 1;
            }

            return srcMipLevel + 1;
        }
        
        private class PassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
            internal Vector2Int pyramidSize;
            internal TextureHandle tempDownsamplePyramid;
            internal Vector2Int tempTargetSize;

            internal ComputeShader cs;
            internal int downsampleKernel;
            internal int gaussianKernel;
        }

        internal void Render(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source, out TextureHandle destination, out int mipCount)
        {
            using (var builder = renderGraph.AddComputePass<PassData>("Color Pyramid", out var passData, base.profilingSampler))
            {
                // Access resources.
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
                if (camHistoryRTSystem == null)
                {
                    destination = TextureHandle.nullHandle;
                    mipCount = 0;
                    return;
                }

                // Set passData
                int actualWidth = cameraData.cameraTargetDescriptor.width;
                int actualHeight = cameraData.cameraTargetDescriptor.height;
                Vector2Int pyramidSize = new Vector2Int(actualWidth, actualHeight);
                if (camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.ColorBufferMipChain) == null)
                {
                    camHistoryRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ColorBufferMipChain);
                    camHistoryRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ColorBufferMipChain,  HistoryBufferAllocatorFunction, cameraData.cameraTargetDescriptor.graphicsFormat, 2);
                }

                RTHandle colorpyramidRTHandle = camHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.ColorBufferMipChain);
                // TempDownsamplePyramid
                int tempRTWidth = Mathf.Max(1, actualWidth >> 1);
                int tempRTHeight = Mathf.Max(1, actualHeight >> 1);
                Vector2Int tempTargetSize = new Vector2Int(tempRTWidth, tempRTHeight);
                var tempDownsamplePyramid = builder.CreateTransientTexture(new TextureDesc(tempRTWidth, tempRTHeight)
                {
                    format = cameraData.cameraTargetDescriptor.graphicsFormat,
                    enableRandomWrite = true,
                    useMipMap = false,
                    name = "Temporary Downsampled Pyramid"
                });

                passData.source = source;
                passData.destination = renderGraph.ImportTexture(colorpyramidRTHandle);
                passData.pyramidSize = pyramidSize;
                passData.tempDownsamplePyramid = tempDownsamplePyramid;
                passData.tempTargetSize = tempTargetSize;

                passData.cs = m_Shader;
                passData.downsampleKernel = m_DownsampleKernel;
                passData.gaussianKernel = m_GaussianKernel;

                // Declare input/output textures
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.destination, AccessFlags.Write);

                // Setup builder state
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ComputeColorGaussianPyramid(context.cmd, data);

                });

                destination = passData.destination;
                mipCount = GetMipCount(passData.pyramidSize);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle activeColor = resourceData.activeColorTexture;
            Render(renderGraph, frameData, in activeColor, out _, out _);
        }

        // BufferedRTHandleSystem API expects an allocator function. We define it here.
        /// <summary>
        /// Allocator for cameraColorBufferMipChain.
        /// TODO: dimension configured
        /// </summary>
        static RTHandle HistoryBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one,  colorFormat: graphicsFormat,
                enableRandomWrite: true, useMipMap: true, autoGenerateMips: false, useDynamicScale: true,
                name: string.Format("{0}_CameraColorBufferMipChain{1}", viewName, frameIndex));
        }

        public static class ShaderConstants
        {
            public static readonly int _Source = Shader.PropertyToID("_Source");
            public static readonly int _SrcScaleBias = Shader.PropertyToID("_SrcScaleBias");
            public static readonly int _SrcUvLimits = Shader.PropertyToID("_SrcUvLimits");
            public static readonly int _SourceMip = Shader.PropertyToID("_SourceMip");

            public static readonly int _Mip0 = Shader.PropertyToID("_Mip0");
            public static readonly int _Size = Shader.PropertyToID("_Size");
            public static readonly int _Destination = Shader.PropertyToID("_Destination");
        }
    }
}
