using VividRP.Runtime.VirtualShadowMap;
using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VSMShadowPass : ShadowCasterPass
    {
        private static readonly int VSMPrototypePageTableId = Shader.PropertyToID("_VSMPrototypePageTable");

        private static readonly int VSMPrototypeWritablePageTableId = Shader.PropertyToID("_VSMPrototypeWritablePageTable");

        private static readonly int VSMPrototypePageMetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly int VSMPageRequestFlagsId = Shader.PropertyToID("_VSMPageRequestFlags");

        private static readonly int VSMPrototypePhysicalPageOwnersId = Shader.PropertyToID("_VSMPrototypePhysicalPageOwners");

        private static readonly int VSMAllocationRequestsId = Shader.PropertyToID("_VSMAllocationRequests");
        private static readonly int VSMPageWorkListId = Shader.PropertyToID("_VSMPageWorkList");
        private static readonly int VSMPageWorkListRWId = Shader.PropertyToID("_VSMPageWorkListRW");
        private static readonly int VSMPageWorkDispatchArgsRWId = Shader.PropertyToID("_VSMPageWorkDispatchArgsRW");

        private static readonly int VSMPrototypeAllocatorCountersId = Shader.PropertyToID("_VSMPrototypeAllocatorCounters");

        private static readonly int VSMPrototypeStaticPhysicalPageRWId = Shader.PropertyToID("_VSMPrototypeStaticPhysicalPageRW");

        private static readonly int VSMPrototypeStaticPhysicalPageId = Shader.PropertyToID("_VSMPrototypeStaticPhysicalPage");

        private static readonly int VSMPrototypeDynamicPhysicalPageRWId = Shader.PropertyToID("_VSMPrototypeDynamicPhysicalPageRW");

        private static readonly int VSMPrototypeDynamicPhysicalPageId = Shader.PropertyToID("_VSMPrototypeDynamicPhysicalPage");

        private static readonly int VSMPrototypePageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");

        private static readonly int VSMPrototypeVirtualResolutionId = Shader.PropertyToID("_VSMPrototypeVirtualResolution");

        private static readonly int VSMPrototypePagesPerAxisId = Shader.PropertyToID("_VSMPrototypePagesPerAxis");

        private static readonly int VSMPrototypePhysicalPagesPerRowId = Shader.PropertyToID("_VSMPrototypePhysicalPagesPerRow");

        private static readonly int VSMPrototypePageTableEntryCountId = Shader.PropertyToID("_VSMPrototypePageTableEntryCount");

        private static readonly int VSMPrototypePhysicalPageCapacityId = Shader.PropertyToID("_VSMPrototypePhysicalPageCapacity");

        private static readonly int VSMPrototypeFeedbackFrameIndexId = Shader.PropertyToID("_VSMPrototypeFeedbackFrameIndex");

        private static readonly int VSMPrototypeCasterLayerId = Shader.PropertyToID("_VSMPrototypeCasterLayer");

        private static readonly int VSMPrototypeSourceMeshletRequestsId = Shader.PropertyToID("_VSMPrototypeSourceMeshletRequests");

        private static readonly int VSMPrototypeSourceMeshletIndirectArgsId = Shader.PropertyToID("_VSMPrototypeSourceMeshletIndirectArgs");

        private static readonly int VSMPrototypeMeshletPageRequestsId = Shader.PropertyToID("_VSMPrototypeMeshletPageRequests");

        private static readonly int VSMPrototypeMeshletPageIndirectArgsId = Shader.PropertyToID("_VSMPrototypeMeshletPageIndirectArgs");

        private static readonly int VSMPrototypeMeshletRasterPagesId = Shader.PropertyToID("_VSMPrototypeMeshletRasterPages");

        private static readonly int VSMPrototypeSourceRequestsPerCascadeCapacityId = Shader.PropertyToID("_VSMPrototypeSourceRequestsPerCascadeCapacity");

        private static readonly int VSMPrototypeStaticInvalidationBoundsId = Shader.PropertyToID("_VSMPrototypeStaticInvalidationBounds");

        private static readonly int VSMPrototypeStaticInvalidationBoundsCountId = Shader.PropertyToID("_VSMPrototypeStaticInvalidationBoundsCount");

        private static readonly int VSMUnityRasterEnabledId = Shader.PropertyToID("_VSMUnityRasterEnabled");

        private static readonly int VSMRasterViewProjectionId = Shader.PropertyToID("_VSMRasterViewProjection");

        private static readonly int VSMRasterOriginId = Shader.PropertyToID("_VSMRasterOrigin");

        private static readonly int VSMProjectionIndexId = Shader.PropertyToID("_VSMProjectionIndex");

        private static readonly int VSMRemapPageMetadataId = Shader.PropertyToID("_VSMRemapPageMetadata");

        private const string VirtualShadowMapCasterKeywordName = "VIVID_VSM_CASTER";

        private const string VirtualShadowMapPageCasterKeywordName = "VIVID_VSM_PAGE_CASTER";

        private const string VSMPrototypeAllocatePagesKernelName = "VSMPrototypeAllocatePages";

        private const string VSMPrototypeMarkAllAllocatedPagesDirtyKernelName = "VSMPrototypeMarkAllAllocatedPagesDirty";

        private const string VSMPrototypeInvalidateStaticPagesKernelName = "VSMPrototypeInvalidateStaticPages";

        private const string VSMPrototypeClearPhysicalPagesKernelName = "VSMClearPhysicalPagesIndirect";

        private const string VSMPrototypeFinalizeDirtyPagesKernelName = "VSMPrototypeFinalizeDirtyPages";

        private const string VSMPrototypePrepareMeshletPageRequestsKernelName = "VSMPrototypePrepareMeshletPageRequests";

        private const string VSMPrototypeCullMeshletsToPagesKernelName = "VSMPrototypeCullMeshletsToPages";

        private static readonly GlobalKeyword s_VirtualShadowMapCasterKeyword =
            GlobalKeyword.Create(VirtualShadowMapCasterKeywordName);

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        private readonly RenderGraphTexture m_DefaultReceiverDepth, m_DefaultReceiverNormal;

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");

        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");

        private static readonly int CSMInvViewProjMatrixId = Shader.PropertyToID("_CSMInvViewProjMatrix");

        private static readonly int CSMOutputWidthId = Shader.PropertyToID("_CSMOutputWidth");

        private static readonly int CSMOutputHeightId = Shader.PropertyToID("_CSMOutputHeight");

        private static readonly int CSMFrameIndexId = Shader.PropertyToID("_CSMFrameIndex");

        private static readonly int VSMReceiverParametersId = Shader.PropertyToID("_VSMReceiverParameters");

        private static readonly int VSMPrototypeRequestEnabledId = Shader.PropertyToID("_VSMPrototypeRequestEnabled");
        private static readonly int VSMPageUpdateBudgetId = Shader.PropertyToID("_VSMPageUpdateBudget");
        private int m_PageUpdateBudget;

        private int m_VSMMarkReceiverPagesKernel = -1;
        private int m_VSMMarkCoarsePagesKernel = -1;
        private int m_VSMClearPageHierarchyKernel = -1;
        private int m_VSMBuildPageHierarchyKernel = -1;

        private int m_VSMClearReceiverRequestsKernel = -1;

        private int m_VSMResetReceiverFeedbackKernel = -1;

        private Matrix4x4 m_ReceiverViewProjection;

        private Vector4 m_ReceiverQuality, m_ReceiverParameters, m_ReceiverSMRTParameters;

        private int m_ReceiverWidth, m_ReceiverHeight;

        private readonly VividGPUCullingContext[] m_ClipmapCullingContexts =
            new VividGPUCullingContext[VirtualShadowMapClipmapLayout.MaxLevels];

        private readonly ShadowDrawingSettings[] m_ClipmapDrawSettings =
            new ShadowDrawingSettings[VirtualShadowMapClipmapLayout.MaxLevels];

        private VividGPULODSelectionContext m_ClipmapLODContext;

        private int m_VSMRemapPagesKernel = -1;

        private int m_VSMUpdatePhysicalAddressesKernel = -1;

        private int m_VSMClearVirtualMappingsKernel = -1;

        private bool m_VirtualShadowMapPrototypeActive;

        private bool m_VirtualShadowMapStaticPoolNeedsCacheRefresh;

        private bool m_VirtualShadowMapStaticPoolNeedsFullRefresh;

        private int m_VirtualShadowMapStaticInvalidationBoundsCount;

        private bool m_DynamicPoolNeedsFullRefresh;

        private bool m_HasUntrackedDynamicCasters;

        private int m_DynamicInvalidationBoundsCount;

        private uint m_DynamicShadowRevision;

        private VirtualShadowMapPrototypeCacheKey m_VirtualShadowMapPrototypeCacheKey;

        private ulong m_PageDebugCameraEntityId;

        private ComputeShader m_VirtualShadowMapPageManagementCompute;

        private int m_VSMPrepareAllocationKernel = -1;

        private int m_VirtualShadowMapAllocatePagesKernel = -1;

        private int m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel = -1;

        private int m_VirtualShadowMapInvalidateStaticPagesKernel = -1;

        private int m_VSMMarkDynamicPagesDirtyKernel = -1;

        private int m_VSMInvalidateDynamicPagesKernel = -1;

        private int m_VirtualShadowMapClearPhysicalPagesKernel = -1;

        private int m_VirtualShadowMapFinalizeDirtyPagesKernel = -1;

        private int m_VSMReducePageOccupancyKernel = -1;
        private int m_VSMBuildPageWorkListsKernel = -1;

        private int m_VirtualShadowMapPrepareMeshletPageRequestsKernel = -1;

        private int m_VirtualShadowMapCullMeshletsToPagesKernel = -1;

        [RenderGraphResource(Name = "VSMPageTable", Access = AccessFlags.ReadWrite)]
        private RenderGraphBuffer m_PageTable = RenderGraphBuffer.CreateStructured("VSMPageTable", sizeof(uint));

        public VSMShadowPass() : base(nameof(VSMShadowPass))
        {
            m_DepthTexture = m_DefaultReceiverDepth = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = m_DefaultReceiverNormal = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.R8G8B8A8_UNorm);
        }

        public override void Create()
        {
            base.Create();
            m_VirtualShadowMapPageManagementCompute =
                PipelineResourceManager.Get<VividRPCoreResources>()?.CSMShadowResolveCompute;
            m_VSMMarkReceiverPagesKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMMarkReceiverPages");
            m_VSMMarkCoarsePagesKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMMarkCoarsePages");
            m_VSMClearPageHierarchyKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMClearPageCullHierarchy");
            m_VSMBuildPageHierarchyKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMBuildPageCullHierarchy");
            m_VSMClearReceiverRequestsKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMPrototypeClearReceiverRequests");
            m_VSMResetReceiverFeedbackKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMPrototypeResetReceiverFeedback");
            m_VSMPrepareAllocationKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMPrototypePrepareAllocation");
            m_VSMMarkDynamicPagesDirtyKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMPrototypeMarkDynamicPagesDirty");
            m_VSMInvalidateDynamicPagesKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMPrototypeInvalidateDynamicPages");
            m_VirtualShadowMapAllocatePagesKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeAllocatePagesKernelName);
            m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeMarkAllAllocatedPagesDirtyKernelName);
            m_VirtualShadowMapInvalidateStaticPagesKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeInvalidateStaticPagesKernelName);
            m_VirtualShadowMapClearPhysicalPagesKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeClearPhysicalPagesKernelName);
            m_VSMReducePageOccupancyKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute, "VSMReducePageOccupancyIndirect");
            m_VSMBuildPageWorkListsKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute, "VSMBuildPageWorkLists");
            m_VirtualShadowMapFinalizeDirtyPagesKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeFinalizeDirtyPagesKernelName);
            m_VirtualShadowMapPrepareMeshletPageRequestsKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypePrepareMeshletPageRequestsKernelName);
            m_VirtualShadowMapCullMeshletsToPagesKernel = FindKernelOrInvalid(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeCullMeshletsToPagesKernelName);
            m_VSMRemapPagesKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMRemapPages");
            m_VSMUpdatePhysicalAddressesKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMUpdatePhysicalPageAddresses");
            m_VSMClearVirtualMappingsKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMClearVirtualPageMappings");
            for (int i = 0; i < m_Materials.Length; i++)
            {
                if (m_Materials[i] == null) continue;
                CoreUtils.SetKeyword(m_Materials[i], VirtualShadowMapCasterKeywordName, true);
                CoreUtils.SetKeyword(m_Materials[i], VirtualShadowMapPageCasterKeywordName, true);
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            VirtualShadowMapPrototypeRuntime.BeginFrame();
            m_PageTable.ClearImportedBuffer();
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapStaticPoolNeedsCacheRefresh = false;
            m_VirtualShadowMapStaticPoolNeedsFullRefresh = false;
            m_VirtualShadowMapStaticInvalidationBoundsCount = 0;
            m_DynamicPoolNeedsFullRefresh = false;
            m_HasUntrackedDynamicCasters = false;
            m_DynamicInvalidationBoundsCount = 0;
            m_DynamicShadowRevision = 0u;
            m_VirtualShadowMapPrototypeCacheKey = default;
            m_IsActive = false;
            var shadowSettings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            if (shadowSettings == null || !shadowSettings.enableVirtualShadowMapPrototype.value)
                return;
            m_PageUpdateBudget = shadowSettings.virtualShadowMapPageUpdateBudget.value;
            base.Prepare(frameData);
            if (!m_IsActive) return;
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var shadowData = m_ShadowData;
            m_PageDebugCameraEntityId = cameraData.camera != null
                ? EntityId.ToULong(cameraData.camera.GetEntityId()) : 0ul;
            VirtualShadowMapClipmapLayout clipmaps = m_ShadowData.clipmaps;
            for (int i = 0; i < clipmaps.Count; i++)
            {
                // No receiver-centered sphere: retained pages need every caster in
                // their unchanged light-space prism, not the current camera view.
                VividGPUDrivenCullingContextUtility.Build(clipmaps.Views[i], clipmaps.Projections[i],
                    clipmaps.Centers[i], clipmaps.Rotation * Vector3.right, clipmaps.Rotation * Vector3.up,
                    new Vector2(clipmaps.Resolution, clipmaps.Resolution), false,
                    VividInstancePassMask.Shadows, Vector4.zero, false,
                    out m_ClipmapCullingContexts[i], out m_ClipmapLODContext);
            }
            // w=0 selects orthographic, per-view shadow-texel LOD in MeshletListBuild.
            m_ClipmapLODContext.CameraPosition.w = 0;
            PrepareVirtualShadowMapPrototype(cameraData);
            if (m_VirtualShadowMapPrototypeActive)
            {
                var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
                float angularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
                var lightData = frameData.GetOrCreate<VividLightData>();
                if (DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out _, out var light)
                    && light != null)
                    angularDiameter = Mathf.Max(light.angularDiameter, 0);
                m_ReceiverViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
                m_ReceiverWidth = cameraData.actualWidth;
                m_ReceiverHeight = cameraData.actualHeight;
                m_ReceiverQuality = VirtualShadowMapReceiverQuality.BuildParameters(settings);
                m_ReceiverSMRTParameters = VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, angularDiameter);
                m_ReceiverParameters = new Vector4(settings.virtualShadowMapPCF.value ? 1 : 0,
                    shadowData.depthBias, shadowData.slopeScaleDepthBias,
                    settings.virtualShadowMapStochasticFiltering.value ? 1 : 0);
            }
        }

        public override void Record(UnsafePassContext context)
        {
            if (!m_IsActive || !m_VirtualShadowMapPrototypeActive) return;
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                nativeCmd.SetGlobalInt(VSMUnityRasterEnabledId, 0);
                RecordVirtualShadowMapLayout(nativeCmd);
                if (!TryPrepareMeshletShadowDraws(nativeCmd, out var meshletContext))
                {
                    VirtualShadowMapPrototypeRuntime.MarkFallback(VirtualShadowMapPrototypeFallbackReason.RecordPreparationFailed);
                    return;
                }
                if (m_HasMeshletShadowCasters && !meshletContext.VirtualTextureReady)
                {
                    VirtualShadowMapPrototypeRuntime.MarkFallback(VirtualShadowMapPrototypeFallbackReason.VirtualTextureUnavailable);
                    return;
                }
                nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowData.shadowCasterState);
                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
                var fallbackReason = VirtualShadowMapPrototypeFallbackReason.ReceiverFeedbackUnavailable;
                m_ShadowData.virtualShadowMapRendered = RecordReceiverPageRequests(nativeCmd)
                    && DrawVirtualShadowMapPrototypePages(nativeCmd, in meshletContext, out fallbackReason);
                if (!m_ShadowData.virtualShadowMapRendered)
                    VirtualShadowMapPrototypeRuntime.MarkFallback(fallbackReason);
            }
        }

        private bool RecordReceiverPageRequests(CommandBuffer cmd)
        {
            if (!m_DepthTexture.innerHandle.IsValid() || !m_GBuffer1.innerHandle.IsValid()
                || m_ReceiverWidth <= 0 || m_ReceiverHeight <= 0
                || !VirtualShadowMapPrototypeRuntime.Projections.LayoutRecorded)
                return false;

            var shader = m_VirtualShadowMapPageManagementCompute;
            SetVirtualShadowMapPageManagementParameters(cmd);
            cmd.SetComputeIntParam(shader, VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, 1);
            // Clear roles every frame, but preserve LRU age unless the producer
            // changed or time was rewound. Failed raster frames cannot leak demand.
            int clearKernel = VirtualShadowMapPrototypeRuntime.RequiresReceiverFeedbackReset(
                m_PageDebugCameraEntityId, m_FrameIndex)
                ? m_VSMResetReceiverFeedbackKernel : m_VSMClearReceiverRequestsKernel;
            using (new ProfilingScope(cmd, VSMProfiling.ResetFeedback))
            {
                cmd.SetComputeVectorParam(shader, VirtualShadowMapReceiverQuality.ParametersId, m_ReceiverQuality);
                cmd.SetComputeBufferParam(shader, clearKernel, VirtualShadowMapReceiverQuality.PressureRWId,
                    VirtualShadowMapPrototypeRuntime.PagePressure);
                cmd.SetComputeBufferParam(shader, clearKernel, VSMPageRequestFlagsId,
                    VirtualShadowMapPrototypeRuntime.PageRequestFlags);
                cmd.SetComputeBufferParam(shader, clearKernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId,
                    VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                if (clearKernel == m_VSMResetReceiverFeedbackKernel)
                    cmd.SetComputeBufferParam(shader, clearKernel, VSMPrototypePageMetadataId,
                        VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.DispatchCompute(shader, clearKernel,
                    CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PageTableEntryCount, 64), 1, 1);
            }
            using (new ProfilingScope(cmd, VSMProfiling.MarkCoarsePages))
            {
                cmd.SetComputeIntParam(shader, VSMPrototypeRequestEnabledId, 1);
                cmd.SetComputeBufferParam(shader, m_VSMMarkCoarsePagesKernel, VirtualShadowMapProjectionSet.BufferId,
                    VirtualShadowMapPrototypeRuntime.Projections.Buffer);
                cmd.SetComputeBufferParam(shader, m_VSMMarkCoarsePagesKernel, VSMPageRequestFlagsId,
                    VirtualShadowMapPrototypeRuntime.PageRequestFlags);
                cmd.SetComputeBufferParam(shader, m_VSMMarkCoarsePagesKernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId,
                    VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                cmd.DispatchCompute(shader, m_VSMMarkCoarsePagesKernel, 1, 1, 1);
            }
            using (new ProfilingScope(cmd, VSMProfiling.MarkReceivers))
            {
                int kernel = m_VSMMarkReceiverPagesKernel;
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapReceiverQuality.PressureId,
                    VirtualShadowMapPrototypeRuntime.PagePressure);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapProjectionSet.BufferId,
                    VirtualShadowMapPrototypeRuntime.Projections.Buffer);
                cmd.SetComputeBufferParam(shader, kernel, VSMPageRequestFlagsId,
                    VirtualShadowMapPrototypeRuntime.PageRequestFlags);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId,
                    VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                cmd.SetComputeTextureParam(shader, kernel, DepthTextureId, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(shader, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
                cmd.SetComputeMatrixParam(shader, CSMInvViewProjMatrixId, m_ReceiverViewProjection.inverse);
                cmd.SetComputeMatrixParam(shader, VirtualShadowMapReceiverQuality.ViewProjectionId, m_ReceiverViewProjection);
                cmd.SetComputeVectorParam(shader, VirtualShadowMapReceiverQuality.ParametersId, m_ReceiverQuality);
                cmd.SetComputeVectorParam(shader, VirtualShadowMapReceiverQuality.SMRTParametersId, m_ReceiverSMRTParameters);
                cmd.SetComputeVectorParam(shader, VSMReceiverParametersId, m_ReceiverParameters);
                cmd.SetComputeIntParam(shader, CSMOutputWidthId, m_ReceiverWidth);
                cmd.SetComputeIntParam(shader, CSMOutputHeightId, m_ReceiverHeight);
                cmd.SetComputeIntParam(shader, CSMFrameIndexId, m_FrameIndex);
                cmd.SetComputeIntParam(shader, VSMPrototypeRequestEnabledId, 1);
                cmd.DispatchCompute(shader, kernel, CoreUtils.DivRoundUp(m_ReceiverWidth, 8),
                    CoreUtils.DivRoundUp(m_ReceiverHeight, 8), 1);
            }
            VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(m_PageDebugCameraEntityId, m_FrameIndex);
            return true;
        }

        private void RecordVirtualShadowMapLayout(CommandBuffer cmd)
        {
            var projections = VirtualShadowMapPrototypeRuntime.Projections;
            if (projections.Count == 0 || !CanManageVirtualShadowMapPages()
                || m_VSMRemapPagesKernel < 0 || m_VSMUpdatePhysicalAddressesKernel < 0
                || m_VSMClearVirtualMappingsKernel < 0)
                return;
            using var layoutScope = new ProfilingScope(cmd, VSMProfiling.Layout);
            projections.Upload(cmd);
            if (projections.RequiresRemap)
            {
                // Like UE UpdatePhysicalPageAddresses, update one owner per physical slot.
                // Snapshot its virtual metadata before clearing the dense mappings; no
                // thread may scatter into a source page another thread still needs.
                var shader = m_VirtualShadowMapPageManagementCompute;
                int physicalGroups = CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, 64);
                SetVirtualShadowMapPageManagementParameters(cmd);
                cmd.SetComputeBufferParam(shader, m_VSMUpdatePhysicalAddressesKernel,
                    VirtualShadowMapProjectionSet.RemapId, projections.RemapBuffer);
                cmd.SetComputeBufferParam(shader, m_VSMUpdatePhysicalAddressesKernel,
                    VSMPrototypePageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.SetComputeBufferParam(shader, m_VSMUpdatePhysicalAddressesKernel,
                    VSMPrototypePhysicalPageOwnersId, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
                cmd.SetComputeBufferParam(shader, m_VSMUpdatePhysicalAddressesKernel,
                    VSMRemapPageMetadataId, VirtualShadowMapPrototypeRuntime.RemapPageMetadata);
                cmd.DispatchCompute(shader, m_VSMUpdatePhysicalAddressesKernel, physicalGroups, 1, 1);

                cmd.SetComputeBufferParam(shader, m_VSMClearVirtualMappingsKernel,
                    VSMPrototypeWritablePageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
                cmd.SetComputeBufferParam(shader, m_VSMClearVirtualMappingsKernel,
                    VSMPrototypePageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.DispatchCompute(shader, m_VSMClearVirtualMappingsKernel,
                    CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PageTableEntryCount, 64), 1, 1);

                cmd.SetComputeBufferParam(shader, m_VSMRemapPagesKernel,
                    VSMRemapPageMetadataId, VirtualShadowMapPrototypeRuntime.RemapPageMetadata);
                cmd.SetComputeBufferParam(shader, m_VSMRemapPagesKernel,
                    VSMPrototypeWritablePageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
                cmd.SetComputeBufferParam(shader, m_VSMRemapPagesKernel,
                    VSMPrototypePageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.SetComputeBufferParam(shader, m_VSMRemapPagesKernel,
                    VSMPrototypePhysicalPageOwnersId, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
                cmd.DispatchCompute(shader, m_VSMRemapPagesKernel, physicalGroups, 1, 1);
            }
            projections.CommitRecordedLayout();
        }

        private void PrepareVirtualShadowMapPrototype(VividCameraData cameraData)
        {
            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            bool prototypeEnabled = settings != null
                && settings.enableVirtualShadowMapPrototype.value;
            if (!prototypeEnabled
                || (!m_HasUnityShadowCasters && !m_HasMeshletShadowCasters))
            {
                return;
            }

            if (m_HasUnityShadowCasters
                && !VirtualShadowMapUnityCasterCompatibility.IsReady())
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.UnityCasterIncompatible);
                return;
            }

            if (cameraData?.camera == null)
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.InvalidCamera);
                return;
            }

            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.UnsupportedPlatform);
                return;
            }

            if (!TryValidateMeshletVirtualShadowMapReadiness(
                    out VirtualShadowMapPrototypeFallbackReason fallbackReason))
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(fallbackReason);
                return;
            }

            VirtualShadowMapClipmapLayout clipmaps = m_ShadowData.clipmaps;
            int virtualResolution = clipmaps.Resolution;
            if (clipmaps.Count <= 0 || !VirtualShadowMapPrototypeRuntime.EnsureResources(
                    virtualResolution, clipmaps.Count, settings.virtualShadowMapPhysicalPageBudget.value))
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.ResourceUnavailable);
                return;
            }

            VirtualShadowMapPrototypeRuntime.Projections.PrepareClipmaps(clipmaps);
            for (int i = 0; i < clipmaps.Count && m_HasUnityShadowCasters; i++)
            {
                var drawSettings = new ShadowDrawingSettings(m_CullingResults, m_MainLightVisibleIndex);
#pragma warning disable CS0618
                drawSettings.splitData = clipmaps.Splits[i];
#pragma warning restore CS0618
                drawSettings.splitIndex = -1;
                drawSettings.objectsFilter = ShadowObjectsFilter.AllObjects;
                drawSettings.useRenderingLayerMaskTest = false;
                m_ClipmapDrawSettings[i] = drawSettings;
            }
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.Projections.RemapBuffer, AccessFlags.ReadWrite);
            m_PageTable.SetImportedBuffer(VirtualShadowMapPrototypeRuntime.PageTable);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageMetadata, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageRequestFlags, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageReceiverMasks, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasks, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageCullHierarchy, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.UncachedPageRectBounds, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.AllocationRequests, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageWorkList, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PagePressure, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.RemapPageMetadata, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer, AccessFlags.ReadWrite);

            if (!CanManageVirtualShadowMapPages() || m_VSMRemapPagesKernel < 0
                || m_VSMUpdatePhysicalAddressesKernel < 0 || m_VSMClearVirtualMappingsKernel < 0)
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.PageManagementUnavailable);
                return;
            }

            if (ReferenceEquals(m_DepthTexture, m_DefaultReceiverDepth)
                || ReferenceEquals(m_GBuffer1, m_DefaultReceiverNormal)
                || m_VSMMarkReceiverPagesKernel < 0 || m_VSMClearReceiverRequestsKernel < 0
                || m_VSMResetReceiverFeedbackKernel < 0)
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.ReceiverFeedbackUnavailable);
                return;
            }

            VirtualShadowMapPrototypeRuntime.MarkPrepared();
            VividGPUDrivenSystem gpuDrivenSystem = m_HasMeshletShadowCasters
                ? VividGPUDrivenSystem.instance
                : null;
            gpuDrivenSystem?.UpdateVirtualTextureShadowInvalidations(m_VirtualTextureFrameData);
            m_VirtualShadowMapPrototypeCacheKey = new VirtualShadowMapPrototypeCacheKey(
                EntityId.ToULong(cameraData.camera.GetEntityId()),
                m_HasMeshletShadowCasters ? gpuDrivenSystem.PrimitiveScene.SceneToken : 0u,
                m_HasMeshletShadowCasters ? gpuDrivenSystem.PrimitiveScene.StaticShadowRevision : 0u,
                m_HasMeshletShadowCasters ? gpuDrivenSystem.TextureBindingRevision : 0u,
                clipmaps.Count,
                virtualResolution,
                m_HasMeshletShadowCasters ? gpuDrivenSystem.ForcedMeshLODNodeDepth : 0,
                m_HasMeshletShadowCasters ? gpuDrivenSystem.MeshLODErrorThreshold : 0.0f,
                0.0f, // VSM slope bias is receiver-only; it cannot invalidate cached caster depth.
                m_ShadowData.shadowCasterState,
                cameraData.camera.cullingMask,
                VirtualShadowMapPrototypeRuntime.Projections.Generation);
            if (!m_VirtualShadowMapPrototypeCacheKey.IsValid)
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.InvalidCacheKey);
                return;
            }

            m_VirtualShadowMapStaticPoolNeedsCacheRefresh =
                VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                    m_VirtualShadowMapPrototypeCacheKey);
            m_VirtualShadowMapStaticPoolNeedsFullRefresh =
                VirtualShadowMapPrototypeRuntime.RequiresFullStaticCacheRefresh(
                    m_VirtualShadowMapPrototypeCacheKey);
            if (m_VirtualShadowMapStaticPoolNeedsCacheRefresh
                && !m_VirtualShadowMapStaticPoolNeedsFullRefresh
                && gpuDrivenSystem != null)
            {
                VividPrimitiveScene primitiveScene = gpuDrivenSystem.PrimitiveScene;
                NativeArray<VividStaticShadowInvalidationBounds> invalidationBounds =
                    primitiveScene.PendingStaticShadowInvalidationBounds;
                if (primitiveScene.StaticShadowInvalidationRequiresFullRefresh
                    || invalidationBounds.Length == 0)
                {
                    m_VirtualShadowMapStaticPoolNeedsFullRefresh = true;
                }
                else if (!VirtualShadowMapPrototypeRuntime
                    .UploadStaticInvalidationBounds(invalidationBounds))
                {
                    VirtualShadowMapPrototypeRuntime.MarkFallback(
                        VirtualShadowMapPrototypeFallbackReason.ResourceUnavailable);
                    return;
                }
                else
                {
                    m_VirtualShadowMapStaticInvalidationBoundsCount =
                        invalidationBounds.Length;
                }
            }

            VividPrimitiveScene dynamicScene = gpuDrivenSystem?.PrimitiveScene;
            m_DynamicShadowRevision = dynamicScene?.DynamicShadowRevision ?? 0u;
            m_HasUntrackedDynamicCasters = (m_HasUnityShadowCasters
                && (VirtualShadowMapUnityCasterCompatibility.HasUnboundedCasters
                    || !VividShadowData.IsBoundsUsable(m_ShadowData.unityShadowCasterBounds)))
                || (dynamicScene?.HasUnboundedDynamicShadowCasters((uint)cameraData.camera.cullingMask) ?? false);
            m_DynamicPoolNeedsFullRefresh = VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(
                m_VirtualShadowMapPrototypeCacheKey, m_HasUntrackedDynamicCasters)
                || (dynamicScene?.DynamicShadowInvalidationRequiresFullRefresh ?? false);
            m_DynamicInvalidationBoundsCount = 0;
            NativeArray<VividStaticShadowInvalidationBounds> dynamicBounds = default;
            if (!m_DynamicPoolNeedsFullRefresh && dynamicScene != null
                && VirtualShadowMapPrototypeRuntime.DynamicShadowRevision != m_DynamicShadowRevision)
            {
                dynamicBounds = dynamicScene.PendingDynamicShadowInvalidationBounds;
                if (dynamicBounds.Length == 0)
                    m_DynamicPoolNeedsFullRefresh = true;
            }
            if (!m_DynamicPoolNeedsFullRefresh)
            {
                dynamicBounds = VirtualShadowMapPrototypeRuntime.BuildDynamicInvalidationBounds(
                    dynamicBounds, m_HasUnityShadowCasters, m_ShadowData.unityShadowCasterBounds);
                if (dynamicBounds.Length > 0
                    && !VirtualShadowMapPrototypeRuntime.UploadDynamicInvalidationBounds(dynamicBounds))
                {
                    VirtualShadowMapPrototypeRuntime.MarkFallback(VirtualShadowMapPrototypeFallbackReason.ResourceUnavailable);
                    return;
                }
                m_DynamicInvalidationBoundsCount = dynamicBounds.Length;
            }
            if (m_DynamicInvalidationBoundsCount > 0)
                PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.DynamicInvalidationBounds, AccessFlags.Read);

            PassRecorder.ImportTextureForPass(
                this,
                VirtualShadowMapPrototypeRuntime.StaticPhysicalPage,
                AccessFlags.ReadWrite);
            PassRecorder.ImportTextureForPass(
                this,
                VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage,
                AccessFlags.ReadWrite);
            PassRecorder.ImportTextureForPass(
                this,
                VirtualShadowMapPrototypeRuntime.RasterDepth,
                AccessFlags.ReadWrite);
            if (m_HasUnityShadowCasters)
                PassRecorder.ImportTextureForPass(this,
                    VirtualShadowMapPrototypeRuntime.UnityRasterDepth, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(
                this,
                VirtualShadowMapPrototypeRuntime.AllocatorCounters,
                AccessFlags.ReadWrite);
            if (m_VirtualShadowMapStaticInvalidationBoundsCount > 0)
            {
                PassRecorder.ImportBufferForPass(
                    this,
                    VirtualShadowMapPrototypeRuntime.StaticInvalidationBounds,
                    AccessFlags.Read);
            }
            m_VirtualShadowMapPrototypeActive = true;
            VirtualShadowMapPrototypeRuntime.MarkReady(
                m_VirtualShadowMapStaticPoolNeedsCacheRefresh);
        }

        internal static bool ShouldPrepareVirtualShadowMapPrototype(
            bool prototypeEnabled,
            bool hasUnityShadowCasters,
            bool hasMeshletShadowCasters,
            bool unityCastersCompatible)
        {
            return prototypeEnabled
                && (hasUnityShadowCasters || hasMeshletShadowCasters)
                && (!hasUnityShadowCasters || unityCastersCompatible);
        }

        private bool TryValidateMeshletVirtualShadowMapReadiness(
            out VirtualShadowMapPrototypeFallbackReason fallbackReason)
        {
            fallbackReason = VirtualShadowMapPrototypeFallbackReason.None;
            if (!m_HasMeshletShadowCasters)
                return true;

            if (!SystemInfo.supportsRenderTargetArrayIndexFromVertexShader)
            {
                fallbackReason =
                    VirtualShadowMapPrototypeFallbackReason.UnsupportedPlatform;
                return false;
            }

            if (!m_MeshletRenderingActive
                || !VividGPUDrivenSystem.HasInstance)
            {
                fallbackReason =
                    VirtualShadowMapPrototypeFallbackReason.GPUDrivenUnavailable;
                return false;
            }

            VividGPUDrivenSystem system = VividGPUDrivenSystem.instance;
            if (!system.IsAvailable
                || system.SceneData == null
                || system.SceneData.InstanceCount == 0)
            {
                fallbackReason =
                    VirtualShadowMapPrototypeFallbackReason.GPUDrivenUnavailable;
                return false;
            }

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (!HasMeshletShadowShaderResources(resources))
            {
                fallbackReason = VirtualShadowMapPrototypeFallbackReason
                    .MeshletShaderResourceUnavailable;
                return false;
            }

            if (m_VirtualShadowMapPrepareMeshletPageRequestsKernel < 0
                || m_VirtualShadowMapCullMeshletsToPagesKernel < 0)
            {
                fallbackReason = VirtualShadowMapPrototypeFallbackReason
                    .PageManagementUnavailable;
                return false;
            }

            if (m_PrimitiveShadowDrawSet == null
                || m_StaticPrimitiveShadowDrawSet == null
                || m_DynamicPrimitiveShadowDrawSet == null)
            {
                fallbackReason = VirtualShadowMapPrototypeFallbackReason
                    .MeshletDrawSetUnavailable;
                return false;
            }

            for (int materialIndex = 0;
                 materialIndex < m_Materials.Length;
                 materialIndex++)
            {
                if (m_Materials[materialIndex] == null)
                {
                    fallbackReason = VirtualShadowMapPrototypeFallbackReason
                        .MeshletShaderResourceUnavailable;
                    return false;
                }
            }

            if (system.UsesVirtualTexture
                && !GPUDrivenVirtualTextureBindingUtility.TryGetBinding(
                    m_VirtualTextureFrameData,
                    out _))
            {
                fallbackReason = VirtualShadowMapPrototypeFallbackReason
                    .VirtualTextureUnavailable;
                return false;
            }

            return true;
        }

        private bool TryPrepareMeshletPageDraws(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            GraphicsBuffer sourceRequestsBuffer,
            GraphicsBuffer sourceArgsBuffer,
            int casterLayer,
            out GraphicsBuffer pageRequestsBuffer,
            out GraphicsBuffer pageArgsBuffer)
        {
            using var pageScope = new ProfilingScope(nativeCmd, VSMProfiling.PageCull);
            pageRequestsBuffer = null;
            pageArgsBuffer = null;
            if (system == null
                || sourceRequestsBuffer == null
                || sourceArgsBuffer == null
                || VirtualShadowMapPrototypeRuntime.Projections.Count <= 0
                || m_ShadowData?.viewProjMatrices == null
                || m_VirtualShadowMapPageManagementCompute == null
                || m_VirtualShadowMapPrepareMeshletPageRequestsKernel < 0
                || m_VirtualShadowMapCullMeshletsToPagesKernel < 0)
            {
                return false;
            }

            VividGPUDrivenBufferSet sceneBuffers = system.BufferSet;
            if (sceneBuffers?.InstanceDataBuffer == null
                || sceneBuffers.MeshletsBuffer == null
                || !VirtualShadowMapPrototypeRuntime
                    .EnsureMeshletPageRequestCapacity(
                        sourceRequestsBuffer.count))
            {
                return false;
            }

            pageRequestsBuffer =
                VirtualShadowMapPrototypeRuntime.MeshletPageRequests;
            pageArgsBuffer =
                VirtualShadowMapPrototypeRuntime.MeshletPageIndirectArgs;
            if (pageRequestsBuffer == null || pageArgsBuffer == null)
                return false;

            int sourceRequestsPerCascadeCapacity =
                sourceRequestsBuffer.count / VirtualShadowMapPrototypeRuntime.Projections.Count;
            if (sourceRequestsPerCascadeCapacity <= 0)
                return false;

            ComputeShader compute = m_VirtualShadowMapPageManagementCompute;
            nativeCmd.SetComputeIntParam(
                compute,
                VSMPrototypeSourceRequestsPerCascadeCapacityId,
                sourceRequestsPerCascadeCapacity);
            nativeCmd.SetComputeIntParam(
                compute,
                VSMPrototypeCasterLayerId,
                casterLayer);
            nativeCmd.SetComputeBufferParam(compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VirtualShadowMapProjectionSet.BufferId,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            SetVirtualShadowMapPageManagementParameters(nativeCmd);

            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                VSMPrototypePhysicalPageOwnersId,
                VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                VSMPrototypePageTableId,
                VirtualShadowMapPrototypeRuntime.PageTable);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                VSMPrototypePageMetadataId,
                VirtualShadowMapPrototypeRuntime.PageMetadata);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                VSMPrototypeMeshletRasterPagesId,
                VirtualShadowMapPrototypeRuntime.MeshletRasterPages);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                VSMPrototypeSourceMeshletIndirectArgsId,
                sourceArgsBuffer);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                VSMPrototypeMeshletPageIndirectArgsId,
                pageArgsBuffer);
            nativeCmd.DispatchCompute(
                compute,
                m_VirtualShadowMapPrepareMeshletPageRequestsKernel,
                1,
                1,
                1);

            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypeSourceMeshletRequestsId,
                sourceRequestsBuffer);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypeSourceMeshletIndirectArgsId,
                sourceArgsBuffer);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypeMeshletPageRequestsId,
                pageRequestsBuffer);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypeMeshletPageIndirectArgsId,
                pageArgsBuffer);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypeMeshletRasterPagesId,
                VirtualShadowMapPrototypeRuntime.MeshletRasterPages);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypePageTableId,
                VirtualShadowMapPrototypeRuntime.PageTable);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VSMPrototypePageMetadataId,
                VirtualShadowMapPrototypeRuntime.PageMetadata);
            nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VirtualShadowMapCullMeshletsToPagesKernel,
                VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
            nativeCmd.SetComputeIntParam(compute, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsEnabledId, 1);
            nativeCmd.SetComputeBufferParam(compute, m_VirtualShadowMapCullMeshletsToPagesKernel,
                VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, VirtualShadowMapPrototypeRuntime.UncachedPageRectBounds);
            nativeCmd.SetComputeIntParam(compute, VirtualShadowMapPrototypeRuntime.PageCullHierarchyEnabledId, 1);
            nativeCmd.SetComputeBufferParam(compute, m_VirtualShadowMapCullMeshletsToPagesKernel,
                VirtualShadowMapPrototypeRuntime.PageCullHierarchyId, VirtualShadowMapPrototypeRuntime.PageCullHierarchy);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VividGPUDrivenShaderIDs._InstanceData,
                sceneBuffers.InstanceDataBuffer);
            nativeCmd.SetComputeIntParam(
                compute,
                VividGPUDrivenShaderIDs._InstanceDataCount,
                sceneBuffers.InstanceCount);
            nativeCmd.SetComputeBufferParam(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                VividGPUDrivenShaderIDs._Meshlets,
                sceneBuffers.MeshletsBuffer);
            nativeCmd.SetComputeIntParam(
                compute,
                VividGPUDrivenShaderIDs._MeshletCount,
                sceneBuffers.MeshletCount);
            nativeCmd.DispatchCompute(
                compute,
                m_VirtualShadowMapCullMeshletsToPagesKernel,
                CoreUtils.DivRoundUp(
                    sourceRequestsPerCascadeCapacity,
                    64),
                VirtualShadowMapPrototypeRuntime.Projections.Count,
                RendererListCount);
            return true;
        }

        private void DrawMeshletVirtualShadowMapPages(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            GraphicsBuffer requestsBuffer,
            GraphicsBuffer argsBuffer,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding)
        {
            for (int rendererListIndex = 0;
                 rendererListIndex < m_Materials.Length;
                 rendererListIndex++)
            {
                VividRendererListID batchKey =
                    (VividRendererListID)rendererListIndex;
                if (!system.IsShadowRendererBatchActive(batchKey))
                    continue;

                Material material =
                    m_Materials[rendererListIndex];
                if (material == null)
                    continue;
                if (!virtualTextureReady
                    && (batchKey & VividRendererListID.AlphaTest) != 0)
                {
                    continue;
                }

                m_DrawProperties.Clear();
                m_DrawProperties.SetBuffer(
                    VSMPrototypeMeshletPageRequestsId,
                    requestsBuffer);
                m_DrawProperties.SetBuffer(
                    s_UnityIndirectDrawArgsId,
                    argsBuffer);
                m_DrawProperties.SetBuffer(
                    VSMPrototypeMeshletRasterPagesId,
                    VirtualShadowMapPrototypeRuntime.MeshletRasterPages);
                if (system.UsesVirtualTexture && virtualTextureReady)
                {
                    GPUDrivenVirtualTextureBindingUtility.BindSpaceProperties(
                        m_DrawProperties,
                        virtualTextureBinding,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex,
                        m_VirtualTextureFrameData.AdaptiveMipBias);
                }

                for (int requestMode = 0; requestMode < 2; requestMode++)
                {
                    m_DrawProperties.SetInteger(
                        s_UnityBaseCommandIdId,
                        requestMode * RendererListCount + rendererListIndex);
                    nativeCmd.DrawProceduralIndirect(
                        Matrix4x4.identity,
                        material,
                        0,
                        MeshTopology.Triangles,
                        argsBuffer,
                        VividGPUDrivenCullingBuffers.GetIndirectDrawArgsByteOffset(
                            requestMode,
                            rendererListIndex),
                        m_DrawProperties);
                }
            }
        }

        private bool DrawVirtualShadowMapPrototypePages(
            CommandBuffer nativeCmd,
            in MeshletShadowRecordContext meshletContext,
            out VirtualShadowMapPrototypeFallbackReason fallbackReason)
        {
            fallbackReason =
                VirtualShadowMapPrototypeFallbackReason.PhysicalResourceUnavailable;
            RTHandle staticPhysicalPage =
                VirtualShadowMapPrototypeRuntime.StaticPhysicalPage;
            RTHandle dynamicPhysicalPage =
                VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage;
            RTHandle rasterDepth = VirtualShadowMapPrototypeRuntime.RasterDepth;
            GraphicsBuffer pageTable = VirtualShadowMapPrototypeRuntime.PageTable;
            GraphicsBuffer pageMetadata =
                VirtualShadowMapPrototypeRuntime.PageMetadata;
            GraphicsBuffer physicalPageOwners =
                VirtualShadowMapPrototypeRuntime.PhysicalPageOwners;
            GraphicsBuffer allocatorCounters =
                VirtualShadowMapPrototypeRuntime.AllocatorCounters;
            int pageTableEntryCount = VirtualShadowMapPrototypeRuntime.PageTableEntryCount;
            int physicalPageCapacity =
                VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity;
            if (staticPhysicalPage == null
                || dynamicPhysicalPage == null
                || rasterDepth == null
                || pageTable == null
                || pageMetadata == null
                || physicalPageOwners == null
                || allocatorCounters == null
                || pageTableEntryCount <= 0
                || physicalPageCapacity <= 0
                || !CanManageVirtualShadowMapPages())
            {
                return false;
            }

            using (new ProfilingScope(nativeCmd, VSMProfiling.Allocate))
            {
                var shader = m_VirtualShadowMapPageManagementCompute;
                BindVirtualShadowMapPageManagementBuffers(
                    nativeCmd,
                    m_VirtualShadowMapAllocatePagesKernel,
                    pageTable,
                    pageMetadata,
                    physicalPageOwners,
                    allocatorCounters);
                using (new ProfilingScope(nativeCmd, VSMProfiling.AllocationPrepare))
                {
                    nativeCmd.SetComputeBufferParam(shader, m_VSMPrepareAllocationKernel,
                        VSMPrototypePageMetadataId, pageMetadata);
                    nativeCmd.SetComputeBufferParam(shader, m_VSMPrepareAllocationKernel,
                        VSMPageRequestFlagsId, VirtualShadowMapPrototypeRuntime.PageRequestFlags);
                    nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMPrepareAllocationKernel,
                        VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                    nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMPrepareAllocationKernel,
                        VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasksId, VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasks);
                    nativeCmd.SetComputeBufferParam(shader, m_VSMPrepareAllocationKernel,
                        VSMAllocationRequestsId, VirtualShadowMapPrototypeRuntime.AllocationRequests);
                    nativeCmd.DispatchCompute(shader, m_VSMPrepareAllocationKernel,
                        CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.AllocationRequests.count, 64), 1, 1);
                }
                using var allocationCommitScope = new ProfilingScope(nativeCmd, VSMProfiling.AllocationCommit);
                nativeCmd.SetComputeBufferParam(shader, m_VirtualShadowMapAllocatePagesKernel,
                    VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasksId, VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasks);
                nativeCmd.SetComputeBufferParam(shader, m_VirtualShadowMapAllocatePagesKernel,
                    VSMPageRequestFlagsId, VirtualShadowMapPrototypeRuntime.PageRequestFlags);
                nativeCmd.SetComputeBufferParam(shader, m_VirtualShadowMapAllocatePagesKernel,
                    VSMAllocationRequestsId, VirtualShadowMapPrototypeRuntime.AllocationRequests);
                nativeCmd.SetComputeBufferParam(shader, m_VirtualShadowMapAllocatePagesKernel,
                    VirtualShadowMapReceiverQuality.PressureRWId, VirtualShadowMapPrototypeRuntime.PagePressure);
                nativeCmd.DispatchCompute(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapAllocatePagesKernel,
                    1,
                    1,
                    1);
            }

            nativeCmd.SetGlobalBuffer(VirtualShadowMapProjectionSet.BufferId,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            nativeCmd.SetGlobalBuffer(VSMPrototypePageTableId, pageTable);
            nativeCmd.SetGlobalBuffer(VSMPrototypePageMetadataId, pageMetadata);
            nativeCmd.SetGlobalInt(VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, 1);
            nativeCmd.SetGlobalBuffer(VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
            nativeCmd.SetGlobalInt(
                VSMPrototypePageSizeId,
                VirtualShadowMapPrototypeRuntime.PageSize);
            nativeCmd.SetGlobalInt(
                VSMPrototypeVirtualResolutionId,
                VirtualShadowMapPrototypeRuntime.VirtualResolution);
            nativeCmd.SetGlobalInt(
                VSMPrototypePagesPerAxisId,
                VirtualShadowMapPrototypeRuntime.PagesPerAxis);
            nativeCmd.SetGlobalInt(
                VSMPrototypePhysicalPagesPerRowId,
                VirtualShadowMapPrototypeRuntime.PhysicalPagesPerRow);

            bool staticCacheHit = !m_VirtualShadowMapStaticPoolNeedsCacheRefresh
                && !VirtualShadowMapPrototypeRuntime.RequiresStaticCacheRefresh(
                    m_VirtualShadowMapPrototypeCacheKey);
            if (!staticCacheHit)
            {
                using var invalidationScope = new ProfilingScope(nativeCmd, VSMProfiling.Invalidate);
                bool requiresFullRefresh =
                    m_VirtualShadowMapStaticPoolNeedsFullRefresh
                    || VirtualShadowMapPrototypeRuntime
                        .RequiresFullStaticCacheRefresh(
                            m_VirtualShadowMapPrototypeCacheKey)
                    || m_VirtualShadowMapStaticInvalidationBoundsCount <= 0;
                if (requiresFullRefresh)
                {
                    nativeCmd.SetComputeBufferParam(
                        m_VirtualShadowMapPageManagementCompute,
                        m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel,
                        VSMPrototypePageMetadataId,
                        pageMetadata);
                    SetVirtualShadowMapPageManagementParameters(nativeCmd);
                    nativeCmd.DispatchCompute(
                        m_VirtualShadowMapPageManagementCompute,
                        m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel,
                        CoreUtils.DivRoundUp(pageTableEntryCount, 64),
                        1,
                        1);
                }
                else
                {
                    nativeCmd.SetComputeBufferParam(
                        m_VirtualShadowMapPageManagementCompute,
                        m_VirtualShadowMapInvalidateStaticPagesKernel,
                        VSMPrototypePageMetadataId,
                        pageMetadata);
                    nativeCmd.SetComputeBufferParam(
                        m_VirtualShadowMapPageManagementCompute,
                        m_VirtualShadowMapInvalidateStaticPagesKernel,
                        VSMPrototypeStaticInvalidationBoundsId,
                        VirtualShadowMapPrototypeRuntime.StaticInvalidationBounds);
                    nativeCmd.SetComputeIntParam(
                        m_VirtualShadowMapPageManagementCompute,
                        VSMPrototypeStaticInvalidationBoundsCountId,
                        m_VirtualShadowMapStaticInvalidationBoundsCount);
                    SetVirtualShadowMapPageManagementParameters(nativeCmd);
                    nativeCmd.DispatchCompute(
                        m_VirtualShadowMapPageManagementCompute,
                        m_VirtualShadowMapInvalidateStaticPagesKernel,
                        m_VirtualShadowMapStaticInvalidationBoundsCount,
                        VirtualShadowMapPrototypeRuntime.Projections.Count,
                        1);
                }
            }

            using (new ProfilingScope(nativeCmd, VSMProfiling.DynamicInvalidate))
            {
                bool full = m_DynamicPoolNeedsFullRefresh
                    || VirtualShadowMapPrototypeRuntime.RequiresFullDynamicCacheRefresh(
                        m_VirtualShadowMapPrototypeCacheKey, m_HasUntrackedDynamicCasters);
                if (full || m_DynamicInvalidationBoundsCount > 0)
                {
                    var shader = m_VirtualShadowMapPageManagementCompute;
                    int kernel = full ? m_VSMMarkDynamicPagesDirtyKernel : m_VSMInvalidateDynamicPagesKernel;
                    nativeCmd.SetComputeBufferParam(shader, kernel, VSMPrototypePageMetadataId, pageMetadata);
                    if (!full)
                    {
                        nativeCmd.SetComputeBufferParam(shader, kernel, VSMPrototypeStaticInvalidationBoundsId,
                            VirtualShadowMapPrototypeRuntime.DynamicInvalidationBounds);
                        nativeCmd.SetComputeIntParam(shader, VSMPrototypeStaticInvalidationBoundsCountId, m_DynamicInvalidationBoundsCount);
                    }
                    SetVirtualShadowMapPageManagementParameters(nativeCmd);
                    nativeCmd.DispatchCompute(shader, kernel,
                        full ? CoreUtils.DivRoundUp(pageTableEntryCount, 64) : m_DynamicInvalidationBoundsCount,
                        full ? 1 : VirtualShadowMapPrototypeRuntime.Projections.Count, 1);
                }
            }

            using (new ProfilingScope(nativeCmd, VSMProfiling.BuildPageWorkLists))
            {
                var shader = m_VirtualShadowMapPageManagementCompute;
                nativeCmd.SetComputeIntParam(shader, VSMPageUpdateBudgetId, m_PageUpdateBudget);
                nativeCmd.SetComputeBufferParam(shader, m_VSMBuildPageWorkListsKernel,
                    VSMPrototypePageMetadataId, pageMetadata);
                nativeCmd.SetComputeBufferParam(shader, m_VSMBuildPageWorkListsKernel,
                    VSMPrototypePageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
                nativeCmd.SetComputeBufferParam(shader, m_VSMBuildPageWorkListsKernel,
                    VSMPageRequestFlagsId, VirtualShadowMapPrototypeRuntime.PageRequestFlags);
                nativeCmd.SetComputeBufferParam(shader, m_VSMBuildPageWorkListsKernel,
                    VSMPrototypePhysicalPageOwnersId, physicalPageOwners);
                nativeCmd.SetComputeBufferParam(shader, m_VSMBuildPageWorkListsKernel,
                    VSMPageWorkListRWId, VirtualShadowMapPrototypeRuntime.PageWorkList);
                nativeCmd.SetComputeBufferParam(shader, m_VSMBuildPageWorkListsKernel,
                    VSMPageWorkDispatchArgsRWId, VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs);
                SetVirtualShadowMapPageManagementParameters(nativeCmd);
                nativeCmd.DispatchCompute(shader, m_VSMBuildPageWorkListsKernel, 1, 1, 1);
            }

            RecordPageCullHierarchy(nativeCmd);

            using (new ProfilingScope(nativeCmd, VSMProfiling.Clear))
            {
                nativeCmd.SetComputeBufferParam(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapClearPhysicalPagesKernel,
                    VSMPrototypePageMetadataId,
                    pageMetadata);
                nativeCmd.SetComputeBufferParam(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapClearPhysicalPagesKernel,
                    VSMPrototypePhysicalPageOwnersId,
                    physicalPageOwners);
                nativeCmd.SetComputeTextureParam(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapClearPhysicalPagesKernel,
                    VSMPrototypeStaticPhysicalPageRWId,
                    staticPhysicalPage);
                nativeCmd.SetComputeTextureParam(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapClearPhysicalPagesKernel,
                    VSMPrototypeDynamicPhysicalPageRWId,
                    dynamicPhysicalPage);
                SetVirtualShadowMapPageManagementParameters(nativeCmd);
                nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapClearPhysicalPagesKernel, VSMPageWorkListId,
                    VirtualShadowMapPrototypeRuntime.PageWorkList);
                nativeCmd.DispatchCompute(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapClearPhysicalPagesKernel,
                    VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs,
                    VirtualShadowMapPrototypeRuntime.ClearWorkArgsOffset);
            }

            bool canDrawStaticMeshletCasters = false;
            GraphicsBuffer staticRequestsBuffer = null;
            GraphicsBuffer staticArgsBuffer = null;
            GraphicsBuffer staticPageRequestsBuffer = null;
            GraphicsBuffer staticPageArgsBuffer = null;
            if (m_HasMeshletShadowCasters)
            {
                using var cullScope = new ProfilingScope(nativeCmd, VSMProfiling.StaticCull);
                if (!TryPrepareMeshletShadowPoolDraws(
                    nativeCmd,
                    meshletContext.System,
                    meshletContext.StaticDrawSet,
                    out staticRequestsBuffer,
                    out staticArgsBuffer,
                    out bool hasStaticDraws, m_ClipmapCullingContexts, VirtualShadowMapPrototypeRuntime.Projections.Count, in m_ClipmapLODContext))
                {
                    fallbackReason = VirtualShadowMapPrototypeFallbackReason
                        .RecordPreparationFailed;
                    return false;
                }

                canDrawStaticMeshletCasters = hasStaticDraws
                    && HasRenderableMeshletShadowBatch(
                        meshletContext.System,
                        meshletContext.VirtualTextureReady);
                if (canDrawStaticMeshletCasters
                    && !TryPrepareMeshletPageDraws(
                        nativeCmd,
                        meshletContext.System,
                        staticRequestsBuffer,
                        staticArgsBuffer,
                        casterLayer: 0,
                        out staticPageRequestsBuffer,
                        out staticPageArgsBuffer))
                {
                    fallbackReason = VirtualShadowMapPrototypeFallbackReason
                        .RecordPreparationFailed;
                    return false;
                }
            }

            if (canDrawStaticMeshletCasters)
            {
                using var rasterScope = new ProfilingScope(nativeCmd, VSMProfiling.StaticRaster);
                nativeCmd.SetGlobalInt(VSMPrototypeCasterLayerId, 0);
                using (new ProfilingScope(nativeCmd, VSMProfiling.StaticRasterClear))
                {
                    CoreUtils.SetRenderTarget(
                        nativeCmd,
                        rasterDepth,
                        ClearFlag.Depth,
                        Color.black,
                        depthSlice: -1);
                }
                using (new ProfilingScope(nativeCmd, VSMProfiling.StaticRasterDraw))
                {
                    nativeCmd.SetRandomWriteTarget(0, staticPhysicalPage);
                    DrawMeshletVirtualShadowMapPages(
                        nativeCmd,
                        meshletContext.System,
                        staticPageRequestsBuffer,
                        staticPageArgsBuffer,
                        meshletContext.VirtualTextureReady,
                        meshletContext.VirtualTextureBinding);
                    nativeCmd.ClearRandomWriteTargets();
                }
            }

            bool canDrawDynamicMeshletCasters = false;
            GraphicsBuffer dynamicRequestsBuffer = null;
            GraphicsBuffer dynamicArgsBuffer = null;
            GraphicsBuffer dynamicPageRequestsBuffer = null;
            GraphicsBuffer dynamicPageArgsBuffer = null;
            if (m_HasMeshletShadowCasters)
            {
                using var cullScope = new ProfilingScope(nativeCmd, VSMProfiling.DynamicCull);
                if (!TryPrepareMeshletShadowPoolDraws(
                    nativeCmd,
                    meshletContext.System,
                    meshletContext.DynamicDrawSet,
                    out dynamicRequestsBuffer,
                    out dynamicArgsBuffer,
                    out bool hasDynamicDraws, m_ClipmapCullingContexts, VirtualShadowMapPrototypeRuntime.Projections.Count, in m_ClipmapLODContext))
                {
                    fallbackReason = VirtualShadowMapPrototypeFallbackReason
                        .RecordPreparationFailed;
                    return false;
                }

                canDrawDynamicMeshletCasters = hasDynamicDraws
                    && HasRenderableMeshletShadowBatch(
                        meshletContext.System,
                        meshletContext.VirtualTextureReady);
                if (canDrawDynamicMeshletCasters
                    && !TryPrepareMeshletPageDraws(
                        nativeCmd,
                        meshletContext.System,
                        dynamicRequestsBuffer,
                        dynamicArgsBuffer,
                        casterLayer: 1,
                        out dynamicPageRequestsBuffer,
                        out dynamicPageArgsBuffer))
                {
                    fallbackReason = VirtualShadowMapPrototypeFallbackReason
                        .RecordPreparationFailed;
                    return false;
                }
            }

            if (canDrawDynamicMeshletCasters)
            using (new ProfilingScope(nativeCmd, VSMProfiling.DynamicRaster))
            {
                nativeCmd.SetGlobalInt(VSMPrototypeCasterLayerId, 1);
                using (new ProfilingScope(nativeCmd, VSMProfiling.DynamicRasterClear))
                {
                    CoreUtils.SetRenderTarget(
                        nativeCmd,
                        rasterDepth,
                        ClearFlag.Depth,
                        Color.black,
                        depthSlice: -1);
                }
                using (new ProfilingScope(nativeCmd, VSMProfiling.DynamicRasterDraw))
                {
                    nativeCmd.SetRandomWriteTarget(0, dynamicPhysicalPage);
                    if (canDrawDynamicMeshletCasters)
                    {
                        DrawMeshletVirtualShadowMapPages(
                            nativeCmd,
                            meshletContext.System,
                            dynamicPageRequestsBuffer,
                            dynamicPageArgsBuffer,
                            meshletContext.VirtualTextureReady,
                            meshletContext.VirtualTextureBinding);
                    }
                    nativeCmd.ClearRandomWriteTargets();
                }
            }
            if (m_HasUnityShadowCasters)
            {
                using var unityScope = new ProfilingScope(nativeCmd, VSMProfiling.UnityRaster);
                nativeCmd.SetGlobalInt(VSMPrototypeCasterLayerId, 1);
                int resolution = VirtualShadowMapPrototypeRuntime.VirtualResolution;
                int tileSize = VirtualShadowMapPrototypeRuntime.UnityRasterDepth.rt.width;
                int tilesPerAxis = CoreUtils.DivRoundUp(resolution, tileSize);
                nativeCmd.SetGlobalInt(VSMUnityRasterEnabledId, 1);
                for (int projectionIndex = 0;
                     projectionIndex < VirtualShadowMapPrototypeRuntime.Projections.Count;
                     projectionIndex++)
                {
                    // Unity shadow renderer lists are single-use per frame.
                    // Each coarse raster tile needs its own list (not one per page).
                    var settings = m_ClipmapDrawSettings[projectionIndex];
                    nativeCmd.SetGlobalInt(VSMProjectionIndexId, projectionIndex);
                    nativeCmd.EnableKeyword(s_VirtualShadowMapCasterKeyword);
                    for (int y = 0; y < tilesPerAxis; y++)
                    for (int x = 0; x < tilesPerAxis; x++)
                    {
                        var rendererList = m_RenderContext.CreateShadowRendererList(ref settings);
                        int originX = x * tileSize;
                        int originY = y * tileSize;
                        CoreUtils.SetRenderTarget(nativeCmd,
                            VirtualShadowMapPrototypeRuntime.UnityRasterDepth, ClearFlag.Depth);
                        nativeCmd.SetGlobalMatrix(VSMRasterViewProjectionId,
                            VirtualShadowMapPrototypeRuntime.Projections.GetRasterMatrix(
                                projectionIndex, resolution, tileSize, originX, originY,
                                SystemInfo.graphicsUVStartsAtTop));
                        nativeCmd.SetGlobalVector(VSMRasterOriginId,
                            new Vector4(originX, originY, 0, 0));
                        nativeCmd.SetRandomWriteTarget(0, dynamicPhysicalPage);
                        nativeCmd.DrawRendererList(rendererList);
                        nativeCmd.ClearRandomWriteTargets();
                    }
                    nativeCmd.DisableKeyword(s_VirtualShadowMapCasterKeyword);
                }
                nativeCmd.SetGlobalInt(VSMUnityRasterEnabledId, 0);
            }

            using (new ProfilingScope(nativeCmd, VSMProfiling.Occupancy))
            {
                nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute,
                    m_VSMReducePageOccupancyKernel, VSMPageWorkListId,
                    VirtualShadowMapPrototypeRuntime.PageWorkList);
                nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute,
                    m_VSMReducePageOccupancyKernel, VSMPrototypePageMetadataId, pageMetadata);
                nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute,
                    m_VSMReducePageOccupancyKernel, VSMPrototypePhysicalPageOwnersId, physicalPageOwners);
                nativeCmd.SetComputeTextureParam(m_VirtualShadowMapPageManagementCompute,
                    m_VSMReducePageOccupancyKernel, VSMPrototypeStaticPhysicalPageId, staticPhysicalPage);
                nativeCmd.SetComputeTextureParam(m_VirtualShadowMapPageManagementCompute,
                    m_VSMReducePageOccupancyKernel, VSMPrototypeDynamicPhysicalPageId, dynamicPhysicalPage);
                SetVirtualShadowMapPageManagementParameters(nativeCmd);
                nativeCmd.DispatchCompute(m_VirtualShadowMapPageManagementCompute,
                    m_VSMReducePageOccupancyKernel, VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs,
                    VirtualShadowMapPrototypeRuntime.OccupancyWorkArgsOffset);
            }

            using (new ProfilingScope(nativeCmd, VSMProfiling.Finalize))
            {
                nativeCmd.SetComputeBufferParam(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapFinalizeDirtyPagesKernel,
                    VSMPrototypePageMetadataId,
                    pageMetadata);
                SetVirtualShadowMapPageManagementParameters(nativeCmd);
                nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VirtualShadowMapFinalizeDirtyPagesKernel,
                    VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VirtualShadowMapFinalizeDirtyPagesKernel,
                    VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasksId, VirtualShadowMapPrototypeRuntime.PhysicalReceiverMasks);
                nativeCmd.DispatchCompute(
                    m_VirtualShadowMapPageManagementCompute,
                    m_VirtualShadowMapFinalizeDirtyPagesKernel,
                    CoreUtils.DivRoundUp(pageTableEntryCount, 64),
                    1,
                    1);
            }

            if (staticCacheHit)
            {
                VirtualShadowMapPrototypeRuntime.TryUseCachedStaticPages(
                    m_VirtualShadowMapPrototypeCacheKey);
            }
            else
            {
                VirtualShadowMapPrototypeRuntime.CommitStaticCache(
                    m_VirtualShadowMapPrototypeCacheKey);
            }
            if (meshletContext.System != null)
            {
                meshletContext.System.PrimitiveScene
                    .AcknowledgeStaticShadowInvalidations(
                        m_VirtualShadowMapPrototypeCacheKey.StaticShadowRevision);
                meshletContext.System.PrimitiveScene.AcknowledgeDynamicShadowInvalidations(m_DynamicShadowRevision);
            }
            VirtualShadowMapPrototypeRuntime.CommitDynamicCache(m_VirtualShadowMapPrototypeCacheKey,
                m_DynamicShadowRevision, m_HasUntrackedDynamicCasters);
            VirtualShadowMapPrototypeRuntime.CommitDynamicUnityBounds(
                m_HasUnityShadowCasters && !m_HasUntrackedDynamicCasters, m_ShadowData.unityShadowCasterBounds);
            VirtualShadowMapPrototypeRuntime.MarkDynamicPoolRefreshed();
            VirtualShadowMapPrototypeRuntime.MarkActive();
            VirtualShadowMapPrototypeRuntime.MarkPageDebugSnapshot(m_PageDebugCameraEntityId, m_FrameIndex);
            fallbackReason = VirtualShadowMapPrototypeFallbackReason.None;
            return true;
        }

        private void BindVirtualShadowMapPageManagementBuffers(
            CommandBuffer nativeCmd,
            int kernel,
            GraphicsBuffer pageTable,
            GraphicsBuffer pageMetadata,
            GraphicsBuffer physicalPageOwners,
            GraphicsBuffer allocatorCounters)
        {
            nativeCmd.SetComputeBufferParam(
                m_VirtualShadowMapPageManagementCompute,
                kernel,
                VSMPrototypeWritablePageTableId,
                pageTable);
            nativeCmd.SetComputeBufferParam(
                m_VirtualShadowMapPageManagementCompute,
                kernel,
                VSMPrototypePageMetadataId,
                pageMetadata);
            nativeCmd.SetComputeBufferParam(
                m_VirtualShadowMapPageManagementCompute,
                kernel,
                VSMPrototypePhysicalPageOwnersId,
                physicalPageOwners);
            nativeCmd.SetComputeBufferParam(
                m_VirtualShadowMapPageManagementCompute,
                kernel,
                VSMPrototypeAllocatorCountersId,
                allocatorCounters);
            SetVirtualShadowMapPageManagementParameters(nativeCmd);
        }

        private void SetVirtualShadowMapPageManagementParameters(
            CommandBuffer nativeCmd)
        {
            nativeCmd.SetComputeIntParam(m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeVirtualResolutionId, VirtualShadowMapPrototypeRuntime.VirtualResolution);
            nativeCmd.SetComputeIntParam(m_VirtualShadowMapPageManagementCompute,
                VSMPrototypePagesPerAxisId, VirtualShadowMapPrototypeRuntime.PagesPerAxis);
            nativeCmd.SetComputeIntParam(m_VirtualShadowMapPageManagementCompute,
                VirtualShadowMapProjectionSet.CountId,
                VirtualShadowMapPrototypeRuntime.Projections.Count);
            nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute,
                m_VirtualShadowMapInvalidateStaticPagesKernel,
                VirtualShadowMapProjectionSet.BufferId,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            nativeCmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute,
                m_VSMInvalidateDynamicPagesKernel, VirtualShadowMapProjectionSet.BufferId,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            nativeCmd.SetComputeIntParam(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypePageSizeId,
                VirtualShadowMapPrototypeRuntime.PageSize);
            nativeCmd.SetComputeIntParam(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypePhysicalPagesPerRowId,
                VirtualShadowMapPrototypeRuntime.PhysicalPagesPerRow);
            nativeCmd.SetComputeIntParam(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypePageTableEntryCountId,
                VirtualShadowMapPrototypeRuntime.PageTableEntryCount);
            nativeCmd.SetComputeIntParam(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypePhysicalPageCapacityId,
                VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity);
            nativeCmd.SetComputeIntParam(
                m_VirtualShadowMapPageManagementCompute,
                VSMPrototypeFeedbackFrameIndexId,
                m_FrameIndex);
        }

        private void RecordPageCullHierarchy(CommandBuffer cmd)
        {
            var shader = m_VirtualShadowMapPageManagementCompute;
            var hierarchy = VirtualShadowMapPrototypeRuntime.PageCullHierarchy;
            var bounds = VirtualShadowMapPrototypeRuntime.UncachedPageRectBounds;
            using (new ProfilingScope(cmd, VSMProfiling.ClearPageHierarchy))
            {
                cmd.SetComputeBufferParam(shader, m_VSMClearPageHierarchyKernel,
                    VirtualShadowMapPrototypeRuntime.PageCullHierarchyRWId, hierarchy);
                cmd.SetComputeBufferParam(shader, m_VSMClearPageHierarchyKernel,
                    VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsRWId, bounds);
                cmd.DispatchCompute(shader, m_VSMClearPageHierarchyKernel,
                    CoreUtils.DivRoundUp(Mathf.Max(hierarchy.count, bounds.count), 64), 1, 1);
            }
            using (new ProfilingScope(cmd, VSMProfiling.BuildPageHierarchy))
            {
                int kernel = m_VSMBuildPageHierarchyKernel;
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.PageCullHierarchyRWId, hierarchy);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsRWId, bounds);
                cmd.SetComputeBufferParam(shader, kernel, VSMPrototypePageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
                cmd.SetComputeBufferParam(shader, kernel, VSMPrototypePageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.SetComputeBufferParam(shader, kernel, VSMPrototypePhysicalPageOwnersId, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.PageReceiverMasksId, VirtualShadowMapPrototypeRuntime.PageReceiverMasks);
                cmd.DispatchCompute(shader, kernel, CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, 64), 1, 1);
            }
        }

        private bool CanManageVirtualShadowMapPages()
        {
            return m_VirtualShadowMapPageManagementCompute != null
                && m_VSMPrepareAllocationKernel >= 0
                && m_VSMMarkCoarsePagesKernel >= 0
                && m_VSMClearPageHierarchyKernel >= 0
                && m_VSMBuildPageHierarchyKernel >= 0
                && m_VSMMarkDynamicPagesDirtyKernel >= 0
                && m_VSMInvalidateDynamicPagesKernel >= 0
                && m_VirtualShadowMapAllocatePagesKernel >= 0
                && m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel >= 0
                && m_VirtualShadowMapInvalidateStaticPagesKernel >= 0
                && m_VirtualShadowMapClearPhysicalPagesKernel >= 0
                && m_VirtualShadowMapFinalizeDirtyPagesKernel >= 0
                && m_VSMReducePageOccupancyKernel >= 0
                && m_VSMBuildPageWorkListsKernel >= 0
                && (!m_HasMeshletShadowCasters
                    || (m_VirtualShadowMapPrepareMeshletPageRequestsKernel >= 0
                        && m_VirtualShadowMapCullMeshletsToPagesKernel >= 0));
        }

        private static int FindKernelOrInvalid(
            ComputeShader shader,
            string kernelName)
        {
            return shader != null && shader.HasKernel(kernelName)
                ? shader.FindKernel(kernelName)
                : -1;
        }

        public override void Dispose()
        {
            base.Dispose();
            m_PageTable.ClearImportedBuffer();
            m_VirtualShadowMapPrototypeActive = false;
            VirtualShadowMapPrototypeRuntime.ReleaseResources();
        }
    }
}
