using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.RenderPass.Core
{
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
        private static readonly int VSMPrototypePageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");
        private static readonly int VSMPrototypeVirtualResolutionId = Shader.PropertyToID("_VSMPrototypeVirtualResolution");
        private static readonly int VSMPrototypePagesPerAxisId = Shader.PropertyToID("_VSMPrototypePagesPerAxis");
        private static readonly int VSMPrototypePhysicalPagesPerRowId = Shader.PropertyToID("_VSMPrototypePhysicalPagesPerRow");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";
        private static readonly string s_VirtualShadowMapPrototypeKeyword = "VIVID_VSM_PROTOTYPE";

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
        private bool m_VirtualShadowMapPrototypeActive;
        private bool m_VirtualShadowMapPrototypeNeedsCacheRefresh;
        private float m_SlopeScaleDepthBias;
        private VirtualShadowMapPrototypeCacheKey m_VirtualShadowMapPrototypeCacheKey;

        private CullingResults m_CullingResults;
        private ScriptableRenderContext m_RenderContext;
        private VividShadowData m_ShadowData;
        private Camera m_LODCamera;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private VividPrimitiveDrawSet m_PrimitiveShadowDrawSet;
        private int m_FrameIndex;

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
                    s_VirtualShadowMapPrototypeKeyword,
                    true);
                m_VirtualShadowMapPrototypeMaterials[rendererListIndex] = vsmMaterial;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            VirtualShadowMapPrototypeRuntime.SetFramePrepared(false);
            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_CascadeCount = 0;
            m_MainLightVisibleIndex = -1;
            m_HasUnityShadowCasters = false;
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapPrototypeNeedsCacheRefresh = false;
            m_SlopeScaleDepthBias = 0.0f;
            m_VirtualShadowMapPrototypeCacheKey = default;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
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
            m_CullingResults = renderingData.cullingResults;
            m_RenderContext = renderingData.context;
            m_MainLightVisibleIndex = shadowData.mainLightVisibleIndex;
            m_HasUnityShadowCasters = shadowData.hasUnityShadowCasters;
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
                if (!m_IsActive || !m_ShadowAtlas.innerHandle.IsValid())
                    return;

                bool canDrawMeshlets = TryPrepareMeshletShadowDraws(
                    nativeCmd,
                    out VividGPUDrivenSystem gpuDrivenSystem,
                    out GraphicsBuffer requestsBuffer,
                    out GraphicsBuffer argsBuffer,
                    out bool virtualTextureReady,
                    out VirtualTextureSpaceBinding virtualTextureBinding);

                ConstantBuffer.PushGlobal(
                    nativeCmd,
                    m_ShadowMatrices,
                    ShadowMatricesConstantBufferId);
                nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowData.shadowCasterState);
                nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);

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
                            gpuDrivenSystem,
                            requestsBuffer,
                            argsBuffer,
                            virtualTextureReady,
                            virtualTextureBinding,
                            cascadeIndex,
                            m_Materials);
                    }
                }

                if (m_VirtualShadowMapPrototypeActive)
                {
                    DrawVirtualShadowMapPrototypePages(
                        nativeCmd,
                        gpuDrivenSystem,
                        requestsBuffer,
                        argsBuffer,
                        canDrawMeshlets,
                        virtualTextureReady,
                        virtualTextureBinding);
                }

                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
            }
        }

        private void PrepareVirtualShadowMapPrototype(VividCameraData cameraData)
        {
            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            if (settings == null
                || !settings.enableVirtualShadowMapPrototype.value
                || !m_MeshletRenderingActive
                || !VirtualShadowMapPrototypeRuntime.EnsureResources(
                    m_CascadeResolution,
                    m_CascadeCount))
            {
                return;
            }

            var gpuDrivenSystem = VividGPUDrivenSystem.instance;
            var rendererDatabase = VividMeshletRendererDatabase.instance;
            m_VirtualShadowMapPrototypeCacheKey = new VirtualShadowMapPrototypeCacheKey(
                EntityId.ToULong(cameraData.camera.GetEntityId()),
                gpuDrivenSystem.PrimitiveScene.SceneToken,
                gpuDrivenSystem.PrimitiveScene.SceneRevision,
                gpuDrivenSystem.ShadowCacheRevision,
                rendererDatabase.StructureRevision,
                rendererDatabase.ResourceRevision,
                rendererDatabase.InstanceRevision,
                gpuDrivenSystem.TextureBindingRevision,
                m_CascadeCount,
                m_CascadeResolution,
                gpuDrivenSystem.ForcedMeshLODNodeDepth,
                gpuDrivenSystem.MeshLODErrorThreshold,
                m_SlopeScaleDepthBias,
                m_ShadowData.shadowCasterState,
                m_ShadowMatrices.Cascade0,
                m_ShadowMatrices.Cascade1,
                m_ShadowMatrices.Cascade2,
                m_ShadowMatrices.Cascade3);
            if (!m_VirtualShadowMapPrototypeCacheKey.IsValid)
                return;

            m_VirtualShadowMapPrototypeNeedsCacheRefresh =
                VirtualShadowMapPrototypeRuntime.RequiresCacheRefresh(
                    m_VirtualShadowMapPrototypeCacheKey);

            PassRecorder.ImportTextureForPass(
                this,
                VirtualShadowMapPrototypeRuntime.PhysicalPage,
                AccessFlags.ReadWrite);
            PassRecorder.ImportTextureForPass(
                this,
                VirtualShadowMapPrototypeRuntime.RasterDepth,
                AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(
                this,
                VirtualShadowMapPrototypeRuntime.PageTable,
                AccessFlags.ReadWrite);
            m_VirtualShadowMapPrototypeActive = true;
            VirtualShadowMapPrototypeRuntime.SetFramePrepared(true);
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
            m_FrameIndex = cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;
            m_PrimitiveShadowDrawSet = frameData
                .GetOrCreate<VividGPUDrivenFrameData>()
                .primitiveShadowDrawSet;
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
            m_MeshletRenderingActive = true;
        }

        private bool TryPrepareMeshletShadowDraws(
            CommandBuffer nativeCmd,
            out VividGPUDrivenSystem system,
            out GraphicsBuffer requestsBuffer,
            out GraphicsBuffer argsBuffer,
            out bool virtualTextureReady,
            out VirtualTextureSpaceBinding virtualTextureBinding)
        {
            system = null;
            requestsBuffer = null;
            argsBuffer = null;
            virtualTextureReady = false;
            virtualTextureBinding = default;
            if (!m_MeshletRenderingActive
                || m_ShadowData == null
                || m_LODCamera == null
                || !VividGPUDrivenSystem.HasInstance)
            {
                return false;
            }

            system = VividGPUDrivenSystem.instance;
            if (!system.IsAvailable
                || system.SceneData == null
                || system.SceneData.InstanceCount == 0)
            {
                return false;
            }

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null
                || resources.GPUInstanceCullingCompute == null
                || resources.MeshletListBuildCompute == null
                || resources.GPUMeshletCullingCompute == null
                || resources.FixupVisibleMeshletIndirectDrawArgsCompute == null)
            {
                return false;
            }

            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
                system.ConfigureTextureBackendKeyword(m_Materials[materialIndex]);

            virtualTextureReady = !system.UsesVirtualTexture
                || GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                    nativeCmd,
                    m_VirtualTextureFrameData,
                    m_VirtualTextureSpaceParams,
                    m_VirtualTextureMipOffsets,
                    m_VirtualTextureLayerFallbacks,
                    m_FrameIndex,
                    feedbackSampleRate: 1,
                    out virtualTextureBinding);

            // Keep shadow LOD selection synchronized with the main camera while deriving
            // frustum planes from the unified cascade matrices.
            m_LODCamera.BuildLODSelectionContext(out var lodContext);
            for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
            {
                BuildShadowCullingContext(
                    cascadeIndex,
                    out m_ShadowCullingContexts[cascadeIndex]);
            }

            VividPrimitiveDrawSet shadowDrawSet = system.CompleteShadowDrawSet(
                m_PrimitiveShadowDrawSet,
                m_LODCamera,
                m_FrameIndex);
            system.CullShadowCascades(
                nativeCmd,
                m_ShadowCullingContexts,
                m_CascadeCount,
                lodContext,
                resources.GPUInstanceCullingCompute,
                resources.MeshletListBuildCompute,
                resources.GPUMeshletCullingCompute,
                resources.FixupVisibleMeshletIndirectDrawArgsCompute,
                shadowDrawSet);

            requestsBuffer = system.GetShadowVisibleMeshletRenderRequestsBuffer(0);
            argsBuffer = system.GetShadowVisibleMeshletIndirectDrawArgsBuffer(0);
            return requestsBuffer != null && argsBuffer != null;
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

        private void DrawVirtualShadowMapPrototypePages(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            GraphicsBuffer requestsBuffer,
            GraphicsBuffer argsBuffer,
            bool canDrawMeshlets,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding)
        {
            RTHandle physicalPage = VirtualShadowMapPrototypeRuntime.PhysicalPage;
            RTHandle rasterDepth = VirtualShadowMapPrototypeRuntime.RasterDepth;
            GraphicsBuffer pageTable = VirtualShadowMapPrototypeRuntime.PageTable;
            uint[] pageTableUpload = VirtualShadowMapPrototypeRuntime.PageTableUpload;
            int pageTableEntryCount = VirtualShadowMapPrototypeRuntime.PageTableEntryCount;
            if (physicalPage == null
                || rasterDepth == null
                || pageTable == null
                || pageTableUpload == null
                || pageTableEntryCount <= 0)
            {
                return;
            }

            if (!m_VirtualShadowMapPrototypeNeedsCacheRefresh
                && VirtualShadowMapPrototypeRuntime.TryUseCachedPages(
                    m_VirtualShadowMapPrototypeCacheKey))
            {
                VirtualShadowMapPrototypeRuntime.SetFrameActive(true);
                return;
            }

            if (!canDrawMeshlets || !virtualTextureReady || system == null)
                return;

            CoreUtils.SetRenderTarget(
                nativeCmd,
                physicalPage,
                ClearFlag.Color,
                Color.clear);

            nativeCmd.SetBufferData(
                pageTable,
                pageTableUpload,
                0,
                0,
                pageTableEntryCount);
            nativeCmd.SetGlobalBuffer(VSMPrototypePageTableId, pageTable);
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

            for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
            {
                CoreUtils.SetRenderTarget(
                    nativeCmd,
                    rasterDepth,
                    ClearFlag.Depth,
                    Color.black,
                    depthSlice: cascadeIndex);
                nativeCmd.SetRandomWriteTarget(0, physicalPage);
                DrawMeshletShadowCascade(
                    nativeCmd,
                    system,
                    requestsBuffer,
                    argsBuffer,
                    virtualTextureReady,
                    virtualTextureBinding,
                    cascadeIndex,
                    m_VirtualShadowMapPrototypeMaterials);
            }

            nativeCmd.ClearRandomWriteTargets();
            VirtualShadowMapPrototypeRuntime.CommitCache(
                m_VirtualShadowMapPrototypeCacheKey);
            VirtualShadowMapPrototypeRuntime.SetFrameActive(true);
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
                s_AlphaTestKeyword,
                (rendererListID & VividRendererListID.AlphaTest) != 0);
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
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapPrototypeNeedsCacheRefresh = false;
            m_CascadeCount = 0;
            m_SlopeScaleDepthBias = 0.0f;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
            m_FrameIndex = 0;
            m_ShadowMatrices = default;
            m_VirtualShadowMapPrototypeCacheKey = default;
            VirtualShadowMapPrototypeRuntime.SetFramePrepared(false);
            VirtualShadowMapPrototypeRuntime.ReleaseResources();
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

    internal readonly struct VirtualShadowMapPrototypeCacheKey
        : IEquatable<VirtualShadowMapPrototypeCacheKey>
    {
        private readonly ulong m_CameraEntityId;
        private readonly uint m_PrimitiveSceneToken;
        private readonly uint m_PrimitiveSceneRevision;
        private readonly uint m_GPUDrivenShadowRevision;
        private readonly uint m_RendererStructureRevision;
        private readonly uint m_RendererResourceRevision;
        private readonly uint m_RendererInstanceRevision;
        private readonly uint m_TextureBindingRevision;
        private readonly int m_CascadeCount;
        private readonly int m_VirtualResolution;
        private readonly int m_ForcedMeshLODNodeDepth;
        private readonly float m_MeshLODErrorThreshold;
        private readonly float m_SlopeScaleDepthBias;
        private readonly Vector4 m_ShadowCasterState;
        private readonly Matrix4x4 m_Cascade0;
        private readonly Matrix4x4 m_Cascade1;
        private readonly Matrix4x4 m_Cascade2;
        private readonly Matrix4x4 m_Cascade3;

        internal VirtualShadowMapPrototypeCacheKey(
            ulong cameraEntityId,
            uint primitiveSceneToken,
            uint primitiveSceneRevision,
            uint gpuDrivenShadowRevision,
            uint rendererStructureRevision,
            uint rendererResourceRevision,
            uint rendererInstanceRevision,
            uint textureBindingRevision,
            int cascadeCount,
            int virtualResolution,
            int forcedMeshLODNodeDepth,
            float meshLODErrorThreshold,
            float slopeScaleDepthBias,
            Vector4 shadowCasterState,
            Matrix4x4 cascade0,
            Matrix4x4 cascade1,
            Matrix4x4 cascade2,
            Matrix4x4 cascade3)
        {
            m_CameraEntityId = cameraEntityId;
            m_PrimitiveSceneToken = primitiveSceneToken;
            m_PrimitiveSceneRevision = primitiveSceneRevision;
            m_GPUDrivenShadowRevision = gpuDrivenShadowRevision;
            m_RendererStructureRevision = rendererStructureRevision;
            m_RendererResourceRevision = rendererResourceRevision;
            m_RendererInstanceRevision = rendererInstanceRevision;
            m_TextureBindingRevision = textureBindingRevision;
            m_CascadeCount = cascadeCount;
            m_VirtualResolution = virtualResolution;
            m_ForcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
            m_MeshLODErrorThreshold = meshLODErrorThreshold;
            m_SlopeScaleDepthBias = slopeScaleDepthBias;
            m_ShadowCasterState = shadowCasterState;
            m_Cascade0 = cascade0;
            m_Cascade1 = cascade1;
            m_Cascade2 = cascade2;
            m_Cascade3 = cascade3;
        }

        internal bool IsValid => m_CameraEntityId != 0ul
            && m_PrimitiveSceneToken != 0u
            && m_CascadeCount > 0
            && m_CascadeCount <= VividShadowData.MaxCascadeCount
            && m_VirtualResolution > 0
            && float.IsFinite(m_MeshLODErrorThreshold)
            && float.IsFinite(m_SlopeScaleDepthBias)
            && IsFinite(m_ShadowCasterState)
            && IsFinite(m_Cascade0)
            && IsFinite(m_Cascade1)
            && IsFinite(m_Cascade2)
            && IsFinite(m_Cascade3);

        public bool Equals(VirtualShadowMapPrototypeCacheKey other)
        {
            return m_CameraEntityId == other.m_CameraEntityId
                && m_PrimitiveSceneToken == other.m_PrimitiveSceneToken
                && m_PrimitiveSceneRevision == other.m_PrimitiveSceneRevision
                && m_GPUDrivenShadowRevision == other.m_GPUDrivenShadowRevision
                && m_RendererStructureRevision == other.m_RendererStructureRevision
                && m_RendererResourceRevision == other.m_RendererResourceRevision
                && m_RendererInstanceRevision == other.m_RendererInstanceRevision
                && m_TextureBindingRevision == other.m_TextureBindingRevision
                && m_CascadeCount == other.m_CascadeCount
                && m_VirtualResolution == other.m_VirtualResolution
                && m_ForcedMeshLODNodeDepth == other.m_ForcedMeshLODNodeDepth
                && m_MeshLODErrorThreshold.Equals(other.m_MeshLODErrorThreshold)
                && m_SlopeScaleDepthBias.Equals(other.m_SlopeScaleDepthBias)
                && m_ShadowCasterState.Equals(other.m_ShadowCasterState)
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
            hash.Add(m_PrimitiveSceneToken);
            hash.Add(m_PrimitiveSceneRevision);
            hash.Add(m_GPUDrivenShadowRevision);
            hash.Add(m_RendererStructureRevision);
            hash.Add(m_RendererResourceRevision);
            hash.Add(m_RendererInstanceRevision);
            hash.Add(m_TextureBindingRevision);
            hash.Add(m_CascadeCount);
            hash.Add(m_VirtualResolution);
            hash.Add(m_ForcedMeshLODNodeDepth);
            hash.Add(m_MeshLODErrorThreshold);
            hash.Add(m_SlopeScaleDepthBias);
            hash.Add(m_ShadowCasterState);
            hash.Add(m_Cascade0);
            hash.Add(m_Cascade1);
            hash.Add(m_Cascade2);
            hash.Add(m_Cascade3);
            return hash.ToHashCode();
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

        private static RTHandle s_PhysicalPage;
        private static RTHandle s_RasterDepth;
        private static GraphicsBuffer s_PageTable;
        private static uint[] s_PageTableUpload;
        private static int s_VirtualResolution;
        private static int s_CascadeCount;
        private static int s_PagesPerAxis;
        private static int s_PhysicalPagesPerRow;
        private static int s_PhysicalPageWidth;
        private static int s_PhysicalPageHeight;
        private static bool s_FramePrepared;
        private static bool s_FrameActive;
        private static bool s_CacheValid;
        private static bool s_LastFrameUsedCache;
        private static int s_CacheHitCount;
        private static int s_CacheRefreshCount;
        private static VirtualShadowMapPrototypeCacheKey s_CachedKey;
        private static bool s_LoggedUnsupportedPlatform;

        internal static RTHandle PhysicalPage => s_PhysicalPage;
        internal static RTHandle RasterDepth => s_RasterDepth;
        internal static GraphicsBuffer PageTable => s_PageTable;
        internal static uint[] PageTableUpload => s_PageTableUpload;
        internal static int PageTableEntryCount => s_PageTableUpload?.Length ?? 0;
        internal static int VirtualResolution => s_VirtualResolution;
        internal static int PagesPerAxis => s_PagesPerAxis;
        internal static int PhysicalPagesPerRow => s_PhysicalPagesPerRow;
        internal static bool IsFramePrepared => s_FramePrepared;
        internal static bool IsFrameActive => s_FrameActive;
        internal static bool IsCacheValid => s_CacheValid;
        internal static bool LastFrameUsedCache => s_LastFrameUsedCache;
        internal static int CacheHitCount => s_CacheHitCount;
        internal static int CacheRefreshCount => s_CacheRefreshCount;

        internal static bool EnsurePhysicalPageForBinding()
        {
            if (s_PageTable == null || !s_PageTable.IsValid())
            {
                s_PageTable?.Dispose();
                s_PageTable = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    sizeof(uint));
                s_PageTable.name = "VSMPrototypePageTable";
                s_PageTableUpload = new[] { 1u };
                s_PageTable.SetData(s_PageTableUpload);
                InvalidateCache();
            }

            bool supportsFormat = IsPhysicalPageFormatSupported();
            if (!supportsFormat)
                return false;

            if (s_PhysicalPage == null || s_PhysicalPage.rt == null)
            {
                s_PhysicalPage?.Release();
                s_PhysicalPage = RTHandles.Alloc(
                    PageSize,
                    PageSize,
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
                    name: "VSMPrototypePhysicalPage");
                InvalidateCache();
            }

            return s_PhysicalPage?.rt != null
                && s_PageTable?.IsValid() == true;
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

            int resolvedResolution = Mathf.Max(PageSize, virtualResolution);
            int resolvedCascadeCount = Mathf.Clamp(
                cascadeCount,
                1,
                VividShadowData.MaxCascadeCount);
            int pagesPerAxis = CalculatePagesPerAxis(resolvedResolution);
            int cascadeColumns = Mathf.Min(2, resolvedCascadeCount);
            int cascadeRows = CoreUtils.DivRoundUp(
                resolvedCascadeCount,
                cascadeColumns);
            int physicalPagesPerRow = pagesPerAxis * cascadeColumns;
            int physicalPageWidth = physicalPagesPerRow * PageSize;
            int physicalPageHeight = pagesPerAxis * cascadeRows * PageSize;
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
            int pageTableEntryCount = pagesPerAxis
                * pagesPerAxis
                * resolvedCascadeCount;
            bool configurationMatches = s_PhysicalPage != null
                && s_PhysicalPage.rt != null
                && s_PhysicalPage.rt.width == physicalPageWidth
                && s_PhysicalPage.rt.height == physicalPageHeight
                && s_RasterDepth != null
                && s_RasterDepth.rt != null
                && s_RasterDepth.rt.width == resolvedResolution
                && s_RasterDepth.rt.height == resolvedResolution
                && s_RasterDepth.rt.volumeDepth == resolvedCascadeCount
                && s_PageTable != null
                && s_PageTable.IsValid()
                && s_PageTable.count == pageTableEntryCount
                && s_PageTableUpload?.Length == pageTableEntryCount
                && s_VirtualResolution == resolvedResolution
                && s_CascadeCount == resolvedCascadeCount
                && s_PagesPerAxis == pagesPerAxis
                && s_PhysicalPagesPerRow == physicalPagesPerRow
                && s_PhysicalPageWidth == physicalPageWidth
                && s_PhysicalPageHeight == physicalPageHeight;
            if (configurationMatches)
                return true;

            ReleaseAllocatedResources();

            s_VirtualResolution = resolvedResolution;
            s_CascadeCount = resolvedCascadeCount;
            s_PagesPerAxis = pagesPerAxis;
            s_PhysicalPagesPerRow = physicalPagesPerRow;
            s_PhysicalPageWidth = physicalPageWidth;
            s_PhysicalPageHeight = physicalPageHeight;

            s_PhysicalPage = RTHandles.Alloc(
                physicalPageWidth,
                physicalPageHeight,
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
                name: "VSMPrototypePhysicalPage");

            s_RasterDepth = RTHandles.Alloc(
                resolvedResolution,
                resolvedResolution,
                slices: resolvedCascadeCount,
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

            s_PageTableUpload = BuildFullyResidentPageTable(
                pagesPerAxis,
                resolvedCascadeCount);
            s_PageTable = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                s_PageTableUpload.Length,
                sizeof(uint));
            s_PageTable.name = "VSMPrototypePageTable";
            s_PageTable.SetData(s_PageTableUpload);
            return s_PhysicalPage != null
                && s_PhysicalPage.rt != null
                && s_RasterDepth != null
                && s_RasterDepth.rt != null
                && s_PageTable != null
                && s_PageTable.IsValid();
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
            ReleaseAllocatedResources();
            s_VirtualResolution = 0;
            s_CascadeCount = 0;
            s_PagesPerAxis = 0;
            s_PhysicalPagesPerRow = 0;
            s_PhysicalPageWidth = 0;
            s_PhysicalPageHeight = 0;
            s_FramePrepared = false;
            s_FrameActive = false;
            s_CacheHitCount = 0;
            s_CacheRefreshCount = 0;
        }

        internal static void SetFrameActive(bool active)
        {
            s_FrameActive = active;
            if (!active)
                s_LastFrameUsedCache = false;
        }

        internal static void SetFramePrepared(bool prepared)
        {
            s_FramePrepared = prepared;
            if (!prepared)
                SetFrameActive(false);
        }

        internal static bool RequiresCacheRefresh(
            in VirtualShadowMapPrototypeCacheKey key)
        {
            return !key.IsValid || !s_CacheValid || !s_CachedKey.Equals(key);
        }

        internal static bool TryUseCachedPages(
            in VirtualShadowMapPrototypeCacheKey key)
        {
            if (RequiresCacheRefresh(key))
                return false;

            s_LastFrameUsedCache = true;
            s_CacheHitCount++;
            return true;
        }

        internal static void CommitCache(
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
            s_CacheRefreshCount++;
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

        internal static uint[] BuildFullyResidentPageTable(
            int pagesPerAxis,
            int cascadeCount)
        {
            int resolvedPagesPerAxis = Mathf.Max(1, pagesPerAxis);
            int resolvedCascadeCount = Mathf.Clamp(
                cascadeCount,
                1,
                VividShadowData.MaxCascadeCount);
            int cascadeColumns = Mathf.Min(2, resolvedCascadeCount);
            int physicalPagesPerRow = resolvedPagesPerAxis * cascadeColumns;
            int pagesPerCascade = resolvedPagesPerAxis * resolvedPagesPerAxis;
            var pageTable = new uint[pagesPerCascade * resolvedCascadeCount];

            for (int cascadeIndex = 0; cascadeIndex < resolvedCascadeCount; cascadeIndex++)
            {
                int cascadeOffsetX = cascadeIndex % cascadeColumns;
                int cascadeOffsetY = cascadeIndex / cascadeColumns;
                for (int pageY = 0; pageY < resolvedPagesPerAxis; pageY++)
                {
                    for (int pageX = 0; pageX < resolvedPagesPerAxis; pageX++)
                    {
                        int virtualPageIndex = cascadeIndex * pagesPerCascade
                            + pageY * resolvedPagesPerAxis
                            + pageX;
                        int physicalPageX = cascadeOffsetX * resolvedPagesPerAxis + pageX;
                        int physicalPageY = cascadeOffsetY * resolvedPagesPerAxis + pageY;
                        int physicalPageIndex = physicalPageY * physicalPagesPerRow
                            + physicalPageX;
                        pageTable[virtualPageIndex] = (uint)physicalPageIndex + 1u;
                    }
                }
            }

            return pageTable;
        }

        private static void ReleaseAllocatedResources()
        {
            InvalidateCache();
            s_PhysicalPage?.Release();
            s_PhysicalPage = null;
            s_RasterDepth?.Release();
            s_RasterDepth = null;
            s_PageTable?.Dispose();
            s_PageTable = null;
            s_PageTableUpload = null;
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
