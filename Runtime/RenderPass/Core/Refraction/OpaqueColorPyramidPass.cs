using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal sealed class VividRefractionFrameData : ContextItem
    {
        internal int Width;
        internal int Height;
        internal int MipCount;
        internal bool HasOpaquePyramid;

        public override void Reset()
        {
            Width = 0;
            Height = 0;
            MipCount = 1;
            HasOpaquePyramid = false;
        }
    }

    /// <summary>
    /// Standard RenderGraph ComputePass that builds a same-frame opaque scene color
    /// mip pyramid (reusing VividRP's FFX SPD ColorPyramid.compute) and a hazard-free
    /// R32_SFloat copy of the opaque scene depth (reusing HDRPHZB.compute KCopyDepthToAtlas)
    /// directly before refractive transparent passes.
    /// </summary>
    public sealed class OpaqueColorPyramidPass : ComputePass
    {
        private const int ThreadGroupSize = 8;
        private const int MaxMipCount = 13;
        private const int AtomicCounterCount = 6;

        private static readonly int InputTextureId = Shader.PropertyToID("_InputTexture");
        private static readonly int OutputSizeId = Shader.PropertyToID("_OutputSize");
        private static readonly int MipsId = Shader.PropertyToID("mips");
        private static readonly int NumWorkGroupsId = Shader.PropertyToID("numWorkGroups");
        private static readonly int WorkGroupOffsetId = Shader.PropertyToID("workGroupOffset");
        private static readonly int GlobalAtomicBufferId = Shader.PropertyToID("spdGlobalAtomic");
        private static readonly int InputDepthId = Shader.PropertyToID("_InputDepth");
        private static readonly int DepthMipChainId = Shader.PropertyToID("_DepthMipChain");
        private static readonly int DstOffsetAndSizeId = Shader.PropertyToID("_DstOffsetAndSize");
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

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "OpaqueColorPyramid",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OpaqueColorPyramid;

        [RenderGraphResource(
            Name = "OpaqueDepth",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OpaqueDepth;

        private ComputeShader m_ColorPyramidCompute;
        private ComputeShader m_CopyDepthCompute;
        private int m_CopyMip0Kernel = -1;
        private int m_SpdKernel = -1;
        private int m_CopyDepthKernel = -1;

        private GraphicsBuffer m_GlobalAtomicBuffer;
        private readonly int[] m_DstOffsetAndSize = new int[4];

        private int m_Width = 1;
        private int m_Height = 1;
        private int m_MipCount = 1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private int m_NumWorkGroups = 1;

        public OpaqueColorPyramidPass()
        {
            profilingSampler = new ProfilingSampler(nameof(OpaqueColorPyramidPass));
            m_ColorTexture = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_OpaqueColorPyramid = RenderGraphTexture.CreateColorTarget("OpaqueColorPyramid", GraphicsFormat.R16G16B16A16_SFloat);
            ConfigurePyramidDescriptor(m_OpaqueColorPyramid, 1, 1, 1);

            m_OpaqueDepth = RenderGraphTexture.CreateColorTarget("OpaqueDepth", GraphicsFormat.R32_SFloat);
            m_OpaqueDepth.desc.ClearBuffer = false;
            m_OpaqueDepth.desc.FilterMode = FilterMode.Point;
            m_OpaqueDepth.desc.WrapMode = TextureWrapMode.Clamp;
            m_OpaqueDepth.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            EnsureShadersLoaded();
        }

        private bool EnsureShadersLoaded()
        {
            if (m_ColorPyramidCompute != null && m_CopyMip0Kernel >= 0 && m_SpdKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ColorPyramidCompute = resources?.ColorPyramidCompute;
            if (m_ColorPyramidCompute != null)
            {
                try
                {
                    m_CopyMip0Kernel = m_ColorPyramidCompute.FindKernel("CopyMip0");
                    m_SpdKernel = m_ColorPyramidCompute.FindKernel("KMain");
                }
                catch (ArgumentException)
                {
                    m_ColorPyramidCompute = null;
                    m_CopyMip0Kernel = -1;
                    m_SpdKernel = -1;
                }
            }

            m_CopyDepthCompute = resources?.HDRPHZBCompute;
            if (m_CopyDepthCompute != null && m_CopyDepthCompute.HasKernel("KCopyDepthToAtlas"))
                m_CopyDepthKernel = m_CopyDepthCompute.FindKernel("KCopyDepthToAtlas");
            else
                m_CopyDepthKernel = -1;

            return m_ColorPyramidCompute != null && m_CopyMip0Kernel >= 0 && m_SpdKernel >= 0;
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return ForwardRefractionPass.EnableRefraction && EnsureShadersLoaded();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            int width = m_ColorTexture?.desc != null && m_ColorTexture.desc.Width > 0
                ? m_ColorTexture.desc.Width
                : CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            int height = m_ColorTexture?.desc != null && m_ColorTexture.desc.Height > 0
                ? m_ColorTexture.desc.Height
                : CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);

            m_Width = Mathf.Max(1, width);
            m_Height = Mathf.Max(1, height);
            m_MipCount = Mathf.Clamp(CalculateMipCount(m_Width, m_Height), 1, MaxMipCount);
            m_DispatchGroupCountX = Mathf.Max(1, (m_Width + 63) >> 6);
            m_DispatchGroupCountY = Mathf.Max(1, (m_Height + 63) >> 6);
            m_NumWorkGroups = m_DispatchGroupCountX * m_DispatchGroupCountY;

            ConfigurePyramidDescriptor(m_OpaqueColorPyramid, m_Width, m_Height, m_MipCount);
            m_OpaqueDepth.Resize(m_Width, m_Height);
            m_OpaqueDepth.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            m_OpaqueDepth.desc.ClearBuffer = false;
            m_OpaqueDepth.desc.FilterMode = FilterMode.Point;
            m_OpaqueDepth.desc.WrapMode = TextureWrapMode.Clamp;
            m_OpaqueDepth.desc.EnableRandomWrite = true;

            EnsureAtomicCounterBuffer();
            m_GlobalAtomicBuffer?.SetData(s_ZeroAtomicCounterData);

            if (PassRecorder.IsPassTextureImportActive && m_GlobalAtomicBuffer != null && m_GlobalAtomicBuffer.IsValid())
                PassRecorder.ImportBufferForPass(this, m_GlobalAtomicBuffer, AccessFlags.ReadWrite);

            var refractionData = frameData.GetOrCreate<VividRefractionFrameData>();
            refractionData.Width = m_Width;
            refractionData.Height = m_Height;
            refractionData.MipCount = m_MipCount;
            refractionData.HasOpaquePyramid = true;
        }

        public override void Record(ComputePassContext context)
        {
            if (!EnsureShadersLoaded()
                || m_ColorTexture == null
                || !m_ColorTexture.innerHandle.IsValid()
                || m_OpaqueColorPyramid == null
                || !m_OpaqueColorPyramid.innerHandle.IsValid()
                || m_GlobalAtomicBuffer == null
                || !m_GlobalAtomicBuffer.IsValid())
            {
                return;
            }

            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 1. Copy opaque scene color into Mip 0
                cmd.SetComputeVectorParam(
                    m_ColorPyramidCompute,
                    OutputSizeId,
                    new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));
                cmd.SetComputeTextureParam(m_ColorPyramidCompute, m_CopyMip0Kernel, InputTextureId, m_ColorTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ColorPyramidCompute, m_CopyMip0Kernel, s_MipTextureIds[0], m_OpaqueColorPyramid.innerHandle, 0);
                cmd.DispatchCompute(
                    m_ColorPyramidCompute,
                    m_CopyMip0Kernel,
                    CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                    CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                    1);

                // 2. Single-pass SPD mip chain generation for rough transmission sampling
                if (m_MipCount > 1)
                {
                    cmd.SetComputeIntParam(m_ColorPyramidCompute, MipsId, m_MipCount - 1);
                    cmd.SetComputeIntParam(m_ColorPyramidCompute, NumWorkGroupsId, m_NumWorkGroups);
                    cmd.SetComputeVectorParam(m_ColorPyramidCompute, WorkGroupOffsetId, s_ZeroWorkGroupOffset);
                    cmd.SetComputeBufferParam(m_ColorPyramidCompute, m_SpdKernel, GlobalAtomicBufferId, m_GlobalAtomicBuffer);

                    for (int i = 0; i < s_MipTextureIds.Length; i++)
                    {
                        int boundMip = Mathf.Clamp(i, 0, Mathf.Max(0, m_MipCount - 1));
                        cmd.SetComputeTextureParam(
                            m_ColorPyramidCompute,
                            m_SpdKernel,
                            s_MipTextureIds[i],
                            m_OpaqueColorPyramid.innerHandle,
                            boundMip);
                    }

                    cmd.DispatchCompute(m_ColorPyramidCompute, m_SpdKernel, m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
                }

                // 3. Copy opaque depth to R32_SFloat SRV for foreground rejection during refraction
                if (m_CopyDepthCompute != null
                    && m_CopyDepthKernel >= 0
                    && m_DepthTexture != null
                    && m_DepthTexture.innerHandle.IsValid()
                    && m_OpaqueDepth != null
                    && m_OpaqueDepth.innerHandle.IsValid())
                {
                    m_DstOffsetAndSize[0] = 0;
                    m_DstOffsetAndSize[1] = 0;
                    m_DstOffsetAndSize[2] = m_Width;
                    m_DstOffsetAndSize[3] = m_Height;
                    cmd.SetComputeIntParams(m_CopyDepthCompute, DstOffsetAndSizeId, m_DstOffsetAndSize);
                    cmd.SetComputeTextureParam(m_CopyDepthCompute, m_CopyDepthKernel, InputDepthId, m_DepthTexture.innerHandle);
                    cmd.SetComputeTextureParam(m_CopyDepthCompute, m_CopyDepthKernel, DepthMipChainId, m_OpaqueDepth.innerHandle);
                    cmd.DispatchCompute(
                        m_CopyDepthCompute,
                        m_CopyDepthKernel,
                        CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                        CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                        1);
                }
            }
        }

        public override void Dispose()
        {
            m_ColorPyramidCompute = null;
            m_CopyDepthCompute = null;
            m_CopyMip0Kernel = -1;
            m_SpdKernel = -1;
            m_CopyDepthKernel = -1;
            m_GlobalAtomicBuffer?.Dispose();
            m_GlobalAtomicBuffer = null;
        }

        private void EnsureAtomicCounterBuffer()
        {
            const int stride = sizeof(uint) * AtomicCounterCount;
            if (m_GlobalAtomicBuffer == null || !m_GlobalAtomicBuffer.IsValid() || m_GlobalAtomicBuffer.stride != stride)
            {
                m_GlobalAtomicBuffer?.Dispose();
                m_GlobalAtomicBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride)
                {
                    name = "VividOpaqueColorPyramid_SpdAtomic",
                };
            }
        }

        private static int CalculateMipCount(int width, int height)
        {
            int maxDim = Mathf.Max(1, Mathf.Max(width, height));
            return Mathf.FloorToInt(Mathf.Log(maxDim, 2.0f)) + 1;
        }

        private static void ConfigurePyramidDescriptor(RenderGraphTexture texture, int width, int height, int mipCount)
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
            texture.desc.Name = "OpaqueColorPyramid";
        }
    }
}
