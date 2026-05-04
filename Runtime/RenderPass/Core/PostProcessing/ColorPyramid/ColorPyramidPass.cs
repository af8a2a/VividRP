using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class ColorPyramidPass : ComputePass, IRenderGraphRecordingPass, IStablePassResourceLayout
    {
        private const string HistoryKey = "ColorPyramid";
        private const int ThreadGroupSize = 8;
        private const int MaxMipCount = 13;

        private static readonly int InputTextureId = Shader.PropertyToID("_InputTexture");
        private static readonly int InputPyramidTextureId = Shader.PropertyToID("_InputPyramidTexture");
        private static readonly int OutputPyramidTextureId = Shader.PropertyToID("_OutputPyramidTexture");
        private static readonly int OutputSizeId = Shader.PropertyToID("_OutputSize");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source;

        private RenderGraphTexture m_CurrentColorPyramid;

        private sealed class GraphPassData
        {
            public ColorPyramidPass Pass;
            public ContextContainer FrameData;
        }

        private RenderGraphTexture m_PreviousColorPyramid;
        private ComputeShader m_ComputeShader;
        private int m_CopyMip0Kernel = -1;
        private int m_DownsampleKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_MipCount = 1;
        private bool m_IsPassResourceLayoutDirty;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public ColorPyramidPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ColorPyramidPass));
            source = RenderGraphTexture.CreateInput("source", GraphicsFormat.R16G16B16A16_SFloat);
            m_PreviousColorPyramid = CreateColorPyramidTexture("ColorPyramidHistory");
            m_CurrentColorPyramid = CreateColorPyramidTexture("ColorPyramidHistoryCurrent");
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.ColorPyramidCompute;
            if (m_ComputeShader == null)
                return;

            try
            {
                m_CopyMip0Kernel = m_ComputeShader.FindKernel("CopyMip0");
                m_DownsampleKernel = m_ComputeShader.FindKernel("DownsampleMip");
            }
            catch (ArgumentException)
            {
                m_CopyMip0Kernel = -1;
                m_DownsampleKernel = -1;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var colorPyramidData = frameData.GetOrCreate<VividColorPyramidData>();
            colorPyramidData.Reset();

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);
            m_MipCount = Mathf.Clamp(CalculateMipCount(m_Width, m_Height), 1, MaxMipCount);

            ConfigureSourceDescriptor(source, m_Width, m_Height);
            ConfigurePyramidDescriptor(m_PreviousColorPyramid, "ColorPyramidHistory", m_Width, m_Height, m_MipCount);
            ConfigurePyramidDescriptor(m_CurrentColorPyramid, "ColorPyramidHistoryCurrent", m_Width, m_Height, m_MipCount);

            if (!HasValidShader())
                return;

            var hasValidHistory = AllocHistoryTexture(
                HistoryKey,
                m_PreviousColorPyramid,
                m_CurrentColorPyramid,
                m_CurrentColorPyramid.desc);

            colorPyramidData.hasValidHistory = hasValidHistory;
            colorPyramidData.previousColorPyramid = m_PreviousColorPyramid;
            colorPyramidData.currentColorPyramid = m_CurrentColorPyramid;
            colorPyramidData.width = m_Width;
            colorPyramidData.height = m_Height;
            colorPyramidData.mipCount = m_MipCount;
        }

        public override void Record(ComputePassContext context)
        {
            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (!CanExecute())
                    return;

                cmd.SetComputeVectorParam(
                    m_ComputeShader,
                    OutputSizeId,
                    new Vector4(m_Width, m_Height, 1.0f / Mathf.Max(1, m_Width), 1.0f / Mathf.Max(1, m_Height)));
                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyMip0Kernel, InputTextureId, source.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyMip0Kernel, OutputPyramidTextureId, m_CurrentColorPyramid.innerHandle, 0);
                cmd.DispatchCompute(
                    m_ComputeShader,
                    m_CopyMip0Kernel,
                    CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                    CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                    1);

                for (var mip = 1; mip < m_MipCount; mip++)
                {
                    var mipWidth = Mathf.Max(1, m_Width >> mip);
                    var mipHeight = Mathf.Max(1, m_Height >> mip);
                    cmd.SetComputeVectorParam(
                        m_ComputeShader,
                        OutputSizeId,
                        new Vector4(mipWidth, mipHeight, 1.0f / mipWidth, 1.0f / mipHeight));
                    cmd.SetComputeTextureParam(m_ComputeShader, m_DownsampleKernel, InputPyramidTextureId, m_CurrentColorPyramid.innerHandle, mip - 1);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_DownsampleKernel, OutputPyramidTextureId, m_CurrentColorPyramid.innerHandle, mip);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_DownsampleKernel,
                        CoreUtils.DivRoundUp(mipWidth, ThreadGroupSize),
                        CoreUtils.DivRoundUp(mipHeight, ThreadGroupSize),
                        1);
                }
            }
        }

        public void RecordGraph(RenderGraphRecordingContext context)
        {
            if (context?.RenderGraph == null || source == null)
                return;

            var sourceHandle = context.GetOrCreateTextureHandle(source);
            var currentHandle = context.GetOrCreateTextureHandle(m_CurrentColorPyramid);
            if (!sourceHandle.IsValid() || !currentHandle.IsValid())
                return;

            source.innerHandle = sourceHandle;
            m_CurrentColorPyramid.innerHandle = currentHandle;

            if (!CanExecute())
                return;

            PassRecorder.RegisterHistoryTextureWriteForPass(this, HistoryKey, m_CurrentColorPyramid);

            using var builder = context.RenderGraph.AddComputePass<GraphPassData>(
                nameof(ColorPyramidPass),
                out var passData);

            passData.Pass = this;
            passData.FrameData = context.FrameData;

            builder.UseTexture(sourceHandle, AccessFlags.Read);
            builder.UseTexture(currentHandle, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (GraphPassData data, ComputeGraphContext graphContext) =>
            {
                data.Pass.Record(new ComputePassContext(graphContext, data.FrameData));
            });
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_CopyMip0Kernel = -1;
            m_DownsampleKernel = -1;
            m_IsPassResourceLayoutDirty = false;
        }

        private bool CanExecute()
        {
            return HasValidShader()
                && source?.innerHandle.IsValid() == true
                && m_CurrentColorPyramid?.innerHandle.IsValid() == true;
        }

        private bool HasValidShader()
        {
            return m_ComputeShader != null
                && m_CopyMip0Kernel >= 0
                && m_DownsampleKernel >= 0;
        }

        private static int CalculateMipCount(int width, int height)
        {
            int maxDimension = Mathf.Max(1, Mathf.Max(width, height));
            return Mathf.FloorToInt(Mathf.Log(maxDimension, 2.0f)) + 1;
        }

        private static void ConfigureSourceDescriptor(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        private static void ConfigurePyramidDescriptor(
            RenderGraphTexture texture,
            string name,
            int width,
            int height,
            int mipCount)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = true;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = Mathf.Clamp(mipCount, 1, MaxMipCount);
            texture.desc.ClearBuffer = false;
            texture.desc.Name = name;
        }

        private static RenderGraphTexture CreateColorPyramidTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat)
            };

            ConfigurePyramidDescriptor(texture, name, 1, 1, 1);
            return texture;
        }
    }
}
