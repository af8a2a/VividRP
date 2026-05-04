using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class HDRPHZBPass : ComputePass, IStablePassResourceLayout
    {
        private const int MaxMipLevelCount = 15;
        private const int ThreadGroupSize = 8;

        private static readonly int InputDepthId = Shader.PropertyToID("_InputDepth");
        private static readonly int DepthMipChainId = Shader.PropertyToID("_DepthMipChain");
        private static readonly int SrcOffsetAndLimitId = Shader.PropertyToID("_SrcOffsetAndLimit");
        private static readonly int DstOffsetAndSizeId = Shader.PropertyToID("_DstOffsetAndSize");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "HZB", Access = AccessFlags.Write)]
        private RenderGraphTexture m_HzbTexture;

        [RenderGraphResource(Name = "HZBMipLevelOffsets", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_HzbMipLevelOffsets;

        private readonly Vector2Int[] m_MipLevelSizes = new Vector2Int[MaxMipLevelCount];
        private readonly Vector2Int[] m_MipLevelOffsets = new Vector2Int[MaxMipLevelCount];
        private readonly int2[] m_MipLevelOffsetData = new int2[MaxMipLevelCount];
        private readonly int[] m_SrcOffsetAndLimit = new int[4];
        private readonly int[] m_DstOffsetAndSize = new int[4];

        private ComputeShader m_ComputeShader;
        private int m_CopyDepthKernel = -1;
        private int m_DownsampleKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_MipLevelCount = 1;
        private Vector2Int m_AtlasSize = Vector2Int.one;
        private bool m_IsPassResourceLayoutDirty;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public HDRPHZBPass()
        {
            profilingSampler = new ProfilingSampler(nameof(HDRPHZBPass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_HzbTexture = CreateHZBTexture("HZB");
            m_HzbMipLevelOffsets = RenderGraphBuffer.CreateStructured("HZBMipLevelOffsets", MaxMipLevelCount, sizeof(int) * 2);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.HDRPHZBCompute;
            if (m_ComputeShader == null)
                return;

            try
            {
                m_CopyDepthKernel = m_ComputeShader.FindKernel("KCopyDepthToAtlas");
                m_DownsampleKernel = m_ComputeShader.FindKernel("KDepthDownsample8DualUav");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[VividRP] HDRPHZB.compute is missing KCopyDepthToAtlas or KDepthDownsample8DualUav. HZB generation will be skipped.");
                m_ComputeShader = null;
                m_CopyDepthKernel = -1;
                m_DownsampleKernel = -1;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);

            ComputePackedMipChainInfo(new Vector2Int(m_Width, m_Height));

            m_DepthTexture.Resize(m_Width, m_Height);
            ConfigureHZBTexture(m_HzbTexture, m_AtlasSize.x, m_AtlasSize.y);
            ConfigureMipLevelOffsetBuffer(m_HzbMipLevelOffsets);
            m_HzbMipLevelOffsets.SetData(m_MipLevelOffsetData);
            m_IsPassResourceLayoutDirty = false;
        }

        public override void Record(ComputePassContext context)
        {
            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (!CanExecute())
                    return;

                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyDepthKernel, InputDepthId, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_CopyDepthKernel, DepthMipChainId, m_HzbTexture.innerHandle);
                cmd.DispatchCompute(
                    m_ComputeShader,
                    m_CopyDepthKernel,
                    CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                    CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                    1);

                for (var mipLevel = 1; mipLevel < m_MipLevelCount; mipLevel++)
                {
                    var dstSize = m_MipLevelSizes[mipLevel];
                    var dstOffset = m_MipLevelOffsets[mipLevel];
                    var srcSize = m_MipLevelSizes[mipLevel - 1];
                    var srcOffset = m_MipLevelOffsets[mipLevel - 1];
                    var srcLimit = srcOffset + srcSize - Vector2Int.one;

                    m_SrcOffsetAndLimit[0] = srcOffset.x;
                    m_SrcOffsetAndLimit[1] = srcOffset.y;
                    m_SrcOffsetAndLimit[2] = srcLimit.x;
                    m_SrcOffsetAndLimit[3] = srcLimit.y;

                    m_DstOffsetAndSize[0] = dstOffset.x;
                    m_DstOffsetAndSize[1] = dstOffset.y;
                    m_DstOffsetAndSize[2] = dstSize.x;
                    m_DstOffsetAndSize[3] = dstSize.y;

                    cmd.SetComputeIntParams(m_ComputeShader, SrcOffsetAndLimitId, m_SrcOffsetAndLimit);
                    cmd.SetComputeIntParams(m_ComputeShader, DstOffsetAndSizeId, m_DstOffsetAndSize);
                    cmd.SetComputeTextureParam(m_ComputeShader, m_DownsampleKernel, DepthMipChainId, m_HzbTexture.innerHandle);
                    cmd.DispatchCompute(
                        m_ComputeShader,
                        m_DownsampleKernel,
                        CoreUtils.DivRoundUp(dstSize.x, ThreadGroupSize),
                        CoreUtils.DivRoundUp(dstSize.y, ThreadGroupSize),
                        1);
                }
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_CopyDepthKernel = -1;
            m_DownsampleKernel = -1;
            m_DepthTexture = null;
            m_HzbTexture = null;
            m_HzbMipLevelOffsets?.ClearImportedBuffer();
            m_HzbMipLevelOffsets = null;
            m_Width = 1;
            m_Height = 1;
            m_MipLevelCount = 1;
            m_AtlasSize = Vector2Int.one;
            m_IsPassResourceLayoutDirty = false;
        }

        private bool CanExecute()
        {
            return m_ComputeShader != null
                && m_CopyDepthKernel >= 0
                && m_DownsampleKernel >= 0
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_HzbTexture?.innerHandle.IsValid() == true;
        }

        private void ComputePackedMipChainInfo(Vector2Int viewportSize)
        {
            viewportSize.x = Mathf.Max(1, viewportSize.x);
            viewportSize.y = Mathf.Max(1, viewportSize.y);

            Array.Clear(m_MipLevelSizes, 0, m_MipLevelSizes.Length);
            Array.Clear(m_MipLevelOffsets, 0, m_MipLevelOffsets.Length);
            Array.Clear(m_MipLevelOffsetData, 0, m_MipLevelOffsetData.Length);

            m_MipLevelSizes[0] = viewportSize;
            m_MipLevelOffsets[0] = Vector2Int.zero;

            var atlasSize = viewportSize;
            var mipSize = viewportSize;
            var mipLevel = 0;

            while ((mipSize.x > 1 || mipSize.y > 1) && mipLevel + 1 < MaxMipLevelCount)
            {
                mipLevel++;
                mipSize.x = Mathf.Max(1, (mipSize.x + 1) >> 1);
                mipSize.y = Mathf.Max(1, (mipSize.y + 1) >> 1);

                m_MipLevelSizes[mipLevel] = mipSize;

                var previousMipBegin = m_MipLevelOffsets[mipLevel - 1];
                var previousMipEnd = previousMipBegin + m_MipLevelSizes[mipLevel - 1];
                var mipBegin = (mipLevel & 1) != 0
                    ? new Vector2Int(previousMipBegin.x, previousMipEnd.y)
                    : new Vector2Int(previousMipEnd.x, previousMipBegin.y);

                m_MipLevelOffsets[mipLevel] = mipBegin;
                atlasSize.x = Mathf.Max(atlasSize.x, mipBegin.x + mipSize.x);
                atlasSize.y = Mathf.Max(atlasSize.y, mipBegin.y + mipSize.y);
            }

            m_MipLevelCount = mipLevel + 1;
            m_AtlasSize = atlasSize;

            for (var i = 0; i < m_MipLevelCount; i++)
                m_MipLevelOffsetData[i] = new int2(m_MipLevelOffsets[i].x, m_MipLevelOffsets[i].y);
        }

        private static RenderGraphTexture CreateHZBTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32_SFloat)
            };

            ConfigureHZBTexture(texture, 1, 1);
            texture.desc.Name = name;
            return texture;
        }

        private static void ConfigureHZBTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.Name = "HZB";
            texture.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = false;
            texture.desc.BindTextureMS = false;
        }

        private static void ConfigureMipLevelOffsetBuffer(RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = MaxMipLevelCount;
            buffer.desc.Stride = sizeof(int) * 2;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = "HZBMipLevelOffsets";
        }
    }
}
