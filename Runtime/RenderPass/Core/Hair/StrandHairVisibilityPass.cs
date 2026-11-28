using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Strand-Based Hair Visibility Pass (IJGC SIGGRAPH 2025 architecture):
    /// 1) Allocates 64x64 tiles in the bounded 32-bit 3-Layer + Coverage Visibility Atlas.
    /// 2) Transforms rest vertices to global clip-space + world-space caches.
    /// 3) Performs frustum + conservative farthest-Z occlusion culling with wave-aggregated
    ///    append, tile histogram accumulation, and visible-vertex bitmask marking.
    /// 4) Phase S3: Runs 1024-bin Global Counting Sort + Natural-Order Vertex Compaction +
    ///    Dual LDS Pixel Request Stack software rasterizer (or S2 direct rasterizer when disabled).
    /// Implements IAsyncComputeSupportedPass for async overlap with opaque lighting/GI.
    /// </summary>
    public sealed class StrandHairVisibilityPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const string ClearAtlasKernelName = "KClearAtlasTiles";
        private const string BuildFarthestDepth8x8KernelName = "KBuildFarthestDepth8x8";
        private const string BuildFarthestDepth32x32KernelName = "KBuildFarthestDepth32x32";
        private const string TransformVerticesKernelName = "KTransformVertices";
        private const string ClearCullArgsKernelName = "KClearCullArgs";
        private const string ClearVisibleVertexMaskKernelName = "KClearVisibleVertexMask";
        private const string ClearSortBinsKernelName = "KClearSortBins";
        private const string CullSegmentsKernelName = "KCullSegments";
        private const string PrefixSumSortBinsKernelName = "KPrefixSumSortBins";
        private const string ScatterSortedSegmentsKernelName = "KScatterSortedSegments";
        private const string CompactVisibleVerticesKernelName = "KCompactVisibleVertices";
        private const string RasterizeSegmentsKernelName = "KRasterizeSegments";
        private const string RasterizeSegmentsLdsKernelName = "KRasterizeSegmentsLDS";

        private static readonly int RestVerticesId = Shader.PropertyToID("_HairRestVertices");
        private static readonly int SegmentIndicesId = Shader.PropertyToID("_HairSegmentIndices");
        private static readonly int PostTransformClipId = Shader.PropertyToID("_HairPostTransformClip");
        private static readonly int WorldPosRadiusId = Shader.PropertyToID("_HairWorldPosRadius");
        private static readonly int VisibleSegmentListId = Shader.PropertyToID("_HairVisibleSegmentList");
        private static readonly int SegmentSortBinId = Shader.PropertyToID("_HairSegmentSortBin");
        private static readonly int SortedSegmentListId = Shader.PropertyToID("_HairSortedSegmentList");
        private static readonly int TileHistogramId = Shader.PropertyToID("_HairTileHistogram");
        private static readonly int IndirectRasterArgsId = Shader.PropertyToID("_HairIndirectRasterArgs");
        private static readonly int VisibleVertexMaskId = Shader.PropertyToID("_HairVisibleVertexMask");
        private static readonly int VisibleVertexListId = Shader.PropertyToID("_HairVisibleVertexList");
        private static readonly int IndirectShadeArgsId = Shader.PropertyToID("_HairIndirectShadeArgs");
        private static readonly int VisAtlasId = Shader.PropertyToID("_HairVisAtlas");
        private static readonly int SceneDepthId = Shader.PropertyToID("_HairSceneDepth");
        private static readonly int FarthestDepth8x8Id = Shader.PropertyToID("_HairFarthestDepth8x8");
        private static readonly int FarthestDepth32x32Id = Shader.PropertyToID("_HairFarthestDepth32x32");
        private static readonly int FarthestDepth8x8SrvId = Shader.PropertyToID("_HairFarthestDepth8x8SRV");
        private static readonly int FarthestDepth32x32SrvId = Shader.PropertyToID("_HairFarthestDepth32x32SRV");

        private static readonly int LocalToWorldId = Shader.PropertyToID("_HairLocalToWorld");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_HairViewMatrix");
        private static readonly int ViewProjMatrixId = Shader.PropertyToID("_HairViewProjMatrix");
        private static readonly int VertexCountId = Shader.PropertyToID("_HairVertexCount");
        private static readonly int SegmentCountId = Shader.PropertyToID("_HairSegmentCount");
        private static readonly int AllocatedTileCountId = Shader.PropertyToID("_HairAllocatedTileCount");
        private static readonly int FirstAtlasTileId = Shader.PropertyToID("_HairFirstAtlasTile");
        private static readonly int SortBinCountId = Shader.PropertyToID("_HairSortBinCount");
        private static readonly int UseSortedSegmentsId = Shader.PropertyToID("_HairUseSortedSegments");
        private static readonly int AtlasTileRectId = Shader.PropertyToID("_HairAtlasTileRect");
        private static readonly int DownscaleFactorId = Shader.PropertyToID("_HairDownscaleFactor");
        private static readonly int EnableHzbId = Shader.PropertyToID("_HairEnableHZB");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_HairScreenSize");
        private static readonly int AtlasScreenSizeId = Shader.PropertyToID("_HairAtlasScreenSize");
        private static readonly int DepthTileCountsId = Shader.PropertyToID("_HairDepthTileCounts");
        private static readonly int RadiusScaleId = Shader.PropertyToID("_HairRadiusScale");
        private static readonly int ProjScaleYId = Shader.PropertyToID("_HairProjScaleY");
        private static readonly int DepthRangeNearId = Shader.PropertyToID("_HairDepthRangeNear");
        private static readonly int DepthRangeInvSpanId = Shader.PropertyToID("_HairDepthRangeInvSpan");
        private static readonly int NearClipPlaneId = Shader.PropertyToID("_HairNearClipPlane");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "HZB", Access = AccessFlags.Read)]
        private RenderGraphTexture m_HzbTexture;

        [RenderGraphResource(Name = "HairVisAtlas", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_VisAtlasBuffer;

        private readonly RenderGraphTexture m_LocalHzbTexture;

        private ComputeShader m_Compute;
        private int m_ClearAtlasKernel = -1;
        private int m_BuildFarthestDepth8x8Kernel = -1;
        private int m_BuildFarthestDepth32x32Kernel = -1;
        private int m_TransformVerticesKernel = -1;
        private int m_ClearCullArgsKernel = -1;
        private int m_ClearVisibleVertexMaskKernel = -1;
        private int m_ClearSortBinsKernel = -1;
        private int m_CullSegmentsKernel = -1;
        private int m_PrefixSumSortBinsKernel = -1;
        private int m_ScatterSortedSegmentsKernel = -1;
        private int m_CompactVisibleVerticesKernel = -1;
        private int m_RasterizeSegmentsKernel = -1;
        private int m_RasterizeSegmentsLdsKernel = -1;

        private StrandHairFrameData m_FrameData;
        private StrandHairGpuPool m_GpuPool;
        private Matrix4x4 m_ViewMatrix;
        private Matrix4x4 m_GpuViewProjMatrix;
        private float m_NearClipPlane = 0.05f;
        private GraphicsBuffer m_FarthestDepth8x8Buffer;
        private GraphicsBuffer m_FarthestDepth32x32Buffer;
        private int m_DepthTiles8x8X;
        private int m_DepthTiles8x8Y;
        private int m_DepthTiles32x32X;
        private int m_DepthTiles32x32Y;
        private readonly int[] m_DepthTileCounts = new int[4];

        public StrandHairVisibilityPass()
        {
            profilingSampler = new ProfilingSampler(nameof(StrandHairVisibilityPass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_LocalHzbTexture = RenderGraphTexture.CreateInput("HZB", GraphicsFormat.R32_SFloat);
            m_LocalHzbTexture.desc.Width = 1;
            m_LocalHzbTexture.desc.Height = 1;
            m_HzbTexture = m_LocalHzbTexture;
            m_VisAtlasBuffer = RenderGraphBuffer.CreateStructured("HairVisAtlas", 1, sizeof(uint));
        }

        public override void Create()
        {
            EnsureShaderLoaded();
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null
                && m_ClearAtlasKernel >= 0
                && m_BuildFarthestDepth8x8Kernel >= 0
                && m_BuildFarthestDepth32x32Kernel >= 0
                && m_TransformVerticesKernel >= 0
                && m_ClearCullArgsKernel >= 0
                && m_ClearVisibleVertexMaskKernel >= 0
                && m_ClearSortBinsKernel >= 0
                && m_CullSegmentsKernel >= 0
                && m_PrefixSumSortBinsKernel >= 0
                && m_ScatterSortedSegmentsKernel >= 0
                && m_CompactVisibleVerticesKernel >= 0
                && m_RasterizeSegmentsKernel >= 0
                && m_RasterizeSegmentsLdsKernel >= 0)
            {
                return true;
            }

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.StrandHairVisibilityCompute;
            if (m_Compute == null)
                return false;

            m_ClearAtlasKernel = m_Compute.FindKernel(ClearAtlasKernelName);
            m_BuildFarthestDepth8x8Kernel = m_Compute.FindKernel(BuildFarthestDepth8x8KernelName);
            m_BuildFarthestDepth32x32Kernel = m_Compute.FindKernel(BuildFarthestDepth32x32KernelName);
            m_TransformVerticesKernel = m_Compute.FindKernel(TransformVerticesKernelName);
            m_ClearCullArgsKernel = m_Compute.FindKernel(ClearCullArgsKernelName);
            m_ClearVisibleVertexMaskKernel = m_Compute.FindKernel(ClearVisibleVertexMaskKernelName);
            m_ClearSortBinsKernel = m_Compute.FindKernel(ClearSortBinsKernelName);
            m_CullSegmentsKernel = m_Compute.FindKernel(CullSegmentsKernelName);
            m_PrefixSumSortBinsKernel = m_Compute.FindKernel(PrefixSumSortBinsKernelName);
            m_ScatterSortedSegmentsKernel = m_Compute.FindKernel(ScatterSortedSegmentsKernelName);
            m_CompactVisibleVerticesKernel = m_Compute.FindKernel(CompactVisibleVerticesKernelName);
            m_RasterizeSegmentsKernel = m_Compute.FindKernel(RasterizeSegmentsKernelName);
            m_RasterizeSegmentsLdsKernel = m_Compute.FindKernel(RasterizeSegmentsLdsKernelName);
            return m_RasterizeSegmentsKernel >= 0
                && m_RasterizeSegmentsLdsKernel >= 0
                && m_BuildFarthestDepth8x8Kernel >= 0
                && m_BuildFarthestDepth32x32Kernel >= 0
                && m_ClearVisibleVertexMaskKernel >= 0
                && m_CompactVisibleVerticesKernel >= 0;
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return HairGroomSystem.ActiveCount > 0 && EnsureShaderLoaded();
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_FrameData = frameData.GetOrCreate<StrandHairFrameData>();
            m_FrameData.Reset();
            m_GpuPool = StrandHairGpuPool.Shared;
            m_FrameData.GpuPool = m_GpuPool;

            if (HairGroomSystem.ActiveCount == 0 || !EnsureShaderLoaded())
                return;

            var cameraData = frameData.Get<VividCameraData>();
            if (cameraData == null || cameraData.camera == null)
                return;

            int depthWidth = m_DepthTexture?.desc != null && m_DepthTexture.desc.Width > 0 ? m_DepthTexture.desc.Width : 0;
            int depthHeight = m_DepthTexture?.desc != null && m_DepthTexture.desc.Height > 0 ? m_DepthTexture.desc.Height : 0;
            int width = depthWidth > 0
                ? depthWidth
                : (cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth);
            int height = depthHeight > 0
                ? depthHeight
                : (cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight);
            if (width <= 0 || height <= 0)
                return;

            int collected = HairGroomSystem.CollectActiveEntries(
                m_FrameData.ActiveEntries,
                cameraData.camera.cullingMask);
            if (collected <= 0)
                return;

            m_ViewMatrix = cameraData.GetViewMatrix();
            Matrix4x4 projMatrix = cameraData.GetProjectionMatrix();
            Matrix4x4 gpuProj = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true);
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
                return;

            int atlasHeight = Mathf.Max(1, (height + downscale - 1) / downscale);
            m_FrameData.VisAtlasBuffer = m_GpuPool.EnsureVisAtlasBuffer(StrandHairGpuPool.GlobalMaxAtlasTiles);
            m_FrameData.TotalAllocatedTiles = totalTiles;
            m_FrameData.DownscaleFactor = downscale;
            m_FrameData.ScreenWidth = width;
            m_FrameData.ScreenHeight = height;
            m_FrameData.ProjScaleY = Mathf.Abs(projMatrix.m11) * 0.5f * atlasHeight;
            m_FrameData.HasVisibleHair = true;

            m_DepthTiles8x8X = Mathf.Max(1, (width + 7) >> 3);
            m_DepthTiles8x8Y = Mathf.Max(1, (height + 7) >> 3);
            m_DepthTiles32x32X = Mathf.Max(1, (m_DepthTiles8x8X + 3) >> 2);
            m_DepthTiles32x32Y = Mathf.Max(1, (m_DepthTiles8x8Y + 3) >> 2);
            m_FarthestDepth8x8Buffer = m_GpuPool.EnsureFarthestDepth8x8Buffer(m_DepthTiles8x8X * m_DepthTiles8x8Y);
            m_FarthestDepth32x32Buffer = m_GpuPool.EnsureFarthestDepth32x32Buffer(m_DepthTiles32x32X * m_DepthTiles32x32Y);

            if (!PassRecorder.IsPassTextureImportActive)
                return;

            if (m_FrameData.VisAtlasBuffer != null && m_FrameData.VisAtlasBuffer.IsValid())
            {
                BufferHandle visAtlasHandle = PassRecorder.ImportBufferHandle(m_FrameData.VisAtlasBuffer);
                PassRecorder.BindImportedBuffer(m_VisAtlasBuffer, visAtlasHandle);
            }

            if (m_FarthestDepth8x8Buffer != null && m_FarthestDepth8x8Buffer.IsValid())
                PassRecorder.ImportBufferForPass(this, m_FarthestDepth8x8Buffer, AccessFlags.ReadWrite);
            if (m_FarthestDepth32x32Buffer != null && m_FarthestDepth32x32Buffer.IsValid())
                PassRecorder.ImportBufferForPass(this, m_FarthestDepth32x32Buffer, AccessFlags.ReadWrite);

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
                    PassRecorder.ImportBufferForPass(this, slot.PostTransformClipBuffer, AccessFlags.Write);
                if (slot.WorldPosRadiusBuffer != null && slot.WorldPosRadiusBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.WorldPosRadiusBuffer, AccessFlags.Write);
                if (slot.VisibleVertexMaskBuffer != null && slot.VisibleVertexMaskBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.VisibleVertexMaskBuffer, AccessFlags.Write);
                if (slot.VisibleVertexListBuffer != null && slot.VisibleVertexListBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.VisibleVertexListBuffer, AccessFlags.Write);
                if (slot.IndirectShadeArgsBuffer != null && slot.IndirectShadeArgsBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, slot.IndirectShadeArgsBuffer, AccessFlags.Write);
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (m_FrameData == null
                || !m_FrameData.HasVisibleHair
                || m_FrameData.TotalAllocatedTiles <= 0
                || m_FrameData.VisAtlasBuffer == null
                || !m_FrameData.VisAtlasBuffer.IsValid()
                || m_DepthTexture == null
                || !m_DepthTexture.innerHandle.IsValid()
                || m_GpuPool == null
                || !EnsureShaderLoaded())
            {
                return;
            }

            ComputeCommandBuffer cmd = context.cmd;
            TextureHandle depthHandle = m_DepthTexture.innerHandle;
            int downscale = Mathf.Max(1, m_FrameData.DownscaleFactor);
            float atlasWidth = Mathf.Max(1, (m_FrameData.ScreenWidth + downscale - 1) / downscale);
            float atlasHeight = Mathf.Max(1, (m_FrameData.ScreenHeight + downscale - 1) / downscale);
            bool useFarthestOcclusion = m_FarthestDepth8x8Buffer != null
                && m_FarthestDepth8x8Buffer.IsValid()
                && m_FarthestDepth32x32Buffer != null
                && m_FarthestDepth32x32Buffer.IsValid()
                && m_DepthTiles8x8X > 0
                && m_DepthTiles8x8Y > 0;
            bool useS3 = StrandHairGpuPool.EnablePhaseS3Optimizations;
            bool useTileSort = useS3 && StrandHairGpuPool.EnableTileSorting;
            bool useLdsStack = useS3 && StrandHairGpuPool.EnableLdsRasterStack;
            bool useCompactShade = useS3 && StrandHairGpuPool.EnableCompactShading;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 1. Clear only the allocated tiles of the Visibility Atlas
                cmd.SetComputeIntParam(m_Compute, AllocatedTileCountId, m_FrameData.TotalAllocatedTiles);
                if (m_VisAtlasBuffer != null && m_VisAtlasBuffer.innerHandle.IsValid())
                    cmd.SetComputeBufferParam(m_Compute, m_ClearAtlasKernel, VisAtlasId, m_VisAtlasBuffer.innerHandle);
                else
                    cmd.SetComputeBufferParam(m_Compute, m_ClearAtlasKernel, VisAtlasId, m_FrameData.VisAtlasBuffer);
                int clearGroups = (m_FrameData.TotalAllocatedTiles * StrandHairAtlasContract.TilePixels + 63) / 64;
                cmd.DispatchCompute(m_Compute, m_ClearAtlasKernel, clearGroups, 1, 1);

                // Shared camera uniforms
                cmd.SetComputeMatrixParam(m_Compute, ViewMatrixId, m_ViewMatrix);
                cmd.SetComputeMatrixParam(m_Compute, ViewProjMatrixId, m_GpuViewProjMatrix);
                cmd.SetComputeIntParam(m_Compute, DownscaleFactorId, downscale);
                cmd.SetComputeIntParam(m_Compute, EnableHzbId, useFarthestOcclusion ? 1 : 0);
                cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, new Vector4(m_FrameData.ScreenWidth, m_FrameData.ScreenHeight, 0f, 0f));
                cmd.SetComputeVectorParam(m_Compute, AtlasScreenSizeId, new Vector4(atlasWidth, atlasHeight, 0f, 0f));
                m_DepthTileCounts[0] = m_DepthTiles8x8X;
                m_DepthTileCounts[1] = m_DepthTiles8x8Y;
                m_DepthTileCounts[2] = m_DepthTiles32x32X;
                m_DepthTileCounts[3] = m_DepthTiles32x32Y;
                cmd.SetComputeIntParams(m_Compute, DepthTileCountsId, m_DepthTileCounts);
                cmd.SetComputeFloatParam(m_Compute, ProjScaleYId, m_FrameData.ProjScaleY);
                cmd.SetComputeFloatParam(m_Compute, NearClipPlaneId, m_NearClipPlane);

                // 1b. Build conservative farthest-Z (min-Z in Reversed-Z) occlusion tiles from scene depth
                if (useFarthestOcclusion)
                {
                    cmd.SetComputeTextureParam(m_Compute, m_BuildFarthestDepth8x8Kernel, SceneDepthId, depthHandle);
                    cmd.SetComputeBufferParam(m_Compute, m_BuildFarthestDepth8x8Kernel, FarthestDepth8x8Id, m_FarthestDepth8x8Buffer);
                    cmd.DispatchCompute(m_Compute, m_BuildFarthestDepth8x8Kernel, m_DepthTiles8x8X, m_DepthTiles8x8Y, 1);

                    cmd.SetComputeBufferParam(m_Compute, m_BuildFarthestDepth32x32Kernel, FarthestDepth8x8SrvId, m_FarthestDepth8x8Buffer);
                    cmd.SetComputeBufferParam(m_Compute, m_BuildFarthestDepth32x32Kernel, FarthestDepth32x32Id, m_FarthestDepth32x32Buffer);
                    cmd.DispatchCompute(
                        m_Compute,
                        m_BuildFarthestDepth32x32Kernel,
                        (m_DepthTiles32x32X + 7) >> 3,
                        (m_DepthTiles32x32Y + 7) >> 3,
                        1);
                }

                // 2. Transform, Cull, Sort, Compact, and Software-Rasterize each visible hair model
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
                    int sortBinCount = Mathf.Clamp(alloc.TileCount, 1, StrandHairAtlasContract.MaxSortBins);

                    cmd.SetComputeMatrixParam(m_Compute, LocalToWorldId, entry.LocalToWorld);
                    cmd.SetComputeIntParam(m_Compute, VertexCountId, vertexCount);
                    cmd.SetComputeIntParam(m_Compute, SegmentCountId, segmentCount);
                    cmd.SetComputeIntParam(m_Compute, FirstAtlasTileId, alloc.FirstAtlasTile);
                    cmd.SetComputeIntParam(m_Compute, SortBinCountId, sortBinCount);
                    cmd.SetComputeIntParam(m_Compute, UseSortedSegmentsId, useTileSort ? 1 : 0);
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

                    // 2b. Reset indirect args, visible vertex bitmask, and sort histogram
                    cmd.SetComputeBufferParam(m_Compute, m_ClearCullArgsKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ClearCullArgsKernel, IndirectShadeArgsId, slot.IndirectShadeArgsBuffer);
                    cmd.DispatchCompute(m_Compute, m_ClearCullArgsKernel, 1, 1, 1);

                    int maskUints = (vertexCount + 31) >> 5;
                    cmd.SetComputeBufferParam(m_Compute, m_ClearVisibleVertexMaskKernel, VisibleVertexMaskId, slot.VisibleVertexMaskBuffer);
                    cmd.DispatchCompute(m_Compute, m_ClearVisibleVertexMaskKernel, (maskUints + 63) / 64, 1, 1);

                    cmd.SetComputeBufferParam(m_Compute, m_ClearSortBinsKernel, TileHistogramId, slot.TileHistogramBuffer);
                    cmd.DispatchCompute(m_Compute, m_ClearSortBinsKernel, (StrandHairAtlasContract.TileHistogramBufferUints + 63) / 64, 1, 1);

                    // 2c. Coarse frustum + conservative farthest-Z occlusion segment culling
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, SegmentIndicesId, gpuRes.SegmentIndexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, VisibleSegmentListId, slot.VisibleSegmentListBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, SegmentSortBinId, slot.SegmentSortBinBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, TileHistogramId, slot.TileHistogramBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, VisibleVertexMaskId, slot.VisibleVertexMaskBuffer);
                    if (useFarthestOcclusion)
                    {
                        cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, FarthestDepth8x8SrvId, m_FarthestDepth8x8Buffer);
                        cmd.SetComputeBufferParam(m_Compute, m_CullSegmentsKernel, FarthestDepth32x32SrvId, m_FarthestDepth32x32Buffer);
                    }
                    cmd.DispatchCompute(m_Compute, m_CullSegmentsKernel, (segmentCount + 63) / 64, 1, 1);

                    if (useTileSort)
                    {
                        // 2d-S3. Global 1024-bin Tile Counting Sort (1-group prefix sum + indirect scatter)
                        cmd.SetComputeBufferParam(m_Compute, m_PrefixSumSortBinsKernel, TileHistogramId, slot.TileHistogramBuffer);
                        cmd.DispatchCompute(m_Compute, m_PrefixSumSortBinsKernel, 1, 1, 1);

                        cmd.SetComputeBufferParam(m_Compute, m_ScatterSortedSegmentsKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_ScatterSortedSegmentsKernel, VisibleSegmentListId, slot.VisibleSegmentListBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_ScatterSortedSegmentsKernel, SegmentSortBinId, slot.SegmentSortBinBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_ScatterSortedSegmentsKernel, TileHistogramId, slot.TileHistogramBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_ScatterSortedSegmentsKernel, SortedSegmentListId, slot.SortedSegmentListBuffer);
                        cmd.DispatchCompute(m_Compute, m_ScatterSortedSegmentsKernel, slot.IndirectRasterArgsBuffer, 0u);
                    }

                    if (useCompactShade)
                    {
                        // 2e-S3. Natural-order visible vertex compaction for 100%-active indirect shading
                        cmd.SetComputeBufferParam(m_Compute, m_CompactVisibleVerticesKernel, VisibleVertexMaskId, slot.VisibleVertexMaskBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_CompactVisibleVerticesKernel, VisibleVertexListId, slot.VisibleVertexListBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_CompactVisibleVerticesKernel, IndirectShadeArgsId, slot.IndirectShadeArgsBuffer);
                        cmd.DispatchCompute(m_Compute, m_CompactVisibleVerticesKernel, (maskUints + 63) / 64, 1, 1);
                    }

                    int rasterKernel = useLdsStack ? m_RasterizeSegmentsLdsKernel : m_RasterizeSegmentsKernel;
                    uint rasterArgsOffset = useLdsStack ? 16u : 0u;
                    cmd.SetComputeBufferParam(m_Compute, rasterKernel, SegmentIndicesId, gpuRes.SegmentIndexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, rasterKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, rasterKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.SetComputeBufferParam(m_Compute, rasterKernel, VisibleSegmentListId, slot.VisibleSegmentListBuffer);
                    cmd.SetComputeBufferParam(m_Compute, rasterKernel, SortedSegmentListId, slot.SortedSegmentListBuffer);
                    cmd.SetComputeBufferParam(m_Compute, rasterKernel, IndirectRasterArgsId, slot.IndirectRasterArgsBuffer);
                    if (m_VisAtlasBuffer != null && m_VisAtlasBuffer.innerHandle.IsValid())
                        cmd.SetComputeBufferParam(m_Compute, rasterKernel, VisAtlasId, m_VisAtlasBuffer.innerHandle);
                    else
                        cmd.SetComputeBufferParam(m_Compute, rasterKernel, VisAtlasId, m_FrameData.VisAtlasBuffer);
                    cmd.SetComputeTextureParam(m_Compute, rasterKernel, SceneDepthId, depthHandle);
                    cmd.DispatchCompute(m_Compute, rasterKernel, slot.IndirectRasterArgsBuffer, rasterArgsOffset);
                }

                m_FrameData.VisibilityRecordedThisFrame = true;
            }
        }

        public override void Dispose()
        {
            m_Compute = null;
            m_ClearAtlasKernel = -1;
            m_BuildFarthestDepth8x8Kernel = -1;
            m_BuildFarthestDepth32x32Kernel = -1;
            m_TransformVerticesKernel = -1;
            m_ClearCullArgsKernel = -1;
            m_ClearVisibleVertexMaskKernel = -1;
            m_ClearSortBinsKernel = -1;
            m_CullSegmentsKernel = -1;
            m_PrefixSumSortBinsKernel = -1;
            m_ScatterSortedSegmentsKernel = -1;
            m_CompactVisibleVerticesKernel = -1;
            m_RasterizeSegmentsKernel = -1;
            m_RasterizeSegmentsLdsKernel = -1;
            m_FarthestDepth8x8Buffer = null;
            m_FarthestDepth32x32Buffer = null;
            m_FrameData = null;
            m_GpuPool = null;
        }
    }
}
