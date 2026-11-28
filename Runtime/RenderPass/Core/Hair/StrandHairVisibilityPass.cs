using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Phase S1 Strand-Based Hair Visibility Pass (IJGC SIGGRAPH 2025 architecture):
    /// 1) Allocates 64x64 tiles in the bounded 32-bit 3-Layer + Coverage Visibility Atlas.
    /// 2) Transforms rest vertices to global clip-space + world-space caches.
    /// 3) Performs frustum + conservative HZB segment culling with wave-aggregated append.
    /// 4) Conservatively software-rasterizes visible segments into the tiled Visibility Atlas.
    /// Implements IAsyncComputeSupportedPass for async overlap with opaque lighting/GI.
    /// </summary>
    public sealed class StrandHairVisibilityPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const string ClearAtlasKernelName = "KClearAtlasTiles";
        private const string TransformVerticesKernelName = "KTransformVertices";
        private const string ClearCullArgsKernelName = "KClearCullArgs";
        private const string CullSegmentsKernelName = "KCullSegments";
        private const string RasterizeSegmentsKernelName = "KRasterizeSegments";

        private static readonly int RestVerticesId = Shader.PropertyToID("_HairRestVertices");
        private static readonly int SegmentIndicesId = Shader.PropertyToID("_HairSegmentIndices");
        private static readonly int PostTransformClipId = Shader.PropertyToID("_HairPostTransformClip");
        private static readonly int WorldPosRadiusId = Shader.PropertyToID("_HairWorldPosRadius");
        private static readonly int VisibleSegmentListId = Shader.PropertyToID("_HairVisibleSegmentList");
        private static readonly int IndirectRasterArgsId = Shader.PropertyToID("_HairIndirectRasterArgs");
        private static readonly int VisAtlasId = Shader.PropertyToID("_HairVisAtlas");
        private static readonly int SceneDepthId = Shader.PropertyToID("_HairSceneDepth");
        private static readonly int HzbTextureId = Shader.PropertyToID("_HairHZBTexture");

        private static readonly int LocalToWorldId = Shader.PropertyToID("_HairLocalToWorld");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_HairViewMatrix");
        private static readonly int ViewProjMatrixId = Shader.PropertyToID("_HairViewProjMatrix");
        private static readonly int VertexCountId = Shader.PropertyToID("_HairVertexCount");
        private static readonly int SegmentCountId = Shader.PropertyToID("_HairSegmentCount");
        private static readonly int AllocatedTileCountId = Shader.PropertyToID("_HairAllocatedTileCount");
        private static readonly int FirstAtlasTileId = Shader.PropertyToID("_HairFirstAtlasTile");
        private static readonly int AtlasTileRectId = Shader.PropertyToID("_HairAtlasTileRect");
        private static readonly int DownscaleFactorId = Shader.PropertyToID("_HairDownscaleFactor");
        private static readonly int EnableHzbId = Shader.PropertyToID("_HairEnableHZB");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_HairScreenSize");
        private static readonly int AtlasScreenSizeId = Shader.PropertyToID("_HairAtlasScreenSize");
        private static readonly int HzbSizeId = Shader.PropertyToID("_HairHZBSize");
        private static readonly int RadiusScaleId = Shader.PropertyToID("_HairRadiusScale");
        private static readonly int ProjScaleYId = Shader.PropertyToID("_HairProjScaleY");
        private static readonly int DepthRangeNearId = Shader.PropertyToID("_HairDepthRangeNear");
        private static readonly int DepthRangeInvSpanId = Shader.PropertyToID("_HairDepthRangeInvSpan");
        private static readonly int NearClipPlaneId = Shader.PropertyToID("_HairNearClipPlane");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "HZB", Access = AccessFlags.Read)]
        private RenderGraphTexture m_HzbTexture;

        private ComputeShader m_Compute;
        private int m_ClearAtlasKernel = -1;
        private int m_TransformVerticesKernel = -1;
        private int m_ClearCullArgsKernel = -1;
        private int m_CullSegmentsKernel = -1;
        private int m_RasterizeSegmentsKernel = -1;

        private readonly StrandHairGpuPool m_GpuPool = new StrandHairGpuPool();
        private StrandHairFrameData m_FrameData;
        private Matrix4x4 m_ViewMatrix;
        private Matrix4x4 m_GpuViewProjMatrix;
        private float m_NearClipPlane = 0.05f;
        private int m_HzbWidth;
        private int m_HzbHeight;
        private int m_HzbMaxMip;

        internal StrandHairGpuPool GpuPool => m_GpuPool;

        public StrandHairVisibilityPass()
        {
            profilingSampler = new ProfilingSampler(nameof(StrandHairVisibilityPass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_HzbTexture = RenderGraphTexture.CreateInput("HZB", GraphicsFormat.R32_SFloat);
        }

        public override void Create()
        {
            EnsureShaderLoaded();
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null && m_RasterizeSegmentsKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.StrandHairVisibilityCompute;
            if (m_Compute == null)
                return false;

            m_ClearAtlasKernel = m_Compute.FindKernel(ClearAtlasKernelName);
            m_TransformVerticesKernel = m_Compute.FindKernel(TransformVerticesKernelName);
            m_ClearCullArgsKernel = m_Compute.FindKernel(ClearCullArgsKernelName);
            m_CullSegmentsKernel = m_Compute.FindKernel(CullSegmentsKernelName);
            m_RasterizeSegmentsKernel = m_Compute.FindKernel(RasterizeSegmentsKernelName);
            return m_RasterizeSegmentsKernel >= 0;
        }

        public override void Prepare(ContextContainer frameData)
        {
            PrepareInternal(frameData, m_HzbTexture?.desc, 0, 0);
        }

        internal bool PrepareInternal(
            ContextContainer frameData,
            RenderGraphTextureDesc hzbDesc,
            int overrideWidth,
            int overrideHeight)
        {
            m_FrameData = frameData.GetOrCreate<StrandHairFrameData>();
            m_FrameData.Reset();

            if (HairGroomSystem.ActiveCount == 0 || !EnsureShaderLoaded())
                return false;

            var cameraData = frameData.Get<VividCameraData>();
            if (cameraData == null || cameraData.camera == null)
                return false;

            int width = overrideWidth > 0
                ? overrideWidth
                : (cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth);
            int height = overrideHeight > 0
                ? overrideHeight
                : (cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight);
            if (width <= 0 || height <= 0)
                return false;

            int collected = HairGroomSystem.CollectActiveEntries(
                m_FrameData.ActiveEntries,
                cameraData.camera.cullingMask);
            if (collected <= 0)
                return false;

            m_ViewMatrix = cameraData.GetViewMatrix();
            Matrix4x4 projMatrix = cameraData.GetProjectionMatrix();
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(projMatrix, true);
            m_GpuViewProjMatrix = gpuProj * m_ViewMatrix;
            m_NearClipPlane = Mathf.Max(cameraData.camera.nearClipPlane, 1e-3f);

            int visibleCount = StrandHairAtlasAllocator.AllocateModels(
                m_FrameData.ActiveEntries,
                m_ViewMatrix,
                m_GpuViewProjMatrix,
                cameraData.camera.transform.position,
                m_NearClipPlane,
                width,
                height,
                StrandHairGpuPool.GlobalMaxAtlasTiles,
                m_FrameData.Allocations,
                out int totalTiles,
                out int downscale);

            if (visibleCount <= 0 || totalTiles <= 0)
                return false;

            int atlasHeight = Mathf.Max(1, (height + downscale - 1) / downscale);
            m_FrameData.VisAtlasBuffer = m_GpuPool.EnsureVisAtlasBuffer(StrandHairGpuPool.GlobalMaxAtlasTiles);
            m_FrameData.TotalAllocatedTiles = totalTiles;
            m_FrameData.DownscaleFactor = downscale;
            m_FrameData.ScreenWidth = width;
            m_FrameData.ScreenHeight = height;
            m_FrameData.ProjScaleY = Mathf.Abs(projMatrix.m11) * 0.5f * atlasHeight;
            m_FrameData.HasVisibleHair = true;

            if (hzbDesc != null && hzbDesc.Width > 0 && hzbDesc.Height > 0)
            {
                m_HzbWidth = hzbDesc.Width;
                m_HzbHeight = hzbDesc.Height;
                m_HzbMaxMip = Mathf.Max(0, hzbDesc.MipCount - 1);
            }
            else
            {
                m_HzbWidth = 0;
                m_HzbHeight = 0;
                m_HzbMaxMip = 0;
            }

            return true;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_DepthTexture == null || !m_DepthTexture.innerHandle.IsValid())
                return;

            TextureHandle hzbHandle = m_HzbTexture != null && m_HzbTexture.innerHandle.IsValid()
                ? m_HzbTexture.innerHandle
                : default;
            RecordWithHandles(context.cmd, m_DepthTexture.innerHandle, hzbHandle);
        }

        internal void RecordWithHandles(
            ComputeCommandBuffer cmd,
            TextureHandle depthHandle,
            TextureHandle hzbHandle)
        {
            if (m_FrameData == null
                || !m_FrameData.HasVisibleHair
                || m_FrameData.TotalAllocatedTiles <= 0
                || m_FrameData.VisAtlasBuffer == null
                || !m_FrameData.VisAtlasBuffer.IsValid()
                || !depthHandle.IsValid()
                || !EnsureShaderLoaded())
            {
                return;
            }

            int downscale = Mathf.Max(1, m_FrameData.DownscaleFactor);
            float atlasWidth = Mathf.Max(1, (m_FrameData.ScreenWidth + downscale - 1) / downscale);
            float atlasHeight = Mathf.Max(1, (m_FrameData.ScreenHeight + downscale - 1) / downscale);
            bool useHzb = hzbHandle.IsValid() && m_HzbWidth > 0 && m_HzbHeight > 0;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 1. Clear only the allocated tiles of the Visibility Atlas
                cmd.SetComputeIntParam(m_Compute, AllocatedTileCountId, m_FrameData.TotalAllocatedTiles);
                cmd.SetComputeBufferParam(m_Compute, m_ClearAtlasKernel, VisAtlasId, m_FrameData.VisAtlasBuffer);
                int clearGroups = (m_FrameData.TotalAllocatedTiles * StrandHairAtlasContract.TilePixels + 63) / 64;
                cmd.DispatchCompute(m_Compute, m_ClearAtlasKernel, clearGroups, 1, 1);

                // Shared camera uniforms
                cmd.SetComputeMatrixParam(m_Compute, ViewMatrixId, m_ViewMatrix);
                cmd.SetComputeMatrixParam(m_Compute, ViewProjMatrixId, m_GpuViewProjMatrix);
                cmd.SetComputeIntParam(m_Compute, DownscaleFactorId, downscale);
                cmd.SetComputeIntParam(m_Compute, EnableHzbId, useHzb ? 1 : 0);
                cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, new Vector4(m_FrameData.ScreenWidth, m_FrameData.ScreenHeight, 0f, 0f));
                cmd.SetComputeVectorParam(m_Compute, AtlasScreenSizeId, new Vector4(atlasWidth, atlasHeight, 0f, 0f));
                cmd.SetComputeVectorParam(m_Compute, HzbSizeId, new Vector4(m_HzbWidth, m_HzbHeight, m_HzbMaxMip, 0f));
                cmd.SetComputeFloatParam(m_Compute, ProjScaleYId, m_FrameData.ProjScaleY);
                cmd.SetComputeFloatParam(m_Compute, NearClipPlaneId, m_NearClipPlane);

                // 2. Transform, Cull, and Software-Rasterize each visible hair model
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

                    int vertexCount = gpuRes.VertexCount;
                    int segmentCount = gpuRes.SegmentCount;
                    if (entry.ActiveStrandLimit > 0 && entry.ActiveStrandLimit < gpuRes.StrandCount)
                    {
                        float ratio = (float)entry.ActiveStrandLimit / Mathf.Max(1, gpuRes.StrandCount);
                        segmentCount = Mathf.Clamp(Mathf.RoundToInt(segmentCount * ratio), 1, gpuRes.SegmentCount);
                    }

                    StrandHairModelScratchSlot slot = m_GpuPool.GetOrCreateSlot(slotIdx, vertexCount, segmentCount);

                    cmd.SetComputeMatrixParam(m_Compute, LocalToWorldId, entry.LocalToWorld);
                    cmd.SetComputeIntParam(m_Compute, VertexCountId, vertexCount);
                    cmd.SetComputeIntParam(m_Compute, SegmentCountId, segmentCount);
                    cmd.SetComputeIntParam(m_Compute, FirstAtlasTileId, alloc.FirstAtlasTile);
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        AtlasTileRectId,
                        new Vector4(alloc.TileOrigin.x, alloc.TileOrigin.y, alloc.TileGridSize.x, alloc.TileGridSize.y));
                    cmd.SetComputeFloatParam(m_Compute, RadiusScaleId, entry.RadiusScale);
                    cmd.SetComputeFloatParam(m_Compute, DepthRangeNearId, alloc.DepthRangeNear);
                    cmd.SetComputeFloatParam(m_Compute, DepthRangeInvSpanId, alloc.DepthRangeInvSpan);

                    // 2a. Transform vertices to clip-space + world-space caches
                    cmd.SetComputeBufferParam(m_Compute, m_TransformVerticesKernel, RestVerticesId, gpuRes.RestVertexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_TransformVerticesKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_TransformVerticesKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.DispatchCompute(m_Compute, m_TransformVerticesKernel, (vertexCount + 63) / 64, 1, 1);

                    // 2b. Reset indirect raster arguments
                    cmd.SetComputeBufferParam(m_Compute, m_ClearCullArgsKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                    cmd.DispatchCompute(m_Compute, m_ClearCullArgsKernel, 1, 1, 1);

                    // 2c. Coarse frustum + HZB segment culling
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, SegmentIndicesId, gpuRes.SegmentIndexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, VisibleSegmentListId, slot.VisibleSegmentListBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                    if (useHzb)
                        cmd.SetComputeTextureParam(m_Compute, m_CullSegmentsKernel, HzbTextureId, hzbHandle);
                    else
                        cmd.SetComputeTextureParam(m_Compute, m_CullSegmentsKernel, HzbTextureId, depthHandle);
                    cmd.DispatchCompute(m_Compute, m_CullSegmentsKernel, (segmentCount + 63) / 64, 1, 1);

                    // 2d. Conservative software rasterization into 3-Layer VisAtlas + Coverage
                    cmd.SetComputeBufferParam(m_Compute, m_RasterizeSegmentsKernel, SegmentIndicesId, gpuRes.SegmentIndexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_RasterizeSegmentsKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_RasterizeSegmentsKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_RasterizeSegmentsKernel, VisibleSegmentListId, slot.VisibleSegmentListBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_RasterizeSegmentsKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_RasterizeSegmentsKernel, VisAtlasId, m_FrameData.VisAtlasBuffer);
                    cmd.SetComputeTextureParam(m_Compute, m_RasterizeSegmentsKernel, SceneDepthId, depthHandle);
                    cmd.DispatchCompute(m_Compute, m_RasterizeSegmentsKernel, slot.IndirectRasterArgsBuffer, 0u);
                }

                m_FrameData.VisibilityRecordedThisFrame = true;
            }
        }

        public override void Dispose()
        {
            m_GpuPool.Dispose();
            m_Compute = null;
            m_ClearAtlasKernel = -1;
            m_TransformVerticesKernel = -1;
            m_ClearCullArgsKernel = -1;
            m_CullSegmentsKernel = -1;
            m_RasterizeSegmentsKernel = -1;
            m_FrameData = null;
        }
    }
}
