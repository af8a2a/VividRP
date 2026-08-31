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
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowAtlas;

        private readonly Material[] m_Materials = new Material[RendererListCount];
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
        private float m_SlopeScaleDepthBias;

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
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_CascadeCount = 0;
            m_MainLightVisibleIndex = -1;
            m_HasUnityShadowCasters = false;
            m_SlopeScaleDepthBias = 0.0f;
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
                            cascadeIndex);
                    }
                }

                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
            }
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
            int cascadeIndex)
        {
            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                VividRendererListID batchKey = (VividRendererListID)rendererListIndex;
                if (!system.IsShadowRendererBatchActive(batchKey))
                    continue;

                Material material = m_Materials[rendererListIndex];
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
                if (m_Materials[materialIndex] == null)
                    continue;

                CoreUtils.Destroy(m_Materials[materialIndex]);
                m_Materials[materialIndex] = null;
            }

            m_IsActive = false;
            m_MeshletRenderingActive = false;
            m_MainLightVisibleIndex = -1;
            m_HasUnityShadowCasters = false;
            m_CascadeCount = 0;
            m_SlopeScaleDepthBias = 0.0f;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = null;
            m_PrimitiveShadowDrawSet = null;
            m_FrameIndex = 0;
            m_ShadowMatrices = default;
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
}
