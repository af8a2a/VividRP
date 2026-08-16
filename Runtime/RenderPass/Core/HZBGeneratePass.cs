using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class HZBGeneratePass : ComputePass, IAsyncComputeSupportedPass
    {
        private const int MaxTextureMipCount = 13;
        private const int CopyKernelThreadGroupSize = 8;
        private const int AtomicCounterCount = 6;

        private static readonly int InputDepthId = Shader.PropertyToID("_InputDepth");
        private static readonly int HzbGlobalTextureId = Shader.PropertyToID("g_depth_tex_hiz");
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

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "HZB", Access = AccessFlags.Write)]
        private RenderGraphTexture m_HzbTexture;

        [RenderGraphResource(
            Name = "HZBGlobalAtomic",
            Access = AccessFlags.ReadWrite)]
        private RenderGraphBuffer m_GlobalAtomicBuffer;

        private ComputeShader m_ComputeShader;
        private GraphicsBuffer m_GlobalAtomicImportedBuffer;
        private int m_CopyKernel = -1;
        private int m_DownsampleKernel = -1;
        private int m_Width;
        private int m_Height;
        private int m_MipCount;
        private int m_DispatchGroupCountX;
        private int m_DispatchGroupCountY;
        private int m_NumWorkGroups;

        public HZBGeneratePass()
        {
            profilingSampler = new ProfilingSampler(nameof(HZBGeneratePass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_HzbTexture = CreateHzbTexture("HZB");
            m_GlobalAtomicBuffer = CreateAtomicCounterBuffer("HZBGlobalAtomic");
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.HZBGenerateCompute;
            if (m_ComputeShader == null)
                return;

            try
            {
                m_CopyKernel = m_ComputeShader.FindKernel("KCopyDepth");
                m_DownsampleKernel = m_ComputeShader.FindKernel("KMain");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[VividRP] HZBGenerate.compute is missing KCopyDepth or KMain. HZB generation will be skipped.");
                m_ComputeShader = null;
                m_CopyKernel = -1;
                m_DownsampleKernel = -1;
            }
        }

        public override void Resize(int width, int height)
        {
            m_Width = width;
            m_Height = height;

            m_MipCount = CalculateMipCount(m_Width, m_Height);
            m_DispatchGroupCountX = Mathf.Max(1, (m_Width + 63) >> 6);
            m_DispatchGroupCountY = Mathf.Max(1, (m_Height + 63) >> 6);
            m_NumWorkGroups = m_DispatchGroupCountX * m_DispatchGroupCountY;

            m_DepthTexture.Resize(m_Width, m_Height);
            ConfigureHzbTexture(m_HzbTexture, m_Width, m_Height, m_MipCount);
        }

        public override void Prepare(ContextContainer frameData)
        {
            EnsureAtomicCounterBuffer();
            ZeroAtomicCounterBuffer();
        }

        public override void Record(ComputePassContext context)
        {
            if (!CanExecute())
                return;

            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyKernel, InputDepthId, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyKernel, s_MipTextureIds[0], m_HzbTexture, 0);
                cmd.DispatchCompute(
                    m_ComputeShader,
                    m_CopyKernel,
                    CoreUtils.DivRoundUp(m_Width, CopyKernelThreadGroupSize),
                    CoreUtils.DivRoundUp(m_Height, CopyKernelThreadGroupSize),
                    1);

                if (m_MipCount > 1)
                {
                    cmd.SetComputeIntParam(m_ComputeShader, MipsId, m_MipCount - 1);
                    cmd.SetComputeIntParam(m_ComputeShader, NumWorkGroupsId, m_NumWorkGroups);
                    cmd.SetComputeVectorParam(m_ComputeShader, WorkGroupOffsetId, s_ZeroWorkGroupOffset);
                    cmd.SetComputeBufferParam(
                        m_ComputeShader,
                        m_DownsampleKernel,
                        GlobalAtomicBufferId,
                        m_GlobalAtomicImportedBuffer);

                    BindMipTextureViews(cmd, m_ComputeShader, m_DownsampleKernel, m_HzbTexture, m_MipCount);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_DownsampleKernel,
                        m_DispatchGroupCountX,
                        m_DispatchGroupCountY,
                        1);
                }

            }
        }


        public override void Dispose()
        {
            m_ComputeShader = null;
            m_CopyKernel = -1;
            m_DownsampleKernel = -1;
            m_DepthTexture = null;
            m_HzbTexture = null;
            ReleaseAtomicCounterBuffer();
            m_GlobalAtomicBuffer = null;
            m_Width = 0;
            m_Height = 0;
            m_MipCount = 0;
            m_DispatchGroupCountX = 0;
            m_DispatchGroupCountY = 0;
            m_NumWorkGroups = 0;
        }

        private bool CanExecute()
        {
            return m_ComputeShader != null
                && m_CopyKernel >= 0
                && m_DownsampleKernel >= 0
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_HzbTexture?.innerHandle.IsValid() == true
                && m_GlobalAtomicImportedBuffer != null;
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

        private static RenderGraphTexture CreateHzbTexture(string name)
        {
            // Preserve device-depth precision before consumers linearize it. R16 quantization
            // produces camera-near-dependent bands in GTAO on large, shallow-gradient surfaces.
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32_SFloat)
            };

            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = true;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.MsaaSamples = MSAASamples.None;
            return texture;
        }

        private static void ConfigureHzbTexture(RenderGraphTexture texture, int width, int height, int mipCount)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = true;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = Mathf.Clamp(mipCount, 1, MaxTextureMipCount);
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.ClearBuffer = false;
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

        private static int CalculateMipCount(int width, int height)
        {
            int mipCount = 1;
            int maxDimension = Mathf.Max(width, height);

            while (maxDimension > 1 && mipCount < MaxTextureMipCount)
            {
                maxDimension >>= 1;
                mipCount++;
            }

            return mipCount;
        }

        private static void BindMipTextureViews(
            ComputeCommandBuffer cmd,
            ComputeShader computeShader,
            int kernelIndex,
            RenderGraphTexture hzbHandle,
            int mipCount)
        {
            if (cmd == null || computeShader == null || hzbHandle == null)
                return;

            for (int shaderMipIndex = 0; shaderMipIndex < s_MipTextureIds.Length; shaderMipIndex++)
            {
                int boundMipIndex = GetBoundMipIndex(shaderMipIndex, mipCount);
                cmd.SetComputeTextureParam(computeShader, kernelIndex, s_MipTextureIds[shaderMipIndex], hzbHandle, boundMipIndex);
            }
        }

        private static int GetBoundMipIndex(int shaderMipIndex, int mipCount)
        {
            int clampedMipCount = Mathf.Clamp(mipCount, 1, s_MipTextureIds.Length);
            return Mathf.Clamp(shaderMipIndex, 0, clampedMipCount - 1);
        }
    }
}
