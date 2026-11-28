using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Phase 2: Dedicated zero-GC local-light VSM pool, unified allocation/hierarchy/view-compaction,
    // shared-append perspective caster culling, and 4x4 windowed mip hardware rasterization.
    // Keeps directional VSM cache isolated so local-light changes never wipe directional clipmaps.
    internal sealed class VirtualShadowMapLocalCasterPipeline : IDisposable
    {
        private const int DefaultLocalPageBudget = 256;
        private const int MaxLocalActiveViews = 256;
        private const int MaxLocalInstanceJobs = 16384;
        private const int MaxLocalPageRequestsPerList = 32768;
        private const int RendererListCount = (int)VividRendererListID.Count;

        internal static readonly int UnifiedViewModeId = Shader.PropertyToID("_VSMUnifiedViewMode");
        internal static readonly int UnifiedActiveViewsId = Shader.PropertyToID("_VSMUnifiedActiveViews");
        internal static readonly int UnifiedActiveViewsRWId = Shader.PropertyToID("_VSMUnifiedActiveViewsRW");
        internal static readonly int UnifiedActiveViewCountRWId = Shader.PropertyToID("_VSMUnifiedActiveViewCountRW");
        internal static readonly int UnifiedInstanceDispatchArgsRWId = Shader.PropertyToID("_VSMUnifiedInstanceDispatchArgsRW");
        internal static readonly int AllocatePassModeId = Shader.PropertyToID("_VSMAllocatePassMode");
        internal static readonly int AllocateStartVSMId = Shader.PropertyToID("_VSMAllocateStartVSMId");
        internal static readonly int LocalMaxPhysicalPagesId = Shader.PropertyToID("_VSMLocalMaxPhysicalPages");
        internal static readonly int UnifiedMaxEntriesPerVSMId = Shader.PropertyToID("_VSMUnifiedMaxEntriesPerVSM");
        internal static readonly int UnifiedTotalHierarchyNodesId = Shader.PropertyToID("_VSMUnifiedTotalHierarchyNodes");
        internal static readonly int UnifiedTotalBoundRectsId = Shader.PropertyToID("_VSMUnifiedTotalBoundRects");
        internal static readonly int CompactStartVSMId = Shader.PropertyToID("_VSMCompactStartVSMId");
        internal static readonly int UnifiedMaxActiveViewsId = Shader.PropertyToID("_VSMUnifiedMaxActiveViews");
        internal static readonly int LocalSourceInstanceCountId = Shader.PropertyToID("_VSMLocalSourceInstanceCount");
        internal static readonly int LocalMaxInstanceJobsId = Shader.PropertyToID("_VSMLocalMaxInstanceJobs");
        internal static readonly int LocalMaxPageRequestsPerListId = Shader.PropertyToID("_VSMLocalMaxPageRequestsPerList");
        internal static readonly int LocalForcedLODDepthId = Shader.PropertyToID("_VSMLocalForcedLODDepth");
        internal static readonly int LocalLODErrorThresholdId = Shader.PropertyToID("_VSMLocalLODErrorThreshold");
        internal static readonly int LocalUseDrawSetId = Shader.PropertyToID("_VSMLocalUseDrawSet");
        internal static readonly int LocalDrawSetIndicesId = Shader.PropertyToID("_VSMLocalDrawSetIndices");
        internal static readonly int LocalInstanceJobsRWId = Shader.PropertyToID("_VSMLocalInstanceJobsRW");
        internal static readonly int LocalInstanceJobsId = Shader.PropertyToID("_VSMLocalInstanceJobs");
        internal static readonly int LocalJobDispatchArgsRWId = Shader.PropertyToID("_VSMLocalJobDispatchArgsRW");
        internal static readonly int LocalMeshletPageRequestsRWId = Shader.PropertyToID("_VSMLocalMeshletPageRequestsRW");
        internal static readonly int LocalMeshletPageIndirectArgsRWId = Shader.PropertyToID("_VSMLocalMeshletPageIndirectArgsRW");
        internal static readonly int LocalAllocatedCountRWId = Shader.PropertyToID("_VSMLocalAllocatedCountRW");

        private static readonly int PageTableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int WritablePageTableId = Shader.PropertyToID("_VSMPrototypeWritablePageTable");
        private static readonly int PageMetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly int RemapPageMetadataId = Shader.PropertyToID("_VSMRemapPageMetadata");
        private static readonly int PageRequestFlagsId = Shader.PropertyToID("_VSMPageRequestFlags");
        private static readonly int PageRequestFlagsROId = Shader.PropertyToID("_VSMPageRequestFlagsRO");
        private static readonly int PhysicalPageOwnersId = Shader.PropertyToID("_VSMPrototypePhysicalPageOwners");
        private static readonly int AllocatorCountersId = Shader.PropertyToID("_VSMPrototypeAllocatorCounters");
        private static readonly int PageWorkListId = Shader.PropertyToID("_VSMPageWorkList");
        private static readonly int PageWorkListRWId = Shader.PropertyToID("_VSMPageWorkListRW");
        private static readonly int PageWorkDispatchArgsRWId = Shader.PropertyToID("_VSMPageWorkDispatchArgsRW");
        private static readonly int MergeListId = Shader.PropertyToID("_VSMMergePageWorkList");
        private static readonly int MergeListRWId = Shader.PropertyToID("_VSMMergePageWorkListRW");
        private static readonly int MergeArgsRWId = Shader.PropertyToID("_VSMMergePageDispatchArgsRW");
        private static readonly int PhysicalPagePoolId = Shader.PropertyToID("_VSMPhysicalPagePool");
        private static readonly int PhysicalPagePoolRWId = Shader.PropertyToID("_VSMPhysicalPagePoolRW");
        private static readonly int PageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");
        private static readonly int PhysicalPagesPerRowId = Shader.PropertyToID("_VSMPrototypePhysicalPagesPerRow");
        private static readonly int PageTableEntryCountId = Shader.PropertyToID("_VSMPrototypePageTableEntryCount");
        private static readonly int PhysicalPageCapacityId = Shader.PropertyToID("_VSMPrototypePhysicalPageCapacity");
        private static readonly int FeedbackFrameIndexId = Shader.PropertyToID("_VSMPrototypeFeedbackFrameIndex");
        private static readonly int CasterLayerId = Shader.PropertyToID("_VSMPrototypeCasterLayer");
        private static readonly int RasterWindowPagesId = Shader.PropertyToID("_VSMRasterWindowPages");
        private static readonly int MeshletPageRequestsId = Shader.PropertyToID("_VSMPrototypeMeshletPageRequests");
        private static readonly int MeshletRasterPagesId = Shader.PropertyToID("_VSMPrototypeMeshletRasterPages");
        private static readonly int UnityIndirectDrawArgsId = Shader.PropertyToID("_UnityIndirectDrawArgs");
        private static readonly int UnityBaseCommandIdId = Shader.PropertyToID("_UnityBaseCommandID");

        private static readonly ProfilingSampler LocalPipelineSampler = new("VSM.LocalCasterPipeline");
        private static readonly ProfilingSampler LocalAllocSampler = new("VSM.LocalAllocate");
        private static readonly ProfilingSampler LocalHierarchySampler = new("VSM.LocalHierarchy");
        private static readonly ProfilingSampler LocalCullRasterSampler = new("VSM.LocalCullAndRaster");
        private static readonly ProfilingSampler LocalFinalizeSampler = new("VSM.LocalFinalize");

        internal struct Kernels
        {
            public int UpdatePhysicalAddressesUnified;
            public int RemapPagesUnified;
            public int ResetPhysicalPageLists;
            public int UpdatePhysicalPagesUnified;
            public int PackAvailablePages;
            public int AppendEmptyPhysicalPages;
            public int AllocateNewPageMappingsUnified;
            public int UpdateLocalPagePressure;
            public int AppendPhysicalPageLists;
            public int ResetPageWorkLists;
            public int BuildPageWorkLists;
            public int ClearUnifiedPageBounds;
            public int BuildUnifiedPageCullHierarchy;
            public int CompactUnifiedViews;
            public int CullLocalInstances;
            public int BuildLocalMeshletPages;
            public int ClearPhysicalPages;
            public int ResetMergePages;
            public int SelectMergePages;
            public int MergeStaticPages;
            public int ReducePageOccupancy;
            public int FinalizeDirtyPages;
            public int PropagateMappedMips;

            public bool IsValid =>
                UpdatePhysicalAddressesUnified >= 0 && RemapPagesUnified >= 0
                && ResetPhysicalPageLists >= 0 && UpdatePhysicalPagesUnified >= 0
                && PackAvailablePages >= 0 && AppendEmptyPhysicalPages >= 0
                && AllocateNewPageMappingsUnified >= 0 && AppendPhysicalPageLists >= 0
                && ResetPageWorkLists >= 0 && BuildPageWorkLists >= 0
                && ClearUnifiedPageBounds >= 0 && BuildUnifiedPageCullHierarchy >= 0
                && CompactUnifiedViews >= 0 && CullLocalInstances >= 0
                && BuildLocalMeshletPages >= 0 && ClearPhysicalPages >= 0
                && ResetMergePages >= 0 && SelectMergePages >= 0
                && MergeStaticPages >= 0 && ReducePageOccupancy >= 0
                && FinalizeDirtyPages >= 0 && PropagateMappedMips >= 0;
        }

        private RTHandle m_PhysicalPagePool;
        private GraphicsBuffer m_PageTable;
        private GraphicsBuffer m_PageMetadata;
        private GraphicsBuffer m_RemapPageMetadata;
        private GraphicsBuffer m_PhysicalPageOwners;
        private GraphicsBuffer m_PhysicalPageDescriptors;
        private GraphicsBuffer m_PhysicalPageLists;
        private GraphicsBuffer m_PhysicalReceiverMasks;
        private GraphicsBuffer m_AllocatorCounters;
        private GraphicsBuffer m_PagePressure;
        private GraphicsBuffer m_PageWorkList;
        private GraphicsBuffer m_PageWorkDispatchArgs;
        private GraphicsBuffer m_MergePageWorkList;
        private GraphicsBuffer m_MergePageDispatchArgs;
        private GraphicsBuffer m_UncachedPageRectBounds;
        private GraphicsBuffer m_PageCullHierarchy;
        private GraphicsBuffer m_UnifiedActiveViews;
        private GraphicsBuffer m_UnifiedActiveViewCount;
        private GraphicsBuffer m_UnifiedInstanceDispatchArgs;
        private GraphicsBuffer m_LocalInstanceJobs;
        private GraphicsBuffer m_LocalJobDispatchArgs;
        private GraphicsBuffer m_LocalMeshletPageRequests;
        private GraphicsBuffer m_LocalMeshletPageIndirectArgs;
        private GraphicsBuffer m_LocalAllocatedCount;
        private GraphicsBuffer m_NextMapBuffer;
        private GraphicsBuffer m_DummyDrawSetIndices;

        private readonly MaterialPropertyBlock m_DrawProperties = new();
        private readonly float[] m_VirtualTextureSpaceParams = new float[8];
        private readonly float[] m_VirtualTextureMipOffsets = new float[8];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[8];

        private VirtualShadowMapRegistry m_Registry;
        private int m_PhysicalPagesPerRow;
        private int m_PhysicalPageCapacity;
        private int m_PageTableCapacity;
        private int m_HierarchyCapacity;
        private int m_BoundRectCapacity;
        private int m_MaxEntriesPerVSM;
        private int m_LocalFinePageQuota;
        private bool m_PagePressureEnabled;

        internal bool Active { get; private set; }
        internal RTHandle PhysicalPagePool => m_PhysicalPagePool;
        internal GraphicsBuffer PageTable => m_PageTable;
        internal GraphicsBuffer PageMetadata => m_PageMetadata;
        internal GraphicsBuffer PhysicalPageDescriptors => m_PhysicalPageDescriptors;
        internal GraphicsBuffer AllocatorCounters => m_AllocatorCounters;
        internal GraphicsBuffer PagePressure => m_PagePressure;
        internal GraphicsBuffer UnifiedActiveViews => m_UnifiedActiveViews;
        internal VirtualShadowMapRegistry Registry => m_Registry;
        internal int PhysicalPagesPerRow => m_PhysicalPagesPerRow;
        internal int PhysicalPageCapacity => m_PhysicalPageCapacity;

        internal void Disable()
        {
            Active = false;
            m_Registry = null;
        }

        internal bool PrepareForPass(
            IRenderPass pass,
            VirtualShadowMapRegistry registry,
            int requestedPhysicalBudget,
            float localFinePageQuotaRatio = 0.85f,
            bool pagePressureEnabled = true)
        {
            Active = false;
            if (registry == null || registry.ProjectionCount <= registry.DirectionalCount
                || registry.TotalPageTableEntries <= 0
                || !VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
            {
                return false;
            }

            int pageBudget = Mathf.Clamp(
                requestedPhysicalBudget > 0 ? requestedPhysicalBudget : DefaultLocalPageBudget,
                16,
                4096);
            int pagesPerRow = Mathf.CeilToInt(Mathf.Sqrt(pageBudget));
            int capacity = pagesPerRow * pagesPerRow;

            int maxEntries = 1;
            for (int i = 0; i < registry.ProjectionCount; i++)
            {
                ref readonly VirtualShadowMapProjection p = ref registry.Projections[i];
                bool isSingle = (p.Meta.y & (uint)VividVSMProjectionFlags.SinglePage) != 0u;
                bool isDir = p.Meta.x == (uint)VividVSMLightType.Directional;
                int axis = (int)Mathf.Max(p.Addressing.z, 1u);
                int entries = isSingle ? 1 : (isDir ? axis * axis : VirtualShadowMapAddressing.CalculateTotalMipChainEntries(axis));
                if (entries > maxEntries) maxEntries = entries;
            }

            EnsureResources(
                pagesPerRow,
                capacity,
                registry.TotalPageTableEntries,
                Mathf.Max(registry.TotalHierarchyNodes, 1),
                Mathf.Max(registry.TotalBoundRectCount, 1),
                Mathf.Max(registry.NextMapCount, 1));

            m_Registry = registry;
            m_MaxEntriesPerVSM = maxEntries;
            float clampedQuota = Mathf.Clamp(localFinePageQuotaRatio, 0.1f, 1.0f);
            m_LocalFinePageQuota = clampedQuota < 0.999f
                ? Mathf.Clamp(Mathf.RoundToInt(capacity * clampedQuota), 1, capacity)
                : 0;
            m_PagePressureEnabled = pagePressureEnabled;

            if (pass != null)
            {
                PassRecorder.ImportTextureForPass(pass, m_PhysicalPagePool, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PageTable, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PageMetadata, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_RemapPageMetadata, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PhysicalPageOwners, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PhysicalPageDescriptors, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PhysicalPageLists, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PhysicalReceiverMasks, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_AllocatorCounters, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PagePressure, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PageWorkList, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PageWorkDispatchArgs, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_MergePageWorkList, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_MergePageDispatchArgs, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_UncachedPageRectBounds, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_PageCullHierarchy, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_UnifiedActiveViews, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_UnifiedActiveViewCount, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_UnifiedInstanceDispatchArgs, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalInstanceJobs, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalJobDispatchArgs, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalMeshletPageRequests, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalMeshletPageIndirectArgs, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_LocalAllocatedCount, AccessFlags.ReadWrite);
                PassRecorder.ImportBufferForPass(pass, m_NextMapBuffer, AccessFlags.ReadWrite);
            }

            Active = true;
            return true;
        }

        internal void RecordLocalRasterPipeline(
            CommandBuffer cmd,
            ComputeShader shader,
            in Kernels kernels,
            VirtualShadowMapLocalLightGrid localGrid,
            VividGPUDrivenSystem gpuSystem,
            VividPrimitiveDrawSet staticDrawSet,
            VividPrimitiveDrawSet dynamicDrawSet,
            Material[] rasterMaterials,
            RTHandle rasterDepth,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding,
            float virtualTextureAdaptiveMipBias,
            int frameIndex,
            int directionalProjectionCount,
            int directionalPageTableEntryCount,
            int directionalPhysicalPagesPerRow,
            int directionalPhysicalPageCapacity)
        {
            if (!Active || !localGrid.Active || !kernels.IsValid || m_Registry == null || gpuSystem == null)
                return;

            int projCount = m_Registry.ProjectionCount;
            int totalEntries = m_Registry.TotalPageTableEntries;
            int totalHierarchy = Mathf.Max(m_Registry.TotalHierarchyNodes, 1);
            int totalBoundRects = Mathf.Max(m_Registry.TotalBoundRectCount, 1);
            int dirCount = m_Registry.DirectionalCount;
            int physGroups = CoreUtils.DivRoundUp(m_PhysicalPageCapacity, 64);
            int pageSize = VirtualShadowMapPrototypeRuntime.PageSize;
            int rasterWindowPages = VirtualShadowMapPrototypeRuntime.RasterWindowScale;

            using (new ProfilingScope(cmd, LocalPipelineSampler))
            {
                cmd.SetComputeIntParam(shader, PageSizeId, pageSize);
                cmd.SetComputeIntParam(shader, PhysicalPagesPerRowId, m_PhysicalPagesPerRow);
                cmd.SetComputeIntParam(shader, PageTableEntryCountId, totalEntries);
                cmd.SetComputeIntParam(shader, PhysicalPageCapacityId, m_PhysicalPageCapacity);
                cmd.SetComputeIntParam(shader, FeedbackFrameIndexId, frameIndex);
                cmd.SetComputeIntParam(shader, VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, 1);
                cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, projCount);
                cmd.SetComputeIntParam(shader, RasterWindowPagesId, rasterWindowPages);

                // 1. Cross-frame physical-slot remap for local lights.
                if (m_Registry.RequiresRemap && m_Registry.NextMapCount > 0)
                {
                    cmd.SetBufferData(m_NextMapBuffer, m_Registry.NextMap, 0, 0, m_Registry.NextMapCount);
                    cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.NextMapCountId, m_Registry.NextMapCount);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, VirtualShadowMapProjectionSet.NextMapId, m_NextMapBuffer);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, VirtualShadowMapPrototypeRuntime.PhysicalPageDescriptorsId, m_PhysicalPageDescriptors);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, RemapPageMetadataId, m_RemapPageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalAddressesUnified, WritablePageTableId, m_PageTable);
                    cmd.DispatchCompute(shader, kernels.UpdatePhysicalAddressesUnified, physGroups, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.RemapPagesUnified, RemapPageMetadataId, m_RemapPageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.RemapPagesUnified, WritablePageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.RemapPagesUnified, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.RemapPagesUnified, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.RemapPagesUnified, VirtualShadowMapPrototypeRuntime.PhysicalPageDescriptorsId, m_PhysicalPageDescriptors);
                    cmd.DispatchCompute(shader, kernels.RemapPagesUnified, physGroups, 1, 1);
                }

                // 2. Unified physical page LRU update + two-pass allocation (Coarse/Single-Page -> Local Primary).
                using (new ProfilingScope(cmd, LocalAllocSampler))
                {
                    cmd.SetComputeBufferParam(shader, kernels.ResetPhysicalPageLists, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, m_PhysicalPageLists);
                    cmd.SetComputeBufferParam(shader, kernels.ResetPhysicalPageLists, AllocatorCountersId, m_AllocatorCounters);
                    cmd.SetComputeBufferParam(shader, kernels.ResetPhysicalPageLists, VirtualShadowMapReceiverQuality.PressureRWId, m_PagePressure);
                    cmd.DispatchCompute(shader, kernels.ResetPhysicalPageLists, 1, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, m_PhysicalPageLists);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, VirtualShadowMapPrototypeRuntime.PhysicalPageDescriptorsId, m_PhysicalPageDescriptors);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, WritablePageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, PageRequestFlagsROId, localGrid.LocalPageRequestFlags);
                    cmd.SetComputeBufferParam(shader, kernels.UpdatePhysicalPagesUnified, AllocatorCountersId, m_AllocatorCounters);
                    cmd.DispatchCompute(shader, kernels.UpdatePhysicalPagesUnified, physGroups, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.PackAvailablePages, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, m_PhysicalPageLists);
                    cmd.DispatchCompute(shader, kernels.PackAvailablePages, 1, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.AppendEmptyPhysicalPages, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, m_PhysicalPageLists);
                    cmd.DispatchCompute(shader, kernels.AppendEmptyPhysicalPages, physGroups, 1, 1);

                    cmd.SetComputeIntParam(shader, UnifiedMaxEntriesPerVSMId, m_MaxEntriesPerVSM);
                    cmd.SetComputeIntParam(shader, AllocateStartVSMId, dirCount);
                    cmd.SetComputeIntParam(shader, LocalMaxPhysicalPagesId, m_LocalFinePageQuota);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, PageRequestFlagsROId, localGrid.LocalPageRequestFlags);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, WritablePageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, VirtualShadowMapPrototypeRuntime.PhysicalPageDescriptorsId, m_PhysicalPageDescriptors);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasksId, m_PhysicalReceiverMasks);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, m_PhysicalPageLists);
                    cmd.SetComputeBufferParam(shader, kernels.AllocateNewPageMappingsUnified, AllocatorCountersId, m_AllocatorCounters);

                    int entryGroups = CoreUtils.DivRoundUp(m_MaxEntriesPerVSM, 64);
                    // Only allocate local projections (Pass 1 coarse/single-page first, then Pass 2 fine pages).
                    cmd.SetComputeIntParam(shader, AllocatePassModeId, 1);
                    cmd.DispatchCompute(shader, kernels.AllocateNewPageMappingsUnified, entryGroups, projCount, 1);
                    cmd.SetComputeIntParam(shader, AllocatePassModeId, 2);
                    cmd.DispatchCompute(shader, kernels.AllocateNewPageMappingsUnified, entryGroups, projCount, 1);

                    if (kernels.UpdateLocalPagePressure >= 0)
                    {
                        cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.LocalPagePressureEnabledId, m_PagePressureEnabled ? 1 : 0);
                        cmd.SetComputeBufferParam(shader, kernels.UpdateLocalPagePressure, AllocatorCountersId, m_AllocatorCounters);
                        cmd.SetComputeBufferParam(shader, kernels.UpdateLocalPagePressure, VirtualShadowMapReceiverQuality.PressureRWId, m_PagePressure);
                        cmd.DispatchCompute(shader, kernels.UpdateLocalPagePressure, 1, 1, 1);
                    }

                    cmd.SetComputeBufferParam(shader, kernels.AppendPhysicalPageLists, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, m_PhysicalPageLists);
                    cmd.DispatchCompute(shader, kernels.AppendPhysicalPageLists, physGroups, 1, 1);
                }

                // 3. Work lists, per-(VSMId, Mip) UncachedPageRectBounds & H-mip hierarchy, physical page clear.
                using (new ProfilingScope(cmd, LocalHierarchySampler))
                {
                    cmd.SetComputeBufferParam(shader, kernels.ResetPageWorkLists, PageWorkDispatchArgsRWId, m_PageWorkDispatchArgs);
                    cmd.DispatchCompute(shader, kernels.ResetPageWorkLists, 1, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.BuildPageWorkLists, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.BuildPageWorkLists, WritablePageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.BuildPageWorkLists, PageRequestFlagsId, localGrid.LocalPageRequestFlags);
                    cmd.SetComputeBufferParam(shader, kernels.BuildPageWorkLists, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.BuildPageWorkLists, PageWorkListRWId, m_PageWorkList);
                    cmd.SetComputeBufferParam(shader, kernels.BuildPageWorkLists, PageWorkDispatchArgsRWId, m_PageWorkDispatchArgs);
                    cmd.DispatchCompute(shader, kernels.BuildPageWorkLists, physGroups, 1, 1);

                    cmd.SetComputeIntParam(shader, UnifiedTotalHierarchyNodesId, totalHierarchy);
                    cmd.SetComputeIntParam(shader, UnifiedTotalBoundRectsId, totalBoundRects);
                    cmd.SetComputeBufferParam(shader, kernels.ClearUnifiedPageBounds, VirtualShadowMapPrototypeRuntime.PageCullHierarchyRWId, m_PageCullHierarchy);
                    cmd.SetComputeBufferParam(shader, kernels.ClearUnifiedPageBounds, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsRWId, m_UncachedPageRectBounds);
                    int clearCount = Mathf.Max(totalHierarchy, totalBoundRects * 2);
                    cmd.DispatchCompute(shader, kernels.ClearUnifiedPageBounds, CoreUtils.DivRoundUp(clearCount, 64), 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, VirtualShadowMapPrototypeRuntime.PhysicalPageDescriptorsId, m_PhysicalPageDescriptors);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, PageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, localGrid.LocalPageReceiverMasks);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, VirtualShadowMapPrototypeRuntime.PageCullHierarchyRWId, m_PageCullHierarchy);
                    cmd.SetComputeBufferParam(shader, kernels.BuildUnifiedPageCullHierarchy, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsRWId, m_UncachedPageRectBounds);
                    cmd.DispatchCompute(shader, kernels.BuildUnifiedPageCullHierarchy, physGroups, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.ClearPhysicalPages, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.ClearPhysicalPages, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.ClearPhysicalPages, PageWorkListId, m_PageWorkList);
                    cmd.SetComputeTextureParam(shader, kernels.ClearPhysicalPages, PhysicalPagePoolRWId, m_PhysicalPagePool);
                    cmd.DispatchCompute(shader, kernels.ClearPhysicalPages, m_PageWorkDispatchArgs, VirtualShadowMapPrototypeRuntime.ClearWorkArgsOffset);
                }

                // 4. Compacted (VSMId, Mip) perspective culling + 4x4 windowed HW rasterization (Static & Dynamic).
                using (new ProfilingScope(cmd, LocalCullRasterSampler))
                {
                    RecordLocalLayerCullAndRaster(cmd, shader, kernels, localGrid, gpuSystem, staticDrawSet,
                        casterLayer: 0, dirCount, totalHierarchy, totalBoundRects, rasterMaterials, rasterDepth,
                        virtualTextureReady, in virtualTextureBinding, virtualTextureAdaptiveMipBias, frameIndex);
                    RecordLocalLayerCullAndRaster(cmd, shader, kernels, localGrid, gpuSystem, dynamicDrawSet,
                        casterLayer: 1, dirCount, totalHierarchy, totalBoundRects, rasterMaterials, rasterDepth,
                        virtualTextureReady, in virtualTextureBinding, virtualTextureAdaptiveMipBias, frameIndex);
                }

                // 5. Merge static -> final, occupancy reduction, finalize dirty pages, and mip-chain propagation.
                using (new ProfilingScope(cmd, LocalFinalizeSampler))
                {
                    cmd.SetComputeBufferParam(shader, kernels.ResetMergePages, MergeArgsRWId, m_MergePageDispatchArgs);
                    cmd.DispatchCompute(shader, kernels.ResetMergePages, 1, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.SelectMergePages, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.SelectMergePages, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.SelectMergePages, MergeListRWId, m_MergePageWorkList);
                    cmd.SetComputeBufferParam(shader, kernels.SelectMergePages, MergeArgsRWId, m_MergePageDispatchArgs);
                    cmd.DispatchCompute(shader, kernels.SelectMergePages, physGroups, 1, 1);

                    cmd.SetComputeBufferParam(shader, kernels.MergeStaticPages, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.MergeStaticPages, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.MergeStaticPages, MergeListId, m_MergePageWorkList);
                    cmd.SetComputeTextureParam(shader, kernels.MergeStaticPages, PhysicalPagePoolRWId, m_PhysicalPagePool);
                    cmd.DispatchCompute(shader, kernels.MergeStaticPages, m_MergePageDispatchArgs, 0);

                    cmd.SetComputeBufferParam(shader, kernels.ReducePageOccupancy, PageWorkListId, m_PageWorkList);
                    cmd.SetComputeBufferParam(shader, kernels.ReducePageOccupancy, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.ReducePageOccupancy, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeTextureParam(shader, kernels.ReducePageOccupancy, PhysicalPagePoolId, m_PhysicalPagePool);
                    cmd.DispatchCompute(shader, kernels.ReducePageOccupancy, m_PageWorkDispatchArgs, VirtualShadowMapPrototypeRuntime.OccupancyWorkArgsOffset);

                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, PageMetadataId, m_PageMetadata);
                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, WritablePageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, localGrid.LocalPageReceiverMasks);
                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasksId, m_PhysicalReceiverMasks);
                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, PhysicalPageOwnersId, m_PhysicalPageOwners);
                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, PageWorkListId, m_PageWorkList);
                    cmd.SetComputeBufferParam(shader, kernels.FinalizeDirtyPages, VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgsId, m_PageWorkDispatchArgs);
                    cmd.DispatchCompute(shader, kernels.FinalizeDirtyPages, m_PageWorkDispatchArgs, VirtualShadowMapPrototypeRuntime.FinalizeWorkArgsOffset);

                    cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, projCount);
                    cmd.SetComputeBufferParam(shader, kernels.PropagateMappedMips, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                    cmd.SetComputeBufferParam(shader, kernels.PropagateMappedMips, WritablePageTableId, m_PageTable);
                    cmd.SetComputeBufferParam(shader, kernels.PropagateMappedMips, VirtualShadowMapProjectionSet.PageOffsetsId, VirtualShadowMapPrototypeRuntime.Projections.PageOffsetsBuffer);
                    cmd.DispatchCompute(shader, kernels.PropagateMappedMips,
                        CoreUtils.DivRoundUp(Mathf.Max(m_Registry.MaxMip0Pages, 1), 64),
                        Mathf.Max(projCount, 1), 1);
                }

                // Restore directional globals & compute params so downstream directional passes see unchanged state.
                cmd.SetGlobalInt(UnifiedViewModeId, 0);
                cmd.SetGlobalBuffer(VirtualShadowMapProjectionSet.BufferId, VirtualShadowMapPrototypeRuntime.Projections.Buffer);
                cmd.SetGlobalBuffer(PageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
                cmd.SetGlobalBuffer(PageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.SetGlobalBuffer(VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                cmd.SetGlobalInt(PhysicalPagesPerRowId, directionalPhysicalPagesPerRow);
                cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, directionalProjectionCount);
                cmd.SetComputeIntParam(shader, PageTableEntryCountId, directionalPageTableEntryCount);
                cmd.SetComputeIntParam(shader, PhysicalPagesPerRowId, directionalPhysicalPagesPerRow);
                cmd.SetComputeIntParam(shader, PhysicalPageCapacityId, directionalPhysicalPageCapacity);
            }
        }

        private void RecordLocalLayerCullAndRaster(
            CommandBuffer cmd,
            ComputeShader shader,
            in Kernels kernels,
            VirtualShadowMapLocalLightGrid localGrid,
            VividGPUDrivenSystem gpuSystem,
            VividPrimitiveDrawSet drawSet,
            int casterLayer,
            int startVsmId,
            int totalHierarchy,
            int totalBoundRects,
            Material[] rasterMaterials,
            RTHandle rasterDepth,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding,
            float virtualTextureAdaptiveMipBias,
            int frameIndex)
        {
            VividGPUDrivenBufferSet buffers = gpuSystem.BufferSet;
            if (buffers == null || buffers.InstanceDataBuffer == null || buffers.MeshLODNodesBuffer == null || buffers.MeshletsBuffer == null)
                return;

            bool directPrimitive = gpuSystem.UsePrimitiveSceneDirect;
            int fullInstanceCount = directPrimitive ? gpuSystem.PrimitiveScene.DrawSectionTable.Count : buffers.InstanceCount;
            bool useDrawSet = drawSet != null && drawSet.DispatchInstanceIndexBuffer != null && drawSet.DispatchInstanceIndexBuffer.IsValid();
            int sourceInstanceCount = useDrawSet ? drawSet.DrawCount : fullInstanceCount;
            if (sourceInstanceCount <= 0)
                return;

            cmd.SetComputeIntParam(shader, CasterLayerId, casterLayer);
            cmd.SetComputeIntParam(shader, CompactStartVSMId, startVsmId);
            cmd.SetComputeIntParam(shader, UnifiedMaxActiveViewsId, MaxLocalActiveViews);
            cmd.SetComputeIntParam(shader, UnifiedTotalHierarchyNodesId, totalHierarchy);
            cmd.SetComputeIntParam(shader, UnifiedTotalBoundRectsId, totalBoundRects);
            cmd.SetComputeIntParam(shader, LocalSourceInstanceCountId, sourceInstanceCount);
            cmd.SetComputeIntParam(shader, LocalMaxInstanceJobsId, MaxLocalInstanceJobs);
            cmd.SetComputeIntParam(shader, LocalMaxPageRequestsPerListId, MaxLocalPageRequestsPerList);
            cmd.SetComputeIntParam(shader, LocalForcedLODDepthId, gpuSystem.ForcedMeshLODNodeDepth);
            cmd.SetComputeFloatParam(shader, LocalLODErrorThresholdId, gpuSystem.MeshLODErrorThreshold);
            cmd.SetComputeIntParam(shader, LocalUseDrawSetId, useDrawSet ? 1 : 0);

            // 4A. Compact active (VSMId, Mip) views & reset shared indirect draw args.
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, m_UncachedPageRectBounds);
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, UnifiedActiveViewsRWId, m_UnifiedActiveViews);
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, UnifiedActiveViewCountRWId, m_UnifiedActiveViewCount);
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, UnifiedInstanceDispatchArgsRWId, m_UnifiedInstanceDispatchArgs);
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, LocalJobDispatchArgsRWId, m_LocalJobDispatchArgs);
            cmd.SetComputeBufferParam(shader, kernels.CompactUnifiedViews, LocalMeshletPageIndirectArgsRWId, m_LocalMeshletPageIndirectArgs);
            cmd.DispatchCompute(shader, kernels.CompactUnifiedViews, 1, 1, 1);

            // 4B. Perspective instance culling -> shared instance-view job append queue.
            cmd.SetComputeIntParam(shader, VividGPUDrivenShaderIDs._InstanceDataCount, fullInstanceCount);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, VividGPUDrivenShaderIDs._InstanceData, buffers.InstanceDataBuffer);
            gpuSystem.PrimitiveSceneBufferSet.BindToCompute(cmd, shader, kernels.CullLocalInstances, gpuSystem.PrimitiveScene, directPrimitive);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, LocalDrawSetIndicesId, useDrawSet ? drawSet.DispatchInstanceIndexBuffer : m_DummyDrawSetIndices);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, UnifiedActiveViewsId, m_UnifiedActiveViews);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, m_UncachedPageRectBounds);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, VirtualShadowMapPrototypeRuntime.PageCullHierarchyId, m_PageCullHierarchy);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, LocalInstanceJobsRWId, m_LocalInstanceJobs);
            cmd.SetComputeBufferParam(shader, kernels.CullLocalInstances, LocalJobDispatchArgsRWId, m_LocalJobDispatchArgs);
            cmd.DispatchCompute(shader, kernels.CullLocalInstances, m_UnifiedInstanceDispatchArgs, 0);

            // 4C. Perspective LOD selection + meshlet perspective bounds + 4x4 window emission.
            cmd.SetComputeIntParam(shader, VividGPUDrivenShaderIDs._MeshLODNodeCount, buffers.MeshLODNodeCount);
            cmd.SetComputeIntParam(shader, VividGPUDrivenShaderIDs._MeshletCount, buffers.MeshletCount);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VividGPUDrivenShaderIDs._InstanceData, buffers.InstanceDataBuffer);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VividGPUDrivenShaderIDs._MaterialData, buffers.MaterialDataBuffer);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VividGPUDrivenShaderIDs._MeshLODNodes, buffers.MeshLODNodesBuffer);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VividGPUDrivenShaderIDs._Meshlets, buffers.MeshletsBuffer);
            gpuSystem.PrimitiveSceneBufferSet.BindToCompute(cmd, shader, kernels.BuildLocalMeshletPages, gpuSystem.PrimitiveScene, directPrimitive);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, UnifiedActiveViewsId, m_UnifiedActiveViews);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, m_UncachedPageRectBounds);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, PageTableId, m_PageTable);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, PageMetadataId, m_PageMetadata);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, localGrid.LocalPageReceiverMasks);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, LocalInstanceJobsId, m_LocalInstanceJobs);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, LocalJobDispatchArgsRWId, m_LocalJobDispatchArgs);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, LocalMeshletPageRequestsRWId, m_LocalMeshletPageRequests);
            cmd.SetComputeBufferParam(shader, kernels.BuildLocalMeshletPages, LocalMeshletPageIndirectArgsRWId, m_LocalMeshletPageIndirectArgs);
            VirtualShadowMapPerformanceThrottle.BindLocalRasterFeedback(cmd, shader, kernels.BuildLocalMeshletPages);
            cmd.DispatchCompute(shader, kernels.BuildLocalMeshletPages, m_LocalJobDispatchArgs, 0);

            // 4D. 4x4 windowed HW rasterization into Local VSM PhysicalPagePool.
            if (rasterMaterials == null || rasterDepth == null)
                return;

            cmd.SetGlobalInt(CasterLayerId, casterLayer);
            cmd.SetGlobalInt(UnifiedViewModeId, 1);
            cmd.SetGlobalInt(PageSizeId, VirtualShadowMapPrototypeRuntime.PageSize);
            cmd.SetGlobalInt(RasterWindowPagesId, VirtualShadowMapPrototypeRuntime.RasterWindowScale);
            cmd.SetGlobalBuffer(UnifiedActiveViewsId, m_UnifiedActiveViews);
            cmd.SetGlobalBuffer(VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
            cmd.SetGlobalBuffer(PageTableId, m_PageTable);
            cmd.SetGlobalBuffer(PageMetadataId, m_PageMetadata);
            cmd.SetGlobalBuffer(VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, localGrid.LocalPageReceiverMasks);
            cmd.SetGlobalInt(PhysicalPagesPerRowId, m_PhysicalPagesPerRow);

            CoreUtils.SetRenderTarget(cmd, rasterDepth, ClearFlag.None, Color.black, depthSlice: -1);
            cmd.SetRandomWriteTarget(0, m_PhysicalPagePool);
            for (int rendererListIndex = 0; rendererListIndex < rasterMaterials.Length; rendererListIndex++)
            {
                VividRendererListID batchKey = (VividRendererListID)rendererListIndex;
                if (!gpuSystem.IsShadowRendererBatchActive(batchKey))
                    continue;
                Material material = rasterMaterials[rendererListIndex];
                if (material == null)
                    continue;
                if (!virtualTextureReady && (batchKey & VividRendererListID.AlphaTest) != 0)
                    continue;

                m_DrawProperties.Clear();
                m_DrawProperties.SetBuffer(MeshletPageRequestsId, m_LocalMeshletPageRequests);
                m_DrawProperties.SetBuffer(UnityIndirectDrawArgsId, m_LocalMeshletPageIndirectArgs);
                m_DrawProperties.SetBuffer(MeshletRasterPagesId, VirtualShadowMapPrototypeRuntime.MeshletRasterPages);
                if (gpuSystem.UsesVirtualTexture && virtualTextureReady)
                {
                    GPUDrivenVirtualTextureBindingUtility.BindSpaceProperties(
                        m_DrawProperties,
                        virtualTextureBinding,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        frameIndex,
                        virtualTextureAdaptiveMipBias);
                }
                m_DrawProperties.SetInteger(UnityBaseCommandIdId, rendererListIndex);
                cmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    material,
                    0,
                    MeshTopology.Triangles,
                    m_LocalMeshletPageIndirectArgs,
                    VividGPUDrivenCullingBuffers.GetIndirectDrawArgsByteOffset(0, rendererListIndex),
                    m_DrawProperties);
            }
            cmd.ClearRandomWriteTargets();
        }

        private void EnsureResources(int pagesPerRow, int capacity, int pageTableEntries,
            int hierarchyNodes, int boundRects, int nextMapCount)
        {
            int pageSize = VirtualShadowMapPrototypeRuntime.PageSize;
            int atlasDim = pagesPerRow * pageSize;

            if (m_PhysicalPagePool == null || m_PhysicalPagePool.rt == null
                || m_PhysicalPagePool.rt.width != atlasDim || m_PhysicalPageCapacity != capacity)
            {
                m_PhysicalPagePool?.Release();
                m_PhysicalPagePool = RTHandles.Alloc(
                    atlasDim,
                    atlasDim,
                    slices: 2,
                    dimension: TextureDimension.Tex2DArray,
                    colorFormat: GraphicsFormat.R32_UInt,
                    enableRandomWrite: true,
                    useMipMap: false,
                    autoGenerateMips: false,
                    filterMode: FilterMode.Point,
                    wrapMode: TextureWrapMode.Clamp,
                    name: "VSMLocalPhysicalPagePool");
                m_PhysicalPagesPerRow = pagesPerRow;
                m_PhysicalPageCapacity = capacity;
                ResetPhysicalPoolBuffers(capacity);
            }

            if (m_PageTable == null || !m_PageTable.IsValid() || m_PageTable.count < pageTableEntries)
            {
                int count = Mathf.NextPowerOfTwo(Mathf.Max(pageTableEntries, 64));
                m_PageTableCapacity = count;
                m_PageTable?.Dispose();
                m_PageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint))
                { name = "VSMLocalPageTable" };
                m_PageTable.SetData(new uint[count]);

                m_PageMetadata?.Dispose();
                m_PageMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint) * 4)
                { name = "VSMLocalPageMetadata" };
                m_PageMetadata.SetData(new uint4[count]);

                // Table growth invalidates prior virtual indices; reset physical pool owners cleanly.
                ResetPhysicalPoolBuffers(m_PhysicalPageCapacity);
            }

            if (m_PageCullHierarchy == null || !m_PageCullHierarchy.IsValid() || m_PageCullHierarchy.count < hierarchyNodes)
            {
                int count = Mathf.NextPowerOfTwo(Mathf.Max(hierarchyNodes, 64));
                m_HierarchyCapacity = count;
                m_PageCullHierarchy?.Dispose();
                m_PageCullHierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint) * 3)
                { name = "VSMLocalPageCullHierarchy" };
            }

            if (m_UncachedPageRectBounds == null || !m_UncachedPageRectBounds.IsValid() || m_UncachedPageRectBounds.count < boundRects * 2)
            {
                int count = Mathf.NextPowerOfTwo(Mathf.Max(boundRects * 2, 64));
                m_BoundRectCapacity = count;
                m_UncachedPageRectBounds?.Dispose();
                m_UncachedPageRectBounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint) * 4)
                { name = "VSMLocalUncachedPageRectBounds" };
            }

            if (m_NextMapBuffer == null || !m_NextMapBuffer.IsValid() || m_NextMapBuffer.count < nextMapCount)
            {
                m_NextMapBuffer?.Dispose();
                m_NextMapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(Mathf.Max(nextMapCount, 16)), VividVSMNextMapEntry.Stride)
                { name = "VSMLocalNextMap" };
            }

            EnsureStaticWorkBuffers();
        }

        private void ResetPhysicalPoolBuffers(int capacity)
        {
            int listWords = capacity * 4 + 4;
            m_PhysicalPageOwners?.Dispose();
            m_PhysicalPageOwners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint))
            { name = "VSMLocalPhysicalPageOwners" };
            m_PhysicalPageOwners.SetData(new uint[capacity]);

            m_PhysicalPageDescriptors?.Dispose();
            m_PhysicalPageDescriptors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 2)
            { name = "VSMLocalPhysicalPageDescriptors" };
            m_PhysicalPageDescriptors.SetData(new uint2[capacity]);

            m_RemapPageMetadata?.Dispose();
            m_RemapPageMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 4)
            { name = "VSMLocalRemapPageMetadata" };

            m_PhysicalReceiverMasks?.Dispose();
            m_PhysicalReceiverMasks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 2)
            { name = "VSMLocalPhysicalReceiverMasks" };
            m_PhysicalReceiverMasks.SetData(new uint2[capacity]);

            m_PhysicalPageLists?.Dispose();
            m_PhysicalPageLists = new GraphicsBuffer(GraphicsBuffer.Target.Structured, listWords, sizeof(uint))
            { name = "VSMLocalPhysicalPageLists" };
            var initialLists = new uint[listWords];
            for (int slot = 0; slot < capacity; slot++) initialLists[slot] = (uint)slot;
            m_PhysicalPageLists.SetData(initialLists);

            m_PageWorkList?.Dispose();
            m_PageWorkList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 2, sizeof(uint))
            { name = "VSMLocalPageWorkList" };

            m_MergePageWorkList?.Dispose();
            m_MergePageWorkList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint))
            { name = "VSMLocalMergePageWorkList" };
        }

        private void EnsureStaticWorkBuffers()
        {
            if (m_AllocatorCounters == null || !m_AllocatorCounters.IsValid() || m_AllocatorCounters.count < 8)
            {
                m_AllocatorCounters?.Dispose();
                m_AllocatorCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, sizeof(uint))
                { name = "VSMLocalAllocatorCounters" };
            }
            if (m_PagePressure == null || !m_PagePressure.IsValid())
            {
                m_PagePressure?.Dispose();
                m_PagePressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint) * 4)
                { name = "VSMLocalPagePressure" };
                m_PagePressure.SetData(new uint4[3]);
            }
            if (m_PageWorkDispatchArgs == null || !m_PageWorkDispatchArgs.IsValid())
            {
                m_PageWorkDispatchArgs?.Dispose();
                m_PageWorkDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 9, sizeof(uint))
                { name = "VSMLocalPageWorkDispatchArgs" };
            }
            if (m_MergePageDispatchArgs == null || !m_MergePageDispatchArgs.IsValid())
            {
                m_MergePageDispatchArgs?.Dispose();
                m_MergePageDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 4, sizeof(uint))
                { name = "VSMLocalMergePageDispatchArgs" };
            }
            if (m_UnifiedActiveViews == null || !m_UnifiedActiveViews.IsValid())
            {
                m_UnifiedActiveViews?.Dispose();
                m_UnifiedActiveViews = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxLocalActiveViews, VividVSMActiveView.Stride)
                { name = "VSMUnifiedActiveViews" };
            }
            if (m_UnifiedActiveViewCount == null || !m_UnifiedActiveViewCount.IsValid())
            {
                m_UnifiedActiveViewCount?.Dispose();
                m_UnifiedActiveViewCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint))
                { name = "VSMUnifiedActiveViewCount" };
            }
            if (m_UnifiedInstanceDispatchArgs == null || !m_UnifiedInstanceDispatchArgs.IsValid())
            {
                m_UnifiedInstanceDispatchArgs?.Dispose();
                m_UnifiedInstanceDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 3, sizeof(uint))
                { name = "VSMUnifiedInstanceDispatchArgs" };
            }
            if (m_LocalInstanceJobs == null || !m_LocalInstanceJobs.IsValid())
            {
                m_LocalInstanceJobs?.Dispose();
                m_LocalInstanceJobs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxLocalInstanceJobs, sizeof(uint) * 2)
                { name = "VSMLocalInstanceJobs" };
            }
            if (m_LocalJobDispatchArgs == null || !m_LocalJobDispatchArgs.IsValid())
            {
                m_LocalJobDispatchArgs?.Dispose();
                m_LocalJobDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 4, sizeof(uint))
                { name = "VSMLocalJobDispatchArgs" };
            }
            if (m_LocalMeshletPageRequests == null || !m_LocalMeshletPageRequests.IsValid())
            {
                m_LocalMeshletPageRequests?.Dispose();
                m_LocalMeshletPageRequests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RendererListCount * MaxLocalPageRequestsPerList, sizeof(uint) * 4)
                { name = "VSMLocalMeshletPageRequests" };
            }
            if (m_LocalMeshletPageIndirectArgs == null || !m_LocalMeshletPageIndirectArgs.IsValid())
            {
                m_LocalMeshletPageIndirectArgs?.Dispose();
                m_LocalMeshletPageIndirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, RendererListCount * 4, sizeof(uint))
                { name = "VSMLocalMeshletPageIndirectArgs" };
            }
            if (m_LocalAllocatedCount == null || !m_LocalAllocatedCount.IsValid())
            {
                m_LocalAllocatedCount?.Dispose();
                m_LocalAllocatedCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint))
                { name = "VSMLocalAllocatedCount" };
            }
            if (m_DummyDrawSetIndices == null || !m_DummyDrawSetIndices.IsValid())
            {
                m_DummyDrawSetIndices?.Dispose();
                m_DummyDrawSetIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint))
                { name = "VSMLocalDummyDrawSetIndices" };
            }
        }

        public void Dispose()
        {
            Disable();
            m_PhysicalPagePool?.Release(); m_PhysicalPagePool = null;
            m_PageTable?.Dispose(); m_PageTable = null;
            m_PageMetadata?.Dispose(); m_PageMetadata = null;
            m_RemapPageMetadata?.Dispose(); m_RemapPageMetadata = null;
            m_PhysicalPageOwners?.Dispose(); m_PhysicalPageOwners = null;
            m_PhysicalPageDescriptors?.Dispose(); m_PhysicalPageDescriptors = null;
            m_PhysicalPageLists?.Dispose(); m_PhysicalPageLists = null;
            m_PhysicalReceiverMasks?.Dispose(); m_PhysicalReceiverMasks = null;
            m_AllocatorCounters?.Dispose(); m_AllocatorCounters = null;
            m_PagePressure?.Dispose(); m_PagePressure = null;
            m_PageWorkList?.Dispose(); m_PageWorkList = null;
            m_PageWorkDispatchArgs?.Dispose(); m_PageWorkDispatchArgs = null;
            m_MergePageWorkList?.Dispose(); m_MergePageWorkList = null;
            m_MergePageDispatchArgs?.Dispose(); m_MergePageDispatchArgs = null;
            m_UncachedPageRectBounds?.Dispose(); m_UncachedPageRectBounds = null;
            m_PageCullHierarchy?.Dispose(); m_PageCullHierarchy = null;
            m_UnifiedActiveViews?.Dispose(); m_UnifiedActiveViews = null;
            m_UnifiedActiveViewCount?.Dispose(); m_UnifiedActiveViewCount = null;
            m_UnifiedInstanceDispatchArgs?.Dispose(); m_UnifiedInstanceDispatchArgs = null;
            m_LocalInstanceJobs?.Dispose(); m_LocalInstanceJobs = null;
            m_LocalJobDispatchArgs?.Dispose(); m_LocalJobDispatchArgs = null;
            m_LocalMeshletPageRequests?.Dispose(); m_LocalMeshletPageRequests = null;
            m_LocalMeshletPageIndirectArgs?.Dispose(); m_LocalMeshletPageIndirectArgs = null;
            m_LocalAllocatedCount?.Dispose(); m_LocalAllocatedCount = null;
            m_NextMapBuffer?.Dispose(); m_NextMapBuffer = null;
            m_DummyDrawSetIndices?.Dispose(); m_DummyDrawSetIndices = null;
            m_PhysicalPagesPerRow = 0;
            m_PhysicalPageCapacity = 0;
            m_PageTableCapacity = 0;
            m_HierarchyCapacity = 0;
            m_BoundRectCapacity = 0;
        }
    }
}
