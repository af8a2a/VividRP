using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class MeshletShadowPass : UnsafePass
    {
        internal const string ShadowCasterShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferShadowCasterPass";

        private const int AtlasGridSize = 2;
        private const int RendererListCount = (int)VividRendererListID.Count;

        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_UnityIndirectDrawArgsId = Shader.PropertyToID("unity_IndirectDrawArgs");
        private static readonly int s_UnityBaseCommandIdId = Shader.PropertyToID("unity_BaseCommandID");
        private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CSMShadowAtlas;

        private readonly Material[] m_Materials = new Material[RendererListCount];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly VividGPUCullingContext[] m_ShadowCullingContexts =
            new VividGPUCullingContext[VividShadowData.MaxCascadeCount];
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        private bool m_IsActive;
        private int m_CascadeCount;
        private int m_CascadeResolution;
        private float m_SlopeScaleDepthBias;
        private Vector4 m_ShadowCasterState;
        private ShaderVariablesGlobal m_CameraShaderGlobals;
        private VividShadowData m_ShadowData;
        private Camera m_LODCamera;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private int m_FrameIndex;

        public MeshletShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(MeshletShadowPass));
        }

        public override void Create()
        {
            Shader shader = Shader.Find(ShadowCasterShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ShadowCasterShaderName}' for {nameof(MeshletShadowPass)}.");
                return;
            }

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{nameof(MeshletShadowPass)}_{(VividRendererListID)rendererListIndex}";
                ConfigureMaterial(material, (VividRendererListID)rendererListIndex);
                m_Materials[rendererListIndex] = material;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_CascadeCount = 0;
            m_CascadeResolution = 0;
            m_SlopeScaleDepthBias = 0.0f;
            m_ShadowCasterState = Vector4.zero;
            m_CameraShaderGlobals = default;
            m_ShadowData = null;
            m_LODCamera = null;
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
            var frameCameraData = frameData.GetOrCreate<VividCameraData>();
            m_FrameIndex = frameCameraData.frameIndex >= 0 ? frameCameraData.frameIndex : Time.frameCount;

            if (m_Materials[0] == null)
                return;

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive || shadowData.cascadeCount <= 0 || shadowData.cascadeResolution <= 0)
                return;

            if (!VividGPUDrivenSystem.HasInstance)
                return;

            var system = VividGPUDrivenSystem.instance;
            if (!system.IsAvailable || system.SceneData == null || system.SceneData.InstanceCount == 0)
                return;

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (!CSMShadowPass.TryResolveVisibleMainDirectionalLight(lightData, out _, out var additionalLightData)
                || additionalLightData == null)
            {
                return;
            }

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            if (cameraData.camera == null)
                return;

            m_CameraShaderGlobals = CSMShadowPass.ResolveCameraShaderGlobals(frameData, cameraData);
            m_SlopeScaleDepthBias = Mathf.Max(0.0f, additionalLightData.slopeBias);
            m_ShadowCasterState = CSMShadowPass.BuildShadowCasterState(lightData.mainVisibleLight);
            m_CascadeCount = Mathf.Min(shadowData.cascadeCount, VividShadowData.MaxCascadeCount);
            m_CascadeResolution = shadowData.cascadeResolution;
            m_ShadowData = shadowData;
            m_LODCamera = cameraData.camera;
            m_IsActive = true;
        }

        public override void Record(UnsafePassContext context)
        {
            if (!m_IsActive || m_ShadowData == null)
                return;

            if (m_CSMShadowAtlas == null || !m_CSMShadowAtlas.innerHandle.IsValid())
                return;

            var system = VividGPUDrivenSystem.instance;
            if (!system.IsAvailable || system.SceneData == null || system.SceneData.InstanceCount == 0)
                return;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null
                || resources.GPUInstanceCullingCompute == null
                || resources.MeshletListBuildCompute == null
                || resources.GPUMeshletCullingCompute == null
                || resources.FixupVisibleMeshletIndirectDrawArgsCompute == null)
            {
                return;
            }

            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
                    system.ConfigureTextureBackendKeyword(m_Materials[materialIndex]);

                bool virtualTextureReady = !system.UsesVirtualTexture
                                           || GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                                               nativeCmd,
                                               m_VirtualTextureFrameData,
                                               m_VirtualTextureSpaceParams,
                                               m_VirtualTextureMipOffsets,
                                               m_VirtualTextureLayerFallbacks,
                                               m_FrameIndex,
                                               feedbackSampleRate: 1,
                                               out _);

                // LOD selection must match the main camera so meshlets do not pop between frames as
                // cascade orientation changes. Frustum culling still uses the cascade view-projection.
                m_LODCamera.BuildLODSelectionContext(
                    out var lodContext);

                // Phase A: build all cascade contexts, then cull them as one two-dimensional workload.
                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    BuildShadowCullingContext(
                        cascadeIndex,
                        out m_ShadowCullingContexts[cascadeIndex]);
                }
                system.CullShadowCascades(
                    nativeCmd,
                    m_ShadowCullingContexts,
                    m_CascadeCount,
                    lodContext,
                    resources.GPUInstanceCullingCompute,
                    resources.MeshletListBuildCompute,
                    resources.GPUMeshletCullingCompute,
                    resources.FixupVisibleMeshletIndirectDrawArgsCompute);

                var requestsBuffer = system.GetShadowVisibleMeshletRenderRequestsBuffer(0);
                var argsBuffer = system.GetShadowVisibleMeshletIndirectDrawArgsBuffer(0);
                if (requestsBuffer == null || argsBuffer == null)
                    return;

                // Phase B: bind atlas (preserve traditional caster depth) and issue indirect draws per cascade.
                nativeCmd.SetRenderTarget(
                    m_CSMShadowAtlas.innerHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store);
                nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);

                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    int offsetX = (cascadeIndex % AtlasGridSize) * m_CascadeResolution;
                    int offsetY = (cascadeIndex / AtlasGridSize) * m_CascadeResolution;
                    var viewMatrix = m_ShadowData.viewMatrices[cascadeIndex];
                    var projMatrix = m_ShadowData.projMatrices[cascadeIndex];
                    var gpuProjMatrix = GL.GetGPUProjectionMatrix(projMatrix, true);
                    var cascadeShaderGlobals = CSMShadowPass.BuildCascadeShaderGlobals(
                        m_CameraShaderGlobals,
                        viewMatrix,
                        projMatrix,
                        gpuProjMatrix);

                    nativeCmd.SetViewport(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.EnableScissorRect(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.SetViewProjectionMatrices(viewMatrix, projMatrix);
                    ConstantBuffer.PushGlobal(nativeCmd, cascadeShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);
                    nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowCasterState);

                    for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
                    {
                        Material material = m_Materials[rendererListIndex];
                        if (material == null)
                            continue;
                        if (!virtualTextureReady
                            && (((VividRendererListID) rendererListIndex & VividRendererListID.AlphaTest) != 0))
                        {
                            continue;
                        }

                        m_DrawProperties.Clear();
                        m_DrawProperties.SetBuffer(s_VisibleMeshletRenderRequestsId, requestsBuffer);
                        m_DrawProperties.SetBuffer(s_UnityIndirectDrawArgsId, argsBuffer);
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

                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
                nativeCmd.DisableScissorRect();
                nativeCmd.SetViewProjectionMatrices(
                    m_CameraShaderGlobals._VividWorldToCamera,
                    m_CameraShaderGlobals._VividCameraProjection);
                ConstantBuffer.PushGlobal(nativeCmd, m_CameraShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);
            }
        }

        public override void Dispose()
        {
            m_VirtualTextureFrameData = null;
            m_FrameIndex = 0;
            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
            {
                if (m_Materials[materialIndex] == null)
                    continue;

                CoreUtils.Destroy(m_Materials[materialIndex]);
                m_Materials[materialIndex] = null;
            }

            m_IsActive = false;
            m_ShadowData = null;
            m_LODCamera = null;
            m_ShadowCasterState = Vector4.zero;
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

            // We pass the cascade matrices for frustum-plane derivation but discard the LOD
            // selection context here. The caller supplies a camera-derived LOD context so meshlet
            // LODs in the shadow atlas stay synchronized with the main view.
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
                cullingContext: out cullingContext,
                lodSelectionContext: out _);
        }

        private static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            if (material == null)
                return;

            material.SetFloat(s_CullId, (float)GetCullMode(rendererListID));
            CoreUtils.SetKeyword(material, s_AlphaTestKeyword, (rendererListID & VividRendererListID.AlphaTest) != 0);
        }

        private static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
                return CullMode.Front;

            if ((rendererListID & VividRendererListID.CullOff) != 0)
                return CullMode.Off;

            return CullMode.Back;
        }
    }
}
