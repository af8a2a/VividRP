using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.ObjectDispatching;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.PrimitiveScene;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.RenderPass.Core
{
    internal enum VirtualShadowMapPrototypeFrameState
    {
        Disabled = 0,
        Prepared = 1,
        Refreshing = 2,
        Cached = 3,
        Active = 4,
        Fallback = 5,
    }

    internal enum VirtualShadowMapPrototypeFallbackReason
    {
        None = 0,
        InvalidCamera = 1,
        UnityCasterIncompatible = 2,
        UnsupportedPlatform = 3,
        ResourceUnavailable = 4,
        InvalidCacheKey = 5,
        GPUDrivenUnavailable = 6,
        MeshletShaderResourceUnavailable = 7,
        MeshletDrawSetUnavailable = 8,
        VirtualTextureUnavailable = 9,
        RecordPreparationFailed = 10,
        PhysicalResourceUnavailable = 11,
        ReceiverFeedbackUnavailable = 12,
        PageManagementUnavailable = 13,
    }

    public sealed class CSMShadowPass : UnsafePass
    {
        internal const string ShadowCasterShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferShadowCasterPass";

        private const int RendererListCount = (int)VividRendererListID.Count;

        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_UnityIndirectDrawArgsId = Shader.PropertyToID("unity_IndirectDrawArgs");
        private static readonly int s_UnityBaseCommandIdId = Shader.PropertyToID("unity_BaseCommandID");
        private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        private static readonly int ShadowMatricesConstantBufferId =
            Shader.PropertyToID("ShaderVariablesShadowMatrices");
        private static readonly int ShadowCascadeIndexId = Shader.PropertyToID("_VividShadowCascadeIndex");
        private static readonly int VSMPrototypePageTableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int VSMPrototypeWritablePageTableId = Shader.PropertyToID("_VSMPrototypeWritablePageTable");
        private static readonly int VSMPrototypePageMetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly int VSMPrototypePhysicalPageOwnersId = Shader.PropertyToID("_VSMPrototypePhysicalPageOwners");
        private static readonly int VSMPrototypeAllocatorCountersId = Shader.PropertyToID("_VSMPrototypeAllocatorCounters");
        private static readonly int VSMPrototypeStaticPhysicalPageRWId = Shader.PropertyToID("_VSMPrototypeStaticPhysicalPageRW");
        private static readonly int VSMPrototypeDynamicPhysicalPageRWId = Shader.PropertyToID("_VSMPrototypeDynamicPhysicalPageRW");
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
        private static readonly int VSMPreviousPageTableId = Shader.PropertyToID("_VSMPreviousPageTable");
        private static readonly int VSMPreviousPageMetadataId = Shader.PropertyToID("_VSMPreviousPageMetadata");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private const string AlphaTestKeywordName = "_ALPHATEST_ON";
        private const string VirtualShadowMapCasterKeywordName = "VIVID_VSM_CASTER";
        private const string VirtualShadowMapPageCasterKeywordName = "VIVID_VSM_PAGE_CASTER";
        private const string VSMPrototypeAllocatePagesKernelName = "VSMPrototypeAllocatePages";
        private const string VSMPrototypeMarkAllAllocatedPagesDirtyKernelName = "VSMPrototypeMarkAllAllocatedPagesDirty";
        private const string VSMPrototypeInvalidateStaticPagesKernelName = "VSMPrototypeInvalidateStaticPages";
        private const string VSMPrototypeClearPhysicalPagesKernelName = "VSMPrototypeClearPhysicalPages";
        private const string VSMPrototypeFinalizeDirtyPagesKernelName = "VSMPrototypeFinalizeDirtyPages";
        private const string VSMPrototypePrepareMeshletPageRequestsKernelName = "VSMPrototypePrepareMeshletPageRequests";
        private const string VSMPrototypeCullMeshletsToPagesKernelName = "VSMPrototypeCullMeshletsToPages";
        private static readonly GlobalKeyword s_VirtualShadowMapCasterKeyword =
            GlobalKeyword.Create(VirtualShadowMapCasterKeywordName);

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowAtlas;

        private readonly Material[] m_Materials = new Material[RendererListCount];
        private readonly Material[] m_VirtualShadowMapPrototypeMaterials =
            new Material[RendererListCount];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly ShadowDrawingSettings[] m_ShadowDrawSettings =
            new ShadowDrawingSettings[VividShadowData.MaxCascadeCount];
        private readonly VividGPUCullingContext[] m_ShadowCullingContexts =
            new VividGPUCullingContext[VividShadowData.MaxCascadeCount];
        private readonly VividGPUCullingContext[] m_ClipmapCullingContexts =
            new VividGPUCullingContext[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly ShadowDrawingSettings[] m_ClipmapDrawSettings =
            new ShadowDrawingSettings[VirtualShadowMapClipmapLayout.MaxLevels];
        private VividGPULODSelectionContext m_ClipmapLODContext;
        private int m_VSMRemapPagesKernel = -1;
        private int m_VSMResetOwnersKernel = -1;
        private ShadowMatricesConstantBuffer m_ShadowMatrices;
        private readonly float[] m_VirtualTextureSpaceParams =
            new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets =
            new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks =
            new Vector4[VTStackDesc.MaxLayerCount];

        private bool m_IsActive;
        private bool m_MeshletRenderingActive;
        private int m_CascadeCount;
        private int m_CascadeResolution;
        private int m_MainLightVisibleIndex = -1;
        private bool m_HasUnityShadowCasters;
        private bool m_HasMeshletShadowCasters;
        private bool m_VirtualShadowMapPrototypeActive;
        private bool m_VirtualShadowMapStaticPoolNeedsCacheRefresh;
        private bool m_VirtualShadowMapStaticPoolNeedsFullRefresh;
        private int m_VirtualShadowMapStaticInvalidationBoundsCount;
        private float m_SlopeScaleDepthBias;
        private VirtualShadowMapPrototypeCacheKey m_VirtualShadowMapPrototypeCacheKey;

        private CullingResults m_CullingResults;
        private ScriptableRenderContext m_RenderContext;
        private VividShadowData m_ShadowData;
        private Camera m_LODCamera;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private VividPrimitiveDrawSet m_PrimitiveShadowDrawSet;
        private VividPrimitiveDrawSet m_StaticPrimitiveShadowDrawSet;
        private VividPrimitiveDrawSet m_DynamicPrimitiveShadowDrawSet;
        private VividGPULODSelectionContext m_ShadowLODSelectionContext;
        private int m_FrameIndex;
        private ulong m_PageDebugCameraEntityId;
        private ComputeShader m_VirtualShadowMapPageManagementCompute;
        private int m_VirtualShadowMapAllocatePagesKernel = -1;
        private int m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel = -1;
        private int m_VirtualShadowMapInvalidateStaticPagesKernel = -1;
        private int m_VirtualShadowMapClearPhysicalPagesKernel = -1;
        private int m_VirtualShadowMapFinalizeDirtyPagesKernel = -1;
        private int m_VirtualShadowMapPrepareMeshletPageRequestsKernel = -1;
        private int m_VirtualShadowMapCullMeshletsToPagesKernel = -1;

        public CSMShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(CSMShadowPass));
            m_ShadowAtlas = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth16)
            };
            m_ShadowAtlas.desc.Name = "CSMShadowAtlas";
            m_ShadowAtlas.desc.ClearBuffer = true;
            m_ShadowAtlas.desc.ClearColor = Color.black;
            m_ShadowAtlas.desc.IsShadowMap = true;
            m_ShadowAtlas.desc.FilterMode = FilterMode.Bilinear;
            m_ShadowAtlas.desc.WrapMode = TextureWrapMode.Clamp;
            m_ShadowAtlas.desc.Dimension = TextureDimension.Tex2DArray;
            m_ShadowAtlas.desc.Slices = VividShadowData.MaxCascadeCount;
        }

        public override void Create()
        {
            PassRecorder.RegisterCascadedShadowCasterPass();
            m_VirtualShadowMapPageManagementCompute =
                PipelineResourceManager.Get<VividRPCoreResources>()?.CSMShadowResolveCompute;
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
            m_VSMResetOwnersKernel = FindKernelOrInvalid(m_VirtualShadowMapPageManagementCompute, "VSMResetPhysicalOwners");
            Shader shader = Shader.Find(ShadowCasterShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ShadowCasterShaderName}' for {nameof(CSMShadowPass)}.");
                return;
            }

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{nameof(CSMShadowPass)}_{(VividRendererListID)rendererListIndex}";
                ConfigureMaterial(material, (VividRendererListID)rendererListIndex);
                m_Materials[rendererListIndex] = material;

                Material vsmMaterial = CoreUtils.CreateEngineMaterial(shader);
                vsmMaterial.name =
                    $"{nameof(CSMShadowPass)}_VSM_{(VividRendererListID)rendererListIndex}";
                ConfigureMaterial(vsmMaterial, (VividRendererListID)rendererListIndex);
                CoreUtils.SetKeyword(
                    vsmMaterial,
                    VirtualShadowMapCasterKeywordName,
                    true);
                CoreUtils.SetKeyword(
                    vsmMaterial,
                    VirtualShadowMapPageCasterKeywordName,
                    true);
                m_VirtualShadowMapPrototypeMaterials[rendererListIndex] = vsmMaterial;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            VirtualShadowMapPrototypeRuntime.BeginFrame();
            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_CascadeCount = 0;
            m_MainLightVisibleIndex = -1;
            m_HasUnityShadowCasters = false;
            m_HasMeshletShadowCasters = false;
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapStaticPoolNeedsCacheRefresh = false;
            m_VirtualShadowMapStaticPoolNeedsFullRefresh = false;
            m_VirtualShadowMapStaticInvalidationBoundsCount = 0;
            m_SlopeScaleDepthBias = 0.0f;
            m_VirtualShadowMapPrototypeCacheKey = default;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
            m_StaticPrimitiveShadowDrawSet = null;
            m_DynamicPrimitiveShadowDrawSet = null;
            m_ShadowLODSelectionContext = default;
            m_FrameIndex = 0;

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive
                || shadowData.cascadeCount <= 0
                || shadowData.cascadeResolution <= 0)
            {
                return;
            }

            var renderingData = frameData.GetOrCreate<VividRenderingData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_FrameIndex = cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;
            m_CullingResults = renderingData.cullingResults;
            m_PageDebugCameraEntityId = cameraData.camera != null
                ? EntityId.ToULong(cameraData.camera.GetEntityId()) : 0ul;
            m_RenderContext = renderingData.context;
            m_MainLightVisibleIndex = shadowData.mainLightVisibleIndex;
            m_HasUnityShadowCasters = shadowData.hasUnityShadowCasters;
            m_HasMeshletShadowCasters = shadowData.hasPrimitiveShadowCasters;
            m_CascadeCount = Mathf.Min(shadowData.cascadeCount, VividShadowData.MaxCascadeCount);
            m_CascadeResolution = shadowData.cascadeResolution;
            m_SlopeScaleDepthBias = shadowData.slopeScaleDepthBias;
            m_ShadowData = shadowData;

            m_IsActive = true;

            m_ShadowAtlas.desc.Width = m_CascadeResolution;
            m_ShadowAtlas.desc.Height = m_CascadeResolution;

            m_ShadowMatrices = new ShadowMatricesConstantBuffer
            {
                Cascade0 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 0),
                Cascade1 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 1),
                Cascade2 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 2),
                Cascade3 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 3),
            };

            // Configure ShadowDrawingSettings per cascade
            for (int i = 0; i < m_CascadeCount && m_HasUnityShadowCasters; i++)
            {
                var settings = new ShadowDrawingSettings(
                    m_CullingResults,
                    m_MainLightVisibleIndex);
#pragma warning disable CS0618 // Intentionally use the non-batched path without CullShadowCasters.
                settings.splitData = shadowData.splitData[i];
#pragma warning restore CS0618
                settings.splitIndex = -1;
                settings.useRenderingLayerMaskTest = false;
                settings.objectsFilter = ShadowObjectsFilter.AllObjects;
                m_ShadowDrawSettings[i] = settings;
            }

            PrepareMeshletRendering(frameData, cameraData);
            PrepareVirtualShadowMapPrototype(cameraData);
        }

        public override void Record(UnsafePassContext context)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                if (!m_IsActive)
                    return;
                if (!m_ShadowAtlas.innerHandle.IsValid())
                {
                    if (m_VirtualShadowMapPrototypeActive)
                    {
                        VirtualShadowMapPrototypeRuntime.MarkFallback(
                            VirtualShadowMapPrototypeFallbackReason.ResourceUnavailable);
                    }
                    return;
                }

                nativeCmd.SetGlobalInt(VSMUnityRasterEnabledId, 0);
                RecordVirtualShadowMapLayout(nativeCmd);
                bool meshletPreparationSucceeded = TryPrepareMeshletShadowDraws(
                    nativeCmd,
                    out MeshletShadowRecordContext meshletContext);

                ConstantBuffer.PushGlobal(
                    nativeCmd,
                    m_ShadowMatrices,
                    ShadowMatricesConstantBufferId);
                nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowData.shadowCasterState);
                // VSM pools store unbiased atomic depth. Both caster backends use
                // the shared receiver bias; only conventional CSM uses raster bias.
                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);

                bool vsmCompleted = false;
                if (m_VirtualShadowMapPrototypeActive)
                {
                    if (!meshletPreparationSucceeded)
                    {
                        VirtualShadowMapPrototypeRuntime.MarkFallback(
                            VirtualShadowMapPrototypeFallbackReason.RecordPreparationFailed);
                    }
                    else if (m_HasMeshletShadowCasters
                        && !meshletContext.VirtualTextureReady)
                    {
                        VirtualShadowMapPrototypeRuntime.MarkFallback(
                            VirtualShadowMapPrototypeFallbackReason.VirtualTextureUnavailable);
                    }
                    else
                    {
                        vsmCompleted = DrawVirtualShadowMapPrototypePages(
                            nativeCmd,
                            in meshletContext,
                            out VirtualShadowMapPrototypeFallbackReason fallbackReason);
                        if (!vsmCompleted)
                            VirtualShadowMapPrototypeRuntime.MarkFallback(fallbackReason);
                    }
                }

                if (!vsmCompleted)
                {
                    nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);
                    DrawConventionalShadowMap(
                        nativeCmd,
                        meshletPreparationSucceeded,
                        in meshletContext);
                }

                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
            }
        }

        private void RecordVirtualShadowMapLayout(CommandBuffer cmd)
        {
            var projections = VirtualShadowMapPrototypeRuntime.Projections;
            if (projections.Count == 0 || !CanManageVirtualShadowMapPages()
                || m_VSMRemapPagesKernel < 0 || m_VSMResetOwnersKernel < 0)
                return;
            using var layoutScope = new ProfilingScope(cmd, VSMProfiling.Layout);
            projections.Upload(cmd);
            if (projections.RequiresRemap)
            {
                // Copy into scratch: in-place shifts would race and corrupt both
                // residency and the feedback whose coordinates we are migrating.
                cmd.CopyBuffer(VirtualShadowMapPrototypeRuntime.PageTable,
                    VirtualShadowMapPrototypeRuntime.PreviousPageTable);
                cmd.CopyBuffer(VirtualShadowMapPrototypeRuntime.PageMetadata,
                    VirtualShadowMapPrototypeRuntime.PreviousPageMetadata);
                SetVirtualShadowMapPageManagementParameters(cmd);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMResetOwnersKernel,
                    VSMPrototypePhysicalPageOwnersId, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
                cmd.DispatchCompute(m_VirtualShadowMapPageManagementCompute, m_VSMResetOwnersKernel,
                    CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, 64), 1, 1);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    VSMPreviousPageTableId, VirtualShadowMapPrototypeRuntime.PreviousPageTable);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    VSMPreviousPageMetadataId, VirtualShadowMapPrototypeRuntime.PreviousPageMetadata);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    VirtualShadowMapProjectionSet.RemapId, projections.RemapBuffer);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    VSMPrototypeWritablePageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    VSMPrototypePageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.SetComputeBufferParam(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    VSMPrototypePhysicalPageOwnersId, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
                cmd.DispatchCompute(m_VirtualShadowMapPageManagementCompute, m_VSMRemapPagesKernel,
                    CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PageTableEntryCount, 64), 1, 1);
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
            if (clipmaps.Count <= 0 || !VirtualShadowMapPrototypeRuntime.EnsureResources(virtualResolution, clipmaps.Count))
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
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageTable, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PageMetadata, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PreviousPageTable, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, VirtualShadowMapPrototypeRuntime.PreviousPageMetadata, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer, AccessFlags.ReadWrite);

            if (!CanManageVirtualShadowMapPages() || m_VSMRemapPagesKernel < 0 || m_VSMResetOwnersKernel < 0)
            {
                VirtualShadowMapPrototypeRuntime.MarkFallback(
                    VirtualShadowMapPrototypeFallbackReason.PageManagementUnavailable);
                return;
            }

            if (VirtualShadowMapPrototypeRuntime.Projections.RequiresFeedbackReset
                || !VirtualShadowMapPrototypeRuntime.HasReceiverFeedbackForFrame(
                    EntityId.ToULong(cameraData.camera.GetEntityId()),
                    m_FrameIndex))
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

        private void PrepareMeshletRendering(
            ContextContainer frameData,
            VividCameraData cameraData)
        {
            if (m_Materials[0] == null
                || cameraData?.camera == null
                || !VividGPUDrivenSystem.HasInstance)
            {
                return;
            }

            var system = VividGPUDrivenSystem.instance;
            if (!system.IsAvailable
                || system.SceneData == null
                || system.SceneData.InstanceCount == 0)
            {
                return;
            }

            m_LODCamera = cameraData.camera;
            // Cache validation and GPU submission must consume the same snapshot.
            m_LODCamera.BuildLODSelectionContext(out m_ShadowLODSelectionContext);
            for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
            {
                BuildShadowCullingContext(
                    cascadeIndex,
                    out m_ShadowCullingContexts[cascadeIndex]);
            }

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
            var gpuDrivenFrameData = frameData.GetOrCreate<VividGPUDrivenFrameData>();
            m_PrimitiveShadowDrawSet = gpuDrivenFrameData.primitiveShadowDrawSet;
            m_StaticPrimitiveShadowDrawSet =
                gpuDrivenFrameData.staticPrimitiveShadowDrawSet;
            m_DynamicPrimitiveShadowDrawSet =
                gpuDrivenFrameData.dynamicPrimitiveShadowDrawSet;
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
            m_MeshletRenderingActive = true;
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
                 materialIndex < m_VirtualShadowMapPrototypeMaterials.Length;
                 materialIndex++)
            {
                if (m_VirtualShadowMapPrototypeMaterials[materialIndex] == null)
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

        private bool TryPrepareMeshletShadowDraws(
            CommandBuffer nativeCmd,
            out MeshletShadowRecordContext recordContext)
        {
            recordContext = default;
            if (!m_HasMeshletShadowCasters)
            {
                recordContext.VirtualTextureReady = true;
                return true;
            }

            if (!m_MeshletRenderingActive
                || m_ShadowData == null
                || m_LODCamera == null
                || !VividGPUDrivenSystem.HasInstance)
            {
                return false;
            }

            VividGPUDrivenSystem system = VividGPUDrivenSystem.instance;
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (!system.IsAvailable
                || system.SceneData == null
                || system.SceneData.InstanceCount == 0
                || !HasMeshletShadowShaderResources(resources))
            {
                return false;
            }

            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
            {
                system.ConfigureTextureBackendKeyword(m_Materials[materialIndex]);
                system.ConfigureTextureBackendKeyword(
                    m_VirtualShadowMapPrototypeMaterials[materialIndex]);
            }

            VirtualTextureSpaceBinding virtualTextureBinding = default;
            bool virtualTextureReady = !system.UsesVirtualTexture
                || GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                    nativeCmd,
                    m_VirtualTextureFrameData,
                    m_VirtualTextureSpaceParams,
                    m_VirtualTextureMipOffsets,
                    m_VirtualTextureLayerFallbacks,
                    m_FrameIndex,
                    feedbackSampleRate: 1,
                    out virtualTextureBinding);

            VividPrimitiveDrawSet aggregateDrawSet = system.CompleteShadowDrawSet(
                m_PrimitiveShadowDrawSet,
                m_LODCamera,
                m_FrameIndex);
            VividPrimitiveDrawSet staticDrawSet = system.CompleteShadowDrawSet(
                m_StaticPrimitiveShadowDrawSet,
                m_LODCamera,
                m_FrameIndex);
            VividPrimitiveDrawSet dynamicDrawSet = system.CompleteShadowDrawSet(
                m_DynamicPrimitiveShadowDrawSet,
                m_LODCamera,
                m_FrameIndex);
            if (aggregateDrawSet == null)
                return false;

            recordContext = new MeshletShadowRecordContext
            {
                System = system,
                AggregateDrawSet = aggregateDrawSet,
                StaticDrawSet = staticDrawSet,
                DynamicDrawSet = dynamicDrawSet,
                VirtualTextureReady = virtualTextureReady,
                VirtualTextureBinding = virtualTextureBinding,
            };
            return true;
        }

        private bool TryPrepareMeshletShadowPoolDraws(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            VividPrimitiveDrawSet drawSet,
            out GraphicsBuffer requestsBuffer,
            out GraphicsBuffer argsBuffer,
            out bool hasDraws,
            bool useClipmaps = false)
        {
            requestsBuffer = null;
            argsBuffer = null;
            hasDraws = false;
            if (system == null || drawSet == null)
                return false;
            if (drawSet.DrawCount <= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (!HasMeshletShadowShaderResources(resources))
                return false;

            system.CullShadowCascades(
                nativeCmd,
                useClipmaps ? m_ClipmapCullingContexts : m_ShadowCullingContexts,
                useClipmaps ? VirtualShadowMapPrototypeRuntime.Projections.Count : m_CascadeCount,
                useClipmaps ? m_ClipmapLODContext : m_ShadowLODSelectionContext,
                resources.GPUInstanceCullingCompute,
                resources.MeshletListBuildCompute,
                resources.GPUMeshletCullingCompute,
                resources.FixupVisibleMeshletIndirectDrawArgsCompute,
                drawSet);
            requestsBuffer = system.GetShadowVisibleMeshletRenderRequestsBuffer(0);
            argsBuffer = system.GetShadowVisibleMeshletIndirectDrawArgsBuffer(0);
            hasDraws = requestsBuffer != null && argsBuffer != null;
            return hasDraws;
        }

        private static bool HasMeshletShadowShaderResources(
            VividRPCoreResources resources)
        {
            return resources != null
                && resources.GPUInstanceCullingCompute != null
                && resources.MeshletListBuildCompute != null
                && resources.GPUMeshletCullingCompute != null
                && resources.FixupVisibleMeshletIndirectDrawArgsCompute != null;
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
                 rendererListIndex < m_VirtualShadowMapPrototypeMaterials.Length;
                 rendererListIndex++)
            {
                VividRendererListID batchKey =
                    (VividRendererListID)rendererListIndex;
                if (!system.IsShadowRendererBatchActive(batchKey))
                    continue;

                Material material =
                    m_VirtualShadowMapPrototypeMaterials[rendererListIndex];
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

        private void DrawMeshletShadowCascade(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            GraphicsBuffer requestsBuffer,
            GraphicsBuffer argsBuffer,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding,
            int cascadeIndex,
            Material[] materials)
        {
            if (materials == null)
                return;

            for (int rendererListIndex = 0; rendererListIndex < materials.Length; rendererListIndex++)
            {
                VividRendererListID batchKey = (VividRendererListID)rendererListIndex;
                if (!system.IsShadowRendererBatchActive(batchKey))
                    continue;

                Material material = materials[rendererListIndex];
                if (material == null)
                    continue;
                if (!virtualTextureReady
                    && (batchKey & VividRendererListID.AlphaTest) != 0)
                {
                    continue;
                }

                m_DrawProperties.Clear();
                m_DrawProperties.SetBuffer(s_VisibleMeshletRenderRequestsId, requestsBuffer);
                m_DrawProperties.SetBuffer(s_UnityIndirectDrawArgsId, argsBuffer);
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
                m_DrawProperties.SetInteger(ShadowCascadeIndexId, cascadeIndex);
                int commandIndex = VividGPUDrivenCullingBuffers.GetIndirectDrawArgsCommandIndex(
                    cascadeIndex,
                    rendererListIndex);
                m_DrawProperties.SetInteger(s_UnityBaseCommandIdId, commandIndex);
                nativeCmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    material,
                    0,
                    MeshTopology.Triangles,
                    argsBuffer,
                    VividGPUDrivenCullingBuffers.GetIndirectDrawArgsByteOffset(
                        cascadeIndex,
                        rendererListIndex),
                    m_DrawProperties);
            }
        }

        private void DrawConventionalShadowMap(
            CommandBuffer nativeCmd,
            bool meshletPreparationSucceeded,
            in MeshletShadowRecordContext meshletContext)
        {
            bool canDrawMeshlets = false;
            GraphicsBuffer requestsBuffer = null;
            GraphicsBuffer argsBuffer = null;
            if (m_HasMeshletShadowCasters && meshletPreparationSucceeded)
            {
                bool cullSucceeded = TryPrepareMeshletShadowPoolDraws(
                    nativeCmd,
                    meshletContext.System,
                    meshletContext.AggregateDrawSet,
                    out requestsBuffer,
                    out argsBuffer,
                    out bool hasDraws);
                canDrawMeshlets = cullSucceeded
                    && hasDraws
                    && HasRenderableMeshletShadowBatch(
                        meshletContext.System,
                        meshletContext.VirtualTextureReady);
            }

            for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
            {
                CoreUtils.SetRenderTarget(
                    nativeCmd,
                    m_ShadowAtlas.innerHandle,
                    ClearFlag.Depth,
                    Color.black,
                    depthSlice: cascadeIndex);
                nativeCmd.SetGlobalInt(ShadowCascadeIndexId, cascadeIndex);

                if (m_HasUnityShadowCasters)
                {
                    var settings = m_ShadowDrawSettings[cascadeIndex];
                    var rendererList = m_RenderContext.CreateShadowRendererList(ref settings);
                    nativeCmd.DrawRendererList(rendererList);
                }

                if (canDrawMeshlets)
                {
                    DrawMeshletShadowCascade(
                        nativeCmd,
                        meshletContext.System,
                        requestsBuffer,
                        argsBuffer,
                        meshletContext.VirtualTextureReady,
                        meshletContext.VirtualTextureBinding,
                        cascadeIndex,
                        m_Materials);
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

            nativeCmd.BeginSample("VSM.Allocate");
            BindVirtualShadowMapPageManagementBuffers(
                nativeCmd,
                m_VirtualShadowMapAllocatePagesKernel,
                pageTable,
                pageMetadata,
                physicalPageOwners,
                allocatorCounters);
            nativeCmd.DispatchCompute(
                m_VirtualShadowMapPageManagementCompute,
                m_VirtualShadowMapAllocatePagesKernel,
                1,
                1,
                1);
            nativeCmd.EndSample("VSM.Allocate");

            nativeCmd.SetGlobalBuffer(VirtualShadowMapProjectionSet.BufferId,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            nativeCmd.SetGlobalBuffer(VSMPrototypePageTableId, pageTable);
            nativeCmd.SetGlobalBuffer(VSMPrototypePageMetadataId, pageMetadata);
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

            nativeCmd.BeginSample("VSM.ClearPhysicalPages");
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
            nativeCmd.DispatchCompute(
                m_VirtualShadowMapPageManagementCompute,
                m_VirtualShadowMapClearPhysicalPagesKernel,
                CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PageSize, 8),
                CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PageSize, 8),
                physicalPageCapacity);
            nativeCmd.EndSample("VSM.ClearPhysicalPages");

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
                    out bool hasStaticDraws, useClipmaps: true))
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
                CoreUtils.SetRenderTarget(
                    nativeCmd,
                    rasterDepth,
                    ClearFlag.Depth,
                    Color.black,
                    depthSlice: -1);
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

            nativeCmd.BeginSample("VSM.FinalizePages");
            nativeCmd.SetComputeBufferParam(
                m_VirtualShadowMapPageManagementCompute,
                m_VirtualShadowMapFinalizeDirtyPagesKernel,
                VSMPrototypePageMetadataId,
                pageMetadata);
            SetVirtualShadowMapPageManagementParameters(nativeCmd);
            nativeCmd.DispatchCompute(
                m_VirtualShadowMapPageManagementCompute,
                m_VirtualShadowMapFinalizeDirtyPagesKernel,
                CoreUtils.DivRoundUp(pageTableEntryCount, 64),
                1,
                1);
            nativeCmd.EndSample("VSM.FinalizePages");

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
                    out bool hasDynamicDraws, useClipmaps: true))
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

            nativeCmd.BeginSample("VSM.DynamicRaster");
            nativeCmd.SetGlobalInt(VSMPrototypeCasterLayerId, 1);
            CoreUtils.SetRenderTarget(
                nativeCmd,
                rasterDepth,
                ClearFlag.Depth,
                Color.black,
                depthSlice: -1);
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

            nativeCmd.EndSample("VSM.DynamicRaster");
            if (m_HasUnityShadowCasters)
            {
                using var unityScope = new ProfilingScope(nativeCmd, VSMProfiling.UnityRaster);
                int resolution = VirtualShadowMapPrototypeRuntime.VirtualResolution;
                int tileSize = VirtualShadowMapPrototypeRuntime.UnityRasterDepth.rt.width;
                int tilesPerAxis = CoreUtils.DivRoundUp(resolution, tileSize);
                nativeCmd.SetGlobalInt(VSMUnityRasterEnabledId, 1);
                for (int projectionIndex = 0;
                     projectionIndex < VirtualShadowMapPrototypeRuntime.Projections.Count;
                     projectionIndex++)
                {
                    // Reuse one renderer list per projection across coarse raster tiles.
                    // These are not per-physical-page CPU draws.
                    var settings = m_ClipmapDrawSettings[projectionIndex];
                    var rendererList = m_RenderContext.CreateShadowRendererList(ref settings);
                    nativeCmd.SetGlobalInt(VSMProjectionIndexId, projectionIndex);
                    nativeCmd.EnableKeyword(s_VirtualShadowMapCasterKeyword);
                    for (int y = 0; y < tilesPerAxis; y++)
                    for (int x = 0; x < tilesPerAxis; x++)
                    {
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
            }
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
                m_FrameIndex - 1);
        }

        private static bool HasRenderableMeshletShadowBatch(
            VividGPUDrivenSystem system,
            bool virtualTextureReady)
        {
            if (system == null)
                return false;

            for (int rendererListIndex = 0; rendererListIndex < RendererListCount; rendererListIndex++)
            {
                VividRendererListID batchKey = (VividRendererListID)rendererListIndex;
                if (system.IsShadowRendererBatchActive(batchKey)
                    && (virtualTextureReady
                        || (batchKey & VividRendererListID.AlphaTest) == 0))
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildShadowCullingContext(
            int cascadeIndex,
            out VividGPUCullingContext cullingContext)
        {
            var viewMatrix = m_ShadowData.viewMatrices[cascadeIndex];
            var projMatrix = m_ShadowData.projMatrices[cascadeIndex];
            var invViewMatrix = viewMatrix.inverse;
            Vector4 col0 = invViewMatrix.GetColumn(0);
            Vector4 col1 = invViewMatrix.GetColumn(1);
            Vector4 col3 = invViewMatrix.GetColumn(3);
            var cullingSphereWS = m_ShadowData.cascadeSpheres[cascadeIndex];
            cullingSphereWS.w = Mathf.Sqrt(Mathf.Max(0.0f, cullingSphereWS.w));

            VividGPUDrivenCullingContextUtility.Build(
                viewMatrix,
                projMatrix,
                cameraPositionWS: new Vector3(col3.x, col3.y, col3.z),
                cameraRightWS: new Vector3(col0.x, col0.y, col0.z),
                cameraUpWS: new Vector3(col1.x, col1.y, col1.z),
                pixelSize: new Vector2(m_CascadeResolution, m_CascadeResolution),
                isPerspective: false,
                passMask: VividInstancePassMask.Shadows,
                cullingSphereWS: cullingSphereWS,
                // Directional shadow vertices are pancaked onto the raster near plane, so
                // rejecting their bounds against that plane would remove valid casters.
                cullAgainstNearPlane: false,
                cullingContext: out cullingContext,
                lodSelectionContext: out _);
        }

        private static Matrix4x4 BuildShadowViewProjectionMatrix(
            VividShadowData shadowData,
            int cascadeCount,
            int cascadeIndex)
        {
            return cascadeIndex < cascadeCount
                ? GL.GetGPUProjectionMatrix(shadowData.projMatrices[cascadeIndex], true)
                    * shadowData.viewMatrices[cascadeIndex]
                : Matrix4x4.identity;
        }

        private static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            if (material == null)
                return;

            material.SetFloat(s_CullId, (float)GetCullMode(rendererListID));
            CoreUtils.SetKeyword(
                material,
                AlphaTestKeywordName,
                (rendererListID & VividRendererListID.AlphaTest) != 0);
        }

        private bool CanManageVirtualShadowMapPages()
        {
            return m_VirtualShadowMapPageManagementCompute != null
                && m_VirtualShadowMapAllocatePagesKernel >= 0
                && m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel >= 0
                && m_VirtualShadowMapInvalidateStaticPagesKernel >= 0
                && m_VirtualShadowMapClearPhysicalPagesKernel >= 0
                && m_VirtualShadowMapFinalizeDirtyPagesKernel >= 0
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

        private static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
                return CullMode.Front;

            if ((rendererListID & VividRendererListID.CullOff) != 0)
                return CullMode.Off;

            return CullMode.Back;
        }

        public override void Dispose()
        {
            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
            {
                if (m_Materials[materialIndex] != null)
                {
                    CoreUtils.Destroy(m_Materials[materialIndex]);
                    m_Materials[materialIndex] = null;
                }

                if (m_VirtualShadowMapPrototypeMaterials[materialIndex] != null)
                {
                    CoreUtils.Destroy(m_VirtualShadowMapPrototypeMaterials[materialIndex]);
                    m_VirtualShadowMapPrototypeMaterials[materialIndex] = null;
                }
            }

            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_MainLightVisibleIndex = -1;
            m_HasUnityShadowCasters = false;
            m_HasMeshletShadowCasters = false;
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapStaticPoolNeedsCacheRefresh = false;
            m_VirtualShadowMapStaticPoolNeedsFullRefresh = false;
            m_VirtualShadowMapStaticInvalidationBoundsCount = 0;
            m_CascadeCount = 0;
            m_SlopeScaleDepthBias = 0.0f;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
            m_StaticPrimitiveShadowDrawSet = null;
            m_DynamicPrimitiveShadowDrawSet = null;
            m_ShadowLODSelectionContext = default;
            m_FrameIndex = 0;
            m_VirtualShadowMapPageManagementCompute = null;
            m_VirtualShadowMapAllocatePagesKernel = -1;
            m_VirtualShadowMapMarkAllAllocatedPagesDirtyKernel = -1;
            m_VirtualShadowMapInvalidateStaticPagesKernel = -1;
            m_VirtualShadowMapClearPhysicalPagesKernel = -1;
            m_VirtualShadowMapFinalizeDirtyPagesKernel = -1;
            m_VirtualShadowMapPrepareMeshletPageRequestsKernel = -1;
            m_VirtualShadowMapCullMeshletsToPagesKernel = -1;
            m_ShadowMatrices = default;
            m_VirtualShadowMapPrototypeCacheKey = default;
            VirtualShadowMapPrototypeRuntime.ReleaseResources();
        }

        private struct MeshletShadowRecordContext
        {
            internal VividGPUDrivenSystem System;
            internal VividPrimitiveDrawSet AggregateDrawSet;
            internal VividPrimitiveDrawSet StaticDrawSet;
            internal VividPrimitiveDrawSet DynamicDrawSet;
            internal bool VirtualTextureReady;
            internal VirtualTextureSpaceBinding VirtualTextureBinding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShadowMatricesConstantBuffer
        {
            public Matrix4x4 Cascade0;
            public Matrix4x4 Cascade1;
            public Matrix4x4 Cascade2;
            public Matrix4x4 Cascade3;
        }
    }

    internal enum VirtualShadowMapUnityCasterFailureReason : byte
    {
        None,
        MissingTerrainMaterial,
        UnsupportedShadowCasterPass,
    }

    internal readonly struct VirtualShadowMapUnityCasterFailure
        : IEquatable<VirtualShadowMapUnityCasterFailure>
    {
        internal VirtualShadowMapUnityCasterFailure(
            Component caster,
            Material material,
            Shader shader,
            int materialSlot,
            string passName,
            VirtualShadowMapUnityCasterFailureReason reason)
        {
            Caster = caster;
            Material = material;
            Shader = shader;
            MaterialSlot = materialSlot;
            PassName = passName;
            Reason = reason;
        }

        internal Component Caster { get; }
        internal Material Material { get; }
        internal Shader Shader { get; }
        internal int MaterialSlot { get; }
        internal string PassName { get; }
        internal VirtualShadowMapUnityCasterFailureReason Reason { get; }

        internal bool IsValid => Caster != null
            && Reason != VirtualShadowMapUnityCasterFailureReason.None;

        public bool Equals(VirtualShadowMapUnityCasterFailure other)
        {
            return Caster == other.Caster
                && Material == other.Material
                && Shader == other.Shader
                && MaterialSlot == other.MaterialSlot
                && string.Equals(PassName, other.PassName, StringComparison.Ordinal)
                && Reason == other.Reason;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualShadowMapUnityCasterFailure other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Caster);
            hash.Add(Material);
            hash.Add(Shader);
            hash.Add(MaterialSlot);
            hash.Add(PassName);
            hash.Add(Reason);
            return hash.ToHashCode();
        }
    }

    internal static class VirtualShadowMapUnityCasterCompatibility
    {
        internal const string CapabilityTagName = "VividVSMCaster";
        internal const string CapabilityTagValue = "2";
        internal const string CasterKeywordName = "VIVID_VSM_CASTER";

        private const string ShadowCasterLightModeName = "ShadowCaster";
        private static readonly ShaderTagId s_LightModeTag = new("LightMode");
        private static readonly ShaderTagId s_ShadowCasterLightMode =
            new(ShadowCasterLightModeName);
        private static readonly ShaderTagId s_CapabilityTag =
            new(CapabilityTagName);
        private static readonly ShaderTagId s_CapabilityValue =
            new(CapabilityTagValue);
        private static readonly List<Material> s_SharedMaterials = new();
        private static readonly HashSet<Material> s_TrackedCasterMaterials = new();

        private static RendererTracker s_RendererTracker;
        private static TerrainTracker s_TerrainTracker;
        private static MaterialTracker s_MaterialTracker;
        private static ShaderTracker s_ShaderTracker;
        private static bool s_TrackingInitialized;
        private static bool s_IsDirty = true;
        private static bool s_IsCompatible = true;
        private static bool s_CollectCasterMaterials;
        private static bool s_HasReportedFailure;
        private static uint s_ValidationRevision;
        private static VirtualShadowMapUnityCasterFailure s_LastFailure;
        private static VirtualShadowMapUnityCasterFailure s_ReportedFailure;

        internal static bool IsReady()
        {
            EnsureTracking();
            ObjectDispatcherService.ProcessUpdates();
            if (s_IsDirty)
                ValidateTrackedCasters();

            return s_IsCompatible;
        }

        internal static bool IsCompatible => s_IsCompatible;
        internal static uint ValidationRevision => s_ValidationRevision;
        internal static VirtualShadowMapUnityCasterFailure LastFailure => s_LastFailure;

        internal static bool TryValidateRenderer(
            Renderer renderer,
            bool activeOnly,
            out VirtualShadowMapUnityCasterFailure failure)
        {
            failure = default;
            if (renderer == null
                || !IsSceneCaster(renderer)
                || renderer.shadowCastingMode == ShadowCastingMode.Off
                || (activeOnly
                    && (!renderer.enabled
                        || renderer.forceRenderingOff
                        || renderer.gameObject == null
                        || !renderer.gameObject.activeInHierarchy)))
            {
                return true;
            }

            s_SharedMaterials.Clear();
            renderer.GetSharedMaterials(s_SharedMaterials);
            for (int materialIndex = 0;
                 materialIndex < s_SharedMaterials.Count;
                 materialIndex++)
            {
                Material material = s_SharedMaterials[materialIndex];
                if (s_CollectCasterMaterials && material != null)
                    s_TrackedCasterMaterials.Add(material);
                if (TryValidateMaterial(
                        material,
                        out Shader shader,
                        out string passName))
                {
                    continue;
                }

                failure = new VirtualShadowMapUnityCasterFailure(
                    renderer,
                    material,
                    shader,
                    materialIndex,
                    passName,
                    VirtualShadowMapUnityCasterFailureReason.UnsupportedShadowCasterPass);
                s_SharedMaterials.Clear();
                return false;
            }

            s_SharedMaterials.Clear();
            return true;
        }

        internal static bool TryValidateTerrain(
            Terrain terrain,
            bool activeOnly,
            out VirtualShadowMapUnityCasterFailure failure)
        {
            failure = default;
            if (terrain == null
                || !IsSceneCaster(terrain)
                || terrain.shadowCastingMode == ShadowCastingMode.Off
                || (activeOnly
                    && (!terrain.enabled
                        || terrain.gameObject == null
                        || !terrain.gameObject.activeInHierarchy)))
            {
                return true;
            }

            Material material = terrain.materialTemplate;
            if (material == null)
            {
                failure = new VirtualShadowMapUnityCasterFailure(
                    terrain,
                    null,
                    null,
                    0,
                    string.Empty,
                    VirtualShadowMapUnityCasterFailureReason.MissingTerrainMaterial);
                return false;
            }

            if (s_CollectCasterMaterials)
                s_TrackedCasterMaterials.Add(material);

            if (TryValidateMaterial(material, out Shader shader, out string passName))
                return true;

            failure = new VirtualShadowMapUnityCasterFailure(
                terrain,
                material,
                shader,
                0,
                passName,
                VirtualShadowMapUnityCasterFailureReason.UnsupportedShadowCasterPass);
            return false;
        }

        internal static bool TryValidateMaterial(
            Material material,
            out Shader unsupportedShader,
            out string unsupportedPassName)
        {
            unsupportedShader = null;
            unsupportedPassName = string.Empty;
            Shader shader = material != null ? material.shader : null;
            if (shader == null)
                return true;

            int passCount = material.passCount;
            for (int passIndex = 0; passIndex < passCount; passIndex++)
            {
                if (!shader.FindPassTagValue(passIndex, s_LightModeTag)
                        .Equals(s_ShadowCasterLightMode))
                {
                    continue;
                }

                string passName = material.GetPassName(passIndex);
                if (!string.IsNullOrEmpty(passName)
                    && !material.GetShaderPassEnabled(passName))
                {
                    continue;
                }

                if (shader.FindPassTagValue(passIndex, s_CapabilityTag)
                        .Equals(s_CapabilityValue)
                    && shader.keywordSpace.FindKeyword(CasterKeywordName).isValid)
                {
                    continue;
                }

                unsupportedShader = shader;
                unsupportedPassName = passName;
                return false;
            }

            return true;
        }

        private static bool IsSceneCaster(Component caster)
        {
            GameObject gameObject = caster != null ? caster.gameObject : null;
            return gameObject != null
                && gameObject.scene.IsValid()
                && gameObject.scene.isLoaded
                && (caster.hideFlags & HideFlags.DontSave) == 0
                && (gameObject.hideFlags & HideFlags.DontSave) == 0;
        }

        internal static string FormatFailure(
            in VirtualShadowMapUnityCasterFailure failure)
        {
            if (!failure.IsValid)
                return "No incompatible Unity VSM shadow caster was found.";

            string gameObjectName = failure.Caster.gameObject != null
                ? failure.Caster.gameObject.name
                : "<missing GameObject>";
            if (failure.Reason
                == VirtualShadowMapUnityCasterFailureReason.MissingTerrainMaterial)
            {
                return $"Unity VSM caster '{gameObjectName}' ({failure.Caster.GetType().Name}) has no Terrain material template. Assign a VSM-compatible TerrainLit material or disable its shadow casting.";
            }

            string materialName = failure.Material != null
                ? failure.Material.name
                : "<missing Material>";
            string shaderName = failure.Shader != null
                ? failure.Shader.name
                : "<missing Shader>";
            string passName = string.IsNullOrEmpty(failure.PassName)
                ? ShadowCasterLightModeName
                : failure.PassName;
            return $"Unity VSM caster '{gameObjectName}' ({failure.Caster.GetType().Name}) material slot {failure.MaterialSlot} uses material '{materialName}', shader '{shaderName}', pass '{passName}' without the required {CapabilityTagName}={CapabilityTagValue} and {CasterKeywordName} variant contract. Implement the V2 tiled projection through VividRP Core.hlsl and the shared VSM depth writer, then add the variant and capability tag; otherwise disable this caster's shadows.";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTracking()
        {
            ObjectDispatcherService.UnregisterObjectTracker(s_RendererTracker);
            ObjectDispatcherService.UnregisterObjectTracker(s_TerrainTracker);
            ObjectDispatcherService.UnregisterObjectTracker(s_MaterialTracker);
            ObjectDispatcherService.UnregisterObjectTracker(s_ShaderTracker);
            s_RendererTracker = null;
            s_TerrainTracker = null;
            s_MaterialTracker = null;
            s_ShaderTracker = null;
            s_TrackedCasterMaterials.Clear();
            s_SharedMaterials.Clear();
            s_TrackingInitialized = false;
            s_IsDirty = true;
            s_IsCompatible = true;
            s_CollectCasterMaterials = false;
            s_HasReportedFailure = false;
            s_ValidationRevision = 0u;
            s_LastFailure = default;
            s_ReportedFailure = default;
        }

        private static void EnsureTracking()
        {
            if (s_TrackingInitialized)
                return;

            s_TrackingInitialized = true;
            s_RendererTracker = new RendererTracker();
            s_TerrainTracker = new TerrainTracker();
            s_MaterialTracker = new MaterialTracker();
            s_ShaderTracker = new ShaderTracker();
            ObjectDispatcherService.RegisterObjectTracker(s_RendererTracker);
            ObjectDispatcherService.RegisterObjectTracker(s_TerrainTracker);
            ObjectDispatcherService.RegisterObjectTracker(s_MaterialTracker);
            ObjectDispatcherService.RegisterObjectTracker(s_ShaderTracker);
            s_IsDirty = true;
        }

        private static void ValidateTrackedCasters()
        {
            VirtualShadowMapUnityCasterFailure failure = default;
            bool compatible = true;
            s_TrackedCasterMaterials.Clear();
            s_CollectCasterMaterials = true;
            try
            {
                Renderer[] renderers = Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include);
                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    if (TryValidateRenderer(
                            renderers[rendererIndex],
                            activeOnly: true,
                            out failure))
                    {
                        continue;
                    }

                    compatible = false;
                    break;
                }

                if (compatible)
                {
                    Terrain[] terrains = Object.FindObjectsByType<Terrain>(
                        FindObjectsInactive.Include);
                    for (int terrainIndex = 0;
                         terrainIndex < terrains.Length;
                         terrainIndex++)
                    {
                        if (TryValidateTerrain(
                                terrains[terrainIndex],
                                activeOnly: true,
                                out failure))
                        {
                            continue;
                        }

                        compatible = false;
                        break;
                    }
                }
            }
            finally
            {
                s_CollectCasterMaterials = false;
            }

            s_IsDirty = false;
            s_IsCompatible = compatible;
            s_LastFailure = compatible ? default : failure;
            s_ValidationRevision++;
            if (compatible)
            {
                s_HasReportedFailure = false;
                s_ReportedFailure = default;
                return;
            }

            if (s_HasReportedFailure && s_ReportedFailure.Equals(failure))
                return;

            s_HasReportedFailure = true;
            s_ReportedFailure = failure;
            Debug.LogError(
                $"[VividRP] Virtual Shadow Map disabled for this frame. {FormatFailure(failure)} Conventional CSM remains active.",
                failure.Caster);
        }

        private sealed class RendererTracker : ObjectTracker<Renderer>
        {
            internal RendererTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (changed.Count > 0 || destroyedId.Length > 0)
                    s_IsDirty = true;
            }
        }

        private sealed class TerrainTracker : ObjectTracker<Terrain>
        {
            internal TerrainTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (changed.Count > 0 || destroyedId.Length > 0)
                    s_IsDirty = true;
            }
        }

        private sealed class MaterialTracker : ObjectTracker<Material>
        {
            internal MaterialTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.Default)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (destroyedId.Length > 0)
                {
                    s_IsDirty = true;
                    return;
                }

                for (int changedIndex = 0;
                     changedIndex < changed.Count;
                     changedIndex++)
                {
                    if (changed[changedIndex] is Material material
                        && s_TrackedCasterMaterials.Contains(material))
                    {
                        s_IsDirty = true;
                        return;
                    }
                }
            }
        }

        private sealed class ShaderTracker : ObjectTracker<Shader>
        {
            internal ShaderTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.Assets)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (destroyedId.Length > 0)
                {
                    s_IsDirty = true;
                    return;
                }

                for (int changedIndex = 0;
                     changedIndex < changed.Count;
                     changedIndex++)
                {
                    if (changed[changedIndex] is not Shader shader)
                        continue;

                    foreach (Material material in s_TrackedCasterMaterials)
                    {
                        if (material != null && material.shader == shader)
                        {
                            s_IsDirty = true;
                            return;
                        }
                    }
                }
            }
        }
    }

    internal readonly struct VirtualShadowMapPrototypeCacheKey
        : IEquatable<VirtualShadowMapPrototypeCacheKey>
    {
        private readonly ulong m_CameraEntityId;
        private readonly ulong m_ClipmapGeneration;
        private readonly uint m_PrimitiveSceneToken;
        private readonly uint m_StaticShadowRevision;
        private readonly uint m_TextureBindingRevision;
        private readonly int m_CascadeCount;
        private readonly int m_VirtualResolution;
        private readonly int m_ForcedMeshLODNodeDepth;
        private readonly float m_MeshLODErrorThreshold;
        private readonly float m_SlopeScaleDepthBias;
        private readonly Vector4 m_ShadowCasterState;
        private readonly int m_CameraCullingMask;
        private readonly VividGPULODSelectionContext m_LODSelectionContext;
        private readonly VividGPUCullingContext m_CullingContext0;
        private readonly VividGPUCullingContext m_CullingContext1;
        private readonly VividGPUCullingContext m_CullingContext2;
        private readonly VividGPUCullingContext m_CullingContext3;
        private readonly Matrix4x4 m_Cascade0;
        private readonly Matrix4x4 m_Cascade1;
        private readonly Matrix4x4 m_Cascade2;
        private readonly Matrix4x4 m_Cascade3;

        internal VirtualShadowMapPrototypeCacheKey(
            ulong cameraEntityId,
            uint primitiveSceneToken,
            uint staticShadowRevision,
            uint textureBindingRevision,
            int cascadeCount,
            int virtualResolution,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold,
            float slopeScaleDepthBias,
            Vector4 shadowCasterState,
            int cameraCullingMask,
            in VividGPULODSelectionContext lodSelectionContext,
            VividGPUCullingContext[] cullingContexts,
            Matrix4x4 cascade0,
            Matrix4x4 cascade1,
            Matrix4x4 cascade2,
            Matrix4x4 cascade3)
        {
            m_CameraEntityId = cameraEntityId;
            m_ClipmapGeneration = 0;
            m_PrimitiveSceneToken = primitiveSceneToken;
            m_StaticShadowRevision = staticShadowRevision;
            m_TextureBindingRevision = textureBindingRevision;
            m_CascadeCount = cascadeCount;
            m_VirtualResolution = virtualResolution;
            m_ForcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
            m_MeshLODErrorThreshold = meshLODErrorThreshold;
            m_SlopeScaleDepthBias = slopeScaleDepthBias;
            m_ShadowCasterState = shadowCasterState;
            m_CameraCullingMask = cameraCullingMask;
            m_LODSelectionContext = lodSelectionContext;
            // Copy values, not the mutable pass-owned array. Ignore inactive cascades.
            m_CullingContext0 = cullingContexts != null && cascadeCount > 0 ? cullingContexts[0] : default;
            m_CullingContext1 = cullingContexts != null && cascadeCount > 1 ? cullingContexts[1] : default;
            m_CullingContext2 = cullingContexts != null && cascadeCount > 2 ? cullingContexts[2] : default;
            m_CullingContext3 = cullingContexts != null && cascadeCount > 3 ? cullingContexts[3] : default;
            m_Cascade0 = cascade0;
            m_Cascade1 = cascade1;
            m_Cascade2 = cascade2;
            m_Cascade3 = cascade3;
        }

        internal bool IsValid => m_CameraEntityId != 0ul
            && m_CascadeCount > 0
            && (m_ClipmapGeneration != 0 || m_CascadeCount <= VividShadowData.MaxCascadeCount)
            && m_VirtualResolution > 0
            && float.IsFinite(m_MeshLODErrorThreshold)
            && float.IsFinite(m_SlopeScaleDepthBias)
            && IsFinite(m_ShadowCasterState)
            && IsFinite(m_LODSelectionContext.ViewProjectionMatrix)
            && math.all(math.isfinite(m_LODSelectionContext.CameraPosition))
            && math.all(math.isfinite(m_LODSelectionContext.CameraUp))
            && math.all(math.isfinite(m_LODSelectionContext.CameraRight))
            && math.all(math.isfinite(m_LODSelectionContext.ScreenSizePixels))
            && IsFinite(m_CullingContext0)
            && IsFinite(m_CullingContext1)
            && IsFinite(m_CullingContext2)
            && IsFinite(m_CullingContext3)
            && IsFinite(m_Cascade0)
            && IsFinite(m_Cascade1)
            && IsFinite(m_Cascade2)
            && IsFinite(m_Cascade3);

        // Clipmap generation covers light identity/basis, level sizes, resolution
        // and stable Z. Integer XY movement is handled by page remapping, not key
        // equality. The actual VSM LOD/culling inputs are derived from that basis.
        internal VirtualShadowMapPrototypeCacheKey(ulong cameraEntityId, uint primitiveSceneToken,
            uint staticShadowRevision, uint textureBindingRevision, int levelCount, int virtualResolution,
            int forcedMeshLODNodeDepth, float meshLODErrorThreshold, float slopeScaleDepthBias,
            Vector4 shadowCasterState, int cameraCullingMask, ulong clipmapGeneration)
            : this(cameraEntityId, primitiveSceneToken, staticShadowRevision, textureBindingRevision,
                levelCount, virtualResolution, forcedMeshLODNodeDepth, meshLODErrorThreshold,
                slopeScaleDepthBias, shadowCasterState, cameraCullingMask, default, null,
                Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity)
        {
            m_ClipmapGeneration = clipmapGeneration;
        }

        internal uint StaticShadowRevision => m_StaticShadowRevision;

        public bool Equals(VirtualShadowMapPrototypeCacheKey other)
        {
            return m_CameraEntityId == other.m_CameraEntityId
                && m_ClipmapGeneration == other.m_ClipmapGeneration
                && m_PrimitiveSceneToken == other.m_PrimitiveSceneToken
                && m_TextureBindingRevision == other.m_TextureBindingRevision
                && m_CascadeCount == other.m_CascadeCount
                && m_VirtualResolution == other.m_VirtualResolution
                && m_ForcedMeshLODNodeDepth == other.m_ForcedMeshLODNodeDepth
                && m_MeshLODErrorThreshold.Equals(other.m_MeshLODErrorThreshold)
                && m_SlopeScaleDepthBias.Equals(other.m_SlopeScaleDepthBias)
                && m_ShadowCasterState.Equals(other.m_ShadowCasterState)
                && m_CameraCullingMask == other.m_CameraCullingMask
                && m_LODSelectionContext.ViewProjectionMatrix.Equals(other.m_LODSelectionContext.ViewProjectionMatrix)
                && m_LODSelectionContext.CameraPosition.Equals(other.m_LODSelectionContext.CameraPosition)
                && m_LODSelectionContext.CameraUp.Equals(other.m_LODSelectionContext.CameraUp)
                && m_LODSelectionContext.CameraRight.Equals(other.m_LODSelectionContext.CameraRight)
                && m_LODSelectionContext.ScreenSizePixels.Equals(other.m_LODSelectionContext.ScreenSizePixels)
                && CullingContextsEqual(m_CullingContext0, other.m_CullingContext0)
                && CullingContextsEqual(m_CullingContext1, other.m_CullingContext1)
                && CullingContextsEqual(m_CullingContext2, other.m_CullingContext2)
                && CullingContextsEqual(m_CullingContext3, other.m_CullingContext3)
                && m_Cascade0.Equals(other.m_Cascade0)
                && m_Cascade1.Equals(other.m_Cascade1)
                && m_Cascade2.Equals(other.m_Cascade2)
                && m_Cascade3.Equals(other.m_Cascade3);
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualShadowMapPrototypeCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(m_CameraEntityId);
            hash.Add(m_ClipmapGeneration);
            hash.Add(m_PrimitiveSceneToken);
            hash.Add(m_TextureBindingRevision);
            hash.Add(m_CascadeCount);
            hash.Add(m_VirtualResolution);
            hash.Add(m_ForcedMeshLODNodeDepth);
            hash.Add(m_MeshLODErrorThreshold);
            hash.Add(m_SlopeScaleDepthBias);
            hash.Add(m_ShadowCasterState);
            hash.Add(m_CameraCullingMask);
            hash.Add(m_LODSelectionContext.ViewProjectionMatrix);
            hash.Add(m_LODSelectionContext.CameraPosition);
            hash.Add(m_LODSelectionContext.CameraUp);
            hash.Add(m_LODSelectionContext.CameraRight);
            hash.Add(m_LODSelectionContext.ScreenSizePixels);
            AddCullingContextHash(ref hash, m_CullingContext0);
            AddCullingContextHash(ref hash, m_CullingContext1);
            AddCullingContextHash(ref hash, m_CullingContext2);
            AddCullingContextHash(ref hash, m_CullingContext3);
            hash.Add(m_Cascade0);
            hash.Add(m_Cascade1);
            hash.Add(m_Cascade2);
            hash.Add(m_Cascade3);
            return hash.ToHashCode();
        }

        // Dispatch offsets and padding only address scratch buffers; they do not
        // change visibility. Compare consumed fields explicitly to avoid boxing.
        private static bool CullingContextsEqual(
            in VividGPUCullingContext left, in VividGPUCullingContext right)
        {
            if (!left.ViewProjectionMatrix.Equals(right.ViewProjectionMatrix)
                || !left.ViewMatrix.Equals(right.ViewMatrix)
                || !left.CameraPosition.Equals(right.CameraPosition)
                || !left.CullingSphereLS.Equals(right.CullingSphereLS)
                || left.PassMask != right.PassMask
                || left.CameraIsPerspective != right.CameraIsPerspective)
                return false;

            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
            {
                if (!left.GetFrustumPlane(planeIndex).Equals(right.GetFrustumPlane(planeIndex)))
                    return false;
            }
            return true;
        }

        private static void AddCullingContextHash(
            ref HashCode hash, in VividGPUCullingContext context)
        {
            hash.Add(context.ViewProjectionMatrix);
            hash.Add(context.ViewMatrix);
            hash.Add(context.CameraPosition);
            hash.Add(context.CullingSphereLS);
            hash.Add(context.PassMask);
            hash.Add(context.CameraIsPerspective);
            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
                hash.Add(context.GetFrustumPlane(planeIndex));
        }

        private static bool IsFinite(in VividGPUCullingContext context)
        {
            if (!IsFinite(context.ViewProjectionMatrix)
                || !IsFinite(context.ViewMatrix)
                || !math.all(math.isfinite(context.CameraPosition))
                || !math.all(math.isfinite(context.CullingSphereLS)))
                return false;

            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
            {
                if (!IsFinite(context.GetFrustumPlane(planeIndex)))
                    return false;
            }
            return true;
        }

        private static bool IsFinite(float4x4 matrix)
        {
            return math.all(math.isfinite(matrix.c0))
                && math.all(math.isfinite(matrix.c1))
                && math.all(math.isfinite(matrix.c2))
                && math.all(math.isfinite(matrix.c3));
        }

        private static bool IsFinite(Vector4 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z)
                && float.IsFinite(value.w);
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int elementIndex = 0; elementIndex < 16; elementIndex++)
            {
                if (!float.IsFinite(matrix[elementIndex]))
                    return false;
            }

            return true;
        }
    }

    internal static class VirtualShadowMapPrototypeRuntime
    {
        internal const int PageSize = 128;
        internal const int MaxPhysicalPageCount = 256;
        internal const int MaxPageRequestsPerMeshlet = 4;
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
        private static GraphicsBuffer s_PreviousPageTable;
        private static GraphicsBuffer s_PreviousPageMetadata;
        private static GraphicsBuffer s_PageTable;
        private static GraphicsBuffer s_PageMetadata;
        private static GraphicsBuffer s_PhysicalPageOwners;
        private static GraphicsBuffer s_AllocatorCounters;
        private static GraphicsBuffer s_MeshletPageRequests;
        private static GraphicsBuffer s_MeshletPageIndirectArgs;
        private static GraphicsBuffer s_MeshletRasterPages;
        private static GraphicsBuffer s_StaticInvalidationBounds;
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
        private static bool s_LoggedUnsupportedPlatform;

        internal static RTHandle StaticPhysicalPage => s_StaticPhysicalPage;
        internal static RTHandle DynamicPhysicalPage => s_DynamicPhysicalPage;
        internal static RTHandle RasterDepth => s_RasterDepth;
        internal static RTHandle UnityRasterDepth => s_UnityRasterDepth;
        internal static GraphicsBuffer PreviousPageTable => s_PreviousPageTable;
        internal static GraphicsBuffer PreviousPageMetadata => s_PreviousPageMetadata;
        internal static GraphicsBuffer PageTable => s_PageTable;
        internal static GraphicsBuffer PageMetadata => s_PageMetadata;
        internal static GraphicsBuffer PhysicalPageOwners => s_PhysicalPageOwners;
        internal static GraphicsBuffer AllocatorCounters => s_AllocatorCounters;
        internal static GraphicsBuffer MeshletPageRequests =>
            s_MeshletPageRequests;
        internal static GraphicsBuffer MeshletPageIndirectArgs =>
            s_MeshletPageIndirectArgs;
        internal static GraphicsBuffer MeshletRasterPages => s_MeshletRasterPages;
        internal static GraphicsBuffer StaticInvalidationBounds =>
            s_StaticInvalidationBounds;
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
                    MaxPhysicalPageCount + 1,
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
        {
            if (!invalidationBounds.IsCreated || invalidationBounds.Length <= 0)
                return false;

            if (s_StaticInvalidationBounds == null
                || !s_StaticInvalidationBounds.IsValid()
                || s_StaticInvalidationBounds.count < invalidationBounds.Length
                || s_StaticInvalidationBounds.stride != StaticInvalidationBoundsStride
                || s_StaticInvalidationBounds.target != GraphicsBuffer.Target.Structured)
            {
                s_StaticInvalidationBounds?.Dispose();
                s_StaticInvalidationBounds = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    invalidationBounds.Length,
                    StaticInvalidationBoundsStride)
                {
                    name = "VSMPrototypeStaticInvalidationBounds",
                };
            }

            s_StaticInvalidationBounds.SetData(
                invalidationBounds,
                0,
                0,
                invalidationBounds.Length);
            return s_StaticInvalidationBounds.IsValid();
        }

        internal static bool EnsureResources(int virtualResolution, int cascadeCount)
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
                resolvedCascadeCount);
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
                && s_DynamicPhysicalPage != null
                && s_DynamicPhysicalPage.rt != null
                && s_DynamicPhysicalPage.rt.width == physicalPageWidth
                && s_DynamicPhysicalPage.rt.height == physicalPageHeight
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
                && s_PreviousPageTable != null && s_PreviousPageTable.IsValid()
                && s_PreviousPageTable.count == pageTableEntryCount
                && s_PreviousPageMetadata != null && s_PreviousPageMetadata.IsValid()
                && s_PreviousPageMetadata.count == pageTableEntryCount
                && s_PageTableUpload?.Length == pageTableEntryCount
                && s_PageMetadata != null
                && s_PageMetadata.IsValid()
                && s_PageMetadata.count == pageTableEntryCount
                && s_PageMetadataUpload?.Length == pageTableEntryCount
                && s_PhysicalPageOwners != null
                && s_PhysicalPageOwners.IsValid()
                && s_PhysicalPageOwners.count == physicalPageCapacity
                && s_PhysicalPageOwnersUpload?.Length == physicalPageCapacity
                && s_AllocatorCounters != null
                && s_AllocatorCounters.IsValid()
                && s_AllocatorCounters.count == s_AllocatorCountersUpload.Length
                && s_VirtualResolution == resolvedResolution
                && s_CascadeCount == resolvedCascadeCount
                && s_PagesPerAxis == pagesPerAxis
                && s_PhysicalPagesPerRow == physicalPagesPerRow
                && s_PhysicalPageCapacity == physicalPageCapacity
                && s_PhysicalPageWidth == physicalPageWidth
                && s_PhysicalPageHeight == physicalPageHeight;
            if (configurationMatches)
                return true;

            ReleaseAllocatedResources();

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
            s_PreviousPageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination, pageTableEntryCount, 4);
            s_PreviousPageTable.name = "VSMPreviousPageTable";
            s_PreviousPageMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination, pageTableEntryCount, 16);
            s_PreviousPageMetadata.name = "VSMPreviousPageMetadata";

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
            return HasPageDebugSnapshot(cameraEntityId, frameIndex)
                && cameraEntityId == s_LastReceiverFeedbackCameraEntityId
                && frameIndex == s_LastReceiverFeedbackFrame;
        }

        internal static bool HasReceiverFeedbackForFrame(ulong cameraEntityId, int frameIndex)
        {
            return cameraEntityId != 0ul
                && cameraEntityId == s_LastReceiverFeedbackCameraEntityId
                && frameIndex > 0
                && s_LastReceiverFeedbackFrame == frameIndex - 1;
        }

        internal static bool RequiresReceiverFeedbackReset(ulong cameraEntityId, int frameIndex)
        {
            // The metadata is shared. Drop requests and their timestamps when a
            // different producer takes over, or a frame is repeated/rewound.
            return !HasReceiverFeedback
                || cameraEntityId != s_LastReceiverFeedbackCameraEntityId
                || frameIndex <= s_LastReceiverFeedbackFrame;
        }

        internal static void MarkReceiverFeedbackProduced(ulong cameraEntityId, int frameIndex)
        {
            bool valid = cameraEntityId != 0ul && frameIndex >= 0;
            s_LastReceiverFeedbackCameraEntityId = valid ? cameraEntityId : 0ul;
            s_LastReceiverFeedbackFrame = valid ? frameIndex : -1;
        }

        internal static void InvalidateCache()
        {
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
            int cascadeCount)
        {
            int resolvedPagesPerAxis = Mathf.Max(1, pagesPerAxis);
            int resolvedCascadeCount = Mathf.Max(1, cascadeCount);
            return Mathf.Min(
                resolvedPagesPerAxis
                    * resolvedPagesPerAxis
                    * resolvedCascadeCount,
                MaxPhysicalPageCount);
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

        private static void ReleaseAllocatedResources()
        {
            Projections.InvalidateLayout();
            s_PreviousPageTable?.Dispose();
            s_PreviousPageTable = null;
            s_PreviousPageMetadata?.Dispose();
            s_PreviousPageMetadata = null;
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
            s_PhysicalPageOwners?.Dispose();
            s_PhysicalPageOwners = null;
            s_AllocatorCounters?.Dispose();
            s_AllocatorCounters = null;
            s_MeshletPageRequests?.Dispose();
            s_MeshletPageRequests = null;
            s_MeshletPageIndirectArgs?.Dispose();
            s_MeshletPageIndirectArgs = null;
            s_MeshletRasterPages?.Dispose();
            s_MeshletRasterPages = null;
            s_StaticInvalidationBounds?.Dispose();
            s_StaticInvalidationBounds = null;
            s_PageTableUpload = null;
            s_PageMetadataUpload = null;
            s_PhysicalPageOwnersUpload = null;
            MarkReceiverFeedbackProduced(0ul, -1);
        }

        private static RTHandle AllocatePhysicalPage(
            int width,
            int height,
            string name)
        {
            return RTHandles.Alloc(
                width,
                height,
                slices: 1,
                depthBufferBits: DepthBits.None,
                colorFormat: GraphicsFormat.R32_UInt,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                dimension: TextureDimension.Tex2D,
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
