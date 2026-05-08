using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class MeshletShadowPass : UnsafePass
    {
        internal const string ShadowCasterShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferShadowCasterPass";

        private const int AtlasGridSize = 2;
        private const int IndirectDrawArgsByteStride = sizeof(uint) * 4;
        private const int RendererListCount = (int)VividRendererListID.Count;

        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CSMShadowAtlas;

        private readonly Material[] m_Materials = new Material[RendererListCount];

        private bool m_IsActive;
        private int m_CascadeCount;
        private int m_CascadeResolution;
        private float m_SlopeScaleDepthBias;
        private ShaderVariablesGlobal m_CameraShaderGlobals;
        private VividShadowData m_ShadowData;

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
            m_CameraShaderGlobals = default;
            m_ShadowData = null;

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
            m_CameraShaderGlobals = CSMShadowPass.ResolveCameraShaderGlobals(frameData, cameraData);
            m_SlopeScaleDepthBias = Mathf.Max(0.0f, additionalLightData.slopeBias);
            m_CascadeCount = Mathf.Min(shadowData.cascadeCount, VividShadowData.MaxCascadeCount);
            m_CascadeResolution = shadowData.cascadeResolution;
            m_ShadowData = shadowData;
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
                // Phase A: cull every cascade with no render target bound. Each cascade owns its own
                // dispatcher so output buffers do not collide and do not overwrite the main-view buffers.
                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    BuildShadowCullingContext(cascadeIndex, out var cullingContext, out var lodContext);
                    system.CullShadowCascade(
                        cascadeIndex,
                        nativeCmd,
                        cullingContext,
                        lodContext,
                        resources.GPUInstanceCullingCompute,
                        resources.MeshletListBuildCompute,
                        resources.GPUMeshletCullingCompute,
                        resources.FixupVisibleMeshletIndirectDrawArgsCompute);
                }

                // Phase B: bind atlas (preserve traditional caster depth) and issue indirect draws per cascade.
                nativeCmd.SetRenderTarget(
                    m_CSMShadowAtlas.innerHandle,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store);
                nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);

                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    var requestsBuffer = system.GetShadowVisibleMeshletRenderRequestsBuffer(cascadeIndex);
                    var argsBuffer = system.GetShadowVisibleMeshletIndirectDrawArgsBuffer(cascadeIndex);
                    if (requestsBuffer == null || argsBuffer == null)
                        continue;

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

                    for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
                    {
                        Material material = m_Materials[rendererListIndex];
                        if (material == null)
                            continue;

                        material.SetBuffer(s_VisibleMeshletRenderRequestsId, requestsBuffer);
                        nativeCmd.DrawProceduralIndirect(
                            Matrix4x4.identity,
                            material,
                            0,
                            MeshTopology.Triangles,
                            argsBuffer,
                            rendererListIndex * IndirectDrawArgsByteStride);
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
            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
            {
                if (m_Materials[materialIndex] == null)
                    continue;

                CoreUtils.Destroy(m_Materials[materialIndex]);
                m_Materials[materialIndex] = null;
            }

            m_IsActive = false;
            m_ShadowData = null;
        }

        private void BuildShadowCullingContext(
            int cascadeIndex,
            out VividGPUCullingContext cullingContext,
            out VividGPULODSelectionContext lodSelectionContext)
        {
            var viewMatrix = m_ShadowData.viewMatrices[cascadeIndex];
            var projMatrix = m_ShadowData.projMatrices[cascadeIndex];
            var invViewMatrix = viewMatrix.inverse;
            var pixelSize = new Vector2(m_CascadeResolution, m_CascadeResolution);
            Vector4 col0 = invViewMatrix.GetColumn(0);
            Vector4 col1 = invViewMatrix.GetColumn(1);
            Vector4 col3 = invViewMatrix.GetColumn(3);

            VividGPUDrivenCullingContextUtility.Build(
                viewMatrix,
                projMatrix,
                cameraPositionWS: new Vector3(col3.x, col3.y, col3.z),
                cameraRightWS: new Vector3(col0.x, col0.y, col0.z),
                cameraUpWS: new Vector3(col1.x, col1.y, col1.z),
                pixelSize: pixelSize,
                isPerspective: false,
                passMask: VividInstancePassMask.Shadows,
                out cullingContext,
                out lodSelectionContext);
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
