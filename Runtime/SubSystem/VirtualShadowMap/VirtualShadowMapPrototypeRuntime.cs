using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.VirtualShadowMap
{
    internal static class VirtualShadowMapPrototypeRuntime
    {
        internal const int PageSize = 128;
        internal const int DepthLayerCount = 16;
        internal const int DefaultPhysicalPageCount = 256;
        internal const int MaxPhysicalPageCount = 1024;
        internal const int ClearWorkArgsOffset = 0;
        internal const int OccupancyWorkArgsOffset = 3 * sizeof(uint);
        internal const int MaxPageRequestsPerMeshlet = 4;
        internal const int RasterPageHeaderSize = 1 + 2 * VirtualShadowMapClipmapLayout.MaxLevels;
        private const int MeshletPageRequestStride = sizeof(uint) * 4;
        private const int StaticInvalidationBoundsStride = sizeof(float) * 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct PageMetadataData
        {
            public uint Flags;
            public uint EncodedPhysicalPage;
            public uint LastRequestedFrame;
            public uint DebugSnapshotFlags;
        }

        private static RTHandle s_StaticPhysicalPage;
        private static RTHandle s_DynamicPhysicalPage;
        private static RTHandle s_RasterDepth;
        private static RTHandle s_UnityRasterDepth;
        internal static readonly VirtualShadowMapProjectionSet Projections = new();
        private static GraphicsBuffer s_RemapPageMetadata;
        private static GraphicsBuffer s_PageTable;
        private static GraphicsBuffer s_PageMetadata;
        private static GraphicsBuffer s_PageRequestFlags;
        private static GraphicsBuffer s_PageReceiverMasks, s_PhysicalReceiverMasks;
        internal static readonly int ReceiverMaskEnabledId = Shader.PropertyToID("_VSMReceiverMaskEnabled");
        internal static readonly int PageReceiverMasksId = Shader.PropertyToID("_VSMPageReceiverMasks");
        internal static readonly int PhysicalReceiverMasksId = Shader.PropertyToID("_VSMPhysicalReceiverMasks");
        private static GraphicsBuffer s_PhysicalPageOwners;
        private static GraphicsBuffer s_AllocatorCounters;
        private static GraphicsBuffer s_AllocationRequests;
        private static GraphicsBuffer s_PageWorkList;
        private static GraphicsBuffer s_PageWorkDispatchArgs;
        private static GraphicsBuffer s_PagePressure;
        private static readonly Unity.Mathematics.uint4[] s_PagePressureUpload = new Unity.Mathematics.uint4[3];
        private static GraphicsBuffer s_MeshletPageRequests;
        private static GraphicsBuffer s_MeshletPageIndirectArgs;
        private static GraphicsBuffer s_MeshletRasterPages;
        private static GraphicsBuffer s_StaticInvalidationBounds;
        private static GraphicsBuffer s_DynamicInvalidationBounds;
        private static NativeList<VividStaticShadowInvalidationBounds> s_DynamicInvalidationScratch;
        private static bool s_HasPreviousUnityBounds;
        private static Bounds s_PreviousUnityBounds;
        private static uint[] s_PageTableUpload;
        private static PageMetadataData[] s_PageMetadataUpload;
        private static uint[] s_PhysicalPageOwnersUpload;
        private static readonly uint[] s_AllocatorCountersUpload = new uint[4];
        private static int s_VirtualResolution;
        private static int s_CascadeCount;
        private static int s_PagesPerAxis;
        private static int s_PhysicalPagesPerRow;
        private static int s_PhysicalPageWidth;
        private static int s_PhysicalPageHeight;
        private static int s_PhysicalPageCapacity;
        private static bool s_ReceiverResolveRecorded;
        private static int s_LastReceiverFeedbackFrame = -1;
        private static ulong s_LastReceiverFeedbackCameraEntityId;
        private static ulong s_PageDebugCameraEntityId;
        private static int s_PageDebugFrameIndex = -1;
        private static VirtualShadowMapPrototypeFrameState s_FrameState;
        private static VirtualShadowMapPrototypeFallbackReason s_LastFallbackReason;
        private static bool s_CacheValid;
        private static bool s_LastFrameUsedCache;
        private static int s_StaticCacheHitCount;
        private static int s_StaticCacheRefreshCount;
        private static int s_DynamicRefreshCount;
        private static VirtualShadowMapPrototypeCacheKey s_CachedKey;
        private static VirtualShadowMapPrototypeCacheKey s_DynamicCachedKey;
        private static bool s_DynamicCacheValid;
        private static bool s_HadUntrackedDynamicCasters;
        private static uint s_DynamicShadowRevision;
        private static bool s_LoggedUnsupportedPlatform;

        internal static RTHandle StaticPhysicalPage => s_StaticPhysicalPage;
        internal static RTHandle DynamicPhysicalPage => s_DynamicPhysicalPage;
        internal static RTHandle RasterDepth => s_RasterDepth;
        internal static RTHandle UnityRasterDepth => s_UnityRasterDepth;
        internal static GraphicsBuffer RemapPageMetadata => s_RemapPageMetadata;
        internal static GraphicsBuffer PageTable => s_PageTable;
        internal static GraphicsBuffer PageMetadata => s_PageMetadata;
        internal static GraphicsBuffer PageRequestFlags => s_PageRequestFlags;
        internal static GraphicsBuffer PageReceiverMasks => s_PageReceiverMasks;
        internal static GraphicsBuffer PhysicalReceiverMasks => s_PhysicalReceiverMasks;
        internal static GraphicsBuffer PhysicalPageOwners => s_PhysicalPageOwners;
        internal static GraphicsBuffer AllocatorCounters => s_AllocatorCounters;
        internal static GraphicsBuffer AllocationRequests => s_AllocationRequests;
        internal static GraphicsBuffer PageWorkList => s_PageWorkList;
        internal static GraphicsBuffer PageWorkDispatchArgs => s_PageWorkDispatchArgs;
        internal static GraphicsBuffer PagePressure => s_PagePressure;
        internal static GraphicsBuffer MeshletPageRequests =>
            s_MeshletPageRequests;
        internal static GraphicsBuffer MeshletPageIndirectArgs =>
            s_MeshletPageIndirectArgs;
        internal static GraphicsBuffer MeshletRasterPages => s_MeshletRasterPages;
        internal static GraphicsBuffer StaticInvalidationBounds =>
            s_StaticInvalidationBounds;
        internal static GraphicsBuffer DynamicInvalidationBounds => s_DynamicInvalidationBounds;
        internal static uint DynamicShadowRevision => s_DynamicShadowRevision;
        internal static uint[] PageTableUpload => s_PageTableUpload;
        internal static int PageTableEntryCount => s_PageTableUpload?.Length ?? 0;
        internal static int VirtualResolution => s_VirtualResolution;
        internal static int PagesPerAxis => s_PagesPerAxis;
        internal static int PhysicalPagesPerRow => s_PhysicalPagesPerRow;
        internal static int PhysicalPageCapacity => s_PhysicalPageCapacity;
        internal static bool HasReceiverFeedback =>
            s_LastReceiverFeedbackCameraEntityId != 0ul
            && s_LastReceiverFeedbackFrame >= 0;
        internal static bool HasPageRequestResources => s_VirtualResolution > 0
            && s_CascadeCount > 0
            && PageTableEntryCount == s_PagesPerAxis
                * s_PagesPerAxis
                * s_CascadeCount
            && s_PageTable?.IsValid() == true
            && s_PageMetadata?.IsValid() == true
            && s_PageRequestFlags?.IsValid() == true
            && s_PageRequestFlags.count == PageTableEntryCount
            && s_PageReceiverMasks?.IsValid() == true && s_PageReceiverMasks.count == PageTableEntryCount
            && s_PhysicalReceiverMasks?.IsValid() == true
            && s_PageTable.count == PageTableEntryCount
            && s_PageMetadata.count == PageTableEntryCount;
        internal static VirtualShadowMapPrototypeFrameState FrameState => s_FrameState;
        internal static VirtualShadowMapPrototypeFallbackReason LastFallbackReason =>
            s_LastFallbackReason;
        internal static bool IsFramePrepared => s_FrameState ==
                VirtualShadowMapPrototypeFrameState.Prepared
            || s_FrameState == VirtualShadowMapPrototypeFrameState.Refreshing
            || s_FrameState == VirtualShadowMapPrototypeFrameState.Cached
            || s_FrameState == VirtualShadowMapPrototypeFrameState.Active;
        internal static bool IsFrameActive =>
            s_FrameState == VirtualShadowMapPrototypeFrameState.Active;
        internal static bool IsCacheValid => s_CacheValid;
        internal static bool LastFrameUsedCache => s_LastFrameUsedCache;
        internal static int StaticCacheHitCount => s_StaticCacheHitCount;
        internal static int StaticCacheRefreshCount => s_StaticCacheRefreshCount;
        internal static int DynamicRefreshCount => s_DynamicRefreshCount;

        internal static bool EnsurePhysicalPageForBinding()
        {
            Projections.EnsureCapacity(1);
            EnsureAllocationResources(s_PageTable != null && s_PageTable.IsValid() ? s_PageTable.count : 1);
            if (s_PageTable == null || !s_PageTable.IsValid())
            {
                s_PageTable?.Dispose();
                s_PageTable = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    sizeof(uint));
                s_PageTable.name = "VSMPrototypePageTable";
                s_PageTableUpload = new uint[1];
                s_PageTable.SetData(s_PageTableUpload);
                InvalidateCache();
            }

            if (s_PageMetadata == null || !s_PageMetadata.IsValid())
            {
                s_PageMetadata?.Dispose();
                s_PageMetadata = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    Marshal.SizeOf<PageMetadataData>());
                s_PageMetadata.name = "VSMPrototypePageMetadata";
                s_PageMetadataUpload = new PageMetadataData[1];
                s_PageMetadata.SetData(s_PageMetadataUpload);
                InvalidateCache();
            }

            if (s_PhysicalPageOwners == null
                || !s_PhysicalPageOwners.IsValid())
            {
                s_PhysicalPageOwners?.Dispose();
                s_PhysicalPageOwners = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    sizeof(uint));
                s_PhysicalPageOwners.name = "VSMPrototypePhysicalPageOwners";
                s_PhysicalPageOwnersUpload = new uint[1];
                s_PhysicalPageOwners.SetData(s_PhysicalPageOwnersUpload);
                InvalidateCache();
            }

            if (s_AllocatorCounters == null || !s_AllocatorCounters.IsValid())
            {
                s_AllocatorCounters?.Dispose();
                s_AllocatorCounters = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    s_AllocatorCountersUpload.Length,
                    sizeof(uint));
                s_AllocatorCounters.name = "VSMPrototypeAllocatorCounters";
                Array.Clear(
                    s_AllocatorCountersUpload,
                    0,
                    s_AllocatorCountersUpload.Length);
                s_AllocatorCounters.SetData(s_AllocatorCountersUpload);
                InvalidateCache();
            }

            bool supportsFormat = IsPhysicalPageFormatSupported();
            if (!supportsFormat)
                return false;

            if (s_StaticPhysicalPage == null || s_StaticPhysicalPage.rt == null)
            {
                s_StaticPhysicalPage?.Release();
                s_StaticPhysicalPage = AllocatePhysicalPage(
                    PageSize,
                    PageSize,
                    "VSMPrototypeStaticPhysicalPage");
                InvalidateCache();
            }

            if (s_DynamicPhysicalPage == null || s_DynamicPhysicalPage.rt == null)
            {
                s_DynamicPhysicalPage?.Release();
                s_DynamicPhysicalPage = AllocatePhysicalPage(
                    PageSize,
                    PageSize,
                    "VSMPrototypeDynamicPhysicalPage");
                InvalidateCache();
            }

            return s_StaticPhysicalPage?.rt != null
                && s_DynamicPhysicalPage?.rt != null
                && s_PageTable?.IsValid() == true
                && s_PageMetadata?.IsValid() == true
                && s_PhysicalPageOwners?.IsValid() == true
                && s_AllocatorCounters?.IsValid() == true;
        }

        private static void EnsureAllocationResources(int pageCount)
        {
            if (s_PageRequestFlags == null || !s_PageRequestFlags.IsValid() || s_PageRequestFlags.count != pageCount)
            {
                s_PageRequestFlags?.Dispose();
                s_PageRequestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageCount, sizeof(uint))
                { name = "VSMPageRequestFlags" };
                s_PageRequestFlags.SetData(new uint[pageCount]);
            }
            EnsureReceiverMaskBuffer(ref s_PageReceiverMasks, pageCount, "VSMPageReceiverMasks");
            EnsureReceiverMaskBuffer(ref s_PhysicalReceiverMasks, Mathf.Max(s_PhysicalPageCapacity, 1), "VSMPhysicalReceiverMasks");
            int words = CoreUtils.DivRoundUp(pageCount, 32);
            if (s_AllocationRequests == null || !s_AllocationRequests.IsValid() || s_AllocationRequests.count != words)
            {
                s_AllocationRequests?.Dispose();
                s_AllocationRequests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, words, sizeof(uint))
                { name = "VSMAllocationRequests" };
            }
            if (s_PagePressure == null || !s_PagePressure.IsValid())
            {
                s_PagePressure?.Dispose();
                s_PagePressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, s_PagePressureUpload.Length, sizeof(uint) * 4)
                { name = "VSMPagePressure" };
                s_PagePressure.SetData(s_PagePressureUpload);
            }
        }

        private static void EnsureReceiverMaskBuffer(ref GraphicsBuffer buffer, int count, string name)
        {
            if (buffer != null && buffer.IsValid() && buffer.count == count) return;
            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, count, sizeof(uint) * 2)
            { name = name };
            buffer.SetData(new Unity.Mathematics.uint2[count]);
        }

        internal static bool EnsureMeshletPageRequestCapacity(
            int sourceRequestCapacity)
        {
            if (sourceRequestCapacity <= 0)
                return false;

            long requiredRequestCapacity = (long)sourceRequestCapacity
                * MaxPageRequestsPerMeshlet;
            if (requiredRequestCapacity > int.MaxValue
                || requiredRequestCapacity
                    + (long)sourceRequestCapacity * MaxPhysicalPageCount > uint.MaxValue)
                return false;

            int requestCapacity = (int)requiredRequestCapacity;
            GraphicsBuffer.Target requestTarget =
                GraphicsBuffer.Target.Structured;
            if (s_MeshletPageRequests == null
                || !s_MeshletPageRequests.IsValid()
                || s_MeshletPageRequests.count < requestCapacity
                || s_MeshletPageRequests.stride != MeshletPageRequestStride
                || s_MeshletPageRequests.target != requestTarget)
            {
                s_MeshletPageRequests?.Dispose();
                s_MeshletPageRequests = new GraphicsBuffer(
                    requestTarget,
                    requestCapacity,
                    MeshletPageRequestStride)
                {
                    name = "VSMPrototypeMeshletPageRequests",
                };
            }

            int indirectArgsWordCount = (int)VividRendererListID.Count
                * VividGPUDrivenCullingBuffers.IndirectDrawArgsWordCount * 2;
            GraphicsBuffer.Target indirectArgsTarget =
                GraphicsBuffer.Target.Raw
                | GraphicsBuffer.Target.IndirectArguments;
            if (s_MeshletPageIndirectArgs == null
                || !s_MeshletPageIndirectArgs.IsValid()
                || s_MeshletPageIndirectArgs.count != indirectArgsWordCount
                || s_MeshletPageIndirectArgs.stride != sizeof(uint)
                || s_MeshletPageIndirectArgs.target != indirectArgsTarget)
            {
                s_MeshletPageIndirectArgs?.Dispose();
                s_MeshletPageIndirectArgs = new GraphicsBuffer(
                    indirectArgsTarget,
                    indirectArgsWordCount,
                    sizeof(uint))
                {
                    name = "VSMPrototypeMeshletPageIndirectArgs",
                };
            }

            if (s_MeshletRasterPages == null || !s_MeshletRasterPages.IsValid())
            {
                s_MeshletRasterPages?.Dispose();
                s_MeshletRasterPages = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    MaxPhysicalPageCount + RasterPageHeaderSize,
                    sizeof(uint))
                {
                    name = "VSMPrototypeMeshletRasterPages",
                };
            }

            return s_MeshletPageRequests?.IsValid() == true
                && s_MeshletPageIndirectArgs?.IsValid() == true
                && s_MeshletRasterPages?.IsValid() == true;
        }

        internal static bool UploadStaticInvalidationBounds(
            NativeArray<VividStaticShadowInvalidationBounds> invalidationBounds)
            => UploadInvalidationBounds(ref s_StaticInvalidationBounds, invalidationBounds, "VSMPrototypeStaticInvalidationBounds");

        internal static bool UploadDynamicInvalidationBounds(NativeArray<VividStaticShadowInvalidationBounds> invalidationBounds)
            => UploadInvalidationBounds(ref s_DynamicInvalidationBounds, invalidationBounds, "VSMPrototypeDynamicInvalidationBounds");

        internal static NativeArray<VividStaticShadowInvalidationBounds> BuildDynamicInvalidationBounds(
            NativeArray<VividStaticShadowInvalidationBounds> meshletBounds, bool hasUnityCasters, Bounds unityBounds)
        {
            // Unity casters still redraw every frame within their conservative
            // coverage. Keep the last COMMITTED coverage to erase movers/removals,
            // including after a skipped or failed shadow pass.
            if (!hasUnityCasters && !s_HasPreviousUnityBounds)
                return meshletBounds;
            if (!s_DynamicInvalidationScratch.IsCreated)
                s_DynamicInvalidationScratch = new NativeList<VividStaticShadowInvalidationBounds>(
                    Mathf.Max(2, meshletBounds.Length + 2), Allocator.Persistent);
            s_DynamicInvalidationScratch.Clear();
            if (meshletBounds.IsCreated)
                s_DynamicInvalidationScratch.AddRange(meshletBounds);
            if (s_HasPreviousUnityBounds)
                AppendUnityInvalidationBounds(s_PreviousUnityBounds);
            if (hasUnityCasters && (!s_HasPreviousUnityBounds || !unityBounds.Equals(s_PreviousUnityBounds)))
                AppendUnityInvalidationBounds(unityBounds);
            return s_DynamicInvalidationScratch.AsArray();
        }

        private static void AppendUnityInvalidationBounds(Bounds bounds)
        {
            s_DynamicInvalidationScratch.Add(new VividStaticShadowInvalidationBounds
            {
                BoundsMin = new Unity.Mathematics.float4(bounds.min, 0),
                BoundsMax = new Unity.Mathematics.float4(bounds.max, 0),
            });
        }

        internal static void CommitDynamicUnityBounds(bool hasBoundedUnityCasters, Bounds bounds)
        {
            s_HasPreviousUnityBounds = hasBoundedUnityCasters;
            s_PreviousUnityBounds = hasBoundedUnityCasters ? bounds : default;
        }

        private static bool UploadInvalidationBounds(ref GraphicsBuffer buffer,
            NativeArray<VividStaticShadowInvalidationBounds> invalidationBounds, string name)
        {
            if (!invalidationBounds.IsCreated || invalidationBounds.Length <= 0)
                return false;

            if (buffer == null
                || !buffer.IsValid()
                || buffer.count < invalidationBounds.Length
                || buffer.stride != StaticInvalidationBoundsStride
                || buffer.target != GraphicsBuffer.Target.Structured)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    invalidationBounds.Length,
                    StaticInvalidationBoundsStride)
                {
                    name = name,
                };
            }

            buffer.SetData(
                invalidationBounds,
                0,
                0,
                invalidationBounds.Length);
            return buffer.IsValid();
        }

        internal static bool EnsureResources(int virtualResolution, int cascadeCount,
            int pageBudget = DefaultPhysicalPageCount)
        {
            if (!IsSupportedOnCurrentPlatform())
            {
                if (!s_LoggedUnsupportedPlatform)
                {
                    Debug.LogWarning(
                        "[VividRP] Virtual Shadow Map prototype requires DX12 or Vulkan, reverse-Z, compute shaders, and R32_UInt render/load-store support. Falling back to CSM.");
                    s_LoggedUnsupportedPlatform = true;
                }

                return false;
            }

            int resolvedResolution = VirtualShadowMapProjectionSet.ResolveResolution(virtualResolution, PageSize);
            int resolvedCascadeCount = Mathf.Max(1, cascadeCount);
            int unityRasterSize = Mathf.Min(resolvedResolution, VirtualShadowMapProjectionSet.UnityRasterTileSize);
            int pagesPerAxis = CalculatePagesPerAxis(resolvedResolution);
            int pageTableEntryCount = pagesPerAxis
                * pagesPerAxis
                * resolvedCascadeCount;
            int physicalPageCapacity = CalculatePhysicalPageCapacity(
                pagesPerAxis,
                resolvedCascadeCount, pageBudget);
            int physicalPagesPerRow = Mathf.CeilToInt(
                Mathf.Sqrt(physicalPageCapacity));
            int physicalPageRows = CoreUtils.DivRoundUp(
                physicalPageCapacity,
                physicalPagesPerRow);
            int physicalPageWidth = physicalPagesPerRow * PageSize;
            int physicalPageHeight = physicalPageRows * PageSize;
            if (physicalPageWidth > SystemInfo.maxTextureSize
                || physicalPageHeight > SystemInfo.maxTextureSize)
            {
                if (!s_LoggedUnsupportedPlatform)
                {
                    Debug.LogWarning(
                        $"[VividRP] Virtual Shadow Map prototype requires a {physicalPageWidth}x{physicalPageHeight} physical pool, exceeding maxTextureSize {SystemInfo.maxTextureSize}. Falling back to CSM.");
                    s_LoggedUnsupportedPlatform = true;
                }

                return false;
            }

            s_LoggedUnsupportedPlatform = false;
            bool configurationMatches = s_StaticPhysicalPage != null
                && s_StaticPhysicalPage.rt != null
                && s_StaticPhysicalPage.rt.width == physicalPageWidth
                && s_StaticPhysicalPage.rt.height == physicalPageHeight
                && s_StaticPhysicalPage.rt.volumeDepth == DepthLayerCount
                && s_DynamicPhysicalPage != null
                && s_DynamicPhysicalPage.rt != null
                && s_DynamicPhysicalPage.rt.width == physicalPageWidth
                && s_DynamicPhysicalPage.rt.height == physicalPageHeight
                && s_DynamicPhysicalPage.rt.volumeDepth == DepthLayerCount
                && s_RasterDepth != null
                && s_RasterDepth.rt != null
                && s_RasterDepth.rt.width == PageSize
                && s_RasterDepth.rt.height == PageSize
                && s_RasterDepth.rt.volumeDepth == physicalPageCapacity
                && s_UnityRasterDepth != null
                && s_UnityRasterDepth.rt != null
                && s_UnityRasterDepth.rt.width == unityRasterSize
                && s_PageTable != null
                && s_PageTable.IsValid()
                && s_PageTable.count == pageTableEntryCount
                && s_RemapPageMetadata != null && s_RemapPageMetadata.IsValid()
                && s_RemapPageMetadata.count == physicalPageCapacity
                && s_PageTableUpload?.Length == pageTableEntryCount
                && s_PageMetadata != null
                && s_PageMetadata.IsValid()
                && s_PageMetadata.count == pageTableEntryCount
                && s_PageMetadataUpload?.Length == pageTableEntryCount
                && s_PageRequestFlags != null && s_PageRequestFlags.IsValid()
                && s_PageRequestFlags.count == pageTableEntryCount
                && s_PageReceiverMasks != null && s_PageReceiverMasks.IsValid() && s_PageReceiverMasks.count == pageTableEntryCount
                && s_PhysicalReceiverMasks != null && s_PhysicalReceiverMasks.IsValid() && s_PhysicalReceiverMasks.count == physicalPageCapacity
                && s_PhysicalPageOwners != null
                && s_PhysicalPageOwners.IsValid()
                && s_PhysicalPageOwners.count == physicalPageCapacity
                && s_PhysicalPageOwnersUpload?.Length == physicalPageCapacity
                && s_AllocatorCounters != null
                && s_AllocatorCounters.IsValid()
                && s_AllocatorCounters.count == s_AllocatorCountersUpload.Length
                && s_AllocationRequests != null && s_AllocationRequests.IsValid()
                && s_AllocationRequests.count == CoreUtils.DivRoundUp(pageTableEntryCount, 32)
                && s_PageWorkList != null && s_PageWorkList.IsValid()
                && s_PageWorkList.count == physicalPageCapacity * 2
                && s_PageWorkDispatchArgs != null && s_PageWorkDispatchArgs.IsValid()
                && s_PagePressure != null && s_PagePressure.IsValid()
                && s_VirtualResolution == resolvedResolution
                && s_CascadeCount == resolvedCascadeCount
                && s_PagesPerAxis == pagesPerAxis
                && s_PhysicalPagesPerRow == physicalPagesPerRow
                && s_PhysicalPageCapacity == physicalPageCapacity
                && s_PhysicalPageWidth == physicalPageWidth
                && s_PhysicalPageHeight == physicalPageHeight;
            if (configurationMatches)
                return true;

            // A budget resize keeps the controller and its producer identity so
            // recovery starts from the established density, not a fine-level spike.
            ReleaseAllocatedResources(preservePressure:
                s_VirtualResolution == resolvedResolution && s_CascadeCount == resolvedCascadeCount);

            s_VirtualResolution = resolvedResolution;
            s_CascadeCount = resolvedCascadeCount;
            s_PagesPerAxis = pagesPerAxis;
            s_PhysicalPagesPerRow = physicalPagesPerRow;
            s_PhysicalPageCapacity = physicalPageCapacity;
            s_PhysicalPageWidth = physicalPageWidth;
            s_PhysicalPageHeight = physicalPageHeight;

            s_StaticPhysicalPage = AllocatePhysicalPage(
                physicalPageWidth,
                physicalPageHeight,
                "VSMPrototypeStaticPhysicalPage");
            s_DynamicPhysicalPage = AllocatePhysicalPage(
                physicalPageWidth,
                physicalPageHeight,
                "VSMPrototypeDynamicPhysicalPage");

            s_UnityRasterDepth = RTHandles.Alloc(unityRasterSize, unityRasterSize,
                depthBufferBits: DepthBits.Depth32, colorFormat: GraphicsFormat.None,
                filterMode: FilterMode.Point, isShadowMap: true,
                name: "VSMUnityRasterDepth");
            s_RasterDepth = RTHandles.Alloc(
                PageSize,
                PageSize,
                slices: physicalPageCapacity,
                depthBufferBits: DepthBits.Depth32,
                colorFormat: GraphicsFormat.None,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                dimension: TextureDimension.Tex2DArray,
                enableRandomWrite: false,
                useMipMap: false,
                autoGenerateMips: false,
                isShadowMap: true,
                anisoLevel: 1,
                mipMapBias: 0.0f,
                msaaSamples: MSAASamples.None,
                bindTextureMS: false,
                useDynamicScale: false,
                useDynamicScaleExplicit: false,
                name: "VSMPrototypeRasterDepth");

            s_PageTableUpload = BuildUnmappedPageTable(
                pagesPerAxis,
                resolvedCascadeCount);
            s_PageTable = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource,
                s_PageTableUpload.Length,
                sizeof(uint));
            s_PageTable.name = "VSMPrototypePageTable";
            s_PageTable.SetData(s_PageTableUpload);

            s_PageMetadataUpload = new PageMetadataData[pageTableEntryCount];
            s_PageMetadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource,
                pageTableEntryCount,
                Marshal.SizeOf<PageMetadataData>());
            s_PageMetadata.name = "VSMPrototypePageMetadata";
            s_PageMetadata.SetData(s_PageMetadataUpload);
            s_RemapPageMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, physicalPageCapacity, 16);
            s_RemapPageMetadata.name = "VSMRemapPageMetadata";

            s_PhysicalPageOwnersUpload = new uint[physicalPageCapacity];
            s_PhysicalPageOwners = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                physicalPageCapacity,
                sizeof(uint));
            s_PhysicalPageOwners.name = "VSMPrototypePhysicalPageOwners";
            s_PhysicalPageOwners.SetData(s_PhysicalPageOwnersUpload);

            Array.Clear(
                s_AllocatorCountersUpload,
                0,
                s_AllocatorCountersUpload.Length);
            s_AllocatorCounters = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                s_AllocatorCountersUpload.Length,
                sizeof(uint));
            s_AllocatorCounters.name = "VSMPrototypeAllocatorCounters";
            s_AllocatorCounters.SetData(s_AllocatorCountersUpload);
            EnsureAllocationResources(pageTableEntryCount);
            s_PageWorkList = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                physicalPageCapacity * 2, sizeof(uint)) { name = "VSMPageWorkList" };
            s_PageWorkDispatchArgs = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                6, sizeof(uint)) { name = "VSMPageWorkDispatchArgs" };
            return s_StaticPhysicalPage != null
                && s_StaticPhysicalPage.rt != null
                && s_DynamicPhysicalPage != null
                && s_DynamicPhysicalPage.rt != null
                && s_RasterDepth != null
                && s_RasterDepth.rt != null
                && s_PageTable != null
                && s_PageTable.IsValid()
                && s_PageMetadata != null
                && s_PageMetadata.IsValid()
                && s_PhysicalPageOwners != null
                && s_PhysicalPageOwners.IsValid()
                && s_AllocatorCounters != null
                && s_AllocatorCounters.IsValid();
        }

        internal static bool IsSupported(
            GraphicsDeviceType deviceType,
            bool usesReversedZBuffer,
            bool supportsComputeShaders,
            bool supportsR32UIntRenderAndLoadStore)
        {
            bool supportedDevice = deviceType == GraphicsDeviceType.Direct3D12
                || deviceType == GraphicsDeviceType.Vulkan;
            return supportedDevice
                && usesReversedZBuffer
                && supportsComputeShaders
                && supportsR32UIntRenderAndLoadStore;
        }

        internal static void ReleaseResources()
        {
            Projections.Dispose();
            ReleaseAllocatedResources();
            s_VirtualResolution = 0;
            s_CascadeCount = 0;
            s_PagesPerAxis = 0;
            s_PhysicalPagesPerRow = 0;
            s_PhysicalPageCapacity = 0;
            s_PhysicalPageWidth = 0;
            s_PhysicalPageHeight = 0;
            BeginFrame();
            s_StaticCacheHitCount = 0;
            s_StaticCacheRefreshCount = 0;
            s_DynamicRefreshCount = 0;
        }

        internal static void BeginFrame()
        {
            s_ReceiverResolveRecorded = false;
            Projections.Reset();
            s_PageDebugCameraEntityId = 0ul;
            s_PageDebugFrameIndex = -1;
            s_FrameState = VirtualShadowMapPrototypeFrameState.Disabled;
            s_LastFallbackReason = VirtualShadowMapPrototypeFallbackReason.None;
            s_LastFrameUsedCache = false;
        }

        internal static void MarkPrepared()
        {
            s_FrameState = VirtualShadowMapPrototypeFrameState.Prepared;
            s_LastFallbackReason = VirtualShadowMapPrototypeFallbackReason.None;
            s_LastFrameUsedCache = false;
        }

        internal static void MarkReady(bool requiresStaticRefresh)
        {
            s_FrameState = requiresStaticRefresh
                ? VirtualShadowMapPrototypeFrameState.Refreshing
                : VirtualShadowMapPrototypeFrameState.Cached;
            s_LastFallbackReason = VirtualShadowMapPrototypeFallbackReason.None;
            s_LastFrameUsedCache = false;
        }

        internal static void MarkActive()
        {
            s_FrameState = VirtualShadowMapPrototypeFrameState.Active;
            s_LastFallbackReason = VirtualShadowMapPrototypeFallbackReason.None;
        }

        internal static void MarkFallback(
            VirtualShadowMapPrototypeFallbackReason fallbackReason)
        {
            s_FrameState = VirtualShadowMapPrototypeFrameState.Fallback;
            s_LastFallbackReason = fallbackReason;
            s_LastFrameUsedCache = false;
        }

        internal static bool RequiresStaticCacheRefresh(
            in VirtualShadowMapPrototypeCacheKey key)
        {
            return RequiresFullStaticCacheRefresh(key)
                || s_CachedKey.StaticShadowRevision != key.StaticShadowRevision;
        }

        internal static bool RequiresFullStaticCacheRefresh(
            in VirtualShadowMapPrototypeCacheKey key)
        {
            return !key.IsValid || !s_CacheValid || !s_CachedKey.Equals(key);
        }

        internal static bool TryUseCachedStaticPages(
            in VirtualShadowMapPrototypeCacheKey key)
        {
            if (RequiresStaticCacheRefresh(key))
                return false;

            s_LastFrameUsedCache = true;
            s_StaticCacheHitCount++;
            return true;
        }

        internal static void CommitStaticCache(
            in VirtualShadowMapPrototypeCacheKey key)
        {
            if (!key.IsValid)
            {
                InvalidateCache();
                return;
            }

            s_CachedKey = key;
            s_CacheValid = true;
            s_LastFrameUsedCache = false;
            s_StaticCacheRefreshCount++;
        }

        internal static bool RequiresFullDynamicCacheRefresh(in VirtualShadowMapPrototypeCacheKey key, bool untrackedCasters)
            => !s_DynamicCacheValid || !key.IsValid || !s_DynamicCachedKey.Equals(key)
                || untrackedCasters || s_HadUntrackedDynamicCasters;

        internal static void CommitDynamicCache(in VirtualShadowMapPrototypeCacheKey key, uint revision, bool untrackedCasters)
        {
            s_DynamicCachedKey = key;
            s_DynamicCacheValid = key.IsValid;
            s_DynamicShadowRevision = revision;
            s_HadUntrackedDynamicCasters = untrackedCasters;
        }

        internal static void MarkDynamicPoolRefreshed()
        {
            s_DynamicRefreshCount++;
        }

        internal static void MarkPageDebugSnapshot(ulong cameraEntityId, int frameIndex)
        {
            s_PageDebugCameraEntityId = cameraEntityId;
            s_PageDebugFrameIndex = frameIndex;
        }

        internal static bool HasPageDebugSnapshot(ulong cameraEntityId, int frameIndex)
        {
            return IsFrameActive && cameraEntityId != 0ul && frameIndex >= 0
                && cameraEntityId == s_PageDebugCameraEntityId && frameIndex == s_PageDebugFrameIndex;
        }

        internal static bool HasReceiverDebugSnapshot(ulong cameraEntityId, int frameIndex)
        {
            return s_ReceiverResolveRecorded && HasPageDebugSnapshot(cameraEntityId, frameIndex)
                && cameraEntityId == s_LastReceiverFeedbackCameraEntityId
                && frameIndex == s_LastReceiverFeedbackFrame;
        }

        internal static bool HasReceiverFeedbackForFrame(ulong cameraEntityId, int frameIndex)
        {
            return cameraEntityId != 0ul
                && cameraEntityId == s_LastReceiverFeedbackCameraEntityId
                && frameIndex >= 0
                && s_LastReceiverFeedbackFrame == frameIndex;
        }

        internal static bool RequiresReceiverFeedbackReset(ulong cameraEntityId, int frameIndex)
        {
            // The metadata is shared. Drop requests and their timestamps when a
            // different producer takes over, or a frame is repeated/rewound.
            return !HasReceiverFeedback
                || cameraEntityId != s_LastReceiverFeedbackCameraEntityId
                || frameIndex <= s_LastReceiverFeedbackFrame;
        }

        internal static void MarkReceiverResolveProduced(ulong cameraEntityId, int frameIndex)
        {
            s_ReceiverResolveRecorded = HasReceiverFeedbackForFrame(cameraEntityId, frameIndex);
        }

        internal static void MarkReceiverFeedbackProduced(ulong cameraEntityId, int frameIndex)
        {
            s_ReceiverResolveRecorded = false;
            bool valid = cameraEntityId != 0ul && frameIndex >= 0;
            s_LastReceiverFeedbackCameraEntityId = valid ? cameraEntityId : 0ul;
            s_LastReceiverFeedbackFrame = valid ? frameIndex : -1;
        }

        internal static void InvalidateCache()
        {
            s_HasPreviousUnityBounds = false;
            s_PreviousUnityBounds = default;
            s_DynamicCacheValid = false;
            s_DynamicCachedKey = default;
            s_DynamicShadowRevision = 0u;
            s_HadUntrackedDynamicCasters = false;
            s_CachedKey = default;
            s_CacheValid = false;
            s_LastFrameUsedCache = false;
        }

        internal static int CalculatePagesPerAxis(int virtualResolution)
        {
            return CoreUtils.DivRoundUp(
                Mathf.Max(1, virtualResolution),
                PageSize);
        }

        internal static int CalculatePhysicalPageCapacity(
            int pagesPerAxis,
            int cascadeCount, int pageBudget = DefaultPhysicalPageCount)
        {
            int resolvedPagesPerAxis = Mathf.Max(1, pagesPerAxis);
            int resolvedCascadeCount = Mathf.Max(1, cascadeCount);
            return Mathf.Min(
                resolvedPagesPerAxis
                    * resolvedPagesPerAxis
                    * resolvedCascadeCount,
                Mathf.Clamp(pageBudget, 1, MaxPhysicalPageCount));
        }

        internal static uint[] BuildUnmappedPageTable(
            int pagesPerAxis,
            int cascadeCount)
        {
            int resolvedPagesPerAxis = Mathf.Max(1, pagesPerAxis);
            int resolvedCascadeCount = Mathf.Max(1, cascadeCount);
            return new uint[
                resolvedPagesPerAxis
                * resolvedPagesPerAxis
                * resolvedCascadeCount];
        }

        private static void ReleaseAllocatedResources(bool preservePressure = false)
        {
            if (s_DynamicInvalidationScratch.IsCreated)
                s_DynamicInvalidationScratch.Dispose();
            Projections.InvalidateLayout();
            s_RemapPageMetadata?.Dispose();
            s_RemapPageMetadata = null;
            InvalidateCache();
            s_StaticPhysicalPage?.Release();
            s_StaticPhysicalPage = null;
            s_DynamicPhysicalPage?.Release();
            s_DynamicPhysicalPage = null;
            s_RasterDepth?.Release();
            s_RasterDepth = null;
            s_UnityRasterDepth?.Release();
            s_UnityRasterDepth = null;
            s_PageTable?.Dispose();
            s_PageTable = null;
            s_PageMetadata?.Dispose();
            s_PageMetadata = null;
            s_PageRequestFlags?.Dispose();
            s_PageRequestFlags = null;
            s_PageReceiverMasks?.Dispose(); s_PageReceiverMasks = null;
            s_PhysicalReceiverMasks?.Dispose(); s_PhysicalReceiverMasks = null;
            s_PhysicalPageOwners?.Dispose();
            s_PhysicalPageOwners = null;
            s_AllocatorCounters?.Dispose();
            s_AllocatorCounters = null;
            s_AllocationRequests?.Dispose();
            s_AllocationRequests = null;
            s_PageWorkList?.Dispose();
            s_PageWorkList = null;
            s_PageWorkDispatchArgs?.Dispose();
            s_PageWorkDispatchArgs = null;
            if (!preservePressure)
            {
                s_PagePressure?.Dispose();
                s_PagePressure = null;
            }
            s_MeshletPageRequests?.Dispose();
            s_MeshletPageRequests = null;
            s_MeshletPageIndirectArgs?.Dispose();
            s_MeshletPageIndirectArgs = null;
            s_MeshletRasterPages?.Dispose();
            s_MeshletRasterPages = null;
            s_StaticInvalidationBounds?.Dispose();
            s_StaticInvalidationBounds = null;
            s_DynamicInvalidationBounds?.Dispose();
            s_DynamicInvalidationBounds = null;
            s_PageTableUpload = null;
            s_PageMetadataUpload = null;
            s_PhysicalPageOwnersUpload = null;
            if (!preservePressure) MarkReceiverFeedbackProduced(0ul, -1);
        }

        private static RTHandle AllocatePhysicalPage(
            int width,
            int height,
            string name)
        {
            return RTHandles.Alloc(
                width,
                height,
                slices: DepthLayerCount,
                depthBufferBits: DepthBits.None,
                colorFormat: GraphicsFormat.R32_UInt,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                dimension: TextureDimension.Tex2DArray,
                enableRandomWrite: true,
                useMipMap: false,
                autoGenerateMips: false,
                isShadowMap: false,
                anisoLevel: 1,
                mipMapBias: 0.0f,
                msaaSamples: MSAASamples.None,
                bindTextureMS: false,
                useDynamicScale: false,
                useDynamicScaleExplicit: false,
                name: name);
        }

        internal static bool IsSupportedOnCurrentPlatform()
        {
            bool supportsFormat = IsPhysicalPageFormatSupported();
            return IsSupported(
                SystemInfo.graphicsDeviceType,
                SystemInfo.usesReversedZBuffer,
                SystemInfo.supportsComputeShaders,
                supportsFormat);
        }

        private static bool IsPhysicalPageFormatSupported()
        {
            return SystemInfo.IsFormatSupported(
                    GraphicsFormat.R32_UInt,
                    GraphicsFormatUsage.Render)
                && SystemInfo.IsFormatSupported(
                    GraphicsFormat.R32_UInt,
                    GraphicsFormatUsage.LoadStore);
        }
    }
}
