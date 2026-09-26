using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.PrimitiveScene;
using VividRP.Runtime.VirtualShadowMap;

namespace VividRP.Runtime.RenderPass.Core
{
    public abstract class ShadowCasterPass : UnsafePass
    {
        internal const string ShadowCasterShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferShadowCasterPass";

        private protected const int RendererListCount = (int)VividRendererListID.Count;

        private protected static readonly int s_CullId = Shader.PropertyToID("_Cull");

        private protected static readonly int s_UnityIndirectDrawArgsId = Shader.PropertyToID("unity_IndirectDrawArgs");

        private protected static readonly int s_UnityBaseCommandIdId = Shader.PropertyToID("unity_BaseCommandID");

        private protected static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");

        private protected const string AlphaTestKeywordName = "_ALPHATEST_ON";

        private protected readonly Material[] m_Materials = new Material[RendererListCount];

        private protected readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();

        private protected readonly float[] m_VirtualTextureSpaceParams =
            new float[VirtualTextureSpaceShaderParams.IntCount];

        private protected readonly float[] m_VirtualTextureMipOffsets =
            new float[VirtualTextureFeedbackProcessor.MaxMipCount];

        private protected readonly Vector4[] m_VirtualTextureLayerFallbacks =
            new Vector4[VTStackDesc.MaxLayerCount];

        private protected bool m_IsActive;

        private protected bool m_MeshletRenderingActive;

        private protected int m_MainLightVisibleIndex = -1;

        private protected bool m_HasUnityShadowCasters;

        private protected bool m_HasMeshletShadowCasters;

        private protected CullingResults m_CullingResults;

        private protected ScriptableRenderContext m_RenderContext;

        private protected VividShadowData m_ShadowData;

        private protected Camera m_LODCamera;

        private protected VividVirtualTextureFrameData m_VirtualTextureFrameData;

        private protected VividPrimitiveDrawSet m_PrimitiveShadowDrawSet;

        private protected VividPrimitiveDrawSet m_StaticPrimitiveShadowDrawSet;

        private protected VividPrimitiveDrawSet m_DynamicPrimitiveShadowDrawSet;

        private protected VividGPULODSelectionContext m_ShadowLODSelectionContext;

        private protected int m_FrameIndex;

        private protected ShadowCasterPass(string passName)
        {
            profilingSampler = new ProfilingSampler(passName);
        }

        public override void Create()
        {
            PassRecorder.RegisterCascadedShadowCasterPass();
            Shader shader = Shader.Find(ShadowCasterShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ShadowCasterShaderName}' for {GetType().Name}.");
                return;
            }
            for (int i = 0; i < m_Materials.Length; i++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{GetType().Name}_{(VividRendererListID)i}";
                ConfigureMaterial(material, (VividRendererListID)i);
                m_Materials[i] = material;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_MainLightVisibleIndex = -1;
            m_HasUnityShadowCasters = false;
            m_HasMeshletShadowCasters = false;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
            m_StaticPrimitiveShadowDrawSet = null;
            m_DynamicPrimitiveShadowDrawSet = null;
            m_ShadowLODSelectionContext = default;
            m_FrameIndex = 0;

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive || shadowData.cascadeCount <= 0 || shadowData.cascadeResolution <= 0)
                return;
            var renderingData = frameData.GetOrCreate<VividRenderingData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_FrameIndex = cameraData.frameIndex >= 0 ? cameraData.frameIndex : Time.frameCount;
            m_CullingResults = renderingData.cullingResults;
            m_RenderContext = renderingData.context;
            m_MainLightVisibleIndex = shadowData.mainLightVisibleIndex;
            m_HasUnityShadowCasters = shadowData.hasUnityShadowCasters;
            m_HasMeshletShadowCasters = shadowData.hasPrimitiveShadowCasters;
            m_ShadowData = shadowData;
            m_IsActive = true;
            PrepareMeshletRendering(frameData, cameraData);
        }

        public override void Dispose()
        {
            for (int i = 0; i < m_Materials.Length; i++)
            {
                CoreUtils.Destroy(m_Materials[i]);
                m_Materials[i] = null;
            }
            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
            m_StaticPrimitiveShadowDrawSet = null;
            m_DynamicPrimitiveShadowDrawSet = null;
        }

        private protected void PrepareMeshletRendering(
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

        private protected bool TryPrepareMeshletShadowDraws(
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

        private protected bool TryPrepareMeshletShadowPoolDraws(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            VividPrimitiveDrawSet drawSet,
            out GraphicsBuffer requestsBuffer,
            out GraphicsBuffer argsBuffer,
            out bool hasDraws,
            VividGPUCullingContext[] cullingContexts,
            int projectionCount,
            in VividGPULODSelectionContext lodContext,
            VirtualShadowMapCullingParameters vsmCulling = default)
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
                cullingContexts,
                projectionCount,
                lodContext,
                resources.GPUInstanceCullingCompute,
                resources.MeshletListBuildCompute,
                resources.GPUMeshletCullingCompute,
                resources.FixupVisibleMeshletIndirectDrawArgsCompute,
                drawSet, vsmCulling);
            requestsBuffer = system.GetShadowVisibleMeshletRenderRequestsBuffer(0);
            argsBuffer = system.GetShadowVisibleMeshletIndirectDrawArgsBuffer(0);
            hasDraws = requestsBuffer != null && argsBuffer != null;
            return hasDraws;
        }

        private protected static bool HasMeshletShadowShaderResources(
            VividRPCoreResources resources)
        {
            return resources != null
                && resources.GPUInstanceCullingCompute != null
                && resources.MeshletListBuildCompute != null
                && resources.GPUMeshletCullingCompute != null
                && resources.FixupVisibleMeshletIndirectDrawArgsCompute != null;
        }

        private protected static bool HasRenderableMeshletShadowBatch(
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

        private protected static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            if (material == null)
                return;

            material.SetFloat(s_CullId, (float)GetCullMode(rendererListID));
            CoreUtils.SetKeyword(
                material,
                AlphaTestKeywordName,
                (rendererListID & VividRendererListID.AlphaTest) != 0);
        }

        private protected static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
                return CullMode.Front;

            if ((rendererListID & VividRendererListID.CullOff) != 0)
                return CullMode.Off;

            return CullMode.Back;
        }

        private protected struct MeshletShadowRecordContext
        {
            internal VividGPUDrivenSystem System;
            internal VividPrimitiveDrawSet AggregateDrawSet;
            internal VividPrimitiveDrawSet StaticDrawSet;
            internal VividPrimitiveDrawSet DynamicDrawSet;
            internal bool VirtualTextureReady;
            internal VirtualTextureSpaceBinding VirtualTextureBinding;
        }

    }
}

