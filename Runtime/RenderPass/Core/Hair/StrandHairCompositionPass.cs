using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Strand-Based Hair Composition Pass (IJGC SIGGRAPH 2025):
    /// Composites each visible hair model back-to-front over SceneColor by
    /// interpolating per-vertex Strand-Space shaded radiance along each segment
    /// across the 3 ordered front visibility layers, reconstructing total coverage
    /// quadratically, and extrapolating deep-layer radiance (w_i = C_i * 0.25^(3-i)).
    /// Standard RenderGraph node with automatic zero-overhead skip when no hair is visible.
    /// </summary>
    public sealed class StrandHairCompositionPass : ComputePass
    {
        private const string ComposeKernelName = "KComposeHairModel";

        private static readonly int RestVerticesId = Shader.PropertyToID("_HairRestVertices");
        private static readonly int SegmentIndicesId = Shader.PropertyToID("_HairSegmentIndices");
        private static readonly int VisibilitySHId = Shader.PropertyToID("_HairVisibilitySH");
        private static readonly int PostTransformClipId = Shader.PropertyToID("_HairPostTransformClip");
        private static readonly int WorldPosRadiusId = Shader.PropertyToID("_HairWorldPosRadius");
        private static readonly int VertexShadingId = Shader.PropertyToID("_HairVertexShading");
        private static readonly int VisAtlasId = Shader.PropertyToID("_HairVisAtlas");
        private static readonly int SceneColorId = Shader.PropertyToID("_SceneColor");

        private static readonly int LocalToWorldId = Shader.PropertyToID("_HairLocalToWorld");
        private static readonly int AtlasTileRectId = Shader.PropertyToID("_HairAtlasTileRect");
        private static readonly int ComposeRectId = Shader.PropertyToID("_HairComposeRect");
        private static readonly int FirstAtlasTileId = Shader.PropertyToID("_HairFirstAtlasTile");
        private static readonly int DownscaleFactorId = Shader.PropertyToID("_HairDownscaleFactor");
        private static readonly int HasBakedSHId = Shader.PropertyToID("_HairHasBakedSH");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_HairScreenSize");
        private static readonly int AtlasScreenSizeId = Shader.PropertyToID("_HairAtlasScreenSize");
        private static readonly int ProjScaleYId = Shader.PropertyToID("_HairProjScaleY");
        private static readonly int DebugViewModeId = Shader.PropertyToID("_HairDebugViewMode");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(Name = "HairVisAtlas", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_VisAtlasBuffer;

        [RenderGraphResource(Name = "HairShadingSync", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ShadingSyncBuffer;

        private ComputeShader m_Compute;
        private int m_ComposeKernel = -1;

        private StrandHairFrameData m_FrameData;
        private StrandHairGpuPool m_GpuPool;
        private GraphicsBuffer m_PreExposureBuffer;

        public StrandHairCompositionPass()
        {
            profilingSampler = new ProfilingSampler(nameof(StrandHairCompositionPass));
            m_ColorTexture = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_ColorTexture.desc.EnableRandomWrite = true;
            m_VisAtlasBuffer = RenderGraphBuffer.CreateStructured("HairVisAtlas", 1, sizeof(uint));
            m_ShadingSyncBuffer = RenderGraphBuffer.CreateStructured("HairShadingSync", 1, sizeof(uint));
        }

        public override void Create()
        {
            EnsureShaderLoaded();
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null && m_ComposeKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.StrandHairCompositionCompute;
            if (m_Compute == null)
                return false;

            m_ComposeKernel = m_Compute.FindKernel(ComposeKernelName);
            return m_ComposeKernel >= 0;
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return frameData != null
                && frameData.Contains<StrandHairFrameData>()
                && frameData.Get<StrandHairFrameData>().HasVisibleHair
                && EnsureShaderLoaded();
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_FrameData = frameData != null && frameData.Contains<StrandHairFrameData>()
                ? frameData.Get<StrandHairFrameData>()
                : null;
            m_GpuPool = m_FrameData?.GpuPool ?? StrandHairGpuPool.Shared;

            if (m_FrameData == null || !m_FrameData.HasVisibleHair || !EnsureShaderLoaded())
                return;

            // Ensure the bound SceneColor target supports UAV writes even when produced by a raster/blit pass (e.g. AtmosphericScatteringPass)
            if (m_ColorTexture?.desc != null)
                m_ColorTexture.desc.EnableRandomWrite = true;

            m_PreExposureBuffer = VividAutoExposureSystem.ResolvePreExposureBuffer(
                frameData.Get<VividExposureData>());

            if (!PassRecorder.IsPassTextureImportActive)
                return;

            int allocCount = m_FrameData.Allocations.Count;
            for (int slotIdx = 0; slotIdx < allocCount; slotIdx++)
            {
                StrandHairModelAtlasAllocation alloc = m_FrameData.Allocations[slotIdx];
                if (!alloc.IsVisible || alloc.TileCount <= 0)
                    continue;

                HairGroomRenderEntry entry = m_FrameData.ActiveEntries[alloc.EntryIndex];
                HairGroomGpuResource gpuRes = entry.Asset?.GetOrCreateGpuResource();
                if (gpuRes == null || !gpuRes.IsValid)
                    continue;

                StrandHairModelScratchSlot slot = m_GpuPool.GetOrCreateSlot(
                    slotIdx,
                    gpuRes.VertexCount,
                    gpuRes.SegmentCount);
                if (slot.PostTransformClipBuffer != null && slot.PostTransformClipBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.PostTransformClipBuffer, AccessFlags.Read);
                if (slot.WorldPosRadiusBuffer != null && slot.WorldPosRadiusBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.WorldPosRadiusBuffer, AccessFlags.Read);
                if (slot.VertexShadingBuffer != null && slot.VertexShadingBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.VertexShadingBuffer, AccessFlags.Read);
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (m_FrameData == null
                || !m_FrameData.HasVisibleHair
                || !m_FrameData.VisibilityRecordedThisFrame
                || m_FrameData.VisAtlasBuffer == null
                || !m_FrameData.VisAtlasBuffer.IsValid()
                || m_ColorTexture == null
                || !m_ColorTexture.innerHandle.IsValid()
                || m_GpuPool == null
                || !EnsureShaderLoaded())
            {
                return;
            }

            ComputeCommandBuffer cmd = context.cmd;
            int downscale = Mathf.Max(1, m_FrameData.DownscaleFactor);
            float atlasWidth = Mathf.Max(1, (m_FrameData.ScreenWidth + downscale - 1) / downscale);
            float atlasHeight = Mathf.Max(1, (m_FrameData.ScreenHeight + downscale - 1) / downscale);

            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetComputeTextureParam(m_Compute, m_ComposeKernel, SceneColorId, m_ColorTexture.innerHandle);
                if (m_VisAtlasBuffer != null && m_VisAtlasBuffer.innerHandle.IsValid())
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, VisAtlasId, m_VisAtlasBuffer.innerHandle);
                else
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, VisAtlasId, m_FrameData.VisAtlasBuffer);

                if (m_PreExposureBuffer != null)
                {
                    cmd.SetComputeBufferParam(
                        m_Compute,
                        m_ComposeKernel,
                        VividAutoExposureSystem.PreExposureBufferId,
                        m_PreExposureBuffer);
                }

                cmd.SetComputeIntParam(m_Compute, DownscaleFactorId, downscale);
                cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, new Vector4(m_FrameData.ScreenWidth, m_FrameData.ScreenHeight, 0f, 0f));
                cmd.SetComputeVectorParam(m_Compute, AtlasScreenSizeId, new Vector4(atlasWidth, atlasHeight, 0f, 0f));
                cmd.SetComputeFloatParam(m_Compute, ProjScaleYId, m_FrameData.ProjScaleY);
                cmd.SetComputeIntParam(m_Compute, DebugViewModeId, (int)StrandHairGpuPool.GlobalDebugViewMode);

                // Allocations are already sorted back-to-front by StrandHairAtlasAllocator
                int allocCount = m_FrameData.Allocations.Count;
                for (int slotIdx = 0; slotIdx < allocCount; slotIdx++)
                {
                    StrandHairModelAtlasAllocation alloc = m_FrameData.Allocations[slotIdx];
                    if (!alloc.IsVisible || alloc.TileCount <= 0 || alloc.ComposeSize.x <= 0 || alloc.ComposeSize.y <= 0)
                        continue;

                    HairGroomRenderEntry entry = m_FrameData.ActiveEntries[alloc.EntryIndex];
                    HairGroomGpuResource gpuRes = entry.Asset?.GetOrCreateGpuResource();
                    if (gpuRes == null || !gpuRes.IsValid)
                        continue;

                    StrandHairModelScratchSlot slot = m_GpuPool.GetOrCreateSlot(
                        slotIdx,
                        gpuRes.VertexCount,
                        gpuRes.SegmentCount);

                    cmd.SetComputeMatrixParam(m_Compute, LocalToWorldId, entry.LocalToWorld);
                    cmd.SetComputeIntParam(m_Compute, FirstAtlasTileId, alloc.FirstAtlasTile);
                    cmd.SetComputeIntParam(m_Compute, HasBakedSHId, gpuRes.HasBakedVisibilitySH ? 1 : 0);
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        AtlasTileRectId,
                        new Vector4(alloc.TileOrigin.x, alloc.TileOrigin.y, alloc.TileGridSize.x, alloc.TileGridSize.y));
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        ComposeRectId,
                        new Vector4(alloc.ComposeMinPixel.x, alloc.ComposeMinPixel.y, alloc.ComposeSize.x, alloc.ComposeSize.y));

                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, RestVerticesId, gpuRes.RestVertexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, SegmentIndicesId, gpuRes.SegmentIndexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, VisibilitySHId, gpuRes.VisibilitySHBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, VertexShadingId, slot.VertexShadingBuffer);

                    int gx = (alloc.ComposeSize.x + 7) >> 3;
                    int gy = (alloc.ComposeSize.y + 7) >> 3;
                    cmd.DispatchCompute(m_Compute, m_ComposeKernel, gx, gy, 1);
                }
            }
        }

        public override void Dispose()
        {
            m_Compute = null;
            m_ComposeKernel = -1;
            m_FrameData = null;
            m_GpuPool = null;
            m_PreExposureBuffer = null;
        }
    }
}
