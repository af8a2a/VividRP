using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class ColorPyramidPass : ComputePass, IStablePassResourceLayout, IRenderGraphSideEffectPass
    {
        private const string HistoryKey = "ColorPyramid";
        private const int ThreadGroupSize = 8;
        private const int MaxMipCount = 13;
        private const int AtomicCounterCount = 6;

        private static readonly int InputTextureId = Shader.PropertyToID("_InputTexture");
        private static readonly int OutputSizeId = Shader.PropertyToID("_OutputSize");
        private static readonly int MipsId = Shader.PropertyToID("mips");
        private static readonly int NumWorkGroupsId = Shader.PropertyToID("numWorkGroups");
        private static readonly int WorkGroupOffsetId = Shader.PropertyToID("workGroupOffset");
        private static readonly int GlobalAtomicBufferId = Shader.PropertyToID("spdGlobalAtomic");
        private static readonly int[] s_MipTextureIds =
        {
            Shader.PropertyToID("rw_spd_mip0"),
            Shader.PropertyToID("rw_spd_mip1"),
            Shader.PropertyToID("rw_spd_mip2"),
            Shader.PropertyToID("rw_spd_mip3"),
            Shader.PropertyToID("rw_spd_mip4"),
            Shader.PropertyToID("rw_spd_mip5"),
            Shader.PropertyToID("rw_spd_mip6"),
            Shader.PropertyToID("rw_spd_mip7"),
            Shader.PropertyToID("rw_spd_mip8"),
            Shader.PropertyToID("rw_spd_mip9"),
            Shader.PropertyToID("rw_spd_mip10"),
            Shader.PropertyToID("rw_spd_mip11"),
            Shader.PropertyToID("rw_spd_mip12"),
        };

        private static readonly Vector4 s_ZeroWorkGroupOffset = Vector4.zero;
        private static readonly SpdGlobalAtomicBufferData[] s_ZeroAtomicCounterData = { default };

        [StructLayout(LayoutKind.Sequential)]
        private struct SpdGlobalAtomicBufferData
        {
            public uint Counter0;
            public uint Counter1;
            public uint Counter2;
            public uint Counter3;
            public uint Counter4;
            public uint Counter5;
        }

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source;

        [RenderGraphResource(
            Name = "ColorPyramidGlobalAtomic",
            Access = AccessFlags.ReadWrite)]
        private RenderGraphBuffer m_GlobalAtomicBuffer;

        [RenderGraphResource(
            Name = "ColorPyramid",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_CurrentColorPyramid;

        private RenderGraphTexture m_PreviousColorPyramid;
        private ComputeShader m_ComputeShader;
        private GraphicsBuffer m_GlobalAtomicImportedBuffer;
        private int m_CopyMip0Kernel = -1;
        private int m_SpdKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_MipCount = 1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private int m_NumWorkGroups = 1;
        private bool m_IsPassResourceLayoutDirty;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public ColorPyramidPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ColorPyramidPass));
            source = RenderGraphTexture.CreateInput("source", GraphicsFormat.R16G16B16A16_SFloat);
            m_GlobalAtomicBuffer = CreateAtomicCounterBuffer("ColorPyramidGlobalAtomic");
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
                m_SpdKernel = m_ComputeShader.FindKernel("KMain");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[VividRP] ColorPyramid.compute is missing CopyMip0 or KMain. Color pyramid generation will be skipped.");
                m_ComputeShader = null;
                m_CopyMip0Kernel = -1;
                m_SpdKernel = -1;
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
            m_DispatchGroupCountX = Mathf.Max(1, (m_Width + 63) >> 6);
            m_DispatchGroupCountY = Mathf.Max(1, (m_Height + 63) >> 6);
            m_NumWorkGroups = m_DispatchGroupCountX * m_DispatchGroupCountY;

            ConfigureSourceDescriptor(source, m_Width, m_Height);
            ConfigurePyramidDescriptor(m_PreviousColorPyramid, "ColorPyramidHistory", m_Width, m_Height, m_MipCount);
            ConfigurePyramidDescriptor(m_CurrentColorPyramid, "ColorPyramidHistoryCurrent", m_Width, m_Height, m_MipCount);
            EnsureAtomicCounterBuffer();
            ZeroAtomicCounterBuffer();

            if (!HasValidShader())
                return;

            var hasValidHistory = AllocHistoryTexture(
                HistoryKey,
                m_PreviousColorPyramid,
                m_CurrentColorPyramid,
                m_CurrentColorPyramid.desc);
            PassRecorder.TryGetHistoryTextureHandlesForPass(
                this,
                HistoryKey,
                out var previousColorPyramidHandle,
                out _,
                out _);

            var previousViewportSize = ResolvePreviousColorPyramidViewportSize(
                previousColorPyramidHandle,
                m_Width,
                m_Height);

            colorPyramidData.hasValidHistory = hasValidHistory;
            colorPyramidData.previousColorPyramid = m_PreviousColorPyramid;
            colorPyramidData.currentColorPyramid = m_CurrentColorPyramid;
            colorPyramidData.width = m_Width;
            colorPyramidData.height = m_Height;
            colorPyramidData.previousWidth = previousViewportSize.x;
            colorPyramidData.previousHeight = previousViewportSize.y;
            colorPyramidData.mipCount = m_MipCount;
            colorPyramidData.previousColorPyramidUvScaleAndLimit = ComputePreviousColorPyramidUvScaleAndLimit(
                previousColorPyramidHandle,
                m_Width,
                m_Height);
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
                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyMip0Kernel, s_MipTextureIds[0], m_CurrentColorPyramid.innerHandle, 0);
                cmd.DispatchCompute(
                    m_ComputeShader,
                    m_CopyMip0Kernel,
                    CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                    CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                    1);

                if (m_MipCount > 1)
                {
                    cmd.SetComputeIntParam(m_ComputeShader, MipsId, m_MipCount - 1);
                    cmd.SetComputeIntParam(m_ComputeShader, NumWorkGroupsId, m_NumWorkGroups);
                    cmd.SetComputeVectorParam(m_ComputeShader, WorkGroupOffsetId, s_ZeroWorkGroupOffset);
                    cmd.SetComputeBufferParam(
                        m_ComputeShader,
                        m_SpdKernel,
                        GlobalAtomicBufferId,
                        m_GlobalAtomicBuffer.innerHandle);

                    BindMipTextureViews(cmd, m_ComputeShader, m_SpdKernel, m_CurrentColorPyramid.innerHandle, m_MipCount);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_SpdKernel,
                        m_DispatchGroupCountX,
                        m_DispatchGroupCountY,
                        1);
                }
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_CopyMip0Kernel = -1;
            m_SpdKernel = -1;
            ReleaseAtomicCounterBuffer();
            m_IsPassResourceLayoutDirty = false;
        }

        private bool CanExecute()
        {
            return HasValidShader()
                && source?.innerHandle.IsValid() == true
                && m_CurrentColorPyramid?.innerHandle.IsValid() == true
                && m_GlobalAtomicBuffer?.innerHandle.IsValid() == true;
        }

        private bool HasValidShader()
        {
            return m_ComputeShader != null
                && m_CopyMip0Kernel >= 0
                && m_SpdKernel >= 0;
        }

        private void EnsureAtomicCounterBuffer()
        {
            const int stride = sizeof(uint) * AtomicCounterCount;

            if (m_GlobalAtomicImportedBuffer == null
                || m_GlobalAtomicImportedBuffer.count != 1
                || m_GlobalAtomicImportedBuffer.stride != stride)
            {
                m_GlobalAtomicImportedBuffer?.Dispose();
                m_GlobalAtomicImportedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            }

            m_GlobalAtomicBuffer?.SetImportedBuffer(m_GlobalAtomicImportedBuffer);
        }

        private void ZeroAtomicCounterBuffer()
        {
            m_GlobalAtomicImportedBuffer?.SetData(s_ZeroAtomicCounterData);
        }

        private void ReleaseAtomicCounterBuffer()
        {
            m_GlobalAtomicBuffer?.ClearImportedBuffer();
            m_GlobalAtomicImportedBuffer?.Dispose();
            m_GlobalAtomicImportedBuffer = null;
        }

        private static int CalculateMipCount(int width, int height)
        {
            int maxDimension = Mathf.Max(1, Mathf.Max(width, height));
            return Mathf.FloorToInt(Mathf.Log(maxDimension, 2.0f)) + 1;
        }

        private static Vector2Int ResolvePreviousColorPyramidViewportSize(RTHandle historyHandle, int fallbackWidth, int fallbackHeight)
        {
            var fallbackSize = new Vector2Int(Mathf.Max(1, fallbackWidth), Mathf.Max(1, fallbackHeight));
            if (historyHandle == null)
                return fallbackSize;

            var properties = historyHandle.rtHandleProperties;
            if (IsValidSize(properties.previousViewportSize))
                return properties.previousViewportSize;

            if (IsValidSize(properties.currentViewportSize))
                return properties.currentViewportSize;

            return fallbackSize;
        }

        private static Vector4 ComputePreviousColorPyramidUvScaleAndLimit(
            RTHandle historyHandle,
            int fallbackWidth,
            int fallbackHeight)
        {
            Vector2Int viewportSize = ResolvePreviousColorPyramidViewportSize(historyHandle, fallbackWidth, fallbackHeight);
            Vector2Int renderTargetSize = ResolvePreviousColorPyramidRenderTargetSize(historyHandle, viewportSize);
            return ComputeViewportScaleAndLimit(viewportSize, renderTargetSize);
        }

        private static Vector2Int ResolvePreviousColorPyramidRenderTargetSize(RTHandle historyHandle, Vector2Int fallbackSize)
        {
            if (historyHandle == null)
                return ClampSize(fallbackSize);

            var properties = historyHandle.rtHandleProperties;
            if (IsValidSize(properties.previousRenderTargetSize))
                return properties.previousRenderTargetSize;

            if (historyHandle.rt != null)
                return new Vector2Int(Mathf.Max(1, historyHandle.rt.width), Mathf.Max(1, historyHandle.rt.height));

            if (IsValidSize(properties.currentRenderTargetSize))
                return properties.currentRenderTargetSize;

            return ClampSize(fallbackSize);
        }

        private static Vector4 ComputeViewportScaleAndLimit(Vector2Int viewportSize, Vector2Int renderTargetSize)
        {
            viewportSize = ClampSize(viewportSize);
            renderTargetSize = ClampSize(renderTargetSize);
            return new Vector4(
                viewportSize.x / (float)renderTargetSize.x,
                viewportSize.y / (float)renderTargetSize.y,
                (viewportSize.x - 0.5f) / renderTargetSize.x,
                (viewportSize.y - 0.5f) / renderTargetSize.y);
        }

        private static Vector2Int ClampSize(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private static bool IsValidSize(Vector2Int size)
        {
            return size.x > 0 && size.y > 0;
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

        private static RenderGraphBuffer CreateAtomicCounterBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = sizeof(uint) * AtomicCounterCount,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }

        private static void BindMipTextureViews(
            ComputeCommandBuffer cmd,
            ComputeShader computeShader,
            int kernelIndex,
            TextureHandle colorPyramidHandle,
            int mipCount)
        {
            if (computeShader == null || !colorPyramidHandle.IsValid())
                return;

            for (int shaderMipIndex = 0; shaderMipIndex < s_MipTextureIds.Length; shaderMipIndex++)
            {
                int boundMipIndex = GetBoundMipIndex(shaderMipIndex, mipCount);
                cmd.SetComputeTextureParam(computeShader, kernelIndex, s_MipTextureIds[shaderMipIndex], colorPyramidHandle, boundMipIndex);
            }
        }

        private static int GetBoundMipIndex(int shaderMipIndex, int mipCount)
        {
            int clampedMipCount = Mathf.Clamp(mipCount, 1, s_MipTextureIds.Length);
            return Mathf.Clamp(shaderMipIndex, 0, clampedMipCount - 1);
        }
    }
}
